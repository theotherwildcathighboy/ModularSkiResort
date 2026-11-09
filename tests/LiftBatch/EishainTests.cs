// The Eishain-Niedertal pass defects (docs/research/field/2026-11-04/eishain, FIX-LIST 48-52). Where-tags `eishain/EI-n`.
// EI-52 a built line is never fitted into a worse plan (the after-load FG line of the pass, reconstructed from the dump's ground).
// EI-49 planner cost on the steep DQ sites (a report: the cost lines before / after FIX-LIST 49).
// EI-48 a ground end plans with the rope of the station that finishes it. (a) The gate: every catalog station's RopeAnchorLocalY is
//       the lowest HV rope sub-lane anchor of its mirror prefab, FrontLocalZ is its lot depth x 4, and its stub tip is at y 0 /
//       StubTipLocalZ. The anchor is below the shortest tower's rope (so no type plans a station end too high again). (b) Tram 2 of
//       the pass: the planner planned it clear (+0.4) with node + 26.25 at its ground ends. It read -6.58 once the stations stood.
//       Planned with the stations' anchors, it stays clear with them. (c) The lot-front rule: ground falling away beyond the end
//       lowers the rope.
// EI-55 a top ground end past a crest (FIX-LIST 55): a tower over its cap never reads clear (plan key, Describe, tower key). The
//       sunk ground end says SUNK_END. The test includes a sweep over the FB-3 lines and crest-end tops. EI-55 pad: with the ground-end
//       pad model (banks-pass 2026-11-09) the coming DQ stations cut their pads, both ends leave at loading height (no SUNK_END), and
//       the over-cap tower still reads PLAN_TRAIN. EI-48 also gates StationDef.PadBackLocalZ against the mirror.
// EI-54b a station snapped onto a built line's END node facing away from it is refused, with its stub tip or its stub's inner node on
//       the node (StationAlign.AttachesBackward / LineEnds, FIX-LIST 54(b)). An interior tower node is not an end.

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
            Ei55();
            Ei54b();
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
        // (ropes node + 7.27 = 1385.84 / 1548.26) and the fit kept it (+0.1). Then both stations were attached line-first with no
        // re-fit, and their lane anchors sit lower (1379.28 / 1545.95). After the load the fit read the ends as stations and the line
        // as it stands (elevations 0.35 / 4 / 4 / 4.5 / 4.5: FG8ComboA, FG12 x 2, FG12BreakA x 2) at -1.7 m. It wrote a Short plan
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

            // (a) in session: ground ends, the tool's fresh nodes (not built). The fit writes its plan. The field's Kept +0.1
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

            // (c) the rule's other sides: a sound plan is always written over a built line. The line as built is no worse than itself
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

        /// <summary>The lowest HV rope anchor of a station prefab (the lane's start, and also its end when the lane joins two nodes, as
        /// LiftLineToolSystem.AddAnchors / LaneSystem.FindAnchors), the SkiLiftMarker stub's outer point and the lot depth.</summary>
        private static bool StationMirror(string prefab, string w, out float anchorY, out float3 stubTip, out float3 stubInner,
                                          out int lotDepth)
        {
            anchorY = float.PositiveInfinity;
            stubTip = new float3(float.NaN);
            stubInner = new float3(float.NaN);
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
                {
                    stubTip = P(net.GetProperty("m_BezierCurve"), "d");
                    stubInner = P(net.GetProperty("m_BezierCurve"), "a");
                }
            return true;
        }

        /// <summary>The pad's back edge a station prefab cuts to (StationDef.PadBackLocalZ): no BuildingTerraformOverride = it levels its
        /// lot (the lot back edge), m_DontLower = it keeps the slope (NaN), else the lot back edge + m_SmoothMinOffset.y.</summary>
        private static float MirrorPadBack(string prefab, string w, int lotDepth)
        {
            JsonElement? root = MirrorJson(prefab, w);
            if (root == null)
                return float.NaN;
            foreach (JsonElement comp in root.Value.GetProperty("components").GetProperty("$rcontent").EnumerateArray())
                if (comp.TryGetProperty("m_DontLower", out JsonElement dl) && comp.TryGetProperty("m_SmoothMinOffset", out JsonElement sm))
                    return dl.GetBoolean() ? float.NaN : -lotDepth * 4f + sm.GetProperty("y").GetSingle();
            return -lotDepth * 4f;
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
                    if (!StationMirror(st.Prefab, w, out float anchorY, out float3 tip, out float3 inner, out int lotDepth))
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
                    // FIX-LIST 54(b): the refusal places the stub's inner node StubLengthM in from the tip (StationAlign.StubInner).
                    Check(math.abs(inner.y) <= 0.005f && math.abs(inner.x - st.StubTipLocalX) <= 0.005f &&
                          math.abs(inner.z - (st.StubTipLocalZ + StationAlign.StubLengthM)) <= 0.005f, w, who + ": the SkiLiftMarker stub's " +
                          "inner node is (" + F(inner.x) + ", " + F(inner.y) + ", " + F(inner.z) + "), StationAlign.StubInner says (" +
                          F(st.StubTipLocalX) + ", 0, " + F(st.StubTipLocalZ + StationAlign.StubLengthM) + ")");
                    // banks-pass 2026-11-09: the pad a ground end plans with is the prefab's cut window (LiftLineInput.PadGround).
                    float pad = MirrorPadBack(st.Prefab, w, lotDepth);
                    Check(math.isfinite(pad) == math.isfinite(st.PadBackLocalZ) && (!math.isfinite(pad) || math.abs(pad - st.PadBackLocalZ) <= 0.005f),
                          w, who + ": PadBackLocalZ " + F(st.PadBackLocalZ) + " but the mirror's BuildingTerraformOverride cuts to z " + F(pad) +
                          " (NaN = keeps the slope)");
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
        // 1608.86), ropes node + 26.25. Plan 0 towers, lowest carrier +5.4 at 760 m (margin +0.4). The node chord clears the ground by
        // +52.5 / +70.2 / +64.1 / +17.2 m at 20 / 40 / 60 / 80 %. High side +2.2 / +1.7 at the ends. Once both stations stood, the
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

            // the field: ground ends planned at the tower rope, full floor -> 0 towers, clear. Violated once the stations stand
            var old = new LiftTowerPlanner { StationA = false, StationB = false };
            LiftTowerPlan field = old.Plan(t, 800f, yA + c.RopeAboveBase(0), yB + c.RopeAboveBase(0), c);
            LiftTowerPlan fieldStood = Stood(field, t, c, standA, standB);
            s_Report.Add("EI-48 tram 2 as planned in the field: " + Desc(field) + "; with the stations standing " + Desc(fieldStood));
            Check(field.Clear && field.LineTowers == 0 && !fieldStood.Clear, w,
                  "the reconstruction: the field plan (node + 26.25) is clear with no tower and violated once the stations stand; got " +
                  Desc(field) + " -> " + Desc(fieldStood));

            // the fix: the tool's ground-end rope and station ends. The plan holds with the stations standing
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
        // span (chord = node y line). The 801 m site shares its base. It runs on 41 m down to its end at 1412.03 (plan 1420.29 - 8.26).
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

        // EI-49 (cost report, no pass / fail: the planner cost lines before / after FIX-LIST 49). The whole host gates plan identity.
        // Cases: the 760 / 801 m sites with ground ends (the tool before FIX-LIST 48) and station ends. Then the site stacked 1, 2 and 3
        // times up the mountain (760 / 1,520 / 2,280 m, +115 m each) with and without a 6 / 12 m bump 5 / 15 m from the top end (no
        // tower may stand there: the plan stays VIOLATED and runs the fallback), both end kinds. The report shows the slowest clear and
        // VIOLATED plan per length, best of 2. In game the cost is about 10x (the 760 m site: 40.9 ms logged, about 4 ms here).
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

        // EI-55: a top ground end just past a crest (the 2 km DQ of the eishain-fixes check, Mod.log 1693: top end rope 1801.33 =
        // node + -8.68, the station's anchor over its lot front 32 m out. DQ12 @1965, 8 m from the end, d +103.0 OVER CAP 32, NO
        // REPAIR. The plan line said "clear"). Here: a 1000 m DQ climbing 0.25, the crest 4 m before the top end node, the ground
        // falling 2 m to the node and on beyond it, and the lot front 12.38 m below the node (so the rope is node + -8.68 as logged). The
        // numbers are honest (vanilla stands the station on the lower front: FIX-LIST 48). What failed is that nothing said so. On
        // 4e0e401 this plan read PLAN_CLEAR, Describe "...; clear", and no SUNK notice (nothing ever checked ground ends).
        private const float Ei55Len = 1000f, Ei55Grade = 0.25f, Ei55CrestBack = 4f, Ei55Drop = 2f, Ei55FrontBelowNode = 12.38f;

        private static float Ei55Ground(float x)
        {
            float crest = Ei55Len - Ei55CrestBack;
            return x <= crest ? 1200f + Ei55Grade * x : 1200f + Ei55Grade * crest - Ei55Drop * (x - crest) / Ei55CrestBack;
        }

        private static void Ei55()
        {
            const string w = "eishain/EI-55";
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("detachable");
            int dq = SkiCatalog.LiftById("detachable").Index;
            StationDef bas = SkiCatalog.StationOf(dq, StationRole.Base), top = SkiCatalog.StationOf(dq, StationRole.Top);
            float yA = Ei55Ground(0f), yB = Ei55Ground(Ei55Len);
            float ropeA = LiftLineInput.GroundEndRope(c, bas, yA, yA - Ei55Grade * LiftTowerPlanConstants.FrontDistM(bas), out _);
            float ropeB = LiftLineInput.GroundEndRope(c, top, yB, yB - Ei55FrontBelowNode, out string srcB);
            Check(math.abs(ropeB - yB + 8.68f) < 0.01f, w + " rope", "the top end plans node + " + F(ropeB - yB) + ", the log had -8.68: " + srcB);
            var e = new LiftLineEdit(c, new float3(0f, yA, 0f), new float3(Ei55Len, yB, 0f), ropeA, ropeB, true, true);
            LiftTowerPlan p = e.Replan(Ei55Sampler(e, false, c), Ei55Sampler(e, true, c));
            int over = p.OverCapTowers, last = p.Nodes.Length - 2;
            // The field shape: every span clear, the last tower right by the end and past its cap, no repair found.
            bool shape = p.Clear && over >= 1 && last >= 1 && p.IsOverCap(last) && Ei55Len - p.Nodes[last].AlongM <= 15f &&
                         (p.Nodes[last].Flags & LiftPlanNodeFlags.OverCap) != 0;
            Check(shape, w + " shape", "not the field shape (spans clear, the last tower within 15 m of the end over its cap, flagged): " + Desc(p));
            string key = LiftLineInput.PlanKey(p, out _);
            Check(key == "PLAN_TRAIN", w + " plan key", "a tower over its cap reads " + key + ": " + Desc(p));
            string line = e.Describe();
            Check(!line.EndsWith("; clear", StringComparison.Ordinal) && line.Contains("OVER CAP"), w + " describe", "the plan line: " + line);
            Check(!p.Sound && p.Verdict.Contains("OVER CAP"), w + " verdict", p.Verdict);
            string tower = "none";
            float bend = 0f, cap = 0f;
            if (last >= 1)
                tower = LiftLineInput.TowerKey(p, last, false, out _, out _, out bend, out cap);
            Check(tower == "TOWER_TRAIN" && bend > cap, w + " tower key", "the over-cap tower reads " + tower + " (" + F(bend, "F1") + " / cap " + F(cap, "F0") + "): " + Desc(p));
            // The end is sunk: the coming station's carriers start below the ground ahead. A ground end says SUNK_END, a built station
            // SUNK, a loose end (a tower node) nothing.
            string sunk = LiftLineInput.SunkKey(p, LiftLineEndKind.Ground, LiftLineEndKind.Ground);
            Check(p.EndCarrierB < -LiftLineInput.SunkWarnM && sunk == "SUNK_END", w + " sunk", "end carrier B " + F(p.EndCarrierB) + ", key " + (sunk ?? "none"));
            Check(LiftLineInput.SunkKey(p, LiftLineEndKind.Ground, LiftLineEndKind.Station) == "SUNK", w + " sunk station", "a built station end is not SUNK");
            Check(!LiftLineInput.SunkEnd(p.StationB, LiftLineEndKind.LooseEnd, p.EndCarrierB), w + " sunk loose", "a loose end reads sunk");
            s_Report.Add("EI-55 crest-end DQ " + F(Ei55Len, "F0") + " m | " + srcB + " | " + line + " | key " + key + ", last tower " + tower + " d " +
                         F(bend, "F1") + ", end carrier B " + F(p.EndCarrierB, "F1") + " m -> " + sunk);
            Ei55Pad(c, bas, top, ropeA, ropeB, yA, yB);
            Ei55Sweep();
        }

        // EI-55 pad (banks-pass 2026-11-09): the same line with the ground-end pad model (LiftLineInput.PadSampler). Both ends are ground
        // ends. The DQ base will cut its pad and 12 m runway at its lot-front plane (8 m below the node on this 0.25 slope), the DQ top
        // its lot-sized pad 12.38 m below the node (the crest end).
        private static void Ei55Pad(LiftTowerPlanConstants c, StationDef bas, StationDef top, float ropeA, float ropeB, float yA, float yB)
        {
            const string w = "eishain/EI-55 pad";
            float planeA = LiftLineInput.GroundEndStationY(yA, yA - Ei55Grade * LiftTowerPlanConstants.FrontDistM(bas));
            float planeB = LiftLineInput.GroundEndStationY(yB, yB - Ei55FrontBelowNode);
            var e = new LiftLineEdit(c, new float3(0f, yA, 0f), new float3(Ei55Len, yB, 0f), ropeA, ropeB, true, true);
            LiftTowerPlan p = e.Replan(LiftLineInput.PadSampler(Ei55Sampler(e, false, c), Ei55Len, false, bas, planeA, top, planeB),
                                       LiftLineInput.PadSampler(Ei55Sampler(e, true, c), Ei55Len, true, bas, planeA, top, planeB));
            string key = LiftLineInput.PlanKey(p, out _);
            string sunk = LiftLineInput.SunkKey(p, LiftLineEndKind.Ground, LiftLineEndKind.Ground);
            // The pads take the ground above both planes away: the carriers leave both ends at loading height (anchor - drop = +0.40), so
            // no SUNK_END. Natural ground read -7.9 at the base end and -12.3 at the top end.
            Check(math.abs(p.EndCarrierA - 0.4f) < 0.05f && math.abs(p.EndCarrierB - 0.4f) < 0.05f && sunk == null, w + " ends",
                  "end carriers " + F(p.EndCarrierA) + " / " + F(p.EndCarrierB) + ", key " + (sunk ?? "none"));
            // The crest still bends the last tower past its cap: the pad must not hide that.
            Check(key == "PLAN_TRAIN" && p.OverCapTowers >= 1, w + " over cap kept", "key " + key + ": " + Desc(p));
            // PadGround per catalog row: bases hold 12 m past the stub tip, tops and the tram base 0, the FG top and tram top keep the slope.
            foreach (LiftTypeDef def in SkiCatalog.Lifts)
                foreach (StationRole role in new[] { StationRole.Base, StationRole.Top })
                {
                    StationDef st = SkiCatalog.StationOf(def.Index, role);
                    float reach = LiftLineInput.PadReachM(st);
                    bool keeps = (def.Id == "fixedGrip" || def.Id == "tram") && role == StationRole.Top;
                    float want = keeps ? float.NaN : role == StationRole.Base && def.Id != "tram" ? 12f : 0f;
                    Check(keeps ? !math.isfinite(reach) : math.abs(reach - want) < 1e-3f, w + " reach", st.Prefab + ": reach " + F(reach) + ", want " + F(want));
                }
            Check(LiftLineInput.PadGround(110f, 0f, bas, 100f) == 100f && LiftLineInput.PadGround(110f, 12f, bas, 100f) == 100f &&
                  math.abs(LiftLineInput.PadGround(110f, 16f, bas, 100f) - 105f) < 1e-3f && LiftLineInput.PadGround(110f, 20f, bas, 100f) == 110f &&
                  LiftLineInput.PadGround(95f, 0f, bas, 100f) == 95f, w + " ground", "pad, runway edge, apron half, apron end, never fills");
            Check(LiftLineInput.PadGround(110f, 0f, SkiCatalog.StationOf(SkiCatalog.LiftById("fixedGrip").Index, StationRole.Top), 100f) == 110f,
                  w + " keeps slope", "the FG top keeps the slope: its ground end reads the raw ground");
            s_Report.Add("EI-55 pad: plane A node " + F(planeA - yA) + ", B node " + F(planeB - yB) + " | " + e.Describe() + " | key " + key +
                         " | end carrier A " + F(p.EndCarrierA, "F1") + " / B " + F(p.EndCarrierB, "F1") + " -> " + (sunk ?? "none"));
        }

        private static LiftTerrainSampler Ei55Sampler(LiftLineEdit e, bool fromB, LiftTowerPlanConstants c) => (s, l) =>
        {
            float3 q = fromB ? e.PointFromB(s, l) : e.PointAt(s, l);
            return Ei55Ground(q.x) + 0.3f * l / c.HalfWidthM;
        };

        // EI-55 sweep: no plan anywhere reads clear with a tower over its cap. The sweep covers the FB-3 lines (DR-1, D192, crests and dips,
        // field tents) with and without the twins (without: more towers stay over a cap), and crest-end DQ / FG / GD tops like the field
        // case. For each plan: PlanKey is not PLAN_CLEAR, Describe and Verdict do not say "clear", and every over-cap tower reads
        // TOWER_TRAIN or TOWER_LOW. The test counts the over-cap plans, to show that the sweep binds.
        private static void Ei55Sweep()
        {
            const string w = "eishain/EI-55 sweep";
            int plans = 0, overPlans = 0, clearKeys = 0;
            var lines = new List<(string Label, LiftLineEdit Edit, LiftTowerPlan Plan)>();
            foreach (FallbackTests.SweepLine l in FallbackTests.SweepLines())
                foreach (bool twins in new[] { true, false })
                {
                    LiftTowerPlanConstants c = twins ? LiftTowerPlanConstants.For(l.Type) : BreakoverTests.WithoutTwins(l.Type);
                    float ya = l.H(0f, 0f), yb = l.H(l.L, 0f);
                    var e = new LiftLineEdit(c, new float3(0f, ya, 0f), new float3(l.L, yb, 0f), ya + c.RopeAboveBase(0) + l.AboveA,
                                             yb + c.RopeAboveBase(0) + l.AboveB, l.Stations, l.Stations);
                    Func<float, float, float> h = l.H;
                    LiftTowerPlan p = e.Replan((s, z) => { float3 q = e.PointAt(s, z); return h(q.x, q.z); },
                                               (s, z) => { float3 q = e.PointFromB(s, z); return h(q.x, q.z); });
                    lines.Add((l.Label + (twins ? "" : " no twins"), e, p));
                }
            foreach (string id in new[] { "fixedGrip", "detachable", "gondola" })
            foreach (float len in new[] { 400f, 600f, 1000f })
            foreach (float g in new[] { 0.20f, 0.30f })
            foreach (float back in new[] { 4f, 8f, 12f, 20f })
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(id);
                int k = SkiCatalog.LiftById(id).Index;
                StationDef bas = SkiCatalog.StationOf(k, StationRole.Base), top = SkiCatalog.StationOf(k, StationRole.Top);
                float crest = len - back;
                Func<float, float> h = x => x <= crest ? 1200f + g * x : 1200f + g * crest - 2f * (x - crest) / back;
                float ya = h(0f), yb = h(len);
                float ra = LiftLineInput.GroundEndRope(c, bas, ya, ya - g * LiftTowerPlanConstants.FrontDistM(bas), out _);
                float rb = LiftLineInput.GroundEndRope(c, top, yb, h(len + LiftTowerPlanConstants.FrontDistM(top)), out _);
                var e = new LiftLineEdit(c, new float3(0f, ya, 0f), new float3(len, yb, 0f), ra, rb, true, true);
                LiftTowerPlan p = e.Replan((s, z) => { float3 q = e.PointAt(s, z); return h(q.x) + 0.3f * z / c.HalfWidthM; },
                                           (s, z) => { float3 q = e.PointFromB(s, z); return h(q.x) + 0.3f * z / c.HalfWidthM; });
                lines.Add(("crest-end " + id + " " + F(len, "F0") + " g " + F(g) + " back " + F(back, "F0"), e, p));
            }
            foreach (var (label, e, p) in lines)
            {
                plans++;
                string key = LiftLineInput.PlanKey(p, out _);
                if (key == "PLAN_CLEAR") clearKeys++;
                Check(key != "PLAN_CLEAR" || p.Sound, w + " " + label, "PLAN_CLEAR on a plan that is not sound: " + Desc(p));
                if (p.OverCapTowers == 0)
                    continue;
                overPlans++;
                string line = e.Describe();
                Check(!line.EndsWith("; clear", StringComparison.Ordinal) && p.Verdict != "clear", w + " " + label, "reads clear with a tower over its cap: " + line);
                for (int i = 1; i < p.Nodes.Length - 1; i++)
                {
                    if (!p.IsOverCap(i))
                        continue;
                    string tk = LiftLineInput.TowerKey(p, i, false, out _, out _, out float bend, out float cap);
                    Check((tk == "TOWER_TRAIN" && bend > cap) || tk == "TOWER_LOW", w + " " + label + " tower " + i, "an over-cap tower reads " + tk);
                }
            }
            Check(overPlans > 0, w, "no plan had a tower over its cap: the sweep does not bind");
            s_Report.Add("EI-55 sweep " + plans + " plans (FB-3 lines with / without twins, crest-end tops) | " + overPlans +
                         " with a tower over its cap, none reads clear; PLAN_CLEAR on " + clearKeys + ", all sound");
        }

        // EI-54b: the rule refuses a station snapped onto a built line's END node facing away from it (StationFacingRefusalSystem's pure rule,
        // StationAlign.AttachesBackward over StationAlign.LineEnds). Field (in-game check 2026-11-04, plan-fixes-2 RESULTS): the steep tram
        // base end node 842295:1 at (5725.6, -5375.9), the line leaving it at bearing 128.7 toward the top. Vanilla offers two reversed
        // snaps there. One puts the stub's INNER node on the end node (station (5735.1, -5378.4) yaw 128.7). It PLACED and attached on
        // 7e52477, whose rule tested the tip only. The other puts the tip on the node ((5737.5, -5380.2) yaw 128.7). It gets vanilla's own
        // "Invalid shape". Aligned: (5713.8, -5371.5) yaw -51.3, the tip on the node, must pass. The FG top preview on line 1's top end
        // 239190:9 (3550, -4000), the line's previous node 85 m back toward the base: (3541.6, -4003.2) yaw -111.6, inner node merged,
        // "Place" on 7e52477. Then every catalog station at every 10 deg of yaw, each stub node on the end. The rule allows a line leaving
        // within 80 deg of the stub axis and refuses one leaving 100 deg or more off it. The rule never refuses a stub node 5 m from the
        // end. And LineEnds: an interior tower node (two line edges) is not an end, so the rule does not judge a reversed station on it.
        private static void Ei54b()
        {
            const string w = "eishain/EI-54b";
            StationDef tram = SkiCatalog.StationOf(SkiCatalog.LiftById("tram").Index, StationRole.Base);
            StationDef fgTop = SkiCatalog.StationOf(0, StationRole.Top);   // the fixed-grip row (CatalogTests: index 0)
            float2 end = new float2(5725.6f, -5375.9f);
            float2 next = end + Bearing(128.7f) * 250f;   // toward the tram top
            StationAlign.StubNode innerField = Snap(tram, new float2(5735.1f, -5378.4f), 128.7f, end, next);
            StationAlign.StubNode tipField = Snap(tram, new float2(5737.5f, -5380.2f), 128.7f, end, next);
            StationAlign.StubNode aligned = Snap(tram, new float2(5713.8f, -5371.5f), -51.3f, end, next);
            float2 fgEnd = new float2(3550f, -4000f), fgNext = new float2(3465f, -4000f);
            StationAlign.StubNode fgField = Snap(fgTop, new float2(3541.6f, -4003.2f), -111.6f, fgEnd, fgNext);
            Check(innerField == StationAlign.StubNode.Inner, w + " field", "the reversed steep tram base with its stub's INNER node on the end " +
                  "(the snap that placed on 7e52477): " + innerField + ", want Inner (refused)");
            Check(tipField == StationAlign.StubNode.Tip, w + " field", "the reversed steep tram base with its stub tip on the end: " + tipField +
                  ", want Tip (refused)");
            Check(aligned == StationAlign.StubNode.None, w + " field", "the aligned tram base (yaw -51.3, tip on the end): " + aligned +
                  ", want None (allowed)");
            Check(fgField == StationAlign.StubNode.Inner, w + " field", "the reversed FG top with its inner node on line 1's top end: " + fgField +
                  ", want Inner (refused)");
            // 7e52477's rule (tip within 1 m, the next node behind the tip) on the same snap: it lets the inner-node snap through.
            bool oldInner = OldRule(tram, new float2(5735.1f, -5378.4f), 128.7f, end, next);
            Check(!oldInner, w + " field", "7e52477's tip-only rule refuses the inner-node snap: the model no longer shows the field miss");

            // The aligned station with its inner node on the end and the line leaving ahead (not a facing case) is not refused either.
            float2 axisA = StationAlign.Axis(quaternion.RotateY(math.radians(-51.3f)));
            StationAlign.StubNode alignedInner = Snap(tram, new float2(5713.8f, -5371.5f) + axisA * StationAlign.StubLengthM, -51.3f, end, next);
            Check(alignedInner == StationAlign.StubNode.None, w + " field", "the aligned tram base with its inner node on the end: " + alignedInner +
                  ", want None");

            // LineEnds: a line base end 0 - tower 1 - tower 2 - top end 3, a lone span 10-11, a degenerate 5-5.
            var edges = new List<KeyValuePair<int, int>>
            {
                new KeyValuePair<int, int>(0, 1), new KeyValuePair<int, int>(1, 2), new KeyValuePair<int, int>(2, 3),
                new KeyValuePair<int, int>(10, 11), new KeyValuePair<int, int>(5, 5),
            };
            Dictionary<int, int> ends = StationAlign.LineEnds(edges);
            bool endsOk = ends.Count == 4 && ends.TryGetValue(0, out int n0) && n0 == 1 && ends.TryGetValue(3, out int n3) && n3 == 2 &&
                          !ends.ContainsKey(1) && !ends.ContainsKey(2) && !ends.ContainsKey(5) && ends.ContainsKey(10) && ends.ContainsKey(11);
            Check(endsOk, w + " ends", "LineEnds of 0-1-2-3 + 10-11 + a degenerate 5-5: got {" + string.Join(", ", ends) + "}");
            // The RESULTS DQ top case (7e52477 refused it as a "line end"): the tip on an interior tower node with the next span behind.
            // Judged alone, the geometry refuses it. It is not an end, so the system never passes it to AttachesBackward.
            Check(!ends.ContainsKey(1) && !ends.ContainsKey(2), w + " ends", "an interior tower node counts as a line end");

            int stations = 0, allowed = 0, refused = 0, wrong = 0;
            var pos = new float2(4330f, -5300f);
            foreach (LiftTypeDef type in SkiCatalog.Lifts)
            foreach (StationRole role in new[] { StationRole.Base, StationRole.Top })
            {
                StationDef def = SkiCatalog.StationOf(type.Index, role);
                if (def == null)
                    continue;
                stations++;
                for (float yaw = -180f; yaw < 180f; yaw += 10f)
                {
                    quaternion rot = quaternion.RotateY(math.radians(yaw));
                    float2 axis = StationAlign.Axis(rot);
                    float3 p3 = new float3(pos.x, 0f, pos.y);
                    foreach (bool onInner in new[] { false, true })
                    {
                        float2 node = onInner ? StationAlign.StubInner(p3, rot, def.StubTipLocalZ, def.StubTipLocalX).xz
                                              : StationAlign.StubTip(p3, rot, def.StubTipLocalZ, def.StubTipLocalX).xz;
                        foreach (float off in new[] { -80f, -30f, 0f, 30f, 80f, 100f, 150f, 180f, 210f, 260f })
                        {
                            float a = math.radians(off);
                            float2 dir = new float2(axis.x * math.cos(a) - axis.y * math.sin(a), axis.x * math.sin(a) + axis.y * math.cos(a));
                            StationAlign.StubNode want = math.abs(off) > 90f
                                ? (onInner ? StationAlign.StubNode.Inner : StationAlign.StubNode.Tip) : StationAlign.StubNode.None;
                            StationAlign.StubNode got = Snap(def, pos, yaw, node, node + dir * 250f);
                            if (got != StationAlign.StubNode.None) refused++; else allowed++;
                            if (got != want && wrong++ < 4)
                                Check(false, w + " sweep", def.Prefab + " yaw " + F(yaw, "F0") + ", " + (onInner ? "inner node" : "tip") +
                                      " on the end, line " + F(off, "F0") + " deg off the stub: " + got + ", want " + want);
                            float2 aside = node + new float2(-axis.y, axis.x) * 5f;   // 5 m to the side: neither stub node within 1 m
                            if (Snap(def, pos, yaw, aside, aside + dir * 250f) != StationAlign.StubNode.None && wrong++ < 4)
                                Check(false, w + " sweep", def.Prefab + " refused 5 m from the line end");
                        }
                    }
                }
            }
            Check(wrong == 0, w + " sweep", wrong + " wrong verdict(s)");
            s_Report.Add("EI-54b reversed snap | steep tram base yaw 128.7: inner node on the end " + innerField + ", tip on the end " + tipField +
                         " (7e52477's tip-only rule on the inner snap: refused " + oldInner + "); aligned yaw -51.3 " + aligned + ", aligned inner " +
                         alignedInner + "; FG top yaw -111.6 " + fgField + " | LineEnds: interior tower node an end " + ends.ContainsKey(2) +
                         " | sweep " + stations + " stations x 36 yaws x 2 stub nodes x 10 line bearings: " + allowed + " allowed, " + refused +
                         " refused, " + wrong + " wrong");
        }

        private static float2 Bearing(float deg) => new float2(math.sin(math.radians(deg)), math.cos(math.radians(deg)));

        /// <summary>The refusal rule for a station at <paramref name="pos"/> (xz) / <paramref name="yawDeg"/> and a true line end at
        /// <paramref name="endXz"/> whose line's next node is <paramref name="nextXz"/> (StationFacingRefusalSystem's call).</summary>
        private static StationAlign.StubNode Snap(StationDef def, float2 pos, float yawDeg, float2 endXz, float2 nextXz)
        {
            quaternion rot = quaternion.RotateY(math.radians(yawDeg));
            float3 p = new float3(pos.x, 0f, pos.y);
            float2 axis = StationAlign.Axis(rot);
            float2 tip = StationAlign.StubTip(p, rot, def.StubTipLocalZ, def.StubTipLocalX).xz;
            float2 inner = StationAlign.StubInner(p, rot, def.StubTipLocalZ, def.StubTipLocalX).xz;
            return StationAlign.AttachesBackward(axis, tip, inner, endXz, nextXz);
        }

        /// <summary>7e52477's rule, kept in the test to show the field miss: the stub tip within 1 m of the end and the next node behind
        /// the tip.</summary>
        private static bool OldRule(StationDef def, float2 pos, float yawDeg, float2 endXz, float2 nextXz)
        {
            quaternion rot = quaternion.RotateY(math.radians(yawDeg));
            float2 axis = StationAlign.Axis(rot);
            float2 tip = StationAlign.StubTip(new float3(pos.x, 0f, pos.y), rot, def.StubTipLocalZ, def.StubTipLocalX).xz;
            return math.distance(tip, endXz) <= StationAlign.AttachTolM && StationAlign.LineLeavesBackward(axis, tip, nextXz);
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
            return "[" + string.Join(", ", parts) + "] spans " + string.Join("/", spans) + " over " + over + " " + p.Verdict;
        }
    }
}
