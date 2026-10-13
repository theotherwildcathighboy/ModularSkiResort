// LiftServedRule — THE lift-served rule and THE power predicate of Round E1 (decisions D5, D6;
// docs/research/econ-round/spec/ch2-runs.md §3.1-§3.3; e1-spec R1, R2). The rule is
// lifts-in-economy.md §5.2 R2 as corrected by its Verification V.1 #9 (the census keeps edge -> row
// only, so the line is a set of CHORDS, not an ordered polyline) and #16 (a complete row may carry a
// spur). Consumers: SkiResortAttractivenessSystem (served runs, lift value), SkiResortStatsSystem
// (servedBy, runsServed), SkiLiftChargeSystem and SkiLiftTicketSystem (IsPowered), via
// LiftCensusSystem.CopyServeLifts / ReadLiftPower; tests/LiftBatch and tests/ResortBatch link it.
//
// THE RULE. A run is served by lift L when all of these hold:
//   1. L's census row is Complete (Open / Unfinished / StationsOnly serve nothing: a section being
//      redrawn drops service until the line closes — D6 accepts this);
//   2. IsPowered(L.Power): not NoPower — NoPower while the game's electricity warning shows and under
//      half of the wanted power arrives (back at three quarters; Round S1 F2, POWER below);
//   3. the run's top T (RunFacts.TopNode, the highest outline node) satisfies
//      T.y <= TopY(L) + TopMarginM, TopY = the top station's stub-tip y;
//   4. the xz distance from T to the nearest chord of L (each SkiChairliftHV line edge,
//      Edge.StartPos.xz -> Edge.EndPos.xz) is <= ServeRadiusM.
//   Both bounds are INCLUSIVE. A run's serving lift is the one with the smallest distance; an exact
//   tie goes to the lower EntityRef (Index, then Version). Applies to all four run kinds.
//   CHORDS, NOT CURVES: a curved section is measured along its straight chord (curved sections: the
//   error is UNVERIFIED; field step 6 puts a run top 140-160 m from a curve).
//
// POWER (R1). PowerOf maps ElectricityConsumer.m_Flags (Game.Buildings/ElectricityConsumerFlags.cs:5-12)
// to NoPower exactly when the game draws its electricity icon (NoElectricityWarning or
// BottleneckWarning, DispatchElectricitySystem.cs:126-158). IsPowered is the ONLY "powered" predicate
// in the mod (service, lift value, billing; the ticket gate no longer reads it since Round M2 T1); PowerGatesService = false turns power into
// status only everywhere (one switch). Round S1 F2 (ski-days spec ch1 §5; s1-spec N7): the census reads
// the 4-argument PowerOf — NoPower only while the warning shows AND under half of the wanted power
// arrives, back at three quarters or when the warning clears (hysteresis); the game warns at any
// shortfall after two short dispatches (:126-142), and a 9 % city shortfall switched FT8's lifts off.
//
// PURE FILE: `using System;`, `using System.Collections.Generic;`, `using Unity.Mathematics;` and
// EntityRef only (the test-host rule), + SkiCatalog consts (Catalog Phase A, 2026-10-11: ServeRadiusM and
// TopMarginM alias SkiCatalog.FixedGrip, compile-time literals; SkiCatalog.cs is linked wherever this file is —
// tests/LiftBatch, tests/ResortBatch). No state; Assign is allocation-free once its lists have grown.
//
// Created 2026-10-04 (Round E1 stage 0, S0: ch2 §3.2 as edited, verbatim); owned by stream S2.
//
// CATALOG PHASE B (2026-10-11, docs/research/art/assets-2026-10/code-round-2/round-2-contract.md §6.3, lift group) — the
// rule per lift TYPE. Lift gains Rule (ServeRuleKind as a byte), RadiusM, TopMarginM and TopXZ (the top station's HV
// stub tip xz, NaN when the top is not in the last build), filled by LiftCensusSystem.CopyServeLifts from the row's
// SkiCatalog.Lifts entry:
//   * AlongLine (fixed-grip, detachable, gondola: radius 150, margin 15): rules 3-4 above with the row's numbers;
//   * TerminalsOnly (tram: radius 300, margin 15): the run top within RadiusM (xz, inclusive) of TopXZ and
//     T.y ≤ TopY + TopMarginM — a tram serves the slopes at its top station, not along its span; distanceM = the xz
//     distance to TopXZ.
// STRUCT-DEFAULT COMPATIBILITY (deviation from the contract's "radius 0 serves nothing"): a Lift built without the new
// fields (every Phase A constructor, tests/LiftBatch rows 1-26) reads RadiusM ≤ 0 as SkiCatalog.FixedGrip.ServeRadiusM
// and TopMarginM ≤ 0 as FixedGrip.TopMarginM, so the old oracles keep their meaning. A Mixed or untyped census row serves
// nothing because it is never Complete (LiftCensus: a mixed component is Unfinished), not through its radius.
// SummitServed (seam S2, R11 / D11): a lodge is summit-served when its xz lies within radiusM (inclusive) of TopXZ of a
// lift that Serves (complete, powered, chords > 0, TopY finite) — the caller passes the lifts of the lodge's ski area.
//
// PHASE D (2026-10-11, docs/research/art/assets-2026-10/code-round-4/round-4-contract.md §1.2-§1.3, D1-D3, with
// round-4-contract-skeptic.md MUST-4 and S-7; rules group). Richard's re-ruling, DECISIONS 171: an unpowered lift of every
// type carries no guests, does nothing for the resort and its carriers stop; a lift is powered from its BASE station only
// (the top is fed through the modelled lift power line and is never read). Additive: nothing above changes.
//   * Carries(complete, power, closed) — THE run predicate: complete AND IsPowered(power) AND NOT closed. The new call
//     sites use it (ChairPin's hold gate through LiftLoopHold.GateOf, the hub's capacity through HubRules.LiftCapacityShown,
//     the tram's Held gate). The older equivalent expressions (Lift.Serves' power factor above, SkiMoneyRules.LiftOperating,
//     the score's Complete && Powered) stay as written; tests pin the equivalence over every input (LiftBatch PG-1,
//     ResortBatch HK-3). The bool overload takes the hub's shown power (Power != NoPower). "closed" is wired false at
//     every call site this round: the pre-beta "chairs stop when closed" item computes it later and passes it here.
//   * GuardPower — the flicker guard, applied by the census's one power reader (LiftCensusSystem.ReadLiftPower) to
//     PowerOf's result: NoPower at once (the game already waits two short dispatches before its warning,
//     DispatchElectricitySystem.cs:126-142, kAlertCooldown); Powered again only after PowerBackHoldFrames (512) sim frames
//     of raw Powered reads (SimulationSystem.frameIndex, SimulationSystem.cs:68; a dispatch every 128 frames at offset 126,
//     DispatchElectricitySystem.cs:222-230). A station whose effective power was never NoPower is passed through
//     untouched (lastEffective != NoPower → raw). IDENTITY holds per consumer-state PATH, not per lift (skeptic MUST-4):
//     a reader polling every 64 frames can see a one-dispatch (128-frame) "warning and under half" window that today's
//     256-frame pollers may miss; that lift then reads NoPower for at least 512 frames (LiftBatch PG-2 h). "Powered" for the
//     identity claim = the base never shows the warning with under half of its wanted power arriving.
//   * ExpireStreak (skeptic S-7, for the caller that tracks its last read frame per station): a Powered streak whose
//     station went unread for more than PowerStreakStaleFrames is dropped, so a lift redrawn mid-streak waits a fresh 512.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>A lift's power from its base station's ElectricityConsumer (§3.3; Round S1 F2: the 4-argument
    /// PowerOf).</summary>
    public enum LiftPower : byte
    {
        Unknown = 0,   // no base station in the build, not complete, or no ElectricityConsumer: treated as powered
        Powered = 1,   // no electricity warning, or at least half of the wanted power arrives (3/4 when coming back)
        NoPower = 2,   // the game's NoElectricity or Bottleneck warning and under half of the wanted power
    }

    public static class LiftServedRule
    {
        // Catalog Phase A (ext row 12; was LiftServedRule.cs:54-55 = 150f / 15f): the lift type's numbers (D5), one
        // source in SkiCatalog.FixedGrip; tests/ResortBatch CatalogGuardTests CG-1 pins alias == row == const.
        public const float ServeRadiusM = SkiCatalog.FixedGrip.ServeRadiusM;
        public const float TopMarginM = SkiCatalog.FixedGrip.TopMarginM;

        /// <summary>One-value reversal to "power is status only" (§3.3): false = a NoPower lift still
        /// serves. static readonly, not const, so the compiler folds nothing and warns about nothing.</summary>
        public static readonly bool PowerGatesService = true;

        /// <summary>= Game.Buildings.ElectricityConsumerFlags (ElectricityConsumerFlags.cs:5-12).</summary>
        public const byte FlagConnected = 1, FlagNoElectricityWarning = 2, FlagBottleneckWarning = 4;

        public struct Chord
        {
            public float2 A, B;   // xz
        }

        public struct Lift
        {
            public EntityRef Id;          // census Row.Id (the live base, else the live top, else an edge)
            public EntityRef Base, Top;   // census Row.Base / Row.Top
            public bool Complete;         // census State == Complete
            public LiftPower Power;       // Unknown unless Complete
            public float TopY;            // top station stub-tip y; NaN when the top station is not in the last build
            public int FirstChord;        // slice of the chord list
            public int ChordCount;
            // Catalog Phase B (round-2-contract §6.3; header CATALOG PHASE B): the row's lift type's rule.
            public byte Rule;             // (byte)ServeRuleKind: 0 AlongLine (default), 1 TerminalsOnly
            public float RadiusM;         // ≤ 0 (struct default) = SkiCatalog.FixedGrip.ServeRadiusM
            public float TopMarginM;      // ≤ 0 (struct default) = SkiCatalog.FixedGrip.TopMarginM
            public float2 TopXZ;          // the top station's HV stub tip xz; NaN when the top is not in the last build

            public bool Serves =>
                Complete && ChordCount > 0 && !float.IsNaN(TopY) && IsPowered(Power);

            /// <summary>The radius the rule uses: RadiusM when &gt; 0, else the fixed-grip ServeRadiusM.</summary>
            public float EffectiveRadiusM => RadiusM > 0f ? RadiusM : ServeRadiusM;

            /// <summary>The top margin the rule uses: TopMarginM when &gt; 0, else the fixed-grip TopMarginM.</summary>
            public float EffectiveTopMarginM => TopMarginM > 0f ? TopMarginM : LiftServedRule.TopMarginM;
        }

        /// <summary>The game's electricity warning on the consumer ⇒ NoPower; else Powered.</summary>
        public static LiftPower PowerOf(byte consumerFlags)
        {
            return (consumerFlags & (FlagNoElectricityWarning | FlagBottleneckWarning)) != 0
                ? LiftPower.NoPower
                : LiftPower.Powered;
        }

        /// <summary>F2 (ski-days spec ch1 §5): 2 × fulfilled &lt; wanted → under half (NoPower); a NoPower lift
        /// is back at 4 × fulfilled ≥ 3 × wanted (hysteresis, refutation R-4).</summary>
        public const int HalfPowerDivisor = 2, BackPowerNumerator = 3, BackPowerDenominator = 4;

        /// <summary>F2 (ski-days spec ch1 §5; s1-spec §2.3, N7): NoPower only when the game shows its electricity
        /// warning AND under half of the wanted power arrives (2 × fulfilled &lt; wanted); a NoPower lift comes back
        /// when the warning clears or at least three quarters arrive (4 × fulfilled ≥ 3 × wanted). Exact in long;
        /// wanted ≤ 0 → Powered. Before electricity unlocks the game sets no warning (DispatchElectricitySystem.cs:61,
        /// :106-109) and fulfilled may be 0, so the warning test comes first (ch1 §11 DV-2). Fields
        /// ElectricityConsumer.m_WantedConsumption / m_FulfilledConsumption (ElectricityConsumer.cs:8-10 (=));
        /// fulfilled = min(flow share × wanted, wanted) (DispatchElectricitySystem.cs:105 (=)). The caller passes
        /// the station's last value (Unknown on the first read). The 1-argument overload stays (tests/LiftBatch
        /// rows 23-24).</summary>
        public static LiftPower PowerOf(byte consumerFlags, int wantedConsumption, int fulfilledConsumption, LiftPower last)
        {
            if ((consumerFlags & (FlagNoElectricityWarning | FlagBottleneckWarning)) == 0)
                return LiftPower.Powered;
            if (wantedConsumption <= 0)
                return LiftPower.Powered;
            if (last == LiftPower.NoPower)
                return (long)fulfilledConsumption * BackPowerDenominator >= (long)wantedConsumption * BackPowerNumerator
                    ? LiftPower.Powered : LiftPower.NoPower;
            return (long)fulfilledConsumption * HalfPowerDivisor >= wantedConsumption ? LiftPower.Powered : LiftPower.NoPower;
        }

        /// <summary>E1 master contract R1: THE "powered" predicate for service (D6), lift value (D6),
        /// lift billing (D23) and the lift charge's operating count (the ticket gate no longer reads lifts since Round M2 T1). Unknown counts as powered.
        /// With PowerGatesService false, power is status only everywhere (one switch).</summary>
        public static bool IsPowered(LiftPower power)
        {
            return !PowerGatesService || power != LiftPower.NoPower;
        }

        // ── Phase D (round-4-contract §1.3, D1, D3; header PHASE D): the flicker guard and THE run predicate ──────────

        /// <summary>Phase D (round-4-contract §1.3, D3): sim frames of raw Powered reads before a NoPower lift counts as
        /// powered again — 4 dispatch periods (DispatchElectricitySystem.cs:222-230: every 128 frames, offset 126), 8.5 s at
        /// 1× (60 sim frames a second, SimulationSystem.cs:238), about 2.8 game minutes (262,144 frames a day,
        /// TimeSystem.cs:18). Absorbs the R15 brownout pattern (the warning on for 128 of every 384 frames,
        /// DispatchElectricitySystem.cs:126-142). Richard's default, DECISIONS 171: 512.</summary>
        public const uint PowerBackHoldFrames = 512u;

        /// <summary>Phase D: no Powered streak in progress (GuardPower's backSince). A streak that starts on frame
        /// 2³² − 1 itself (about 2.3 years of play at 60 frames a second) restarts at the next read: one read of delay
        /// (risk R-12).</summary>
        public const uint NoBackSince = uint.MaxValue;

        /// <summary>Phase D (skeptic S-7): a streak whose station was not read for more than this many sim frames is stale
        /// (2 × PowerBackHoldFrames = 1,024). Every Complete row is read at least every 256 frames (the attraction's and
        /// the charge's power polls), so a gap this long means the row was not Complete, or not read, meanwhile.</summary>
        public const uint PowerStreakStaleFrames = 2u * PowerBackHoldFrames;

        /// <summary>Phase D (round-4-contract §1.3): the effective power after one read. raw = PowerOf(4 arguments) of this
        /// read, computed with the station's last EFFECTIVE value; lastEffective = that value. NoPower → Powered waits until
        /// raw has read Powered for PowerBackHoldFrames (backSince = the first frame of the current Powered streak; elapsed
        /// by unchecked uint subtraction, so the frame counter may wrap). Every other transition is immediate. raw NoPower
        /// ends a streak (recut = true when one was running); raw Unknown passes through and clears the streak. A station
        /// whose lastEffective is Powered or Unknown gets raw back unchanged (the powered-lift identity, §6). Allocation-free,
        /// Burst-safe, no state of its own (the caller keeps backSince per station).</summary>
        public static LiftPower GuardPower(LiftPower raw, LiftPower lastEffective, ref uint backSince, uint nowFrame, out bool recut)
        {
            recut = false;
            if (raw == LiftPower.NoPower)
            {
                recut = backSince != NoBackSince;
                backSince = NoBackSince;
                return LiftPower.NoPower;
            }
            if (raw != LiftPower.Powered || lastEffective != LiftPower.NoPower)
            {
                backSince = NoBackSince;
                return raw;
            }
            if (backSince == NoBackSince)
                backSince = nowFrame;
            if (unchecked(nowFrame - backSince) >= PowerBackHoldFrames)
            {
                backSince = NoBackSince;
                return LiftPower.Powered;
            }
            return LiftPower.NoPower;
        }

        /// <summary>Phase D (skeptic S-7): backSince, or NoBackSince when the station was last read more than
        /// PowerStreakStaleFrames before <paramref name="nowFrame"/> (unchecked uint subtraction). Call it before GuardPower
        /// with the station's previous read frame; a caller that does not track read frames skips it (fail open: a streak
        /// that started before a section redraw may then count across the gap).</summary>
        public static uint ExpireStreak(uint backSince, uint lastReadFrame, uint nowFrame)
        {
            return backSince != NoBackSince && unchecked(nowFrame - lastReadFrame) > PowerStreakStaleFrames ? NoBackSince : backSince;
        }

        /// <summary>Phase D (round-4-contract D1): THE run predicate — a lift carries guests (counts, moves, has capacity)
        /// when it is complete, powered (IsPowered: Unknown counts, PowerGatesService false ignores power) and not closed.
        /// = SkiMoneyRules.LiftOperating(complete, IsPowered(power)) &amp;&amp; !closed (ResortBatch HK-3), and Lift.Serves' power
        /// factor (LiftBatch PG-1).</summary>
        public static bool Carries(bool complete, LiftPower power, bool closed)
        {
            return complete && IsPowered(power) && !closed;
        }

        /// <summary>Phase D: Carries for a caller that holds the status's bool (the hub: shownPowered = Power != NoPower,
        /// SkiResortAttractivenessSystem's LiftScoreView.Powered or the hub's own CopyServeLifts read).</summary>
        public static bool Carries(bool complete, bool shownPowered, bool closed)
        {
            return complete && (!PowerGatesService || shownPowered) && !closed;
        }

        /// <summary>Squared xz distance from p to segment ab (a degenerate segment is the point a).</summary>
        public static float DistanceSqXZ(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float len2 = math.dot(ab, ab);
            float t = len2 > 0f ? math.saturate(math.dot(p - a, ab) / len2) : 0f;
            return math.lengthsq(p - (a + t * ab));
        }

        /// <summary>Does <paramref name="lift"/> serve a run whose highest outline node is
        /// <paramref name="runTop"/>? <paramref name="distanceM"/> = xz distance to its nearest chord
        /// (+inf when false).</summary>
        public static bool Reaches(float3 runTop, in Lift lift, List<Chord> chords, out float distanceM)
        {
            distanceM = float.PositiveInfinity;
            // Catalog Phase B (round-2-contract §6.3; was `runTop.y > lift.TopY + TopMarginM` and `ServeRadiusM`, the
            // fixed-grip consts, LiftServedRule.cs:127 / :137): the row's own margin and radius.
            float radius = lift.EffectiveRadiusM;
            if (!lift.Serves || !math.all(math.isfinite(runTop)) || runTop.y > lift.TopY + lift.EffectiveTopMarginM)
                return false;
            float2 p = runTop.xz;
            if (lift.Rule == (byte)ServeRuleKind.TerminalsOnly)
            {
                // Catalog Phase B: a tram serves the run tops around its top station only (inclusive radius).
                if (!math.all(math.isfinite(lift.TopXZ)))
                    return false;
                float t2 = math.lengthsq(p - lift.TopXZ);
                if (!(t2 <= radius * radius))
                    return false;
                distanceM = math.sqrt(t2);
                return true;
            }
            float best = float.PositiveInfinity;
            int end = lift.FirstChord + lift.ChordCount;
            for (int c = lift.FirstChord; c < end; c++)
            {
                float d2 = DistanceSqXZ(p, chords[c].A, chords[c].B);
                if (d2 < best)
                    best = d2;
            }
            if (!(best <= radius * radius))
                return false;
            distanceM = math.sqrt(best);
            return true;
        }

        /// <summary>Catalog Phase B seam S2 (round-2-contract §6.3, R11 / D11): a lodge at <paramref name="lodgeXz"/> is
        /// summit-served when any lift of <paramref name="lifts"/> Serves (complete, powered, chords &gt; 0, TopY finite)
        /// and its TopXZ lies within <paramref name="radiusM"/> (inclusive) of the lodge. The caller passes the lifts of
        /// the lodge's ski area (SkiCatalog.Ladder.SummitServedRadiusM = 300). A non-finite lodge position, an empty or
        /// null list, a NaN TopXZ (top not in the build) or a radius that is not &gt;= 0 → false. Allocation-free.</summary>
        public static bool SummitServed(float2 lodgeXz, List<Lift> lifts, float radiusM)
        {
            if (lifts == null || !math.all(math.isfinite(lodgeXz)) || !(radiusM >= 0f))
                return false;
            float r2 = radiusM * radiusM;
            for (int l = 0; l < lifts.Count; l++)
            {
                Lift lift = lifts[l];
                if (!lift.Serves || !math.all(math.isfinite(lift.TopXZ)))
                    continue;
                if (math.lengthsq(lodgeXz - lift.TopXZ) <= r2)
                    return true;
            }
            return false;
        }

        /// <summary>Index of the serving lift (smallest distance, tie → lower Id), or −1.</summary>
        public static int ServingLift(float3 runTop, List<Lift> lifts, List<Chord> chords, out float distanceM)
        {
            int best = -1;
            distanceM = float.PositiveInfinity;
            for (int l = 0; l < lifts.Count; l++)
            {
                if (!Reaches(runTop, lifts[l], chords, out float d))
                    continue;
                if (best < 0 || d < distanceM || (d == distanceM && lifts[l].Id.CompareTo(lifts[best].Id) < 0))
                {
                    best = l;
                    distanceM = d;
                }
            }
            return best;
        }

        /// <summary>For every run top: its serving lift (or −1) and the distance; for every lift: how
        /// many runs it serves (a run within reach of two lifts counts for both — the "serves N runs"
        /// reverse map). Output lists are cleared and filled; allocation-free once grown.</summary>
        public static void Assign(List<float3> runTops, List<Lift> lifts, List<Chord> chords,
            List<int> servingLift, List<float> servingDistanceM, List<int> servedCount)
        {
            servingLift.Clear();
            servingDistanceM.Clear();
            servedCount.Clear();
            for (int l = 0; l < lifts.Count; l++)
                servedCount.Add(0);
            for (int r = 0; r < runTops.Count; r++)
            {
                int best = -1;
                float bestD = float.PositiveInfinity;
                for (int l = 0; l < lifts.Count; l++)
                {
                    if (!Reaches(runTops[r], lifts[l], chords, out float d))
                        continue;
                    servedCount[l]++;
                    if (best < 0 || d < bestD || (d == bestD && lifts[l].Id.CompareTo(lifts[best].Id) < 0))
                    {
                        best = l;
                        bestD = d;
                    }
                }
                servingLift.Add(best);
                servingDistanceM.Add(bestD);
            }
        }
    }
}
