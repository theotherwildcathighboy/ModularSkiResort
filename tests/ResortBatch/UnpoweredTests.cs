// UnpoweredTests — Phase D (docs/research/art/assets-2026-10/code-round-4/round-4-contract.md §7.2, rules group, with
// round-4-contract-skeptic.md MUST-1): Richard's re-ruling, DECISIONS 171 — an unpowered lift of every type carries no
// guests and does nothing for the resort, but still pays its full upkeep. The simulation half was already the code
// (contract §0.1); these rows pin it per lift type t ∈ SkiCatalog.Lifts (fixedGrip, detachable, gondola, tram), power ON
// (Powered), OFF (NoPower) and Unknown, through the pure rules the systems call. Where-tags `unpowered/…`.
//   UP-1  served: LiftServedRule.Assign, one lift of type t (its ServeRule, ServeRadiusM, TopMarginM) and one run top inside
//         its reach (50 m off the line, 10 m off the top station): ON → served (index 0, count 1); OFF → unserved (−1, 0);
//         Unknown → served; Lift.Serves == Carries(Complete, Power, false).
//   UP-2  attraction: SkiResortScore.Compute, one area, one A-frame, one rated blue run (Served from UP-1), one LiftInput{
//         Complete, IsPowered(p), TypeIndex t}, backcountry 40: OFF → LiftsCounted 0, LiftsUnpowered 1, LiftsValue 0, the
//         run's ServedFactor 0.4, RunsCapacity = 0.4 × ON's, Attraction < ON's; backcountry 0 → RunsCounted 0, SummerMode.
//   UP-3  summit: SummitServed false with the only lift OFF, true ON and Unknown.
//   UP-4  upkeep (MUST-1, DECISIONS 171 sub-ruling "an unpowered lift still costs its maintenance"): SkiMoneyRules
//         .LiftUpkeepBillingOf has no power input — Full when the area has a counted run (OFF at backcountry 40: the area still
//         counts its run at the backcountry share), so the rate is LiftFullRate of type t's own numbers bit for bit, not 25 %;
//         = LiftBillingOf(complete, true, areaOpen) for all four inputs; the kept reductions: Unfinished, and NoOpenRun when
//         the area counts no run (OFF at backcountry 0 → 25 %); LiftOperating(true, false) stays false (the Operating count).
//   UP-5  tickets: TicketGateOf(40, 1, true, RunsCounted of UP-2): ON → Open; OFF at backcountry 40 → Open (as built:
//         backcountry guests still buy a ticket, Richard's default for round-4 Q3); OFF at backcountry 0 → NothingOpen.
//   UP-6  ski day: AreaStateOf with UP-2's RunsCounted: OFF at backcountry 0 → NoOpenRun, ON → not NoOpenRun;
//         Capacity(RunsCapacity, 10 lodges, powder 0) is runs-limited and scales by 0.4 OFF; ClosingEdgeOf(hill seen open,
//         …, runsCounted 0) == 1 (PullNoRun: on-site claims are pulled).

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using Unity.Mathematics;
using Score = SkiResortMod.Systems.SkiResortScore;

namespace ResortBatch
{
    public static class UnpoweredTests
    {
        private static Func<bool, string, string, bool> s_Check;

        private const float LineLengthM = 1000f;
        private static readonly LiftPower[] s_Powers = { LiftPower.Powered, LiftPower.NoPower, LiftPower.Unknown };

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Check(LiftServedRule.PowerGatesService, "unpowered", "PowerGatesService is true (the oracles assume power gates service)");
            Check(SkiCatalog.Lifts.Count == 4, "unpowered", "four lift types (got " + SkiCatalog.Lifts.Count + ")");
            for (int t = 0; t < SkiCatalog.Lifts.Count; t++)
                PerType(SkiCatalog.Lifts[t]);
            Up4UpkeepTable();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);

        private static void Eq<T>(T expected, T actual, string where, string what)
        {
            Check(EqualityComparer<T>.Default.Equals(expected, actual), where, what + ": expected " + expected + ", got " + actual);
        }

        private static bool Near(double a, double b, double relEps) => Math.Abs(a - b) <= relEps * Math.Max(1.0, Math.Abs(b));

