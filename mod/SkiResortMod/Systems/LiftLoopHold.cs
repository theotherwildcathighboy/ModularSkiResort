// Pure math of a chairlift loop that stops and resumes. An unpowered lift's carriers stop: ChairPinSystem holds a loop per
// station pair (transient, no save data) and uses this arithmetic. Linked into tests/LiftBatch (LoopHoldTests).
//
// Chair clock (ChairPinSystem): chair i stands at s_i = (offset_i + PhaseOf) % L, PhaseOf = m_SpeedMps x TimeS
// (ChairPinSystem.cs:1273-1276 move pass, :1381-1384 PhaseOf), TimeS = (RenderingSystem.frameIndex + frameTime) / 60 in
// double (:1373-1376).
//   * GateOf: Run when no census row is known for the loop's station pair, the row is not Complete, or the lift carries
//     (LiftServedRule.Carries: complete, powered at its base station, not closed; closed = its ski area has no open run,
//     LiftClosedRule, one game hour of hysteresis); Hold otherwise. Fails open: a loop the census cannot explain keeps running.
//   * FrozenPhase: a held loop stands at s_i = (offset_i + FrozenPhase(speed, FrozenTimeS)) % L, PhaseOf's product at the
//     frozen clock bit for bit, so the hold-entry frame writes the pose a running pass would.
//   * Rebase: at the resume every offset becomes (offset + speed x (frozen - now)) mod L in [0, L), so
//     (offset' + speed x now) % L equals the frozen s to double rounding (worst measured 2.03e-7 m over the whole uint clock;
//     the test asserts < 1e-6 m, inside MoveChair's 1 mm idempotence test, ChairPinSystem.cs:1410-1411). Every offset shifts
//     by the same amount, so spacing is kept. No state: the caller keeps FrozenTimeS per held loop.
//   * ExpectsLoop: only a Loop-mover lift owes ChairPin a closed station-to-station loop. A Shuttle (the tram) is moved by
//     TramShuttleSystem and never harvested by ChairPin, so the "did not close into a loop" WARN must skip it; untyped
//     (null) rows are expected to loop.
//   * HeldSuffix: the rebuild signature's held count names why loops are held ("no power" vs "closed").
//
// Pure file: System and Unity.Mathematics only, plus the pure siblings LiftServedRule.cs and SkiCatalog.cs. Static and
// Burst-safe except HeldSuffix (a log string, managed callers only). net48: no double.IsFinite (use math.isfinite), no Math.Clamp.

using System;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>What ChairPin does with one loop this poll.</summary>
    public enum LoopGate : byte { Run = 0, Hold = 1 }

    public static class LiftLoopHold
    {
        /// <summary>Run when no census row is known for the loop's station pair, the row is not Complete, or the lift carries
        /// (LiftServedRule.Carries); Hold otherwise, i.e. exactly for a known Complete row that is unpowered (with PowerGatesService) or
        /// closed.</summary>
        public static LoopGate GateOf(bool rowKnown, bool complete, LiftPower power, bool closed)
        {
            return !rowKnown || !complete || LiftServedRule.Carries(complete, power, closed) ? LoopGate.Run : LoopGate.Hold;
        }

        /// <summary>The phase a held loop stands at: speed x frozenTimeS, ChairPinSystem.PhaseOf's product at the frozen clock
        /// (ChairPinSystem.cs:1381-1384), bit for bit.</summary>
        public static double FrozenPhase(double speedMps, double frozenTimeS)
        {
            return speedMps * frozenTimeS;
        }

        /// <summary>Re-base one offset at the resume so that (offset' + speed x nowTimeS) mod L == (offset + speed x frozenTimeS) mod L
        /// (to double rounding); offset' in [0, L). nowTimeS == frozenTimeS and offset in [0, L) return offset bit for bit. L not
        /// &gt; 0 or a non-finite input returns offset unchanged.</summary>
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

        /// <summary>True when a complete lift of this type should have a ChairPin loop (Loop mover, or untyped); false for a Shuttle.</summary>
        public static bool ExpectsLoop(LiftTypeDef type)
        {
            return type == null || type.Mover != MoverKind.Shuttle;
        }

        /// <summary>The rebuild signature's held part: "" when nothing is held; else ", N held (why)" where why names the non-zero
        /// reasons: "no power", "closed", or "p no power, c closed". Negative counts read as 0.</summary>
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
