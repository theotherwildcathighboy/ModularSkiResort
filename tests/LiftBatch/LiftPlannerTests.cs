// LiftPlannerTests — T48 step 1 (docs/research/field/2026-10-19/tower-planner-build-1.md): the pure LiftTowerPlanner on the three
// measured profiles of the 2026-10-19 tower test and on synthetic ground. Where-tags `planner/…`.
//   LP-0  constants: every catalog lift type has planner constants; ladders ascend; the shortest member's rope = catalog AxleM; every
//         member's rope = its manifest axle (FG 7.04/11.04/15.04/19.04, DQ 8..18, GD 10/14/18, tram stubs 26.25/41.25/56.25);
//   LP-1  DQ gully replay (log :629-:649): 2 line towers, one of them tall (>= DQ16), every span clear; and the same with the
//         analysis' assumed 6.0 m rope-to-ground minimum (analysis-towers.md §3);
//   LP-2  tram pitch replay (log :666-:706): 1 line tower, every span clear;
//   LP-3  FG dip replay (log :786-:801): 1 line tower, every span clear;
//   LP-4  flat ground: infill only, at the typical span, shortest members;
//   LP-5  a 40 m wall, up and down: max rope height respected everywhere and the floor clear;
//   LP-6  side-hill: the high side governs (a taller member than on flat ground), side samples at ±half width, SideHill flag;
//   LP-7  breakover: a node flagged Breakover at the convex knee, on its uphill side;
//   LP-8  idempotence: re-planning every plan with its own nodes as seeds changes no position, member or rope; planning twice is
//         identical (determinism);
//   LP-9  the "Towers [plan]" block: header + one line per node + one per span, in the "Towers [nodes]" layout.
//   Step 3 (fixed node count, LiftTowerPlanner.Fit; field/2026-10-19-evening/t48-step3-build.md):
//   LP-11 the 21:14 FG line (evening log :1031): violated before (5 x FG8), clear after with its 5 nodes, spans <= 60 m, both
//         station-rope models (node + 7.04; ET68 5.84 / ET62 5.24);
//   LP-12 the three morning lines at the engine's equal split: FG dip and tram clear; DQ gully Short (needs 4 towers, has 3),
//         much better than before;
//   LP-13 a fitted line re-fits to itself (Kept, same nodes and members: the system's write-once guarantee);
//   LP-14 Short when the line has too few nodes; allowMove false never moves;
//   LP-15 engine numbers: the vanilla ladder picks the planned member at E = TopM[m] - TopM[0] with score 1, top-anchored placement
//         stands it on the ground, ground + E + AxleM = the planner's rope, E within the net's elevation cap, the edge curve formula.
// Replay terrain: ground at each node = the log's `terrain`; between nodes the four chord samples at 20/40/60/80 % give
// ground = lerp(node y, next node y, t) - chord clearance (the diag's chord runs between NODE positions, LiftTowerDiagSystem
// DescribeNode); linear in between. The log has no cross-slope, so the replay sampler ignores the lateral offset.

