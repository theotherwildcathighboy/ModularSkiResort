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
using System.Globalization;
using Colossal.Serialization.Entities;
using Game;

namespace SkiResortMod.Systems
{
    public sealed partial class ResortLiftBuildSystem : GameSystemBase
    {
        private readonly ResortPlanSheet m_Sheet = new ResortPlanSheet();
        private ResortPlan m_SheetPlan;   // the published plan the rows came from (reference compare, one immutable snapshot per publish)
        private bool m_Armed, m_Disabled, m_ErrorLogged;
        private LiftCensusSystem m_CensusSystem;
        private int m_HandSheetRev = -1, m_HandCensusRev = -1;   // the last sheet and census revisions the hand-built match looked at
        private string m_Notice;
        private int m_NoticeFrame;
        private const int NoticeFrames = 420;

        /// <summary>The plan sheet. Read API for the hub: Rows, Revision, PlanSerial (EngineNotes/StationPlaceToolSystem.md).</summary>
        internal ResortPlanSheet Sheet => m_Sheet;

        internal bool IsArmed => m_Armed && !m_Disabled;

        protected override void OnCreate()
        {
            base.OnCreate();
            CreateQueue();
            m_CensusSystem = World.GetOrCreateSystemManaged<LiftCensusSystem>();
            Mod.Log.Info($"{nameof(ResortLiftBuildSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Disabled = m_ErrorLogged = false;
            m_Sheet.Clear("load");
            m_SheetPlan = null;
            ResetQueue();
            m_Notice = null;
            m_HandSheetRev = m_HandCensusRev = -1;
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

        private void Tick()
        {
            TickQueue();
            TickHandBuilt();
        }

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

        // ---------------------------------------------------------------- a line the player drew by hand

        /// <summary>A row whose stations stand without a line (StationsPlaced, NeedsFitting, or a Refused row that kept both stations) becomes Built when a complete lift of its type
        /// stands between its two stations, whoever drew the line (the player in the lift tool, not the queue). The lift in flight is left to
        /// its own job. Cheap: a pass runs only when the sheet or the census changed (two int compares per frame otherwise), and looks only at
        /// rows without a line.</summary>
        private void TickHandBuilt()
        {
            if (m_CensusSystem == null || !m_CensusSystem.IsArmed || m_Sheet.Count == 0)
                return;
            int sheetRev = m_Sheet.Revision, censusRev = m_CensusSystem.Revision;
            if (sheetRev == m_HandSheetRev && censusRev == m_HandCensusRev)
                return;
            m_HandSheetRev = sheetRev;
            m_HandCensusRev = censusRev;
            IReadOnlyList<LiftCensus.Row> census = null;
            uint inFlight = InFlightKey;
            IReadOnlyList<ResortPlanSheet.Row> rows = m_Sheet.Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                ResortPlanSheet.Row r = rows[i];
                if (r.State == SheetLiftState.Built || r.State == SheetLiftState.Proposed || r.Key == inFlight ||
                    r.BaseStation.IsNull || r.TopStation.IsNull)
                    continue;
                census = census ?? m_CensusSystem.CensusRows;
                if (!ResortPlanSheet.HasCompleteLiftBetween(census, r.BaseStation, r.TopStation, r.TypeIndex))
                    continue;
                m_Sheet.SetState(r.Key, SheetLiftState.Built, null);
                DropWatch(r.Key);
                Mod.Log.Info($"ResortLifts [sheet]: {r.Name} BUILT by hand — a complete lift of its type stands between its two stations " +
                             $"(base {Fmt(Alive(r.BaseStation))}, top {Fmt(Alive(r.TopStation))})");
            }
        }

        // ---------------------------------------------------------------- the hub's calls

        /// <summary>The hub's notice about the lift calls (a locale key, null when none). It ends after NoticeFrames frames.</summary>
        internal string Notice => m_Notice != null && m_Frame - m_NoticeFrame < NoticeFrames ? m_Notice : null;

        private void SayNotice(string name)
        {
            m_Notice = "SkiResort.Tooltip.QUICK_LIFTS_NOTICE[" + name + "]";
            m_NoticeFrame = m_Frame;
            m_QueueRev++;
        }

        /// <summary>The hub's four lift calls (SkiResortHubUISystem quickLiftsAct): "buildAll", "build" (key), "stop", "clear". Main thread, in
        /// the hub's update: they only queue keys or empty the sheet, never create an entity or a command buffer. A call the queue cannot take
        /// gets a notice and a log line. <paramref name="plannerBusy"/>: the Resort Planner tool has a scan, a preview or a build in progress.
        /// Building a lift then would hand the tool to the station placer and close the planner, and Clear would unhook the preview's
        /// toggles. Returns a short text for the hub's trigger line.</summary>
        internal string HubAct(string action, uint key, bool plannerBusy)
        {
            string result = HubActCore(action, key, plannerBusy);
            Mod.Log.Info($"ResortLifts [hub]: {action}{(action == "build" ? " " + key.ToString("X8", CultureInfo.InvariantCulture) : string.Empty)} — {result}");
            return result;
        }

        private string HubActCore(string action, uint key, bool plannerBusy)
        {
            const string PlannerBusy = "refused: the planner has a scan, a preview or a build in progress";
            if (!IsArmed)
            {
                SayNotice("Off");
                return "refused: the lift build is not armed (not in a game, or disabled after an error)";
            }
            switch (action)
            {
                case "buildAll":
                    if (plannerBusy)
                    {
                        SayNotice("RunsBusy");
                        return PlannerBusy;
                    }
                    if (m_Sheet.BuildAllKeys().Count == 0)
                    {
                        SayNotice("Nothing");
                        return "refused: no lift is switched on and unbuilt";
                    }
                    if (BuildAll("hub") == 0)
                    {
                        SayNotice("Queued");
                        return "refused: every such lift is already queued or in flight";
                    }
                    return "queued";
                case "build":
                    if (plannerBusy)
                    {
                        SayNotice("RunsBusy");
                        return PlannerBusy;
                    }
                    if (!BuildOne(key, "hub"))
                    {
                        ResortPlanSheet.Row r = m_Sheet.Find(key);
                        SayNotice(r == null || !r.Buildable ? "Nothing" : "Queued");
                        return r == null ? "refused: no such lift on the sheet" : !r.Buildable ? "refused: the lift is built" : "refused: the lift is already queued or in flight";
                    }
                    return "queued";
                case "stop":
                    return Stop("hub") > 0 || QueueBusy ? "stopping" : "idle";
                case "clear":
                    if (QueueBusy || QueueRunning)
                    {
                        SayNotice("Busy");
                        return "refused: lifts are being built";
                    }
                    if (plannerBusy)
                    {
                        SayNotice("RunsBusy");
                        return PlannerBusy;
                    }
                    ClearSheet("hub");
                    return "cleared";
                default:
                    Mod.Log.Warn($"ResortLifts [hub]: unknown action '{action}' — ignored (contract drift?)");
                    return "unknown action";
            }
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
