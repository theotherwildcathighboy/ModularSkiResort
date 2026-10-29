// RunsFirst beginner terrain (prototype): every real base has a learning zone. For each base (lift bottoms in the low third of the
// region) with no green way home, the planner searches uphill from the base: from its stations and from flat spots on the last
// 400 m of the runs that come home to it, over ground an easy run can wind down (raw 30 m pitches green but for a few short
// stretches, no climb a run may not make). If that ground reaches the top of a lift of the base, the winding route is the green
// way down from it. Otherwise a short fixed-grip chair (the mod has no surface lifts; its norms: 300-1500 m, 95-512 m rise) goes
// from a pad the route ends at to the cheapest point that clears the norms: flat stations, the rope clear of the ground, no
// crossing, neither chord on a run; the winding route is its green run. A pod whose station no run of the base passes would
// split the lift network, so one that keeps it whole wins.

using System;
using System.Collections.Generic;
using System.Linq;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class RunsFirst
    {
        private const float GreenRawPitch = 0.235f;

        /// <summary>What an uphill route search allows on the way down: the smoothed step descent, the raw ~30 m pitch that counts as
        /// steep and the one never allowed, the climb over any point below (meters), the steep share of the length, the length.</summary>
        private readonly struct EasyLimits
        {
            public readonly float EdgeMax, SteepPitch, HardPitch, Climb, SteepShare, MaxLen;

            public EasyLimits(float edgeMax, float steepPitch, float hardPitch, float climb, float steepShare, float maxLen)
            {
                EdgeMax = edgeMax;
                SteepPitch = steepPitch;
                HardPitch = hardPitch;
                Climb = climb;
                SteepShare = steepShare;
                MaxLen = maxLen;
            }
        }

        // a green run (85th percentile under 0.25, the run rule's 10 m climb less a margin); a cat track (the connector rules: no
        // 30 m pitch over 0.50, no rise over 4.5 m); a blue run; a black run (an expert way into the next area)
        private static readonly EasyLimits GreenWay = new EasyLimits(0.22f, GreenRawPitch, 0.40f, 7f, 0.08f, 4500f);
        private static readonly EasyLimits CatTrack = new EasyLimits(0.30f, ConnPitchMax, ConnPitchMax, 3.5f, 1f, 1500f);
        private static readonly EasyLimits BlueWay = new EasyLimits(0.36f, 0.38f, 0.62f, 7f, 0.08f, 3500f);
        private static readonly EasyLimits BlackWay = new EasyLimits(0.55f, 0.58f, 0.95f, 7f, 0.10f, 3500f);
        private const float PodRouteMaxM = 2600f, BaseShareCut = 0.35f, OriginRunM = 400f;
        private const float PodMinRise = 97f, PodMinLength = 320f, PodClearM = 18f;

        /// <summary>The easy-run search tree: Down = the next cell toward the base (-1 at an origin), Root = the origin a cell's
        /// route ends at, Len / Cost / Steep (meters steeper than green) / Hi (highest raw point) along that route.</summary>
        private sealed class EasyReach
        {
            public readonly Dictionary<int, int> Down = new Dictionary<int, int>(), Root = new Dictionary<int, int>();
            public readonly Dictionary<int, float> Len = new Dictionary<int, float>(), Cost = new Dictionary<int, float>();
            public readonly Dictionary<int, float> Steep = new Dictionary<int, float>(), Hi = new Dictionary<int, float>();

            public List<int> Route(int c)
            {
                var cells = new List<int>();
                for (; c >= 0 && cells.Count < 2000; c = Down[c])
                    cells.Add(c);
                return cells;
            }
        }

        private static void AddBeginnerPods(Grid g, Plan plan, List<Lift> lifts)
        {
            if (lifts.Count == 0)
                return;
            float hLo = float.MaxValue, hHi = float.MinValue;
            for (int c = 0; c < g.W * g.D; c++)
                if (g.In[c] && !float.IsNaN(g.Hr[c]))
                {
                    hLo = Math.Min(hLo, g.Hr[c]);
                    hHi = Math.Max(hHi, g.Hr[c]);
                }
            float lowCut = hLo + BaseShareCut * (hHi - hLo);
            int pods = 0, ways = 0, tried = 0;
            var failed = new HashSet<int>();   // bases (by their lowest lift's id) where nothing fits
            for (int pass = 0; pass < 6; pass++)
            {
                // bases as a skier sees them: lift bottoms within 400 m of each other, at least one low on the mountain
                Dictionary<int, int> rootOf = BaseClusters(g, lifts, lowCut, out HashSet<int> bases);
                SkiGraph sg = Ski(g, plan, lifts);
                List<int> members = null;
                int key = -1;
                foreach (int b in bases)
                {
                    var m = rootOf.Where(kv => kv.Value == b).Select(kv => kv.Key).OrderBy(L => RawH(g, lifts[L].Bottom.xz)).ToList();
                    if (!failed.Contains(lifts[m[0]].Id) && !BaseHasGreen(sg, plan, m))
                    {
                        members = m;
                        key = lifts[m[0]].Id;
                        break;
                    }
                }
                if (members == null)
                    break;
                tried++;
                var avoid = new PointHash();
                foreach (PlanRun r in plan.Runs)
                    foreach (float2 p in r.Pts)
                        avoid.Add(p);
                List<int> origins = BaseOrigins(g, plan, lifts, members, sg);
                EasyReach er = EasyUp(g, origins, avoid, GreenWay);
                if (TryWayDown(g, plan, lifts, members, er, avoid))
                    ways++;
                else if (TryPod(g, plan, lifts, members, er, avoid, sg))
                    pods++;
                else
                    failed.Add(key);
            }
            plan.Log.Add($"beginner terrain: {ways} green way(s) down, {pods} pod(s) ({tried} base(s) without a green way home)");
        }

        /// <summary>Lift index -> cluster root (lift bottoms within 400 m chain together, as the scorecard's bases); bases = the
        /// roots of clusters with a bottom under lowCut.</summary>
        private static Dictionary<int, int> BaseClusters(Grid g, List<Lift> lifts, float lowCut, out HashSet<int> bases)
        {
            var p = new int[lifts.Count];
            for (int i = 0; i < p.Length; i++)
                p[i] = i;
            for (int a = 0; a < lifts.Count; a++)
                for (int b = 0; b < a; b++)
                    if (math.distance(lifts[a].Bottom.xz, lifts[b].Bottom.xz) < 400f)
                        p[Root(p, a)] = Root(p, b);
            var o = new Dictionary<int, int>();
            bases = new HashSet<int>();
            for (int L = 0; L < lifts.Count; L++)
            {
                o[L] = Root(p, L);
                if (RawH(g, lifts[L].Bottom.xz) <= lowCut)
                    bases.Add(o[L]);
            }
            return o;
        }

        /// <summary>Where an easy run may end at a base: its lift stations, pads beside them, and flat spots on the last 400 m of the
        /// served runs that come home to it (a green run ending there flows on with that run).</summary>
        private static List<int> BaseOrigins(Grid g, Plan plan, List<Lift> lifts, List<int> members, SkiGraph sg)
        {
            var o = new List<int>();
            void Add(int c)
            {
                if (c >= 0 && g.In[c] && !float.IsNaN(g.Hr[c]) && !o.Any(x => math.distancesq(g.World(x), g.World(c)) < 35f * 35f))
                    o.Add(c);
            }
            foreach (int L in members)
            {
                Add(lifts[L].BottomCell);
                foreach (int c in PadsNear(g, lifts[L].BottomCell, 25f, 45f, 8))
                    Add(c);
            }
            var home = new HashSet<int>(members);
            for (int i = 0; i < sg.Runs && o.Count < 48; i++)
            {
                if (!sg.Reached[i] || !sg.Returns[i] || !sg.Delivers[i].Any(home.Contains))
                    continue;
                List<float2> pts = plan.Runs[i].Pts;
                float along = 0f;
                for (int k = pts.Count - 1; k > 0 && along < OriginRunM && o.Count < 48; k--)
                {
                    along += math.distance(pts[k], pts[k - 1]);
                    int c = CellOf(g, pts[k]);
                    if (k % 3 == 0 && c >= 0 && g.In[c] && RawGrade(g, g.World(c)) <= StationMax)
                        Add(c);
                }
            }
            return o;
        }

        /// <summary>Up to count station pads (raw grade under StationMax) between rMin and rMax from c, spread around it.</summary>
        private static List<int> PadsNear(Grid g, int c, float rMin, float rMax, int count)
        {
            var o = new List<int>();
            float2 p0 = g.World(c);
            for (int dir = 0; dir < 8 && o.Count < count; dir++)
            {
                float ang = dir * MathF.PI / 4f;
                for (float r = rMin; r <= rMax; r += g.CellM)
                {
                    int cc = CellOf(g, p0 + r * new float2(MathF.Cos(ang), MathF.Sin(ang)));
                    if (cc >= 0 && g.In[cc] && RawGrade(g, g.World(cc)) <= StationMax)
                    {
                        o.Add(cc);
                        break;
                    }
                }
            }
            return o;
        }

        /// <summary>A lift of the base serves a green run whose skier ends up back at the base.</summary>
        private static bool BaseHasGreen(SkiGraph sg, Plan plan, List<int> members)
        {
            var home = new HashSet<int>(members);
            foreach (int L in members)
                foreach (int i in sg.Skied[L])
                {
                    if (i >= sg.Runs || !sg.Returns[i] || plan.Runs[i].Band != SlopeBand.Green)
                        continue;
                    var seen = new HashSet<int> { i };
                    var st = new Stack<int>();
                    st.Push(i);
                    while (st.Count > 0)
                    {
                        int e = st.Pop();
                        if (sg.Delivers[e].Any(home.Contains))
                            return true;
                        foreach (int j in sg.Next[e])
                            if (seen.Add(j))
                                st.Push(j);
                    }
                }
            return false;
        }

        /// <summary>The winding route from a reached cell down to its origin as a green run, or null when it does not hold up: the
        /// band on the raw terrain, the run rules, mostly away from other runs (the last 200 m into the base aside) and off every lift
        /// line. why: 0 not green, 1 run rules, 2 crowds runs, 3 along a lift line. soft: the route crowds other runs (up to 55 %)
        /// or lies along a lift line; a caller may still take it when nothing clean exists (a lost base costs more than either).</summary>
        private static List<float2> GreenRoute(Grid g, Plan plan, List<Lift> lifts, EasyReach er, int x, PointHash avoid, int[] why, out bool soft)
        {
            soft = false;
            List<int> cells = er.Route(x);
            List<float2> route = Polyline(g, cells, false);
            if (RunBand(g, route) != SlopeBand.Green)
            {
                route = cells.ConvertAll(c => g.World(c));
                if (RunBand(g, route) != SlopeBand.Green)
                {
                    why[0]++;
                    return null;
                }
            }
            List<float2> ok = SanitizeRun(g, route);
            if (ok == null || ok.Count < Densify(route).Count)
            {
                why[1]++;
                return null;
            }
            route = ok;
            int crowd = 0, counted = 0;
            float along = RunLength(route), at = 0f;
            for (int i = 0; i < route.Count; i++)
            {
                if (i > 0)
                    at += math.distance(route[i - 1], route[i]);
                if (along - at < 200f)
                    break;
                counted++;
                if (avoid.Near(route[i], 40f))
                    crowd++;
            }
            if (crowd > 0.35f * Math.Max(1, counted))
            {
                why[2]++;
                if (crowd > 0.55f * Math.Max(1, counted))
                    return null;
                soft = true;
            }
            var guard = new ChordGuard(lifts);
            foreach (PlanRun r in plan.Runs)
                guard.Add(r.Pts, 0, r.Pts.Count - 1);
            if (!guard.Fits(route, 0, route.Count - 1))
            {
                why[3]++;
                soft = true;
            }
            return route;
        }

        /// <summary>A green way down from the top of a lift of the base, when the easy ground reaches it.</summary>
        private static bool TryWayDown(Grid g, Plan plan, List<Lift> lifts, List<int> members, EasyReach er, PointHash avoid)
        {
            var why = new int[4];
            List<float2> second = null;
            Lift secondBy = null;
            foreach (int L in members)
            {
                float2 top = lifts[L].Top.xz;
                var near = er.Cost.Where(kv => math.distancesq(g.World(kv.Key), top) < 120f * 120f).OrderBy(kv => kv.Value).Take(40).ToList();
                foreach (var kv in near)
                {
                    List<float2> route = GreenRoute(g, plan, lifts, er, kv.Key, avoid, why, out bool soft);
                    if (route == null)
                        continue;
                    if (soft)
                    {
                        if (second == null)
                        {
                            second = route;
                            secondBy = lifts[L];
                        }
                        continue;
                    }
                    plan.Runs.Add(new PlanRun { Pts = route, Band = SlopeBand.Green, By = lifts[L], Kind = "beginner" });
                    plan.Log.Add($"green way down from L{lifts[L].Id}'s top: {RunLength(route):F0} m");
                    return true;
                }
            }
            if (second != null)
            {
                plan.Runs.Add(new PlanRun { Pts = second, Band = SlopeBand.Green, By = secondBy, Kind = "beginner" });
                plan.Log.Add($"green way down from L{secondBy.Id}'s top (along other runs or a lift line): {RunLength(second):F0} m");
                return true;
            }
            if (why.Sum() > 0)
                plan.Log.Add($"green way down: rejected not green {why[0]}, run rules {why[1]}, crowds runs {why[2]}, along a lift line {why[3]}");
            return false;
        }

        private static bool TryPod(Grid g, Plan plan, List<Lift> lifts, List<int> members, EasyReach er, PointHash avoid, SkiGraph before)
        {
            var bottoms = new HashSet<int>(lifts.Select(lf => lf.BottomCell));
            var cands = er.Cost.Where(kv => er.Len[kv.Key] <= PodRouteMaxM && g.Hr[kv.Key] - g.Hr[er.Root[kv.Key]] >= PodMinRise)
                .OrderBy(kv => kv.Value).Select(kv => kv.Key).ToList();
            int checkedTops = 0, netChecks = 0;
            var why = new int[10];
            Lift fallback = null;
            PlanRun fallbackRun = null;
            float shareBefore = lifts.Count > 0 ? before.BestSize / (float)lifts.Count : 0f;
            foreach (int x in cands)
            {
                if (++checkedTops > 4000 || netChecks >= 6)
                    break;
                int top = FlatCell(g, x, 20f);
                if (top < 0 || math.distance(g.World(x), g.World(top)) > 120f)
                {
                    why[0]++;
                    continue;
                }
                // the bottom station: the pad the route ends at, or one beside the lift station it ends at (the first that works)
                int root = er.Root[x], from = -1, fail = 10;
                List<int> froms = bottoms.Contains(root) ? PadsNear(g, root, 25f, 45f, 8) : new List<int> { root };
                float2 b = default, t = g.World(top);
                froms = froms.OrderBy(c => math.distancesq(g.World(c), t)).ToList();
                // or a pad on the route's lower part, still at the base (lowest first)
                List<int> along = er.Route(x);
                for (int i = along.Count - 1; i >= 0; i -= 6)
                    if (g.Hr[top] - g.Hr[along[i]] >= PodMinRise && RawGrade(g, g.World(along[i])) <= StationMax)
                        froms.Add(along[i]);
                float rise = 0f, len = 0f, plan2 = 0f;
                foreach (int fc in froms)
                {
                    b = g.World(fc);
                    if (!members.Any(L => math.distance(lifts[L].Bottom.xz, b) < 390f))
                        continue;
                    float bh = g.Hr[fc], th = g.Hr[top];
                    plan2 = math.distance(b, t);
                    rise = th - bh;
                    len = math.sqrt(plan2 * plan2 + rise * rise);
                    if (len < PodMinLength || len > 1450f || rise < PodMinRise || rise > 500f || rise / math.max(1f, plan2) > 0.75f)
                    {
                        fail = Math.Min(fail, 1);
                        continue;
                    }
                    bool clear = true;
                    for (float d = 10f; d < plan2 - 10f && clear; d += 10f)
                    {
                        float f = d / plan2;
                        clear = RawH(g, math.lerp(b, t, f)) - math.lerp(bh, th, f) <= PodClearM;
                    }
                    if (!clear)
                    {
                        fail = Math.Min(fail, 2);
                        continue;
                    }
                    if (lifts.Any(lf => SegCrossScore(lf.Bottom.xz, lf.Top.xz, b, t)))
                    {
                        fail = Math.Min(fail, 3);
                        continue;
                    }
                    from = fc;
                    break;
                }
                if (from < 0)
                {
                    why[fail == 10 ? 0 : fail]++;
                    continue;
                }
                var rw = new int[4];
                List<float2> route = GreenRoute(g, plan, lifts, er, x, avoid, rw, out bool softRoute);
                if (route == null || softRoute)
                {
                    for (int k = 0; k < 4; k++)
                        why[4 + k] += rw[k];
                    continue;
                }
                // the pod's own chord: its middle must not lie along a run (this route included)
                var guard = new ChordGuard(new List<Lift>());
                guard.AddLift(b, t);
                foreach (PlanRun r in plan.Runs)
                    guard.Add(r.Pts, 0, r.Pts.Count - 1);
                if (!guard.Fits(route, 0, route.Count - 1))
                {
                    why[8]++;
                    continue;
                }
                var lift = new Lift
                {
                    Id = lifts.Count + 1, Type = "fixed-grip", TopCell = top, BottomCell = from, Kind = "beginner",
                    Top = new float3(t.x, g.H[top], t.y), Bottom = new float3(b.x, g.H[from], b.y),
                };
                lift.Rise = lift.Top.y - lift.Bottom.y;
                lift.Length = math.distance(lift.Top, lift.Bottom);
                var run = new PlanRun { Pts = route, Band = SlopeBand.Green, By = lift, Kind = "beginner" };
                lifts.Add(lift);
                plan.Runs.Add(run);
                // the network: a pod no run of the base passes would stand on its own; keep looking for one that joins it
                netChecks++;
                SkiGraph after = Ski(g, plan, lifts);
                if (after.BestSize / (float)lifts.Count >= shareBefore - 1e-4f)
                {
                    plan.Log.Add($"beginner pod at L{lifts[members[0]].Id}'s base: fixed-grip {len:F0} m / {rise:F0} m up, green run {RunLength(route):F0} m");
                    return true;
                }
                why[9]++;
                lifts.RemoveAt(lifts.Count - 1);
                plan.Runs.RemoveAt(plan.Runs.Count - 1);
                if (fallback == null)
                {
                    fallback = lift;
                    fallbackRun = run;
                }
            }
            if (fallback != null)
            {
                lifts.Add(fallback);
                plan.Runs.Add(fallbackRun);
                plan.Log.Add($"beginner pod at L{lifts[members[0]].Id}'s base (off the network): fixed-grip {fallback.Length:F0} m / {fallback.Rise:F0} m up, " +
                    $"green run {RunLength(fallbackRun.Pts):F0} m");
                return true;
            }
            float maxRise = er.Len.Count > 0 ? er.Len.Keys.Max(c => g.Hr[c] - g.Hr[er.Root[c]]) : 0f;
            plan.Log.Add($"beginner pod at L{lifts[members[0]].Id}: easy reach {er.Len.Count} cell(s), max rise {maxRise:F0} m, {cands.Count} tall enough; " +
                $"rejected: pad {why[0]}, norms {why[1]}, clearance {why[2]}, crossing {why[3]}, not green {why[4]}, run rules {why[5]}, " +
                $"crowds runs {why[6]}, along a lift line {why[7]}, chord on run {why[8]}, splits network {why[9]}");
            return false;
        }

        private static float RunLength(List<float2> pl)
        {
            float s = 0f;
            for (int i = 1; i < pl.Count; i++)
                s += math.distance(pl[i - 1], pl[i]);
            return s;
        }

        /// <summary>Uphill search from the origin cells over ground an easy run can come down: per 7 m step the skier's descent is at
        /// most EasyEdgeMax (smoothed) and the climb tiny; on the raw terrain the route never climbs EasyClimbM over a point below
        /// it (the run rule's 10 m, less a margin), no ~30 m pitch exceeds EasyRawMax, and pitches over a green grade stay a small
        /// share (the band reads the 85th percentile). Routes along other runs cost more.</summary>
        private static EasyReach EasyUp(Grid g, List<int> origins, PointHash avoid, EasyLimits lim)
        {
            var er = new EasyReach();
            var pq = new PriorityQueue<int, float>();
            foreach (int o in origins)
            {
                er.Down[o] = -1;
                er.Root[o] = o;
                er.Len[o] = er.Cost[o] = er.Steep[o] = 0f;
                er.Hi[o] = g.Hr[o];
                pq.Enqueue(o, 0f);
            }
            var done = new HashSet<int>();
            while (pq.TryDequeue(out int c, out float d))
            {
                if (!done.Add(c))
                    continue;
                int x = c % g.W, z = c / g.W;
                for (int k = 0; k < 8; k++)
                {
                    int xx = x + DX[k], zz = z + DZ[k];
                    if (xx < 0 || zz < 0 || xx >= g.W || zz >= g.D)
                        continue;
                    int nb = zz * g.W + xx;
                    if (!g.In[nb] || done.Contains(nb) || float.IsNaN(g.Hr[nb]))
                        continue;
                    float len = g.CellM * ((k & 1) == 1 ? 1.4142f : 1f);
                    float dg = (g.H[nb] - g.H[c]) / len;   // the skier's descent from nb to c
                    float nl = er.Len[c] + len;
                    if (dg > lim.EdgeMax || dg < -0.02f || nl > lim.MaxLen || er.Hi[c] - g.Hr[nb] > lim.Climb)
                        continue;
                    bool ok = true, steep = false;
                    float2 pn = g.World(nb);
                    float back = len;
                    for (int a = c; ok && a >= 0 && back <= 60f; )
                    {
                        if (back >= 20f)
                        {
                            float p = (g.Hr[nb] - g.Hr[a]) / math.max(1f, math.distance(g.World(a), pn));
                            if (p > lim.HardPitch)
                                ok = false;
                            else if (p > lim.SteepPitch && back <= 40f)
                                steep = true;
                        }
                        int na = er.Down[a];
                        if (na >= 0)
                            back += math.distance(g.World(a), g.World(na));
                        a = na;
                    }
                    if (!ok)
                        continue;
                    float ns = er.Steep[c] + (steep ? len : 0f);
                    if (ns > lim.SteepShare * nl + 10f)
                        continue;
                    float nd = d + len * (steep ? 4f : 1f) * (nl > 150f && avoid.Near(pn, 40f) ? 3f : 1f);
                    if (er.Cost.TryGetValue(nb, out float old) && nd >= old)
                        continue;
                    er.Len[nb] = nl;
                    er.Cost[nb] = nd;
                    er.Steep[nb] = ns;
                    er.Hi[nb] = Math.Max(er.Hi[c], g.Hr[nb]);
                    er.Down[nb] = c;
                    er.Root[nb] = er.Root[c];
                    pq.Enqueue(nb, nd);
                }
            }
            return er;
        }

        /// <summary>The scorecard's crossing rule: chords that cross in plan away from a shared station (30 m).</summary>
        private static bool SegCrossScore(float2 a, float2 b, float2 c, float2 d)
        {
            if (math.distance(a, c) < 30f || math.distance(a, d) < 30f || math.distance(b, c) < 30f || math.distance(b, d) < 30f)
                return false;
            return Cross(a, b, c, d);
        }
    }
}
