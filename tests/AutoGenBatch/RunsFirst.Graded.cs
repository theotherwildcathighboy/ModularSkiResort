// RunsFirst graded tracks (round v3): how scorecard v3 reads an element, so the planner sees what the scorecard sees. A run is rated by
// the ground under it (the 85th percentile of the raw 10 m terrain grade at its 10 m samples), as the game's classifier rates a painted
// run: a traverse across a black face is black. A connector is a graded cat track (the grading tool cuts a bench across the face) and
// is rated by its own grade along the path: under the blue line it is a green cat track; steeper, a diagonal run of that band.

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
        /// <summary>The v3 band of a run: the ground under it (85th percentile of the raw 10 m grade at its 10 m samples; flat reads green).</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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

        /// <summary>A graded track's own grade along the path, as the scorecard rates a connector: the 85th percentile of the drop over
        /// 20-30 m of path from every 10 m sample.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static (bool Run, SlopeBand Band) TrackClass(Grid g, List<float2> pl)
        {
            float along = AlongGrade(g, pl);
            if (along >= SkiSlopeClassifier.GradeBlue && RawH(g, pl[0]) > RawH(g, pl[pl.Count - 1]))
                return (true, SkiSlopeClassifier.BandOf(along));
            return (false, SlopeBand.Green);
        }

        /// <summary>RF_V3DBG: the plan's elements by their v3 class, and the crowded samples (the scorecard's rule, served or not) by the
        /// kinds of the two elements.</summary>
        private static void V3Debug(Grid g, Plan plan, List<Lift> lifts)
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
        }

        /// <summary>A route the grading tool can cut as a green cat track: the connector rules on the raw terrain and an along-path
        /// grade under the blue line (less a margin).</summary>
        private static bool GreenTrack(Grid g, List<float2> pl, float margin = 0.01f) =>
            ConnectorOk(g, pl) && AlongGrade(g, pl) < SkiSlopeClassifier.GradeBlue - margin;
    }
}
