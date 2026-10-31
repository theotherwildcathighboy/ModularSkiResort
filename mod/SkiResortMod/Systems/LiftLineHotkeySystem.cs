// LiftLineHotkeySystem - the lift-line tool's two keys:
//   ToggleLiftLineTool (default Ctrl+Shift+L) -> LiftLineToolSystem.TryActivate("hotkey") / Deactivate, in a loaded city;
//   BuildLiftLine (default Enter) -> LiftLineToolSystem.RequestBuild(), live only while the tool is active.
// This system is the one writer of shouldBeEnabled for the Build key (live while the lift tool, the grading tool or the Quick runs
// tool is open; two writers would fight every frame). CatTrackGradeHotkeySystem and QuickRunsHotkeySystem read the press for theirs.
// The hotkey opens the tool with no lift type (the first station or loose end sets it); the hub's line tiles open it with
// their type (SkiResortHubUISystem.ApplyLaunch). Also tells the tool when a lift station is on the object tool (it never ticks then).
// Engine notes: EngineNotes/LiftLineHotkeySystem.md

using System;
using Colossal.Serialization.Entities;
using Game;
using Game.Input;
using Game.SceneFlow;
using Game.Tools;
using SkiResortMod.Settings;

namespace SkiResortMod.Systems
{
    public sealed partial class LiftLineHotkeySystem : GameSystemBase
    {
        private const int ProbeDelayFrames = 120;

        private LiftLineToolSystem m_Tool;
        private CatTrackGradeToolSystem m_Grade;
        private QuickRunsToolSystem m_QuickRuns;
        private ToolSystem m_ToolSystem;
        private ObjectToolSystem m_ObjectTool;
        private ProxyAction m_Toggle, m_Build;
        private bool m_Armed, m_Disabled, m_ErrorLogged;
        private int m_Probe = -1;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Tool = World.GetOrCreateSystemManaged<LiftLineToolSystem>();
            m_Grade = World.GetOrCreateSystemManaged<CatTrackGradeToolSystem>();
            m_QuickRuns = World.GetOrCreateSystemManaged<QuickRunsToolSystem>();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_ObjectTool = World.GetOrCreateSystemManaged<ObjectToolSystem>();
            Mod.Log.Info($"{nameof(LiftLineHotkeySystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = m_Disabled = m_ErrorLogged = false;
            m_Probe = -1;
            try
            {
                SkiResortSettings s = Mod.Settings;
                m_Toggle = s != null ? TryGet(s, SkiResortSettings.ActToggleLiftLine) : null;
                m_Build = s != null ? TryGet(s, SkiResortSettings.ActBuildLiftLine) : null;
                bool inGame = mode.IsGame();
                if (m_Toggle != null)
                    m_Toggle.shouldBeEnabled = inGame;
                if (m_Build != null)
                    m_Build.shouldBeEnabled = false;
                if (!inGame)
                    return;
                if (m_Toggle == null || m_Build == null)
                {
                    Mod.Log.Warn($"LiftLine [keys]: INERT — action(s) not registered ({(m_Toggle == null ? SkiResortSettings.ActToggleLiftLine + " " : string.Empty)}" +
                                 $"{(m_Build == null ? SkiResortSettings.ActBuildLiftLine : string.Empty)}; RegisterKeyBindings missing or failed in Mod.OnLoad)");
                    return;
                }
                m_Armed = true;
                m_Probe = ProbeDelayFrames;
                Mod.Log.Info($"LiftLine [keys]: ARMED — {SkiResortSettings.ActToggleLiftLine} toggles the lift-line tool, " +
                             $"{SkiResortSettings.ActBuildLiftLine} builds (live only while the tool is open); rebind in Options ▸ Modular Ski Resort ▸ Key bindings");
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        protected override void OnUpdate()
        {
            if (!m_Armed || m_Disabled)
                return;
            try
            {
                GameManager gm = GameManager.instance;
                if (gm == null || gm.isGameLoading)
                    return;
                bool active = m_Tool.IsActive;
                m_Tool.SetStationPlacing(m_ToolSystem.activeTool == m_ObjectTool && m_ObjectTool.prefab != null &&
                                         SkiCatalog.StationByPrefab(m_ObjectTool.prefab.name) != null);
                bool buildLive = active || m_Grade.IsActive || m_QuickRuns.IsActive;
                if (m_Build.shouldBeEnabled != buildLive)
                    m_Build.shouldBeEnabled = buildLive;
                if (m_Probe > 0 && --m_Probe == 0 && !m_Toggle.enabled)
                    Mod.Log.Warn($"LiftLine [keys]: {SkiResortSettings.ActToggleLiftLine} is not live {ProbeDelayFrames} frames after the " +
                                 "load (probable key conflict with a vanilla action, InputConflictResolution.cs:138-198) — rebind it");
                if (m_Toggle.WasPressedThisFrame())
                {
                    if (active)
                        m_Tool.Deactivate();
                    else
                        m_Tool.TryActivate("hotkey");
                }
                if (active && m_Build.WasPressedThisFrame())
                    m_Tool.RequestBuild();
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
                Mod.Log.Error($"!!! {nameof(LiftLineHotkeySystem)} EXCEPTION — lift-line keys disabled until the next game load:\n{e}");
            }
        }
    }
}
