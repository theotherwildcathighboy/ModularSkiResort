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
        private readonly List<long> m_Borders = new List<long>();      // (a << 32 | b), a < b: a run of equal shared border incidences
        private readonly List<int> m_BorderW = new List<int>();        // per m_Borders entry: its incidences
        private int[] m_BorderCount = new int[64], m_BStart = new int[64], m_BFill = new int[64], m_BUniq = new int[64];
        private int[] m_BorderB = new int[64], m_BorderBW = new int[64];   // bucketed by the smaller component: the other one, the weight
        private bool[] m_Settled = new bool[64], m_PodKeep = new bool[16];
        private readonly List<float[]> m_SharePool = new List<float[]>();   // BandShare arrays by creation order (TerrainPlan clones them)
        private int m_PodEdges;
        private readonly List<PlanPod> m_PodList = new List<PlanPod>();
        private readonly int[] m_PadRoot = new int[MaxPads], m_PadDown = new int[MaxPads], m_PadDownCell = new int[MaxPads];
        private readonly float[] m_PadDownY = new float[MaxPads];
        private readonly List<int> m_PadFirst = new List<int>();       // pad id -> its smallest cell (the pod key)
        private int[] m_PodStat = new int[64], m_PodBands = new int[64 * 7], m_PodOfComp = new int[64];
        private double[] m_PodGrade = new double[64];
        private ulong[] m_PodPads = new ulong[64 * 4];                 // per component: the pads its cells touch (256-bit set)
        private ulong[] m_CompBench = new ulong[64 * 4];               // per component: the benches merged into its lower pad it touches
        private int[] m_AdjStart = new int[64], m_AdjFill = new int[64], m_Adj = new int[64], m_AdjLen = new int[64];
        private int[] m_MemberNext = new int[64], m_MemberLast = new int[64], m_Acc = new int[64];
        private readonly List<int> m_PodTouched = new List<int>();

        // m_PodStat per component (stride PodStatStride): skiable cells, cells above the tree line, the top cell (-1 = none)
        private const int PodStatStride = 3;

        /// <summary>Pods of the last Resort scan, in id order; empty in Runs mode.</summary>
        public int PodCount => m_PodList.Count;

        private bool PadCapable(int pad) => pad < m_PadList.Count && m_PadList[pad].InscribedM >= StationHalfDiagonalOf(0);

        /// <summary>A capable pad big enough to be a pod bottom (a lift's bottom terminal): PodBottomMinAreaM2 (1 ha). Smaller capable pads
        /// are labels like any other but always merge into Down(P) (they stay pads: benches, top stations).</summary>
        private bool PadBottom(int pad) => PadCapable(pad) && m_PadList[pad].AreaM2 >= PodBottomMinAreaM2;

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
        /// 4 rows: labels and component sums; 5 rows: shared borders (across ridge cells, and diagonal touches) and the merged benches
        /// each component touches; 6 one pad per unit: bench links; 7 one unit: border buckets; 8 chunks: scatter the borders into
        /// buckets; 9 buckets: sort and fold each; 10 one unit: adjacency starts; 11 buckets: the adjacency; 12: the small-component
        /// merge (bounded work per unit); 13 rows: pod statistics per merged group; 14 one unit: the pod list; 15 rows: the pod id
        /// layer. Then Lifts.</summary>
        private void UnitPods()
        {
            switch (m_Cursor2)
            {
                case 0:
                    m_PodComps.Clear();
                    m_Borders.Clear();
                    m_BorderW.Clear();
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
                    PodBenchLinks();
                    return;
                case 7:
                    PodBorderBuckets();
                    m_Cursor = 0;
                    m_Cursor2 = 8;
                    return;
                case 8:
                    PodBorderScatter();
                    return;
                case 9:
                    PodBorderFold();
                    return;
                case 10:
                    PodAdjacency();
                    m_Cursor = 0;
                    m_Cursor2 = 11;
                    return;
                case 11:
                    PodAdjacencyFill();
                    return;
                case 12:
                    PodSmallMerge();
                    return;
                case 13:
                    PodStatRows();
                    return;
                case 14:
                    BuildPods();
                    m_Cursor = m_RegionJ0;
                    m_Cursor2 = 15;
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

        /// <summary>A cell that belongs to a pod component: labeled, not a cell of any pad merged into its own lower pad (the pod is the
        /// ground ABOVE its bottom station site: two faces draining onto one pad, or onto a bench merged into it, stay two pods), and not
        /// a ridge cell (other pads keep their ridge cells).</summary>
        private bool PodCell(int c)
        {
            ushort l = m_PodOf[c];
            if (l == MUnreached)
                return false;
            byte pad = m_PadOf[c];
            if (pad == PadNone)
                return m_Ridge[c] == 0;
            return Root(m_PadRoot, pad) != Root(m_PadRoot, l);
        }

        // Phase 1, rows: Down(P) = the label of P's lowest outside 8-neighbor below P's lowest cell that carries another label (its
        // descent; ties: the smaller cell; a same-height or higher neighbor is never a descent). Only capable pads are labels, so Down(P)
        // is always a station site.
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
                    float minY = m_PadList[pad].MinY;
                    for (int d = 0; d < 8; d++)
                    {
                        int n = c + DirZ[d] * m_Nx + DirX[d];
                        ushort l = m_PodOf[n];
                        if (m_PadOf[n] == pad || l == MUnreached || l == pad)
                            continue;
                        float h = m_H[n];
                        if (!(h < minY))
                            continue;
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

        // Phase 2: pads in ascending MinY (ties: id); P merges into Down(P) when P is not a pod bottom (PadBottom: under 1 ha) or its
        // drop to the merged set below is under PodMinVerticalM, measured against that set's root's MaxY (a 70 m staircase of 35 m steps
        // does not chain into one pod). The root of a merged set is its lowest pod-bottom pad (ties: the smaller id), else its lowest
        // pad, so the survivor is the lift bottom; a set with no pod-bottom pad yields no pod (BuildPods).
        private void PodMerge()
        {
            int n = m_PadList.Count;
            m_PadOrder.Clear();
            for (int p = 0; p < n; p++)
                if (PadCapable(p))
                    m_PadOrder.Add(p);
            m_PadOrder.Sort(m_ByPadMinY ?? (m_ByPadMinY = ByPadMinY));
            for (int k = 0; k < m_PadOrder.Count; k++)
            {
                int p = m_PadOrder[k], down = m_PadDown[p];
                if (down < 0)
                    continue;
                int a = Root(m_PadRoot, p), b = Root(m_PadRoot, down);
                if (a == b || (PadBottom(p) && m_PadList[p].MinY - m_PadList[b].MaxY >= PodMinVerticalM))
                    continue;
                if (RootBefore(a, b))
                    m_PadRoot[b] = a;
                else
                    m_PadRoot[a] = b;
            }
        }

        /// <summary>Pad a is the better root of a merged set than b: a pod bottom first, then the lower MinY, then the smaller id.</summary>
        private bool RootBefore(int a, int b)
        {
            bool ba = PadBottom(a), bb = PadBottom(b);
            if (ba != bb)
                return ba;
            float ya = m_PadList[a].MinY, yb = m_PadList[b].MinY;
            return ya < yb || (ya == yb && a < b);
        }

        // Cached comparisons (no delegate allocation per scan, AR-17)
        private Comparison<int> m_ByPadMinY, m_ByPodArea;
        private Comparison<PlanPod> m_ByPodBottom;

        private int ByPadMinY(int a, int b)
        {
            int byY = m_PadList[a].MinY.CompareTo(m_PadList[b].MinY);
            return byY != 0 ? byY : a.CompareTo(b);
        }

        private int ByPodArea(int a, int b)
        {
            int byArea = m_PodList[b].SkiableAreaM2.CompareTo(m_PodList[a].SkiableAreaM2);
            return byArea != 0 ? byArea : m_PodList[a].Id.CompareTo(m_PodList[b].Id);
        }

        private int ByPodBottom(PlanPod a, PlanPod b)
        {
            int byY = m_PadList[a.LowerPad].MinY.CompareTo(m_PadList[b.LowerPad].MinY);
            if (byY != 0)
                return byY;
            int ta = CellOfXz(a.TopCenter), tb = CellOfXz(b.TopCenter);
            return ta != tb ? ta.CompareTo(tb) : a.Id.CompareTo(b.Id);
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
            Grow(ref m_BorderCount, nc);
            Array.Clear(m_BorderCount, 0, nc);
            Grow(ref m_CompBench, nc * 4);
            Array.Clear(m_CompBench, 0, nc * 4);
            m_Cursor = m_RegionJ0;
            m_Cursor2 = 5;
        }

        /// <summary>The component label of a pod cell after phase 4 (-1 for any other cell).</summary>
        private int PodCompOf(int c) => PodCell(c) ? -m_Order[c] - 1 : -1;

        // Phase 5, rows: a ridge cell (not a pad cell) borders every pair of distinct same-pad components among its 8 neighbors; two
        // components touching only diagonally border too (SE and SW of each cell); a component's cells next to a pad merged into its
        // lower pad (not the lower pad itself) mark that bench in m_CompBench (phase 6)
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
                        int own = m_PodComps[k].Key;
                        for (int d = 0; d < 8; d++)
                        {
                            byte pad = m_PadOf[c + DirZ[d] * m_Nx + DirX[d]];
                            if (pad != PadNone && pad != own && PadCapable(pad) && Root(m_PadRoot, pad) == own)
                                m_CompBench[k * 4 + (pad >> 6)] |= 1UL << (pad & 63);
                        }
                        if (j < m_RegionJ1)
                        {
                            if (i < m_RegionI1)
                                AddBorder(k, PodCompOf(c + m_Nx + 1));
                            if (i > m_RegionI0)
                                AddBorder(k, PodCompOf(c + m_Nx - 1));
                        }
                        continue;
                    }
                    if (m_PodOf[c] == MUnreached || m_Ridge[c] == 0 || m_PadOf[c] != PadNone)
                        continue;   // a pad cell never borders (a flat pad's own ridge cells would join its faces)
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
            {
                m_Cursor = 0;
                m_Cursor2 = 6;
            }
        }

        // Phase 6, one pad per unit: the components touching a bench merged into their lower pad border through it (one incidence:
        // each links to the largest of them, ties: the smaller index). Merged-bench cells stay out of the components (two faces draining
        // onto a bench stay two pods), but a face too small for a pod can still join its neighbor across the bench (a 60 m step under a
        // 50 m step: both faces small, one pod together).
        private void PodBenchLinks()
        {
            int p = m_Cursor++;
            if (p >= m_PadList.Count)
            {
                m_Cursor2 = 7;
                return;
            }
            if (!PadCapable(p) || Root(m_PadRoot, p) == p)
                return;
            int nc = m_PodComps.Count, word = p >> 6, big = -1;
            ulong bit = 1UL << (p & 63);
            for (int k = 0; k < nc; k++)
                if ((m_CompBench[k * 4 + word] & bit) != 0 && (big < 0 || m_PodComps[k].Cells > m_PodComps[big].Cells))
                    big = k;
            for (int k = 0; k < nc; k++)
                if (k != big && (m_CompBench[k * 4 + word] & bit) != 0)
                    AddBorder(k, big);
        }

        // One incidence per call; a repeat of the last pair (runs along a row) only adds to its weight, so the list holds far fewer
        // entries than incidences; each entry is counted under its smaller component (the bucket of phase 8)
        private void AddBorder(int a, int b)
        {
            if (b < 0 || a == b || m_PodComps[a].Key != m_PodComps[b].Key)
                return;
            long key = ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
            int n = m_Borders.Count;
            if (n > 0 && m_Borders[n - 1] == key)
            {
                m_BorderW[n - 1]++;
                return;
            }
            m_Borders.Add(key);
            m_BorderW.Add(1);
            m_BorderCount[Math.Min(a, b)]++;
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

        /// <summary>The merge-group root of component k, halving the path on the way (every parent is a smaller index; roots unchanged).</summary>
        private int CompRoot(int k)
        {
            while (true)
            {
                int p = m_PodComps[k].Parent;
                if (p == k)
                    return k;
                int gp = m_PodComps[p].Parent;
                if (gp != p)
                {
                    PodComp pc = m_PodComps[k];
                    pc.Parent = gp;
                    m_PodComps[k] = pc;
                }
                k = gp;
            }
        }

        private bool PodSmall(int root)
        {
            PodComp pc = m_PodComps[root];
            return pc.Cells * m_Texel * m_Texel < PodMinAreaM2 || pc.MaxY - pc.MinY < PodMinVerticalM;
        }

        /// <summary>Border entries (or adjacency entries) handled per unit in phases 8, 9, 11 and 12.</summary>
        private const int BorderWorkPerUnit = 8192;

        // Phase 7, one unit (O(components)): the bucket starts of the border entries by their smaller component
        private void PodBorderBuckets()
        {
            int nc = m_PodComps.Count, nb = m_Borders.Count;
            Grow(ref m_BStart, nc + 1);
            Grow(ref m_BFill, nc);
            Grow(ref m_BUniq, nc);
            Grow(ref m_BorderB, nb);
            Grow(ref m_BorderBW, nb);
            m_BStart[0] = 0;
            for (int k = 0; k < nc; k++)
            {
                m_BStart[k + 1] = m_BStart[k] + m_BorderCount[k];
                m_BFill[k] = m_BStart[k];
            }
        }

        // Phase 8, BorderWorkPerUnit entries per unit: each entry's larger component and weight into its bucket (a counting sort by the
        // smaller component, so no unit sorts the whole list)
        private void PodBorderScatter()
        {
            int nb = m_Borders.Count, end = Math.Min(nb, m_Cursor + BorderWorkPerUnit);
            for (int s = m_Cursor; s < end; s++)
            {
                long key = m_Borders[s];
                int at = m_BFill[(int)(key >> 32)]++;
                m_BorderB[at] = (int)(key & 0xFFFFFFFFL);
                m_BorderBW[at] = m_BorderW[s];
            }
            m_Cursor = end;
            if (m_Cursor < nb)
                return;
            int nc = m_PodComps.Count;
            Grow(ref m_AdjStart, nc + 1);
            Array.Clear(m_AdjStart, 0, nc + 1);
            m_PodEdges = 0;
            m_Cursor = 0;
            m_Cursor2 = 9;
        }

        // Phase 9, buckets until BorderWorkPerUnit entries per unit: sort the bucket by the other component, fold equal pairs (weights
        // summed: the border length in incidences) to the bucket's front, count each pair in both components' adjacency
        private void PodBorderFold()
        {
            int nc = m_PodComps.Count, work = 0;
            while (m_Cursor < nc && work < BorderWorkPerUnit)
            {
                int a = m_Cursor++, s0 = m_BStart[a], len = m_BStart[a + 1] - s0, w = s0;
                work += len + 1;
                if (len > 1)
                    Array.Sort(m_BorderB, m_BorderBW, s0, len);
                for (int s = s0; s < s0 + len; s++)
                {
                    if (w > s0 && m_BorderB[w - 1] == m_BorderB[s])
                    {
                        m_BorderBW[w - 1] += m_BorderBW[s];
                        continue;
                    }
                    m_BorderB[w] = m_BorderB[s];
                    m_BorderBW[w++] = m_BorderBW[s];
                    m_AdjStart[a + 1]++;
                    m_AdjStart[m_BorderB[s] + 1]++;
                    m_PodEdges++;
                }
                m_BUniq[a] = w - s0;
            }
            if (m_Cursor < nc)
                return;
            m_Cursor2 = 10;
        }

        // Phase 10, one unit (O(components)): the adjacency starts and one member list per merge group
        private void PodAdjacency()
        {
            int nc = m_PodComps.Count;
            Grow(ref m_AdjFill, nc);
            Grow(ref m_MemberNext, nc);
            Grow(ref m_MemberLast, nc);
            Grow(ref m_Acc, nc);
            Grow(ref m_Settled, nc);
            Array.Clear(m_Acc, 0, nc);
            Array.Clear(m_Settled, 0, nc);
            for (int k = 0; k < nc; k++)
            {
                m_AdjStart[k + 1] += m_AdjStart[k];
                m_AdjFill[k] = m_AdjStart[k];
                m_MemberNext[k] = -1;
                m_MemberLast[k] = k;
            }
            Grow(ref m_Adj, 2 * m_PodEdges);
            Grow(ref m_AdjLen, 2 * m_PodEdges);
        }

        // Phase 11, buckets until BorderWorkPerUnit pairs per unit: the folded pairs into both components' adjacency
        private void PodAdjacencyFill()
        {
            int nc = m_PodComps.Count, work = 0;
            while (m_Cursor < nc && work < BorderWorkPerUnit)
            {
                int a = m_Cursor++, s0 = m_BStart[a], n = m_BUniq[a];
                work += n + 1;
                for (int s = s0; s < s0 + n; s++)
                {
                    int b = m_BorderB[s], len = m_BorderBW[s];
                    m_Adj[m_AdjFill[a]] = b;
                    m_AdjLen[m_AdjFill[a]++] = len;
                    m_Adj[m_AdjFill[b]] = a;
                    m_AdjLen[m_AdjFill[b]++] = len;
                }
            }
            if (m_Cursor < nc)
                return;
            m_Cursor = 0;
            m_Cursor2 = 12;
        }

        // Phase 12: components in ascending order (= smallest cell); one whose merge group is still small joins the neighbor group of the
        // same pad with the longest shared border (summed over the group's members; ties: the smaller root), the smaller root wins.
        // A group found with no neighbor is settled (its adjacency is not scanned again until another group merges into it). At most
        // one merge or BorderWorkPerUnit adjacency entries (plus one per check) per unit.
        private void PodSmallMerge()
        {
            int nc = m_PodComps.Count, work = 0;
            while (m_Cursor < nc && work < BorderWorkPerUnit)
            {
                work++;
                int g = CompRoot(m_Cursor++);
                if (m_Settled[g] || !PodSmall(g))
                    continue;
                m_PodTouched.Clear();
                for (int m = g; m >= 0; m = m_MemberNext[m])
                {
                    work += m_AdjStart[m + 1] - m_AdjStart[m];
                    for (int e = m_AdjStart[m]; e < m_AdjStart[m + 1]; e++)
                    {
                        int o = CompRoot(m_Adj[e]);
                        if (o == g)
                            continue;
                        if (m_Acc[o] == 0)
                            m_PodTouched.Add(o);
                        m_Acc[o] += m_AdjLen[e];
                    }
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
                {
                    m_Settled[g] = true;   // no neighbor of the same pad: stays small, BuildPods drops it
                    continue;
                }
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
                m_Settled[root] = false;   // new members, new neighbors
                break;
            }
            if (m_Cursor < nc)
                return;
            // Flatten the merge forest and clear the per-group sums for phase 13
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
            m_Cursor2 = 13;
        }

        // Phase 13, rows: per merged group the skiable cells (under PodHardGrade), band counts, grade sum, cells above the tree line, the
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
                m_Cursor2 = 14;
        }

        // Phase 14, one unit: groups that drain to a pod-bottom pad (PadBottom), are big enough and have a top cell PodMinVerticalM above
        // their lowest cell become pods; ordered by (LowerPad.MinY, top cell); over MaxPodsOf the smallest by SkiableAreaM2 drop first (ties: the later pod).
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
                if (pc.Parent != g || PodSmall(g) || top < 0 || pc.Key < 0 || pc.Key >= m_PadList.Count || !PadBottom(pc.Key))
                    continue;
                PlanPad lower = m_PadList[pc.Key];
                int skiable = m_PodStat[s];
                float topY = m_H[top], bottomY = pc.MinY, vertical = topY - bottomY;
                if (vertical < PodMinVerticalM)
                    continue;   // the top cell sits EdgeSeedMarginM inside: its vertical can fall under the group's cell range
                float2 topXz = CellCenter(top);
                float reach = math.distance(topXz, lower.Center);
                if (m_PodList.Count == m_SharePool.Count)
                    m_SharePool.Add(new float[7]);
                float[] share = m_SharePool[m_PodList.Count];
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
                m_PadOrder.Sort(m_ByPodArea ?? (m_ByPodArea = ByPodArea));
                Grow(ref m_PodKeep, m_PodList.Count);
                bool[] keep = m_PodKeep;
                Array.Clear(keep, 0, m_PodList.Count);
                for (int k = 0; k < cap; k++)
                    keep[m_PadOrder[k]] = true;
                int w = 0;
                for (int k = 0; k < m_PodList.Count; k++)
                    if (keep[k])
                        m_PodList[w++] = m_PodList[k];
                m_PodList.RemoveRange(w, m_PodList.Count - w);
            }
            m_PodList.Sort(m_ByPodBottom ?? (m_ByPodBottom = ByPodBottom));
            for (int k = 0; k < m_PodList.Count; k++)
            {
                PlanPod pod = m_PodList[k];
                m_PodOfComp[pod.Id] = k;
                pod.Id = k;
                m_PodList[k] = pod;
            }
        }

        private int CellOfXz(float2 p) => Cell(p, out int c) ? c : int.MaxValue;

        // Phase 15, rows: the pod id layer (MUnreached = no pod: ridge cells, dropped components, unreached ground)
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