        /// <summary>One lift of type <paramref name="def"/>: a straight line (0,0) → (0,1000) xz, top station tip y 100 at
        /// (0,1000), the type's serve rule and numbers.</summary>
        private static LiftServedRule.Lift LiftOf(LiftTypeDef def, LiftPower power)
        {
            return new LiftServedRule.Lift
            {
                Id = new EntityRef(10, 1), Base = new EntityRef(10, 1), Top = new EntityRef(11, 1),
                Complete = true, Power = power, TopY = 100f, FirstChord = 0, ChordCount = 1,
                Rule = (byte)def.ServeRule, RadiusM = def.ServeRadiusM, TopMarginM = def.TopMarginM,
                TopXZ = new float2(0f, LineLengthM),
            };
        }

        private struct Outcome
        {
            public bool Served;
            public Score.Result Result;
            public double ServedFactor;
        }

        private static Outcome Area(LiftTypeDef def, LiftPower power, int backcountry)
        {
            var o = new Outcome();
            // UP-1's assignment decides the run's Served flag (the attraction system's own path, SkiResortAttractivenessSystem).
            var lifts = new List<LiftServedRule.Lift> { LiftOf(def, power) };
            var chords = new List<LiftServedRule.Chord> { new LiftServedRule.Chord { A = new float2(0f, 0f), B = new float2(0f, LineLengthM) } };
            var tops = new List<float3> { new float3(50f, 90f, 990f) };
            var serving = new List<int>();
            var dist = new List<float>();
            var count = new List<int>();
            LiftServedRule.Assign(tops, lifts, chords, serving, dist, count);
            o.Served = serving.Count == 1 && serving[0] == 0;

            var area = new Score.AreaInput
            {
                ActiveLodges = 1,
                Efficiency = 1.0,
                Settings = new Score.ScoreSettings { K = 4, BackcountryPercent = backcountry, SnowGating = false },
            };
            var runs = new List<Score.RunInput>
            {
                new Score.RunInput
                {
                    Kind = Score.KindGroomed, State = Score.BandState.Rated, Band = Score.BandBlue, AreaM2 = 20000.0,
                    Served = o.Served, OpenFactor = 1.0, TopM = 1100.0, BottomM = 900.0,
                },
            };
            var liftInputs = new List<Score.LiftInput>
            {
                new Score.LiftInput { Complete = true, Powered = LiftServedRule.IsPowered(power), TypeIndex = def.Index },
            };
            var parts = new List<Score.RunPart>();
            o.Result = Score.Compute(area, runs, liftInputs, parts);
            o.ServedFactor = parts.Count == 1 ? parts[0].ServedFactor : double.NaN;
            return o;
        }

