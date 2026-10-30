// Resort Planner (v2) stage A: plan graph, ways home, dead ends, verdicts, honest mix, site verdict (spec 4.6, 6.2). Runs inside Finish
// (FinishResort, one unit, Resort mode only), after the v1 junctions, kinds, clusters and warnings: there is no separate Flow stage, so the
// Home knob's Reselect restarts at Finish (A0). Reads the final runs and junctions, m_PadOf / m_PodOf (final after Pods), the lift skeleton
// (LiftPlan) and the cat track obstacles; writes only the new ResortPlan, the pod clusters and the three v2 warnings.
// Graph nodes: pads 0..P-1, then per run i its top (P + 2i) and bottom (P + 2i + 1), then per lift l its bottom (P + 2R + 2l) and top
// (+1). Edges (PlanEdge.Kind): 0 run (top -> bottom, its rated band); 3 link (run A's bottom inside or within LinkMeters of run B's outline,
// or A merges into B (MergeInto: the v1 merge junction) -> B's bottom with B's band: the skier skis the rest of B; a run bottom with no
// such link (a merge whose junction BuildRing dropped, or a fan line stopped at the near gap) -> the nearest run within the merge stop
// when the outlines pass the game's own link rule (LiftServedRule.OutlinesLinked); a run bottom inside or within LinkMeters of a pad's cells -> that pad; a run
// bottom within LinkMeters of a lift bottom -> that lift's bottom node; lift bottom <-> its BottomPad; lift top -> its TopPad; lift top ->
// every run the served rule reaches (LiftServedRule.Reaches on the run's highest outline node: the corridor and top margin, AR-3b)); 1 lift
// (bottom -> top, uphill); 2 cat track (an existing cat track outline touching two or more nodes joins them, Green, both directions).
// Way out (DECISIONS 451 c, AR-2): every lift bottom node, every pad holding a lift bottom, and a pad touching the v1 base band with an
// access point (road, Welcome Sign) within its radius plus HomeAccessWalkM; ground is never a way out by itself (no lowest-pad fallback).
// A cat track touching one of those reaches it through its edges. Home per lift = the minimax band over downhill paths (every kind but 1)
// from its top to a way out, ties by length (Dijkstra over (band, meters), ties by node index, binary heap): that band itself
// (DECISIONS 451 b: Green .. Double Black), Download only when no downhill path reaches a way out (a legitimate design, not an error).
// A dead end = a run bottom with no downhill path to a way out that is not on an existing run or cat track either.
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
        /// <summary>A base pad is a way out when an access point lies within its radius (PadRadiusM) plus this walk (AR-2).</summary>
        public const float HomeAccessWalkM = 100f;

        private readonly List<PlanEdge> m_Edges = new List<PlanEdge>();
        private readonly List<int> m_Touch = new List<int>(), m_CatTouch = new List<int>();
        private readonly List<LiftServedRule.Chord> m_ServeChords = new List<LiftServedRule.Chord>();
        private readonly List<float2> m_HullPts = new List<float2>();
        private readonly DescentScratch m_Descent = new DescentScratch();
        private float2[] m_RunLo = new float2[0], m_RunHi = new float2[0];
        private float3[] m_RunTop3 = new float3[0];
        private double[] m_EdgeM = new double[0];
        private bool[] m_HomeTarget = new bool[0], m_WayOut = new bool[0];
        private int[] m_RevFirst = new int[0], m_RevAdj = new int[0], m_RevAt = new int[0], m_FlowQueue = new int[0], m_ClParent = new int[0], m_ClSize = new int[0];

        /// <summary>A pad's reach radius: the larger of its inscribed distance and its equal-area disc (the host oracle uses the same).</summary>
        public static float PadRadiusM(in PlanPad pad) => Math.Max(pad.InscribedM, (float)Math.Sqrt(pad.AreaM2 / Math.PI));

        /// <summary>A base pad that is a way out by itself (AR-2): it touches the v1 base band and an access point lies within its radius
        /// plus HomeAccessWalkM (AccessM is +inf without access points, so a plan without roads has no such pad).</summary>
        public static bool PadIsWayOut(in PlanPad pad) => pad.TouchesBase && pad.AccessM <= PadRadiusM(pad) + HomeAccessWalkM;

        /// <summary>End of Finish (Resort only, after kinds, clusters and v1 warnings): pads and pods (A3), the lift skeleton (A4), then
        /// RunPod / PodRuns / RunLift, the graph, ways home, dead ends, the honest mix, the site verdict inputs, the pod clusters, the
        /// Download / DeadEnd warnings and the plan fingerprint. The probe slots are copied into the plan by the caller afterwards.</summary>
        private void FinishResort(AutoPlanResult res)
        {
            res.Resort = new ResortPlan { TreeLineY = m_In.TreeLineY };
            TerrainPlan(res.Resort);   // A3: pads and pods (Pods.cs); A6 keeps this line
            LiftPlan(res.Resort, res);   // A4: the lift skeleton, Serves / near-top / at-bottom counts, NoLiftSite (Lifts.cs); A6 keeps this line
            ResortPlan plan = res.Resort;
            PadBaseTouch(plan);
            FanRunPlan(plan, res);   // A5: RunLift = the seeding lift, RunPod / PodRuns (Fans.cs); before PodClusters, which makes Cluster the pod
            BuildGraph(plan, res);
            HomeTargets(plan, res);
            CountDeadEnds(plan, res);
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

        /// <summary>TouchesBase per pad from the pad components (Terrain.cs keeps IsBase with its lowest-pad fallback for the S gates;
        /// the way out never reads that fallback, DECISIONS 451 c).</summary>
        private void PadBaseTouch(ResortPlan plan)
        {
            for (int k = 0; k < m_PadComps.Count; k++)
            {
                int pad = m_PadComps[k].Pad;
                if (pad >= 0 && pad < plan.Pads.Length)
                    plan.Pads[pad].TouchesBase = m_PadComps[k].Base;
            }
        }

        /// <summary>The merge stop of a fan line (FanApproach, FanWalk): its centerline runs on to this distance from the accepted ring
        /// (the v1 gap, at least FanNearGapM, plus the pinch half width), capped at JunctionReachM plus that half width so the end of a
        /// ring at least MinHalfWidthM wide lies within JunctionReachM of its parent and BuildRing keeps the junction (AR-1).</summary>
        /// <summary>NearRunLink's search radius: the merge stop's cap (JunctionReachM + MinHalfWidthM) plus a half width of slack
        /// for the cell walk and the smoothed centerline (the host oracle uses the same constant).</summary>
        public const float MergeLinkReachM = JunctionReachM + 2f * MinHalfWidthM;

        private float FanMergeStopM(int pass) => Math.Min(FanNearGap(pass), JunctionReachM) + MinHalfWidthM;

        private void BuildGraph(ResortPlan plan, AutoPlanResult res)
        {
            int pads = plan.Pads.Length, runs = res.Runs.Count, lifts = plan.Lifts.Length;
            m_Edges.Clear();
            Grow(ref m_RunLo, runs);
            Grow(ref m_RunHi, runs);
            Grow(ref m_RunTop3, runs);
            for (int i = 0; i < runs; i++)
            {
                AutoRun run = res.Runs[i];
                float2[] ring = run.Ring;
                m_RunLo[i] = new float2(float.PositiveInfinity);
                m_RunHi[i] = new float2(float.NegativeInfinity);
                for (int k = 0; k < ring.Length; k++)
                {
                    m_RunLo[i] = math.min(m_RunLo[i], ring[k]);
                    m_RunHi[i] = math.max(m_RunHi[i], ring[k]);
                }
                // the served rule's run top: the highest outline node (Lifts.cs Serves reads the same)
                float3 top = run.Nodes != null && run.Nodes.Length > 0 ? run.Nodes[0] : new float3(float.NaN);
                for (int v = 1; run.Nodes != null && v < run.Nodes.Length; v++)
                    if (run.Nodes[v].y > top.y)
                        top = run.Nodes[v];
                m_RunTop3[i] = top;
            }
            for (int a = 0; a < runs; a++)
            {
                AutoRun run = res.Runs[a];
                int top = RunTopNode(pads, a), bottom = RunBottomNode(pads, a);
                m_Edges.Add(new PlanEdge { From = top, To = bottom, Kind = EdgeRun, Band = run.Band, Run = a });
                float2 b = Bottom(run);
                bool runLink = false;
                for (int o = 0; o < runs; o++)
                {
                    bool merge = run.Merge && run.MergeInto == o;   // the v1 merge: its junction pair is within LinkMeters (V1-3)
                    if (o == a || (!merge && (math.any(b < m_RunLo[o] - LinkMeters) || math.any(b > m_RunHi[o] + LinkMeters))))
                        continue;
                    if (merge || OnRing(res.Runs[o].Ring, b))
                    {
                        m_Edges.Add(new PlanEdge { From = bottom, To = RunBottomNode(pads, o), Kind = EdgeLink, Band = res.Runs[o].Band, Run = o });
                        runLink = true;
                    }
                }
                if (!runLink)
                    NearRunLink(res, a, bottom, pads);
                m_Touch.Clear();
                PadsNear(b, LinkMeters, m_Touch);
                for (int k = 0; k < m_Touch.Count; k++)
                    m_Edges.Add(new PlanEdge { From = bottom, To = m_Touch[k], Kind = EdgeLink, Band = SlopeBand.Flat, Run = -1 });
                for (int l = 0; l < lifts; l++)
                    if (Finite3(plan.Lifts[l].Bottom) && math.distance(b, plan.Lifts[l].Bottom.xz) <= LinkMeters)
                        m_Edges.Add(new PlanEdge { From = bottom, To = LiftBottomNode(pads, runs, l), Kind = EdgeLink, Band = SlopeBand.Flat, Run = -1 });
            }
            m_ServeChords.Clear();
            for (int q = 0; q + 1 < m_LiftChords.Count; q += 2)
                m_ServeChords.Add(new LiftServedRule.Chord { A = m_LiftChords[q], B = m_LiftChords[q + 1] });
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
                if (!Finite3(lift.Top) || l >= m_LiftChordFirst.Count)
                    continue;
                LiftServedRule.Lift serve = ServeOf(lift, l);
                for (int r = 0; r < runs; r++)
                    if (LiftServedRule.Reaches(m_RunTop3[r], serve, m_ServeChords, out float _))
                        m_Edges.Add(new PlanEdge { From = lt, To = RunTopNode(pads, r), Kind = EdgeLink, Band = SlopeBand.Flat, Run = -1 });
            }
            CatTrackEdges(plan, res);
            plan.Graph = m_Edges.ToArray();
        }

        /// <summary>The served rule's view of plan lift l (Lifts.cs Serves builds the same row): complete, power unknown (= powered),
        /// its chord slice, the type's rule, radius and top margin.</summary>
        private LiftServedRule.Lift ServeOf(in PlanLift l, int i)
        {
            LiftTypeDef def = l.TypeIndex < SkiCatalog.Lifts.Count ? SkiCatalog.Lifts[l.TypeIndex] : SkiCatalog.Lifts[0];
            return new LiftServedRule.Lift
            {
                Complete = true, Power = LiftPower.Unknown, TopY = l.Top.y, FirstChord = m_LiftChordFirst[i] / 2,
                ChordCount = m_LiftChordCount[i], Rule = (byte)def.ServeRule, RadiusM = def.ServeRadiusM, TopMarginM = def.TopMarginM,
                TopXZ = l.Top.xz,
            };
        }

        /// <summary>A run bottom with no run link: a merge line whose junction BuildRing dropped (its ring ended over JunctionReachM
        /// from the parent), or a fan line the near gap stopped beside another run (FanMergeStopM from its outline, no Merge flag).
        /// Link it to the nearest run whose outline lies within MergeLinkReachM of the bottom, when the two outlines pass the game's
        /// own link rule (LiftServedRule.OutlinesLinked: a node pair within LinkMeters, crossing edges, or one inside the other), so
        /// the graph says "linked" only where the served rule does too.</summary>
        private void NearRunLink(AutoPlanResult res, int a, int bottom, int pads)
        {
            AutoRun run = res.Runs[a];
            float2 b = Bottom(run);
            const float reach = MergeLinkReachM;
            int best = -1;
            float bestD = float.PositiveInfinity;
            for (int o = 0; o < res.Runs.Count; o++)
            {
                if (o == a || math.any(b < m_RunLo[o] - reach) || math.any(b > m_RunHi[o] + reach))
                    continue;
                float d = PointRingDistance(res.Runs[o].Ring, b);
                if (d <= reach && d < bestD && LiftServedRule.OutlinesLinked(run.Ring, res.Runs[o].Ring))
                {
                    bestD = d;
                    best = o;
                }
            }
            if (best >= 0)
                m_Edges.Add(new PlanEdge { From = bottom, To = RunBottomNode(pads, best), Kind = EdgeLink, Band = res.Runs[best].Band, Run = best });
        }

        /// <summary>The way-out nodes (DECISIONS 451 c, AR-2): every lift bottom node with a finite station, every pad holding a lift
        /// bottom, and every base pad with access (PadIsWayOut). Shared by WaysHome and CountDeadEnds.</summary>
        private void HomeTargets(ResortPlan plan, AutoPlanResult res)
        {
            int pads = plan.Pads.Length, runs = res.Runs.Count, lifts = plan.Lifts.Length, n = pads + 2 * runs + 2 * lifts;
            Grow(ref m_HomeTarget, n);
            Array.Clear(m_HomeTarget, 0, n);
            for (int k = 0; k < pads; k++)
                m_HomeTarget[k] = PadIsWayOut(plan.Pads[k]);
            for (int l = 0; l < lifts; l++)
            {
                if (!Finite3(plan.Lifts[l].Bottom))
                    continue;
                m_HomeTarget[LiftBottomNode(pads, runs, l)] = true;
                if (plan.Lifts[l].BottomPad >= 0 && plan.Lifts[l].BottomPad < pads)
                    m_HomeTarget[plan.Lifts[l].BottomPad] = true;
            }
        }

        /// <summary>Dead ends: run bottoms with no downhill path (every edge kind but lifts) to a way out, unless the bottom lies on an
        /// existing run or cat track (the player's network beyond the plan). One reverse breadth-first pass from the way-out nodes.</summary>
        private void CountDeadEnds(ResortPlan plan, AutoPlanResult res)
        {
            int pads = plan.Pads.Length, runs = res.Runs.Count, n = pads + 2 * runs + 2 * plan.Lifts.Length;
            PlanEdge[] g = plan.Graph;
            Grow(ref m_RevFirst, n + 1);
            Grow(ref m_RevAt, n);
            Grow(ref m_RevAdj, g.Length);
            Grow(ref m_WayOut, n);
            Grow(ref m_FlowQueue, n);
            Array.Clear(m_RevFirst, 0, n + 1);
            Array.Clear(m_RevAt, 0, n);
            for (int e = 0; e < g.Length; e++)
                if (g[e].Kind != EdgeLift)
                    m_RevFirst[g[e].To + 1]++;
            for (int v = 0; v < n; v++)
                m_RevFirst[v + 1] += m_RevFirst[v];
            for (int e = 0; e < g.Length; e++)
                if (g[e].Kind != EdgeLift)
                    m_RevAdj[m_RevFirst[g[e].To] + m_RevAt[g[e].To]++] = g[e].From;
            int head = 0, tail = 0;
            for (int v = 0; v < n; v++)
            {
                m_WayOut[v] = m_HomeTarget[v];
                if (m_WayOut[v])
                    m_FlowQueue[tail++] = v;
            }
            while (head < tail)
            {
                int v = m_FlowQueue[head++];
                for (int k = m_RevFirst[v]; k < m_RevFirst[v + 1]; k++)
                {
                    int u = m_RevAdj[k];
                    if (m_WayOut[u])
                        continue;
                    m_WayOut[u] = true;
                    m_FlowQueue[tail++] = u;
                }
            }
            int dead = 0;
            for (int a = 0; a < runs; a++)
            {
                float2 b = Bottom(res.Runs[a]);
                if (!m_WayOut[RunBottomNode(pads, a)] && !OnExistingRun(b) && !OnCatTrack(b))
                    dead++;
            }
            plan.DeadEnds = dead;
        }

        /// <summary>Existing cat tracks (obstacles) touching two or more nodes join them: Green, both directions (spec 4.6). A point node
        /// touches when it lies inside the outline or within LinkMeters of it; a pad when a cell of it lies within LinkMeters of a ring vertex.</summary>
        private void CatTrackEdges(ResortPlan plan, AutoPlanResult res)
        {
            List<AutoObstacle> obs = m_In.Obstacles;
            if (obs == null)
                return;
            int pads = plan.Pads.Length, runs = res.Runs.Count, lifts = plan.Lifts.Length;
            List<int> touch = m_CatTouch;
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

        /// <summary>Per lift: Wanted (the knob; Auto = Green when BottomPad touches the v1 base band, else Blue: information and the
        /// color, never the verdict), Home (the easiest downhill path's hardest band, Green .. DoubleBlack; Download when no downhill path
        /// reaches a way out; None for a pinned lift with neither station inside the region box), HomeRun (the first run of the easiest
        /// path, any band; -1 none).</summary>
        private void WaysHome(ResortPlan plan, AutoPlanResult res)
        {
            int pads = plan.Pads.Length, runs = res.Runs.Count, lifts = plan.Lifts.Length;
            int n = pads + 2 * runs + 2 * lifts;
            PlanEdge[] g = plan.Graph;
            Grow(ref m_EdgeM, g.Length);
            for (int e = 0; e < g.Length; e++)
                m_EdgeM[e] = EdgeLength(g[e], plan, res);
            m_Descent.Prepare(n, g, m_EdgeM);
            for (int l = 0; l < lifts; l++)
            {
                ref PlanLift lift = ref plan.Lifts[l];
                bool baseBottom = lift.BottomPad >= 0 && lift.BottomPad < pads && plan.Pads[lift.BottomPad].TouchesBase;
                HomeLevel wanted = m_Knobs.Home == HomeLevel.Auto ? (baseBottom ? HomeLevel.Green : HomeLevel.Blue) : m_Knobs.Home;
                lift.Wanted = wanted;
                lift.HomeRun = -1;
                if (lift.Origin == LiftOrigin.Pinned && !(Finite3(lift.Top) && InRegionBox(lift.Top.xz, out _)) &&
                    !(Finite3(lift.Bottom) && InRegionBox(lift.Bottom.xz, out _)))
                {
                    lift.Home = HomeVerdict.None;
                    continue;
                }
                int easiest = m_Descent.Run(LiftTopNode(pads, runs, l), m_HomeTarget, pads, out lift.HomeRun);
                lift.Home = easiest >= 0 ? (HomeVerdict)Math.Max(easiest, (int)SlopeBand.Green) : HomeVerdict.Download;
            }
        }

        /// <summary>True when a verdict meets its Wanted level: Green, or Blue under a Blue wish (Black, Double Black and Download do not).</summary>
        public static bool MeetsWanted(HomeVerdict home, HomeLevel wanted) =>
            home == HomeVerdict.Green || (home == HomeVerdict.Blue && wanted == HomeLevel.Blue);

        /// <summary>The easiest descent on a plan graph: Dijkstra over (hardest band so far, meters) from src along every edge but lifts
        /// (Kind 1), ties by node index, until the first target node is settled. Link edges from src onto a pad (node below srcPadLimit)
        /// are skipped: getting off a lift onto its own top pad is no way home (a cat track from the top still is). Returns the hardest band (SlopeBand value) of that path, -1 = no
        /// path; firstRun = the run of the path's first edge that carries one (-1 none). One-shot form (tests); FinishResort reuses a
        /// DescentScratch over all lifts.</summary>
        public static int EasiestDescent(int nodes, PlanEdge[] g, double[] meters, int src, bool[] target, int srcPadLimit, out int firstRun)
        {
            var s = new DescentScratch();
            s.Prepare(nodes, g, meters);
            return s.Run(src, target, srcPadLimit, out firstRun);
        }

        /// <summary>EasiestDescent's state, Grow'd and reused (AR-17): the adjacency of every edge but lifts (built once per graph by
        /// Prepare) and a binary heap keyed (band, meters, node), the linear scan's order exactly, so the settled sequence and the
        /// predecessors match it (AR-7: O(E log E) instead of O(V^2) per lift).</summary>
        public sealed class DescentScratch
        {
            private int[] m_First = new int[0], m_Adj = new int[0], m_At = new int[0], m_Band = new int[0], m_Pred = new int[0];
            private int[] m_HBand = new int[0], m_HNode = new int[0];
            private double[] m_Len = new double[0], m_HLen = new double[0];
            private bool[] m_Done = new bool[0];
            private PlanEdge[] m_G;
            private double[] m_Meters;
            private int m_Nodes, m_HN;

            public void Prepare(int nodes, PlanEdge[] g, double[] meters)
            {
                m_Nodes = nodes;
                m_G = g;
                m_Meters = meters;
                Grow(ref m_First, nodes + 1);
                Grow(ref m_At, nodes);
                Array.Clear(m_First, 0, nodes + 1);
                Array.Clear(m_At, 0, nodes);
                for (int e = 0; e < g.Length; e++)
                    if (g[e].Kind != EdgeLift)
                        m_First[g[e].From + 1]++;
                for (int v = 0; v < nodes; v++)
                    m_First[v + 1] += m_First[v];
                Grow(ref m_Adj, m_First[nodes]);
                for (int e = 0; e < g.Length; e++)
                    if (g[e].Kind != EdgeLift)
                        m_Adj[m_First[g[e].From] + m_At[g[e].From]++] = e;
                Grow(ref m_Band, nodes);
                Grow(ref m_Len, nodes);
                Grow(ref m_Pred, nodes);
                Grow(ref m_Done, nodes);
                int cap = m_First[nodes] + 1;   // one push per relaxation, plus the source
                Grow(ref m_HBand, cap);
                Grow(ref m_HLen, cap);
                Grow(ref m_HNode, cap);
            }

            public int Run(int src, bool[] target, int srcPadLimit, out int firstRun)
            {
                firstRun = -1;
                int nodes = m_Nodes;
                PlanEdge[] g = m_G;
                for (int v = 0; v < nodes; v++)
                {
                    m_Band[v] = int.MaxValue;
                    m_Len[v] = double.PositiveInfinity;
                    m_Pred[v] = -1;
                    m_Done[v] = false;
                }
                m_Band[src] = 0;
                m_Len[src] = 0.0;
                m_HN = 0;
                Push(0, 0.0, src);
                while (m_HN > 0)
                {
                    int u = Pop();
                    if (m_Done[u])
                        continue;   // a stale entry: the node settled under a smaller key
                    m_Done[u] = true;
                    if (u != src && target[u])
                    {
                        for (int v = u; m_Pred[v] >= 0; v = g[m_Pred[v]].From)
                            if (g[m_Pred[v]].Run >= 0)
                                firstRun = g[m_Pred[v]].Run;   // the last one written is the first run below src
                        return m_Band[u];
                    }
                    for (int k = m_First[u]; k < m_First[u + 1]; k++)
                    {
                        int e = m_Adj[k], w = g[e].To;
                        if (m_Done[w] || (u == src && w < srcPadLimit && g[e].Kind == EdgeLink))
                            continue;
                        int b = Math.Max(m_Band[u], (int)g[e].Band);
                        double d = m_Len[u] + m_Meters[e];
                        if (b < m_Band[w] || (b == m_Band[w] && d < m_Len[w]))
                        {
                            m_Band[w] = b;
                            m_Len[w] = d;
                            m_Pred[w] = e;
                            Push(b, d, w);
                        }
                    }
                }
                return -1;
            }

            private bool Less(int i, int j) =>
                m_HBand[i] != m_HBand[j] ? m_HBand[i] < m_HBand[j] : m_HLen[i] != m_HLen[j] ? m_HLen[i] < m_HLen[j] : m_HNode[i] < m_HNode[j];

            private void Swap(int i, int j)
            {
                int b = m_HBand[i], v = m_HNode[i];
                double d = m_HLen[i];
                m_HBand[i] = m_HBand[j];
                m_HLen[i] = m_HLen[j];
                m_HNode[i] = m_HNode[j];
                m_HBand[j] = b;
                m_HLen[j] = d;
                m_HNode[j] = v;
            }

            private void Push(int band, double len, int node)
            {
                int i = m_HN++;
                m_HBand[i] = band;
                m_HLen[i] = len;
                m_HNode[i] = node;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (!Less(i, p))
                        break;
                    Swap(i, p);
                    i = p;
                }
            }

            private int Pop()
            {
                int top = m_HNode[0];
                m_HN--;
                if (m_HN > 0)
                {
                    m_HBand[0] = m_HBand[m_HN];
                    m_HLen[0] = m_HLen[m_HN];
                    m_HNode[0] = m_HNode[m_HN];
                    for (int i = 0; ;)
                    {
                        int l = 2 * i + 1, r = l + 1, m = i;
                        if (l < m_HN && Less(l, m))
                            m = l;
                        if (r < m_HN && Less(r, m))
                            m = r;
                        if (m == i)
                            break;
                        Swap(i, m);
                        i = m;
                    }
                }
                return top;
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
            List<float2> pts = m_HullPts;
            pts.Clear();
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
        /// percent, so the line the player sees is v1's), plus the served pods and those whose verdict meets their Wanted (MeetsWanted),
        /// the largest bench and base pad.</summary>
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
                if (plan.Pads[k].TouchesBase)
                    s.BaseBenchHa = Math.Max(s.BaseBenchHa, ha);
            }
            for (int k = 0; k < plan.Pods.Length; k++)
            {
                int l = plan.Pods[k].Lift;
                if (l < 0 || l >= plan.Lifts.Length)
                    continue;
                s.PodsServed++;
                if (MeetsWanted(plan.Lifts[l].Home, plan.Lifts[l].Wanted))
                    s.PodsWithGreenHome++;   // AR-13: the pods whose way home meets their Wanted (the field name is kept)
            }
            plan.Site = s;
        }

        /// <summary>Resort mode: Clusters carries one cluster per pod (Center = TopCenter, Pod = true, PodBand = the pod class's rated band;
        /// Runs / Linked / HasGreen over the pod's runs) and each run's Cluster is its pod (-1 = none), so the v1 readers (cluster rows,
        /// hover line) keep working on pods.</summary>
        private void PodClusters(ResortPlan plan, AutoPlanResult res)
        {
            int n = res.Runs.Count;
            Grow(ref m_ClParent, n);
            Grow(ref m_ClSize, Math.Max(1, n));
            int[] parent = m_ClParent, size = m_ClSize;
            Array.Clear(size, 0, Math.Max(1, n));
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
                h = Mix(h, (d.IsBase ? 1u : 0u) | (d.IsBench ? 2u : 0u) | (d.TouchesBase ? 4u : 0u));
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
            h = Mix(h, (uint)p.Site.PodsServed);
            return h == 0UL ? 1UL : h;
        }
    }
}
