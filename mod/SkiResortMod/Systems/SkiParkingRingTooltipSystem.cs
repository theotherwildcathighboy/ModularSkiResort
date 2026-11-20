// Mouse tooltip for the lodge parking ring (T263, 2026-11-20). While a road lodge is being placed, SkiAreaRingOverlaySystem draws a bright
// blue ring around the ghost (SkiParkingRules.RadiusM) and nothing said what it was. One line says it: only the car spaces inside the
// ring count for the lodge. Same pattern as SkiSnowLineTooltipSystem: one StringTooltip widget as a field, the LocalizedString rebuilt only
// when the radius text changes (never per frame). Mod.cs: UpdateAfter TempCostTooltipSystem at UITooltip. A "!!!" ERROR disables it until the
// next load. A selected lodge shows its ring without the line (no cursor to hang it on).

using System;
using Game.UI.Localization;
using Game.UI.Tooltip;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiParkingRingTooltipSystem : TooltipSystemBase
    {
        public const string KeyRing = "SkiResort.Tooltip.PARKING_RING";

        private SkiAreaRingOverlaySystem m_Rings;
        private StringTooltip m_Line;
        private bool m_Disabled, m_ErrorLogged;
        private int m_ShownUnits = -1;   // the unit options can change in a session

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Rings = World.GetOrCreateSystemManaged<SkiAreaRingOverlaySystem>();
            m_Line = new StringTooltip
            {
                path = "skiParkingRing",
                color = TooltipColor.Info,
            };
            Mod.Log.Info($"{nameof(SkiParkingRingTooltipSystem)} created");
        }

        protected override void OnUpdate()
        {
            if (m_Disabled || m_Rings.ParkingMode != SkiAreaRingOverlaySystem.ParkingRingMode.PlacingLodge)
                return;
            try
            {
                int units = DisplayState.Packed;
                if (units != m_ShownUnits)
                {
                    m_ShownUnits = units;
                    // m / yd by the game's unit setting, like the survey line's radius.
                    m_Line.value = LocalizedString.Id(KeyRing, ("RADIUS", DisplayText.Length(SkiParkingRules.RadiusM)));
                }
                AddMouseTooltip(m_Line);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiParkingRingTooltipSystem)} EXCEPTION — the parking ring tooltip is off until the next " +
                                  $"game load:\n{e}");
                }
            }
        }
    }
}
