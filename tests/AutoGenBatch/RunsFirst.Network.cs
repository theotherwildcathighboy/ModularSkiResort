// RunsFirst lift network (prototype): a skier should be able to get from every lift to every other one. Where the ski graph shows a
// gap (a lift whose station no run passes, an area whose runs never lead back to the main lifts), the planner searches uphill from
// the stations the skier must reach over ground a cat track can follow (the connector rules), then over ground a blue run can,
// until it meets a run or traverse the skier is already on; that route becomes the missing link: a cat track between spread-out
// base stations, a run down from one area into the next.

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
        private const float LinkOnM = 30f;   // a link starts on an element the skier is on: within this distance, at most 4 m above it

        /// <summary>A bottom station no served run passes (set on a bench above where the runs collect) moves onto the nearest run-out
        /// within 350 m, no higher than it was, that the lift still fits from: a flat pad, the norms and the rope clearance, no
        /// crossing, the chord off the runs. Traverses and cat tracks that led to the old station go. Returns the stations moved.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static int ReseatBottoms(Grid g, Plan plan, List<Lift> lifts, List<List<int>>[] need)
        {
            int moved = 0;
            SkiGraph sg = Ski(g, plan, lifts);
            if (sg.BestSize >= lifts.Count)
                return 0;
            for (int Y = 0; Y < lifts.Count; Y++)
            {
                if (Enumerable.Range(0, sg.Runs).Any(i => sg.Reached[i] && sg.Returns[i] && sg.Delivers[i].Contains(Y)))
                    continue;
                float2 b0 = lifts[Y].Bottom.xz, t = lifts[Y].Top.xz;
                float h0 = RawH(g, b0);
                var cands = new List<(int C, float D)>();
                for (int i = 0; i < sg.Runs; i++)
                {
                    if (!sg.Reached[i] || !sg.Returns[i])
                        continue;
                    foreach (float3 p in sg.P[i])
                    {
                        float d = math.distance(p.xz, b0);
                        int c = CellOf(g, p.xz);
                        if (d <= 350f && p.y <= h0 + 5f && c >= 0 && g.In[c] && RawGrade(g, g.World(c)) <= StationMax)
                            cands.Add((c, d));
                    }
                }
                foreach (var (c, d) in cands.OrderBy(x => x.D))
                {
                    string type = TypeFor(g, c, lifts[Y].TopCell);
                    float2 nb = g.World(c);
                    if (type == null || Enumerable.Range(0, lifts.Count).Any(L => L != Y && SegCrossScore(lifts[L].Bottom.xz, lifts[L].Top.xz, nb, t)) ||
                        UnderRope(g, lifts.Where((_, L) => L != Y).ToList(), lifts[Y].TopCell, c))
                        continue;
                    var guard = new ChordGuard(new List<Lift>());
                    guard.AddLift(nb, t);
                    foreach (PlanRun r in plan.Runs)
                        guard.Add(r.Pts, 0, r.Pts.Count - 1);
                    if (!guard.Fits(new List<float2> { nb }, 0, 0))
                        continue;
                    Lift lf = lifts[Y];
                    foreach (List<List<int>> paths in need)
                        paths.RemoveAll(p => math.distance(g.World(p[0]), b0) < 40f);
                    plan.Links.RemoveAll(p => math.distance(p[p.Count - 1], b0) < 40f);
                    lf.BottomCell = c;
                    lf.Bottom = new float3(nb.x, g.H[c], nb.y);
                    lf.Type = type;
                    lf.Rise = lf.Top.y - lf.Bottom.y;
                    lf.Length = math.distance(lf.Top, lf.Bottom);
                    plan.Log.Add($"L{lf.Id}'s bottom station moved {d:F0} m onto a run-out no run passed ({type} {lf.Length:F0} m / {lf.Rise:F0} m up)");
                    moved++;
                    break;
                }
            }
            return moved;
        }

        /// <summary>Adds link routes while the lift network is split (at most a few); returns how many.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static int LinkByRoutes(Grid g, Plan plan, List<Lift> lifts)
        {
            int added = 0;
            var tried = new HashSet<(int, int)>();
            for (int round = 0; round < 6; round++)
            {
                SkiGraph sg = Ski(g, plan, lifts);
                if (sg.BestSize >= lifts.Count)
                    break;
                var fwd = new HashSet<int>[lifts.Count];
                for (int L = 0; L < lifts.Count; L++)
                    fwd[L] = Closure(L, sg.Reaches);
                var main = Enumerable.Range(0, lifts.Count).Where(L => sg.Component[L] == sg.BestComponent).ToList();
                bool any = false;
                foreach (int Y in Enumerable.Range(0, lifts.Count).Where(L => sg.Component[L] != sg.BestComponent))
                {
                    // into Y from the main lifts, then out of Y back to them
                    if (!main.Any(X => fwd[X].Contains(Y)) && tried.Add((-1, lifts[Y].Id)) && TryLink(g, plan, lifts, sg, main, new List<int> { Y }))
                        any = true;
                    else if (!main.Any(X => fwd[Y].Contains(X)) && tried.Add((lifts[Y].Id, -1)) && TryLink(g, plan, lifts, sg, new List<int> { Y }, main))
                        any = true;
                    if (any)
                        break;
                }
                if (!any)
                    break;
                added++;
            }
            return added;
        }

        /// <summary>A route from where the froms' skiers are to a bottom station of the tos: a cat track when the ground allows one,
        /// else a run (blue at most but for short stretches). False when neither search meets the froms' terrain.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static bool TryLink(Grid g, Plan plan, List<Lift> lifts, SkiGraph sg, List<int> froms, List<int> tos)
        {
            // where the skier can be: every sampled point of the elements skied from the froms' tops (in 60 m buckets)
            var at = new Dictionary<long, List<float3>>();
            static long Key(float2 p) => ((long)Math.Floor(p.x / 60f) << 32) ^ (uint)(int)Math.Floor(p.y / 60f);
            foreach (int i in froms.SelectMany(L => sg.Skied[L]).Distinct())
                foreach (float3 p in sg.P[i])
                {
                    long k = Key(p.xz);
                    if (!at.TryGetValue(k, out List<float3> l))
                        at[k] = l = new List<float3>();
                    l.Add(p);
                }
            // ... or at a top of the froms: a skier starts any element within 150 m of it
            var tops = froms.Select(L => lifts[L].Top).ToList();
            // the cells a link may start from (within 120 m of a top and no higher than 4 m over it, or within LinkOnM of a point
            // the skier is on and no higher than 4 m over it), marked once: a failing search reaches a few hundred thousand cells
            bool[] on = null;
            void Mark(float2 o, float r, float maxH)
            {
                int ox = (int)Math.Round((o.x - g.OX) / g.CellM), oz = (int)Math.Round((o.y - g.OZ) / g.CellM);
                int rc = (int)Math.Ceiling(r / g.CellM) + 1;
                for (int z = Math.Max(0, oz - rc); z <= Math.Min(g.D - 1, oz + rc); z++)
                    for (int x = Math.Max(0, ox - rc); x <= Math.Min(g.W - 1, ox + rc); x++)
                    {
                        int c = z * g.W + x;
                        if (!on[c] && math.distancesq(o, g.World(c)) < r * r && g.Hr[c] <= maxH)
                            on[c] = true;
                    }
            }
            bool On(int c)
            {
                if (on == null)
                {
                    on = new bool[g.W * g.D];
                    foreach (float3 t in tops)
                        Mark(t.xz, 120f, RawH(g, t.xz) + 4f);
                    foreach (List<float3> l in at.Values)
                        foreach (float3 p in l)
                            Mark(p.xz, LinkOnM, p.y + 4f);
                }
                return on[c];
            }
            // where the route may end: a station of the tos, or any served run that delivers there (the skier flows on with it)
            var origins = tos.Select(Z => lifts[Z].BottomCell).Distinct().ToList();
            var toSet = new HashSet<int>(tos);
            for (int i = 0; i < sg.Runs; i++)
                if (sg.Reached[i] && sg.Returns[i] && sg.Delivers[i].Any(toSet.Contains))
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
            string names = string.Join("/", froms.Select(L => "L" + lifts[L].Id)) + " -> " + string.Join("/", tos.Select(L => "L" + lifts[L].Id));
            var diag = new List<string>();
            // a link run keeps off the lift lines: their middles cost more in the search, and a route that would still bring a line
            // over the chord share is passed over
            PointHash lines = null;
            ChordGuard guard = null;
            foreach (int mode in new[] { 0, 1, 2 })
            {
                bool asRun = mode > 0;
                if (asRun && lines == null)
                {
                    lines = new PointHash();
                    foreach (Lift lf in lifts)
                        for (float f = 0.15f; f <= 0.85f; f += 10f / math.max(10f, math.distance(lf.Bottom.xz, lf.Top.xz)))
                            lines.Add(math.lerp(lf.Bottom.xz, lf.Top.xz, f));
                    guard = new ChordGuard(lifts);
                    foreach (PlanRun r in plan.Runs)
                        guard.Add(r.Pts, 0, r.Pts.Count - 1);
                }
                // the link searches share a budget: past it a link gives up (the beginner searches keep the rest)
                if (LinkWork >= LinkBudget)
                {
                    diag.Add("search budget spent");
                    break;
                }
                long work = EasyWork;
                EasyReach er = EasyUp(g, origins, avoid, mode == 0 ? CatTrack : mode == 1 ? BlueWay : BlackWay, lines);
                LinkWork += EasyWork - work;
                diag.Add($"{(mode == 0 ? "cat track" : mode == 1 ? "blue run" : "black run")} reach {er.Cost.Count} cell(s)");
                foreach (int c in er.Cost.Where(kv => kv.Value > 0f && On(kv.Key)).OrderBy(kv => kv.Value).Take(12).Select(kv => kv.Key))
                {
                    List<int> cells = er.Route(c);
                    if (cells.Count < 3)
                        continue;
                    var tries = new[] { Polyline(g, cells, false), cells.ConvertAll(x => g.World(x)) };
                    foreach (List<float2> route in tries)
                    {
                        if (!asRun)
                        {
                            if (!ConnectorOk(g, route))
                                continue;
                            plan.Connectors.Add(route);
                            plan.Links.Add(route);
                            plan.Log.Add($"link {names}: cat track {RunLength(route):F0} m");
                            return true;
                        }
                        List<float2> ok = SanitizeRun(g, route);
                        if (ok == null || ok.Count < Densify(route).Count || RawH(g, ok[0]) - RawH(g, ok[ok.Count - 1]) < MinRunDropM)
                            continue;
                        SlopeBand band = RunBand(g, ok);
                        if (band == SlopeBand.DoubleBlack || (mode == 1 && band == SlopeBand.Black) || !guard.FitsAdding(ok, 0, ok.Count - 1))
                            continue;
                        plan.Runs.Add(new PlanRun { Pts = ok, Band = band, By = lifts[froms[0]], Kind = "link" });
                        plan.Log.Add($"link {names}: {band} run {RunLength(ok):F0} m, {RawH(g, ok[0]) - RawH(g, ok[ok.Count - 1]):F0} m down");
                        return true;
                    }
                }
            }
            plan.Log.Add($"link {names}: no route ({string.Join("; ", diag)})");
            return false;
        }
    }
}
