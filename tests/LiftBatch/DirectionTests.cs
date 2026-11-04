// The tower planner gives the same plan, mirrored, whichever end is A (planner-direction-2026-11-04). Where-tags `direction/DR-n`.
// Every case draws one straight line along +x from x 0 to x L over a world ground H(x, z) and plans it from x 0 ("forward") and from
// x L ("backward") the way the lift tool does (LiftLineEdit: pairs, the edge slope rule, the end node heights, the ground sampled from
// either end with PointAt / PointFromB), then compares the backward plan mirrored with the forward one.
// DR-0 PlansFromB: the lower rope first, exact and antisymmetric; equal ropes: the world key; no key: A.
// DR-1 every lift type, station to station, asymmetric profiles on and off the 5 / 10 m grid: the tool's path mirrors EXACTLY (nodes,
//      members, flags, ropes, deflections, kinds, spans); the planner alone (the fit / diag path, no B sampler: the planner mirrors the
//      ground itself) within the snap step with the same members and pairs; and the sweep binds: AsGiven (the planner before the rule,
//      working from A as given) differs on part of it.
// DR-2 a station end and a loose end with its outer junction point (the junction judged; DQ / GD give it a partner), from either end.
// DR-3 pins and a removed tower, set at the same world spots from either end: the pins, the removed spot and the plan mirror.
// DR-4 crests that need a breakover pair (FG / DQ / GD, on and off the grid): the same pair from either end; the PAIR note names it.
// DR-5 the FG station pair at x -1200 (bridge run 2026-11-03), reconstructed from the two logged plans (approximate: the terrain is not
//      logged): the same plan from either end, the old base-first one; AsGiven differs.
// DR-6 equal ropes (the world key orders the ends); Plan(Plan) from B is a fixed point; Fit's free plan and Needed either way.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    public static class DirectionTests
    {
        private static Func<bool, string, string, bool> s_Check;
        private static readonly List<string> s_Report = new List<string>();

        public static IReadOnlyList<string> Report => s_Report;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            s_Report.Clear();
            Dr0Order();
            Dr1Sweep();
            Dr2Junction();
            Dr3Pins();
            Dr4Pairs();
            Dr5FieldPair();
            Dr6EqualRopesFixedPointFit();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);
        private static string F(float v, string f = "F0") => v.ToString(f, CultureInfo.InvariantCulture);

        // ── the world line ──────────────────────────────────────────────────────────────────────────────────────────────────
        private sealed class World
        {
            public string Label;
            public LiftTowerPlanConstants C;
            public float L;
            public Func<float, float, float> H;   // ground at world (x, z)
            public bool Station0 = true, StationL = true;
            public float Above0, AboveL;           // end rope above the end node minus RopeAboveBase(0) (a station's rope anchor)
            public float OuterDist0 = float.NaN, OuterRope0 = float.NaN, OuterDistL = float.NaN, OuterRopeL = float.NaN;

            public float Y0 => H(0f, 0f);
            public float YL => H(L, 0f);
            public float R0 => Y0 + C.RopeAboveBase(0) + Above0;
            public float RL => YL + C.RopeAboveBase(0) + AboveL;
        }

        /// <summary>The lift tool's edit from x 0 (forward) or x L (backward), its outer points set, and its two samplers (from its A
        /// and from its B, as LiftLineToolSystem.Replan builds them).</summary>
        private static LiftLineEdit Edit(World w, bool backward, out LiftTerrainSampler fromA, out LiftTerrainSampler fromB)
        {
            var p0 = new float3(0f, w.Y0, 0f);
            var pL = new float3(w.L, w.YL, 0f);
            LiftLineEdit e = backward
                ? new LiftLineEdit(w.C, pL, p0, w.RL, w.R0, w.StationL, w.Station0)
                : new LiftLineEdit(w.C, p0, pL, w.R0, w.RL, w.Station0, w.StationL);
            e.OuterDistA = backward ? w.OuterDistL : w.OuterDist0;
            e.OuterRopeA = backward ? w.OuterRopeL : w.OuterRope0;
            e.OuterDistB = backward ? w.OuterDist0 : w.OuterDistL;
            e.OuterRopeB = backward ? w.OuterRope0 : w.OuterRopeL;
            Func<float, float, float> h = w.H;
            fromA = (s, l) => { float3 p = e.PointAt(s, l); return h(p.x, p.z); };
            fromB = (s, l) => { float3 p = e.PointFromB(s, l); return h(p.x, p.z); };
            return e;
        }

        private static LiftTowerPlan Replan(LiftLineEdit e, LiftTerrainSampler fromA, LiftTerrainSampler fromB) => e.Replan(fromA, fromB);

        /// <summary>The planner alone with the tool's rules, from x 0 or x L; no B sampler (the planner mirrors the ground itself, as for
        /// the fit, the diag and the quick-runs planner), no world keys; <paramref name="asGiven"/> = the planner before the rule.</summary>
        private static LiftTowerPlan Direct(World w, bool backward, bool asGiven)
        {
            float len = w.L;
            Func<float, float, float> h = w.H;
            LiftTerrainSampler t = backward ? (LiftTerrainSampler)((s, l) => h(len - s, l)) : (s, l) => h(s, -l);
            var pl = new LiftTowerPlanner
            {
                Pairs = true, CheckEdgeSlope = true, AsGiven = asGiven,
                StationA = backward ? w.StationL : w.Station0, StationB = backward ? w.Station0 : w.StationL,
                EndNodeYA = backward ? w.YL : w.Y0, EndNodeYB = backward ? w.Y0 : w.YL,
                EndOuterDistA = backward ? w.OuterDistL : w.OuterDist0, EndOuterRopeA = backward ? w.OuterRopeL : w.OuterRope0,
                EndOuterDistB = backward ? w.OuterDist0 : w.OuterDistL, EndOuterRopeB = backward ? w.OuterRope0 : w.OuterRopeL,
            };
            return backward ? pl.Plan(t, len, w.RL, w.R0, w.C) : pl.Plan(t, len, w.R0, w.RL, w.C);
        }

        /// <summary>Null when <paramref name="back"/> mirrored is <paramref name="fwd"/>, else the first difference. Always: the node
        /// count, positions within <paramref name="posTol"/>, members, the Pair / OverCap flags. <paramref name="exact"/>: every flag,
        /// rope, judged angle and kind, every span (length, margin, flags) and the per-end fields, bit for bit.</summary>
        private static string Mirror(LiftTowerPlan fwd, LiftTowerPlan back, float posTol, bool exact)
        {
            if (fwd.Nodes.Length != back.Nodes.Length)
                return "towers " + fwd.LineTowers + " forward, " + back.LineTowers + " backward";
            int n = fwd.Nodes.Length;
            float len = fwd.LengthM;
            const LiftPlanNodeFlags PairFlags = LiftPlanNodeFlags.Pair | LiftPlanNodeFlags.OverCap;
            for (int i = 0; i < n; i++)
            {
                LiftPlanNode a = fwd.Nodes[i], b = back.Nodes[n - 1 - i];
                string at = "node " + i + " @" + F(a.AlongM, "F1") + ": ";
                if (math.abs(a.AlongM - (len - b.AlongM)) > posTol)
                    return at + "backward @" + F(len - b.AlongM, "F1");
                if (a.Member != b.Member)
                    return at + "member " + a.Member + ", backward " + b.Member;
                if ((a.Flags & PairFlags) != (b.Flags & PairFlags))
                    return at + "pair / over-cap [" + LiftTowerPlan.FlagText(a.Flags) + "], backward [" + LiftTowerPlan.FlagText(b.Flags) + "]";
                if (!exact)
                    continue;
                if (a.AlongM != len - b.AlongM || a.Flags != b.Flags || a.RopeM != b.RopeM || a.GroundM != b.GroundM || a.HighSideM != b.HighSideM)
                    return at + "position / flags / rope / ground not exact";
                if (a.HasAngles != b.HasAngles || a.DeflectionDeg != b.DeflectionDeg || a.TightDeg != b.TightDeg || a.HeavyDeg != b.HeavyDeg ||
                    a.Kind != b.Kind || a.CapDeg != b.CapDeg)
                    return at + "angles d " + F(a.DeflectionDeg, "F3") + " " + a.Kind + ", backward d " + F(b.DeflectionDeg, "F3") + " " + b.Kind;
            }
            if (!exact)
                return null;
            int sc = fwd.Spans.Length;
            for (int i = 0; i < sc; i++)
            {
                LiftPlanSpan a = fwd.Spans[i], b = back.Spans[sc - 1 - i];
                if (a.LengthM != b.LengthM || a.MinFloorMarginM != b.MinFloorMarginM || a.MaxRopeAboveM != b.MaxRopeAboveM ||
                    a.FloorViolated != b.FloorViolated || a.HeightViolated != b.HeightViolated || a.SlopeViolated != b.SlopeViolated ||
                    a.EdgeSlope != b.EdgeSlope || a.MinFloorMarginAtM != b.LengthM - b.MinFloorMarginAtM)
                    return "span " + i + " differs";
            }
            if (fwd.StationA != back.StationB || fwd.StationB != back.StationA || fwd.EndCarrierA != back.EndCarrierB || fwd.EndCarrierB != back.EndCarrierA)
                return "per-end fields not swapped";
            if (fwd.SampleAlongM.Length != back.SampleAlongM.Length)
                return "profile length";
            for (int q = 0, m = fwd.SampleAlongM.Length; q < m; q++)
                if (fwd.SampleAlongM[q] != len - back.SampleAlongM[m - 1 - q] || fwd.SampleHighM[q] != back.SampleHighM[m - 1 - q] ||
                    fwd.SampleFloorM[q] != back.SampleFloorM[m - 1 - q])
                    return "profile sample " + q;
            if (fwd.AngleWork != back.AngleWork || fwd.AngleNotes.Count != back.AngleNotes.Count)
                return "angle work " + fwd.AngleWork + " / " + back.AngleWork + ", notes " + fwd.AngleNotes.Count + " / " + back.AngleNotes.Count;
            return null;
        }

        private static string Towers(LiftTowerPlan p, bool mirror = false)
        {
            var list = new List<string>();
            for (int i = 1; i < p.Nodes.Length - 1; i++)
            {
                LiftPlanNode n = p.Nodes[i];
                list.Add(p.Constants.Ladder[n.Member].Name.Replace("SkiLiftTower", "").Replace("SkiTramTower", "T") + "@" +
                         F(mirror ? p.LengthM - n.AlongM : n.AlongM) + ((n.Flags & LiftPlanNodeFlags.Pair) != 0 ? "*" : string.Empty) +
                         ((n.Flags & LiftPlanNodeFlags.OverCap) != 0 ? "!" : string.Empty));
            }
            if (mirror)
                list.Reverse();
            return (list.Count == 0 ? "no tower" : string.Join(", ", list)) + (p.Clear ? ", clear" : ", VIOLATED");
        }

        private static Func<float, float, float> Hump(float g, float h, float x0, float w, float bow, float len, float side) =>
            (x, z) => 1000f + g * x + bow * x * x / len + h * math.exp(-(x - x0) * (x - x0) / (w * w)) + side * z;

        private static Func<float, float, float> Crest(float g1, float up, float g2, float side) =>
            (x, z) => 1000f + (x <= up ? g1 * x : g1 * up + g2 * (x - up)) + side * z;

        // ── DR-0: the order rule ────────────────────────────────────────────────────────────────────────────────────────────
        private static void Dr0Order()
        {
            const string W = "direction/DR-0";
            float2 none = new float2(float.NaN), k0 = new float2(0f, 0f), k1 = new float2(10f, -5f), k2 = new float2(10f, 5f);
            Check(LiftTowerPlanner.PlansFromB(1010f, 1000f, none, none) && !LiftTowerPlanner.PlansFromB(1000f, 1010f, none, none), W,
                  "not from the lower rope");
            Check(LiftTowerPlanner.PlansFromB(1000.0001f, 1000f, none, none) && !LiftTowerPlanner.PlansFromB(1000f, 1000.0001f, none, none), W,
                  "a tiny rope difference is not exact");
            Check(!LiftTowerPlanner.PlansFromB(1000f, 1000f, none, none), W, "equal ropes, no key: not A");
            Check(!LiftTowerPlanner.PlansFromB(1000f, 1000f, k0, none) && !LiftTowerPlanner.PlansFromB(1000f, 1000f, none, k0), W, "one key alone decided");
            Check(!LiftTowerPlanner.PlansFromB(1000f, 1000f, k0, k1) && LiftTowerPlanner.PlansFromB(1000f, 1000f, k1, k0), W, "keys: not the smaller x first");
            Check(!LiftTowerPlanner.PlansFromB(1000f, 1000f, k1, k2) && LiftTowerPlanner.PlansFromB(1000f, 1000f, k2, k1), W, "keys: equal x, not the smaller z first");
            // antisymmetric on a grid of ropes and keys: swapping the ends always flips the answer unless both are the same end
            int pairs = 0;
            float[] ropes = { 999f, 1000f, 1000f + 1e-4f, 1001f };
            float2[] keys = { none, k0, k1, k2 };
            foreach (float ra in ropes)
            foreach (float rb in ropes)
            foreach (float2 ka in keys)
            foreach (float2 kb in keys)
            {
                bool tie = ra == rb && (!math.all(math.isfinite(ka)) || !math.all(math.isfinite(kb)) || math.all(ka == kb));
                bool ab = LiftTowerPlanner.PlansFromB(ra, rb, ka, kb), ba = LiftTowerPlanner.PlansFromB(rb, ra, kb, ka);
                Check(tie ? !ab && !ba : ab != ba, W, "not antisymmetric: ropes " + ra + " / " + rb + ", keys " + ka + " / " + kb);
                pairs++;
            }
            s_Report.Add("DR-0 PlansFromB | the lower rope first, then the smaller world x, z; no key: A; swapping the ends flips it | " + pairs + " combinations");
        }

        // ── DR-1: the sweep ─────────────────────────────────────────────────────────────────────────────────────────────────
        private static void Dr1Sweep()
        {
            const string W = "direction/DR-1";
            int lines = 0, fromB = 0, asGivenDiffer = 0, directDiffer = 0;
            double msFwd = 0, msBack = 0;
            var examples = new List<string>();
            foreach (string id in new[] { "fixedGrip", "detachable", "gondola", "tram" })
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(id);
                float baseLen = id == "fixedGrip" ? 400f : id == "tram" ? 2000f : 1000f;
                // on the grid, and off it by a fraction of a step (the old grid's remainder at B)
                foreach (float len in new[] { baseLen, baseLen + 0.27f * c.StepM + 1f, baseLen + 0.83f * c.StepM })
                foreach (float g in new[] { 0.14f, -0.31f })
                for (int shape = 0; shape < 3; shape++)
                {
                    Func<float, float, float> h = shape == 0 ? Hump(g, 0f, 0f, 1f, 0.03f, len, 0.05f)
                        : shape == 1 ? Hump(g, 0.03f * len, 0.33f * len, 0.07f * len, 0f, len, -0.08f)
                        : Hump(g, -0.025f * len, 0.6f * len, 0.1f * len, 0.02f, len, 0.12f);
                    var w = new World { Label = id + " " + F(len, "F1") + " m, grade " + F(g, "F2") + ", shape " + shape, C = c, L = len, H = h,
                                        Above0 = -0.4f, AboveL = 1.1f };
                    LiftLineEdit ef = Edit(w, false, out LiftTerrainSampler fa, out LiftTerrainSampler fb);
                    LiftLineEdit eb = Edit(w, true, out LiftTerrainSampler ba, out LiftTerrainSampler bb);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    LiftTowerPlan pf = Replan(ef, fa, fb);
                    msFwd += sw.Elapsed.TotalMilliseconds;
                    sw.Restart();
                    LiftTowerPlan pb = Replan(eb, ba, bb);
                    msBack += sw.Elapsed.TotalMilliseconds;
                    lines++;
                    fromB += ef.FromB ? 1 : 0;
                    Check(ef.FromB != eb.FromB, W, w.Label + ": both edits work from the same side of their own A");
                    string diff = Mirror(pf, pb, 0f, true);
                    Check(diff == null, W, w.Label + " (tool path): " + diff + " | forward " + Towers(pf) + " | backward " + Towers(pb, true));
                    // the planner alone: the ground mirrored by the planner (float rounding), within the snap step
                    string direct = Mirror(Direct(w, false, false), Direct(w, true, false), c.StepM - 1e-3f, false);
                    if (direct != null)
                        directDiffer++;
                    Check(direct == null, W, w.Label + " (planner alone): " + direct);
                    // binding: the planner before the rule
                    LiftTowerPlan gf = Direct(w, false, true), gb = Direct(w, true, true);
                    string given = Mirror(gf, gb, c.StepM - 1e-3f, false);
                    if (given != null)
                    {
                        asGivenDiffer++;
                        if (examples.Count < 4)
                            examples.Add(w.Label + ": " + given + " (forward " + Towers(gf) + " | backward " + Towers(gb, true) + ")");
                    }
                }
            }
            Check(asGivenDiffer > 0, W, "the sweep never binds: the planner working from A as given plans every line the same both ways");
            Check(fromB > 0 && fromB < lines, W, "the sweep never works from one side: " + fromB + " of " + lines + " forward edits from B");
            s_Report.Add("DR-1 sweep, every type, station to station, " + lines + " line(s) on and off the grid | the tool path mirrors exactly, the " +
                         "planner alone within the snap step | " + directDiffer + " differ alone; before the rule (AsGiven) " + asGivenDiffer +
                         " of " + lines + " differ; " + fromB + " forward edits work from B; tool plans " + (msFwd / lines).ToString("F1", CultureInfo.InvariantCulture) +
                         " ms forward, " + (msBack / lines).ToString("F1", CultureInfo.InvariantCulture) + " ms backward on average");
            foreach (string e in examples)
                s_Report.Add("DR-1 before the rule | " + e);
        }

        // ── DR-2: a station end and a loose end with its outer junction point ───────────────────────────────────────────────
        private static void Dr2Junction()
        {
            const string W = "direction/DR-2";
            foreach (string id in new[] { "fixedGrip", "detachable", "gondola" })
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(id);
                // Level ground 402.6 m from a station at x 0 to a loose end at x L on a crest: the built line beyond it arrived climbing at
                // g (its far node 80 m beyond, a tower of the shortest member). The station's rope sits above or below the loose end's.
                float g = id == "fixedGrip" ? 0.15f : id == "detachable" ? 0.6f : 0.9f;
                foreach (float stationAbove in new[] { -0.6f, 0.6f })
                {
                    var w = new World
                    {
                        Label = id + " station -> loose end (station rope " + F(stationAbove, "F1") + ")", C = c, L = 402.6f, H = (x, z) => 1000f,
                        Station0 = true, StationL = false, Above0 = stationAbove, AboveL = 0f,
                        OuterDistL = 80f, OuterRopeL = 1000f - g * 80f + c.RopeAboveBase(0),
                    };
                    LiftLineEdit ef = Edit(w, false, out LiftTerrainSampler fa, out LiftTerrainSampler fb);
                    LiftLineEdit eb = Edit(w, true, out LiftTerrainSampler ba, out LiftTerrainSampler bb);
                    LiftTowerPlan pf = Replan(ef, fa, fb), pb = Replan(eb, ba, bb);
                    int last = pf.Nodes.Length - 1;
                    Check(pf.Nodes[last].HasAngles && pb.Nodes[0].HasAngles && !pf.Nodes[0].HasAngles, W, w.Label + ": the junction not judged at its end");
                    string diff = Mirror(pf, pb, 0f, true);
                    Check(diff == null, W, w.Label + ": " + diff + " | forward " + Towers(pf) + " | backward " + Towers(pb, true));
                    bool partner = (pf.Nodes[last].Flags & LiftPlanNodeFlags.Pair) != 0;
                    if (id != "fixedGrip")
                        Check(partner, W, w.Label + ": no partner at the junction: " + Towers(pf) + " | " + string.Join(" | ", pf.AngleNotes));
                    s_Report.Add("DR-2 " + w.Label + " | the same plan both ways, the junction judged" + (id != "fixedGrip" ? " with its partner" : "") +
                                 " | works from " + (ef.FromB ? "the loose end" : "the station") + "; " + Towers(pf) + " | junction " +
                                 pf.AngleText(last) + " | " + string.Join(" | ", pf.AngleNotes));
                }
            }
        }

        // ── DR-3: pins and a removed tower ──────────────────────────────────────────────────────────────────────────────────
        private static void Dr3Pins()
        {
            const string W = "direction/DR-3";
            foreach (string id in new[] { "detachable", "fixedGrip" })
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(id);
                float len = id == "detachable" ? 1003.7f : 402.6f;
                // x 0 is the TOP here (the ground falls toward x L), so the forward edit works from B and snaps its pins from x L.
                var w = new World { Label = id + " " + F(len, "F1") + " m, top at x 0", C = c, L = len, H = Hump(-0.28f, 0.02f * len, 0.45f * len, 0.08f * len, 0f, len, 0f) };
                LiftLineEdit ef = Edit(w, false, out LiftTerrainSampler fa, out LiftTerrainSampler fb);
                LiftLineEdit eb = Edit(w, true, out LiftTerrainSampler ba, out LiftTerrainSampler bb);
                Check(ef.FromB && !eb.FromB, W, w.Label + ": not planned from the base");
                float[] pinsX = { 0.31f * len, 0.64f * len };
                foreach (float x in pinsX)
                {
                    Check(ef.AddPin(x) == LiftLineEditResult.Done && eb.AddPin(len - x) == LiftLineEditResult.Done, W, w.Label + ": a pin refused at x " + F(x));
                }
                LiftTowerPlan pf = Replan(ef, fa, fb), pb = Replan(eb, ba, bb);
                bool pinsMirror = ef.Pins.Count == eb.Pins.Count;
                for (int i = 0; pinsMirror && i < ef.Pins.Count; i++)
                    pinsMirror = math.abs(ef.Pins[i] - (len - eb.Pins[eb.Pins.Count - 1 - i])) < 1e-3f && ef.TowerNear(ef.Pins[i], 1e-3f) > 0;
                Check(pinsMirror, W, w.Label + ": pins not at the same world spots, or not kept: " + string.Join(", ", ef.Pins) + " / " + string.Join(", ", eb.Pins));
                // a pin snaps to the grid of the end the planner works from: x L - k x StepM here
                float r = (len - ef.Pins[0]) / c.StepM;
                Check(math.abs(r - math.round(r)) < 1e-3f, W, w.Label + ": the pin at " + ef.Pins[0] + " is not on the base's grid");
                string diff = Mirror(pf, pb, 0f, true);
                Check(diff == null, W, w.Label + ", pinned: " + diff + " | forward " + Towers(pf) + " | backward " + Towers(pb, true));
                // remove the first free tower (by x) from both
                float spot = float.NaN;
                for (int i = 1; i < pf.Nodes.Length - 1 && float.IsNaN(spot); i++)
                    if (!ef.IsPinned(pf.Nodes[i].AlongM))
                        spot = pf.Nodes[i].AlongM;
                if (!Check(!float.IsNaN(spot), W, w.Label + ": no free tower to remove: " + Towers(pf)))
                    continue;
                float spotB = float.NaN;
                for (int i = 1; i < pb.Nodes.Length - 1; i++)
                    if (math.abs(pb.Nodes[i].AlongM - (len - spot)) < 1e-3f)
                        spotB = pb.Nodes[i].AlongM;
                Check(ef.RemoveTower(spot) == LiftLineEditResult.Done && eb.RemoveTower(spotB) == LiftLineEditResult.Done, W, w.Label + ": remove refused");
                LiftTowerPlan qf = Replan(ef, fa, fb), qb = Replan(eb, ba, bb);
                diff = Mirror(qf, qb, 0f, true);
                Check(diff == null, W, w.Label + ", tower at x " + F(spot) + " removed: " + diff + " | forward " + Towers(qf) + " | backward " + Towers(qb, true));
                Check(ef.Removed.Count == 1 && eb.Removed.Count == 1 && math.abs(ef.Removed[0] - (len - eb.Removed[0])) < 1e-3f, W, w.Label + ": removed spots differ");
                s_Report.Add("DR-3 " + w.Label + ", pins at x " + F(ef.Pins[0], "F1") + " / " + F(ef.Pins[1], "F1") + " (the base's grid) | the same pins and plan both ways, " +
                             "and after removing the tower at x " + F(spot) + " | " + Towers(pf) + " -> " + Towers(qf));
            }
        }

        // ── DR-4: crests that need a pair ───────────────────────────────────────────────────────────────────────────────────
        private static void Dr4Pairs()
        {
            const string W = "direction/DR-4";
            foreach (string id in new[] { "fixedGrip", "detachable", "gondola" })
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(id);
                foreach (float extra in new[] { 0f, 2.3f })
                {
                    // BO-1's lines (a ground break past one train's reach, inside a pair's), on and off the grid
                    World w = id == "fixedGrip" ? new World { Label = "FG crest 0.20 -> 0.05", C = c, L = 400f + extra, H = Crest(0.20f, 200f, 0.05f, 0f) }
                        : id == "detachable" ? new World { Label = "DQ crest 0.60 -> 0.05", C = c, L = 1000f + extra, H = Crest(0.60f, 500f, 0.05f, 0f) }
                        : new World { Label = "GD crest 0.90 -> 0.10", C = c, L = 1000f + extra, H = Crest(0.90f, 500f, 0.10f, 0f) };
                    w.Label += ", " + F(w.L, "F1") + " m";
                    LiftLineEdit ef = Edit(w, false, out LiftTerrainSampler fa, out LiftTerrainSampler fb);
                    LiftLineEdit eb = Edit(w, true, out LiftTerrainSampler ba, out LiftTerrainSampler bb);
                    LiftTowerPlan pf = Replan(ef, fa, fb), pb = Replan(eb, ba, bb);
                    string diff = Mirror(pf, pb, 0f, true);
                    Check(diff == null, W, w.Label + ": " + diff + " | forward " + Towers(pf) + " | backward " + Towers(pb, true));
                    var pair = new List<int>();
                    bool flagged = false;
                    for (int i = 0; i < pf.Nodes.Length; i++)
                    {
                        if ((pf.Nodes[i].Flags & LiftPlanNodeFlags.Pair) != 0)
                            pair.Add(i);
                        flagged |= (pf.Nodes[i].Flags & LiftPlanNodeFlags.OverCap) != 0;
                    }
                    // the planner before the rule, from the other end (x L): what a player clicking the top first got
                    LiftTowerPlan given = Direct(w, true, true);
                    string before = " | before the rule, from x L: " + Towers(given, true);
                    if (pair.Count != 2)
                    {
                        // off the grid a pair may not fit; then the crest must stay single, flagged, with a note (BO-6), the same both ways
                        Check(extra > 0f && pair.Count == 0 && flagged && pf.AngleNotes.Count > 0, W, w.Label + ": not one pair: " + Towers(pf) + before);
                        s_Report.Add("DR-4 " + w.Label + " | no pair fits: the crest single and flagged, the same both ways | works from " +
                                     (ef.FromB ? "x L" : "x 0") + "; " + Towers(pf) + " | " + string.Join(" | ", pf.AngleNotes) + before);
                        continue;
                    }
                    // each plan's PAIR note names its own pair, in its own order along the line
                    string noteF = PairNote(pf), noteB = PairNote(pb);
                    int n = pf.Nodes.Length;
                    string wantF = "PAIR " + F(pf.Nodes[pair[0]].AlongM) + " / " + F(pf.Nodes[pair[1]].AlongM) + " m";
                    string wantB = "PAIR " + F(pb.Nodes[n - 1 - pair[1]].AlongM) + " / " + F(pb.Nodes[n - 1 - pair[0]].AlongM) + " m";
                    Check(noteF != null && noteF.Contains(wantF) && noteB != null && noteB.Contains(wantB), W,
                          w.Label + ": the PAIR notes do not name the pair (" + wantF + " / " + wantB + "): " + noteF + " | " + noteB);
                    s_Report.Add("DR-4 " + w.Label + " | the same pair both ways, the note in each frame | works from " + (ef.FromB ? "x L" : "x 0") +
                                 "; " + Towers(pf) + " | forward: " + noteF + " | backward: " + noteB + before);
                }
            }
        }

        private static string PairNote(LiftTowerPlan p)
        {
            foreach (string s in p.AngleNotes)
                if (s.Contains("PAIR"))
                    return s;
            return null;
        }

        // ── DR-5: the FG station pair at x -1200 ────────────────────────────────────────────────────────────────────────────
        // Log field/2026-11-03/breakover-b1/SkiResortMod.Mod.log :554 (base -> top) and :632 (top -> base): 426 m, base station rope 1225.44
        // (stub node + -2.51), top 1281.57 (node + 3.21), carriers 8.9 m / 0.4 m below the highest ground at the stub tips (SUNK STATION).
        // The terrain is not logged: the profile runs through the tower grounds the two logged plans imply (each plan's rope points solved
        // tower by tower from its logged deflections, FG sag 0.02; the base plan's first tower from its logged 0.89 edge slope, the top
        // plan's FG2 10 m from the top from its logged y 1276.8), straight between them, no cross slope. Approximate: it does not give the
        // logged plans (6 / 7 towers); it does differ by direction on the planner before the rule.
        private static readonly float[] FieldPairProfile =
        {
            0f, 1231.94f, 6f, 1229.94f, 15f, 1233.30f, 16f, 1232.48f, 90f, 1247.36f, 101f, 1249.63f, 176f, 1264.73f, 180f, 1267.42f,
            251f, 1296.62f, 270f, 1298.33f, 300f, 1292.62f, 331f, 1280.47f, 375f, 1279.05f, 416f, 1276.80f, 426f, 1279.57f,
        };

        private static float FieldPairGround(float x)
        {
            float[] p = FieldPairProfile;
            if (x <= p[0]) return p[1];
            for (int i = 2; i < p.Length; i += 2)
                if (x <= p[i])
                    return math.lerp(p[i - 1], p[i + 1], (x - p[i - 2]) / (p[i] - p[i - 2]));
            return p[p.Length - 1];
        }

        private static void Dr5FieldPair()
        {
            const string W = "direction/DR-5";
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("fixedGrip");
            const float L = 426f, ropeBase = 1225.44f, ropeTop = 1281.57f, nodeBase = 1225.44f + 2.51f, nodeTop = 1281.57f - 3.21f;
            // the stub nodes sit in the stations' holes: the edit's end points carry the logged node heights, the sampler the profile
            Func<float, float, float> h = (x, z) => FieldPairGround(x);
            var w = new World { Label = "FG station pair x -1200, 426 m (reconstructed)", C = c, L = L, H = h };
            var p0 = new float3(0f, nodeBase, 0f);
            var pL = new float3(L, nodeTop, 0f);
            var ef = new LiftLineEdit(c, p0, pL, ropeBase, ropeTop);
            var eb = new LiftLineEdit(c, pL, p0, ropeTop, ropeBase);
            LiftTowerPlan pf = ef.Replan((s, l) => h(ef.PointAt(s, l).x, 0f), (s, l) => h(ef.PointFromB(s, l).x, 0f));
            LiftTowerPlan pb = eb.Replan((s, l) => h(eb.PointAt(s, l).x, 0f), (s, l) => h(eb.PointFromB(s, l).x, 0f));
            Check(!ef.FromB && eb.FromB, W, "not planned from the base (the lower rope)");
            Check(pf.EndCarrierA < -8f && pb.EndCarrierB < -8f, W, "the sunk base station (carrier 8.9 m under the ground) not reproduced: " +
                  F(pf.EndCarrierA, "F1") + " / " + F(pb.EndCarrierB, "F1"));
            string diff = Mirror(pf, pb, 0f, true);
            Check(diff == null, W, "base -> top vs top -> base: " + diff + " | " + Towers(pf) + " | " + Towers(pb, true));
            // before the rule: the planner alone from each end (the tool's settings)
            LiftTowerPlan gf = GivenPlan(c, L, h, false, ropeBase, ropeTop, nodeBase, nodeTop);
            LiftTowerPlan gb = GivenPlan(c, L, h, true, ropeBase, ropeTop, nodeBase, nodeTop);
            string given = Mirror(gf, gb, c.StepM - 1e-3f, false);
            Check(given != null, W, "the case does not bind: the planner before the rule plans it the same from both ends");
            // the plan from either end is the old plan from the base (the end with the lower rope)
            Check(Mirror(pf, gf, 0f, false) == null || Same(pf, gf), W, "not the base-first plan: " + Towers(pf) + " vs " + Towers(gf));
            s_Report.Add("DR-5 " + w.Label + " | the same plan from either end: the base-first one | " + Towers(pf) + " | " +
                         string.Join(" | ", pf.AngleNotes) + " | before the rule: base -> top " + Towers(gf) + ", top -> base " + Towers(gb, true) + " (" + given + ")");
        }

        private static bool Same(LiftTowerPlan a, LiftTowerPlan b)
        {
            if (a.Nodes.Length != b.Nodes.Length)
                return false;
            for (int i = 0; i < a.Nodes.Length; i++)
                if (math.abs(a.Nodes[i].AlongM - b.Nodes[i].AlongM) > 1e-3f || a.Nodes[i].Member != b.Nodes[i].Member)
                    return false;
            return true;
        }

        private static LiftTowerPlan GivenPlan(LiftTowerPlanConstants c, float len, Func<float, float, float> h, bool fromTop, float ropeBase, float ropeTop,
                                               float nodeBase, float nodeTop)
        {
            var pl = new LiftTowerPlanner { Pairs = true, CheckEdgeSlope = true, AsGiven = true, EndNodeYA = fromTop ? nodeTop : nodeBase, EndNodeYB = fromTop ? nodeBase : nodeTop };
            LiftTerrainSampler t = fromTop ? (LiftTerrainSampler)((s, l) => h(len - s, 0f)) : (s, l) => h(s, 0f);
            return fromTop ? pl.Plan(t, len, ropeTop, ropeBase, c) : pl.Plan(t, len, ropeBase, ropeTop, c);
        }

        // ── DR-6: equal ropes, Plan(Plan) from B, the fit ───────────────────────────────────────────────────────────────────
        private static void Dr6EqualRopesFixedPointFit()
        {
            const string W = "direction/DR-6";
            // Equal ropes: a symmetric valley between two ground ends at the same height; the world key (x) orders the ends.
            LiftTowerPlanConstants dq = LiftTowerPlanConstants.For("detachable");
            var w = new World { Label = "DQ 803.3 m valley, equal ropes", C = dq, L = 803.3f, Station0 = false, StationL = false,
                                H = (x, z) => 1000f - 60f * math.sin(math.PI * x / 803.3f) + 0.004f * x * (803.3f - x) / 803.3f * (x > 300f ? 1f : 0.5f) };
            w.H = SameEnds(w.H, w.L);
            LiftLineEdit ef = Edit(w, false, out LiftTerrainSampler fa, out LiftTerrainSampler fb);
            LiftLineEdit eb = Edit(w, true, out LiftTerrainSampler ba, out LiftTerrainSampler bb);
            Check(w.R0 == w.RL && !ef.FromB && eb.FromB, W, "equal ropes not ordered by the world key (x 0 first)");
            LiftTowerPlan pf = Replan(ef, fa, fb), pb = Replan(eb, ba, bb);
            string diff = Mirror(pf, pb, 0f, true);
            Check(diff == null, W, w.Label + ": " + diff + " | forward " + Towers(pf) + " | backward " + Towers(pb, true));
            s_Report.Add("DR-6 " + w.Label + " | ordered by the world key, the same plan both ways | " + Towers(pf));

            // Plan(Plan) from B: a line whose A is the top is planned from B; re-planning with its own nodes as seeds keeps it.
            foreach (string id in new[] { "fixedGrip", "detachable", "tram" })
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(id);
                float len = id == "tram" ? 1503.3f : id == "fixedGrip" ? 401.7f : 1002.6f;
                Func<float, float, float> h = Hump(-0.3f, 0.02f * len, 0.4f * len, 0.08f * len, 0.01f, len, 0f);
                LiftTerrainSampler t = (s, l) => h(s, -l);
                float ya = h(0f, 0f), yb = h(len, 0f), ra = ya + c.RopeAboveBase(0), rb = yb + c.RopeAboveBase(0);
                var pl = new LiftTowerPlanner { Pairs = true, CheckEdgeSlope = true, EndNodeYA = ya, EndNodeYB = yb };
                LiftTowerPlan p = pl.Plan(t, len, ra, rb, c);
                LiftTowerPlan again = pl.Plan(t, len, ra, rb, c, p.Nodes);
                Check(LiftTowerPlanner.PlansFromB(ra, rb, pl.EndKeyA, pl.EndKeyB) && Same(p, again), W, id + " top at A: Plan(Plan) from B is not a fixed point: " +
                      Towers(p) + " -> " + Towers(again));
                Check(pl.StationA && pl.StationB && pl.EndNodeYA == ya && pl.EndNodeYB == yb && pl.Forbidden == null, W, id + ": Plan did not restore the per-end inputs");
                // the fit, both ways: the free plan mirrored (within the snap step), the same tower count needed
                var inner = new List<float>();
                var innerBack = new List<float>();
                for (int i = 1; i < p.Nodes.Length - 1; i++)
                {
                    inner.Add(p.Nodes[i].AlongM);
                    innerBack.Insert(0, len - p.Nodes[i].AlongM);
                }
                LiftTowerFit ff = new LiftTowerPlanner().Fit(t, len, ra, rb, c, inner, true, c.MaxSpanM);
                LiftTowerFit fbk = new LiftTowerPlanner().Fit((s, l) => h(len - s, l), len, rb, ra, c, innerBack, true, c.MaxSpanM);
                string fd = Mirror(ff.Free, fbk.Free, c.StepM - 1e-3f, false);
                Check(ff.Needed == fbk.Needed && fd == null, W, id + ": the fit's free plan differs by direction: needed " + ff.Needed + " / " + fbk.Needed + ", " + fd);
                s_Report.Add("DR-6 " + id + " " + F(len, "F1") + " m, top at A | Plan(Plan) from B a fixed point; the fit's free plan the same both ways (needs " +
                             ff.Needed + ") | " + Towers(p));
            }
        }

        /// <summary>The ground with both end heights equal (a linear correction), so the two end ropes tie exactly.</summary>
        private static Func<float, float, float> SameEnds(Func<float, float, float> h, float len)
        {
            float y0 = h(0f, 0f), yl = h(len, 0f);
            return (x, z) => x <= 0f ? y0 : x >= len ? y0 : h(x, z) + (y0 - yl) * x / len;
        }
    }
}
