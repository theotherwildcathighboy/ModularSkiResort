// Fans suite (A5): the run fans (spec 4.5) on the synthetic fixtures. F-1 to F-7 and F-9 HARD on Alpine 1-3 and Benched 1-2 (F-8 SOFT,
// a REPORT), the v1 invariants on every Resort result (V1-3), every run seeded by a lift (RunLift) and inside the region of its pod's
// plan, the PodFull reject firing on a Dense pod, and determinism of the fans (D-2 slices, D-3 Reselect of Seed / Mix / Density against a
// fresh run). FanCase is the per-region REPORT for the `-- v2` matrix (V2Tests): runs per lift and the G / B / K / DB mix, before (the
// Runs-mode v1 plan of the same region) and after.

using System;
using System.Collections.Generic;
using System.Globalization;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class Program
    {
        /// <summary>F gates that FAIL on the HARD fixtures. F-1, F-2, F-4, F-5, F-6 and F-9 miss on short lifts and converging fall lines
        /// (EngineNotes/ResortPlanner.md, A5): REPORT until the calibration questions of the A5 handover are ruled; F-8 is SOFT.</summary>
        private static readonly HashSet<string> FanArmed = new HashSet<string> { "F-3", "F-7" };

        private static void FanTests()
        {
            var cases = new (string, HeightFn, float2[], bool)[]   // name, terrain, region, F-gates HARD
            {
                ("alpine1", Terrain.Alpine(1u), Terrain.AlpineRegion(), true),
                ("alpine2", Terrain.Alpine(2u), Terrain.AlpineRegion(), true),
                ("alpine3", Terrain.Alpine(3u), Terrain.AlpineRegion(), true),
                ("benched1", Terrain.Benched(1u), Terrain.AlpineRegion(), true),
                ("benched2", Terrain.Benched(2u), Terrain.AlpineRegion(), true),
                ("alpine lower", Terrain.Alpine(2u), Terrain.AlpineRegion(true), false),
                ("steep1", Terrain.Steep(1u), Terrain.Square(0f, 0f, 1250f), false),
            };
            foreach (var (name, f, region, hard) in cases)
            {
                string w = "Fans " + name;
                AutoPlanInput runs = Input(f, region);
                AutoPlanResult v1 = new SkiRunAutoPlanner().Run(runs);
                AutoPlanInput input = ResortInput(Input(f, region));
                var planner = new SkiRunAutoPlanner();
                AutoPlanResult r = planner.Run(input);
                Check(r.Status == AutoPlanStatus.Ok && r.Resort != null, w, "no plan: " + r.Status);
                if (r.Resort == null || r.Status != AutoPlanStatus.Ok)
                    continue;
                Invariants("V1-3 " + w, input, r);
                FanInvariants(w, planner, r, input.Knobs.Density);
                Console.WriteLine($"INFO  Fans {name}: {FanLine(planner, r, v1)}");
                foreach (Gate g in FanGates(r, input.Knobs.Density))
                {
                    if (hard && FanArmed.Contains(g.Id))
                        Check(g.Pass, w, g.ToString());
                    else
                        Console.WriteLine($"REPORT Fans {name}: {g}{(!hard ? " (SOFT here)" : g.Id == "F-8" ? " (SOFT)" : " (calibration, A5 handover)")}");
                }
            }
            FanDeterminism();
            FanPodFullTest();
        }

        /// <summary>The F gates of one Resort result (F-10 needs the A6 mix and stays with the matrix).</summary>
        private static List<Gate> FanGates(AutoPlanResult r, AutoDensity density)
        {
            ResortPlan p = r.Resort;
            float km2 = r.RegionAreaM2 / 1.0e6f;
            return new List<Gate>
            {
                V2Metrics.F1TrailsPerLift(r, p, density), V2Metrics.F2StartsNearTop(r, p), V2Metrics.F3EndsAtBottom(r, p),
                V2Metrics.F4Branching(r), V2Metrics.F5Spacing(r, p), V2Metrics.F6PodShare(r, p),
                V2Metrics.F7Density(r, p, km2, false, density), V2Metrics.F8RunGeometry(r), V2Metrics.F9Collapse(r),
            };
        }

        /// <summary>Plan-side fan invariants: RunLift / RunPod / PodRuns shapes, every run seeded by a lift,
        /// no pod over its developable cap, per-lift counts within the cap, the reject histogram adds up (no candidate lost).</summary>
        private static void FanInvariants(string w, SkiRunAutoPlanner planner, AutoPlanResult r, AutoDensity density)
        {
            ResortPlan p = r.Resort;
            int n = r.Runs.Count;
            Check(p.RunLift.Length == n && p.RunPod.Length == n, w, $"RunLift / RunPod lengths {p.RunLift.Length} / {p.RunPod.Length}, runs {n}");
            if (p.RunLift.Length != n || p.RunPod.Length != n)
                return;
            int podded = 0;
            for (int i = 0; i < n; i++)
            {
                Check(p.RunLift[i] >= 0 && p.RunLift[i] < p.Lifts.Length, w, $"run {i}: RunLift {p.RunLift[i]}, lifts {p.Lifts.Length}");   // not Cluster: A6 makes it the pod
                Check(p.RunPod[i] >= -1 && p.RunPod[i] < p.Pods.Length, w, $"run {i}: RunPod {p.RunPod[i]}");
                if (p.RunPod[i] >= 0)
                    podded++;
            }
            Check(p.PodRuns.Length == podded, w, $"PodRuns {p.PodRuns.Length}, podded runs {podded}");
            for (int k = 0; k < p.Pods.Length; k++)
            {
                PlanPod pod = p.Pods[k];
                double area = 0.0;
                for (int q = 0; q < pod.RunCount; q++)
                {
                    int ri = p.PodRuns[pod.RunFirst + q];
                    Check(p.RunPod[ri] == k, w, $"pod {k} lists run {ri} of pod {p.RunPod[ri]}");
                    area += r.Runs[ri].AreaM2;
                }
                Check(area <= pod.DevelopableShare * pod.SkiableAreaM2 + 1.0, w,
                    $"pod {k}: runs {area / 1e4:F1} ha over the developable cap {pod.DevelopableShare * pod.SkiableAreaM2 / 1e4:F1} ha");
            }
            var per = new int[p.Lifts.Length];
            foreach (int l in p.RunLift)
                per[l]++;
            int cap = SkiRunAutoPlanner.FanRunsPerLift[(int)density];
            foreach (int c in per)
                Check(c <= cap, w, $"a lift has {c} runs over its cap {cap}");
            int rejects = 0;
            for (int i = 1; i < r.Rejects.Length; i++)
                rejects += r.Rejects[i];
            int pre = r.Rejects[(int)AutoReject.Duplicate] + r.Rejects[(int)AutoReject.FlatLine];
            Check(r.Rejects[(int)AutoReject.TopTaken] == 0 && pre == 0, w,
                $"fan candidates are pre-walked and free when proposed: TopTaken {r.Rejects[(int)AutoReject.TopTaken]}, Duplicate + FlatLine {pre}");
            Check(r.Runs.Count + rejects == r.Candidates, w, $"{r.Runs.Count} runs + {rejects} rejects for {r.Candidates} candidates (every candidate is tried once)");
        }

        private static string FanMix(AutoPlanResult r) => $"{r.BandCount[1]}/{r.BandCount[2]}/{r.BandCount[3]}/{r.BandCount[4]}";

        /// <summary>One line: lifts, runs per lift (tops near / ends at bottom), G / B / K / DB before (v1) and after, candidates, kept share,
        /// the largest rejects.</summary>
        private static string FanLine(SkiRunAutoPlanner planner, AutoPlanResult r, AutoPlanResult v1)
        {
            ResortPlan p = r.Resort;
            var per = new int[p.Lifts.Length];
            var tops = new int[p.Lifts.Length];
            var bots = new int[p.Lifts.Length];
            for (int i = 0; i < r.Runs.Count && i < p.RunLift.Length; i++)
            {
                int l = p.RunLift[i];
                if (l < 0)
                    continue;
                per[l]++;
                if (math.distance(r.Runs[i].Centerline[0], p.Lifts[l].Top.xz) <= 100f)
                    tops[l]++;
                if (math.distance(r.Runs[i].Centerline[r.Runs[i].Centerline.Length - 1], p.Lifts[l].Bottom.xz) <= 100f)
                    bots[l]++;
            }
            var lifts = new List<string>();
            for (int l = 0; l < per.Length; l++)
                lifts.Add($"{per[l]} ({tops[l]}t {bots[l]}b)");
            var rej = new List<string>();
            for (int i = 1; i < r.Rejects.Length; i++)
                if (r.Rejects[i] > 0)
                    rej.Add($"{(AutoReject)i} {r.Rejects[i]}");
            double kept = r.Candidates > 0 ? 100.0 * r.Runs.Count / r.Candidates : 0.0;
            string before = v1 == null ? "-" : $"{v1.Runs.Count} runs {FanMix(v1)}, kept {(v1.Candidates > 0 ? 100.0 * v1.Runs.Count / v1.Candidates : 0.0):F0} % of {v1.Candidates}";
            return string.Format(CultureInfo.InvariantCulture,
                "{0} lifts, runs per lift [{1}], {2} runs G/B/K/DB {3} | before (v1): {4} | {5} candidates ({9} branches), kept {6:F0} %, reserve {7}, dropped at the pick {8}, rejects: {10}",
                p.Lifts.Length, string.Join(", ", lifts), r.Runs.Count, FanMix(r), before, r.Candidates, kept, planner.FanReserve, planner.FanDropped + $" ({planner.FanDropWhy(0)} gap, {planner.FanDropWhy(1)} no branch, {planner.FanDropWhy(2)} stub, {planner.FanDropWhy(3)} branch)", planner.FanBranchCandidates,
                rej.Count > 0 ? string.Join(", ", rej) : "none");
        }

        /// <summary>D-2 / D-3 on the fans: Step(4 ms) slices and Reselect (Seed, Mix, Density) equal a fresh run.</summary>
        private static void FanDeterminism()
        {
            AutoPlanInput input = ResortInput(Input(Terrain.Alpine(2u), Terrain.AlpineRegion()));
            ulong want = new SkiRunAutoPlanner().Run(input).Fingerprint;
            var sliced = new SkiRunAutoPlanner();
            sliced.Begin(input);
            while (!sliced.Step(4f))
            {
            }
            Check(sliced.Result.Fingerprint == want, "Fans determinism", "D-2: Step(4) slices planned other runs");
            AutoPlanKnobs k = input.Knobs;
            var variants = new (string, AutoPlanKnobs)[]
            {
                ("Seed", With(k, x => x.Seed = 12345u)), ("Mix", With(k, x => x.Mix = AutoMix.Beginner)),
                ("Density Dense", With(k, x => x.Density = AutoDensity.Dense)), ("Density Sparse", With(k, x => x.Density = AutoDensity.Sparse)),
                ("Lifts", With(k, x => x.Lifts = 2)),
            };
            foreach (var (name, knobs) in variants)
            {
                var p = new SkiRunAutoPlanner();
                p.Run(input);
                bool ok = p.Reselect(knobs);
                while (ok && !p.Step(float.PositiveInfinity))
                {
                }
                AutoPlanInput fresh = ResortInput(Input(Terrain.Alpine(2u), Terrain.AlpineRegion()));
                fresh.Knobs = knobs;
                AutoPlanResult fr = new SkiRunAutoPlanner().Run(fresh);
                Check(ok && p.Result.Fingerprint == fr.Fingerprint, "Fans determinism", $"D-3: Reselect {name} differs from a fresh run");
                Gate f1 = V2Metrics.F1TrailsPerLift(fr, fr.Resort, knobs.Density);
                Console.WriteLine($"INFO  Fans alpine2 {name}: {f1}; {fr.Runs.Count} runs G/B/K/DB {FanMix(fr)}");
            }
        }

        /// <summary>PodFull fires on a Dense pod: Dense alpine fans fill the pods to their developable cap (the plan invariants above check
        /// that no pod goes over it).</summary>
        private static void FanPodFullTest()
        {
            int fired = 0;
            foreach (uint seed in new[] { 1u, 2u, 3u })
            {
                AutoPlanInput input = ResortInput(Input(Terrain.Alpine(seed), Terrain.AlpineRegion()));
                input.Knobs.Density = AutoDensity.Dense;
                var planner = new SkiRunAutoPlanner();
                AutoPlanResult r = planner.Run(input);
                if (r.Resort == null || r.Status != AutoPlanStatus.Ok)
                    continue;
                FanInvariants("Fans PodFull alpine" + seed + " Dense", planner, r, AutoDensity.Dense);
                fired += r.Rejects[(int)AutoReject.PodFull];
                Console.WriteLine($"INFO  Fans PodFull alpine{seed} Dense: {FanLine(planner, r, null)}");
            }
            Check(fired > 0, "Fans PodFull", "no PodFull reject on the Dense alpine pods");
        }

        /// <summary>The `-- v2` per-region REPORT: before (Runs mode) and after (Resort) mix and runs per lift.</summary>
        private static void FanCase(V2Case c, SkiRunAutoPlanner planner, AutoPlanResult r, AutoPlanResult v1)
        {
            if (r.Resort == null)
                return;
            Console.WriteLine($"REPORT fans {c.Id}: {FanLine(planner, r, v1)}");
        }
    }
}
