// The pure part of the Options prop density. SkiRunPropSystem calls these on the main thread; tests/ResortBatch DensityTests
// checks them with literal values.
// Look only. Two Options sliders, "Snow guns and hydrants" (snowmaking AND night runs) and "Floodlight masts on night runs",
// 0-100 % step 5, default 100. They change how many corners of our propped runs carry a prop; snow (a per-texel line by run type)
// and upkeep (per active hectare by run type, SkiRunUpkeepSystem) never read them.
// Mirrors the group branch of CreateSecondaryObject for one area corner (SecondaryObjectSystem.cs, 1.6.0f1).
// Pure file: `using System;` only (no Game.dll, no Unity types); linked into tests/ResortBatch. C# 9 / net48: no Math.Clamp.
// Engine notes: EngineNotes/RunPropDensityRules.md

using System;

namespace SkiResortMod.Systems
{
    public static class RunPropDensityRules
    {
        /// <summary>The two Options sliders: 0-100 % in steps of 5, default 100 (= today's look).</summary>
        public const int PctMin = 0, PctMax = 100, PctStep = 5, PctDefault = 100;

        /// <summary>OnUpdate frames the slider pair must stay still before an apply, so a drag writes once.</summary>
        public const int SettleFrames = 30;

        /// <summary>Census classes of a run prop (log lines only).</summary>
        public const byte ClassOther = 0, ClassGun = 1, ClassHydrant = 2, ClassMast = 3, ClassEmpty = 4;

        /// <summary>The run prefab chance at full look (AreaInitializeSystem.cs:398) and the roll's bound (:3279).</summary>
        private const int ChanceMax = 100;

        /// <summary>The empty marker's prefab name (mesh-less SkiPropNone) and the hydrant prefix (SkiHydrantBox*).</summary>
        private const string EmptyMarkerName = "SkiPropNone";
        private const string HydrantPrefix = "SkiHydrant";

        public static int ClampPct(int pct)
        {
            return pct < PctMin ? PctMin : pct > PctMax ? PctMax : pct;
        }

        /// <summary>The snowmaking run prefab's per-corner chance: round(base × g / 100), clamped 0..100.</summary>
        public static int SnowmakingChance(int baseChance, int gunPct)
        {
            long b = baseChance < 0 ? 0 : baseChance;
            int g = ClampPct(gunPct);
            return ClampChance(RoundDiv(b * g, PctMax));
        }

        /// <summary>The night run prefab's per-corner chance (engine notes): round(base × (g × NM + m × M) / (100 × (NM + M))),
        /// clamped 0..100; 0 when NM + M &lt;= 0 or both sliders are 0.</summary>
        public static int NightChance(int baseChance, int gunPct, int mastPct, int sharedWeight, int mastWeight)
        {
            long b = baseChance < 0 ? 0 : baseChance;
            long nm = sharedWeight < 0 ? 0 : sharedWeight;
            long mm = mastWeight < 0 ? 0 : mastWeight;
            int g = ClampPct(gunPct);
            int m = ClampPct(mastPct);
            if (nm + mm <= 0 || (g == 0 && m == 0))
                return 0;
            return ClampChance(RoundDiv(b * (g * nm + m * mm), PctMax * (nm + mm)));
        }

        /// <summary>A mast's (night-only member's) weight: 0 when base &lt;= 0 or m = 0; base when g = 0; else
        /// max(1, round(base × m / g)).</summary>
        public static int MastWeight(int baseWeight, int gunPct, int mastPct)
        {
            int g = ClampPct(gunPct);
            int m = ClampPct(mastPct);
            if (baseWeight <= 0 || m == 0)
                return 0;
            if (g == 0)
                return baseWeight;
            long w = RoundDiv((long)baseWeight * m, g);
            if (w < 1)
                return 1;
            return w > int.MaxValue ? int.MaxValue : (int)w;
        }

        /// <summary>A shared member's weight in the night group (also the snowmaking group's): 0 when g = 0, else its
        /// base weight (never negative).</summary>
        public static int NonMastWeight(int baseWeight, int gunPct)
        {
            if (ClampPct(gunPct) == 0 || baseWeight <= 0)
                return 0;
            return baseWeight;
        }

        public static int NextStable(int stable, bool changed)
        {
            if (changed)
                return 0;
            int s = stable < 0 ? 0 : stable;
            return s >= SettleFrames ? SettleFrames : s + 1;
        }

