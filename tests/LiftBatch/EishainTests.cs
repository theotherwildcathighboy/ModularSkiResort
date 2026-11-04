// The Eishain-Niedertal pass defects (docs/research/field/2026-11-04/eishain, FIX-LIST 48-52). Where-tags `eishain/EI-n`.
// EI-52 a built line is never fitted into a worse plan (the after-load FG line of the pass, reconstructed from the dump's ground).
// EI-49 planner cost on the steep DQ sites (a report: the cost lines before / after FIX-LIST 49).
// EI-48 a ground end plans with the rope of the station that finishes it. (a) The gate: every catalog station's RopeAnchorLocalY is
//       the lowest HV rope sub-lane anchor of its mirror prefab, FrontLocalZ its lot depth x 4, its stub tip at y 0 / StubTipLocalZ, and
//       the anchor is below the shortest tower's rope (so no type plans a station end too high again). (b) Tram 2 of the pass: planned
//       clear (+0.4) with node + 26.25 at its ground ends and read -6.58 once the stations stood; planned with the stations' anchors it
//       stays clear with them. (c) The lot-front rule: ground falling away beyond the end lowers the rope.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    public static class EishainTests
    {
        private static Func<bool, string, string, bool> s_Check;
        private static readonly List<string> s_Report = new List<string>();

        public static IReadOnlyList<string> Report => s_Report;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            s_Report.Clear();
            Ei52();
            Ei48Gate();
            Ei48Tram2();
            Ei48Front();
            Ei49();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);
        private static string F(double v, string f = "F2") => v.ToString(f, CultureInfo.InvariantCulture);

        // along, center ground (the dump's terrain at the nodes and chord - clearance at 20/40/60/80 % of each span)
        internal static readonly float[] FgCenter = {
            0f, 1378.57f, 15f, 1384.16f, 30f, 1391.55f, 45f, 1400.93f, 60f, 1411.32f, 75f, 1422.16f,
            90f, 1433.11f, 105f, 1444.11f, 120f, 1455.00f, 135f, 1464.70f, 150f, 1474.00f,
            167f, 1484.27f, 184f, 1494.14f, 201f, 1503.30f, 218f, 1511.97f, 235f, 1519.84f,
            246f, 1524.65f, 257f, 1529.46f, 268f, 1534.47f, 279f, 1539.58f, 290f, 1544.39f,
            297f, 1546.70f, 304f, 1548.42f, 311f, 1549.33f, 318f, 1549.45f, 325f, 1548.96f,
            342.8f, 1546.27f, 360.6f, 1543.37f, 378.4f, 1541.78f, 396.2f, 1541.18f, 414f, 1540.99f };
        // along, high side - center (the fit / plan logs at the nodes)
        internal static readonly float[] FgSide = { 0f, 0.5f, 75f, 0.6f, 150f, 0.7f, 235f, 0.2f, 290f, 0.9f, 325f, 1.5f, 414f, 1.7f };

        internal static float Lerp(float[] sy, float s)
        {
            if (s <= sy[0]) return sy[1];
            for (int i = 2; i < sy.Length; i += 2)
                if (s <= sy[i])
                    return math.lerp(sy[i - 1], sy[i + 1], (s - sy[i - 2]) / math.max(sy[i] - sy[i - 2], 1e-6f));
            return sy[sy.Length - 1];
        }

        internal static LiftTerrainSampler Sampler(float[] center, float[] side, float half) =>
            (s, l) => Lerp(center, s) + Lerp(side, s) * l / half;

        // EI-52: the FG line of the pass (Mod.log 1309-1427, 2434-2468, 2642-2658). In session the tool built it with ground ends
        // (ropes node + 7.27 = 1385.84 / 1548.26) and the fit kept it (+0.1); both stations were then attached line-first with no
        // re-fit, and their lane anchors sit lower (1379.28 / 1545.95). After the load the fit read the ends as stations and the line
        // as it stands (elevations 0.35 / 4 / 4 / 4.5 / 4.5: FG8ComboA, FG12 x 2, FG12BreakA x 2) at -1.7 m, and wrote a Short plan
        // with a head over its cap (FG20BreakA d +47.2) and changed heads.
        private static readonly float[] FgInner = { 75f, 150f, 235f, 290f, 325f };
        private static readonly int[] FgMembers = { 0, 1, 1, 1, 1 };
        private static readonly LiftTowerHead[] FgHeads =
            { LiftTowerHead.Combo, LiftTowerHead.Line, LiftTowerHead.Line, LiftTowerHead.Break, LiftTowerHead.Break };

        private static void Ei52()
        {
            const string w = "eishain/EI-52";
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("fixedGrip");
            LiftTerrainSampler t = Sampler(FgCenter, FgSide, c.HalfWidthM);
            var kinds = new LiftTrainKind[FgInner.Length];

            // (a) in session: ground ends, the tool's fresh nodes (not built): the fit's plan is written; the field's Kept +0.1
            var pl = new LiftTowerPlanner { PreviousKinds = kinds, PreviousHeads = new LiftTowerHead[FgInner.Length] };
            LiftTowerFit f0 = pl.Fit(t, 414f, 1385.84f, 1548.26f, c, FgInner, false, 90f, FgMembers);
            Check(f0.Mode == LiftFitMode.Kept && f0.Plan.Sound, w, "in session the tool line is Kept and sound (field: Kept +0.1); got " +
                  f0.Mode + " " + Desc(f0.Plan));
            Check(LiftTowerPlanner.ToWrite(null, f0, false, out _) == f0.Plan, w, "a line not yet built takes the fit");
            s_Report.Add("EI-52 in session: " + f0.Mode + " " + F(f0.Plan.MinFloorMarginM) + " " + Desc(f0.Plan));

            // (b) after the load (or the station attach in session): station ends, the lane ropes, the line as built with its own heads
            pl = new LiftTowerPlanner { StationA = true, StationB = true, PreviousKinds = kinds, PreviousHeads = FgHeads, HeadsAsGiven = true };
            LiftTowerPlan before = pl.PlanFixed(t, 414f, 1379.28f, 1545.95f, c, FgInner, FgMembers, null, assign: false);
            pl.HeadsAsGiven = false;
            LiftTowerFit fit = pl.Fit(t, 414f, 1379.28f, 1545.95f, c, FgInner, true, 60f, FgMembers);
            before.KindCounts(out _, out _, out _, out _, out int overB);
            fit.Plan.KindCounts(out _, out _, out _, out _, out int overF);
            s_Report.Add("EI-52 after load as built: " + F(before.MinFloorMarginM) + " " + Desc(before));
            s_Report.Add("EI-52 after load best fit: " + fit.Mode + " " + F(fit.Plan.MinFloorMarginM) + " " + Desc(fit.Plan) +
                         " needed " + fit.Needed);
            // the repro: the line as built is violated near a station and the best fit is Short with a tower over its cap (field)
            Check(!before.Clear && overB == 0, w, "the line as built: violated near a station, no tower over cap (field -1.7 m); got " +
                  Desc(before));
            Check(fit.Mode == LiftFitMode.Short && overF > overB, w,
                  "the best fit is Short with more towers over cap (field FG20BreakA d +47.2); got " + fit.Mode + " " + Desc(fit.Plan));
            // the fix: over a built line that plan is not written
            LiftTowerPlan written = LiftTowerPlanner.ToWrite(before, fit, true, out string worse);
            Check(written == null && worse != null && worse.Contains("over cap"), w,
                  "a worse unsound plan is never written over a built line; got " +
                  (written == null ? "kept (" + worse + ")" : "written " + Desc(written)));
            s_Report.Add("EI-52 kept as built: " + (worse ?? "-"));

            // (c) the rule's other sides: a sound plan is always written over a built line; the line as built is no worse than itself
            Check(LiftTowerPlanner.ToWrite(before, f0, true, out _) == f0.Plan, w, "a sound plan is written over a built line");
            var same = new LiftTowerFit { Mode = LiftFitMode.Kept, Plan = before };
            Check(LiftTowerPlanner.ToWrite(before, same, true, out string none) == before && none == null, w,
                  "the line as built is no worse than itself; got " + none);
        }

        // ------------------------------------------------------------------ EI-48

        private static readonly Dictionary<string, string> s_CidName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static JsonElement? MirrorJson(string name, string w)
        {
            string path = CatalogTests.RepoFile("gamedata-mirror/ImportedData/" + name + "/" + name + ".Prefab");
            if (!Check(path != null, w, name + ".Prefab not in gamedata-mirror"))
                return null;
            string text = Regex.Replace(File.ReadAllText(path), "\\$fstrref:(\"[^\"]*\")", "$1");
            return JsonDocument.Parse(text).RootElement;
        }

        private static string CidName(string cidRef)
        {
            string cid = cidRef.Replace("CID:", string.Empty);
            if (s_CidName.Count == 0)
            {
                string any = CatalogTests.RepoFile("gamedata-mirror/ImportedData/SkiTramTrackRope/SkiTramTrackRope.Prefab");
                foreach (string dir in Directory.GetDirectories(Path.GetDirectoryName(Path.GetDirectoryName(any))))
                {
                    string n = Path.GetFileName(dir);
                    string c = Path.Combine(dir, n + ".Prefab.cid");
                    if (File.Exists(c))
                        s_CidName[File.ReadAllText(c).Trim()] = n;
                }
            }
            return s_CidName.TryGetValue(cid, out string name) ? name : cidRef;
        }

        private static IEnumerable<JsonElement> Rows(JsonElement root, string field)
        {
            foreach (JsonElement comp in root.GetProperty("components").GetProperty("$rcontent").EnumerateArray())
                if (comp.TryGetProperty(field, out JsonElement list))
                    foreach (JsonElement row in list.GetProperty("$rcontent").EnumerateArray())
                        yield return row;
        }

        private static float3 P(JsonElement curve, string pt)
        {
            JsonElement e = curve.GetProperty(pt);
            return new float3(e.GetProperty("x").GetSingle(), e.GetProperty("y").GetSingle(), e.GetProperty("z").GetSingle());
        }

        /// <summary>The lowest HV rope anchor of a station prefab (the lane's start; also its end when the lane joins two nodes, as
        /// LiftLineToolSystem.AddAnchors / LaneSystem.FindAnchors), the SkiLiftMarker stub's outer point and the lot depth.</summary>
        private static bool StationMirror(string prefab, string w, out float anchorY, out float3 stubTip, out int lotDepth)
        {
            anchorY = float.PositiveInfinity;
            stubTip = new float3(float.NaN);
            lotDepth = 0;
            JsonElement? root = MirrorJson(prefab, w);
            if (root == null)
                return false;
            lotDepth = root.Value.GetProperty("m_LotDepth").GetInt32();
            foreach (JsonElement lane in Rows(root.Value, "m_SubLanes"))
            {
                string laneName = CidName(lane.GetProperty("m_LanePrefab").GetString());
                string lanePath = CatalogTests.RepoFile("gamedata-mirror/ImportedData/" + laneName + "/" + laneName + ".Prefab");
                if (lanePath == null)
                    continue;   // not one of ours (the comm wire's vanilla lane): no rope
                JsonElement? lp = MirrorJson(laneName, w);
                bool hv = false;
                foreach (JsonElement comp in lp.Value.GetProperty("components").GetProperty("$rcontent").EnumerateArray())
                    if (comp.TryGetProperty("m_UtilityType", out JsonElement ut) && (ut.GetInt32() & 0x40) != 0)
                        hv = true;
                if (!hv)
                    continue;
                JsonElement curve = lane.GetProperty("m_BezierCurve");
                anchorY = math.min(anchorY, P(curve, "a").y);
                JsonElement ni = lane.GetProperty("m_NodeIndex");
                if (ni.GetProperty("x").GetInt32() != ni.GetProperty("y").GetInt32())
                    anchorY = math.min(anchorY, P(curve, "d").y);
            }
            foreach (JsonElement net in Rows(root.Value, "m_SubNets"))
                if (CidName(net.GetProperty("m_NetPrefab").GetString()) == "SkiLiftMarker")
                    stubTip = P(net.GetProperty("m_BezierCurve"), "d");
            return true;
        }

        private static void Ei48Gate()
        {
            const string w = "eishain/EI-48";
            int rows = 0;
            foreach (LiftTypeDef def in SkiCatalog.Lifts)
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(def);
                if (c == null)
                    continue;
                foreach (StationRole role in new[] { StationRole.Base, StationRole.Top })
                {
                    StationDef st = SkiCatalog.StationOf(def.Index, role);
                    if (!Check(st != null, w, def.Id + " " + role + ": no catalog station"))
                        continue;
                    if (!StationMirror(st.Prefab, w, out float anchorY, out float3 tip, out int lotDepth))
                        continue;
                    rows++;
                    string who = st.Prefab + " (" + def.Id + " " + role + ")";
                    Check(math.isfinite(anchorY), w, who + ": no HV rope sub-lane in the mirror");
                    Check(math.abs(st.RopeAnchorLocalY - anchorY) <= 0.005f, w, who + ": RopeAnchorLocalY " + F(st.RopeAnchorLocalY) +
                          " but the mirror's lowest HV rope anchor is y " + F(anchorY));
                    Check(math.abs(st.FrontLocalZ - lotDepth * 4f) <= 0.005f, w, who + ": FrontLocalZ " + F(st.FrontLocalZ) +
                          " but the mirror's m_LotDepth " + lotDepth + " puts the lot front at z " + F(lotDepth * 4f));
                    Check(math.abs(tip.y) <= 0.005f && math.abs(tip.z - st.StubTipLocalZ) <= 0.005f && math.abs(tip.x - st.StubTipLocalX) <= 0.005f,
                          w, who + ": the SkiLiftMarker stub tip is (" + F(tip.x) + ", " + F(tip.y) + ", " + F(tip.z) + "), the row says (" +
                          F(st.StubTipLocalX) + ", 0, " + F(st.StubTipLocalZ) + ")");
                    float tower = c.RopeAboveBase(0);
                    Check(st.RopeAnchorLocalY < tower, w, who + ": the station anchor (" + F(st.RopeAnchorLocalY) +
                          ") is not below the shortest tower's rope (" + F(tower) + ")");
                    Check(math.abs(c.StationEndRope(st, 100f) - (100f + st.RopeAnchorLocalY)) <= 1e-3f, w,
                          who + ": a level ground end plans node + the station anchor");
                    s_Report.Add("EI-48 " + who + ": anchor y " + F(anchorY) + " (tower rope " + F(tower) + ", " + F(anchorY - tower) +
                                 " m), lot front z " + F(lotDepth * 4f) + ", " + F(LiftTowerPlanConstants.FrontDistM(st), "F0") +
                                 " m from the stub tip");
                }
            }
            Check(rows == 8, w, "8 catalog stations checked against the mirror; got " + rows);
        }

        // Tram 2 (Mod.log 1790-1846): ground (5448, -5687) -> ground (4835, -6201), 800 m, nodes y 1470.12 / 1608.99 (terrain 1469.99 /
        // 1608.86), ropes node + 26.25; plan 0 towers, lowest carrier +5.4 at 760 m (margin +0.4); the node chord clears the ground by
        // +52.5 / +70.2 / +64.1 / +17.2 m at 20 / 40 / 60 / 80 %; high side +2.2 / +1.7 at the ends. Once both stations stood the
        // clearance pass read -6.58 m at (4863, -6169), 42 m from the top end.
        internal static readonly float[] Tram2Center = {
            0f, 1469.99f, 160f, 1445.37f, 320f, 1455.47f, 480f, 1489.34f, 640f, 1564.02f, 700f, 1584.3f, 760f, 1604.6f, 800f, 1608.86f };
        internal static readonly float[] Tram2Side = { 0f, 2.2f, 800f, 1.7f };

        private static void Ei48Tram2()
        {
            const string w = "eishain/EI-48";
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("tram");
            LiftTerrainSampler t = Sampler(Tram2Center, Tram2Side, c.HalfWidthM);
            const float yA = 1470.12f, yB = 1608.99f;
            int tram = SkiCatalog.LiftById("tram").Index;
            StationDef sa = SkiCatalog.StationOf(tram, StationRole.Base), sb = SkiCatalog.StationOf(tram, StationRole.Top);
            float standA = yA + sa.RopeAnchorLocalY, standB = yB + sb.RopeAnchorLocalY;   // the stations as they stand (level)

            // the field: ground ends planned at the tower rope, full floor -> 0 towers, clear; violated once the stations stand
            var old = new LiftTowerPlanner { StationA = false, StationB = false };
            LiftTowerPlan field = old.Plan(t, 800f, yA + c.RopeAboveBase(0), yB + c.RopeAboveBase(0), c);
            LiftTowerPlan fieldStood = Stood(field, t, c, standA, standB);
            s_Report.Add("EI-48 tram 2 as planned in the field: " + Desc(field) + "; with the stations standing " + Desc(fieldStood));
            Check(field.Clear && field.LineTowers == 0 && !fieldStood.Clear, w,
                  "the reconstruction: the field plan (node + 26.25) is clear with no tower and violated once the stations stand; got " +
                  Desc(field) + " -> " + Desc(fieldStood));

            // the fix: the tool's ground-end rope and station ends; the plan holds with the stations standing
            float ropeA = LiftLineInput.GroundEndRope(c, sa, yA, float.NaN, out string srcA);
            float ropeB = LiftLineInput.GroundEndRope(c, sb, yB, float.NaN, out string srcB);
            var edit = new LiftLineEdit(c, new float3(0f, yA, 0f), new float3(0f, yB, 800f), ropeA, ropeB, true, true);
            LiftTowerPlan plan = edit.Replan((s, l) => t(s, l), (s, l) => t(800f - s, -l));
            LiftTowerPlan stood = Stood(plan, t, c, standA, standB);
            s_Report.Add("EI-48 tram 2 with station-anchor ends (" + srcA + " / " + srcB + "): " + Desc(plan) +
                         "; with the stations standing " + Desc(stood));
            Check(math.abs(ropeA - standA) < 1e-3f && math.abs(ropeB - standB) < 1e-3f, w,
                  "the ground ends plan with the stations' anchors; got " + F(ropeA - yA) + " / " + F(ropeB - yB) + " above the nodes");
            Check(stood.Clear, w, "planned at the stations' anchors, tram 2 stays clear once they stand; got " + Desc(stood));
        }

        /// <summary>The plan's own towers re-evaluated with the stations standing at both ends (their ropes, approach floor).</summary>
        private static LiftTowerPlan Stood(LiftTowerPlan plan, LiftTerrainSampler t, LiftTowerPlanConstants c, float ropeA, float ropeB)
        {
            var inner = new List<float>();
            var members = new List<int>();
            for (int i = 1; i < plan.Nodes.Length - 1; i++)
            {
                inner.Add(plan.Nodes[i].AlongM);
                members.Add(plan.Nodes[i].Member);
            }
            var pl = new LiftTowerPlanner { StationA = true, StationB = true };
            return pl.PlanFixed(t, plan.LengthM, ropeA, ropeB, c, inner, members, null, assign: false);
        }

        private static void Ei48Front()
        {
            const string w = "eishain/EI-48";
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("detachable");
            StationDef st = SkiCatalog.StationOf(SkiCatalog.LiftById("detachable").Index, StationRole.Base);
            Check(math.abs(c.StationEndRope(st, 100f, 90f) - 93.70f) < 1e-3f, w,
                  "ground falling 10 m to the lot front: the anchor over the front (93.70); got " + F(c.StationEndRope(st, 100f, 90f)));
            Check(math.abs(c.StationEndRope(st, 100f, 110f) - 103.70f) < 1e-3f, w,
                  "ground rising to the lot front: the level anchor (103.70); got " + F(c.StationEndRope(st, 100f, 110f)));
            Check(math.abs(c.StationEndRope(null, 100f) - (100f + c.RopeAboveBase(0))) < 1e-3f, w, "no station row: the tower rope");
        }

        // ------------------------------------------------------------------ EI-49

        // The 760 m DQ line of the pass (Mod.log 854-940): node terrain + the chord clearance samples at 20 / 40 / 60 / 80 % of each built
        // span (chord = node y line); the 801 m site shares its base and runs on 41 m down to its end at 1412.03 (plan 1420.29 - 8.26).
        internal static readonly float[] Dq760Center = {
            0f, 1321.47f, 45f, 1306.50f, 90f, 1291.48f, 135f, 1295.95f, 180f, 1301.13f, 225f, 1310.89f,
            271f, 1325.79f, 317f, 1342.28f, 363f, 1361.38f, 409f, 1383.47f, 455f, 1407.86f,
            486f, 1423.89f, 517f, 1439.12f, 548f, 1453.84f, 579f, 1468.37f, 610f, 1481.00f,
            616f, 1482.96f, 622f, 1484.52f, 628f, 1485.88f, 634f, 1486.84f, 640f, 1487.30f,
            652f, 1487.13f, 664f, 1485.25f, 676f, 1481.88f, 688f, 1477.40f, 700f, 1471.93f,
            712f, 1465.73f, 724f, 1458.83f, 736f, 1451.53f, 748f, 1444.03f, 760f, 1436.66f, 801f, 1412.03f };
        internal static readonly float[] Dq760Side = { 0f, 0.7f, 235f, 0.7f, 470f, 0.6f, 640f, 0.9f, 695f, 1.3f, 760f, 2.1f, 801f, 2.1f };

        internal static double TimePlan(LiftTowerPlanner pl, LiftTerrainSampler t, float len, float ropeA, float ropeB, LiftTowerPlanConstants c,
                                        out LiftTowerPlan plan, int reps = 3)
        {
            plan = null;
            double best = double.MaxValue;
            for (int r = 0; r < reps; r++)
            {
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                plan = pl.Plan(t, len, ropeA, ropeB, c);
                best = Math.Min(best, (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            }
            return best;
        }

        // EI-49 (cost report, no pass / fail: the planner cost lines before / after FIX-LIST 49; plan identity is gated by the whole
        // host): the 760 / 801 m sites with ground ends (the tool before FIX-LIST 48) and station ends; then the site stacked 1, 2 and 3
        // times up the mountain (760 / 1,520 / 2,280 m, +115 m each) with and without a 6 / 12 m bump 5 / 15 m from the top end (no
        // tower may stand there: the plan stays VIOLATED and runs the fallback), both end kinds; the slowest clear and VIOLATED plan per
        // length, best of 2. In game the cost is about 10x (the 760 m site: 40.9 ms logged, about 4 ms here).
        private static void Ei49()
        {
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("detachable");
            foreach (float len in new[] { 760f, 801f })
            {
                LiftTerrainSampler t = Sampler(Dq760Center, Dq760Side, c.HalfWidthM);
                float yA = 1321.53f, yB = Lerp(Dq760Center, len) + 0.26f;
                foreach (bool station in new[] { false, true })
                {
                    var pl = new LiftTowerPlanner { StationA = station, StationB = station, CheckEdgeSlope = true, EndNodeYA = yA, EndNodeYB = yB, Pairs = true };
                    float rA = station ? yA + 3.70f : yA + c.RopeAboveBase(0), rB = station ? yB + 3.70f : yB + c.RopeAboveBase(0);
                    double ms = TimePlan(pl, t, len, rA, rB, c, out LiftTowerPlan p);
                    s_Report.Add("EI-49 cost DQ " + F(len, "F0") + " m site, " + (station ? "station" : "ground") + " ends: " + F(ms, "F1") +
                                 " ms; " + F(p.MinFloorMarginM) + " " + Desc(p));
                }
            }
            for (int copies = 1; copies <= 3; copies++)
            {
                var cs = new List<float>();
                for (int k = 0; k < copies; k++)
                    for (int i = 0; i < Dq760Center.Length - 2; i += 2)
                        if (k == 0 || Dq760Center[i] > 0f)
                        {
                            cs.Add(Dq760Center[i] + 760f * k);
                            cs.Add(Dq760Center[i + 1] + 115.19f * k);
                        }
                float[] ctr = cs.ToArray();
                float len = 760f * copies;
                LiftTerrainSampler bt = Sampler(ctr, Dq760Side, c.HalfWidthM);
                double clearMs = 0, violMs = 0;
                string clearWhat = "-", violWhat = "-";
                foreach (float bumpH in new[] { 0f, 6f, 12f })
                    foreach (float bumpAt in new[] { 5f, 15f })
                    {
                        if (bumpH == 0f && bumpAt != 5f)
                            continue;
                        float h = bumpH, at = bumpAt, l0 = len;
                        LiftTerrainSampler t = (s, l) => bt(s, l) + h * math.saturate(1f - math.abs(l0 - s - at) / 8f);
                        float yA = t(0f, 0f) + 0.06f, yB = t(len, 0f) + 0.26f;
                        foreach (bool station in new[] { false, true })
                        {
                            var pl = new LiftTowerPlanner { StationA = station, StationB = station, CheckEdgeSlope = true, EndNodeYA = yA, EndNodeYB = yB, Pairs = true };
                            float rA = station ? yA + 3.70f : yA + c.RopeAboveBase(0), rB = station ? yB + 3.70f : yB + c.RopeAboveBase(0);
                            double ms = TimePlan(pl, t, len, rA, rB, c, out LiftTowerPlan p, 2);
                            string what = (h > 0f ? "bump " + F(h, "F0") + " m " + F(at, "F0") + " m from the top" : "no bump") + ", " +
                                          (station ? "station" : "ground") + " ends: " + F(ms, "F1") + " ms, " + p.LineTowers + " towers " + F(p.MinFloorMarginM);
                            if (p.Clear && ms > clearMs) { clearMs = ms; clearWhat = what; }
                            if (!p.Clear && ms > violMs) { violMs = ms; violWhat = what; }
                        }
                    }
                s_Report.Add("EI-49 cost DQ " + F(len, "F0") + " m stacked: slowest clear " + clearWhat + "; slowest VIOLATED " + violWhat);
            }
        }

        internal static string Desc(LiftTowerPlan p)
        {
            var parts = new List<string>();
            for (int i = 1; i < p.Nodes.Length - 1; i++)
                parts.Add(p.Constants.NameOf(p.Nodes[i].Member, p.Nodes[i].Head).Replace("SkiLiftTower", string.Empty) + "@" +
                          F(p.Nodes[i].AlongM, "F0") + " d" + F(p.Nodes[i].DeflectionDeg, "F1"));
            var spans = new List<string>();
            for (int i = 0; i < p.Spans.Length; i++)
                spans.Add(F(p.Spans[i].MinFloorMarginM, "F1"));
            p.KindCounts(out _, out _, out _, out _, out int over);
            return "[" + string.Join(", ", parts) + "] spans " + string.Join("/", spans) + " over " + over + (p.Clear ? " clear" : " VIOLATED");
        }
    }
}
