// Closes the Electricity infoview that the vanilla net tool opens when the player starts drawing one of our lift LINES (every
// found SkiCatalog.Lifts row with CloseElectricityView), once per tool activation, and leaves it open when the player had it
// open before. Targets come from SkiCatalog and their PrefabBase from SkiPrefabCatalogSystem (PrefabID fast path, then its one
// name scan). `!!! Infoview: ... not found` only for an expected line (LiftTypeDef.LineExpected) that is missing; pending lines
// (not imported yet) are listed in the ARMED line ("pending: ..."). The base station keeps its view: its low-voltage feed is
// run there (LocaleEN.cs, the base station's description).
// Engine citations are `Game.X/File.cs:line` in tools/decompile/1.6.0f1; "1.6.2 :N" marks a shift in the installed 1.6.2f1
// tree, unmarked = identical in 1.6.2.
//
// Why code, not prefab data: no prefab field names the placement infoview. InfoviewInitializeSystem scores every infoview per
// placeable prefab and writes the winners into its PlaceableInfoviewItem buffer (InfoviewInitializeSystem.cs:277-428,
// :601-656); the lift scores Electricity through PowerLineData (:410-415, the lift is a power line), the base station through
// its Transformer (:290, the lift's power door). Both causes are load-bearing.
//
// Mechanism:
//   * A tool's infoview is element [0] of its prefab's PlaceableInfoviewItem buffer, written into ToolBaseSystem.infoview by
//     UpdateInfoview (ToolBaseSystem.cs:176, :477-504), which the net tool calls from its OnUpdate every frame
//     (NetToolSystem.cs:6045, 1.6.2 :6183).
//   * ToolSystem.ToolUpdate applies it only when the active tool's infoview differs from the last one it applied
//     (m_LastToolInfoview, ToolSystem.cs:321-342); SetInfoview (:377-399) replaces the view. ToolSystem runs in MainLoop before
//     UIUpdate (SystemOrder.cs:58, :67), so at our UIUpdate the tool's view of this frame is already applied.
//   * The public setter ToolSystem.infoview (:118-131) is the vanilla infoview menu's own call (InfoviewsUISystem.SetActiveInfoview,
//     InfoviewsUISystem.cs:431-439). Writing null closes the view; the next ToolUpdate does not reopen it, because the tool's
//     infoview still equals m_LastToolInfoview (:333) and the re-apply arm needs activeInfoview == the tool's view (:343). A
//     tool switch (bulldozer and back) makes the tool's view differ once and reopens it; we then close it again (a new
//     activation, the latch is cleared).
//   * activePrefab = m_ActiveTool.GetPrefab() (ToolSystem.cs:116; NetToolSystem.GetPrefab :5548-5555, 1.6.2 :5676). We call
//     activeTool.GetPrefab() ourselves so a null activeTool cannot throw. activeInfoview (ToolSystem.cs:133-143) is null for an
//     invalid view.
//
// Rule: on an activePrefab change, clear the latch; playerHadIt = the view at our previous update equaled the new tool's
// infoview and that view was not opened by a tool. Then, when the prefab is a target, activeInfoview != null, activeInfoview ==
// activeTool.infoview and the latch is clear: playerHadIt latches and counts Kept ("Infoview [keep]"); otherwise
// ToolSystem.infoview = null, latch, Closed++ ("Infoview [close]"). Re-opening the view by hand during the same activation is kept.
// "The view equaled the tool's at the previous update" is also true when the previous tool opened it (the base station tool opens
// Electricity, :290; the order base -> line is common), so we track who opened the current view: a view change seen in the same
// update as a tool/prefab change, equal to the tool's own infoview, is tool-opened; any other view change is the player's.
// Cost: a one-frame Electricity overlay flash per activation (colors recompute in the next ToolUpdate, ToolSystem.cs:369-373);
// unverified in game.
//
// Restore: our close goes through the public setter, which changes only m_CurrentInfoview (SetInfoview, ToolSystem.cs:377-399);
// ToolSystem's private m_LastToolInfoview keeps Electricity (it is written only inside ToolUpdate's
// `infoviewPrefab != m_LastToolInfoview` arm, :333-336, and the re-apply arm needs activeInfoview == the tool's view, null after
// our close). So a direct switch from the lift tool to another tool with the same Electricity view (the hub's Base station tile:
// ToolSystem.ActivatePrefabTool switches net tool -> object tool with no default tool between, :263-277; or the vanilla
// low-voltage line) would leave that tool without its view. m_ClosedView = the view we closed; it is cleared as soon as the
// active tool's view differs from it (the cache moved on: first arm, :333-336) or any view is open again. While it is set, when
// the active prefab is not a target, the tool's own view is that closed view and no view is open, we reopen it with the same
// public setter, latch it as tool-opened, log one capped `Infoview [restore]` line and clear m_ClosedView. The setter passes no
// infomode list (SetInfoview(value, null)), so every optional infomode is on and the tool's supplemental ones are not (:388), as
// when the vanilla infoview menu opens a view (InfoviewsUISystem.cs:431-439); the next ToolUpdate's re-apply arm (:343-367)
// reconciles them. Not headless-testable (four reference compares on game objects); needs a field test: lift line -> Base
// station tile directly -> Electricity is open.
//
// Phase (Mod.cs): UpdateBefore<SkiPlacementInfoviewSystem, Game.UI.InGame.InfoviewsUISystem>(UIUpdate) only; adding UpdateAt as
// well would register the system twice and run it twice per frame (UpdateSystem.cs:158-176 -> Register :271-292 appends one
// SystemData per call, no de-duplication). InfoviewsUISystem is UIUpdate (SystemOrder.cs:939, 1.6.2 :940), so the vanilla
// infoview binding sees our close in the same frame (its OnInfoviewChanged handler, InfoviewsUISystem.cs:138, :174-177). UIUpdate
// runs every frame, paused included (SystemOrder.cs:67).
//
// Logs: "created" line; one "Infoview: ARMED" line per load naming the targets and every constant; a one-shot "!!!" catch that
// disables the system until the next load; a "!!!" line when the target prefab is missing (the view then stays open while
// drawing lifts). At most one line per tool activation or restore, capped per load (LogLinesMax) with one "cap reached" line, so
// a long bulldoze/redraw session cannot flood. Game mode only; per frame: three property reads and a few reference compares.

