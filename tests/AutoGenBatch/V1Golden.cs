// Gate V1-1 (Resort Planner v2, stage A) holds the v1 planner's result fingerprints. They were recorded from main at the branch point
// (before any v2 hook existed). This file replays them in Runs mode (the planner's default). Any change to the v1 stage machine, field,
// seeds, select, finish or fingerprint that is not bit-identical causes a FAIL line naming the fixture.
// `dotnet run --project tests/AutoGenBatch/AutoGenBatch.csproj -c Release -- golden` prints the table in source form (CoreCLR,
// the fingerprint is a pure function of the input within a runtime, SkiRunAutoPlanner.cs header). Re-record only when main's v1
// planner changed on purpose (the merge commit says so). Never re-record to silence a v2 difference.

using System;
using System.Collections.Generic;
using System.IO;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class Program
    {
        private static IEnumerable<(string Name, AutoPlanInput Input)> GoldenCases()
        {
            // Determinism fixtures (Program.cs Determinism): one fbm square and its knob variants
            float2[] sq480 = Terrain.Square(0f, 0f, 480f);
            HeightFn fbm4 = Terrain.Fbm(4u, 0.32, 70.0);
            yield return ("determinism base", Input(fbm4, sq480));
            AutoPlanInput g = Input(fbm4, sq480);
            g.Knobs.Mix = AutoMix.Expert;
            yield return ("determinism mix expert", g);
            g = Input(fbm4, sq480);
            g.Knobs.Kinds = AutoKinds.Groomed | AutoKinds.Snowmaking | AutoKinds.Night;
            yield return ("determinism kinds", g);
            g = Input(fbm4, sq480);
            g.Knobs.Seed = SkiRunAutoPlanner.SeedOf(sq480, 7);
            yield return ("determinism seed 7", g);
            g = Input(fbm4, sq480);
            g.Knobs.Density = AutoDensity.Dense;
            yield return ("determinism dense", g);

            // Mixed mountain, alone and beside an existing run (Knobs fixtures)
            yield return ("mixed mountain", Input(Mixed, MixedRegion));
            var existing = new List<AutoObstacle> { new AutoObstacle { Kind = AutoObstacleKind.ExistingRun, Ring = Terrain.Rect(-20f, -300f, 20f, 300f) } };
            foreach (AutoDensity d in new[] { AutoDensity.Sparse, AutoDensity.Normal, AutoDensity.Dense })
            {
                g = Input(Mixed, MixedRegion, existing);
                g.Knobs.Density = d;
                yield return ("mixed existing run " + d, g);
            }

            // Fill fixtures: three open slopes at every density, two planes Dense
            float2[] sq = Terrain.Square(0f, 0f, 499f);
            for (uint s = 1; s <= 3; s++)
                for (int d = 0; d < 3; d++)
                {
                    g = Input(OpenSlope(s), sq);
                    g.Knobs.Density = (AutoDensity)d;
                    yield return ($"open slope {s} {(AutoDensity)d}", g);
                }
            foreach (double grade in new[] { 0.30, 0.44 })
            {
                g = Input(Terrain.Plane(grade), sq);
                g.Knobs.Density = AutoDensity.Dense;
                yield return ($"plane {grade:F2} dense", g);
            }

            // Field fixtures: Alpine seeds 1-3, bowl and lower region, Normal and Dense, Balanced
            for (uint seed = 1; seed <= 3; seed++)
            {
                HeightFn f = Terrain.Alpine(seed);
                foreach (bool lower in new[] { false, true })
                    foreach (AutoDensity d in new[] { AutoDensity.Normal, AutoDensity.Dense })
                    {
                        g = Input(f, Terrain.AlpineRegion(lower));
                        g.Knobs.Density = d;
                        g.Knobs.Mix = AutoMix.Balanced;
                        yield return ($"alpine {seed} {(lower ? "lower" : "bowl")} {d}", g);
                    }
            }

            // Timing fixtures: 1, 4, 9 km2 (the 9 km2 one exercises the lattice cap)
            foreach (float km2 in new[] { 1f, 4f, 9f })
            {
                float half = 500f * (float)Math.Sqrt(km2) - 1f;
                yield return ($"timing {km2} km2", Input(Terrain.Fbm(21u, 0.30, 90.0), Terrain.Square(0f, 0f, half)));
            }

            // Real in-game captures (SlopeBatch), the CaptureTests inputs
            string dir = Path.Combine("tests", "SlopeBatch", "captures");
            if (Directory.Exists(dir))
            {
                string[] files = Directory.GetFiles(dir, "*.skislope");
                Array.Sort(files, StringComparer.Ordinal);
                foreach (string file in files)
                {
                    Capture c = Capture.Load(file);
                    HeightSnapshot snap = c.Snapshot();
                    if (snap == null)
                        continue;
                    g = new AutoPlanInput
                    {
                        Snapshot = snap, Region = c.Ring, Knobs = AutoPlanKnobs.Default(SkiRunAutoPlanner.SeedOf(c.Ring, 0)),
                        MinAreaM2 = 100f, MinVerticalM = 5f,
                    };
                    g.Knobs.Mix = AutoMix.Terrain;
                    yield return ("capture " + c.Name, g);
                }
            }
        }

        /// <summary>`-- golden`: print the table in the source form of <see cref="GoldenTable"/>.</summary>
        private static void GoldenPrint()
        {
            foreach (var (name, input) in GoldenCases())
            {
                AutoPlanResult r = new SkiRunAutoPlanner().Run(input);
                Console.WriteLine($"            (\"{name}\", 0x{r.Fingerprint:X16}UL),   // {r.Status}, {r.Runs.Count} runs");
            }
        }

        /// <summary>V1-1: every golden case in Runs mode reproduces its recorded fingerprint. V1-2: every golden case also allocates nothing of v2.</summary>
        private static void V1GoldenTests()
        {
            Check(GoldenTable.Length > 0, "V1Golden", "no golden fingerprints recorded");
            var want = new Dictionary<string, ulong>();
            foreach (var (name, fp) in GoldenTable)
                want[name] = fp;
            int n = 0;
            foreach (var (name, input) in GoldenCases())
            {
                n++;
                string w = "V1-1 " + name;
                if (!want.TryGetValue(name, out ulong fp))
                {
                    Check(false, w, "no recorded fingerprint");
                    continue;
                }
                Check(input.Mode == AutoPlanMode.Runs, w, "the golden input is not in Runs mode");
                AutoPlanResult r = new SkiRunAutoPlanner().Run(input);
                Check(r.Fingerprint == fp, w, $"fingerprint 0x{r.Fingerprint:X16} differs from the recorded 0x{fp:X16}");
                Check(r.Resort == null, "V1-2 " + name, "Runs mode produced a ResortPlan");
            }
            Check(n == GoldenTable.Length, "V1Golden", $"{n} cases but {GoldenTable.Length} recorded (a case was added or removed without re-recording)");
        }

        // Recorded from main 9bb8302 (the branch point, plus B1's API-only narrow profile 74a5bc7, which GradeBatch covers): CoreCLR Release.
        private static readonly (string Name, ulong Fingerprint)[] GoldenTable =
        {
            ("determinism base", 0x6FF04D63920191B5UL),   // Ok, 10 runs
            ("determinism mix expert", 0x5B9728F80E76D6E7UL),   // Ok, 10 runs
            ("determinism kinds", 0xE7FBE9DDA9B27B12UL),   // Ok, 10 runs
            ("determinism seed 7", 0xD120CB1CD6742B10UL),   // Ok, 12 runs
            ("determinism dense", 0x0570791265545160UL),   // Ok, 11 runs
            ("mixed mountain", 0xBE3540A8FBC45631UL),   // Ok, 12 runs
            ("mixed existing run Sparse", 0xBBF3E86B8F97B250UL),   // Ok, 8 runs
            ("mixed existing run Normal", 0x9125D8708BB5B288UL),   // Ok, 12 runs
            ("mixed existing run Dense", 0xEA1F174C59534FE6UL),   // Ok, 16 runs
            ("open slope 1 Sparse", 0xC9FE01F98A650F61UL),   // Ok, 8 runs
            ("open slope 1 Normal", 0xCC777EB9D0724DC8UL),   // Ok, 12 runs
            ("open slope 1 Dense", 0x395B92C5D2E485BDUL),   // Ok, 16 runs
            ("open slope 2 Sparse", 0x27A13D4DCF906BF2UL),   // Ok, 8 runs
            ("open slope 2 Normal", 0x72ED903ADCF893ACUL),   // Ok, 12 runs
            ("open slope 2 Dense", 0x4D41CE39E58BBD61UL),   // Ok, 13 runs
            ("open slope 3 Sparse", 0xC40231225C4230BFUL),   // Ok, 7 runs
            ("open slope 3 Normal", 0xFCFB408067DE77B3UL),   // Ok, 12 runs
            ("open slope 3 Dense", 0x8E001A95A281B056UL),   // Ok, 12 runs
            ("plane 0.30 dense", 0x67D5076CE3F96D47UL),   // Ok, 16 runs
            ("plane 0.44 dense", 0x7A69CBFF5962469CUL),   // Ok, 16 runs
            ("alpine 1 bowl Normal", 0xE1AC4B4FCA7948F3UL),   // Ok, 48 runs
            ("alpine 1 bowl Dense", 0xEB3945D56E98E00FUL),   // Ok, 62 runs
            ("alpine 1 lower Normal", 0x403A70766BBDF4D3UL),   // Ok, 13 runs
            ("alpine 1 lower Dense", 0xC90EEE6280A5427FUL),   // Ok, 17 runs
            ("alpine 2 bowl Normal", 0x2F15C3D197127EBDUL),   // Ok, 48 runs
            ("alpine 2 bowl Dense", 0x2856C7D4D8A8933DUL),   // Ok, 58 runs
            ("alpine 2 lower Normal", 0x978BC18FB2A1789FUL),   // Ok, 13 runs
            ("alpine 2 lower Dense", 0x0B92C9BEB689B5C2UL),   // Ok, 17 runs
            ("alpine 3 bowl Normal", 0x5D4CAAE18C2FD608UL),   // Ok, 48 runs
            ("alpine 3 bowl Dense", 0x3646860315A5DC17UL),   // Ok, 54 runs
            ("alpine 3 lower Normal", 0x010C3CC36F9F908FUL),   // Ok, 13 runs
            ("alpine 3 lower Dense", 0x859663BD040B58B3UL),   // Ok, 17 runs
            ("timing 1 km2", 0xD5B5699F390768F1UL),   // Ok, 12 runs
            ("timing 4 km2", 0xE6E23267652495B9UL),   // Ok, 31 runs
            ("timing 9 km2", 0x5985A2EDD9A36315UL),   // Ok, 48 runs
            ("capture run-20261001-092600.skislope", 0xD7E636CE78D5DDF9UL),   // Ok, 1 runs
            ("capture run-20261001-093455.skislope", 0x084A5307B4FFA7BFUL),   // NoRuns, 0 runs
            ("capture run-20261001-093534.skislope", 0x084A5307B4FFA7BFUL),   // NoRuns, 0 runs
            ("capture run-20261001-093640.skislope", 0x53538B1C166D8DFCUL),   // Ok, 1 runs
            ("capture run-20261001-095100.skislope", 0xA8F1F464369B9C59UL),   // Ok, 2 runs
            ("capture run-20261003-142231.skislope", 0x2C08985D8FDD9CAFUL),   // Ok, 3 runs
        };
    }
}
