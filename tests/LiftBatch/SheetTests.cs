// The Resort Planner's plan sheet (ResortPlanSheet, task R1-c): the rows the build queue, the marks outside the planner and the hub read.
// Where-tags `sheet/...`, PS-1..PS-13.
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
// - The waiting reasons (PS-11, task R1-f): a line that waits in the lift tool gets its own reason group (PLAN_TRAIN, PLAN_SADDLE, PLAN_PITCH,
//   PLAN_CLEARANCE, BUILD_REFUSED, WAIT), never the GAME text (nothing refused it unless the game did).
// - The queue index (PS-12): a Stop keeps the run total, so "Stopping after lift 1 / 3" reads the place in the build that was asked for.
// - The queue rule (PS-13, task R2-l): a line with every span clear, exactly one tower over its sheave-train limit and no other tower fault
//   builds (BuildFlagged), and its Built row carries OverLimitTowers = 1. Two or more such towers wait as PLAN_TRAINS. A span fault, a tower
//   too steep or past its clearance, or a tram saddle past its cap waits as before. The flag survives in the dump and clears on a rebuild.

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
            Ps11WaitingKeys();
            Ps12QueueIndex();
            Ps13QueueRule();
            Ps14QueueStation();
        }

        /// <summary>PS-14 (task 2b-l): the queue's station height rule, shared by the placer, the planner and the realizer
        /// (StationAlign.PlaceStation). Ground falling 0.3 m per meter of local +z (the lot front is downhill, the line side uphill).</summary>
        private static void Ps14QueueStation()
        {
            const string W = "sheet/PS-14";
            System.Func<float3, float> ground = p => 1743.67f - 0.3f * p.z;
            StationDef dqBase = SkiCatalog.StationOf(1, StationRole.Base), fgTop = SkiCatalog.StationOf(0, StationRole.Top);
            Check(StationAlign.CatalogKindOf(dqBase) == StationAlign.StationTerrainKind.Pad && StationAlign.CatalogKindOf(fgTop) ==
                  StationAlign.StationTerrainKind.Slope, W, "the catalog kinds: DQ base pad, FG top slope");
            // a pad station cuts down to the lowest of front, origin, line side; its stub node sits on that plane; the rope at y + anchor
            StationAlign.QueueStation a = StationAlign.PlaceStation(StationAlign.QueueHeightRule, dqBase, float3.zero, quaternion.identity, ground);
            float front = ground(new float3(0f, 0f, dqBase.FrontLocalZ));
            Check(math.abs(a.StationY - front) < 1e-3f && math.abs(a.NodeY - a.StationY) < 1e-3f, W, $"pad: y {a.StationY:F2} (front {front:F2}), node {a.NodeY:F2}");
            Check(math.abs(a.RopeY - (a.StationY + dqBase.RopeAnchorLocalY)) < 1e-3f && math.abs(a.Tip.z - dqBase.StubTipLocalZ) < 1e-3f, W,
                  $"pad: rope {a.RopeY:F2}, tip z {a.Tip.z:F2}");
            Check(a.OriginGround - a.StationY > 4f, W, $"the pad station sits {a.OriginGround - a.StationY:F2} m under its origin ground (field r1 P3: 5.77)");
            // a slope station keeps the ground: y at its origin, its stub node on the terrain at the tip
            StationAlign.QueueStation s = StationAlign.PlaceStation(StationAlign.QueueHeightRule, fgTop, float3.zero, quaternion.identity, ground);
            float tipG = ground(new float3(0f, 0f, fgTop.StubTipLocalZ));
            Check(math.abs(s.StationY - 1743.67f) < 1e-3f && math.abs(s.NodeY - tipG) < 1e-3f && math.abs(s.RopeY - (1743.67f + fgTop.RopeAnchorLocalY)) < 1e-3f,
                  W, $"slope: y {s.StationY:F2}, node {s.NodeY:F2} (tip ground {tipG:F2}), rope {s.RopeY:F2}");
        }

        private static void Ps13QueueRule()
        {
            const string W = "sheet/PS-13";
            const LiftQueueBuild B = LiftQueueBuild.Build, F = LiftQueueBuild.BuildFlagged, Wt = LiftQueueBuild.Wait;
            // (clear, trains, over cap, the worst bend past a cap, too steep, clearance) -> the queue's choice, and the waiting key for plan
            // key k. The flag cap (task 2b-l): one tower over by at most LiftTowerPlan.FlagMaxExcessDeg builds flagged; further over waits.
            // Field r2g round 2: DQ tower 1 at 34.0 against 32 (builds flagged), gondola tower 8 at 47.5 against 40 (waits).
            Check(LiftTowerPlan.FlagMaxExcessDeg == 5.0f, W, "the flag cap is " + LiftTowerPlan.FlagMaxExcessDeg);
            var cases = new (bool clear, bool trains, int over, float ex, int steep, int clr, LiftQueueBuild want, string planKey, string wantKey)[]
            {
                (true, true, 0, 0f, 0, 0, B, "PLAN_CLEAR", null),
                (true, true, 1, 34.0f - 32f, 0, 0, F, "PLAN_TRAIN", null),    // r2 P1 detachable: 34 against 32 builds flagged
                (true, true, 1, 5.0f, 0, 0, F, "PLAN_TRAIN", null),           // at the cap
                (true, true, 1, 47.5f - 40f, 0, 0, Wt, "PLAN_TRAIN", "PLAN_TRAIN"),   // r4 P1 gondola: 47.5 against 40 waits
                (true, true, 1, 5.1f, 0, 0, Wt, "PLAN_TRAIN", "PLAN_TRAIN"),
                (true, true, 2, 1.0f, 0, 0, Wt, "PLAN_TRAIN", "PLAN_TRAINS"),  // two towers over waits, however small
                (true, true, 3, 1.0f, 0, 0, Wt, "PLAN_TRAIN", "PLAN_TRAINS"),
                (true, true, 1, 2f, 1, 0, Wt, "PLAN_TRAIN", "PLAN_PITCH"),    // one over the limit, one too steep: waits on the pitch
                (true, true, 1, 2f, 0, 1, Wt, "PLAN_TRAIN", "PLAN_CLEARANCE"),
                (true, true, 0, 0f, 1, 0, Wt, "PLAN_PITCH", "PLAN_PITCH"),
                (true, true, 0, 0f, 0, 2, Wt, "PLAN_CLEARANCE", "PLAN_CLEARANCE"),
                (false, true, 1, 2f, 0, 0, Wt, "PLAN_LOW", "PLAN_LOW"),      // an unclear span with one tower over: waits on the span
                (false, true, 0, 0f, 0, 0, Wt, "PLAN_STEEP", "PLAN_STEEP"),
                (true, false, 1, 2f, 0, 0, Wt, "PLAN_SADDLE", "PLAN_SADDLE"), // the tram saddle is no sheave train
                (true, false, 0, 0f, 0, 0, B, "PLAN_CLEAR", null),
            };
            foreach (var c in cases)
            {
                string at = $"clear {c.clear}, trains {c.trains}, over {c.over} by {c.ex:F1} deg, steep {c.steep}, clearance {c.clr}";
                LiftQueueBuild got = LiftTowerPlan.QueueRule(c.clear, c.trains, c.over, c.steep, c.clr, c.ex);
                Check(got == c.want, W, at + ": queue " + got + " (want " + c.want + ")");
                if (c.wantKey == null)
                    continue;
                string key = LiftTowerPlan.QueueWaitKey(c.planKey, c.clear, c.trains, c.over, c.steep, c.clr);
                Check(key == c.wantKey, W, at + ": waits as " + key + " (want " + c.wantKey + ")");
                Check(ResortPlanSheet.WaitingKey(key) == key && ResortPlanSheet.ReasonGroup(key) == key, W, key + " is not its own row group");
            }

            // The row flag: the state stays Built, the facts stay on the row, Revision moves once per change.
            ResortPlanSheet s = Make();
            s.SetState(9, SheetLiftState.Built, null);
            int rev = s.Revision;
            Check(s.SetOverLimit(9, 1, 4, 612f, 21.5f, 16f) && s.Revision == rev + 1, W, "the flag did not bump Revision");
            Check(!s.SetOverLimit(9, 1, 4, 612f, 21.5f, 16f) && s.Revision == rev + 1, W, "a repeated flag bumped Revision");
            Check(!s.SetOverLimit(99, 1, 4, 612f, 21.5f, 16f), W, "an unknown key took the flag");
            ResortPlanSheet.Row r = s.Find(9);
            Check(r.State == SheetLiftState.Built && r.ReasonKey == null && !r.Buildable, W, "the flagged row is not Built: " + r.State);
            Check(r.OverLimitTowers == 1 && r.OverLimitTower == 4 && r.OverLimitAlongM == 612f && r.OverLimitBendDeg == 21.5f && r.OverLimitCapDeg == 16f,
                  W, "the row facts: " + r.OverLimitTowers + " / " + r.OverLimitTower + " / " + r.OverLimitAlongM + " / " + r.OverLimitBendDeg + " / " + r.OverLimitCapDeg);
            Check(s.CountIn(SheetLiftState.Built) == 1 && !s.BuildAllKeys().Contains(9), W, "a flagged row is built again by build all");
            Check(ResortPlanSheet.RowText(r).Contains("Built [OverLimitTowers=1]"), W, "the row text: " + ResortPlanSheet.RowText(r));
            Check(!ResortPlanSheet.RowText(s.Find(2)).Contains("OverLimitTowers"), W, "an unflagged row prints the flag");
            // A rebuild with a sound line clears the flag (the queue writes the ticket's count every time).
            Check(s.SetOverLimit(9, 0, 4, 612f, 21.5f, 16f), W, "clearing the flag changed nothing");
            Check(r.OverLimitTowers == 0 && r.OverLimitTower == -1 && r.OverLimitBendDeg == 0f, W, "the cleared flag kept facts");
            // A fresh load starts without flags.
            s.SetOverLimit(9, 1, 4, 612f, 21.5f, 16f);
            s = Make();
            foreach (ResortPlanSheet.Row x in s.Rows)
                Check(x.OverLimitTowers == 0 && x.OverLimitTower == -1, W, x.Name + " starts flagged");
        }

        private static void Ps11WaitingKeys()
        {
            const string W = "sheet/PS-11";
            string[][] want =
            {
                new[] { "PLAN_TRAIN", "PLAN_TRAIN" }, new[] { "PLAN_TRAINS", "PLAN_TRAINS" }, new[] { "PLAN_SADDLE", "PLAN_SADDLE" },
                new[] { "PLAN_PITCH", "PLAN_PITCH" }, new[] { "PLAN_CLEARANCE", "PLAN_CLEARANCE" }, new[] { "PLAN_LOW", "PLAN_LOW" }, new[] { "ALONG", "ALONG" }, new[] { "BACKWARD", "BACKWARD" },
                new[] { "CROSS_SHALLOW", "CROSS_SHALLOW" },
                new[] { "the game refused the build (2 error(s): OverlapExisting x2)", "BUILD_REFUSED" },
                new[] { "no Temp edges 600 frames after the emission", "WAIT" }, new[] { "auto build off", "WAIT" }, new[] { "building", "WAIT" },
                new[] { "the build request was refused (see the line above)", "WAIT" }, new[] { "no plan", "WAIT" }, new[] { "", "WAIT" }, new[] { null, "WAIT" },
            };
            foreach (string[] w in want)
            {
                string key = ResortPlanSheet.WaitingKey(w[0]);
                Check(key == w[1], W, "waiting reason '" + (w[0] ?? "null") + "' -> '" + key + "' (want '" + w[1] + "')");
                Check(ResortPlanSheet.ReasonGroup(key) != "GAME", W, "waiting key '" + key + "' falls to the GAME text");
            }
            // The reasons reach the hub as their own group, never as the game's text.
            foreach (string k in new[] { "PLAN_TRAIN", "PLAN_TRAINS", "PLAN_SADDLE", "PLAN_PITCH", "PLAN_CLEARANCE", "BUILD_REFUSED", "WAIT" })
                Check(ResortPlanSheet.ReasonGroup(ResortPlanSheet.WaitingKey(k)) == k, W, "group of " + k);
        }

        private static void Ps12QueueIndex()
        {
            const string W = "sheet/PS-12";
            // A queue of 3 lifts. Running: lift 1 in flight, none done.
            Check(ResortPlanSheet.QueueIndexOf(true, 3, 0, false, true) == 1, W, "lift 1 in flight");
            Check(ResortPlanSheet.QueueIndexOf(true, 3, 1, false, true) == 2, W, "lift 2 in flight");
            Check(ResortPlanSheet.QueueIndexOf(true, 3, 3, false, false) == 3, W, "past the last lift stays on the total");
            // Stop with lift 1 in flight: the two waiting keys leave the queue, the total stays 3 ("Stopping after lift 1 / 3").
            Check(ResortPlanSheet.QueueIndexOf(true, 3, 0, true, true) == 1, W, "stopping on lift 1 of 3");
            // The lift ended, the queue has not seen its end yet: the place stays on the lift that finished.
            Check(ResortPlanSheet.QueueIndexOf(true, 3, 1, true, false) == 1, W, "stopped after lift 1");
            Check(ResortPlanSheet.QueueIndexOf(true, 3, 2, true, false) == 2, W, "stopped after lift 2");
            Check(ResortPlanSheet.QueueIndexOf(false, 3, 1, false, false) == 0, W, "idle reads 0");
            Check(ResortPlanSheet.QueueIndexOf(true, 1, 0, false, false) == 1, W, "never below 1 while running");
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
            // task 2b-l: a row that waits for a hand fit goes after every other row (it never blocks a row that may build)
            Check(string.Join(",", s.BuildAllKeys()) == "2,3,5", W, "after a build " + string.Join(",", s.BuildAllKeys()) + " (want 2,3,5: 5 waits)");
            Check(s.CostToBuild() == 300000, W, "cost to build after a build " + s.CostToBuild());
            s.SetOn(7, true);
            Check(string.Join(",", s.BuildAllKeys()) == "2,7,3,5", W, "7 switched on: " + string.Join(",", s.BuildAllKeys()));
            s.SetState(2, SheetLiftState.NeedsFitting, "PLAN_TRAIN");
            Check(string.Join(",", s.BuildAllKeys()) == "7,3,2,5", W, "two waiting rows keep their order, last: " + string.Join(",", s.BuildAllKeys()));
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
                new[] { "PLAN_TRAIN", "PLAN_TRAIN" }, new[] { "PLAN_TRAINS", "PLAN_TRAINS" }, new[] { "PLAN_SADDLE", "PLAN_SADDLE" },
                new[] { "PLAN_PITCH", "PLAN_PITCH" }, new[] { "PLAN_CLEARANCE", "PLAN_CLEARANCE" }, new[] { "BUILD_REFUSED", "BUILD_REFUSED" }, new[] { "WAIT", "WAIT" }, new[] { "ALONG", "ALONG" }, new[] { "BACKWARD", "BACKWARD" }, new[] { "CROSS_SHALLOW", "CROSS" },
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
