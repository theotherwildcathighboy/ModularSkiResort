// Mouse tooltip of the lift-line tool: the prompt for the current step, a refusal notice, the plan line (tower count, length and
// what is wrong, in words) and, over a tower, its height and clearance. The overlay cannot draw text, so the height lives here.
// Player text is a LocaleEN SkiResort.LiftTool.* key with {ARG} values (LocalizedString.Id(key, args), like SkiRunTooltipSystem);
// never IdWithFallback(fixedId, text): the frontend's translate cache is keyed by the id alone (Cities2_Data/Content/Game/UI/index.js
// `function dc()`), so a fixed id with changing text keeps the first text. The log keeps the full detail (LiftLineEdit.Describe).
// One INFO log line per prompt change, so a field log shows the prompt.
// Mod.cs: UpdateAt UITooltip (the TooltipSystemBase phase). Tooltips draw only while the mouse is not over the UI
// (TooltipUISystem.OnUpdate: !mouseOverUI). One "!!!" ERROR disables it until the next load.

using System;
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
        private LocalizedString m_LastPrompt, m_LastNotice, m_LastPlan, m_LastHover;
        private string m_LoggedPrompt;
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
                string key = LiftLineInput.PromptKey(m_Tool.Step, m_Tool.PendingTypeName, m_Tool.Dragging, m_Tool.DragOff);
                if (!string.Equals(key, m_LoggedPrompt, StringComparison.Ordinal))
                {
                    m_LoggedPrompt = key;
                    Mod.Log.Info($"LiftLine [tooltip]: step {m_Tool.Step}, prompt {key}");
                }
                Show(m_Prompt, ref m_LastPrompt, LiftLineToolSystem.T(key, ("TYPE", TypeText(m_Tool.PendingTypeName))));
                LocalizedString? notice = m_Tool.Notice;
                if (notice.HasValue)
                    Show(m_Notice, ref m_LastNotice, notice.Value);
                if (e?.Plan != null)
                {
                    Show(m_Plan, ref m_LastPlan, PlanText(e.Plan, out bool clear));
                    m_Plan.color = clear ? TooltipColor.Success : TooltipColor.Warning;
                    LocalizedString? hover = HoverText(e);
                    if (hover.HasValue)
                        Show(m_Hover, ref m_LastHover, hover.Value);
                }
            }
            catch (Exception ex)
            {
                m_Disabled = true;
                Mod.Log.Error($"!!! {nameof(LiftLineTooltipSystem)} EXCEPTION — lift-line tooltip disabled until the next game load:\n{ex}");
            }
        }

        /// <summary>The lift type's name for {TYPE} (TYPE[id]; TYPE[any] while no type is known).</summary>
        private static LocalizedString TypeText(string typeId) =>
            LocalizedString.Id(LiftLineToolSystem.LocPrefix + "TYPE[" + (LiftLineInput.KnownType(typeId) ? typeId : "any") + "]");

        /// <summary>The plan line: tower count and length, and the first thing wrong in words (the overlay shows where): a span below
        /// the floor (red), an edge too steep for the engine (red), a span above the max rope height (purple).</summary>
        internal static LocalizedString PlanText(LiftTowerPlan plan, out bool clear)
        {
            string key = LiftLineInput.PlanKey(plan, out float length);
            clear = key == "PLAN_CLEAR";
            return LiftLineToolSystem.T(key, ("TOWERS", LiftLineToolSystem.N(plan.LineTowers)), ("LENGTH", LiftLineToolSystem.N(length)));
        }

        /// <summary>Over a tower: its number, its height (the ladder member's tower top above its base) and the lowest chair or cabin
        /// over the ground in its two spans.</summary>
        private LocalizedString? HoverText(LiftLineEdit e)
        {
            LiftTowerPlan plan = e.Plan;
            int t = m_Tool.HoverTower;
            if (t <= 0 || t >= plan.Nodes.Length - 1)
                return null;
            string key = LiftLineInput.TowerKey(plan, t, e.IsPinned(plan.Nodes[t].AlongM), out float height, out float clear);
            return LiftLineToolSystem.T(key, ("N", LiftLineToolSystem.N(t)), ("COUNT", LiftLineToolSystem.N(plan.LineTowers)),
                ("HEIGHT", LiftLineToolSystem.N(height)), ("CLEAR", LiftLineToolSystem.N(clear)));
        }

        private void Show(StringTooltip tip, ref LocalizedString last, LocalizedString text)
        {
            if (!text.Equals(last))
            {
                last = text;
                tip.value = text;
            }
            AddMouseTooltip(tip);
        }
    }
}
