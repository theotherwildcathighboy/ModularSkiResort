// Tests for the Ski Bus Depot's pure rules: SkiBusRules (the coach bill, the nearest pick, what a verdict means), SkiDayRules.CheckDepot (the
// lodge order with the road always required, no slope-side rule, a passing hill lodge required), the claim flag, the ledger's bus charge.
// `public static class SkiBusTests` with `Run(check)`; Program.Main calls it. Oracles are literal hand values, floats +-0.01.

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using Unity.Mathematics;
using Rules = SkiResortMod.Systems.SkiDayRules;
using LodgeReject = SkiResortMod.Systems.SkiDayRules.LodgeReject;

namespace ResortBatch
{
    public static class SkiBusTests
    {
        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Bill();
            Nearest();
            Verdicts();
            Depot();
            Ledger();
        }

        private static bool Near(float a, float b) => Math.Abs(a - b) < 0.01f;

        private static void Bill()
        {
            const string w = "bus A";
            s_Check(SkiBusRules.BillFixedPerMonth == 1500f && SkiBusRules.BillPerKmPerMonth == 300f, w, "the constants: 1,500 + 300 per km (hubText repeats them, T22.1c)");
            s_Check(Near(SkiBusRules.MonthlyBill(7000f), 3600f), w, "7 km = 1,500 + 2,100");
            s_Check(Near(SkiBusRules.MonthlyBill(500f), 1650f), w, "0.5 km = 1,650 (a km is not rounded)");
            s_Check(Near(SkiBusRules.MonthlyBill(50000f), 16500f), w, "50 km = 16,500 (no cap)");
            s_Check(SkiBusRules.MonthlyBill(0f) == 1500f, w, "0 m = the fixed part");
            s_Check(SkiBusRules.MonthlyBill(-5f) == 1500f, w, "a negative distance = the fixed part");
            s_Check(SkiBusRules.MonthlyBill(float.NaN) == 1500f, w, "NaN = the fixed part");
            s_Check(SkiBusRules.MonthlyBill(float.PositiveInfinity) == 1500f, w, "+inf = the fixed part");
        }

        private static void Nearest()
        {
            const string w = "bus B";
            var none = new List<float2>();
            s_Check(SkiBusRules.NearestIndex(new float2(0f, 0f), none, out float d0) == -1 && float.IsPositiveInfinity(d0), w, "empty = -1, +inf");
            var two = new List<float2> { new float2(300f, 400f), new float2(0f, 100f) };
            s_Check(SkiBusRules.NearestIndex(new float2(0f, 0f), two, out float d1) == 1 && Near(d1, 100f), w, "the nearer one, 100 m");
            var tie = new List<float2> { new float2(100f, 0f), new float2(-100f, 0f) };
            s_Check(SkiBusRules.NearestIndex(new float2(0f, 0f), tie, out float d2) == 0 && Near(d2, 100f), w, "a tie: the first wins");
            var nan = new List<float2> { new float2(float.NaN, 0f), new float2(900f, 0f) };
            s_Check(SkiBusRules.NearestIndex(new float2(0f, 0f), nan, out float d3) == 1 && Near(d3, 900f), w, "a NaN position never wins");
            var inf = new List<float2> { new float2(float.PositiveInfinity, 0f) };
            s_Check(SkiBusRules.NearestIndex(new float2(0f, 0f), inf, out float d4) == -1 && float.IsPositiveInfinity(d4), w, "only an infinite position: none");
        }

        private static void Verdicts()
        {
            const string w = "bus C";
            s_Check(SkiBusRules.IsBilled(LodgeReject.None) && SkiBusRules.IsBilled(LodgeReject.NoLeisureTag), w, "None and the temporary leisure gate bill");
            foreach (LodgeReject v in Enum.GetValues(typeof(LodgeReject)))
            {
                if (v != LodgeReject.None && v != LodgeReject.NoLeisureTag)
                    s_Check(!SkiBusRules.IsBilled(v), w, v + " bills nothing");
                s_Check(SkiBusRules.CannotWork(v) == (v == LodgeReject.NoHillLodge || v == LodgeReject.NoRoad), w, v + ": the hub's depot row only for no hill lodge or no road");
            }
            s_Check((int)LodgeReject.NoHillLodge == 8, w, "LodgeReject.NoHillLodge = 8 (append only)");
            s_Check(SkiDayTiming.FlagToDepot == 128, w, "the claim's depot flag = 128");
        }

