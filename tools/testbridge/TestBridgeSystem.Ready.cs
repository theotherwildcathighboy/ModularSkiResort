// The readiness gate: `status.ready` and the READY / NOT READY lines in SkiTestBridge.log (README "Wait for ready").
// The 1.6.2f1 decompile shows loading continues past the moment the main menu draws:
//   - GameManager.state is Loading during every load and WorldReady after it (Game.SceneFlow/GameManager.cs:1063, :1137).
//   - Paradox Mods can populate after boot ("Defer Mods load", GameManager.cs:1524-1531). Their prefabs, code mods and UI
//     modules then arrive in the main menu (GameManager.cs:1541-1650: ModManager.Initialize(reinitialize), AddUIModule).
//   - Virtual texturing inits its material queues over many frames (TextureStreamingSystem.OnUpdate, VTMaterialsLeftToLoadCount)
//     and refills them when mods add surfaces (RefreshVT, GameManager.cs:1499 and :1568).
//   - With --uiDeveloperMode, Gameface live reload is on (UIManager settings liveReload = developerMode). A .js / .css / .html
//     change in a watched folder (local UI mods are watched) reloads the page 1 s later (Colossal.UI UILiveReload.OnChanged).
// No part proves why the 2026-11-08 exits happened. The gate only says that nothing above is still moving.

using System;
using System.Linq;
using Colossal;
using Colossal.IO.AssetDatabase;
using Colossal.IO.AssetDatabase.VirtualTexturing;
using Colossal.Serialization.Entities;
using Game;
using Game.SceneFlow;
using Newtonsoft.Json.Linq;
using Unity.Entities;

namespace SkiTestBridge
{
    public partial class TestBridgeSystem
    {
        /// <summary>Seconds every part must hold, with an unchanged fingerprint, before `ready` turns true. Longer than the gap
        /// between a mode change and the live-reload page loads that follow it (measured 2026-11-08: 6.1 to 7.6 s after the main
        /// menu, 9.3 s after a save load).</summary>
        private const double kReadyQuietSeconds = 15.0;

        private TextureStreamingSystem m_TextureStreaming;
        private EntityQuery m_PrefabQuery;
        private Colossal.UI.UIView m_ReadyView;
        private JObject m_ReadyParts = new JObject();
        private string m_ReadyFingerprint;
        private double m_ReadyQuietSince = -1;
        private double m_ReadyNextSample;
        private double m_ReadyNextWaitingLog = 30;
        private bool m_ReadyLast;
        private double m_ModeReachedAt = -1;
        private string m_ModeReached;
        private double m_LastPageLoad = -1;
        private int m_PageLoads;

        private static double Now => UnityEngine.Time.realtimeSinceStartupAsDouble;

        private void CreateReady()
        {
            m_TextureStreaming = World.GetExistingSystemManaged<TextureStreamingSystem>();
            m_PrefabQuery = GetEntityQuery(ComponentType.ReadOnly<Game.Prefabs.PrefabData>());
            GameManager.instance.onGameLoadingComplete += OnModeReached;
            m_ReadyView = GameManager.instance.userInterface?.view;
            if (m_ReadyView != null)
                m_ReadyView.Listener.FinishLoad += OnPageLoaded;
            // The bridge may come up after the first main menu (a mod reinitialize): count from now then.
            if (GameManager.instance.state == GameManager.State.WorldReady && GameManager.instance.gameMode != GameMode.Other)
                OnModeReached(Purpose.Cleanup, GameManager.instance.gameMode);
        }

        private void DestroyReady()
        {
            GameManager.instance.onGameLoadingComplete -= OnModeReached;
            if (m_ReadyView != null)
                m_ReadyView.Listener.FinishLoad -= OnPageLoaded;
        }

        private void OnModeReached(Purpose purpose, GameMode mode)
        {
            m_ModeReachedAt = Now;
            m_ModeReached = mode.ToString();
            m_ReadyLast = false;
            m_ReadyQuietSince = -1;
            Mod.Log.Info($"MODE {mode} reached at t={Now:F1}s ({purpose})");
        }

        private void OnPageLoaded(string url)
        {
            m_LastPageLoad = Now;
            m_PageLoads++;
            Mod.Log.Info($"UI page load #{m_PageLoads} finished at t={Now:F1}s: {url}");
        }

        /// <summary>Samples the parts twice a second (UIUpdate runs in every mode, GameManager.cs:712-725).</summary>
        private void TickReady()
        {
            double now = Now;
            if (now < m_ReadyNextSample)
                return;
            m_ReadyNextSample = now + 0.5;
            m_ReadyParts = ReadyParts(out bool allParts, out string fingerprint);
            // The quiet clock starts when every part holds, and restarts whenever the fingerprint moves.
            if (!allParts)
                m_ReadyQuietSince = -1;
            else if (m_ReadyQuietSince < 0 || fingerprint != m_ReadyFingerprint)
                m_ReadyQuietSince = now;
            m_ReadyFingerprint = fingerprint;
            bool ready = allParts && m_ReadyQuietSince >= 0 && now - m_ReadyQuietSince >= kReadyQuietSeconds;
            if (ready != m_ReadyLast)
            {
                string since = m_ModeReachedAt >= 0 ? $", {now - m_ModeReachedAt:F1}s after MODE {m_ModeReached}" : "";
                Mod.Log.Info($"{(ready ? "READY" : "NOT READY")} at t={now:F1}s{since}: {m_ReadyParts.ToString(Newtonsoft.Json.Formatting.None)}");
                m_ReadyLast = ready;
                m_ReadyNextWaitingLog = now + 30;
            }
            else if (!ready && now >= m_ReadyNextWaitingLog)
            {
                // Silence must be impossible: a gate that never opens says every 30 s what holds it.
                Mod.Log.Info($"WAITING at t={now:F1}s (fingerprint {m_ReadyFingerprint}): {m_ReadyParts.ToString(Newtonsoft.Json.Formatting.None)}");
                m_ReadyNextWaitingLog = now + 30;
            }
        }

