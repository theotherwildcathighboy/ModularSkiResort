// RunsFirstPlanner ski graph: how a skier moves through a plan. From a lift top a skier takes any run or traverse starting
// within 150 m. The skier continues onto anything its end touches (or that branches off it). A piece passing a lift's bottom
// station delivers the skier to that lift. The planner uses the graph, the way a person reads a trail map, to find these:
// - runs nobody can reach or that strand the skier
// - dead lifts
// - lifts that cannot get to each other.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class RunsFirstPlanner
    {
        private sealed class SkiGraph
        {
            public int Runs;                         // elements 0..Runs-1 are the plan's runs, the rest its connectors
            public List<float3[]> P = new List<float3[]>();
            public List<int>[] Next, Delivers;       // Delivers: lift bottoms passed, and gondola / tram tops ended at (ride down)
            public List<int>[] AtBottom;             // lift bottoms passed only
            public bool[] IsRun;                     // as scorecard v3 reads it: plan runs, and graded tracks steeper than the blue line
            public SlopeBand[] Band;                 // runs by the ground under them, graded tracks by their grade along the path
            public HashSet<int>[] Skied, Reaches;    // per lift: elements skied from its top, lift bottoms reached
            public bool[] Returns, Reached;
            public int[] Component;                  // per lift: its strongly connected set (index)
            public int BestComponent, BestSize;
        }

        [MethodImpl(Hot)]
        private static SkiGraph Ski(Grid g, PlanDraft plan, List<Lift> lifts)
        {
            var sg = new SkiGraph { Runs = plan.Runs.Count };
            foreach (PlanRun r in plan.Runs)
                sg.P.Add(Sampled(g, r.Pts).ToArray());
            foreach (List<float2> c in plan.Connectors)
                sg.P.Add(Sampled(g, c).ToArray());
            int m = sg.P.Count;
            sg.IsRun = new bool[m];
            sg.Band = new SlopeBand[m];
            for (int i = 0; i < m; i++)
            {
                List<float2> pts = i < sg.Runs ? plan.Runs[i].Pts : plan.Connectors[i - sg.Runs];
                if (i < sg.Runs && !plan.Runs[i].Graded)
                {
                    sg.IsRun[i] = true;
                    sg.Band[i] = SeenBand(g, pts, RaterGraph);
                }
                else
                    (sg.IsRun[i], sg.Band[i]) = TrackClass(g, pts);
            }
            var bot = lifts.ConvertAll(lf => new float3(lf.Bottom.x, RawH(g, lf.Bottom.xz), lf.Bottom.z));
            var top = lifts.ConvertAll(lf => new float3(lf.Top.x, RawH(g, lf.Top.xz), lf.Top.z));
            sg.Next = new List<int>[m];
            sg.Delivers = new List<int>[m];
            sg.AtBottom = new List<int>[m];
            // each element's xz box: a point farther than r from the box is farther than r from every point of the element, so the
            // box test only skips pairs the point tests would reject (the same graph, bit for bit; the scan's time was in these tests)
            var box = new float4[m];
            for (int i = 0; i < m; i++)
                box[i] = BoxOf(sg.P[i]);
            for (int i = 0; i < m; i++)
            {
                sg.Next[i] = new List<int>();
                float3[] pi = sg.P[i];
                float3 end = pi[pi.Length - 1];
                for (int j = 0; j < m; j++)
                {
                    if (i == j)
                        continue;
                    float3[] pj = sg.P[j];
                    float3 st = pj[0];
                    if ((NearBox(end.xz, box[j], 40f) && AnyNearBelow(pj, end)) || (NearBox(st.xz, box[i], 40f) && AnyNearAbove(pi, st)))
                        sg.Next[i].Add(j);
                }
                sg.Delivers[i] = new List<int>();
                sg.AtBottom[i] = new List<int>();
                for (int L = 0; L < lifts.Count; L++)
                    if (NearBox(bot[L].xz, box[i], 60f) && AnyAtBottom(pi, bot[L]))
                    {
                        sg.Delivers[i].Add(L);
                        sg.AtBottom[i].Add(L);
                    }
                    else if (Aerial(lifts[L].Type) && math.distancesq(end.xz, top[L].xz) < 60f * 60f)
                        sg.Delivers[i].Add(L);   // a gondola or tram top: the skier rides it down
            }
            sg.Skied = new HashSet<int>[lifts.Count];
            sg.Reaches = new HashSet<int>[lifts.Count];
            for (int L = 0; L < lifts.Count; L++)
            {
                sg.Skied[L] = new HashSet<int>();
                sg.Reaches[L] = new HashSet<int>();
                var q = new Queue<int>();
                for (int i = 0; i < m; i++)
                    if (math.distancesq(sg.P[i][0].xz, top[L].xz) < 150f * 150f && sg.Skied[L].Add(i))
                        q.Enqueue(i);
                while (q.Count > 0)
                {
                    int i = q.Dequeue();
                    foreach (int b in sg.Delivers[i])
                        sg.Reaches[L].Add(b);
                    foreach (int j in sg.Next[i])
                        if (sg.Skied[L].Add(j))
                            q.Enqueue(j);
                }
            }
            sg.Returns = new bool[m];
            for (bool changed = true; changed;)
            {
                changed = false;
                for (int i = 0; i < m; i++)
                    if (!sg.Returns[i] && (sg.Delivers[i].Count > 0 || sg.Next[i].Any(j => sg.Returns[j])))
                        sg.Returns[i] = changed = true;
            }
            sg.Reached = new bool[m];
            for (int L = 0; L < lifts.Count; L++)
                foreach (int i in sg.Skied[L])
                    sg.Reached[i] = true;
            // strongly connected sets of lifts (lift A links to B when a skier leaving A's top gets to B's bottom)
            sg.Component = new int[lifts.Count];
            FillAll(sg.Component, -1);
            var fwd = new HashSet<int>[lifts.Count];
            for (int L = 0; L < lifts.Count; L++)
                fwd[L] = Closure(L, sg.Reaches);
            int comps = 0;
            for (int L = 0; L < lifts.Count; L++)
            {
                if (sg.Component[L] >= 0)
                    continue;
                int size = 0;
                for (int M = 0; M < lifts.Count; M++)
                    if (fwd[L].Contains(M) && fwd[M].Contains(L))
                    {
                        sg.Component[M] = comps;
                        size++;
                    }
                if (size > sg.BestSize)
                {
                    sg.BestSize = size;
                    sg.BestComponent = comps;
                }
                comps++;
            }
            return sg;
        }

        /// <summary>The xz box of a sampled element: (min x, min z, max x, max z).</summary>
        private static float4 BoxOf(float3[] pts)
        {
            var b = new float4(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
            foreach (float3 p in pts)
                b = new float4(math.min(b.x, p.x), math.min(b.y, p.z), math.max(b.z, p.x), math.max(b.w, p.z));
            return b;
        }

        /// <summary>False only when q is r or more from the box on x or on z, so every point in the box is r or more from q. One meter of
        /// slack covers the rounding of the point tests' differences.</summary>
        private static bool NearBox(float2 q, float4 box, float r)
        {
            r += 1f;
            return q.x - box.z < r && box.x - q.x < r && q.y - box.w < r && box.y - q.y < r;
        }

        // the ski graph's point tests (Ski, before the box tests: LINQ Any over the points), as loops
        private static bool AnyNearBelow(float3[] pts, float3 end)
        {
            foreach (float3 p in pts)
                if (math.distancesq(p.xz, end.xz) < 40f * 40f && p.y <= end.y + 5f)
                    return true;
            return false;
        }

        private static bool AnyNearAbove(float3[] pts, float3 st)
        {
            foreach (float3 p in pts)
                if (math.distancesq(p.xz, st.xz) < 40f * 40f && st.y <= p.y + 5f)
                    return true;
            return false;
        }

        private static bool AnyAtBottom(float3[] pts, float3 bot)
        {
            for (int k = 1; k < pts.Length; k++)
                if (math.distancesq(pts[k].xz, bot.xz) < 60f * 60f && pts[k].y >= bot.y - 15f)
                    return true;
            return false;
        }

        [MethodImpl(Hot)]
        private static HashSet<int> Closure(int start, HashSet<int>[] adj)
        {
            var seen = new HashSet<int> { start };
            var st = new Stack<int>();
            st.Push(start);
            while (st.Count > 0)
                foreach (int y in adj[st.Pop()])
                    if (seen.Add(y))
                        st.Push(y);
            return seen;
        }

        /// <summary>A lift is dead when nothing served leaves its top.</summary>
        private static bool Dead(SkiGraph sg, int L) => !sg.Skied[L].Any(i => sg.IsRun[i] && sg.Returns[i]);
    }
}
