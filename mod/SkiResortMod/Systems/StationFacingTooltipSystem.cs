// Mouse tooltip for StationFacingRefusalSystem. While a station preview is refused for facing away from the line end it snaps to,
// one error line says why (SkiResort.StationFacing.REFUSED). The vanilla tooltips show no text for our Error (it carries no ErrorType icon).
// Same pattern as PowerJoinTooltipSystem. Mod.cs: UpdateAfter TempCostTooltipSystem at UITooltip. A "!!!" ERROR disables it until the
// next load.

using System;
using Game.UI.Localization;
using Game.UI.Tooltip;
using Game.UI.Widgets;

namespace SkiResortMod.Systems
{
    public sealed partial class StationFacingTooltipSystem : TooltipSystemBase
    {
        public const string KeyRefused = "SkiResort.StationFacing.REFUSED";

        private StationFacingRefusalSystem m_Refusal;
        private StringTooltip m_Line;
        private bool m_Disabled;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Refusal = World.GetOrCreateSystemManaged<StationFacingRefusalSystem>();
            m_Line = new StringTooltip
            {
                path = "skiStationFacingRefused",
                value = LocalizedString.Id(KeyRefused),
                color = TooltipColor.Error,
            };
            Mod.Log.Info($"{nameof(StationFacingTooltipSystem)} created");
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
                Mod.Log.Error($"!!! {nameof(StationFacingTooltipSystem)} EXCEPTION — reversed-station tooltip disabled until the next game load " +
                              $"(the refusal itself still works):\n{ex}");
            }
        }
    }
}
