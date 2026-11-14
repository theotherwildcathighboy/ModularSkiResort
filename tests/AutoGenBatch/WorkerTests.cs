// Worker suite (port round R2-a): the tool's scan job (QuickRunsScanJob, the runs-first planner and the realizer in one call on the
// tool's worker thread, QuickRunsToolSystem.Worker.cs). It tests these cases on the synthetic golden regions (RfGolden):
// - a job inside Task.Run equals a job on the calling thread (plan hash = the recorded golden, result fingerprint equal),
// - a cancel from another thread ends the job within CancelBoundMs with Canceled set and no result. A fresh job then gives the golden,
// - the job's progress never goes back and ends at 1,
// - the snapshot contract: a published result does not change when a later job runs. A job from the cached plan gives the same result,
// - a Kinds change realizes the cached plan again (the plan hash stays),
// - empty planner inputs keep the golden hash. A blocked disc moves the plan and no run, lift or connector enters it,
// - existing stations (post-release round 1, T171): the lot of a station from its stub tip, a row with no line still gives its
//   stations, and no planned station lot overlaps the lot of a station the game already has,
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
            WorkerStations(snap, ring, golden);
        }

        /// <summary>T171. The lot of an existing station from its stub tip (QuickRunsScanJob.StationSpot), and a plan made around the
        /// stations of the golden plan's first lift as if the game had them already (placed, no line yet: a census row with no chord):
        /// no planned station lot overlaps one of them, and no two planned lots overlap.</summary>
        private static void WorkerStations(HeightSnapshot snap, float2[] ring, RunsFirstResult golden)
        {
            const string where = "worker job stations";
            // the lot from the tips: a detachable (stub 16 m, lot half diagonal 17.9 m) climbing +z
            var line = new PinnedLift { TypeIndex = 1, Bottom = new float3(100f, 0f, 200f), Top = new float3(100f, 0f, 700f) };
            RunsFirstInputs.Station sb = QuickRunsScanJob.StationSpot(line, false, true), st = QuickRunsScanJob.StationSpot(line, true, true);
            float stub = Math.Abs(SkiCatalog.StationOf(1, StationRole.Base).StubTipLocalZ), lot = PlanGeometry.StationHalfDiagonalOf(1);
            Check(math.distance(sb.Xz, new float2(100f, 200f - stub)) < 0.01f && math.distance(st.Xz, new float2(100f, 700f + stub)) < 0.01f, where,
                $"lot centers ({sb.Xz.x:F1}, {sb.Xz.y:F1}) / ({st.Xz.x:F1}, {st.Xz.y:F1}): want the tips moved {stub:F0} m back out along the line");
            Check(Math.Abs(sb.RadiusM - lot) < 0.01f && Math.Abs(st.RadiusM - lot) < 0.01f, where, $"lot radius {sb.RadiusM:F1} / {st.RadiusM:F1}, want {lot:F1}");
            RunsFirstInputs.Station lone = QuickRunsScanJob.StationSpot(line, false, false);
            Check(math.distance(lone.Xz, line.Bottom.xz) < 0.01f && Math.Abs(lone.RadiusM - (lot + stub)) < 0.01f, where,
                $"a station with no line: center ({lone.Xz.x:F1}, {lone.Xz.y:F1}) radius {lone.RadiusM:F1}, want the tip and {lot + stub:F1} m");

            // the golden plan's first lift as two placed stations with no line (no chord): the planner wants that line again
            RunsFirstResult.Lift g0 = golden.Lifts[0];
            byte type = RunsFirstRealizer.TypeIndexOf(g0.Type);
            AutoPlanInput input = JobInput(snap, ring);
            input.Pinned = new List<PinnedLift> { new PinnedLift { Key = 1u, TypeIndex = type, Bottom = g0.Bottom, Top = g0.Top } };
            RunsFirstInputs inp = QuickRunsScanJob.InputsFrom(input, false);
            Check(inp != null && inp.Stations.Count == 2 && inp.Blocked.Count == 2, where,
                $"a row with no chord gave {inp?.Stations.Count} station(s) and {inp?.Blocked.Count} blocked shape(s), want 2 and 2");
            var job = new QuickRunsScanJob(input, null, inp, null, "w-stations");
            job.Run();
            Check(job.Plan != null && job.Plan.Plan.Lifts.Count > 0, where, "no plan around the existing stations");
            if (job.Plan == null || inp == null)
                return;
            RunsFirstResult p = job.Plan.Plan;
            int OnExisting(RunsFirstResult plan, out float nearest)
            {
                int n = 0;
                nearest = float.PositiveInfinity;
                foreach (RunsFirstResult.Lift l in plan.Lifts)
                    foreach (float2 q in new[] { l.Bottom.xz, l.Top.xz })
                        foreach (RunsFirstInputs.Station s in inp.Stations)
                        {
                            float gap = math.distance(q, s.Xz) - s.RadiusM - PlanGeometry.StationHalfDiagonalOf(RunsFirstRealizer.TypeIndexOf(l.Type));
                            nearest = Math.Min(nearest, gap);
                            if (gap < 0f)
                                n++;
                        }
                return n;
            }
            int onExisting = OnExisting(p, out float nearestGap);
            Check(onExisting == 0, where, $"{onExisting} planned station lot(s) overlap an existing station's lot (nearest gap {nearestGap:F1} m)");
            Check(LotOverlaps(p) == 0, where, $"{LotOverlaps(p)} pair(s) of planned station lots overlap");
            // The same stations the way the tool passed them before this round (a report, no check): a blocked disc of the lot's size
            // around each stub tip, and no station list
            var old = new RunsFirstInputs();
            foreach (float3 tip in new[] { g0.Bottom, g0.Top })
                old.Blocked.Add(new RunsFirstInputs.Shape { Pts = new[] { tip.xz }, ClearM = PlanGeometry.StationHalfDiagonalOf(type) });
            var before = new QuickRunsScanJob(JobInput(snap, ring), null, old, null, "w-stations-old");
            before.Run();
            int oldOn = before.Plan != null ? OnExisting(before.Plan.Plan, out float oldGap) : -1;
            Console.WriteLine($"REPORT worker job stations: {p.Lifts.Count} lifts around 2 existing stations, nearest lot gap {nearestGap:F0} m, plan {job.Plan.Hash:x16}; " +
                $"the inputs of before T171 (a disc at each stub tip): {oldOn} planned lot(s) on an existing lot");
        }

        /// <summary>T171: the pairs of stations of two planned lifts (any ends) whose lot discs overlap.</summary>
        internal static int LotOverlaps(RunsFirstResult p)
        {
            int n = 0;
            for (int a = 0; a < p.Lifts.Count; a++)
                for (int b = 0; b < a; b++)
                {
                    float need = PlanGeometry.StationHalfDiagonalOf(RunsFirstRealizer.TypeIndexOf(p.Lifts[a].Type)) +
                        PlanGeometry.StationHalfDiagonalOf(RunsFirstRealizer.TypeIndexOf(p.Lifts[b].Type));
                    foreach (float2 x in new[] { p.Lifts[a].Bottom.xz, p.Lifts[a].Top.xz })
                        foreach (float2 y in new[] { p.Lifts[b].Bottom.xz, p.Lifts[b].Top.xz })
                            if (math.distance(x, y) < need)
                                n++;
                }
            return n;
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
