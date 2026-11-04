// The Eishain-Niedertal pass defects (docs/research/field/2026-11-04/eishain, FIX-LIST 48-52). Where-tags `eishain/EI-n`.
// EI-52 a built line is never fitted into a worse plan (the after-load FG line of the pass, reconstructed from the dump's ground).

using System;
using System.Collections.Generic;
using System.Globalization;
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
