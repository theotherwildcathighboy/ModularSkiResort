// SkiRunBindingRules - the bind decision table, one pure function used by both the load pass and the sweep of SkiRunOwnershipSystem
// so the two can never disagree about what a run's state means. tests/BindingBatch links this file and asserts every one of the
// 2^7 fact combinations, the priority order, idempotence and the money rules. Pure file: no usings (it compiles unchanged into the
// net48 mod and the net8 test host), no ECS, no Unity types, no allocation. SkiRunFees is pure too (System.Math written out).
//
// The table (first matching row wins):
//   1  HasOwner && OwnerIsOurSign                 MigrateLegacy  drop the sign's SubArea entry, remove Owner, binding {owner if
//                                                                live else Null, Paid, cost}; never charge
//   2  HasOwner && !OwnerIsOurSign                LeaveForeign   untouched, counted, never charged
//   3  HasBinding && GateLive                     KeepBound
//   4  HasBinding && !GateLive && SignInTether    RebindFree     m_Gate = nearest sign, Paid kept, "[rebind] ... no charge"
//   5  HasBinding && !GateLive                    Orphan         m_Gate = Null (if it was not), Paid kept, WARN once per run
//   6  !HasBinding && SignInTether && LoadPass    RestoreFree    binding {sign, Paid|Restored, 0}, "[restore]"
//   7  !HasBinding && SignInTether                BindCharge     binding {sign, Paid, cost} + Charge(cost) (refused before LoadPassDone)
//   8  otherwise                                  LeaveUnbound
//
// Facts (built by SkiRunOwnershipSystem): GateNull (m_Gate == Entity.Null, "never had a sign" vs "sign died"; steers only the
// effect of Orphan, Decide never reads it); GateLive (ownership.IsLiveGate: != Null, exists, not Deleted, PrefabRef in the sign
// prefab set); OwnerIsOurSign (the owner is a sign of ours or no longer exists: only our old sweep ever wrote Owner on a drawn
// run, vanilla never does in game mode, AreaToolSystem.cs:2105-2111 editor-only, 1.6.2 :2112-2118, so a dead owner on one of our
// runs is legacy; a live non-sign owner is foreign); SignInTether (a live sign lies within TetherM of the run's
// Geometry.m_CenterPosition.xz); LoadPass (the load pass in OnGameLoadingComplete; a paused SweepNow is not one: there an
// unbound run in tether is a fresh draw and pays).
//
// Never delete on an inferred event: no row deletes a run. A broken reference at load would otherwise wipe the resort. A paid
// orphan is the one exception, when the cascade was missed.

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

    /// <summary>The seven facts one decision reads (definitions in the file header)</summary>
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
