// RunsFirst (prototype after field test 5): terrain-first run candidates. Reads the mountain the way a person does (drainages and
// spurs, top to bottom), rates each line, picks a spaced set, and renders it beside the v2 plan of the same region (uphill = image top).
// Host only: nothing here is wired into the mod. Run from the repo root:
//   dotnet run --project tests/AutoGenBatch/AutoGenBatch.csproj -c Release -- rf <out-dir>
// The planner's hot methods are marked AggressiveOptimization: a region plans in about a second once the runtime has tiered its
// code up, but the first region of a run spent 2-3 s more in tier-0 code (tiering waits while the pipeline keeps compiling new
// methods), and the first region counts against the same 5 s as the others.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class RunsFirst
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
        private const float SpacingM = 60f;       // the upper 60 % of two picked lines stay this far apart
        private const float OverlapShare = 0.3f;
        private const float MergeM = 35f;         // a picked line ends where it runs onto an earlier one (3 samples in a row this close, a trail width)
        private static readonly float PicksPerKm2 = Env("RF_PPK", 5f);     // line cap per km2 (12-80), the Normal density
        private const float GentleLatticeM = 50f, GentleMinGrade = 0.06f, GentleMaxGrade = 0.40f;

        // per family: steepest 30 m pitch the line may hold (cut there), runout grade trimmed off the bottom, min drop / length (m)
        private static (float MaxPitch, float Runout, float MinDrop, float MinLength) Rules(string family) =>
            family == "gentle" ? (0.40f, 0.015f, 40f, 200f) : (0.85f, 0.04f, 120f, 300f);

        // beginner and intermediate vertical is scarce and worth more per meter than expert vertical
        private static float BandWeight(SlopeBand b) => b == SlopeBand.Green ? 3f : b == SlopeBand.Blue ? 2f : 1f;

        private static float2 P(float x, float z) => new float2(x, z);

        /// <summary>Experiment knob: an environment variable as a float (the default when unset).</summary>
        private static float Env(string name, float dflt) =>
            float.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : dflt;

        private sealed class Grid
        {
            public int W, D;
            public float CellM;
            public double OX, OZ;          // world xz of cell (0, 0)'s center
            public float[] H;              // smoothed heights
            public bool[] In;
            public HeightSnapshot S;       // the raw terrain: stations, traverses and runs are checked on it
            public float[] Hr;             // raw height at each cell center
            public float2 World(int c) => new float2((float)(OX + (c % W) * CellM), (float)(OZ + (c / W) * CellM));
        }

        private sealed class Line
        {
            public int Id;
            public string Family;          // drain / spur
            public List<float3> Pts = new List<float3>();   // world, top to bottom, every StepM
            public float Drop, Length, Mean, Rating, Steepest, Width, Score;
            public SlopeBand Band;
            public bool Picked, Merge;
            public int Pick;               // 1..N in pick order (the map label)
            public int Lift = -1;          // the lift whose lap covers most of this line
            public List<float2> Flow;      // the drainage below the line's end, to the region edge (the way back)
            public string Why = "";
        }

        public static int Run(string[] args)
        {
            // "rf-holdout <file>": writes the real-resort regions (9 km2 around each real fixture base point) as a regions file
            if (args[0] == "rf-holdout")
            {
                var rows = new List<string>();
                foreach (string name in new[] { "JacksonHole", "Whistler", "Snowbird", "Zermatt", "Chamonix", "Okemo" })
                    foreach (V2Case c in V2Fixtures.Real(name, out _))
                        if (c.Id.Contains("base 9 km2"))
                            rows.Add(name.ToLowerInvariant() + " @" + name + " " + string.Join(" ", c.Region.Select(p =>
                                p.x.ToString("F0", CultureInfo.InvariantCulture) + "," + p.y.ToString("F0", CultureInfo.InvariantCulture))));
                File.WriteAllLines(args[1], rows);
                Console.WriteLine($"wrote {rows.Count} holdout region(s) to {args[1]}");
                return 0;
            }
            string outDir = args.Length > 1 ? args[1] : "rf-out";
            Directory.CreateDirectory(outDir);
            if (!Heightmap.TryOpen("MountainVillage", out HeightmapFile file, out string why))
            {
                Console.WriteLine("SKIP RunsFirst: " + why);
                return 0;
            }
            // a regions file (tools/regions-from-log.js: "<id> x,z x,z ...") replaces the built-in field test 5 list
            var regions = new List<(string, float2[])>(Regions);
            if (args.Length > 2)
            {
                regions.Clear();
                foreach (string row in File.ReadAllLines(args[2]))
                {
                    var t = new List<string>(row.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                    // optional second token "@Fixture" (default MountainVillage)
                    string fx = t.Count > 1 && t[1].StartsWith("@") ? t[1].Substring(1) : "MountainVillage";
                    if (t.Count > 1 && t[1].StartsWith("@"))
                        t.RemoveAt(1);
                    if (t.Count < 4)
                        continue;
                    // "hole x,z x,z ..." after the outline cuts a no-go area out of the region (a cliff band): the ring becomes a
                    // keyhole (outline, a zero-width bridge to the hole, the hole reversed, back), which every even-odd inside
                    // test and the shoelace area already read as outline minus hole
                    var parts = new List<List<float2>> { new List<float2>() };
                    for (int i = 1; i < t.Count; i++)
                    {
                        if (t[i] == "hole")
                        {
                            parts.Add(new List<float2>());
                            continue;
                        }
                        string[] xz = t[i].Split(',');
                        parts[parts.Count - 1].Add(new float2(float.Parse(xz[0], CultureInfo.InvariantCulture), float.Parse(xz[1], CultureInfo.InvariantCulture)));
                    }
                    var ring = new List<float2>(parts[0]);
                    double Signed(List<float2> r)
                    {
                        double a = 0.0;
                        for (int i = 0, j = r.Count - 1; i < r.Count; j = i++)
                            a += (double)r[j].x * r[i].y - (double)r[i].x * r[j].y;
                        return a;
                    }
                    foreach (List<float2> hole in parts.Skip(1).Where(h => h.Count >= 3))
                    {
                        if (Math.Sign(Signed(hole)) == Math.Sign(Signed(parts[0])))
                            hole.Reverse();
                        ring.Add(parts[0][0]);
                        ring.AddRange(hole);
                        ring.Add(hole[0]);
                    }
                    regions.Add((fx + "|" + t[0], ring.ToArray()));
                }
            }
            var files = new Dictionary<string, HeightmapFile> { ["MountainVillage"] = file };
            foreach (var (key, ring) in regions)
            {
                int bar = key.IndexOf('|');
                string fx = bar < 0 ? "MountainVillage" : key.Substring(0, bar), id = bar < 0 ? key : key.Substring(bar + 1);
                if (!files.TryGetValue(fx, out HeightmapFile hf))
                {
                    if (!Heightmap.TryOpen(fx, out hf, out string why2))
                    {
                        Console.WriteLine($"SKIP RunsFirst {id}: {why2}");
                        continue;
                    }
                    files[fx] = hf;
                }
                RunRegion(id, ring, hf, outDir);
            }
            return 0;
        }

        private static System.Diagnostics.Stopwatch Clock = new System.Diagnostics.Stopwatch();
        private static readonly bool Timing = Environment.GetEnvironmentVariable("RF_TIME") == "1";
        // the passes after the way home refine a finished plan: past this point of a region's 5 s they stop where they are
        private static readonly float LateMs = Env("RF_LATEMS", 4200f);
        private static bool Late => Clock.ElapsedMilliseconds > LateMs;
        private static void Tick(string what)
        {
            if (Timing)
                Console.WriteLine($"TIME {what} {Clock.ElapsedMilliseconds} ms (easy {EasyWork}, links {LinkWork})");
            // RF_WHDBG: the lifts without a way home after each stage of the plan
            if (WayDbg != null)
            {
                var (g, plan, lifts) = WayDbg.Value;
                bool[] ok = WayHomeOk(Ski(g, plan, lifts), lifts, V3Bases(g, lifts, out HashSet<int> bases), bases);
                Console.WriteLine($"WHDBG {what}: none from " + string.Join(" ", Enumerable.Range(0, lifts.Count).Where(L => !ok[L]).Select(L => "L" + lifts[L].Id)));
            }
        }
        private static (Grid, Plan, List<Lift>)? WayDbg;

        private static void RunRegion(string id, float2[] ring, HeightmapFile file, string outDir)
        {
            HeightSnapshot snap = Heightmap.Snapshot(file, ring);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Clock = sw;
            EasyWork = LinkWork = 0;
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
            // gentle ground: fall lines from a 50 m lattice of green / blue cells, highest first, kept down to the base
            int gs = Math.Max(1, (int)Math.Round(GentleLatticeM / g.CellM));
            var seeds = new List<int>();
            for (int z = gs / 2; z < g.D; z += gs)
                for (int x = gs / 2; x < g.W; x += gs)
                {
                    int c = z * g.W + x;
                    float gr = g.In[c] ? GradeAt(g, g.World(c)) : 0f;
                    if (g.In[c] && gr >= GentleMinGrade && gr <= GentleMaxGrade)
                        seeds.Add(c);
                }
            seeds.Sort((a, b) => g.H[b].CompareTo(g.H[a]));
            foreach (int s0 in seeds)
            {
                var path = new List<int>();
                for (int c = s0; c >= 0; c = rec[c])
                    path.Add(c);
                AddLine(lines, g, path, "gentle");
            }
            float km2r = (float)(SkiRunAutoPlanner.PolygonArea(ring) / 1e6);
            Tick("lines");
            PointHash taken = Pick(lines, g, km2r);
            List<Lift> lifts = PlaceLifts(g, rec, lines, km2r, taken);
            Tick("lifts");
            Plan built = BuildPlan(g, rec, lines, lifts, km2r);
            Tick("plan");
            // the village lift (from the opus-med contest entry): the longest main lift off the lowest base whose top is a hub (another
            // lift loads within 300 m) is a gondola, a resort's main lift from the village to mid-mountain with chairs stacked above it
            Lift village = null;
            float lowest = lifts.Count > 0 ? lifts.Min(lf => lf.Bottom.y) : 0f;
            foreach (Lift lf in lifts)
                if (lf.Kind == "main" && (lf.Type == "detachable" || lf.Type == "fixed-grip") && lf.Bottom.y < lowest + 30f && lf.Length >= 1200f && lf.Length <= 2700f
                    && lifts.Exists(o => o != lf && math.distance(o.Bottom.xz, lf.Top.xz) < 300f) && (village == null || lf.Length > village.Length)
                    && !lifts.Exists(o => o != lf && Aerial(o.Type) && SegCrossScore(o.Bottom.xz, o.Top.xz, lf.Bottom.xz, lf.Top.xz)))
                    village = lf;
            if (village != null && Environment.GetEnvironmentVariable("RF_NOVILLAGE") != "1")
                village.Type = "gondola";
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
            WriteLifts(Path.Combine(outDir, id + ".txt"), id, lifts, lines);

            // the scorecard (RunsFirst.Score.cs) reads the plan exactly as drawn (RunsFirst.Plan.cs)
            var plan = new ScorePlan();
            foreach (PlanRun r in built.Runs)
                (r.Graded ? plan.Connectors : plan.Runs).Add(r.Pts);
            plan.Connectors.AddRange(built.Connectors);
            foreach (Lift lf in lifts)
                plan.Lifts.Add((lf.Type, lf.Bottom.xz, lf.Top.xz));
            foreach (string s in built.Log)
                Console.WriteLine($"REPORT rf plan {id}: {s}");
            RunsFirstScore.Result score = RunsFirstScore.Evaluate(snap, ring, plan);
            Console.WriteLine(RunsFirstScore.Line(id, score));
            if (Environment.GetEnvironmentVariable("RF_V3DBG") == "1")
                V3Debug(g, built, lifts);
            File.AppendAllText(Path.Combine(outDir, id + ".txt"), "\n" + RunsFirstScore.Line(id, score) + "\n" +
                string.Concat(score.Notes.Select(s => "  " + s + "\n")));
            Render(Path.Combine(outDir, id + ".png"), snap, ring, g, acc, accR, lines, v2, lifts, built);
        }

        // ------------------------------------------------------------------ grid and flow

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static Grid BuildGrid(HeightSnapshot s, float2[] ring)
        {
            var g = new Grid { W = s.Width / F, D = s.Depth / F, CellM = s.TexelM * F, S = s };
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
            g.Hr = new float[n];
            for (int c = 0; c < n; c++)
            {
                g.In[c] = Inside(ring, g.World(c));
                g.Hr[c] = RawH(g, g.World(c));
            }
            return g;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static int[] Receivers(Grid g, float[] h, out float[] acc)
        {
            int n = g.W * g.D;
            var fill = new float[n];
            var done = new bool[n];
            var pq = new CellHeap();
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

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static float HeightAt(Grid g, float2 p)
        {
            float fx = (float)((p.x - g.OX) / g.CellM), fz = (float)((p.y - g.OZ) / g.CellM);
            int x0 = Math.Clamp((int)Math.Floor(fx), 0, g.W - 2), z0 = Math.Clamp((int)Math.Floor(fz), 0, g.D - 2);
            float tx = Math.Clamp(fx - x0, 0f, 1f), tz = Math.Clamp(fz - z0, 0f, 1f);
            float a = g.H[z0 * g.W + x0], b = g.H[z0 * g.W + x0 + 1], c = g.H[(z0 + 1) * g.W + x0], d = g.H[(z0 + 1) * g.W + x0 + 1];
            return math.lerp(math.lerp(a, b, tx), math.lerp(c, d, tx), tz);
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static float GradeAt(Grid g, float2 p)
        {
            float e = g.CellM;
            float gx = (HeightAt(g, p + new float2(e, 0)) - HeightAt(g, p - new float2(e, 0))) / (2 * e);
            float gz = (HeightAt(g, p + new float2(0, e)) - HeightAt(g, p - new float2(0, e))) / (2 * e);
            return math.sqrt(gx * gx + gz * gz);
        }

        /// <summary>Raw terrain height (the 3.5 m heightmap, as the game samples it); NaN outside the snapshot.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static float RawH(Grid g, float2 p) => g.S.Sample(new float3(p.x, 0f, p.y));

        /// <summary>Raw grade from a 10 m central difference: what a station pad is judged by.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static float RawGrade(Grid g, float2 p)
        {
            const float d = 10f;
            float gx = (RawH(g, p + new float2(d, 0f)) - RawH(g, p - new float2(d, 0f))) / (2f * d);
            float gz = (RawH(g, p + new float2(0f, d)) - RawH(g, p - new float2(0f, d))) / (2f * d);
            return math.sqrt(gx * gx + gz * gz);
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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

            var rule = Rules(family);
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
                if (i == pts.Count || pitch[i] > rule.MaxPitch)
                {
                    if (i - 1 - a0 > bestB - bestA)
                    {
                        bestA = a0;
                        bestB = i - 1;
                    }
                    a0 = i + 1;
                }
            int run = (int)(100f / StepM);
            while (bestB - run > bestA && (h[bestB - run] - h[bestB]) / 100f < rule.Runout)
                bestB--;
            if (bestB - bestA < 5)
                return;

            var l = new Line { Family = family };
            for (int i = bestA; i <= bestB; i++)
                l.Pts.Add(new float3(pts[i].x, h[i], pts[i].y));
            Stats(g, l);
            if (l.Drop < rule.MinDrop || l.Length < rule.MinLength)
                return;
            lines.Add(l);
        }

        /// <summary>Length, drop, mean / 85th-percentile / steepest 30 m pitch, band, corridor width and score, from Pts.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void Stats(Grid g, Line l)
        {
            int per = (int)(PitchM / StepM), n = l.Pts.Count;
            l.Length = (n - 1) * StepM;
            l.Drop = l.Pts[0].y - l.Pts[n - 1].y;
            l.Mean = l.Length > 0f ? l.Drop / l.Length : 0f;
            var ps = new List<float>();
            for (int i = 0; i + per < n; i++)
                ps.Add(Math.Abs(l.Pts[i].y - l.Pts[i + per].y) / PitchM);
            ps.Sort();
            l.Rating = ps.Count > 0 ? ps[(int)(ps.Count * 0.85f)] : l.Mean;
            l.Steepest = ps.Count > 0 ? ps[ps.Count - 1] : l.Mean;
            l.Band = SkiSlopeClassifier.BandOf(l.Rating);
            l.Width = Width(g, l);
            l.Score = l.Drop * Math.Clamp(l.Width / 40f, 0.3f, 1f) * BandWeight(l.Band);
        }

        /// <summary>Picked line points bucketed on a 60 m grid: Near(p, r) for r up to 60 m.</summary>
        private sealed class PointHash
        {
            private const float Cell = 60f;
            private readonly Dictionary<long, List<float2>> m_Cells = new Dictionary<long, List<float2>>();
            private int m_Count;   // points added: a remembered answer holds only while the hash stays as it was
            private Grid m_MemoGrid;
            private int m_MemoCount;
            private readonly Dictionary<float, sbyte[]> m_Memo = new Dictionary<float, sbyte[]>();
            private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
            public void Add(float2 p)
            {
                long k = Key((int)Math.Floor(p.x / Cell), (int)Math.Floor(p.y / Cell));
                if (!m_Cells.TryGetValue(k, out List<float2> l))
                    m_Cells[k] = l = new List<float2>();
                l.Add(p);
                m_Count++;
            }
            /// <summary>Near(g.World(c), r), remembered per cell (a search asks it for the same cell from every neighbor).</summary>
            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            public bool NearCell(Grid g, int c, float r)
            {
                if (m_MemoGrid != g || m_MemoCount != m_Count)
                {
                    m_Memo.Clear();
                    m_MemoGrid = g;
                    m_MemoCount = m_Count;
                }
                if (!m_Memo.TryGetValue(r, out sbyte[] memo))
                    m_Memo[r] = memo = new sbyte[g.W * g.D];
                if (memo[c] == 0)
                    memo[c] = Near(g.World(c), r) ? (sbyte)1 : (sbyte)-1;
                return memo[c] > 0;
            }
            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            public bool Near(float2 p, float r)
            {
                int cx = (int)Math.Floor(p.x / Cell), cz = (int)Math.Floor(p.y / Cell);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (m_Cells.TryGetValue(Key(cx + dx, cz + dz), out List<float2> l))
                            foreach (float2 q in l)
                                if (math.distancesq(p, q) < r * r)
                                    return true;
                return false;
            }
        }

        /// <summary>Median corridor width: from every third sample, step sideways until the ground turns cliff (over 0.75) or rises /
        /// falls away from the line faster than half the distance.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static PointHash Pick(List<Line> lines, Grid g, float km2)
        {
            lines.Sort((a, b) => b.Score.CompareTo(a.Score));
            for (int i = 0; i < lines.Count; i++)
                lines[i].Id = i + 1;
            var taken = new PointHash();
            var best = new float[5];   // per band: "weak" is judged against lines of the same color
            foreach (Line l in lines)
                best[(int)l.Band] = Math.Max(best[(int)l.Band], l.Score);
            int cap = Math.Clamp((int)Math.Round(PicksPerKm2 * km2), 12, 80), picks = 0;
            foreach (Line l in lines)
            {
                if (picks >= cap)
                {
                    l.Why = "cap";
                    continue;
                }
                if (l.Score < 0.15f * best[(int)l.Band])
                {
                    l.Why = "weak";
                    continue;
                }
                // the merge: the line ends where it runs onto an earlier pick (the shared trunk is drawn and counted once);
                // found first, so a line that feeds a picked trunk is judged on its own part only
                int run = 0, end = l.Pts.Count;
                for (int i = 3; i < l.Pts.Count; i++)
                {
                    run = taken.Near(l.Pts[i].xz, MergeM) ? run + 1 : 0;
                    if (run == 3)
                    {
                        end = i - 1;
                        break;
                    }
                }
                if (end < 6)
                {
                    l.Why = "merges at once";
                    continue;
                }
                int upper = Math.Max(1, (int)(end * 0.6f)), near = 0;
                for (int i = 0; i < upper; i++)
                    if (taken.Near(l.Pts[i].xz, SpacingM))
                        near++;
                if (near > OverlapShare * upper)
                {
                    l.Why = "overlap";
                    continue;
                }
                if (end < l.Pts.Count)
                {
                    l.Pts.RemoveRange(end, l.Pts.Count - end);
                    l.Merge = true;
                    Stats(g, l);
                    if (l.Drop < 40f)
                    {
                        l.Why = "merges at once";
                        continue;
                    }
                }
                l.Picked = true;
                l.Pick = ++picks;
                foreach (float3 p in l.Pts)
                    taken.Add(p.xz);
            }
            return taken;
        }

        private static void WriteTable(string path, string id, List<Line> lines)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# runs-first candidates {id}: map# id family picked band drop_m length_m mean rating steepest width_m score top(x,z,y) bottom(x,z,y) why");
            foreach (Line l in lines)
            {
                float3 t = l.Pts[0], b = l.Pts[l.Pts.Count - 1];
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{18} {0} {1}{19} {2} {3} {4:F0} {5:F0} {6:F2} {7:F2} {8:F2} {9:F0} {10:F0} ({11:F0},{12:F0},{13:F0}) ({14:F0},{15:F0},{16:F0}) {17}",
                    l.Id, l.Family, l.Picked ? "Y" : "-", l.Band, l.Drop, l.Length, l.Mean, l.Rating, l.Steepest, l.Width, l.Score,
                    t.x, t.z, t.y, b.x, b.z, b.y, l.Why, l.Picked ? "#" + l.Pick : "", l.Merge ? "+merge" : ""));
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

        private static void Render(string path, HeightSnapshot s, float2[] ring, Grid g, float[] acc, float[] accR, List<Line> lines, AutoPlanResult v2, List<Lift> lifts, Plan built)
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
            // pixel -> world for the -draw.png (and either panel of <id>.png): tools/rf-pix2world.cjs reads it
            File.WriteAllText(Path.ChangeExtension(path, null) + "-draw.map", string.Format(CultureInfo.InvariantCulture,
                "{{\"rot\":{0},\"w\":{1},\"d\":{2},\"ox\":{3},\"oz\":{4},\"x0\":{5},\"z0\":{6},\"t\":{7},\"panelW\":{8},\"gap\":12}}\n",
                rot, w, d, s.Origin.x, s.Origin.y, s.X0, s.Z0, t, iw));
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
            // right: the plan as scorecard v3 reads it: runs solid by the ground under them; graded tracks dashed, a green cat track
            // light green, a diagonal run (graded steeper than the blue line) in its band's color with a white edge; picked lines no
            // lift serves gray
            void Track(List<float2> tr)
            {
                var (run, band) = TrackClass(g, tr);
                List<float2> pl = tr.ConvertAll(p => fromWorld(p));
                if (run)
                {
                    // a diagonal run (a graded track at a run's grade): thinner than a run, striped white and its band's color
                    Stroke(right, pl, 3.2f, (255, 255, 255));
                    Dashed(right, pl, 1.9f, BandColor(band), 9f, 4f);
                }
                else
                {
                    // a green cat track: dotted
                    Dashed(right, pl, 2.4f, (20, 60, 20), 8f, 4f);
                    Dashed(right, pl, 1.4f, (150, 255, 120), 8f, 4f);
                }
            }
            foreach (List<float2> tr in built.Connectors)
                Track(tr);
            foreach (PlanRun r in built.Runs)
            {
                if (r.Graded)
                {
                    Track(r.Pts);
                    continue;
                }
                List<float2> pl = r.Pts.ConvertAll(p => fromWorld(p));
                if (r.Src != null && r.Src.Lift < 0)
                {
                    Stroke(right, pl, 2f, (150, 150, 150));
                    continue;
                }
                SlopeBand band = GroundBand(g, r.Pts);
                Stroke(right, pl, 4.5f, EdgeColor(band));
                Stroke(right, pl, 3f, BandColor(band));
            }
            foreach (PlanRun r in built.Runs)
                if (r.Src == null || r.Src.Lift >= 0)
                {
                    float2 p0 = fromWorld(r.Pts[0]);
                    right.Disc(p0.x, p0.y, 6f, (255, 255, 255));
                    if (r.Src != null && r.Src.Pick > 0)
                        Label(right, (int)p0.x + 8, (int)p0.y - 6, r.Src.Pick);
                }

            foreach (var (p, cr, cg, cb) in built.Dots)
            {
                float2 q = fromWorld(p);
                right.Disc(q.x, q.y, 2f, (cr, cg, cb));
            }
            foreach (Lift lf in lifts)
            {
                var pl = new List<float2> { fromWorld(lf.Bottom.xz), fromWorld(lf.Top.xz) };
                Stroke(right, pl, 3f, (0, 0, 0));
                Stroke(right, pl, 2f, (255, 220, 0));
                right.Disc(pl[0].x, pl[0].y, 7f, (0, 0, 0));
                right.Disc(pl[0].x, pl[0].y, 5.5f, (255, 220, 0));
                right.Disc(pl[1].x, pl[1].y, 7f, (0, 0, 0));
                right.Disc(pl[1].x, pl[1].y, 5.5f, (255, 220, 0));
                float2 mid = math.lerp(pl[0], pl[1], 0.5f);
                Label(right, (int)mid.x + 6, (int)mid.y - 6, lf.Id, (255, 220, 0), (0, 0, 0));
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

        private static void Dashed(Canvas cv, List<float2> pl, float r, (int, int, int) c, float period = 10f, float on = 6f)
        {
            float run = 0f;
            for (int i = 1; i < pl.Count; i++)
            {
                float len = math.distance(pl[i - 1], pl[i]);
                for (float u = 0; u <= len; u += 0.5f, run += 0.5f)
                    if (run % period < on)
                    {
                        float2 p = math.lerp(pl[i - 1], pl[i], len > 0 ? u / len : 0);
                        cv.Disc(p.x, p.y, r, c);
                    }
            }
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

        private static void Label(Canvas cv, int x, int y, int n) => Label(cv, x, y, n, (0, 0, 0), (255, 255, 255));

        private static void Label(Canvas cv, int x, int y, int n, (int, int, int) bg, (int, int, int) fg)
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
                                cv.Set(x + k * 4 * sc + gx * sc + px, y + gy * sc + py, bg);
                for (int gy = 0; gy < 5; gy++)
                    for (int gx = 0; gx < 3; gx++)
                        if (glyph[gy * 3 + gx] == '1')
                            for (int py = 0; py < sc; py++)
                                for (int px = 0; px < sc; px++)
                                    cv.Set(x + k * 4 * sc + gx * sc + px, y + gy * sc + py, fg);
            }
        }
    }
}
