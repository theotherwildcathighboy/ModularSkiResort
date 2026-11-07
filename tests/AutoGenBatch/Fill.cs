// P2 (field test 2026-10-26): run counts on open slopes at each density, Shuffle moving runs, and the 9 km2 cap within the unit budget.
// FillTests are checks of the normal run. `dotnet run ... -- diag` prints only the diagnostic tables (no checks). `-- big` prints the
// 1 / 4 / 9 km2 stage table. AUTOGEN_SVG=<folder> with diag writes a PNG per fixture.

using System;
using System.Collections.Generic;
using System.Globalization;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class Program
    {
        /// <summary>The field's open slope: 1 km2, about 440 m of vertical, natural relief.</summary>
        private static HeightFn OpenSlope(uint seed) => Terrain.Fbm(seed, 0.44, 60.0);

        // ---------------------------------------------------------------- checks

        private static void FillTests()
        {
            // 1. Run counts: the field's Dense on a 1 km2 open slope picked 4-6 of 21 candidates (target 16). Thresholds sit under the
            // measured counts (open slopes Sparse 8 / Normal 10-12 / Dense 9-16, planes up to 16) with room for terrain-bound slopes.
            float2[] sq = Terrain.Square(0f, 0f, 499f);
            var sums = new int[3];
            foreach (var (name, f) in new (string, HeightFn)[] { ("open slope 1", OpenSlope(1u)), ("open slope 2", OpenSlope(2u)), ("open slope 3", OpenSlope(3u)) })
                for (int d = 0; d < 3; d++)
                {
                    AutoPlanInput input = Input(f, sq);
                    input.Knobs.Density = (AutoDensity)d;
                    AutoPlanResult r = Plan(input);
                    string where = $"fill {name} {(AutoDensity)d}";
                    Check(r.Status == AutoPlanStatus.Ok, where, r.Status.ToString());
                    if (r.Status != AutoPlanStatus.Ok)
                        continue;
                    Invariants(where, input, r);
                    sums[d] += r.Runs.Count;
                    Check(r.Runs.Count >= (d == 0 ? 6 : 8), where, $"{r.Runs.Count} runs");
                    Check(r.Candidates >= 40, where, $"{r.Candidates} candidates (the field had 21)");
                }
            Check(sums[0] >= 21 && sums[1] >= 30 && sums[2] >= 30, "fill open slopes", $"runs Sparse {sums[0]}, Normal {sums[1]}, Dense {sums[2]} over 3 slopes");
            Console.WriteLine($"INFO  fill open slopes (3 x 1 km2, ~500 m): runs Sparse {sums[0]}, Normal {sums[1]}, Dense {sums[2]}");
            foreach (var (name, g) in new (string, double)[] { ("plane 30", 0.30), ("plane 44", 0.44) })
            {
                AutoPlanInput input = Input(Terrain.Plane(g), sq);
                input.Knobs.Density = AutoDensity.Dense;
                AutoPlanResult r = Plan(input);
                Check(r.Status == AutoPlanStatus.Ok && r.Runs.Count >= 12, $"fill {name} Dense", $"{r.Status}, {r.Runs.Count} runs");
            }

            // 2. Shuffle moves runs. Other seeds give other layouts (fingerprint and cover). The same seed gives the same layout. A
            // Reselect to a seed equals a fresh scan with that seed.
            foreach (var (name, f) in new (string, HeightFn)[] { ("plane 30", Terrain.Plane(0.30)), ("open slope 1", OpenSlope(1u)) })
            {
                AutoPlanInput input = Input(f, sq);
                input.Knobs.Density = AutoDensity.Dense;
                var p = new SkiRunAutoPlanner();
                AutoPlanResult r0 = p.Run(input);
                var cover0 = new AutoPlanResult();
                cover0.Runs.AddRange(r0.Runs);
                ulong fp0 = r0.Fingerprint;
                double worst = 0;
                for (uint s = 1; s <= 3; s++)
                {
                    AutoPlanKnobs k = input.Knobs;
                    k.Seed = SkiRunAutoPlanner.SeedOf(sq, s);
                    p.Reselect(k);
                    while (!p.Step(float.PositiveInfinity))
                    {
                    }
                    double iou = CoverIoU(sq, cover0, p.Result);
                    worst = Math.Max(worst, iou);
                    Check(p.Result.Fingerprint != fp0, $"shuffle {name} seed {s}", "same layout as seed 0");
                    Check(iou < 0.7, $"shuffle {name} seed {s}", $"cover IoU {iou:F2} vs seed 0 (runs barely moved)");
                    AutoPlanInput fresh = Input(f, sq);
                    fresh.Knobs = k;
                    ulong fpFresh = Plan(fresh).Fingerprint;
                    Check(fpFresh == p.Result.Fingerprint, $"shuffle {name} seed {s}", "Reselect differs from a fresh scan with the seed");
                    Check(Plan(fresh).Fingerprint == fpFresh, $"shuffle {name} seed {s}", "not deterministic");
                }
                Console.WriteLine($"INFO  shuffle {name}: highest cover IoU vs seed 0 over 3 seeds {worst:F2}");
            }

            // 4. Probe counters are per scan: a Seeds re-select reports only its own stages. So stage 0 (Heights) is 0 and the units do
            // not add up across Shuffles. The field's stage 13 grew 17 -> 217 ms over 14 shuffles.
            {
                AutoPlanInput input = Input(OpenSlope(2u), sq);
                var p = new SkiRunAutoPlanner();
                AutoPlanResult r = p.Run(input);
                int units0 = r.Units, maxUnits = 0;
                for (uint s = 1; s <= 6; s++)
                {
                    AutoPlanKnobs k = input.Knobs;
                    k.Seed = SkiRunAutoPlanner.SeedOf(sq, s);
                    p.Reselect(k);
                    while (!p.Step(float.PositiveInfinity))
                    {
                    }
                    maxUnits = Math.Max(maxUnits, p.Result.Units);
                    Check(p.Result.StageMs[0] == 0f && p.Result.StageMaxMs[0] == 0f, $"probe reselect {s}", "Heights time carried over from the scan");
                }
                Check(maxUnits < units0, "probe reselect", $"re-select units {maxUnits} not under the scan's {units0} (accumulating)");
            }

            // 3. The 9 km2 cap within the unit budget. Mono runs the planner 4-8x slower than CoreCLR (field 2026-10-26: Field x10,
            // Route x3.4, Minimax x3.8, Select x7.7). So a CoreCLR unit of 4 ms is about one 60 fps frame on Mono at x4 (two at x8).
            // Best of three scans: the host shares the machine (a game may be running).
            {
                float2[] nine = Terrain.Square(0f, 0f, 1499f);
                AutoPlanInput input = Input(Terrain.Fbm(21u, 0.30, 90.0), nine);
                input.Knobs.Density = AutoDensity.Dense;
                AutoPlanResult best = null;
                double bestWall = 0;
                for (int t = 0; t < 3; t++)
                {
                    AutoPlanResult r = TimedScan(input, out double wall, out _);
                    if (best == null || r.MaxUnitMs < best.MaxUnitMs)
                    {
                        best = r;
                        bestWall = wall;
                    }
                }
                Check(best.Status == AutoPlanStatus.Ok, "fill 9 km2", best.Status.ToString());
                if (best.Status == AutoPlanStatus.Ok)
                    Invariants("fill 9 km2", input, best);
                Check(best.Runs.Count >= 30, "fill 9 km2", $"{best.Runs.Count} runs");
                if (TimingChecked)
                    Check(best.MaxUnitMs <= 4f, "fill 9 km2", string.Format(CultureInfo.InvariantCulture, "longest unit {0:F2} ms over the 4 ms CoreCLR budget ({1})",
                        best.MaxUnitMs, StageText(best)));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "TIME  autogen 9 km2 Dense (CoreCLR, best of 3): {0:F0} ms, {1} units, longest unit {2:F2} ms (Mono x4-8: {3:F0}-{4:F0} ms); {5} runs",
                    bestWall, best.Units, best.MaxUnitMs, 4 * best.MaxUnitMs, 8 * best.MaxUnitMs, best.Runs.Count));
                Console.WriteLine("      stage total/max ms: " + StageText(best));
            }
        }

        // ---------------------------------------------------------------- diagnostics

        private static void FillDiag()
        {
            float2[] sq = Terrain.Square(0f, 0f, 499f);
            var fixtures = new List<(string, HeightFn, float2[])>
            {
                ("plane 30", Terrain.Plane(0.30), sq),
                ("plane 44", Terrain.Plane(0.44), sq),
                ("open slope 1", OpenSlope(1u), sq),
                ("open slope 2", OpenSlope(2u), sq),
                ("open slope 3", OpenSlope(3u), sq),
                ("mixed mountain", Mixed, MixedRegion),
                ("cone", (x, z) => 1500.0 - 0.35 * Math.Sqrt(x * x + z * z), Terrain.Disc(0f, 0f, 560f, 32)),
            };
            foreach (var (name, f, region) in fixtures)
                foreach (AutoDensity d in new[] { AutoDensity.Sparse, AutoDensity.Normal, AutoDensity.Dense })
                {
                    AutoPlanInput input = Input(f, region);
                    input.Knobs.Density = d;
                    var p = new SkiRunAutoPlanner();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    AutoPlanResult r = p.Run(input);
                    sw.Stop();
                    Info($"{name} {d} ({sw.ElapsedMilliseconds} ms, vertical {r.VerticalM:F0} m)", r);
                    string dir = Environment.GetEnvironmentVariable("AUTOGEN_SVG");
                    if (!string.IsNullOrEmpty(dir))
                        Png(System.IO.Path.Combine(dir, $"{name.Replace(' ', '-')}-{d}.png"), f, region, r);
                }
        }

        /// <summary>One sliced scan at Step(4 ms) (the tool's budget). It returns the result with per-stage time and longest unit.</summary>
        private static AutoPlanResult TimedScan(AutoPlanInput input, out double wallMs, out int calls)
        {
            var p = new SkiRunAutoPlanner();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            p.Begin(input);
            calls = 1;
            while (!p.Step(4f))
                calls++;
            wallMs = sw.Elapsed.TotalMilliseconds;
            return p.Result;
        }

        private static string StageText(AutoPlanResult r)
        {
            var parts = new List<string>();
            for (int s = 0; s < SkiRunAutoPlanner.StageCount - 1; s++)
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1:F1}/{2:F2}", s, r.StageMs[s], r.StageMaxMs[s]));
            return string.Join(", ", parts);
        }

        private static void BigDiag()
        {
            foreach (float km2 in new[] { 1f, 4f, 9f })
            {
                float half = 500f * (float)Math.Sqrt(km2) - 1f;
                AutoPlanInput input = Input(Terrain.Fbm(21u, 0.30, 90.0), Terrain.Square(0f, 0f, half));
                input.Knobs.Density = AutoDensity.Dense;
                TimedScan(input, out _, out _);   // JIT warm-up
                AutoPlanResult r = TimedScan(input, out double ms, out int calls);
                Info($"big {km2} km2 ({ms:F0} ms, {calls} calls, {r.Units} units, max unit {r.MaxUnitMs:F2} ms)", r);
                Console.WriteLine("      stage total/max ms: " + StageText(r));
            }
        }

        /// <summary>Intersection over union of two layouts' run cover, sampled every 5 m over the region's bbox.</summary>
        private static double CoverIoU(float2[] region, AutoPlanResult a, AutoPlanResult b)
        {
            SkiRunAutoPlanner.BBox(region, out float2 lo, out float2 hi);
            int both = 0, any = 0;
            for (float z = lo.y; z < hi.y; z += 5f)
                for (float x = lo.x; x < hi.x; x += 5f)
                {
                    var p = new float2(x, z);
                    bool ia = Covered(a, p), ib = Covered(b, p);
                    if (ia && ib)
                        both++;
                    if (ia || ib)
                        any++;
                }
            return any == 0 ? 1.0 : both / (double)any;
        }

        private static bool Covered(AutoPlanResult r, float2 p)
        {
            foreach (AutoRun run in r.Runs)
                if (GradeRaster.Inside(run.Ring, p))
                    return true;
            return false;
        }

        private static void ShuffleDiag()
        {
            float2[] sq = Terrain.Square(0f, 0f, 499f);
            foreach (var (name, f) in new (string, HeightFn)[] { ("plane 30", Terrain.Plane(0.30)), ("open slope 1", OpenSlope(1u)), ("open slope 2", OpenSlope(2u)), ("mixed", Mixed) })
            {
                float2[] region = name == "mixed" ? MixedRegion : sq;
                AutoPlanInput input = Input(f, region);
                input.Knobs.Density = AutoDensity.Dense;
                var p = new SkiRunAutoPlanner();
                AutoPlanResult r0 = p.Run(input);
                var parts = new List<string>();
                var r0copy = new AutoPlanResult();
                r0copy.Runs.AddRange(r0.Runs);
                for (uint s = 1; s <= 5; s++)
                {
                    AutoPlanKnobs k = input.Knobs;
                    k.Seed = SkiRunAutoPlanner.SeedOf(region, s);
                    p.Reselect(k);
                    while (!p.Step(float.PositiveInfinity))
                    {
                    }
                    parts.Add($"{p.Result.Runs.Count} runs IoU {CoverIoU(region, r0copy, p.Result):F2}");
                }
                Console.WriteLine($"INFO  shuffle {name}: seed 0 {r0copy.Runs.Count} runs; " + string.Join("; ", parts));
            }
        }

        /// <summary>Diagnostic picture. Gray = height (20 m contours darker, 50 m over 600 m of relief). Cliff: red-tinted. Runs: tinted by band with white outlines. Red = clusters.</summary>
        private static void Png(string path, HeightFn f, float2[] region, AutoPlanResult r)
        {
            SkiRunAutoPlanner.BBox(region, out float2 lo, out float2 hi);
            int px = 500;
            float m = (hi.x - lo.x) / px;
            int pz = (int)((hi.y - lo.y) / m);
            var rgb = new byte[px * pz * 3];
            double hmin = double.MaxValue, hmax = double.MinValue;
            var hs = new double[px * pz];
            for (int j = 0; j < pz; j++)
                for (int i = 0; i < px; i++)
                {
                    double v = f(lo.x + (i + 0.5) * m, lo.y + (j + 0.5) * m);
                    hs[j * px + i] = v;
                    hmin = Math.Min(hmin, v);
                    hmax = Math.Max(hmax, v);
                }
            double ci = hmax - hmin > 600.0 ? 50.0 : 20.0;   // contour interval (50 m on the alpine fixtures)
            int[][] col = { new[] { 128, 128, 128 }, new[] { 40, 200, 40 }, new[] { 40, 100, 255 }, new[] { 10, 10, 10 }, new[] { 255, 40, 255 } };
            for (int j = 0; j < pz; j++)
                for (int i = 0; i < px; i++)
                {
                    var p = new float2(lo.x + (i + 0.5f) * m, lo.y + (j + 0.5f) * m);
                    double v = hs[j * px + i];
                    int g = (int)(90 + 140 * (v - hmin) / Math.Max(1e-6, hmax - hmin));
                    double hx = f(p.x + m, p.y), hz = f(p.x, p.y + m);
                    if (Math.Floor(v / ci) != Math.Floor(hx / ci) || Math.Floor(v / ci) != Math.Floor(hz / ci))
                        g -= 50;
                    bool cliff = Math.Sqrt((hx - v) * (hx - v) + (hz - v) * (hz - v)) / m >= SkiRunAutoPlanner.CliffGrade;
                    if (!GradeRaster.Inside(region, p))
                        g /= 3;
                    int cr = g, cg = g, cb = g;
                    if (cliff)
                    {
                        cr = Math.Min(255, g + 90);   // cliff (point grade over 100 %): red tint
                        cg = cb = g / 2;
                    }
                    foreach (AutoRun run in r.Runs)
                    {
                        if (!GradeRaster.Inside(run.Ring, p))
                            continue;
                        int[] c = col[(int)run.Band];
                        bool edge = !GradeRaster.Inside(run.Ring, p + new float2(2 * m, 0)) || !GradeRaster.Inside(run.Ring, p - new float2(2 * m, 0)) ||
                                    !GradeRaster.Inside(run.Ring, p + new float2(0, 2 * m)) || !GradeRaster.Inside(run.Ring, p - new float2(0, 2 * m));
                        cr = edge ? 255 : (c[0] + cr) / 2;
                        cg = edge ? 255 : (c[1] + cg) / 2;
                        cb = edge ? 255 : (c[2] + cb) / 2;
                    }
                    foreach (AutoCluster c in r.Clusters)
                        if (math.distance(c.Center, p) < 10f)
                        {
                            cr = 255;
                            cg = c.Pod ? 160 : 0;
                            cb = 0;
                        }
                    int o = (j * px + i) * 3;
                    rgb[o] = (byte)Math.Max(0, Math.Min(255, cr));
                    rgb[o + 1] = (byte)Math.Max(0, Math.Min(255, cg));
                    rgb[o + 2] = (byte)Math.Max(0, Math.Min(255, cb));
                }
            WritePng(path, px, pz, rgb);
        }

        internal static void WritePngRgb(string path, int w, int h, byte[] rgb) => WritePng(path, w, h, rgb);

        private static void WritePng(string path, int w, int h, byte[] rgb)
        {
            var raw = new System.IO.MemoryStream();
            for (int j = 0; j < h; j++)
            {
                raw.WriteByte(0);
                raw.Write(rgb, j * w * 3, w * 3);
            }
            var z = new System.IO.MemoryStream();
            using (var zs = new System.IO.Compression.ZLibStream(z, System.IO.Compression.CompressionLevel.Optimal, true))
                raw.WriteTo(zs);
            using var fs = System.IO.File.Create(path);
            fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            void Chunk(string type, byte[] data)
            {
                var len = BitConverter.GetBytes(data.Length);
                Array.Reverse(len);
                fs.Write(len);
                var td = new byte[4 + data.Length];
                System.Text.Encoding.ASCII.GetBytes(type).CopyTo(td, 0);
                data.CopyTo(td, 4);
                fs.Write(td);
                uint crc = 0xFFFFFFFFu;
                foreach (byte b in td)
                {
                    crc ^= b;
                    for (int k = 0; k < 8; k++)
                        crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
                }
                var cb = BitConverter.GetBytes(~crc);
                Array.Reverse(cb);
                fs.Write(cb);
            }
            var ihdr = new byte[13];
            var wb = BitConverter.GetBytes(w);
            var hb = BitConverter.GetBytes(h);
            Array.Reverse(wb);
            Array.Reverse(hb);
            wb.CopyTo(ihdr, 0);
            hb.CopyTo(ihdr, 4);
            ihdr[8] = 8;
            ihdr[9] = 2;
            Chunk("IHDR", ihdr);
            Chunk("IDAT", z.ToArray());
            Chunk("IEND", new byte[0]);
        }
    }
}
