// RunsFirst regrade (round v3): the traverse from a lift top to the runs it opens falls at a run's grade where the reach let it, and
// scorecard v3 reads such a piece as a diagonal blue run: across a face of fall-line runs it crowds every run it crosses, the short
// pieces between forks are stubs, and one under a lift line lies on the chord. The grading tool cuts the traverse instead as a green
// cat track: a bench that winds across the face between the same two points. The pass searches that bench for every diagonal
// piece, then takes them in the order of the crowding and stubs they clear per meter of track, while the connector km stay inside
// the economy share of the served run km; a piece no bench fits stays a diagonal run. After the chord repair has moved the runs, the
// diagonal pieces still under the middle of a lift line that lies on runs are cut the same way, past the economy share.

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

        /// <summary>Where the plan's elements start and end, and its lift bottoms: a piece something joins (or that passes a station)
        /// between its two ends carries that join, and a bench elsewhere would cut it (the scorecard joins within 40 m, stations 60 m).</summary>
        private sealed class JoinPoints
        {
            public readonly List<float2> Ends = new List<float2>(), Bottoms = new List<float2>();
            public readonly PointHash EndHash = new PointHash(), Stations = new PointHash();

            public JoinPoints(Plan plan, List<Lift> lifts)
            {
                foreach (PlanRun r in plan.Runs)
                {
                    Ends.Add(r.Pts[0]);
                    Ends.Add(r.Pts[r.Pts.Count - 1]);
                }
                foreach (List<float2> c in plan.Connectors)
                {
                    Ends.Add(c[0]);
                    Ends.Add(c[c.Count - 1]);
                }
                foreach (float2 p in Ends)
                    EndHash.Add(p);
                foreach (Lift lf in lifts)
                {
                    Bottoms.Add(lf.Bottom.xz);
                    Stations.Add(lf.Bottom.xz);
                }
            }
        }

        private static float MinDist(List<float2> pl, float2 q)
        {
            float best = float.MaxValue;
            foreach (float2 p in pl)
                best = math.min(best, math.distancesq(p, q));
            return math.sqrt(best);
        }

        /// <summary>The green cat tracks a diagonal piece could be cut as: the direct bench from its head to its foot (Keeps: it passes
        /// every join the piece carried between its ends), and where that misses a join, a bench routed through the joins; null where
        /// none fits. tally counts the legs past the search budget [0] and the legs no bench reached [1].</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static (List<float2> Direct, bool Keeps, List<float2> Routed) Benches(Grid g, List<float2> old, JoinPoints jp, int[] tally)
        {
            // uphill from the foot to the head over ground a green cat track can be graded across
            List<float2> od = Densify(old);
            List<int> Bench(List<int> stops)
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
                    var lm = new EasyLimits(WayGreen.EdgeMax, WayGreen.SteepPitch, WayGreen.HardPitch, WayGreen.Climb, WayGreen.SteepShare,
                        RegradeStretch * legM + 100f, true);
                    EasyReach er = EasyUp(g, new List<int> { lo }, new PointHash(), lm, null, 3f, hi);
                    if (!er.Down.ContainsKey(hi))
                    {
                        tally[EasyWork >= (long)EasyBudget ? 0 : 1]++;
                        return null;
                    }
                    List<int> leg = er.Route(hi);
                    if (cells.Count > 0 && cells[cells.Count - 1] == leg[0])
                        leg.RemoveAt(0);
                    cells.AddRange(leg);
                }
                return cells.Count < 2 ? null : cells;
            }
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
            // the joins the piece carries between its ends: element ends within 45 m of it, lift bottoms within 65 m
            bool Inner(float2 p) => math.distancesq(p, od[0]) > 2500f && math.distancesq(p, od[od.Count - 1]) > 2500f;
            var joinsAt = new List<(float2 P, float R)>();
            foreach (float2 q in jp.Ends)
                if (Inner(q) && MinDist(od, q) < 45f)
                    joinsAt.Add((q, 36f));
            foreach (float2 q in jp.Bottoms)
                if (Inner(q) && MinDist(od, q) < 65f)
                    joinsAt.Add((q, 54f));
            bool KeepsJoins(List<float2> pl)
            {
                List<float2> d = Densify(pl);
                foreach (var (q, r) in joinsAt)
                    if (MinDist(d, q) > r)
                        return false;
                return true;
            }
            // the stops for a routed bench: the head, the piece's point in the middle of each stretch that carries a join, the foot
            bool Carries(int k) => Inner(od[k]) && (jp.EndHash.Near(od[k], 45f) || jp.Stations.Near(od[k], 65f));
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
            List<float2> direct = Track(Bench(new List<int> { 0, od.Count - 1 }));
            bool keeps = direct != null && KeepsJoins(direct);
            List<float2> routed = keeps || stops.Count <= 2 ? null : Track(Bench(stops));
            if (routed != null && !KeepsJoins(routed))
                routed = null;
            return (direct, keeps, routed);
        }

        /// <summary>What a cut must not lose, as scorecard v3 reads the plan: each lift's way home, the elements a skier reaches and
        /// gets back from, the lifts that link up, and each base's green skiing to lap.</summary>
        private sealed class SkiRead
        {
            public bool[] Home, Reached, Returns, Green;
            public int Net;

            public static SkiRead Of(Grid g, Plan plan, List<Lift> lifts)
            {
                int[] root = V3Bases(g, lifts, out HashSet<int> bases);
                SkiGraph sg = Ski(g, plan, lifts);
                return new SkiRead
                {
                    Home = WayHomeOk(sg, lifts, root, bases),
                    Reached = sg.Reached,
                    Returns = sg.Returns,
                    Net = sg.BestSize,
                    Green = bases.OrderBy(b => b).Select(b => BaseHasGreen(sg, plan, lifts, Enumerable.Range(0, lifts.Count).Where(L => root[L] == b).ToList())).ToArray(),
                };
            }

            public bool Loses(SkiRead after)
            {
                bool Lost(bool[] x, bool[] y) => x.Length != y.Length || x.Where((v, i) => v && !y[i]).Any();
                return after.Net < Net || Lost(Home, after.Home) || Lost(Reached, after.Reached) || Lost(Returns, after.Returns) || Lost(Green, after.Green);
            }
        }

        /// <summary>Puts a new track in a connector's place (and in the links, which hold the same list).</summary>
        private static void SwapConnector(Plan plan, int ci, List<float2> track)
        {
            int li = plan.Links.IndexOf(plan.Connectors[ci]);
            if (li >= 0)
                plan.Links[li] = track;
            plan.Connectors[ci] = track;
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

            // the benches for every diagonal piece, and the crowding and stubs each would clear
            var cands = new List<(int Ci, List<float2> Direct, bool Keeps, List<float2> Routed, float Gain, float OldKm, float NewKm)>();
            var tally = new int[2];
            int failed = 0, joined = 0;
            var jp = new JoinPoints(plan, lifts);
            foreach (int ci in diag)
            {
                List<float2> old = plan.Connectors[ci];
                var (direct, keeps, routed) = Benches(g, old, jp, tally);
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
                cands.Add((ci, direct, keeps, routed, gain, RunLength(old) / 1000f, RunLength(direct ?? routed) / 1000f));
            }
            // the most crowding and stubs cleared per meter of new cat track first, inside the economy share; a direct bench that
            // misses a join is kept where the ski graph loses nothing by it, else the bench routed through the joins
            int done = 0, checkedCuts = 0, refused = 0;
            float km = 0f;
            SkiRead before = null;
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
                        before = SkiRead.Of(g, plan, lifts);
                    plan.Connectors[cd.Ci] = tr;
                    if (check)
                    {
                        checkedCuts++;
                        SkiRead after = SkiRead.Of(g, plan, lifts);
                        if (before.Loses(after))
                        {
                            plan.Connectors[cd.Ci] = old;
                            refused++;
                            continue;
                        }
                        before = after;
                    }
                    else
                        before = null;   // the plan changed: read it again before the next check
                    plan.Connectors[cd.Ci] = old;
                    SwapConnector(plan, cd.Ci, tr);
                    if (tr == cd.Routed)
                        joined++;
                    connKm += newKm;
                    runKm -= cd.OldKm;
                    done++;
                    km += newKm;
                    break;
                }
            }
            plan.Log.Add($"regrade: {done} of {diag.Count} diagonal traverse piece(s) cut as green cat tracks ({km:F1} km; {cands.Count} had a bench), " +
                $"connectors {connKm:F1} km of {runKm:F1} run km; no bench for {failed} ({tally[1]} leg(s) unreached, {tally[0]} past the search budget); " +
                $"{checkedCuts} direct bench(es) missing a join checked on the ski graph, {refused} refused; {joined} routed through the joins");
            return done;
        }

        /// <summary>The runs a lift line's middle may not lie along, as scorecard v3 reads them (runs, and graded tracks steeper than
        /// the blue line), with the connector each graded track is (-1 for a plan run) and whether it is a green run (a learning
        /// chair's own green run is not counted under its line).</summary>
        private static List<(List<float2> D, int Conn, bool Green)> ChordEls(Grid g, Plan plan)
        {
            var els = new List<(List<float2>, int, bool)>();
            foreach (PlanRun r in plan.Runs)
            {
                var (run, band) = r.Graded ? TrackClass(g, r.Pts) : (true, GroundBand(g, r.Pts));
                if (run)
                    els.Add((Densify(r.Pts), -1, band <= SlopeBand.Green));
            }
            for (int ci = 0; ci < plan.Connectors.Count; ci++)
            {
                var (run, band) = TrackClass(g, plan.Connectors[ci]);
                if (run)
                    els.Add((Densify(plan.Connectors[ci]), ci, band <= SlopeBand.Green));
            }
            return els;
        }

        /// <summary>The share of a lift chord's middle 70 % (every 10 m) within 20 m of a run as scorecard v3 reads it, and the
        /// samples each element is near.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static float ChordShareV3(Lift lf, List<(List<float2> D, int Conn, bool Green)> els, out int[] hits)
        {
            float2 b = lf.Bottom.xz, t = lf.Top.xz;
            float plan2 = math.distance(b, t);
            hits = new int[els.Count];
            var counted = new bool[els.Count];
            for (int i = 0; i < els.Count; i++)
            {
                List<float2> d = els[i].D;
                counted[i] = !(els[i].Green && math.distancesq(d[0], t) < 150f * 150f && math.distancesq(d[d.Count - 1], b) < 60f * 60f);   // the scorecard's 150 m start, 60 m station
            }
            int n = 0, near = 0;
            for (float f = 0.15f; f <= 0.85f; f += 10f / math.max(10f, plan2), n++)
            {
                float2 q = math.lerp(b, t, f);
                bool hit = false;
                for (int i = 0; i < els.Count; i++)
                    if (counted[i] && els[i].D.Any(p => math.distancesq(p, q) < ChordOnRunM * ChordOnRunM))
                    {
                        hit = true;
                        hits[i]++;
                    }
                if (hit)
                    near++;
            }
            return n > 0 ? near / (float)n : 0f;
        }

        /// <summary>A lift line whose middle still lies along runs after the chord repair: the diagonal pieces under it are cut as green
        /// cat tracks (a cat track under a lift line is no run), the most under the line first, until it clears. Each cut is kept where
        /// the ski graph loses nothing, and a line's cuts are undone when it does not clear. Returns the lines cleared.</summary>
        private static int ClearChordTracks(Grid g, Plan plan, List<Lift> lifts)
        {
            int cleared = 0;
            List<(List<float2> D, int Conn, bool Green)> els = ChordEls(g, plan);
            foreach (Lift lf in lifts)
            {
                float share = ChordShareV3(lf, els, out int[] hits);
                if (share <= ChordShareMax)
                    continue;
                List<int> under = Enumerable.Range(0, els.Count).Where(i => els[i].Conn >= 0 && hits[i] > 0)
                    .OrderByDescending(i => hits[i]).Select(i => els[i].Conn).ToList();
                if (under.Count == 0)
                {
                    plan.Log.Add($"chord tracks L{lf.Id}: no diagonal piece under the line ({share:P0} of its middle on runs)");
                    continue;
                }
                var jp = new JoinPoints(plan, lifts);
                SkiRead before = SkiRead.Of(g, plan, lifts);
                var cut = new List<(int Ci, List<float2> Old)>();
                float now = share;
                foreach (int ci in under)
                {
                    List<float2> old = plan.Connectors[ci];
                    var (direct, _, routed) = Benches(g, old, jp, new int[2]);
                    foreach (List<float2> tr in new[] { direct, routed })
                    {
                        if (tr == null)
                            continue;
                        SwapConnector(plan, ci, tr);
                        SkiRead after = SkiRead.Of(g, plan, lifts);
                        if (before.Loses(after))
                        {
                            SwapConnector(plan, ci, old);
                            continue;
                        }
                        before = after;
                        cut.Add((ci, old));
                        break;
                    }
                    els = ChordEls(g, plan);
                    now = ChordShareV3(lf, els, out _);
                    if (now <= ChordShareMax)
                        break;
                }
                if (now <= ChordShareMax)
                {
                    cleared++;
                    plan.Log.Add($"chord tracks L{lf.Id}: {cut.Count} diagonal piece(s) under the line cut as green cat tracks ({share:P0} -> {now:P0} of its middle on runs)");
                    continue;
                }
                foreach (var (ci, old) in cut)
                    SwapConnector(plan, ci, old);
                els = ChordEls(g, plan);
                plan.Log.Add($"chord tracks L{lf.Id}: undone ({share:P0} -> {now:P0} of its middle on runs with {cut.Count} of {under.Count} diagonal piece(s) cut)");
            }
            return cleared;
        }
    }
}
