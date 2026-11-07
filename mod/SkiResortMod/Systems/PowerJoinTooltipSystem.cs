// Mouse tooltip for PowerJoinRefusalSystem. While the preview holds a refused vanilla HV join, one error line says why
// (SkiResort.PowerJoin.REFUSED).
// The vanilla tooltips show no text for our Error. It carries no ErrorType icon. The pattern is the same as CatTrackGradeTooltipSystem.
// In Mod.cs, the system runs UpdateAfter TempCostTooltipSystem at UITooltip. A "!!!" ERROR disables it until the next load.

using System;
using Game.UI.Localization;
using Game.UI.Tooltip;
using Game.UI.Widgets;

namespace SkiResortMod.Systems
{
    public sealed partial class PowerJoinTooltipSystem : TooltipSystemBase
    {
        public const string KeyRefused = "SkiResort.PowerJoin.REFUSED";

        private PowerJoinRefusalSystem m_Refusal;
        private StringTooltip m_Line;
        private bool m_Disabled;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Refusal = World.GetOrCreateSystemManaged<PowerJoinRefusalSystem>();
            m_Line = new StringTooltip
            {
                path = "skiPowerJoinRefused",
                value = LocalizedString.Id(KeyRefused),
                color = TooltipColor.Error,
            };
            Mod.Log.Info($"{nameof(PowerJoinTooltipSystem)} created");
        }

        protected override void OnUpdate()
        {
            if (m_Disabled || !m_Refusal.RefusingNow)
                return;
            try
            {
                AddMouseTooltip(m_Line);
            }
            catch (Exception ex)
            {
                m_Disabled = true;
                Mod.Log.Error($"!!! {nameof(PowerJoinTooltipSystem)} EXCEPTION — power-line refusal tooltip disabled until the next game load " +
                              $"(the refusal itself still works):\n{ex}");
            }
        }
    }
}
