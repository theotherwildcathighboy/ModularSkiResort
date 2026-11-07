// A reload writes nothing over a built line (FIX-LIST 57, DECISIONS 600 g). Where-tags `reload/RL-n`.
// The fit right after a build reads the ground before the line's own cut reaches the CPU heights. The cut (TerrainSystem.AddSegment,
// TerrainSystem.cs:1397-1516, ceiling = edge y + m_HeightRange.min + m_MinHeightOffset, never a fill) then sinks the ground under the
// towers. The load re-fit (every line: the fit's seen keys are in memory only) reads the sunk ground. Field (handtest-tower-lean
// run 2, tram x -1700): every tram tower stood 0.3-1.0 m over its ground at load, and T60 520719 stood 1.05 m over, past
// HeightAboveTolM 1.0. The fit re-wrote it 1.05 m lower. The ground under it then sank another 0.28 m (our cut again).
// RL-57 the field tram replayed from the build dump (Mod.log 1444-1501): the build fit, the measured sink under each tower, the load
//       re-fit. The old height rule writes the T60 (the repro). The built rule writes nothing. Also the rule's own sides: the fit
//       still raises a built node below its target, and a line not yet built still takes the fit's heights both ways.
// RL-1  a reload of every type writes nothing. The types are FG / DQ / GD / tram, drawn bottom to top and top to bottom, so towers
//       lean and both cases of the pad turn are covered. The test also covers the tram on a bench where its pedestals float
//       (DECISIONS 600 b). The ground is the same, and also sunk 1.05 m under every tower. The fit reads no tower yaw
//       (TowerLeanSystem's turn and lean are tower rotations only), so the draw order is what decides a turned tower here.

