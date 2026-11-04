// Moves the aerial tram's two cabins (Shuttle carriers hosted on a Shuttle Base station). The dev-tab foldout is TramShuttleSystem.Debug.cs.
// Clock: TimeS = ShuttleProfile.TimeSOf(RenderingSystem.frameIndex, frameTime) = ChairPinSystem's TimeS bit for bit; one clock for every tram.
// Gate (per shuttle, at a rebuild / power change / kill switch): Running / Held / Parked; power and closed are ONE predicate,
// LiftServedRule.Carries (THE run predicate, shared with ChairPinSystem's hold and the hub's capacity).
// Scheduling (Mod.cs; load-bearing): UpdateAfter<TramShuttleSystem, LiftCensusSystem>(Modification4B), census -> this -> ChairPinSystem;
// the phase must stay Modification4B or the anchor is skipped and the system silently never runs.
// Never: save data (positions come from the clock); Hidden / Deleted on a cabin; a Loop carrier, a Temp entity or anything in the Editor.
// Logs: every state change is logged ("silence must be impossible"). Engine notes: EngineNotes/TramShuttleSystem.md

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Rendering;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace SkiResortMod.Systems
{
    public sealed partial class TramShuttleSystem : GameSystemBase
    {
        // Tuning
        internal const int CensusGateFrames = 120;  // frames without a census build before running on the cable path alone
        internal const int PowerPollFrames = 64;
        internal const int RebuildCooldownFrames = 30;        // = ChairPinSystem.RebuildCooldownFrames
        internal const int WatchdogIntervalFrames = 4096;     // = ChairPinSystem.WatchdogIntervalFrames
        internal const double BoundsRefreshMeters = 2.0;      // = ChairPinSystem.BoundsRefreshMeters (ChairPinSystem.cs:289)
        internal const float FreshBaseToleranceM = 0.05f;  // fresh-base rule (a)
        internal const float HostedCheckToleranceM = 0.05f;   // [arm] CHECK: hosted pose vs dock − drop
        internal const double HeldStandingToleranceM = 1.0;  // Σ over the cabins "already at a terminal configuration"
        internal const double TieToleranceM = 1e-3;  // an assignment tie
        internal const double SlowPassMs = 1.0;
        internal const float StateLogWindowS = 10f;  // the [state] lines, like the census power lines

        // Kill switch (dev tab): false parks every cabin in its hosted pose
        private static bool MovingShuttles = true;

        private SkiPrefabCatalogSystem m_PrefabCatalog;
        private RenderingSystem m_RenderingSystem;
        private LiftCensusSystem m_Census;                    // resolved lazily (GetExistingSystemManaged: never created here)
        private readonly LiftLoopCache m_Cache = new LiftLoopCache();   // our own instance, never ChairPin's

        private EntityQuery m_AllCabinQuery;                  // = ChairPin's m_AllChairQuery shape (ChairPinSystem.cs:479-485)
        private EntityQuery m_ChangedCabinQuery;              // = ChairPin's m_UpdatedChairQuery shape (:458-477)
        private EntityQuery m_LaneQuery;                      // = ChairPin's (:513-520)
        private EntityQuery m_StationQuery;                   // = ChairPin's (:522-527)

        // The Shuttle carrier prefabs of this load (by catalog Generation)
        private struct CarrierInfo
        {
            public LiftTypeDef Def;
            public CarrierDef Carrier;
            public float GripTop;   // bounds.max.y (CarrierGripTops); 0 = no usable bounds
            public float Drop;      // LiftMoverMath.CarrierDrop(GripTop, GripTopAboveRopeM); ≤ 0 = never rides (parked, WARN)
        }
        private readonly Dictionary<Entity, CarrierInfo> m_Carriers = new Dictionary<Entity, CarrierInfo>();
        private int m_CarrierGeneration = int.MinValue;

        // One shuttle (a station pair with a path): rebuilt geometry + transient gate state carried by pair
        private sealed class Run
        {
            public LiftLoopCache.Shuttle Sh;
            public LiftTypeDef Def;                                // null = not a Shuttle type (Parked)
            public Entity Base, Top;
            public int BayOfA;                                     // hosted bay of chain A's cabin; chain B's is 1 − BayOfA
            public readonly Entity[] Cabin = new Entity[2];        // [0] rides chain A, [1] chain B; Entity.Null = none
            public readonly Entity[] Carriage = new Entity[2];     // the carrier grip round: each cabin's carriage (CarriagesOf), Entity.Null = none
            public readonly float[] Drop = new float[2];
            public readonly Transform[] Park = new Transform[2];   // world hosted pose of the bay of chain A / chain B
            public readonly bool[] ParkKnown = new bool[2];
            public readonly double[] S = new double[2];            // last s along each path (dump)
            public int Cabins;
            public double L, LA, LB, TripS, CycleS;
            public bool ProfileValid;
            public bool RowFound;
            public LiftCensus.State RowState;
            public bool RowMixed;
            public LiftPower Power = LiftPower.Unknown;
            public bool Closed;  // the last IsClosed read (Gate); PollPower re-gates on a change
            public ShuttleState State = ShuttleState.Parked;
            public bool StateKnown;                                // false until the first gate of this run
            public string StateWhy = "";
            public bool HasLatch;  // the Held latch
            public double HoldU;
            public double U;
            public ShuttleLeg Leg;
            public double BoundsU = double.NaN;
            public bool PoseDirty = true;
            public string AssignRule = "-";
            public LogWindow StateLog;
            public bool Seen;
            public bool ReturnedFromVanish;                        // a new Run of a pair whose earlier Run vanished (logs "Parked → <state>")
        }
        private readonly List<Run> m_Runs = new List<Run>();
        private readonly Dictionary<(Entity, Entity), Run> m_RunsByPair = new Dictionary<(Entity, Entity), Run>();
        private readonly List<(Entity, Entity)> m_PairScratch = new List<(Entity, Entity)>();

        /// <summary>Cabin → hosted bay, transient (per session, cleared per load, never saved).</summary>
        private readonly Dictionary<Entity, int> m_CabinBay = new Dictionary<Entity, int>();

        private struct ParkedCabin
        {
            public Entity Cabin, Owner;
            public Transform Pose;
            public bool PoseKnown;
        }
        private readonly List<ParkedCabin> m_ParkedCabins = new List<ParkedCabin>();

        // Rebuild scratch (allocation-free once grown; rebuilds are event-driven)
        private readonly List<Entity> m_StationList = new List<Entity>();
        private readonly List<Transform> m_StationTransforms = new List<Transform>();
        private readonly List<StationDef> m_StationDefList = new List<StationDef>();
        private readonly Dictionary<int, int> m_StationByOwnerIndex = new Dictionary<int, int>();
        private readonly List<LiftLoopCache.CableLane> m_Lanes = new List<LiftLoopCache.CableLane>();
        private readonly Dictionary<Entity, List<Entity>> m_OwnerCabins = new Dictionary<Entity, List<Entity>>();
        private readonly Dictionary<Entity, int> m_HostedCount = new Dictionary<Entity, int>();
        private readonly Dictionary<Entity, Entity> m_BaseWinner = new Dictionary<Entity, Entity>();
        private readonly Dictionary<Entity, int> m_BaseLines = new Dictionary<Entity, int>();
        private readonly HashSet<Entity> m_Riding = new HashSet<Entity>();
        private readonly List<Transform> m_HostedScratch = new List<Transform>();
        private readonly List<bool> m_BayTaken = new List<bool>();
        private readonly List<LiftCensus.Row> m_Rows = new List<LiftCensus.Row>();
        private static readonly Comparison<Entity> s_ByEntity = CompareEntity;

        // Per-load state
        private bool m_InGame, m_InEditor;
        private bool m_Disabled, m_ErrorLogged;
        private bool m_ArmLogged, m_IdleLogged, m_CensusWarned, m_CensusBuilt;
        private int m_GateWaitFrames;
        private bool m_Dirty = true, m_Immediate = true, m_GateDirty, m_AnyPoseDirty;
        private string m_DirtyWhy = "load";
        private int m_Cooldown;
        private int m_WatchdogCountdown = WatchdogIntervalFrames;
        private int m_LastRevision = int.MinValue;
        private uint m_PowerPollFrame = uint.MaxValue;
        private double m_LastTimeS = -1.0;
        private string m_LastSignature;
        private int m_LastBuildStations;
        private bool m_MovingAliveLogged, m_SlowWarned, m_WatchdogWarned;
        private readonly HashSet<string> m_Warned = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<Entity> m_CabinWarned = new HashSet<Entity>();
        // The "starts N tram lines" WARN has its own once-per-base set, so a base that ALSO hosts a wrong
        // number of cabins (m_CabinWarned) still gets both lines.
        private readonly HashSet<Entity> m_TwoLinesWarned = new HashSet<Entity>();
        // Transient, per load, never saved: pairs whose known Run vanished from a rebuild; the pair's
        // next Run logs "Parked → <state>" (a new Run has no previous state of its own).
        private readonly HashSet<(Entity, Entity)> m_VanishedPairs = new HashSet<(Entity, Entity)>();

        // Diagnostics (dev tab, AppendDump): plain counters, written where the work happens
        private int m_DiagUpdateCalls;
        private string m_LastExit = "never ran";
        private string m_LastError;
        private int m_DiagRebuilds;
        private long m_DiagLastRebuildFrame = -1;
        private string m_DiagLastRebuildWhy = "-";
        private int m_DiagProbeHits, m_DiagProbeHitsLast;
        private int m_DiagLanes;
        private int m_DiagCabinsFound, m_DiagEnrolled;
        private int m_DiagLastMoved, m_DiagLastInPlace, m_DiagLastMissing;
        private int m_DiagPausedSkips, m_DiagPowerReads;
        private double m_DiagPassUs, m_DiagPassUsMax, m_DiagRebuildMs, m_DiagRebuildMsMax;

        // Accessors for SkiResortStatsSystem / SkiResortHubUISystem

        /// <summary>A Game load, a Shuttle carrier prefab FOUND this load, not disabled by an exception.</summary>
        internal bool IsArmed => m_InGame && !m_Disabled && m_Carriers.Count > 0;

        /// <summary>The shuttle between the two stations (either order); false = no path. Parked shuttles answer
        /// true with CabinsOnPath 0 (the hub reads them ChairsStopped).</summary>
        internal bool TryGetShuttle(Entity stationA, Entity stationB, out ShuttleView view)
        {
            // After an exception the system is disabled for the load and its cabins stand where they
            // were; the last Run would still read "Running". No path -> Stats reads state 1 (ChairsStopped).
            Run run = m_Disabled ? null : FindRun(stationA, stationB);
            if (run == null)
            {
                view = default(ShuttleView);
                return false;
            }
            view = new ShuttleView
            {
                Cabins = run.Cabins,
                CabinsOnPath = run.State == ShuttleState.Parked ? 0 : run.Cabins,
                PathM = run.L,
                State = run.State,
                Leg = run.Leg,
                FractionA = run.U,
                TripS = (float)run.TripS,
                CycleS = (float)run.CycleS,
            };
            return true;
        }

        /// <summary>Shuttle carriers owned by the station at the last rebuild (the cabins it hosts).</summary>
        internal int CabinsHostedBy(Entity station)
        {
            return station != Entity.Null && m_HostedCount.TryGetValue(station, out int n) ? n : 0;
        }

        /// <summary>One line for the hub's DUMP ("Running, cabin A Up at 0.42, path 1013 m, trip 113.8 s, cycle
        /// 347.6 s, cabins 2/2"); "" when no shuttle runs between the two stations.</summary>
        internal string DescribeShuttle(Entity stationA, Entity stationB)
        {
            Run run = FindRun(stationA, stationB);
            if (run == null)
                return "";
            if (m_Disabled)
                return "disabled for this load by an exception (cabins frozen; see the TramShuttle dump)";
            int onPath = run.State == ShuttleState.Parked ? 0 : run.Cabins;
            return Inv($"{run.State}{run.StateWhy}, cabin A {run.Leg} at {run.U:F2}, path {run.L:F0} m, trip {run.TripS:F1} s, ") +
                   Inv($"cycle {run.CycleS:F1} s, cabins {onPath}/{run.Cabins}");
        }

        /// <summary>The DUMP block (ChairPinSystem.Debug's dump and this system's dev-tab button).</summary>
        internal void AppendDump(StringBuilder sb)
        {
            try
            {
                sb.Append(Inv($"  {nameof(TramShuttleSystem)} [dump]: mode={(MovingShuttles ? "moving" : "off")} armed={IsArmed} "))
                  .Append(Inv($"inGame={m_InGame} disabled={m_Disabled} lastExit=\"{m_LastExit}\" census={(m_CensusBuilt ? "built" : m_CensusWarned ? "fallback (not built)" : "waiting")} "))
                  .Append(Inv($"carrierPrefabs={m_Carriers.Count} shuttles={m_Runs.Count} parkedCabins={m_ParkedCabins.Count} cabinsFound={m_DiagCabinsFound} "))
                  .Append(Inv($"rebuilds={m_DiagRebuilds} lastRebuild={m_DiagLastRebuildFrame} why=\"{m_DiagLastRebuildWhy}\" probeHits={m_DiagProbeHits} "))
                  .Append(Inv($"powerReads={m_DiagPowerReads} pausedSkips={m_DiagPausedSkips} lastPass moved={m_DiagLastMoved} inPlace={m_DiagLastInPlace} "))
                  .Append(Inv($"missing={m_DiagLastMissing} passUs={m_DiagPassUs:F1}/{m_DiagPassUsMax:F1} rebuildMs={m_DiagRebuildMs:F2}/{m_DiagRebuildMsMax:F2} "))
                  .Append(Inv($"timeS={m_LastTimeS:F2} lanes={m_DiagLanes} stations={m_StationList.Count} chains={m_Cache.m_ChainCount}/"))
                  .AppendLine(Inv($"{m_Cache.m_UnclosedChainCount} unclosed cabinBayMap={m_CabinBay.Count}"));
                for (int r = 0; r < m_Runs.Count; r++)
                {
                    Run run = m_Runs[r];
                    LiftLoopCache.Shuttle sh = run.Sh;
                    string latch = run.HasLatch ? Inv($" (held at {run.HoldU:F0})") : "";
                    string row = run.RowFound ? run.RowState + (run.RowMixed ? " mixed" : "") : m_CensusBuilt ? "none" : "unknown (census not built)";
                    sb.Append(Inv($"    shuttle {r}: base {Ent(run.Base)} → top {Ent(run.Top)} ({(run.Def != null ? run.Def.Id : "not a Shuttle type")}): "))
                      .Append(Inv($"{run.State}{run.StateWhy}, leg {run.Leg}, u {run.U:F4}{latch}; "))
                      .Append(Inv($"L_A {run.LA:F2} / L_B {run.LB:F2} / profile {run.L:F2} m; trip {run.TripS:F2} s, cycle {run.CycleS:F2} s; "))
                      .Append(Inv($"docks base {DockName(sh.m_A.m_BaseDock)} {sh.m_A.m_BaseRunM:F2} / {DockName(sh.m_B.m_BaseDock)} {sh.m_B.m_BaseRunM:F2} m, "))
                      .Append(Inv($"top {DockName(sh.m_A.m_TopDock)} {sh.m_A.m_TopRunM:F2} / {DockName(sh.m_B.m_TopDock)} {sh.m_B.m_TopRunM:F2} m; "))
                      .AppendLine(Inv($"bay of A {run.BayOfA}; cabins by {run.AssignRule}; power {run.Power}; census row {row}"));
                    for (int c = 0; c < 2; c++)
                    {
                        Entity cabin = run.Cabin[c];
                        if (cabin == Entity.Null)
                        {
                            sb.AppendLine($"      cabin {(c == 0 ? "A" : "B")}: none");
                            continue;
                        }
                        string pos = EntityManager.Exists(cabin) && EntityManager.TryGetComponent(cabin, out Transform t)
                            ? Vec(t.m_Position) : "gone";
                        bool interp = EntityManager.Exists(cabin) && EntityManager.HasComponent<InterpolatedTransform>(cabin);
                        string parkedPose = run.ParkKnown[c] ? Vec(run.Park[c].m_Position) : "unknown";
                        sb.Append(Inv($"      cabin {(c == 0 ? "A" : "B")}: {Ent(cabin)} bay {(c == 0 ? run.BayOfA : 1 - run.BayOfA)} "))
                          .AppendLine(Inv($"s {run.S[c]:F2} m, drop {run.Drop[c]:F2} m, at {pos}, parked pose {parkedPose}, interpolated {(interp ? "Y" : "N")}"));
                    }
                }
                for (int p = 0; p < m_ParkedCabins.Count; p++)
                {
                    ParkedCabin parked = m_ParkedCabins[p];
                    sb.AppendLine($"    parked cabin {Ent(parked.Cabin)} (owner {Ent(parked.Owner)}): " +
                                  (parked.PoseKnown ? "hosted pose " + Vec(parked.Pose.m_Position) : "no hosted pose (left untouched)"));
                }
                for (int n = 0; n < m_Cache.m_BuildNotes.Count; n++)
                    sb.AppendLine("    build: " + m_Cache.m_BuildNotes[n]);
                if (m_LastSignature != null)
                    sb.AppendLine("    signature: " + m_LastSignature);
                if (m_LastError != null)
                    sb.AppendLine("    lastError=" + m_LastError);
            }
            catch (Exception e)
            {
                sb.AppendLine("  TramShuttle dump aborted: " + e);
            }
        }


        protected override void OnCreate()
        {
            base.OnCreate();
            // The prefab catalog (Enabled = false, load hooks only; GetOrCreate is safe whatever Mod.cs's order).
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_RenderingSystem = World.GetOrCreateSystemManaged<RenderingSystem>();
            CarriagesOnCreate();   // the wind map (TramShuttleSystem.Carriages.cs)

            // Hosted cabins keep Game.Objects.Static (engine notes VANILLA FACTS); the Shuttle prefab filter is in the passes.
            m_ChangedCabinQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Objects.Static>(),
                    ComponentType.ReadOnly<Owner>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    // The probe only counts PrefabRef; ReadWrite<Transform> made a
                    // CompleteDependency here sync every Transform writer in the city (cars, pedestrians).
                    ComponentType.ReadOnly<Transform>(),
                },
                Any = new[]
                {
                    ComponentType.ReadOnly<Created>(),
                    ComponentType.ReadOnly<Updated>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            m_AllCabinQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Objects.Static>(),
                ComponentType.ReadOnly<Owner>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadWrite<Transform>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
            m_LaneQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Net.Lane>(),
                ComponentType.ReadOnly<Game.Net.UtilityLane>(),
                ComponentType.ReadOnly<Curve>(),
                ComponentType.ReadOnly<Owner>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
            m_StationQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Buildings.Building>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Transform>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            Mod.Log.Info($"{nameof(TramShuttleSystem)} created");
        }

        /// <summary>A load (or leaving to the menu): write the folded [state] lines of the city being left and stop moving
        /// until the load completes. Never throws: GameSystemBase.GamePreload DISABLES a system whose OnGamePreload throws
        /// (Game/GameSystemBase.cs:85-96; same in 1.6.2).</summary>
        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            m_InGame = false;
            m_InEditor = false;
            try
            {
                FlushStateLogs();
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"{nameof(TramShuttleSystem)}: folded [state] lines could not be written before the load " +
                             $"({e.GetType().Name}: {e.Message}) — non-fatal");
            }
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            // Like OnGamePreload: vanilla catches a throw here and writes it to
            // the game's log only, without disabling the system (Game/GameSystemBase.cs:60-70), so a half-done reset would
            // leave nothing in SkiResortMod.Mod.log. A failed reset disables this load (fail-stop, as OnUpdate's catch).
            try
            {
                LoadingComplete(purpose, mode);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                m_LastError = e.ToString();
                m_LastExit = "exception (see Last error)";
                m_ErrorLogged = true;
                Mod.Log.Error($"!!! {nameof(TramShuttleSystem)} disabled for this load (the per-load reset failed): " +
                              $"{e.GetType().Name}: {e.Message}\n{e.StackTrace}");
            }
        }

        private void LoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_InGame = mode == GameMode.Game;
            m_InEditor = mode == GameMode.Editor;

            // Re-arm per load (an exception disables the CURRENT load only) and drop every transient: the cache, the runs,
            // the bay map (entity indices are remapped on load), the latches, the once-per-load latches.
            m_Disabled = false;
            m_ErrorLogged = false;
            m_LastError = null;
            m_ArmLogged = false;
            m_LaneScanLogged = false;
            m_HarvestVerified = m_HarvestFullThisLoad = false;  // the lane-harvest watchdog re-arms per load
            m_IdleLogged = false;
            m_CensusWarned = false;
            m_CensusBuilt = false;
            m_GateWaitFrames = 0;
            m_Dirty = true;
            m_Immediate = true;
            m_DirtyWhy = "load";
            m_GateDirty = false;
            m_AnyPoseDirty = false;
            m_Cooldown = 0;
            m_WatchdogCountdown = WatchdogIntervalFrames;
            m_LastRevision = int.MinValue;
            m_PowerPollFrame = uint.MaxValue;
            m_LastTimeS = -1.0;
            m_LastSignature = null;
            m_LastBuildStations = 0;
            m_MovingAliveLogged = m_SlowWarned = m_WatchdogWarned = false;
            m_Warned.Clear();
            m_CabinWarned.Clear();
            m_TwoLinesWarned.Clear();
            m_VanishedPairs.Clear();
            m_Cache.Clear();
            m_Runs.Clear();
            m_RunsByPair.Clear();
            m_CabinBay.Clear();
            m_ParkedCabins.Clear();
            m_OwnerCabins.Clear();
            m_HostedCount.Clear();
            CarriagesReset();
            m_Rows.Clear();
            m_StationList.Clear();
            m_CarrierGeneration = int.MinValue;
            m_Carriers.Clear();

            m_DiagUpdateCalls = 0;
            m_LastExit = "load complete, no update yet";
            m_DiagRebuilds = 0;
            m_DiagLastRebuildFrame = -1;
            m_DiagLastRebuildWhy = "-";
            m_DiagProbeHits = m_DiagProbeHitsLast = 0;
            m_DiagLanes = m_DiagCabinsFound = m_DiagEnrolled = 0;
            m_DiagLastMoved = m_DiagLastInPlace = m_DiagLastMissing = 0;
            m_DiagPausedSkips = m_DiagPowerReads = 0;
            m_DiagPassUs = m_DiagPassUsMax = m_DiagRebuildMs = m_DiagRebuildMsMax = 0.0;

            if (m_InEditor)
            {
                m_ArmLogged = true;
                Mod.Log.Info("TramShuttle [arm]: Editor — inactive, cabins keep their hosted pose");
            }
        }

        /// <summary>Quit to desktop raises no OnGamePreload, but the world's systems are destroyed while the logger still
        /// lives (LogWindow.cs header): the folded [state] lines are written here. Teardown never throws.</summary>
        protected override void OnDestroy()
        {
            try
            {
                FlushStateLogs();
            }
            catch (Exception)
            {
                // nothing left to report to
            }
            base.OnDestroy();
        }


        protected override void OnUpdate()
        {
            m_DiagUpdateCalls++;
            if (m_Disabled)
            {
                m_LastExit = "disabled by exception (see Last error)";
                return;
            }
            if (!m_InGame)
            {
                m_LastExit = m_InEditor ? "Editor: inactive (C10)" : "no game loaded";
                return;
            }
            try
            {
                // 1. Idle this load: no Shuttle carrier FOUND (one count check per frame; the set is rebuilt only when the
                //    catalog's Generation moves).
                RefreshCarriers();
                if (m_Carriers.Count == 0)
                {
                    if (!m_ArmLogged)
                        LogIdle();
                    m_LastExit = "idle: no Shuttle carrier prefab found this load";
                    return;
                }
                if (m_IdleLogged)
                {
                    // A catalog re-resolution FOUND a Shuttle carrier after the idle line: write the arm line too.
                    m_IdleLogged = false;
                    m_ArmLogged = false;
                }

                // 2. Census gate: write nothing until the census has built once this load.
                if (m_Census == null)
                    m_Census = World.GetExistingSystemManaged<LiftCensusSystem>();
                LiftCensusSystem census = m_Census;
                bool censusBuilt = census != null && census.IsArmed && census.Builds > 0;
                if (!censusBuilt)
                {
                    if (m_GateWaitFrames < CensusGateFrames)
                    {
                        m_GateWaitFrames++;
                        m_LastExit = "waiting for the lift census (C12)";
                        return;
                    }
                    if (!m_CensusWarned)
                    {
                        m_CensusWarned = true;
                        Mod.Log.Warn($"TramShuttle: the lift census has not built after {CensusGateFrames} frames — the tram " +
                                     "runs on its cable path alone this load (power unknown = powered)" +
                                     (census == null ? " (LiftCensusSystem is not registered)" : ""));
                    }
                }
                if (censusBuilt != m_CensusBuilt)
                {
                    m_CensusBuilt = censusBuilt;
                    MarkDirty(censusBuilt ? "census built" : "census lost", immediate: true);
                }
                if (!m_ArmLogged)
                    LogArm(censusBuilt);

                double timeS = ShuttleProfile.TimeSOf(m_RenderingSystem.frameIndex, m_RenderingSystem.frameTime);

                // 3. Triggers. The cabin probe runs only once a tram exists (a Shuttle census row, a run or a parked
                // cabin); a city without one never reads the query's chunks.
                bool tramPresent = m_Runs.Count > 0 || m_ParkedCabins.Count > 0 || AnyShuttleRow();
                m_DiagProbeHitsLast = !tramPresent || m_ChangedCabinQuery.IsEmptyIgnoreFilter ? 0 : CountChangedCabins();
                if (m_DiagProbeHitsLast > 0)
                {
                    m_DiagProbeHits += m_DiagProbeHitsLast;
                    MarkDirty("cabin created or updated", immediate: true);
                }
                if (censusBuilt && census.Revision != m_LastRevision)
                {
                    m_LastRevision = census.Revision;
                    census.CopyRows(m_Rows);
                    if (m_LastBuildStations > 0 || m_Runs.Count > 0 || m_ParkedCabins.Count > 0 || AnyShuttleRow())
                        MarkDirty("census revision", immediate: true);
                }
                if (--m_WatchdogCountdown <= 0)
                {
                    m_WatchdogCountdown = WatchdogIntervalFrames;
                    if (m_LastBuildStations > 0 || m_Runs.Count > 0 || m_ParkedCabins.Count > 0)
                        MarkDirty("watchdog", immediate: false);
                }
                if (m_Cooldown > 0)
                    m_Cooldown--;
                bool rebuilt = false;
                if (m_Dirty && (m_Cooldown <= 0 || m_Immediate))
                {
                    Rebuild(timeS);
                    rebuilt = true;
                }

                // 4. Power poll and the gate on a change; the kill switch re-gates everything.
                if (censusBuilt)
                    PollPower(census, timeS);
                if (m_GateDirty)
                {
                    m_GateDirty = false;
                    for (int r = 0; r < m_Runs.Count; r++)
                        ReGate(m_Runs[r], timeS);
                }

                // 5. Paused: the derived positions are identical, write nothing.
                if (timeS == m_LastTimeS && !rebuilt && !m_AnyPoseDirty)
                {
                    m_DiagPausedSkips++;
                    m_LastExit = "paused (clock unchanged)";
                    TrailStateLogs();
                    return;
                }
                m_LastTimeS = timeS;

                // 6-7. Move / hold / park, bounds refresh. Timed alone: the rebuild has its own figure in [rebuild],
                // and the power poll's ReadLiftPower is a sync point on the electricity jobs, not mover work.
                long moveStart = Stopwatch.GetTimestamp();
                MovePass(timeS);
                TrailStateLogs();

                double us = (Stopwatch.GetTimestamp() - moveStart) * 1e6 / Stopwatch.Frequency;
                m_DiagPassUs = us;
                if (us > m_DiagPassUsMax)
                    m_DiagPassUsMax = us;
                if (us > SlowPassMs * 1000.0 && !m_SlowWarned && !rebuilt)
                {
                    m_SlowWarned = true;
                    Mod.Log.Warn(Inv($"TramShuttle: a mover pass took {us / 1000.0:F2} ms (over {SlowPassMs:F1} ms) — {m_Runs.Count} shuttle(s), ") +
                                 Inv($"{CountRiding()} cabin(s) (logged once per load)"));
                }
            }
            catch (Exception e)
            {
                // Never throw into the game loop: disable for THIS load only (re-armed by OnGameLoadingComplete).
                m_Disabled = true;
                m_LastError = e.ToString();
                m_LastExit = "exception (see Last error)";
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(TramShuttleSystem)} disabled for this load: {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
                }
            }
        }

        /// <summary>The [rebuild] line names the FIRST pending reason (an event replaces a pending watchdog request).</summary>
        private void MarkDirty(string why, bool immediate)
        {
            if (!m_Dirty || (immediate && !m_Immediate))
                m_DirtyWhy = why;
            m_Dirty = true;
            if (immediate)
                m_Immediate = true;
        }

        /// <summary>The kill switch (dev tab): off parks every cabin in its hosted pose; on re-gates them.</summary>
        internal void ToggleMoving(string source)
        {
            MovingShuttles = !MovingShuttles;
            m_GateDirty = true;
            Mod.Log.Info($"TramShuttle [{source}]: moving {(MovingShuttles ? "ON — the cabins follow the clock" : "OFF — every cabin parks in its hosted pose")}");
        }


        /// <summary>The change probe: Shuttle cabins among this frame's Created / Updated
        /// owned static objects. Called only when the query is non-empty.</summary>
        private int CountChangedCabins()
        {
            int count = 0;
            // Complete the writers of the component read below (as ChairPinSystem's main-thread chunk reads).
            // PrefabRef only (the query itself is Transform ReadOnly).
            EntityManager.CompleteDependencyBeforeRO<PrefabRef>();
            var chunks = m_ChangedCabinQuery.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    for (int i = 0; i < prefabRefs.Length; i++)
                    {
                        if (m_Carriers.ContainsKey(prefabRefs[i].m_Prefab))
                            count++;
                    }
                }
            }
            finally
            {
                chunks.Dispose();
            }
            return count;
        }

        private bool AnyShuttleRow()
        {
            for (int r = 0; r < m_Rows.Count; r++)
            {
                LiftTypeDef def = SkiCatalog.LiftByIndex(m_Rows[r].Type);
                if (def != null && def.Mover == MoverKind.Shuttle)
                    return true;
            }
            return false;
        }

        /// <summary>ReadLiftPower per shuttle whose census row is Complete, every PowerPollFrames of the rendered frame
        /// counter (nothing while paused); a change re-gates that shuttle.</summary>
        private void PollPower(LiftCensusSystem census, double timeS)
        {
            uint frame = m_RenderingSystem.frameIndex;
            if (m_PowerPollFrame != uint.MaxValue && frame >= m_PowerPollFrame && frame - m_PowerPollFrame < PowerPollFrames)
                return;
            m_PowerPollFrame = frame;
            for (int r = 0; r < m_Runs.Count; r++)
            {
                Run run = m_Runs[r];
                if (!run.RowFound || run.RowState != LiftCensus.State.Complete)
                    continue;
                LiftPower power = census.ReadLiftPower(run.Base);
                m_DiagPowerReads++;
                // A change of the closed flag re-gates too (the ski area's run state flips the cabins like power does).
                bool closedNow = IsClosed(run);
                if (power == run.Power && closedNow == run.Closed)
                    continue;
                run.Power = power;
                ReGate(run, timeS);
            }
        }

        private void MovePass(double timeS)
        {
            PollTramWind(timeS);   // the carrier grip round: the cabins' wind (every 512 render frames)
            int moved = 0, inPlace = 0, missing = 0, running = 0, cabinsRunning = 0;
            for (int r = 0; r < m_Runs.Count; r++)
            {
                Run run = m_Runs[r];
                if (run.State == ShuttleState.Parked)
                {
                    run.U = 0.0;
                    run.Leg = ShuttleLeg.DwellBase;
                    if (run.PoseDirty)
                    {
                        for (int c = 0; c < 2; c++)
                        {
                            if (run.Cabin[c] != Entity.Null && run.ParkKnown[c])
                            {
                                Count(Park(run.Cabin[c], run.Park[c]), ref moved, ref inPlace, ref missing);
                                ParkCarriage(run, run.Carriage[c], run.Park[c], tag: true);
                            }
                        }
                        run.PoseDirty = false;
                        run.BoundsU = double.NaN;
                    }
                    continue;
                }

                double u;
                ShuttleLeg leg;
                if (run.State == ShuttleState.Running)
                {
                    running++;
                    cabinsRunning += run.Cabins;
                    u = ShuttleProfile.FractionA(timeS, run.L, run.Def.SpeedMps, run.Def.AccelMps2, run.Def.DwellS, out leg);
                }
                else
                {
                    // Held: the latched terminal; constant, so it is written only when it changed.
                    u = run.HoldU;
                    leg = u > 0.5 ? ShuttleLeg.DwellTop : ShuttleLeg.DwellBase;
                    if (!run.PoseDirty)
                    {
                        run.U = u;
                        run.Leg = leg;
                        continue;
                    }
                }
                run.U = u;
                run.Leg = leg;

                // Bounds refresh: Updated on both cabins every BoundsRefreshMeters of travel (and on a jump).
                bool tag = run.PoseDirty || double.IsNaN(run.BoundsU) || Math.Abs(u - run.BoundsU) * run.L >= BoundsRefreshMeters;
                // The carrier grip round: a cabin with a paired carriage is posed with it (TramShuttleSystem.Carriages.cs).
                for (int c = 0; c < 2; c++)
                {
                    Entity cabin = run.Cabin[c];
                    if (cabin == Entity.Null)
                        continue;
                    Entity carriage = run.Carriage[c];
                    MoveResult result;
                    if (run.Drop[c] > 0f)
                    {
                        double s = c == 0 ? u * run.LA : (1.0 - u) * run.LB;
                        run.S[c] = s;
                        quaternion fallback = run.ParkKnown[c] ? run.Park[c].m_Rotation : run.Sh.m_BaseRotation;
                        result = carriage != Entity.Null && run.Def != null
                            ? PlaceSplitOnPath(run, c, cabin, carriage, c == 0 ? run.Sh.m_A : run.Sh.m_B, s, fallback, timeS)
                            : PlaceOnPath(cabin, c == 0 ? run.Sh.m_A : run.Sh.m_B, s, run.Drop[c], fallback);
                    }
                    else
                    {
                        // No usable drop (WARNed per prefab): this cabin is only ever parked.
                        result = run.PoseDirty && run.ParkKnown[c] ? Park(cabin, run.Park[c]) : MoveResult.InPlace;
                        if (run.PoseDirty && run.ParkKnown[c])
                            ParkCarriage(run, carriage, run.Park[c], tag: true);
                    }
                    Count(result, ref moved, ref inPlace, ref missing);
                    if (tag && result != MoveResult.Missing)
                    {
                        TagUpdated(cabin);
                        if (carriage != Entity.Null)
                            TagUpdated(carriage);
                    }
                }
                if (tag)
                    run.BoundsU = u;
                run.PoseDirty = false;
            }
            m_AnyPoseDirty = false;

            if (missing > 0)
                MarkDirty("cabin missing", immediate: true);
            m_DiagLastMoved = moved;
            m_DiagLastInPlace = inPlace;
            m_DiagLastMissing = missing;
            m_LastExit = m_Runs.Count == 0 ? "moving: no shuttle path"
                : running > 0 ? "moving"
                : "moving: no shuttle running (held or parked)";

            if (moved > 0 && running > 0 && !m_MovingAliveLogged)
            {
                m_MovingAliveLogged = true;
                Mod.Log.Info($"TramShuttle [moving] pass alive — {cabinsRunning} cabin(s) on {running} shuttle(s)");
            }
        }

        private enum MoveResult : byte { Moved, InPlace, Missing }

        private static void Count(MoveResult result, ref int moved, ref int inPlace, ref int missing)
        {
            switch (result)
            {
                case MoveResult.Moved: moved++; break;
                case MoveResult.InPlace: inPlace++; break;
                default: missing++; break;
            }
        }

        /// <summary>HOW A CABIN IS PLACED (engine notes; ChairPinSystem.MoveChair, ChairPinSystem.cs:1152-1187).</summary>
        private MoveResult PlaceOnPath(Entity cabin, LiftLoopCache.Path path, double s, float drop, quaternion fallback)
        {
            if (!EntityManager.Exists(cabin) || EntityManager.HasComponent<Deleted>(cabin) ||
                !EntityManager.TryGetComponent(cabin, out Transform transform))
                return MoveResult.Missing;
            LiftLoopCache.Sample(path, s, out float3 rope, out float3 baseToTop);
            float3 position = rope;
            position.y -= drop;
            // -t_up, never the travel direction (no 180-degree turn at a reversal); y zeroed: level cabins.
            quaternion rotation = NetUtils.GetNodeRotation(-baseToTop, fallback);
            return Write(cabin, transform, position, rotation);
        }

        /// <summary>The hosted pose; one Updated tag when it moved the cabin.</summary>
        private MoveResult Park(Entity cabin, Transform pose)
        {
            if (!EntityManager.Exists(cabin) || EntityManager.HasComponent<Deleted>(cabin) ||
                !EntityManager.TryGetComponent(cabin, out Transform transform))
                return MoveResult.Missing;
            MoveResult result = Write(cabin, transform, pose.m_Position, pose.m_Rotation);
            if (result == MoveResult.Moved)
                TagUpdated(cabin);
            return result;
        }

        private MoveResult Write(Entity cabin, Transform transform, float3 position, quaternion rotation)
        {
            // Idempotence (ChairPinSystem.cs:1168-1171).
            if (math.distancesq(position, transform.m_Position) < 1e-6f &&
                math.abs(math.dot(rotation, transform.m_Rotation)) > 0.99999f)
                return MoveResult.InPlace;
            transform.m_Position = position;
            transform.m_Rotation = rotation;
            EntityManager.SetComponentData(cabin, transform);
            return MoveResult.Moved;
        }

        /// <summary>Updated for CullingInfo, the static search tree and UpdateGroupSystem's InterpolatedTransform copy;
        /// stripped by CleanUpSystem the same frame (engine notes TRIGGERS).</summary>
        private void TagUpdated(Entity cabin)
        {
            if (EntityManager.Exists(cabin) &&
                !EntityManager.HasComponent<Deleted>(cabin) &&
                !EntityManager.HasComponent<Updated>(cabin) &&
                !EntityManager.HasComponent<Created>(cabin))
                EntityManager.AddComponent<Updated>(cabin);
        }


        private ShuttleState Gate(Run run, out string why)
        {
            if (!MovingShuttles)
            {
                why = " (moving off: developer tab)";
                return ShuttleState.Parked;
            }
            if (run.Def == null)
            {
                why = " (not one Shuttle lift type)";
                return ShuttleState.Parked;
            }
            if (!run.ProfileValid)
            {
                why = " (shuttle profile invalid)";
                return ShuttleState.Parked;
            }
            if (m_CensusBuilt)
            {
                if (!run.RowFound)
                {
                    why = " (no census row)";
                    return ShuttleState.Parked;
                }
                if (run.RowState != LiftCensus.State.Complete)
                {
                    why = run.RowState == LiftCensus.State.Open ? " (census Open)"
                        : run.RowState == LiftCensus.State.Unfinished ? " (census Unfinished)"
                        : " (census StationsOnly)";
                    return ShuttleState.Parked;
                }
                if (run.RowMixed)
                {
                    why = " (census: mixes lift types)";
                    return ShuttleState.Parked;
                }
            }
            bool closed = IsClosed(run);  // the ski area has no open run
            run.Closed = closed;           // PollPower re-gates when it changes
            // THE run predicate; the row is Complete here (checked above, or assumed while the census has not built).
            // Carries(true, power, closed) = IsPowered(power) && !closed (LiftBatch PG-1): power is named first.
            if (!LiftServedRule.Carries(true, run.Power, closed))
            {
                why = !LiftServedRule.IsPowered(run.Power) ? " at the terminals last reached (no power)"
                                                           : " at the terminals last reached (closed)";
                return ShuttleState.Held;
            }
            why = "";
            return ShuttleState.Running;
        }

        /// <summary>The shuttle's lift is closed when its ski area
        /// has no open run — SkiLiftChargeSystem.IsLiftClosed, the one per-lift signal the chairs read too (LiftClosedRule: the
        /// state the 25 % billing reads, held one game hour before it flips). No census row, or no ski area: never closed.</summary>
        private bool IsClosed(Run run)
        {
            if (!run.RowFound)
                return false;
            if (m_Charge == null)
                m_Charge = World.GetExistingSystemManaged<SkiLiftChargeSystem>();
            return m_Charge != null && m_Charge.IsLiftClosed(run.Base, run.Top);
        }

        private SkiLiftChargeSystem m_Charge;  // resolved lazily (GetExistingSystemManaged, never created here)

        /// <summary>A gate change outside a rebuild (power, kill switch): transition, latch, log.</summary>
        private void ReGate(Run run, double timeS)
        {
            ShuttleState gate = Gate(run, out string why);
            if (gate == run.State && run.StateKnown && why == run.StateWhy)
                return;
            ApplyState(run, gate, why, timeS, gate == ShuttleState.Held ? HoldHint(run, timeS) : double.NaN);
        }

        /// <summary>The terminal configuration a Held episode latches (engine notes GATE).</summary>
        private double HoldHint(Run run, double timeS)
        {
            if (run.StateKnown && run.State == ShuttleState.Running && run.Def != null)
                return ShuttleProfile.HoldFractionA(timeS, run.L, run.Def.SpeedMps, run.Def.AccelMps2, run.Def.DwellS, out _);
            double standing = StandingHoldU(run);
            if (!double.IsNaN(standing))
                return standing;
            return run.Def != null
                ? ShuttleProfile.HoldFractionA(timeS, run.L, run.Def.SpeedMps, run.Def.AccelMps2, run.Def.DwellS, out _)
                : 0.0;
        }

        /// <summary>u ∈ {0, 1} when the cabins already stand at that terminal configuration (Σ ≤ 1 m), else NaN.</summary>
        private double StandingHoldU(Run run)
        {
            double best = double.MaxValue, bestU = double.NaN;
            for (int k = 0; k < 2; k++)
            {
                double u = k;
                double cost = 0.0;
                int counted = 0;
                for (int c = 0; c < 2; c++)
                {
                    Entity cabin = run.Cabin[c];
                    if (cabin == Entity.Null || !(run.Drop[c] > 0f) || !EntityManager.Exists(cabin) ||
                        !EntityManager.TryGetComponent(cabin, out Transform t))
                        continue;
                    cost += math.distance(t.m_Position, PathTarget(run, c, u, run.Drop[c]));
                    counted++;
                }
                if (counted > 0 && cost < best)
                {
                    best = cost;
                    bestU = u;
                }
            }
            return best <= HeldStandingToleranceM ? bestU : double.NaN;
        }

        private void ApplyState(Run run, ShuttleState gate, string why, double timeS, double holdHint)
        {
            ShuttleState previous = run.State;
            bool known = run.StateKnown;
            if (gate == ShuttleState.Held)
            {
                bool keepLatch = run.HasLatch && known && previous == ShuttleState.Held;
                if (!keepLatch)
                {
                    run.HasLatch = true;
                    run.HoldU = !double.IsNaN(holdHint) ? holdHint
                        : run.Def != null ? ShuttleProfile.HoldFractionA(timeS, run.L, run.Def.SpeedMps, run.Def.AccelMps2, run.Def.DwellS, out _)
                        : 0.0;
                }
            }
            else
            {
                run.HasLatch = false;
            }
            if (known && previous != gate)
                LogState(run, previous, gate, why);
            else if (!known && run.ReturnedFromVanish)
                LogState(run, ShuttleState.Parked, gate, why);  // the pair gets a shuttle path back
            run.ReturnedFromVanish = false;
            if (!known || previous != gate)
            {
                run.PoseDirty = true;
                m_AnyPoseDirty = true;
            }
            run.State = gate;
            run.StateWhy = why;
            run.StateKnown = true;
        }

        private void LogState(Run run, ShuttleState from, ShuttleState to, string why)
        {
            string body = $"base {Ent(run.Base)} → top {Ent(run.Top)}: {from} → {to}{why}";
            float now = UnityEngine.Time.realtimeSinceStartup;
            switch (run.StateLog.Offer(now, false, body))
            {
                case LogWindow.Verdict.Plain:
                    Mod.Log.Info("TramShuttle [state] " + body);
                    break;
                case LogWindow.Verdict.Summary:
                    WriteStateSummary(run, now);
                    break;
            }
        }

        private static void WriteStateSummary(Run run, float now)
        {
            Mod.Log.Info(Inv($"TramShuttle [state] {run.StateLog.Count} more change(s) in {run.StateLog.SpanS(now):F0} s, folded (one line per ") +
                         Inv($"{run.StateLog.WindowS:0} s) — last: {run.StateLog.LastText}"));
            run.StateLog.Wrote(now);
        }

        /// <summary>The end of a [state] burst is written at most StateLogWindowS after the last line (LogWindow header).
        /// Per frame: one int compare per shuttle while nothing is folded.</summary>
        private void TrailStateLogs()
        {
            for (int r = 0; r < m_Runs.Count; r++)
            {
                LogWindow log = m_Runs[r].StateLog;
                if (log == null || log.Count == 0)
                    continue;
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (log.TrailingDue(now))
                    WriteStateSummary(m_Runs[r], now);
            }
        }

        private void FlushStateLogs()
        {
            foreach (var kv in m_RunsByPair)
            {
                Run run = kv.Value;
                if (run.StateLog != null && run.StateLog.Count > 0)
                    WriteStateSummary(run, UnityEngine.Time.realtimeSinceStartup);
            }
        }


        private void Rebuild(double timeS)
        {
            long t0 = Stopwatch.GetTimestamp();
            string why = m_DirtyWhy;
            m_Dirty = false;
            m_Immediate = false;
            m_DirtyWhy = "-";
            m_Cooldown = RebuildCooldownFrames;
            m_DiagRebuilds++;
            m_DiagLastRebuildFrame = m_RenderingSystem.frameIndex;
            m_DiagLastRebuildWhy = why;

            // 1-3. Stations, track-rope lanes, the open-chain cache.
            HarvestStations();
            if (m_StationList.Count > 0)
            {
                HarvestLanes();
                m_Cache.BuildShuttles(m_Lanes, m_StationList, m_StationTransforms, m_StationDefList, m_StationByOwnerIndex);
            }
            else
            {
                m_Lanes.Clear();
                m_Cache.Clear();
            }
            m_DiagLanes = m_Lanes.Count;
            m_LastBuildStations = m_StationList.Count;

            // 4. Roster: Shuttle carriers grouped by owner, (Index, Version) order.
            HarvestCabins();

            // 5-8. Runs: hosted poses, census rows, power, gate, cabins, enrollment.
            if (m_CensusBuilt && m_Census != null)
            {
                m_LastRevision = m_Census.Revision;
                m_Census.CopyRows(m_Rows);
            }
            BuildRuns(timeS);
            m_PowerPollFrame = m_RenderingSystem.frameIndex;  // the rebuild read the power
            ParkTheRest();
            WarnRefusals();

            double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            m_DiagRebuildMs = ms;
            if (ms > m_DiagRebuildMsMax)
                m_DiagRebuildMsMax = ms;
            LogRebuild(why, ms);
            m_AnyPoseDirty = true;
        }

        private void HarvestStations()
        {
            // The StationDefs view, fetched ONCE before the chunk loop; rows of a Shuttle type.
            IReadOnlyDictionary<Entity, StationDef> stationDefs = m_PrefabCatalog.StationDefs;
            m_StationList.Clear();
            m_StationTransforms.Clear();
            m_StationDefList.Clear();
            m_StationByOwnerIndex.Clear();
            // Complete the writers of the query's components before the main-thread GetNativeArray reads below.
            m_StationQuery.CompleteDependency();
            var chunks = m_StationQuery.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var entityHandle = GetEntityTypeHandle();
                var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                var transformHandle = GetComponentTypeHandle<Transform>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    var transforms = chunks[c].GetNativeArray(ref transformHandle);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (!stationDefs.TryGetValue(prefabRefs[i].m_Prefab, out StationDef def) || def == null)
                            continue;
                        LiftTypeDef type = SkiCatalog.LiftByIndex(def.LiftTypeIndex);
                        if (type == null || type.Mover != MoverKind.Shuttle)
                            continue;
                        m_StationList.Add(entities[i]);
                        m_StationTransforms.Add(transforms[i]);
                        m_StationDefList.Add(def);
                    }
                }
            }
            finally
            {
                chunks.Dispose();
            }

            // Station list order = entity order (Index, then Version), not chunk order: the pair notes and the rebuild signature name
            // stations by list index, and an icon clear (IconElement removed) reorders the chunks without a census revision, which the
            // watchdog read as "the shuttle set changed without an event". The pairing keys on lane order, not on station order, so the
            // sort moves no run or pairing, only the numbers in the text.
            SortStationsByEntity();

            // Station ownership map for the chain walk (ChairPinSystem.cs:1359-1370 verbatim): a station's anchors may be
            // stamped with the station itself OR one of its children (sub-objects, sub-net nodes / edges).
            for (int i = 0; i < m_StationList.Count; i++)
            {
                Entity station = m_StationList[i];
                m_StationByOwnerIndex[station.Index] = i;
                if (EntityManager.TryGetBuffer<Game.Objects.SubObject>(station, isReadOnly: true, out var subObjects))
                    for (int k = 0; k < subObjects.Length; k++)
                        m_StationByOwnerIndex[subObjects[k].m_SubObject.Index] = i;
                if (EntityManager.TryGetBuffer<Game.Net.SubNet>(station, isReadOnly: true, out var subNets))
                    for (int k = 0; k < subNets.Length; k++)
                        m_StationByOwnerIndex[subNets[k].m_SubNet.Index] = i;
                // An HV edge lane's end PathNode is built as PathNode(owner = the edge, ...) (Game.Net/LaneSystem.cs:4093-4095,
                // 1.6.0), so a terminal's owner index is the LINE's edge, never a node: there is no node to map here. The station
                // map is only ever hit when FindAnchor snapped the end to a station-owned lane; otherwise the terminal falls to
                // the position rules (dock reach, LiftLoopTopology.ClassifyShuttleTerminal).
            }
        }

        // SortStationsByEntity scratch (rebuild-only, grown once)
        private readonly List<int> m_SortIndex = new List<int>();
        private readonly List<int> m_SortVersion = new List<int>();
        private readonly List<int> m_SortOrder = new List<int>();
        private readonly List<Entity> m_SortEntities = new List<Entity>();
        private readonly List<Transform> m_SortTransforms = new List<Transform>();
        private readonly List<StationDef> m_SortDefs = new List<StationDef>();

        /// <summary>Reorders the three parallel station lists by entity (LiftLoopTopology.SortedStationOrder).</summary>
        private void SortStationsByEntity()
        {
            int n = m_StationList.Count;
            if (n < 2)
                return;
            m_SortIndex.Clear();
            m_SortVersion.Clear();
            m_SortEntities.Clear();
            m_SortTransforms.Clear();
            m_SortDefs.Clear();
            for (int i = 0; i < n; i++)
            {
                Entity e = m_StationList[i];
                m_SortIndex.Add(e.Index);
                m_SortVersion.Add(e.Version);
                m_SortEntities.Add(e);
                m_SortTransforms.Add(m_StationTransforms[i]);
                m_SortDefs.Add(m_StationDefList[i]);
            }
            LiftLoopTopology.SortedStationOrder(m_SortIndex, m_SortVersion, m_SortOrder);
            for (int k = 0; k < n; k++)
            {
                int from = m_SortOrder[k];
                m_StationList[k] = m_SortEntities[from];
                m_StationTransforms[k] = m_SortTransforms[from];
                m_StationDefList[k] = m_SortDefs[from];
            }
        }

        /// <summary>The HV lanes owned by a Shuttle line's edges and nodes (ChairPinSystem.cs:1307-1351 with the
        /// Mover test inverted). R28: the track ropes are the only HV lanes; the haul ropes (LV, 8) fail the flag.</summary>
        private bool m_LaneScanLogged;  // one cost line per load

        // Scoped harvest (ChairPinSystem.Harvest.cs is the twin): the census's
        // line edges + their end nodes are the owners (LiftCensusSystem.CopyLineOwners), their SubLane buffers the lanes
        // (LiftLaneHarvest.Scoped, Shuttle movers) — O(lift edges) instead of every utility lane of the city. The full scan
        // (FullLaneScan) runs when the census is not built, and once per load next to the first scoped harvest as the watchdog;
        // a difference is a WARN and keeps the full scan for the rest of the load.
        private readonly List<Entity> m_HarvestOwners = new List<Entity>();
        private readonly HashSet<Entity> m_HarvestSeen = new HashSet<Entity>();
        private readonly List<Entity> m_HarvestMissed = new List<Entity>();
        private readonly List<Entity> m_HarvestExtra = new List<Entity>();
        private bool m_HarvestVerified, m_HarvestFullThisLoad;   // per load (OnGameLoadingComplete)

        private void HarvestLanes()
        {
            IReadOnlyDictionary<Entity, LiftTypeDef> lineTypes = m_PrefabCatalog.LiftLineTypes;
            m_Lanes.Clear();
            double msPerTick = 1000.0 / Stopwatch.Frequency;
            int owners = m_CensusBuilt && m_Census != null && !m_HarvestFullThisLoad ? m_Census.CopyLineOwners(m_HarvestOwners) : -1;
            if (owners < 0)
            {
                long f0 = Stopwatch.GetTimestamp();
                FullLaneScan(m_Lanes, out int seen, out int hv);
                if (!m_LaneScanLogged)
                {
                    m_LaneScanLogged = true;
                    Mod.Log.Info($"TramShuttle [cost] lane harvest (first harvest of the load): full scan " +
                                 $"({(m_HarvestFullThisLoad ? "the watchdog found a difference this load" : "census not built")}): {seen} " +
                                 $"utility lane(s) in the city, {hv} of an HV lane prefab, {m_Lanes.Count} kept as tram cable lane(s), " +
                                 $"{(Stopwatch.GetTimestamp() - f0) * msPerTick:F2} ms");
                }
                return;
            }

            long s0 = Stopwatch.GetTimestamp();
            int visited = LiftLaneHarvest.Scoped(EntityManager, m_HarvestOwners, lineTypes, MoverKind.Shuttle, m_Lanes, m_HarvestSeen);
            LiftLaneHarvest.SortByLane(m_Lanes);
            double scopedMs = (Stopwatch.GetTimestamp() - s0) * msPerTick;
            if (m_HarvestVerified)
                return;

            m_HarvestVerified = true;
            var check = new List<LiftLoopCache.CableLane>();
            long w0 = Stopwatch.GetTimestamp();
            FullLaneScan(check, out int citySeen, out int cityHv);
            double watchdogMs = (Stopwatch.GetTimestamp() - w0) * msPerTick;
            LiftLaneHarvest.Compare(check, m_Lanes, m_HarvestMissed, m_HarvestExtra);
            bool same = m_HarvestMissed.Count == 0 && m_HarvestExtra.Count == 0;
            m_LaneScanLogged = true;
            Mod.Log.Info($"TramShuttle [cost] lane harvest (first scoped harvest of the load): scoped {m_Lanes.Count} tram cable lane(s) " +
                         $"from {owners} line edge/node owner(s) ({visited} SubLane entries) in {scopedMs:F3} ms; watchdog full scan " +
                         $"{citySeen} utility lane(s) in the city, {cityHv} of an HV lane prefab, {check.Count} kept, {watchdogMs:F2} ms — " +
                         (same ? "identical sets; the rest of this load harvests scoped"
                               : $"{m_HarvestMissed.Count} missed / {m_HarvestExtra.Count} extra; this harvest and the rest of the load " +
                                 "use the full scan (WARN below)"));
            if (same)
                return;
            m_HarvestFullThisLoad = true;
            if (m_HarvestMissed.Count > 0)
                Mod.Log.Warn($"TramShuttle [harvest] the city-wide lane scan found {m_HarvestMissed.Count} tram cable lane(s) the " +
                             $"census-scoped harvest missed ({LiftLaneHarvest.Describe(EntityManager, m_HarvestMissed, 5)}) — using the " +
                             "full scan for the rest of this load; please report this line (R-4 watchdog)");
            if (m_HarvestExtra.Count > 0)
                Mod.Log.Warn($"TramShuttle [harvest] the census-scoped harvest kept {m_HarvestExtra.Count} lane(s) the city-wide scan did " +
                             $"not ({LiftLaneHarvest.Describe(EntityManager, m_HarvestExtra, 5)}) — the two filters drifted; using the " +
                             "full scan for the rest of this load; please report this line (R-4 watchdog)");
            m_Lanes.Clear();
            m_Lanes.AddRange(check);
        }

        private void FullLaneScan(List<LiftLoopCache.CableLane> lanes, out int lanesSeen, out int lanesHv)
        {
            IReadOnlyDictionary<Entity, LiftTypeDef> lineTypes = m_PrefabCatalog.LiftLineTypes;
            // Every utility lane of the city is in this query; the lane PREFAB is tested first through
            // the shared cached lookup (CableLanePrefabs), so only HV-lane-prefab lanes pay for the owner read.
            lanesSeen = 0;
            lanesHv = 0;
            // Complete the writers of the query's components before the main-thread GetNativeArray reads below.
            m_LaneQuery.CompleteDependency();
            var chunks = m_LaneQuery.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var entityHandle = GetEntityTypeHandle();
                var laneHandle = GetComponentTypeHandle<Game.Net.Lane>(isReadOnly: true);
                var curveHandle = GetComponentTypeHandle<Curve>(isReadOnly: true);
                var ownerHandle = GetComponentTypeHandle<Owner>(isReadOnly: true);
                var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var laneData = chunks[c].GetNativeArray(ref laneHandle);
                    var curves = chunks[c].GetNativeArray(ref curveHandle);
                    var owners = chunks[c].GetNativeArray(ref ownerHandle);
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    lanesSeen += entities.Length;
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (!CableLanePrefabs.IsHighVoltage(EntityManager, prefabRefs[i].m_Prefab))
                            continue;
                        lanesHv++;
                        Entity ownerEntity = owners[i].m_Owner;
                        if (ownerEntity == Entity.Null ||
                            !EntityManager.TryGetComponent<PrefabRef>(ownerEntity, out var ownerPrefab) ||
                            !lineTypes.TryGetValue(ownerPrefab.m_Prefab, out LiftTypeDef lineDef) || lineDef == null ||
                            lineDef.Mover != MoverKind.Shuttle)
                            continue;
                        lanes.Add(new LiftLoopCache.CableLane
                        {
                            m_Lane = entities[i],
                            m_Owner = ownerEntity,
                            m_Bezier = curves[i].m_Bezier,
                            m_Start = laneData[i].m_StartNode,
                            m_End = laneData[i].m_EndNode,
                            m_Length = curves[i].m_Length,
                            m_TypeIndex = lineDef.Index,
                        });
                    }
                }
            }
            finally
            {
                chunks.Dispose();
            }
            LiftLaneHarvest.SortByLane(lanes);  // one stable lane order whichever harvest ran (LiftLaneHarvest header ORDER)
        }

        private void HarvestCabins()
        {
            m_OwnerCabins.Clear();
            m_HostedCount.Clear();
            m_DiagCabinsFound = 0;
            // Complete the writers of the query's components before the main-thread GetNativeArray reads below.
            m_AllCabinQuery.CompleteDependency();
            var chunks = m_AllCabinQuery.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var entityHandle = GetEntityTypeHandle();
                var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                var ownerHandle = GetComponentTypeHandle<Owner>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    var owners = chunks[c].GetNativeArray(ref ownerHandle);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (!m_Carriers.ContainsKey(prefabRefs[i].m_Prefab))
                            continue;
                        Entity owner = owners[i].m_Owner;
                        if (!m_OwnerCabins.TryGetValue(owner, out List<Entity> list))
                            m_OwnerCabins[owner] = list = new List<Entity>(2);
                        list.Add(entities[i]);
                        m_DiagCabinsFound++;
                    }
                }
            }
            finally
            {
                chunks.Dispose();
            }
            foreach (var kv in m_OwnerCabins)
            {
                kv.Value.Sort(s_ByEntity);
                m_HostedCount[kv.Key] = kv.Value.Count;
            }
            HarvestCarriages();   // the carrier grip round: the carriages and their cabin pairing (TramShuttleSystem.Carriages.cs)
        }

        private void BuildRuns(double timeS)
        {
            // A base that starts several shuttles gives its cabins to the one with the lower top (Index, Version).
            m_BaseWinner.Clear();
            m_BaseLines.Clear();
            List<LiftLoopCache.Shuttle> shuttles = m_Cache.m_Shuttles;
            for (int s = 0; s < shuttles.Count; s++)
            {
                LiftLoopCache.Shuttle sh = shuttles[s];
                m_BaseLines[sh.m_BaseStation] = m_BaseLines.TryGetValue(sh.m_BaseStation, out int n) ? n + 1 : 1;
                if (!m_BaseWinner.TryGetValue(sh.m_BaseStation, out Entity top) || CompareEntity(sh.m_TopStation, top) < 0)
                    m_BaseWinner[sh.m_BaseStation] = sh.m_TopStation;
            }

            foreach (var kv in m_RunsByPair)
                kv.Value.Seen = false;
            m_Runs.Clear();
            m_Riding.Clear();
            for (int s = 0; s < shuttles.Count; s++)
            {
                LiftLoopCache.Shuttle sh = shuttles[s];
                var key = (sh.m_BaseStation, sh.m_TopStation);
                if (!m_RunsByPair.TryGetValue(key, out Run run))
                {
                    run = new Run { StateLog = new LogWindow(StateLogWindowS) };
                    run.ReturnedFromVanish = m_VanishedPairs.Remove(key);  // "Parked → <state>" at its first gate
                    m_RunsByPair[key] = run;
                }
                run.Seen = true;
                FillRun(run, sh, timeS);
                m_Runs.Add(run);
            }
            m_PairScratch.Clear();
            foreach (var kv in m_RunsByPair)
            {
                if (!kv.Value.Seen)
                    m_PairScratch.Add(kv.Key);
            }
            for (int i = 0; i < m_PairScratch.Count; i++)
            {
                Run gone = m_RunsByPair[m_PairScratch[i]];
                // A known Run that vanished (a deleted span leaves unclosed chains, so no shuttle path) is a state
                // change to Parked; it goes through the Run's own window, and the fold is flushed just below because this
                // Run leaves m_RunsByPair (FlushStateLogs / TrailStateLogs would never see it again).
                if (gone.StateKnown)
                {
                    LogState(gone, gone.State, ShuttleState.Parked, " (no shuttle path)");
                    m_VanishedPairs.Add(m_PairScratch[i]);
                }
                if (gone.StateLog != null && gone.StateLog.Count > 0)
                    WriteStateSummary(gone, UnityEngine.Time.realtimeSinceStartup);
                m_RunsByPair.Remove(m_PairScratch[i]);
            }

            // Two lines from one base (WARN once per base per load).
            foreach (var kv in m_BaseLines)
            {
                if (kv.Value < 2 || !m_TwoLinesWarned.Add(kv.Key))  // its own set, not the cabin-count one
                    continue;
                Entity winner = m_BaseWinner[kv.Key];
                var tops = new StringBuilder();
                LiftTypeDef def = null;
                for (int s = 0; s < shuttles.Count; s++)
                {
                    if (shuttles[s].m_BaseStation != kv.Key)
                        continue;
                    def = def ?? SkiCatalog.LiftByIndex(shuttles[s].m_TypeIndex);
                    if (tops.Length > 0)
                        tops.Append(" and ");
                    tops.Append(Ent(shuttles[s].m_TopStation));
                }
                Mod.Log.Warn($"TramShuttle: base station {Ent(kv.Key)} starts {kv.Value} {(def != null ? def.Id : "shuttle")} lines " +
                             $"(to {tops}) — its cabins ride the line to {Ent(winner)}, the other{(kv.Value > 2 ? "s stay" : " stays")} parked");
            }
        }

        private void FillRun(Run run, LiftLoopCache.Shuttle sh, double timeS)
        {
            run.Sh = sh;
            run.Base = sh.m_BaseStation;
            run.Top = sh.m_TopStation;
            LiftTypeDef def = SkiCatalog.LiftByIndex(sh.m_TypeIndex);
            run.Def = def != null && def.Mover == MoverKind.Shuttle ? def : null;
            run.LA = sh.m_A.m_Length;
            run.LB = sh.m_B.m_Length;
            run.L = sh.ProfileLengthM;  // the 1 cm-quantized mean
            run.ProfileValid = run.Def != null && ShuttleProfile.Valid(run.L, run.Def.SpeedMps, run.Def.AccelMps2, run.Def.DwellS);
            run.TripS = run.ProfileValid ? ShuttleProfile.TripTimeS(run.L, run.Def.SpeedMps, run.Def.AccelMps2) : 0.0;
            run.CycleS = run.ProfileValid ? ShuttleProfile.PeriodS(run.L, run.Def.SpeedMps, run.Def.AccelMps2, run.Def.DwellS) : 0.0;
            // The bay whose cabin rides chain A = chain A's base dock (0 = DockLocalA = hosted entry 0); when that dock
            // was refused, the bay chain B does not use; default 0.
            run.BayOfA = sh.m_A.m_BaseDock >= 0 ? sh.m_A.m_BaseDock
                : sh.m_B.m_BaseDock >= 0 ? 1 - sh.m_B.m_BaseDock
                : 0;
            HostedPoses(run.Base, m_HostedScratch);
            for (int c = 0; c < 2; c++)
            {
                int bay = c == 0 ? run.BayOfA : 1 - run.BayOfA;
                run.ParkKnown[c] = bay < m_HostedScratch.Count;
                run.Park[c] = run.ParkKnown[c] ? m_HostedScratch[bay] : default(Transform);
                run.S[c] = 0.0;
            }

            // Census row: the row whose {Base, Top} is the pair, either order.
            run.RowFound = false;
            run.RowMixed = false;
            run.RowState = LiftCensus.State.Open;
            if (m_CensusBuilt)
            {
                EntityRef b = LiftCensusSystem.Ref(run.Base), t = LiftCensusSystem.Ref(run.Top);
                for (int r = 0; r < m_Rows.Count; r++)
                {
                    LiftCensus.Row row = m_Rows[r];
                    if ((row.Base.Equals(b) && row.Top.Equals(t)) || (row.Base.Equals(t) && row.Top.Equals(b)))
                    {
                        run.RowFound = true;
                        run.RowState = row.State;
                        run.RowMixed = row.Mixed;
                        break;
                    }
                }
            }
            // Power (read at the rebuild too). No row / not Complete / no census: Unknown (= powered).
            if (m_CensusBuilt && m_Census != null && run.RowFound && run.RowState == LiftCensus.State.Complete)
            {
                run.Power = m_Census.ReadLiftPower(run.Base);
                m_DiagPowerReads++;
            }
            else
            {
                run.Power = LiftPower.Unknown;
            }

            ShuttleState gate = Gate(run, out string why);
            AssignCabins(run, gate, timeS, out double holdHint);
            if (gate == ShuttleState.Held && double.IsNaN(holdHint))
                holdHint = HoldHint(run, timeS);
            ApplyState(run, gate, why, timeS, holdHint);
            run.PoseDirty = true;
            run.BoundsU = double.NaN;

            // Enroll every cabin of a shuttle that has a path (ChairPinSystem.cs:1494-1504 verbatim).
            for (int c = 0; c < 2; c++)
            {
                Entity cabin = run.Cabin[c];
                if (cabin == Entity.Null || !EntityManager.Exists(cabin) || EntityManager.HasComponent<Deleted>(cabin))
                    continue;
                if (!EntityManager.HasComponent<InterpolatedTransform>(cabin))
                {
                    var transform = EntityManager.GetComponentData<Transform>(cabin);
                    EntityManager.AddComponentData(cabin, new InterpolatedTransform(transform));
                    m_DiagEnrolled++;
                }
                if (!EntityManager.HasComponent<Updated>(cabin) && !EntityManager.HasComponent<Created>(cabin))
                    EntityManager.AddComponent<Updated>(cabin);
            }
            EnrollCarriages(run);   // the carrier grip round: the carriages ride with their cabins
        }

        /// <summary>(engine notes CABIN A / B IDENTITY) The shuttle's riders (the first two Shuttle carriers its
        /// base hosts, by (Index, Version)) and their bays. For a Held gate without a carried latch it also returns the
        /// terminal configuration the chosen assignment stands at (NaN = none within HeldStandingToleranceM).</summary>
        private void AssignCabins(Run run, ShuttleState gate, double timeS, out double holdHint)
        {
            holdHint = double.NaN;
            run.Cabin[0] = run.Cabin[1] = Entity.Null;
            run.Drop[0] = run.Drop[1] = 0f;
            run.Cabins = 0;
            if (!m_BaseWinner.TryGetValue(run.Base, out Entity winnerTop) || winnerTop != run.Top)
            {
                run.AssignRule = "none: its base rides another line";
                return;
            }
            m_OwnerCabins.TryGetValue(run.Base, out List<Entity> cabins);
            int hosted = cabins != null ? cabins.Count : 0;
            WarnCabinCount(run, hosted);
            if (hosted == 0)
            {
                run.AssignRule = "none hosted";
                return;
            }
            int m = Math.Min(2, hosted);
            Entity c0 = cabins[0], c1 = m > 1 ? cabins[1] : Entity.Null;
            float3 p0 = PositionOf(c0), p1 = m > 1 ? PositionOf(c1) : default(float3);
            float d0 = DropOf(c0), d1 = m > 1 ? DropOf(c1) : 0f;

            // Option 0: c0 → bay 0 (and c1 → bay 1); option 1: c0 → bay 1 (and c1 → bay 0).
            int option;
            string rule;
            bool keptHeld = gate == ShuttleState.Held && run.HasLatch && run.StateKnown && run.State == ShuttleState.Held;
            if (m_CabinBay.TryGetValue(c0, out int b0) && (b0 == 0 || b0 == 1) &&
                (m == 1 || (m_CabinBay.TryGetValue(c1, out int b1) && b1 == 1 - b0)))
            {
                option = b0 == 0 ? 0 : 1;
                rule = "kept";
            }
            else if (FreshFit(run, 0, m, p0, p1) || FreshFit(run, 1, m, p0, p1))
            {
                option = FreshFit(run, 0, m, p0, p1) ? 0 : 1;
                rule = "fresh base";
            }
            else
            {
                // (b) the smaller Σ |Transform − target|; Held without a carried latch tries both terminal configurations.
                double u0, u1;
                int candidates;
                if (gate == ShuttleState.Running)
                {
                    u0 = ShuttleProfile.FractionA(timeS, run.L, run.Def.SpeedMps, run.Def.AccelMps2, run.Def.DwellS, out _);
                    u1 = u0;
                    candidates = 1;
                }
                else if (gate == ShuttleState.Held && keptHeld)
                {
                    u0 = u1 = run.HoldU;
                    candidates = 1;
                }
                else if (gate == ShuttleState.Held && run.StateKnown && run.State == ShuttleState.Running && run.Def != null)
                {
                    u0 = u1 = ShuttleProfile.HoldFractionA(timeS, run.L, run.Def.SpeedMps, run.Def.AccelMps2, run.Def.DwellS, out _);
                    candidates = 1;
                }
                else if (gate == ShuttleState.Held)
                {
                    u0 = 0.0;
                    u1 = 1.0;
                    candidates = 2;
                }
                else
                {
                    u0 = u1 = double.NaN;   // Parked: the hosted poses
                    candidates = 1;
                }
                double best = double.MaxValue;
                int bestOption = 0;
                double bestU = u0;
                for (int k = 0; k < candidates; k++)
                {
                    double u = k == 0 ? u0 : u1;
                    for (int o = 0; o < 2; o++)
                    {
                        double cost = Cost(run, o, u, m, p0, d0, p1, d1);
                        // Strictly better by more than the tie tolerance: a tie keeps option 0 = (Index, Version) order.
                        if (cost < best - TieToleranceM)
                        {
                            best = cost;
                            bestOption = o;
                            bestU = u;
                        }
                    }
                }
                option = bestOption;
                double other = Cost(run, 1 - bestOption, bestU, m, p0, d0, p1, d1);
                rule = Math.Abs(other - best) <= TieToleranceM
                    ? "tie by entity"
                    : FormattableString.Invariant($"{(double.IsNaN(bestU) ? "by hosted poses" : "by clock places")}, sum {best:F2} m");
                if (gate == ShuttleState.Held && !keptHeld && candidates == 2 && best <= HeldStandingToleranceM)
                    holdHint = bestU;
            }

            int bay0 = option == 0 ? 0 : 1;
            m_CabinBay[c0] = bay0;
            SetRider(run, c0, bay0, d0);
            if (m > 1)
            {
                m_CabinBay[c1] = 1 - bay0;
                SetRider(run, c1, 1 - bay0, d1);
            }
            run.AssignRule = rule;
        }

        private void SetRider(Run run, Entity cabin, int bay, float drop)
        {
            int chain = bay == run.BayOfA ? 0 : 1;
            run.Cabin[chain] = cabin;
            run.Drop[chain] = drop;
            // A cabin with no usable drop (Drop <= 0) is only ever parked, so it is not counted among the
            // cabins on the path (hub capacity, "cabin(s) riding"); it stays a rider of this run (never ParkTheRest's).
            if (drop > 0f)
                run.Cabins++;
            m_Riding.Add(cabin);
        }

        /// <summary>Fresh-base rule (a): every cabin within FreshBaseToleranceM of its bay's hosted pose under this option.</summary>
        private static bool FreshFit(Run run, int option, int m, float3 p0, float3 p1)
        {
            int bay0 = option == 0 ? 0 : 1;
            if (!HostedPoseOfBay(run, bay0, out float3 h0) || !(math.distance(p0, h0) <= FreshBaseToleranceM))
                return false;
            if (m < 2)
                return true;
            return HostedPoseOfBay(run, 1 - bay0, out float3 h1) && math.distance(p1, h1) <= FreshBaseToleranceM;
        }

        private static bool HostedPoseOfBay(Run run, int bay, out float3 position)
        {
            int chain = bay == run.BayOfA ? 0 : 1;
            position = run.Park[chain].m_Position;
            return run.ParkKnown[chain];
        }

        /// <summary>Rule (b): Σ |Transform − target| for an option; u NaN = the hosted poses (Parked / no drop).</summary>
        private static double Cost(Run run, int option, double u, int m, float3 p0, float d0, float3 p1, float d1)
        {
            int bay0 = option == 0 ? 0 : 1;
            double cost = TargetDistance(run, bay0, u, p0, d0);
            if (m > 1)
                cost += TargetDistance(run, 1 - bay0, u, p1, d1);
            return cost;
        }

        private static double TargetDistance(Run run, int bay, double u, float3 position, float drop)
        {
            int chain = bay == run.BayOfA ? 0 : 1;
            if (!math.all(math.isfinite(position)))
                return 0.0;
            if (double.IsNaN(u) || !(drop > 0f))
                return run.ParkKnown[chain] ? math.distance(position, run.Park[chain].m_Position) : 0.0;
            return math.distance(position, PathTarget(run, chain, u, drop));
        }

        /// <summary>Where the cabin of <paramref name="chain"/> stands at fraction u (cabin A at u, cabin B at 1 − u).</summary>
        private static float3 PathTarget(Run run, int chain, double u, float drop)
        {
            double s = chain == 0 ? u * run.LA : (1.0 - u) * run.LB;
            LiftLoopCache.Sample(chain == 0 ? run.Sh.m_A : run.Sh.m_B, s, out float3 rope, out _);
            rope.y -= drop;
            return rope;
        }

        private float3 PositionOf(Entity cabin)
        {
            return EntityManager.Exists(cabin) && EntityManager.TryGetComponent(cabin, out Transform t)
                ? t.m_Position
                : new float3(float.NaN);
        }

        private float DropOf(Entity cabin)
        {
            if (!EntityManager.Exists(cabin) || !EntityManager.TryGetComponent(cabin, out PrefabRef prefabRef) ||
                !m_Carriers.TryGetValue(prefabRef.m_Prefab, out CarrierInfo info))
                return 0f;
            if (!(info.Drop > 0f) && m_Warned.Add("drop:" + info.Carrier.Prefab))
            {
                Mod.Log.Warn(Inv($"TramShuttle: cabin prefab '{info.Carrier.Prefab}' has no usable drop (grip-top {info.GripTop:F3} m − ") +
                             Inv($"{info.Carrier.GripTopAboveRopeM:F2} m above the rope = {info.Drop:F2} m) — its cabins stay parked and are not counted as riding (hub capacity) ") +
                             "(logged once per prefab per load)");
            }
            return info.Drop;
        }

        /// <summary>A shuttle's base hosting 0, 1 or more than 2 cabins (WARN once per station per load).</summary>
        private void WarnCabinCount(Run run, int hosted)
        {
            if (hosted == 2 || m_CabinWarned.Contains(run.Base))
                return;
            m_CabinWarned.Add(run.Base);
            string names = run.Def != null ? "'" + string.Join("' / '", run.Def.CarrierPrefabs) + "'" : "Shuttle carrier(s)";
            if (hosted == 0)
                Mod.Log.Warn($"TramShuttle: base station {Ent(run.Base)} hosts 0 {names} — nothing to move (its prefab lacks the " +
                             "two hosted cabins, R29, or it is still under construction)");
            else if (hosted == 1)
                Mod.Log.Warn($"TramShuttle: base station {Ent(run.Base)} hosts 1 {names} — it rides alone (R29 hosts two)");
            else
                Mod.Log.Warn($"TramShuttle: base station {Ent(run.Base)} hosts {hosted} {names} — the first 2 by entity ride, " +
                             $"{hosted - 2} stay(s) parked");
        }

        /// <summary>Every Shuttle carrier that rides no shuttle (an open, unfinished or path-less line, a second line, a third
        /// cabin) goes to a hosted pose of its owner: its mapped bay, else the nearest free one (bay map updated).</summary>
        private void ParkTheRest()
        {
            m_ParkedCabins.Clear();
            foreach (var kv in m_OwnerCabins)
            {
                Entity owner = kv.Key;
                List<Entity> cabins = kv.Value;
                int notRiding = 0;
                for (int i = 0; i < cabins.Count; i++)
                    if (!m_Riding.Contains(cabins[i]))
                        notRiding++;
                if (notRiding == 0)
                    continue;
                HostedPoses(owner, m_HostedScratch);
                m_BayTaken.Clear();
                for (int b = 0; b < m_HostedScratch.Count; b++)
                    m_BayTaken.Add(false);
                for (int i = 0; i < cabins.Count; i++)
                {
                    if (m_Riding.Contains(cabins[i]) && m_CabinBay.TryGetValue(cabins[i], out int rb) && rb >= 0 && rb < m_BayTaken.Count)
                        m_BayTaken[rb] = true;
                }
                // Pass 1: mapped cabins keep their bay; pass 2: the rest take the nearest free bay.
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int i = 0; i < cabins.Count; i++)
                    {
                        Entity cabin = cabins[i];
                        if (m_Riding.Contains(cabin) || IsParkedListed(cabin))
                            continue;
                        int bay;
                        if (pass == 0)
                        {
                            if (!m_CabinBay.TryGetValue(cabin, out bay) || bay < 0 || bay >= m_BayTaken.Count || m_BayTaken[bay])
                                continue;
                        }
                        else
                        {
                            bay = NearestFreeBay(cabin);
                        }
                        var parked = new ParkedCabin { Cabin = cabin, Owner = owner, PoseKnown = bay >= 0 };
                        if (bay >= 0)
                        {
                            m_BayTaken[bay] = true;
                            m_CabinBay[cabin] = bay;
                            parked.Pose = m_HostedScratch[bay];
                            Park(cabin, parked.Pose);
                            ParkCarriageOf(cabin, parked.Pose);   // else it stays mid-span and blocks the redraw (field test C)
                        }
                        m_ParkedCabins.Add(parked);
                    }
                }
            }
        }

        private bool IsParkedListed(Entity cabin)
        {
            for (int p = 0; p < m_ParkedCabins.Count; p++)
                if (m_ParkedCabins[p].Cabin == cabin)
                    return true;
            return false;
        }

        private int NearestFreeBay(Entity cabin)
        {
            float3 position = PositionOf(cabin);
            bool finite = math.all(math.isfinite(position));
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int b = 0; b < m_HostedScratch.Count; b++)
            {
                if (m_BayTaken[b])
                    continue;
                float d = finite ? math.distance(position, m_HostedScratch[b].m_Position) : b;
                if (best < 0 || d < bestDistance)
                {
                    best = b;
                    bestDistance = d;
                }
            }
            return best;
        }

        /// <summary>The world hosted poses of <paramref name="owner"/>'s Shuttle-carrier entries, in prefab order
        /// (entry k = bay k): ObjectUtils.LocalToWorld(owner Transform, Transform(m_Position, m_Rotation)), ObjectUtils.cs:244-250,
        /// the SubObjectSystem formula (:1757). The prefab buffer is ObjectInitializeSystem's (:703-731).</summary>
        private void HostedPoses(Entity owner, List<Transform> into)
        {
            into.Clear();
            if (owner == Entity.Null || !EntityManager.Exists(owner) ||
                !EntityManager.TryGetComponent(owner, out Transform ownerTransform) ||
                !EntityManager.TryGetComponent(owner, out PrefabRef ownerPrefab) ||
                !EntityManager.TryGetBuffer<Game.Prefabs.SubObject>(ownerPrefab.m_Prefab, isReadOnly: true, out var entries))
                return;
            for (int i = 0; i < entries.Length; i++)
            {
                Game.Prefabs.SubObject entry = entries[i];
                if (m_Carriers.ContainsKey(entry.m_Prefab))
                    into.Add(ObjectUtils.LocalToWorld(ownerTransform, new Transform(entry.m_Position, entry.m_Rotation)));
            }
        }

        /// <summary>A census-Complete Shuttle lift with no path, and the dock-run refusals of a built shuttle (WARN once
        /// per text per load). Without a census every pair-level refusal is written once.</summary>
        private void WarnRefusals()
        {
            List<string> notes = m_Cache.m_BuildNotes;
            if (m_CensusBuilt)
            {
                for (int r = 0; r < m_Rows.Count; r++)
                {
                    LiftCensus.Row row = m_Rows[r];
                    if (row.State != LiftCensus.State.Complete || row.Mixed)
                        continue;
                    LiftTypeDef def = SkiCatalog.LiftByIndex(row.Type);
                    if (def == null || def.Mover != MoverKind.Shuttle)
                        continue;
                    Entity b = LiftCensusSystem.Ent(row.Base), t = LiftCensusSystem.Ent(row.Top);
                    if (FindRun(b, t) != null)
                        continue;
                    string text = $"TramShuttle [rebuild]: {def.Id} lift base {Ent(b)} → top {Ent(t)} is Complete in the census but " +
                                  $"its track ropes give no shuttle path ({PairNotes(b, t)}{ChainTerminalNotes()}) — its cabins stay parked";
                    if (m_Warned.Add(text))
                        Mod.Log.Warn(text);
                }
            }
            else
            {
                for (int n = 0; n < notes.Count; n++)
                {
                    string note = notes[n];
                    if (!note.StartsWith("stations (", StringComparison.Ordinal) && !note.StartsWith("shuttle between", StringComparison.Ordinal))
                        continue;
                    if (note.IndexOf(": shuttle, path", StringComparison.Ordinal) >= 0)
                        continue;   // a success note
                    string text = "TramShuttle [rebuild]: no census rows this load — " + note + ChainTerminalNotes();
                    if (m_Warned.Add(text))
                        Mod.Log.Warn(text);
                }
            }
            for (int r = 0; r < m_Runs.Count; r++)
            {
                Run run = m_Runs[r];
                string prefix = PairPrefix(run.Sh.m_BaseStationIndex, run.Sh.m_TopStationIndex);
                for (int n = 0; n < notes.Count; n++)
                {
                    string note = notes[n];
                    if (!note.StartsWith(prefix, StringComparison.Ordinal) || note.IndexOf(" dock run ", StringComparison.Ordinal) < 0)
                        continue;
                    string text = $"TramShuttle [rebuild]: base {Ent(run.Base)} → top {Ent(run.Top)}: {note.Substring(prefix.Length)}";
                    if (m_Warned.Add(text))
                        Mod.Log.Warn(text);
                }
            }
        }

        /// <summary>The build notes of a station pair (LiftLoopCache's pair keys are station LIST indices, lo before hi).</summary>
        private string PairNotes(Entity a, Entity b)
        {
            int i = m_StationList.IndexOf(a), j = m_StationList.IndexOf(b);
            if (i < 0 || j < 0)
                return "a station is not in the shuttle station scan";
            string prefix = PairPrefix(i, j);
            string between = $"shuttle between stations {Math.Min(i, j)} and {Math.Max(i, j)}";
            var sb = new StringBuilder();
            List<string> notes = m_Cache.m_BuildNotes;
            for (int n = 0; n < notes.Count; n++)
            {
                if (!notes[n].StartsWith(prefix, StringComparison.Ordinal) && !notes[n].StartsWith(between, StringComparison.Ordinal))
                    continue;
                if (sb.Length > 0)
                    sb.Append("; ");
                sb.Append(notes[n]);
            }
            if (sb.Length == 0)
                sb.Append($"no track-rope chain runs between stations ({Math.Min(i, j)},{Math.Max(i, j)}): " +
                          $"{m_Cache.m_ChainCount} chain(s), {m_Cache.m_UnclosedChainCount} unclosed");
            return sb.ToString();
        }

        /// <summary>The per-chain terminal diagnostics the topology keeps (LiftLoopCache.m_ChainDiags): for every track-rope
        /// chain, its link count and, per terminal, the
        /// station index it classified to (-1 = none), the PathNode owner index, how it was decided ("owner", "child",
        /// "pos 8.3m", "dock 12.1m", or "none (owner N, nearest station Xm, nearest dock Ym)") and the world position.
        /// Empty when the build saw no chain. Appended to the [rebuild] WARNs, which fire once per text per load
        /// (m_Warned), so the numbers reach the log the first time a Complete lift has no path.</summary>
        private string ChainTerminalNotes()
        {
            List<LiftLoopCache.ChainDiag> diags = m_Cache.m_ChainDiags;
            if (diags.Count == 0)
                return "";
            var sb = new StringBuilder(160 * diags.Count);
            sb.Append("; chain terminals: ");
            for (int c = 0; c < diags.Count; c++)
            {
                LiftLoopCache.ChainDiag d = diags[c];
                if (c > 0)
                    sb.Append(" | ");
                sb.Append(Inv($"chain {c} ({d.m_Links} lane(s)): start station {d.m_StartStation} owner {d.m_StartOwner} [{d.m_StartHow}] ")).Append(Pos(d.m_StartPos))
                  .Append(Inv($" → end station {d.m_EndStation} owner {d.m_EndOwner} [{d.m_EndHow}] ")).Append(Pos(d.m_EndPos));
            }
            return sb.ToString();
        }

        private static string Pos(float3 p) => Inv($"at ({p.x:F0}, {p.y:F0}, {p.z:F0})");

        private static string PairPrefix(int i, int j)
        {
            return $"stations ({Math.Min(i, j)},{Math.Max(i, j)}): ";
        }

        private void LogRebuild(string why, double ms)
        {
            int riding = CountRiding();
            var sig = new StringBuilder(256);
            var text = new StringBuilder(256);
            string head = Inv($"{m_Runs.Count} shuttle(s), {riding} cabin(s) riding, {m_ParkedCabins.Count} parked ({m_StationList.Count} ") +
                          Inv($"station(s), {m_Lanes.Count} track-rope lane(s))");
            sig.Append(head);
            text.Append(head);
            for (int r = 0; r < m_Runs.Count; r++)
            {
                Run run = m_Runs[r];
                LiftLoopCache.Shuttle sh = run.Sh;
                string upLane = sh.m_UpLaneDecided ? sh.m_UpLaneSwapped ? "chain order swapped" : "chain order kept" : "undecided — chain order kept";
                string cabinA = run.Cabin[0] != Entity.Null ? Ent(run.Cabin[0]) : "none";
                string cabinB = run.Cabin[1] != Entity.Null ? Ent(run.Cabin[1]) : "none";
                string part =
                    Inv($"base {Ent(run.Base)} → top {Ent(run.Top)} ({(run.Def != null ? run.Def.Id : "not a Shuttle type")}; ") +
                    Inv($"base {(sh.m_BaseByRole ? "by role" : "by height")}; path A {sh.m_A.m_Length:F1} m / B {sh.m_B.m_Length:F1} m; ") +
                    Inv($"docks base {sh.m_A.m_BaseRunM:F1} / {sh.m_B.m_BaseRunM:F1} m, top {sh.m_A.m_TopRunM:F1} / {sh.m_B.m_TopRunM:F1} m; ") +
                    Inv($"up lane right looking uphill, {upLane}; cabin A {cabinA}, cabin B {cabinB}");
                sig.Append(r == 0 ? " — " : ", ").Append(part).Append(')');
                text.Append(r == 0 ? " — " : ", ").Append(part)
                    .Append(Inv($" (cabins {run.AssignRule}); trip {run.TripS:F1} s, cycle {run.CycleS:F1} s; {run.State}{run.StateWhy})"));
            }
            List<string> notes = m_Cache.m_BuildNotes;
            for (int n = 0; n < notes.Count; n++)
                sig.Append(" | ").Append(notes[n]);
            string signature = sig.ToString();
            if (signature == m_LastSignature)
                return;
            if (why == "watchdog" && m_LastSignature != null && !m_WatchdogWarned)
            {
                m_WatchdogWarned = true;
                Mod.Log.Warn($"TramShuttle [watchdog] the shuttle set changed without an event (was: {m_LastSignature}; now: " +
                             $"{signature}) — rebuilt (logged once per load; please report this line)");
            }
            m_LastSignature = signature;
            Mod.Log.Info(FormattableString.Invariant($"TramShuttle [rebuild] {text}; why: {why}; {ms:F2} ms"));
        }

        private int CountRiding()
        {
            int riding = 0;
            for (int r = 0; r < m_Runs.Count; r++)
                riding += m_Runs[r].Cabins;
            return riding;
        }

        private Run FindRun(Entity a, Entity b)
        {
            for (int r = 0; r < m_Runs.Count; r++)
            {
                Run run = m_Runs[r];
                if ((run.Base == a && run.Top == b) || (run.Base == b && run.Top == a))
                    return run;
            }
            return null;
        }


        /// <summary>The Shuttle carrier prefabs FOUND this load (any SkiCatalog lift type whose Mover is Shuttle), rebuilt only
        /// when the catalog's Generation moves (a load or a re-resolution).</summary>
        private void RefreshCarriers()
        {
            int generation = m_PrefabCatalog.Generation;
            if (generation == m_CarrierGeneration)
                return;
            m_CarrierGeneration = generation;
            m_Carriers.Clear();
            IReadOnlyDictionary<Entity, float> grips = m_PrefabCatalog.CarrierGripTops;
            IReadOnlyList<LiftTypeDef> lifts = SkiCatalog.Lifts;
            for (int l = 0; l < lifts.Count; l++)
            {
                LiftTypeDef def = lifts[l];
                if (def.Mover != MoverKind.Shuttle)
                    continue;
                for (int c = 0; c < def.Carriers.Count; c++)
                {
                    CarrierDef carrier = def.Carriers[c];
                    Entity prefab = m_PrefabCatalog.EntityOf(carrier.Prefab);
                    if (prefab == Entity.Null)
                        continue;
                    float grip = grips.TryGetValue(prefab, out float g) ? g : 0f;
                    m_Carriers[prefab] = new CarrierInfo
                    {
                        Def = def,
                        Carrier = carrier,
                        GripTop = grip,
                        Drop = grip > 0f ? LiftMoverMath.CarrierDrop(grip, carrier.GripTopAboveRopeM) : 0f,
                    };
                }
            }
            MarkDirty("prefab catalog resolved", immediate: true);
        }

        /// <summary>The idle line, once per load (no Shuttle carrier prefab FOUND).</summary>
        private void LogIdle()
        {
            m_ArmLogged = true;
            m_IdleLogged = true;
            var pending = new StringBuilder();
            var missing = new StringBuilder();
            var found = new StringBuilder();
            IReadOnlyList<LiftTypeDef> lifts = SkiCatalog.Lifts;
            for (int l = 0; l < lifts.Count; l++)
            {
                LiftTypeDef def = lifts[l];
                if (def.Mover != MoverKind.Shuttle)
                    continue;
                for (int c = 0; c < def.Carriers.Count; c++)
                {
                    CarrierDef carrier = def.Carriers[c];
                    AppendItem(carrier.Expected ? missing : pending, $"'{carrier.Prefab}' ({def.Id} carrier)");
                }
                if (m_PrefabCatalog.LineEntity(def) != Entity.Null)
                    AppendItem(found, $"'{def.LinePrefab}' ({def.Id} line)");
                else
                    AppendItem(def.LineExpected ? missing : pending, $"'{def.LinePrefab}' ({def.Id} line)");
            }
            string line = "TramShuttle [arm]: idle this load — no Shuttle carrier prefab found (" +
                          (pending.Length > 0 ? "pending, not imported yet: " + pending : "nothing pending") +
                          (missing.Length > 0 ? "; MISSING (expected): " + missing : "") +
                          (found.Length > 0 ? "; found: " + found : "") + ")";
            if (missing.Length > 0)
                Mod.Log.Warn(line);
            else
                Mod.Log.Info(line);
        }

        /// <summary>The arm line, once per load per Shuttle lift type, by the first update after the census gate; WARN
        /// with the CHECK list when a prefab number disagrees with the expected geometry.</summary>
        private void LogArm(bool censusBuilt)
        {
            m_ArmLogged = true;
            IReadOnlyDictionary<Entity, float> grips = m_PrefabCatalog.CarrierGripTops;
            IReadOnlyList<LiftTypeDef> lifts = SkiCatalog.Lifts;
            IReadOnlyList<StationDef> stations = SkiCatalog.Stations;
            var hosted = new List<Transform>();
            for (int l = 0; l < lifts.Count; l++)
            {
                LiftTypeDef def = lifts[l];
                if (def.Mover != MoverKind.Shuttle)
                    continue;
                var sb = new StringBuilder(512);
                var checks = new List<string>();
                bool lineFound = m_PrefabCatalog.LineEntity(def) != Entity.Null;
                sb.Append("TramShuttle [arm]: ").Append(def.Id).Append(" — line '").Append(def.LinePrefab).Append("' ")
                  .Append(FoundText(lineFound, def.LineExpected));
                if (!lineFound && def.LineExpected)
                    checks.Add($"the expected line '{def.LinePrefab}' is MISSING");

                float drop = 0f;
                bool dropKnown = false;
                for (int c = 0; c < def.Carriers.Count; c++)
                {
                    CarrierDef carrier = def.Carriers[c];
                    Entity prefab = m_PrefabCatalog.EntityOf(carrier.Prefab);
                    sb.Append(", cabin '").Append(carrier.Prefab).Append("' ");
                    if (prefab == Entity.Null)
                    {
                        sb.Append(FoundText(false, carrier.Expected));
                        if (carrier.Expected)
                            checks.Add($"the expected cabin '{carrier.Prefab}' is MISSING");
                        continue;
                    }
                    if (!grips.TryGetValue(prefab, out float grip))
                    {
                        sb.Append("FOUND without usable bounds (its cabins stay parked)");
                        checks.Add($"cabin '{carrier.Prefab}' has no usable bounds");
                        continue;
                    }
                    float d = LiftMoverMath.CarrierDrop(grip, carrier.GripTopAboveRopeM);
                    sb.Append(FormattableString.Invariant(
                        $"FOUND (grip-top {grip:F3} m, {carrier.GripTopAboveRopeM:F2} above the rope: hangs {d:F2} m below the track rope)"));
                    if (!(d > 0f))
                        checks.Add(FormattableString.Invariant($"cabin '{carrier.Prefab}' drop {d:F2} m ≤ 0 — it never rides"));
                    if (!dropKnown)
                    {
                        drop = d;
                        dropKnown = true;
                    }
                }

                double trip1k = ShuttleProfile.TripTimeS(1000.0, def.SpeedMps, def.AccelMps2);
                double cycle1k = ShuttleProfile.PeriodS(1000.0, def.SpeedMps, def.AccelMps2, def.DwellS);
                double capacity1k = ShuttleProfile.CapacityPerHour(2, def.SeatsPerCarrier, 1000.0, def.SpeedMps, def.AccelMps2, def.DwellS);
                sb.Append(Inv($"; profile {def.SpeedMps:0.##} m/s, {def.AccelMps2:0.##} m/s², dwell {def.DwellS:0.##} s (trip {trip1k:0.#} s, "))
                  .Append(Inv($"cycle {cycle1k:0.#} s, {capacity1k:F0} riders/h at 1 km)"));
                if (!ShuttleProfile.Valid(1000.0, def.SpeedMps, def.AccelMps2, def.DwellS))
                    checks.Add("the shuttle profile is invalid (speed, acceleration or dwell)");

                for (int s = 0; s < stations.Count; s++)
                {
                    StationDef st = stations[s];
                    if (st.LiftTypeIndex != def.Index)
                        continue;
                    bool isBase = st.Role == StationRole.Base;
                    Entity stationPrefab = m_PrefabCatalog.EntityOf(st.Prefab);
                    bool docksFinite = math.all(math.isfinite(st.DockLocalA)) && math.all(math.isfinite(st.DockLocalB));
                    sb.Append("; ").Append(isBase ? "base" : "top").Append(" '").Append(st.Prefab).Append("' ")
                      .Append(FoundText(stationPrefab != Entity.Null, st.Expected))
                      .Append(" (docks ").Append(Vec2(st.DockLocalA)).Append(" / ").Append(Vec2(st.DockLocalB));
                    if (stationPrefab == Entity.Null)
                    {
                        if (st.Expected)
                            checks.Add($"the expected station '{st.Prefab}' is MISSING");
                        sb.Append(')');
                        continue;
                    }
                    if (!docksFinite)
                        checks.Add($"'{st.Prefab}' has no docks (NaN): its cabins dwell at the rope ends");
                    HostedLocal(stationPrefab, hosted);
                    if (isBase || hosted.Count > 0)
                    {
                        sb.Append("; its prefab hosts ").Append(hosted.Count).Append(" cabin(s)");
                        for (int k = 0; k < hosted.Count; k++)
                            sb.Append(k == 0 ? " at " : " / ").Append(Vec2(hosted[k].m_Position));
                    }
                    sb.Append(')');
                    if (isBase && hosted.Count != 2)
                        checks.Add($"base '{st.Prefab}' hosts {hosted.Count} cabin(s), want 2 (R29)");
                    if (!isBase && hosted.Count > 0)
                        checks.Add($"top '{st.Prefab}' hosts {hosted.Count} cabin(s), want none (R29)");
                    if (hosted.Count > 0 && !st.HostsCarriers)
                        checks.Add($"'{st.Prefab}' hosts {hosted.Count} cabin(s) but its StationDef.HostsCarriers is false");
                    if (isBase && hosted.Count == 0 && st.HostsCarriers)
                        checks.Add($"'{st.Prefab}' has HostsCarriers true but its prefab hosts no cabin");
                    for (int k = 0; k < hosted.Count && k < 2; k++)
                    {
                        float3 dock = k == 0 ? st.DockLocalA : st.DockLocalB;
                        if (dropKnown && math.all(math.isfinite(dock)))
                        {
                            float3 expected = dock - new float3(0f, drop, 0f);
                            float off = math.distance(hosted[k].m_Position, expected);
                            if (off > HostedCheckToleranceM)
                                checks.Add(FormattableString.Invariant(
                                    $"hosted cabin {k} of '{st.Prefab}' at {Vec2(hosted[k].m_Position)} is {off:F2} m off its dock − drop {Vec2(expected)}"));
                        }
                        if (math.abs(math.dot(hosted[k].m_Rotation, quaternion.identity)) < 0.99999f)
                            checks.Add($"hosted cabin {k} of '{st.Prefab}' is rotated (R29 wants identity)");
                    }
                }
                sb.Append("; clock (frameIndex + frameTime) / 60 s (ChairPin's); census gate ")
                  .Append(censusBuilt ? "built" : $"not built after {CensusGateFrames} frames");
                if (checks.Count > 0)
                {
                    sb.Append("; CHECK: ").Append(string.Join("; ", checks));
                    Mod.Log.Warn(sb.ToString());
                }
                else
                {
                    Mod.Log.Info(sb.ToString());
                }
            }
        }

        /// <summary>The prefab-local hosted entries (Shuttle carriers) of a station PREFAB, in order.</summary>
        private void HostedLocal(Entity stationPrefab, List<Transform> into)
        {
            into.Clear();
            if (!EntityManager.TryGetBuffer<Game.Prefabs.SubObject>(stationPrefab, isReadOnly: true, out var entries))
                return;
            for (int i = 0; i < entries.Length; i++)
            {
                if (m_Carriers.ContainsKey(entries[i].m_Prefab))
                    into.Add(new Transform(entries[i].m_Position, entries[i].m_Rotation));
            }
        }

        private static string FoundText(bool found, bool expected)
        {
            return found ? "FOUND" : expected ? "MISSING" : "pending (not imported)";
        }

        private static void AppendItem(StringBuilder sb, string item)
        {
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append(item);
        }


        private static int CompareEntity(Entity a, Entity b)
        {
            if (a.Index != b.Index)
                return a.Index < b.Index ? -1 : 1;
            return a.Version.CompareTo(b.Version);
        }

        /// <summary>Invariant-culture formatting for log lines. C# 9 converts only a single interpolated string to
        /// FormattableString, so long lines are concatenations of several Inv(...) calls.</summary>
        private static string Inv(FormattableString text) => FormattableString.Invariant(text);
        private static string Ent(Entity e) => FormattableString.Invariant($"{e.Index}:{e.Version}");
        private static string Vec(float3 v) => FormattableString.Invariant($"({v.x:F1},{v.y:F1},{v.z:F1})");
        private static string Vec2(float3 v) => FormattableString.Invariant($"({v.x:F2}, {v.y:F2}, {v.z:F2})");
        private static string DockName(sbyte dock) => dock == 0 ? "A" : dock == 1 ? "B" : "-";
    }
}
