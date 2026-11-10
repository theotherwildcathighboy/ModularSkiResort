// The lift build queue of the Resort Planner (task R1-c, plan sections 3.2 and 3.3): builds one lift or all kept lifts of the plan sheet,
// lowest bottom spot first. Per lift:
// 1. Money: the city must pay the lift's estimate less the stations it already has, else Refused MONEY and nothing is placed.
// 2. Pose: StationAlign.PoseLift from the two spots and the type's station rows, else Refused POSE.
// 3. Stations: the missing ones go to the station placer (StationPlaceToolSystem.Enqueue), the base first. The top is asked only after the
//    base stands (ResortPlanSheet.NextStationStep). A refused base places nothing and charges nothing: Refused BASE[error type]. A refused
//    top keeps the base: Refused TOP[error type] with the base in the row. The next lift starts. The queue's stations take the height rule
//    StationHeight (default Fit, StationAlign.StationY: a pad station takes the lowest of three heights and never floats, a slope
//    station takes its origin).
// 4. Settle: the lot cut runs after the station is permanent and the CPU heights lag it, so the queue samples the terrain under both stub
//    tips every SettleEveryFrames and goes on when two samples agree within SettleToleranceM (at least SettleMinFrames, at most
//    SettleMaxFrames with a warning).
// 5. Line: LiftLineToolSystem.TryActivateFor with auto build. Built: the row is Built, the next lift starts. Waiting (the plan is not
//    sound, a notice stands, or the game refused the line): NeedsFitting, the queue stops with the tool open in Edit. Refused (the ends
//    could not be picked): Refused LINE[PICK], the next lift starts. Abandoned (the player took another tool): the queue stops.
// A row that waits in the lift tool stays watched after the queue ends: a later build by the player makes it Built.
// The tool: a built scripted line hands the lift tool back to the default tool (LiftLineToolSystem). When the queue ends, HandToolBack
// gives an idle placer or an idle lift tool back too. A line that waits for the player keeps the lift tool open.
// Stop: the lift in flight finishes, nothing starts after it. A new plan or a clear stops the queue the same way, and the lift in flight
// writes nothing into the new rows (PlanSerial guard).
// Ticks from ResortLiftBuildSystem.OnUpdate (UIUpdate: every frame, whatever tool is active). The entry points only queue keys: the first
// step runs in the next tick, never inside another tool's update.
// Logs (silence impossible): ResortLifts [queue] per start, stop, lift step, refusal and end, ResortLifts [watch] per watched ticket end.
// Engine notes: EngineNotes/StationPlaceToolSystem.md (Build queue)

using System;
using System.Collections.Generic;
using System.Globalization;
using Colossal.Entities;
using Game.City;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Unity.Entities;
using Unity.Mathematics;
using PlaceStatus = SkiResortMod.Systems.StationPlaceToolSystem.PlaceStatus;
using ScriptOutcome = SkiResortMod.Systems.LiftLineToolSystem.ScriptOutcome;
using ScriptTicket = SkiResortMod.Systems.LiftLineToolSystem.ScriptTicket;

namespace SkiResortMod.Systems
{
    public sealed partial class ResortLiftBuildSystem
    {
        /// <summary>Frames between the planner's call and the first step (the run build's last Apply settles first).</summary>
        private const int StartDelayFrames = 5;
        private const int SettleEveryFrames = 10;
        private const int SettleMinFrames = 30;
        private const int SettleMaxFrames = 900;
        private const float SettleToleranceM = 0.05f;
        /// <summary>A scripted line the lift tool never picked within this many frames: the queue stops (the tool did not open).</summary>
        private const int LinePickMaxFrames = 600;

        private enum JobStep : byte { Placing, Settling, Line }

        private sealed class LiftJob
        {
            public uint Key;
            public string Name;
            public int PlanSerial;
            public LiftTypeDef Type;
            public JobStep Step;
            public int StepFrame;
            public string BaseTag, TopTag;
            public int BaseSerial, TopSerial;   // 0 = the station was there before the job (or, for the top, not asked yet)
            public bool BaseHad, TopHad, TopAsked;
            public Entity Base, Top;
            public StationDef TopDef;   // the top's request, made once the base stands
            public float3 TopPosition;
            public quaternion TopRotation;
            public float LastBaseY = float.NaN, LastTopY = float.NaN;
            public int NextSample;
            public ScriptTicket Ticket;
        }

