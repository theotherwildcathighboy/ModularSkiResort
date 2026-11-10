// Quick runs tool mouse tooltip, the cat track tooltip's pattern. Mod.cs: UpdateAfter TempCostTooltipSystem at UITooltip. Fixed lines go by
// LocalizedString.Id (locale keys, root LocaleEN.cs "SkiResort.Tooltip.QUICK_*"). Numbers go in number widgets with a localized label (the
// glade tooltip's IntTooltip idiom). The two lines that mix several numbers into a sentence (mix, cluster) fill the active dictionary's
// template (the SkiDaySystem.ReachRowText precedent) and go by Value. The step prompt is always shown while the tool is open, then the
// build's or the planting's progress line. Refusals, notices and warnings follow under it. Resort Planner v2 (stage A, task A7): with a
// plan showing, the tooltip also shows the plan line, the terrain's honest mix, the lifts-are-proposals note and the site verdict's detail
// line (the v1 site line itself is unchanged). The lift under the cursor gets its own line (its way-home verdict, and how its line builds:
// flagged, or waiting in the lift tool), the lift group's ring too ("Runs from P2", the catch-all group is "Other runs"). The empty region
// says a click on a lift station plans around it. Round R2-b (runs-first plans): the run and mix counts are pistes only and the cat tracks
// have their own line (QuickRunsShown), the mix line drops "of {target}" when the planner gives no target, and the Lifts / Mix / Way home
// knobs are not mentioned anywhere. Zero allocation per frame except when a value changes. A "!!!" ERROR
// disables the system until the next load.
// Engine notes: EngineNotes/QuickRunsToolSystem.md (Tooltip)

using System;
using System.Collections.Generic;
using System.Globalization;
using Game.UI.Localization;
using Game.UI.Tooltip;
using Game.UI.Widgets;

namespace SkiResortMod.Systems
{
    public sealed partial class QuickRunsTooltipSystem : TooltipSystemBase
    {
        private const string P = QuickRunsRules.TipPrefix;
        private const int MaxWarnings = 8;
        private const string NoSignSomeKey = P + "WARN[NoSignSome]";   // "{N} of {M} runs": the one warning with numbers

