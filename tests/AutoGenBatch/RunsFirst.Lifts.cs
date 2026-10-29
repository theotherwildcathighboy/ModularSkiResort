// RunsFirst lifts (prototype): lifts placed to serve the picked lines, the way a resort is laid out around its terrain.
// A lift top "reaches" every cell a skier gets to from it without climbing (+3 % at most) and without anything steeper than a
// black start (40 %) or crossing a cliff cell: the traverse along a ridge that opens a whole wall or bowl. A line counts for a lift
// from the highest point the top reaches down to where the line (or the drainage below it) passes the lift's bottom station.
// Greedy by new vertical served; stacked lifts fall out of it (a lower lift is credited with the lower parts of long lines).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class RunsFirst
    {
        private const float AccessRadiusM = 1500f, AccessMaxDown = 0.40f, AccessMaxUp = 0.03f, AccessCliff = 0.75f;
        private const float TopLatticeM = 100f, ReturnM = 150f, HubLinkM = 150f, BenchGrade = 0.25f, MinLapM = 60f;
        private const float TowerLiftM = 20f;
        private const float TwinM = 300f;       // a lift whose top AND bottom are this close to an existing one's is a twin, not a new lift

        // hourly capacity relative to a detachable quad (2,400 / h): a tram lap is worth a third of a chair lap to a resort
        private static float Capacity(string type) => type switch { "fixed-grip" => 0.75f, "detachable" => 1f, "gondola" => 0.8f, _ => 0.3f };   // terrain may rise this far above the straight chord (towers carry the rope over it)

        private sealed class Lift
        {
            public int Id;
            public string Type;
            public float3 Bottom, Top;
            public int TopCell, BottomCell;
            public float Length, Rise, Gain, BottomGrade;
            public readonly List<Line> Lines = new List<Line>();
            public readonly List<List<float2>> Traverses = new List<List<float2>>();
        }

        // name, length min / max (m), rise min / max (m), mean grade cap: stage-a-spec 4.4 norms and edge caps
        private static readonly (string Name, float L0, float L1, float R0, float R1, float Cap)[] Types =
        {
            ("fixed-grip", 300f, 1500f, 95f, 512f, 0.8f),
            ("detachable", 600f, 2200f, 95f, 512f, 0.9f),
            ("gondola", 800f, 2700f, 96f, 781f, 1.2f),
            ("tram", 800f, 3000f, 220f, 888f, 2.5f),
        };

        private static int CellOf(Grid g, float2 p)
        {
            int x = (int)Math.Round((p.x - g.OX) / g.CellM), z = (int)Math.Round((p.y - g.OZ) / g.CellM);
            return x < 0 || z < 0 || x >= g.W || z >= g.D ? -1 : z * g.W + x;
        }

        private static List<Lift> PlaceLifts(Grid g, int[] rec, List<Line> all, float km2)
        {
            int n = g.W * g.D;
            var lines = all.FindAll(l => l.Picked);
            var slope = new float[n];
            float hMin = float.MaxValue, hMax = float.MinValue;
            for (int c = 0; c < n; c++)
            {
                if (!g.In[c])
                    continue;
                slope[c] = GradeAt(g, g.World(c));
                hMin = Math.Min(hMin, g.H[c]);
                hMax = Math.Max(hMax, g.H[c]);
            }

            // the way back: each line's points, then the drainage below its end to the region edge
            foreach (Line l in lines)
            {
                l.Flow = new List<float2>();
                int c = CellOf(g, l.Pts[l.Pts.Count - 1].xz);
                for (int k = 0; c >= 0; c = rec[c], k++)
                    if (k % 3 == 0)
                        l.Flow.Add(g.World(c));
            }

            // bottom candidates: hubs (line ends linked within 150 m, the lowest end each) and benches the lines cross
            var bottoms = new List<int>();
            var ends = new List<int>();
            foreach (Line l in lines)
                ends.Add(CellOf(g, l.Pts[l.Pts.Count - 1].xz));
            var hub = new int[ends.Count];
            for (int i = 0; i < ends.Count; i++)
                hub[i] = i;
            for (int i = 0; i < ends.Count; i++)
                for (int j = i + 1; j < ends.Count; j++)
                    if (ends[i] >= 0 && ends[j] >= 0 && math.distance(g.World(ends[i]), g.World(ends[j])) < HubLinkM)
                    {
                        int a = Root(hub, i), b = Root(hub, j);
                        hub[a] = b;
                    }
            var low = new Dictionary<int, int>();
            for (int i = 0; i < ends.Count; i++)
            {
                if (ends[i] < 0)
                    continue;
                int r = Root(hub, i);
                if (!low.TryGetValue(r, out int cur) || g.H[ends[i]] < g.H[cur])
                    low[r] = ends[i];
            }
            bottoms.AddRange(low.Values);
            foreach (Line l in lines)
                for (int i = 0; i < l.Pts.Count; i += 15)
                {
                    int c = CellOf(g, l.Pts[i].xz);
                    if (c >= 0 && g.In[c] && slope[c] < BenchGrade && !Near(g, bottoms, c, 120f))
                        bottoms.Add(c);
                }

            // where each line passes each bottom (first / last index along Pts + Flow; -1 = never)
            int B = bottoms.Count;
            var passFirst = new int[lines.Count, B];
            var passLast = new int[lines.Count, B];
            for (int li = 0; li < lines.Count; li++)
            {
                List<float2> path = LinePath(lines[li]);
                for (int b = 0; b < B; b++)
                {
                    passFirst[li, b] = passLast[li, b] = -1;
                    float2 bp = g.World(bottoms[b]);
                    for (int k = 0; k < path.Count; k++)
                        if (math.distancesq(path[k], bp) < ReturnM * ReturnM)
                        {
                            if (passFirst[li, b] < 0)
                                passFirst[li, b] = k;
                            passLast[li, b] = k;
                        }
                }
            }

            // top candidates on a 100 m lattice: the reach of each, as the highest reached index per line
            int step = Math.Max(1, (int)Math.Round(TopLatticeM / g.CellM));
            var tops = new List<int>();
            var entry = new List<int[]>();
            var dist = new float[n];
            Array.Fill(dist, float.PositiveInfinity);
            for (int z = step / 2; z < g.D; z += step)
                for (int x = step / 2; x < g.W; x += step)
                {
                    int t = z * g.W + x;
                    if (!g.In[t] || slope[t] > 0.6f)
                        continue;
                    List<int> touched = Reach(g, slope, t, dist, null);
                    var e = new int[lines.Count];
                    bool any = false;
                    for (int li = 0; li < lines.Count; li++)
                    {
                        e[li] = -1;
                        List<float3> pts = lines[li].Pts;
                        for (int k = 0; k < pts.Count; k++)
                        {
                            int c = CellOf(g, pts[k].xz);
                            if (c >= 0 && dist[c] < float.PositiveInfinity)
                            {
                                e[li] = k;
                                any = true;
                                break;
                            }
                        }
                    }
                    foreach (int c in touched)
                        dist[c] = float.PositiveInfinity;
                    if (any)
                    {
                        tops.Add(t);
                        entry.Add(e);
                    }
                }

            // greedy: the lift that adds the most new vertical (per line, elevation intervals already lapped don't count twice)
            var covered = new List<(float lo, float hi)>[lines.Count];
            for (int li = 0; li < lines.Count; li++)
                covered[li] = new List<(float, float)>();
            float vertical = hMax - hMin;
            int want = Math.Clamp(Math.Max((int)Math.Round(km2 / 2f), (int)Math.Ceiling(vertical / 400f)), 1, 9);
            var lifts = new List<Lift>();
            float firstGain = 0f;
            while (lifts.Count < want)
            {
                float best = 0f;
                int bt = -1, bb = -1;
                string bestType = null;
                for (int ti = 0; ti < tops.Count; ti++)
                    for (int b = 0; b < B; b++)
                    {
                        int t = tops[ti], bc = bottoms[b];
                        float rise = g.H[t] - g.H[bc];
                        if (rise < 95f)
                            continue;
                        float gain = 0f;
                        for (int li = 0; li < lines.Count; li++)
                        {
                            int e = entry[ti][li];
                            if (e < 0 || passLast[li, b] < e)
                                continue;
                            float hi = lines[li].Pts[e].y, lo = g.H[bc];
                            if (hi - lo >= MinLapM)
                                gain += Uncovered(covered[li], lo, hi);
                        }
                        if (gain <= best)
                            continue;
                        if (Twin(g, lifts, t, bc))
                            continue;
                        string type = TypeFor(g, bc, t);
                        if (type == null || gain * Capacity(type) <= best)
                            continue;
                        best = gain * Capacity(type);
                        bt = ti;
                        bb = b;
                        bestType = type;
                    }
                if (bt < 0 || (firstGain > 0f && best < 0.1f * firstGain))
                    break;
                if (firstGain == 0f)
                    firstGain = best;
                int T = tops[bt], Bc = bottoms[bb];
                var lift = new Lift
                {
                    Id = lifts.Count + 1, Type = bestType, TopCell = T, BottomCell = Bc, Gain = best,
                    Top = new float3(g.World(T).x, g.H[T], g.World(T).y), Bottom = new float3(g.World(Bc).x, g.H[Bc], g.World(Bc).y),
                    BottomGrade = slope[Bc],
                };
                lift.Rise = lift.Top.y - lift.Bottom.y;
                lift.Length = math.distance(lift.Top, lift.Bottom);
                var parent = new int[n];
                List<int> touched = Reach(g, slope, T, dist, parent);
                for (int li = 0; li < lines.Count; li++)
                {
                    int e = entry[bt][li];
                    if (e < 0 || passLast[li, bb] < e)
                        continue;
                    float hi = lines[li].Pts[e].y, lo = g.H[Bc];
                    if (hi - lo < MinLapM || Uncovered(covered[li], lo, hi) <= 0f)
                        continue;
                    covered[li].Add((lo, hi));
                    lift.Lines.Add(lines[li]);
                    if (lines[li].Lift < 0)
                        lines[li].Lift = lift.Id;
                    // the traverse from the top to the entry, when it is more than a step off the station
                    var tr = new List<float2>();
                    for (int c = CellOf(g, lines[li].Pts[e].xz); c >= 0 && c != T; c = parent[c])
                        tr.Add(g.World(c));
                    tr.Add(g.World(T));
                    if (tr.Count * g.CellM > 150f)
                        lift.Traverses.Add(tr);
                }
                foreach (int c in touched)
                    dist[c] = float.PositiveInfinity;
                lifts.Add(lift);
            }
            return lifts;
        }

        private static bool Twin(Grid g, List<Lift> lifts, int t, int b)
        {
            // a twin (both ends within TwinM), a parallel (the middle 60 % within 200 m of the placed chord), or a crossing in plan
            // away from a shared station
            float2 a0 = g.World(b), a1 = g.World(t);
            foreach (Lift lf in lifts)
            {
                float2 b0 = lf.Bottom.xz, b1 = lf.Top.xz;
                if (math.distance(a1, b1) < TwinM && math.distance(a0, b0) < TwinM)
                    return true;
                bool parallel = true;
                for (float s = 0.2f; s <= 0.8f && parallel; s += 0.1f)
                    parallel = SegDist(math.lerp(a0, a1, s), b0, b1) < 200f;
                if (parallel)
                    return true;
                float shared = math.min(math.min(math.distance(a0, b0), math.distance(a0, b1)), math.min(math.distance(a1, b0), math.distance(a1, b1)));
                if (shared > 60f && Cross(a0, a1, b0, b1))
                    return true;
            }
            return false;
        }

        private static float SegDist(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float u = math.saturate(math.dot(p - a, ab) / math.max(1e-3f, math.dot(ab, ab)));
            return math.distance(p, a + u * ab);
        }

        private static float Side(float2 a, float2 b, float2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

        private static bool Cross(float2 p, float2 p2, float2 q, float2 q2) =>
            Side(p, p2, q) * Side(p, p2, q2) < 0f && Side(q, q2, p) * Side(q, q2, p2) < 0f;

        private static int Root(int[] p, int i)
        {
            while (p[i] != i)
                i = p[i] = p[p[i]];
            return i;
        }

        private static bool Near(Grid g, List<int> cells, int c, float m)
        {
            foreach (int o in cells)
                if (math.distancesq(g.World(o), g.World(c)) < m * m)
                    return true;
            return false;
        }

        private static List<float2> LinePath(Line l)
        {
            var p = new List<float2>(l.Pts.Count + l.Flow.Count);
            foreach (float3 q in l.Pts)
                p.Add(q.xz);
            p.AddRange(l.Flow);
            return p;
        }

        /// <summary>Dijkstra from a lift top over the cells a skier reaches without climbing (+3 %), without a pitch over 40 % and
        /// without a cliff cell, out to 1.5 km. Returns the touched cells (the caller resets dist); parent (optional) points home.</summary>
        private static List<int> Reach(Grid g, float[] slope, int t, float[] dist, int[] parent)
        {
            var touched = new List<int> { t };
            var pq = new PriorityQueue<int, float>();
            dist[t] = 0f;
            if (parent != null)
                parent[t] = -1;
            pq.Enqueue(t, 0f);
            while (pq.TryDequeue(out int c, out float d))
            {
                if (d > dist[c])
                    continue;
                int x = c % g.W, z = c / g.W;
                for (int k = 0; k < 8; k++)
                {
                    int xx = x + DX[k], zz = z + DZ[k];
                    if (xx < 0 || zz < 0 || xx >= g.W || zz >= g.D)
                        continue;
                    int nb = zz * g.W + xx;
                    if (!g.In[nb] || slope[nb] > AccessCliff)
                        continue;
                    float len = g.CellM * ((k & 1) == 1 ? 1.4142f : 1f);
                    float grade = (g.H[nb] - g.H[c]) / len;
                    if (grade > AccessMaxUp || grade < -AccessMaxDown)
                        continue;
                    float nd = d + len;
                    if (nd > AccessRadiusM || nd >= dist[nb])
                        continue;
                    if (float.IsPositiveInfinity(dist[nb]))
                        touched.Add(nb);
                    dist[nb] = nd;
                    if (parent != null)
                        parent[nb] = c;
                    pq.Enqueue(nb, nd);
                }
            }
            return touched;
        }

        private static float Uncovered(List<(float lo, float hi)> cov, float lo, float hi)
        {
            // length of [lo, hi] not inside any covered interval (intervals are few: one per lift)
            var cuts = new List<(float, float)>();
            foreach (var (a, b) in cov)
                if (b > lo && a < hi)
                    cuts.Add((Math.Max(a, lo), Math.Min(b, hi)));
            cuts.Sort((p, q) => p.Item1.CompareTo(q.Item1));
            float free = 0f, at = lo;
            foreach (var (a, b) in cuts)
            {
                if (a > at)
                    free += a - at;
                at = Math.Max(at, b);
            }
            return free + Math.Max(0f, hi - at);
        }

        /// <summary>The cheapest lift type whose length / rise / mean grade norms fit and whose chord clears the ground (terrain at
        /// most TowerLiftM above the straight line); null when none fits. Long chairs prefer the detachable.</summary>
        private static string TypeFor(Grid g, int b, int t)
        {
            float3 a = new float3(g.World(b).x, g.H[b], g.World(b).y), c = new float3(g.World(t).x, g.H[t], g.World(t).y);
            float len = math.distance(a, c), rise = c.y - a.y, run = math.distance(a.xz, c.xz);
            for (float s = 0.05f; s < 0.95f; s += 10f / Math.Max(run, 10f))
            {
                float3 p = math.lerp(a, c, s);
                if (HeightAt(g, p.xz) - p.y > TowerLiftM)
                    return null;
            }
            foreach (var ty in Types)
                if (len >= ty.L0 && len <= ty.L1 && rise >= ty.R0 && rise <= ty.R1 && rise / run <= ty.Cap)
                    return ty.Name == "fixed-grip" && len > 1000f && len >= 600f ? "detachable" : ty.Name;
            return null;
        }

        private static void WriteLifts(string path, string id, List<Lift> lifts, List<Line> lines)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# lifts {id}: lift type length_m rise_m bottom_grade new_vertical_m lines(map#:band) traverses");
            foreach (Lift lf in lifts)
            {
                var ls = new List<string>();
                foreach (Line l in lf.Lines)
                    ls.Add($"{l.Pick}:{l.Band}");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "L{0} {1} {2:F0} {3:F0} {4:F2} {5:F0} [{6}] {7}",
                    lf.Id, lf.Type, lf.Length, lf.Rise, lf.BottomGrade, lf.Gain, string.Join(" ", ls), lf.Traverses.Count));
            }
            int picked = 0, unserved = 0;
            foreach (Line l in lines)
                if (l.Picked)
                {
                    picked++;
                    if (l.Lift < 0)
                        unserved++;
                }
            sb.AppendLine($"# {unserved} of {picked} picked line(s) served by no lift");
            File.AppendAllText(path, sb.ToString());
            Console.WriteLine($"REPORT rf lifts {id}: {lifts.Count} lift(s) [{string.Join(", ", lifts.ConvertAll(l => $"L{l.Id} {l.Type} {l.Length:F0} m / {l.Rise:F0} m up, {l.Lines.Count} line(s), {l.Traverses.Count} traverse(s)"))}]; {unserved} of {picked} line(s) unserved");
        }
    }
}