        private static Rules.LodgeFacts GoodDepot()
        {
            // IsLodgePrefab = the job's "this row is a claim target"; the depot's slope-side distance is never measured.
            return new Rules.LodgeFacts
            {
                IsLodgePrefab = true, OnLiveSign = true, Active = true, Destroyed = false, LeisureTagged = true, HasRoad = true,
                NearestRunOrStationM = float.PositiveInfinity,
            };
        }

        private static void Depot()
        {
            const string w = "bus D";
            Rules.LodgeFacts l = GoodDepot();
            s_Check(Rules.CheckDepot(l, 1) == LodgeReject.None, w, "in town, far from every run, with a hill lodge: None (IsSlopeside skipped)");
            s_Check(Rules.CheckLodge(l) == LodgeReject.NotSlopeside, w, "the same facts as a lodge would be not slope-side: the difference is the rule");
            s_Check(Rules.CheckDepot(l, 0) == LodgeReject.NoHillLodge, w, "no passing hill lodge: NoHillLodge");
            s_Check(Rules.CheckDepot(l, -1) == LodgeReject.NoHillLodge, w, "a negative count: NoHillLodge");
            l = GoodDepot(); l.HasRoad = false;
            s_Check(Rules.CheckDepot(l, 1) == LodgeReject.NoRoad, w, "no road: NoRoad");
            s_Check(Rules.CheckDepot(l, 0) == LodgeReject.NoRoad, w, "no road outranks no hill lodge");
            l = GoodDepot(); l.NoRoadNeeded = true; l.HasRoad = false;
            s_Check(Rules.CheckDepot(l, 1) == LodgeReject.NoRoad, w, "NoRoadNeeded never excuses a depot: it needs its own road");
            l = GoodDepot(); l.LeisureTagged = false;
            s_Check(Rules.CheckDepot(l, 0) == LodgeReject.NoLeisureTag, w, "no leisure tag first, as for a lodge");
            l = GoodDepot(); l.IsLodgePrefab = false; l.Destroyed = true;
            s_Check(Rules.CheckDepot(l, 1) == LodgeReject.NotLodge, w, "not a target + destroyed: NotLodge (the lodge order)");
            l = GoodDepot(); l.OnLiveSign = false;
            s_Check(Rules.CheckDepot(l, 1) == LodgeReject.NotInstalled, w, "no live sign: NotInstalled");
            l = GoodDepot(); l.Active = false; l.Destroyed = true;
            s_Check(Rules.CheckDepot(l, 1) == LodgeReject.Inactive, w, "inactive + destroyed: Inactive (the lodge order)");
            l = GoodDepot(); l.Destroyed = true;
            s_Check(Rules.CheckDepot(l, 1) == LodgeReject.Destroyed, w, "destroyed: Destroyed");
        }

        private static void Ledger()
        {
            const string w = "bus E";
            var a = new EntityRef(10, 1);
            var l = new SkiResortLedger();
            s_Check(l.Advance(700) != SkiResortLedger.AdvanceResult.None, w, "setup: the month starts");
            int rev = l.Revision;
            l.AddBusCharge(a, 12.5);
            l.AddBusCharge(a, 0.5);
            s_Check(l.TryGet(a, out SkiResortLedger.Entry e) && e.This.BusCharged == 13.0 && e.This.LiftCharged == 0.0 && l.Revision != rev, w,
                    "the bill books under the area, apart from the lift charge");
            rev = l.Revision;
            l.AddBusCharge(a, 0.0);
            l.AddBusCharge(a, -3.0);
            l.AddBusCharge(a, double.NaN);
            l.AddBusCharge(a, double.PositiveInfinity);
            s_Check(l.TryGet(a, out e) && e.This.BusCharged == 13.0 && l.Revision == rev, w, "zero, negative, NaN, +inf book nothing");
            var saved = new SkiResortLedger.Month { BusCharged = 40.0 };
            l.SeedThis(a, saved, 2025, 0, 0, 0, 0);
            s_Check(l.TryGet(a, out e) && e.This.BusCharged == 53.0, w, "a saved month with only a bill is not empty: it seeds (" + e.This.BusCharged + ")");
        }
    }
}
