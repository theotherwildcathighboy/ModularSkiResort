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
    /// (DisplayUnits.StripKey) and the edge fade level (1..FadeLevels, FadeLevels = full alpha).</summary>
    public struct SlopeOverlayRun
    {
        public int J, I0, I1;
        public byte Key, Fade;
        public float Y;   // mean height of the two end cells (m)
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
            m_Pad = SkiSlopeClassifier.FullWindowMargin(texel);
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
            for (int j = 0; j < TileCells; j++)
                for (int i = 0; i < TileCells; i++)
                {
                    float o = SkiSlopeClassifier.CellValue(h, m_Mask, side, side, i + pad, j + pad, in m_Params, out bool _,
                        out float _, out float _, out float _);
                    int q = j * TileCells + i;
                    t.Grade[q] = o;
                    t.Band[q] = (byte)SkiSlopeClassifier.BandOf(o);
                }
            t.Valid = true;
            t.VerifyCursor = 0;
            TilesComputed++;
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
                    float y = 0f;
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
                            byte band = tile.Band[q];
                            if (band <= (byte)SlopeBand.DoubleBlack && !skip.Skip(i, j))
                            {
                                have = true;
                                key = DisplayUnits.StripKey(band, tile.Grade[q], eu);
                                double dx = m_Origin.x + i * t - cx;
                                double edge = radius - math.sqrt(dx * dx + dz * dz);
                                fade = FadeLevel(edge, fadeM);
                                y = tile.Heights[(lj + m_Pad) * m_Side + li + m_Pad];
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
                            cur = new SlopeOverlayRun { J = j, I0 = i, I1 = i, Key = key, Fade = fade, Y = y };
                            open = true;
                        }
                        else
                        {
                            cur.I1 = i;
                            cur.Y = 0.5f * (cur.Y + y);
                        }
                    }
                }
            }
            return true;
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
