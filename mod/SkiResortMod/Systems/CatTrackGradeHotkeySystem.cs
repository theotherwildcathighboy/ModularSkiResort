// CatTrackGradeHotkeySystem — the grading tool's keys and its every-frame watch (T139c; build note
// docs/research/field/2026-10-20/grading-tool-build.md). The LiftLineHotkeySystem pattern (its header holds the action plumbing
// citations: an inactive tool never ticks, ToolSystem.cs:312-327; GetAction is null before RegisterKeyBindings, ModSetting.cs:289-292;
// mod actions start disabled, ProxyAction.cs:322-349):
//   ToggleCatTrackGradeTool (default Ctrl+Shift+K) → CatTrackGradeToolSystem.TryActivate("hotkey") / Deactivate, in a loaded city;
//   the Build key (BuildLiftLine, default Enter) is SHARED with the lift tool: LiftLineHotkeySystem keeps it live while either tool is
//   open (one writer of shouldBeEnabled, so the two systems never fight over it); this system reads its press while the grading tool
//   is active and calls RequestApply. One key for "build what the tool shows" in both tools.
//   Every frame it also calls the tool's TickWatch (the dev-tab probe's follow-up checks run while the tool is closed).
// UIUpdate (SystemOrder.cs:67): every frame, paused or not.
// LOGS: one ARMED / INERT line per load; one line when the toggle is not live 120 frames after the load (probable key conflict).

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
        private ProxyAction m_Toggle, m_Build;
        private bool m_Armed, m_Disabled, m_ErrorLogged;
        private int m_Probe = -1;

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
            m_Probe = -1;
            try
            {
                SkiResortSettings s = Mod.Settings;
                m_Toggle = s != null ? TryGet(s, SkiResortSettings.ActToggleCatTrackGrade) : null;
                m_Build = s != null ? TryGet(s, SkiResortSettings.ActBuildLiftLine) : null;
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
                Mod.Log.Info($"Grade [keys]: ARMED — {SkiResortSettings.ActToggleCatTrackGrade} toggles the grading tool, " +
                             $"{SkiResortSettings.ActBuildLiftLine} (shared with the lift tool) grades while it is open; rebind in Options ▸ Modular Ski Resort ▸ Key bindings");
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