        private sealed class Watch
        {
            public uint Key;
            public string Name;
            public int PlanSerial;
            public ScriptTicket Ticket;
        }

        private readonly List<uint> m_Pending = new List<uint>();
        private readonly List<Watch> m_Watches = new List<Watch>();
        private LiftJob m_Job;
        private int m_Frame, m_StartFrame;
        private int m_QueueBuilt, m_QueueFitting, m_QueueRefused;
        private string m_QueueWhy;
        private bool m_Running;
        // The hub's view of the queue (SkiResortHubUISystem.QuickRuns.cs): lifts asked in this run, lifts finished, a stop asked while a lift
        // is in flight, and a revision that moves with every change the hub shows (no sheet row changes with them).
        private int m_QueueTotal, m_QueueDone, m_QueueRev;
        private bool m_StopAsked;

        private StationPlaceToolSystem m_Placer;
        private LiftLineToolSystem m_LiftTool;
        private SkiPrefabCatalogSystem m_Catalog;
        private CitySystem m_CitySystem;
        private TerrainSystem m_TerrainSystem;

        /// <summary>The queue has a lift in flight or keys waiting.</summary>
        internal bool QueueBusy => m_Job != null || m_Pending.Count > 0;

        /// <summary>The queue is on: a lift in flight, or keys waiting for their turn (a lift may have just finished).</summary>
        internal bool QueueRunning => m_Running;

        /// <summary>Stop was asked while a lift was in flight: it finishes, nothing starts after it.</summary>
        internal bool QueueStopping => m_Running && m_StopAsked;

        /// <summary>Lifts asked in this run (stopped keys stay in it until the run ends) and the 1-based place of the lift in flight or next, 0 when idle.</summary>
        internal int QueueTotal => m_QueueTotal;
        internal int QueueIndex => ResortPlanSheet.QueueIndexOf(m_Running, m_QueueTotal, m_QueueDone, m_StopAsked, m_Job != null);

        /// <summary>Moves with every change of the queue the hub shows (start, step, end, stop, a notice).</summary>
        internal int QueueRevision => m_QueueRev;

        /// <summary>The key of the lift in flight (0 = none) and the keys still to build, for the hub and the dump.</summary>
        internal uint InFlightKey => m_Job?.Key ?? 0;
        internal IReadOnlyList<uint> PendingKeys => m_Pending;

        private void CreateQueue()
        {
            m_Placer = World.GetOrCreateSystemManaged<StationPlaceToolSystem>();
            m_LiftTool = World.GetOrCreateSystemManaged<LiftLineToolSystem>();
            m_Catalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
        }

        private void ResetQueue()
        {
            m_Pending.Clear();
            m_Watches.Clear();
            m_Job = null;
            m_Running = false;
            m_StopAsked = false;
            m_QueueTotal = m_QueueDone = 0;
            m_QueueRev++;
            StationHeight = DefaultStationHeight;
        }

        // ---------------------------------------------------------------- entry points (the planner, the dev triggers, the hub later)

        /// <summary>Build every row switched on and not built, lowest first. Keys already queued or in flight are not added twice. The
        /// first step runs in a later tick. Returns the count of keys added.</summary>
        internal int BuildAll(string why)
        {
            if (!IsArmed)
                return 0;
            List<uint> keys = m_Sheet.BuildAllKeys();
            BeginRunAccounting();
            int added = 0;
            foreach (uint k in keys)
                if (AddPending(k))
                    added++;
            StartQueue(why, added, keys.Count);
            return added;
        }

        /// <summary>Build one row by its key (switched on or not). False when the key is unknown, built, or already queued.</summary>
        internal bool BuildOne(uint key, string why)
        {
            if (!IsArmed)
                return false;
            ResortPlanSheet.Row r = m_Sheet.Find(key);
            if (r == null || !r.Buildable)
            {
                Mod.Log.Warn($"ResortLifts [queue]: build one {key:X8} REFUSED ({why}) — " +
                             (r == null ? "no such row in the sheet" : $"{r.Name} is already built"));
                return false;
            }
            BeginRunAccounting();
            bool added = AddPending(key);
            StartQueue(why, added ? 1 : 0, 1);
            return added;
        }

