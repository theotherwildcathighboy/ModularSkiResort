// Runs-first realizer: the result (spec section 8). Groups replace pods one to one with lifts (plus one "other runs" group), two pads per
// lift, one cluster per group. The graph has every area and every lift: lift top to area by the game's served rule, area to area by the
// game's link graph (LiftServedRule.RunLinks), area to lift bottom by the planner's delivery rule, area to an aerial top by its
// ride-down rule. From it: served (the game's rule, LiftServedRule.AssignLinked), each lift's way home (EasiestDescent, copied from
// SkiRunAutoPlanner.Flow), the bottom reach (B-1), dead ends, the mix, the site line, the warnings, and the relation gate. Pure.
// Spec: docs/research/quickruns-v2/port/REALIZER-SPEC.md section 8.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class RunsFirstRealizer
    {
        public const byte EdgeRun = 0, EdgeLift = 1, EdgeCatTrack = 2, EdgeLink = 3;   // PlanEdge.Kind (as v2)
        public const float HullGrowM = 50f;

        private readonly List<Area> m_Out = new List<Area>();   // AutoPlanResult.Runs order: pistes, then the new tracks

        private void Assemble()
        {
            AutoPlanResult res = m_Res;
            foreach (Area a in m_P)
            {
                a.Out = res.Runs.Count;
                m_Out.Add(a);
                res.Runs.Add(ToRun(a, IdOf(a.Graded ? (byte)2 : (byte)1, m_E[a.Elem].Source, a.Segment, a.Line)));
            }
            foreach (Area a in m_T)
            {
                if (a.Existing)
                    continue;
                a.Out = res.Runs.Count;
                m_Out.Add(a);
                res.Runs.Add(ToRun(a, IdOf(3, m_E[a.Elem].Source, a.Segment, a.Line)));
            }
            AssignKinds();
            foreach (Area a in m_P)
            {
                int b = (int)(a.Band == SlopeBand.Flat ? SlopeBand.Green : a.Band);
                res.BandCount[b]++;
                if (a.CrossesTrack)
                    res.Warnings |= AutoPlanWarning.CrossesCatTrack;
            }
            res.Rated = m_P.Count;
            res.Status = m_P.Count > 0 ? AutoPlanStatus.Ok : AutoPlanStatus.NoRuns;
            var plan = new ResortPlan { Lifts = m_Lifts, TreeLineY = m_In.TreeLineY };
            res.Resort = plan;
            Groups(plan);
            Graph(plan);
            MixAndHull(plan);
            plan.Site = new SiteVerdict
            {
                Kind = (byte)QuickRunsRules.SiteTierOf(QuickRunsRules.SitePercent(res.MeanGrade)), MeanGrade = res.MeanGrade,
                SteepShare = res.SteepShare, Pods = 0, PodsServed = 0, PodsWithGreenHome = 0,
            };
            // Warnings (8.3)
            if (m_Lifts.Length == 0)
                res.Warnings |= AutoPlanWarning.NoLiftSite;
            foreach (PlanLift l in m_Lifts)
                if (l.Home == HomeVerdict.Download)
                    res.Warnings |= AutoPlanWarning.Download;
            if (plan.DeadEnds > 0)
                res.Warnings |= AutoPlanWarning.DeadEnd;
            if (res.Runs.Count < 2)
                res.Warnings |= AutoPlanWarning.FewRuns;
            if (res.BandCount[1] == 0)
                res.Warnings |= AutoPlanWarning.NoGreen;
            if (res.BandCount[2] == 0)
                res.Warnings |= AutoPlanWarning.NoBlue;
            if (res.BandCount[3] == 0)
                res.Warnings |= AutoPlanWarning.NoBlack;
            // NoWayToBottom (spec 8.3, plan 5.4, R2-y): the realized plan decides. A lift with a way down (skiing from its top on the
            // realized plan, or skiing in the plan) whose skiing does not end in the base band sets the warning. The planner's named block
            // stays in the report.
            m_Rep.LiftPlanned = new RunsFirstBottom[m_Lifts.Length];
            for (int l = 0; l < m_Lifts.Length && l < m_Plan.Lifts.Count; l++)
            {
                RunsFirstResult.Lift lf = m_Plan.Lifts[l];
                RunsFirstBottom pb = lf.ToBottom;
                m_Rep.LiftPlanned[l] = pb;
                bool way = m_Rep.LiftSkis[l] || pb != RunsFirstBottom.NoSkiing, real = m_Rep.LiftBottom[l];
                if (!way)
                    continue;
                m_Rep.BottomWayDown++;
                if (pb == RunsFirstBottom.Reaches)
                    m_Rep.BottomPlanner++;
                if (real)
                    m_Rep.BottomRealized++;
                if (pb == RunsFirstBottom.Reaches && !real)
                    m_Rep.BottomLost++;
                if (pb != RunsFirstBottom.Reaches && real)
                    m_Rep.BottomGained++;
                if (!real)
                {
                    res.Warnings |= AutoPlanWarning.NoWayToBottom;
                    m_Rep.NoWayToBottom = true;
                    m_Rep.Lines.Add($"lift L{lf.Id}: no way to the bottom on the realized plan (the planner: {pb})");
                }
                else if (pb != RunsFirstBottom.Reaches)
                    m_Rep.Lines.Add($"lift L{lf.Id}: reaches the bottom on the realized plan (the planner: {pb})");
            }
            RelationGate();
            BandKeepReport();
            plan.Fingerprint = Hash(res);
        }

        // ---------------------------------------------------------------- groups, pads, clusters (8.1)

        /// <summary>The group of an area: its source's lift, or the last group (links, ways home, no lift).</summary>
        private int GroupOf(Area a)
        {
            Elem e = m_E[a.Elem];
            return !e.Link && e.Lift >= 0 && e.Lift < m_Lifts.Length ? e.Lift : m_Lifts.Length;
        }

        private static TerrainBand ClassOf(SlopeBand b) =>
            b <= SlopeBand.Green ? TerrainBand.Beginner : b == SlopeBand.Blue ? TerrainBand.Intermediate : b == SlopeBand.Black ? TerrainBand.Advanced : TerrainBand.Expert;

        private void Groups(ResortPlan plan)
        {
            AutoPlanResult res = m_Res;
            int L = m_Lifts.Length, n = res.Runs.Count;
            var groupOf = new int[n];
            bool other = false;
            for (int i = 0; i < n; i++)
            {
                groupOf[i] = GroupOf(m_Out[i]);
                other |= groupOf[i] == L;
            }
            int groups = L + (other ? 1 : 0);
            plan.RunPod = new int[n];
            plan.RunLift = new int[n];
            var pods = new PlanPod[groups];
            var podRuns = new List<int>(n);
            for (int g = 0; g < groups; g++)
            {
                int first = podRuns.Count;
                var km = new double[5];
                float2 sum = 0f;
                int cnt = 0;
                float topY = float.NegativeInfinity, botY = float.PositiveInfinity;
                for (int i = 0; i < n; i++)
                {
                    if (groupOf[i] != g)
                        continue;
                    podRuns.Add(i);
                    plan.RunPod[i] = g;
                    plan.RunLift[i] = g < L ? g : -1;
                    res.Runs[i].Cluster = g;
                    Area a = m_Out[i];
                    if (!a.Track)
                        km[(int)a.Band] += a.LengthM;
                    sum += 0.5f * (a.Lo + a.Hi);
                    cnt++;
                    topY = Math.Max(topY, a.TopY);
                    botY = Math.Min(botY, a.BottomY);
                }
                int best = 1;
                for (int b = 0; b < 5; b++)
                    if (km[b] > km[best])
                        best = b;
                var p = new PlanPod
                {
                    Id = g, LowerPad = g < L ? 2 * g : -1, UpperPad = g < L ? 2 * g + 1 : -1, PrimaryClass = ClassOf((SlopeBand)best),
                    BandShare = new float[7], Lift = g < L ? g : -1, RunFirst = first, RunCount = podRuns.Count - first,
                };
                if (g < L)
                {
                    p.Key = m_Lifts[g].Key;
                    p.TopCenter = m_Lifts[g].Top.xz;
                    p.TopY = m_Lifts[g].Top.y;
                    p.BottomY = m_Lifts[g].Bottom.y;
                }
                else
                {
                    p.Key = OtherGroupKey;
                    p.TopCenter = cnt > 0 ? sum / cnt : 0f;
                    p.TopY = cnt > 0 ? topY : 0f;
                    p.BottomY = cnt > 0 ? botY : 0f;
                }
                p.VerticalM = p.TopY - p.BottomY;
                pods[g] = p;
                bool green = false;
                for (int i = p.RunFirst; i < p.RunFirst + p.RunCount; i++)
                    green |= !m_Out[podRuns[i]].Track && m_Out[podRuns[i]].Band <= SlopeBand.Green;
                res.Clusters.Add(new AutoCluster { Center = p.TopCenter, TopY = p.TopY, Runs = p.RunCount, Linked = p.RunCount, HasGreen = green });
            }
            plan.Pods = pods;
            plan.PodRuns = podRuns.ToArray();
            // Two pads per lift (bottom at 2 x index, top at 2 x index + 1): the lot disc
            var pads = new PlanPad[2 * L];
            for (int l = 0; l < L; l++)
                for (int s = 0; s < 2; s++)
                {
                    float3 at = s == 0 ? m_Lifts[l].Bottom : m_Lifts[l].Top;
                    float r = PlanGeometry.StationHalfDiagonalOf(m_Lifts[l].TypeIndex);
                    float grade = Cell(at.xz, out int c) && !float.IsNaN(m_Grade[c]) ? m_Grade[c] : 0f;
                    bool baseTouch = s == 0 && at.y <= m_BottomTop;
                    pads[2 * l + s] = new PlanPad
                    {
                        Id = 2 * l + s, Kind = grade < 0.08f ? PadKind.Flat : grade < 0.15f ? PadKind.Learning : PadKind.Novice, AreaM2 = (float)(Math.PI * r * r),
                        MeanGrade = grade, MinY = at.y, MaxY = at.y, Center = at.xz, InscribedM = r,
                        IsBase = s == 0 && m_Plan.Lifts[l].Base, TouchesBase = baseTouch, IsBench = false, AccessM = float.PositiveInfinity,
                    };
                }
            plan.Pads = pads;
        }

        // ---------------------------------------------------------------- the graph, served, home, bottom reach, dead ends (8.2, 8.3)

        private void Graph(ResortPlan plan)
        {
            AutoPlanResult res = m_Res;
            int L = m_Lifts.Length, A = res.Runs.Count;
            // The game's link graph on the realized outlines
            var outlines = new List<float2[]>(A);
            var heights = new List<float[]>(A);
            var areaIds = new List<int>(A);
            var tracks = new List<bool>(A);
            var tops = new List<float3>(A);
            for (int i = 0; i < A; i++)
            {
                Area a = m_Out[i];
                outlines.Add(a.Ring);
                var y = new float[a.Nodes.Length];
                float3 top = a.Nodes[0];
                for (int k = 0; k < y.Length; k++)
                {
                    y[k] = a.Nodes[k].y;
                    if (a.Nodes[k].y > top.y)
                        top = a.Nodes[k];
                }
                heights.Add(y);
                areaIds.Add(0);
                tracks.Add(a.Track);
                tops.Add(top);
            }
            var links = new LiftServedRule.RunLinks();
            links.Build(outlines, heights, areaIds);
            var lifts = new List<LiftServedRule.Lift>(L);
            var chords = new List<LiftServedRule.Chord>(L);
            for (int l = 0; l < L; l++)
            {
                LiftTypeDef def = SkiCatalog.Lifts[m_Lifts[l].TypeIndex];
                chords.Add(new LiftServedRule.Chord { A = m_Lifts[l].Bottom.xz, B = m_Lifts[l].Top.xz });
                lifts.Add(new LiftServedRule.Lift
                {
                    Complete = true, Power = LiftPower.Powered, TopY = m_Lifts[l].Top.y, FirstChord = l, ChordCount = 1, Rule = (byte)def.ServeRule,
                    RadiusM = def.ServeRadiusM, TopMarginM = def.TopMarginM, TopXZ = m_Lifts[l].Top.xz,
                });
            }
            var serving = new List<int>();
            var servingD = new List<float>();
            var servedCount = new List<int>();
            var root = new List<int>();
            var via = new List<int>();
            LiftServedRule.AssignLinked(tops, links, lifts, chords, serving, servingD, servedCount, root, via, tracks);
            for (int i = 0; i < A; i++)
            {
                if (root[i] >= 0)
                    continue;
                res.Runs[i].Unserved = true;
                m_Rep.UnservedAreas++;
                m_Rep.Lines.Add("unserved: " + LostRelation(m_Out[i], tops[i], root));
            }
            // Per lift: Serves (Reaches on each area's highest node), StartsNearTop, EndsAtBottom
            for (int l = 0; l < L; l++)
            {
                int serves = 0, near = 0, ends = 0;
                for (int i = 0; i < A; i++)
                {
                    if (LiftServedRule.Reaches(tops[i], lifts[l], chords, out float _))
                        serves++;
                    Area a = m_Out[i];
                    if (math.distance(a.Line[0], m_Lifts[l].Top.xz) <= NearTopM)
                        near++;
                    if (math.distance(a.Line[a.Line.Length - 1], m_Lifts[l].Bottom.xz) <= NearTopM)
                        ends++;
                }
                m_Lifts[l].Serves = serves;
                m_Lifts[l].StartsNearTop = near;
                m_Lifts[l].EndsAtBottom = ends;
            }
            // The descent graph. Nodes: lift tops 0..L-1, lift bottoms L..2L-1, area entries 2L + a, area exits 2L + A + a, aerial
            // ride-down nodes 2L + 2A + l
            int nodes = 2 * L + 2 * A + L;
            var edges = new List<PlanEdge>();
            var meters = new List<double>();
            void Edge(int from, int to, byte kind, SlopeBand band, int run, double m)
            {
                edges.Add(new PlanEdge { From = from, To = to, Kind = kind, Band = band, Run = run });
                meters.Add(m);
            }
            m_Rep.LiftSkis = new bool[L];
            m_Rep.LiftBottom = new bool[L];
            for (int l = 0; l < L; l++)
            {
                Edge(L + l, l, EdgeLift, SlopeBand.Flat, -1, m_Lifts[l].LengthM);
                for (int i = 0; i < A; i++)
                    if (LiftServedRule.Reaches(tops[i], lifts[l], chords, out float _))
                    {
                        Edge(l, 2 * L + i, EdgeLink, SlopeBand.Flat, -1, 1.0);
                        m_Rep.LiftSkis[l] = true;
                    }
            }
            var hasOut = new bool[A];
            for (int i = 0; i < A; i++)
            {
                Area a = m_Out[i];
                Edge(2 * L + i, 2 * L + A + i, a.Track ? EdgeCatTrack : EdgeRun, res.Runs[i].Band, i, Math.Max(1.0, a.LengthM));
                links.NeighboursOf(i, out int first, out int count);
                for (int k = 0; k < count; k++)
                {
                    int slot = first + k, m = links.Neighbour(slot);
                    float2 range = links.YRange(m);
                    links.ContactsOf(slot, out int c0, out int cn);
                    bool ok = false;
                    for (int c = c0; c < c0 + cn && !ok; c++)
                    {
                        LiftServedRule.Contact ct = links.ContactAt(c);
                        ok = m_Out[m].Track ? ct.ToY <= ct.FromY + LiftServedRule.ClimbToleranceM
                            : LiftServedRule.BelowJunction(Math.Min(ct.FromY, ct.ToY), range.x, range.y);
                    }
                    if (ok)
                    {
                        Edge(2 * L + A + i, 2 * L + m, EdgeLink, SlopeBand.Flat, -1, 1.0);
                        hasOut[i] = true;
                    }
                }
                for (int l = 0; l < L; l++)
                {
                    float3 bot = m_Lifts[l].Bottom;
                    bool delivers = false;
                    for (int k = 1; k < a.Line.Length && !delivers; k++)
                        delivers = math.distancesq(a.Line[k], bot.xz) < DeliverM * DeliverM && H(a.Line[k]) >= bot.y - DeliverDropM;
                    if (delivers)
                    {
                        Edge(2 * L + A + i, L + l, EdgeLink, SlopeBand.Flat, -1, 1.0);
                        hasOut[i] = true;
                    }
                    else if (Aerial(m_Lifts[l].TypeIndex) && math.distancesq(a.Line[a.Line.Length - 1], m_Lifts[l].Top.xz) < RideDownM * RideDownM)
                    {
                        Edge(2 * L + A + i, 2 * L + 2 * A + l, EdgeLink, SlopeBand.Flat, -1, 1.0);
                        hasOut[i] = true;
                    }
                }
            }
            plan.Graph = edges.ToArray();
            var descent = new DescentScratch();
            descent.Prepare(nodes, plan.Graph, meters.ToArray());
            // Home: the easiest way from the top to a base lift's bottom or an aerial top
            var home = new bool[nodes];
            for (int l = 0; l < L; l++)
            {
                if (m_Plan.Lifts[l].Base)
                    home[L + l] = true;
                if (Aerial(m_Lifts[l].TypeIndex))
                    home[2 * L + 2 * A + l] = true;
            }
            var bottom = new bool[nodes];
            for (int i = 0; i < A; i++)
                bottom[2 * L + A + i] = m_Out[i].EndY <= m_BottomTop;
            for (int l = 0; l < L; l++)
            {
                int easiest = descent.Run(l, home, 0, out int firstRun);
                m_Lifts[l].Home = easiest >= 0 ? (HomeVerdict)Math.Max(easiest, (int)SlopeBand.Green) : HomeVerdict.Download;
                m_Lifts[l].HomeRun = firstRun;
                m_Lifts[l].Wanted = HomeLevel.Auto;
                int b1 = descent.Run(l, bottom, 0, out int _);
                if (b1 >= 0)
                    m_Rep.BottomReach++;
                m_Rep.LiftBottom[l] = b1 >= 0;
                m_Rep.Lines.Add($"lift L{l + 1} ({m_Plan.Lifts[l].Type}): home {m_Lifts[l].Home}, bottom band {(b1 >= 0 ? ((SlopeBand)Math.Max(b1, 1)).ToString() : "not reached")}, serves {m_Lifts[l].Serves}, towers {(m_Lifts[l].TowersUnchecked ? "not clear" : "clear")}");
            }
            // Dead ends: no way on (another area, a lift bottom, an aerial top) and not in the bottom band
            for (int i = 0; i < A; i++)
            {
                Area a = m_Out[i];
                if (hasOut[i] || a.EndY <= m_BottomTop)
                    continue;
                plan.DeadEnds++;
                float2 p = a.Line[a.Line.Length - 1];
                m_Rep.Lines.Add($"dead end: {(a.Track ? "track" : "piste")} {m_E[a.Elem].Source} ({m_E[a.Elem].Kind}) segment {a.Segment} ends at ({p.x:F0}, {p.y:F0}) {a.EndY - m_BottomTop:F0} m over the bottom band");
            }
            m_Rep.DeadEnds = plan.DeadEnds;
            BottomCauses(L, A, edges, bottom);
        }

        private static bool Aerial(byte type) => type == 2 || type == 3;

        /// <summary>The planner relation an unserved area lost: its nearest lift top (distance, height over it), and the planner contacts
        /// into its source with their state (R2-r: the outline gap of a kept contact that is not a link, whether a linked contact is
        /// served itself, and for a later segment whether the segment before it is a link).</summary>
        private string LostRelation(Area a, float3 top, List<int> root)
        {
            Elem e = m_E[a.Elem];
            float best = float.PositiveInfinity, dy = 0f;
            int bl = -1;
            for (int l = 0; l < m_Lifts.Length; l++)
            {
                float d = LiftServedRule.DistanceSqXZ(top.xz, m_Lifts[l].Bottom.xz, m_Lifts[l].Top.xz);
                if (d < best)
                {
                    best = d;
                    bl = l;
                    dy = top.y - m_Lifts[l].Top.y;
                }
            }
            var sb = new System.Text.StringBuilder();
            sb.Append($"{(a.Track ? "track" : "piste")} {e.Source} ({e.Kind}) segment {a.Segment} top ({top.x:F0}, {top.z:F0})");
            if (bl >= 0)
                sb.Append($", nearest lift line L{bl + 1} {Math.Sqrt(best):F0} m, top {dy:+0;-0} m over its top");
            int shown = 0;
            foreach (Elem b in m_E)
            {
                if (b == e || !(EndsOnto(b, e) || StartsOff(e, b)) || shown >= 3)
                    continue;
                bool kept = b.Areas.Count > 0, linked = false, served = false;
                foreach (int k in b.Areas)
                {
                    Area o = b.Type == ElemType.Track ? m_T[k] : m_P[k];
                    if (LiftServedRule.OutlinesLinked(a.Ring, o.Ring))
                    {
                        linked = true;
                        served |= o.Out >= 0 && root[o.Out] >= 0;
                    }
                }
                string link = linked ? served ? "linked" : "linked, unserved itself" : $"not linked ({Gap(a, b):F0} m apart)";
                string state = b.Merged ? $"merged into {HostNames(b)}, {link}" : kept ? linked ? link : "kept, " + link : "dropped";
                sb.Append($"; planner contact from {(b.Type == ElemType.Track ? "track" : "piste")} {b.Source} ({b.Kind}): {state}");
                shown++;
            }
            if (shown == 0)
                sb.Append("; no planner contact into it (the planner served it from a lift top within 150 m)");
            if (a.Segment > 0 && a.Segment < SpurSegment)
            {
                // a later segment is entered from the segment before it (the same source)
                Area prev = null;
                foreach (int k in e.Areas)
                {
                    Area o = e.Type == ElemType.Track ? m_T[k] : m_P[k];
                    if (o.Segment == a.Segment - 1)
                        prev = o;
                }
                sb.Append(prev == null ? "; the segment before it is lost" : LiftServedRule.OutlinesLinked(a.Ring, prev.Ring)
                    ? $"; linked to the segment before it{(prev.Out >= 0 && root[prev.Out] >= 0 ? "" : ", unserved itself")}" : $"; not linked to the segment before it ({PlanGeometry.RingDistance(a.Ring, prev.Ring, 500f, out _, out _, out _, out _):F0} m apart)");
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- mix, density (as v2)

        /// <summary>MountainMix = piste-km share per rated band (Flat counted with Green), RunKm, HullKm2 = the convex hull of the piste
        /// rings and lift stations grown by HullGrowM, RunKmPerKm2 (copied from SkiRunAutoPlanner.Flow MixAndHull).</summary>
        private void MixAndHull(ResortPlan plan)
        {
            double total = 0.0;
            var km = new double[5];
            var pts = new List<float2>();
            foreach (Area a in m_P)
            {
                int b = (int)(a.Band == SlopeBand.Flat ? SlopeBand.Green : a.Band);
                km[b] += a.LengthM;
                total += a.LengthM;
                pts.AddRange(a.Ring);
            }
            for (int b = 0; b < 5; b++)
                plan.MountainMix[b] = total > 0.0 ? (float)(km[b] / total) : 0f;
            foreach (PlanLift l in m_Lifts)
            {
                pts.Add(l.Bottom.xz);
                pts.Add(l.Top.xz);
            }
            plan.RunKm = (float)(total / 1000.0);
            plan.HullKm2 = (float)HullKm2(pts, HullGrowM);
            plan.RunKmPerKm2 = plan.HullKm2 > 0f ? plan.RunKm / plan.HullKm2 : 0f;
        }

        private static float Cross(float2 o, float2 a, float2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        /// <summary>Area (km2) of the convex hull (monotone chain) of the points grown by grow meters (copied from v2).</summary>
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

        // ---------------------------------------------------------------- the relation gate (spec section 10)

        /// <summary>Every planner relation between two kept areas, tested with the game's link rule on the realized outlines. A relation
        /// is an area end with a junction partner (a cut by a partner), or an area end that holds its source's end and a planner contact
        /// there (A ends onto B, A starts off B) with a kept area of B within reach. Counted once per (area end, element).</summary>
        private void RelationGate()
        {
            foreach (Area x in m_Out)
            {
                Elem a = m_E[x.Elem];
                for (int s = 0; s < 2; s++)
                {
                    var byElem = new Dictionary<int, bool>();   // element -> linked
                    var order = new List<int>();
                    var through = new Dictionary<int, string>();   // element -> how the relation came (report only)
                    // key: the element the relation is with. A merged chain is one relation over all its areas (its hosts and spurs)
                    void Consider(Area y, string how, int key)
                    {
                        if (y.Existing || y.Elem < 0 || y.Elem == x.Elem && y.Segment == x.Segment)
                            return;
                        bool linked = LiftServedRule.OutlinesLinked(x.Ring, y.Ring);
                        if (!byElem.TryGetValue(key, out bool was))
                        {
                            order.Add(key);
                            byElem[key] = linked;
                            through[key] = how;
                        }
                        else if (linked && !was)
                            byElem[key] = true;
                    }
                    List<int> js = s == 0 ? x.J0 : x.J1;
                    foreach (int j in js)
                    {
                        Area y = x.Track ? m_T[j] : m_P[j];
                        Consider(y, "a junction", y.Elem);
                    }
                    if (s == 0 ? x.Orig0 : x.Orig1)
                    {
                        float2 endPt = s == 0 ? a.Pts[0] : a.Pts[a.Pts.Count - 1];
                        foreach (Elem b in m_E)
                        {
                            if (b == a || b.Areas.Count == 0 || !(s == 0 ? StartsOff(a, b) : EndsOnto(a, b)))
                                continue;
                            foreach (int k in b.Areas)
                            {
                                Area y = b.Type == ElemType.Track ? m_T[k] : m_P[k];
                                if (PlanGeometry.PointRingDistance(y.Ring, endPt) <= RelationM + JunctionReachM)
                                    Consider(y, b.Merged ? $"merged track {b.Source}" : "a contact", b.Merged ? b.Id : y.Elem);
                            }
                        }
                    }
                    int unl = s == 0 ? x.Unlinked0 : x.Unlinked1;
                    foreach (int b in order)
                    {
                        m_Rep.Relations++;
                        if (byElem[b])
                        {
                            m_Rep.RelationLinks++;
                            continue;
                        }
                        if (Via(x, js, m_E[b]))
                        {
                            m_Rep.RelationLinks++;
                            m_Rep.RelationVia++;
                            continue;   // the end was cut by an area that took the shared stretch, and that area links to b
                        }
                        if (unl == 2)
                            continue;   // an end the report marks unlinked because a blocked cell stopped the extension
                        m_Rep.RelationMisses++;
                        Elem be = m_E[b];
                        float2 p = s == 0 ? x.Line[0] : x.Line[x.Line.Length - 1];
                        m_Rep.Lines.Add($"relation not linked: {(x.Track ? "track" : "piste")} {a.Source} ({a.Kind}) segment {x.Segment} {(s == 0 ? "start" : "end")} at ({p.x:F0}, {p.y:F0}) and {(be.Type == ElemType.Track ? "track" : "piste")} {be.Source} ({be.Kind}){(unl == 1 ? " (end marked unlinked)" : "")}, outlines {Gap(x, be):F0} m apart, through {through[b]}");
                    }
                }
            }
            MergedRelationGate();
        }

        /// <summary>R2-r: the planner relations of a merged chain's own ends (it starts off b, it ends onto b, b has a kept area within
        /// RelationM + JunctionReachM of that end). The chain is its hosts in the game, so each relation must be a game link between a host
        /// and a kept area of b. A host that is an area of b is the link (the chain merged into the element it relates to).</summary>
        private void MergedRelationGate()
        {
            foreach (Elem a in m_E)
            {
                if (!a.Merged || a.Areas.Count == 0)
                    continue;
                for (int s = 0; s < 2; s++)
                {
                    float2 endPt = s == 0 ? a.Pts[0] : a.Pts[a.Pts.Count - 1];
                    var seen = new List<int>();   // host elements already counted at this end
                    foreach (Elem b in m_E)
                    {
                        if (b == a || b.Areas.Count == 0 || !(s == 0 ? StartsOff(a, b) : EndsOnto(a, b)))
                            continue;
                        bool near = false, linked = false;
                        int key = -1;
                        foreach (int k in b.Areas)
                        {
                            Area y = AreaOf(b, k);
                            if (y.Existing || PlanGeometry.PointRingDistance(y.Ring, endPt) > RelationM + JunctionReachM)
                                continue;
                            near = true;
                            key = key < 0 ? y.Elem : key;
                            if (b.Type == ElemType.Track && a.Areas.Contains(k))
                                linked = true;
                            foreach (int h in a.Areas)
                                linked |= LiftServedRule.OutlinesLinked(m_T[h].Ring, y.Ring);
                        }
                        if (!near || seen.Contains(key))
                            continue;   // the same area element again (b merged into it too): counted once
                        seen.Add(key);
                        m_Rep.Relations++;
                        m_Rep.MergedRelations++;
                        bool through = !linked && MergedVia(a, b, endPt);
                        if (linked || through)
                        {
                            m_Rep.RelationLinks++;
                            m_Rep.MergedLinks++;
                            if (through)
                                m_Rep.RelationVia++;
                            continue;
                        }
                        m_Rep.RelationMisses++;
                        m_Rep.Lines.Add($"relation not linked: merged track {a.Source} ({a.Kind}) {(s == 0 ? "start" : "end")} at ({endPt.x:F0}, {endPt.y:F0}) " +
                            $"and {(b.Type == ElemType.Track ? "track" : "piste")} {b.Source} ({b.Kind}), hosts {HostNames(a)}");
                    }
                }
            }
        }

        /// <summary>R2-r: a merged chain's relation to b also holds through one track that lies between them: a track area within RelationM
        /// of the chain's end that is a game link to a host of the chain and to a kept area of b (the merged form of Via).</summary>
        private bool MergedVia(Elem a, Elem b, float2 endPt)
        {
            foreach (Area z in m_T)
            {
                if (z.Existing || z.Elem == a.Id || z.Elem == b.Id || a.Areas.Contains(m_T.IndexOf(z)) ||
                    PlanGeometry.PointRingDistance(z.Ring, endPt) > RelationM)
                    continue;
                bool toHost = false, toB = false;
                foreach (int h in a.Areas)
                    toHost |= LiftServedRule.OutlinesLinked(z.Ring, m_T[h].Ring);
                if (!toHost)
                    continue;
                foreach (int k in b.Areas)
                {
                    Area y = AreaOf(b, k);
                    toB |= !y.Existing && LiftServedRule.OutlinesLinked(z.Ring, y.Ring);
                }
                if (toB)
                    return true;
            }
            return false;
        }

        /// <summary>True when an area that cut this end (a junction partner at it) is a game link to a kept area of b.</summary>
        private bool Via(Area x, List<int> js, Elem b)
        {
            foreach (int j in js)
            {
                Area z = x.Track ? m_T[j] : m_P[j];
                foreach (int k in b.Areas)
                {
                    Area y = b.Type == ElemType.Track ? m_T[k] : m_P[k];
                    if (y != z && LiftServedRule.OutlinesLinked(z.Ring, y.Ring))
                        return true;
                }
            }
            return false;
        }

        /// <summary>The smallest outline distance from an area to the kept areas of an element (report only).</summary>
        private float Gap(Area x, Elem b)
        {
            float best = float.PositiveInfinity;
            foreach (int k in b.Areas)
            {
                Area y = b.Type == ElemType.Track ? m_T[k] : m_P[k];
                best = Math.Min(best, PlanGeometry.RingDistance(x.Ring, y.Ring, 500f, out _, out _, out _, out _));
            }
            return best;
        }

        // ---------------------------------------------------------------- R2-y: why a lift loses the bottom (report only)

        /// <summary>Report only. For each lift the planner reads as reaching the base band (ToBottom Reaches) and the realized plan does
        /// not: the cheapest way down when the planner's own link rules may fill in what the game does not link. Nodes are the realized
        /// areas and the dropped elements (no area). A game link costs 0. Each planner-only step costs 1: a start within 150 m of the top
        /// that the served rule does not take ("top"), two centerlines within 40 m whose outlines are not a game link ("link", with the
        /// outline gap), the next segment of the same source ("piece", the stretch lost between them), a dropped element ("dropped").
        /// One line per lift: "bottom lost: L{id}" and its steps. For a lift the realized plan takes to the band and the planner does
        /// not: the area that gets there ("bottom gained").</summary>
        private void BottomCauses(int L, int A, List<PlanEdge> edges, bool[] bottomNode)
        {
            bool any = false;
            for (int l = 0; l < L && l < m_Plan.Lifts.Count; l++)
                any |= (m_Plan.Lifts[l].ToBottom == RunsFirstBottom.Reaches) != m_Rep.LiftBottom[l];
            if (!any)
                return;
            // nodes: areas 0..A-1, dropped elements A..
            var pts = new List<float3[]>(A);
            var names = new List<string>(A);
            var src = new List<int>(A);
            for (int i = 0; i < A; i++)
            {
                Area a = m_Out[i];
                var p = new float3[a.Line.Length];
                for (int k = 0; k < p.Length; k++)
                    p[k] = new float3(a.Line[k].x, H(a.Line[k]), a.Line[k].y);
                pts.Add(p);
                names.Add($"{(a.Track ? "track" : "piste")} {m_E[a.Elem].Source}/{a.Segment}");
                src.Add(a.Elem);
            }
            foreach (Elem e in m_E)
                if (e.Areas.Count == 0 && e.Samp != null && e.Samp.Length >= 2)
                {
                    pts.Add(e.Samp);
                    names.Add($"{(e.Type == ElemType.Track ? "track" : "piste")} {e.Source} (dropped, {e.LengthM:F0} m)");
                    src.Add(e.Id);
                }
            // the lost head and tail of a kept element: its planned stretch before its first area and after its last (over 20 m)
            int firstLost = pts.Count;
            foreach (Elem e in m_E)
            {
                if (e.Areas.Count == 0 || e.Merged || e.Samp == null || e.Samp.Length < 3)
                    continue;
                int first = -1, last = -1;
                for (int i = 0; i < A; i++)
                    if (m_Out[i].Elem == e.Id)
                    {
                        if (first < 0 || m_Out[i].Segment < m_Out[first].Segment)
                            first = i;
                        if (last < 0 || m_Out[i].Segment > m_Out[last].Segment)
                            last = i;
                    }
                if (first < 0)
                    continue;
                for (int s = 0; s < 2; s++)
                {
                    float2 at = s == 0 ? m_Out[first].Line[0] : m_Out[last].Line[m_Out[last].Line.Length - 1];
                    int k0 = 0;
                    for (int k = 1; k < e.Samp.Length; k++)
                        if (math.distancesq(e.Samp[k].xz, at) < math.distancesq(e.Samp[k0].xz, at))
                            k0 = k;
                    int a0 = s == 0 ? 0 : k0, a1 = s == 0 ? k0 : e.Samp.Length - 1;
                    if (a1 - a0 < 2)
                        continue;
                    var part = new float3[a1 - a0 + 1];
                    Array.Copy(e.Samp, a0, part, 0, part.Length);
                    pts.Add(part);
                    names.Add($"{(e.Type == ElemType.Track ? "track" : "piste")} {e.Source} (lost {(s == 0 ? "head" : "tail")}, {10 * (part.Length - 1)} m)");
                    src.Add(e.Id);
                }
            }
            int n = pts.Count;
            // game links between areas (the descent graph's link edges, exit to entry), and the lift tops' served areas
            var game = new List<int>[n];
            var relax = new List<int>[n];
            for (int v = 0; v < n; v++)
            {
                game[v] = new List<int>();
                relax[v] = new List<int>();
            }
            var served = new List<int>[L];
            for (int l = 0; l < L; l++)
                served[l] = new List<int>();
            foreach (PlanEdge e in edges)
                if (e.Kind == EdgeLink && e.From >= 2 * L + A && e.From < 2 * L + 2 * A && e.To >= 2 * L && e.To < 2 * L + A)
                    game[e.From - 2 * L - A].Add(e.To - 2 * L);
                else if (e.Kind == EdgeLink && e.From < L && e.To >= 2 * L && e.To < 2 * L + A)
                    served[e.From].Add(e.To - 2 * L);
            const float NextM = 40f, RiseM = 5f, TopM = 150f;
            for (int i = 0; i < n; i++)
            {
                float3 end = pts[i][pts[i].Length - 1];
                for (int j = 0; j < n; j++)
                {
                    if (i == j || game[i].Contains(j))
                        continue;
                    float3 st = pts[j][0];
                    bool near = false;
                    foreach (float3 p in pts[j])
                        if (math.distancesq(p.xz, end.xz) < NextM * NextM && p.y <= end.y + RiseM)
                        {
                            near = true;
                            break;
                        }
                    if (!near)
                        foreach (float3 p in pts[i])
                            if (math.distancesq(p.xz, st.xz) < NextM * NextM && st.y <= p.y + RiseM)
                            {
                                near = true;
                                break;
                            }
                    // the next segment of the same source (a stretch lost between two areas)
                    if (!near && i < A && j < A && src[i] == src[j] && m_Out[j].Segment == m_Out[i].Segment + 1)
                        near = true;
                    if (near)
                        relax[i].Add(j);
                }
            }
            var cost = new int[n];
            var pred = new int[n];
            for (int l = 0; l < L && l < m_Plan.Lifts.Count; l++)
            {
                bool planned = m_Plan.Lifts[l].ToBottom == RunsFirstBottom.Reaches;
                if (planned == m_Rep.LiftBottom[l])
                    continue;
                float2 top = m_Lifts[l].Top.xz;
                var dq = new LinkedList<int>();
                for (int v = 0; v < n; v++)
                {
                    pred[v] = -1;
                    cost[v] = served[l].Contains(v) ? 0 : planned && math.distancesq(pts[v][0].xz, top) < TopM * TopM ? 1 : int.MaxValue;
                    if (cost[v] == 0)
                        dq.AddFirst(v);
                    else if (cost[v] == 1)
                        dq.AddLast(v);
                }
                int hit = -1;
                var done = new bool[n];
                while (dq.Count > 0)
                {
                    int u = dq.First.Value;
                    dq.RemoveFirst();
                    if (done[u])
                        continue;
                    done[u] = true;
                    if (u < A ? bottomNode[2 * L + A + u] : pts[u][pts[u].Length - 1].y <= m_BottomTop)
                    {
                        hit = u;
                        break;
                    }
                    foreach (int w in game[u])
                        if (cost[u] < cost[w])
                        {
                            cost[w] = cost[u];
                            pred[w] = u;
                            dq.AddFirst(w);
                        }
                    if (!planned)
                        continue;   // the realized way: game links only
                    foreach (int w in relax[u])
                        if (cost[u] + 1 < cost[w])
                        {
                            cost[w] = cost[u] + 1;
                            pred[w] = u;
                            dq.AddLast(w);
                        }
                }
                int id = m_Plan.Lifts[l].Id;
                if (!planned)
                {
                    int root = hit;
                    var way = new List<string>();
                    for (int v = hit; v >= 0; v = pred[v])
                    {
                        root = v;
                        way.Add(names[v]);
                    }
                    way.Reverse();
                    m_Rep.Lines.Add(hit >= 0 ? $"bottom gained: L{id}: {string.Join(" > ", way)} ends in the band at {pts[hit][pts[hit].Length - 1].y:F0} m, band top {m_BottomTop:F0} m; " +
                        $"the first starts {math.distance(pts[root][0].xz, top):F0} m from the top (the planner: {m_Plan.Lifts[l].ToBottom})"
                        : $"bottom gained: L{id}: no path found (the planner: {m_Plan.Lifts[l].ToBottom})");
                    continue;
                }
                if (hit < 0)
                {
                    int low = -1;
                    for (int v = 0; v < n; v++)
                        if (done[v] && (low < 0 || pts[v][pts[v].Length - 1].y < pts[low][pts[low].Length - 1].y))
                            low = v;
                    m_Rep.Lines.Add($"bottom lost: L{id}: [none] no way down even with the planner's links" +
                        (low >= 0 ? $", lowest end {names[low]} at {pts[low][pts[low].Length - 1].y:F0} m, band top {m_BottomTop:F0} m" : ""));
                    continue;
                }
                var steps = new List<string>();
                for (int v = hit; v >= 0; v = pred[v])
                {
                    int u = pred[v];
                    if (u >= 0 ? game[u].Contains(v) : served[l].Contains(v))
                        continue;
                    if (v >= A)
                        steps.Add(v >= firstLost ? $"[piece] {names[v]}" : $"[dropped] {names[v]}");
                    else if (u < 0)
                        steps.Add($"[top] {names[v]} starts {math.distance(pts[v][0].xz, top):F0} m from the top, not served");
                    else if (u >= A)
                        continue;   // the step out of a dropped element: the step into it names it
                    else if (src[u] == src[v] && m_Out[v].Segment == m_Out[u].Segment + 1)
                        steps.Add($"[piece] {names[u]} to {names[v]}, {math.distance(pts[u][pts[u].Length - 1].xz, pts[v][0].xz):F0} m lost between them");
                    else
                        steps.Add(LinkWhy(names[u], names[v], m_Out[u], m_Out[v]));
                }
                steps.Reverse();
                m_Rep.Lines.Add($"bottom lost: L{id}: " + string.Join("; ", steps) + $"; ends {names[hit]}");
            }
        }

        /// <summary>Report only: why two areas whose centerlines come within 40 m are not a game link. Touching outlines with the entry too
        /// low on a piste fail rule 1 (BelowJunction: the contact must lie min(30 m, a third of the relief of the piste) over its lowest
        /// node). Otherwise the outline gap.</summary>
        private string LinkWhy(string nu, string nv, Area u, Area v)
        {
            float gap = PlanGeometry.RingDistance(u.Ring, v.Ring, 500f, out _, out _, out _, out _);
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (float3 p in v.Nodes)
            {
                lo = Math.Min(lo, p.y);
                hi = Math.Max(hi, p.y);
            }
            float endY = H(u.Line[u.Line.Length - 1]);
            string kind = !LiftServedRule.OutlinesLinked(u.Ring, v.Ring) ? "not linked"
                : v.Track ? "linked, the track climbs from the entry"
                : $"linked, the entry under the junction rule (need {Math.Min(LiftServedRule.MinBelowM, (hi - lo) / LiftServedRule.BelowDropDivisor):F0} m over its low end)";
            return $"[link] {nu} to {nv}, outlines {gap:F0} m apart, {kind}; {nu} ends at {endY:F0} m, {nv} spans {lo:F0} to {hi:F0} m";
        }

        // ---------------------------------------------------------------- EasiestDescent (copied from SkiRunAutoPlanner.Flow, spec section 9)

        /// <summary>The easiest descent on a plan graph: Dijkstra over (hardest band so far, meters) from src along every edge but lifts
        /// (Kind 1), ties by node index, until the first target node is settled. The search skips link edges from src onto a node below
        /// srcPadLimit. Returns the hardest band (SlopeBand value) of that path, -1 = no path. firstRun = the run of the path's first edge
        /// that carries one (-1 none). One-shot form (tests).</summary>
        public static int EasiestDescent(int nodes, PlanEdge[] g, double[] meters, int src, bool[] target, int srcPadLimit, out int firstRun)
        {
            var s = new DescentScratch();
            s.Prepare(nodes, g, meters);
            return s.Run(src, target, srcPadLimit, out firstRun);
        }

        /// <summary>EasiestDescent's state, reused over all lifts: the adjacency of every edge but lifts and a binary heap keyed (band, meters,
        /// node) (copied from SkiRunAutoPlanner.Flow DescentScratch).</summary>
        public sealed class DescentScratch
        {
            private int[] m_First = new int[0], m_Adj = new int[0], m_At = new int[0], m_Band = new int[0], m_Pred = new int[0];
            private int[] m_HBand = new int[0], m_HNode = new int[0];
            private double[] m_Len = new double[0], m_HLen = new double[0];
            private bool[] m_Done = new bool[0];
            private PlanEdge[] m_G;
            private double[] m_Meters;
            private int m_Nodes, m_HN;

            private static void Grow<T>(ref T[] a, int n)
            {
                if (a.Length < n)
                    a = new T[Math.Max(n, 2 * a.Length)];
            }

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
                int cap = m_First[nodes] + 1;
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
                        continue;
                    m_Done[u] = true;
                    if (u != src && target[u])
                    {
                        for (int v = u; m_Pred[v] >= 0; v = g[m_Pred[v]].From)
                            if (g[m_Pred[v]].Run >= 0)
                                firstRun = g[m_Pred[v]].Run;
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
    }
}
