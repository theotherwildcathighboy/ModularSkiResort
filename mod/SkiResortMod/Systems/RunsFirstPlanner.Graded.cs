// RunsFirst graded tracks (round v3): how scorecard v3 reads an element, so the planner sees what the scorecard sees. The
// scorecard rates a run by the ground under it (the 85th percentile of the raw 10 m terrain grade at its 10 m samples). The
// game's classifier rates a painted run the same way. A traverse across a black face is black. A connector is a graded cat
// track (the grading tool cuts a bench across the face). The scorecard rates it by its own grade along the path. Under the
// blue line it is a green cat track. Steeper, it is a diagonal run of that band.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class RunsFirstPlanner
    {
        /// <summary>The v3 band of a run: the ground under it (85th percentile of the raw 10 m grade at its 10 m samples, and flat reads green).</summary>
        [MethodImpl(Hot)]
        private static SlopeBand GroundBand(Grid g, List<float2> run)
        {
            List<float2> d = Densify(run);
            var gs = new float[d.Count];
            for (int i = 0; i < d.Count; i++)
                gs[i] = RawGrade(g, d[i]);
            Array.Sort(gs);
            SlopeBand b = SkiSlopeClassifier.BandOf(gs.Length > 0 ? gs[(int)((gs.Length - 1) * 0.85f)] : 0f);
            return b == SlopeBand.Flat ? SlopeBand.Green : b;
        }

        // v5 natural runs (RF_NATURAL). 0: none. 1: double black by the ground. 2: also a black run whose ground is mostly past the
        // double-black line (cliffs and rock: hostile ground a designer leaves ungroomed)
        private int NaturalMode => O.NaturalMode;
        private float NaturalHostile => O.NaturalHostile;

        /// <summary>Scorecard v5: skiers ski steep ground ungroomed. A run (not a graded track) is natural (the mod's RunKind.Natural)
        /// when its ground rates double black, or when its ground rates black with NaturalHostile of it past the double-black line.
        /// Returns the runs marked.</summary>
        private int MarkNatural(Grid g, PlanDraft plan)
        {
            if (NaturalMode == 0)
                return 0;
            int marked = 0;
            float km = 0f;
            foreach (PlanRun r in plan.Runs)
            {
                if (r.Graded || r.Pts.Count < 2)
                    continue;
                SlopeBand band = GroundBand(g, r.Pts);
                bool natural = band == SlopeBand.DoubleBlack;
                if (!natural && band == SlopeBand.Black && NaturalMode >= 2)
                {
                    List<float2> d = Densify(r.Pts);
                    natural = d.Count(p => RawGrade(g, p) >= SkiSlopeClassifier.GradeDoubleBlack) >= NaturalHostile * d.Count;
                }
                if (natural)
                {
                    r.Surface = RunKind.Natural;
                    marked++;
                    km += RunLength(r.Pts) / 1000f;
                }
            }
            plan.Log.Add($"natural runs: {marked} of {plan.Runs.Count(r => !r.Graded)} ({km:F1} km) ungroomed on steep ground");
            return marked;
        }

        /// <summary>A graded track's own grade along the path, as the scorecard rates a connector: the 85th percentile of the drop over
        /// 20-30 m of path from every 10 m sample.</summary>
        [MethodImpl(Hot)]
        private static float AlongGrade(Grid g, List<float2> pl)
        {
            List<float3> p = Sampled(g, pl);
            var cum = new float[p.Count];
            for (int k = 1; k < p.Count; k++)
                cum[k] = cum[k - 1] + math.distance(p[k].xz, p[k - 1].xz);
            var cs = new List<float>(p.Count);
            for (int k = 0, j = 0; k < p.Count; k++)
            {
                j = Math.Max(j, k);
                while (j < p.Count - 1 && cum[j] - cum[k] < 30f)
                    j++;
                if (cum[j] - cum[k] >= 20f)
                    cs.Add(Math.Max(0f, p[k].y - p[j].y) / (cum[j] - cum[k]));
            }
            cs.Sort();
            return cs.Count > 0 ? cs[(int)((cs.Count - 1) * 0.85f)] : 0f;
        }

        /// <summary>A connector as the scorecard reads it: a green cat track (Run false) below the blue line, else a diagonal run of
        /// its along-path band when it drops.</summary>
        [MethodImpl(Hot)]
        private static (bool Run, SlopeBand Band) TrackClass(Grid g, List<float2> pl)
        {
            float along = AlongGrade(g, pl);
            if (along >= SkiSlopeClassifier.GradeBlue && RawH(g, pl[0]) > RawH(g, pl[pl.Count - 1]))
                return (true, SkiSlopeClassifier.BandOf(along));
            return (false, SlopeBand.Green);
        }

        private float StraightDevM => O.StraightDevM;   // a straight piece stays this close to the track's own points

        /// <summary>Green cat tracks pulled straight. A route search steps between cell centers, and the smoothing only softens the
        /// staircase. So a track wobbles where a cat track on the ground runs straight. Between its anchors, a track takes the longest
        /// straight pieces. The anchors are its ends, its points near where another element starts or ends, and its points near a lift
        /// bottom. A piece stays within StraightDevM of the track's points, inside the region, under the blue line and within the
        /// connector rules. The code keeps a straightened track only where it still reads green. It keeps the pass only where the ski
        /// graph loses nothing. Returns the tracks pulled straight.</summary>
        [MethodImpl(Hot)]
        private int StraightenTracks(Grid g, PlanDraft plan, List<Lift> lifts)
        {
            var ends = new List<float2>();
            foreach (PlanRun r in plan.Runs)
                ends.AddRange(new[] { r.Pts[0], r.Pts[r.Pts.Count - 1] });
            foreach (List<float2> c in plan.Connectors)
                ends.AddRange(new[] { c[0], c[c.Count - 1] });
            SkiRead before = SkiRead.Of(g, plan, lifts);
            var old = new List<(List<float2> C, List<float2> Pts)>();
            float kmBefore = 0f, kmAfter = 0f;
            foreach (List<float2> c in plan.Connectors)
            {
                if (c.Count < 4 || TrackClass(g, c).Run)
                    continue;
                var anchor = new bool[c.Count];
                anchor[0] = anchor[c.Count - 1] = true;
                for (int k = 1; k < c.Count - 1; k++)
                {
                    float2 q = c[k];
                    anchor[k] = ends.Any(e => math.distancesq(e, q) < 50f * 50f) || lifts.Any(lf => math.distancesq(lf.Bottom.xz, q) < 70f * 70f);
                }
                var o = new List<float2> { c[0] };
                for (int i = 0; i < c.Count - 1;)
                {
                    int j = i + 1;
                    for (int k = i + 2; k < c.Count && !anchor[k - 1] && math.distance(c[i], c[k]) < 300f && StraightOk(g, c, i, k); k++)
                        j = k;
                    o.Add(c[j]);
                    i = j;
                }
                if (o.Count == c.Count || !ConnectorOk(g, o) || TrackClass(g, o).Run)
                    continue;
                old.Add((c, c.ToList()));
                kmBefore += RunLength(c) / 1000f;
                kmAfter += RunLength(o) / 1000f;
                c.Clear();
                c.AddRange(o);
            }
            if (old.Count == 0)
                return 0;
            if (before.Loses(SkiRead.Of(g, plan, lifts)))
            {
                foreach (var (c, pts) in old)
                {
                    c.Clear();
                    c.AddRange(pts);
                }
                plan.Log.Add($"straight tracks: undone ({old.Count} green cat track(s) pulled straight cost the skiing)");
                return 0;
            }
            plan.Log.Add($"straight tracks: {old.Count} green cat track(s) pulled straight, {kmBefore:F1} km -> {kmAfter:F1} km");
            return old.Count;
        }

        /// <summary>Whether track c may run straight from its point i to its point k. All of these must hold:
        /// - every point between lies within StraightDevM of the piece
        /// - the piece lies inside the region
        /// - the piece has no 30 m pitch or 50 m climb past the connector rules
        /// - no 20-30 m of the piece lies at the blue line.</summary>
        [MethodImpl(Hot)]
        private bool StraightOk(Grid g, List<float2> c, int i, int k)
        {
            for (int m = i + 1; m < k; m++)
                if (SegDist(c[m], c[i], c[k]) > StraightDevM)
                    return false;
            List<float3> p = Sampled(g, new List<float2> { c[i], c[k] });
            foreach (float3 q in p)
            {
                int cell = CellOf(g, q.xz);
                if (cell < 0 || !g.In[cell] || float.IsNaN(q.y))
                    return false;
            }
            for (int s = 0; s + 5 < p.Count; s++)
                if (p[s + 5].y - p[s].y > ConnRiseMax)
                    return false;
            for (int s = 0; s + 2 < p.Count; s++)
                for (int t = s + 2; t <= Math.Min(s + 3, p.Count - 1); t++)
                {
                    float drop = (p[s].y - p[t].y) / math.max(1f, math.distance(p[s].xz, p[t].xz));
                    if (drop > 0.95f * SkiSlopeClassifier.GradeBlue || t == s + 3 && drop > ConnPitchMax)
                        return false;
                }
            return true;
        }

        /// <summary>RF_V3DBG: the plan's elements by their v3 class, and the crowded samples (the scorecard's rule, served or not) by the
        /// kinds of the two elements.</summary>
        private void V3Debug(Grid g, PlanDraft plan, List<Lift> lifts)
        {
            var els = new List<(string Kind, float3[] P, bool Run)>();
            foreach (PlanRun r in plan.Runs)
                els.Add(("run:" + r.Kind, Sampled(g, r.Pts).ToArray(), true));
            int[] cls = new int[6];
            float[] km = new float[6];
            foreach (List<float2> c in plan.Connectors)
            {
                var (run, band) = TrackClass(g, c);
                int k = run ? (int)band : 0;
                cls[k]++;
                km[k] += RunLength(c) / 1000f;
                els.Add((run ? "graded-" + band : "cat", Sampled(g, c).ToArray(), run));
            }
            Console.WriteLine($"V3 connectors: green cat {cls[0]} ({km[0]:F1} km), diagonal blue {cls[2]} ({km[2]:F1} km), black {cls[3]} ({km[3]:F1} km), " +
                $"double black {cls[4]} ({km[4]:F1} km); runs by ground: " +
                string.Join(" ", Enumerable.Range(1, 4).Select(b => $"{(SlopeBand)b} {plan.Runs.Count(r => GroundBand(g, r.Pts) == (SlopeBand)b)}")) +
                $"; graded stubs (< 50 m drop) {els.Count(e => e.Run && e.Kind.StartsWith("graded") && e.P[0].y - e.P[e.P.Length - 1].y < 50f)}");
            var by = new Dictionary<string, int>();
            int pts = 0, crowded = 0;
            for (int i = 0; i < els.Count; i++)
            {
                if (!els[i].Run)
                    continue;
                float3[] P = els[i].P;
                for (int k = 0; k < P.Length - 15; k++)
                {
                    pts++;
                    for (int j = 0; j < els.Count; j++)
                    {
                        if (j == i || !els[j].Run)
                            continue;
                        float3[] Q = els[j].P;
                        if (math.distancesq(Q[0].xz, P[k].xz) < 1e4f || math.distancesq(Q[Q.Length - 1].xz, P[k].xz) < 1e4f)
                            continue;
                        if (Q.Any(q => math.distancesq(q.xz, P[k].xz) < 1600f))
                        {
                            crowded++;
                            string key = els[i].Kind.Split(':')[0].Replace("graded-Blue", "graded").Replace("graded-Black", "graded").Replace("graded-DoubleBlack", "graded")
                                + " by " + els[j].Kind.Split(':')[0].Replace("graded-Blue", "graded").Replace("graded-Black", "graded").Replace("graded-DoubleBlack", "graded");
                            by[key] = by.TryGetValue(key, out int v) ? v + 1 : 1;
                            break;
                        }
                    }
                }
            }
            Console.WriteLine($"V3 crowding: {crowded} of {pts} samples ({(pts > 0 ? crowded / (float)pts : 0f):P0}); " + string.Join(", ", by.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " " + kv.Value)));
            // the graded pieces: length, drop, along-path grade, link or traverse tree, and the crowded samples they take part in
            var links = new HashSet<List<float2>>(plan.Links);
            var rows = new List<(int Hits, string Text)>();
            for (int ci = 0; ci < plan.Connectors.Count; ci++)
            {
                int e = plan.Runs.Count + ci;
                if (!els[e].Run)
                    continue;
                float3[] Q = els[e].P;
                int hits = 0;
                for (int i = 0; i < els.Count; i++)
                {
                    if (i == e || !els[i].Run)
                        continue;
                    float3[] P = els[i].P;
                    for (int k = 0; k < P.Length - 15; k++)
                        if (math.distancesq(Q[0].xz, P[k].xz) >= 1e4f && math.distancesq(Q[Q.Length - 1].xz, P[k].xz) >= 1e4f && Q.Any(q => math.distancesq(q.xz, P[k].xz) < 1600f))
                            hits++;
                    for (int k = 0; k < Q.Length - 15; k++)
                        if (math.distancesq(P[0].xz, Q[k].xz) >= 1e4f && math.distancesq(P[P.Length - 1].xz, Q[k].xz) >= 1e4f && P.Any(q => math.distancesq(q.xz, Q[k].xz) < 1600f))
                            hits++;
                }
                List<float2> c = plan.Connectors[ci];
                rows.Add((hits, $"{(links.Contains(c) ? "link" : "tree")} {RunLength(c):F0} m, {Q[0].y - Q[Q.Length - 1].y:F0} m down, along {AlongGrade(g, c):F2}, crowd {hits}, from {c[0].x:F0},{c[0].y:F0} to {c[c.Count - 1].x:F0},{c[c.Count - 1].y:F0}"));
            }
            foreach (var row in rows.OrderByDescending(r => r.Hits).Take(O.DebugV3All ? 999 : 12))
                Console.WriteLine("V3 graded " + row.Text);
        }

        /// <summary>A route the grading tool can cut as a green cat track: the connector rules on the raw terrain and an along-path
        /// grade under the blue line (less a margin).</summary>
        private static bool GreenTrack(Grid g, List<float2> pl, float margin = 0.01f) =>
            ConnectorOk(g, pl) && AlongGrade(g, pl) < SkiSlopeClassifier.GradeBlue - margin;
    }
}
