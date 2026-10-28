// V2 gate matrix (A1): `dotnet run ... -- v2` plans every region of every fixture (synthetic in git, real heightmaps from the fixture
// folder) in Resort mode and evaluates the gate metrics of stage-a-gates section 2. Output per gate ID: FAIL (a HARD gate missed),
// REPORT (a SOFT or not yet armed gate: value and target), SKIP (nothing to run on this machine). A gate becomes HARD when its owner task
// adds its ID to V2Hard below (A3: S-1..S-3, A4: S-4..S-7, A5: F-1..F-10, A6: H-1..H-6); until then a miss is a REPORT, so a stubbed
// planner never fails the host. The default run (CI) never reaches this file: it only runs the in-git unit suites.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class Program
    {
        /// <summary>Gate IDs that FAIL on a miss. Owner tasks add their IDs here when they turn their gates HARD.</summary>
        private static readonly HashSet<string> V2Hard = new HashSet<string>
        {
            "V1-2", "V1-3", "V1-4", "D-1", "D-2", "D-3", "P-1", "P-2", "P-3", "P-4",
            "S-4", "S-5", "S-6", "S-7", "P-5",   // A4
            "H-1", "H-2", "H-3", "H-5", "H-6", "F-10",   // A6 (H-5 / H-6 stay SOFT: V2AlwaysSoft; H-4 is armed after A5: v1 runs end in the base band, not on pads)
        };

        /// <summary>IDs that stay REPORT even when armed: SOFT in the gates table (F-8, H-5 for pinned-only plans, H-6, the bottom height).</summary>
        private static readonly HashSet<string> V2AlwaysSoft = new HashSet<string> { "F-8", "H-5", "H-6", "S-6 bottoms" };

        /// <summary>HARD on Alpine, Benched and real fixtures under 45 % mean grade, SOFT on Steep / Mountain Village (gates 2.3 header, 2.2 S-5).</summary>
        private static readonly HashSet<string> V2SoftOnSteeper = new HashSet<string>
        {
            "F-1", "F-2", "F-3", "F-4", "F-5", "F-6", "F-7", "F-9", "F-10", "S-5",
        };

        private static readonly SortedDictionary<string, int[]> V2Tally = new SortedDictionary<string, int[]>(StringComparer.Ordinal);   // pass, fail, report, skip

        private static int[] Tally(string id)
        {
            if (!V2Tally.TryGetValue(id, out int[] t))
                V2Tally[id] = t = new int[4];
            return t;
        }

        private static string V2Where(V2Case c) => c.Id;

        private static void V2Skip(string id, string why)
        {
            Tally(id)[3]++;
            Console.WriteLine($"SKIP  {id}: {why}");
        }

        /// <summary>One gate result of one case: HARD miss = FAIL, any other miss = REPORT; passes print a REPORT line too (value and target).</summary>
        private static void V2Emit(V2Case c, Gate g, bool softHere)
        {
            string id = g.Id;
            bool hard = V2Hard.Contains(id) && !V2AlwaysSoft.Contains(id) && !softHere;
            int[] t = Tally(id);
            if (g.Pass)
            {
                t[0]++;
                s_Checks++;
                Console.WriteLine($"REPORT {id} {V2Where(c)}: {g.Text} [ok]");
            }
            else if (hard)
            {
                t[1]++;
                Check(false, $"{id} {V2Where(c)}", g.Text);
            }
            else
            {
                t[2]++;
                Console.WriteLine($"REPORT {id} {V2Where(c)}: {g.Text} [{(V2Hard.Contains(id) && !V2AlwaysSoft.Contains(id) ? "SOFT here" : V2AlwaysSoft.Contains(id) ? "SOFT" : "not armed yet")}]");
            }
        }

        private static void V2Tests()
        {
            Console.WriteLine("V2    real-heightmap matrix: fixture folder " + (Heightmap.FixtureDir() ?? "(none)"));
            var rows = new List<string>();
            var cases = new List<V2Case>(V2Fixtures.Synthetic());
            foreach (string name in V2Fixtures.RealNames)
            {
                List<V2Case> real = V2Fixtures.Real(name, out string why);
                if (real.Count == 0)
                    V2Skip("fixture " + name, why);
                cases.AddRange(real);
            }
            var sw = Stopwatch.StartNew();
            V2BasePoints();
            foreach (uint seed in new uint[] { 2u })   // Steep seed 2 shape (the default run checks seed 1 only)
            {
                float2[] region = Terrain.Square(0f, 0f, 1250f);
                V2Fixtures.GradeStats st = V2Fixtures.Stats(Terrain.Snapshot(Terrain.Steep(seed), region), region);
                Check(st.VerticalM >= 1000f && st.VerticalM <= 1400f && st.MeanGrade >= 0.50f && st.MeanGrade <= 0.65f && st.ShareOver100 >= 0.08f && st.ShareOver100 <= 0.20f,
                    $"V2Fixtures Steep{seed}", $"vertical {st.VerticalM:F0} m, mean grade {st.MeanGrade * 100:F0} %, over 100 % {st.ShareOver100 * 100:F1} %");
                Console.WriteLine($"INFO  Steep{seed}: vertical {st.VerticalM:F0} m, mean grade {st.MeanGrade * 100:F0} %, over 100 % {st.ShareOver100 * 100:F1} % of cells, benches {BenchText(st)}");
            }
            foreach (V2Case c in cases)
                V2RunCase(c, rows);
            Console.WriteLine();
            Console.WriteLine("ROW   case | status | km2 | mean grade | v1 runs | v2 runs | candidates | kept % | pads | pods | lifts | home G/B/K/DB/Dl | dead ends | plan s | longest unit ms | layers MB");
            foreach (string row in rows)
                Console.WriteLine("ROW   " + row);
            V2Skip("D-4", "worker equality is owned by WorkerTests (A2)");
            V2Skip("D-5", "cancel latency is owned by WorkerTests (A2)");
            V2Skip("2.6 realism", "needs the real lift and run layouts per fixture ([E] / [A]); promoted by owner ruling after A5");
            Console.WriteLine();
            Console.WriteLine("GATES id | ok | FAIL | REPORT | SKIP   (HARD ids: " + string.Join(" ", V2Hard) + ")");
            foreach (var kv in V2Tally)
                Console.WriteLine($"GATE  {kv.Key,-14} | {kv.Value[0],3} | {kv.Value[1],3} | {kv.Value[2],4} | {kv.Value[3],3}");
            Console.WriteLine($"V2    matrix took {sw.Elapsed.TotalSeconds:F0} s for {cases.Count} regions");
        }

        /// <summary>REPORT: the elevation under each base point against the village elevations read in [E] 3.1 (checks the lat/lon -> game xz mapping).</summary>
        private static void V2BasePoints()
        {
            foreach (var (name, lat0, lon0, lat, lon, want) in V2Fixtures.BasePointChecks)
            {
                if (!Heightmap.TryOpen(name, out HeightmapFile f, out string why))
                    continue;
                float2 xz = V2Fixtures.LatLonXz(lat0, lon0, lat, lon);
                float h = Heightmap.HeightAt(f, xz.x, xz.y);
                Console.WriteLine($"REPORT base point {name}: ({xz.x:F0}, {xz.y:F0}) reads {h:F0} m, [E] village elevation {want} m (difference {h - want:+0;-0} m)");
            }
        }

        private static void V2RunCase(V2Case c, List<string> rows)
        {
            HeightSnapshot snap = c.Snap();
            if (snap == null || !snap.Valid)
            {
                V2Skip("case " + c.Id, "no snapshot (degenerate region or fixture unreadable)");
                return;
            }
            AutoPlanKnobs knobs = AutoPlanKnobs.Default(SkiRunAutoPlanner.SeedOf(c.Region, 0));
            AutoPlanInput inRuns = new AutoPlanInput { Snapshot = snap, Region = c.Region, Knobs = knobs };
            AutoPlanInput inRes = new AutoPlanInput { Snapshot = snap, Region = c.Region, Knobs = knobs, Mode = AutoPlanMode.Resort };
            TerrainCase(c.Id, inRes, c.ExpectBench, c.Kind == FixtureKind.Synthetic);   // A3: pads / pods invariants, S-1..S-3, REPORT

            var pRuns = new SkiRunAutoPlanner();
            AutoPlanResult v1 = pRuns.Run(inRuns);
            var pRes = new SkiRunAutoPlanner();
            var sw = Stopwatch.StartNew();
            AutoPlanResult r = pRes.Run(inRes);
            double seconds = sw.Elapsed.TotalSeconds;
            float km2 = r.RegionAreaM2 > 0f ? r.RegionAreaM2 / 1.0e6f : c.AreaKm2;

            // V1-2 / V1-4: Runs mode allocates nothing of v2; Resort keeps the refusal ladder (same status as Runs)
            V2Emit(c, new Gate("V1-2", v1.Resort == null && pRuns.ResortLayerBytes == 0 && (r.Status != AutoPlanStatus.Ok || r.Resort != null), 0, "Runs mode: no ResortPlan and no v2 layers; Resort mode: a plan whenever the scan is Ok"), false);
            V2Emit(c, new Gate("V1-4", r.Status == v1.Status || (v1.Status == AutoPlanStatus.NoRuns && r.Status == AutoPlanStatus.Ok), 0, $"Resort status {r.Status}, Runs status {v1.Status}"), false);   // NoRuns is a selection outcome, not a refusal: the A5 fans may find runs v1 did not

            bool ok = r.Status == AutoPlanStatus.Ok && r.Resort != null;
            if (ok)
                Invariants("V1-3 " + c.Id, inRes, r);   // FAIL lines come from the shared invariants (HARD)
            Tally("V1-3")[ok ? 0 : 3]++;
            if (!ok)
            {
                Console.WriteLine($"REPORT status {c.Id}: {r.Status} (Runs mode: {v1.Status}); no plan to measure");
                rows.Add($"{c.Id} | {r.Status} | {km2:F2} | {r.MeanGrade * 100:F0} % | {v1.Runs.Count} | - | - | - | - | - | - | - | - | {seconds:F2} | {r.MaxUnitMs:F1} | {pRes.ResortLayerBytes / 1.0e6:F1}");
                return;
            }

            ResortPlan p = r.Resort;
            LiftCase(c, inRes, pRes, r);   // A4: P-5, S-7 (synthetic), lift invariants, the per-region lift REPORT
            FanCase(c, pRes, r, v1);   // A5: the per-region fan REPORT line
            bool softFans = c.Steeper || r.MeanGrade >= 0.45f;
            var sampler = new SnapshotSampler(snap);
            var grid = new SlopeGrid();
            var res = new SlopeResult();
            Func<AutoRun, SlopeBand> rate = run =>
                SkiRunRater.Rate(run.Nodes, run.Nodes.Length, snap.Origin, ref sampler, snap.TexelM, 0, grid, null, res, out int _, out float _, out float _) == RateStatus.Rated
                    ? res.Band : run.Band;
            bool steepCompact = false;   // the "steep-compact" class of [P] 10 is not a fixture attribute yet: Steep / Mountain Village are SOFT instead

            V2Emit(c, V2Metrics.S1Pads(p), false);
            V2Emit(c, V2Metrics.S2PodClass(p), false);
            V2Emit(c, V2Metrics.S3Pods(p, km2, c.ExpectBench), false);
            V2Emit(c, V2Metrics.S4Lifts(p), LiftsTerrainLimited(r, false, pRes));   // A4: SOFT only where the terrain holds no lift site
            V2Emit(c, V2Metrics.S5LiftCount(p, km2, steepCompact, 0), c.Steeper || LiftsTerrainLimited(r, true, pRes));   // A4: SOFT where every liftable pod has its lift
            V2Emit(c, V2Metrics.S6Stacking(p, c.ExpectBench && r.VerticalM > 600f), false);
            V2Emit(c, V2Metrics.S6BottomHeight(p), false);
            V2Emit(c, V2Metrics.F1TrailsPerLift(r, p, knobs.Density), softFans);
            V2Emit(c, V2Metrics.F2StartsNearTop(r, p), softFans);
            V2Emit(c, V2Metrics.F3EndsAtBottom(r, p), softFans);
            V2Emit(c, V2Metrics.F4Branching(r), softFans);
            V2Emit(c, V2Metrics.F5Spacing(r, p), softFans);
            V2Emit(c, V2Metrics.F6PodShare(r, p), softFans);
            V2Emit(c, V2Metrics.F7Density(r, p, km2, steepCompact, knobs.Density), softFans);
            V2Emit(c, V2Metrics.F8RunGeometry(r), false);
            V2Emit(c, V2Metrics.F9Collapse(r), softFans && !(c.Fixture == "Alpine" || c.Id.StartsWith("MountainVillage field", StringComparison.Ordinal)));   // F-9 is named HARD on the Alpine bowl and every MV region
            V2Emit(c, V2Metrics.F10MixHonest(r, p, rate), softFans);
            V2Emit(c, V2Metrics.H1Verdicts(p), p.Lifts.Length == 0);   // A6: n/a without lifts is no miss
            V2Emit(c, V2Metrics.H2Honesty(r, p, rate), p.Lifts.Length == 0);
            V2Emit(c, V2Metrics.H3NoFakeGreen(p), p.Lifts.Length == 0);
            V2Emit(c, V2Metrics.H4DeadEnds(r, p), false);
            V2Emit(c, V2Metrics.H5Reachability(r, p), false);
            V2Emit(c, V2Metrics.H6GreenShare(p), false);

            // P-1 is a v1 gate (synthetic 9 km2 fixtures, FillTests): one run is noisy at the millisecond (GC, JIT), so a miss is re-measured
            // once and the smaller figure counts. On the real heightmaps a miss stays REPORT (that is v1 unit time on terrain the v1 gate
            // never saw; the planner is not tuned here, DECISIONS / handover note).
            double unitMs = UnitMsWithoutTowerPlans(r);   // A4: tower plan units are P-5's (spec 4.4: 10-44 ms each, on the worker)
            if (unitMs >= 4.0)
                unitMs = Math.Min(unitMs, UnitMsWithoutTowerPlans(new SkiRunAutoPlanner().Run(inRes)));
            V2Emit(c, V2Metrics.P1LongestUnit(unitMs), c.Kind != FixtureKind.Synthetic);
            if (km2 >= 3.5f)
                V2Emit(c, V2Metrics.PWorkerTime(km2, seconds), false);
            long cells = (long)snap.Width * snap.Depth;
            V2Emit(c, V2Metrics.P4Memory(pRes.ResortLayerBytes + 49L * cells, $"(v2 layers {pRes.ResortLayerBytes / 1.0e6:F1} MB exact + 49 B x {cells / 1000} k cells for the v1 layers, estimated)"), false);

            if (c.Determinism)
                V2Determinism(c, snap, inRes, r.Fingerprint);

            int[] home = new int[6];
            foreach (PlanLift l in p.Lifts)
                home[(int)l.Home]++;
            double kept = r.Candidates > 0 ? 100.0 * r.Runs.Count / r.Candidates : 0.0;
            rows.Add($"{c.Id} | {r.Status} | {km2:F2} | {r.MeanGrade * 100:F0} % | {v1.Runs.Count} | {r.Runs.Count} | {r.Candidates} | {kept:F1} | {p.Pads.Length} | {p.Pods.Length} | {p.Lifts.Length} | " +
                     $"{home[1]}/{home[2]}/{home[3]}/{home[4]}/{home[5]} | {p.DeadEnds} | {seconds:F2} | {r.MaxUnitMs:F1} | {pRes.ResortLayerBytes / 1.0e6:F1}");
        }

        // ---------------------------------------------------------------- D-1 .. D-3

        private static ulong V2Fresh(AutoPlanInput template, AutoPlanKnobs knobs, float2[] region = null)
        {
            var input = new AutoPlanInput
            {
                Snapshot = template.Snapshot, Region = region ?? template.Region, Obstacles = template.Obstacles, Knobs = knobs, Mode = template.Mode, Pinned = template.Pinned,
            };
            return new SkiRunAutoPlanner().Run(input).Fingerprint;
        }

        private static void V2Determinism(V2Case c, HeightSnapshot snap, AutoPlanInput input, ulong want)
        {
            float2[] reg = input.Region;
            int n = reg.Length;
            var rot = new float2[n];
            var rev = new float2[n];
            for (int i = 0; i < n; i++)
            {
                rot[i] = reg[(i + 2) % n];
                rev[i] = reg[n - 1 - i];
            }
            V2Emit(c, V2Metrics.DEqual("D-1", "rerun", want, V2Fresh(input, input.Knobs)), false);
            V2Emit(c, V2Metrics.DEqual("D-1", "rotated region", want, V2Fresh(input, input.Knobs, rot)), false);
            V2Emit(c, V2Metrics.DEqual("D-1", "reversed region", want, V2Fresh(input, input.Knobs, rev)), false);

            var sliced = new SkiRunAutoPlanner();
            sliced.Begin(input);
            while (!sliced.Step(4f))
            {
            }
            V2Emit(c, V2Metrics.DEqual("D-2", "Step(4) slices", want, sliced.Result.Fingerprint), false);

            // D-3: each Reselect equals a fresh run with those knobs
            AutoPlanKnobs k = input.Knobs;
            var variants = new (string, AutoPlanKnobs)[6];
            variants[0] = ("Seed", With(k, x => x.Seed = SkiRunAutoPlanner.SeedOf(input.Region, 7)));
            variants[1] = ("Mix", With(k, x => x.Mix = AutoMix.Expert));
            variants[2] = ("Density", With(k, x => x.Density = AutoDensity.Dense));
            variants[3] = ("Lifts", With(k, x => x.Lifts = 2));
            variants[4] = ("Home", With(k, x => x.Home = HomeLevel.Green));
            variants[5] = ("Kinds", With(k, x => x.Kinds = AutoKinds.Groomed | AutoKinds.Snowmaking | AutoKinds.Night));
            foreach (var (name, knobs) in variants)
            {
                var p = new SkiRunAutoPlanner();
                p.Run(input);
                bool accepted = p.Reselect(knobs);
                while (accepted && !p.Step(float.PositiveInfinity))
                {
                }
                ulong got = accepted ? p.Result.Fingerprint : 0UL;
                Gate g = V2Metrics.DEqual("D-3", "Reselect " + name, V2Fresh(input, knobs), got);
                V2Emit(c, accepted ? g : new Gate("D-3", false, 1, "Reselect " + name + " refused"), false);
            }
        }

        private static AutoPlanKnobs With(AutoPlanKnobs k, Action<BoxedKnobs> set)
        {
            var b = new BoxedKnobs { K = k };
            set(b);
            return b.K;
        }

        private sealed class BoxedKnobs
        {
            public AutoPlanKnobs K;
            public uint Seed { set => K.Seed = value; }
            public AutoMix Mix { set => K.Mix = value; }
            public AutoDensity Density { set => K.Density = value; }
            public int Lifts { set => K.Lifts = value; }
            public HomeLevel Home { set => K.Home = value; }
            public AutoKinds Kinds { set => K.Kinds = value; }
        }
    }
}