        private QuickRunsToolSystem m_Tool;
        private SkiResortHubUISystem m_Hub;
        private StringTooltip m_Prompt, m_Notice, m_Status, m_Mix, m_Cluster, m_Progress, m_Site;
        private StringTooltip m_PlanLine, m_Honest, m_Proposal, m_SiteDetail, m_Around, m_HoverPlan, m_HoverVerdict, m_HoverFit;
        private ProgressTooltip m_Scan;
        private IntTooltip m_Region, m_RegionMax, m_Vertical, m_Runs, m_Tracks, m_Fee, m_Skiers, m_Plant, m_Remove, m_TreeCost, m_TreeLine;
        private readonly StringTooltip[] m_Warn = new StringTooltip[MaxWarnings];
        private readonly string[] m_LastWarn = new string[MaxWarnings];
        private string m_LastPrompt, m_LastNotice, m_LastStatus, m_LastMix, m_LastCluster, m_LastProgress, m_LastSite, m_LoggedPrompt;
        private string m_LastPlanLine, m_LastHonest, m_LastProposal, m_LastSiteDetail, m_LastAround, m_LastHoverPlan, m_LastHoverVerdict, m_LastHoverFit;
        private QuickRunsShown m_Shown;              // the counts of the published result (pistes and cat tracks apart), per hub revision
        private int m_ShownRevision = -1;
        private AutoPlanResult m_ShownFor;
        private bool m_Disabled;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Tool = World.GetOrCreateSystemManaged<QuickRunsToolSystem>();
            m_Hub = World.GetOrCreateSystemManaged<SkiResortHubUISystem>();
            m_Prompt = new StringTooltip { path = "skiQuickPrompt", color = TooltipColor.Info };
            m_Notice = new StringTooltip { path = "skiQuickNotice", color = TooltipColor.Warning };
            m_Status = new StringTooltip { path = "skiQuickStatus", color = TooltipColor.Error };
            m_Mix = new StringTooltip { path = "skiQuickMix", color = TooltipColor.Info };
            m_Cluster = new StringTooltip { path = "skiQuickCluster", color = TooltipColor.Info };
            m_Progress = new StringTooltip { path = "skiQuickProgress", color = TooltipColor.Info };
            m_Site = new StringTooltip { path = "skiQuickSite", color = TooltipColor.Info };
            m_PlanLine = new StringTooltip { path = "skiQuickPlanLine", color = TooltipColor.Info };
            m_Honest = new StringTooltip { path = "skiQuickHonest", color = TooltipColor.Info };
            m_Proposal = new StringTooltip { path = "skiQuickProposal", color = TooltipColor.Info };
            m_SiteDetail = new StringTooltip { path = "skiQuickSiteDetail", color = TooltipColor.Info };
            m_Around = new StringTooltip { path = "skiQuickAround", color = TooltipColor.Info };
            m_HoverPlan = new StringTooltip { path = "skiQuickHoverPlan", color = TooltipColor.Info };
            m_HoverVerdict = new StringTooltip { path = "skiQuickHoverVerdict", color = TooltipColor.Info };
            m_HoverFit = new StringTooltip { path = "skiQuickHoverFit", color = TooltipColor.Warning };
            // IntTooltip unit "area" like the game's area tool (AreaToolTooltipSystem.cs:78-83): the game formats m2 / km2 / acres itself. A
            // ProgressTooltip printed the raw "2658641.75 / 9000000 <area>" (its fraction formatter has no area entry).
            m_Region = new IntTooltip { path = "skiQuickRegion", label = LocalizedString.Id(P + "REGION"), unit = "area" };
            m_RegionMax = new IntTooltip { path = "skiQuickRegionMax", label = LocalizedString.Id(P + "REGION_MAX"), unit = "area" };
            m_Scan = new ProgressTooltip { path = "skiQuickScan", label = LocalizedString.Id(P + "SCANNING"), unit = "percentage", max = 100f };
            m_Vertical = new IntTooltip { path = "skiQuickVertical", label = LocalizedString.Id(P + "VERTICAL"), unit = "length" };
            m_Runs = new IntTooltip { path = "skiQuickRuns", label = LocalizedString.Id(P + "RUNS"), unit = "integer" };
            m_Tracks = new IntTooltip { path = "skiQuickTracks", label = LocalizedString.Id(P + "TRACKS"), unit = "integer" };
            m_Fee = new IntTooltip { path = "skiQuickFee", label = LocalizedString.Id(P + "FEE"), unit = "money" };
            m_Skiers = new IntTooltip { path = "skiQuickSkiers", label = LocalizedString.Id(P + "SKIERS"), unit = "integer" };
            m_Plant = new IntTooltip { path = "skiQuickPlant", label = LocalizedString.Id(P + "TREES_PLANT"), unit = "integer" };
            m_Remove = new IntTooltip { path = "skiQuickRemove", label = LocalizedString.Id(P + "TREES_REMOVE"), unit = "integer" };
            m_TreeCost = new IntTooltip { path = "skiQuickTreeCost", label = LocalizedString.Id(P + "TREES_COST"), unit = "money" };
            m_TreeLine = new IntTooltip { path = "skiQuickTreeLine", label = LocalizedString.Id(P + "TREE_LINE"), unit = "length" };
            for (int i = 0; i < MaxWarnings; i++)
                m_Warn[i] = new StringTooltip { path = "skiQuickWarn" + i, color = TooltipColor.Warning };
            Mod.Log.Info($"{nameof(QuickRunsTooltipSystem)} created");
        }

