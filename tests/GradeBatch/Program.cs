// Headless tests for the pure half of the cat track grading tool (CatTrackGrade.cs), cases GR-1..GR-19:
// path (Bezier chain through every click), profiles (flat, follow ground, infeasible climbs with ShortfallM, hump and
// hollow, diagonal traverse, constant grade), cross-section slopes (cut 1:1.5, fill 1:2, flat top TopWidthM, bank
// blending), guards (lot, lift line, water), undo (bit-exact pre-image, foreign edits survive, LIFO stack capped at
// MaxEntries; entries over the texel cap refused), raster texel cap, nearest-leg template at switchbacks, map encoding,
// edit model and input rules, cost (1.5 km path under 3 s), the default width and its earthwork (GR-18), the run outline
// ribbon (GR-19: loops cut at tight bends, simple ring, legs too close refused), the default profile unchanged by hash (GR-23), the
// narrow full-cut profile (GR-24: no fill, 1:1 bank, tread width, grade limit, cost per m), the groomer's centerline of a generated ribbon (GR-30).
// Run from the repo root: dotnet run --project tests/GradeBatch/GradeBatch.csproj -c Release
// Prints `FAIL  where: msg` lines and one SUMMARY line; Environment.ExitCode = failures (the LiftBatch convention).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace GradeBatch
{
    internal static class Program
    {
        private static int s_Failures;
        private static long s_Checks;

        // The vanilla default map: 4096 texels over 14,336 m (3.5 m), 4,096 m of height range.
        private static readonly GradeMap Map = new GradeMap
        {
            Offset = new float2(-7168f, -7168f),
            Area = new float2(14336f, 14336f),
            Res = new int2(4096, 4096),
            HeightScale = 4096f,
            HeightOffset = 0f,
        };

        private static int Main()
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            PathShape();
            FlatGround();
            GentleSlope();
            SteepStraight();
            HumpHollow();
            LongTraverse();
            ConstantGrade();
            SideSlopes();
            Sidehill();
            Guards();
            UndoRoundTrip();
            UndoStack();
            RasterCap();
            SwitchbackLegs();
            Encoding();
            EditAndInput();
            Cost();
            WidthCost();
            Ribbon();
            CrossingRatings();
            FadeAndAlongside();
            InsideRuns();
            GoldenDefaultProfile();
            NarrowProfile();
            GroomerCenterlines();
            PlayerText();
            Console.WriteLine($"SUMMARY GradeBatch: {s_Checks} checks, {s_Failures} failure(s)");
            Environment.ExitCode = s_Failures;
            return s_Failures;
        }

        private static bool Check(bool ok, string where, string msg)
        {
            s_Checks++;
            if (!ok)
            {
                s_Failures++;
                Console.WriteLine($"FAIL  {where}: {msg}");
            }
            return ok;
        }

        private sealed class Solved
        {
            public GradePath Path;
            public GradeProfile Prof;
            public GradeRaster Raster;
            public float Reach;
        }

        private static Solved Solve(IList<float2> pts, Func<float2, float> ground, GradeParams p, bool raster = true, IList<float2[]> keep = null, bool pin = true)
        {
            var s = new Solved { Path = GradePath.Build(pts, p.SampleStepM) };
            var g = new float[s.Path.Count];
            for (int i = 0; i < g.Length; i++)
                g[i] = ground(s.Path.Xz[i]);
            s.Prof = GradeProfile.Solve(s.Path, g, p, pin ? GradeRaster.PinTolerance(s.Path, keep, p) : null);   // the tool's pipeline: held in the fade, within InsideMaxM inside
            if (raster && GradeRaster.Bounds(Map, s.Path, s.Prof, p, out int4 rect, out float reach))
            {
                s.Reach = reach;
                s.Raster = GradeRaster.Build(Map, rect, s.Path, s.Prof, p, reach, (i, j) => ground(Map.TexelCenter(i, j)), keep);
            }
            return s;
        }

        private static float2[] Line(float2 a, float2 b) => new[] { a, b };


        private static void PathShape()
        {
            var pts = new[] { new float2(0, 0), new float2(100, 30), new float2(180, -20), new float2(260, 40) };
            GradePath path = GradePath.Build(pts, 2f);
            Check(path != null, "GR-1", "path built");
            float chords = 0f;
            for (int i = 1; i < pts.Length; i++)
                chords += math.distance(pts[i - 1], pts[i]);
            Check(path.LengthM >= chords - 0.01f, "GR-1 length", $"{path.LengthM:F2} >= chords {chords:F2}");
            Check(math.distance(path.Xz[0], pts[0]) < 1e-3f && math.distance(path.Xz[path.Count - 1], pts[3]) < 1e-3f, "GR-1 ends", "exact");
            for (int k = 1; k < pts.Length - 1; k++)
            {
                path.Nearest(pts[k], out float _, out int _);
                float d = path.Nearest(pts[k], out float _, out int _);
                Check(d < 0.6f, "GR-1 through", $"clicked point {k} is {d:F2} m off the chain");
            }
            float step = path.StepM;
            Check(step <= 2f + 1e-4f && step > 1.5f, "GR-1 step", $"{step:F3}");
            bool equal = true;
            for (int i = 1; i < path.Count; i++)
                equal &= math.abs(path.S[i] - path.S[i - 1] - step) < 1e-3f;
            Check(equal, "GR-1 equal steps", "every sample step = StepM");
            Check(GradePath.Build(new[] { new float2(5, 5), new float2(5, 5) }, 2f) == null, "GR-1 degenerate", "two equal points = no path");
        }


        private static void FlatGround()
        {
            var s = Solve(Line(new float2(0, 0), new float2(300, 50)), _ => 812.4f, new GradeParams());
            Check(s.Prof.Feasible && s.Prof.MaxGradeAbs < 1e-4f, "GR-2 grade", $"{s.Prof.MaxGradeAbs}");
            Check(s.Raster.Changed == 0, "GR-2 raster", $"{s.Raster.Changed} texels changed on flat ground");
        }

        private static void GentleSlope()
        {
            Func<float2, float> ground = xz => 900f + 0.15f * xz.x;
            var s = Solve(Line(new float2(0, 0), new float2(400, 0)), ground, new GradeParams { TopWidthM = 8f });   // the 50 m3 oracle is for 8 m
            Check(s.Prof.Feasible, "GR-3 feasible", "15 % under 25 %");
            float dev = 0f;
            for (int i = 0; i < s.Path.Count; i++)
                dev = math.max(dev, math.abs(s.Prof.Y[i] - s.Prof.Ground[i]));
            Check(dev < 0.01f, "GR-3 follows", $"max deviation {dev:F3} m");
            Check(s.Prof.MaxGradeAbs <= 0.25f + 1e-3f, "GR-3 limit", $"{s.Prof.MaxGradeAbs:P1}");
            Check(s.Raster.CutM3 + s.Raster.FillM3 < 50f, "GR-3 earthwork", $"cut {s.Raster.CutM3:F0} fill {s.Raster.FillM3:F0} m3");
        }

        private static void SteepStraight()
        {
            Func<float2, float> ground = xz => 900f + 0.40f * xz.x;
            var p = new GradeParams();
            var s = Solve(Line(new float2(0, 0), new float2(200, 0)), ground, p);
            float dh = 80f, need = dh / 0.25f - s.Path.LengthM;
            Check(!s.Prof.Feasible, "GR-4 infeasible", "40 % > 25 %");
            Check(math.abs(s.Prof.ShortfallM - need) < 0.5f, "GR-4 shortfall", $"{s.Prof.ShortfallM:F1} vs {need:F1}");
            Check(math.abs(s.Prof.Y[0] - 900f) < 1e-3f && math.abs(s.Prof.Y[s.Path.Count - 1] - 980f) < 1e-3f, "GR-4 ends", "pinned");
            int red = 0;
            for (int i = 0; i < s.Prof.SegGrade.Length; i++)
                red += GradeProfile.GradeClass(math.abs(s.Prof.SegGrade[i]), p.MaxGrade) == 2 ? 1 : 0;
            Check(red > 0, "GR-4 red", "the steep part shows red");
            string text = GradeInput.Describe(s.Path, s.Prof, s.Raster, p);
            Check(text.Contains("needs") && text.Contains("switchback"), "GR-4 text (the log line keeps the detail)", text);
            GradeMsg msg = GradeInput.PlanMsg(s.Path, s.Prof, s.Raster, p);
            Check(msg.Key == "PLAN_STEEP" && msg.Args.Any(a => a.Name == "SHORT" && a.Value > 0f), "GR-4 player text", msg.Signature);
        }

        private static void HumpHollow()
        {
            // A 6 m hump at x = 150 and a 6 m hollow at x = 350 (Gaussian, 25 m sigma: up to ~14 % slopes, crest curvature over G).
            Func<float2, float> ground = xz =>
                1000f + 6f * math.exp(-math.pow((xz.x - 150f) / 12f, 2f)) - 6f * math.exp(-math.pow((xz.x - 350f) / 12f, 2f));
            var p = new GradeParams { MaxGrade = 0.10f };
            var s = Solve(Line(new float2(0, 0), new float2(500, 0)), ground, p);
            Check(s.Prof.Feasible && s.Prof.MaxGradeAbs <= 0.10f + 1e-3f, "GR-5 limit", $"{s.Prof.MaxGradeAbs:P1}");
            Check(math.abs(s.Prof.Y[0] - s.Prof.Ground[0]) < 1e-3f && math.abs(s.Prof.Y[s.Path.Count - 1] - s.Prof.Ground[s.Path.Count - 1]) < 1e-3f,
                "GR-5 ends", "meet the ground");
            Check(s.Prof.MaxCutM > 1f && s.Prof.MaxFillM > 1f, "GR-5 cut + fill", $"cut {s.Prof.MaxCutM:F2} fill {s.Prof.MaxFillM:F2}");
            Check(s.Prof.MaxCutM < 5.9f && s.Prof.MaxFillM < 5.9f, "GR-5 min-max", "the fit splits the hump, never flattens it all");
            Check(s.Raster.CutM3 > 0f && s.Raster.FillM3 > 0f, "GR-5 raster", s.Raster.Describe());
            // The written flat top along the centerline follows the profile within a height step + the 3.5 m texel sampling.
            int bad = 0;
            for (int i = 0; i < s.Path.Count; i += 5)
            {
                float2 tx = Map.ToTexel(s.Path.Xz[i]);
                int ti = (int)math.round(tx.x), tj = (int)math.round(tx.y);
                float2 c = Map.TexelCenter(ti, tj);
                if (math.distance(c, s.Path.Xz[i]) > 2f)
                    continue;
                float y = s.Raster.After[s.Raster.Index(ti, tj)];
                s.Path.Nearest(c, out float along, out int _);
                float expect = Lerp(s.Path, s.Prof.Y, along);
                if (math.abs(y - expect) > 0.1f)
                    bad++;
            }
            Check(bad == 0, "GR-5 top", $"{bad} centerline texel(s) off the profile");
        }

        private static float Lerp(GradePath path, float[] y, float along)
        {
            float f = along / path.StepM;
            int i = math.clamp((int)math.floor(f), 0, path.Count - 2);
            return math.lerp(y[i], y[i + 1], math.saturate(f - i));
        }

        private static void LongTraverse()
        {
            Func<float2, float> mountain = xz => 1200f + 0.30f * xz.x;   // 30 % fall line along +x
            var p = new GradeParams();
            // Diagonal at 45 degrees: path grade 0.30 x cos 45 = 21.2 %; gains ~255 m over ~1200 m.
            var pts = new[] { new float2(0, 0), new float2(300, 300), new float2(600, 600), new float2(850, 850) };
            var s = Solve(pts, mountain, p, raster: false);
            Check(s.Prof.Feasible && s.Prof.ShortfallM == 0f, "GR-6 traverse", $"max {s.Prof.MaxGradeAbs:P1}, shortfall {s.Prof.ShortfallM:F0}");
            Check(s.Prof.MaxGradeAbs <= 0.25f + 1e-3f, "GR-6 limit", $"{s.Prof.MaxGradeAbs:P1}");
            float gain = s.Prof.Y[s.Path.Count - 1] - s.Prof.Y[0];
            Check(math.abs(gain - 255f) < 1f, "GR-6 gain", $"{gain:F1} m climbed, no switchback");
            // The same gain straight up the fall line (850 m at 30 %).
            var up = Solve(Line(new float2(0, 0), new float2(850, 0)), mountain, p, raster: false);
            Check(!up.Prof.Feasible && up.Prof.ShortfallM > 100f, "GR-6 fall line", $"shortfall {up.Prof.ShortfallM:F0} m");
        }

        private static void ConstantGrade()
        {
            Func<float2, float> bumpy = xz => 500f + 0.12f * xz.x + 2f * math.sin(xz.x / 20f);
            var p = new GradeParams { Mode = GradeProfileMode.ConstantGrade };
            var s = Solve(Line(new float2(0, 0), new float2(300, 0)), bumpy, p, raster: false);
            float g0 = s.Prof.Ground[0], gn = s.Prof.Ground[s.Path.Count - 1];
            float maxErr = 0f;
            for (int i = 0; i < s.Path.Count; i++)
                maxErr = math.max(maxErr, math.abs(s.Prof.Y[i] - (g0 + (gn - g0) * s.Path.S[i] / s.Path.LengthM)));
            Check(maxErr < 1e-3f, "GR-7 ramp", $"{maxErr}");
            Check(s.Prof.Feasible, "GR-7 feasible", "about 12 %");
            var steep = Solve(Line(new float2(0, 0), new float2(100, 0)), xz => 0.3f * xz.x, p, raster: false);
            Check(!steep.Prof.Feasible && steep.Prof.ShortfallM > 0f, "GR-7 infeasible", $"{steep.Prof.ShortfallM:F1}");
        }


        private static void SideSlopes()
        {
            var p = new GradeParams { Mode = GradeProfileMode.ConstantGrade, TopWidthM = 8f };   // the 8 m template oracle below
            // A 5 m high, 120 m wide mound across a level path at z = 0; the path runs along x from -200 to 200 at height 0.
            Func<float2, float> mound = xz => math.abs(xz.x) < 60f ? 5f : 0f;
            var s = Solve(Line(new float2(-200, 0.25f), new float2(200, 0.25f)), mound, p);
            // Texels at x ~ 0 (well inside the mound), across z.
            int ti = (int)math.round(Map.ToTexel(new float2(0, 0)).x);
            int bad = 0, flat = 0;
            for (int tj = s.Raster.Z0; tj < s.Raster.Z0 + s.Raster.H; tj++)
            {
                float2 c = Map.TexelCenter(ti, tj);
                float d = math.abs(c.y - 0.25f);
                float expect = d <= 4f ? 0f : math.min(5f, (d - 4f) / 1.5f);
                float got = s.Raster.After[s.Raster.Index(ti, tj)];
                if (math.abs(got - expect) > 0.07f)
                    bad++;
                if (d <= 4f)
                    flat++;
            }
            Check(bad == 0, "GR-8 cut 1:1.5", $"{bad} texel(s) off the cut template");
            Check(flat >= 2, "GR-8 flat top", $"{flat} texel(s) on the 8 m top (3.5 m texels)");

            Func<float2, float> ditch = xz => math.abs(xz.x) < 60f ? -5f : 0f;
            var f = Solve(Line(new float2(-200, 0.25f), new float2(200, 0.25f)), ditch, p);
            bad = 0;
            int untouched = 0;
            for (int tj = f.Raster.Z0; tj < f.Raster.Z0 + f.Raster.H; tj++)
            {
                float2 c = Map.TexelCenter(ti, tj);
                float d = math.abs(c.y - 0.25f);
                float expect = d <= 4f ? 0f : math.max(-5f, -(d - 4f) / 2f);
                float got = f.Raster.After[f.Raster.Index(ti, tj)];
                if (math.abs(got - expect) > 0.07f)
                    bad++;
                if (d > 4f + 2f * 5f + 1f && got == -5f)
                    untouched++;
            }
            Check(bad == 0, "GR-8 fill 1:2", $"{bad} texel(s) off the fill template");
            Check(untouched > 0, "GR-8 outside", "beyond the slope the ground is untouched");
            Check(f.Raster.FillM3 > 0f && f.Raster.CutM3 == 0f, "GR-8 volumes", f.Raster.Describe());
        }

        private static void Sidehill()
        {
            // 60 % cross slope (ground rises with z), path along x: the fill side (z < 0) drops faster than 1:2 forever.
            Func<float2, float> hill = xz => 1000f + 0.6f * xz.y;
            var p = new GradeParams();
            var s = Solve(Line(new float2(0, 0), new float2(200, 0)), hill, p);
            Check(s.Raster.SteepBankTexels > 0, "GR-9 counted", s.Raster.Describe());
            // No step at the edge of the reach: neighbors across the reach boundary differ by no more than the hill itself + 1 m.
            float worst = 0f;
            var r = s.Raster;
            for (int j = r.Z0 + 1; j < r.Z0 + r.H; j++)
            {
                for (int i = r.X0; i < r.X0 + r.W; i++)
                {
                    float a = r.After[r.Index(i, j)], b = r.After[r.Index(i, j - 1)];
                    worst = math.max(worst, math.abs(a - b) - 0.6f * Map.Cell.y);
                }
            }
            Check(worst < 3.6f, "GR-9 no cliff", $"worst extra jump between neighbors {worst:F2} m");
            // The texel at the reach edge is the ground.
            int ti = (int)math.round(Map.ToTexel(new float2(100, 0)).x);
            int tj = (int)math.floor(Map.ToTexel(new float2(100, -s.Reach - 2f)).y);
            if (tj >= r.Z0)
                Check(math.abs(r.After[r.Index(ti, tj)] - r.Before[r.Index(ti, tj)]) < 1e-4f, "GR-9 edge", "ground at the reach");
        }


        private static void Guards()
        {
            var p = new GradeParams();
            Func<float2, float> slope = xz => 700f + 0.1f * xz.x + 3f * math.sin(xz.x / 15f);
            var s = Solve(Line(new float2(0, 0), new float2(300, 0)), slope, p);
            var obs = new GradeObstacles();
            Check(GradeGuards.Check(Map, s.Raster, obs, out float2 _) == GradeGuardKind.None, "GR-10 clear", "no obstacle");

            obs.Lots.Add(new GradeObstacles.Lot { Center = new float2(150, 6), Right = new float2(1, 0), Forward = new float2(0, 1), Half = new float2(8, 6) });
            GradeGuardKind k = GradeGuards.Check(Map, s.Raster, obs, out float2 at);
            Check(k == GradeGuardKind.Lot, "GR-10 lot", $"{k} at {at}");
            obs.Clear();
            obs.Lots.Add(new GradeObstacles.Lot { Center = new float2(150, 90), Right = new float2(1, 0), Forward = new float2(0, 1), Half = new float2(8, 6) });
            Check(GradeGuards.Check(Map, s.Raster, obs, out _) == GradeGuardKind.None, "GR-10 lot clear", "a lot 90 m off the track");

            obs.Clear();
            obs.Lines.Add(new GradeObstacles.Line { A = new float2(200, -300), B = new float2(200, 300), Clearance = 6f });
            Check(GradeGuards.Check(Map, s.Raster, obs, out _) == GradeGuardKind.LiftLine, "GR-10 line", "a lift line across the track");
            obs.Clear();
            obs.Lines.Add(new GradeObstacles.Line { A = new float2(-50, 100), B = new float2(400, 100), Clearance = 6f });
            Check(GradeGuards.Check(Map, s.Raster, obs, out _) == GradeGuardKind.None, "GR-10 line clear", "a parallel line 100 m off");

            obs.Clear();
            int2 wet = (int2)math.round(Map.ToTexel(new float2(120, 0)));
            obs.WetTexels.Add(wet);
            Check(GradeGuards.Check(Map, s.Raster, obs, out _) == GradeGuardKind.Water, "GR-10 water", "a wet texel on the track");
            obs.Clear();
            obs.WetTexels.Add((int2)math.round(Map.ToTexel(new float2(120, 200))));
            Check(GradeGuards.Check(Map, s.Raster, obs, out _) == GradeGuardKind.None, "GR-10 water clear", "water 200 m off");

            foreach (GradeGuardKind g in new[] { GradeGuardKind.Lot, GradeGuardKind.LiftLine, GradeGuardKind.Water, GradeGuardKind.OffMap, GradeGuardKind.TooLarge })
                Check(!GradeGuards.Reason(g).IsEmpty, "GR-10 reason", g.ToString());
        }


        private static void UndoRoundTrip()
        {
            var p = new GradeParams();
            Func<float2, float> ground = xz => 900f + 0.35f * xz.x + 4f * math.sin(xz.y / 9f);
            var s = Solve(Line(new float2(0, 0), new float2(300, 60)), ground, p);
            GradeRaster r = s.Raster;
            var before = new ushort[r.Before.Length];
            var after = new ushort[r.After.Length];
            int changed = 0;
            for (int t = 0; t < before.Length; t++)
            {
                before[t] = Map.ToU16(r.Before[t]);
                after[t] = r.Zone[t] != 0 ? Map.ToU16(r.After[t]) : before[t];
                changed += after[t] != before[t] ? 1 : 0;
            }
            Check(changed > 0, "GR-11 wrote", $"{changed} texels");
            var e = new GradeUndoEntry { X0 = r.X0, Z0 = r.Z0, W = r.W, H = r.H, Before = before, After = after, Label = "test" };
            ushort[] restored = GradeUndo.Restore(e, (ushort[])after.Clone(), out int kept);
            bool exact = kept == 0;
            for (int t = 0; t < before.Length && exact; t++)
                exact = restored[t] == before[t];
            Check(exact, "GR-11 bit-exact", $"kept {kept}");

            // A foreign edit after the grade (a terrain brush on 10 texels): those survive the undo, the rest goes back.
            var current = (ushort[])after.Clone();
            int foreign = 0;
            for (int t = 0; t < current.Length && foreign < 10; t++)
            {
                if (after[t] == before[t])
                    continue;
                current[t] = (ushort)(current[t] + 7);
                foreign++;
            }
            restored = GradeUndo.Restore(e, current, out kept);
            int wrong = 0;
            for (int t = 0; t < current.Length; t++)
            {
                ushort expect = current[t] == after[t] ? before[t] : current[t];
                wrong += restored[t] != expect ? 1 : 0;
            }
            Check(kept == foreign && wrong == 0, "GR-11 foreign kept", $"kept {kept} of {foreign}, wrong {wrong}");
        }

        private static void UndoStack()
        {
            var u = new GradeUndo();
            for (int i = 0; i < 10; i++)
                u.Push(new GradeUndoEntry { W = 1, H = 1, Before = new ushort[1], After = new ushort[1], Label = "g" + i });
            Check(u.Count == GradeUndo.MaxEntries, "GR-12 cap", $"{u.Count}");
            Check(u.Pop().Label == "g9" && u.Pop().Label == "g8", "GR-12 LIFO", "newest first");
            var big = new GradeUndo();
            for (int i = 0; i < GradeUndo.MaxEntries; i++)
                big.Push(new GradeUndoEntry { Before = new ushort[GradeRaster.MaxTexels], After = new ushort[GradeRaster.MaxTexels], Label = "m" + i });
            Check(big.Count == GradeUndo.MaxEntries && big.Bytes <= GradeUndo.MaxBytes, "GR-12 bytes",
                  $"{big.Count} cap-size entries hold {big.Bytes / 1048576f:F0} MB of {GradeUndo.MaxBytes / 1048576} MB");
            var over = new GradeUndo();
            int pushed = over.Push(new GradeUndoEntry { Before = new ushort[GradeRaster.MaxTexels + 1], After = new ushort[GradeRaster.MaxTexels + 1], Label = "x" });
            Check(pushed == -1 && over.Count == 0, "GR-12 over-cap entry", $"push returned {pushed}, {over.Count} stored");
            Check(new GradeUndo().Pop() == null, "GR-12 empty", "pop on empty = null");
        }


        private static void RasterCap()
        {
            var p = new GradeParams();
            Check(!GradeRaster.TooLarge(new int4(0, 0, 1024, 1024)), "GR-17 at cap", "1024 x 1024 = MaxTexels is allowed");
            Check(GradeRaster.TooLarge(new int4(0, 0, 1025, 1024)), "GR-17 over cap", "one column over is refused");
            Check(GradeRaster.TooLarge(new int4(0, 0, int.MaxValue, int.MaxValue)), "GR-17 overflow", "the product does not wrap");

            var path = GradePath.Build(Line(new float2(-5000, -5000), new float2(5000, 5000)), p.SampleStepM);
            var prof = GradeProfile.Solve(path, new float[path.Count], p);
            bool ok = GradeRaster.Bounds(Map, path, prof, p, out int4 rect, out float _);
            Check(ok && GradeRaster.TooLarge(rect), "GR-17 corner to corner", $"a 14 km diagonal needs {rect.z} x {rect.w} texels");

            path = GradePath.Build(Line(new float2(0, 0), new float2(1500, 0)), p.SampleStepM);
            prof = GradeProfile.Solve(path, new float[path.Count], p);
            ok = GradeRaster.Bounds(Map, path, prof, p, out rect, out float _);
            Check(ok && !GradeRaster.TooLarge(rect), "GR-17 normal track", $"a 1.5 km track needs {rect.z} x {rect.w} texels");
        }


        private static void SwitchbackLegs()
        {
            // Two legs 24 m apart joined by a hairpin, on a 30 % hill rising with x; the upper leg sits 7.2 m higher than the lower.
            Func<float2, float> hill = xz => 800f + 0.3f * xz.x;
            var p = new GradeParams();
            var pts = new[] { new float2(0, 0), new float2(0, 200), new float2(12, 214), new float2(24, 200), new float2(24, 0) };
            var s = Solve(pts, hill, p);
            GradeRaster r = s.Raster;
            // A texel 3 m from the lower leg (x = 3) mid-way along: it must sit on the LOWER leg's top.
            int ti = (int)math.round(Map.ToTexel(new float2(1.75f, 100)).x), tj = (int)math.round(Map.ToTexel(new float2(1.75f, 100)).y);
            float2 c = Map.TexelCenter(ti, tj);
            float got = r.After[r.Index(ti, tj)];
            // Nearest leg = the lower one (x = 0): its profile height near s = 100.
            float lowerY = float.NaN;
            float bestD = float.MaxValue;
            for (int i = 0; i < s.Path.Count; i++)
            {
                float d = math.distance(s.Path.Xz[i], c);
                if (d < bestD)
                {
                    bestD = d;
                    lowerY = s.Prof.Y[i];
                }
            }
            Check(math.abs(c.x) < 4f, "GR-13 setup", $"texel x {c.x:F2}");
            Check(math.abs(got - lowerY) < 0.15f, "GR-13 nearest leg", $"{got:F2} vs lower leg {lowerY:F2}");
        }


        private static void Encoding()
        {
            float2 c = Map.TexelCenter(2048, 1000);
            float2 t = Map.ToTexel(c);
            Check(math.all(math.abs(t - new float2(2048, 1000)) < 1e-3f), "GR-14 texel", $"{t}");
            Check(math.abs(Map.Cell.x - 3.5f) < 1e-4f && math.abs(Map.StepM - 0.0625f) < 1e-3f, "GR-14 sizes", $"{Map.Cell.x} m, {Map.StepM} m");
            float worst = 0f;
            for (float h = 0f; h < 4096f; h += 37.3f)
                worst = math.max(worst, math.abs(Map.ToHeight(Map.ToU16(h)) - h));
            Check(worst <= 0.5f * Map.StepM + 1e-4f, "GR-14 round trip", $"{worst:F4} m");
            Check(Map.ToU16(-10f) == 0 && Map.ToU16(5000f) == 65535, "GR-14 clamp", "out of range clamps");
        }


        private static void EditAndInput()
        {
            var e = new GradeEdit();
            Check(GradeInput.StepOf(e.Count, false) == GradeStep.Empty, "GR-15 empty", "no point");
            e.Add(new float2(0, 0));
            e.Add(new float2(100, 0));
            Check(GradeInput.StepOf(e.Count, false) == GradeStep.Path && e.UndoDepth == 2, "GR-15 add", $"{e.Count} / {e.UndoDepth}");
            int ins = e.InsertIndex(new float2(50, 4), 10f);
            Check(ins == 1, "GR-15 insert index", $"{ins}");
            Check(e.InsertIndex(new float2(200, 40), 10f) == 2, "GR-15 append index", "off the path = after the last point");
            e.Insert(ins, new float2(50, 4));
            Check(e.Count == 3 && e.Points[1].y == 4f, "GR-15 insert", "");
            Check(e.PointNear(new float2(98, 1), 8f) == 2, "GR-15 pick", "");
            Check(e.Move(2, new float2(120, 10)) && !e.Move(2, new float2(120, 10)) && !e.Move(7, default), "GR-15 move", "moved / unchanged / missing");
            int depth = e.UndoDepth;
            Check(depth == 4, "GR-15 depth", $"{depth}");
            e.Undo();
            Check(e.Points[2].x == 100f, "GR-15 undo move", "");
            e.Undo();
            Check(e.Count == 2, "GR-15 undo insert", "");
            e.Undo();
            e.Undo();
            Check(e.Count == 0 && e.UndoDepth == 0 && !e.Undo(), "GR-15 undo to empty", "");

            Check(GradeInput.RightClick(GradeStep.Path, true, 3) == GradeBack.CancelDrag, "GR-15 rc drag", "");
            Check(GradeInput.RightClick(GradeStep.Path, false, 3) == GradeBack.UndoEdit, "GR-15 rc edit", "");
            Check(GradeInput.RightClick(GradeStep.OnePoint, false, 1) == GradeBack.UndoEdit, "GR-15 rc one point", "drops the point");
            Check(GradeInput.RightClick(GradeStep.Empty, false, 0) == GradeBack.CloseTool, "GR-15 rc close",
                  "with no point left a right click closes the tool: it never un-grades or deletes a built cat track (field test 2026-10-26b)");
            Check(GradeInput.RightClick(GradeStep.Applying, false, 2) == GradeBack.None, "GR-15 rc applying", "");
            Check(Enum.GetNames(typeof(GradeBack)).Length == 5, "GR-15 no undo-grade step", "GradeBack is None / CancelDrag / UndoEdit / CloseTool / LeaveDeleteMode");

            // Walk back: three points -> 3 undos, then close (however many tracks were built this opening: they stay).
            var w = new GradeEdit();
            w.Add(new float2(0, 0));
            w.Add(new float2(50, 0));
            w.Add(new float2(90, 10));
            var seq = new List<GradeBack>();
            for (int guard = 0; guard < 10; guard++)
            {
                GradeBack b = GradeInput.RightClick(GradeInput.StepOf(w.Count, false), false, w.UndoDepth);
                seq.Add(b);
                if (b == GradeBack.UndoEdit)
                    w.Undo();
                else
                    break;
            }
            Check(string.Join(",", seq) == "UndoEdit,UndoEdit,UndoEdit,CloseTool", "GR-15 walk back", string.Join(",", seq));

            var seen = new HashSet<string>();
            foreach (GradeStep st in new[] { GradeStep.Empty, GradeStep.OnePoint, GradeStep.Path, GradeStep.Applying })
            {
                foreach (bool drag in new[] { false, true })
                {
                    string pr = GradeInput.PromptKey(st, drag);
                    Check(!string.IsNullOrWhiteSpace(pr), "GR-15 prompt", $"{st} empty");
                    seen.Add(pr);
                    string low = pr.ToLowerInvariant();
                    foreach (string brit in new[] { "colour", "metre", "centre", "grey" })
                        Check(!low.Contains(brit), "GR-15 US English", $"{st}: {brit}");
                }
            }
            Check(seen.Count >= 4, "GR-15 prompt per step", $"{seen.Count} distinct");
            Check(GradeInput.PromptKey(GradeStep.Path, true) == "PROMPT_DRAG" && GradeInput.PromptKey(GradeStep.Empty, false) == "PROMPT_EMPTY", "GR-15 prompt keys", "");

            // GR-15d Delete mode (field test 2026-10-26b): right click / Esc only leave the mode, in any step but Applying, whatever is
            // placed; the two-click confirm; the pick is point in outline.
            foreach (GradeStep st in new[] { GradeStep.Empty, GradeStep.OnePoint, GradeStep.Path })
                Check(GradeInput.RightClick(st, false, 3, deleteMode: true) == GradeBack.LeaveDeleteMode, "GR-15d rc leaves delete mode", st.ToString());
            Check(GradeInput.RightClick(GradeStep.Applying, false, 0, deleteMode: true) == GradeBack.None, "GR-15d rc applying", "");
            Check(GradeDeleteInput.Click(-1, -1) == GradeDeleteClick.None, "GR-15d click off", "");
            Check(GradeDeleteInput.Click(2, -1) == GradeDeleteClick.Arm, "GR-15d first click arms", "");
            Check(GradeDeleteInput.Click(2, 2) == GradeDeleteClick.Delete, "GR-15d second click deletes", "");
            Check(GradeDeleteInput.Click(1, 2) == GradeDeleteClick.Arm, "GR-15d other track re-arms", "never deletes the armed one");
            Check(GradeDeleteInput.Click(-1, 2) == GradeDeleteClick.Disarm, "GR-15d click off disarms", "");
            // Walk: hover a track, click, click: exactly one delete, and only on the second click
            int armedIx = -1, deletes = 0, firstDeleteAt = -1;
            for (int c = 0; c < 2; c++)
            {
                GradeDeleteClick dc = GradeDeleteInput.Click(0, armedIx);
                if (dc == GradeDeleteClick.Arm)
                    armedIx = 0;
                else if (dc == GradeDeleteClick.Delete)
                {
                    deletes++;
                    firstDeleteAt = c;
                    armedIx = -1;
                }
            }
            Check(deletes == 1 && firstDeleteAt == 1, "GR-15d two clicks one delete", $"deletes {deletes} at click {firstDeleteAt}");
            var rings = new List<float2[]>
            {
                new[] { new float2(0, 0), new float2(100, 0), new float2(100, 10), new float2(0, 10) },
                new[] { new float2(100, 0), new float2(110, 0), new float2(110, 80), new float2(100, 80) },   // a branch touching the first
            };
            Check(GradeDeleteInput.Pick(rings, new float2(50, 5)) == 0, "GR-15d pick inside", "");
            Check(GradeDeleteInput.Pick(rings, new float2(105, 40)) == 1, "GR-15d pick the branch", "");
            Check(GradeDeleteInput.Pick(rings, new float2(50, 20)) == -1, "GR-15d pick outside", "");
            Check(GradeDeleteInput.Pick(null, new float2(0, 0)) == -1, "GR-15d pick no tracks", "");
            Check(GradeDeleteInput.PromptKey(0, 0) == "PROMPT_DEL_ARMED" && GradeDeleteInput.PromptKey(0, -1) == "PROMPT_DEL_HOVER" &&
                  GradeDeleteInput.PromptKey(-1, -1) == "PROMPT_DEL_NONE", "GR-15d prompts", GradeDeleteInput.PromptKey(0, 0));
        }

        private static string RepoFile(string rel)
        {
            for (string dir = AppContext.BaseDirectory; !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
            {
                string p = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(p))
                    return p;
            }
            return null;
        }

        /// <summary>GR-23: the cat track tool's player text (LocaleEN SkiResort.CatTrack.*, the LP-31 idea of the lift tool): every key the tool
        /// and its tooltip can show exists, reads as plain sentences (at most 25 words, no em dash, no log syntax, no ids), names only the
        /// {ARG}s the code passes, and no key is stale.</summary>
        private static void PlayerText()
        {
            const string W = "GR-23";
            string locPath = RepoFile("mod/SkiResortMod/LocaleEN.cs");
            string toolPath = RepoFile("mod/SkiResortMod/Systems/CatTrackGradeToolSystem.cs");
            string purePath = RepoFile("mod/SkiResortMod/Systems/CatTrackGrade.cs");
            if (!Check(locPath != null && toolPath != null && purePath != null, W, "LocaleEN.cs or the tool sources not found"))
                return;
            string prefix = GradeMsg.Prefix;
            var loc = new Dictionary<string, string>();
            foreach (Match m in new Regex("\\{ \"" + Regex.Escape(prefix) + "([^\"]+)\", \"((?:[^\"\\\\]|\\\\.)*)\" \\}").Matches(File.ReadAllText(locPath)))
                loc[m.Groups[1].Value] = m.Groups[2].Value;
            // The keys the code shows: every GradeMsg.Of("KEY" in the tool and the pure file, plus what the prompt functions return.
            var used = new SortedSet<string>();
            foreach (string path in new[] { toolPath, purePath })
                foreach (Match m in new Regex("GradeMsg\\.Of\\(\"([A-Z_]+)\"").Matches(File.ReadAllText(path)))
                    used.Add(m.Groups[1].Value);
            foreach (GradeStep st in Enum.GetValues(typeof(GradeStep)))
                foreach (bool drag in new[] { false, true })
                    used.Add(GradeInput.PromptKey(st, drag));
            foreach (var ha in new[] { (-1, -1), (0, -1), (0, 0), (1, 0) })
                used.Add(GradeDeleteInput.PromptKey(ha.Item1, ha.Item2));
            foreach (GradeGuardKind g in Enum.GetValues(typeof(GradeGuardKind)))
                if (!GradeGuards.Reason(g).IsEmpty)
                    used.Add(GradeGuards.Reason(g).Key);
            var args = new HashSet<string> { "LENGTH", "MAX", "LIMIT", "CUT", "FILL", "SHORT" };
            int worst = 0;
            foreach (string key in used)
            {
                if (!Check(loc.TryGetValue(key, out string text), W, prefix + key + " is shown but missing from LocaleEN"))
                    continue;
                int words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
                worst = Math.Max(worst, words);
                Check(words <= 25, W, key + " has " + words + " words (at most 25): " + text);
                Check(text.IndexOf('\u2014') < 0 && !text.Contains("\\u2014"), W, key + " has an em dash: " + text);
                Check(!Regex.IsMatch(text, "\\[[A-Za-z:]+\\]|\\d+\\.\\d|Grade \\[|entity|texel|heightmap|\\bguard\\b|\\bm3\\b|\\bsee the log"), W, key + " reads like a log: " + text);
                string low = text.ToLowerInvariant();
                foreach (string brit in new[] { "colour", "metre", "centre", "grey" })
                    Check(!low.Contains(brit), W, key + " is not US English: " + brit);
                foreach (Match a in Regex.Matches(text, "\\{([A-Z]+)\\}"))
                    Check(args.Contains(a.Groups[1].Value), W, key + " names {" + a.Groups[1].Value + "}, which the code never passes");
            }
            foreach (string key in loc.Keys)
                Check(used.Contains(key), W, prefix + key + " in LocaleEN is never shown (stale)");
            Console.WriteLine($"GR-23 cat track player text: {used.Count} key(s) shown, longest {worst} words");

            // The plan line follows the plan: every arg it passes is one a text names; a gentle track reads PLAN, a steep one PLAN_STEEP.
            var p = new GradeParams();
            var gentle = Solve(Line(new float2(0, 0), new float2(200, 0)), xz => 900f + 0.05f * xz.x, p);
            GradeMsg pm = GradeInput.PlanMsg(gentle.Path, gentle.Prof, gentle.Raster, p);
            Check(pm.Key == "PLAN" && loc.TryGetValue("PLAN", out string planText) && pm.Args.All(a => planText.Contains("{" + a.Name + "}")), W + " plan", pm.Signature);
            GradeMsg nr = GradeInput.PlanMsg(gentle.Path, gentle.Prof, null, p);
            Check(nr.Key == "PLAN_LENGTH" && loc.TryGetValue("PLAN_LENGTH", out string lenText) && nr.Args.All(a => lenText.Contains("{" + a.Name + "}")), W + " plan, no raster", nr.Signature);
            var steep = Solve(Line(new float2(0, 0), new float2(200, 0)), xz => 900f + 0.40f * xz.x, p);
            GradeMsg sm = GradeInput.PlanMsg(steep.Path, steep.Prof, steep.Raster, p);
            Check(sm.Key == "PLAN_STEEP" && loc.TryGetValue("PLAN_STEEP", out string steepText) && sm.Args.All(a => steepText.Contains("{" + a.Name + "}")), W + " plan, steep", sm.Signature);
            Check(GradeInput.PlanMsg(null, null, null, p).IsEmpty, W + " no plan", "no path, no line");
        }


        private static void Cost()
        {
            Func<float2, float> mountain = xz => 1500f + 0.28f * xz.x + 6f * math.sin(xz.y / 40f) + 3f * math.cos(xz.x / 23f);
            var pts = new List<float2>();
            for (int i = 0; i <= 8; i++)
                pts.Add(new float2(i * 120f, (i % 2 == 0 ? 0f : 140f)));
            var sw = Stopwatch.StartNew();
            var s = Solve(pts, mountain, new GradeParams());
            sw.Stop();
            Console.WriteLine($"INFO  GR-16: {s.Path.LengthM:F0} m path, {s.Path.Count} samples, {s.Raster.W} x {s.Raster.H} rect, " +
                              $"{s.Raster.Changed} texels changed — solve + raster {sw.Elapsed.TotalMilliseconds:F0} ms; {s.Raster.Describe()}");
            Check(s.Path.LengthM > 1400f, "GR-16 length", $"{s.Path.LengthM:F0}");
            Check(sw.Elapsed.TotalMilliseconds < 3000, "GR-16 time", $"{sw.Elapsed.TotalMilliseconds:F0} ms");
        }


        /// <summary>GR-18: the default flat top is 2.5x the first build's 8 m, inside the settings range, and the earthwork table for
        /// the width choice: a 1 km track climbing 12 % along a 30 % sidehill (the common cat-track case), widths 8 to 32 m.</summary>
        private static void WidthCost()
        {
            var p = new GradeParams();
            Check(p.TopWidthM == 20f && GradeParams.DefaultTopWidthM == 20f, "GR-18 default", $"{p.TopWidthM} m");
            Check(GradeParams.DefaultMaxGrade == SkiRunRatingRules.CatTrackMaxGrade, "GR-18 green rule",
                  "the tool's max grade must be the limit the run rates green under");
            Check(GradeParams.MinTopWidthM <= 8f && GradeParams.MaxTopWidthM >= 24f &&
                  p.TopWidthM >= GradeParams.MinTopWidthM && p.TopWidthM <= GradeParams.MaxTopWidthM, "GR-18 range", "8 and 24 m both settable");
            Func<float2, float> sidehill = xz => 1200f + 0.12f * xz.x + 0.30f * xz.y + 2f * math.sin(xz.x / 30f);
            var pts = Line(new float2(0, 0), new float2(1000, 0));
            int top8 = 0, top20 = 0;
            float cut8 = 0f;
            foreach (float w in new[] { 8f, 16f, 20f, 24f, 32f })
            {
                var q = new GradeParams { TopWidthM = w };
                var sw = Stopwatch.StartNew();
                var s = Solve(pts, sidehill, q);
                sw.Stop();
                int top = 0;
                for (int t = 0; t < s.Raster.Zone.Length; t++)
                    if (s.Raster.Zone[t] == 2)
                        top++;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "INFO  GR-18 width {0,2:F0} m: flat-top texels {1,5}, changed {2,5}, cut {3,7:F0} m3, fill {4,7:F0} m3 (max cut {5:F1} m, " +
                    "max fill {6:F1} m), steep bank texels {7}, solve + raster {8:F0} ms", w, top, s.Raster.Changed, s.Raster.CutM3,
                    s.Raster.FillM3, s.Raster.MaxCutM, s.Raster.MaxFillM, s.Raster.SteepBankTexels, sw.Elapsed.TotalMilliseconds));
                if (w == 8f)
                {
                    top8 = top;
                    cut8 = s.Raster.CutM3;
                }
                if (w == 20f)
                {
                    top20 = top;
                    Check(s.Raster.CutM3 > 2f * cut8, "GR-18 more earth", $"20 m cuts {s.Raster.CutM3:F0} m3 vs 8 m {cut8:F0} m3");
                    Check(sw.Elapsed.TotalMilliseconds < 3000, "GR-18 time", $"{sw.Elapsed.TotalMilliseconds:F0} ms");
                }
            }
            float ratio = top8 > 0 ? top20 / (float)top8 : 0f;
            Check(ratio > 2.0f && ratio < 3.2f, "GR-18 flat top 2.5x", $"20 m / 8 m flat-top texels = {ratio:F2}");
        }


        /// <summary>GR-19: the run outline. A straight track is a 4-node rectangle of the top's area; an S-bend stays simple with the
        /// centerline inside; a hairpin tighter than the half width has its inside loop cut (the raw offsets cross, the ring does not);
        /// legs closer than the width are refused (Crosses); ring nodes keep MinSpacingM along each edge; the GR-16 mountain track at
        /// the default width has a bounded node count.</summary>
        /// <summary>GR-30: the groomer's centerline over outlines the tool really generates: the path stays within a small distance of the
        /// generator's own centerline (a real oracle, not a second run of the same code), keeps its length and stays deterministic.</summary>
        private static void GroomerCenterlines()
        {
            const string W = "GR-30";
            float2[][] shapes =
            {
                new[] { new float2(0, 0), new float2(400, 0) },
                new[] { new float2(0, 0), new float2(120, 60), new float2(240, -40), new float2(360, 20) },
                new[] { new float2(0, 0), new float2(200, 0), new float2(300, 80), new float2(300, 260), new float2(160, 320), new float2(0, 320) },
                new[] { new float2(0, 0), new float2(300, 10), new float2(330, 60), new float2(300, 110), new float2(0, 120), new float2(-30, 180), new float2(0, 240), new float2(300, 250) },
            };
            string[] names = { "straight", "S-bend", "long bend", "switchbacks" };
            float[] widths = { 8f, 12f, 20f, 32f };
            int built = 0;
            for (int sh = 0; sh < shapes.Length; sh++)
            {
                foreach (float width in widths)
                {
                    string tag = $"{W} {names[sh]} {width:0} m";
                    GradePath gp = GradePath.Build(shapes[sh], 2f);
                    float2[] ring = GradeRibbon.Build(gp, width * 0.5f, out GradeRibbonFail ribbonFail);
                    if (ribbonFail != GradeRibbonFail.None)
                    {
                        Console.WriteLine($"INFO {tag}: the tool refuses this one ({ribbonFail}), so it can never reach the groomer");
                        continue;
                    }
                    GroomerPath path = GroomerCenterline.Build(ring, out GroomerPathFail fail);
                    if (!Check(path != null && fail == GroomerPathFail.None, tag, $"no path: {fail}"))
                        continue;
                    built++;
                    float worst = 0f;
                    for (int i = 0; i < path.Points.Length; i++)
                    {
                        float near = float.MaxValue;
                        for (int k = 0; k < gp.Count - 1; k++)
                            near = math.min(near, DistanceToSegment(path.Points[i], gp.Xz[k], gp.Xz[k + 1]));
                        worst = math.max(worst, near);
                    }
                    // The moving average cuts a bend by a fraction of its window, and the outline cuts an inside bend tighter than the half width.
                    float allowed = math.max(1.5f, width * 0.2f);
                    Check(worst <= allowed, tag + " on center", $"{worst:F2} m off the generator's centerline, allowed {allowed:F2}");
                    float trim = math.clamp(0.5f * width, GroomerCenterline.EndTrimMinM, GroomerCenterline.EndTrimMaxM);
                    Check(path.LengthM >= gp.LengthM - 2f * trim - width * 2f && path.LengthM <= gp.LengthM + 1f, tag + " length",
                        $"{path.LengthM:F1} m of {gp.LengthM:F1} m");
                    int outside = 0;
                    for (int i = 0; i < path.Points.Length; i++)
                        if (!Inside(ring, path.Points[i]))
                            outside++;
                    Check(outside == 0, tag + " inside", $"{outside} point(s) outside");
                    GroomerPath again = GroomerCenterline.Build(ring, out _);
                    Check(again != null && again.Points.Length == path.Points.Length && math.all(again.Points[again.Points.Length / 2] == path.Points[path.Points.Length / 2]),
                        tag + " deterministic", "a second build differs");
                }
            }
            Check(built >= 12, W + " coverage", $"only {built} of 16 shapes reached the centerline");
            // Cost: a long winding ribbon builds in milliseconds (one build per frame at most, in the game)
            var winding = new List<float2>();
            for (int i = 0; i <= 60; i++)
                winding.Add(new float2(i * 50f, 60f * math.sin(i * 0.7f)));
            GradePath far = GradePath.Build(winding, 2f);
            float2[] farRing = GradeRibbon.Build(far, 10f, out GradeRibbonFail farFail);
            if (Check(farFail == GradeRibbonFail.None, W + " long", $"{farFail}"))
            {
                var sw = Stopwatch.StartNew();
                GroomerPath farPath = GroomerCenterline.Build(farRing, out GroomerPathFail fail);
                sw.Stop();
                Check(farPath != null, W + " long", $"{fail}");
                Check(sw.ElapsedMilliseconds < 500, W + " long cost", $"{sw.ElapsedMilliseconds} ms for {farRing.Length} nodes");
                Console.WriteLine($"INFO {W}: {far.LengthM:F0} m track, {farRing.Length} nodes: {sw.Elapsed.TotalMilliseconds:F1} ms");
            }
        }

        private static float DistanceToSegment(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float len = math.dot(ab, ab);
            float t = len > 0f ? math.saturate(math.dot(p - a, ab) / len) : 0f;
            return math.distance(p, a + ab * t);
        }

        private static void Ribbon()
        {
            GradePath straight = GradePath.Build(Line(new float2(0, 0), new float2(200, 0)), 2f);
            float2[] ring = GradeRibbon.Build(straight, 10f, out GradeRibbonFail fail);
            Check(fail == GradeRibbonFail.None && ring != null && ring.Length == 4, "GR-19 straight", $"{fail}, {ring?.Length} nodes");
            Check(ring != null && math.abs(Area(ring) - 4000f) < 1f, "GR-19 straight area", $"{(ring != null ? Area(ring) : 0f):F1} m2 vs 4000");

            GradePath s = GradePath.Build(new[] { new float2(0, 0), new float2(120, 60), new float2(240, -40), new float2(360, 20) }, 2f);
            ring = GradeRibbon.Build(s, 10f, out fail);
            Check(fail == GradeRibbonFail.None, "GR-19 S-bend", $"{fail}");
            int outside = 0;
            for (int i = 1; i < s.Count - 1; i++)
                if (!Inside(ring, s.Xz[i]))
                    outside++;
            Check(outside == 0, "GR-19 centerline inside", $"{outside} sample(s) outside");
            Check(math.abs(Area(ring) / (s.LengthM * 20f) - 1f) < 0.05f, "GR-19 S-bend area", $"{Area(ring):F0} m2 vs {s.LengthM * 20f:F0}");
            float minStep = float.MaxValue;
            for (int i = 0; i < ring.Length; i++)
                minStep = math.min(minStep, math.distance(ring[i], ring[(i + 1) % ring.Length]));
            Check(minStep >= GradeRibbon.MinSpacingM - 1e-3f, "GR-19 spacing", $"closest consecutive nodes {minStep:F2} m");

            // Hairpin: legs 40 m apart, apex about 20 m above the legs' ends; a 32 m top (half width 16) is wider than the bend.
            var pin = GradePath.Build(new[] { new float2(0, 0), new float2(0, 150), new float2(20, 172), new float2(40, 150), new float2(40, 0) }, 2f);
            var rawLeft = new List<float2>();
            var rawRight = new List<float2>();
            for (int i = 0; i < pin.Count; i++)
            {
                float2 t = math.normalizesafe(pin.Xz[math.min(i + 1, pin.Count - 1)] - pin.Xz[math.max(i - 1, 0)]);
                rawLeft.Add(pin.Xz[i] + new float2(-t.y, t.x) * 16f);
                rawRight.Add(pin.Xz[i] - new float2(-t.y, t.x) * 16f);
            }
            var raw = new List<float2>(rawLeft);
            for (int i = rawRight.Count - 1; i >= 0; i--)
                raw.Add(rawRight[i]);
            Check(!GradeRibbon.Clear(raw.ToArray(), 0.01f), "GR-19 hairpin raw", "the raw inside offset loops (the case under test)");
            ring = GradeRibbon.Build(pin, 16f, out fail);
            Check(fail == GradeRibbonFail.None, "GR-19 hairpin cut", $"{fail}, {ring?.Length} nodes");

            // Legs 24 m apart with a 24 m top: the two flat tops touch.
            var close = GradePath.Build(new[] { new float2(0, 0), new float2(0, 200), new float2(12, 214), new float2(24, 200), new float2(24, 0) }, 2f);
            GradeRibbon.Build(close, 12f, out fail);
            Check(fail == GradeRibbonFail.Crosses, "GR-19 legs too close", $"{fail}");
            GradeRibbon.Build(close, 10f, out fail);
            Check(fail == GradeRibbonFail.None, "GR-19 legs clear", $"{fail} (20 m top, 4 m gap)");

            Check(GradeRibbon.Build(null, 10f, out fail) == null && fail == GradeRibbonFail.TooShort, "GR-19 no path", $"{fail}");
            Check(GradeRibbon.Simplify(new List<float2>(straight.Xz), 0.5f).Count == 2, "GR-19 simplify", "a straight edge keeps its ends");

            var mpts = new List<float2>();
            for (int i = 0; i <= 8; i++)
                mpts.Add(new float2(i * 120f, (i % 2 == 0 ? 0f : 140f)));
            GradePath m = GradePath.Build(mpts, 2f);
            var sw = Stopwatch.StartNew();
            ring = GradeRibbon.Build(m, 0.5f * GradeParams.DefaultTopWidthM, out fail);
            sw.Stop();
            Console.WriteLine($"INFO  GR-19: {m.LengthM:F0} m zigzag at {GradeParams.DefaultTopWidthM:F0} m: {fail}, {ring?.Length} ring nodes, " +
                              $"{sw.Elapsed.TotalMilliseconds:F1} ms");
            Check(fail == GradeRibbonFail.None && ring.Length < 1000, "GR-19 mountain ring", $"{fail}, {ring?.Length} nodes");
        }

        /// <summary>The test ground read like the game's TerrainUtils.SampleHeight: bilinear over texel centers. GradeRaster.Sampler (the
        /// tool's own) reads the raster over it.</summary>
        private struct GradedSampler : IHeightSampler
        {
            public Func<float2, float> Ground;

            private float H(int i, int j) => Ground(Map.TexelCenter(i, j));

            public float Sample(float3 world)
            {
                float2 t = Map.ToTexel(world.xz);
                int i = (int)math.floor(t.x), j = (int)math.floor(t.y);
                float fx = t.x - i, fz = t.y - j;
                return math.lerp(math.lerp(H(i, j), H(i + 1, j), fx), math.lerp(H(i, j + 1), H(i + 1, j + 1), fx), fz);
            }
        }

        private static SlopeResult RateOn(float2[] outline, Func<float2, float> ground, GradeRaster graded, out RateStatus status)
        {
            var bilinear = new GradedSampler { Ground = ground };   // the test ground read like TerrainUtils.SampleHeight (texel centers)
            var sampler = new GradeRaster.Sampler { Map = Map, R = graded, After = true, Outside = w => bilinear.Sample(w) };
            var nodes = new float3[outline.Length];
            for (int i = 0; i < outline.Length; i++)
                nodes[i] = new float3(outline[i].x, sampler.Sample(new float3(outline[i].x, 0f, outline[i].y)), outline[i].y);
            float2 o = Map.TexelCenter(0, 0);
            var result = new SlopeResult();
            status = SkiRunRater.Rate(nodes, nodes.Length, new double2(o.x, o.y), ref sampler, Map.Cell.x, 0, new SlopeGrid(), null, result,
                                      out int _, out float _, out float _);
            return result;
        }

        private static string Rated(SlopeResult r, RateStatus s) =>
            s != RateStatus.Rated ? s.ToString() : string.Format(CultureInfo.InvariantCulture, "{0} (crux {1:P1}, steepest {2:P1})", r.Band, r.RatingGrade, r.SteepestGrade);

        /// <summary>GR-20 (2026-10-25, the crossing requirement): the game's rater (SkiRunRater + SkiSlopeClassifier) on graded terrain.
        /// (a) Why a cat track's band does not come from the rater: its own outline on its graded ground reads the profile on straight
        /// legs but up to ~29 % at a hairpin graded to 25 % (20 m windows at the bend). Measured, printed. (b) A run crossed by a 20 m
        /// track: graded straight through (no keep), a flat bench flips green 20-24 % runs to blue, blue 37 % to black, black 50 % to
        /// double black (printed); with the crossed run's outline kept (GradeRaster.Build keep, the tool's way) the run keeps its band
        /// and its band (since DECISIONS 420 inside changes stay within InsideMaxM). (c) KeepWeight / Inside / the final-surface guard: a
        /// level traverse is fine, a diagonal down a 45 % run is not.</summary>
        private static void CrossingRatings()
        {
            var p = new GradeParams();

            // (a) the cat track itself (printed only: the band of a tool-built track is Green by construction, SkiRunRatingRules.CatTrackBand)
            Func<float2, float> hill = xz => 1000f + 0.35f * xz.y;
            var climb = new[] { new float2(0, 0), new float2(200, 60), new float2(215, 85), new float2(200, 110), new float2(0, 170) };
            var s = Solve(climb, hill, p);
            float2[] ring = GradeRibbon.Build(s.Path, 0.5f * p.TopWidthM, out GradeRibbonFail fail);
            Check(s.Prof.Feasible && fail == GradeRibbonFail.None, "GR-20 setup", $"feasible {s.Prof.Feasible}, ring {fail}");
            SlopeResult before = RateOn(ring, hill, null, out RateStatus sb);
            SlopeResult after = RateOn(ring, hill, s.Raster, out RateStatus sa);
            Console.WriteLine($"INFO  GR-20a cat track outline (hairpin) on a 35 % hill, max profile grade {s.Prof.MaxGradeAbs:P1}: ungraded " +
                              $"{Rated(before, sb)}, graded {Rated(after, sa)}");

            // (b) runs crossed by a 300 m traverse at mid height
            foreach (float fall in new[] { 0.15f, 0.22f, 0.30f, 0.37f, 0.45f, 0.52f })
            {
                Func<float2, float> slope = xz => 1000f + fall * xz.y;
                float2[] run = { new float2(-30, 0), new float2(30, 0), new float2(30, 300), new float2(-30, 300) };
                var track = Line(new float2(-150, 140), new float2(150, 160));
                var through = Solve(track, slope, p);
                var kept = Solve(track, slope, p, true, new List<float2[]> { run });
                SlopeResult rb = RateOn(run, slope, null, out RateStatus rsb);
                SlopeResult rt = RateOn(run, slope, through.Raster, out RateStatus rst);
                SlopeResult rk = RateOn(run, slope, kept.Raster, out RateStatus rsk);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "INFO  GR-20b run {0:P0} crossed by a {1:F0} m top: before {2}; graded through {3}; crossing kept {4} ({5})", fall,
                    p.TopWidthM, Rated(rb, rsb), Rated(rt, rst), Rated(rk, rsk), kept.Raster.Describe()));
                Check(rsb == RateStatus.Rated && rsk == RateStatus.Rated && rk.Band == rb.Band, "GR-20b band kept",
                      $"{fall:P0}: {Rated(rb, rsb)} -> {Rated(rk, rsk)}");
                Check(math.abs(rk.RatingGrade - rb.RatingGrade) < 0.01f, "GR-20b crux kept", $"{fall:P0}: crux {rb.RatingGrade:P1} -> {rk.RatingGrade:P1}");
                Check(kept.Raster.KeptTexels + kept.Raster.InsideTexels > 0, "GR-20b inside texels", kept.Raster.Describe());
                int inside = 0, moved = 0;
                for (int j = kept.Raster.Z0; j < kept.Raster.Z0 + kept.Raster.H; j++)
                {
                    for (int i = kept.Raster.X0; i < kept.Raster.X0 + kept.Raster.W; i++)
                    {
                        if (!GradeRaster.Inside(run, Map.TexelCenter(i, j)))
                            continue;
                        inside++;
                        int t = kept.Raster.Index(i, j);
                        if (math.abs(kept.Raster.After[t] - kept.Raster.Before[t]) > p.InsideMaxM + 1e-3f)
                            moved++;
                    }
                }
                Check(inside > 0 && moved == 0, "GR-20b inside within the allowance", $"{moved} of {inside} texel(s) inside the crossed run moved over {p.InsideMaxM} m");
                Check(kept.Raster.AlongTrackSteepSample(Map, kept.Path, new List<float2[]> { run }, p.CrossBlendM, p.MaxGrade) < 0,
                      "GR-20b level crossing", "a near-level traverse is no steep crossing");
            }

            // (c) the pieces
            float2[] sq = { new float2(0, 0), new float2(10, 0), new float2(10, 10), new float2(0, 10) };
            var keep = new List<float2[]> { sq };
            float4[] box = GradeRaster.KeepBoxes(keep, 15f);
            Check(GradeRaster.KeepWeight(new float2(5, 5), keep, box, 15f) == 1f, "GR-20c inside", "1 inside");
            Check(math.abs(GradeRaster.KeepWeight(new float2(17.5f, 5), keep, box, 15f) - 0.5f) < 1e-4f, "GR-20c fade", "7.5 m out of 15 = 0.5");
            Check(GradeRaster.KeepWeight(new float2(30, 5), keep, box, 15f) == 0f, "GR-20c beyond", "0 beyond the fade");
            Check(GradeRaster.KeepBoxes(null, 15f) == null && GradeRaster.KeepBoxes(new List<float2[]>(), 15f) == null, "GR-20c none", "no keep, no boxes");
            Func<float2, float> steep = xz => 1000f + 0.45f * xz.y;
            float2[] steepRun = { new float2(-30, 0), new float2(30, 0), new float2(30, 300), new float2(-30, 300) };
            var diag = Solve(Line(new float2(-150, 300), new float2(150, 0)), steep, p, true, new List<float2[]> { steepRun });
            int at = diag.Raster.AlongTrackSteepSample(Map, diag.Path, new List<float2[]> { steepRun }, p.CrossBlendM, p.MaxGrade);
            Check(at >= 0, "GR-20c steep crossing",
                  $"a diagonal down a 45 % run (ground {0.45f * 300f / math.length(new float2(300, 300)):P0} along the track) must trip CrossingSteep (sample {at})");
        }

        // ----- GR-20d / GR-21 (review fixes, 2026-10-25): the grade along the track in the crossing fade, and a track beside a run -----

        /// <summary>What the tool does for a plan with crossed runs: pin mask -> profile -> raster (keep) -> the two crossing guards.</summary>
        private sealed class Planned
        {
            public GradePath Path;
            public GradeProfile Prof;
            public GradeRaster Raster;
            public int GuardAt = -1;   // first path sample the crossing guards flag, or -1
            public float FadeMaxGrade; // the steepest step of the FINAL surface along the path where a kept outline's fade or inside reaches
        }

        private static Planned Plan(IList<float2> pts, Func<float2, float> ground, GradeParams p, IList<float2[]> keep, bool pin = true)
        {
            var pl = new Planned();
            var s = Solve(pts, ground, p, true, keep, pin);
            pl.Path = s.Path;
            pl.Prof = s.Prof;
            pl.Raster = s.Raster;
            var g = new float[s.Path.Count];
            for (int i = 0; i < g.Length; i++)
                g[i] = ground(s.Path.Xz[i]);
            pl.GuardAt = s.Raster.AlongTrackSteepSample(Map, s.Path, keep, p.CrossBlendM, p.MaxGrade);
            pl.FadeMaxGrade = FadeBandMaxGrade(s.Raster, s.Path, keep, p.CrossBlendM, out int _);
            return pl;
        }

        /// <summary>The steepest step of the final surface (the raster's After, bilinear at the path samples) over the steps where either
        /// end is inside a kept outline or inside its fade band.</summary>
        private static float FadeBandMaxGrade(GradeRaster r, GradePath path, IList<float2[]> keep, float blendM, out int at)
        {
            at = -1;
            float worst = 0f;
            if (keep == null || keep.Count == 0)
                return 0f;
            float4[] boxes = GradeRaster.KeepBoxes(keep, blendM);
            bool[] fade = GradeRaster.EnteredOutlines(path, keep);
            var h = new float[path.Count];
            var w = new float[path.Count];
            for (int i = 0; i < path.Count; i++)
            {
                float2 t = Map.ToTexel(path.Xz[i]);
                int ti = (int)math.floor(t.x), tj = (int)math.floor(t.y);
                float fx = t.x - ti, fz = t.y - tj;
                float H(int a, int b) => r.After[r.Index(a, b)];
                h[i] = math.lerp(math.lerp(H(ti, tj), H(ti + 1, tj), fx), math.lerp(H(ti, tj + 1), H(ti + 1, tj + 1), fx), fz);
                w[i] = GradeRaster.KeepWeight(path.Xz[i], keep, boxes, blendM, fade);
            }
            for (int i = 0; i < path.Count - 1; i++)
            {
                if (w[i] <= 0f && w[i + 1] <= 0f)
                    continue;
                float gr = math.abs(h[i + 1] - h[i]) / (path.S[i + 1] - path.S[i]);
                if (gr > worst)
                {
                    worst = gr;
                    at = i;
                }
            }
            return worst;
        }

        private static void FadeAndAlongside()
        {
            const float Tol = 0.01f;   // the net guard's margin over the max grade: 1 pp (bilinear read noise measured 0.7 pp)
            var p = new GradeParams();
            float2[] run = { new float2(195, -30), new float2(330, -30), new float2(330, 30), new float2(195, 30) };
            var keep = new List<float2[]> { run };

            // 1. FollowGround: a 35 % pitch for 40 m right before the run, on a gentle 10 % hillside
            Func<float2, float> pitch = xz => 1000f + 0.10f * xz.x + (xz.x < 150f ? 0f : xz.x < 190f ? 0.25f * (xz.x - 150f) : 10f);
            var c1 = Plan(Line(new float2(0, 0), new float2(400, 0)), pitch, p, keep);
            Console.WriteLine($"INFO  GR-21 case 1 FollowGround, 35 % pitch before the run: profile max {c1.Prof.MaxGradeAbs:P1}, final surface max {c1.FadeMaxGrade:P1} " +
                              $"in the fade band, guard sample {c1.GuardAt}");
            Check(c1.GuardAt >= 0 || c1.FadeMaxGrade <= p.MaxGrade + Tol, "GR-21 pitch before a crossing",
                  $"the final surface reaches {c1.FadeMaxGrade:P1} along the track and no guard fires (limit {p.MaxGrade:P0})");

            // 1b. The same pitch 45 m before the run: the profile lags the ground by a few m at the edge. Unpinned, the fade pitches over the limit
            // (the review's 38 %); pinned, the solver carries the approach and the track is BUILT, no guard.
            Func<float2, float> early = xz => 1000f + 0.10f * xz.x + (xz.x < 140f ? 0f : xz.x < 180f ? 0.25f * (xz.x - 140f) : 10f);
            var open = Plan(Line(new float2(0, 0), new float2(400, 0)), early, p, keep, pin: false);
            var shut = Plan(Line(new float2(0, 0), new float2(400, 0)), early, p, keep);
            Console.WriteLine($"INFO  GR-21 case 1b pitch 45 m before the run: unpinned surface max {open.FadeMaxGrade:P1} (guard {open.GuardAt}), pinned {shut.FadeMaxGrade:P1} (guard {shut.GuardAt})");
            Check(open.GuardAt >= 0 || open.FadeMaxGrade > p.MaxGrade + Tol, "GR-21 pin needed", $"the unpinned plan must be over the limit to prove the case ({open.FadeMaxGrade:P1})");
            Check(shut.GuardAt < 0 && shut.FadeMaxGrade <= p.MaxGrade + Tol, "GR-21 pin carries the approach", $"pinned: surface {shut.FadeMaxGrade:P1}, guard {shut.GuardAt}");

            // 2. ConstantGrade 12 %, a 4 m hollow right at the edge of the run (the straight ramp cannot follow it)
            Func<float2, float> hollow = xz => 1000f + 0.12f * xz.x - (math.abs(xz.x - 178f) < 12f ? 4f * (1f - math.abs(xz.x - 178f) / 12f) : 0f);
            var pc = new GradeParams { Mode = GradeProfileMode.ConstantGrade };
            var c2 = Plan(Line(new float2(0, 0), new float2(400, 0)), hollow, pc, keep);
            Console.WriteLine($"INFO  GR-21 case 2 ConstantGrade 12 %, 4 m hollow at the edge: profile max {c2.Prof.MaxGradeAbs:P1}, final surface max {c2.FadeMaxGrade:P1}, " +
                              $"guard sample {c2.GuardAt}");
            Check(c2.GuardAt >= 0 || c2.FadeMaxGrade <= pc.MaxGrade + Tol, "GR-21 hollow before a crossing",
                  $"the final surface reaches {c2.FadeMaxGrade:P1} along the track and no guard fires");

            // 3. GR-20d (the other review's case): a 25 % track straight up a 32 % hillside, a 60 m wide run across it 120 m up. Inside the run
            // the ground itself is steeper than the limit, so the plan must be refused, never built silently.
            Func<float2, float> steep = xz => 1000f + 0.32f * xz.x;
            float2[] runAcross = { new float2(120, -40), new float2(180, -40), new float2(180, 40), new float2(120, 40) };
            var c3 = Plan(Line(new float2(0, 0), new float2(300, 0)), steep, p, new List<float2[]> { runAcross });
            Console.WriteLine($"INFO  GR-20d 25 % track up a 32 % hill through a run across it: profile max {c3.Prof.MaxGradeAbs:P1}, final surface max {c3.FadeMaxGrade:P1}, " +
                              $"guard sample {c3.GuardAt}");
            Check(c3.GuardAt >= 0 || c3.FadeMaxGrade <= p.MaxGrade + Tol, "GR-20d climbing through a run",
                  $"surface {c3.FadeMaxGrade:P1}, no guard");
            // the level traverse of GR-20b still passes untouched
            Func<float2, float> level = xz => 1000f + 0.37f * xz.y;
            float2[] run60 = { new float2(-30, 0), new float2(30, 0), new float2(30, 300), new float2(-30, 300) };
            var c4 = Plan(Line(new float2(-150, 140), new float2(150, 160)), level, p, new List<float2[]> { run60 });
            Check(c4.GuardAt < 0 && c4.FadeMaxGrade <= p.MaxGrade + Tol, "GR-20d level traverse", $"guard {c4.GuardAt}, surface {c4.FadeMaxGrade:P1}");

            // 4. A track ALONGSIDE a run (centerline 18 m from its edge, never entering it) on a 30 % sidehill keeps its whole flat top
            Func<float2, float> side = xz => 1000f + 0.10f * xz.x + 0.30f * xz.y;
            float2[] runBeside = { new float2(100, 18), new float2(300, 18), new float2(300, 80), new float2(100, 80) };
            var track = Line(new float2(0, 0), new float2(400, 0));
            var alone = Solve(track, side, p);
            var beside = Solve(track, side, p, true, new List<float2[]> { runBeside });
            int top = 0, off = 0;
            float worst = 0f;
            for (int t = 0; t < alone.Raster.After.Length; t++)
            {
                if (alone.Raster.Zone[t] != 2)
                    continue;
                top++;
                float d = math.abs(beside.Raster.After[t] - alone.Raster.After[t]);
                if (d > 0.3f)
                    off++;
                worst = math.max(worst, d);
            }
            Console.WriteLine($"INFO  GR-21 case 4 track 18 m beside a run on a 30 % sidehill: {off} of {top} flat-top texels off the bench by more than 0.3 m, worst {worst:F2} m");
            // The price of no fade beside a run: the side slope may still be off the ground where it meets the kept outline. Measured, printed.
            var br = beside.Raster;
            float edge = 0f;
            for (int j = br.Z0; j < br.Z0 + br.H - 1; j++)
            {
                for (int i = br.X0; i < br.X0 + br.W; i++)
                {
                    bool a0 = GradeRaster.Inside(runBeside, Map.TexelCenter(i, j)), a1 = GradeRaster.Inside(runBeside, Map.TexelCenter(i, j + 1));
                    if (a0 != a1)
                        edge = math.max(edge, math.abs(br.After[br.Index(i, j)] - br.After[br.Index(i, j + 1)]) - 0.30f * Map.Cell.y);   // minus the hillside itself
                }
            }
            Console.WriteLine($"INFO  GR-21 case 4 step across the kept outline beside the track (over the hillside's own 30 %): up to {edge:F2} m between neighboring texels");
            Check(top > 0 && off == 0, "GR-21 bench beside a run", $"{off} of {top} flat-top texels moved (worst {worst:F2} m) by a run the track never enters");
        }

        /// <summary>GR-22 (DECISIONS 420, 2026-10-26): grading INSIDE a crossed run. (a) The along-track correction is clamped to InsideMaxM:
        /// a flat constant-grade track over a raised plateau inside a run cuts 2.0 m of a 2.0 m plateau and 2.0 m of a 2.1 m one (0.1 m stays),
        /// half that 5 m inside the outline (the 10 m inward fade), none outside the flat top beyond the side fade; the cross slope is the
        /// ground's (no bench). (b) The allowance steps down until the crossed run keeps its rated band: a 23.5 % green run under a raised
        /// constant-grade track turns Blue at 2 m (crux 26 %); the chooser takes 1 m, which keeps it Green.
        /// (c) A level traverse of 15-52 % runs keeps their bands at the full 2 m.</summary>
        private static void InsideRuns()
        {
            float2[] run = { new float2(-60, -60), new float2(60, -60), new float2(60, 60), new float2(-60, 60) };
            var keep = new List<float2[]> { run };
            foreach (float plateau in new[] { 2.0f, 2.1f })
            {
                Func<float2, float> ground = xz => 1000f + (math.abs(xz.x) < 45f && math.abs(xz.y) < 45f ? plateau : 0f);
                var pc = new GradeParams { Mode = GradeProfileMode.ConstantGrade };
                var s = Solve(Line(new float2(-200, 0.25f), new float2(200, 0.25f)), ground, pc, true, keep);
                float At(float x, float z)
                {
                    float2 t = Map.ToTexel(new float2(x, z));
                    int i = (int)math.round(t.x), j = (int)math.round(t.y);
                    return s.Raster.After[s.Raster.Index(i, j)] - s.Raster.Before[s.Raster.Index(i, j)];
                }
                float center = At(0f, 0f);
                float edge = At(-55f, 0f);      // 5 m inside the outline: half the inward fade (ground there is the plain, no plateau)
                float wide = At(0f, 40f);       // 30 m off the flat top: beyond the 10 m side fade
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "INFO  GR-22a plateau {0:F1} m inside a run: center change {1:F2} m, " +
                    "5 m in from the edge {2:F2} m, 30 m off the track {3:F2} m; {4}", plateau, center, edge, wide, s.Raster.Describe()));
                Check(math.abs(center + 2.0f) < 0.02f, "GR-22a clamp", $"plateau {plateau:F1} m: the center is cut {-center:F2} m, expected 2.0");
                Check(math.abs(wide) < 1e-3f, "GR-22a side fade", $"30 m off the track nothing changes ({wide:F3} m)");
                Check(s.Raster.MaxInsideM <= pc.InsideMaxM + 1e-4f, "GR-22a never over 2 m", $"max inside change {s.Raster.MaxInsideM:F3} m");
            }
            // The inward fade alone: a 2 m plateau reaching the outline's edge, cut at 5 m in (half the 10 m fade) about 1 m
            {
                Func<float2, float> wall = xz => 1000f + (math.abs(xz.x) < 70f && math.abs(xz.y) < 45f ? 2f : 0f);
                var pc = new GradeParams { Mode = GradeProfileMode.ConstantGrade };
                var s = Solve(Line(new float2(-200, 0.25f), new float2(200, 0.25f)), wall, pc, true, keep);
                float2 t = Map.ToTexel(new float2(-55f, 0f));
                int ti = (int)math.round(t.x), tj = (int)math.round(t.y);
                float2 c = Map.TexelCenter(ti, tj);
                float expect = -2f * math.saturate((60f + c.x) / pc.InsideFadeM);
                float got = s.Raster.After[s.Raster.Index(ti, tj)] - s.Raster.Before[s.Raster.Index(ti, tj)];
                Check(math.abs(got - expect) < 0.05f, "GR-22a inward fade", $"at x {c.x:F2} ({60f + c.x:F2} m in): {got:F2} m, expected {expect:F2} m");
            }

            // (b) a 40 m wide 23.5 % green run under a constant-grade track whose ramp rides 3-4 m above it (a 6 m mound under the track's
            // start): the full 2 m fill along the corridor would turn the run Blue, 1 m keeps it Green
            {
                Func<float2, float> wobble = xz => 1000f + 0.235f * xz.y + 6f * math.saturate((-120f - xz.x) / 40f);
                float2[] runB = { new float2(-20, -150), new float2(20, -150), new float2(20, 150), new float2(-20, 150) };
                var keepB = new List<float2[]> { runB };
                var track = Line(new float2(-200, 0.25f), new float2(200, 0.25f));
                SlopeResult b0 = RateOn(runB, wobble, null, out RateStatus st0);
                var bands = new Dictionary<float, SlopeBand>();
                bool KeepsBand(float a)
                {
                    var pa = new GradeParams { InsideMaxM = a, Mode = GradeProfileMode.ConstantGrade };
                    var sa = Solve(track, wobble, pa, true, keepB);
                    SlopeResult ra = RateOn(runB, wobble, sa.Raster, out RateStatus sta);
                    bands[a] = ra.Band;
                    return sta == RateStatus.Rated && ra.Band == b0.Band;
                }
                bool full = KeepsBand(2f);
                float chosen = GradeRaster.ChooseInsideAllowance(KeepsBand);
                Console.WriteLine($"INFO  GR-22b 23.5 % green run under a raised constant-grade track: before {Rated(b0, st0)}; at 2 m {bands[2f]}; chosen allowance {chosen:F1} m " +
                                  $"({string.Join(", ", bands)})");
                Check(st0 == RateStatus.Rated && !full, "GR-22b the case is real", $"at 2 m the band must change ({b0.Band} -> {bands[2f]})");
                Check(chosen >= 0f && chosen < 2f, "GR-22b reduced", $"a smaller allowance keeps the band (chosen {chosen})");
                Check(chosen < 0f || bands[chosen] == b0.Band, "GR-22b band kept", $"chosen {chosen}: {bands[chosen]} vs {b0.Band}");
            }
            // (c) a level traverse of a 15 % run over a short 1.5 m bump ON the track line (37.5 % along the track): the ground inside the run is
            // steeper than the limit, which the old rule refused ("cross it more level"); within 2 m the profile flattens it and the track builds
            {
                Func<float2, float> bump = xz => 1000f + 0.15f * xz.y + 1.5f * math.max(0f, 1f - math.abs(xz.x) / 4f);
                float2[] runC = { new float2(-30, -150), new float2(30, -150), new float2(30, 150), new float2(-30, 150) };
                var keepC = new List<float2[]> { runC };
                var c = Plan(Line(new float2(-150, 0.25f), new float2(150, 0.25f)), bump, new GradeParams(), keepC);
                float steepGround = 0f;
                for (int i = 0; i < c.Path.Count - 1; i++)
                    if (GradeRaster.Inside(runC, c.Path.Xz[i]) && GradeRaster.Inside(runC, c.Path.Xz[i + 1]))
                        steepGround = math.max(steepGround, math.abs(c.Prof.Ground[i + 1] - c.Prof.Ground[i]) / (c.Path.S[i + 1] - c.Path.S[i]));
                Console.WriteLine($"INFO  GR-22c bump on the track inside a 15 % run: ground up to {steepGround:P1} along the track, final surface {c.FadeMaxGrade:P1}, " +
                                  $"guard {c.GuardAt}; {c.Raster.Describe()}");
                Check(steepGround > 0.30f, "GR-22c the case is real", $"the ground inside must be steeper than the limit ({steepGround:P1})");
                Check(c.GuardAt < 0 && c.FadeMaxGrade <= 0.26f, "GR-22c built inside", $"surface {c.FadeMaxGrade:P1}, guard {c.GuardAt}");
                Check(c.Raster.InsideTexels > 0 && c.Raster.MaxInsideM <= 2f + 1e-4f, "GR-22c corrected within 2 m", c.Raster.Describe());
            }
            Check(GradeRaster.ChooseInsideAllowance(a => false) == -1f, "GR-22b refused", "no allowance keeps the band: -1 (guard CrossedRunBand)");
            Check(GradeRaster.ChooseInsideAllowance(a => true) == 2f, "GR-22b full", "the first step is 2 m");
            Check(GradeParams.InsideAllowanceSteps[GradeParams.InsideAllowanceSteps.Length - 1] == 0f, "GR-22b last step", "the last step is 0 (the old keep)");
        }

        /// <summary>GR-23: the default profile is untouched by the narrow full-cut option (GradeParams.NoFill). Each fixture is solved
        /// with the default GradeParams (or the existing non-default knobs) and every output bit (ground, profile, before / after heights,
        /// zones, volumes, counters) is hashed. The expected hashes were taken from the code BEFORE the NoFill change (main a3d2bd1), so
        /// a difference in any default result, however small, fails here. Re-baseline only for a deliberate change of the default.</summary>
        private static void GoldenDefaultProfile()
        {
            Func<float2, float> mountain = xz => 1500f + 0.28f * xz.x + 6f * math.sin(xz.y / 40f) + 3f * math.cos(xz.x / 23f);
            Func<float2, float> sidehill = xz => 1200f + 0.12f * xz.x + 0.30f * xz.y + 2f * math.sin(xz.x / 30f);
            Func<float2, float> hill60 = xz => 1000f + 0.6f * xz.y;
            Func<float2, float> mound = xz => math.abs(xz.x) < 60f ? 5f : 0f;
            Func<float2, float> ditch = xz => math.abs(xz.x) < 60f ? -5f : 0f;
            Func<float2, float> hump = xz => 100f + 6f * math.exp(-xz.x * xz.x / 2500f);
            Func<float2, float> bump = xz => 1000f + 0.15f * xz.y + 1.5f * math.max(0f, 1f - math.abs(xz.x) / 4f);
            float2[] runA = { new float2(-30, -150), new float2(30, -150), new float2(30, 150), new float2(-30, 150) };
            var keepA = new List<float2[]> { runA };
            var sw = new List<float2>();
            for (int i = 0; i <= 6; i++)
                sw.Add(new float2(i * 100f, i % 2 == 0 ? 0f : 120f));
            float2[] km = Line(new float2(0, 0), new float2(1000, 0));
            float2[] cross = Line(new float2(-150, 0.25f), new float2(150, 0.25f));
            float2[] wide = Line(new float2(-200, 0.25f), new float2(200, 0.25f));
            var cases = new (string Name, IList<float2> Pts, Func<float2, float> Ground, GradeParams P, IList<float2[]> Keep)[]
            {
                ("mountain switchbacks, default", sw, mountain, new GradeParams(), null),
                ("sidehill 1 km, default", km, sidehill, new GradeParams(), null),
                ("sidehill 1 km, 8 m", km, sidehill, new GradeParams { TopWidthM = 8f }, null),
                ("sidehill 1 km, 32 m 10 %", km, sidehill, new GradeParams { TopWidthM = 32f, MaxGrade = 0.10f }, null),
                ("60 % sidehill", Line(new float2(0, 0), new float2(200, 0)), hill60, new GradeParams(), null),
                ("mound cut, constant grade 8 m", wide, mound, new GradeParams { Mode = GradeProfileMode.ConstantGrade, TopWidthM = 8f }, null),
                ("ditch fill, constant grade 8 m", wide, ditch, new GradeParams { Mode = GradeProfileMode.ConstantGrade, TopWidthM = 8f }, null),
                ("hump 10 %, cut 1:1 fill 1:3", cross, hump, new GradeParams { MaxGrade = 0.10f, CutRun = 1f, FillRun = 3f }, null),
                ("crossed run", cross, bump, new GradeParams(), keepA),
            };
            string[] expect = GoldenHashes;
            var got = new List<string>();
            for (int k = 0; k < cases.Length; k++)
            {
                var c = cases[k];
                string h = HashSolved(Solve(c.Pts, c.Ground, c.P, true, c.Keep));
                got.Add(h);
                bool known = expect.Length == cases.Length;
                Check(known && expect[k] == h, "GR-23 default unchanged", known ? $"{c.Name}: {h} vs {expect[k]}" : $"{c.Name}: {h} (no baseline)");
            }
            if (expect.Length != cases.Length)
                Console.WriteLine("GOLDEN " + string.Join(", ", got.Select(x => "\"" + x + "\"")));
        }

        private static readonly string[] GoldenHashes =
        {
            "abb134cbd097775a", "32d5cd8a372b8d96", "5dee9d8da1069e20", "e89eda14b426811e", "bb9690017069d4d1",
            "88b027b00c7dadf1", "4d00f23e9d6095b8", "232f8aa457aa0277", "a4c448b461db794d",
        };

        private static string HashSolved(Solved s)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                foreach (float f in s.Prof.Ground) w.Write(f);
                foreach (float f in s.Prof.Y) w.Write(f);
                foreach (float f in s.Prof.SegGrade) w.Write(f);
                w.Write(s.Prof.MaxGradeAbs); w.Write(s.Prof.AvgGradeAbs); w.Write(s.Prof.MaxCutM); w.Write(s.Prof.MaxFillM);
                w.Write(s.Prof.ShortfallM); w.Write(s.Prof.Feasible);
                GradeRaster r = s.Raster;
                w.Write(r.X0); w.Write(r.Z0); w.Write(r.W); w.Write(r.H); w.Write(r.Changed);
                foreach (float f in r.Before) w.Write(f);
                foreach (float f in r.After) w.Write(f);
                w.Write(r.Zone);
                w.Write(r.CutM3); w.Write(r.FillM3); w.Write(r.MaxCutM); w.Write(r.MaxFillM); w.Write(r.SteepBankTexels);
                w.Write(r.KeptTexels); w.Write(r.InsideTexels); w.Write(r.MaxInsideM); w.Write(r.MaxBankStepM);
                w.Flush();
                using (var sha = System.Security.Cryptography.SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(ms.ToArray())).Replace("-", "").Substring(0, 16).ToLowerInvariant();
            }
        }

        /// <summary>Solve a NoFill profile the way the planner calls it: the ground array is GradeProfile.LowEdgeGround.</summary>
        private static Solved SolveNarrow(IList<float2> pts, Func<float2, float> ground, GradeParams p, IList<float2[]> keep = null)
        {
            var s = new Solved { Path = GradePath.Build(pts, p.SampleStepM) };
            float[] g = GradeProfile.LowEdgeGround(s.Path, ground, 0.5f * p.TopWidthM);
            s.Prof = GradeProfile.Solve(s.Path, g, p, GradeRaster.PinTolerance(s.Path, keep, p));
            if (GradeRaster.Bounds(Map, s.Path, s.Prof, p, out int4 rect, out float reach))
            {
                s.Reach = reach;
                s.Raster = GradeRaster.Build(Map, rect, s.Path, s.Prof, p, reach, (i, j) => ground(Map.TexelCenter(i, j)), keep);
            }
            return s;
        }

        /// <summary>GR-24: the narrow full-cut profile (DECISIONS 443). Parameters; the 50 % cross slope template (tread, 1:1 bank,
        /// nothing raised, no fill volume); the unfilled count when a caller solves against the centerline ground; the grade limit on
        /// the bench; cut-only on a hump and across a crossed run; earthwork per meter next to the default profile; time.</summary>
        private static void NarrowProfile()
        {
            var np = GradeParams.NarrowFullCut();
            Check(np.NoFill && np.TopWidthM == 10f && np.CutRun == 1f && np.MaxGrade == SkiRunRatingRules.CatTrackMaxGrade &&
                  np.Mode == GradeProfileMode.FollowGround, "GR-24 params", np.Describe());
            Check(GradeParams.NarrowFullCut(topWidthM: 2f).TopWidthM == GradeParams.NarrowMinTopWidthM &&
                  GradeParams.NarrowFullCut(topWidthM: 40f).TopWidthM == GradeParams.NarrowMaxTopWidthM &&
                  GradeParams.NarrowFullCut(0.1f, 9f).MaxGrade == 0.1f && GradeParams.NarrowFullCut(0.1f, 9f).TopWidthM == 9f,
                  "GR-24 clamp", "8-12 m, max grade passed through");
            Check(!new GradeParams().NoFill && new GradeParams().Describe() == "max grade 25 %, top 20.0 m, cut 1:1.5, fill 1:2.0, follow ground",
                  "GR-24 default", new GradeParams().Describe());

            // (a) 50 % cross slope (ground rises with z), the path along x at z = 0.25 (off a texel center line): level along the track
            Func<float2, float> face50 = xz => 1000f + 0.5f * xz.y;
            float2[] along = Line(new float2(-200, 0.25f), new float2(200, 0.25f));
            var s = SolveNarrow(along, face50, np);
            GradeRaster r = s.Raster;
            float yb = 1000f + 0.5f * (0.25f - 5f);   // the bench: the lowest ground across the 10 m top (its downhill edge)
            Check(math.abs(s.Prof.Y[s.Path.Count / 2] - yb) < 0.02f, "GR-24 bench level", $"{s.Prof.Y[s.Path.Count / 2]:F3} vs {yb:F3}");
            Check(r.FillM3 == 0f && r.MaxFillM == 0f, "GR-24 no fill volume", r.Describe());
            int raised = 0;
            for (int t = 0; t < r.After.Length; t++)
                if (r.After[t] > r.Before[t] + 1e-4f)
                    raised++;
            Check(raised == 0, "GR-24 nothing raised", $"{raised} texel(s) above the ground");
            Check(r.UnfilledTopTexels == 0 && r.TopTexels > 0, "GR-24 whole top in cut", $"{r.UnfilledTopTexels} of {r.TopTexels} top texels unfilled");
            int ti = (int)math.round(Map.ToTexel(new float2(0, 0)).x);
            int bad = 0, tread = 0, bank = 0;
            float worstSlope = 0f;
            float prevH = float.NaN, prevD = float.NaN;
            for (int tj = r.Z0; tj < r.Z0 + r.H; tj++)
            {
                float2 c = Map.TexelCenter(ti, tj);
                float d = math.abs(c.y - 0.25f);
                float before = face50(c);
                float expect = d <= 5f ? math.min(before, yb) : (c.y > 0.25f ? math.min(before, yb + (d - 5f) / 1f) : before);
                float got = r.After[r.Index(ti, tj)];
                if (math.abs(got - expect) > 0.07f)
                    bad++;
                if (d <= 5f && math.abs(got - yb) < 0.07f)
                    tread++;
                // the cut bank (uphill, beyond the top, still below the ground): consecutive texels climb 1 m per m
                bool onBank = c.y > 0.25f && d > 5f && got < before - 0.1f;
                if (onBank)
                {
                    bank++;
                    if (!float.IsNaN(prevH) && math.abs(d - prevD - Map.Cell.y) < 1e-3f)
                        worstSlope = math.max(worstSlope, math.abs((got - prevH) / (d - prevD) - 1f));
                    prevH = got;
                    prevD = d;
                }
                else
                    prevH = float.NaN;
            }
            Check(bad == 0, "GR-24 cut template", $"{bad} texel(s) off the 10 m / 1:1 / no-fill template");
            Check(math.abs(tread * Map.Cell.y - np.TopWidthM) <= Map.Cell.y, "GR-24 tread width", $"{tread} texel(s) x {Map.Cell.y} m vs {np.TopWidthM} m");
            Check(bank >= 2 && worstSlope < 0.02f, "GR-24 bank 1:1", $"{bank} bank texel(s), worst slope error {worstSlope:F3}");

            // (b) solved against the CENTERLINE ground (a caller that skips LowEdgeGround): the downhill half of the top is not tread
            var ctr = Solve(along, face50, np);
            Check(ctr.Raster.FillM3 == 0f && ctr.Raster.UnfilledTopTexels > 0 && ctr.Raster.UnfilledTopTexels < ctr.Raster.TopTexels &&
                  ctr.Raster.MaxUnfilledM > 0.5f, "GR-24 unfilled counted", ctr.Raster.Describe());

            // (c) earthwork per meter on that face, narrow vs the default 20 m profile (both report cut + fill)
            float len = s.Path.LengthM;
            var def = Solve(along, face50, new GradeParams());
            float narrowPerM = (r.CutM3 + r.FillM3) / len, defPerM = (def.Raster.CutM3 + def.Raster.FillM3) / len;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "INFO  GR-24 50 % face, {0:F0} m: narrow cut {1:F1} m3/m fill {2:F1} m3/m; default 20 m cut {3:F1} m3/m fill {4:F1} m3/m",
                len, r.CutM3 / len, r.FillM3 / len, def.Raster.CutM3 / len, def.Raster.FillM3 / len));
            // analytic section: tread 10 m under a 50 % face = 25 m2 (0..5 m deep), bank 1:1 meets the face 10 m out = 25 m2 more
            Check(math.abs(narrowPerM - 50f) < 7f, "GR-24 cost per m", $"{narrowPerM:F1} m3/m vs 50 analytic");

            // (d) the grade limit holds on the bench: a 12 % climb with bumps along the track, and a 40 % climb that cannot
            Func<float2, float> climb = xz => 1000f + 0.5f * xz.y + 0.12f * xz.x + 3f * math.sin(xz.x / 25f);
            var g1 = SolveNarrow(Line(new float2(0, 0.25f), new float2(600, 0.25f)), climb, np);
            Check(g1.Prof.Feasible && g1.Prof.MaxGradeAbs <= 0.25f + 1e-3f, "GR-24 grade limit", $"{g1.Prof.MaxGradeAbs:P1}");
            int above = 0;
            for (int i = 0; i < g1.Path.Count; i++)
                if (g1.Prof.Y[i] > g1.Prof.Ground[i] + 1e-3f)
                    above++;
            Check(above == 0, "GR-24 bench at or below the low edge", $"{above} sample(s) above");
            float worstSurf = 0f;
            for (int i = 0; i < g1.Path.Count; i++)
                worstSurf = math.max(worstSurf, math.abs(g1.Raster.SampleAt(Map, g1.Raster.After, g1.Path.Xz[i]) - g1.Prof.Y[i]));
            Check(worstSurf < 0.35f, "GR-24 surface = bench", $"worst centerline deviation {worstSurf:F2} m");
            Check(g1.Raster.FillM3 == 0f, "GR-24 climb no fill", g1.Raster.Describe());
            var steep = SolveNarrow(Line(new float2(0, 0.25f), new float2(200, 0.25f)), xz => 900f + 0.5f * xz.y + 0.40f * xz.x, np);
            Check(!steep.Prof.Feasible && steep.Prof.ShortfallM > 0f, "GR-24 steep refused", $"shortfall {steep.Prof.ShortfallM:F1} m");

            // (e) a hump on a level track (the default fills the hollows): cut only, still within the limit
            Func<float2, float> hump = xz => 100f + 6f * math.exp(-xz.x * xz.x / 2500f) - 5f * math.exp(-(xz.x - 80f) * (xz.x - 80f) / 400f);
            var hp = SolveNarrow(Line(new float2(-150, 0.25f), new float2(150, 0.25f)), hump, GradeParams.NarrowFullCut(0.10f));
            Check(hp.Prof.Feasible && hp.Prof.MaxGradeAbs <= 0.10f + 1e-3f && hp.Raster.FillM3 == 0f && hp.Prof.MaxFillM <= 1e-3f, "GR-24 hump", hp.Raster.Describe());

            // (f) across a crossed run nothing is raised either (the inside correction is cut-only)
            float2[] runA = { new float2(-30, -150), new float2(30, -150), new float2(30, 150), new float2(-30, 150) };
            var keepA = new List<float2[]> { runA };
            Func<float2, float> bump = xz => 1000f + 0.15f * xz.y + 1.5f * math.max(0f, 1f - math.abs(xz.x) / 4f);
            var kp = SolveNarrow(Line(new float2(-150, 0.25f), new float2(150, 0.25f)), bump, np, keepA);
            int raisedK = 0;
            for (int t = 0; t < kp.Raster.After.Length; t++)
                if (kp.Raster.After[t] > kp.Raster.Before[t] + 1e-4f)
                    raisedK++;
            Check(raisedK == 0 && kp.Raster.FillM3 == 0f, "GR-24 crossing no fill", $"{raisedK} raised; {kp.Raster.Describe()}");

            // (g) time: the 1.5 km mountain track
            Func<float2, float> mountain = xz => 1500f + 0.28f * xz.x + 6f * math.sin(xz.y / 40f) + 3f * math.cos(xz.x / 23f);
            var pts = new List<float2>();
            for (int i = 0; i <= 8; i++)
                pts.Add(new float2(i * 120f, (i % 2 == 0 ? 0f : 140f)));
            var sw = Stopwatch.StartNew();
            var m = SolveNarrow(pts, mountain, np);
            sw.Stop();
            Console.WriteLine($"INFO  GR-24 mountain {m.Path.LengthM:F0} m narrow: {sw.Elapsed.TotalMilliseconds:F0} ms; {m.Raster.Describe()}");
            Check(sw.Elapsed.TotalMilliseconds < 3000 && m.Raster.FillM3 == 0f, "GR-24 time", $"{sw.Elapsed.TotalMilliseconds:F0} ms");
        }

        private static float Area(float2[] ring)
        {
            float a = 0f;
            for (int i = 0; i < ring.Length; i++)
            {
                float2 p = ring[i], q = ring[(i + 1) % ring.Length];
                a += p.x * q.y - q.x * p.y;
            }
            return math.abs(0.5f * a);
        }

        private static bool Inside(float2[] ring, float2 pt)
        {
            bool inside = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                if ((ring[i].y > pt.y) != (ring[j].y > pt.y) &&
                    pt.x < (ring[j].x - ring[i].x) * (pt.y - ring[i].y) / (ring[j].y - ring[i].y) + ring[i].x)
                    inside = !inside;
            }
            return inside;
        }
    }
}
