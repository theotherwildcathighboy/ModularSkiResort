// Plan geometry: the pure static helpers and constants of the ski run planner that the tool layer, the tests and any planner share.
// They used to live inside SkiRunAutoPlanner (v1 / v2). Every body moved here unchanged (port round R0-b), and port round R2-d deleted
// the v1 / v2 planner with its forwarders (one stays: SkiRunAutoPlanner.SnapshotRect, for the frozen scorecard probes). Every reader
// calls PlanGeometry directly. The result and input types are in PlanContract.cs.
// Pure on purpose: no state, no logging, no throwing. Usings are System, System.Collections.Generic and Unity.Mathematics ONLY
// (plus the project's own pure grade helpers), so the file also links into the net8 test host.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public static class PlanGeometry
    {
        // ---------------------------------------------------------------- constants (sources: design.md 3.2, FEASIBILITY 4/9)
        public const float V1MaxAreaM2 = 9.0e6f, ReadyMaxAreaM2 = 9.0e6f;      // owner ruling 9 km2 (DECISIONS 419). The code is tested to it
        public const float DefaultMinAreaM2 = 40000f, DefaultMinVerticalM = 50f;
        public const int MaxLatticeCells = 1 << 21;                              // scan rectangle cap (5 x 5 km at 3.5 m, seed keys pack 21 bits)
        public const float LatticeMarginM = 40f;                                 // 20 m windows + Dilation + bilinear reach
        public const float MeanGradeCap = 2.0f;                                  // one cell counts at most 200 % in the region mean (a sheer face must not drown the rest)
        public static readonly float[] TargetWidthM = { 40f, 35f, 30f, 20f };
        public const float SparseGapM = 30f, NormalGapM = 15f, DenseGapM = 10f;
        public const float MergeGapM = 2f;                                       // abutting is legal (ValidationHelpers.cs:575)
        public const float SurfaceClearanceM = MergeGapM, UnownedClearanceM = 0.5f;
        public const int StageCount = 16;    // the v1 probe slots (AutoPlanResult.StageMs). The Resort-only stages have none (ResortPlan.StageMs)
        public const int SelectPhases = 5;   // stage 13's m_SelPhase as probe slots: pick, A* (reroute), build, rate, chamfer (4 and 5)

        // Station lot half-diagonals by lift type (fixed grip, detachable, gondola, tram): lots 16x24, 16x32, 24x32, 48x24 m ([C1] table
        // 3b, lot cell 8 m, DC/Game.Prefabs/BuildingInitializeSystem.cs:1266-1272). Private: read through StationHalfDiagonalOf.
        private static readonly float[] s_StationHalfDiagonal = { 14.4f, 17.9f, 20.0f, 26.8f };
        public const int StationTypes = 4;

        // ---------------------------------------------------------------- seed, snapshot window, station lots, score band

        /// <summary>Default seed: FNV-1a over the region's node bits, starting at the lexicographically smallest node and walking
        /// counterclockwise, so the start vertex and the drawing direction never change the layout. Shuffle adds a counter.</summary>
        public static uint SeedOf(float2[] region, uint shuffle)
        {
            uint h = 2166136261u;
            if (region == null || region.Length == 0)
                return (h ^ shuffle) | 1u;
            int n = region.Length, start = 0;
            for (int i = 1; i < n; i++)
                if (region[i].x < region[start].x || (region[i].x == region[start].x && region[i].y < region[start].y))
                    start = i;
            double s = 0.0;
            for (int i = 0, k = n - 1; i < n; k = i++)
                s += (double)region[k].x * region[i].y - (double)region[i].x * region[k].y;
            int dir = s >= 0.0 ? 1 : -1;
            for (int c = 0; c < n; c++)
            {
                float2 p = region[((start + dir * c) % n + n) % n];
                h = Fnv(h, FloatBits(p.x));
                h = Fnv(h, FloatBits(p.y));
            }
            h = Fnv(h, shuffle);
            return h | 1u;
        }

        /// <summary>The texel rectangle the tool must copy into the snapshot for this region (inclusive bounds, clamped to the map).
        /// False for a degenerate region.</summary>
        public static bool SnapshotRect(float2[] region, int resX, int resZ, float3 scale, float3 offset,
            out int x0, out int z0, out int width, out int depth)
        {
            x0 = z0 = width = depth = 0;
            if (!BBox(region, out float2 lo, out float2 hi) || !(scale.x > 0f))
                return false;
            float texel = 1f / scale.x;
            int pad = (int)math.ceil(LatticeMarginM / texel) + 3;
            long k0 = (long)math.floor((lo.x + offset.x) * scale.x) - pad, k1 = (long)math.ceil((hi.x + offset.x) * scale.x) + pad;
            long l0 = (long)math.floor((lo.y + offset.z) * scale.z) - pad, l1 = (long)math.ceil((hi.y + offset.z) * scale.z) + pad;
            k0 = Math.Max(0L, Math.Min(resX - 1, k0));
            k1 = Math.Max(0L, Math.Min(resX - 1, k1));
            l0 = Math.Max(0L, Math.Min(resZ - 1, l0));
            l1 = Math.Max(0L, Math.Min(resZ - 1, l1));
            x0 = (int)k0;
            z0 = (int)l0;
            width = (int)(k1 - k0 + 1);
            depth = (int)(l1 - l0 + 1);
            return true;
        }

        /// <summary>The station lot half diagonal of a lift type (SkiCatalog index, clamped to 0..StationTypes - 1).</summary>
        public static float StationHalfDiagonalOf(int type) => s_StationHalfDiagonal[Math.Max(0, Math.Min(StationTypes - 1, type))];

        /// <summary>A fresh copy of the half diagonals by type for callers that index an array (the overlay). The planner reads
        /// StationHalfDiagonalOf.</summary>
        public static float[] StationHalfDiagonalM => (float[])s_StationHalfDiagonal.Clone();

        /// <summary>The rated band a pod class is skied as (SkiResortScore bands): Beginner and below green, Intermediate blue, Advanced
        /// black, Expert and Cliff double black.</summary>
        public static byte ScoreBandOf(TerrainBand c) =>
            c <= TerrainBand.Beginner ? SkiResortScore.BandGreen : c == TerrainBand.Intermediate ? SkiResortScore.BandBlue
            : c == TerrainBand.Advanced ? SkiResortScore.BandBlack : SkiResortScore.BandDoubleBlack;

        // ---------------------------------------------------------------- obstacle clearance

        /// <summary>Clearance an obstacle keeps from a generated run: an existing run a tree island (the density's gap), any other
        /// surface only enough to never overlap (abutting is legal, ValidationHelpers.cs:575), an unowned tile a hair.</summary>
        public static float ClearanceOf(AutoObstacleKind kind, AutoDensity density)
        {
            switch (kind)
            {
                case AutoObstacleKind.ExistingRun: return GapOf(density, TargetWidthM[2]);
                case AutoObstacleKind.Unowned: return UnownedClearanceM;
                case AutoObstacleKind.CatTrack: return 0f;
                default: return SurfaceClearanceM;
            }
        }

        /// <summary>Gap between two runs (design 3.2): Sparse 30 m, Normal max(15 m, half the run width), Dense 10 m.</summary>
        public static float GapOf(AutoDensity density, float widthM)
        {
            switch (density)
            {
                case AutoDensity.Sparse: return SparseGapM;
                case AutoDensity.Dense: return DenseGapM;
                default: return Math.Max(NormalGapM, 0.5f * widthM);
            }
        }

        /// <summary>True when the ring keeps every hard obstacle's clearance (ClearanceOf), containment included. Public: the host
        /// re-checks every output with it, and the tool re-checks on Enter after re-collecting obstacles (FEASIBILITY 4 row 12).</summary>
        public static bool ObstaclesClear(float2[] ring, List<AutoObstacle> obstacles, AutoDensity density)
        {
            if (obstacles == null)
                return true;
            BBox(ring, out float2 lo, out float2 hi);
            for (int o = 0; o < obstacles.Count; o++)
            {
                AutoObstacle ob = obstacles[o];
                if (ob == null || ob.Ring == null || ob.Ring.Length < 2 || ob.Kind == AutoObstacleKind.CatTrack)
                    continue;
                float clear = ClearanceOf(ob.Kind, density);
                BBox(ob.Ring, out float2 olo, out float2 ohi);
                if (math.any(ohi < lo - clear) || math.any(olo > hi + clear))
                    continue;
                if (ob.Ring.Length >= 3)
                {
                    if (RingDistance(ring, ob.Ring, clear, out _, out _, out _, out _) < clear)
                        return false;
                }
                else
                {
                    if (GradeRaster.Inside(ring, ob.Ring[0]) || GradeRaster.Inside(ring, ob.Ring[1]))
                        return false;
                    for (int i = 0; i < ring.Length; i++)
                        if (GradeRibbon.SegSegDistance(ring[i], ring[(i + 1) % ring.Length], ob.Ring[0], ob.Ring[1]) < clear)
                            return false;
                }
            }
            return true;
        }

        // ---------------------------------------------------------------- exact geometry

        public static bool BBox(float2[] p, out float2 lo, out float2 hi)
        {
            lo = new float2(float.MaxValue, float.MaxValue);
            hi = new float2(float.MinValue, float.MinValue);
            if (p == null || p.Length == 0)
                return false;
            for (int i = 0; i < p.Length; i++)
            {
                lo = math.min(lo, p[i]);
                hi = math.max(hi, p[i]);
            }
            return true;
        }

        public static double PolygonArea(float2[] p)
        {
            if (p == null || p.Length < 3)
                return 0.0;
            double s = 0.0;
            for (int i = 0, k = p.Length - 1; i < p.Length; k = i++)
                s += (double)p[k].x * p[i].y - (double)p[i].x * p[k].y;
            return Math.Abs(s) * 0.5;
        }

        /// <summary>Closest points of segments ab and cd (GradeRibbon.SegCross for a crossing, else the best of the four endpoint
        /// projections, GradePath.SegDistance).</summary>
        public static float SegClosest(float2 a, float2 b, float2 c, float2 d, out float2 p, out float2 q)
        {
            if (GradeRibbon.SegCross(a, b, c, d, out float2 x))
            {
                p = q = x;
                return 0f;
            }
            float best = GradePath.SegDistance(a, c, d, out float u);
            p = a;
            q = math.lerp(c, d, u);
            float t = GradePath.SegDistance(b, c, d, out u);
            if (t < best)
            {
                best = t;
                p = b;
                q = math.lerp(c, d, u);
            }
            t = GradePath.SegDistance(c, a, b, out u);
            if (t < best)
            {
                best = t;
                p = math.lerp(a, b, u);
                q = c;
            }
            t = GradePath.SegDistance(d, a, b, out u);
            if (t < best)
            {
                best = t;
                p = math.lerp(a, b, u);
                q = d;
            }
            return best;
        }

        /// <summary>Minimum distance between two closed rings (0 when they cross or one holds a vertex of the other). It also gives the
        /// closest points and the edges they lie on. The function skips edge pairs whose boxes are farther apart than
        /// <paramref name="cutoff"/>. So the result is exact up to cutoff and "at least cutoff" beyond.</summary>
        public static float RingDistance(float2[] a, float2[] b, float cutoff, out float2 pa, out float2 pb, out int ea, out int eb)
        {
            float best = EdgeDistance(a, b, cutoff, out pa, out pb, out ea, out eb);
            if (best > 0f && (GradeRaster.Inside(b, a[0]) || GradeRaster.Inside(a, b[0])))
            {
                pa = a[0];
                pb = b[0];
                return 0f;
            }
            return best;
        }

        /// <summary>Minimum distance between the edges of two closed rings. Containment is NOT counted: a ring inside the region is
        /// as far from it as from its nearest edge. The function skips segment pairs farther apart than cutoff on either axis.</summary>
        public static float EdgeDistance(float2[] a, float2[] b, float cutoff, out float2 pa, out float2 pb, out int ea, out int eb)
        {
            pa = pb = default;
            ea = eb = -1;
            float best = float.PositiveInfinity;
            int na = a.Length, nb = b.Length;
            for (int i = 0; i < na; i++)
            {
                float2 a0 = a[i], a1 = a[(i + 1) % na];
                float2 alo = math.min(a0, a1) - cutoff, ahi = math.max(a0, a1) + cutoff;
                for (int k = 0; k < nb; k++)
                {
                    float2 b0 = b[k], b1 = b[(k + 1) % nb];
                    if (math.any(math.max(b0, b1) < alo) || math.any(math.min(b0, b1) > ahi))
                        continue;
                    float dd = SegClosest(a0, a1, b0, b1, out float2 p, out float2 q);
                    if (dd < best)
                    {
                        best = dd;
                        pa = p;
                        pb = q;
                        ea = i;
                        eb = k;
                    }
                }
            }
            return best;
        }

        /// <summary>Minimum distance from a point to a closed ring's edges.</summary>
        public static float PointRingDistance(float2[] ring, float2 p)
        {
            float best = float.PositiveInfinity;
            for (int i = 0, n = ring.Length; i < n; i++)
                best = Math.Min(best, GradePath.SegDistance(p, ring[i], ring[(i + 1) % n], out float _));
            return best;
        }

        // ---------------------------------------------------------------- hashing (FNV-1a)

        internal static uint FloatBits(float f) => math.asuint(f);

        internal static uint Fnv(uint h, uint v)
        {
            for (int b = 0; b < 4; b++)
            {
                h ^= (v >> (8 * b)) & 0xFFu;
                h *= 16777619u;
            }
            return h;
        }
    }
}
