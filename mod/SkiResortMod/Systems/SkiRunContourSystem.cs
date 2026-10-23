// SkiRunContourSystem - turns the vanilla terrain contour lines on while the area tool holds one of our run prefabs, and gives
// the player's own setting back afterwards. Engine citations are `Game.X/File.cs:line` in tools/decompile/1.6.0f1.
//
// The only route: UndergroundViewSystem.OnUpdate sets contourLinesOn = globalContourLinesOn || (GetActualSnap(selectedSnap, on,
// off) & Snap.ContourLines) != 0 (UndergroundViewSystem.cs:120; GetActualSnap ToolBaseSystem.cs:467-470). The area tool's Surface
// branch never puts ContourLines into its masks (AreaToolSystem.cs:3291-3299; 1.6.2 :3302), so OR-ing it into selectedSnap would be
// masked out and would corrupt the player's snap selection. What remains is the public setter globalContourLinesOn
// (UndergroundViewSystem.cs:67), the flag the vanilla contour button reads and writes (ToolUISystem.cs:154, 177, 499-511; 1.6.2
// :555-567). It is not serialized, and OnGameLoaded resets it to false on every load (:92-96).
//
// Order: UpdateBefore<SkiRunContourSystem, UndergroundViewSystem>(PreCulling) in Mod.cs (UndergroundViewSystem at SystemOrder.cs:632)
// so our write is read the same frame. PreCulling runs every frame, paused included.
//
// State machine (Step()):
//   want = setting && armed && activeTool == areaTool && areaTool.prefab is one of our runs
//   g    = uvs.globalContourLinesOn
//   if (m_Active) { if (g != m_Written) { m_Active = false; m_Latched = want; }  // someone else wrote it: theirs, never restore
//                   else if (!want)     { g := false; m_Active = false; } }
//   else if (want && !m_Latched) { if (!g) { g := true; m_Written = true; m_Active = true; } else m_Latched = true; }
//   if (!want) m_Latched = false;
// Release (OnStopRunning / OnDestroy / exception): if (m_Active && g == m_Written) g := false; clear the flags. OnGameLoadingComplete
// applies Release too: after a load vanilla has already reset g (:94), so the guard (g == m_Written) is false and nothing is
// written; it can only fire if g still holds our write, and then it removes our own leftover.
//
// Why poll: a click on the vanilla button is a plain property write with no callback (ToolUISystem.cs:508-511); the button shows
// ON while we override (its getter reads the same flag, :499-506) and a click writes false, which the next frame sees as
// g != m_Written, so the player's click wins and is never undone. The "Toggle Global Contour Lines" hotkey has no C# handler
// (JS-side; presumably the same trigger, unverified). Same-frame double writes are indistinguishable from no write; accepted.
//
// No RequireForUpdate: OnUpdate must run to restore. The tool's prefab is cached as reference -> isRunPrefab and resolved
// (PrefabSystem.TryGetEntity, PrefabSystem.cs:710-713, + SkiRunOwnershipSystem.IsRunPrefab) only when the reference changes and
// ownership is armed. AreaToolSystem.prefab and ToolSystem.activeTool are plain field getters (AreaToolSystem.cs:2637-2652;
// ToolSystem.cs:84-99). While contourLinesOn, UndergroundPass skips its early-out and re-renders terrain heights + one
// full-screen contour compute per frame (UndergroundPass.cs:105-126): vanilla's own toggle cost, only while we override.
//
// Game mode only (outside it want is false, so a leftover override is still restored). Defensive contract: "created" line, ARMED
// line per load, m_Armed/m_Disabled/m_ErrorLogged reset in OnGameLoadingComplete, one-shot "!!!" catch that restores (if ours) and
// disables the override until the next game load.

