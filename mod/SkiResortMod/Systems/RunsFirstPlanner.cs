// RunsFirstPlanner: the runs-first resort planner (after field test 5). It reads the mountain the way a person does: drainages and
// spurs, top to bottom. It rates each line, picks a spaced set, places lifts to serve them, then builds the plan a skier would see
// in about 35 passes (RunsFirstPlanner.Plan.cs). Pure C# 9 on System and Unity.Mathematics: no game type, no console, no file.
// One instance plans one region (Plan). It holds all the state of that plan, so two instances can plan at the same time on two
// threads. The test host (tests/AutoGenBatch/RunsFirst.Host.cs, RunsFirst.Render.cs) links these files, scores and renders the plans,
// and keeps the plan hashes (the RF golden). No pass reads the clock: the plan depends on its input only, and the per-pass times
// are a report. See EngineNotes/RunsFirstPlanner.md.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class RunsFirstPlanner
    {
        private const int F = 2;                  // texels per flow cell (7 m cells at 3.5 m texels)
        private const float ChannelM2 = 20000f;   // 2 ha catchment = a drainage (inverted: a spur)
        private const float StepM = 10f, PitchM = 30f;
        private const float SpacingM = 60f;       // the upper 60 % of two picked lines stay this far apart
        private const float OverlapShare = 0.3f;
        private const float MergeM = 35f;         // a picked line ends where it runs onto an earlier one (3 samples in a row this close, a trail width)
        private float PicksPerKm2 => O.PicksPerKm2;     // line cap per km2 (12-80), the Normal density
        private const float GentleLatticeM = 50f, GentleMinGrade = 0.06f, GentleMaxGrade = 0.40f;

        // per family: steepest 30 m pitch the line may hold (cut there), runout grade trimmed off the bottom, min drop / length (m)
        private static (float MaxPitch, float Runout, float MinDrop, float MinLength) Rules(string family) =>
            family == "gentle" ? (0.40f, 0.015f, 40f, 200f) : (0.85f, 0.04f, 120f, 300f);

        // beginner and intermediate vertical is scarce and worth more per meter than expert vertical
        private float BandWeight(SlopeBand b) => MixW != null ? MixW[(int)b] : b == SlopeBand.Green ? 3f : b == SlopeBand.Blue ? 2f : 1f;

        // v5 grade mix (RF_MIXW): weigh a band by the region's own ground over the share of the candidate lines' drop in it, instead of the
        // fixed 3 / 2 / 1. The region's own ground is the share of its 100 m cells in the band, rated as the scorecard rates them. 1: lines
        // banded by their own pitch, the weights also used for run candidates. 2: lines banded by the ground under them (the scorecard's run
        // band), the pick only. Off: both lowered the visible grade mix (see the v5 report).
        private int MixMode => O.MixMode;
        private float MixGreenMin => O.MixGreenMin;   // green keeps at least this weight (the learning areas)
        private float[] MixW;   // per band, for the region being planned (null: the fixed weights)

        /// <summary>The region's ground mix as scorecard v5 rates it: 100 m cells inside the ring, each the 85th percentile of the raw
        /// grade on a 20 m grid inside it, skiable from the green line to a 1.0 pitch (hostile ground counts as double black).</summary>
        private static float[] GroundMix(Grid g, float2[] ring)
        {
            var bands = new float[5];
            float minX = ring.Min(p => p.x), maxX = ring.Max(p => p.x), minZ = ring.Min(p => p.y), maxZ = ring.Max(p => p.y);
            var gs = new List<float>(25);
            for (float z = minZ + 50f; z - 50f < maxZ; z += 100f)
                for (float x = minX + 50f; x - 50f < maxX; x += 100f)
                {
                    var p = new float2(x, z);
                    if (!Inside(ring, p))
                        continue;
                    gs.Clear();
                    for (float dz = -40f; dz < 50f; dz += 20f)
                        for (float dx = -40f; dx < 50f; dx += 20f)
                        {
                            var q = p + new float2(dx, dz);
                            float gq = Inside(ring, q) ? RawGrade(g, q) : float.NaN;
                            if (!float.IsNaN(gq))
                                gs.Add(gq);
                        }
                    if (gs.Count == 0)
                        continue;
                    gs.Sort();
                    float rating = gs[(int)((gs.Count - 1) * 0.85f)];
                    if (rating >= SkiSlopeClassifier.GradeGreen && rating <= 1f)
                        bands[(int)SkiSlopeClassifier.BandOf(rating)]++;
                }
            return bands;
        }

        private void SetMixWeights(string id, Grid g, float2[] ring, List<Line> lines)
        {
            MixW = null;
            if (MixMode == 0)
                return;
            float[] ground = GroundMix(g, ring);
            SlopeBand[] lb = lines.Select(l => MixMode == 2 ? GroundBand(g, l.Pts.Select(p => p.xz).ToList()) : l.Band).ToArray();
            var pool = new float[5];
            for (int i = 0; i < lines.Count; i++)
                pool[(int)lb[i]] += lines[i].Drop * Clamp(lines[i].Width / 40f, 0.3f, 1f);
            float gsum = ground.Sum(), psum = pool.Sum();
            if (gsum <= 0f || psum <= 0f)
                return;
            var w = new float[5];
            for (int b = 1; b < 5; b++)
                w[b] = Clamp(ground[b] / gsum / Math.Max(0.01f, pool[b] / psum), 0.3f, 3f);
            w[(int)SlopeBand.Green] = Math.Max(w[(int)SlopeBand.Green], MixGreenMin);
            w[0] = w[(int)SlopeBand.Green];
            for (int i = 0; i < lines.Count; i++)
                lines[i].Score = lines[i].Drop * Clamp(lines[i].Width / 40f, 0.3f, 1f) * w[(int)lb[i]];
            if (MixMode == 1)
                MixW = w;
            string Pc(float[] a, float s) => string.Join("/", Enumerable.Range(1, 4).Select(b => $"{100f * a[b] / s:F0}"));
            Trace($"REPORT rf {id}: mix weights G/B/K/DB {string.Join("/", w.Skip(1).Select(x => $"{x:F2}"))} (ground {Pc(ground, gsum)} %, lines {Pc(pool, psum)} %)");
        }

        /// <summary>The options the planner reads (the only knobs it has). The host sets them per run; the default is today's defaults.</summary>
        internal RunsFirstOptions O = new RunsFirstOptions();

        private sealed class Grid
        {
            public int W, D;
            public float CellM;
            public double OX, OZ;          // world xz of cell (0, 0)'s center
            public float[] H;              // smoothed heights
            public bool[] In;
            public HeightSnapshot S;       // the raw terrain: the code checks stations, traverses and runs on it
            public float[] Hr;             // raw height at each cell center
            public BandRater Rater;        // the mod rater's band of a polyline, cached (RunsFirstPlanner.Rater.cs)
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

        // The clock only reports: no pass reads it, so a plan depends on its input alone (the time cutoff RF_LATEMS and the learning
        // search's RF_ABOVEMS went in the port round R0-c). Tick records the end of each pass; the host prints the table.
        private System.Diagnostics.Stopwatch Clock = new System.Diagnostics.Stopwatch();
        private bool Timing => O.Timing;
        private readonly List<(string Pass, long Ms)> m_Ticks = new List<(string, long)>();   // each pass's end, ms since the start

        /// <summary>The per-pass time table of the last plan, in plan order: each pass's own milliseconds (a report only, never read
        /// by a pass). See EngineNotes/RunsFirstPlanner.md for the pass list.</summary>
        public IReadOnlyList<(string Pass, long Ms)> PassTimes
        {
            get
            {
                var t = new List<(string, long)>(m_Ticks.Count);
                for (int i = 0; i < m_Ticks.Count; i++)
                    t.Add((m_Ticks[i].Pass, m_Ticks[i].Ms - (i > 0 ? m_Ticks[i - 1].Ms : 0L)));
                return t;
            }
        }

        // Progress: the passes done out of PassCount (each Tick is one), plus an inner fraction in the three longest passes (the
        // lift-top reach scan, the serve pick, the regrade). A pass an option turns off still ticks. It never goes back.
        private const int PassCount = 35;
        private volatile int m_Progress;   // per-mille, written by the planning thread (and the reach scan's workers), read by any thread
        private readonly object m_ProgressLock = new object();
        private int m_PassesDone;

        /// <summary>Progress of the plan in per-mille (0 to 1000), safe to read from any thread while Plan runs. It never goes back and
        /// ends at 1000 when Plan returns a plan.</summary>
        public int ProgressVolatile => m_Progress;

        private void SetProgress(int perMille)
        {
            perMille = Clamp(perMille, 0, 1000);
            lock (m_ProgressLock)
                if (perMille > m_Progress)
                    m_Progress = perMille;
        }

        /// <summary>The share done of the pass in work (0 to 1): progress inside a long pass. Also a cancel poll.</summary>
        private void Inner(float share)
        {
            Poll();
            SetProgress(Math.Min(999, (int)((m_PassesDone + Clamp(share, 0f, 1f)) * 1000f / PassCount)));
        }

        // Cancel: the token is polled at every pass boundary (Tick), at every inner progress step and every few thousand steps of the
        // long loops. A request ends the plan at the next poll: Plan returns null and the instance is dropped with its half-made state.
        private AutoPlanCancel m_Cancel;

        private sealed class PlanCanceled : Exception
        {
        }

        private bool CancelRequested => m_Cancel != null && m_Cancel.Requested;

        private void Poll()
        {
            if (m_Cancel != null && m_Cancel.Requested)
                throw new PlanCanceled();
        }

        private static bool IsCanceled(AggregateException e)
        {
            foreach (Exception x in e.Flatten().InnerExceptions)
                if (!(x is PlanCanceled))
                    return false;
            return true;
        }

        private void Tick(string what)
        {
            Poll();
            m_Ticks.Add((what, Clock.ElapsedMilliseconds));
            m_PassesDone++;
            SetProgress(Math.Min(999, m_PassesDone * 1000 / PassCount));
            if (Timing)
                Trace($"TIME {what} {Clock.ElapsedMilliseconds} ms (easy {EasyWork}, links {LinkWork})");
            // RF_WHDBG: the lifts without a way home after each stage of the plan
            if (WayDbg != null)
            {
                var (g, plan, lifts) = WayDbg.Value;
                bool[] ok = WayHomeOk(Ski(g, plan, lifts), lifts, V3Bases(g, lifts, out HashSet<int> bases), bases);
                Trace($"WHDBG {what}: none from " + string.Join(" ", Enumerable.Range(0, lifts.Count).Where(L => !ok[L]).Select(L => "L" + lifts[L].Id)));
            }
        }
        private (Grid, PlanDraft, List<Lift>)? WayDbg;

        /// <summary>Where the debug and timing lines go (the host sets it to the console). Null: they go to Log. The default options
        /// print none.</summary>
        internal Action<string> TraceSink { get; set; }
        private readonly List<string> m_Log = new List<string>();

        /// <summary>The planner's report: the debug and timing lines the options asked for (none by default), then the plan's own
        /// log (the lines RunsFirstResult.Log holds too). Read it after Plan returns.</summary>
        public IReadOnlyList<string> Log => m_Log;

        private void Trace(string line)
        {
            if (TraceSink != null)
                TraceSink(line);
            else
                m_Log.Add(line);
        }

        /// <summary>A finished plan: the plain result, plus the planner's own objects the host draws, writes and scores.</summary>
        private sealed class Planned
        {
            public Grid G;
            public float[] Acc, AccR;
            public List<Line> Lines;
            public List<Lift> Lifts;
            public PlanDraft Built;
            public long Ms;
            public RunsFirstResult Result;
        }

        /// <summary>The region's name in the planner's own report lines (the host sets it).</summary>
        internal string Id = "region";
        private bool m_Used;

        /// <summary>Plans one region and returns the plain plan object, or null when the cancel token was set (it returns within a
        /// fraction of a second of the request). One instance plans once: a planner holds the state of its plan (no static state), so
        /// two instances may plan at the same time on two threads. Options null: the defaults.</summary>
        public RunsFirstResult Plan(HeightSnapshot snapshot, float2[] ring, RunsFirstOptions options, AutoPlanCancel cancel = null)
        {
            if (m_Used)
                throw new InvalidOperationException("RunsFirstPlanner plans once: use a new instance for each plan");
            m_Used = true;
            O = options ?? new RunsFirstOptions();
            m_Cancel = cancel;
            RunsFirstResult result;
            try
            {
                Poll();
                result = PlanCore(Id, snapshot, ring).Result;
            }
            catch (PlanCanceled)
            {
                return null;
            }
            catch (AggregateException e) when (IsCanceled(e))
            {
                return null;   // a poll inside the reach scan's Parallel.For
            }
            SetProgress(1000);
            return result;
        }

        private Planned PlanCore(string id, HeightSnapshot snap, float2[] ring)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Clock = sw;
            m_Ticks.Clear();
            EasyWork = LinkWork = 0;
            Grid g = BuildGrid(snap, ring);
            g.Rater = new BandRater { Cancel = m_Cancel, Bits = O.RaterBands };
            Tick("grid");
            int n = g.W * g.D;
            float cellsPerChannel = ChannelM2 / (g.CellM * g.CellM);

            // drainages on the terrain, spurs on the inverted terrain
            int[] rec = Receivers(g, g.H, out float[] acc);
            var inv = new float[n];
            for (int c = 0; c < n; c++)
                inv[c] = -g.H[c];
            Poll();
            int[] recR = Receivers(g, inv, out float[] accR);
            Tick("flow");

            var lines = new List<Line>();
            foreach (List<int> path in Stems(g, rec, acc, cellsPerChannel))
            {
                Poll();
                AddLine(lines, g, path, "drain");
            }
            foreach (List<int> path in Stems(g, recR, accR, cellsPerChannel))
            {
                Poll();
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
            SortStable(seeds, (a, b) => g.H[b].CompareTo(g.H[a]));
            foreach (int s0 in seeds)
            {
                Poll();
                var path = new List<int>();
                for (int c = s0; c >= 0; c = rec[c])
                    path.Add(c);
                AddLine(lines, g, path, "gentle");
            }
            float km2r = (float)(SkiRunAutoPlanner.PolygonArea(ring) / 1e6) - BlockedKm2;   // the open area (Inputs.cs: 0 with no input)
            Tick("lines");
            SetMixWeights(id, g, ring, lines);
            PointHash taken = Pick(lines, g, km2r);
            Tick("pick");
            List<Lift> lifts = PlaceLifts(g, rec, lines, km2r, taken);
            Tick("lifts");
            PlanDraft built = BuildPlan(g, rec, lines, lifts, km2r);
            Tick("plan");
            // the village lift (from the opus-med contest entry): the longest main lift off the lowest base whose top is a hub (another
            // lift loads within 300 m) is a gondola: a resort's main lift from the village to mid-mountain, with chairs stacked above it
            Lift village = null;
            float lowest = lifts.Count > 0 ? lifts.Min(lf => lf.Bottom.y) : 0f;
            foreach (Lift lf in lifts)
                if (lf.Kind == "main" && (lf.Type == "detachable" || lf.Type == "fixed-grip") && lf.Bottom.y < lowest + 30f && lf.Length >= 1200f && lf.Length <= 2700f
                    && lifts.Exists(o => o != lf && math.distance(o.Bottom.xz, lf.Top.xz) < 300f) && (village == null || lf.Length > village.Length)
                    && !lifts.Exists(o => o != lf && Aerial(o.Type) && SegCrossScore(o.Bottom.xz, o.Top.xz, lf.Bottom.xz, lf.Top.xz)))
                    village = lf;
            if (village != null && O.Village)
                village.Type = "gondola";
            if (TypeUse == 2)
                RetypeByUse(g, built, lifts, village);
            // the drawn numbers follow the list, as the scorecard's notes count the lifts (passes take lifts out and add new ones)
            for (int i = 0; i < lifts.Count; i++)
                lifts[i].Id = i + 1;
            // lifts the lift tool can build: a lift whose tower plan is not Sound is shifted, retyped or kept flagged (Towers.cs)
            if (O.Towers)
                FitTowers(g, built, lifts, km2r);
            Tick("towers");
            RunsFirstResult result = MakeResult(g, built, lifts);
            Tick("result");
            long ms = sw.ElapsedMilliseconds;
            m_Log.AddRange(built.Log);
            return new Planned { G = g, Acc = acc, AccR = accR, Lines = lines, Lifts = lifts, Built = built, Ms = ms, Result = result };
        }


        [MethodImpl(Hot)]
        private Grid BuildGrid(HeightSnapshot s, float2[] ring)
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
                if ((c & 32767) == 32767)
                    Poll();
                g.In[c] = Inside(ring, g.World(c));
                g.Hr[c] = RawH(g, g.World(c));
            }
            ApplyInputs(g);   // the game's blocked ground leaves the mask (RunsFirstPlanner.Inputs.cs). No input: no change
            return g;
        }

        [MethodImpl(Hot)]
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

        [MethodImpl(Hot)]
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
        [MethodImpl(Hot)]
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
            SortByKey(keys, order);
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

        [MethodImpl(Hot)]
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

        /// <summary>Hack-order stems: from each outlet, the main stem climbs the largest upstream branch to the divide. Every other
        /// branch over the channel size starts its own stem. Each returned path runs from its stem's top down to the outlet.</summary>
        private static IEnumerable<List<int>> Stems(Grid g, int[] rec, float[] acc, float minCells)
        {
            int n = g.W * g.D;
            var first = new int[n];
            var next = new int[n];
            FillAll(first, -1);
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

        [MethodImpl(Hot)]
        private static float HeightAt(Grid g, float2 p)
        {
            float fx = (float)((p.x - g.OX) / g.CellM), fz = (float)((p.y - g.OZ) / g.CellM);
            int x0 = Clamp((int)Math.Floor(fx), 0, g.W - 2), z0 = Clamp((int)Math.Floor(fz), 0, g.D - 2);
            float tx = Clamp(fx - x0, 0f, 1f), tz = Clamp(fz - z0, 0f, 1f);
            float a = g.H[z0 * g.W + x0], b = g.H[z0 * g.W + x0 + 1], c = g.H[(z0 + 1) * g.W + x0], d = g.H[(z0 + 1) * g.W + x0 + 1];
            return math.lerp(math.lerp(a, b, tx), math.lerp(c, d, tx), tz);
        }

        [MethodImpl(Hot)]
        private static float GradeAt(Grid g, float2 p)
        {
            float e = g.CellM;
            float gx = (HeightAt(g, p + new float2(e, 0)) - HeightAt(g, p - new float2(e, 0))) / (2 * e);
            float gz = (HeightAt(g, p + new float2(0, e)) - HeightAt(g, p - new float2(0, e))) / (2 * e);
            return math.sqrt(gx * gx + gz * gz);
        }

        /// <summary>Raw terrain height (the 3.5 m heightmap, as the game samples it). NaN outside the snapshot.</summary>
        [MethodImpl(Hot)]
        private static float RawH(Grid g, float2 p) => g.S.Sample(new float3(p.x, 0f, p.y));

        /// <summary>Raw grade from a 10 m central difference: what the code judges a station pad by.</summary>
        [MethodImpl(Hot)]
        private static float RawGrade(Grid g, float2 p)
        {
            const float d = 10f;
            float gx = (RawH(g, p + new float2(d, 0f)) - RawH(g, p - new float2(d, 0f))) / (2f * d);
            float gz = (RawH(g, p + new float2(0f, d)) - RawH(g, p - new float2(0f, d))) / (2f * d);
            return math.sqrt(gx * gx + gz * gz);
        }

        [MethodImpl(Hot)]
        private void AddLine(List<Line> lines, Grid g, List<int> cells, string family)
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
        [MethodImpl(Hot)]
        private void Stats(Grid g, Line l)
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
            l.Band = g.Rater.Use(RaterLine) ? RaterBand(g, l.Pts.ConvertAll(p => p.xz)) : SkiSlopeClassifier.BandOf(l.Rating);
            l.Width = Width(g, l);
            l.Score = l.Drop * Clamp(l.Width / 40f, 0.3f, 1f) * BandWeight(l.Band);
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
            [MethodImpl(Hot)]
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
            [MethodImpl(Hot)]
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
        [MethodImpl(Hot)]
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

        [MethodImpl(Hot)]
        private PointHash Pick(List<Line> lines, Grid g, float km2)
        {
            SortStable(lines, (a, b) => b.Score.CompareTo(a.Score));
            for (int i = 0; i < lines.Count; i++)
                lines[i].Id = i + 1;
            var taken = new PointHash();
            var best = new float[5];   // per band: the pick judges "weak" against lines of the same color
            foreach (Line l in lines)
                best[(int)l.Band] = Math.Max(best[(int)l.Band], l.Score);
            int cap = Clamp((int)Math.Round(PicksPerKm2 * km2), 12, 80), picks = 0;
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
                // the merge: the line ends where it runs onto an earlier pick (the code draws and counts the shared trunk once).
                // The code finds the merge first, so it judges a line that feeds a picked trunk on its own part only
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
    }
}