        /// <summary>The lift in flight finishes, nothing starts after it. Returns the count of keys dropped.</summary>
        internal int Stop(string why)
        {
            int dropped = DropPending();
            if (m_Job == null && dropped == 0)
            {
                Mod.Log.Info($"ResortLifts [queue]: stop ({why}) — the queue is idle");
                return 0;
            }
            if (m_Job != null)
                m_StopAsked = true;
            m_QueueRev++;
            Mod.Log.Info($"ResortLifts [queue]: STOP ({why}) — {dropped} lift(s) dropped; " +
                         (m_Job != null ? $"{m_Job.Name} in flight finishes ({m_Job.Step})" : "nothing in flight"));
            if (m_Job == null)
                EndQueue("stopped");
            return dropped;
        }

        private bool AddPending(uint key)
        {
            if (m_Pending.Contains(key) || (m_Job != null && m_Job.Key == key))
                return false;
            m_Pending.Add(key);
            m_QueueTotal++;
            m_QueueRev++;
            return true;
        }

        /// <summary>The keys waiting leave the queue. The run keeps its total, so the hub reads the place in the build that was asked for
        /// ("Stopping after lift 1 / 3"); the total resets when the next run starts (BeginRunAccounting). Returns how many.</summary>
        private int DropPending()
        {
            int dropped = m_Pending.Count;
            m_Pending.Clear();
            if (dropped > 0)
                m_QueueRev++;
            return dropped;
        }

        /// <summary>A new run of the queue counts from zero. A call while the queue runs adds to the run in progress.</summary>
        private void BeginRunAccounting()
        {
            if (!m_Running)
            {
                m_QueueTotal = m_QueueDone = 0;
                m_StopAsked = false;
            }
        }

        private void StartQueue(string why, int added, int asked)
        {
            if (!m_Running && m_Pending.Count > 0)
            {
                m_Running = true;
                m_QueueBuilt = m_QueueFitting = m_QueueRefused = 0;
                m_QueueWhy = why;
                m_StartFrame = m_Frame + StartDelayFrames;
                m_QueueRev++;
            }
            Mod.Log.Info($"ResortLifts [queue]: BUILD ({why}) — {added} of {asked} lift(s) added; queue {KeysText()}" +
                         (m_Job != null ? $", {m_Job.Name} in flight" : string.Empty) + $"; city money {CityMoney()}");
        }

        // ---------------------------------------------------------------- the tick

        private void TickQueue()
        {
            m_Frame++;
            TickWatches();
            if (m_Job != null)
            {
                StepJob(m_Job);
                return;
            }
            if (!m_Running || m_Frame < m_StartFrame)
                return;
            while (m_Job == null && m_Pending.Count > 0)
            {
                uint key = m_Pending[0];
                m_Pending.RemoveAt(0);
                BeginJob(key);
            }
            if (m_Job == null)
                EndQueue("done");
        }

        private void EndQueue(string how)
        {
            if (!m_Running)
                return;
            m_Running = false;
            m_StopAsked = false;
            m_QueueRev++;
            Mod.Log.Info($"ResortLifts [queue]: END ({how}; {m_QueueWhy}) — this run: {m_QueueBuilt} built, {m_QueueFitting} needs fitting, " +
                         $"{m_QueueRefused} refused; {m_Watches.Count} watched; sheet {m_Sheet.Dump()}");
            HandToolBack();
        }

        /// <summary>The queue ended: the tool it opened goes back to the default tool. Only the station placer with nothing queued, or the
        /// lift-line tool with nothing in hand (LiftLineToolSystem.IdleAfterScript). A line that waits for the player keeps the lift tool
        /// open, and a tool the player took is never touched.</summary>
        private void HandToolBack()
        {
            var tools = World.GetExistingSystemManaged<Game.Tools.ToolSystem>();
            var defaultTool = World.GetExistingSystemManaged<Game.Tools.DefaultToolSystem>();
            if (tools == null || defaultTool == null || tools.activeTool == null || tools.activeTool == defaultTool)
                return;
            bool placerIdle = tools.activeTool == m_Placer && !m_Placer.Busy;
            bool liftIdle = tools.activeTool == m_LiftTool && m_LiftTool.IdleAfterScript;
            if (!placerIdle && !liftIdle)
            {
                Mod.Log.Info($"ResortLifts [queue]: the tool stays ({tools.activeTool.toolID}" +
                             (tools.activeTool == m_LiftTool ? ": a line waits for the player or is in hand" :
                              tools.activeTool == m_Placer ? ": stations are still queued" : ": not a tool the queue opened") + ")");
                return;
            }
            tools.activeTool = defaultTool;
            Mod.Log.Info($"ResortLifts [queue]: the tool goes back to the default tool (was {(placerIdle ? m_Placer.toolID : m_LiftTool.toolID)})");
        }

