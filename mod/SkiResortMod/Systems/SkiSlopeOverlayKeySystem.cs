// T253: polls the slope overlay's toggle key (ToggleSlopeOverlay, default Ctrl+Shift+O) in UIUpdate and hands each press to
// SkiSlopeOverlaySystem.RequestToggle. Why a second system: ProxyAction.WasPressedThisFrame read false for every press when polled from
// the overlay's Rendering-phase OnUpdate (bridge test 2026-11-19, two key forms), while the mod's UIUpdate pollers (SkiGladeHotkeySystem,
// LiftLineHotkeySystem) saw the same bridge keys. The overlay system owns the action, its enabled state and the key-liveness probe.
// Engine notes: EngineNotes/SkiSlopeOverlaySystem.md

using Game;
using Game.Input;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiSlopeOverlayKeySystem : GameSystemBase
    {
        private SkiSlopeOverlaySystem m_Overlay;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Overlay = World.GetOrCreateSystemManaged<SkiSlopeOverlaySystem>();
        }

        protected override void OnUpdate()
        {
            ProxyAction a = m_Overlay.ToggleAction;
            if (a != null && a.WasPressedThisFrame())
                m_Overlay.RequestToggle("key");
        }
    }
}
