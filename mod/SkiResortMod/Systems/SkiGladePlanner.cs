// The pure, deterministic tree planner behind the Glade tool. Entities-free on purpose: usings are System,
// System.Collections.Generic and Unity.Mathematics ONLY.
// The same file is compiled into the net48 mod AND linked into the net8 headless test host tests/GladeBatch.
// Re-derived from the vanilla systems. The code copies nothing from ParkManager.
// Contract: the output is a pure function of (ring, triangles, seed, params, obstacles, existing, thinnable, exclusion rings, fill mask).
// The planner has no static mutable state, never logs and never throws. Degenerate input gives a GladeStatus and empty output lists.
// Layers run in a fixed order (sanitize, thin, glade, belt). Each has its own salted random stream with a FIXED number of draws
// per attempt and per accepted tree. SkiGladeCompose relies on the resulting prefix property (MaxPlant m1 is a bit-exact prefix of m2).
// An optional GladeMask (the Quick runs tree filler: run holes + margin, tree line) narrows Thin and Glade. A null mask changes nothing, bit for bit.
// Its optional edge band (T157) thins Glade darts toward the polygon's outline (no draws). Band off = the pre-band mask, bit for bit.
// Engine notes: EngineNotes/SkiGladePlanner.md

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    [Flags]
    public enum GladeMode
    {
        None = 0,
        Thin = 1,
        Glade = 2,
        Belt = 4,
    }

    public enum GladeStatus
    {
        Ok,
        TooFewVertices,
        ZeroArea,
        NonFinite,
        BadTriangles,
        Clamped,
    }

    public struct GladeParams
    {
        public GladeMode Mode;
        public float ThinFraction;        // 0..1, threshold on a position hash (idempotent, monotone)
        public float GladeDensity;        // trees per m² inside the polygon (target INCLUDING survivors)
        public float BeltDensity;         // trees per m² inside the belt band
        public float MinSpacing;          // m, between any two trees (planted, survivors, members)
        public float InnerMargin;         // m, glade trees stay this far inside the boundary
        public float BeltGap, BeltDepth;  // m, belt band = [gap, depth] outside the boundary
        public float BeltCoverage;        // 0..1 share of the perimeter that gets trees
        public float BeltNoiseWavelength; // m, period of the coverage noise along the perimeter
        public int SpeciesCount;          // pool size. Species index 0 is dominant
        public float DominantShare;       // 0..1
        public float4 AgeWeights;         // Sapling, Young, Mature, Elderly (sum > 0)
        public int MaxPlant, AttemptsPerTree;
    }

    public struct GladeObstacle
    {
        public float3 Center;
        public float Radius;
    }

    /// <summary>Ground height at an xz point (m), for the fill mask's tree line. An adapter seam: the game side wraps its own height
    /// sampler. Must be a pure function of (x, z) for one Fingerprint. NaN means unknown: the planner reads it as above the tree line
    /// (no tree).</summary>
    public abstract class GladeHeightField
    {
        public abstract float HeightAt(double x, double z);

        /// <summary>Changes whenever any height changes (hashed into SkiGladeCompose.PlanKey).</summary>
        public abstract ulong Fingerprint { get; }
    }

    /// <summary>Fill mask (the Quick runs tree filler). It narrows the polygon for the Thin and Glade layers to the cells that are outside
    /// every hole by HoleMargin. With a Ground, the cells must also be at or below TreeLine. The Belt layer ignores it. A null mask gives
    /// the glade tool's behavior, bit for bit (GladeBatch pin). Engine notes: EngineNotes/SkiGladePlanner.md, FILL MASK.</summary>
    public sealed class GladeMask
    {
        /// <summary>Rings (run outlines) no tree stands inside or within HoleMargin of. The planner skips null entries, rings under 3 vertices
        /// and rings with a non-finite vertex.</summary>
        public float3[][] Holes;
        public float HoleMargin;
        /// <summary>Null = no tree line.</summary>
        public GladeHeightField Ground;
        /// <summary>Highest ground height (m) a tree may stand on. NaN or +Inf = no tree line.</summary>
        public float TreeLine = float.PositiveInfinity;
        /// <summary>Cell (m) of the masked-area quadrature. 0 or less = DefaultAreaCellM.</summary>
        public float AreaCellM;

        /// <summary>Edge band (T157): a Glade dart at distance d inside the polygon's own outline stands with probability
        /// smoothstep(0, depth, d). depth runs from EdgeBandMinM to EdgeBandMaxM by a world-anchored 2-D value noise with lattice cell
        /// EdgeNoiseWavelengthM. The hole margins stay hard. EdgeBandMaxM 0 or less (the default) = no band: the mask is the pre-band mask,
        /// bit for bit. MaskedArea then weighs each allowed cell by that probability, so the Glade target counts what the band lets stand.</summary>
        public float EdgeBandMinM, EdgeBandMaxM;
        /// <summary>Lattice cell (m) of the band's depth noise. 0 or less = DefaultEdgeNoiseWavelengthM.</summary>
        public float EdgeNoiseWavelengthM;

        public const float DefaultAreaCellM = 2.5f;
        public const float DefaultEdgeNoiseWavelengthM = 60f;

        /// <summary>True when the band is on (EdgeBandMaxM finite and above 0).</summary>
        public bool HasEdgeBand => EdgeBandMaxM > 0f && math.isfinite(EdgeBandMaxM);
    }

    /// <summary>Cooperative cancel for a plan / compose that runs on a worker thread (the Quick runs tree filler). The owner calls Request.
    /// The planner polls between dart-throw batches and quadrature rows, and stops early with GladePlan.Canceled set. A canceled plan
    /// is incomplete: callers drop it (never cached, never emitted). With null everywhere, the code path is the uncanceled one, unchanged.</summary>
    public sealed class GladeCancel
    {
        private volatile bool m_Requested;
        public bool Requested => m_Requested;
        public void Request() => m_Requested = true;
    }

    public struct PlantedTree
    {
        public float3 Position;
        public float Rotation;
        public int Species;
        public float Age;
        public byte Layer; // 0 glade, 1 belt
    }

    public sealed class GladePlan
    {
        public readonly List<PlantedTree> Planted = new List<PlantedTree>();
        public readonly List<int> Thinned = new List<int>(); // indices into `existing`
        public GladeStatus Status;
        public float Area, BeltArea;
        public int TargetGlade, TargetBelt, Attempts;
        public bool Saturated;

        /// <summary>The glade / belt target BEFORE the MaxPlant clamp (the `raw` of the target formulas, floored at 0, capped at
        /// int.MaxValue). 0 for a layer not planned.</summary>
        public int RawTargetGlade, RawTargetBelt;

        /// <summary>A MaxPlant budget bound a target. Status == Clamped stays as before. This flag is the explicit form of the same event.</summary>
        public bool Clamped;

        /// <summary>The fill mask's share of Area (m², quadrature on a GladeMask.AreaCellM grid). It equals Area without a mask. The Glade
        /// target reads it.</summary>
        public float MaskedArea;

        /// <summary>A GladeCancel stopped this plan early: incomplete, never to be used.</summary>
        public bool Canceled;

        public void Clear()
        {
            MaskedArea = 0f;
            Canceled = false;
            Planted.Clear();
            Thinned.Clear();
            Status = GladeStatus.Ok;
            Area = 0f;
            BeltArea = 0f;
            TargetGlade = 0;
            TargetBelt = 0;
            Attempts = 0;
            Saturated = false;
            RawTargetGlade = 0;
            RawTargetBelt = 0;
            Clamped = false;
        }
    }

    public static class SkiGladePlanner
    {
        // Vanilla ToolUtils.GetRandomAge constants (ToolUtils.cs:158-165, Mature at :162-163). Both 0.425f and the vanilla value map to
        // TreeState.Adult in ObjectUtils.InitializeTreeState :438-442
        public const float AgeSapling = 0f, AgeYoung = 0.17500001f, AgeMature = 0.425f, AgeElderly = 0.77500004f;

        // Stream salts: changing any of them changes every plan
        private const uint SaltThin = 0x7A1B3C5Du;
        private const uint SaltGlade = 0x9E3779B9u;
        private const uint SaltBelt = 0x85EBCA6Bu;
        private const uint SaltNoise = 0xC2B2AE35u;
        // Edge band (T157): the depth noise lattice (world-anchored, no plan seed: the probe and the stream must weigh the same area) and the
        // per-dart gate hash (with the plan seed)
        private const uint SaltEdgeNoise = 0x27D4EB2Fu;
        private const uint SaltEdgeGate = 0x165667B1u;

        // Hard ceilings so absurd parameters can never hang the main thread
        private const long MaxAttemptsPerLayer = 200000;
        private const int MaxNoiseCells = 1000000;
        private const double DuplicateEpsSq = 1e-6;   // 1 mm in xz
        private const double MinArea = 1.0;           // m²
        private const double TriangleAreaTolerance = 0.05;

        /// <summary>32-bit run seed. FNV-1a (word-wise, offset 2166136261, prime 16777619) over
        /// (int)round(10·x), (int)round(10·y), (int)round(10·z). This is exactly the 0.1 m quantization
        /// of SkiRunPropSystem.HashSeed (SkiRunPropSystem.cs:251-264). The hash then takes the salt as a
        /// fourth word, then h ^= h >> 15. Non-finite components quantize to 0. It never returns 0.</summary>
        public static uint Seed32(float3 firstNode, uint salt)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)Quantize10(firstNode.x)) * 16777619u;
                h = (h ^ (uint)Quantize10(firstNode.y)) * 16777619u;
                h = (h ^ (uint)Quantize10(firstNode.z)) * 16777619u;
                h = (h ^ salt) * 16777619u;
                h ^= h >> 15;
                return h != 0u ? h : 0x6E624EB7u;
            }
        }

        /// <summary>Thinning predicate: hash of int2(floor(xz·8)) (12.5 cm cells, so ±0 and
        /// last-ulp float noise cannot flip it) with the seed. Selected iff
        /// (h &amp; 0xFFFF) &lt; fraction·65536. Monotone in fraction. Fraction ≤ 0 → never,
        /// ≥ 1 → always. Non-finite or out-of-int-range positions are never selected.
        /// h = Fmix32(math.hash(...)). math.hash(uint4) alone is a LINEAR combination of its
        /// lanes (no avalanche). GladeBatch measured this: cells 1 m apart agreed 83 % of the
        /// time, a stripe lattice. So the finalizer is load-bearing for spatial randomness.</summary>
        public static bool ThinSelect(float3 pos, uint seed, float fraction)
        {
            if (!(fraction > 0f))
                return false; // also NaN
            if (!math.isfinite(pos.x) || !math.isfinite(pos.z))
                return false;
            float qxf = math.floor(pos.x * 8f);
            float qzf = math.floor(pos.z * 8f);
            if (math.abs(qxf) > 2.0e9f || math.abs(qzf) > 2.0e9f)
                return false;
            if (fraction >= 1f)
                return true;
            unchecked
            {
                uint h = Fmix32(math.hash(new uint4(seed, SaltThin, (uint)(int)qxf, (uint)(int)qzf)));
                return (float)(h & 0xFFFFu) < fraction * 65536f;
            }
        }

        /// <summary>The full 32-bit hash that ThinSelect thresholds (same 12.5 cm cells, seed/salt words and Fmix32 finalizer).
        /// For a finite, in-range position: ThinSelect(pos, seed, f) == ((ThinHash(pos, seed) &amp; 0xFFFF) &lt; f*65536). Non-finite or
        /// out-of-int-range positions (the ones ThinSelect never selects) return 0. y is ignored. SkiGladeCompose ranks by it.</summary>
        public static uint ThinHash(float3 pos, uint seed)
        {
            if (!math.isfinite(pos.x) || !math.isfinite(pos.z))
                return 0u;
            float qxf = math.floor(pos.x * 8f);
            float qzf = math.floor(pos.z * 8f);
            if (math.abs(qxf) > 2.0e9f || math.abs(qzf) > 2.0e9f)
                return 0u;
            unchecked
            {
                return Fmix32(math.hash(new uint4(seed, SaltThin, (uint)(int)qxf, (uint)(int)qzf)));
            }
        }

        public static GladeStatus Plan(float3[] ring, int3[] trianglesOrNull, uint seed, in GladeParams p,
                                       GladeObstacle[] obstaclesOrNull, float3[] existingOrNull, bool[] thinnableOrNull,
                                       float3[][] exclusionRingsOrNull, GladePlan output)
        {
            return Plan(ring, trianglesOrNull, seed, in p, obstaclesOrNull, existingOrNull, thinnableOrNull, exclusionRingsOrNull, null,
                output);
        }

        /// <summary>Plan with an optional fill mask (null = the overload above). The mask only adds rejection tests and changes the
        /// Glade target's area. It consumes no random draws, so the prefix property holds with a mask too.</summary>
        public static GladeStatus Plan(float3[] ring, int3[] trianglesOrNull, uint seed, in GladeParams p,
                                       GladeObstacle[] obstaclesOrNull, float3[] existingOrNull, bool[] thinnableOrNull,
                                       float3[][] exclusionRingsOrNull, GladeMask maskOrNull, GladePlan output)
        {
            return Plan(ring, trianglesOrNull, seed, in p, obstaclesOrNull, existingOrNull, thinnableOrNull, exclusionRingsOrNull, maskOrNull,
                null, output);
        }

        /// <summary>Plan with a cancel flag (null = the overload above). The planner polls it every 1024 attempts and every quadrature row.
        /// A request ends the plan early with output.Canceled set.</summary>
        public static GladeStatus Plan(float3[] ring, int3[] trianglesOrNull, uint seed, in GladeParams p,
                                       GladeObstacle[] obstaclesOrNull, float3[] existingOrNull, bool[] thinnableOrNull,
                                       float3[][] exclusionRingsOrNull, GladeMask maskOrNull, GladeCancel cancelOrNull, GladePlan output)
        {
            if (output == null)
                output = new GladePlan(); // never throw. The caller gets nothing back
            output.Clear();

            Polygon poly;
            GladeStatus geo = BuildPolygon(ring, trianglesOrNull, out poly);
            if (geo != GladeStatus.Ok)
            {
                output.Status = geo;
                return geo;
            }
            output.Area = (float)poly.Area;
            output.MaskedArea = output.Area;
            MaskSet mask = maskOrNull != null ? new MaskSet(maskOrNull, poly) : null;

            Knobs k = Knobs.From(in p);
            GladeStatus status = GladeStatus.Ok;

            int existingCount = existingOrNull != null ? existingOrNull.Length : 0;
            bool[] thinned = existingCount > 0 ? new bool[existingCount] : null;
            if ((k.Mode & GladeMode.Thin) != 0 && k.ThinFraction > 0f)
            {
                for (int i = 0; i < existingCount; i++)
                {
                    float3 e = existingOrNull[i];
                    if (!IsFinite(e))
                        continue;
                    bool thinnable = thinnableOrNull == null || (i < thinnableOrNull.Length && thinnableOrNull[i]);
                    if (!thinnable)
                        continue;
                    if (!poly.Contains(new double2(e.x, e.z)))
                        continue;
                    if (mask != null && !mask.Allows(new double2(e.x, e.z)))
                        continue;
                    if (ThinSelect(e, seed, k.ThinFraction))
                    {
                        thinned[i] = true;
                        output.Thinned.Add(i); // ascending by construction
                    }
                }
            }

            bool wantGlade = (k.Mode & GladeMode.Glade) != 0;
            bool wantBelt = (k.Mode & GladeMode.Belt) != 0;
            if (!wantGlade && !wantBelt)
            {
                output.Status = status;
                return status;
            }

            SpacingGrid grid = new SpacingGrid(k.MinSpacing);
            int survivorsInside = 0;
            for (int i = 0; i < existingCount; i++)
            {
                if (thinned[i])
                    continue;
                float3 e = existingOrNull[i];
                if (!IsFinite(e))
                    continue;
                double2 q = new double2(e.x, e.z);
                grid.Add(q);
                if (wantGlade && poly.Contains(q) && (mask == null || mask.Allows(q)))
                    survivorsInside++;
            }

            ObstacleSet obstacles = new ObstacleSet(obstaclesOrNull, 0.5 * k.MinSpacing);
            ExclusionSet exclusions = new ExclusionSet(exclusionRingsOrNull);
            AgeTable ages = new AgeTable(k.AgeWeights);
            int budget = k.MaxPlant;

            if (wantGlade)
            {
                double gladeArea = poly.Area;
                if (mask != null)
                {
                    gladeArea = mask.Area(poly, cancelOrNull);
                    output.MaskedArea = (float)gladeArea;
                    if (cancelOrNull != null && cancelOrNull.Requested)
                    {
                        output.Canceled = true;
                        output.Status = status;
                        return status;
                    }
                }
                double raw = math.floor(k.GladeDensity * gladeArea + 0.5) - survivorsInside;
                output.RawTargetGlade = RawToInt(raw);
                int target = ClampTarget(raw, budget, ref status, ref output.Clamped);
                output.TargetGlade = target;
                if (target > 0 && poly.TriangleTotal > 0.0)
                {
                    Unity.Mathematics.Random rng = new Unity.Mathematics.Random(math.hash(new uint2(seed, SaltGlade)) | 1u);
                    long maxAttempts = math.min((long)target * k.AttemptsPerTree, MaxAttemptsPerLayer);
                    long attempts = 0;
                    int placed = 0;
                    while (placed < target && attempts < maxAttempts)
                    {
                        if (cancelOrNull != null && (attempts & 1023) == 0 && cancelOrNull.Requested)
                        {
                            output.Canceled = true;
                            break;
                        }
                        attempts++;
                        float uTri = rng.NextFloat();
                        float2 bary = rng.NextFloat2();
                        int t = poly.PickTriangle(uTri);
                        if (bary.x + bary.y > 1f)
                            bary = 1f - bary; // reflection, as AreaUtils.cs:258-259
                        double3 a = poly.TriA[t], b = poly.TriB[t], c = poly.TriC[t];
                        double3 pos = a + (double)bary.x * (b - a) + (double)bary.y * (c - a);
                        float3 world = new float3((float)pos.x, (float)pos.y, (float)pos.z);
                        double2 q = new double2(world.x, world.z);
                        if (!grid.IsFree(q))
                            continue;
                        if (obstacles.Blocks(q))
                            continue;
                        if (!poly.Contains(q))
                            continue;
                        if (k.InnerMargin > 0f && poly.BoundaryDistanceSq(q) < (double)k.InnerMargin * k.InnerMargin)
                            continue;
                        if (exclusions.Contains(q))
                            continue;
                        if (mask != null && !mask.Allows(q, ref world.y))
                            continue;
                        if (mask != null && mask.HasBand && !mask.EdgeKeeps(q, seed))
                            continue;   // last test, no draws: the prefix property holds
                        grid.Add(q);
                        output.Planted.Add(MakeTree(ref rng, world, 0, in k, ages));
                        placed++;
                    }
                    output.Attempts += (int)attempts;
                    if (placed < target)
                        output.Saturated = true;
                }
                else if (target > 0)
                {
                    output.Saturated = true; // no usable triangle area (defensive: cannot happen after sanitize)
                }
            }

            if (wantBelt)
            {
                double band = (double)k.BeltDepth - k.BeltGap;
                double beltArea = band > 0.0 ? poly.Perimeter * band : 0.0;
                output.BeltArea = (float)beltArea;
                int left = budget - output.Planted.Count;
                if (left < 0)
                    left = 0;
                double raw = math.floor(k.BeltDensity * k.BeltCoverage * beltArea + 0.5);
                output.RawTargetBelt = RawToInt(raw);
                int target = ClampTarget(raw, left, ref status, ref output.Clamped);
                output.TargetBelt = target;
                if (target > 0 && poly.Perimeter > 0.0)
                {
                    Unity.Mathematics.Random rng = new Unity.Mathematics.Random(math.hash(new uint2(seed, SaltBelt)) | 1u);
                    int cells = NoiseCells(poly.Perimeter, k.BeltNoiseWavelength);
                    double gapSq = (double)k.BeltGap * k.BeltGap;
                    double depthSq = (double)k.BeltDepth * k.BeltDepth;
                    float lo = 1f - k.BeltCoverage - 0.15f;
                    float hi = 1f - k.BeltCoverage + 0.15f;
                    long maxAttempts = math.min((long)target * k.AttemptsPerTree, MaxAttemptsPerLayer);
                    long attempts = 0;
                    int placed = 0;
                    while (placed < target && attempts < maxAttempts)
                    {
                        if (cancelOrNull != null && (attempts & 1023) == 0 && cancelOrNull.Requested)
                        {
                            output.Canceled = true;
                            break;
                        }
                        attempts++;
                        float uEdge = rng.NextFloat();
                        float uT = rng.NextFloat();
                        float uD = rng.NextFloat();
                        float uGate = rng.NextFloat();
                        int e = poly.PickEdge(uEdge);
                        double3 a = poly.Ring[e];
                        double3 b = poly.Ring[(e + 1) % poly.Count];
                        double d = k.BeltGap + (double)uD * band;
                        double2 n = poly.OutwardNormal[e];
                        double3 pos = a + (double)uT * (b - a);
                        pos.x += n.x * d;
                        pos.z += n.y * d;
                        float3 world = new float3((float)pos.x, (float)pos.y, (float)pos.z);
                        double2 q = new double2(world.x, world.z);

                        if (k.BeltCoverage < 1f)
                        {
                            double arc = poly.EdgeStart[e] + (double)uT * poly.EdgeLength[e];
                            float noise = ValueNoise(arc / poly.Perimeter * cells, cells, seed);
                            if (!(math.smoothstep(lo, hi, noise) > uGate))
                                continue;
                        }
                        if (!grid.IsFree(q))
                            continue;
                        if (obstacles.Blocks(q))
                            continue;
                        if (poly.Contains(q))
                            continue;
                        double distSq = poly.BoundaryDistanceSq(q);
                        if (distSq < gapSq || distSq > depthSq)
                            continue;
                        if (exclusions.Contains(q))
                            continue;
                        grid.Add(q);
                        output.Planted.Add(MakeTree(ref rng, world, 1, in k, ages));
                        placed++;
                    }
                    output.Attempts += (int)attempts;
                    if (placed < target)
                        output.Saturated = true;
                }
            }

            output.Status = status;
            return status;
        }


        private static PlantedTree MakeTree(ref Unity.Mathematics.Random rng, float3 world, byte layer, in Knobs k, AgeTable ages)
        {
            float uRot = rng.NextFloat();
            float uDom = rng.NextFloat();
            float uIdx = rng.NextFloat();
            float uAge = rng.NextFloat();

            float rot = uRot * 6.2831855f;
            if (!(rot < 6.2831855f))
                rot = 0f; // float rounding can land exactly on 2π. Keep [0, 2π)

            int species = 0;
            if (k.SpeciesCount > 1 && !(uDom < k.DominantShare))
            {
                int others = k.SpeciesCount - 1;
                int idx = (int)(uIdx * others);
                if (idx >= others)
                    idx = others - 1;
                species = 1 + idx;
            }

            PlantedTree t;
            t.Position = world;
            t.Rotation = rot;
            t.Species = species;
            t.Age = ages.Pick(uAge);
            t.Layer = layer;
            return t;
        }

        private static int ClampTarget(double raw, int budget, ref GladeStatus status, ref bool clamped)
        {
            if (!(raw > 0.0))
                return 0; // also NaN
            if (raw > budget)
            {
                status = GladeStatus.Clamped;
                clamped = true; // GladePlan.Clamped, set exactly where the status is
                return budget;
            }
            return (int)raw;
        }

        /// <summary>The raw target floored at 0 (also NaN) and capped at int.MaxValue (raw is already integral: floor(x + 0.5), an int).</summary>
        private static int RawToInt(double raw)
        {
            if (!(raw > 0.0))
                return 0;
            if (raw >= int.MaxValue)
                return int.MaxValue;
            return (int)raw;
        }

        private static int NoiseCells(double perimeter, float wavelength)
        {
            double cells = 2.0;
            if (wavelength > 0f && math.isfinite(wavelength))
                cells = math.floor(perimeter / wavelength + 0.5);
            if (!(cells >= 2.0))
                cells = 2.0;
            if (cells > MaxNoiseCells)
                cells = MaxNoiseCells;
            return (int)cells;
        }

        /// <summary>Periodic 1-D value noise in [0,1): hashed lattice values, smoothstep-weighted
        /// lerp, lattice index wrapped modulo <paramref name="cells"/>.</summary>
        private static float ValueNoise(double x, int cells, uint seed)
        {
            double fl = math.floor(x);
            double f = x - fl;
            int i0 = (int)fl % cells;
            if (i0 < 0)
                i0 += cells;
            int i1 = (i0 + 1) % cells;
            float v0 = Hash01(seed, i0);
            float v1 = Hash01(seed, i1);
            float s = (float)(f * f * (3.0 - 2.0 * f));
            return v0 + (v1 - v0) * s;
        }

        private static float Hash01(uint seed, int i)
        {
            unchecked
            {
                // Fmix32: without it consecutive lattice values form a Weyl sequence
                // (math.hash is linear in i), not independent noise.
                uint h = Fmix32(math.hash(new uint3(seed, SaltNoise, (uint)i)));
                return (h >> 8) * (1f / 16777216f);
            }
        }

        /// <summary>MurmurHash3 32-bit finalizer (fmix32): full avalanche over a 32-bit word.
        /// Pure integer arithmetic, deterministic on every runtime.</summary>
        private static uint Fmix32(uint h)
        {
            unchecked
            {
                h ^= h >> 16;
                h *= 0x85EBCA6Bu;
                h ^= h >> 13;
                h *= 0xC2B2AE35u;
                h ^= h >> 16;
                return h;
            }
        }

        private static int Quantize10(float v)
        {
            if (!math.isfinite(v))
                return 0;
            float r = math.round(v * 10f); // same call as SkiRunPropSystem.HashSeed
            if (r > 2.0e9f)
                r = 2.0e9f;
            else if (r < -2.0e9f)
                r = -2.0e9f;
            return (int)r;
        }

        private static bool IsFinite(float3 v)
        {
            return math.isfinite(v.x) && math.isfinite(v.y) && math.isfinite(v.z);
        }

        private static double Cross(double2 a, double2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private struct Knobs
        {
            public GladeMode Mode;
            public float ThinFraction, GladeDensity, BeltDensity, MinSpacing, InnerMargin;
            public float BeltGap, BeltDepth, BeltCoverage, BeltNoiseWavelength, DominantShare;
            public int SpeciesCount, MaxPlant, AttemptsPerTree;
            public float4 AgeWeights;

            public static Knobs From(in GladeParams p)
            {
                Knobs k;
                k.Mode = p.Mode;
                k.ThinFraction = Clamp01(p.ThinFraction);
                k.GladeDensity = NonNeg(p.GladeDensity);
                k.BeltDensity = NonNeg(p.BeltDensity);
                k.MinSpacing = NonNeg(p.MinSpacing);
                k.InnerMargin = NonNeg(p.InnerMargin);
                k.BeltGap = NonNeg(p.BeltGap);
                k.BeltDepth = NonNeg(p.BeltDepth);
                k.BeltCoverage = Clamp01(p.BeltCoverage);
                k.BeltNoiseWavelength = NonNeg(p.BeltNoiseWavelength);
                k.DominantShare = Clamp01(p.DominantShare);
                k.SpeciesCount = p.SpeciesCount < 1 ? 1 : p.SpeciesCount;
                k.MaxPlant = p.MaxPlant < 0 ? 0 : p.MaxPlant;
                k.AttemptsPerTree = p.AttemptsPerTree < 1 ? 1 : p.AttemptsPerTree;
                k.AgeWeights = p.AgeWeights;
                return k;
            }

            private static float Clamp01(float v)
            {
                if (!(v > 0f))
                    return 0f; // also NaN
                return v < 1f ? v : 1f;
            }

            private static float NonNeg(float v)
            {
                if (!(v > 0f))
                    return 0f;
                return v < 1.0e7f ? v : 1.0e7f; // +Inf and absurd values capped
            }
        }

        private sealed class AgeTable
        {
            private readonly float m_C0, m_C1, m_C2;

            public AgeTable(float4 w)
            {
                float a = W(w.x), b = W(w.y), c = W(w.z), d = W(w.w);
                float sum = a + b + c + d;
                if (!(sum > 0f) || !math.isfinite(sum))
                {
                    a = 0f; b = 0f; c = 1f; d = 0f; sum = 1f; // fallback: all Mature
                }
                m_C0 = a / sum;
                m_C1 = (a + b) / sum;
                m_C2 = (a + b + c) / sum;
            }

            private static float W(float v)
            {
                return v > 0f && math.isfinite(v) ? v : 0f;
            }

            public float Pick(float u)
            {
                if (u < m_C0)
                    return AgeSapling;
                if (u < m_C1)
                    return AgeYoung;
                if (u < m_C2)
                    return AgeMature;
                return AgeElderly;
            }
        }

        private sealed class SpacingGrid
        {
            private readonly Dictionary<long, int> m_Head = new Dictionary<long, int>();
            private readonly List<double2> m_Pos = new List<double2>();
            private readonly List<int> m_Next = new List<int>();
            private readonly double m_Cell;
            private readonly double m_MinSq;
            private readonly bool m_Active;

            public SpacingGrid(float minSpacing)
            {
                m_Active = minSpacing > 0f;
                m_MinSq = (double)minSpacing * minSpacing;
                m_Cell = minSpacing > 0.5f ? minSpacing : 0.5; // cell >= spacing => 3x3 query suffices
            }

            private static long Key(long cx, long cz)
            {
                unchecked
                {
                    return (cx << 32) ^ (cz & 0xFFFFFFFFL);
                }
            }

            public void Add(double2 p)
            {
                if (!m_Active)
                    return;
                long key = Key((long)math.floor(p.x / m_Cell), (long)math.floor(p.y / m_Cell));
                int head;
                if (!m_Head.TryGetValue(key, out head))
                    head = -1;
                m_Pos.Add(p);
                m_Next.Add(head);
                m_Head[key] = m_Pos.Count - 1;
            }

            public bool IsFree(double2 p)
            {
                if (!m_Active)
                    return true;
                long cx = (long)math.floor(p.x / m_Cell);
                long cz = (long)math.floor(p.y / m_Cell);
                for (long dx = -1; dx <= 1; dx++)
                {
                    for (long dz = -1; dz <= 1; dz++)
                    {
                        int i;
                        if (!m_Head.TryGetValue(Key(cx + dx, cz + dz), out i))
                            continue;
                        while (i >= 0)
                        {
                            double2 d = m_Pos[i] - p;
                            if (d.x * d.x + d.y * d.y < m_MinSq)
                                return false;
                            i = m_Next[i];
                        }
                    }
                }
                return true;
            }
        }

        private sealed class ObstacleSet
        {
            private readonly Dictionary<long, int> m_Head = new Dictionary<long, int>();
            private readonly List<double2> m_Center = new List<double2>();
            private readonly List<double> m_ClearSq = new List<double>();
            private readonly List<int> m_Next = new List<int>();
            private readonly double m_Cell = 1.0;

            public ObstacleSet(GladeObstacle[] obstacles, double halfSpacing)
            {
                if (obstacles == null)
                    return;
                double maxClear = 0.0;
                for (int i = 0; i < obstacles.Length; i++)
                {
                    GladeObstacle o = obstacles[i];
                    if (!IsFinite(o.Center) || !math.isfinite(o.Radius))
                        continue;
                    double clear = math.max(0.0, (double)o.Radius) + halfSpacing;
                    if (clear > maxClear)
                        maxClear = clear;
                }
                if (maxClear <= 0.0)
                    return;
                m_Cell = math.max(maxClear, 1.0);
                for (int i = 0; i < obstacles.Length; i++)
                {
                    GladeObstacle o = obstacles[i];
                    if (!IsFinite(o.Center) || !math.isfinite(o.Radius))
                        continue;
                    double clear = math.max(0.0, (double)o.Radius) + halfSpacing;
                    if (clear <= 0.0)
                        continue;
                    double2 c = new double2(o.Center.x, o.Center.z);
                    long key = Key((long)math.floor(c.x / m_Cell), (long)math.floor(c.y / m_Cell));
                    int head;
                    if (!m_Head.TryGetValue(key, out head))
                        head = -1;
                    m_Center.Add(c);
                    m_ClearSq.Add(clear * clear);
                    m_Next.Add(head);
                    m_Head[key] = m_Center.Count - 1;
                }
            }

            private static long Key(long cx, long cz)
            {
                unchecked
                {
                    return (cx << 32) ^ (cz & 0xFFFFFFFFL);
                }
            }

            public bool Blocks(double2 p)
            {
                if (m_Center.Count == 0)
                    return false;
                long cx = (long)math.floor(p.x / m_Cell);
                long cz = (long)math.floor(p.y / m_Cell);
                for (long dx = -1; dx <= 1; dx++)
                {
                    for (long dz = -1; dz <= 1; dz++)
                    {
                        int i;
                        if (!m_Head.TryGetValue(Key(cx + dx, cz + dz), out i))
                            continue;
                        while (i >= 0)
                        {
                            double2 d = m_Center[i] - p;
                            if (d.x * d.x + d.y * d.y < m_ClearSq[i])
                                return true;
                            i = m_Next[i];
                        }
                    }
                }
                return false;
            }
        }

        private sealed class ExclusionSet
        {
            private readonly List<double2[]> m_Rings = new List<double2[]>();
            private readonly List<double4> m_Bounds = new List<double4>(); // minx, minz, maxx, maxz

            public ExclusionSet(float3[][] rings)
            {
                if (rings == null)
                    return;
                for (int r = 0; r < rings.Length; r++)
                {
                    float3[] src = rings[r];
                    if (src == null || src.Length < 3)
                        continue;
                    bool ok = true;
                    double2[] v = new double2[src.Length];
                    double4 b = new double4(double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
                    for (int i = 0; i < src.Length; i++)
                    {
                        if (!math.isfinite(src[i].x) || !math.isfinite(src[i].z))
                        {
                            ok = false;
                            break;
                        }
                        v[i] = new double2(src[i].x, src[i].z);
                        b.x = math.min(b.x, v[i].x);
                        b.y = math.min(b.y, v[i].y);
                        b.z = math.max(b.z, v[i].x);
                        b.w = math.max(b.w, v[i].y);
                    }
                    if (!ok)
                        continue; // a broken neighbor never blocks the plan
                    m_Rings.Add(v);
                    m_Bounds.Add(b);
                }
            }

            public bool Contains(double2 p)
            {
                for (int r = 0; r < m_Rings.Count; r++)
                {
                    double4 b = m_Bounds[r];
                    if (p.x < b.x || p.y < b.y || p.x > b.z || p.y > b.w)
                        continue;
                    if (CrossingInside(m_Rings[r], m_Rings[r].Length, p))
                        return true;
                }
                return false;
            }
        }

        /// <summary>Crossing-number point-in-polygon on xz (double2.y holds world z).</summary>
        private static bool CrossingInside(double2[] v, int n, double2 p)
        {
            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double2 vi = v[i], vj = v[j];
                if ((vi.y > p.y) != (vj.y > p.y))
                {
                    double x = (vj.x - vi.x) * (p.y - vi.y) / (vj.y - vi.y) + vi.x;
                    if (p.x < x)
                        inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>A GladeMask compiled for one plan: holes (with HoleMargin-expanded bounds) and the tree line. Reads its source
        /// once and never throws.</summary>
        private sealed class MaskSet
        {
            private const long MaxAreaSamples = 4000000;

            private readonly List<double2[]> m_Holes = new List<double2[]>();
            private readonly List<double4> m_Bounds = new List<double4>(); // minx, minz, maxx, maxz, already grown by the margin
            private readonly double m_Margin, m_MarginSq;
            private readonly GladeHeightField m_Ground;
            private readonly float m_TreeLine;
            private readonly double m_Cell;

            // Edge band (T157). Off: none of these is read and the mask is the pre-band mask, bit for bit
            private readonly Polygon m_Poly;
            private readonly bool m_Band;
            private readonly double m_BandMin, m_BandSpan, m_BandMaxSq, m_NoiseCell;
            private readonly EdgeGrid m_Edges;

            public bool HasBand => m_Band;

            public MaskSet(GladeMask m, Polygon poly)
            {
                m_Poly = poly;
                if (m.HasEdgeBand)
                {
                    double hi = math.min((double)m.EdgeBandMaxM, 1.0e5);
                    double lo = m.EdgeBandMinM > 0f && math.isfinite(m.EdgeBandMinM) ? math.min((double)m.EdgeBandMinM, hi) : 0.0;
                    float wave = m.EdgeNoiseWavelengthM;
                    m_Band = true;
                    m_BandMin = lo;
                    m_BandSpan = hi - lo;
                    m_BandMaxSq = hi * hi;
                    m_NoiseCell = wave > 0f && math.isfinite(wave) ? math.max((double)wave, 1.0) : GladeMask.DefaultEdgeNoiseWavelengthM;
                    m_Edges = new EdgeGrid(poly, hi);
                }
                float margin = m.HoleMargin;
                m_Margin = margin > 0f && math.isfinite(margin) ? margin : 0.0;
                m_MarginSq = m_Margin * m_Margin;
                bool line = m.Ground != null && math.isfinite(m.TreeLine);
                m_Ground = line ? m.Ground : null;
                m_TreeLine = m.TreeLine;
                float cell = m.AreaCellM;
                m_Cell = cell > 0f && math.isfinite(cell) ? math.max(cell, 0.25f) : GladeMask.DefaultAreaCellM;
                float3[][] rings = m.Holes;
                if (rings == null)
                    return;
                for (int r = 0; r < rings.Length; r++)
                {
                    float3[] src = rings[r];
                    if (src == null || src.Length < 3)
                        continue;
                    bool ok = true;
                    double2[] v = new double2[src.Length];
                    double4 b = new double4(double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
                    for (int i = 0; i < src.Length; i++)
                    {
                        if (!math.isfinite(src[i].x) || !math.isfinite(src[i].z))
                        {
                            ok = false;
                            break;
                        }
                        v[i] = new double2(src[i].x, src[i].z);
                        b.x = math.min(b.x, v[i].x);
                        b.y = math.min(b.y, v[i].y);
                        b.z = math.max(b.z, v[i].x);
                        b.w = math.max(b.w, v[i].y);
                    }
                    if (!ok)
                        continue; // a broken run outline never blocks the fill
                    m_Holes.Add(v);
                    m_Bounds.Add(new double4(b.x - m_Margin, b.y - m_Margin, b.z + m_Margin, b.w + m_Margin));
                }
            }

            public bool Allows(double2 q)
            {
                float y = 0f;
                return Allows(q, ref y);
            }

            /// <summary>True when q is outside every hole by the margin and (with a ground) at or below the tree line. Then
            /// <paramref name="groundY"/> is the ground height there (unchanged without a ground).</summary>
            public bool Allows(double2 q, ref float groundY)
            {
                for (int r = 0; r < m_Holes.Count; r++)
                {
                    double4 b = m_Bounds[r];
                    if (q.x < b.x || q.y < b.y || q.x > b.z || q.y > b.w)
                        continue;
                    double2[] v = m_Holes[r];
                    if (CrossingInside(v, v.Length, q))
                        return false;
                    if (m_Margin > 0.0 && RingDistanceSq(v, q) < m_MarginSq)
                        return false;
                }
                if (m_Ground != null)
                {
                    float h = m_Ground.HeightAt(q.x, q.y);
                    if (!(h <= m_TreeLine))
                        return false; // also NaN: unknown ground never gets a tree
                    groundY = h;
                }
                return true;
            }

            /// <summary>Allowed area inside the polygon. The method counts the cell centers of a grid anchored at the polygon's bounds minimum
            /// that are inside and allowed. It then multiplies by the cell area. The cell grows when the bounds would need more than MaxAreaSamples.</summary>
            public double Area(Polygon poly, GladeCancel cancel = null)
            {
                double4 bb = poly.Bounds;
                double w = bb.z - bb.x, d = bb.w - bb.y;
                if (!(w > 0.0) || !(d > 0.0))
                    return 0.0;
                double cell = m_Cell;
                double samples = math.ceil(w / cell) * math.ceil(d / cell);
                if (samples > MaxAreaSamples)
                    cell *= math.sqrt(samples / MaxAreaSamples) * 1.001;
                long nx = (long)math.ceil(w / cell), nz = (long)math.ceil(d / cell);
                if (m_Band)
                    return BandArea(poly, cancel, bb, cell, nx, nz);
                long count = 0;
                for (long j = 0; j < nz; j++)
                {
                    if (cancel != null && cancel.Requested)
                        return 0.0;   // the caller sees the request and drops the plan
                    double z = bb.y + (j + 0.5) * cell;
                    for (long i = 0; i < nx; i++)
                    {
                        double2 q = new double2(bb.x + (i + 0.5) * cell, z);
                        if (poly.Contains(q) && Allows(q))
                            count++;
                    }
                }
                return count * cell * cell;
            }

            /// <summary>Area() with the band on: each allowed cell weighs its EdgeAccept probability (the expected share of darts the
            /// band keeps there), so the Glade target is the number of trees the band lets stand.</summary>
            private double BandArea(Polygon poly, GladeCancel cancel, double4 bb, double cell, long nx, long nz)
            {
                double sum = 0.0;
                for (long j = 0; j < nz; j++)
                {
                    if (cancel != null && cancel.Requested)
                        return 0.0;
                    double z = bb.y + (j + 0.5) * cell;
                    double rowSum = 0.0;   // per row: keeps the double sum's rounding small on 1.4 M cells
                    for (long i = 0; i < nx; i++)
                    {
                        double2 q = new double2(bb.x + (i + 0.5) * cell, z);
                        if (poly.Contains(q) && Allows(q))
                            rowSum += EdgeAccept(q);
                    }
                    sum += rowSum;
                }
                return sum * cell * cell;
            }

            /// <summary>The band's keep probability at q (inside the polygon): smoothstep(0, depth, d), d = distance to the polygon's
            /// outline, depth = min + span x noise(q). 1 without a band and at or past the deepest band.</summary>
            public double EdgeAccept(double2 q)
            {
                if (!m_Band)
                    return 1.0;
                double dSq = m_Edges.DistanceSqWithin(m_Poly, q);
                if (!(dSq < m_BandMaxSq))
                    return 1.0;
                double depth = m_BandMin + m_BandSpan * ValueNoise2(q.x / m_NoiseCell, q.y / m_NoiseCell);
                if (!(depth > 0.0))
                    return 1.0;
                double t = math.sqrt(dSq) / depth;
                if (t >= 1.0)
                    return 1.0;
                return t * t * (3.0 - 2.0 * t);
            }

            /// <summary>The per-dart gate: keep iff a position hash in [0, 1) (12.5 cm cells, the plan seed) is under EdgeAccept(q).</summary>
            public bool EdgeKeeps(double2 q, uint seed)
            {
                double p = EdgeAccept(q);
                if (p >= 1.0)
                    return true;
                if (!(p > 0.0))
                    return false;
                double qx = math.floor(q.x * 8.0), qz = math.floor(q.y * 8.0);
                if (math.abs(qx) > 2.0e9 || math.abs(qz) > 2.0e9)
                    return false;
                unchecked
                {
                    uint h = Fmix32(math.hash(new uint4(seed, SaltEdgeGate, (uint)(int)qx, (uint)(int)qz)));
                    return (h >> 8) * (1.0 / 16777216.0) < p;
                }
            }

            /// <summary>World-anchored 2-D value noise in [0, 1): hashed lattice values (SaltEdgeNoise, no plan seed), smoothstep-weighted
            /// bilinear blend. x and z are in lattice cells.</summary>
            private static double ValueNoise2(double x, double z)
            {
                double fx = math.floor(x), fz = math.floor(z);
                if (math.abs(fx) > 1.0e9 || math.abs(fz) > 1.0e9)
                    return 0.5;
                int ix = (int)fx, iz = (int)fz;
                double tx = x - fx, tz = z - fz;
                tx = tx * tx * (3.0 - 2.0 * tx);
                tz = tz * tz * (3.0 - 2.0 * tz);
                double v00 = Lattice(ix, iz), v10 = Lattice(ix + 1, iz), v01 = Lattice(ix, iz + 1), v11 = Lattice(ix + 1, iz + 1);
                double a = v00 + (v10 - v00) * tx;
                double b = v01 + (v11 - v01) * tx;
                return a + (b - a) * tz;
            }

            private static double Lattice(int ix, int iz)
            {
                unchecked
                {
                    uint h = Fmix32(math.hash(new uint3(SaltEdgeNoise, (uint)ix, (uint)iz)));
                    return (h >> 8) * (1.0 / 16777216.0);
                }
            }

            private static double RingDistanceSq(double2[] v, double2 p)
            {
                double best = double.MaxValue;
                int n = v.Length;
                for (int i = 0; i < n; i++)
                {
                    double2 a = v[i];
                    double2 ab = v[(i + 1) % n] - a;
                    double2 ap = p - a;
                    double len2 = ab.x * ab.x + ab.y * ab.y;
                    double t = len2 > 0.0 ? (ap.x * ab.x + ap.y * ab.y) / len2 : 0.0;
                    t = t < 0.0 ? 0.0 : t > 1.0 ? 1.0 : t;
                    double2 dd = ap - t * ab;
                    double dsq = dd.x * dd.x + dd.y * dd.y;
                    if (dsq < best)
                        best = dsq;
                }
                return best;
            }
        }

        /// <summary>The outline's edges bucketed on a dense grid (cell >= the band's max depth) for the edge band. Each cell lists every edge
        /// that passes through it or one of its 8 neighbors. An edge with a point within `reach` of q passes through a cell within one cell
        /// of q's, so DistanceSqWithin is exact below reach^2 (the same per-edge formula as Polygon.BoundaryDistanceSq) and returns
        /// double.MaxValue when no edge is that near.</summary>
        private sealed class EdgeGrid
        {
            private const long MaxCells = 1 << 18;
            private readonly double m_X0, m_Z0, m_Cell;
            private readonly int m_Nx, m_Nz;
            private readonly int[][] m_Lists;

            public EdgeGrid(Polygon poly, double reach)
            {
                double4 b = poly.Bounds;
                double w = b.z - b.x + 2.0 * reach, d = b.w - b.y + 2.0 * reach;
                double cell = math.max(reach, 1.0);
                if (math.ceil(w / cell) * math.ceil(d / cell) > MaxCells)
                    cell = math.sqrt(w * d / MaxCells) * 1.001;
                m_Cell = cell;
                m_X0 = b.x - reach;
                m_Z0 = b.y - reach;
                m_Nx = math.max(1, (int)math.ceil(w / cell));
                m_Nz = math.max(1, (int)math.ceil(d / cell));
                var lists = new List<int>[m_Nx * m_Nz];
                for (int e = 0; e < poly.Count; e++)
                {
                    double2 a = poly.Xz[e], c = poly.Xz[(e + 1) % poly.Count];
                    int ix0 = Ix(math.min(a.x, c.x)), ix1 = Ix(math.max(a.x, c.x));
                    int iz0 = Iz(math.min(a.y, c.y)), iz1 = Iz(math.max(a.y, c.y));
                    for (int iz = iz0; iz <= iz1; iz++)
                    {
                        for (int ix = ix0; ix <= ix1; ix++)
                        {
                            if (!SegmentHitsBox(a, c, m_X0 + ix * cell, m_Z0 + iz * cell, cell))
                                continue;
                            for (int nz = math.max(0, iz - 1); nz <= math.min(m_Nz - 1, iz + 1); nz++)
                            {
                                for (int nx = math.max(0, ix - 1); nx <= math.min(m_Nx - 1, ix + 1); nx++)
                                {
                                    List<int> l = lists[nz * m_Nx + nx] ?? (lists[nz * m_Nx + nx] = new List<int>(4));
                                    if (l.Count == 0 || l[l.Count - 1] != e)
                                        l.Add(e);   // edges arrive in order: a repeat is always the last entry
                                }
                            }
                        }
                    }
                }
                m_Lists = new int[lists.Length][];
                for (int i = 0; i < lists.Length; i++)
                    m_Lists[i] = lists[i] != null ? lists[i].ToArray() : null;
            }

            private int Ix(double x)
            {
                int i = (int)math.floor((x - m_X0) / m_Cell);
                return i < 0 ? 0 : i >= m_Nx ? m_Nx - 1 : i;
            }

            private int Iz(double z)
            {
                int i = (int)math.floor((z - m_Z0) / m_Cell);
                return i < 0 ? 0 : i >= m_Nz ? m_Nz - 1 : i;
            }

            /// <summary>Slab test of segment a-c against the square [x0, x0 + s] x [z0, z0 + s] (closed).</summary>
            private static bool SegmentHitsBox(double2 a, double2 c, double x0, double z0, double s)
            {
                double t0 = 0.0, t1 = 1.0;
                double2 dv = c - a;
                return Slab(a.x, dv.x, x0, x0 + s, ref t0, ref t1) && Slab(a.y, dv.y, z0, z0 + s, ref t0, ref t1);
            }

            private static bool Slab(double p, double dp, double lo, double hi, ref double t0, ref double t1)
            {
                if (dp == 0.0)
                    return p >= lo && p <= hi;
                double ta = (lo - p) / dp, tb = (hi - p) / dp;
                if (ta > tb)
                {
                    double t = ta;
                    ta = tb;
                    tb = t;
                }
                if (ta > t0)
                    t0 = ta;
                if (tb < t1)
                    t1 = tb;
                return t0 <= t1;
            }

            public double DistanceSqWithin(Polygon poly, double2 p)
            {
                double fx = math.floor((p.x - m_X0) / m_Cell), fz = math.floor((p.y - m_Z0) / m_Cell);
                if (!(fx >= 0.0 && fz >= 0.0 && fx < m_Nx && fz < m_Nz))
                    return double.MaxValue;   // outside the grown bounds: farther than reach from every edge
                int[] l = m_Lists[(int)fz * m_Nx + (int)fx];
                if (l == null)
                    return double.MaxValue;
                double best = double.MaxValue;
                for (int k = 0; k < l.Length; k++)
                {
                    double dsq = poly.EdgeDistanceSq(l[k], p);
                    if (dsq < best)
                        best = dsq;
                }
                return best;
            }
        }

        private sealed class Polygon
        {
            public int Count;
            public double3[] Ring;          // sanitized vertices (world)
            public double2[] Xz;            // same, xz only
            public double2[] OutwardNormal; // per edge i -> i+1
            public double[] EdgeLength;
            public double[] EdgeStart;      // arc length at edge start
            public double[] EdgeCdf;        // cumulative length at edge end
            public double Perimeter;
            public double Area;             // |shoelace|
            public double3[] TriA, TriB, TriC;
            public double[] TriCdf;
            public double TriangleTotal;
            public double4 Bounds;

            public bool Contains(double2 p)
            {
                if (p.x < Bounds.x || p.y < Bounds.y || p.x > Bounds.z || p.y > Bounds.w)
                    return false;
                return CrossingInside(Xz, Count, p);
            }

            public double BoundaryDistanceSq(double2 p)
            {
                double best = double.MaxValue;
                for (int i = 0; i < Count; i++)
                {
                    double2 a = Xz[i];
                    double2 ab = Xz[(i + 1) % Count] - a;
                    double2 ap = p - a;
                    double len2 = ab.x * ab.x + ab.y * ab.y;
                    double t = len2 > 0.0 ? (ap.x * ab.x + ap.y * ab.y) / len2 : 0.0;
                    if (t < 0.0)
                        t = 0.0;
                    else if (t > 1.0)
                        t = 1.0;
                    double2 d = ap - t * ab;
                    double dsq = d.x * d.x + d.y * d.y;
                    if (dsq < best)
                        best = dsq;
                }
                return best;
            }

            /// <summary>Squared distance from p to edge i (i -> i + 1): BoundaryDistanceSq's per-edge formula, kept separate so that
            /// method stays bit-identical.</summary>
            public double EdgeDistanceSq(int i, double2 p)
            {
                double2 a = Xz[i];
                double2 ab = Xz[(i + 1) % Count] - a;
                double2 ap = p - a;
                double len2 = ab.x * ab.x + ab.y * ab.y;
                double t = len2 > 0.0 ? (ap.x * ab.x + ap.y * ab.y) / len2 : 0.0;
                if (t < 0.0)
                    t = 0.0;
                else if (t > 1.0)
                    t = 1.0;
                double2 d = ap - t * ab;
                return d.x * d.x + d.y * d.y;
            }

            public int PickTriangle(float u)
            {
                return LowerBound(TriCdf, u * TriangleTotal);
            }

            public int PickEdge(float u)
            {
                return LowerBound(EdgeCdf, u * Perimeter);
            }

            /// <summary>First index whose cumulative value is &gt; x. Zero-weight entries are
            /// never picked unless everything is zero. The result is clamped to the last index.</summary>
            private static int LowerBound(double[] cdf, double x)
            {
                int lo = 0, hi = cdf.Length - 1;
                while (lo < hi)
                {
                    int mid = (lo + hi) >> 1;
                    if (cdf[mid] > x)
                        hi = mid;
                    else
                        lo = mid + 1;
                }
                return lo;
            }
        }

        private static GladeStatus BuildPolygon(float3[] ring, int3[] tris, out Polygon poly)
        {
            poly = null;
            if (ring == null || ring.Length < 3)
                return ring != null && !AllFinite(ring) ? GladeStatus.NonFinite : GladeStatus.TooFewVertices;
            if (!AllFinite(ring))
                return GladeStatus.NonFinite;

            List<double3> v = new List<double3>(ring.Length);
            for (int i = 0; i < ring.Length; i++)
            {
                double3 c = (double3)ring[i];
                if (v.Count > 0 && DistSqXz(v[v.Count - 1], c) < DuplicateEpsSq)
                    continue;
                v.Add(c);
            }
            while (v.Count > 1 && DistSqXz(v[v.Count - 1], v[0]) < DuplicateEpsSq)
                v.RemoveAt(v.Count - 1);
            int n = v.Count;
            if (n < 3)
                return GladeStatus.TooFewVertices;

            Polygon pg = new Polygon();
            pg.Count = n;
            pg.Ring = v.ToArray();
            pg.Xz = new double2[n];
            double4 b = new double4(double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
            for (int i = 0; i < n; i++)
            {
                pg.Xz[i] = pg.Ring[i].xz;
                b.x = math.min(b.x, pg.Xz[i].x);
                b.y = math.min(b.y, pg.Xz[i].y);
                b.z = math.max(b.z, pg.Xz[i].x);
                b.w = math.max(b.w, pg.Xz[i].y);
            }
            pg.Bounds = b;

            double2 o = pg.Xz[0];
            double twice = 0.0;
            for (int i = 1; i < n - 1; i++)
                twice += Cross(pg.Xz[i] - o, pg.Xz[i + 1] - o);
            double signedArea = 0.5 * twice;
            double area = math.abs(signedArea);
            if (!(area >= MinArea))
                return GladeStatus.ZeroArea;
            pg.Area = area;
            bool ccw = signedArea > 0.0; // CCW in (x, z): interior on the LEFT of each edge

            pg.EdgeLength = new double[n];
            pg.EdgeStart = new double[n];
            pg.EdgeCdf = new double[n];
            pg.OutwardNormal = new double2[n];
            double acc = 0.0;
            for (int i = 0; i < n; i++)
            {
                double2 d = pg.Xz[(i + 1) % n] - pg.Xz[i];
                double len = math.sqrt(d.x * d.x + d.y * d.y);
                pg.EdgeLength[i] = len;
                pg.EdgeStart[i] = acc;
                acc += len;
                pg.EdgeCdf[i] = acc;
                double2 right = len > 0.0 ? new double2(d.y, -d.x) / len : new double2(0.0, 0.0);
                pg.OutwardNormal[i] = ccw ? right : -right; // CCW: interior left, outside right
            }
            pg.Perimeter = acc;

            List<double3> ta = new List<double3>(), tb = new List<double3>(), tc = new List<double3>();
            if (tris != null && tris.Length > 0)
            {
                double sum = 0.0;
                for (int i = 0; i < tris.Length; i++)
                {
                    int3 t = tris[i];
                    if (t.x < 0 || t.y < 0 || t.z < 0 || t.x >= ring.Length || t.y >= ring.Length || t.z >= ring.Length)
                        return GladeStatus.BadTriangles;
                    double3 a = (double3)ring[t.x], bb = (double3)ring[t.y], c = (double3)ring[t.z];
                    ta.Add(a);
                    tb.Add(bb);
                    tc.Add(c);
                    sum += math.abs(Cross(bb.xz - a.xz, c.xz - a.xz)) * 0.5;
                }
                if (math.abs(sum - area) > TriangleAreaTolerance * area)
                    return GladeStatus.BadTriangles;
            }
            else
            {
                if (!EarClip(pg.Xz, n, ccw, pg.Ring, ta, tb, tc))
                    return GladeStatus.BadTriangles;
            }

            int m = ta.Count;
            pg.TriA = ta.ToArray();
            pg.TriB = tb.ToArray();
            pg.TriC = tc.ToArray();
            pg.TriCdf = new double[m];
            double total = 0.0;
            for (int i = 0; i < m; i++)
            {
                total += math.abs(Cross(pg.TriB[i].xz - pg.TriA[i].xz, pg.TriC[i].xz - pg.TriA[i].xz)) * 0.5;
                pg.TriCdf[i] = total;
            }
            pg.TriangleTotal = total;
            if (m == 0 || !(total > 0.0))
                return GladeStatus.BadTriangles;

            poly = pg;
            return GladeStatus.Ok;
        }

        private static bool AllFinite(float3[] ring)
        {
            for (int i = 0; i < ring.Length; i++)
            {
                if (!IsFinite(ring[i]))
                    return false;
            }
            return true;
        }

        private static double DistSqXz(double3 a, double3 b)
        {
            double dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        /// <summary>Ear clipping over the sanitized ring, walked in CCW order. Degenerate
        /// (collinear / spike) vertices are dropped without a triangle. Returns false when a
        /// full pass finds no ear (self-intersecting input).</summary>
        private static bool EarClip(double2[] xz, int n, bool ccw, double3[] ring,
                                    List<double3> ta, List<double3> tb, List<double3> tc)
        {
            int[] idx = new int[n];
            for (int i = 0; i < n; i++)
                idx[i] = ccw ? i : n - 1 - i;
            int m = n;
            int cur = 0;
            int misses = 0;
            while (m > 3)
            {
                if (misses > m)
                    return false;
                int ip = (cur + m - 1) % m;
                int inx = (cur + 1) % m;
                double2 a = xz[idx[ip]], b = xz[idx[cur]], c = xz[idx[inx]];
                double2 ab = b - a, bc = c - b;
                double cr = Cross(ab, bc);
                double scale = math.sqrt((ab.x * ab.x + ab.y * ab.y) * (bc.x * bc.x + bc.y * bc.y));
                bool degenerate = math.abs(cr) <= 1e-12 * scale;
                if (degenerate || (cr > 0.0 && !AnyInside(xz, idx, m, ip, cur, inx)))
                {
                    if (!degenerate)
                    {
                        ta.Add(ring[idx[ip]]);
                        tb.Add(ring[idx[cur]]);
                        tc.Add(ring[idx[inx]]);
                    }
                    for (int j = cur; j < m - 1; j++)
                        idx[j] = idx[j + 1];
                    m--;
                    if (cur >= m)
                        cur = 0;
                    misses = 0;
                    continue;
                }
                cur = (cur + 1) % m;
                misses++;
            }
            double2 p0 = xz[idx[0]], p1 = xz[idx[1]], p2 = xz[idx[2]];
            if (math.abs(Cross(p1 - p0, p2 - p1)) > 0.0)
            {
                ta.Add(ring[idx[0]]);
                tb.Add(ring[idx[1]]);
                tc.Add(ring[idx[2]]);
            }
            return true;
        }

        private static bool AnyInside(double2[] xz, int[] idx, int m, int ia, int ib, int ic)
        {
            double2 a = xz[idx[ia]], b = xz[idx[ib]], c = xz[idx[ic]];
            for (int j = 0; j < m; j++)
            {
                if (j == ia || j == ib || j == ic)
                    continue;
                double2 p = xz[idx[j]];
                if ((p.x == a.x && p.y == a.y) || (p.x == b.x && p.y == b.y) || (p.x == c.x && p.y == c.y))
                    continue;
                // inclusive test against the CCW triangle (a, b, c)
                if (Cross(b - a, p - a) >= 0.0 && Cross(c - b, p - b) >= 0.0 && Cross(a - c, p - c) >= 0.0)
                    return true;
            }
            return false;
        }
    }
}
