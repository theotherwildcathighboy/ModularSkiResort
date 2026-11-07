// Cat track tool mouse tooltip. The pattern is the same as LiftLineTooltipSystem. In Mod.cs, the system runs UpdateAfter
// TempCostTooltipSystem at UITooltip.
// Every line is a LocaleEN key SkiResort.CatTrack.* (GradeMsg) with its whole-number {ARG} values. The lines are plain sentences for a
// player, with no ids, decimals or log syntax. The log keeps the detail. GradeBatch GR-23 gates the texts.
// The step hint always shows while the tool is open. A refusal (CatTrackGradeToolSystem.Notice) shows as a warning under it.
// A "!!!" ERROR disables the system until the next load.

using System;
using Game.UI.Localization;
using Game.UI.Tooltip;
using Game.UI.Widgets;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class CatTrackGradeTooltipSystem : TooltipSystemBase
    {
        private CatTrackGradeToolSystem m_Tool;
        private StringTooltip m_Prompt, m_Notice, m_Plan, m_Guard, m_Bank, m_Check;
        private string m_LastPrompt, m_LastNotice, m_LastPlan, m_LastGuard, m_LastBank, m_LastCheck, m_LoggedPrompt;
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
            m_Check = new StringTooltip { path = "skiGradeCheck", color = TooltipColor.Info };
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
                string promptKey = m_Tool.DeleteMode ? m_Tool.DeletePromptKey : GradeInput.PromptKey(m_Tool.Step, m_Tool.Dragging);
                if (!string.Equals(promptKey, m_LoggedPrompt, StringComparison.Ordinal))
                {
                    m_LoggedPrompt = promptKey;
                    Mod.Log.Info($"Grade [tooltip]: step {m_Tool.Step} — prompt {promptKey}");
                }
                Show(m_Prompt, ref m_LastPrompt, GradeMsg.Of(promptKey));
                GradeMsg notice = m_Tool.Notice;
                if (!notice.IsEmpty)
                    Show(m_Notice, ref m_LastNotice, notice);
                GradeMsg plan = m_Tool.Dragging || m_Tool.DeleteMode ? GradeMsg.Empty : m_Tool.PlanMsg;
                if (!plan.IsEmpty)
                {
                    m_Plan.color = m_Tool.PlanOk ? TooltipColor.Success : TooltipColor.Warning;
                    Show(m_Plan, ref m_LastPlan, plan);
                    GradeMsg check = m_Tool.CheckMsg;   // the guards of this plan are still being solved (DECISIONS 601 f)
                    if (!check.IsEmpty)
                        Show(m_Check, ref m_LastCheck, check);
                    GradeMsg guard = m_Tool.GuardMsg;
                    if (!guard.IsEmpty)
                        Show(m_Guard, ref m_LastGuard, guard);
                    GradeMsg bank = m_Tool.BankMsg;
                    if (!bank.IsEmpty)
                        Show(m_Bank, ref m_LastBank, bank);
                }
            }
            catch (Exception ex)
            {
                m_Disabled = true;
                Mod.Log.Error($"!!! {nameof(CatTrackGradeTooltipSystem)} EXCEPTION — grading tooltip disabled until the next game load:\n{ex}");
            }
        }

        private void Show(StringTooltip tip, ref string last, GradeMsg msg)
        {
            string sig = msg.Signature;
            if (!string.Equals(sig, last, StringComparison.Ordinal))
            {
                last = sig;
                tip.value = Text(msg);
            }
            AddMouseTooltip(tip);
        }

        /// <summary>The message as a LocalizedString: the key SkiResort.CatTrack.KEY with each {ARG} as a whole number.</summary>
        internal static LocalizedString Text(GradeMsg msg)
        {
            int n = msg.Args == null ? 0 : msg.Args.Length;
            var args = new (string, ILocElement)[n];
            for (int i = 0; i < n; i++)
                args[i] = (msg.Args[i].Name, new LocalizedNumber<int>((int)math.round(msg.Args[i].Value), "integer"));
            return LocalizedString.Id(GradeMsg.Prefix + msg.Key, args);
        }
    }
}