        protected override void OnUpdate()
        {
            if (m_Disabled || !m_Tool.IsActive)
            {
                m_LoggedPrompt = null;
                return;
            }
            try
            {
                string prompt = PromptKey();
                if (!string.Equals(prompt, m_LoggedPrompt, StringComparison.Ordinal))
                {
                    m_LoggedPrompt = prompt;
                    Mod.Log.Info($"QuickRuns [tooltip]: phase {m_Tool.Phase} — {prompt}");
                }
                Show(m_Prompt, ref m_LastPrompt, prompt);
                string notice = m_Tool.Notice;
                if (notice != null)
                    Show(m_Notice, ref m_LastNotice, notice);

                QuickRunsToolSystem.ToolPhase phase = m_Tool.Phase;
                QuickRegionReadout ro = m_Tool.Readout;
                if (phase == QuickRunsToolSystem.ToolPhase.Region && ro.State != QuickRegionState.Open)
                {
                    m_Region.value = (int)Math.Round(ro.AreaM2);
                    m_Region.color = ro.State == QuickRegionState.Over ? TooltipColor.Error
                        : ro.State == QuickRegionState.Near || ro.State == QuickRegionState.TooSmall ? TooltipColor.Warning : TooltipColor.Info;
                    AddMouseTooltip(m_Region);
                    m_RegionMax.value = (int)Math.Round(m_Tool.MaxAreaM2);
                    AddMouseTooltip(m_RegionMax);
                    m_Vertical.value = (int)Math.Round(ro.VerticalM);
                    AddMouseTooltip(m_Vertical);
                }
                // The hub's planner panel shows these lines. The cursor repeats them only with the hub closed
                bool detail = !m_Hub.IsOpen;
                // The site verdict: the corner estimate in the Region step, the scan's figure after it
                if (detail && ((phase == QuickRunsToolSystem.ToolPhase.Region && ro.State != QuickRegionState.Open) || m_Tool.HasPreview))
                    ShowSite(m_Tool.SiteGrade);
                ResortPlan plan = m_Tool.Plan;
                if (detail && plan != null)
                    ShowPlan(plan);
                if (detail && m_Tool.CanPlanAround)
                    Show(m_Around, ref m_LastAround, P + "AROUND_LIFT");
                // "Building runs 4 / 11", "Planting trees 2,400 / 9,000" (templates, the mix line's idiom)
                if (m_Tool.Building)
                    ShowValue(m_Progress, ref m_LastProgress, Template(P + "BUILDING_PROGRESS")
                        .Replace("{N}", m_Tool.BuildDone.ToString("N0", CultureInfo.InvariantCulture))
                        .Replace("{M}", m_Tool.BuildTotal.ToString("N0", CultureInfo.InvariantCulture)));
                else if (m_Tool.Planting)
                    ShowValue(m_Progress, ref m_LastProgress, Template(P + "PLANTING_PROGRESS")
                        .Replace("{N}", m_Tool.PlantDone.ToString("N0", CultureInfo.InvariantCulture))
                        .Replace("{M}", m_Tool.PlantTotal.ToString("N0", CultureInfo.InvariantCulture)));
                else if (m_Tool.RemovingTrees)
                    ShowValue(m_Progress, ref m_LastProgress, Template(P + "REMOVING_PROGRESS")
                        .Replace("{N}", m_Tool.RemoveDone.ToString("N0", CultureInfo.InvariantCulture))
                        .Replace("{M}", m_Tool.RemoveTotal.ToString("N0", CultureInfo.InvariantCulture)));
                if (phase == QuickRunsToolSystem.ToolPhase.Scanning)
                {
                    m_Scan.value = (float)Math.Round(m_Tool.ScanProgress * 100f);
                    AddMouseTooltip(m_Scan);
                }
                string status = m_Tool.StatusKey;
                if (detail && status != null && phase == QuickRunsToolSystem.ToolPhase.Region)
                    Show(m_Status, ref m_LastStatus, status);
                if (detail && m_Tool.HasPreview)
                    ShowTotals();
                if (detail && m_Tool.TreesOn)
                    ShowTrees();
                IReadOnlyList<string> warnings = m_Tool.WarningKeys;
                for (int i = 0; detail && i < warnings.Count && i < MaxWarnings; i++)
                {
                    if (warnings[i] == NoSignSomeKey)
                        ShowValue(m_Warn[i], ref m_LastWarn[i], Template(NoSignSomeKey).Replace("{N}", m_Tool.UnboundPistes.ToString())
                            .Replace("{M}", Shown().PistesOn.ToString()));
                    else
                        Show(m_Warn[i], ref m_LastWarn[i], warnings[i]);
                }
                string clusterKey = m_Tool.HoverClusterKey;
                if (plan != null && (m_Tool.HoverPod >= 0 || m_Tool.HoverLift >= 0))
                    ShowHoverPlan(plan);
                else if (clusterKey != null)
                {
                    string text = Template(clusterKey).Replace("{N}", m_Tool.HoverClusterRuns.ToString());
                    ShowValue(m_Cluster, ref m_LastCluster, text);
                }
            }
            catch (Exception ex)
            {
                m_Disabled = true;
                Mod.Log.Error($"!!! {nameof(QuickRunsTooltipSystem)} EXCEPTION — Quick runs tooltip disabled until the next game load:\n{ex}");
            }
        }

