// Rope pitch at a tower (tower-lean round, DECISIONS 600; docs research/pitch-cap-bite-2026-11-04.md). Where-tags `pitch/...`.
// Pitch = the rope's slope through a tower: the mean of the two adjacent chords' angles (LiftAlignMath.ChordPitchDeg). The cap is per
// tower, never per line.
// PC-0 the bite measurement: over the sweep line sets (FB-3: FallbackTests.SweepLines; TS-1: TramSaddleTests.Profiles; crest-end:
//      the EI-55 crest-end tops), the share of lines with any line tower over the type's hard pitch cap, as the planner places towers
//      without the cap and with it (the angle pass repairs pitch like bend: shift, member, pair). Report only (no check binds on the
//      counts; the gate checks are PC-1..).
// PC-1 the lean rule: LiftAlignMath.LeanDeg = clamp(|pitch| - LeanFreeDeg, 0, max lean) (HeadFirst), the leaned anchor (foot pivot,
//      downhill), LeanRotation leans the top downhill and MastPitch reads the signed lean back.
// PC-2 head relative to the mast: up to the hard cap, |pitch| - lean stays within LeanFreeDeg (DQ / GD head and FG trains 20, tram 30);
//      the headed clamp is HeadRelMax (20) on mast-relative pitches (594's DQ 42 / GD 50.2 went).
// PC-3 the cap repair: every crest-end FG line over the cap as placed without it is under it with the cap (a shift or a member), and
//      the TS-1 shoulders get a pair / an added tower; no repaired line warns.
// PC-4 the warning: a line still over the cap is not Sound, its Verdict says TOO STEEP, its towers key TOWER_PITCH (unless LOW /
//      TRAIN come first) and the plan key PLAN_PITCH when nothing else fails.
// PC-5 the shifted anchor: every leaned tower's anchor is LeanedAnchor of its node, the rope hangs from it (RopeAt), the floor margins
//      are judged on it, and the fit (PlanFixed on the same towers) gives the same anchors.
// PC-6 the sweep report: per set and type, towers leaning and by how much, lines warning too steep, the largest head-relative angle,
//      the FG and tram half-train pitches relative to the mast (is the tram's letter D saddle still needed).

