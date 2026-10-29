// RunsFirst coverage fill (prototype): once the lifts, runs, network and beginner terrain stand, skiable ground a skier gets to can
// still lie away from every run (a broad face between two trunk runs, the gentle side of a valley). A candidate line that leaves a
// run or traverse the skier is already on and goes down over that ground becomes a run when it opens enough new ground for its
// length and keeps its distance from the runs beside it; best new ground first, until no line is worth a trail.

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class RunsFirst
    {
        private const float FillCoverM = 50f;       // ground within this of a run counts as opened (the scorecard's coverage radius)
        private const float FillNewPerM = 0.08f;    // new 20 m lattice points per meter of trail a fill run must open (a third of its band)
        private const int FillNewMin = 25;          // ... and at least this many (a hectare)
        private const float FillCrowdShare = 0.25f, FillCrowdM = 40f;   // at most this share of it within 40 m of another run (its last 150 m aside)
        private const float FillForkM = 30f;        // a fill run leaves a run or traverse within this distance, at most 3 m above it
        private const float FillDropM = 60f, FillMinM = 250f;
        private const float FillFeedM = 110f, FillFeedShare = 0.3f;   // ... or from a straight cut-over this long at most (a share of the run)
        // a fill run must open more ground than the crowding it brings costs: the scorecard weighs coverage 15 against its 60 % target
        // and crowding 5 against a 30 % share, so a crowded 10 m sample costs about 0.67 x (lattice points / run samples) new points
        private static readonly float FillCrowdTrade = Env("RF_FTRADE", 0.67f);

        /// <summary>Fall lines through open ground: from an open lattice point up the steepest ascent until it meets ground a skier is on
        /// (onNear) and down the steepest descent until it has run along a run for a few steps or reached a station; one per stretch of
        /// face (an open point near a line already traced starts none). Returned as candidates the fill judges like the picked lines.</summary>
        private static List<Cand> FaceLines(Grid g, List<Lift> lifts, List<float2> lat, bool[] covered, List<float3> onPts, List<float2> runPts)
        {
            var o = new List<Cand>();
            var traced = new PointHash();
            // per cell: the highest point a skier is on within FillForkM (the fork rule: at most 3 m above it), and whether a run
            // lies within MergeM
            int cellCount = g.W * g.D;
            var onY = new float[cellCount];
            Array.Fill(onY, float.NegativeInfinity);
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
                // down until it has run along a run for a while (the fill ends it where it runs onto one) or reaches a station
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

        private static int FillCover(Grid g, Plan plan, List<Lift> lifts, List<Cand> cands)
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
            void AddOn(float3 p)
            {
                if (!on.TryGetValue(Key(p.xz), out List<float3> l))
                    on[Key(p.xz)] = l = new List<float3>();
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
                    if (i < sg.Runs)
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
            for (int i = 0; i < sg.Runs; i++)
                if (sg.Reached[i] && sg.Returns[i])
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
                near.Sort((a, b) => a.D.CompareTo(b.D));
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
                guard.Add(r.Pts, 0, r.Pts.Count - 1);

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

            // best new ground first (lazy: a line's new ground only shrinks as runs are added)
            var queue = new PriorityQueue<Cand, int>();
            foreach (Cand cd in cands)
                if (!cd.L.Picked && Eval(cd, out int f0, out _) != null)
                    queue.Enqueue(cd, -f0);
            Tick("fill setup");
            int faceLines = 0;
            if (Environment.GetEnvironmentVariable("RF_NOFACE") != "1")
                foreach (Cand cd in FaceLines(g, lifts, lat, covered, on.Values.SelectMany(l => l).ToList(), smp.Select(x => x.P).ToList()))
                {
                    faceLines++;
                    if (Eval(cd, out int f0, out _) != null)
                        queue.Enqueue(cd, -f0);
                }
            if (Environment.GetEnvironmentVariable("RF_DEBUG") == "1")
                Console.WriteLine($"DEBUG fill: {queue.Count} of {cands.Count} candidate(s) and {faceLines} face line(s); no fork {why[0]}, never leaves {why[1]}, no way home {why[2]}, short {why[3]}, crowded {why[4]}, little new ground {why[5]}, crowding costs more {why[6]}");
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
            // a fill run the ski graph does not carry back to a lift (or that no skier reaches) goes again
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
            int faces = mine.Count(r => r.Src.Family == "face");
            plan.Log.Add($"coverage fill: {added} run(s) ({faces} down a face, {fed} from a cut-over), {km:F1} km over ground no run opened ({opened} lattice points; {have} of {lat.Count} skiable now open)");
            return added;
        }
    }
}
