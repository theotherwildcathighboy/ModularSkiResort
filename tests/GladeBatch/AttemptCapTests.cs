// Measures the worst case of the glade planner on a giant, saturated run and prints it. The test asserts nothing about speed.
// A layer's attempt budget is min(target * AttemptsPerTree, MaxAttemptsPerLayer). The tool plans with AttemptsPerTree = 60 and
// MaxPlant = int.MaxValue on a full replan (Program.cs). In a saturated run, stand + MinSpacing leave no room for the target.
// Such a run burns up to the whole cap per layer on the main thread. Cases:
// - sat.2000x2000 glade (400 ha, target 50,000, budget = the 200,000 cap)
// - the same +belt (two layers)
// - field.400x2000 (80 ha at the 157.3 / ha stream, budget 755k, not saturated)
// - sat.400x2000 (12 m spacing)
// Output: `INFO  D5 attempts  <case>: attempts A (budget B), planted N of target T, saturated S, mean/max ms over R plans`.

using System;
using System.Diagnostics;
using System.Globalization;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace GladeBatch
{
    internal static partial class Program
    {
        private static void AttemptCapReport()
        {
            double2 zero = new double2(0, 0);
            Fixture giant = Fx("giant2000x2000", "giant", MakeRing(D2(0, 0, 2000, 0, 2000, 2000, 0, 2000), 0, zero), null, null);
            Fixture field = Fx("field400x2000", "field", MakeRing(D2(0, 0, 400, 0, 400, 2000, 0, 2000), 0, zero), null, null);

            // 1 + 2: the saturated giant, glade only and glade + belt.
            GladeParams sat = Defaults();
            sat.Mode = GladeMode.Glade;
            sat.GladeDensity = 1f / 8f;
            sat.MaxPlant = int.MaxValue;
            sat.AttemptsPerTree = 60;
            AttemptCase(giant, "sat.2000x2000 glade", sat, 3);

            GladeParams satBelt = sat;
            satBelt.Mode = GladeMode.Glade | GladeMode.Belt;
            satBelt.BeltDensity = 1f / 8f;
            AttemptCase(giant, "sat.2000x2000 glade+belt", satBelt, 3);

            // 3: the field fixture at the level-8 stream (157.3 / ha = 0.01573 per m2).
            GladeParams fieldP = Defaults();
            fieldP.Mode = GladeMode.Glade;
            fieldP.GladeDensity = 0.01573f;
            fieldP.MaxPlant = int.MaxValue;
            fieldP.AttemptsPerTree = 60;
            AttemptCase(field, "field.400x2000 level-8 stream", fieldP, 5);

            // 4: a smaller run that saturates early.
            GladeParams satSmall = sat;
            satSmall.MinSpacing = 12f;
            AttemptCase(field, "sat.400x2000 spacing 12", satSmall, 3);
        }

        private static void AttemptCase(Fixture f, string label, GladeParams p, int reps)
        {
            float3[] none = new float3[0];
            bool[] noThin = new bool[0];
            GladePlan plan = new GladePlan();
            Run(f, PlanSeed(f, 0), p, none, noThin, plan);   // warm-up / JIT
            double sum = 0, max = 0;
            for (int r = 0; r < reps; r++)
            {
                Stopwatch sw = Stopwatch.StartNew();
                Run(f, PlanSeed(f, r), p, none, noThin, plan);
                sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds;
                sum += ms;
                if (ms > max)
                    max = ms;
            }
            long budgetGlade = Math.Min((long)plan.TargetGlade * p.AttemptsPerTree, 200000L);
            long budgetBelt = (p.Mode & GladeMode.Belt) != 0 ? Math.Min((long)plan.TargetBelt * p.AttemptsPerTree, 200000L) : 0;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "INFO  D5 attempts  {0}: attempts {1} (budget {2}{3}), planted {4} (glade target {5}{6}), saturated {7}, mean {8:F1} ms, max {9:F1} ms over {10} plans",
                label, plan.Attempts, budgetGlade, budgetBelt > 0 ? " + belt " + budgetBelt : "", plan.Planted.Count, plan.TargetGlade,
                (p.Mode & GladeMode.Belt) != 0 ? ", belt target " + plan.TargetBelt : "", plan.Saturated, sum / reps, max, reps));
        }
    }
}
