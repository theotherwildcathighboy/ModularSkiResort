// RunsFirst (prototype after field test 5): terrain-first run candidates. Reads the mountain the way a person does (drainages and
// spurs, top to bottom), rates each line, picks a spaced set, and renders it beside the v2 plan of the same region (uphill = image top).
// Host only: nothing here is wired into the mod. Run from the repo root:
//   dotnet run --project tests/AutoGenBatch/AutoGenBatch.csproj -c Release -- rf <out-dir>

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static class RunsFirst
    {
        // the field test 5 regions, corners as the mod log printed them (docs/research/field/2026-10-28/ft5/logs)
        private static readonly (string Id, float2[] Ring)[] Regions =
        {
            ("ft5-r1", new[] { P(3372, -1943), P(3462, -3193), P(6386, -2246), P(6244, -1083) }),
            ("ft5-r2", new[] { P(2688, -1711), P(2768, -2269), P(2717, -2714), P(3317, -3708), P(3593, -3839), P(6312, -2292), P(7148, -1835),
                P(6951, -983), P(5793, -1107), P(4905, -1124), P(4047, -1423), P(3703, -1716), P(3358, -1858) }),
            ("ft5-r3", new[] { P(6276, -966), P(5976, 559), P(5020, 1226), P(2076, -686), P(3790, -756), P(4412, -411), P(4942, -362), P(5651, -1175) }),
            ("ft5-r4", new[] { P(-2941, -4929), P(-978, -5499), P(-339, -4233), P(-1945, -3261), P(-2580, -3672) }),
        };

        private const int F = 2;                  // texels per flow cell (7 m cells at 3.5 m texels)
        private const float ChannelM2 = 20000f;   // 2 ha catchment = a drainage (inverted: a spur)
        private const float StepM = 10f, PitchM = 30f;
        private const float MaxPitch = 0.85f;     // steepest 30 m pitch a line may hold; past it the line is cut
        private const float MinDropM = 120f, MinLengthM = 300f;
        private const float SpacingM = 60f;       // the upper 60 % of two picked lines stay this far apart
        private const float OverlapShare = 0.3f;
        private const int MaxPicks = 30;

        private static float2 P(float x, float z) => new float2(x, z);

        private sealed class Grid
        {
            public int W, D;
            public float CellM;
            public double OX, OZ;          // world xz of cell (0, 0)'s center
            public float[] H;              // smoothed heights
            public bool[] In;
            public float2 World(int c) => new float2((float)(OX + (c % W) * CellM), (float)(OZ + (c / W) * CellM));
        }

        private sealed class Line
        {
            public int Id;
            public string Family;          // drain / spur
            public List<float3> Pts = new List<float3>();   // world, top to bottom, every StepM
            public float Drop, Length, Mean, Rating, Steepest, Width, Score;
            public SlopeBand Band;
            public bool Picked;
            public int Pick;               // 1..N in pick order (the map label)
            public string Why = "";
        }

        public static int Run(string[] args)
        {
            string outDir = args.Length > 1 ? args[1] : "rf-out";
            Directory.CreateDirectory(outDir);
            if (!Heightmap.TryOpen("MountainVillage", out HeightmapFile file, out string why))
            {
                Console.WriteLine("SKIP RunsFirst: " + why);
                return 0;
            }
            foreach (var (id, ring) in Regions)
                RunRegion(id, ring, file, outDir);
            return 0;
        }

        private static void RunRegion(string id, float2[] ring, HeightmapFile file, string outDir)
        {
            HeightSnapshot snap = Heightmap.Snapshot(file, ring);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Grid g = BuildGrid(snap, ring);
            int n = g.W * g.D;
            float cellsPerChannel = ChannelM2 / (g.CellM * g.CellM);

            // drainages on the terrain, spurs on the inverted terrain
            int[] rec = Receivers(g, g.H, out float[] acc);
            var inv = new float[n];
            for (int c = 0; c < n; c++)
                inv[c] = -g.H[c];
            int[] recR = Receivers(g, inv, out float[] accR);

            var lines = new List<Line>();
            foreach (List<int> path in Stems(g, rec, acc, cellsPerChannel))
                AddLine(lines, g, path, "drain");
            foreach (List<int> path in Stems(g, recR, accR, cellsPerChannel))
            {
                path.Reverse();   // inverted paths run low to high
                AddLine(lines, g, path, "spur");
            }
            Pick(lines);
            long ms = sw.ElapsedMilliseconds;

            // the v2 plan of the same region, as the host replays it
            AutoPlanKnobs knobs = AutoPlanKnobs.Default(SkiRunAutoPlanner.SeedOf(ring, 0));
            AutoPlanResult v2 = new SkiRunAutoPlanner().Run(new AutoPlanInput { Snapshot = snap, Region = ring, Knobs = knobs, Mode = AutoPlanMode.Resort });

            int picked = 0;
            var bands = new int[5];
            float km = 0f;
            foreach (Line l in lines)
                if (l.Picked)
                {
                    picked++;
                    bands[(int)l.Band]++;
                    km += l.Length / 1000f;
                }
            float km2 = (float)(SkiRunAutoPlanner.PolygonArea(ring) / 1e6);
            Console.WriteLine($"REPORT rf {id}: {km2:F2} km2, {lines.Count} candidate line(s), {picked} picked (G/B/K/DB {bands[1]}/{bands[2]}/{bands[3]}/{bands[4]}), " +
                $"{km:F1} km of runs, {ms} ms; v2 {v2.Status}: {v2.Runs.Count} run(s), {v2.Resort?.Lifts?.Length ?? 0} lift(s)");

            WriteTable(Path.Combine(outDir, id + ".txt"), id, lines);
            Render(Path.Combine(outDir, id + ".png"), snap, ring, g, acc, accR, lines, v2);
        }

        // ------------------------------------------------------------------ grid and flow

        private static Grid BuildGrid(HeightSnapshot s, float2[] ring)
        {
            var g = new Grid { W = s.Width / F, D = s.Depth / F, CellM = s.TexelM * F };
            g.OX = s.Origin.x + (s.X0 + (F - 1) * 0.5) * s.TexelM;
            g.OZ = s.Origin.y + (s.Z0 + (F - 1) * 0.5) * s.TexelM;
            int n = g.W * g.D;
            var raw = new float[n];
            for (int cz = 0; cz < g.D; cz++)
                for (int cx = 0; cx < g.W; cx++)
                {
                    float sum = 0f;
                    for (int dz = 0; dz < F; dz++)
                        for (int dx = 0; dx < F; dx++)
                            sum += s.ToWorld(s.Heights[(cz * F + dz) * s.Width + cx * F + dx]);
                    raw[cz * g.W + cx] = sum / (F * F);
                }
            g.H = Box(Box(raw, g.W, g.D), g.W, g.D);
            g.In = new bool[n];
            for (int c = 0; c < n; c++)
                g.In[c] = Inside(ring, g.World(c));
            return g;
        }

        private static float[] Box(float[] a, int w, int d)
        {
            var o = new float[a.Length];
            for (int z = 0; z < d; z++)
                for (int x = 0; x < w; x++)
                {
                    float s = 0f;
                    int k = 0;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx, zz = z + dz;
                            if (xx < 0 || zz < 0 || xx >= w || zz >= d)
                                continue;
                            s += a[zz * w + xx];
                            k++;
                        }
                    o[z * w + x] = s / k;
                }
            return o;
        }

        private static bool Inside(float2[] r, float2 p)
        {
            bool inside = false;
            for (int i = 0, j = r.Length - 1; i < r.Length; j = i++)
                if ((r[i].y > p.y) != (r[j].y > p.y) && p.x < (r[j].x - r[i].x) * (p.y - r[i].y) / (r[j].y - r[i].y) + r[i].x)
                    inside = !inside;
            return inside;
        }

        private static readonly int[] DX = { 1, 1, 0, -1, -1, -1, 0, 1 }, DZ = { 0, 1, 1, 1, 0, -1, -1, -1 };

        /// <summary>D8 receivers on the epsilon-filled surface (priority flood from the region edge), so every cell drains to an outlet
        /// on the edge (receiver -1). acc = upstream cell count.</summary>
        private static int[] Receivers(Grid g, float[] h, out float[] acc)
        {
            int n = g.W * g.D;
            var fill = new float[n];
            var done = new bool[n];
            var pq = new PriorityQueue<int, float>();
            for (int c = 0; c < n; c++)
            {
                if (!g.In[c] || !OnEdge(g, c))
                    continue;
                fill[c] = h[c];
                done[c] = true;
                pq.Enqueue(c, h[c]);
            }
            while (pq.TryDequeue(out int c, out _))
            {
                int x = c % g.W, z = c / g.W;
                for (int k = 0; k < 8; k++)
                {
                    int xx = x + DX[k], zz = z + DZ[k];
                    if (xx < 0 || zz < 0 || xx >= g.W || zz >= g.D)
                        continue;
                    int nb = zz * g.W + xx;
                    if (!g.In[nb] || done[nb])
                        continue;
                    fill[nb] = Math.Max(h[nb], fill[c] + 2e-3f);
                    done[nb] = true;
                    pq.Enqueue(nb, fill[nb]);
                }
            }
            var rec = new int[n];
            for (int c = 0; c < n; c++)
            {
                rec[c] = -1;
                if (!g.In[c])
                    continue;
                int x = c % g.W, z = c / g.W;
                float best = 0f;
                for (int k = 0; k < 8; k++)
                {
                    int xx = x + DX[k], zz = z + DZ[k];
                    if (xx < 0 || zz < 0 || xx >= g.W || zz >= g.D)
                        continue;
                    int nb = zz * g.W + xx;
                    if (!g.In[nb])
                        continue;
                    float s = (fill[c] - fill[nb]) / ((k & 1) == 1 ? 1.4142f : 1f);
                    if (s > best)
                    {
                        best = s;
                        rec[c] = nb;
                    }
                }
            }
            var order = new int[n];
            var keys = new float[n];
            for (int c = 0; c < n; c++)
            {
                order[c] = c;
                keys[c] = -fill[c];
            }
            Array.Sort(keys, order);
            acc = new float[n];
            foreach (int c in order)
            {
                if (!g.In[c])
                    continue;
                acc[c] += 1f;
                if (rec[c] >= 0)
                    acc[rec[c]] += acc[c];
            }
            return rec;
        }

        private static bool OnEdge(Grid g, int c)
        {
            int x = c % g.W, z = c / g.W;
            for (int k = 0; k < 8; k++)
            {
                int xx = x + DX[k], zz = z + DZ[k];
                if (xx < 0 || zz < 0 || xx >= g.W || zz >= g.D || !g.In[zz * g.W + xx])
                    return true;
            }
            return false;
        }

        /// <summary>Hack-order stems: from each outlet, the main stem climbs the largest upstream branch to the divide; every other
        /// branch over the channel size starts its own stem. Each returned path runs from its stem's top down to the outlet.</summary>
        private static IEnumerable<List<int>> Stems(Grid g, int[] rec, float[] acc, float minCells)
        {
            int n = g.W * g.D;
            var first = new int[n];
            var next = new int[n];
            Array.Fill(first, -1);
            for (int c = 0; c < n; c++)
                if (g.In[c] && rec[c] >= 0)
                {
                    next[c] = first[rec[c]];
                    first[rec[c]] = c;
                }
            var todo = new Queue<int>();
            for (int c = 0; c < n; c++)
                if (g.In[c] && rec[c] < 0 && acc[c] >= minCells)
                    todo.Enqueue(c);
            while (todo.Count > 0)
            {
                int start = todo.Dequeue();
                var stem = new List<int> { start };
                int cur = start;
                while (true)
                {
                    int main = -1;
                    for (int ch = first[cur]; ch >= 0; ch = next[ch])
                        if (main < 0 || acc[ch] > acc[main])
                            main = ch;
                    for (int ch = first[cur]; ch >= 0; ch = next[ch])
                        if (ch != main && acc[ch] >= minCells)
                            todo.Enqueue(ch);
                    if (main < 0)
                        break;
                    stem.Add(main);
                    cur = main;
                }
                stem.Reverse();
                for (int c = rec[start]; c >= 0; c = rec[c])
                    stem.Add(c);
                yield return stem;
            }
        }

        // ------------------------------------------------------------------ lines

        private static float HeightAt(Grid g, float2 p)
        {
            float fx = (float)((p.x - g.OX) / g.CellM), fz = (float)((p.y - g.OZ) / g.CellM);
            int x0 = Math.Clamp((int)Math.Floor(fx), 0, g.W - 2), z0 = Math.Clamp((int)Math.Floor(fz), 0, g.D - 2);
            float tx = Math.Clamp(fx - x0, 0f, 1f), tz = Math.Clamp(fz - z0, 0f, 1f);
            float a = g.H[z0 * g.W + x0], b = g.H[z0 * g.W + x0 + 1], c = g.H[(z0 + 1) * g.W + x0], d = g.H[(z0 + 1) * g.W + x0 + 1];
            return math.lerp(math.lerp(a, b, tx), math.lerp(c, d, tx), tz);
        }

        private static float GradeAt(Grid g, float2 p)
        {
            float e = g.CellM;
            float gx = (HeightAt(g, p + new float2(e, 0)) - HeightAt(g, p - new float2(e, 0))) / (2 * e);
            float gz = (HeightAt(g, p + new float2(0, e)) - HeightAt(g, p - new float2(0, e))) / (2 * e);
            return math.sqrt(gx * gx + gz * gz);
        }

        private static void AddLine(List<Line> lines, Grid g, List<int> cells, string family)
        {
            if (cells.Count < 4)
                return;
            // cell centers, smoothed (D8 zigzag), resampled every StepM
            var raw = new List<float2>(cells.Count);
            foreach (int c in cells)
                raw.Add(g.World(c));
            var sm = new List<float2>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                float2 s = 0f;
                int k = 0;
                for (int j = Math.Max(0, i - 3); j <= Math.Min(raw.Count - 1, i + 3); j++, k++)
                    s += raw[j];
                sm.Add(s / k);
            }
            var pts = new List<float2> { sm[0] };
            float carry = 0f;
            for (int i = 1; i < sm.Count; i++)
            {
                float2 a = sm[i - 1], b = sm[i];
                float seg = math.distance(a, b), t = StepM - carry;
                while (t <= seg)
                {
                    pts.Add(math.lerp(a, b, t / seg));
                    t += StepM;
                }
                carry = seg - (t - StepM);
            }
            if (pts.Count < 6)
                return;

            int per = (int)(PitchM / StepM);
            var h = new float[pts.Count];
            for (int i = 0; i < pts.Count; i++)
                h[i] = HeightAt(g, pts[i]);
            var pitch = new float[pts.Count];   // grade of the 30 m below sample i
            for (int i = 0; i < pts.Count; i++)
                pitch[i] = i + per < pts.Count ? Math.Abs(h[i] - h[i + per]) / PitchM : 0f;

            // the longest run of samples whose pitches all hold, then the flat runout off the bottom
            int bestA = 0, bestB = -1, a0 = 0;
            for (int i = 0; i <= pts.Count; i++)
                if (i == pts.Count || pitch[i] > MaxPitch)
                {
                    if (i - 1 - a0 > bestB - bestA)
                    {
                        bestA = a0;
                        bestB = i - 1;
                    }
                    a0 = i + 1;
                }
            int run = (int)(100f / StepM);
            while (bestB - run > bestA && (h[bestB - run] - h[bestB]) / 100f < 0.04f)
                bestB--;
            if (bestB - bestA < 5)
                return;

            var l = new Line { Family = family };
            for (int i = bestA; i <= bestB; i++)
                l.Pts.Add(new float3(pts[i].x, h[i], pts[i].y));
            l.Length = (bestB - bestA) * StepM;
            l.Drop = h[bestA] - h[bestB];
            if (l.Drop < MinDropM || l.Length < MinLengthM)
                return;
            l.Mean = l.Drop / l.Length;
            var ps = new List<float>();
            for (int i = bestA; i + per <= bestB; i++)
                ps.Add(pitch[i]);
            ps.Sort();
            l.Rating = ps.Count > 0 ? ps[(int)(ps.Count * 0.85f)] : l.Mean;
            l.Steepest = ps.Count > 0 ? ps[ps.Count - 1] : l.Mean;
            l.Band = SkiSlopeClassifier.BandOf(l.Rating);
            l.Width = Width(g, l);
            l.Score = l.Drop * Math.Clamp(l.Width / 40f, 0.3f, 1f);
            lines.Add(l);
        }

        /// <summary>Median corridor width: from every third sample, step sideways until the ground turns cliff (over 0.75) or rises /
        /// falls away from the line faster than half the distance.</summary>
        private static float Width(Grid g, Line l)
        {
            var ws = new List<float>();
            for (int i = 1; i + 1 < l.Pts.Count; i += 3)
            {
                float2 t = math.normalizesafe(l.Pts[i + 1].xz - l.Pts[i - 1].xz);
                float2 nrm = new float2(-t.y, t.x);
                float w = 0f;
                foreach (float side in new[] { -1f, 1f })
                    for (float d = 5f; d <= 60f; d += 5f)
                    {
                        float2 p = l.Pts[i].xz + nrm * side * d;
                        float2 cell = new float2((float)((p.x - g.OX) / g.CellM), (float)((p.y - g.OZ) / g.CellM));
                        int c = (int)cell.y * g.W + (int)cell.x;
                        if (cell.x < 0 || cell.y < 0 || cell.x >= g.W || cell.y >= g.D || !g.In[c] || GradeAt(g, p) > 0.75f ||
                            Math.Abs(HeightAt(g, p) - l.Pts[i].y) > 0.5f * d + 3f)
                            break;
                        w += 5f;
                    }
                ws.Add(w);
            }
            if (ws.Count == 0)
                return 0f;
            ws.Sort();
            return ws[ws.Count / 2];
        }

        private static void Pick(List<Line> lines)
        {
            lines.Sort((a, b) => b.Score.CompareTo(a.Score));
            for (int i = 0; i < lines.Count; i++)
                lines[i].Id = i + 1;
            var taken = new List<float2>();
            float best = lines.Count > 0 ? lines[0].Score : 0f;
            int picks = 0;
            foreach (Line l in lines)
            {
                if (picks >= MaxPicks)
                {
                    l.Why = "cap";
                    continue;
                }
                if (l.Score < 0.15f * best)
                {
                    l.Why = "weak";
                    continue;
                }
                int upper = Math.Max(1, (int)(l.Pts.Count * 0.6f)), near = 0;
                for (int i = 0; i < upper; i++)
                    foreach (float2 t in taken)
                        if (math.distancesq(t, l.Pts[i].xz) < SpacingM * SpacingM)
                        {
                            near++;
                            break;
                        }
                if (near > OverlapShare * upper)
                {
                    l.Why = "overlap";
                    continue;
                }
                l.Picked = true;
                l.Pick = ++picks;
                foreach (float3 p in l.Pts)
                    taken.Add(p.xz);
            }
        }

        private static void WriteTable(string path, string id, List<Line> lines)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# runs-first candidates {id}: map# id family picked band drop_m length_m mean rating steepest width_m score top(x,z,y) bottom(x,z,y) why");
            foreach (Line l in lines)
            {
                float3 t = l.Pts[0], b = l.Pts[l.Pts.Count - 1];
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{18} {0} {1} {2} {3} {4:F0} {5:F0} {6:F2} {7:F2} {8:F2} {9:F0} {10:F0} ({11:F0},{12:F0},{13:F0}) ({14:F0},{15:F0},{16:F0}) {17}",
                    l.Id, l.Family, l.Picked ? "Y" : "-", l.Band, l.Drop, l.Length, l.Mean, l.Rating, l.Steepest, l.Width, l.Score,
                    t.x, t.z, t.y, b.x, b.z, b.y, l.Why, l.Picked ? "#" + l.Pick : ""));
            }
            File.WriteAllText(path, sb.ToString());
        }

        // ------------------------------------------------------------------ render

        private sealed class Canvas
        {
            public int W, H;
            public byte[] Rgb;
            public Canvas(int w, int h)
            {
                W = w;
                H = h;
                Rgb = new byte[w * h * 3];
            }
            public void Set(int x, int y, (int r, int g, int b) c, float a = 1f)
            {
                if (x < 0 || y < 0 || x >= W || y >= H)
                    return;
                int o = (y * W + x) * 3;
                Rgb[o] = (byte)(Rgb[o] + (c.r - Rgb[o]) * a);
                Rgb[o + 1] = (byte)(Rgb[o + 1] + (c.g - Rgb[o + 1]) * a);
                Rgb[o + 2] = (byte)(Rgb[o + 2] + (c.b - Rgb[o + 2]) * a);
            }
            public void Disc(float x, float y, float r, (int, int, int) c)
            {
                for (int yy = (int)(y - r); yy <= (int)(y + r) + 1; yy++)
                    for (int xx = (int)(x - r); xx <= (int)(x + r) + 1; xx++)
                        if ((xx - x) * (xx - x) + (yy - y) * (yy - y) <= r * r)
                            Set(xx, yy, c);
            }
        }

        private static readonly string[] Digits =
        {
            "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
            "111100111001111", "111100111101111", "111001001001001", "111101111101111", "111101111001111",
        };

        private static (int, int, int) BandColor(SlopeBand b) => b switch
        {
            SlopeBand.Green => (40, 170, 60),
            SlopeBand.Blue => (40, 110, 235),
            SlopeBand.Black => (10, 10, 10),
            SlopeBand.DoubleBlack => (10, 10, 10),
            _ => (200, 200, 200),
        };

        private static (int, int, int) EdgeColor(SlopeBand b) => b == SlopeBand.DoubleBlack ? (255, 150, 0) : (255, 255, 255);

        private static void Render(string path, HeightSnapshot s, float2[] ring, Grid g, float[] acc, float[] accR, List<Line> lines, AutoPlanResult v2)
        {
            int w = s.Width, d = s.Depth;
            float t = s.TexelM;
            Func<int, int, float> hAt = (i, j) => s.ToWorld(s.Heights[Math.Clamp(j, 0, d - 1) * w + Math.Clamp(i, 0, w - 1)]);
            // uphill = image top: the axis closest to lowest -> highest cell in the region
            int lo = -1, hi = -1;
            for (int c = 0; c < g.W * g.D; c++)
                if (g.In[c])
                {
                    if (lo < 0 || g.H[c] < g.H[lo])
                        lo = c;
                    if (hi < 0 || g.H[c] > g.H[hi])
                        hi = c;
                }
            float2 up = g.World(hi) - g.World(lo);
            int rot = Math.Abs(up.x) > Math.Abs(up.y) ? (up.x > 0 ? 1 : 3) : (up.y > 0 ? 0 : 2);   // 0 +z, 1 +x, 2 -z, 3 -x
            int iw = rot % 2 == 0 ? w : d, ih = rot % 2 == 0 ? d : w;
            Func<float, float, float2> img = (i, j) => rot switch
            {
                0 => new float2(i, d - 1 - j),
                1 => new float2(d - 1 - j, w - 1 - i),
                2 => new float2(w - 1 - i, j),
                _ => new float2(j, i),
            };
            Func<float2, float2> fromWorld = p => img((float)((p.x - s.Origin.x) / t - s.X0), (float)((p.y - s.Origin.y) / t - s.Z0));

            Canvas Base(bool flow)
            {
                var cv = new Canvas(iw, ih);
                for (int j = 0; j < d; j++)
                    for (int i = 0; i < w; i++)
                    {
                        float dzdx = (hAt(i + 1, j) - hAt(i - 1, j)) / (2 * t), dzdz = (hAt(i, j + 1) - hAt(i, j - 1)) / (2 * t);
                        float3 nrm = math.normalize(new float3(-dzdx, 1f, -dzdz));
                        float shade = math.max(0f, math.dot(nrm, math.normalize(new float3(-1f, 0.8f, 1f))));
                        float grade = math.sqrt(dzdx * dzdx + dzdz * dzdz);
                        float2 wp = new float2((float)(s.Origin.x + (s.X0 + i) * t), (float)(s.Origin.y + (s.Z0 + j) * t));
                        bool inside = Inside(ring, wp);
                        int v = (int)(60 + 170 * shade);
                        (int, int, int) col = (v, v, v);
                        if (grade > 0.70f)
                            col = (Math.Min(255, v + 40), (int)(v * 0.75f), (int)(v * 0.7f));   // cliff tint
                        else if (grade < 0.15f)
                            col = ((int)(v * 0.8f), Math.Min(255, v + 25), (int)(v * 0.8f));   // bench tint
                        // contours every 50 m, heavier every 250 m
                        float h0 = hAt(i, j);
                        int band0 = (int)Math.Floor(h0 / 50f);
                        if ((int)Math.Floor(hAt(i + 1, j) / 50f) != band0 || (int)Math.Floor(hAt(i, j + 1) / 50f) != band0)
                        {
                            bool major = (int)Math.Floor(Math.Max(Math.Max(hAt(i + 1, j), hAt(i, j + 1)), h0) / 250f) != (int)Math.Floor(Math.Min(Math.Min(hAt(i + 1, j), hAt(i, j + 1)), h0) / 250f);
                            float k = major ? 0.35f : 0.65f;
                            col = ((int)(col.Item1 * k), (int)(col.Item2 * k), (int)(col.Item3 * k));
                        }
                        if (!inside)
                        {
                            int gray = (col.Item1 + col.Item2 + col.Item3) / 9;
                            col = (gray, gray, gray);
                        }
                        float2 q = img(i, j);
                        cv.Set((int)q.x, (int)q.y, col);
                    }
                // drainages blue, spurs orange (2 ha and up)
                float cell2 = g.CellM * g.CellM;
                for (int c = 0; flow && c < g.W * g.D; c++)
                {
                    if (!g.In[c])
                        continue;
                    float2 q = fromWorld(g.World(c));
                    if (acc[c] * cell2 >= ChannelM2)
                        cv.Disc(q.x, q.y, 1.2f, (60, 140, 255));
                    else if (accR[c] * cell2 >= ChannelM2)
                        cv.Disc(q.x, q.y, 0.8f, (230, 150, 60));
                }
                for (int k = 0; k < ring.Length; k++)
                    Stroke(cv, new List<float2> { fromWorld(ring[k]), fromWorld(ring[(k + 1) % ring.Length]) }, 1f, (255, 255, 255));
                return cv;
            }

            Canvas clean = Base(false);
            Program.WritePngRgb(Path.ChangeExtension(path, null) + "-draw.png", clean.W, clean.H, clean.Rgb);
            Canvas left = Base(true), right = Base(true);
            // left: the v2 plan (runs by band, lift chords yellow)
            foreach (AutoRun r in v2.Runs)
            {
                var pl = new List<float2>();
                foreach (float2 p in r.Centerline)
                    pl.Add(fromWorld(p));
                Stroke(left, pl, 4f, EdgeColor(r.Band));
                Stroke(left, pl, 2.5f, BandColor(r.Band));
            }
            if (v2.Resort?.Lifts != null)
                foreach (PlanLift lf in v2.Resort.Lifts)
                {
                    var pl = new List<float2> { fromWorld(lf.Bottom.xz), fromWorld(lf.Top.xz) };
                    Stroke(left, pl, 2f, (255, 220, 0));
                    left.Disc(pl[0].x, pl[0].y, 5f, (255, 220, 0));
                    left.Disc(pl[1].x, pl[1].y, 5f, (255, 220, 0));
                }
            // right: every candidate thin, picked lines thick by band, numbered at the top
            foreach (Line l in lines)
                if (!l.Picked)
                    Stroke(right, Img(l, fromWorld), 0.6f, (120, 200, 210));
            foreach (Line l in lines)
                if (l.Picked)
                {
                    List<float2> pl = Img(l, fromWorld);
                    Stroke(right, pl, 4.5f, EdgeColor(l.Band));
                    Stroke(right, pl, 3f, BandColor(l.Band));
                    right.Disc(pl[0].x, pl[0].y, 6f, (255, 255, 255));
                    Label(right, (int)pl[0].x + 8, (int)pl[0].y - 6, l.Pick);
                }

            var both = new Canvas(iw * 2 + 12, ih);
            for (int y = 0; y < ih; y++)
            {
                Array.Copy(left.Rgb, y * iw * 3, both.Rgb, y * both.W * 3, iw * 3);
                Array.Copy(right.Rgb, y * iw * 3, both.Rgb, (y * both.W + iw + 12) * 3, iw * 3);
            }
            Program.WritePngRgb(path, both.W, both.H, both.Rgb);
            string[] ups = { "north (+z)", "east (+x)", "south (-z)", "west (-x)" };
            Console.WriteLine($"REPORT rf render {Path.GetFileName(path)}: {both.W} x {both.H}, image top = {ups[rot]}, 1 px = {t:F1} m; left v2, right runs-first");
        }

        private static List<float2> Img(Line l, Func<float2, float2> fromWorld)
        {
            var pl = new List<float2>(l.Pts.Count);
            foreach (float3 p in l.Pts)
                pl.Add(fromWorld(p.xz));
            return pl;
        }

        private static void Stroke(Canvas cv, List<float2> pl, float r, (int, int, int) c)
        {
            for (int i = 1; i < pl.Count; i++)
            {
                float len = math.distance(pl[i - 1], pl[i]);
                for (float u = 0; u <= len; u += 0.5f)
                {
                    float2 p = math.lerp(pl[i - 1], pl[i], len > 0 ? u / len : 0);
                    cv.Disc(p.x, p.y, r, c);
                }
            }
        }

        private static void Label(Canvas cv, int x, int y, int n)
        {
            string s = n.ToString(CultureInfo.InvariantCulture);
            const int sc = 3;
            for (int k = 0; k < s.Length; k++)
            {
                string glyph = Digits[s[k] - '0'];
                for (int gy = -1; gy <= 5; gy++)
                    for (int gx = -1; gx <= 3; gx++)
                        for (int py = 0; py < sc; py++)
                            for (int px = 0; px < sc; px++)
                                cv.Set(x + k * 4 * sc + gx * sc + px, y + gy * sc + py, (0, 0, 0));
                for (int gy = 0; gy < 5; gy++)
                    for (int gx = 0; gx < 3; gx++)
                        if (glyph[gy * 3 + gx] == '1')
                            for (int py = 0; py < sc; py++)
                                for (int px = 0; px < sc; px++)
                                    cv.Set(x + k * 4 * sc + gx * sc + px, y + gy * sc + py, (255, 255, 255));
            }
        }
    }
}
