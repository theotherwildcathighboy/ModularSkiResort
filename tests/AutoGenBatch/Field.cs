// `dotnet run ... -- field`: the field-mirror diagnostic (field test 2026-10-26b, a real alpine map). Runs Terrain.Alpine at seeds 1-3
// on both field regions (the ~3.9 km2 bowl and the ~1.1 km2 lower-middle slope), densities Normal and Dense, mix Balanced, and prints
// per scan the counts, the full reject histogram, runs per km2 of open ground, run length / width / drop quartiles, base and merge
// shares, the per-phase Select maximum and the longest unit (CoreCLR, sliced at the tool's 4 ms budget), then one line per run.
// No checks there; FieldTests (the normal run) checks the same fixtures. AUTOGEN_SVG=<folder> writes a PNG per scan.

using System;
using System.Collections.Generic;
using System.Globalization;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class Program
    {
        private static void FieldDiag()
        {
            string dir = Environment.GetEnvironmentVariable("AUTOGEN_SVG");
            // JIT warm-up: three rounds of both densities, so tiered compilation has promoted the Select code before the first printed
            // scan (one round left 4-5 ms spikes in the first fixture's build and rate maxima)
            AutoPlanInput w = Input(Terrain.Alpine(3u), Terrain.AlpineRegion(true));
            for (int k = 0; k < 6; k++)
            {
                w.Knobs.Density = k % 2 == 0 ? AutoDensity.Normal : AutoDensity.Dense;
                TimedScan(w, out _, out _);
            }
            for (uint seed = 1; seed <= 3; seed++)
            {
                HeightFn f = Terrain.Alpine(seed);
                foreach (bool lower in new[] { false, true })
                {
                    float2[] region = Terrain.AlpineRegion(lower);
                    HeightRange(f, region, out double hmin, out double hmax);
                    Console.WriteLine($"TERRAIN alpine {seed} {(lower ? "lower 1.1" : "bowl 3.9")}: {GradeText(f, region, hmin, hmax)}");
                    foreach (AutoDensity d in new[] { AutoDensity.Normal, AutoDensity.Dense })
                    {
                        AutoPlanInput input = Input(f, region);
                        input.Knobs.Density = d;
                        input.Knobs.Mix = AutoMix.Balanced;
                        AutoPlanResult r = TimedScan(input, out double wall, out int calls);
                        string name = $"alpine {seed} {(lower ? "lower 1.1" : "bowl 3.9")} {d}";
                        FieldLines(name, r, wall, hmin, hmax);
                        if (!string.IsNullOrEmpty(dir))
                            Png(System.IO.Path.Combine(dir, $"alpine-{seed}-{(lower ? "lower" : "bowl")}-{d}.png"), f, region, r);
                    }
                }
            }
        }

        // ---------------------------------------------------------------- checks (the normal run)

        /// <summary>The field mirror as checks (quality pass step 2): Terrain.Alpine seeds 1-3 on the bowl and the lower region, Normal
        /// and Dense, Balanced. Bowl: Ok (not AllCliff), runs per km2 of open ground (Normal >= 8, Dense >= 9), greens >= 2 and >= 40 %
        /// of the green target, median width >= 25 m, every centerline >= MinLengthM, a green whose top lies in the lower half of the
        /// vertical, every ring EdgeSeedMarginM / 2 inside the region edge. Lower region: Ok, Dense >= 10 runs per km2, >= 1 green.
        /// Invariants on every result. Unit budget: per-phase Select maxima (best of two sliced scans, Step(4 ms)) pick / A* / build /
        /// chamfer <= 1.2 ms and rate <= 2 ms CoreCLR (Mono x7.7 for Select: about 8 ms).</summary>
        private static void FieldTests()
        {
            var ic = CultureInfo.InvariantCulture;
            var phaseMax = new float[SkiRunAutoPlanner.SelectPhases];
            int mergeStubs = 0, idleSummits = 0, summits = 0;
            for (uint seed = 1; seed <= 3; seed++)
            {
                HeightFn f = Terrain.Alpine(seed);
                foreach (bool lower in new[] { false, true })
                {
                    float2[] region = Terrain.AlpineRegion(lower);
                    HeightRange(f, region, out double hmin, out double hmax);
                    foreach (AutoDensity d in new[] { AutoDensity.Normal, AutoDensity.Dense })
                    {
                        string w = $"field alpine {seed} {(lower ? "lower" : "bowl")} {d}";
                        AutoPlanInput input = Input(f, region);
                        input.Knobs.Density = d;
                        input.Knobs.Mix = AutoMix.Balanced;
                        AutoPlanResult r = TimedScan(input, out _, out _);
                        AutoPlanResult r2 = TimedScan(input, out _, out _);
                        Check(r.Status == AutoPlanStatus.Ok, w, r.Status.ToString());
                        if (r.Status != AutoPlanStatus.Ok)
                            continue;
                        Check(r.Fingerprint == r2.Fingerprint, w, "two scans differ");
                        Invariants(w, input, r);
                        mergeStubs += r.Rejects[(int)AutoReject.MergeStub];
                        foreach (AutoCluster cl in r.Clusters)
                            if (!cl.Pod)
                            {
                                summits++;
                                if (cl.Runs == 0)
                                    idleSummits++;
                            }
                        double openKm2 = r.OpenShare * r.RegionAreaM2 / 1e6;
                        double perKm2 = r.Runs.Count / Math.Max(1e-6, openKm2);
                        int greens = r.BandCount[(int)SlopeBand.Green];
                        var widths = new List<float>();
                        float shortest = float.PositiveInfinity, edge = float.PositiveInfinity;
                        bool lowGreen = false;
                        foreach (AutoRun run in r.Runs)
                        {
                            widths.Add(run.LengthM > 0f ? run.AreaM2 / run.LengthM : 0f);
                            float line = 0f;
                            for (int i = 1; i < run.Centerline.Length; i++)
                                line += math.distance(run.Centerline[i - 1], run.Centerline[i]);
                            shortest = Math.Min(shortest, line);
                            edge = Math.Min(edge, SkiRunAutoPlanner.EdgeDistance(run.Ring, region, 100f, out _, out _, out _, out _));
                            lowGreen |= run.Band == SlopeBand.Green && run.TopY < hmin + 0.5 * (hmax - hmin);
                        }
                        widths.Sort();
                        float median = widths.Count > 0 ? widths[widths.Count / 2] : 0f;
                        if (lower)
                        {
                            if (d == AutoDensity.Dense)
                                Check(perKm2 >= 10.0, w, string.Format(ic, "{0:F1} runs per km2 of open ground (want >= 10)", perKm2));
                            Check(greens >= 1, w, $"{greens} greens");
                        }
                        else
                        {
                            Check(perKm2 >= (d == AutoDensity.Dense ? 9.0 : 8.0), w, string.Format(ic, "{0:F1} runs per km2 of open ground", perKm2));
                            Check(greens >= 2 && greens >= 0.4 * r.BandTarget[(int)SlopeBand.Green], w, $"{greens} greens of target {r.BandTarget[1]}");
                            Check(median >= 25f, w, string.Format(ic, "median width {0:F1} m", median));
                            Check(lowGreen, w, "no green run tops out in the lower half of the vertical");
                        }
                        Check(shortest >= SkiRunAutoPlanner.MinLengthM - 0.01f, w, string.Format(ic, "a centerline of {0:F0} m", shortest));
                        Check(edge >= 0.5f * SkiRunAutoPlanner.EdgeSeedMarginM - EdgeToleranceM, w,
                            string.Format(ic, "a ring {0:F1} m from the region edge", edge));
                        for (int k = 0; k < phaseMax.Length; k++)
                        {
                            float best = Math.Min(r.SelectPhaseMaxMs[k], r2.SelectPhaseMaxMs[k]);
                            phaseMax[k] = Math.Max(phaseMax[k], best);
                            if (TimingChecked)
                                Check(best <= (k == 3 ? 2.0f : 1.2f), w, string.Format(ic, "Select phase {0} unit {1:F2} ms (pick/A*/build/rate/chamfer)", k, best));
                        }
                        Console.WriteLine(string.Format(ic,
                            "INFO  {0}: {1} runs, {2:F1}/km2 open, {3}, median width {4:F0} m, shortest line {5:F0} m, nearest edge {6:F1} m, low green {7}",
                            w, r.Runs.Count, perKm2, Mix(r), median, shortest, edge, lowGreen));
                    }
                }
            }
            // Review F12: the merge-stub rule (LineChecks, MergeKeepShare / MergeKeepM) has its own reject code, so this fails when the
            // rule stops firing (verified once by disabling it: 0 stubs, this check failed)
            Check(mergeStubs > 0, "field merge stubs", "the merge-stub rule never fired on the alpine fixtures");
            Console.WriteLine($"INFO  field alpine: {mergeStubs} merge stubs refused; {idleSummits} of {summits} summit clusters without a run");
            Console.WriteLine("TIME  field alpine Select phase max (CoreCLR, best of 2 per scan, worst scan) pick/A*/build/rate/chamfer " +
                              string.Join("/", Array.ConvertAll(phaseMax, v => v.ToString("F2", ic))) + " ms");
        }

        /// <summary>Ring vertices sit WidthMarginM inside the last free march step and the outline is simplified at RingSimplifyM.</summary>
        private const float EdgeToleranceM = SkiRunAutoPlanner.WidthMarginM + SkiRunAutoPlanner.RingSimplifyM + 1.5f;

        private static void FieldLines(string name, AutoPlanResult r, double wall, double hmin, double hmax)
        {
            var ic = CultureInfo.InvariantCulture;
            double openKm2 = r.OpenShare * r.RegionAreaM2 / 1e6;
            var len = new List<float>();
            var wid = new List<float>();
            var drop = new List<float>();
            int baseN = 0, mergeN = 0;
            foreach (AutoRun run in r.Runs)
            {
                len.Add(run.LengthM);
                wid.Add(run.LengthM > 0f ? run.AreaM2 / run.LengthM : 0f);
                drop.Add(run.DropM);
                if (run.TouchesBase)
                    baseN++;
                if (run.Merge)
                    mergeN++;
            }
            int slow = 0;
            for (int s = 1; s < r.StageMaxMs.Length; s++)
                if (r.StageMaxMs[s] > r.StageMaxMs[slow])
                    slow = s;
            int n = Math.Max(1, r.Runs.Count);
            Console.WriteLine($"FIELD {name}: {r.Status}, {r.Runs.Count} runs, {r.Clusters.Count} clusters {ClusterText(r)}, bands {Mix(r)}, " +
                              $"candidates {r.Candidates}, rated {r.Rated}, rejects [{r.RejectText()}]");
            Console.WriteLine(string.Format(ic,
                "      area {0:F2} km2, open {1:P0}, usable {2:P0}, cliff {3:P0}, vertical {4:F0} m; open {5:F2} km2 -> {6:F1} runs/km2 open",
                r.RegionAreaM2 / 1e6, r.OpenShare, r.UsableShare, r.CliffShare, r.VerticalM, openKm2, openKm2 > 0 ? r.Runs.Count / openKm2 : 0));
            Console.WriteLine($"      length m {Quart(len)}, width m {Quart(wid)}, drop m {Quart(drop)} (q25/q50/q75); touch base {baseN * 100 / n} %, " +
                              $"merge {mergeN * 100 / n} %");
            Console.WriteLine(string.Format(ic,
                "      select max ms pick/A*/build/rate/chamfer {0}; longest unit {1:F2} ms (stage {2}); {3:F0} ms wall, {4} units",
                r.SelectPhaseText(), r.MaxUnitMs, slow, wall, r.Units));
            double topLine = hmax - Math.Max(SkiRunAutoPlanner.TopBandM, SkiRunAutoPlanner.TopShare * (hmax - hmin));
            foreach (AutoRun run in r.Runs)
                Console.WriteLine(string.Format(ic,
                    "        run {0,5} {1,-11} pass {2,-11} len {3,5:F0} w {4,4:F0} drop {5,4:F0} topY {6,5:F0} {7} {8}{9}",
                    run.Id, run.Band, run.PassBand, run.LengthM, run.LengthM > 0f ? run.AreaM2 / run.LengthM : 0f, run.DropM, run.TopY,
                    run.TopY >= topLine ? "TOP " : "mid ", run.TouchesBase ? "base" : "    ", run.Merge ? " merge" : ""));
        }

        /// <summary>Lowest and highest terrain height inside the region (10 m samples; the planner's top line uses usable ground, so
        /// TOP / mid is an approximation of its TopShare band).</summary>
        private static void HeightRange(HeightFn f, float2[] region, out double hmin, out double hmax)
        {
            SkiRunAutoPlanner.BBox(region, out float2 lo, out float2 hi);
            hmin = double.MaxValue;
            hmax = double.MinValue;
            for (float z = lo.y; z <= hi.y; z += 10f)
                for (float x = lo.x; x <= hi.x; x += 10f)
                    if (GradeRaster.Inside(region, new float2(x, z)))
                    {
                        double v = f(x, z);
                        hmin = Math.Min(hmin, v);
                        hmax = Math.Max(hmax, v);
                    }
        }

        /// <summary>Terrain grades (10 m central differences, the scale of the classifier's 20 m window) by height third of the region:
        /// median grade, share at or over the double black ceiling (80 %) and share of cliff (100 %).</summary>
        private static string GradeText(HeightFn f, float2[] region, double hmin, double hmax)
        {
            SkiRunAutoPlanner.BBox(region, out float2 lo, out float2 hi);
            var g = new[] { new List<float>(), new List<float>(), new List<float>() };
            for (float z = lo.y; z <= hi.y; z += 10f)
                for (float x = lo.x; x <= hi.x; x += 10f)
                    if (GradeRaster.Inside(region, new float2(x, z)))
                    {
                        double gx = (f(x + 5, z) - f(x - 5, z)) / 10.0, gz = (f(x, z + 5) - f(x, z - 5)) / 10.0;
                        double u = (f(x, z) - hmin) / Math.Max(1.0, hmax - hmin);
                        g[u >= 2.0 / 3.0 ? 0 : u >= 1.0 / 3.0 ? 1 : 2].Add((float)Math.Sqrt(gx * gx + gz * gz));
                    }
            var parts = new List<string>();
            string[] names = { "upper", "middle", "lower" };
            for (int k = 0; k < 3; k++)
            {
                int n = Math.Max(1, g[k].Count), over = 0, cliff = 0;
                foreach (float v in g[k])
                {
                    if (v >= SkiRunAutoPlanner.PassCeiling[3])
                        over++;
                    if (v >= SkiRunAutoPlanner.CliffGrade)
                        cliff++;
                }
                g[k].Sort();
                float med = g[k].Count > 0 ? g[k][g[k].Count / 2] : 0f;
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0} third median {1:P0} (q90 {2:P0}), {3:P0} >= 80 %, {4:P0} cliff", names[k], med,
                    g[k].Count > 0 ? g[k][(int)(0.9 * (g[k].Count - 1))] : 0f, over / (double)n, cliff / (double)n));
            }
            return string.Format(CultureInfo.InvariantCulture, "heights {0:F0}-{1:F0} m; ", hmin, hmax) + string.Join("; ", parts);
        }

        private static string Quart(List<float> v)
        {
            if (v.Count == 0)
                return "-";
            v.Sort();
            float Q(double p) => v[(int)Math.Round(p * (v.Count - 1))];
            return string.Format(CultureInfo.InvariantCulture, "{0:F0}/{1:F0}/{2:F0}", Q(0.25), Q(0.5), Q(0.75));
        }

        // ---------------------------------------------------------------- review 153b64e fixes (F2, F6, F11)

        private static void ReviewTests()
        {
            // F2: a corridor down one 30 % slope. Under 70 m (2 x EdgeSeedMarginM) no cell can seed: TooNarrow up front, never NoRuns
            // with an empty histogram (Plan asserts that for every scan of the sweep)
            foreach (float width in new[] { 20f, 40f, 60f, 68f, 80f, 100f, 140f, 200f })
            {
                AutoPlanInput input = Input(Terrain.Plane(0.30), Terrain.Rect(-0.5f * width, -600f, 0.5f * width, 600f));
                AutoPlanResult r = Plan(input);
                string w = string.Format(CultureInfo.InvariantCulture, "review corridor {0:F0} m", width);
                Console.WriteLine($"INFO  {w}: {r.Status}, {r.Runs.Count} runs, rejects [{Rejects(r)}]");
                if (width <= 60f)
                    Check(r.Status == AutoPlanStatus.TooNarrow || r.Status == AutoPlanStatus.TooSmall, w, r.Status.ToString());
                if (width >= 140f)
                    Check(r.Status == AutoPlanStatus.Ok, w, r.Status.ToString());
                if (r.Status == AutoPlanStatus.Ok)
                    Invariants(w, input, r);
            }
            // F2: open ground hemmed in by other surfaces: 50 m lanes between 40 m painted strips (55 % open, so not Blocked)
            {
                float2[] sq = Terrain.Square(0f, 0f, 450f);
                var strips = new List<AutoObstacle>();
                for (float x = -450f + 50f; x < 450f; x += 90f)
                    strips.Add(new AutoObstacle { Kind = AutoObstacleKind.Surface, Ring = Terrain.Rect(x, -460f, x + 40f, 460f) });
                AutoPlanResult r = Plan(Input(Terrain.Plane(0.30), sq, strips));
                Check(r.Status == AutoPlanStatus.TooNarrow, "review hemmed lanes", $"{r.Status} (open {r.OpenShare:P0})");
            }
            // F11 / F2: a wide upper slope that drains through a 24 m tongue: the minimax reaches the base through it, but no route
            // source lies 17.5 m inside the edge, so no route exists (was NoRuns with an empty histogram)
            {
                var tongue = new[]
                {
                    new float2(-300f, -500f), new float2(300f, -500f), new float2(300f, 100f), new float2(12f, 100f),
                    new float2(12f, 400f), new float2(-12f, 400f), new float2(-12f, 100f), new float2(-300f, 100f),
                };
                AutoPlanResult r = Plan(Input(Terrain.Plane(0.30), tongue));
                Check(r.Status == AutoPlanStatus.TooNarrow, "review base tongue", r.Status.ToString());
            }
        }
    }
}
