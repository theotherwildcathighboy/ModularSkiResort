// Headless tests for the Quick runs planner (SkiRunAutoPlanner*.cs), design.md 2.5 / terrain-scan.md 6 plus the skeptic's changes.
// They cover:
// - Snapshot sampler (the SampleHeightInternal shape).
// - Refusal ladder (each status and its order).
// - Planes (every rated band equals the plane's band).
// - Cone / ridge / bowl / valley-with-gully.
// - Obstacles (surfaces and unowned tiles block, cat tracks and water do not).
// - Knobs (mix, density, kinds).
// - The minimax field against a brute-force oracle.
// - The tiled slope field against one call (bit for bit).
// - Determinism (rerun, rotated / reversed region, sliced Step, Reselect against a fresh run).
// - The SlopeBatch captures.
// - TIME lines for 1 and 4 km2.
// Every Ok result passes these invariants:
// - Ring inside the region and the map.
// - Obstacle clearances.
// - Pairwise gap (or a merge).
// - GradeRibbon.Clear.
// - Area under 20 ha at step multiple 1.
// - An independent re-rate equals the stored band.
// - Every pair of runs closer than JunctionReachM has a node pair within LinkMeters (skeptic finding 1).
// Run from the repo root: dotnet run --project tests/AutoGenBatch/AutoGenBatch.csproj -c Release
// Prints `FAIL  where: msg` lines and one SUMMARY line. Environment.ExitCode = failures (the LiftBatch convention).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class Program
    {
        private static int s_Failures;
        private static long s_Checks;

        private static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            if (args.Length > 0 && (args[0] == "rf" || args[0] == "rf-holdout" || args[0] == "rf-golden" || args[0] == "rf-cancel"))
                return RunsFirst.Run(args);   // the runs-first prototype (field test 5): renders, no checks
            if (args.Length > 0 && (args[0] == "rf-replay" || args[0] == "rf-scan")) return ScanHost.Run(args);   // the tool's scan job: replay a game dump, density presets (ScanHost.cs)
            if (args.Length > 0 && args[0] == "rf-realize")
                return RealizerHost.Run(args);   // the runs-first realizer on the real sets (RealizerHost.cs)
            if (args.Length > 0 && args[0] == "golden")
            {
                GoldenPrint();
                return 0;
            }
            if (args.Length > 0 && args[0] == "field")
            {
                FieldDiag();
                return 0;
            }
            if (args.Length > 0 && (args[0] == "diag" || args[0] == "big"))
            {
                if (args[0] == "diag")
                {
                    FillDiag();
                    ShuffleDiag();
                }
                BigDiag();
                return 0;
            }
            if (!TimingChecked)
                Console.WriteLine("NOTE  Debug build: the unit-time limits (FieldTests, FillTests 9 km2) print but are not checked; run with -c Release");
            ToRepoRoot();
            V1GoldenTests();
            PlanTypes();
            SamplerTests();
            SeedTests();
            MinimaxOracle();
            TiledField();
            Ladder();
            Planes();
            Shapes();
            Obstacles();
            Knobs();
            Determinism();
            CaptureTests();
            QuickRunsTests();
            FillTests();
            FieldTests();
            ReviewTests();
            HeightmapTests();
            V2FixtureTests();
            V2MetricsTests();
            WorkerTests();
            TerrainTests();
            LiftTests();
            FanTests();
            FlowTests();
            RunsFirstGoldenTests();
            RunsFirstScoreTests();
            RealizerTests();
            if (args.Length > 0 && args[0] == "v2")
                V2Tests();   // the real-heightmap matrix (A1): local only, the fixtures live outside git
            Timing();
            Console.WriteLine($"SUMMARY AutoGenBatch: {s_Checks} checks, {s_Failures} failure(s)");
            Environment.ExitCode = s_Failures;
            return s_Failures;
        }

        // ---------------------------------------------------------------- plumbing

        /// <summary>The captures paths are repo-relative (tests/SlopeBatch/captures). When the current directory has no gamedata-mirror,
        /// this method moves to the first parent of AppContext.BaseDirectory (tests/AutoGenBatch/bin/&lt;cfg&gt;/net8.0/) that has one. So a run
        /// started from tests/ or mod/SkiResortUI checks the same files as a run from the root. Only the check run moves. The rf / golden
        /// / diag modes keep the caller's directory for their relative output paths.</summary>
        private static void ToRepoRoot()
        {
            if (Directory.Exists("gamedata-mirror"))
                return;
            for (DirectoryInfo d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, "gamedata-mirror")))
                {
                    Directory.SetCurrentDirectory(d.FullName);
                    return;
                }
        }

        private static void Check(bool ok, string where, string msg)
        {
            s_Checks++;
            if (!ok)
            {
                s_Failures++;
                Console.WriteLine($"FAIL  {where}: {msg}");
            }
        }

        private static AutoPlanInput Input(HeightFn f, float2[] region, List<AutoObstacle> obstacles = null, Func<double, double, bool> wet = null)
        {
            return new AutoPlanInput
            {
                Snapshot = Terrain.Snapshot(f, region, wet),
                Region = region,
                Obstacles = obstacles,
                Knobs = AutoPlanKnobs.Default(SkiRunAutoPlanner.SeedOf(region, 0)),
            };
        }

        private static AutoPlanResult Plan(AutoPlanInput input)
        {
            AutoPlanResult r = new SkiRunAutoPlanner().Run(input);
            NoRunsHasReason("plan", r);
            return r;
        }

        /// <summary>Review F2 / F11: NoRuns promises the reject histogram says why. An empty one means a refusal was missed.</summary>
        private static void NoRunsHasReason(string where, AutoPlanResult r)
        {
            if (r.Status != AutoPlanStatus.NoRuns)
                return;
            int total = 0;
            for (int i = 1; i < (int)AutoReject.Count; i++)
                total += r.Rejects[i];
            Check(total > 0, where, "NoRuns with an empty reject histogram (no reason)");
        }

        /// <summary>Review F7: the unit-time limits are CoreCLR Release numbers. A Debug build runs the planner 1.3-1.5x slower, so
        /// there they print without a check. A shared CI runner is no timer either (P-5 read 57.8 ms on one, 2026-10-31), so the test
        /// checks the limits on local runs only.</summary>
