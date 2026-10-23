// Tests for the pure LiftTowerPlanner on measured profiles replayed from field logs and on synthetic ground (where-tags `planner/...`,
// LP-0..LP-25). Pinned: every lift type has planner constants and each member's rope = its manifest axle; the DQ gully, tram pitch and
// FG dip replays end with every span clear; flat ground gets infill only; walls, side-hill (the high side governs) and breakover knees
// are respected; Fit (fixed node count) is Kept / Moved / Short as designed and a fitted line re-fits to itself (write-once guarantee).
// Replay terrain: ground at each node = the log's `terrain`; between nodes the four chord samples at 20/40/60/80 % give ground =
// lerp(node y, next node y, t) - chord clearance (the diag's chord runs between node positions, LiftTowerDiagSystem DescribeNode);
// linear in between. The log has no cross-slope, so the replay sampler ignores the lateral offset.

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

        /// <summary>The case -> got lines (printed by Program after the run).</summary>
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
            Lp16TramRefit();
            Lp17Objective();
            Lp18ToolEmission();
            Lp19Pins();
            Lp20VanillaAfterPatch();
            Lp21GondolaShortLoop();
            Lp22StationClearance();
            Lp23OwnCut();
            Lp24TowerNumbers();
            Lp25TramLongSpan();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);


        private struct LogNode
        {
            public float SpanXz; // to the next node (0 for the last)
            public float Y;
            public float Terrain; // terrain under the node
            public float[] Chord; // chord clearance at 20/40/60/80 % (null for the last)
        }

        private static LogNode N(float span, float y, float terrain, params float[] chord) =>
            new LogNode { SpanXz = span, Y = y, Terrain = terrain, Chord = chord.Length == 0 ? null : chord };

        // Detachable across the gully.
        private static readonly LogNode[] DqGully =
        {
            N(84f, 1187.80f, 1187.80f, -3.8f, -3.8f, -3.7f, -2.1f),
            N(84f, 1154.07f, 1154.06f, 6.0f, 13.6f, 14.7f, 8.8f),
            N(84f, 1140.77f, 1140.77f, -1.7f, -3.8f, -3.7f, -3.7f),
            N(84f, 1177.12f, 1177.01f, 2.1f, 0.5f, 0.0f, -3.7f),
            N(0f, 1228.12f, 1228.12f),
        };

        // Tram up the pitch.
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

        // Fixed grip over the dip.
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


        private static void Lp0Constants()
        {
            const string W = "planner/LP-0";
            var axles = new Dictionary<string, float[]>
            {
                { "fixedGrip", new[] { 7.04f, 11.04f, 15.04f, 19.04f } }, // SkiLiftTowerFG8O/12O/16O/20O manifests "axle"
                { "detachable", new[] { 8f, 10f, 12f, 14f, 16f, 18f } }, // SkiLiftTowerDQ10O..DQ20O manifests "axle"
                { "gondola", new[] { 10f, 14f, 18f } }, // SkiLiftTowerGD12O/16O/20O manifests "axle"
                { "tram", new[] { 26.25f, 41.25f, 56.25f } }, // SkiTramTower30A/45A/60A manifests stubs pos y
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


        private static void Lp1DetachableGully()
        {
            const string W = "planner/LP-1";
            Profile p = Reconstruct(DqGully);
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("detachable");
            LiftTowerPlan plan = PlanAndReplan("LP-1", (s, l) => p.At(s), p.Length, c, p.Start, p.End);
            // Fewest towers: one tall tower spans the gully (the previous objective built 2 x DQ16; LP-17 prints both).
            AddReport("LP-1 DQ gully (log :628), catalog drop 3.3 + floor 3", "1 line tower, tall (>= DQ16), clear", plan);
            Check(plan.LineTowers == 1, W, "line towers " + plan.LineTowers + ", expected 1: " + Describe(plan));
            Check(MaxMember(plan) >= 3, W, "no tall member (>= DQ16): " + Describe(plan));
            Check(plan.Clear, W, "a span is violated: " + Describe(plan));

            // The analysis' assumption: rope >= 6.0 m above the ground (2.4-2.6 m carrier + ~3 m floor).
            LiftTowerPlan plan6 = PlanAndReplan("LP-1b", (s, l) => p.At(s), p.Length, c.With(floorM: 6.0f - c.CarrierDropM), p.Start, p.End);
            AddReport("LP-1b DQ gully, analysis' rope >= 6.0 m", "1 line tower, taller than DQ10, clear (analysis §3 row L)", plan6);
            Check(plan6.LineTowers == 1 && plan6.Clear && MaxMember(plan6) >= 1, W + "b", Describe(plan6));
        }

        private static void Lp2TramPitch()
        {
            const string W = "planner/LP-2";
            Profile p = Reconstruct(TramPitch);
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("tram");
            // The 1,000 m span of the original replay (before DECISIONS 381) needs one tower; at the 3 km max the 1,160 m pitch is one clear span.
            LiftTowerPlan plan1k = PlanAndReplan("LP-2/1k", (s, l) => p.At(s), p.Length, c.With(maxSpanM: 1000f), p.Start, p.End);
            AddReport("LP-2 tram pitch (log :665), max span 1,000 m", "1 line tower, clear", plan1k);
            Check(plan1k.LineTowers == 1, W, "1 km max: line towers " + plan1k.LineTowers + ", expected 1: " + Describe(plan1k));
            Check(plan1k.Clear, W, "1 km max: a span is violated: " + Describe(plan1k));
            LiftTowerPlan plan = PlanAndReplan("LP-2", (s, l) => p.At(s), p.Length, c, p.Start, p.End);
            AddReport("LP-2 tram pitch (log :665), max span 3,000 m", "0 line towers, one clear span", plan);
            Check(plan.LineTowers == 0, W, "line towers " + plan.LineTowers + ", expected 0: " + Describe(plan));
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


        private static void Lp4Flat()
        {
            const string W = "planner/LP-4";
            foreach (string id in new[] { "fixedGrip", "detachable", "tram" })
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(id);
                float length = id == "tram" ? 2400f : 5f * c.TypicalSpanM;
                LiftTowerPlan plan = PlanAndReplan("LP-4/" + id, (s, l) => 1000f, length, c, 1000f, 1000f);
                // Fewest towers: the fewest spans the max span allows, even (within one sample), on flat ground.
                int spans = (int)math.ceil(length / c.MaxSpanM - 1e-4f);
                float even = length / spans;
                AddReport("LP-4 flat " + id + " " + length.ToString("F0") + " m", spans + " spans of ~" + even.ToString("F0") + " m (max " + c.MaxSpanM.ToString("F0") + "), fewest towers, infill only", plan);
                Check(plan.Spans.Length == spans, W, id + ": " + plan.Spans.Length + " spans, expected " + spans + ": " + Describe(plan));
                for (int s = 0; s < plan.Spans.Length; s++)
                    Check(math.abs(plan.Spans[s].LengthM - even) <= c.StepM + 1e-3f, W,
                          id + " span " + (s + 1) + " " + plan.Spans[s].LengthM.ToString("F0") + " m, expected ~" + even.ToString("F0"));
                // FG: m_Hanging 0.03 sags a 75 m span 2.25 m, so FG8s (rope 7.04) leave the chair under the 3 m floor: the flat FG line stands
                // on FG12s (a taller member that saves a tower vs 60 m spans). DQ / tram (0.004) keep the shortest member.
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
            // A 50 % pitch rising to a plateau; the knee is rounded over 160-180 m (crest = the knee at 170 m). The rule is a tie-break
            // after the tower count, so the knee sits where 260 m on 90 m spans allows its second tower (170-180 m); a knee at 190-210 m
            // would cost a third tower and is reported in LP-17 instead.
            Func<float, float> g = s =>
            {
                if (s <= 160f) return 1000f + 0.5f * s;
                if (s >= 180f) return 1090f;
                float u = s - 160f; // 0..20: slope 0.5 -> 0
                return 1080f + 0.5f * u - 0.0125f * u * u;
            };
            LiftTowerPlan plan = PlanAndReplan("LP-7", (s, l) => g(s), 260f, c, g(0f), g(260f));
            AddReport("LP-7 FG pitch to plateau (knee 160-180 m)", "a Breakover node at the knee, uphill side (170-185 m)", plan);
            int found = -1;
            for (int i = 1; i < plan.Nodes.Length - 1; i++)
                if ((plan.Nodes[i].Flags & LiftPlanNodeFlags.Breakover) != 0)
                    found = i;
            if (Check(found > 0, W, "no Breakover node: " + Describe(plan)))
            {
                float at = plan.Nodes[found].AlongM;
                Check(at >= 170f && at <= 185f, W, "breakover node at " + at.ToString("F0") + " m, expected on the uphill side of the knee (170-185 m)");
            }
            Check(plan.Clear, W, "violated: " + Describe(plan));
        }


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


        // A seven-node FG line (a "Towers [nodes]" block of a field test): n1 and n7 are station stub nodes (no tower, not the line
        // prefab), n2-n6 carry SkiLiftTowerFG2 (= FG8).
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
            // Station ropes: (A) the default (stub node y + the shortest tower's rope 7.04); (B) the terminal towers' axles (PIPELINE
            // constants: drive ET68 5.84 at the base, return ET62 5.24 at the top); the game reads the real anchor off the rope lane
            // (LiftTowerFitSystem.StationRope).
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
            // Three lines with the engine's own node count (equal split, CourseSplitSystem.cs:3485-3493): DQ gully 336 m / 90 -> 4 x 84 m
            // (3 line towers); tram 1,160 m / 1,000 -> 2 x 580 m (1); FG dip 129 m / 60 -> 3 x 43 m (2). The DQ gully cannot be cleared
            // with 3 line towers on spans <= 90 m (the free plan needs 4): Short, reported, and far better than before; adding a node needs
            // a new course.
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
            // Two 25 m humps (at 30 m and 90 m, 24 m wide) on a 120 m FG line split 2 x 60: its one tower stands in the dip between them;
            // a tower on each hump is what clears it, so the free plan needs more towers than the line has.
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


        // The 1,605 m tram as the loop guard left it: a "Towers [nodes]" block (node lines and the span line under each). Members from
        // net Elevation (15 -> T45). Ropes from the fit blocks ("station ropes y").
        private static readonly LogNode[] Tram1605 =
        {
            N(382f, 992.93f, 992.78f, 10.6f, 22.8f, 32.1f, 18.9f),
            N(64f, 1172.78f, 1157.54f, 5.6f, -3.3f, -5.1f, -1.2f),
            N(322f, 1184.14f, 1184.14f, 11.0f, 23.4f, 5.8f, -4.0f),
            N(32f, 1317.61f, 1302.54f, 15.2f, 13.1f, 8.8f, 3.8f),
            N(148f, 1318.20f, 1318.10f, 2.7f, 13.3f, 25.6f, 18.2f),
            N(20f, 1369.69f, 1369.51f, 0.1f, -1.6f, -1.7f, -0.8f),
            N(76f, 1375.85f, 1375.78f, 9.1f, 11.7f, 13.5f, 17.2f),
            N(96f, 1463.71f, 1448.29f, -2.6f, 1.9f, 5.8f, 1.6f),
            N(52f, 1531.80f, 1531.77f, 1.2f, 1.8f, 3.1f, 7.6f),
            N(166f, 1593.08f, 1577.97f, 6.3f, 25.5f, 23.3f, 14.5f),
            N(48f, 1711.43f, 1711.33f, -2.8f, -9.7f, -10.2f, -6.8f),
            N(82f, 1783.61f, 1783.54f, 13.1f, 22.7f, 12.6f, -0.9f),
            N(84f, 1863.17f, 1862.95f, -0.1f, 0.7f, 9.0f, 7.6f),
            N(32f, 1923.33f, 1923.08f, -0.3f, -3.3f, -2.4f, -1.2f),
            N(0f, 1937.73f, 1937.47f),
        };
        private static readonly int[] Tram1605Members = { 1, 0, 1, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0 };

        // The 2,292 m tram, after the guard; all T30.
        private static readonly LogNode[] Tram2292 =
        {
            N(510f, 960.48f, 959.91f, 25.3f, 46.3f, 28.5f, 7.6f),
            N(475f, 1203.00f, 1203.00f, 45.2f, 63.7f, 55.4f, 23.6f),
            N(475f, 1327.99f, 1327.74f, 1.4f, 25.7f, 3.6f, 13.7f),
            N(832f, 1600.67f, 1600.61f, 62.6f, 151.6f, 163.2f, 107.2f),
            N(0f, 2106.69f, 2105.86f),
        };
        private static readonly int[] Tram2292Members = { 0, 0, 0 };

        private static void Lp16TramRefit()
        {
            const string W = "planner/LP-16";
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("tram");
            var lines = new (string Name, LogNode[] Log, int[] Members, float RopeA, float RopeB)[]
            {
                ("tram 1605 m (edge 120310:57, log :1796)", Tram1605, Tram1605Members, 1019.18f, 1963.98f),
                ("tram 2292 m (edge 346939:43, log :4851)", Tram2292, Tram2292Members, 986.73f, 2132.94f),
            };
            foreach (var ln in lines)
            {
                Profile p = Reconstruct(ln.Log);
                LiftTerrainSampler t = (s, l) => p.At(s); // no cross-slope (the log has none mid-span; as LP-1..LP-12)
                float[] inner = InnerAlong(ln.Log);
                LiftTowerPlan current = s_Planner.PlanFixed(t, p.Length, ln.RopeA, ln.RopeB, c, inner, ln.Members, null, assign: false);
                LiftTowerFit oldFit = s_Planner.Fit(t, p.Length, ln.RopeA, ln.RopeB, c, inner, true, 1000f);
                LiftTowerFit fit = s_Planner.Fit(t, p.Length, ln.RopeA, ln.RopeB, c, inner, true, 1000f, ln.Members);

                // Height writes the fit system would make at these nodes: the old rule (|dy| > 0.25 m) and the one-sided rule.
                int oldWrites = 0, newWrites = 0, memberChanges = 0, oldMemberChanges = 0;
                for (int i = 1; i < ln.Log.Length - 1; i++)
                {
                    LiftPlanNode n = fit.Plan.Nodes[i];
                    float target = n.GroundM + LiftTowerEdit.ElevationM(c, n.Member);
                    float oldTarget = oldFit.Plan.Nodes[i].GroundM + LiftTowerEdit.ElevationM(c, oldFit.Plan.Nodes[i].Member);
                    if (math.abs(ln.Log[i].Y - oldTarget) > 0.25f || oldFit.Plan.Nodes[i].Member != ln.Members[i - 1] ||
                        math.abs(oldFit.Plan.Nodes[i].AlongM - inner[i - 1]) > 0.05f)
                        oldWrites++;
                    if (oldFit.Plan.Nodes[i].Member != ln.Members[i - 1])
                        oldMemberChanges++;
                    if (n.Member != ln.Members[i - 1])
                        memberChanges++;
                    if (n.Member != ln.Members[i - 1] || math.abs(n.AlongM - inner[i - 1]) > 0.05f || LiftTowerEdit.HeightWrite(ln.Log[i].Y, target))
                        newWrites++;
                }
                s_Report.Add("LP-16 " + ln.Name + " as left by the guard | current " + (current.Clear ? "clear" : "VIOLATED") +
                             "; old fit: " + oldFit.Mode + ", " + oldMemberChanges + " member change(s), " + oldWrites + " node write(s)" +
                             " -> T115 fit: " + fit.Mode + ", " + memberChanges + " member change(s), " + newWrites + " node write(s) | " +
                             DescribeFit(fit));
                Check(current.Clear, W, ln.Name + ": the replayed line is not clear as it stands: " + Describe(current));
                Check(fit.Mode == LiftFitMode.Kept && memberChanges == 0, W, ln.Name + ": the T115 fit changed members: " + DescribeFit(fit));
                Check(newWrites == 0, W, ln.Name + ": the T115 fit would write " + newWrites + " node(s) on a fitted line");
                // Identity holds on the next re-fit too (the carve stops once nothing is written).
                LiftTowerFit again = s_Planner.Fit(t, p.Length, ln.RopeA, ln.RopeB, c, InnerAlong(fit.Plan), true, 1000f, MembersOf(fit.Plan));
                Check(again.Mode == LiftFitMode.Kept && Same(again.Plan, fit.Plan), W, ln.Name + ": re-fit not identity: " + DescribeFit(again));
            }


            // The carve ratchet (node ground 1158.50 -> 1158.11 -> 1157.78, the node re-grounded each time; t6 1370.31 -> 1369.96 ->
            // 1369.69): each write lowers the node by the drift and the carve lowers the ground under it by ~0.85 of that. A model, not a
            // measurement: count the writes until the line settles, old rule vs one-sided rule.
            int Ratchet(Func<float, float, bool> write)
            {
                float y = 1158.50f, ground = 1158.11f;
                int writes = 0;
                for (int round = 0; round < 12; round++)
                {
                    if (!write(y, ground))
                        break;
                    float drop = y - ground;
                    y = ground;
                    ground -= 0.85f * drop;
                    writes++;
                }
                return writes;
            }
            int before = Ratchet((y, target) => math.abs(y - target) > 0.25f);
            int after = Ratchet(LiftTowerEdit.HeightWrite);
            s_Report.Add("LP-16 carve ratchet model (sink 0.85 x drop, from the log) | old rule hits the guard (3 writes), T115 none | " +
                         "old rule " + before + " write(s), T115 " + after + " write(s)");
            Check(before >= 3, W, "the ratchet model does not reproduce the guard: " + before + " write(s)");
            Check(after == 0, W, "the T115 rule still chases the carved ground: " + after + " write(s)");
        }


        // The engine's lines before our fit touched them: the "Towers [nodes]" block that precedes each first fit block, station ropes
        // from the fit header.
        private static readonly LogNode[] Fg516 =
        {
            N(60f, 940.84f, 940.84f, -1.4f, -1.8f, -1.4f, -0.6f), N(57f, 945.27f, 945.28f, 0.5f, 0.4f, -0.3f, -0.5f),
            N(57f, 950.52f, 950.50f, 1.5f, 1.6f, 0.8f, 0.1f), N(57f, 961.87f, 961.87f, 1.1f, 0.9f, 1.0f, 0.5f),
            N(57f, 983.82f, 983.83f, -0.4f, 0.8f, 3.4f, 2.3f), N(57f, 1007.45f, 1007.46f, -2.6f, -2.3f, -2.2f, -0.6f),
            N(57f, 1018.02f, 1018.02f, 1.1f, 2.9f, 5.1f, 3.1f), N(57f, 1041.16f, 1041.28f, -0.1f, 2.2f, 3.1f, 1.4f),
            N(57f, 1067.34f, 1067.34f, -1.9f, -2.3f, -2.1f, -1.6f), N(0f, 1087.91f, 1087.91f),
        };
        private static readonly LogNode[] Dq328 =
        {
            N(34f, 1186.16f, 1186.16f, -2.5f, -3.7f, -3.7f, -1.9f), N(22f, 1167.52f, 1166.24f, 2.3f, 3.3f, 3.7f, 3.5f),
            N(81f, 1148.33f, 1145.51f, 3.1f, 3.3f, 5.6f, 4.7f), N(81f, 1115.14f, 1115.14f, 8.2f, 10.1f, 10.6f, 6.6f),
            N(16f, 1144.26f, 1140.03f, 5.6f, 6.4f, 6.8f, 6.4f), N(47f, 1158.37f, 1152.87f, -1.2f, -3.7f, -3.6f, -1.2f),
            N(47f, 1186.16f, 1186.16f, -2.2f, -3.7f, -3.8f, -3.7f), N(0f, 1201.45f, 1201.45f),
        };
        private static readonly LogNode[] Fg312 = // Short with 5 towers
        {
            N(57f, 1162.09f, 1162.09f, -2.9f, -2.3f, -2.3f, -2.8f), N(57f, 1142.89f, 1142.92f, 1.4f, 1.2f, 1.0f, 1.4f),
            N(57f, 1113.58f, 1113.58f, 4.8f, 8.7f, 10.1f, 8.0f), N(28f, 1120.64f, 1117.04f, 4.5f, 4.9f, 5.1f, 5.1f),
            N(56f, 1143.03f, 1138.96f, -2.7f, -2.3f, -2.4f, -2.9f), N(56f, 1164.14f, 1164.14f, 0.8f, -1.9f, -2.4f, -2.6f),
            N(0f, 1192.52f, 1192.52f),
        };
        private static readonly LogNode[] Gd483 =
        {
            N(180f, 1191.71f, 1191.71f, 2.2f, -2.3f, 7.1f, -2.9f), N(30f, 1274.89f, 1274.89f, 2.5f, 2.9f, 1.6f, 0.2f),
            N(137f, 1295.46f, 1295.65f, -2.8f, -1.8f, 10.9f, 5.2f), N(137f, 1299.85f, 1299.79f, -4.0f, 2.2f, 2.7f, 2.6f),
            N(0f, 1353.32f, 1353.32f),
        };
        private static readonly LogNode[] Dq619 =
        {
            N(88f, 952.62f, 952.62f, -0.7f, -0.3f, -1.1f, -1.5f), N(88f, 975.09f, 975.08f, -1.5f, -2.0f, 1.1f, 2.5f),
            N(88f, 990.16f, 990.15f, -2.0f, -1.1f, -3.8f, -3.3f), N(88f, 1003.68f, 1003.67f, 2.2f, -0.2f, -3.7f, -3.2f),
            N(88f, 1029.71f, 1029.70f, 0.4f, 4.6f, 6.8f, 3.6f), N(88f, 1049.67f, 1049.69f, 2.5f, 5.8f, 6.5f, 4.1f),
            N(88f, 1066.78f, 1066.76f, 1.1f, 1.1f, -0.3f, -0.2f), N(0f, 1109.56f, 1109.56f),
        };
        private static readonly LogNode[] Gd619 = // Short with 2 towers
        {
            N(206f, 955.94f, 955.94f, -1.4f, -2.9f, -3.2f, -0.2f), N(206f, 989.47f, 989.44f, -0.1f, -2.1f, -0.4f, 0.7f),
            N(206f, 1039.06f, 1039.08f, 7.5f, 4.1f, 7.5f, 0.8f), N(0f, 1103.06f, 1103.06f),
        };
        private static readonly LogNode[] Tram1605Engine = // 13 clicked nodes
        {
            N(382f, 992.93f, 992.78f, 7.9f, 17.3f, 23.8f, 7.9f), N(64f, 1159.03f, 1158.50f, -5.4f, -11.4f, -10.5f, -3.9f),
            N(322f, 1184.26f, 1184.15f, 8.2f, 17.6f, -3.0f, -11.7f), N(32f, 1302.86f, 1302.61f, 3.5f, 4.3f, 3.1f, 1.1f),
            N(148f, 1318.45f, 1318.20f, 3.2f, 14.0f, 26.3f, 19.1f), N(20f, 1370.83f, 1370.31f, 0.1f, -1.1f, -1.0f, -0.4f),
            N(76f, 1376.13f, 1375.85f, 6.9f, 7.0f, 6.3f, 7.5f), N(96f, 1451.53f, 1450.20f, -11.2f, -5.3f, 1.1f, -0.6f),
            N(52f, 1532.11f, 1531.80f, -1.6f, -4.0f, -5.7f, -4.2f), N(166f, 1578.19f, 1578.08f, -5.3f, 17.2f, 18.3f, 12.8f),
            N(48f, 1713.01f, 1711.78f, -2.9f, -8.7f, -9.6f, -6.5f), N(82f, 1783.61f, 1783.54f, 13.3f, 23.1f, 13.2f, -0.1f),
            N(84f, 1864.09f, 1863.46f, 0.8f, 1.5f, 9.8f, 8.3f), N(32f, 1924.01f, 1923.33f, -0.3f, -2.9f, -2.1f, -1.1f),
            N(0f, 1937.73f, 1937.47f),
        };

        private static void Lp17Objective()
        {
            const string W = "planner/LP-17";
            var lines = new (string Name, LogNode[] Log, string Type, float RopeA, float RopeB)[]
            {
                ("FG 516 m (log :566)", Fg516, "fixedGrip", 947.84f, 1097.81f),
                ("FG 312 m (log :1231, Short)", Fg312, "fixedGrip", 1169.13f, 1199.56f),
                ("DQ 328 m (log :999)", Dq328, "detachable", 1194.16f, 1209.45f),
                ("DQ 619 m (log :2155)", Dq619, "detachable", 960.62f, 1117.56f),
                ("GD 483 m (log :1348)", Gd483, "gondola", 1201.71f, 1363.32f),
                ("GD 619 m (log :2271, Short)", Gd619, "gondola", 965.94f, 1113.06f),
                ("tram 1605 m (log :1537)", Tram1605Engine, "tram", 1019.18f, 1963.98f),
                ("tram 2292 m (log :4851)", Tram2292, "tram", 986.73f, 2132.94f),
            };
            int fewer = 0, tallerAndFewer = 0;
            var legacy = new LiftTowerPlanner { LegacySpacing = true };
            foreach (var ln in lines)
            {
                Profile p = Reconstruct(ln.Log);
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(ln.Type);
                LiftTerrainSampler t = (s, l) => p.At(s);
                LiftTowerPlan before = legacy.Plan(t, p.Length, ln.RopeA, ln.RopeB, c);
                LiftTowerPlan after = PlanAndReplan("LP-17/" + ln.Name, t, p.Length, c, p.Start, p.End, ln.RopeA, ln.RopeB);
                s_Report.Add("LP-17 " + ln.Name + " free plan | 2026-10-19 objective -> fewest towers | {" + before.MemberCounts() + "} " +
                             Describe(before) + "  ->  {" + after.MemberCounts() + "} " + Describe(after));
                Check(after.LineTowers <= before.LineTowers, W, ln.Name + ": more towers than the 2026-10-19 objective: " + Describe(after));
                Check(after.Clear || !before.Clear, W, ln.Name + ": the 2026-10-19 plan is clear and the new one is not: " + Describe(after));
                if (after.LineTowers < before.LineTowers)
                {
                    fewer++;
                    if (MaxMember(after) > MaxMember(before))
                        tallerAndFewer++;
                }
            }
            s_Report.Add("LP-17 summary | fewer towers on most lines, taller members where they save one | fewer on " + fewer + " / " +
                         lines.Length + " line(s), taller max member on " + tallerAndFewer);
            Check(fewer >= lines.Length / 2, W, "fewer towers on only " + fewer + " of " + lines.Length + " lines");

            // The earlier LP-7 knee (190-210 m on a 260 m FG line): the breakover rule never costs a tower.
            LiftTowerPlanConstants fg = LiftTowerPlanConstants.For("fixedGrip");
            Func<float, float> g = s => s <= 190f ? 1000f + 0.5f * s : s >= 210f ? 1100f : 1095f + 0.5f * (s - 190f) - 0.0125f * (s - 190f) * (s - 190f);
            LiftTowerPlan kOld = legacy.Plan((s, l) => g(s), 260f, g(0f) + fg.RopeAboveBase(0), g(260f) + fg.RopeAboveBase(0), fg);
            LiftTowerPlan kNew = PlanAndReplan("LP-17/knee", (s, l) => g(s), 260f, fg, g(0f), g(260f));
            s_Report.Add("LP-17 FG knee 190-210 m (the 2026-10-19 LP-7) | breakover rule after the tower count | " + Describe(kOld) + "  ->  " + Describe(kNew));
            Check(kNew.LineTowers <= kOld.LineTowers && kNew.Clear, W, "knee: " + Describe(kNew));
        }


        /// <summary>A straight line along +x from (0, ground(0), 0); the profile's along = world x.</summary>
        private static LiftLineEdit EditOn(Profile p, LiftTowerPlanConstants c, float ropeA, float ropeB)
        {
            var a = new float3(0f, p.Start, 0f);
            var b = new float3(p.Length, p.End, 0f);
            return new LiftLineEdit(c, a, b, ropeA, ropeB);
        }

        private static void CheckEmission(string where, LiftLineEdit e, LiftTerrainSampler t)
        {
            const string W = "planner/LP-18";
            List<LiftLineNode> nodes = e.Emit();
            LiftTowerPlan plan = e.Plan;
            Check(nodes.Count == plan.Nodes.Length, W, where + ": emitted " + nodes.Count + " node(s) for a plan of " + plan.Nodes.Length);
            if (nodes.Count != plan.Nodes.Length)
                return;
            Check(nodes[0].Kind == LiftLineNodeKind.Station && nodes[nodes.Count - 1].Kind == LiftLineNodeKind.Station &&
                  nodes[0].Position.Equals(e.A) && nodes[nodes.Count - 1].Position.Equals(e.B), W, where + ": station ends not the stub nodes");
            var inner = new float[nodes.Count - 2];
            var members = new int[nodes.Count - 2];
            for (int i = 1; i < nodes.Count - 1; i++)
            {
                LiftLineNode n = nodes[i];
                LiftPlanNode pn = plan.Nodes[i];
                float e0 = LiftTowerEdit.ElevationM(e.C, pn.Member);
                Check(n.Member == pn.Member && math.abs(n.Position.y - (pn.GroundM + e0)) < 1e-3f && math.abs(n.ElevationM - e0) < 1e-4f, W,
                      where + ": node " + (i + 1) + " not at ground + E of its member");
                e.Project(n.Position, out float lateral);
                Check(math.abs(lateral) < 1e-3f && math.abs(n.Position.x - pn.AlongM) < 1e-3f, W, where + ": node " + (i + 1) + " off the line");
                Check((n.Kind == LiftLineNodeKind.Pinned) == e.IsPinned(pn.AlongM), W, where + ": node " + (i + 1) + " pin flag");
                inner[i - 1] = pn.AlongM;
                members[i - 1] = pn.Member;
            }
            // Short is impossible on a tool line: the fit of the built line (heights only, the tool's members) is the plan itself.
            LiftTowerFit fit = s_Planner.Fit(t, e.LengthM, e.RopeA, e.RopeB, e.C, inner, false, 0f, members);
            Check(fit.Mode == LiftFitMode.Kept || !plan.Clear, W, where + ": the fit of the tool line is " + fit.Mode + " for a clear plan");
            if (plan.Clear)
                Check(Same(fit.Plan, plan), W, where + ": the fit changed the tool line: " + DescribeFit(fit) + " vs " + Describe(plan));
        }

        private static void Lp18ToolEmission()
        {
            const string W = "planner/LP-18";
            var lines = new (string Name, LogNode[] Log, string Type, float RopeA, float RopeB)[]
            {
                ("GD 619 m (log :2271)", Gd619, "gondola", 965.94f, 1113.06f),
                ("DQ 328 m (log :999)", Dq328, "detachable", 1194.16f, 1209.45f),
                ("FG 516 m (log :566)", Fg516, "fixedGrip", 947.84f, 1097.81f),
                ("tram 2292 m (log :4851)", Tram2292, "tram", 986.73f, 2132.94f),
            };
            foreach (var ln in lines)
            {
                Profile p = Reconstruct(ln.Log);
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(ln.Type);
                LiftTerrainSampler t = (s, l) => p.At(s);
                LiftLineEdit e = EditOn(p, c, ln.RopeA, ln.RopeB);
                e.Replan(t);
                s_Report.Add("LP-18 tool line " + ln.Name + " | emitted nodes = the plan, clear, the fit keeps it | " + e.Describe());
                Check(e.Plan.Clear, W, ln.Name + ": the tool's plan is not clear: " + e.Describe());
                CheckEmission(ln.Name, e, t);
            }
        }

        private static void Lp19Pins()
        {
            const string W = "planner/LP-19";
            Profile p = Reconstruct(Gd619);
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("gondola");
            LiftTerrainSampler t = (s, l) => p.At(s);
            LiftLineEdit e = EditOn(p, c, 965.94f, 1113.06f);
            LiftTowerPlan free = e.Replan(t);
            string freeText = Describe(free);

            // (a) a click on the line pins a tower there (snapped to the 5 m grid); the planner fills around it.
            Check(e.AddPin(203f) == LiftLineEditResult.Done, W, "add pin at 203 m refused");
            e.Replan(t);
            int at = e.TowerNear(205f, 0.01f);
            Check(at > 0 && e.IsPinned(205f), W, "no pinned tower at 205 m (203 snapped): " + e.Describe());
            CheckEmission("pin 205", e, t);
            for (int s = 0; s < e.Plan.Spans.Length; s++)
                Check(e.Plan.Spans[s].LengthM <= c.MaxSpanM + 1e-3f, W, "pinned plan span " + (s + 1) + " over the max: " + e.Describe());
            s_Report.Add("LP-19 GD 619 m pins | (a) pin at 203 -> tower at 205 (grid), planner fills around it | " + Describe(e.Plan));

            // (b) drag it to 300 m: the pin moves, nothing stays at 205.
            Check(e.MoveTower(205f, 298f) == LiftLineEditResult.Done, W, "move 205 -> 298 refused");
            e.Replan(t);
            Check(e.IsPinned(300f) && !e.IsPinned(205f) && e.TowerNear(205f, 0.01f) < 0 && e.TowerNear(300f, 0.01f) > 0, W,
                  "move did not land at 300 m: " + e.Describe());
            CheckEmission("pin 300", e, t);
            s_Report.Add("LP-19 GD 619 m pins | (b) drag 205 -> 298 -> pinned at 300 | " + Describe(e.Plan));

            // (c) idempotence with pins: re-planning the same pins gives the same line.
            LiftTowerPlan before = e.Plan;
            e.Replan(t);
            Check(Same(before, e.Plan), W, "re-plan with the same pins changed the line");

            // (d) right click on the pinned tower unpins it: the free plan comes back.
            Check(e.RemoveTower(300f) == LiftLineEditResult.Done && e.Pins.Count == 0, W, "unpin refused");
            e.Replan(t);
            Check(Same(e.Plan, free), W, "unpinned plan != the free plan: " + Describe(e.Plan) + " vs " + freeText);

            // (e) right click on a FREE tower: every other tower is pinned where it stands, this one goes.
            float drop = e.Plan.Nodes[1].AlongM;
            int towersBefore = e.Plan.LineTowers;
            Check(e.RemoveTower(drop) == LiftLineEditResult.Done, W, "remove free tower refused");
            Check(e.Pins.Count == towersBefore - 1, W, "remove free: " + e.Pins.Count + " pins, expected " + (towersBefore - 1));
            e.Replan(t);
            Check(e.TowerNear(drop, e.RemovedRadiusM - 0.01f) < 0, W, "a tower came back within " + e.RemovedRadiusM.ToString("F0") +
                  " m of the removed spot " + drop.ToString("F0") + " m: " + e.Describe());
            for (int s = 0; s < e.Plan.Spans.Length; s++)
                Check(e.Plan.Spans[s].LengthM <= c.MaxSpanM + 1e-3f, W, "after the remove a span exceeds the max: " + e.Describe());
            for (int i = 0; i < e.Pins.Count; i++)
                Check(e.TowerNear(e.Pins[i], 0.01f) > 0, W, "a frozen tower moved: pin " + e.Pins[i].ToString("F0"));
            CheckEmission("remove free", e, t);
            s_Report.Add("LP-19 GD 619 m pins | (e) right click the free tower at " + drop.ToString("F0") + " m: the others pinned, the planner fills what exceeds the max span | " + e.Describe());

            // (f) refusals: within MinSpanM of a station or of a pin; dragging a free tower pins it.
            e.ClearPins();
            e.Replan(t);
            Check(e.AddPin(c.MinSpanM * 0.5f) == LiftLineEditResult.TooCloseToStation, W, "pin next to the base station accepted");
            Check(e.AddPin(e.LengthM - c.MinSpanM * 0.5f) == LiftLineEditResult.TooCloseToStation, W, "pin next to the top station accepted");
            Check(e.AddPin(300f) == LiftLineEditResult.Done && e.AddPin(305f) == LiftLineEditResult.TooCloseToPin, W, "pin 5 m from a pin accepted");
            e.ClearPins();
            e.Replan(t);
            float freeAt = e.Plan.Nodes[1].AlongM;
            Check(e.MoveTower(freeAt, freeAt + 1f) == LiftLineEditResult.Done && e.IsPinned(freeAt), W, "click-drag in place did not pin the free tower");

            // (g) the registry: the built line is a tool line (either direction, 0.5 m tolerance), another line is not.
            var reg = new LiftToolLines();
            e.Replan(t);
            List<LiftLineNode> emitted = e.Emit();
            reg.Register("gondola", emitted);
            var inner = new List<float3>();
            for (int i = 1; i < emitted.Count - 1; i++)
                inner.Add(emitted[i].Position + new float3(0.3f, 5f, -0.2f));
            Check(reg.Match(inner, out int[] mem, out bool[] pin) != null && mem[0] == emitted[1].Member && pin[0], W, "registry: own line not matched");
            inner.Reverse();
            Check(reg.Match(inner, out mem, out _) != null && mem[mem.Length - 1] == emitted[1].Member, W, "registry: reversed line not matched");
            inner[0] += new float3(2f, 0f, 0f);
            Check(reg.Match(inner, out _, out _) == null, W, "registry: a node 2 m off still matched");
        }

        /// <summary>LP-20: the vanilla net-tool path (hysteresis + the fewest-towers objective; the span raise is scoped to the
        /// lift-line tool, so the engine splits at m_MaxPylonDistance FG 60 / DQ 90 / GD 250). The field lines at the engine's equal
        /// split (ceil(L / max) spans, CourseSplitSystem.cs:3487-3493), then the fit.</summary>
        private static void Lp20VanillaAfterPatch()
        {
            const string W = "planner/LP-20";
            var lines = new (string Name, LogNode[] Log, string Type, float RopeA, float RopeB, bool ExpectClear)[]
            {
                ("GD 619 m (log :2271, Short x2 tonight)", Gd619, "gondola", 965.94f, 1113.06f, true),
                ("FG 312 m (log :1231, Short tonight)", Fg312, "fixedGrip", 1169.13f, 1199.56f, false),
                ("DQ 328 m drawn in ONE click (tonight: 7 clicked spans, Moved clear)", Dq328, "detachable", 1194.16f, 1209.45f, false),
            };
            foreach (var ln in lines)
            {
                Profile p = Reconstruct(ln.Log);
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(ln.Type);
                LiftTerrainSampler t = (s, l) => p.At(s);
                float vanillaMax = ln.Type == "fixedGrip" ? 60f : ln.Type == "detachable" ? 90f : 250f; // m_MaxPylonDistance
                int spans = (int)math.ceil((p.Length - 0.16f) / vanillaMax);
                var inner = new float[spans - 1];
                for (int i = 0; i < inner.Length; i++)
                    inner[i] = p.Length * (i + 1) / spans;
                LiftTowerFit fit = s_Planner.Fit(t, p.Length, ln.RopeA, ln.RopeB, c, inner, true, vanillaMax, new int[inner.Length]);
                s_Report.Add("LP-20 vanilla net tool with round 2, " + ln.Name + " | engine split " + spans + " x " +
                             (p.Length / spans).ToString("F0") + " m, then the fit" + (ln.ExpectClear ? ": clear" : ": whatever the nodes allow (Short = use the tool)") + " | " + DescribeFit(fit));
                if (ln.ExpectClear)
                    Check(fit.Plan.Clear, W, ln.Name + ": not clear on the vanilla path: " + DescribeFit(fit));
                Check(fit.Plan.LineTowers == inner.Length, W, ln.Name + ": node count changed");
            }
        }


        // The 1,408 m gondola drawn with the vanilla tool in 12 clicks: a "Towers [nodes]" block (nodes and the span line under each);
        // Short x3, then the loop guard. Ropes from the fit header.
        private static readonly LogNode[] Gd1408 =
        {
            N(27f, 931.58f, 931.58f, 0.6f, 1.3f, 1.9f, 2.6f), N(14f, 934.82f, 931.58f, 6.4f, 9.5f, 12.6f, 11.9f),
            N(214f, 950.45f, 944.14f, -5.5f, -9.3f, -8.1f, 2.1f), N(214f, 989.32f, 989.30f, 8.4f, 17.2f, 13.5f, 3.8f),
            N(214f, 1037.74f, 1037.71f, -11.0f, -11.0f, -10.6f, 0.3f), N(214f, 1091.41f, 1091.38f, -0.9f, 4.0f, 14.9f, 17.6f),
            N(214f, 1181.81f, 1181.84f, -2.9f, -10.2f, -11.0f, -10.9f), N(32f, 1292.58f, 1292.55f, 0.1f, -0.2f, -0.2f, 0.1f),
            N(10f, 1295.42f, 1295.38f, 1.1f, 1.8f, 2.4f, 2.8f), N(22f, 1302.34f, 1299.26f, 4.5f, 4.9f, 4.0f, 2.7f),
            N(196f, 1327.77f, 1326.46f, -10.4f, -8.8f, -3.0f, -9.4f), N(37f, 1436.88f, 1434.35f, 3.5f, 1.7f, -3.2f, -4.9f),
            N(0f, 1461.15f, 1461.15f),
        };
        // The diag's own plan block for that line: its ground at the planned spots (along, ground), merged into the replay profile (the
        // chord samples alone leave 43 m gaps on the 214 m spans).
        private static readonly float2[] Gd1408Plan =
        {
            new float2(50f, 957.60f), new float2(190f, 984.25f), new float2(400f, 1013.95f), new float2(595f, 1080.36f),
            new float2(810f, 1130.90f), new float2(1005f, 1248.95f), new float2(1175f, 1327.04f), new float2(1195f, 1349.00f),
            new float2(1400f, 1461.15f),
        };

        private static Profile Merge(Profile p, float2[] extra)
        {
            var pts = new List<float2>();
            for (int i = 0; i < p.S.Count; i++)
                pts.Add(new float2(p.S[i], p.G[i]));
            pts.AddRange(extra);
            pts.Sort((a, b) => a.x.CompareTo(b.x));
            var q = new Profile();
            for (int i = 0; i < pts.Count; i++)
            {
                if (q.S.Count > 0 && math.abs(pts[i].x - q.S[q.S.Count - 1]) < 0.5f)
                    continue; // the log's own value at the node wins
                q.S.Add(pts[i].x);
                q.G.Add(pts[i].y);
            }
            return q;
        }

        /// <summary>LP-21: the 1,408 m gondola with its 11 engine nodes. Before: Short (phase (c) alone re-assigned the free plan's
        /// members greedily and broke the max rope height, "MAX HEIGHT VIOLATED" x2); after: Moved and clear, and the next re-fit of
        /// the written line is Kept with nothing to write (no loop). Plus the tram tool line Short by -0.1 m: kept within
        /// KeepTolM.</summary>
        private static void Lp21GondolaShortLoop()
        {
            const string W = "planner/LP-21";
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("gondola");
            Profile p = Merge(Reconstruct(Gd1408), Gd1408Plan);
            LiftTerrainSampler t = (s, l) => p.At(s);
            float[] inner = InnerAlong(Gd1408);
            const float ropeA = 941.58f, ropeB = 1471.15f;
            LiftTowerFit fit = s_Planner.Fit(t, p.Length, ropeA, ropeB, c, inner, true, 250f, new int[inner.Length]);
            s_Report.Add("LP-21 GD 1408 m, 11 engine nodes (log :1007 / :1096, Short x3 + loop guard) | Moved, clear, 11 towers | " + DescribeFit(fit));
            Check(fit.Mode == LiftFitMode.Moved && fit.Plan.Clear, W, "the 1,408 m gondola is still Short / violated: " + DescribeFit(fit));
            Check(fit.Plan.LineTowers == inner.Length, W, "node count changed: " + fit.Plan.LineTowers);
            LiftTowerFit again = s_Planner.Fit(t, p.Length, ropeA, ropeB, c, InnerAlong(fit.Plan), true, 250f, MembersOf(fit.Plan));
            int writes = 0;
            for (int i = 1; i < fit.Plan.Nodes.Length - 1; i++)
            {
                LiftPlanNode a = fit.Plan.Nodes[i], b = again.Plan.Nodes[i];
                float target = b.GroundM + LiftTowerEdit.ElevationM(c, b.Member);
                float y = a.GroundM + LiftTowerEdit.ElevationM(c, a.Member);
                if (a.Member != b.Member || math.abs(a.AlongM - b.AlongM) > 0.05f || LiftTowerEdit.HeightWrite(y, target))
                    writes++;
            }
            s_Report.Add("LP-21 GD 1408 m re-fit of the written line | Kept, 0 writes | " + again.Mode + ", " + writes + " write(s)");
            Check(again.Mode == LiftFitMode.Kept && writes == 0, W, "the re-fit is not identity: " + DescribeFit(again) + ", " + writes + " write(s)");

            // The tram tool line (1 tower T60 at 925 m from the base; the tool planned it from the top: +0.2 m).
            LogNode[] tram =
            {
                N(925f, 942.45f, 942.45f, 24.4f, 37.3f, 15.6f, 39.0f), N(1000f, 1353.80f, 1323.68f, 92.1f, 12.5f, 2.8f, 61.6f),
                N(0f, 2053.46f, 2050.85f),
            };
            LiftTowerPlanConstants tc = LiftTowerPlanConstants.For("tram");
            Profile tp = Reconstruct(tram);
            LiftTerrainSampler tt = (s, l) => tp.At(s);
            LiftTowerPlan standing = s_Planner.PlanFixed(tt, tp.Length, 968.70f, 2079.71f, tc, InnerAlong(tram), new[] { 2 }, null, assign: false);
            LiftTowerFit tf = s_Planner.Fit(tt, tp.Length, 968.70f, 2079.71f, tc, InnerAlong(tram), false, 1000f, new[] { 2 });
            s_Report.Add("LP-21 tram 1925 m tool line (log :2131, Short by -0.1 m) | Kept (within " + LiftTowerPlanner.KeepTolM.ToString("F1") +
                         " m: the tool's grid ran from the other end) | standing " + LiftTowerPlan.Sg(standing.MinFloorMarginM) + " m on the replay profile (4 chord samples per 1,000 m span cannot show the -0.1 m spot; the tolerance itself is checked on flat ground below) -> " + tf.Mode);
            Check(standing.MinFloorMarginM > -LiftTowerPlanner.KeepTolM ? tf.Mode == LiftFitMode.Kept : tf.Mode != LiftFitMode.Kept, W,
                  "keep tolerance: standing " + standing.MinFloorMarginM.ToString("F2") + " m gave " + tf.Mode);
            // The tolerance itself on a synthetic flat line: -0.4 m kept, -0.6 m not.
            LiftTowerPlanConstants g = LiftTowerPlanConstants.For("gondola");
            foreach (float drop in new[] { 0.4f, 0.6f })
            {
                // Rope over flat ground at 1000 m, the 200 m span's mid-span sag (4 x 0.004 x 200 x 1/4 = 0.8 m) added back: margin -drop at
                // mid-span, the lowest point.
                float rope = 1000f + g.CarrierDropM + g.FloorM - drop + g.SagRatio * 200f;
                LiftTowerFit k = s_Planner.Fit((s, l) => 1000f, 200f, rope, rope, g, new float[0], false, 250f, new int[0]);
                bool kept = k.Mode == LiftFitMode.Kept;
                Check(kept == (drop < LiftTowerPlanner.KeepTolM), W, "keep tolerance at -" + drop + " m: " + k.Mode);
            }
        }

        /// <summary>LP-22: the station clearance. Constants from the catalog's station geometry (StationRunM); no new tower inside it on
        /// any free plan of this file's lines (both directions: the tool planned the gondola from the top); a pin inside it is
        /// kept.</summary>
        private static void Lp22StationClearance()
        {
            const string W = "planner/LP-22";
            var want = new (string Type, float Clear)[] { ("fixedGrip", 12.2f), ("detachable", 25.6f), ("gondola", 25.4f), ("tram", 40f) };
            var sb = new StringBuilder();
            foreach (var w in want)
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(w.Type);
                sb.Append(w.Type).Append(' ').Append(c.StationClearM.ToString("F1")).Append(" m; ");
                Check(math.abs(c.StationClearM - w.Clear) < 0.05f, W, w.Type + ": StationClearM " + c.StationClearM + ", want " + w.Clear +
                      " (max(MinSpanM, the longest bullwheel / dock to stub run of its stations))");
            }
            s_Report.Add("LP-22 station clearance by type | max(MinSpanM, station rope run) | " + sb.ToString().TrimEnd(' ', ';'));

            var lines = new (string Name, LogNode[] Log, float2[] Extra, string Type, float RopeA, float RopeB)[]
            {
                ("GD 1408 m (playthrough :1007)", Gd1408, Gd1408Plan, "gondola", 941.58f, 1471.15f),
                ("FG 516 m (log :566)", Fg516, null, "fixedGrip", 947.84f, 1097.81f),
                ("DQ 328 m (log :999)", Dq328, null, "detachable", 1194.16f, 1209.45f),
                ("DQ 619 m (log :2155)", Dq619, null, "detachable", 960.62f, 1117.56f),
                ("GD 619 m (log :2271)", Gd619, null, "gondola", 965.94f, 1113.06f),
                ("tram 2292 m (log :4851)", Tram2292, null, "tram", 986.73f, 2132.94f),
            };
            foreach (var ln in lines)
            {
                Profile p = ln.Extra != null ? Merge(Reconstruct(ln.Log), ln.Extra) : Reconstruct(ln.Log);
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(ln.Type);
                for (int dir = 0; dir < 2; dir++)
                {
                    LiftTerrainSampler t = dir == 0 ? (LiftTerrainSampler)((s, l) => p.At(s)) : (s, l) => p.At(p.Length - s);
                    float ra = dir == 0 ? ln.RopeA : ln.RopeB, rb = dir == 0 ? ln.RopeB : ln.RopeA;
                    LiftTowerPlan plan = PlanAndReplan("LP-22/" + ln.Name + "/" + dir, t, p.Length, c, p.At(dir == 0 ? 0f : p.Length),
                                                       p.At(dir == 0 ? p.Length : 0f), ra, rb);
                    int inside = 0;
                    for (int i = 1; i < plan.Nodes.Length - 1; i++)
                    {
                        LiftPlanNode nd = plan.Nodes[i];
                        if (nd.AlongM >= c.StationClearM - 0.01f && nd.AlongM <= p.Length - c.StationClearM + 0.01f)
                            continue;
                        inside++;
                        Check((nd.Flags & LiftPlanNodeFlags.NearStation) != 0, W, ln.Name + (dir == 0 ? "" : " (reversed)") +
                              ": an unflagged tower inside the station clearance " + c.StationClearM + " m: " + Describe(plan));
                    }
                    if (ln.Name.StartsWith("GD 1408"))
                        s_Report.Add("LP-22 " + ln.Name + (dir == 0 ? " from the base" : " from the top as the tool did (:1492, spans 50/.../185/8)") +
                                     " | towers outside " + c.StationClearM.ToString("F1") + " m unless the ground leaves no clear plan (near-station) | " +
                                     inside + " inside: " + Describe(plan));
                    else
                        Check(inside == 0, W, ln.Name + (dir == 0 ? "" : " (reversed)") + ": " + inside + " tower(s) inside the clearance: " + Describe(plan));
                    Check(plan.Clear, W, ln.Name + (dir == 0 ? "" : " (reversed)") + ": not clear: " + Describe(plan));
                }
            }
            // A pin inside the clearance stays (the player's choice), and nothing else is added inside it.
            LiftTowerPlanConstants gc = LiftTowerPlanConstants.For("gondola");
            var seed = new[] { new LiftPlanNode { AlongM = 10f, Member = 0, Flags = LiftPlanNodeFlags.Seed } };
            LiftTowerPlan pinned = s_Planner.Plan((s, l) => 1000f + 0.3f * s, 600f, 1000f + gc.RopeAboveBase(0), 1180f + gc.RopeAboveBase(0), gc, seed);
            bool kept = false;
            for (int i = 1; i < pinned.Nodes.Length - 1; i++)
            {
                if (math.abs(pinned.Nodes[i].AlongM - 10f) < 0.01f)
                    kept = true;
                else
                    Check(pinned.Nodes[i].AlongM >= gc.StationClearM - 0.01f && pinned.Nodes[i].AlongM <= 600f - gc.StationClearM + 0.01f, W,
                          "a planner tower inside the clearance next to a pin: " + Describe(pinned));
            }
            Check(kept, W, "the pin at 10 m (inside the clearance) was dropped: " + Describe(pinned));
        }

        /// <summary>LP-23: the fit's ground is the player's, not our own cut. Pure UncutGround cases, then the 1,408 m gondola model:
        /// the natural ground (the replay profile) cut by the vanilla line's own edges (ceiling = edge y + 6 - 2 over the piece width),
        /// the fit with the raw cascade sampler stands towers in the trench (pivot below the natural ground by more than the 4 m that
        /// keeps a node's own cut alive = a pit), the UncutSampler stands none there.</summary>
        private static void Lp23OwnCut()
        {
            const string W = "planner/LP-23";
            // UncutGround: at the ceiling with uncut ground beside -> the plane; a valley under the rope (below the ceiling) -> raw; at the
            // ceiling but the ground beside is no higher -> raw; a cross slope reconstructs linearly.
            float g1 = LiftTowerEdit.UncutGround(100f, 100.1f, 108f, 108f, 0f, 12f, out bool c1);
            float g2 = LiftTowerEdit.UncutGround(95f, 100f, 108f, 108f, 0f, 12f, out bool c2);
            float g3 = LiftTowerEdit.UncutGround(100f, 100f, 99f, 100.2f, 0f, 12f, out bool c3);
            float g4 = LiftTowerEdit.UncutGround(100f, 100f, 104f, 112f, 6f, 12f, out bool c4);
            Check(c1 && math.abs(g1 - 108f) < 1e-3f, W, "at the ceiling: " + g1);
            Check(!c2 && g2 == 95f, W, "below the ceiling (a valley): " + g2);
            Check(!c3 && g3 == 100f, W, "uncut beside: " + g3);
            Check(c4 && math.abs(g4 - 110f) < 1e-3f, W, "cross slope plane at +6 of ±12: " + g4);

            // The model. Natural ground = the replay profile, flat across; the vanilla line's own cut (its 13 node points, straight edges,
            // gondola net m_Hanging 0, ceiling = edge y + 4) applies within the piece half width + 2 m; beyond, natural.
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("gondola");
            Profile p = Merge(Reconstruct(Gd1408), Gd1408Plan);
            int n = Gd1408.Length;
            var pts = new float3[n];
            var cum = new float[n];
            for (int i = 0; i < n; i++)
            {
                cum[i] = i == 0 ? 0f : cum[i - 1] + Gd1408[i - 1].SpanXz;
                pts[i] = new float3(cum[i], Gd1408[i].Y, 0f);
            }
            var off = new float[n - 1];
            for (int i = 0; i < off.Length; i++)
                off[i] = 6f + LiftTowerEdit.PieceMinHeightOffsetM;
            float cutHalf = c.HalfWidthM + 2f;
            LiftTerrainSampler natural = (s, l) => p.At(s);
            LiftTerrainSampler cascade = (s, l) =>
            {
                float g = p.At(s);
                if (math.abs(l) > cutHalf)
                    return g;
                int seg = 0;
                while (seg < n - 2 && s > cum[seg + 1])
                    seg++;
                float t = math.saturate((s - cum[seg]) / math.max(cum[seg + 1] - cum[seg], 1e-3f));
                return math.min(g, LiftTowerEdit.HangingY(pts[seg], pts[seg + 1], 0f, t) + off[seg]);
            };
            int cutSamples = 0;
            LiftTerrainSampler uncut = LiftTowerEdit.UncutSampler(cascade, pts, cum, 0f, off, c.HalfWidthM + LiftTowerEdit.CutFarMarginM, _ => cutSamples++);
            float[] inner = InnerAlong(Gd1408);
            int Pits(LiftTerrainSampler sampler, out string worst)
            {
                LiftTowerFit f = s_Planner.Fit(sampler, p.Length, 941.58f, 1471.15f, c, inner, true, 250f, new int[inner.Length]);
                int pits = 0;
                float deepest = 0f;
                worst = "none";
                for (int i = 1; i < f.Plan.Nodes.Length - 1; i++)
                {
                    LiftPlanNode nd = f.Plan.Nodes[i];
                    float sunk = natural(nd.AlongM, 0f) - nd.GroundM; // pivot = the fit's ground (top-anchored), vs the player's ground
                    if (sunk > 6f + LiftTowerEdit.PieceMinHeightOffsetM)
                        pits++;
                    if (sunk > deepest)
                    {
                        deepest = sunk;
                        worst = "@" + nd.AlongM.ToString("F0") + " m " + sunk.ToString("F1") + " m under";
                    }
                }
                return pits;
            }
            int before = Pits(cascade, out string wb);
            int after = Pits(uncut, out string wa);
            s_Report.Add("LP-23 GD 1408 m in its own trench (model: vanilla edges cut to edge y + 4) | raw sampler stands towers in the trench, " +
                         "the T127 sampler none | raw: " + before + " tower(s) > 4 m under the natural ground (deepest " + wb + "); T127: " + after +
                         " (deepest " + wa + "), " + cutSamples + " sample(s) reconstructed");
            Check(before > 0, W, "the model does not reproduce a sunken tower with the raw sampler (deepest " + wb + ")");
            Check(after == 0, W, "the T127 sampler still stands " + after + " tower(s) in the trench (deepest " + wa + ")");
            // Natural ground and no cut: the sampler is the identity.
            int idCut = 0;
            LiftTerrainSampler same = LiftTowerEdit.UncutSampler(natural, pts, cum, 0f, new float[] { 40f }, 12f, _ => idCut++);
            Check(same(500f, 0f) == natural(500f, 0f) && idCut == 0, W, "uncut ground changed by the T127 sampler");
        }

        /// <summary>LP-24: tower numbers 1..N base to top by distance along the line; reversed when the distances run from the top; ties
        /// keep the input order; an edit renumbers only the towers after it.</summary>
        private static void Lp24TowerNumbers()
        {
            const string W = "planner/LP-24";
            int[] a = LiftTowerNumbering.Numbers(new[] { 300f, 50f, 120f, 600f });
            Check(a[0] == 3 && a[1] == 1 && a[2] == 2 && a[3] == 4, W, "base first: " + string.Join(",", a));
            int[] r = LiftTowerNumbering.Numbers(new[] { 300f, 50f, 120f, 600f }, baseIsFirst: false);
            Check(r[0] == 2 && r[1] == 4 && r[2] == 3 && r[3] == 1, W, "from the top: " + string.Join(",", r));
            int[] tie = LiftTowerNumbering.Numbers(new[] { 100f, 100f });
            Check(tie[0] == 1 && tie[1] == 2, W, "tie: " + string.Join(",", tie));
            Check(LiftTowerNumbering.Numbers(new float[0]).Length == 0 && LiftTowerNumbering.Numbers(null).Length == 0, W, "empty");
            // A section edit: the tower at 300 m moves to 310 m (same order) -> no number changes; a tower added at 200 m -> only the
            // towers beyond it shift by one.
            int[] moved = LiftTowerNumbering.Numbers(new[] { 310f, 50f, 120f, 600f });
            Check(string.Join(",", moved) == string.Join(",", a), W, "a move in place renumbered: " + string.Join(",", moved));
            int[] added = LiftTowerNumbering.Numbers(new[] { 300f, 50f, 120f, 600f, 200f });
            Check(added[1] == 1 && added[2] == 2 && added[4] == 3 && added[0] == 4 && added[3] == 5, W, "insert: " + string.Join(",", added));
            s_Report.Add("LP-24 tower numbers | base -> top by distance, dense, an edit shifts only the towers after it | " +
                         "{300,50,120,600} -> " + string.Join(",", a) + "; from the top " + string.Join(",", r) + "; + a tower at 200 -> " + string.Join(",", added));
        }

        /// <summary>LP-25 (DECISIONS 381): the tram's 3 km free span with the sag check intact. Stations on ground 1,000 m at both ends,
        /// ropes on the shortest member. (a) a 3,000 m valley 300 m deep is ONE clear span, its mid-span rope 12 m (3,000 x 0.004) under
        /// the chord, and the tool emits only the two stations; (b) the same valley with a ridge at mid-span up to station level: the bare
        /// chord clears it (+10.75 m) but the sagging rope does not (-1.25 m), so the free plan puts a tower there and the fixed fit of
        /// the bare span is Short; (c) a 3,200 m valley needs one tower (the cap); (d) 3,000 m of flat ground needs one tower (the sag
        /// leaves the cabin 3.75 m up, under the 5 m floor); (e) the 3 km plans stay fast.</summary>
        private static void Lp25TramLongSpan()
        {
            const string W = "planner/LP-25";
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("tram");
            Check(c.MaxSpanM == LiftTowerPlanConstants.TramMaxSpanM && c.MaxSpanM == 3000f, W, "tram max span " + c.MaxSpanM + ", expected 3000");
            const float L = 3000f, G = 1000f;
            float rope = G + c.RopeAboveBase(0);
            LiftTerrainSampler valley = (s, l) => G - 300f * math.sin(math.PI * math.saturate(s / L));
            LiftTerrainSampler ridge = (s, l) => math.max(valley(s, l), G - 0.5f * math.abs(s - 1500f));

            // (a) the valley: one span, the engine's parabola.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            LiftTowerPlan a = PlanAndReplan("LP-25a", valley, L, c, G, G);
            double msA = sw.Elapsed.TotalMilliseconds;
            AddReport("LP-25a tram 3,000 m valley", "0 line towers, one 3,000 m span, clear", a);
            Check(a.LineTowers == 0 && a.Clear && a.Spans.Length == 1 && math.abs(a.Spans[0].LengthM - L) < 1e-2f, W, "valley: " + Describe(a));
            float mid = a.RopeAt(0, 1500f);
            Check(math.abs(rope - mid - L * c.SagRatio) < 0.05f, W, "mid-span sag " + (rope - mid).ToString("F2") + " m, expected " + (L * c.SagRatio).ToString("F2"));
            LiftLineEdit e = new LiftLineEdit(c, new float3(0f, G, 0f), new float3(L, G, 0f), rope, rope);
            e.Replan(valley);
            Check(e.Plan.Clear && e.Emit().Count == 2, W, "tool on the valley: " + e.Describe());
            CheckEmission("LP-25a tram 3 km valley", e, valley);
            s_Report.Add("LP-25a tool on the 3,000 m valley | 2 nodes (the stations), clear | " + e.Describe() +
                         "; mid-span sag " + (rope - mid).ToString("F2") + " m");

            // (b) a ridge the chord clears but the sag does not.
            float need = c.CarrierDropM + c.FloorM;
            float chordMargin = rope - need - G;
            float sagMargin = rope - L * c.SagRatio - need - G;
            Check(chordMargin > 0f && sagMargin < 0f, W, "ridge arithmetic: chord " + chordMargin.ToString("F2") + ", with sag " + sagMargin.ToString("F2"));
            LiftTowerPlan bare = s_Planner.PlanFixed(ridge, L, rope, rope, c, new float[0]);
            Check(!bare.Clear && bare.Spans[0].FloorViolated && math.abs(bare.Spans[0].MinFloorMarginAtM - 1500f) <= c.StepM, W,
                  "bare 3 km span over the ridge should violate the floor at 1,500 m: " + Describe(bare));
            sw.Restart();
            LiftTowerPlan b = PlanAndReplan("LP-25b", ridge, L, c, G, G);
            double msB = sw.Elapsed.TotalMilliseconds;
            AddReport("LP-25b tram 3,000 m valley + mid ridge at station level", "a tower at the ridge, every span clear", b);
            Check(b.LineTowers >= 1 && b.Clear, W, "ridge: " + Describe(b));
            LiftTowerFit fit = s_Planner.Fit(ridge, L, rope, rope, c, new float[0], true, c.MaxSpanM, new int[0]);
            Check(fit.Mode == LiftFitMode.Short && fit.Needed >= 1, W, "fixed fit of the bare span: " + fit.Mode + ", needs " + fit.Needed);
            s_Report.Add("LP-25b bare 3,000 m span over the ridge | floor violated at 1,500 m (chord +" + chordMargin.ToString("F2") +
                         " m, with the 12 m sag " + sagMargin.ToString("F2") + " m); fixed fit Short | " + Describe(bare) + "; fit " + fit.Mode +
                         ", needs " + fit.Needed);

            // (c) past the cap.
            LiftTerrainSampler valley32 = (s, l) => G - 300f * math.sin(math.PI * math.saturate(s / 3200f));
            LiftTowerPlan cc = PlanAndReplan("LP-25c", valley32, 3200f, c, G, G);
            AddReport("LP-25c tram 3,200 m valley", "1 line tower (span cap), clear", cc);
            Check(cc.LineTowers == 1 && cc.Clear, W, "3,200 m valley: " + Describe(cc));

            // (d) flat: the sag alone needs a tower.
            LiftTowerPlan d = PlanAndReplan("LP-25d", (s, l) => G, L, c, G, G);
            AddReport("LP-25d tram 3,000 m flat", "1 line tower (12 m sag leaves the cabin 3.75 m up), clear", d);
            Check(d.LineTowers == 1 && d.Clear, W, "flat 3 km: " + Describe(d));

            // (e) cost: each PlanAndReplan above is 3 plans; the tool re-plans once per edit.
            Check(msA < 1500.0 && msB < 1500.0, W, "3 km plans too slow: valley " + msA.ToString("F1") + " ms, ridge " + msB.ToString("F1") + " ms (3 plans each)");
            s_Report.Add("LP-25e 3 km plan time (3 plans: plan, determinism, idempotence) | < 1,500 ms | valley " + msA.ToString("F1") +
                         " ms, ridge " + msB.ToString("F1") + " ms");
        }

        private static int[] MembersOf(LiftTowerPlan p)
        {
            var m = new int[p.Nodes.Length - 2];
            for (int i = 1; i < p.Nodes.Length - 1; i++)
                m[i - 1] = p.Nodes[i].Member;
            return m;
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
