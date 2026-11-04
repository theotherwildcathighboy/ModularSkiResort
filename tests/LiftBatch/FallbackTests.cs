// The planner's fallback when no clear plan exists, and the twins never making a plan worse (planner-fallback-2026-11-04). Where-tags
// `fallback/FB-n`.
// Field case (bridge run 2026-11-04, B2 import): a plan-only FG line at x 1250 (z 2100-2550, ground ends) planned 16 towers, 13 of them
// 10 m apart, VIOLATED. Cause: ground inside a ground end's tower-free zone (NearStation: StationClearM at both ends, also at a ground
// end) that the end's fixed rope cannot clear; the exact search and Anchors find nothing, and the legacy greedy Split scattered towers
// along the whole segment. Fix: when the legacy segment is not clear either, the exact search runs once more with the shortfall as its
// first cost (SoftInsteadOfLegacy): the fewest towers with the least shortfall, every edge-slope / max-height / spacing rule kept.
// FB-1 the field line reconstructed from the two logged plans (approximate: the terrain is not logged) plus a 6 m bump 5 m from end B:
//      the fewest towers, only the end span short, with and without twins; the clean line: 5 towers clear, as B1 planned it.
// FB-2 the twins change only the angle pass and the heads: Pairs off, the plan with twins is the plan without on every sweep line
//      (nodes; member and rope of every line-head tower; a twin head may take the nearest member with that twin, HD-3).
// FB-3 the sweep gate, twins vs WithoutTwins on the same line: never VIOLATED where the line without twins is clear, and the tower
//      count rises at most by the extra pairs the Break cap makes (one partner per pair). Lines: the DR-1 shapes, the 192-line
//      direction sweep (four types, on / off the grid, grades 0.15 / 0.35, a hump or none, stations or ground ends), the FG / DQ / GD
//      crests and dips, and the field line with bumps near either end.

