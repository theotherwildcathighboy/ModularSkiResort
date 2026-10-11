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
// in the mod (service, lift value, billing, ticket gate); PowerGatesService = false turns power into
// status only everywhere (one switch). Round S1 F2 (ski-days spec ch1 §5; s1-spec N7): the census reads
// the 4-argument PowerOf — NoPower only while the warning shows AND under half of the wanted power
// arrives, back at three quarters or when the warning clears (hysteresis); the game warns at any
// shortfall after two short dispatches (:126-142), and a 9 % city shortfall switched FT8's lifts off.
//
// PURE FILE: `using System;`, `using System.Collections.Generic;`, `using Unity.Mathematics;` and
// EntityRef only (the test-host rule). No state; Assign is allocation-free once its lists have grown.
//
// Created 2026-10-04 (Round E1 stage 0, S0: ch2 §3.2 as edited, verbatim); owned by stream S2.

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
        public const float ServeRadiusM = 150f;
        public const float TopMarginM = 15f;

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

            public bool Serves =>
                Complete && ChordCount > 0 && !float.IsNaN(TopY) && IsPowered(Power);
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
        /// lift billing (D23) and the ticket gate's operating lifts (D26). Unknown counts as powered.
        /// With PowerGatesService false, power is status only everywhere (one switch).</summary>
        public static bool IsPowered(LiftPower power)
        {
            return !PowerGatesService || power != LiftPower.NoPower;
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
            if (!lift.Serves || !math.all(math.isfinite(runTop)) || runTop.y > lift.TopY + TopMarginM)
                return false;
            float best = float.PositiveInfinity;
            float2 p = runTop.xz;
            int end = lift.FirstChord + lift.ChordCount;
            for (int c = lift.FirstChord; c < end; c++)
            {
                float d2 = DistanceSqXZ(p, chords[c].A, chords[c].B);
                if (d2 < best)
                    best = d2;
            }
            if (!(best <= ServeRadiusM * ServeRadiusM))
                return false;
            distanceM = math.sqrt(best);
            return true;
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
