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
    /// rounded to the nearest RoundTo (half up). A non-positive or NaN area counts as 0 ha. Unknown kind -> -1.</summary>
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

        public static int BaseFee(int kind)
        {
            switch (kind)
            {
                case KindGroomed: return GroomedBase;
                case KindSnowmaking: return SnowmakingBase;
                case KindNight: return NightBase;
                case KindNatural: return NaturalBase;
                case KindCatTrack: return CatTrackBase;
                default: return -1;
            }
        }

        public static int PerHaFee(int kind)
        {
            switch (kind)
            {
                case KindGroomed: return GroomedPerHa;
                case KindSnowmaking: return SnowmakingPerHa;
                case KindNight: return NightPerHa;
                case KindNatural: return NaturalPerHa;
                case KindCatTrack: return CatTrackPerHa;
                default: return -1;
            }
        }

        public static int Fee(int kind, float areaM2)
        {
            int b = BaseFee(kind);
            if (b < 0)
                return -1;
            double ha = areaM2 > 0f ? areaM2 / 10000.0 : 0.0;   // NaN and negatives fail "> 0"
            if (ha > MaxAreaHa)
                ha = MaxAreaHa;                                  // +Infinity lands here too
            double raw = b + PerHaFee(kind) * ha;
            return (int)(System.Math.Floor(raw / RoundTo + 0.5) * RoundTo);
        }
    }
}
