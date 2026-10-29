// RunsFirst regrade (round v3): the traverse from a lift top to the runs it opens falls at a run's grade where the reach let it, and
// scorecard v3 reads such a piece as a diagonal blue run: across a face of fall-line runs it crowds every run it crosses, the short
// pieces between forks are stubs, and one under a lift line lies on the chord. The grading tool cuts the traverse instead as a green
// cat track: a bench that winds across the face between the same two points. The pass searches that bench for every diagonal
// piece, then takes them in the order of the crowding and stubs they clear per meter of track, while the connector km stay inside
// the economy share of the served run km; a piece no bench fits stays a diagonal run.

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
        private static readonly float RegradeStretch = Env("RF_RGSTRETCH", 1.8f);   // a bench at most this many times the piece's length (plus 100 m)
        private const float RegradeShare = 0.33f;    // connector km kept under this share of the run km (the scorecard's 0.35, less a margin)

        /// <summary>The planner's read of the economy term: connector km (green cat tracks, graded greens) and run km (runs and
        /// diagonal runs, all taken as served).</summary>
        private static (float Conn, float Run) EconomyKm(Grid g, Plan plan)
        {
            float conn = 0f, run = 0f;
            foreach (PlanRun r in plan.Runs)
                if (r.Graded)
                    conn += RunLength(r.Pts) / 1000f;
                else
                    run += RunLength(r.Pts) / 1000f;
            foreach (List<float2> c in plan.Connectors)
                if (TrackClass(g, c).Run)
                    run += RunLength(c) / 1000f;
                else
                    conn += RunLength(c) / 1000f;
            return (conn, run);
        }

        /// <summary>Cuts diagonal traverse pieces as green cat tracks where a bench fits; returns the pieces regraded.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static int RegradeTraverses(Grid g, Plan plan, List<Lift> lifts)
        {
            // the run samples (runs by the ground, diagonal pieces) for the crowding estimate, and the km on each side of the economy
            var runEls = new List<(List<float2> P, int Conn)>();
            var (connKm, runKm) = EconomyKm(g, plan);
            foreach (PlanRun r in plan.Runs)
                if (!r.Graded)
                    runEls.Add((Densify(r.Pts), -1));
            var diag = new List<int>();
            for (int ci = 0; ci < plan.Connectors.Count; ci++)
                if (TrackClass(g, plan.Connectors[ci]).Run)
                {
                    diag.Add(ci);
                    runEls.Add((Densify(plan.Connectors[ci]), ci));
                }
            if (diag.Count == 0)
                return 0;
            var hash = new Dictionary<long, List<(float2 P, int El)>>();
            long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;
            for (int e = 0; e < runEls.Count; e++)
                foreach (float2 p in runEls[e].P)
                {
                    long k = Key((int)Math.Floor(p.x / 40f), (int)Math.Floor(p.y / 40f));
                    if (!hash.TryGetValue(k, out var l))
                        hash[k] = l = new List<(float2, int)>();
                    l.Add((p, e));
                }
            // the samples of a piece within 40 m of another run (outside its junction zones), and of other runs within 40 m of it
            int Crowd(int e)
            {
                List<float2> P = runEls[e].P;
                var near = new HashSet<(int El, int K)>();
                int own = 0;
                for (int k = 0; k < P.Count; k++)
                {
                    int cx = (int)Math.Floor(P[k].x / 40f), cz = (int)Math.Floor(P[k].y / 40f);
                    bool hit = false;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                            if (hash.TryGetValue(Key(cx + dx, cz + dz), out var l))
                                foreach (var (q, o) in l)
                                {
                                    if (o == e || math.distancesq(q, P[k]) >= 1600f)
                                        continue;
                                    List<float2> Q = runEls[o].P;
                                    if (math.distancesq(Q[0], P[k]) >= 1e4f && math.distancesq(Q[Q.Count - 1], P[k]) >= 1e4f)
                                        hit = true;
                                    if (math.distancesq(P[0], q) >= 1e4f && math.distancesq(P[P.Count - 1], q) >= 1e4f)
                                        near.Add((o, (int)Math.Round(math.distance(Q[0], q))));
                                }
                    if (hit)
                        own++;
                }
                return own + near.Count;
            }

            // a bench for every diagonal piece: uphill from its foot to its head over ground a green cat track can be graded across
            var lim = new EasyLimits(WayGreen.EdgeMax, WayGreen.SteepPitch, WayGreen.HardPitch, WayGreen.Climb, WayGreen.SteepShare, 9000f, true);
            var cands = new List<(int Ci, List<float2> Direct, bool Keeps, List<float2> Routed, float Gain, float OldKm, float NewKm)>();
            int capped = 0, unreached = 0, failed = 0, joined = 0;
            // where other elements start or end, and the lift bottoms: a piece something joins (or that passes a station) between
            // its two ends carries that join, and a bench elsewhere would cut it (the scorecard joins within 40 m, stations 60 m)
            var endPts = new List<float2>();
            foreach (PlanRun r in plan.Runs)
            {
                endPts.Add(r.Pts[0]);
                endPts.Add(r.Pts[r.Pts.Count - 1]);
            }
            foreach (List<float2> c in plan.Connectors)
            {
                endPts.Add(c[0]);
                endPts.Add(c[c.Count - 1]);
            }
            var ends = new PointHash();
            foreach (float2 p in endPts)
                ends.Add(p);
            var stations = new PointHash();
            foreach (Lift lf in lifts)
                stations.Add(lf.Bottom.xz);
            // the cells of a bench through the given stops of a piece: one leg per pair, uphill from the lower stop to the upper one
            List<int> Bench(List<float2> od, List<int> stops)
            {
                var cells = new List<int>();
                for (int si = 0; si + 1 < stops.Count; si++)
                {
                    int hi = CellOf(g, od[stops[si]]), lo = CellOf(g, od[stops[si + 1]]);
                    if (hi < 0 || lo < 0)
                        return null;
                    if (hi == lo)
                        continue;
                    float legM = RunLength(od.GetRange(stops[si], stops[si + 1] - stops[si] + 1));
                    var lm = new EasyLimits(lim.EdgeMax, lim.SteepPitch, lim.HardPitch, lim.Climb, lim.SteepShare, RegradeStretch * legM + 100f, true);
                    EasyReach er = EasyUp(g, new List<int> { lo }, new PointHash(), lm, null, 3f, hi);
                    if (!er.Down.ContainsKey(hi))
                    {
                        if (EasyWork >= (long)EasyBudget)
                            capped++;
                        else
                            unreached++;
                        return null;
                    }
                    List<int> leg = er.Route(hi);
                    if (cells.Count > 0 && cells[cells.Count - 1] == leg[0])
                        leg.RemoveAt(0);
                    cells.AddRange(leg);
                }
                return cells.Count < 2 ? null : cells;
            }
            float MinDist(List<float2> pl, float2 q)
            {
                float best = float.MaxValue;
                foreach (float2 p in pl)
                    best = math.min(best, math.distancesq(p, q));
                return math.sqrt(best);
            }
            foreach (int ci in diag)
            {
                List<float2> old = plan.Connectors[ci];
                List<float2> od = Densify(old);
                float oldM = RunLength(old);
                // the joins the piece carries between its ends: element ends within 45 m of it, lift bottoms within 65 m
                bool Inner(float2 p) => math.distancesq(p, od[0]) > 2500f && math.distancesq(p, od[od.Count - 1]) > 2500f;
                var joinsAt = new List<(float2 P, float R)>();
                foreach (float2 q in endPts)
                    if (Inner(q) && MinDist(od, q) < 45f)
                        joinsAt.Add((q, 36f));
                foreach (Lift lf in lifts)
                    if (Inner(lf.Bottom.xz) && MinDist(od, lf.Bottom.xz) < 65f)
                        joinsAt.Add((lf.Bottom.xz, 54f));
                bool KeepsJoins(List<float2> pl)
                {
                    List<float2> d = Densify(pl);
                    foreach (var (q, r) in joinsAt)
                        if (MinDist(d, q) > r)
                            return false;
                    return true;
                }
                // the stops for a routed bench: the head, the piece's point in the middle of each stretch that carries a join, the foot
                bool Carries(int k) => Inner(od[k]) && (ends.Near(od[k], 45f) || stations.Near(od[k], 65f));
                var stops = new List<int> { 0 };
                for (int k = 1; k < od.Count - 1; k++)
                    if (Carries(k))
                    {
                        int k2 = k;
                        while (k2 + 1 < od.Count - 1 && Carries(k2 + 1))
                            k2++;
                        stops.Add((k + k2) / 2);
                        k = k2;
                    }
                stops.Add(od.Count - 1);
                // the direct bench from the head to the foot, and where it misses a join one routed through the joins
                List<float2> Track(List<int> cells)
                {
                    if (cells == null)
                        return null;
                    foreach (List<float2> pl in new[] { Polyline(g, cells, false), cells.ConvertAll(c => g.World(c)) })
                    {
                        pl[0] = old[0];
                        pl[pl.Count - 1] = old[old.Count - 1];
                        if (ConnectorOk(g, pl) && AlongGrade(g, pl) < SkiSlopeClassifier.GradeBlue - 0.01f)
                            return pl;
                    }
                    return null;
                }
                List<float2> direct = Track(Bench(od, new List<int> { 0, od.Count - 1 }));
                bool keeps = direct != null && KeepsJoins(direct);
                List<float2> routed = keeps || stops.Count <= 2 ? null : Track(Bench(od, stops));
                if (routed != null && !KeepsJoins(routed))
                    routed = null;
                if (direct == null && routed == null)
                {
                    failed++;
                    continue;
                }
                int e = runEls.FindIndex(x => x.Conn == ci);
                List<float3> s = Sampled(g, old);
                bool stub = s[0].y - s[s.Count - 1].y < 50f;
                float gain = Crowd(e) + (stub ? 40f : 0f);
                if (gain <= 0f)
                    continue;
                cands.Add((ci, direct, keeps, routed, gain, oldM / 1000f, RunLength(direct ?? routed) / 1000f));
            }
            // what a cut must not lose, as scorecard v3 reads the plan: each lift's way home, the elements a skier reaches and gets
            // back from, the lifts that link up, and each base's green skiing to lap
            int[] root = V3Bases(g, lifts, out HashSet<int> bases);
            (bool[] Home, bool[] Reached, bool[] Returns, int Net, bool[] Green) Read()
            {
                SkiGraph sg = Ski(g, plan, lifts);
                bool[] green = bases.Select(b => BaseHasGreen(sg, plan, lifts, Enumerable.Range(0, lifts.Count).Where(L => root[L] == b).ToList())).ToArray();
                return (WayHomeOk(sg, lifts, root, bases), sg.Reached, sg.Returns, sg.BestSize, green);
            }
            bool Loses((bool[] Home, bool[] Reached, bool[] Returns, int Net, bool[] Green) a, (bool[] Home, bool[] Reached, bool[] Returns, int Net, bool[] Green) b)
            {
                bool Lost(bool[] x, bool[] y) => x.Where((v, i) => v && !y[i]).Any();
                return b.Net < a.Net || Lost(a.Home, b.Home) || Lost(a.Reached, b.Reached) || Lost(a.Returns, b.Returns) || Lost(a.Green, b.Green);
            }
            // the most crowding and stubs cleared per meter of new cat track first, inside the economy share; a direct bench that
            // misses a join is kept where the ski graph loses nothing by it, else the bench routed through the joins
            int done = 0, checkedCuts = 0, refused = 0;
            float km = 0f;
            var links = new HashSet<List<float2>>(plan.Links);
            (bool[] Home, bool[] Reached, bool[] Returns, int Net, bool[] Green)? before = null;
            foreach (var cd in cands.OrderByDescending(c => c.Gain / c.NewKm))
            {
                List<float2> old = plan.Connectors[cd.Ci];
                foreach (List<float2> tr in new[] { cd.Direct, cd.Routed })
                {
                    if (tr == null)
                        continue;
                    float newKm = RunLength(tr) / 1000f;
                    if (connKm + newKm > RegradeShare * (runKm - cd.OldKm))
                        continue;
                    bool check = tr == cd.Direct && !cd.Keeps;
                    if (check && before == null)
                        before = Read();
                    plan.Connectors[cd.Ci] = tr;
                    if (check)
                    {
                        checkedCuts++;
                        var after = Read();
                        if (Loses(before.Value, after))
                        {
                            plan.Connectors[cd.Ci] = old;
                            refused++;
                            continue;
                        }
                        before = after;
                    }
                    else
                        before = null;   // the plan changed: read it again before the next check
                    if (tr == cd.Routed)
                        joined++;
                    connKm += newKm;
                    runKm -= cd.OldKm;
                    if (links.Contains(old))
                        plan.Links[plan.Links.IndexOf(old)] = tr;
                    done++;
                    km += newKm;
                    break;
                }
            }
            plan.Log.Add($"regrade: {done} of {diag.Count} diagonal traverse piece(s) cut as green cat tracks ({km:F1} km; {cands.Count} had a bench), " +
                $"connectors {connKm:F1} km of {runKm:F1} run km; no bench for {failed} ({unreached} leg(s) unreached, {capped} past the search budget); " +
                $"{checkedCuts} direct bench(es) missing a join checked on the ski graph, {refused} refused; {joined} routed through the joins");
            return done;
        }
    }
}
