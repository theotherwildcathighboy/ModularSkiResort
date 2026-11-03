// SkiRunBindingRules - the bind decision table, one pure function used by both the load pass and the sweep of SkiRunOwnershipSystem
// so the two can never disagree about what a run's state means. tests/BindingBatch asserts every one of the 2^7 fact combinations.
// Pure file: no usings (it compiles unchanged into the net48 mod and the net8 test host), no ECS, no Unity types, no allocation.
// Never delete on an inferred event: no row deletes a run (a broken reference at load would wipe the resort). A paid orphan is the
// one exception, when the cascade was missed.
// Engine notes: EngineNotes/SkiRunBindingRules.md

namespace SkiResortMod.Systems
{
    /// <summary>What the load pass / sweep does with one run. Not persisted.</summary>
    public enum BindAction : byte
    {
        KeepBound,
        MigrateLegacy,
        LeaveForeign,
        Orphan,
        RebindFree,
        RestoreFree,
        BindCharge,
        LeaveUnbound,
    }

    /// <summary>The seven facts one decision reads (definitions in the engine notes)</summary>
    public struct BindFacts
    {
        public bool HasBinding, GateNull, GateLive, HasOwner, OwnerIsOurSign, SignInTether, LoadPass;
    }

    public static class SkiRunBindingRules
    {
        /// <summary>The table above, first matching row wins. Pure and total: every one of the 2^7 fact combinations maps to
        /// exactly one action, inconsistent ones included (GateLive without HasBinding is ignored, because rows 3-5 test
        /// HasBinding first).</summary>
        public static BindAction Decide(in BindFacts f)
        {
            if (f.HasOwner)
                return f.OwnerIsOurSign ? BindAction.MigrateLegacy      // row 1
                                        : BindAction.LeaveForeign;      // row 2
            if (f.HasBinding)
            {
                if (f.GateLive)
                    return BindAction.KeepBound;                         // row 3
                return f.SignInTether ? BindAction.RebindFree            // row 4
                                      : BindAction.Orphan;               // row 5
            }
            if (f.SignInTether)
                return f.LoadPass ? BindAction.RestoreFree               // row 6
                                  : BindAction.BindCharge;               // row 7
            return BindAction.LeaveUnbound;                              // row 8
        }
    }

    /// <summary>The one-time run fee by plan area: fee = base + perHa x ha, ha = clamp(areaM2 / 10,000, 0, MaxAreaHa),
    /// rounded to the nearest RoundTo (half up). A non-positive or NaN area counts as 0 ha. Unknown kind -> -1. The int overloads
    /// wrap the RunFeatures ones (RunFeatures.FromKind).</summary>
    public static class SkiRunFees
    {
        /// <summary>= RunKind values (BindingBatch asserts the equality)</summary>
        public const int KindGroomed = 0, KindSnowmaking = 1, KindNight = 2, KindNatural = 3, KindCatTrack = 4;

        public const int NaturalBase = 500, NaturalPerHa = 250;
        public const int GroomedBase = 2000, GroomedPerHa = 1000;
        // The cat track pays the groomed fee by plan area
        public const int CatTrackBase = GroomedBase, CatTrackPerHa = GroomedPerHa;
        public const int SnowmakingBase = 4000, SnowmakingPerHa = 4000;
        public const int NightBase = 6000, NightPerHa = 7000;

        /// <summary>Area cap (int-overflow guard; 2,000 ha = 20 km2): night tops out at 14,006,000</summary>
        public const double MaxAreaHa = 2000.0;
        public const int RoundTo = 10;

        // The feature steps (DECISIONS 576): a groomed piste pays the groomed fee, snowmaking adds its step, lights add theirs, so
        // groomed + snowmaking = SnowmakingBase / PerHa and + lights = NightBase / PerHa. Base and per-ha parts are summed as ints before
        // the one rounding, so the totals are exact.
        public const int SnowmakingStepBase = SnowmakingBase - GroomedBase, SnowmakingStepPerHa = SnowmakingPerHa - GroomedPerHa;
        public const int LightsStepBase = NightBase - SnowmakingBase, LightsStepPerHa = NightPerHa - SnowmakingPerHa;

        /// <summary>Unknown: -1. Cat track: CatTrackBase (its guns and lights carry no fee). Natural: NaturalBase. Groomed piste:
        /// GroomedBase plus the snowmaking and lights steps it has.</summary>
        public static int BaseFee(RunFeatures f)
        {
            if (!f.Known)
                return -1;
            if (f.IsCatTrack)
                return CatTrackBase;
            if (!f.Groomed)
                return NaturalBase;
            return GroomedBase + (f.Snowmaking ? SnowmakingStepBase : 0) + (f.Lights ? LightsStepBase : 0);
        }

        /// <summary>Per hectare, the same cases as <see cref="BaseFee(RunFeatures)"/>.</summary>
        public static int PerHaFee(RunFeatures f)
        {
            if (!f.Known)
                return -1;
            if (f.IsCatTrack)
                return CatTrackPerHa;
            if (!f.Groomed)
                return NaturalPerHa;
            return GroomedPerHa + (f.Snowmaking ? SnowmakingStepPerHa : 0) + (f.Lights ? LightsStepPerHa : 0);
        }

        public static int Fee(RunFeatures f, float areaM2)
        {
            int b = BaseFee(f);
            if (b < 0)
                return -1;
            double ha = areaM2 > 0f ? areaM2 / 10000.0 : 0.0;   // NaN and negatives fail "> 0"
            if (ha > MaxAreaHa)
                ha = MaxAreaHa;                                  // +Infinity lands here too
            double raw = b + PerHaFee(f) * ha;
            return (int)(System.Math.Floor(raw / RoundTo + 0.5) * RoundTo);
        }

        public static int BaseFee(int kind) => BaseFee(RunFeatures.FromKind(kind));

        public static int PerHaFee(int kind) => PerHaFee(RunFeatures.FromKind(kind));

        public static int Fee(int kind, float areaM2) => Fee(RunFeatures.FromKind(kind), areaM2);
    }

    /// <summary>The monthly run upkeep per billed hectare as feature steps (DECISIONS 576): a groomed run (piste or cat track) pays
    /// GroomedPerHaMonth, snowmaking adds its step, lights add theirs; natural and unknown pay 0. So groomed 150, snowmaking 500, night 800,
    /// and a cat track 500 with its guns on (SkiRunUpkeepSystem bills a cat track only while its guns run). SkiRunUpkeepSystem.Tuning's
    /// totals are derived from these.</summary>
    public static class SkiRunUpkeepRates
    {
        public const int GroomedPerHaMonth = 150, SnowmakingStepPerHaMonth = 350, LightsStepPerHaMonth = 300;

        public static int PerHaMonth(RunFeatures f)
        {
            if (!f.Groomed)   // natural and unknown (Unknown is never groomed)
                return 0;
            return GroomedPerHaMonth + (f.Snowmaking ? SnowmakingStepPerHaMonth : 0) + (f.Lights ? LightsStepPerHaMonth : 0);
        }
    }
}
