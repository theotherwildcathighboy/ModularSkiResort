// Resort Planner (v2) stage A: pod route and pods (spec 4.2). Stages PodRoute and Pods, Resort mode only, between Terrain and Lifts.
// PodRoute: one labeled reverse Dijkstra from the cells of every station-capable pad (InscribedM >= the cheapest station's half
// diagonal) with the v1 StepCost (floor 0.08, ceiling = hard ceiling 0.70: the Ecosign "70 %+ unskiable" band), so each reached cell
// carries the pad its least-cost descent reaches first (m_PodOf as the label scratch, m_Dist[0] the cost; StartPassRows resets it).
// Pods: a pad whose drop to the pad below is under PodMinVerticalM merges into it (union-find, Root); a pod = a 4-connected component
// of a surviving pad's catchment minus ridge cells; small components join the same pad's neighbor with the longest shared border or
// drop; per pod the statistics of PlanPod; at most MaxPodsOf(area) pods, sorted by (LowerPad.MinY, top cell). m_PodOf ends as the pod
// id layer (MUnreached = none). Scratch m_Order (parents, then component labels; TopRows refills it in Seeds).
// Pure and Entities-free (usings System, System.Collections.Generic, Unity.Mathematics only). Spec: docs/research/quickruns-v2/spec/stage-a-spec.md.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunAutoPlanner
    {
        // ---------------------------------------------------------------- constants (spec 4.2; moonshot 5.2.2, [B 1a])
        public const float PodRouteFloor = 0.08f, PodHardGrade = 0.70f;     // StepCost floor / ceiling = hard ceiling
        public const float PodMinVerticalM = 80f, PodMinAreaM2 = 20000f;    // 80 m of vertical, 2 ha
        public const float DevelopableForest = 0.30f, DevelopableOpen = 0.50f, DevelopableUnknown = 0.35f;
        public const float DevelopableSmoothBonus = 0.2f, DevelopableMax = 0.90f, SmoothBandShare = 0.80f;
        public const float PrimaryClassQuantile = 0.80f;

        /// <summary>Pods kept for a region of this area: round(4 x AreaScale) (4 at 1 km2, 12 at 9 km2).</summary>
        public static int MaxPodsOf(float areaM2) => (int)Math.Round(4f * AreaScale(areaM2));

        /// <summary>The Ecosign class of a pod: the band of the PrimaryClassQuantile grade of its skiable cells, from the per-band counts
        /// (the k-th smallest grade, k = floor(q (n - 1)), lies in the first band whose cumulative count exceeds k; bands are monotone
        /// in the grade, so this equals sorting the grades). Public for the host's S-2 check.</summary>
        public static TerrainBand PrimaryClassOf(int[] bandCounts, int offset)
        {
            int n = 0;
            for (int b = 0; b < 7; b++)
                n += bandCounts[offset + b];
            if (n == 0)
                return TerrainBand.Flat;
            int k = (int)Math.Floor(PrimaryClassQuantile * (n - 1)), cum = 0;
            for (int b = 0; b < 7; b++)
            {
                cum += bandCounts[offset + b];
                if (cum > k)
                    return (TerrainBand)b;
            }
            return TerrainBand.Cliff;
        }

        // ---------------------------------------------------------------- state
        private struct PodComp
        {
            public int First, Key, Cells;      // smallest cell, the surviving lower pad, cell count
            public float MinY, MaxY;
            public int Parent;                 // merge union-find over components (the smaller index is the root)
        }

        private readonly List<PodComp> m_PodComps = new List<PodComp>();
        private readonly List<long> m_Borders = new List<long>();      // (a << 32 | b), a < b: one entry per shared border incidence
        private readonly List<PlanPod> m_PodList = new List<PlanPod>();
        private readonly int[] m_PadRoot = new int[MaxPads], m_PadDown = new int[MaxPads], m_PadDownCell = new int[MaxPads];
        private readonly float[] m_PadDownY = new float[MaxPads];
        private readonly List<int> m_PadFirst = new List<int>();       // pad id -> its smallest cell (the pod key)
        private int[] m_PodStat = new int[64], m_PodBands = new int[64 * 7], m_PodOfComp = new int[64];
        private double[] m_PodGrade = new double[64];
        private ulong[] m_PodPads = new ulong[64 * 4];                 // per component: the pads its cells touch (256-bit set)
        private int[] m_AdjStart = new int[64], m_AdjFill = new int[64], m_Adj = new int[64], m_AdjLen = new int[64];
        private int[] m_MemberNext = new int[64], m_MemberLast = new int[64], m_Acc = new int[64];
        private readonly List<int> m_PodTouched = new List<int>();

        // m_PodStat per component (stride PodStatStride): skiable cells, cells above the tree line, the top cell (-1 = none)
        private const int PodStatStride = 3;

        /// <summary>Pods of the last Resort scan, in id order; empty in Runs mode.</summary>
        public int PodCount => m_PodList.Count;

        private bool PadCapable(int pad) => pad < m_PadList.Count && m_PadList[pad].InscribedM >= StationHalfDiagonalM[0];

        // ---------------------------------------------------------------- stage PodRoute

        /// <summary>Stage PodRoute (Resort only). m_Cursor2 0: rows over the region bbox reset the cost and push every capable pad's cells
        /// (label = the pad); 1: RoutePops heap pops per unit, the label rides along each relaxation. Then Pods.</summary>
        private void UnitPodRoute()
        {
            float[] dist = m_Dist[0];
            if (m_Cursor2 == 0)
            {
                if (m_Cursor == 0)
                {
                    m_Heap.Clear();
                    m_Cursor = m_RegionJ0;
                }
                int jEnd = Math.Min(m_RegionJ1 + 1, m_Cursor + RowsPerUnit);
                for (int j = m_Cursor; j < jEnd; j++)
                    for (int i = m_RegionI0; i <= m_RegionI1; i++)
                    {
                        int c = j * m_Nx + i;
                        dist[c] = float.PositiveInfinity;
                        byte pad = m_PadOf[c];
                        if (pad == PadNone || !PadCapable(pad) || !(m_O[c] < PodHardGrade) || m_DEdge[c] < 0.5f * EdgeSeedMarginM)
                            continue;
                        dist[c] = 0f;
                        m_PodOf[c] = pad;
                        m_Heap.Push(0f, c);
                    }
                m_Cursor = jEnd;
                if (m_Cursor > m_RegionJ1)
                    m_Cursor2 = 1;
                return;
            }
            float t = m_Texel, inv2t = 1f / (2f * m_Texel);
            int budget = RoutePops;
            while (budget-- > 0)
            {
                if (!m_Heap.Pop(out float du, out int u))
                {
                    Next(Stage.Pods);
                    return;
                }
                if (du > dist[u])
                    continue;
                ushort lu = m_PodOf[u];
                int ui = u % m_Nx, uj = u / m_Nx;
                for (int d = 0; d < 8; d++)
                {
                    int vi = ui + DirX[d], vj = uj + DirZ[d];
                    if (vi < 1 || vj < 1 || vi >= m_Nx - 1 || vj >= m_Nz - 1)
                        continue;
                    int v = vj * m_Nx + vi;
                    float w = StepCost(u, v, d, PodRouteFloor, PodHardGrade, PodHardGrade, t, inv2t);
                    if (!(w >= 0f))
                        continue;
                    float dv = du + w;
                    if (dv < dist[v])
                    {
                        dist[v] = dv;
                        m_PodOf[v] = lu;
                        m_Heap.Push(dv, v);
                    }
                }
            }
        }

        // ---------------------------------------------------------------- stage Pods

        /// <summary>Stage Pods (Resort only). m_Cursor2 phases: 0 setup; 1 rows: each pad's lowest outside neighbor with another label
        /// (Down); 2 one unit: the 80 m merge; 3 rows: union of catchment cells (4-connected, same surviving pad, ridge cells out);
        /// 4 rows: labels and component sums; 5 rows: shared borders (across ridge cells, and diagonal touches); 6 one unit: sort the
        /// borders; 7: the small-component merge (one component per unit); 8 rows: pod statistics per merged group; 9 one unit: the pod
        /// list; 10 rows: the pod id layer. Then Lifts.</summary>
        private void UnitPods()
        {
            switch (m_Cursor2)
            {
                case 0:
                    m_PodComps.Clear();
                    m_Borders.Clear();
                    m_PodList.Clear();
                    for (int p = 0; p < MaxPads; p++)
                    {
                        m_PadRoot[p] = p;
                        m_PadDown[p] = -1;
                        m_PadDownCell[p] = int.MaxValue;
                        m_PadDownY[p] = float.PositiveInfinity;
                    }
                    m_Cursor = m_RegionJ0;
                    m_Cursor2 = 1;
                    return;
                case 1:
                    PodDrainRows();
                    return;
                case 2:
                    PodMerge();
                    m_Cursor = m_RegionJ0;
                    m_Cursor2 = 3;
                    return;
                case 3:
                    PodUnionRows();
                    return;
                case 4:
                    PodLabelRows();
                    return;
                case 5:
                    PodBorderRows();
                    return;
                case 6:
                    PodAdjacency();
                    m_Cursor = 0;
                    m_Cursor2 = 7;
                    return;
                case 7:
                    PodSmallMerge();
                    return;
                case 8:
                    PodStatRows();
                    return;
                case 9:
                    BuildPods();
                    m_Cursor = m_RegionJ0;
                    m_Cursor2 = 10;
                    return;
                default:
                    PodIdRows();
                    return;
            }
        }

        /// <summary>The surviving lower pad of a catchment cell (-1 when the cell has no label).</summary>
        private int PodKeyOf(int c)
        {
            ushort l = m_PodOf[c];
            return l == MUnreached ? -1 : Root(m_PadRoot, l);
        }

        /// <summary>A cell that belongs to a pod component: labeled, not a cell of its own lower pad (the pod is the ground ABOVE its
        /// bottom station site: two faces draining onto one pad stay two pods), and not a ridge cell (other pads keep their ridge cells).</summary>
        private bool PodCell(int c)
        {
            ushort l = m_PodOf[c];
            if (l == MUnreached)
                return false;
            byte pad = m_PadOf[c];
            if (pad == PadNone)
                return m_Ridge[c] == 0;
            return pad != Root(m_PadRoot, l);
        }

        // Phase 1, rows: Down(P) = the label of P's lowest outside 8-neighbor that carries another label (its descent; ties: the smaller
        // cell). Only capable pads are labels, so Down(P) is always a possible lift bottom.
        private void PodDrainRows()
        {
            int jEnd = Math.Min(m_RegionJ1 + 1, m_Cursor + RowsPerUnit);
            for (int j = m_Cursor; j < jEnd; j++)
                for (int i = m_RegionI0; i <= m_RegionI1; i++)
                {
                    int c = j * m_Nx + i;
                    byte pad = m_PadOf[c];
                    if (pad == PadNone || !PadCapable(pad))
                        continue;
                    for (int d = 0; d < 8; d++)
                    {
                        int n = c + DirZ[d] * m_Nx + DirX[d];
                        ushort l = m_PodOf[n];
                        if (m_PadOf[n] == pad || l == MUnreached || l == pad)
                            continue;
                        float h = m_H[n];
                        if (h < m_PadDownY[pad] || (h == m_PadDownY[pad] && n < m_PadDownCell[pad]))
                        {
                            m_PadDownY[pad] = h;
                            m_PadDownCell[pad] = n;
                            m_PadDown[pad] = l;
                        }
                    }
                }
            m_Cursor = jEnd;
            if (m_Cursor > m_RegionJ1)
                m_Cursor2 = 2;
        }

        // Phase 2: pads in ascending MinY (ties: id); P.MinY - Down(P).MaxY under PodMinVerticalM merges P into Down(P). The root of a
        // merged set is its lowest pad (ties: the smaller id), so the survivor is the lift bottom.
        private void PodMerge()
        {
            int n = m_PadList.Count;
            m_PadOrder.Clear();
            for (int p = 0; p < n; p++)
                if (PadCapable(p))
                    m_PadOrder.Add(p);
            m_PadOrder.Sort((a, b) =>
            {
                int byY = m_PadList[a].MinY.CompareTo(m_PadList[b].MinY);
                return byY != 0 ? byY : a.CompareTo(b);
            });
            for (int k = 0; k < m_PadOrder.Count; k++)
            {
                int p = m_PadOrder[k], down = m_PadDown[p];
                if (down < 0 || m_PadList[p].MinY - m_PadList[down].MaxY >= PodMinVerticalM)
                    continue;
                int a = Root(m_PadRoot, p), b = Root(m_PadRoot, down);
                if (a == b)
                    continue;
                bool aLower = m_PadList[a].MinY < m_PadList[b].MinY || (m_PadList[a].MinY == m_PadList[b].MinY && a < b);
                if (aLower)
                    m_PadRoot[b] = a;
                else
                    m_PadRoot[a] = b;
            }
        }

        // Phase 3, rows: union of pod cells with their W and N neighbors of the same surviving pad (4-connected: a ridge line one cell
        // wide, diagonal steps included, separates)
        private void PodUnionRows()
        {
            int[] p = m_Order;
            int jEnd = Math.Min(m_RegionJ1 + 1, m_Cursor + RowsPerUnit);
            for (int j = m_Cursor; j < jEnd; j++)
                for (int i = m_RegionI0; i <= m_RegionI1; i++)
                {
                    int c = j * m_Nx + i;
                    if (!PodCell(c))
                        continue;
                    p[c] = c;
                    int key = PodKeyOf(c);
                    if (i > m_RegionI0)
                        PodUnion(p, c, c - 1, key);
                    if (j > m_RegionJ0)
                        PodUnion(p, c, c - m_Nx, key);
                }
            m_Cursor = jEnd;
            if (m_Cursor > m_RegionJ1)
            {
                m_Cursor = m_RegionJ0;
                m_Cursor2 = 4;
            }
        }

        private void PodUnion(int[] p, int c, int n, int key)
        {
            if (!PodCell(n) || PodKeyOf(n) != key)
                return;
            int a = Root(p, c), b = Root(p, n);
            if (a != b)
                p[Math.Max(a, b)] = Math.Min(a, b);
        }

        // Phase 4, rows ascending: compact labels -(k + 1) (TerrainLabelRows' flatten) and the component sums
        private void PodLabelRows()
        {
            int[] p = m_Order;
            int jEnd = Math.Min(m_RegionJ1 + 1, m_Cursor + RowsPerUnit);
            for (int j = m_Cursor; j < jEnd; j++)
                for (int i = m_RegionI0; i <= m_RegionI1; i++)
                {
                    int c = j * m_Nx + i;
                    if (!PodCell(c))
                        continue;
                    int k;
                    if (p[c] == c)
                    {
                        k = m_PodComps.Count;
                        m_PodComps.Add(new PodComp { First = c, Key = PodKeyOf(c), MinY = float.PositiveInfinity, MaxY = float.NegativeInfinity, Parent = k });
                        p[c] = -(k + 1);
                    }
                    else
                    {
                        p[c] = p[p[c]];
                        k = -p[c] - 1;
                    }
                    PodComp pc = m_PodComps[k];
                    pc.Cells++;
                    pc.MinY = Math.Min(pc.MinY, m_H[c]);
                    pc.MaxY = Math.Max(pc.MaxY, m_H[c]);
                    m_PodComps[k] = pc;
                }
            m_Cursor = jEnd;
            if (m_Cursor <= m_RegionJ1)
                return;
            int nc = m_PodComps.Count;
            Grow(ref m_PodStat, nc * PodStatStride);
            Grow(ref m_PodBands, nc * 7);
            Grow(ref m_PodGrade, nc);
            Grow(ref m_PodOfComp, nc);
            Grow(ref m_PodPads, nc * 4);
            m_Cursor = m_RegionJ0;
            m_Cursor2 = 5;
        }

        /// <summary>The component label of a pod cell after phase 4 (-1 for any other cell).</summary>
        private int PodCompOf(int c) => PodCell(c) ? -m_Order[c] - 1 : -1;

        // Phase 5, rows: a ridge cell (not a pad cell) borders every pair of distinct same-pad components among its 8 neighbors; two
        // components touching only diagonally border too (SE and SW of each cell)
        private void PodBorderRows()
        {
            Span8 seen = default;
            int jEnd = Math.Min(m_RegionJ1 + 1, m_Cursor + RowsPerUnit);
            for (int j = m_Cursor; j < jEnd; j++)
                for (int i = m_RegionI0; i <= m_RegionI1; i++)
                {
                    int c = j * m_Nx + i;
                    int k = PodCompOf(c);
                    if (k >= 0)
                    {
                        if (j < m_RegionJ1)
                        {
                            if (i < m_RegionI1)
                                AddBorder(k, PodCompOf(c + m_Nx + 1));
                            if (i > m_RegionI0)
                                AddBorder(k, PodCompOf(c + m_Nx - 1));
                        }
                        continue;
                    }
                    if (m_PodOf[c] == MUnreached || m_Ridge[c] == 0)
                        continue;
                    int key = PodKeyOf(c);
                    seen.Count = 0;
                    for (int d = 0; d < 8; d++)
                    {
                        int nk = PodCompOf(c + DirZ[d] * m_Nx + DirX[d]);
                        if (nk >= 0 && m_PodComps[nk].Key == key)
                            seen.Add(nk);
                    }
                    for (int a = 0; a < seen.Count; a++)
                        for (int b = a + 1; b < seen.Count; b++)
                            AddBorder(seen[a], seen[b]);
                }
            m_Cursor = jEnd;
            if (m_Cursor > m_RegionJ1)
                m_Cursor2 = 6;
        }

        private void AddBorder(int a, int b)
        {
            if (b < 0 || a == b || m_PodComps[a].Key != m_PodComps[b].Key)
                return;
            m_Borders.Add(((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b));
        }

        /// <summary>Up to 8 distinct ints without an allocation (the ridge cell's neighbor components).</summary>
        private struct Span8
        {
            private int m_A, m_B, m_C, m_D, m_E, m_F, m_G, m_H;
            public int Count;

            public int this[int i]
            {
                get
                {
                    switch (i)
                    {
                        case 0: return m_A;
                        case 1: return m_B;
                        case 2: return m_C;
                        case 3: return m_D;
                        case 4: return m_E;
                        case 5: return m_F;
                        case 6: return m_G;
                        default: return m_H;
                    }
                }
                private set
                {
                    switch (i)
                    {
                        case 0: m_A = value; break;
                        case 1: m_B = value; break;
                        case 2: m_C = value; break;
                        case 3: m_D = value; break;
                        case 4: m_E = value; break;
                        case 5: m_F = value; break;
                        case 6: m_G = value; break;
                        default: m_H = value; break;
                    }
                }
            }

            public void Add(int v)
            {
                for (int k = 0; k < Count; k++)
                    if (this[k] == v)
                        return;
                if (Count < 8)
                    this[Count++] = v;
            }
        }

        private int CompRoot(int k)
        {
            while (m_PodComps[k].Parent != k)
                k = m_PodComps[k].Parent;
            return k;
        }

        private bool PodSmall(int root)
        {
            PodComp pc = m_PodComps[root];
            return pc.Cells * m_Texel * m_Texel < PodMinAreaM2 || pc.MaxY - pc.MinY < PodMinVerticalM;
        }

        // Phase 6, one unit: the sorted border list as runs (one per component pair, its length = the incidences) into an adjacency
        // list per component (both directions), and one member list per merge group
        private void PodAdjacency()
        {
            int nc = m_PodComps.Count;
            m_Borders.Sort();
            Grow(ref m_AdjStart, nc + 1);
            Grow(ref m_AdjFill, nc);
            Grow(ref m_MemberNext, nc);
            Grow(ref m_MemberLast, nc);
            Grow(ref m_Acc, nc);
            Array.Clear(m_AdjStart, 0, nc + 1);
            Array.Clear(m_Acc, 0, nc);
            int nb = m_Borders.Count, edges = 0;
            for (int s = 0; s < nb; s = NextRun(s))
            {
                long key = m_Borders[s];
                m_AdjStart[(int)(key >> 32) + 1]++;
                m_AdjStart[(int)(key & 0xFFFFFFFFL) + 1]++;
                edges++;
            }
            for (int k = 0; k < nc; k++)
            {
                m_AdjStart[k + 1] += m_AdjStart[k];
                m_AdjFill[k] = m_AdjStart[k];
                m_MemberNext[k] = -1;
                m_MemberLast[k] = k;
            }
            Grow(ref m_Adj, 2 * edges);
            Grow(ref m_AdjLen, 2 * edges);
            for (int s = 0; s < nb;)
            {
                long key = m_Borders[s];
                int e = NextRun(s), len = e - s, a = (int)(key >> 32), b = (int)(key & 0xFFFFFFFFL);
                m_Adj[m_AdjFill[a]] = b;
                m_AdjLen[m_AdjFill[a]++] = len;
                m_Adj[m_AdjFill[b]] = a;
                m_AdjLen[m_AdjFill[b]++] = len;
                s = e;
            }
        }

        private int NextRun(int s)
        {
            long key = m_Borders[s];
            int e = s + 1;
            while (e < m_Borders.Count && m_Borders[e] == key)
                e++;
            return e;
        }

        // Phase 7: components in ascending order (= smallest cell); one whose merge group is still small joins the neighbor group of the
        // same pad with the longest shared border (summed over the group's members; ties: the smaller root), the smaller root wins.
        // At most one merge or 256 checks per unit.
        private void PodSmallMerge()
        {
            int nc = m_PodComps.Count;
            for (int checks = 0; checks < 256 && m_Cursor < nc; checks++)
            {
                int g = CompRoot(m_Cursor++);
                if (!PodSmall(g))
                    continue;
                m_PodTouched.Clear();
                for (int m = g; m >= 0; m = m_MemberNext[m])
                    for (int e = m_AdjStart[m]; e < m_AdjStart[m + 1]; e++)
                    {
                        int o = CompRoot(m_Adj[e]);
                        if (o == g)
                            continue;
                        if (m_Acc[o] == 0)
                            m_PodTouched.Add(o);
                        m_Acc[o] += m_AdjLen[e];
                    }
                int best = -1;
                for (int t = 0; t < m_PodTouched.Count; t++)
                {
                    int o = m_PodTouched[t];
                    if (best < 0 || m_Acc[o] > m_Acc[best] || (m_Acc[o] == m_Acc[best] && o < best))
                        best = o;
                }
                for (int t = 0; t < m_PodTouched.Count; t++)
                    m_Acc[m_PodTouched[t]] = 0;
                m_PodTouched.Clear();
                if (best < 0)
                    continue;   // no neighbor of the same pad: stays small, BuildPods drops it
                int root = Math.Min(g, best), child = Math.Max(g, best);
                PodComp r = m_PodComps[root], ch = m_PodComps[child];
                r.Cells += ch.Cells;
                r.MinY = Math.Min(r.MinY, ch.MinY);
                r.MaxY = Math.Max(r.MaxY, ch.MaxY);
                ch.Parent = root;
                m_PodComps[root] = r;
                m_PodComps[child] = ch;
                m_MemberNext[m_MemberLast[root]] = child;
                m_MemberLast[root] = m_MemberLast[child];
                break;
            }
            if (m_Cursor < nc)
                return;
            // Flatten the merge forest and clear the per-group sums for phase 8
            for (int k = 0; k < nc; k++)
            {
                PodComp pc = m_PodComps[k];
                pc.Parent = CompRoot(k);
                m_PodComps[k] = pc;
            }
            Array.Clear(m_PodStat, 0, nc * PodStatStride);
            Array.Clear(m_PodBands, 0, nc * 7);
            Array.Clear(m_PodGrade, 0, nc);
            Array.Clear(m_PodPads, 0, nc * 4);
            for (int k = 0; k < nc; k++)
                m_PodStat[k * PodStatStride + 2] = -1;
            m_Cursor = m_RegionJ0;
            m_Cursor2 = 8;
        }

        // Phase 8, rows: per merged group the skiable cells (under PodHardGrade), band counts, grade sum, cells above the tree line, the
        // top cell (the highest with DHard >= EdgeSeedMarginM; ties: the smaller cell) and the pads its cells are or touch
        private void PodStatRows()
        {
            float treeLine = m_In.TreeLineY;
            int jEnd = Math.Min(m_RegionJ1 + 1, m_Cursor + RowsPerUnit);
            for (int j = m_Cursor; j < jEnd; j++)
                for (int i = m_RegionI0; i <= m_RegionI1; i++)
                {
                    int c = j * m_Nx + i;
                    int k = PodCompOf(c);
                    if (k < 0)
                        continue;
                    int g = m_PodComps[k].Parent, s = g * PodStatStride;
                    float o = m_O[c];
                    if (o < PodHardGrade)
                    {
                        m_PodStat[s]++;
                        m_PodBands[g * 7 + m_Band[c]]++;
                        m_PodGrade[g] += o;
                    }
                    if (m_H[c] > treeLine)
                        m_PodStat[s + 1]++;
                    if (m_DHard[c] >= EdgeSeedMarginM)
                    {
                        int top = m_PodStat[s + 2];
                        if (top < 0 || m_H[c] > m_H[top])
                            m_PodStat[s + 2] = c;
                    }
                    for (int d = -1; d < 8; d++)
                    {
                        int n = d < 0 ? c : c + DirZ[d] * m_Nx + DirX[d];
                        byte pad = m_PadOf[n];
                        if (pad != PadNone)
                            m_PodPads[g * 4 + (pad >> 6)] |= 1UL << (pad & 63);
                    }
                }
            m_Cursor = jEnd;
            if (m_Cursor > m_RegionJ1)
                m_Cursor2 = 9;
        }

        // Phase 9, one unit: groups that are big enough and have a top cell PodMinVerticalM above their lowest cell become pods;
        // ordered by (LowerPad.MinY, top cell); over MaxPodsOf the smallest by SkiableAreaM2 drop first (ties: the later pod).
        private void BuildPods()
        {
            int nc = m_PodComps.Count;
            float cellArea = m_Texel * m_Texel, treeLine = m_In.TreeLineY;
            bool treeKnown = !float.IsNaN(treeLine);
            for (int g = 0; g < nc; g++)
            {
                m_PodOfComp[g] = -1;
                PodComp pc = m_PodComps[g];
                int s = g * PodStatStride, top = m_PodStat[s + 2];
                if (pc.Parent != g || PodSmall(g) || top < 0 || pc.Key < 0 || pc.Key >= m_PadList.Count)
                    continue;
                PlanPad lower = m_PadList[pc.Key];
                int skiable = m_PodStat[s];
                float topY = m_H[top], bottomY = pc.MinY, vertical = topY - bottomY;
                if (vertical < PodMinVerticalM)
                    continue;   // the top cell sits EdgeSeedMarginM inside: its vertical can fall under the group's cell range
                float2 topXz = CellCenter(top);
                float reach = math.distance(topXz, lower.Center);
                var share = new float[7];
                bool smooth = false;
                for (int b = 0; b < 7; b++)
                {
                    share[b] = skiable > 0 ? m_PodBands[g * 7 + b] / (float)skiable : 0f;
                    smooth |= share[b] > SmoothBandShare;
                }
                bool above = treeKnown && m_PodStat[s + 1] * 2 > pc.Cells;
                float dev = !treeKnown ? DevelopableUnknown : above ? DevelopableOpen : DevelopableForest;
                if (smooth)
                    dev = Math.Min(DevelopableMax, dev + DevelopableSmoothBonus);
                // UpperPad: the largest pad (ties: the smaller id) the pod's cells are or touch, other than its lower pad, whose lowest
                // cell lies in the pod's upper third of vertical (a bench at the pod's top: the stacking pad)
                int upper = -1;
                float upperLine = bottomY + vertical * (2f / 3f);
                for (int p = 0; p < m_PadList.Count; p++)
                {
                    if (p == pc.Key || (m_PodPads[g * 4 + (p >> 6)] & (1UL << (p & 63))) == 0 || m_PadList[p].MinY < upperLine)
                        continue;
                    if (upper < 0 || m_PadList[p].AreaM2 > m_PadList[upper].AreaM2)
                        upper = p;
                }
                m_PodList.Add(new PlanPod
                {
                    Id = g,   // the group for now; renumbered below
                    Key = Fnv(Fnv(2166136261u, (uint)m_PadFirst[pc.Key]), (uint)top),
                    LowerPad = pc.Key, UpperPad = upper,
                    TopCenter = topXz, TopY = topY, BottomY = bottomY, VerticalM = vertical, ReachM = reach,
                    StraightGrade = reach > 0f ? vertical / reach : 0f,
                    AreaM2 = pc.Cells * cellArea, SkiableAreaM2 = skiable * cellArea, DevelopableShare = dev,
                    PrimaryClass = PrimaryClassOf(m_PodBands, g * 7),
                    BandShare = share,
                    MeanFanGrade = skiable > 0 ? (float)(m_PodGrade[g] / skiable) : 0f,
                    AboveTreeLine = above,
                    Lift = -1, RunFirst = 0, RunCount = 0,
                });
            }
            int cap = MaxPodsOf(m_Result.RegionAreaM2);
            if (m_PodList.Count > cap)
            {
                // Rank by skiable area (ties: the earlier group) and keep the cap's best
                m_PadOrder.Clear();
                for (int k = 0; k < m_PodList.Count; k++)
                    m_PadOrder.Add(k);
                m_PadOrder.Sort((a, b) =>
                {
                    int byArea = m_PodList[b].SkiableAreaM2.CompareTo(m_PodList[a].SkiableAreaM2);
                    return byArea != 0 ? byArea : m_PodList[a].Id.CompareTo(m_PodList[b].Id);
                });
                var keep = new bool[m_PodList.Count];
                for (int k = 0; k < cap; k++)
                    keep[m_PadOrder[k]] = true;
                int w = 0;
                for (int k = 0; k < m_PodList.Count; k++)
                    if (keep[k])
                        m_PodList[w++] = m_PodList[k];
                m_PodList.RemoveRange(w, m_PodList.Count - w);
            }
            m_PodList.Sort((a, b) =>
            {
                int byY = m_PadList[a.LowerPad].MinY.CompareTo(m_PadList[b.LowerPad].MinY);
                if (byY != 0)
                    return byY;
                int ta = CellOfXz(a.TopCenter), tb = CellOfXz(b.TopCenter);
                return ta != tb ? ta.CompareTo(tb) : a.Id.CompareTo(b.Id);
            });
            for (int k = 0; k < m_PodList.Count; k++)
            {
                PlanPod pod = m_PodList[k];
                m_PodOfComp[pod.Id] = k;
                pod.Id = k;
                m_PodList[k] = pod;
            }
        }

        private int CellOfXz(float2 p) => Cell(p, out int c) ? c : int.MaxValue;

        // Phase 10, rows: the pod id layer (MUnreached = no pod: ridge cells, dropped components, unreached ground)
        private void PodIdRows()
        {
            int jEnd = Math.Min(m_RegionJ1 + 1, m_Cursor + RowsPerUnit);
            for (int j = m_Cursor; j < jEnd; j++)
                for (int i = m_RegionI0; i <= m_RegionI1; i++)
                {
                    int c = j * m_Nx + i;
                    int k = PodCompOf(c);
                    int pod = k >= 0 ? m_PodOfComp[m_PodComps[k].Parent] : -1;
                    m_PodOf[c] = pod >= 0 ? (ushort)pod : MUnreached;
                }
            m_Cursor = jEnd;
            if (m_Cursor > m_RegionJ1)
                Next(Stage.Lifts);
        }

        /// <summary>Host oracle (S-2): appends the grade (m_O) of every skiable cell of a pod of the last Resort scan; false when the
        /// pod id is out of range. Valid until the next Begin.</summary>
        public bool PodGrades(int pod, List<float> grades)
        {
            if (m_Mode != AutoPlanMode.Resort || pod < 0 || pod >= m_PodList.Count || m_PodOf == null)
                return false;
            for (int j = m_RegionJ0; j <= m_RegionJ1; j++)
                for (int i = m_RegionI0; i <= m_RegionI1; i++)
                {
                    int c = j * m_Nx + i;
                    if (m_PodOf[c] == pod && m_O[c] < PodHardGrade)
                        grades.Add(m_O[c]);
                }
            return true;
        }

        /// <summary>Copies the terrain part of the plan (pads, pods) into a new ResortPlan: fresh arrays per result (the planner keeps its
        /// lists for a Reselect that restarts after Pods). Called by FinishResort.</summary>
        private void TerrainPlan(ResortPlan plan)
        {
            plan.Pads = m_PadList.ToArray();
            plan.Pods = m_PodList.ToArray();
            for (int k = 0; k < plan.Pods.Length; k++)
                plan.Pods[k].BandShare = (float[])plan.Pods[k].BandShare.Clone();
        }
    }
}
