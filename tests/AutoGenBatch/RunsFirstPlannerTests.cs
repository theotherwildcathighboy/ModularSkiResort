// The planner object (port round R1-a): RunsFirstPlanner holds the state of its one plan, so it must
// - plan two regions at the same time on two threads and give each its recorded hash (no shared state),
// - return null soon after a cancel request (CancelBoundMs), and leave nothing behind: a fresh plan gives the recorded hash,
// - report a progress that never goes back and ends at 1000, over the full pass list.
// The regions are the synthetic golden cases (RunsFirstGolden.cs, RfGolden). The largest real region's bound is measured with
// "rf-cancel" on the big set (docs: port/r1a).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class Program
    {
        private const double CancelBoundMs = 250.0;
        private const int PassCount = 37;   // RunsFirstPlanner.PassCount: the named passes (EngineNotes/RunsFirstPlanner.md)

        private static void RunsFirstPlannerTests()
        {
            var cases = V2Fixtures.Synthetic();
            (HeightSnapshot Snap, float2[] Ring, ulong Want) Case(string id)
            {
                V2Case c = cases.First(x => x.Id == id);
                return (c.Snap(), c.Region, RfGolden.First(r => r.Id == id).Hash);
            }
            var a = Case("Alpine1 full");
            var b = Case("Benched1 full");

            // (a) two planners, two threads, at the same time
            {
                RunsFirstResult ra = null, rb = null;
                Exception ea = null, eb = null;
                using (var go = new ManualResetEventSlim(false))
                {
                    var ta = new Thread(() => { go.Wait(); try { ra = new RunsFirstPlanner().Plan(a.Snap, a.Ring, new RunsFirstOptions()); } catch (Exception e) { ea = e; } });
                    var tb = new Thread(() => { go.Wait(); try { rb = new RunsFirstPlanner().Plan(b.Snap, b.Ring, new RunsFirstOptions()); } catch (Exception e) { eb = e; } });
                    ta.Start();
                    tb.Start();
                    go.Set();
                    ta.Join();
                    tb.Join();
                }
                string where = "RunsFirstPlanner parallel";
                Check(ea == null && eb == null, where, $"a plan threw: {ea?.Message} {eb?.Message}");
                Check(ra != null && ra.Hash() == a.Want, where, $"Alpine1 on its thread {ra?.Hash():x16} != recorded {a.Want:x16}");
                Check(rb != null && rb.Hash() == b.Want, where, $"Benched1 on its thread {rb?.Hash():x16} != recorded {b.Want:x16}");
                Console.WriteLine($"REPORT rf parallel: Alpine1 {ra?.Hash():x16}, Benched1 {rb?.Hash():x16} planned at the same time");
            }

            // (c) progress: sampled from this thread while the plan runs on another
            long planMs;
            {
                var planner = new RunsFirstPlanner();
                RunsFirstResult res = null;
                var sw = Stopwatch.StartNew();
                var worker = new Thread(() => res = planner.Plan(a.Snap, a.Ring, new RunsFirstOptions()));
                worker.Start();
                var samples = new List<int>();
                while (worker.IsAlive)
                {
                    samples.Add(planner.ProgressVolatile);
                    Thread.Sleep(1);
                }
                worker.Join();
                planMs = sw.ElapsedMilliseconds;
                samples.Add(planner.ProgressVolatile);
                int back = 0;
                for (int i = 1; i < samples.Count; i++)
                    if (samples[i] < samples[i - 1])
                        back++;
                int distinct = samples.Distinct().Count();
                string where = "RunsFirstPlanner progress";
                Check(res != null && res.Hash() == a.Want, where, "the plan differs from the recorded one");
                Check(back == 0, where, $"progress went back {back} time(s) in {samples.Count} samples");
                Check(samples[samples.Count - 1] == 1000, where, $"progress ends at {samples[samples.Count - 1]}, not 1000");
                Check(distinct >= 10, where, $"only {distinct} distinct progress values in {samples.Count} samples");
                Check(planner.PassTimes.Count == PassCount, where, $"{planner.PassTimes.Count} passes timed, not {PassCount}");
                Check(planner.Log.SequenceEqual(res?.Log ?? new List<string>()), where, "the planner's log is not the plan's log (no debug option is on)");
                Console.WriteLine($"REPORT rf progress: {samples.Count} samples, {distinct} distinct values, 0 to {samples[samples.Count - 1]}, {planner.PassTimes.Count} passes, {planMs} ms");
            }

            // (b) cancel at three points of the plan, then a fresh plan
            {
                string where = "RunsFirstPlanner cancel";
                var lat = new List<double>();
                foreach (double at in new[] { 0.2, 0.5, 0.8 })
                {
                    var cancel = new AutoPlanCancel();
                    var planner = new RunsFirstPlanner();
                    RunsFirstResult res = null;
                    bool done = false;
                    var worker = new Thread(() => { res = planner.Plan(a.Snap, a.Ring, new RunsFirstOptions(), cancel); done = true; });
                    worker.Start();
                    Thread.Sleep((int)(planMs * at));
                    var w = Stopwatch.StartNew();
                    cancel.Request();
                    worker.Join();
                    double ms = w.Elapsed.TotalMilliseconds;
                    if (res != null)
                    {
                        Console.WriteLine($"REPORT rf cancel at {at:P0}: the plan finished before the request");
                        continue;
                    }
                    lat.Add(ms);
                    Check(done, where, "the worker did not finish");
                    Check(ms < CancelBoundMs, where, $"returned {ms:F1} ms after the request at {at:P0} (bound {CancelBoundMs} ms)");
                }
                Check(lat.Count > 0, where, "no plan was canceled (every plan finished first)");
                var pre = new AutoPlanCancel();
                pre.Request();
                Check(new RunsFirstPlanner().Plan(a.Snap, a.Ring, new RunsFirstOptions(), pre) == null, where, "a plan canceled before it started returned a plan");
                ulong fresh = new RunsFirstPlanner().Plan(a.Snap, a.Ring, new RunsFirstOptions()).Hash();
                Check(fresh == a.Want, where, $"a fresh plan after the cancels {fresh:x16} != recorded {a.Want:x16}");
                var once = new RunsFirstPlanner();
                once.Plan(b.Snap, b.Ring, new RunsFirstOptions());
                bool threw = false;
                try
                {
                    once.Plan(b.Snap, b.Ring, new RunsFirstOptions());
                }
                catch (InvalidOperationException)
                {
                    threw = true;
                }
                Check(threw, where, "a planner planned twice");
                Console.WriteLine($"REPORT rf cancel: {lat.Count} canceled plan(s), latency max {(lat.Count > 0 ? lat.Max() : 0):F1} ms ({string.Join(", ", lat.Select(x => x.ToString("F1")))}), fresh plan {fresh:x16}");
            }
        }
    }
}
