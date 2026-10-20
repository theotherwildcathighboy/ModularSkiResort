// HubRulesTests — Round E1 test file for tests/ResortBatch (econ-round spec ch5 §10.1: hub rules, problems,
// piste-map rules, the aggregate's run vertical, sections A-G; Round S1 adds H). Owner: stream S5 (docs/research/econ-round/
// e1-spec.md §3.2). Created empty by stage 0 (S0, 2026-10-04); written 2026-10-04 by S5.
// e1-spec R9 — `public static class HubRulesTests` with `public static void Run(Func<bool, string, string, bool> check)`
// and its own Near; Program.Main calls HubRulesTests.Run(Check).
//
// ORACLES are literal hand values (ch5 §10.1 tables), never a second run of the same code:
//   A  the small rules of HubRules (Status, OpenStateOf, Season, NextStep, LiftStatusOf, RideTimeS,
//      OpenSharePct, FactorEffectPct, CountedFraction, TicketIncomeMonth, NetMonth, LiftNoPair; B1 2026-10-19: NetMonth over
//      HubMonthMoney, MonthMoneyOf, MonthShare, RateSoFar and the invariant "the Finances rows add up to the Net");
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
//      unchanged VerticalM (C14);
//   H  Round S1 (2026-10-05, stream D; docs/research/ski-days/spec/ch4-hub.md §G.3): HubRules.SkiDayStateOf rows
//      H1-H15 and H16, the powder-mark parity of C# and TS (SkiDayRules.PowderMark vs Percent ≥ 50, s1-spec N10).
// Round S1 also adds, in C, P32-P41 (ski-day rows 20-22, F1's Efficiency hysteresis) and P42-P44 (F1b: a Factor row
// only from a 5-point loss, orchestrator ruling R-Q2), and in E the three codes, HubSkiDayState and the two constants.
// Fixer pass (2026-10-05, review HU-1): P45, the band row's "more" counts only the listed factors.
// Round M1 "measure first" (Builder B, 2026-10-05; m1-spec §4 and M10 D9): P46, no LodgeNotSlopeside row on an area
// without runs (the spec's "P42"; that number was taken by F1b). P33-P35 keep runs 3 and pass unchanged.
// Round M2 (builder B3, 2026-10-10; docs/research/round-m2/build-contract.md §7): section A's twelve Season rows gain
// `false, false` (gunsLiveKnown, anyGunOn: the fallback is the old rule, expected values unchanged); H17 the price
// factor's shared literal table with test-hub-rules.js T10.1 and the 40 / 200 edge (2 checks); H18 eight Season rows
// with the live gun gates (R5). HubRulesTests +10.
// Catalog Phase B (lift group, 2026-10-11; docs/research/art/assets-2026-10/code-round-2/round-2-contract.md §11.2 with
// round-2-contract-skeptic.md MUST 1): B's summer synthetic row carries ScoreFlags.SummerMode (AttractionWithLodges reads
// the A-frame's season share from the flag, not from LodgeShare) and L10's oracle moves 61 → 73 (LodgeBaseOf(10) 32 → 44);
// E gains codes 23-25 (26 values); new section I — RideTimeS per speed, MUST 1's summer-no-lodge row, AttractionWithLodgeApplied,
// LodgePart = LodgeApplied, problems 23-25 and the per-type build items / pairing, H17 with the price tolerance.
// Catalog Phase C (rules group, 2026-10-11; docs/research/art/assets-2026-10/code-round-3/round-3-contract.md §8.3 with
// round-3-contract-skeptic.md MUST 4): new section J — the tram's hub numbers, HubRules.ShuttleRideTimeS (with its dwell
// parameter and the ShuttleRideIncludesDwell switch) and ShuttleCapacityPerHour, over the pure ShuttleProfile.cs (linked
// into this host); the float oracles HJ-1/2/3/5 are bit-exact (the skeptic recomputed them with Math.fround); the brief's
// a → ∞ figures (code-economy A.2: 2,250 / 1,000 pph); the catalog's tram row through both; C14's status mapping.
// Phase D (rules group, 2026-10-11; docs/research/art/assets-2026-10/code-round-4/round-4-contract.md §7.2): new section K —
// HubRules.LiftCapacityShown (capacity "–" for a lift that does not carry, every type), LiftStatusOf restated for a held lift,
// and LiftServedRule.Carries (the bool overload; D1's equivalence with SkiMoneyRules.LiftOperating).
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
            SectionH();   // Round S1 (ch4 §G.3, refutation RF-12)
            SectionI();   // Catalog Phase B (round-2-contract §11.2, lift group)
            SectionJ();   // Catalog Phase C (round-3-contract §8.3, rules group)
            SectionK();   // Phase D (round-4-contract §7.2, rules group)
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
            Eq(HubRules.Season(false, 7, 7, true, false, false, true, 5f, 0f, 2f), SeasonHint.GatingOff, w, "Season(false,7,7,true,false,false,true,5,0,2)");
            Eq(HubRules.Season(true, 0, 0, false, false, false, true, -5f, 0f, 2f), SeasonHint.None, w, "Season(true,0,0,false,false,false,true,-5,0,2)");
            Eq(HubRules.Season(true, 7, 0, false, false, false, false, -5f, 0f, 2f), SeasonHint.None, w, "Season(true,7,0,false,false,false,false,-5,0,2)");
            Eq(HubRules.Season(true, 7, 0, false, false, false, true, float.NaN, 0f, 2f), SeasonHint.None, w, "Season(true,7,0,false,false,false,true,NaN,0,2)");
            Eq(HubRules.Season(true, 7, 5, false, false, false, true, -3f, 0f, 2f), SeasonHint.SnowSeason, w, "Season(true,7,5,false,false,false,true,-3,0,2)");
            Eq(HubRules.Season(true, 7, 5, true, false, false, true, 1.5f, 0f, 2f), SeasonHint.SnowGunsOnly, w, "Season(true,7,5,true,false,false,true,1.5,0,2)");
            Eq(HubRules.Season(true, 7, 5, false, false, false, true, 1.5f, 0f, 2f), SeasonHint.Thawing, w, "Season(true,7,5,false,false,false,true,1.5,0,2)");
            Eq(HubRules.Season(true, 7, 5, true, false, false, true, 2.5f, 0f, 2f), SeasonHint.Thawing, w, "Season(true,7,5,true,false,false,true,2.5,0,2)");
            Eq(HubRules.Season(true, 7, 0, false, false, false, true, -1f, 0f, 2f), SeasonHint.WaitingForSnow, w, "Season(true,7,0,false,false,false,true,-1,0,2)");
            Eq(HubRules.Season(true, 7, 0, true, false, false, true, 2.0f, 0f, 2f), SeasonHint.WaitingForSnow, w, "Season(true,7,0,true,false,false,true,2.0,0,2)");
            Eq(HubRules.Season(true, 7, 0, true, false, false, true, 2.1f, 0f, 2f), SeasonHint.TooWarm, w, "Season(true,7,0,true,false,false,true,2.1,0,2)");
            Eq(HubRules.Season(true, 7, 0, false, false, false, true, 0.5f, 0f, 2f), SeasonHint.TooWarm, w, "Season(true,7,0,false,false,false,true,0.5,0,2)");

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
            // T132 (field 2026-10-20 note 20): a held-closed complete lift reads Closed, chairs on the loop or not; No power wins.
            Eq(HubRules.LiftStatusOf(0, true, true), HubLiftStatus.Closed, w, "T132 LiftStatusOf(0,true,closed) = Closed (held chairs on the loop)");
            Eq(HubRules.LiftStatusOf(1, true, true), HubLiftStatus.Closed, w, "T132 LiftStatusOf(1,true,closed) = Closed");
            Eq(HubRules.LiftStatusOf(0, false, true), HubLiftStatus.NoPower, w, "T132 LiftStatusOf(0,false,closed) = NoPower (power first)");
            Eq(HubRules.LiftStatusOf(2, true, true), HubLiftStatus.SectionMissing, w, "T132 LiftStatusOf(2,true,closed) = SectionMissing");
            Eq(HubRules.LiftStatusOf(3, true, true), HubLiftStatus.Unfinished, w, "T132 LiftStatusOf(3,true,closed) = Unfinished");
            Eq(HubRules.LiftStatusOf(0, true, false), HubLiftStatus.Running, w, "T132 LiftStatusOf(0,true,open) = Running (the tram in October)");
            Eq((int)HubLiftStatus.Closed, 5, w, "T132 Closed appended as 5 (bindings.ts LIFT_STATUS.Closed)");

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

            // NetMonth (B1, 2026-10-19: the one Net over HubMonthMoney; was max(0, tickets) − cost rates). Terms in the order
            // tickets, lodge, lessons, passes | lift, run, buildings, wages.
            Net(Mm(8000, 1200, 500, 30000, 5091, 3600, 6000, 15000), 10009, false);       // 39,700 − 29,691
            Net(Mm(0, 0, 0, 120000, 0, 0, 3000, 9000), 108000, false);                    // a pre-season September: passes land when sold
            Net(Mm(8000, 0, 0, 0, 5091, 3600, -1, -1), -691, true);                       // the building bill unknown: read 0, "≈"
            Net(Mm(-1, -1, -1, -1, -1, -1, 6000, 15000), -21000, true);                   // the ledger not counting yet
            Net(Mm(0, 0, 0, 0, 450, 99, 0, 0), -549, false);                              // Not linked: minus the booked lift + run
            Net(Mm(int.MaxValue, 0, 0, int.MaxValue, 0, 0, 0, 0), int.MaxValue, false);   // clamped up
            Net(Mm(0, 0, 0, 0, int.MaxValue, int.MaxValue, 0, 0), int.MinValue, false);   // clamped down
            Net(HubMonthMoney.Unknown, 0, true);

            // MonthMoneyOf: the history element's mapping (ClampInt; LiftCharged half away from zero, NaN 0)
            var lm = new SkiResortLedger.Month
            {
                TicketIncome = 3_000_000_000L, LodgeSpendIncome = 11, LessonIncome = 13, PassIncome = 12, PassIncomePre = 5,
                LiftCharged = 449.5, RunUpkeep = 99,
            };
            HubMonthMoney mo = HubRules.MonthMoneyOf(lm, 70, -5);
            Check(mo.TicketIncome == int.MaxValue && mo.LodgeIncome == 11 && mo.LessonIncome == 13 && mo.PassIncome == 12 &&
                  mo.LiftCost == 450 && mo.RunCost == 99 && mo.BuildingsCost == 70 && mo.WagesCost == -1, "hub A B1",
                  $"MonthMoneyOf: ticket {mo.TicketIncome}, pass {mo.PassIncome} (pre-season counted once), lift {mo.LiftCost}, wages {mo.WagesCost}");
            lm.LiftCharged = double.NaN;
            Check(HubRules.MonthMoneyOf(lm, 0, 0).LiftCost == 0, "hub A B1", "MonthMoneyOf: NaN lift charge not 0");
            lm.LiftCharged = 2.5;
            Check(HubRules.MonthMoneyOf(lm, 0, 0).LiftCost == 3, "hub A B1", "MonthMoneyOf: 2.5 not rounded away from zero");

            // MonthShare / RateSoFar (the rate snapshots' part of the month so far)
            Check(HubRules.MonthShare(0, 100) == 0.0 && HubRules.MonthShare(50, 200) == 0.25 && HubRules.MonthShare(300, 200) == 1.0 &&
                  HubRules.MonthShare(10, 0) == 0.0 && HubRules.MonthShare(double.NaN, 100) == 0.0 && HubRules.MonthShare(-5, 100) == 0.0,
                  "hub A B1", "MonthShare");
            Check(HubRules.RateSoFar(1000, 0.25) == 250 && HubRules.RateSoFar(1001, 0.5) == 501 && HubRules.RateSoFar(-1, 0.5) == -1 &&
                  HubRules.RateSoFar(1000, double.NaN) == 0 && HubRules.RateSoFar(1000, 2.0) == 1000 && HubRules.RateSoFar(0, 0.7) == 0,
                  "hub A B1", "RateSoFar");

            // B1 INVARIANT: the Finances rows add up to the Net. The UI sums the eight rows it shows (contract 11: ticketsThisMonth,
            // lodgeIncomeMonth, lessonIncomeMonth, passIncomeMonth | liftChargedMonth, runCostMonth, buildingsSoFar, wagesSoFar), each
            // max(0, row) since −1 shows "–"; written out here as the UI will, over a grid that includes unknowns and zeros.
            int[] vals = { -1, 0, 1, 499, 120000 };
            int bad = 0, cases = 0;
            for (int a = 0; a < vals.Length; a++)
                for (int b = 0; b < vals.Length; b++)
                    for (int c = 0; c < vals.Length; c++)
                    {
                        HubMonthMoney t = Mm(vals[a], vals[(a + 1) % 5], vals[b], vals[(b + 2) % 5], vals[c], vals[(c + 3) % 5],
                                             vals[(a + c) % 5], vals[(b + c) % 5]);
                        long rows = Math.Max(0, t.TicketIncome) + (long)Math.Max(0, t.LodgeIncome) + Math.Max(0, t.LessonIncome) +
                                    Math.Max(0, t.PassIncome) - Math.Max(0, t.LiftCost) - (long)Math.Max(0, t.RunCost) -
                                    Math.Max(0, t.BuildingsCost) - Math.Max(0, t.WagesCost);
                        int net = HubRules.NetMonth(t, out bool est);
                        bool anyUnknown = t.TicketIncome < 0 || t.LodgeIncome < 0 || t.LessonIncome < 0 || t.PassIncome < 0 ||
                                          t.LiftCost < 0 || t.RunCost < 0 || t.BuildingsCost < 0 || t.WagesCost < 0;
                        cases++;
                        if (net != rows || est != anyUnknown)
                            bad++;
                    }
            Check(bad == 0 && cases == 125, "hub A B1 invariant", $"{bad} of {cases} cases: rows do not add up to NetMonth (or ≈ wrong)");

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

        private static HubMonthMoney Mm(int tickets, int lodge, int lessons, int passes, int lift, int run, int buildings, int wages) =>
            new HubMonthMoney
            {
                TicketIncome = tickets, LodgeIncome = lodge, LessonIncome = lessons, PassIncome = passes,
                LiftCost = lift, RunCost = run, BuildingsCost = buildings, WagesCost = wages,
            };

        private static void Net(in HubMonthMoney m, int want, bool wantEst)
        {
            int got = HubRules.NetMonth(m, out bool est);
            Check(got == want && est == wantEst, "hub A",
                $"NetMonth({m.TicketIncome},{m.LodgeIncome},{m.LessonIncome},{m.PassIncome} | {m.LiftCost},{m.RunCost},{m.BuildingsCost}," +
                $"{m.WagesCost}) = {got} est {est}; want {want} est {wantEst}");
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
            // Catalog Phase B (round-2-contract-skeptic MUST 1): the A-frame's season share comes from the Result's summer
            // flag, not from LodgeShare (a summer Result carries ScoreFlags.SummerMode; was `LodgeShare = 0.5` alone).
            var sw = new SkiResortScore.Result { AOpen = 4.898979486, LodgeBase = 0, LodgeShare = 0.5, Efficiency = 1,
                                                 Flags = SkiResortScore.ScoreFlags.SummerMode };
            Eq(HubRules.AttractionWithLodges(in sw, 1), 15, w, "AttractionWithLodges(W1 terrain, 1)");
            var s0 = new SkiResortScore.Result { AOpen = 71.585929905, LodgeBase = 0, LodgeShare = 1, Efficiency = 0 };
            Eq(HubRules.AttractionWithLodges(in s0, 1), 0, w, "AttractionWithLodges(efficiency 0, 1)");
            var s10 = new SkiResortScore.Result { AOpen = 28.882668052, LodgeBase = 0, LodgeShare = 1, Efficiency = 1 };
            // Catalog Phase B (MUST 1, §11.2 L10): LodgeBaseOf(10) = 20 + min(4 × 9, 24) = 44 (R11's cap 24; was 32 → 61).
            Eq(HubRules.AttractionWithLodges(in s10, 10), 73, w, "AttractionWithLodges(T1 terrain, 10) (L10: 44, was 61)");
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

            // T100 (2026-10-19, docs/research/ui/hub-school-launch-2026-10-19.md): a road-less top-level ski school joins the
            // LodgeNoRoad count but never its "every lodge" severity; unlinked schools get row 26 on the Not linked entry only.
            x = In(1, 1, 100, 1, 3);
            x.Lifts = Ls(L(0, true, true, true));
            x.SchoolsNoRoad = 1;
            Expect("T100a a school alone off the road: severity 1, count 1", x, o, P(HubProblemCode.LodgeNoRoad, 1, count: 1));
            x = In(2, 2, 100, 1, 3);
            x.Lifts = Ls(L(0, true, true, true));
            x.LodgesNoRoad = 2;
            x.SchoolsNoRoad = 1;
            Expect("T100b every lodge and the school off the road: severity 3, count 3", x, o, P(HubProblemCode.LodgeNoRoad, 3, count: 3));
            x = In(0, 0, 100, 1, 3);
            x.Lifts = Ls(L(0, true, true, true));
            x.IfOneLodge = 10;
            x.SchoolsNoRoad = 1;
            Expect("T100c no lodge, a school off the road: NoLodge, then the road row (severity 1)", x, o,
                P(HubProblemCode.NoLodge, 3, ifFixed: 10, build: "lodge"),
                P(HubProblemCode.LodgeNoRoad, 1, count: 1));
            x = new HubProblemInput
            {
                Unlinked = true, Runs = 0, LiftRows = 0, FreeLodges = 1, FreeSchools = 2, SchoolsNoRoad = 2, EfficiencyPct = -1,
                AttractionFull = -1, IfOneLodge = -1, IfLodgesOn = -1,
            };
            Expect("T100d Not linked: free lodges, then the unlinked schools; road inputs ignored", x, o,
                P(HubProblemCode.FreeLodges, 1, count: 1),
                P(HubProblemCode.SchoolsNotLinked, 1, count: 2));
            x = In(1, 1, 100, 1, 3);
            x.Lifts = Ls(L(0, true, true, true));
            x.FreeSchools = 3;
            Expect("T100e a sign's area ignores FreeSchools", x, o);

            // Round S1 (docs/research/ski-days/spec/ch4-hub.md §G.3): ski-day rows 20-22 (D28) and F1's Efficiency
            // hysteresis. P32-P41 add 2 + 8 + 3 + 2 + 2 + 3 + 3 + 2 + 3 + 1 = 29 checks. Every case keeps one powered,
            // complete lift unless given.
            // P32 guests cannot reach: one severity-2 row, no count.
            x = In(1, 1, 100, 1, 3);
            x.Lifts = Ls(L(0, true, true, true));
            x.SkiDaysCannotReach = true;
            Expect("P32 cannot reach", x, o, P(HubProblemCode.SkiDaysCannotReach, 2));

            // P33 the sort: severity 2 by code (Efficiency 4, LiftNoPower 5, SkiDaysCannotReach 20), then severity 1
            // by code (RunsFlat 17, NoParkingNearLodge 21, LodgeNotSlopeside 22), then the Factor row.
            x = In(1, 1, 50, 2, 3);
            x.Factors = Fs(F(elec, -50));
            x.Lifts = Ls(L(0, true, true, true), L(0, false, true, true));
            x.RunsFlat = 1;
            x.AttractionFull = 69;
            x.SkiDaysCannotReach = true;
            x.LodgesNoParking = 1;
            x.LodgesNotSlopeside = 1;
            Expect("P33 ski-day order", x, o,
                P(HubProblemCode.Efficiency, 2, pct: 50, ifFixed: 69, factor: elec),
                P(HubProblemCode.LiftNoPower, 2, liftIndex: 1),
                P(HubProblemCode.SkiDaysCannotReach, 2),
                P(HubProblemCode.RunsFlat, 1, count: 1),
                P(HubProblemCode.NoParkingNearLodge, 1, count: 1),
                P(HubProblemCode.LodgeNotSlopeside, 1, count: 1),
                P(HubProblemCode.Factor, 0, pct: -50, factor: elec));

            // P34 the lodge counts clamp to the area's lodges (3 → 1, 2 → 1).
            x = In(1, 1, 100, 1, 3);
            x.Lifts = Ls(L(0, true, true, true));
            x.LodgesNoParking = 3;
            x.LodgesNotSlopeside = 2;
            Expect("P34 counts clamp", x, o,
                P(HubProblemCode.NoParkingNearLodge, 1, count: 1),
                P(HubProblemCode.LodgeNotSlopeside, 1, count: 1));

            // P35 no lodge: min(n, 0) = 0, so only NoLodge.
            x = In(0, 0, 100, 1, 3);
            x.Lifts = Ls(L(0, true, true, true));
            x.IfOneLodge = 10;
            x.LodgesNoParking = 1;
            x.LodgesNotSlopeside = 1;
            Expect("P35 no lodge", x, o, P(HubProblemCode.NoLodge, 3, ifFixed: 10, build: "lodge"));

            // P36 Not linked ignores every ski-day input and the F1 state.
            x = new HubProblemInput
            {
                Unlinked = true, Runs = 1, LiftRows = 0, FreeLodges = 0, EfficiencyPct = -1, AttractionFull = -1, IfOneLodge = -1,
                IfLodgesOn = -1, SkiDaysCannotReach = true, LodgesNoParking = 1, LodgesNotSlopeside = 1, EfficiencyRowWasOn = true,
            };
            Expect("P36 Not linked", x, o, P(HubProblemCode.Unlinked, 3, build: "sign"));

            // P37-P41 F1 (decisions F1: show at <= 90 %, hide at >= 95 %). P5 (91, not held → no row) and P6 (90 → row)
            // above are the "not held" half. P37 held at 91.
            x = In(1, 1, 91, 1, 3);
            x.Factors = Fs(F(elec, -9));
            x.Lifts = Ls(L(0, true, true, true));
            x.AttractionFull = 60;
            x.EfficiencyRowWasOn = true;
            Expect("P37 F1 held at 91", x, o,
                P(HubProblemCode.Efficiency, 2, pct: 91, ifFixed: 60, factor: elec),
                P(HubProblemCode.Factor, 0, pct: -9, factor: elec));

            // P38 held at 94 (the last percent below EfficiencyClearPct).
            x = In(1, 1, 94, 1, 3);
            x.Factors = Fs(F(elec, -6));
            x.Lifts = Ls(L(0, true, true, true));
            x.AttractionFull = 60;
            x.EfficiencyRowWasOn = true;
            Expect("P38 F1 held at 94", x, o,
                P(HubProblemCode.Efficiency, 2, pct: 94, ifFixed: 60, factor: elec),
                P(HubProblemCode.Factor, 0, pct: -6, factor: elec));

            // P39 released at 95; the −5 factor still lists (F1b: a 5-point loss is listed).
            x = In(1, 1, 95, 1, 3);
            x.Factors = Fs(F(elec, -5));
            x.Lifts = Ls(L(0, true, true, true));
            x.AttractionFull = 60;
            x.EfficiencyRowWasOn = true;
            Expect("P39 F1 released at 95", x, o, P(HubProblemCode.Factor, 0, pct: -5, factor: elec));

            // P40 held at 0: the not-running severity 3 is unchanged.
            x = In(1, 1, 0, 1, 3);
            x.Factors = Fs(F(elec, -100));
            x.Lifts = Ls(L(0, true, true, true));
            x.AttractionFull = 49;
            x.EfficiencyRowWasOn = true;
            Expect("P40 F1 held at 0", x, o,
                P(HubProblemCode.Efficiency, 3, pct: 0, ifFixed: 49, factor: elec),
                P(HubProblemCode.Factor, 0, pct: -100, factor: elec));

            // P41 no result: nothing, held or not.
            x = In(1, 1, -1, 1, 1);
            x.Factors = Fs(F(elec, -50));
            x.Lifts = Ls(L(0, true, true, true));
            x.EfficiencyRowWasOn = true;
            Expect("P41 F1 no result", x, o);

            // Round S1 F1b (s1-spec §5.3 ruling R-Q2): a Factor row only when the factor costs 5 points or more
            // (FactorRowMinLossPct; EffectPct <= −5). P42-P44 add 2 + 1 + 2 = 5 checks.
            // P42 91 % = 0.95 × 0.96 (0.912): no Efficiency row (91, not held); the −5 factor listed, the −4 not.
            x = In(1, 1, 91, 1, 3);
            x.Factors = Fs(F("NotEnoughEmployees", -5), F(elec, -4));
            x.Lifts = Ls(L(0, true, true, true));
            x.AttractionFull = 60;
            Expect("P42 F1b -5 listed, -4 not", x, o, P(HubProblemCode.Factor, 0, pct: -5, factor: "NotEnoughEmployees"));

            // P43 a lone −4 factor at 96 %: no row at all.
            x = In(1, 1, 96, 1, 3);
            x.Factors = Fs(F(elec, -4));
            x.Lifts = Ls(L(0, true, true, true));
            x.AttractionFull = 60;
            Expect("P43 F1b lone -4", x, o);

            // P44 three −4 factors, 0.96³ = 0.884736 → 88 %: no Factor row is listed, so the band row still names the
            // worst factor and counts the other two (fixer, HU-1: with none listed, "more" counts every factor).
            x = In(1, 1, 88, 1, 3);
            x.Factors = Fs(F("WaterSupply", -4), F("SewageHandling", -4), F(elec, -4));
            x.Lifts = Ls(L(0, true, true, true));
            x.AttractionFull = 60;
            Expect("P44 F1b band row keeps small factors", x, o,
                P(HubProblemCode.Efficiency, 2, pct: 88, more: 2, ifFixed: 60, factor: "WaterSupply"));

            // P45 (fixer pass, review HU-1) mixed: −12, −3, −1 (0.88 × 0.97 × 0.99 = 0.845) → 85 %: one factor is
            // listed, so "and {N} more" counts only listed factors (the tooltip names them): more 0. 3 checks.
            x = In(1, 1, 85, 1, 3);
            x.Factors = Fs(F("WaterSupply", -12), F("SewageHandling", -3), F(elec, -1));
            x.Lifts = Ls(L(0, true, true, true));
            x.AttractionFull = 60;
            Expect("P45 HU-1 more counts listed factors", x, o,
                P(HubProblemCode.Efficiency, 2, pct: 85, more: 0, ifFixed: 60, factor: "WaterSupply"),
                P(HubProblemCode.Factor, 0, pct: -12, factor: "WaterSupply"));

            // P46 Round M1 D9 (docs/research/ski-days/measure/m1-spec.md §4 "P42"; renumbered: P42 is F1b's): an area
            // with no runs shows NoRuns and never "Lodge is far from the runs"; the same lodge with 3 runs shows it as
            // in P34. 2 + 2 checks.
            x = In(1, 1, 100, 1, 0);
            x.Lifts = Ls(L(0, true, true, true));
            x.LodgesNotSlopeside = 1;
            Expect("P46 D9 no runs: only NoRuns", x, o, P(HubProblemCode.NoRuns, 1));
            x = In(1, 1, 100, 1, 3);
            x.Lifts = Ls(L(0, true, true, true));
            x.LodgesNotSlopeside = 1;
            Expect("P46 D9 with runs: LodgeNotSlopeside", x, o, P(HubProblemCode.LodgeNotSlopeside, 1, count: 1));

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
                  HubRules.FollowMinIntervalS == 2f && HubRules.FollowQuietAfterUserS == 2f &&
                  HubRules.EfficiencyClearPct == 95 && HubRules.FactorRowMinLossPct == 5,   // Round S1 F1, F1b
                  w, "a HubRules constant moved: the oracles are literal for it");

            Eq((int)HubProblemCode.Unlinked, 0, w, "HubProblemCode.Unlinked");
            Eq((int)HubProblemCode.Efficiency, 4, w, "HubProblemCode.Efficiency");
            Eq((int)HubProblemCode.StationsOnly, 10, w, "HubProblemCode.StationsOnly");
            Eq((int)HubProblemCode.Factor, 16, w, "HubProblemCode.Factor");
            Eq((int)HubProblemCode.RunsFlat, 17, w, "HubProblemCode.RunsFlat");
            Eq((int)HubProblemCode.SignNoRoad, 18, w, "HubProblemCode.SignNoRoad");     // Round F7 D5
            Eq((int)HubProblemCode.LodgeNoRoad, 19, w, "HubProblemCode.LodgeNoRoad");   // Round F7 D5
            Eq((int)HubProblemCode.SkiDaysCannotReach, 20, w, "HubProblemCode.SkiDaysCannotReach");   // Round S1 D28
            Eq((int)HubProblemCode.NoParkingNearLodge, 21, w, "HubProblemCode.NoParkingNearLodge");   // Round S1 D28
            Eq((int)HubProblemCode.LodgeNotSlopeside, 22, w, "HubProblemCode.LodgeNotSlopeside");     // Round S1 D28
            Eq((int)HubProblemCode.MixedLiftTypes, 23, w, "HubProblemCode.MixedLiftTypes");                     // Catalog Phase B
            Eq((int)HubProblemCode.SummitLodgeNotLiftServed, 24, w, "HubProblemCode.SummitLodgeNotLiftServed"); // Catalog Phase B
            Eq((int)HubProblemCode.SchoolNoGreenRun, 25, w, "HubProblemCode.SchoolNoGreenRun");                 // Catalog Phase B
            Eq((int)HubProblemCode.SchoolsNotLinked, 26, w, "HubProblemCode.SchoolsNotLinked");                 // T100
            Eq(Enum.GetValues(typeof(HubProblemCode)).Length, 27, w, "HubProblemCode has 27 values (append only)");
            Eq((int)HubSkiDayState.Full, 7, w, "HubSkiDayState.Full");                                // Round S1 (contract 4)
            Eq(Enum.GetValues(typeof(HubSkiDayState)).Length, 8, w, "HubSkiDayState has 8 values");
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

        // ================================================================== H. ski-day state (Round S1)

        private static void SectionH()
        {
            const string w = "hub H";
            const HubSkiDayState Unknown = HubSkiDayState.Unknown, Off = HubSkiDayState.Off, SignOff = HubSkiDayState.SignOff,
                NoRun = HubSkiDayState.NoRun, NoLodge = HubSkiDayState.NoLodge, Night = HubSkiDayState.Night,
                Room = HubSkiDayState.Room, Full = HubSkiDayState.Full;
            // ch4 §G.3: SkiDayStateOf(off, hasView, scoreKnown, signRunning, runsCounted, targetLodges, claimHours, now, cap).
            State("H1", true, true, true, true, 5, 1, true, 0, 154, Off);
            State("H2", true, false, false, false, 0, 0, false, -1, -1, Off);       // off wins over everything
            State("H3", false, false, true, true, 5, 1, true, 0, 154, Unknown);     // no view
            State("H4", false, true, false, true, 5, 1, true, 0, 154, Unknown);     // no score / first-write hold
            State("H5", false, true, true, false, 0, 0, false, 0, 154, SignOff);
            State("H6", false, true, true, true, 0, 0, false, 0, 154, NoRun);
            State("H7", false, true, true, true, 5, 0, false, 0, 154, NoLodge);
            State("H8", false, true, true, true, 5, 1, false, 30, 154, Night);
            State("H9", false, true, true, true, 5, 1, true, 0, 154, Room);
            State("H10", false, true, true, true, 5, 1, true, 153, 154, Room);
            State("H11", false, true, true, true, 5, 1, true, 154, 154, Full);
            State("H12", false, true, true, true, 5, 1, true, 200, 154, Full);
            State("H13", false, true, true, true, 5, 1, true, 0, 0, Full);          // cap 0 is real (s1-spec N9)
            State("H14", false, true, true, true, 5, 1, true, 0, -1, Unknown);      // before the first pass
            State("H15", false, true, true, true, 5, 1, true, -1, 154, Unknown);    // now unknown

            // H16 (s1-spec N10, refutation RF-12): the C# powder mark and the TS threshold (POWDER_MARK_PCT = 50 on
            // skiDaysPowderPct = HubRules.Percent(powder)) agree at every reachable powder: snow-hours 0..24 (powder
            // takes sixths, 7 h = exactly 0.5). One check.
            bool parity = true;
            string first = "";
            for (int h = 0; h <= 48; h++)   // T134: fresh steps 0..48 (24 h at precipitation 1)
            {
                double powder = SkiDayRules.PowderOf(h);
                bool cs = SkiDayRules.PowderMark(powder), ts = HubRules.Percent(powder) >= 50;
                if (cs != ts && parity)
                {
                    parity = false;
                    first = $"h {h}: powder {powder}, PowderMark {cs}, Percent {HubRules.Percent(powder)} >= 50 {ts}";
                }
            }
            Check(parity, w, "H16 powder mark parity C# / TS: " + first);

            // H17 (Round M2 R7, hub-price.md §3; build-contract.md §7, Decided here 13-14): the hub's price line computes
            // D16's factor in TS (hubRules.priceFactor); this is the C# half of the shared literal table
            // (mod/SkiResortUI/tools/test-hub-rules.js T10.1 holds the same rows). Two checks: the table, then the edge.
            int[] priceIn = { 0, 5, 40, 45, 80, 100, 120, 150, 200, 260 };
            double[] priceOut = { 1, 1, 1, 0.975, 0.8, 0.7, 0.6, 0.45, 0.2, 0.2 };
            bool priceOk = true;
            string priceFirst = "";
            for (int i = 0; i < priceIn.Length; i++)
            {
                double got = SkiDayRules.PriceFactor(priceIn[i]);
                if (!Near(got, priceOut[i], 1e-12) && priceOk)
                {
                    priceOk = false;
                    priceFirst = $"price {priceIn[i]}: C# {got}, table {priceOut[i]}";
                }
            }
            Check(priceOk, w, "H17 price factor C# = the TS table: " + priceFirst);
            // The Options texts and the hub tooltip bake "40", "60" and "200" (Settings/LocaleEN.cs Locals who ski and Lift ticket
            // price; MONEY_PRICE_TIP[Curve] through PRICE_NO_EFFECT_MAX): the no-effect edge 240 - 200, the default (80 since T137, 60 since
            // econ-2 round 2; the factor at 80 is 0.8: "80 % at 80"), the cap.
            Check(SkiDayRules.PriceFullAt - SkiDayRules.PriceSpan == 40 && SkiMoneyRules.TicketPriceDefault == 80 &&
                  SkiDayRules.PriceMax == 200 && SkiMoneyRules.TicketPriceMax == 200 &&
                  SkiDayRules.PriceFactor(SkiMoneyRules.TicketPriceDefault, 0) == 0.8,
                  w, "H17 price edge 40, default 80 (factor 0.8; T137 I1, was 60), cap 200 (the Options texts say so)");

            // H18 (Round M2 R5, thaw-rules.md §3): Season reads the live gun gates when known. Arguments (gating, runs,
            // open, anyGunRun, gunsLiveKnown, anyGunOn, topKnown, T, freezing 0, margin 2). Eight checks.
            Eq(HubRules.Season(true, 7, 5, true, true, false, true, 1.5f, 0f, 2f), SeasonHint.Thawing, w,
               "H18 Season(true,7,5,true,true,false,true,1.5,0,2) (old rule SnowGunsOnly; run 2b Dump 3 shape)");
            Eq(HubRules.Season(true, 7, 5, true, true, true, true, 2.5f, 0f, 2f), SeasonHint.SnowGunsOnly, w,
               "H18 Season(true,7,5,true,true,true,true,2.5,0,2) (night guns on a warm-mean day; old rule Thawing)");
            Eq(HubRules.Season(true, 7, 5, true, true, false, true, -0.8f, 0f, 2f), SeasonHint.SnowSeason, w,
               "H18 Season(true,7,5,true,true,false,true,-0.8,0,2) (Dump 2: the mean decides natural snow)");
            Eq(HubRules.Season(true, 7, 0, true, true, true, true, 3.0f, 0f, 2f), SeasonHint.WaitingForSnow, w,
               "H18 Season(true,7,0,true,true,true,true,3.0,0,2)");
            Eq(HubRules.Season(true, 7, 0, true, true, false, true, 1.0f, 0f, 2f), SeasonHint.TooWarm, w,
               "H18 Season(true,7,0,true,true,false,true,1.0,0,2) (old rule WaitingForSnow)");
            Eq(HubRules.Season(true, 7, 5, false, true, true, true, 1.5f, 0f, 2f), SeasonHint.Thawing, w,
               "H18 Season(true,7,5,false,true,true,true,1.5,0,2) (no gun run: anyGunOn ignored)");
            Eq(HubRules.Season(false, 7, 5, true, true, true, true, 1.5f, 0f, 2f), SeasonHint.GatingOff, w,
               "H18 Season(false,7,5,true,true,true,true,1.5,0,2)");
            Eq(HubRules.Season(true, 7, 5, true, true, true, false, float.NaN, 0f, 2f), SeasonHint.None, w,
               "H18 Season(true,7,5,true,true,true,false,NaN,0,2)");
        }

        // ================================================================== I. Catalog Phase B (lift group)

        private static HubProblemLift LT(int state, bool powered, bool hasBase, bool hasTop, byte type, bool mixed = false,
                                         bool named = false) =>
            new HubProblemLift { State = state, Powered = powered, HasBase = hasBase, HasTop = hasTop, CustomName = named,
                                 Type = type, Mixed = mixed };

        /// <summary>Catalog Phase B (round-2-contract §11.2 "HubRulesTests.cs + MoneyBatch.cs (lift)"; skeptic MUST 1):
        ///   I1  RideTimeS(loop, speed) per lift type, and the one-argument CG-2 twin;
        ///   I2  MUST 1: a SUMMER area with no lodge keeps today's "if one lodge" number (15), whatever LodgeShare says;
        ///   I3  AttractionWithLodgeApplied (the IfLodgesOn ladder value) on literal results and on T2 from Compute;
        ///   I4  LodgePart = LodgeApplied (seam S4);
        ///   I5  problems: MixedLiftTypes 23 (build item by type, no LiftUnfinished row), 24 and 25 (counts, severities,
        ///       the 24 cap), StationsOnly / SectionMissing build items per type, and lone stations pairing per type only;
        ///   I6  H17 with tolerance: SkiDayRules.PriceFactor(price, tolerance) = the TS priceFactor table
        ///       (mod/SkiResortUI/tools/test-hub-rules.js, §8.7), and PriceFactor(p) ≡ PriceFactor(p, 0).
        /// Oracles are literal hand values (contract §11.1-§11.2 tables).</summary>
        private static void SectionI()
        {
            const string w = "hub I";

            // I1 ride time per speed (contract §11.2)
            Eq(HubRules.RideTimeS(2045f, 5.0f), 204.5f, w, "RideTimeS(2045, 5.0) (detachable)");
            Eq(HubRules.RideTimeS(2045f, 5.5f), 2045f / 2f / 5.5f, w, "RideTimeS(2045, 5.5) (gondola)");
            Eq(HubRules.RideTimeS(2045f, 0f), -1f, w, "RideTimeS(2045, 0)");
            Eq(HubRules.RideTimeS(2045f, -2.5f), -1f, w, "RideTimeS(2045, -2.5)");
            Eq(HubRules.RideTimeS(2045f, float.NaN), -1f, w, "RideTimeS(2045, NaN)");
            Eq(HubRules.RideTimeS(0f, 5.0f), -1f, w, "RideTimeS(0, 5.0)");
            Eq(HubRules.RideTimeS(2045f), 409f, w, "RideTimeS(2045) (CG-2 twin: the fixed-grip 2.5)");
            Eq(HubRules.RideTimeS(2045f, SkiCatalog.FixedGrip.SpeedMps), HubRules.RideTimeS(2045f), w,
               "RideTimeS(2045, FixedGrip.SpeedMps) == RideTimeS(2045)");

            // I2 MUST 1: summer, 0 lodges, from Compute (W1's runs with no lodge): today's 4.898979486 + 20 × 0.5 → 15.
            var p = new List<SkiResortScore.RunPart>();
            SkiResortScore.Result nlw = Score(0, 1.0, Small(0.0), 1, p);
            Check((nlw.Flags & SkiResortScore.ScoreFlags.SummerMode) != 0, w, "I2: Small(0) with no lodge is summer mode");
            Check(nlw.LodgeApplied == 0.0, w, $"I2: no lodge → LodgeApplied 0, got {nlw.LodgeApplied}");
            Eq(HubRules.AttractionWithLodges(in nlw, 1), 15, w, "I2 MUST 1: AttractionWithLodges(summer, 0 lodges, 1) = today's 15");
            Eq(HubRules.AttractionWithLodges(in nlw, 2), 17, w, "I2: AttractionWithLodges(summer, 0 lodges, 2) = 4.899 + 12 → 17");

            // I3 AttractionWithLodgeApplied (seam S4; the stats system's IfLodgesOn with the ladder's applied value).
            var s = new SkiResortScore.Result { AOpen = 71.585929905, Efficiency = 1 };   // T2 terrain, no lodge applied
            Eq(HubRules.AttractionWithLodgeApplied(in s, 40.0), 112, w, "I3: T2 terrain + 40 (a Grand alone) → 112");
            Eq(HubRules.AttractionWithLodgeApplied(in s, 52.0), 124, w, "I3: T2 terrain + 52 (a Grand and a Summit, no luxury cap: LD5) → 124");
            Eq(HubRules.AttractionWithLodgeApplied(in s, 64.0), 136, w, "I3: T2 terrain + 64 (three luxury lodges, the extras cap reached: LX1) → 136");
            Eq(HubRules.AttractionWithLodgeApplied(in s, 24.0), 96, w, "I3: T2 terrain + 24 (two A-frames) → 96");
            Eq(HubRules.AttractionWithLodgeApplied(in s, 24.0), HubRules.AttractionWithLodges(in s, 2), w,
               "I3: applied 24 == AttractionWithLodges(r, 2) in winter");
            Eq(HubRules.AttractionWithLodgeApplied(in s, double.NaN), 72, w, "I3: a NaN applied value reads 0 → 72");
            Eq(HubRules.AttractionWithLodgeApplied(in s, -5.0), 72, w, "I3: a negative applied value reads 0 → 72");
            s.Efficiency = 0.5;
            Eq(HubRules.AttractionWithLodgeApplied(in s, 40.0), 56, w, "I3: 111.586 × 0.5 → 56");
            var big = new SkiResortScore.Result { AOpen = 190.0, Efficiency = 1 };
            Eq(HubRules.AttractionWithLodgeApplied(in big, 40.0), 200, w, "I3: clamped to MaxAttraction before rounding");
            SkiResortScore.Result t2 = Score(2, 1.0, Medium(), 3, p);
            NearF(t2.LodgeApplied, 24.0, 1e-9, w, "I3: T2 (two A-frames, winter) LodgeApplied");
            Eq(HubRules.AttractionWithLodgeApplied(in t2, 44.0), 116, w, "I3: T2 with a Grand + an A-frame (44) → 116 (LD4)");
            Eq(HubRules.AttractionWithLodgeApplied(in t2, 24.0), 96, w, "I3: T2 with its own lodges → its A 96");

            // I4 LodgePart = LodgeApplied (LS3: Grand + A-frame in summer, applied 26 of base 44).
            var ls3 = new SkiResortScore.Result { LodgeBase = 44, LodgeShare = 26.0 / 44.0, LodgeApplied = 26 };
            NearF(HubRules.LodgePart(in ls3), 26.0, 1e-12, w, "I4: LodgePart = LodgeApplied");
            var nan = new SkiResortScore.Result { LodgeApplied = double.NaN };
            NearF(HubRules.LodgePart(in nan), 0.0, 0.0, w, "I4: a NaN LodgeApplied reads 0");

            // I5 problems (§6.1).
            var o = new List<HubProblem>();
            HubProblemInput x = In(1, 1, 100, 1, 3);
            x.Lifts = Ls(LT(3, true, true, true, 1, mixed: true));
            Expect("I5a mixed detachable line on fixed-grip stations", x, o,
                P(HubProblemCode.MixedLiftTypes, 2, liftIndex: 0, build: "liftDetachable"));
            x = In(1, 1, 100, 2, 3);
            x.Lifts = Ls(LT(0, true, true, true, 0), LT(3, true, true, false, 0, mixed: true));
            Expect("I5b a mixed row typed fixed-grip (its base) builds the fixed-grip line", x, o,
                P(HubProblemCode.MixedLiftTypes, 2, liftIndex: 1, build: "lift"));
            x = In(3, 3, 100, 1, 3);
            x.Lifts = Ls(LT(0, true, true, true, 0));
            x.SummitLodgesNotServed = 2;
            x.SchoolNoGreenRun = true;
            Expect("I5c rows 24 and 25", x, o,
                P(HubProblemCode.SummitLodgeNotLiftServed, 1, count: 2),
                P(HubProblemCode.SchoolNoGreenRun, 1));
            x = In(1, 1, 100, 1, 3);
            x.Lifts = Ls(LT(0, true, true, true, 0));
            x.SummitLodgesNotServed = 3;
            Expect("I5d row 24 counts at most the area's lodges", x, o, P(HubProblemCode.SummitLodgeNotLiftServed, 1, count: 1));
            x = In(1, 1, 100, 1, 3);
            x.Lifts = Ls(LT(0, true, true, true, 0));
            Expect("I5e no row 24 / 25 without their inputs", x, o);
            x = In(1, 1, 100, 2, 3);
            x.Lifts = Ls(LT(4, false, true, false, 1), LT(4, false, false, true, 2));
            Expect("I5f lone detachable base and lone gondola top: different types never pair", x, o,
                P(HubProblemCode.StationsOnly, 1, liftIndex: 0, detail: 1, build: "topStationDetachable"),
                P(HubProblemCode.StationsOnly, 1, liftIndex: 1, detail: 2, build: "baseStationGondola"));
            x = In(1, 1, 100, 2, 3);
            x.Lifts = Ls(LT(4, false, true, false, 1), LT(4, false, false, true, 1));
            Expect("I5g lone detachable base and top pair: one 'draw the detachable line' row at the top's row", x, o,
                P(HubProblemCode.StationsOnly, 1, liftIndex: 1, detail: 0, build: "liftDetachable"));
            x = In(1, 1, 100, 2, 3);
            x.Lifts = Ls(LT(4, false, true, false, 1, named: true), LT(4, false, false, true, 1));
            Expect("I5h the pair row sits at a named base's row", x, o,
                P(HubProblemCode.StationsOnly, 1, liftIndex: 0, detail: 0, build: "liftDetachable"));
            x = In(1, 1, 100, 2, 3);
            x.Lifts = Ls(LT(4, false, true, false, 1), LT(3, true, false, true, 0));
            Expect("I5i a fixed-grip line from a top does not complete a detachable base", x, o,
                P(HubProblemCode.LiftUnfinished, 2, liftIndex: 1, build: "lift"),
                P(HubProblemCode.StationsOnly, 1, liftIndex: 0, detail: 1, build: "topStationDetachable"));
            x = In(1, 1, 100, 2, 3);
            x.Lifts = Ls(LT(4, false, true, false, 2), LT(3, true, false, true, 2));
            Expect("I5j a gondola line from a top waits for the lone gondola base", x, o,
                P(HubProblemCode.LiftUnfinished, 2, liftIndex: 1, build: "liftGondola"),
                P(HubProblemCode.StationsOnly, 1, liftIndex: 0, detail: 0, build: "liftGondola"));
            x = In(1, 1, 100, 1, 3);
            x.Lifts = Ls(LT(2, true, true, true, 3));
            Expect("I5k a tram section missing builds the tram line", x, o,
                P(HubProblemCode.LiftSectionMissing, 2, liftIndex: 0, build: "liftTram"));
            x = In(1, 1, 100, 1, 3);
            x.Lifts = Ls(LT(4, false, true, false, 255));
            Expect("I5l an untyped lone base falls back to the fixed-grip items", x, o,
                P(HubProblemCode.StationsOnly, 1, liftIndex: 0, detail: 1, build: "topStation"));

            // I6 H17 with tolerance (C# half; test-hub-rules.js holds the TS priceFactor rows of contract §8.7).
            int[] tolPrice = { 80, 80, 80, 200, 250, 200, 0 };
            int[] tolTol = { 0, 20, 40, 40, 40, 0, 40 };
            double[] tolOut = { 0.8, 0.9, 1.0, 0.4, 0.4, 0.2, 1.0 };
            bool tolOk = true;
            string tolFirst = "";
            for (int i = 0; i < tolPrice.Length; i++)
            {
                double got = SkiDayRules.PriceFactor(tolPrice[i], tolTol[i]);
                if (!Near(got, tolOut[i], 1e-12) && tolOk)
                {
                    tolOk = false;
                    tolFirst = $"PriceFactor({tolPrice[i]}, {tolTol[i]}) = {got}, table {tolOut[i]}";
                }
            }
            Check(tolOk, w, "I6 H17 price factor with tolerance C# = the TS table: " + tolFirst);
            bool sameOk = true;
            string sameFirst = "";
            for (int price = -10; price <= 260; price++)
            {
                double a = SkiDayRules.PriceFactor(price), b = SkiDayRules.PriceFactor(price, 0);
                if (!(a == b) && sameOk)
                {
                    sameOk = false;
                    sameFirst = $"price {price}: PriceFactor(p) {a}, PriceFactor(p, 0) {b}";
                }
            }
            Check(sameOk, w, "I6 PriceFactor(p) ≡ PriceFactor(p, 0) for p in −10..260: " + sameFirst);
        }

        private static void State(string name, bool off, bool hasView, bool scoreKnown, bool signRunning, int runsCounted,
                                  int targetLodges, bool claimHours, int now, int cap, HubSkiDayState want)
        {
            HubSkiDayState got = HubRules.SkiDayStateOf(off, hasView, scoreKnown, signRunning, runsCounted, targetLodges, claimHours, now, cap);
            Check(got == want, "hub H", $"{name} SkiDayStateOf({off},{hasView},{scoreKnown},{signRunning},{runsCounted},{targetLodges}," +
                                        $"{claimHours},{now},{cap}) = {got}, want {want}");
        }

        // ================================================================== J. Catalog Phase C (rules group): the tram

        /// <summary>Catalog Phase C (round-3-contract §8.3, C5, C14; skeptic MUST 4). v 10 m/s, a 0.8 m/s² (0.8f, as the
        /// catalog row), dwell 60 s. Literal oracles (contract §3.3's worked rows; the skeptic's Math.fround recomputation):
        ///   HJ-1..HJ-3  ShuttleRideTimeS: 1 km 112.5 s, 3 km 312.5 s, 100 m 22.360679774997898 (triangle) — bit-exact floats;
        ///               2 km 212.5, 125 m 25 (the degenerate trapezoid with 0.8f);
        ///   HJ-4        invalid → −1 (path 0 / NaN / −5 / ∞, speed 0, accel 0, dwell −1 / NaN);
        ///   HJ-4b       C5: the dwell does not enter the ride time while ShuttleRideIncludesDwell is false (pinned false:
        ///               flipping it is Richard's open decision and must revisit these oracles and LIFT_RIDE_TIP);
        ///   HJ-5        ShuttleCapacityPerHour(2, 100, 1 km) == (float)(720000 / 345) bit-exact;
        ///   HJ-6        a → ∞ (1e9): 1 km 2,250 and 3 km 1,000 pph ± 0.01 (the brief's "pax × 3600 / (L/v + dwell)");
        ///   HJ-7        one cabin = half (exact in float); cabins 0, seats 0, dwell −1, cabins −1, NaN path → −1;
        ///   HJ-8        the Loop rule untouched: RideTimeS(2045, 2.5) == 409 (CG-2 twin);
        ///   HJ-9        the catalog's tram row (SkiCatalog index 3) through both: 112.5 s / 2,086.96 pph at 1 km; the worked
        ///               capacities 100 m 4,371.0, 125 m 4,235.3, 2 km 1,321.10, 3 km 966.44;
        ///   HJ-10       C14's mapping through LiftStatusOf: Running / Held (NoPower) / no path (ChairsStopped, NoPower).</summary>
        private static void SectionJ()
        {
            const string w = "hub J";
            const float v = 10f, a = 0.8f, d = 60f;

            // HJ-1..HJ-3 ride time (trip, dock to dock)
            Eq(HubRules.ShuttleRideTimeS(1000f, v, a, d), 112.5f, w, "HJ-1 ShuttleRideTimeS(1000, 10, 0.8, 60)");
            Eq(HubRules.ShuttleRideTimeS(3000f, v, a, d), 312.5f, w, "HJ-2 ShuttleRideTimeS(3000, 10, 0.8, 60)");
            Eq(HubRules.ShuttleRideTimeS(100f, v, a, d), (float)22.360679774997898, w, "HJ-3 ShuttleRideTimeS(100, …) (triangle)");
            Eq(HubRules.ShuttleRideTimeS(2000f, v, a, d), 212.5f, w, "HJ-2b ShuttleRideTimeS(2000, …)");
            Eq(HubRules.ShuttleRideTimeS(125f, v, a, d), 25f, w, "HJ-2c ShuttleRideTimeS(125, …) (ramps meet)");

            // HJ-4 invalid inputs
            Eq(HubRules.ShuttleRideTimeS(0f, v, a, d), -1f, w, "HJ-4 ShuttleRideTimeS(0, …)");
            Eq(HubRules.ShuttleRideTimeS(float.NaN, v, a, d), -1f, w, "HJ-4 ShuttleRideTimeS(NaN, …)");
            Eq(HubRules.ShuttleRideTimeS(-5f, v, a, d), -1f, w, "HJ-4 ShuttleRideTimeS(-5, …)");
            Eq(HubRules.ShuttleRideTimeS(float.PositiveInfinity, v, a, d), -1f, w, "HJ-4 ShuttleRideTimeS(+inf, …)");
            Eq(HubRules.ShuttleRideTimeS(1000f, 0f, a, d), -1f, w, "HJ-4 ShuttleRideTimeS(1000, 0, 0.8, 60)");
            Eq(HubRules.ShuttleRideTimeS(1000f, v, 0f, d), -1f, w, "HJ-4 ShuttleRideTimeS(1000, 10, 0, 60)");
            Eq(HubRules.ShuttleRideTimeS(1000f, v, a, -1f), -1f, w, "HJ-4 ShuttleRideTimeS(1000, 10, 0.8, -1)");
            Eq(HubRules.ShuttleRideTimeS(1000f, v, a, float.NaN), -1f, w, "HJ-4 ShuttleRideTimeS(1000, 10, 0.8, NaN)");

            // HJ-4b C5 / MUST 4: the trip only, whatever the dwell, while the switch is off.
            Check(!HubRules.ShuttleRideIncludesDwell, w, "HJ-4b ShuttleRideIncludesDwell is false (C5 as built; open decision)");
            Eq(HubRules.ShuttleRideTimeS(1000f, v, a, 0f), 112.5f, w, "HJ-4b ShuttleRideTimeS(1000, 10, 0.8, 0)");
            Eq(HubRules.ShuttleRideTimeS(1000f, v, a, 600f), 112.5f, w, "HJ-4b ShuttleRideTimeS(1000, 10, 0.8, 600)");

            // HJ-5 capacity, 2 cabins × 100 seats, 1 km: 720000 / 345
            float cap2 = HubRules.ShuttleCapacityPerHour(2, 100, 1000f, v, a, d);
            Eq(cap2, (float)(720000.0 / 345.0), w, "HJ-5 ShuttleCapacityPerHour(2, 100, 1000, 10, 0.8, 60)");

            // HJ-6 the brief's limit (a → ∞): 2 × 100 × 3600 / (2 (L/v + D)) = 100 × 3600 / (L/v + D)
            NearF(HubRules.ShuttleCapacityPerHour(2, 100, 1000f, v, 1e9f, d), 2250.0, 0.01, w, "HJ-6 a 1e9, 1 km");
            NearF(HubRules.ShuttleCapacityPerHour(2, 100, 3000f, v, 1e9f, d), 1000.0, 0.01, w, "HJ-6 a 1e9, 3 km");

            // HJ-7 one cabin = half; refusals
            Eq(HubRules.ShuttleCapacityPerHour(1, 100, 1000f, v, a, d), cap2 / 2f, w, "HJ-7 one cabin = half of two");
            Eq(HubRules.ShuttleCapacityPerHour(0, 100, 1000f, v, a, d), -1f, w, "HJ-7 cabins 0");
            Eq(HubRules.ShuttleCapacityPerHour(-1, 100, 1000f, v, a, d), -1f, w, "HJ-7 cabins -1");
            Eq(HubRules.ShuttleCapacityPerHour(2, 0, 1000f, v, a, d), -1f, w, "HJ-7 seats 0");
            Eq(HubRules.ShuttleCapacityPerHour(2, 100, 1000f, v, a, -1f), -1f, w, "HJ-7 dwell -1");
            Eq(HubRules.ShuttleCapacityPerHour(2, 100, float.NaN, v, a, d), -1f, w, "HJ-7 path NaN");
            Eq(HubRules.ShuttleCapacityPerHour(2, 100, 1000f, v, 0f, d), -1f, w, "HJ-7 accel 0");

            // HJ-8 the Loop rule is untouched
            Eq(HubRules.RideTimeS(2045f, 2.5f), 409f, w, "HJ-8 RideTimeS(2045, 2.5)");

            // HJ-9 the catalog's tram row through both functions
            LiftTypeDef tram = SkiCatalog.LiftByIndex(3);
            if (Check(tram != null && tram.Id == "tram" && tram.Mover == MoverKind.Shuttle, w, "HJ-9 SkiCatalog row 3 is the tram (Shuttle)"))
            {
                Check(tram.SeatsPerCarrier == 100 && tram.SpeedMps == 10f && tram.AccelMps2 == 0.8f && tram.DwellS == 60f, w,
                      "HJ-9 tram row 100 seats, 10 m/s, 0.8 m/s², 60 s (got " + tram.SeatsPerCarrier + ", " + tram.SpeedMps + ", " +
                      tram.AccelMps2 + ", " + tram.DwellS + ")");
                Eq(HubRules.ShuttleRideTimeS(1000f, tram.SpeedMps, tram.AccelMps2, tram.DwellS), 112.5f, w, "HJ-9 ride 1 km");
                Eq(HubRules.ShuttleCapacityPerHour(2, tram.SeatsPerCarrier, 1000f, tram.SpeedMps, tram.AccelMps2, tram.DwellS), cap2,
                   w, "HJ-9 capacity 1 km");
                NearF(cap2, 2086.96, 0.005, w, "HJ-9 capacity 1 km ≈ 2,086.96");
                NearF(HubRules.ShuttleCapacityPerHour(2, tram.SeatsPerCarrier, 100f, tram.SpeedMps, tram.AccelMps2, tram.DwellS),
                      4371.0, 0.05, w, "HJ-9 capacity 100 m");
                NearF(HubRules.ShuttleCapacityPerHour(2, tram.SeatsPerCarrier, 125f, tram.SpeedMps, tram.AccelMps2, tram.DwellS),
                      4235.29, 0.01, w, "HJ-9 capacity 125 m");
                NearF(HubRules.ShuttleCapacityPerHour(2, tram.SeatsPerCarrier, 2000f, tram.SpeedMps, tram.AccelMps2, tram.DwellS),
                      1321.10, 0.01, w, "HJ-9 capacity 2 km");
                NearF(HubRules.ShuttleCapacityPerHour(2, tram.SeatsPerCarrier, 3000f, tram.SpeedMps, tram.AccelMps2, tram.DwellS),
                      966.44, 0.01, w, "HJ-9 capacity 3 km");
            }

            // HJ-10 C14: the stats state of a tram row → the hub status (no new HubLiftStatus value, contract 6)
            Eq(HubRules.LiftStatusOf(0, true), HubLiftStatus.Running, w, "HJ-10 path + cabins, powered → Running");
            Eq(HubRules.LiftStatusOf(0, false), HubLiftStatus.NoPower, w, "HJ-10 path + cabins, unpowered (Held) → NoPower");
            Eq(HubRules.LiftStatusOf(1, true), HubLiftStatus.ChairsStopped, w, "HJ-10 no path / no cabin, powered → ChairsStopped");
            Eq(HubRules.LiftStatusOf(1, false), HubLiftStatus.NoPower, w, "HJ-10 no path / no cabin, unpowered → NoPower");
        }

        // ================================================================== K. Phase D (rules group): unpowered lifts

        /// <summary>Phase D (round-4-contract §7.2, D9; Richard's re-ruling DECISIONS 171: an unpowered lift of every type
        /// carries no guests). Literal oracles:
        ///   HK-1  LiftCapacityShown: the Loop capacities riding × seats × speed × 3600 / L (the Stats expression, riding 40,
        ///         L 2,000 m: fixed-grip 3 / 2.5 → 540, detachable 4 / 5.0 → 1,440, gondola 8 / 5.5 → 3,168) and the tram's
        ///         ShuttleCapacityPerHour(2, 100, 1000, 10, 0.8, 60): (c, true) bit-equal to c, (c, false) == −1, (−1, true)
        ///         == −1; through Carries for a powered and an unpowered complete row (the hub's composition, §2.6);
        ///   HK-2  LiftStatusOf(0, false) NoPower, (0, true) Running, (1, false) NoPower, (1, true) ChairsStopped (unchanged
        ///         oracles, restated next to the capacity rows: the status of a held lift stays "No power");
        ///   HK-3  Carries(complete, shownPowered, closed), 8 rows; equal to Carries(complete, p, closed) with shownPowered =
        ///         p != NoPower, and to SkiMoneyRules.LiftOperating(complete, IsPowered(p)) &amp;&amp; !closed for all 12 inputs
        ///         (D1's equivalence with the billing system's operating predicate, SkiMoneyRules.cs:73).</summary>
        private static void SectionK()
        {
            const string w = "hub K";
            Check(LiftServedRule.PowerGatesService, w, "PowerGatesService is true (the oracles assume power gates service)");

            // HK-1 the four types' capacities, as SkiResortStatsSystem computes them (Loop) and HubRules (tram)
            const int riding = 40;
            const double loopM = 2000.0;
            float[] caps = new float[4];
            int[] seats = { 3, 4, 8 };
            float[] speeds = { 2.5f, 5.0f, 5.5f };
            float[] want = { 540f, 1440f, 3168f };
            for (int t = 0; t < 3; t++)
            {
                LiftTypeDef def = SkiCatalog.LiftByIndex(t);
                Check(def != null && def.Mover == MoverKind.Loop && def.SeatsPerCarrier == seats[t] && def.SpeedMps == speeds[t], w,
                      "HK-1 catalog row " + t + " is a Loop with " + seats[t] + " seats at " + speeds[t] + " m/s");
                caps[t] = (float)(riding * seats[t] * (double)speeds[t] * 3600.0 / loopM);
                Eq(caps[t], want[t], w, "HK-1 Loop capacity row " + t);
            }
            caps[3] = HubRules.ShuttleCapacityPerHour(2, 100, 1000f, 10f, 0.8f, 60f);
            Eq(caps[3], (float)(720000.0 / 345.0), w, "HK-1 tram capacity (HJ-5's)");
            for (int t = 0; t < 4; t++)
            {
                float c = caps[t];
                Eq(BitConverter.SingleToInt32Bits(HubRules.LiftCapacityShown(c, true)), BitConverter.SingleToInt32Bits(c), w,
                   "HK-1 LiftCapacityShown(c, true) bit-equal c, row " + t);
                Eq(HubRules.LiftCapacityShown(c, false), -1f, w, "HK-1 LiftCapacityShown(c, false) row " + t);
                Eq(HubRules.LiftCapacityShown(c, LiftServedRule.Carries(true, true, false)), c, w, "HK-1 a powered complete row keeps c, row " + t);
                Eq(HubRules.LiftCapacityShown(c, LiftServedRule.Carries(true, false, false)), -1f, w, "HK-1 an unpowered complete row shows −1, row " + t);
            }
            Eq(HubRules.LiftCapacityShown(-1f, true), -1f, w, "HK-1 LiftCapacityShown(−1, true)");
            Eq(HubRules.LiftCapacityShown(-1f, false), -1f, w, "HK-1 LiftCapacityShown(−1, false)");
            Eq(BitConverter.SingleToInt32Bits(HubRules.LiftCapacityShown(float.NaN, true)), BitConverter.SingleToInt32Bits(float.NaN), w,
               "HK-1 LiftCapacityShown passes any float through untouched when it carries (NaN bits)");

            // HK-2 the status of a held lift (unchanged)
            Eq(HubRules.LiftStatusOf(0, false), HubLiftStatus.NoPower, w, "HK-2 LiftStatusOf(0, false)");
            Eq(HubRules.LiftStatusOf(0, true), HubLiftStatus.Running, w, "HK-2 LiftStatusOf(0, true)");
            Eq(HubRules.LiftStatusOf(1, false), HubLiftStatus.NoPower, w, "HK-2 LiftStatusOf(1, false)");
            Eq(HubRules.LiftStatusOf(1, true), HubLiftStatus.ChairsStopped, w, "HK-2 LiftStatusOf(1, true)");

            // HK-3 the bool overload, and D1's equivalences
            for (int c = 0; c < 2; c++)
                for (int s = 0; s < 2; s++)
                    for (int z = 0; z < 2; z++)
                    {
                        bool complete = c == 1, shown = s == 1, closed = z == 1;
                        Eq(LiftServedRule.Carries(complete, shown, closed), complete && shown && !closed, w,
                           "HK-3 Carries(" + complete + ", shownPowered " + shown + ", closed " + closed + ")");
                    }
            LiftPower[] powers = { LiftPower.Unknown, LiftPower.Powered, LiftPower.NoPower };
            for (int c = 0; c < 2; c++)
                foreach (LiftPower p in powers)
                    for (int z = 0; z < 2; z++)
                    {
                        bool complete = c == 1, closed = z == 1;
                        bool byEnum = LiftServedRule.Carries(complete, p, closed);
                        string tag = complete + ", " + p + ", closed " + closed;
                        Eq(LiftServedRule.Carries(complete, p != LiftPower.NoPower, closed), byEnum, w, "HK-3 bool overload == enum overload (" + tag + ")");
                        Eq(SkiMoneyRules.LiftOperating(complete, LiftServedRule.IsPowered(p)) && !closed, byEnum, w,
                           "HK-3 Carries == LiftOperating(complete, IsPowered(p)) && !closed (" + tag + ")");
                    }
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
