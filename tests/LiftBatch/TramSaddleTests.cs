// Tram saddle articulation (DECISIONS 597; docs research/art/tram-saddle-2026-11/build.md). Where-tags `saddle/...`.
// TS-1 the planner's tram rope angles: a sweep of synthetic tram lines (uniform, run-out, crest, shoulder and cliff profiles, every
// length class) planned by the tool's planner; per line tower the two half pitches the engine's lanes give the saddle halves
// (front = the leaving rope's angle, back = the arriving rope's, LiftAlignMath.RopeAngles), the half-break (d / 2) and the mean.
// The report is the art round's input (the fold sweep range; assets/blender/tests/data/tram_saddle_poses.json holds a copy).
// TS-2 the wide tram saddle's limits (LiftAlignMath.TrainLimits): 60 / 30 for a wide saddle, the train limits (30 / 20 on the tram,
//      TrainPitchLimit on every type) otherwise; the old HalfPitches / TrainOverDeg overloads are the new ones at MaxHalfBreakDeg.
// TS-3 WideTramSaddle by mesh name: tram tower meshes of letter D or later only.
// TS-4 the clamp: a wide saddle follows a 27.5 deg half-break and a 55 deg rope in full, clamps 35 to 30 about the mean and 65 to 60.
// TS-5 coverage on the TS-1 sweep: towers whose halves follow the rope (TrainOverDeg <= TrainWarnDeg) at the C limits and at the D
//      limits, and the worst rope offset at a half's outer end (2.4 m out) where the half is clamped.

