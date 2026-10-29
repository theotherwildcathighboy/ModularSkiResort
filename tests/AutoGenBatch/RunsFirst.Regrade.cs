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
            var cands = new List<(int Ci, List<float2> Track, float Gain, float OldKm, float NewKm)>();
            int capped = 0, unreached = 0, failed = 0;
            foreach (int ci in diag)
            {
                List<float2> old = plan.Connectors[ci];
                int head = CellOf(g, old[0]), foot = CellOf(g, old[old.Count - 1]);
                if (head < 0 || foot < 0 || head == foot)
                    continue;
                float oldM = RunLength(old);
                var lm = new EasyLimits(lim.EdgeMax, lim.SteepPitch, lim.HardPitch, lim.Climb, lim.SteepShare, RegradeStretch * oldM + 100f, true);
                EasyReach er = EasyUp(g, new List<int> { foot }, new PointHash(), lm, null, 3f, head);
                if (!er.Down.ContainsKey(head))
                {
                    if (EasyWork >= (long)EasyBudget)
                        capped++;
                    else
                        unreached++;
                    continue;
                }
                List<int> cells = er.Route(head);
                if (cells.Count < 2 || cells[cells.Count - 1] != foot)
                    continue;
                List<float2> track = null;
                foreach (List<float2> pl in new[] { Polyline(g, cells, false), cells.ConvertAll(c => g.World(c)) })
                {
                    pl[0] = old[0];
                    pl[pl.Count - 1] = old[old.Count - 1];
                    if (ConnectorOk(g, pl) && AlongGrade(g, pl) < SkiSlopeClassifier.GradeBlue - 0.01f)
                    {
                        track = pl;
                        break;
                    }
                }
                if (track == null)
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
                cands.Add((ci, track, gain, oldM / 1000f, RunLength(track) / 1000f));
            }
            // the most crowding and stubs cleared per meter of new cat track first, inside the economy share
            int done = 0;
            float km = 0f;
            var links = new HashSet<List<float2>>(plan.Links);
            foreach (var cd in cands.OrderByDescending(c => c.Gain / c.NewKm))
            {
                if (connKm + cd.NewKm > RegradeShare * (runKm - cd.OldKm))
                    continue;
                connKm += cd.NewKm;
                runKm -= cd.OldKm;
                List<float2> old = plan.Connectors[cd.Ci];
                plan.Connectors[cd.Ci] = cd.Track;
                if (links.Contains(old))
                    plan.Links[plan.Links.IndexOf(old)] = cd.Track;
                done++;
                km += cd.NewKm;
            }
            plan.Log.Add($"regrade: {done} of {diag.Count} diagonal traverse piece(s) cut as green cat tracks ({km:F1} km; {cands.Count} had a bench), " +
                $"connectors {connKm:F1} km of {runKm:F1} run km; no bench: {unreached} unreached, {capped} past the search budget, {failed} off the connector rules");
            return done;
        }
    }
}
