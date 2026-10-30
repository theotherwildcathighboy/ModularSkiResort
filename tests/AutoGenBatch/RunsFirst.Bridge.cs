// RunsFirst bridge lifts (prototype): where the lift network stays split after the link routes, the cut-off area can lie over a
// saddle above the place the other lifts' skiers collect (a hub in a basin below the pass into the next valley). No run or cat
// track links the two; a short lift does, as a resort's transfer chair: from where the skiers collect up onto ground a run comes
// down from into the cut-off area (or onto the area's own runs, when they start within reach of its top).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class RunsFirst
    {
        private const float BridgeMinM = 250f, BridgeMaxM = 1500f;   // bridge chord in plan
        private const float BridgeStartM = 140f;                     // an element starting this close to the top is skied from it

        /// <summary>Adds bridge lifts while the network is split and the lift budget allows; returns how many.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static int BridgeLifts(Grid g, Plan plan, List<Lift> lifts, float km2)
        {
            int added = 0, budget = (int)Math.Ceiling(1.5 * km2) + 2;
            float hLo = float.MaxValue, hHi = float.MinValue;
            for (int c = 0; c < g.W * g.D; c++)
                if (g.In[c] && !float.IsNaN(g.Hr[c]))
                {
                    hLo = Math.Min(hLo, g.Hr[c]);
                    hHi = Math.Max(hHi, g.Hr[c]);
                }
            float lowCut = hLo + BaseShareCut * (hHi - hLo);
            var tried = new HashSet<(int, int)>();
            for (int round = 0; round < 3 && lifts.Count < budget; round++)
            {
                SkiGraph sg = Ski(g, plan, lifts);
                if (sg.BestSize >= lifts.Count)
                    break;
                var fwd = new HashSet<int>[lifts.Count];
                for (int L = 0; L < lifts.Count; L++)
                    fwd[L] = Closure(L, sg.Reaches);
                var main = Enumerable.Range(0, lifts.Count).Where(L => sg.Component[L] == sg.BestComponent).ToList();
                bool any = false;
                foreach (int Y in Enumerable.Range(0, lifts.Count).Where(L => sg.Component[L] != sg.BestComponent))
                {
                    // into Y from the main lifts, then out of Y back to them
                    if (!main.Any(X => fwd[X].Contains(Y)) && tried.Add((-1, lifts[Y].Id)))
                        any = TryBridge(g, plan, lifts, sg, main, new List<int> { Y }, lowCut);
                    else if (!main.Any(X => fwd[Y].Contains(X)) && tried.Add((lifts[Y].Id, -1)))
                        any = TryBridge(g, plan, lifts, sg, new List<int> { Y }, main, lowCut);
                    if (any)
                        break;
                }
                if (!any)
                    break;
                added++;
            }
            return added;
        }

        /// <summary>The shortest lift from where the froms' skiers are up to ground the tos' bottom stations can be skied to from: a
        /// pad beside what the froms ski (no new base), a top whose run (an existing one starting near it, else a route found uphill
        /// from the tos) comes down to the tos; the lift norms, no crossing, its rope clear of other ropes and its chord off the runs.
        /// Kept only when the network grows.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static bool TryBridge(Grid g, Plan plan, List<Lift> lifts, SkiGraph sg, List<int> froms, List<int> tos, float lowCut)
        {
            string names = string.Join("/", froms.Select(L => "L" + lifts[L].Id)) + " -> " + string.Join("/", tos.Select(L => "L" + lifts[L].Id));
            // bottom pads: beside what the froms ski (one per 60 m bucket, its lowest point), the element passing the station
            var low = new Dictionary<long, float3>();
            static long Key(float2 p, float s) => ((long)Math.Floor(p.x / s) << 32) ^ (uint)(int)Math.Floor(p.y / s);
            foreach (int i in froms.SelectMany(L => sg.Skied[L]).Distinct())
                foreach (float3 p in sg.P[i].Skip(1))
                {
                    long k = Key(p.xz, 60f);
                    if (!low.TryGetValue(k, out float3 q) || p.y < q.y)
                        low[k] = p;
                }
            var bots = new List<int>();
            foreach (float3 p in low.Values)
            {
                int c = CellOf(g, p.xz);
                if (c < 0 || !g.In[c])
                    continue;
                int pad = FlatCell(g, c, 28f);
                if (pad >= 0 && !bots.Contains(pad) && RawH(g, g.World(pad)) <= p.y + 8f && !OpensBase(g, lifts, pad, lowCut))
                    bots.Add(pad);
            }
            if (bots.Count == 0)
            {
                plan.Log.Add($"bridge {names}: no station pad beside the runs");
                return false;
            }
            // the elements a skier gets to the tos from (their starts can be skied from a nearby top)
            var toSet = new HashSet<int>(tos);
            int m = sg.P.Count;
            var toEls = new bool[m];
            for (bool changed = true; changed; )
            {
                changed = false;
                for (int i = 0; i < m; i++)
                    if (!toEls[i] && (sg.Delivers[i].Any(toSet.Contains) || sg.Next[i].Any(j => toEls[j])))
                        toEls[i] = changed = true;
            }
            // the tos' catchment as a run comes down it: uphill from their stations and from the served runs that deliver there
            var origins = tos.Select(Z => lifts[Z].BottomCell).ToList();
            for (int i = 0; i < sg.Runs; i++)
                if (sg.Reached[i] && sg.Returns[i] && toEls[i])
                    for (int k = 0; k < sg.P[i].Length; k += 3)
                    {
                        int c = CellOf(g, sg.P[i][k].xz);
                        if (c >= 0 && g.In[c] && !float.IsNaN(g.Hr[c]))
                            origins.Add(c);
                    }
            origins = origins.Distinct().ToList();
            var avoid = new PointHash();
            foreach (PlanRun r in plan.Runs)
                foreach (float2 p in r.Pts)
                    avoid.Add(p);
            float botMin = bots.Min(c => g.Hr[c]);
            // runs that would lie along a lift line (the plan's runs in, as the serve pass left them)
            var lines = new ChordGuard(lifts);
            foreach (PlanRun r in plan.Runs)
                lines.Add(r.Pts, 0, r.Pts.Count - 1);
            int checks = 0, skis = 0;
            foreach (EasyLimits lim in new[] { BlueWay, BlackWay })
            {
                EasyReach er = EasyUp(g, origins, avoid, lim);
                // top pads on a 21 m lattice of the catchment, high enough over some bottom pad
                var tops = new List<(int T, int X)>();
                var seenT = new HashSet<int>();
                foreach (int x in er.Cost.Keys)
                {
                    if ((x % g.W) % 3 != 0 || (x / g.W) % 3 != 0 || g.Hr[x] < botMin + 95f)
                        continue;
                    int t = FlatCell(g, x, 20f);
                    if (t >= 0 && seenT.Add(t))
                        tops.Add((t, x));
                }
                // a pair with a station on another lift's station (bottom on bottom, top on top) comes after every other: each lift
                // its own terminal (scorecard v4), though the spread may still give it one where no other pair fits
                var pairs = new List<(float Cost, int B, int Ti)>();
                for (int ti = 0; ti < tops.Count; ti++)
                {
                    float2 tw = g.World(tops[ti].T);
                    float th = g.Hr[tops[ti].T];
                    bool topShared = lifts.Exists(lf => math.distance(lf.Top.xz, tw) < LaySameSpotM + 2f);
                    foreach (int b in bots)
                    {
                        float2 bw = g.World(b);
                        float d = math.distance(bw, tw), rise = th - g.Hr[b];
                        if (d < BridgeMinM || d > BridgeMaxM || rise < 95f || rise > 512f || rise / d > 0.75f)
                            continue;
                        bool shared = topShared || lifts.Exists(lf => math.distance(lf.Bottom.xz, bw) < LaySameSpotM + 2f);
                        pairs.Add((math.sqrt(d * d + rise * rise) + 0.5f * er.Len[tops[ti].X] + (shared ? 1e6f : 0f), b, ti));
                    }
                }
                pairs.Sort((p, q) => p.Cost.CompareTo(q.Cost));
                foreach (var (_, b, ti) in pairs)
                {
                    if (checks >= 4000 || skis >= 6)
                        break;
                    int t = tops[ti].T, x = tops[ti].X;
                    float2 bw = g.World(b), tw = g.World(t);
                    // a pair across a placed lift is cheap to pass over and does not count against the checks (where the cut-off
                    // area lies beyond other lifts, the shortest few thousand pairs all cross one)
                    if (lifts.Any(lf => SegCrossScore(lf.Bottom.xz, lf.Top.xz, bw, tw) || (NoParallel && ParallelLines(bw, tw, lf.Bottom.xz, lf.Top.xz))
                        || (NoStacked && StackedLines(bw, tw, lf.Bottom.xz, lf.Top.xz))))
                        continue;
                    checks++;
                    if (UnderRope(g, lifts, t, b))
                        continue;
                    float plan2 = math.distance(bw, tw);
                    bool apart = true;
                    for (float f = 0.15f; f <= 0.85f && apart; f += 20f / plan2)
                    {
                        float2 q = math.lerp(bw, tw, f);
                        apart = lifts.All(lf => SegDist(q, lf.Bottom.xz, lf.Top.xz) >= 40f);
                    }
                    string type = apart ? TypeFor(g, b, t) : null;
                    if (type == null)
                        continue;
                    // the run from the top: an element to the tos starting near it, else the route down the catchment
                    float th = RawH(g, tw);
                    bool direct = Enumerable.Range(0, m).Any(i => toEls[i] && math.distancesq(sg.P[i][0].xz, tw) < BridgeStartM * BridgeStartM && sg.P[i][0].y <= th + 10f);
                    List<float2> run = null;
                    if (!direct)
                    {
                        List<int> cells = er.Route(x);
                        if (cells.Count < 3)
                            continue;
                        var pts = new List<float2> { tw };
                        pts.AddRange(Polyline(g, cells, false));
                        run = SanitizeRun(g, pts);
                        if (run == null || run.Count < Densify(pts).Count || RawH(g, run[0]) - RawH(g, run[run.Count - 1]) < MinRunDropM)
                            continue;
                    }
                    // the chord's middle off the runs (the new one too: the guard's 25 % within 23 m), the new run off every lift line
                    int n = 0, near = 0;
                    for (float f = 0.15f; f <= 0.85f; f += 10f / plan2, n++)
                    {
                        float2 q = math.lerp(bw, tw, f);
                        if (avoid.Near(q, 23f) || (run != null && run.Any(rp => math.distancesq(rp, q) < 23f * 23f)))
                            near++;
                    }
                    if (near > 0.25f * n || (run != null && !lines.FitsAdding(run, 0, run.Count - 1)))
                        continue;
                    var lift = new Lift
                    {
                        Id = lifts.Count + 1, Type = type, TopCell = t, BottomCell = b, Kind = "bridge",
                        Top = new float3(tw.x, g.H[t], tw.y), Bottom = new float3(bw.x, g.H[b], bw.y),
                    };
                    lift.Rise = lift.Top.y - lift.Bottom.y;
                    lift.Length = math.distance(lift.Top, lift.Bottom);
                    lifts.Add(lift);
                    if (run != null)
                        plan.Runs.Add(new PlanRun { Pts = run, Band = RunBand(g, run), By = lift, Kind = "bridge" });
                    skis++;
                    SkiGraph after = Ski(g, plan, lifts);
                    if (after.BestSize > sg.BestSize)
                    {
                        plan.Log.Add($"bridge {names}: {type} {lift.Length:F0} m / {RawH(g, tw) - RawH(g, bw):F0} m up, " +
                            (run == null ? "onto the runs starting at its top" : $"{RunBand(g, run)} run {RunLength(run):F0} m down"));
                        return true;
                    }
                    lifts.RemoveAt(lifts.Count - 1);
                    if (run != null)
                        plan.Runs.RemoveAt(plan.Runs.Count - 1);
                }
            }
            plan.Log.Add($"bridge {names}: none fits ({bots.Count} bottom pad(s), {checks} pair(s) checked)");
            return false;
        }
    }
}
