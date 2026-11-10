// RunsFirstPlanner order: sorts and a queue with a defined order for equal keys. List.Sort and Array.Sort are introsorts: the order they
// leave equal keys in is unspecified and may differ between runtimes (the mod runs on Unity's Mono, the host on .NET 8).
// PriorityQueue (not in net48) leaves equal priorities in an unspecified order too. Every result-relevant sort and queue of the
// planner goes through these helpers, so a plan depends on its input only. Pure C# 9, net48-clean.

using System;
using System.Collections.Generic;

namespace SkiResortMod.Systems
{
    public sealed partial class RunsFirstPlanner
    {
        /// <summary>A stable sort: equal keys keep their order in the list. The comparison plus the list index is a total order, so
        /// any correct sort gives this one result.</summary>
        private static void SortStable<T>(List<T> list, Comparison<T> cmp)
        {
            int n = list.Count;
            if (n < 2)
                return;
            T[] items = list.ToArray();
            var idx = new int[n];
            for (int i = 0; i < n; i++)
                idx[i] = i;
            Array.Sort(idx, (a, b) =>
            {
                int c = cmp(items[a], items[b]);
                return c != 0 ? c : a.CompareTo(b);
            });
            for (int i = 0; i < n; i++)
                list[i] = items[idx[i]];
        }

        /// <summary>Array.Sort(keys, items), then equal keys in ascending item order. The callers' items are the indices 0..n-1, so
        /// this is the stable order.</summary>
        private static void SortByKey(float[] keys, int[] items)
        {
            Array.Sort(keys, items);
            for (int i = 0; i < keys.Length;)
            {
                int j = i + 1;
                while (j < keys.Length && keys[j].CompareTo(keys[i]) == 0)
                    j++;
                if (j - i > 1)
                    Array.Sort(items, i, j - i);
                i = j;
            }
        }

        /// <summary>A binary min-heap. Equal priorities leave in the order they came in (a running sequence number breaks the tie), so
        /// an item enqueued again goes behind the items already waiting at its priority. The PriorityQueue calls it replaces.</summary>
        private sealed class StableHeap<TElement, TPriority> where TPriority : IComparable<TPriority>
        {
            private TElement[] m_Item = new TElement[16];
            private TPriority[] m_Key = new TPriority[16];
            private long[] m_Seq = new long[16];
            private long m_Next;

            public int Count { get; private set; }

            private bool Less(int a, int b)
            {
                int c = m_Key[a].CompareTo(m_Key[b]);
                return c < 0 || c == 0 && m_Seq[a] < m_Seq[b];
            }

            private void Swap(int a, int b)
            {
                (m_Item[a], m_Item[b]) = (m_Item[b], m_Item[a]);
                (m_Key[a], m_Key[b]) = (m_Key[b], m_Key[a]);
                (m_Seq[a], m_Seq[b]) = (m_Seq[b], m_Seq[a]);
            }

            public void Enqueue(TElement item, TPriority priority)
            {
                if (Count == m_Item.Length)
                {
                    Array.Resize(ref m_Item, Count * 2);
                    Array.Resize(ref m_Key, Count * 2);
                    Array.Resize(ref m_Seq, Count * 2);
                }
                int i = Count++;
                m_Item[i] = item;
                m_Key[i] = priority;
                m_Seq[i] = m_Next++;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (!Less(i, p))
                        break;
                    Swap(i, p);
                    i = p;
                }
            }

            public bool TryPeek(out TElement item, out TPriority priority)
            {
                if (Count == 0)
                {
                    item = default;
                    priority = default;
                    return false;
                }
                item = m_Item[0];
                priority = m_Key[0];
                return true;
            }

            public bool TryDequeue(out TElement item, out TPriority priority)
            {
                if (!TryPeek(out item, out priority))
                    return false;
                int last = --Count;
                if (last > 0)
                {
                    m_Item[0] = m_Item[last];
                    m_Key[0] = m_Key[last];
                    m_Seq[0] = m_Seq[last];
                    for (int i = 0; ;)
                    {
                        int l = 2 * i + 1, r = l + 1, m = i;
                        if (l < last && Less(l, m))
                            m = l;
                        if (r < last && Less(r, m))
                            m = r;
                        if (m == i)
                            break;
                        Swap(i, m);
                        i = m;
                    }
                }
                m_Item[last] = default;
                return true;
            }
        }
    }
}