using System;
using System.Collections.Generic;
using System.Globalization;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    public static class PitchCapTests
    {
        private static Func<bool, string, string, bool> s_Check;
        private static readonly List<string> s_Report = new List<string>();

        public static IReadOnlyList<string> Report => s_Report;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            s_Report.Clear();
            Pc0Bite();
            Pc1LeanRule();
            Pc2HeadRelative();
            Pc3To6Sweep();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);
        private static string F(double v, string f = "F1") => v.ToString(f, CultureInfo.InvariantCulture);

        internal sealed class Line
        {
            public string Set, Label, Type;
            public Func<float, float, float> H;   // ground (x along, z lateral)
            public float L, RopeA, RopeB;
            public bool Stations;
            public bool Tool;                     // planned through LiftLineEdit.Replan (the lift tool), else the bare planner (TS-1)
        }

        /// <summary>The three sets: FB-3 (with twins), TS-1 (tram profiles, the bare planner as TS-1 plans them), crest-end tops.</summary>
        internal static List<Line> Lines()
        {
            var lines = new List<Line>();
            foreach (FallbackTests.SweepLine l in FallbackTests.SweepLines())
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(l.Type);
                float ya = l.H(0f, 0f), yb = l.H(l.L, 0f);
                lines.Add(new Line
                {
                    Set = "FB-3", Label = l.Label, Type = l.Type, H = l.H, L = l.L, Stations = l.Stations, Tool = true,
                    RopeA = ya + c.RopeAboveBase(0) + l.AboveA, RopeB = yb + c.RopeAboveBase(0) + l.AboveB,
                });
            }
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For("tram");
                foreach ((string label, float L, LiftTerrainSampler T) in TramSaddleTests.Profiles())
                {
                    LiftTerrainSampler t = T;
                    float ya = t(0f, 0f), yb = t(L, 0f);
                    lines.Add(new Line
                    {
                        Set = "TS-1", Label = label, Type = "tram", H = (x, z) => t(x, z), L = L, Stations = true, Tool = false,
                        RopeA = ya + c.RopeAboveBase(0), RopeB = yb + c.RopeAboveBase(0),
                    });
                }
            }
            foreach (string id in new[] { "fixedGrip", "detachable", "gondola" })
            foreach (float len in new[] { 400f, 600f, 1000f })
            foreach (float g in new[] { 0.20f, 0.30f })
            foreach (float back in new[] { 4f, 8f, 12f, 20f })
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(id);
                int k = SkiCatalog.LiftById(id).Index;
                StationDef bas = SkiCatalog.StationOf(k, StationRole.Base), top = SkiCatalog.StationOf(k, StationRole.Top);
                float crest = len - back, gg = g;
                Func<float, float> h = x => x <= crest ? 1200f + gg * x : 1200f + gg * crest - 2f * (x - crest) / back;
                float ya = h(0f), yb = h(len);
                float ra = LiftLineInput.GroundEndRope(c, bas, ya, ya - g * LiftTowerPlanConstants.FrontDistM(bas), out _);
                float rb = LiftLineInput.GroundEndRope(c, top, yb, h(len + LiftTowerPlanConstants.FrontDistM(top)), out _);
                float hw = c.HalfWidthM;
                lines.Add(new Line
                {
                    Set = "crest-end", Label = "crest-end " + id + " " + F(len, "F0") + " g " + F(g, "F2") + " back " + F(back, "F0"),
                    Type = id, H = (x, z) => h(x) + 0.3f * z / hw, L = len, Stations = true, Tool = true, RopeA = ra, RopeB = rb,
                });
            }
            // steep: the band the old sets never reach. Uniform grades up to the line prefab's own edge slope rule (FG 0.8, DQ 0.9, GD 1.2,
            // tram 2.5: past it the engine refuses the edge anyway), and a gentle 0.3 slope with one steep band (a cliff step of 60 or
            // 150 m at grade 0.9 / 1.2 / 1.6 for chairs, 1.6 / 2.4 for the tram) where tower placement on the benches can help.
            foreach (string id in new[] { "fixedGrip", "detachable", "gondola", "tram" })
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(id);
                bool tram = id == "tram";
                float[] lens = tram ? new[] { 800f, 1500f, 2500f } : new[] { 300f, 600f, 1000f };
                float[] grades = tram ? new[] { 0.8f, 1.0f, 1.2f, 1.4f, 1.6f, 2.0f, 2.4f } : new[] { 0.5f, 0.6f, 0.7f, 0.75f, 0.8f, 0.85f, 0.9f, 1.0f, 1.1f, 1.2f };
                foreach (float len in lens)
                {
                    foreach (float g in grades)
                    {
                        if (g > c.MaxEdgeSlope + 1e-3f)
                            continue;
                        float gg = g;
                        lines.Add(new Line
                        {
                            Set = "steep", Label = "steep uniform " + id + " " + F(len, "F0") + " g " + F(g, "F2"), Type = id,
                            H = (x, z) => 1000f + gg * x + 0.04f * z, L = len, Stations = true, Tool = true,
                            RopeA = 1000f + c.RopeAboveBase(0), RopeB = 1000f + gg * len + c.RopeAboveBase(0),
                        });
                    }
                    foreach (float band in new[] { 60f, 150f })
                    foreach (float gs in tram ? new[] { 1.6f, 2.4f } : new[] { 0.9f, 1.2f, 1.6f })
                    {
                        float x0 = 0.5f * len - 0.5f * band, x1 = x0 + band, bb = band, gss = gs;
                        Func<float, float> h = x => 1000f + 0.3f * x + gss * math.clamp(x - x0, 0f, bb) - 0.3f * math.clamp(x - x0, 0f, bb);
                        float yb = h(len);
                        lines.Add(new Line
                        {
                            Set = "steep", Label = "steep band " + id + " " + F(len, "F0") + " band " + F(band, "F0") + " m g " + F(gs, "F1"), Type = id,
                            H = (x, z) => h(x) + 0.04f * z, L = len, Stations = true, Tool = true,
                            RopeA = 1000f + c.RopeAboveBase(0), RopeB = yb + c.RopeAboveBase(0),
                        });
                    }
                }
            }
            return lines;
        }

        /// <summary>Plans <paramref name="l"/> with the pitch cap on or off.</summary>
        internal static LiftTowerPlan PlanLine(Line l, bool pitchCap)
        {
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For(l.Type);
            if (!pitchCap)
                c = c.With(pitchCapDeg: float.PositiveInfinity, pitchSoftDeg: float.PositiveInfinity);
            float ya = l.H(0f, 0f), yb = l.H(l.L, 0f);
            if (!l.Tool)
            {
                var pl = new LiftTowerPlanner { CheckEdgeSlope = true, EndNodeYA = ya, EndNodeYB = yb, Pairs = pitchCap };
                Func<float, float, float> h = l.H;
                return pl.Plan((s, z) => h(s, z), l.L, l.RopeA, l.RopeB, c);
            }
            var e = new LiftLineEdit(c, new float3(0f, ya, 0f), new float3(l.L, yb, 0f), l.RopeA, l.RopeB, l.Stations, l.Stations);
            Func<float, float, float> g = l.H;
            return e.Replan((s, z) => { float3 q = e.PointAt(s, z); return g(q.x, q.z); },
                            (s, z) => { float3 q = e.PointFromB(s, z); return g(q.x, q.z); });
        }

        /// <summary>The steepest |pitch| over the plan's line towers (inner nodes), degrees, and how many are over <paramref name="cap"/>.</summary>
        internal static float MaxPitch(LiftTowerPlan p, float cap, out int over)
        {
            over = 0;
            float worst = 0f;
            for (int i = 1; i < p.Nodes.Length - 1; i++)
            {
                LiftPlanNode a = p.Nodes[i - 1], n = p.Nodes[i], b = p.Nodes[i + 1];
                float pitch = math.abs(LiftAlignMath.ChordPitchDeg(a.AlongM, a.RopeM, n.AlongM, n.RopeM, b.AlongM, b.RopeM));
                worst = math.max(worst, pitch);
                if (pitch > cap + LiftTowerPlan.AngleTolDeg)
                    over++;
            }
            return worst;
        }

        /// <summary>The steepest ground grade, degrees, over any window of the type's typical span along the line (what no tower placement
        /// can flatten much below).</summary>
        private static float SteepestGround(Line l, float window)
        {
            float worst = 0f, step = 5f;
            window = math.min(window, l.L);
            for (float x = 0f; x + window <= l.L + 1e-3f; x += step)
                worst = math.max(worst, math.degrees(math.atan(math.abs(l.H(x + window, 0f) - l.H(x, 0f)) / window)));
            return worst;
        }

        private static void Pc0Bite()
        {
            var types = new[] { "fixedGrip", "detachable", "gondola", "tram" };
            var sets = new[] { "FB-3", "TS-1", "crest-end", "steep" };
            List<Line> lines = Lines();
            var worstRows = new List<string>();
            foreach (string set in sets)
            foreach (string id in types)
            {
                float cap = LiftAlignMath.PitchCapDeg(id);
                int n = 0, overBefore = 0, overAfter = 0, steepGround = 0, towersBefore = 0, towersAfter = 0, overTowersBefore = 0, overTowersAfter = 0;
                int afterNotSteep = 0;
                float maxBefore = 0f, maxAfter = 0f;
                string worstLabel = string.Empty;
                foreach (Line l in lines)
                {
                    if (l.Set != set || l.Type != id)
                        continue;
                    n++;
                    LiftTowerPlan pb = PlanLine(l, false), pa = PlanLine(l, true);
                    float mb = MaxPitch(pb, cap, out int ob), ma = MaxPitch(pa, cap, out int oa);
                    bool steep = SteepestGround(l, LiftTowerPlanConstants.For(id).TypicalSpanM) > cap;
                    if (steep) steepGround++;
                    if (ob > 0) overBefore++;
                    if (oa > 0) overAfter++;
                    if (oa > 0 && !steep) afterNotSteep++;
                    overTowersBefore += ob;
                    overTowersAfter += oa;
                    towersBefore += pb.LineTowers;
                    towersAfter += pa.LineTowers;
                    if (mb > maxBefore) maxBefore = mb;
                    if (ma > maxAfter) { maxAfter = ma; worstLabel = l.Label; }
                    if (oa > 0 && worstRows.Count < 400)
                        worstRows.Add(set + " " + l.Label + ": max pitch " + F(mb) + " -> " + F(ma) + " (" + oa + " tower(s) over " + F(cap, "F0") +
                                      (steep ? ", ground steeper than the cap over a typical span" : "") + "), towers " + pb.LineTowers + " -> " + pa.LineTowers);
                }
                if (n == 0)
                    continue;
                s_Report.Add("PC-0 " + set + " " + id + " cap " + F(cap, "F0") + " | " + n + " lines | lines over: today " + overBefore + " (" +
                             F(100.0 * overBefore / n) + " %), with the cap " + overAfter + " (" + F(100.0 * overAfter / n) + " %), of them on ground " +
                             "steeper than the cap " + (overAfter - afterNotSteep) + " | towers over " + overTowersBefore + " -> " + overTowersAfter +
                             " | line towers " + towersBefore + " -> " + towersAfter + " | max pitch " + F(maxBefore) + " -> " + F(maxAfter) +
                             (worstLabel.Length > 0 ? " (" + worstLabel + ")" : "") + " | lines with ground steeper than the cap " + steepGround);
            }
            foreach (string r in worstRows)
                s_Report.Add("PC-0 row | " + r);
        }

        // ── PC-1: the lean rule ──────────────────────────────────────────────────────────────────────────────────────────────
        private static void Pc1LeanRule()
        {
            const string W = "pitch/PC-1";
            Check(LiftAlignMath.Rule == LiftAlignMath.LeanRule.HeadFirst, W, "the lean rule is not HeadFirst");
            foreach (string id in new[] { "fixedGrip", "detachable", "gondola", "tram" })
            {
                float free = LiftAlignMath.LeanFreeDeg(id), max = LiftAlignMath.MaxLeanDegOf(id);
                Check(free == (id == "tram" ? LiftAlignMath.TrainPitchLimitDeg : LiftAlignMath.HeadRelMaxDeg), W + " " + id, "lean-free pitch " + F(free));
                Check(max == (id == "tram" ? 25f : 20f), W + " " + id, "max lean " + F(max));
                for (float p = -70f; p <= 70f; p += 0.5f)
                {
                    float want = math.clamp(math.abs(p) - free, 0f, max), got = LiftAlignMath.LeanDeg(id, p);
                    if (math.abs(want - got) > 1e-4f)
                    {
                        Check(false, W + " " + id + " pitch " + F(p), "lean " + F(got, "F3") + ", want " + F(want, "F3"));
                        break;
                    }
                }
                Check(LiftAlignMath.LeanDeg(id, float.NaN) == 0f && LiftAlignMath.LeanDeg(id, float.PositiveInfinity) == 0f, W + " " + id, "no pitch leans");
            }
            // the anchor: foot pivot, downhill (against the pitch's sign), lower by h (1 - cos)
            float h = 12f, a = math.radians(10f);
            LiftAlignMath.LeanedAnchor(100f, 1000f, 1000f + h, 30f, 10f, out float up, out float yUp);
            LiftAlignMath.LeanedAnchor(100f, 1000f, 1000f + h, -30f, 10f, out float dn, out float yDn);
            Check(math.abs(up - (100f - h * math.sin(a))) < 1e-3f && math.abs(dn - (100f + h * math.sin(a))) < 1e-3f, W, "anchor along " + F(up, "F3") + " / " + F(dn, "F3"));
            Check(math.abs(yUp - (1000f + h * math.cos(a))) < 1e-3f && math.abs(yDn - yUp) < 1e-4f, W, "anchor y " + F(yUp, "F3"));
            LiftAlignMath.LeanedAnchor(100f, 1000f, 1000f + h, 30f, 0f, out float plumb, out float yPlumb);
            Check(plumb == 100f && math.abs(yPlumb - 1000f - h) < 1e-4f, W, "lean 0 moved the anchor");
            // the rotation: a rope rising toward +z leans the top toward -z; MastPitch reads the signed lean back
            quaternion yaw = quaternion.RotateY(math.radians(37f));
            quaternion r = LiftAlignMath.LeanRotation(yaw, 30f, 10f);
            float3 top = math.mul(math.inverse(yaw), math.mul(r, new float3(0f, 1f, 0f)));
            Check(top.z < -0.17f && top.z > -0.18f && math.abs(top.x) < 1e-5f, W, "top of a +z-rising lean at " + top);
            Check(math.abs(math.degrees(LiftAlignMath.MastPitch(r)) - 10f) < 1e-3f && math.abs(LiftAlignMath.SignedLean(30f, 10f) - a) < 1e-6f, W,
                  "mast pitch " + F(math.degrees(LiftAlignMath.MastPitch(r)), "F3"));
            Check(math.abs(math.degrees(LiftAlignMath.MastPitch(LiftAlignMath.LeanRotation(yaw, -30f, 10f))) + 10f) < 1e-3f &&
                  math.abs(LiftAlignMath.MastPitch(yaw)) < 1e-6f, W, "mast pitch sign / plumb");
            s_Report.Add("PC-1 lean = clamp(|pitch| - free, 0, max): FG / DQ / GD free " + F(LiftAlignMath.HeadRelMaxDeg, "F0") + " max " +
                         F(LiftAlignMath.MaxLeanDeg, "F0") + ", tram free " + F(LiftAlignMath.TrainPitchLimitDeg, "F0") + " max " +
                         F(LiftAlignMath.TramMaxLeanDeg, "F0") + "; rule " + LiftAlignMath.Rule + "; a 12 m anchor at 10 deg moves " +
                         F(h * math.sin(a), "F2") + " m downhill, " + F(h * (1f - math.cos(a)), "F2") + " m down");
        }

        // ── PC-2: the head relative to the mast ─────────────────────────────────────────────────────────────────────────────
        private static void Pc2HeadRelative()
        {
            const string W = "pitch/PC-2";
            float d = math.radians(1f);
            foreach (string id in new[] { "fixedGrip", "detachable", "gondola", "tram" })
            {
                float cap = LiftAlignMath.PitchCapDeg(id), worst = 0f;
                for (float p = 0f; p <= cap + 1e-3f; p += 0.25f)
                {
                    // the game's path: the tower leans by LeanRotation, TrainPitchSystem takes MastPitch off the absolute rope pitch
                    float lean = LiftAlignMath.LeanDeg(id, p);
                    float mast = LiftAlignMath.MastPitch(LiftAlignMath.LeanRotation(quaternion.identity, p, lean));
                    worst = math.max(worst, math.degrees(p * d - mast));
                }
                Check(worst <= LiftAlignMath.LeanFreeDeg(id) + 1e-3f, W + " " + id, "relative pitch " + F(worst, "F2") + " up to the cap " + F(cap, "F0"));
                s_Report.Add("PC-2 " + id + ": up to the cap " + F(cap, "F0") + " deg the rope's mean pitch relative to the mast stays within " +
                             F(worst, "F1") + " deg (lean-free " + F(LiftAlignMath.LeanFreeDeg(id), "F0") + ")");
            }
            float hm = LiftAlignMath.HeadRelMax;
            Check(math.abs(hm - 20f * d) < 1e-6f, W, "HeadRelMax is not 20 deg");
            // a 40 deg GD rope: lean 20, the head turns 20 relative to the mast, not clamped
            float leanGd = LiftAlignMath.LeanDeg("gondola", 40f);
            float rel = 40f * d - LiftAlignMath.MastPitch(LiftAlignMath.LeanRotation(quaternion.identity, 40f, leanGd));
            bool clamped = LiftAlignMath.HeadedHalfPitches(rel, 2, rel, 2, hm, out float head, out float pf, out float pb);
            Check(!clamped && math.abs(head - 20f * d) < 1e-4f && math.abs(pf - head) < 1e-6f && math.abs(pb - head) < 1e-6f, W,
                  "GD 40: head " + F(math.degrees(head), "F2"));
            // a 30 deg rope on a plumb mast: the head stops at 20, the halves reach the rope with the half-break
            Check(math.abs(LiftAlignMath.HeadPitch(30f * d, 1, 30f * d, 1, hm) - hm) < 1e-6f, W, "head past 20 relative");
            Check(LiftAlignMath.HeadedTrainOverDeg(30f * d, 1, 30f * d, 1, hm) < 1e-3f, W, "a straight 30 within head + half-break");
            Check(math.abs(LiftAlignMath.HeadedTrainOverDeg(50f * d, 1, 50f * d, 1, hm) - 10f) < 1e-3f, W,
                  "a straight 50 on a plumb mast is not 10 over (head 20 + half 20)");
        }

        // ── PC-3 .. PC-6: the sweep ──────────────────────────────────────────────────────────────────────────────────────────
        private sealed class Agg
        {
            public int Lines, Towers, Leaning, TooSteepLines, TooSteepTowers, HalfOver, HalfOverPlumb;
            public float MaxLean, MaxRel, MaxHalfRel, MaxHalfBreak;
            public string MaxHalfAt = string.Empty;
        }

        private static void Pc3To6Sweep()
        {
            const string W3 = "pitch/PC-3", W4 = "pitch/PC-4", W5 = "pitch/PC-5";
            List<Line> lines = Lines();
            int crestOver = 0, crestFixed = 0, tsPairs = 0, keyPitch = 0, steepLines = 0, leanedChecked = 0, fitChecked = 0;
            var aggs = new Dictionary<string, Agg>();
            var order = new List<string>();
            foreach (Line l in lines)
            {
                LiftTowerPlan on = PlanLine(l, true);
                // PC-3: the repair
                if (l.Set == "crest-end" && l.Type == "fixedGrip")
                {
                    LiftTowerPlan off = PlanLine(l, false);
                    MaxPitch(off, LiftAlignMath.PitchCapDeg(l.Type), out int ob);
                    if (ob > 0)
                    {
                        crestOver++;
                        if (on.TooSteepTowers == 0) crestFixed++;
                        Check(on.TooSteepTowers == 0 && on.LineTowers == off.LineTowers, W3 + " " + l.Label,
                              "not repaired by a shift / member: too steep " + on.TooSteepTowers + ", towers " + off.LineTowers + " -> " + on.LineTowers);
                    }
                }
                if (l.Set == "TS-1")
                {
                    LiftTowerPlan off = PlanLine(l, false);
                    MaxPitch(off, LiftAlignMath.PitchCapDeg(l.Type), out int ob);
                    if (ob > 0 && on.TooSteepTowers == 0 && on.LineTowers > off.LineTowers)
                        tsPairs++;
                }
                // PC-4: the warning
                if (on.TooSteepTowers > 0)
                {
                    steepLines++;
                    Check(!on.Sound && on.OverCapNote.Contains("TOO STEEP") && (!on.Clear || on.Verdict.Contains("TOO STEEP")), W4 + " " + l.Label,
                          "a too steep line reads " + on.Verdict);
                    string key = LiftLineInput.PlanKey(on, out _);
                    if (key == "PLAN_PITCH") keyPitch++;
                    Check(key == "PLAN_PITCH" || key == "PLAN_LOW" || key == "PLAN_STEEP" || key == "PLAN_HIGH" || key == "PLAN_TRAIN", W4 + " " + l.Label, "key " + key);
                    for (int i = 1; i < on.Nodes.Length - 1; i++)
                    {
                        if (!on.IsTooSteep(i))
                            continue;
                        string tk = LiftLineInput.TowerKey(on, i, false, out _, out _, out float deg, out float capDeg);
                        Check(tk == "TOWER_LOW" || tk == "TOWER_TRAIN" || (tk == "TOWER_PITCH" && deg > capDeg && capDeg == on.Constants.PitchCapDeg),
                              W4 + " " + l.Label + " tower " + i, "key " + tk + " " + F(deg) + " / " + F(capDeg));
                    }
                }
                else
                    Check(LiftLineInput.PlanKey(on, out _) != "PLAN_PITCH", W4 + " " + l.Label, "PLAN_PITCH without a too steep tower");
                // PC-5: the anchors
                var inner = new List<float>();
                var members = new List<int>();
                for (int i = 0; i < on.Nodes.Length; i++)
                {
                    LiftPlanNode n = on.Nodes[i];
                    if (i > 0 && i < on.Nodes.Length - 1) { inner.Add(n.AlongM); members.Add(n.Member); }
                    if (n.LeanDeg <= 0f)
                    {
                        Check(n.AnchorAlongM == n.AlongM && n.AnchorRopeM == n.RopeM, W5 + " " + l.Label + " node " + i, "a plumb node's anchor moved");
                        continue;
                    }
                    leanedChecked++;
                    LiftAlignMath.LeanedAnchor(n.AlongM, n.GroundM, n.RopeM, n.PitchDeg, n.LeanDeg, out float sa, out float ra);
                    Check(math.abs(sa - n.AnchorAlongM) < 1e-3f && math.abs(ra - n.AnchorRopeM) < 1e-3f, W5 + " " + l.Label + " node " + i,
                          "anchor " + F(n.AnchorAlongM, "F2") + " / " + F(n.AnchorRopeM, "F2") + ", want " + F(sa, "F2") + " / " + F(ra, "F2"));
                    Check(math.abs(n.LeanDeg - LiftAlignMath.LeanDeg(l.Type, n.PitchDeg)) < 1e-4f, W5 + " " + l.Label + " node " + i, "lean off the rule");
                    if (i < on.Spans.Length)
                        Check(math.abs(on.RopeAt(i, n.AnchorAlongM) - n.AnchorRopeM) < 1e-3f, W5 + " " + l.Label + " node " + i, "the rope does not leave the anchor");
                }
                if (on.Clear)
                {
                    bool under = false;
                    for (int s = 0; s < on.Spans.Length && !under; s++)
                        for (int q = 0; q < on.SampleAlongM.Length && !under; q++)
                        {
                            float x = on.SampleAlongM[q];
                            if (x <= on.Nodes[s].AnchorAlongM + 1e-3f || x >= on.Nodes[s + 1].AnchorAlongM - 1e-3f)
                                continue;
                            under = on.MarginAt(s, q) < on.Spans[s].MinFloorMarginM - 1e-3f;
                        }
                    Check(!under, W5 + " " + l.Label, "a leaned rope sample under its span's reported minimum");
                }
                // the fit: the same towers through PlanFixed give the same anchors (the bare-planner lines: a tool line's frame is LiftLineEdit's)
                if (!l.Tool && on.Nodes.Length > 2)
                {
                    LiftTowerPlanConstants c = LiftTowerPlanConstants.For(l.Type);
                    var pl = new LiftTowerPlanner { CheckEdgeSlope = true, EndNodeYA = l.H(0f, 0f), EndNodeYB = l.H(l.L, 0f), Pairs = true };
                    Func<float, float, float> hh = l.H;
                    LiftTowerPlan fit = pl.PlanFixed((s, z) => hh(s, z), l.L, l.RopeA, l.RopeB, c, inner, members, null, assign: false);
                    bool same = fit.Nodes.Length == on.Nodes.Length;
                    for (int i = 0; same && i < on.Nodes.Length; i++)
                        same = math.abs(fit.Nodes[i].AnchorAlongM - on.Nodes[i].AnchorAlongM) < 1e-3f && math.abs(fit.Nodes[i].AnchorRopeM - on.Nodes[i].AnchorRopeM) < 1e-3f;
                    Check(same, W5 + " fit " + l.Label, "the fit's anchors differ from the plan's");
                    fitChecked++;
                }
                // PC-6: the report rows
                string k = l.Set + " " + l.Type;
                if (!aggs.TryGetValue(k, out Agg ag)) { aggs[k] = ag = new Agg(); order.Add(k); }
                ag.Lines++;
                if (on.TooSteepTowers > 0) { ag.TooSteepLines++; ag.TooSteepTowers += on.TooSteepTowers; }
                for (int i = 1; i < on.Nodes.Length - 1; i++)
                {
                    LiftPlanNode n = on.Nodes[i];
                    if (!n.HasPitch || n.Member < 0)
                        continue;
                    ag.Towers++;
                    if (n.LeanDeg > 0f) { ag.Leaning++; ag.MaxLean = math.max(ag.MaxLean, n.LeanDeg); }
                    if (on.IsTooSteep(i) || on.IsOverCap(i))
                        continue;   // the warned towers: the train is not expected to follow there
                    float relMean = math.abs(n.PitchDeg) - n.LeanDeg;
                    float halfBreak = n.HasAngles ? 0.5f * math.abs(n.DeflectionDeg) : 0f;
                    ag.MaxRel = math.max(ag.MaxRel, relMean);
                    ag.MaxHalfBreak = math.max(ag.MaxHalfBreak, halfBreak);
                    float halfRel = relMean + halfBreak;
                    if (halfRel > ag.MaxHalfRel) { ag.MaxHalfRel = halfRel; ag.MaxHalfAt = l.Label + " t" + i; }
                    if (halfRel > LiftAlignMath.TrainPitchLimitDeg + 1e-3f) ag.HalfOver++;
                    if (math.abs(n.PitchDeg) + halfBreak > LiftAlignMath.TrainPitchLimitDeg + 1e-3f) ag.HalfOverPlumb++;
                }
            }
            Check(crestOver > 0 && crestFixed == crestOver, W3, "crest-end FG over the cap " + crestOver + ", repaired " + crestFixed);
            Check(tsPairs > 0, W3, "no TS-1 line repaired by an added tower");
            Check(steepLines > 0 && keyPitch > 0, W4, "no line warns too steep (" + steepLines + ") or keys PLAN_PITCH (" + keyPitch + ")");
            Check(leanedChecked > 0 && fitChecked > 0, W5, "nothing leaned (" + leanedChecked + ") or fitted (" + fitChecked + ")");
            s_Report.Add("PC-3 crest-end FG lines over the cap without it " + crestOver + ", repaired by a shift / member " + crestFixed +
                         "; TS-1 lines repaired by an added tower " + tsPairs);
            s_Report.Add("PC-4 " + steepLines + " line(s) still too steep after the repair, " + keyPitch + " keyed PLAN_PITCH (the rest: an earlier key)");
            s_Report.Add("PC-5 leaned anchors checked " + leanedChecked + ", fits checked " + fitChecked);
            foreach (string k in order)
            {
                Agg a = aggs[k];
                s_Report.Add("PC-6 " + k + " | " + a.Lines + " lines, " + a.Towers + " towers | leaning " + a.Leaning + " (" +
                             F(a.Towers > 0 ? 100.0 * a.Leaning / a.Towers : 0) + " %), max lean " + F(a.MaxLean) + " | too steep: " + a.TooSteepLines +
                             " line(s), " + a.TooSteepTowers + " tower(s) | relative to the mast (towers within the pitch and bend caps): max mean pitch " + F(a.MaxRel) +
                             ", max half-break " + F(a.MaxHalfBreak) + ", max half " + F(a.MaxHalfRel) + (a.MaxHalfAt.Length > 0 ? " (" + a.MaxHalfAt + ")" : "") +
                             ", towers with a half past " + F(LiftAlignMath.TrainPitchLimitDeg, "F0") + ": " + a.HalfOver + " (plumb masts: " + a.HalfOverPlumb + ")");
            }
        }
    }
}
