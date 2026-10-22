// LiftToolInputTests — T125 (one lift tool; docs/research/field/2026-10-20/playthrough/lift-tool-build.md): the lift tool's input
// state machine (LiftLineInput) and the edit model's undo stack (LiftLineEdit.Undo), headless. TI-1..TI-8.
//   TI-1 right click table: every step x dragging x undo depth -> the one action it does (vanilla "Undo Net Control Point").
//   TI-2 a full right-click walk back: two ends + three edits -> undo x3 -> drop the second end -> drop the first -> close.
//   TI-3 undo restores the exact pins / removed spots after add, move, remove (free and pinned) and clear; refusals push nothing.
//   TI-4 the step hint: never empty, different per step, names right click / Enter / Esc where they act, US English.
//   TI-5 drag off the line removes (threshold), on the line moves.
//   TI-6 minimum line length: station-to-station keeps 2 x MinSpanM; a section between loose / ground ends needs 2 x StepM, and
//        the planner plans such a short section clear with no tower.
//   TI-7 Enter on the default plan: the emitted line is the planner's own (stations first / last, every tower Free), so the tool
//        with no edit builds what the old quick-place built, with the planner's towers.
//   TI-8 the type names the hub tiles pass.

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    internal static class LiftToolInputTests
    {
        private static Func<bool, string, string, bool> s_Check;

        private static void Check(bool ok, string where, string msg) => s_Check(ok, where, msg);

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            RightClickTable();
            WalkBack();
            UndoRestores();
            Prompts();
            DragOff();
            MinLength();
            DefaultPlan();
            TypeNames();
            SplitReplayDq();
            SplitReplayGondola();
            SplitOrdinaryLines();
            SplitChordMembers();
        }

        // ── R-6 (2026-10-22): the engine split the tool's courses (CourseSplitSystem elevation transitions) ────────────────────
        // Replay of the 2026-10-20 playthrough log (docs/research/field/2026-10-20/playthrough/logs/SkiResortMod.Mod.log): the
        // "Towers [nodes]" block written BEFORE the fit moved the line (:710 DQ, :1533 gondola), base first: span to the next node,
        // node y, terrain, chord clearance at 20/40/60/80 %. Ground between nodes = lerp(node y) - clearance (LiftPlannerTests rule).

        private struct DumpNode
        {
            public float Span, Y, Terrain;
            public float[] Chord;
        }

        private static DumpNode D(float span, float y, float terrain, params float[] chord) =>
            new DumpNode { Span = span, Y = y, Terrain = terrain, Chord = chord.Length == 0 ? null : chord };

        // DQ 1,065 m, 11:25:47 (log :710-:793): 17 nodes for 10 planned courses.
        private static readonly DumpNode[] DqBuilt =
        {
            D(28f, 923.27f, 923.27f, -0.5f, -0.2f, 0.9f, 2.6f), D(57f, 929.25f, 925.86f, 4.5f, 5.7f, 6.8f, 5.5f),
            D(30f, 940.65f, 934.65f, 7.1f, 7.0f, 6.4f, 2.6f), D(50f, 947.70f, 945.70f, 2.2f, 1.3f, 0.6f, 0.1f),
            D(220f, 960.93f, 960.93f, 6.7f, 7.9f, 11.3f, 9.9f), D(205f, 996.65f, 994.65f, 5.2f, 6.9f, 7.2f, 3.1f),
            D(20f, 1063.35f, 1061.35f, 1.6f, 1.3f, 1.5f, 2.0f), D(48f, 1071.52f, 1068.97f, 3.6f, 4.3f, 5.0f, 4.4f),
            D(12f, 1091.25f, 1087.86f, 3.2f, 3.1f, 3.1f, 3.2f), D(28f, 1096.11f, 1092.64f, 3.9f, 4.3f, 4.5f, 4.2f),
            D(8f, 1107.61f, 1103.78f, 3.8f, 3.8f, 3.8f, 3.9f), D(161f, 1110.89f, 1106.89f, 2.5f, 2.4f, 2.7f, 2.7f),
            D(64f, 1164.27f, 1163.14f, 4.1f, 7.4f, 8.4f, 5.6f), D(45f, 1185.61f, 1183.61f, 1.2f, 0.8f, 0.1f, -0.5f),
            D(70f, 1202.29f, 1202.29f, 1.5f, 1.0f, -0.6f, -0.8f), D(20f, 1223.50f, 1223.50f, 0f, 0f, 0f, 0f),
            D(0f, 1227.82f, 1227.82f),
        };

        // The same DQ line after the fit MOVED it (11:25:47, log :843-:926; spans from the fit's positions, :807-:840): more ground
        // samples for the replay (the 220 / 205 m spans above leave 44 m gaps).
        private static readonly DumpNode[] DqMoved =
        {
            D(15f, 923.27f, 923.27f, 1.4f, 3.3f, 5.4f, 7.6f), D(110f, 936.10f, 926.10f, 13.8f, 16.0f, 15.0f, 15.2f),
            D(110f, 958.21f, 948.21f, 4.5f, 0.4f, 3.2f, 3.8f), D(59f, 968.02f, 966.02f, 1.5f, 1.6f, 2.0f, 1.3f),
            D(59f, 970.59f, 970.59f, 1.7f, 3.2f, 3.2f, 3.8f), D(58f, 986.58f, 982.58f, 2.4f, 1.6f, 1.7f, 3.8f),
            D(59f, 1003.96f, 997.96f, 3.3f, 0.9f, 0.7f, 0.6f), D(59f, 1017.36f, 1017.36f, 1.2f, 2.4f, 2.4f, 2.6f),
            D(59f, 1039.84f, 1037.84f, 0.7f, 0.7f, 1.8f, 2.8f), D(58f, 1061.99f, 1059.83f, 0.4f, 1.4f, 2.1f, 2.5f),
            D(59f, 1084.02f, 1082.02f, 0.4f, 0.3f, 0.9f, 0.3f), D(58f, 1106.93f, 1106.93f, 1.2f, 1.5f, 0.7f, 0.5f),
            D(57f, 1130.38f, 1128.38f, 2.9f, 3.2f, 2.7f, 1.9f), D(115f, 1148.35f, 1146.35f, 1.4f, 1.0f, 7.0f, 7.5f),
            D(65f, 1188.13f, 1186.13f, 1.4f, 0.5f, 0.7f, 3.0f), D(65f, 1212.48f, 1208.48f, 1.4f, -0.1f, -0.4f, -0.3f),
            D(0f, 1227.82f, 1227.82f),
        };

        // Gondola 1,408 m, 11:45:19 (log :1533-:1648): 23 nodes for 13 planned courses.
        private static readonly DumpNode[] GdBuilt =
        {
            D(27f, 931.58f, 931.58f, 0.8f, 1.5f, 2.3f, 3.1f), D(14f, 935.42f, 931.58f, 6.8f, 9.8f, 12.9f, 12.9f),
            D(10f, 950.43f, 941.30f, 2.6f, 0.3f, -0.3f, 0.1f), D(124f, 958.27f, 957.80f, 0.6f, 3.4f, -0.3f, 1.8f),
            D(16f, 984.93f, 982.95f, 1.9f, 2.2f, 2.6f, 3.2f), D(210f, 988.25f, 984.25f, 9.4f, 8.4f, 17.5f, 17.3f),
            D(87f, 1017.95f, 1013.95f, 3.7f, 3.1f, 4.1f, 6.0f), D(63f, 1050.66f, 1048.10f, 0.3f, -0.7f, 0.3f, 1.9f),
            D(40f, 1073.89f, 1070.65f, 4.0f, 4.5f, 4.8f, 6.0f), D(152f, 1088.62f, 1080.62f, 14.0f, 20.1f, 17.2f, 10.8f),
            D(68f, 1120.79f, 1115.58f, 5.0f, 2.4f, 0.1f, 0.4f), D(204f, 1134.90f, 1130.90f, 17.5f, 10.5f, 11.1f, 9.4f),
            D(26f, 1266.50f, 1264.40f, 1.2f, 0.6f, 0.0f, -0.2f), D(35f, 1282.69f, 1282.69f, 0.5f, 0.3f, 0.1f, 0.8f),
            D(46f, 1305.79f, 1301.79f, 5.0f, 8.0f, 9.4f, 8.9f), D(14f, 1300.37f, 1293.55f, 6.1f, 5.5f, 5.0f, 4.4f),
            D(45f, 1298.72f, 1294.72f, 10.1f, 13.1f, 11.2f, 5.5f), D(15f, 1333.71f, 1333.35f, 0.3f, 0.5f, 0.0f, -0.8f),
            D(40f, 1349.00f, 1348.82f, -0.9f, -1.8f, -1.0f, 0.0f), D(26f, 1368.66f, 1368.66f, -0.2f, -0.4f, -0.6f, 0.1f),
            D(139f, 1384.95f, 1383.77f, 7.3f, 5.8f, 6.4f, 17.4f), D(8f, 1469.15f, 1461.15f, 6.4f, 4.8f, 3.2f, 1.6f),
            D(0f, 1461.15f, 1461.15f),
        };

        /// <summary>Ground at <paramref name="s"/> meters from the base along the dumped line (piecewise linear).</summary>
        private static LiftTerrainSampler Ground(params DumpNode[][] dumps)
        {
            var pts = new List<float2>();
            float[] f = { 0.2f, 0.4f, 0.6f, 0.8f };
            foreach (DumpNode[] nodes in dumps)
            {
                float at = 0f;
                for (int i = 0; i < nodes.Length; i++)
                {
                    pts.Add(new float2(at, nodes[i].Terrain));
                    if (nodes[i].Chord == null)
                        break;
                    for (int q = 0; q < 4; q++)
                        pts.Add(new float2(at + f[q] * nodes[i].Span, nodes[i].Y + (nodes[i + 1].Y - nodes[i].Y) * f[q] - nodes[i].Chord[q]));
                    at += nodes[i].Span;
                }
            }
            pts.Sort((a, b) => a.x.CompareTo(b.x));
            var S = new List<float>();
            var G = new List<float>();
            foreach (float2 p in pts)
            {
                if (S.Count > 0 && p.x - S[S.Count - 1] < 0.5f)
                    continue;   // one ground value per 0.5 m
                S.Add(p.x);
                G.Add(p.y);
            }
            return (s, l) =>
            {
                if (s <= S[0])
                    return G[0];
                for (int i = 1; i < S.Count; i++)
                    if (s <= S[i])
                        return G[i - 1] + (G[i] - G[i - 1]) * (s - S[i - 1]) / math.max(S[i] - S[i - 1], 1e-6f);
                return G[G.Count - 1];
            };
        }

        /// <summary>The tool's emitted line in world xz along +x from the BASE (x = meters from the base): <paramref name="towers"/>
        /// = (along from the tool's FIRST end, member, pinned); <paramref name="fromTop"/> = the tool's first click was the top.</summary>
        private static List<LiftLineNode> Emitted(LiftTowerPlanConstants c, float length, LiftTerrainSampler g, bool fromTop,
                                                  (float Along, int Member, bool Pin)[] towers)
        {
            var list = new List<LiftLineNode>();
            float X(float a) => fromTop ? length - a : a;
            list.Add(new LiftLineNode { AlongM = 0f, Position = new float3(X(0f), g(X(0f), 0f), 0f), Member = -1, Kind = LiftLineNodeKind.Station });
            foreach (var t in towers)
            {
                float x = X(t.Along), e = LiftTowerEdit.ElevationM(c, t.Member);
                list.Add(new LiftLineNode
                {
                    AlongM = t.Along, Position = new float3(x, g(x, 0f) + e, 0f), GroundM = g(x, 0f), ElevationM = e, Member = t.Member,
                    Kind = t.Pin ? LiftLineNodeKind.Pinned : LiftLineNodeKind.Free,
                });
            }
            list.Add(new LiftLineNode { AlongM = length, Position = new float3(X(length), g(X(length), 0f), 0f), Member = -1, Kind = LiftLineNodeKind.Station });
            return list;
        }

        /// <summary>The built path as the census walks it (base first): the emitted nodes with float noise (±0.3 m, inside the 0.5 m
        /// match) plus the engine's extra split nodes at <paramref name="extrasFromBase"/>.</summary>
        private static List<float3> Built(List<LiftLineNode> emitted, float length, float[] extrasFromBase, LiftTerrainSampler g)
        {
            var xs = new List<float3>();
            for (int i = 0; i < emitted.Count; i++)
            {
                float3 p = emitted[i].Position;
                bool end = i == 0 || i == emitted.Count - 1;
                xs.Add(end ? p : p + new float3(i % 2 == 0 ? 0.3f : -0.25f, 0f, i % 3 == 0 ? 0.2f : -0.15f));
            }
            foreach (float x in extrasFromBase)
                xs.Add(new float3(x, g(x, 0f) + 2f, 0.1f));
            xs.Sort((a, b) => a.x.CompareTo(b.x));
            return xs;
        }

        /// <summary>The fit's tool-line path (LiftTowerFitSystem.FitLine): MatchSplit, ChordMembers, Fit heights only.</summary>
        private static void SplitReplay(string W, string type, DumpNode[][] dumps, float length, bool fromTop, float ropeBase, float ropeTop,
                                        (float Along, int Member, bool Pin)[] towers, float[] extrasFromBase)
        {
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For(type);
            LiftTerrainSampler g = Ground(dumps);
            List<LiftLineNode> emitted = Emitted(c, length, g, fromTop, towers);
            var reg = new LiftToolLines();
            reg.Register(type, emitted);
            List<float3> path = Built(emitted, length, extrasFromBase, g);
            int n = path.Count;
            Check(n - 1 == towers.Length + 1 + extrasFromBase.Length, W, $"replay built {n - 1} edges, expected {towers.Length + 1 + extrasFromBase.Length}");

            // Before R-6: the exact match fails, so the fit treated the line as an ordinary one and MOVED it.
            var innerPts = path.GetRange(1, n - 2);
            Check(reg.Match(innerPts, out _, out _) == null, W, "the exact match unexpectedly accepts a split line");

            LiftToolMatch m = reg.MatchSplit(path);
            Check(m != null, W, "split tool line not matched");
            if (m == null)
                return;
            int pins = 0;
            foreach (var t in towers)
                pins += t.Pin ? 1 : 0;
            Check(m.Registered == towers.Length && m.Extras == extrasFromBase.Length && m.Pins == pins && m.Reversed == fromTop, W,
                  $"match counts: registered {m.Registered}/{towers.Length}, extras {m.Extras}/{extrasFromBase.Length}, pins {m.Pins}/{pins}, reversed {m.Reversed}");
            // Every tool node got its own member back; every extra is flagged.
            var inner = new float[n - 2];
            var ground = new float[n - 2];
            for (int i = 0; i < n - 2; i++)
            {
                inner[i] = path[i + 1].x;
                ground[i] = g(inner[i], 0f);
                bool isExtra = Array.Exists(extrasFromBase, x => math.abs(x - inner[i]) < 1e-3f);
                Check(m.Extra[i] == isExtra, W, $"node at {inner[i]:F0} m: extra {m.Extra[i]}, expected {isExtra}");
                if (!isExtra)
                {
                    float along = fromTop ? length - inner[i] : inner[i];
                    var t = Array.Find(towers, q => math.abs(q.Along - along) < 1f);
                    Check(m.Members[i] == t.Member && m.Pinned[i] == t.Pin, W, $"tool node at {along:F0} m: member {m.Members[i]} pin {m.Pinned[i]}, expected {t.Member} {t.Pin}");
                }
            }
            int[] members = (int[])m.Members.Clone();
            LiftToolLines.ChordMembers(c, inner, ground, members, new int[n - 2], length, ropeBase, ropeTop);
            bool filled = true;
            for (int i = 0; i < n - 2; i++)
                filled &= members[i] >= 0 && members[i] < c.Ladder.Length && (m.Extra[i] || members[i] == m.Members[i]);
            Check(filled, W, "ChordMembers left an extra unfilled or changed a tool node");

            // The tool's own plan (no extras) on this replay ground: the yardstick for the line with extras.
            var toolAlong = new List<float>();
            var toolMem = new List<int>();
            for (int i = 0; i < n - 2; i++)
                if (!m.Extra[i])
                {
                    toolAlong.Add(inner[i]);
                    toolMem.Add(m.Members[i]);
                }
            var planner = new LiftTowerPlanner();
            LiftTowerPlan toolPlan = planner.PlanFixed(g, length, ropeBase, ropeTop, c, toolAlong, toolMem, null, assign: false);
            LiftTowerFit fit = planner.Fit(g, length, ropeBase, ropeTop, c, inner, false, c.MaxSpanM, members, m.Extra);
            Check(fit.Mode != LiftFitMode.Moved && fit.Plan.Nodes.Length == n, W, $"fit {fit.Mode}, {fit.Plan.Nodes.Length} nodes for {n}");
            bool kept = true, heights = true;
            for (int i = 1; i < n - 1; i++)
            {
                kept &= math.abs(fit.Plan.Nodes[i].AlongM - inner[i - 1]) < 1e-3f;
                if (!m.Extra[i - 1])
                    heights &= fit.Plan.Nodes[i].Member == m.Members[i - 1];
            }
            Check(kept, W, "a node moved (a tool line is fitted heights only)");
            // When the tool's plan clears this ground, the split line must too, with every tool member (pins AND heights) kept.
            if (toolPlan.MinFloorMarginM >= -LiftTowerPlanner.KeepTolM)
                Check(fit.Mode == LiftFitMode.Kept && heights, W, $"the tool plan clears ({toolPlan.MinFloorMarginM:F1} m) but the split line is " +
                      $"{fit.Mode} with tool members {(heights ? "kept" : "changed")}");
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < n - 2; i++)
                if (m.Extra[i])
                    sb.Append(sb.Length > 0 ? ", " : string.Empty).Append(inner[i].ToString("F0")).Append(' ')
                      .Append(c.Ladder[members[i]].Name.Replace("SkiLiftTower", string.Empty));
            Console.WriteLine($"  {W}: {n - 1} built edges for {towers.Length + 1} planned -> MatchSplit {m.Registered} tool + {m.Extras} extra " +
                              $"({m.Pins} pinned, reversed {m.Reversed}); extras {sb}; fit {fit.Mode}, no node moved, tool members " +
                              $"{(heights ? "kept" : "re-assigned")}; min floor margin {fit.Plan.MinFloorMarginM:F1} m (the tool's own plan " +
                              $"on this replay ground: {toolPlan.MinFloorMarginM:F1} m)");
        }

        // TI-9: the 16-vs-10 detachable (drawn from the top; pins 20/135/475/680/900/950; log :700-:703).
        private static void SplitReplayDq()
        {
            var towers = new[]
            {
                (20f, 0, true), (90f, 0, false), (135f, 1, true), (360f, 2, false), (475f, 1, true), (680f, 1, true),
                (900f, 0, true), (950f, 1, true), (980f, 3, false),
            };
            var extras = new[] { 27f, 609f, 657f, 669f, 697f, 866f };
            // (a) the station ropes the tool planned with (stub node + the shortest member's rope: 923.27 + 8 / 1227.82 + 8).
            SplitReplay("tool/TI-9 DQ 16 vs 10", "detachable", new[] { DqBuilt, DqMoved }, 1065f, true, 931.27f, 1235.82f, towers, extras);
            // (b) the ropes the fit reads in game (the station's rope lane, log :807: 925.96 / 1235.40): the base lane sits 5.3 m
            // under the tool's assumption, so span 1 is violated whatever the towers (a station-rope mismatch, not R-6): still no
            // node moves; the fit WARNs that the pinned towers stay.
            SplitReplay("tool/TI-9b DQ 16 vs 10 (lane ropes)", "detachable", new[] { DqBuilt, DqMoved }, 1065f, true, 925.96f, 1235.40f, towers, extras);
            LanePlannedDq();
        }

        /// <summary>TI-9c (R-6 follow-up): the tool now plans a station end from the station's rope lane (LiftLineInput.StationRope),
        /// as the fit reads it. The same DQ line, same six pins, planned with the lane ropes (top 1235.40, base 925.96): the planner
        /// clears the base span itself, and the fit with the lane ropes keeps the built line (Kept, every member, nothing moved);
        /// with the extra split nodes too. Plus the pure anchor rule.</summary>
        private static void LanePlannedDq()
        {
            const string W = "tool/TI-9c";
            // The anchor rule: the lowest HV anchor within 30 m; sub-objects before the station; none -> the old node rule.
            var node = new float3(0f, 923.27f, 0f);
            float r = LiftLineInput.StationRope(null, new[] { new float3(2f, 925.96f, 1f), new float3(3f, 928.11f, -1f), new float3(80f, 900f, 0f) },
                                                node, 931.27f, out bool lane);
            Check(lane && math.abs(r - 925.96f) < 1e-3f, W, $"station anchors -> {r:F2} (lane {lane}), expected 925.96 (the lowest within 30 m)");
            r = LiftLineInput.StationRope(new[] { new float3(1f, 927f, 0f) }, new[] { new float3(2f, 925.96f, 1f) }, node, 931.27f, out lane);
            Check(lane && math.abs(r - 927f) < 1e-3f, W, $"a node sub-object anchor wins over the station's: {r:F2}");
            r = LiftLineInput.StationRope(null, new[] { new float3(80f, 900f, 0f) }, node, 931.27f, out lane);
            Check(!lane && math.abs(r - 931.27f) < 1e-3f, W, $"no anchor within reach -> the node rule: {r:F2}");

            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("detachable");
            LiftTerrainSampler g = Ground(DqBuilt, DqMoved);   // x = meters from the base
            const float L = 1065f, ropeTop = 1235.40f, ropeBase = 925.96f;
            var a = new float3(L, g(L, 0f), 0f);
            var b = new float3(0f, g(0f, 0f), 0f);
            var e = new LiftLineEdit(c, a, b, ropeTop, ropeBase);
            LiftTerrainSampler fromTop = (s, l) => g(L - s, l);
            e.Replan(fromTop);
            foreach (float pin in new[] { 20f, 135f, 475f, 680f, 900f, 950f })
                Check(e.AddPin(pin) == LiftLineEditResult.Done, W, $"pin {pin} refused");
            LiftTowerPlan plan = e.Replan(fromTop);
            LiftPlanSpan baseSpan = plan.Spans[plan.Spans.Length - 1];
            Check(plan.Clear, W, $"the lane-rope plan is not clear: base span {baseSpan.MinFloorMarginM:F1} m — {e.Describe()}");

            // Built (exact, and with extra split nodes), base first, then the fit with the lane ropes.
            List<LiftLineNode> emitted = e.Emit();
            var reg = new LiftToolLines();
            reg.Register("detachable", emitted);
            var inner = new List<float>();
            var members = new List<int>();
            for (int i = emitted.Count - 2; i >= 1; i--)
            {
                inner.Add(emitted[i].Position.x);
                members.Add(emitted[i].Member);
            }
            LiftTowerFit fit = new LiftTowerPlanner().Fit(g, L, ropeBase, ropeTop, c, inner, false, c.MaxSpanM, members);
            bool same = fit.Mode == LiftFitMode.Kept;
            for (int i = 0; i < inner.Count && same; i++)
                same = fit.Plan.Nodes[i + 1].Member == members[i] && math.abs(fit.Plan.Nodes[i + 1].AlongM - inner[i]) < 1e-3f;
            Check(same, W, $"the fit with lane ropes does not keep the lane-planned line: {fit.Mode}, min margin {fit.Plan.MinFloorMarginM:F1} m");

            List<float3> path = Built(emitted, L, new[] { 13f, 609f, 657f, 669f, 697f, 866f }, g);
            LiftToolMatch m = reg.MatchSplit(path);
            bool splitKept = false;
            if (m != null)
            {
                int n = path.Count;
                var al = new float[n - 2];
                var gr = new float[n - 2];
                for (int i = 0; i < n - 2; i++)
                {
                    al[i] = path[i + 1].x;
                    gr[i] = g(al[i], 0f);
                }
                int[] mm = (int[])m.Members.Clone();
                LiftToolLines.ChordMembers(c, al, gr, mm, null, L, ropeBase, ropeTop);
                LiftTowerFit f2 = new LiftTowerPlanner().Fit(g, L, ropeBase, ropeTop, c, al, false, c.MaxSpanM, mm, m.Extra);
                splitKept = f2.Mode == LiftFitMode.Kept;
                for (int i = 0; i < n - 2 && splitKept; i++)
                    splitKept = math.abs(f2.Plan.Nodes[i + 1].AlongM - al[i]) < 1e-3f && (m.Extra[i] || f2.Plan.Nodes[i + 1].Member == m.Members[i]);
            }
            Check(m != null && splitKept, W, "the lane-planned line with extra split nodes is not matched and Kept");
            Console.WriteLine($"  {W}: anchor rule ok; DQ 1065 m re-planned with the lane ropes (base 925.96, was 931.27) and the six pins: " +
                              $"{e.Describe()}; base span margin {baseSpan.MinFloorMarginM:F1} m; fit with lane ropes {fit.Mode}, members kept " +
                              $"{same}; with 6 extra splits matched + Kept {splitKept}");
        }

        // TI-10: the 22-vs-13 gondola (drawn from the base; pins 1075/1180/1235; log :1520-:1527).
        private static void SplitReplayGondola()
        {
            SplitReplay("tool/TI-10 GD 22 vs 13", "gondola", new[] { GdBuilt }, 1408f, false, 941.58f, 1471.15f,
                new[]
                {
                    (50f, 0, false), (190f, 1, false), (400f, 1, false), (590f, 2, false), (810f, 1, false), (1040f, 0, false),
                    (1075f, 1, true), (1135f, 1, false), (1180f, 0, true), (1195f, 0, false), (1235f, 0, true), (1400f, 2, false),
                },
                new[] { 27f, 41f, 175f, 488f, 551f, 743f, 1015f, 1122f, 1262f });
        }

        // TI-11: ordinary lines stay ordinary (the fit's old path): the vanilla equal split of the same corridor, a line with one
        // tool node 2 m off (diagnosed by BestPartial: the fit WARNs when it moves its pins), an extra node off the tool's line,
        // and a path whose end is not the tool's.
        private static void SplitOrdinaryLines()
        {
            const string W = "tool/TI-11";
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("detachable");
            LiftTerrainSampler g = Ground(DqBuilt);
            var towers = new[] { (20f, 0, true), (90f, 0, false), (135f, 1, true), (360f, 2, false), (475f, 1, true), (680f, 1, true),
                                 (900f, 0, true), (950f, 1, true), (980f, 3, false) };
            List<LiftLineNode> emitted = Emitted(c, 1065f, g, true, towers);
            var reg = new LiftToolLines();
            reg.Register("detachable", emitted);

            // (a) the vanilla net tool's equal split between the same two stations (5 x 213 m).
            var vanilla = new List<float3>();
            for (int i = 0; i <= 5; i++)
                vanilla.Add(new float3(1065f * i / 5f, g(1065f * i / 5f, 0f), 0f));
            Check(reg.Match(vanilla.GetRange(1, 4), out _, out _) == null && reg.MatchSplit(vanilla) == null, W, "a vanilla split line matched the tool line");
            Check(reg.BestPartial(vanilla, out int f0, out _) == null && f0 == 0, W, "BestPartial found tool nodes on a vanilla line");

            // (b) one tool node 2 m off: not the tool's plan any more (ordinary fit), BestPartial reports 8 of 9 with the pins.
            List<float3> path = Built(emitted, 1065f, new[] { 27f, 609f, 657f, 669f, 697f, 866f }, g);
            List<float3> off = new List<float3>(path);
            int k = off.FindIndex(p => math.abs(p.x - 590f) < 1f);   // the pinned tower at 475 m from the top
            off[k] += new float3(2f, 0f, 0f);
            Check(k > 0 && reg.MatchSplit(off) == null, W, "a line with a tool node 2 m off still matched");
            Check(reg.BestPartial(off, out int found, out int pins) != null && found == 8 && pins == 5, W,
                  $"BestPartial on the near miss: {found} found, {pins} pins (expected 8, 5)");

            // (c) an extra node 3 m beside the tool's line: another line, not a split of this one.
            List<float3> beside = new List<float3>(path);
            int x = beside.FindIndex(p => math.abs(p.x - 609f) < 1e-3f);
            beside[x] += new float3(0f, 0f, 3f);
            Check(reg.MatchSplit(beside) == null, W, "an extra node 3 m off the line matched");

            // (d) the same nodes but the line continues past the tool's end (a longer line through it): the ends differ.
            List<float3> longer = new List<float3>(path) { new float3(1200f, g(1065f, 0f), 0f) };
            Check(reg.MatchSplit(longer) == null, W, "a longer line through the tool's nodes matched");

            // (e) the unsplit tool line itself still matches exactly (old path, unchanged).
            List<float3> exact = Built(emitted, 1065f, new float[0], g);
            Check(reg.Match(exact.GetRange(1, exact.Count - 2), out _, out _) != null, W, "the unsplit tool line no longer matches exactly");
            Console.WriteLine($"  {W}: vanilla 5 x 213 m split -> ordinary; tool node 2 m off -> ordinary, BestPartial {found}/9 with {pins} pins " +
                              "(fit WARNs if Moved); extra 3 m off -> ordinary; longer line -> ordinary; unsplit tool line -> exact Match");
        }

        // TI-12: ChordMembers gives an extra the member whose rope is nearest the planned chord (tie -> taller), touches no tool
        // node, and keeps the current member within the hysteresis.
        private static void SplitChordMembers()
        {
            const string W = "tool/TI-12";
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("detachable");   // DQ ladder ropes 2 m apart
            const float G0 = 1000f;
            float rope0 = G0 + c.RopeAboveBase(0);
            // Between two DQ20 tool towers on flat ground the chord is the DQ20 rope: the extra gets DQ20, the tool nodes stay.
            int[] mem = { 5, -1, 5 };
            LiftToolLines.ChordMembers(c, new[] { 100f, 200f, 300f }, new[] { G0, G0, G0 }, mem, null, 400f, rope0, rope0);
            Check(mem[0] == 5 && mem[1] == 5 && mem[2] == 5, W, $"extra between two DQ20: {mem[1]}");
            // Station (DQ10 rope) to a DQ20 at 200 m, extra at 100 m: the chord is DQ10 rope + 5 m -> DQ14 (+4) and DQ16 (+6) are 1 m
            // off each: a tie, the taller wins (never a dip below the planned rope).
            mem = new[] { -1, 5 };
            LiftToolLines.ChordMembers(c, new[] { 100f, 200f }, new[] { G0, G0 }, mem, null, 400f, rope0, rope0);
            Check(mem[0] == 3, W, $"extra under a half chord: {mem[0]} (expected DQ16)");
            // Nearest: the chord 0.6 m under the DQ14 rope picks DQ14; 1.4 m under picks DQ12.
            float rope2 = G0 + c.RopeAboveBase(2);
            mem = new[] { 2, -1 };
            LiftToolLines.ChordMembers(c, new[] { 100f, 150f }, new[] { G0, G0 }, mem, null, 200f, rope0, rope2 - 1.2f);
            Check(mem[1] == 2, W, $"chord 0.6 m under DQ14: {mem[1]} (expected DQ14)");
            mem = new[] { 2, -1 };
            LiftToolLines.ChordMembers(c, new[] { 100f, 150f }, new[] { G0, G0 }, mem, null, 200f, rope0, rope2 - 2.8f);
            Check(mem[1] == 1, W, $"chord 1.4 m under DQ14: {mem[1]} (expected DQ12)");
            // Hysteresis: the chord 1.2 m under DQ14 picks DQ12 (0.8 m off) fresh; a node already DQ14 (1.2 <= 0.8 + 0.5) stays.
            mem = new[] { 2, -1 };
            LiftToolLines.ChordMembers(c, new[] { 100f, 150f }, new[] { G0, G0 }, mem, new[] { 2, 2 }, 200f, rope0, rope2 - 2.4f);
            Check(mem[1] == 2, W, $"hysteresis: a DQ14 within {LiftToolLines.ChordHystM} m stays: {mem[1]}");
            // Beyond the hysteresis (chord 1.8 m under DQ14: DQ12 0.2 m off vs 1.8 m) the current member goes.
            mem = new[] { 2, -1 };
            LiftToolLines.ChordMembers(c, new[] { 100f, 150f }, new[] { G0, G0 }, mem, new[] { 2, 2 }, 200f, rope0, rope2 - 3.6f);
            Check(mem[1] == 1, W, $"past the hysteresis: {mem[1]} (expected DQ12)");
        }

        private static LiftLineEdit Flat(string type, float length, float ground = 1000f)
        {
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For(type);
            var a = new float3(0f, ground, 0f);
            var b = new float3(length, ground, 0f);
            var e = new LiftLineEdit(c, a, b, ground + c.RopeAboveBase(0), ground + c.RopeAboveBase(0));
            e.Replan((s, l) => ground);
            return e;
        }

        private static readonly LiftTerrainSampler FlatGround = (s, l) => 1000f;

        // TI-1
        private static void RightClickTable()
        {
            const string W = "tool/TI-1";
            Check(LiftLineInput.RightClick(LiftLineStep.PickBase, false, 0) == LiftLineBack.CloseTool, W, "PickBase must close the tool");
            Check(LiftLineInput.RightClick(LiftLineStep.PickTop, false, 0) == LiftLineBack.DropBase, W, "PickTop must drop the first end");
            Check(LiftLineInput.RightClick(LiftLineStep.Edit, false, 0) == LiftLineBack.DropTop, W, "Edit, no edits, must drop the second end");
            Check(LiftLineInput.RightClick(LiftLineStep.Edit, false, 3) == LiftLineBack.UndoEdit, W, "Edit with edits must undo");
            Check(LiftLineInput.RightClick(LiftLineStep.Edit, true, 3) == LiftLineBack.CancelDrag, W, "a drag must be cancelled first");
            Check(LiftLineInput.RightClick(LiftLineStep.Edit, true, 0) == LiftLineBack.CancelDrag, W, "a drag must be cancelled first (no edits)");
            Check(LiftLineInput.RightClick(LiftLineStep.Building, false, 2) == LiftLineBack.None, W, "building must ignore right click");
            // Never the old behavior: no single right click drops a line that has edits.
            foreach (LiftLineStep s in (LiftLineStep[])Enum.GetValues(typeof(LiftLineStep)))
                for (int d = 1; d < 4; d++)
                    Check(LiftLineInput.RightClick(s, false, d) != LiftLineBack.DropTop &&
                          LiftLineInput.RightClick(s, false, d) != LiftLineBack.DropBase || s == LiftLineStep.PickTop,
                          W, $"step {s} depth {d}: a right click dropped an end while edits remain");
            Check(LiftLineInput.After(LiftLineStep.Edit, LiftLineBack.DropTop) == LiftLineStep.PickTop, W, "DropTop -> PickTop");
            Check(LiftLineInput.After(LiftLineStep.PickTop, LiftLineBack.DropBase) == LiftLineStep.PickBase, W, "DropBase -> PickBase");
            Check(LiftLineInput.After(LiftLineStep.Edit, LiftLineBack.UndoEdit) == LiftLineStep.Edit, W, "undo stays in Edit");
        }

        // TI-2: the tool's sequence, driven only through LiftLineInput and LiftLineEdit (what LiftLineToolSystem.StepBack does).
        private static void WalkBack()
        {
            const string W = "tool/TI-2";
            LiftLineEdit e = Flat("detachable", 900f);
            LiftLineStep step = LiftLineStep.Edit;   // two ends clicked
            int towers0 = e.Plan.LineTowers;
            Check(e.AddPin(200f) == LiftLineEditResult.Done, W, "add 200");
            e.Replan(FlatGround);
            Check(e.AddPin(450f) == LiftLineEditResult.Done, W, "add 450");
            e.Replan(FlatGround);
            Check(e.MoveTower(450f, 500f) == LiftLineEditResult.Done, W, "move 450 -> 500");
            e.Replan(FlatGround);
            Check(e.UndoDepth == 3, W, "undo depth after three edits: " + e.UndoDepth);
            var seen = new List<LiftLineBack>();
            bool closed = false;
            for (int guard = 0; guard < 10 && !closed; guard++)
            {
                LiftLineBack back = LiftLineInput.RightClick(step, false, step == LiftLineStep.Edit ? e.UndoDepth : 0);
                seen.Add(back);
                if (back == LiftLineBack.UndoEdit)
                {
                    e.Undo();
                    e.Replan(FlatGround);
                }
                closed = back == LiftLineBack.CloseTool;
                step = LiftLineInput.After(step, back);
            }
            string got = string.Join(",", seen);
            Check(got == "UndoEdit,UndoEdit,UndoEdit,DropTop,DropBase,CloseTool", W, "walk back: " + got);
            Check(e.Pins.Count == 0 && e.Plan.LineTowers == towers0, W, "after three undos the line is the planner's own again");
        }

        private static string State(LiftLineEdit e) => string.Join("/", e.Pins) + "|" + string.Join("/", e.Removed);

        // TI-3
        private static void UndoRestores()
        {
            const string W = "tool/TI-3";
            LiftLineEdit e = Flat("gondola", 1400f);
            string s0 = State(e);
            Check(!e.Undo(), W, "undo with nothing to undo must return false");

            Check(e.AddPin(300f) == LiftLineEditResult.Done, W, "add");
            e.Replan(FlatGround);
            string s1 = State(e);
            Check(e.AddPin(e.C.MinSpanM * 0.5f) == LiftLineEditResult.TooCloseToStation && e.UndoDepth == 1, W,
                  "a refused add pushed an undo step");
            Check(e.AddPin(300f) == LiftLineEditResult.Unchanged && e.UndoDepth == 1, W, "an unchanged add pushed an undo step");

            Check(e.MoveTower(300f, 400f) == LiftLineEditResult.Done, W, "move");
            e.Replan(FlatGround);
            string s2 = State(e);

            // Remove a FREE tower: every other tower gets pinned; one undo must give back exactly s2.
            float free = float.NaN;
            for (int i = 1; i < e.Plan.Nodes.Length - 1; i++)
                if (!e.IsPinned(e.Plan.Nodes[i].AlongM)) { free = e.Plan.Nodes[i].AlongM; break; }
            Check(!float.IsNaN(free), W, "no free tower to remove on the gondola line");
            if (!float.IsNaN(free))
            {
                Check(e.RemoveTower(free) == LiftLineEditResult.Done, W, "remove free");
                Check(e.Removed.Count == 1, W, "remove free did not record the spot");
                e.Replan(FlatGround);
                e.ClearPins();
                Check(e.Pins.Count == 0 && e.Removed.Count == 0, W, "clear");
                Check(e.Undo() && State(e) != s2 && e.Removed.Count == 1, W, "undo of clear did not restore the pins and the removed spot");
                Check(e.Undo() && State(e) == s2, W, "undo of the free-tower remove: " + State(e) + " vs " + s2);
            }
            Check(e.Undo() && State(e) == s1, W, "undo of the move: " + State(e) + " vs " + s1);
            Check(e.Undo() && State(e) == s0, W, "undo of the add: " + State(e) + " vs " + s0);
            Check(e.UndoDepth == 0 && !e.Undo(), W, "undo stack not empty at the start state");

            // Unpin (remove a PINNED tower) is one step too.
            Check(e.AddPin(600f) == LiftLineEditResult.Done, W, "add 600");
            e.Replan(FlatGround);
            Check(e.RemoveTower(600f) == LiftLineEditResult.Done && e.Pins.Count == 0, W, "unpin");
            Check(e.Undo() && e.IsPinned(600f), W, "undo of the unpin");
        }

        // TI-4
        private static void Prompts()
        {
            const string W = "tool/TI-4";
            string[] types = { "", "fixedGrip", "detachable", "gondola", "tram" };
            string[] british = { "colour", "metre", "centre", "grey", "behaviour", "cancelled" };
            foreach (string t in types)
            {
                string pb = LiftLineInput.Prompt(LiftLineStep.PickBase, t, false);
                string pt = LiftLineInput.Prompt(LiftLineStep.PickTop, t, false);
                string ed = LiftLineInput.Prompt(LiftLineStep.Edit, t, false);
                string dr = LiftLineInput.Prompt(LiftLineStep.Edit, t, true);
                string bu = LiftLineInput.Prompt(LiftLineStep.Building, t, false);
                foreach (string p in new[] { pb, pt, ed, dr, bu })
                {
                    Check(!string.IsNullOrWhiteSpace(p), W, $"type '{t}': an empty hint");
                    foreach (string b in british)
                        Check(p.IndexOf(b, StringComparison.OrdinalIgnoreCase) < 0, W, $"British spelling '{b}' in \"{p}\"");
                }
                Check(new HashSet<string> { pb, pt, ed, dr, bu }.Count == 5, W, $"type '{t}': two steps share a hint");
                Check(pb.Contains("right click: close"), W, "PickBase hint must say right click closes: " + pb);
                Check(pt.Contains("right click: back"), W, "PickTop hint must say right click steps back: " + pt);
                Check(ed.Contains("right click: undo") && ed.Contains("Enter: build") && ed.Contains("Esc"), W, "Edit hint: " + ed);
                Check(dr.Contains("right click"), W, "drag hint must say how to cancel: " + dr);
                Check(pb.Contains("open ground") == (t.Length > 0), W, $"open ground offered only with a type ('{t}'): {pb}");
            }
        }

        // TI-5
        private static void DragOff()
        {
            const string W = "tool/TI-5";
            Check(!LiftLineInput.DragRemoves(0f, 15f) && !LiftLineInput.DragRemoves(15f, 15f) && !LiftLineInput.DragRemoves(-14.9f, 15f),
                  W, "on the line must move");
            Check(LiftLineInput.DragRemoves(15.1f, 15f) && LiftLineInput.DragRemoves(-40f, 15f), W, "off the line must remove");
        }

        // TI-6
        private static void MinLength()
        {
            const string W = "tool/TI-6";
            foreach (string t in new[] { "fixedGrip", "detachable", "gondola", "tram" })
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(t);
                Check(Math.Abs(LiftLineInput.MinLengthM(c, true) - 2f * c.MinSpanM) < 1e-4f, W, t + ": station-to-station minimum changed");
                float sec = LiftLineInput.MinLengthM(c, false);
                Check(Math.Abs(sec - 2f * c.StepM) < 1e-4f, W, t + ": section minimum is not 2 x StepM");
                // A short section between two loose ends (tower nodes) plans clear with no tower.
                float len = math.max(sec, 3f * c.StepM);
                LiftLineEdit e = Flat(t, len);
                Check(e.Plan != null && e.Plan.Nodes.Length >= 2 && e.Plan.Clear, W, $"{t}: a {len:F0} m section did not plan clear: {e.Describe()}");
                Check(e.Plan.LineTowers == 0, W, $"{t}: a {len:F0} m flat section got {e.Plan.LineTowers} tower(s)");
            }
        }

        // TI-7
        private static void DefaultPlan()
        {
            const string W = "tool/TI-7";
            foreach (string t in new[] { "fixedGrip", "detachable", "gondola", "tram" })
            {
                LiftLineEdit e = Flat(t, t == "tram" ? 1800f : t == "fixedGrip" ? 500f : 1200f);
                List<LiftLineNode> n = e.Emit();
                Check(n.Count == e.Plan.Nodes.Length, W, t + ": emitted node count != plan");
                Check(n[0].Kind == LiftLineNodeKind.Station && n[n.Count - 1].Kind == LiftLineNodeKind.Station, W, t + ": ends are not stations");
                bool allFree = true;
                for (int i = 1; i < n.Count - 1; i++)
                    allFree &= n[i].Kind == LiftLineNodeKind.Free;
                Check(allFree && e.UndoDepth == 0 && e.Pins.Count == 0, W, t + ": the default plan carries edits");
                for (int s = 0; s < e.Plan.Spans.Length; s++)
                    Check(e.Plan.Spans[s].LengthM <= e.C.MaxSpanM + 1e-3f, W, $"{t}: span {s} {e.Plan.Spans[s].LengthM:F0} m over the max");
            }
        }

        // TI-8
        private static void TypeNames()
        {
            const string W = "tool/TI-8";
            Check(LiftLineInput.TypeName("fixedGrip") == "Fixed-grip" && LiftLineInput.TypeName("detachable") == "Detachable" &&
                  LiftLineInput.TypeName("gondola") == "Gondola" && LiftLineInput.TypeName("tram") == "Tram", W, "type names");
            Check(LiftLineInput.TypeName("nope") == "Lift" && LiftLineInput.TypeName(null) == "Lift", W, "unknown type name");
        }
    }
}
