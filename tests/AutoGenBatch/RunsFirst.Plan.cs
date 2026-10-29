// RunsFirst plan (prototype): turns the candidate lines and the placed lifts into the plan a skier would see (and the scorecard reads).
// The serve pass re-picks the runs knowing the lifts: every candidate line (picked or not) is trimmed to where the highest lift
// traverse meets it and ends where it delivers to a lift bottom (on down its drainage when that is where the lift is) or runs onto
// an earlier run; the best of these are kept with the pick's spacing rules. Each lift's traverses are one shared tree (a trunk
// along the ridge, short spurs to each run). Every piece is checked on the raw terrain with the scorecard's own sampling, less a
// margin, before it goes into the plan.

using System;
using System.Collections.Generic;
using System.Linq;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class RunsFirst
    {
        private const float ConnPitchMax = 0.46f, ConnRiseMax = 4.0f;   // connector rules (no 30 m pitch over 0.50, no 4.5 m rise in 50 m)
        private const float RunClimbMax = 9.5f, RunPitchMax = 0.97f;    // run rules (no 10 m climb, no 30 m pitch over 1.0)
        private const float DirectStartM = 140f;   // a run starting this close to a lift top needs no traverse (the scorecard's 150 m)
        private const float DeliverM = 50f;        // a run delivers to a lift when it passes this close to the bottom station (scorecard 60 m)
        private const float MinRunDropM = 55f;     // shorter served runs read as stubs (the scorecard counts under 50 m)
        private static readonly float ServeCapPerKm2 = Env("RF_SCAP", 8f);
        private static readonly float RunOutM = Env("RF_RUNOUT", 1600f);   // how far below its first station a run may go on to others
        private static readonly float ChordDefer = Env("RF_CDEF", 0.35f);   // value share of a run that lies along a lift line
        private static readonly float ChordKeep = Env("RF_CKEEP", 0.5f);    // ... kept anyway at this share of the best run's value

        private sealed class PlanRun
        {
            public Line Src;                 // the candidate line it comes from (null for runs the plan adds)
            public List<float2> Pts;         // skiing direction
            public SlopeBand Band;
            public Lift By;                  // the lift whose traverse (or top) the run starts from
            public string Kind = "line";
        }

        private sealed class Plan
        {
            public readonly List<PlanRun> Runs = new List<PlanRun>();
            public readonly List<List<float2>> Connectors = new List<List<float2>>();
            public readonly List<List<float2>> Links = new List<List<float2>>();   // cat tracks between lifts (not in a traverse tree)
            public readonly List<string> Log = new List<string>();
            public readonly List<(float2 P, int R, int G, int B)> Dots = new List<(float2, int, int, int)>();   // RF_DEBUG marks for the render
        }

        /// <summary>A candidate line as the serve pass sees it: its way down (the line, then its drainage, points every 10 m at
        /// most), raw heights, the highest lift entry and the end where it delivers to a lift.</summary>
        private sealed class Cand
        {
            public Line L;
            public List<float2> Way;
            public float[] Wh;
            public int LineEnd;              // index of the line's own last point in Way
            public int Entry = -1, By = -1;  // highest entry over all lifts, and that lift
            public int[] EntryOf;            // per lift (-1 = not reached)
            public int ValidEnd;             // the way holds the run rules (from the entry) up to here
            public int End = -1;             // where it delivers to a lift bottom (-1 = nowhere)
            public float Value;
        }

        private static Plan BuildPlan(Grid g, int[] rec, List<Line> lines, List<Lift> lifts, float km2)
        {
            var plan = new Plan();
            int n = g.W * g.D;
            var slope = new float[n];
            for (int c = 0; c < n; c++)
                if (g.In[c])
                    slope[c] = GradeAt(g, g.World(c));

            // 1. every lift top's reach (the honest traverse model), kept as parent pointers
            var dist = new float[n];
            Array.Fill(dist, float.PositiveInfinity);
            var parent = new int[n];
            var reachOf = new Dictionary<int, int>[lifts.Count];
            for (int L = 0; L < lifts.Count; L++)
            {
                List<int> touched = Reach(g, slope, lifts[L].TopCell, dist, parent);
                var d = new Dictionary<int, int>(touched.Count);
                foreach (int c in touched)
                {
                    d[c] = parent[c];
                    dist[c] = float.PositiveInfinity;
                }
                reachOf[L] = d;
            }
            var botH = lifts.ConvertAll(lf => RawH(g, lf.Bottom.xz));

            // 2. candidates: every line with a lift entry, trimmed and ended where it delivers
            var cands = new List<Cand>();
            foreach (Line l in lines)
            {
                if (l.Flow == null)
                {
                    l.Flow = new List<float2>();
                    int c0 = CellOf(g, l.Pts[l.Pts.Count - 1].xz);
                    for (int c = c0, k = 0; c >= 0; c = rec[c], k++)
                        if (k % 3 == 0 && k > 0)
                            l.Flow.Add(g.World(c));
                }
                var cd = new Cand { L = l, EntryOf = new int[lifts.Count] };
                var way = new List<float2>(l.Pts.Count + l.Flow.Count);
                foreach (float3 p in l.Pts)
                    way.Add(p.xz);
                cd.LineEnd = Densify(way).Count - 1;
                way.AddRange(l.Flow);
                cd.Way = Densify(way);
                for (int L = 0; L < lifts.Count; L++)
                {
                    cd.EntryOf[L] = -1;
                    for (int k = 0; k <= cd.LineEnd; k++)
                    {
                        int c = CellOf(g, cd.Way[k]);
                        if (c >= 0 && reachOf[L].ContainsKey(c))
                        {
                            cd.EntryOf[L] = k;
                            break;
                        }
                    }
                    if (cd.EntryOf[L] >= 0 && (cd.Entry < 0 || cd.EntryOf[L] < cd.Entry ||
                        (cd.EntryOf[L] == cd.Entry && math.distance(lifts[L].Top.xz, cd.Way[cd.Entry]) < math.distance(lifts[cd.By].Top.xz, cd.Way[cd.Entry]))))
                    {
                        cd.Entry = cd.EntryOf[L];
                        cd.By = L;
                    }
                }
                if (cd.Entry < 0 || cd.Entry > cd.LineEnd - 5)
                    continue;
                cd.Wh = cd.Way.ConvertAll(p => RawH(g, p)).ToArray();
                // the run rules from the entry: no climb over RunClimbMax above the low point so far, no pitch over RunPitchMax
                cd.ValidEnd = cd.Way.Count - 1;
                int lowAt = cd.Entry;
                for (int i = cd.Entry + 1; i < cd.Way.Count; i++)
                {
                    bool climb = float.IsNaN(cd.Wh[i]) || cd.Wh[i] > cd.Wh[lowAt] + RunClimbMax;
                    if (climb || (i >= cd.Entry + 3 && (cd.Wh[i - 3] - cd.Wh[i]) / math.max(1f, math.distance(cd.Way[i - 3], cd.Way[i])) > RunPitchMax))
                    {
                        cd.ValidEnd = climb ? lowAt : i - 3;
                        break;
                    }
                    if (cd.Wh[i] < cd.Wh[lowAt])
                        lowAt = i;
                }
                // the end: through the station area of each lift bottom the way passes; below the line's own end it goes on down
                // the drainage only while that passes other lifts' bottoms within RunOutM (a base whose stations are spread, or a
                // valley run to the base below, links them all)
                var passed = new HashSet<int>();
                float beyond = -1f;
                for (int i = cd.Entry + 5; i <= cd.ValidEnd; i++)
                {
                    bool any = false, fresh = false;
                    for (int L = 0; L < lifts.Count; L++)
                        if (math.distancesq(cd.Way[i], lifts[L].Bottom.xz) < DeliverM * DeliverM && cd.Wh[i] >= botH[L] - 12f)
                        {
                            any = true;
                            fresh |= passed.Add(L);
                        }
                    if (any && (fresh || cd.End == i - 1))
                        cd.End = i;
                    if (beyond >= 0f)
                        beyond += math.distance(cd.Way[i - 1], cd.Way[i]);
                    else if (any && i >= cd.LineEnd)
                        beyond = 0f;
                    if (beyond > RunOutM)
                        break;
                }
                cd.Value = CandValue(g, cd, cd.End >= 0 ? cd.End : Math.Min(cd.LineEnd, cd.ValidEnd));
                cands.Add(cd);
            }

            // 3. the serve pick: best first; a run that runs onto an earlier one ends there (it delivers where that one does)
            cands.Sort((a, b) => b.Value.CompareTo(a.Value));
            var taken = new PointHash();
            int cap = Math.Clamp((int)Math.Round(ServeCapPerKm2 * km2), 12, 100), picks = 0;
            foreach (Line l in lines)
            {
                l.Picked = false;
                l.Lift = -1;
                l.Pick = 0;
                l.Merge = false;
            }
            var chosen = new List<(Cand C, int End)>();
            var chords = new ChordGuard(lifts);
            // a run that would lie along a lift line waits (its value cut to ChordDefer) for the runs that don't; when it comes
            // up again it is kept anyway and that lift line stops being guarded (one violation, not a lost trunk run)
            var queue = new PriorityQueue<(Cand C, bool Deferred), float>();
            float topValue = cands.Count > 0 ? cands[0].Value : 0f;
            foreach (Cand cd in cands)
                queue.Enqueue((cd, false), -cd.Value);
            while (picks < cap && queue.TryDequeue(out var item, out float negValue))
            {
                Cand cd = item.C;
                int end = cd.End, run = 0;
                for (int i = cd.Entry + 3; i <= Math.Min(cd.ValidEnd, end >= 0 ? end : cd.ValidEnd); i++)
                {
                    run = taken.Near(cd.Way[i], MergeM) ? run + 1 : 0;
                    if (run == 3)
                    {
                        end = i - 1;
                        cd.L.Merge = true;
                        break;
                    }
                }
                if (end < 0 || end - cd.Entry < 5 || cd.Wh[cd.Entry] - cd.Wh[end] < MinRunDropM)
                {
                    cd.L.Merge = false;
                    continue;
                }
                int upper = Math.Max(1, (int)((end - cd.Entry) * 0.6f)), near = 0;
                for (int i = cd.Entry; i < cd.Entry + upper; i++)
                    if (taken.Near(cd.Way[i], SpacingM))
                        near++;
                if (near > OverlapShare * upper)
                {
                    cd.L.Merge = false;
                    continue;
                }
                if (!chords.Fits(cd.Way, cd.Entry, end))
                {
                    cd.L.Merge = false;
                    if (!item.Deferred)
                    {
                        queue.Enqueue((cd, true), negValue * ChordDefer);
                        continue;
                    }
                    // worth a violation: a lift's only terrain, or one of the region's major runs
                    if (chosen.Any(x => x.C.By == cd.By) && cd.Value < ChordKeep * topValue)
                        continue;
                    chords.Spend(cd.Way, cd.Entry, end);
                }
                chords.Add(cd.Way, cd.Entry, end);
                chosen.Add((cd, end));
                for (int i = cd.Entry; i <= end; i++)
                    taken.Add(cd.Way[i]);
                cd.L.Picked = true;
                cd.L.Pick = ++picks;
                cd.L.Lift = lifts[cd.By].Id;
                plan.Runs.Add(new PlanRun { Src = cd.L, Pts = cd.Way.GetRange(cd.Entry, end - cd.Entry + 1), By = lifts[cd.By] });
            }
            foreach (PlanRun r in plan.Runs)
                r.Band = RunBand(g, r.Pts);

            // 4. traverses: per lift, the tree of the paths to the runs it opens (and, for a lift that opens none, to the nearest run
            // its reach touches, so no lift is left without terrain)
            var need = new List<List<int>>[lifts.Count];
            for (int L = 0; L < lifts.Count; L++)
                need[L] = new List<List<int>>();
            var opens = new int[lifts.Count];
            foreach (var (cd, end) in chosen)
            {
                opens[cd.By]++;
                if (math.distance(lifts[cd.By].Top.xz, cd.Way[cd.Entry]) > DirectStartM)
                    need[cd.By].Add(PathHome(g, reachOf[cd.By], cd.Way[cd.Entry], lifts[cd.By].TopCell));
            }
            for (int L = 0; L < lifts.Count; L++)
            {
                if (opens[L] > 0)
                    continue;
                (Cand C, int End) bestRun = default;
                float bestDrop = 0f;
                foreach (var (cd, end) in chosen)
                    if (cd.EntryOf[L] >= 0 && cd.EntryOf[L] < end - 5 && cd.Wh[cd.EntryOf[L]] - cd.Wh[end] > bestDrop)
                    {
                        bestDrop = cd.Wh[cd.EntryOf[L]] - cd.Wh[end];
                        bestRun = (cd, end);
                    }
                if (bestRun.C != null)
                {
                    float2 at = bestRun.C.Way[bestRun.C.EntryOf[L]];
                    if (math.distance(lifts[L].Top.xz, at) > DirectStartM)
                        need[L].Add(PathHome(g, reachOf[L], at, lifts[L].TopCell));
                }
            }
            int chains = 0, badChains = 0;
            for (int L = 0; L < lifts.Count; L++)
                EmitTree(g, need[L], lifts[L].TopCell, plan.Connectors, ref chains, ref badChains);
            // the network: traverses and routes where lifts cannot get to each other; a station no run passes moves onto a run-out
            int links = LinkLifts(g, plan, lifts, reachOf, need, ref chains, ref badChains);
            links += LinkByRoutes(g, plan, lifts);
            if (ReseatBottoms(g, plan, lifts, need) > 0)
            {
                plan.Connectors.Clear();
                chains = badChains = 0;
                for (int L = 0; L < lifts.Count; L++)
                    EmitTree(g, need[L], lifts[L].TopCell, plan.Connectors, ref chains, ref badChains);
                plan.Connectors.AddRange(plan.Links);
                links += LinkLifts(g, plan, lifts, reachOf, need, ref chains, ref badChains);
                links += LinkByRoutes(g, plan, lifts);
            }
            int spurs = Environment.GetEnvironmentVariable("RF_NOSPUR") == "1" ? 0 : MergeSpurs(g, plan, lifts);
            if (Environment.GetEnvironmentVariable("RF_NOPOD") != "1")
            {
                AddBeginnerPods(g, plan, lifts);
                // a pod's top can open a way into an area the main lifts could not get to
                links += LinkByRoutes(g, plan, lifts);
            }
            int fills = Environment.GetEnvironmentVariable("RF_NOFILL") == "1" ? 0 : FillCover(g, plan, lifts, cands);
            if (Environment.GetEnvironmentVariable("RF_COVER") == "1")
            {
                // skiable 20 m lattice points no run covers: red where a lift top's reach gets to them, orange where none does
                var near = new PointHash();
                foreach (PlanRun r in plan.Runs)
                    foreach (float2 p in Densify(r.Pts))
                        near.Add(p);
                int inReach = 0, outReach = 0, skiable = 0;
                for (int c = 0; c < g.W * g.D; c++)
                {
                    float2 p = g.World(c);
                    if (!g.In[c] || Math.Abs(p.x % 20f) > g.CellM / 2f || Math.Abs(p.y % 20f) > g.CellM / 2f)
                        continue;
                    float gr = RawGrade(g, p);
                    if (gr < 0.06f || gr > 0.85f)
                        continue;
                    skiable++;
                    if (near.Near(p, 50f))
                        continue;
                    bool reached = reachOf.Any(d => d.ContainsKey(c));
                    if (reached)
                        inReach++;
                    else
                        outReach++;
                    plan.Dots.Add((p, 255, reached ? 0 : 160, 0));
                }
                Console.WriteLine($"DEBUG cover: {skiable} skiable, {inReach} uncovered in a lift's reach, {outReach} uncovered out of every reach");
            }
            if (Environment.GetEnvironmentVariable("RF_DEBUG") == "1")
            {
                SkiGraph sg = Ski(g, plan, lifts);
                for (int i = 0; i < sg.Runs; i++)
                    if (!sg.Reached[i] || !sg.Returns[i])
                    {
                        PlanRun r = plan.Runs[i];
                        Console.WriteLine($"DEBUG run #{r.Src?.Pick} by L{r.By?.Id}: reached {sg.Reached[i]} returns {sg.Returns[i]}, start {r.Pts[0].x:F0},{r.Pts[0].y:F0} " +
                            $"end {r.Pts[r.Pts.Count - 1].x:F0},{r.Pts[r.Pts.Count - 1].y:F0} merge {r.Src?.Merge} top-dist {math.distance(r.By.Top.xz, r.Pts[0]):F0}");
                    }
                for (int L = 0; L < lifts.Count && L < reachOf.Length; L++)
                {
                    float2 bp = lifts[L].Bottom.xz;
                    float near = float.MaxValue;
                    foreach (PlanRun r in plan.Runs)
                        foreach (float2 p in r.Pts)
                            near = Math.Min(near, math.distance(p, bp));
                    var by = Enumerable.Range(0, reachOf.Length).Where(X => X != L && reachOf[X].ContainsKey(lifts[L].BottomCell)).Select(X => lifts[X].Id);
                    Console.WriteLine($"DEBUG L{lifts[L].Id} bottom {bp.x:F0},{bp.y:F0} h {RawH(g, bp):F0} top {lifts[L].Top.x:F0},{lifts[L].Top.z:F0} h {RawH(g, lifts[L].Top.xz):F0}; nearest run point {near:F0} m; bottom in reach of [{string.Join(",", by)}]");
                }
                for (int L = 0; L < lifts.Count; L++)
                    Console.WriteLine($"DEBUG L{lifts[L].Id}: reaches [{string.Join(",", sg.Reaches[L].Select(b => lifts[b].Id))}] comp {sg.Component[L]} dead {Dead(sg, L)}");
            }
            plan.Log.Add($"serve pass: {cands.Count} candidate(s) with a lift entry, {plan.Runs.Count} run(s) kept (cap {cap}); " +
                $"{badChains} of {chains} traverse piece(s) fail the raw connector rules; {links} link(s) added between lifts; {spurs} spur(s) skied as the start of their run");
            return plan;
        }

        /// <summary>The middle 70 % of every lift chord, sampled every 10 m as the scorecard does: no run set may lie along more
        /// than ChordShare of it (the scorecard flags 30 % within 20 m; a run under the lift line is fine, one along it is not).</summary>
        private sealed class ChordGuard
        {
            private const float ShareMax = 0.25f, NearM = 23f;
            private readonly List<float2[]> mid = new List<float2[]>();
            private readonly List<bool[]> hit = new List<bool[]>();
            private readonly List<bool> spent = new List<bool>();

            public ChordGuard(List<Lift> lifts)
            {
                foreach (Lift lf in lifts)
                    AddLift(lf.Bottom.xz, lf.Top.xz);
            }

            public void AddLift(float2 b, float2 t)
            {
                var m = new List<float2>();
                float plan2 = math.distance(b, t);
                for (float f = 0.15f; f <= 0.85f; f += 10f / math.max(10f, plan2))
                    m.Add(math.lerp(b, t, f));
                mid.Add(m.ToArray());
                hit.Add(new bool[m.Count]);
                spent.Add(false);
            }

            public bool Fits(List<float2> pts, int from, int to)
            {
                for (int L = 0; L < mid.Count; L++)
                    if (!spent[L] && Over(L, pts, from, to))
                        return false;
                return true;
            }

            /// <summary>For a run added late, with all runs already in: only the lift lines it would itself bring onto (or further over)
            /// the share count; a line the serve pass already gave over to runs does not block a run elsewhere.</summary>
            public bool FitsAdding(List<float2> pts, int from, int to)
            {
                for (int L = 0; L < mid.Count; L++)
                {
                    int n = 0;
                    bool adds = false;
                    for (int k = 0; k < mid[L].Length; k++)
                        if (hit[L][k])
                            n++;
                        else if (Near(pts, from, to, mid[L][k]))
                        {
                            n++;
                            adds = true;
                        }
                    if (adds && n > ShareMax * mid[L].Length)
                        return false;
                }
                return true;
            }

            /// <summary>The lift lines this run takes over the share stop being guarded.</summary>
            public void Spend(List<float2> pts, int from, int to)
            {
                for (int L = 0; L < mid.Count; L++)
                    spent[L] |= Over(L, pts, from, to);
            }

            private bool Over(int L, List<float2> pts, int from, int to)
            {
                int n = 0;
                for (int k = 0; k < mid[L].Length; k++)
                    if (hit[L][k] || Near(pts, from, to, mid[L][k]))
                        n++;
                return n > ShareMax * mid[L].Length;
            }

            public void Add(List<float2> pts, int from, int to)
            {
                for (int L = 0; L < mid.Count; L++)
                    for (int k = 0; k < mid[L].Length; k++)
                        hit[L][k] |= Near(pts, from, to, mid[L][k]);
            }

            private static bool Near(List<float2> pts, int from, int to, float2 q)
            {
                for (int i = from; i <= to; i++)
                    if (math.distancesq(pts[i], q) < NearM * NearM)
                        return true;
                return false;
            }
        }

        /// <summary>The last piece of traverse into a run (from the trunk's fork down to where the run starts) is skied as the run's own
        /// first stretch: the skier leaves the cat track and skis on. Only the stretch clear of other runs joins (two runs leaving one
        /// fork side by side stay on the cat track until they part), and only when the joined run keeps the run rules and stays off
        /// the lift lines; the rest stays a traverse. Returns the spurs merged.</summary>
        private static int MergeSpurs(Grid g, Plan plan, List<Lift> lifts)
        {
            const float clearM = 44f, minM = 30f;
            var guard = new ChordGuard(lifts);
            foreach (PlanRun r in plan.Runs)
                guard.Add(r.Pts, 0, r.Pts.Count - 1);
            // every run's points in 60 m buckets, with the run they belong to
            var at = new Dictionary<long, List<(float2 P, PlanRun R)>>();
            static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
            void Put(float2 p, PlanRun r)
            {
                long k = Key((int)Math.Floor(p.x / 60f), (int)Math.Floor(p.y / 60f));
                if (!at.TryGetValue(k, out var l))
                    at[k] = l = new List<(float2, PlanRun)>();
                l.Add((p, r));
            }
            bool Crowded(float2 p, PlanRun self)
            {
                int cx = (int)Math.Floor(p.x / 60f), cz = (int)Math.Floor(p.y / 60f);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (at.TryGetValue(Key(cx + dx, cz + dz), out var l) && l.Any(q => q.R != self && math.distancesq(q.P, p) < clearM * clearM))
                            return true;
                return false;
            }
            foreach (PlanRun r in plan.Runs)
                foreach (float2 p in Densify(r.Pts))
                    Put(p, r);
            var links = new HashSet<List<float2>>(plan.Links);
            int merged = 0;
            foreach (PlanRun r in plan.Runs)
            {
                float2 entry = g.World(CellOf(g, r.Pts[0]));
                int ci = plan.Connectors.FindIndex(c => !links.Contains(c) && math.distancesq(c[c.Count - 1], entry) < 1f);
                if (ci < 0)
                {
                    if (Environment.GetEnvironmentVariable("RF_DEBUG") == "1")
                        Console.WriteLine($"DEBUG spur: run by L{r.By?.Id} at {r.Pts[0].x:F0},{r.Pts[0].y:F0}: no traverse ends there (nearest end " +
                            $"{plan.Connectors.Select(c => math.distance(c[c.Count - 1], r.Pts[0])).DefaultIfEmpty(-1f).Min():F0} m, top {math.distance(r.By.Top.xz, r.Pts[0]):F0} m)");
                    continue;
                }
                List<float2> spur = Densify(plan.Connectors[ci]);
                int cut = spur.Count - 1;
                while (cut > 0 && !Crowded(spur[cut - 1], r))
                    cut--;
                if (RunLength(spur.GetRange(cut, spur.Count - cut)) < minM)
                    continue;
                var pts = spur.GetRange(cut, spur.Count - 1 - cut);
                pts.AddRange(r.Pts);
                List<float2> ok = SanitizeRun(g, pts);
                if (ok == null || ok.Count < Densify(pts).Count || !guard.FitsAdding(pts, 0, spur.Count - 1 - cut))
                {
                    if (Environment.GetEnvironmentVariable("RF_DEBUG") == "1")
                        Console.WriteLine($"DEBUG spur: run by L{r.By?.Id}: {(ok == null ? "no run" : ok.Count < Densify(pts).Count ? $"cut to {ok.Count}/{Densify(pts).Count}" : "lift line")}");
                    continue;
                }
                guard.Add(pts, 0, spur.Count - 1 - cut);
                for (int i = cut; i < spur.Count - 1; i++)
                    Put(spur[i], r);
                r.Pts = ok;
                r.Band = RunBand(g, ok);
                if (cut > 0)
                    plan.Connectors[ci] = spur.GetRange(0, cut + 1);
                else
                    plan.Connectors.RemoveAt(ci);
                merged++;
            }
            return merged;
        }

        /// <summary>A run cut where it would climb (ends at its low point before the climb) or before a pitch over the run maximum,
        /// checked on the scorecard's 10 m sampling; null when too little is left.</summary>
        private static List<float2> SanitizeRun(Grid g, List<float2> run)
        {
            if (run.Count < 2)
                return null;
            run = Densify(run);
            var h = run.ConvertAll(p => RawH(g, p));
            int end = run.Count - 1, lowAt = 0;
            for (int i = 1; i < run.Count; i++)
            {
                if (float.IsNaN(h[i]) || h[i] > h[lowAt] + RunClimbMax)
                {
                    end = lowAt;
                    break;
                }
                if (h[i] < h[lowAt])
                    lowAt = i;
            }
            for (int i = 0; i + 3 <= end; i++)
                if ((h[i] - h[i + 3]) / math.max(1f, math.distance(run[i], run[i + 3])) > RunPitchMax)
                {
                    end = i;
                    break;
                }
            if (end < 5)
                return null;
            return end == run.Count - 1 ? run : run.GetRange(0, end + 1);
        }

        /// <summary>Drop from the entry to end, weighted by the band (beginner and intermediate vertical is scarce) and the corridor
        /// width; a line that delivers nowhere counts half (it can still end on another run).</summary>
        private static float CandValue(Grid g, Cand cd, int end)
        {
            if (end <= cd.Entry + 3)
                return 0f;
            float drop = cd.Wh[cd.Entry] - cd.Wh[end];
            SlopeBand band = RunBand(g, cd.Way.GetRange(cd.Entry, end - cd.Entry + 1));
            return Math.Max(0f, drop) * Math.Clamp(cd.L.Width / 40f, 0.3f, 1f) * BandWeight(band) * (cd.End >= 0 ? 1f : 0.5f);
        }

        /// <summary>The lift network as a skier rides it: while some lifts cannot get to the main set (or it to them), a traverse from a
        /// lift top whose reach takes in the other lift's bottom station closes the gap (the ridge traverse to the bowl lift, the
        /// cat track from the upper lift to the base). The traverse trees are then emitted again. Returns the traverses added.</summary>
        private static int LinkLifts(Grid g, Plan plan, List<Lift> lifts, Dictionary<int, int>[] reachOf, List<List<int>>[] need, ref int chains, ref int bad)
        {
            int added = 0;
            for (int round = 0; round < 3; round++)
            {
                SkiGraph sg = Ski(g, plan, lifts);
                if (sg.BestSize >= lifts.Count)
                    break;
                var fwd = new HashSet<int>[lifts.Count];
                for (int L = 0; L < lifts.Count; L++)
                    fwd[L] = Closure(L, sg.Reaches);
                var main = Enumerable.Range(0, lifts.Count).Where(L => sg.Component[L] == sg.BestComponent).ToList();
                int before = added;
                foreach (int Y in Enumerable.Range(0, lifts.Count).Where(L => sg.Component[L] != sg.BestComponent).ToList())
                {
                    if (!main.Any(X => fwd[X].Contains(Y)) && AddLink(g, lifts, reachOf, need, main, new List<int> { Y }))
                        added++;
                    if (!main.Any(X => fwd[Y].Contains(X)) && AddLink(g, lifts, reachOf, need, new List<int> { Y }, main))
                        added++;
                }
                if (added == before)
                    break;
                plan.Connectors.Clear();
                chains = bad = 0;
                for (int L = 0; L < lifts.Count; L++)
                    EmitTree(g, need[L], lifts[L].TopCell, plan.Connectors, ref chains, ref bad);
                plan.Connectors.AddRange(plan.Links);
            }
            return added;
        }

        /// <summary>The shortest traverse from a top in froms to a bottom station in tos (a reach cell within 35 m of it, the top
        /// above the station), added to that lift's traverse tree; false when no top reaches any.</summary>
        private static bool AddLink(Grid g, List<Lift> lifts, Dictionary<int, int>[] reachOf, List<List<int>>[] need, List<int> froms, List<int> tos)
        {
            List<int> best = null;
            int bestX = -1;
            foreach (int X in froms)
                foreach (int Z in tos)
                {
                    if (X == Z || lifts[X].Top.y < lifts[Z].Bottom.y + 20f)
                        continue;
                    int c0 = lifts[Z].BottomCell, k = (int)Math.Ceiling(35f / g.CellM);
                    for (int dz = -k; dz <= k; dz++)
                        for (int dx = -k; dx <= k; dx++)
                        {
                            int c = c0 + dz * g.W + dx;
                            if (c < 0 || c >= g.W * g.D || !reachOf[X].ContainsKey(c) || math.distance(g.World(c), g.World(c0)) > 35f)
                                continue;
                            List<int> path = PathHome(g, reachOf[X], g.World(c), lifts[X].TopCell);
                            if (best == null || path.Count < best.Count)
                            {
                                best = path;
                                bestX = X;
                            }
                        }
                }
            if (best == null)
                return false;
            need[bestX].Add(best);
            return true;
        }

        /// <summary>Cells from the reach cell under p up the parent pointers to the top (entry first).</summary>
        private static List<int> PathHome(Grid g, Dictionary<int, int> reach, float2 p, int top)
        {
            var cells = new List<int>();
            int c = CellOf(g, p);
            while (c >= 0 && c != top && reach.TryGetValue(c, out int up) && cells.Count < 2000)
            {
                cells.Add(c);
                c = up;
            }
            cells.Add(top);
            return cells;
        }

        /// <summary>One lift's traverse tree: the union of its paths (entry first, top last), cut at every node (top, entry, fork) into
        /// connectors skied from the top down; each piece checked on the raw terrain.</summary>
        private static void EmitTree(Grid g, List<List<int>> need, int top, List<List<float2>> outConn, ref int chains, ref int bad)
        {
            var kids = new Dictionary<int, int>();
            var seenEdge = new HashSet<long>();
            foreach (List<int> cells in need)
                for (int i = 0; i + 1 < cells.Count; i++)
                    if (seenEdge.Add(((long)cells[i] << 32) | (uint)cells[i + 1]))
                        kids[cells[i + 1]] = kids.TryGetValue(cells[i + 1], out int k) ? k + 1 : 1;
            var nodes = new HashSet<int> { top };
            foreach (List<int> cells in need)
                nodes.Add(cells[0]);
            foreach (var kv in kids)
                if (kv.Value >= 2)
                    nodes.Add(kv.Key);
            var emitted = new HashSet<int>();
            foreach (List<int> cells in need)
            {
                int i = 0;
                while (i < cells.Count - 1 && cells[i] != top && emitted.Add(cells[i]))
                {
                    int j = i + 1;
                    while (j < cells.Count - 1 && !nodes.Contains(cells[j]))
                        j++;
                    List<float2> chain = Polyline(g, cells.GetRange(i, j - i + 1), true);
                    chains++;
                    if (!ConnectorOk(g, chain))
                    {
                        // the smoothing can cut a corner onto steeper ground: the cell centers themselves, then
                        chain = cells.GetRange(i, j - i + 1).ConvertAll(c => g.World(c));
                        chain.Reverse();
                    }
                    if (ConnectorOk(g, chain))
                        outConn.Add(chain);
                    else
                        bad++;
                    i = j;
                }
            }
        }

        /// <summary>World points of a cell chain, smoothed (D8 zigzag) with the ends kept; reversed = from the last cell to the first.</summary>
        private static List<float2> Polyline(Grid g, List<int> cells, bool reversed)
        {
            var raw = new List<float2>(cells.Count);
            for (int i = 0; i < cells.Count; i++)
                raw.Add(g.World(cells[reversed ? cells.Count - 1 - i : i]));
            var o = new List<float2>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                if (i == 0 || i == raw.Count - 1)
                {
                    o.Add(raw[i]);
                    continue;
                }
                float2 s = 0f;
                int k = 0;
                for (int j = Math.Max(0, i - 2); j <= Math.Min(raw.Count - 1, i + 2); j++, k++)
                    s += raw[j];
                o.Add(s / k);
            }
            return o;
        }

        /// <summary>The scorecard's sampling: every 10 m (or less) along each segment, heights from the raw terrain.</summary>
        private static List<float3> Sampled(Grid g, List<float2> pl)
        {
            var pts = new List<float3>();
            foreach (float2 q in Densify(pl))
                pts.Add(new float3(q.x, RawH(g, q), q.y));
            return pts;
        }

        private static bool ConnectorOk(Grid g, List<float2> pl)
        {
            if (pl.Count < 2)
                return false;
            List<float3> p = Sampled(g, pl);
            for (int k = 0; k + 5 < p.Count; k++)
                if (p[k + 5].y - p[k].y > ConnRiseMax)
                    return false;
            for (int k = 0; k + 3 < p.Count; k++)
                if ((p[k].y - p[k + 3].y) / math.max(1f, math.distance(p[k].xz, p[k + 3].xz)) > ConnPitchMax)
                    return false;
            return true;
        }

        /// <summary>A polyline with its points at most 10 m apart (the scorecard samples every 10 m: checks see what it sees).</summary>
        private static List<float2> Densify(List<float2> pl)
        {
            var o = new List<float2>(pl.Count);
            for (int i = 0; i + 1 < pl.Count; i++)
            {
                float len = math.distance(pl[i], pl[i + 1]);
                int k = Math.Max(1, (int)Math.Ceiling(len / 10f));
                for (int j = 0; j < k; j++)
                    o.Add(math.lerp(pl[i], pl[i + 1], j / (float)k));
            }
            o.Add(pl[pl.Count - 1]);
            return o;
        }

        /// <summary>The scorecard's band: the 85th percentile of the downhill 30 m pitches on the raw terrain (flat reads green).</summary>
        private static SlopeBand RunBand(Grid g, List<float2> run)
        {
            List<float3> p = Sampled(g, run);
            var ps = new List<float>();
            for (int k = 0; k + 3 < p.Count; k++)
                ps.Add(Math.Max(0f, p[k].y - p[k + 3].y) / math.max(1f, math.distance(p[k].xz, p[k + 3].xz)));
            ps.Sort();
            SlopeBand b = SkiSlopeClassifier.BandOf(ps.Count > 0 ? ps[(int)(ps.Count * 0.85f)] : 0f);
            return b == SlopeBand.Flat ? SlopeBand.Green : b;
        }
    }
}
