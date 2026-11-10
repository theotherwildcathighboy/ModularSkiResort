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
            MergedSpurs();
            ReachPisteEnds();
            // A piste a new track crosses (informational, AutoRun.CrossesCatTrack)
            foreach (Area p in m_P)
                foreach (Area t in m_T)
                    if (!(math.any(t.Hi < p.Lo) || math.any(t.Lo > p.Hi)) && PlanGeometry.RingDistance(p.Ring, t.Ring, 0.01f, out _, out _, out _, out _) <= 0f)
                    {
                        p.CrossesTrack = true;
                        break;
                    }
        }

        /// <summary>Report only: the track whose gap holds a cell (FreeTrack's own). own indexes m_T, which also holds the existing cat
        /// tracks of the obstacle list (Existing, Elem -1, no Line): a rescan of a built region has them (fix round 2b-x, the in-game
        /// ArgumentOutOfRangeException of ReachToward).</summary>
        private string GapOwner(int own) =>
            own < 0 || own >= m_T.Count ? "(none)" : m_T[own].Existing || m_T[own].Elem < 0 ? "(existing)" : m_E[m_T[own].Elem].Source.ToString();

        /// <summary>The cause of a cell for a track. own: the m_T index of the track whose gap holds it (an existing track too), or -1.</summary>
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
            bool anyTrack = false, anyRegion = false;
            for (int i = 0; i < n; i++)
            {
                anyTrack |= cause[i] == CTrack;
                anyRegion |= cause[i] == CRegion;
            }
            var st = new RangeState();
            int segment = 0;
            RealizeRange(e, path, cause, owner, 0, n - 1, true, true, null, null, ref segment, st);
            if (e.Areas.Count > 0)
            {
                m_Rep.TrackKmShared += st.Shared / 1000.0;
                return;
            }
            // No own piece: a chain that lies in the corridor of accepted tracks over most of its length, and whose own stretches were
            // only lost as too short (none failed), merges into them (MergeChain). Anything else is a drop
            if (anyTrack && !st.Failed && st.Shared >= MergeShare * path.LengthM && MergeChain(e, path, cause, owner, st.Shared))
                return;
            DropElem(e, st.Any ? st.Last : anyTrack ? AutoReject.Overlap : anyRegion ? AutoReject.Region : AutoReject.Obstacle,
                (st.Any ? "every piece failed" : "no free stretch") + $", {path.LengthM:F0} m" + CorridorNote(e, path, cause, owner));
        }

        /// <summary>What one chain's pieces did (RealizeRange).</summary>
        private sealed class RangeState
        {
            public bool Any, Failed;
            public AutoReject Last = AutoReject.TooShort;
            public float Shared;       // m of the chain in the gap of accepted tracks (carried by them)
            public int SelfSplits;
        }

        /// <summary>The free stretches of the samples lo..hi become pieces (6.2). A stretch in the gap of an accepted track is that track's
        /// corridor (R2-r): two tracks in one corridor are one track in the game, so the accepted track carries the stretch, built once,
        /// and the pieces on both sides butt onto it. orig0 / orig1: the range holds the chain's start / end. j0 / j1: the junction
        /// partners at lo / hi (a range after a self-split starts at the piece that holds the stretch before it).</summary>
        private void RealizeRange(Elem e, GradePath path, byte[] cause, int[] owner, int lo, int hi, bool orig0, bool orig1, List<int> j0, List<int> j1,
            ref int segment, RangeState st)
        {
            for (int i = lo; i < hi; i++)
                if (cause[i] == CTrack && cause[i + 1] == CTrack && m_T[owner[i]].Elem != e.Id)
                    st.Shared += path.S[i + 1] - path.S[i];   // the chain's own corridor after a self-split is lost, not carried
            for (int i = lo; i <= hi; i++)
            {
                if (cause[i] != CFree || (i > lo && cause[i - 1] == CFree))
                    continue;
                int j = i;
                while (j + 1 <= hi && cause[j + 1] == CFree)
                    j++;
                st.Any = true;
                var pc = new Piece { S = i, E = j };
                pc.Orig0 = orig0 && (i == lo || AllTrack(cause, lo, i - 1));
                pc.Orig1 = orig1 && (j == hi || AllTrack(cause, j + 1, hi));
                if (i > lo && cause[i - 1] == CTrack)
                    pc.J0.Add(owner[i - 1]);
                else if (i == lo && j0 != null)
                    pc.J0.AddRange(j0);
                if (j < hi && cause[j + 1] == CTrack)
                    pc.J1.Add(owner[j + 1]);
                else if (j == hi && j1 != null)
                    pc.J1.AddRange(j1);
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
                if (why == AutoReject.Kept)
                    m_Rep.TrackKmOwn += len / 1000.0;
                else if (why == AutoReject.Crosses && SelfSplit(e, path, pc, ref segment, st))
                    continue;
                else
                {
                    st.Last = why;
                    st.Failed = true;
                    m_Res.Rejects[(int)why]++;
                    m_Rep.Lines.Add($"track {e.Source} ({e.Kind}): piece at ({at.x:F0}, {at.y:F0}) dropped ({why})");
                }
            }
        }

        /// <summary>R2-r: a piece whose ribbon still crosses itself after the bend split (switchback legs closer than the track width plus
        /// the gap) is one track in its own corridor. Its longest clean prefix is accepted. The rest is tested against the lattice that now
        /// holds that prefix: the stretch beside the prefix is the prefix's corridor (carried by it), and the free stretches after it are
        /// pieces butted onto it (RealizeRange, again with this fallback). At most MaxSelfSplits per chain.</summary>
        private bool SelfSplit(Elem e, GradePath path, Piece pc, ref int segment, RangeState st)
        {
            if (st.SelfSplits >= MaxSelfSplits)
                return false;
            // The longest prefix [pc.S, m] whose 10 m ribbon is clear and passes the checks (binary search: a prefix that crosses itself
            // stays crossed when it grows)
            int first = pc.S + 1;
            while (first < pc.E && path.S[first] - path.S[pc.S] < TrackStubM)
                first++;
            int a = first, b = pc.E - 1, best = -1;
            float2[] bestRing = null;
            while (a <= b)
            {
                int m = (a + b) >> 1;
                float2[] ring = GradeRibbon.Build(SubPath(path, pc.S, m), TrackHalfWidthM, out GradeRibbonFail fail);
                if (fail == GradeRibbonFail.None && CheckTrack(ring) == AutoReject.Kept)
                {
                    best = m;
                    bestRing = ring;
                    a = m + 1;
                }
                else
                    b = m - 1;
            }
            if (best < 0)
                return false;
            st.SelfSplits++;
            GradePath pre = SubPath(path, pc.S, best), line = pre;
            if (pc.Orig0)
            {
                // the chain's start keeps its reach to a related element when the reached prefix is still clear
                int reaches = m_Rep.TrackReaches;
                GradePath ext = ReachEnds(e, pre, true, false);
                GradeRibbonFail fail = GradeRibbonFail.TooShort;
                float2[] ring = ext != pre ? GradeRibbon.Build(ext, TrackHalfWidthM, out fail) : null;
                if (ring != null && fail == GradeRibbonFail.None && CheckTrack(ring) == AutoReject.Kept)
                {
                    line = ext;
                    bestRing = ring;
                }
                else
                    m_Rep.TrackReaches = reaches;
            }
            int idx = AcceptTrack(e, line, bestRing, TrackHalfWidthM, pc.J0, new List<int>(), pc.Orig0, false, segment++);
            m_Rep.TrackKmOwn += pre.LengthM / 1000.0;
            m_Rep.TrackSelfSplits++;
            m_Rep.Lines.Add($"track {e.Source} ({e.Kind}): crosses itself, split at ({path.Xz[best].x:F0}, {path.Xz[best].y:F0}) after {pre.LengthM:F0} m, the rest butts onto it");
            // The rest against the lattice that now holds the prefix
            int n = path.Count;
            var cause = new byte[n];
            var owner = new int[n];
            for (int i = best + 1; i <= pc.E; i++)
                cause[i] = FreeTrack(path.Xz[i], out owner[i]);
            RealizeRange(e, path, cause, owner, best + 1, pc.E, false, pc.Orig1, new List<int> { idx }, pc.J1, ref segment, st);
            return true;
        }

        /// <summary>R2-r (DECISIONS 637): a chain with no own piece that runs in the corridor of accepted tracks merges into them. The tracks
        /// that hold its corridor (its hosts) carry the shared stretch, so the chain adds no area. Its element takes the hosts' areas as its
        /// own: a later track or piste that the planner relates to this chain reaches and links to the hosts, and the relation gate tests
        /// the chain's own relations on the hosts (RelationGate). A chain held only by existing tracks merges into them with no area.</summary>
        private bool MergeChain(Elem e, GradePath path, byte[] cause, int[] owner, float shared)
        {
            var hosts = new List<int>();
            bool existing = false;
            for (int i = 0; i < path.Count; i++)
            {
                if (cause[i] != CTrack || owner[i] < 0)
                    continue;
                if (m_T[owner[i]].Existing)
                    existing = true;
                else if (!hosts.Contains(owner[i]))
                    hosts.Add(owner[i]);
            }
            if (hosts.Count == 0 && !existing)
                return false;
            e.Merged = true;
            e.Areas.AddRange(hosts);
            m_Rep.TrackMerged++;
            m_Rep.TrackKmShared += shared / 1000.0;
            m_Rep.TrackKmMerged += path.LengthM / 1000.0;
            m_Rep.Lines.Add($"track {e.Source} ({e.Kind}) at ({path.Xz[0].x:F0}, {path.Xz[0].y:F0}) merged into {HostNames(e)}{CorridorNote(e, path, cause, owner)}");
            return true;
        }

        /// <summary>R2-r: after the track pass, every planner relation of a merged chain that its hosts do not link gets a spur: a short
        /// straight track that leaves the corridor it starts in and stops at the game link to the other side. Two kinds: (1) the merged
        /// chain's own end (it starts off or ends onto an element) toward that element's nearest kept area, (2) the source end of a kept
        /// track that starts off or ends onto the merged chain toward the nearest host. A spur is an area of the merged chain. Pistes
        /// that relate to a merged chain reach its hosts in ReachPisteEnds, which runs after this pass.</summary>
        private void MergedSpurs()
        {
            foreach (Elem b in m_E)
            {
                if (!b.Merged || b.Areas.Count == 0 || Canceled)
                    continue;
                var hosts = new List<int>(b.Areas);
                for (int s = 0; s < 2; s++)
                {
                    float2 endPt = s == 0 ? b.Pts[0] : b.Pts[b.Pts.Count - 1];
                    foreach (Elem c in m_E)
                    {
                        if (c == b || c.Areas.Count == 0 || !(s == 0 ? StartsOff(b, c) : EndsOnto(b, c)))
                            continue;
                        Area near = NearArea(c, endPt, out bool linked, b.Areas);
                        if (near != null && !linked)
                            Spur(b, endPt, hosts, near);
                    }
                }
                for (int x = 0; x < m_T.Count; x++)
                {
                    Area xa = m_T[x];
                    if (xa.Existing || xa.Elem == b.Id || m_E[xa.Elem].Merged)
                        continue;
                    Elem a = m_E[xa.Elem];
                    for (int s = 0; s < 2; s++)
                    {
                        if (!(s == 0 ? xa.Orig0 : xa.Orig1) || !(s == 0 ? StartsOff(a, b) : EndsOnto(a, b)))
                            continue;
                        float2 endPt = s == 0 ? a.Pts[0] : a.Pts[a.Pts.Count - 1];
                        Area near = NearArea(b, endPt, out bool linked, new List<int> { x });
                        if (near != null && !linked)
                            Spur(b, s == 0 ? xa.Line[0] : xa.Line[xa.Line.Length - 1], new List<int> { x }, near);
                    }
                }
            }
        }

        /// <summary>The kept area of c nearest to p within RelationM + JunctionReachM (the relation gate's reach), and whether any area of c
        /// is already a game link to one of the given track areas.</summary>
        private Area NearArea(Elem c, float2 p, out bool linked, List<int> tracks)
        {
            linked = false;
            Area best = null;
            float bd = RelationM + JunctionReachM;
            foreach (int k in c.Areas)
            {
                Area y = AreaOf(c, k);
                if (y.Existing)
                    continue;
                foreach (int t in tracks)
                    linked |= (c.Type == ElemType.Track && k == t) || LiftServedRule.OutlinesLinked(m_T[t].Ring, y.Ring);
                float d = GradeRaster.Inside(y.Ring, p) ? 0f : PlanGeometry.PointRingDistance(y.Ring, p);
                if (d <= bd)
                {
                    bd = d;
                    best = y;
                }
            }
            return best;
        }

        /// <summary>One spur (MergedSpurs): from p straight toward the nearest point of the target's centerline, in SampleM steps. The
        /// first samples in the gap of a start track are skipped (the spur butts onto it). It stops at the first sample within
        /// TrackReachM of the target's outline or in the target's gap. Any other blocked sample ends it without a spur. The ring must
        /// pass the track checks and be a game link at both ends.</summary>
        private int Spur(Elem b, float2 p, List<int> start, Area target)
        {
            if (target.Line == null)
                return -1;
            float2 aim = target.Line[0];
            float bd = float.PositiveInfinity;
            for (int k = 0; k + 1 < target.Line.Length; k++)
            {
                float2 a = target.Line[k], d = target.Line[k + 1] - a;
                float len2 = math.lengthsq(d);
                float2 c = a + d * (len2 > 0f ? math.saturate(math.dot(p - a, d) / len2) : 0f);
                float dd = math.distancesq(p, c);
                if (dd < bd)
                {
                    bd = dd;
                    aim = c;
                }
            }
            float dist = math.distance(p, aim);
            if (dist < SampleM)
                return -1;
            float2 dir = (aim - p) / dist;
            int tIdx = target.Track ? m_T.IndexOf(target) : -1;
            var pts = new List<float2>();
            bool reached = false;
            string block = "the target is out of reach";
            for (float t = 0f; t <= dist + 0.01f; t += SampleM)
            {
                float2 q = p + dir * t;
                byte cause = FreeTrack(q, out int own);
                if (cause == CTrack && pts.Count == 0 && start.Contains(own))
                    continue;   // still in the gap of the track it starts from
                if (cause == CTrack && own == tIdx)
                {
                    reached = true;   // in the target's gap: the butt joint is the link
                    break;
                }
                if (cause != CFree)
                {
                    block = (cause == CTrack ? $"the gap of track {GapOwner(own)}" : $"cause {cause}") + $" after {t:F0} m";
                    break;
                }
                pts.Add(q);
                if (Linked(q, target))
                {
                    reached = true;
                    break;
                }
            }
            string what = $"track {b.Source} ({b.Kind}): spur from ({p.x:F0}, {p.y:F0}) to {(target.Track ? "track" : "piste")} {m_E[target.Elem].Source}";
            GradePath path = reached && pts.Count >= 3 ? GradePath.Build(pts, SampleM) : null;
            GradeRibbonFail fail = GradeRibbonFail.TooShort;
            float2[] ring = path != null ? GradeRibbon.Build(path, TrackHalfWidthM, out fail) : null;
            bool ok = ring != null && fail == GradeRibbonFail.None && CheckTrack(ring) == AutoReject.Kept &&
                LiftServedRule.OutlinesLinked(ring, target.Ring);
            if (ok)
            {
                bool startLinked = false;
                foreach (int s in start)
                    startLinked |= LiftServedRule.OutlinesLinked(ring, m_T[s].Ring);
                ok = startLinked;
            }
            if (!ok)
            {
                m_Rep.Lines.Add(what + (reached ? " not built (its ring fails a check)" : $" not built ({block})"));
                return -1;
            }
            var j1 = new List<int>();
            if (tIdx >= 0)
                j1.Add(tIdx);
            int idx = AcceptTrack(b, path, ring, TrackHalfWidthM, start, j1, false, false, SpurSegment + b.Spurs++);
            m_Rep.TrackSpurs++;
            m_Rep.Lines.Add($"{what}, {path.LengthM:F0} m");
            return idx;
        }

        /// <summary>Report only: the sources of a merged chain's hosts.</summary>
        private string HostNames(Elem e)
        {
            // e.Areas holds only this scan's areas (AcceptTrack, MergeChain's hosts): never an existing track
            var names = new List<string>();
            foreach (int k in e.Areas)
            {
                string s = $"track {m_E[m_T[k].Elem].Source}";
                if (!names.Contains(s))
                    names.Add(s);
            }
            return names.Count > 0 ? string.Join(", ", names) : "an existing track";
        }

        /// <summary>Report only: the share of a chain's centerline that lies in the gap of accepted tracks, which tracks hold it (parent,
        /// same tree, other tree, link), and how far the chain strays from their centerlines.</summary>
        private string CorridorNote(Elem e, GradePath path, byte[] cause, int[] owner)
        {
            int n = path.Count, inGap = 0;
            float far = 0f;
            var who = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (cause[i] != CTrack || owner[i] < 0)
                    continue;
                inGap++;
                Area o = m_T[owner[i]];
                if (!who.Contains(owner[i]))
                    who.Add(owner[i]);
                if (o.Line != null)
                    far = Math.Max(far, LineDistance(o.Line, path.Xz[i]));
            }
            if (inGap == 0)
                return "";
            var sb = new System.Text.StringBuilder();
            sb.Append($"; corridor {100f * inGap / n:F0} % of {path.LengthM:F0} m, at most {far:F0} m from the centerline of");
            var seen = new List<int>();
            foreach (int k in who)
            {
                Area o = m_T[k];
                if (o.Existing)
                {
                    sb.Append(" an existing track");
                    continue;
                }
                if (seen.Contains(o.Elem))
                    continue;
                seen.Add(o.Elem);
                Elem b = m_E[o.Elem];
                string rel = b.Id == e.Parent ? "parent" : b.Link || e.Link ? "link" : b.Lift == e.Lift && b.Kind == "tree" && e.Kind == "tree" ? "same lift" : "other tree";
                sb.Append($" track {b.Source} ({rel})");
            }
            return sb.ToString();
        }

        private static float LineDistance(float2[] line, float2 p)
        {
            float bd = float.PositiveInfinity;
            for (int k = 0; k + 1 < line.Length; k++)
            {
                float2 a = line[k], d = line[k + 1] - a;
                float len2 = math.lengthsq(d);
                float2 c = a + d * (len2 > 0f ? math.saturate(math.dot(p - a, d) / len2) : 0f);
                bd = Math.Min(bd, math.distancesq(p, c));
            }
            return line.Length == 1 ? math.distance(line[0], p) : (float)Math.Sqrt(bd);
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
            GradePath plain = SubPath(path, pc.S, pc.E);
            int reaches = m_Rep.TrackReaches;
            GradePath sub = ReachEnds(e, plain, pc.Orig0, pc.Orig1);
            AutoReject why = BuildTrackRing(e, path, pc, sub, ref segment, out at);
            if (why != AutoReject.Kept && sub != plain)
            {
                m_Rep.TrackReaches = reaches;
                // R2-r: both ends reached and the piece fails: it keeps one reached end, its end first (a track mostly ends onto what
                // it serves), then its start
                for (int k = 0; k < 2 && pc.Orig0 && pc.Orig1; k++)
                {
                    GradePath one = ReachEnds(e, plain, k == 1, k == 0);
                    if (one == plain || one.Count == sub.Count)
                    {
                        m_Rep.TrackReaches = reaches;
                        continue;   // that end was not reached, or it was the only one reached
                    }
                    if (BuildTrackRing(e, path, pc, one, ref segment, out at) == AutoReject.Kept)
                    {
                        m_Rep.Lines.Add($"track {e.Source} ({e.Kind}): the piece failed with both reached ends ({why}), kept with its reached {(k == 0 ? "end" : "start")}");
                        return AutoReject.Kept;
                    }
                    m_Rep.TrackReaches = reaches;
                }
                // the piece with its reached ends fails: it goes without them (iteration 3), the reach is not counted
                m_Rep.Lines.Add($"track {e.Source} ({e.Kind}): reached ends dropped, the piece failed with them ({why})");
                int before = e.Areas.Count;
                why = BuildTrackRing(e, path, pc, plain, ref segment, out at);
                if (why == AutoReject.Kept)
                {
                    // R2-r: each end that had a reach gets it as a spur, a separate area butted onto the piece (a straight extension
                    // joined to a bent end is what made the ribbon cross itself)
                    for (int s = 0; s < 2; s++)
                    {
                        if (!(s == 0 ? pc.Orig0 : pc.Orig1))
                            continue;
                        int idx = e.Areas[s == 0 ? before : e.Areas.Count - 1];
                        Area x = m_T[idx];
                        float2 end = s == 0 ? x.Line[0] : x.Line[x.Line.Length - 1];
                        Area target = ReachTarget(e, end, s == 0, out Elem _);
                        int spur = target != null ? Spur(e, end, new List<int> { idx }, target) : -1;
                        if (spur >= 0)
                            (s == 0 ? x.J0 : x.J1).Add(spur);
                    }
                }
            }
            return why;
        }

        private AutoReject BuildTrackRing(Elem e, GradePath path, Piece pc, GradePath sub, ref int segment, out float2 at)
        {
            at = path.Xz[pc.S];
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
            Area target = ReachTarget(e, end, start, out Elem tb);
            if (target == null)
                return null;
            return ReachToward(e, end, inner, start, target, tb);
        }

        /// <summary>The related element with no kept area within reach of this end, nearest first: its nearest kept area, or null.</summary>
        private Area ReachTarget(Elem e, float2 end, bool start, out Elem tb)
        {
            Area target = null;
            tb = null;
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
                    continue;   // already a link to this element: nothing to reach for it (iteration 3: the other related elements still count)
                if (near < best && near <= TrackReachM + ExtendMaxM)
                {
                    best = near;
                    target = nearA;
                    tb = b;
                }
            }
            return target;
        }

        private List<float2> ReachToward(Elem e, float2 end, float2 inner, bool start, Area target, Elem tb)
        {
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
                {
                    if (add.Count > 0 && Linked(add[add.Count - 1], target))
                        return add;
                    m_Rep.Lines.Add($"track {e.Source} ({e.Kind}): {(start ? "start" : "end")} cannot reach {(tb.Type == ElemType.Track ? "track" : "piste")} {tb.Source}: " +
                        (cause == CTrack && own >= 0 ? $"the gap of track {GapOwner(own)}" : $"cause {cause}") + $" after {t - SampleM:F0} m");
                    return null;
                }
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
