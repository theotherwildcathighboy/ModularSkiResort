// Polls the three glade-tool keys and the dump-diagnostics key at SystemUpdatePhase.UIUpdate (every frame, paused or not).
// An inactive tool never ticks, so the tool cannot poll its own toggle (ToolBaseSystem.cs:310, ToolSystem.cs:312-327).
// toggle (Ctrl+Shift+G) -> tool.IsActive ? Deactivate() : TryActivate("hotkey").
// Dump (Ctrl+Alt+G) is polled BEFORE the toggle and the gate below.
// GladeNextMode (Alt+G) -> tool.ToggleTarget, GladeNextVariant (Shift+G) -> tool.StepLevel: ONLY while the tool is active (no double toggle).
// Mod actions start DISABLED and exist only after Mod.OnLoad calls RegisterKeyBindings (the system retries resolution at every load).
// OnGameLoadingComplete enables each action per game mode. A mod action always loses a key conflict to an enabled vanilla action.
// The system works in Game mode only. An isGameLoading guard applies (GameManager.cs:262-272). A one-shot "!!!" disables the hotkeys
// until the next load.
// Engine notes: EngineNotes/SkiGladeHotkeySystem.md

using System;
using System.Collections.Generic;
using System.Text;
using Colossal.Serialization.Entities;
using Game;
using Game.Input;
using Game.SceneFlow;
using Game.Tools;
using SkiResortMod.Settings;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiGladeHotkeySystem : GameSystemBase
    {
        private static class Tuning
        {
            /// <summary>UIUpdate frames between arming and the one-shot conflict probe. Conflict resolution runs when the enabled-action set
            /// changes, and two seconds is comfortably after that (exact latency unverified).</summary>
            public const int ProbeDelayFrames = 120;

            /// <summary>Watcher drop lines per action and per cause label (mask / not mask) per load, each with its matching "live again"
            /// line. Then one "further drops not logged" line for that action and label.</summary>
            public const int WatcherLinesPerAction = 8;
        }

        private const int CauseMask = 0, CauseOther = 1, CauseCount = 2;

        private const int SlotToggle = 0, SlotVariant = 1, SlotMode = 2, SlotDump = 3, SlotCount = 4;
        private static readonly string[] SlotNames =
        {
            SkiResortSettings.ActToggleGlade, SkiResortSettings.ActGladeNextVariant,
            SkiResortSettings.ActGladeNextMode, SkiResortSettings.ActDumpDiagnostics,
        };

        private SkiGladeToolSystem m_Tool;
        private SkiRunDrawFeedbackSystem m_Feedback;
        private ToolSystem m_ToolSystem;
        private AreaToolSystem m_AreaTool;
        private ProxyAction m_Toggle;
        private ProxyAction m_NextVariant;
        private ProxyAction m_NextMode;
        private ProxyAction m_Dump;
        private readonly ProxyAction[] m_Slots = new ProxyAction[SlotCount];

        // Per-load state — reset at OnGameLoadingComplete.
        private bool m_Armed;          // at least one action group armed
        private bool m_GladeArmed;     // toggle + next variant + next mode resolved and enabled
        private bool m_DumpArmed;      // dump action resolved and enabled (independent)
        private bool m_Disabled;
        private bool m_ErrorLogged;
        private bool m_DumpOffLogged;  // the "diagnostics off" refusal, once per load
        private int m_ProbeCountdown = -1;
        private readonly bool[] m_WasLive = new bool[SlotCount];
        // Re-arming watcher state
        private readonly int[] m_DropLines = new int[SlotCount * CauseCount];      // [slot * CauseCount + cause]
        private readonly bool[] m_DropCapNoted = new bool[SlotCount * CauseCount];
        private readonly bool[] m_BackPending = new bool[SlotCount];                // a logged drop awaits its "live again"

        public int WatcherDrops;

        /// <summary>One-line hotkey status for the "Ski Glades" developer tab. The system writes it at arm/probe/watcher time, so the 10 Hz
        /// tab getter costs nothing.</summary>
        public string StatusLine { get; private set; } = "not armed (no city loaded yet)";

        public long DumpPresses;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Tool = World.GetOrCreateSystemManaged<SkiGladeToolSystem>();
            m_Feedback = World.GetOrCreateSystemManaged<SkiRunDrawFeedbackSystem>();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_AreaTool = World.GetOrCreateSystemManaged<AreaToolSystem>();
            TryResolveActions();
            Mod.Log.Info($"{nameof(SkiGladeHotkeySystem)} created (glade key actions " +
                         $"{(GladeActionsResolved() ? "resolved" : "not resolved yet")}, dump key action " +
                         $"{(m_Dump != null ? "resolved" : "not resolved yet")} — each retried at every load)");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            m_Armed = false;
            m_GladeArmed = false;
            m_DumpArmed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_DumpOffLogged = false;
            m_ProbeCountdown = -1;
            DumpPresses = 0;
            WatcherDrops = 0;
            for (int i = 0; i < SlotCount; i++)
            {
                m_WasLive[i] = false;
                m_BackPending[i] = false;
            }
            for (int i = 0; i < m_DropLines.Length; i++)
            {
                m_DropLines[i] = 0;
                m_DropCapNoted[i] = false;
            }

            // shouldBeEnabled = mode.IsGame() (GameModeExtensions.cs:21-24)
            bool inGame = mode.IsGame();
            try
            {
                if (!GladeActionsResolved() || m_Dump == null)
                    TryResolveActions();

                SetActionsEnabled(inGame);

                if (!inGame)
                {
                    StatusLine = $"inert (game mode {mode})";
                    return;
                }

                m_GladeArmed = GladeActionsResolved();
                m_DumpArmed = m_Dump != null;
                if (!m_GladeArmed)
                {
                    // An incomplete glade group is never polled: leave none of it live.
                    for (int i = 0; i < SlotDump; i++)
                    {
                        if (m_Slots[i] != null)
                            m_Slots[i].shouldBeEnabled = false;
                    }
                    Mod.Log.Warn("Glade: glade hotkeys inert — key actions not registered " +
                                 $"({DescribeMissing()}; Settings.RegisterKeyBindings missing or failed in " +
                                 "Mod.OnLoad; ModSetting.cs:289-292 returns null until it runs). The 'Ski " +
                                 "Glades' developer tab still activates the tool.");
                }
                if (!m_DumpArmed)
                {
                    Mod.Log.Warn("Glade: dump hotkey inert — action '" + SkiResortSettings.ActDumpDiagnostics +
                                 "' not registered (RegisterKeyBindings missing or failed in Mod.OnLoad). " +
                                 "The developer tabs' 'Dump' buttons still work.");
                }
                if (!m_GladeArmed && !m_DumpArmed)
                {
                    StatusLine = "INERT — key actions not registered";
                    return;
                }

                m_Armed = true;
                m_ProbeCountdown = Tuning.ProbeDelayFrames;
                string bindings = DescribeBindings(withState: false);
                StatusLine = "armed — " + bindings;
                Mod.Log.Info($"Glade: hotkeys ARMED — {bindings}; glade keys {(m_GladeArmed ? "armed" : "INERT")}, " +
                             $"dump key {(m_DumpArmed ? "armed" : "INERT")} (resolved independently, R3); " +
                             "polled every frame at UIUpdate (paused included); dump first (any tool: area tool → " +
                             "run feedback dump, glade tool → glade dump, otherwise both), then toggle always, " +
                             $"{SkiResortSettings.ActGladeNextMode} (interior / tree line) and " +
                             $"{SkiResortSettings.ActGladeNextVariant} (tree density 1-8) only while the tool is " +
                             "active; per-frame watcher logs every time " +
                             "an armed action turns non-live and back (with the active tool and a cause label: " +
                             "text field / overlay mask, or probable key conflict), at most " +
                             $"{Tuning.WatcherLinesPerAction} lines per action and label per load " +
                             "(WatcherLinesPerAction), then one 'further drops not logged' line; " +
                             $"conflict probe (runtime + options marker) after {Tuning.ProbeDelayFrames} frames " +
                             "(ProbeDelayFrames)");
            }
            catch (Exception e)
            {
                m_Disabled = true;
                SafeDisableActions();
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiGladeHotkeySystem)} EXCEPTION during load " +
                                  $"setup — glade and dump hotkeys disabled until the next game load:\n{e}");
                }
            }
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !m_Armed)
                    return;

                GameManager gm = GameManager.instance;
                if (gm == null || gm.isGameLoading)
                    return;

                if (m_ProbeCountdown > 0)
                {
                    m_ProbeCountdown--;
                    if (m_ProbeCountdown == 0)
                        ProbeConflicts();
                }

                WatchActions();

                // The dump key first, in any tool, independent of the glade keys
                if (m_DumpArmed && m_Dump.WasPressedThisFrame())
                    DispatchDump();

                if (!m_GladeArmed)
                    return;

                if (m_Toggle.WasPressedThisFrame())
                {
                    if (m_Tool.IsActive)
                    {
                        Mod.Log.Info("Glade: hotkey toggle — deactivating the glade tool");
                        m_Tool.Deactivate();
                    }
                    else
                    {
                        // TryActivate owns the gating (armed, not disabled, Game mode) and logs its own refusal reason
                        bool ok = m_Tool.TryActivate("hotkey");
                        Mod.Log.Info($"Glade: hotkey toggle — activate {(ok ? "ok" : "refused")}");
                    }
                    // A toggle frame never also forwards target/density (the activation lands in the next ToolUpdate, ToolSystem.cs:312-327)
                    return;
                }

                if (!m_Tool.IsActive)
                    return;

                // GladeNextMode toggles run interior / tree line, GladeNextVariant steps the tree density 1..8 with wrap. The tool writes one
                // line per press ("Glade: target -> Tree line - hotkey").
                if (m_NextMode.WasPressedThisFrame())
                    m_Tool.ToggleTarget("hotkey");
                if (m_NextVariant.WasPressedThisFrame())
                    m_Tool.StepLevel("hotkey");
            }
            catch (Exception e)
            {
                m_Disabled = true;
                SafeDisableActions();
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    StatusLine = "DISABLED by exception (see mod log)";
                    Mod.Log.Error($"!!! {nameof(SkiGladeHotkeySystem)} EXCEPTION — glade and dump " +
                                  $"hotkeys disabled until the next game load:\n{e}");
                }
            }
        }


        /// <summary>Each dump has its own try/catch (both DumpToLog bodies also catch internally), so a failing dump costs one WARN, never
        /// the hotkeys.</summary>
        private void DispatchDump()
        {
            // A developer aid: a player who hits the chord by accident gets one line, not a page of state
            if (!SkiResortMod.Settings.SkiResortSettings.DiagnosticsOn)
            {
                if (!m_DumpOffLogged)
                {
                    m_DumpOffLogged = true;
                    Mod.Log.Info("Glade: hotkey dump ignored — turn on Options > Diagnostics > Measurement diagnostics to use it");
                }
                return;
            }
            DumpPresses++;
            bool area = m_ToolSystem.activeTool == m_AreaTool;   // ToolSystem.cs:84-99
            bool glade = m_Tool.IsActive;
            string context = area ? "area tool" : glade ? "glade tool" : "tool '" + ActiveToolId() + "': both dumps";
            Mod.Log.Info($"Glade: hotkey dump ({context}) — action live {m_Dump.enabled}");
            if (area || !glade)
                SafeDump("run feedback", m_Feedback.DumpToLog);
            if (glade || !area)
                SafeDump("glade tool", m_Tool.DumpToLog);
        }

        private static void SafeDump(string what, Action dump)
        {
            try
            {
                dump();
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Glade: hotkey dump — {what} dump failed: {e.GetType().Name}: {e.Message}");
            }
        }

        private string ActiveToolId()
        {
            ToolBaseSystem tool = m_ToolSystem.activeTool;
            return tool == null ? "none" : tool.toolID;   // ToolBaseSystem.cs:142
        }


        private bool GladeActionsResolved()
        {
            return m_Toggle != null && m_NextVariant != null && m_NextMode != null;
        }

        /// <summary>Finds each action in our input map, independently: a failed lookup costs only that action. Null-tolerant: OnLoad
        /// assigns Mod.Settings before system registration, but RegisterKeyBindings may have failed (try/caught there).</summary>
        private void TryResolveActions()
        {
            SkiResortSettings settings = Mod.Settings;
            if (settings == null || !settings.keyBindingRegistered)   // ModSetting.cs:30, 230
                return;
            if (m_Toggle == null) m_Toggle = TryGet(settings, SkiResortSettings.ActToggleGlade);
            if (m_NextVariant == null) m_NextVariant = TryGet(settings, SkiResortSettings.ActGladeNextVariant);
            if (m_NextMode == null) m_NextMode = TryGet(settings, SkiResortSettings.ActGladeNextMode);
            if (m_Dump == null) m_Dump = TryGet(settings, SkiResortSettings.ActDumpDiagnostics);
            m_Slots[SlotToggle] = m_Toggle;
            m_Slots[SlotVariant] = m_NextVariant;
            m_Slots[SlotMode] = m_NextMode;
            m_Slots[SlotDump] = m_Dump;
        }

        private static ProxyAction TryGet(SkiResortSettings settings, string name)
        {
            try
            {
                return settings.GetAction(name);   // ModSetting.cs:289-292
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Glade: key action lookup '{name}' failed (that key is inert): {e.Message}");
                return null;
            }
        }

        private void SetActionsEnabled(bool enabled)
        {
            // Not built-in, so the setter cannot throw (ProxyAction.cs:334-337)
            for (int i = 0; i < SlotCount; i++)
            {
                if (m_Slots[i] != null)
                    m_Slots[i].shouldBeEnabled = enabled;
            }
        }

        private void SafeDisableActions()
        {
            try { SetActionsEnabled(false); }
            catch (Exception) { /*already in the failure path. The "!!!" line carries the cause*/ }
        }

        private string DescribeMissing()
        {
            var sb = new StringBuilder(64);
            for (int i = 0; i < SlotDump; i++)
            {
                if (m_Slots[i] != null)
                    continue;
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append(SlotNames[i]);
            }
            return sb.Length == 0 ? "none missing" : "missing " + sb;
        }


        /// <summary>Per-frame watcher, re-arming (see engine notes, Probe): four bool reads per frame. The watcher counts every true -> false
        /// of an armed action's `enabled` (WatcherDrops). It logs the drop with the active tool's id and a cause label read at that moment,
        /// at most Tuning.WatcherLinesPerAction lines per action and label per load. Then it writes one "further drops not logged" line.
        /// Each logged drop arms one "live again" line. Actions that change on the same frame share one line (all at once is the signature
        /// of a keyboard mask rather than a key conflict). No allocation unless a line is written.</summary>
        private void WatchActions()
        {
            // Read once per frame only when something dropped (two public getters, InputManager.cs:325, :341). They serve as labels, never as filters
            int dropped = 0, capped = 0, back = 0;   // slot bitmasks of this frame's logged transitions
            int cause = -1;
            for (int i = 0; i < SlotCount; i++)
            {
                ProxyAction a = m_Slots[i];
                if (a == null || (i == SlotDump ? !m_DumpArmed : !m_GladeArmed))
                    continue;
                bool live = a.enabled;   // ProxyAction.cs:303-307 (the Unity source action's state)
                if (m_WasLive[i] && !live)
                {
                    WatcherDrops++;
                    if (cause < 0)
                    {
                        InputManager im = InputManager.instance;
                        cause = im != null && (im.hasInputFieldFocus || im.overlayActive) ? CauseMask : CauseOther;
                    }
                    int k = i * CauseCount + cause;
                    if (m_DropLines[k] < Tuning.WatcherLinesPerAction)
                    {
                        m_DropLines[k]++;
                        m_BackPending[i] = true;
                        dropped |= 1 << i;
                    }
                    else if (!m_DropCapNoted[k])
                    {
                        m_DropCapNoted[k] = true;
                        capped |= 1 << i;
                    }
                }
                else if (!m_WasLive[i] && live && m_BackPending[i])
                {
                    m_BackPending[i] = false;
                    back |= 1 << i;
                }
                m_WasLive[i] = live;
            }
            if (dropped != 0)
            {
                string names = SlotList(dropped);
                string tool = ActiveToolId();
                if (cause == CauseMask)
                {
                    Mod.Log.Info($"Glade: hotkey watcher — runtime: {names} turned NOT live in tool '{tool}' while a " +
                                 "text field has focus or an overlay is up — the keyboard mask (InputManager.cs:644-652), " +
                                 "harmless while it lasts; a 'live again' line follows when it ends " +
                                 $"(≤ {Tuning.WatcherLinesPerAction} such lines per action per load)");
                }
                else
                {
                    Mod.Log.Info($"Glade: hotkey watcher — runtime: {names} turned NOT live in tool '{tool}' with no " +
                                 "text field focus and no overlay — probably a key conflict lost to an action enabled " +
                                 "in that tool (InputConflictResolution.cs:138-198), or a mask this mod cannot read " +
                                 "(key-rebinding capture, a UI input barrier); a 'live again' line follows when it " +
                                 $"returns (≤ {Tuning.WatcherLinesPerAction} such lines per action per load)");
                }
                StatusLine = "armed — " + names + " went NOT live in tool '" + tool + "' (" +
                             (cause == CauseMask ? "text field / overlay mask" : "probable key conflict") + "; see mod log)";
            }
            if (capped != 0)
            {
                Mod.Log.Info($"Glade: hotkey watcher — {SlotList(capped)}: further drops " +
                             (cause == CauseMask ? "under a text field / overlay mask" : "without a text field / overlay") +
                             $" not logged this load (WatcherLinesPerAction = {Tuning.WatcherLinesPerAction}; " +
                             "the dev tab's hotkey row still shows the current state)");
                StatusLine = "armed — " + SlotList(capped) + " NOT live in tool '" + ActiveToolId() + "' (" +
                             (cause == CauseMask ? "text field / overlay mask" : "probable key conflict") +
                             "; further drops not logged)";
            }
            if (back != 0)
            {
                Mod.Log.Info($"Glade: hotkey watcher — runtime: {SlotList(back)} live again (tool '{ActiveToolId()}')");
                bool anyDown = false;
                for (int i = 0; i < SlotCount; i++)
                {
                    if (m_Slots[i] != null && (i == SlotDump ? m_DumpArmed : m_GladeArmed) && !m_WasLive[i])
                        anyDown = true;
                }
                if (!anyDown)
                    StatusLine = "armed — " + DescribeBindings(withState: true) + "; " + WatcherDrops + " drop(s) this load";
            }
        }

        private string SlotList(int mask)
        {
            var sb = new StringBuilder(96);
            for (int i = 0; i < SlotCount; i++)
            {
                if ((mask & (1 << i)) == 0)
                    continue;
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append(SlotNames[i]).Append(" (").Append(m_Slots[i] == null ? "?" : FormatBinding(m_Slots[i])).Append(')');
            }
            return sb.ToString();
        }

        /// <summary>One-shot, ProbeDelayFrames after arming. Runtime: a false `enabled` while shouldBeEnabled is true means the conflict
        /// pass disabled it, or the keyboard is masked right now (WARN). Options marker (INFO): every binding's hasConflicts and each
        /// conflicting map/action.</summary>
        private void ProbeConflicts()
        {
            string line = DescribeBindings(withState: true);
            StatusLine = "armed — " + line;
            bool anyOff = false;
            for (int i = 0; i < SlotCount; i++)
            {
                ProxyAction a = m_Slots[i];
                if (a != null && (i == SlotDump ? m_DumpArmed : m_GladeArmed) && !a.enabled)
                    anyOff = true;
            }
            if (anyOff)
            {
                Mod.Log.Warn($"Glade: hotkey probe — runtime: {line} (tool '{ActiveToolId()}') — an action that " +
                             "should be live is NOT enabled. Most likely it lost a key conflict to an enabled vanilla " +
                             "action (InputConflictResolution.cs:138-198), or a text field/overlay masked the keyboard " +
                             "at probe time (InputManager.cs:644-652). Rebind under Options → Modular Ski Resort → Key " +
                             "bindings.");
            }
            else
            {
                Mod.Log.Info($"Glade: hotkey probe — runtime: {line} (tool '{ActiveToolId()}')");
            }
            Mod.Log.Info("Glade: hotkey probe — options marker (ProxyBinding.hasConflicts, checkUsage true, any " +
                         "enabled state — the options row's triangle; may disagree with the runtime state above): " +
                         DescribeOptionsMarkers());
        }

        /// <summary>Per armed action: "none" or the conflicting bindings as map/action [built-in|mod]. ProxyBinding.conflicts allocates a
        /// list per call (probe only).</summary>
        private string DescribeOptionsMarkers()
        {
            var sb = new StringBuilder(256);
            for (int i = 0; i < SlotCount; i++)
            {
                ProxyAction a = m_Slots[i];
                if (i > 0)
                    sb.Append("; ");
                sb.Append(SlotNames[i]).Append(": ");
                if (a == null)
                {
                    sb.Append("MISSING");
                    continue;
                }
                bool any = false;
                foreach (ProxyBinding b in a.bindings)
                {
                    if (!b.isSet)
                        continue;
                    ProxyBinding.ConflictType type = b.hasConflicts;   // ProxyBinding.cs:414-456
                    if (type == ProxyBinding.ConflictType.None)
                        continue;
                    any = true;
                    sb.Append(type).Append(" with ");
                    IList<ProxyBinding> conflicts = b.conflicts;      // ProxyBinding.cs:458-501
                    for (int c = 0; c < conflicts.Count; c++)
                    {
                        ProxyBinding o = conflicts[c];
                        if (c > 0)
                            sb.Append(", ");
                        sb.Append(o.mapName).Append('/').Append(o.actionName)
                          .Append(o.isBuiltIn ? " [built-in]" : " [mod]");
                    }
                }
                if (!any)
                    sb.Append("none");
            }
            return sb.ToString();
        }

        private string DescribeBindings(bool withState)
        {
            var sb = new StringBuilder(200);
            for (int i = 0; i < SlotCount; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                AppendAction(sb, SlotNames[i], m_Slots[i], withState);
            }
            return sb.ToString();
        }

        private static void AppendAction(StringBuilder sb, string name, ProxyAction action, bool withState)
        {
            sb.Append(name).Append('=');
            if (action == null)
            {
                sb.Append("MISSING");
                return;
            }
            sb.Append(FormatBinding(action));
            if (withState)
                sb.Append(action.enabled ? " [live]" : " [NOT live]");
        }

        /// <summary>"ctrl+g"-style text from the action's current (possibly rebound) bindings (ProxyAction.bindings, ProxyAction.cs:204-217).
        /// Hand-formatted: ProxyBinding.ToString dereferences m_Modifiers without a null check (ProxyBinding.cs:773-776).</summary>
        private static string FormatBinding(ProxyAction action)
        {
            foreach (ProxyBinding b in action.bindings)
            {
                if (!b.isSet)
                    continue;
                var sb = new StringBuilder(24);
                IReadOnlyList<ProxyModifier> mods = b.modifiers;   // null-safe getter, :518-528
                for (int i = 0; i < mods.Count; i++)
                    sb.Append(StripDevice(mods[i].m_Path)).Append('+');
                sb.Append(StripDevice(b.path));
                return sb.ToString();
            }
            return "unbound";
        }

        private static string StripDevice(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "?";
            int slash = path.LastIndexOf('/');
            return slash >= 0 && slash < path.Length - 1 ? path.Substring(slash + 1) : path;
        }
    }
}
