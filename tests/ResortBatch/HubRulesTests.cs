// HubRulesTests — Round E1 test file for tests/ResortBatch (econ-round spec ch5 §10.1: hub rules, problems,
// piste-map rules, the aggregate's run vertical, sections A-G). Owner: stream S5 (docs/research/econ-round/
// e1-spec.md §3.2). Created empty by stage 0 (S0, 2026-10-04); written 2026-10-04 by S5.
// e1-spec R9 — `public static class HubRulesTests` with `public static void Run(Func<bool, string, string, bool> check)`
// and its own Near; Program.Main calls HubRulesTests.Run(Check).
//
// ORACLES are literal hand values (ch5 §10.1 tables), never a second run of the same code:
//   A  the small rules of HubRules (Status, OpenStateOf, Season, NextStep, LiftStatusOf, RideTimeS,
//      OpenSharePct, FactorEffectPct, CountedFraction, TicketIncomeMonth, NetMonth, LiftNoPair);
//   B  AttractionWithLodges, the receipt parity LodgePart + Terrain = AOpen and RunValueFull on ch1 §3.1's
//      compositions built HERE (literal runs; no dependency on ScoreTests' helpers). Needs ch1's
//      SkiResortScore.Compute / LodgeBaseOf / RoundToEven (stream S1): with the stage-0 stubs it FAILS
//      (expected until stage 2, e1-spec §4.2) — every index is guarded so it never throws;
//   C  HubProblems.Build, cases P1-P26, every field of every row (P20-P23 and five LiftNoPair rows in A: E1
//      fixer, e1-spec §8.9 F9 — lone stations pair across census rows; P24-P26 and the pair row's index in
//      P20-P22: E1 fixer pass 2, §8.10 G4 — the row whose name the finished lift keeps; P27-P31: Round F7 D5,
//      docs/research/field-test-7-fixes.md — SignNoRoad / LodgeNoRoad from Building.m_RoadEdge);
//   D  the Status property over all 286 (runs, open, partly) combinations;
//   E  constants, literal enum values (gate R17's other half), HubSnowSums, TopTempC, Percent, FollowCheck;
//   F  PisteMapRules (StyleOf, IsDashed, WidthScale, SolidRing, DashRing incl. dashes across corners,
//      truncation, solid fallback, slope, non-finite node, tiny ring, appending);
//   G  SkiResortAggregate.RunVerticalM on Program.AggregateTests' fixture (copied literally) next to the
//      unchanged VerticalM (C14).
// Tolerances: ±1e-9 for the parity identity, ±1e-6 for ch1's 9-decimal values, ±1e-4 for float rules.

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace ResortBatch
{
    public static class HubRulesTests
    {
        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            SectionA();
            SectionB();
            SectionC();
            SectionD();
            SectionE();
            SectionF();
            SectionG();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);

        private static bool Near(double a, double b, double eps)
        {
            return Math.Abs(a - b) <= eps;
        }

        // ================================================================== A. small rules

        private static void SectionA()
        {
            const string w = "hub A";
            // Status
            Eq(HubRules.Status(0, 0, 0), ResortStatus.NoRuns, w, "Status(0,0,0)");
            Eq(HubRules.Status(7, 0, 0), ResortStatus.Closed, w, "Status(7,0,0)");
            Eq(HubRules.Status(7, 4, 1), ResortStatus.Partly, w, "Status(7,4,1)");
            Eq(HubRules.Status(7, 0, 2), ResortStatus.Partly, w, "Status(7,0,2)");
            Eq(HubRules.Status(7, 6, 1), ResortStatus.Partly, w, "Status(7,6,1)");
            Eq(HubRules.Status(7, 7, 0), ResortStatus.Open, w, "Status(7,7,0)");
            Eq(HubRules.Status(1, 1, 0), ResortStatus.Open, w, "Status(1,1,0)");

            // OpenStateOf
            Eq(HubRules.OpenStateOf(1.0, true), RunOpenState.Open, w, "OpenStateOf(1.0,true)");
            Eq(HubRules.OpenStateOf(1.2, true), RunOpenState.Open, w, "OpenStateOf(1.2,true)");
            Eq(HubRules.OpenStateOf(0.999, true), RunOpenState.Partly, w, "OpenStateOf(0.999,true)");
            Eq(HubRules.OpenStateOf(0.5, true), RunOpenState.Partly, w, "OpenStateOf(0.5,true)");
            Eq(HubRules.OpenStateOf(0.0, true), RunOpenState.Closed, w, "OpenStateOf(0.0,true)");
            Eq(HubRules.OpenStateOf(-0.1, true), RunOpenState.Closed, w, "OpenStateOf(-0.1,true)");
            Eq(HubRules.OpenStateOf(double.NaN, true), RunOpenState.Closed, w, "OpenStateOf(NaN,true)");
            Eq(HubRules.OpenStateOf(1.0, false), RunOpenState.None, w, "OpenStateOf(1.0,false)");

            // Season
            Eq(HubRules.Season(false, 7, 7, true, true, 5f, 0f, 2f), SeasonHint.GatingOff, w, "Season(false,7,7,true,true,5,0,2)");
            Eq(HubRules.Season(true, 0, 0, false, true, -5f, 0f, 2f), SeasonHint.None, w, "Season(true,0,0,false,true,-5,0,2)");
            Eq(HubRules.Season(true, 7, 0, false, false, -5f, 0f, 2f), SeasonHint.None, w, "Season(true,7,0,false,false,-5,0,2)");
            Eq(HubRules.Season(true, 7, 0, false, true, float.NaN, 0f, 2f), SeasonHint.None, w, "Season(true,7,0,false,true,NaN,0,2)");
            Eq(HubRules.Season(true, 7, 5, false, true, -3f, 0f, 2f), SeasonHint.SnowSeason, w, "Season(true,7,5,false,true,-3,0,2)");
            Eq(HubRules.Season(true, 7, 5, true, true, 1.5f, 0f, 2f), SeasonHint.SnowGunsOnly, w, "Season(true,7,5,true,true,1.5,0,2)");
            Eq(HubRules.Season(true, 7, 5, false, true, 1.5f, 0f, 2f), SeasonHint.Thawing, w, "Season(true,7,5,false,true,1.5,0,2)");
            Eq(HubRules.Season(true, 7, 5, true, true, 2.5f, 0f, 2f), SeasonHint.Thawing, w, "Season(true,7,5,true,true,2.5,0,2)");
            Eq(HubRules.Season(true, 7, 0, false, true, -1f, 0f, 2f), SeasonHint.WaitingForSnow, w, "Season(true,7,0,false,true,-1,0,2)");
            Eq(HubRules.Season(true, 7, 0, true, true, 2.0f, 0f, 2f), SeasonHint.WaitingForSnow, w, "Season(true,7,0,true,true,2.0,0,2)");
            Eq(HubRules.Season(true, 7, 0, true, true, 2.1f, 0f, 2f), SeasonHint.TooWarm, w, "Season(true,7,0,true,true,2.1,0,2)");
            Eq(HubRules.Season(true, 7, 0, false, true, 0.5f, 0f, 2f), SeasonHint.TooWarm, w, "Season(true,7,0,false,true,0.5,0,2)");

            // NextStep
            Eq(HubRules.NextStep(0, 1, 3), NextStepKind.Lodge, w, "NextStep(0,1,3)");
            Eq(HubRules.NextStep(0, 0, 0), NextStepKind.Lodge, w, "NextStep(0,0,0)");
            Eq(HubRules.NextStep(2, 0, 3), NextStepKind.Lift, w, "NextStep(2,0,3)");
            Eq(HubRules.NextStep(2, 1, 0), NextStepKind.Run, w, "NextStep(2,1,0)");
            Eq(HubRules.NextStep(2, 1, 3), NextStepKind.Done, w, "NextStep(2,1,3)");

            // LiftStatusOf
            Eq(HubRules.LiftStatusOf(0, true), HubLiftStatus.Running, w, "LiftStatusOf(0,true)");
            Eq(HubRules.LiftStatusOf(0, false), HubLiftStatus.NoPower, w, "LiftStatusOf(0,false)");
            Eq(HubRules.LiftStatusOf(1, true), HubLiftStatus.ChairsStopped, w, "LiftStatusOf(1,true)");
            Eq(HubRules.LiftStatusOf(1, false), HubLiftStatus.NoPower, w, "LiftStatusOf(1,false)");
            Eq(HubRules.LiftStatusOf(2, true), HubLiftStatus.SectionMissing, w, "LiftStatusOf(2,true)");
            Eq(HubRules.LiftStatusOf(2, false), HubLiftStatus.SectionMissing, w, "LiftStatusOf(2,false)");
            Eq(HubRules.LiftStatusOf(3, true), HubLiftStatus.Unfinished, w, "LiftStatusOf(3,true)");
            Eq(HubRules.LiftStatusOf(4, false), HubLiftStatus.Unfinished, w, "LiftStatusOf(4,false)");
            Eq(HubRules.LiftStatusOf(9, true), HubLiftStatus.Unfinished, w, "LiftStatusOf(9,true)");

            // RideTimeS
            NearF(HubRules.RideTimeS(2045f), 409.0, 0.01, w, "RideTimeS(2045)");
            NearF(HubRules.RideTimeS(4643f), 928.6, 0.01, w, "RideTimeS(4643)");
            Eq(HubRules.RideTimeS(0f), -1f, w, "RideTimeS(0)");
            Eq(HubRules.RideTimeS(-5f), -1f, w, "RideTimeS(-5)");
            Eq(HubRules.RideTimeS(float.NaN), -1f, w, "RideTimeS(NaN)");

            // OpenSharePct
            Eq(HubRules.OpenSharePct(51.7495, 72.9183), 71, w, "OpenSharePct(51.7495,72.9183)");
            Eq(HubRules.OpenSharePct(0, 0), -1, w, "OpenSharePct(0,0)");
            Eq(HubRules.OpenSharePct(10, 10), 100, w, "OpenSharePct(10,10)");
            Eq(HubRules.OpenSharePct(0, 10), 0, w, "OpenSharePct(0,10)");
            Eq(HubRules.OpenSharePct(5.005, 10), 50, w, "OpenSharePct(5.005,10)");
            Eq(HubRules.OpenSharePct(12, 10), 100, w, "OpenSharePct(12,10)");

            // FactorEffectPct (the game's number)
            Eq(HubRules.FactorEffectPct(0.5f), -50, w, "FactorEffectPct(0.5)");
            Eq(HubRules.FactorEffectPct(0f), -100, w, "FactorEffectPct(0)");
            Eq(HubRules.FactorEffectPct(-0.2f), -100, w, "FactorEffectPct(-0.2)");
            Eq(HubRules.FactorEffectPct(float.NaN), -100, w, "FactorEffectPct(NaN)");
            Eq(HubRules.FactorEffectPct(1f), 0, w, "FactorEffectPct(1)");
            Eq(HubRules.FactorEffectPct(1.25f), 25, w, "FactorEffectPct(1.25)");
            Eq(HubRules.FactorEffectPct(0.004f), -99, w, "FactorEffectPct(0.004)");
            Eq(HubRules.FactorEffectPct(0.996f), 0, w, "FactorEffectPct(0.996)");
            Eq(HubRules.FactorEffectPct(0.32f), -68, w, "FactorEffectPct(0.32)");

            // CountedFraction
            NearF(HubRules.CountedFraction(false, 0.125f, 0u, 12), 0.5, 1e-4, w, "CountedFraction(false,0.125,0,12)");
            NearF(HubRules.CountedFraction(false, 0f, 0u, 12), 0.0, 1e-4, w, "CountedFraction(false,0,0,12)");
            NearF(HubRules.CountedFraction(true, 0.125f, 65536u, 12), 0.25, 1e-4, w, "CountedFraction(true,0.125,65536,12)");
            NearF(HubRules.CountedFraction(true, 0.125f, 786432u, 12), 0.5, 1e-4, w, "CountedFraction(true,0.125,786432,12)");
            NearF(HubRules.CountedFraction(true, 0.125f, 0u, 12), 0.0, 1e-4, w, "CountedFraction(true,0.125,0,12)");
            NearF(HubRules.CountedFraction(true, 0.125f, 21845u, 0), 0.5, 1e-4, w, "CountedFraction(true,0.125,21845,0)");
            NearF(HubRules.CountedFraction(false, float.NaN, 0u, 12), 0.0, 1e-4, w, "CountedFraction(false,NaN,0,12)");

            // TicketIncomeMonth
            Ticket(true, 8000, 200, 3200, 80, 0.4f, 8000, 200, false);
            Ticket(false, -1, -1, 3200, 80, 0.4f, 8000, 200, true);
            Ticket(false, -1, -1, 1200, 30, 0.3f, 4000, 100, true);
            Ticket(false, -1, -1, 1200, 30, 0.2f, -1, -1, true);
            Ticket(false, -1, -1, 0, 0, 0.5f, 0, 0, true);
            Ticket(true, 0, 0, 500, 10, 0.9f, 0, 0, false);

            // NetMonth
            Net(8000, false, 3600f, 5091f, 6000f, 15000f, -21691, false);
            Net(-1, true, 3600f, 5091f, 6000f, 15000f, -29691, true);
            Net(8000, false, 3600f, 5091f, -1f, -1f, -691, true);
            Net(0, false, 1793.5f, 0f, 0f, 0f, -1794, false);

            // LiftNoPair
            Eq(HubRules.LiftNoPair(new List<HubLiftHintRow>()), true, w, "LiftNoPair([])");
            Eq(HubRules.LiftNoPair(Hints(H(4, true, false))), true, w, "LiftNoPair([(4,T,F)])");
            Eq(HubRules.LiftNoPair(Hints(H(4, true, true))), false, w, "LiftNoPair([(4,T,T)])");
            Eq(HubRules.LiftNoPair(Hints(H(2, true, true))), false, w, "LiftNoPair([(2,T,T)])");
            Eq(HubRules.LiftNoPair(Hints(H(3, true, false))), true, w, "LiftNoPair([(3,T,F)])");
            Eq(HubRules.LiftNoPair(Hints(H(0, true, true))), true, w, "LiftNoPair([(0,T,T)])");
            Eq(HubRules.LiftNoPair(Hints(H(0, true, true), H(4, false, true))), true, w, "LiftNoPair([(0,T,T),(4,F,T)])");
            // E1 fixer (e1-spec §8.9 F9): the census never builds (4,T,T) — each lone station is its own row,
            // so the pair is found across rows (+5). (4,T,T) above stays as the defensive branch.
            Eq(HubRules.LiftNoPair(Hints(H(4, true, false), H(4, false, true))), false, w, "LiftNoPair([(4,T,F),(4,F,T)]) base and top on two rows");
            Eq(HubRules.LiftNoPair(Hints(H(4, false, true), H(3, true, false))), false, w, "LiftNoPair([(4,F,T),(3,T,F)]) a line from the base, a lone top");
            Eq(HubRules.LiftNoPair(Hints(H(4, true, false), H(4, true, false))), true, w, "LiftNoPair([(4,T,F),(4,T,F)]) two bases, no top");
            Eq(HubRules.LiftNoPair(Hints(H(0, true, true), H(4, true, false), H(0, false, true))), true, w, "LiftNoPair([(0,T,T),(4,T,F),(0,F,T)]) a complete row's top is not lone");
            Eq(HubRules.LiftNoPair(Hints(H(3, false, false), H(4, false, true))), true, w, "LiftNoPair([(3,F,F),(4,F,T)]) a line with no station, a lone top");
        }

        private static void Ticket(bool lastFull, long incomeLast, int paidLast, long incomeThis, int paidThis, float f,
                                   int wantIncome, int wantPaid, bool wantEst)
        {
            int got = HubRules.TicketIncomeMonth(lastFull, incomeLast, paidLast, incomeThis, paidThis, f, out int paid, out bool est);
            Check(got == wantIncome && paid == wantPaid && est == wantEst, "hub A",
                $"TicketIncomeMonth({lastFull},{incomeLast},{paidLast},{incomeThis},{paidThis},{f}) = {got}, paid {paid}, est {est}; " +
                $"want {wantIncome}, paid {wantPaid}, est {wantEst}");
        }

        private static void Net(int income, bool incomeEst, float run, float lift, float buildings, float wages, int want, bool wantEst)
        {
            int got = HubRules.NetMonth(income, incomeEst, run, lift, buildings, wages, out bool est);
            Check(got == want && est == wantEst, "hub A",
                $"NetMonth({income},{incomeEst},{run},{lift},{buildings},{wages}) = {got} est {est}; want {want} est {wantEst}");
        }

        private static HubLiftHintRow H(int state, bool hasBase, bool hasTop) =>
            new HubLiftHintRow { State = state, HasBase = hasBase, HasTop = hasTop };

        private static List<HubLiftHintRow> Hints(params HubLiftHintRow[] rows) => new List<HubLiftHintRow>(rows);

        // ================================================================== B. receipt, what-if, run value

        private static SkiResortScore.RunInput R(byte kind, byte band, double areaM2, double top, double bottom, double open)
        {
            return new SkiResortScore.RunInput
            {
                Kind = kind, State = SkiResortScore.BandState.Rated, Band = band, AreaM2 = areaM2, Served = true,
                OpenFactor = open, OpenEstimated = false, TopM = top, BottomM = bottom,
            };
        }

        /// <summary>ch1 §3.1 Small: Groomed Green 2 ha 1150/1000; Groomed Blue 2.5 ha 1250/1000; Groomed Blue 2 ha 1250/1050.</summary>
        private static List<SkiResortScore.RunInput> Small(double open)
        {
            const byte G = SkiResortScore.KindGroomed;
            return new List<SkiResortScore.RunInput>
            {
                R(G, SkiResortScore.BandGreen, 20000, 1150, 1000, open),
                R(G, SkiResortScore.BandBlue, 25000, 1250, 1000, open),
                R(G, SkiResortScore.BandBlue, 20000, 1250, 1050, open),
            };
        }

        /// <summary>ch1 §3.1 Medium: 3 × Groomed Green 2.5 ha 1250/1000; 3 × Groomed Blue 3 ha 1400/1000; Snowmaking
        /// Blue 3 ha 1400/1000; Night Blue 2.5 ha 1300/1000; 2 × Natural Black 3 ha 1500/1100.</summary>
        private static List<SkiResortScore.RunInput> Medium()
        {
            const byte G = SkiResortScore.KindGroomed;
            var runs = new List<SkiResortScore.RunInput>();
            for (int i = 0; i < 3; i++)
                runs.Add(R(G, SkiResortScore.BandGreen, 25000, 1250, 1000, 1.0));
            for (int i = 0; i < 3; i++)
                runs.Add(R(G, SkiResortScore.BandBlue, 30000, 1400, 1000, 1.0));
            runs.Add(R(SkiResortScore.KindSnowmaking, SkiResortScore.BandBlue, 30000, 1400, 1000, 1.0));
            runs.Add(R(SkiResortScore.KindNight, SkiResortScore.BandBlue, 25000, 1300, 1000, 1.0));
            for (int i = 0; i < 2; i++)
                runs.Add(R(SkiResortScore.KindNatural, SkiResortScore.BandBlack, 30000, 1500, 1100, 1.0));
            return runs;
        }

        private static List<SkiResortScore.LiftInput> Lifts(int n)
        {
            var lifts = new List<SkiResortScore.LiftInput>();
            for (int i = 0; i < n; i++)
                lifts.Add(new SkiResortScore.LiftInput { Complete = true, Powered = true });
            return lifts;
        }

        private static SkiResortScore.Result Score(int lodges, double efficiency, List<SkiResortScore.RunInput> runs, int lifts,
                                                   List<SkiResortScore.RunPart> parts)
        {
            var area = new SkiResortScore.AreaInput
            {
                ActiveLodges = lodges, InactiveLodges = 0, Efficiency = efficiency,
                Settings = new SkiResortScore.ScoreSettings { K = 4, BackcountryPercent = 40, SnowGating = true },
            };
            return SkiResortScore.Compute(in area, runs, Lifts(lifts), parts);
        }

        private static void SectionB()
        {
            const string w = "hub B";
            var p = new List<SkiResortScore.RunPart>();
            SkiResortScore.Result t1 = Score(1, 1.0, Small(1.0), 1, p);
            double[] t1Parts = { PartFull(p, 0), PartFull(p, 1), PartFull(p, 2) };
            SkiResortScore.Result t2 = Score(2, 1.0, Medium(), 3, p);
            SkiResortScore.Result e50 = Score(2, 0.5, Medium(), 3, p);
            SkiResortScore.Result w1 = Score(1, 1.0, Small(0.0), 1, p);
            SkiResortScore.Result nl = Score(0, 1.0, Medium(), 3, p);
            Score(1, 1.0, Small(0.5), 1, p);
            double hFull = PartFull(p, 1), hValue = p.Count > 1 ? p[1].Value : double.NaN;

            // Parity: LodgePart + Terrain == AOpen (±1e-9).
            Parity(t1, "T1");
            Parity(t2, "T2");
            Parity(e50, "E50");
            Parity(w1, "W1");
            Parity(nl, "NL");

            // LodgePart, Terrain, AttractionFullEfficiency, Attraction.
            Receipt(t1, "T1", 20, 28.882668052, 49, 49);
            Receipt(t2, "T2", 24, 71.585929905, 96, 96);
            Receipt(e50, "E50", 24, 71.585929905, 96, 48);
            Receipt(w1, "W1", 10, 4.898979486, 15, 15);
            Receipt(nl, "NL", 0, 71.585929905, 0, 0);

            // The receipt adds up the way the UI shows it (ActiveLodges ≥ 1).
            Adds(t1, "T1");
            Adds(t2, "T2");
            Adds(e50, "E50");
            Adds(w1, "W1");

            // AttractionWithLodges on synthetic results.
            var s = new SkiResortScore.Result { AOpen = 71.585929905, LodgeBase = 0, LodgeShare = 1, Efficiency = 1 };
            Eq(HubRules.AttractionWithLodges(in s, 1), 92, w, "AttractionWithLodges(T2 terrain, 1)");
            Eq(HubRules.AttractionWithLodges(in s, 2), 96, w, "AttractionWithLodges(T2 terrain, 2)");
            Eq(HubRules.AttractionWithLodges(in s, 0), 0, w, "AttractionWithLodges(T2 terrain, 0)");
            s.Efficiency = 0.5;
            Eq(HubRules.AttractionWithLodges(in s, 2), 48, w, "AttractionWithLodges(T2 terrain at 0.5, 2)");
            var sw = new SkiResortScore.Result { AOpen = 4.898979486, LodgeBase = 0, LodgeShare = 0.5, Efficiency = 1 };
            Eq(HubRules.AttractionWithLodges(in sw, 1), 15, w, "AttractionWithLodges(W1 terrain, 1)");
            var s0 = new SkiResortScore.Result { AOpen = 71.585929905, LodgeBase = 0, LodgeShare = 1, Efficiency = 0 };
            Eq(HubRules.AttractionWithLodges(in s0, 1), 0, w, "AttractionWithLodges(efficiency 0, 1)");
            var s10 = new SkiResortScore.Result { AOpen = 28.882668052, LodgeBase = 0, LodgeShare = 1, Efficiency = 1 };
            Eq(HubRules.AttractionWithLodges(in s10, 10), 61, w, "AttractionWithLodges(T1 terrain, 10)");
            Eq(HubRules.AttractionWithLodges(in nl, 2), 96, w, "AttractionWithLodges(NL from Compute, 2) — a lodge pair restores T2");

            // RunValueFull = the run's value fully open.
            NearF(t1Parts[0], 6.0, 1e-6, w, "RunValueFull(T1 part 0)");
            NearF(t1Parts[1], 12.803316785, 1e-6, w, "RunValueFull(T1 part 1)");
            NearF(t1Parts[2], 12.0, 1e-6, w, "RunValueFull(T1 part 2)");
            NearF(hFull, 12.803316785, 1e-6, w, "RunValueFull(H part 1)");
            NearF(hValue, 6.401658393, 1e-6, w, "H part 1 Value (open 0.5)");
        }

        private static double PartFull(List<SkiResortScore.RunPart> parts, int i)
        {
            if (i >= parts.Count)
                return double.NaN;   // the stage-0 Compute stub returns no parts: fail, never throw
            SkiResortScore.RunPart part = parts[i];
            return HubRules.RunValueFull(in part);
        }

        private static void Parity(in SkiResortScore.Result r, string name)
        {
            double lp = HubRules.LodgePart(in r), tr = HubRules.Terrain(in r);
            Check(Near(lp + tr, r.AOpen, 1e-9) && !double.IsNaN(r.AOpen), "hub B",
                $"{name}: LodgePart {lp} + Terrain {tr} != AOpen {r.AOpen}");
        }

        private static void Receipt(in SkiResortScore.Result r, string name, double lodgePart, double terrain, int full, int a)
        {
            double lp = HubRules.LodgePart(in r), tr = HubRules.Terrain(in r);
            Check(Near(lp, lodgePart, 1e-6), "hub B", $"{name}: LodgePart {lp}, want {lodgePart}");
            Check(Near(tr, terrain, 1e-6), "hub B", $"{name}: Terrain {tr}, want {terrain}");
            Check(r.AttractionFullEfficiency == full, "hub B", $"{name}: AttractionFullEfficiency {r.AttractionFullEfficiency}, want {full}");
            Check(r.Attraction == a, "hub B", $"{name}: Attraction {r.Attraction}, want {a}");
        }

        private static void Adds(in SkiResortScore.Result r, string name)
        {
            double sum = HubRules.LodgePart(in r) + HubRules.Terrain(in r);
            int full = Clamp200(SkiResortScore.RoundToEven(sum));
            int a = Clamp200(SkiResortScore.RoundToEven(sum * r.Efficiency));
            Check(r.ActiveLodges >= 1 && full == r.AttractionFullEfficiency && a == r.Attraction, "hub B",
                $"{name}: receipt round {full} / {a} vs result {r.AttractionFullEfficiency} / {r.Attraction} (lodges {r.ActiveLodges})");
        }

        private static int Clamp200(int x) => x < 0 ? 0 : x > 200 ? 200 : x;

        // ================================================================== C. problems

        private static HubProblem P(HubProblemCode code, byte severity, int liftIndex = -1, int count = -1, int pct = -1, int more = 0,
                                    int detail = 0, int ifFixed = -1, string factor = "", string build = "")
        {
            return new HubProblem
            {
                Code = code, Severity = severity, LiftIndex = liftIndex, Count = count, Pct = pct, More = more, Detail = detail,
                IfFixed = ifFixed, Factor = factor, BuildItem = build,
            };
        }

        private static HubProblemLift L(int state, bool powered, bool hasBase, bool hasTop, bool named = false) =>
            new HubProblemLift { State = state, Powered = powered, HasBase = hasBase, HasTop = hasTop, CustomName = named };

        private static HubProblemFactor F(string name, int effect) => new HubProblemFactor { Name = name, EffectPct = effect };

        private static HubProblemInput In(int lodges, int active, int efficiencyPct, int liftRows, int runs)
        {
            return new HubProblemInput
            {
                Lodges = lodges, LodgesActive = active, EfficiencyPct = efficiencyPct, LiftRows = liftRows, Runs = runs,
                AttractionFull = -1, IfOneLodge = -1, IfLodgesOn = -1,
            };
        }

        private static List<HubProblemLift> Ls(params HubProblemLift[] lifts) => new List<HubProblemLift>(lifts);

        private static List<HubProblemFactor> Fs(params HubProblemFactor[] factors) => new List<HubProblemFactor>(factors);

        private static void Expect(string name, HubProblemInput input, List<HubProblem> output, params HubProblem[] want)
        {
            HubProblems.Build(in input, output);
            Rows(name, output, want);
        }

        private static void Rows(string name, List<HubProblem> got, HubProblem[] want)
        {
            Check(got.Count == want.Length, "hub C", $"{name}: {got.Count} row(s), want {want.Length}: {Text(got)}");
            int n = Math.Min(got.Count, want.Length);
            for (int i = 0; i < n; i++)
            {
                HubProblem g = got[i], e = want[i];
                bool same = g.Code == e.Code && g.Severity == e.Severity && g.LiftIndex == e.LiftIndex && g.Count == e.Count &&
                            g.Pct == e.Pct && g.More == e.More && g.Detail == e.Detail && g.IfFixed == e.IfFixed &&
                            g.Factor == e.Factor && g.BuildItem == e.BuildItem;
                Check(same, "hub C", $"{name} row {i}: got {Text(g)}, want {Text(e)}");
            }
        }

        private static string Text(HubProblem p) =>
            $"({p.Code}, {p.Severity}, lift {p.LiftIndex}, count {p.Count}, pct {p.Pct}, more {p.More}, detail {p.Detail}, " +
            $"ifFixed {p.IfFixed}, factor '{p.Factor}', build '{p.BuildItem}')";

        private static string Text(List<HubProblem> rows)
        {
            var parts = new List<string>();
            for (int i = 0; i < rows.Count; i++)
                parts.Add(Text(rows[i]));
            return string.Join("; ", parts);
        }

        private static void SectionC()
        {
            var o = new List<HubProblem>();
            const string elec = "ElectricitySupply";

            // P1 new area
            HubProblemInput x = In(0, 0, 100, 0, 0);
            x.BackcountryPct = 40;
            x.IfOneLodge = 10;
            Expect("P1 new area", x, o,
                P(HubProblemCode.NoLodge, 3, ifFixed: 10, build: "lodge"),
                P(HubProblemCode.NoLift, 1, pct: 40, build: "baseStation"),
                P(HubProblemCode.NoRuns, 1));

            // P2 Copper Bowl
            x = In(2, 2, 50, 3, 7);
            x.Factors = Fs(F(elec, -50));
            x.Lifts = Ls(L(0, true, true, true), L(0, false, true, true), L(4, false, true, false));
            x.RunsUnserved = 2;
            x.BackcountryPct = 40;
            x.AttractionFull = 69;
            HubProblemInput p2 = x;
            Expect("P2 Copper Bowl", x, o,
                P(HubProblemCode.Efficiency, 2, pct: 50, more: 0, ifFixed: 69, factor: elec),
                P(HubProblemCode.LiftNoPower, 2, liftIndex: 1),
                P(HubProblemCode.StationsOnly, 1, liftIndex: 2, detail: 1, build: "topStation"),
                P(HubProblemCode.RunsUnserved, 1, count: 2, pct: 40),
                P(HubProblemCode.Factor, 0, pct: -50, factor: elec));

            // P3 field test 6
            x = In(1, 1, 4, 1, 3);
            x.Factors = Fs(F("NotEnoughEmployees", -68), F(elec, -50), F("WaterSupply", -50), F("SewageHandling", -50));
            x.Lifts = Ls(L(0, false, true, true));
            x.AttractionFull = 49;
            Expect("P3 field test 6", x, o,
                P(HubProblemCode.Efficiency, 2, pct: 4, more: 3, ifFixed: 49, factor: "NotEnoughEmployees"),
                P(HubProblemCode.LiftNoPower, 2, liftIndex: 0),
                P(HubProblemCode.Factor, 0, pct: -68, factor: "NotEnoughEmployees"),
                P(HubProblemCode.Factor, 0, pct: -50, factor: elec),
                P(HubProblemCode.Factor, 0, pct: -50, factor: "WaterSupply"),
                P(HubProblemCode.Factor, 0, pct: -50, factor: "SewageHandling"));

            // P4 not running
            x = In(1, 1, 0, 1, 3);
            x.Factors = Fs(F(elec, -100), F("NotEnoughEmployees", -40));
            x.Lifts = Ls(L(0, true, true, true));
            x.AttractionFull = 49;
            Expect("P4 not running", x, o,
                P(HubProblemCode.Efficiency, 3, pct: 0, more: 1, ifFixed: 49, factor: elec),
                P(HubProblemCode.Factor, 0, pct: -100, factor: elec),
                P(HubProblemCode.Factor, 0, pct: -40, factor: "NotEnoughEmployees"));

            // P5 at 91 %
            x = In(1, 1, 91, 1, 3);
            x.Factors = Fs(F(elec, -9));
            x.Lifts = Ls(L(0, true, true, true));
            Expect("P5 at 91 %", x, o, P(HubProblemCode.Factor, 0, pct: -9, factor: elec));

            // P6 at 90 %
            x = In(1, 1, 90, 1, 3);
            x.Factors = Fs(F(elec, -10));
            x.Lifts = Ls(L(0, true, true, true));
            x.AttractionFull = 60;
            Expect("P6 at 90 %", x, o,
                P(HubProblemCode.Efficiency, 2, pct: 90, ifFixed: 60, factor: elec),
                P(HubProblemCode.Factor, 0, pct: -10, factor: elec));

            // P7 all lodges off
            x = In(2, 0, 100, 1, 10);
            x.IfLodgesOn = 96;
            x.Lifts = Ls(L(0, true, true, true));
            Expect("P7 all lodges off", x, o, P(HubProblemCode.LodgesAllOff, 3, count: 2, ifFixed: 96));

            // P8 some off
            x = In(3, 2, 100, 1, 5);
            x.Lifts = Ls(L(0, true, true, true));
            Expect("P8 some off", x, o, P(HubProblemCode.LodgesSomeOff, 1, count: 1));

            // P9 lift mix
            x = In(1, 1, 100, 4, 2);
            x.Lifts = Ls(L(2, true, true, true), L(3, true, true, false), L(1, true, true, true), L(0, false, true, true));
            Expect("P9 lift mix", x, o,
                P(HubProblemCode.LiftNoPower, 2, liftIndex: 3),
                P(HubProblemCode.LiftSectionMissing, 2, liftIndex: 0, build: "lift"),
                P(HubProblemCode.LiftUnfinished, 2, liftIndex: 1, build: "lift"),
                P(HubProblemCode.LiftChairsStopped, 1, liftIndex: 2));

            // P10 Not linked
            x = new HubProblemInput { Unlinked = true, Runs = 2, LiftRows = 1, FreeLodges = 1, EfficiencyPct = -1, AttractionFull = -1, IfOneLodge = -1, IfLodgesOn = -1 };
            Expect("P10 Not linked", x, o,
                P(HubProblemCode.Unlinked, 3, build: "sign"),
                P(HubProblemCode.FreeLodges, 1, count: 1));

            // P11 cap
            x = In(1, 1, 100, 12, 3);
            x.Lifts = new List<HubProblemLift>();
            for (int i = 0; i < 12; i++)
                x.Lifts.Add(L(0, false, true, true));
            var p11 = new HubProblem[8];
            for (int i = 0; i < 8; i++)
                p11[i] = P(HubProblemCode.LiftNoPower, 2, liftIndex: i);
            Expect("P11 cap", x, o, p11);

            // P12 backcountry 100 %
            x = In(1, 1, 100, 1, 4);
            x.Lifts = Ls(L(0, true, true, true));
            x.RunsUnserved = 3;
            x.BackcountryPct = 100;
            Expect("P12 backcountry 100 %", x, o);

            // P13 stations variants
            x = In(1, 1, 100, 3, 1);
            x.Lifts = Ls(L(4, false, false, true), L(4, false, true, true), L(4, false, false, false));
            Expect("P13 stations variants", x, o,
                P(HubProblemCode.StationsOnly, 1, liftIndex: 0, detail: 2, build: "baseStation"),
                P(HubProblemCode.StationsOnly, 1, liftIndex: 1, detail: 0, build: "lift"));

            // P14 rating
            x = In(1, 1, 100, 1, 5);
            x.Lifts = Ls(L(0, true, true, true));
            x.RunsUnrateable = 1;
            x.RunsRating = 3;
            Expect("P14 rating", x, o,
                P(HubProblemCode.RunsUnrateable, 1, count: 1),
                P(HubProblemCode.RunsRating, 1, count: 3));

            // P18 flat (second pass)
            x = In(1, 1, 100, 1, 3);
            x.Lifts = Ls(L(0, true, true, true));
            x.RunsFlat = 2;
            Expect("P18 flat", x, o, P(HubProblemCode.RunsFlat, 1, count: 2));

            // P19 flat sorts last
            x = In(1, 1, 100, 1, 4);
            x.Lifts = Ls(L(0, true, true, true));
            x.RunsUnrateable = 1;
            x.RunsFlat = 1;
            Expect("P19 flat sorts last", x, o,
                P(HubProblemCode.RunsUnrateable, 1, count: 1),
                P(HubProblemCode.RunsFlat, 1, count: 1));

            // E1 fixer (e1-spec §8.9 F9): lone stations on DIFFERENT rows pair up (the census never builds a
            // state-4 row with both stations). P20-P23 add 2 + 2 + 3 + 3 = 10 checks.
            // E1 fixer pass 2 (e1-spec §8.10 G4): the pair row sits at the row whose name the finished lift
            // keeps — the TOP's row ("Lift N" is numbered by top height, rows without a top last), or the
            // base's row when the base has a custom name. P20-P22 moved to the top's row; P24-P26 add 2 + 2 + 3.
            // P20 a base and a top placed, no line yet → one "draw the lift line" row at the top's row.
            x = In(1, 1, 100, 2, 1);
            x.Lifts = Ls(L(4, false, true, false), L(4, false, false, true));
            Expect("P20 stations pair across rows", x, o,
                P(HubProblemCode.StationsOnly, 1, liftIndex: 1, detail: 0, build: "lift"));

            // P21 top listed first (the stats order: rows with a top first): the pair row at index 0.
            x = In(1, 1, 100, 2, 1);
            x.Lifts = Ls(L(4, false, false, true), L(4, false, true, false));
            Expect("P21 top first", x, o,
                P(HubProblemCode.StationsOnly, 1, liftIndex: 0, detail: 0, build: "lift"));

            // P22 two bases and one top: the first base pairs (row at the top, index 2), the second still
            // needs its top (Detail 1 at index 1, listed first: equal keys keep lift order).
            x = In(1, 1, 100, 3, 1);
            x.Lifts = Ls(L(4, false, true, false), L(4, false, true, false), L(4, false, false, true));
            Expect("P22 two bases one top", x, o,
                P(HubProblemCode.StationsOnly, 1, liftIndex: 1, detail: 1, build: "topStation"),
                P(HubProblemCode.StationsOnly, 1, liftIndex: 2, detail: 0, build: "lift"));

            // P24 the base has a custom name: the finished lift keeps it, so the pair row is the base's.
            x = In(1, 1, 100, 2, 1);
            x.Lifts = Ls(L(4, false, false, true), L(4, false, true, false, named: true));
            Expect("P24 named base keeps the row", x, o,
                P(HubProblemCode.StationsOnly, 1, liftIndex: 1, detail: 0, build: "lift"));

            // P25 only the top has a custom name: the finished lift takes it (no base name), the top's row.
            x = In(1, 1, 100, 2, 1);
            x.Lifts = Ls(L(4, false, false, true, named: true), L(4, false, true, false));
            Expect("P25 named top keeps the row", x, o,
                P(HubProblemCode.StationsOnly, 1, liftIndex: 0, detail: 0, build: "lift"));

            // P26 two pairs: tops t0, t1 (rows 0, 1), bases b0 named (row 2), b1 (row 3). Pair 1 = (b0, t0)
            // → b0's row 2; pair 2 = (b1, t1) → t1's row 1. Lift order: row 1, then row 2.
            x = In(1, 1, 100, 4, 1);
            x.Lifts = Ls(L(4, false, false, true), L(4, false, false, true), L(4, false, true, false, named: true),
                L(4, false, true, false));
            Expect("P26 two pairs, one named base", x, o,
                P(HubProblemCode.StationsOnly, 1, liftIndex: 1, detail: 0, build: "lift"),
                P(HubProblemCode.StationsOnly, 1, liftIndex: 2, detail: 0, build: "lift"));

            // P23 a line drawn from the base (Unfinished) and a lone top: finish the line to the top.
            x = In(1, 1, 100, 2, 1);
            x.Lifts = Ls(L(3, true, true, false), L(4, false, false, true));
            Expect("P23 unfinished line and a lone top", x, o,
                P(HubProblemCode.LiftUnfinished, 2, liftIndex: 0, build: "lift"),
                P(HubProblemCode.StationsOnly, 1, liftIndex: 1, detail: 0, build: "lift"));

            // Round F7 D5 (field-test-7-fixes.md): road edges. P27-P31 add 2 + 9 + 3 + 2 + 2 + 3 = 21 checks.
            // P27 the sign alone is off the road: one SignNoRoad row, severity 3, nothing else set.
            x = In(1, 1, 100, 1, 3);
            x.Lifts = Ls(L(0, true, true, true));
            x.SignNoRoad = true;
            Expect("P27 sign not at a road", x, o, P(HubProblemCode.SignNoRoad, 3));

            // P28 field test 7 with neither on a road: sign and both lodges off the road, the three utilities at
            // 50 % (efficiency 12). Severity 3 first, code ascending (18 before 19), then Efficiency (2), NoLift,
            // NoRuns; Factor rows last.
            x = In(2, 2, 12, 0, 0);
            x.SignNoRoad = true;
            x.LodgesNoRoad = 2;
            x.Factors = Fs(F("SewageHandling", -50), F("WaterSupply", -50), F(elec, -50));
            x.BackcountryPct = 40;
            x.AttractionFull = 69;
            Expect("P28 sign and every lodge off the road", x, o,
                P(HubProblemCode.SignNoRoad, 3),
                P(HubProblemCode.LodgeNoRoad, 3, count: 2),
                P(HubProblemCode.Efficiency, 2, pct: 12, more: 2, ifFixed: 69, factor: "SewageHandling"),
                P(HubProblemCode.NoLift, 1, pct: 40, build: "baseStation"),
                P(HubProblemCode.NoRuns, 1),
                P(HubProblemCode.Factor, 0, pct: -50, factor: "SewageHandling"),
                P(HubProblemCode.Factor, 0, pct: -50, factor: "WaterSupply"),
                P(HubProblemCode.Factor, 0, pct: -50, factor: elec));

            // P29 one of three lodges off the road: severity 1 (as LodgesSomeOff), count 1; sorts after the
            // severity-1 LodgesSomeOff (code 3 < 19).
            x = In(3, 2, 100, 1, 4);
            x.Lifts = Ls(L(0, true, true, true));
            x.LodgesNoRoad = 1;
            Expect("P29 some lodges off the road", x, o,
                P(HubProblemCode.LodgesSomeOff, 1, count: 1),
                P(HubProblemCode.LodgeNoRoad, 1, count: 1));

            // P30 no lodge: NoLodge only (LodgesNoRoad is ignored without a lodge); a count above Lodges clamps.
            x = In(0, 0, 100, 1, 3);
            x.Lifts = Ls(L(0, true, true, true));
            x.LodgesNoRoad = 1;
            x.IfOneLodge = 10;
            Expect("P30 no lodge, stray count", x, o, P(HubProblemCode.NoLodge, 3, ifFixed: 10, build: "lodge"));
            x = In(1, 1, 100, 1, 3);
            x.Lifts = Ls(L(0, true, true, true));
            x.LodgesNoRoad = 5;
            Expect("P30b count clamps to the lodges", x, o, P(HubProblemCode.LodgeNoRoad, 3, count: 1));

            // P31 Not linked ignores both road inputs (no sign; free lodges are counted apart).
            x = new HubProblemInput
            {
                Unlinked = true, Runs = 1, LiftRows = 0, FreeLodges = 2, EfficiencyPct = -1, AttractionFull = -1, IfOneLodge = -1,
                IfLodgesOn = -1, SignNoRoad = true, LodgesNoRoad = 2,
            };
            Expect("P31 Not linked ignores road inputs", x, o,
                P(HubProblemCode.Unlinked, 3, build: "sign"),
                P(HubProblemCode.FreeLodges, 1, count: 2));

            // P15 no result
            x = In(1, 1, -1, 1, 1);
            x.Factors = Fs(F(elec, -50));
            x.Lifts = Ls(L(0, true, true, true));
            Expect("P15 no result", x, o);

            // P16 null lists (no throw)
            x = In(1, 1, 100, 0, 2);
            x.Factors = null;
            x.Lifts = null;
            x.BackcountryPct = 0;
            bool threw = false;
            try
            {
                HubProblems.Build(in x, o);
            }
            catch (Exception e)
            {
                threw = true;
                Check(false, "hub C", "P16 null lists threw " + e.GetType().Name);
            }
            if (!threw)
                Rows("P16 null lists", o, new[] { P(HubProblemCode.NoLift, 1, pct: 0, build: "baseStation") });

            // P17 reuse: P2 into the list, then P1 into the same list → exactly P1's rows.
            HubProblems.Build(in p2, o);
            x = In(0, 0, 100, 0, 0);
            x.BackcountryPct = 40;
            x.IfOneLodge = 10;
            Expect("P17 reuse", x, o,
                P(HubProblemCode.NoLodge, 3, ifFixed: 10, build: "lodge"),
                P(HubProblemCode.NoLift, 1, pct: 40, build: "baseStation"),
                P(HubProblemCode.NoRuns, 1));
        }

        // ================================================================== D. status property

        private static void SectionD()
        {
            for (int runs = 0; runs <= 10; runs++)
            {
                for (int open = 0; open <= runs; open++)
                {
                    for (int partly = 0; partly <= runs - open; partly++)
                    {
                        ResortStatus s = HubRules.Status(runs, open, partly);
                        bool ok = (s == ResortStatus.Open) == (open == runs && runs > 0) &&
                                  (s == ResortStatus.Closed) == (open + partly == 0 && runs > 0) &&
                                  (s == ResortStatus.Partly) == (open + partly > 0 && open < runs) &&
                                  (s == ResortStatus.NoRuns) == (runs == 0);
                        Check(ok, "hub D", $"Status({runs},{open},{partly}) = {s}");
                    }
                }
            }
        }

        // ================================================================== E. new helpers

        private static void SectionE()
        {
            const string w = "hub E";
            Check(HubRules.ChairSpeedMps == 2.5f && HubRules.MinCountedFraction == 0.25f && HubRules.EfficiencyProblemPct == 90 &&
                  HubRules.MaxBandRows == 8 && HubRules.LiftLinePerKm == 7500 && HubRules.TicksPerDay == 262144 &&
                  HubRules.FollowMinIntervalS == 2f && HubRules.FollowQuietAfterUserS == 2f, w, "a HubRules constant moved: the oracles are literal for it");

            Eq((int)HubProblemCode.Unlinked, 0, w, "HubProblemCode.Unlinked");
            Eq((int)HubProblemCode.Efficiency, 4, w, "HubProblemCode.Efficiency");
            Eq((int)HubProblemCode.StationsOnly, 10, w, "HubProblemCode.StationsOnly");
            Eq((int)HubProblemCode.Factor, 16, w, "HubProblemCode.Factor");
            Eq((int)HubProblemCode.RunsFlat, 17, w, "HubProblemCode.RunsFlat");
            Eq((int)HubProblemCode.SignNoRoad, 18, w, "HubProblemCode.SignNoRoad");     // Round F7 D5
            Eq((int)HubProblemCode.LodgeNoRoad, 19, w, "HubProblemCode.LodgeNoRoad");   // Round F7 D5
            Eq(Enum.GetValues(typeof(HubProblemCode)).Length, 20, w, "HubProblemCode has 20 values (append only)");
            Eq((int)SeasonHint.GatingOff, 6, w, "SeasonHint.GatingOff");
            Eq((int)RunOpenState.None, -1, w, "RunOpenState.None");
            Eq((int)ResortStatus.Open, 3, w, "ResortStatus.Open");
            Eq((int)HubLiftStatus.Unfinished, 4, w, "HubLiftStatus.Unfinished");
            Eq((int)NextStepKind.Run, 3, w, "NextStepKind.Run");
            Eq((int)FollowVerdict.TooSoon, 4, w, "FollowVerdict.TooSoon");
            Eq((int)PisteStyle.DoubleBlack, 6, w, "PisteStyle.DoubleBlack");
            Check(PisteMapRules.KindNatural == (int)RunKind.Natural && PisteMapRules.KindNatural == 3, w,
                $"PisteMapRules.KindNatural {PisteMapRules.KindNatural} vs RunKind.Natural {(int)RunKind.Natural}");

            // HubSnowSums
            var sums = new HubSnowSums();
            sums.Add(20000, 1.0, 0.12, false);
            sums.Add(10000, 0.5, double.NaN, true);
            sums.Add(0, 1.0, 0.3, false);
            sums.Add(5000, double.NaN, 0.2, false);
            NearF(sums.SnowPct(), 83.3333, 1e-3, w, "HubSnowSums.SnowPct (4 adds)");
            NearF(sums.DepthCm(), 13.6, 1e-4, w, "HubSnowSums.DepthCm (4 adds)");
            Eq(sums.Runs, 2, w, "HubSnowSums.Runs (4 adds)");
            Eq(sums.AnyEstimated, true, w, "HubSnowSums.AnyEstimated (4 adds)");
            var empty = new HubSnowSums();
            Check(empty.SnowPct() == -1f && empty.DepthCm() == -1f && !empty.AnyEstimated, w,
                $"empty HubSnowSums: {empty.SnowPct()} / {empty.DepthCm()} / {empty.AnyEstimated}");
            var nanArea = new HubSnowSums();
            nanArea.Add(double.NaN, 0.5, 0.1, true);
            Check(nanArea.Runs == 0 && nanArea.AnyEstimated && nanArea.SnowPct() == -1f, w,
                $"Add(NaN area): runs {nanArea.Runs}, estimated {nanArea.AnyEstimated}, pct {nanArea.SnowPct()}");
            var over = new HubSnowSums();
            over.Add(1000, 1.5, double.NaN, false);
            NearF(over.SnowPct(), 100.0, 1e-4, w, "Add(share 1.5) clamps to 100 %");

            // TopTempC
            NearF(HubRules.TopTempC(-2f, 1800f, 1000f, 0.006f), -6.8, 1e-4, w, "TopTempC(-2,1800,1000,0.006)");
            NearF(HubRules.TopTempC(5f, 1000f, 1000f, 0.006f), 5.0, 1e-4, w, "TopTempC(5,1000,1000,0.006)");
            NearF(HubRules.TopTempC(1f, 500f, 1000f, 0.01f), 6.0, 1e-4, w, "TopTempC(1,500,1000,0.01)");
            Check(float.IsNaN(HubRules.TopTempC(float.NaN, 1800f, 1000f, 0.006f)), w, "TopTempC(NaN,…) is not NaN");
            Check(float.IsNaN(HubRules.TopTempC(0f, float.PositiveInfinity, 1000f, 0.006f)), w, "TopTempC(0,+∞,…) is not NaN");

            // Percent
            Eq(HubRules.Percent(0.5), 50, w, "Percent(0.5)");
            Eq(HubRules.Percent(0.125), 13, w, "Percent(0.125)");
            Eq(HubRules.Percent(1.2), 100, w, "Percent(1.2)");
            Eq(HubRules.Percent(-0.3), 0, w, "Percent(-0.3)");
            Eq(HubRules.Percent(0), 0, w, "Percent(0)");
            Eq(HubRules.Percent(double.NaN), -1, w, "Percent(NaN)");
            Eq(HubRules.Percent(double.PositiveInfinity), -1, w, "Percent(+∞)");

            // FollowCheck (order matters)
            float ni = float.NegativeInfinity;
            Eq(HubRules.FollowCheck(false, false, 10f, ni, ni), FollowVerdict.HubClosed, w, "FollowCheck(false,false,10,−∞,−∞)");
            Eq(HubRules.FollowCheck(true, true, 10f, ni, ni), FollowVerdict.RuleSwitched, w, "FollowCheck(true,true,10,−∞,−∞)");
            Eq(HubRules.FollowCheck(true, false, 10f, ni, 9f), FollowVerdict.UserJustSelected, w, "FollowCheck(true,false,10,−∞,9)");
            Eq(HubRules.FollowCheck(true, false, 10f, 8.5f, 0f), FollowVerdict.TooSoon, w, "FollowCheck(true,false,10,8.5,0)");
            Eq(HubRules.FollowCheck(true, false, 10f, 8f, 8f), FollowVerdict.Follow, w, "FollowCheck(true,false,10,8,8)");
            Eq(HubRules.FollowCheck(true, false, 10f, ni, ni), FollowVerdict.Follow, w, "FollowCheck(true,false,10,−∞,−∞)");
            Eq(HubRules.FollowCheck(true, true, 10f, 9.9f, 9.9f), FollowVerdict.RuleSwitched, w, "FollowCheck(true,true,10,9.9,9.9)");
            Eq(HubRules.FollowCheck(true, false, 10f, 9f, 7f), FollowVerdict.TooSoon, w, "FollowCheck(true,false,10,9,7)");
        }

        // ================================================================== F. piste-map rules

        private static List<float3> Square() => new List<float3>
        {
            new float3(0, 0, 0), new float3(30, 0, 0), new float3(30, 0, 30), new float3(0, 0, 30),
        };

        private static void Piece(List<float3> a, List<float3> b, int i, float3 wantA, float3 wantB, string name)
        {
            bool ok = i < a.Count && i < b.Count && NearV(a[i], wantA) && NearV(b[i], wantB);
            Check(ok, "hub F", i < a.Count && i < b.Count
                ? $"{name} piece {i}: ({a[i].x},{a[i].y},{a[i].z})-({b[i].x},{b[i].y},{b[i].z}), want ({wantA.x},{wantA.y},{wantA.z})-({wantB.x},{wantB.y},{wantB.z})"
                : $"{name}: no piece {i} ({a.Count} piece(s))");
        }

        private static bool NearV(float3 a, float3 b) => Near(a.x, b.x, 1e-4) && Near(a.y, b.y, 1e-4) && Near(a.z, b.z, 1e-4);

        private static void SectionF()
        {
            const string w = "hub F";
            // StyleOf
            Eq(PisteMapRules.StyleOf(-2), PisteStyle.Unrateable, w, "StyleOf(-2)");
            Eq(PisteMapRules.StyleOf(-1), PisteStyle.Pending, w, "StyleOf(-1)");
            Eq(PisteMapRules.StyleOf(0), PisteStyle.Flat, w, "StyleOf(0)");
            Eq(PisteMapRules.StyleOf(1), PisteStyle.Green, w, "StyleOf(1)");
            Eq(PisteMapRules.StyleOf(2), PisteStyle.Blue, w, "StyleOf(2)");
            Eq(PisteMapRules.StyleOf(3), PisteStyle.Black, w, "StyleOf(3)");
            Eq(PisteMapRules.StyleOf(4), PisteStyle.DoubleBlack, w, "StyleOf(4)");
            Eq(PisteMapRules.StyleOf(5), PisteStyle.Pending, w, "StyleOf(5)");
            Eq(PisteMapRules.StyleOf(-3), PisteStyle.Pending, w, "StyleOf(-3)");

            // IsDashed
            Eq(PisteMapRules.IsDashed(3), true, w, "IsDashed(3)");
            Eq(PisteMapRules.IsDashed(0), false, w, "IsDashed(0)");
            Eq(PisteMapRules.IsDashed(1), false, w, "IsDashed(1)");
            Eq(PisteMapRules.IsDashed(2), false, w, "IsDashed(2)");

            // WidthScale
            float[] zooms = { 0f, -5f, float.NaN, 500f, 750f, 1500f, 3000f, 4500f, 9000f, float.PositiveInfinity };
            float[] scales = { 1f, 1f, 1f, 1f, 1f, 2f, 4f, 6f, 6f, 6f };
            for (int i = 0; i < zooms.Length; i++)
                NearF(PisteMapRules.WidthScale(zooms[i]), scales[i], 1e-4, w, $"WidthScale({zooms[i]})");

            var a = new List<float3>();
            var b = new List<float3>();

            // SolidRing(S, 100)
            int n = PisteMapRules.SolidRing(Square(), 100, a, b);
            Eq(n, 4, w, "SolidRing(S,100) count");
            Piece(a, b, 3, new float3(0, 0, 30), new float3(0, 0, 0), "SolidRing(S,100)");

            // DashRing(S, 10, 5, 100)
            a.Clear(); b.Clear();
            n = PisteMapRules.DashRing(Square(), 10f, 5f, 100, a, b);
            Eq(n, 8, w, "DashRing(S,10,5,100) count");
            Piece(a, b, 0, new float3(0, 0, 0), new float3(10, 0, 0), "DashRing(S,10,5,100)");
            Piece(a, b, 1, new float3(15, 0, 0), new float3(25, 0, 0), "DashRing(S,10,5,100)");
            Piece(a, b, 2, new float3(30, 0, 0), new float3(30, 0, 10), "DashRing(S,10,5,100)");
            Piece(a, b, 7, new float3(0, 0, 15), new float3(0, 0, 5), "DashRing(S,10,5,100)");

            // DashRing(S, 8, 4, 100): dashes cross corners
            a.Clear(); b.Clear();
            n = PisteMapRules.DashRing(Square(), 8f, 4f, 100, a, b);
            Eq(n, 12, w, "DashRing(S,8,4,100) count");
            Piece(a, b, 2, new float3(24, 0, 0), new float3(30, 0, 0), "DashRing(S,8,4,100)");
            Piece(a, b, 3, new float3(30, 0, 0), new float3(30, 0, 2), "DashRing(S,8,4,100)");
            Piece(a, b, 8, new float3(6, 0, 30), new float3(0, 0, 30), "DashRing(S,8,4,100)");
            Piece(a, b, 11, new float3(0, 0, 12), new float3(0, 0, 4), "DashRing(S,8,4,100)");

            // Truncation and the solid fallback
            a.Clear(); b.Clear();
            Eq(PisteMapRules.DashRing(Square(), 10f, 5f, 5, a, b), 5, w, "DashRing(S,10,5,5) truncated");
            Eq(a.Count, 5, w, "DashRing(S,10,5,5) pieces appended");
            a.Clear(); b.Clear();
            Eq(PisteMapRules.DashRing(Square(), 0f, 5f, 100, a, b), 4, w, "DashRing(S,0,5,100) solid fallback");
            a.Clear(); b.Clear();
            Eq(PisteMapRules.DashRing(Square(), 10f, -1f, 100, a, b), 4, w, "DashRing(S,10,-1,100) solid fallback");

            // Sloped ring: y interpolated
            var y = new List<float3> { new float3(0, 100, 0), new float3(30, 130, 0), new float3(30, 130, 30), new float3(0, 100, 30) };
            a.Clear(); b.Clear();
            PisteMapRules.DashRing(y, 10f, 5f, 100, a, b);
            Piece(a, b, 0, new float3(0, 100, 0), new float3(10, 110, 0), "DashRing(Y,10,5,100)");

            // A non-finite node: its two edges are skipped and add no length
            var bad = new List<float3> { new float3(0, 0, 0), new float3(30, 0, 0), new float3(float.NaN, 0, 30), new float3(0, 0, 30) };
            a.Clear(); b.Clear();
            Eq(PisteMapRules.SolidRing(bad, 100, a, b), 2, w, "SolidRing(NaN node)");
            a.Clear(); b.Clear();
            Eq(PisteMapRules.DashRing(bad, 10f, 5f, 100, a, b), 4, w, "DashRing(NaN node,10,5,100)");
            Piece(a, b, 2, new float3(0, 0, 30), new float3(0, 0, 20), "DashRing(NaN node)");
            Piece(a, b, 3, new float3(0, 0, 15), new float3(0, 0, 5), "DashRing(NaN node)");

            // One node, empty ring
            var one = new List<float3> { new float3(1, 2, 3) };
            var none = new List<float3>();
            a.Clear(); b.Clear();
            Check(PisteMapRules.SolidRing(one, 100, a, b) == 0 && PisteMapRules.DashRing(one, 10f, 5f, 100, a, b) == 0 &&
                  PisteMapRules.SolidRing(none, 100, a, b) == 0 && PisteMapRules.DashRing(none, 10f, 5f, 100, a, b) == 0 && a.Count == 0,
                w, "a one-node or empty ring must give 0 pieces");

            // A ring whose pieces are all shorter than MinPieceM
            var tiny = new List<float3> { new float3(0, 0, 0), new float3(0.02f, 0, 0) };
            Eq(PisteMapRules.DashRing(tiny, 10f, 5f, 100, a, b), 0, w, "DashRing(tiny ring)");
            Eq(a.Count, 0, w, "DashRing(tiny ring) appended nothing");

            // Appending: a second call adds after the first call's pieces
            a.Clear(); b.Clear();
            PisteMapRules.SolidRing(Square(), 100, a, b);
            int second = PisteMapRules.DashRing(Square(), 10f, 5f, 100, a, b);
            Check(second == 8 && a.Count == 12 && b.Count == 12, w, $"appending: second call {second}, lists {a.Count} / {b.Count} (want 8, 12 / 12)");
            Piece(a, b, 4, new float3(0, 0, 0), new float3(10, 0, 0), "appending (first dash after the 4 solid pieces)");
        }

        // ================================================================== G. the aggregate's run vertical

        private static SkiResortAggregate.RunIn Run(int resort, bool rated, sbyte band, float len, float area, float top, float bottom,
                                                    float cost, float snowHa, float cover, float natural)
        {
            return new SkiResortAggregate.RunIn
            {
                Resort = resort, RateState = (byte)(rated ? 1 : 0), Band = rated ? band : (sbyte)-1, LengthM = len, AreaM2 = area,
                TopM = top, BottomM = bottom, CostMonth = cost, SnowHa = snowHa, Cover = cover, NaturalCover = natural,
            };
        }

        private static SkiResortAggregate.LiftIn Lift(int resort, int id, byte state, int chairs, float len, float rise, float baseY, float topY)
        {
            return new SkiResortAggregate.LiftIn
            {
                Resort = resort, Id = new EntityRef(id, 1), State = state, Chairs = chairs, LengthM = len, RiseM = rise, BaseTipY = baseY, TopTipY = topY,
            };
        }

        private static void SectionG()
        {
            const string w = "hub G";
            // Program.AggregateTests' fixture, copied literally.
            var runs = new List<SkiResortAggregate.RunIn>
            {
                Run(0, true, 2, 500f, 20000f, 1500f, 1300f, 100f, 1.0f, 1.0f, 0.0f),
                Run(0, true, 3, 300f, 10000f, 1600f, 1450f, 40f, 0.5f, 0.5f, 0.5f),
                Run(0, false, -1, 200f, 5000f, 1400f, 1350f, 0f, 0f, 0f, 0f),
                Run(-1, false, -1, 100f, 3000f, 900f, 850f, 0f, 0f, float.NaN, float.NaN),
                Run(1, true, 1, 250f, 8000f, 2000f, 1900f, 0f, 0f, 0f, 0f),
                Run(7, true, 4, 50f, 1000f, 700f, 650f, 0f, 0f, float.NaN, float.NaN),
            };
            var lifts = new List<SkiResortAggregate.LiftIn>
            {
                Lift(0, 1, 0, 40, 800f, 300f, 1250f, 1700f),
                Lift(0, 2, 2, 30, 500f, 200f, 1300f, 1650f),
                Lift(-1, 3, 4, 0, 0f, 0f, 950f, float.NaN),
            };
            var lodges = new List<SkiResortAggregate.LodgeIn>
            {
                new SkiResortAggregate.LodgeIn { Resort = 0, Score = 25, Free = false },
                new SkiResortAggregate.LodgeIn { Resort = -1, Score = 10, Free = true },
            };
            var result = new SkiResortAggregate.Agg[3];
            SkiResortAggregate.Aggregate(runs, lifts, lodges, 2, result);
            Vertical(result[0], 300f, 450f, w, "resort 0");
            Vertical(result[1], 100f, 100f, w, "resort 1");
            Vertical(result[2], 250f, 250f, w, "not linked");

            var res2 = new SkiResortAggregate.Agg[2];
            var onlyLift = new List<SkiResortAggregate.LiftIn> { Lift(0, 9, 1, 0, 400f, 120f, 1000f, 1120f) };
            SkiResortAggregate.Aggregate(new List<SkiResortAggregate.RunIn>(), onlyLift, null, 1, res2);
            Vertical(res2[0], -1f, 120f, w, "stations only");
            var topOnly = new List<SkiResortAggregate.LiftIn> { Lift(0, 9, 4, 0, 0f, 0f, float.NaN, 1500f) };
            SkiResortAggregate.Aggregate(null, topOnly, null, 1, res2);
            Vertical(res2[0], -1f, -1f, w, "top station only");
        }

        private static void Vertical(SkiResortAggregate.Agg a, float run, float all, string w, string name)
        {
            Check(a.RunVerticalM == run, w, $"{name}: RunVerticalM {a.RunVerticalM}, want {run}");
            Check(a.VerticalM == all, w, $"{name}: VerticalM {a.VerticalM}, want {all} (unchanged, C14)");
        }

        // ================================================================== helpers

        private static void Eq<T>(T got, T want, string where, string call)
        {
            Check(EqualityComparer<T>.Default.Equals(got, want), where, $"{call} = {got}, want {want}");
        }

        private static void NearF(double got, double want, double eps, string where, string call)
        {
            Check(Near(got, want, eps), where, $"{call} = {got}, want {want} (± {eps})");
        }
    }
}
