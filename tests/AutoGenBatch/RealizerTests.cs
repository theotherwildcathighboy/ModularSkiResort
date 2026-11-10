// Runs-first realizer gates (REALIZER-SPEC.md section 10), in the default host run:
// - the synthetic regions (V2Fixtures.Synthetic: Alpine1, Benched1, Steep1 full): the planner plans, the realizer realizes, and every
//   ring passes the invariants (3 nodes, 2 m node spacing, GradeRibbon.Clear, inside the region, the band = a fresh rate), no overlap
//   the game refuses, the relation gate, the same hash on a second run;
// - the easiest descent copy against the v2 original on the Flow hand graph;
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
                double kmP = rep.PisteKmPlanned + rep.GradedKmPlanned, kmK = rep.PisteKmKept + rep.GradedKmKept;
                Console.WriteLine($"REPORT realizer {id}: {res.Runs.Count} areas, piste km {kmK:F2}/{kmP:F2}, track km {rep.TrackKmKept:F2}/{rep.TrackKmPlanned:F2}, " +
                    $"relations {rep.RelationLinks}/{rep.Relations}, unserved {rep.UnservedAreas}, dead ends {rep.DeadEnds}, {rep.Ms:F0} ms, hash {a:x16}" +
                    string.Concat(rep.Lines.Where(l => l.StartsWith("relation not linked") || l.Contains("cannot reach") || l.Contains(": spur ") || l.Contains(" merged into ")).Select(l => "\n  " + l)));
                string only = Environment.GetEnvironmentVariable("RZ_LINES");   // debugging: every report line that contains this text
                if (!string.IsNullOrEmpty(only))
                    foreach (string l in rep.Lines.Where(l => l.Contains(only)))
                        Console.WriteLine("  RZ " + l);
            }
        }

        // ---------------------------------------------------------------- the descent copy

        private static void RealizerDescent()
        {
            const string w = "Realizer descent";
            var g = new[]
            {
                E(1, 2, 3, SlopeBand.Flat), E(2, 3, 0, SlopeBand.Black, 0), E(3, 0, 3, SlopeBand.Flat), E(1, 4, 3, SlopeBand.Flat), E(4, 5, 0, SlopeBand.Blue, 1),
                E(5, 7, 3, SlopeBand.Green, 2), E(6, 7, 0, SlopeBand.Green, 2), E(7, 0, 3, SlopeBand.Flat), E(1, 9, 3, SlopeBand.Flat), E(9, 10, 0, SlopeBand.Blue, 3),
                E(10, 0, 3, SlopeBand.Flat), E(1, 0, 1, SlopeBand.Flat), E(0, 8, 3, SlopeBand.Flat), E(8, 1, 1, SlopeBand.Flat),
            };
            double[] m = { 10, 300, 10, 10, 600, 200, 400, 10, 10, 1200, 10, 1, 10, 900 };
            var target = new bool[12];
            target[0] = true;
            int band = RunsFirstRealizer.EasiestDescent(12, g, m, 1, target, 1, out int run);
            int want = SkiRunAutoPlanner.EasiestDescent(12, g, m, 1, target, 1, out int wantRun);
            Check(band == (int)SlopeBand.Blue && run == 1 && band == want && run == wantRun, w, $"band {band} run {run}, v2 {want} run {wantRun} (want Blue via run 1)");
            var lone = new[] { E(1, 0, 1, SlopeBand.Flat), E(1, 11, 0, SlopeBand.Green, 0) };
            band = RunsFirstRealizer.EasiestDescent(12, lone, new double[] { 1, 1 }, 1, target, 1, out run);
            Check(band == -1 && run == -1, w, $"no way home: band {band}, run {run}");
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
        }
    }
}
