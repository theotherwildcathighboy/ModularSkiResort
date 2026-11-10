// Host mode rf-game-plans <dir> [bound] (port round 2b-f): the gate on the game's OWN plans, which replaces bit equality between the
// game (Unity's Mono) and the host (.NET 8). Float noise may give the game a sibling plan; the gate asks that the sibling is valid and as
// good, not that it is the same.
// For every *.rfscan in the folder (version 2 dumps: QuickRunsScanDump carries the game's plan):
//   - the host plans the same input (QuickRunsScanJob, as rf-replay does),
//   - the game's plan and the host's plan each get the HARD checks the host applies to its own plans: a plan with runs and lifts, the
//     plan's band of every plain run equals scorecard v7's (RunsFirst.BandDiffers), and the plan realized in the host passes the realizer
//     invariants (RealizerInvariants; the relation gate a REPORT line, as rf-realize reports it on real regions) and the way-to-the-bottom gate (RealizedBottomGate),
//   - both are scored under scorecard v7 (RunsFirstScore.Evaluate on the raw plan, as rf-golden scores, and as skied on the realized plan).
// Exit 0 when every game plan passes its HARD checks and the mean of (game raw score - host raw score) is at or over the bound
// (default -3.0); 3 when not; 1 when no version 2 dump is in the folder. The host plan's own HARD failures are reported, not gated.
// Run from the repo root: dotnet run --project tests/AutoGenBatch/AutoGenBatch.csproj -c Release -- rf-game-plans <dir> [bound]

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SkiResortMod.Systems;

namespace AutoGenBatch
{
    internal static partial class Program
    {
        private static int GamePlans(string[] args)
        {
            string dir = args.Length > 1 ? args[1] : null;
            double bound = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : -3.0;
            if (dir == null || !Directory.Exists(dir))
            {
                Console.WriteLine($"rf-game-plans: no folder '{dir}'");
                return 1;
            }
            var diffs = new List<double>();
            int hardFails = 0, files = 0;
            foreach (string path in Directory.GetFiles(dir, "*.rfscan").OrderBy(p => p, StringComparer.Ordinal))
            {
                AutoPlanInput input;
                RunsFirstOptions options;
                QuickRunsScanDump.Header head;
                using (FileStream fs = File.OpenRead(path))
                    input = QuickRunsScanDump.Read(fs, out options, out head);
                string name = Path.GetFileName(path);
                if (head.Plan == null)
                {
                    Console.WriteLine($"GAMEPLAN {name}: version {head.Version} dump, no game plan: skipped");
                    continue;
                }
                files++;
                var job = new QuickRunsScanJob(input, options, QuickRunsScanJob.InputsFrom(input, head.Bare), null, head.Tag);
                job.Run();
                if (job.Plan == null)
                {
                    Console.WriteLine($"GAMEPLAN {name}: the host made no plan (status {job.Result?.Status.ToString() ?? "none"})");
                    hardFails++;
                    continue;
                }
                var (gOk, gRaw, gSkied) = CheckAndScore($"game plan {head.Tag}", head.Plan, input);
                var (hOk, hRaw, hSkied) = CheckAndScore($"host plan {head.Tag}", job.Plan.Plan, input);
                if (!gOk)
                    hardFails++;
                double d = gRaw - hRaw;
                diffs.Add(d);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "GAMEPLAN {0} ({1}): game {2:x16} {3} runs {4} lifts, HARD {5}, raw {6:F2}, skied {7:F2} | host {8:x16} {9} runs {10} lifts, HARD {11}, " +
                    "raw {12:F2}, skied {13:F2} | game - host raw {14:+0.00;-0.00;0.00}, skied {15:+0.00;-0.00;0.00}{16}",
                    name, head.Tag, head.PlanHash, head.Plan.Runs.Count, head.Plan.Lifts.Count, gOk ? "ok" : "FAIL", gRaw, gSkied, job.Plan.Hash,
                    job.Plan.Plan.Runs.Count, job.Plan.Plan.Lifts.Count, hOk ? "ok" : "FAIL", hRaw, hSkied, d, gSkied - hSkied,
                    head.PlanHash == job.Plan.Hash ? " (same plan)" : ""));
            }
            if (files == 0)
            {
                Console.WriteLine($"GAMEPLAN: no version 2 dump in {dir}");
                return 1;
            }
            double mean = diffs.Count > 0 ? diffs.Average() : 0.0;
            bool pass = hardFails == 0 && mean >= bound;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "GAMEPLAN SUMMARY: {0} dump(s), {1} game plan(s) failing a HARD check, mean game - host raw {2:+0.00;-0.00;0.00} (bound {3:F2}): {4}",
                files, hardFails, mean, bound, pass ? "PASS" : "FAIL"));
            return pass ? 0 : 3;
        }

        /// <summary>The HARD checks of one plan (true: all pass) and its v7 scores (raw plan, as skied).</summary>
        private static (bool Ok, float Raw, float Skied) CheckAndScore(string where, RunsFirstResult plan, AutoPlanInput dumped)
        {
            int before = s_Failures;
            HeightSnapshot snap = dumped.Snapshot;
            Check(plan.Runs.Count > 0 && plan.Lifts.Count > 0, where, $"an empty plan ({plan.Runs.Count} runs, {plan.Lifts.Count} lifts)");
            int differ = RunsFirst.BandDiffers(snap, plan, out int plain, out double dkm);
            Check(differ == 0, where, $"the plan's band differs from the scorecard's on {differ} of {plain} plain run(s) ({dkm:F2} km)");
            // the realizer in the host on the dumped input (its obstacles), as the scan job realizes
            var rep = new RealizeReport();
            AutoPlanResult res = RunsFirstRealizer.Realize(plan, dumped, rep);
            Check(res.Status == AutoPlanStatus.Ok && res.Runs.Count > 0, where, $"realized: status {res.Status}, {res.Runs.Count} areas");
            RealizerInvariants(where, res, dumped, rep, false);
            RealizedBottomGate(where, plan, res, rep);
            float raw = RunsFirstScore.Evaluate(snap, dumped.Region, RealizerHost.ScoreOfRaw(plan)).Total;
            float skied = RunsFirstScore.Evaluate(snap, dumped.Region, RealizerHost.ScoreOfRealized(res, plan, true)).Total;
            return (s_Failures == before, raw, skied);
        }
    }
}