using System;
using System.Collections.Generic;
using System.Globalization;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    public static class ReloadTests
    {
        private static Func<bool, string, string, bool> s_Check;
        private static readonly List<string> s_Report = new List<string>();

        public static IReadOnlyList<string> Report => s_Report;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            s_Report.Clear();
            Rl57();
            Rl1();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);
        private static string F(double v, string f = "F2") => v.ToString(f, CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ the fit, as LiftTowerFitSystem runs it

        /// <summary>A built line: inner node positions, members, heads and node y as the build fit wrote them (y = ground + E + float).</summary>
        private sealed class Built
        {
            public float[] Inner;
            public int[] Members;
            public LiftTowerHead[] Heads;
            public float[] Y;
            public LiftTowerPlan Plan;
        }

        private static Built Write(LiftTowerPlan p, LiftTowerPlanConstants c)
        {
            int n = p.Nodes.Length - 2;
            var b = new Built { Inner = new float[n], Members = new int[n], Heads = new LiftTowerHead[n], Y = new float[n], Plan = p };
            for (int i = 0; i < n; i++)
            {
                LiftPlanNode pn = p.Nodes[i + 1];
                b.Inner[i] = pn.AlongM;
                b.Members[i] = pn.Member;
                b.Heads[i] = pn.Head;
                b.Y[i] = pn.GroundM + LiftTowerEdit.ElevationM(c, pn.Member, pn.Head) + pn.FloatM;
            }
            return b;
        }

        /// <summary>The load re-fit of a built line and the node writes it makes. The re-fit is LiftTowerFitSystem.FitLine after a load: no
        /// tool registry, kinds unknown, the line's own heads. The line is straight, so Moved is allowed. <paramref name=pre57/>: as before
        /// FIX-LIST 57 (the ground as sampled, the two-sided height rule). Otherwise: as now (a built line judged at its towers' feet,
        /// BuiltFootSampler, and the built height rule). <paramref name=maxAbove/>: the most a node stands over its target.</summary>
        private static LiftTowerPlan Reload(LiftTerrainSampler t, float length, float ropeA, float ropeB, LiftTowerPlanConstants c,
                                            Built b, float engineMax, bool stations, bool built, bool pre57, out int writes,
                                            out float maxAbove, out string why)
        {
            int n = b.Inner.Length;
            if (built && !pre57)
            {
                var foot = new float[n];
                for (int i = 0; i < n; i++)
                    foot[i] = b.Y[i] - LiftTowerEdit.ElevationM(c, b.Members[i], b.Heads[i]);
                t = LiftTowerEdit.BuiltFootSampler(t, b.Inner, foot);
            }
            var pl = new LiftTowerPlanner
            {
                StationA = stations, StationB = stations, PreviousKinds = new LiftTrainKind[n], PreviousHeads = b.Heads, HeadsAsGiven = true,
                FootAlong = built && !pre57 ? b.Inner : null,
            };
            LiftTowerPlan before = pl.PlanFixed(t, length, ropeA, ropeB, c, b.Inner, b.Members, null, assign: false);
            pl.HeadsAsGiven = false;
            LiftTowerFit fit = pl.Fit(t, length, ropeA, ropeB, c, b.Inner, true, engineMax, b.Members);
            writes = 0;
            maxAbove = 0f;
            why = string.Empty;
            LiftTowerPlan plan = LiftTowerPlanner.ToWrite(before, fit, built, out string worse);
            if (plan == null)
            {
                why = "kept as built (" + worse + ")";
                return null;
            }
            for (int i = 0; i < n; i++)
            {
                LiftPlanNode pn = plan.Nodes[i + 1];
                float target = pn.GroundM + LiftTowerEdit.ElevationM(c, pn.Member, pn.Head) + pn.FloatM;
                bool moved = math.abs(pn.AlongM - b.Inner[i]) > 0.05f;
                bool reMembered = pn.Member != b.Members[i] || pn.Head != b.Heads[i];
                maxAbove = math.max(maxAbove, b.Y[i] - target);
                bool write = pre57 ? moved || reMembered || LiftTowerEdit.HeightWrite(b.Y[i], target)
                                   : LiftTowerEdit.NodeWrite(built, moved, reMembered, b.Y[i], target, false);
                if (write)
                {
                    writes++;
                    why += (why.Length > 0 ? ", " : string.Empty) + "t" + (i + 2) + (moved ? " moved" : string.Empty) +
                           (reMembered ? " " + c.NameOf(b.Members[i], b.Heads[i]) + " -> " + c.NameOf(pn.Member, pn.Head) : string.Empty) +
                           " y " + F(b.Y[i]) + " -> " + F(target);
                }
            }
            return plan;
        }

        /// <summary>The ground sunk by our own cut: <paramref name="depth"/> at each tower, fading to 0 within 10 m along the line.</summary>
        private static LiftTerrainSampler Sunk(LiftTerrainSampler t, float[] at, float[] depth) =>
            (s, l) =>
            {
                float d = 0f;
                for (int i = 0; i < at.Length; i++)
                    d = math.max(d, depth[i] * math.saturate(1f - math.abs(s - at[i]) / 10f));
                return t(s, l) - d;
            };

        // ------------------------------------------------------------------ RL-57: the field tram

        // x -1700, drawn top to bottom (the path's first node is the low end here). The data come from the "Towers [nodes]" block before the
        // build fit: along, node y, terrain, chord clearance at 20/40/60/80 % of the span to the next node.
        private static readonly float[] TramAlong = { 0f, 450f, 670f, 710f, 850f, 900f };
        private static readonly float[] TramY = { 782.03f, 1320.82f, 1415.16f, 1416.56f, 1391.24f, 1356.78f };
        private static readonly float[] TramGround = { 781.65f, 1290.82f, 1385.16f, 1386.56f, 1376.24f, 1356.32f };
        private static readonly float[][] TramChord =
        {
            new[] { 72.7f, 129.9f, 143.2f, 48.8f }, new[] { 36.0f, 17.4f, 9.8f, 8.3f }, new[] { 33.0f, 36.2f, 37.0f, 33.9f },
            new[] { 27.2f, 9.8f, 6.2f, 21.4f }, new[] { 7.0f, 3.3f, 1.7f, 0.6f },
        };
        // high side - center at the nodes (the plan / fit lines)
        private static readonly float[] TramSide = { 0f, 0.9f, 450f, 11.4f, 670f, 7.6f, 710f, 7.2f, 850f, 6.0f, 900f, 2.1f };
        // the sink at load under each line tower, with T60 520719 at 1.05 m (pivot above terrain at load, Mod.log 2593-2640)
        private static readonly float[] TramSink = { 1.05f, 0.6f, 0.6f, 0.6f };

        internal static float[] TramCenter()
        {
            var sy = new List<float>();
            float[] f = { 0.2f, 0.4f, 0.6f, 0.8f };
            for (int i = 0; i < TramAlong.Length; i++)
            {
                sy.Add(TramAlong[i]);
                sy.Add(TramGround[i]);
                if (i == TramAlong.Length - 1)
                    break;
                for (int q = 0; q < 4; q++)
                {
                    sy.Add(math.lerp(TramAlong[i], TramAlong[i + 1], f[q]));
                    sy.Add(math.lerp(TramY[i], TramY[i + 1], f[q]) - TramChord[i][q]);
                }
            }
            return sy.ToArray();
        }

        private static void Rl57()
        {
            const string w = "reload/RL-57";
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("tram");
            LiftTerrainSampler u = EishainTests.Sampler(TramCenter(), TramSide, c.HalfWidthM);
            const float L = 900f, ropeA = 808.28f, ropeB = 1383.03f;
            float[] inner = { 450f, 670f, 710f, 850f };

            // (a) the build: the tool's line (T45 x3, T30, ground ends) fitted heights only, as in session (field: T60 x3, T45, clear)
            var pl = new LiftTowerPlanner { StationA = false, StationB = false };
            LiftTowerFit f0 = pl.Fit(u, L, ropeA, ropeB, c, inner, false, 3000f, new[] { 1, 1, 1, 0 });
            Built b = Write(f0.Plan, c);
            s_Report.Add("RL-57 build: " + f0.Mode + " " + F(f0.Plan.MinFloorMarginM) + " {" + f0.Plan.MemberCounts() + "} (field: Kept +2.7, " +
                         "{SkiTramTower45 x1, SkiTramTower60 x3})");
            Check(f0.Plan.Clear && b.Members[0] == 2, w, "the build fit is clear with a T60 at 450 m (field); got " + f0.Mode + " " +
                  F(f0.Plan.MinFloorMarginM) + " {" + f0.Plan.MemberCounts() + "}");
            Check(math.abs(b.Y[0] - 1320.82f) < 0.02f, w, "the T60 node at ground + 30 = 1320.82 (field); got " + F(b.Y[0]));

            // (b) a reload on the same ground writes nothing either way (the fixed point)
            Reload(u, L, ropeA, ropeB, c, b, 1000f, false, true, false, out int same, out _, out string sameWhy);
            Reload(u, L, ropeA, ropeB, c, b, 1000f, false, true, true, out int sameOld, out _, out _);
            Check(same == 0 && sameOld == 0, w, "a reload on the build's ground writes " + same + " (old rule " + sameOld + "): " + sameWhy);

            // (c) the field: the ground sank under the towers before the load
            LiftTerrainSampler sunk = Sunk(u, inner, TramSink);
            Reload(sunk, L, ropeA, ropeB, c, b, 1000f, false, true, true, out int oldWrites, out float above, out string oldWhy);
            LiftTowerPlan p = Reload(sunk, L, ropeA, ropeB, c, b, 1000f, false, true, false, out int writes, out _, out string why);
            s_Report.Add("RL-57 load on the sunk ground: pre-57 rule " + oldWrites + " write(s) (field: 1, T60 1.05 m lower: " + oldWhy + "), now " +
                         writes + "; max tower over its sunk target before " + F(above) + " m; " + (p != null ? "plan {" + p.MemberCounts() + "}" : why));
            Check(oldWrites >= 1, w, "the repro: the pre-57 rule re-writes the T60 1.05 m over its sunk ground; got " + oldWrites + " write(s)");
            Check(writes == 0, w, "a reload writes nothing over the built tram (judged at its feet); got " + writes + ": " + why);
            Check(above > LiftTowerEdit.HeightAboveTolM, w, "the T60 stands past HeightAboveTolM over its sunk ground (field 1.05); got " + F(above));

            // (d) the rule's other sides, at the field numbers (node 1320.82, E 30, ground 1289.77 at load)
            Check(LiftTowerEdit.HeightWrite(1320.82f, 1289.77f + 30f) && !LiftTowerEdit.HeightWrite(1320.82f, 1289.77f + 30f, true), w,
                  "the field node: written by the pre-57 rule, kept by the built rule");
            Check(LiftTowerEdit.HeightWrite(1320.82f, 1289.77f + 30f, false), w, "a line not yet built still takes the fit's height (above)");
            Check(LiftTowerEdit.HeightWrite(1320.82f, 1291.20f + 30f, true), w, "a built node 0.38 m under its target is still raised (sinking tower)");
            Check(!LiftTowerEdit.HeightWrite(1320.82f, 1290.90f + 30f, true), w, "a built node within HeightBelowTolM is left alone");
            Check(LiftTowerEdit.NodeWrite(true, true, false, 1320.82f, 1319.77f, false) && LiftTowerEdit.NodeWrite(true, false, true, 1320.82f, 1319.77f, false) &&
                  LiftTowerEdit.NodeWrite(true, false, false, 1320.82f, 1320.82f, true), w,
                  "a move, a new member or head, or an Elevation off its (E, E) is still written over a built line");
            // the line not yet built (the tool's fresh nodes) on the sunk ground: the fit writes its heights (no change in session)
            Reload(sunk, L, ropeA, ropeB, c, b, 1000f, false, false, false, out int fresh, out _, out _);
            Check(fresh >= 1, w, "a line not yet built is re-grounded on the sunk ground; got " + fresh + " write(s)");
        }

        // ------------------------------------------------------------------ RL-1: every type reloads to nothing

        private static void Rl1()
        {
            const string w = "reload/RL-1";
            var lines = new List<(string Type, string Label, float Length, Func<float, float> H, float Side)>
            {
                ("fixedGrip", "FG 500 m face 30 deg", 500f, s => 1000f + 0.58f * s + 14f * math.sin(math.PI * s / 500f), 2.0f),
                ("fixedGrip", "FG 400 m crest", 400f, s => 1000f + 0.72f * s - 0.0008f * s * s, 1.0f),
                ("detachable", "DQ 400 m face 35 deg", 400f, s => 1000f + 0.70f * s + 12f * math.sin(math.PI * s / 400f), 2.5f),
                ("gondola", "GD 500 m face 37 deg", 500f, s => 1000f + 0.75f * s + 18f * math.sin(math.PI * s / 500f), 3.0f),
                ("tram", "tram 900 m face 45 deg", 900f, s => 1000f + 1.0f * s + 60f * math.sin(math.PI * s / 900f), 6.0f),
                ("tram", "tram 800 m bench (pedestal float)", 800f, s => 1000f + 1.5f * math.min(s, 385f) + 1.5f * math.max(0f, s - 415f), 0.0f),
            };
            var leaned = new Dictionary<string, int>();
            int lineCount = 0, towers = 0, floated = 0, keptAsBuilt = 0;
            float maxAbove = 0f;
            foreach (var ln in lines)
            {
                foreach (bool reversed in new[] { false, true })
                {
                    LiftTowerPlanConstants c = LiftTowerPlanConstants.For(ln.Type);
                    float L = ln.Length;
                    Func<float, float> h = reversed ? (Func<float, float>)(s => ln.H(L - s)) : ln.H;
                    float side = ln.Side;
                    LiftTerrainSampler u = (s, l) => h(s) + side * math.abs(l) / c.HalfWidthM;
                    float ropeA = h(0f) + c.RopeAboveBase(0), ropeB = h(L) + c.RopeAboveBase(0);
                    string label = ln.Label + (reversed ? " top to bottom" : " bottom to top");
                    bool bench = ln.Label.Contains("bench");

                    // the tool's plan, then the build fit over its fresh nodes (heights only, the tool's members)
                    var pl = new LiftTowerPlanner { StationA = false, StationB = false };
                    LiftTowerPlan tool = bench
                        ? pl.PlanFixed(u, L, ropeA, ropeB, c, new List<float> { 400f }, new List<int> { 2 }, null, assign: false)
                        : pl.Plan(u, L, ropeA, ropeB, c);
                    Built t0 = Write(tool, c);
                    LiftTowerFit f0 = pl.Fit(u, L, ropeA, ropeB, c, t0.Inner, false, c.MaxSpanM, t0.Members);
                    Built b = Write(f0.Plan, c);
                    lineCount++;
                    towers += b.Inner.Length;
                    for (int i = 1; i < f0.Plan.Nodes.Length - 1; i++)
                    {
                        if (f0.Plan.Nodes[i].LeanDeg > 0.5f)
                            leaned[ln.Type] = (leaned.TryGetValue(ln.Type, out int k) ? k : 0) + 1;
                        if (f0.Plan.Nodes[i].FloatM > 0.5f)
                            floated++;
                    }

                    // the reload: the same ground, then ground sunk 1.05 m under every tower (the field's worst, past HeightAboveTolM)
                    float engineMax = math.min(c.MaxSpanM, ln.Type == "tram" ? 1000f : c.MaxSpanM);
                    LiftTowerPlan p1 = Reload(u, L, ropeA, ropeB, c, b, engineMax, false, true, false, out int w1, out _, out string why1);
                    Reload(u, L, ropeA, ropeB, c, b, engineMax, false, true, true, out int o1, out _, out _);
                    var sink = new float[b.Inner.Length];
                    for (int i = 0; i < sink.Length; i++)
                        sink[i] = 1.05f;
                    LiftTerrainSampler sunkU = Sunk(u, b.Inner, sink);
                    LiftTowerPlan p2 = Reload(sunkU, L, ropeA, ropeB, c, b, engineMax, false, true, false, out int w2, out _, out string why2);
                    Reload(sunkU, L, ropeA, ropeB, c, b, engineMax, false, true, true, out int o2, out float above, out _);
                    if (p1 == null || p2 == null)
                        keptAsBuilt++;
                    maxAbove = math.max(maxAbove, above);
                    Check(w1 == 0 && o1 == 0, w + " " + label, "a reload on the same ground writes " + w1 + " (pre-57 rule " + o1 + "): " + why1);
                    Check(w2 == 0, w + " " + label, "a reload on ground sunk 1.05 m under the towers writes " + w2 + " (pre-57 rule " + o2 + "): " + why2);
                    s_Report.Add("RL-1 " + label + ": build " + f0.Mode + " " + F(f0.Plan.MinFloorMarginM) + " {" + f0.Plan.MemberCounts() +
                                 "}, max lean " + F(MaxLean(f0.Plan), "F1") + ", max float " + F(MaxFloat(f0.Plan)) + " | reload writes " + w1 +
                                 ", sunk 1.05 m: " + w2 + " (pre-57 rule " + o2 + ")");
                }
            }
            foreach (string type in new[] { "fixedGrip", "detachable", "gondola", "tram" })
                Check(leaned.TryGetValue(type, out int k) && k > 0, w, type + ": no leaned tower in the reload set");
            Check(floated > 0, w, "no tram tower floats on its pedestals in the reload set (the bench)");
            s_Report.Add("RL-1 " + lineCount + " lines, " + towers + " line towers (leaned FG " + Get(leaned, "fixedGrip") + " / DQ " +
                         Get(leaned, "detachable") + " / GD " + Get(leaned, "gondola") + " / tram " + Get(leaned, "tram") + ", floated " +
                         floated + "), kept as built " + keptAsBuilt + "; max tower over its sunk target " + F(maxAbove) + " m");
        }

        private static int Get(Dictionary<string, int> d, string k) => d.TryGetValue(k, out int v) ? v : 0;

        private static float MaxLean(LiftTowerPlan p)
        {
            float m = 0f;
            for (int i = 1; i < p.Nodes.Length - 1; i++)
                m = math.max(m, p.Nodes[i].LeanDeg);
            return m;
        }

        private static float MaxFloat(LiftTowerPlan p)
        {
            float m = 0f;
            for (int i = 1; i < p.Nodes.Length - 1; i++)
                m = math.max(m, p.Nodes[i].FloatM);
            return m;
        }
    }
}