using System;
using Colossal.Serialization.Entities;
using Game;
using Game.Prefabs;
using Game.Rendering;
using Game.SceneFlow;
using Game.Tools;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunContourSystem : GameSystemBase
    {
        public const string StateOff = "off";
        public const string StateOverriding = "overriding";
        public const string StateLatchedPlayerOn = "latched (player had it on)";
        public const string StateLatchedPlayerWrote = "latched (player changed it)";
        public const string StateDisabled = "DISABLED (exception, see mod log)";

        private ToolSystem m_ToolSystem;
        private AreaToolSystem m_AreaTool;
        private PrefabSystem m_PrefabSystem;
        private UndergroundViewSystem m_UndergroundView;
        private SkiRunOwnershipSystem m_Ownership;

        private bool m_Active;     // our override is live: g holds OUR write
        private bool m_Written;    // the value we last wrote (only ever true while active)
        private bool m_Latched;    // want is true but we stand aside until want drops

        private PrefabBase m_CachedPrefab;
        private bool m_CachedIsRun;

        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;

        /// <summary>Dev-tab line ("Ski Runs" tab, contour state): "off" / "overriding" / "latched (player had it on)" /
        /// "latched (player changed it)" / "DISABLED ...". A plain field written on transitions only.</summary>
        public string StateText = StateOff;

        /// <summary>Overrides started since the last load (dev-tab aid)</summary>
        public int Overrides;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_AreaTool = World.GetOrCreateSystemManaged<AreaToolSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_UndergroundView = World.GetOrCreateSystemManaged<UndergroundViewSystem>();
            m_Ownership = World.GetOrCreateSystemManaged<SkiRunOwnershipSystem>();
            Mod.Log.Info($"{nameof(SkiRunContourSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            // Applied through Release so a leftover of our write (g still == m_Written) is removed instead of leaked; after a normal
            // load vanilla already reset g (UndergroundViewSystem.cs:94) and nothing is written.
            SafeRelease("load");

            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_CachedPrefab = null;
            m_CachedIsRun = false;
            Overrides = 0;
            StateText = StateOff;

            if (mode != GameMode.Game)
                return;

            m_Armed = true;
            Mod.Log.Info("RunContour: ARMED — sets UndergroundViewSystem.globalContourLinesOn " +
                         "while the area tool holds a run prefab (ContourLinesWhileDrawingRuns, " +
                         "ownership armed), restores it on leaving only if the flag still holds " +
                         "our write; a player write (contour button / hotkey) or a load wins and " +
                         "is never undone; PreCulling before UndergroundViewSystem");
        }

        protected override void OnUpdate()
        {
            if (m_Disabled)
                return;

            try
            {
                bool want = ComputeWant();
                bool g = m_UndergroundView.globalContourLinesOn;
                bool wasActive = m_Active;
                bool wasLatched = m_Latched;

                int write = Step(ref m_Active, ref m_Written, ref m_Latched, want, g);
                if (write >= 0)
                    m_UndergroundView.globalContourLinesOn = write == 1;

                if (m_Active != wasActive || m_Latched != wasLatched)
                    OnTransition(wasActive, want, g, write);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                SafeRelease("exception");
                StateText = StateDisabled;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiRunContourSystem)} EXCEPTION — contour-line " +
                                  $"override disabled until the next game load:\n{e}");
                }
            }
        }

        protected override void OnStopRunning()
        {
            SafeRelease("system stopped");
            base.OnStopRunning();
        }

        protected override void OnDestroy()
        {
            SafeRelease("system destroyed");
            base.OnDestroy();
        }


        /// <summary>One frame of the state machine. Returns -1 = no write, 0 = write false, 1 = write true. Never touches
        /// engine state.</summary>
        internal static int Step(ref bool active, ref bool written, ref bool latched, bool want, bool g)
        {
            int write = -1;
            if (active)
            {
                if (g != written)
                {
                    active = false;
                    latched = want;
                }
                else if (!want)
                {
                    write = 0;
                    active = false;
                }
            }
            else if (want && !latched)
            {
                if (!g)
                {
                    write = 1;
                    written = true;
                    active = true;
                }
                else
                {
                    latched = true;
                }
            }
            if (!want)
                latched = false;
            return write;
        }

        /// <summary>True when the caller must write false (the flag still holds our write). Clears the flags either way.</summary>
        internal static bool Release(ref bool active, ref bool written, ref bool latched, bool g)
        {
            bool restore = active && g == written;
            active = false;
            latched = false;
            return restore;
        }


        private bool ComputeWant()
        {
            if (!m_Armed)
                return false;
            var settings = Mod.Settings;
            if (settings != null && !settings.ContourLinesWhileDrawingRuns)
                return false;
            if (m_ToolSystem.activeTool != m_AreaTool)
                return false;
            // A save loading forces the default tool anyway (ToolSystem.cs:610-614); this only makes sure we never start an
            // override mid-load (GameManager.cs:262-272).
            GameManager gm = GameManager.instance;
            if (gm == null || gm.isGameLoading)
                return false;
            if (!m_Ownership.IsArmed)
                return false;
            return IsRunPrefabCached(m_AreaTool.prefab);
        }

        private bool IsRunPrefabCached(PrefabBase prefab)
        {
            if (ReferenceEquals(prefab, m_CachedPrefab))
                return m_CachedIsRun;
            m_CachedPrefab = prefab;
            m_CachedIsRun = !ReferenceEquals(prefab, null) &&
                            m_PrefabSystem.TryGetEntity(prefab, out Entity entity) &&
                            m_Ownership.IsRunPrefab(entity);
            return m_CachedIsRun;
        }

        private void OnTransition(bool wasActive, bool want, bool g, int write)
        {
            if (m_Active)
            {
                Overrides++;
                StateText = StateOverriding;
                Mod.Log.Info("RunContour: contour lines ON for run drawing (player's setting " +
                             "was off; restored on leaving)");
            }
            else if (m_Latched)
            {
                if (wasActive)
                {
                    StateText = StateLatchedPlayerWrote;
                    Mod.Log.Info($"RunContour: contour flag changed by someone else " +
                                 $"(now {g}) — theirs now, override released without restore");
                }
                else
                {
                    StateText = StateLatchedPlayerOn;
                    Mod.Log.Info("RunContour: contour lines already on (player's setting) — " +
                                 "left untouched");
                }
            }
            else
            {
                StateText = StateOff;
                if (wasActive && write == 0)
                    Mod.Log.Info("RunContour: contour lines restored to off (left run drawing)");
                else if (wasActive)
                    Mod.Log.Info($"RunContour: contour flag changed by someone else (now {g}) " +
                                 "while leaving — released without restore");
            }
        }

        private void SafeRelease(string why)
        {
            try
            {
                bool wasActive = m_Active;
                bool g = m_UndergroundView != null && m_UndergroundView.globalContourLinesOn;
                if (Release(ref m_Active, ref m_Written, ref m_Latched, g))
                {
                    m_UndergroundView.globalContourLinesOn = false;
                    Mod.Log.Info($"RunContour: contour lines restored to off ({why})");
                }
                else if (wasActive)
                {
                    Mod.Log.Info($"RunContour: override released without restore ({why}; " +
                                 "the flag no longer held our write)");
                }
                if (StateText != StateDisabled)
                    StateText = StateOff;
            }
            catch (Exception e)
            {
                m_Active = false;
                m_Latched = false;
                Mod.Log.Warn($"RunContour: release failed ({why}): {e.Message}");
            }
        }
    }
}