        // ---------------------------------------------------------------- the station height rule (one switch, compared in game)

        /// <summary>The default height rule of the stations the queue places: Fit (StationAlign.StationY). A pad station takes the lowest
        /// terrain of the lot's front edge, its origin and its line-side edge, so it never floats (DECISIONS 625, 626). A slope station
        /// (DontRaise and DontLower) takes the terrain at its origin, because it does not cut.</summary>
        internal const StationAlign.StationHeightRule DefaultStationHeight = StationAlign.StationHeightRule.Fit;

        /// <summary>The height rule of the stations the queue places. The dev trigger "height vanilla|lowest|fit" switches it so the three
        /// rules can be compared in game. A load resets it to DefaultStationHeight. The dev probe and an explicit y keep the vanilla rule.</summary>
        internal StationAlign.StationHeightRule StationHeight { get; private set; } = DefaultStationHeight;

        internal void SetStationHeight(StationAlign.StationHeightRule rule, string why)
        {
            StationAlign.StationHeightRule old = StationHeight;
            StationHeight = rule;
            Mod.Log.Info($"ResortLifts [queue]: station height rule {old} -> {rule} ({why}); the next station the queue asks uses it");
        }

        // ---------------------------------------------------------------- one lift

        private void BeginJob(uint key)
        {
            ResortPlanSheet.Row r = m_Sheet.Find(key);
            if (r == null || !r.Buildable)
            {
                Mod.Log.Info($"ResortLifts [queue]: {key:X8} skipped — " + (r == null ? "no longer in the sheet" : $"{r.Name} is built"));
                m_QueueDone++;
                m_QueueRev++;
                return;
            }
            LiftTypeDef type = SkiCatalog.LiftByIndex(r.TypeIndex);
            StationDef baseDef = SkiCatalog.StationOf(r.TypeIndex, StationRole.Base);
            StationDef topDef = SkiCatalog.StationOf(r.TypeIndex, StationRole.Top);
            if (type == null || baseDef == null || topDef == null)
            {
                Refuse(r, "PREFAB", $"lift type index {r.TypeIndex} has no catalog row");
                return;
            }
            DropWatch(key);
            Entity baseSt = Alive(r.BaseStation), topSt = Alive(r.TopStation);
            long need = Math.Max(0L, r.Cost - (baseSt != Entity.Null ? type.StationCostBase : 0) - (topSt != Entity.Null ? type.StationCostTop : 0));
            long money = CityMoney();
            if (money < need)
            {
                Refuse(r, "MONEY", $"the lift costs about {need}, the city has {money}; nothing placed");
                return;
            }
            StationAlign.LiftPose pose = StationAlign.PoseLift(r.Bottom.xz, r.Top.xz, baseDef, topDef);
            if (!pose.Valid)
            {
                Refuse(r, "POSE", string.Format(CultureInfo.InvariantCulture, "no station pose: spots {0:F1} m apart", pose.SpotDistanceM));
                return;
            }
            var job = new LiftJob
            {
                Key = key, Name = r.Name, PlanSerial = m_Sheet.PlanSerial, Type = type, Step = JobStep.Placing, StepFrame = m_Frame,
                BaseTag = r.Name + " base", TopTag = r.Name + " top", Base = baseSt, Top = topSt,
                BaseHad = baseSt != Entity.Null, TopHad = topSt != Entity.Null,
                TopDef = topDef, TopPosition = pose.TopPosition, TopRotation = pose.TopRotation,
            };
            Mod.Log.Info(string.Format(CultureInfo.InvariantCulture,
                "ResortLifts [queue]: {0} START — {1}, cost about {2} (city {3}); spots {4:F1} m apart, stub tips {5:F1} m; stations already " +
                "there: base {6}, top {7}; station height rule {8}", r.Name, type.Id, need, money, pose.SpotDistanceM, pose.TipDistanceM,
                Fmt(baseSt), Fmt(topSt), StationHeight));
            // The base first. The top is asked only after the base stands (StepPlacing, ResortPlanSheet.NextStationStep): a refused base
            // places nothing and charges nothing.
            if (baseSt == Entity.Null && !QueueStation(job, baseDef, pose.BasePosition, pose.BaseRotation, job.BaseTag, out job.BaseSerial))
            {
                Refuse(r, "PREFAB", $"the base station {baseDef.Prefab} could not be queued (see the line above)");
                return;
            }
            m_Job = job;
            m_QueueRev++;
            if (job.BaseSerial == 0)
                StepPlacing(job, r);   // the base stands already: ask the top now, or settle when both stand
        }

