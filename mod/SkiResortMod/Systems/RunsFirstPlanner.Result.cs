// RunsFirstPlanner result: fills the plain plan object (SkiResortMod.Systems.RunsFirstResult, mod/SkiResortMod/Systems/RunsFirstResult.cs)
// from the planner's own types. Pure C# 9, net48-clean.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class RunsFirstPlanner
    {
        /// <summary>The plain plan object of a finished plan (copies: the planner's own types stay inside). A plain run's band is the
        /// mod rater's band of its ribbon (RaterBands bit RaterResult): the band scorecard v7 reads and the game shows. A graded run keeps
        /// its band along the path.</summary>
        private static RunsFirstResult MakeResult(Grid g, PlanDraft built, List<Lift> lifts, RunsFirstBottom[] bottom)
        {
            var res = new RunsFirstResult();
            foreach (PlanRun r in built.Runs)
                res.Runs.Add(new RunsFirstResult.Run
                {
                    Pts = new List<float2>(r.Pts), Band = !r.Graded && g.Rater.Use(RaterResult) ? RaterBand(g, r.Pts) : r.Band, Kind = r.Kind, Graded = r.Graded, Groomed = r.Groomed,
                    Lift = r.By == null ? 0 : lifts.IndexOf(r.By) + 1,
                });
            foreach (List<float2> c in built.Connectors)
                res.Connectors.Add(new List<float2>(c));
            foreach (List<float2> c in built.Links)
                res.Links.Add(new List<float2>(c));
            int[] root = V3Bases(g, lifts, out HashSet<int> bases);   // the base clusters, as the way-home passes read them
            for (int i = 0; i < lifts.Count; i++)
                res.Lifts.Add(new RunsFirstResult.Lift
                {
                    Id = i + 1, Type = lifts[i].Type, Kind = lifts[i].Kind, Bottom = lifts[i].Bottom, Top = lifts[i].Top, RidesDown = lifts[i].RidesDown,
                    ToBottom = bottom != null && i < bottom.Length ? bottom[i] : RunsFirstBottom.Reaches,
                    Base = bases.Contains(root[i]),
                });
            res.Log.AddRange(built.Log);
            return res;
        }
    }
}
