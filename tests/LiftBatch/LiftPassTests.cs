// LP-26..LP-29: the 2026-10-26 lift pass (field test 2, build 8c1d406; report docs/research/lifts/lift-pass-2026-10-26.md).
// LP-26: the refused DQ line replayed on a uniform slope (the log has no Towers dump for it: ground from the two stub nodes, ropes as logged).
// LP-27: ordinary lines of every type: the tower next to each station is the shortest member the ground allows.
// LP-28: the engine's edge slope rule (ValidationHelpers.cs:1435-1490, SteepSlope) holds on every tool span; a too-steep span is flagged.
// LP-29: the tool's pin end margin (per end: a station end vs a loose end).
// LP-30: the review of the pass (docs/research/review-153b64e-2026-10-26.md): F9 the slope gate on a span with no tower, F3 phase (c)
// keeps the station-side members, F4 the station rule buys at most one tower. Each sweep must bind at least once; LP-26..30 were each
// shown able to fail by breaking their rule once (lift-pass-2026-10-26.md, "Review fixes").
// LP-32: removing a tower adds at most one (field test 2026-10-31, FIX-LIST item 24), see the profile below.

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
            Lp30ReviewFixes();
            Lp31ToolText();
            Lp32RemoveTower();
            Lp33Crossing();
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
            // The field line was logged on the 0.004 DQ rope (before the rope sag round): replayed at that sag.
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("detachable").With(sagRatio: LiftPlannerTests.LoggedSag);
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
            // The planner before the pass (the full floor at the stations, no slope gate) reproduces the refused field plan: a DQ18
            // 9 m from the base station, slope 1.10. Each rule alone removes the refused edge: the approach floor (p1 above) and the gate.
            // That planner worked from end A as given (the top here): AsGiven replays it. The 9 m tower needs the grid's 4 m remainder at
            // the base, which only the top-first plan has; since planner-direction-2026-11-04 the planner works from the base (the lower
            // rope), the remainder lies at the top, and the pre-pass rules alone stand a tower 9 m from the TOP instead (reported below).
            var before = new LiftTowerPlanner { CheckEdgeSlope = false, StationA = false, StationB = false, EndNodeYA = DqTopNode, EndNodeYB = DqBaseNode, AsGiven = true };
            LiftTowerPlan p0 = before.Plan(t, DqLength, DqRopeTop, DqRopeBase, c);
            var gateOnly = new LiftTowerPlanner { CheckEdgeSlope = true, StationA = false, StationB = false, EndNodeYA = DqTopNode, EndNodeYB = DqBaseNode, AsGiven = true };
            LiftTowerPlan pg = gateOnly.Plan(t, DqLength, DqRopeTop, DqRopeBase, c);
            var canonical = new LiftTowerPlanner { CheckEdgeSlope = false, StationA = false, StationB = false, EndNodeYA = DqTopNode, EndNodeYB = DqBaseNode };
            LiftTowerPlan pc = canonical.Plan(t, DqLength, DqRopeTop, DqRopeBase, c);
            s_Report.Add("LP-26 field DQ, the pre-pass rules from the base (the planner's end since 2026-11-04) | end members " + EndMembers(pc) +
                         ", max edge slope " + pc.MaxEdgeSlope.ToString("F2"));
            s_Report.Add("LP-26 field DQ, the planner before the pass (full floor at the stations, no gate) | the refused field plan | end members " +
                         EndMembers(p0) + ", max edge slope " + p0.MaxEdgeSlope.ToString("F2") + "; with the gate alone " + EndMembers(pg) + ", " +
                         pg.MaxEdgeSlope.ToString("F2") + (pg.Clear ? ", clear" : ", not clear (the floor: the approach floor is what clears it)"));
            Check(p0.MaxEdgeSlope >= c.MaxEdgeSlope, W, "the pre-pass plan no longer reproduces the field refusal: " + LiftPlannerTests.Describe(p0));
            Check(pg.MaxEdgeSlope < c.MaxEdgeSlope, W, "the slope gate leaves an edge the engine refuses: " + pg.MaxEdgeSlope.ToString("F2"));
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
            // A knoll just past the station clearance: the floor wants a tall tower 10-40 m from the station, an edge the engine
            // refuses (SteepSlope). With the gate the plan is clear and every edge passes; the sweep must bind (the gate changes the plan).
            int binds = 0, knolls = 0;
            foreach (string id in new[] { "fixedGrip", "detachable", "gondola" })
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(id);
                const float len = 400f;
                foreach (float x0 in new[] { 15f, 20f, 25f, 30f, 40f })
                foreach (float h in new[] { 4f, 6f, 8f, 10f, 12f })
                foreach (float w in new[] { 5f, 10f })
                {
                    LiftTerrainSampler t = Hump(0f, h, x0, w);
                    float yA = t(0f, 0f), yB = t(len, 0f);
                    float ra = yA + c.RopeAboveBase(0), rb = yB + c.RopeAboveBase(0);
                    LiftTowerPlan off = ToolPlanner(yA, yB, false).Plan(t, len, ra, rb, c);
                    LiftTowerPlan on = ToolPlanner(yA, yB).Plan(t, len, ra, rb, c);
                    if (!off.Clear)
                        continue;
                    knolls++;
                    string label = id + " knoll " + h.ToString("F0") + " m @" + x0.ToString("F0") + " w " + w.ToString("F0");
                    if (off.MaxEdgeSlope >= c.MaxEdgeSlope + 0.0005f)
                        binds++;
                    Check(on.Clear && on.MaxEdgeSlope < c.MaxEdgeSlope, W, label + ": with the gate " + LiftPlannerTests.Describe(on) +
                          "; max edge slope " + on.MaxEdgeSlope.ToString("F2") + " (without " + off.MaxEdgeSlope.ToString("F2") + ")");
                }
            }
            s_Report.Add("LP-27 knoll 15-40 m past the base station (FG / DQ / GD, 400 m) | clear with every edge under the limit | " + knolls +
                         " line(s), the gate changes " + binds + " of them (without it an edge the engine refuses)");
            Check(binds > 0, W, "the knoll sweep never binds (no plan without the gate has an edge over the limit)");
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
            // The tool's own nodes for this line, members assigned (phase (c), from the shortest): clear, every edge under the limit.
            var tool = new LiftLineEdit(c, new float3(0f, DqTopNode, 0f), new float3(DqLength, DqBaseNode, 0f), DqRopeTop, DqRopeBase);
            float[] toolNodes = Inner(tool.Replan(t));
            LiftTowerPlan fixedNodes = planner.PlanFixed(t, DqLength, DqRopeTop, DqRopeBase, c, toolNodes);
            s_Report.Add("LP-28 the tool's nodes, members assigned | clear, no edge over the limit | " + LiftPlannerTests.Describe(fixedNodes) +
                         "; max edge slope " + fixedNodes.MaxEdgeSlope.ToString("F2"));
            Check(fixedNodes.Clear && fixedNodes.MaxEdgeSlope < c.MaxEdgeSlope, W, "assigned members at the tool's nodes: " + LiftPlannerTests.Describe(fixedNodes));
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

        /// <summary>The tool's planner settings (LiftLineEdit.Replan) on a line with both ends at stations, the end nodes at
        /// <paramref name="yA"/> / <paramref name="yB"/>.</summary>
        private static LiftTowerPlanner ToolPlanner(float yA, float yB, bool checkSlope = true, bool stationRule = true) =>
            new LiftTowerPlanner { CheckEdgeSlope = checkSlope, StationRule = stationRule, EndNodeYA = yA, EndNodeYB = yB };

        /// <summary>Station-side rise of a plan (Rise of the towers next to each station; one tower counts twice).</summary>
        private static float EndRise(LiftTowerPlan p) =>
            p.LineTowers == 0 ? 0f : Rise(p.Constants, p.Nodes[1].Member) + Rise(p.Constants, p.Nodes[p.Nodes.Length - 2].Member);

        private static float[] Inner(LiftTowerPlan p)
        {
            var a = new float[p.LineTowers];
            for (int i = 0; i < a.Length; i++)
                a[i] = p.Nodes[i + 1].AlongM;
            return a;
        }

        /// <summary>A hump of <paramref name="h"/> m (Gaussian, width w) at x0 on a uniform grade g.</summary>
        private static LiftTerrainSampler Hump(float g, float h, float x0, float w) =>
            (s, l) => 1000f + g * s + h * math.exp(-(s - x0) * (s - x0) / (w * w));

        private static void Lp30ReviewFixes()
        {
            const string W = "pass/LP-30";
            // F9: a short station-to-station span, clear for the floor but steeper than the line prefab allows: never a clear plan with
            // that span (the engine refuses it, SteepSlope).
            int steep = 0, avoided = 0;
            foreach (string id in new[] { "fixedGrip", "detachable", "gondola" })
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(id);
                foreach (float len in new[] { 60f, 90f, 120f, 160f })
                {
                    float dy = (c.MaxEdgeSlope + 0.15f) * len;
                    LiftTerrainSampler t = (s, l) => 1000f + dy * s / len;
                    float ra = 1000f + c.RopeAboveBase(0), rb = 1000f + dy + c.RopeAboveBase(0);
                    LiftTowerPlan off = ToolPlanner(1000f, 1000f + dy, false).Plan(t, len, ra, rb, c);
                    LiftTowerPlan on = ToolPlanner(1000f, 1000f + dy).Plan(t, len, ra, rb, c);
                    string label = "F9 " + id + " " + len.ToString("F0") + " m at " + (dy / len).ToString("F2");
                    if (off.LineTowers == 0 && off.Clear)
                        steep++;
                    Check(!on.Clear || on.MaxEdgeSlope < c.MaxEdgeSlope, W, label + ": clear with an edge the engine refuses (" +
                          on.MaxEdgeSlope.ToString("F2") + "): " + LiftPlannerTests.Describe(on));
                    Check(on.LineTowers > 0 || on.Spans[0].SlopeViolated, W, label + ": a too-steep tower-free span not flagged");
                    // Towers cannot cut a chord between fixed end heights (their edges average its slope): none added for it.
                    Check(on.LineTowers == off.LineTowers, W, label + ": " + on.LineTowers + " tower(s) added for a slope they cannot fix");
                    if (on.LineTowers == 0 && on.Spans[0].SlopeViolated)
                        avoided++;
                }
            }
            s_Report.Add("LP-30 F9 steep station-to-station spans (grade = limit + 0.15, FG / DQ / GD 60-160 m) | flagged, never clear, no " +
                         "useless towers | " + steep + " tower-free plan(s) clear for the floor without the gate, " + avoided + " flagged with it");
            Check(steep > 0, W, "F9: the sweep never binds (no tower-free span clear for the floor)");

            // F3 + F4 on hump terrains: the plan's station-side rise is the least any clear member assignment at its nodes allows (F3),
            // and it uses at most one tower more than the fewest (F4).
            int cases = 0, extra = 0, f3binds = 0;
            foreach (string id in new[] { "fixedGrip", "detachable" })
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(id);
                int L = c.Ladder.Length;
                foreach (float len in id == "fixedGrip" ? new[] { 120f, 160f, 175f, 240f, 320f } : new[] { 240f, 320f, 420f, 460f, 485f })
                foreach (float g in new[] { 0f, 0.2f })
                foreach (float h in new[] { 4f, 8f, 12f, 16f, 20f })
                foreach (float x0f in new[] { 0.3f, 0.5f })
                foreach (float w in new[] { 20f, 45f, 80f })
                {
                    LiftTerrainSampler t = Hump(g, h, x0f * len, w);
                    float yA = t(0f, 0f), yB = t(len, 0f);
                    float ra = yA + c.RopeAboveBase(0), rb = yB + c.RopeAboveBase(0);
                    LiftTowerPlan p = ToolPlanner(yA, yB).Plan(t, len, ra, rb, c);
                    LiftTowerPlan fewest = ToolPlanner(yA, yB, true, false).Plan(t, len, ra, rb, c);
                    if (!p.Clear || !fewest.Clear)
                        continue;
                    cases++;
                    string label = id + " " + len.ToString("F0") + " m, grade " + g.ToString("F1") + ", hump " + h.ToString("F0") + " m @" +
                                   (x0f * len).ToString("F0") + " w " + w.ToString("F0");
                    Check(p.LineTowers <= fewest.LineTowers + 1, W, "F4 " + label + ": " + p.LineTowers + " towers, fewest " + fewest.LineTowers);
                    if (p.LineTowers > fewest.LineTowers)
                        extra++;
                    if (p.LineTowers < 1 || p.LineTowers > 3)
                        continue;
                    // F3 oracle: every member assignment at the plan's nodes.
                    float[] inner = Inner(p);
                    LiftTowerPlanner oracle = ToolPlanner(yA, yB);
                    int n = inner.Length, combos = 1;
                    for (int q = 0; q < n; q++)
                        combos *= L;
                    float best = float.PositiveInfinity;
                    var mem = new int[n];
                    for (int code = 0; code < combos; code++)
                    {
                        for (int q = 0, x = code; q < n; q++, x /= L)
                            mem[q] = x % L;
                        LiftTowerPlan f = oracle.PlanFixed(t, len, ra, rb, c, inner, mem, null, assign: false);
                        if (f.Clear)
                            best = math.min(best, EndRise(f));
                    }
                    Check(EndRise(p) <= best + 1e-3f, W, "F3 " + label + ": station-side rise " + EndRise(p).ToString("F1") + " > the least " +
                          best.ToString("F1") + " (" + EndMembers(p) + ")");
                    // F3 binds where phase (c) alone (no station rule: the shortest members, then raises) lands above the least rise.
                    LiftTowerPlan reset = ToolPlanner(yA, yB, true, false).PlanFixed(t, len, ra, rb, c, inner);
                    if (reset.Clear && EndRise(reset) > best + 1e-3f)
                        f3binds++;
                }
            }
            s_Report.Add("LP-30 F3 / F4 hump terrains (FG, DQ) | the least station-side rise at the plan's nodes, at most one extra tower | " +
                         cases + " clear line(s), " + extra + " with one extra tower; F3 binds on " + f3binds);
            Check(f3binds > 0, W, "F3: the sweep never binds (phase (c) never lands above the least rise)");

            // F4 where it binds: FG lines over three narrow 12-16 m humps (a quarter, half, three quarters). Without the cap the
            // trade takes two towers more than the fewest (FG2 beside both stations: 4 -> 6 and 5 -> 7 towers; probe, 2026-10-26).
            LiftTowerPlanConstants fg = LiftTowerPlanConstants.For("fixedGrip");
            foreach (float2 lh in new[] { new float2(400f, 12f), new float2(490f, 16f) })
            {
                float len = lh.x;
                LiftTerrainSampler t = Humps3(lh.y, 4f, len);
                float y = t(0f, 0f), r0 = y + fg.RopeAboveBase(0);
                LiftTowerPlan p = ToolPlanner(y, y).Plan(t, len, r0, r0, fg);
                LiftTowerPlan fewest = ToolPlanner(y, y, true, false).Plan(t, len, r0, r0, fg);
                s_Report.Add("LP-30 F4 FG " + len.ToString("F0") + " m over three " + lh.y.ToString("F0") + " m humps | at most one tower over the fewest | " +
                             p.LineTowers + " tower(s) (fewest " + fewest.LineTowers + "), " + EndMembers(p) + ", score " + Score(p).ToString("F0") +
                             " | " + LiftPlannerTests.Describe(p));
                Check(p.Clear && fewest.Clear, W, "F4 FG " + len + " m: not clear: " + LiftPlannerTests.Describe(p));
                Check(p.LineTowers <= fewest.LineTowers + 1, W, "F4 FG " + len + " m: " + p.LineTowers + " towers, fewest " + fewest.LineTowers);
            }
        }

        /// <summary>Three Gaussian humps of <paramref name="h"/> m (width w) at a quarter, half and three quarters of the line.</summary>
        private static LiftTerrainSampler Humps3(float h, float w, float len) =>
            (s, l) => 1000f + h * (math.exp(-math.pow((s - 0.25f * len) / w, 2f)) + math.exp(-math.pow((s - 0.5f * len) / w, 2f)) +
                                   math.exp(-math.pow((s - 0.75f * len) / w, 2f)));

        /// <summary>LP-31: the lift tool's player text (LocaleEN SkiResort.LiftTool.*): every key the tool and its tooltip can show
        /// exists, reads as plain sentences (at most 25 words, no em dash, no log syntax), names only the {ARG}s the code passes; the
        /// plan and tower keys follow the plan.</summary>
        private static void Lp31ToolText()
        {
            const string W = "pass/LP-31";
            const string Prefix = "SkiResort.LiftTool.";
            string locPath = CatalogTests.RepoFile("mod/SkiResortMod/LocaleEN.cs");
            string toolPath = CatalogTests.RepoFile("mod/SkiResortMod/Systems/LiftLineToolSystem.cs");
            // The tool's partials (LiftLineToolSystem.*.cs, e.g. the section delete) and its tooltip show keys too.
            var codePaths = new List<string>();
            if (toolPath != null)
            {
                codePaths.AddRange(Directory.GetFiles(Path.GetDirectoryName(toolPath), "LiftLineToolSystem*.cs"));
                codePaths.Add(Path.Combine(Path.GetDirectoryName(toolPath), "LiftLineTooltipSystem.cs"));
            }
            if (!Check(locPath != null && toolPath != null, W, "LocaleEN.cs or LiftLineToolSystem.cs not found"))
                return;
            var loc = new Dictionary<string, string>();
            foreach (Match m in new Regex("\\{ \"" + Regex.Escape(Prefix) + "([^\"]+)\", \"((?:[^\"\\\\]|\\\\.)*)\" \\}").Matches(File.ReadAllText(locPath)))
                loc[m.Groups[1].Value] = m.Groups[2].Value;
            // The keys the code shows: prompts, type names, plan / tower keys, the tool's T("...") notices and the REFUSED[type] set.
            var used = new SortedSet<string>();
            foreach (LiftLineStep step in Enum.GetValues(typeof(LiftLineStep)))
                foreach (string type in new[] { null, "detachable" })
                    foreach (bool drag in new[] { false, true })
                        foreach (bool off in new[] { false, true })
                            used.Add(LiftLineInput.PromptKey(step, type, drag, off && drag));
            foreach (string t in new[] { "fixedGrip", "detachable", "gondola", "tram", "any" })
                used.Add("TYPE[" + t + "]");
            // PLAN_TRAIN / TOWER_TRAIN (a tower over its cap) and SUNK / SUNK_END (LiftLineInput.SunkKey): FIX-LIST 55; PLAN_PITCH /
            // TOWER_PITCH (a tower past the pitch cap): DECISIONS 600; PLAN_SADDLE / TOWER_SADDLE (a tram saddle past its cap): 600 a; TOWER_CLEARANCE
            // (a rope half past its carrier clearance; the plan line keys PLAN_TRAIN): 600 c.
            foreach (string k in new[] { "PLAN_CLEAR", "PLAN_LOW", "PLAN_STEEP", "PLAN_HIGH", "PLAN_TRAIN", "TOWER", "TOWER_PINNED", "TOWER_LOW",
                                         "TOWER_TRAIN", "PLAN_PITCH", "TOWER_PITCH", "PLAN_SADDLE", "TOWER_SADDLE", "TOWER_CLEARANCE", "SUNK", "SUNK_END" })
                used.Add(k);
            foreach (string path in codePaths)
                foreach (Match m in new Regex("\\bT\\(\"([A-Z_]+(?:\\[[A-Za-z]+\\])?)\"").Matches(File.ReadAllText(path)))
                    used.Add(m.Groups[1].Value);
            foreach (string e in new[] { "SteepSlope", "OverlapExisting", "LongDistance", "ShortDistance", "InWater", "ExceedsCityLimits", "Other" })
                used.Add("REFUSED[" + e + "]");
            // DEG: the BEND notice's plan bend and TOWER_TRAIN's rope bend (LiftLineToolSystem.Deg); CAP: TOWER_TRAIN's cap. LIMIT: TOWER_CLEARANCE's
            // carrier-clearance limit (one decimal, LiftLineToolSystem.Deg).
            var args = new HashSet<string> { "TYPE", "TOWERS", "LENGTH", "N", "COUNT", "HEIGHT", "CLEAR", "MIN", "M", "DEG", "CAP", "LIMIT" };
            int worst = 0;
            foreach (string key in used)
            {
                if (!Check(loc.TryGetValue(key, out string text), W, Prefix + key + " is shown but missing from LocaleEN"))
                    continue;
                int words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
                worst = math.max(worst, words);
                Check(words <= 25, W, key + " has " + words + " words (at most 25): " + text);
                Check(text.IndexOf('—') < 0 && !text.Contains("\\u2014"), W, key + " has an em dash: " + text);   // the char or its source escape
                Check(!Regex.IsMatch(text, "\\[[a-z]+\\]|\\d+\\.\\d|LiftLine|Temp |entity|\\bspan\\(s\\)"), W, key + " reads like a log: " + text);
                foreach (Match a in Regex.Matches(text, "\\{([A-Z]+)\\}"))
                    Check(args.Contains(a.Groups[1].Value), W, key + " names {" + a.Groups[1].Value + "}, which the code never passes");
            }
            foreach (string key in loc.Keys)
                Check(used.Contains(key), W, Prefix + key + " in LocaleEN is never shown (stale)");
            s_Report.Add("LP-31 lift tool player text | every shown key in LocaleEN, at most 25 words, no em dash or log syntax | " + used.Count +
                         " key(s), longest " + worst + " words");

            // The plan and tower keys follow the plan: the field DQ as logged is clear; the pre-pass DQ18 next to the base is too steep.
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("detachable");
            LiftTerrainSampler t0 = (s, l) => DqGround(s);
            var edit = new LiftLineEdit(c, new float3(0f, DqTopNode, 0f), new float3(DqLength, DqBaseNode, 0f), DqRopeTop, DqRopeBase);
            LiftTowerPlan clear = edit.Replan(t0);
            Check(LiftLineInput.PlanKey(clear, out float len) == "PLAN_CLEAR" && math.abs(len - DqLength) < 1f, W,
                  "the clear field DQ reads " + LiftLineInput.PlanKey(clear, out _) + ", length " + len);
            var gate = new LiftTowerPlanner { CheckEdgeSlope = true, EndNodeYA = DqTopNode, EndNodeYB = DqBaseNode };
            LiftTowerPlan steep = gate.PlanFixed(t0, DqLength, DqRopeTop, DqRopeBase, c, new[] { 500f, 1015f }, new[] { 0, c.Ladder.Length - 1 }, null, assign: false);
            string steepKey = LiftLineInput.PlanKey(steep, out _);
            Check(steepKey == "PLAN_STEEP" || steepKey == "PLAN_LOW", W, "a DQ20 9 m from the base station reads " + steepKey);
            string towerKey = LiftLineInput.TowerKey(clear, 1, false, out float h, out float cl, out _, out _);
            Check(math.abs(h - c.Ladder[clear.Nodes[1].Member].TopM) < 1e-3f && cl > 0f && towerKey == "TOWER", W,
                  "tower 1 of the clear DQ: " + towerKey + ", height " + h + ", clear " + cl);
            s_Report.Add("LP-31 plan / tower keys | clear field DQ " + LiftLineInput.PlanKey(clear, out _) + ", tower 1 " + towerKey + " " +
                         h.ToString("F0") + " m tall, chairs " + cl.ToString("F0") + " m up; DQ20 beside the base " + steepKey);
        }

        // LP-32: removing a tower never adds more than the line needs (field test 2026-10-31, FIX-LIST item 24). Gondola, both ends open
        // ground, ground measured in game every 3.5 m from the TOP end (z -4800) toward the base: the base end node stands 1.5 m past the
        // last sample and the top end node 0.5 m short of the first (they match the end ropes, node + 10, to 0.03 m).
        private static readonly float[] L8Ground =
        {
            1549.7f, 1547.3f, 1544.9f, 1542.5f, 1540.2f, 1538.0f, 1535.8f, 1533.6f, 1531.4f, 1529.2f, 1527.0f, 1524.8f, 1522.7f, 1520.6f,
            1518.5f, 1516.4f, 1514.4f, 1512.5f, 1510.5f, 1508.5f, 1506.7f, 1504.8f, 1502.8f, 1500.9f, 1499.0f, 1497.0f, 1495.0f, 1493.0f,
            1491.1f, 1489.1f, 1487.2f, 1485.2f, 1483.3f, 1481.5f, 1479.6f, 1477.8f, 1476.1f, 1474.4f, 1472.7f, 1471.1f, 1469.5f, 1467.9f,
            1466.4f, 1465.0f, 1463.5f, 1462.1f, 1460.8f, 1459.5f, 1458.4f, 1457.2f, 1456.0f, 1455.0f, 1454.0f, 1453.1f, 1452.4f, 1451.6f,
            1451.1f, 1450.8f, 1450.5f, 1450.4f, 1450.6f, 1450.8f, 1451.4f, 1452.0f, 1452.9f, 1453.8f, 1455.0f, 1456.2f, 1457.6f, 1458.9f,
            1460.2f, 1461.4f, 1462.5f, 1463.5f, 1464.3f, 1465.1f, 1465.8f, 1466.3f, 1466.7f, 1467.0f, 1467.2f, 1467.5f, 1467.5f, 1467.6f,
            1467.4f, 1467.2f, 1466.9f, 1466.4f, 1465.8f, 1464.9f, 1464.0f, 1462.8f, 1461.6f, 1460.3f, 1458.8f, 1457.2f, 1455.6f, 1453.9f,
            1452.3f, 1450.6f, 1448.9f, 1447.2f, 1445.6f, 1444.0f, 1442.3f, 1440.7f, 1439.2f, 1437.7f, 1436.2f, 1434.7f, 1433.4f, 1432.1f,
            1430.8f, 1429.5f, 1428.1f, 1426.8f, 1425.4f, 1424.0f, 1422.6f, 1421.0f, 1419.3f, 1417.4f, 1415.3f, 1413.0f, 1410.5f, 1407.8f,
            1405.0f, 1401.9f, 1398.6f, 1395.2f, 1391.7f, 1387.9f, 1384.0f, 1380.0f, 1376.1f, 1372.1f, 1368.0f, 1364.0f, 1360.1f, 1356.3f,
            1352.7f, 1349.1f, 1345.7f,
        };

        private const float L8Length = 498f, L8RopeBase = 1354.21f, L8RopeTop = 1559.33f;

        private static float L8At(float along)
        {
            float k = (L8Length + 0.5f - along) / 3.5f;
            int i = math.clamp((int)math.floor(k), 0, L8Ground.Length - 2);
            return L8Ground[i] + (L8Ground[i + 1] - L8Ground[i]) * (k - i);
        }

        // The profile has no side samples and the planner's floor runs over the high side (half width 4.1 m). This high side above the
        // center, a knot every 50 m, is FITTED, not measured: on the planner before the fix it gave the field's first plan exactly
        // (GD20 @70 / @195 / @300) and a second plan of 9 towers, GD16 x7 + GD20 x2, 8 of the 9 positions the field's (60 m for 230 m).
        private static readonly float[] L8Side = { 0.71f, 3.76f, 3.56f, 0.87f, 2.02f, 1.81f, 5.15f, 3.73f, 3.80f, 4.03f, 3.01f };

        private static float L8High(float along)
        {
            float q = math.clamp(along / 50f, 0f, L8Side.Length - 1.001f);
            int i = (int)q;
            return L8At(along) + L8Side[i] + (L8Side[i + 1] - L8Side[i]) * (q - i);
        }

        // The field line and its fitted sides are from the 0.004 gondola rope (before the rope sag round): replayed at that sag.
        private static LiftTowerPlanConstants L8Constants(bool today = false) =>
            today ? LiftTowerPlanConstants.For("gondola") : LiftTowerPlanConstants.For("gondola").With(sagRatio: LiftPlannerTests.LoggedSag);

        private static LiftLineEdit L8Edit(bool today = false) =>
            new LiftLineEdit(L8Constants(today), new float3(0f, L8RopeBase - 10f, 0f), new float3(L8Length, L8RopeTop - 10f, 0f),
                             L8RopeBase, L8RopeTop, false, false);

        private static string Towers(LiftTowerPlan p)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 1; i < p.Nodes.Length - 1; i++)
                sb.Append(i == 1 ? string.Empty : ", ").Append(Short(p, p.Nodes[i])).Append(" @").Append(p.Nodes[i].AlongM.ToString("F0"));
            return sb.ToString();
        }

        private static void Lp32RemoveTower()
        {
            const string W = "pass/LP-32";
            const float SensibleSpanM = 30f;
            var grounds = new (string Name, LiftTerrainSampler T)[]
            {
                ("field sides", (s, l) => l == 0f ? L8At(s) : L8High(s)),
                ("center only", (s, l) => L8At(s)),
            };
            foreach (var g in grounds)
            {
                LiftLineEdit e = L8Edit();
                LiftTowerPlan first = e.Replan(g.T);
                string firstText = Towers(first);
                if (g.Name == "field sides")
                    Check(firstText == "GD20 @70, GD20 @195, GD20 @300", W, "the fitted ground no longer gives the field's first plan: " + firstText);
                // Every tower of the first plan, one at a time: at most one more tower than before, none on the removed spot, every span sensible.
                for (int r = 1; r < first.Nodes.Length - 1; r++)
                {
                    float spot = first.Nodes[r].AlongM;
                    string label = g.Name + ", tower at " + spot.ToString("F0") + " m removed";
                    LiftLineEdit x = L8Edit();
                    x.Replan(g.T);
                    Check(x.RemoveTower(spot) == LiftLineEditResult.Done, W, label + ": refused");
                    LiftTowerPlan p = x.Replan(g.T);
                    s_Report.Add("LP-32 " + label + " | at most one tower more than " + first.LineTowers + ", none within " + x.RemovedRadiusM.ToString("F0") +
                                 " m of it, spans at least " + SensibleSpanM.ToString("F0") + " m | " + Towers(p) + "; " + x.Describe());
                    Check(p.Clear, W, label + ": not clear: " + x.Describe());
                    Check(p.LineTowers <= first.LineTowers + 1, W, label + ": " + p.LineTowers + " towers, was " + first.LineTowers + ": " + Towers(p));
                    Check(x.TowerNear(spot, x.RemovedRadiusM - 0.01f) < 0, W, label + ": a tower inside the radius: " + Towers(p));
                    for (int s = 0; s < p.Spans.Length; s++)
                        Check(p.Spans[s].LengthM >= SensibleSpanM - 1e-3f, W, label + ": a span of " + p.Spans[s].LengthM.ToString("F0") + " m: " + Towers(p));
                    for (int i = 0; i < x.Pins.Count; i++)
                        Check(x.TowerNear(x.Pins[i], 0.01f) > 0, W, label + ": the pin at " + x.Pins[i].ToString("F0") + " m moved: " + Towers(p));
                    // Undo gives the first plan back (the field's right click).
                    Check(x.Undo() && Towers(x.Replan(g.T)) == firstText, W, label + ": undo: " + Towers(x.Plan));
                }
            }

            // The same removals at today's gondola sag (0.025), reported: the fitted sides were fitted at 0.004, so they pin nothing here.
            {
                LiftLineEdit e = L8Edit(true);
                LiftTowerPlan first = e.Replan(grounds[0].T);
                var sb = new System.Text.StringBuilder(Towers(first));
                for (int r = 1; r < first.Nodes.Length - 1; r++)
                {
                    LiftLineEdit x = L8Edit(true);
                    x.Replan(grounds[0].T);
                    x.RemoveTower(first.Nodes[r].AlongM);
                    LiftTowerPlan p = x.Replan(grounds[0].T);
                    float shortest = float.MaxValue;
                    for (int s = 0; s < p.Spans.Length; s++)
                        shortest = math.min(shortest, p.Spans[s].LengthM);
                    sb.Append(" | -@").Append(first.Nodes[r].AlongM.ToString("F0")).Append(": ").Append(Towers(p))
                      .Append(p.Clear ? ", clear" : ", VIOLATED").Append(", shortest span ").Append(shortest.ToString("F0")).Append(" m");
                }
                s_Report.Add("LP-32 field sides at today's sag 0.025 (report) | " + sb);
            }

            // The field case on the planner alone: pins at 70 and 300 m, 195 m forbidden.
            var pins = new[] { new LiftPlanNode { AlongM = 70f, Member = -1 }, new LiftPlanNode { AlongM = 300f, Member = -1 } };
            var direct = new LiftTowerPlanner
            {
                Forbidden = new[] { 195f }, StationA = false, StationB = false, CheckEdgeSlope = true,
                EndNodeYA = L8RopeBase - 10f, EndNodeYB = L8RopeTop - 10f,
            };
            LiftTowerPlan d = direct.Plan(grounds[0].T, L8Length, L8RopeBase, L8RopeTop, L8Constants(), pins);
            s_Report.Add("LP-32 pins 70 / 300, 195 forbidden, the planner alone | one or two towers between the pins | " + Towers(d));
            Check(d.Clear && d.LineTowers <= 4, W, "pins 70 / 300, 195 forbidden: " + Towers(d));

            // One spot only: the same line with a 25 m knife ridge (flanks 1.1 m per m) at 290 m. A tower must stand within 9 m of the crest for the
            // rope to pass it, so removing the crest tower leaves the line no other way: it comes back there, and no more towers than before.
            // (The other towers are tall pins here: with no plan under the Forbidden rule they would drop to the shortest member and buy rescue towers.)
            LiftTerrainSampler knife = (s, l) => (l == 0f ? L8At(s) : L8High(s)) + math.max(0f, 25f - 1.1f * math.abs(s - 290f));
            LiftLineEdit ridge = L8Edit();
            LiftTowerPlan ridgeFirst = ridge.Replan(knife);
            int crest = ridge.TowerNear(290f, 9f);
            if (Check(ridgeFirst.Clear && crest > 0, W, "knife ridge: no clear first plan with a tower at the crest: " + Towers(ridgeFirst)))
            {
                Check(ridge.RemoveTower(ridgeFirst.Nodes[crest].AlongM) == LiftLineEditResult.Done, W, "knife ridge: remove refused");
                LiftTowerPlan back = ridge.Replan(knife);
                s_Report.Add("LP-32 knife ridge, the crest tower removed (" + Towers(ridgeFirst) + ") | it comes back at the crest, no more towers | " + Towers(back));
                Check(back.Clear && back.LineTowers <= ridgeFirst.LineTowers && ridge.TowerNear(290f, 9f) > 0, W, "knife ridge: " + Towers(back));
            }

            // Every lift type on hump ground: removing any tower adds at most one (the sweep reports how often it adds one).
            int removals = 0, oneMore = 0, moreThanOne = 0;
            foreach (string id in new[] { "fixedGrip", "detachable", "gondola", "tram" })
            {
                LiftTowerPlanConstants k = LiftTowerPlanConstants.For(id);
                float len = id == "tram" ? 2000f : id == "fixedGrip" ? 400f : 900f;
                foreach (float grade in new[] { 0.1f, 0.35f })
                foreach (float h in new[] { 0f, 10f, 25f })
                foreach (float at in new[] { 0.3f, 0.6f })
                {
                    LiftTerrainSampler t = Hump(grade, h, at * len, 0.06f * len);
                    float yA = t(0f, 0f), yB = t(len, 0f);
                    float3 a = new float3(0f, yA, 0f), b = new float3(len, yB, 0f);
                    float ra = yA + k.RopeAboveBase(0), rb = yB + k.RopeAboveBase(0);
                    var e = new LiftLineEdit(k, a, b, ra, rb);
                    LiftTowerPlan free = e.Replan(t);
                    if (!free.Clear)
                        continue;
                    for (int r = 1; r < free.Nodes.Length - 1; r++)
                    {
                        var x = new LiftLineEdit(k, a, b, ra, rb);
                        x.Replan(t);
                        x.RemoveTower(free.Nodes[r].AlongM);
                        LiftTowerPlan p = x.Replan(t);
                        string label = id + " " + len.ToString("F0") + " m, grade " + grade.ToString("F2") + ", hump " + h.ToString("F0") + " m at " +
                                       (at * len).ToString("F0") + ", tower at " + free.Nodes[r].AlongM.ToString("F0") + " m removed";
                        removals++;
                        Check(p.Clear, W, label + ": not clear: " + LiftPlannerTests.Describe(p));
                        if (p.LineTowers == free.LineTowers + 1)
                            oneMore++;
                        else if (p.LineTowers > free.LineTowers + 1)
                        {
                            moreThanOne++;
                            Check(false, W, label + ": " + p.LineTowers + " towers, was " + free.LineTowers + ": " + Towers(p));
                        }
                    }
                }
            }
            s_Report.Add("LP-32 sweep, every lift type on hump ground | removing any tower adds at most one | " + removals + " removal(s), " + oneMore +
                         " add one more than the plan had, " + moreThanOne + " add more");
            Check(removals > 0, W, "the sweep removed nothing");
        }

        // LP-33: a line crossing another lift is found in plan view (field test 2026-10-31, FIX-LIST item 26); a joint at the line's own end is
        // not a crossing. The line runs from (0,0) to (500,0) in x and z.
        private static void Lp33Crossing()
        {
            const string W = "pass/LP-33";
            LiftLineEdit e = Flat(LiftTowerPlanConstants.For("gondola"), 500f, true, true);
            const float M = 5f;
            Check(e.Crosses(new float2(250f, -40f), new float2(250f, 40f), M), W, "a piece square across the middle crosses");
            Check(e.Crosses(new float2(100f, -40f), new float2(180f, 40f), M), W, "a slanted piece across crosses");
            Check(e.Crosses(new float2(250f, 0f), new float2(250f, 40f), M), W, "a piece starting on the line joins it");
            Check(!e.Crosses(new float2(250f, 10f), new float2(250f, 40f), M), W, "a piece stopping short does not cross");
            Check(!e.Crosses(new float2(0f, 0f), new float2(0f, 80f), M), W, "a joint at the first end is no crossing");
            Check(!e.Crosses(new float2(503f, -40f), new float2(503f, 40f), M), W, "a piece within the margin of the far end is no crossing");
            Check(!e.Crosses(new float2(620f, -40f), new float2(620f, 40f), M), W, "a piece beyond the far end is no crossing");
            Check(!e.Crosses(new float2(0f, 20f), new float2(500f, 20f), M), W, "a parallel piece never crosses");
            s_Report.Add("LP-33 crossing another lift | found across the line, not at its own ends | 8 cases");
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