        private bool QueueStation(LiftJob job, StationDef def, float3 position, quaternion rotation, string tag, out int serial)
        {
            serial = 0;
            Entity prefab = m_Catalog.EntityOf(def.Prefab);
            if (prefab == Entity.Null)
            {
                Mod.Log.Warn($"ResortLifts [queue]: {job.Name} — the station prefab {def.Prefab} is MISSING");
                return false;
            }
            serial = m_Placer.Enqueue("plan " + job.Name, new StationPlaceToolSystem.PlaceRequest
            {
                Prefab = prefab, Position = position, Rotation = rotation, Tag = tag, HeightRule = StationHeight,
            });
            return serial != 0;
        }

        /// <summary>A station request's state for ResortPlanSheet.NextStationStep. A serial of 0 is no request. An outcome that is gone (the
        /// placer keeps the last 64) is None.</summary>
        private ResortPlanSheet.StationOutcome OutcomeOf(int serial, out StationPlaceToolSystem.PlaceOutcome o)
        {
            o = null;
            if (serial == 0 || !m_Placer.TryGetOutcome(serial, out o) || o == null)
                return ResortPlanSheet.StationOutcome.None;
            switch (o.Status)
            {
                case PlaceStatus.Pending: return ResortPlanSheet.StationOutcome.Pending;
                case PlaceStatus.Placed: return o.Placed != Entity.Null ? ResortPlanSheet.StationOutcome.Placed : ResortPlanSheet.StationOutcome.Refused;
                case PlaceStatus.Canceled: return ResortPlanSheet.StationOutcome.Canceled;
                default: return ResortPlanSheet.StationOutcome.Refused;
            }
        }

        private void StepJob(LiftJob job)
        {
            ResortPlanSheet.Row r = job.PlanSerial == m_Sheet.PlanSerial ? m_Sheet.Find(job.Key) : null;
            switch (job.Step)
            {
                case JobStep.Placing:
                    StepPlacing(job, r);
                    break;
                case JobStep.Settling:
                    StepSettling(job, r);
                    break;
                case JobStep.Line:
                    StepLine(job, r);
                    break;
            }
        }