using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Prefabs;
using Game.Tools;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiPlacementInfoviewSystem : GameSystemBase
    {
        private static class Tuning
        {
            /// <summary>Close / keep lines written per load; further ones are only counted (dev tab / dump). One line per lift-tool
            /// activation is a human-rate event.</summary>
            public const int LogLinesMax = 64;
        }

        // The targets are the lift LINES of SkiCatalog.Lifts with CloseElectricityView; their type is SkiCatalog.TypeLine.

        private ToolSystem m_ToolSystem;
        private SkiPrefabCatalogSystem m_PrefabCatalog;

        // Resolved once per load (OnGameLoadingComplete): a list compared by reference in IsTarget; never holds null.
        private readonly List<PrefabBase> m_Targets = new List<PrefabBase>(2);
        private string m_TargetNames = "-";

        // The rule's state (header "Rule").
        private PrefabBase m_LastPrefab;
        private InfoviewPrefab m_PrevView;
        private bool m_PrevViewByTool;
        private ToolBaseSystem m_LastTool;
        private bool m_Latched;
        private bool m_PlayerHadIt;
        private InfoviewPrefab m_ClosedView;   // the view we closed, until the cache moves on

        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;
        private int m_LogLines;
        private bool m_CapLogged;

        /// <summary>Activations whose view we closed / kept, and views we reopened after a direct tool switch (header "Restore").</summary>
        public int Closed, Kept, Restored;

        /// <summary>The last action, written on events only.</summary>
        public string LastAction = "-";

        public bool IsArmed => m_Armed;
        public bool IsDisabled => m_Disabled;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            Mod.Log.Info($"{nameof(SkiPlacementInfoviewSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_Targets.Clear();
            m_TargetNames = "-";
            m_LastPrefab = null;
            m_PrevView = null;
            m_PrevViewByTool = false;
            m_LastTool = null;
            m_Latched = false;
            m_PlayerHadIt = false;
            m_ClosedView = null;
            m_LogLines = 0;
            m_CapLogged = false;
            Closed = 0;
            Kept = 0;
            Restored = 0;
            LastAction = "-";
            if (mode != GameMode.Game)
                return;
            try
            {
                // Every found lift line with CloseElectricityView, from the catalog (null when missing: the "!!!" below). A pending line
                // (not imported yet) that is missing is listed in the ARMED line ("pending: ..."), never "!!!".
                var wanted = new List<string>(4);
                var found = new List<string>(4);
                var missing = new List<string>(4);
                var pending = new List<string>(4);
                for (int l = 0; l < SkiCatalog.Lifts.Count; l++)
                {
                    LiftTypeDef def = SkiCatalog.Lifts[l];
                    if (!def.CloseElectricityView)
                        continue;
                    PrefabBase target = m_PrefabCatalog.LinePrefabBase(def);
                    if (target == null)
                    {
                        if (def.LineExpected)
                        {
                            wanted.Add(def.LinePrefab);
                            missing.Add(def.LinePrefab);
                        }
                        else
                        {
                            pending.Add(def.LinePrefab);
                        }
                        continue;
                    }
                    wanted.Add(def.LinePrefab);
                    if (!m_Targets.Contains(target))
                        m_Targets.Add(target);
                    found.Add(def.LinePrefab);
                }
                if (m_Targets.Count == 0)
                {
                    m_Disabled = true;
                    LastAction = "DISABLED: target prefab missing";
                    string names = wanted.Count > 0 ? string.Join(", ", wanted.ToArray())
                                                    : "(no expected SkiCatalog lift line with CloseElectricityView)";
                    Mod.Log.Error($"!!! Infoview: {names} not found — the Electricity view will stay open " +
                                  "while drawing lifts (asset pack missing?)" +
                                  (pending.Count > 0 ? $"; pending (not imported yet): {string.Join(", ", pending.ToArray())}" : ""));
                    return;
                }
                if (missing.Count > 0)
                    Mod.Log.Error($"!!! Infoview: {string.Join(", ", missing.ToArray())} not found — the Electricity view " +
                                  "will stay open while drawing that lift line (asset pack missing?); the other target(s) stay armed");
                m_TargetNames = string.Join(", ", found.ToArray());
                m_Armed = true;
                Mod.Log.Info($"Infoview: ARMED — targets {TargetsText(found)} only; pending: " +
                             $"{(pending.Count > 0 ? string.Join(", ", pending.ToArray()) : "none")} (OD-11 (a): the base " +
                             "station keeps the Electricity view for its low-voltage feed); on each activation of the lift " +
                             "tool the view it opened is closed once (ToolSystem.infoview = null), unless the player had it " +
                             "open before (kept); re-opening it by hand is kept until the next activation; a view we closed " +
                             "is reopened when the player switches straight to another tool whose own view it is (the base " +
                             "station, the vanilla low-voltage line — ToolSystem's cache would not); UIUpdate before " +
                             $"InfoviewsUISystem; LogLinesMax={Tuning.LogLinesMax}");
            }
            catch (Exception e)
            {
                HandleException(e, "OnGameLoadingComplete");
            }
        }

        protected override void OnUpdate()
        {
            if (m_Disabled || !m_Armed)
                return;
            try
            {
                ToolBaseSystem tool = m_ToolSystem.activeTool;                       // ToolSystem.cs:84-99
                PrefabBase prefab = tool != null ? tool.GetPrefab() : null;          // = activePrefab (:116)
                InfoviewPrefab toolView = tool != null ? tool.infoview : null;      // ToolBaseSystem.cs:176
                InfoviewPrefab view = m_ToolSystem.activeInfoview;                  // ToolSystem.cs:133-143
                bool prefabChanged = !ReferenceEquals(prefab, m_LastPrefab);
                bool toolChanged = !ReferenceEquals(tool, m_LastTool);

                // Who opened the current view (header "Rule"). Reference compares only.
                bool viewByTool = m_PrevViewByTool;
                if (!ReferenceEquals(view, m_PrevView))
                    viewByTool = view != null && ReferenceEquals(view, toolView) && (prefabChanged || toolChanged);

                if (prefabChanged)
                {
                    m_Latched = false;
                    m_PlayerHadIt = m_PrevView != null && ReferenceEquals(m_PrevView, toolView) && !m_PrevViewByTool;
                    m_LastPrefab = prefab;
                }
                m_LastTool = tool;

                if (!m_Latched && view != null && ReferenceEquals(view, toolView) && IsTarget(prefab))
                {
                    m_Latched = true;
                    if (m_PlayerHadIt)
                    {
                        Kept++;
                        LastAction = "kept (the player had it open)";
                        Line($"Infoview [keep]: {view.name} was already open when the lift tool started — left open");
                    }
                    else
                    {
                        m_ToolSystem.infoview = null;                                // ToolSystem.cs:118-131
                        Closed++;
                        LastAction = "closed " + view.name;
                        Line($"Infoview [close]: the lift tool opened {view.name} — closed (reopen it from the infoview " +
                             "menu)");
                        m_ClosedView = view;                                      
                        view = m_ToolSystem.activeInfoview;                          // null after the close
                        viewByTool = false;
                    }
                }

                // A direct switch to another tool whose view is the one we closed would otherwise stay without it (ToolSystem.cs:333-343).
                // Reference compares only.
                if (m_ClosedView != null)
                {
                    if (view != null || !ReferenceEquals(toolView, m_ClosedView))
                    {
                        m_ClosedView = null;   // a view is open again, or ToolSystem's cache moved on (:333-336)
                    }
                    else if (!IsTarget(prefab))
                    {
                        InfoviewPrefab restore = m_ClosedView;
                        m_ClosedView = null;
                        m_ToolSystem.infoview = restore;                             // ToolSystem.cs:118-131
                        Restored++;
                        LastAction = "restored " + restore.name;
                        Line($"Infoview [restore]: switched straight from the lift tool to {(prefab != null ? prefab.name : "another tool")} " +
                             $"— reopened {restore.name}, which that tool opens (ToolSystem keeps it closed: its cache still holds it)");
                        view = m_ToolSystem.activeInfoview;
                        viewByTool = view != null;                                   // tool-opened, not the player's
                    }
                }

                m_PrevView = view;
                m_PrevViewByTool = viewByTool;
            }
            catch (Exception e)
            {
                HandleException(e, "OnUpdate");
            }
        }

        /// <summary>Reference compare over the per-load target list. No allocation; null is never a target. Reference compares only (no
        /// UnityEngine.Object == overload).</summary>
        private bool IsTarget(PrefabBase prefab)
        {
            for (int i = 0; i < m_Targets.Count; i++)
            {
                if (ReferenceEquals(prefab, m_Targets[i]))
                    return true;
            }
            return false;
        }

        /// <summary>"SkiChairliftHV (PowerLinePrefab)" per target, joined: the ARMED line's words.</summary>
        private static string TargetsText(List<string> names)
        {
            var parts = new string[names.Count];
            for (int i = 0; i < names.Count; i++)
                parts[i] = names[i] + " (" + SkiCatalog.TypeLine + ")";
            return string.Join(", ", parts);
        }

        /// <summary>One close / keep line, capped per load (header "Logs").</summary>
        private void Line(string text)
        {
            if (m_LogLines < Tuning.LogLinesMax)
            {
                m_LogLines++;
                Mod.Log.Info(text);
                return;
            }
            if (!m_CapLogged)
            {
                m_CapLogged = true;
                Mod.Log.Info($"Infoview: {Tuning.LogLinesMax} close/keep lines this load (LogLinesMax) — further ones are " +
                             "only counted (closed / kept in the DUMP)");
            }
        }

        /// <summary>Dump text (main thread).</summary>
        public string StateText()
        {
            return $"armed {m_Armed}, disabled {m_Disabled}, target {m_TargetNames}, closed {Closed}, " +
                   $"kept {Kept}, restored {Restored}, latched {m_Latched}, pending restore " +
                   $"{(m_ClosedView != null ? m_ClosedView.name : "-")}, last: {LastAction}";
        }

        private void HandleException(Exception e, string where)
        {
            m_Disabled = true;
            LastAction = "DISABLED (exception, see the mod log)";
            if (m_ErrorLogged)
                return;
            m_ErrorLogged = true;
            Mod.Log.Error($"!!! {nameof(SkiPlacementInfoviewSystem)} EXCEPTION in {where} — the lift tool's Electricity " +
                          "view is left to vanilla until the next game load:\n" + e);
        }
    }
}
