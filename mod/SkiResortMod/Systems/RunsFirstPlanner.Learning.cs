// RunsFirstPlanner learning areas (scorecard v5): a base's beginners learn on green runs, not on cat tracks. A base means two or more
// lift bottoms within 400 m low on the mountain, as the scorecard reads them. If the beginners of a base get to under 0.8 km
// of green runs, the base gets a learning area. The area has these parts:
// - a beginner chair (a short fixed-grip, 30-150 m of rise: a beginner should not face 300 m of vertical first) from a pad at
//   the base up its gentle ground
// - up to three green runs from its top back to its bottom.
// The chair keeps the other lifts' rules (flat stations, the rope clear of the ground, no crossing, 40 m off the other ropes,
// no station under a rope, its chord off the runs). Its runs are green by the ground under them, groomed, and keep away from
// the other runs.

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class RunsFirstPlanner
    {
        private float LearnMinRise => O.LearnMinRise;
        private float LearnMaxRise => O.LearnMaxRise;
        private float LearnMinLen => O.LearnMinLen;
        private float LearnRouteMax => O.LearnRouteMax;
        private const float LearnFullKm = 0.85f, LearnChairGrade = 0.45f, LearnTopM = 90f, LearnRunMinM = 200f, LearnBelowTopM = 130f;
        private float LearnCrowd => O.LearnCrowd;
        private float LearnFallDeg => O.LearnFallDeg;   // a learning chair climbs its slope's fall line too
        private bool LearnHigh => O.LearnHigh;

        private const float LearnChordMax = 0.25f;

        /// <summary>The scorecard's chord rule: the share of a chord's middle (15-85 %, every 10 m) within 20 m of a run or connector,
        /// a learning run from near the top back to the bottom station excepted (a learning chair rides over its own green run).
        /// <paramref name="extra"/> are runs about to join the plan.</summary>
        private static float ChordOnRuns(PlanDraft plan, float2 b, float2 t, params List<float2>[] extra)
        {
            bool Own(List<float2> p) => math.distance(p[0], t) < 145f && math.distance(p[p.Count - 1], b) < 57f;
            var sets = new List<List<float2>>();
            foreach (PlanRun r in plan.Runs)
                if (r.Pts.Count >= 2 && !(r.Kind == "learning" && Own(r.Pts)))
                    sets.Add(Densify(r.Pts));
            foreach (List<float2> c in plan.Connectors)
                if (c.Count >= 2)
                    sets.Add(Densify(c));
            foreach (List<float2> e in extra)
                if (e != null && e.Count >= 2 && !Own(e))
                    sets.Add(Densify(e));
            float plan2 = math.distance(b, t);
            int n = 0, near = 0;
            for (float f = 0.15f; f <= 0.85f; f += 10f / math.max(10f, plan2), n++)
            {
                float2 q = math.lerp(b, t, f);
                if (sets.Any(s => s.Any(p => math.distancesq(p, q) < 23f * 23f)))
                    near++;
            }
            return n > 0 ? near / (float)n : 0f;
        }

        /// <summary>Pads for a learning chair's bottom station below a lift's top: green ground 160-700 m away, 35-130 m lower, off the
        /// runs.</summary>
        private List<int> BelowTop(Grid g, List<Lift> lifts, Lift lf, PointHash avoid)
        {
            var o = new List<int>();
            float2 t = lf.Top.xz;
            float th = RawH(g, t);
            for (float r = 160f; r <= 700f && o.Count < 400; r += 25f)
                for (float a = 0f; a < 2f * (float)Math.PI; a += 25f / r)
                {
                    float2 p = t + r * new float2((float)Math.Cos(a), (float)Math.Sin(a));
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

        /// <summary>Pads for a learning chair's bottom station beside an aerial lift's top: gentle ground 50-300 m away, 25 m lower to
        /// 8 m higher (a flat cat track each way), off the runs and the lift lines.</summary>
        private static List<int> AboveTop(Grid g, List<Lift> lifts, Lift lf, PointHash avoid)
        {
            var o = new List<int>();
            float2 t = lf.Top.xz;
            float th = RawH(g, t);
            for (float r = 50f; r <= 300f && o.Count < 300; r += 20f)
                for (float a = 0f; a < 2f * (float)Math.PI; a += 20f / r)
                {
                    float2 p = t + r * new float2((float)Math.Cos(a), (float)Math.Sin(a));
                    int c = CellOf(g, p);
                    if (c < 0 || !g.In[c] || float.IsNaN(g.Hr[c]))
                        continue;
                    float dh = g.Hr[c] - th;
                    if (dh >= -25f && dh <= 8f && RawGrade(g, g.World(c)) <= 0.22f && !avoid.Near(g.World(c), 30f)
                        && !lifts.Any(o2 => SegDist(g.World(c), o2.Bottom.xz, o2.Top.xz) < 45f))
                        o.Add(c);
                }
            return o;
        }

        /// <summary>A straight cat track between two stations where the ground allows one as the scorecard reads a connector (no climb
        /// past 4.5 m in 50 m, no 30 m pitch past 0.5, inside the region). Returns whether it was added.</summary>
        private static bool StationTrack(Grid g, PlanDraft plan, float2 from, float2 to)
        {
            float len = math.distance(from, to);
            if (len < 20f || len > 400f)
                return false;
            var pts = new List<float2>();
            int k = Math.Max(1, (int)Math.Ceiling(len / 10f));
            for (int i = 0; i <= k; i++)
                pts.Add(math.lerp(from, to, i / (float)k));
            var h = pts.ConvertAll(p => RawH(g, p));
            if (h.Any(float.IsNaN) || pts.Any(p => CellOf(g, p) < 0 || !g.In[CellOf(g, p)]))
                return false;
            for (int i = 0; i + 5 < h.Count; i++)
                if (h[i + 5] - h[i] > 4f)
                    return false;
            for (int i = 0; i + 3 < h.Count; i++)
                if ((h[i] - h[i + 3]) / 30f > 0.45f)
                    return false;
            plan.Connectors.Add(pts);
            return true;
        }

        // off by default (RF_LEARNABOVE=1). A learning area on a green bench just above a base lift top found no site that the other
        // modes missed on the five sets (no plan changed). It cost planning time
        private bool LearnAbove => O.LearnAbove;
        private EasyLimits LearnWayShort => new EasyLimits(0.22f, GreenRawPitch, 0.40f, 7f, 0.08f, 1600f, false, O.LearnGround);

        // off by default (RF_LEARNCONVERT=1): turning a short base lift into the beginner chair changed no plan on the five sets
        private bool LearnConvert => O.LearnConvert;

        /// <summary>A short lift (30-150 m of rise, not aerial) becomes a beginner chair, with up to two green runs from its top back
        /// to its bottom, where that adds green km for the base. The lift qualifies when its bottom is at the base (within 390 m of a
        /// base lift's bottom) or beside the top of the base's gondola or tram (within 300 m), and green ground runs from its bottom
        /// to its top. Returns whether one did.</summary>
        private bool ConvertToLearning(Grid g, PlanDraft plan, List<Lift> lifts, List<int> members, float kmBefore)
        {
            var aerials = members.Where(L => Aerial(lifts[L].Type)).Select(L => lifts[L]).ToList();
            var cands = new List<(Lift Lf, Lift Via)>();
            foreach (Lift lf in lifts)
            {
                float rise = RawH(g, lf.Top.xz) - RawH(g, lf.Bottom.xz), plan2 = math.distance(lf.Bottom.xz, lf.Top.xz);
                float len = math.sqrt(plan2 * plan2 + rise * rise);
                if (Aerial(lf.Type) || lf.Type == "beginner" || rise < LearnMinRise || rise > 145f || len < 150f || rise / math.max(1f, plan2) > 0.75f)
                    continue;
                Lift via = aerials.Where(a => a != lf && math.distance(a.Top.xz, lf.Bottom.xz) < 700f).OrderBy(a => math.distance(a.Top.xz, lf.Bottom.xz)).FirstOrDefault();
                if (via != null || members.Any(L => lifts[L] != lf && math.distance(lifts[L].Bottom.xz, lf.Bottom.xz) < 390f) || members.Contains(lifts.IndexOf(lf)))
                    cands.Add((lf, via));
            }
            if (O.DebugConvert)
                foreach (Lift lf in lifts)
                    plan.Log.Add($"  convert? L{lf.Id} {lf.Type} rise {RawH(g, lf.Top.xz) - RawH(g, lf.Bottom.xz):F0} plan {math.distance(lf.Bottom.xz, lf.Top.xz):F0} aerial top {(aerials.Count > 0 ? aerials.Min(x => math.distance(x.Top.xz, lf.Bottom.xz)) : -1):F0} members {string.Join("/", members.Select(L => lifts[L].Id))}");
            plan.Log.Add($"  convert: {cands.Count} short lift(s) at the base or beside an aerial top: {string.Join(", ", cands.Select(c => $"L{c.Lf.Id}{(c.Via != null ? $" via L{c.Via.Id}" : "")}"))}");
            foreach (var (lf, via) in cands.OrderBy(c => c.Lf.Rise))
            {
                var runs = new List<PlanRun>();
                MoreLearningRuns(g, plan, lifts, members, lf, runs, kmBefore);
                if (runs.Count == 0 || ChordOnRuns(plan, lf.Bottom.xz, lf.Top.xz) > LearnChordMax)
                {
                    foreach (PlanRun r in runs)
                        plan.Runs.Remove(r);
                    continue;
                }
                var tracks = new List<List<float2>>();
                if (via != null)
                    foreach ((float2 a, float2 b) in new[] { (via.Top.xz, lf.Bottom.xz), (lf.Bottom.xz, via.Top.xz) })
                        if (StationTrack(g, plan, a, b))
                            tracks.Add(plan.Connectors[plan.Connectors.Count - 1]);
                (string type, string kind) = (lf.Type, lf.Kind);
                (lf.Type, lf.Kind) = ("beginner", "beginner");
                SkiGraph sg = Ski(g, plan, lifts);
                int[] root = V3Bases(g, lifts, out HashSet<int> bases);
                float km = LearnKm(sg, plan, lifts, Enumerable.Range(0, lifts.Count).Where(L => root[L] == root[members[0]]));
                if (km > kmBefore + 0.05f)
                {
                    plan.Log.Add($"learning area: L{lf.Id} ({type} {lf.Length:F0} m / {lf.Rise:F0} m up) becomes the beginner chair, {runs.Count} green run(s) " +
                        $"{runs.Sum(r => RunLength(r.Pts)):F0} m{(via != null ? $", {tracks.Count} cat track(s) to L{via.Id}'s top" : "")}; base {kmBefore:F2} -> {km:F2} km");
                    return true;
                }
                (lf.Type, lf.Kind) = (type, kind);
                foreach (PlanRun r in runs)
                    plan.Runs.Remove(r);
                foreach (List<float2> t in tracks)
                    plan.Connectors.Remove(t);
            }
            return false;
        }

        /// <summary>Learning areas for the bases short of green runs. Returns the chairs added.</summary>
        private int AddLearningAreas(Grid g, PlanDraft plan, List<Lift> lifts)
        {
            int added = 0;
            var tried = new HashSet<int>();
            for (int pass = 0; pass < 4; pass++)
            {
                int[] root = V3Bases(g, lifts, out HashSet<int> bases, out List<int> baseOrder);
                SkiGraph sg = Ski(g, plan, lifts);
                List<int> members = null;
                float km = 0f;
                foreach (int b in baseOrder)
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
                List<int> cands = er.CostInOrder().Where(kv => er.Len[kv.Key] <= LearnRouteMax && Rise(er, kv.Key) >= LearnMinRise && Rise(er, kv.Key) <= LearnMaxRise)
                    .OrderBy(kv => kv.Value - Math.Min(er.Len[kv.Key], 900f) + 3f * Math.Abs(Rise(er, kv.Key) - mid)).Select(kv => kv.Key).ToList();
                // on the fall line first. A learning chair across its gentle slope beats a base without one
                if (TryLearningChair(g, plan, lifts, members, er, cands, null, avoid, guard, sg, km, LearnFallDeg)
                    || TryLearningChair(g, plan, lifts, members, er, cands, null, avoid, guard, sg, km, 180f))
                {
                    added++;
                    continue;
                }
                // or below the top of one of the base's lifts (a gondola first). This is a learning area the beginners ride up to, its runs
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
                    List<int> high = eh.Order.Where(c => eh.Len[c] <= LearnRouteMax && Rise(eh, c) >= LearnMinRise && Rise(eh, c) <= LearnMaxRise
                        && math.distancesq(g.World(c), top) < LearnBelowTopM * LearnBelowTopM).OrderByDescending(c => eh.Len[c]).ToList();
                    if (TryLearningChair(g, plan, lifts, members, eh, high, lifts[L], avoid, guard, sg, km, LearnFallDeg)
                        || TryLearningChair(g, plan, lifts, members, eh, high, lifts[L], avoid, guard, sg, km, 180f))
                    {
                        added++;
                        goto next;
                    }
                }
                // or above the top of the base's gondola or tram (a summit plateau). Beginners ride it up and down. The learning chair rises
                // from beside its top station, with a flat cat track each way between the two
                if (!LearnAbove)
                    continue;
                foreach (int L in members.Where(L => Aerial(lifts[L].Type)).ToList())
                {
                    List<int> origins = AboveTop(g, lifts, lifts[L], avoid);
                    if (origins.Count == 0)
                        continue;
                    EasyReach ea = EasyUp(g, origins, avoid, LearnWayShort, lines);
                    List<int> up = ea.CostInOrder().Where(kv => ea.Len[kv.Key] <= LearnRouteMax && Rise(ea, kv.Key) >= LearnMinRise && Rise(ea, kv.Key) <= LearnMaxRise)
                        .OrderBy(kv => kv.Value - Math.Min(ea.Len[kv.Key], 900f) + 3f * Math.Abs(Rise(ea, kv.Key) - mid)).Select(kv => kv.Key).ToList();
                    if (TryLearningChair(g, plan, lifts, members, ea, up, lifts[L], avoid, guard, sg, km, LearnFallDeg, true)
                        || TryLearningChair(g, plan, lifts, members, ea, up, lifts[L], avoid, guard, sg, km, 180f, true))
                    {
                        Lift chair = lifts[lifts.Count - 1];
                        int tracks = (StationTrack(g, plan, lifts[L].Top.xz, chair.Bottom.xz) ? 1 : 0) + (StationTrack(g, plan, chair.Bottom.xz, lifts[L].Top.xz) ? 1 : 0);
                        plan.Log.Add($"  {tracks} cat track(s) between L{lifts[L].Id}'s top and the learning chair");
                        added++;
                        goto next;
                    }
                }
                // or a short lift already there on green ground becomes the learning chair (at the base, or beside the top of the base's
                // gondola or tram). It gets green runs from its top back to its bottom
                if (LearnConvert && ConvertToLearning(g, plan, lifts, members, km))
                    added++;
            next:;
            }
            if (added > 0)
                LinkByRoutes(g, plan, lifts);
            return added;
        }

        /// <summary>Scorecard v5's beginner terrain for a base: the km of green runs (groomed, served, by the ground) a beginner gets to
        /// from the base's lifts on green skiing, when that skiing brings them back to a lift (a loop to lap). It is 0 without a loop.</summary>
        private static float LearnKm(SkiGraph sg, PlanDraft plan, List<Lift> lifts, IEnumerable<int> baseLifts)
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
        /// run, then more green runs from its top. The first chair that keeps the lift network whole wins. Otherwise the winner is the
        /// first that fits.</summary>
        private bool TryLearningChair(Grid g, PlanDraft plan, List<Lift> lifts, List<int> members, EasyReach er, List<int> cands, Lift below,
            PointHash avoid, ChordGuard guard, SkiGraph before, float kmBefore, float fallMax, bool above = false)
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
                // the bottom station: the pad the route ends at, or one beside it, or one on the route's lower part. The route passes it
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
                    if (len < LearnMinLen || rise < LearnMinRise || rise > LearnMaxRise || rise / math.max(1f, plan2) > LearnChairGrade
                        || FallDeg(g, b, t) > fallMax)
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
                    if (lifts.Any(lf => SegCrossScore(lf.Bottom.xz, lf.Top.xz, b, t) || ParallelLines(b, t, lf.Bottom.xz, lf.Top.xz)
                            || StackedLines(b, t, lf.Bottom.xz, lf.Top.xz))
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
                // the route ends at the chair's bottom (the chair rides over its own run). The stretch on to the base is a run of its own
                List<float2> tail = null;
                if (math.distance(route[route.Count - 1], b) >= 57f)
                {
                    int k = Enumerable.Range(1, route.Count - 1).OrderBy(i => math.distancesq(route[i], b)).First();
                    if (math.distance(route[k], b) < 50f && k < route.Count - 1)
                    {
                        List<float2> rest = route.GetRange(k, route.Count - k);
                        route = route.GetRange(0, k + 1);
                        if (RunLength(rest) >= 80f && RawH(g, rest[0]) - RawH(g, rest[rest.Count - 1]) > 2f)
                            tail = rest;
                    }
                }
                if (RunLength(route) < 150f)
                {
                    why[4]++;
                    continue;
                }
                if (ChordOnRuns(plan, b, t, route, tail) > LearnChordMax)
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
                if (tail != null)
                    runs.Add(new PlanRun { Pts = tail, Band = SlopeBand.Green, By = lift, Kind = "learning" });
                lifts.Add(lift);
                plan.Runs.AddRange(runs);
                // more green runs from the top while the base is short of green km, each away from the ones before
                MoreLearningRuns(g, plan, lifts, members, lift, runs, kmBefore);
                while (runs.Count > 1 && ChordOnRuns(plan, b, t) > LearnChordMax)
                {
                    plan.Runs.Remove(runs[runs.Count - 1]);
                    runs.RemoveAt(runs.Count - 1);
                }
                netChecks++;
                SkiGraph after = Ski(g, plan, lifts);
                if (after.BestSize >= shareBefore + 1)
                {
                    plan.Log.Add($"learning area {(below != null ? $"{(above ? "above" : "below")} L{below.Id}'s top" : $"at L{lifts[members[0]].Id}'s base")}: beginner chair {len:F0} m / {rise:F0} m up, " +
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
                plan.Log.Add($"learning area {(below != null ? $"{(above ? "above" : "below")} L{below.Id}'s top" : $"at L{lifts[members[0]].Id}'s base")} (off the network): beginner chair {fallback.Lift.Length:F0} m / " +
                    $"{fallback.Lift.Rise:F0} m up, {fallback.Runs.Count} green run(s) {fallback.Runs.Sum(r => RunLength(r.Pts)):F0} m");
                return true;
            }
            plan.Log.Add($"learning area {(below != null ? $"{(above ? "above" : "below")} L{below.Id}'s top" : $"at L{lifts[members[0]].Id}")}: {cands.Count} top(s) in the rise range; rejected: pad {why[0]}, norms {why[1]}, " +
                $"clearance {why[2]}, crossing / rope {why[3]}, route {why[4]} (not green {rwhy[0]}, run rules {rwhy[1]}, crowds {rwhy[2]}, lift line {rwhy[3]}, graded {why[7]}), chord on run {why[5]}, base station {why[6]}; base had {kmBefore:F2} km; easy work {EasyWork}");
            return false;
        }

        /// <summary>Up to two more green runs from a learning chair's top to its bottom (or the base), each a route of its own away from
        /// the runs before (crowding), while the base is short of green km.</summary>
        private void MoreLearningRuns(Grid g, PlanDraft plan, List<Lift> lifts, List<int> members, Lift chair, List<PlanRun> runs, float kmBefore)
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
                foreach (int x in er.Order.Where(c => math.distancesq(g.World(c), t) < LearnTopM * LearnTopM && g.Hr[c] >= th - 12f)
                    .OrderByDescending(c => er.Len[c]))
                {
                    if (++tries > 300)
                        break;
                    if (er.Len[x] < LearnRunMinM || er.Len[x] <= bestLen)
                    {
                        mw[0]++;
                        continue;
                    }
                    // green by the ground and the run rules. Off the other runs, as the scorecard reads crowding. The exception: where the runs of
                    // this chair start together (the first 120 m) and come home (the last 150 m)
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
                    int counted = 0, crowd = 0, close = 0;
                    for (int i = 1; i < route.Count; i++)
                    {
                        at += math.distance(route[i - 1], route[i]);
                        if (at < 120f || rl - at < 150f)
                            continue;
                        counted++;
                        if (avoid.Near(route[i], 40f))
                            crowd++;
                        if (avoid.Near(route[i], 22f))
                            close++;
                    }
                    // a learning slope is wide. A second trail may share some of it (the crowding costs less than a base without its green km).
                    // It may not lie on the first
                    if (crowd > LearnCrowd * Math.Max(1, counted) || close > 0.15f * Math.Max(1, counted))
                    {
                        mw[5]++;
                        continue;
                    }
                    best = new PlanRun { Pts = route, Band = SlopeBand.Green, By = chair, Kind = "learning" };
                    bestLen = RunLength(route);
                }
                if (O.Debug || true)
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
