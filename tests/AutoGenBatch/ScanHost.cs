// Host modes of the tool's scan job (port round R2-a): QuickRunsScanJob is the code the tool runs on its worker (the runs-first planner
// and the realizer), so these modes measure and replay exactly that.
//   rf-replay <dump file>: reads a dump the tool wrote (QuickRunsScanDump, the dev trigger's "dump"), runs the same job on the same input
//     and prints the game's and the host's plan hash and result hash. Exit 0 when both match, 2 when one differs (the round's
//     cross-runtime gate).
//   rf-scan <out file> [regions file]: each region at the three density presets (RunsFirstOptions.ForDensity) through the job, with no
//     planner input (the host has no obstacles): plan hash, runs, lifts, km, the scorecard (raw plan, realized as skied), time and the
//     peak managed memory of the scan. No regions file = the field test 5 regions plus one 3 x 3 km square (the 9 km2 cap). The Normal
//     row's hash must equal the golden (rf-golden) of the region: the job plans what the host scores.
// Report only (no checks) except rf-replay's exit code. Run from the repo root:
//   dotnet run --project tests/AutoGenBatch/AutoGenBatch.csproj -c Release -- rf-scan rf-scan.txt [regions file]

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static class ScanHost
    {
        private static float2 P(float x, float z) => new float2(x, z);

        // the field test 5 regions (a copy of RunsFirst.Regions, which is private), then a 3 x 3 km square at ft5-r1 (the 9 km2 cap)
        private static readonly (string Id, float2[] Ring)[] Builtin =
        {
            ("ft5-r1", new[] { P(3372, -1943), P(3462, -3193), P(6386, -2246), P(6244, -1083) }),
            ("ft5-r2", new[] { P(2688, -1711), P(2768, -2269), P(2717, -2714), P(3317, -3708), P(3593, -3839), P(6312, -2292), P(7148, -1835),
                P(6951, -983), P(5793, -1107), P(4905, -1124), P(4047, -1423), P(3703, -1716), P(3358, -1858) }),
            ("ft5-r3", new[] { P(6276, -966), P(5976, 559), P(5020, 1226), P(2076, -686), P(3790, -756), P(4412, -411), P(4942, -362), P(5651, -1175) }),
            ("ft5-r4", new[] { P(-2941, -4929), P(-978, -5499), P(-339, -4233), P(-1945, -3261), P(-2580, -3672) }),
            ("sq9-ft5", new[] { P(3366, -3166), P(6366, -3166), P(6366, -166), P(3366, -166) }),
        };

        public static int Run(string[] args)
        {
            if (args[0] == "rf-replay")
                return Replay(args.Length > 1 ? args[1] : null);
            return Scan(args.Length > 1 ? args[1] : "rf-scan.txt", args.Length > 2 ? args[2] : null);
        }

        // ---------------------------------------------------------------- rf-replay

        private static int Replay(string path)
        {
            if (path == null || !File.Exists(path))
            {
                Console.WriteLine($"rf-replay: no dump file '{path}'");
                return 1;
            }
            AutoPlanInput input;
            RunsFirstOptions options;
            QuickRunsScanDump.Header head;
            using (FileStream fs = File.OpenRead(path))
                input = QuickRunsScanDump.Read(fs, out options, out head);
            if (head.UnknownOptions.Count > 0)
                Console.WriteLine($"REPLAY options in the file this build lacks (skipped): {string.Join(", ", head.UnknownOptions)}");
            ulong optPrint = options.Fingerprint();
            Console.WriteLine($"REPLAY {head.Tag}: snapshot {input.Snapshot.Width} x {input.Snapshot.Depth} texels, region {input.Region.Length} corners " +
                              $"({PlanGeometry.PolygonArea(input.Region) / 1e6:F2} km2), {input.Obstacles?.Count ?? 0} obstacle(s), {input.Pinned?.Count ?? 0} existing lift(s), " +
                              $"{input.Access?.Length ?? 0} access point(s), wet layer {(input.Snapshot.Wet != null ? "yes" : "no")}, bare {head.Bare}, " +
                              $"density {input.Knobs.Density}, kinds {input.Knobs.Kinds}, game status {head.Status}");
            var job = new QuickRunsScanJob(input, options, QuickRunsScanJob.InputsFrom(input, head.Bare), null, head.Tag);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            job.Run();
            ulong plan = job.Plan?.Hash ?? 0UL, result = job.Result?.Fingerprint ?? 0UL;
            bool planOk = plan == head.PlanHash, resultOk = result == head.ResultHash, optOk = optPrint == head.OptionsPrint;
            Console.WriteLine($"REPLAY options  game {head.OptionsPrint:x16} host {optPrint:x16} {(optOk ? "MATCH" : "DIFF")}");
            Console.WriteLine($"REPLAY plan     game {head.PlanHash:x16} host {plan:x16} {(planOk ? "MATCH" : "DIFF")}");
            Console.WriteLine($"REPLAY result   game {head.ResultHash:x16} host {result:x16} {(resultOk ? "MATCH" : "DIFF")}");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "REPLAY host {0}: {1} runs, {2} lifts, {3} connectors in the plan; {4} areas realized, status {5}; blocked cells {6} of {7}; plan {8:F0} ms, " +
                "realize {9:F0} ms, job {10:F0} ms; passes [{11}]", head.Tag, job.Plan?.Plan.Runs.Count ?? 0, job.Plan?.Plan.Lifts.Count ?? 0,
                job.Plan?.Plan.Connectors.Count ?? 0, job.Result?.Runs.Count ?? 0, job.Result?.Status.ToString() ?? "none", job.Plan?.BlockedCells ?? 0,
                (job.Plan?.BlockedCells ?? 0) + (job.Plan?.OpenCells ?? 0), job.Plan?.PlanMs ?? 0, job.RealizeMs, sw.Elapsed.TotalMilliseconds, job.PassText()));
            return planOk && resultOk ? 0 : 2;
        }

        // ---------------------------------------------------------------- rf-scan

        private static int Scan(string outFile, string regionsFile)
        {
            var regions = new List<(string Fx, string Id, float2[] Ring)>();
            if (regionsFile != null)
                regions.AddRange(ReadRegions(regionsFile));
            else
                foreach (var (id, ring) in Builtin)
                    regions.Add(("MountainVillage", id, ring));
            var files = new Dictionary<string, HeightmapFile>();
            var rows = new List<string> { "region\tdensity\tkm2\tplan\truns\tlifts\tconnectors\tareas\tpistes\ttracks\tpiste_km\ttrack_km\traw\tskied\tplan_ms\trealize_ms\tchecks_ms\tpeak_mb\talloc_mb" };
            var sums = new Dictionary<AutoDensity, List<double[]>>();
            foreach (var (fx, id, ring) in regions)
            {
                if (!files.TryGetValue(fx, out HeightmapFile hf))
                {
                    if (!Heightmap.TryOpen(fx, out hf, out string why))
                    {
                        Console.WriteLine($"SKIP rf-scan {id}: {why}");
                        continue;
                    }
                    files[fx] = hf;
                }
                HeightSnapshot snap = Heightmap.Snapshot(hf, ring);
                double km2 = PlanGeometry.PolygonArea(ring) / 1e6;
                foreach (AutoDensity d in new[] { AutoDensity.Sparse, AutoDensity.Normal, AutoDensity.Dense })
                {
                    AutoPlanInput input = RealizerHost.InputOf(snap, ring);
                    input.MaxAreaM2 = PlanGeometry.V1MaxAreaM2 * 1.01f;   // the tool's cap (the square is exactly 9 km2)
                    AutoPlanKnobs k = input.Knobs;
                    k.Density = d;
                    input.Knobs = k;
                    RunsFirstOptions options = RunsFirstOptions.ForDensity(d);
                    var job = new QuickRunsScanJob(input, options, QuickRunsScanJob.InputsFrom(input, false), null, id);
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    long base0 = GC.GetTotalMemory(true), alloc0 = GC.GetTotalAllocatedBytes(true), peak = base0;
                    bool done = false;
                    var sampler = new Thread(() =>
                    {
                        while (!Volatile.Read(ref done))
                        {
                            long m = GC.GetTotalMemory(false);
                            if (m > Interlocked.Read(ref peak))
                                Interlocked.Exchange(ref peak, m);
                            Thread.Sleep(1);
                        }
                    }) { IsBackground = true };
                    sampler.Start();
                    job.Run();
                    Volatile.Write(ref done, true);
                    sampler.Join();
                    peak = Math.Max(peak, GC.GetTotalMemory(false));
                    double peakMb = (peak - base0) / 1048576.0, allocMb = (GC.GetTotalAllocatedBytes(true) - alloc0) / 1048576.0;
                    if (job.Plan == null || job.Result == null)
                    {
                        Console.WriteLine($"SCAN {id} {d}: no plan (status {job.Result?.Status.ToString() ?? "none"}, refused {job.Refused})");
                        continue;
                    }
                    RunsFirstResult plan = job.Plan.Plan;
                    AutoPlanResult res = job.Result;
                    int tracks = res.Runs.Count(r => r.Kind == RunKind.CatTrack);
                    double pisteKm = res.Runs.Where(r => r.Kind != RunKind.CatTrack).Sum(r => (double)r.LengthM) / 1000.0;
                    double trackKm = res.Runs.Where(r => r.Kind == RunKind.CatTrack).Sum(r => (double)r.LengthM) / 1000.0;
                    float raw = RunsFirstScore.Evaluate(snap, ring, RealizerHost.ScoreOfRaw(plan)).Total;
                    float skied = RunsFirstScore.Evaluate(snap, ring, RealizerHost.ScoreOfRealized(res, plan, true)).Total;
                    string row = string.Format(CultureInfo.InvariantCulture,
                        "{0}\t{1}\t{2:F2}\t{3:x16}\t{4}\t{5}\t{6}\t{7}\t{8}\t{9}\t{10:F2}\t{11:F2}\t{12:F2}\t{13:F2}\t{14:F0}\t{15:F0}\t{16:F0}\t{17:F1}\t{18:F0}",
                        id, d, km2, job.Plan.Hash, plan.Runs.Count, plan.Lifts.Count, plan.Connectors.Count, res.Runs.Count, res.Runs.Count - tracks, tracks,
                        pisteKm, trackKm, raw, skied, job.Plan.PlanMs, job.RealizeMs, job.PrecheckMs, peakMb, allocMb);
                    rows.Add(row);
                    Console.WriteLine("SCAN " + row.Replace('\t', ' '));
                    if (!sums.TryGetValue(d, out List<double[]> list))
                        sums[d] = list = new List<double[]>();
                    list.Add(new double[] { plan.Runs.Count, plan.Lifts.Count, pisteKm, trackKm, raw, skied, job.Plan.PlanMs + job.RealizeMs + job.PrecheckMs, peakMb });
                }
            }
            foreach (var kv in sums)
            {
                List<double[]> l = kv.Value;
                string line = string.Format(CultureInfo.InvariantCulture,
                    "SCAN mean {0} over {1} region(s): runs {2:F1}, lifts {3:F1}, piste km {4:F2}, track km {5:F2}, score raw {6:F2}, as skied {7:F2}, " +
                    "job {8:F0} ms, peak {9:F1} MB (max {10:F1})", kv.Key, l.Count, l.Average(x => x[0]), l.Average(x => x[1]), l.Average(x => x[2]),
                    l.Average(x => x[3]), l.Average(x => x[4]), l.Average(x => x[5]), l.Average(x => x[6]), l.Average(x => x[7]), l.Max(x => x[7]));
                rows.Add("# " + line);
                Console.WriteLine(line);
            }
            File.WriteAllLines(outFile, rows);
            return 0;
        }

        private static IEnumerable<(string, string, float2[])> ReadRegions(string path)
        {
            foreach (string row in File.ReadAllLines(path))
            {
                var t = new List<string>(row.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                string fx = t.Count > 1 && t[1].StartsWith("@") ? t[1].Substring(1) : "MountainVillage";
                if (t.Count > 1 && t[1].StartsWith("@"))
                    t.RemoveAt(1);
                if (t.Count < 4 || t.Contains("hole"))
                    continue;   // the keyhole rows belong to rf / rf-realize
                var ring = new List<float2>();
                for (int i = 1; i < t.Count; i++)
                {
                    string[] xz = t[i].Split(',');
                    ring.Add(new float2(float.Parse(xz[0], CultureInfo.InvariantCulture), float.Parse(xz[1], CultureInfo.InvariantCulture)));
                }
                yield return (fx, t[0], ring.ToArray());
            }
        }
    }
}
