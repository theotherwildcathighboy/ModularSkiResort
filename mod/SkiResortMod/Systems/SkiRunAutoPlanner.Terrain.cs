// Resort Planner (v2) stage A: terrain: bands, pads, benches, ridges, summits (spec 4.1).
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
        /// <summary>Stage Terrain (Resort only): row chunks over m_O / m_Flow / m_Flags into m_OS, m_Band, m_PadOf, m_Ridge and the pad
        /// list. Stub: hands over to PodRoute.</summary>
        private void UnitTerrain()
        {
            Next(Stage.PodRoute);
        }
    }
}