using System;
using System.Collections.Generic;
using System.Text;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    public static class LiftPlannerTests
    {
        private static Func<bool, string, string, bool> s_Check;
        private static readonly List<string> s_Report = new List<string>();
        private static readonly LiftTowerPlanner s_Planner = new LiftTowerPlanner();

        /// <summary>The case -> got lines (printed by Program after the run; the build note's test table is made from them).</summary>
        public static IReadOnlyList<string> Report => s_Report;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            s_Report.Clear();
            Lp0Constants();
            Lp1DetachableGully();
            Lp2TramPitch();
            Lp3FixedGripDip();
            Lp4Flat();
            Lp5Wall();
            Lp6SideHill();
            Lp7Breakover();
            Lp9LogBlock();
            Lp10LongLines();
            Lp11Line2114();
            Lp12FieldFits();
            Lp14Short();
            Lp15EngineNumbers();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);

        // ════════════════════════════════════════════════════════════════════════════════════════
        // Replay data (docs/research/field/2026-10-19/logs/SkiResortMod.Mod.log)
        // ════════════════════════════════════════════════════════════════════════════════════════

        private struct LogNode
        {
            public float SpanXz;      // to the next node (0 for the last)
            public float Y;           // node y
            public float Terrain;     // terrain under the node
            public float[] Chord;     // chord clearance at 20/40/60/80 % (null for the last)
        }

        private static LogNode N(float span, float y, float terrain, params float[] chord) =>
            new LogNode { SpanXz = span, Y = y, Terrain = terrain, Chord = chord.Length == 0 ? null : chord };

        // Detachable across the gully: Towers [nodes] block at log :628 (nodes :629 :634 :639 :644 :649, spans :630 :635 :640 :645).
        private static readonly LogNode[] DqGully =
        {
            N(84f, 1187.80f, 1187.80f, -3.8f, -3.8f, -3.7f, -2.1f),
            N(84f, 1154.07f, 1154.06f, 6.0f, 13.6f, 14.7f, 8.8f),
            N(84f, 1140.77f, 1140.77f, -1.7f, -3.8f, -3.7f, -3.7f),
            N(84f, 1177.12f, 1177.01f, 2.1f, 0.5f, 0.0f, -3.7f),
            N(0f, 1228.12f, 1228.12f),
        };

        // Tram up the pitch: block at log :665 (nodes :666 :671 :676 :681 :686 :691 :696 :701 :706, spans one line below each node).
        private static readonly LogNode[] TramPitch =
        {
            N(220f, 1044.89f, 1044.57f, 4.0f, 15.8f, 13.5f, 18.9f),
            N(48f, 1149.21f, 1149.04f, -0.8f, -1.7f, -1.6f, 0.0f),
            N(84f, 1175.44f, 1174.90f, -0.3f, -1.6f, -6.7f, -7.5f),
            N(40f, 1225.45f, 1225.02f, 2.3f, 5.2f, 6.0f, 3.5f),
            N(336f, 1234.50f, 1234.27f, 17.1f, 22.1f, 23.6f, 17.5f),
            N(68f, 1372.19f, 1371.87f, 1.4f, 5.2f, 5.6f, 2.4f),
            N(222f, 1403.67f, 1403.18f, 17.8f, 23.9f, 28.6f, 9.6f),
            N(142f, 1589.09f, 1588.57f, 3.8f, -6.9f, -0.1f, 6.7f),
            N(0f, 1725.76f, 1724.82f),
        };

        // Fixed grip over the dip: block at log :785 (nodes :786 :791 :796 :801, spans :787 :792 :797).
        private static readonly LogNode[] FgDip =
        {
            N(43f, 1125.24f, 1125.03f, -1.6f, -2.2f, -1.7f, -0.9f),
            N(43f, 1104.85f, 1104.84f, 4.9f, 7.1f, 6.2f, 3.7f),
            N(43f, 1108.44f, 1108.43f, -0.1f, -0.4f, -0.6f, -0.8f),
            N(0f, 1132.01f, 1132.01f),
        };

        private sealed class Profile
        {
            public readonly List<float> S = new List<float>();
            public readonly List<float> G = new List<float>();
            public float Length => S[S.Count - 1];
            public float Start => G[0];
            public float End => G[G.Count - 1];

            public float At(float s)
            {
                if (s <= S[0])
                    return G[0];
                for (int i = 1; i < S.Count; i++)
                {
                    if (s <= S[i])
                    {
                        float t = (s - S[i - 1]) / math.max(S[i] - S[i - 1], 1e-6f);
                        return G[i - 1] + (G[i] - G[i - 1]) * t;
                    }
                }
                return G[G.Count - 1];
            }
        }

        private static Profile Reconstruct(LogNode[] nodes)
        {
            var p = new Profile();
            float s = 0f;
            float[] f = { 0.2f, 0.4f, 0.6f, 0.8f };
            for (int i = 0; i < nodes.Length; i++)
            {
                p.S.Add(s);
                p.G.Add(nodes[i].Terrain);
                if (nodes[i].Chord == null)
                    break;
                for (int q = 0; q < 4; q++)
                {
                    float y = nodes[i].Y + (nodes[i + 1].Y - nodes[i].Y) * f[q];
                    p.S.Add(s + f[q] * nodes[i].SpanXz);
                    p.G.Add(y - nodes[i].Chord[q]);
                }
                s += nodes[i].SpanXz;
            }
            return p;
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>Plan between two stations whose rope sits on the shortest member above the end ground (the field lines had a
        /// shortest tower on both end nodes); then run the idempotence check (LP-8) on the result.</summary>
        private static LiftTowerPlan PlanAndReplan(string where, LiftTerrainSampler terrain, float length, LiftTowerPlanConstants c,
                                                   float groundA, float groundB, float? ropeA = null, float? ropeB = null)
        {
            float ra = ropeA ?? groundA + c.RopeAboveBase(0);
            float rb = ropeB ?? groundB + c.RopeAboveBase(0);
            LiftTowerPlan plan = s_Planner.Plan(terrain, length, ra, rb, c);
            LiftTowerPlan again = s_Planner.Plan(terrain, length, ra, rb, c);
            Check(Same(plan, again), "planner/" + where + "/determinism", "planning twice gave different nodes");
            LiftTowerPlan replan = s_Planner.Plan(terrain, length, ra, rb, c, plan.Nodes);
            Check(Same(plan, replan), "planner/" + where + "/idempotence",
                  "re-planning the plan's own nodes changed it: " + Describe(plan) + " -> " + Describe(replan));
            for (int s = 0; s < plan.Spans.Length; s++)
                Check(plan.Spans[s].LengthM <= c.MaxSpanM + 1e-3f, "planner/" + where + "/max-span",
                      "span " + (s + 1) + " is " + plan.Spans[s].LengthM.ToString("F0") + " m > max " + c.MaxSpanM);
            return plan;
        }

        private static bool Same(LiftTowerPlan a, LiftTowerPlan b)
        {
            if (a.Nodes.Length != b.Nodes.Length)
                return false;
            for (int i = 0; i < a.Nodes.Length; i++)
            {
                if (math.abs(a.Nodes[i].AlongM - b.Nodes[i].AlongM) > 1e-3f || a.Nodes[i].Member != b.Nodes[i].Member ||
                    math.abs(a.Nodes[i].RopeM - b.Nodes[i].RopeM) > 1e-3f)
                    return false;
            }
            return true;
        }

        internal static string Describe(LiftTowerPlan p)
        {
            var sb = new StringBuilder();
            sb.Append(p.LineTowers).Append(" line tower(s)");
            for (int i = 1; i < p.Nodes.Length - 1; i++)
            {
                LiftPlanNode n = p.Nodes[i];
                sb.Append(i == 1 ? ": " : ", ").Append(p.Constants.Ladder[n.Member].Name.Replace("SkiLiftTower", "").Replace("SkiTramTower", "T"))
                  .Append(" @").Append(n.AlongM.ToString("F0")).Append(" m [").Append(LiftTowerPlan.FlagText(n.Flags)).Append(']');
            }
            float maxAbove = float.NegativeInfinity;
            for (int s = 0; s < p.Spans.Length; s++)
                maxAbove = math.max(maxAbove, p.Spans[s].MaxRopeAboveM);
            sb.Append("; spans ");
            for (int s = 0; s < p.Spans.Length; s++)
                sb.Append(s == 0 ? string.Empty : "/").Append(p.Spans[s].LengthM.ToString("F0"));
            sb.Append(" m; min floor margin ").Append(LiftTowerPlan.Sg(p.MinFloorMarginM)).Append(" m; max rope above ground ")
              .Append(maxAbove.ToString("F1")).Append(" m; ").Append(p.Clear ? "clear" : "VIOLATED");
            for (int s = 0; s < p.Spans.Length; s++)
                if (p.Spans[s].FloorViolated || p.Spans[s].HeightViolated)
                    sb.Append(" [span ").Append(s + 1).Append(p.Spans[s].FloorViolated ? " floor " + LiftTowerPlan.Sg(p.Spans[s].MinFloorMarginM) : " height " + p.Spans[s].MaxRopeAboveM.ToString("F1")).Append(" at ").Append(p.Spans[s].MinFloorMarginAtM.ToString("F0")).Append(" m]");
            return sb.ToString();
        }

        private static void AddReport(string caseName, string expected, LiftTowerPlan p)
        {
            s_Report.Add(caseName + " | " + expected + " | " + Describe(p));
        }

        private static int MaxMember(LiftTowerPlan p)
        {
            int m = -1;
            for (int i = 0; i < p.Nodes.Length; i++)
                m = math.max(m, p.Nodes[i].Member);
            return m;
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // LP-0
        // ════════════════════════════════════════════════════════════════════════════════════════

        private static void Lp0Constants()
        {
            const string W = "planner/LP-0";
            var axles = new Dictionary<string, float[]>
            {
                { "fixedGrip", new[] { 7.04f, 11.04f, 15.04f, 19.04f } },        // SkiLiftTowerFG8O/12O/16O/20O manifests "axle"
                { "detachable", new[] { 8f, 10f, 12f, 14f, 16f, 18f } },         // SkiLiftTowerDQ10O..DQ20O manifests "axle"
                { "gondola", new[] { 10f, 14f, 18f } },                          // SkiLiftTowerGD12O/16O/20O manifests "axle"
                { "tram", new[] { 26.25f, 41.25f, 56.25f } },                    // SkiTramTower30A/45A/60A manifests stubs pos y
            };
            for (int t = 0; t < SkiCatalog.Lifts.Count; t++)
            {
                LiftTypeDef def = SkiCatalog.Lifts[t];
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(def);
                if (!Check(c != null, W, "no planner constants for lift type " + def.Id))
                    continue;
                for (int m = 1; m < c.Ladder.Length; m++)
                    Check(c.Ladder[m].TopM > c.Ladder[m - 1].TopM, W, def.Id + " ladder not ascending at " + c.Ladder[m].Name);
                Check(math.abs(c.RopeAboveBase(0) - def.AxleM) < 1e-4f, W, def.Id + " shortest rope " + c.RopeAboveBase(0) + " != catalog AxleM " + def.AxleM);
                Check(math.abs(c.CarrierDropM - def.HangM) < 1e-4f, W, def.Id + " drop != catalog HangM");
                if (axles.TryGetValue(def.Id, out float[] want) && Check(want.Length == c.Ladder.Length, W, def.Id + " ladder length"))
                {
                    for (int m = 0; m < want.Length; m++)
                        Check(math.abs(c.RopeAboveBase(m) - want[m]) < 2e-3f, W,
                              def.Id + " " + c.Ladder[m].Name + " rope " + c.RopeAboveBase(m).ToString("F3") + " != manifest axle " + want[m]);
                }
                Check(c.MaxRopeAboveGroundM <= 0f || c.RopeAboveBase(c.Ladder.Length - 1) <= c.MaxRopeAboveGroundM, W,
                      def.Id + " tallest member's rope above the max rope height");
            }
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // LP-1..LP-3 replays
        // ════════════════════════════════════════════════════════════════════════════════════════

        private static void Lp1DetachableGully()
        {
            const string W = "planner/LP-1";
            Profile p = Reconstruct(DqGully);
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("detachable");
            LiftTowerPlan plan = PlanAndReplan("LP-1", (s, l) => p.At(s), p.Length, c, p.Start, p.End);
            AddReport("LP-1 DQ gully (log :628), catalog drop 3.3 + floor 3", "2 line towers, one tall (>= DQ16), clear", plan);
            Check(plan.LineTowers == 2, W, "line towers " + plan.LineTowers + ", expected 2: " + Describe(plan));
            Check(MaxMember(plan) >= 3, W, "no tall member (>= DQ16): " + Describe(plan));
            Check(plan.Clear, W, "a span is violated: " + Describe(plan));

            // The analysis' assumption: rope >= 6.0 m above the ground (2.4-2.6 m carrier + ~3 m floor; analysis-towers.md §3).
            LiftTowerPlan plan6 = PlanAndReplan("LP-1b", (s, l) => p.At(s), p.Length, c.With(floorM: 6.0f - c.CarrierDropM), p.Start, p.End);
            AddReport("LP-1b DQ gully, analysis' rope >= 6.0 m", "2 line towers, one taller than DQ10, clear (analysis §3 row L)", plan6);
            Check(plan6.LineTowers == 2 && plan6.Clear && MaxMember(plan6) >= 1, W + "b", Describe(plan6));
        }

        private static void Lp2TramPitch()
        {
            const string W = "planner/LP-2";
            Profile p = Reconstruct(TramPitch);
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("tram");
            LiftTowerPlan plan = PlanAndReplan("LP-2", (s, l) => p.At(s), p.Length, c, p.Start, p.End);
            AddReport("LP-2 tram pitch (log :665)", "1 line tower, clear", plan);
            Check(plan.LineTowers == 1, W, "line towers " + plan.LineTowers + ", expected 1: " + Describe(plan));
            Check(plan.Clear, W, "a span is violated: " + Describe(plan));
        }

        private static void Lp3FixedGripDip()
        {
            const string W = "planner/LP-3";
            Profile p = Reconstruct(FgDip);
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("fixedGrip");
            LiftTowerPlan plan = PlanAndReplan("LP-3", (s, l) => p.At(s), p.Length, c, p.Start, p.End);
            AddReport("LP-3 FG dip (log :785)", "1 line tower, clear", plan);
            Check(plan.LineTowers == 1, W, "line towers " + plan.LineTowers + ", expected 1: " + Describe(plan));
            Check(plan.Clear, W, "a span is violated: " + Describe(plan));
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // LP-4..LP-7 synthetic
        // ════════════════════════════════════════════════════════════════════════════════════════

        private static void Lp4Flat()
        {
            const string W = "planner/LP-4";
            foreach (string id in new[] { "fixedGrip", "detachable", "tram" })
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(id);
                float length = id == "tram" ? 2400f : 5f * c.TypicalSpanM;
                LiftTowerPlan plan = PlanAndReplan("LP-4/" + id, (s, l) => 1000f, length, c, 1000f, 1000f);
                int spans = (int)math.round(length / c.TypicalSpanM);
                AddReport("LP-4 flat " + id + " " + length.ToString("F0") + " m", spans + " spans of " + c.TypicalSpanM.ToString("F0") + " m, shortest members, infill only", plan);
                Check(plan.Spans.Length == spans, W, id + ": " + plan.Spans.Length + " spans, expected " + spans + ": " + Describe(plan));
                for (int s = 0; s < plan.Spans.Length; s++)
                    Check(math.abs(plan.Spans[s].LengthM - c.TypicalSpanM) <= c.StepM + 1e-3f, W,
                          id + " span " + (s + 1) + " " + plan.Spans[s].LengthM.ToString("F0") + " m, expected " + c.TypicalSpanM);
                // FG: m_Hanging 0.03 sags a 60 m span 1.8 m, so two FG8s (rope 7.04) leave the chair 2.84 m up (< 3 m floor): every
                // other FG tower is an FG12 on flat ground. DQ / tram (0.004) keep the shortest member.
                int maxMember = id == "fixedGrip" ? 1 : 0;
                for (int i = 1; i < plan.Nodes.Length - 1; i++)
                    Check(plan.Nodes[i].Member <= maxMember && plan.Nodes[i].Flags == LiftPlanNodeFlags.Infill, W,
                          id + " node " + (i + 1) + " member " + plan.Nodes[i].Member + " flags " + plan.Nodes[i].Flags);
                Check(plan.Clear, W, id + " violated on flat ground");
            }
            // The FG sag finding itself: an FG8-FG8 60 m span on flat ground is below the 3 m floor, a 54 m one is not.
            LiftTowerPlanConstants fg = LiftTowerPlanConstants.For("fixedGrip");
            float chair60 = fg.RopeAboveBase(0) - 60f * fg.SagRatio - fg.CarrierDropM;
            Check(chair60 < fg.FloorM && fg.RopeAboveBase(0) - 54f * fg.SagRatio - fg.CarrierDropM >= fg.FloorM, W,
                  "FG sag arithmetic: chair at mid-span " + chair60.ToString("F2") + " m");
            s_Report.Add("LP-4 FG sag finding | FG8-FG8 60 m on flat: chair " + chair60.ToString("F2") + " m above ground (floor 3) | " +
                         "FG8 needs spans <= " + ((fg.RopeAboveBase(0) - fg.CarrierDropM - fg.FloorM) / fg.SagRatio).ToString("F0") + " m");
        }

        private static void Lp5Wall()
        {
            const string W = "planner/LP-5";
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("fixedGrip");
            // 0-150 m flat at 1000, a 40 m wall over 150-160 m, 160-300 m flat at 1040.
            Func<float, float> wall = s => s <= 150f ? 1000f : s >= 160f ? 1040f : 1000f + 4f * (s - 150f);
            LiftTowerPlan up = PlanAndReplan("LP-5/up", (s, l) => wall(s), 300f, c, 1000f, 1040f);
            AddReport("LP-5 FG up a 40 m wall", "rope <= 30 m above ground everywhere, floor clear", up);
            LiftTowerPlan down = PlanAndReplan("LP-5/down", (s, l) => wall(300f - s), 300f, c, 1040f, 1000f);
            AddReport("LP-5 FG down a 40 m wall", "rope <= 30 m above ground everywhere, floor clear", down);
            foreach (LiftTowerPlan plan in new[] { up, down })
            {
                Check(plan.Clear, W, "violated: " + Describe(plan));
                for (int s = 0; s < plan.Spans.Length; s++)
                    Check(plan.Spans[s].MaxRopeAboveM <= c.MaxRopeAboveGroundM + 0.02f, W,
                          "span " + (s + 1) + " rope " + plan.Spans[s].MaxRopeAboveM.ToString("F1") + " m above ground");
                bool onWall = false;
                for (int i = 1; i < plan.Nodes.Length - 1; i++)
                    onWall |= plan.Nodes[i].AlongM >= 140f && plan.Nodes[i].AlongM <= 170f;
                Check(onWall, W, "no tower at the wall (140-170 m): " + Describe(plan));
            }

            // A notch the chord passes 47 m above: the max height forces towers into it (Valley).
            Func<float, float> notch = s => s >= 30f && s <= 90f ? 1000f : 1040f;
            LiftTowerPlan valley = PlanAndReplan("LP-5/notch", (s, l) => notch(s), 120f, c, 1040f, 1040f);
            AddReport("LP-5 FG over a 40 m deep, 60 m wide notch (vertical walls)", "max rope height respected: towers in the notch", valley);
            Check(valley.Clear, W, "notch violated: " + Describe(valley));
            bool inNotch = false;
            for (int i = 1; i < valley.Nodes.Length - 1; i++)
                inNotch |= valley.Nodes[i].AlongM > 30f && valley.Nodes[i].AlongM < 90f && (valley.Nodes[i].Flags & LiftPlanNodeFlags.Valley) != 0;
            Check(inNotch, W, "no Valley tower in the notch: " + Describe(valley));
        }

        private static void Lp6SideHill()
        {
            const string W = "planner/LP-6";
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("fixedGrip");
            var laterals = new HashSet<float>();
            // Flat along the line, 150 % cross slope (the right side +4.5 m at 3 m out, the left side -4.5 m).
            LiftTerrainSampler side = (s, l) =>
            {
                laterals.Add(l);
                return 1000f + 1.5f * l;
            };
            LiftTowerPlan plan = PlanAndReplan("LP-6", side, 120f, c, 1000f, 1000f, 1016f, 1016f);
            LiftTowerPlan flat = s_Planner.Plan((s, l) => 1000f, 120f, 1016f, 1016f, c);
            AddReport("LP-6 FG side-hill, 150 % cross slope", "one tower, member taller than on flat ground (FG12), SideHill flag", plan);
            Check(laterals.Contains(c.HalfWidthM) && laterals.Contains(-c.HalfWidthM) && laterals.Contains(0f), W,
                  "side samples not at 0 / ±" + c.HalfWidthM);
            Check(plan.LineTowers == 1 && flat.LineTowers == 1, W, "expected one tower each: " + Describe(plan) + " / " + Describe(flat));
            if (plan.LineTowers == 1 && flat.LineTowers == 1)
            {
                LiftPlanNode n = plan.Nodes[1];
                Check(flat.Nodes[1].Member == 0, W, "flat tower not the shortest");
                Check(n.Member == 1, W, "side-hill member " + c.Ladder[n.Member].Name + ", expected SkiLiftTowerFG12 (high side +4.5 governs)");
                Check((n.Flags & LiftPlanNodeFlags.SideHill) != 0, W, "SideHill flag missing");
                Check(math.abs(n.HighSideM - 1004.5f) < 1e-3f && math.abs(n.LowSideM - 995.5f) < 1e-3f, W, "high / low side samples");
                float chair = n.RopeM - c.CarrierDropM - n.HighSideM;
                Check(chair >= c.FloorM - 0.01f, W, "carrier over the high side " + chair.ToString("F2") + " m < floor");
            }
            Check(plan.Clear, W, "violated: " + Describe(plan));
        }

        private static void Lp7Breakover()
        {
            const string W = "planner/LP-7";
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("fixedGrip");
            // A 50 % pitch rising to a plateau; the knee is rounded over 190-210 m (crest = the knee at 200 m).
            Func<float, float> g = s =>
            {
                if (s <= 190f) return 1000f + 0.5f * s;
                if (s >= 210f) return 1100f;
                float u = s - 190f;                                   // 0..20: slope 0.5 -> 0
                return 1095f + 0.5f * u - 0.0125f * u * u;
            };
            LiftTowerPlan plan = PlanAndReplan("LP-7", (s, l) => g(s), 260f, c, g(0f), g(260f));
            AddReport("LP-7 FG pitch to plateau (knee 190-210 m)", "a Breakover node at the knee, uphill side (>= 200 m)", plan);
            int found = -1;
            for (int i = 1; i < plan.Nodes.Length - 1; i++)
                if ((plan.Nodes[i].Flags & LiftPlanNodeFlags.Breakover) != 0)
                    found = i;
            if (Check(found > 0, W, "no Breakover node: " + Describe(plan)))
            {
                float at = plan.Nodes[found].AlongM;
                Check(at >= 200f && at <= 215f, W, "breakover node at " + at.ToString("F0") + " m, expected on the uphill side of the knee (200-215 m)");
            }
            Check(plan.Clear, W, "violated: " + Describe(plan));
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // LP-10 long lines (cost)
        // ════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>3 km of rolling, rising ground per type: a plan in well under a frame budget per edit (the overlay plans once per
        /// printed block), clear, idempotent.</summary>
        private static void Lp10LongLines()
        {
            const string W = "planner/LP-10";
            LiftTerrainSampler rolling = (s, l) => 1000f + 0.3f * s + 18f * math.sin(s / 70f) + 9f * math.sin(s / 23f + 1f) + 0.2f * l;
            foreach (string id in new[] { "fixedGrip", "detachable", "gondola", "tram" })
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(id);
                const float len = 3000f;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                LiftTowerPlan plan = s_Planner.Plan(rolling, len, rolling(0f, 0f) + c.RopeAboveBase(0), rolling(len, 0f) + c.RopeAboveBase(0), c);
                sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds;
                PlanAndReplan("LP-10/" + id, rolling, len, c, rolling(0f, 0f), rolling(len, 0f));
                s_Report.Add("LP-10 " + id + " 3 km rolling | clear, < 500 ms (Debug build) | " + ms.ToString("F1") + " ms; " + Describe(plan));
                Check(ms < 500.0, W, id + " took " + ms.ToString("F0") + " ms");
                Check(plan.Clear, W, id + " violated: " + Describe(plan));
            }
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // LP-9
        // ════════════════════════════════════════════════════════════════════════════════════════

        private static void Lp9LogBlock()
        {
            const string W = "planner/LP-9";
            Profile p = Reconstruct(FgDip);
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("fixedGrip");
            LiftTowerPlan plan = s_Planner.Plan((s, l) => p.At(s), p.Length, p.Start + c.RopeAboveBase(0), p.End + c.RopeAboveBase(0), c);
            var sb = new StringBuilder();
            plan.AppendLog(sb, "line fixedGrip edge 433471:5 (replay)");
            string[] lines = sb.ToString().TrimEnd('\n').Split('\n');
            Check(lines.Length == 1 + plan.Nodes.Length + plan.Spans.Length, W, "line count " + lines.Length);
            Check(lines[0].StartsWith("Towers [plan]: line fixedGrip edge 433471:5 (replay) — planned ", StringComparison.Ordinal), W, "header: " + lines[0]);
            Check(lines[1].StartsWith("  p1 at 0 m: ground ", StringComparison.Ordinal) && lines[1].Contains("station"), W, "first node: " + lines[1]);
            Check(lines[2].StartsWith("      span -> p2: ", StringComparison.Ordinal), W, "first span: " + lines[2]);
            Check(lines[3].StartsWith("  p2 at ", StringComparison.Ordinal) && lines[3].Contains("member SkiLiftTower"), W, "tower node: " + lines[3]);
            s_Report.Add("LP-9 block sample:\n" + sb.ToString().TrimEnd('\n'));
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // LP-11..LP-15: step 3 (fixed node count; build note field/2026-10-19-evening/t48-step3-build.md)
        // ════════════════════════════════════════════════════════════════════════════════════════

        // The 21:14 fixed-grip line of the 2026-10-19 evening test (shot 21.14.16; "Towers [nodes]" DUMP block at
        // field/2026-10-19-evening/logs/SkiResortMod.Mod.log:1031, nodes :1032 :1036 :1041 :1046 :1051 :1056 :1061, spans one line
        // below each). n1 and n7 are station stub nodes (no tower, not the line prefab), n2-n6 carry SkiLiftTowerFG2 (= FG8).
        private static readonly LogNode[] Fg2114 =
        {
            N(60f, 933.83f, 933.83f, 0.1f, 1.7f, 1.8f, 1.0f),
            N(54f, 943.80f, 943.82f, 0.4f, 0.5f, -0.1f, -0.8f),
            N(54f, 959.52f, 959.52f, 0.5f, 2.7f, 3.9f, 2.2f),
            N(54f, 966.24f, 966.33f, 0.6f, 2.1f, 1.8f, 1.1f),
            N(54f, 974.43f, 974.43f, -0.1f, -0.7f, -0.4f, 0.1f),
            N(57f, 993.88f, 993.88f, 0.8f, -0.6f, -2.4f, -2.8f),
            N(0f, 1022.69f, 1022.69f),
        };

        private static float[] InnerAlong(LogNode[] nodes)
        {
            var a = new float[nodes.Length - 2];
            float s = 0f;
            for (int i = 0; i < nodes.Length - 2; i++)
            {
                s += nodes[i].SpanXz;
                a[i] = s;
            }
            return a;
        }

        private static float[] InnerAlong(LiftTowerPlan p)
        {
            var a = new float[p.Nodes.Length - 2];
            for (int i = 1; i < p.Nodes.Length - 1; i++)
                a[i - 1] = p.Nodes[i].AlongM;
            return a;
        }

        internal static string DescribeFit(LiftTowerFit f) =>
            f.Mode + " (line towers " + f.Inner + ", free plan needs " + f.Needed + "): " + Describe(f.Plan);

        /// <summary>LP-13: re-fitting a fitted line (its nodes where the fit put them) keeps every node and member.</summary>
        private static void FitFixedPoint(string where, LiftTerrainSampler terrain, float length, float ra, float rb, LiftTowerPlanConstants c,
                                          LiftTowerFit fit, float engineMax)
        {
            if (fit.Mode == LiftFitMode.Short)
                return;
            LiftTowerFit again = s_Planner.Fit(terrain, length, ra, rb, c, InnerAlong(fit.Plan), true, engineMax);
            Check(again.Mode == LiftFitMode.Kept && Same(again.Plan, fit.Plan), "planner/LP-13/" + where,
                  "re-fit of the fitted line changed it: " + DescribeFit(fit) + " -> " + DescribeFit(again));
        }

        private static void Lp11Line2114()
        {
            const string W = "planner/LP-11";
            Profile p = Reconstruct(Fg2114);
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("fixedGrip");
            LiftTerrainSampler t = (s, l) => p.At(s);
            float[] inner = InnerAlong(Fg2114);
            var fg8 = new int[inner.Length];
            // Station ropes: (A) the build-1 default (stub node y + the shortest tower's rope 7.04); (B) the terminal towers' axles
            // (PIPELINE constants: drive ET68 5.84 at the base, return ET62 5.24 at the top) — the game reads the real anchor off the
            // rope lane (LiftTowerFitSystem.StationRope).
            var ends = new[]
            {
                ("ends +7.04", p.Start + c.RopeAboveBase(0), p.End + c.RopeAboveBase(0)),
                ("ends ET68 5.84 / ET62 5.24", p.Start + 5.84f, p.End + 5.24f),
            };
            foreach ((string label, float ra, float rb) in ends)
            {
                LiftTowerPlan before = s_Planner.PlanFixed(t, p.Length, ra, rb, c, inner, fg8, null, assign: false);
                AddReport("LP-11 21:14 FG line BEFORE (" + label + "): 5 x FG8 at the engine's nodes", "violated (chairs in the rock)", before);
                Check(!before.Clear, W, label + ": the replayed 21:14 line is clear before the fit: " + Describe(before));

                LiftTowerFit fit = s_Planner.Fit(t, p.Length, ra, rb, c, inner, true, 60f);
                s_Report.Add("LP-11 21:14 FG line AFTER (" + label + ") | clear, max 5 line towers, every span <= 60 m | " + DescribeFit(fit));
                Check(fit.Plan.Clear, W, label + ": not clear after the fit: " + DescribeFit(fit));
                Check(fit.Plan.LineTowers == inner.Length, W, label + ": the fit changed the node count: " + DescribeFit(fit));
                for (int s = 0; s < fit.Plan.Spans.Length; s++)
                    Check(fit.Plan.Spans[s].LengthM <= 60f + 1e-3f, W, label + ": span " + (s + 1) + " " + fit.Plan.Spans[s].LengthM.ToString("F1") + " m > the engine's 60");
                for (int i = 1; i < fit.Plan.Nodes.Length - 1; i++)
                {
                    LiftPlanNode n = fit.Plan.Nodes[i];
                    Check(n.RopeM - n.GroundM <= c.MaxRopeAboveGroundM + 0.02f, W, label + ": rope above the cap at node " + (i + 1));
                }
                FitFixedPoint("LP-11/" + label, t, p.Length, ra, rb, c, fit, 60f);
            }
        }

        private static void Lp12FieldFits()
        {
            const string W = "planner/LP-12";
            // The three 2026-10-19 lines with the engine's own node count (equal split, CourseSplitSystem.cs:3485-3493): DQ gully
            // 336 m / 90 -> 4 x 84 m (3 line towers, as logged); tram 1,160 m / 1,000 -> 2 x 580 m (1); FG dip 129 m / 60 -> 3 x 43 m (2).
            // The DQ gully cannot be cleared with 3 line towers on spans <= 90 m (the free plan needs 4): Short, reported, and far better
            // than before; adding a node needs a new course (step 3c).
            var cases = new (string Name, LogNode[] Log, string Type, float EngineMax, int Inner, bool ExpectClear)[]
            {
                ("DQ gully", DqGully, "detachable", 90f, 3, false),
                ("tram pitch", TramPitch, "tram", 1000f, 1, true),
                ("FG dip", FgDip, "fixedGrip", 60f, 2, true),
            };
            foreach (var cs in cases)
            {
                Profile p = Reconstruct(cs.Log);
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(cs.Type);
                LiftTerrainSampler t = (s, l) => p.At(s);
                var inner = new float[cs.Inner];
                for (int i = 0; i < cs.Inner; i++)
                    inner[i] = p.Length * (i + 1) / (cs.Inner + 1);
                float ra = p.Start + c.RopeAboveBase(0), rb = p.End + c.RopeAboveBase(0);
                LiftTowerPlan before = s_Planner.PlanFixed(t, p.Length, ra, rb, c, inner, new int[cs.Inner], null, assign: false);
                LiftTowerFit fit = s_Planner.Fit(t, p.Length, ra, rb, c, inner, true, cs.EngineMax);
                s_Report.Add("LP-12 " + cs.Name + " at the engine's equal split | before -> after, " + (cs.ExpectClear ? "clear" : "Short, better than before") +
                             " | " + Describe(before) + "  ->  " + DescribeFit(fit));
                if (cs.ExpectClear)
                {
                    Check(fit.Plan.Clear, W, cs.Name + " not clear: " + DescribeFit(fit));
                }
                else
                {
                    Check(fit.Mode == LiftFitMode.Short && fit.Needed > fit.Inner, W, cs.Name + " expected Short (needs more towers): " + DescribeFit(fit));
                    Check(fit.Plan.MinFloorMarginM > before.MinFloorMarginM + 1f && fit.Plan.MinFloorMarginM > -0.5f, W,
                          cs.Name + " Short fit not much better than before: " + DescribeFit(fit));
                }
                Check(fit.Plan.LineTowers == cs.Inner, W, cs.Name + " node count changed: " + DescribeFit(fit));
                for (int s = 0; s < fit.Plan.Spans.Length; s++)
                    Check(fit.Plan.Spans[s].LengthM <= cs.EngineMax + 1e-3f, W, cs.Name + " span over the engine max: " + DescribeFit(fit));
                FitFixedPoint("LP-12/" + cs.Name, t, p.Length, ra, rb, c, fit, cs.EngineMax);
            }
        }

        private static void Lp14Short()
        {
            const string W = "planner/LP-14";
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("fixedGrip");
            // Two 25 m humps (at 30 m and 90 m, 24 m wide) on a 120 m FG line split 2 x 60: its one tower stands in the dip between
            // them; a tower on each hump is what clears it, so the free plan needs more towers than the line has.
            Func<float, float> hump = s => 1000f + 25f * math.max(0f, 1f - math.abs(s - 30f) / 12f) + 25f * math.max(0f, 1f - math.abs(s - 90f) / 12f);
            LiftTerrainSampler t = (s, l) => hump(s);
            LiftTowerFit fit = s_Planner.Fit(t, 120f, 1000f + c.RopeAboveBase(0), 1000f + c.RopeAboveBase(0), c, new[] { 60f }, true, 60f);
            s_Report.Add("LP-14 FG 120 m over two 25 m humps, one line tower | Short: needs more towers, violation reported | " + DescribeFit(fit));
            if (fit.Mode == LiftFitMode.Short)
            {
                Check(!fit.Plan.Clear, W, "Short but clear");
                Check(fit.Needed > fit.Inner || fit.Moved != null, W, "Short without a reason: " + DescribeFit(fit));
            }
            else
            {
                Check(fit.Plan.Clear, W, "not Short and not clear: " + DescribeFit(fit));
            }
            // Heights only (no move allowed): a bent line keeps its nodes; the result is the kept plan.
            LiftTowerFit bent = s_Planner.Fit(t, 120f, 1000f + c.RopeAboveBase(0), 1000f + c.RopeAboveBase(0), c, new[] { 60f }, false, 60f);
            Check(bent.Moved == null && bent.Plan == bent.Kept, W, "allowMove false still moved: " + DescribeFit(bent));
        }

        private static void Lp15EngineNumbers()
        {
            const string W = "planner/LP-15";
            for (int ti = 0; ti < SkiCatalog.Lifts.Count; ti++)
            {
                LiftTypeDef def = SkiCatalog.Lifts[ti];
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(def);
                if (c == null)
                    continue;
                for (int m = 0; m < c.Ladder.Length; m++)
                {
                    float e = LiftTowerEdit.ElevationM(c, m);
                    int win = LiftTowerEdit.EngineWinner(c, e, out float score);
                    Check(win == m && math.abs(score - 1f) < 1e-5f, W,
                          def.Id + " elevation " + e.ToString("F3") + ": the engine ladder picks " + (win < 0 ? "a TIE" : c.Ladder[win].Name) +
                          " (score " + score.ToString("F3") + "), planned " + c.Ladder[m].Name);
                    // Rope: node y (= ground + E) + the line lane y (catalog AxleM) is the planner's rope for that member.
                    Check(math.abs(e + def.AxleM - c.RopeAboveBase(m)) < 2e-3f, W,
                          def.Id + " " + c.Ladder[m].Name + ": ground + E + AxleM " + (e + def.AxleM).ToString("F3") + " != planner rope " + c.RopeAboveBase(m).ToString("F3"));
                    // Top-anchored placement (SubObjectSystem.cs:2464-2469, :2477-2482) stands the member on the ground.
                    Check(math.abs(LiftTowerEdit.EnginePivotAboveGround(c, m, e)) < 1e-4f, W,
                          def.Id + " " + c.Ladder[m].Name + ": pivot " + LiftTowerEdit.EnginePivotAboveGround(c, m, e).ToString("F3") + " m above the ground");
                    // The tool's elevation cap (PlaceableNet m_ElevationRange.max) holds every ladder elevation.
                    Check(e <= c.EngineElevationCapM + 1e-3f, W, def.Id + " " + c.Ladder[m].Name + " elevation " + e + " over the net's cap " + c.EngineElevationCapM);
                }
            }
            LiftTowerEdit.HangingCurve(new float3(0f, 100f, 0f), new float3(60f, 112f, 0f), 0.03f, out float3 b, out float3 cc);
            Check(math.abs(b.x - 20f) < 1e-4f && math.abs(b.y - (104f - 2.4f)) < 1e-4f && math.abs(cc.y - (108f - 2.4f)) < 1e-4f, W,
                  "HangingCurve != NetUtils.StraightCurve(a, d, 0.03): b " + b + " c " + cc);
        }
    }
}
