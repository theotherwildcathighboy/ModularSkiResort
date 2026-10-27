// Resort Planner (v2) stage A: run fans: seeds, sinks, developable cap, widths, merge zones (spec 4.5).
// A0 spine STUB: the hooks below keep v1 behavior (Runs-mode output is bit-identical, Resort mode returns the v1 runs plus an
// empty ResortPlan). A5 replaces the bodies; nothing else in this file's callers changes.
// Pure and Entities-free (usings System, System.Collections.Generic, Unity.Mathematics only). Spec: docs/research/quickruns-v2/spec/stage-a-spec.md.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunAutoPlanner
    {
        /// <summary>Route-pass source rule in Resort mode (StartPassRows, one call per cell and pass, m_Pass is the pass): the selected
        /// lifts' bottom-pad cells instead of the base band. Stub: the v1 rule (base band below the pass's SourceTop).</summary>
        private bool IsResortSource(int c) => (m_Flags[c] & FBase) != 0 && m_H[c] <= SourceTop(m_Pass);

        /// <summary>Seeds hook (UnitSeeds, Resort only, called once per unit): true = this unit was the fan seeding's (it owns m_Cursor2
        /// and calls Next(Stage.Candidates) when done, and does the v1 case 0 bookkeeping: m_FieldWarnings = m_Result.Warnings,
        /// ClearCandidates); false = run the v1 seeding. Stub: false.</summary>
        private bool FanSeeds() => false;

        /// <summary>Select hook (end of BeginSelect, Resort only): per-lift run caps and the developable cap per pod. Stub: nothing.</summary>
        private void FanCaps()
        {
        }
    }
}
