// Road keep-outs (FIX-LIST 62). Where-tags `keepout/...`.
// A tower the planner stands on a road reads Overlapping items in game (2026-11-05 collision box pass). The tool hands the planner the
// stretches of the line that a road, track or path runs under (LiftLineEdit.KeepoutOf, merged). The planner keeps NEW towers out of
// them, softly like the removed-tower spots (Forbidden). Where the line has no clear plan without a tower there, the tower stands anyway.
// KO-0 none, no change: Keepouts null / empty plan bit-identically.
// KO-1 geometry: KeepoutOf against hand-worked capsules (square, 45 deg, parallel and short of the line, the end circle, clipping at
//      the ends) and MergeStretches.
// KO-2 avoid: every free tower of a plan gets a 10 m road through it. The new plan has no free tower in the stretch where it stays
//      clear, and it is never less clear than the plan without the road (soft). Both draw directions.
// KO-3 soft: a road stretch over the whole line: never less clear than without it (the fewest-towers search and the angle repair
//      yield). The test reports towers past their angle caps and does not gate them. The breakover move does not step onto a road (a
//      tower on a road is a build error, one past its cap a warning), at most one more per line.
//      (the fewest-towers search and the angle repair yield. The repair may pick another fix, e.g. dropping a tower).

using System;
using System.Collections.Generic;
using System.Globalization;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    public static class KeepoutTests
    {
        private static Func<bool, string, string, bool> s_Check;
        private static readonly List<string> s_Report = new List<string>();

        public static IReadOnlyList<string> Report => s_Report;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            s_Report.Clear();
            Ko0NoChange();
            Ko1Geometry();
            Ko2Avoid();
            Ko3Soft();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);
        private static string F(double v, string f = "F1") => v.ToString(f, CultureInfo.InvariantCulture);

        private const float G = 1000f;

        private static readonly (string name, Func<float, float> ground)[] Grounds =
        {
            ("flat", s => G),
            ("slope", s => G + 0.25f * s),
            ("valley", s => G - 45f * math.exp(-math.pow((s - 300f) / 90f, 2f))),
            ("crest", s => G + 0.3f * s - 30f * math.exp(-math.pow((s - 250f) / 60f, 2f)) + 25f * math.exp(-math.pow((s - 450f) / 50f, 2f))),
        };

        private static readonly (string type, float length)[] Lines =
        {
            ("fixedGrip", 420f), ("detachable", 650f), ("gondola", 820f), ("tram", 1400f),
        };

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

        private static bool SameNodes(LiftTowerPlan a, LiftTowerPlan b)
        {
            if (a.Nodes.Length != b.Nodes.Length)
                return false;
            for (int i = 0; i < a.Nodes.Length; i++)
            {
                LiftPlanNode x = a.Nodes[i], y = b.Nodes[i];
                if (x.AlongM != y.AlongM || x.Member != y.Member || x.Head != y.Head || x.RopeM != y.RopeM || x.Flags != y.Flags)
                    return false;
            }
            return true;
        }

        private static void Ko0NoChange()
        {
            foreach (var (type, length) in Lines)
                foreach (var (gname, ground) in Grounds)
                    foreach (bool rev in new[] { false, true })
                    {
                        string tag = $"keepout/KO-0/{type}/{gname}/{(rev ? "fromB" : "fromA")}";
                        LiftLineEdit plain = Edit(type, length, ground, rev), empty = Edit(type, length, ground, rev);
                        empty.Keepouts = new float2[0];
                        LiftTerrainSampler t = Sampler(ground, rev, length);
                        Check(SameNodes(plain.Replan(t), empty.Replan(t)), tag, "an empty Keepouts list changed the plan");
                    }
            s_Report.Add("KO-0 empty keep-outs plan bit-identically on " + (Lines.Length * Grounds.Length * 2) + " lines");
        }

        private static void Near(float got, float want, string tag, string what) =>
            Check(math.abs(got - want) < 0.01f, tag, $"{what}: got {F(got, "F3")}, want {F(want, "F3")}");

        private static void Ko1Geometry()
        {
            LiftLineEdit e = Edit("detachable", 400f, s => G);   // A (0, 0) -> B (400, 0) along x
            const string T = "keepout/KO-1";
            float2 s;
            Check(e.KeepoutOf(new float2(200f, -50f), new float2(200f, 50f), 10f, out s), T + "/square", "a square crossing missed");
            Near(s.x, 190f, T + "/square", "from"); Near(s.y, 210f, T + "/square", "to");
            float d = 50f / math.sqrt(2f);
            Check(e.KeepoutOf(new float2(200f - d, -d), new float2(200f + d, d), 10f, out s), T + "/45", "a 45 deg crossing missed");
            Near(s.x, 200f - 10f * math.sqrt(2f), T + "/45", "from"); Near(s.y, 200f + 10f * math.sqrt(2f), T + "/45", "to");
            Check(!e.KeepoutOf(new float2(0f, 30f), new float2(400f, 30f), 10f, out s), T + "/parallel", "a road 30 m off caught the line");
            Check(e.KeepoutOf(new float2(100f, 8f), new float2(300f, 8f), 10f, out s), T + "/alongside", "a road 8 m off missed");
            Near(s.x, 100f - 6f, T + "/alongside", "from"); Near(s.y, 300f + 6f, T + "/alongside", "to");
            Check(e.KeepoutOf(new float2(200f, -50f), new float2(200f, -5f), 10f, out s), T + "/short", "a road ending 5 m short missed");
            Near(s.x, 200f - math.sqrt(75f), T + "/short", "from"); Near(s.y, 200f + math.sqrt(75f), T + "/short", "to");
            Check(!e.KeepoutOf(new float2(200f, -50f), new float2(200f, -12f), 10f, out s), T + "/far-end", "a road ending 12 m short caught");
            Check(e.KeepoutOf(new float2(-5f, -50f), new float2(-5f, 50f), 10f, out s), T + "/clip", "a road past end A missed");
            Near(s.x, 0f, T + "/clip", "from"); Near(s.y, 5f, T + "/clip", "to");
            Check(!e.KeepoutOf(new float2(-20f, -50f), new float2(-20f, 50f), 10f, out s), T + "/beyond", "a road 20 m past A caught");
            var m = new List<float2> { new float2(50f, 60f), new float2(10f, 20f), new float2(55f, 70f), new float2(20f, 25f), new float2(90f, 95f) };
            LiftLineEdit.MergeStretches(m);
            Check(m.Count == 3 && m[0].Equals(new float2(10f, 25f)) && m[1].Equals(new float2(50f, 70f)) && m[2].Equals(new float2(90f, 95f)),
                  T + "/merge", "merge gave " + string.Join(" ", m));
            s_Report.Add("KO-1 capsule geometry and merge");
        }

        private static void Ko2Avoid()
        {
            int cases = 0, avoided = 0, kept = 0, more = 0, clearLost = 0;
            foreach (var (type, length) in Lines)
                foreach (var (gname, ground) in Grounds)
                    foreach (bool rev in new[] { false, true })
                    {
                        LiftTerrainSampler t = Sampler(ground, rev, length);
                        LiftTowerPlan p0 = Edit(type, length, ground, rev).Replan(t);
                        float half = 5f + LiftTowerPlanConstants.For(type).HalfWidthM + LiftLineEdit.KeepoutClearM;   // a 10 m road
                        for (int n = 1; n < p0.Nodes.Length - 1; n++)
                        {
                            float at = p0.Nodes[n].AlongM;
                            string tag = $"keepout/KO-2/{type}/{gname}/{(rev ? "fromB" : "fromA")}/@{F(at, "F0")}";
                            LiftLineEdit e = Edit(type, length, ground, rev);
                            var ko = new float2(math.max(0f, at - half), math.min(length, at + half));
                            e.Keepouts = new[] { ko };
                            LiftTowerPlan p1 = e.Replan(t);
                            cases++;
                            bool inside = false;
                            for (int q = 1; q < p1.Nodes.Length - 1; q++)
                                inside |= p1.Nodes[q].AlongM > ko.x + 1e-3f && p1.Nodes[q].AlongM < ko.y - 1e-3f;
                            if (inside) kept++; else avoided++;
                            if (p1.Nodes.Length > p0.Nodes.Length) more++;
                            if (p0.Clear && !p1.Clear)
                            {
                                clearLost++;
                                Check(false, tag, "the road cost a clear line its clearance: " + p1.Verdict);
                            }
                        }
                    }
            Check(cases > 0 && avoided >= 0.9f * cases, "keepout/KO-2", $"only {avoided} of {cases} roads avoided");
            s_Report.Add($"KO-2 {cases} roads through a planned tower: {avoided} avoided, {kept} kept a tower (no clear plan without), " +
                         $"{more} plans with more towers, {clearLost} lost clearance");
        }

        private static void Ko3Soft()
        {
            int n = 0, changed = 0, caps = 0;
            foreach (var (type, length) in Lines)
                foreach (var (gname, ground) in Grounds)
                {
                    string tag = $"keepout/KO-3/{type}/{gname}";
                    LiftTerrainSampler t = Sampler(ground, false, length);
                    LiftTowerPlan p0 = Edit(type, length, ground).Replan(t);
                    LiftLineEdit e = Edit(type, length, ground);
                    e.Keepouts = new[] { new float2(0f, length) };
                    LiftTowerPlan p1 = e.Replan(t);
                    Check(p1.Clear == p0.Clear && p1.OverCapTowers <= p0.OverCapTowers + 1, tag,
                          $"a road under the whole line cost the plan: clear {p0.Clear} -> {p1.Clear}, towers past their angle caps " +
                          $"{p0.OverCapTowers} -> {p1.OverCapTowers}");
                    if (!SameNodes(p0, p1))
                        changed++;
                    caps += math.max(0, p1.OverCapTowers - p0.OverCapTowers);
                    n++;
                }
            s_Report.Add("KO-3 a road under the whole line yields on " + n + " lines (" + changed + " with other towers, none less clear, " + caps + " more tower(s) past an angle cap)");
        }
    }
}
