// RunsFirst coverage lifts (prototype): the main lift search follows the picked lines, so a face or a lower valley no picked line
// crosses never gets a lift, however much skiable ground it holds. After it, lifts are added by the ground they open: the skiable
// 20 m lattice points a skier gets to from the top (the honest traverse model) and that drain past a lift bottom (the new one or
// any placed one: the skier comes home through the network). Each such lift must be reachable from the placed lifts and lead back
// to them, and must open a real share of the region; the budget leaves room for the beginner pods.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class RunsFirst
    {
        private static readonly bool NoNewBase = Environment.GetEnvironmentVariable("RF_CNEWBASE") != "1";
        private static readonly float CoverLiftShare = Env("RF_CLS", 0.02f);   // a coverage lift opens at least this share of the region's skiable ground
        private static readonly int CoverLiftMinPts = (int)Env("RF_CLM", 120f);     // ... and at least this many 20 m lattice points (11 ha)
        private static readonly float CoverParallelM = Env("RF_CPAR", 170f);       // ... and no closer than this alongside a placed lift
        private static readonly float CoverClearM = Env("RF_CCLR", 100f);          // ... its stations no closer than this to another rope

        /// <summary>The skiable 20 m lattice: per cell its index (-1 = not a lattice point or not skiable).</summary>
        private static int[] SkiLattice(Grid g, out int count)
        {
            var idx = new int[g.W * g.D];
            Array.Fill(idx, -1);
            int s = Math.Max(1, (int)Math.Round(20f / g.CellM));
            count = 0;
            for (int z = s / 2; z < g.D; z += s)
                for (int x = s / 2; x < g.W; x += s)
                {
                    int c = z * g.W + x;
                    if (!g.In[c])
                        continue;
                    float gr = RawGrade(g, g.World(c));
                    if (gr >= 0.06f && gr <= 0.85f)
                        idx[c] = count++;
                }
            return idx;
        }

        /// <summary>The 20 m lattice as a drainage graph: each lattice position's next position down its fall line, highest first;
        /// Below(cells) is the skiable lattice a skier gets to from those cells by skiing on down the fall lines.</summary>
        private sealed class LatticeFlow
        {
            private readonly Grid g;
            private readonly int s, pw, pd;
            private readonly int[] next, order, ski;
            public LatticeFlow(Grid grid, int[] rec, int[] latIdx)
            {
                g = grid;
                s = Math.Max(1, (int)Math.Round(20f / g.CellM));
                pw = (g.W - s / 2 + s - 1) / s;
                pd = (g.D - s / 2 + s - 1) / s;
                int m = pw * pd;
                next = new int[m];
                ski = new int[m];
                var h = new float[m];
                for (int pz = 0; pz < pd; pz++)
                    for (int px = 0; px < pw; px++)
                    {
                        int pi = pz * pw + px, c = (pz * s + s / 2) * g.W + px * s + s / 2;
                        next[pi] = -1;
                        ski[pi] = latIdx[c];
                        h[pi] = g.In[c] ? -g.H[c] : float.MaxValue;
                        if (!g.In[c])
                            continue;
                        for (int k = 0, at = c; k < 6 * s && (at = rec[at]) >= 0; k++)
                        {
                            int q = Pos(at);
                            if (q != pi)
                            {
                                next[pi] = q;
                                break;
                            }
                        }
                    }
                order = new int[m];
                for (int i = 0; i < m; i++)
                    order[i] = i;
                Array.Sort(h, order);
            }
            private int Pos(int c)
            {
                int x = c % g.W, z = c / g.W;
                int px = Math.Clamp((int)Math.Round((x - s / 2) / (double)s), 0, pw - 1), pz = Math.Clamp((int)Math.Round((z - s / 2) / (double)s), 0, pd - 1);
                return pz * pw + px;
            }
            public bool[] NewMark() => new bool[next.Length];
            /// <summary>The skiable lattice points on or below the cells, marked in the caller's buffer (one per thread).</summary>
            public ulong[] Below(List<int> cells, int latCount, bool[] mark)
            {
                Array.Clear(mark, 0, mark.Length);
                foreach (int c in cells)
                    mark[Pos(c)] = true;
                foreach (int pi in order)
                    if (mark[pi] && next[pi] >= 0)
                        mark[next[pi]] = true;
                var bits = new ulong[(latCount + 63) / 64];
                for (int pi = 0; pi < mark.Length; pi++)
                    if (mark[pi] && ski[pi] >= 0)
                        bits[ski[pi] / 64] |= 1UL << (ski[pi] % 64);
                return bits;
            }
        }

        private static int AddCoverLifts(Grid g, int[] rec, List<Lift> lifts, List<int> tops, List<bool[]> reachB, List<ulong[]> reachBits,
            List<int> bottoms, int[] latIdx, int latCount, PointHash runs, float km2, float[] slope)
        {
            int n = g.W * g.D, B = bottoms.Count, wb = (B + 63) / 64, wl = (latCount + 63) / 64;
            float cellKm2 = (float)Math.Pow(Math.Max(1, (int)Math.Round(20f / g.CellM)) * g.CellM, 2) / 1e6f;   // ground per lattice point
            int budget = (int)Math.Ceiling(1.5 * km2) + 2 - 2;   // two left for the beginner pods
            float hLo = float.MaxValue, hHi = float.MinValue;
            for (int c = 0; c < g.W * g.D; c++)
                if (g.In[c] && !float.IsNaN(g.Hr[c]))
                {
                    hLo = Math.Min(hLo, g.Hr[c]);
                    hHi = Math.Max(hHi, g.Hr[c]);
                }
            float lowCut = hLo + BaseShareCut * (hHi - hLo);
            var greenRise = new Dictionary<int, float>();
            if (lifts.Count == 0 || lifts.Count >= budget || B == 0)
                return 0;
            foreach (Lift lf in lifts)
                lf.TopIndex = tops.IndexOf(lf.TopCell);   // from here on the index of the top in this (wider) list
            if (lifts.Exists(lf => lf.TopIndex < 0))
                return 0;

            // per cell, the bottoms its drainage passes (within ReturnM, from no more than 15 m below the station): lowest cells first
            var near = new Dictionary<long, List<int>>();
            static long Key(float2 p) => ((long)Math.Floor(p.x / 150f) << 32) ^ (uint)(int)Math.Floor(p.y / 150f);
            for (int b = 0; b < B; b++)
            {
                long k = Key(g.World(bottoms[b]));
                if (!near.TryGetValue(k, out List<int> l))
                    near[k] = l = new List<int>();
                l.Add(b);
            }
            var order = new int[n];
            var hs = new float[n];
            for (int c = 0; c < n; c++)
            {
                order[c] = c;
                hs[c] = g.In[c] ? g.H[c] : float.MaxValue;
            }
            Array.Sort(hs, order);
            var passes = new ulong[n * wb];
            foreach (int c in order)
            {
                if (!g.In[c])
                    break;
                float2 p = g.World(c);
                int cx = (int)Math.Floor(p.x / 150f), cz = (int)Math.Floor(p.y / 150f);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (near.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out List<int> l))
                            foreach (int b in l)
                                if (math.distancesq(g.World(bottoms[b]), p) < ReturnM * ReturnM && g.H[c] >= g.H[bottoms[b]] - 15f)
                                    passes[c * wb + b / 64] |= 1UL << (b % 64);
                int r = rec[c];
                if (r >= 0 && r != c)
                    for (int w = 0; w < wb; w++)
                        passes[c * wb + w] |= passes[r * wb + w];
            }
            // per bottom, the lattice points that drain past it
            var catchOf = new ulong[B][];
            for (int b = 0; b < B; b++)
                catchOf[b] = new ulong[wl];
            for (int c = 0; c < n; c++)
            {
                int i = latIdx[c];
                if (i < 0)
                    continue;
                for (int w = 0; w < wb; w++)
                    for (ulong m = passes[c * wb + w]; m != 0; m &= m - 1)
                        catchOf[w * 64 + BitOperations.TrailingZeroCount(m)][i / 64] |= 1UL << (i % 64);
            }

            // what the placed lifts already serve: their tops' reach that drains past any placed bottom
            var homes = new ulong[wl];
            var reached = new ulong[wl];
            foreach (Lift lf in lifts)
                for (int w = 0; w < wl; w++)
                {
                    homes[w] |= catchOf[lf.BottomIndex][w];
                    reached[w] |= reachBits[lf.TopIndex][w];
                }
            int added = 0;
            bool debug = Environment.GetEnvironmentVariable("RF_DEBUG") == "1";
            // a station skiers get to: a skiable lattice point within 30 m lies below a placed top
            bool BelowPlaced(int c)
            {
                int x0 = c % g.W, z0 = c / g.W, k = (int)Math.Ceiling(30f / g.CellM);
                for (int dz = -k; dz <= k; dz++)
                    for (int dx = -k; dx <= k; dx++)
                    {
                        int x = x0 + dx, z = z0 + dz;
                        if (x < 0 || z < 0 || x >= g.W || z >= g.D)
                            continue;
                        int i = latIdx[z * g.W + x];
                        if (i >= 0 && (reached[i / 64] & (1UL << (i % 64))) != 0)
                            return true;
                    }
                return false;
            }
            if (debug)
            {
                int hc = 0, rc = 0;
                for (int w = 0; w < wl; w++)
                {
                    hc += BitOperations.PopCount(homes[w]);
                    rc += BitOperations.PopCount(reached[w]);
                }
                Console.WriteLine($"DEBUG cover lifts: {latCount} skiable lattice points, {rc} below a placed top, {hc} draining past a placed bottom; {B} bottom candidate(s), {tops.Count} top(s)");
            }
            while (lifts.Count < budget)
            {
                var open = new ulong[wl];
                for (int w = 0; w < wl; w++)
                    open[w] = ~(reached[w] & homes[w]);
                if (latCount % 64 != 0)
                    open[wl - 1] &= (1UL << (latCount % 64)) - 1;
                var cands = new List<(int Gain, int T, int B)>();
                int maxGain = 0, pairs = 0, noInto = 0, noHome = 0, openPts = 0;
                for (int w = 0; w < wl; w++)
                    openPts += BitOperations.PopCount(open[w]);
                for (int ti = 0; ti < tops.Count; ti++)
                {
                    // the top must lead back to the placed lifts (its reach takes in a placed bottom) or drain past one
                    bool back = false;
                    foreach (Lift lf in lifts)
                        back |= reachB[ti][lf.BottomIndex];
                    ulong[] rb = reachBits[ti];
                    int baseGain = 0;
                    for (int w = 0; w < wl; w++)
                        baseGain += BitOperations.PopCount(rb[w] & open[w] & homes[w]);
                    int tBest = 0, tInto = 0, tHome = 0, tPairs = 0;
                    for (int b = 0; b < B; b++)
                    {
                        int t = tops[ti], bc = bottoms[b];
                        float rise = g.H[t] - g.H[bc];
                        if (rise < 95f || rise > 888f || math.distancesq(g.World(t), g.World(bc)) > 3000f * 3000f)
                            continue;
                        // skiers get to the station down the fall lines from a placed top
                        bool into = BelowPlaced(bc), home = back;
                        foreach (Lift lf in lifts)
                        {
                            home |= (passes[bc * wb + lf.BottomIndex / 64] & (1UL << (lf.BottomIndex % 64))) != 0;
                        }
                        pairs++;
                        tPairs++;
                        if (!into)
                            noInto++;
                        if (!home)
                            noHome++;
                        if (into)
                            tInto++;
                        if (home)
                            tHome++;
                        if (!into || !home)
                            continue;
                        int gain = baseGain;
                        ulong[] cb = catchOf[b];
                        for (int w = 0; w < wl; w++)
                            gain += BitOperations.PopCount(rb[w] & open[w] & cb[w] & ~homes[w]);
                        maxGain = Math.Max(maxGain, gain);
                        tBest = Math.Max(tBest, gain);
                        if (gain >= Math.Max(CoverLiftMinPts, CoverLiftShare * latCount))
                            cands.Add((gain, ti, b));
                    }
                    if (debug && Environment.GetEnvironmentVariable("RF_TOPDBG") == "1" && baseGain >= 40)
                        Console.WriteLine($"DEBUG cover top {g.World(tops[ti]).x:F0},{g.World(tops[ti]).y:F0} h {g.Hr[tops[ti]]:F0}: base gain {baseGain}, back {back}, best {tBest}, pairs {tPairs}, into {tInto}, home {tHome}");
                }
                cands.Sort((a, b) => b.Gain.CompareTo(a.Gain));
                if (debug && Environment.GetEnvironmentVariable("RF_TOPDBG") == "1")
                    Console.WriteLine("DEBUG cover top lines above");
                if (debug)
                    Console.WriteLine($"DEBUG cover lifts: {openPts} of {latCount} lattice points unserved; {pairs} pair(s), {noInto} not reachable, {noHome} not leading home; best gain {maxGain}; {cands.Count} over the bar");
                bool placed = false;
                var rej = new int[5];
                foreach (var (gain, ti, b) in cands)
                {
                    int t = tops[ti], bc = bottoms[b];
                    if (Twin(g, lifts, t, bc, CoverParallelM))
                    {
                        if (debug && Environment.GetEnvironmentVariable("RF_TWINDBG") == "1")
                        {
                            float2 a0 = g.World(bc), a1 = g.World(t);
                            var why = new List<string>();
                            foreach (Lift lf in lifts)
                            {
                                float2 b0 = lf.Bottom.xz, b1 = lf.Top.xz;
                                if (math.distance(a1, b1) < TwinM && math.distance(a0, b0) < TwinM)
                                    why.Add($"twin L{lf.Id}");
                                bool par = true;
                                for (float s2 = 0.2f; s2 <= 0.8f && par; s2 += 0.1f)
                                    par = SegDist(math.lerp(a0, a1, s2), b0, b1) < 200f;
                                if (par)
                                    why.Add($"parallel L{lf.Id}");
                                float sh = math.min(math.min(math.distance(a0, b0), math.distance(a0, b1)), math.min(math.distance(a1, b0), math.distance(a1, b1)));
                                if (sh > 60f && Cross(a0, a1, b0, b1))
                                    why.Add($"cross L{lf.Id}");
                            }
                            Console.WriteLine($"DEBUG twin: gain {gain} top {a1.x:F0},{a1.y:F0} h {g.Hr[t]:F0} bottom {a0.x:F0},{a0.y:F0} h {g.Hr[bc]:F0}: {string.Join(", ", why)}");
                        }
                        rej[0]++;
                        continue;
                    }
                    if (UnderRope(g, lifts, t, bc, CoverClearM))
                    {
                        rej[1]++;
                        continue;
                    }
                    if (OnRuns(g, runs, bc, t))
                    {
                        rej[2]++;
                        continue;
                    }
                    // a coverage lift may start a base of its own only where green ground rises a pod's height from its station (the
                    // beginner pass needs it there)
                    if (NoNewBase && OpensBase(g, lifts, bc, lowCut))
                    {
                        if (!greenRise.TryGetValue(bc, out float gr))
                        {
                            EasyReach er = EasyUp(g, new List<int> { bc }, new PointHash(), GreenProbe);
                            greenRise[bc] = gr = er.Cost.Count > 0 ? er.Cost.Keys.Max(c => g.Hr[c]) - g.Hr[bc] : 0f;
                        }
                        if (gr < PodMinRise)
                        {
                            rej[4]++;
                            continue;
                        }
                    }
                    string type = TypeFor(g, bc, t);
                    if (type == null)
                    {
                        rej[3]++;
                        continue;
                    }
                    var lift = new Lift
                    {
                        Id = lifts.Count + 1, Type = type, TopCell = t, BottomCell = bc, TopIndex = ti, BottomIndex = b,
                        Top = new float3(g.World(t).x, g.H[t], g.World(t).y), Bottom = new float3(g.World(bc).x, g.H[bc], g.World(bc).y),
                        BottomGrade = slope[bc],
                        OpensKm2 = gain * cellKm2,
                    };
                    lift.Rise = lift.Top.y - lift.Bottom.y;
                    lift.Length = math.distance(lift.Top, lift.Bottom);
                    lifts.Add(lift);
                    for (int w = 0; w < wl; w++)
                    {
                        homes[w] |= catchOf[b][w];
                        reached[w] |= reachBits[ti][w];
                    }
                    if (debug)
                        Console.WriteLine($"DEBUG cover lift L{lift.Id}: {type} {lift.Length:F0} m / {lift.Rise:F0} m up opens {gain} of {latCount} lattice points");
                    added++;
                    placed = true;
                    break;
                }
                if (!placed)
                {
                    if (debug)
                        Console.WriteLine($"DEBUG cover lifts: none placed; twin {rej[0]}, under a rope {rej[1]}, on runs {rej[2]}, no lift type fits {rej[3]}, opens a base {rej[4]}");
                    break;
                }
            }
            return added;
        }
    }
}
