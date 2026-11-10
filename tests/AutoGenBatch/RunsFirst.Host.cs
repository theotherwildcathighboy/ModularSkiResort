// RunsFirst host: the test host's entry into the runs-first planner (mod/SkiResortMod/Systems/RunsFirstPlanner*.cs).

using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class RunsFirst
    {
        public static int Run(string[] args) => RunsFirstPlanner.Run(args);

        internal static RunsFirstResult PlanRegion(HeightSnapshot snap, float2[] ring, RunsFirstOptions options, string id = "region") =>
            RunsFirstPlanner.PlanRegion(snap, ring, options, id);
    }
}