        /// <summary>The stations of one lift, base first (ResortPlanSheet.NextStationStep): the top is asked only once the base stands. So a
        /// refused base places nothing and charges nothing. A refused top leaves the base in the row (plan 3.3).</summary>
        private void StepPlacing(LiftJob job, ResortPlanSheet.Row r)
        {
            ResortPlanSheet.StationOutcome bs = OutcomeOf(job.BaseSerial, out StationPlaceToolSystem.PlaceOutcome bo);
            ResortPlanSheet.StationOutcome ts = OutcomeOf(job.TopSerial, out StationPlaceToolSystem.PlaceOutcome to);
            if (!job.BaseHad && job.Base == Entity.Null && bs == ResortPlanSheet.StationOutcome.Placed)
            {
                job.Base = bo.Placed;
                if (r != null)
                    m_Sheet.SetStations(job.Key, Ref(job.Base), Ref(job.Top));
            }
            if (job.TopAsked && job.Top == Entity.Null && ts == ResortPlanSheet.StationOutcome.Placed)
                job.Top = to.Placed;
            switch (ResortPlanSheet.NextStationStep(job.BaseHad, job.BaseHad ? ResortPlanSheet.StationOutcome.None : bs, job.TopHad, job.TopAsked, ts))
            {
                case ResortPlanSheet.StationStep.Wait:
                    return;
                case ResortPlanSheet.StationStep.AskTop:
                    job.TopAsked = true;
                    if (!QueueStation(job, job.TopDef, job.TopPosition, job.TopRotation, job.TopTag, out job.TopSerial))
                    {
                        EndJob(job, r, SheetLiftState.Refused, "PREFAB",
                               $"the top station {job.TopDef.Prefab} could not be queued (see the line above); the base {Fmt(job.Base)} stays");
                        return;
                    }
                    Mod.Log.Info($"ResortLifts [queue]: {job.Name} the base {Fmt(job.Base)} stands — top station requested (#{job.TopSerial})");
                    return;
                case ResortPlanSheet.StationStep.Settle:
                    if (r != null)
                        m_Sheet.SetStations(job.Key, Ref(job.Base), Ref(job.Top));
                    BeginSettle(job, r);
                    return;
                case ResortPlanSheet.StationStep.PlayerStop:
                    // A placer cancel the queue did not ask for (the player took another tool) stops the queue.
                    EndJob(job, r, r != null && (job.Base != Entity.Null || job.Top != Entity.Null) ? SheetLiftState.Refused : SheetLiftState.Proposed,
                           "STOPPED", "the station placer was closed before both stations stood");
                    StopAfterJob("the station placer was closed");
                    return;
                default:
                    bool baseRefused = job.Base == Entity.Null;
                    StationPlaceToolSystem.PlaceOutcome o = baseRefused ? bo : to;
                    string error = o?.FirstError ?? (o == null ? "LOST" : "GAME");
                    EndJob(job, r, SheetLiftState.Refused, (baseRefused ? "BASE[" : "TOP[") + error + "]",
                           $"the game refused the {(baseRefused ? "base" : "top")} station: {o?.Summary ?? "no outcome"}" +
                           (baseRefused ? (job.Top != Entity.Null ? $"; the top {Fmt(job.Top)} stays" : "; nothing placed, nothing charged")
                                        : $"; the base {Fmt(job.Base)} stays"));
                    return;
            }
        }

        private void BeginSettle(LiftJob job, ResortPlanSheet.Row r)
        {
            job.Step = JobStep.Settling;
            job.StepFrame = m_Frame;
            job.NextSample = m_Frame + SettleEveryFrames;
            job.LastBaseY = job.LastTopY = float.NaN;
            if (r != null)
                m_Sheet.SetState(job.Key, SheetLiftState.StationsPlaced, null);
            Mod.Log.Info($"ResortLifts [queue]: {job.Name} STATIONS PLACED — base {Fmt(job.Base)}, top {Fmt(job.Top)}; waiting for the ground " +
                         $"under the stubs to settle (every {SettleEveryFrames} frames, {SettleToleranceM} m, at most {SettleMaxFrames} frames)");
        }

        private void StepSettling(LiftJob job, ResortPlanSheet.Row r)
        {
            if (m_Frame < job.NextSample)
                return;
            job.NextSample = m_Frame + SettleEveryFrames;
            if (!TipGround(job.Base, out float by) || !TipGround(job.Top, out float ty))
            {
                bool baseGone = Alive(job.Base) == Entity.Null;
                EndJob(job, r, SheetLiftState.Refused, baseGone ? "BASE[GONE]" : "TOP[GONE]",
                       $"the {(baseGone ? "base" : "top")} station is gone (bulldozed?) or the terrain is not ready");
                return;
            }
            int waited = m_Frame - job.StepFrame;
            bool agree = math.abs(by - job.LastBaseY) <= SettleToleranceM && math.abs(ty - job.LastTopY) <= SettleToleranceM;
            job.LastBaseY = by;
            job.LastTopY = ty;
            if (agree && waited >= SettleMinFrames)
            {
                Mod.Log.Info(string.Format(CultureInfo.InvariantCulture,
                    "ResortLifts [queue]: {0} SETTLED after {1} frames — ground under the stubs {2:F2} / {3:F2} m", job.Name, waited, by, ty));
                BeginLine(job);
                return;
            }
            if (waited >= SettleMaxFrames)
            {
                Mod.Log.Warn(string.Format(CultureInfo.InvariantCulture,
                    "ResortLifts [queue]: {0} the ground did not settle in {1} frames (last {2:F2} / {3:F2} m) — drawing the line anyway",
                    job.Name, waited, by, ty));
                BeginLine(job);
            }
        }

