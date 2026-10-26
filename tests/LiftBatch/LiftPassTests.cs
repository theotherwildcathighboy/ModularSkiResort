// LP-26..LP-29: the 2026-10-26 lift pass (field test 2, build 8c1d406; report docs/research/lifts/lift-pass-2026-10-26.md).
// LP-26: the refused DQ line replayed on a uniform slope (the log has no Towers dump for it: ground from the two stub nodes, ropes as logged).
// LP-27: ordinary lines of every type: the tower next to each station is the shortest member the ground allows.
// LP-28: the engine's edge slope rule (ValidationHelpers.cs:1435-1490, SteepSlope) holds on every tool span; a too-steep span is flagged.
// LP-29: the tool's pin end margin (per end: a station end vs a loose end).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using SkiResortMod;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    public static class LiftPassTests
    {
        private static Func<bool, string, string, bool> s_Check;
        private static readonly List<string> s_Report = new List<string>();

        public static IReadOnlyList<string> Report => s_Report;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            s_Report.Clear();
            Lp26FieldDq();
            Lp27OrdinaryLines();
            Lp28EdgeSlope();
            Lp29PinMargins();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);

        // Field DQ (log :1828): first end = the TOP station 73311:283, stub node y 1240.43 - 3.10 = 1237.33; other end = the base
        // 73380:5, stub node y 1018.15 + 0.58 = 1018.73; 1024 m xz.
        private const float DqLength = 1024f, DqTopNode = 1237.33f, DqBaseNode = 1018.73f, DqRopeTop = 1240.43f, DqRopeBase = 1018.15f;

        private static float DqGround(float s) => math.lerp(DqTopNode, DqBaseNode, math.saturate(s / DqLength));

        private static void Lp26FieldDq()
        {
            const string W = "pass/LP-26";
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("detachable");
            LiftTerrainSampler t = (s, l) => DqGround(s);
            // The tool's path: LiftLineEdit plans with the edge slope rule and the end node heights.
            var asLogged = new LiftLineEdit(c, new float3(0f, DqTopNode, 0f), new float3(DqLength, DqBaseNode, 0f), DqRopeTop, DqRopeBase);
            LiftTowerPlan p1 = asLogged.Replan(t);
            s_Report.Add("LP-26 field DQ 1024 m, ropes as logged (top node + 3.10, base node - 0.58) | " + LiftPlannerTests.Describe(p1) +
                         " | end members " + EndMembers(p1) + "; max edge slope " + p1.MaxEdgeSlope.ToString("F2") + " (refused at " +
                         c.MaxEdgeSlope.ToString("F2") + "); carrier at the stations " + p1.EndCarrierA.ToString("F1") + " / " + p1.EndCarrierB.ToString("F1") + " m");
            Check(p1.Clear, W, "as logged: not clear: " + LiftPlannerTests.Describe(p1));
            Check(p1.MaxEdgeSlope < c.MaxEdgeSlope, W, "as logged: an edge the engine refuses (SteepSlope): " + p1.MaxEdgeSlope.ToString("F2"));
            Check(p1.LineTowers > 0 && p1.Nodes[1].Member == 0 && p1.Nodes[p1.Nodes.Length - 2].Member == 0, W,
                  "as logged: not the shortest tower next to each station: " + EndMembers(p1));
            Check(p1.EndCarrierB < -3f, W, "as logged: the sunk base station (carrier " + p1.EndCarrierB.ToString("F1") + " m) not detected");
            var leveled = new LiftLineEdit(c, new float3(0f, DqTopNode, 0f), new float3(DqLength, DqBaseNode, 0f), DqTopNode + 3.70f, DqBaseNode + 3.70f);
            LiftTowerPlan p2 = leveled.Replan(t);
            s_Report.Add("LP-26 field DQ 1024 m, leveled stations (rope = stub ground + 3.70) | " + LiftPlannerTests.Describe(p2) +
                         " | end members " + EndMembers(p2) + "; max edge slope " + p2.MaxEdgeSlope.ToString("F2"));
            Check(p2.Clear && p2.MaxEdgeSlope < c.MaxEdgeSlope, W, "leveled: " + LiftPlannerTests.Describe(p2));
            Check(p2.EndCarrierA >= 0f && p2.EndCarrierB >= 0f, W, "leveled: a station reported sunk");
        }

        /// <summary>The station's rope height above its stub node: the bullwheel (WheelLocal.y) or, on a shuttle, the dock.</summary>
        private static float StationRopeAbove(LiftTypeDef def, StationRole role)
        {
            StationDef s = SkiCatalog.StationOf(def.Index, role);
            if (s == null)
                return 0f;
            return float.IsNaN(s.WheelLocal.y) ? s.DockLocalA.y : s.WheelLocal.y;
        }

        private static void Lp27OrdinaryLines()
        {
            const string W = "pass/LP-27";
            var lengths = new Dictionary<string, float> { { "fixedGrip", 400f }, { "detachable", 1200f }, { "gondola", 1500f }, { "tram", 2000f } };
            int lines = 0, shortest = 0;
            for (int t = 0; t < SkiCatalog.Lifts.Count; t++)
            {
                LiftTypeDef def = SkiCatalog.Lifts[t];
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(def);
                if (c == null || !lengths.TryGetValue(def.Id, out float L))
                    continue;
                float baseAbove = StationRopeAbove(def, StationRole.Base), topAbove = StationRopeAbove(def, StationRole.Top);
                // Uniform slopes, and a base run-out (flat 100 m, then the slope): a typical base area.
                foreach (float g in new[] { 0.15f, 0.30f, 0.45f })
                {
                    foreach (bool runOut in new[] { false, true })
                    {
                        Func<float, float> ground = s => 1000f + g * (runOut ? math.max(0f, s - 100f) : s);
                        LiftTerrainSampler terrain = (s, l) => ground(s);
                        var a = new float3(0f, ground(0f), 0f);
                        var b = new float3(L, ground(L), 0f);
                        var e = new LiftLineEdit(c, a, b, a.y + baseAbove, b.y + topAbove);
                        LiftTowerPlan p = e.Replan(terrain);
                        string label = def.Id + " " + L.ToString("F0") + " m at " + (g * 100f).ToString("F0") + "%" + (runOut ? " after a 100 m run-out" : string.Empty);
                        lines++;
                        Check(p.Clear, W, label + ": not clear: " + LiftPlannerTests.Describe(p));
                        Check(p.MaxEdgeSlope < c.MaxEdgeSlope, W, label + ": an edge the engine refuses: " + p.MaxEdgeSlope.ToString("F2"));
                        if (p.LineTowers == 0)
                            continue;
                        int endA = p.Nodes[1].Member, endB = p.Nodes[p.Nodes.Length - 2].Member, inner = 0;
                        for (int i = 2; i < p.Nodes.Length - 2; i++)
                            inner = math.max(inner, p.Nodes[i].Member);
                        // A uniform slope: no station tower taller than the middle. After a run-out a station tower may stand up to one
                        // tower's worth (TowerWorthM) taller: a DQ14 is 4 m, worth less than a tower. The tram keeps fewest towers first
                        // (TowerWorthM 0): reported, not held to the rule.
                        float limit = Rise(c, inner) + (runOut ? c.TowerWorthM - 1e-3f : 1e-3f);
                        bool ok = c.TowerWorthM <= 0f || (p.LineTowers <= 2 ? endA == 0 || endB == 0
                            : Rise(c, endA) <= limit && Rise(c, endB) <= limit);
                        if (endA == 0 && endB == 0)
                            shortest++;
                        Check(ok, W, label + ": a station tower taller than the line's middle: " + EndMembers(p) + " | " + LiftPlannerTests.Describe(p));
                        if (g == 0.30f)
                            s_Report.Add("LP-27 " + label + " | shortest tower next to each station, clear, edges within the slope limit | " +
                                         "end members " + EndMembers(p) + "; max edge slope " + p.MaxEdgeSlope.ToString("F2") + " | " + LiftPlannerTests.Describe(p));
                    }
                }
            }
            s_Report.Add("LP-27 summary | every line clear, no station tower taller than the middle | " + lines + " line(s), " + shortest +
                         " with the shortest member at both station ends");
        }

        private static void Lp28EdgeSlope()
        {
            const string W = "pass/LP-28";
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("detachable");
            LiftTerrainSampler t = (s, l) => DqGround(s);
            int tallest = c.Ladder.Length - 1;
            var planner = new LiftTowerPlanner { CheckEdgeSlope = true, EndNodeYA = DqTopNode, EndNodeYB = DqBaseNode };
            // The field plan's base end: the tallest DQ 9 m from the base station (DQ18 @1015 m in the field: slope 1.10 > 0.9).
            LiftTowerPlan steep = planner.PlanFixed(t, DqLength, DqRopeTop, DqRopeBase, c, new[] { 500f, 1015f }, new[] { 0, tallest }, null, assign: false);
            LiftPlanSpan last = steep.Spans[steep.Spans.Length - 1];
            s_Report.Add("LP-28 DQ20 9 m from the base station | the span flagged TOO STEEP (" + c.MaxEdgeSlope.ToString("F2") + ") | edge slope " +
                         last.EdgeSlope.ToString("F2") + (last.SlopeViolated ? " violated" : " passed"));
            Check(last.SlopeViolated && !steep.Clear, W, "a 9 m edge from the station to a DQ20 not flagged: slope " + last.EdgeSlope.ToString("F2"));
            // The same nodes with the member assignment free: phase (c) keeps the edge under the limit.
            LiftTowerPlan fixedNodes = planner.PlanFixed(t, DqLength, DqRopeTop, DqRopeBase, c, new[] { 500f, 1015f });
            s_Report.Add("LP-28 the same nodes, members assigned | no edge over the limit | " + LiftPlannerTests.Describe(fixedNodes) +
                         "; max edge slope " + fixedNodes.MaxEdgeSlope.ToString("F2"));
            for (int s = 0; s < fixedNodes.Spans.Length; s++)
                Check(!fixedNodes.Spans[s].SlopeViolated, W, "assigned members leave span " + s + " too steep: " + fixedNodes.Spans[s].EdgeSlope.ToString("F2"));
            // Off (the fit, the diagnostic): no span is flagged and Clear ignores the slope.
            var off = new LiftTowerPlanner();
            LiftTowerPlan offPlan = off.PlanFixed(t, DqLength, DqRopeTop, DqRopeBase, c, new[] { 500f, 1015f }, new[] { 0, tallest }, null, assign: false);
            Check(!offPlan.Spans[offPlan.Spans.Length - 1].SlopeViolated, W, "the slope rule flagged a span with CheckEdgeSlope off");
            // Every type's limit is its line prefab's NetGeometryPrefab m_MaxSlopeSteepness, read from the gamedata-mirror (a prefab edit
            // that changes it without the planner constant fails here).
            var slope = new Regex("\"m_MaxSlopeSteepness\":\\s*([0-9.]+)");
            for (int i = 0; i < SkiCatalog.Lifts.Count; i++)
            {
                LiftTypeDef def = SkiCatalog.Lifts[i];
                LiftTowerPlanConstants lc = LiftTowerPlanConstants.For(def);
                string path = CatalogTests.RepoFile("gamedata-mirror/ImportedData/" + def.LinePrefab + "/" + def.LinePrefab + ".Prefab");
                if (lc == null || !Check(path != null, W, def.LinePrefab + ".Prefab not in the gamedata-mirror"))
                    continue;
                Match m = slope.Match(File.ReadAllText(path));
                if (!Check(m.Success, W, def.LinePrefab + ": no m_MaxSlopeSteepness"))
                    continue;
                float prefab = float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                Check(math.abs(lc.MaxEdgeSlope - prefab) < 1e-4f, W, def.Id + " MaxEdgeSlope " + lc.MaxEdgeSlope + " != " + def.LinePrefab + " m_MaxSlopeSteepness " + prefab);
            }
        }

        private static void Lp29PinMargins()
        {
            const string W = "pass/LP-29";
            LiftTowerPlanConstants tram = LiftTowerPlanConstants.For("tram");
            const float L = 1756f;   // field test 2 (log :1412): a pin at 1,737 m refused five times
            LiftLineEdit stations = Flat(tram, L, true, true);
            LiftLineEditResult r = stations.AddPin(1737f);
            s_Report.Add("LP-29 tram 1,756 m station to station, pin at 1,737 m | accepted (station end margin " + stations.PinMarginB.ToString("F0") +
                         " m; was 40) | " + r);
            Check(r == LiftLineEditResult.Done, W, "tram pin 19 m from a station refused: " + r);
            Check(stations.AddPin(L - 5f) == LiftLineEditResult.TooCloseToStation && math.abs(stations.LastEndMarginM - tram.StepM) < 1e-3f, W,
                  "tram pin 5 m from a station accepted, or the wrong margin " + stations.LastEndMarginM);
            LiftLineEdit loose = Flat(tram, L, true, false);
            Check(loose.AddPin(1737f) == LiftLineEditResult.TooCloseToStation && math.abs(loose.LastEndMarginM - tram.MinSpanM) < 1e-3f, W,
                  "tram pin 19 m from a loose end (a tower) accepted, or the wrong margin " + loose.LastEndMarginM);
            foreach (string id in new[] { "fixedGrip", "detachable", "gondola" })
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(id);
                LiftLineEdit e = Flat(c, 600f, true, true);
                Check(e.PinMarginA == c.MinSpanM && e.PinMarginB == c.MinSpanM, W, id + " end margin changed: " + e.PinMarginA);
                Check(e.AddPin(c.MinSpanM - c.StepM) == LiftLineEditResult.TooCloseToStation && e.AddPin(c.MinSpanM) == LiftLineEditResult.Done, W,
                      id + " end margin not MinSpanM");
            }
        }

        private static float Rise(LiftTowerPlanConstants c, int member) => c.Ladder[member].TopM - c.Ladder[0].TopM;

        /// <summary>The planner's objective for a plan: towers x TowerWorthM + the rise of the towers next to the stations (a single
        /// tower counts for both ends, as in MinCost); the tram (TowerWorthM 0): the tower count.</summary>
        internal static float Score(LiftTowerPlan p)
        {
            LiftTowerPlanConstants c = p.Constants;
            if (c.TowerWorthM <= 0f)
                return p.LineTowers;
            float rise = p.LineTowers == 0 ? 0f : Rise(c, p.Nodes[1].Member) + Rise(c, p.Nodes[p.Nodes.Length - 2].Member);
            return p.LineTowers * c.TowerWorthM + rise;
        }

        private static LiftLineEdit Flat(LiftTowerPlanConstants c, float length, bool stationA, bool stationB)
        {
            var e = new LiftLineEdit(c, new float3(0f, 1000f, 0f), new float3(length, 1000f, 0f), 1000f + c.RopeAboveBase(0), 1000f + c.RopeAboveBase(0),
                                     stationA, stationB);
            e.Replan((s, l) => 1000f);
            return e;
        }

        internal static string EndMembers(LiftTowerPlan p)
        {
            if (p.LineTowers == 0)
                return "-";
            LiftPlanNode a = p.Nodes[1], b = p.Nodes[p.Nodes.Length - 2];
            return Short(p, a) + " @" + a.AlongM.ToString("F0") + " / " + Short(p, b) + " @" + b.AlongM.ToString("F0");
        }

        private static string Short(LiftTowerPlan p, LiftPlanNode n) =>
            p.Constants.Ladder[n.Member].Name.Replace("SkiLiftTower", "").Replace("SkiTramTower", "T");
    }
}