        /// <summary>The step prompt key by phase (root LocaleEN QUICK_PROMPT[*]).</summary>
        private string PromptKey()
        {
            if (m_Tool.Dragging)
                return P + "PROMPT[Drag]";
            if (m_Tool.Planting)
                return P + "PROMPT[Planting]";
            if (m_Tool.TreesOn && (m_Tool.Phase == QuickRunsToolSystem.ToolPhase.Region || m_Tool.Phase == QuickRunsToolSystem.ToolPhase.Preview))
                return P + "PROMPT[Trees]";
            switch (m_Tool.Phase)
            {
                case QuickRunsToolSystem.ToolPhase.Scanning:
                    return P + "PROMPT[Scanning]";
                case QuickRunsToolSystem.ToolPhase.Preview:
                    return P + "PROMPT[Preview]";
                case QuickRunsToolSystem.ToolPhase.Emitted:
                case QuickRunsToolSystem.ToolPhase.Retry:
                case QuickRunsToolSystem.ToolPhase.Applying:
                    return P + "PROMPT[Building]";
                default:
                    if (m_Tool.CanUndoBatch && m_Tool.Corners >= 3)
                        return P + "PROMPT[Built]";
                    return m_Tool.Corners == 0 ? P + "PROMPT[Start]" : m_Tool.Corners < 3 ? P + "PROMPT[Corners]" : !m_Tool.RegionClosed ? P + "PROMPT[Close]" : m_Tool.Readout.State == QuickRegionState.Over ? P + "PROMPT[Over]" : P + "PROMPT[Ready]";
            }
        }

        private void ShowTotals()
        {
            QuickTotals t = m_Tool.Totals;
            QuickRunsShown sh = Shown();
            m_Runs.value = sh.PistesOn;
            AddMouseTooltip(m_Runs);
            if (sh.Tracks > 0)
            {
                m_Tracks.value = sh.TracksOn;
                AddMouseTooltip(m_Tracks);
            }
            // The runs-first planner gives no target mix (all zeros): the line then shows the counts alone
            bool targets = t.Target[1] + t.Target[2] + t.Target[3] + t.Target[4] > 0;
            string mix = Template(P + (targets ? "MIX" : "MIX_COUNTS"))
                .Replace("{G}", sh.Count[1].ToString()).Replace("{B}", sh.Count[2].ToString())
                .Replace("{K}", sh.Count[3].ToString()).Replace("{D}", sh.Count[4].ToString())
                .Replace("{TG}", t.Target[1].ToString()).Replace("{TB}", t.Target[2].ToString())
                .Replace("{TK}", t.Target[3].ToString()).Replace("{TD}", t.Target[4].ToString());
            m_Mix.color = sh.Variety ? TooltipColor.Success : TooltipColor.Info;
            ShowValue(m_Mix, ref m_LastMix, mix);
            m_Fee.value = (int)Math.Min(t.Fee, int.MaxValue);
            AddMouseTooltip(m_Fee);
            m_Skiers.value = (int)Math.Round(t.SkiersPerDay);
            AddMouseTooltip(m_Skiers);
        }

