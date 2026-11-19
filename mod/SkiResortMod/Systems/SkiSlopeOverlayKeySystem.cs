// T253: polls the slope overlay's toggle key (ToggleSlopeOverlay, default Ctrl+Shift+O) in UIUpdate and hands each press to
// SkiSlopeOverlaySystem.RequestToggle. Why a second system: ProxyAction.WasPressedThisFrame read false for every press when polled from
// the overlay's Rendering-phase OnUpdate (bridge test 2026-11-19, two key forms), while the mod's UIUpdate pollers (SkiGladeHotkeySystem,
// LiftLineHotkeySystem) saw the same bridge keys. The overlay system owns the action, its enabled state and the key-liveness probe.
// Logs (a dead key must be visible): one line when the poller first sees the armed action, one line per change of the action's live
// state (InputConflictResolution.cs:137-198 turns a mod action off while an enabled vanilla action shares its keys, at most
// MaxStateLines per load), and one line on the first press it sees.
// Engine notes: EngineNotes/SkiSlopeOverlaySystem.md

using Game;
using Game.Input;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiSlopeOverlayKeySystem : GameSystemBase
    {
        private const int MaxStateLines = 6;

        private SkiSlopeOverlaySystem m_Overlay;
        private ProxyAction m_Seen;
        private bool m_LastEnabled, m_PressLogged;
        private int m_StateLines;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Overlay = World.GetOrCreateSystemManaged<SkiSlopeOverlaySystem>();
        }

        protected override void OnUpdate()
        {
            ProxyAction a = m_Overlay.ToggleAction;
            if (a == null)
            {
                m_Seen = null;
                return;
            }
            if (!ReferenceEquals(a, m_Seen))
            {
                m_Seen = a;
                m_LastEnabled = a.enabled;
                m_PressLogged = false;
                m_StateLines = 0;
                Mod.Log.Info($"SlopeOverlay [keys]: polling {a.name} in UIUpdate, live {a.enabled}, shouldBeEnabled {a.shouldBeEnabled}");
            }
            else if (a.enabled != m_LastEnabled)
            {
                m_LastEnabled = a.enabled;
                if (m_StateLines++ < MaxStateLines)
                    Mod.Log.Info($"SlopeOverlay [keys]: {a.name} live {a.enabled} (shouldBeEnabled {a.shouldBeEnabled}" +
                                 (a.enabled ? ")" : "; a vanilla action with the same keys is enabled now)"));
            }
            bool pressed = a.WasPressedThisFrame();
            if (!m_PressLogged && (pressed || a.IsPressed()))
            {
                m_PressLogged = true;
                Mod.Log.Info($"SlopeOverlay [keys]: first press seen (WasPressedThisFrame {pressed})");
            }
            if (pressed)
                m_Overlay.RequestToggle("key");
        }
    }
}
