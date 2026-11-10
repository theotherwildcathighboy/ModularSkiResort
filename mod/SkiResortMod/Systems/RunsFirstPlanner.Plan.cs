// RunsFirst plan (prototype): turns the candidate lines and the placed lifts into the plan a skier would see. The scorecard reads the same plan.
// The serve pass re-picks the runs knowing the lifts. It trims every candidate line (picked or not) to where the highest lift
// traverse meets it. A trimmed line ends in one of two places. It ends where it delivers to a lift bottom (on down its drainage
// when that is where the lift is). Or it ends where it runs onto an earlier run.
// The pass keeps the best of these lines with the pick's spacing rules.
// Each lift's traverses are one shared tree (a trunk along the ridge, short spurs to each run). The code checks every piece on
// the raw terrain with the scorecard's own sampling, less a margin. This happens before the piece goes into the plan.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class RunsFirstPlanner
    {
        private const float ConnPitchMax = 0.46f, ConnRiseMax = 4.0f;   // connector rules (no 30 m pitch over 0.50, no 4.5 m rise in 50 m)
        private const float RunClimbMax = 9.5f, RunPitchMax = 0.97f;    // run rules (no 10 m climb, no 30 m pitch over 1.0)
        private const float DirectStartM = 140f;   // a run starting this close to a lift top needs no traverse (the scorecard's 150 m)
        private const float DeliverM = 50f;        // a run delivers to a lift when it passes this close to the bottom station (scorecard 60 m)
        private const float MinRunDropM = 55f;     // shorter served runs read as stubs (the scorecard counts under 50 m)
        private const float ContactM = 39f;        // a merging run ends where it first comes this close to the run it joins (the skier's 40 m)
        private static float ServeCapPerKm2 => O.ServeCapPerKm2;
        private static float RunOutM => O.RunOutM;   // how far below its first station a run may go on to others
        private static float ChordDefer => O.ChordDefer;   // value share of a run that lies along a lift line
        private static float ChordKeep => O.ChordKeep;    // ... kept anyway at this share of the best run's value

        private sealed class PlanRun
        {
            public Line Src;                 // the candidate line it comes from (null for runs the plan adds)
            public List<float2> Pts;         // skiing direction
            public SlopeBand Band;
            public Lift By;                  // the lift whose traverse (or top) the run starts from
            public string Kind = "line";
            public bool Graded;              // cut with the grading tool: handed over as a connector (rated along its path, not by the ground)
            public RunKind Surface = RunKind.Groomed;   // or Natural (ungroomed: black or double black only, scorecard v5)
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

        [MethodImpl(Hot)]
        private static Plan BuildPlan(Grid g, int[] rec, List<Line> lines, List<Lift> lifts, float km2)
        {
            var plan = new Plan();
            WayDbg = O.DebugWayHome ? (g, plan, lifts) : null;
            plan.Dots.AddRange(DebugDots);
            DebugDots.Clear();
            plan.Log.AddRange(EarlyLog);
            EarlyLog.Clear();
            int n = g.W * g.D;
            var slope = new float[n];
            for (int c = 0; c < n; c++)
                if (g.In[c])
                    slope[c] = GradeAt(g, g.World(c));

            // 1. every lift top's reach (the honest traverse model), kept as parent pointers
            var dist = new float[n];
            FillAll(dist, float.PositiveInfinity);
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
                // the end: the way runs through the station area of each lift bottom that it passes. Below the line's own end it goes on down
                // the drainage only while that drainage passes other lifts' bottoms within RunOutM. It then links them all (a base whose stations
                // are spread, or a valley run to the base below).
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

            // 3. the serve pick: best first. A run that runs onto an earlier one ends there. It delivers where that one does.
            SortStable(cands, (a, b) => b.Value.CompareTo(a.Value));
            var taken = new PointHash();
            var takenH = new HeightHash();
            // the cap per km2, and the runs of the ground each coverage lift was placed to open
            int cap = Clamp((int)Math.Round(ServeCapPerKm2 * km2), 12, 100) + (int)Math.Ceiling(ServeCapPerKm2 * lifts.Sum(lf => lf.OpensKm2)), picks = 0;
            foreach (Line l in lines)
            {
                l.Picked = false;
                l.Lift = -1;
                l.Pick = 0;
                l.Merge = false;
            }
            var chosen = new List<(Cand C, int End)>();
            var chords = new ChordGuard(lifts);
            // a run that would lie along a lift line waits for the runs that don't (its value is cut to ChordDefer). When it comes
            // up again, the code keeps it anyway and stops guarding that lift line. This gives one violation, not a lost trunk run.
            var queue = new StableHeap<(Cand C, bool Deferred), float>();
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
                        end = FirstContact(cd.Way, cd.Wh, cd.Entry + 5, i - 1, takenH);
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
                {
                    taken.Add(cd.Way[i]);
                    takenH.Add(new float3(cd.Way[i].x, cd.Wh[i], cd.Way[i].y));
                }
                cd.L.Picked = true;
                cd.L.Pick = ++picks;
                cd.L.Lift = lifts[cd.By].Id;
                plan.Runs.Add(new PlanRun { Src = cd.L, Pts = cd.Way.GetRange(cd.Entry, end - cd.Entry + 1), By = lifts[cd.By] });
            }
            foreach (PlanRun r in plan.Runs)
                r.Band = RunBand(g, r.Pts);

            // 4. traverses: per lift, the tree of the paths to the runs it opens. A lift that opens none gets a tree to the nearest run
            // its reach touches, so that every lift has terrain.
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
            Tick("serve");
            int chains = 0, badChains = 0;
            for (int L = 0; L < lifts.Count; L++)
                EmitTree(g, need[L], lifts[L].TopCell, plan.Connectors, ref chains, ref badChains);
            // the network: traverses and routes where lifts cannot get to each other. A station that no run passes moves onto a run-out.
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
            Tick("links");
            int spurs = !O.Spurs ? 0 : MergeSpurs(g, plan, lifts);
            Tick("spurs");
            if (O.Pods)
            {
                AddBeginnerPods(g, plan, lifts);
                // a pod's top can open a way into an area the main lifts could not get to
                links += LinkByRoutes(g, plan, lifts);
            }
            Tick("pods");
            // an area over a saddle from where the other lifts' skiers collect: a bridge lift
            if (O.Bridges)
                links += BridgeLifts(g, plan, lifts, km2);
            Tick("bridge");
            // a learning pod on the green ground a base's runs leave alone, in the lift slots still free. Off by default (RF_LEARNPOD=1)
            // because it cost more than it gave. Visible 97.84 -> 97.65: owner-1 gets a violation and loses cover and crowding.
            // 80 % outlines 95.75 -> 95.97, 120 % 96.88 -> 96.62, squares unchanged.
            if (O.LearningPods)
                AddLearningPods(g, plan, lifts, km2);
            Tick("learning pods");
            // an easy way home from every lift's top (green cat tracks to a base or a gondola top)
            if (O.WayHome)
                AddWaysHome(g, plan, lifts);
            Tick("way home");
            // a lift still without a way home is a download lift: a gondola or tram
            if (O.Downloads)
                DownloadLifts(g, plan, lifts);
            // a lift with neither (no way home, no download), or with no served run, goes with the skiing only it serves
            if (O.DropStranded)
                DropStrandedLifts(g, plan, lifts, cands);
            // traverses at a run's grade cut as green cat tracks where a bench fits
            if (O.Regrade)
                RegradeTraverses(g, plan, lifts);
            Tick("regrade");
            // a base still without green skiing to lap gets a green cat track down from one of its tops.
            // This applies when the ways home and the regrade gave it none.
            if (O.GradedWays)
                AddGradedLearningWays(g, plan, lifts);
            Tick("learning ways");
            int fills = !O.Fill ? 0 : FillCover(g, plan, lifts, cands);
            Tick("fill");
            // runs left under a lift line move out beside it
            if (O.Chords)
                RepairChords(g, plan, lifts);
            Tick("chords");
            // a line still on runs moves off them where its stations can (the runs stay where they are)
            if (O.Chords && O.Nudge)
                NudgeLines(g, plan, lifts);
            Tick("nudge");
            // and the diagonal graded tracks still under a line that lies on runs are cut as green cat tracks
            if (O.Chords && O.ChordTracks)
                ClearChordTracks(g, plan, lifts);
            Tick("chord tracks");
            // a line still on runs after all that goes, where its runs are other lifts' runs too and the skiing loses nothing else
            int chordDropped = 0;
            if (O.Chords && O.ChordDrop)
                chordDropped = DropStrandedLifts(g, plan, lifts, cands, true);
            Tick("chord drop");
            // a lift the chord drop took out may have held the way down from the peak
            if (O.WayHome && !V3Home && (chordDropped > 0 || PeakWayAgain))
                AddPeakWayHome(g, plan, lifts);
            Tick("peak way");
            // the lifts the plan added or moved (pods, bridges, nudges) get terminals of their own too
            if (O.Spread)
                SpreadTerminals(g, lifts, plan, null);
            Tick("spread");
            // a diagonal stub piece joins the piece it runs into
            if (O.Merge)
                MergeStubPieces(g, plan, lifts);
            Tick("merge");
            // runs whose crowding (or stub) costs more score than the ground they open go
            if (O.Prune)
                PruneRuns(g, plan, lifts);
            Tick("prune");
            // and the green cat tracks run straight where the ground lets them (a route search leaves them stepping between cells)
            if (O.Straight)
                StraightenTracks(g, plan, lifts);
            Tick("straight tracks");
            // merging runs end where they crowd the run they join least. Off by default, because scorecard v2 stopped counting junction
            // zones as crowding. It is score-neutral there, and a painted run should touch the trunk it joins. RF_JUNCTION=1 turns it on.
            if (O.Junctions)
                TrimJunctions(g, plan, lifts);
            Tick("junctions");
            // and runs start where they crowd the runs beside them least. Off by default for the same reason (RF_HEAD=1).
            if (O.Heads)
                for (int round = 0; round < 2 && TrimHeads(g, plan, lifts) > 0; round++)
                {
                }
            Tick("heads");
            // scorecard v5: a learning area (a beginner chair and green runs) at each base short of green runs
            if (O.LearningAreas)
                AddLearningAreas(g, plan, lifts);
            Tick("learning areas");
            MarkNatural(g, plan);
            if (O.DebugChords)
                ChordDebug(g, plan, lifts);
            if (O.DebugModel)
            {
                var cm = new CutModel(g, plan);
                Console.WriteLine("MODELDBG " + cm.Terms());
                Console.WriteLine("MODELDBG crowded: " + cm.CrowdBreakdown());
            }
            if (O.DebugCover)
                CoverDebug(g, plan, lifts);
            if (O.DebugCoverMap)
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
            if (O.DebugCrowd)
            {
                // where the crowding is: each served run sample (its last 150 m aside) near another run, by where on that other run
                SkiGraph cs = Ski(g, plan, lifts);
                var served = Enumerable.Range(0, cs.Runs).Where(i => cs.Reached[i] && cs.Returns[i]).ToList();
                int all = 0, ownStart = 0, nearEnd = 0, nearStart = 0, nearMid = 0, nearEndStation = 0, nearEndHere = 0, atJunction = 0;
                var endBy = new Dictionary<string, int>();
                var byKind = new Dictionary<string, int>();
                foreach (int i in served)
                {
                    float3[] P = cs.P[i];
                    for (int k = 0; k < P.Length - 15; k++)
                    {
                        all++;
                        int hitJ = -1, hitK = -1;
                        foreach (int j in served)
                        {
                            if (j == i)
                                continue;
                            for (int m = 0; m < cs.P[j].Length; m++)
                                if (math.distancesq(cs.P[j][m].xz, P[k].xz) < 1600f)
                                {
                                    hitJ = j;
                                    hitK = m;
                                    break;
                                }
                            if (hitJ >= 0)
                                break;
                        }
                        if (hitJ < 0)
                            continue;
                        string kind = plan.Runs[i].Kind;
                        byKind[kind] = byKind.TryGetValue(kind, out int v) ? v + 1 : 1;
                        if (k < 10)
                            ownStart++;
                        else if (hitK >= cs.P[hitJ].Length - 20)
                        {
                            nearEnd++;
                            float2 e = cs.P[hitJ][cs.P[hitJ].Length - 1].xz;
                            if (lifts.Any(lf => math.distance(lf.Bottom.xz, e) < 80f))
                                nearEndStation++;
                            if (lifts.Any(lf => math.distance(lf.Bottom.xz, P[k].xz) < 200f))
                                nearEndHere++;
                            if (math.distance(e, P[k].xz) < 45f)
                                atJunction++;
                            string jk = plan.Runs[hitJ].Kind + (plan.Runs[hitJ].Src != null && plan.Runs[hitJ].Src.Merge ? "/merge" : "");
                            endBy[jk] = endBy.TryGetValue(jk, out int w) ? w + 1 : 1;
                        }
                        else if (hitK < 10)
                            nearStart++;
                        else
                            nearMid++;
                        plan.Dots.Add((P[k].xz, 255, 0, 255));
                    }
                }
                // how close merging runs end to the run they join, and how many of its samples their end crowds
                var hist = new int[5];
                int ends = 0, endCrowds = 0;
                foreach (int i in served)
                {
                    float2 e = cs.P[i][cs.P[i].Length - 1].xz;
                    float best = float.MaxValue;
                    int within = 0;
                    foreach (int j in served)
                        if (j != i)
                            for (int m = 0; m < cs.P[j].Length - 15; m++)
                            {
                                float d = math.distance(cs.P[j][m].xz, e);
                                best = Math.Min(best, d);
                                if (d < 40f)
                                    within++;
                            }
                    if (best >= 40f)
                        continue;
                    ends++;
                    endCrowds += within;
                    hist[best < 10f ? 0 : best < 20f ? 1 : best < 30f ? 2 : best < 35f ? 3 : 4]++;
                }
                Console.WriteLine($"DEBUG crowd ends: {ends} run(s) end within 40 m of another run's counted samples, {endCrowds} sample(s) within 40 m of those ends; nearest <10 {hist[0]}, 10-20 {hist[1]}, 20-30 {hist[2]}, 30-35 {hist[3]}, 35-40 {hist[4]}");
                Console.WriteLine($"DEBUG crowd: {all} samples, {ownStart + nearEnd + nearStart + nearMid} crowded: own first 100 m {ownStart}, by the last 200 m of another run {nearEnd} ({nearEndStation} of them ending at a station, {nearEndHere} within 200 m of one, {atJunction} within 45 m of its end; by its kind {string.Join(", ", endBy.Select(kv => kv.Key + " " + kv.Value))}), by the first 100 m of another {nearStart}, alongside {nearMid}; by kind {string.Join(", ", byKind.Select(kv => kv.Key + " " + kv.Value))}");
            }
            if (O.Debug)
            {
                SkiGraph sg = Ski(g, plan, lifts);
                for (int i = 0; i < sg.Runs; i++)
                    if (!sg.Reached[i] || !sg.Returns[i])
                    {
                        PlanRun r = plan.Runs[i];
                        Console.WriteLine($"DEBUG run #{r.Src?.Pick} by L{r.By?.Id}: reached {sg.Reached[i]} returns {sg.Returns[i]}, start {r.Pts[0].x:F0},{r.Pts[0].y:F0} " +
                            $"end {r.Pts[r.Pts.Count - 1].x:F0},{r.Pts[r.Pts.Count - 1].y:F0} merge {r.Src?.Merge} top-dist {(r.By != null ? math.distance(r.By.Top.xz, r.Pts[0]) : -1f):F0}");
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
                for (int L = 0; L < lifts.Count; L++)
                {
                    // which runs lie along the chord's middle (the scorecard: 30 % of the 15-85 % samples within 20 m)
                    float2 b0 = lifts[L].Bottom.xz, t0 = lifts[L].Top.xz;
                    float span = math.distance(b0, t0);
                    int nS = 0;
                    var hits = new Dictionary<int, int>();
                    for (float f = 0.15f; f <= 0.85f; f += 10f / Math.Max(10f, span), nS++)
                    {
                        float2 q = math.lerp(b0, t0, f);
                        for (int i = 0; i < plan.Runs.Count; i++)
                            if (Densify(plan.Runs[i].Pts).Any(pp => math.distancesq(pp, q) < 400f))
                                hits[i] = hits.TryGetValue(i, out int v) ? v + 1 : 1;
                    }
                    if (hits.Count > 0)
                        Console.WriteLine($"DEBUG L{lifts[L].Id} chord ({nS} samples): " + string.Join(", ", hits.Select(kv => $"run {kv.Key} {plan.Runs[kv.Key].Kind} by L{plan.Runs[kv.Key].By?.Id} {kv.Value}")));
                }
            }
            plan.Log.Add($"serve pass: {cands.Count} candidate(s) with a lift entry, {plan.Runs.Count} run(s) kept (cap {cap}); " +
                $"{badChains} of {chains} traverse piece(s) fail the raw connector rules; {links} link(s) added between lifts; {spurs} spur(s) skied as the start of their run");
            return plan;
        }

        /// <summary>Where a run that merges at index end first comes within ContactM of the runs it joins. The joined run's samples
        /// near the approach count as crowded, so the run stops at first contact (no earlier than from). (The joined run must have a
        /// point there no more than 4 m above, for the skier to flow on).</summary>
        [MethodImpl(Hot)]
        private static int FirstContact(List<float2> way, float[] wh, int from, int end, HeightHash joined)
        {
            int e = end;
            while (e - 1 >= from && joined.NearBelow(way[e - 1], ContactM, float.MaxValue))
                e--;
            while (e < end && !joined.NearBelow(way[e], ContactM, wh[e] + 4f))
                e++;
            return e;
        }

        /// <summary>Points with heights in 60 m buckets.</summary>
        private sealed class HeightHash
        {
            private readonly Dictionary<long, List<float3>> m_Cells = new Dictionary<long, List<float3>>();
            private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
            public void Add(float3 p)
            {
                long k = Key((int)Math.Floor(p.x / 60f), (int)Math.Floor(p.z / 60f));
                if (!m_Cells.TryGetValue(k, out List<float3> l))
                    m_Cells[k] = l = new List<float3>();
                l.Add(p);
            }
            /// <summary>A point within r (up to 60 m) of p no higher than maxY.</summary>
            [MethodImpl(Hot)]
            public bool NearBelow(float2 p, float r, float maxY)
            {
                int cx = (int)Math.Floor(p.x / 60f), cz = (int)Math.Floor(p.y / 60f);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (m_Cells.TryGetValue(Key(cx + dx, cz + dz), out List<float3> l))
                            foreach (float3 q in l)
                                if (q.y <= maxY && math.distancesq(p, q.xz) < r * r)
                                    return true;
                return false;
            }
        }

        /// <summary>The middle 70 % of every lift chord, sampled every 10 m as the scorecard does. No run set may lie along more
        /// than ChordShare of it (the scorecard flags 30 % within 20 m, and a run under the lift line is fine, one along it is not).</summary>
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

            [MethodImpl(Hot)]
            public bool Fits(List<float2> pts, int from, int to)
            {
                for (int L = 0; L < mid.Count; L++)
                    if (!spent[L] && Over(L, pts, from, to))
                        return false;
                return true;
            }

            /// <summary>For a run added late, with all runs already in, only the lift lines it would itself bring onto (or further over)
            /// the share count. A line the serve pass already gave over to runs does not block a run elsewhere.</summary>
            [MethodImpl(Hot)]
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

            [MethodImpl(Hot)]
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

            [MethodImpl(Hot)]
            private static bool Near(List<float2> pts, int from, int to, float2 q)
            {
                for (int i = from; i <= to; i++)
                    if (math.distancesq(pts[i], q) < NearM * NearM)
                        return true;
                return false;
            }
        }

        /// <summary>A skier skis the last piece of traverse into a run (from the trunk's fork down to where the run starts) as the run's own
        /// first stretch. The skier leaves the cat track and skis on. Only the stretch clear of other runs joins. Two runs that leave one
        /// fork side by side stay on the cat track until they part. A stretch joins only when the joined run keeps the run rules and stays
        /// off the lift lines. The rest stays a traverse. Returns the spurs merged.</summary>
        [MethodImpl(Hot)]
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
                    if (O.Debug)
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
                    if (O.Debug)
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

        /// <summary>A run cut where it would climb (it ends at its low point before the climb) or before a pitch over the run maximum.
        /// The method checks the cut on the scorecard's 10 m sampling. Returns null when too little is left.</summary>
        [MethodImpl(Hot)]
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
        /// width. A line that delivers nowhere counts half (it can still end on another run).</summary>
        [MethodImpl(Hot)]
        private static float CandValue(Grid g, Cand cd, int end)
        {
            if (end <= cd.Entry + 3)
                return 0f;
            float drop = cd.Wh[cd.Entry] - cd.Wh[end];
            SlopeBand band = RunBand(g, cd.Way.GetRange(cd.Entry, end - cd.Entry + 1));
            return Math.Max(0f, drop) * Clamp(cd.L.Width / 40f, 0.3f, 1f) * BandWeight(band) * (cd.End >= 0 ? 1f : 0.5f);
        }

        /// <summary>The lift network as a skier rides it. While some lifts cannot get to the main set (or it to them), a traverse closes
        /// the gap. The traverse goes from a lift top whose reach takes in the other lift's bottom station. Examples: the ridge traverse
        /// to the bowl lift, the cat track from the upper lift to the base. The method then emits the traverse trees again.
        /// Returns the traverses added.</summary>
        [MethodImpl(Hot)]
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
        /// above the station). The method adds it to that lift's traverse tree. Returns false when no top reaches any.</summary>
        [MethodImpl(Hot)]
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
        [MethodImpl(Hot)]
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

        /// <summary>One lift's traverse tree: the union of its paths (entry first, top last). The method cuts the tree at every node (top,
        /// entry, fork) into connectors skied from the top down. It checks each piece on the raw terrain.</summary>
        [MethodImpl(Hot)]
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

        /// <summary>World points of a cell chain, smoothed (D8 zigzag) with the ends kept.
        /// With reversed, the chain goes from the last cell to the first.</summary>
        [MethodImpl(Hot)]
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
        [MethodImpl(Hot)]
        private static List<float3> Sampled(Grid g, List<float2> pl)
        {
            var pts = new List<float3>();
            foreach (float2 q in Densify(pl))
                pts.Add(new float3(q.x, RawH(g, q), q.y));
            return pts;
        }

        [MethodImpl(Hot)]
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
        [MethodImpl(Hot)]
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
        [MethodImpl(Hot)]
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
