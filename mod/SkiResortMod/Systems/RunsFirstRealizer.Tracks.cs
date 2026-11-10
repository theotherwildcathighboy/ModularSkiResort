// Runs-first realizer: cat tracks (spec section 6). The planner emits each lift's traverse tree cut at every node, down to 7 m pieces.
// The realizer rebuilds each tree from its pieces (they share end points) and cuts it into chains: the trunk (the longest path from
// the tree's top to a leaf), then repeatedly the longest remaining path from a fork to a leaf (its parent is the chain it forks from).
// Links and graded runs under the blue line are chains by themselves. Each chain becomes 10 m ribbons (the bench round 3 grades), cut
// where another accepted track is in the way (a butt joint), split at its sharpest bend when the ribbon crosses itself. Pure.
// Spec: docs/research/quickruns-v2/port/REALIZER-SPEC.md section 6.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class RunsFirstRealizer
    {
        private readonly List<Elem> m_TreeChains = new List<Elem>(), m_LinkChains = new List<Elem>();

        // ---------------------------------------------------------------- trees to chains (6.1)

        private static ulong PolyKey(List<float2> p)
        {
            ulong h = 14695981039346656037UL;
            void U(uint v)
            {
                h ^= v;
                h *= 1099511628211UL;
            }
            U((uint)p.Count);
            foreach (float2 q in p)
            {
                U(math.asuint(q.x));
                U(math.asuint(q.y));
            }
            return h;
        }

        private static bool SamePoly(List<float2> a, List<float2> b)
        {
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
                if (!a[i].Equals(b[i]))
                    return false;
            return true;
        }

        private static int Root(int[] p, int i)
        {
            while (p[i] != i)
                i = p[i] = p[p[i]];
            return i;
        }

        private void BuildChains()
        {
            RunsFirstResult plan = m_Plan;
            List<List<float2>> conns = plan.Connectors;
            // Links are a subset of the connectors (the same polylines again): realize each once, as a link
            var linkKeys = new Dictionary<ulong, List<int>>();
            for (int l = 0; l < plan.Links.Count; l++)
            {
                ulong k = PolyKey(plan.Links[l]);
                if (!linkKeys.TryGetValue(k, out List<int> li))
                    linkKeys[k] = li = new List<int>();
                li.Add(l);
            }
            var linkConn = new int[plan.Links.Count];
            for (int l = 0; l < linkConn.Length; l++)
                linkConn[l] = -1;
            var isLink = new bool[conns.Count];
            for (int c = 0; c < conns.Count; c++)
            {
                if (conns[c] == null || conns[c].Count < 2)
                    continue;
                m_Rep.TrackKmPlanned += Length(conns[c]) / 1000.0;
                if (!linkKeys.TryGetValue(PolyKey(conns[c]), out List<int> li))
                    continue;
                foreach (int l in li)
                    if (linkConn[l] < 0 && SamePoly(conns[c], plan.Links[l]))
                    {
                        linkConn[l] = c;
                        isLink[c] = true;
                        break;
                    }
            }
            // Pieces and their end nodes (ends within JoinM are one node)
            var pieces = new List<int>();
            for (int c = 0; c < conns.Count; c++)
                if (!isLink[c] && conns[c] != null && conns[c].Count >= 2)
                    pieces.Add(c);
            int np = pieces.Count;
            m_Rep.TrackSources += np + plan.Links.Count;
            var ends = new float2[2 * np];
            for (int k = 0; k < np; k++)
            {
                List<float2> pl = conns[pieces[k]];
                ends[2 * k] = pl[0];
                ends[2 * k + 1] = pl[pl.Count - 1];
            }
            var node = new int[2 * np];
            for (int a = 0; a < node.Length; a++)
                node[a] = a;
            for (int a = 0; a < node.Length; a++)
                for (int b = a + 1; b < node.Length; b++)
                    if (math.distancesq(ends[a], ends[b]) <= JoinM * JoinM)
                    {
                        int ra = Root(node, a), rb = Root(node, b);
                        if (ra != rb)
                            node[Math.Max(ra, rb)] = Math.Min(ra, rb);
                    }
            for (int a = 0; a < node.Length; a++)
                node[a] = Root(node, a);
            // Trees: pieces joined through shared nodes
            var treeOf = new int[np];
            {
                var p = new int[node.Length];
                for (int a = 0; a < p.Length; a++)
                    p[a] = a;
                for (int k = 0; k < np; k++)
                {
                    int ra = Root(p, node[2 * k]), rb = Root(p, node[2 * k + 1]);
                    if (ra != rb)
                        p[Math.Max(ra, rb)] = Math.Min(ra, rb);
                }
                for (int k = 0; k < np; k++)
                    treeOf[k] = Root(p, node[2 * k]);
            }
            var trees = new List<int>();   // tree roots in the order of their first piece
            for (int k = 0; k < np; k++)
                if (!trees.Contains(treeOf[k]))
                    trees.Add(treeOf[k]);
            // The tree's lift: the lift whose top is nearest to the tree's highest point, within TreeLiftM
            var treeLift = new int[trees.Count];
            var treeTop = new int[trees.Count];
            for (int t = 0; t < trees.Count; t++)
            {
                int top = -1;
                float topY = float.NegativeInfinity, anyY = float.NegativeInfinity;
                int any = -1;
                var into = new HashSet<int>();
                for (int k = 0; k < np; k++)
                    if (treeOf[k] == trees[t])
                        into.Add(node[2 * k + 1]);
                for (int k = 0; k < np; k++)
                {
                    if (treeOf[k] != trees[t])
                        continue;
                    for (int s = 0; s < 2; s++)
                    {
                        int v = node[2 * k + s];
                        float y = H(ends[v]);
                        if (y > anyY)
                        {
                            anyY = y;
                            any = v;
                        }
                        if (!into.Contains(v) && y > topY)
                        {
                            topY = y;
                            top = v;
                        }
                    }
                }
                treeTop[t] = top >= 0 ? top : any;
                float2 hp = ends[treeTop[t]];
                int best = -1;
                float bd = TreeLiftM;
                for (int L = 0; L < plan.Lifts.Count; L++)
                {
                    float d = math.distance(plan.Lifts[L].Top.xz, hp);
                    if (d <= bd)
                    {
                        bd = d;
                        best = L;
                    }
                }
                treeLift[t] = best;
            }
            // Chains per tree, the trees in lift order (no lift last)
            var treeOrder = new List<int>();
            for (int L = 0; L <= plan.Lifts.Count; L++)
                for (int t = 0; t < trees.Count; t++)
                    if ((L < plan.Lifts.Count && treeLift[t] == L) || (L == plan.Lifts.Count && treeLift[t] < 0))
                        treeOrder.Add(t);
            foreach (int t in treeOrder)
                TreeChains(trees[t], treeTop[t], treeLift[t], pieces, node, treeOf, ends);
            // Links, each a chain by itself, in list order (the last group)
            for (int l = 0; l < plan.Links.Count; l++)
            {
                List<float2> pl = plan.Links[l];
                if (pl == null || pl.Count < 2)
                    continue;
                if (linkConn[l] < 0)
                    m_Rep.TrackKmPlanned += Length(pl) / 1000.0;   // a link that is not among the connectors (spec: never)
                var e = new Elem
                {
                    Id = m_E.Count, Source = -1 - (linkConn[l] >= 0 ? linkConn[l] : conns.Count + l), Type = ElemType.Track, Pts = pl,
                    Planned = SlopeBand.Green, Kind = "link", Link = true,
                };
                Sample(e);
                m_E.Add(e);
                m_LinkChains.Add(e);
            }
            m_Rep.TrackChains = m_TreeChains.Count + m_LinkChains.Count;
        }

        private static float Length(List<float2> p)
        {
            float s = 0f;
            for (int i = 1; i < p.Count; i++)
                s += math.distance(p[i - 1], p[i]);
            return s;
        }

        /// <summary>One tree as chains (6.1): the shortest-path tree from the top (ties by piece index), the trunk = the longest path from
        /// the top to a leaf, then repeatedly the longest remaining path from a node on a chain to a leaf. A piece outside the spanning
        /// tree (a cycle) is a chain by itself.</summary>
        private void TreeChains(int tree, int top, int lift, List<int> pieces, int[] node, int[] treeOf, float2[] ends)
        {
            List<List<float2>> conns = m_Plan.Connectors;
            var my = new List<int>();
            for (int k = 0; k < pieces.Count; k++)
                if (treeOf[k] == tree)
                    my.Add(k);
            // Shortest-path tree from the top (Dijkstra on the few nodes, linear scan)
            var dist = new Dictionary<int, float> { [top] = 0f };
            var via = new Dictionary<int, int>();   // node -> piece (local k) that reaches it
            var done = new HashSet<int>();
            while (true)
            {
                int u = -1;
                float du = float.PositiveInfinity;
                foreach (KeyValuePair<int, float> kv in dist)
                    if (!done.Contains(kv.Key) && (kv.Value < du || (kv.Value == du && kv.Key < u)))
                    {
                        u = kv.Key;
                        du = kv.Value;
                    }
                if (u < 0)
                    break;
                done.Add(u);
                foreach (int k in my)
                {
                    int a = node[2 * k], b = node[2 * k + 1];
                    int w = a == u ? b : b == u ? a : -1;
                    if (w < 0 || done.Contains(w))
                        continue;
                    float d = du + Length(conns[pieces[k]]);
                    if (!dist.TryGetValue(w, out float old) || d < old || (d == old && k < via[w]))
                    {
                        dist[w] = d;
                        via[w] = k;
                    }
                }
            }
            // Children per node in the spanning tree (piece k from its parent node to its child node)
            var kids = new Dictionary<int, List<int>>();
            var inTree = new HashSet<int>();
            foreach (KeyValuePair<int, int> kv in via)
            {
                int k = kv.Value, child = kv.Key;
                int parent = node[2 * k] == child ? node[2 * k + 1] : node[2 * k];
                if (!kids.TryGetValue(parent, out List<int> l))
                    kids[parent] = l = new List<int>();
                l.Add(k);
                inTree.Add(k);
            }
            foreach (List<int> l in kids.Values)
                l.Sort();
            // Longest way down from each node (memoized over the tree)
            var down = new Dictionary<int, float>();
            float Down(int v)
            {
                if (down.TryGetValue(v, out float d))
                    return d;
                float best = 0f;
                if (kids.TryGetValue(v, out List<int> l))
                    foreach (int k in l)
                    {
                        int c = node[2 * k] == v ? node[2 * k + 1] : node[2 * k];
                        best = Math.Max(best, Length(conns[pieces[k]]) + Down(c));
                    }
                down[v] = best;
                return best;
            }
            var used = new HashSet<int>();
            var chainNodes = new List<(int Node, Elem Chain)>();
            Elem MakeChain(int from, int firstPiece, Elem parent)
            {
                var pts = new List<float2>();
                int v = from, k = firstPiece;
                int firstConn = pieces[k];
                var nodesOn = new List<int> { from };
                while (k >= 0)
                {
                    used.Add(k);
                    List<float2> pl = conns[pieces[k]];
                    bool fwd = node[2 * k] == v;
                    int w = fwd ? node[2 * k + 1] : node[2 * k];
                    for (int i = 0; i < pl.Count; i++)
                    {
                        float2 q = fwd ? pl[i] : pl[pl.Count - 1 - i];
                        if (pts.Count == 0 || math.distancesq(pts[pts.Count - 1], q) > JoinM * JoinM)
                            pts.Add(q);
                    }
                    v = w;
                    nodesOn.Add(v);
                    // continue down the longest way
                    k = -1;
                    float best = -1f;
                    if (kids.TryGetValue(v, out List<int> l))
                        foreach (int kk in l)
                        {
                            if (used.Contains(kk))
                                continue;
                            int c = node[2 * kk] == v ? node[2 * kk + 1] : node[2 * kk];
                            float len = Length(conns[pieces[kk]]) + Down(c);
                            if (len > best)
                            {
                                best = len;
                                k = kk;
                            }
                        }
                }
                var e = new Elem
                {
                    Id = m_E.Count, Source = -1 - firstConn, Type = ElemType.Track, Pts = pts, Planned = SlopeBand.Green, Kind = "tree",
                    Lift = lift, Parent = parent != null ? parent.Id : -1,
                };
                Sample(e);
                m_E.Add(e);
                m_TreeChains.Add(e);
                foreach (int nn in nodesOn)
                    chainNodes.Add((nn, e));
                return e;
            }
            // The trunk
            {
                int k = -1;
                float best = -1f;
                if (kids.TryGetValue(top, out List<int> l))
                    foreach (int kk in l)
                    {
                        int c = node[2 * kk] == top ? node[2 * kk + 1] : node[2 * kk];
                        float len = Length(conns[pieces[kk]]) + Down(c);
                        if (len > best)
                        {
                            best = len;
                            k = kk;
                        }
                    }
                if (k >= 0)
                    MakeChain(top, k, null);
            }
            // The forks, longest first
            while (true)
            {
                int bestK = -1, bestFrom = -1;
                Elem bestParent = null;
                float best = -1f;
                foreach ((int Node, Elem Chain) cn in chainNodes)
                {
                    if (!kids.TryGetValue(cn.Node, out List<int> l))
                        continue;
                    foreach (int kk in l)
                    {
                        if (used.Contains(kk))
                            continue;
                        int c = node[2 * kk] == cn.Node ? node[2 * kk + 1] : node[2 * kk];
                        float len = Length(conns[pieces[kk]]) + Down(c);
                        if (len > best || (len == best && kk < bestK))
                        {
                            best = len;
                            bestK = kk;
                            bestFrom = cn.Node;
                            bestParent = cn.Chain;
                        }
                    }
                }
                if (bestK < 0)
                    break;
                MakeChain(bestFrom, bestK, bestParent);
            }
            // Pieces outside the spanning tree (a cycle): each a chain by itself
            foreach (int k in my)
            {
                if (used.Contains(k))
                    continue;
                used.Add(k);
                var e = new Elem
                {
                    Id = m_E.Count, Source = -1 - pieces[k], Type = ElemType.Track, Pts = conns[pieces[k]], Planned = SlopeBand.Green,
                    Kind = "tree", Lift = lift,
                };
                Sample(e);
                m_E.Add(e);
                m_TreeChains.Add(e);
            }
        }

        // ---------------------------------------------------------------- tracks (6.2)

        private void RealizeTracks()
        {
            var order = new List<Elem>(m_TreeChains);
            foreach (Elem e in m_E)
                if (e.Type == ElemType.Track && e.Source >= 0)
                    order.Add(e);   // graded runs under the blue line, in list order
            order.AddRange(m_LinkChains);
            foreach (Elem e in order)
            {
                if (Canceled)
                    return;
                RealizeTrack(e);
            }
            // A piste a new track crosses (informational, AutoRun.CrossesCatTrack)
            foreach (Area p in m_P)
                foreach (Area t in m_T)
                    if (!(math.any(t.Hi < p.Lo) || math.any(t.Lo > p.Hi)) && PlanGeometry.RingDistance(p.Ring, t.Ring, 0.01f, out _, out _, out _, out _) <= 0f)
                    {
                        p.CrossesTrack = true;
                        break;
                    }
        }

        private byte FreeTrack(float2 p, out int own)
        {
            own = -1;
            if (!Cell(p, out int c))
                return CRegion;
            byte f = m_Flags[c];
            // the ribbon is TrackHalfWidthM each side of the centerline: near the edge, test it exactly (iteration 2, item 5)
            if ((f & FBlockRegion) != 0 || ((f & FNearEdge) != 0 && !InRegion(p, EdgeBlockM + TrackHalfWidthM)))
                return CRegion;
            if ((f & FBlockHard) != 0)
                return CObstacle;
            int N = m_OwnTrack[c];
            if (N >= 0 && m_DTrack[c] < TrackFreeM)
            {
                own = N;
                return CTrack;
            }
            if ((f & FBlockTrack) != 0)
                return CObstacle;
            return CFree;
        }

        private void RealizeTrack(Elem e)
        {
            GradePath path = GradePath.Build(e.Pts, SampleM);
            if (path == null)
            {
                DropElem(e, AutoReject.TooShort, "no centerline");
                return;
            }
            int n = path.Count;
            var cause = new byte[n];
            var owner = new int[n];
            for (int i = 0; i < n; i++)
                cause[i] = FreeTrack(path.Xz[i], out owner[i]);
            AutoReject last = AutoReject.TooShort;
            bool anyTrack = false, anyRegion = false, any = false;
            int segment = 0;
            for (int i = 0; i < n; i++)
            {
                anyTrack |= cause[i] == CTrack;
                anyRegion |= cause[i] == CRegion;
                if (cause[i] != CFree || (i > 0 && cause[i - 1] == CFree))
                    continue;
                int j = i;
                while (j + 1 < n && cause[j + 1] == CFree)
                    j++;
                any = true;
                var pc = new Piece { S = i, E = j };
                pc.Orig0 = i == 0 || AllTrack(cause, 0, i - 1);
                pc.Orig1 = j == n - 1 || AllTrack(cause, j + 1, n - 1);
                if (i > 0 && cause[i - 1] == CTrack)
                    pc.J0.Add(owner[i - 1]);
                if (j < n - 1 && cause[j + 1] == CTrack)
                    pc.J1.Add(owner[j + 1]);
                float len = path.S[j] - path.S[i];
                bool joined = (pc.Orig0 || pc.J0.Count > 0) && (pc.Orig1 || pc.J1.Count > 0);
                if (j - i < 1 || len < TrackStubM || (!joined && len < TrackMinPieceM))
                {
                    m_Rep.TrackPiecesLost++;
                    m_Res.Rejects[(int)AutoReject.TooShort]++;
                    m_Rep.Lines.Add($"track {e.Source} ({e.Kind}): piece {len:F0} m at ({path.Xz[i].x:F0}, {path.Xz[i].y:F0}) lost (too short)");
                    continue;
                }
                AutoReject why = BuildTrackPiece(e, path, pc, ref segment, out float2 at);
                if (why != AutoReject.Kept)
                {
                    last = why;
                    m_Res.Rejects[(int)why]++;
                    m_Rep.Lines.Add($"track {e.Source} ({e.Kind}): piece at ({at.x:F0}, {at.y:F0}) dropped ({why})");
                }
            }
            if (e.Areas.Count == 0)
                DropElem(e, any ? last : anyTrack ? AutoReject.Overlap : anyRegion ? AutoReject.Region : AutoReject.Obstacle,
                    any ? "every piece failed" : "no free stretch");
        }

        private static bool AllTrack(byte[] cause, int a, int b)
        {
            for (int i = a; i <= b; i++)
                if (cause[i] != CTrack)
                    return false;
            return true;
        }

        private static GradePath SubPath(GradePath path, int s, int e)
        {
            int n = e - s + 1;
            var p = new GradePath { Xz = new float2[n], S = new float[n], StepM = path.StepM };
            for (int i = 0; i < n; i++)
            {
                p.Xz[i] = path.Xz[s + i];
                p.S[i] = path.S[s + i] - path.S[s];
            }
            p.LengthM = p.S[n - 1];
            return p;
        }

        /// <summary>6.2: the ribbon at 10 m; on Crosses, split at the sharpest bend into two butt-jointed pieces; still Crosses, the same at
        /// 8 m; still, drop with the place.</summary>
        private AutoReject BuildTrackPiece(Elem e, GradePath path, Piece pc, ref int segment, out float2 at)
        {
            at = path.Xz[pc.S];
            GradePath sub = ReachEnds(e, SubPath(path, pc.S, pc.E), pc.Orig0, pc.Orig1);
            AutoReject why = AutoReject.Crosses;
            foreach (float hw in new[] { TrackHalfWidthM, TrackNarrowHalfWidthM })
            {
                float2[] ring = GradeRibbon.Build(sub, hw, out GradeRibbonFail fail, out float2 where);
                if (fail == GradeRibbonFail.None)
                {
                    why = CheckTrack(ring);
                    if (why == AutoReject.Kept)
                    {
                        AcceptTrack(e, sub, ring, hw, pc.J0, pc.J1, pc.Orig0, pc.Orig1, segment++);
                        return AutoReject.Kept;
                    }
                    at = path.Xz[pc.S];
                    continue;
                }
                at = where;
                why = AutoReject.Crosses;
                if (fail != GradeRibbonFail.Crosses || sub.Count < 7)
                    continue;
                // The sharpest bend: the largest turn between the 8 m legs on both sides of a sample
                int k = -1;
                float bestTurn = -2f;
                for (int i = 2; i + 2 < sub.Count; i++)
                {
                    float2 a = math.normalizesafe(sub.Xz[i] - sub.Xz[i - 2]), b = math.normalizesafe(sub.Xz[i + 2] - sub.Xz[i]);
                    float turn = -math.dot(a, b);
                    if (turn > bestTurn)
                    {
                        bestTurn = turn;
                        k = i;
                    }
                }
                for (int g = 1; g <= 8 && k >= 0; g++)
                {
                    int ea = k - g, sb = k + g;
                    if (ea < 1 || sb > sub.Count - 2)
                        break;
                    if (sub.S[ea] - sub.S[0] < TrackStubM || sub.S[sub.Count - 1] - sub.S[sb] < TrackStubM)
                        break;   // a half under two track widths: no split
                    GradePath pa = SubPath(sub, 0, ea), pb = SubPath(sub, sb, sub.Count - 1);
                    float2[] ra = GradeRibbon.Build(pa, hw, out GradeRibbonFail fa), rb = GradeRibbon.Build(pb, hw, out GradeRibbonFail fb);
                    if (fa != GradeRibbonFail.None || fb != GradeRibbonFail.None)
                        continue;
                    if (PlanGeometry.RingDistance(ra, rb, MergeGapM, out _, out _, out _, out _) < MergeGapM)
                        continue;
                    if (CheckTrack(ra) != AutoReject.Kept || CheckTrack(rb) != AutoReject.Kept)
                        continue;
                    int ia = AcceptTrack(e, pa, ra, hw, pc.J0, new List<int>(), pc.Orig0, false, segment++);
                    var j0 = new List<int> { ia };
                    int ib = AcceptTrack(e, pb, rb, hw, j0, pc.J1, false, pc.Orig1, segment++);
                    m_T[ia].J1.Add(ib);
                    m_Rep.Lines.Add($"track {e.Source} ({e.Kind}): split at its sharpest bend ({sub.Xz[k].x:F0}, {sub.Xz[k].y:F0}) at {2f * hw:F0} m");
                    return AutoReject.Kept;
                }
            }
            return why;
        }

        /// <summary>Iteration 2, item 6: an original end of a track that the planner relates to a piste or a track (it starts off it or
        /// ends onto it) and that is not within TrackReachM of any kept area of that element is extended straight toward the element's
        /// nearest realized centerline, in SampleM steps, at most ExtendMaxM, until the end is within TrackReachM of its outline (a game
        /// link, LiftServedRule.LinkMeters 25 m). A track may cross a piste, so a piste never stops it. A blocked cell, the region and
        /// another track's gap stop it (the related track's own gap ends it: the butt joint is the link).</summary>
        private GradePath ReachEnds(Elem e, GradePath sub, bool orig0, bool orig1)
        {
            if (sub == null || sub.Count < 2 || (!orig0 && !orig1))
                return sub;
            List<float2> head = orig0 ? Reach(e, sub.Xz[0], sub.Xz[1], true) : null;
            List<float2> tail = orig1 ? Reach(e, sub.Xz[sub.Count - 1], sub.Xz[sub.Count - 2], false) : null;
            if ((head == null || head.Count == 0) && (tail == null || tail.Count == 0))
                return sub;
            var pts = new List<float2>();
            if (head != null)
                for (int i = head.Count - 1; i >= 0; i--)
                    pts.Add(head[i]);
            pts.AddRange(sub.Xz);
            if (tail != null)
                pts.AddRange(tail);
            GradePath ext = GradePath.Build(pts, sub.StepM);
            if (ext == null)
                return sub;
            m_Rep.TrackReaches += (head != null && head.Count > 0 ? 1 : 0) + (tail != null && tail.Count > 0 ? 1 : 0);
            return ext;
        }

        private List<float2> Reach(Elem e, float2 end, float2 inner, bool start)
        {
            // the related element with no kept area within reach of this end, nearest first
            Area target = null;
            Elem tb = null;
            float best = float.PositiveInfinity;
            foreach (Elem b in m_E)
            {
                if (b == e || b.Areas.Count == 0 || !(start ? StartsOff(e, b) : EndsOnto(e, b)))
                    continue;
                float near = float.PositiveInfinity;
                Area nearA = null;
                foreach (int k in b.Areas)
                {
                    Area y = b.Type == ElemType.Track ? m_T[k] : m_P[k];
                    float d = PlanGeometry.PointRingDistance(y.Ring, end);
                    if (GradeRaster.Inside(y.Ring, end))
                        d = 0f;
                    if (d < near)
                    {
                        near = d;
                        nearA = y;
                    }
                }
                if (near <= TrackReachM)
                    return null;   // already a link to this element: nothing to reach
                if (near < best && near <= TrackReachM + ExtendMaxM)
                {
                    best = near;
                    target = nearA;
                    tb = b;
                }
            }
            if (target == null)
                return null;
            // toward the nearest point of the target's realized centerline
            float2 aim = target.Line[0];
            float bd = float.PositiveInfinity;
            for (int k = 0; k + 1 < target.Line.Length; k++)
            {
                float2 a = target.Line[k], d = target.Line[k + 1] - a;
                float len2 = math.lengthsq(d);
                float2 c = a + d * (len2 > 0f ? math.saturate(math.dot(end - a, d) / len2) : 0f);
                float dd = math.distancesq(end, c);
                if (dd < bd)
                {
                    bd = dd;
                    aim = c;
                }
            }
            float2 dir = math.normalizesafe(aim - end);
            if (math.lengthsq(dir) < 0.5f || math.dot(dir, math.normalizesafe(end - inner)) < 0f)
                return null;   // the target lies behind the end: an extension would fold the track back
            var add = new List<float2>();
            for (float t = SampleM; t <= ExtendMaxM + 0.01f; t += SampleM)
            {
                float2 q = end + dir * t;
                byte cause = FreeTrack(q, out int own);
                if (cause == CTrack && tb.Type == ElemType.Track && tb.Areas.Contains(own))
                    break;   // inside the related track's gap: the butt joint is the link
                if (cause != CFree)
                    return add.Count > 0 && Linked(add[add.Count - 1], target) ? add : null;
                add.Add(q);
                if (Linked(q, target))
                    return add;
            }
            return add.Count > 0 && Linked(add[add.Count - 1], target) ? add : null;
        }

        private static bool Linked(float2 q, Area target) => GradeRaster.Inside(target.Ring, q) || PlanGeometry.PointRingDistance(target.Ring, q) <= TrackReachM;

        /// <summary>6.2 checks: region, unowned tiles (their clearance), and ExactGapShare x MergeGapM from every accepted and existing cat
        /// track. Pistes never block a track.</summary>
        private AutoReject CheckTrack(float2[] ring)
        {
            if (!RegionOk(ring))
                return AutoReject.Region;
            List<AutoObstacle> obs = m_In.Obstacles;
            if (obs != null)
                foreach (AutoObstacle ob in obs)
                {
                    if (ob == null || ob.Kind != AutoObstacleKind.Unowned || ob.Ring == null || ob.Ring.Length < 3)
                        continue;
                    if (PlanGeometry.RingDistance(ring, ob.Ring, PlanGeometry.UnownedClearanceM, out _, out _, out _, out _) < PlanGeometry.UnownedClearanceM)
                        return AutoReject.Obstacle;
                }
            PlanGeometry.BBox(ring, out float2 lo, out float2 hi);
            float need = ExactGapShare * MergeGapM;
            foreach (Area t in m_T)
            {
                if (math.any(t.Hi < lo - need) || math.any(t.Lo > hi + need))
                    continue;
                if (PlanGeometry.RingDistance(ring, t.Ring, need, out _, out _, out _, out _) < need)
                    return AutoReject.Overlap;
            }
            return AutoReject.Kept;
        }

        private int AcceptTrack(Elem e, GradePath line, float2[] ring, float hw, List<int> j0, List<int> j1, bool orig0, bool orig1, int segment)
        {
            var nodes = new float3[ring.Length];
            float top = float.NegativeInfinity, bot = float.PositiveInfinity;
            for (int i = 0; i < ring.Length; i++)
            {
                nodes[i] = At(ring[i]);
                top = Math.Max(top, nodes[i].y);
                bot = Math.Min(bot, nodes[i].y);
            }
            var a = new Area
            {
                Elem = e.Id, Segment = segment, Track = true, Graded = true, Ring = ring, Nodes = nodes, Line = (float2[])line.Xz.Clone(),
                LengthM = line.LengthM, AreaM2 = (float)PlanGeometry.PolygonArea(ring), TopY = top, BottomY = bot, Band = SlopeBand.Green,
                Planned = SlopeBand.Green, EndY = H(line.Xz[line.Count - 1]), J0 = new List<int>(j0), J1 = new List<int>(j1), Orig0 = orig0,
                Orig1 = orig1, Chain = e.Id,
            };
            a.DropM = H(line.Xz[0]) - a.EndY;
            PlanGeometry.BBox(ring, out a.Lo, out a.Hi);
            // Pitches: 30 m windows of the centerline on the raw ground
            int pitches = 0, over = 0;
            int w = Math.Max(1, (int)Math.Round(PitchM / Math.Max(0.1f, line.StepM)));
            for (int i = 0; i + w < line.Count; i++)
            {
                float d = line.S[i + w] - line.S[i];
                if (d <= 0f)
                    continue;
                pitches++;
                if (Math.Abs(H(line.Xz[i]) - H(line.Xz[i + w])) / d > PitchGrade)
                    over++;
            }
            a.PitchShare = pitches > 0 ? over / (float)pitches : 0f;
            m_Rep.TrackPitches += pitches;
            m_Rep.TrackPitchesOver += over;
            int idx = m_T.Count;
            m_T.Add(a);
            e.Areas.Add(idx);
            SeedArea(m_DTrack, m_OwnTrack, ring, idx, TrackReachM);
            m_Rep.TrackAreas++;
            m_Rep.TrackKmKept += a.LengthM / 1000.0;
            // A junction end farther than JunctionReachM from the track it meets is reported (tracks are not extended)
            for (int s = 0; s < 2; s++)
            {
                List<int> js = s == 0 ? a.J0 : a.J1;
                if (js.Count == 0)
                    continue;
                float2 endPt = s == 0 ? line.Xz[0] : line.Xz[line.Count - 1];
                float best = float.PositiveInfinity;
                foreach (int t in js)
                    foreach (float2 v in ring)
                        if (math.distance(v, endPt) <= hw + 3f)
                            best = Math.Min(best, PlanGeometry.PointRingDistance(m_T[t].Ring, v));
                if (best > JunctionReachM)
                {
                    m_Rep.UnlinkedEnds++;
                    if (s == 0)
                        a.Unlinked0 = 1;
                    else
                        a.Unlinked1 = 1;
                    m_Rep.Lines.Add($"track {e.Source} ({e.Kind}) segment {segment}: {(s == 0 ? "start" : "end")} at ({endPt.x:F0}, {endPt.y:F0}) unlinked: {best:F0} m from the track it meets");
                }
            }
            return idx;
        }
    }
}
