// Quick runs tool: the Resort Planner's hub-side state (stage A, task A7; stage-a-spec.md 6.1-6.3). The per-pod and per-lift toggles
// (keyed by the plan's stable pod / lift keys, so they survive a Reselect), the pod rows the hub shows, the hover pick of a pod ring or
// a lift chord (tooltip lines), the click pick that toggles them, and "plan around this lift" (a click on an existing lift's station
// while the region is empty starts a region around that lift). The rules are pure (QuickRunsPlanRules.cs, linked into the test host).
// A toggle only changes which runs are enabled (QuickRunsToolSystem.Scan.cs RefreshEnabled asks PlanLeftOut): nothing is re-planned.
// Engine notes: EngineNotes/QuickRunsToolSystem.md (Resort Planner hub)

using System.Collections.Generic;
using System.Globalization;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class QuickRunsToolSystem
    {
        private readonly HashSet<uint> m_OffPods = new HashSet<uint>(), m_OffLifts = new HashSet<uint>();
        private QuickPodRow[] m_PodRows = new QuickPodRow[0];
        private int m_PodRowsRevision = -1;
        private AutoPlanResult m_PodRowsFor;
        private int m_AroundRevision = -1;   // GradeEdit.Revision right after "plan around" set the corners (right click clears them at once)

        /// <summary>Pod / lift under the cursor in the Preview (tooltip lines), -1 none.</summary>
        internal int HoverPod { get; private set; } = -1;
        internal int HoverLift { get; private set; } = -1;

        /// <summary>The published plan while a Resort preview shows; else null.</summary>
        internal ResortPlan Plan => HasPreview ? m_Published.Resort : null;

        /// <summary>The region is empty and the census knows a station: the tooltip says a click on a lift station plans around it.</summary>
        internal bool CanPlanAround
        {
            get
            {
                if (m_Phase != ToolPhase.Region || m_Edit.Count != 0)
                    return false;
                LiftCensusSystem census = Census();
                return census != null && census.IsArmed && census.StationCount > 0;
            }
        }

        /// <summary>Pod rows for the hub (cached per hub revision and published result).</summary>
        internal QuickPodRow[] PodRows
        {
            get
            {
                if (m_PodRowsRevision != HubRevision || !ReferenceEquals(m_PodRowsFor, m_Published))
                {
                    m_PodRowsRevision = HubRevision;
                    m_PodRowsFor = m_Published;
                    m_PodRows = Plan == null ? new QuickPodRow[0] : QuickRunsPlanRules.PodRows(m_Published, m_EnabledByIndex, m_OffPods, m_OffLifts);
                }
                return m_PodRows;
            }
        }

        /// <summary>RefreshEnabled's extra test: the run's pod or lift is switched off.</summary>
        private bool PlanLeftOut(AutoPlanResult r, int run) => QuickRunsPlanRules.LeftOut(r.Resort, run, m_OffPods, m_OffLifts);

        internal bool IsLiftOff(uint key) => m_OffLifts.Contains(key);

        private LiftCensusSystem Census()
        {
            if (m_LiftCensus == null)
                m_LiftCensus = World.GetExistingSystemManaged<LiftCensusSystem>();
            return m_LiftCensus;
        }

        private void ClearPlanToggles()
        {
            m_OffPods.Clear();
            m_OffLifts.Clear();
        }

        /// <summary>The hub's pod / lift toggle (HubSet "pod" / "lift", value = the plan key as a signed int).</summary>
        private string TogglePlanKey(bool pod, int value)
        {
            string what = pod ? "pod" : "lift";
            if (m_Phase == ToolPhase.Scanning)
                return "busy: scanning (wait for the preview)";
            ResortPlan plan = Plan;
            if (plan == null)
                return "no preview";
            uint key = unchecked((uint)value);
            string name = null;
            if (pod)
            {
                for (int i = 0; i < plan.Pods.Length && name == null; i++)
                    if (plan.Pods[i].Key == key)
                        name = QuickRunsPlanRules.Letter(plan.Pods[i].Id);
            }
            else
            {
                for (int i = 0; i < plan.Lifts.Length && name == null; i++)
                    if (plan.Lifts[i].Key == key)
                        name = LiftName(plan, i);
            }
            if (name == null)
                return $"no {what} with key {key:X8} in this plan";
            HashSet<uint> set = pod ? m_OffPods : m_OffLifts;
            bool nowOn = set.Remove(key);
            if (!nowOn)
                set.Add(key);
            RefreshEnabled();
            Say(string.Format(CultureInfo.InvariantCulture, "QuickRuns [toggle]: {0} {1} {2} — {3} of {4} run(s) enabled, fee {5}",
                what, name, nowOn ? "ON" : "OFF", m_Totals.Enabled, m_Totals.Runs, m_Totals.Fee));
            return $"{what} {name} {(nowOn ? "on" : "off")}";
        }

        /// <summary>"P1" for a proposal; "Existing 1" is a locale key in the hub / tooltip, "E1" in logs.</summary>
        internal static string LiftName(ResortPlan plan, int lift) =>
            (plan.Lifts[lift].Origin == LiftOrigin.Pinned ? "E" : "P") + QuickRunsPlanRules.Ordinal(plan, lift).ToString(CultureInfo.InvariantCulture);

        /// <summary>A click in the Preview: a proposed chord toggles its lift, else a run (v1), else a pod ring toggles its pod.</summary>
        private void PreviewPick(float2 hit)
        {
            ResortPlan plan = Plan;
            if (plan != null)
            {
                int lift = QuickRunsPlanRules.NearestLift(plan, hit, QuickRunsPlanRules.LiftPickM);
                if (lift >= 0)
                {
                    TogglePlanKey(false, unchecked((int)plan.Lifts[lift].Key));
                    return;
                }
                if (QuickRunsRules.Pick(hit, m_Published.Runs) < 0)
                {
                    int pod = QuickRunsPlanRules.NearestPod(plan, hit, QuickRunsPlanRules.PodPickM);
                    if (pod >= 0)
                    {
                        TogglePlanKey(true, unchecked((int)plan.Pods[pod].Key));
                        return;
                    }
                }
            }
            TogglePick(hit);
        }

        private void UpdateHoverPlan()
        {
            int pod = -1, lift = -1;
            ResortPlan plan = m_Phase == ToolPhase.Preview && m_HasHit && !Dragging ? Plan : null;
            if (plan != null)
            {
                lift = QuickRunsPlanRules.NearestLift(plan, m_Hit.xz, QuickRunsPlanRules.LiftPickM);
                if (lift < 0)
                    pod = QuickRunsPlanRules.NearestPod(plan, m_Hit.xz, QuickRunsPlanRules.PodPickM);
            }
            HoverPod = pod;
            HoverLift = lift;
        }

        // ---------------------------------------------------------------- plan around this lift

        /// <summary>The region is empty and the press landed on a census station: the region becomes the lift's corridor plus the
        /// stretch below its bottom station (QuickRunsPlanRules.AroundRegion). True when the click was used. The lift itself is pinned by
        /// the next scan (CollectPinned takes every census lift with a chord near the region).</summary>
        private bool TryPlanAround(float2 hit)
        {
            LiftCensusSystem census = Census();
            if (census == null || !census.IsArmed || census.StationCount == 0)
                return false;
            int at = -1;
            float bd = QuickRunsPlanRules.StationPickM;
            for (int i = 0; i < census.StationCount; i++)
            {
                float d = math.distance(census.StationAt(i).Position.xz, hit);
                if (d <= bd)
                {
                    bd = d;
                    at = i;
                }
            }
            if (at < 0)
                return false;
            LiftCensus.Station st = census.StationAt(at);
            int n = census.CopyServeLifts(m_ServeLifts, m_ServeChords);
            int found = -1;
            for (int i = 0; i < n && found < 0; i++)
                if (m_ServeLifts[i].ChordCount > 0 && (m_ServeLifts[i].Base == st.Id || m_ServeLifts[i].Top == st.Id))
                    found = i;
            if (found < 0)
            {
                Say($"QuickRuns [around]: the station at ({st.Position.x:F0}, {st.Position.z:F0}) has no lift line yet — corner placed instead");
                return false;
            }
            LiftServedRule.Lift lift = m_ServeLifts[found];
            var line = new List<float2>();
            for (int c = 0; c < lift.ChordCount; c++)
            {
                LiftServedRule.Chord ch = m_ServeChords[lift.FirstChord + c];
                if (line.Count == 0 || math.distance(line[line.Count - 1], ch.A) > 1f)
                    line.Add(ch.A);
                line.Add(ch.B);
            }
            // Bottom first: the base station's side (the chord list normally runs base -> top; a reversed list is flipped)
            float2 baseXz = st.IsBase ? st.Position.xz : float2.zero;
            if (!st.IsBase)
                for (int i = 0; i < census.StationCount; i++)
                {
                    LiftCensus.Station other = census.StationAt(i);
                    if (other.Id == lift.Base)
                        baseXz = other.Position.xz;
                }
            if (math.lengthsq(baseXz) > 0f && math.distancesq(line[line.Count - 1], baseXz) < math.distancesq(line[0], baseXz))
                line.Reverse();
            float2[] region = QuickRunsPlanRules.AroundRegion(line, FallLine(line[0]), SkiRunAutoPlanner.V1MaxAreaM2);
            if (region == null)
            {
                Say("QuickRuns [around]: the lift line is degenerate — corner placed instead");
                return false;
            }
            m_Edit.Clear();
            for (int i = 0; i < region.Length; i++)
                m_Edit.Add(region[i]);
            m_AroundRevision = m_Edit.Revision;
            RegionChanged(string.Format(CultureInfo.InvariantCulture, "plan around the lift from ({0:F0}, {1:F0}) to ({2:F0}, {3:F0}): {4} corners",
                line[0].x, line[0].y, line[line.Count - 1].x, line[line.Count - 1].y, region.Length));
            return true;
        }

        /// <summary>Downhill direction at a point (central differences over 40 m); zero on flat ground (the caller falls back to the lift's own axis).</summary>
        private float2 FallLine(float2 p)
        {
            Game.Simulation.TerrainHeightData hd = m_TerrainSystem.GetHeightData();
            if (!hd.isCreated)
                return float2.zero;
            const float s = 20f;
            float hx = RunAreaEmit.Ground(ref hd, p + new float2(s, 0f)).y - RunAreaEmit.Ground(ref hd, p - new float2(s, 0f)).y;
            float hz = RunAreaEmit.Ground(ref hd, p + new float2(0f, s)).y - RunAreaEmit.Ground(ref hd, p - new float2(0f, s)).y;
            float2 down = new float2(-hx, -hz);
            return math.length(down) < 0.03f * 2f * s ? float2.zero : math.normalize(down);
        }
    }
}
