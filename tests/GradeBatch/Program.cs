// GradeBatch — headless tests for the PURE half of the Cat Track Grading tool (T139b; DECISIONS 362 / 363; build note
// docs/research/field/2026-10-20/grading-tool-build.md): CatTrackGrade.cs. GR-1..GR-16.
//   GR-1  the path: the Bezier chain passes every clicked point, equal sample steps, ends exact, length >= the chords.
//   GR-2  flat ground: the profile is the ground, nothing changes.
//   GR-3  a 15 % slope under a 25 % limit: follow ground keeps the ground (no earthwork), grade within the limit.
//   GR-4  a 40 % straight climb: infeasible, ShortfallM = dh / G - L, the overlay class goes red; ends still pinned.
//   GR-5  hump and hollow: grade within the limit everywhere, both ends meet the ground, the hump is cut and the hollow filled.
//   GR-6  switchback-free long climb: a diagonal traverse of a 30 % mountain (path grade 30 % x cos 45 = 21 %) is feasible with no
//         shortfall; the same height gain straight up the fall line is not.
//   GR-7  constant grade: a straight ramp between the ends; infeasible past the limit.
//   GR-8  side slopes: a cut through a 5 m mound is 1 : 1.5, a fill across a 5 m ditch is 1 : 2, the flat top is TopWidthM wide,
//         and outside the slopes the ground is untouched.
//   GR-9  sidehill steeper than the fill slope: the bank blends into the ground (counted), never a vertical step at the reach.
//   GR-10 guards: a lot, a lift line or water under the corridor refuses with its kind; the same obstacles clear of it do not.
//   GR-11 undo: the pre-image comes back bit-exact; a texel a foreign edit changed since keeps that edit.
//   GR-12 undo stack: LIFO, capped at MaxEntries, the newest always kept.
//   GR-13 switchback legs: a texel takes its NEAREST leg's template (two legs never stack).
//   GR-14 map encoding: texel center <-> continuous texel, height <-> u16 within half a step.
//   GR-15 the edit model + input rules: add / insert / move / undo; the right-click table (cancel drag > undo edit > undo grade
//         > close); the prompt is never empty, differs per step, names right click / Enter / Esc, US English.
//   GR-16 cost: a 1.5 km winding path solves and rasterizes (time printed; FAIL above 3 s).
// Run (from the ModularSkiResort root):
//   dotnet run --project tests/GradeBatch/GradeBatch.csproj -c Release
// Prints `FAIL  where: msg` lines and one SUMMARY line; Environment.ExitCode = failures (the LiftBatch convention).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
            SwitchbackLegs();
            Encoding();
            EditAndInput();
            Cost();
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

        private static Solved Solve(IList<float2> pts, Func<float2, float> ground, GradeParams p, bool raster = true)
        {
            var s = new Solved { Path = GradePath.Build(pts, p.SampleStepM) };
            var g = new float[s.Path.Count];
            for (int i = 0; i < g.Length; i++)
                g[i] = ground(s.Path.Xz[i]);
            s.Prof = GradeProfile.Solve(s.Path, g, p);
            if (raster && GradeRaster.Bounds(Map, s.Path, s.Prof, p, out int4 rect, out float reach))
            {
                s.Reach = reach;
                s.Raster = GradeRaster.Build(Map, rect, s.Path, s.Prof, p, reach, (i, j) => ground(Map.TexelCenter(i, j)));
            }
            return s;
        }

        private static float2[] Line(float2 a, float2 b) => new[] { a, b };

        // ── GR-1 ──────────────────────────────────────────────────────────────────────────────────────

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

        // ── GR-2 .. GR-7: profiles ────────────────────────────────────────────────────────────────────

        private static void FlatGround()
        {
            var s = Solve(Line(new float2(0, 0), new float2(300, 50)), _ => 812.4f, new GradeParams());
            Check(s.Prof.Feasible && s.Prof.MaxGradeAbs < 1e-4f, "GR-2 grade", $"{s.Prof.MaxGradeAbs}");
            Check(s.Raster.Changed == 0, "GR-2 raster", $"{s.Raster.Changed} texels changed on flat ground");
        }

        private static void GentleSlope()
        {
            Func<float2, float> ground = xz => 900f + 0.15f * xz.x;
            var s = Solve(Line(new float2(0, 0), new float2(400, 0)), ground, new GradeParams());
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
            Check(text.Contains("needs") && text.Contains("switchback"), "GR-4 text", text);
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

        // ── GR-8 / GR-9: cross-section ────────────────────────────────────────────────────────────────

        private static void SideSlopes()
        {
            var p = new GradeParams { Mode = GradeProfileMode.ConstantGrade };
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

        // ── GR-10 guards ──────────────────────────────────────────────────────────────────────────────

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

            foreach (GradeGuardKind g in new[] { GradeGuardKind.Lot, GradeGuardKind.LiftLine, GradeGuardKind.Water, GradeGuardKind.OffMap })
                Check(GradeGuards.Reason(g).Length > 10, "GR-10 reason", g.ToString());
        }

        // ── GR-11 / GR-12 undo ────────────────────────────────────────────────────────────────────────

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
            int half = GradeUndo.MaxBytes / 2 / 2;   // ushorts in half the budget
            big.Push(new GradeUndoEntry { Before = new ushort[half], After = new ushort[half], Label = "a" });
            big.Push(new GradeUndoEntry { Before = new ushort[half], After = new ushort[half], Label = "b" });
            Check(big.Count == 1 && big.Peek().Label == "b", "GR-12 bytes", "over the byte cap the oldest goes, the newest stays");
            Check(new GradeUndo().Pop() == null, "GR-12 empty", "pop on empty = null");
        }

        // ── GR-13 switchback legs ─────────────────────────────────────────────────────────────────────

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

        // ── GR-14 encoding ────────────────────────────────────────────────────────────────────────────

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

        // ── GR-15 edit model + input ──────────────────────────────────────────────────────────────────

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

            // Right click table.
            Check(GradeInput.RightClick(GradeStep.Path, true, 3, 0) == GradeBack.CancelDrag, "GR-15 rc drag", "");
            Check(GradeInput.RightClick(GradeStep.Path, false, 3, 1) == GradeBack.UndoEdit, "GR-15 rc edit", "");
            Check(GradeInput.RightClick(GradeStep.OnePoint, false, 1, 0) == GradeBack.UndoEdit, "GR-15 rc one point", "drops the point");
            Check(GradeInput.RightClick(GradeStep.Empty, false, 0, 1) == GradeBack.UndoGrade, "GR-15 rc grade", "");
            Check(GradeInput.RightClick(GradeStep.Empty, false, 0, 0) == GradeBack.CloseTool, "GR-15 rc close", "");
            Check(GradeInput.RightClick(GradeStep.Applying, false, 2, 1) == GradeBack.None, "GR-15 rc applying", "");

            // Walk back: three points + one grade -> 3 undos, undo the grade, close.
            var w = new GradeEdit();
            w.Add(new float2(0, 0));
            w.Add(new float2(50, 0));
            w.Add(new float2(90, 10));
            int grades = 1;
            var seq = new List<GradeBack>();
            for (int guard = 0; guard < 10; guard++)
            {
                GradeBack b = GradeInput.RightClick(GradeInput.StepOf(w.Count, false), false, w.UndoDepth, grades);
                seq.Add(b);
                if (b == GradeBack.UndoEdit)
                    w.Undo();
                else if (b == GradeBack.UndoGrade)
                    grades--;
                else
                    break;
            }
            Check(string.Join(",", seq) == "UndoEdit,UndoEdit,UndoEdit,UndoGrade,CloseTool", "GR-15 walk back", string.Join(",", seq));

            // Prompts.
            var seen = new HashSet<string>();
            foreach (GradeStep st in new[] { GradeStep.Empty, GradeStep.OnePoint, GradeStep.Path, GradeStep.Applying })
            {
                foreach (bool canUndo in new[] { false, true })
                {
                    string pr = GradeInput.Prompt(st, false, canUndo);
                    Check(!string.IsNullOrWhiteSpace(pr), "GR-15 prompt", $"{st} empty");
                    seen.Add(pr);
                    string low = pr.ToLowerInvariant();
                    foreach (string brit in new[] { "colour", "metre", "centre", "grey" })
                        Check(!low.Contains(brit), "GR-15 US English", $"{st}: {brit}");
                }
            }
            Check(seen.Count >= 5, "GR-15 prompt per step", $"{seen.Count} distinct");
            Check(GradeInput.Prompt(GradeStep.Path, false, false).Contains("Enter") && GradeInput.Prompt(GradeStep.Path, false, false).Contains("Esc") &&
                  GradeInput.Prompt(GradeStep.Path, false, false).Contains("right click"), "GR-15 keys named", "");
            Check(GradeInput.Prompt(GradeStep.Empty, false, true).Contains("undo the last grade"), "GR-15 undo named", "");
            Check(GradeInput.Prompt(GradeStep.Path, true, false).StartsWith("Release"), "GR-15 drag prompt", "");
        }

        // ── GR-16 cost ────────────────────────────────────────────────────────────────────────────────

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
    }
}
