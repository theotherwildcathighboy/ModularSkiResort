// The Resort Planner's plan sheet (ResortPlanSheet, task R1-c): the rows the build queue, the marks outside the planner and the hub read.
// Where-tags `sheet/...`, PS-1..PS-6.
// Pinned:
// - Load sorts the rows lowest bottom spot first (ties by key, a NaN height last) and numbers Order. A repeated key keeps its first row.
// - Every change bumps Revision. Load and Clear bump PlanSerial. A no-op write bumps nothing.
// - BuildAllKeys takes the rows switched on and not built, in build order. CostToBuild sums the same rows.
// - Stations and states are per row. An unknown key changes nothing.
// - Dump names every row.
// - A hand-drawn line counts (PS-7): a complete census row of the row's type between its two stations. Not an open row, not another type,
//   not another pair of stations, not a row without both stations.
// - ReasonGroup (PS-8) maps every reason key of the queue and the lift tool to one player-text group, and an unknown key to GAME.
// - The lifts total (PS-9): CountToBuild, CountUnbuilt and the built count follow the states and the switches.
// - The station order of the queue (PS-10, task R1-e): the top is asked only after the base stands, so a refused base places and charges
//   nothing (field r1c 8b placed and charged the top). A refused top leaves the base. A cancel the queue did not ask for stops it.

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
            Ps7HandBuilt();
            Ps8ReasonGroups();
            Ps9Totals();
            Ps10StationOrder();
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

        private static LiftCensus.Row Census(EntityRef b, EntityRef t, byte type, LiftCensus.State state) =>
            new LiftCensus.Row { Id = b, Base = b, Top = t, State = state, Type = type };

        private static void Ps7HandBuilt()
        {
            const string W = "sheet/PS-7";
            var b = new EntityRef(10, 1);
            var t = new EntityRef(11, 1);
            var o = new EntityRef(12, 1);
            var rows = new List<LiftCensus.Row> { Census(o, t, 0, LiftCensus.State.Complete), Census(b, o, 0, LiftCensus.State.Complete) };
            Check(!ResortPlanSheet.HasCompleteLiftBetween(rows, b, t, 0), W, "a lift between other stations matched");
            rows.Add(Census(b, t, 0, LiftCensus.State.Open));
            Check(!ResortPlanSheet.HasCompleteLiftBetween(rows, b, t, 0), W, "an open (unfinished) row matched");
            rows.Add(Census(b, t, 2, LiftCensus.State.Complete));
            Check(!ResortPlanSheet.HasCompleteLiftBetween(rows, b, t, 0), W, "a complete lift of another type matched");
            Check(ResortPlanSheet.HasCompleteLiftBetween(rows, b, t, 2), W, "a complete lift of the row's type did not match");
            rows.Clear();
            rows.Add(Census(t, b, 1, LiftCensus.State.Complete));
            Check(ResortPlanSheet.HasCompleteLiftBetween(rows, b, t, 1), W, "the pair in the other order did not match");
            Check(!ResortPlanSheet.HasCompleteLiftBetween(rows, b, EntityRef.Null, 1), W, "a row without its top station matched");
            Check(!ResortPlanSheet.HasCompleteLiftBetween(rows, EntityRef.Null, t, 1), W, "a row without its base station matched");
            Check(!ResortPlanSheet.HasCompleteLiftBetween(null, b, t, 1) && !ResortPlanSheet.HasCompleteLiftBetween(new List<LiftCensus.Row>(), b, t, 1), W,
                  "a null or empty census matched");
        }

        private static void Ps8ReasonGroups()
        {
            const string W = "sheet/PS-8";
            string[][] want =
            {
                new[] { null, "" }, new[] { "", "" }, new[] { "MONEY", "MONEY" }, new[] { "POSE", "POSE" }, new[] { "PREFAB", "PREFAB" },
                new[] { "BASE[OverlapExisting]", "BASE_OVERLAP" }, new[] { "BASE[InWater]", "BASE_WATER" }, new[] { "BASE[ExceedsCityLimits]", "BASE_LAND" },
                new[] { "BASE[SteepSlope]", "BASE_OTHER" }, new[] { "BASE[GAME]", "BASE_OTHER" }, new[] { "BASE[LOST]", "BASE_OTHER" },
                new[] { "TOP[OverlapExisting]", "TOP_OVERLAP" }, new[] { "TOP[InWater]", "TOP_WATER" }, new[] { "TOP[ExceedsCityLimits]", "TOP_LAND" },
                new[] { "TOP[AlreadyExists]", "TOP_OTHER" }, new[] { "BASE[NotEnoughMoney]", "MONEY" }, new[] { "TOP[NotEnoughMoney]", "MONEY" },
                new[] { "BASE[GONE]", "GONE" }, new[] { "TOP[GONE]", "GONE" }, new[] { "STOPPED", "STOPPED" },
                new[] { "LINE[PICK]", "LINE_PICK" }, new[] { "LINE[ABANDONED]", "LINE_ABANDONED" }, new[] { "LINE[REFUSED]", "LINE_REFUSED" },
                new[] { "LINE[PENDING]", "GAME" }, new[] { "PLAN_LOW", "PLAN_LOW" }, new[] { "PLAN_STEEP", "PLAN_STEEP" }, new[] { "PLAN_HIGH", "PLAN_HIGH" },
                new[] { "PLAN_TRAIN", "GAME" }, new[] { "ALONG", "ALONG" }, new[] { "BACKWARD", "BACKWARD" }, new[] { "CROSS_SHALLOW", "CROSS" },
                new[] { "CROSS_CLEARANCE", "CROSS" }, new[] { "CROSS", "CROSS" }, new[] { "GAME", "GAME" }, new[] { "something odd", "GAME" },
            };
            foreach (string[] w in want)
            {
                string got = ResortPlanSheet.ReasonGroup(w[0]);
                Check(got == w[1], W, "reason '" + (w[0] ?? "null") + "' -> '" + got + "' (want '" + w[1] + "')");
            }
        }

        private static void Ps9Totals()
        {
            const string W = "sheet/PS-9";
            ResortPlanSheet s = Make();   // 2, 9, 7 (off), 5, 3: five rows, 100000 each
            Check(s.CountToBuild() == 4 && s.CountUnbuilt() == 5 && s.CountIn(SheetLiftState.Built) == 0, W,
                  "fresh sheet: to build " + s.CountToBuild() + ", unbuilt " + s.CountUnbuilt());
            s.SetState(2, SheetLiftState.Built, null);
            s.SetState(9, SheetLiftState.Refused, "MONEY");
            s.SetOn(3, false);
            Check(s.CountToBuild() == 2 && s.CountUnbuilt() == 4 && s.CountIn(SheetLiftState.Built) == 1, W,
                  "after a build, a switch off: to build " + s.CountToBuild() + ", unbuilt " + s.CountUnbuilt());
            Check(s.CostToBuild() == 200000, W, "cost " + s.CostToBuild());
            s.Clear("t");
            Check(s.CountToBuild() == 0 && s.CountUnbuilt() == 0, W, "an empty sheet has counts");
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
        private static void Ps10StationOrder()
        {
            const string W = "sheet/PS-10";
            const ResortPlanSheet.StationOutcome N = ResortPlanSheet.StationOutcome.None, Pe = ResortPlanSheet.StationOutcome.Pending,
                Pl = ResortPlanSheet.StationOutcome.Placed, Rf = ResortPlanSheet.StationOutcome.Refused, Cn = ResortPlanSheet.StationOutcome.Canceled;
            // The field r1c 8b sequence: the base waits, then the game refuses it. The top is never asked.
            Check(ResortPlanSheet.NextStationStep(false, Pe, false, false, N) == ResortPlanSheet.StationStep.Wait, W, "a pending base did not wait");
            Check(ResortPlanSheet.NextStationStep(false, Rf, false, false, N) == ResortPlanSheet.StationStep.RefuseBase, W, "a refused base was not RefuseBase");
            Check(ResortPlanSheet.NextStationStep(false, N, false, false, N) == ResortPlanSheet.StationStep.RefuseBase, W, "a lost base outcome was not RefuseBase");
            // 8a: the base stands, the top is asked, then refused: RefuseTop (the base stays in the row).
            Check(ResortPlanSheet.NextStationStep(false, Pl, false, false, N) == ResortPlanSheet.StationStep.AskTop, W, "a placed base did not ask the top");
            Check(ResortPlanSheet.NextStationStep(false, Pl, false, true, Pe) == ResortPlanSheet.StationStep.Wait, W, "a pending top did not wait");
            Check(ResortPlanSheet.NextStationStep(false, Pl, false, true, Rf) == ResortPlanSheet.StationStep.RefuseTop, W, "a refused top was not RefuseTop");
            Check(ResortPlanSheet.NextStationStep(false, Pl, false, true, N) == ResortPlanSheet.StationStep.RefuseTop, W, "a lost top outcome was not RefuseTop");
            Check(ResortPlanSheet.NextStationStep(false, Pl, false, true, Pl) == ResortPlanSheet.StationStep.Settle, W, "two placed stations did not settle");
            // The player took another tool: a canceled base or top stops the queue.
            Check(ResortPlanSheet.NextStationStep(false, Cn, false, false, N) == ResortPlanSheet.StationStep.PlayerStop, W, "a canceled base did not stop");
            Check(ResortPlanSheet.NextStationStep(false, Pl, false, true, Cn) == ResortPlanSheet.StationStep.PlayerStop, W, "a canceled top did not stop");
            // A second build of a row that kept a station starts from it.
            Check(ResortPlanSheet.NextStationStep(true, N, false, false, N) == ResortPlanSheet.StationStep.AskTop, W, "a kept base did not ask the top");
            Check(ResortPlanSheet.NextStationStep(true, N, true, false, N) == ResortPlanSheet.StationStep.Settle, W, "two kept stations did not settle");
            Check(ResortPlanSheet.NextStationStep(false, Pe, true, false, N) == ResortPlanSheet.StationStep.Wait, W, "a kept top did not wait for the base");
            Check(ResortPlanSheet.NextStationStep(false, Pl, true, false, N) == ResortPlanSheet.StationStep.Settle, W, "a placed base and a kept top did not settle");
            // Every input: the top is asked only when the base stands and the top is neither there nor asked.
            var all = new[] { N, Pe, Pl, Rf, Cn };
            int cases = 0, bad = 0;
            foreach (bool baseHad in new[] { false, true })
                foreach (ResortPlanSheet.StationOutcome b in all)
                    foreach (bool topHad in new[] { false, true })
                        foreach (bool asked in new[] { false, true })
                            foreach (ResortPlanSheet.StationOutcome tp in all)
                            {
                                cases++;
                                ResortPlanSheet.StationStep s = ResortPlanSheet.NextStationStep(baseHad, b, topHad, asked, tp);
                                bool baseStands = baseHad || b == Pl;
                                if (s == ResortPlanSheet.StationStep.AskTop && !(baseStands && !topHad && !asked)) bad++;
                                if (!baseStands && (s == ResortPlanSheet.StationStep.AskTop || s == ResortPlanSheet.StationStep.Settle || s == ResortPlanSheet.StationStep.RefuseTop)) bad++;
                                if (s == ResortPlanSheet.StationStep.Settle && !(baseStands && (topHad || (asked && tp == Pl)))) bad++;
                            }
            Check(cases == 200 && bad == 0, W, bad + " of " + cases + " input combinations broke the base-first rule");
        }
    }
}