#if DEBUG
        private static readonly bool TimingChecked = false;   // readonly, not const: no unreachable-code warning
#else
        private static readonly bool TimingChecked = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true";
#endif

        private static string Mix(AutoPlanResult r) =>
            $"G{r.BandCount[1]} B{r.BandCount[2]} K{r.BandCount[3]} DB{r.BandCount[4]} (target G{r.BandTarget[1]} B{r.BandTarget[2]} K{r.BandTarget[3]} DB{r.BandTarget[4]})";

        private static string Rejects(AutoPlanResult r)
        {
            var parts = new List<string>();
            for (int i = 1; i < (int)AutoReject.Count; i++)
                if (r.Rejects[i] > 0)
                    parts.Add($"{(AutoReject)i} {r.Rejects[i]}");
            return parts.Count == 0 ? "none" : string.Join(", ", parts);
        }

        private static void Info(string name, AutoPlanResult r)
        {
            Console.WriteLine($"INFO  {name}: {r.Status}, {r.Runs.Count} runs {Mix(r)}, warnings [{r.Warnings}], clusters " +
                              $"{ClusterText(r)}, junctions {r.Junctions.Count}, candidates {r.Candidates}, rated {r.Rated}, " +
                              $"rejects [{Rejects(r)}]");
        }

        private static string ClusterText(AutoPlanResult r)
        {
            var parts = new List<string>();
            foreach (AutoCluster c in r.Clusters)
                parts.Add($"{c.Runs}/{c.Linked}{(c.Pod ? "p" + (int)c.PodBand : "")}");
            return "[" + string.Join(" ", parts) + "]";
        }

        // ---------------------------------------------------------------- invariants (every Ok result)

        private static void Invariants(string name, AutoPlanInput input, AutoPlanResult r)
        {
            HeightSnapshot snap = input.Snapshot;
            var sampler = new SnapshotSampler(snap);
            var grid = new SlopeGrid();
            var res = new SlopeResult();
            AutoKinds allowed = input.Knobs.Kinds == AutoKinds.None ? AutoKinds.Default : input.Knobs.Kinds;
            Check(r.Clusters.Count <= SkiRunAutoPlanner.ClusterCap(r.RegionAreaM2) + SkiRunAutoPlanner.PodCap(r.RegionAreaM2), name, $"{r.Clusters.Count} clusters");
            int cap = SkiRunAutoPlanner.RunCapOf(input.Knobs, r.RegionAreaM2);
            Check(r.Runs.Count <= cap, name, $"{r.Runs.Count} runs over the cap {cap}");
            for (int a = 0; a < r.Runs.Count; a++)
            {
                AutoRun run = r.Runs[a];
                string w = $"{name} run {run.Id}";
                float2[] ring = run.Ring;
                bool inside = true;
                foreach (float2 p in ring)
                    inside &= GradeRaster.Inside(input.Region, p);
                Check(inside && SkiRunAutoPlanner.EdgeDistance(ring, input.Region, 1f, out _, out _, out _, out _) >= SkiRunAutoPlanner.RegionClearanceM - 1e-3f,
                    w, "outline leaves the region");
                Check(SkiRunAutoPlanner.ObstaclesClear(ring, input.Obstacles, input.Knobs.Density), w, "obstacle clearance violated");
                Check(GradeRibbon.Clear(ring, GradeRibbon.MinClearanceM), w, "outline edges closer than 1.5 m");
                double area = SkiRunAutoPlanner.PolygonArea(ring);
                Check(area <= SkiRunAutoPlanner.MaxRunAreaM2 && SkiSlopeClassifier.StepMultiple(area, snap.TexelM, 0) == 1, w,
                    $"area {area:F0} m2 over the cap or step multiple > 1");
                Check(run.Nodes != null && run.Nodes.Length == ring.Length, w, "nodes do not match the ring");
                bool nodesOk = true;
                for (int i = 0; i < ring.Length; i++)
                    nodesOk &= run.Nodes[i].x == ring[i].x && run.Nodes[i].z == ring[i].y && run.Nodes[i].y == snap.Sample(run.Nodes[i]);
                Check(nodesOk, w, "node heights are not the snapshot's");
                RateStatus rs = SkiRunRater.Rate(run.Nodes, run.Nodes.Length, snap.Origin, ref sampler, snap.TexelM, 0, grid, null, res,
                    out int m, out float _, out float _);
                Check(rs == RateStatus.Rated && res.Band == run.Band && m == 1, w, $"re-rate {rs} {res.Band} m{m} vs stored {run.Band}");
                Check(KindAllowed(run.Kind, allowed), w, $"kind {run.Kind} not allowed ({allowed})");
                Check(run.Cluster >= 0 && run.Cluster < r.Clusters.Count, w, "no cluster");
                Check(run.Band != SlopeBand.Flat, w, "flat run");
            }
            for (int a = 0; a < r.Runs.Count; a++)
                for (int b = a + 1; b < r.Runs.Count; b++)
                {
                    AutoRun ra = r.Runs[a], rb = r.Runs[b];
                    float d = SkiRunAutoPlanner.RingDistance(ra.Ring, rb.Ring, 200f, out _, out _, out _, out _);
                    bool merge = false, linked = false;
                    foreach (AutoJunction j in r.Junctions)
                        if (!j.Existing && ((j.RunA == a && j.RunB == b) || (j.RunA == b && j.RunB == a)))
                        {
                            merge |= j.Merge;
                            float2 na = r.Runs[j.RunA].Ring[j.NodeA], nb = r.Runs[j.RunB].Ring[j.NodeB];
                            linked |= math.distance(na, nb) <= SkiRunAutoPlanner.LinkMeters;
                        }
                    float gap = SkiRunAutoPlanner.GapOf(input.Knobs.Density,
                        Math.Min(SkiRunAutoPlanner.TargetWidthM[(int)ra.PassBand - 1], SkiRunAutoPlanner.TargetWidthM[(int)rb.PassBand - 1]));
                    string w = $"{name} runs {ra.Id}/{rb.Id}";
                    Check(d >= SkiRunAutoPlanner.MergeGapM - 1e-3f, w, $"overlap or closer than the merge gap ({d:F2} m)");
                    Check(merge || d >= SkiRunAutoPlanner.ExactGapShare * gap - 1e-3f, w, $"{d:F2} m apart, gap {gap} m, no merge");
                    if (d <= SkiRunAutoPlanner.JunctionReachM - 0.01f)
                        Check(linked, w, $"{d:F2} m apart but no node pair within {SkiRunAutoPlanner.LinkMeters} m");
                }
            foreach (AutoJunction j in r.Junctions)
            {
                bool ok = j.RunA >= 0 && j.RunA < r.Runs.Count && j.NodeA >= 0 && j.NodeA < r.Runs[j.RunA].Ring.Length;
                if (ok && j.Existing)
                    ok = input.Obstacles != null && j.RunB < input.Obstacles.Count && j.NodeB < input.Obstacles[j.RunB].Ring.Length &&
                         math.distance(r.Runs[j.RunA].Ring[j.NodeA], input.Obstacles[j.RunB].Ring[j.NodeB]) <= SkiRunAutoPlanner.LinkMeters;
                else if (ok)
                    ok = j.RunB >= 0 && j.RunB < r.Runs.Count && j.NodeB >= 0 && j.NodeB < r.Runs[j.RunB].Ring.Length &&
                         math.distance(r.Runs[j.RunA].Ring[j.NodeA], r.Runs[j.RunB].Ring[j.NodeB]) <= SkiRunAutoPlanner.LinkMeters;
                Check(ok, name, $"junction {j.RunA}:{j.NodeA} - {j.RunB}:{j.NodeB} invalid or over {SkiRunAutoPlanner.LinkMeters} m");
            }
        }

        private static bool KindAllowed(RunKind k, AutoKinds a)
        {
            switch (k)
            {
                case RunKind.Groomed: return (a & AutoKinds.Groomed) != 0;
                case RunKind.Natural: return (a & AutoKinds.Natural) != 0;
                case RunKind.Snowmaking: return (a & AutoKinds.Snowmaking) != 0;
                case RunKind.Night: return (a & AutoKinds.Night) != 0;
                default: return false;
            }
        }

        private static AutoPlanResult PlanChecked(string name, AutoPlanInput input)
        {
            AutoPlanResult r = Plan(input);
            Info(name, r);
            if (r.Status == AutoPlanStatus.Ok)
                Invariants(name, input, r);
            return r;
        }

        // ---------------------------------------------------------------- snapshot sampler

        private static void SamplerTests()
        {
            float2[] region = Terrain.Square(0f, 0f, 200f);
            HeightSnapshot s = Terrain.Snapshot(Terrain.Fbm(3u, 0.3, 40.0), region);
            Check(s.Valid, "sampler", "snapshot not valid");
            int k = s.X0 + 10, l = s.Z0 + 12;
            float x = (float)(s.Origin.x + k * (double)s.TexelM), z = (float)(s.Origin.y + l * (double)s.TexelM);
            s.TryRaw(k, l, out ushort raw);
            float at = s.Sample(new float3(x, 0f, z));
            Check(Math.Abs(at - s.ToWorld(raw)) < 1e-3f, "sampler", $"texel center {at} vs raw {s.ToWorld(raw)}");
            s.TryRaw(k + 1, l, out ushort raw1);
            float mid = s.Sample(new float3(x + 0.5f * s.TexelM, 0f, z));
            Check(Math.Abs(mid - 0.5f * (s.ToWorld(raw) + s.ToWorld(raw1))) < 2e-3f, "sampler", "half-texel bilinear");
            Check(float.IsNaN(s.Sample(new float3(x - 4000f, 0f, z))), "sampler", "outside the rectangle is not NaN");
            Check(!s.CoversClamped(s.X0 - 1, s.Z0, s.X0 + 1, s.Z0), "sampler", "coverage left of the rectangle");
        }

        private static void SeedTests()
        {
            float2[] a = { new float2(0, 0), new float2(100, 0), new float2(100, 50), new float2(0, 60) };
            float2[] rot = { a[2], a[3], a[0], a[1] };
            float2[] rev = { a[3], a[2], a[1], a[0] };
            uint s = SkiRunAutoPlanner.SeedOf(a, 0);
            Check(s == SkiRunAutoPlanner.SeedOf(rot, 0) && s == SkiRunAutoPlanner.SeedOf(rev, 0), "seed", "start vertex or direction changes the seed");
            Check(s != SkiRunAutoPlanner.SeedOf(a, 1), "seed", "shuffle does not change the seed");
            Check((s & 1u) == 1u, "seed", "seed is even (Random needs nonzero)");
        }

        // ---------------------------------------------------------------- stage 3 oracle

        private static void MinimaxOracle()
        {
            var rng = new Unity.Mathematics.Random(12345u);
            int cases = 0;
            for (int t = 0; t < 300; t++)
            {
                int nx = rng.NextInt(3, 14), nz = rng.NextInt(3, 12), n = nx * nz;
                var bucket = new ushort[n];
                var src = new bool[n];
                for (int c = 0; c < n; c++)
                {
                    bucket[c] = rng.NextFloat() < 0.15f ? ushort.MaxValue : (ushort)rng.NextInt(0, 40);
                    src[c] = rng.NextFloat() < 0.08f;
                }
                var got = new ushort[n];
                SkiRunAutoPlanner.MinimaxField(nx, nz, bucket, src, got);
                // Brute force: the smallest threshold at which a flood over cells with bucket <= threshold from the sources reaches c
                var want = new ushort[n];
                for (int c = 0; c < n; c++)
                    want[c] = ushort.MaxValue;
                for (int th = 0; th < 40; th++)
                {
                    var seen = new bool[n];
                    var q = new Queue<int>();
                    for (int c = 0; c < n; c++)
                        if (src[c] && bucket[c] <= th)
                        {
                            seen[c] = true;
                            q.Enqueue(c);
                        }
                    while (q.Count > 0)
                    {
                        int c = q.Dequeue();
                        if (want[c] == ushort.MaxValue)
                            want[c] = (ushort)th;
                        int i = c % nx, j = c / nx;
                        for (int dz = -1; dz <= 1; dz++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int ni = i + dx, nj = j + dz;
                                if (ni < 0 || nj < 0 || ni >= nx || nj >= nz)
                                    continue;
                                int nc = nj * nx + ni;
                                if (!seen[nc] && bucket[nc] <= th)
                                {
                                    seen[nc] = true;
                                    q.Enqueue(nc);
                                }
                            }
                    }
                }
                bool same = true;
                for (int c = 0; c < n; c++)
                    same &= got[c] == want[c];
                Check(same, "minimax oracle", $"case {t} ({nx}x{nz}) differs from the threshold flood");
                cases++;
            }
            Console.WriteLine($"INFO  minimax oracle: {cases} random grids equal the brute-force threshold flood");
        }

        // ---------------------------------------------------------------- tiled field == one call

        private static void TiledField()
        {
            float2[] region = Terrain.Square(300f, -200f, 300f);
            HeightSnapshot s = Terrain.Snapshot(Terrain.Fbm(11u, 0.35, 60.0), region);
            var sampler = new SnapshotSampler(s);
            double2 o = s.Origin;
            float t = s.TexelM;
            int k0 = (int)Math.Round((region[0].x - o.x) / t) + 2, l0 = (int)Math.Round((region[0].y - o.y) / t) + 2;
            int k1 = (int)Math.Round((region[2].x - o.x) / t) - 2, l1 = (int)Math.Round((region[2].y - o.y) / t) - 2;
            int a = SkiRunAutoPlanner.ApronCells(t);
            var g1 = new SlopeGrid();
            var r1 = new SlopeResult();
            bool ok1 = SkiRunAutoPlanner.RectField(ref sampler, o, t, k0 - a, l0 - a, k1 + a, l1 + a, g1, r1);
            Check(ok1, "tiled field", "single call failed");
            var g2 = new SlopeGrid();
            var r2 = new SlopeResult();
            int diff = 0, cells = 0, tiles = 0;
            const int T = 37;   // odd tile size on purpose: tile seams fall everywhere
            for (int tl = l0; tl <= l1; tl += T)
                for (int tk = k0; tk <= k1; tk += T)
                {
                    int ek = Math.Min(k1, tk + T - 1), el = Math.Min(l1, tl + T - 1);
                    if (!SkiRunAutoPlanner.RectField(ref sampler, o, t, tk - a, tl - a, ek + a, el + a, g2, r2))
                    {
                        Check(false, "tiled field", $"tile {tk},{tl} failed");
                        continue;
                    }
                    tiles++;
                    for (int l = tl; l <= el; l++)
                        for (int k = tk; k <= ek; k++)
                        {
                            float v1 = r1.CellGrade[(l - g1.J0) * g1.Nx + (k - g1.I0)];
                            float v2 = r2.CellGrade[(l - g2.J0) * g2.Nx + (k - g2.I0)];
                            cells++;
                            if (math.asuint(v1) != math.asuint(v2))
                                diff++;
                        }
                }
            Check(diff == 0, "tiled field", $"{diff} of {cells} core cells differ from the single call");
            Console.WriteLine($"INFO  tiled field: {tiles} tiles, {cells} core cells, {diff} differ bit for bit from one Plan + Classify");
        }

        // ---------------------------------------------------------------- refusal ladder

        private static void Ladder()
        {
            Expect("ladder null", new AutoPlanInput(), AutoPlanStatus.BadInput);
            float2[] sq = Terrain.Square(0f, 0f, 450f);
            AutoPlanInput nan = Input(Terrain.Plane(0.3), sq);
            nan.Region = new[] { new float2(0, 0), new float2(float.NaN, 0), new float2(10, 10) };
            Expect("ladder NaN region", nan, AutoPlanStatus.BadInput);
            AutoPlanInput two = Input(Terrain.Plane(0.3), sq);
            two.Region = new[] { new float2(0, 0), new float2(10, 0) };
            Expect("ladder two points", two, AutoPlanStatus.BadInput);
            AutoPlanInput small = Input(Terrain.Plane(0.3), Terrain.Square(0f, 0f, 400f));
            small.Region = Terrain.Square(0f, 0f, 86f);   // 3 ha
            Expect("ladder 3 ha", small, AutoPlanStatus.TooSmall);
            float2[] big = Terrain.Square(0f, 0f, 550f);    // 1.21 km2
            Expect("ladder 1.21 km2 (9 km2 cap)", Input(Terrain.Plane(0.3), big), AutoPlanStatus.Ok);
            AutoPlanInput hostCap = Input(Terrain.Plane(0.3), big);
            hostCap.MaxAreaM2 = 1.0e6f;   // a host may pass a smaller cap
            Expect("ladder 1.21 km2 (1 km2 host cap)", hostCap, AutoPlanStatus.TooLarge);
            Expect("ladder 9.3 km2", Input(Terrain.Plane(0.3), Terrain.Rect(-1550f, -1500f, 1550f, 1500f)), AutoPlanStatus.TooLarge);
            AutoPlanInput stale = Input(Terrain.Plane(0.3), sq);
            stale.Region = Terrain.Square(800f, 0f, 450f);  // the snapshot was cut for another place
            Expect("ladder snapshot elsewhere", stale, AutoPlanStatus.BadInput);
            Expect("ladder flat", Input((x, z) => 900.0, sq), AutoPlanStatus.NoVertical);
            Expect("ladder 4 % (64 m)", Input(Terrain.Plane(0.04), Terrain.Rect(-300f, -800f, 300f, 800f)), AutoPlanStatus.TooFlat);
            // Cliff band: flat top, a 120 m drop over 60 m (200 %), flat bottom
            HeightFn cliff = (x, z) => z < -30 ? 1220.0 : z > 30 ? 1100.0 : 1160.0 - 2.0 * z;
            Expect("ladder cliff band", Input(cliff, sq), AutoPlanStatus.AllCliff);
            Expect("ladder all cliff", Input(Terrain.Plane(1.4), sq), AutoPlanStatus.AllCliff);
            var cover = new List<AutoObstacle> { new AutoObstacle { Kind = AutoObstacleKind.Surface, Ring = Terrain.Rect(-460f, -460f, 460f, 300f) } };
            Expect("ladder 82 % painted surface", Input(Terrain.Plane(0.3), sq, cover), AutoPlanStatus.Blocked);
            var tiles = new List<AutoObstacle> { new AutoObstacle { Kind = AutoObstacleKind.Unowned, Ring = Terrain.Rect(-460f, -460f, 300f, 460f) } };
            Expect("ladder unowned tiles", Input(Terrain.Plane(0.3), sq, tiles), AutoPlanStatus.Blocked);
            // Mostly off the map: the map ends at x = +7168 m
            float2[] edge = Terrain.Rect(7050f, -400f, 7650f, 400f);
            Expect("ladder off the map", Input(Terrain.Plane(0.3), edge), AutoPlanStatus.Blocked);
            // Ladder order: too large wins over everything. On a flat covered area, blocked comes before no-vertical
            Expect("ladder order flat + covered", Input((x, z) => 900.0, sq, cover), AutoPlanStatus.Blocked);
        }

        private static void Expect(string name, AutoPlanInput input, AutoPlanStatus want)
        {
            AutoPlanResult r = Plan(input);
            Check(r.Status == want, name, $"status {r.Status}, want {want}");
            if (r.Status == AutoPlanStatus.Ok)
                Invariants(name, input, r);
            Console.WriteLine($"INFO  {name}: {r.Status}{(r.Status == AutoPlanStatus.Ok ? $", {r.Runs.Count} runs {Mix(r)}" : "")}");
        }

        // ---------------------------------------------------------------- planes

        private static void Planes()
        {
            float2[] sq = Terrain.Square(0f, 0f, 450f);
            (double grade, SlopeBand band)[] planes = { (0.15, SlopeBand.Green), (0.30, SlopeBand.Blue), (0.45, SlopeBand.Black), (0.70, SlopeBand.DoubleBlack) };
            foreach (var (grade, band) in planes)
            {
                string name = $"plane {grade * 100:F0} %";
                AutoPlanInput input = Input(Terrain.Plane(grade), sq);
                input.Knobs.Mix = AutoMix.Terrain;
                AutoPlanResult r = PlanChecked(name, input);
                Check(r.Status == AutoPlanStatus.Ok && r.Runs.Count >= 2, name, $"{r.Status}, {r.Runs.Count} runs");
                bool all = true;
                foreach (AutoRun run in r.Runs)
                    all &= run.Band == band;
                Check(all, name, $"bands {Mix(r)}, all should be {band}");
            }
        }

        // ---------------------------------------------------------------- cone, ridge, bowl, valley

        private static void Shapes()
        {
            float2[] disc = Terrain.Disc(0f, 0f, 560f, 32);
            AutoPlanInput cone = Input((x, z) => 1500.0 - 0.35 * Math.Sqrt(x * x + z * z), disc);
            AutoPlanResult rc = PlanChecked("cone", cone);
            Check(rc.Status == AutoPlanStatus.Ok && rc.Runs.Count >= 3, "cone", $"{rc.Status}, {rc.Runs.Count} runs");
            if (rc.Clusters.Count > 0)
                Check(math.length(rc.Clusters[0].Center) < 30f, "cone", $"first cluster at {rc.Clusters[0].Center}, not the apex");
            Check(rc.Junctions.Count > 0 && rc.Clusters.Count > 0 && rc.Clusters[0].Linked >= 2, "cone",
                $"{rc.Junctions.Count} junctions, linked {ClusterText(rc)}: the apex lift should serve at least 2 runs");

            float2[] sq = Terrain.Square(0f, 0f, 450f);
            AutoPlanInput ridge = Input((x, z) => 1500.0 - 0.35 * Math.Abs(x), sq);
            AutoPlanResult rr = PlanChecked("ridge", ridge);
            Check(rr.Status == AutoPlanStatus.Ok && rr.Runs.Count >= 2, "ridge", $"{rr.Status}, {rr.Runs.Count} runs");
            int east = 0, west = 0;
            foreach (AutoRun run in rr.Runs)
            {
                bool pos = false, neg = false;
                foreach (float2 p in run.Ring)
                {
                    pos |= p.x > 15f;
                    neg |= p.x < -15f;
                }
                Check(!(pos && neg), "ridge", $"run {run.Id} crosses the crest");
                if (pos)
                    east++;
                if (neg)
                    west++;
            }
            Check(east > 0 && west > 0, "ridge", $"runs on both flanks (east {east}, west {west})");

            AutoPlanInput bowl = Input((x, z) => 1000.0 + 0.35 * Math.Max(0.0, Math.Sqrt(x * x + z * z) - 100.0), disc);
            AutoPlanResult rb = PlanChecked("bowl", bowl);
            Check(rb.Status == AutoPlanStatus.Ok && rb.Runs.Count >= 2, "bowl", $"{rb.Status}, {rb.Runs.Count} runs");
            foreach (AutoRun run in rb.Runs)
            {
                float2 top = run.Centerline[0], bottom = run.Centerline[run.Centerline.Length - 1];
                Check(math.length(bottom) < math.length(top), "bowl", $"run {run.Id} does not run inward");
            }

            // Valley side with a gully: a 30 % face with an 8 m deep, 40 m wide V notch down its middle
            HeightFn gully = (x, z) => 1500.0 - 0.3 * z - 8.0 * Math.Max(0.0, 1.0 - Math.Abs(x) / 20.0);
            AutoPlanResult rg = PlanChecked("gully", Input(gully, sq));
            Check(rg.Status == AutoPlanStatus.Ok, "gully", rg.Status.ToString());
            foreach (AutoRun run in rg.Runs)
            {
                int inGully = 0;
                foreach (float2 p in run.Centerline)
                    if (Math.Abs(p.x) < 6f)
                        inGully++;
                Check(inGully <= run.Centerline.Length / 2, "gully", $"run {run.Id} follows the gully ({inGully} of {run.Centerline.Length} samples)");
            }

            // The mixed mountain: a 50 % top, a 32 % middle, a 15 % lower slope, a flat base (Balanced should find G, B and K)
            AutoPlanResult rm = PlanChecked("mixed mountain", Input(Mixed, MixedRegion));
            Check(rm.Status == AutoPlanStatus.Ok && rm.BandCount[1] > 0 && rm.BandCount[2] > 0 && rm.BandCount[3] > 0, "mixed mountain",
                $"{rm.Status}, {Mix(rm)}: the variety bonus needs a green, a blue and a black");

            for (uint seed = 1; seed <= 5; seed++)
            {
                string name = $"fBm {seed}";
                AutoPlanResult rf = PlanChecked(name, Input(Terrain.Fbm(seed, 0.32, 70.0), Terrain.Square(0f, 0f, 480f)));
                Check(rf.Status == AutoPlanStatus.Ok, name, rf.Status.ToString());
            }
        }

        private static readonly float2[] MixedRegion = Terrain.Rect(-375f, -500f, 375f, 800f);   // 0.975 km2

        private static double Mixed(double x, double z)
        {
            // z from -500 (top) to +800 (bottom): 50 % to -300, 32 % to 150, 15 % to 750, then 2 %. The 15 % part is a 90 m beginner slope,
            // enough to clear the base band of the lowest 15 % of the vertical.
            double h = 2000.0;
            h -= 0.50 * (Math.Min(z, -300.0) + 500.0);
            if (z > -300.0) h -= 0.32 * (Math.Min(z, 150.0) + 300.0);
            if (z > 150.0) h -= 0.15 * (Math.Min(z, 750.0) - 150.0);
            if (z > 750.0) h -= 0.02 * (z - 750.0);
            return h + 6.0 * Math.Sin(x / 90.0);
        }

        // ---------------------------------------------------------------- obstacles

        private static void Obstacles()
        {
            float2[] region = MixedRegion;
            var obs = new List<AutoObstacle>
            {
                // an existing run down the middle, a painted surface, an unowned corner tile, a cat track across the slope
                new AutoObstacle { Kind = AutoObstacleKind.ExistingRun, Ring = Terrain.Rect(-20f, -300f, 20f, 300f) },
                new AutoObstacle { Kind = AutoObstacleKind.Surface, Ring = Terrain.Rect(200f, 100f, 300f, 200f) },
                new AutoObstacle { Kind = AutoObstacleKind.Unowned, Ring = Terrain.Rect(-460f, 350f, -250f, 560f) },
                new AutoObstacle { Kind = AutoObstacleKind.CatTrack, Ring = Terrain.Rect(-460f, 0f, 460f, 12f) },
                new AutoObstacle { Kind = AutoObstacleKind.Surface, Ring = new[] { new float2(-400f, -100f), new float2(-300f, -60f) } },
            };
            // A stream across the blue slope
            AutoPlanInput input = Input(Mixed, region, obs, (x, z) => Math.Abs(z - 60.0) < 4.0);
            AutoPlanResult r = PlanChecked("obstacles", input);
            Check(r.Status == AutoPlanStatus.Ok, "obstacles", r.Status.ToString());
            Check((r.Warnings & AutoPlanWarning.CrossesCatTrack) != 0, "obstacles", "no run crosses the cat track (it must be soft)");
            Check((r.Warnings & AutoPlanWarning.OverWater) != 0, "obstacles", "no run over the stream warned (water is a warning only)");
            int existing = 0;
            foreach (AutoJunction j in r.Junctions)
                if (j.Existing)
                    existing++;
            Console.WriteLine($"INFO  obstacles: {existing} junction(s) to the existing run");
            // Every ExistingRun node within reach of a generated ring has a link (one per run and obstacle)
            for (int a = 0; a < r.Runs.Count; a++)
            {
                bool near = false, has = false;
                foreach (float2 v in obs[0].Ring)
                    near |= SkiRunAutoPlanner.PointRingDistance(r.Runs[a].Ring, v) <= SkiRunAutoPlanner.JunctionReachM - 0.01f;
                foreach (AutoJunction j in r.Junctions)
                    has |= j.Existing && j.RunA == a && j.RunB == 0;
                if (near)
                    Check(has, "obstacles", $"run {r.Runs[a].Id} reaches the existing run's node but has no junction");
            }
        }

        // ---------------------------------------------------------------- knobs

        private static void Knobs()
        {
            float2[] region = MixedRegion;
            AutoPlanResult beg = PlanKnob("mix Beginner", region, k => k.Mix = AutoMix.Beginner);
            AutoPlanResult exp = PlanKnob("mix Expert", region, k => k.Mix = AutoMix.Expert);
            Check(beg.BandCount[1] >= exp.BandCount[1], "mix", $"Beginner greens {beg.BandCount[1]} < Expert greens {exp.BandCount[1]}");
            Check(exp.BandCount[3] + exp.BandCount[4] >= beg.BandCount[3] + beg.BandCount[4], "mix", "Expert has fewer black runs than Beginner");
            AutoPlanResult sparse = PlanKnob("density Sparse", region, k => k.Density = AutoDensity.Sparse);
            AutoPlanResult dense = PlanKnob("density Dense", region, k => k.Density = AutoDensity.Dense);
            Check(sparse.Runs.Count <= 8 && dense.Runs.Count >= sparse.Runs.Count, "density", $"sparse {sparse.Runs.Count}, dense {dense.Runs.Count}");
            AutoPlanResult groomed = PlanKnob("kinds groomed only", region, k => k.Kinds = AutoKinds.Groomed);
            foreach (AutoRun run in groomed.Runs)
                Check(run.Kind == RunKind.Groomed, "kinds groomed only", $"run {run.Id} is {run.Kind}");
            AutoPlanResult all = PlanKnob("kinds all", region, k => k.Kinds = AutoKinds.Groomed | AutoKinds.Natural | AutoKinds.Snowmaking | AutoKinds.Night);
            int night = 0;
            foreach (AutoRun run in all.Runs)
            {
                if (run.Kind == RunKind.Night)
                    night++;
                if (run.Band >= SlopeBand.Black)
                    Check(run.Kind == RunKind.Natural, "kinds all", $"black run {run.Id} is {run.Kind}");
            }
            Check(night <= 2, "kinds all", $"{night} night runs");
            AutoPlanResult natural = PlanKnob("kinds natural only", region, k => k.Kinds = AutoKinds.Natural);
            foreach (AutoRun run in natural.Runs)
                Check(run.Kind == RunKind.Natural, "kinds natural only", $"run {run.Id} is {run.Kind}");
            int[] t = new int[5];
            SkiRunAutoPlanner.Targets(AutoMix.Balanced, 12, t);
            Check(t[1] + t[2] + t[3] + t[4] == 12 && t[1] >= 1 && t[2] >= 1 && t[3] >= 1, "targets", $"Balanced 12: {t[1]}/{t[2]}/{t[3]}/{t[4]}");
            SkiRunAutoPlanner.Targets(AutoMix.Balanced, 40, t);
            Check(t[4] >= 1 && t[4] <= 2, "targets", $"Balanced 40 double blacks {t[4]} (about 1 in 20 to 30)");
            Console.WriteLine($"INFO  targets: Balanced 40 = {t[1]}/{t[2]}/{t[3]}/{t[4]}");
        }

        private static AutoPlanResult PlanKnob(string name, float2[] region, Action<KnobBox> set)
        {
            AutoPlanInput input = Input(Mixed, region);
            var box = new KnobBox { K = input.Knobs };
            set(box);
            input.Knobs = box.K;
            return PlanChecked(name, input);
        }

        private sealed class KnobBox
        {
            public AutoPlanKnobs K;
            public AutoMix Mix { set => K.Mix = value; }
            public AutoDensity Density { set => K.Density = value; }
            public AutoKinds Kinds { set => K.Kinds = value; }
        }

        // ---------------------------------------------------------------- determinism

        private static void Determinism()
        {
            float2[] region = Terrain.Square(0f, 0f, 480f);
            HeightFn f = Terrain.Fbm(4u, 0.32, 70.0);
            AutoPlanInput input = Input(f, region);
            ulong a = Plan(input).Fingerprint, b = Plan(input).Fingerprint;
            Check(a == b, "determinism", "rerun differs");
            AutoPlanInput rot = Input(f, new[] { region[2], region[3], region[0], region[1] });
            Check(Plan(rot).Fingerprint == a, "determinism", "rotated start vertex changes the layout");
            AutoPlanInput rev = Input(f, new[] { region[3], region[2], region[1], region[0] });
            Check(Plan(rev).Fingerprint == a, "determinism", "reversed region changes the layout");
            // Sliced: a tiny budget gives the same result as one call
            var p = new SkiRunAutoPlanner();
            p.Begin(input);
            int steps = 0;
            while (!p.Step(0.05f))
                steps++;
            Check(p.Result.Fingerprint == a, "determinism", "sliced Step differs from Run");
            Console.WriteLine($"INFO  determinism: sliced run took {steps + 1} Step calls, {p.Result.Units} units, max unit {p.Result.MaxUnitMs:F2} ms");
            // Reselect equals a fresh run with the same knobs (mix, kinds, seed)
            AutoPlanKnobs k2 = input.Knobs;
            k2.Mix = AutoMix.Expert;
            ReselectCheck("reselect mix", input, k2);
            AutoPlanKnobs k3 = input.Knobs;
            k3.Kinds = AutoKinds.Groomed | AutoKinds.Snowmaking | AutoKinds.Night;
            ReselectCheck("reselect kinds", input, k3);
            AutoPlanKnobs k4 = input.Knobs;
            k4.Seed = SkiRunAutoPlanner.SeedOf(region, 7);
            ReselectCheck("reselect seed", input, k4);
            AutoPlanKnobs k5 = input.Knobs;
            k5.Density = AutoDensity.Dense;
            ReselectCheck("reselect density", input, k5);
            AutoPlanInput shuffled = Input(f, region);
            shuffled.Knobs.Seed = SkiRunAutoPlanner.SeedOf(region, 3);
            Check(Plan(shuffled).Fingerprint != a, "determinism", "Shuffle (another seed) gives the same layout");
            // Density with an existing run: its clearance is the density gap, rastered in stage 0, so Reselect restarts the scan
            var existing = new List<AutoObstacle> { new AutoObstacle { Kind = AutoObstacleKind.ExistingRun, Ring = Terrain.Rect(-20f, -300f, 20f, 300f) } };
            AutoPlanInput withRun = Input(Mixed, MixedRegion, existing);
            AutoPlanKnobs k6 = withRun.Knobs;
            k6.Density = AutoDensity.Sparse;
            ReselectCheck("reselect density, existing run", withRun, k6);
            Check(withRun.Knobs.Density == AutoDensity.Normal, "reselect density, existing run", "Reselect changed the caller's input knobs");
            // F5 (quick runs fix round): the check above must not pass trivially. Three checks follow:
            // (1) the existing run changes the layout.
            // (2) The density changes it WITH the run present.
            // (3) Dense and a chain of Reselects equal a fresh run too.
            AutoPlanInput without = Input(Mixed, MixedRegion);
            ulong fpRun = Plan(withRun).Fingerprint, fpNone = Plan(without).Fingerprint;
            Check(fpRun != fpNone, "reselect density, existing run", "the existing run does not change the layout (the case proves nothing)");
            // AutoPlanInput is a class: build separate inputs (an alias would share one Knobs)
            AutoPlanInput sparseRun = Input(Mixed, MixedRegion, existing), denseRun = Input(Mixed, MixedRegion, existing);
            sparseRun.Knobs.Density = AutoDensity.Sparse;
            denseRun.Knobs.Density = AutoDensity.Dense;
            ulong fpSparse = Plan(sparseRun).Fingerprint, fpDense = Plan(denseRun).Fingerprint;
            Check(fpSparse != fpRun && fpDense != fpRun && fpSparse != fpDense, "reselect density, existing run",
                  "the density does not change the layout beside an existing run (Normal " + fpRun + ", Sparse " + fpSparse + ", Dense " + fpDense + ")");
            AutoPlanKnobs k7 = withRun.Knobs;
            k7.Density = AutoDensity.Dense;
            ReselectCheck("reselect density Dense, existing run", withRun, k7);
            // A chain on ONE planner: Normal -> Sparse -> Dense -> Normal ends where a fresh Normal run starts
            var chain = new SkiRunAutoPlanner();
            chain.Run(withRun);
            foreach (AutoDensity d in new[] { AutoDensity.Sparse, AutoDensity.Dense, AutoDensity.Normal })
            {
                AutoPlanKnobs kd = withRun.Knobs;
                kd.Density = d;
                Check(chain.Reselect(kd), "reselect chain, existing run", "Reselect refused at " + d);
                while (!chain.Step(float.PositiveInfinity))
                {
                }
                ulong want = d == AutoDensity.Sparse ? fpSparse : d == AutoDensity.Dense ? fpDense : fpRun;
                Check(chain.Result.Fingerprint == want, "reselect chain, existing run", "after Reselect to " + d + " the layout differs from a fresh run");
            }
        }

        private static void ReselectCheck(string name, AutoPlanInput input, AutoPlanKnobs knobs)
        {
            var p = new SkiRunAutoPlanner();
            p.Run(input);
            Check(p.Reselect(knobs), name, "Reselect refused");
            while (!p.Step(float.PositiveInfinity))
            {
            }
            ulong got = p.Result.Fingerprint;
            AutoPlanInput fresh = new AutoPlanInput { Snapshot = input.Snapshot, Region = input.Region, Obstacles = input.Obstacles, Knobs = knobs };
            ulong want = Plan(fresh).Fingerprint;
            Check(got == want, name, "differs from a fresh run with the same knobs");
        }

        // ---------------------------------------------------------------- captures

        private static void CaptureTests()
        {
            string dir = Path.Combine("tests", "SlopeBatch", "captures");
            if (!Directory.Exists(dir))
            {
                Check(false, "captures", $"{dir} not found (run from the ModularSkiResort root)");
                return;
            }
            int n = 0, ok = 0, match = 0;
            foreach (string file in Directory.GetFiles(dir, "*.skislope"))
            {
                Capture c = Capture.Load(file);
                HeightSnapshot s = c.Snapshot();
                if (s == null)
                {
                    Console.WriteLine($"INFO  capture {c.Name}: skipped (multiple {c.Multiple} or another map origin)");
                    continue;
                }
                n++;
                var input = new AutoPlanInput
                {
                    Snapshot = s, Region = c.Ring, Knobs = AutoPlanKnobs.Default(SkiRunAutoPlanner.SeedOf(c.Ring, 0)),
                    MinAreaM2 = 100f, MinVerticalM = 5f,
                };
                input.Knobs.Mix = AutoMix.Terrain;
                AutoPlanResult r = Plan(input);
                if (r.Status == AutoPlanStatus.Ok)
                {
                    ok++;
                    Invariants($"capture {c.Name}", input, r);
                    AutoRun best = r.Runs[0];
                    foreach (AutoRun run in r.Runs)
                        if (run.AreaM2 > best.AreaM2)
                            best = run;
                    if (best.Band.ToString() == c.RatedBand)
                        match++;
                    Console.WriteLine($"INFO  capture {c.Name}: Ok, {r.Runs.Count} runs, largest {best.Band} {best.AreaM2:F0} m2 (capture rated {c.RatedBand})");
                }
                else
                {
                    Console.WriteLine($"INFO  capture {c.Name}: {r.Status} (rejects [{Rejects(r)}]; capture rated {c.RatedBand}, {SkiRunAutoPlanner.PolygonArea(c.Ring):F0} m2)");
                }
            }
            Check(n > 0, "captures", "no capture loaded");
            Console.WriteLine($"INFO  captures: {n} replayed, {ok} produced runs, {match} with the largest run in the capture's band");
        }

        // ---------------------------------------------------------------- timing

        private static void Timing()
        {
            foreach (float km2 in new[] { 1f, 4f, 9f })
            {
                float half = 500f * (float)Math.Sqrt(km2) - 1f;
                AutoPlanInput input = Input(Terrain.Fbm(21u, 0.30, 90.0), Terrain.Square(0f, 0f, half));
                var p = new SkiRunAutoPlanner();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                p.Begin(input);
                int calls = 0;
                while (!p.Step(8f))
                    calls++;
                sw.Stop();
                AutoPlanResult r = p.Result;
                if (r.Status == AutoPlanStatus.Ok)
                    Invariants($"timing {km2} km2", input, r);
                Check(r.Status == AutoPlanStatus.Ok, $"timing {km2} km2", r.Status.ToString());
                float[] ms = r.StageMs;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "TIME  autogen {0} km2 (CoreCLR): total {1:F0} ms in {2} Step(8 ms) calls, {3} units, max unit {4:F1} ms; " +
                    "heights {5:F1}, obstacles {6:F1}, field {7:F1}, distance {8:F1}, flow {9:F1}, usable+zones {10:F1}, minimax {11:F1}, " +
                    "routes {12:F1}, seeds+candidates {13:F1}, select {14:F1} ({15} ratings), finish {16:F1} ms; {17} runs {18}",
                    km2, sw.Elapsed.TotalMilliseconds, calls + 1, r.Units, r.MaxUnitMs,
                    ms[0], ms[1], ms[2], ms[3] + ms[4], ms[5] + ms[6], ms[7] + ms[8], ms[9], ms[10], ms[11] + ms[12], ms[13], r.Rated, ms[14],
                    r.Runs.Count, Mix(r)));
            }
        }
    }
}
