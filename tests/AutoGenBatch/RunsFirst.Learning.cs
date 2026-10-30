// RunsFirst learning areas (scorecard v5): a base's beginners learn on green runs, not on cat tracks. A base (two or more lift bottoms
// within 400 m low on the mountain, as the scorecard reads them) whose beginners get to under 0.8 km of green runs gets a learning
// area: a beginner chair (a short fixed-grip, 30-150 m of rise: a beginner should not face 300 m of vertical first) from a pad at the
// base up its gentle ground, and up to three green runs from its top back to its bottom. The chair keeps the other lifts' rules
// (flat stations, the rope clear of the ground, no crossing, 40 m off the other ropes, no station under a rope, its chord off the
// runs); its runs are green by the ground under them, groomed, and keep away from the other runs.

using System;
using System.Collections.Generic;
using System.Linq;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class RunsFirst
    {
        private static readonly float LearnMinRise = Env("RF_LMINRISE", 32f), LearnMaxRise = Env("RF_LMAXRISE", 130f);
        private static readonly float LearnMinLen = Env("RF_LMINLEN", 180f), LearnRouteMax = Env("RF_LROUTE", 1500f);
        private const float LearnFullKm = 0.85f, LearnChairGrade = 0.45f, LearnTopM = 90f, LearnRunMinM = 200f, LearnBelowTopM = 130f;
        private static readonly bool LearnHigh = Environment.GetEnvironmentVariable("RF_NOLEARNHIGH") != "1";

        /// <summary>Pads for a learning chair's bottom station below a lift's top: green ground 160-700 m away, 35-130 m lower, off the
        /// runs.</summary>
        private static List<int> BelowTop(Grid g, List<Lift> lifts, Lift lf, PointHash avoid)
        {
            var o = new List<int>();
            float2 t = lf.Top.xz;
            float th = RawH(g, t);
            for (float r = 160f; r <= 700f && o.Count < 400; r += 25f)
                for (float a = 0f; a < 2f * MathF.PI; a += 25f / r)
                {
                    float2 p = t + r * new float2(MathF.Cos(a), MathF.Sin(a));
                    int c = CellOf(g, p);
                    if (c < 0 || !g.In[c] || float.IsNaN(g.Hr[c]))
                        continue;
                    float drop = th - g.Hr[c];
                    if (drop >= LearnMinRise && drop <= LearnMaxRise && RawGrade(g, g.World(c)) <= 0.22f && !avoid.Near(g.World(c), 30f)
                        && !lifts.Any(o2 => SegDist(g.World(c), o2.Bottom.xz, o2.Top.xz) < 45f))
                        o.Add(c);
                }
            return o;
        }

        /// <summary>Learning areas for the bases short of green runs; returns the chairs added.</summary>
        private static int AddLearningAreas(Grid g, Plan plan, List<Lift> lifts)
        {
            int added = 0;
            var tried = new HashSet<int>();
            for (int pass = 0; pass < 4; pass++)
            {
                int[] root = V3Bases(g, lifts, out HashSet<int> bases);
                SkiGraph sg = Ski(g, plan, lifts);
                List<int> members = null;
                float km = 0f;
                foreach (int b in bases)
                {
                    var m = Enumerable.Range(0, lifts.Count).Where(L => root[L] == b).OrderBy(L => RawH(g, lifts[L].Bottom.xz)).ToList();
                    if (tried.Contains(lifts[m[0]].Id) || m.Any(L => lifts[L].Type == "beginner"))
                        continue;
                    km = LearnKm(sg, plan, lifts, m);
                    if (km >= LearnFullKm)
                        continue;
                    members = m;
                    break;
                }
                if (members == null)
                    break;
                tried.Add(lifts[members[0]].Id);
                var avoid = new PointHash();
                foreach (PlanRun r in plan.Runs)
                    foreach (float2 p in r.Pts)
                        avoid.Add(p);
                foreach (List<float2> c in plan.Connectors)
                    foreach (float2 p in c)
                        avoid.Add(p);
                var lines = new PointHash();
                foreach (Lift lf in lifts)
                    for (float f = 0.15f; f <= 0.85f; f += 10f / math.max(10f, math.distance(lf.Bottom.xz, lf.Top.xz)))
                        lines.Add(math.lerp(lf.Bottom.xz, lf.Top.xz, f));
                var guard = new ChordGuard(lifts);
                foreach (PlanRun r in plan.Runs)
                    guard.Add(r.Pts, 0, r.Pts.Count - 1);
                // at the base: green ground uphill from its stations and the run-outs that come home to it
                EasyReach er = EasyUp(g, BaseOrigins(g, plan, lifts, members, sg), avoid, LearnWay, lines);
                float mid = 0.5f * (LearnMinRise + LearnMaxRise);
                float Rise(EasyReach e, int c) => g.Hr[c] - g.Hr[e.Root[c]];
                List<int> cands = er.Cost.Where(kv => er.Len[kv.Key] <= LearnRouteMax && Rise(er, kv.Key) >= LearnMinRise && Rise(er, kv.Key) <= LearnMaxRise)
                    .OrderBy(kv => kv.Value - Math.Min(er.Len[kv.Key], 900f) + 3f * Math.Abs(Rise(er, kv.Key) - mid)).Select(kv => kv.Key).ToList();
                if (TryLearningChair(g, plan, lifts, members, er, cands, null, avoid, guard, sg, km))
                {
                    added++;
                    continue;
                }
                // or below the top of one of the base's lifts (a gondola first): a learning area the beginners ride up to, its runs
                // starting where they get off
                if (!LearnHigh)
                    continue;
                foreach (int L in members.OrderByDescending(L => Aerial(lifts[L].Type)).ThenBy(L => lifts[L].Rise).ToList())
                {
                    List<int> origins = BelowTop(g, lifts, lifts[L], avoid);
                    if (origins.Count == 0)
                        continue;
                    EasyReach eh = EasyUp(g, origins, avoid, LearnWay, lines);
                    float2 top = lifts[L].Top.xz;
                    List<int> high = eh.Len.Keys.Where(c => eh.Len[c] <= LearnRouteMax && Rise(eh, c) >= LearnMinRise && Rise(eh, c) <= LearnMaxRise
                        && math.distancesq(g.World(c), top) < LearnBelowTopM * LearnBelowTopM).OrderByDescending(c => eh.Len[c]).ToList();
                    if (TryLearningChair(g, plan, lifts, members, eh, high, lifts[L], avoid, guard, sg, km))
                    {
                        added++;
                        break;
                    }
                }
            }
            if (added > 0)
                LinkByRoutes(g, plan, lifts);
            return added;
        }

        /// <summary>Scorecard v5's beginner terrain for a base: the km of green runs (groomed, served, by the ground) a beginner gets to
        /// from the base's lifts on green skiing, when that skiing brings them back to a lift (a loop to lap); 0 without a loop.</summary>
        private static float LearnKm(SkiGraph sg, Plan plan, List<Lift> lifts, IEnumerable<int> baseLifts)
        {
            var easy = new Dictionary<int, HashSet<int>>();
            HashSet<int> E(int L) => easy.TryGetValue(L, out HashSet<int> s) ? s : easy[L] = EasyFrom(sg, lifts, L, SlopeBand.Green);
            var seen = new HashSet<int>();
            var q = new Queue<int>();
            foreach (int L in baseLifts)
                if ((Aerial(lifts[L].Type) || E(L).Count > 0) && seen.Add(L))
                    q.Enqueue(L);
            bool loop = false;
            var runs = new HashSet<int>();
            while (q.Count > 0)
                foreach (int i in E(q.Dequeue()))
                {
                    if (i < sg.Runs && !plan.Runs[i].Graded && plan.Runs[i].Surface == RunKind.Groomed && sg.Band[i] == SlopeBand.Green
                        && sg.Reached[i] && sg.Returns[i])
                        runs.Add(i);
                    foreach (int M in sg.Delivers[i])
                    {
                        loop = true;
                        if ((Aerial(lifts[M].Type) || E(M).Count > 0) && seen.Add(M))
                            q.Enqueue(M);
                    }
                }
            return loop ? runs.Sum(i => RunLength(plan.Runs[i].Pts)) / 1000f : 0f;
        }

        /// <summary>A beginner chair from a pad at the base to the top of an easy route up its gentle ground, the route its first green
        /// run, then more green runs from its top. The first chair that keeps the lift network whole wins; else the first that fits.</summary>
        private static bool TryLearningChair(Grid g, Plan plan, List<Lift> lifts, List<int> members, EasyReach er, List<int> cands, Lift below,
            PointHash avoid, ChordGuard guard, SkiGraph before, float kmBefore)
        {
            var bottoms = new HashSet<int>(lifts.Select(lf => lf.BottomCell));
            var topsSeen = new HashSet<int>();
            var why = new int[8];
            var rwhy = new int[4];
            int checks = 0, netChecks = 0;
            (Lift Lift, List<PlanRun> Runs) fallback = (null, null);
            int shareBefore = before.BestSize;
            foreach (int x in cands)
            {
                if (++checks > 3000 || netChecks >= 5)
                    break;
                int top = FlatCell(g, x, 20f);
                if (top < 0 || math.distance(g.World(x), g.World(top)) > 60f || !topsSeen.Add(top))
                {
                    why[0]++;
                    continue;
                }
                float2 t = g.World(top);
                if (lifts.Any(lf => math.distance(lf.Top.xz, t) < LaySameSpotM + 25f || math.distance(lf.Bottom.xz, t) < LaySameSpotM + 25f))
                {
                    why[0]++;
                    continue;
                }
                // the bottom station: the pad the route ends at, or one beside it, or one on the route's lower part; the route passes it
                int root = er.Root[x];
                List<float2> way = er.Route(x).ConvertAll(c => g.World(c));
                var froms = new List<int>();
                if (!bottoms.Contains(root))
                    froms.Add(root);
                froms.AddRange(PadsNear(g, root, 25f, 60f, 8));
                List<int> along = er.Route(x);
                for (int i = along.Count - 1; i >= 0; i -= 4)
                    if (g.Hr[top] - g.Hr[along[i]] >= LearnMinRise && RawGrade(g, g.World(along[i])) <= StationMax)
                        froms.Add(along[i]);
                int from = -1, fail = 7;
                float2 b = default;
                float len = 0f, rise = 0f;
                foreach (int fc in froms.Distinct())
                {
                    b = g.World(fc);
                    if ((below == null && !members.Any(L => math.distance(lifts[L].Bottom.xz, b) < 390f)) || !way.Any(p => math.distancesq(p, b) < 52f * 52f)
                        || lifts.Any(lf => math.distance(lf.Bottom.xz, b) < LaySameSpotM + 25f || math.distance(lf.Top.xz, b) < LaySameSpotM + 25f))
                    {
                        fail = Math.Min(fail, 6);
                        continue;
                    }
                    float bh = g.Hr[fc], th = g.Hr[top], plan2 = math.distance(b, t);
                    rise = th - bh;
                    len = math.sqrt(plan2 * plan2 + rise * rise);
                    if (len < LearnMinLen || rise < LearnMinRise || rise > LearnMaxRise || rise / math.max(1f, plan2) > LearnChairGrade)
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
                    if (lifts.Any(lf => SegCrossScore(lf.Bottom.xz, lf.Top.xz, b, t) || ParallelLines(b, t, lf.Bottom.xz, lf.Top.xz))
                        || UnderRope(g, lifts, top, fc))
                    {
                        fail = Math.Min(fail, 3);
                        continue;
                    }
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
                if (from < 0)
                {
                    why[fail]++;
                    continue;
                }
                var rw = new int[4];
                List<float2> route = GreenRoute(g, plan, lifts, members, er, x, avoid, guard, rw, out float soft, out bool graded);
                if (route == null || soft > 0f || graded)
                {
                    why[4]++;
                    for (int k = 0; k < 4; k++)
                        rwhy[k] += rw[k];
                    if (graded)
                        why[7]++;
                    continue;
                }
                var chord = new ChordGuard(new List<Lift>());
                chord.AddLift(b, t);
                foreach (PlanRun r in plan.Runs)
                    chord.Add(r.Pts, 0, r.Pts.Count - 1);
                if (!chord.Fits(new List<float2> { t }, 0, 0))
                {
                    why[5]++;
                    continue;
                }
                var lift = new Lift
                {
                    Id = lifts.Count + 1, Type = "beginner", TopCell = top, BottomCell = from, Kind = "beginner",
                    Top = new float3(t.x, g.H[top], t.y), Bottom = new float3(b.x, g.H[from], b.y),
                };
                lift.Rise = lift.Top.y - lift.Bottom.y;
                lift.Length = math.distance(lift.Top, lift.Bottom);
                var runs = new List<PlanRun> { new PlanRun { Pts = route, Band = SlopeBand.Green, By = lift, Kind = "learning" } };
                lifts.Add(lift);
                plan.Runs.AddRange(runs);
                // more green runs from the top while the base is short of green km, each away from the ones before
                MoreLearningRuns(g, plan, lifts, members, lift, runs, kmBefore);
                netChecks++;
                SkiGraph after = Ski(g, plan, lifts);
                if (after.BestSize >= shareBefore + 1)
                {
                    plan.Log.Add($"learning area {(below != null ? $"below L{below.Id}'s top" : $"at L{lifts[members[0]].Id}'s base")}: beginner chair {len:F0} m / {rise:F0} m up, " +
                        $"{runs.Count} green run(s) {runs.Sum(r => RunLength(r.Pts)):F0} m");
                    return true;
                }
                lifts.RemoveAt(lifts.Count - 1);
                foreach (PlanRun r in runs)
                    plan.Runs.Remove(r);
                if (fallback.Lift == null)
                    fallback = (lift, runs);
            }
            if (fallback.Lift != null)
            {
                lifts.Add(fallback.Lift);
                plan.Runs.AddRange(fallback.Runs);
                plan.Log.Add($"learning area {(below != null ? $"below L{below.Id}'s top" : $"at L{lifts[members[0]].Id}'s base")} (off the network): beginner chair {fallback.Lift.Length:F0} m / " +
                    $"{fallback.Lift.Rise:F0} m up, {fallback.Runs.Count} green run(s) {fallback.Runs.Sum(r => RunLength(r.Pts)):F0} m");
                return true;
            }
            plan.Log.Add($"learning area {(below != null ? $"below L{below.Id}'s top" : $"at L{lifts[members[0]].Id}")}: {cands.Count} top(s) in the rise range; rejected: pad {why[0]}, norms {why[1]}, " +
                $"clearance {why[2]}, crossing / rope {why[3]}, route {why[4]} (not green {rwhy[0]}, run rules {rwhy[1]}, crowds {rwhy[2]}, lift line {rwhy[3]}, graded {why[7]}), chord on run {why[5]}, base station {why[6]}; base had {kmBefore:F2} km; easy work {EasyWork}");
            return false;
        }

        /// <summary>Up to two more green runs from a learning chair's top to its bottom (or the base), each a route of its own away from
        /// the runs before (crowding), while the base is short of green km.</summary>
        private static void MoreLearningRuns(Grid g, Plan plan, List<Lift> lifts, List<int> members, Lift chair, List<PlanRun> runs, float kmBefore)
        {
            for (int k = 0; k < 2 && kmBefore + runs.Sum(r => RunLength(r.Pts)) / 1000f < LearnFullKm; k++)
            {
                var avoid = new PointHash();
                foreach (PlanRun r in plan.Runs)
                    foreach (float2 p in r.Pts)
                        avoid.Add(p);
                var lines = new PointHash();
                foreach (Lift lf in lifts)
                    for (float f = 0.1f; f <= 0.9f; f += 10f / math.max(10f, math.distance(lf.Bottom.xz, lf.Top.xz)))
                        lines.Add(math.lerp(lf.Bottom.xz, lf.Top.xz, f));
                // the chair's runs so far push the next one away (past where they start together)
                foreach (PlanRun r in runs)
                {
                    float at = 0f;
                    for (int i = 1; i < r.Pts.Count; i++)
                    {
                        at += math.distance(r.Pts[i - 1], r.Pts[i]);
                        if (at > 100f)
                            lines.Add(r.Pts[i]);
                    }
                }
                var origins = new List<int> { chair.BottomCell };
                origins.AddRange(PadsNear(g, chair.BottomCell, 20f, 40f, 8));
                EasyReach er = EasyUp(g, origins, avoid, LearnWay, lines, 8f);
                var guard = new ChordGuard(lifts);
                foreach (PlanRun r in plan.Runs)
                    guard.Add(r.Pts, 0, r.Pts.Count - 1);
                float2 t = chair.Top.xz;
                float th = RawH(g, t);
                PlanRun best = null;
                float bestLen = 0f;
                int tries = 0;
                var mw = new int[6];
                foreach (int x in er.Len.Keys.Where(c => math.distancesq(g.World(c), t) < LearnTopM * LearnTopM && g.Hr[c] >= th - 12f)
                    .OrderByDescending(c => er.Len[c]))
                {
                    if (++tries > 300)
                        break;
                    if (er.Len[x] < LearnRunMinM || er.Len[x] <= bestLen)
                    {
                        mw[0]++;
                        continue;
                    }
                    // green by the ground and the run rules; off the other runs but where the runs of this chair start together (the
                    // first 120 m) and come home (the last 150 m), as the scorecard reads crowding
                    List<float2> route = null;
                    List<int> cells = er.Route(x);
                    foreach (List<float2> pl in new[] { Polyline(g, cells, false), cells.ConvertAll(c => g.World(c)) })
                        if (GroundBand(g, pl) == SlopeBand.Green && SanitizeRun(g, pl) is { } ok && ok.Count >= Densify(pl).Count)
                        {
                            route = ok;
                            break;
                        }
                    if (route == null || RunLength(route) <= bestLen || !route.Skip(1).Any(p => math.distancesq(p, chair.Bottom.xz) < 50f * 50f)
                        || !guard.FitsAdding(route, 0, route.Count - 1))
                    {
                        mw[route == null ? 1 : RunLength(route) <= bestLen ? 2 : !route.Skip(1).Any(p => math.distancesq(p, chair.Bottom.xz) < 50f * 50f) ? 3 : 4]++;
                        continue;
                    }
                    float rl = RunLength(route), at = 0f;
                    int counted = 0, crowd = 0;
                    for (int i = 1; i < route.Count; i++)
                    {
                        at += math.distance(route[i - 1], route[i]);
                        if (at < 120f || rl - at < 150f)
                            continue;
                        counted++;
                        if (avoid.Near(route[i], 40f))
                            crowd++;
                    }
                    if (crowd > 0.2f * Math.Max(1, counted))
                    {
                        mw[5]++;
                        continue;
                    }
                    best = new PlanRun { Pts = route, Band = SlopeBand.Green, By = chair, Kind = "learning" };
                    bestLen = RunLength(route);
                }
                if (Environment.GetEnvironmentVariable("RF_DEBUG") == "1" || true)
                    plan.Log.Add($"  more learning runs: {er.Len.Count} reached, {tries} tried near the top; short {mw[0]}, not green {mw[1]}, shorter {mw[2]}, " +
                        $"misses the bottom {mw[3]}, lift line {mw[4]}, crowds {mw[5]}{(best != null ? $"; added {RunLength(best.Pts):F0} m" : "")}");
                if (best == null)
                    break;
                runs.Add(best);
                plan.Runs.Add(best);
            }
        }
    }
}
