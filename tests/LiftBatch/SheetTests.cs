// The Resort Planner's plan sheet (ResortPlanSheet, task R1-c): the rows the build queue, the marks outside the planner and the hub read.
// Where-tags `sheet/...`, PS-1..PS-6.
// Pinned:
// - Load sorts the rows lowest bottom spot first (ties by key, a NaN height last) and numbers Order. A repeated key keeps its first row.
// - Every change bumps Revision. Load and Clear bump PlanSerial. A no-op write bumps nothing.
// - BuildAllKeys takes the rows switched on and not built, in build order. CostToBuild sums the same rows.
// - Stations and states are per row. An unknown key changes nothing.
// - Dump names every row.

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    internal static class SheetTests
    {
        private static Func<bool, string, string, bool> s_Check;

        private static bool Check(bool ok, string where, string msg) => s_Check(ok, where, msg);

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Ps1LoadOrder();
            Ps2Revision();
            Ps3BuildAll();
            Ps4StatesAndStations();
            Ps5Clear();
            Ps6Dump();
        }

        private static ResortPlanSheet.Input L(uint key, float bottomY, bool on = true, long cost = 100000) => new ResortPlanSheet.Input
        {
            Key = key, Name = "P" + key, TypeIndex = 0, Bottom = new float3(key * 10f, bottomY, 0f), Top = new float3(key * 10f, bottomY + 200f, 600f),
            Cost = cost, On = on,
        };

        private static ResortPlanSheet Make()
        {
            var s = new ResortPlanSheet();
            s.Load(new[] { L(5, 900f), L(2, 700f), L(9, 700f), L(3, float.NaN), L(2, 100f), L(7, 800f, on: false) }, "test");
            return s;
        }

        private static void Ps1LoadOrder()
        {
            const string W = "sheet/PS-1";
            ResortPlanSheet s = Make();
            var keys = new List<uint>();
            foreach (ResortPlanSheet.Row r in s.Rows)
                keys.Add(r.Key);
            Check(string.Join(",", keys) == "2,9,7,5,3", W, "order " + string.Join(",", keys) + " (want 2,9,7,5,3: 700 ties by key, NaN last, the repeated 2 dropped)");
            for (int i = 0; i < s.Rows.Count; i++)
                Check(s.Rows[i].Order == i && s.Rows[i].State == SheetLiftState.Proposed && s.Rows[i].ReasonKey == null, W, "row " + i + " order / state");
            Check(s.Find(2).Bottom.y == 700f, W, "the repeated key 2 kept its first row (700), got " + s.Find(2).Bottom.y);
            Check(s.Find(42) == null, W, "an unknown key was found");
        }

        private static void Ps2Revision()
        {
            const string W = "sheet/PS-2";
            ResortPlanSheet s = Make();
            int rev = s.Revision, plan = s.PlanSerial;
            Check(!s.SetOn(2, true) && s.Revision == rev, W, "a no-op toggle bumped the revision");
            Check(s.SetOn(2, false) && s.Revision == rev + 1, W, "a toggle did not bump the revision");
            Check(!s.SetState(42, SheetLiftState.Built, null) && s.Revision == rev + 1, W, "an unknown key bumped the revision");
            Check(s.SetState(2, SheetLiftState.Refused, "MONEY") && s.Revision == rev + 2, W, "a state did not bump the revision");
            Check(!s.SetState(2, SheetLiftState.Refused, "MONEY") && s.Revision == rev + 2, W, "a repeated state bumped the revision");
            Check(s.PlanSerial == plan, W, "a row change bumped the plan serial");
            s.Load(new[] { L(1, 1f) }, "again");
            Check(s.PlanSerial == plan + 1 && s.Revision > rev + 2 && s.Count == 1 && s.Source == "again", W, "a load did not replace the rows");
        }

        private static void Ps3BuildAll()
        {
            const string W = "sheet/PS-3";
            ResortPlanSheet s = Make();
            Check(string.Join(",", s.BuildAllKeys()) == "2,9,5,3", W, "build all " + string.Join(",", s.BuildAllKeys()) + " (want 2,9,5,3: 7 is off)");
            Check(s.CostToBuild() == 400000, W, "cost to build " + s.CostToBuild());
            s.SetState(9, SheetLiftState.Built, null);
            s.SetState(5, SheetLiftState.NeedsFitting, "PLAN_LOW");
            Check(string.Join(",", s.BuildAllKeys()) == "2,5,3", W, "after a build " + string.Join(",", s.BuildAllKeys()) + " (want 2,5,3)");
            Check(s.CostToBuild() == 300000, W, "cost to build after a build " + s.CostToBuild());
            s.SetOn(7, true);
            Check(string.Join(",", s.BuildAllKeys()) == "2,7,5,3", W, "7 switched on: " + string.Join(",", s.BuildAllKeys()));
        }

        private static void Ps4StatesAndStations()
        {
            const string W = "sheet/PS-4";
            ResortPlanSheet s = Make();
            var b = new EntityRef(101, 3);
            Check(s.SetStations(5, b, EntityRef.Null) && s.Find(5).BaseStation == b && s.Find(5).TopStation.IsNull, W, "the base station did not stick");
            Check(!s.SetStations(5, b, EntityRef.Null), W, "a repeated station write reported a change");
            s.SetState(5, SheetLiftState.Refused, "TOP[OverlapExisting]");
            Check(s.Find(5).State == SheetLiftState.Refused && s.Find(5).ReasonKey == "TOP[OverlapExisting]" && s.Find(5).BaseStation == b, W,
                  "a refused row lost its base station or reason");
            Check(s.Find(2).State == SheetLiftState.Proposed && s.Find(2).BaseStation.IsNull, W, "another row changed");
            Check(s.CountIn(SheetLiftState.Refused) == 1 && s.CountIn(SheetLiftState.Proposed) == 4, W, "counts");
        }

        private static void Ps5Clear()
        {
            const string W = "sheet/PS-5";
            ResortPlanSheet s = Make();
            int plan = s.PlanSerial, rev = s.Revision;
            s.Clear("load");
            Check(s.Count == 0 && s.PlanSerial == plan + 1 && s.Revision == rev + 1 && s.Source == "load" && s.BuildAllKeys().Count == 0 &&
                  s.CostToBuild() == 0, W, "clear left rows or did not bump");
            s.Load(null, "no plan");
            Check(s.Count == 0, W, "a null load left rows");
        }

        private static void Ps6Dump()
        {
            const string W = "sheet/PS-6";
            ResortPlanSheet s = Make();
            s.SetState(9, SheetLiftState.Built, null);
            string d = s.Dump();
            foreach (ResortPlanSheet.Row r in s.Rows)
                Check(d.Contains(" " + r.Name + " key "), W, "the dump misses " + r.Name);
            Check(d.Contains("1 built") && d.Contains("(test)"), W, "the dump header: " + d.Split('\n')[0]);
        }
    }
}
