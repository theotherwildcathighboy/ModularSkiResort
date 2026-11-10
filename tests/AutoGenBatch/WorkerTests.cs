// Worker suite (port round R2-a): the tool's scan job (QuickRunsScanJob, the runs-first planner and the realizer in one call on the
// tool's worker thread, QuickRunsToolSystem.Worker.cs). It tests these cases on the synthetic golden regions (RfGolden):
// - a job inside Task.Run equals a job on the calling thread (plan hash = the recorded golden, result fingerprint equal),
// - a cancel from another thread ends the job within CancelBoundMs with Canceled set and no result. A fresh job then gives the golden,
// - the job's progress never goes back and ends at 1,
// - the snapshot contract: a published result does not change when a later job runs. A job from the cached plan gives the same result,
// - a Kinds change realizes the cached plan again (the plan hash stays),
// - empty planner inputs keep the golden hash. A blocked disc moves the plan and no run, lift or connector enters it,
// - a dump (QuickRunsScanDump) read back plans and realizes to the same hashes (the rf-replay contract).
// The planner object itself (two threads, cancel per pass, progress over the pass list) is RunsFirstPlannerTests.cs.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class Program
    {
        private static void WorkerTests()
        {
            var cases = V2Fixtures.Synthetic();
            V2Case alpine = cases.First(x => x.Id == "Alpine1 full");
            ulong want = RfGolden.First(r => r.Id == alpine.Id).Hash;
            HeightSnapshot snap = alpine.Snap();
            AutoPlanInput Fresh() => JobInput(snap, alpine.Region);

            // 1. Calling thread == worker
            var here = new QuickRunsScanJob(Fresh(), null, null, null, "w-here");
            here.Run();
            string where = "worker job threads";
            Check(here.Result != null && here.Result.Status == AutoPlanStatus.Ok, where, $"status {here.Result?.Status}");
            Check(here.Plan != null && here.Plan.Hash == want, where, $"plan {here.Plan?.Hash:x16} != golden {want:x16}");
            Check(here.Result?.Resort != null, where, "no ResortPlan published");
            Check(here.Progress == 1f, where, $"progress {here.Progress} after Run");
            var worker = new QuickRunsScanJob(Fresh(), null, null, null, "w-worker");
            Task.Run(worker.Run).Wait();
            Check(worker.Plan != null && worker.Plan.Hash == want, where, $"worker plan {worker.Plan?.Hash:x16} != golden {want:x16}");
            Check(worker.Result != null && worker.Result.Fingerprint == here.Result.Fingerprint, where,
                $"worker result {worker.Result?.Fingerprint:x16} != calling thread {here.Result.Fingerprint:x16}");
            Console.WriteLine($"REPORT worker job {alpine.Id}: plan {here.Plan.Hash:x16}, result {here.Result.Fingerprint:x16}, " +
                $"checks {here.PrecheckMs:F0} ms, plan {here.Plan.PlanMs:F0} ms, realize {here.RealizeMs:F0} ms, total {here.TotalMs:F0} ms");

            WorkerCancel(snap, alpine.Region, want);
            WorkerProgress(snap, alpine.Region);
            WorkerSnapshotContract(snap, alpine.Region, here);
            WorkerInputs(snap, alpine.Region, want);
            WorkerDump(here);
        }

        private static AutoPlanInput JobInput(HeightSnapshot snap, float2[] ring)
        {
            AutoPlanInput input = RealizerHost.InputOf(snap, ring);
            input.Cancel = new AutoPlanCancel();
            return input;
        }

        /// <summary>A cancel from another thread while the planner works: the job ends within CancelBoundMs, Canceled, no result. Then a
        /// fresh job on the same input gives the golden (nothing left behind).</summary>
        private static void WorkerCancel(HeightSnapshot snap, float2[] ring, ulong want)
        {
            const string where = "worker job cancel";
            foreach (float at in new[] { 0.05f, 0.4f })
            {
                AutoPlanInput input = JobInput(snap, ring);
                var job = new QuickRunsScanJob(input, null, null, null, "w-cancel");
                Task task = Task.Run(job.Run);
                var wait = Stopwatch.StartNew();
                while (job.Progress < at * QuickRunsScanJob.PlanShare && !task.IsCompleted && wait.ElapsedMilliseconds < 60000)
                    Thread.Sleep(1);
                if (task.IsCompleted)
                {
                    Check(false, where, $"the job ended before {at:P0} of the plan (it is too fast to cancel)");
                    continue;
                }
                var sw = Stopwatch.StartNew();
                input.Cancel.Request();
                task.Wait();
                double ms = sw.Elapsed.TotalMilliseconds;
                Check(job.Canceled, where, $"at {at:P0}: not Canceled");
                Check(job.Result == null, where, $"at {at:P0}: a canceled job published a result");
                Check(ms <= CancelBoundMs, where, $"at {at:P0}: {ms:F0} ms from the request to the end > {CancelBoundMs} ms");
                Console.WriteLine($"REPORT worker job cancel at {at:P0} of the plan: {ms:F1} ms");
            }
            var fresh = new QuickRunsScanJob(JobInput(snap, ring), null, null, null, "w-after-cancel");
            Task.Run(fresh.Run).Wait();
            Check(fresh.Plan != null && fresh.Plan.Hash == want, where, $"the job after a cancel {fresh.Plan?.Hash:x16} != golden {want:x16}");
        }

        /// <summary>The progress read from another thread never goes back and ends at 1.</summary>
        private static void WorkerProgress(HeightSnapshot snap, float2[] ring)
        {
            const string where = "worker job progress";
            var job = new QuickRunsScanJob(JobInput(snap, ring), null, null, null, "w-progress");
            Task task = Task.Run(job.Run);
            float last = 0f;
            int reads = 0, back = 0;
            while (!task.IsCompleted)
            {
                float p = job.Progress;
                if (p < last)
                    back++;
                last = math.max(last, p);
                reads++;
                Thread.Sleep(1);
            }
            task.Wait();
            Check(back == 0, where, $"{back} of {reads} reads went back");
            Check(job.Progress == 1f, where, $"ends at {job.Progress}");
            Check(reads > 10, where, $"only {reads} reads (the job is too fast to watch)");
        }

        /// <summary>The tool publishes the job's own result: a later job (planned or from the cache) does not change it. A job from the
        /// cached plan gives the same result. A Kinds change from the cached plan keeps the plan hash.</summary>
        private static void WorkerSnapshotContract(HeightSnapshot snap, float2[] ring, QuickRunsScanJob published)
        {
            const string where = "worker job snapshot contract";
            AutoPlanResult pub = published.Result;
            ulong pubHash = RunsFirstRealizer.Hash(pub);
            int runs = pub.Runs.Count;
            var cached = new QuickRunsScanJob(JobInput(snap, ring), null, null, published.Plan, "w-cached");
            cached.Run();
            Check(cached.FromCache, where, "the job with a cached plan planned again");
            Check(cached.Result != null && cached.Result.Fingerprint == pub.Fingerprint, where,
                $"the cached plan's result {cached.Result?.Fingerprint:x16} != the first {pub.Fingerprint:x16}");
            Check(!ReferenceEquals(cached.Result, pub), where, "two jobs share one result object");
            var again = new QuickRunsScanJob(JobInput(snap, ring), null, null, null, "w-again");
            again.Run();
            Check(RunsFirstRealizer.Hash(pub) == pubHash && pub.Runs.Count == runs, where, "a later job changed the published result");
            AutoPlanInput kinds = JobInput(snap, ring);
            AutoPlanKnobs k = kinds.Knobs;
            k.Kinds = AutoKinds.Groomed;
            kinds.Knobs = k;
            var groomed = new QuickRunsScanJob(kinds, null, null, published.Plan, "w-kinds");
            groomed.Run();
            Check(groomed.Result != null && groomed.Result.Status == AutoPlanStatus.Ok, where, $"Kinds groomed: status {groomed.Result?.Status}");
            Check(groomed.Plan.Hash == published.Plan.Hash, where, "a Kinds change moved the plan hash");
            Check(RunsFirstRealizer.Hash(pub) == pubHash, where, "a Kinds job changed the published result");
            Console.WriteLine($"REPORT worker job cache: realize only {cached.TotalMs:F0} ms (planned {published.TotalMs:F0} ms), " +
                $"Kinds groomed result {groomed.Result?.Fingerprint:x16} vs default {pub.Fingerprint:x16}");
        }

        /// <summary>Empty planner inputs keep the golden. A blocked disc (the game's blocked ground) moves the plan, and nothing of the
        /// plan enters the disc.</summary>
        private static void WorkerInputs(HeightSnapshot snap, float2[] ring, ulong want)
        {
            const string where = "worker job inputs";
            AutoPlanInput input = JobInput(snap, ring);
            RunsFirstInputs empty = QuickRunsScanJob.InputsFrom(input, false);
            Check(empty != null && empty.Empty, where, "a fixture input with no obstacle, lift, water or access gives non-empty inputs");
            var e = new QuickRunsScanJob(input, null, empty, null, "w-empty");
            e.Run();
            Check(e.Plan != null && e.Plan.Hash == want, where, $"empty inputs {e.Plan?.Hash:x16} != golden {want:x16}");
            Check(e.Plan != null && e.Plan.BlockedCells == 0, where, $"empty inputs blocked {e.Plan?.BlockedCells} cells");

            // A disc of 150 m around the plan's first lift bottom: the plan must move away from it
            RunsFirstResult golden = e.Plan.Plan;
            float2 c = golden.Lifts[0].Bottom.xz;
            const float r = 150f;
            var blocked = new RunsFirstInputs();
            blocked.Blocked.Add(new RunsFirstInputs.Shape { Pts = new[] { c }, ClearM = r });
            var b = new QuickRunsScanJob(JobInput(snap, ring), null, blocked, null, "w-blocked");
            b.Run();
            Check(b.Plan != null && b.Plan.Hash != want, where, "a blocked disc left the plan unchanged");
            Check(b.Plan != null && b.Plan.BlockedCells > 0, where, "a blocked disc blocked no cell");
            if (b.Plan == null)
                return;
            RunsFirstResult p = b.Plan.Plan;
            float tol = r - 12f;   // one planner cell of slack at the disc's edge
            int inside = 0;
            void Pts(IEnumerable<float2> pts)
            {
                foreach (float2 q in pts)
                    if (math.distance(q, c) < tol)
                        inside++;
            }
            foreach (RunsFirstResult.Run run in p.Runs)
                Pts(run.Pts);
            foreach (List<float2> con in p.Connectors)
                Pts(con);
            foreach (RunsFirstResult.Lift l in p.Lifts)
                Pts(new[] { l.Bottom.xz, l.Top.xz });
            Check(inside == 0, where, $"{inside} plan point(s) inside the blocked disc");
            Console.WriteLine($"REPORT worker job inputs: blocked disc {r} m, {b.Plan.BlockedCells} of {b.Plan.OpenCells} cells, " +
                $"plan {b.Plan.Hash:x16} ({p.Runs.Count} runs, {p.Lifts.Count} lifts), 0 points inside: {inside == 0}");
        }

        /// <summary>The dump of a published scan, read back and run again, gives the same plan and result (rf-replay).</summary>
        private static void WorkerDump(QuickRunsScanJob published)
        {
            const string where = "worker job dump";
            var head = new QuickRunsScanDump.Header
            {
                Tag = published.Id, Bare = published.PlannerInputs == null, PlanHash = published.Plan.Hash,
                ResultHash = published.Result.Fingerprint, OptionsPrint = published.Options.Fingerprint(), Status = published.Result.Status.ToString(),
            };
            var ms = new MemoryStream();
            QuickRunsScanDump.Write(ms, head, published.Input, published.Options);
            ms.Position = 0;
            AutoPlanInput input = QuickRunsScanDump.Read(ms, out RunsFirstOptions options, out QuickRunsScanDump.Header back);
            Check(back.PlanHash == head.PlanHash && back.ResultHash == head.ResultHash && back.Bare == head.Bare && back.Tag == head.Tag, where,
                "the header did not round-trip");
            Check(back.UnknownOptions.Count == 0, where, $"unknown options: {string.Join(", ", back.UnknownOptions)}");
            Check(options.Fingerprint() == head.OptionsPrint, where, $"options {options.Fingerprint():x16} != {head.OptionsPrint:x16}");
            input.Cancel = new AutoPlanCancel();
            var job = new QuickRunsScanJob(input, options, QuickRunsScanJob.InputsFrom(input, back.Bare), null, "w-replay");
            job.Run();
            Check(job.Plan != null && job.Plan.Hash == head.PlanHash, where, $"replay plan {job.Plan?.Hash:x16} != {head.PlanHash:x16}");
            Check(job.Result != null && job.Result.Fingerprint == head.ResultHash, where,
                $"replay result {job.Result?.Fingerprint:x16} != {head.ResultHash:x16}");
            Console.WriteLine($"REPORT worker job dump: {ms.Length / 1024} KiB, replay plan {job.Plan?.Hash:x16}, result {job.Result?.Fingerprint:x16}");
        }
    }
}
