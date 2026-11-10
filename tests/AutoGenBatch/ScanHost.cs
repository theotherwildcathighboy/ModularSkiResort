// Host modes of the tool's scan job (port round R2-a): QuickRunsScanJob is the code the tool runs on its worker (the runs-first planner
// and the realizer), so these modes measure and replay exactly that.
//   rf-replay <dump file>: reads a dump the tool wrote (QuickRunsScanDump, the dev trigger's "dump"), runs the same job on the same input
//     and prints the game's and the host's plan hash and result hash. Exit 0 when both match, 2 when one differs (the round's
//     cross-runtime gate), 3 when the job throws (a fault dump, SkiResortMod.fault-n.rfscan).
//     Version 2 dumps (port round 2b-f) also give game against host per planner pass and per realizer stage (RunsFirstPrint), name the
//     FIRST pass that differs with its parts (grid, flow, lines, lifts, runs, connectors, links), and the first plan element that differs.
//     "--redump <out>" writes the host's plan and prints as a version 2 dump (a test input for rf-game-plans, GamePlans.cs).
//   rf-scan <out file> [regions file]: each region at the three density presets (RunsFirstOptions.ForDensity) through the job, with no
//     planner input (the host has no obstacles): plan hash, runs, lifts, km, the scorecard (raw plan, realized as skied), time and the
//     peak managed memory of the scan. No regions file = the field test 5 regions plus one 3 x 3 km square (the 9 km2 cap). The Normal
//     row's hash must equal the golden (rf-golden) of the region: the job plans what the host scores. RF_SCAN_LIVE=1: the peak_mb
//     column is the live set (a full GC every LiveSampleMs, so the times are slower): the memory bound reads this column.
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
        private const int LiveSampleMs = 20;   // RF_SCAN_LIVE=1: the live-set sampler's period (a full GC per sample)

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
                return Replay(args.Length > 1 ? args[1] : null, args);
            return Scan(args.Length > 1 ? args[1] : "rf-scan.txt", args.Length > 2 ? args[2] : null);
        }

        // ---------------------------------------------------------------- rf-replay

        private static int Replay(string path, string[] args)
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
            try
            {
                job.Run();
            }
            catch (Exception ex)
            {
                // a fault dump (SkiResortMod.fault-n.rfscan, game status "fault") replays the exception
                Console.WriteLine($"REPLAY FAULT {head.Tag} (game status {head.Status}): plan game {head.PlanHash:x16} host {job.Plan?.Hash ?? 0UL:x16}; {ex}");
                return 3;
            }
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
            if (head.Version < 2)
                Console.WriteLine($"REPLAY prints: a version {head.Version} dump holds no game prints and no game plan (the host's prints only)");
            ComparePrints("pass", head.Passes, job.Plan?.Plan.Passes, RunsFirstPlanner.PrintParts);
            ComparePrints("stage", head.Stages, job.Report.Stages, StageParts);
            if (head.Plan != null && job.Plan != null)
                ComparePlans(head.Plan, job.Plan.Plan);
            int redump = Array.IndexOf(args, "--redump");
            if (redump > 0 && redump + 1 < args.Length && job.Plan != null)
            {
                // a version 2 dump with the HOST's plan and prints in the game's slots: a test input for rf-replay and rf-game-plans
                var h2 = new QuickRunsScanDump.Header
                {
                    Tag = head.Tag, Bare = head.Bare, PlanHash = plan, ResultHash = result, OptionsPrint = optPrint, Status = job.Result?.Status.ToString() ?? "none",
                    Passes = job.Plan.Plan.Passes, Stages = job.Report.Stages, Plan = job.Plan.Plan,
                };
                using (FileStream fs = File.Create(args[redump + 1]))
                    QuickRunsScanDump.Write(fs, h2, input, options);
                Console.WriteLine($"REPLAY redump: the host's plan and prints written as a version 2 dump to {args[redump + 1]}");
            }
            return planOk && resultOk ? 0 : 2;
        }

        /// <summary>The part names of a realizer stage print (RunsFirstRealizer.Stage).</summary>
        internal static readonly string[] StageParts = { "region", "elements", "lifts", "pistes", "tracks", "result" };

        /// <summary>Game against host per pass (or stage): one row each, then the parts of the FIRST pass that differs (print and count
        /// per part). game null: the host's prints only. Returns the index of the first differing row (-1: none, or no game prints).</summary>
        internal static int ComparePrints(string what, List<RunsFirstPrint> game, IReadOnlyList<RunsFirstPrint> host, string[] partNames)
        {
            if (host == null)
                host = new List<RunsFirstPrint>();
            int first = -1, n = Math.Max(game?.Count ?? 0, host.Count);
            for (int i = 0; i < n; i++)
            {
                RunsFirstPrint g = game != null && i < game.Count ? game[i] : null, h = i < host.Count ? host[i] : null;
                bool same = g != null && h != null && g.Pass == h.Pass && g.Print == h.Print;
                if (game != null && !same && first < 0)
                    first = i;
                Console.WriteLine($"REPLAY {what} {i,2} {(h ?? g).Pass,-16} game {(g == null ? "-" : g.Print.ToString("x16"))} host " +
                    $"{(h == null ? "-" : h.Print.ToString("x16"))} {(game == null ? "" : same ? "MATCH" : "DIFF")}{(i == first ? "  <-- first difference" : "")}");
            }
            if (game == null)
                return -1;
            if (first < 0)
            {
                Console.WriteLine($"REPLAY {what}: every print matches ({n})");
                return -1;
            }
            RunsFirstPrint fg = first < game.Count ? game[first] : null, fh = first < host.Count ? host[first] : null;
            Console.WriteLine($"REPLAY {what}: FIRST DIFFERENCE after {what} {first} '{(fh ?? fg).Pass}'" + (fg != null && fh != null && fg.Pass != fh.Pass ? $" (game pass '{fg.Pass}')" : ""));
            if (fg != null && fh != null)
                for (int k = 0; k < partNames.Length && k < fg.Parts.Length && k < fh.Parts.Length; k++)
                {
                    bool same = fg.Parts[k] == fh.Parts[k] && fg.Counts[k] == fh.Counts[k];
                    bool before = first > 0 && game[first - 1].Parts.Length > k && host[first - 1].Parts.Length > k && game[first - 1].Parts[k] == host[first - 1].Parts[k];
                    Console.WriteLine($"REPLAY {what}   part {partNames[k],-11} game {fg.Parts[k]:x16} n {fg.Counts[k],6}  host {fh.Parts[k]:x16} n {fh.Counts[k],6}  " +
                        $"{(same ? "same" : "DIFF")}{(!same && (first == 0 || before) ? " (new in this " + what + ")" : "")}{(same ? "" : FirstItem(fg, fh, k))}");
                }
            return first;
        }

        /// <summary>", first differing element 17 of 741 (5 differ)" from the per-element prints, or "" when the prints have none.</summary>
        private static string FirstItem(RunsFirstPrint g, RunsFirstPrint h, int k)
        {
            ulong[] a = g.Items != null && k < g.Items.Length ? g.Items[k] : null, b = h.Items != null && k < h.Items.Length ? h.Items[k] : null;
            if (a == null || b == null)
                return "";
            int first = -1, differ = Math.Abs(a.Length - b.Length);
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
                if (a[i] != b[i])
                {
                    differ++;
                    if (first < 0)
                        first = i;
                }
            if (first < 0)
                first = Math.Min(a.Length, b.Length);
            return $"; first differing element {first} ({differ} differ by index)";
        }

        /// <summary>Game plan against host plan, element by element: per list the counts, then the first element that differs and its
        /// first differing point (the float bits and the distance).</summary>
        internal static void ComparePlans(RunsFirstResult game, RunsFirstResult host)
        {
            string P(float2 p) => string.Format(CultureInfo.InvariantCulture, "({0:F3}, {1:F3})", p.x, p.y);
            void Lines(string what, IList<List<float2>> g, IList<List<float2>> h, Func<int, string> gTag, Func<int, string> hTag)
            {
                int n = Math.Min(g.Count, h.Count);
                for (int i = 0; i < n; i++)
                {
                    string tg = gTag(i), th = hTag(i);
                    int k = 0, m = Math.Min(g[i].Count, h[i].Count);
                    while (k < m && math.asuint(g[i][k]).Equals(math.asuint(h[i][k])))
                        k++;
                    if (tg == th && k == m && g[i].Count == h[i].Count)
                        continue;
                    string pt = k < m ? $", first point {k} game {P(g[i][k])} host {P(h[i][k])} ({math.distance(g[i][k], h[i][k]):G3} m apart)" : "";
                    Console.WriteLine($"REPLAY plan {what}: {g.Count} game, {h.Count} host; first difference at {what} {i}: game [{tg}] {g[i].Count} pts, " +
                        $"host [{th}] {h[i].Count} pts{pt}");
                    return;
                }
                Console.WriteLine($"REPLAY plan {what}: {g.Count} game, {h.Count} host; {(g.Count == h.Count ? "all equal" : $"the first {n} equal")}");
            }
            string RunTag(RunsFirstResult.Run r) => $"{r.Kind} {r.Band}{(r.Graded ? " graded" : "")}{(r.Groomed ? "" : " natural")} L{r.Lift}";
            Lines("run", game.Runs.Select(r => r.Pts).ToList(), host.Runs.Select(r => r.Pts).ToList(), i => RunTag(game.Runs[i]), i => RunTag(host.Runs[i]));
            Lines("connector", game.Connectors, host.Connectors, i => "", i => "");
            Lines("link", game.Links, host.Links, i => "", i => "");
            string LiftTag(RunsFirstResult.Lift l) => $"L{l.Id} {l.Type} {l.Kind} {l.ToBottom}{(l.RidesDown ? " down" : "")}";
            Lines("lift", game.Lifts.Select(l => new List<float2> { l.Bottom.xz, l.Top.xz }).ToList(), host.Lifts.Select(l => new List<float2> { l.Bottom.xz, l.Top.xz }).ToList(),
                i => LiftTag(game.Lifts[i]), i => LiftTag(host.Lifts[i]));
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
                    bool done = false, live = Environment.GetEnvironmentVariable("RF_SCAN_LIVE") == "1";
                    var sampler = new Thread(() =>
                    {
                        while (!Volatile.Read(ref done))
                        {
                            // live: a full collection per sample (the job pauses for it), so the peak is the live set, not the garbage
                            long m = GC.GetTotalMemory(live);
                            if (m > Interlocked.Read(ref peak))
                                Interlocked.Exchange(ref peak, m);
                            Thread.Sleep(live ? LiveSampleMs : 1);
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
