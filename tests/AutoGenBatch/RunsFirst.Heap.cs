// RunsFirst heap: the cell queue of the flood, reach and easy-ground searches. PriorityQueue<int, float> is not precompiled in
// the runtime, so the first region of a run used to drive every search through its unoptimized tier-0 code. This is the same
// 4-ary min-heap (the same sift rules and tie order, so every search settles its cells in the same order) on two plain arrays,
// compiled optimized from the first call.

using System;
using System.Runtime.CompilerServices;

namespace AutoGenBatch
{
    internal static partial class RunsFirst
    {
        private sealed class CellHeap
        {
            private int[] m_Cell = new int[64];
            private float[] m_Cost = new float[64];
            private int m_Count;

            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            public void Enqueue(int cell, float cost)
            {
                if (m_Count == m_Cell.Length)
                {
                    Array.Resize(ref m_Cell, m_Count * 2);
                    Array.Resize(ref m_Cost, m_Count * 2);
                }
                // move up while strictly cheaper than the parent
                int i = m_Count++;
                while (i > 0)
                {
                    int p = (i - 1) >> 2;
                    if (cost.CompareTo(m_Cost[p]) >= 0)
                        break;
                    m_Cell[i] = m_Cell[p];
                    m_Cost[i] = m_Cost[p];
                    i = p;
                }
                m_Cell[i] = cell;
                m_Cost[i] = cost;
            }

            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            public bool TryDequeue(out int cell, out float cost)
            {
                if (m_Count == 0)
                {
                    cell = 0;
                    cost = 0f;
                    return false;
                }
                cell = m_Cell[0];
                cost = m_Cost[0];
                int size = --m_Count;
                if (size == 0)
                    return true;
                // the last node moves down from the root past every child strictly cheaper than it (the first cheapest child)
                int node = m_Cell[size];
                float key = m_Cost[size];
                int at = 0, c;
                while ((c = (at << 2) + 1) < size)
                {
                    int min = c;
                    float minKey = m_Cost[c];
                    int end = Math.Min(c + 4, size);
                    while (++c < end)
                        if (m_Cost[c].CompareTo(minKey) < 0)
                        {
                            min = c;
                            minKey = m_Cost[c];
                        }
                    if (key.CompareTo(minKey) <= 0)
                        break;
                    m_Cell[at] = m_Cell[min];
                    m_Cost[at] = minKey;
                    at = min;
                }
                m_Cell[at] = node;
                m_Cost[at] = key;
                return true;
            }
        }
    }
}
