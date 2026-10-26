// Cat track tool keys and the every-frame watch. Pattern of LiftLineHotkeySystem, whose engine notes (EngineNotes/LiftLineHotkeySystem.md) cover the action plumbing
// (an inactive tool never ticks, ToolSystem.cs:312-327; GetAction is null before RegisterKeyBindings, ModSetting.cs:289-292;
// mod actions start disabled, ProxyAction.cs:322-349).
// ToggleCatTrackGradeTool (default Ctrl+Shift+K) → CatTrackGradeToolSystem.TryActivate("hotkey") / Deactivate, in a loaded city.
// The Build key (BuildLiftLine, default Enter) is shared with the lift tool: LiftLineHotkeySystem keeps it live while either tool
// is open (one writer of shouldBeEnabled); this system reads the press while the grading tool is active and calls RequestApply.
// CatTrackDeleteMode (default Ctrl+Shift+D) is live only while the tool is open (this system is its one writer) and toggles its
// Delete mode (field test 2026-10-26b). It is enabled only while the tool is open, so its liveness probe runs then: once per load,
// ProbeDelayFrames after the first opening, a warning if the conflict resolver disabled it (InputConflictResolution.cs:138-198). A
// modifier binding conflicts with a plain vanilla key only when one side's composite disallows modifiers (ProxyBinding.cs:822-825);
// the field logs of 2026-10-26 / 26b show Ctrl+Shift+Q live beside the vanilla camera's Q.
// Every frame it also calls the tool's TickWatch (dev-tab probe follow-ups also run while closed). UIUpdate, SystemOrder.cs:67:
// every frame, paused or not. Logs: one ARMED / INERT line per load; one line if the toggle is not live 120 frames after the load.

using System;
using Colossal.Serialization.Entities;
using Game;
using Game.Input;
using Game.SceneFlow;
using SkiResortMod.Settings;

namespace SkiResortMod.Systems
{
    public sealed partial class CatTrackGradeHotkeySystem : GameSystemBase
    {
        private const int ProbeDelayFrames = 120;

        private CatTrackGradeToolSystem m_Tool;
        private ProxyAction m_Toggle, m_Build, m_DeleteMode;
        private bool m_Armed, m_Disabled, m_ErrorLogged;
        private int m_Probe = -1, m_DeleteProbe = -1;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Tool = World.GetOrCreateSystemManaged<CatTrackGradeToolSystem>();
            Mod.Log.Info($"{nameof(CatTrackGradeHotkeySystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = m_Disabled = m_ErrorLogged = false;
            m_Probe = m_DeleteProbe = -1;
            try
            {
                SkiResortSettings s = Mod.Settings;
                m_Toggle = s != null ? TryGet(s, SkiResortSettings.ActToggleCatTrackGrade) : null;
                m_Build = s != null ? TryGet(s, SkiResortSettings.ActBuildLiftLine) : null;
                m_DeleteMode = s != null ? TryGet(s, SkiResortSettings.ActCatTrackDeleteMode) : null;
                if (m_DeleteMode != null)
                    m_DeleteMode.shouldBeEnabled = false;
                bool inGame = mode.IsGame();
                if (m_Toggle != null)
                    m_Toggle.shouldBeEnabled = inGame;
                if (!inGame)
                    return;
                if (m_Toggle == null || m_Build == null)
                {
                    Mod.Log.Warn($"Grade [keys]: INERT — action(s) not registered ({(m_Toggle == null ? SkiResortSettings.ActToggleCatTrackGrade + " " : string.Empty)}" +
                                 $"{(m_Build == null ? SkiResortSettings.ActBuildLiftLine : string.Empty)}; RegisterKeyBindings missing or failed in Mod.OnLoad)");
                    return;
                }
                m_Armed = true;
                m_Probe = ProbeDelayFrames;
                Mod.Log.Info($"Grade [keys]: ARMED — {SkiResortSettings.ActToggleCatTrackGrade} toggles the cat track tool, " +
                             $"{SkiResortSettings.ActBuildLiftLine} (shared with the lift tool) grades and builds while it is open, " +
                             $"{SkiResortSettings.ActCatTrackDeleteMode} toggles its delete mode ({(m_DeleteMode != null ? "registered" : "NOT registered")}); rebind in Options ▸ Modular Ski Resort ▸ Key bindings");
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
                    Mod.Log.Warn($"Grade [keys]: {SkiResortSettings.ActToggleCatTrackGrade} is not live {ProbeDelayFrames} frames after the " +
                                 "load (probable key conflict with a vanilla action, InputConflictResolution.cs:138-198) — rebind it");
                if (m_Toggle.WasPressedThisFrame())
                {
                    if (active)
                        m_Tool.Deactivate();
                    else
                        m_Tool.TryActivate("hotkey");
                }
                if (active && m_Build.WasPressedThisFrame())
                    m_Tool.RequestApply();
                if (m_DeleteMode != null)
                {
                    if (m_DeleteMode.shouldBeEnabled != active)
                    {
                        m_DeleteMode.shouldBeEnabled = active;
                        if (active && m_DeleteProbe < 0)
                            m_DeleteProbe = ProbeDelayFrames;   // once per load (0 = done)
                    }
                    if (active && m_DeleteProbe > 0 && --m_DeleteProbe == 0 && !m_DeleteMode.enabled)
                        Mod.Log.Warn($"Grade [keys]: {SkiResortSettings.ActCatTrackDeleteMode} is not live {ProbeDelayFrames} frames after the tool " +
                                     "opened (probable key conflict with a vanilla action, InputConflictResolution.cs:138-198) — rebind it; the " +
                                     "hub's Delete cat tracks chip still works");
                    if (active && m_DeleteMode.WasPressedThisFrame())
                        m_Tool.ToggleDeleteMode("key");
                }
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
                Mod.Log.Error($"!!! {nameof(CatTrackGradeHotkeySystem)} EXCEPTION — grading keys disabled until the next game load:\n{e}");
            }
        }
    }
}
