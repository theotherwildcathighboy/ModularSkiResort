// RunsFirst lifts (prototype): lifts placed to serve the picked lines, the way a resort is laid out around its terrain.
// A lift top "reaches" every cell a skier gets to from it with these limits:
// - no climbing (+3 % at most)
// - nothing steeper than a black start (40 %)
// - no crossing of a cliff cell.
// The traverse along a ridge that opens a whole wall or bowl is an example. A line counts for a lift from the highest point the
// top reaches down to where the line (or the drainage below it) passes the lift's bottom station. The search is greedy by new
// vertical served. Stacked lifts fall out of it (a lower lift is credited with the lower parts of long lines).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class RunsFirst
    {
        private const float AccessRadiusM = 1500f, AccessMaxDown = 0.35f, AccessMaxUp = 0.07f, AccessCliff = 0.75f;
        private const float AccessRawPitch = 0.42f, AccessRawRise = 3.5f;   // on the raw terrain: 30 m pitch, rise within 55 m
        private const float StationMax = 0.34f;   // raw 10 m grade a station pad may sit on (the scorecard flags over 0.40)
        private static readonly float TopLatticeM = Env("RF_LAT", 100f), ReturnM = Env("RF_RET", 150f);
        private static readonly float ReachBudget = Env("RF_RBUDGET", 15e6f);   // cells the top lattice's reach searches may visit
        // a lattice point with no pad within 30 m takes the flattest pad within this reach, as a top for the coverage lifts only.
        // The reach is about half the lattice diagonal, so every pad on a steep ridge belongs to some lattice point
        private static readonly float PadReachM = Env("RF_PADR", 70f);
        private static readonly int ScanThreads = Math.Max(1, (int)Env("RF_THREADS", 4f));   // threads for the top lattice's reach scan
        private const float MarginalShare = 0.1f;   // a lift worth less than this share of the first is marginal
        private const float HubLinkM = 150f, BenchGrade = 0.25f, MinLapM = 60f;
        private const float TowerLiftM = 20f;
        private const float TwinM = 300f;       // a lift whose top AND bottom are this close to an existing one's is a twin, not a new lift

        // hourly capacity relative to a detachable quad (2,400 / h): a tram lap is worth a third of a chair lap to a resort
        private static float Capacity(string type) => type switch { "fixed-grip" => 0.75f, "detachable" => 1f, "gondola" => GondolaCap, _ => 0.3f };   // terrain may rise this far above the straight chord (towers carry the rope over it)
        // v5 lift type by use: 0 off, 1 in the placement (the value follows the new type), 2 a relabel of the finished plan
        private static readonly int TypeUse = (int)Env("RF_TYPEUSE", 2f);
        private static readonly float ChairMaxRise = Env("RF_CHAIRRISE", 700f), VeryLongM = Env("RF_VERYLONG", 2500f), TramRise = Env("RF_TRAMRISE", 800f);
        private static readonly float GondolaCap = Env("RF_GONDCAP", 0.6f);
        private static readonly float LiftFallW = Env("RF_LIFTFALL", 0f);   // the share of a lift's value lost 60 degrees off the fall line (off: at 0.6 it re-picked the lifts, -2.8 visible, more lone bases)

        private static readonly List<(float2 P, int R, int G, int B)> DebugDots = new List<(float2, int, int, int)>();   // RF_TOPS marks

        private sealed class Lift
        {
            public int Id;
            public string Type;
            public string Kind = "main";    // main / beginner
            public float3 Bottom, Top;
            public int TopCell, BottomCell, TopIndex, BottomIndex;
            public float Transfer;          // credit for delivering skiers to other lifts' bottoms (over a ridge into a bowl)
            public bool RidesDown;          // a download lift (no ski-out): a gondola or tram whatever its rise
            public float OpensKm2;          // a coverage lift: the skiable ground the code placed it to open
            public float Length, Rise, Gain, BottomGrade;
            public readonly List<Line> Lines = new List<Line>();
            public readonly List<List<float2>> Traverses = new List<List<float2>>();
            public readonly List<(Line Line, int Entry, List<int> Cells)> Paths = new List<(Line, int, List<int>)>();   // entry to top
        }

        // name, length min / max (m), rise min / max (m), mean grade cap: stage-a-spec 4.4 norms and edge caps
        private static readonly (string Name, float L0, float L1, float R0, float R1, float Cap)[] Types =
        {
            ("fixed-grip", 300f, 1500f, 95f, 512f, 0.8f),
            ("detachable", 600f, 2200f, 95f, 512f, 0.9f),
            ("gondola", 800f, 2700f, 96f, 781f, 1.2f),
            ("tram", 800f, 3000f, 220f, 888f, 2.5f),
            ("beginner", 150f, 1500f, 30f, 150f, 0.8f),   // scorecard v5: a learning chair, given by a pass, never by the ladder (TypeFor)
        };

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static int CellOf(Grid g, float2 p)
        {
            int x = (int)Math.Round((p.x - g.OX) / g.CellM), z = (int)Math.Round((p.y - g.OZ) / g.CellM);
            return x < 0 || z < 0 || x >= g.W || z >= g.D ? -1 : z * g.W + x;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static List<Lift> PlaceLifts(Grid g, int[] rec, List<Line> all, float km2, PointHash runs)
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
            var lowOrder = new List<int>();   // the hubs in first-seen order (a defined order, not the dictionary's)
            for (int i = 0; i < ends.Count; i++)
            {
                if (ends[i] < 0)
                    continue;
                int r = Root(hub, i);
                if (!low.TryGetValue(r, out int cur))
                    lowOrder.Add(r);
                if (!low.TryGetValue(r, out cur) || g.H[ends[i]] < g.H[cur])
                    low[r] = ends[i];
            }
            foreach (int c0 in lowOrder.Select(r => low[r]))
            {
                int c = FlatCell(g, c0, 35f);
                if (c >= 0 && !bottoms.Contains(c))
                    bottoms.Add(c);
            }
            foreach (Line l in lines)
                for (int i = 0; i < l.Pts.Count; i += 15)
                {
                    int c = CellOf(g, l.Pts[i].xz);
                    if (c >= 0 && g.In[c] && slope[c] < BenchGrade && !Near(g, bottoms, c, 120f))
                    {
                        c = FlatCell(g, c, 35f);
                        if (c >= 0)
                            bottoms.Add(c);
                    }
                }

            // stations for the coverage lifts: flat ground on a 150 m lattice away from the other candidates (not for the main search)
            int mainB = bottoms.Count;
            if (Environment.GetEnvironmentVariable("RF_NOCOVERLIFT") != "1")
            {
                int bs = Math.Max(1, (int)Math.Round(150f / g.CellM));
                for (int z = bs / 2; z < g.D; z += bs)
                    for (int x = bs / 2; x < g.W; x += bs)
                    {
                        int c = z * g.W + x;
                        if (g.In[c] && (c = FlatCell(g, c, 35f)) >= 0 && !Near(g, bottoms, c, 120f))
                            bottoms.Add(c);
                    }
            }
            // where each line passes each bottom (first / last index along Pts + Flow, -1 = never)
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
            var reachB = new List<bool[]>();   // per top: which bottom candidates its reach touches
            int[] latIdx = SkiLattice(g, out int latCount);
            var flow = Environment.GetEnvironmentVariable("RF_NOBELOW") == "1" ? null : new LatticeFlow(g, rec, latIdx);
            var reachBits = new List<ulong[]>();   // per top: the skiable lattice points below its reach
            var cTops = new List<int>();           // every lattice top with a pad, picked lines in reach or not (the coverage lifts)
            var cReachB = new List<bool[]>();
            var cReachBits = new List<ulong[]>();
            var dist = new float[n];
            Array.Fill(dist, float.PositiveInfinity);
            // the lattice in levels, coarse to fine (400 m, its centers, 200 m, its centers, 100 m). On wide gentle ground every
            // top reaches most of the region. So the code scans a finer level only while the reach work stays in budget (a gentle
            // 9 km2 valley would take seconds). The tops keep the lattice order
            var lattice = new List<(int Order, int Level, int T, bool Far)>();
            for (int z = step / 2, j = 0, order = 0; z < g.D; z += step, j++)
                for (int x = step / 2, i = 0; x < g.W; x += step, i++, order++)
                {
                    int t = z * g.W + x;
                    if (!g.In[t])
                        continue;
                    int pad = FlatCell(g, t, 30f);
                    bool far = pad < 0 && PadReachM > 30f;
                    if (far)
                        pad = FlatCell(g, t, PadReachM);
                    if ((t = pad) < 0)
                        continue;
                    int level = i % 4 == 0 && j % 4 == 0 ? 0 : i % 4 == 2 && j % 4 == 2 ? 1 : i % 2 == 0 && j % 2 == 0 ? 2 : i % 2 == 1 && j % 2 == 1 ? 3 : 4;
                    lattice.Add((order, level, t, far));
                }
            var scanned = new SortedDictionary<int, (int T, int[] E, bool Any, bool[] Rb, ulong[] Bits, bool Far)>();
            long work = 0;
            int levels = 0;
            // each top's reach is independent of the others. So the code scans a level's tops on a few threads, each with its own
            // search buffers (the plan is the same on any thread count)
            var buffers = new ConcurrentBag<(float[] Dist, int[] Parent, bool[] Mark)>();
            (float[] Dist, int[] Parent, bool[] Mark) NewBuffers()
            {
                var d = new float[n];
                Array.Fill(d, float.PositiveInfinity);
                return (d, new int[n], flow?.NewMark());
            }
            for (; levels < 5; levels++)
            {
                var at = lattice.FindAll(p => p.Level == levels);
                double projected = scanned.Count > 0 ? work + (double)work / scanned.Count * at.Count : 0;
                if (Environment.GetEnvironmentVariable("RF_TIME") == "1")
                    Console.WriteLine($"TIME tops level {levels}: {at.Count} top(s), {projected / 1e6:F2} M cells projected");
                if (projected > ReachBudget)
                    break;
                var found = new (int[] E, bool Any, bool[] Rb, ulong[] Bits, int Work)[at.Count];
                Parallel.For(0, at.Count, new ParallelOptions { MaxDegreeOfParallelism = ScanThreads },
                    () => buffers.TryTake(out var b) ? b : NewBuffers(),
                    (i, _, buf) =>
                    {
                        float[] reached = buf.Dist;
                        List<int> touched = Reach(g, slope, at[i].T, reached, buf.Parent);
                        var e = new int[lines.Count];
                        bool any = false;
                        for (int li = 0; li < lines.Count; li++)
                        {
                            e[li] = -1;
                            List<float3> pts = lines[li].Pts;
                            for (int k = 0; k < pts.Count; k++)
                            {
                                int c = CellOf(g, pts[k].xz);
                                if (c >= 0 && reached[c] < float.PositiveInfinity)
                                {
                                    e[li] = k;
                                    any = true;
                                    break;
                                }
                            }
                        }
                        var rb = new bool[bottoms.Count];
                        for (int b = 0; b < bottoms.Count; b++)
                            rb[b] = reached[bottoms[b]] < float.PositiveInfinity;
                        ulong[] bits;
                        if (flow != null)
                            bits = flow.Below(touched, latCount, buf.Mark);
                        else
                        {
                            bits = new ulong[(latCount + 63) / 64];
                            foreach (int c in touched)
                                if (latIdx[c] >= 0)
                                    bits[latIdx[c] / 64] |= 1UL << (latIdx[c] % 64);
                        }
                        foreach (int c in touched)
                            reached[c] = float.PositiveInfinity;
                        found[i] = (e, any, rb, bits, touched.Count);
                        return buf;
                    },
                    buf => buffers.Add(buf));
                for (int i = 0; i < at.Count; i++)
                {
                    work += found[i].Work;
                    scanned[at[i].Order] = (at[i].T, found[i].E, found[i].Any, found[i].Rb, found[i].Bits, at[i].Far);
                }
            }
            foreach (var (t, e, any, rb, bits, far) in scanned.Values)
            {
                cTops.Add(t);
                if (Environment.GetEnvironmentVariable("RF_TOPS") == "1")
                    DebugDots.Add((g.World(t), any ? 0 : 255, any ? 255 : 0, 255));
                cReachB.Add(rb);
                cReachBits.Add(bits);
                if (any && !far)
                {
                    tops.Add(t);
                    entry.Add(e);
                    reachB.Add(rb);
                    reachBits.Add(bits);
                }
            }
            if (Environment.GetEnvironmentVariable("RF_TIME") == "1")
                Console.WriteLine($"TIME tops: {scanned.Count} of {lattice.Count} lattice tops ({levels} of 5 levels), {work} cells reached");
            Tick("tops");
            // greedy: the lift that adds the most new vertical (per line, elevation intervals already lapped don't count twice)
            var covered = new List<(float lo, float hi)>[lines.Count];
            for (int li = 0; li < lines.Count; li++)
                covered[li] = new List<(float, float)>();
            float vertical = hMax - hMin;
            float perKm2 = float.Parse(Environment.GetEnvironmentVariable("RF_LPK") ?? "1.1", System.Globalization.CultureInfo.InvariantCulture);
            float stopShare = float.Parse(Environment.GetEnvironmentVariable("RF_STOP") ?? "0.05", System.Globalization.CultureInfo.InvariantCulture);
            // the scorecard's bases: lift bottoms in the lowest 35 % of the region's (raw) relief
            float rLo = float.MaxValue, rHi = float.MinValue;
            for (int c = 0; c < n; c++)
                if (g.In[c] && !float.IsNaN(g.Hr[c]))
                {
                    rLo = Math.Min(rLo, g.Hr[c]);
                    rHi = Math.Max(rHi, g.Hr[c]);
                }
            float lowCut = rLo + 0.35f * (rHi - rLo);
            var excluded = new HashSet<int>();   // bottom candidates a marginal lift may not use
            for (int b = mainB; b < B; b++)
                excluded.Add(b);
            int want = Math.Clamp(Math.Max((int)Math.Round(km2 * perKm2), (int)Math.Ceiling(vertical / 400f)), 1, (int)Math.Ceiling(1.5 * km2) + 2);
            var lifts = new List<Lift>();
            float firstGain = 0f;
            var fed = new HashSet<int>();   // lifts whose bottom another lift already delivers to
            var bans = new HashSet<long>();   // top-bottom pairs whose bottom found no pad of its own
            var spotFails = new int[B];
            int fails = 0;
            bool spread = Environment.GetEnvironmentVariable("RF_NOSPREAD") != "1" && SpreadEach;
            while (lifts.Count < want)
            {
                float best = 0f, bestOwn = 0f, bestTransfer = 0f;
                int bt = -1, bb = -1;
                string bestType = null;
                for (int ti = 0; ti < tops.Count; ti++)
                    for (int b = 0; b < B; b++)
                    {
                        if (excluded.Contains(b) || bans.Contains((long)ti * B + b))
                            continue;
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
                        // the transfer: this top reaches a placed lift's bottom from above (a base lift over the ridge into a bowl)
                        float transfer = 0f;
                        foreach (Lift lf in lifts)
                            if (!fed.Contains(lf.Id) && reachB[ti][lf.BottomIndex] && g.H[t] > lf.Bottom.y + 20f)
                                transfer += 0.5f * lf.Gain;
                        if (gain + transfer <= best)
                            continue;
                        if (OnRuns(g, runs, bc, t) || AlongEdge(g, bc, t))
                            continue;
                        // a pure transfer lift (no runs of its own, it carries skiers from one area into the next) may run beside one other
                        string type = TypeFor(g, bc, t);
                        if (type == null || (gain + transfer) * Capacity(type) <= best)
                            continue;
                        // v5: a lift climbs its slope's fall line. A lift that climbs sideways across the slope is worth less (no cost to
                        // 30 degrees off the fall line, FallW of the value at 60)
                        float value = (gain + transfer) * Capacity(type);
                        if (LiftFallW > 0f)
                            value *= 1f - LiftFallW * FallCost(g, g.World(bc), g.World(t), 20f);
                        if (value <= best || Twin(g, lifts, t, bc, 200f, type, gain > 0f ? 0 : TransferBeside))
                            continue;
                        best = value;
                        bestOwn = gain;
                        bestTransfer = transfer;
                        bt = ti;
                        bb = b;
                        bestType = type;
                    }
                if (bt < 0 || (firstGain > 0f && best < stopShare * firstGain))
                {
                    if (Environment.GetEnvironmentVariable("RF_DEBUG") == "1")
                        Console.WriteLine($"DEBUG lifts stop at {lifts.Count} of {want}: best {best:F0} first {firstGain:F0} (bt {bt})");
                    break;
                }
                if (Environment.GetEnvironmentVariable("RF_DEBUG") == "1")
                    Console.WriteLine($"DEBUG lift {lifts.Count + 1}: {bestType} gain {bestOwn:F0} transfer {bestTransfer:F0} value {best:F0} top {g.World(tops[bt]).x:F0},{g.World(tops[bt]).y:F0} bottom {g.World(bottoms[bb]).x:F0},{g.World(bottoms[bb]).y:F0}{(firstGain > 0f && best < MarginalShare * firstGain && OpensBase(g, lifts, bottoms[bb], lowCut) ? " (marginal, opens a base: skipped)" : "")}");
                // a marginal lift may not open a base of its own. A low station away from every other base needs its own beginner
                // terrain and services. A lift worth that little does not earn them
                if (firstGain > 0f && best < MarginalShare * firstGain && OpensBase(g, lifts, bottoms[bb], lowCut))
                {
                    excluded.Add(bb);
                    continue;
                }
                if (firstGain == 0f)
                    firstGain = best;
                int T = tops[bt], Bc = bottoms[bb];
                var lift = new Lift
                {
                    Id = lifts.Count + 1, Type = bestType, TopCell = T, BottomCell = Bc, Gain = bestOwn, Transfer = bestTransfer,
                    TopIndex = bt, BottomIndex = bb,
                    Top = new float3(g.World(T).x, g.H[T], g.World(T).y), Bottom = new float3(g.World(Bc).x, g.H[Bc], g.World(Bc).y),
                    BottomGrade = slope[Bc],
                };
                lift.Rise = lift.Top.y - lift.Bottom.y;
                lift.Length = math.distance(lift.Top, lift.Bottom);
                // a bottom on a placed lift's bottom moves to a pad of its own now. The lines that fan out from that spot are few now. Once
                // the search places all of them, every sideways move crosses the next line. Tops wait for the coverage lifts (their index).
                // Where no pad is free (a crowded base) the lift is not placed. The search takes the next pair
                if (spread)
                {
                    var keep = lifts.ConvertAll(lf => (lf.Bottom, lf.BottomCell, lf.Top, lf.TopCell, lf.Rise, lf.Length));
                    lifts.Add(lift);
                    SpreadTerminals(g, lifts, null, runs, true);
                    lifts.RemoveAt(lifts.Count - 1);
                    if (OwnBottom && fails < OwnBottomTries && lifts.Exists(o => math.distance(o.Bottom.xz, lift.Bottom.xz) < LaySameSpotM + 2f))
                    {
                        for (int i = 0; i < lifts.Count; i++)
                            (lifts[i].Bottom, lifts[i].BottomCell, lifts[i].Top, lifts[i].TopCell, lifts[i].Rise, lifts[i].Length) = keep[i];
                        bans.Add((long)bt * B + bb);
                        if (++spotFails[bb] >= 2)
                            excluded.Add(bb);
                        fails++;
                        if (Environment.GetEnvironmentVariable("RF_DEBUG") == "1")
                            Console.WriteLine($"DEBUG lift {lifts.Count + 1}: no pad of its own at {lift.Bottom.x:F0},{lift.Bottom.z:F0}: not placed");
                        continue;
                    }
                }
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
                    // the traverse from the top to the entry (cells, entry first)
                    var cells = new List<int>();
                    for (int c = CellOf(g, lines[li].Pts[e].xz); c >= 0 && c != T; c = parent[c])
                        cells.Add(c);
                    cells.Add(T);
                    lift.Paths.Add((lines[li], e, cells));
                    if (cells.Count * g.CellM > 150f)
                        lift.Traverses.Add(cells.ConvertAll(c => g.World(c)));
                }
                foreach (int c in touched)
                    dist[c] = float.PositiveInfinity;
                foreach (Lift lf in lifts)
                    if (reachB[bt][lf.BottomIndex] && lift.Top.y > lf.Bottom.y + 20f)
                        fed.Add(lf.Id);
                lifts.Add(lift);
            }
            if (Environment.GetEnvironmentVariable("RF_NOCOVERLIFT") != "1")
                AddCoverLifts(g, rec, lifts, cTops, cReachB, cReachBits, bottoms, latIdx, latCount, runs, km2, slope);
            Tick("cover lifts");
            // each lift its own terminal (scorecard v4 layout): a station on another lift's spot moves to a pad of its own
            if (Environment.GetEnvironmentVariable("RF_NOSPREAD") != "1")
                SpreadTerminals(g, lifts, null, runs);
            Tick("spread");
            if (Environment.GetEnvironmentVariable("RF_DEBUG") == "1")
                for (int L = 0; L < lifts.Count; L++)
                {
                    var others = lifts.FindAll(o => o != lifts[L]);
                    if (UnderRope(g, others, lifts[L].TopCell, lifts[L].BottomCell))
                        Console.WriteLine($"DEBUG L{lifts[L].Id}: a station under another rope (or one under its own)");
                }
            return lifts;
        }

        /// <summary>Whether a lift from bottom cell c would start a new base: c in the low part of the relief, and no placed bottom
        /// within 400 m belongs to a cluster (400 m chains, as the scorecard's) that already holds a low station.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static bool OpensBase(Grid g, List<Lift> lifts, int c, float lowCut)
        {
            float2 p = g.World(c);
            if (RawH(g, p) > lowCut)
                return false;
            var root = new int[lifts.Count];
            for (int i = 0; i < root.Length; i++)
                root[i] = i;
            for (int a = 0; a < lifts.Count; a++)
                for (int b = 0; b < a; b++)
                    if (math.distance(lifts[a].Bottom.xz, lifts[b].Bottom.xz) < 400f)
                        root[Root(root, a)] = Root(root, b);
            var lowRoots = new HashSet<int>();
            for (int L = 0; L < lifts.Count; L++)
                if (RawH(g, lifts[L].Bottom.xz) <= lowCut)
                    lowRoots.Add(Root(root, L));
            for (int L = 0; L < lifts.Count; L++)
                if (math.distance(lifts[L].Bottom.xz, p) < 400f && lowRoots.Contains(Root(root, L)))
                    return false;
            return true;
        }

        /// <summary>The flattest cell (raw 10 m grade) within r meters of c that a station pad may sit on, nearer cells first on a
        /// tie. Returns -1 when none is at or under StationMax.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static int FlatCell(Grid g, int c, float r)
        {
            int k = (int)Math.Ceiling(r / g.CellM), x0 = c % g.W, z0 = c / g.W, best = -1;
            float bestKey = float.MaxValue;
            for (int dz = -k; dz <= k; dz++)
                for (int dx = -k; dx <= k; dx++)
                {
                    int x = x0 + dx, z = z0 + dz;
                    if (x < 0 || z < 0 || x >= g.W || z >= g.D || dx * dx + dz * dz > k * k)
                        continue;
                    int cc = z * g.W + x;
                    if (!g.In[cc])
                        continue;
                    float gr = RawGrade(g, g.World(cc));
                    float key = gr + 0.004f * (dx * dx + dz * dz);
                    if (gr <= StationMax && key < bestKey)
                    {
                        bestKey = key;
                        best = cc;
                    }
                }
            return best;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        /// <summary>A lift line along the region edge: over half of its chord's middle within EdgeLiftM of ground outside the region (the
        /// low ground a station wants often lies on the edge of a cut-out region, and a designer keeps the line inside it).</summary>
        private static bool AlongEdge(Grid g, int b, int t)
        {
            if (EdgeLiftM <= 0f)
                return false;
            float2 p0 = g.World(b), p1 = g.World(t);
            int n = 0, near = 0;
            for (float f = 0.2f; f <= 0.801f; f += 0.05f, n++)
            {
                float2 q = math.lerp(p0, p1, f);
                for (int k = 0; k < 16; k++)
                {
                    float a = k * (float)Math.PI / 8f;
                    int c = CellOf(g, q + EdgeLiftM * new float2((float)Math.Cos(a), (float)Math.Sin(a)));
                    if (c < 0 || !g.In[c])
                    {
                        near++;
                        break;
                    }
                }
            }
            return near * 2 > n;
        }
        private static readonly float EdgeLiftM = Env("RF_EDGELIFT", 45f);

        private static bool Aerial(string type) => type == "gondola" || type == "tram";

        // scorecard v4's parallel lines are twins too (RF_PARALLELOK=1: allowed), past the lines a new one may run beside
        private static readonly bool NoParallel = Environment.GetEnvironmentVariable("RF_PARALLELOK") != "1";
        private static readonly int TransferBeside = (int)Env("RF_TRANSFERBESIDE", 1f);

        // a lift of the search whose bottom finds no pad of its own is not placed, up to this many times a region (RF_OWNBOTTOM=0: off)
        private static readonly bool OwnBottom = Environment.GetEnvironmentVariable("RF_OWNBOTTOM") != "0";
        private static readonly int OwnBottomTries = (int)Env("RF_OWNBOTTOMTRIES", 8f);

        private static bool Twin(Grid g, List<Lift> lifts, int t, int b, float parallelM = 200f, string type = null, int beside = 0)
        {
            int v4 = 0;
            // this check returns true for:
            // - a twin (both ends within TwinM)
            // - a parallel (the middle 60 % within parallelM of the placed chord)
            // - a crossing in plan away from a shared station (a gondola or tram may cross a chair: the owner's rule, pending an in-game check)
            float2 a0 = g.World(b), a1 = g.World(t);
            foreach (Lift lf in lifts)
            {
                float2 b0 = lf.Bottom.xz, b1 = lf.Top.xz;
                if (math.distance(a1, b1) < TwinM && math.distance(a0, b0) < TwinM)
                    return true;
                // scorecard v5.1: the code rejects one lift riding up on another's line (tops together, bottoms apart)
                if (NoStacked && StackedLines(a0, a1, b0, b1))
                    return true;
                bool parallel = true;
                for (float s = 0.2f; s <= 0.8f && parallel; s += 0.1f)
                    parallel = SegDist(math.lerp(a0, a1, s), b0, b1) < parallelM;
                // scorecard v4: side by side up one valley (a designer runs one long lift and branches from its top)
                if (parallel || (NoParallel && ParallelLines(a0, a1, b0, b1) && ++v4 > beside))
                    return true;
                float shared = math.min(math.min(math.distance(a0, b0), math.distance(a0, b1)), math.min(math.distance(a1, b0), math.distance(a1, b1)));
                if (shared > 60f && Cross(a0, a1, b0, b1) && (type == null || Aerial(type) == Aerial(lf.Type)))
                    return true;
            }
            return false;
        }

        /// <summary>A station of the new lift under (within clearM of) a placed lift's rope, or a placed station under the new rope
        /// (away from the stations the two share).</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static bool UnderRope(Grid g, List<Lift> lifts, int t, int b, float clearM = 40f)
        {
            float2 a0 = g.World(b), a1 = g.World(t);
            foreach (Lift lf in lifts)
            {
                float2 b0 = lf.Bottom.xz, b1 = lf.Top.xz;
                foreach (float2 st in new[] { a0, a1 })
                    if (SegDist(st, b0, b1) < clearM && math.distance(st, b0) > 60f && math.distance(st, b1) > 60f)
                        return true;
                foreach (float2 st in new[] { b0, b1 })
                    if (SegDist(st, a0, a1) < clearM && math.distance(st, a0) > 60f && math.distance(st, a1) > 60f)
                        return true;
            }
            return false;
        }

        /// <summary>A chord whose middle 70 % runs within 25 m of picked lines for over 30 % of its samples sits on a run.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static bool OnRuns(Grid g, PointHash runs, int b, int t)
        {
            float2 a = g.World(b), c = g.World(t);
            int n = 0, near = 0;
            for (float s = 0.15f; s <= 0.85f; s += 20f / Math.Max(20f, math.distance(a, c)), n++)
                if (runs.Near(math.lerp(a, c, s), 25f))
                    near++;
            return n > 0 && near > 0.3f * n;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static bool Near(Grid g, List<int> cells, int c, float m)
        {
            foreach (int o in cells)
                if (math.distancesq(g.World(o), g.World(c)) < m * m)
                    return true;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static List<float2> LinePath(Line l)
        {
            var p = new List<float2>(l.Pts.Count + l.Flow.Count);
            foreach (float3 q in l.Pts)
                p.Add(q.xz);
            p.AddRange(l.Flow);
            return p;
        }

        /// <summary>Dijkstra from a lift top over the cells a skier reaches without climbing (+3 %), without a pitch over 35 % and
        /// without a cliff cell, out to 1.5 km. On the raw terrain the path back holds the connector rules (no 30 m pitch over
        /// AccessRawPitch, no rise over AccessRawRise within 55 m). Returns the touched cells (the caller resets dist). Parent points
        /// home.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static List<int> Reach(Grid g, float[] slope, int t, float[] dist, int[] parent)
        {
            var touched = new List<int> { t };
            var pq = new CellHeap();
            dist[t] = 0f;
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
                    // the raw path back: walk up to ~60 m of ancestors
                    bool ok = g.Hr[nb] - g.Hr[c] <= AccessRawRise;
                    float2 pn = g.World(nb);
                    for (int a = c, steps = 0; ok && a >= 0 && steps < 9; a = parent[a], steps++)
                    {
                        float back = d - dist[a] + len;
                        if (back > 60f)
                            break;
                        if (g.Hr[nb] - g.Hr[a] > AccessRawRise)
                            ok = false;
                        else if (back >= 20f && (g.Hr[a] - g.Hr[nb]) / math.max(1f, math.distance(g.World(a), pn)) > AccessRawPitch)
                            ok = false;
                    }
                    if (!ok)
                        continue;
                    if (float.IsPositiveInfinity(dist[nb]))
                        touched.Add(nb);
                    dist[nb] = nd;
                    parent[nb] = c;
                    pq.Enqueue(nb, nd);
                }
            }
            return touched;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static float Uncovered(List<(float lo, float hi)> cov, float lo, float hi)
        {
            // length of [lo, hi] not inside any covered interval (intervals are few: one per lift)
            var cuts = new List<(float, float)>();
            foreach (var (a, b) in cov)
                if (b > lo && a < hi)
                    cuts.Add((Math.Max(a, lo), Math.Min(b, hi)));
            SortStable(cuts, (p, q) => p.Item1.CompareTo(q.Item1));
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
        /// most TowerLiftM above the straight line). Returns null when none fits. Long chairs prefer the detachable.</summary>
        private static readonly float MaxRiseM = Env("RF_MAXRISE", 0f);

        private static string TypeFor(Grid g, int b, int t)
        {
            // the real ground, as the rope sees it: raw heights at the stations and every 10 m along the chord
            float3 a = new float3(g.World(b).x, g.Hr[b], g.World(b).y), c = new float3(g.World(t).x, g.Hr[t], g.World(t).y);
            float len = math.distance(a, c), rise = c.y - a.y, run = math.distance(a.xz, c.xz);
            // optional stacking cap (from the opus-med contest entry): no lift over RF_MAXRISE m of rise. Off by default. At 560 m it
            // halved the served vertical on alpine terrain (Chamonix 97.9 -> 93.9) and cost ft5-r4 4 points. At 700 m ft5-r2 lost 6.6
            if (MaxRiseM > 0f && rise > MaxRiseM)
                return null;
            for (float d = 10f; d < run - 10f; d += 10f)
            {
                float3 p = math.lerp(a, c, d / run);
                if (RawH(g, p.xz) - p.y > TowerLiftM - 2f)
                    return null;
            }
            if (TypeUse == 1)
                return ByUse(len, rise, run);
            foreach (var ty in Types)
                if (ty.Name != "beginner" && len >= ty.L0 && len <= ty.L1 && rise >= ty.R0 && rise <= ty.R1 && rise / run <= ty.Cap)
                    return ty.Name == "fixed-grip" && len > 1000f && len >= 600f ? "detachable" : ty.Name;
            // past the ladder's lengths and rises: no lift length limit (scorecard v3), so the aerial lift that fits the grade
            if (len >= 800f && rise >= 220f)
                return rise / run <= 1.2f ? "gondola" : rise / run <= 2.5f ? "tram" : null;
            if (len >= 800f && rise >= 96f && rise / run <= 1.2f)
                return "gondola";
            return null;
        }

        /// <summary>v5, the type by use, not by rise: chairs to ChairMaxRise (a fixed-grip to 1000 m of length, detachable beyond), a
        /// gondola for a very long line or a grade past a chair's, a tram for a trunk line past TramRise (base to ridge).</summary>
        private static string ByUse(float len, float rise, float run)
        {
            float gr = rise / run;
            if (rise >= 95f && rise <= ChairMaxRise && len <= VeryLongM)
            {
                if (len >= 300f && len <= 1000f && rise <= 512f && gr <= 0.8f)
                    return "fixed-grip";
                if (len >= 600f && gr <= 0.9f)
                    return "detachable";
            }
            if (len < 800f || rise < 96f)
                return null;
            if (rise >= TramRise || gr > 1.2f)
                return rise >= 220f && gr <= 2.5f ? "tram" : null;
            return "gondola";
        }

        /// <summary>v5, the type by use on the finished plan (RF_TYPEUSE=2, the default). The code places the lifts as before (the
        /// ladder by rise sets their value). Then a gondola or tram that is neither the village lift nor a download lift becomes the
        /// chair its line allows. A gondola past TramRise becomes a tram. Both changes happen where no chair line crosses the lift and
        /// the ski graph keeps its links and ways home (a gondola top also takes skiers down). Returns the lifts changed.</summary>
        private static int RetypeByUse(Grid g, Plan plan, List<Lift> lifts, Lift village)
        {
            int changed = 0;
            var log = new List<string>();
            foreach (Lift lf in lifts)
            {
                if (lf.Kind == "beginner" || lf == village || lf.RidesDown)
                    continue;
                float2 b = lf.Bottom.xz, t = lf.Top.xz;
                float rise = RawH(g, t) - RawH(g, b), run = math.distance(b, t), len = math.sqrt(run * run + rise * rise);
                string by = ByUse(len, rise, run);
                if (by == null || by == lf.Type || (!Aerial(by) && !Aerial(lf.Type)))
                    continue;
                if (lifts.Any(o => o != lf && Aerial(o.Type) == Aerial(by) && SegCrossScore(o.Bottom.xz, o.Top.xz, b, t)))
                {
                    log.Add($"L{lf.Id} {lf.Type} stays (a {by} would cross a lift of its class)");
                    continue;
                }
                SkiGraph before = Ski(g, plan, lifts);
                int[] root = V3Bases(g, lifts, out HashSet<int> bases);
                int homeBefore = WayHomeOk(before, lifts, root, bases).Count(x => x);
                string was = lf.Type;
                lf.Type = by;
                SkiGraph after = Ski(g, plan, lifts);
                root = V3Bases(g, lifts, out bases);
                int homeAfter = WayHomeOk(after, lifts, root, bases).Count(x => x);
                if (after.BestSize < before.BestSize || homeAfter < homeBefore)
                {
                    lf.Type = was;
                    log.Add($"L{lf.Id} {was} stays (as a {by}: network {after.BestSize}/{before.BestSize}, ways home {homeAfter}/{homeBefore})");
                    continue;
                }
                log.Add($"L{lf.Id} {was} -> {by} ({len:F0} m / {rise:F0} m up)");
                changed++;
            }
            if (log.Count > 0)
                plan.Log.Add("type by use: " + string.Join("; ", log));
            return changed;
        }

        private static void WriteLifts(string path, string id, List<Lift> lifts, List<Line> lines)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# lifts {id}: lift type length_m rise_m bottom_grade new_vertical_m transfer lines(map#:band) traverses");
            foreach (Lift lf in lifts)
            {
                var ls = new List<string>();
                foreach (Line l in lf.Lines)
                    ls.Add($"{l.Pick}:{l.Band}");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "L{0} {1} {2:F0} {3:F0} {4:F2} {5:F0} {8:F0} [{6}] {7}",
                    lf.Id, lf.Type, lf.Length, lf.Rise, lf.BottomGrade, lf.Gain, string.Join(" ", ls), lf.Traverses.Count, lf.Transfer));
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
            Console.WriteLine($"REPORT rf lifts {id}: {lifts.Count} lift(s) [{string.Join(", ", lifts.ConvertAll(l => $"L{l.Id} {l.Type} {l.Length:F0} m / {l.Rise:F0} m up, {l.Lines.Count} line(s), {l.Traverses.Count} traverse(s){(l.Transfer > 0 ? ", feeds another lift" : "")}"))}]; {unserved} of {picked} line(s) unserved");
        }
    }
}
