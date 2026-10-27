// Terrain and Pods suites (A3): terraces, ridge faces, the 80 m merge rule; S-1 (pads), S-2 (pod class), S-3 (pods present), P-1
// (unit time of the Terrain / PodRoute / Pods stages at 9 km2), P-4 (planner buffers at 9 km2), D-1 / D-2 / D-3 on the pads and pods.
// TerrainCase is the per-fixture check + REPORT line: the synthetic cases run here (CI); the real heightmaps run it from V2Tests (A1's
// matrix, `-- v2`) once A1's loader is merged, and SKIP here until then.

using System;
using System.Collections.Generic;
using System.Globalization;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class Program
    {
        // The host's own band floors (S-2 recomputes the class independently of SkiRunAutoPlanner.BandOfGrade)
        private static readonly float[] HostBandFloor = { 0.06f, 0.08f, 0.25f, 0.45f, 0.70f, 1.00f };

        private static void TerrainTests()
        {
            // Terraces on a 35 % plane falling toward +z: the lower terrace sits in the base band, the upper one 60 m (merged: one pod)
            // or 110 m (its own pod, a bench, the lower pod's UpperPad) above the lower terrace's top
            float2[] region = Terrain.Rect(-500f, -600f, 500f, 600f);
            {
                ResortPlan p = TerrainRun("Terrain terraces 60 m", Terraces(0.35, (209.0, 289.0), (460.0, 560.0)), region, out _);
                if (p != null)
                {
                    Check(p.Pads.Length == 2, "Terrain terraces 60 m", "pads " + p.Pads.Length + " (2 expected)");
                    Check(p.Pods.Length == 1, "Terrain terraces 60 m", "pods " + p.Pods.Length + " (1 expected: the upper terrace merges, 80 m rule)");
                    if (p.Pods.Length == 1 && p.Pads.Length == 2)
                        Check(p.Pads[p.Pods[0].LowerPad].IsBase && p.Pads[p.Pods[0].LowerPad].MinY < p.Pads[1 - p.Pods[0].LowerPad].MinY,
                              "Terrain terraces 60 m", "the pod does not drain to the lower (base) terrace");
                }
            }
            {
                ResortPlan p = TerrainRun("Terrain terraces 110 m", Terraces(0.35, (66.0, 146.0), (460.0, 560.0)), region, out _);
                if (p != null)
                {
                    Check(p.Pads.Length == 2, "Terrain terraces 110 m", "pads " + p.Pads.Length + " (2 expected)");
                    Check(p.Pods.Length == 2, "Terrain terraces 110 m", "pods " + p.Pods.Length + " (2 expected: 105 m over the 80 m rule)");
                    if (p.Pods.Length == 2 && p.Pads.Length == 2)
                    {
                        int upperPad = p.Pads[0].MinY > p.Pads[1].MinY ? 0 : 1;
                        Check(p.Pads[upperPad].IsBench && !p.Pads[upperPad].IsBase && p.Pads[1 - upperPad].IsBase, "Terrain terraces 110 m",
                              "the upper terrace is not a bench / the lower not a base");
                        Check(p.Pods[0].LowerPad == 1 - upperPad && p.Pods[1].LowerPad == upperPad, "Terrain terraces 110 m",
                              "pods not ordered by their lower pad's height, or not one per terrace");
                        Check(p.Pods[0].UpperPad == upperPad, "Terrain terraces 110 m", "the lower pod's UpperPad is " + p.Pods[0].UpperPad + ", not the bench above it");
                        Check(p.Pods[1].UpperPad == -1, "Terrain terraces 110 m", "the top pod has an UpperPad " + p.Pods[1].UpperPad);
                    }
                }
            }
            // A ridge: two 40 % faces (35 % down the fall line, 20 % off the crest) draining onto ONE apron pad (8 % down the fall line,
            // 20 % off the crest); the crest sits on a cell column so it is a strict maximum. One pad, two pods (one per face).
            {
                const double crestX = 1.75;   // a lattice cell center (texel 2048 of the vanilla map)
                HeightFn ridge = (x, z) => 1500.0 - (z < 400.0 ? 0.35 * z : 140.0 + 0.02 * (z - 400.0)) - 0.20 * Math.Abs(x - crestX);
                ResortPlan p = TerrainRun("Terrain ridge", ridge, region, out _);
                if (p != null)
                {
                    Check(p.Pads.Length == 1, "Terrain ridge", "pads " + p.Pads.Length + " (1 expected: the apron)");
                    Check(p.Pods.Length == 2, "Terrain ridge", "pods " + p.Pods.Length + " (2 expected: one per face)");
                    if (p.Pods.Length == 2)
                    {
                        Check(p.Pods[0].LowerPad == p.Pods[1].LowerPad, "Terrain ridge", "the faces drain to different pads");
                        Check(Math.Sign(p.Pods[0].TopCenter.x - crestX) != Math.Sign(p.Pods[1].TopCenter.x - crestX), "Terrain ridge",
                              "both pod tops on one face");
                        float a0 = p.Pods[0].AreaM2, a1 = p.Pods[1].AreaM2;
                        Check(Math.Abs(a0 - a1) < 0.05f * Math.Max(a0, a1), "Terrain ridge", $"face areas {a0:F0} / {a1:F0} m2 differ by over 5 %");
                    }
                }
            }
            // A plane steeper than the pad rule everywhere: no pad, no pod, the scan still runs (the v1 runs are untouched)
            {
                ResortPlan p = TerrainRun("Terrain plane 35 %", Terrain.Plane(0.35), region, out AutoPlanResult r);
                if (p != null)
                    Check(p.Pads.Length == 0 && p.Pods.Length == 0 && r.Status == AutoPlanStatus.Ok, "Terrain plane 35 %",
                          $"pads {p.Pads.Length}, pods {p.Pods.Length}, status {r.Status}");
            }

            // The synthetic fixtures: every pad / pod invariant, S-1..S-3, REPORT lines
            foreach (uint seed in new uint[] { 1u, 2u, 3u })
                TerrainCase($"Alpine{seed} full", Input(Terrain.Alpine(seed), Terrain.AlpineRegion(false)), false, true);
            TerrainCase("Alpine1 lower", Input(Terrain.Alpine(1u), Terrain.AlpineRegion(true)), false, false);
            TerrainCase("Benched terraces", Input(Terraces(0.35, (66.0, 146.0), (460.0, 560.0)), region), true, true);

            TerrainDeterminism();
            TerrainTiming();
            Console.WriteLine("SKIP  Terrain real fixtures: run from V2Tests (`-- v2`) through TerrainCase once A1's heightmap loader is merged");
        }

        /// <summary>A plane of the given grade falling toward +z with flat terraces over the z ranges (z0, z1).</summary>
        private static HeightFn Terraces(double grade, params (double Z0, double Z1)[] flats) => (x, z) =>
        {
            double flat = 0.0;
            foreach (var (z0, z1) in flats)
                flat += Math.Max(0.0, Math.Min(z, z1) - z0) * (z > z0 ? 1.0 : 0.0);
            return 1500.0 - grade * (z + 2000.0 - flat);
        };

        private static AutoPlanInput ResortInput(AutoPlanInput i)
        {
            i.Mode = AutoPlanMode.Resort;
            return i;
        }

        /// <summary>A Resort run on f / region with the pad and pod invariants checked; null when the plan is missing.</summary>
        private static ResortPlan TerrainRun(string where, HeightFn f, float2[] region, out AutoPlanResult r)
        {
            var planner = new SkiRunAutoPlanner();
            r = planner.Run(ResortInput(Input(f, region)));
            Check(r.Resort != null, where, "no ResortPlan (" + r.Status + ")");
            if (r.Resort == null)
                return null;
            PlanInvariants(where, planner, r);
            return r.Resort;
        }

        /// <summary>Every pad / pod invariant of spec 4.1-4.2 (S-1, S-2 and the shape of the lists) on one finished Resort result.</summary>
        private static void PlanInvariants(string where, SkiRunAutoPlanner planner, AutoPlanResult r)
        {
            ResortPlan p = r.Resort;
            // S-1: pads
            for (int k = 0; k < p.Pads.Length; k++)
            {
                PlanPad pad = p.Pads[k];
                Check(pad.Id == k, where, $"pad {k} has id {pad.Id}");
                Check(pad.AreaM2 >= SkiRunAutoPlanner.PadMinAreaM2 && pad.MeanGrade < SkiRunAutoPlanner.PadMaxGrade &&
                      pad.InscribedM >= SkiRunAutoPlanner.PadMinInscribedM, where,
                      $"S-1 pad {k}: {pad.AreaM2:F0} m2, mean {pad.MeanGrade:P1}, inscribed {pad.InscribedM:F1} m");
                Check(pad.MinY <= pad.MaxY && !(pad.IsBase && pad.IsBench), where, $"pad {k}: heights / base / bench flags");
                Check(pad.GradeM3 >= 0f && pad.GradeMaxM >= 0f && !float.IsInfinity(pad.GradeM3), where, $"pad {k}: cut / fill estimate {pad.GradeM3}");
                Check((pad.Kind == PadKind.Flat) == (pad.MeanGrade < 0.08f) && (pad.Kind == PadKind.Novice) == (pad.MeanGrade >= 0.15f), where,
                      $"pad {k}: kind {pad.Kind} at {pad.MeanGrade:P1}");
            }
            if (p.Pads.Length > 0)
            {
                int lowest = 0;
                for (int k = 1; k < p.Pads.Length; k++)
                    if (p.Pads[k].MinY < p.Pads[lowest].MinY)
                        lowest = k;
                Check(p.Pads[lowest].IsBase, where, "the lowest pad is not a base pad");
            }
            Check(p.Pods.Length <= SkiRunAutoPlanner.MaxPodsOf(r.RegionAreaM2), where, $"pods {p.Pods.Length} over the cap");
            var keys = new HashSet<uint>();
            var grades = new List<float>();
            for (int k = 0; k < p.Pods.Length; k++)
            {
                PlanPod pod = p.Pods[k];
                string w = where + " pod " + k;
                Check(pod.Id == k && keys.Add(pod.Key), w, "id or key not unique");
                Check(pod.LowerPad >= 0 && pod.LowerPad < p.Pads.Length && pod.UpperPad != pod.LowerPad && pod.UpperPad < p.Pads.Length, w,
                      $"pads {pod.LowerPad} / {pod.UpperPad}");
                if (pod.LowerPad < 0 || pod.LowerPad >= p.Pads.Length)
                    continue;
                Check(p.Pads[pod.LowerPad].InscribedM >= SkiRunAutoPlanner.StationHalfDiagonalM[0], w,
                      $"S-1: lower pad inscribed {p.Pads[pod.LowerPad].InscribedM:F1} m cannot hold the cheapest station");
                Check(pod.AreaM2 >= SkiRunAutoPlanner.PodMinAreaM2 && pod.VerticalM >= SkiRunAutoPlanner.PodMinVerticalM - 1e-3f, w,
                      $"{pod.AreaM2:F0} m2, {pod.VerticalM:F0} m: under the pod minimum");
                Check(pod.SkiableAreaM2 <= pod.AreaM2 + 1f && pod.TopY > pod.BottomY && pod.ReachM > 0f, w, "areas / heights / reach");
                if (k > 0)
                {
                    PlanPod prev = p.Pods[k - 1];
                    Check(p.Pads[prev.LowerPad].MinY <= p.Pads[pod.LowerPad].MinY, w, "pods not sorted by their lower pad's height");
                }
                float shareSum = 0f;
                bool smooth = false;
                for (int b = 0; b < 7; b++)
                {
                    shareSum += pod.BandShare[b];
                    smooth |= pod.BandShare[b] > SkiRunAutoPlanner.SmoothBandShare;
                }
                Check(Math.Abs(shareSum - 1f) < 1e-3f, w, "band shares sum to " + shareSum);
                float dev = float.IsNaN(r.Resort.TreeLineY) ? 0.35f : pod.AboveTreeLine ? 0.50f : 0.30f;
                if (smooth)
                    dev = Math.Min(0.90f, dev + 0.2f);
                Check(Math.Abs(pod.DevelopableShare - dev) < 1e-6f, w, $"developable share {pod.DevelopableShare} ({dev} expected)");
                // S-2: the class is the band of the pod's 80th percentile grade, recomputed from the cells
                grades.Clear();
                Check(planner.PodGrades(k, grades) && grades.Count > 0, w, "no cells for the S-2 recompute");
                if (grades.Count == 0)
                    continue;
                grades.Sort();
                float q = grades[(int)Math.Floor(0.8 * (grades.Count - 1))];
                int band = 0;
                while (band < HostBandFloor.Length && q >= HostBandFloor[band])
                    band++;
                Check((TerrainBand)band == pod.PrimaryClass, w, $"S-2: class {pod.PrimaryClass}, the host's p80 {q:P1} gives {(TerrainBand)band}");
                Check(Math.Abs(grades.Count * Terrain4Cell - pod.SkiableAreaM2) <= 1e-5f * pod.SkiableAreaM2 + 1f, w, $"S-2: {grades.Count} cells vs {pod.SkiableAreaM2:F0} m2 skiable");
                float mean = 0f;
                foreach (float g in grades)
                    mean += g;
                Check(Math.Abs(mean / grades.Count - pod.MeanFanGrade) < 1e-3f, w, "mean fan grade");
            }
        }

        private const float Terrain4Cell = 3.5f * 3.5f;   // the vanilla texel area (every fixture here is on the vanilla lattice)

        /// <summary>One fixture: invariants, S-3 when the fixture should have pods (gates 2.2: >= 2 pods per 4 km2; a bench fixture at
        /// least one pod whose lower pad is a bench), and the REPORT line next to [E]'s bench table.</summary>
        private static void TerrainCase(string id, AutoPlanInput input, bool expectBench, bool expectPods)
        {
            string where = "Terrain " + id;
            var planner = new SkiRunAutoPlanner();
            AutoPlanResult r = planner.Run(ResortInput(input));
            if (r.Resort == null)
            {
                Console.WriteLine($"REPORT {where}: refused {r.Status} (no plan)");
                Check(!expectPods, where, "refused " + r.Status);
                return;
            }
            PlanInvariants(where, planner, r);
            ResortPlan p = r.Resort;
            int benches = 0, bases = 0, benchPods = 0;
            float largestBench = 0f, baseHa = 0f;
            foreach (PlanPad pad in p.Pads)
            {
                if (pad.IsBench)
                {
                    benches++;
                    largestBench = Math.Max(largestBench, pad.AreaM2 / 1e4f);
                }
                if (pad.IsBase)
                {
                    bases++;
                    baseHa += pad.AreaM2 / 1e4f;
                }
            }
            var classes = new int[7];
            foreach (PlanPod pod in p.Pods)
            {
                classes[(int)pod.PrimaryClass]++;
                if (p.Pads[pod.LowerPad].IsBench)
                    benchPods++;
            }
            float km2 = r.RegionAreaM2 / 1e6f;
            if (expectPods)
                Check(p.Pods.Length >= Math.Max(1, (int)Math.Floor(2f * km2 / 4f + 1e-3f)), where,
                      $"S-3: {p.Pods.Length} pods on {km2:F1} km2 (at least 2 per 4 km2)");
            if (expectBench)
                Check(benchPods > 0, where, "S-3: no pod drains to a bench");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "REPORT {0}: {1:F1} km2, mean grade {2:P0}; pads {3} (base {4}, {5:F1} ha; benches {6}, largest {7:F1} ha); pods {8} " +
                "(on a bench {9}; class F/M/B/I/A/E {10}); {11}",
                where, km2, r.MeanGrade, p.Pads.Length, bases, baseHa, benches, largestBench, p.Pods.Length, benchPods,
                ClassText(classes), PodText(p)));
        }

        private static string ClassText(int[] classes)
        {
            var parts = new string[6];
            for (int b = 0; b < 6; b++)
                parts[b] = classes[b].ToString(CultureInfo.InvariantCulture);
            return string.Join("/", parts);
        }

        private static string PodText(ResortPlan p)
        {
            var parts = new List<string>();
            foreach (PlanPod pod in p.Pods)
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1:F0} m / {2:F0} ha {3}{4}", (char)('A' + Math.Min(pod.Id, 25)),
                    pod.VerticalM, pod.SkiableAreaM2 / 1e4f, pod.PrimaryClass, pod.UpperPad >= 0 ? " +pad" : ""));
            return parts.Count == 0 ? "no pods" : string.Join(", ", parts);
        }

        /// <summary>FNV over every pad and pod field (the plan fingerprint is A6's; this one covers the terrain part only).</summary>
        private static ulong TerrainPrint(ResortPlan p)
        {
            ulong h = 14695981039346656037UL;
            void M(uint v) => h = (h ^ v) * 1099511628211UL;
            void F(float v) => M(math.asuint(v));
            foreach (PlanPad d in p.Pads)
            {
                M((uint)d.Id);
                M((uint)d.Kind);
                F(d.AreaM2); F(d.MeanGrade); F(d.MinY); F(d.MaxY); F(d.Center.x); F(d.Center.y); F(d.InscribedM); F(d.AccessM); F(d.GradeM3);
                M(d.IsBase ? 1u : 0u);
                M(d.IsBench ? 1u : 0u);
            }
            foreach (PlanPod d in p.Pods)
            {
                M(d.Key);
                M((uint)d.LowerPad);
                M((uint)d.UpperPad);
                M((uint)d.PrimaryClass);
                F(d.TopCenter.x); F(d.TopCenter.y); F(d.VerticalM); F(d.AreaM2); F(d.SkiableAreaM2); F(d.DevelopableShare); F(d.MeanFanGrade);
                foreach (float s in d.BandShare)
                    F(s);
            }
            return h;
        }

        // D-1 (rerun, rotated, reversed region), D-2 (sliced Step), D-3 (Reselect of Seed / Lifts / Home keeps the pads and pods equal to
        // a fresh run with those knobs) on the terrain part of the plan
        private static void TerrainDeterminism()
        {
            foreach (var (name, f, region) in new (string, HeightFn, float2[])[]
                     {
                         ("alpine", Terrain.Alpine(2u), Terrain.AlpineRegion(false)),
                         ("fbm", Terrain.Fbm(4u, 0.32, 70.0), Terrain.Square(0f, 0f, 480f)),
                     })
            {
                string w = "Terrain determinism " + name;
                AutoPlanInput input = ResortInput(Input(f, region));
                AutoPlanResult a = new SkiRunAutoPlanner().Run(input);
                if (a.Resort == null)
                {
                    Check(false, w, "no plan: " + a.Status);
                    continue;
                }
                ulong fp = TerrainPrint(a.Resort);
                Check(TerrainPrint(new SkiRunAutoPlanner().Run(input).Resort) == fp, w, "D-1: rerun differs");
                AutoPlanResult rot = new SkiRunAutoPlanner().Run(ResortInput(Input(f, new[] { region[2], region[3], region[0], region[1] })));
                AutoPlanResult rev = new SkiRunAutoPlanner().Run(ResortInput(Input(f, new[] { region[3], region[2], region[1], region[0] })));
                Check(rot.Resort != null && TerrainPrint(rot.Resort) == fp, w, "D-1: rotated start vertex changes the pads / pods");
                Check(rev.Resort != null && TerrainPrint(rev.Resort) == fp, w, "D-1: reversed region changes the pads / pods");
                var sliced = new SkiRunAutoPlanner();
                sliced.Begin(input);
                while (!sliced.Step(0.05f))
                {
                }
                Check(sliced.Result.Resort != null && TerrainPrint(sliced.Result.Resort) == fp && sliced.Result.Fingerprint == a.Fingerprint, w,
                      "D-2: sliced Step differs from Run");
                AutoPlanKnobs seed = input.Knobs;
                seed.Seed = SkiRunAutoPlanner.SeedOf(region, 5);
                foreach (var (kname, knobs) in new (string, AutoPlanKnobs)[]
                         {
                             ("seed", seed), ("lifts 2", WithKnobs(input.Knobs, 2, HomeLevel.Auto)), ("home green", WithKnobs(input.Knobs, 0, HomeLevel.Green)),
                         })
                {
                    var p = new SkiRunAutoPlanner();
                    p.Run(input);
                    Check(p.Reselect(knobs), w + " reselect " + kname, "Reselect refused");
                    while (!p.Step(float.PositiveInfinity))
                    {
                    }
                    var fresh = new AutoPlanInput { Snapshot = input.Snapshot, Region = input.Region, Knobs = knobs, Mode = AutoPlanMode.Resort };
                    AutoPlanResult want = new SkiRunAutoPlanner().Run(fresh);
                    Check(p.Result.Resort != null && want.Resort != null && TerrainPrint(p.Result.Resort) == TerrainPrint(want.Resort) &&
                          TerrainPrint(want.Resort) == fp, w + " reselect " + kname, "D-3: the pads / pods differ from a fresh run");
                }
            }
        }

        // P-1: the longest unit of the Terrain / PodRoute / Pods stages under 4 ms CoreCLR at 9 km2 (Step(4 ms) slices, the main-thread
        // fallback's budget); P-4: every per-cell buffer under 80 MB at 9 km2. Plus the stage totals as a TIME line.
        private static void TerrainTiming()
        {
            foreach (var (name, f, region) in new (string, HeightFn, float2[])[]
                     {
                         ("fbm 9 km2", Terrain.Fbm(21u, 0.30, 90.0), Terrain.Square(0f, 0f, 1499f)),
                         ("alpine 3.9 km2", Terrain.Alpine(1u), Terrain.AlpineRegion(false)),
                     })
            {
                string w = "Terrain timing " + name;
                AutoPlanInput input = ResortInput(Input(f, region));
                // Best of 3 per stage (the v1 timing gates' rule, Fill.cs): one GC pause of the host must not read as a slow unit
                var mx = new[] { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
                SkiRunAutoPlanner p = null;
                AutoPlanResult r = null;
                for (int run = 0; run < 3; run++)
                {
                    p = new SkiRunAutoPlanner();
                    p.Begin(input);
                    while (!p.Step(4f))
                    {
                    }
                    r = p.Result;
                    if (r.Resort == null)
                        break;
                    for (int s = 0; s < 3; s++)
                        mx[s] = Math.Min(mx[s], r.Resort.StageMaxMs[s]);
                }
                Check(r.Resort != null, w, "no plan: " + r.Status);
                if (r.Resort == null)
                    continue;
                float[] ms = r.Resort.StageMs;
                int[] units = r.Resort.StageUnits;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "TIME  terrain {0} (CoreCLR, unit max best of 3): terrain {1:F1} ms in {2} units (max {3:F2}), pod route {4:F1} ms in {5} (max {6:F2}), " +
                    "pods {7:F1} ms in {8} (max {9:F2}); buffers {10:F1} MB (v2 layers {11:F1} MB); pads {12}, pods {13}",
                    name, ms[0], units[0], mx[0], ms[1], units[1], mx[1], ms[2], units[2], mx[2], p.BufferBytes / 1048576.0,
                    p.ResortLayerBytes / 1048576.0, r.Resort.Pads.Length, r.Resort.Pods.Length));
                if (TimingChecked)
                    for (int s = 0; s < 3; s++)
                        Check(mx[s] < 4f, w, $"P-1: stage {(s == 0 ? "Terrain" : s == 1 ? "PodRoute" : "Pods")} unit {mx[s]:F2} ms (4 ms limit)");
                Check(p.BufferBytes < 80L * 1024 * 1024, w, $"P-4: planner buffers {p.BufferBytes / 1048576.0:F1} MB (80 MB limit)");
            }
        }
    }
}
