// Resort Planner (v2) stage A: lift skeleton: chord candidates, tower check, selection, pinned lifts (spec 4.4).
// A0 spine STUB: the hooks below keep v1 behavior (Runs-mode output is bit-identical, Resort mode returns the v1 runs plus an
// empty ResortPlan). A4 replaces the bodies; nothing else in this file's callers changes.
// Pure and Entities-free (usings System, System.Collections.Generic, Unity.Mathematics only). Spec: docs/research/quickruns-v2/spec/stage-a-spec.md.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunAutoPlanner
    {
        /// <summary>Stage Lifts (Resort only): one candidate chord / one tower plan per unit. Hands over to the band passes (EnterRoute,
        /// whose sources are the selected lifts' bottom pads through IsResortSource). Stub: no lifts, straight to Route.</summary>
        private void UnitLifts()
        {
            EnterRoute();
        }
    }
}