        private static void PerType(LiftTypeDef def)
        {
            string w = "unpowered/" + def.Id;

            // UP-1 served
            foreach (LiftPower p in s_Powers)
            {
                var lifts = new List<LiftServedRule.Lift> { LiftOf(def, p) };
                var chords = new List<LiftServedRule.Chord> { new LiftServedRule.Chord { A = new float2(0f, 0f), B = new float2(0f, LineLengthM) } };
                var tops = new List<float3> { new float3(50f, 90f, 990f) };
                var serving = new List<int>();
                var dist = new List<float>();
                var count = new List<int>();
                LiftServedRule.Assign(tops, lifts, chords, serving, dist, count);
                bool on = p != LiftPower.NoPower;
                Eq(on ? 0 : -1, serving.Count == 1 ? serving[0] : -99, w + " UP-1", "serving lift (" + p + ")");
                Eq(on ? 1 : 0, count.Count == 1 ? count[0] : -99, w + " UP-1", "runs served (" + p + ")");
                Eq(LiftServedRule.Carries(lifts[0].Complete, lifts[0].Power, false), lifts[0].Serves, w + " UP-1",
                   "Lift.Serves == Carries(Complete, Power, false) (" + p + ")");
            }

            // UP-2 attraction
            Outcome on40 = Area(def, LiftPower.Powered, 40);
            Outcome off40 = Area(def, LiftPower.NoPower, 40);
            Outcome unk40 = Area(def, LiftPower.Unknown, 40);
            Outcome off0 = Area(def, LiftPower.NoPower, 0);
            Outcome on0 = Area(def, LiftPower.Powered, 0);
            string w2 = w + " UP-2";
            Eq(1, on40.Result.LiftsCounted, w2, "ON LiftsCounted");
            Eq(def.ScoreValue, on40.Result.LiftsValue, w2, "ON LiftsValue = the type's ScoreValue");
            Eq(1.0, on40.ServedFactor, w2, "ON the run's ServedFactor");
            Eq(0, off40.Result.LiftsCounted, w2, "OFF LiftsCounted");
            Eq(1, off40.Result.LiftsUnpowered, w2, "OFF LiftsUnpowered");
            Eq(0.0, off40.Result.LiftsValue, w2, "OFF LiftsValue");
            Eq(0.0, off40.Result.LiftsApplied, w2, "OFF LiftsApplied");
            Eq(0.4, off40.ServedFactor, w2, "OFF the run's ServedFactor (backcountry 40 %)");
            Eq(1, off40.Result.RunsUnserved, w2, "OFF RunsUnserved");
            Check(on40.Result.RunsCapacity > 0.0 && Near(off40.Result.RunsCapacity, 0.4 * on40.Result.RunsCapacity, 1e-12), w2,
                  "OFF RunsCapacity = 0.4 × ON's (" + off40.Result.RunsCapacity + " vs " + on40.Result.RunsCapacity + ")");
            Check(off40.Result.Attraction < on40.Result.Attraction, w2,
                  "OFF Attraction < ON's (" + off40.Result.Attraction + " vs " + on40.Result.Attraction + ")");
            Check(unk40.Result.Equals(on40.Result), w2, "Unknown power scores exactly as Powered");
            Eq(0, off0.Result.RunsCounted, w2, "OFF, backcountry 0: RunsCounted");
            Check((off0.Result.Flags & Score.ScoreFlags.SummerMode) != 0, w2, "OFF, backcountry 0: SummerMode");
            Eq(1, on0.Result.RunsCounted, w2, "ON, backcountry 0: RunsCounted (the lift serves the run)");

            // UP-3 summit (a lodge 50 m from the top station tip, SkiCatalog.Ladder.SummitServedRadiusM 300)
            string w3 = w + " UP-3";
            float2 lodge = new float2(30f, LineLengthM + 40f);
            foreach (LiftPower p in s_Powers)
            {
                var lifts = new List<LiftServedRule.Lift> { LiftOf(def, p) };
                Eq(p != LiftPower.NoPower, LiftServedRule.SummitServed(lodge, lifts, SkiCatalog.Ladder.SummitServedRadiusM), w3,
                   "SummitServed (" + p + ")");
            }

            // UP-4 upkeep, per type (MUST-1)
            string w4 = w + " UP-4";
            bool openOff40 = SkiMoneyRules.LiftAreaRunOpen(true, true, false, off40.Result.RunsCounted);
            bool openOff0 = SkiMoneyRules.LiftAreaRunOpen(true, true, false, off0.Result.RunsCounted);
            Check(openOff40 && !openOff0, w4, "area run open: OFF at 40 % yes, OFF at 0 % no");
            SkiMoneyRules.LiftBilling bOff40 = SkiMoneyRules.LiftUpkeepBillingOf(true, openOff40);
            SkiMoneyRules.LiftBilling bOff0 = SkiMoneyRules.LiftUpkeepBillingOf(true, openOff0);
            Eq(SkiMoneyRules.LiftBilling.Full, bOff40, w4, "OFF, its area open: billed in full");
            float full = SkiMoneyRules.LiftFullRate(LineLengthM, def.FixedPerMonth, def.PerKmPerMonth);
            Eq(full, SkiMoneyRules.LiftRate(bOff40, LineLengthM, def.FixedPerMonth, def.PerKmPerMonth), w4,
               "OFF rate = LiftFullRate(1000, the type's FixedPerMonth, PerKmPerMonth), not 25 %");
            Eq(def.FixedPerMonth + def.PerKmPerMonth, full, w4, "the full rate at 1 km = fixed + per km");
            Eq(SkiMoneyRules.LiftBilling.NoOpenRun, bOff0, w4, "OFF, its area with no counted run: NoOpenRun (kept reduction)");
            Eq(SkiMoneyRules.LiftReducedShare * full, SkiMoneyRules.LiftRate(bOff0, LineLengthM, def.FixedPerMonth, def.PerKmPerMonth),
               w4, "NoOpenRun bills 25 %");
            Check(!SkiMoneyRules.LiftOperating(true, LiftServedRule.IsPowered(LiftPower.NoPower)), w4, "OFF: not operating (Operating count keeps power)");

            // UP-5 tickets
            string w5 = w + " UP-5";
            Eq(SkiMoneyRules.TicketGate.Open, SkiMoneyRules.TicketGateOf(40, 1f, true, on40.Result.RunsCounted), w5, "ON");
            Eq(SkiMoneyRules.TicketGate.Open, SkiMoneyRules.TicketGateOf(40, 1f, true, off40.Result.RunsCounted), w5,
               "OFF, backcountry 40 (backcountry guests still buy a ticket, as built)");
            Eq(SkiMoneyRules.TicketGate.NothingOpen, SkiMoneyRules.TicketGateOf(40, 1f, true, off0.Result.RunsCounted), w5, "OFF, backcountry 0");

            // UP-6 ski day
            string w6 = w + " UP-6";
            var factsOff0 = new SkiDayRules.AreaFacts { ScoreKnown = true, WriteHeld = false, Efficiency = 1.0, RunsCounted = off0.Result.RunsCounted, SkiDayLodges = 1 };
            var factsOn = new SkiDayRules.AreaFacts { ScoreKnown = true, WriteHeld = false, Efficiency = 1.0, RunsCounted = on40.Result.RunsCounted, SkiDayLodges = 1 };
            Eq(SkiDayRules.AreaState.NoOpenRun, SkiDayRules.AreaStateOf(factsOff0, 0.5f, false, 0, 100), w6, "OFF, backcountry 0: AreaStateOf");
            Check(SkiDayRules.AreaStateOf(factsOn, 0.5f, false, 0, 100) != SkiDayRules.AreaState.NoOpenRun, w6, "ON: not NoOpenRun");
            double capOn = SkiDayRules.Capacity(on40.Result.RunsCapacity, 10, 0.0, out SkiDayLimit limOn);
            double capOff = SkiDayRules.Capacity(off40.Result.RunsCapacity, 10, 0.0, out SkiDayLimit limOff);
            Check(limOn == SkiDayLimit.Runs && limOff == SkiDayLimit.Runs, w6, "runs-limited with 10 lodges (" + limOn + ", " + limOff + ")");
            Check(capOn > 0.0 && Near(capOff, 0.4 * capOn, 1e-12), w6, "OFF capacity = 0.4 × ON's (" + capOff + " vs " + capOn + ")");
            Eq((byte)1, SkiDayRules.ClosingEdgeOf(true, false, true, false, off0.Result.RunsCounted, false), w6,
               "ClosingEdgeOf(hill seen open, runsCounted 0) = PullNoRun");
        }