        private void BeginLine(LiftJob job)
        {
            job.Step = JobStep.Line;
            job.StepFrame = m_Frame;
            job.Ticket = m_LiftTool.TryActivateFor("plan " + job.Name, job.Type, job.Base, job.Top, autoBuild: true);
            Mod.Log.Info($"ResortLifts [queue]: {job.Name} LINE — scripted line #{job.Ticket.Serial} {job.Ticket.Outcome}");
        }

        private void StepLine(LiftJob job, ResortPlanSheet.Row r)
        {
            ScriptTicket t = job.Ticket;
            switch (t.Outcome)
            {
                case ScriptOutcome.Built:
                    m_QueueBuilt++;
                    EndJob(job, r, SheetLiftState.Built, null, string.Format(CultureInfo.InvariantCulture,
                        "BUILT — {0:F0} m, {1} tower(s), line {2}", t.LengthM, t.Towers, t.Line != null ? "registered" : "not registered"));
                    return;
                case ScriptOutcome.Refused:
                    EndJob(job, r, SheetLiftState.Refused, "LINE[PICK]", "the lift tool could not pick the ends: " + t.Reason);
                    return;
                case ScriptOutcome.Waiting:
                    m_QueueFitting++;
                    AddWatch(job, t);
                    EndJob(job, r, SheetLiftState.NeedsFitting, ResortPlanSheet.WaitingKey(t.Reason), "the lift tool waits in Edit for the player: " + t.Reason);
                    StopAfterJob($"{job.Name} needs fitting");
                    return;
                case ScriptOutcome.Abandoned:
                    EndJob(job, r, SheetLiftState.StationsPlaced, "STOPPED", "the lift tool was closed before the line was built: " + t.Reason);
                    StopAfterJob("the lift tool was closed");
                    return;
                default:
                    if (!t.Picked && m_Frame - job.StepFrame > LinePickMaxFrames)
                    {
                        Mod.Log.Warn($"ResortLifts [queue]: {job.Name} the lift tool did not pick the line in {LinePickMaxFrames} frames " +
                                     $"(active tool: {ActiveToolId()})");
                        AddWatch(job, t);
                        EndJob(job, r, SheetLiftState.StationsPlaced, "STOPPED", "the scripted line was not picked");
                        StopAfterJob("the lift tool did not open");
                    }
                    return;
            }
        }

        /// <summary>The lift's last step: the row's state (when the row still belongs to the job's plan), one log line, the next lift in the
        /// next tick.</summary>
        private void EndJob(LiftJob job, ResortPlanSheet.Row r, SheetLiftState state, string reasonKey, string text)
        {
            m_Job = null;
            m_QueueDone++;
            m_QueueRev++;
            if (state == SheetLiftState.Refused)
                m_QueueRefused++;
            bool written = r != null && job.PlanSerial == m_Sheet.PlanSerial;
            if (written)
            {
                m_Sheet.SetStations(job.Key, Ref(job.Base), Ref(job.Top));
                m_Sheet.SetState(job.Key, state, reasonKey);
            }
            string line = $"ResortLifts [queue]: {job.Name} {state}{(reasonKey != null ? " (" + reasonKey + ")" : string.Empty)} — {text}" +
                          (written ? string.Empty : " (the sheet was replaced: the row is not written)") + $"; queue {KeysText()}";
            if (state == SheetLiftState.Built || state == SheetLiftState.StationsPlaced)
                Mod.Log.Info(line);
            else
                Mod.Log.Warn(line);
        }

        private void Refuse(ResortPlanSheet.Row r, string reasonKey, string text)
        {
            m_QueueRefused++;
            m_QueueDone++;
            m_QueueRev++;
            m_Sheet.SetState(r.Key, SheetLiftState.Refused, reasonKey);
            Mod.Log.Warn($"ResortLifts [queue]: {r.Name} Refused ({reasonKey}) — {text}; queue {KeysText()}");
        }

