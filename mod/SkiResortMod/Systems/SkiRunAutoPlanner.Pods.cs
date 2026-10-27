// Resort Planner (v2) stage A: pod route and pods (spec 4.2).
// A0 spine STUB: the hooks below keep v1 behavior (Runs-mode output is bit-identical, Resort mode returns the v1 runs plus an
// empty ResortPlan). A3 replaces the bodies; nothing else in this file's callers changes.
// Pure and Entities-free (usings System, System.Collections.Generic, Unity.Mathematics only). Spec: docs/research/quickruns-v2/spec/stage-a-spec.md.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunAutoPlanner
    {
        /// <summary>Stage PodRoute (Resort only): the labeled reverse Dijkstra from every station-capable pad (scratch m_Pred[0] /
        /// m_Dist[0], overwritten later by the band passes). Stub: hands over to Pods.</summary>
        private void UnitPodRoute()
        {
            Next(Stage.Pods);
        }

        /// <summary>Stage Pods (Resort only): union-find over pads by drain, ridge split, pod statistics. Stub: hands over to Lifts.</summary>
        private void UnitPods()
        {
            Next(Stage.Lifts);
        }
    }
}