        /// <summary>UP-4's table: LiftUpkeepBillingOf == LiftBillingOf(complete, powered: true, areaOpen) for every input, and
        /// it never returns NoPower.</summary>
        private static void Up4UpkeepTable()
        {
            const string W = "unpowered/UP-4 table";
            for (int c = 0; c < 2; c++)
                for (int a = 0; a < 2; a++)
                {
                    bool complete = c == 1, areaOpen = a == 1;
                    SkiMoneyRules.LiftBilling b = SkiMoneyRules.LiftUpkeepBillingOf(complete, areaOpen);
                    Eq(SkiMoneyRules.LiftBillingOf(complete, true, areaOpen), b, W, "complete " + complete + ", area open " + areaOpen);
                    Check(b != SkiMoneyRules.LiftBilling.NoPower, W, "never NoPower (complete " + complete + ", area open " + areaOpen + ")");
                }
            Eq(SkiMoneyRules.LiftBilling.Full, SkiMoneyRules.LiftUpkeepBillingOf(true, true), W, "complete, area open → Full");
            Eq(SkiMoneyRules.LiftBilling.NoOpenRun, SkiMoneyRules.LiftUpkeepBillingOf(true, false), W, "complete, no counted run → NoOpenRun");
            Eq(SkiMoneyRules.LiftBilling.Unfinished, SkiMoneyRules.LiftUpkeepBillingOf(false, true), W, "not complete → Unfinished");
            Eq(SkiMoneyRules.LiftBilling.Unfinished, SkiMoneyRules.LiftUpkeepBillingOf(false, false), W, "not complete, no run → Unfinished");
        }
    }
}
