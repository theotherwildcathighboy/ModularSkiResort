// The slope overlay's cell cache and strip runs (T253). Pure and Entities-free: the usings are System, System.Collections.Generic and
// Unity.Mathematics ONLY, so tests/SlopeBatch links it (the classifier's rule).
// One source of truth: every cell value is SkiSlopeClassifier.CellValue, the same per-cell value the rating and the run heat-map use, on
// the same world-anchored lattice (node I at Origin.x + I * Texel, the heightmap texel centers). A tile pads its heights by
// SkiSlopeClassifier.FullWindowMargin, so every shown cell reads the full 20 m window: the value a run covering that cell reads in its
// interior. SlopeBatch proves the parity against ClassifyRing.
// Cache: world-anchored tiles of TileCells x TileCells cells, keyed by tile index, computed nearest-first under a per-call budget, evicted
// least recently used above the cap. Terrain changes: no height-version signal exists (EngineNotes/SkiRunDrawFeedbackSystem.md, Recompute
// policy), so Verify re-samples a round-robin slice of the stored heights and compares them bit-exact; a mismatch recomputes that tile.
// No allocation per call once the pool and the scratch lists have grown. Never throws on bad input, never logs.
// Engine notes: EngineNotes/SkiSlopeOverlaySystem.md

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>One strip run of the overlay: lattice row J (or the middle row of a row group), cells I0..I1, the color key
    /// (DisplayUnits.StripKey) and the edge fade level (1..FadeLevels, FadeLevels = full alpha). JLo..JHi are the lattice rows it covers:
    /// one row group from BuildRuns, several stacked groups after MergeRows (a rectangle, drawn as one strip).</summary>
    public struct SlopeOverlayRun
    {
        public int J, I0, I1, JLo, JHi;
        public byte Key, Fade;
        public float Y;   // mean height of the two end cells (m)
        public float YLo, YHi;   // lowest and highest node height near its cells (Tile.Lo / Hi), the strip's draw box (m)
    }

    /// <summary>Which cells the overlay leaves to someone else (the run heat-map paints the live run polygon). Struct, no allocation.</summary>
    public interface ISlopeCellSkip
    {
        bool Skip(int i, int j);
    }

    public struct NoSlopeCellSkip : ISlopeCellSkip
    {
        public bool Skip(int i, int j) => false;
    }

    public sealed class SkiSlopeOverlayTiles
    {
        public const int TileCells = 32;
        public const int FadeLevels = 4;

        public sealed class Tile
        {
            public int TI, TJ;
            public bool Valid;
            public int LastUsed;
            public int VerifyCursor;
            public float[] Heights;   // (TileCells + 2·Pad)², padded node heights as sampled
            public float[] Grade;     // TileCells², CellValue
            public byte[] Band;       // TileCells², BandOf(Grade)
            public float[] Lo, Hi;    // TileCells², min / max node height within HeightSpan nodes of the cell
            public byte[] KeyNa, KeyEu;   // TileCells², the DISPLAY key per scheme: StripKey, then ModeFilter3 when SmoothDisplay
        }

        /// <summary>Lo / Hi cover the nodes within this many lattice steps of a cell (clamped to Pad): the cell's own square (1), plus the
        /// rows of a row group of stride MaxRowStride drawn from its middle row (1 more), plus one spare.</summary>
        public const int HeightSpan = 3;

        /// <summary>The display key of a cell with no value (outside the known heights, or not a cell of the run).</summary>
        public const byte NoKey = 255;

        /// <summary>T253 look: the shown key is the 3x3 mode of the cell keys (ModeFilter3). It removes single-cell speckle and softens
        /// stair steps. Display only: Grade and Band (the rating's values, TryCell, the parity tests) stay raw.</summary>
        public static bool SmoothDisplay = true;

        private static readonly int[] s_Votes = new int[8];   // main thread only (the overlay and the heat-map both build there)

        /// <summary>3x3 mode filter over display keys. dst[z * w + x] is the key of src cell (x0 + x, z0 + z) (src is sx x sz) that the
        /// most of its 3x3 neighbors (itself included, NoKey cells and cells off src excluded) have. The cell keeps its own key unless
        /// another key has strictly more votes, so ties never move an edge. A NoKey cell stays NoKey and never gains a key. Keys 0..7.
        /// Used by the overlay tiles and the run heat-map, so the two draw the same display rule.</summary>
        public static void ModeFilter3(byte[] src, int sx, int sz, int x0, int z0, int w, int h, byte[] dst)
        {
            int[] votes = s_Votes;
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    int cx = x0 + x, cz = z0 + z;
                    byte own = src[cz * sx + cx];
                    if (own >= 8)
                    {
                        dst[z * w + x] = own;
                        continue;
                    }
                    Array.Clear(votes, 0, 8);
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int qz = cz + dz;
                        if (qz < 0 || qz >= sz)
                            continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int qx = cx + dx;
                            if (qx < 0 || qx >= sx)
                                continue;
                            byte k = src[qz * sx + qx];
                            if (k < 8)
                                votes[k]++;
                        }
                    }
                    byte best = own;
                    int bestVotes = votes[own];
                    for (int k = 0; k < 8; k++)
                        if (votes[k] > bestVotes)
                        {
                            best = (byte)k;
                            bestVotes = votes[k];
                        }
                    dst[z * w + x] = best;
                }
        }

        private readonly Dictionary<long, Tile> m_Tiles = new Dictionary<long, Tile>();
        private readonly Stack<Tile> m_Pool = new Stack<Tile>();
        private readonly List<Tile> m_Scratch = new List<Tile>();
        private readonly List<long> m_Wanted = new List<long>();
        private byte[] m_Mask;
        private int m_Pad = -1, m_Side;
        private float m_Texel;
        private double2 m_Origin;
        private SkiSlopeClassifier.CellParams m_Params;
        private int m_VerifyTile;

        /// <summary>Tiles kept at most (least recently used evicted first).</summary>
        public int Capacity = 96;

        public int Count => m_Tiles.Count;
        public float Texel => m_Texel;
        public double2 Origin => m_Origin;
        public int Pad => m_Pad;

        // Counters (the system's dev tab and the ARMED / summary log lines).
        public int TilesComputed, TilesInvalidated, TilesEvicted, NodesVerified;

        /// <summary>Sets the lattice. A different texel or origin (a new map, a different heightmap) drops every tile. False for an
        /// unusable lattice (the cache then stays empty).</summary>
        public bool SetLattice(double2 origin, float texel)
        {
            if (!(texel > 0f) || texel > 100f || !IsFinite(origin.x) || !IsFinite(origin.y))
                return false;
            if (texel == m_Texel && origin.x == m_Origin.x && origin.y == m_Origin.y && m_Pad >= 0)
                return true;
            Clear();
            m_Texel = texel;
            m_Origin = origin;
            m_Params = SkiSlopeClassifier.CellParams.For(texel);
            // One node more than the full window: the ring of cells around the tile (ModeFilter3's neighbors) also gets a full window.
            m_Pad = SkiSlopeClassifier.FullWindowMargin(texel) + 1;
            m_Side = TileCells + 2 * m_Pad;
            m_Mask = new byte[m_Side * m_Side];
            for (int k = 0; k < m_Mask.Length; k++)
                m_Mask[k] = 1;
            m_Pool.Clear();   // tile buffers are sized by Pad
            return true;
        }

        public void Clear()
        {
            foreach (KeyValuePair<long, Tile> kv in m_Tiles)
            {
                kv.Value.Valid = false;
                m_Pool.Push(kv.Value);
            }
            m_Tiles.Clear();
            m_VerifyTile = 0;
        }

        public static long Key(int ti, int tj) => ((long)ti << 32) ^ (uint)tj;

        public static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        /// <summary>Lattice index of the node nearest to world coordinate x (or z) on axis origin o.</summary>
        public int NodeOf(double w, double o) => (int)math.floor((w - o) / m_Texel + 0.5);

        public Tile Find(int ti, int tj) => m_Tiles.TryGetValue(Key(ti, tj), out Tile t) ? t : null;

        /// <summary>Band and grade of lattice cell (i, j) when its tile is computed and valid.</summary>
        public bool TryCell(int i, int j, out byte band, out float grade)
        {
            int ti = FloorDiv(i, TileCells), tj = FloorDiv(j, TileCells);
            Tile t = Find(ti, tj);
            if (t == null || !t.Valid)
            {
                band = SkiSlopeClassifier.BandOutside;
                grade = 0f;
                return false;
            }
            int q = (j - tj * TileCells) * TileCells + (i - ti * TileCells);
            band = t.Band[q];
            grade = t.Grade[q];
            return true;
        }

        /// <summary>Lists the tiles a disk of `radius` around (cx, cz) touches, nearest first, marks them used at `frame`, and computes
        /// missing or invalid ones until `budgetMs` (wall clock, Stopwatch) is spent. At least one tile per call is computed. Returns the
        /// number of tiles still missing. Evicts above Capacity, never a tile of this disk.</summary>
        public int Update<T>(ref T sampler, double cx, double cz, float radius, int frame, float budgetMs) where T : struct, IHeightSampler
        {
            if (m_Pad < 0 || !(radius > 0f) || !IsFinite(cx) || !IsFinite(cz))
                return 0;
            double tileM = (double)TileCells * m_Texel;
            int ti0 = (int)math.floor((cx - radius - m_Origin.x) / tileM), ti1 = (int)math.floor((cx + radius - m_Origin.x) / tileM);
            int tj0 = (int)math.floor((cz - radius - m_Origin.y) / tileM), tj1 = (int)math.floor((cz + radius - m_Origin.y) / tileM);
            m_Wanted.Clear();
            for (int tj = tj0; tj <= tj1; tj++)
                for (int ti = ti0; ti <= ti1; ti++)
                {
                    // nearest point of the tile rectangle to the center
                    double x0 = m_Origin.x + (ti * (double)TileCells - 0.5) * m_Texel, x1 = x0 + tileM;
                    double z0 = m_Origin.y + (tj * (double)TileCells - 0.5) * m_Texel, z1 = z0 + tileM;
                    double nx = cx < x0 ? x0 : cx > x1 ? x1 : cx, nz = cz < z0 ? z0 : cz > z1 ? z1 : cz;
                    if ((nx - cx) * (nx - cx) + (nz - cz) * (nz - cz) > (double)radius * radius)
                        continue;
                    m_Wanted.Add(Key(ti, tj));
                    Tile t = Find(ti, tj);
                    if (t != null)
                        t.LastUsed = frame;
                }

            // Compute missing tiles, nearest first (selection by distance, the wanted list is small).
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            double budgetTicks = budgetMs * System.Diagnostics.Stopwatch.Frequency / 1000.0;
            int computed = 0, missing = 0;
            while (true)
            {
                int best = -1;
                double bestD = double.MaxValue;
                for (int w = 0; w < m_Wanted.Count; w++)
                {
                    long k = m_Wanted[w];
                    if (m_Tiles.TryGetValue(k, out Tile t) && t.Valid)
                        continue;
                    int ti = (int)(k >> 32), tj = (int)(uint)k;
                    double mx = m_Origin.x + ((ti + 0.5) * TileCells - 0.5) * m_Texel - cx;
                    double mz = m_Origin.y + ((tj + 0.5) * TileCells - 0.5) * m_Texel - cz;
                    double d = mx * mx + mz * mz;
                    if (d < bestD)
                    {
                        bestD = d;
                        best = w;
                    }
                }
                if (best < 0)
                    break;
                if (computed > 0 && System.Diagnostics.Stopwatch.GetTimestamp() - t0 >= budgetTicks)
                {
                    for (int w = 0; w < m_Wanted.Count; w++)
                        if (!m_Tiles.TryGetValue(m_Wanted[w], out Tile t) || !t.Valid)
                            missing++;
                    break;
                }
                long key = m_Wanted[best];
                Tile tile = Acquire((int)(key >> 32), (int)(uint)key, frame);
                Compute(tile, ref sampler);
                computed++;
            }
            Evict(frame);
            return missing;
        }

        private Tile Acquire(int ti, int tj, int frame)
        {
            long key = Key(ti, tj);
            if (!m_Tiles.TryGetValue(key, out Tile t))
            {
                t = m_Pool.Count > 0 ? m_Pool.Pop() : new Tile();
                int side = m_Side;
                if (t.Heights == null || t.Heights.Length != side * side)
                    t.Heights = new float[side * side];
                if (t.Grade == null)
                {
                    t.Grade = new float[TileCells * TileCells];
                    t.Band = new byte[TileCells * TileCells];
                }
                if (t.Lo == null)
                {
                    t.Lo = new float[TileCells * TileCells];
                    t.Hi = new float[TileCells * TileCells];
                    t.KeyNa = new byte[TileCells * TileCells];
                    t.KeyEu = new byte[TileCells * TileCells];
                }
                m_Tiles.Add(key, t);
            }
            t.TI = ti;
            t.TJ = tj;
            t.LastUsed = frame;
            t.Valid = false;
            return t;
        }

        /// <summary>Samples the padded heights of a tile and fills its cells with CellValue. A non-finite height leaves the tile invalid
        /// (retried next call).</summary>
        public void Compute<T>(Tile t, ref T sampler) where T : struct, IHeightSampler
        {
            int side = m_Side, pad = m_Pad;
            int baseI = t.TI * TileCells - pad, baseJ = t.TJ * TileCells - pad;
            float[] h = t.Heights;
            for (int j = 0; j < side; j++)
            {
                float wz = (float)(m_Origin.y + (baseJ + j) * (double)m_Texel);
                for (int i = 0; i < side; i++)
                {
                    float wx = (float)(m_Origin.x + (baseI + i) * (double)m_Texel);
                    float v = sampler.Sample(new float3(wx, 0f, wz));
                    if (!IsFinite(v))
                    {
                        t.Valid = false;
                        return;
                    }
                    h[j * side + i] = v;
                }
            }
            // The tile's cells plus a one-cell ring (r = -1 .. TileCells): the ring feeds ModeFilter3 only, so the filter needs no
            // neighbor tile and a tile never goes stale when its neighbor is computed later.
            const int ring = TileCells + 2;
            if (m_RingNa == null)
            {
                m_RingNa = new byte[ring * ring];
                m_RingEu = new byte[ring * ring];
            }
            for (int j = -1; j <= TileCells; j++)
                for (int i = -1; i <= TileCells; i++)
                {
                    float o = SkiSlopeClassifier.CellValue(h, m_Mask, side, side, i + pad, j + pad, in m_Params, out bool _,
                        out float _, out float _, out float _);
                    byte band = (byte)SkiSlopeClassifier.BandOf(o);
                    bool shown = band <= (byte)SlopeBand.DoubleBlack;
                    int rq = (j + 1) * ring + i + 1;
                    m_RingNa[rq] = shown ? DisplayUnits.StripKey(band, o, false) : NoKey;
                    m_RingEu[rq] = shown ? DisplayUnits.StripKey(band, o, true) : NoKey;
                    if (i < 0 || j < 0 || i >= TileCells || j >= TileCells)
                        continue;
                    int q = j * TileCells + i;
                    t.Grade[q] = o;
                    t.Band[q] = band;
                }
            if (SmoothDisplay)
            {
                ModeFilter3(m_RingNa, ring, ring, 1, 1, TileCells, TileCells, t.KeyNa);
                ModeFilter3(m_RingEu, ring, ring, 1, 1, TileCells, TileCells, t.KeyEu);
            }
            else
                for (int j = 0; j < TileCells; j++)
                    for (int i = 0; i < TileCells; i++)
                    {
                        t.KeyNa[j * TileCells + i] = m_RingNa[(j + 1) * ring + i + 1];
                        t.KeyEu[j * TileCells + i] = m_RingEu[(j + 1) * ring + i + 1];
                    }
            HeightRange(t);
            t.Valid = true;
            t.VerifyCursor = 0;
            TilesComputed++;
        }

        private float[] m_RowLo, m_RowHi;
        private byte[] m_RingNa, m_RingEu;

        /// <summary>Tile.Lo / Hi: the min / max node height within HeightSpan nodes of each cell (separable: rows, then columns).</summary>
        private void HeightRange(Tile t)
        {
            int side = m_Side, pad = m_Pad, r = HeightSpan < pad ? HeightSpan : pad;
            if (m_RowLo == null || m_RowLo.Length != side * TileCells)
            {
                m_RowLo = new float[side * TileCells];
                m_RowHi = new float[side * TileCells];
            }
            float[] h = t.Heights;
            for (int j = 0; j < side; j++)
                for (int i = 0; i < TileCells; i++)
                {
                    float lo = float.MaxValue, hi = float.MinValue;
                    int row = j * side + i + pad;
                    for (int d = -r; d <= r; d++)
                    {
                        float v = h[row + d];
                        lo = v < lo ? v : lo;
                        hi = v > hi ? v : hi;
                    }
                    m_RowLo[j * TileCells + i] = lo;
                    m_RowHi[j * TileCells + i] = hi;
                }
            for (int j = 0; j < TileCells; j++)
                for (int i = 0; i < TileCells; i++)
                {
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (int d = -r; d <= r; d++)
                    {
                        int q = (j + pad + d) * TileCells + i;
                        lo = m_RowLo[q] < lo ? m_RowLo[q] : lo;
                        hi = m_RowHi[q] > hi ? m_RowHi[q] : hi;
                    }
                    t.Lo[j * TileCells + i] = lo;
                    t.Hi[j * TileCells + i] = hi;
                }
        }

        private void Evict(int frame)
        {
            if (m_Tiles.Count <= Capacity)
                return;
            m_Scratch.Clear();
            foreach (KeyValuePair<long, Tile> kv in m_Tiles)
                if (kv.Value.LastUsed != frame)
                    m_Scratch.Add(kv.Value);
            // Oldest first: repeated min selection (the overflow is a few tiles per call).
            while (m_Tiles.Count > Capacity && m_Scratch.Count > 0)
            {
                int oldest = 0;
                for (int k = 1; k < m_Scratch.Count; k++)
                    if (m_Scratch[k].LastUsed < m_Scratch[oldest].LastUsed)
                        oldest = k;
                Tile t = m_Scratch[oldest];
                m_Scratch[oldest] = m_Scratch[m_Scratch.Count - 1];
                m_Scratch.RemoveAt(m_Scratch.Count - 1);
                m_Tiles.Remove(Key(t.TI, t.TJ));
                t.Valid = false;
                m_Pool.Push(t);
                TilesEvicted++;
            }
        }

        /// <summary>Re-samples up to `nodes` stored heights of the tiles used at `frame` (round-robin over tiles and nodes) and compares
        /// them bit-exact. A mismatch invalidates the tile (Update recomputes it). Returns the number of tiles invalidated.</summary>
        public int Verify<T>(ref T sampler, int frame, int nodes) where T : struct, IHeightSampler
        {
            if (m_Pad < 0 || m_Tiles.Count == 0 || nodes <= 0)
                return 0;
            m_Scratch.Clear();
            foreach (KeyValuePair<long, Tile> kv in m_Tiles)
                if (kv.Value.Valid && kv.Value.LastUsed == frame)
                    m_Scratch.Add(kv.Value);
            if (m_Scratch.Count == 0)
                return 0;
            int side = m_Side, total = side * side, invalidated = 0, left = nodes, guard = m_Scratch.Count;
            while (left > 0 && guard-- > 0)
            {
                if (m_VerifyTile >= m_Scratch.Count)
                    m_VerifyTile = 0;
                Tile t = m_Scratch[m_VerifyTile];
                int baseI = t.TI * TileCells - m_Pad, baseJ = t.TJ * TileCells - m_Pad;
                while (left > 0 && t.VerifyCursor < total)
                {
                    int q = t.VerifyCursor++;
                    int j = q / side, i = q - j * side;
                    float wx = (float)(m_Origin.x + (baseI + i) * (double)m_Texel);
                    float wz = (float)(m_Origin.y + (baseJ + j) * (double)m_Texel);
                    float v = sampler.Sample(new float3(wx, 0f, wz));
                    left--;
                    NodesVerified++;
                    if (math.asint(v) != math.asint(t.Heights[q]))
                    {
                        t.Valid = false;
                        invalidated++;
                        TilesInvalidated++;
                        break;
                    }
                }
                if (!t.Valid || t.VerifyCursor >= total)
                {
                    t.VerifyCursor = 0;
                    m_VerifyTile++;
                }
            }
            return invalidated;
        }

        /// <summary>Strip runs of the disk of `radius` around (cx, cz) into `runs` (cleared first): per lattice row (or row group of
        /// `stride` rows, drawn from its middle row), the same-key, same-fade cells of computed tiles, the cells `skip` names excluded.
        /// Fade: the outer `fadeM` of the disk in FadeLevels steps (level from the cell center's distance). `eu` selects the European
        /// key (DisplayUnits.StripKey). Stops and returns false when more than `cap` runs are needed (runs then holds the first `cap`).</summary>
        public bool BuildRuns<TSkip>(double cx, double cz, float radius, float fadeM, bool eu, int stride, int cap, ref TSkip skip,
                                     List<SlopeOverlayRun> runs) where TSkip : struct, ISlopeCellSkip
        {
            runs.Clear();
            if (m_Pad < 0 || !(radius > 0f) || !IsFinite(cx) || !IsFinite(cz) || stride < 1)
                return true;
            double t = m_Texel, r2 = (double)radius * radius;
            int jLo = (int)math.ceil((cz - radius - m_Origin.y) / t), jHi = (int)math.floor((cz + radius - m_Origin.y) / t);
            for (int g = jLo; g <= jHi; g += stride)
            {
                int gHi = g + stride - 1 > jHi ? jHi : g + stride - 1;
                int j = (g + gHi) / 2;
                double wz = m_Origin.y + j * t, dz = wz - cz;
                double rem = r2 - dz * dz;
                if (rem < 0.0)
                    continue;
                double half = math.sqrt(rem);
                int iLo = (int)math.ceil((cx - half - m_Origin.x) / t), iHi = (int)math.floor((cx + half - m_Origin.x) / t);
                bool open = false;
                SlopeOverlayRun cur = default;
                int tj = FloorDiv(j, TileCells), lj = j - tj * TileCells;
                int lastTi = int.MinValue;
                Tile tile = null;
                for (int i = iLo; i <= iHi + 1; i++)
                {
                    bool have = false;
                    byte key = 0, fade = 0;
                    float y = 0f, yLo = 0f, yHi = 0f;
                    if (i <= iHi)
                    {
                        int ti = FloorDiv(i, TileCells);
                        if (ti != lastTi)
                        {
                            lastTi = ti;
                            tile = Find(ti, tj);
                            if (tile != null && !tile.Valid)
                                tile = null;
                        }
                        if (tile != null)
                        {
                            int li = i - ti * TileCells, q = lj * TileCells + li;
                            byte shownKey = eu ? tile.KeyEu[q] : tile.KeyNa[q];
                            if (shownKey != NoKey && !skip.Skip(i, j))
                            {
                                have = true;
                                key = shownKey;
                                double dx = m_Origin.x + i * t - cx;
                                double edge = radius - math.sqrt(dx * dx + dz * dz);
                                fade = FadeLevel(edge, fadeM);
                                y = tile.Heights[(lj + m_Pad) * m_Side + li + m_Pad];
                                yLo = tile.Lo[q];
                                yHi = tile.Hi[q];
                            }
                        }
                    }
                    if (open && (!have || key != cur.Key || fade != cur.Fade))
                    {
                        if (runs.Count >= cap)
                            return false;
                        runs.Add(cur);
                        open = false;
                    }
                    if (have)
                    {
                        if (!open)
                        {
                            cur = new SlopeOverlayRun { J = j, I0 = i, I1 = i, JLo = g, JHi = gHi, Key = key, Fade = fade, Y = y, YLo = yLo, YHi = yHi };
                            open = true;
                        }
                        else
                        {
                            cur.I1 = i;
                            cur.Y = 0.5f * (cur.Y + y);
                            cur.YLo = yLo < cur.YLo ? yLo : cur.YLo;
                            cur.YHi = yHi > cur.YHi ? yHi : cur.YHi;
                        }
                    }
                }
            }
            return true;
        }

        private readonly List<int> m_PrevOpen = new List<int>(), m_CurOpen = new List<int>();

        /// <summary>Stacks BuildRuns' row runs into rectangles (rects is cleared first). A run joins the rectangle right above it when both
        /// have the same I0, I1, key and fade and its rows follow on (JLo = the rectangle's JHi + 1). Every cell stays in exactly one
        /// rectangle with its own key and fade, so the picture is the same with fewer strips (9-19 % fewer on the SlopeBatch terrains). A
        /// rectangle's YLo / YHi are the min / max of its rows' (its draw box). The rows must come in BuildRuns order (row groups up, I up).</summary>
        public void MergeRows(List<SlopeOverlayRun> rows, List<SlopeOverlayRun> rects)
        {
            rects.Clear();
            List<int> prev = m_PrevOpen, cur = m_CurOpen;
            prev.Clear();
            int k = 0;
            while (k < rows.Count)
            {
                int gLo = rows[k].JLo;
                cur.Clear();
                int p = 0;
                for (; k < rows.Count && rows[k].JLo == gLo; k++)
                {
                    SlopeOverlayRun r = rows[k];
                    while (p < prev.Count && rects[prev[p]].I0 < r.I0)
                        p++;
                    if (p < prev.Count)
                    {
                        SlopeOverlayRun q = rects[prev[p]];
                        if (q.I0 == r.I0 && q.I1 == r.I1 && q.Key == r.Key && q.Fade == r.Fade && q.JHi + 1 == r.JLo)
                        {
                            q.JHi = r.JHi;
                            q.J = (q.JLo + q.JHi) / 2;
                            q.YLo = r.YLo < q.YLo ? r.YLo : q.YLo;
                            q.YHi = r.YHi > q.YHi ? r.YHi : q.YHi;
                            rects[prev[p]] = q;
                            cur.Add(prev[p]);
                            p++;
                            continue;
                        }
                    }
                    rects.Add(r);
                    cur.Add(rects.Count - 1);
                }
                List<int> swap = prev;
                prev = cur;
                cur = swap;
            }
        }

        /// <summary>1..FadeLevels from the distance to the disk edge (m): FadeLevels from fadeM inward, fewer toward the edge.</summary>
        public static byte FadeLevel(double edgeM, float fadeM)
        {
            if (!(fadeM > 0f) || edgeM >= fadeM)
                return FadeLevels;
            int l = (int)math.floor(edgeM / fadeM * FadeLevels) + 1;
            return (byte)(l < 1 ? 1 : l > FadeLevels ? FadeLevels : l);
        }


        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
