// RunsFirst coverage lifts (prototype): the main lift search follows the picked lines, so a face or a lower valley no picked line
// crosses never gets a lift, however much skiable ground it holds. After it, lifts are added by the ground they open: the skiable
// 20 m lattice points a skier gets to from the top (the honest traverse model) and that drain past a lift bottom (the new one or
// any placed one: the skier comes home through the network). Each such lift must be reachable from the placed lifts and lead back
// to them, and must open a real share of the region; the budget leaves room for the beginner pods.

using System;
using System.Collections.Generic;
using System.Numerics;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class RunsFirst
    {
        private static readonly float CoverLiftShare = Env("RF_CLS", 0.035f);   // a coverage lift opens at least this share of the region's skiable ground
        private static readonly int CoverLiftMinPts = (int)Env("RF_CLM", 250f);     // ... and at least this many 20 m lattice points (11 ha)

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

        private static int AddCoverLifts(Grid g, int[] rec, List<Lift> lifts, List<int> tops, List<bool[]> reachB, List<ulong[]> reachBits,
            List<int> bottoms, int[] latIdx, int latCount, PointHash runs, float km2, float[] slope)
        {
            int n = g.W * g.D, B = bottoms.Count, wb = (B + 63) / 64, wl = (latCount + 63) / 64;
            int budget = (int)Math.Ceiling(1.5 * km2) + 2 - 2;   // two left for the beginner pods
            if (lifts.Count == 0 || lifts.Count >= budget || B == 0)
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
                    for (int b = 0; b < B; b++)
                    {
                        int t = tops[ti], bc = bottoms[b];
                        float rise = g.H[t] - g.H[bc];
                        if (rise < 95f || rise > 888f || math.distancesq(g.World(t), g.World(bc)) > 3000f * 3000f)
                            continue;
                        bool into = false, home = back;
                        foreach (Lift lf in lifts)
                        {
                            into |= reachB[lf.TopIndex][b] || math.distancesq(lf.Bottom.xz, g.World(bc)) < 150f * 150f;
                            home |= (passes[bc * wb + lf.BottomIndex / 64] & (1UL << (lf.BottomIndex % 64))) != 0;
                        }
                        pairs++;
                        if (!into)
                            noInto++;
                        if (!home)
                            noHome++;
                        if (!into || !home)
                            continue;
                        int gain = baseGain;
                        ulong[] cb = catchOf[b];
                        for (int w = 0; w < wl; w++)
                            gain += BitOperations.PopCount(rb[w] & open[w] & cb[w] & ~homes[w]);
                        maxGain = Math.Max(maxGain, gain);
                        if (gain >= Math.Max(CoverLiftMinPts, CoverLiftShare * latCount))
                            cands.Add((gain, ti, b));
                    }
                }
                cands.Sort((a, b) => b.Gain.CompareTo(a.Gain));
                if (debug)
                    Console.WriteLine($"DEBUG cover lifts: {openPts} of {latCount} lattice points unserved; {pairs} pair(s), {noInto} not reachable, {noHome} not leading home; best gain {maxGain}; {cands.Count} over the bar");
                bool placed = false;
                foreach (var (gain, ti, b) in cands)
                {
                    int t = tops[ti], bc = bottoms[b];
                    if (Twin(g, lifts, t, bc) || OnRuns(g, runs, bc, t))
                        continue;
                    string type = TypeFor(g, bc, t);
                    if (type == null)
                        continue;
                    var lift = new Lift
                    {
                        Id = lifts.Count + 1, Type = type, TopCell = t, BottomCell = bc, TopIndex = ti, BottomIndex = b,
                        Top = new float3(g.World(t).x, g.H[t], g.World(t).y), Bottom = new float3(g.World(bc).x, g.H[bc], g.World(bc).y),
                        BottomGrade = slope[bc],
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
                    break;
            }
            return added;
        }
    }
}
