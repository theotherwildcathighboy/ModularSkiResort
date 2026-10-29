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
                        end = i - 1;
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
                if (n >= FillNewMin && n >= FillNewPerM * len)
                    return pts;
                why[5]++;
                return null;
            }

            // best new ground first (lazy: a line's new ground only shrinks as runs are added)
            var queue = new PriorityQueue<Cand, int>();
            foreach (Cand cd in cands)
                if (!cd.L.Picked && Eval(cd, out int f0, out _) != null)
                    queue.Enqueue(cd, -f0);
            if (Environment.GetEnvironmentVariable("RF_DEBUG") == "1")
                Console.WriteLine($"DEBUG fill: {queue.Count} of {cands.Count} candidate(s); no fork {why[0]}, never leaves {why[1]}, no way home {why[2]}, short {why[3]}, crowded {why[4]}, little new ground {why[5]}");
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
            plan.Log.Add($"coverage fill: {added} run(s) ({fed} from a cut-over), {km:F1} km over ground no run opened ({opened} lattice points; {have} of {lat.Count} skiable now open)");
            return added;
        }
    }
}
