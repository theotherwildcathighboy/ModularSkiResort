// Resort Planner (v2) stage A: plan graph, ways home, dead ends, verdicts, honest mix, site verdict (spec 4.6).
// A0 spine STUB: the hooks below keep v1 behavior (Runs-mode output is bit-identical, Resort mode returns the v1 runs plus an
// empty ResortPlan). A6 replaces the bodies; nothing else in this file's callers changes.
// Pure and Entities-free (usings System, System.Collections.Generic, Unity.Mathematics only). Spec: docs/research/quickruns-v2/spec/stage-a-spec.md.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunAutoPlanner
    {
        /// <summary>End of Finish (Resort only, after kinds, clusters and v1 warnings): builds RunPod / RunLift, the graph, verdicts, mix,
        /// site verdict, replaces Clusters by the pod clusters and adds NoLiftSite / Download / DeadEnd. The probe slots are copied into
        /// the plan by the caller afterwards. Stub: an empty plan (arrays empty, Fingerprint 0) so the result stays v1-identical.</summary>
        private void FinishResort(AutoPlanResult res)
        {
            res.Resort = new ResortPlan { TreeLineY = m_In.TreeLineY };
        }
    }
}
