// T253: polls the slope overlay's toggle key (ToggleSlopeOverlay) in UIUpdate, like every other hotkey poller of the mod
// (SkiGladeHotkeySystem, LiftLineHotkeySystem), and hands each press to SkiSlopeOverlaySystem.RequestToggle. The overlay system owns
// the action, its enabled state and the key-liveness probe.
// Field note (bridge test 2026-11-19): the first default, Ctrl+Shift+O, never reached the game on the test machine. Another program
// holds it as a system-wide hotkey (RegisterHotKey fails with 1409), and Windows hands such a chord to that program, not to the
// focused window. The game's raw keyboard saw a plain O but never Ctrl+Shift+O. A mod cannot detect that from inside the game, so the
// log lines below make a dead key visible instead. Since then the action has no default key (the player binds one in Options).
// Logs: one line when the poller first sees the armed action (its live state and bindings), one line per change of the live state
// (InputConflictResolution.cs:137-198 turns a mod action off while an enabled vanilla action shares its keys, at most MaxStateLines per
// load), and one line on the first press.
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
                var sb = new System.Text.StringBuilder();
                foreach (ProxyBinding pb in a.bindings)
                    sb.Append(sb.Length > 0 ? "; " : "").Append(pb.ToString());
                Mod.Log.Info($"SlopeOverlay [keys]: polling {a.name} in UIUpdate, live {a.enabled}, shouldBeEnabled {a.shouldBeEnabled}, " +
                             $"set {a.isSet}, bindings [{sb}]");
            }
            else if (a.enabled != m_LastEnabled)
            {
                m_LastEnabled = a.enabled;
                if (m_StateLines++ < MaxStateLines)
                    Mod.Log.Info($"SlopeOverlay [keys]: {a.name} live {a.enabled} (shouldBeEnabled {a.shouldBeEnabled}" +
                                 (a.enabled ? ")" : "; a vanilla action with the same keys is enabled now)"));
            }
            bool pressed = a.WasPressedThisFrame();
            if (!m_PressLogged && pressed)
            {
                m_PressLogged = true;
                Mod.Log.Info($"SlopeOverlay [keys]: first press of {a.name} this load");
            }
            if (pressed)
                m_Overlay.RequestToggle("key");
        }
    }
}
