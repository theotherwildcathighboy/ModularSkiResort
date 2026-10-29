// RunsFirst way home (round v3): every lift needs an easy way home, Whistler's "easiest way down": a green cat track cut with the
// grading tool that winds across whole faces from the lift's top to a base, crossing black runs as a bench where it must. The
// planner reads the plan the way scorecard v3 does (bases, bands, a gondola or tram top a skier may ride down from) and, for every
// lift whose top has no green or blue route home, searches uphill from where home is (the base lifts' bottoms, gondola and tram tops,
// and every green or blue run or track already leading home) over ground a graded track can be cut across, until it reaches
// the lift's top. The routes share one search tree, so later tracks join earlier ones. The search takes pitches up to the blue
// line along the path, so the shortest easy route home is a blue diagonal run where it takes them (a run, not connector km: the
// green cat tracks it saves leave the connector budget to the regrade) and a green cat track where it stays under the blue line;
// a second search under the green limits takes the lifts the first could not serve (a blue route lying along a lift line). Where
// none fits at all, the lift is a download lift, and a download lift must be a gondola or tram.

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
        // a green cat track (the connector rules, the along-path grade under the blue line but for short pitches), and a blue one
        // (pitches along the path, so a track may zigzag down a face)
        private static readonly EasyLimits WayGreen = new EasyLimits(0.40f, GreenRawPitch, ConnPitchMax, 3.5f, 0.08f, 9000f, true);
        private static readonly EasyLimits WayBlue = new EasyLimits(0.45f, 0.37f, ConnPitchMax, 3.5f, 0.08f, 9000f, true);
        private const float WayStartM = 120f;   // a way home starts this close to the top (the scorecard's 150 m)

        /// <summary>Scorecard v3's bases: lift bottoms chained within 400 m; a base is such a cluster with two or more bottoms in the
        /// lowest 35 % of the region's relief, or the lowest lift's cluster when none has. Returns each lift's cluster root.</summary>
        private static int[] V3Bases(Grid g, List<Lift> lifts, out HashSet<int> bases)
        {
            var p = new int[lifts.Count];
            for (int i = 0; i < p.Length; i++)
                p[i] = i;
            for (int a = 0; a < lifts.Count; a++)
                for (int b = 0; b < a; b++)
                    if (math.distance(lifts[a].Bottom.xz, lifts[b].Bottom.xz) < 400f)
                        p[Root(p, a)] = Root(p, b);
            float hLo = float.MaxValue, hHi = float.MinValue;
            for (int c = 0; c < g.W * g.D; c++)
                if (g.In[c] && !float.IsNaN(g.Hr[c]))
                {
                    hLo = Math.Min(hLo, g.Hr[c]);
                    hHi = Math.Max(hHi, g.Hr[c]);
                }
            float lowCut = hLo + 0.35f * (hHi - hLo);
            var root = new int[lifts.Count];
            var size = new Dictionary<int, int>();
            for (int L = 0; L < lifts.Count; L++)
            {
                root[L] = Root(p, L);
                size[root[L]] = size.TryGetValue(root[L], out int n) ? n + 1 : 1;
            }
            bases = new HashSet<int>();
            for (int L = 0; L < lifts.Count; L++)
                if (RawH(g, lifts[L].Bottom.xz) <= lowCut && size[root[L]] >= 2)
                    bases.Add(root[L]);
            if (bases.Count == 0 && lifts.Count > 0)
                bases.Add(root[Enumerable.Range(0, lifts.Count).OrderBy(L => RawH(g, lifts[L].Bottom.xz)).First()]);
            return root;
        }

        /// <summary>Elements a skier holding to bands up to max takes from lift L's top (the scorecard's EasyFrom).</summary>
        private static HashSet<int> EasyFrom(SkiGraph sg, List<Lift> lifts, int L, SlopeBand max)
        {
            var seen = new HashSet<int>();
            var q = new Queue<int>();
            for (int i = 0; i < sg.P.Count; i++)
                if (sg.Band[i] <= max && math.distancesq(sg.P[i][0].xz, lifts[L].Top.xz) < 150f * 150f && seen.Add(i))
                    q.Enqueue(i);
            while (q.Count > 0)
                foreach (int j in sg.Next[q.Dequeue()])
                    if (sg.Band[j] <= max && seen.Add(j))
                        q.Enqueue(j);
            return seen;
        }

        /// <summary>Per element: it is green or blue and leads a skier who holds to green and blue home (to a base lift's bottom, or to
        /// a gondola or tram top to ride down).</summary>
        private static bool[] HomeElements(SkiGraph sg, List<Lift> lifts, int[] root, HashSet<int> bases, int notTop = -1)
        {
            int m = sg.P.Count;
            var home = new bool[m];
            for (bool changed = true; changed;)
            {
                changed = false;
                for (int i = 0; i < m; i++)
                    if (!home[i] && sg.Band[i] <= SlopeBand.Blue && (sg.AtBottom[i].Any(M => bases.Contains(root[M]))
                        || sg.Delivers[i].Any(M => M != notTop && Aerial(lifts[M].Type) && !sg.AtBottom[i].Contains(M)) || sg.Next[i].Any(j => home[j])))
                        home[i] = changed = true;
            }
            return home;
        }

        /// <summary>Beginner terrain as scorecard v3 reads it: from the lifts of a base a beginner rides lifts whose tops have skiing at
        /// their level (or a gondola / tram, both ways) and finds skiing there that delivers them to a lift again (a loop to lap).</summary>
        private static bool LearnOk(SkiGraph sg, List<Lift> lifts, IEnumerable<int> baseLifts, SlopeBand max)
        {
            var easy = new Dictionary<int, HashSet<int>>();
            HashSet<int> E(int L) => easy.TryGetValue(L, out HashSet<int> s) ? s : easy[L] = EasyFrom(sg, lifts, L, max);
            var seen = new HashSet<int>();
            var q = new Queue<int>();
            foreach (int L in baseLifts)
                if ((Aerial(lifts[L].Type) || E(L).Count > 0) && seen.Add(L))
                    q.Enqueue(L);
            bool loop = false;
            while (q.Count > 0)
                foreach (int i in E(q.Dequeue()))
                    foreach (int M in sg.Delivers[i])
                    {
                        loop = true;
                        if ((Aerial(lifts[M].Type) || E(M).Count > 0) && seen.Add(M))
                            q.Enqueue(M);
                    }
            return loop;
        }

        /// <summary>The lifts with an easy way home, as scorecard v3 reads the plan.</summary>
        private static bool[] WayHomeOk(SkiGraph sg, List<Lift> lifts, int[] root, HashSet<int> bases)
        {
            bool[] home = HomeElements(sg, lifts, root, bases);
            var ok = new bool[lifts.Count];
            for (int L = 0; L < lifts.Count; L++)
                ok[L] = Aerial(lifts[L].Type) || EasyFrom(sg, lifts, L, SlopeBand.Blue).Any(i => home[i]);
            return ok;
        }

        /// <summary>Scorecard v4's way home per lift: an easy (green or blue) route from its top home, with no shortcut for a gondola or
        /// tram (riding down from its own top is not an intermediate way down).</summary>
        private static bool[] EasyHomeV4(SkiGraph sg, List<Lift> lifts, int[] root, HashSet<int> bases)
        {
            bool[] home = HomeElements(sg, lifts, root, bases);
            var ok = new bool[lifts.Count];
            for (int L = 0; L < lifts.Count; L++)
                ok[L] = EasyFrom(sg, lifts, L, SlopeBand.Blue).Any(i => home[i]);
            return ok;
        }

        /// <summary>The highest lift top (raw height) an intermediate skis home from, as scorecard v4 reads the plan (-inf: none).</summary>
        private static float EasyHi(Grid g, SkiGraph sg, List<Lift> lifts, int[] root, HashSet<int> bases)
        {
            bool[] ok = EasyHomeV4(sg, lifts, root, bases);
            float hi = float.NegativeInfinity;
            for (int L = 0; L < lifts.Count; L++)
                if (ok[L])
                    hi = Math.Max(hi, RawH(g, lifts[L].Top.xz));
            return hi;
        }

        // round v3's rule (every lift needs an easy way home: way-home tracks from every top, download gondolas, stranded lifts taken
        // out); scorecard v4 reads experts-only lifts as normal, so it is off (RF_V3HOME=1 turns it back on)
        private static readonly bool V3Home = Environment.GetEnvironmentVariable("RF_V3HOME") == "1";

        /// <summary>A lift still without an easy way home is a download lift: its riders ride it down, so it is a gondola, or a tram
        /// where the grade needs one, when the norms fit with a margin and its chord crosses no other gondola or tram. Returns the
        /// lifts changed.</summary>
        private static int DownloadLifts(Grid g, Plan plan, List<Lift> lifts)
        {
            SkiGraph sg = Ski(g, plan, lifts);
            int[] root = V3Bases(g, lifts, out HashSet<int> bases);
            bool[] ok = WayHomeOk(sg, lifts, root, bases);
            int changed = 0;
            for (int L = 0; L < lifts.Count; L++)
            {
                Lift lf = lifts[L];
                // v4: an experts-only lift is normal; only a lift with no ski-out at all (nothing from its top comes back to a lift)
                // rides down
                if (ok[L] || Aerial(lf.Type) || !V3Home && sg.Skied[L].Any(i => sg.Returns[i]))
                    continue;
                float rise = RawH(g, lf.Top.xz) - RawH(g, lf.Bottom.xz), len = math.length(new float2(math.distance(lf.Bottom.xz, lf.Top.xz), rise));
                string type = DownloadType(g, lifts, L);
                if (type == null)
                {
                    plan.Log.Add($"L{lf.Id} has no way home and cannot be a download lift ({lf.Type} {len:F0} m / {rise:F0} m up)");
                    continue;
                }
                plan.Log.Add($"L{lf.Id} has no way home: a download lift, {lf.Type} -> {type} ({len:F0} m / {rise:F0} m up)");
                lf.Type = type;
                changed++;
            }
            return changed;
        }

        /// <summary>A lift that still has no way home and cannot be a download lift breaks the owner's first rule (every upper lift
        /// gets a green or blue way down), and a lift with no served run is a dead lift. Each is taken out with the skiing that then
        /// serves no one (runs and tracks no other lift's skiers reach, runs that came home only to its bottom), where the rest of the
        /// ski graph loses nothing: every other lift keeps its way home and its served runs, the links hold (the largest linked set
        /// loses at most the lift itself), the bases keep their share of green laps, and every element left keeps being reached and
        /// coming home. With chords set (after the chord repairs): a lift whose line still lies along runs (the scorecard's
        /// chord-on-run test) or crosses another lift of its kind, taken out only where every run it serves is another lift's run too. Returns the lifts taken out.</summary>
        private static int DropStrandedLifts(Grid g, Plan plan, List<Lift> lifts, List<Cand> cands, bool chords = false)
        {
            int dropped = 0;
            var tried = new HashSet<Lift>();
            while (!Late)
            {
                SkiGraph sg = Ski(g, plan, lifts);
                int[] root = V3Bases(g, lifts, out HashSet<int> bases);
                bool[] ok = WayHomeOk(sg, lifts, root, bases);
                var els = chords ? ChordEls(g, plan) : null;
                var share = new float[lifts.Count];
                int L = Enumerable.Range(0, lifts.Count).FirstOrDefault(i => !tried.Contains(lifts[i]) && (chords
                    ? (share[i] = ChordShareV3(lifts[i], els, out _)) > ChordShareMax || CrossesKind(lifts, lifts[i])
                    : (V3Home && !ok[i]) || !sg.Skied[i].Any(e => sg.IsRun[e] && sg.Returns[e])), -1);
                if (L < 0)
                    break;
                Lift lf = lifts[L];
                tried.Add(lf);
                string why = chords ? share[L] > ChordShareMax ? $"its line lies along runs, {100 * share[L]:F0} % of its middle" : "its line crosses another of its kind" : ok[L] ? "no served run" : "no way home and no download";
                SkiRead before = SkiRead.Of(g, plan, lifts);
                var served = new Dictionary<object, bool>();
                for (int i = 0; i < sg.P.Count; i++)
                    served[i < sg.Runs ? plan.Runs[i] : (object)plan.Connectors[i - sg.Runs]] = sg.Reached[i] && sg.Returns[i];
                var (runs, conns, links) = (plan.Runs.ToList(), plan.Connectors.ToList(), plan.Links.ToList());
                lifts.RemoveAt(L);
                int gone = 0, goneRuns = 0;
                for (int round = 0; round < 8; round++)
                {
                    SkiGraph s2 = Ski(g, plan, lifts);
                    var drop = new HashSet<object>();
                    for (int i = 0; i < s2.P.Count; i++)
                    {
                        object key = i < s2.Runs ? plan.Runs[i] : (object)plan.Connectors[i - s2.Runs];
                        if (served.TryGetValue(key, out bool was) && was && !(s2.Reached[i] && s2.Returns[i]))
                            drop.Add(key);
                    }
                    if (drop.Count == 0)
                        break;
                    gone += drop.Count;
                    goneRuns += plan.Runs.RemoveAll(r => drop.Contains(r));
                    plan.Connectors.RemoveAll(c => drop.Contains(c));
                    plan.Links.RemoveAll(c => drop.Contains(c));
                }
                SkiRead after = SkiRead.Of(g, plan, lifts);
                bool lost = chords && goneRuns > 0 || after.Net < before.Net - 1
                    || after.Green.Count(x => x) * before.Green.Length < before.Green.Count(x => x) * after.Green.Length;
                for (int M = 0, k = 0; M < before.Home.Length && !lost; M++)
                    if (M != L)
                    {
                        lost = before.Home[M] && !after.Home[k] || before.Live[M] && !after.Live[k];
                        k++;
                    }
                foreach (var kv in before.Els)
                    if (!lost && after.Els.TryGetValue(kv.Key, out var now) && (kv.Value.Reached && !now.Reached || kv.Value.Returns && !now.Returns))
                        lost = true;
                if (lost)
                {
                    lifts.Insert(L, lf);
                    plan.Runs.Clear();
                    plan.Connectors.Clear();
                    plan.Links.Clear();
                    plan.Runs.AddRange(runs);
                    plan.Connectors.AddRange(conns);
                    plan.Links.AddRange(links);
                    plan.Log.Add($"L{lf.Id} ({why}) stays: {(chords && goneRuns > 0 ? $"{goneRuns} run(s) only it serves would go" : "taking it out would cost the rest of the skiing")}");
                    continue;
                }
                dropped++;
                DropFromCands(cands, lifts, L);
                plan.Log.Add($"L{lf.Id} taken out ({why}; {lf.Type} {lf.Length:F0} m / {lf.Rise:F0} m up), with {gone} run(s) and track(s) only it served");
            }
            return dropped;
        }

        /// <summary>Lift L taken out of the list: the fill's candidate lines close their per-lift entries up past it, a line it entered
        /// takes its next highest entry (as the serve pass picks it), and a line no lift enters any more leaves the candidates.</summary>
        private static void DropFromCands(List<Cand> cands, List<Lift> lifts, int L)
        {
            foreach (Cand cd in cands)
            {
                if (cd.EntryOf != null && L < cd.EntryOf.Length)
                    cd.EntryOf = cd.EntryOf.Where((_, i) => i != L).ToArray();
                if (cd.By > L)
                    cd.By--;
                else if (cd.By == L)
                {
                    (cd.Entry, cd.By) = (-1, -1);
                    for (int M = 0; cd.EntryOf != null && M < cd.EntryOf.Length; M++)
                        if (cd.EntryOf[M] >= 0 && (cd.Entry < 0 || cd.EntryOf[M] < cd.Entry ||
                            (cd.EntryOf[M] == cd.Entry && math.distance(lifts[M].Top.xz, cd.Way[cd.Entry]) < math.distance(lifts[cd.By].Top.xz, cd.Way[cd.Entry]))))
                            (cd.Entry, cd.By) = (cd.EntryOf[M], M);
                }
            }
            cands.RemoveAll(cd => cd.By < 0);
        }

        /// <summary>The aerial type lift L may become (gondola, or tram where the grade needs one: the norms with a margin, a chord
        /// that crosses no other gondola or tram), or null.</summary>
        private static string DownloadType(Grid g, List<Lift> lifts, int L)
        {
            float2 b = lifts[L].Bottom.xz, t = lifts[L].Top.xz;
            float rise = RawH(g, t) - RawH(g, b), run = math.distance(b, t), len = math.sqrt(run * run + rise * rise);
            string type = len < 810f ? null : rise >= 100f && rise / run <= 1.15f ? "gondola" : rise >= 225f && rise / run <= 2.4f ? "tram" : null;
            if (type == null || Enumerable.Range(0, lifts.Count).Any(M => M != L && Aerial(lifts[M].Type) && SegCrossScore(lifts[M].Bottom.xz, lifts[M].Top.xz, b, t)))
                return null;
            return type;
        }

        // only the way down from the peak (RF_WAYPEAK=1): the lifts below it get no way-home track of their own
        private static readonly bool WayPeakOnly = Environment.GetEnvironmentVariable("RF_WAYPEAK") == "1";
        // the way down from the peak is sought again after the chord drop only where it took a lift out (RF_PEAKAGAIN=1: always)
        private static readonly bool PeakWayAgain = Environment.GetEnvironmentVariable("RF_PEAKAGAIN") == "1";

        /// <summary>The easy ways home: a track from every lift's top that has none (a gondola or tram top counts as home there, its
        /// riders ride down), then, for scorecard v4, the way down from the peak (a gondola or tram top needs one of its own).
        /// Returns the tracks added.</summary>
        private static int AddWaysHome(Grid g, Plan plan, List<Lift> lifts)
        {
            int added = WayPeakOnly ? 0 : AddLiftWaysHome(g, plan, lifts);
            if (!V3Home)
                added += AddPeakWayHome(g, plan, lifts);
            return added;
        }

        /// <summary>Adds a way-home cat track from the top of every lift that has none; returns the tracks added.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static int AddLiftWaysHome(Grid g, Plan plan, List<Lift> lifts)
        {
            int added = 0, blue = 0;
            var used = new HashSet<int>();   // cells of the tracks this pass emitted (a later track ends where it meets one)
            // blue first: visible 97.00 -> 97.48, 80 % outlines 94.80 -> 95.38, 120 % 95.30 -> 95.90 (RF_WAYGREEN1=1: green first)
            foreach (EasyLimits lim in Environment.GetEnvironmentVariable("RF_WAYGREEN1") == "1" ? new[] { WayGreen, WayBlue } : new[] { WayBlue, WayGreen })
            {
                SkiGraph sg = Ski(g, plan, lifts);
                int[] root = V3Bases(g, lifts, out HashSet<int> bases);
                bool[] ok = WayHomeOk(sg, lifts, root, bases);
                var missing = Enumerable.Range(0, lifts.Count).Where(L => !ok[L]).OrderByDescending(L => lifts[L].Top.y).ToList();
                if (missing.Count == 0)
                    break;
                // home: the base lifts' bottoms, gondola and tram tops (ride down), and the elements already leading home
                bool[] homeEl = HomeElements(sg, lifts, root, bases);
                var origins = new List<int>();
                for (int M = 0; M < lifts.Count; M++)
                {
                    if (bases.Contains(root[M]))
                        origins.Add(lifts[M].BottomCell);
                    if (Aerial(lifts[M].Type))
                        origins.Add(lifts[M].TopCell);
                }
                for (int i = 0; i < sg.P.Count; i++)
                    if (homeEl[i])
                        for (int k = 0; k < sg.P[i].Length; k += 3)
                        {
                            int c = CellOf(g, sg.P[i][k].xz);
                            if (c >= 0 && g.In[c] && !float.IsNaN(g.Hr[c]))
                                origins.Add(c);
                        }
                foreach (int c in used)
                    origins.Add(c);
                origins = origins.Distinct().ToList();
                var avoid = new PointHash();
                foreach (PlanRun r in plan.Runs)
                    foreach (float2 p in r.Pts)
                        avoid.Add(p);
                var lines = new PointHash();
                foreach (Lift lf in lifts)
                    for (float f = 0.15f; f <= 0.85f; f += 10f / math.max(10f, math.distance(lf.Bottom.xz, lf.Top.xz)))
                        lines.Add(math.lerp(lf.Bottom.xz, lf.Top.xz, f));
                EasyReach er = EasyUp(g, origins, avoid, lim, lines);
                // a blue track is a diagonal run: it may not lie along a lift line (the scorecard's chord-on-run test)
                var guard = new ChordGuard(lifts);
                for (int i = 0; i < sg.P.Count; i++)
                    if (sg.IsRun[i])
                        guard.Add(i < sg.Runs ? plan.Runs[i].Pts : plan.Connectors[i - sg.Runs], 0, (i < sg.Runs ? plan.Runs[i].Pts : plan.Connectors[i - sg.Runs]).Count - 1);
                foreach (int L in missing)
                {
                    float2 top = lifts[L].Top.xz;
                    float topH = RawH(g, top);
                    var starts = er.Cost.Where(kv => kv.Value > 0f && math.distancesq(g.World(kv.Key), top) < WayStartM * WayStartM && g.Hr[kv.Key] <= topH + 3f)
                        .OrderBy(kv => kv.Value).Take(8).Select(kv => kv.Key).ToList();
                    List<float2> track = null;
                    List<int> trackCells = null;
                    foreach (int s in starts)
                    {
                        List<int> cells = er.Route(s);
                        int cut = cells.FindIndex(c => used.Contains(c));
                        if (cut >= 0)
                            cells = cells.GetRange(0, cut + 1);
                        if (cells.Count < 3)
                            continue;
                        foreach (List<float2> pl in new[] { Polyline(g, cells, false), cells.ConvertAll(c => g.World(c)) })
                        {
                            if (!ConnectorOk(g, pl))
                                continue;
                            float along = AlongGrade(g, pl);
                            if (along < SkiSlopeClassifier.GradeBlue - 0.01f || (lim.SteepPitch > GreenRawPitch && along < SkiSlopeClassifier.GradeBlack - 0.01f
                                && guard.FitsAdding(Densify(pl), 0, Densify(pl).Count - 1)))
                            {
                                track = pl;
                                break;
                            }
                        }
                        if (track != null)
                        {
                            trackCells = cells;
                            break;
                        }
                    }
                    if (track == null)
                        continue;
                    var (run, band) = TrackClass(g, track);
                    foreach (int c in trackCells)
                        used.Add(c);
                    if (run)
                        guard.Add(Densify(track), 0, Densify(track).Count - 1);
                    plan.Connectors.Add(track);
                    plan.Links.Add(track);
                    added++;
                    if (run)
                        blue++;
                    plan.Log.Add($"way home from L{lifts[L].Id}'s top: {(run ? $"{band} diagonal" : "green cat track")} {RunLength(track):F0} m, {RawH(g, track[0]) - RawH(g, track[track.Count - 1]):F0} m down");
                }
            }
            SkiGraph after = Ski(g, plan, lifts);
            int[] rootA = V3Bases(g, lifts, out HashSet<int> basesA);
            bool[] okA = WayHomeOk(after, lifts, rootA, basesA);
            plan.Log.Add($"way home: {added} track(s) added ({blue} blue), {okA.Count(x => x)} of {lifts.Count} lift(s) with a way home" +
                (okA.All(x => x) ? "" : $" (none from {string.Join(", ", Enumerable.Range(0, lifts.Count).Where(L => !okA[L]).Select(L => "L" + lifts[L].Id))})"));
            return added;
        }

        /// <summary>Scorecard v4: a resort strives for an intermediate way down from at least one of its peaks. The highest lift top
        /// without an easy way home gets one: the uphill search from home (base lift bottoms, other gondola and tram tops to ride down,
        /// the easy skiing already leading home) takes the shortest route under the blue limits first (a blue diagonal where it takes
        /// pitches past the blue line), then the green ones. Where none fits, the next highest top; one is enough, and the lifts below
        /// it may stay experts-only. Returns the tracks added.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static int AddPeakWayHome(Grid g, Plan plan, List<Lift> lifts)
        {
            var tried = new HashSet<Lift>();
            int added = 0;
            for (int round = 0; round < 6 && !Late; round++)
            {
                SkiGraph sg = Ski(g, plan, lifts);
                int[] root = V3Bases(g, lifts, out HashSet<int> bases);
                bool[] ok = EasyHomeV4(sg, lifts, root, bases);
                float easyHi = float.NegativeInfinity;
                for (int L = 0; L < lifts.Count; L++)
                    if (ok[L])
                        easyHi = Math.Max(easyHi, RawH(g, lifts[L].Top.xz));
                int T = Enumerable.Range(0, lifts.Count).Where(L => !ok[L] && !tried.Contains(lifts[L]) && RawH(g, lifts[L].Top.xz) > easyHi + 1f)
                    .OrderByDescending(L => RawH(g, lifts[L].Top.xz)).FirstOrDefault(-1);
                if (T < 0)
                    break;
                tried.Add(lifts[T]);
                // home, less the lift's own top: riding down from where the skier stands is not a way down
                bool[] homeEl = HomeElements(sg, lifts, root, bases, T);
                var origins = new List<int>();
                for (int M = 0; M < lifts.Count; M++)
                {
                    if (bases.Contains(root[M]))
                        origins.Add(lifts[M].BottomCell);
                    if (M != T && Aerial(lifts[M].Type))
                        origins.Add(lifts[M].TopCell);
                }
                for (int i = 0; i < sg.P.Count; i++)
                    if (homeEl[i])
                        for (int k = 0; k < sg.P[i].Length; k += 3)
                        {
                            int c = CellOf(g, sg.P[i][k].xz);
                            if (c >= 0 && g.In[c] && !float.IsNaN(g.Hr[c]))
                                origins.Add(c);
                        }
                origins = origins.Distinct().ToList();
                var avoid = new PointHash();
                foreach (PlanRun r in plan.Runs)
                    foreach (float2 p in r.Pts)
                        avoid.Add(p);
                var lines = new PointHash();
                foreach (Lift lf in lifts)
                    for (float f = 0.15f; f <= 0.85f; f += 10f / math.max(10f, math.distance(lf.Bottom.xz, lf.Top.xz)))
                        lines.Add(math.lerp(lf.Bottom.xz, lf.Top.xz, f));
                var guard = new ChordGuard(lifts);
                for (int i = 0; i < sg.P.Count; i++)
                    if (sg.IsRun[i])
                        guard.Add(i < sg.Runs ? plan.Runs[i].Pts : plan.Connectors[i - sg.Runs], 0, (i < sg.Runs ? plan.Runs[i].Pts : plan.Connectors[i - sg.Runs]).Count - 1);
                float2 top = lifts[T].Top.xz;
                float topH = RawH(g, top);
                List<float2> track = null;
                foreach (EasyLimits lim in new[] { WayBlue, WayGreen })
                {
                    EasyReach er = EasyUp(g, origins, avoid, lim, lines);
                    var starts = er.Cost.Where(kv => kv.Value > 0f && math.distancesq(g.World(kv.Key), top) < WayStartM * WayStartM && g.Hr[kv.Key] <= topH + 3f)
                        .OrderBy(kv => kv.Value).Take(8).Select(kv => kv.Key).ToList();
                    foreach (int s0 in starts)
                    {
                        List<int> cells = er.Route(s0);
                        if (cells.Count < 3)
                            continue;
                        foreach (List<float2> pl in new[] { Polyline(g, cells, false), cells.ConvertAll(c => g.World(c)) })
                        {
                            if (!ConnectorOk(g, pl))
                                continue;
                            float along = AlongGrade(g, pl);
                            if (along < SkiSlopeClassifier.GradeBlue - 0.01f || (lim.SteepPitch > GreenRawPitch && along < SkiSlopeClassifier.GradeBlack - 0.01f
                                && guard.FitsAdding(Densify(pl), 0, Densify(pl).Count - 1)))
                            {
                                track = pl;
                                break;
                            }
                        }
                        if (track != null)
                            break;
                    }
                    if (track != null)
                        break;
                }
                if (track == null)
                {
                    plan.Log.Add($"way down from the peak: none fits from L{lifts[T].Id}'s top ({topH:F0} m)");
                    continue;
                }
                var (run, band) = TrackClass(g, track);
                plan.Connectors.Add(track);
                plan.Links.Add(track);
                added++;
                plan.Log.Add($"way down from the peak, L{lifts[T].Id}'s top ({topH:F0} m): {(run ? $"{band} diagonal" : "green cat track")} {RunLength(track):F0} m, " +
                    $"{RawH(g, track[0]) - RawH(g, track[track.Count - 1]):F0} m down");
            }
            SkiGraph after = Ski(g, plan, lifts);
            int[] rootA = V3Bases(g, lifts, out HashSet<int> basesA);
            float hiA = EasyHi(g, after, lifts, rootA, basesA), topA = lifts.Count > 0 ? lifts.Max(lf => RawH(g, lf.Top.xz)) : 0f;
            plan.Log.Add($"way down from the peak: {added} track(s) added; easy from {(float.IsNegativeInfinity(hiA) ? "no top" : $"{hiA:F0} m")} of the highest top's {topA:F0} m");
            return added;
        }
    }
}
