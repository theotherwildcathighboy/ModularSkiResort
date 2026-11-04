// Rope pitch at a tower (tower-lean round, DECISIONS 600; docs research/pitch-cap-bite-2026-11-04.md). Where-tags `pitch/...`.
// Pitch = the rope's slope through a tower: the mean of the two adjacent chords' angles (LiftAlignMath.ChordPitchDeg). The cap is per
// tower, never per line.
// PC-0 the bite measurement: over the sweep line sets (FB-3: FallbackTests.SweepLines; TS-1: TramSaddleTests.Profiles; crest-end:
//      the EI-55 crest-end tops), the share of lines with any line tower over the type's hard pitch cap, as the planner places towers
//      without the cap and with it (the angle pass repairs pitch like bend: shift, member, pair). Report only (no check binds on the
//      counts; the gate checks are PC-1..).
// PC-1 the lean rule: LiftAlignMath.LeanDeg = clamp(|pitch| - LeanFreeDeg, 0, max lean) (HeadFirst; DQ / GD 20 / 20, FG 12 / 25 since
//      600 a call d, tram 30 / 25), the leaned anchor (foot pivot, downhill), LeanRotation leans the top downhill and MastPitch reads
//      the signed lean back.
// PC-2 head relative to the mast: up to the hard cap, |pitch| - lean stays within MaxRelMeanDeg (DQ / GD head 20, FG trains 13, tram
//      30); the headed clamp is HeadRelMax (20) on mast-relative pitches (594's DQ 42 / GD 50.2 went).
// PC-3 the cap repair: every crest-end FG line over the cap as placed without it is under it with the cap, with no added tower (a
//      shift, a member or, since 600 c, a longer span: the station-exit pair the pitch took before passes the carrier clearance), and
//      the TS-1 shoulders get a pair / an added tower; no repaired line warns.
// PC-4 the warning: a line still over the cap is not Sound, its Verdict says TOO STEEP, its towers key TOWER_PITCH (unless LOW /
//      TRAIN come first) and the plan key PLAN_PITCH when nothing else fails.
// PC-5 the shifted anchor: every leaned tower's anchor is LeanedAnchor of its node, the rope hangs from it (RopeAt), the floor margins
//      are judged on it, and the fit (PlanFixed on the same towers) gives the same anchors.
// PC-6 the sweep report: per set and type, towers leaning and by how much, lines warning too steep, the largest head-relative angle,
//      the FG and tram half-train pitches relative to the mast (is the tram's letter D saddle still needed).
// PC-7 the tram saddle cap (600 a call b): the TS-1 / steep / FB-3 tram lines planned with no saddle cap and at 15 / 17.5 / 20 deg per
//      half: lines that take a breakover pair (more towers), lines still OVER CAP, the widest half-break left, the planner's cost. At
//      LiftAlignMath.TramBendHalfCapDeg every tower within the caps holds its half-break under it, and a line over it keys PLAN_SADDLE /
//      TOWER_SADDLE (never TRAIN), is not Sound and reads OVER CAP.
// PC-8 the tram pedestals (600 a call c): LiftAlignMath.TramFloatM reproduces the art's foundation table (tower_lean.py: the crest
//      dig-in and cover at lean 20 / 25, the matched-slope cover), floats only a leaned tower, never past the ceiling; every leaned
//      tram tower in the sweep floats by it (node y = ground + E + FloatM through LiftLineEdit, the rope with it), the train lifts never.
// PC-9 the carrier clearance (600 c): (a) the C# table (LiftAlignMath.WorldHalfTable) equals the art's ART_WORLD_HALF_MAX, parsed from
//      assets/blender/tests/test_grip_clearance.py (until the art branch is merged the file has no table: reported, not failed; a file
//      that judges world halves without the table fails); (b) the bite: every set planned with the rule off and on (lines that take a
//      pair / more towers, lines past the table, lines that newly warn among the crossable ones: the go / no-go is 3 %); (c) no plan
//      poses a half past the table: TrainPitchSystem's rule (LiftAlignMath.PoseTrain) replayed on every sweep tower keeps each half's
//      world pitch within its head's limit, and every tower whose rope is past it is PAST CLEARANCE (TOWER_CLEARANCE unless an earlier
//      key; the line not Sound, never PLAN_CLEAR).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
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
            Pc7SaddleCap();
            Pc8Pedestals();
            Pc9Clearance();
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

        /// <summary>Plans <paramref name="l"/> with the pitch cap on or off (off: the tram saddle cap too, the plans as before 600);
        /// <paramref name="saddleHalfCapDeg"/> overrides the tram saddle cap (NaN: the type's).</summary>
        internal static LiftTowerPlan PlanLine(Line l, bool pitchCap, float saddleHalfCapDeg = float.NaN, bool worldHalf = true)
        {
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For(l.Type);
            if (!pitchCap)
                c = c.With(pitchCapDeg: float.PositiveInfinity, pitchSoftDeg: float.PositiveInfinity, saddleHalfCapDeg: float.PositiveInfinity,
                           worldHalfOn: false);
            else if (!float.IsNaN(saddleHalfCapDeg))
                c = c.With(saddleHalfCapDeg: saddleHalfCapDeg);
            if (!worldHalf)
                c = c.With(worldHalfOn: false);
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

        /// <summary>The lift tool's edit of <paramref name="l"/> after its plan (tool lines only; null for the bare-planner lines).</summary>
        private static LiftLineEdit Edited(Line l)
        {
            if (!l.Tool)
                return null;
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For(l.Type);
            float ya = l.H(0f, 0f), yb = l.H(l.L, 0f);
            var e = new LiftLineEdit(c, new float3(0f, ya, 0f), new float3(l.L, yb, 0f), l.RopeA, l.RopeB, l.Stations, l.Stations);
            Func<float, float, float> g = l.H;
            e.Replan((s, z) => { float3 q = e.PointAt(s, z); return g(q.x, q.z); },
                     (s, z) => { float3 q = e.PointFromB(s, z); return g(q.x, q.z); });
            return e;
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
                Check(free == (id == "tram" ? 30f : id == "fixedGrip" ? 12f : 20f), W + " " + id, "lean-free pitch " + F(free));
                Check(max == (id == "tram" || id == "fixedGrip" ? 25f : 20f), W + " " + id, "max lean " + F(max));
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
            s_Report.Add("PC-1 lean = clamp(|pitch| - free, 0, max): DQ / GD free " + F(LiftAlignMath.HeadRelMaxDeg, "F0") + " max " +
                         F(LiftAlignMath.MaxLeanDeg, "F0") + ", FG free " + F(LiftAlignMath.FgLeanFreeDeg, "F0") + " max " +
                         F(LiftAlignMath.FgMaxLeanDeg, "F0") + ", tram free " + F(LiftAlignMath.TrainPitchLimitDeg, "F0") + " max " +
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
                float bound = LiftAlignMath.MaxRelMeanDeg(id);
                Check(worst <= bound + 1e-3f && bound == (id == "fixedGrip" ? 13f : id == "tram" ? 30f : 20f), W + " " + id,
                      "relative pitch " + F(worst, "F2") + " up to the cap " + F(cap, "F0") + " (bound " + F(bound) + ")");
                s_Report.Add("PC-2 " + id + ": up to the cap " + F(cap, "F0") + " deg the rope's mean pitch relative to the mast stays within " +
                             F(worst, "F1") + " deg (lean-free " + F(LiftAlignMath.LeanFreeDeg(id), "F0") + ", max lean " + F(LiftAlignMath.MaxLeanDegOf(id), "F0") + ")");
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
                        Check(on.TooSteepTowers == 0 && on.LineTowers <= off.LineTowers, W3 + " " + l.Label,
                              "not repaired without an added tower: too steep " + on.TooSteepTowers + ", towers " + off.LineTowers + " -> " + on.LineTowers);
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
                    Check(key == "PLAN_PITCH" || key == "PLAN_LOW" || key == "PLAN_STEEP" || key == "PLAN_HIGH" ||
                          key == (l.Type == "tram" ? "PLAN_SADDLE" : "PLAN_TRAIN"), W4 + " " + l.Label, "key " + key);
                    for (int i = 1; i < on.Nodes.Length - 1; i++)
                    {
                        if (!on.IsTooSteep(i))
                            continue;
                        string tk = LiftLineInput.TowerKey(on, i, false, out _, out _, out float deg, out float capDeg);
                        Check(tk == "TOWER_LOW" || tk == (l.Type == "tram" ? "TOWER_SADDLE" : "TOWER_TRAIN") || (tk == "TOWER_PITCH" && deg > capDeg && capDeg == on.Constants.PitchCapDeg),
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
                    LiftAlignMath.LeanedAnchor(n.AlongM, n.GroundM + n.FloatM, n.RopeM, n.PitchDeg, n.LeanDeg, out float sa, out float ra);
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
            s_Report.Add("PC-3 crest-end FG lines over the cap without it " + crestOver + ", repaired without an added tower " + crestFixed +
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

        // ── PC-7: the tram saddle cap ───────────────────────────────────────────────────────────────────────────────────────
        private static void Pc7SaddleCap()
        {
            const string W = "pitch/PC-7";
            float pick = LiftAlignMath.TramBendHalfCapDeg;
            var caps = new List<float> { float.PositiveInfinity, 20f, 17.5f, 15f };
            if (!caps.Contains(pick))
                caps.Add(pick);
            List<Line> tram = Lines().FindAll(l => l.Type == "tram");
            var baseTowers = new Dictionary<Line, int>();
            int keyed = 0, overLines = 0;
            foreach (string set in new[] { "TS-1", "steep", "FB-3" })
            {
                foreach (float cap in caps)
                {
                    int n = 0, pairLines = 0, added = 0, overL = 0, overT = 0, towers = 0, overSteep = 0, overCliff = 0;
                    float maxHalf = 0f, maxHalfAll = 0f;
                    double ms = 0;
                    foreach (Line l in tram)
                    {
                        if (l.Set != set)
                            continue;
                        n++;
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        LiftTowerPlan p = PlanLine(l, true, cap);
                        ms += sw.Elapsed.TotalMilliseconds;
                        if (float.IsPositiveInfinity(cap))
                            baseTowers[l] = p.LineTowers;
                        else if (p.LineTowers > baseTowers[l])
                        {
                            pairLines++;
                            added += p.LineTowers - baseTowers[l];
                        }
                        towers += p.LineTowers;
                        if (p.OverCapTowers > 0)
                        {
                            overL++;
                            overT += p.OverCapTowers;
                            if (p.TooSteepTowers > 0) overSteep++;
                            if (SteepestGround(l, 100f) > 45f) overCliff++;
                        }
                        for (int i = 1; i < p.Nodes.Length - 1; i++)
                        {
                            LiftPlanNode t = p.Nodes[i];
                            if (!t.HasAngles || t.Member < 0)
                                continue;
                            float half = 0.5f * math.abs(t.DeflectionDeg);
                            maxHalfAll = math.max(maxHalfAll, half);
                            if (!p.IsOverCap(i) && !p.IsTooSteep(i))
                                maxHalf = math.max(maxHalf, half);
                        }
                        if (float.IsPositiveInfinity(cap))
                            Check(p.OverCapTowers == 0, W + " " + l.Label, "a tower over an infinite saddle cap");
                        if (cap != pick)
                            continue;
                        // the chosen cap: every tower within the caps holds the half-break; a line over it warns as a saddle
                        for (int i = 1; i < p.Nodes.Length - 1; i++)
                        {
                            LiftPlanNode t = p.Nodes[i];
                            if (!t.HasAngles || t.Member < 0)
                                continue;
                            Check(t.CapDeg == 2f * pick, W + " " + l.Label + " t" + i, "saddle cap " + F(t.CapDeg));
                            if (!p.IsOverCap(i))
                                Check(0.5f * math.abs(t.DeflectionDeg) <= pick + LiftTowerPlan.AngleTolDeg, W + " " + l.Label + " t" + i,
                                      "half-break " + F(0.5f * math.abs(t.DeflectionDeg), "F2") + " within the cap");
                            else
                            {
                                string tk = LiftLineInput.TowerKey(p, i, false, out _, out _, out float deg, out float capDeg);
                                Check(tk == "TOWER_LOW" || (tk == "TOWER_SADDLE" && deg > capDeg && capDeg == 2f * pick), W + " " + l.Label + " t" + i,
                                      "an over-cap tram tower keys " + tk + " " + F(deg) + " / " + F(capDeg));
                            }
                        }
                        if (p.OverCapTowers > 0)
                        {
                            overLines++;
                            string key = LiftLineInput.PlanKey(p, out _);
                            if (key == "PLAN_SADDLE") keyed++;
                            Check(!p.Sound && p.OverCapNote.Contains("OVER CAP") && key != "PLAN_TRAIN" && key != "PLAN_CLEAR", W + " " + l.Label,
                                  "an over-cap tram line reads " + key + " / " + p.Verdict);
                        }
                    }
                    if (n == 0)
                        continue;
                    s_Report.Add("PC-7 " + set + " tram saddle cap " + (float.IsPositiveInfinity(cap) ? "none" : F(cap) + " per half") +
                                 (cap == pick ? " (picked)" : "") + " | " + n + " lines, " + towers + " towers | lines with a breakover pair / more towers " +
                                 pairLines + " (+" + added + " towers) | lines still over the cap " + overL + " (" + overT + " towers; of these lines too steep as well " + overSteep +
                                 ", with ground past 45 deg over 100 m " + overCliff + ") | widest half-break " +
                                 "within the caps " + F(maxHalf) + ", of all towers " + F(maxHalfAll) + " | planner " + F(ms, "F0") + " ms (" +
                                 F(ms / n, "F1") + " ms per line)");
                }
            }
            Check(overLines == 0 || keyed > 0, W, "no over-cap tram line keys PLAN_SADDLE (" + overLines + " over)");
        }

        // ── PC-8: the tram pedestals ────────────────────────────────────────────────────────────────────────────────────────
        private static void Pc8Pedestals()
        {
            const string W = "pitch/PC-8";
            // the art's foundation table (assets/blender/tower_lean.py --foundation, the tower-lean art round; docs build.md):
            // per top 30 / 45 / 60: steel buried on a level crest (= the floor) and the crest / matched-slope cover (= the ceiling)
            float[] tops = { 30f, 45f, 60f };
            float[] dig25 = { 0.920f, 1.131f, 1.342f }, dig20 = { 0.442f, 0.613f, 0.784f };
            float[] cov25 = { 8.467f, 8.255f, 8.044f }, cov20 = { 9.327f, 9.156f, 8.985f };
            float[] covM25 = { 13.150f, 12.714f, 12.278f }, covM20 = { 11.734f, 11.345f, 10.956f };
            Func<float, float> crest = x => 0f;
            for (int h = 0; h < 3; h++)
            {
                float bh = LiftAlignMath.TramBaseHalfM(tops[h]);
                string w = W + " T" + F(tops[h], "F0");
                float f25 = LiftAlignMath.TramFloatM(bh, 25f, crest, out float fl25, out float ce25);
                float f20 = LiftAlignMath.TramFloatM(bh, 20f, crest, out float fl20, out float ce20);
                Check(math.abs(fl25 - dig25[h]) < 0.01f && math.abs(fl20 - dig20[h]) < 0.01f, w, "crest floor " + F(fl25, "F3") + " / " + F(fl20, "F3"));
                Check(math.abs(ce25 - cov25[h]) < 0.01f && math.abs(ce20 - cov20[h]) < 0.01f, w, "crest ceiling " + F(ce25, "F3") + " / " + F(ce20, "F3"));
                Check(f25 == fl25 && f20 == fl20, w, "the crest float is not the floor");
                // matched ground: the terrain falls downhill at the world pitch (lean + 30): no downhill dig-in, the piers' cover
                float k25 = math.tan(math.radians(55f)), k20 = math.tan(math.radians(50f));
                float m25 = LiftAlignMath.TramFloatM(bh, 25f, x => -x * k25, out float mf25, out float mc25);
                LiftAlignMath.TramFloatM(bh, 20f, x => -x * k20, out _, out float mc20);
                Check(m25 == 0f && mf25 == 0f, w, "a matched slope floats " + F(m25, "F3"));
                Check(math.abs(mc25 - covM25[h]) < 0.01f && math.abs(mc20 - covM20[h]) < 0.01f, w, "matched ceiling " + F(mc25, "F3") + " / " + F(mc20, "F3"));
                Check(LiftAlignMath.TramFloatM(bh, 0f, crest, out _, out _) == 0f, w, "a plumb tower floats");
                // the ceiling binds: ground 15 m higher downhill (a notch): the float stops where the uphill piers still reach
                float fn = LiftAlignMath.TramFloatM(bh, 25f, x => x > 0f ? 15f : 0f, out float fln, out float cen);
                Check(fln > cen && fn == cen && math.abs(cen - cov25[h]) < 0.01f, w, "notch: float " + F(fn, "F3") + ", floor " + F(fln, "F3") + ", ceiling " + F(cen, "F3"));
            }
            s_Report.Add("PC-8 TramFloatM vs the art table: crest floor at lean 25 " + F(dig25[0], "F2") + " / " + F(dig25[1], "F2") + " / " +
                         F(dig25[2], "F2") + " m, ceiling " + F(cov25[0], "F2") + " / " + F(cov25[1], "F2") + " / " + F(cov25[2], "F2") +
                         " m (T30 / T45 / T60); matched slope: floor 0, ceiling " + F(covM25[0], "F2") + "-" + F(covM25[2], "F2") + " m");
            // the sweep
            int floated = 0, leaned = 0, emitted = 0, bound = 0, liftOff = 0;
            float maxF = 0f, sumF = 0f;
            foreach (Line l in Lines())
            {
                LiftTowerPlan p = PlanLine(l, true);
                bool isTram = l.Type == "tram";
                for (int i = 0; i < p.Nodes.Length; i++)
                {
                    LiftPlanNode n = p.Nodes[i];
                    if (!isTram || n.Member < 0 || n.LeanDeg <= 0f)
                    {
                        Check(n.FloatM == 0f, W + " " + l.Label + " node " + i, "floats " + F(n.FloatM, "F3") + (isTram ? " unleaned" : " on a train lift"));
                        continue;
                    }
                    leaned++;
                    float bh = LiftAlignMath.TramBaseHalfM(p.Constants.Ladder[n.Member].TopM), down = n.PitchDeg > 0f ? -1f : 1f, foot = n.AlongM, g0 = n.GroundM;
                    float want = LiftAlignMath.TramFloatM(bh, n.LeanDeg, x => CentreAt(p, foot + down * x) - g0, out float fl, out float ce);
                    // FloatM comes from the lean on the unfloated ropes; the node's lean is the floated one (a few hundredths of a degree)
                    if (ce < 0f) liftOff++;   // the piers lift off at float 0 already (a pre-existing condition: no float can help)
                    Check(math.abs(n.FloatM - want) < 0.05f && n.FloatM <= math.max(0f, ce) + 0.05f, W + " " + l.Label + " node " + i,
                          "float " + F(n.FloatM, "F3") + ", want " + F(want, "F3") + " (floor " + F(fl, "F3") + ", ceiling " + F(ce, "F3") + ")");
                    if (n.FloatM > 0f)
                    {
                        floated++;
                        maxF = math.max(maxF, n.FloatM);
                        sumF += n.FloatM;
                        if (fl > ce)
                            bound++;
                    }
                }
                LiftLineEdit e = isTram ? Edited(l) : null;
                if (e == null)
                    continue;
                List<LiftLineNode> nodes = e.Emit();
                for (int i = 1; i < nodes.Count - 1; i++)
                {
                    LiftPlanNode pn = e.Plan.Nodes[i];
                    Check(math.abs(nodes[i].Position.y - (pn.GroundM + nodes[i].ElevationM + pn.FloatM)) < 1e-3f &&
                          nodes[i].ElevationM == LiftTowerEdit.ElevationM(e.Plan.Constants, pn.Member, pn.Head), W + " emit " + l.Label + " node " + i,
                          "node y " + F(nodes[i].Position.y, "F3") + " is not ground + E + float");
                    if (pn.FloatM > 0f) emitted++;
                }
            }
            s_Report.Add("PC-8 sweep: leaned tram towers " + leaned + ", floated " + floated + " (max " + F(maxF, "F2") + " m, mean " +
                         F(floated > 0 ? sumF / floated : 0f, "F2") + " m; the ceiling bound " + bound + "; piers off the ground at float 0 already " +
                         liftOff + "); tool-emitted floated nodes " + emitted + "; train lifts never float");
            // the case the rule is for: a level bench on a 1.5 grade (56 deg) tram face, a T60 fixed on it: lean 25 on level ground
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For("tram");
                const float L = 800f, g = 1.5f, x0 = 385f, x1 = 415f;
                Func<float, float> h = x => 1000f + g * math.min(x, x0) + g * math.max(0f, x - x1);
                float ya = h(0f), yb = h(L);
                var pl = new LiftTowerPlanner { CheckEdgeSlope = true, EndNodeYA = ya, EndNodeYB = yb, Pairs = true };
                LiftTowerPlan p = pl.PlanFixed((s, z) => h(s), L, ya + c.RopeAboveBase(0), yb + c.RopeAboveBase(0), c,
                                               new List<float> { 400f }, new List<int> { 2 }, null, assign: false);
                LiftPlanNode t = p.Nodes[1];
                float want = LiftAlignMath.TramFloatM(LiftAlignMath.TramBaseHalfM(60f), t.LeanDeg, x => 0f, out float fl, out _);
                Check(t.LeanDeg > 20f && t.FloatM > 0.5f && math.abs(t.FloatM - want) < 0.05f && math.abs(t.RopeM - (t.GroundM + c.RopeAboveBase(2) + t.FloatM)) < 1e-3f,
                      W + " bench", "T60 on a bench: pitch " + F(t.PitchDeg) + ", lean " + F(t.LeanDeg) + ", float " + F(t.FloatM, "F3") + " (floor " + F(fl, "F3") + ")");
                s_Report.Add("PC-8 bench: T60 on a 30 m level bench of a 1.5 grade face: pitch " + F(t.PitchDeg) + ", lean " + F(t.LeanDeg) + ", float " +
                             F(t.FloatM, "F2") + " m (the downhill legs' dig-in; uphill pedestals show " +
                             F((LiftAlignMath.TramBaseHalfM(60f) + LiftAlignMath.TramPierHalfM) * math.sin(math.radians(t.LeanDeg)) + t.FloatM, "F2") + " m)");
            }
        }

        // ── PC-9: the carrier clearance ─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>The art's grip-clearance test (the other side of the table).</summary>
        internal const string ArtGripTest = "assets/blender/tests/test_grip_clearance.py";

        /// <summary>The rows of ART_WORLD_HALF_MAX = {("FG", "support"): 41.5, ...} in the art test's source, keyed "family/kind"; null
        /// when the file has no such assignment.</summary>
        internal static Dictionary<string, float> ParseArtTable(string py)
        {
            Match m = Regex.Match(py, @"^ART_WORLD_HALF_MAX\s*=\s*\{(?<body>[^}]*)\}", RegexOptions.Multiline);
            if (!m.Success)
                return null;
            var rows = new Dictionary<string, float>();
            foreach (Match r in Regex.Matches(m.Groups["body"].Value, @"\(\s*""(?<f>\w+)""\s*,\s*""(?<k>\w+)""\s*\)\s*:\s*(?<v>[0-9]+(?:\.[0-9]+)?)"))
                rows[r.Groups["f"].Value + "/" + r.Groups["k"].Value] = float.Parse(r.Groups["v"].Value, CultureInfo.InvariantCulture);
            return rows;
        }

        private static void Pc9Clearance()
        {
            const string W = "pitch/PC-9";
            // (a) the table gate. LIFTBATCH_GRIP_PY points at another copy of the art test (a check before the art branch is merged).
            var cs = new Dictionary<string, float>();
            foreach ((string Family, string Kind, float Deg) r in LiftAlignMath.WorldHalfTable())
                Check(cs.TryAdd(r.Family + "/" + r.Kind, r.Deg), W + " table", "a duplicate C# row " + r.Family + "/" + r.Kind);
            string path = Environment.GetEnvironmentVariable("LIFTBATCH_GRIP_PY");
            if (string.IsNullOrEmpty(path))
                path = CatalogTests.RepoFile(ArtGripTest);
            string py = path != null && File.Exists(path) ? File.ReadAllText(path) : null;
            Dictionary<string, float> art = py == null ? null : ParseArtTable(py);
            if (art != null)
            {
                bool same = art.Count == cs.Count;
                foreach (KeyValuePair<string, float> kv in cs)
                    same &= art.TryGetValue(kv.Key, out float v) && math.abs(v - kv.Value) < 1e-4f;
                Check(same && art.Count > 0, W + " table", "LiftAlignMath.WorldHalfTable " + Rows(cs) + " != the art's ART_WORLD_HALF_MAX " + Rows(art));
                s_Report.Add("PC-9 table | the C# table " + (same ? "equals" : "DIFFERS FROM") + " the art's ART_WORLD_HALF_MAX (" + Path.GetFileName(path) + "): " + Rows(cs));
            }
            else
            {
                // the art branch is not merged into this tree yet: nothing to compare. A file that already judges world halves without the
                // table (renamed or moved) fails, so the gate cannot go quiet after the merge.
                Check(py == null || !py.Contains("world_half"), W + " table", ArtGripTest + " judges world halves but has no ART_WORLD_HALF_MAX table");
                s_Report.Add("PC-9 table | " + ArtGripTest + (py == null ? " not found" : " has no ART_WORLD_HALF_MAX yet (the art branch is not merged)") +
                             ": C# table only " + Rows(cs));
            }

            // (b) the bite and (c) the pose over the sweep sets
            List<Line> lines = Lines();
            var rowsOut = new List<string>();
            int crossTotal = 0, newWarnTotal = 0, posed = 0, held = 0, heldOff = 0, keyedClearance = 0, clearanceLines = 0;
            float worstPosed = 0f, worstHeldRope = 0f, worstOffRope = 0f;
            foreach (string set in new[] { "FB-3", "TS-1", "crest-end", "steep" })
            foreach (string id in new[] { "fixedGrip", "detachable", "gondola" })
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(id);
                int n = 0, cross = 0, pastOff = 0, pastOn = 0, pairLines = 0, added = 0, fewerLines = 0, warnOff = 0, warnOn = 0, newWarn = 0, newWarnCross = 0;
                int towersOff = 0, towersOn = 0;
                double msOff = 0, msOn = 0;
                float maxOff = 0f, maxOn = 0f;
                foreach (Line l in lines)
                {
                    if (l.Set != set || l.Type != id)
                        continue;
                    n++;
                    bool crossable = !(SteepestGround(l, c.TypicalSpanM) > c.PitchCapDeg);
                    if (crossable) cross++;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    LiftTowerPlan off = PlanLine(l, true, worldHalf: false);
                    msOff += sw.Elapsed.TotalMilliseconds;
                    sw.Restart();
                    LiftTowerPlan on = PlanLine(l, true);
                    msOn += sw.Elapsed.TotalMilliseconds;
                    towersOff += off.LineTowers;
                    towersOn += on.LineTowers;
                    int po = PastTable(off, id, out float mo, out float poseOff), pn = PastTable(on, id, out float mn, out _);
                    maxOff = math.max(maxOff, mo);
                    maxOn = math.max(maxOn, mn);
                    if (po > 0) pastOff++;
                    if (pn > 0) pastOn++;
                    if (po > 0 && poseOff > 0f) { heldOff++; worstOffRope = math.max(worstOffRope, poseOff); }
                    if (on.LineTowers > off.LineTowers) { pairLines++; added += on.LineTowers - off.LineTowers; }
                    else if (on.LineTowers < off.LineTowers) fewerLines++;
                    if (!off.Sound) warnOff++;
                    if (!on.Sound) warnOn++;
                    if (off.Sound && !on.Sound)
                    {
                        newWarn++;
                        if (crossable) newWarnCross++;
                        if (rowsOut.Count < 200)
                            rowsOut.Add(set + " " + l.Label + (crossable ? "" : " (ground steeper than the cap)") + ": " + on.Verdict);
                    }
                    Check(pn == on.ClearanceTowers, W + " " + l.Label, "towers past the table " + pn + " but PAST CLEARANCE " + on.ClearanceTowers);
                    // (c) the pose: every tower's posed halves within the table, and the warning wired
                    for (int i = 1; i < on.Nodes.Length - 1; i++)
                    {
                        LiftPlanNode t = on.Nodes[i];
                        if (t.Member < 0 || !t.HasAngles)
                            continue;
                        float lim = LiftAlignMath.WorldHalfMaxDeg(id, LiftTowerPlanConstants.WorldHalfKind(t.Head));
                        Pose(on, i, id, out float worldPosed, out bool worldClamped);
                        posed++;
                        if (worldClamped) { held++; worstHeldRope = math.max(worstHeldRope, t.HalfDeg); }
                        if (math.isfinite(lim))
                            worstPosed = math.max(worstPosed, worldPosed - lim);
                        Check(!math.isfinite(lim) || worldPosed <= lim + 1e-3f, W + " " + l.Label + " t" + i,
                              "posed half " + F(worldPosed, "F2") + " past the table's " + F(lim));
                        if (!on.IsOverClearance(i))
                            continue;
                        string tk = LiftLineInput.TowerKey(on, i, false, out _, out _, out float deg, out float capDeg);
                        if (tk == "TOWER_CLEARANCE") keyedClearance++;
                        Check(tk == "TOWER_LOW" || tk == "TOWER_TRAIN" || tk == "TOWER_PITCH" || (tk == "TOWER_CLEARANCE" && deg > capDeg && capDeg == lim),
                              W + " " + l.Label + " t" + i, "a tower past its clearance keys " + tk + " " + F(deg) + " / " + F(capDeg));
                    }
                    if (on.ClearanceTowers > 0)
                    {
                        clearanceLines++;
                        string key = LiftLineInput.PlanKey(on, out _);
                        Check(!on.Sound && on.OverCapNote.Contains("PAST CLEARANCE") && key != "PLAN_CLEAR" && key != "PLAN_SADDLE", W + " " + l.Label,
                              "a line past its clearance reads " + key + " / " + on.Verdict);
                    }
                }
                if (n == 0)
                    continue;
                crossTotal += cross;
                newWarnTotal += newWarnCross;
                s_Report.Add("PC-9 " + set + " " + id + " | " + n + " lines (" + cross + " crossable) | lines with a rope half past the table: off " + pastOff +
                             ", on " + pastOn + " | lines with a pair / more towers " + pairLines + " (+" + added + " towers), with fewer towers " + fewerLines + " (" + towersOff +
                             " -> " + towersOn + " towers) | lines warning off " + warnOff + ", on " + warnOn + ", newly " + newWarn + " (crossable " + newWarnCross + ", " +
                             F(cross > 0 ? 100.0 * newWarnCross / cross : 0) + " %) | steepest rope half off " + F(maxOff) + ", on " + F(maxOn) +
                             " | planner off " + F(msOff, "F0") + " ms, on " + F(msOn, "F0") + " ms (" + F(msOn / n, "F1") + " ms per line)");
            }
            s_Report.Add("PC-9 bite | newly warning crossable lines " + newWarnTotal + " of " + crossTotal + " (" +
                         F(crossTotal > 0 ? 100.0 * newWarnTotal / crossTotal : 0, "F2") + " %; the go / no-go is 3 %)");
            s_Report.Add("PC-9 pose | " + posed + " towers posed by PoseTrain, " + held + " held at their limit (steepest rope half there " + F(worstHeldRope) +
                         "), worst posed half past its limit " + F(worstPosed, "F3") + " deg | with the rule off " + heldOff + " line(s) would pose a half " +
                         "past the table without the clamp (worst " + F(worstOffRope) + " deg over) | towers keyed TOWER_CLEARANCE " + keyedClearance +
                         " on " + clearanceLines + " line(s)");
            foreach (string r in rowsOut)
                s_Report.Add("PC-9 row | " + r);
        }

        private static string Rows(Dictionary<string, float> t)
        {
            var keys = new List<string>(t.Keys);
            keys.Sort(StringComparer.Ordinal);
            return "{" + string.Join(", ", keys.ConvertAll(k => k + " " + F(t[k]))) + "}";
        }

        /// <summary>Towers of <paramref name="p"/> whose steeper rope half is past the table for their head; the steepest half (deg) and the
        /// most a pose without the world clamp would put a half past its limit (deg).</summary>
        private static int PastTable(LiftTowerPlan p, string typeId, out float maxHalf, out float worstUnclamped)
        {
            int past = 0;
            maxHalf = worstUnclamped = 0f;
            for (int i = 1; i < p.Nodes.Length - 1; i++)
            {
                LiftPlanNode t = p.Nodes[i];
                if (t.Member < 0 || !t.HasAngles)
                    continue;
                maxHalf = math.max(maxHalf, t.HalfDeg);
                float lim = LiftAlignMath.WorldHalfMaxDeg(typeId, LiftTowerPlanConstants.WorldHalfKind(t.Head));
                if (!(t.HalfDeg > lim + LiftTowerPlan.AngleTolDeg))
                    continue;
                past++;
                Pose(p, i, typeId, out float worldPosed, out _, clamp: false);
                worstUnclamped = math.max(worstUnclamped, worldPosed - lim);
            }
            return past;
        }

        /// <summary>TrainPitchSystem's pose of plan tower <paramref name="i"/> (LiftAlignMath.PoseTrain): the tower's +z along the line toward
        /// B, the front half on the leaving rope, the back on the arriving one, the mast at SignedLean, DQ / GD headed (letter T), the limits
        /// as the game reads them. <paramref name="worldPosed"/> = the steeper posed half's world pitch, degrees.</summary>
        private static void Pose(LiftTowerPlan p, int i, string typeId, out float worldPosed, out bool worldClamped, bool clamp = true)
        {
            LiftPlanNode a = p.Nodes[i - 1], t = p.Nodes[i], b = p.Nodes[i + 1];
            LiftAlignMath.RopeAngles(new float3(0f, a.RopeM, a.AlongM), new float3(0f, t.RopeM, t.AlongM), new float3(0f, b.RopeM, b.AlongM),
                p.Constants.SagRatio, out float inA, out float outA);
            float mast = LiftAlignMath.SignedLean(t.PitchDeg, t.LeanDeg);
            LiftAlignMath.TrainLimits(SkiCatalog.LiftById(typeId).Envelope.SlopeMax, false, out float maxPitch, out float maxHalfBreak);
            float worldMax = clamp ? math.radians(LiftAlignMath.WorldHalfMaxDeg(typeId, LiftTowerPlanConstants.WorldHalfKind(t.Head))) : float.PositiveInfinity;
            LiftAlignMath.PoseTrain(outA - mast, 1, inA - mast, 1, mast, typeId != "fixedGrip", LiftAlignMath.HeadRelMax, maxPitch, maxHalfBreak,
                worldMax, out _, out float front, out float back, out _, out worldClamped);
            worldPosed = math.degrees(math.max(math.abs(front + mast), math.abs(back + mast)));
        }

        /// <summary>The plan's center ground at <paramref name="s"/> (linear between its samples, clamped): LiftTowerPlanner.CentreAt.</summary>
        private static float CentreAt(LiftTowerPlan p, float s)
        {
            float[] a = p.SampleAlongM, c = p.SampleCentreM;
            int last = a.Length - 1;
            if (s <= a[0]) return c[0];
            if (s >= a[last]) return c[last];
            int lo = 0, hi = last;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (a[mid] <= s) lo = mid;
                else hi = mid;
            }
            float d = a[hi] - a[lo];
            return d > 1e-6f ? math.lerp(c[lo], c[hi], (s - a[lo]) / d) : c[lo];
        }
    }
}