        /// <summary>The plan line, the honest mix, the proposal note and the site detail line (a plan with pods only).</summary>
        private void ShowPlan(ResortPlan plan)
        {
            int proposed = 0;
            for (int i = 0; i < plan.Lifts.Length; i++)
                if (plan.Lifts[i].Origin == LiftOrigin.Proposed)
                    proposed++;
            ShowValue(m_PlanLine, ref m_LastPlanLine, Template(P + "PLAN_LINE")
                .Replace("{LIFTS}", proposed.ToString(CultureInfo.InvariantCulture))
                .Replace("{KM}", plan.RunKm.ToString("F1", CultureInfo.InvariantCulture))
                .Replace("{TKM}", Shown().TrackKm.ToString("F1", CultureInfo.InvariantCulture)));
            float[] mix = plan.MountainMix;
            if (mix[1] + mix[2] + mix[3] + mix[4] > 0f)
            {
                QuickRunsPlanRules.MixPercents(mix, out int mixG, out int mixB, out int mixK);   // the hub prints the same three numbers
                ShowValue(m_Honest, ref m_LastHonest, Template(P + "MIX_HONEST")
                    .Replace("{G}", mixG.ToString(CultureInfo.InvariantCulture))
                    .Replace("{B}", mixB.ToString(CultureInfo.InvariantCulture))
                    .Replace("{K}", mixK.ToString(CultureInfo.InvariantCulture)));
            }
            if (proposed > 0)
                Show(m_Proposal, ref m_LastProposal, P + "PROPOSAL_NOTE");
            if (plan.Site.Pods > 0)
                ShowValue(m_SiteDetail, ref m_LastSiteDetail, Template(P + "SITE_DETAIL")
                    .Replace("{G}", plan.Site.PodsWithGreenHome.ToString(CultureInfo.InvariantCulture))
                    .Replace("{PODS}", plan.Site.Pods.ToString(CultureInfo.InvariantCulture))
                    .Replace("{HA}", plan.Site.LargestBenchHa.ToString("F1", CultureInfo.InvariantCulture)));
        }

        /// <summary>The pod or lift under the cursor: "Pod B: Beginner, 4 run(s), Green way home" / "P1: Detachable, 1.3 km, 540 m up, reaches 7 run(s)"
        /// (then the lift's way-home verdict).</summary>
        private void ShowHoverPlan(ResortPlan plan)
        {
            int lift = m_Tool.HoverLift, pod = m_Tool.HoverPod;
            if (lift >= 0 && lift < plan.Lifts.Length)
            {
                PlanLift l = plan.Lifts[lift];
                string id = l.Origin == LiftOrigin.Pinned
                    ? Template(P + "LIFT_ID_PINNED")
                    : "P" + QuickRunsPlanRules.Ordinal(plan, lift).ToString(CultureInfo.InvariantCulture);
                string type = l.TypeIndex < SkiCatalog.Lifts.Count ? Template("SkiResort.Hub.LIFT_TYPE[" + SkiCatalog.Lifts[l.TypeIndex].Id + "]") : string.Empty;
                ShowValue(m_HoverPlan, ref m_LastHoverPlan, Template(P + "LIFT_LINE")
                    .Replace("{ID}", id).Replace("{TYPE}", type)
                    .Replace("{LENGTH}", (l.LengthM / 1000f).ToString("F1", CultureInfo.InvariantCulture))
                    .Replace("{RISE}", Math.Round(l.RiseM).ToString(CultureInfo.InvariantCulture))
                    .Replace("{RUNS}", l.Serves.ToString(CultureInfo.InvariantCulture)));
                if (l.Home != HomeVerdict.None)
                    Show(m_HoverVerdict, ref m_LastHoverVerdict, P + "VERDICT[" + l.Home + "]");
                // How the line builds: one tower over its limit (the queue builds it and flags the row), or it waits for the player
                if (l.BuildsFlagged)
                    Show(m_HoverFit, ref m_LastHoverFit, P + "LIFT_FIT[Flagged]");
                else if (l.HandFit)
                    Show(m_HoverFit, ref m_LastHoverFit, P + "LIFT_FIT[Waits]");
                return;
            }
            if (pod < 0 || pod >= plan.Pods.Length)
                return;
            PlanPod p = plan.Pods[pod];
            QuickRunsShown sh = Shown();
            string runs = (pod < sh.PodPistes.Length ? sh.PodPistes[pod] : 0).ToString(CultureInfo.InvariantCulture);
            string tracks = (pod < sh.PodTracks.Length ? sh.PodTracks[pod] : 0).ToString(CultureInfo.InvariantCulture);
            string line;
            if (p.Lift >= 0 && p.Lift < plan.Lifts.Length)
            {
                // A lift's group: "Runs from P2: Intermediate, 5 run(s), 3 cat track(s), Green way home"
                PlanLift l = plan.Lifts[p.Lift];
                string id = l.Origin == LiftOrigin.Pinned ? Template(P + "LIFT_ID_PINNED") : "P" + QuickRunsPlanRules.Ordinal(plan, p.Lift).ToString(CultureInfo.InvariantCulture);
                line = Template(P + "POD_LINE")
                    .Replace("{ID}", id).Replace("{CLASS}", Template(P + "CLASS[" + p.PrimaryClass + "]"))
                    .Replace("{RUNS}", runs).Replace("{TRACKS}", tracks)
                    .Replace("{VERDICT}", l.Home != HomeVerdict.None ? Template(P + "VERDICT[" + l.Home + "]") : string.Empty);
            }
            else
                line = Template(P + "OTHER_LINE").Replace("{RUNS}", runs).Replace("{TRACKS}", tracks);
            ShowValue(m_HoverPlan, ref m_LastHoverPlan, line.TrimEnd(' ', ','));
        }

