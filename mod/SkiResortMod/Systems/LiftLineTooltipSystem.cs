// Mouse tooltip of the lift-line tool: the prompt for the current step, a refusal notice, the plan line (towers,
// member mix, spans, clearance) and, over the line, the hovered tower or span. The overlay cannot draw text, so
// span lengths live here.
//   * Text is LocalizedString.Value(text), never IdWithFallback(fixedId, text): the frontend's translate cache is
//     keyed by the id alone (Cities2_Data/Content/Game/UI/index.js `function dc()`), so a fixed id keeps the first
//     text it was given. Value() has a null id and renders the text itself (index.js `xc`).
//   * One INFO log line per prompt change, so a field log shows what the player was told.
// Mod.cs: UpdateAt UITooltip (the TooltipSystemBase phase). Tooltips draw only while the mouse is not over the UI
// (TooltipUISystem.OnUpdate: !mouseOverUI). One "!!!" ERROR disables it until the next load.

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
        private StringTooltip m_Prompt, m_Notice, m_Plan, m_Hover;
        private string m_LastPrompt, m_LastNotice, m_LastPlan, m_LastHover, m_LoggedPrompt;
        private bool m_Disabled;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Tool = World.GetOrCreateSystemManaged<LiftLineToolSystem>();
            m_Prompt = new StringTooltip { path = "skiLiftLinePrompt", color = TooltipColor.Info };
            m_Notice = new StringTooltip { path = "skiLiftLineNotice", color = TooltipColor.Warning };
            m_Plan = new StringTooltip { path = "skiLiftLinePlan", color = TooltipColor.Info };
            m_Hover = new StringTooltip { path = "skiLiftLineHover", color = TooltipColor.Info };
            Mod.Log.Info($"{nameof(LiftLineTooltipSystem)} created");
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
                LiftLineEdit e = m_Tool.Edit;
                string prompt = LiftLineInput.Prompt(m_Tool.Step, m_Tool.PendingTypeName, m_Tool.Dragging);
                if (m_Tool.DragOff)
                    prompt = "Release here: remove this tower · move back onto the line to keep it · right click: cancel";
                if (!string.Equals(prompt, m_LoggedPrompt, StringComparison.Ordinal))
                {
                    m_LoggedPrompt = prompt;
                    Mod.Log.Info($"LiftLine [tooltip]: step {m_Tool.Step} — \"{prompt}\"");
                }
                Show(m_Prompt, ref m_LastPrompt, prompt);
                string notice = m_Tool.Notice;
                if (notice != null)
                    Show(m_Notice, ref m_LastNotice, notice);
                if (e?.Plan != null)
                {
                    Show(m_Plan, ref m_LastPlan, e.Describe());
                    m_Plan.color = e.Plan.Clear ? TooltipColor.Success : TooltipColor.Warning;
                    string hover = HoverText(e);
                    if (hover != null)
                        Show(m_Hover, ref m_LastHover, hover);
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
                   " m above the ground · drag to move";
        }

        private void Show(StringTooltip tip, ref string last, string text)
        {
            if (!string.Equals(text, last, StringComparison.Ordinal))
            {
                last = text;
                tip.value = LocalizedString.Value(text);   // never a fixed id (the frontend caches by id)
            }
            AddMouseTooltip(tip);
        }

        private static string F(float v) => v.ToString("F0", CultureInfo.InvariantCulture);
        private static string F1(float v) => v.ToString("F1", CultureInfo.InvariantCulture);
    }
}
