// RunsFirst beginner terrain (prototype): every real base has a learning zone. For each base (lift bottoms in the low third of the
// region) with no green way home, the planner searches uphill from the base. The search starts from the stations of the base and from
// flat spots on the last 400 m of the runs that come home to it. It covers ground where an easy run can wind down (raw 30 m pitches
// green but for a few short stretches, no climb a run may not make). It stays away from other runs and lift lines where it can.
// If that ground reaches the top of a lift of the base, the winding route is the green way down from it. A route that runs onto a
// run whose rest is a green run-out home may end there.
// Otherwise a short fixed-grip chair (the mod has no surface lifts) goes from a pad the route ends at, or from a pad beside a
// station of the base, to the cheapest point that clears the norms. The chair norms: 300-1500 m, 95-512 m rise. A point clears the
// norms with:
// - flat stations,
// - the rope clear of the ground,
// - no crossing,
// - 40 m off the other ropes,
// - neither chord on a run.
// The winding route is its green run. A pod whose station no run of the base passes would split the lift network. So a pod that
// keeps the network whole wins. Last, a green way down that shares its valley with other runs (or a lift line) beats a base with no
// beginner terrain at all.

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
        private const float GreenRawPitch = 0.235f;

        /// <summary>What an uphill route search allows on the way down: the smoothed step descent, the raw ~30 m pitch that counts as
        /// steep and the one never allowed, the climb over any point below (meters), the steep share of the length, the length.</summary>
        private readonly struct EasyLimits
        {
            public readonly float EdgeMax, SteepPitch, HardPitch, Climb, SteepShare, MaxLen;
            // a graded track: the search measures its steep pitches along the path (the scorecard rates a connector by its grade along the
            // path). So a route may zigzag down a face steeper than any one direction of the grid allows
            public readonly bool AlongPath;
            // a green run (scorecard v5 rates it by the ground under it). Ground past GroundMax (raw 10 m grade) counts as steep. The route
            // does not go past GroundMax + 0.15 (0 = off)
            public readonly float GroundMax;

            public EasyLimits(float edgeMax, float steepPitch, float hardPitch, float climb, float steepShare, float maxLen, bool alongPath = false,
                float groundMax = 0f)
            {
                GroundMax = groundMax;
                EdgeMax = edgeMax;
                SteepPitch = steepPitch;
                HardPitch = hardPitch;
                Climb = climb;
                SteepShare = steepShare;
                MaxLen = maxLen;
                AlongPath = alongPath;
            }
        }

        // Limits per route type:
        // - a green run (85th percentile under 0.25, the run rule's 10 m climb less a margin)
        // - a cat track (the connector rules: no 30 m pitch over 0.50, no rise over 4.5 m)
        // - a blue run
        // - a black run (an expert way into the next area)
        private static readonly EasyLimits GreenWay = new EasyLimits(0.22f, GreenRawPitch, 0.40f, 7f, 0.08f, 4500f);
        private static EasyLimits LearnWay => new EasyLimits(0.22f, GreenRawPitch, 0.40f, 7f, 0.08f, 4500f, false, O.LearnGround);
        private static readonly EasyLimits GreenProbe = new EasyLimits(0.22f, GreenRawPitch, 0.40f, 7f, 0.08f, 1500f);   // green ground near a station
        private static readonly EasyLimits CatTrack = new EasyLimits(0.30f, ConnPitchMax, ConnPitchMax, 3.5f, 1f, 1500f);
        private static readonly EasyLimits BlueWay = new EasyLimits(0.36f, 0.38f, 0.62f, 7f, 0.08f, 3500f);
        private static readonly EasyLimits BlackWay = new EasyLimits(0.55f, 0.58f, 0.95f, 7f, 0.10f, 3500f);
        private const float PodRouteMaxM = 2600f, BaseShareCut = 0.35f, OriginRunM = 400f;
        private const float PodMinRise = 97f, PodMinLength = 320f, PodClearM = 18f;

        /// <summary>The easy-run search tree, keyed in the order the search first reached each cell: Down = the next cell toward the
        /// base (-1 at an origin), Root = the origin a cell's route ends at, Len / Cost along that route.</summary>
        private sealed class EasyReach
        {
            public readonly Dictionary<int, int> Down, Root;
            public readonly Dictionary<int, float> Len, Cost;
            public readonly List<int> Order;   // the keys in first-reached order: every enumeration that feeds a result reads this

            public EasyReach(int count)
            {
                Down = new Dictionary<int, int>(count);
                Root = new Dictionary<int, int>(count);
                Len = new Dictionary<int, float>(count);
                Cost = new Dictionary<int, float>(count);
                Order = new List<int>(count);
            }

            /// <summary>The cells and their costs in first-reached order (the dictionary's own order is not defined).</summary>
            public IEnumerable<KeyValuePair<int, float>> CostInOrder() => Order.Select(c => new KeyValuePair<int, float>(c, Cost[c]));

            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            public List<int> Route(int c)
            {
                var cells = new List<int>();
                for (; c >= 0 && cells.Count < 2000; c = Down[c])
                    cells.Add(c);
                return cells;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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
                // bases as scorecard v3 reads them: two or more lift bottoms within 400 m low on the mountain (else the lowest lift's)
                int[] rootOf = V3Bases(g, lifts, out HashSet<int> bases, out List<int> baseOrder);
                if (O.Debug && pass == 0)
                    Console.WriteLine($"DEBUG bases: relief {hLo:F0}-{hHi:F0}, low cut {lowCut:F0}; " + string.Join("; ", bases.Select(b => string.Join(",", Enumerable.Range(0, lifts.Count).Where(L => rootOf[L] == b).Select(L => $"L{lifts[L].Id} ({RawH(g, lifts[L].Bottom.xz):F0})")))));
                SkiGraph sg = Ski(g, plan, lifts);
                List<int> members = null;
                int key = -1;
                foreach (int b in baseOrder)
                {
                    var m = Enumerable.Range(0, lifts.Count).Where(L => rootOf[L] == b).OrderBy(L => RawH(g, lifts[L].Bottom.xz)).ToList();
                    if (!failed.Contains(lifts[m[0]].Id) && !BaseHasGreen(sg, plan, lifts, m))
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
                // the middle of every lift line, which a green route should not follow either
                var lines = new PointHash();
                foreach (Lift lf in lifts)
                    for (float f = 0.15f; f <= 0.85f; f += 10f / math.max(10f, math.distance(lf.Bottom.xz, lf.Top.xz)))
                        lines.Add(math.lerp(lf.Bottom.xz, lf.Top.xz, f));
                EasyReach er = EasyUp(g, origins, avoid, GreenWay, lines);
                var guard = new ChordGuard(lifts);
                foreach (PlanRun r in plan.Runs)
                    guard.Add(r.Pts, 0, r.Pts.Count - 1);
                // Order of tries:
                // - a clean green way down first,
                // - then a pod with its own green run,
                // - then a way down that shares its valley or a green branch lower down that leaves a run of the base.
                // Off the lift lines first, then from a top first. The code keeps each try only when a beginner at the base then has green
                // skiing to lap (as scorecard v3 reads it)
                int runsBefore = plan.Runs.Count, liftsBefore = lifts.Count, logBefore = plan.Log.Count;
                bool Laps()
                {
                    // the base as it stands now (a pod's bottom joins its cluster)
                    int[] now = V3Bases(g, lifts, out _);
                    if (BaseHasGreen(Ski(g, plan, lifts), plan, lifts, Enumerable.Range(0, lifts.Count).Where(L => now[L] == now[members[0]]).ToList()))
                        return true;
                    plan.Runs.RemoveRange(runsBefore, plan.Runs.Count - runsBefore);
                    lifts.RemoveRange(liftsBefore, lifts.Count - liftsBefore);
                    plan.Log.Add("  (undone: no green lap for the base's beginners)");
                    failed.Add(key);
                    return false;
                }
                if (TryWayDown(g, plan, lifts, members, er, avoid, guard, out PlanRun shared, out float sharedSoft))
                {
                    if (Laps())
                        ways++;
                }
                else if (TryPod(g, plan, lifts, members, er, avoid, guard, sg))
                {
                    if (Laps())
                        pods++;
                }
                else
                {
                    PlanRun branch = null;
                    float branchSoft = float.MaxValue;
                    if (shared == null || sharedSoft >= 1f)
                        branch = FindBranch(g, plan, lifts, members, er, avoid, guard, sg, out branchSoft);
                    PlanRun pick = shared != null && sharedSoft < 1f ? shared : branch != null && branchSoft < 1f ? branch : shared ?? branch;
                    if (pick == null)
                    {
                        failed.Add(key);
                        continue;
                    }
                    float soft = pick == shared ? sharedSoft : branchSoft;
                    plan.Runs.Add(pick);
                    plan.Log.Add($"{(pick == shared ? $"green way down from L{pick.By.Id}'s top" : $"green branch home at L{lifts[members[0]].Id}'s base")}" +
                        $"{(soft >= 1f ? " (along a lift line)" : soft > 0f ? " (along other runs)" : "")}: {RunLength(pick.Pts):F0} m, " +
                        $"{RawH(g, pick.Pts[0]) - RawH(g, pick.Pts[pick.Pts.Count - 1]):F0} m down");
                    if (Laps())
                        ways++;
                }
            }
            plan.Log.Add($"beginner terrain: {ways} green way(s) down, {pods} pod(s) ({tried} base(s) without a green way home)");
        }

        /// <summary>The owner's sixth rule (generous at the bottom, learning terrain where the terrain is). A base that laps green already
        /// still gets a learning pod on the green ground its runs leave alone. This holds while the lift budget has room (the slots held for
        /// the beginner pods often stay free). The method keeps a pod only where it joins the network and the ski graph loses nothing.
        /// Returns the pods added.</summary>
        private static int AddLearningPods(Grid g, Plan plan, List<Lift> lifts, float km2)
        {
            int budget = (int)Math.Ceiling(1.5 * km2) + 2, added = 0;
            var tried = new HashSet<int>();
            while (lifts.Count < budget)
            {
                int[] rootOf = V3Bases(g, lifts, out HashSet<int> bases);
                List<int> members = bases.OrderBy(b => b)
                    .Select(b => Enumerable.Range(0, lifts.Count).Where(L => rootOf[L] == b).OrderBy(L => RawH(g, lifts[L].Bottom.xz)).ToList())
                    .FirstOrDefault(m => !tried.Contains(lifts[m[0]].Id));
                if (members == null)
                    break;
                tried.Add(lifts[members[0]].Id);
                SkiGraph sg = Ski(g, plan, lifts);
                var avoid = new PointHash();
                foreach (PlanRun r in plan.Runs)
                    foreach (float2 p in r.Pts)
                        avoid.Add(p);
                var lines = new PointHash();
                foreach (Lift lf in lifts)
                    for (float f = 0.15f; f <= 0.85f; f += 10f / math.max(10f, math.distance(lf.Bottom.xz, lf.Top.xz)))
                        lines.Add(math.lerp(lf.Bottom.xz, lf.Top.xz, f));
                EasyReach er = EasyUp(g, BaseOrigins(g, plan, lifts, members, sg), avoid, GreenWay, lines);
                var guard = new ChordGuard(lifts);
                foreach (PlanRun r in plan.Runs)
                    guard.Add(r.Pts, 0, r.Pts.Count - 1);
                SkiRead before = SkiRead.Of(g, plan, lifts);
                int n = lifts.Count, runsBefore = plan.Runs.Count, logBefore = plan.Log.Count;
                if (!TryPod(g, plan, lifts, members, er, avoid, guard, sg))
                {
                    if (!O.Debug)
                        plan.Log.RemoveRange(logBefore, plan.Log.Count - logBefore);
                    continue;
                }
                SkiGraph now = Ski(g, plan, lifts);
                SkiRead after = SkiRead.Of(g, plan, lifts);
                bool lost = now.BestSize < before.Net + 1 || after.Green.Count(x => x) < before.Green.Count(x => x);
                for (int L = 0; L < n && !lost; L++)
                    lost = before.Home[L] && !after.Home[L] || before.Live[L] && !after.Live[L];
                foreach (var kv in before.Els)
                    if (!lost && after.Els.TryGetValue(kv.Key, out var e) && (kv.Value.Reached && !e.Reached || kv.Value.Returns && !e.Returns))
                        lost = true;
                if (lost)
                {
                    lifts.RemoveRange(n, lifts.Count - n);
                    plan.Runs.RemoveRange(runsBefore, plan.Runs.Count - runsBefore);
                    plan.Log.RemoveRange(logBefore, plan.Log.Count - logBefore);
                    continue;
                }
                plan.Log[plan.Log.Count - 1] = plan.Log[plan.Log.Count - 1].Replace("beginner pod", "learning pod");
                added++;
            }
            return added;
        }

        /// <summary>Lift index -> cluster root (lift bottoms within 400 m chain together, as the scorecard's bases). Bases = the
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
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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

        /// <summary>A beginner at the base finds green skiing to lap (scorecard v3: up the base's lifts, anywhere they lead).</summary>
        private static bool BaseHasGreen(SkiGraph sg, Plan plan, List<Lift> lifts, List<int> members) => LearnOk(sg, lifts, members, SlopeBand.Green);

        /// <summary>The winding route from a reached cell down to its origin as a green run, or null when it does not hold up. The route
        /// holds up when it meets all these conditions:
        /// - It is green by the ground under it and the run rules, or else a green cat track that the grading tool cuts (graded: the
        ///   connector rules and an along-path grade under the blue line. The winding green on steeper ground).
        /// - It stays mostly away from other runs (the last 200 m into the base aside).
        /// - It stays off every lift line.
        /// why: 0 not green, 1 run rules, 2 crowds runs, 3 along a lift line.
        /// soft (0 = clean): the share of the route along other runs when over 35 % (up to 70 %), plus 1 when a run lies along a lift line.
        /// A caller may still take the softest when nothing clean exists (a base without beginner terrain costs more than either).</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static List<float2> GreenRoute(Grid g, Plan plan, List<Lift> lifts, List<int> members, EasyReach er, int x, PointHash avoid, ChordGuard guard, int[] why, out float soft,
            out bool graded)
        {
            soft = 0f;
            graded = false;
            List<int> cells = er.Route(x);
            List<float2> route = null;
            bool green = false;
            foreach (List<float2> pl in new[] { Polyline(g, cells, false), cells.ConvertAll(c => g.World(c)) })
            {
                if (GroundBand(g, pl) == SlopeBand.Green)
                {
                    green = true;
                    List<float2> ok = SanitizeRun(g, pl);
                    if (ok != null && ok.Count >= Densify(pl).Count)
                    {
                        route = ok;
                        break;
                    }
                }
                if (GreenTrack(g, pl))
                {
                    route = pl;
                    graded = true;
                    break;
                }
            }
            if (route == null)
            {
                why[green ? 1 : 0]++;
                return null;
            }
            float share = CrowdShare(route, avoid);
            if (share > 0.35f)
            {
                // a route that runs onto a run in a valley it shares can end there when that run's way on is a green run-out to the
                // base. The skier follows it home
                List<float2> joined = JoinRunOut(g, plan, lifts, members, route, avoid, graded);
                if (joined != null)
                {
                    route = joined;
                    share = CrowdShare(route, avoid);
                }
            }
            if (share > 0.35f)
            {
                why[2]++;
                if (share > 0.70f)
                    return null;
                soft = share;
            }
            if (!graded && !guard.FitsAdding(route, 0, route.Count - 1))
            {
                why[3]++;
                soft += 1f;
            }
            return route;
        }

        /// <summary>The share of a route's samples within 40 m of a run (the last 200 m, where it comes home, left out).</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static float CrowdShare(List<float2> route, PointHash avoid)
        {
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
            return crowd / (float)Math.Max(1, counted);
        }

        /// <summary>The route (top first) cut where it first comes onto a run (25 m) whose rest is green and passes a station of the
        /// base, with that run's point as its end. The result is null when it meets none past its first 250 m or keeps under 60 m of drop.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static List<float2> JoinRunOut(Grid g, Plan plan, List<Lift> lifts, List<int> members, List<float2> route, PointHash avoid, bool graded)
        {
            float at = 0f;
            int tries = 0;
            for (int i = 1; i < route.Count && tries < 12; i++)
            {
                at += math.distance(route[i - 1], route[i]);
                if (at < 250f || !avoid.Near(route[i], 25f))
                    continue;
                tries++;
                foreach (PlanRun r in plan.Runs)
                {
                    int k = -1;
                    float best = 25f * 25f;
                    for (int j = 0; j < r.Pts.Count; j++)
                    {
                        float d2 = math.distancesq(r.Pts[j], route[i]);
                        if (d2 < best)
                        {
                            best = d2;
                            k = j;
                        }
                    }
                    if (k < 0 || k > r.Pts.Count - 3)
                        continue;
                    List<float2> rest = r.Pts.GetRange(k, r.Pts.Count - k);
                    if (!members.Any(L => rest.Skip(1).Any(p => math.distancesq(p, lifts[L].Bottom.xz) < 55f * 55f))
                        || (r.Graded ? !GreenTrack(g, r.Pts) : GroundBand(g, r.Pts) != SlopeBand.Green))
                        continue;
                    var o = route.GetRange(0, i);
                    o.Add(r.Pts[k]);
                    if (RawH(g, o[0]) - RawH(g, o[o.Count - 1]) < 60f || (graded ? !GreenTrack(g, o)
                        : GroundBand(g, o) != SlopeBand.Green || SanitizeRun(g, o) is not { } sane || sane.Count < Densify(o).Count))
                        continue;
                    return o;
                }
            }
            return null;
        }

        /// <summary>A green way down from the top of a lift of the base, when the easy ground reaches it. Out parameter shared = the softest one
        /// that shares its ground with other runs or a lift line (not added) when no clean one exists.</summary>
        private static bool TryWayDown(Grid g, Plan plan, List<Lift> lifts, List<int> members, EasyReach er, PointHash avoid, ChordGuard guard, out PlanRun shared,
            out float softest)
        {
            var why = new int[4];
            shared = null;
            softest = float.MaxValue;
            foreach (int L in members)
            {
                float2 top = lifts[L].Top.xz;
                var near = er.CostInOrder().Where(kv => math.distancesq(g.World(kv.Key), top) < 120f * 120f).OrderBy(kv => kv.Value).Take(40).ToList();
                foreach (var kv in near)
                {
                    List<float2> route = GreenRoute(g, plan, lifts, members, er, kv.Key, avoid, guard, why, out float soft, out bool graded);
                    // a learning run, not a transfer: a route this short would credit a hop onto the next lift
                    if (route == null || RunLength(route) < 250f || RawH(g, route[0]) - RawH(g, route[route.Count - 1]) < 40f)
                        continue;
                    if (soft > 0f)
                    {
                        if (soft < softest)
                        {
                            softest = soft;
                            shared = new PlanRun { Pts = route, Band = SlopeBand.Green, By = lifts[L], Kind = "beginner", Graded = graded };
                        }
                        continue;
                    }
                    plan.Runs.Add(new PlanRun { Pts = route, Band = SlopeBand.Green, By = lifts[L], Kind = "beginner", Graded = graded });
                    plan.Log.Add($"green way down from L{lifts[L].Id}'s top: {RunLength(route):F0} m");
                    return true;
                }
            }
            if (why.Sum() > 0)
                plan.Log.Add($"green way down: rejected not green {why[0]}, run rules {why[1]}, crowds runs {why[2]}, along a lift line {why[3]}");
            return false;
        }

        /// <summary>For each base that still has no green skiing to lap once the ways home and the regrade are in (no green way down, pod,
        /// way home or regraded track gives it one), the method gives it a winding green that the grading tool cuts. The owner's rule: a green
        /// learning run on steep ground is a graded track. The green runs from the top of a lift of the base down to its stations. Returns the
        /// bases given one.</summary>
        private static int AddGradedLearningWays(Grid g, Plan plan, List<Lift> lifts)
        {
            int added = 0, tried = 0;
            int[] root = V3Bases(g, lifts, out HashSet<int> bases);
            SkiGraph sg = Ski(g, plan, lifts);
            foreach (int b in bases.OrderBy(b => b))
            {
                var members = Enumerable.Range(0, lifts.Count).Where(L => root[L] == b).OrderBy(L => RawH(g, lifts[L].Bottom.xz)).ToList();
                if (BaseHasGreen(sg, plan, lifts, members))
                    continue;
                tried++;
                var avoid = new PointHash();
                foreach (PlanRun r in plan.Runs)
                    foreach (float2 p in r.Pts)
                        avoid.Add(p);
                var lines = new PointHash();
                foreach (Lift lf in lifts)
                    for (float f = 0.15f; f <= 0.85f; f += 10f / math.max(10f, math.distance(lf.Bottom.xz, lf.Top.xz)))
                        lines.Add(math.lerp(lf.Bottom.xz, lf.Top.xz, f));
                int before = plan.Runs.Count;
                if (!TryGradedWayDown(g, plan, lifts, members, avoid, lines))
                    continue;
                sg = Ski(g, plan, lifts);
                if (BaseHasGreen(sg, plan, lifts, members))
                {
                    added++;
                    continue;
                }
                plan.Runs.RemoveRange(before, plan.Runs.Count - before);
                plan.Log.Add("  (undone: no green lap for the base's beginners)");
                sg = Ski(g, plan, lifts);
            }
            if (tried > 0)
                plan.Log.Add($"learning ways: {added} of {tried} base(s) without green skiing to lap given a graded green way down");
            return added;
        }

        /// <summary>A green cat track from the top of a lift of the base down to its stations. The search treats it as it treats the ways
        /// home (steep pitches along the path, so it may zigzag down a face). It is at least 250 m long and 40 m down (a learning run, not a
        /// transfer).</summary>
        private static bool TryGradedWayDown(Grid g, Plan plan, List<Lift> lifts, List<int> members, PointHash avoid, PointHash lines)
        {
            var origins = new List<int>();
            foreach (int L in members)
            {
                origins.Add(lifts[L].BottomCell);
                origins.AddRange(PadsNear(g, lifts[L].BottomCell, 25f, 45f, 8));
            }
            EasyReach er = EasyUp(g, origins.Distinct().ToList(), avoid, WayGreen, lines);
            foreach (int L in members)
            {
                float2 top = lifts[L].Top.xz;
                float topH = RawH(g, top);
                var starts = er.CostInOrder().Where(kv => kv.Value > 0f && math.distancesq(g.World(kv.Key), top) < WayStartM * WayStartM && g.Hr[kv.Key] <= topH + 3f)
                    .OrderBy(kv => kv.Value).Take(8).Select(kv => kv.Key).ToList();
                foreach (int s in starts)
                {
                    List<int> cells = er.Route(s);
                    if (cells.Count < 3)
                        continue;
                    foreach (List<float2> pl in new[] { Polyline(g, cells, false), cells.ConvertAll(c => g.World(c)) })
                    {
                        float drop = RawH(g, pl[0]) - RawH(g, pl[pl.Count - 1]);
                        if (!GreenTrack(g, pl) || RunLength(pl) < 250f || drop < 40f)
                            continue;
                        plan.Runs.Add(new PlanRun { Pts = pl, Band = SlopeBand.Green, By = lifts[L], Kind = "beginner", Graded = true });
                        plan.Log.Add($"graded green way down from L{lifts[L].Id}'s top: {RunLength(pl):F0} m, {drop:F0} m down");
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>For a base whose easy ground neither reaches a top nor rises the 97 m a pod needs: a green run that leaves a run
        /// or traverse skied from a lift of the base where the easy ground meets it (30 m, not above it) and winds home. The green run is
        /// at least 60 m down and 300 m long. The highest clean one comes first (else the softest, not added). Whoever comes that far
        /// down skis green the rest of the way.</summary>
        private static PlanRun FindBranch(Grid g, Plan plan, List<Lift> lifts, List<int> members, EasyReach er, PointHash avoid, ChordGuard guard, SkiGraph sg,
            out float softest)
        {
            var at = new Dictionary<long, List<float3>>();
            static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
            foreach (int i in members.SelectMany(L => sg.Skied[L]).Distinct())
                foreach (float3 p in sg.P[i])
                {
                    long k = Key((int)Math.Floor(p.x / 60f), (int)Math.Floor(p.z / 60f));
                    if (!at.TryGetValue(k, out List<float3> l))
                        at[k] = l = new List<float3>();
                    l.Add(p);
                }
            bool On(int c)
            {
                float2 q = g.World(c);
                int cx = (int)Math.Floor(q.x / 60f), cz = (int)Math.Floor(q.y / 60f);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (at.TryGetValue(Key(cx + dx, cz + dz), out List<float3> l) &&
                            l.Any(p => math.distancesq(p.xz, q) < 30f * 30f && g.Hr[c] <= p.y + 4f))
                            return true;
                return false;
            }
            var why = new int[4];
            PlanRun best = null;
            softest = float.MaxValue;
            int tries = 0;
            foreach (int c in er.Order.Where(c => er.Len[c] >= 300f && g.Hr[c] - g.Hr[er.Root[c]] >= 60f)
                .OrderByDescending(c => g.Hr[c] - g.Hr[er.Root[c]]))
            {
                if (!On(c))
                    continue;
                if (++tries > 60)
                    break;
                List<float2> route = GreenRoute(g, plan, lifts, members, er, c, avoid, guard, why, out float soft, out bool graded);
                if (route == null || soft >= softest)
                    continue;
                softest = soft;
                best = new PlanRun { Pts = route, Band = SlopeBand.Green, By = lifts[members[0]], Kind = "beginner", Graded = graded };
                if (soft == 0f)
                    break;
            }
            if (best == null)
                plan.Log.Add($"green branch at L{lifts[members[0]].Id}'s base: none ({tries} start(s) on the base's runs; rejected not green {why[0]}, " +
                    $"run rules {why[1]}, crowds runs {why[2]}, along a lift line {why[3]})");
            return best;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static bool TryPod(Grid g, Plan plan, List<Lift> lifts, List<int> members, EasyReach er, PointHash avoid, ChordGuard lines, SkiGraph before)
        {
            var bottoms = new HashSet<int>(lifts.Select(lf => lf.BottomCell));
            var cands = er.CostInOrder().Where(kv => er.Len[kv.Key] <= PodRouteMaxM && g.Hr[kv.Key] - g.Hr[er.Root[kv.Key]] >= PodMinRise)
                .OrderBy(kv => kv.Value).Select(kv => kv.Key).ToList();
            int checkedTops = 0, netChecks = 0;
            var topsSeen = new HashSet<int>();
            int reroutes = 0;
            var why = new int[10];
            bool dbg = O.Debug;
            if (dbg)
                foreach (int c in er.Order)
                    if (er.Len[c] <= PodRouteMaxM)
                        plan.Dots.Add((g.World(c), 120, 255, 255));
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
                // a top station already judged from the cheapest route to it
                if (!topsSeen.Add(top))
                    continue;
                // the bottom station: the pad the route ends at, or one beside the lift station it ends at (the first that works)
                int root = er.Root[x], from = -1, fail = 10;
                List<int> froms = bottoms.Contains(root) ? PadsNear(g, root, 25f, 45f, 8) : new List<int> { root };
                float2 b = default, t = g.World(top);
                // or one beside any station of the base: a terminal of its own in the same base area, its rope parting from the other's
                foreach (int L in members)
                    froms.AddRange(PadsNear(g, lifts[L].BottomCell, SpreadMinM, SpreadMinM + 14f, 8).Where(c => math.distance(g.World(c), lifts[L].Bottom.xz) < SpreadMinM + 17f));
                froms = froms.Distinct().OrderBy(c => math.distancesq(g.World(c), t)).ToList();
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
                    if (lifts.Any(lf => SegCrossScore(lf.Bottom.xz, lf.Top.xz, b, t) || (NoParallel && ParallelLines(b, t, lf.Bottom.xz, lf.Top.xz))))
                    {
                        fail = Math.Min(fail, 3);
                        continue;
                    }
                    // past its first 15 % (where it may leave a shared terminal area) the rope keeps 40 m off every other rope
                    bool apart = true;
                    for (float f = 0.15f; f <= 1f && apart; f += 20f / math.max(20f, plan2))
                    {
                        float2 q = math.lerp(b, t, f);
                        apart = lifts.All(lf => SegDist(q, lf.Bottom.xz, lf.Top.xz) >= 40f);
                    }
                    if (!apart)
                    {
                        fail = Math.Min(fail, 3);
                        continue;
                    }
                    from = fc;
                    break;
                }
                if (dbg)
                {
                    var col = from >= 0 ? (0, 255, 0) : fail == 1 ? (180, 0, 255) : fail == 2 ? (255, 140, 0) : fail == 3 ? (255, 0, 0) : (128, 128, 128);
                    plan.Dots.Add((g.World(x), col.Item1, col.Item2, col.Item3));
                }
                if (from < 0)
                {
                    why[fail == 10 ? 0 : fail]++;
                    continue;
                }
                var rw = new int[4];
                List<float2> route = GreenRoute(g, plan, lifts, members, er, x, avoid, lines, rw, out float softRoute, out bool graded);
                if (route == null || softRoute > 0f)
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
                    // the route winds up under the rope: look again from the same pad, kept off it (when the other runs leave room)
                    List<float2> off = null;
                    if (reroutes < 8 && guard.Fits(new List<float2> { b }, 0, 0))
                    {
                        reroutes++;
                        var rope = new PointHash();
                        for (float f = 0.1f; f <= 0.9f; f += 10f / math.max(10f, plan2))
                            rope.Add(math.lerp(b, t, f));
                        EasyReach er2 = EasyUp(g, new List<int> { er.Root[x] }, avoid, GreenWay, rope, 10f);
                        if (er2.Cost.ContainsKey(x))
                        {
                            off = GreenRoute(g, plan, lifts, members, er2, x, avoid, lines, new int[4], out float softOff, out bool gradedOff);
                            graded = off != null ? gradedOff : graded;
                            if (off != null && (softOff > 0f || !guard.Fits(off, 0, off.Count - 1)))
                                off = null;
                        }
                    }
                    if (off == null)
                    {
                        why[8]++;
                        continue;
                    }
                    route = off;
                }
                var lift = new Lift
                {
                    Id = lifts.Count + 1, Type = "fixed-grip", TopCell = top, BottomCell = from, Kind = "beginner",
                    Top = new float3(t.x, g.H[top], t.y), Bottom = new float3(b.x, g.H[from], b.y),
                };
                lift.Rise = lift.Top.y - lift.Bottom.y;
                lift.Length = math.distance(lift.Top, lift.Bottom);
                var run = new PlanRun { Pts = route, Band = SlopeBand.Green, By = lift, Kind = "beginner", Graded = graded };
                lifts.Add(lift);
                plan.Runs.Add(run);
                // the network: a pod no run of the base passes would stand on its own. Keep looking for one that joins it
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

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static float RunLength(List<float2> pl)
        {
            float s = 0f;
            for (int i = 1; i < pl.Count; i++)
                s += math.distance(pl[i - 1], pl[i]);
            return s;
        }

        // cells the easy-ground searches settled in this region's plan, and the most they may settle. On wide gentle ground one
        // search floods the whole region (a link that fails tries three of them per round). The region must plan in seconds
        private static long EasyWork, LinkWork;
        // a graded track may climb a little per step (a contour step on rough ground). The route's climb rule still holds it
        private static float EasyUpTol => O.EasyUpTol;
        private static float EasyBudget => O.EasyBudget;
        private static float LinkBudget => O.LinkBudget;

        /// <summary>The easy-ground search's working arrays per grid cell (Steep: meters steeper than green, Hi: the highest raw point
        /// along the route), clean between searches. One set serves every search (regions plan one at a time).</summary>
        private sealed class EasyScratch
        {
            public readonly int[] Down, Root;
            public readonly float[] Len, Cost, Steep, Hi;
            public readonly bool[] Seen, Done;
            public readonly List<int> Order = new List<int>();   // the cells in the order the search first reached them

            public EasyScratch(int n)
            {
                Down = new int[n];
                Root = new int[n];
                Len = new float[n];
                Cost = new float[n];
                Steep = new float[n];
                Hi = new float[n];
                Seen = new bool[n];
                Done = new bool[n];
            }
        }
        private static EasyScratch s_Easy;

        /// <summary>Uphill search from the origin cells over ground an easy run can come down. Per 7 m step the skier's descent is at
        /// most EasyEdgeMax (smoothed) and the climb tiny. On the raw terrain these limits hold:
        /// - The route never climbs EasyClimbM over a point below it (the run rule's 10 m, less a margin).
        /// - No ~30 m pitch exceeds EasyRawMax.
        /// - Pitches over a green grade stay a small share (the band reads the 85th percentile).
        /// Routes along other runs, or along the given lift lines, cost more. With a goal cell the search heads for it (A*, the straight
        /// distance left as the estimate) and stops when it settles that cell.</summary>
        private static float TrackTurnM => O.TrackTurnM;

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static EasyReach EasyUp(Grid g, List<int> origins, PointHash avoid, EasyLimits lim, PointHash lines = null, float linesCost = 3f, int goal = -1)
        {
            int n = g.W * g.D;
            if (s_Easy == null || s_Easy.Done.Length < n)
                s_Easy = new EasyScratch(n);
            EasyScratch er = s_Easy;
            List<int> order = er.Order;
            order.Clear();
            var pq = new CellHeap();
            foreach (int o in origins)
            {
                if (!er.Seen[o])
                {
                    er.Seen[o] = true;
                    order.Add(o);
                }
                er.Down[o] = -1;
                er.Root[o] = o;
                er.Len[o] = er.Cost[o] = er.Steep[o] = 0f;
                er.Hi[o] = g.Hr[o];
                pq.Enqueue(o, 0f);
            }
            int settled = 0;
            long cap = (long)EasyBudget - EasyWork;   // a search past the budget stops where it is (its cheapest cells settled)
            float2 goalP = goal >= 0 ? g.World(goal) : 0f;
            while (settled < cap && pq.TryDequeue(out int c, out float _))
            {
                if (er.Done[c])
                    continue;
                er.Done[c] = true;
                settled++;
                if (c == goal)
                    break;
                float d = er.Cost[c];
                int x = c % g.W, z = c / g.W;
                for (int k = 0; k < 8; k++)
                {
                    int xx = x + DX[k], zz = z + DZ[k];
                    if (xx < 0 || zz < 0 || xx >= g.W || zz >= g.D)
                        continue;
                    int nb = zz * g.W + xx;
                    if (!g.In[nb] || er.Done[nb] || float.IsNaN(g.Hr[nb]))
                        continue;
                    float len = g.CellM * ((k & 1) == 1 ? 1.4142f : 1f);
                    float dg = (g.H[nb] - g.H[c]) / len;   // the skier's descent from nb to c
                    float nl = er.Len[c] + len;
                    if (dg > lim.EdgeMax || dg < (lim.AlongPath ? -EasyUpTol : -0.02f) || nl > lim.MaxLen || er.Hi[c] - g.Hr[nb] > lim.Climb)
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
                            else if ((lim.AlongPath ? (g.Hr[nb] - g.Hr[a]) / back : p) > lim.SteepPitch && back <= 40f)
                                steep = true;
                        }
                        int na = er.Down[a];
                        if (na >= 0)
                            back += math.distance(g.World(a), g.World(na));
                        a = na;
                    }
                    if (!ok)
                        continue;
                    if (lim.GroundMax > 0f)
                    {
                        float gr = RawGrade(g, pn);
                        if (gr > lim.GroundMax + 0.15f)
                            continue;
                        steep |= gr > lim.GroundMax;
                    }
                    float ns = er.Steep[c] + (steep ? len : 0f);
                    if (ns > lim.SteepShare * nl + 10f)
                        continue;
                    float nd = d + len * (steep ? 4f : 1f) * (nl > 150f && avoid.NearCell(g, nb, 40f) ? 3f : 1f) * (lines != null && lines.NearCell(g, nb, 25f) ? linesCost : 1f);
                    // a graded track: a sharp turn (past 60 degrees) costs as much as TrackTurnM more track. So of the zigzags down a
                    // face at the same grade (all the same length) the search takes the one with the longest legs
                    int dn = er.Down[c];
                    if (lim.AlongPath && dn >= 0 && TrackTurnM > 0f)
                    {
                        int ox = dn % g.W - x, oz = dn / g.W - z;
                        if (-DX[k] * ox - DZ[k] * oz < 0.5f * math.sqrt((DX[k] * DX[k] + DZ[k] * DZ[k]) * (ox * ox + oz * oz)))
                            nd += TrackTurnM;
                    }
                    if (er.Seen[nb] && nd >= er.Cost[nb])
                        continue;
                    if (!er.Seen[nb])
                    {
                        er.Seen[nb] = true;
                        order.Add(nb);
                    }
                    er.Len[nb] = nl;
                    er.Cost[nb] = nd;
                    er.Steep[nb] = ns;
                    er.Hi[nb] = Math.Max(er.Hi[c], g.Hr[nb]);
                    er.Down[nb] = c;
                    er.Root[nb] = er.Root[c];
                    pq.Enqueue(nb, goal >= 0 ? nd + math.distance(pn, goalP) : nd);
                }
            }
            EasyWork += settled;
            // the tree as the maps the callers read, keyed in first-reached order. The arrays go back clean
            var tree = new EasyReach(order.Count);
            foreach (int c in order)
            {
                tree.Down[c] = er.Down[c];
                tree.Root[c] = er.Root[c];
                tree.Len[c] = er.Len[c];
                tree.Cost[c] = er.Cost[c];
                tree.Order.Add(c);
                er.Seen[c] = er.Done[c] = false;
            }
            return tree;
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
