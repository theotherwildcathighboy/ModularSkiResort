// LiftLoopHold — the PURE math of a chairlift loop that stops and resumes (Phase D, docs/research/art/assets-2026-10/
// code-round-4/round-4-contract.md §3.2, D4-D6, seam S2; round-4-contract-skeptic.md claims 11-13 and S-10; rules group).
// Richard's re-ruling, DECISIONS 171: an unpowered lift's carriers STOP. ChairPinSystem (movers group) holds a loop per
// station pair, transient, no save data; this file is the arithmetic it uses, linked into tests/LiftBatch (LoopHoldTests
// LH-1..LH-4).
//
// THE CHAIR CLOCK (ChairPinSystem, not edited by this file): chair i stands at s_i = (offset_i + PhaseOf) % L, PhaseOf =
// m_SpeedMps × TimeS (ChairPinSystem.cs:1273-1276 the move pass, :1381-1384 PhaseOf), TimeS = (RenderingSystem.frameIndex +
// frameTime) / 60 in double (:1373-1376). Lines as of the round-4 fixer pass; in the pre-Phase-D copy eb26ac3a they were
// :1072-1076, :1140-1143 and :1132-1135.
//   * GateOf — D5: Run when no census row is known for the loop's station pair, the row is not Complete, or the lift
//     carries (LiftServedRule.Carries: complete, powered at its BASE station, not closed — T13: closed = its ski area has no
//     open run, LiftClosedRule in LiftServedRule.cs, one game hour of hysteresis); Hold otherwise. Fail open: a
//     loop the census cannot explain keeps running, as before Phase D.
//   * FrozenPhase — a held loop stands at s_i = (offset_i + FrozenPhase(speed, FrozenTimeS)) % L: PhaseOf's product at the
//     frozen clock, bit for bit, so the hold-entry frame writes the pose a running pass would write on that frame.
//   * Rebase — at the resume every offset becomes offset' = (offset + speed × (frozen − now)) mod L in [0, L), so
//     (offset' + speed × now) % L equals the frozen s to double rounding (LH-1 measures 5.69e-10 m worst over 10,000 cases
//     for a hold ≤ 2e5 s and 2.03e-7 m over the whole uint clock, frozen ≤ 7.16e7 s; the skeptic's node run, claim 12, gave
//     2.3e-10 m and 1.2e-7 m over 300,000 cases; LH-1 asserts < 1e-6 m, far inside MoveChair's 1 mm idempotence test,
//     ChairPinSystem.cs:1410-1411, :1169-1171 in eb26ac3a). Every offset shifts by the same amount, so the spacing is kept
//     (LH-3). No state: the caller keeps FrozenTimeS per held loop.
//
//   * ExpectsLoop — T116 (2026-10-20 field log 23:14:34): only a Loop-mover lift owes ChairPin a closed station-to-station
//     loop. A Shuttle (the tram, Phase C) is never harvested by ChairPin (TramShuttleSystem moves it), so the "complete
//     lift(s) did not close into a station-to-station loop" WARN must skip it; untyped (null) rows keep the old behavior.
//   * HeldSuffix — T116: the rebuild signature's held count names WHY the loops are held. Before, every held loop printed
//     "held (no power)" though the gate also holds a CLOSED lift (T13: no open run); the FG lift of 2026-10-19 was
//     "Running → Held (closed)" (log line 588) and read "1 held (no power)" for sixteen minutes.
//
// PURE FILE: `using System;` and `using Unity.Mathematics;` only, + the pure siblings LiftServedRule.cs (LiftPower,
// Carries) and SkiCatalog.cs (LiftTypeDef, MoverKind). Static, Burst-safe except HeldSuffix (a log string, managed
// callers only). net48 traps avoided: no double.IsFinite (math.isfinite), no Math.Clamp.

using System;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>Phase D (round-4-contract §3.2): what ChairPin does with one loop this poll.</summary>
    public enum LoopGate : byte { Run = 0, Hold = 1 }

    public static class LiftLoopHold
    {
        /// <summary>D5: Run when no census row is known for the loop's station pair, the row is not Complete, or the lift
        /// carries (LiftServedRule.Carries); Hold otherwise — i.e. Hold exactly for a known, Complete row whose power is
        /// NoPower (with PowerGatesService) or which is closed.</summary>
        public static LoopGate GateOf(bool rowKnown, bool complete, LiftPower power, bool closed)
        {
            return !rowKnown || !complete || LiftServedRule.Carries(complete, power, closed) ? LoopGate.Run : LoopGate.Hold;
        }

        /// <summary>The phase a held loop stands at: speed × frozenTimeS — ChairPinSystem.PhaseOf's product at the frozen
        /// clock (ChairPinSystem.cs:1381-1384; :1140-1143 in eb26ac3a), bit for bit.</summary>
        public static double FrozenPhase(double speedMps, double frozenTimeS)
        {
            return speedMps * frozenTimeS;
        }

        /// <summary>Re-base one offset at the resume so that (offset' + speed × nowTimeS) mod L == (offset + speed ×
        /// frozenTimeS) mod L (to double rounding); offset' in [0, L). nowTimeS == frozenTimeS and offset in [0, L) return
        /// offset bit for bit. L not &gt; 0 or a non-finite input returns offset unchanged.</summary>
        public static double Rebase(double offset, double speedMps, double frozenTimeS, double nowTimeS, double lengthM)
        {
            if (!(lengthM > 0.0) || !math.isfinite(offset) || !math.isfinite(speedMps) || !math.isfinite(frozenTimeS) ||
                !math.isfinite(nowTimeS) || !math.isfinite(lengthM))
                return offset;
            double r = (offset + speedMps * (frozenTimeS - nowTimeS)) % lengthM;
            if (r < 0.0)
                r += lengthM;
            if (r >= lengthM)
                r -= lengthM;
            return r;
        }

        /// <summary>T116: true when a complete lift of this type should have a ChairPin loop (Loop mover, or untyped);
        /// false for a Shuttle (the tram: TramShuttleSystem's, never harvested by ChairPin).</summary>
        public static bool ExpectsLoop(LiftTypeDef type)
        {
            return type == null || type.Mover != MoverKind.Shuttle;
        }

        /// <summary>T116: the rebuild signature's held part: "" when nothing is held; else ", N held (why)" where why
        /// names the non-zero reasons: "no power", "closed", or "p no power, c closed". Negative counts read as 0.</summary>
        public static string HeldSuffix(int heldForPower, int heldClosed)
        {
            int p = Math.Max(heldForPower, 0), c = Math.Max(heldClosed, 0);
            if (p + c == 0)
                return string.Empty;
            string why = p > 0 && c > 0 ? p + " no power, " + c + " closed" : p > 0 ? "no power" : "closed";
            return ", " + (p + c) + " held (" + why + ")";
        }
    }
}
