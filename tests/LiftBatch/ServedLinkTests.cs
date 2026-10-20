// ServedLinkTests — Round T06 (docs/research/field/2026-10-15/build/T06-served-links.md; Items 76 / 80 of the 2026-10-15 field
// test): a run connected to a served run is served. LiftServedRule.RunLinks.Build + AssignLinked, the one rule both the
// attraction path and the stats path call. Where-tags `served-link/…`.
//   SL-1  connector between two served runs → served (via the lower-indexed neighbor, root = that served run);
//   SL-2  a run whose only neighbor is unserved → unserved, and so is the neighbor; a lift without power serves nothing, linked or not;
//   SL-3  chain A(served) - B - C → C served (root A, via B), B served via A; the end of a chain with a gap stays unserved;
//   SL-4  distance: 24.99 m and exactly 25 m linked (inclusive), 25.01 m and 26 m not; the nodes are compared, not the edges;
//   SL-5  a run of another ski area with coinciding nodes → not linked; area id < 0 never links;
//   SL-6  overlap without near nodes: crossing outlines linked, a small ring inside a big one linked, disjoint boxes not;
//   SL-7  the existing served cases unchanged: no links → servingLift / distance / servedCount equal Assign's, served = direct;
//   SL-8  graph reuse: Build with the same arrays keeps the graph (Reuses), a replaced array or area rebuilds; a graph of another
//         size or null links is ignored; a cycle of runs terminates; 200 runs × 120 nodes build in well under a second.

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    public static class ServedLinkTests
    {
        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Sl0Constant();
            Sl1Connector();
            Sl2UnservedNeighbour();
            Sl3Chain();
            Sl4Distance();
            Sl5OtherArea();
            Sl6Overlap();
            Sl7ExistingCasesUnchanged();
            Sl8Reuse();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);

        private static void Eq<T>(T expected, T actual, string where, string what)
        {
            Check(EqualityComparer<T>.Default.Equals(expected, actual), where, what + ": expected " + expected + ", got " + actual);
        }

        // ── fixtures ────────────────────────────────────────────────────────────────────────────

        /// <summary>One lift: a chord (0,0) → (0,500), top station y 300, powered; a run top at x ≤ 150 and y ≤ 315 is served.</summary>
        private static void Lift(LiftPower power, out List<LiftServedRule.Lift> lifts, out List<LiftServedRule.Chord> chords)
        {
            chords = new List<LiftServedRule.Chord> { new LiftServedRule.Chord { A = new float2(0f, 0f), B = new float2(0f, 500f) } };
            lifts = new List<LiftServedRule.Lift>
            {
                new LiftServedRule.Lift
                {
                    Id = new EntityRef(101, 1), Base = new EntityRef(101, 1), Top = new EntityRef(102, 1),
                    Complete = true, Power = power, TopY = 300f, FirstChord = 0, ChordCount = 1,
                },
            };
        }

        private static readonly float3 Served1 = new float3(100f, 250f, 200f);    // 100 m from the chord, below the top margin
        private static readonly float3 Served2 = new float3(100f, 250f, 400f);
        private static readonly float3 Far = new float3(1000f, 200f, 200f);        // 1000 m from the chord: not directly served
        private static readonly float3 TooHigh = new float3(50f, 400f, 200f);      // near the chord but above TopY + 15

        private sealed class Scene
        {
            public readonly List<float3> Tops = new List<float3>();
            public readonly List<float2[]> Outlines = new List<float2[]>();
            public readonly List<int> Areas = new List<int>();
            public readonly LiftServedRule.RunLinks Links = new LiftServedRule.RunLinks();
            public List<LiftServedRule.Lift> Lifts;
            public List<LiftServedRule.Chord> Chords;
            public readonly List<int> ServingLift = new List<int>(), ServedCount = new List<int>(), Root = new List<int>(), Via = new List<int>();
            public readonly List<float> Distance = new List<float>();

            public Scene(LiftPower power = LiftPower.Powered)
            {
                Lift(power, out Lifts, out Chords);
            }

            public int Add(float3 top, float2[] outline, int area = 0)
            {
                Tops.Add(top);
                Outlines.Add(outline);
                Areas.Add(area);
                return Tops.Count - 1;
            }

            public Scene Solve()
            {
                Links.Build(Outlines, Areas);
                LiftServedRule.AssignLinked(Tops, Links, Lifts, Chords, ServingLift, Distance, ServedCount, Root, Via);
                return this;
            }

            public bool Served(int i) => Root[i] >= 0;
        }

        /// <summary>An axis-aligned ring (four nodes) from (x0, z0) to (x1, z1).</summary>
        private static float2[] Rect(float x0, float z0, float x1, float z1)
        {
            return new[] { new float2(x0, z0), new float2(x1, z0), new float2(x1, z1), new float2(x0, z1) };
        }

        // ── cases ───────────────────────────────────────────────────────────────────────────────

        private static void Sl0Constant()
        {
            Eq(25f, LiftServedRule.LinkMeters, "served-link/SL-0", "LinkMeters");
        }

        private static void Sl1Connector()
        {
            const string W = "served-link/SL-1";
            var s = new Scene();
            int a = s.Add(Served1, Rect(90f, 100f, 130f, 200f));
            int b = s.Add(Served2, Rect(90f, 400f, 130f, 500f));
            // The connector runs from A's lower end (z 200) to B's upper end (z 400); its own top (Far) fails the lift test.
            int c = s.Add(Far, Rect(100f, 215f, 120f, 385f));
            s.Solve();
            Check(s.Served(a) && s.Served(b), W, "A and B directly served");
            Eq(-1, s.Via[a], W, "A via");
            Check(s.ServingLift[c] < 0, W, "the connector's own top is not served by the lift");
            Check(s.Served(c), W, "the connector between two served runs is served");
            Eq(a, s.Root[c], W, "connector root: the first (lower) served neighbour");
            Eq(a, s.Via[c], W, "connector via");
            Eq(2, s.Links.LinkCount, W, "links A-C and B-C");
            Eq(2, s.ServedCount[0], W, "the lift's runs-served count stays direct only (A, B)");
        }

        private static void Sl2UnservedNeighbour()
        {
            const string W = "served-link/SL-2";
            var s = new Scene();
            int d = s.Add(Far, Rect(1000f, 100f, 1040f, 200f));
            int e = s.Add(new float3(1100f, 200f, 200f), Rect(1050f, 100f, 1090f, 200f));   // 10 m from D, also unserved
            int f = s.Add(TooHigh, Rect(40f, 150f, 80f, 250f));                                // near the chord but too high; no neighbor
            s.Solve();
            Check(!s.Served(d) && !s.Served(e), W, "two linked runs, neither near a lift: both unserved");
            Eq(1, s.Links.LinkCount, W, "D-E linked");
            Check(!s.Served(f), W, "a run above the lift's top margin with no neighbour is unserved");
            Eq(-1, s.Via[d], W, "unserved via");

            var off = new Scene(LiftPower.NoPower);
            int a = off.Add(Served1, Rect(90f, 100f, 130f, 200f));
            int b = off.Add(Far, Rect(100f, 210f, 120f, 300f));
            off.Solve();
            Check(!off.Served(a) && !off.Served(b), W, "a lift without power serves nothing, linked or not");
        }

        private static void Sl3Chain()
        {
            const string W = "served-link/SL-3";
            var s = new Scene();
            int a = s.Add(Served1, Rect(90f, 100f, 130f, 200f));
            int b = s.Add(Far, Rect(100f, 220f, 120f, 300f));      // 20 m above A's upper edge nodes (z 200 → 220)
            int c = s.Add(new float3(1100f, 100f, 200f), Rect(100f, 320f, 120f, 400f));   // 20 m beyond B
            int g = s.Add(new float3(1200f, 100f, 200f), Rect(100f, 440f, 120f, 520f));   // 40 m beyond C: a gap
            s.Solve();
            Check(s.Served(a), W, "A direct");
            Check(s.Served(b), W, "B served via A");
            Eq(a, s.Via[b], W, "B via");
            Check(s.Served(c), W, "C served through B (chain A - B - C)");
            Eq(a, s.Root[c], W, "C root");
            Eq(b, s.Via[c], W, "C via");
            Check(!LiftServedRule.OutlinesLinked(s.Outlines[a], s.Outlines[c]), W, "A and C themselves are not linked");
            Check(!s.Served(g), W, "a run 40 m past the end of the chain is unserved");
        }

        private static void Sl4Distance()
        {
            const string W = "served-link/SL-4";
            float[] gaps = { 24.99f, 25f, 25.01f, 26f };
            bool[] linked = { true, true, false, false };
            for (int i = 0; i < gaps.Length; i++)
            {
                var s = new Scene();
                int a = s.Add(Served1, Rect(90f, 100f, 130f, 200f));
                int b = s.Add(Far, Rect(90f, 200f + gaps[i], 130f, 300f + gaps[i]));   // straight above A's node (90, 200): z gap
                s.Solve();
                Eq(linked[i], s.Served(b), W, "run " + gaps[i] + " m away served");
            }
            // Nodes, not edges: a long edge passing 10 m from a node of the other run does not link when no node is within 25 m.
            var t = new Scene();
            t.Add(Served1, new[] { new float2(0f, 0f), new float2(200f, 0f), new float2(200f, 10f), new float2(0f, 10f) });
            int b2 = t.Add(Far, new[] { new float2(100f, 20f), new float2(100f, 120f), new float2(110f, 120f), new float2(110f, 20f) });
            // b2 nodes (100, 20) and (110, 20) are 10 m from a2's edge but 100 m from a2's nodes; outlines do not cross or contain.
            t.Solve();
            Check(!t.Served(b2), W, "edge-near-node without a node within 25 m is not linked (nodes, not edges)");
        }

        private static void Sl5OtherArea()
        {
            const string W = "served-link/SL-5";
            var s = new Scene();
            float2[] ring = Rect(90f, 100f, 130f, 200f);
            int a = s.Add(Served1, ring, 0);
            int b = s.Add(Far, ring, 1);          // the very same nodes, another ski area
            int c = s.Add(Far, Rect(90f, 100f, 130f, 200f), -1);   // no area (unbound)
            s.Solve();
            Check(s.Served(a), W, "A direct");
            Check(!s.Served(b), W, "a run of another ski area is not linked");
            Check(!s.Served(c), W, "a run with no area is never linked");
            Eq(0, s.Links.LinkCount, W, "no links");
        }

        private static void Sl6Overlap()
        {
            const string W = "served-link/SL-6";
            // A plus: a long horizontal bar and a long vertical bar crossing in the middle; every node of one is far from every node of the other.
            float2[] h = Rect(-300f, -10f, 300f, 10f);
            float2[] v = Rect(-10f, -300f, 10f, 300f);
            Check(LiftServedRule.OutlinesLinked(h, v), W, "crossing outlines (a plus) are linked");
            // A small ring inside a large one, 100 m from every node of the large ring.
            float2[] big = Rect(-200f, -200f, 200f, 200f);
            float2[] small = Rect(-20f, -20f, 20f, 20f);
            Check(LiftServedRule.OutlinesLinked(big, small), W, "a ring inside another is linked (overlap)");
            Check(LiftServedRule.OutlinesLinked(small, big), W, "...and symmetric");
            // Disjoint, boxes far apart, and boxes within the margin but nodes apart.
            Check(!LiftServedRule.OutlinesLinked(Rect(0f, 0f, 10f, 10f), Rect(500f, 500f, 510f, 510f)), W, "far apart is not linked");
            Check(!LiftServedRule.OutlinesLinked(null, small) && !LiftServedRule.OutlinesLinked(small, new float2[0]), W, "null / empty never link");
            // Through the scene (box prefilter must not hide a crossing).
            var s = new Scene();
            int a = s.Add(Served1, h);
            int b = s.Add(Far, v);
            s.Solve();
            Check(s.Served(b), W, "the scene links the crossing run (box filter keeps overlaps)");
            Eq(a, s.Via[b], W, "via");
        }

        private static void Sl7ExistingCasesUnchanged()
        {
            const string W = "served-link/SL-7";
            // Two lifts, as ServedAssignTests: runs at various distances, no outline links at all (null outlines).
            var chords = new List<LiftServedRule.Chord>
            {
                new LiftServedRule.Chord { A = new float2(0f, 0f), B = new float2(0f, 500f) },
                new LiftServedRule.Chord { A = new float2(0f, 500f), B = new float2(0f, 1000f) },
                new LiftServedRule.Chord { A = new float2(200f, 0f), B = new float2(200f, 1000f) },
            };
            var lifts = new List<LiftServedRule.Lift>
            {
                new LiftServedRule.Lift
                {
                    Id = new EntityRef(101, 1), Base = new EntityRef(101, 1), Top = new EntityRef(102, 1),
                    Complete = true, Power = LiftPower.Powered, TopY = 300f, FirstChord = 0, ChordCount = 2,
                },
                new LiftServedRule.Lift
                {
                    Id = new EntityRef(55, 1), Base = new EntityRef(55, 1), Top = new EntityRef(56, 1),
                    Complete = true, Power = LiftPower.Powered, TopY = 400f, FirstChord = 2, ChordCount = 1,
                },
            };
            var tops = new List<float3>
            {
                new float3(100f, 250f, 100f),      // 100 m from L1 and L2: both reach, L1 wins on the tie
                new float3(-150f, 250f, 100f),     // 150 m: inclusive, L1 only
                new float3(-150.01f, 250f, 100f),  // just outside
                new float3(100f, 316f, 100f),      // above L1's top + 15, L2 reaches (TopY 400)
                new float3(float.NaN, 0f, 0f),     // no facts
                new float3(5000f, 0f, 5000f),
            };
            var sLift = new List<int>(); var sDist = new List<float>(); var sCount = new List<int>();
            LiftServedRule.Assign(tops, lifts, chords, sLift, sDist, sCount);

            var servingLift = new List<int>(); var dist = new List<float>(); var count = new List<int>();
            var root = new List<int>(); var via = new List<int>();
            var outlines = new List<float2[]>(); var areas = new List<int>();
            for (int i = 0; i < tops.Count; i++) { outlines.Add(null); areas.Add(0); }
            var links = new LiftServedRule.RunLinks();
            links.Build(outlines, areas);
            LiftServedRule.AssignLinked(tops, links, lifts, chords, servingLift, dist, count, root, via);
            for (int i = 0; i < tops.Count; i++)
            {
                Eq(sLift[i], servingLift[i], W, "run " + i + " servingLift == Assign");
                Eq(sDist[i], dist[i], W, "run " + i + " distance == Assign");
                Eq(sLift[i] >= 0 ? i : -1, root[i], W, "run " + i + " served == direct (root is itself)");
                Eq(-1, via[i], W, "run " + i + " via");
            }
            for (int l = 0; l < lifts.Count; l++)
                Eq(sCount[l], count[l], W, "lift " + l + " runs served == Assign");
            Eq(0, links.LinkCount, W, "null outlines never link");
        }

        private static void Sl8Reuse()
        {
            const string W = "served-link/SL-8";
            var s = new Scene();
            s.Add(Served1, Rect(90f, 100f, 130f, 200f));
            s.Add(Far, Rect(100f, 210f, 120f, 300f));
            s.Solve();
            Eq(1, s.Links.Rebuilds, W, "first Build recomputes");
            s.Solve();
            Eq(1, s.Links.Rebuilds, W, "same arrays and areas: kept");
            Eq(1, s.Links.Reuses, W, "reuse counted");
            s.Outlines[1] = Rect(100f, 210f, 120f, 300f);   // a new array (the catalog allocates one when an outline moved)
            s.Solve();
            Eq(2, s.Links.Rebuilds, W, "a replaced outline array rebuilds");
            s.Areas[1] = 3;
            s.Solve();
            Eq(3, s.Links.Rebuilds, W, "a changed area rebuilds");
            Check(!s.Served(1), W, "and the run moved to another area is no longer linked");
            s.Areas[1] = 0;
            s.Solve();
            Check(s.Served(1), W, "back in the area: linked again");

            // A graph of another size, or null links: ignored (direct service only).
            var other = new LiftServedRule.RunLinks();
            other.Build(new List<float2[]> { Rect(0f, 0f, 1f, 1f) }, new List<int> { 0 });
            var root = new List<int>(); var via = new List<int>();
            var lf = new List<int>(); var d = new List<float>(); var cnt = new List<int>();
            LiftServedRule.AssignLinked(s.Tops, other, s.Lifts, s.Chords, lf, d, cnt, root, via);
            Check(root[0] == 0 && root[1] == -1, W, "a graph of another size is ignored");
            LiftServedRule.AssignLinked(s.Tops, null, s.Lifts, s.Chords, lf, d, cnt, root, via);
            Check(root[0] == 0 && root[1] == -1, W, "null links are ignored");

            // A cycle of five runs, one of them directly served: terminates, all served, each visited once.
            var c = new Scene();
            const int N = 5;
            for (int i = 0; i < N; i++)
            {
                double ang = i * 2.0 * Math.PI / N;
                float cx = 3000f + (float)Math.Cos(ang) * 40f, cz = (float)Math.Sin(ang) * 40f;
                float2[] ring = Rect(cx - 20f, cz - 20f, cx + 20f, cz + 20f);
                c.Add(i == 0 ? Served1 : Far, ring);
            }
            c.Solve();
            int served = 0;
            for (int i = 0; i < N; i++)
                if (c.Served(i)) served++;
            Eq(N, served, W, "a cycle of linked runs: all served");
            Eq(-1, c.Via[0], W, "the directly served run has no via");

            // Cost shape: 200 runs on a 20 x 10 grid, 120 nodes each, neighbors 30 m apart (nodes within 25 m only along shared sides).
            var big = new Scene();
            for (int gx = 0; gx < 20; gx++)
            {
                for (int gz = 0; gz < 10; gz++)
                {
                    var ring = new float2[120];
                    for (int k = 0; k < ring.Length; k++)
                    {
                        double ang = k * 2.0 * Math.PI / ring.Length;
                        ring[k] = new float2(gx * 60f + (float)Math.Cos(ang) * 20f, gz * 60f + (float)Math.Sin(ang) * 20f);
                    }
                    big.Add(gx == 0 && gz == 0 ? Served1 : Far, ring);
                }
            }
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            big.Solve();
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "INFO  served-link/SL-8 cost: 200 runs x 120 nodes, graph build + assign {0:F2} ms, {1} links", ms, big.Links.LinkCount));
            Check(ms < 1000.0, W, "200 runs x 120 nodes under a second (" + ms.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " ms)");
            // 40 m between neighboring ring nodes' centers minus 2 x 20 m radius = 20 m gap: every grid neighbor is linked; Served1 is run 0.
            int bigServed = 0;
            for (int i = 0; i < big.Tops.Count; i++)
                if (big.Served(i)) bigServed++;
            Eq(200, bigServed, W, "the connected grid is served through one root");
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            big.Solve();
            double ms2 = (System.Diagnostics.Stopwatch.GetTimestamp() - t1) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "INFO  served-link/SL-8 cost: same inputs again (graph kept) {0:F3} ms", ms2));
        }
    }
}