        /// <summary>Apply now: forced (a game load), or the seen pair differs from the applied pair and has been still
        /// for SettleFrames updates.</summary>
        public static bool ApplyDue(int seenGun, int seenMast, int appliedGun, int appliedMast, int stable, bool force)
        {
            if (force)
                return true;
            return (seenGun != appliedGun || seenMast != appliedMast) && stable >= SettleFrames;
        }

        /// <summary>The census expectation in tenths of a prop: round(corners × chance × classWeight × 10 /
        /// (100 × totalWeight)); 0 when totalWeight &lt;= 0. Negative inputs read as 0, the chance is clamped 0..100.</summary>
        public static int ExpectedTenths(int corners, int chance, int classWeight, int totalWeight)
        {
            if (totalWeight <= 0)
                return 0;
            long c = corners < 0 ? 0 : corners;
            long p = ClampChance(chance);
            long w = classWeight < 0 ? 0 : classWeight;
            long t = RoundDiv(c * p * w * 10, (long)ChanceMax * totalWeight);
            return t > int.MaxValue ? int.MaxValue : (int)t;
        }

        /// <summary>The census class of a run prop by prefab name: null or empty → Other; a night-only member → Mast;
        /// SkiPropNone → Empty; SkiHydrant* → Hydrant; anything else → Gun. Log lines only.</summary>
        public static byte ClassOf(string prefabName, bool nightOnly)
        {
            if (string.IsNullOrEmpty(prefabName))
                return ClassOther;
            if (nightOnly)
                return ClassMast;
            if (string.Equals(prefabName, EmptyMarkerName, StringComparison.Ordinal))
                return ClassEmpty;
            if (prefabName.StartsWith(HydrantPrefix, StringComparison.Ordinal))
                return ClassHydrant;
            return ClassGun;
        }

        /// <summary>Which census classes a run kind may carry at its corners. The cat track (RunKind.CatTrack = 4) is hydrants only: no
        /// guns, no floodlight masts (it is not injected or lit), and no empty-marker gaps of its own. Every other kind keeps what its
        /// prefab ships. No consumer yet: the SkiRunCatTrackA prefab ships no AreaSubObjects (a clone of the groomed prefab), so this rule
        /// is the spec and test for the day its hydrant entry lands; the prop system already watches the prefab.</summary>
        public static bool PropClassAllowed(int runKind, byte propClass)
        {
            if (runKind == (int)RunKind.CatTrack)
                return propClass == ClassHydrant;
            return true;
        }

        private static long RoundDiv(long a, long b)
        {
            return (2 * a + b) / (2 * b);
        }

        private static int ClampChance(long v)
        {
            return v < 0 ? 0 : v > ChanceMax ? ChanceMax : (int)v;
        }
    }

    /// <summary>The yaw a night-run floodlight mast must have so its lamps aim INTO the run and slightly downhill. Pure, plain floats
    /// (System only), x/z in the game's xz plane, y = height.
    /// The game places an area-border prop with local +Z along the outgoing border segment (SecondaryObjectSystem.cs:2984-2992) and
    /// never consults the winding, so a prefab yaw cannot fix it; we turn the placed mast ourselves (SkiNightLightSystem.AimMasts)
    /// to look along the vector returned here. Interior side from the shoelace sign (CCW in x,z = interior on the LEFT of travel,
    /// the glade planner's convention); at a corner the two edges' inward normals are summed (the miter direction).
    /// Deterministic: a function of the node ring only. Engine notes: EngineNotes/RunPropDensityRules.md</summary>
    public static class MastAimRules
    {
        /// <summary>Largest sideways lean toward downhill (degrees) and the fall rate (m/m) that earns all of it.</summary>
        public const float BiasMaxDeg = 25f;
        public const float SlopeFull = 0.15f;
        /// <summary>Mast to node match radius (m): the prop sits exactly on the node (sub-object offset 0).</summary>
        public const float MatchRadiusM = 0.6f;
        /// <summary>Rotation difference (degrees) below which the mast counts as already aimed (no write).</summary>
        public const float WriteToleranceDeg = 1.5f;
        private const float CuspMinLen = 0.25f;
        private const float MinEdgeM = 0.05f;