        private JObject ReadyState()
        {
            double now = Now;
            var result = new JObject
            {
                ["ready"] = m_ReadyLast,
                ["quietFor"] = m_ReadyQuietSince >= 0 ? Math.Round(now - m_ReadyQuietSince, 1) : 0,
                ["parts"] = m_ReadyParts,
                ["pageLoads"] = m_PageLoads,
            };
            if (m_ModeReachedAt >= 0)
                result["sinceMode"] = Math.Round(now - m_ModeReachedAt, 1);
            if (m_LastPageLoad >= 0)
                result["sincePageLoad"] = Math.Round(now - m_LastPageLoad, 1);
            return result;
        }

        // `settings` op: the text scale and the screen resolution through the same setters and Apply as the options menu, without its
        // confirmation dialog (GraphicsSettings.Apply, Game.Settings/GraphicsSettings.cs:331-403; resolution snaps to an available
        // mode, :137-147). textScale takes any value; the slider offers 1.0 to 1.5 in 0.1 steps (InterfaceSettings.cs:144).
        // The game saves these into Settings.coc: back the file up first and restore it with the game closed.
        private static JObject SettingsCommand(JObject args)
        {
            var shared = Game.Settings.SharedSettings.instance;
            if (args["textScale"] != null)
            {
                shared.userInterface.textScale = (float)args["textScale"];
                shared.userInterface.Apply();
            }
            var graphics = shared.graphics;
            if (args["width"] != null || args["displayMode"] != null)
            {
                if (args["displayMode"] != null)
                    graphics.displayMode = (Game.Settings.DisplayMode)Enum.Parse(typeof(Game.Settings.DisplayMode), (string)args["displayMode"]);
                if (args["width"] != null)
                {
                    Game.Settings.ScreenResolution resolution = graphics.resolution;
                    resolution.width = (int)args["width"];
                    resolution.height = (int)args["height"];
                    graphics.resolution = resolution;
                }
                graphics.Apply();
            }
            return new JObject
            {
                ["textScale"] = shared.userInterface.textScale,
                ["resolution"] = graphics.resolution.ToString(),
                ["displayMode"] = graphics.displayMode.ToString(),
                ["screen"] = new JArray(UnityEngine.Screen.width, UnityEngine.Screen.height),
            };
        }

        private JObject ReadyParts(out bool all, out string fingerprint)
        {
            GameManager game = GameManager.instance;
            bool state = game.state == GameManager.State.WorldReady;
            bool mode = game.gameMode == GameMode.MainMenu || game.gameMode == GameMode.Game || game.gameMode == GameMode.Editor;

            var mods = game.modManager;
            int modCount = 0, modsLoaded = 0;
            if (mods != null)
                foreach (var info in mods)
                {
                    modCount++;
                    if (info.isLoaded)
                        modsLoaded++;
                }
            int enabled = mods?.ListModsEnabled().Length ?? 0;
            bool modsOk = mods != null && mods.isInitialized;
            bool pdx = AssetDatabase.global.databases.Contains(AssetDatabase<ParadoxMods>.instance);

            int vtLeft = m_TextureStreaming?.VTMaterialsLeftToLoadCount ?? -1;
            int vtDup = m_TextureStreaming?.VTMaterialsDuplicatesToProcessCount ?? -1;
            int vtBusy = m_TextureStreaming?.busyRequestsCount ?? -1;
            bool vt = vtLeft == 0 && vtDup == 0;

            // The task queue holds a "Ping" job that comes and goes for as long as the game runs (measured 2026-11-08), so only a
            // pending save or load ("SaveLoadGame", Game.UI.Menu/MenuUISystem.cs:718-1049) holds the gate.
            string[] tasks = TaskManager.instance.taskRegistry.Where(t => !t.Value.IsCompleted).Select(t => t.Key).ToArray();
            int queued = TaskManager.instance.taskQueue.count;
            string[] queueNames = TaskManager.instance.taskQueue.ToArray();
            bool tasksOk = tasks.Length == 0 && !queueNames.Contains("SaveLoadGame");

            bool view = m_ReadyView != null && m_ReadyView.View != null && m_ReadyView.View.IsReadyForBindings();
            bool page = m_LastPageLoad < 0 || Now - m_LastPageLoad >= kReadyQuietSeconds;

            int prefabs = m_PrefabQuery.CalculateEntityCount();
            all = state && mode && modsOk && pdx && vt && tasksOk && view && page;
            fingerprint = $"{game.state}|{game.gameMode}|{modCount}|{modsLoaded}|{enabled}|{prefabs}|{m_PageLoads}";
            return new JObject
            {
                ["state"] = game.state.ToString(),
                ["mode"] = game.gameMode.ToString(),
                ["mods"] = new JObject { ["ok"] = modsOk, ["loaded"] = modsLoaded, ["code"] = modCount, ["enabled"] = enabled, ["pdxRegistered"] = pdx },
                ["vt"] = new JObject { ["ok"] = vt, ["toLoad"] = vtLeft, ["duplicates"] = vtDup, ["busyRequests"] = vtBusy },
                ["tasks"] = new JObject { ["ok"] = tasksOk, ["running"] = new JArray(tasks.Cast<object>().ToArray()), ["queued"] = queued, ["queues"] = new JArray(queueNames.Cast<object>().ToArray()) },
                ["ui"] = new JObject { ["ok"] = view && page, ["bindings"] = view, ["pageLoads"] = m_PageLoads },
                ["prefabs"] = prefabs,
            };
        }
    }
}
