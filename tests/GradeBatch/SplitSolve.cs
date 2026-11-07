// GR-31 (2026-11-05, DECISIONS 601 f, hand-test note 20 "massive hitches"): the tool's solve is split. The quick part (path, ground, rect,
// profile) runs on every edit. The full part (GradeSolver.Run: raster, crossed-run band rating, guards, along-track check, outline) runs
// debounced on a worker from a CROPPED copy of the heights. For Enter it runs inline. This file proves what the split must not change:
//   (a) the full solve on the tool's crop of the terrain (the rect plus the entered runs plus the planner's lattice margin) equals the
//       one-shot solve of before the split on a roomy copy. It has the same guard, the same inside allowance, the same raster arrays,
//       the same outline and the same profile. Also, the crop is never read outside itself (the miss counter the tool falls back on stays 0).
//   (b) the quick profile is the final profile where no crossed run changes the allowance.
//   (c) a cancel request ends the job at its next stage boundary and marks the result.
//   (d) the main-thread part (quick) of a 30-point track stays under one 33 ms frame, with the before/after numbers printed.
// The engine halves (the snapshot copy, the water layer, the debounce, the worker) are checked in game: the tool logs one
// "Grade [cost]" line per solve.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace GradeBatch
{
    internal static partial class Program
    {
        // The heightmap of the tool's CPU heights (TerrainHeightData): 4096 texels over the 14,336 m map, 16 raw units per meter.
        private static readonly float3 HdScale = new float3(4096f / 14336f, 16f, 4096f / 14336f);
        private static readonly float3 HdOffset = new float3(7168f, 0f, 7168f);

        /// <summary>A copy of the terrain over a world box grown by <paramref name="padTexels"/>: SkiRunAutoPlanner.SnapshotRect's rectangle.</summary>
        private static HeightSnapshot Crop(Func<float2, float> terrain, float2 lo, float2 hi, int padTexels)
        {
            int k0 = Math.Max(0, (int)Math.Floor((lo.x + HdOffset.x) * HdScale.x) - padTexels);
            int k1 = Math.Min(4095, (int)Math.Ceiling((hi.x + HdOffset.x) * HdScale.x) + padTexels);
            int l0 = Math.Max(0, (int)Math.Floor((lo.y + HdOffset.z) * HdScale.z) - padTexels);
            int l1 = Math.Min(4095, (int)Math.Ceiling((hi.y + HdOffset.z) * HdScale.z) + padTexels);
            HeightSnapshot s = HeightSnapshot.Allocate(k0, l0, k1 - k0 + 1, l1 - l0 + 1, 4096, 4096, HdScale, HdOffset, wet: false);
            for (int l = 0; l < s.Depth; l++)
            {
                for (int k = 0; k < s.Width; k++)
                {
                    float x = (k0 + k) / HdScale.x - HdOffset.x, z = (l0 + l) / HdScale.z - HdOffset.z;
                    s.Heights[l * s.Width + k] = (ushort)math.clamp(math.round(terrain(new float2(x, z)) * HdScale.y), 0f, 65535f);
                }
            }
            return s;
        }

        private sealed class MissCounter
        {
            public HeightSnapshot Snap;
            public int Misses;

            public float Sample(float3 w)
            {
                float h = Snap.Sample(w);
                if (float.IsNaN(h))
                {
                    Misses++;
                    return 0f;
                }
                return h;
            }
        }

        private sealed class SplitCase
        {
            public string Name;
            public List<float2> Points;
            public List<float2[]> Runs = new List<float2[]>();
            public List<GradeObstacles.Lot> Lots = new List<GradeObstacles.Lot>();
            public Func<float2, bool> Wet;
            public GradeParams P = new GradeParams();
        }

        private static List<float2> Winding(int n, float stepX, float amp)
        {
            var pts = new List<float2>();
            for (int i = 0; i < n; i++)
                pts.Add(new float2(i * stepX, amp * math.sin(i * 0.55f)));
            return pts;
        }

        /// <summary>The tool's quick solve (SolveQuick) on a terrain copy: path, ground, rect, profile. Null when off the map.</summary>
        private static SplitQuick Quick(SplitCase c, HeightSnapshot roomy)
        {
            var q = new SplitQuick { Case = c };
            q.Path = GradePath.Build(c.Points, c.P.SampleStepM);
            q.Ground = new float[q.Path.Count];
            for (int i = 0; i < q.Ground.Length; i++)
                q.Ground[i] = roomy.Sample(new float3(q.Path.Xz[i].x, 0f, q.Path.Xz[i].y));
            q.Obstacles = new GradeObstacles();
            q.Obstacles.Runs.AddRange(c.Runs);
            q.Obstacles.Lots.AddRange(c.Lots);
            if (!GradeRaster.Bounds(Map, q.Path, null, c.P, out q.Rect, out q.Reach) || GradeRaster.TooLarge(q.Rect))
                return null;
            q.Prof = GradeSolver.QuickProfile(q.Path, q.Ground, c.P, q.Obstacles.Runs);
            return q;
        }

        private sealed class SplitQuick
        {
            public SplitCase Case;
            public GradePath Path;
            public float[] Ground;
            public int4 Rect;
            public float Reach;
            public GradeObstacles Obstacles;
            public GradeProfile Prof;
        }

        /// <summary>The tool's crop: the rect's world box and every entered run's outline, plus SkiRunAutoPlanner.SnapshotRect's pad
        /// (LatticeMarginM 40 m in texels, + 3).</summary>
        private static HeightSnapshot ToolCrop(SplitQuick q, Func<float2, float> terrain)
        {
            Colossal_Bounds wb = WorldBox(q.Rect);
            float2 lo = wb.Min, hi = wb.Max;
            bool[] entered = GradeRaster.EnteredOutlines(q.Path, q.Obstacles.Runs);
            for (int k = 0; entered != null && k < entered.Length; k++)
            {
                if (!entered[k])
                    continue;
                foreach (float2 v in q.Obstacles.Runs[k])
                {
                    lo = math.min(lo, v);
                    hi = math.max(hi, v);
                }
            }
            float texel = 1f / HdScale.x;
            return Crop(terrain, lo, hi, (int)Math.Ceiling(40f / texel) + 3);
        }

        private struct Colossal_Bounds
        {
            public float2 Min, Max;
        }

        private static Colossal_Bounds WorldBox(int4 rect)
        {
            float2 c = Map.Cell;
            return new Colossal_Bounds
            {
                Min = Map.Offset + new float2(rect.x, rect.y) * c,
                Max = Map.Offset + new float2(rect.x + rect.z, rect.y + rect.w) * c,
            };
        }

        private static GradeSolveInput InputOf(SplitQuick q, Func<float3, float> height, Func<int, int, bool> wet, HeightSnapshot rateOf)
        {
            // each solve owns its obstacles (the worker fills WetTexels)
            var obs = new GradeObstacles();
            obs.Runs.AddRange(q.Obstacles.Runs);
            obs.Lots.AddRange(q.Obstacles.Lots);
            obs.Lines.AddRange(q.Obstacles.Lines);
            return new GradeSolveInput
            {
                Map = Map, Path = q.Path, Ground = q.Ground, Params = q.Case.P, Rect = q.Rect, Reach = q.Reach, Obstacles = obs,
                Height = height, IsWet = wet, RateOrigin = rateOf.Origin, RateTexel = rateOf.TexelM,
            };
        }

        /// <summary>The solve as it was before the split (CatTrackGradeToolSystem.Solve with settle: true, DECISIONS 601 f), kept here as the
        /// golden copy: BuildAt / ChooseInsideAllowance, then CheckGuards, CrossingSteep, the band guard and the outline, written out
        /// stage by stage as the one-shot did.</summary>
        private static GradeSolveResult OneShotBeforeTheSplit(GradeSolveInput inp)
        {
            var res = new GradeSolveResult();
            GradeMap map = inp.Map;
            GradePath path = inp.Path;
            GradeParams p = inp.Params;
            var runs = inp.Obstacles.Runs;
            Func<int, int, float> baseHeight = (i, j) =>
            {
                float2 c = map.TexelCenter(i, j);
                return inp.Height(new float3(c.x, 0f, c.y));
            };
            bool[] entered = GradeRaster.EnteredOutlines(path, runs);
            bool crossing = false;
            for (int k = 0; entered != null && k < entered.Length; k++)
                crossing |= entered[k];
            sbyte[] bandsBefore = null;
            string bandText = null;
            var steps = new System.Text.StringBuilder();
            GradeParams pa = p;
            GradeProfile prof = null;
            GradeRaster raster = null;
            sbyte[] Rate(bool after)
            {
                var bands = new sbyte[runs.Count];
                var sampler = new GradeRaster.Sampler { Map = map, R = raster, After = after, Outside = inp.Height };
                for (int k = 0; k < bands.Length; k++)
                {
                    bands[k] = -3;
                    float2[] poly = runs[k];
                    if (entered == null || !entered[k] || poly == null || poly.Length < 3)
                        continue;
                    var nodes = new float3[poly.Length];
                    for (int i = 0; i < poly.Length; i++)
                        nodes[i] = new float3(poly[i].x, sampler.Sample(new float3(poly[i].x, 0f, poly[i].y)), poly[i].y);
                    var result = new SlopeResult();
                    RateStatus st = SkiRunRater.Rate(nodes, nodes.Length, inp.RateOrigin, ref sampler, inp.RateTexel, 0, new SlopeGrid(), null, result,
                        out int _, out float _, out float _);
                    if (st == RateStatus.Rated)
                        bands[k] = (sbyte)result.Band;
                }
                return bands;
            }
            float chosen = GradeRaster.ChooseInsideAllowance(a =>
            {
                pa = p.Clone();
                pa.InsideMaxM = a;
                prof = GradeProfile.Solve(path, inp.Ground, pa, GradeRaster.PinTolerance(path, runs, pa));
                raster = GradeRaster.Build(map, inp.Rect, path, prof, pa, inp.Reach, baseHeight, runs);
                if (!crossing)
                    return true;
                if (bandsBefore == null)
                    bandsBefore = Rate(false);
                bool kept = true;
                sbyte[] after = Rate(true);
                var sb = new System.Text.StringBuilder();
                for (int k = 0; k < bandsBefore.Length; k++)
                {
                    if (bandsBefore[k] == -3 || after[k] == -3)
                        continue;
                    sb.Append(sb.Length > 0 ? ", " : string.Empty).Append("run ").Append(k).Append(' ').Append((SlopeBand)bandsBefore[k]);
                    if (bandsBefore[k] != after[k])
                    {
                        kept = false;
                        sb.Append(" -> ").Append((SlopeBand)after[k]);
                    }
                    else
                        sb.Append(" kept");
                }
                bandText = sb.Length > 0 ? "crossed runs: " + sb : "no crossed run rated";
                steps.Append(steps.Length > 0 ? "; " : string.Empty).AppendFormat(CultureInfo.InvariantCulture, "{0:F1} m {1} {2:F0} ms", a, kept ? "kept" : "changed", 1.0);
                return kept;
            });
            res.Allowance = chosen;
            res.BandText = bandText;
            res.StepText = steps.ToString();
            res.Prof = prof;
            res.Raster = raster;
            res.PlanParams = pa;
            res.Crossing = crossing;
            inp.Obstacles.WetTexels.Clear();
            for (int j = 0; j < raster.H; j++)
                for (int i = 0; i < raster.W; i++)
                    if (raster.Zone[j * raster.W + i] != 0 && inp.IsWet != null && inp.IsWet(raster.X0 + i, raster.Z0 + j))
                        inp.Obstacles.WetTexels.Add(new int2(raster.X0 + i, raster.Z0 + j));
            res.Guard = GradeGuards.Check(map, raster, inp.Obstacles, out res.GuardAt);
            if (res.Guard == GradeGuardKind.None && runs.Count > 0)
            {
                int s = raster.AlongTrackSteepSample(map, path, runs, p.CrossBlendM, p.MaxGrade);
                if (s >= 0)
                {
                    res.Guard = GradeGuardKind.CrossingSteep;
                    res.GuardAt = path.Xz[s];
                }
            }
            if (res.Guard == GradeGuardKind.None && chosen < 0f)
            {
                res.Guard = GradeGuardKind.CrossedRunBand;
                res.GuardAt = path.Xz[path.Count / 2];
            }
            if (res.Guard == GradeGuardKind.None)
            {
                res.Ring = GradeRibbon.Build(path, 0.5f * p.TopWidthM, out GradeRibbonFail fail, out float2 at);
                if (fail != GradeRibbonFail.None)
                {
                    res.Ring = null;
                    res.Guard = GradeGuardKind.RunShape;
                    res.GuardAt = at;
                }
            }
            return res;
        }

        private static string SameResult(GradeSolveResult a, GradeSolveResult b)
        {
            if (a.Guard != b.Guard) return $"guard {a.Guard} vs {b.Guard}";
            if (!a.GuardAt.Equals(b.GuardAt)) return $"guard at {a.GuardAt} vs {b.GuardAt}";
            if (a.Allowance != b.Allowance) return $"allowance {a.Allowance} vs {b.Allowance}";
            if (a.PlanParams.InsideMaxM != b.PlanParams.InsideMaxM) return "plan params";
            if (a.BandText != b.BandText) return $"bands '{a.BandText}' vs '{b.BandText}'";
            string sa = Regex.Replace(a.StepText, "\\d+ ms", "N ms"), sb = Regex.Replace(b.StepText, "\\d+ ms", "N ms");
            if (sa != sb) return $"steps '{sa}' vs '{sb}'";
            if (a.Prof.Y.Length != b.Prof.Y.Length) return "profile length";
            for (int i = 0; i < a.Prof.Y.Length; i++)
                if (a.Prof.Y[i] != b.Prof.Y[i]) return $"profile Y[{i}] {a.Prof.Y[i]} vs {b.Prof.Y[i]}";
            GradeRaster ra = a.Raster, rb = b.Raster;
            if (ra.X0 != rb.X0 || ra.Z0 != rb.Z0 || ra.W != rb.W || ra.H != rb.H) return "raster rect";
            for (int t = 0; t < ra.Zone.Length; t++)
            {
                if (ra.Zone[t] != rb.Zone[t]) return $"zone[{t}]";
                if (ra.Before[t] != rb.Before[t]) return $"before[{t}] {ra.Before[t]} vs {rb.Before[t]}";
                if (ra.After[t] != rb.After[t]) return $"after[{t}] {ra.After[t]} vs {rb.After[t]}";
            }
            if (ra.Changed != rb.Changed || ra.CutM3 != rb.CutM3 || ra.FillM3 != rb.FillM3) return "raster totals";
            if ((a.Ring == null) != (b.Ring == null)) return "ring presence";
            if (a.Ring != null)
            {
                if (a.Ring.Length != b.Ring.Length) return "ring length";
                for (int i = 0; i < a.Ring.Length; i++)
                    if (!a.Ring[i].Equals(b.Ring[i])) return $"ring[{i}]";
            }
            return null;
        }

        private static void SplitSolve()
        {
            const string W = "GR-31";
            Func<float2, float> mountain = xz => 1500f + 0.28f * xz.x + 6f * math.sin(xz.y / 40f) + 3f * math.cos(xz.x / 23f);
            var cases = new List<SplitCase>();
            cases.Add(new SplitCase { Name = "30 points, open ground", Points = Winding(30, 40f, 70f) });
            cases.Add(new SplitCase { Name = "4 points, open ground", Points = Winding(4, 120f, 60f) });
            cases.Add(new SplitCase { Name = "30 points, long open track (3.2 km)", Points = Winding(30, 110f, 200f) });
            var crossed = new SplitCase { Name = "30 points, two crossed runs", Points = Winding(30, 40f, 70f) };
            crossed.Runs.Add(new[] { new float2(300, -150), new float2(360, -150), new float2(360, 150), new float2(300, 150) });
            crossed.Runs.Add(new[] { new float2(800, -150), new float2(860, -150), new float2(860, 150), new float2(800, 150) });
            cases.Add(crossed);
            var lot = new SplitCase { Name = "12 points, a lot on the track (guard Lot)", Points = Winding(12, 60f, 50f) };
            float2 mid = lot.Points[6];
            lot.Lots.Add(new GradeObstacles.Lot { Center = mid, Right = new float2(1, 0), Forward = new float2(0, 1), Half = new float2(16, 16) });
            cases.Add(lot);
            var wet = new SplitCase { Name = "12 points, water across the track (guard Water)", Points = Winding(12, 60f, 50f) };
            wet.Wet = xz => xz.x > 300f && xz.x < 330f;
            cases.Add(wet);

            foreach (SplitCase c in cases)
            {
                HeightSnapshot roomy = Crop(mountain, new float2(-800, -800), new float2(4000, 800), 200);
                SplitQuick q = Quick(c, roomy);
                if (!Check(q != null, W + " setup", c.Name))
                    continue;
                HeightSnapshot crop = ToolCrop(q, mountain);
                var miss = new MissCounter { Snap = crop };
                Func<int, int, bool> wetFn = c.Wet == null ? null : (Func<int, int, bool>)((i, j) => c.Wet(Map.TexelCenter(i, j)));

                var swOld = Stopwatch.StartNew();
                GradeSolveResult old = OneShotBeforeTheSplit(InputOf(q, w => roomy.Sample(w), wetFn, roomy));
                swOld.Stop();
                var swNew = Stopwatch.StartNew();
                GradeSolveResult now = GradeSolver.Run(InputOf(q, miss.Sample, wetFn, crop));
                swNew.Stop();

                string diff = SameResult(old, now);
                Check(diff == null, W + " equals the one-shot", $"{c.Name}: {diff}");
                Check(miss.Misses == 0, W + " crop covers every read", $"{c.Name}: {miss.Misses} read(s) outside the crop ({crop.Width} x {crop.Depth} texels)");
                if (c.Name.Contains("two crossed"))
                {
                    // the miss counter the tool falls back on really fires: the rating reads a crop with no margin outside itself
                    HeightSnapshot tight = Crop(mountain, WorldBox(q.Rect).Min, WorldBox(q.Rect).Max, 0);
                    var missT = new MissCounter { Snap = tight };
                    GradeSolver.Run(InputOf(q, missT.Sample, wetFn, tight));
                    Check(missT.Misses > 0, W + " tight crop is caught", $"{missT.Misses} read(s) outside a crop with no margin");
                }
                if (c.Name.Contains("lot"))
                    Check(now.Guard == GradeGuardKind.Lot, W + " lot guard", now.Guard.ToString());
                if (c.Name.Contains("water"))
                    Check(now.Guard == GradeGuardKind.Water, W + " water guard", now.Guard.ToString());
                if (c.Name.Contains("open"))
                    Check(now.Guard == GradeGuardKind.None && now.Ring != null, W + " open ground", $"guard {now.Guard}");
                if (!now.Crossing)
                    Check(now.Allowance == GradeParams.InsideAllowanceSteps[0] && SameY(q.Prof, now.Prof), W + " quick profile is the final one",
                          $"{c.Name}: allowance {now.Allowance}");
                else
                    Check(now.Raster.KeptTexels + now.Raster.InsideTexels > 0, W + " crossing seen", now.Raster.Describe());

                // The main-thread part, measured: path + ground + rect + profile (the tool adds the obstacle search, ~1 ms in game)
                var swQ = Stopwatch.StartNew();
                for (int r = 0; r < 20; r++)
                    Quick(c, roomy);
                swQ.Stop();
                double quickMs = swQ.Elapsed.TotalMilliseconds / 20.0;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "INFO  GR-31 {0}: rect {1} x {2} = {3} texels, snapshot {4} x {5}; BEFORE the split every edit costs one-shot {6:F1} ms on the main thread; " +
                    "AFTER: quick {7:F2} ms per edit on the main thread, full solve {8:F1} ms on the worker (raster {9:F1}, guards {10:F1}, outline {11:F1}); guard {12}",
                    c.Name, q.Rect.z, q.Rect.w, (long)q.Rect.z * q.Rect.w, crop.Width, crop.Depth, swOld.Elapsed.TotalMilliseconds, quickMs,
                    swNew.Elapsed.TotalMilliseconds, now.RasterMs, now.GuardMs, now.RingMs, now.Guard));
                if (c.Points.Count >= 30)
                    Check(quickMs < 33.0, W + " quick part under a frame", $"{c.Name}: {quickMs:F1} ms");
            }

            // (e) the gate: what runs on every edit never grows with the rect (hand-test note 20). SolveQuick may not build a raster, run the
            // full solve or wait for the worker. Only the debounce (UpdateFull / LaunchFull) and Enter (FinishFullNow) may.
            {
                string tool = RepoFile("mod/SkiResortMod/Systems/CatTrackGradeToolSystem.cs");
                if (Check(tool != null, W + " gate", "tool source not found"))
                {
                    string src = File.ReadAllText(tool);
                    int b0 = src.IndexOf("private void SolveQuick(", StringComparison.Ordinal);
                    int b1 = src.IndexOf("/// <summary>Every frame in the Edit phase", StringComparison.Ordinal);
                    Check(b0 > 0 && b1 > b0, W + " gate", "SolveQuick body not found");
                    string body = b0 > 0 && b1 > b0 ? src.Substring(b0, b1 - b0) : string.Empty;
                    foreach (string banned in new[] { "GradeRaster.Build(", "GradeSolver.Run(", "RunInline(", "FinishFullNow(", "CheckGuards(", "RateCrossed(", "TryBuildInput(", ".Wait(" })
                        Check(!body.Contains(banned), W + " SolveQuick stays quick", "SolveQuick calls " + banned + " (a per-edit cost that grows with the rect, DECISIONS 601 f)");
                    int e0 = src.IndexOf("if (m_Dirty && !Dragging)", StringComparison.Ordinal);
                    Check(e0 > 0 && src.IndexOf("SolveQuick(\"edit\")", e0, StringComparison.Ordinal) > 0 && src.IndexOf("RunInline(", e0, 400, StringComparison.Ordinal) < 0,
                          W + " edit path", "the per-edit branch of UpdateCore calls SolveQuick only");
                }
            }

            // (c) a cancel request ends the job at a stage boundary
            {
                var c = cases[0];
                HeightSnapshot roomy = Crop(mountain, new float2(-800, -800), new float2(4000, 800), 200);
                SplitQuick q = Quick(c, roomy);
                var cancel = new GradeCancel();
                cancel.Request();
                GradeSolveInput inp = InputOf(q, w => roomy.Sample(w), null, roomy);
                inp.Cancel = cancel;
                var sw = Stopwatch.StartNew();
                GradeSolveResult r = GradeSolver.Run(inp);
                sw.Stop();
                Check(r.Canceled, W + " cancel", "a request before the run marks the result canceled");
                var inp2 = InputOf(q, w => roomy.Sample(w), null, roomy);
                inp2.Cancel = new GradeCancel();
                Check(!GradeSolver.Run(inp2).Canceled, W + " no cancel", "an unrequested token cancels nothing");
            }
        }

        private static bool SameY(GradeProfile a, GradeProfile b)
        {
            if (a.Y.Length != b.Y.Length)
                return false;
            for (int i = 0; i < a.Y.Length; i++)
                if (a.Y[i] != b.Y[i])
                    return false;
            return true;
        }
    }
}
