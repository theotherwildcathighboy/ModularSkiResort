// RunsFirstPlanner coverage fill: once the lifts, runs, network and beginner terrain stand, skiable ground a skier gets to
// can still lie away from every run. Examples: a broad face between two trunk runs, the gentle side of a valley. A candidate
// line becomes a run when it leaves a run or traverse the skier is already on and goes down over that ground. It must also open
// enough new ground for its length and keep its distance from the runs beside it. The fill takes the best new ground first,
// until no line is worth a trail.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class RunsFirstPlanner
    {
        private const float FillCoverM = 50f;       // ground within this of a run counts as opened (the scorecard's coverage radius)
        private const float FillNewPerM = 0.08f;    // new 20 m lattice points per meter of trail a fill run must open (a third of its band)
        private const int FillNewMin = 25;          // ... and at least this many (a hectare)
        private const float FillCrowdShare = 0.25f, FillCrowdM = 40f;   // at most this share of it within 40 m of another run (its last 150 m aside)
        private const float FillForkM = 30f;        // a fill run leaves a run or traverse within this distance, at most 3 m above it
        private const float FillDropM = 60f;
        private float FillMinM => O.FillMinM;   // a short fill run still drops FillDropM (no stub). Scorecard v3 rates length nowhere else
        private const float FillFeedM = 110f, FillFeedShare = 0.3f;   // ... or from a straight cut-over this long at most (a share of the run)
        // a fill run must open ground for the crowding it brings. The scorecard weighs coverage 10 against its 60 % target and
        // crowding 5 against a 30 % share. So at par a crowded 10 m sample costs about 1.0 x (lattice points / run samples) new points.
        // The fill asks a quarter of that: 0.25 against 0.67. Result: visible 97.48 -> 97.53, 80 % outlines 95.33 -> 95.40, 120 %
        // 96.10 -> 96.22, squares 95.01 -> 95.21, no region worse (1.0: 97.41 against 97.48)
        private float FillCrowdTrade => O.FillCrowdTrade;

        /// <summary>Fall lines through open ground. From an open lattice point, a line goes up the steepest ascent until it meets ground
        /// a skier is on (onNear). It then goes down the steepest descent until it runs along a run for a few steps or reaches a
        /// station. The code makes one line per stretch of face (an open point near a line already traced starts none). It returns the
        /// lines as candidates that the fill judges like the picked lines.</summary>
        [MethodImpl(Hot)]
        private static List<Cand> FaceLines(Grid g, List<Lift> lifts, List<float2> lat, bool[] covered, List<float3> onPts, List<float2> runPts)
        {
            var o = new List<Cand>();
            var traced = new PointHash();
            // per cell, two values. The first is the highest point a skier is on within FillForkM (the fork rule: at most 3 m above it).
            // The second is whether a run lies within MergeM
            int cellCount = g.W * g.D;
            var onY = new float[cellCount];
            FillAll(onY, float.NegativeInfinity);
            var nearRun = new bool[cellCount];
            void Stamp(float2 q, float r, Action<int> f)
            {
                int c = CellOf(g, q);
                if (c < 0)
                    return;
                int k = (int)Math.Ceiling(r / g.CellM), x0 = c % g.W, z0 = c / g.W;
                for (int dz = -k; dz <= k; dz++)
                    for (int dx = -k; dx <= k; dx++)
                    {
                        int x = x0 + dx, z = z0 + dz;
                        if (x >= 0 && z >= 0 && x < g.W && z < g.D && math.distancesq(g.World(z * g.W + x), q) < r * r)
                            f(z * g.W + x);
                    }
            }
            foreach (float3 q in onPts)
                Stamp(q.xz, FillForkM, c => onY[c] = Math.Max(onY[c], q.y));
            foreach (float2 q in runPts)
                Stamp(q, MergeM, c => nearRun[c] = true);
            var nearTop = new bool[cellCount];
            foreach (Lift lf in lifts)
                Stamp(lf.Top.xz, DirectStartM, c => nearTop[c] = true);
            int Step(int c, bool up)
            {
                int x = c % g.W, z = c / g.W, best = -1;
                float bh = g.H[c];
                for (int k = 0; k < 8; k++)
                {
                    int xx = x + DX[k], zz = z + DZ[k];
                    if (xx < 0 || zz < 0 || xx >= g.W || zz >= g.D)
                        continue;
                    int nb = zz * g.W + xx;
                    if (float.IsNaN(g.Hr[nb]))
                        continue;
                    float d = (g.H[nb] - g.H[c]) / ((k & 1) == 1 ? 1.4142f : 1f);
                    if (up ? d > 0.05f && g.H[nb] > bh : d < -0.05f && g.H[nb] < bh)
                    {
                        best = nb;
                        bh = g.H[nb];
                    }
                }
                // a fall line that leaves the region ends at its edge instead of sliding along it
                return best >= 0 && g.In[best] ? best : -1;
            }
            for (int i = 0; i < lat.Count; i++)
            {
                if (covered[i] || traced.Near(lat[i], 40f))
                    continue;
                int c0 = CellOf(g, lat[i]);
                if (c0 < 0)
                    continue;
                // up to where the skier is
                var up = new List<int> { c0 };
                bool found = false;
                for (int c = c0, n = 0; n < 250; n++)
                {
                    if (g.Hr[c] <= onY[c] + 3f || nearTop[c])
                    {
                        found = true;
                        break;
                    }
                    c = Step(c, true);
                    if (c < 0)
                        break;
                    up.Add(c);
                }
                if (!found || up.Count < 2)
                    continue;
                // down until it runs along a run for a while (the fill ends it where it runs onto one) or reaches a station
                var cells = Enumerable.Reverse(up).ToList();
                bool away = false;
                for (int c = c0, n = 0, along = 0; n < 400; n++)
                {
                    c = Step(c, false);
                    if (c < 0)
                        break;
                    cells.Add(c);
                    float2 q = g.World(c);
                    if (lifts.Any(lf => math.distancesq(lf.Bottom.xz, q) < 40f * 40f))
                        break;
                    if (!nearRun[c])
                        away = true;
                    else if (away && ++along >= 4)
                        break;
                }
                List<float2> way = Polyline(g, cells, false);
                foreach (float2 q in way)
                    traced.Add(q);
                if (way.Count < 20)
                    continue;
                var wh = way.Select(q => RawH(g, q)).ToArray();
                int end = -1;
                for (int k = 5; k < way.Count && end < 0; k++)
                    foreach (Lift lf in lifts)
                        if (math.distancesq(lf.Bottom.xz, way[k]) < 50f * 50f && wh[k] >= RawH(g, lf.Bottom.xz) - 10f)
                        {
                            end = k;
                            break;
                        }
                int by = 0;
                for (int L = 1; L < lifts.Count; L++)
                    if (math.distancesq(lifts[L].Top.xz, way[0]) < math.distancesq(lifts[by].Top.xz, way[0]))
                        by = L;
                o.Add(new Cand
                {
                    L = new Line { Id = -1, Family = "face", Width = 60f, Lift = by }, Way = way, Wh = wh, LineEnd = way.Count - 1, Entry = 0, By = by,
                    ValidEnd = way.Count - 1, End = end,
                });
            }
            return o;
        }

        private const float DiagGrade = 0.30f;   // the grade a diagonal blue run holds along its path (blue reads 0.25 to 0.40)

        /// <summary>The smoothed ground's gradient at p (rise per meter along x and z).</summary>
        private static float2 GradVec(Grid g, float2 p)
        {
            float e = 2f * g.CellM;
            return new float2(HeightAt(g, p + new float2(e, 0f)) - HeightAt(g, p - new float2(e, 0f)),
                HeightAt(g, p + new float2(0f, e)) - HeightAt(g, p - new float2(0f, e))) / (2f * e);
        }

        /// <summary>A diagonal from p across the slope to one side, holding DiagGrade down the smoothed ground (straight down where the
        /// ground is gentler, turning at most 45 degrees per 10 m). It ends when it comes within 30 m of a run 120 m or more on, or at a
        /// lift bottom. It returns null when it leaves the region, turns back on itself or runs 1.5 km.</summary>
        [MethodImpl(Hot)]
        private static List<float2> DiagonalTrace(Grid g, float2 p, int side, PointHash runs, List<Lift> lifts)
        {
            var pts = new List<float2> { p };
            float2 prev = 0f;
            for (int step = 1; step <= 150; step++)
            {
                float2 gr = GradVec(g, p);
                float m = math.length(gr);
                if (m < 0.02f)
                    return null;
                float2 f = -gr / m, c = side * new float2(-f.y, f.x);
                float2 dir = m <= DiagGrade ? f : math.sqrt(1f - DiagGrade * DiagGrade / (m * m)) * c + DiagGrade / m * f;
                if (step > 1 && math.dot(dir, prev) < 0.707f)
                    dir = math.normalize(prev + dir);
                prev = dir;
                p += 10f * dir;
                int cell = CellOf(g, p);
                if (cell < 0 || !g.In[cell] || float.IsNaN(RawH(g, p)))
                    return null;
                for (int k = 0; k < pts.Count - 8; k++)
                    if (math.distancesq(pts[k], p) < 400f)
                        return null;
                pts.Add(p);
                if ((step >= 12 && runs.Near(p, 30f)) || lifts.Exists(lf => math.distancesq(lf.Bottom.xz, p) < 1600f))
                    return pts;
            }
            return null;
        }

        [MethodImpl(Hot)]
        private int FillCover(Grid g, PlanDraft plan, List<Lift> lifts, List<Cand> cands)
        {
            SkiGraph sg = Ski(g, plan, lifts);
            static long Key(float2 p) => ((long)Math.Floor(p.x / 60f) << 32) ^ (uint)(int)Math.Floor(p.y / 60f);

            // the skiable 20 m lattice and what the served runs already open of it
            int s = Math.Max(1, (int)Math.Round(20f / g.CellM));
            var lat = new List<float2>();
            for (int z = s / 2; z < g.D; z += s)
                for (int x = s / 2; x < g.W; x += s)
                {
                    int c = z * g.W + x;
                    if (!g.In[c])
                        continue;
                    float2 p = g.World(c);
                    float gr = RawGrade(g, p);
                    if (gr >= 0.06f && gr <= 0.85f)
                        lat.Add(p);
                }
            var latAt = new Dictionary<long, List<int>>();
            for (int i = 0; i < lat.Count; i++)
            {
                if (!latAt.TryGetValue(Key(lat[i]), out List<int> l))
                    latAt[Key(lat[i])] = l = new List<int>();
                l.Add(i);
            }
            var covered = new bool[lat.Count];
            var stamp = new int[lat.Count];
            int stampNow = 0, have = 0;
            // calls f for every lattice point within FillCoverM of p
            void Around(float2 p, Action<int> f)
            {
                int cx = (int)Math.Floor(p.x / 60f), cz = (int)Math.Floor(p.y / 60f);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (latAt.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out List<int> l))
                            foreach (int i in l)
                                if (math.distancesq(lat[i], p) < FillCoverM * FillCoverM)
                                    f(i);
            }
            var runs = new PointHash();                        // served run samples
            var on = new Dictionary<long, List<float3>>();    // samples of every element a skier is on (reached and returning)
            var onLists = new List<List<float3>>();             // on's lists in creation order: the enumerations read this
            void AddOn(float3 p)
            {
                if (!on.TryGetValue(Key(p.xz), out List<float3> l))
                {
                    on[Key(p.xz)] = l = new List<float3>();
                    onLists.Add(l);
                }
                l.Add(p);
            }
            void Open(float2 p) => Around(p, i =>
            {
                if (!covered[i])
                {
                    covered[i] = true;
                    have++;
                }
            });
            for (int i = 0; i < sg.P.Count; i++)
            {
                if (!sg.Reached[i] || !sg.Returns[i])
                    continue;
                foreach (float3 p in sg.P[i])
                {
                    AddOn(p);
                    if (sg.IsRun[i])
                    {
                        runs.Add(p.xz);
                        Open(p.xz);
                    }
                }
            }
            // the scorecard's crowding: served run samples every 10 m (each run's last 150 m aside) within 40 m of another run
            var smp = new List<(float2 P, int Run, bool Tail)>();
            var smpAt = new Dictionary<long, List<int>>();
            var crowdedS = new List<bool>();
            var runsH = new HeightHash();
            int nonTail = 0, runIds = 0;
            void AddSamples(List<float3> P)
            {
                int id = runIds++;
                for (int k = 0; k < P.Count; k++)
                {
                    bool tail = k >= P.Count - 15;
                    if (!smpAt.TryGetValue(Key(P[k].xz), out List<int> l))
                        smpAt[Key(P[k].xz)] = l = new List<int>();
                    l.Add(smp.Count);
                    smp.Add((P[k].xz, id, tail));
                    crowdedS.Add(false);
                    runsH.Add(P[k]);
                    if (!tail)
                        nonTail++;
                }
            }
            // calls f for every sample within FillCrowdM of q (any run but skip)
            void Crowds(float2 q, int skip, Action<int> f)
            {
                int cx = (int)Math.Floor(q.x / 60f), cz = (int)Math.Floor(q.y / 60f);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (smpAt.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out List<int> l))
                            foreach (int j in l)
                                if (smp[j].Run != skip && math.distancesq(smp[j].P, q) < FillCrowdM * FillCrowdM)
                                    f(j);
            }
            for (int i = 0; i < sg.P.Count; i++)
                if (sg.IsRun[i] && sg.Reached[i] && sg.Returns[i])
                    AddSamples(sg.P[i].ToList());
            for (int j = 0; j < smp.Count; j++)
                if (!smp[j].Tail)
                {
                    bool hit = false;
                    Crowds(smp[j].P, smp[j].Run, _ => hit = true);
                    crowdedS[j] = hit;
                }
            var cstamp = new int[0];
            int cstampNow = 0;
            // the crowded samples a run along pts would add: its own (its last 150 m aside) and the ones it brings beside it
            int CrowdCost(List<float2> pts)
            {
                List<float2> d = Densify(pts);
                if (cstamp.Length < smp.Count)
                    Array.Resize(ref cstamp, smp.Count + 1024);
                cstampNow++;
                int own = 0, beside = 0;
                for (int k = 0; k < d.Count; k++)
                {
                    bool hit = false;
                    Crowds(d[k], -1, j =>
                    {
                        hit = true;
                        if (!smp[j].Tail && !crowdedS[j] && cstamp[j] != cstampNow)
                        {
                            cstamp[j] = cstampNow;
                            beside++;
                        }
                    });
                    if (hit && k < d.Count - 15)
                        own++;
                }
                return own + beside;
            }

            bool OnNear(float2 q, float h)
            {
                int cx = (int)Math.Floor(q.x / 60f), cz = (int)Math.Floor(q.y / 60f);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (on.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out List<float3> l))
                            foreach (float3 p in l)
                                if (math.distancesq(p.xz, q) < FillForkM * FillForkM && h <= p.y + 3f)
                                    return true;
                return false;
            }
            // a straight cut-over to q from an element the skier is on (no lower than q, within the connector rules)
            List<float2> Feeder(float2 q, float h)
            {
                int cx = (int)Math.Floor(q.x / 60f), cz = (int)Math.Floor(q.y / 60f);
                var near = new List<(float D, float2 P)>();
                for (int dz = -2; dz <= 2; dz++)
                    for (int dx = -2; dx <= 2; dx++)
                        if (on.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out List<float3> l))
                            foreach (float3 p in l)
                            {
                                float d = math.distance(p.xz, q);
                                if (d >= FillForkM && d <= FillFeedM && p.y >= h - 1f && p.y <= h + 0.4f * d)
                                    near.Add((d, p.xz));
                            }
                SortStable(near, (a, b) => a.D.CompareTo(b.D));
                for (int k = 0; k < Math.Min(4, near.Count); k++)
                {
                    var c = new List<float2> { near[k].P, q };
                    if (ConnectorOk(g, c))
                        return c;
                }
                return null;
            }
            float servedKm = 0f, connKm = 0f;
            for (int i = 0; i < sg.P.Count; i++)
                if (sg.Reached[i] && sg.Returns[i] && i < sg.Runs)
                    servedKm += RunLength(plan.Runs[i].Pts) / 1000f;
            foreach (List<float2> c in plan.Connectors)
                connKm += RunLength(c) / 1000f;
            var guard = new ChordGuard(lifts);
            foreach (PlanRun r in plan.Runs)
                if (!r.Graded)
                    guard.Add(r.Pts, 0, r.Pts.Count - 1);
            foreach (List<float2> c in plan.Connectors)
                if (TrackClass(g, c).Run)
                    guard.Add(c, 0, c.Count - 1);

            // a candidate's fill run: from where it leaves an element the skier is on, to where it runs onto a run or delivers
            var why = new int[7];
            List<float2> Eval(Cand cd, out int fresh, out List<float2> feed)
            {
                fresh = 0;
                feed = null;
                int st = -1;
                for (int i = cd.Entry; i <= Math.Min(cd.LineEnd, cd.ValidEnd) - 5; i++)
                {
                    float2 q = cd.Way[i];
                    if (lifts.Any(lf => math.distancesq(lf.Top.xz, q) < DirectStartM * DirectStartM) || OnNear(q, cd.Wh[i]) ||
                        ((i - cd.Entry) % 2 == 0 && (feed = Feeder(q, cd.Wh[i])) != null))
                    {
                        st = i;
                        break;
                    }
                }
                if (st < 0)
                {
                    why[0]++;
                    return null;
                }
                int end = cd.End >= st + 5 ? cd.End : -1, lim = end >= 0 ? end : cd.ValidEnd;
                bool away = false, merged = false;
                for (int i = st + 1, run = 0; i <= lim; i++)
                {
                    bool near = runs.Near(cd.Way[i], MergeM);
                    if (!near)
                    {
                        away = true;
                        run = 0;
                    }
                    else if (away && ++run == 3)
                    {
                        end = FirstContact(cd.Way, cd.Wh, st + 5, i - 1, runsH);
                        merged = true;
                        break;
                    }
                }
                if (!away)
                {
                    why[1]++;
                    return null;
                }
                if (end < 0 || (!merged && cd.End < st + 5) || end - st < 5)
                {
                    why[2]++;
                    return null;
                }
                if (cd.Wh[st] - cd.Wh[end] < FillDropM)
                {
                    why[3]++;
                    return null;
                }
                List<float2> pts = cd.Way.GetRange(st, end - st + 1);
                float len = RunLength(pts);
                if (len < FillMinM || (feed != null && RunLength(feed) > FillFeedShare * len))
                {
                    why[3]++;
                    return null;
                }
                int tail = Math.Max(1, pts.Count - 15), crowded = 0;
                for (int k = 0; k < tail; k++)
                    if (runs.Near(pts[k], FillCrowdM))
                        crowded++;
                if (crowded > FillCrowdShare * tail)
                {
                    why[4]++;
                    return null;
                }
                stampNow++;
                int n = 0;
                foreach (float2 p in pts)
                    Around(p, i =>
                    {
                        if (!covered[i] && stamp[i] != stampNow)
                        {
                            stamp[i] = stampNow;
                            n++;
                        }
                    });
                fresh = n;
                if (n < FillNewMin || n < FillNewPerM * len)
                {
                    why[5]++;
                    return null;
                }
                if (n < FillCrowdTrade * CrowdCost(pts) * lat.Count / Math.Max(1, nonTail))
                {
                    why[6]++;
                    return null;
                }
                return pts;
            }

            // best new ground first (lazy: a line's new ground only shrinks as the fill adds runs)
            var queue = new StableHeap<Cand, int>();
            foreach (Cand cd in cands)
                if (!cd.L.Picked && Eval(cd, out int f0, out _) != null)
                    queue.Enqueue(cd, -f0);
            Tick("fill setup");
            int faceLines = 0;
            if (O.FaceLines)
                foreach (Cand cd in FaceLines(g, lifts, lat, covered, onLists.SelectMany(l => l).ToList(), smp.Select(x => x.P).ToList()))
                {
                    faceLines++;
                    if (Eval(cd, out int f0, out _) != null)
                        queue.Enqueue(cd, -f0);
                }
            if (O.Debug)
                Trace($"DEBUG fill: {queue.Count} of {cands.Count} candidate(s) and {faceLines} face line(s); no fork {why[0]}, never leaves {why[1]}, no way home {why[2]}, short {why[3]}, crowded {why[4]}, little new ground {why[5]}, crowding costs more {why[6]}");
            Tick("face lines");
            int added = 0, opened = 0;
            float km = 0f;
            var mine = new List<PlanRun>();
            var feeds = new Dictionary<PlanRun, List<float2>>();
            int fed = 0;
            while (queue.TryDequeue(out Cand cd, out int negKey) && have < 0.62f * lat.Count)
            {
                List<float2> pts = Eval(cd, out int fresh, out List<float2> feed);
                if (pts == null)
                    continue;
                if (queue.TryPeek(out _, out int nextKey) && fresh < -nextKey)
                {
                    queue.Enqueue(cd, -fresh);
                    continue;
                }
                List<float2> ok = SanitizeRun(g, pts);
                if (ok == null || ok.Count < pts.Count || !guard.FitsAdding(pts, 0, pts.Count - 1))
                    continue;
                var run = new PlanRun { Src = cd.L, Pts = pts, Band = RunBand(g, pts), By = lifts[cd.By], Kind = "fill" };
                plan.Runs.Add(run);
                mine.Add(run);
                if (feed != null)
                {
                    plan.Connectors.Add(feed);
                    feeds[run] = feed;
                    foreach (float3 p in Sampled(g, feed))
                        AddOn(p);
                    connKm += RunLength(feed) / 1000f;
                    fed++;
                }
                servedKm += RunLength(pts) / 1000f;
                {
                    List<float3> P = Sampled(g, pts);
                    int first = smp.Count;
                    var beside = new List<int>();
                    var ownHit = new bool[P.Count];
                    for (int k = 0; k < P.Count; k++)
                    {
                        int kk = k;
                        Crowds(P[k].xz, -1, j =>
                        {
                            ownHit[kk] = true;
                            if (!smp[j].Tail)
                                beside.Add(j);
                        });
                    }
                    foreach (int j in beside)
                        crowdedS[j] = true;
                    AddSamples(P);
                    for (int k = 0; k < P.Count; k++)
                        crowdedS[first + k] = ownHit[k] && k < P.Count - 15;
                }
                cd.L.Picked = true;
                guard.Add(pts, 0, pts.Count - 1);
                foreach (float3 p in Sampled(g, pts))
                {
                    AddOn(p);
                    runs.Add(p.xz);
                    Open(p.xz);
                }
                added++;
                opened += fresh;
                km += RunLength(pts) / 1000f;
            }
            // diagonal blue runs (scorecard v3): a graded track that holds a blue grade across a face opens the wedge two converging
            // fall lines leave. From a point a skier is on, it traverses down at a steady grade until it meets a run or a lift bottom
            int diagonals = 0, traces = 0, traced = 0;
            float diagKm = 0f;
            var dwhy = new int[8];
            var diagRuns = new List<PlanRun>();
            if (O.Diagonals && have < 0.62f * lat.Count)
            {
                var dq = new StableHeap<List<float2>, int>();
                var seenStart = new PointHash();
                foreach (List<float3> l in onLists)
                    foreach (float3 p in l)
                    {
                        if (seenStart.Near(p.xz, 45f) || GradeAt(g, p.xz) < DiagGrade + 0.04f)
                            continue;
                        seenStart.Add(p.xz);
                        foreach (int side in new[] { -1, 1 })
                        {
                            List<float2> tr = DiagonalTrace(g, p.xz, side, runs, lifts);
                            traces++;
                            if (tr != null)
                                traced++;
                            if (tr != null && DiagEval(tr, out int f0) != null)
                                dq.Enqueue(tr, -f0);
                        }
                    }
                while (dq.TryDequeue(out List<float2> tr, out _) && have < 0.62f * lat.Count)
                {
                    List<float2> pts = DiagEval(tr, out int fresh);
                    if (pts == null)
                        continue;
                    if (dq.TryPeek(out _, out int nextKey) && fresh < -nextKey)
                    {
                        dq.Enqueue(tr, -fresh);
                        continue;
                    }
                    var run = new PlanRun { Pts = pts, Band = SlopeBand.Blue, By = null, Kind = "diagonal", Graded = true };
                    plan.Runs.Add(run);
                    diagRuns.Add(run);
                    List<float3> P = Sampled(g, pts);
                    int first = smp.Count;
                    var ownHit = new bool[P.Count];
                    for (int k = 0; k < P.Count; k++)
                    {
                        int kk = k;
                        Crowds(P[k].xz, -1, j =>
                        {
                            ownHit[kk] = true;
                            if (!smp[j].Tail)
                                crowdedS[j] = true;
                        });
                    }
                    AddSamples(P);
                    for (int k = 0; k < P.Count; k++)
                        crowdedS[first + k] = ownHit[k] && k < P.Count - 15;
                    guard.Add(pts, 0, pts.Count - 1);
                    foreach (float3 p in P)
                    {
                        AddOn(p);
                        runs.Add(p.xz);
                        Open(p.xz);
                    }
                    diagonals++;
                    if (O.Debug)
                        Trace($"DEBUG diagonal {RunLength(pts):F0} m from {pts[0].x:F0},{pts[0].y:F0} to {pts[pts.Count - 1].x:F0},{pts[pts.Count - 1].y:F0}, {fresh} new lattice points");
                    opened += fresh;
                    diagKm += RunLength(pts) / 1000f;
                }
            }
            if (O.Debug)
                Trace($"DEBUG diagonals: {traces} trace(s), {traced} reached a run or station; not a run {dwhy[0]}, past blue {dwhy[1]}, connector rules {dwhy[2]}, " +
                    $"short {dwhy[3]}, crowded {dwhy[4]}, little new ground {dwhy[5]}, crowding costs more {dwhy[6]}, lift chord {dwhy[7]}");
            // a diagonal's case for a trail. It is a blue diagonal run by the scorecard's reading. It is within the connector rules, not a
            // stub and clear of the lift chords. The case also needs enough new ground for its length, and more than the crowding it
            // brings costs
            List<float2> DiagEval(List<float2> pts, out int fresh)
            {
                fresh = 0;
                var (isRun, band) = TrackClass(g, pts);
                if (!isRun || band != SlopeBand.Blue)
                {
                    dwhy[isRun ? 1 : 0]++;
                    return null;
                }
                if (!ConnectorOk(g, pts))
                {
                    dwhy[2]++;
                    return null;
                }
                float len = RunLength(pts);
                if (len < FillMinM || RawH(g, pts[0]) - RawH(g, pts[pts.Count - 1]) < FillDropM)
                {
                    dwhy[3]++;
                    return null;
                }
                List<float2> d = Densify(pts);
                int tail = Math.Max(1, d.Count - 15), crowded = 0;
                for (int k = 0; k < tail; k++)
                    if (runs.Near(d[k], FillCrowdM))
                        crowded++;
                if (crowded > FillCrowdShare * tail)
                {
                    dwhy[4]++;
                    return null;
                }
                stampNow++;
                int n = 0;
                foreach (float2 p in d)
                    Around(p, i =>
                    {
                        if (!covered[i] && stamp[i] != stampNow)
                        {
                            stamp[i] = stampNow;
                            n++;
                        }
                    });
                fresh = n;
                if (n < FillNewMin || n < FillNewPerM * len)
                {
                    dwhy[5]++;
                    return null;
                }
                if (n < FillCrowdTrade * CrowdCost(pts) * lat.Count / Math.Max(1, nonTail))
                {
                    dwhy[6]++;
                    return null;
                }
                if (!guard.FitsAdding(pts, 0, pts.Count - 1))
                {
                    dwhy[7]++;
                    return null;
                }
                return pts;
            }

            // a fill run the ski graph does not carry back to a lift (or that no skier reaches) goes again
            mine.AddRange(diagRuns);
            for (int round = 0; round < 3 && mine.Count > 0; round++)
            {
                SkiGraph after = Ski(g, plan, lifts);
                var drop = new HashSet<PlanRun>();
                for (int i = 0; i < after.Runs; i++)
                    if (mine.Contains(plan.Runs[i]) && (!after.Reached[i] || !after.Returns[i]))
                        drop.Add(plan.Runs[i]);
                if (drop.Count == 0)
                    break;
                plan.Runs.RemoveAll(drop.Contains);
                foreach (PlanRun r in drop)
                    if (feeds.TryGetValue(r, out List<float2> f))
                    {
                        plan.Connectors.Remove(f);
                        fed--;
                    }
                mine.RemoveAll(drop.Contains);
                added -= drop.Count;
            }
            int faces = mine.Count(r => r.Src != null && r.Src.Family == "face"), diagKept = mine.Count(r => r.Kind == "diagonal");
            plan.Log.Add($"coverage fill: {mine.Count - diagKept} run(s) ({faces} down a face, {fed} from a cut-over), {km:F1} km, and {diagKept} diagonal blue run(s) " +
                $"({diagonals} placed, {diagKm:F1} km) over ground no run opened ({opened} lattice points; {have} of {lat.Count} skiable now open)");
            return mine.Count;
        }

        /// <summary>RF_COVDBG: a map of the skiable ground and what the served runs cover of it, 100 m a character, +z up and +x right
        /// ("#" all covered, "+" half or more, "." less, "o" none, A-Z a lift bottom, a-z its top).</summary>
        private void CoverDebug(Grid g, PlanDraft plan, List<Lift> lifts)
        {
            // the elements the scorecard reads as runs (as the cut model takes them: every one served)
            var runs = new PointHash();
            foreach (PlanRun r in plan.Runs)
                if (!r.Graded || TrackClass(g, r.Pts).Run)
                    foreach (float2 p in Densify(r.Pts))
                        runs.Add(p);
            foreach (List<float2> c in plan.Connectors)
                if (TrackClass(g, c).Run)
                    foreach (float2 p in Densify(c))
                        runs.Add(p);
            int s = Math.Max(1, (int)Math.Round(20f / g.CellM));
            var cells = new Dictionary<(int, int), (int Ski, int Cov)>();
            int x0 = int.MaxValue, x1 = int.MinValue, z0 = int.MaxValue, z1 = int.MinValue, all = 0, cov = 0;
            for (int z = s / 2; z < g.D; z += s)
                for (int x = s / 2; x < g.W; x += s)
                {
                    int c = z * g.W + x;
                    if (!g.In[c])
                        continue;
                    float2 p = g.World(c);
                    int cx = (int)Math.Floor(p.x / 100f), cz = (int)Math.Floor(p.y / 100f);
                    x0 = Math.Min(x0, cx); x1 = Math.Max(x1, cx); z0 = Math.Min(z0, cz); z1 = Math.Max(z1, cz);
                    float gr = RawGrade(g, p);
                    if (gr < 0.06f || gr > 0.85f)
                        continue;
                    bool on = runs.Near(p, 50f);
                    cells.TryGetValue((cx, cz), out var v);
                    cells[(cx, cz)] = (v.Ski + 1, v.Cov + (on ? 1 : 0));
                    all++;
                    if (on)
                        cov++;
                }
            var mark = new Dictionary<(int, int), char>();
            for (int L = 0; L < lifts.Count && L < 26; L++)
            {
                mark[((int)Math.Floor(lifts[L].Bottom.x / 100f), (int)Math.Floor(lifts[L].Bottom.z / 100f))] = (char)('A' + L);
                mark[((int)Math.Floor(lifts[L].Top.x / 100f), (int)Math.Floor(lifts[L].Top.z / 100f))] = (char)('a' + L);
            }
            Trace($"COVDBG {cov} of {all} skiable lattice points covered ({100f * cov / Math.Max(1, all):F0} %, the scorecard's 60 % = full credit)");
            for (int cz = z1; cz >= z0; cz--)
            {
                var sb = new System.Text.StringBuilder("COVDBG ");
                for (int cx = x0; cx <= x1; cx++)
                {
                    if (mark.TryGetValue((cx, cz), out char m))
                    {
                        sb.Append(m);
                        continue;
                    }
                    cells.TryGetValue((cx, cz), out var v);
                    sb.Append(v.Ski == 0 ? ' ' : v.Cov == v.Ski ? '#' : 2 * v.Cov >= v.Ski ? '+' : v.Cov > 0 ? '.' : 'o');
                }
                Trace(sb.ToString().TrimEnd());
            }
            // and the height: 25 m a step above the lowest ground (0-9, then a-z)
            float lo = float.MaxValue;
            for (int c = 0; c < g.W * g.D; c++)
                if (g.In[c])
                    lo = Math.Min(lo, g.H[c]);
            for (int cz = z1; cz >= z0; cz--)
            {
                var sb = new System.Text.StringBuilder("COVDBGH ");
                for (int cx = x0; cx <= x1; cx++)
                {
                    float2 p = new float2(cx * 100f + 50f, cz * 100f + 50f);
                    int c = CellOf(g, p);
                    if (c < 0 || !g.In[c])
                    {
                        sb.Append(' ');
                        continue;
                    }
                    int k = Clamp((int)((g.H[c] - lo) / 25f), 0, 35);
                    sb.Append(k < 10 ? (char)('0' + k) : (char)('a' + k - 10));
                }
                Trace(sb.ToString().TrimEnd());
            }
        }
    }
}
