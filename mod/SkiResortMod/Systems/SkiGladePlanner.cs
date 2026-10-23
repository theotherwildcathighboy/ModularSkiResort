// The pure, deterministic tree planner behind the Glade tool. Entities-free on purpose: usings are System,
// System.Collections.Generic and Unity.Mathematics ONLY, so the same file is compiled into the net48 mod AND linked into the
// net8 headless test host tests/GladeBatch. Re-derived from the vanilla systems; nothing copied from ParkManager.
// Contract: the output is a pure function of (ring, triangles, seed, params, obstacles, existing, thinnable, exclusion rings); no
// static mutable state, never logs, never throws: degenerate input gives a GladeStatus and empty output lists.
// Layers run in a fixed order (sanitize, thin, glade, belt); each has its own salted random stream with a FIXED number of draws
// per attempt and per accepted tree. SkiGladeCompose relies on the resulting prefix property (MaxPlant m1 is a bit-exact prefix of m2).
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
        public int SpeciesCount;          // pool size; species index 0 is dominant
        public float DominantShare;       // 0..1
        public float4 AgeWeights;         // Sapling, Young, Mature, Elderly (sum > 0)
        public int MaxPlant, AttemptsPerTree;
    }

    public struct GladeObstacle
    {
        public float3 Center;
        public float Radius;
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

        /// <summary>A MaxPlant budget bound a target (Status == Clamped stays as before; this flag is the explicit form of the same event).</summary>
        public bool Clamped;

        public void Clear()
        {
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
        // Vanilla ToolUtils.GetRandomAge constants (ToolUtils.cs:158-165; Mature at :162-163); both 0.425f and the vanilla value map to
        // TreeState.Adult in ObjectUtils.InitializeTreeState :438-442
        public const float AgeSapling = 0f, AgeYoung = 0.17500001f, AgeMature = 0.425f, AgeElderly = 0.77500004f;

        // Stream salts: changing any of them changes every plan
        private const uint SaltThin = 0x7A1B3C5Du;
        private const uint SaltGlade = 0x9E3779B9u;
        private const uint SaltBelt = 0x85EBCA6Bu;
        private const uint SaltNoise = 0xC2B2AE35u;

        // Hard ceilings so absurd parameters can never hang the main thread
        private const long MaxAttemptsPerLayer = 2000000;
        private const int MaxNoiseCells = 1000000;
        private const double DuplicateEpsSq = 1e-6;   // 1 mm in xz
        private const double MinArea = 1.0;           // m²
        private const double TriangleAreaTolerance = 0.05;

        /// <summary>32-bit run seed. FNV-1a (word-wise, offset 2166136261, prime 16777619) over
        /// (int)round(10·x), (int)round(10·y), (int)round(10·z) — exactly the 0.1 m quantization
        /// of SkiRunPropSystem.HashSeed (SkiRunPropSystem.cs:251-264) — then the salt as a
        /// fourth word, then h ^= h >> 15. Non-finite components quantize to 0; never returns 0.</summary>
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
        /// last-ulp float noise cannot flip it) with the seed; selected iff
        /// (h &amp; 0xFFFF) &lt; fraction·65536. Monotone in fraction; fraction ≤ 0 → never,
        /// ≥ 1 → always. Non-finite or out-of-int-range positions are never selected.
        /// h = Fmix32(math.hash(...)): math.hash(uint4) alone is a LINEAR combination of its
        /// lanes (no avalanche; measured in GladeBatch — cells 1 m apart agreed 83 % of the
        /// time, a stripe lattice), so the finalizer is load-bearing for spatial randomness.</summary>
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

        /// <summary>The full 32-bit hash ThinSelect thresholds (same 12.5 cm cells, seed/salt words and Fmix32 finalizer), so for a finite,
        /// in-range position ThinSelect(pos, seed, f) == ((ThinHash(pos, seed) &amp; 0xFFFF) &lt; f*65536). Non-finite or out-of-int-range
        /// positions (the ones ThinSelect never selects) return 0. y is ignored. SkiGladeCompose ranks by it.</summary>
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
            if (output == null)
                output = new GladePlan(); // never throw; the caller simply gets nothing back
            output.Clear();

            Polygon poly;
            GladeStatus geo = BuildPolygon(ring, trianglesOrNull, out poly);
            if (geo != GladeStatus.Ok)
            {
                output.Status = geo;
                return geo;
            }
            output.Area = (float)poly.Area;

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
                if (wantGlade && poly.Contains(q))
                    survivorsInside++;
            }

            ObstacleSet obstacles = new ObstacleSet(obstaclesOrNull, 0.5 * k.MinSpacing);
            ExclusionSet exclusions = new ExclusionSet(exclusionRingsOrNull);
            AgeTable ages = new AgeTable(k.AgeWeights);
            int budget = k.MaxPlant;

            if (wantGlade)
            {
                double raw = math.floor(k.GladeDensity * poly.Area + 0.5) - survivorsInside;
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
                    output.Saturated = true; // no usable triangle area (cannot happen after sanitize; defensive)
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
                rot = 0f; // float rounding can land exactly on 2π; keep [0, 2π)

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

            public int PickTriangle(float u)
            {
                return LowerBound(TriCdf, u * TriangleTotal);
            }

            public int PickEdge(float u)
            {
                return LowerBound(EdgeCdf, u * Perimeter);
            }

            /// <summary>First index whose cumulative value is &gt; x (zero-weight entries are
            /// never picked unless everything is zero); clamped to the last index.</summary>
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
