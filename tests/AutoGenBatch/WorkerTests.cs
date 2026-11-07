// Worker suite (A2, stage-a-gates.md 2.5 / 3). It tests these cases:
// - D-4: Run inside Task.Run == Run on the calling thread, both planner modes, and a Reselect resumed on a worker == a fresh run
//   with those knobs.
// - D-5: a cancel at unit k = 1, 10, 100, last returns Canceled within one unit. Reselect refuses afterwards. The next fresh run on
//   the same planner equals the uncanceled fingerprint. Also a cancel from another thread while Run works on a worker.
// - ProgressVolatile read across threads.
// - Snapshot() copy independence (the published copy does not change when the planner runs again).
// The tool's job (QuickRunsToolSystem.Worker.cs) is engine side. These are the planner contracts it relies on.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class Program
    {
        private static void WorkerTests()
        {
            float2[] sq480 = Terrain.Square(0f, 0f, 480f);
            HeightFn fbm4 = Terrain.Fbm(4u, 0.32, 70.0);
            var cases = new List<(string Name, Func<AutoPlanInput> Make)>
            {
                ("determinism base", () => Input(fbm4, sq480)),
                ("alpine 1 bowl", () => Input(Terrain.Alpine(1u), Terrain.AlpineRegion(false))),
            };
            foreach (var (name, make) in cases)
                foreach (AutoPlanMode mode in new[] { AutoPlanMode.Runs, AutoPlanMode.Resort })
                {
                    string where = $"worker D-4 {name} {mode}";
                    AutoPlanInput input = make();
                    input.Mode = mode;
                    ulong here = new SkiRunAutoPlanner().Run(input).Fingerprint;
                    AutoPlanResult onWorker = Task.Run(() => new SkiRunAutoPlanner().Run(input)).Result;
                    Check(onWorker.Status == AutoPlanStatus.Ok, where, $"status {onWorker.Status}");
                    Check(onWorker.Fingerprint == here, where, $"worker {onWorker.Fingerprint:X16} != calling thread {here:X16}");
                    // The tool's planner is one instance: a worker run after a calling-thread run, then the reverse
                    var p = new SkiRunAutoPlanner();
                    p.Run(input);
                    Check(Task.Run(() => p.Run(input).Fingerprint).Result == here, where, "the same instance on a worker after a calling-thread run differs");
                    Check(p.Run(input).Fingerprint == here, where, "the same instance on the calling thread after a worker run differs");
                    Check(mode == AutoPlanMode.Resort ? onWorker.Resort != null : onWorker.Resort == null, where, "ResortPlan presence does not follow the mode");
                }
            WorkerReselect();
            WorkerCancel();
            WorkerProgress();
            WorkerSnapshotCopy();
        }

        /// <summary>D-4 for the tool's knob path: Reselect on the calling thread, then the Step loop on a worker (what LaunchScan resume
        /// runs) equals a fresh calling-thread run with the new knobs.</summary>
        private static void WorkerReselect()
        {
            float2[] region = Terrain.Square(0f, 0f, 480f);
            HeightFn f = Terrain.Fbm(4u, 0.32, 70.0);
            foreach (AutoPlanMode mode in new[] { AutoPlanMode.Runs, AutoPlanMode.Resort })
            {
                AutoPlanInput input = Input(f, region);
                input.Mode = mode;
                AutoPlanKnobs seed7 = input.Knobs, expert = input.Knobs;
                seed7.Seed = SkiRunAutoPlanner.SeedOf(region, 7);
                expert.Mix = AutoMix.Expert;
                foreach (var (what, knobs) in new[] { ("seed 7", seed7), ("mix expert", expert) })
                {
                    string where = $"worker D-4 reselect {what} {mode}";
                    AutoPlanInput fresh = Input(f, region);
                    fresh.Mode = mode;
                    fresh.Knobs = knobs;
                    ulong want = new SkiRunAutoPlanner().Run(fresh).Fingerprint;
                    var p = new SkiRunAutoPlanner();
                    p.Run(input);
                    Check(p.Reselect(knobs), where, "Reselect refused on a finished scan");
                    ulong got = Task.Run(() =>
                    {
                        while (!p.Step(float.PositiveInfinity))
                        {
                        }
                        return p.Result.Fingerprint;
                    }).Result;
                    Check(got == want, where, $"resumed on a worker {got:X16} != fresh {want:X16}");
                }
            }
        }

        /// <summary>D-5: a cancel at unit k (1, 10, 100, the last) via Step(0) (exactly one unit per call), then from another thread.</summary>
        private static void WorkerCancel()
        {
            foreach (AutoPlanMode mode in new[] { AutoPlanMode.Runs, AutoPlanMode.Resort })
            {
                AutoPlanInput input = Input(Terrain.Alpine(1u), Terrain.AlpineRegion(false));
                input.Mode = mode;
                AutoPlanResult full = new SkiRunAutoPlanner().Run(input);
                ulong want = full.Fingerprint;
                int units = full.Units;
                Check(units > 100, $"worker D-5 {mode}", $"the fixture has only {units} units (the k = 100 case proves nothing)");
                var p = new SkiRunAutoPlanner();
                foreach (int k in new[] { 1, 10, 100, units - 1 })
                {
                    string where = $"worker D-5 {mode} cancel at unit {k} of {units}";
                    var cancel = new AutoPlanCancel();
                    input.Cancel = cancel;
                    bool done = p.Begin(input) != AutoPlanStatus.Running;
                    for (int u = 0; u < k && !done; u++)
                        done = p.Step(0f);
                    Check(!done, where, "the scan finished before the cancel");
                    int before = p.Result.Units;
                    cancel.Request();
                    bool stopped = p.Step(0f);
                    Check(stopped && p.Done, where, "Step did not stop on the cancel");
                    Check(p.Result.Canceled, where, "Result.Canceled not set");
                    Check(p.Result.Units == before, where, $"{p.Result.Units - before} unit(s) ran after the cancel (want 0: the flag is polled before each unit)");
                    Check(!p.Reselect(input.Knobs), where, "Reselect accepted a canceled scan's field");
                    input.Cancel = null;
                    AutoPlanResult again = p.Run(input);
                    Check(!again.Canceled && again.Fingerprint == want, where,
                        $"the next fresh run on the same planner {again.Fingerprint:X16} != uncanceled {want:X16}");
                }

                // From another thread while Run works on a worker (the tool's path): the worker returns Canceled within about one unit
                var cross = new AutoPlanCancel();
                input.Cancel = cross;
                var q = new SkiRunAutoPlanner();
                Task<AutoPlanResult> job = Task.Run(() => q.Run(input));
                while (!job.IsCompleted && q.ProgressVolatile < 0.3f)
                {
                }
                var sw = System.Diagnostics.Stopwatch.StartNew();
                cross.Request();
                AutoPlanResult r = job.Result;
                double latency = sw.Elapsed.TotalMilliseconds;
                string w = $"worker D-5 {mode} cross-thread";
                if (!r.Canceled)
                    Console.WriteLine($"INFO  {w}: the worker finished before the cancel landed (no latency figure)");
                else
                {
                    Console.WriteLine($"INFO  {w}: canceled after {r.Units} of {units} units, returned {latency:F2} ms after the request " +
                                      $"(longest unit {full.MaxUnitMs:F2} ms)");
                    Check(latency <= Math.Max(250.0, 4.0 * full.MaxUnitMs), w, $"the worker took {latency:F0} ms to stop");
                }
                input.Cancel = null;
                Check(q.Run(input).Fingerprint == want, w, "the next fresh run after a cross-thread cancel differs");
            }
        }

        /// <summary>ProgressVolatile read from the main thread while the worker runs: never decreasing, 1 when done.</summary>
        private static void WorkerProgress()
        {
            AutoPlanInput input = Input(Terrain.Alpine(2u), Terrain.AlpineRegion(false));
            input.Mode = AutoPlanMode.Resort;
            var p = new SkiRunAutoPlanner();
            Task<AutoPlanResult> job = Task.Run(() => p.Run(input));
            float last = 0f;
            int reads = 0, backwards = 0;
            while (!job.IsCompleted)
            {
                float v = p.ProgressVolatile;
                if (v < last)
                    backwards++;
                last = Math.Max(last, v);
                reads++;
            }
            job.Wait();
            Check(backwards == 0, "worker progress", $"ProgressVolatile went backwards {backwards} time(s) in {reads} read(s)");
            Check(Math.Abs(p.ProgressVolatile - 1f) < 1e-6f, "worker progress", $"ProgressVolatile {p.ProgressVolatile} after the run (want 1)");
        }

        /// <summary>The published copy (AutoPlanResult.Snapshot) stays as it was while the planner runs again (a Reselect and a whole new
        /// scan), in both modes: lists, counters, fingerprint, the shared AutoRun objects, and the deep-copied ResortPlan.</summary>
        private static void WorkerSnapshotCopy()
        {
            float2[] region = Terrain.Square(0f, 0f, 480f);
            HeightFn f = Terrain.Fbm(4u, 0.32, 70.0);
            foreach (AutoPlanMode mode in new[] { AutoPlanMode.Runs, AutoPlanMode.Resort })
            {
                string where = $"worker snapshot {mode}";
                AutoPlanInput input = Input(f, region);
                input.Mode = mode;
                var p = new SkiRunAutoPlanner();
                AutoPlanResult live = p.Run(input);
                AutoPlanResult pub = live.Snapshot();
                Check(!ReferenceEquals(pub, live) && !ReferenceEquals(pub.Runs, live.Runs) && !ReferenceEquals(pub.BandCount, live.BandCount),
                    where, "the copy shares its containers with the live result");
                ulong fp = pub.Fingerprint;
                int runs = pub.Runs.Count, clusters = pub.Clusters.Count, junctions = pub.Junctions.Count, units = pub.Units;
                var bands = (int[])pub.BandCount.Clone();
                AutoRun first = runs > 0 ? pub.Runs[0] : null;
                float2[] firstRing = first?.Ring;
                Check(runs > 0, where, "the fixture has no runs (the check proves nothing)");
                if (mode == AutoPlanMode.Resort)
                    Check(pub.Resort != null && !ReferenceEquals(pub.Resort, live.Resort) && !ReferenceEquals(pub.Resort.Pods, live.Resort.Pods) &&
                          !ReferenceEquals(pub.Resort.Lifts, live.Resort.Lifts) && !ReferenceEquals(pub.Resort.MountainMix, live.Resort.MountainMix),
                        where, "the ResortPlan is not deep-copied");

                // Run again on the same planner: a Reselect (another seed) to the end, then a whole new scan of other terrain
                AutoPlanKnobs k = input.Knobs;
                k.Seed = SkiRunAutoPlanner.SeedOf(region, 5);
                Check(p.Reselect(k), where, "Reselect refused");
                while (!p.Step(float.PositiveInfinity))
                {
                }
                Check(p.Result.Fingerprint != fp, where, "the reselected result equals the first (the check proves nothing)");
                p.Run(Input(Terrain.Alpine(3u), Terrain.AlpineRegion(true)));

                Check(pub.Fingerprint == fp && pub.Runs.Count == runs && pub.Clusters.Count == clusters && pub.Junctions.Count == junctions &&
                      pub.Units == units, where, "the published copy changed when the planner ran again");
                for (int b = 0; b < bands.Length; b++)
                    Check(pub.BandCount[b] == bands[b], where, $"BandCount[{b}] changed");
                Check(first == null || (ReferenceEquals(pub.Runs[0], first) && ReferenceEquals(first.Ring, firstRing)), where,
                    "the published run object was replaced or rewritten");
                Check(pub.Status == AutoPlanStatus.Ok && !pub.Canceled, where, $"status {pub.Status}, canceled {pub.Canceled}");
                if (mode == AutoPlanMode.Resort)
                    Check(pub.Resort != null && !ReferenceEquals(pub.Resort, p.Result.Resort), where, "the published ResortPlan is the live one");
            }
        }
    }
}
