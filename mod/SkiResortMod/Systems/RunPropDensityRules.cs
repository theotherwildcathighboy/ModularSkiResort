// RunPropDensityRules — the PURE part of the Options prop density (Round M2 option 5, ruling R8;
// docs/research/round-m2/understand/density.md §2.1 with its check density-check.md, build-contract.md §6.9 / §7 B5).
// SkiRunPropSystem calls these on the main thread; tests/ResortBatch DensityTests checks them with literal values
// (sections D1-D8 = density.md §3.1 A-H). Written 2026-10-10 (Round M2, builder B5).
//
// LOOK ONLY. Two Options sliders, "Snow guns and hydrants" (snowmaking AND night runs) and "Floodlight masts on night
// runs", 0-100 % step 5, default 100. They change how many corners of our propped runs carry a prop; snow (a per-texel
// line by run type) and upkeep (per active hectare by run type, SkiRunUpkeepSystem) never read them.
//
// WHAT IT MIRRORS. Game.Objects/SecondaryObjectSystem.cs (1.6.0f1; the 1.6.2f1 diff changes only :3499 and :3617, no
// shift, so these lines hold for both), the group branch of CreateSecondaryObject, one area corner:
//   * CreateSecondaryAreaObjects passes the run prefab entry's Game.Prefabs.SubObject.m_Probability on as the chance
//     (:3004); AreaInitializeSystem sets it to 100 once, when the prefab entity is created
//     (Game.Prefabs/AreaInitializeSystem.cs:398, fill :384-404; unchanged in 1.6.2).
//   * A group with m_RandomizeGroupIndex draws a fresh group index per corner (:3075-3077). Each member: the stream
//     is copied (:3243) and advanced twice (:3244-3245); the first member takes the slot (:3251-3259); every later one,
//     all tied at score 0 (no traffic data on a run), takes it when random.NextInt(running weight sum) < its weight
//     (:3260-3271), the weight being the member's SpawnableObjectData.m_Probability read live (:3258, :3262). A zero
//     sum returns (:3273-3276). Then the roll random3.NextInt(100) < chance on the PICKED member's copy (:3279), and
//     the object's seed is the next draw on that copy (:3392-3393).
//   So a corner shows member X with probability chance/100 × w_X / W' (W' = the group's current weight sum): the pick
//   is weight-proportional and the roll is independent of it.
//
// FORMULAS (g, m = the clamped sliders; round(a / b) = (2a + b) / (2b) in long, half up, a >= 0, b > 0):
//   SnowmakingChance = round(base × g / 100), 0..100. The snowmaking group's weights stay as they are while g > 0, so
//     guns+hydrants show on chance × 75 % of corners (110 + 40 of 200) — pure thinning, corner by corner.
//   NightChance = round(base × (g × NM + m × M) / (100 × (NM + M))), 0..100; 0 when NM + M <= 0 or g = m = 0.
//     NM = base weight of the night group's SHARED members (guns, hydrants, empty marker: also in the snowmaking group),
//     M = base weight of its OWN members (the masts).
//   MastWeight = 0 when base <= 0 or m = 0; base when g = 0; else max(1, round(base × m / g)).
//   NonMastWeight = 0 when g = 0, else base.
//   Why exact: shared weights kept and masts scaled by r = m / g give W' = NM + rM, so chance g (NM + rM) / (NM + M)
//   shows each shared member on g × w_X / (NM + M) and each mast on m × w_X / (NM + M): today's share times its slider.
//   At g = 0 the shared weights go to 0 (snowmaking runs then have chance 0, so shared members show nowhere) and the
//   chance is m × M / (NM + M). With m = g: r = 1, chance g, pure thinning. Integer rounding keeps every tabled
//   setting within 0.3 pp of the target (density-check C5). With g ≠ m night corners re-pick (same shares, other
//   corners); the same settings give the same layout for the same group member order (density-check C4: the order is
//   the prefab-init order, Game.Prefabs/ObjectInitializeSystem.cs:900-905, 1.6.2 :910-915).
//   NextStable / ApplyDue: the settle rule (an apply once the pair has been still for SettleFrames updates, or forced
//   at a game load). ExpectedTenths / ClassOf: the census log line only; the math above uses group membership, never
//   names.
//
// PURE FILE: `using System;` only (no Game.dll, no Unity types); compiled into the mod by the glob and linked into
// tests/ResortBatch. C# 9 / net48: no Math.Clamp.

using System;

namespace SkiResortMod.Systems
{
    /// <summary>Options prop density rules (header).</summary>
    public static class RunPropDensityRules
    {
        /// <summary>The two Options sliders: 0-100 % in steps of 5, default 100 (= today's look).</summary>
        public const int PctMin = 0, PctMax = 100, PctStep = 5, PctDefault = 100;

        /// <summary>OnUpdate frames the slider pair must stay still before an apply (R8: "30 frames after a slider
        /// settles"), so a drag writes once.</summary>
        public const int SettleFrames = 30;

        /// <summary>Census classes of a run prop (log lines only).</summary>
        public const byte ClassOther = 0, ClassGun = 1, ClassHydrant = 2, ClassMast = 3, ClassEmpty = 4;

        /// <summary>The run prefab chance at full look (AreaInitializeSystem.cs:398) and the roll's bound (:3279).</summary>
        private const int ChanceMax = 100;

        /// <summary>The empty marker's prefab name (mesh-less SkiPropNone) and the hydrant prefix (SkiHydrantBox*).</summary>
        private const string EmptyMarkerName = "SkiPropNone";
        private const string HydrantPrefix = "SkiHydrant";

        /// <summary>The slider clamped to PctMin..PctMax.</summary>
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

        /// <summary>The night run prefab's per-corner chance (header): round(base × (g × NM + m × M) / (100 × (NM + M))),
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

        /// <summary>The settle counter: 0 on a change, else one more, capped at SettleFrames.</summary>
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

        /// <summary>round(a / b), half up, for a &gt;= 0 and b &gt; 0.</summary>
        private static long RoundDiv(long a, long b)
        {
            return (2 * a + b) / (2 * b);
        }

        private static int ClampChance(long v)
        {
            return v < 0 ? 0 : v > ChanceMax ? ChanceMax : (int)v;
        }
    }
}
