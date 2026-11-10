// Runs-first realizer gates (REALIZER-SPEC.md section 10), in the default host run:
// - the synthetic regions (V2Fixtures.Synthetic: Alpine1, Benched1, Steep1 full): the planner plans, the realizer realizes, and every
//   ring passes the invariants (3 nodes, 2 m node spacing, GradeRibbon.Clear, inside the region, the band = a fresh rate), no overlap
//   the game refuses, the relation gate, the same hash on a second run;
// - the easiest descent on a hand graph with known answers, and against a reference linear-scan Dijkstra on random graphs (both
//   moved from the deleted v2 FlowTests in port round R2-d);
// - hand fixtures: hand-made plans on a plane, each with the expected pieces, junctions and links.
// The real sets run in the rf-realize host mode (RealizerHost.cs): numbers only, no checks.

using System;
using System.Collections.Generic;
using System.Linq;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class Program
    {
        private static void RealizerTests()
        {
            RealizerSynthetic();
            RealizerDescent();
            RealizerHandFixtures();
            BottomFixtures();
        }

        // ---------------------------------------------------------------- the way to the bottom (R2-c, plan 5.1 to 5.4)

        /// <summary>Planned lifts with skiing that reach the base band, and those with skiing (the planner's own read).</summary>
        private static (int Reach, int Skiing) PlannerBottom(RunsFirstResult plan) =>
            (plan.Lifts.Count(l => l.ToBottom == RunsFirstBottom.Reaches), plan.Lifts.Count(l => l.ToBottom != RunsFirstBottom.NoSkiing));

        /// <summary>The landing class the pass logged (0: no pass line).</summary>
        private static int LandingClass(RunsFirstResult plan)
        {
            string l = plan.Log.FirstOrDefault(s => s.StartsWith("way to the bottom: ", StringComparison.Ordinal));
            int at = l?.IndexOf("landing class ", StringComparison.Ordinal) ?? -1;
            return at >= 0 ? l[at + 14] - '0' : 0;
        }

        /// <summary>A height function of z alone, piecewise linear through (z, h) knots (flat past the ends).</summary>
        private static HeightFn Profile(params double[] zh) => (x, z) =>
        {
            if (z <= zh[0])
                return zh[1];
            for (int i = 2; i + 1 < zh.Length; i += 2)
                if (z <= zh[i])
                    return zh[i - 1] + (zh[i + 1] - zh[i - 1]) * (z - zh[i - 2]) / (zh[i] - zh[i - 2]);
            return zh[zh.Length - 1];
        };

        /// <summary>Six hand fixtures: one lift falling toward +z on a 1 x 2 km region, its run ending above the base band.</summary>
        private static void BottomFixtures()
        {
            float2[] region = Terrain.Rect(-500f, -1000f, 500f, 1000f);
            RunsFirstResult Probe(string name, HeightFn f, float bottomZ, Func<double, double, bool> wet = null, RunsFirstInputs inputs = null)
            {
                HeightSnapshot snap = Terrain.Snapshot(f, region, wet);
                var planner = new RunsFirstPlanner { Id = "bottom " + name, Inputs = inputs };
                RunsFirstResult plan = planner.BottomProbe(snap, region, new List<(float2, float2)> { (new float2(0f, bottomZ), new float2(0f, -950f)) },
                    new List<List<float2>> { Line(0f, -930f, 0f, bottomZ - 10f) }, new RunsFirstOptions { ScanThreads = 2 });
                // the realized plan and B-1 (pad-based: it sees a landing at a lift bottom only, so landings of class 2 to 4 read stranded)
                AutoPlanInput input = RealizerHost.InputOf(snap, region);
                AutoPlanResult res = RunsFirstRealizer.Realize(plan, input, new RealizeReport());
                Gate b1 = res.Resort != null ? V2Metrics.B1WayToBottom(res, res.Resort) : default;
                var (reach, skiing) = PlannerBottom(plan);
                Console.WriteLine($"REPORT bottom fixture {name}: {plan.Lifts[0].ToBottom}, landing class {LandingClass(plan)}, {plan.Connectors.Count} track(s), " +
                    $"{plan.Runs.Count(r => r.Kind == "bottom")} run(s) to the bottom; planner {reach}/{skiing}, realized B-1 {plan.Lifts.Count - (int)b1.Value}/{plan.Lifts.Count}" +
                    $" ({res.Warnings}); " + string.Join("; ", plan.Log.Where(s => s.Contains("to the bottom"))));
                Check(res.Warnings.HasFlag(AutoPlanWarning.NoWayToBottom) == (reach < skiing), "Bottom fixture " + name,
                    $"NoWayToBottom warning {res.Warnings.HasFlag(AutoPlanWarning.NoWayToBottom)}, {reach}/{skiing} lifts reach the bottom");
                return plan;
            }
            // 1. a bench above the base: the run ends on the bench, a trail goes on down the gentle slope below it
            {
                const string w = "Bottom fixture: bench above a base";
                RunsFirstResult p = Probe("bench", Profile(-1000, 1800, -200, 1520, 100, 1514, 1000, 1334), -220f);
                Check(p.Lifts.Count == 1 && p.Lifts[0].ToBottom == RunsFirstBottom.Reaches, w, $"the lift does not reach the bottom: {p.Lifts[0].ToBottom}");
                Check(p.Connectors.Count + p.Runs.Count(r => r.Kind == "bottom") >= 1, w, "no trail added");
                Check(p.Log.Any(s => s.Contains("0 reached the bottom before")), w, "the lift reached the bottom before the pass (the fixture tests nothing)");
            }
            // 2. a cliff band across the whole region: no trail, the lift stays, the result names the cliff
            {
                const string w = "Bottom fixture: cliff band";
                RunsFirstResult p = Probe("cliff", Profile(-1000, 1800, 0, 1500, 40, 1440, 1000, 1152), -50f);
                Check(p.Lifts.Count == 1 && p.Lifts[0].ToBottom == RunsFirstBottom.Cliff, w, $"want Cliff, got {p.Lifts[0].ToBottom}");
                Check(p.Connectors.Count == 0 && p.Runs.Count == 1, w, $"{p.Connectors.Count} track(s), {p.Runs.Count} run(s): want none added");
            }
            // 3. a lake across the foot of the lift's skiing (water the plan may not use)
            {
                const string w = "Bottom fixture: lake at the foot";
                RunsFirstResult p = Probe("lake", Terrain.Plane(0.25), 180f, (x, z) => z >= 200 && z <= 320, new RunsFirstInputs { Water = true });
                Check(p.Lifts.Count == 1 && p.Lifts[0].ToBottom == RunsFirstBottom.Water, w, $"want Water, got {p.Lifts[0].ToBottom}");
                Check(p.Connectors.Count == 0 && p.Runs.Count == 1, w, $"{p.Connectors.Count} track(s), {p.Runs.Count} run(s): want none added");
            }
            // 4. a hole in the region across the way (blocked ground, region below it)
            {
                const string w = "Bottom fixture: hole across the way";
                var inputs = new RunsFirstInputs();
                inputs.Blocked.Add(new RunsFirstInputs.Shape { Pts = Terrain.Rect(-600f, 200f, 600f, 320f), ClearM = 0f });
                RunsFirstResult p = Probe("hole", Terrain.Plane(0.25), 180f, null, inputs);
                Check(p.Lifts.Count == 1 && p.Lifts[0].ToBottom == RunsFirstBottom.Hole, w, $"want Hole, got {p.Lifts[0].ToBottom}");
                Check(p.Connectors.Count == 0 && p.Runs.Count == 1, w, $"{p.Connectors.Count} track(s), {p.Runs.Count} run(s): want none added");
            }
            // 5. a steep bottom band (no flat ground, no lift bottom in it): the landing is the band itself (class 4)
            {
                const string w = "Bottom fixture: steep bottom band";
                RunsFirstResult p = Probe("steep", Terrain.Plane(0.30), 300f);
                Check(LandingClass(p) == 4, w, $"landing class {LandingClass(p)}, want 4");
                Check(p.Lifts.Count == 1 && p.Lifts[0].ToBottom == RunsFirstBottom.Reaches, w, $"the lift does not reach the bottom: {p.Lifts[0].ToBottom}");
            }
            // 6. one access point at a flat foot (class 2): the trail ends in reach of it
            {
                const string w = "Bottom fixture: access point at the foot";
                var access = new float2(300f, 950f);
                RunsFirstResult p = Probe("access", Profile(-1000, 1750, 700, 1325, 1000, 1316), 100f, null, new RunsFirstInputs { Access = new[] { access } });
                Check(LandingClass(p) == 2, w, $"landing class {LandingClass(p)}, want 2");
                Check(p.Lifts.Count == 1 && p.Lifts[0].ToBottom == RunsFirstBottom.Reaches, w, $"the lift does not reach the bottom: {p.Lifts[0].ToBottom}");
                float d = p.Connectors.Concat(p.Runs.Where(r => r.Kind == "bottom").Select(r => r.Pts)).Select(c => math.distance(c[c.Count - 1], access)).DefaultIfEmpty(float.MaxValue).Min();
                Check(d < 170f, w, $"the trail ends {d:F0} m from the access point (want within 150 m and a cell)");
            }
        }

        // ---------------------------------------------------------------- invariants on a realized plan

        /// <summary>The HARD invariants of one realized plan. Returns the relation misses not excused by a blocked extension.</summary>
        private static void RealizerInvariants(string where, AutoPlanResult res, AutoPlanInput input, RealizeReport rep)
        {
            HeightSnapshot snap = input.Snapshot;
            var grid = new SlopeGrid();
            var rr = new SlopeResult();
            for (int i = 0; i < res.Runs.Count; i++)
            {
                AutoRun r = res.Runs[i];
                string w = $"{where} area {i} (source {r.Source} segment {r.Segment})";
                float2[] ring = r.Ring;
                Check(ring != null && ring.Length >= 3, w, $"ring with {ring?.Length ?? 0} node(s)");
                if (ring == null || ring.Length < 3)
                    continue;
                float near = float.PositiveInfinity;
                for (int k = 0; k < ring.Length; k++)
                    near = Math.Min(near, math.distance(ring[k], ring[(k + 1) % ring.Length]));
                Check(near >= GradeRibbon.MinSpacingM - 1e-3f, w, $"two neighbor nodes {near:F2} m apart (under {GradeRibbon.MinSpacingM} m)");
                Check(GradeRibbon.Clear(ring, GradeRibbon.MinClearanceM), w, "the ring is not GradeRibbon.Clear");
                bool inside = ring.All(p => GradeRaster.Inside(input.Region, p)) &&
                    !(PlanGeometry.EdgeDistance(ring, input.Region, RunsFirstRealizer.RegionClearanceM, out _, out _, out _, out _) < RunsFirstRealizer.RegionClearanceM - 1e-3f);
                Check(inside, w, "a node outside the region (or an edge within 0.25 m of its edge)");
                // the type rule: a piste keeps every hard obstacle's clearance, a track only the unowned tiles'
                List<AutoObstacle> hard = r.Kind == RunKind.CatTrack ? input.Obstacles?.Where(o => o.Kind == AutoObstacleKind.Unowned).ToList() : input.Obstacles;
                Check(PlanGeometry.ObstaclesClear(ring, hard, input.Knobs.Density), w, "the ring violates an obstacle's clearance");
                if (r.Kind != RunKind.CatTrack)
                {
                    var nodes = new float3[ring.Length];
                    var sampler = new SnapshotSampler(snap);
                    for (int k = 0; k < ring.Length; k++)
                        nodes[k] = new float3(ring[k].x, snap.Sample(new float3(ring[k].x, 0f, ring[k].y)), ring[k].y);
                    RateStatus rs = SkiRunRater.Rate(nodes, nodes.Length, snap.Origin, ref sampler, snap.TexelM, 0, grid, null, rr, out int _, out float _, out float _);
                    Check(rs == RateStatus.Rated && (rr.Status == SlopeStatus.Ok || (r.Graded && rr.Status == SlopeStatus.ShortRun)) && rr.Band == r.Band, w, $"band {r.Band}, a fresh rate gives {rs} {rr.Status} {rr.Band}");
                }
            }
            // No overlap the game refuses: same type (piste / piste, track / track), at least 1.6 m, no vertex of one inside the other
            for (int i = 0; i < res.Runs.Count; i++)
                for (int j = i + 1; j < res.Runs.Count; j++)
                {
                    AutoRun a = res.Runs[i], b = res.Runs[j];
                    if ((a.Kind == RunKind.CatTrack) != (b.Kind == RunKind.CatTrack))
                        continue;
                    float d = PlanGeometry.RingDistance(a.Ring, b.Ring, 2f, out _, out _, out _, out _);
                    bool inside = a.Ring.Any(p => GradeRaster.Inside(b.Ring, p)) || b.Ring.Any(p => GradeRaster.Inside(a.Ring, p));
                    Check(d >= 1.6f - 1e-3f && !inside, where, $"areas {i} and {j} ({a.Kind}, {b.Kind}) {d:F2} m apart{(inside ? ", a node inside the other" : "")}");
                }
            // The relation gate: every planner relation between two kept areas is a game link (spec 10)
            Check(rep.RelationMisses == 0, where, $"{rep.RelationMisses} of {rep.Relations} planner relations are not game links: " +
                string.Join("; ", rep.Lines.Where(s => s.StartsWith("relation not linked")).Take(4)));
        }

        // ---------------------------------------------------------------- the synthetic regions

        private static void RealizerSynthetic()
        {
            var cases = V2Fixtures.Synthetic();
            foreach (string id in new[] { "Alpine1 full", "Benched1 full", "Steep1 full" })
            {
                V2Case c = cases.First(x => x.Id == id);
                HeightSnapshot snap = c.Snap();
                RunsFirstResult plan = RunsFirst.PlanRegion(snap, c.Region, new RunsFirstOptions { ScanThreads = 4 }, id);
                AutoPlanInput input = RealizerHost.InputOf(snap, c.Region);
                var rep = new RealizeReport();
                AutoPlanResult res = RunsFirstRealizer.Realize(plan, input, rep);
                string where = "Realizer " + id;
                ulong a = RunsFirstRealizer.Hash(res), b = RunsFirstRealizer.Hash(RunsFirstRealizer.Realize(plan, RealizerHost.InputOf(snap, c.Region), null));
                Check(res.Status == AutoPlanStatus.Ok && res.Runs.Count > 0, where, $"status {res.Status}, {res.Runs.Count} areas");
                Check(a == b, where, $"second run {b:x16} != first {a:x16}");
                Check(res.Resort != null && res.Resort.Lifts.Length == plan.Lifts.Count, where, "the plan does not carry one PlanLift per planner lift");
                RealizerInvariants(where, res, input, rep);
                // the way to the bottom: the planner's read and B-1 on the realized plan (pad-based, so a landing of class 1 only)
                var (reach, skiing) = PlannerBottom(plan);
                Gate b1 = V2Metrics.B1WayToBottom(res, res.Resort);
                int b1Reach = plan.Lifts.Count - (int)b1.Value;
                Console.WriteLine($"REPORT realizer {id} way to the bottom: planner {reach}/{skiing}, landing class {LandingClass(plan)}, realized B-1 {b1Reach}/{plan.Lifts.Count}" +
                    $" ({b1.Text}); " + string.Join(", ", plan.Lifts.Where(l => l.ToBottom != RunsFirstBottom.Reaches).Select(l => $"L{l.Id} {l.ToBottom}")));
                if (LandingClass(plan) == 1)
                    Check(b1Reach == reach, where,
                        $"way to the bottom: the planner reads {reach} lift(s) reaching the base band, B-1 on the realized plan {b1Reach}");
                Check(res.Warnings.HasFlag(AutoPlanWarning.NoWayToBottom) == (reach < skiing), where, $"NoWayToBottom warning {res.Warnings.HasFlag(AutoPlanWarning.NoWayToBottom)}, {reach}/{skiing} lifts reach the bottom");
                double kmP = rep.PisteKmPlanned + rep.GradedKmPlanned, kmK = rep.PisteKmKept + rep.GradedKmKept;
                Console.WriteLine($"REPORT realizer {id}: {res.Runs.Count} areas, piste km {kmK:F2}/{kmP:F2}, track km {rep.TrackKmKept:F2}/{rep.TrackKmPlanned:F2}, " +
                    $"relations {rep.RelationLinks}/{rep.Relations}, unserved {rep.UnservedAreas}, dead ends {rep.DeadEnds}, {rep.Ms:F0} ms, hash {a:x16}" +
                    string.Concat(rep.Lines.Where(l => l.StartsWith("relation not linked") || l.Contains("cannot reach") || l.Contains(": spur ") || l.Contains(" merged into ")).Select(l => "\n  " + l)));
                string only = Environment.GetEnvironmentVariable("RZ_LINES");   // debugging: every report line that contains one of these texts (| separates them)
                if (!string.IsNullOrEmpty(only))
                    foreach (string l in rep.Lines.Where(l => only.Split('|').Any(o => l.Contains(o))))
                        Console.WriteLine("  RZ " + l);
            }
        }

        // ---------------------------------------------------------------- the descent copy

        private static PlanEdge E(int from, int to, byte kind, SlopeBand band, int run = -1) =>
            new PlanEdge { From = from, To = to, Kind = kind, Band = band, Run = run };

        private static void RealizerDescent()
        {
            RealizerDescentHandGraph();
            RealizerDescentHeapEquality();
        }

        /// <summary>Nodes: 0 base pad, 1 lift top, 2/3 run A (black, short), 4/5 run B (blue), 6/7 run C (green, joined from B's bottom),
        /// 8 lift bottom, 9/10 run D (blue, longer than B), 11 an island. The easiest descent from 1 is B then C (hardest band Blue), not A
        /// (Black, shorter) and not D (Blue, longer). The descent never rides the lift edge 1 -> 0 down.</summary>
        private static void RealizerDescentHandGraph()
        {
            const string w = "Realizer descent";
            const byte Run = RunsFirstRealizer.EdgeRun, Lift = RunsFirstRealizer.EdgeLift, Link = RunsFirstRealizer.EdgeLink, Cat = RunsFirstRealizer.EdgeCatTrack;
            var g = new List<PlanEdge>
            {
                E(1, 2, Link, SlopeBand.Flat), E(2, 3, Run, SlopeBand.Black, 0), E(3, 0, Link, SlopeBand.Flat),
                E(1, 4, Link, SlopeBand.Flat), E(4, 5, Run, SlopeBand.Blue, 1), E(5, 7, Link, SlopeBand.Green, 2),
                E(6, 7, Run, SlopeBand.Green, 2), E(7, 0, Link, SlopeBand.Flat),
                E(1, 9, Link, SlopeBand.Flat), E(9, 10, Run, SlopeBand.Blue, 3), E(10, 0, Link, SlopeBand.Flat),
                E(1, 0, Lift, SlopeBand.Flat), E(0, 8, Link, SlopeBand.Flat), E(8, 1, Lift, SlopeBand.Flat),
            };
            double[] m = { 10, 300, 10, 10, 600, 200, 400, 10, 10, 1200, 10, 1, 10, 900 };
            var target = new bool[12];
            target[0] = true;
            int band = RunsFirstRealizer.EasiestDescent(12, g.ToArray(), m, 1, target, 1, out int run);
            Check(band == (int)SlopeBand.Blue && run == 1, w, $"easiest descent band {band}, first run {run} (want Blue via run 1)");

            // the black run made green: now A is easiest (Green, shortest)
            g[1] = E(2, 3, Run, SlopeBand.Green, 0);
            band = RunsFirstRealizer.EasiestDescent(12, g.ToArray(), m, 1, target, 1, out run);
            Check(band == (int)SlopeBand.Green && run == 0, w, $"after the regrade: band {band}, first run {run} (want Green via run 0)");

            // ties by length: B blocked, D is the only blue
            g[1] = E(2, 3, Run, SlopeBand.Black, 0);
            g[5] = E(5, 11, Link, SlopeBand.Green, 2);
            band = RunsFirstRealizer.EasiestDescent(12, g.ToArray(), m, 1, target, 1, out run);
            Check(band == (int)SlopeBand.Blue && run == 3, w, $"B dead-ended: band {band}, first run {run} (want Blue via run 3)");

            // no path at all (only the lift edge reaches the pad)
            var lone = new[] { E(1, 0, Lift, SlopeBand.Flat), E(1, 11, Run, SlopeBand.Green, 0) };
            band = RunsFirstRealizer.EasiestDescent(12, lone, new double[] { 1, 1 }, 1, target, 1, out run);
            Check(band == -1 && run == -1, w, $"no way home: band {band}, run {run} (want -1, -1)");

            // the search skips a link from the top onto a pad (getting off on the top pad is no way home). It does not skip a cat track
            var hop = new[] { E(1, 0, Link, SlopeBand.Flat) };
            band = RunsFirstRealizer.EasiestDescent(12, hop, new double[] { 1 }, 1, target, 1, out run);
            Check(band == -1, w, $"top pad hop counted as a way home (band {band})");
            var cat = new[] { E(1, 0, Cat, SlopeBand.Green) };
            band = RunsFirstRealizer.EasiestDescent(12, cat, new double[] { 1 }, 1, target, 1, out run);
            Check(band == (int)SlopeBand.Green && run == -1, w, $"cat track from the top: band {band} (want Green)");
        }

        /// <summary>The heap Dijkstra settles in the linear scan's order: same band and first run as a reference O(V^2) scan (ties by
        /// node index) on random graphs with many equal lengths, every source, two target sets.</summary>
        private static void RealizerDescentHeapEquality()
        {
            const string w = "Realizer descent heap";
            var rng = new System.Random(451);
            int bad = 0, cases = 0;
            for (int t = 0; t < 40; t++)
            {
                int n = 6 + rng.Next(40), m = n + rng.Next(4 * n);
                var g = new PlanEdge[m];
                var meters = new double[m];
                for (int e = 0; e < m; e++)
                {
                    g[e] = E(rng.Next(n), rng.Next(n), (byte)rng.Next(4), (SlopeBand)rng.Next(5), rng.Next(3) == 0 ? -1 : rng.Next(50));
                    meters[e] = 10 * rng.Next(1, 5);   // coarse lengths: many ties
                }
                var target = new bool[n];
                for (int v = 0; v < n; v++)
                    target[v] = rng.Next(5) == 0;
                for (int src = 0; src < n; src++)
                {
                    cases++;
                    int a = RunsFirstRealizer.EasiestDescent(n, g, meters, src, target, 3, out int ra);
                    int b = ReferenceDescent(n, g, meters, src, target, 3, out int rb);
                    if ((a != b || ra != rb) && bad++ == 0)
                        Check(false, w, $"graph {t} src {src}: heap band {a} run {ra}, reference band {b} run {rb}");
                }
            }
            Check(bad == 0, w, $"{bad} of {cases} descents differ from the reference");
        }

        /// <summary>The linear-scan Dijkstra of the first version (v2 task A6), kept verbatim as the reference for the heap version.</summary>
        private static int ReferenceDescent(int nodes, PlanEdge[] g, double[] meters, int src, bool[] target, int srcPadLimit, out int firstRun)
        {
            firstRun = -1;
            var band = new int[nodes];
            var len = new double[nodes];
            var pred = new int[nodes];
            var done = new bool[nodes];
            for (int v = 0; v < nodes; v++)
            {
                band[v] = int.MaxValue;
                len[v] = double.PositiveInfinity;
                pred[v] = -1;
            }
            band[src] = 0;
            len[src] = 0.0;
            while (true)
            {
                int u = -1;
                for (int v = 0; v < nodes; v++)
                    if (!done[v] && band[v] != int.MaxValue && (u < 0 || band[v] < band[u] || (band[v] == band[u] && len[v] < len[u])))
                        u = v;
                if (u < 0)
                    return -1;
                done[u] = true;
                if (u != src && target[u])
                {
                    for (int v = u; pred[v] >= 0; v = g[pred[v]].From)
                        if (g[pred[v]].Run >= 0)
                            firstRun = g[pred[v]].Run;
                    return band[u];
                }
                for (int e = 0; e < g.Length; e++)
                {
                    if (g[e].From != u || g[e].Kind == RunsFirstRealizer.EdgeLift)
                        continue;
                    int x = g[e].To;
                    if (done[x] || (u == src && x < srcPadLimit && g[e].Kind == RunsFirstRealizer.EdgeLink))
                        continue;
                    int b = Math.Max(band[u], (int)g[e].Band);
                    double d = len[u] + meters[e];
                    if (b < band[x] || (b == band[x] && d < len[x]))
                    {
                        band[x] = b;
                        len[x] = d;
                        pred[x] = e;
                    }
                }
            }
        }

        // ---------------------------------------------------------------- hand fixtures

        private sealed class Hand
        {
            public RunsFirstResult Plan = new RunsFirstResult();
            public float2[] Region;
            public HeightFn F;
            public List<AutoObstacle> Obstacles;
            public AutoPlanResult Res;
            public RealizeReport Rep;
            public AutoPlanInput Input;

            public float3 At(float x, float z) => new float3(x, (float)F(x, z), z);

            public Hand Run(List<float2> pts, SlopeBand band, bool graded = false, int lift = 0, string kind = "line")
            {
                Plan.Runs.Add(new RunsFirstResult.Run { Pts = pts, Band = band, Kind = kind, Graded = graded, Groomed = true, Lift = lift });
                return this;
            }

            public Hand Lift(float bx, float bz, float tx, float tz, string type = "detachable", bool isBase = true)
            {
                Plan.Lifts.Add(new RunsFirstResult.Lift { Id = Plan.Lifts.Count + 1, Type = type, Kind = "main", Bottom = At(bx, bz), Top = At(tx, tz), Base = isBase });
                return this;
            }

            public Hand Realize()
            {
                Input = new AutoPlanInput
                {
                    Snapshot = Terrain.Snapshot(F, Region), Region = Region, Obstacles = Obstacles, Knobs = AutoPlanKnobs.Default(1u), Mode = AutoPlanMode.Resort,
                };
                Rep = new RealizeReport();
                Res = RunsFirstRealizer.Realize(Plan, Input, Rep);
                return this;
            }

            public List<AutoRun> Of(int source) => Res.Runs.Where(r => r.Source == source).OrderBy(r => r.Segment).ToList();
            public List<AutoRun> Tracks => Res.Runs.Where(r => r.Kind == RunKind.CatTrack).ToList();
        }

        private static List<float2> L(params float[] xz)
        {
            var l = new List<float2>();
            for (int i = 0; i + 1 < xz.Length; i += 2)
                l.Add(new float2(xz[i], xz[i + 1]));
            return l;
        }

        /// <summary>A straight line from (x0, z0) to (x1, z1) every 10 m.</summary>
        private static List<float2> Line(float x0, float z0, float x1, float z1)
        {
            var a = new float2(x0, z0);
            var b = new float2(x1, z1);
            int n = Math.Max(1, (int)Math.Ceiling(math.distance(a, b) / 10f));
            var l = new List<float2>();
            for (int i = 0; i <= n; i++)
                l.Add(math.lerp(a, b, (float)i / n));
            return l;
        }

        private static List<float2> Bent(float x0, float z0, float x1, float z1, float x2, float z2)
        {
            List<float2> l = Line(x0, z0, x1, z1);
            l.AddRange(Line(x1, z1, x2, z2).Skip(1));
            return l;
        }

        private static bool Linked(AutoRun a, AutoRun b) => a != null && b != null && LiftServedRule.OutlinesLinked(a.Ring, b.Ring);

        private static string Areas(Hand h) => string.Join(", ", h.Res.Runs.Select(r => $"src {r.Source}/{r.Segment} {r.Kind} {r.LengthM:F0} m")) +
            " | " + string.Join("; ", h.Rep.Lines.Where(l => !l.StartsWith("lift ")).Take(4));

        private static Hand NewHand(float grade = 0.2f, float half = 600f) =>
            new Hand { F = Terrain.Plane(grade), Region = Terrain.Square(0f, 0f, half) };

        private static void RealizerHandFixtures()
        {
            // 1. Two parallel runs 45 m apart (a blue's 35 m target would overlap): both kept, narrowed, the gap kept
            {
                const string w = "Realizer hand: two parallel runs";
                Hand h = NewHand().Run(Line(-22f, -400f, -22f, 300f), SlopeBand.Blue).Run(Line(23f, -400f, 23f, 300f), SlopeBand.Blue).Realize();
                RealizerInvariants(w, h.Res, h.Input, h.Rep);
                Check(h.Of(0).Count == 1 && h.Of(1).Count == 1, w, "want one area each: " + Areas(h));
                if (h.Of(0).Count == 1 && h.Of(1).Count == 1)
                    Check(PlanGeometry.RingDistance(h.Of(0)[0].Ring, h.Of(1)[0].Ring, 20f, out _, out _, out _, out _) >= RunsFirstRealizer.ExactGapShare * PlanGeometry.MergeGapM,
                        w, "the pair is closer than the exact gap");
            }
            // 2. A run ends 39 m short of a double black it merges into: the end is extended and the pair is a game link
            {
                const string w = "Realizer hand: merge into a double black 39 m away";
                Hand h = NewHand(0.45f).Run(Line(0f, -500f, 0f, 400f), SlopeBand.DoubleBlack).Run(Line(-250f, -400f, -39f, 0f), SlopeBand.Black).Realize();
                RealizerInvariants(w, h.Res, h.Input, h.Rep);
                List<AutoRun> a = h.Of(1), b = h.Of(0);
                Check(a.Count == 1 && b.Count == 1 && Linked(a[0], b[0]), w, $"want the merge linked ({h.Rep.Extensions} extension(s)): " + Areas(h));
            }
            // 3. A branch listed before its parent: the parent is realized first and stays whole, the branch links to it
            {
                const string w = "Realizer hand: branch before its parent";
                Hand h = NewHand().Run(Line(0f, 0f, -200f, 450f), SlopeBand.Blue).Run(Line(0f, -450f, 0f, 450f), SlopeBand.Blue).Realize();
                RealizerInvariants(w, h.Res, h.Input, h.Rep);
                List<AutoRun> parent = h.Of(1), branch = h.Of(0);
                Check(parent.Count == 1 && parent[0].LengthM > 850f, w, "the parent is not whole: " + Areas(h));
                Check(branch.Count == 1 && parent.Count == 1 && Linked(branch[0], parent[0]), w, "the branch does not link to its parent: " + Areas(h));
            }
            // 4. A graded diagonal across two runs: cut at each run into pieces, each piece links to the runs it touches
            {
                const string w = "Realizer hand: diagonal across two runs";
                Hand h = NewHand(0.45f).Run(Line(-150f, -500f, -150f, 500f), SlopeBand.Black).Run(Line(150f, -500f, 150f, 500f), SlopeBand.Black)
                    .Run(Line(-400f, -450f, 400f, 450f), SlopeBand.Blue, graded: true, kind: "diagonal").Realize();
                RealizerInvariants(w, h.Res, h.Input, h.Rep);
                List<AutoRun> d = h.Of(2);
                Check(d.Count == 3, w, "want three diagonal pieces: " + Areas(h));
                if (d.Count == 3 && h.Of(0).Count == 1 && h.Of(1).Count == 1)
                    Check(Linked(d[0], h.Of(0)[0]) && Linked(d[1], h.Of(0)[0]) && Linked(d[1], h.Of(1)[0]) && Linked(d[2], h.Of(1)[0]), w, "a piece does not link to a run it touches");
            }
            // 5. Two runs from one lift top: both kept, linked to each other, both served
            {
                const string w = "Realizer hand: two runs from one lift top";
                Hand h = NewHand().Lift(0f, 450f, 0f, -400f).Run(Bent(0f, -380f, -150f, 0f, -40f, 430f), SlopeBand.Blue, lift: 1).Run(Bent(0f, -380f, 150f, 0f, 40f, 430f), SlopeBand.Blue, lift: 1).Realize();
                RealizerInvariants(w, h.Res, h.Input, h.Rep);
                List<AutoRun> a = h.Of(0), b = h.Of(1);
                Check(a.Count == 1 && b.Count == 1 && Linked(a[0], b[0]), w, "want two linked areas: " + Areas(h));
                Check(h.Res.Runs.All(r => !r.Unserved), w, "an area is unserved");
                Check(h.Res.Resort.Lifts.Length == 1 && h.Res.Resort.Lifts[0].Home != HomeVerdict.Download, w, "the lift has no way home");
            }
            // 6. A tree: a trunk from a lift top, three forks, a 7 m piece inside the trunk. Four chains, every fork links to the trunk
            {
                const string w = "Realizer hand: tree with a trunk and three forks";
                Hand h = NewHand(0.1f).Lift(-400f, 300f, -400f, -400f);
                h.Plan.Connectors.Add(Line(-400f, -380f, -100f, -380f));
                h.Plan.Connectors.Add(Line(-100f, -380f, -93f, -380f));   // the 7 m piece
                h.Plan.Connectors.Add(Line(-93f, -380f, 300f, -380f));
                h.Plan.Connectors.Add(Line(-250f, -380f, -250f, 100f));
                h.Plan.Connectors.Add(Line(-50f, -380f, -50f, 100f));
                h.Plan.Connectors.Add(Line(150f, -380f, 150f, 100f));
                h.Realize();
                RealizerInvariants(w, h.Res, h.Input, h.Rep);
                List<AutoRun> t = h.Tracks;
                Check(h.Rep.TrackChains == 4 && t.Count == 4, w, $"want 4 chains and 4 track areas, have {h.Rep.TrackChains} and {t.Count}: " + Areas(h));
                AutoRun trunk = t.OrderByDescending(r => r.LengthM).FirstOrDefault();
                Check(trunk != null && trunk.LengthM > 650f, w, "the trunk is not whole");
                Check(t.Where(r => r != trunk).All(r => Linked(r, trunk)), w, "a fork does not link to the trunk");
            }
            // 7. A hairpin track: one chain that doubles back. Kept, as one area or two linked halves (split at the bend)
            {
                const string w = "Realizer hand: hairpin track";
                Hand h = NewHand(0.1f);
                var pts = Line(-300f, -200f, 100f, -150f);
                pts.AddRange(Line(105f, -140f, -300f, -100f).Skip(1));
                h.Plan.Connectors.Add(pts);
                h.Realize();
                RealizerInvariants(w, h.Res, h.Input, h.Rep);
                List<AutoRun> t = h.Tracks;
                Check(t.Count >= 1 && t.Count <= 2 && (t.Count == 1 || Linked(t[0], t[1])), w, "want one area or two linked halves: " + Areas(h));
                Check(h.Rep.TrackKmKept > 0.6 * h.Rep.TrackKmPlanned, w, $"kept {h.Rep.TrackKmKept:F2} of {h.Rep.TrackKmPlanned:F2} km");
            }
            // 8. Two crossing tracks: the second butts the first (two pieces), both pieces link to it
            {
                const string w = "Realizer hand: two crossing tracks";
                Hand h = NewHand(0.1f);
                h.Plan.Connectors.Add(Line(-400f, 0f, 400f, 0f));
                h.Plan.Connectors.Add(Line(0f, -400f, 0f, 400f));
                h.Realize();
                RealizerInvariants(w, h.Res, h.Input, h.Rep);
                List<AutoRun> t = h.Tracks;
                Check(t.Count == 3, w, "want three track areas: " + Areas(h));
                AutoRun first = t.OrderByDescending(r => r.LengthM).FirstOrDefault();
                Check(t.Where(r => r != first).All(r => Linked(r, first)), w, "a piece of the second track does not link to the first");
            }
            // R2-r (DECISIONS 637): two chains in one corridor are one track. A is a 700 m traverse from the lift top.
            // 8a. B runs 8 m beside A over its whole length: B merges into A (no area, no drop), and a piste that starts off B's end links
            //     to A. B's own planner relations are game links on A.
            {
                const string w = "Realizer hand: two chains in one corridor over their whole length";
                Hand h = NewHand(0.1f).Lift(-400f, 300f, -400f, -400f);
                h.Plan.Connectors.Add(Line(-400f, -380f, 300f, -380f));
                h.Plan.Connectors.Add(Line(-300f, -372f, 200f, -372f));
                h.Run(Line(210f, -352f, 210f, 400f), SlopeBand.Blue, lift: 1);
                h.Realize();
                RealizerInvariants(w, h.Res, h.Input, h.Rep);
                List<AutoRun> t = h.Tracks;
                Check(t.Count == 1 && t[0].Source == -1, w, "want one track area (A): " + Areas(h));
                Check(h.Rep.TrackMerged == 1 && h.Rep.TrackDrops.Sum() == 0, w, $"want B merged and no drop, have {h.Rep.TrackMerged} merged, {h.Rep.TrackDrops.Sum()} dropped");
                Check(h.Rep.MergedRelations > 0 && h.Rep.MergedLinks == h.Rep.MergedRelations, w, $"B's relations linked {h.Rep.MergedLinks}/{h.Rep.MergedRelations}");
                Check(h.Rep.TrackKmOwn + h.Rep.TrackKmShared >= 0.95 * h.Rep.TrackKmPlanned, w,
                    $"built {h.Rep.TrackKmOwn:F2} + carried {h.Rep.TrackKmShared:F2} of {h.Rep.TrackKmPlanned:F2} km");
                Check(t.Count == 1 && h.Of(0).Count == 1 && Linked(h.Of(0)[0], t[0]), w, "the piste that starts off B does not link to A");
            }
            // 8b. B runs beside A for 300 m, then turns downhill for 670 m: B keeps its own part, butted onto A at the split and linked to it
            {
                const string w = "Realizer hand: two chains in one corridor over a part";
                Hand h = NewHand(0.1f).Lift(-400f, 300f, -400f, -400f);
                h.Plan.Connectors.Add(Line(-400f, -380f, 300f, -380f));
                h.Plan.Connectors.Add(Bent(-300f, -372f, 0f, -372f, 0f, 300f));
                h.Realize();
                RealizerInvariants(w, h.Res, h.Input, h.Rep);
                List<AutoRun> a = h.Of(-1), b = h.Of(-2);
                Check(a.Count == 1 && b.Count == 1 && h.Tracks.Count == 2, w, "want A and B's own part, one area each: " + Areas(h));
                if (a.Count == 1 && b.Count == 1)
                {
                    Check(b[0].LengthM > 600f && b[0].LengthM < 700f, w, $"B's own part is {b[0].LengthM:F0} m (want its 670 m downhill leg)");
                    Check(Linked(a[0], b[0]), w, "B does not link to A at the split");
                }
                Check(h.Rep.TrackMerged == 0 && h.Rep.TrackDrops.Sum() == 0 && h.Rep.TrackKmShared > 0.25, w,
                    $"merged {h.Rep.TrackMerged}, dropped {h.Rep.TrackDrops.Sum()}, carried {h.Rep.TrackKmShared:F2} km (want about 0.3)");
            }
            // 8c. Three chains in one corridor (B 8 m on one side of A, C 8 m on the other): one track, B and C merged into it
            {
                const string w = "Realizer hand: three chains in one corridor";
                Hand h = NewHand(0.1f).Lift(-400f, 300f, -400f, -400f);
                h.Plan.Connectors.Add(Line(-400f, -380f, 300f, -380f));
                h.Plan.Connectors.Add(Line(-300f, -372f, 200f, -372f));
                h.Plan.Connectors.Add(Line(-250f, -388f, 250f, -388f));
                h.Realize();
                RealizerInvariants(w, h.Res, h.Input, h.Rep);
                Check(h.Tracks.Count == 1 && h.Of(-1).Count == 1, w, "want one track area (A): " + Areas(h));
                Check(h.Rep.TrackMerged == 2 && h.Rep.TrackDrops.Sum() == 0, w, $"want B and C merged, have {h.Rep.TrackMerged} merged, {h.Rep.TrackDrops.Sum()} dropped");
                Check(h.Rep.MergedLinks == h.Rep.MergedRelations, w, $"merged relations linked {h.Rep.MergedLinks}/{h.Rep.MergedRelations}");
            }
            // 8d. B leaves A's corridor for a 70 m detour and rejoins it: the detour is B's own part, butted onto A at both ends
            {
                const string w = "Realizer hand: a chain that leaves the corridor and rejoins it";
                Hand h = NewHand(0.1f).Lift(-400f, 300f, -400f, -400f);
                h.Plan.Connectors.Add(Line(-400f, -380f, 300f, -380f));
                List<float2> b = Line(-300f, -372f, -100f, -372f);
                b.AddRange(Line(-100f, -372f, -50f, -300f).Skip(1));
                b.AddRange(Line(-50f, -300f, 50f, -300f).Skip(1));
                b.AddRange(Line(50f, -300f, 100f, -372f).Skip(1));
                b.AddRange(Line(100f, -372f, 200f, -372f).Skip(1));
                h.Plan.Connectors.Add(b);
                h.Realize();
                RealizerInvariants(w, h.Res, h.Input, h.Rep);
                List<AutoRun> a = h.Of(-1), d = h.Of(-2);
                Check(a.Count == 1 && d.Count == 1, w, "want A whole and B's detour as one area: " + Areas(h));
                if (a.Count == 1 && d.Count == 1)
                {
                    Check(d[0].LengthM > 180f, w, $"the detour is {d[0].LengthM:F0} m");
                    float2 s = d[0].Centerline[0], e = d[0].Centerline[d[0].Centerline.Length - 1];
                    Check(Linked(a[0], d[0]) && s.x < -60f && e.x > 60f, w, $"the detour is not butted onto A at both ends (it runs from x {s.x:F0} to x {e.x:F0})");
                }
                Check(h.Rep.UnlinkedEnds == 0 && h.Rep.TrackMerged == 0, w, $"{h.Rep.UnlinkedEnds} unlinked end(s), {h.Rep.TrackMerged} merged");
            }
            // 8e. R2-x: run 0 starts 38 m below a track (a planner contact, outlines about 30 m apart), so its start is extended after the
            //     track pass. Runs 1 and 2 end onto run 0 at the base, 2 m from its centerline: they come later and keep only the merge gap.
            //     The extension re-checks the ring of run 0. Each later run is tested by the rule that accepted it, so the start links.
            {
                const string w = "Realizer hand: start extension with two runs that converge at the base";
                Hand h = NewHand();
                h.Plan.Connectors.Add(Line(-300f, -400f, 300f, -400f));
                h.Run(Line(0f, -362f, 0f, 450f), SlopeBand.Blue);
                h.Run(Bent(-300f, -100f, -150f, 300f, -2f, 440f), SlopeBand.Blue);
                h.Run(Bent(300f, -100f, 150f, 300f, 2f, 440f), SlopeBand.Blue);
                h.Realize();
                RealizerInvariants(w, h.Res, h.Input, h.Rep);
                List<AutoRun> r0 = h.Of(0), t = h.Tracks;
                Check(r0.Count == 1 && t.Count == 1 && Linked(r0[0], t[0]), w, "the start of run 0 does not link to the track: " + Areas(h));
                Check(h.Of(1).Count >= 1 && h.Of(2).Count >= 1 && r0.Count == 1 && Linked(h.Of(1).Last(), r0[0]) && Linked(h.Of(2).Last(), r0[0]), w,
                    "a converging run does not link to run 0: " + Areas(h));
                Check(h.Rep.Lines.Any(l => l.StartsWith("piste 0 (line) segment 0: start extended")), w,
                    "the start of run 0 is not extended (the case is not reproduced): " + string.Join("; ", h.Rep.Lines.Where(l => l.StartsWith("piste 0 "))));
            }
            // 9. A run through an obstacle (an unowned tile across its middle): two pieces, both clear of it
            {
                const string w = "Realizer hand: run through an obstacle";
                Hand h = NewHand();
                h.Obstacles = new List<AutoObstacle> { new AutoObstacle { Kind = AutoObstacleKind.Unowned, Ring = Terrain.Rect(-100f, -40f, 100f, 40f) } };
                h.Run(Line(0f, -500f, 0f, 500f), SlopeBand.Blue).Realize();
                RealizerInvariants(w, h.Res, h.Input, h.Rep);
                Check(h.Of(0).Count == 2, w, "want two pieces: " + Areas(h));
            }
            // 10. A run along the region edge (8 m inside): kept, narrowed, inside the region
            {
                const string w = "Realizer hand: run along the region edge";
                Hand h = NewHand().Run(Line(-592f, -500f, -592f, 500f), SlopeBand.Blue).Realize();
                RealizerInvariants(w, h.Res, h.Input, h.Rep);
                Check(h.Of(0).Count >= 1, w, "the run is dropped: " + Areas(h) + " " + string.Join("; ", h.Rep.Lines.Take(3)));
            }
            // 11. An existing piste and an existing cat track in the region. A new track crosses the piste (one piece) and butts the track
            //     (two pieces). A new piste keeps its gap from the existing piste and crosses the existing track.
            {
                const string w = "Realizer hand: existing piste and cat track";
                Hand h = NewHand();
                h.Obstacles = new List<AutoObstacle>
                {
                    new AutoObstacle { Kind = AutoObstacleKind.ExistingRun, Ring = Terrain.Rect(-320f, -500f, -280f, 500f) },
                    new AutoObstacle { Kind = AutoObstacleKind.CatTrack, Ring = Terrain.Rect(200f, -500f, 210f, 500f) },
                };
                h.Run(Line(-250f, -500f, -250f, 500f), SlopeBand.Blue);                        // 30 m from the existing piste's edge
                h.Run(Line(50f, -100f, 400f, 450f), SlopeBand.Blue);                           // crosses the existing track
                h.Plan.Connectors.Add(Line(-500f, -300f, 500f, -300f));                       // crosses the piste and the existing track
                h.Realize();
                RealizerInvariants(w, h.Res, h.Input, h.Rep);
                Check(h.Of(0).Count == 1, w, "the piste beside the existing piste is not one area: " + Areas(h));
                if (h.Of(0).Count == 1)
                    Check(PlanGeometry.RingDistance(h.Of(0)[0].Ring, h.Input.Obstacles[0].Ring, 30f, out _, out _, out _, out _) >=
                        PlanGeometry.ClearanceOf(AutoObstacleKind.ExistingRun, h.Input.Knobs.Density) - 0.01f, w, "the new piste is inside the existing piste's clearance");
                Check(h.Of(1).Count == 1 && h.Of(1)[0].CrossesCatTrack, w, "the piste across the existing track is not one area flagged CrossesCatTrack: " + Areas(h));
                List<AutoRun> t = h.Tracks;
                Check(t.Count == 2, w, "want the new track in two pieces (butted at the existing track, across the piste): " + Areas(h));
                Check(t.All(r => PlanGeometry.RingDistance(r.Ring, h.Input.Obstacles[1].Ring, 2f, out _, out _, out _, out _) >= 1.6f), w, "a track piece overlaps the existing track");
            }
            RealizerOverLimitLifts();   // task R2-l (the block at the end of this file)
        }

        // ================================================================ task R2-l: PlanLift.OverLimitTowers / BuildsFlagged (block start)
        // The hub tells "the queue builds it, one tower over its sheave-train limit" (BuildsFlagged) from "needs fitting by hand" (HandFit and
        // not BuildsFlagged). Lifts over a convex knee (a gentle slope, then a steep face) give towers over their cap. Pinned:
        // - a sound lift has no over-limit tower and is not flagged;
        // - a flagged lift is HandFit (HandFit keeps its meaning: not Sound) with exactly one over-limit tower;
        // - the tram has no sheave train: never an over-limit tower, never flagged;
        // - the sweep has at least one flagged lift and one that waits with two or more (the fields are live).
        private static void RealizerOverLimitLifts()
        {
            const string w = "Realizer R2-l: over-limit lifts";
            int lifts = 0, sound = 0, flagged = 0, waitsTrains = 0, otherHandFit = 0;
            var shapes = new List<(string name, double[] seg)>();
            // A profile from the bottom (u = 0) up the line: pairs of (length m, grade). 900 m in all.
            foreach (double face in new[] { 0.5, 0.65, 0.8 })
                foreach (double crest in new[] { 12.0, 20.0, 35.0 })
                    foreach (double low in new[] { 0.12, 0.25 })
                        shapes.Add(($"lip face {face:F2} crest {crest:F0} low {low:F2}", new[] { 640.0 - crest, low, 260.0, face, crest, 0.0 }));
            foreach (double face in new[] { 0.65, 0.8 })
                shapes.Add(($"two lips face {face:F2}", new[] { 150.0, 0.15, 150.0, face, 20.0, 0.0, 300.0, 0.15, 160.0, face, 20.0, 0.0, 100.0, 0.0 }));
            var counts = new Dictionary<string, int>();
            foreach (string type in new[] { "fixedGrip", "detachable", "gondola", "tram" })
                foreach (var (name, seg) in shapes)
                {
                    double[] sg = seg;
                    HeightFn f = (x, z) =>
                    {
                        double u = 450.0 - z, y = 1000.0;
                        for (int i = 0; i + 1 < sg.Length && u > 0.0; i += 2)
                        {
                            double d = Math.Min(u, sg[i]);
                            y += d * sg[i + 1];
                            u -= d;
                        }
                        return y;
                    };
                    var h = new Hand { F = f, Region = Terrain.Square(0f, 0f, 600f) };
                    h.Lift(0f, 450f, 0f, -450f, type).Realize();
                    foreach (PlanLift L in h.Res.Resort.Lifts)
                    {
                        lifts++;
                        string at = $"{type} {name}: towers {L.Towers}, HandFit {L.HandFit}, over limit {L.OverLimitTowers}, flagged {L.BuildsFlagged}";
                        Check(L.OverLimitTowers >= 0, w, at);
                        Check(L.HandFit || (L.OverLimitTowers == 0 && !L.BuildsFlagged), w, "a sound lift carries an over-limit tower: " + at);
                        Check(!L.BuildsFlagged || (L.HandFit && L.OverLimitTowers == 1), w, "a flagged lift is not HandFit with one over-limit tower: " + at);
                        Check(type != "tram" || (L.OverLimitTowers == 0 && !L.BuildsFlagged), w, "the tram has an over-limit tower: " + at);
                        string bucket = !L.HandFit ? "sound" : L.BuildsFlagged ? "flagged" : L.OverLimitTowers >= 2 ? "waits 2+" : "other";
                        if (!L.HandFit) sound++;
                        else if (L.BuildsFlagged) flagged++;
                        else if (L.OverLimitTowers >= 2) waitsTrains++;
                        else otherHandFit++;
                        string key = type + " " + bucket + " over " + L.OverLimitTowers;
                        counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
                    }
                }
            Check(flagged > 0, w, $"no lift in the sweep is flagged ({lifts} lifts): the field is dead");
            Check(waitsTrains > 0, w, $"no lift in the sweep waits with two or more over-limit towers ({lifts} lifts)");
            Console.WriteLine($"REPORT rf realizer R2-l over-limit sweep: {lifts} lifts, {sound} sound, {flagged} flagged (the queue builds them), " +
                              $"{waitsTrains} wait with 2+ over the limit, {otherHandFit} other hand fits; " +
                              string.Join(", ", counts.OrderBy(kv => kv.Key).Select(kv => kv.Key + " x" + kv.Value)));
        }
        // ================================================================ task R2-l (block end)
    }
}
