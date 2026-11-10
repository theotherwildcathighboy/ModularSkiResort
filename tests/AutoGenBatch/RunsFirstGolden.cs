// The runs-first golden (port round R0-c): the planner plans synthetic regions (V2Fixtures.Synthetic, in git, unlike the real
// heightmaps) and each plan's hash (RunsFirstResult.Hash) must be:
// - the same on a second run in the same process (no state leaks from one region into the next, the input is not changed),
// - the same with 1 reach-scan thread as with 4 (the Parallel.For writes its results by index),
// - the recorded constant below,
// - and every plain run carries the band scorecard v7 gives it (port step R2-p1: the rater's band of its ribbon).
// A planner change that moves a plan changes the constant on purpose: run the host, review the change on the real sets
// (rf / rf-golden), then record the new value printed in the FAIL line.

using System;
using System.Linq;
using SkiResortMod.Systems;

namespace AutoGenBatch
{
    internal static partial class Program
    {
        private static readonly (string Id, ulong Hash)[] RfGolden =
        {
            ("Alpine1 full", 0x59b2ee492f4ef4c5UL),
            ("Benched1 full", 0x0166f01e2c9530c4UL),
            ("Steep1 full", 0x9725dc31047d72c7UL),
        };

        private static void RunsFirstGoldenTests()
        {
            var cases = V2Fixtures.Synthetic();
            foreach (var (id, want) in RfGolden)
            {
                V2Case c = cases.First(x => x.Id == id);
                HeightSnapshot snap = c.Snap();
                var four = new RunsFirstOptions { ScanThreads = 4 };
                var one = new RunsFirstOptions { ScanThreads = 1 };
                var sw = System.Diagnostics.Stopwatch.StartNew();
                RunsFirstResult first = RunsFirst.PlanRegion(snap, c.Region, four, id);
                long ms = sw.ElapsedMilliseconds;
                ulong a = first.Hash();
                ulong b = RunsFirst.PlanRegion(snap, c.Region, four, id).Hash();
                ulong s = RunsFirst.PlanRegion(snap, c.Region, one, id).Hash();
                string where = "RunsFirstGolden " + id;
                Check(first.Runs.Count > 0 && first.Lifts.Count > 0, where, $"an empty plan ({first.Runs.Count} runs, {first.Lifts.Count} lifts)");
                Check(a == b, where, $"second run {b:x16} != first {a:x16}");
                Check(a == s, where, $"1 thread {s:x16} != 4 threads {a:x16}");
                Check(a == want, where, $"hash {a:x16} != recorded {want:x16}");
                // port step R2-p1: a plain run's band in the plan is the band scorecard v7 gives it (the rater on its ribbon)
                int differ = RunsFirst.BandDiffers(snap, first, out int plain, out double dkm);
                Check(differ == 0, where, $"the plan's band differs from the scorecard's on {differ} of {plain} plain run(s) ({dkm:F2} km)");
                Console.WriteLine($"REPORT rf golden {id}: {a:x16}, {first.Runs.Count} runs, {first.Lifts.Count} lifts, {first.Connectors.Count} connectors, {ms} ms");
            }
            RunsFirstPlannerTests();   // the planner object: two threads, cancel, progress (RunsFirstPlannerTests.cs)
        }
    }
}