using System;
using System.Collections.Generic;
using System.Globalization;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    public static class FallbackTests
    {
        private static Func<bool, string, string, bool> s_Check;
        private static readonly List<string> s_Report = new List<string>();

        public static IReadOnlyList<string> Report => s_Report;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            s_Report.Clear();
            Fb1FieldLine();
            Fb2Fb3Sweep();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);
        private static string F(double v, string f = "F2") => v.ToString(f, CultureInfo.InvariantCulture);

        // ── the field line ──────────────────────────────────────────────────────────────────────────────────────────────────
        /// <summary>The x 1250 FG line's ground (m along, y), reconstructed from B1's 5-tower and B2's 16-tower plans (rope heights,
        /// members and deflections solved back to the ground under each tower).</summary>
        private static readonly float[] FieldProfile = {
            0f, 751.21f, 15f, 761.03f, 35f, 772.79f, 45f, 771.47f, 55f, 768.84f, 65f, 764.65f, 75f, 762.22f, 85f, 760.49f, 95f, 761.53f,
            105f, 763.82f, 115f, 764.26f, 125f, 765.18f, 135f, 766.63f, 210f, 780.69f, 225f, 787.52f, 300f, 836.78f, 315f, 845.50f,
            330f, 854.85f, 405f, 887.65f, 410f, 889.27f, 435f, 885.29f, 450f, 885.70f };

        private const float FieldLength = 450f;

        private static float Field(float s)
        {
            float[] sy = FieldProfile;
            if (s <= sy[0]) return sy[1];
            for (int i = 2; i < sy.Length; i += 2)
                if (s <= sy[i])
                    return math.lerp(sy[i - 1], sy[i + 1], (s - sy[i - 2]) / math.max(sy[i] - sy[i - 2], 1e-6f));
            return sy[sy.Length - 1];
        }

        /// <summary>A tent of height <paramref name="h"/> and half-width 5 m at <paramref name="x"/> m along.</summary>
        private static Func<float, float, float> FieldWithBump(float x, float h) =>
            (s, z) => Field(s) + h * math.max(0f, 1f - math.abs(s - x) / 5f);

        /// <summary>A straight world line along +x from 0 to <paramref name="len"/> over <paramref name="h"/>(x, z), planned the way the
        /// lift tool plans it (LiftLineEdit.Replan with the ground from A and from B); ground ends rope = ground + RopeAboveBase(0).</summary>
        private static LiftTowerPlan Tool(LiftTowerPlanConstants c, Func<float, float, float> h, float len, bool stations, float aboveA = 0f, float aboveB = 0f)
        {
            float ya = h(0f, 0f), yb = h(len, 0f);
            var e = new LiftLineEdit(c, new float3(0f, ya, 0f), new float3(len, yb, 0f), ya + c.RopeAboveBase(0) + aboveA,
                                     yb + c.RopeAboveBase(0) + aboveB, stations, stations);
            LiftTerrainSampler fa = (s, l) => { float3 p = e.PointAt(s, l); return h(p.x, p.z); };
            LiftTerrainSampler fb = (s, l) => { float3 p = e.PointFromB(s, l); return h(p.x, p.z); };
            return e.Replan(fa, fb);
        }

        /// <summary>The tool's rules with Pairs off (phases a-c and the heads only).</summary>
        private static LiftTowerPlan NoPairs(LiftTowerPlanConstants c, Func<float, float, float> h, float len, bool stations, float aboveA = 0f, float aboveB = 0f)
        {
            float ya = h(0f, 0f), yb = h(len, 0f);
            var pl = new LiftTowerPlanner { CheckEdgeSlope = true, EndNodeYA = ya, EndNodeYB = yb, Pairs = false, StationA = stations, StationB = stations };
            return pl.Plan((s, l) => h(s, -l), len, ya + c.RopeAboveBase(0) + aboveA, yb + c.RopeAboveBase(0) + aboveB, c);
        }

        private static int PairNodes(LiftTowerPlan p)
        {
            int n = 0;
            foreach (LiftPlanNode x in p.Nodes)
                if ((x.Flags & LiftPlanNodeFlags.Pair) != 0)
                    n++;
            return n;
        }

        /// <summary>Phases a-c alike: the same nodes, and the same member and rope on every tower that keeps the line head (a twin head
        /// may move its tower to the nearest member that has the twin, re-roped and floor-checked, AssignHeads / HD-3).</summary>
        private static bool SameNodes(LiftTowerPlan withTwins, LiftTowerPlan without)
        {
            LiftPlanNode[] a = withTwins.Nodes, b = without.Nodes;
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i].AlongM != b[i].AlongM || (a[i].Head == LiftTowerHead.Line && (a[i].Member != b[i].Member || a[i].RopeM != b[i].RopeM)))
                    return false;
            return true;
        }

        private static float MinGap(LiftTowerPlan p)
        {
            float g = float.MaxValue;
            for (int i = 2; i < p.Nodes.Length - 1; i++)
                g = math.min(g, p.Nodes[i].AlongM - p.Nodes[i - 1].AlongM);
            return g;
        }

        private static string Describe(LiftTowerPlan p)
        {
            var parts = new List<string>();
            for (int i = 1; i < p.Nodes.Length - 1; i++)
                parts.Add(p.Constants.NameOf(p.Nodes[i].Member, p.Nodes[i].Head).Replace("SkiLiftTower", string.Empty) + "@" +
                          F(p.Nodes[i].AlongM, "F0") + ((p.Nodes[i].Flags & LiftPlanNodeFlags.Pair) != 0 ? "*" : string.Empty));
            return p.LineTowers + " towers (" + string.Join(", ", parts) + "), " + (p.Clear ? "clear" : "VIOLATED") + ", min margin " +
                   F(p.MinFloorMarginM);
        }

        // ── FB-1: the field line ────────────────────────────────────────────────────────────────────────────────────────────
        private static void Fb1FieldLine()
        {
            const string W = "fallback/FB-1";
            LiftTowerPlanConstants tw = LiftTowerPlanConstants.For("fixedGrip"), no = BreakoverTests.WithoutTwins("fixedGrip");

            // The clean reconstruction: B1 planned the field line as 5 towers, clear.
            Func<float, float, float> clean = FieldWithBump(445f, 0f);
            LiftTowerPlan c0 = Tool(no, clean, FieldLength, false), c1 = Tool(tw, clean, FieldLength, false);
            Check(c0.Clear && c0.LineTowers == 5, W + " clean no twins", Describe(c0));
            Check(c1.Clear && c1.LineTowers <= c0.LineTowers + math.max(0, (PairNodes(c1) - PairNodes(c0)) / 2), W + " clean twins", Describe(c1));
            s_Report.Add("FB-1 clean   no twins " + Describe(c0));
            s_Report.Add("FB-1 clean   twins    " + Describe(c1));

            // A 6 m bump 5 m from end B, inside B's tower-free zone (12.2 m): no rope from B's fixed height clears it, so no clear plan
            // exists. HEAD before the fix: 16 / 17 towers 10 m apart (the legacy Split), margin -0.39 all the same.
            Func<float, float, float> bump = FieldWithBump(445f, 6f);
            foreach (bool twins in new[] { false, true })
            {
                LiftTowerPlan p = Tool(twins ? tw : no, bump, FieldLength, false);
                string tag = twins ? "twins" : "no twins";
                s_Report.Add("FB-1 bump    " + (twins ? "twins    " : "no twins ") + Describe(p));
                Check(!p.Clear, W + " bump " + tag + " flagged", "the 6 m bump at 445 m cannot clear: " + Describe(p));
                Check(p.LineTowers <= 6, W + " bump " + tag + " fewest towers", "at most one over the clean plan's 5: " + Describe(p));
                Check(PairNodes(p) == 0 ? MinGap(p) >= 20f : MinGap(p) >= 10f, W + " bump " + tag + " no scatter",
                      "min gap " + F(MinGap(p), "F1") + " m: " + Describe(p));
                int bad = 0, badAt = -1;
                for (int i = 0; i < p.Spans.Length; i++)
                    if (p.Spans[i].FloorViolated || p.Spans[i].HeightViolated || p.Spans[i].SlopeViolated)
                    {
                        bad++;
                        badAt = i;
                    }
                Check(bad == 1 && badAt == p.Spans.Length - 1 && !p.Spans[badAt].SlopeViolated && !p.Spans[badAt].HeightViolated,
                      W + " bump " + tag + " end span only", bad + " violated span(s), last at " + badAt + " of " + p.Spans.Length);
                Check(p.MinFloorMarginM >= -0.40f, W + " bump " + tag + " least shortfall", "min margin " + F(p.MinFloorMarginM) + " (unavoidable -0.39)");
            }
            // Pairs off: the twins change nothing before the angle pass.
            Check(SameNodes(NoPairs(tw, bump, FieldLength, false), NoPairs(no, bump, FieldLength, false)), W + " bump phases a-c", "twins vs none, Pairs off");
        }

        // ── FB-2 / FB-3: the sweep gate ─────────────────────────────────────────────────────────────────────────────────────
        private sealed class SweepLine
        {
            public string Label, Type;
            public Func<float, float, float> H;
            public float L;
            public bool Stations;
            public float AboveA, AboveB;
        }

        private static IEnumerable<SweepLine> SweepLines()
        {
            // DR-1's shapes (station to station, a raised B rope), and the same lines with ground ends.
            foreach (string id in new[] { "fixedGrip", "detachable", "gondola", "tram" })
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(id);
                float baseLen = id == "fixedGrip" ? 400f : id == "tram" ? 2000f : 1000f;
                foreach (float len in new[] { baseLen, baseLen + 0.27f * c.StepM + 1f, baseLen + 0.83f * c.StepM })
                foreach (float g in new[] { 0.14f, -0.31f })
                for (int shape = 0; shape < 3; shape++)
                foreach (bool st in new[] { true, false })
                {
                    Func<float, float, float> h = shape == 0 ? Hump(g, 0f, 0f, 1f, 0.03f, len, 0.05f)
                        : shape == 1 ? Hump(g, 0.03f * len, 0.33f * len, 0.07f * len, 0f, len, -0.08f)
                        : Hump(g, -0.025f * len, 0.6f * len, 0.1f * len, 0.02f, len, 0.12f);
                    yield return new SweepLine { Label = "DR-1 " + id + " " + F(len, "F1") + " g " + F(g) + " shape " + shape + (st ? " stations" : " ground"),
                                                 Type = id, H = h, L = len, Stations = st, AboveA = st ? -0.4f : 0f, AboveB = st ? 1.1f : 0f };
                }
            }
            // The 192-line direction sweep: four types, six lengths on and off the 5 m grid, grades 0.15 / 0.35, a hump or none,
            // stations or ground ends.
            foreach (string id in new[] { "fixedGrip", "detachable", "gondola", "tram" })
            {
                float baseLen = id == "fixedGrip" ? 350f : id == "tram" ? 1800f : 900f;
                foreach (float dl in new[] { 0f, 1.3f, 2.5f, 3.7f, 52f, 104.6f })
                foreach (float g in new[] { 0.15f, 0.35f })
                foreach (bool hump in new[] { false, true })
                foreach (bool st in new[] { true, false })
                {
                    float len = baseLen + dl;
                    Func<float, float, float> h = Hump(g, hump ? 0.04f * len : 0f, 0.45f * len, 0.08f * len, 0f, len, 0.06f);
                    yield return new SweepLine { Label = "D192 " + id + " " + F(len, "F1") + " g " + F(g) + (hump ? " hump" : "") + (st ? " stations" : " ground"),
                                                 Type = id, H = h, L = len, Stations = st };
                }
            }
            // Crests and dips past each type's cap (the BO cases' shapes), stations and ground ends.
            foreach (string id in new[] { "fixedGrip", "detachable", "gondola" })
            {
                float up = id == "fixedGrip" ? 200f : 500f, on = up;
                foreach (float[] gg in new[] { new[] { 0.20f, 0.05f }, new[] { 0.35f, 0.08f }, new[] { 0.45f, 0.10f }, new[] { 0.05f, 0.30f }, new[] { 0.08f, 0.45f }, new[] { 0.60f, 0f } })
                foreach (bool st in new[] { true, false })
                {
                    float g1 = gg[0], g2 = gg[1];
                    Func<float, float, float> h = (x, z) => 1000f + (x <= up ? g1 * x : g1 * up + g2 * (x - up)) + 0.04f * z;
                    yield return new SweepLine { Label = "crest/dip " + id + " " + F(g1) + " -> " + F(g2) + (st ? " stations" : " ground"),
                                                 Type = id, H = h, L = up + on, Stations = st };
                }
            }
            // The field line with tents near either end and mid-line (the ground ends of the field case).
            foreach (float x in new[] { 5f, 10f, 20f, 60f, 200f, 380f, 430f, 440f, 445f })
            foreach (float bh in new[] { 1f, 3f, 5.7f, 8f })
                yield return new SweepLine { Label = "field tent " + F(x, "F0") + " +" + F(bh, "F1"), Type = "fixedGrip", H = FieldWithBump(x, bh), L = FieldLength, Stations = false };
        }

        private static Func<float, float, float> Hump(float g, float h, float x0, float w, float bow, float len, float side) =>
            (x, z) => 1000f + g * x + bow * x * x / len + h * math.exp(-(x - x0) * (x - x0) / (w * w)) + side * z;

        private static void Fb2Fb3Sweep()
        {
            int lines = 0, clearBoth = 0, violBoth = 0, violTwinsOnly = 0, violNoneOnly = 0, moreTowers = 0, fewerTowers = 0, overRule = 0, phaseDiff = 0;
            int towersTw = 0, towersNo = 0, pairsTw = 0, pairsNo = 0;
            var examples = new List<string>();
            foreach (SweepLine l in SweepLines())
            {
                LiftTowerPlanConstants tw = LiftTowerPlanConstants.For(l.Type), no = BreakoverTests.WithoutTwins(l.Type);
                LiftTowerPlan pt = Tool(tw, l.H, l.L, l.Stations, l.AboveA, l.AboveB);
                LiftTowerPlan pn = Tool(no, l.H, l.L, l.Stations, l.AboveA, l.AboveB);
                lines++;
                towersTw += pt.LineTowers;
                towersNo += pn.LineTowers;
                pairsTw += PairNodes(pt) / 2;
                pairsNo += PairNodes(pn) / 2;
                if (pt.Clear && pn.Clear) clearBoth++;
                else if (!pt.Clear && !pn.Clear) violBoth++;
                else if (!pt.Clear) violTwinsOnly++;
                else violNoneOnly++;
                if (pt.LineTowers > pn.LineTowers) moreTowers++;
                if (pt.LineTowers < pn.LineTowers) fewerTowers++;
                // the twin rule: each extra pair the Break cap makes adds at most one partner
                int allowed = math.max(0, PairNodes(pt) / 2 - PairNodes(pn) / 2);
                bool over = pt.LineTowers - pn.LineTowers > allowed;
                if (over) overRule++;
                Check(pt.Clear || !pn.Clear, "fallback/FB-3 " + l.Label + " clear", "twins VIOLATED where none is clear: twins " + Describe(pt) + " | none " + Describe(pn));
                Check(!over, "fallback/FB-3 " + l.Label + " towers", "twins " + Describe(pt) + " | none " + Describe(pn) + " (allowed +" + allowed + ")");
                bool same = SameNodes(NoPairs(tw, l.H, l.L, l.Stations, l.AboveA, l.AboveB), NoPairs(no, l.H, l.L, l.Stations, l.AboveA, l.AboveB));
                if (!same) phaseDiff++;
                Check(same, "fallback/FB-2 " + l.Label, "Pairs off: the plan with twins differs from the plan without: " +
                      Describe(NoPairs(tw, l.H, l.L, l.Stations, l.AboveA, l.AboveB)) + " | " + Describe(NoPairs(no, l.H, l.L, l.Stations, l.AboveA, l.AboveB)));
                if ((pt.LineTowers != pn.LineTowers || pt.Clear != pn.Clear) && examples.Count < 6)
                    examples.Add(l.Label + ": twins " + Describe(pt) + " | none " + Describe(pn));
            }
            s_Report.Add("FB-3 sweep " + lines + " lines: clear both " + clearBoth + ", violated both " + violBoth + ", twins-only violated " +
                         violTwinsOnly + ", none-only violated " + violNoneOnly + "; towers twins " + towersTw + " / none " + towersNo +
                         " (more on " + moreTowers + ", fewer on " + fewerTowers + ", over the pair rule " + overRule + "); pairs twins " +
                         pairsTw + " / none " + pairsNo + "; FB-2 phases a-c differ " + phaseDiff);
            foreach (string e in examples)
                s_Report.Add("FB-3   " + e);
        }
    }
}
