// Resort Planner (v2) stage A: plan graph, ways home, dead ends, verdicts, honest mix, site verdict (spec 4.6, 6.2). Runs inside Finish
// (FinishResort, one unit, Resort mode only), after the v1 junctions, kinds, clusters and warnings: there is no separate Flow stage, so the
// Home knob's Reselect restarts at Finish (A0). Reads the final runs and junctions, m_PadOf / m_PodOf (final after Pods), the lift skeleton
// (LiftPlan) and the cat track obstacles; writes only the new ResortPlan, the pod clusters and the three v2 warnings.
// Graph nodes: pads 0..P-1, then per run i its top (P + 2i) and bottom (P + 2i + 1), then per lift l its bottom (P + 2R + 2l) and top
// (+1). Edges (PlanEdge.Kind): 0 run (top -> bottom, its rated band); 3 link (run A's bottom inside or within LinkMeters of run B's outline,
// or A merges into B (MergeInto: the v1 merge junction) -> B's bottom with B's band: the skier skis the rest of B; a run bottom inside or within LinkMeters of a pad's cells -> that pad; a run
// bottom within LinkMeters of a lift bottom -> that lift's bottom node; lift bottom <-> its BottomPad; lift top -> its TopPad; lift top ->
// every run top within LiftNearM, the F-2 counter's radius); 1 lift (bottom -> top, uphill); 2 cat track (an existing cat track outline
// touching two or more nodes joins them, Green, both directions). Home per lift = the minimax band over downhill paths (every kind but 1)
// from its top to its own bottom node, its BottomPad or any base pad, ties by length (Dijkstra over (band, meters), ties by node index);
// the verdict is that band when it is at most Wanted, else Download (spec 4.6), so Black / DoubleBlack are not set in stage A.
// A dead end = a run bottom with no outgoing edge that is not on an existing run or cat track either.
// Pure and Entities-free (usings System, System.Collections.Generic, Unity.Mathematics only). Spec: docs/research/quickruns-v2/spec/stage-a-spec.md.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunAutoPlanner
    {
        public const byte EdgeRun = 0, EdgeLift = 1, EdgeCatTrack = 2, EdgeLink = 3;
        /// <summary>The region hull of the density figure is grown by this (F-7: runs + lifts + 50 m).</summary>
        public const float HullGrowM = 50f;

        private readonly List<PlanEdge> m_Edges = new List<PlanEdge>();
        private readonly List<int> m_Touch = new List<int>();

        /// <summary>End of Finish (Resort only, after kinds, clusters and v1 warnings): pads and pods (A3), the lift skeleton (A4), then
        /// RunPod / PodRuns / RunLift, the graph, ways home, dead ends, the honest mix, the site verdict inputs, the pod clusters, the
        /// Download / DeadEnd warnings and the plan fingerprint. The probe slots are copied into the plan by the caller afterwards.</summary>
        private void FinishResort(AutoPlanResult res)
        {
            res.Resort = new ResortPlan { TreeLineY = m_In.TreeLineY };
            TerrainPlan(res.Resort);   // A3: pads and pods (Pods.cs); A6 keeps this line
            LiftPlan(res.Resort, res);   // A4: the lift skeleton, Serves / near-top / at-bottom counts, NoLiftSite (Lifts.cs); A6 keeps this line
            ResortPlan plan = res.Resort;
            FanRunPlan(plan, res);   // A5: RunLift = the seeding lift, RunPod / PodRuns (Fans.cs); before PodClusters, which makes Cluster the pod
            BuildGraph(plan, res);
            WaysHome(plan, res);
            MixAndHull(plan, res);
            SiteInputs(plan, res);
            PodClusters(plan, res);
            for (int i = 0; i < plan.Lifts.Length; i++)
                if (plan.Lifts[i].Home == HomeVerdict.Download)
                    res.Warnings |= AutoPlanWarning.Download;
            if (plan.DeadEnds > 0)
                res.Warnings |= AutoPlanWarning.DeadEnd;
            plan.Fingerprint = PlanFingerprint(plan);
        }

        // ---------------------------------------------------------------- runs per pod and per lift: FanRunPlan (Fans.cs)

        /// <summary>A lattice cell inside the region bbox (the Resort layers are valid there only).</summary>
        private bool InRegionBox(float2 p, out int c)
        {
            if (!Cell(p, out c))
                return false;
            int i = c % m_Nx, j = c / m_Nx;
            return i >= m_RegionI0 && i <= m_RegionI1 && j >= m_RegionJ0 && j <= m_RegionJ1;
        }

        // ---------------------------------------------------------------- the graph

        private static int RunTopNode(int pads, int run) => pads + 2 * run;
        private static int RunBottomNode(int pads, int run) => pads + 2 * run + 1;
        private static int LiftBottomNode(int pads, int runs, int lift) => pads + 2 * runs + 2 * lift;
        private static int LiftTopNode(int pads, int runs, int lift) => pads + 2 * runs + 2 * lift + 1;

        private void BuildGraph(ResortPlan plan, AutoPlanResult res)
        {
            int pads = plan.Pads.Length, runs = res.Runs.Count, lifts = plan.Lifts.Length;
            m_Edges.Clear();
            var lo = new float2[runs];
            var hi = new float2[runs];
            for (int i = 0; i < runs; i++)
            {
                float2[] ring = res.Runs[i].Ring;
                lo[i] = new float2(float.PositiveInfinity);
                hi[i] = new float2(float.NegativeInfinity);
                for (int k = 0; k < ring.Length; k++)
                {
                    lo[i] = math.min(lo[i], ring[k]);
                    hi[i] = math.max(hi[i], ring[k]);
                }
            }
            int dead = 0;
            for (int a = 0; a < runs; a++)
            {
                AutoRun run = res.Runs[a];
                int top = RunTopNode(pads, a), bottom = RunBottomNode(pads, a);
                m_Edges.Add(new PlanEdge { From = top, To = bottom, Kind = EdgeRun, Band = run.Band, Run = a });
                int before = m_Edges.Count;
                float2 b = Bottom(run);
                for (int o = 0; o < runs; o++)
                {
                    bool merge = run.Merge && run.MergeInto == o;   // the v1 merge: its junction pair is within LinkMeters (V1-3)
                    if (o == a || (!merge && (math.any(b < lo[o] - LinkMeters) || math.any(b > hi[o] + LinkMeters))))
                        continue;
                    if (merge || OnRing(res.Runs[o].Ring, b))
                        m_Edges.Add(new PlanEdge { From = bottom, To = RunBottomNode(pads, o), Kind = EdgeLink, Band = res.Runs[o].Band, Run = o });
                }
                m_Touch.Clear();
                PadsNear(b, LinkMeters, m_Touch);
                for (int k = 0; k < m_Touch.Count; k++)
                    m_Edges.Add(new PlanEdge { From = bottom, To = m_Touch[k], Kind = EdgeLink, Band = SlopeBand.Flat, Run = -1 });
                for (int l = 0; l < lifts; l++)
                    if (Finite3(plan.Lifts[l].Bottom) && math.distance(b, plan.Lifts[l].Bottom.xz) <= LinkMeters)
                        m_Edges.Add(new PlanEdge { From = bottom, To = LiftBottomNode(pads, runs, l), Kind = EdgeLink, Band = SlopeBand.Flat, Run = -1 });
                if (m_Edges.Count == before && !OnExistingRun(b) && !OnCatTrack(b))
                    dead++;
            }
            plan.DeadEnds = dead;
            for (int l = 0; l < lifts; l++)
            {
                PlanLift lift = plan.Lifts[l];
                int lb = LiftBottomNode(pads, runs, l), lt = LiftTopNode(pads, runs, l);
                m_Edges.Add(new PlanEdge { From = lb, To = lt, Kind = EdgeLift, Band = SlopeBand.Flat, Run = -1 });
                if (lift.BottomPad >= 0 && lift.BottomPad < pads)
                {
                    m_Edges.Add(new PlanEdge { From = lift.BottomPad, To = lb, Kind = EdgeLink, Band = SlopeBand.Flat, Run = -1 });
                    m_Edges.Add(new PlanEdge { From = lb, To = lift.BottomPad, Kind = EdgeLink, Band = SlopeBand.Flat, Run = -1 });
                }
                if (lift.TopPad >= 0 && lift.TopPad < pads)
                    m_Edges.Add(new PlanEdge { From = lt, To = lift.TopPad, Kind = EdgeLink, Band = SlopeBand.Flat, Run = -1 });
                if (!Finite3(lift.Top))
                    continue;
                for (int r = 0; r < runs; r++)
                    if (math.distance(Top(res.Runs[r]), lift.Top.xz) <= LiftNearM)
                        m_Edges.Add(new PlanEdge { From = lt, To = RunTopNode(pads, r), Kind = EdgeLink, Band = SlopeBand.Flat, Run = -1 });
            }
            CatTrackEdges(plan, res);
            plan.Graph = m_Edges.ToArray();
        }

        /// <summary>Existing cat tracks (obstacles) touching two or more nodes join them: Green, both directions (spec 4.6). A point node
        /// touches when it lies inside the outline or within LinkMeters of it; a pad when a cell of it lies within LinkMeters of a ring vertex.</summary>
        private void CatTrackEdges(ResortPlan plan, AutoPlanResult res)
        {
            List<AutoObstacle> obs = m_In.Obstacles;
            if (obs == null)
                return;
            int pads = plan.Pads.Length, runs = res.Runs.Count, lifts = plan.Lifts.Length;
            var touch = new List<int>();
            for (int o = 0; o < obs.Count; o++)
            {
                if (obs[o].Kind != AutoObstacleKind.CatTrack || obs[o].Ring == null || obs[o].Ring.Length < 2)
                    continue;
                float2[] ring = obs[o].Ring;
                touch.Clear();
                m_Touch.Clear();
                for (int k = 0; k < ring.Length; k++)
                    PadsNear(ring[k], LinkMeters, m_Touch);
                m_Touch.Sort();
                for (int k = 0; k < m_Touch.Count; k++)
                    if (k == 0 || m_Touch[k] != m_Touch[k - 1])
                        touch.Add(m_Touch[k]);
                for (int r = 0; r < runs; r++)
                {
                    if (OnRing(ring, Top(res.Runs[r])))
                        touch.Add(RunTopNode(pads, r));
                    if (OnRing(ring, Bottom(res.Runs[r])))
                        touch.Add(RunBottomNode(pads, r));
                }
                for (int l = 0; l < lifts; l++)
                {
                    if (Finite3(plan.Lifts[l].Bottom) && OnRing(ring, plan.Lifts[l].Bottom.xz))
                        touch.Add(LiftBottomNode(pads, runs, l));
                    if (Finite3(plan.Lifts[l].Top) && OnRing(ring, plan.Lifts[l].Top.xz))
                        touch.Add(LiftTopNode(pads, runs, l));
                }
                for (int x = 0; x < touch.Count; x++)
                    for (int y = 0; y < touch.Count; y++)
                        if (x != y)
                            m_Edges.Add(new PlanEdge { From = touch[x], To = touch[y], Kind = EdgeCatTrack, Band = SlopeBand.Green, Run = -1 });
            }
        }

        private static bool OnRing(float2[] ring, float2 p) => GradeRaster.Inside(ring, p) || PointRingDistance(ring, p) <= LinkMeters;

        private static float2 Top(AutoRun run) => run.Centerline[0];
        private static float2 Bottom(AutoRun run) => run.Centerline[run.Centerline.Length - 1];

        private bool OnExistingRun(float2 p)
        {
            List<AutoObstacle> obs = m_In.Obstacles;
            for (int o = 0; obs != null && o < obs.Count; o++)
                if (obs[o].Kind == AutoObstacleKind.ExistingRun && obs[o].Ring != null && obs[o].Ring.Length >= 3 && OnRing(obs[o].Ring, p))
                    return true;
            return false;
        }

        private bool OnCatTrack(float2 p)
        {
            List<AutoObstacle> obs = m_In.Obstacles;
            for (int o = 0; obs != null && o < obs.Count; o++)
                if (obs[o].Kind == AutoObstacleKind.CatTrack && obs[o].Ring != null && obs[o].Ring.Length >= 2 && OnRing(obs[o].Ring, p))
                    return true;
            return false;
        }

        /// <summary>Appends (ascending, once each) the pads with a cell center within r of p (region bbox cells only).</summary>
        private void PadsNear(float2 p, float r, List<int> into)
        {
            if (m_PadOf == null || m_Texel <= 0f)
                return;
            long ci = (long)Math.Round((p.x - m_Origin.x) / m_Texel) - m_LI0, cj = (long)Math.Round((p.y - m_Origin.y) / m_Texel) - m_LJ0;
            int reach = (int)Math.Ceiling(r / m_Texel) + 1;
            int start = into.Count;
            for (long j = Math.Max(m_RegionJ0, cj - reach); j <= Math.Min(m_RegionJ1, cj + reach); j++)
                for (long i = Math.Max(m_RegionI0, ci - reach); i <= Math.Min(m_RegionI1, ci + reach); i++)
                {
                    int c = (int)(j * m_Nx + i);
                    byte pad = m_PadOf[c];
                    if (pad == PadNone || pad >= m_PadList.Count || math.distance(CellCenter(c), p) > r)
                        continue;
                    bool seen = false;
                    for (int k = start; k < into.Count && !seen; k++)
                        seen = into[k] == pad;
                    if (!seen)
                        into.Add(pad);
                }
            into.Sort(start, into.Count - start, null);
        }

        // ---------------------------------------------------------------- ways home

        /// <summary>Per lift: Wanted (the knob; Auto = Green when BottomPad is a base pad, else Blue), Home (Green / Blue when the easiest
        /// downhill path's hardest band is at most Wanted, else Download; None for a pinned lift with neither station inside the region
        /// box), HomeRun (the first run of the easiest path, any band; -1 none).</summary>
        private void WaysHome(ResortPlan plan, AutoPlanResult res)
        {
            int pads = plan.Pads.Length, runs = res.Runs.Count, lifts = plan.Lifts.Length;
            int n = pads + 2 * runs + 2 * lifts;
            PlanEdge[] g = plan.Graph;
            var meters = new double[g.Length];
            for (int e = 0; e < g.Length; e++)
                meters[e] = EdgeLength(g[e], plan, res);
            var target = new bool[n];
            for (int l = 0; l < lifts; l++)
            {
                ref PlanLift lift = ref plan.Lifts[l];
                bool baseBottom = lift.BottomPad >= 0 && lift.BottomPad < pads && plan.Pads[lift.BottomPad].IsBase;
                HomeLevel wanted = m_Knobs.Home == HomeLevel.Auto ? (baseBottom ? HomeLevel.Green : HomeLevel.Blue) : m_Knobs.Home;
                lift.Wanted = wanted;
                lift.HomeRun = -1;
                if (lift.Origin == LiftOrigin.Pinned && !(Finite3(lift.Top) && InRegionBox(lift.Top.xz, out _)) &&
                    !(Finite3(lift.Bottom) && InRegionBox(lift.Bottom.xz, out _)))
                {
                    lift.Home = HomeVerdict.None;
                    continue;
                }
                Array.Clear(target, 0, n);
                for (int k = 0; k < pads; k++)
                    target[k] = plan.Pads[k].IsBase || k == lift.BottomPad;
                target[LiftBottomNode(pads, runs, l)] = true;
                int easiest = EasiestDescent(n, g, meters, LiftTopNode(pads, runs, l), target, pads, out lift.HomeRun);
                int want = wanted == HomeLevel.Green ? (int)SlopeBand.Green : (int)SlopeBand.Blue;
                lift.Home = easiest >= 0 && Math.Max(easiest, (int)SlopeBand.Green) <= want
                    ? (HomeVerdict)Math.Max(easiest, (int)SlopeBand.Green) : HomeVerdict.Download;
            }
        }

        /// <summary>The easiest descent on a plan graph: Dijkstra over (hardest band so far, meters) from src along every edge but lifts
        /// (Kind 1), ties by node index, until the first target node is settled. Link edges from src onto a pad (node below srcPadLimit)
        /// are skipped: getting off a lift onto its own top pad is no way home (a cat track from the top still is). Returns the hardest band (SlopeBand value) of that path, -1 = no
        /// path; firstRun = the run of the path's first edge that carries one (-1 none).</summary>
        public static int EasiestDescent(int nodes, PlanEdge[] g, double[] meters, int src, bool[] target, int srcPadLimit, out int firstRun)
        {
            firstRun = -1;
            var first = new int[nodes + 1];
            for (int e = 0; e < g.Length; e++)
                if (g[e].Kind != EdgeLift)
                    first[g[e].From + 1]++;
            for (int v = 0; v < nodes; v++)
                first[v + 1] += first[v];
            var adj = new int[first[nodes]];
            var at = new int[nodes];
            for (int e = 0; e < g.Length; e++)
                if (g[e].Kind != EdgeLift)
                    adj[first[g[e].From] + at[g[e].From]++] = e;
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
                            firstRun = g[pred[v]].Run;   // the last one written is the first run below src
                    return band[u];
                }
                for (int k = first[u]; k < first[u + 1]; k++)
                {
                    int e = adj[k], w = g[e].To;
                    if (done[w] || (u == src && w < srcPadLimit && g[e].Kind == EdgeLink))
                        continue;
                    int b = Math.Max(band[u], (int)g[e].Band);
                    double d = len[u] + meters[e];
                    if (b < band[w] || (b == band[w] && d < len[w]))
                    {
                        band[w] = b;
                        len[w] = d;
                        pred[w] = e;
                    }
                }
            }
        }

        /// <summary>Meters of an edge: a run its length, any other edge the xz distance between its nodes.</summary>
        private static double EdgeLength(in PlanEdge e, ResortPlan plan, AutoPlanResult res)
        {
            if (e.Kind == EdgeRun)
                return res.Runs[e.Run].LengthM;
            return math.distance(NodeXz(e.From, plan, res), NodeXz(e.To, plan, res));
        }

        private static float2 NodeXz(int v, ResortPlan plan, AutoPlanResult res)
        {
            int pads = plan.Pads.Length, runs = res.Runs.Count;
            if (v < pads)
                return plan.Pads[v].Center;
            v -= pads;
            if (v < 2 * runs)
                return (v & 1) == 0 ? Top(res.Runs[v >> 1]) : Bottom(res.Runs[v >> 1]);
            v -= 2 * runs;
            PlanLift l = plan.Lifts[v >> 1];
            return (v & 1) == 0 ? l.Bottom.xz : l.Top.xz;
        }

        // ---------------------------------------------------------------- honest mix, density, site verdict, pod clusters

        /// <summary>MountainMix = run-km share per rated band (the mix the terrain gave); RunKm; HullKm2 = the convex hull of every run
        /// outline vertex and lift station grown by HullGrowM (Steiner: area + perimeter x grow + pi x grow^2); RunKmPerKm2.</summary>
        private void MixAndHull(ResortPlan plan, AutoPlanResult res)
        {
            double total = 0.0;
            var km = new double[5];
            var pts = new List<float2>();
            for (int i = 0; i < res.Runs.Count; i++)
            {
                AutoRun run = res.Runs[i];
                km[(int)run.Band] += run.LengthM;
                total += run.LengthM;
                pts.AddRange(run.Ring);
            }
            for (int b = 0; b < 5; b++)
                plan.MountainMix[b] = total > 0.0 ? (float)(km[b] / total) : 0f;
            for (int l = 0; l < plan.Lifts.Length; l++)
            {
                if (Finite3(plan.Lifts[l].Bottom))
                    pts.Add(plan.Lifts[l].Bottom.xz);
                if (Finite3(plan.Lifts[l].Top))
                    pts.Add(plan.Lifts[l].Top.xz);
            }
            plan.RunKm = (float)(total / 1000.0);
            plan.HullKm2 = (float)HullKm2(pts, HullGrowM);
            plan.RunKmPerKm2 = plan.HullKm2 > 0f ? plan.RunKm / plan.HullKm2 : 0f;
        }

        /// <summary>Area (km2) of the convex hull (monotone chain) of the points grown by grow meters.</summary>
        public static double HullKm2(List<float2> pts, double grow)
        {
            var p = new List<float2>(pts);
            p.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            if (p.Count < 3)
                return p.Count == 0 ? 0.0 : Math.PI * grow * grow / 1.0e6;
            var h = new float2[2 * p.Count];
            int k = 0;
            for (int i = 0; i < p.Count; i++)
            {
                while (k >= 2 && Cross(h[k - 2], h[k - 1], p[i]) <= 0f)
                    k--;
                h[k++] = p[i];
            }
            for (int i = p.Count - 2, t = k + 1; i >= 0; i--)
            {
                while (k >= t && Cross(h[k - 2], h[k - 1], p[i]) <= 0f)
                    k--;
                h[k++] = p[i];
            }
            double area = 0.0, perim = 0.0;
            for (int i = 0; i + 1 < k; i++)
            {
                area += (double)h[i].x * h[i + 1].y - (double)h[i + 1].x * h[i].y;
                perim += math.distance(h[i], h[i + 1]);
            }
            return (Math.Abs(area) * 0.5 + perim * grow + Math.PI * grow * grow) / 1.0e6;
        }

        /// <summary>The site verdict inputs (spec 6.2): Kind = the v1 tier of the v1 figure (QuickRunsRules.SiteTierOf of the shown
        /// percent, so the line the player sees is v1's), plus the pods with a Green way home, the largest bench and base pad.</summary>
        private static void SiteInputs(ResortPlan plan, AutoPlanResult res)
        {
            var s = new SiteVerdict
            {
                Kind = (byte)QuickRunsRules.SiteTierOf(QuickRunsRules.SitePercent(res.MeanGrade)),
                MeanGrade = res.MeanGrade, SteepShare = res.SteepShare, Pods = plan.Pods.Length,
            };
            for (int k = 0; k < plan.Pads.Length; k++)
            {
                float ha = plan.Pads[k].AreaM2 / 1.0e4f;
                if (plan.Pads[k].IsBench)
                    s.LargestBenchHa = Math.Max(s.LargestBenchHa, ha);
                if (plan.Pads[k].IsBase)
                    s.BaseBenchHa = Math.Max(s.BaseBenchHa, ha);
            }
            for (int k = 0; k < plan.Pods.Length; k++)
            {
                int l = plan.Pods[k].Lift;
                if (l >= 0 && l < plan.Lifts.Length && plan.Lifts[l].Home == HomeVerdict.Green)
                    s.PodsWithGreenHome++;
            }
            plan.Site = s;
        }

        /// <summary>Resort mode: Clusters carries one cluster per pod (Center = TopCenter, Pod = true, PodBand = the pod class's rated band;
        /// Runs / Linked / HasGreen over the pod's runs) and each run's Cluster is its pod (-1 = none), so the v1 readers (cluster rows,
        /// hover line) keep working on pods.</summary>
        private static void PodClusters(ResortPlan plan, AutoPlanResult res)
        {
            int n = res.Runs.Count;
            var parent = new int[n];
            for (int i = 0; i < n; i++)
                parent[i] = i;
            for (int j = 0; j < res.Junctions.Count; j++)
            {
                AutoJunction jn = res.Junctions[j];
                if (jn.Existing || plan.RunPod[jn.RunA] != plan.RunPod[jn.RunB])
                    continue;
                int a = Root(parent, jn.RunA), b = Root(parent, jn.RunB);
                if (a != b)
                    parent[Math.Max(a, b)] = Math.Min(a, b);
            }
            res.Clusters.Clear();
            var size = new int[Math.Max(1, n)];
            for (int k = 0; k < plan.Pods.Length; k++)
            {
                PlanPod pod = plan.Pods[k];
                var cl = new AutoCluster { Center = pod.TopCenter, TopY = pod.TopY, Pod = true, PodBand = (SlopeBand)ScoreBandOf(pod.PrimaryClass) };
                for (int q = pod.RunFirst; q < pod.RunFirst + pod.RunCount; q++)
                {
                    int i = plan.PodRuns[q];
                    cl.Runs++;
                    cl.HasGreen |= res.Runs[i].Band == SlopeBand.Green;
                    int root = Root(parent, i);
                    cl.Linked = Math.Max(cl.Linked, ++size[root]);
                }
                res.Clusters.Add(cl);
            }
            // Runs outside every pod share one trailing plain cluster (v1 shape: its center is the highest of their tops), so every run
            // keeps a cluster (the v1 invariant)
            int outside = -1;
            for (int i = 0; i < n; i++)
            {
                if (plan.RunPod[i] >= 0)
                {
                    res.Runs[i].Cluster = plan.RunPod[i];
                    continue;
                }
                AutoRun run = res.Runs[i];
                if (outside < 0)
                {
                    outside = res.Clusters.Count;
                    res.Clusters.Add(new AutoCluster { Center = Top(run), TopY = run.TopY, PodBand = SlopeBand.Flat });
                }
                AutoCluster o = res.Clusters[outside];
                if (run.TopY > o.TopY)
                {
                    o.Center = Top(run);
                    o.TopY = run.TopY;
                }
                o.Runs++;
                o.HasGreen |= run.Band == SlopeBand.Green;
                o.Linked = Math.Max(o.Linked, ++size[Root(parent, i)]);
                res.Clusters[outside] = o;
                run.Cluster = outside;
            }
        }

        // ---------------------------------------------------------------- fingerprint

        /// <summary>FNV-1a over pads, pods, lifts, verdicts, the run maps, the graph, dead ends, mix and site inputs; never 0 (0 = no plan
        /// content, AutoPlanResult.Fingerprint skips it).</summary>
        private static ulong PlanFingerprint(ResortPlan p)
        {
            ulong h = 14695981039346656037UL;
            h = Mix(h, (uint)p.Pads.Length);
            for (int i = 0; i < p.Pads.Length; i++)
            {
                PlanPad d = p.Pads[i];
                h = Mix(h, (uint)d.Kind);
                h = Mix(h, math.asuint(d.AreaM2));
                h = Mix(h, math.asuint(d.Center.x));
                h = Mix(h, math.asuint(d.Center.y));
                h = Mix(h, (d.IsBase ? 1u : 0u) | (d.IsBench ? 2u : 0u));
            }
            h = Mix(h, (uint)p.Pods.Length);
            for (int i = 0; i < p.Pods.Length; i++)
            {
                PlanPod d = p.Pods[i];
                h = Mix(h, d.Key);
                h = Mix(h, (uint)d.LowerPad);
                h = Mix(h, (uint)d.UpperPad);
                h = Mix(h, (uint)d.PrimaryClass);
                h = Mix(h, (uint)d.Lift);
                h = Mix(h, (uint)d.RunFirst);
                h = Mix(h, (uint)d.RunCount);
            }
            h = Mix(h, (uint)p.Lifts.Length);
            for (int i = 0; i < p.Lifts.Length; i++)
            {
                PlanLift d = p.Lifts[i];
                h = Mix(h, d.Key);
                h = Mix(h, (uint)d.Origin);
                h = Mix(h, d.TypeIndex);
                h = Mix(h, math.asuint(d.Bottom.x));
                h = Mix(h, math.asuint(d.Bottom.z));
                h = Mix(h, math.asuint(d.Top.x));
                h = Mix(h, math.asuint(d.Top.z));
                h = Mix(h, (uint)d.Towers);
                h = Mix(h, (uint)d.Home);
                h = Mix(h, (uint)d.Wanted);
                h = Mix(h, (uint)d.HomeRun);
                h = Mix(h, (uint)d.Serves);
            }
            for (int i = 0; i < p.RunPod.Length; i++)
            {
                h = Mix(h, (uint)p.RunPod[i]);
                h = Mix(h, (uint)p.RunLift[i]);
            }
            for (int i = 0; i < p.Graph.Length; i++)
            {
                PlanEdge e = p.Graph[i];
                h = Mix(h, (uint)e.From);
                h = Mix(h, (uint)e.To);
                h = Mix(h, e.Kind | ((uint)e.Band << 8));
                h = Mix(h, (uint)e.Run);
            }
            h = Mix(h, (uint)p.DeadEnds);
            for (int b = 0; b < 5; b++)
                h = Mix(h, math.asuint(p.MountainMix[b]));
            h = Mix(h, p.Site.Kind);
            h = Mix(h, (uint)p.Site.PodsWithGreenHome);
            return h == 0UL ? 1UL : h;
        }
    }
}
