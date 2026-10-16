// SkiRunBindingRules — THE bind decision table (docs/research/run-binding-glade-spec.md §3.3).
// One pure function used by BOTH the load pass and the sweep of SkiRunOwnershipSystem, so the two
// can never disagree about what a run's state means. tests/BindingBatch links this file and
// asserts every one of the 2^7 fact combinations, the priority order, idempotence and the
// money rules.
//
// PURE FILE: no usings at all (the spec allows `System` only) — it compiles unchanged into the
// net48 mod and the net8 test host. No ECS, no Unity types, no allocation.
//
// THE TABLE (first matching row wins):
//   1  HasOwner && OwnerIsOurSign                 MigrateLegacy  drop the sign's SubArea entry,
//                                                                remove Owner, binding {owner if
//                                                                live else Null, Paid, cost};
//                                                                NEVER charge
//   2  HasOwner && !OwnerIsOurSign                LeaveForeign   untouched, counted, never charged
//   3  HasBinding && GateLive                     KeepBound      —
//   4  HasBinding && !GateLive && SignInTether    RebindFree     m_Gate = nearest sign, Paid kept,
//                                                                "[rebind] … no charge"
//   5  HasBinding && !GateLive                    Orphan         m_Gate = Null (if it was not),
//                                                                Paid kept, WARN once per run
//   6  !HasBinding && SignInTether && LoadPass    RestoreFree    binding {sign, Paid|Restored, 0},
//                                                                "[restore]" (D6)
//   7  !HasBinding && SignInTether                BindCharge     binding {sign, Paid, cost} +
//                                                                Charge(cost) (refused before
//                                                                LoadPassDone)
//   8  otherwise                                  LeaveUnbound   —
//
// FACT DEFINITIONS (the caller builds them; SkiRunOwnershipSystem):
//   HasBinding     the run carries SkiRunBinding.
//   GateNull       SkiRunBinding.m_Gate == Entity.Null ("never had a sign" vs "sign died"). It
//                  steers only the EFFECT of Orphan (write Null + WARN only on the transition);
//                  Decide never reads it, so the action is identical either way.
//   GateLive       ownership.IsLiveGate(m_Gate): != Null, exists, not Deleted, PrefabRef in the
//                  sign prefab set.
//   HasOwner       the run carries Game.Common.Owner.
//   OwnerIsOurSign the owner is a sign of ours OR no longer exists (Null / !Exists): only our old
//                  sweep ever wrote Owner on a drawn run (vanilla never does in game mode,
//                  AreaToolSystem.cs:2105-2111 editor-only, 1.6.2 :2112-2118), so a dead owner
//                  on one of our runs is legacy; a LIVE non-sign owner is foreign.
//   SignInTether   a live sign lies within TetherM of the run's Geometry.m_CenterPosition.xz.
//   LoadPass       the caller is the load pass in OnGameLoadingComplete (a paused SweepNow is
//                  NOT a load pass: there an unbound run in tether is a fresh draw and pays).
//
// NEVER DELETE on an inferred event: no row deletes a run. A broken reference at load would
// otherwise wipe the resort (precedent SkiGladeCleanupSystem keeps orphaned trees). A paid
// orphan is the documented exception to DECISIONS 90 when the cascade was missed (D7).
//
// Created 2026-10-01 (Round O1, implementer I-1).
//
// Round E1 (2026-10-04, docs/research/econ-round/spec/ch2-runs.md §6.1, D21): SkiRunFees, the
// one-time run fee by plan area (base + per hectare, rounded to ₵10), appended verbatim by stage 0;
// still pure with no usings (System.Math written out). tests/BindingBatch asserts it.

namespace SkiResortMod.Systems
{
    /// <summary>What the load pass / sweep does with one run (§3.3). Not persisted.</summary>
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

    /// <summary>The seven facts one decision reads (definitions in the file header).</summary>
    public struct BindFacts
    {
        public bool HasBinding, GateNull, GateLive, HasOwner, OwnerIsOurSign, SignInTether, LoadPass;
    }

    public static class SkiRunBindingRules
    {
        /// <summary>The §3.3 table, first matching row wins. Pure and total: every one of the
        /// 2^7 fact combinations maps to exactly one action, inconsistent ones included
        /// (e.g. GateLive without HasBinding is ignored, because rows 3-5 test HasBinding
        /// first).</summary>
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

    /// <summary>Round E1 (D21): the one-time run fee by plan area. Pure; linked into tests/BindingBatch.
    /// fee = base + perHa × ha, ha = clamp(areaM2 / 10,000, 0, MaxAreaHa), rounded to the nearest
    /// RoundTo (half up). A non-positive or NaN area counts as 0 ha. Unknown kind → −1.</summary>
    public static class SkiRunFees
    {
        /// <summary>= RunKind values (BindingBatch asserts the equality).</summary>
        public const int KindGroomed = 0, KindSnowmaking = 1, KindNight = 2, KindNatural = 3, KindCatTrack = 4;

        public const int NaturalBase = 500, NaturalPerHa = 250;
        public const int GroomedBase = 2000, GroomedPerHa = 1000;
        // T54 probe 1 (cat-track-run.md §2, D1 pending): the cat track pays the groomed fee by plan area.
        public const int CatTrackBase = GroomedBase, CatTrackPerHa = GroomedPerHa;
        public const int SnowmakingBase = 4000, SnowmakingPerHa = 4000;
        public const int NightBase = 6000, NightPerHa = 7000;

        /// <summary>Area cap (int-overflow guard; 2,000 ha = 20 km²): night tops out at ₵14,006,000.</summary>
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