        private void StopAfterJob(string why)
        {
            if (m_Pending.Count > 0)
                Mod.Log.Info($"ResortLifts [queue]: STOP ({why}) — {m_Pending.Count} lift(s) not started: {KeysText()}");
            DropPending();
            EndQueue("stopped: " + why);
        }

        // ---------------------------------------------------------------- watched tickets (rows that wait in the lift tool)

        private void AddWatch(LiftJob job, ScriptTicket t)
        {
            DropWatch(job.Key);
            m_Watches.Add(new Watch { Key = job.Key, Name = job.Name, PlanSerial = job.PlanSerial, Ticket = t });
        }

        private void DropWatch(uint key)
        {
            for (int i = m_Watches.Count - 1; i >= 0; i--)
                if (m_Watches[i].Key == key)
                    m_Watches.RemoveAt(i);
        }

        private void TickWatches()
        {
            for (int i = m_Watches.Count - 1; i >= 0; i--)
            {
                Watch w = m_Watches[i];
                ScriptTicket t = w.Ticket;
                if (!t.Final)
                    continue;
                m_Watches.RemoveAt(i);
                bool same = w.PlanSerial == m_Sheet.PlanSerial && m_Sheet.Find(w.Key) != null;
                if (t.Outcome == ScriptOutcome.Built)
                {
                    if (same)
                        m_Sheet.SetState(w.Key, SheetLiftState.Built, null);
                    Mod.Log.Info(string.Format(CultureInfo.InvariantCulture,
                        "ResortLifts [watch]: {0} BUILT by the player — {1:F0} m, {2} tower(s){3}", w.Name, t.LengthM, t.Towers,
                        same ? string.Empty : " (the sheet was replaced: the row is not written)"));
                }
                else
                {
                    if (same)
                        m_Sheet.SetState(w.Key, SheetLiftState.StationsPlaced, "LINE[" + t.Outcome.ToString().ToUpperInvariant() + "]");
                    Mod.Log.Info($"ResortLifts [watch]: {w.Name} the line was not built ({t.Outcome}: {t.Reason}); the stations stay");
                }
            }
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>The terrain under a station's stub tip (catalog row and live transform). False when the station is gone.</summary>
        private bool TipGround(Entity station, out float y)
        {
            y = float.NaN;
            if (Alive(station) == Entity.Null || !EntityManager.TryGetComponent(station, out PrefabRef pr) ||
                !EntityManager.TryGetComponent(station, out Game.Objects.Transform tr) || !m_Catalog.TryGetStation(pr.m_Prefab, out StationDef sd))
                return false;
            TerrainHeightData hd = m_TerrainSystem.GetHeightData();
            if (!hd.isCreated)
                return false;
            float3 tip = StationAlign.StubTip(tr.m_Position, tr.m_Rotation, sd.StubTipLocalZ, sd.StubTipLocalX);
            y = TerrainUtils.SampleHeight(ref hd, tip);
            return math.isfinite(y);
        }

        private Entity Alive(EntityRef r) => r.IsNull ? Entity.Null : Alive(new Entity { Index = r.Index, Version = r.Version });

        private Entity Alive(Entity e) =>
            e != Entity.Null && EntityManager.Exists(e) && !EntityManager.HasComponent<Deleted>(e) ? e : Entity.Null;

        private static EntityRef Ref(Entity e) => e == Entity.Null ? EntityRef.Null : new EntityRef(e.Index, e.Version);

        private static string Fmt(Entity e) => e == Entity.Null ? "-" : e.Index + ":" + e.Version;

        private long CityMoney()
        {
            Entity city = m_CitySystem.City;
            return city != Entity.Null && EntityManager.TryGetComponent(city, out PlayerMoney money) ? money.money : long.MaxValue;
        }

        private string ActiveToolId() => World.GetExistingSystemManaged<Game.Tools.ToolSystem>()?.activeTool?.toolID ?? "none";

        private string KeysText()
        {
            if (m_Pending.Count == 0)
                return "empty";
            var names = new List<string>(m_Pending.Count);
            foreach (uint k in m_Pending)
                names.Add(m_Sheet.Find(k)?.Name ?? k.ToString("X8", CultureInfo.InvariantCulture));
            return string.Join(", ", names);
        }
    }
}
