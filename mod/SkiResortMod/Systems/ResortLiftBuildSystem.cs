// The Resort Planner's lift side (task R1-c, plan section 3): the owner of the plan sheet (ResortPlanSheet) and the lift build queue.
// The planner tool loads the sheet when it publishes a plan (QuickRunsToolSystem.FinishScan) and keeps the rows' toggles in step
// (TogglePlanKey). The sheet outlives the run build and the planner tool. A new published plan replaces it, ClearSheet and a load empty
// it. Session only.
// It runs at UIUpdate (SystemOrder.cs:67), every frame, paused or not, whatever tool is active: the queue's steps run while the station
// placer or the lift-line tool is the active tool (an inactive tool never ticks, ToolSystem.cs:312-327).
// The build queue (ResortLiftBuildSystem.Queue.cs) ticks here.
// Logs (silence impossible): one line per load and clear of the sheet, the queue's lines (Queue.cs). An exception disables the system until
// the next load.
// Engine notes: EngineNotes/StationPlaceToolSystem.md (Plan sheet, Build queue)

using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;

namespace SkiResortMod.Systems
{
    public sealed partial class ResortLiftBuildSystem : GameSystemBase
    {
        private readonly ResortPlanSheet m_Sheet = new ResortPlanSheet();
        private ResortPlan m_SheetPlan;   // the published plan the rows came from (reference compare, one immutable snapshot per publish)
        private bool m_Armed, m_Disabled, m_ErrorLogged;

        /// <summary>The plan sheet. Read API for the hub: Rows, Revision, PlanSerial (EngineNotes/StationPlaceToolSystem.md).</summary>
        internal ResortPlanSheet Sheet => m_Sheet;

        internal bool IsArmed => m_Armed && !m_Disabled;

        protected override void OnCreate()
        {
            base.OnCreate();
            CreateQueue();
            Mod.Log.Info($"{nameof(ResortLiftBuildSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Disabled = m_ErrorLogged = false;
            m_Sheet.Clear("load");
            m_SheetPlan = null;
            ResetQueue();
            m_Armed = mode == GameMode.Game;
            Mod.Log.Info(m_Armed
                ? "ResortLifts [sheet]: ARMED — the plan sheet is empty; a published Resort Planner plan fills it, a new plan replaces it"
                : $"ResortLifts [sheet]: NOT ARMED (mode {mode})");
        }

        protected override void OnUpdate()
        {
            if (m_Disabled || !m_Armed)
                return;
            try
            {
                Tick();
            }
            catch (Exception e)
            {
                Fail(e, "OnUpdate");
            }
        }

        private void Tick() => TickQueue();

        // ---------------------------------------------------------------- the sheet

        /// <summary>A plan was published (QuickRunsToolSystem.FinishScan): the sheet takes its proposed lifts. A plan without lifts, or a
        /// failed scan (plan null), empties it.</summary>
        internal void LoadFromPlan(ResortPlan plan, IReadOnlyList<ResortPlanSheet.Input> lifts, string why)
        {
            if (!m_Armed)
                return;
            if (plan == null || lifts == null || lifts.Count == 0)
            {
                m_Sheet.Clear(why);
                m_SheetPlan = null;
                Mod.Log.Info($"ResortLifts [sheet]: CLEARED ({why}) — the published plan has no proposed lift");
                return;
            }
            OnSheetReplaced(why);
            m_Sheet.Load(lifts, why);
            m_SheetPlan = plan;
            Mod.Log.Info($"ResortLifts [sheet]: LOADED ({why}) — {m_Sheet.Dump()}");
        }

        /// <summary>The planner's toggles for the rows of the plan the sheet came from (a toggle of another plan is ignored).</summary>
        internal void SyncToggles(ResortPlan plan, IReadOnlyList<ResortPlanSheet.Input> lifts)
        {
            if (!m_Armed || plan == null || !ReferenceEquals(plan, m_SheetPlan) || lifts == null)
                return;
            int changed = 0;
            foreach (ResortPlanSheet.Input l in lifts)
                if (m_Sheet.SetOn(l.Key, l.On))
                    changed++;
            if (changed > 0)
                Mod.Log.Info($"ResortLifts [sheet]: {changed} lift toggle(s) synced from the planner; {m_Sheet.BuildAllKeys().Count} lift(s) on " +
                             $"and not built, cost to build {m_Sheet.CostToBuild()}");
        }

        /// <summary>The hub's "Clear" (a later task) and the dev trigger: the sheet empties.</summary>
        internal void ClearSheet(string why)
        {
            if (!m_Armed)
                return;
            OnSheetReplaced(why);
            m_Sheet.Clear(why);
            m_SheetPlan = null;
            Mod.Log.Info($"ResortLifts [sheet]: CLEARED ({why})");
        }

        /// <summary>The dev trigger's dump: every row in one log line.</summary>
        internal void DumpSheet(string why) => Mod.Log.Info($"ResortLifts [sheet]: DUMP ({why}) — {m_Sheet.Dump()}");

        /// <summary>The rows are about to be replaced or emptied: the queue stops (the lift in flight finishes and writes nothing into the
        /// new rows).</summary>
        private void OnSheetReplaced(string why)
        {
            if (QueueBusy)
                Stop("the sheet is replaced: " + why);
        }

        private void Fail(Exception e, string where)
        {
            m_Disabled = true;
            if (!m_ErrorLogged)
            {
                m_ErrorLogged = true;
                Mod.Log.Error($"!!! {nameof(ResortLiftBuildSystem)} EXCEPTION in {where} — the planner's lift build is disabled until the next " +
                              $"game load. Please report this stack:\n{e}");
            }
        }
    }
}
