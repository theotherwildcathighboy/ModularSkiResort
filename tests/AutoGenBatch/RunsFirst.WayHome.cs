// RunsFirst way home (round v3): every lift needs an easy way home, Whistler's "easiest way down": a green cat track cut with the
// grading tool that winds across whole faces from the lift's top to a base, crossing black runs as a bench where it must. The
// planner reads the plan the way scorecard v3 does (bases, bands, a gondola or tram top a skier may ride down from) and, for every
// lift whose top has no green or blue route home, searches uphill from where home is (the base lifts' bottoms, gondola and tram tops,
// and every green or blue run or track already leading home) over ground a green cat track can be graded across, until it reaches
// the lift's top. The routes share one search tree, so later tracks join earlier ones. Where no green track fits, a blue one (a
// diagonal run); where none fits at all, the lift is a download lift, and a download lift must be a gondola or tram.

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
        private static bool[] HomeElements(SkiGraph sg, List<Lift> lifts, int[] root, HashSet<int> bases)
        {
            int m = sg.P.Count;
            var home = new bool[m];
            for (bool changed = true; changed;)
            {
                changed = false;
                for (int i = 0; i < m; i++)
                    if (!home[i] && sg.Band[i] <= SlopeBand.Blue && (sg.AtBottom[i].Any(M => bases.Contains(root[M]))
                        || sg.Delivers[i].Any(M => Aerial(lifts[M].Type) && !sg.AtBottom[i].Contains(M)) || sg.Next[i].Any(j => home[j])))
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
                if (ok[L] || Aerial(lf.Type))
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

        /// <summary>Adds a way-home cat track from the top of every lift that has none; returns the tracks added.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static int AddWaysHome(Grid g, Plan plan, List<Lift> lifts)
        {
            int added = 0, blue = 0;
            var used = new HashSet<int>();   // cells of the tracks this pass emitted (a later track ends where it meets one)
            foreach (EasyLimits lim in new[] { WayGreen, WayBlue })
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
    }
}
