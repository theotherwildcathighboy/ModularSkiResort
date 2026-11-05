// Crossings with built lifts of another type (FIX-LIST 60 a). Where-tags `cross/...`.
// Since 12d5ff6 different lift types no longer join where they cross; the game validates the two edge boxes (the hanging curve up
// by the composition m_HeightRange, over the strips' overlap) and refuses overlapping ones. The planner keeps that clearance.
// CX-0 no crossing, no change: Crossings null / empty / one refused before planning plan bit-identically to a planner never given
//      the field (DQ, GD, FG, tram lines over valleys, crests and slopes, from either end, with pins).
// CX-1 over: a DQ across valleys whose floor carries a gondola edge. Without the rule the drawn edge's box overlaps (or misses the
//      margin); with it the crossing is met (planned >= required), an independent oracle (the drawn curve evaluated over the whole
//      overlap parallelogram, span by span, against the crossed plane) finds no overlap, the curve at the crossing rises, no tower
//      stands in the crossed corridor. At 90 deg every case is met; a flatter one may be refused, then the plan is the one without it.
// CX-2 refusals: too shallow, inside the drawn line's station run-out, inside the crossed line's station run-out, beside a crossed
//      tower, unreachable (flat ground, a crossed curve 6 m up: the plan shown is the plan
//      without the crossing), a pin in the crossed corridor; a valley with the crossed curve 22 m up is met under it (refused with the 14 m box).
// CX-3 under: a DQ under a tram span 60 m up passes under it with its own plan (over is out of reach); a DQ across a GD valley
//      span 35 m up clears it on the side the planner picks.
// CX-4 direction: the same crossing drawn from either end: the same side, verdict and towers (mirrored).
// CX-5 the notice keys (LiftLineInput.CrossKey): refusal first, under before over, CROSSES only for the same type; the geometry
//      helpers (CrossReach, CrossBandM) and the shallow text against CrossMinAngleDeg.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    public static class CrossingTests
    {
        private static Func<bool, string, string, bool> s_Check;
        private static readonly List<string> s_Report = new List<string>();

        public static IReadOnlyList<string> Report => s_Report;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            s_Report.Clear();
            Cx0NoChange();
            Cx1Over();
            Cx2Refusals();
            Cx3Under();
            Cx4Direction();
            Cx5Keys();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);
        private static string F(double v, string f = "F1") => v.ToString(f, CultureInfo.InvariantCulture);

        private const float G = 1000f;   // base ground

        private static Func<float, float> Valley(float at, float depth, float width) =>
            s => G - depth * math.exp(-math.pow((s - at) / width, 2f));

        /// <summary>A tool edit of type <paramref name="type"/> between two stations on the ground (ropes = node + the station anchor, as
        /// the tool's ground ends plan: DQ 3.70, GD 4.85, FG 5.84, tram 16).</summary>
        private static LiftLineEdit Edit(string type, float length, Func<float, float> ground, bool reversed = false)
        {
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For(type);
            float anchor = type == "detachable" ? 3.70f : type == "gondola" ? 4.85f : type == "tram" ? 16f : 5.84f;
            float ya = ground(reversed ? length : 0f), yb = ground(reversed ? 0f : length);
            var a = new float3(reversed ? length : 0f, ya, 0f);
            var b = new float3(reversed ? 0f : length, yb, 0f);
            return new LiftLineEdit(c, a, b, ya + anchor, yb + anchor);
        }

        private static LiftTerrainSampler Sampler(Func<float, float> ground, bool reversed, float length) =>
            reversed ? (LiftTerrainSampler)((s, l) => ground(length - s)) : (s, l) => ground(s);

        /// <summary>A crossing of a crossed edge of <paramref name="crossedType"/> at <paramref name="along"/> (from the drawn line's A).</summary>
        private static LiftPlanCrossing X(string crossedType, float along, float crossedY, float angleDeg = 90f, float slope = 0f,
                                          float towerDist = 100f, float stationDist = float.PositiveInfinity)
        {
            LiftTowerPlanConstants cc = LiftTowerPlanConstants.For(crossedType);
            LiftTowerPlanConstants.NetHeightRange(crossedType, out float lo, out float hi);
            return new LiftPlanCrossing
            {
                AlongM = along, CrossedY = crossedY, CrossedSlope = slope, AngleDeg = angleDeg, CrossedWidthM = 2f * cc.HalfWidthM,
                CrossedMinM = lo, CrossedMaxM = hi, TowerDistM = towerDist, StationDistM = stationDist,
                CrossedClearM = cc.StationClearM, CrossedHeadHalfM = cc.HalfWidthM, CrossedType = crossedType,
            };
        }

        /// <summary>The drawn edge's curve y at <paramref name="s"/> (meters from A) as the engine builds it: node to node (towers at
        /// ground + E(member, head) + float, the ends at the edit's A / B y), lowered by 4 d h t (1 - t).</summary>
        private static float CurveY(LiftTowerPlan p, LiftLineEdit e, float s)
        {
            LiftTowerPlanConstants c = p.Constants;
            int n = p.Nodes.Length;
            int k = 0;
            while (k < n - 2 && p.Nodes[k + 1].AlongM < s)
                k++;
            float ya = NodeY(p, e, k), yb = NodeY(p, e, k + 1);
            float d = p.Nodes[k + 1].AlongM - p.Nodes[k].AlongM;
            float t = d > 0f ? math.saturate((s - p.Nodes[k].AlongM) / d) : 0f;
            return ya + (yb - ya) * t - 4f * d * c.SagRatio * t * (1f - t);
        }

        private static float NodeY(LiftTowerPlan p, LiftLineEdit e, int i)
        {
            if (i == 0) return e.A.y;
            if (i == p.Nodes.Length - 1) return e.B.y;
            LiftPlanNode nd = p.Nodes[i];
            return nd.GroundM + LiftTowerEdit.ElevationM(p.Constants, nd.Member, nd.Head) + nd.FloatM;
        }

        /// <summary>Independent oracle of the box rule: over a 21 x 21 grid of the strips' overlap parallelogram, the drawn curve (span
        /// by span) against the crossed plane (y + slope x offset along it). Returns the margin of the side: OVER = min(drawn) +
        /// drawn min - (max(crossed) + crossed max); UNDER = min(crossed) + crossed min - (max(drawn) + drawn max). >= 0: no overlap.</summary>
        private static float Oracle(LiftTowerPlan p, LiftLineEdit e, LiftPlanCrossing x, bool over)
        {
            LiftTowerPlanConstants c = p.Constants;
            LiftTowerPlanConstants.NetHeightRange(c.TypeId, out float nMin, out float nMax);
            float th = math.radians(x.AngleDeg), sin = math.sin(th), cos = math.cos(th);
            float wN = 2f * c.HalfWidthM, wC = x.CrossedWidthM;
            float minN = float.PositiveInfinity, maxN = float.NegativeInfinity, minC = float.PositiveInfinity, maxC = float.NegativeInfinity;
            for (int ia = 0; ia <= 20; ia++)
            for (int ib = 0; ib <= 20; ib++)
            {
                float alpha = (ia / 10f - 1f) * wC / (2f * sin), beta = (ib / 10f - 1f) * wN / (2f * sin);
                float yN = CurveY(p, e, x.AlongM + alpha + beta * cos);
                float yC = x.CrossedY + x.CrossedSlope * (alpha * cos + beta);
                minN = math.min(minN, yN); maxN = math.max(maxN, yN);
                minC = math.min(minC, yC); maxC = math.max(maxC, yC);
            }
            return over ? minN + nMin - (maxC + x.CrossedMaxM) : minC + x.CrossedMinM - (maxN + nMax);
        }

        private static string Nodes(LiftTowerPlan p)
        {
            var parts = new List<string>();
            for (int i = 1; i < p.Nodes.Length - 1; i++)
                parts.Add(p.Constants.NameOf(p.Nodes[i].Member, p.Nodes[i].Head).Replace("SkiLiftTower", string.Empty)
                           .Replace("SkiTramTower", "T") + " @" + F(p.Nodes[i].AlongM, "F0"));
            return string.Join(", ", parts);
        }

        private static bool SameNodes(LiftTowerPlan a, LiftTowerPlan b)
        {
            if (a.Nodes.Length != b.Nodes.Length || a.Spans.Length != b.Spans.Length)
                return false;
            for (int i = 0; i < a.Nodes.Length; i++)
            {
                LiftPlanNode x = a.Nodes[i], y = b.Nodes[i];
                if (x.AlongM != y.AlongM || x.Member != y.Member || x.Head != y.Head || x.RopeM != y.RopeM || x.Flags != y.Flags ||
                    x.AnchorAlongM != y.AnchorAlongM || x.AnchorRopeM != y.AnchorRopeM || x.FloatM != y.FloatM)
                    return false;
            }
            for (int i = 0; i < a.Spans.Length; i++)
                if (a.Spans[i].MinFloorMarginM != b.Spans[i].MinFloorMarginM || a.Spans[i].MaxRopeAboveM != b.Spans[i].MaxRopeAboveM)
                    return false;
            return true;
        }

        // ── CX-0 ────────────────────────────────────────────────────────────────────────────────────────────────────────────────

        private static void Cx0NoChange()
        {
            const string W = "cross/CX-0";
            var shapes = new List<(string type, float len, Func<float, float> g, string label)>
            {
                ("detachable", 900f, Valley(450f, 40f, 120f), "DQ valley"),
                ("detachable", 1200f, s => G + 0.35f * s, "DQ slope"),
                ("detachable", 800f, s => G + 25f * math.exp(-math.pow((s - 400f) / 80f, 2f)), "DQ crest"),
                ("gondola", 1400f, Valley(700f, 90f, 200f), "GD valley"),
                ("fixedGrip", 600f, s => G + 0.3f * s + 6f * math.sin(s / 40f), "FG rough slope"),
                ("tram", 1800f, Valley(900f, 200f, 300f), "tram valley"),
            };
            int lines = 0, same = 0;
            foreach (var sh in shapes)
            {
                foreach (bool rev in new[] { false, true })
                {
                    foreach (bool pins in new[] { false, true })
                    {
                        LiftLineEdit plain = Edit(sh.type, sh.len, sh.g, rev);
                        LiftLineEdit empty = Edit(sh.type, sh.len, sh.g, rev);
                        LiftLineEdit refused = Edit(sh.type, sh.len, sh.g, rev);
                        empty.Crossings = new LiftPlanCrossing[0];
                        // a crossing refused before planning (too shallow) is no constraint: the plan stays as it was
                        refused.Crossings = new[] { X("tram", 0.5f * sh.len, sh.g(0.5f * sh.len) + 5f, angleDeg: 10f) };
                        if (pins)
                            foreach (LiftLineEdit e in new[] { plain, empty, refused })
                                e.AddPin(0.37f * sh.len);
                        LiftTerrainSampler t = Sampler(sh.g, rev, sh.len);
                        LiftTowerPlan p0 = plain.Replan(t), p1 = empty.Replan(t), p2 = refused.Replan(t);
                        lines++;
                        bool ok = SameNodes(p0, p1) && SameNodes(p0, p2) && p0.Crossings.Length == 0 && p1.Crossings.Length == 0 &&
                                  p2.Crossings.Length == 1 && p2.Crossings[0].Refusal == LiftCrossRefusal.Shallow;
                        same += ok ? 1 : 0;
                        Check(ok, W, sh.label + (rev ? " reversed" : string.Empty) + (pins ? " pinned" : string.Empty) +
                              ": plan changed by a null / empty / refused crossing list (" + Nodes(p0) + " | " + Nodes(p1) + " | " + Nodes(p2) + ")");
                    }
                }
            }
            s_Report.Add("CX-0 no crossing, no change | " + same + " of " + lines + " lines bit-identical with Crossings null, empty and one refused before planning");
        }

        // ── CX-1 ────────────────────────────────────────────────────────────────────────────────────────────────────────────────

        private static void Cx1Over()
        {
            const string W = "cross/CX-1";
            // A DQ 600 m across a valley at 300 m; a gondola edge runs along the valley floor, its curve `up` over the floor. Each case
            // binds: the DQ plan without the rule overlaps the gondola's box. The over side has a window: the DQ curve at least the
            // crossed curve + 9.7 m (gondola max 12 - DQ min 2.8 + the 0.5 margin; 14.7 before the collision box round) (+ the footprints' slope), its rope at most 30 m over the ground (MaxRopeAboveGroundM).
            // At 90 deg every case must be met; at a flatter angle the footprints grow, so a case may instead be refused (Clearance),
            // and then the plan shown must be the plan without the crossing.
            const float len = 600f, at = 300f;
            var cases = new (float depth, float width, float up)[] { (18f, 70f, 9f), (12f, 70f, 5f), (24f, 120f, 9f), (24f, 70f, 11f) };   // crossed heights +5 m with the 2026-11-05 box
            int met = 0, refused = 0;
            foreach (var cs in cases)
            {
                Func<float, float> g = Valley(at, cs.depth, cs.width);
                foreach (float angle in new[] { 90f, 60f, 35f })
                {
                    foreach (float slope in new[] { 0f, 0.15f })
                    {
                        LiftPlanCrossing x = X("gondola", at, g(at) + cs.up, angle, slope);
                        LiftLineEdit before = Edit("detachable", len, g), after = Edit("detachable", len, g);
                        after.Crossings = new[] { x };
                        LiftTowerPlan p0 = before.Replan(Sampler(g, false, len)), p1 = after.Replan(Sampler(g, false, len));
                        string tag = W + " valley " + F(cs.depth, "F0") + " m / " + F(cs.width, "F0") + " m, crossed +" + F(cs.up, "F0") + " m, " +
                                     F(angle, "F0") + " deg, slope " + F(slope, "F2");
                        LiftPlanCrossing r = p1.Crossings.Length == 1 ? p1.Crossings[0] : default;
                        float o0 = Oracle(p0, before, x, true), o1 = Oracle(p1, after, x, true);
                        Check(o0 < LiftTowerPlanner.CrossMarginM, tag, "the rule does not bind: the plan without it already clears with the margin (" + F(o0, "F2") + " m)");
                        float h0 = NeighborTops(p0, at), h1 = NeighborTops(p1, at);
                        if (r.Refusal == LiftCrossRefusal.Clearance && angle < 90f)
                        {
                            refused++;
                            Check(SameNodes(p0, p1), tag, "refused, but the plan shown is not the plan without the crossing: " + Nodes(p1));
                            s_Report.Add("CX-1 " + tag.Substring(W.Length + 1) + " | refused (Clearance): planned " + F(r.PlannedM) + " / required " +
                                         F(r.RequiredM) + " m; shown = without the crossing");
                            continue;
                        }
                        met++;
                        Check(r.Side == LiftCrossSide.Over && r.Refusal == LiftCrossRefusal.None && r.PlannedM >= r.RequiredM - 0.011f, tag,
                              "crossing not met: side " + r.Side + ", " + r.Refusal + ", planned " + F(r.PlannedM, "F2") + " / required " + F(r.RequiredM, "F2"));
                        Check(o1 >= -0.01f, tag, "the oracle finds the boxes overlapping by " + F(-o1, "F2") + " m (" + Nodes(p1) + ")");
                        Check(p1.Clear, tag, "the crossing plan is not clear: " + p1.Verdict);
                        // the rule lifts the line at the crossing (taller towers beside it, or towers moved to higher ground)
                        float y0 = CurveY(p0, before, at), y1 = CurveY(p1, after, at);
                        Check(y1 > y0 + 0.01f, tag, "the curve at the crossing does not rise: " + F(y0 - g(at)) + " -> " + F(y1 - g(at)) + " m over the floor");
                        Check(h1 >= h0 - 0.01f, tag, "the towers beside the crossing are shorter: " + F(h0) + " -> " + F(h1) + " m");
                        // no tower inside the crossed corridor
                        float band = LiftTowerPlanner.CrossBandM(angle, 2f * after.C.HalfWidthM, x.CrossedWidthM);
                        for (int i = 1; i < p1.Nodes.Length - 1; i++)
                            Check(math.abs(p1.Nodes[i].AlongM - at) >= band - 0.01f, tag, "a tower stands " + F(math.abs(p1.Nodes[i].AlongM - at)) +
                                  " m from the crossing, inside the " + F(band) + " m corridor");
                        s_Report.Add("CX-1 " + tag.Substring(W.Length + 1) + " | without: " + Nodes(p0) + " (oracle " + F(o0, "F2") + " m) | with: " +
                                     Nodes(p1) + ", planned " + F(r.PlannedM) + " / required " + F(r.RequiredM) + " m, oracle +" + F(o1, "F2") +
                                     " m, curve " + F(y0 - g(at)) + " -> " + F(y1 - g(at)) + " m over the floor, neighbor tops " + F(h0) + " -> " + F(h1) + " m");
                    }
                }
            }
            Check(met >= cases.Length * 2, W, "only " + met + " crossings met");
            s_Report.Add("CX-1 over a valley floor | " + met + " met, " + refused + " refused at a flat angle (plan shown = without the crossing)");
        }


        /// <summary>Summed tower tops (member heights) of the two nodes either side of <paramref name="at"/> (stations count 0).</summary>
        private static float NeighborTops(LiftTowerPlan p, float at)
        {
            int k = 0;
            while (k < p.Nodes.Length - 2 && p.Nodes[k + 1].AlongM < at)
                k++;
            float h = 0f;
            for (int i = k; i <= k + 1; i++)
                if (p.Nodes[i].Member >= 0)
                    h += p.Constants.TopOf(p.Nodes[i].Member, p.Nodes[i].Head);
            return h;
        }

        // ── CX-2 ────────────────────────────────────────────────────────────────────────────────────────────────────────────────

        private static void Cx2Refusals()
        {
            const string W = "cross/CX-2";
            const float len = 600f, at = 300f;
            Func<float, float> g = Valley(at, 24f, 70f);
            float y = g(at) + 3f;
            LiftTowerPlanConstants dq = LiftTowerPlanConstants.For("detachable");
            var cases = new List<(string label, LiftPlanCrossing x, LiftCrossRefusal want, Func<float, float> ground, float pin)>
            {
                ("19 deg", X("gondola", at, y, angleDeg: 19f), LiftCrossRefusal.Shallow, g, -1f),
                ("21 deg (allowed, may be unreachable)", X("gondola", at, y, angleDeg: 21f), LiftCrossRefusal.None, g, -1f),
                ("15 m from the drawn station A", X("gondola", 15f, g(15f) + 3f), LiftCrossRefusal.Station, g, -1f),
                ("15 m from the drawn station B", X("gondola", len - 15f, g(len - 15f) + 3f), LiftCrossRefusal.Station, g, -1f),
                ("10 m from a crossed station", X("gondola", at, y, stationDist: 10f), LiftCrossRefusal.Station, g, -1f),
                ("4 m from a crossed tower", X("gondola", at, y, towerDist: 4f), LiftCrossRefusal.Tower, g, -1f),
                ("12 m from a crossed tower at 40 deg", X("gondola", at, y, angleDeg: 40f, towerDist: 12f), LiftCrossRefusal.Tower, g, -1f),
                ("flat ground, crossed chord 6 m up", X("detachable", at, G + 6f), LiftCrossRefusal.Clearance, s => G, -1f),
                ("a pin at the crossing", X("gondola", at, y), LiftCrossRefusal.OwnTower, g, at),
            };
            foreach (var cs in cases)
            {
                LiftLineEdit e = Edit("detachable", len, cs.ground);
                e.Crossings = new[] { cs.x };
                if (cs.pin >= 0f)
                    e.AddPin(cs.pin);
                LiftTowerPlan p = e.Replan(Sampler(cs.ground, false, len));
                LiftPlanCrossing r = p.Crossings[0];
                bool want = cs.want == LiftCrossRefusal.None ? r.Refusal != LiftCrossRefusal.Shallow && r.Refusal != LiftCrossRefusal.Tower &&
                                                               r.Refusal != LiftCrossRefusal.Station
                                                             : r.Refusal == cs.want;
                Check(want, W + " " + cs.label, "refusal " + r.Refusal + ", want " + cs.want + " (side " + r.Side + ", planned " +
                      F(r.PlannedM) + " / required " + F(r.RequiredM) + ")");
                string key = LiftLineInput.CrossKey(p, false, out bool refused, out float need);
                Check(refused == (r.Refusal != LiftCrossRefusal.None) && (refused ? key.StartsWith("CROSS_") : key.StartsWith("CROSSES_")),
                      W + " " + cs.label, "notice key " + key + ", refused " + refused);
                s_Report.Add("CX-2 " + cs.label + " | " + r.Refusal + ", side " + r.Side + ", planned " + F(r.PlannedM) + " / required " +
                             F(r.RequiredM) + " m, key " + key + (cs.want == LiftCrossRefusal.Clearance ? ", needs " + F(need, "F0") + " m" : string.Empty));
            }
            // unreachable: the plan is the one without the crossing (no row of last-resort towers), refused with the attempt's numbers
            // (a valley with the crossed curve 12 m up was unreachable with the 14 m box; since the 2026-11-05 collision box round no
            // crossed height in this valley is: up to ~17 m the DQ passes over, from ~22 m under. Flat ground stays unreachable.)
            {
                Func<float, float> flat = s => G;
                LiftPlanCrossing low = X("detachable", at, G + 6f);
                LiftLineEdit plain = Edit("detachable", len, flat), e = Edit("detachable", len, flat);
                e.Crossings = new[] { low };
                LiftTowerPlan p0 = plain.Replan(Sampler(flat, false, len)), p1 = e.Replan(Sampler(flat, false, len));
                LiftPlanCrossing r = p1.Crossings[0];
                Check(r.Refusal == LiftCrossRefusal.Clearance && SameNodes(p0, p1), W + " flat ground, crossed chord 6 m up (shown plan)",
                      "refusal " + r.Refusal + "; plan " + Nodes(p1) + " vs without the crossing " + Nodes(p0));
                s_Report.Add("CX-2 flat ground, crossed chord 6 m up | " + r.Refusal + ", side " + r.Side + ", planned " + F(r.PlannedM) +
                             " / required " + F(r.RequiredM) + " m; shown plan = the plan without the crossing: " + SameNodes(p0, p1));
            }
            // the valley with the crossed curve 22 m up: unreachable with the 14 m box, now met under it
            {
                LiftPlanCrossing high = X("gondola", at, g(at) + 22f);
                LiftLineEdit e = Edit("detachable", len, g);
                e.Crossings = new[] { high };
                LiftTowerPlan p1 = e.Replan(Sampler(g, false, len));
                LiftPlanCrossing r = p1.Crossings[0];
                float o1 = Oracle(p1, e, high, false);
                Check(r.Refusal == LiftCrossRefusal.None && r.Side == LiftCrossSide.Under && o1 >= -0.01f, W + " valley, crossed curve 22 m up",
                      "side " + r.Side + ", " + r.Refusal + ", oracle " + F(o1, "F2") + "; plan " + Nodes(p1));
                s_Report.Add("CX-2 valley, crossed curve 22 m up | " + r.Refusal + ", side " + r.Side + ", planned " + F(r.PlannedM) +
                             " / required " + F(r.RequiredM) + " m, oracle " + F(o1, "F2") + ", " + Nodes(p1));
            }
            // the refusals known before planning, straight from the rule (no plan)
            LiftPlanCrossing b = X("gondola", at, y);
            Check(LiftTowerPlanner.CrossRefusalBeforePlan(b, dq, len, true, true) == LiftCrossRefusal.None, W, "a clean crossing is refused before planning");
            b.AlongM = dq.StationClearM - 0.1f;
            Check(LiftTowerPlanner.CrossRefusalBeforePlan(b, dq, len, true, true) == LiftCrossRefusal.Station &&
                  LiftTowerPlanner.CrossRefusalBeforePlan(b, dq, len, false, true) == LiftCrossRefusal.None, W,
                  "the station run-out applies at a station end only");
        }

        // ── CX-3 ────────────────────────────────────────────────────────────────────────────────────────────────────────────────

        private static void Cx3Under()
        {
            const string W = "cross/CX-3";
            const float len = 700f, at = 350f;
            // (a) under a tram span whose curve is 60 m over gentle ground: over is out of reach, the DQ keeps its own plan below it
            Func<float, float> g = s => G + 0.1f * s;
            LiftPlanCrossing tram = X("tram", at, g(at) + 60f, slope: 0.3f);
            LiftLineEdit plain = Edit("detachable", len, g), under = Edit("detachable", len, g);
            under.Crossings = new[] { tram };
            LiftTowerPlan p0 = plain.Replan(Sampler(g, false, len)), p1 = under.Replan(Sampler(g, false, len));
            LiftPlanCrossing r = p1.Crossings[0];
            float o = Oracle(p1, under, tram, false);
            Check(r.Side == LiftCrossSide.Under && r.Refusal == LiftCrossRefusal.None && o >= -0.01f, W + " tram",
                  "side " + r.Side + ", " + r.Refusal + ", oracle " + F(o, "F2"));
            Check(p1.LineTowers == p0.LineTowers, W + " tram", "under a high tram the DQ needs " + p0.LineTowers + " towers, got " + p1.LineTowers);
            s_Report.Add("CX-3 DQ under a tram 60 m up | side " + r.Side + ", planned " + F(r.PlannedM) + " / required " + F(r.RequiredM) +
                         " m, oracle +" + F(o, "F2") + " m, towers " + p0.LineTowers + " -> " + p1.LineTowers);

            // (b) a DQ drawn up a valley side under a gondola valley span 35 m over the floor at the crossing
            Func<float, float> v = Valley(at, 30f, 150f);
            LiftPlanCrossing gd = X("gondola", at, v(at) + 35f, angleDeg: 70f, slope: 0.05f);
            LiftLineEdit e = Edit("detachable", len, v);
            e.Crossings = new[] { gd };
            LiftTowerPlan p2 = e.Replan(Sampler(v, false, len));
            LiftPlanCrossing r2 = p2.Crossings[0];
            float o2 = Oracle(p2, e, gd, r2.Side == LiftCrossSide.Over);
            Check(r2.Refusal == LiftCrossRefusal.None && o2 >= -0.01f, W + " GD valley span", "side " + r2.Side + ", " + r2.Refusal + ", oracle " + F(o2, "F2"));
            s_Report.Add("CX-3 DQ across a GD valley span 35 m up | side " + r2.Side + ", planned " + F(r2.PlannedM) + " / required " + F(r2.RequiredM) +
                         " m, oracle +" + F(o2, "F2") + " m, " + Nodes(p2));
        }

        // ── CX-4 ────────────────────────────────────────────────────────────────────────────────────────────────────────────────

        private static void Cx4Direction()
        {
            const string W = "cross/CX-4";
            const float len = 600f, at = 260f;
            Func<float, float> g = s => Valley(at, 18f, 70f)(s) + 0.05f * s;
            LiftLineEdit fwd = Edit("detachable", len, g), rev = Edit("detachable", len, g, reversed: true);
            fwd.Crossings = new[] { X("gondola", at, g(at) + 4f, 75f, 0.1f) };
            rev.Crossings = new[] { X("gondola", len - at, g(at) + 4f, 75f, 0.1f) };
            LiftTowerPlan a = fwd.Replan(Sampler(g, false, len)), b = rev.Replan(Sampler(g, true, len));
            LiftPlanCrossing ra = a.Crossings[0], rb = b.Crossings[0];
            bool mirrored = a.Nodes.Length == b.Nodes.Length;
            for (int i = 0; mirrored && i < a.Nodes.Length; i++)
                mirrored = math.abs(a.Nodes[i].AlongM - (len - b.Nodes[a.Nodes.Length - 1 - i].AlongM)) < 0.5f &&
                           a.Nodes[i].Member == b.Nodes[a.Nodes.Length - 1 - i].Member;
            Check(ra.Side == rb.Side && ra.Refusal == rb.Refusal && math.abs(ra.PlannedM - rb.PlannedM) < 0.05f && mirrored, W,
                  "A->B " + ra.Side + "/" + ra.Refusal + " " + F(ra.PlannedM, "F2") + " (" + Nodes(a) + ") vs B->A " + rb.Side + "/" + rb.Refusal +
                  " " + F(rb.PlannedM, "F2") + " (" + Nodes(b) + ")");
            s_Report.Add("CX-4 either end | " + ra.Side + " " + ra.Refusal + ", planned " + F(ra.PlannedM, "F2") + " / " + F(rb.PlannedM, "F2") + " m, " + Nodes(a));
        }

        // ── CX-5 ────────────────────────────────────────────────────────────────────────────────────────────────────────────────

        private static void Cx5Keys()
        {
            const string W = "cross/CX-5";
            LiftTowerPlan Plan(params LiftPlanCrossing[] xs) => new LiftTowerPlan { Crossings = xs };
            LiftPlanCrossing over = new LiftPlanCrossing { Side = LiftCrossSide.Over }, under = new LiftPlanCrossing { Side = LiftCrossSide.Under };
            LiftPlanCrossing tower = new LiftPlanCrossing { Refusal = LiftCrossRefusal.Tower }, clear = new LiftPlanCrossing
            {
                Side = LiftCrossSide.Over, Refusal = LiftCrossRefusal.Clearance, RequiredM = 15.2f,
            };
            Check(LiftLineInput.CrossKey(Plan(), false, out bool r0, out _) == null && !r0, W, "no crossing reads a key");
            Check(LiftLineInput.CrossKey(Plan(), true, out r0, out _) == "CROSSES" && !r0, W, "the same type does not read CROSSES (join)");
            Check(LiftLineInput.CrossKey(Plan(over), true, out r0, out _) == "CROSSES_OVER" && !r0, W, "over");
            Check(LiftLineInput.CrossKey(Plan(over, under), false, out r0, out _) == "CROSSES_UNDER" && !r0, W, "under before over");
            Check(LiftLineInput.CrossKey(Plan(over, tower), true, out r0, out _) == "CROSS_TOWER" && r0, W, "a refusal wins");
            Check(LiftLineInput.CrossKey(Plan(clear), false, out r0, out float need) == "CROSS_CLEARANCE" && r0 && need == 15.2f, W,
                  "unreachable: key or needed separation");
            Check(LiftLineInput.CrossKey(null, false, out r0, out _) == null, W, "a null plan");

            // geometry: perpendicular strips reach half the other's width; the corridor is both half widths + CrossBandClearM
            LiftTowerPlanner.CrossReach(90f, 6f, 8.2f, out float eN, out float eC);
            Check(math.abs(eN - 4.1f) < 1e-4f && math.abs(eC - 3f) < 1e-4f, W, "CrossReach at 90 deg: " + eN + " / " + eC);
            LiftTowerPlanner.CrossReach(30f, 6f, 6f, out eN, out eC);
            Check(math.abs(eN - (6f + 6f * math.cos(math.radians(30f))) / (2f * 0.5f)) < 1e-3f && math.abs(eN - eC) < 1e-4f, W, "CrossReach at 30 deg: " + eN);
            Check(math.abs(LiftTowerPlanner.CrossBandM(90f, 6f, 8.2f) - (3f + 4.1f + LiftTowerPlanner.CrossBandClearM)) < 1e-4f, W, "CrossBandM at 90 deg");

            // the shallow text names the angle the rule uses
            string loc = CatalogTests.RepoFile("mod/SkiResortMod/LocaleEN.cs");
            if (Check(loc != null, W, "LocaleEN.cs not found"))
            {
                string text = File.ReadAllText(loc);
                int i = text.IndexOf("\"SkiResort.LiftTool.CROSS_SHALLOW\"", StringComparison.Ordinal);
                int end = i < 0 ? -1 : text.IndexOf('\n', i);
                string line = i < 0 || end < 0 ? string.Empty : text.Substring(i, end - i);
                Check(line.Contains(F(LiftTowerPlanner.CrossMinAngleDeg, "F0") + "°"), W, "CROSS_SHALLOW does not name " +
                      F(LiftTowerPlanner.CrossMinAngleDeg, "F0") + "°: " + line);
            }
            s_Report.Add("CX-5 keys and geometry | refusal first, under before over, CROSSES only for the same type; reach at 90 deg 4.1 / 3 m");
        }
    }
}
