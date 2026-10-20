// CatTrackGradeTooltipSystem — the grading tool's mouse tooltip (T139c; build note docs/research/field/2026-10-20/grading-tool-build.md).
// The LiftLineTooltipSystem pattern, unchanged in its two load-bearing rules:
//   * The step hint is ALWAYS shown while the tool is open and names the current step (GradeInput.Prompt, tested in GradeBatch GR-15);
//     a refusal (CatTrackGradeToolSystem.Notice) shows as a warning line for a few seconds under it.
//   * Text is LocalizedString.Value(text), never a fixed id: the frontend's translate cache is keyed by the id alone, so a fixed id keeps
//     the FIRST text it was given (LiftLineTooltipSystem header, field test 2026-10-20 shot 11.44.28).
// Lines: prompt; notice; the plan (length, grade avg / max against the limit, cut / fill volume, the switchback shortfall), green
// when it can grade, amber otherwise; the guard reason (error color); a steep-bank note.
// One INFO line per prompt change. Mod.cs: UpdateAfter TempCostTooltipSystem at UITooltip (as LiftLineTooltipSystem). One "!!!" ERROR
// disables it until the next load.

using System;
using Game.UI.Localization;
using Game.UI.Tooltip;
using Game.UI.Widgets;

namespace SkiResortMod.Systems
{
    public sealed partial class CatTrackGradeTooltipSystem : TooltipSystemBase
    {
        private CatTrackGradeToolSystem m_Tool;
        private StringTooltip m_Prompt, m_Notice, m_Plan, m_Guard, m_Bank;
        private string m_LastPrompt, m_LastNotice, m_LastPlan, m_LastGuard, m_LastBank, m_LoggedPrompt;
        private bool m_Disabled;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Tool = World.GetOrCreateSystemManaged<CatTrackGradeToolSystem>();
            m_Prompt = new StringTooltip { path = "skiGradePrompt", color = TooltipColor.Info };
            m_Notice = new StringTooltip { path = "skiGradeNotice", color = TooltipColor.Warning };
            m_Plan = new StringTooltip { path = "skiGradePlan", color = TooltipColor.Info };
            m_Guard = new StringTooltip { path = "skiGradeGuard", color = TooltipColor.Error };
            m_Bank = new StringTooltip { path = "skiGradeBank", color = TooltipColor.Warning };
            Mod.Log.Info($"{nameof(CatTrackGradeTooltipSystem)} created");
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
                string prompt = GradeInput.Prompt(m_Tool.Step, m_Tool.Dragging, m_Tool.CanUndoGrade);
                if (!string.Equals(prompt, m_LoggedPrompt, StringComparison.Ordinal))
                {
                    m_LoggedPrompt = prompt;
                    Mod.Log.Info($"Grade [tooltip]: step {m_Tool.Step} — \"{prompt}\"");
                }
                Show(m_Prompt, ref m_LastPrompt, prompt);
                string notice = m_Tool.Notice;
                if (notice != null)
                    Show(m_Notice, ref m_LastNotice, notice);
                string plan = m_Tool.Dragging ? null : m_Tool.PlanLine;
                if (!string.IsNullOrEmpty(plan))
                {
                    m_Plan.color = m_Tool.PlanOk ? TooltipColor.Success : TooltipColor.Warning;
                    Show(m_Plan, ref m_LastPlan, plan);
                    string guard = m_Tool.GuardLine;
                    if (guard != null)
                        Show(m_Guard, ref m_LastGuard, guard);
                    string bank = m_Tool.BankLine;
                    if (bank != null)
                        Show(m_Bank, ref m_LastBank, bank);
                }
            }
            catch (Exception ex)
            {
                m_Disabled = true;
                Mod.Log.Error($"!!! {nameof(CatTrackGradeTooltipSystem)} EXCEPTION — grading tooltip disabled until the next game load:\n{ex}");
            }
        }

        private void Show(StringTooltip tip, ref string last, string text)
        {
            if (!string.Equals(text, last, StringComparison.Ordinal))
            {
                last = text;
                tip.value = LocalizedString.Value(text);   // header: never a fixed id (the frontend caches by id)
            }
            AddMouseTooltip(tip);
        }
    }
}
