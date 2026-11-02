// Pure: a groomer's drive path from a run's ribbon outline. The chordal axis of the outline's constrained Delaunay triangulation, the
// longest route through it, trimmed at both ends, smoothed, resampled at a uniform step and checked inside the outline. Hand-drawn and
// generated tracks take the same route; a ring that is not a corridor returns null and a reason, never a wild path.
// Linked into tests/ResortBatch and tests/GradeBatch; `using System; System.Collections.Generic; Unity.Mathematics;` only.
// Engine notes: EngineNotes/GroomerCenterline.md

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>Why an outline has no drive path. None = the path is good.</summary>
    public enum GroomerPathFail : byte
    {
        None = 0,
        NoOutline,        // null, or fewer than three distinct nodes
        TooManyNodes,     // over GroomerCenterline.MaxOutlineNodes
        NotFinite,        // a NaN or infinite node
        NoArea,           // collinear or smaller than MinAreaM2
        NotSimple,        // two non-adjacent edges cross or touch
        NoTriangulation,  // ear clipping found no ear, or the triangles do not connect
        NotACorridor,     // a blob, not a track (MinAspect), or a route longer than the outline itself
        TooShort,         // under MinPathLengthM after the end trim
        LeftOutline,      // a path point fell outside the outline (should not happen; guards every rule above)
    }

    /// <summary>A drive path: points at one uniform step, so a position lookup is O(1).</summary>
    public sealed class GroomerPath
    {
        public readonly float2[] Points;
        public readonly float StepM;
        public readonly float LengthM;
        /// <summary>The outline's mean width (area / route length), m.</summary>
        public readonly float WidthM;

        public GroomerPath(float2[] points, float stepM, float widthM)
        {
            Points = points;
            StepM = stepM;
            LengthM = stepM * (points.Length - 1);
            WidthM = widthM;
        }

        /// <summary>The point at <paramref name="s"/> meters along the path, clamped to the ends.</summary>
        public float2 At(double s)
        {
            int last = Points.Length - 1;
            if (!(s > 0.0))
                return Points[0];
            double f = s / StepM;
            if (f >= last)
                return Points[last];
            int i = (int)f;
            return math.lerp(Points[i], Points[i + 1], (float)(f - i));
        }

        /// <summary>The unit direction of travel at <paramref name="s"/>: the chord from s - half to s + half, clamped to the ends. No state,
        /// so the same s always gives the same heading, and the chord spreads a bend over its own length.</summary>
        public float2 Direction(double s, float chordHalfM)
        {
            float2 d = At(s + chordHalfM) - At(s - chordHalfM);
            float len = math.length(d);
            if (len > 1e-4f)
                return d / len;
            float2 whole = Points[Points.Length - 1] - Points[0];
            float wholeLen = math.length(whole);
            return wholeLen > 1e-4f ? whole / wholeLen : new float2(0f, 1f);
        }
    }

    /// <summary>The centerline of a closed ring. See the engine notes for why each step exists.</summary>
    public static class GroomerCenterline
    {
        public const int MaxOutlineNodes = 4000;
        /// <summary>Budget for the nodes the subdivision adds (keeps the O(n^2) steps bounded on a very long track).</summary>
        public const int MaxAddedNodes = 1500;
        public const float MinAreaM2 = 20f;
        /// <summary>Route length / mean width below this is a blob with no direction, not a track.</summary>
        public const float MinAspect = 2.5f;
        /// <summary>The shortest drive worth a groomer: a few of its own lengths.</summary>
        public const float MinPathLengthM = 30f;
        /// <summary>The end trim is half the mean width (the fan toward the corners ends there), within these. The minimum keeps the machine's
        /// nose inside the end of a narrow track.</summary>
        public const float EndTrimMinM = 4f;
        public const float EndTrimMaxM = 24f;
        /// <summary>Long edges are subdivided to this share of the mean width, within the two limits, or a long straight edge gives a chord
        /// across the whole track instead of a ladder.</summary>
        public const float SubdivideWidthShare = 0.75f;
        public const float SubdivideMinM = 3f;
        public const float SubdivideMaxM = 40f;
        public const float PathStepM = 2f;
        public const int MaxPathPoints = 2048;
        /// <summary>Moving average over the path: half a mean width, within the two limits, three passes (about a Gaussian).</summary>
        public const float SmoothWindowMinM = 4f;
        public const float SmoothWindowMaxM = 12f;
        public const int SmoothPasses = 3;

        private const double DuplicateM = 0.05;
        private const double AreaEps = 1e-6;
        private const double InCircleEps = 1e-6;
        private const int MaxFlipPasses = 200;

        /// <summary>The path of a ring (no closing duplicate needed, either winding), or null and the reason. Pure, deterministic, no state.</summary>
        public static GroomerPath Build(float2[] outline, out GroomerPathFail fail)
        {
            double2[] ring = Clean(outline, out fail);
            if (ring == null)
                return null;

            double area = SignedArea(ring);
            if (Math.Abs(area) < MinAreaM2)
            {
                fail = GroomerPathFail.NoArea;
                return null;
            }
            if (area < 0.0)
                Array.Reverse(ring);
            area = Math.Abs(area);

            if (!IsSimple(ring))
            {
                fail = GroomerPathFail.NotSimple;
                return null;
            }

            double perimeter = Perimeter(ring);
            double2[] fine = Subdivide(ring, perimeter, area);
            var tris = new List<int>(3 * fine.Length);
            if (!EarClip(fine, tris))
            {
                fail = GroomerPathFail.NoTriangulation;
                return null;
            }
            int[] triangles = tris.ToArray();
            int[] neighbors = BuildNeighbors(triangles);
            if (neighbors == null)
            {
                fail = GroomerPathFail.NoTriangulation;
                return null;
            }
            MakeDelaunay(fine, triangles, neighbors);

            double2[] route = LongestRoute(fine, triangles, neighbors, out bool connected);
            if (!connected)
            {
                fail = GroomerPathFail.NoTriangulation;
                return null;
            }
            double routeLength = route == null ? 0.0 : Length(route);
            if (!(routeLength > 0.0) || routeLength * routeLength < MinAspect * area || routeLength > perimeter)
            {
                fail = GroomerPathFail.NotACorridor;
                return null;
            }

            double width = area / routeLength;
            double trim = Math.Min(Math.Max(0.5 * width, EndTrimMinM), EndTrimMaxM);
            double kept = routeLength - 2.0 * trim;
            if (kept < MinPathLengthM)
            {
                fail = GroomerPathFail.TooShort;
                return null;
            }

            int count = (int)Math.Min(Math.Ceiling(kept / PathStepM) + 1.0, MaxPathPoints);
            double step = kept / (count - 1);
            double2[] raw = Resample(route, trim, routeLength - trim, count);
            double window = Math.Min(Math.Max(0.5 * width, SmoothWindowMinM), SmoothWindowMaxM);
            int half = (int)Math.Round(window / step);
            // The smoothest version that stays inside: a moving average cuts the corner of a tight bend. Half == 0 is the raw chord route.
            while (true)
            {
                double2[] candidate = half > 0 ? Resample(Smooth(raw, half), count) : raw;
                if (AllInside(candidate, ring))
                {
                    double candidateLength = Length(candidate);
                    if (candidateLength < MinPathLengthM)
                    {
                        fail = GroomerPathFail.TooShort;
                        return null;
                    }
                    var points = new float2[count];
                    for (int i = 0; i < count; i++)
                        points[i] = (float2)candidate[i];
                    fail = GroomerPathFail.None;
                    return new GroomerPath(points, (float)(candidateLength / (count - 1)), (float)width);
                }
                if (half == 0)
                {
                    fail = GroomerPathFail.LeftOutline;
                    return null;
                }
                half /= 2;
            }
        }

        // ---- the ring -------------------------------------------------------------------------------------------------------------

        /// <summary>Finite nodes without consecutive duplicates, a closing duplicate or zero-width out-and-back spikes.</summary>
        private static double2[] Clean(float2[] outline, out GroomerPathFail fail)
        {
            fail = GroomerPathFail.NoOutline;
            if (outline == null || outline.Length < 3)
                return null;
            if (outline.Length > MaxOutlineNodes)
            {
                fail = GroomerPathFail.TooManyNodes;
                return null;
            }
            var list = new List<double2>(outline.Length);
            for (int i = 0; i < outline.Length; i++)
            {
                if (!math.all(math.isfinite(outline[i])))
                {
                    fail = GroomerPathFail.NotFinite;
                    return null;
                }
                list.Add(outline[i]);
            }
            // One sweep after another until nothing moves: removing a spike leaves a duplicate behind, and the ring is cyclic.
            bool removed = true;
            while (removed && list.Count >= 3)
            {
                removed = false;
                for (int i = list.Count - 1; i >= 0 && list.Count >= 3; i--)
                {
                    int n = list.Count;
                    double2 a = list[(i + n - 1) % n], b = list[i], c = list[(i + 1) % n];
                    bool duplicate = math.distance(a, b) <= DuplicateM;
                    bool spike = Math.Abs(Cross(a, b, c)) <= AreaEps && math.dot(a - b, c - b) > 0.0;
                    if (duplicate || spike)
                    {
                        list.RemoveAt(i);
                        removed = true;
                    }
                }
            }
            if (list.Count < 3)
                return null;
            fail = GroomerPathFail.None;
            return list.ToArray();
        }

        private static double SignedArea(double2[] p)
        {
            double a = 0.0;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                a += p[j].x * p[i].y - p[i].x * p[j].y;
            return 0.5 * a;
        }

        private static double Perimeter(double2[] p)
        {
            double sum = 0.0;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                sum += math.distance(p[j], p[i]);
            return sum;
        }

        private static double Length(double2[] p)
        {
            double sum = 0.0;
            for (int i = 1; i < p.Length; i++)
                sum += math.distance(p[i - 1], p[i]);
            return sum;
        }

        /// <summary>Twice the signed area of a, b, c: positive when counterclockwise.</summary>
        private static double Cross(double2 a, double2 b, double2 c)
        {
            return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        }

        /// <summary>No two non-adjacent edges cross or touch (the rule the engine holds a finished area to, ValidationHelpers.cs:887-943).</summary>
        private static bool IsSimple(double2[] p)
        {
            int n = p.Length;
            for (int i = 0; i < n; i++)
            {
                double2 a = p[i], b = p[(i + 1) % n];
                double2 lo = math.min(a, b), hi = math.max(a, b);
                for (int j = i + 2; j < n; j++)
                {
                    if (i == 0 && j == n - 1)
                        continue;
                    double2 c = p[j], d = p[(j + 1) % n];
                    if (math.any(math.max(c, d) < lo) || math.any(math.min(c, d) > hi))
                        continue;
                    if (SegmentsTouch(a, b, c, d))
                        return false;
                }
            }
            return true;
        }

        private static bool SegmentsTouch(double2 a, double2 b, double2 c, double2 d)
        {
            double d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
            if (((d1 > 0.0 && d2 < 0.0) || (d1 < 0.0 && d2 > 0.0)) && ((d3 > 0.0 && d4 < 0.0) || (d3 < 0.0 && d4 > 0.0)))
                return true;
            return (d1 == 0.0 && OnBox(c, d, a)) || (d2 == 0.0 && OnBox(c, d, b)) || (d3 == 0.0 && OnBox(a, b, c)) || (d4 == 0.0 && OnBox(a, b, d));
        }

        private static bool OnBox(double2 a, double2 b, double2 p)
        {
            return math.all(p >= math.min(a, b)) && math.all(p <= math.max(a, b));
        }

        /// <summary>Long edges cut to about 3/4 of the mean width. The added nodes are exactly collinear with their edge.</summary>
        private static double2[] Subdivide(double2[] ring, double perimeter, double area)
        {
            double width = 2.0 * area / perimeter;
            double spacing = Math.Min(Math.Max(SubdivideWidthShare * width, SubdivideMinM), SubdivideMaxM);
            spacing = Math.Max(spacing, perimeter / MaxAddedNodes);
            var o = new List<double2>(ring.Length * 2);
            for (int i = 0; i < ring.Length; i++)
            {
                double2 a = ring[i], b = ring[(i + 1) % ring.Length];
                o.Add(a);
                int cuts = (int)Math.Ceiling(math.distance(a, b) / spacing);
                for (int k = 1; k < cuts; k++)
                    o.Add(math.lerp(a, b, (double)k / cuts));
            }
            return o.ToArray();
        }

        // ---- the triangles --------------------------------------------------------------------------------------------------------

        /// <summary>Ear clipping over a counterclockwise ring. A node that lies on a would-be diagonal blocks it, so collinear nodes stay in the
        /// triangulation. Stuck (rounding on near-collinear runs): drop one collinear node, which only removes a subdivision.</summary>
        private static bool EarClip(double2[] p, List<int> tris)
        {
            int n = p.Length;
            var prev = new int[n];
            var next = new int[n];
            for (int i = 0; i < n; i++)
            {
                prev[i] = (i + n - 1) % n;
                next[i] = (i + 1) % n;
            }
            int remaining = n, cur = 0, sinceClip = 0;
            while (remaining > 3)
            {
                int a = prev[cur], b = cur, c = next[cur];
                if (IsEar(p, next, a, b, c))
                {
                    tris.Add(a);
                    tris.Add(b);
                    tris.Add(c);
                    next[a] = c;
                    prev[c] = a;
                    remaining--;
                    sinceClip = 0;
                    cur = next[c];   // walk on past c: ears spread along the ring instead of fanning from one node
                    continue;
                }
                cur = c;
                if (++sinceClip <= remaining)
                    continue;
                bool dropped = false;
                int v = cur;
                for (int i = 0; i < remaining && !dropped; i++)
                {
                    if (Math.Abs(Cross(p[prev[v]], p[v], p[next[v]])) <= 1e-3)
                    {
                        next[prev[v]] = next[v];
                        prev[next[v]] = prev[v];
                        cur = next[v];
                        remaining--;
                        sinceClip = 0;
                        dropped = true;
                    }
                    v = next[v];
                }
                if (!dropped)
                    return false;
            }
            int la = prev[cur], lb = cur, lc = next[cur];
            if (Cross(p[la], p[lb], p[lc]) > AreaEps)
            {
                tris.Add(la);
                tris.Add(lb);
                tris.Add(lc);
            }
            return tris.Count >= 3;
        }

        private static bool IsEar(double2[] p, int[] next, int a, int b, int c)
        {
            double2 pa = p[a], pb = p[b], pc = p[c];
            if (!(Cross(pa, pb, pc) > AreaEps))
                return false;
            double2 lo = math.min(math.min(pa, pb), pc), hi = math.max(math.max(pa, pb), pc);
            for (int v = next[c]; v != a; v = next[v])
            {
                double2 q = p[v];
                if (math.any(q < lo) || math.any(q > hi))
                    continue;
                if (Cross(pa, pb, q) >= 0.0 && Cross(pb, pc, q) >= 0.0 && Cross(pc, pa, q) >= 0.0)
                    return false;
            }
            return true;
        }

        /// <summary>For triangle t, entry 3t + k = the triangle across the edge v[k] -> v[k + 1], or -1 on the boundary. Null when an edge is
        /// shared by more than two triangles.</summary>
        private static int[] BuildNeighbors(int[] t)
        {
            var nbr = new int[t.Length];
            var open = new Dictionary<long, int>(t.Length);
            for (int e = 0; e < t.Length; e++)
                nbr[e] = -1;
            for (int e = 0; e < t.Length; e++)
            {
                int tri = e / 3, k = e % 3;
                int u = t[e], v = t[3 * tri + (k + 1) % 3];
                long key = ((long)Math.Min(u, v) << 32) | (uint)Math.Max(u, v);
                if (open.TryGetValue(key, out int other))
                {
                    if (other < 0)
                        return null;
                    nbr[e] = other / 3;
                    nbr[other] = tri;
                    open[key] = -1;
                }
                else
                {
                    open.Add(key, e);
                }
            }
            return nbr;
        }

        /// <summary>Lawson flips to the constrained Delaunay triangulation: ear clipping leaves slivers along the track whose midpoints sit
        /// off center. A bounded number of passes: an unfinished triangulation is still valid, only less centered.</summary>
        private static void MakeDelaunay(double2[] p, int[] t, int[] nbr)
        {
            int triCount = t.Length / 3;
            for (int pass = 0; pass < MaxFlipPasses; pass++)
            {
                bool flipped = false;
                for (int tri = 0; tri < triCount; tri++)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        int other = nbr[3 * tri + k];
                        if (other > tri && TryFlip(p, t, nbr, tri, k, other))
                        {
                            flipped = true;
                            break;   // tri's corners changed: its other edges come round again on the next pass
                        }
                    }
                }
                if (!flipped)
                    return;
            }
        }

        private static bool TryFlip(double2[] p, int[] t, int[] nbr, int tri, int k, int other)
        {
            int m = -1;
            for (int i = 0; i < 3; i++)
                if (nbr[3 * other + i] == tri)
                    m = i;
            if (m < 0)
                return false;
            int a = t[3 * tri + k], b = t[3 * tri + (k + 1) % 3], c = t[3 * tri + (k + 2) % 3];
            int d = t[3 * other + (m + 2) % 3];
            // The quad a, d, b, c must stay two counterclockwise triangles, and d must lie inside the circle through a, b, c.
            if (!(Cross(p[c], p[a], p[d]) > AreaEps) || !(Cross(p[d], p[b], p[c]) > AreaEps) || !(InCircle(p[a], p[b], p[c], p[d]) > InCircleEps))
                return false;
            int nBC = nbr[3 * tri + (k + 1) % 3], nCA = nbr[3 * tri + (k + 2) % 3];
            int nAD = nbr[3 * other + (m + 1) % 3], nDB = nbr[3 * other + (m + 2) % 3];
            t[3 * tri] = c;
            t[3 * tri + 1] = a;
            t[3 * tri + 2] = d;
            nbr[3 * tri] = nCA;
            nbr[3 * tri + 1] = nAD;
            nbr[3 * tri + 2] = other;
            t[3 * other] = d;
            t[3 * other + 1] = b;
            t[3 * other + 2] = c;
            nbr[3 * other] = nDB;
            nbr[3 * other + 1] = nBC;
            nbr[3 * other + 2] = tri;
            Repoint(nbr, nAD, other, tri);
            Repoint(nbr, nBC, tri, other);
            return true;
        }

        private static void Repoint(int[] nbr, int tri, int from, int to)
        {
            if (tri < 0)
                return;
            for (int i = 0; i < 3; i++)
                if (nbr[3 * tri + i] == from)
                    nbr[3 * tri + i] = to;
        }

        /// <summary>Positive when d lies inside the circle through the counterclockwise triangle a, b, c.</summary>
        private static double InCircle(double2 a, double2 b, double2 c, double2 d)
        {
            double2 pa = a - d, pb = b - d, pc = c - d;
            return math.dot(pa, pa) * (pb.x * pc.y - pc.x * pb.y)
                 - math.dot(pb, pb) * (pa.x * pc.y - pc.x * pa.y)
                 + math.dot(pc, pc) * (pa.x * pb.y - pb.x * pa.y);
        }

        // ---- the route ------------------------------------------------------------------------------------------------------------

        private static double2 Centroid(double2[] p, int[] t, int tri)
        {
            return (p[t[3 * tri]] + p[t[3 * tri + 1]] + p[t[3 * tri + 2]]) / 3.0;
        }

        private static double2 EdgeMid(double2[] p, int[] t, int tri, int k)
        {
            return 0.5 * (p[t[3 * tri + k]] + p[t[3 * tri + (k + 1) % 3]]);
        }

        /// <summary>The longest walk through the triangle tree, as the midpoints of the edges it crosses plus the centroid of its first and last
        /// triangle. Side branches and the stubs of a bulge are shorter, so the walk leaves them out. connected = false when the triangles
        /// do not form one piece. Null when there is no walk at all.</summary>
        private static double2[] LongestRoute(double2[] p, int[] t, int[] nbr, out bool connected)
        {
            int triCount = t.Length / 3;
            var parent = new int[triCount];
            var dist = new double[triCount];
            var entry = new double2[triCount];
            var stack = new int[triCount];
            int a = Farthest(0, p, t, nbr, parent, dist, entry, stack, out int visited);
            connected = visited == triCount;
            if (!connected)
                return null;
            int b = Farthest(a, p, t, nbr, parent, dist, entry, stack, out _);
            if (a == b)
                return null;
            var walk = new List<double2>(triCount + 2) { Centroid(p, t, b) };
            for (int tri = b; tri != a; tri = parent[tri])
                walk.Add(entry[tri]);
            walk.Add(Centroid(p, t, a));
            return walk.ToArray();
        }

        /// <summary>An explicit stack: a long track is a chain thousands of triangles deep. First found wins a tie, so the result is deterministic.</summary>
        private static int Farthest(int start, double2[] p, int[] t, int[] nbr, int[] parent, double[] dist, double2[] entry, int[] stack, out int visited)
        {
            for (int i = 0; i < dist.Length; i++)
                dist[i] = -1.0;
            int top = 0, best = start;
            double bestScore = -1.0;
            stack[top++] = start;
            parent[start] = -1;
            dist[start] = 0.0;
            entry[start] = Centroid(p, t, start);
            visited = 0;
            while (top > 0)
            {
                int tri = stack[--top];
                visited++;
                double score = dist[tri] + math.distance(entry[tri], Centroid(p, t, tri));
                if (score > bestScore)
                {
                    bestScore = score;
                    best = tri;
                }
                for (int k = 0; k < 3; k++)
                {
                    int u = nbr[3 * tri + k];
                    if (u < 0 || dist[u] >= 0.0)
                        continue;
                    parent[u] = tri;
                    entry[u] = EdgeMid(p, t, tri, k);
                    dist[u] = dist[tri] + math.distance(entry[tri], entry[u]);
                    stack[top++] = u;
                }
            }
            return best;
        }

        /// <summary>count points at a uniform step between meters s0 and s1 of the polyline.</summary>
        private static double2[] Resample(double2[] poly, double s0, double s1, int count)
        {
            var o = new double2[count];
            int seg = 0;
            double segStart = 0.0;
            double segLen = math.distance(poly[0], poly[1]);
            for (int i = 0; i < count; i++)
            {
                double s = s0 + (s1 - s0) * i / (count - 1);
                while (s > segStart + segLen && seg < poly.Length - 2)
                {
                    segStart += segLen;
                    seg++;
                    segLen = math.distance(poly[seg], poly[seg + 1]);
                }
                double f = segLen > 0.0 ? math.saturate((s - segStart) / segLen) : 0.0;
                o[i] = math.lerp(poly[seg], poly[seg + 1], f);
            }
            return o;
        }

        private static double2[] Resample(double2[] poly, int count)
        {
            return Resample(poly, 0.0, Length(poly), count);
        }

        /// <summary>Moving average, SmoothPasses times. The window shrinks toward the ends, symmetrically, so the ends stay put and nothing drifts.</summary>
        private static double2[] Smooth(double2[] p, int half)
        {
            int n = p.Length;
            var a = (double2[])p.Clone();
            var sum = new double2[n + 1];
            for (int pass = 0; pass < SmoothPasses; pass++)
            {
                for (int i = 0; i < n; i++)
                    sum[i + 1] = sum[i] + a[i];
                var b = new double2[n];
                for (int i = 0; i < n; i++)
                {
                    int h = Math.Min(half, Math.Min(i, n - 1 - i));
                    b[i] = (sum[i + h + 1] - sum[i - h]) / (2 * h + 1);
                }
                a = b;
            }
            return a;
        }

        private static bool AllInside(double2[] pts, double2[] ring)
        {
            for (int i = 0; i < pts.Length; i++)
                if (!Inside(pts[i], ring))
                    return false;
            return true;
        }

        private static bool Inside(double2 q, double2[] ring)
        {
            bool inside = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                double2 a = ring[i], b = ring[j];
                if ((a.y > q.y) != (b.y > q.y) && q.x < (b.x - a.x) * (q.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }
    }
}
