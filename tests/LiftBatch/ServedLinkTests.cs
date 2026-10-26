// A run connected to a served run is served: LiftServedRule.RunLinks.Build + AssignLinked, the one rule the attraction and
// stats paths both call. Where-tags `served-link/...`.
//   SL-1..3 connectors, unserved neighbors, chains and gaps    SL-4 distance boundary (24.99 / 25 m linked, 25.01 m not;
//   nodes are compared, not edges)    SL-5 other ski area or area id < 0 never links    SL-6 outline overlap without near nodes
//   SL-7 no links reproduces Assign    SL-8 graph reuse, size mismatch, cycles, cost of 200 runs x 120 nodes
// Downhill (2026-10-25): SL-1..8 pass no heights (flat terrain), which reproduces the old linking exactly. With node heights:
//   SL-9 BelowJunction boundaries    SL-10 a run touching only at its bottom is not served    SL-11 a run joined mid-way is served,
//   and service never flows up it past its entry    SL-12 downhill chains    SL-13 a terrain re-snap (new height array) rebuilds
//   SL-14 (quick runs fix round) a LEVEL cat track crossing a run at mid height vs in its bottom 20 m (30.0 / 29.9 m boundary), and two
//   parallel runs side by side on a sidehill sharing an edge: the "board highest" pick serves both from one lift, whichever is served

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
            Sl9Below();
            Sl10BottomTouch();
            Sl11MidJoin();
            Sl12Chain();
            Sl13HeightReuse();
            Sl14LevelCrossingAndNeighbours();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);

        private static void Eq<T>(T expected, T actual, string where, string what)
        {
            Check(EqualityComparer<T>.Default.Equals(expected, actual), where, what + ": expected " + expected + ", got " + actual);
        }


        /// <summary>One lift: a chord (0,0) to (0,500), top station y 300, powered; a run top within x 150 and y 315 is served.</summary>
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
            public readonly List<float[]> Heights = new List<float[]>();
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
                Heights.Add(null);
                Areas.Add(area);
                return Tops.Count - 1;
            }

            /// <summary>A run on the test mountain: node heights from Slope(z).</summary>
            public int AddSloped(float3 top, float2[] outline)
            {
                int i = Add(top, outline);
                Heights[i] = SlopeHeights(outline);
                return i;
            }

            public Scene Solve()
            {
                Links.Build(Outlines, Heights, Areas);
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


        /// <summary>The test mountain: y = 1000 - 0.5 z (a 50 % fall line toward +z; x is level).</summary>
        private static float[] SlopeHeights(float2[] ring)
        {
            var y = new float[ring.Length];
            for (int k = 0; k < ring.Length; k++)
                y[k] = 1000f - 0.5f * ring[k].y;
            return y;
        }

        /// <summary>y = 1000 + 0.5 z: the test mountain reversed.</summary>
        private static float[] Reverse(float2[] ring)
        {
            var y = new float[ring.Length];
            for (int k = 0; k < ring.Length; k++)
                y[k] = 1000f + 0.5f * ring[k].y;
            return y;
        }

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
            Eq(a, s.Root[c], W, "connector root: the first (lower) served neighbor");
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
            Check(!s.Served(f), W, "a run above the lift's top margin with no neighbor is unserved");
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

        private static void Sl9Below()
        {
            const string W = "served-link/SL-9";
            Eq(30f, LiftServedRule.MinBelowM, W, "MinBelowM");
            Eq(3f, LiftServedRule.BelowDropDivisor, W, "BelowDropDivisor");
            Eq(3f, LiftServedRule.ClimbToleranceM, W, "ClimbToleranceM");
            // Drop 150: the need is 30 m (the cap). Inclusive.
            Check(LiftServedRule.BelowJunction(830f, 800f, 950f), W, "drop 150, 30 m below: served");
            Check(!LiftServedRule.BelowJunction(829.9f, 800f, 950f), W, "drop 150, 29.9 m below: not served");
            // Drop 60: the need is a third, 20 m.
            Check(LiftServedRule.BelowJunction(820f, 800f, 860f), W, "drop 60, 20 m below: served");
            Check(!LiftServedRule.BelowJunction(819.9f, 800f, 860f), W, "drop 60, 19.9 m below: not served");
            Check(!LiftServedRule.BelowJunction(800f, 800f, 860f), W, "entry at the run's bottom: not served");
            // A flat outline (drop 0) needs nothing: the flat-terrain case of SL-1..8.
            Check(LiftServedRule.BelowJunction(0f, 0f, 0f), W, "flat outline: served");
            Check(!LiftServedRule.BelowJunction(float.NaN, 800f, 860f), W, "NaN entry: not served");
        }

        private static void Sl10BottomTouch()
        {
            const string W = "served-link/SL-10";
            // A (served) spans z 100-200 (y 950-900). B lies ABOVE it, z -10 to 90 (y 1005-955): its bottom nodes are 10 m from A's top.
            var s = new Scene();
            int a = s.AddSloped(Served1, Rect(90f, 100f, 130f, 200f));
            int b = s.AddSloped(Far, Rect(90f, -10f, 130f, 90f));
            s.Solve();
            Eq(1, s.Links.LinkCount, W, "A and B are still linked (the graph is geometric)");
            Check(s.Served(a), W, "A direct");
            Check(!s.Served(b), W, "a run touching a served run only at its own bottom is NOT served");
            Eq(-1, s.Via[b], W, "B via");
            // The same pair without heights (flat): served, the old rule.
            var flat = new Scene();
            flat.Add(Served1, Rect(90f, 100f, 130f, 200f));
            int fb = flat.Add(Far, Rect(90f, -10f, 130f, 90f));
            flat.Solve();
            Check(flat.Served(fb), W, "without heights the same pair is served (flat terrain = the old linking)");
            // B below A, touching A's bottom with its own top: served (all of it lies below the junction).
            var down = new Scene();
            down.AddSloped(Served1, Rect(90f, 100f, 130f, 200f));
            int db = down.AddSloped(Far, Rect(90f, 210f, 130f, 400f));
            down.Solve();
            Check(down.Served(db), W, "a run hanging below a served run's bottom is served");
        }

        private static void Sl11MidJoin()
        {
            const string W = "served-link/SL-11";
            // A (served): x 0-40, z 0-400 (y 1000-800). B: x 30-100, z -200 to 300 (y 1100-850): A's top corner (40, 0) lies inside B and
            // B's left edge crosses A's top edge at (30, 0), so the entry onto B is y 1000, mid-way down B; 150 m of B lies below it.
            var s = new Scene();
            int a = s.AddSloped(Served1, Rect(0f, 0f, 40f, 400f));
            int b = s.AddSloped(Far, Rect(30f, -200f, 100f, 300f));
            // D touches B only at B's top (z -215 vs -200, 15 m): above B's entry, so service cannot flow up B into D.
            int d = s.AddSloped(Far, Rect(30f, -260f, 100f, -215f));
            // E touches B's bottom corner (100, 300) with its top corner (110, 300), below B's entry: x 110-150, z 300-500 (y 850-750).
            int e = s.AddSloped(Far, Rect(110f, 300f, 150f, 500f));
            s.Solve();
            Eq(3, s.Links.LinkCount, W, "links A-B, B-D, B-E");
            Check(s.Served(a), W, "A direct");
            Check(s.Served(b), W, "a run joined mid-way (150 m below the junction) is served");
            Eq(a, s.Via[b], W, "B via A");
            Check(!s.Served(d), W, "service never flows UP B past its entry: D (linked at B's top only) is not served");
            Check(s.Served(e), W, "E, joined below B's entry, is served through B");
            Eq(b, s.Via[e], W, "E via B");
            Eq(a, s.Root[e], W, "E root A");
            // D is served from above: F, directly served, touches D's top (10 m).
            var t = new Scene();
            t.AddSloped(Served1, Rect(0f, 0f, 40f, 400f));
            int tb = t.AddSloped(Far, Rect(30f, -200f, 100f, 300f));
            int td = t.AddSloped(Far, Rect(30f, -260f, 100f, -215f));
            int tf = t.AddSloped(Served2, Rect(40f, -320f, 100f, -270f));
            t.Solve();
            Check(t.Served(tf) && t.Served(td), W, "D served from above through F");
            Eq(tf, t.Via[td], W, "D via F");
            Check(t.Served(tb), W, "B served");
        }

        private static void Sl12Chain()
        {
            const string W = "served-link/SL-12";
            // A (served) z 0-200 (y 1000-900). B below it, z 210-400: entry y 895 at its top, served. C beside B's top, x 60-100,
            // z 220-260 (y 890-870): its node (60, 220) is 22.4 m from B's node (40, 210) at y 895, B's entry height: usable; C's drop
            // 20, need 6.7, below 20: served. H touches C's bottom node (100, 260) with its top (110, 270), 14.1 m; H z 270-500.
            var s = new Scene();
            int a = s.AddSloped(Served1, Rect(0f, 0f, 40f, 200f));
            int b = s.AddSloped(Far, Rect(0f, 210f, 40f, 400f));
            int c = s.AddSloped(Far, Rect(60f, 220f, 100f, 260f));
            int h = s.AddSloped(Far, Rect(110f, 270f, 150f, 500f));
            s.Solve();
            Check(s.Served(a) && s.Served(b) && s.Served(c) && s.Served(h), W, "the downhill chain A - B - C - H is served");
            Eq(a, s.Via[b], W, "B via A");
            Eq(b, s.Via[c], W, "C via B");
            Eq(c, s.Via[h], W, "H via C");
            Eq(a, s.Root[h], W, "H root A");
            // The mountain reversed (falling toward -z): B now rises away from A and touches it at B's own bottom.
            var up = new Scene();
            int ua = up.Add(Served1, Rect(0f, 0f, 40f, 200f));
            up.Heights[ua] = Reverse(up.Outlines[ua]);
            int ub = up.Add(Far, Rect(0f, 210f, 40f, 400f));
            up.Heights[ub] = Reverse(up.Outlines[ub]);
            up.Solve();
            Check(!up.Served(ub), W, "a run that rises away from the served run (bottom touch) is not served");
        }

        private static void Sl13HeightReuse()
        {
            const string W = "served-link/SL-13";
            var s = new Scene();
            s.AddSloped(Served1, Rect(90f, 100f, 130f, 200f));
            int b = s.AddSloped(Far, Rect(90f, -10f, 130f, 90f));
            s.Solve();
            Eq(1, s.Links.Rebuilds, W, "first Build");
            s.Solve();
            Eq(1, s.Links.Rebuilds, W, "same outline and height arrays: kept");
            Check(!s.Served(b), W, "B touches at its bottom: not served");
            // A terrain re-snap: the catalog hands a new height array (same xz array). B now falls AWAY from A (y 950 at its z 90
            // nodes, beside A's top at 950; 900 at z -10): the graph rebuilds and B lies below the junction.
            var resnap = new float[s.Outlines[b].Length];
            for (int k = 0; k < resnap.Length; k++)
                resnap[k] = 905f + 0.5f * s.Outlines[b][k].y;
            s.Heights[b] = resnap;
            s.Solve();
            Eq(2, s.Links.Rebuilds, W, "a replaced height array rebuilds");
            Check(s.Served(b), W, "after the re-snap B lies below the junction: served");
            // A height array of the wrong length reads as flat (no heights) for that run.
            s.Heights[b] = new float[2];
            s.Solve();
            Check(s.Served(b), W, "a mismatched height array reads flat (served, as without heights)");
        }

        /// <summary>A level cat track ring of four nodes (its two ends only, as GradeRibbon simplifies a straight one): x0..x1, z zc +- 1.</summary>
        private static float2[] LevelTrack(float x0, float x1, float zc) => Rect(x0, zc - 1f, x1, zc + 1f);

        private static float[] Level(float2[] ring, float y)
        {
            var h = new float[ring.Length];
            for (int k = 0; k < h.Length; k++)
                h[k] = y;
            return h;
        }

        /// <summary>The same Build + AssignLinked as Scene.Solve with the cat track mask (Scene.Solve passes none).</summary>
        private static void SolveWithTracks(Scene s, List<bool> tracks)
        {
            s.Links.Build(s.Outlines, s.Heights, s.Areas);
            LiftServedRule.AssignLinked(s.Tops, s.Links, s.Lifts, s.Chords, s.ServingLift, s.Distance, s.ServedCount, s.Root, s.Via, tracks);
        }

        /// <summary>SL-14 (a): A (served directly, x 0-40, z 0-400, y 1000-800) and a LEVEL cat track at y 900 (A's height at z 200) from x 10
        /// to 510 that crosses A and then runs X: x 455-475, from z 0 down to zBottom on the same mountain (y = 1000 - 0.5 z). The track
        /// meets X at its edge crossing, 900 m high on the track and 899.5-900.5 on X: the junction is p = 900. X is served iff
        /// p - lowest(X) >= min(30, drop / 3): mid height (zBottom 400, lowest 800: 100 m below) yes; zBottom 260 (lowest 870, exactly 30.0 m
        /// below) yes; 259.8 (lowest 870.1, 29.9 m) no; 240 (the bottom 20 m, 20 m below) no.
        /// (b) Two runs side by side on a sidehill (y = 1000 - 0.5 z - 0.2 x), sharing the edge x = 40 and its corner nodes: one lift serves
        /// either of them directly and the other is served through the shared edge. A skier boards the neighbor at its HIGHEST shared node
        /// (992 m at z 0), not the lowest (792 m at z 400, only 8 m above its bottom), so both are served in both directions.</summary>
        private static void Sl14LevelCrossingAndNeighbours()
        {
            const string W = "served-link/SL-14";
            foreach (var (zBottom, lowest, served, what) in new (float, float, bool, string)[]
            {
                (400f, 800f, true, "a level crossing at mid height (100 m of the run below it)"),
                (260f, 870f, true, "exactly 30.0 m of the run below the crossing"),
                (259.8f, 870.1f, false, "29.9 m of the run below the crossing"),
                (240f, 880f, false, "the crossing in the run's bottom 20 m"),
            })
            {
                var s = new Scene();
                int a = s.AddSloped(Served1, Rect(0f, 0f, 40f, 400f));
                float2[] ring = LevelTrack(10f, 510f, 200f);
                int t = s.Add(Far, ring);
                s.Heights[t] = Level(ring, 900f);
                int x = s.AddSloped(Far, Rect(455f, 0f, 475f, zBottom));
                float low = float.MaxValue;
                foreach (float y in s.Heights[x])
                    low = math.min(low, y);
                SolveWithTracks(s, new List<bool> { false, true, false });
                Check(math.abs(low - lowest) < 1e-3f, W, "setup: X's lowest node is " + low + ", expected " + lowest);
                Check(s.Served(a) && s.Served(t) && s.Via[t] == a, W, what + ": the level track is served from A (via " + s.Via[t] + ")");
                Check(s.Served(x) == served, W, what + ": X " + (s.Served(x) ? "served" : "NOT served") + ", expected " + (served ? "served" : "not served") +
                      " (junction 900, lowest " + low + ", drop " + (1000f - low) + ")");
                if (served)
                    Eq(t, s.Via[x], W, what + ": X via the track");
            }

            // (b) two parallel runs sharing an edge, on a sidehill with a cross slope
            foreach (bool leftServed in new[] { true, false })
            {
                var s = new Scene();
                float2[] p = Rect(0f, 0f, 40f, 400f), q = Rect(40f, 0f, 80f, 400f);
                int ip = s.Add(leftServed ? Served1 : Far, p);
                int iq = s.Add(leftServed ? Far : Served1, q);
                s.Heights[ip] = SideHeights(p);
                s.Heights[iq] = SideHeights(q);
                s.Solve();
                string who = leftServed ? "the upper run directly served" : "the lower run directly served";
                Eq(1, s.Links.LinkCount, W, who + ": one link along the shared edge");
                Check(s.Served(ip) && s.Served(iq), W, who + ": both runs served (the neighbor boarded at its highest shared node)");
                Eq(leftServed ? ip : iq, s.Via[leftServed ? iq : ip], W, who + ": the other is served through it");
            }
            // The same pair with a denser edge: both rings carry a node at (40, 200) too
            {
                var s = new Scene();
                float2[] p = { new float2(0f, 0f), new float2(40f, 0f), new float2(40f, 200f), new float2(40f, 400f), new float2(0f, 400f) };
                float2[] q = { new float2(40f, 0f), new float2(80f, 0f), new float2(80f, 400f), new float2(40f, 400f), new float2(40f, 200f) };
                int ip = s.Add(Served1, p);
                int iq = s.Add(Far, q);
                s.Heights[ip] = SideHeights(p);
                s.Heights[iq] = SideHeights(q);
                s.Solve();
                Check(s.Served(ip) && s.Served(iq), W, "five-node rings sharing three edge nodes: both served");
            }
        }

        /// <summary>The sidehill: y = 1000 - 0.5 z - 0.2 x (the fall line toward +z, a 20 % cross slope toward +x).</summary>
        private static float[] SideHeights(float2[] ring)
        {
            var y = new float[ring.Length];
            for (int k = 0; k < ring.Length; k++)
                y[k] = 1000f - 0.5f * ring[k].y - 0.2f * ring[k].x;
            return y;
        }
    }
}
