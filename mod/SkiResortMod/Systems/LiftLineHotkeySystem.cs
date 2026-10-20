// LiftLineHotkeySystem — the lift-line tool's two keys (T48 round 2; build note docs/research/field/2026-10-20/t48-round2-build.md):
//   ToggleLiftLineTool (default Ctrl+Shift+L) → LiftLineToolSystem.TryActivate("hotkey") / Deactivate, in a loaded city;
//   BuildLiftLine (default Enter) → LiftLineToolSystem.RequestBuild(), live ONLY while the tool is active (shouldBeEnabled follows
//   the tool every frame, so Enter is never taken from the game otherwise).
// Why a separate system and the action plumbing: the SkiGladeHotkeySystem header (an inactive tool never ticks, ToolSystem.cs:312-327;
// mod actions exist after Mod.OnLoad's RegisterKeyBindings, ModSetting.cs:141-231, GetAction is null before, :289-292; they start
// disabled, ProxyAction.cs:322-349; a key lost to a vanilla action is disabled by the conflict pass, InputConflictResolution.cs:138-198).
// UIUpdate (SystemOrder.cs:67): every frame, paused or not. The build request lands on the tool's next frame.
// LOGS: one ARMED / INERT line per load with the bindings; one line when an armed action is not live 120 frames after the load
// (probable key conflict); the tool logs every press it acts on.

using System;
using Colossal.Serialization.Entities;
using Game;
using Game.Input;
using Game.SceneFlow;
using SkiResortMod.Settings;

namespace SkiResortMod.Systems
{
    public sealed partial class LiftLineHotkeySystem : GameSystemBase
    {
        private const int ProbeDelayFrames = 120;

        private LiftLineToolSystem m_Tool;
        private ProxyAction m_Toggle, m_Build;
        private bool m_Armed, m_Disabled, m_ErrorLogged;
        private int m_Probe = -1;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Tool = World.GetOrCreateSystemManaged<LiftLineToolSystem>();
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
                if (m_Build.shouldBeEnabled != active)
                    m_Build.shouldBeEnabled = active;
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
