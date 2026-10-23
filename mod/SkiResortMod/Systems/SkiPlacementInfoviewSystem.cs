// Closes the Electricity infoview that the vanilla net tool opens when the player starts drawing one of our lift LINES (every
// found SkiCatalog.Lifts row with CloseElectricityView), once per tool activation, and leaves it open when the player had it
// open before. The base station keeps its view: its low-voltage feed is run there (LocaleEN.cs, the base station's description).
// Code, not prefab data: no prefab field names the placement infoview (InfoviewInitializeSystem.cs:277-428, :601-656).
// Phase (Mod.cs): UpdateBefore<SkiPlacementInfoviewSystem, Game.UI.InGame.InfoviewsUISystem>(UIUpdate) only; adding UpdateAt as
// well would register the system twice (UpdateSystem.cs:158-176 -> Register :271-292 does not de-duplicate).
// Engine notes: EngineNotes/SkiPlacementInfoviewSystem.md

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

        // The rule's state (Engine notes: Rule).
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

        /// <summary>Activations whose view we closed / kept, and views we reopened after a direct tool switch (Engine notes: Restore).</summary>
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

                // Who opened the current view (Engine notes: Rule). Reference compares only.
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

        /// <summary>One close / keep line, capped per load (Engine notes: Logs).</summary>
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
