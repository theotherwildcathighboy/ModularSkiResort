// Quick runs tool keys and the every-frame watch. The pattern is the same as CatTrackGradeHotkeySystem. EngineNotes/LiftLineHotkeySystem.md
// covers the action plumbing:
//   * An inactive tool never ticks (ToolSystem.cs:312-327).
//   * GetAction is null before RegisterKeyBindings (ModSetting.cs:289-292).
//   * Mod actions start disabled (ProxyAction.cs:322-349).
// ToggleQuickRunsTool (default Ctrl+Shift+Q) calls QuickRunsToolSystem.TryActivate("hotkey") or Deactivate, in a loaded city. It is inert
// while the Quick runs option is off (TryActivate refuses and logs).
// The Build key (BuildLiftLine, default Enter) is shared with the lift tool. LiftLineHotkeySystem keeps it live while any of our build
// tools is open (one writer of shouldBeEnabled). This system reads the press while the Quick runs tool is active and calls RequestEnter.
// Every frame, the system also calls the tool's TickWatch. The bind line after a build also runs while the tool is closed.
// The system runs in UIUpdate (SystemOrder.cs:67), every frame, paused or not.
// Logs: one ARMED / INERT line per load. One line if the toggle is not live 120 frames after the load.

using System;
using Colossal.Serialization.Entities;
using Game;
using Game.Input;
using Game.SceneFlow;
using SkiResortMod.Settings;

namespace SkiResortMod.Systems
{
    public sealed partial class QuickRunsHotkeySystem : GameSystemBase
    {
        private const int ProbeDelayFrames = 120;

        private QuickRunsToolSystem m_Tool;
        private ProxyAction m_Toggle, m_Build;
        private bool m_Armed, m_Disabled, m_ErrorLogged;
        private int m_Probe = -1;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Tool = World.GetOrCreateSystemManaged<QuickRunsToolSystem>();
            Mod.Log.Info($"{nameof(QuickRunsHotkeySystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = m_Disabled = m_ErrorLogged = false;
            m_Probe = -1;
            try
            {
                SkiResortSettings s = Mod.Settings;
                m_Toggle = s != null ? TryGet(s, SkiResortSettings.ActToggleQuickRuns) : null;
                m_Build = s != null ? TryGet(s, SkiResortSettings.ActBuildLiftLine) : null;
                bool inGame = mode.IsGame();
                if (m_Toggle != null)
                    m_Toggle.shouldBeEnabled = inGame;
                if (!inGame)
                    return;
                if (m_Toggle == null || m_Build == null)
                {
                    Mod.Log.Warn($"QuickRuns [keys]: INERT — action(s) not registered ({(m_Toggle == null ? SkiResortSettings.ActToggleQuickRuns + " " : string.Empty)}" +
                                 $"{(m_Build == null ? SkiResortSettings.ActBuildLiftLine : string.Empty)}; RegisterKeyBindings missing or failed in Mod.OnLoad)");
                    return;
                }
                m_Armed = true;
                m_Probe = ProbeDelayFrames;
                Mod.Log.Info($"QuickRuns [keys]: ARMED — {SkiResortSettings.ActToggleQuickRuns} toggles the Quick runs tool (inert while the option " +
                             $"is off), {SkiResortSettings.ActBuildLiftLine} (shared with the lift tool) scans, builds or plants while it is open; " +
                             "rebind in Options ▸ Modular Ski Resort ▸ Key bindings");
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        protected override void OnUpdate()
        {
            if (m_Disabled)
                return;
            try
            {
                m_Tool.TickWatch();
                if (!m_Armed)
                    return;
                GameManager gm = GameManager.instance;
                if (gm == null || gm.isGameLoading)
                    return;
                bool active = m_Tool.IsActive;
                if (m_Probe > 0 && --m_Probe == 0 && !m_Toggle.enabled)
                    Mod.Log.Warn($"QuickRuns [keys]: {SkiResortSettings.ActToggleQuickRuns} is not live {ProbeDelayFrames} frames after the " +
                                 "load (probable key conflict with a vanilla action, InputConflictResolution.cs:138-198) — rebind it");
                if (m_Toggle.WasPressedThisFrame())
                {
                    if (active)
                        m_Tool.Deactivate();
                    else
                        m_Tool.TryActivate("hotkey");
                }
                if (active && m_Build.WasPressedThisFrame())
                    m_Tool.RequestEnter();
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        private static ProxyAction TryGet(SkiResortSettings settings, string name)
        {
            try
            {
                return settings.GetAction(name);   // ModSetting.cs:289-292
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void Fail(Exception e)
        {
            m_Disabled = true;
            if (!m_ErrorLogged)
            {
                m_ErrorLogged = true;
                Mod.Log.Error($"!!! {nameof(QuickRunsHotkeySystem)} EXCEPTION — Quick runs keys disabled until the next game load:\n{e}");
            }
        }
    }
}
