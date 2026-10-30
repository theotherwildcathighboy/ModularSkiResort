// Flow suite (A6, A-R round 1): a hand graph with a known minimax descent (EasiestDescent, also against a reference linear-scan Dijkstra
// on a random graph), then per synthetic fixture the plan graph, ways home and the rest of FinishResort: H-1 to H-3 and F-10 HARD, H-4 to
// H-6 REPORT (the fixtures pass no roads, so a run ending on a base pad is an honest dead end: class 4), the graph shape, RunPod / PodRuns / pod clusters, dead ends recounted from the graph (no downhill path
// to a way out: DECISIONS 451 c), real-band verdicts (451 b: Download only without a path), the warnings, the site inputs (pods meeting
// their Wanted, AR-13), the Home knob (Wanted changes, Home does not), a cat track to a base pad with a road as a green way home, the
// fingerprint fold.

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class Program
    {
        private static void FlowTests()
        {
            FlowHandGraph();
            FlowHeapEquality();
            foreach (V2Case c in V2Fixtures.Synthetic())
                if (c.Id == "Alpine1 lower" || c.Id == "Alpine1 full" || c.Id == "Benched1 full" || c.Id == "Steep1 full")
                    FlowCase(c);
            FlowCatTrack();
        }

        private static bool FlowCheck(bool ok, string where, string msg)
        {
            Check(ok, where, msg);
            return ok;
        }

        private static PlanEdge E(int from, int to, byte kind, SlopeBand band, int run = -1) =>
            new PlanEdge { From = from, To = to, Kind = kind, Band = band, Run = run };

        /// <summary>Nodes: 0 base pad, 1 lift top, 2/3 run A (black, short), 4/5 run B (blue), 6/7 run C (green, joined from B's bottom),
        /// 8 lift bottom, 9/10 run D (blue, longer than B), 11 an island. The easiest descent from 1 is B then C (hardest band Blue), not A
        /// (Black, shorter) and not D (Blue, longer); the lift edge 1 -> 0 is never ridden down.</summary>
        private static void FlowHandGraph()
        {
            const string w = "Flow hand graph";
            var g = new List<PlanEdge>
            {
                E(1, 2, SkiRunAutoPlanner.EdgeLink, SlopeBand.Flat), E(2, 3, SkiRunAutoPlanner.EdgeRun, SlopeBand.Black, 0), E(3, 0, SkiRunAutoPlanner.EdgeLink, SlopeBand.Flat),
                E(1, 4, SkiRunAutoPlanner.EdgeLink, SlopeBand.Flat), E(4, 5, SkiRunAutoPlanner.EdgeRun, SlopeBand.Blue, 1), E(5, 7, SkiRunAutoPlanner.EdgeLink, SlopeBand.Green, 2),
                E(6, 7, SkiRunAutoPlanner.EdgeRun, SlopeBand.Green, 2), E(7, 0, SkiRunAutoPlanner.EdgeLink, SlopeBand.Flat),
                E(1, 9, SkiRunAutoPlanner.EdgeLink, SlopeBand.Flat), E(9, 10, SkiRunAutoPlanner.EdgeRun, SlopeBand.Blue, 3), E(10, 0, SkiRunAutoPlanner.EdgeLink, SlopeBand.Flat),
                E(1, 0, SkiRunAutoPlanner.EdgeLift, SlopeBand.Flat), E(0, 8, SkiRunAutoPlanner.EdgeLink, SlopeBand.Flat), E(8, 1, SkiRunAutoPlanner.EdgeLift, SlopeBand.Flat),
            };
            double[] m = { 10, 300, 10, 10, 600, 200, 400, 10, 10, 1200, 10, 1, 10, 900 };
            var target = new bool[12];
            target[0] = true;
            int band = SkiRunAutoPlanner.EasiestDescent(12, g.ToArray(), m, 1, target, 1, out int run);
            Check(band == (int)SlopeBand.Blue && run == 1, w, $"easiest descent band {band}, first run {run} (want Blue via run 1)");

            // the black run made green: now A is easiest (Green, shortest)
            g[1] = E(2, 3, SkiRunAutoPlanner.EdgeRun, SlopeBand.Green, 0);
            band = SkiRunAutoPlanner.EasiestDescent(12, g.ToArray(), m, 1, target, 1, out run);
            Check(band == (int)SlopeBand.Green && run == 0, w, $"after the regrade: band {band}, first run {run} (want Green via run 0)");

            // ties by length: B blocked, D is the only blue
            g[1] = E(2, 3, SkiRunAutoPlanner.EdgeRun, SlopeBand.Black, 0);
            g[5] = E(5, 11, SkiRunAutoPlanner.EdgeLink, SlopeBand.Green, 2);
            band = SkiRunAutoPlanner.EasiestDescent(12, g.ToArray(), m, 1, target, 1, out run);
            Check(band == (int)SlopeBand.Blue && run == 3, w, $"B dead-ended: band {band}, first run {run} (want Blue via run 3)");

            // no path at all (only the lift edge reaches the pad)
            var lone = new[] { E(1, 0, SkiRunAutoPlanner.EdgeLift, SlopeBand.Flat), E(1, 11, SkiRunAutoPlanner.EdgeRun, SlopeBand.Green, 0) };
            band = SkiRunAutoPlanner.EasiestDescent(12, lone, new double[] { 1, 1 }, 1, target, 1, out run);
            Check(band == -1 && run == -1, w, $"no way home: band {band}, run {run} (want -1, -1)");

            // a link from the top onto a pad is skipped (getting off on the top pad is no way home); a cat track is not
            var hop = new[] { E(1, 0, SkiRunAutoPlanner.EdgeLink, SlopeBand.Flat) };
            band = SkiRunAutoPlanner.EasiestDescent(12, hop, new double[] { 1 }, 1, target, 1, out run);
            Check(band == -1, w, $"top pad hop counted as a way home (band {band})");
            var cat = new[] { E(1, 0, SkiRunAutoPlanner.EdgeCatTrack, SlopeBand.Green) };
            band = SkiRunAutoPlanner.EasiestDescent(12, cat, new double[] { 1 }, 1, target, 1, out run);
            Check(band == (int)SlopeBand.Green && run == -1, w, $"cat track from the top: band {band} (want Green)");
        }

        /// <summary>AR-7: the heap Dijkstra settles in the linear scan's order: same band and first run as a reference O(V^2) scan (ties by
        /// node index) on random graphs with many equal lengths, every source, two target sets.</summary>
        private static void FlowHeapEquality()
        {
            const string w = "Flow heap Dijkstra";
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
                    int a = SkiRunAutoPlanner.EasiestDescent(n, g, meters, src, target, 3, out int ra);
                    int b = ReferenceDescent(n, g, meters, src, target, 3, out int rb);
                    if ((a != b || ra != rb) && bad++ == 0)
                        Check(false, w, $"graph {t} src {src}: heap band {a} run {ra}, reference band {b} run {rb}");
                }
            }
            Check(bad == 0, w, $"{bad} of {cases} descents differ from the reference");
        }

        /// <summary>The A6 linear-scan Dijkstra, kept verbatim as the reference for the heap version.</summary>
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
                    if (g[e].From != u || g[e].Kind == SkiRunAutoPlanner.EdgeLift)
                        continue;
                    int x = g[e].To;
                    if (done[x] || (u == src && x < srcPadLimit && g[e].Kind == SkiRunAutoPlanner.EdgeLink))
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

        private static AutoPlanResult FlowRun(V2Case c, HomeLevel home, List<AutoObstacle> obstacles = null, float2[] access = null)
        {
            AutoPlanKnobs knobs = AutoPlanKnobs.Default(SkiRunAutoPlanner.SeedOf(c.Region, 0));
            knobs.Home = home;
            var input = new AutoPlanInput { Snapshot = c.Snap(), Region = c.Region, Knobs = knobs, Mode = AutoPlanMode.Resort, Obstacles = obstacles, Access = access };
            return new SkiRunAutoPlanner().Run(input);
        }

        private static void FlowCase(V2Case c)
        {
            string w = "Flow " + c.Id;
            HeightSnapshot snap = c.Snap();
            AutoPlanResult r = FlowRun(c, HomeLevel.Auto);
            ResortPlan p = r.Resort;
            if (!FlowCheck(r.Status == AutoPlanStatus.Ok && p != null, w, "no plan: " + r.Status))
                return;
            int pads = p.Pads.Length, runs = r.Runs.Count, lifts = p.Lifts.Length, nodes = pads + 2 * runs + 2 * lifts;

            // Gates (V2Metrics, the matrix's own functions)
            var sampler = new SnapshotSampler(snap);
            var grid = new SlopeGrid();
            var sres = new SlopeResult();
            Func<AutoRun, SlopeBand> rate = run =>
                SkiRunRater.Rate(run.Nodes, run.Nodes.Length, snap.Origin, ref sampler, snap.TexelM, 0, grid, null, sres, out int _, out float _, out float _) == RateStatus.Rated
                    ? sres.Band : run.Band;
            foreach (Gate g in new[] { V2Metrics.H1Verdicts(p), V2Metrics.H2Honesty(r, p, rate), V2Metrics.H3NoFakeGreen(p), V2Metrics.F10MixHonest(r, p, rate) })
                Check(g.Pass || (lifts == 0 && g.Id != "F-10"), w + " " + g.Id, g.Text);
            foreach (Gate g in new[] { V2Metrics.H4DeadEnds(r, p), V2Metrics.H5Reachability(r, p), V2Metrics.H6GreenShare(p) })
                Console.WriteLine($"REPORT {g.Id} {w}: {g.Text}{(g.Pass ? " [ok]" : " [SOFT]")}");

            // Graph shape: valid nodes, one run edge per run, one lift edge per lift, cat track edges only with cat tracks
            int runEdges = 0, liftEdges = 0, catEdges = 0;
            var outDeg = new int[nodes];
            foreach (PlanEdge e in p.Graph)
            {
                Check(e.From >= 0 && e.From < nodes && e.To >= 0 && e.To < nodes && e.Run >= -1 && e.Run < runs, w, $"edge {e.From}->{e.To} run {e.Run} out of range");
                if (e.Kind == SkiRunAutoPlanner.EdgeRun)
                {
                    runEdges++;
                    Check(e.From == pads + 2 * e.Run && e.To == e.From + 1 && e.Band == r.Runs[e.Run].Band, w, $"run edge {e.From}->{e.To} of run {e.Run}");
                }
                liftEdges += e.Kind == SkiRunAutoPlanner.EdgeLift ? 1 : 0;
                catEdges += e.Kind == SkiRunAutoPlanner.EdgeCatTrack ? 1 : 0;
                outDeg[e.From]++;
            }
            Check(runEdges == runs && liftEdges == lifts && catEdges == 0, w, $"edges: {runEdges} run (want {runs}), {liftEdges} lift (want {lifts}), {catEdges} cat track (want 0)");

            // Dead ends = run bottoms with no downhill path to a way out (no obstacles here); a run bottom without an outgoing edge is one;
            // the warnings follow the counts
            List<int> deadList = V2Metrics.HostDeadEnds(r, p);
            int noEdge = 0;
            for (int i = 0; i < runs; i++)
                noEdge += outDeg[pads + 2 * i + 1] == 0 ? 1 : 0;
            Check(deadList.Count == p.DeadEnds && noEdge <= p.DeadEnds, w, $"DeadEnds {p.DeadEnds}, graph recount {deadList.Count}, bottoms without an edge {noEdge}");
            Check(((r.Warnings & AutoPlanWarning.DeadEnd) != 0) == (p.DeadEnds > 0), w, "DeadEnd warning does not follow DeadEnds");
            foreach (string line in V2Metrics.DeadEndLines(r, p, deadList))
                Console.WriteLine($"REPORT dead end {w}: {line}");
            bool anyDownload = false;
            foreach (PlanLift l in p.Lifts)
            {
                anyDownload |= l.Home == HomeVerdict.Download;
                Check(l.Home != HomeVerdict.None, w, $"lift {l.Id}: no verdict");
                Check(l.HomeRun >= -1 && l.HomeRun < runs && (l.Home == HomeVerdict.Download || l.HomeRun >= 0), w, $"lift {l.Id}: HomeRun {l.HomeRun} with {l.Home}");
            }
            Check(((r.Warnings & AutoPlanWarning.Download) != 0) == anyDownload, w, "Download warning does not follow the verdicts");

            // RunPod / PodRuns / pod clusters
            Check(p.RunPod.Length == runs && p.RunLift.Length == runs, w, "RunPod / RunLift length");
            int inPods = 0, outside = 0;
            for (int i = 0; i < runs; i++)
            {
                Check(p.RunPod[i] >= -1 && p.RunPod[i] < p.Pods.Length && p.RunLift[i] >= -1 && p.RunLift[i] < lifts, w, $"run {i}: pod {p.RunPod[i]}, lift {p.RunLift[i]}");
                outside += p.RunPod[i] < 0 ? 1 : 0;
            }
            for (int k = 0; k < p.Pods.Length; k++)
            {
                PlanPod pod = p.Pods[k];
                inPods += pod.RunCount;
                for (int q = pod.RunFirst; q < pod.RunFirst + pod.RunCount; q++)
                    Check(p.PodRuns[q] >= 0 && p.PodRuns[q] < runs && p.RunPod[p.PodRuns[q]] == k, w, $"PodRuns[{q}] not in pod {k}");
                Check(r.Clusters[k].Pod && r.Clusters[k].Runs == pod.RunCount && r.Clusters[k].Center.Equals(pod.TopCenter), w, $"cluster {k} is not pod {k}");
            }
            Check(inPods == p.PodRuns.Length && inPods + outside == runs, w, $"PodRuns {p.PodRuns.Length}, pods hold {inPods}, outside {outside}, runs {runs}");
            Check(r.Clusters.Count == p.Pods.Length + (outside > 0 ? 1 : 0), w, $"{r.Clusters.Count} clusters for {p.Pods.Length} pods (+1 when runs lie outside)");
            for (int i = 0; i < runs; i++)
                Check(r.Runs[i].Cluster == (p.RunPod[i] >= 0 ? p.RunPod[i] : p.Pods.Length), w, $"run {i}: cluster {r.Runs[i].Cluster}, pod {p.RunPod[i]}");

            // Honest mix, density, site inputs
            float mix = 0f;
            for (int b = 0; b < 5; b++)
                mix += p.MountainMix[b];
            Check(runs == 0 || Math.Abs(mix - 1f) < 1e-4f, w, "MountainMix sums to " + mix);
            Check(p.RunKm > 0f && p.HullKm2 > 0f && Math.Abs(p.RunKmPerKm2 - p.RunKm / p.HullKm2) < 1e-4f, w, $"run km {p.RunKm}, hull {p.HullKm2} km2");
            int met = 0, served = 0;
            foreach (PlanPod pod in p.Pods)
            {
                if (pod.Lift < 0)
                    continue;
                served++;
                PlanLift pl = p.Lifts[pod.Lift];
                met += pl.Home == HomeVerdict.Green || (pl.Home == HomeVerdict.Blue && pl.Wanted == HomeLevel.Blue) ? 1 : 0;
            }
            Check(p.Site.Kind == (byte)QuickRunsRules.SiteTierOf(QuickRunsRules.SitePercent(r.MeanGrade)) && p.Site.Pods == p.Pods.Length &&
                  p.Site.PodsWithGreenHome == met && p.Site.PodsServed == served && p.Site.MeanGrade == r.MeanGrade, w,
                  $"site inputs: pods meeting their Wanted {p.Site.PodsWithGreenHome} (want {met}), served {p.Site.PodsServed} (want {served})");

            // Fingerprint fold
            AutoPlanInput runsIn = new AutoPlanInput { Snapshot = snap, Region = c.Region, Knobs = AutoPlanKnobs.Default(SkiRunAutoPlanner.SeedOf(c.Region, 0)) };
            Check(p.Fingerprint != 0UL && r.Fingerprint != new SkiRunAutoPlanner().Run(runsIn).Fingerprint, w, "the plan fingerprint is not folded in");

            // Home knob: Wanted follows it (Auto = Green when the bottom pad touches the base band, Blue above); the verdict does not
            // (DECISIONS 451 b: the real band, whatever was wanted)
            AutoPlanResult rg = FlowRun(c, HomeLevel.Green), rb = FlowRun(c, HomeLevel.Blue);
            for (int i = 0; i < lifts && i < rg.Resort.Lifts.Length && i < rb.Resort.Lifts.Length; i++)
            {
                PlanLift a = p.Lifts[i], lg = rg.Resort.Lifts[i], lb = rb.Resort.Lifts[i];
                bool baseBottom = a.BottomPad >= 0 && p.Pads[a.BottomPad].TouchesBase;
                Check(a.Wanted == (baseBottom ? HomeLevel.Green : HomeLevel.Blue) && lg.Wanted == HomeLevel.Green && lb.Wanted == HomeLevel.Blue, w, $"lift {i}: Wanted {a.Wanted} / {lg.Wanted} / {lb.Wanted}");
                Check(lg.Home == a.Home && lb.Home == a.Home && lg.HomeRun == a.HomeRun && lb.HomeRun == a.HomeRun, w, $"lift {i}: Home {a.Home} / {lg.Home} / {lb.Home} changes with the knob");
            }

            var homes = new int[6];
            foreach (PlanLift l in p.Lifts)
                homes[(int)l.Home]++;
            Console.WriteLine($"REPORT flow {c.Id}: {lifts} lifts, home G/B/K/DB/Download {homes[1]}/{homes[2]}/{homes[3]}/{homes[4]}/{homes[5]}, dead ends {p.DeadEnds} of {runs}, " +
                              $"{p.Graph.Length} edges, pods meeting their Wanted {p.Site.PodsWithGreenHome} of {p.Site.PodsServed} served, mix G {p.MountainMix[1] * 100:F0} B {p.MountainMix[2] * 100:F0} K {p.MountainMix[3] * 100:F0} DB {p.MountainMix[4] * 100:F0} %, Flow unit {p.StageMaxMs[5]:F2} ms");
        }

        /// <summary>A cat track (obstacle outline) from a lift top down to a base pad with a road at it (an access point at its center,
        /// so the pad is a way out, AR-2) is a green way home: the edges are Green, both directions, and that lift's verdict becomes Green.
        /// The same base pad without the road is no way out. The fixture: the first synthetic full case with a lift whose verdict is not
        /// Green and a base pad holding no lift bottom.</summary>
        private static void FlowCatTrack()
        {
            const string w = "Flow cat track";
            V2Case c = null;
            AutoPlanResult r0 = null;
            ResortPlan p0 = null;
            int lift = -1, basePad = -1;
            foreach (V2Case k in V2Fixtures.Synthetic())
            {
                if (!k.Id.EndsWith(" full", StringComparison.Ordinal))
                    continue;
                r0 = FlowRun(k, HomeLevel.Auto);
                p0 = r0.Resort;
                lift = basePad = -1;
                for (int i = 0; p0 != null && i < p0.Lifts.Length && lift < 0; i++)
                    if (p0.Lifts[i].Home != HomeVerdict.Green && p0.Lifts[i].Home != HomeVerdict.None)
                        lift = i;
                for (int q = 0; p0 != null && q < p0.Pads.Length && basePad < 0; q++)
                    if (p0.Pads[q].TouchesBase && Array.FindIndex(p0.Lifts, l => l.BottomPad == q) < 0)
                        basePad = q;   // a base pad holding no lift bottom: only the road makes it a way out
                if (lift >= 0 && basePad >= 0)
                {
                    c = k;
                    break;
                }
            }
            if (!FlowCheck(c != null, w, "no synthetic full case has a lift without a green way home and a base pad holding no lift bottom"))
                return;
            Check(!SkiRunAutoPlanner.PadIsWayOut(p0.Pads[basePad]), w, "a base pad without a road counts as a way out");
            float2 a = p0.Lifts[lift].Top.xz, b = p0.Pads[basePad].Center;
            float2 n = math.normalize(new float2(b.y - a.y, a.x - b.x)) * 5f;
            var track = new AutoObstacle { Kind = AutoObstacleKind.CatTrack, Ring = new[] { a + n, b + n, b - n, a - n } };
            AutoPlanResult r1 = FlowRun(c, HomeLevel.Auto, new List<AutoObstacle> { track }, new[] { b });
            ResortPlan p1 = r1.Resort;
            if (!FlowCheck(p1 != null && p1.Lifts.Length > lift && p1.Lifts[lift].Top.Equals(p0.Lifts[lift].Top), w, "the cat track moved the lift skeleton"))
                return;
            int pads = p1.Pads.Length, top = pads + 2 * r1.Runs.Count + 2 * lift + 1, cat = 0;
            bool fromTop = false;
            var pairs = new HashSet<long>();
            foreach (PlanEdge e in p1.Graph)
                if (e.Kind == SkiRunAutoPlanner.EdgeCatTrack)
                {
                    cat++;
                    Check(e.Band == SlopeBand.Green && e.Run == -1, w, $"cat track edge {e.From}->{e.To} band {e.Band}");
                    fromTop |= e.From == top && e.To == basePad;
                    pairs.Add(((long)e.From << 32) | (uint)e.To);
                }
            foreach (long key in pairs)
                Check(pairs.Contains(((key & 0xffffffffL) << 32) | (key >> 32)), w, "a cat track edge without its reverse");
            Check(cat >= 2 && fromTop, w, $"{cat} cat track edges, top -> base pad {fromTop}");
            Check(SkiRunAutoPlanner.PadIsWayOut(p1.Pads[basePad]), w, "the base pad with a road at its center is no way out");
            Check(p1.Lifts[lift].Home == HomeVerdict.Green, w, $"lift {lift} with a cat track to the base: {p1.Lifts[lift].Home} (want Green)");
        }
    }
}
