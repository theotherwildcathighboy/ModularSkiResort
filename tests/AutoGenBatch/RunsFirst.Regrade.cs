// RunsFirst regrade (round v3): the traverse from a lift top to the runs it opens falls at a run's grade where the reach let it.
// Scorecard v3 reads such a piece as a diagonal blue run. Three things follow:
// - Across a face of fall-line runs, the piece crowds every run it crosses.
// - The short pieces between forks are stubs.
// - A piece under a lift line lies on the chord.
// The grading tool cuts the traverse instead as a green cat track: a bench that winds across the face between the same two points.
// The pass searches that bench for every diagonal piece. Then it takes the pieces in the order of the crowding and stubs they
// clear per meter of track. It takes pieces only while the connector km stay inside the economy share of the served run km. A piece that no bench fits
// stays a diagonal run. After the chord repair moves the runs, the pass cuts the diagonal pieces the same way. It cuts only the
// pieces still under the middle of a lift line that lies on runs. This cut goes past the economy share.

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

        /// <summary>The green cat tracks a diagonal piece could be cut as. First, the direct bench from its head to its foot (Keeps: it passes
        /// every join the piece carried between its ends). Where that misses a join, a bench routed through the joins. Null where
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
        /// gets back from, the lifts with a served run from their top, the lifts that link up, and each base's green skiing to lap.</summary>
        private sealed class SkiRead
        {
            public bool[] Home, Live, Green;
            public Dictionary<object, (bool Reached, bool Returns)> Els;   // by plan run and connector (a piece changed or taken out is not compared)
            public int Net;
            public float EasyHi;   // scorecard v4: the highest top an intermediate skis home from

            public static SkiRead Of(Grid g, Plan plan, List<Lift> lifts)
            {
                int[] root = V3Bases(g, lifts, out HashSet<int> bases);
                SkiGraph sg = Ski(g, plan, lifts);
                var els = new Dictionary<object, (bool, bool)>();
                for (int i = 0; i < sg.P.Count; i++)
                    els[i < sg.Runs ? plan.Runs[i] : (object)plan.Connectors[i - sg.Runs]] = (sg.Reached[i], sg.Returns[i]);
                return new SkiRead
                {
                    Home = WayHomeOk(sg, lifts, root, bases),
                    Live = Enumerable.Range(0, lifts.Count).Select(L => sg.Skied[L].Any(i => sg.IsRun[i] && sg.Returns[i])).ToArray(),
                    Els = els,
                    Net = sg.BestSize,
                    EasyHi = EasyHi(g, sg, lifts, root, bases),
                    Green = bases.OrderBy(b => b).Select(b => BaseHasGreen(sg, plan, lifts, Enumerable.Range(0, lifts.Count).Where(L => root[L] == b).ToList())).ToArray(),
                };
            }

            /// <summary>Whether the plan read after a change lost anything this read had (an element taken out loses nothing itself).</summary>
            public bool Loses(SkiRead after)
            {
                bool Lost(bool[] x, bool[] y) => x.Length != y.Length || x.Where((v, i) => v && !y[i]).Any();
                if (after.Net < Net || after.EasyHi < EasyHi - 1f || Lost(Home, after.Home) || Lost(Live, after.Live) || Lost(Green, after.Green))
                    return true;
                foreach (var kv in Els)
                    if (after.Els.TryGetValue(kv.Key, out var now) && (kv.Value.Reached && !now.Reached || kv.Value.Returns && !now.Returns))
                        return true;
                return false;
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

        /// <summary>The terms of scorecard v3 a cut moves, read off the plan the way the scorecard reads it (every run taken as
        /// served): crowding, connector economy, stubs, and coverage below its target. Gain is the score change of cutting a run
        /// element as a green cat track of the given length.</summary>
        private sealed class CutModel
        {
            public readonly List<int> Conn = new List<int>();   // per element: its connector (-1 for a plan run)
            public readonly List<PlanRun> Run = new List<PlanRun>();   // ... and its plan run (null for a connector)
            private readonly List<float2>[] m_D;
            private readonly int[][] m_Count;                     // per element and counted sample: the active elements crowding it
            private readonly List<(int E, int K)>[] m_Crowds;     // per element: the counted samples of other elements it crowds
            private readonly List<int>[] m_Covers;                // per element: the lattice points within 50 m of it
            private readonly int[] m_CoverCount;
            private readonly bool[] m_Active, m_Stub;
            private readonly float[] m_Km;
            private int m_Pts, m_Crowded, m_Stubs, m_Runs, m_Covered;
            private readonly int m_Skiable;
            private float m_RunKm, m_ConnKm;

            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            public CutModel(Grid g, Plan plan)
            {
                var els = new List<List<float2>>();
                foreach (PlanRun r in plan.Runs)
                    if (!r.Graded || TrackClass(g, r.Pts).Run)
                    {
                        els.Add(r.Pts);
                        Conn.Add(-1);
                        Run.Add(r);
                    }
                    else
                        m_ConnKm += RunLength(r.Pts) / 1000f;
                for (int ci = 0; ci < plan.Connectors.Count; ci++)
                    if (TrackClass(g, plan.Connectors[ci]).Run)
                    {
                        els.Add(plan.Connectors[ci]);
                        Conn.Add(ci);
                        Run.Add(null);
                    }
                    else
                        m_ConnKm += RunLength(plan.Connectors[ci]) / 1000f;
                int m = els.Count;
                m_D = new List<float2>[m];
                m_Km = new float[m];
                m_Stub = new bool[m];
                m_Active = new bool[m];
                m_Runs = m;
                for (int e = 0; e < m; e++)
                {
                    m_D[e] = Densify(els[e]);
                    m_Km[e] = RunLength(els[e]) / 1000f;
                    m_Stub[e] = RawH(g, els[e][0]) - RawH(g, els[e][els[e].Count - 1]) < 50f;
                    m_Active[e] = true;
                    m_RunKm += m_Km[e];
                    if (m_Stub[e])
                        m_Stubs++;
                }
                // crowding: a sample (all but the last 150 m) within 40 m of another run, away from that run's start and end (100 m)
                var hash = new Dictionary<long, List<(float2 P, int E)>>();
                long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;
                for (int e = 0; e < m; e++)
                    foreach (float2 p in m_D[e])
                    {
                        long k = Key((int)Math.Floor(p.x / 40f), (int)Math.Floor(p.y / 40f));
                        if (!hash.TryGetValue(k, out var l))
                            hash[k] = l = new List<(float2, int)>();
                        l.Add((p, e));
                    }
                m_Count = new int[m][];
                m_Crowds = new List<(int, int)>[m];
                for (int e = 0; e < m; e++)
                    m_Crowds[e] = new List<(int, int)>();
                var by = new List<int>();
                for (int e = 0; e < m; e++)
                {
                    int n = Math.Max(0, m_D[e].Count - 15);
                    m_Count[e] = new int[n];
                    m_Pts += n;
                    for (int k = 0; k < n; k++)
                    {
                        float2 p = m_D[e][k];
                        int cx = (int)Math.Floor(p.x / 40f), cz = (int)Math.Floor(p.y / 40f);
                        by.Clear();
                        for (int dz = -1; dz <= 1; dz++)
                            for (int dx = -1; dx <= 1; dx++)
                                if (hash.TryGetValue(Key(cx + dx, cz + dz), out var l))
                                    foreach (var (q, j) in l)
                                        if (j != e && !by.Contains(j) && math.distancesq(q, p) < 1600f
                                            && math.distancesq(m_D[j][0], p) >= 1e4f && math.distancesq(m_D[j][m_D[j].Count - 1], p) >= 1e4f)
                                            by.Add(j);
                        m_Count[e][k] = by.Count;
                        if (by.Count > 0)
                            m_Crowded++;
                        foreach (int j in by)
                            m_Crowds[j].Add((e, k));
                    }
                }
                // coverage: the skiable 20 m lattice points within 50 m of a run
                int s = Math.Max(1, (int)Math.Round(20f / g.CellM));
                var lat = new Dictionary<long, List<(float2 P, int I)>>();
                for (int z = s / 2; z < g.D; z += s)
                    for (int x = s / 2; x < g.W; x += s)
                    {
                        int c = z * g.W + x;
                        if (!g.In[c])
                            continue;
                        float2 p = g.World(c);
                        float gr = RawGrade(g, p);
                        if (gr < 0.06f || gr > 0.85f)
                            continue;
                        long k = Key((int)Math.Floor(p.x / 50f), (int)Math.Floor(p.y / 50f));
                        if (!lat.TryGetValue(k, out var l))
                            lat[k] = l = new List<(float2, int)>();
                        l.Add((p, m_Skiable++));
                    }
                m_CoverCount = new int[m_Skiable];
                m_Covers = new List<int>[m];
                var seen = new HashSet<int>();
                for (int e = 0; e < m; e++)
                {
                    seen.Clear();
                    foreach (float2 p in m_D[e])
                    {
                        int cx = (int)Math.Floor(p.x / 50f), cz = (int)Math.Floor(p.y / 50f);
                        for (int dz = -1; dz <= 1; dz++)
                            for (int dx = -1; dx <= 1; dx++)
                                if (lat.TryGetValue(Key(cx + dx, cz + dz), out var l))
                                    foreach (var (q, i) in l)
                                        if (math.distancesq(q, p) < 2500f)
                                            seen.Add(i);
                    }
                    m_Covers[e] = seen.ToList();
                    foreach (int i in m_Covers[e])
                        if (m_CoverCount[i]++ == 0)
                            m_Covered++;
                }
            }

            private float Score(int pts, int crowded, int stubs, int runs, float runKm, float connKm, int covered)
            {
                float cf = pts > 0 ? 1f - Math.Min(1f, crowded / (float)pts / 0.3f) : 1f;
                float ec = connKm <= 0.35f * runKm ? 1f : 0.35f * runKm / connKm;
                float sf = runs > 0 ? 1f - stubs / (float)runs : 0f;
                float cv = m_Skiable > 0 ? Math.Min(1f, covered / (float)m_Skiable / 0.6f) : 0f;
                return 5f * cf + 5f * ec + 5f * sf + 10f * cv;
            }

            public string Terms() => $"crowd-free {(m_Pts > 0 ? 1f - Math.Min(1f, m_Crowded / (float)m_Pts / 0.3f) : 1f):F2} ({m_Crowded}/{m_Pts}) economy {(m_ConnKm <= 0.35f * m_RunKm ? 1f : 0.35f * m_RunKm / m_ConnKm):F2} ({m_ConnKm:F1}/{m_RunKm:F1} km) stub-free {1f - m_Stubs / (float)Math.Max(1, m_Runs):F2} ({m_Stubs}/{m_Runs}) cover {Math.Min(1f, m_Covered / (float)Math.Max(1, m_Skiable) / 0.6f):F2} ({m_Covered}/{m_Skiable})";

            public float Total() => Score(m_Pts, m_Crowded, m_Stubs, m_Runs, m_RunKm, m_ConnKm, m_Covered);

            /// <summary>RF_MODELDBG: the crowded samples by what crowds them (a fall-line run or a diagonal piece, across or alongside)
            /// and where on the crowded element they lie.</summary>
            public string CrowdBreakdown()
            {
                int m = m_D.Length;
                var by = new List<int>[m][];
                for (int e = 0; e < m; e++)
                    by[e] = new List<int>[m_Count[e].Length];
                for (int j = 0; j < m; j++)
                    foreach (var (e, k) in m_Crowds[j])
                        (by[e][k] ??= new List<int>()).Add(j);
                var tally = new SortedDictionary<string, int>();
                float2 Dir(List<float2> d, int k) => math.normalizesafe(d[Math.Min(d.Count - 1, k + 1)] - d[Math.Max(0, k - 1)]);
                for (int e = 0; e < m; e++)
                    for (int k = 0; k < m_Count[e].Length; k++)
                    {
                        if (by[e][k] == null)
                            continue;
                        float2 p = m_D[e][k], de = Dir(m_D[e], k);
                        bool anyRun = by[e][k].Exists(j => Conn[j] < 0);
                        int jj = by[e][k].First(j => !anyRun || Conn[j] < 0);
                        int kn = 0;
                        for (int q = 1; q < m_D[jj].Count; q++)
                            if (math.distancesq(m_D[jj][q], p) < math.distancesq(m_D[jj][kn], p))
                                kn = q;
                        bool across = Math.Abs(math.dot(de, Dir(m_D[jj], kn))) < 0.7f;
                        string key = (Conn[e] < 0 ? "run" : "diag") + " by " + (anyRun ? "run" : "diag") + (across ? " across" : " alongside") + (k < 10 ? " (its first 100 m)" : "");
                        tally[key] = tally.TryGetValue(key, out int v) ? v + 1 : 1;
                    }
                return string.Join(", ", tally.Select(kv => kv.Key + " " + kv.Value));
            }

            public float Gain(int e, float newKm)
            {
                int own = 0, freed = 0, lost = 0;
                foreach (int c in m_Count[e])
                    if (c > 0)
                        own++;
                foreach (var (o, k) in m_Crowds[e])
                    if (m_Active[o] && m_Count[o][k] == 1)
                        freed++;
                foreach (int i in m_Covers[e])
                    if (m_CoverCount[i] == 1)
                        lost++;
                return Score(m_Pts - m_Count[e].Length, m_Crowded - own - freed, m_Stubs - (m_Stub[e] ? 1 : 0), m_Runs - 1,
                    m_RunKm - m_Km[e], m_ConnKm + newKm, m_Covered - lost) - Score(m_Pts, m_Crowded, m_Stubs, m_Runs, m_RunKm, m_ConnKm, m_Covered);
            }

            public void Cut(int e, float newKm)
            {
                m_Active[e] = false;
                foreach (int c in m_Count[e])
                    if (c > 0)
                        m_Crowded--;
                m_Pts -= m_Count[e].Length;
                foreach (var (o, k) in m_Crowds[e])
                    if (--m_Count[o][k] == 0 && m_Active[o])
                        m_Crowded--;
                foreach (int i in m_Covers[e])
                    if (--m_CoverCount[i] == 0)
                        m_Covered--;
                if (m_Stub[e])
                    m_Stubs--;
                m_Runs--;
                m_RunKm -= m_Km[e];
                m_ConnKm += newKm;
            }
        }

        private static readonly float RegradeMinGain = Env("RF_RGMIN", 0.005f);   // a cut that moves the score less than this stays a diagonal

        /// <summary>Cuts diagonal traverse pieces as green cat tracks where a bench fits and the cut gains score (crowding, stubs and
        /// economy against coverage, as the scorecard reads them). Returns the pieces regraded.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static int RegradeTraverses(Grid g, Plan plan, List<Lift> lifts)
        {
            var model = new CutModel(g, plan);
            var diag = Enumerable.Range(0, model.Conn.Count).Where(e => model.Conn[e] >= 0).ToList();
            if (diag.Count == 0)
                return 0;
            // the benches for every diagonal piece
            var open = new List<(int El, List<float2> Direct, bool Keeps, List<float2> Routed)>();
            var tally = new int[2];
            int failed = 0, joined = 0;
            var jp = new JoinPoints(plan, lifts);
            foreach (int e in diag)
            {
                var (direct, keeps, routed) = Benches(g, plan.Connectors[model.Conn[e]], jp, tally);
                if (direct == null && routed == null)
                    failed++;
                else
                    open.Add((e, direct, keeps, routed));
            }
            int benches = open.Count;
            // the cut that gains the most score first, while one gains. The code keeps a direct bench that misses a join where the ski graph
            // loses nothing by it. Otherwise it uses the bench routed through the joins
            int done = 0, checkedCuts = 0, refused = 0;
            float km = 0f, gained = 0f;
            SkiRead before = null;
            while (open.Count > 0)
            {
                int bi = -1;
                float best = RegradeMinGain;
                for (int i = 0; i < open.Count; i++)
                {
                    float gn = model.Gain(open[i].El, RunLength(open[i].Direct ?? open[i].Routed) / 1000f);
                    if (gn > best)
                    {
                        best = gn;
                        bi = i;
                    }
                }
                if (bi < 0)
                    break;
                var cd = open[bi];
                open.RemoveAt(bi);
                int ci = model.Conn[cd.El];
                List<float2> old = plan.Connectors[ci];
                foreach (List<float2> tr in new[] { cd.Direct, cd.Routed })
                {
                    if (tr == null)
                        continue;
                    float newKm = RunLength(tr) / 1000f, gn = model.Gain(cd.El, newKm);
                    if (gn <= RegradeMinGain)
                        continue;
                    bool check = tr == cd.Direct && !cd.Keeps;
                    if (check && before == null)
                        before = SkiRead.Of(g, plan, lifts);
                    SwapConnector(plan, ci, tr);
                    if (check)
                    {
                        checkedCuts++;
                        SkiRead after = SkiRead.Of(g, plan, lifts);
                        if (before.Loses(after))
                        {
                            SwapConnector(plan, ci, old);
                            refused++;
                            continue;
                        }
                        before = after;
                    }
                    else
                        before = null;   // the plan changed: read it again before the next check
                    model.Cut(cd.El, newKm);
                    if (tr == cd.Routed)
                        joined++;
                    done++;
                    km += newKm;
                    gained += gn;
                    break;
                }
            }
            var (connKm, runKm) = EconomyKm(g, plan);
            plan.Log.Add($"regrade: {done} of {diag.Count} diagonal traverse piece(s) cut as green cat tracks ({km:F1} km, +{gained:F2} by the planner's read; {benches} had a bench), " +
                $"connectors {connKm:F1} km of {runKm:F1} run km; no bench for {failed} ({tally[1]} leg(s) unreached, {tally[0]} past the search budget); " +
                $"{checkedCuts} direct bench(es) missing a join checked on the ski graph, {refused} refused; {joined} routed through the joins");
            return done;
        }

        private static readonly float PruneMinGain = Env("RF_PRUNEMIN", 0.01f);   // a run taken out gains at least this much score
        private const int PruneReads = 40;                                         // ski graph reads the prune may spend

        /// <summary>Takes out the plan runs whose removal gains score as the scorecard reads the plan (a run crowded along most of its
        /// length that opens little ground of its own, or a stub), the largest gain first, where the ski graph loses nothing by it.
        /// Returns the runs taken out.</summary>
        private static int PruneRuns(Grid g, Plan plan, List<Lift> lifts)
        {
            var model = new CutModel(g, plan);
            var open = Enumerable.Range(0, model.Run.Count).Where(e => model.Run[e] != null).ToList();
            SkiRead before = SkiRead.Of(g, plan, lifts);
            int removed = 0, refused = 0, reads = 1;
            float gained = 0f, km = 0f;
            while (open.Count > 0 && reads < PruneReads)
            {
                int bi = -1;
                float best = PruneMinGain;
                for (int i = 0; i < open.Count; i++)
                {
                    float gn = model.Gain(open[i], 0f);
                    if (gn > best)
                    {
                        best = gn;
                        bi = i;
                    }
                }
                if (bi < 0)
                    break;
                int e = open[bi];
                open.RemoveAt(bi);
                PlanRun r = model.Run[e];
                int at = plan.Runs.IndexOf(r);
                plan.Runs.RemoveAt(at);
                SkiRead after = SkiRead.Of(g, plan, lifts);
                reads++;
                if (before.Loses(after))
                {
                    plan.Runs.Insert(at, r);
                    refused++;
                    continue;
                }
                before = after;
                model.Cut(e, 0f);
                removed++;
                gained += best;
                km += RunLength(r.Pts) / 1000f;
            }
            plan.Log.Add($"prune: {removed} run(s) taken out ({km:F1} km, +{gained:F2} by the planner's read), {refused} kept for the skiing");
            return removed;
        }

        private const int MergeTries = 40;              // merges the stub pass may try
        private const float MergeMinGain = 0.005f;      // ... and the score one must gain

        /// <summary>A diagonal piece with a stub's drop joined end to start with the piece it runs into (or that runs into it) as one
        /// graded track: the same line on the ground, read by the scorecard as one run (a run may branch off anywhere along it). Kept
        /// where the score gains (the model), the ski graph loses nothing and no more lift lines lie on runs. Returns the pieces merged.</summary>
        private static int MergeStubPieces(Grid g, Plan plan, List<Lift> lifts)
        {
            int merged = 0, tried = 0;
            float now = new CutModel(g, plan).Total();
            SkiRead before = SkiRead.Of(g, plan, lifts);
            float Drop(List<float2> pl) => RawH(g, pl[0]) - RawH(g, pl[pl.Count - 1]);
            // the lift lines whose middle lies on runs: a merged piece reads as one run and may lie under a line a cut track cleared
            int Lines()
            {
                List<(List<float2> D, int Conn, PlanRun Run, bool Green)> els = ChordEls(g, plan);
                return lifts.Count(lf => ChordShareV3(lf, els, out _) > ChordShareMax);
            }
            int lines = Lines();
            for (bool again = true; again && tried < MergeTries;)
            {
                again = false;
                for (int a = 0; a < plan.Connectors.Count && !again && tried < MergeTries; a++)
                {
                    List<float2> A = plan.Connectors[a];
                    if (Drop(A) >= 50f || !TrackClass(g, A).Run)
                        continue;
                    for (int b = 0; b < plan.Connectors.Count && !again && tried < MergeTries; b++)
                    {
                        List<float2> B = plan.Connectors[b];
                        List<float2> m = null;
                        if (b != a && math.distancesq(A[A.Count - 1], B[0]) < 25f)
                            m = A.Concat(B.Skip(1)).ToList();
                        else if (b != a && math.distancesq(B[B.Count - 1], A[0]) < 25f)
                            m = B.Concat(A.Skip(1)).ToList();
                        if (m == null || !ConnectorOk(g, m))
                            continue;
                        tried++;
                        int la = plan.Links.IndexOf(A), lb = plan.Links.IndexOf(B);
                        plan.Connectors[a] = m;
                        plan.Connectors.RemoveAt(b);
                        float then = new CutModel(g, plan).Total();
                        SkiRead after = then > now + MergeMinGain ? SkiRead.Of(g, plan, lifts) : null;
                        int linesAfter = after != null && !before.Loses(after) ? Lines() : int.MaxValue;
                        if (linesAfter <= lines)
                        {
                            lines = linesAfter;
                            if (la >= 0)
                                plan.Links[la] = m;
                            else if (lb >= 0)
                                plan.Links[lb] = m;
                            if (la >= 0 && lb >= 0)
                                plan.Links.RemoveAt(lb);
                            now = then;
                            before = after;
                            merged++;
                            again = true;
                            continue;
                        }
                        plan.Connectors.Insert(b, B);
                        plan.Connectors[a] = A;
                    }
                }
            }
            plan.Log.Add($"stub merge: {merged} diagonal stub piece(s) joined with the piece they run into ({tried} tried)");
            return merged;
        }

        /// <summary>The runs a lift line's middle may not lie along, as scorecard v3 reads them (runs, and graded tracks steeper than
        /// the blue line). It returns each with the connector that the graded track is (-1 for a plan run). It also returns whether the
        /// element is a green run (a learning chair's own green run does not count under its line).</summary>
        private static List<(List<float2> D, int Conn, PlanRun Run, bool Green)> ChordEls(Grid g, Plan plan)
        {
            var els = new List<(List<float2>, int, PlanRun, bool)>();
            foreach (PlanRun r in plan.Runs)
            {
                var (run, band) = r.Graded ? TrackClass(g, r.Pts) : (true, GroundBand(g, r.Pts));
                if (run)
                    els.Add((Densify(r.Pts), -1, r, band <= SlopeBand.Green));
            }
            for (int ci = 0; ci < plan.Connectors.Count; ci++)
            {
                var (run, band) = TrackClass(g, plan.Connectors[ci]);
                if (run)
                    els.Add((Densify(plan.Connectors[ci]), ci, null, band <= SlopeBand.Green));
            }
            return els;
        }

        /// <summary>The share of a lift chord's middle 70 % (every 10 m) within 20 m of a run as scorecard v3 reads it, and the
        /// samples each element is near.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static float ChordShareV3(Lift lf, List<(List<float2> D, int Conn, PlanRun Run, bool Green)> els, out int[] hits)
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
            // only the points within reach of the line's middle can be near a sample on it
            float2 m0 = math.lerp(b, t, 0.15f), m1 = math.lerp(b, t, 0.85f);
            var close = new List<(int I, List<float2> P)>();
            for (int i = 0; i < els.Count; i++)
                if (counted[i])
                {
                    List<float2> pts = null;
                    foreach (float2 p in els[i].D)
                        if (SegDist(p, m0, m1) < ChordOnRunM + 1f)
                            (pts ??= new List<float2>()).Add(p);
                    if (pts != null)
                        close.Add((i, pts));
                }
            int n = 0, near = 0;
            for (float f = 0.15f; f <= 0.85f; f += 10f / math.max(10f, plan2), n++)
            {
                float2 q = math.lerp(b, t, f);
                bool hit = false;
                foreach (var (i, pts) in close)
                    if (pts.Any(p => math.distancesq(p, q) < ChordOnRunM * ChordOnRunM))
                    {
                        hit = true;
                        hits[i]++;
                    }
                if (hit)
                    near++;
            }
            return n > 0 ? near / (float)n : 0f;
        }

        private const float ChordViolationWorth = 0.8f;   // the score a cleared line may cost elsewhere (a violation costs 0.86 to 0.95)

        /// <summary>A lift line whose middle still lies along runs after the chord repair. The method cuts the diagonal pieces under it as green
        /// cat tracks (a cat track under a lift line is no run), the most under the line first. Where that does not clear the line, the
        /// method removes the plan runs under it. It does this only while the ground and crowding they cost (as the scorecard reads them)
        /// stay under a violation's worth. It keeps each change where the ski graph loses nothing. It undoes a line's changes when they
        /// do not clear the line. Returns the lines cleared.</summary>
        private static int ClearChordTracks(Grid g, Plan plan, List<Lift> lifts)
        {
            int cleared = 0;
            List<(List<float2> D, int Conn, PlanRun Run, bool Green)> els = ChordEls(g, plan);
            foreach (Lift lf in lifts)
            {
                float share = ChordShareV3(lf, els, out int[] hits);
                if (share <= ChordShareMax)
                    continue;
                List<int> under = Enumerable.Range(0, els.Count).Where(i => els[i].Conn >= 0 && hits[i] > 0)
                    .OrderByDescending(i => hits[i]).Select(i => els[i].Conn).ToList();
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
                // the plan runs under the line, the most under it first
                var gone = new List<(PlanRun R, int At)>();
                float cost = 0f;
                if (now > ChordShareMax)
                {
                    var model = new CutModel(g, plan);
                    now = ChordShareV3(lf, els, out hits);
                    List<PlanRun> runsUnder = Enumerable.Range(0, els.Count).Where(i => els[i].Run != null && hits[i] > 0)
                        .OrderByDescending(i => hits[i]).Select(i => els[i].Run).ToList();
                    foreach (PlanRun r in runsUnder)
                    {
                        int e = model.Run.IndexOf(r);
                        if (e < 0)
                            continue;
                        float gn = model.Gain(e, 0f);
                        if (cost - gn > ChordViolationWorth)
                            continue;
                        int at = plan.Runs.IndexOf(r);
                        plan.Runs.RemoveAt(at);
                        SkiRead after = SkiRead.Of(g, plan, lifts);
                        if (before.Loses(after))
                        {
                            plan.Runs.Insert(at, r);
                            continue;
                        }
                        before = after;
                        model.Cut(e, 0f);
                        cost -= gn;
                        gone.Add((r, at));
                        els = ChordEls(g, plan);
                        now = ChordShareV3(lf, els, out _);
                        if (now <= ChordShareMax)
                            break;
                    }
                }
                if (now <= ChordShareMax)
                {
                    cleared++;
                    plan.Log.Add($"chord tracks L{lf.Id}: {cut.Count} diagonal piece(s) under the line cut as green cat tracks, {gone.Count} run(s) under it taken out " +
                        $"({share:P0} -> {now:P0} of its middle on runs; {cost:F2} of score elsewhere by the planner's read)");
                    continue;
                }
                for (int k = gone.Count - 1; k >= 0; k--)
                    plan.Runs.Insert(gone[k].At, gone[k].R);
                foreach (var (ci, old) in cut)
                    SwapConnector(plan, ci, old);
                els = ChordEls(g, plan);
                plan.Log.Add($"chord tracks L{lf.Id}: undone ({share:P0} -> {now:P0} of its middle on runs with {cut.Count} of {under.Count} diagonal piece(s) cut, " +
                    $"{gone.Count} run(s) taken out)");
            }
            return cleared;
        }
    }
}