        public static float SignedArea2(float[] x, float[] z, int n)
        {
            double sum = 0.0;
            for (int i = 0; i < n; i++)
            {
                int j = i + 1 == n ? 0 : i + 1;
                sum += (double)x[i] * z[j] - (double)x[j] * z[i];
            }
            return (float)sum;
        }

        public static int NearestNode(float[] x, float[] z, int n, float px, float pz)
        {
            int best = -1;
            float bestD2 = MatchRadiusM * MatchRadiusM;
            for (int i = 0; i < n; i++)
            {
                float dx = x[i] - px, dz = z[i] - pz;
                float d2 = dx * dx + dz * dz;
                if (d2 <= bestD2)
                {
                    bestD2 = d2;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>The horizontal direction (unit, ax, az) the mast at node j must face; false when the ring is degenerate
        /// (fewer than 3 nodes, zero area, or both edges at j shorter than 5 cm).</summary>
        public static bool Aim(float[] x, float[] y, float[] z, int n, int j, out float ax, out float az)
        {
            ax = 0f;
            az = 0f;
            if (n < 3 || j < 0 || j >= n)
                return false;
            float area2 = SignedArea2(x, z, n);
            if (!(Math.Abs(area2) > 1e-3f))
                return false;
            bool ccw = area2 > 0f;
            int p = j == 0 ? n - 1 : j - 1;
            int q = j + 1 == n ? 0 : j + 1;
            float outX = x[q] - x[j], outZ = z[q] - z[j];
            float inX = x[j] - x[p], inZ = z[j] - z[p];
            float lenOut = (float)Math.Sqrt(outX * outX + outZ * outZ);
            float lenIn = (float)Math.Sqrt(inX * inX + inZ * inZ);
            bool haveOut = lenOut >= MinEdgeM, haveIn = lenIn >= MinEdgeM;
            if (!haveOut && !haveIn)
                return false;
            float oX = haveOut ? outX / lenOut : inX / lenIn, oZ = haveOut ? outZ / lenOut : inZ / lenIn;
            float iX = haveIn ? inX / lenIn : oX, iZ = haveIn ? inZ / lenIn : oZ;
            // Inward normals of the two edges, summed (miter); a cusp falls back to the outgoing edge's.
            float nOutX = ccw ? -oZ : oZ, nOutZ = ccw ? oX : -oX;
            float nInX = ccw ? -iZ : iZ, nInZ = ccw ? iX : -iX;
            float nX = nOutX + nInX, nZ = nOutZ + nInZ;
            float nLen = (float)Math.Sqrt(nX * nX + nZ * nZ);
            if (nLen < CuspMinLen)
            {
                nX = nOutX;
                nZ = nOutZ;
                nLen = 1f;
            }
            nX /= nLen;
            nZ /= nLen;
            // Fall rate along travel (positive: going along the border loses height), averaged over the two edges.
            float sOut = haveOut ? (y[j] - y[q]) / lenOut : 0f;
            float sIn = haveIn ? (y[p] - y[j]) / lenIn : 0f;
            float s = haveOut && haveIn ? 0.5f * (sOut + sIn) : haveOut ? sOut : sIn;
            float k = s / SlopeFull;
            k = k < -1f ? -1f : k > 1f ? 1f : k;
            float bias = k * (float)Math.Tan(BiasMaxDeg * Math.PI / 180.0);
            float tX = oX + iX, tZ = oZ + iZ;
            float tLen = (float)Math.Sqrt(tX * tX + tZ * tZ);
            if (tLen < CuspMinLen)
            {
                tX = oX;
                tZ = oZ;
                tLen = 1f;
            }
            tX /= tLen;
            tZ /= tLen;
            float rX = nX + bias * tX, rZ = nZ + bias * tZ;
            float rLen = (float)Math.Sqrt(rX * rX + rZ * rZ);
            if (!(rLen > 1e-6f))
                return false;
            ax = rX / rLen;
            az = rZ / rLen;
            return true;
        }

        /// <summary>The angle in degrees between two horizontal directions (0..180).</summary>
        public static float AngleDeg(float ax, float az, float bx, float bz)
        {
            double dot = (double)ax * bx + (double)az * bz;
            double cross = (double)ax * bz - (double)az * bx;
            return (float)(Math.Abs(Math.Atan2(cross, dot)) * 180.0 / Math.PI);
        }
    }
}
