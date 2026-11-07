// Pure math of a chairlift loop that stops and resumes. An unpowered lift's carriers stop: ChairPinSystem holds a loop per
// station pair (transient, no save data) and uses this arithmetic. Linked into tests/LiftBatch (LoopHoldTests).
// Chair i stands at s_i = (offset_i + PhaseOf) % L. Rebase at the resume keeps spacing (every offset shifts by the same amount).
// GateOf fails open: a loop the census cannot explain keeps running. ExpectsLoop: a Shuttle (the tram) never owes a loop.
// Pure file: System and Unity.Mathematics only, plus the pure siblings LiftServedRule.cs and SkiCatalog.cs. Static and
// Burst-safe except HeldSuffix (a log string, managed callers only). net48: no double.IsFinite (use math.isfinite), no Math.Clamp.
// Engine notes: EngineNotes/LiftLoopHold.md

using System;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>What ChairPin does with one loop this poll.</summary>
    public enum LoopGate : byte { Run = 0, Hold = 1 }

    public static class LiftLoopHold
    {
        /// <summary>Run when no census row is known for the loop's station pair, the row is not Complete, or the lift carries
        /// (LiftServedRule.Carries). Hold otherwise, i.e. exactly for a known Complete row that is unpowered (with PowerGatesService) or
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

        /// <summary>Re-base one offset at the resume so that (offset' + speed x nowTimeS) mod L == (offset + speed x frozenTimeS) mod L (to
        /// double rounding). offset' in [0, L). nowTimeS == frozenTimeS and offset in [0, L) return offset bit for bit. L not &gt; 0 or a
        /// non-finite input returns offset unchanged.</summary>
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

        /// <summary>True when a complete lift of this type should have a ChairPin loop (Loop mover, or untyped). False for a
        /// Shuttle.</summary>
        public static bool ExpectsLoop(LiftTypeDef type)
        {
            return type == null || type.Mover != MoverKind.Shuttle;
        }

        /// <summary>The rebuild signature's held part: "" when nothing is held. Otherwise ", N held (why)", where why names the non-zero
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