using System;
using System.Collections.Generic;
using System.Globalization;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    public static class TramSaddleTests
    {
        private static Func<bool, string, string, bool> s_Check;
        private static readonly List<string> s_Report = new List<string>();

        public static IReadOnlyList<string> Report => s_Report;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            s_Report.Clear();
            Ts1PlannerRange();
            Ts2Limits();
            Ts3Names();
            Ts4Clamp();
            Ts5Coverage();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);
        private static string F(double v, string f = "F1") => v.ToString(f, CultureInfo.InvariantCulture);

        /// <summary>Piecewise linear ground through (s, y) points; flat beyond the ends; no cross slope.</summary>
        private static LiftTerrainSampler Ground(params float[] sy)
        {
            return (s, l) =>
            {
                if (s <= sy[0]) return sy[1];
                for (int i = 2; i < sy.Length; i += 2)
                    if (s <= sy[i])
                        return math.lerp(sy[i - 1], sy[i + 1], (s - sy[i - 2]) / math.max(sy[i] - sy[i - 2], 1e-6f));
                return sy[sy.Length - 1];
            };
        }

        internal struct Pose
        {
            public string Line;
            public float Along, Front, Back, Mean, HalfBreak, DeflectionDeg;
        }

        /// <summary>The tram profiles of the sweep: (label, length, ground).</summary>
        internal static List<(string, float, LiftTerrainSampler)> Profiles()
        {
            var p = new List<(string, float, LiftTerrainSampler)>();
            foreach (float L in new[] { 600f, 1200f, 2000f, 3000f })
            {
                foreach (float g in new[] { 0.1f, 0.3f, 0.6f, 1.0f, 1.5f, 2.0f })
                {
                    p.Add(("uniform " + F(g, "F1") + " L" + F(L, "F0"), L, Ground(0f, 1000f, L, 1000f + g * L)));
                    p.Add(("run-out " + F(g, "F1") + " L" + F(L, "F0"), L, Ground(0f, 1000f, 0.3f * L, 1000f, L, 1000f + g * 0.7f * L)));
                    p.Add(("shoulder " + F(g, "F1") + " L" + F(L, "F0"), L, Ground(0f, 1000f, 0.6f * L, 1000f + g * 0.6f * L, L, 1000f + g * 0.6f * L + 0.05f * 0.4f * L)));
                }
                foreach (float g1 in new[] { 0.3f, 0.6f, 1.0f, 1.5f })
                    foreach (float g2 in new[] { -0.3f, 0f, 0.2f })
                        p.Add(("crest " + F(g1, "F1") + "/" + F(g2, "F1") + " L" + F(L, "F0"), L,
                               Ground(0f, 1000f, 0.5f * L, 1000f + g1 * 0.5f * L, L, 1000f + g1 * 0.5f * L + g2 * 0.5f * L)));
                foreach (float h in new[] { 100f, 250f, 400f })
                    p.Add(("cliff " + F(h, "F0") + " L" + F(L, "F0"), L, Ground(0f, 1000f, 0.45f * L, 1000f, 0.55f * L, 1000f + h, L, 1000f + h + 0.1f * 0.45f * L)));
            }
            return p;
        }

        internal static List<Pose> Sweep(out int lines)
        {
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("tram");
            var poses = new List<Pose>();
            lines = 0;
            foreach ((string label, float L, LiftTerrainSampler T) in Profiles())
            {
                float ya = T(0f, 0f), yb = T(L, 0f);
                var pl = new LiftTowerPlanner { CheckEdgeSlope = true, EndNodeYA = ya, EndNodeYB = yb };
                LiftTowerPlan plan = pl.Plan(T, L, ya + c.RopeAboveBase(0), yb + c.RopeAboveBase(0), c);
                if (plan?.Nodes == null)
                    continue;
                lines++;
                for (int i = 1; i < plan.Nodes.Length - 1; i++)
                {
                    LiftPlanNode pv = plan.Nodes[i - 1], n = plan.Nodes[i], nx = plan.Nodes[i + 1];
                    var a = new float3(pv.AlongM, pv.RopeM, 0f);
                    var b = new float3(n.AlongM, n.RopeM, 0f);
                    var d = new float3(nx.AlongM, nx.RopeM, 0f);
                    LiftAlignMath.RopeAngles(a, b, d, c.SagRatio, out float inA, out float outA);
                    float back = math.degrees(inA), front = math.degrees(outA);
                    poses.Add(new Pose
                    {
                        Line = label, Along = n.AlongM, Front = front, Back = back, Mean = 0.5f * (front + back),
                        HalfBreak = 0.5f * (back - front), DeflectionDeg = back - front,
                    });
                }
            }
            return poses;
        }

        private static void Ts1PlannerRange()
        {
            List<Pose> poses = Sweep(out int lines);
            Check(lines > 0 && poses.Count > 0, "saddle/TS-1", "no tram line planned");
            Pose worstPitch = default, worstBreak = default, worstMean = default;
            float mp = -1f, mb = -1f, mm = -1f;
            int over30 = 0, overBreak20 = 0;
            foreach (Pose p in poses)
            {
                float ap = math.max(math.abs(p.Front), math.abs(p.Back));
                if (ap > mp) { mp = ap; worstPitch = p; }
                if (math.abs(p.HalfBreak) > mb) { mb = math.abs(p.HalfBreak); worstBreak = p; }
                if (math.abs(p.Mean) > mm) { mm = math.abs(p.Mean); worstMean = p; }
                if (ap > 30f) over30++;
                if (math.abs(p.HalfBreak) > 20f) overBreak20++;
            }
            s_Report.Add("TS-1 tram planner sweep | " + lines + " lines, " + poses.Count + " line towers | max |half pitch| " + F(mp) + " (" + worstPitch.Line +
                         " @" + F(worstPitch.Along, "F0") + "), max half-break " + F(mb) + " (" + worstBreak.Line + " @" + F(worstBreak.Along, "F0") +
                         "), max |mean| " + F(mm) + " (" + worstMean.Line + ") | towers past 30 deg " + over30 + ", past the 20 deg half-break " + overBreak20);
            foreach (Pose p in poses)
                s_Report.Add("TS-1 row | " + p.Line + " @" + F(p.Along, "F0") + " front " + F(p.Front) + " back " + F(p.Back) + " mean " + F(p.Mean) +
                             " half-break " + F(p.HalfBreak));
        }

        private const float TramSlopeMax = 2.5f;     // SkiCatalog tram Envelope.SlopeMax (atan 68.2 deg)
        private static float R(float deg) => math.radians(deg);

        private static void Ts2Limits()
        {
            Check(LiftAlignMath.TramSaddlePitchLimitDeg == 60f && LiftAlignMath.TramHalfBreakMaxDeg == 30f, "saddle/TS-2",
                  "tram saddle limits " + LiftAlignMath.TramSaddlePitchLimitDeg + " / " + LiftAlignMath.TramHalfBreakMaxDeg + " (want 60 / 30: the art's G6 grid)");
            LiftAlignMath.TrainLimits(TramSlopeMax, true, out float wp, out float wb);
            Check(math.abs(wp - R(60f)) < 1e-6f && math.abs(wb - R(30f)) < 1e-6f, "saddle/TS-2", "wide tram limits " + F(math.degrees(wp), "F2") + " / " +
                  F(math.degrees(wb), "F2"));
            LiftAlignMath.TrainLimits(TramSlopeMax, false, out float np, out float nb);
            Check(np == LiftAlignMath.TrainPitchLimit(TramSlopeMax) && nb == R(LiftAlignMath.MaxHalfBreakDeg), "saddle/TS-2",
                  "narrow tram limits " + F(math.degrees(np), "F2") + " / " + F(math.degrees(nb), "F2") + " (want the train limits 30 / 20)");
            // a gentle type: the wide clamp never exceeds the type's steepest rope
            LiftAlignMath.TrainLimits(0.5f, true, out float gp, out _);
            Check(math.abs(gp - LiftAlignMath.MaxTrainPitch(0.5f)) < 1e-6f, "saddle/TS-2", "wide limit past the type's steepest rope on slope 0.5");
            foreach (LiftTypeDef t in SkiCatalog.Lifts)
            {
                LiftAlignMath.TrainLimits(t.Envelope.SlopeMax, false, out float p, out float b);
                Check(p == LiftAlignMath.TrainPitchLimit(t.Envelope.SlopeMax) && b == R(LiftAlignMath.MaxHalfBreakDeg), "saddle/TS-2",
                      t.Id + ": narrow limits differ from the train limits");
            }
            var rnd = new System.Random(597);
            int same = 0;
            for (int i = 0; i < 2000; i++)
            {
                float f = R((float)(rnd.NextDouble() * 140.0 - 70.0)), b = R((float)(rnd.NextDouble() * 140.0 - 70.0));
                int nf = rnd.Next(3), nb2 = rnd.Next(3);
                float mp = R(30f);
                bool c1 = LiftAlignMath.HalfPitches(f, nf, b, nb2, mp, out float a1, out float b1);
                bool c2 = LiftAlignMath.HalfPitches(f, nf, b, nb2, mp, R(LiftAlignMath.MaxHalfBreakDeg), out float a2, out float b2);
                float o1 = LiftAlignMath.TrainOverDeg(f, nf, b, nb2, mp);
                float o2 = LiftAlignMath.TrainOverDeg(f, nf, b, nb2, mp, R(LiftAlignMath.MaxHalfBreakDeg));
                if (c1 == c2 && a1 == a2 && b1 == b2 && o1 == o2)
                    same++;
            }
            Check(same == 2000, "saddle/TS-2", "old overloads differ from the new ones at MaxHalfBreakDeg on " + (2000 - same) + " / 2000");
            s_Report.Add("TS-2 limits | wide tram " + F(math.degrees(wp)) + " / " + F(math.degrees(wb)) + ", narrow tram " + F(math.degrees(np)) + " / " +
                         F(math.degrees(nb)) + "; old overloads = new at 20 deg on 2000 / 2000 random inputs");
        }

        private static void Ts3Names()
        {
            var cases = new (string, bool)[]
            {
                ("SkiTramTower30D Mesh", true), ("SkiTramTower45D_LOD1 Mesh", true), ("SkiTramTower60D_LOD2 Mesh", true), ("SkiTramTower30E Mesh", true),
                ("SkiTramTower30C Mesh", false), ("SkiTramTower60C_LOD1 Mesh", false), ("SkiTramTower45A Mesh", false), ("SkiTramTower30 Mesh", false),
                ("SkiTramTowerPlaceholder", false), ("SkiTramTower", false), ("SkiTramTower30", false), ("SkiTramTower30Dx Mesh", false),
                ("SkiLiftTowerDQ12S Mesh", false), ("SkiLiftTowerGD20D Mesh", false), ("skitramtower30D Mesh", false), (null, false), ("", false),
            };
            int bad = 0;
            foreach ((string n, bool want) in cases)
                if (!Check(LiftAlignMath.WideTramSaddle(n) == want, "saddle/TS-3", "WideTramSaddle(" + (n ?? "null") + ") != " + want))
                    bad++;
            s_Report.Add("TS-3 mesh names | " + (cases.Length - bad) + " / " + cases.Length + " as expected (letter D or later on SkiTramTower<H> only)");
        }

        private static void Ts4Clamp()
        {
            LiftAlignMath.TrainLimits(TramSlopeMax, true, out float mp, out float mb);
            // a crest the D saddle follows in full: front -50, back +5 (half-break 27.5, code sign)
            bool c = LiftAlignMath.HalfPitches(R(-50f), 1, R(5f), 1, mp, mb, out float f, out float b);
            Check(!c && math.abs(f - R(-50f)) < 1e-6f && math.abs(b - R(5f)) < 1e-6f, "saddle/TS-4", "-50 / +5 not followed: " + F(math.degrees(f)) + " / " +
                  F(math.degrees(b)));
            Check(LiftAlignMath.TrainOverDeg(R(-50f), 1, R(5f), 1, mp, mb) == 0f, "saddle/TS-4", "-50 / +5 over the wide limits");
            // the same crest on the narrow (C) saddle: clamped, over by 7.5 deg (half-break 27.5 - 20) and 20 deg (|-50| - 30): 20
            LiftAlignMath.TrainLimits(TramSlopeMax, false, out float np, out float nb);
            float overC = LiftAlignMath.TrainOverDeg(R(-50f), 1, R(5f), 1, np, nb);
            Check(math.abs(overC - 20f) < 1e-3f, "saddle/TS-4", "-50 / +5 on C over " + F(overC, "F3") + " (want 20)");
            // a 35 deg half-break about mean -10: clamped to 30 about the mean
            c = LiftAlignMath.HalfPitches(R(-45f), 1, R(25f), 1, mp, mb, out f, out b);
            Check(c && math.abs(f - R(-40f)) < 1e-5f && math.abs(b - R(20f)) < 1e-5f, "saddle/TS-4", "-45 / +25 clamp: " + F(math.degrees(f)) + " / " +
                  F(math.degrees(b)) + " (want -40 / +20)");
            // a 65 deg uniform rope: each half to 60
            c = LiftAlignMath.HalfPitches(R(65f), 1, R(65f), 1, mp, mb, out f, out b);
            Check(!c && math.abs(f - R(60f)) < 1e-5f && math.abs(b - R(60f)) < 1e-5f, "saddle/TS-4", "65 / 65 not at 60: " + F(math.degrees(f)));
            Check(math.abs(LiftAlignMath.TrainOverDeg(R(65f), 1, R(65f), 1, mp, mb) - 5f) < 1e-3f, "saddle/TS-4", "65 / 65 not 5 deg over");
        }

        private static void Ts5Coverage()
        {
            List<Pose> poses = Sweep(out _);
            const float halfLen = 2.4f;                // the saddle half's length (SADDLE_L_DEFAULT 4.8 / 2)
            int inC = 0, inD = 0, dFollowsCClamps = 0;
            float worstC = 0f, worstD = 0f;
            string worstCLine = "-", worstDLine = "-";
            LiftAlignMath.TrainLimits(TramSlopeMax, false, out float cp, out float cb);
            LiftAlignMath.TrainLimits(TramSlopeMax, true, out float dp, out float db);
            foreach (Pose p in poses)
            {
                float fr = R(p.Front), bk = R(p.Back);
                float oc = LiftAlignMath.TrainOverDeg(fr, 1, bk, 1, cp, cb), od = LiftAlignMath.TrainOverDeg(fr, 1, bk, 1, dp, db);
                if (oc <= LiftAlignMath.TrainWarnDeg)
                    inC++;
                if (od <= LiftAlignMath.TrainWarnDeg)
                    inD++;
                if (od <= LiftAlignMath.TrainWarnDeg && oc > LiftAlignMath.TrainWarnDeg)
                    dFollowsCClamps++;
                LiftAlignMath.HalfPitches(fr, 1, bk, 1, cp, cb, out float cf, out float cbk);
                LiftAlignMath.HalfPitches(fr, 1, bk, 1, dp, db, out float df, out float dbk);
                float offC = halfLen * math.sin(math.max(math.abs(fr - cf), math.abs(bk - cbk)));
                float offD = halfLen * math.sin(math.max(math.abs(fr - df), math.abs(bk - dbk)));
                if (offC > worstC) { worstC = offC; worstCLine = p.Line + " @" + F(p.Along, "F0"); }
                if (offD > worstD) { worstD = offD; worstDLine = p.Line + " @" + F(p.Along, "F0"); }
                Check(od <= oc + 1e-4f, "saddle/TS-5", p.Line + ": the wide limits ask more than C's (" + F(od, "F2") + " > " + F(oc, "F2") + ")");
            }
            Check(inD >= inC && inD >= (int)(0.8f * poses.Count), "saddle/TS-5", "the D limits follow " + inD + " / " + poses.Count + " towers (want >= 80 %)");
            s_Report.Add("TS-5 coverage | " + poses.Count + " tram line towers: halves follow the rope at C 30 / 20 on " + inC + ", at D 60 / 30 on " + inD +
                         " (+" + dFollowsCClamps + "); worst rope offset at a half's outer end C " + F(worstC, "F2") + " m (" + worstCLine + "), D " + F(worstD, "F2") +
                         " m (" + worstDLine + ")");
        }
    }
}
