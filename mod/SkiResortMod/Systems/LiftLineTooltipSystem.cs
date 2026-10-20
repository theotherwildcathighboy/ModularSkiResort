// LiftLineTooltipSystem — the lift-line tool's mouse tooltip (T48 round 2; build note docs/research/field/2026-10-20/
// t48-round2-build.md): the prompt for the current step, the plan line (towers, member mix, spans, clearance) and, over the line,
// the hovered tower or span with its length and lowest carrier. The overlay cannot draw text, so span lengths live here.
// Text uses LocalizedString.IdWithFallback (Game.UI.Localization/LocalizedString.cs:66): English fallback values until the locale
// files carry the keys (SkiResort.LiftLine.*). Mod.cs: UpdateAt UITooltip (the TooltipSystemBase phase, as SkiLiftTooltipSystem).
// No logs per frame; one "!!!" ERROR disables it until the next load.

using System;
using System.Globalization;
using Game.UI.Localization;
using Game.UI.Tooltip;
using Game.UI.Widgets;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class LiftLineTooltipSystem : TooltipSystemBase
    {
        private LiftLineToolSystem m_Tool;
        private StringTooltip m_Prompt, m_Plan, m_Hover;
        private string m_LastPrompt, m_LastPlan, m_LastHover;
        private bool m_Disabled;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Tool = World.GetOrCreateSystemManaged<LiftLineToolSystem>();
            m_Prompt = new StringTooltip { path = "skiLiftLinePrompt", color = TooltipColor.Info };
            m_Plan = new StringTooltip { path = "skiLiftLinePlan", color = TooltipColor.Info };
            m_Hover = new StringTooltip { path = "skiLiftLineHover", color = TooltipColor.Info };
            Mod.Log.Info($"{nameof(LiftLineTooltipSystem)} created");
        }

        protected override void OnUpdate()
        {
            if (m_Disabled || !m_Tool.IsActive)
                return;
            try
            {
                LiftLineEdit e = m_Tool.Edit;
                string prompt;
                switch (m_Tool.State)
                {
                    case LiftLineToolSystem.ToolState.PickBase: prompt = "Lift line: click a lift station"; break;
                    case LiftLineToolSystem.ToolState.PickTop: prompt = "Lift line (" + m_Tool.PendingTypeName + "): click the other station"; break;
                    case LiftLineToolSystem.ToolState.Edit: prompt = "Click: add tower · drag: move · right click: remove · Enter: build · Esc: drop"; break;
                    default: prompt = "Building…"; break;
                }
                Show(m_Prompt, ref m_LastPrompt, "SkiResort.LiftLine.PROMPT", prompt);
                if (e?.Plan != null)
                {
                    Show(m_Plan, ref m_LastPlan, "SkiResort.LiftLine.PLAN", e.Describe());
                    m_Plan.color = e.Plan.Clear ? TooltipColor.Success : TooltipColor.Warning;
                    string hover = HoverText(e);
                    if (hover != null)
                        Show(m_Hover, ref m_LastHover, "SkiResort.LiftLine.HOVER", hover);
                }
            }
            catch (Exception ex)
            {
                m_Disabled = true;
                Mod.Log.Error($"!!! {nameof(LiftLineTooltipSystem)} EXCEPTION — lift-line tooltip disabled until the next game load:\n{ex}");
            }
        }

        private string HoverText(LiftLineEdit e)
        {
            LiftTowerPlan plan = e.Plan;
            int t = m_Tool.HoverTower;
            if (t <= 0 || t >= plan.Nodes.Length - 1)
                return null;
            LiftPlanNode n = plan.Nodes[t];
            LiftPlanSpan left = plan.Spans[t - 1], right = plan.Spans[t];
            return plan.Constants.Ladder[n.Member].Name.Replace("SkiLiftTower", string.Empty).Replace("SkiTramTower", "T") +
                   " at " + F(n.AlongM) + " m" + (e.IsPinned(n.AlongM) ? " (pinned)" : string.Empty) + "; spans " + F(left.LengthM) +
                   " / " + F(right.LengthM) + " m; lowest carrier " + F1(math.min(left.MinChairAboveGroundM, right.MinChairAboveGroundM)) +
                   " m above the ground";
        }

        private void Show(StringTooltip tip, ref string last, string id, string text)
        {
            if (!string.Equals(text, last, StringComparison.Ordinal))
            {
                last = text;
                tip.value = LocalizedString.IdWithFallback(id, text);
            }
            AddMouseTooltip(tip);
        }

        private static string F(float v) => v.ToString("F0", CultureInfo.InvariantCulture);
        private static string F1(float v) => v.ToString("F1", CultureInfo.InvariantCulture);
    }
}
