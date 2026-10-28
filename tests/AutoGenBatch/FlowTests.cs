// Flow suite (A6): a hand graph with a known minimax descent (EasiestDescent), then per synthetic fixture the plan graph, ways home and
// the rest of FinishResort: H-1 to H-3 and F-10 HARD, H-4 / H-5 REPORT while the runs are v1-shaped (A5's sinks put run ends on pads;
// the final H-* pass is after A5 merges), the graph shape, RunPod / PodRuns / pod clusters, dead ends recounted from the graph (Steep1 is
// the dead end fixture: v1 runs end in the base band where no pad lies), the warnings, the site inputs, the Home knob, a cat track as a
// green way home, the fingerprint fold.

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

        private static AutoPlanResult FlowRun(V2Case c, HomeLevel home, List<AutoObstacle> obstacles = null)
        {
            AutoPlanKnobs knobs = AutoPlanKnobs.Default(SkiRunAutoPlanner.SeedOf(c.Region, 0));
            knobs.Home = home;
            var input = new AutoPlanInput { Snapshot = c.Snap(), Region = c.Region, Knobs = knobs, Mode = AutoPlanMode.Resort, Obstacles = obstacles };
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
                Console.WriteLine($"REPORT {g.Id} {w}: {g.Text}{(g.Pass ? " [ok]" : " [REPORT until A5 merges]")}");

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

            // Dead ends = run bottoms without an outgoing edge (no obstacles here); the warnings follow the counts
            int dead = 0;
            for (int i = 0; i < runs; i++)
                dead += outDeg[pads + 2 * i + 1] == 0 ? 1 : 0;
            Check(dead == p.DeadEnds, w, $"DeadEnds {p.DeadEnds}, graph says {dead}");
            Check(((r.Warnings & AutoPlanWarning.DeadEnd) != 0) == (p.DeadEnds > 0), w, "DeadEnd warning does not follow DeadEnds");
            if (c.Id == "Steep1 full")
                Check(p.DeadEnds > 0 && (r.Warnings & AutoPlanWarning.DeadEnd) != 0, w, "the dead end fixture shows no dead end");
            bool anyDownload = false;
            foreach (PlanLift l in p.Lifts)
            {
                anyDownload |= l.Home == HomeVerdict.Download;
                Check(l.Home != HomeVerdict.Black && l.Home != HomeVerdict.DoubleBlack, w, $"lift {l.Id}: verdict {l.Home} above the wanted level (spec 4.6: Download)");
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
            int green = 0;
            foreach (PlanPod pod in p.Pods)
                green += pod.Lift >= 0 && p.Lifts[pod.Lift].Home == HomeVerdict.Green ? 1 : 0;
            Check(p.Site.Kind == (byte)QuickRunsRules.SiteTierOf(QuickRunsRules.SitePercent(r.MeanGrade)) && p.Site.Pods == p.Pods.Length &&
                  p.Site.PodsWithGreenHome == green && p.Site.MeanGrade == r.MeanGrade, w, "site inputs");

            // Fingerprint fold
            AutoPlanInput runsIn = new AutoPlanInput { Snapshot = snap, Region = c.Region, Knobs = AutoPlanKnobs.Default(SkiRunAutoPlanner.SeedOf(c.Region, 0)) };
            Check(p.Fingerprint != 0UL && r.Fingerprint != new SkiRunAutoPlanner().Run(runsIn).Fingerprint, w, "the plan fingerprint is not folded in");

            // Home knob: Wanted follows it; Green under the Green knob stays Green under Blue; Auto = Green on base bottoms, Blue above
            AutoPlanResult rg = FlowRun(c, HomeLevel.Green), rb = FlowRun(c, HomeLevel.Blue);
            for (int i = 0; i < lifts && i < rg.Resort.Lifts.Length && i < rb.Resort.Lifts.Length; i++)
            {
                PlanLift a = p.Lifts[i], lg = rg.Resort.Lifts[i], lb = rb.Resort.Lifts[i];
                bool baseBottom = a.BottomPad >= 0 && p.Pads[a.BottomPad].IsBase;
                Check(a.Wanted == (baseBottom ? HomeLevel.Green : HomeLevel.Blue) && lg.Wanted == HomeLevel.Green && lb.Wanted == HomeLevel.Blue, w, $"lift {i}: Wanted {a.Wanted} / {lg.Wanted} / {lb.Wanted}");
                Check(lg.Home == HomeVerdict.Green || lg.Home == HomeVerdict.Download, w, $"lift {i}: {lg.Home} under the Green knob");
                Check(lg.Home != HomeVerdict.Green || lb.Home == HomeVerdict.Green, w, $"lift {i}: Green under the Green knob but {lb.Home} under Blue");
                Check(lb.Home != HomeVerdict.Download || lg.Home == HomeVerdict.Download, w, $"lift {i}: Download under Blue but {lg.Home} under Green");
            }

            var homes = new int[6];
            foreach (PlanLift l in p.Lifts)
                homes[(int)l.Home]++;
            Console.WriteLine($"REPORT flow {c.Id}: {lifts} lifts, home G/B/Download {homes[1]}/{homes[2]}/{homes[5]}, dead ends {p.DeadEnds} of {runs}, " +
                              $"{p.Graph.Length} edges, green home pods {p.Site.PodsWithGreenHome} of {p.Site.Pods}, mix G {p.MountainMix[1] * 100:F0} B {p.MountainMix[2] * 100:F0} K {p.MountainMix[3] * 100:F0} DB {p.MountainMix[4] * 100:F0} %, Flow unit {p.StageMaxMs[5]:F2} ms");
        }

        /// <summary>A cat track (obstacle outline) from a lift top down to the base pad is a green way home: the edges are Green, both
        /// directions, and that lift's verdict becomes Green.</summary>
        private static void FlowCatTrack()
        {
            const string w = "Flow cat track";
            V2Case c = null;
            foreach (V2Case k in V2Fixtures.Synthetic())
                if (k.Id == "Steep1 full")
                    c = k;
            if (!FlowCheck(c != null, w, "no Steep1 fixture"))
                return;
            AutoPlanResult r0 = FlowRun(c, HomeLevel.Auto);
            ResortPlan p0 = r0.Resort;
            int lift = -1, basePad = -1;
            for (int i = 0; i < p0.Lifts.Length && lift < 0; i++)
                if (p0.Lifts[i].Home == HomeVerdict.Download)
                    lift = i;
            for (int k = 0; k < p0.Pads.Length && basePad < 0; k++)
                if (p0.Pads[k].IsBase)
                    basePad = k;
            if (!FlowCheck(lift >= 0 && basePad >= 0, w, $"Steep1 has no Download lift ({lift}) or no base pad ({basePad})"))
                return;
            float2 a = p0.Lifts[lift].Top.xz, b = p0.Pads[basePad].Center;
            float2 n = math.normalize(new float2(b.y - a.y, a.x - b.x)) * 5f;
            var track = new AutoObstacle { Kind = AutoObstacleKind.CatTrack, Ring = new[] { a + n, b + n, b - n, a - n } };
            AutoPlanResult r1 = FlowRun(c, HomeLevel.Auto, new List<AutoObstacle> { track });
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
            Check(p1.Lifts[lift].Home == HomeVerdict.Green, w, $"lift {lift} with a cat track to the base: {p1.Lifts[lift].Home} (want Green)");
        }
    }
}
