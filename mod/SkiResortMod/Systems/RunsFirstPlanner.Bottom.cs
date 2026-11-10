// RunsFirstPlanner way to the bottom (port round R2-c, plan sections 5.1 to 5.4): from the top of every lift with skiing, a trail of
// any rating or a cat track goes all the way to the bottom of the region. The bottom is the base band: the lowest 15 % of the region's
// relief, 10 to 60 m (the realizer's band). A skier reaches it on runs and cat tracks alone, with no lift ride (a download lift does
// not count). The pass runs after the ways home:
// 1. On the ski graph it marks the lifts whose skiing ends in the band.
// 2. For the others, lowest base first, it searches uphill (blue limits, then green) from the bottom landing and from every element
//    that already reaches the band. A hit is a green cat track or a blue diagonal, as the way home cuts them.
// 3. With no hit: one fall-line run of any rating from the low end of the lift's skiing down its drainage to the band, under the
//    run rules (SanitizeRun). Where the drainage is blocked, a run of any rating searched uphill under the run rules (step 3b).
// 4. With still none, the lift stays and the result names what is in the way (RunsFirstBottom: cliff, basin, water, hole, edge).
// The bottom landing is the first class with cells: lift bottoms in the band; flat band ground in reach of a road or sign
// (RunsFirstInputs.Access); other flat band ground; the band itself. The pass spends a budget of its own (RunsFirstOptions.BottomBudget).
// Pure C# 9 on System and Unity.Mathematics, net48-clean. See EngineNotes/RunsFirstPlanner.md (Way to the bottom).

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class RunsFirstPlanner
    {
        private const float BottomShare = 0.15f, BottomMinM = 10f, BottomMaxM = 60f;   // the base band (the realizer's constants)
        private const float BottomFlatGrade = 0.08f;     // flat landing ground (raw 10 m grade)
        private const float BottomAccessM = 150f;        // a road or sign this close puts flat ground in reach
        private const float BottomStartM = 30f;          // a trail starts this close to the lift's skiing (the skier's 40 m, less a margin)
        private const float BottomRunMinM = 60f;         // a fall-line run goes on into the band until it is this long
        private const float BottomProbeM = 3000f;        // past a gap in the mask, the fall line looks this far for region below
        // a run of any rating searched uphill from the bottom (step 3b): pitches up to the run rules, no along-path grade
        private static readonly EasyLimits WayRun = new EasyLimits(0.90f, 0.90f, RunPitchMax, 3.5f, 1f, 9000f);

        /// <summary>The top of the base band (raw heights of the open region cells).</summary>
        private static float BottomBandTop(Grid g)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            for (int c = 0; c < g.W * g.D; c++)
                if (g.In[c] && !float.IsNaN(g.Hr[c]))
                {
                    lo = Math.Min(lo, g.Hr[c]);
                    hi = Math.Max(hi, g.Hr[c]);
                }
            if (lo > hi)
                return float.NaN;
            return lo + Math.Max(BottomMinM, Math.Min(BottomMaxM, BottomShare * (hi - lo)));
        }

        /// <summary>Per element: it ends in the band, or a skier on it gets to one that does.</summary>
        private static bool[] BottomElements(SkiGraph sg, float bandTop)
        {
            int m = sg.P.Count;
            var at = new bool[m];
            for (int i = 0; i < m; i++)
                at[i] = sg.P[i][sg.P[i].Length - 1].y <= bandTop;
            for (bool changed = true; changed;)
            {
                changed = false;
                for (int i = 0; i < m; i++)
                    if (!at[i] && sg.Next[i].Any(j => at[j]))
                        at[i] = changed = true;
            }
            return at;
        }

        /// <summary>The low end of a lift's skiing: the lowest end of an element skied from its top.</summary>
        private static float3 LowEnd(SkiGraph sg, int L)
        {
            float3 low = new float3(0f, float.MaxValue, 0f);
            foreach (int i in sg.Skied[L].OrderBy(i => i))
            {
                float3 e = sg.P[i][sg.P[i].Length - 1];
                if (e.y < low.y)
                    low = e;
            }
            return low;
        }

        /// <summary>The way to the bottom of every lift on the plan as it stands (the result field).
        /// atLift: the lifts whose skiing gets to a lift bottom in the band (the landing B-1 of the test host sees: its pads).</summary>
        private RunsFirstBottom[] BottomStatus(Grid g, PlanDraft plan, List<Lift> lifts, int[] rec, float bandTop, out int atLift)
        {
            var st = new RunsFirstBottom[lifts.Count];
            atLift = 0;
            if (lifts.Count == 0 || float.IsNaN(bandTop))
                return st;
            SkiGraph sg = Ski(g, plan, lifts);
            bool[] at = BottomElements(sg, bandTop);
            var low = new bool[sg.P.Count];
            for (int i = 0; i < low.Length; i++)
                low[i] = sg.AtBottom[i].Any(M => RawH(g, lifts[M].Bottom.xz) <= bandTop);
            for (bool changed = true; changed;)
            {
                changed = false;
                for (int i = 0; i < low.Length; i++)
                    if (!low[i] && sg.Next[i].Any(j => low[j]))
                        low[i] = changed = true;
            }
            for (int L = 0; L < lifts.Count; L++)
                if (sg.Skied[L].Any(i => low[i]))
                    atLift++;
            for (int L = 0; L < lifts.Count; L++)
            {
                st[L] = sg.Skied[L].Count == 0 ? RunsFirstBottom.NoSkiing
                    : sg.Skied[L].Any(i => at[i]) ? RunsFirstBottom.Reaches
                    : FallLine(g, rec, LowEnd(sg, L).xz, bandTop, out _);
                if (st[L] == RunsFirstBottom.Reaches && !sg.Skied[L].Any(i => at[i]))
                    st[L] = RunsFirstBottom.Other;   // a drainage that holds, but no trail on it (the pass was off or ran out)
            }
            return st;
        }

        /// <summary>The fall line from a point down its drainage to the band. Reaches: run holds a run under the run rules that ends in the
        /// band. Otherwise the reason (run null).</summary>
        private RunsFirstBottom FallLine(Grid g, int[] rec, float2 from, float bandTop, out List<float2> run)
        {
            run = null;
            int c = CellOf(g, from);
            m_FallNote = $"from {RawH(g, from):F0} m";
            if (c < 0)
                return RunsFirstBottom.Edge;
            if (!g.In[c])
            {
                m_FallNote += ", off the mask";
                return RunsFirstBottom.Other;
            }
            var pts = new List<float2> { from };
            float len = 0f;
            for (int steps = 0; steps < g.W + g.D + 4000; steps++)
            {
                if (g.Hr[c] <= bandTop && len >= BottomRunMinM)
                    break;
                int nx = rec[c];
                if (nx < 0)
                    break;
                len += math.distance(g.World(c), g.World(nx));
                pts.Add(g.World(nx));
                c = nx;
            }
            m_FallNote += $", drainage {len:F0} m to {g.Hr[c]:F0} m";
            if (g.Hr[c] <= bandTop)
            {
                List<float2> ok = SanitizeRun(g, pts);
                if (ok != null && RawH(g, ok[ok.Count - 1]) <= bandTop)
                {
                    run = ok;
                    return RunsFirstBottom.Reaches;
                }
                return RunRuleBreak(g, pts) ?? RunsFirstBottom.Other;
            }
            return RunRuleBreak(g, pts) ?? MaskExit(g, c);
        }

        private string m_FallNote;   // the last fall line, for the plan log

        /// <summary>Where a path breaks the run rules (SanitizeRun's tests): a pitch is a cliff, a climb is a basin.
        /// Null: it keeps them.</summary>
        private static RunsFirstBottom? RunRuleBreak(Grid g, List<float2> path)
        {
            if (path.Count < 2)
                return null;
            List<float2> run = Densify(path);
            var h = run.ConvertAll(p => RawH(g, p));
            int end = run.Count - 1, lowAt = 0;
            bool climb = false;
            for (int i = 1; i < run.Count; i++)
            {
                if (float.IsNaN(h[i]) || h[i] > h[lowAt] + RunClimbMax)
                {
                    end = lowAt;
                    climb = true;
                    break;
                }
                if (h[i] < h[lowAt])
                    lowAt = i;
            }
            for (int i = 0; i + 3 <= end; i++)
                if ((h[i] - h[i + 3]) / math.max(1f, math.distance(run[i], run[i + 3])) > RunPitchMax)
                    return RunsFirstBottom.Cliff;
            return climb ? RunsFirstBottom.Basin : (RunsFirstBottom?)null;
        }

        /// <summary>A cell any of whose texels is wet.</summary>
        private static bool WetCell(Grid g, int c)
        {
            HeightSnapshot s = g.S;
            if (s.Wet == null)
                return false;
            int cx = c % g.W, cz = c / g.W;
            for (int dz = 0; dz < F; dz++)
                for (int dx = 0; dx < F; dx++)
                    if (s.Wet[(cz * F + dz) * s.Width + cx * F + dx] != 0)
                        return true;
            return false;
        }

        /// <summary>What stops the drainage at a mask edge cell: the lowest neighbor out of the mask is water, a hole (the fall line
        /// finds region below it) or the edge of the region.</summary>
        private static RunsFirstBottom MaskExit(Grid g, int c)
        {
            int Lowest(int at, bool outOnly, out bool off)
            {
                off = false;
                int x = at % g.W, z = at / g.W, best = -1;
                float bh = outOnly ? float.MaxValue : g.H[at];
                for (int k = 0; k < 8; k++)
                {
                    int xx = x + DX[k], zz = z + DZ[k];
                    if (xx < 0 || zz < 0 || xx >= g.W || zz >= g.D)
                    {
                        off = true;
                        continue;
                    }
                    int nb = zz * g.W + xx;
                    if (outOnly && g.In[nb])
                        continue;
                    if (g.H[nb] < bh)
                    {
                        bh = g.H[nb];
                        best = nb;
                    }
                }
                return best;
            }
            int p = Lowest(c, true, out bool edge);
            if (p < 0)
                return RunsFirstBottom.Edge;
            if (WetCell(g, p))
                return RunsFirstBottom.Water;
            int steps = (int)(BottomProbeM / g.CellM);
            for (int k = 0; k < steps; k++)
            {
                if (g.In[p])
                    return RunsFirstBottom.Hole;
                int nx = Lowest(p, false, out bool off);
                if (nx < 0)
                {
                    // a low point out of the mask: a hole when region lies close around it, else the edge
                    if (off)
                        return RunsFirstBottom.Edge;
                    int x = p % g.W, z = p / g.W, r = (int)(60f / g.CellM) + 1;
                    for (int dz = -r; dz <= r; dz++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            int xx = x + dx, zz = z + dz;
                            if (xx >= 0 && zz >= 0 && xx < g.W && zz < g.D && g.In[zz * g.W + xx])
                                return RunsFirstBottom.Hole;
                        }
                    return RunsFirstBottom.Edge;
                }
                p = nx;
            }
            return RunsFirstBottom.Edge;
        }

        /// <summary>The bottom landing (plan 5.1): the cells of the first class with any. Returns the class (1 to 4, 0: none).</summary>
        private int BottomLanding(Grid g, List<Lift> lifts, float bandTop, List<int> cells)
        {
            cells.Clear();
            foreach (Lift lf in lifts)
                if (lf.BottomCell >= 0 && g.In[lf.BottomCell] && RawH(g, lf.Bottom.xz) <= bandTop)
                    cells.Add(lf.BottomCell);
            if (cells.Count > 0)
            {
                List<int> one = cells.Distinct().OrderBy(c => c).ToList();
                cells.Clear();
                cells.AddRange(one);
                return 1;
            }
            var band = new List<int>();
            var flat = new List<int>();
            for (int c = 0; c < g.W * g.D; c++)
                if (g.In[c] && !float.IsNaN(g.Hr[c]) && g.Hr[c] <= bandTop)
                {
                    band.Add(c);
                    if (RawGrade(g, g.World(c)) < BottomFlatGrade)
                        flat.Add(c);
                }
            float2[] access = Inputs?.Access;
            if (access != null && access.Length > 0 && flat.Count > 0)
            {
                var near = new Dictionary<long, List<float2>>();
                long Key(float2 p) => ((long)Math.Floor(p.x / BottomAccessM) << 32) ^ (uint)(int)Math.Floor(p.y / BottomAccessM);
                foreach (float2 a in access)
                {
                    long k = Key(a);
                    if (!near.TryGetValue(k, out List<float2> l))
                        near[k] = l = new List<float2>();
                    l.Add(a);
                }
                foreach (int c in flat)
                {
                    float2 p = g.World(c);
                    int kx = (int)Math.Floor(p.x / BottomAccessM), kz = (int)Math.Floor(p.y / BottomAccessM);
                    bool inReach = false;
                    for (int dz = -1; dz <= 1 && !inReach; dz++)
                        for (int dx = -1; dx <= 1 && !inReach; dx++)
                            if (near.TryGetValue(((long)(kx + dx) << 32) ^ (uint)(kz + dz), out List<float2> l))
                                inReach = l.Exists(a => math.distancesq(a, p) < BottomAccessM * BottomAccessM);
                    if (inReach)
                        cells.Add(c);
                }
                if (cells.Count > 0)
                    return 2;
            }
            if (flat.Count > 0)
            {
                cells.AddRange(flat);
                return 3;
            }
            cells.AddRange(band);
            return band.Count > 0 ? 4 : 0;
        }

        /// <summary>The pass on its own, for the host's hand fixtures: the grid and drainage of the region (with Inputs), the given lifts
        /// (bottom, top) and their runs (each served by the lift of the same index, or by lift 0), then the pass and the final status.
        /// Returns the plain plan (the lifts' ToBottom, the trails, the log).</summary>
        internal RunsFirstResult BottomProbe(HeightSnapshot snap, float2[] ring, List<(float2 Bottom, float2 Top)> lifts, List<List<float2>> runs,
            RunsFirstOptions options = null)
        {
            O = options ?? new RunsFirstOptions();
            Clock = System.Diagnostics.Stopwatch.StartNew();
            EasyWork = LinkWork = 0;
            Grid g = BuildGrid(snap, ring);
            g.Rater = new BandRater { Bits = O.RaterBands };
            int[] rec = Receivers(g, g.H, out _);
            var ls = new List<Lift>();
            foreach (var (b, t) in lifts)
                ls.Add(new Lift
                {
                    Id = ls.Count + 1, Type = "detachable", Bottom = new float3(b.x, RawH(g, b), b.y), Top = new float3(t.x, RawH(g, t), t.y),
                    BottomCell = CellOf(g, b), TopCell = CellOf(g, t),
                });
            var plan = new PlanDraft();
            for (int i = 0; i < runs.Count; i++)
                plan.Runs.Add(new PlanRun { Pts = runs[i], Band = RunBand(g, runs[i]), By = ls[Math.Min(i, ls.Count - 1)] });
            if (O.WayToBottom)
            {
                if (O.BottomBudget > 0f)
                    m_OwnBudget = O.BottomBudget;
                AddWayToBottom(g, plan, ls, rec);
                m_OwnBudget = 0f;
            }
            RunsFirstBottom[] st = BottomStatus(g, plan, ls, rec, BottomBandTop(g), out _);
            return MakeResult(g, plan, ls, st);
        }

        /// <summary>The way-to-the-bottom pass. Returns the trails added.</summary>
        private int AddWayToBottom(Grid g, PlanDraft plan, List<Lift> lifts, int[] rec)
        {
            float bandTop = BottomBandTop(g);
            if (lifts.Count == 0 || float.IsNaN(bandTop))
                return 0;
            var landing = new List<int>();
            int cls = BottomLanding(g, lifts, bandTop, landing);
            int tracks = 0, blue = 0, runs = 0, searched = 0;
            float trackKm = 0f, runKm = 0f;
            var used = new HashSet<int>();
            List<int> Missing(SkiGraph sg, bool[] at) => Enumerable.Range(0, lifts.Count).Where(L => sg.Skied[L].Count > 0 && !sg.Skied[L].Any(i => at[i]))
                .OrderBy(L => RawH(g, lifts[L].Bottom.xz)).ThenBy(L => L).ToList();
            SkiGraph sg0 = Ski(g, plan, lifts);
            int skiing = Enumerable.Range(0, lifts.Count).Count(L => sg0.Skied[L].Count > 0);
            int before = skiing - Missing(sg0, BottomElements(sg0, bandTop)).Count;

            // one search round: uphill from the landing, the elements already reaching the band and the trails this pass cut, to the
            // skiing of each lift still missing. asRun: a plain run under the run rules (any rating), else a graded track
            void Round(EasyLimits lim, bool asRun)
            {
                SkiGraph sg = Ski(g, plan, lifts);
                bool[] at = BottomElements(sg, bandTop);
                List<int> missing = Missing(sg, at);
                if (missing.Count == 0 || landing.Count == 0)
                    return;
                var origins = new List<int>(landing);
                for (int i = 0; i < sg.P.Count; i++)
                    if (at[i])
                        for (int k = 0; k < sg.P[i].Length; k += 3)
                        {
                            int c = CellOf(g, sg.P[i][k].xz);
                            if (c >= 0 && g.In[c] && !float.IsNaN(g.Hr[c]))
                                origins.Add(c);
                        }
                origins.AddRange(used.OrderBy(c => c));
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
                searched++;
                var guard = new ChordGuard(lifts);
                for (int i = 0; i < sg.P.Count; i++)
                    if (sg.IsRun[i])
                    {
                        List<float2> pts = i < sg.Runs ? plan.Runs[i].Pts : plan.Connectors[i - sg.Runs];
                        guard.Add(pts, 0, pts.Count - 1);
                    }
                foreach (int L in missing)
                {
                    // a start the skier gets to: within BottomStartM of a point of the lift's skiing, and no higher than it (the ski
                    // graph's branch rule, 40 m and 5 m)
                    var near = new Dictionary<long, List<float3>>();
                    foreach (int i in sg.Skied[L])
                        foreach (float3 p in sg.P[i])
                        {
                            long k = ((long)Math.Floor(p.x / 40f) << 32) ^ (uint)(int)Math.Floor(p.z / 40f);
                            if (!near.TryGetValue(k, out List<float3> l))
                                near[k] = l = new List<float3>();
                            l.Add(p);
                        }
                    bool Joins(int c)
                    {
                        float2 w = g.World(c);
                        int kx = (int)Math.Floor(w.x / 40f), kz = (int)Math.Floor(w.y / 40f);
                        for (int dz = -1; dz <= 1; dz++)
                            for (int dx = -1; dx <= 1; dx++)
                                if (near.TryGetValue(((long)(kx + dx) << 32) ^ (uint)(kz + dz), out List<float3> l))
                                    foreach (float3 p in l)
                                        if (math.distancesq(p.xz, w) < BottomStartM * BottomStartM && g.Hr[c] <= p.y + 4f)
                                            return true;
                        return false;
                    }
                    var starts = er.CostInOrder().Where(kv => kv.Value > 0f && Joins(kv.Key)).OrderBy(kv => kv.Value).Take(8).Select(kv => kv.Key).ToList();
                    List<float2> trail = null;
                    List<int> trailCells = null;
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
                            if (asRun)
                            {
                                List<float2> ok = SanitizeRun(g, pl);
                                if (ok != null && ok.Count == Densify(pl).Count)
                                {
                                    trail = ok;
                                    break;
                                }
                                continue;
                            }
                            if (!ConnectorOk(g, pl))
                                continue;
                            float along = AlongGrade(g, pl);
                            if (along < SkiSlopeClassifier.GradeBlue - 0.01f || (lim.SteepPitch > GreenRawPitch && along < SkiSlopeClassifier.GradeBlack - 0.01f
                                && guard.FitsAdding(Densify(pl), 0, Densify(pl).Count - 1)))
                            {
                                trail = pl;
                                break;
                            }
                        }
                        if (trail != null)
                        {
                            trailCells = cells;
                            break;
                        }
                    }
                    if (trail == null)
                        continue;
                    foreach (int c in trailCells)
                        used.Add(c);
                    float km = RunLength(trail) / 1000f, down = RawH(g, trail[0]) - RawH(g, trail[trail.Count - 1]);
                    if (asRun)
                    {
                        SlopeBand rb = RaterBand(g, trail);
                        plan.Runs.Add(new PlanRun { Pts = trail, Band = rb, By = lifts[L], Kind = "bottom" });
                        runs++;
                        runKm += km;
                        plan.Log.Add($"way to the bottom from L{lifts[L].Id}: {rb} searched run {1000f * km:F0} m, {down:F0} m down");
                        continue;
                    }
                    var (isRun, band) = TrackClass(g, trail);
                    if (isRun)
                        guard.Add(Densify(trail), 0, Densify(trail).Count - 1);
                    plan.Connectors.Add(trail);
                    plan.Links.Add(trail);
                    tracks++;
                    if (isRun)
                        blue++;
                    trackKm += km;
                    plan.Log.Add($"way to the bottom from L{lifts[L].Id}: {(isRun ? $"{band} diagonal" : "green cat track")} {1000f * km:F0} m, {down:F0} m down");
                }
            }

            // 2. graded tracks: blue limits, then green
            Round(WayBlue, false);
            Round(WayGreen, false);
            // 3. one fall-line run of any rating down the drainage, lowest base first
            var why = new Dictionary<int, RunsFirstBottom>();
            var notes = new Dictionary<int, string>();
            var ran = new HashSet<int>();
            for (bool again = true; again;)
            {
                again = false;
                SkiGraph sg = Ski(g, plan, lifts);
                foreach (int L in Missing(sg, BottomElements(sg, bandTop)).Where(L => !why.ContainsKey(L) && !ran.Contains(L)))
                {
                    RunsFirstBottom r = FallLine(g, rec, LowEnd(sg, L).xz, bandTop, out List<float2> run);
                    if (r != RunsFirstBottom.Reaches)
                    {
                        why[L] = r;
                        notes[L] = m_FallNote;
                        continue;
                    }
                    ran.Add(L);
                    SlopeBand band = RaterBand(g, run);
                    plan.Runs.Add(new PlanRun { Pts = run, Band = band, By = lifts[L], Kind = "bottom" });
                    runs++;
                    runKm += RunLength(run) / 1000f;
                    plan.Log.Add($"way to the bottom from L{lifts[L].Id}: {band} fall-line run {RunLength(run):F0} m, {RawH(g, run[0]) - RawH(g, run[run.Count - 1]):F0} m down");
                    again = true;   // the run may carry other lifts' skiers too: read the graph again
                    break;
                }
            }
            // 3b. where the drainage is blocked, a run of any rating searched across the slope (the run rules, not the track limits)
            Round(WayRun, true);
            // 4. what is left stays, with the fall line's block
            SkiGraph after = Ski(g, plan, lifts);
            List<int> left = Missing(after, BottomElements(after, bandTop));
            foreach (int L in left)
                plan.Log.Add($"no way to the bottom from L{lifts[L].Id}: {(why.TryGetValue(L, out RunsFirstBottom w) ? w : RunsFirstBottom.Other)} ({(notes.TryGetValue(L, out string n) ? n : "")})");
            plan.Log.Add($"way to the bottom: band top {bandTop:F0} m, landing class {cls} ({landing.Count} cells), {skiing} lift(s) with skiing, " +
                $"{before} reached the bottom before, {skiing - left.Count} after; {tracks} track(s) ({blue} blue, {trackKm:F2} km), {runs} run(s) ({runKm:F2} km), {searched} search(es), {EasyWork} cells settled");
            return tracks + runs;
        }
    }
}
