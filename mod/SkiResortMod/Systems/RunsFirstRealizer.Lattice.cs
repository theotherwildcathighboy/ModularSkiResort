// Runs-first realizer: the lattice (spec section 3). One grid on the snapshot's texels over the region's box. Layers: the blocked bits
// (all types, pistes only, tracks only), the 20 m window grade (the v1 field kernel), and per type the distance to the nearest accepted
// outline with that outline's index (a chamfer that carries the owner). Pure: System, System.Collections.Generic, Unity.Mathematics.
// Spec: docs/research/quickruns-v2/port/REALIZER-SPEC.md section 3. Engine notes: EngineNotes/RunsFirstRealizer.md

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class RunsFirstRealizer
    {
        // Cell flags. BlockedAll = FBlockRegion | FBlockHard (spec 3: outside the region or within 2 m of its edge, unowned tiles with
        // their clearance, wet texels). FBlockPiste: other Surface areas and existing pistes. FBlockTrack: existing cat tracks
        private const byte FRegion = 1, FBlockRegion = 2, FBlockHard = 4, FBlockPiste = 8, FBlockTrack = 16, FWet = 32;
        private const byte FNearEdge = 64;   // within NearEdgeM of the region's edge or the map margin: the free tests check exactly (iteration 2, item 5)
        private const byte FBlockAll = FBlockRegion | FBlockHard;

        /// <summary>The lattice cap. The spec names the snapshot's own 2^21. The big set (25-29 km2) needs more, and the realizer has no
        /// area limit of its own, so the cap is 2^23 cells (about 170 MB of layers, never reached under 30 km2).</summary>
        public const int MaxCells = 1 << 23;
        public const float LatticeMarginM = PlanGeometry.LatticeMarginM;
        public const int OffMapMarginTexels = SkiSlopeClassifier.Dilation + 2;
        public const int TileCoreCells = 80;
        private const float Sqrt2 = 1.41421356f;

        private int m_LI0, m_LJ0, m_Nx, m_Nz, m_N;
        private float m_Texel;
        private double2 m_Origin;
        private byte[] m_Flags;
        private float[] m_Grade, m_DPiste, m_DTrack;
        private int[] m_OwnPiste, m_OwnTrack;
        private int m_RI0, m_RI1, m_RJ0, m_RJ1, m_RegionCells;
        private readonly List<float> m_Xs = new List<float>();

        private long LayerBytes => m_N == 0 ? 0L : (long)m_N * (1 + 4 + 4 + 4 + 4 + 4);

        private bool Cell(float2 p, out int c)
        {
            double fx = (p.x - m_Origin.x) / m_Texel, fz = (p.y - m_Origin.y) / m_Texel;
            long i = (long)Math.Floor(fx + 0.5) - m_LI0, j = (long)Math.Floor(fz + 0.5) - m_LJ0;
            if (i < 0 || j < 0 || i >= m_Nx || j >= m_Nz || double.IsNaN(fx) || double.IsNaN(fz))
            {
                c = -1;
                return false;
            }
            c = (int)(j * m_Nx + i);
            return true;
        }

        private float2 CellCenter(int i, int j) =>
            new float2((float)(m_Origin.x + (m_LI0 + i) * (double)m_Texel), (float)(m_Origin.y + (m_LJ0 + j) * (double)m_Texel));

        /// <summary>Sets up the lattice over the region's box. Returns Running, or the refusal (TooLarge, BadInput).</summary>
        private AutoPlanStatus BuildLattice()
        {
            HeightSnapshot snap = m_Snap;
            m_Texel = snap.TexelM;
            m_Origin = snap.Origin;
            PlanGeometry.BBox(m_Region, out float2 lo, out float2 hi);
            int margin = (int)math.ceil(LatticeMarginM / m_Texel) + 1;
            long i0 = (long)Math.Floor((lo.x - m_Origin.x) / m_Texel) - margin, i1 = (long)Math.Ceiling((hi.x - m_Origin.x) / m_Texel) + margin;
            long j0 = (long)Math.Floor((lo.y - m_Origin.y) / m_Texel) - margin, j1 = (long)Math.Ceiling((hi.y - m_Origin.y) / m_Texel) + margin;
            long nx = i1 - i0 + 1, nz = j1 - j0 + 1;
            if (nx * nz > MaxCells || Math.Abs(i0) > 1L << 24 || Math.Abs(j0) > 1L << 24)
                return AutoPlanStatus.TooLarge;
            if (!snap.CoversClamped(i0 - 1, j0 - 1, i1 + 1, j1 + 1))
                return AutoPlanStatus.BadInput;
            m_LI0 = (int)i0;
            m_LJ0 = (int)j0;
            m_Nx = (int)nx;
            m_Nz = (int)nz;
            m_N = m_Nx * m_Nz;
            m_Flags = new byte[m_N];
            m_Grade = new float[m_N];
            m_DPiste = new float[m_N];
            m_DTrack = new float[m_N];
            m_OwnPiste = new int[m_N];
            m_OwnTrack = new int[m_N];
            for (int c = 0; c < m_N; c++)
            {
                m_Grade[c] = float.NaN;
                m_DPiste[c] = m_DTrack[c] = float.PositiveInfinity;
                m_OwnPiste[c] = m_OwnTrack[c] = -1;
            }
            // The region (cell center inside, even-odd: a keyhole ring reads as outline minus hole), the map margin, water
            ScanFill(m_Region, FRegion);
            m_RI0 = m_RJ0 = int.MaxValue;
            m_RI1 = m_RJ1 = int.MinValue;
            m_RegionCells = 0;
            int resX = snap.ResX, resZ = snap.ResZ;
            for (int j = 0; j < m_Nz; j++)
            {
                long l = (long)m_LJ0 + j;
                for (int i = 0; i < m_Nx; i++)
                {
                    int c = j * m_Nx + i;
                    long k = (long)m_LI0 + i;
                    byte f = m_Flags[c];
                    if ((f & FRegion) == 0)
                        f |= FBlockRegion;
                    else
                    {
                        m_RegionCells++;
                        m_RI0 = Math.Min(m_RI0, i);
                        m_RI1 = Math.Max(m_RI1, i);
                        m_RJ0 = Math.Min(m_RJ0, j);
                        m_RJ1 = Math.Max(m_RJ1, j);
                    }
                    if (k < OffMapMarginTexels || l < OffMapMarginTexels || k > resX - 1 - OffMapMarginTexels || l > resZ - 1 - OffMapMarginTexels)
                        f |= FBlockRegion;
                    int nearT = OffMapMarginTexels + (int)math.ceil(NearEdgeM / m_Texel);
                    if (k < nearT || l < nearT || k > resX - 1 - nearT || l > resZ - 1 - nearT)
                        f |= FNearEdge;
                    if (snap.IsWet(k, l))
                        f |= (byte)(FWet | FBlockHard);
                    m_Flags[c] = f;
                }
            }
            if (m_RegionCells == 0)
                return AutoPlanStatus.TooSmall;
            // Within EdgeBlockM of the region's edge
            for (int e = 0, n = m_Region.Length; e < n; e++)
                MarkNear(m_Region[e], m_Region[(e + 1) % n], EdgeBlockM, FBlockRegion);
            for (int e = 0, n = m_Region.Length; e < n; e++)
                MarkNear(m_Region[e], m_Region[(e + 1) % n], NearEdgeM + m_Texel, FNearEdge);
            return AutoPlanStatus.Running;
        }

        /// <summary>The exact region test of a point on a cell flagged FNearEdge: inside the region, clear of its edges by clear meters,
        /// and inside the map margin by the same meters (the ring check RegionOk tests the vertices exactly, the cells are 3.5 m).</summary>
        private bool InRegion(float2 q, float clear)
        {
            if (!GradeRaster.Inside(m_Region, q) || PlanGeometry.PointRingDistance(m_Region, q) < clear)
                return false;
            HeightSnapshot snap = m_Snap;
            float hx = (q.x + snap.Offset.x) * snap.Scale.x, hz = (q.y + snap.Offset.z) * snap.Scale.z, m = OffMapMarginTexels + clear / m_Texel;
            return hx >= m && hz >= m && hx <= snap.ResX - 1 - m && hz <= snap.ResZ - 1 - m;
        }

        /// <summary>Obstacles (spec 3): unowned tiles block every type (their 0.5 m clearance), other Surface areas block pistes (2 m),
        /// existing pistes block pistes (the piste gap), existing cat tracks block tracks (their cells) and enter the track distance as
        /// accepted tracks.</summary>
        private void MarkObstacles()
        {
            List<AutoObstacle> obs = m_In.Obstacles;
            if (obs == null)
                return;
            for (int o = 0; o < obs.Count; o++)
            {
                AutoObstacle ob = obs[o];
                if (ob == null || ob.Ring == null || ob.Ring.Length < 2 || !RingFinite(ob.Ring))
                    continue;
                byte bit;
                float clear;
                switch (ob.Kind)
                {
                    case AutoObstacleKind.Unowned: bit = FBlockHard; clear = PlanGeometry.UnownedClearanceM; break;
                    case AutoObstacleKind.CatTrack: bit = FBlockTrack; clear = 0f; break;
                    case AutoObstacleKind.ExistingRun: bit = FBlockPiste; clear = PlanGeometry.ClearanceOf(ob.Kind, m_Density); break;
                    default: bit = FBlockPiste; clear = PlanGeometry.SurfaceClearanceM; break;
                }
                if (ob.Ring.Length >= 3)
                    ScanFill(ob.Ring, bit);
                int n = ob.Ring.Length;
                for (int e = 0; e < n; e++)
                {
                    if (n == 2 && e == 1)
                        break;
                    MarkNear(ob.Ring[e], ob.Ring[(e + 1) % n], Math.Max(clear, 0.5f * m_Texel), bit);
                }
                if (ob.Kind == AutoObstacleKind.CatTrack && ob.Ring.Length >= 3)
                {
                    var a = new Area { Track = true, Existing = true, Elem = -1, Ring = ob.Ring, Obstacle = o };
                    PlanGeometry.BBox(a.Ring, out a.Lo, out a.Hi);
                    m_T.Add(a);
                    SeedArea(m_DTrack, m_OwnTrack, a.Ring, m_T.Count - 1, TrackReachM);
                }
            }
        }

        private static bool RingFinite(float2[] r)
        {
            for (int i = 0; i < r.Length; i++)
                if (!math.all(math.isfinite(r[i])))
                    return false;
            return true;
        }

        /// <summary>Sets bit on every cell whose center lies inside the ring (even-odd, row scanlines).</summary>
        private void ScanFill(float2[] ring, byte bit)
        {
            int n = ring.Length;
            PlanGeometry.BBox(ring, out float2 lo, out float2 hi);
            int j0 = Math.Max(0, (int)Math.Floor((lo.y - m_Origin.y) / m_Texel) - m_LJ0 - 1);
            int j1 = Math.Min(m_Nz - 1, (int)Math.Ceiling((hi.y - m_Origin.y) / m_Texel) - m_LJ0 + 1);
            for (int j = j0; j <= j1; j++)
            {
                float z = (float)(m_Origin.y + (m_LJ0 + j) * (double)m_Texel);
                m_Xs.Clear();
                for (int e = 0, k = n - 1; e < n; k = e++)
                {
                    float2 a = ring[k], b = ring[e];
                    if ((a.y > z) != (b.y > z))
                        m_Xs.Add(a.x + (z - a.y) / (b.y - a.y) * (b.x - a.x));
                }
                if (m_Xs.Count < 2)
                    continue;
                m_Xs.Sort();
                for (int q = 0; q + 1 < m_Xs.Count; q += 2)
                {
                    int ia = Math.Max(0, (int)Math.Ceiling((m_Xs[q] - m_Origin.x) / m_Texel) - m_LI0);
                    int ib = Math.Min(m_Nx - 1, (int)Math.Floor((m_Xs[q + 1] - m_Origin.x) / m_Texel) - m_LI0);
                    for (int i = ia; i <= ib; i++)
                    {
                        float x = (float)(m_Origin.x + (m_LI0 + i) * (double)m_Texel);
                        if (x > m_Xs[q] && x < m_Xs[q + 1])
                            m_Flags[j * m_Nx + i] |= bit;
                    }
                }
            }
        }

        /// <summary>Sets bit on every cell whose center lies within dist of segment ab.</summary>
        private void MarkNear(float2 a, float2 b, float dist, byte bit)
        {
            float2 lo = math.min(a, b) - dist, hi = math.max(a, b) + dist;
            int i0 = Math.Max(0, (int)Math.Floor((lo.x - m_Origin.x) / m_Texel) - m_LI0);
            int i1 = Math.Min(m_Nx - 1, (int)Math.Ceiling((hi.x - m_Origin.x) / m_Texel) - m_LI0);
            int j0 = Math.Max(0, (int)Math.Floor((lo.y - m_Origin.y) / m_Texel) - m_LJ0);
            int j1 = Math.Min(m_Nz - 1, (int)Math.Ceiling((hi.y - m_Origin.y) / m_Texel) - m_LJ0);
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                    if (GradePath.SegDistance(CellCenter(i, j), a, b, out float _) < dist)
                        m_Flags[j * m_Nx + i] |= bit;
        }

        // ---------------------------------------------------------------- the grade layer (the v1 field kernel, copied)

        /// <summary>The 20 m window grade of every cell of the region's box: tiles of TileCoreCells plus an apron of one window, each one
        /// SkiSlopeClassifier Plan + FillHeights + Classify (SkiRunAutoPlanner.RectField, copied). NaN where the classifier has no
        /// value.</summary>
        private void BuildGrade()
        {
            var grid = new SlopeGrid();
            var res = new SlopeResult();
            var sampler = new SnapshotSampler(m_Snap);
            int apron = (int)math.ceil(SkiSlopeClassifier.WindowLength / m_Texel);
            for (int tj = m_RJ0; tj <= m_RJ1; tj += TileCoreCells)
                for (int ti = m_RI0; ti <= m_RI1; ti += TileCoreCells)
                {
                    int i1 = Math.Min(m_RI1, ti + TileCoreCells - 1), j1 = Math.Min(m_RJ1, tj + TileCoreCells - 1);
                    bool any = false;
                    for (int j = tj; j <= j1 && !any; j++)
                        for (int i = ti; i <= i1; i++)
                            if ((m_Flags[j * m_Nx + i] & FRegion) != 0)
                            {
                                any = true;
                                break;
                            }
                    if (!any)
                        continue;
                    if (!RectField(ref sampler, m_Origin, m_Texel, m_LI0 + ti - apron, m_LJ0 + tj - apron, m_LI0 + i1 + apron, m_LJ0 + j1 + apron, grid, res))
                        continue;
                    for (int j = tj; j <= j1; j++)
                        for (int i = ti; i <= i1; i++)
                        {
                            int q = (m_LJ0 + j - grid.J0) * grid.Nx + (m_LI0 + i - grid.I0);
                            m_Grade[j * m_Nx + i] = grid.Mask[q] != 0 ? res.CellGrade[q] : float.NaN;
                        }
                }
        }

        /// <summary>Plan + FillHeights + Classify over the cells [k0, k1] x [l0, l1] (texel indices). Copied from SkiRunAutoPlanner.RectField.</summary>
        private static bool RectField<T>(ref T sampler, double2 origin, float texel, int k0, int l0, int k1, int l1, SlopeGrid grid,
            SlopeResult result) where T : struct, IHeightSampler
        {
            float x0 = (float)(origin.x + (k0 - 0.5) * texel), x1 = (float)(origin.x + (k1 + 0.5) * texel);
            float z0 = (float)(origin.y + (l0 - 0.5) * texel), z1 = (float)(origin.y + (l1 + 0.5) * texel);
            var pts = new[] { new float3(x0, 0f, z0), new float3(x1, 0f, z0), new float3(x1, 0f, z1), new float3(x0, 0f, z1) };
            grid.DiagonalRule = false;
            SlopeStatus ps = SkiSlopeClassifier.Plan(pts, 4, origin, texel, grid);
            if (ps != SlopeStatus.Ok || grid.PolylineMode)
                return false;
            SkiRunRater.FillHeights(ref sampler, grid, null, out int _, out int _);
            SlopeStatus cs = SkiSlopeClassifier.Classify(grid, result);
            return cs == SlopeStatus.Ok || cs == SlopeStatus.ShortRun;
        }

        // ---------------------------------------------------------------- distance layers with owners

        /// <summary>Enters an accepted outline into a distance layer: its inside cells and the cells its edges pass get -0.5 texel and the
        /// owner index, then a two-pass chamfer (owner carried) over the ring's box plus reachM.</summary>
        private void SeedArea(float[] d, int[] own, float2[] ring, int idx, float reachM)
        {
            float seed = -0.5f * m_Texel;
            PlanGeometry.BBox(ring, out float2 lo, out float2 hi);
            int i0 = Math.Max(0, (int)Math.Floor((lo.x - m_Origin.x) / m_Texel) - m_LI0 - 1);
            int i1 = Math.Min(m_Nx - 1, (int)Math.Ceiling((hi.x - m_Origin.x) / m_Texel) - m_LI0 + 1);
            int j0 = Math.Max(0, (int)Math.Floor((lo.y - m_Origin.y) / m_Texel) - m_LJ0 - 1);
            int j1 = Math.Min(m_Nz - 1, (int)Math.Ceiling((hi.y - m_Origin.y) / m_Texel) - m_LJ0 + 1);
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    int c = j * m_Nx + i;
                    if (seed < d[c] && GradeRaster.Inside(ring, CellCenter(i, j)))
                    {
                        d[c] = seed;
                        own[c] = idx;
                    }
                }
            int n = ring.Length;
            for (int e = 0; e < n; e++)
            {
                float2 a = ring[e], b = ring[(e + 1) % n];
                int steps = Math.Max(1, (int)math.ceil(math.distance(a, b) / (0.5f * m_Texel)));
                for (int s = 0; s <= steps; s++)
                    if (Cell(math.lerp(a, b, s / (float)steps), out int c) && seed < d[c])
                    {
                        d[c] = seed;
                        own[c] = idx;
                    }
            }
            int r = (int)math.ceil(reachM / m_Texel) + 2;
            i0 = Math.Max(0, i0 - r);
            i1 = Math.Min(m_Nx - 1, i1 + r);
            j0 = Math.Max(0, j0 - r);
            j1 = Math.Min(m_Nz - 1, j1 + r);
            Chamfer(d, own, m_Nx, i0, i1, j0, j1, true, m_Texel);
            Chamfer(d, own, m_Nx, i0, i1, j0, j1, false, m_Texel);
        }

        /// <summary>One chamfer pass (3x3 mask, texel and texel x sqrt 2) over the window, the owner carried with the distance. Copied from
        /// SkiRunAutoPlanner.Chamfer, the owner index added (spec section 9).</summary>
        private static void Chamfer(float[] d, int[] own, int nx, int i0, int i1, int j0, int j1, bool forward, float texel)
        {
            float a = texel, b = texel * Sqrt2;
            if (forward)
            {
                for (int j = j0; j <= j1; j++)
                {
                    int row = j * nx;
                    for (int i = i0; i <= i1; i++)
                    {
                        int c = row + i;
                        float v = d[c];
                        int o = own[c];
                        if (i > i0 && d[c - 1] + a < v)
                        {
                            v = d[c - 1] + a;
                            o = own[c - 1];
                        }
                        if (j > j0)
                        {
                            int up = c - nx;
                            if (d[up] + a < v)
                            {
                                v = d[up] + a;
                                o = own[up];
                            }
                            if (i > i0 && d[up - 1] + b < v)
                            {
                                v = d[up - 1] + b;
                                o = own[up - 1];
                            }
                            if (i < i1 && d[up + 1] + b < v)
                            {
                                v = d[up + 1] + b;
                                o = own[up + 1];
                            }
                        }
                        d[c] = v;
                        own[c] = o;
                    }
                }
            }
            else
            {
                for (int j = j1; j >= j0; j--)
                {
                    int row = j * nx;
                    for (int i = i1; i >= i0; i--)
                    {
                        int c = row + i;
                        float v = d[c];
                        int o = own[c];
                        if (i < i1 && d[c + 1] + a < v)
                        {
                            v = d[c + 1] + a;
                            o = own[c + 1];
                        }
                        if (j < j1)
                        {
                            int dn = c + nx;
                            if (d[dn] + a < v)
                            {
                                v = d[dn] + a;
                                o = own[dn];
                            }
                            if (i < i1 && d[dn + 1] + b < v)
                            {
                                v = d[dn + 1] + b;
                                o = own[dn + 1];
                            }
                            if (i > i0 && d[dn - 1] + b < v)
                            {
                                v = d[dn - 1] + b;
                                o = own[dn - 1];
                            }
                        }
                        d[c] = v;
                        own[c] = o;
                    }
                }
            }
        }

        // ---------------------------------------------------------------- region statistics (the refusal ladder's inputs)

        /// <summary>Open share, relief, mean grade, steep and cliff shares over the region's cells (open = not BlockedAll or
        /// BlockedPiste), and the bottom band's top (plan section 5.1).</summary>
        private void RegionStats()
        {
            int open = 0, usable = 0, steep = 0, cliff = 0;
            double sum = 0.0;
            float hMin = float.PositiveInfinity, hMax = float.NegativeInfinity;
            for (int j = m_RJ0; j <= m_RJ1; j++)
                for (int i = m_RI0; i <= m_RI1; i++)
                {
                    int c = j * m_Nx + i;
                    byte f = m_Flags[c];
                    if ((f & FRegion) == 0 || (f & (FBlockAll | FBlockPiste)) != 0)
                        continue;
                    float o = m_Grade[c];
                    if (float.IsNaN(o))
                        continue;
                    open++;
                    sum += Math.Min(o, PlanGeometry.MeanGradeCap);
                    m_Snap.TryRaw((long)m_LI0 + i, (long)m_LJ0 + j, out ushort raw);
                    float h = m_Snap.ToWorld(raw);
                    hMin = Math.Min(hMin, h);
                    hMax = Math.Max(hMax, h);
                    if (o >= CliffGrade)
                        cliff++;
                    else
                    {
                        usable++;
                        if (o >= SkiSlopeClassifier.GradeGreen)
                            steep++;
                    }
                }
            AutoPlanResult r = m_Res;
            r.OpenShare = open / (float)Math.Max(1, m_RegionCells);
            r.UsableShare = usable / (float)Math.Max(1, m_RegionCells);
            r.CliffShare = open > 0 ? cliff / (float)open : 0f;
            r.SteepShare = usable > 0 ? steep / (float)usable : 0f;
            r.VerticalM = open > 0 ? hMax - hMin : 0f;
            r.MeanGrade = open > 0 ? (float)(sum / open) : 0f;
            m_HMin = open > 0 ? hMin : 0f;
            float band = Math.Max(BottomMinM, Math.Min(BottomMaxM, BottomShare * r.VerticalM));
            m_BottomTop = m_HMin + band;
            m_Rep.BottomTop = m_BottomTop;
        }
    }
}
