// Quick runs planner: field kernels (chamfer distance, the minimax bucket queue, the Dijkstra heap) and the exact geometry checks.
// Pure. See SkiRunAutoPlanner.cs for the contract. Segment distances reuse CatTrackGrade's GradeRibbon / GradePath helpers.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunAutoPlanner
    {
        /// <summary>One chamfer pass (3x3 mask, texel and texel·√2) over rows [jFrom, jTo] of the window [i0, i1] x [j0, j1] of a
        /// row-major field of width nx. Forward reads the W, NW, N, NE neighbors. Backward reads the E, SE, S, SW ones. Seeds are the
        /// cells' initial values (min-plus), so a seed may be negative (a clearance). Public for the host's oracle.</summary>
        public static void Chamfer(float[] d, int nx, int i0, int i1, int j0, int j1, int jFrom, int jTo, bool forward, float texel)
        {
            float a = texel, b = texel * Sqrt2;
            if (forward)
            {
                for (int j = Math.Max(j0, jFrom); j <= Math.Min(j1, jTo); j++)
                {
                    int row = j * nx;
                    for (int i = i0; i <= i1; i++)
                    {
                        int c = row + i;
                        float v = d[c];
                        if (i > i0 && d[c - 1] + a < v)
                            v = d[c - 1] + a;
                        if (j > j0)
                        {
                            int up = c - nx;
                            if (d[up] + a < v)
                                v = d[up] + a;
                            if (i > i0 && d[up - 1] + b < v)
                                v = d[up - 1] + b;
                            if (i < i1 && d[up + 1] + b < v)
                                v = d[up + 1] + b;
                        }
                        d[c] = v;
                    }
                }
            }
            else
            {
                for (int j = Math.Min(j1, jTo); j >= Math.Max(j0, jFrom); j--)
                {
                    int row = j * nx;
                    for (int i = i1; i >= i0; i--)
                    {
                        int c = row + i;
                        float v = d[c];
                        if (i < i1 && d[c + 1] + a < v)
                            v = d[c + 1] + a;
                        if (j < j1)
                        {
                            int dn = c + nx;
                            if (d[dn] + a < v)
                                v = d[dn] + a;
                            if (i < i1 && d[dn + 1] + b < v)
                                v = d[dn + 1] + b;
                            if (i > i0 && d[dn - 1] + b < v)
                                v = d[dn - 1] + b;
                        }
                        d[c] = v;
                    }
                }
            }
        }

        /// <summary>Monotone bucket queue (2048 buckets, the classifier's 1/1024 grade resolution), FIFO inside a bucket.</summary>
        public sealed class MinimaxQueue
        {
            private const int Buckets = 2048;
            private readonly int[] m_Head = new int[Buckets], m_Tail = new int[Buckets];
            private int[] m_Next, m_Cell;
            private int m_Count, m_Cur;

            public void Init(int capacity)
            {
                if (m_Next == null || m_Next.Length < capacity)
                {
                    m_Next = new int[Math.Max(capacity, 1024)];
                    m_Cell = new int[m_Next.Length];
                }
                for (int b = 0; b < Buckets; b++)
                    m_Head[b] = m_Tail[b] = -1;
                m_Count = 0;
                m_Cur = 0;
            }

            public void Push(int key, int cell)
            {
                if (m_Count >= m_Next.Length)
                {
                    Array.Resize(ref m_Next, m_Next.Length * 2);
                    Array.Resize(ref m_Cell, m_Next.Length);
                }
                if (key < 0)
                    key = 0;
                if (key >= Buckets)
                    key = Buckets - 1;
                if (key < m_Cur)
                    m_Cur = key;
                int e = m_Count++;
                m_Cell[e] = cell;
                m_Next[e] = -1;
                if (m_Tail[key] < 0)
                    m_Head[key] = e;
                else
                    m_Next[m_Tail[key]] = e;
                m_Tail[key] = e;
            }

            public bool Pop(out int key, out int cell)
            {
                while (m_Cur < Buckets && m_Head[m_Cur] < 0)
                    m_Cur++;
                if (m_Cur >= Buckets)
                {
                    key = cell = -1;
                    return false;
                }
                int e = m_Head[m_Cur];
                m_Head[m_Cur] = m_Next[e];
                if (m_Head[m_Cur] < 0)
                    m_Tail[m_Cur] = -1;
                key = m_Cur;
                cell = m_Cell[e];
                return true;
            }
        }

        /// <summary>Lazy binary min-heap of (cost, cell). Ties pop the lower cell index first (deterministic).</summary>
        public sealed class DijkstraQueue
        {
            private float[] m_Key = new float[1024];
            private int[] m_Cell = new int[1024];
            private int m_Count;

            public int Count => m_Count;

            public void Clear() => m_Count = 0;

            public void Push(float key, int cell)
            {
                if (m_Count == m_Key.Length)
                {
                    Array.Resize(ref m_Key, m_Count * 2);
                    Array.Resize(ref m_Cell, m_Count * 2);
                }
                int i = m_Count++;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (!Less(key, cell, m_Key[p], m_Cell[p]))
                        break;
                    m_Key[i] = m_Key[p];
                    m_Cell[i] = m_Cell[p];
                    i = p;
                }
                m_Key[i] = key;
                m_Cell[i] = cell;
            }

            public bool Pop(out float key, out int cell)
            {
                if (m_Count == 0)
                {
                    key = 0f;
                    cell = -1;
                    return false;
                }
                key = m_Key[0];
                cell = m_Cell[0];
                float lk = m_Key[--m_Count];
                int lc = m_Cell[m_Count];
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1;
                    if (l >= m_Count)
                        break;
                    int r = l + 1;
                    int m = r < m_Count && Less(m_Key[r], m_Cell[r], m_Key[l], m_Cell[l]) ? r : l;
                    if (!Less(m_Key[m], m_Cell[m], lk, lc))
                        break;
                    m_Key[i] = m_Key[m];
                    m_Cell[i] = m_Cell[m];
                    i = m;
                }
                m_Key[i] = lk;
                m_Cell[i] = lc;
                return true;
            }

            private static bool Less(float ka, int ca, float kb, int cb) => ka < kb || (ka == kb && ca < cb);
        }

        /// <summary>Minimax field on a small grid (8-neighbor, sources = isSource, blocked = bucket 65535). out[c] = min over paths to a
        /// source of the max bucket along the path, 65535 if unreached. The planner's stage 3 kernel in one call (host oracle).</summary>
        public static void MinimaxField(int nx, int nz, ushort[] bucket, bool[] isSource, ushort[] result)
        {
            var q = new MinimaxQueue();
            int n = nx * nz;
            q.Init(n);
            for (int c = 0; c < n; c++)
            {
                result[c] = MUnreached;
                if (isSource[c] && bucket[c] != MUnreached)
                    q.Push(bucket[c], c);
            }
            while (q.Pop(out int key, out int c))
            {
                if (result[c] != MUnreached)
                    continue;
                result[c] = (ushort)key;
                int i = c % nx, j = c / nx;
                for (int d = 0; d < 8; d++)
                {
                    int ni = i + DirX[d], nj = j + DirZ[d];
                    if (ni < 0 || nj < 0 || ni >= nx || nj >= nz)
                        continue;
                    int nc = nj * nx + ni;
                    if (result[nc] == MUnreached && bucket[nc] != MUnreached)
                        q.Push(Math.Max(key, bucket[nc]), nc);
                }
            }
        }

        // ---------------------------------------------------------------- exact geometry

        // Forwarders: the bodies live in PlanGeometry.cs (the tool layer and any planner share them).
        public static bool BBox(float2[] p, out float2 lo, out float2 hi) => PlanGeometry.BBox(p, out lo, out hi);

        public static double PolygonArea(float2[] p) => PlanGeometry.PolygonArea(p);

        public static float SegClosest(float2 a, float2 b, float2 c, float2 d, out float2 p, out float2 q) =>
            PlanGeometry.SegClosest(a, b, c, d, out p, out q);

        public static float RingDistance(float2[] a, float2[] b, float cutoff, out float2 pa, out float2 pb, out int ea, out int eb) =>
            PlanGeometry.RingDistance(a, b, cutoff, out pa, out pb, out ea, out eb);

        public static float EdgeDistance(float2[] a, float2[] b, float cutoff, out float2 pa, out float2 pb, out int ea, out int eb) =>
            PlanGeometry.EdgeDistance(a, b, cutoff, out pa, out pb, out ea, out eb);

        public static float PointRingDistance(float2[] ring, float2 p) => PlanGeometry.PointRingDistance(ring, p);

        /// <summary>Inserts point p (on or next to edge e) into the ring as a vertex. If a vertex of that edge lies within
        /// GradeRibbon.MinSpacingM, it reuses that vertex instead (a new vertex that close to a corner would break the 1.5 m edge
        /// clearance). Returns the vertex index.</summary>
        public static int InsertNode(List<float2> ring, int e, float2 p)
        {
            int n = ring.Count;
            float2 a = ring[e], b = ring[(e + 1) % n];
            float min = GradeRibbon.MinSpacingM;
            if (math.distance(a, p) < min)
                return e;
            if (math.distance(b, p) < min)
                return (e + 1) % n;
            GradePath.SegDistance(p, a, b, out float u);
            ring.Insert(e + 1, math.lerp(a, b, u));
            return e + 1;
        }
    }
}
