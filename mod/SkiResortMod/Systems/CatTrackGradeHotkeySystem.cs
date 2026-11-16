// Cat track tool keys. Pattern of LiftLineHotkeySystem. Its engine notes (EngineNotes/LiftLineHotkeySystem.md) cover the action plumbing:
// an inactive tool never ticks (ToolSystem.cs:312-327). GetAction is null before RegisterKeyBindings (ModSetting.cs:289-292). Mod actions
// start disabled (ProxyAction.cs:322-349).
// ToggleCatTrackGradeTool (default Ctrl+Shift+K) calls CatTrackGradeToolSystem.TryActivate("hotkey") / Deactivate, in a loaded city.
// The Build key (BuildLiftLine, default Enter) is shared with the lift tool. LiftLineHotkeySystem keeps it live while either tool is open
// (one writer of shouldBeEnabled). This system reads the press while the grading tool is active and calls RequestApply.
// Two mode keys are live only while the tool is open (this system is their one writer): CatTrackDeleteMode (default Ctrl+Shift+D) toggles
// the tool's Delete mode (field test 2026-10-26b), CatTrackSkiWay (default Ctrl+Shift+J) its Ski way profile (T194). As they are enabled
// only while the tool is open, their liveness probe runs then: once per load, ProbeDelayFrames after the first opening, with a warning if
// the conflict resolver disabled one (InputConflictResolution.cs:138-198). A modifier binding conflicts with a plain vanilla key only when
// one side's composite disallows modifiers (ProxyBinding.cs:822-825). The field logs of 2026-10-26 / 26b show Ctrl+Shift+Q live beside
// the vanilla camera's Q (D and W are camera keys too).
// UIUpdate, SystemOrder.cs:67: every frame, paused or not. Logs: one ARMED / INERT line per load. One line if a key is not live 120 frames
// after the load (the toggle) or after the tool first opened (the mode keys).

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

        /// <summary>A key that is live only while the tool is open, with its once-per-load liveness probe.</summary>
        private sealed class ModeKey
        {
            public string Name, ChipName;
            public ProxyAction Action;
            public int Probe = -1;
        }

        private CatTrackGradeToolSystem m_Tool;
        private ProxyAction m_Toggle, m_Build;
        private readonly ModeKey m_DeleteMode = new ModeKey { Name = SkiResortSettings.ActCatTrackDeleteMode, ChipName = "Delete cat tracks" };
        private readonly ModeKey m_SkiWay = new ModeKey { Name = SkiResortSettings.ActCatTrackSkiWay, ChipName = "Ski way" };
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
                foreach (ModeKey k in new[] { m_DeleteMode, m_SkiWay })
                {
                    k.Probe = -1;
                    k.Action = s != null ? TryGet(s, k.Name) : null;
                    if (k.Action != null)
                        k.Action.shouldBeEnabled = false;
                }
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
                             $"{m_DeleteMode.Name} toggles its delete mode ({(m_DeleteMode.Action != null ? "registered" : "NOT registered")}), " +
                             $"{m_SkiWay.Name} its Ski way profile ({(m_SkiWay.Action != null ? "registered" : "NOT registered")}); " +
                             "rebind in Options ▸ Modular Ski Resort ▸ Key bindings");
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        protected override void OnUpdate()
        {
            if (m_Disabled || !m_Armed)
                return;
            try
            {
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
                if (Pressed(m_DeleteMode, active))
                    m_Tool.ToggleDeleteMode("key");
                if (Pressed(m_SkiWay, active))
                    m_Tool.ToggleSkiWay("key");
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        /// <summary>Keeps a mode key live exactly while the tool is open, runs its probe, and reads its press.</summary>
        private static bool Pressed(ModeKey k, bool active)
        {
            if (k.Action == null)
                return false;
            if (k.Action.shouldBeEnabled != active)
            {
                k.Action.shouldBeEnabled = active;
                if (active && k.Probe < 0)
                    k.Probe = ProbeDelayFrames;   // once per load (0 = done)
            }
            if (active && k.Probe > 0 && --k.Probe == 0 && !k.Action.enabled)
                Mod.Log.Warn($"Grade [keys]: {k.Name} is not live {ProbeDelayFrames} frames after the tool opened (probable key conflict with a " +
                             $"vanilla action, InputConflictResolution.cs:138-198) — rebind it; the hub's {k.ChipName} chip still works");
            return active && k.Action.WasPressedThisFrame();
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
