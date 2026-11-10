// RunsFirstPlanner prints (port round 2b-f): a 64-bit print of the plan state at every pass boundary (Tick), for the cross-runtime
// diagnosis. The game and the host plan the same dumped input; the first pass whose print differs is where the two runtimes split.
// The state a pass can change: the grid (heights and mask, printed once), the flow (accumulations, printed once), the candidate lines,
// the lifts and the plan draft (runs, connectors, links). The parts are known from the pass where they exist (PlanCore and BuildPlan
// set them). Cost: one pass over the plan's points per Tick (a few hundred thousand floats), well under a millisecond each.
// A report only: no pass reads a print. Pure C# 9, net48-clean.

using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class RunsFirstPlanner
    {
        /// <summary>The part names of a planner print, in RunsFirstPrint.Parts order.</summary>
        public static readonly string[] PrintParts = { "grid", "flow", "lines", "lifts", "runs", "connectors", "links" };

        private readonly List<RunsFirstPrint> m_Prints = new List<RunsFirstPrint>();
        private Grid m_PrGrid;
        private float[] m_PrAcc, m_PrAccR;
        private List<Line> m_PrLines;
        private List<Lift> m_PrLifts;
        private PlanDraft m_PrPlan;
        private ulong m_PrGridH, m_PrFlowH;
        private bool m_PrGridDone, m_PrFlowDone;

        /// <summary>The per-pass prints of the last plan, in pass order (one per Tick).</summary>
        public IReadOnlyList<RunsFirstPrint> PassPrints => m_Prints;

        private void PrintPass(string what)
        {
            var parts = new ulong[7];
            var counts = new int[7];
            if (m_PrGrid != null)
            {
                if (!m_PrGridDone)
                {
                    ulong h = RunsFirstPrint.Seed;
                    Grid g = m_PrGrid;
                    h = RunsFirstPrint.Mix(RunsFirstPrint.Mix(h, (ulong)g.W), (ulong)g.D);
                    for (int c = 0; c < g.H.Length; c++)
                        h = RunsFirstPrint.Mix(h, ((ulong)math.asuint(g.H[c]) << 1) | (g.In[c] ? 1UL : 0UL));
                    m_PrGridH = h;
                    m_PrGridDone = true;
                }
                parts[0] = m_PrGridH;
                counts[0] = m_PrGrid.W * m_PrGrid.D;
            }
            if (m_PrAcc != null)
            {
                if (!m_PrFlowDone)
                {
                    ulong h = RunsFirstPrint.Seed;
                    for (int c = 0; c < m_PrAcc.Length; c++)
                        h = RunsFirstPrint.Mix(h, ((ulong)math.asuint(m_PrAcc[c]) << 32) | math.asuint(m_PrAccR[c]));
                    m_PrFlowH = h;
                    m_PrFlowDone = true;
                }
                parts[1] = m_PrFlowH;
                counts[1] = m_PrAcc.Length;
            }
            var items = new ulong[7][];
            if (m_PrLines != null)
            {
                var it = items[2] = new ulong[m_PrLines.Count];
                for (int i = 0; i < it.Length; i++)
                {
                    Line l = m_PrLines[i];
                    ulong h = RunsFirstPrint.Mix(RunsFirstPrint.Seed, (ulong)l.Id);
                    h = RunsFirstPrint.Mix(h, (ulong)(uint)l.Pick | (l.Picked ? 1UL << 32 : 0UL) | (l.Merge ? 1UL << 33 : 0UL) | ((ulong)(uint)l.Band << 34));
                    h = RunsFirstPrint.Mix(h, (ulong)(uint)l.Lift);
                    h = RunsFirstPrint.Mix(h, l.Score);
                    h = RunsFirstPrint.Mix(h, l.Rating);
                    h = RunsFirstPrint.Mix(h, (ulong)l.Pts.Count);
                    foreach (float3 p in l.Pts)
                        h = RunsFirstPrint.Mix(h, p);
                    it[i] = h;
                }
            }
            if (m_PrLifts != null)
            {
                var it = items[3] = new ulong[m_PrLifts.Count];
                for (int i = 0; i < it.Length; i++)
                    it[i] = LiftPrint(RunsFirstPrint.Seed, m_PrLifts[i]);
            }
            if (m_PrPlan != null)
            {
                var it = items[4] = new ulong[m_PrPlan.Runs.Count];
                for (int i = 0; i < it.Length; i++)
                    it[i] = RunPrint(RunsFirstPrint.Seed, m_PrPlan.Runs[i]);
                it = items[5] = new ulong[m_PrPlan.Connectors.Count];
                for (int i = 0; i < it.Length; i++)
                    it[i] = RunsFirstPrint.Mix(RunsFirstPrint.Seed, m_PrPlan.Connectors[i]);
                it = items[6] = new ulong[m_PrPlan.Links.Count];
                for (int i = 0; i < it.Length; i++)
                    it[i] = RunsFirstPrint.Mix(RunsFirstPrint.Seed, m_PrPlan.Links[i]);
            }
            m_Prints.Add(RunsFirstPrint.Of(what, parts, counts, items));
        }

        private static ulong LiftPrint(ulong h, Lift lf)
        {
            h = RunsFirstPrint.Mix(h, (ulong)(uint)lf.Id | (lf.RidesDown ? 1UL << 32 : 0UL));
            h = RunsFirstPrint.Mix(h, lf.Type);
            h = RunsFirstPrint.Mix(h, lf.Kind);
            h = RunsFirstPrint.Mix(h, lf.Bottom);
            return RunsFirstPrint.Mix(h, lf.Top);
        }

        private static ulong RunPrint(ulong h, PlanRun r)
        {
            h = RunsFirstPrint.Mix(h, r.Kind);
            h = RunsFirstPrint.Mix(h, (ulong)(uint)r.Band | (r.Graded ? 1UL << 32 : 0UL) | (r.Groomed ? 1UL << 33 : 0UL));
            h = RunsFirstPrint.Mix(h, (ulong)(uint)(r.By?.Id ?? -1));
            h = RunsFirstPrint.Mix(h, (ulong)(uint)(r.Src?.Id ?? -1));
            return RunsFirstPrint.Mix(h, r.Pts);
        }
    }
}
