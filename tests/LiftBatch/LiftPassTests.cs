// LP-26..LP-29: the 2026-10-26 lift pass (field test 2, build 8c1d406; report docs/research/lifts/lift-pass-2026-10-26.md).
// LP-26: the refused DQ line replayed on a uniform slope (the log has no Towers dump for it: ground from the two stub nodes, ropes as logged).
// LP-27: ordinary lines of every type: the tower next to each station is the shortest member the ground allows.
// LP-28: the engine's edge slope rule (ValidationHelpers.cs:1435-1490, SteepSlope) holds on every tool span; a too-steep span is flagged.
// LP-29: the tool's pin end margin (per end: a station end vs a loose end).
// LP-30: the review of the pass (docs/research/review-153b64e-2026-10-26.md): F9 the slope gate on a span with no tower, F3 phase (c)
// keeps the station-side members, F4 the station rule buys at most one tower. Each sweep must bind at least once; LP-26..30 were each
// shown able to fail by breaking their rule once (lift-pass-2026-10-26.md, "Review fixes").

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
            // The planner before the pass (the full floor at the stations, no slope gate) reproduces the refused field plan: a DQ18
            // 9 m from the base station, slope 1.10. Each rule alone removes the refused edge: the approach floor (p1 above) and the gate.
            var before = new LiftTowerPlanner { CheckEdgeSlope = false, StationA = false, StationB = false, EndNodeYA = DqTopNode, EndNodeYB = DqBaseNode };
            LiftTowerPlan p0 = before.Plan(t, DqLength, DqRopeTop, DqRopeBase, c);
            var gateOnly = new LiftTowerPlanner { CheckEdgeSlope = true, StationA = false, StationB = false, EndNodeYA = DqTopNode, EndNodeYB = DqBaseNode };
            LiftTowerPlan pg = gateOnly.Plan(t, DqLength, DqRopeTop, DqRopeBase, c);
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