        /// <summary>The counts of the published result with the pistes and the cat tracks apart (the hub's too), rebuilt when the hub
        /// revision or the result moves.</summary>
        private QuickRunsShown Shown()
        {
            if (m_Shown == null || m_ShownRevision != m_Tool.HubRevision || !ReferenceEquals(m_ShownFor, m_Tool.Result))
            {
                m_Shown = QuickRunsShown.Of(m_Tool);
                m_ShownRevision = m_Tool.HubRevision;
                m_ShownFor = m_Tool.Result;
            }
            return m_Shown;
        }

        private void ShowTrees()
        {
            m_Plant.value = m_Tool.TreeToPlant;
            AddMouseTooltip(m_Plant);
            if (m_Tool.TreeRemove > 0)
            {
                m_Remove.value = m_Tool.TreeRemove;
                AddMouseTooltip(m_Remove);
            }
            // What Plant will cost (the fee line's money widget): before Plant only, from the ready preview's Temp cost
            int cost = m_Tool.TreeCost;
            if (cost > 0 && !m_Tool.Planting && !m_Tool.RemovingTrees)
            {
                m_TreeCost.value = cost;
                AddMouseTooltip(m_TreeCost);
            }
            float line = m_Tool.TreeLine;
            if (!float.IsInfinity(line) && line < m_Tool.GroundMax + QuickRunsRules.TreeLineStepM)
            {
                m_TreeLine.value = (int)Math.Round(line);
                AddMouseTooltip(m_TreeLine);
            }
        }

        /// <summary>The active dictionary's text for a template key (the SkiDaySystem.ReachRowText precedent). The key itself when
        /// absent.</summary>
        /// <summary>One line: "Steep slope (average 33 %): ..." (QUICK_SITE[*] with the whole percent). Nothing while the grade is
        /// unknown.</summary>
        private void ShowSite(float meanGrade)
        {
            int pct = QuickRunsRules.SitePercent(meanGrade);
            string key = QuickRunsRules.SiteKey(QuickRunsRules.SiteTierOf(pct));
            if (key != null)
                ShowValue(m_Site, ref m_LastSite, Template(key).Replace("{N}", pct.ToString(CultureInfo.InvariantCulture)));
        }

        private static string Template(string key)
        {
            var dictionary = Game.SceneFlow.GameManager.instance?.localizationManager?.activeDictionary;
            return dictionary != null && dictionary.TryGetValue(key, out string text) && !string.IsNullOrEmpty(text) ? text : key;
        }

        private void Show(StringTooltip tip, ref string last, string text)
        {
            if (!string.Equals(text, last, StringComparison.Ordinal))
            {
                last = text;
                tip.value = text.StartsWith("SkiResort.", StringComparison.Ordinal) ? LocalizedString.Id(text) : LocalizedString.Value(text);
            }
            AddMouseTooltip(tip);
        }

        private void ShowValue(StringTooltip tip, ref string last, string text)
        {
            if (!string.Equals(text, last, StringComparison.Ordinal))
            {
                last = text;
                tip.value = LocalizedString.Value(text);
            }
            AddMouseTooltip(tip);
        }
    }
}
