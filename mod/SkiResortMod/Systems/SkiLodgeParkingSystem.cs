// Lodge parking census: for every active Base lodge on a Welcome Sign, the car spaces within 300 m (street parking lanes,
// lanes and garages of parking lots and halls; not boarding-only, bicycle, service or private spaces), counted by a Burst
// job over the lane search tree and the parking facilities. SkiDaySystem reads it every pass (TryGetParking, hub row 21
// "No parking near the lodge"); the stats DUMP prints it (AppendDump). The rules are SkiParkingRules'; the lodge verdict is
// SkiDayRules.CheckLodge. In memory only, reset on load.
// Only lodges whose SkiCatalog row has LodgeRoadRule.Base are counted (the ski-day targets): mountain lodges (hut, summit:
// roadless by design) and patrol huts never get a parking row, so row 21 can never name them. The tier comes from
// SkiResortAttractivenessSystem.CopyActiveLodgeInfo; mountain lodges skipped by the
// last census are counted in the DUMP's system line.
//
// Phase: GameSimulation, UpdateAt (Mod.cs, after SkiDaySystem's line). Not anchored: the census reads the attraction
// snapshot's lodge list and the lane search tree through the reader handle, so no frame order is needed. Pause stops it;
// the Editor never arms it.
// Cadence: GetUpdateInterval 16 (one tick is one int compare unless a census is due). A census is due when
// SkiResortAttractivenessSystem.LodgeSetRevision moved, when the parking signature moved (parking-lane entity count × 31 +
// parking-facility count, read every SignatureTicks = 16 ticks = 256 frames with CalculateEntityCount, which never syncs; a
// road built anywhere also moves it, which is fine), and every BackstopTicks = 256 ticks (4,096 frames). A swap that keeps
// the parking-lane count (a Gravel Road upgraded to a Small Road) does not move the signature and waits for the backstop;
// a component order version is no better a trigger (every car parking on a street lane moves it). The job scheduled on one
// tick is completed and applied on the next, so the main thread never waits.
//
// The census (ParkingCensusJob, one Burst IJob), per (lodge, sign) pair from CopySigns / CopyActiveLodgeInfo:
//   1. State = SkiDayRules.CheckLodge(IsLodgePrefab, OnLiveSign, Active = Building && !Inactive (BuildingUtils.cs:351),
//      Destroyed = Destroyed || Deleted || no Building, LeisureTagged = Game.Buildings.LeisureProvider (Tourism Overhaul
//      removes / restores it), HasRoad = Building.m_RoadEdge, NearestRunOrStationM = 0: slope-side is SkiDaySystem's to
//      judge). No position gives Done = false (DUMP "no census (no position)").
//   2. Lane pass: the game's lane search tree (SearchSystem.GetLaneSearchTree(readOnly), SearchSystem.cs:839-843; it holds
//      every lane with LaneGeometry or ParkingLane, :733-741) over the lodge xz ± 300 m (box, LaneBoxIterator: the house
//      iterator shape, SkiGladeToolSystem.TreeBoundsIterator); each hit with a Game.Net.ParkingLane: d = MathUtils.Distance(
//      curve xz, lodge xz); restricted = SkiParkingRules.IsRestricted(m_AccessRestriction set and not our sign or lodge, the
//      lane's AllowEnter | AllowExit). ParkingLaneDataSystem sets the restriction to the owning building for every building
//      lane (:383-398) and the two allow flags from its Restricted* building flags (:225-279), which is what the
//      pathfinder honors (PathUtils.cs:1195, :1220-1225). Verdict SkiParkingRules.ClassifyLane. Count = NetUtils.
//      GetParkingSlotCount (NetUtils.cs:954-957, the roads info view's own count, RoadsInfoviewUISystem.cs:134-173) and the
//      LaneObjects that are ParkedCars; the owner walk (<= MaxOwnerDepth) splits street (no building on the chain) from lots
//      (any building: a parking lot or hall, or a park's or shop's lot the game opens to outsiders) for the DUMP only.
//   3. Garage pass: every CarParkingFacility building within 300 m (its Transform; not an upgrade, whose garage lanes sit on
//      its root, m_FacilityWalkQuery); its GarageLane connection lanes reached through SubLane, SubNet → SubLane,
//      SubObject → SubLane and SubObject → SubObject → SubLane (the info view's walk, depth fixed at 2); verdict
//      SkiParkingRules.ClassifyGarage with the connection lane's own restriction and AllowEnter | AllowExit; Count =
//      m_VehicleCapacity spaces, m_VehicleCount parked. Garage lanes are connection lanes (RoadConnectionSystem.cs:1374-1399,
//      :1445-1448), not in the lane tree, and lot ParkingLanes come only from pass 2, so nothing is counted twice.
//   Reader handshake (SearchSystem.cs:839-843, :855-858; vanilla FixParkingLocationSystem.cs:1847, :1856): the tree's write
//   dependency joins the schedule, AddLaneSearchTreeReader(handle) after it; the tree field is [ReadOnly]
//   (FixParkingLocationSystem.cs:928). Its read-only Transform, Curve and LaneObject lookups make the next frame's vanilla
//   writers of those types wait for it as a reader (a job edge, not a main-thread stall).
//
// Flags: SkiParkingRules' constants are asserted against Game.Net.ParkingLaneFlags / ConnectionLaneFlags / RoadTypes at
// arm time (s_FlagsMatch; the enum values are folded in at compile time, so only a rebuild against a Game.dll whose values
// moved can trip it): a mismatch is one WARN and the census is off for the load.
// Cost (estimates; the [cost] line measures): idle tick = one int compare (two CalculateEntityCount every 16th tick);
// census tick = <= 10 signs × <= 10 lodges copied, one async chunk list, one schedule; worker = every lane of a 600 × 600 m
// box per lodge (thousands in town, unverified) + every parking facility. Measured at the CostCensuses-th census with
// lodges after each load, which is completed synchronously: its dependencies first (the vanilla writers of the lookups'
// types, the one main-thread sync of a measurement), then schedule + complete (the job's own time, "job"; a Stopwatch
// cannot run inside a Burst job), then applied at once. Re-armed once per game day at the first tick at or after 12:00
// (SkiDayRules.DayKey; only once the last line was written); a census on the arming tick is not measured. The line prints
// the vanilla wait (tRun − tDeps, "not ours") on its own and ends "; measurement n this load, YYYY-MM at HH:MM"
// (SkiGuestCounterSystem.CostSuffix).
// Logs: created; ARMED; `Parking [census]` per lodge whose Done, State or car spaces changed (or new), through one
// LogWindow(10 s) for all lodges (trailing flush each tick, preload, destroy); `Parking [idle]` on each transition to no
// lodge; `Parking [cost]` after the load and daily at midday; the flag WARN; the one-shot `!!!` (disabled until the next
// load). DUMP: the system line, one line per lodge (managed copies only, made in ApplyCensus after Complete).
// Contract (house pattern): "created" line; per-load reset and re-arm in OnGameLoadingComplete (Game mode only);
// OnGamePreload completes and discards the census in flight and flushes the fold (try/caught: GameSystemBase.cs:85-96
// disables a system whose OnGamePreload throws); OnDestroy completes, flushes and disposes.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Colossal.Collections;          // NativeQuadTree / INativeQuadTreeIterator (Colossal.Collections.dll)
using Colossal.Mathematics;          // Bounds2, MathUtils
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Simulation;
using Game.Tools;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using NetSearchSystem = Game.Net.SearchSystem;

namespace SkiResortMod.Systems
{
    /// <summary>One lodge's last parking census.</summary>
    public struct LodgeParking
    {
        public Entity Lodge, Sign;
        public bool Done;                    // false: the lodge had no Transform (gone)
        public SkiDayRules.LodgeReject State; // at the census (CheckLodge)
        public int StreetSpaces, LotSpaces, GarageSpaces, StreetParked, LotParked, GarageParked;
        public int ClosedLanes, PrivateLanes, OtherLanes;   // excluded (DUMP)
        public float NearestM;               // nearest counted lane / garage building, −1 none
        public uint Frame;                   // SimulationSystem.frameIndex at schedule
        public int CarSpaces => StreetSpaces + LotSpaces + GarageSpaces;
        public int Parked => StreetParked + LotParked + GarageParked;
    }

    public sealed partial class SkiLodgeParkingSystem : GameSystemBase
    {
        private static class Tuning
        {
            /// <summary>At most one census line per this many wall-clock seconds, all lodges together.</summary>
            public const float LogWindowS = 10f;
            /// <summary>The census with lodges that is timed for the `Parking [cost]` line (after the load, then after each midday re-arm).</summary>
            public const int CostCensuses = 4;
        }

        /// <summary>SkiParkingRules' constants equal the Game.dll enums this build was compiled against.</summary>
        private static readonly bool s_FlagsMatch =
            (uint)Game.Net.ParkingLaneFlags.VirtualLane == SkiParkingRules.LaneFlagVirtual &&
            (uint)Game.Net.ParkingLaneFlags.ParkingDisabled == SkiParkingRules.LaneFlagParkingDisabled &&
            (uint)Game.Net.ParkingLaneFlags.SpecialVehicles == SkiParkingRules.LaneFlagSpecialVehicles &&
            (uint)(Game.Net.ParkingLaneFlags.AllowEnter | Game.Net.ParkingLaneFlags.AllowExit) == SkiParkingRules.LaneFlagAllowEnterExit &&
            (uint)Game.Net.ConnectionLaneFlags.Disabled == SkiParkingRules.ConnFlagDisabled &&
            (uint)(Game.Net.ConnectionLaneFlags.AllowEnter | Game.Net.ConnectionLaneFlags.AllowExit) == SkiParkingRules.ConnFlagAllowEnterExit &&
            (byte)Game.Net.RoadTypes.Car == SkiParkingRules.RoadTypeCar;

        private SimulationSystem m_SimulationSystem;
        private TimeSystem m_TimeSystem;   // midday re-arm and the [cost] clock
        private SkiResortAttractivenessSystem m_Attractiveness;
        private NetSearchSystem m_NetSearch;
        private Game.UI.NameSystem m_NameSystem;

        // Queries, handles, lookups (registered in OnCreate: the system's Dependency carries their writers)
        private EntityQuery m_FacilityQuery, m_FacilityWalkQuery, m_ParkingLaneQuery;
        private EntityTypeHandle m_EntityType;
        private ComponentLookup<Game.Objects.Transform> m_TransformLookup;
        private ComponentLookup<Building> m_BuildingLookup;
        private ComponentLookup<Destroyed> m_DestroyedLookup;
        private ComponentLookup<Deleted> m_DeletedLookup;
        private ComponentLookup<LeisureProvider> m_LeisureProviderLookup;
        private ComponentLookup<Game.Net.ParkingLane> m_ParkingLaneLookup;
        private ComponentLookup<Game.Net.GarageLane> m_GarageLaneLookup;
        private ComponentLookup<Game.Net.ConnectionLane> m_ConnectionLaneLookup;
        private ComponentLookup<Game.Net.Curve> m_CurveLookup;
        private ComponentLookup<Owner> m_OwnerLookup;
        private ComponentLookup<Game.Prefabs.PrefabRef> m_PrefabRefLookup;
        private ComponentLookup<Game.Prefabs.ParkingLaneData> m_ParkingLaneDataLookup;
        private ComponentLookup<CarParkingFacility> m_CarParkingFacilityLookup;
        private ComponentLookup<Game.Routes.CarParking> m_CarParkingLookup;
        private ComponentLookup<Game.Vehicles.ParkedCar> m_ParkedCarLookup;
        private BufferLookup<Game.Net.LaneObject> m_LaneObjectLookup;
        private BufferLookup<Game.Net.SubLane> m_SubLaneLookup;
        private BufferLookup<Game.Net.SubNet> m_SubNetLookup;
        private BufferLookup<Game.Objects.SubObject> m_SubObjectLookup;

        // Job data (Persistent; the job owns them while in flight)
        private NativeList<Entity> m_JobLodges, m_JobSigns;
        private NativeList<LodgeParking> m_Results;
        private NativeList<int> m_LodgeHits;       // lane-tree hits per lodge (the [cost] line's "at most for one lodge")
        private NativeArray<int> m_Stats;          // [0] lane hits, [1] parking buildings in reach (summed over lodges)
        private JobHandle m_Handle;
        private bool m_InFlight;
        private uint m_JobFrame;

        // Managed state (what the accessors and the DUMP read)
        private readonly Dictionary<Entity, LodgeParking> m_ByLodge = new Dictionary<Entity, LodgeParking>();
        private readonly List<Entity> m_Order = new List<Entity>(8);       // m_ByLodge's keys in census order (DUMP)
        private readonly HashSet<Entity> m_Seen = new HashSet<Entity>();
        private readonly List<Entity> m_Drop = new List<Entity>(2);
        private readonly List<Entity> m_Signs = new List<Entity>(4);
        private readonly List<Entity> m_Lodges = new List<Entity>(4);
        // The snapshot's lodge tiers (seam S5) and the mountain lodges the last census skipped (DUMP).
        private readonly List<SkiResortAttractivenessSystem.LodgeInfo> m_LodgeInfo = new List<SkiResortAttractivenessSystem.LodgeInfo>(4);
        private int m_LastMountainSkipped;
        private readonly HashSet<Entity> m_Paired = new HashSet<Entity>();
        private readonly LogWindow m_CensusLog = new LogWindow(Tuning.LogWindowS);

        private bool m_Armed, m_Disabled, m_ErrorLogged, m_Idle;
        private int m_LastRev = int.MinValue, m_LastSig = int.MinValue;
        private int m_TicksSince, m_SigTicks;
        private int m_Censuses, m_Revision;
        private uint m_LastCensusFrame;
        private int m_LastLanesVisited, m_LastFacilitiesInReach, m_LastMaxHits, m_LastLodges;
        // cost (first run after the load, then re-armed once per game day at midday)
        private int m_CostCensuses;
        private double m_CostSumMs, m_CostMaxMs;
        private bool m_CostLogged;
        private bool m_CostSkipNext;               // true on the arming tick only; RecordCost leaves that tick out
        private int m_CostArmDay = int.MinValue;   // SkiDayRules.DayKey of the last midday re-arm or afternoon line
        private int m_CostMeasurements;            // `Parking [cost]` lines this load


        /// <summary>The last census of <paramref name="lodge"/> (false: no census for that lodge yet). Main thread,
        /// no job sync, managed data only. SkiDaySystem reads it every pass.</summary>
        internal bool TryGetParking(Entity lodge, out LodgeParking parking)
        {
            return m_ByLodge.TryGetValue(lodge, out parking);
        }

        /// <summary>The parking lines of the stats DUMP. Appends only, never throws; managed copies only.</summary>
        internal void AppendDump(StringBuilder sb)
        {
            try
            {
                sb.Append("  Parking: armed ").Append(m_Armed).Append(", disabled ").Append(m_Disabled)
                  .Append(", censuses ").Append(m_Censuses).Append(", last at frame ").Append(m_LastCensusFrame)
                  .Append(", revision ").Append(m_Revision)
                  .Append(", radius ").Append(SkiParkingRules.RadiusM.ToString("0", CultureInfo.InvariantCulture))
                  .Append(" m, backstop ").Append(SkiParkingRules.BackstopTicks * 16).Append(" frames; last census ")
                  .Append(m_LastLanesVisited).Append(" lane(s) visited, ").Append(m_LastFacilitiesInReach)
                  .Append(" parking building(s) in reach; mountain lodges skipped (no parking row) ").Append(m_LastMountainSkipped)
                  .Append('\n');
                for (int i = 0; i < m_Order.Count; i++)
                {
                    if (!m_ByLodge.TryGetValue(m_Order[i], out LodgeParking p))
                        continue;
                    sb.Append("    lodge ").Append(Fmt(p.Lodge)).Append(" of ski area ").Append(Fmt(p.Sign)).Append(": ");
                    if (!p.Done)
                    {
                        sb.Append("no census (no position)\n");
                        continue;
                    }
                    sb.Append(p.CarSpaces).Append(" car spaces (street ").Append(p.StreetSpaces).Append(", lots ")
                      .Append(p.LotSpaces).Append(", garages ").Append(p.GarageSpaces).Append("; parked now ").Append(p.Parked)
                      .Append("), nearest ")
                      .Append(p.NearestM < 0f ? "-" : p.NearestM.ToString("F0", CultureInfo.InvariantCulture))
                      .Append(" m, state ").Append(p.State).Append("; not counted: ").Append(p.ClosedLanes).Append(" closed, ")
                      .Append(p.PrivateLanes).Append(" private, ").Append(p.OtherLanes).Append(" not for cars\n");
                }
            }
            catch (Exception e)
            {
                sb.Append("  Parking: dump failed (").Append(e.GetType().Name).Append(": ").Append(e.Message).Append(")\n");
            }
        }


        protected override void OnCreate()
        {
            base.OnCreate();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_TimeSystem = World.GetOrCreateSystemManaged<TimeSystem>();
            m_Attractiveness = World.GetOrCreateSystemManaged<SkiResortAttractivenessSystem>();
            m_NetSearch = World.GetOrCreateSystemManaged<NetSearchSystem>();
            m_NameSystem = World.GetOrCreateSystemManaged<Game.UI.NameSystem>();

            // Counted for the signature: every CarParkingFacility, an extension too
            m_FacilityQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<CarParkingFacility>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            // Walked by the garage pass: upgrades excluded, as ParkingFacilityAISystem.cs:245 (1.6.2) excludes them. An installed
            // parking extension gives its root CarParkingFacility too (ParkingFacility.cs:45-57), its garage lanes sit on the root
            // (RoadConnectionSystem.cs:1026-1043) and the root's SubObject walk reaches the extension's lanes, so walking the
            // extension again would count a second building in reach.
            m_FacilityWalkQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<CarParkingFacility>() },
                None = new[]
                {
                    ComponentType.ReadOnly<Game.Buildings.ServiceUpgrade>(), ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
            });
            // Counted only (the signature), never walked: CalculateEntityCount needs no sync.
            m_ParkingLaneQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Net.ParkingLane>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });

            m_EntityType = GetEntityTypeHandle();
            m_TransformLookup = GetComponentLookup<Game.Objects.Transform>(isReadOnly: true);
            m_BuildingLookup = GetComponentLookup<Building>(isReadOnly: true);
            m_DestroyedLookup = GetComponentLookup<Destroyed>(isReadOnly: true);
            m_DeletedLookup = GetComponentLookup<Deleted>(isReadOnly: true);
            m_LeisureProviderLookup = GetComponentLookup<LeisureProvider>(isReadOnly: true);
            m_ParkingLaneLookup = GetComponentLookup<Game.Net.ParkingLane>(isReadOnly: true);
            m_GarageLaneLookup = GetComponentLookup<Game.Net.GarageLane>(isReadOnly: true);
            m_ConnectionLaneLookup = GetComponentLookup<Game.Net.ConnectionLane>(isReadOnly: true);
            m_CurveLookup = GetComponentLookup<Game.Net.Curve>(isReadOnly: true);
            m_OwnerLookup = GetComponentLookup<Owner>(isReadOnly: true);
            m_PrefabRefLookup = GetComponentLookup<Game.Prefabs.PrefabRef>(isReadOnly: true);
            m_ParkingLaneDataLookup = GetComponentLookup<Game.Prefabs.ParkingLaneData>(isReadOnly: true);
            m_CarParkingFacilityLookup = GetComponentLookup<CarParkingFacility>(isReadOnly: true);
            m_CarParkingLookup = GetComponentLookup<Game.Routes.CarParking>(isReadOnly: true);
            m_ParkedCarLookup = GetComponentLookup<Game.Vehicles.ParkedCar>(isReadOnly: true);
            m_LaneObjectLookup = GetBufferLookup<Game.Net.LaneObject>(isReadOnly: true);
            m_SubLaneLookup = GetBufferLookup<Game.Net.SubLane>(isReadOnly: true);
            m_SubNetLookup = GetBufferLookup<Game.Net.SubNet>(isReadOnly: true);
            m_SubObjectLookup = GetBufferLookup<Game.Objects.SubObject>(isReadOnly: true);

            m_JobLodges = new NativeList<Entity>(8, Allocator.Persistent);
            m_JobSigns = new NativeList<Entity>(8, Allocator.Persistent);
            m_Results = new NativeList<LodgeParking>(8, Allocator.Persistent);
            m_LodgeHits = new NativeList<int>(8, Allocator.Persistent);
            m_Stats = new NativeArray<int>(2, Allocator.Persistent);

            // Job reflection data and JIT before the first Schedule (the PrewarmJit precedent). Schedules nothing.
            IJobExtensions.EarlyJobInit<ParkingCensusJob>();

            Mod.Log.Info($"{nameof(SkiLodgeParkingSystem)} created");
        }

        /// <summary>One tick is one int compare unless a census is due (header cadence).</summary>
        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 16;
        }

        protected override void OnDestroy()
        {
            try
            {
                m_Handle.Complete();
                if (m_CensusLog.Count > 0)
                    WriteCensusSummary(UnityEngine.Time.realtimeSinceStartup);
            }
            catch (Exception)
            {
                // teardown: nothing left to report to
            }
            if (m_JobLodges.IsCreated) m_JobLodges.Dispose();
            if (m_JobSigns.IsCreated) m_JobSigns.Dispose();
            if (m_Results.IsCreated) m_Results.Dispose();
            if (m_LodgeHits.IsCreated) m_LodgeHits.Dispose();
            if (m_Stats.IsCreated) m_Stats.Dispose();
            base.OnDestroy();
        }

        /// <summary>A load (or leaving to the menu): the census in flight is completed and discarded (the world is
        /// replaced), the census fold written. Never throws: GameSystemBase.GamePreload DISABLES a system whose
        /// OnGamePreload throws (Game/GameSystemBase.cs:85-96).</summary>
        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            try
            {
                m_Handle.Complete();
                m_InFlight = false;
                if (m_CensusLog.Count > 0)
                    WriteCensusSummary(UnityEngine.Time.realtimeSinceStartup);
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Parking: the census in flight or the folded census lines could not be closed before the load " +
                             $"({e.GetType().Name}: {e.Message}) — non-fatal");
            }
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            m_Handle.Complete();
            m_InFlight = false;
            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_Idle = false;
            m_LastRev = int.MinValue;
            m_LastSig = int.MinValue;
            m_TicksSince = 0;
            m_SigTicks = SkiParkingRules.SignatureTicks - 1;   // the first tick reads the signature too (one census, not two)
            m_Censuses = m_Revision = 0;
            m_LastCensusFrame = 0;
            m_LastLanesVisited = m_LastFacilitiesInReach = m_LastMaxHits = m_LastLodges = 0;
            m_CostCensuses = 0;
            m_CostSumMs = m_CostMaxMs = 0.0;
            m_CostLogged = false;
            m_CostSkipNext = false;
            m_CostArmDay = int.MinValue;
            m_CostMeasurements = 0;
            m_JobLodges.Clear();
            m_JobSigns.Clear();
            m_Results.Clear();
            m_LodgeHits.Clear();
            m_Stats[0] = m_Stats[1] = 0;
            m_ByLodge.Clear();
            m_Order.Clear();
            m_Seen.Clear();
            m_Drop.Clear();
            m_Signs.Clear();
            m_Lodges.Clear();
            m_LodgeInfo.Clear();
            m_LastMountainSkipped = 0;
            m_Paired.Clear();
            m_CensusLog.Reset();   // flushed in OnGamePreload; nothing carries over

            if (mode != GameMode.Game)
                return;
            if (!s_FlagsMatch)
            {
                m_Disabled = true;
                Mod.Log.Warn("Parking: Game.Net ParkingLaneFlags / ConnectionLaneFlags / RoadTypes values differ from " +
                             "SkiParkingRules' constants (this build was compiled against another Game.dll) — the parking " +
                             "census is off for this load");
                return;
            }

            m_Armed = true;
            Mod.Log.Info("Parking: ARMED — car spaces within 300 m of each active road lodge (mountain lodges and patrol huts get no row): street parking lanes (not boarding-only " +
                         "lanes such as the two-way Gravel Road's and Alley's), lanes and garages of parking lots and halls; " +
                         "not bicycle, service or private spaces; census when the lodge list or the parking count changes, at " +
                         $"least every {SkiParkingRules.BackstopTicks * 16} sim frames; in memory, reset on load");
        }

        protected override void OnUpdate()
        {
            try
            {
                // The census fold's trailing summary (silence impossible): one int compare while nothing is folded.
                if (m_CensusLog.Count > 0)
                {
                    float now = UnityEngine.Time.realtimeSinceStartup;
                    if (m_CensusLog.TrailingDue(now))
                        WriteCensusSummary(now);
                }
                if (!m_Armed || m_Disabled)
                    return;
                // 0. The cost measurement re-arms once per game day at midday; a census on the arming tick is not measured (the flag lives
                // for this tick only).
                m_CostSkipNext = RearmCostAtMidday();

                // 1. Last tick's census (≥ 16 frames old: done by now).
                if (m_InFlight)
                    ApplyCensus();

                // After a load the attraction snapshot is empty until its first recompute; a census before it would find no lodge and
                // write a false `Parking [idle]` line. m_LastRev stays int.MinValue, so the first tick after the recompute is due.
                if (!m_Attractiveness.HasSnapshot)
                    return;

                // 2. Due? (header CADENCE)
                m_TicksSince++;
                m_SigTicks++;
                int rev = m_Attractiveness.LodgeSetRevision;
                bool due = rev != m_LastRev || m_TicksSince >= SkiParkingRules.BackstopTicks;
                if (m_SigTicks >= SkiParkingRules.SignatureTicks)
                {
                    m_SigTicks = 0;
                    int sig = unchecked(m_ParkingLaneQuery.CalculateEntityCount() * 31 + m_FacilityQuery.CalculateEntityCount());
                    if (sig != m_LastSig)
                    {
                        m_LastSig = sig;
                        due = true;
                    }
                }
                if (!due)
                    return;
                m_LastRev = rev;
                m_TicksSince = 0;
                long t0 = Stopwatch.GetTimestamp();

                // 3. The (lodge, sign) pairs of the attraction snapshot (first sign wins, as CopyActiveLodgeInfo does): Base road lodges only
                // (the ski-day targets); mountain lodges are counted for the DUMP and get no row.
                m_JobLodges.Clear();
                m_JobSigns.Clear();
                m_Paired.Clear();
                int mountain = 0;
                m_Attractiveness.CopySigns(m_Signs);
                for (int s = 0; s < m_Signs.Count; s++)
                {
                    m_Attractiveness.CopyActiveLodgeInfo(m_Signs[s], m_LodgeInfo);
                    for (int l = 0; l < m_LodgeInfo.Count; l++)
                    {
                        SkiResortAttractivenessSystem.LodgeInfo info = m_LodgeInfo[l];
                        if (!IsBaseRoadLodge(info.LodgeIndex))
                        {
                            mountain++;
                            continue;
                        }
                        if (!m_Paired.Add(info.Lodge))
                            continue;
                        m_JobLodges.Add(info.Lodge);
                        m_JobSigns.Add(m_Signs[s]);
                    }
                }
                m_LastMountainSkipped = mountain;
                if (m_JobLodges.Length == 0)
                {
                    if (m_ByLodge.Count > 0)
                    {
                        m_ByLodge.Clear();
                        m_Order.Clear();
                        m_Revision++;
                    }
                    SetIdle(true);
                    return;
                }
                SetIdle(false);

                // 4. Schedule (the CostCensuses-th census with lodges after the load or a midday re-arm is completed
                // synchronously and timed).
                bool timed = !m_CostLogged && m_CostCensuses == Tuning.CostCensuses - 1;
                Schedule(timed, t0);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                try
                {
                    m_Handle.Complete();
                }
                catch (Exception)
                {
                    // the first exception is the one worth reporting
                }
                m_InFlight = false;
                // The text below promises an empty row 21: drop the last census, as the idle path does. Managed, cannot throw.
                m_ByLodge.Clear();
                m_Order.Clear();
                m_Revision++;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiLodgeParkingSystem)} EXCEPTION — the lodge parking census is disabled until " +
                                  $"the next game load (hub row 21 stays empty). Please report this stack:\n{e}");
                }
            }
        }


        /// <summary>Schedules the census: lookups updated, the facility chunk list (async), the lane tree (reader), one
        /// IJob on Dependency + both handles; AddLaneSearchTreeReader. <paramref name="timed"/>: the [cost] census
        /// — dependencies completed first (timed with the rest as "schedule to complete"), then schedule + complete
        /// (the job's own time), then applied at once.</summary>
        private void Schedule(bool timed, long t0)
        {
            int n = m_JobLodges.Length;
            m_Results.ResizeUninitialized(n);
            m_LodgeHits.ResizeUninitialized(n);
            m_Stats[0] = m_Stats[1] = 0;

            m_EntityType.Update(ref CheckedStateRef);
            m_TransformLookup.Update(this);
            m_BuildingLookup.Update(this);
            m_DestroyedLookup.Update(this);
            m_DeletedLookup.Update(this);
            m_LeisureProviderLookup.Update(this);
            m_ParkingLaneLookup.Update(this);
            m_GarageLaneLookup.Update(this);
            m_ConnectionLaneLookup.Update(this);
            m_CurveLookup.Update(this);
            m_OwnerLookup.Update(this);
            m_PrefabRefLookup.Update(this);
            m_ParkingLaneDataLookup.Update(this);
            m_CarParkingFacilityLookup.Update(this);
            m_CarParkingLookup.Update(this);
            m_ParkedCarLookup.Update(this);
            m_LaneObjectLookup.Update(this);
            m_SubLaneLookup.Update(this);
            m_SubNetLookup.Update(this);
            m_SubObjectLookup.Update(this);

            NativeList<ArchetypeChunk> facilityChunks = m_FacilityWalkQuery.ToArchetypeChunkListAsync(Allocator.TempJob, out JobHandle facilityDeps);
            // Reader handshake (SearchSystem.cs:839-843: readOnly → the tree's write dependency only).
            NativeQuadTree<Entity, QuadTreeBoundsXZ> tree = m_NetSearch.GetLaneSearchTree(readOnly: true, out JobHandle treeDeps);
            uint frame = m_SimulationSystem.frameIndex;
            var job = new ParkingCensusJob
            {
                Lodges = m_JobLodges.AsArray(),
                Signs = m_JobSigns.AsArray(),
                FacilityChunks = facilityChunks,
                EntityType = m_EntityType,
                LaneTree = tree,
                Transforms = m_TransformLookup,
                Buildings = m_BuildingLookup,
                Destroyeds = m_DestroyedLookup,
                Deleteds = m_DeletedLookup,
                LeisureProviders = m_LeisureProviderLookup,
                ParkingLanes = m_ParkingLaneLookup,
                GarageLanes = m_GarageLaneLookup,
                ConnectionLanes = m_ConnectionLaneLookup,
                Curves = m_CurveLookup,
                Owners = m_OwnerLookup,
                PrefabRefs = m_PrefabRefLookup,
                ParkingLaneDatas = m_ParkingLaneDataLookup,
                CarParkingFacilities = m_CarParkingFacilityLookup,
                CarParkings = m_CarParkingLookup,
                ParkedCars = m_ParkedCarLookup,
                LaneObjects = m_LaneObjectLookup,
                SubLanes = m_SubLaneLookup,
                SubNets = m_SubNetLookup,
                SubObjects = m_SubObjectLookup,
                Results = m_Results.AsArray(),
                LodgeHits = m_LodgeHits.AsArray(),
                Stats = m_Stats,
                Frame = frame,
            };
            JobHandle deps = JobHandle.CombineDependencies(Dependency, facilityDeps, treeDeps);

            long tDeps = 0, tRun = 0;
            if (timed)
            {
                tDeps = Stopwatch.GetTimestamp();
                deps.Complete();   // the one main-thread sync of a measurement: after the load, then daily
                tRun = Stopwatch.GetTimestamp();
            }
            JobHandle handle = job.Schedule(deps);
            m_NetSearch.AddLaneSearchTreeReader(handle);
            facilityChunks.Dispose(handle);
            Dependency = handle;
            m_Handle = handle;
            m_InFlight = true;
            m_JobFrame = frame;

            if (!timed)
            {
                RecordCost(Ms(Stopwatch.GetTimestamp() - t0));
                return;
            }
            handle.Complete();
            long tDone = Stopwatch.GetTimestamp();
            RecordCost(Ms(tDeps - t0));
            ApplyCensus();
            m_CostLogged = true;
            m_CostMeasurements++;
            if (m_TimeSystem.normalizedTime >= 0.5f)   //a line written at or after 12:00 is today's (as SkiDaySystem)
                m_CostArmDay = SkiDayRules.DayKey(m_TimeSystem.year, m_TimeSystem.normalizedDate, m_TimeSystem.daysPerYear);
            // The synchronous figure includes the wait for the vanilla writers of the job's inputs (deps.Complete above), printed on
            // its own: that part is not ours.
            Mod.Log.Info(Invariant($"Parking [cost] main thread per census tick: mean {m_CostSumMs / Math.Max(1, m_CostCensuses):F3} ms, ") +
                         Invariant($"max {m_CostMaxMs:F3} ms over {m_CostCensuses} census(es); last: {m_LastLodges} lodge(s), ") +
                         Invariant($"{m_LastLanesVisited} lane(s) visited ({m_LastMaxHits} at most for one lodge), ") +
                         Invariant($"{m_LastFacilitiesInReach} parking building(s) in reach, job {Ms(tDone - tRun):F3} ms; ") +
                         Invariant($"one synchronous census (schedule to complete): {Ms(tDone - tDeps):F3} ms; ") +
                         Invariant($"waited first for vanilla jobs {Ms(tRun - tDeps):F3} ms (not ours)") +
                         SkiGuestCounterSystem.CostSuffix(m_CostMeasurements, m_TimeSystem));
        }

        /// <summary>Once the last `Parking [cost]` line was written, the first tick at or after 12:00 of a new game day
        /// (SkiDayRules.DayKey) starts a new measurement; the CostCensuses-th census after it is the synchronous one. Returns true
        /// on the arming tick, which RecordCost leaves out.</summary>
        private bool RearmCostAtMidday()
        {
            // The midday re-arm is measurement scaffolding behind Options > Diagnostics (off by default); the once-per-load
            // measurement stays.
            if (!SkiResortMod.Settings.SkiResortSettings.DiagnosticsOn)
                return false;
            if (!m_CostLogged || !(m_TimeSystem.normalizedTime >= 0.5f))
                return false;
            int day = SkiDayRules.DayKey(m_TimeSystem.year, m_TimeSystem.normalizedDate, m_TimeSystem.daysPerYear);
            if (day == m_CostArmDay)
                return false;
            m_CostArmDay = day;
            m_CostCensuses = 0;
            m_CostSumMs = m_CostMaxMs = 0.0;
            m_CostLogged = false;
            return true;
        }

        private void RecordCost(double ms)
        {
            if (m_CostLogged)
                return;
            if (m_CostSkipNext)
            {
                m_CostSkipNext = false;   //a census on the arming tick is not measured
                return;
            }
            m_CostCensuses++;
            m_CostSumMs += ms;
            if (ms > m_CostMaxMs)
                m_CostMaxMs = ms;
        }

        /// <summary>Completes the census (scheduled ≥ 16 frames ago: free by now), copies the job's counters into
        /// managed fields (the DUMP and [cost] read only these), stamps each result with the schedule
        /// frame, writes one census line per lodge whose Done, State or car spaces changed (or that is new), drops
        /// the lodges no longer listed (no line), and moves the private revision once when anything changed.</summary>
        private void ApplyCensus()
        {
            m_Handle.Complete();
            m_InFlight = false;
            m_Censuses++;
            m_LastCensusFrame = m_JobFrame;
            m_LastLanesVisited = m_Stats[0];
            m_LastFacilitiesInReach = m_Stats[1];
            m_LastLodges = m_Results.Length;
            m_LastMaxHits = 0;
            for (int i = 0; i < m_LodgeHits.Length; i++)
            {
                if (m_LodgeHits[i] > m_LastMaxHits)
                    m_LastMaxHits = m_LodgeHits[i];
            }

            bool changed = false;
            m_Seen.Clear();
            m_Order.Clear();
            for (int i = 0; i < m_Results.Length; i++)
            {
                LodgeParking p = m_Results[i];
                p.Frame = m_JobFrame;
                m_Seen.Add(p.Lodge);
                m_Order.Add(p.Lodge);
                bool had = m_ByLodge.TryGetValue(p.Lodge, out LodgeParking old);
                m_ByLodge[p.Lodge] = p;
                if (had && old.Done == p.Done && old.State == p.State && old.CarSpaces == p.CarSpaces)
                    continue;
                changed = true;
                CensusLine(p);
            }
            if (m_ByLodge.Count > m_Seen.Count)
            {
                m_Drop.Clear();
                foreach (var pair in m_ByLodge)
                {
                    if (!m_Seen.Contains(pair.Key))
                        m_Drop.Add(pair.Key);
                }
                for (int i = 0; i < m_Drop.Count; i++)
                    m_ByLodge.Remove(m_Drop[i]);
                m_Drop.Clear();
                changed = true;
            }
            if (changed)
                m_Revision++;
        }


        /// <summary>One census line through m_CensusLog (Plain now, Summary = the fold line, Fold = counted;
        /// slot 0 = the lodge's car spaces).</summary>
        private void CensusLine(LodgeParking p)
        {
            string body = "lodge " + Fmt(p.Lodge) + " of ski area " + Fmt(p.Sign) + NameSuffix(p.Sign) + ": ";
            if (!p.Done)
            {
                body += "no census (no position)";
            }
            else
            {
                int n = p.CarSpaces;
                body += $"{n} car space(s) within {SkiParkingRules.RadiusM.ToString("0", CultureInfo.InvariantCulture)} m " +
                        $"(street {p.StreetSpaces}, lots {p.LotSpaces}, garages {p.GarageSpaces}; {p.Parked} parked now)" +
                        (n == 0 ? " — no parking near this lodge" : "") +
                        (p.State != SkiDayRules.LodgeReject.None ? "; lodge " + p.State : "");
            }
            float now = UnityEngine.Time.realtimeSinceStartup;
            switch (m_CensusLog.Offer(now, false, body, p.Done ? p.CarSpaces : 0))
            {
                case LogWindow.Verdict.Plain:
                    Mod.Log.Info("Parking [census] " + body);
                    break;
                case LogWindow.Verdict.Summary:
                    WriteCensusSummary(now);
                    break;
            }
        }

        private void WriteCensusSummary(float now)
        {
            Mod.Log.Info($"Parking [census] {m_CensusLog.Count} more change(s) in {m_CensusLog.SpanS(now):F0} s, folded " +
                         $"(one line per LogWindowS={Tuning.LogWindowS:0} s): spaces {m_CensusLog.Range(0, "F0")} — last: " +
                         m_CensusLog.LastText);
            m_CensusLog.Wrote(now);
        }

        /// <summary>`Parking [idle]` on each transition into idle (the census fold is written first).</summary>
        private void SetIdle(bool idle)
        {
            if (idle == m_Idle)
                return;
            m_Idle = idle;
            if (!idle)
                return;
            if (m_CensusLog.Count > 0)
                WriteCensusSummary(UnityEngine.Time.realtimeSinceStartup);
            // A ski area with mountain lodges only is idle too (they need no parking); the line says so.
            Mod.Log.Info("Parking [idle]: no active lodge on a Welcome Sign — nothing counted" +
                         (m_LastMountainSkipped > 0 ? $" ({m_LastMountainSkipped} active mountain lodge(s): roadless by design, no parking row)" : ""));
        }

        /// <summary>A lodge row whose LodgeRoadRule is Base (a ski-day target, needs a road and parking). An out-of-range index
        /// reads the A-frame row (Base), as the score does.</summary>
        private static bool IsBaseRoadLodge(int lodgeIndex)
        {
            if (SkiCatalog.Lodges.Count == 0)
                return true;
            LodgeDef def = lodgeIndex >= 0 && lodgeIndex < SkiCatalog.Lodges.Count ? SkiCatalog.Lodges[lodgeIndex] : SkiCatalog.Lodges[0];
            return def.RoadRule == LodgeRoadRule.Base;
        }


        private string NameSuffix(Entity e)
        {
            if (e == Entity.Null || !m_NameSystem.TryGetCustomName(e, out string name) || string.IsNullOrEmpty(name))
                return "";
            return " '" + name + "'";
        }

        private static string Fmt(Entity e) => e.Index + ":" + e.Version;

        private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        private static string Invariant(FormattableString s) => FormattableString.Invariant(s);


        /// <summary>Lane search tree hits inside the lodge's ± 300 m box (bounds test only; the distance test runs on
        /// each ParkingLane hit). Shape of the house iterator (SkiGladeToolSystem.TreeBoundsIterator).</summary>
        private struct LaneBoxIterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>,
            IUnsafeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public Bounds2 m_Bounds;
            public NativeList<Entity> m_Results;

            public bool Intersect(QuadTreeBoundsXZ bounds)
            {
                return MathUtils.Intersect(bounds.m_Bounds.xz, m_Bounds);
            }

            public void Iterate(QuadTreeBoundsXZ bounds, Entity entity)
            {
                if (MathUtils.Intersect(bounds.m_Bounds.xz, m_Bounds))
                    m_Results.Add(entity);
            }
        }

        /// <summary>Steps 1-3 of the header census, per lodge. Every lookup read is guarded (TryGetComponent / HasComponent /
        /// TryGetBuffer) and every index checked by its loop bound.</summary>
        [BurstCompile]
        private struct ParkingCensusJob : IJob
        {
            [ReadOnly] public NativeArray<Entity> Lodges;
            [ReadOnly] public NativeArray<Entity> Signs;
            [ReadOnly] public NativeList<ArchetypeChunk> FacilityChunks;
            [ReadOnly] public EntityTypeHandle EntityType;
            [ReadOnly] public NativeQuadTree<Entity, QuadTreeBoundsXZ> LaneTree;   // FixParkingLocationSystem.cs:928
            [ReadOnly] public ComponentLookup<Game.Objects.Transform> Transforms;
            [ReadOnly] public ComponentLookup<Building> Buildings;
            [ReadOnly] public ComponentLookup<Destroyed> Destroyeds;
            [ReadOnly] public ComponentLookup<Deleted> Deleteds;
            [ReadOnly] public ComponentLookup<LeisureProvider> LeisureProviders;
            [ReadOnly] public ComponentLookup<Game.Net.ParkingLane> ParkingLanes;
            [ReadOnly] public ComponentLookup<Game.Net.GarageLane> GarageLanes;
            [ReadOnly] public ComponentLookup<Game.Net.ConnectionLane> ConnectionLanes;
            [ReadOnly] public ComponentLookup<Game.Net.Curve> Curves;
            [ReadOnly] public ComponentLookup<Owner> Owners;
            [ReadOnly] public ComponentLookup<Game.Prefabs.PrefabRef> PrefabRefs;
            [ReadOnly] public ComponentLookup<Game.Prefabs.ParkingLaneData> ParkingLaneDatas;
            [ReadOnly] public ComponentLookup<CarParkingFacility> CarParkingFacilities;
            [ReadOnly] public ComponentLookup<Game.Routes.CarParking> CarParkings;
            [ReadOnly] public ComponentLookup<Game.Vehicles.ParkedCar> ParkedCars;
            [ReadOnly] public BufferLookup<Game.Net.LaneObject> LaneObjects;
            [ReadOnly] public BufferLookup<Game.Net.SubLane> SubLanes;
            [ReadOnly] public BufferLookup<Game.Net.SubNet> SubNets;
            [ReadOnly] public BufferLookup<Game.Objects.SubObject> SubObjects;
            public NativeArray<LodgeParking> Results;   // Lodges.Length
            public NativeArray<int> LodgeHits;          // Lodges.Length
            public NativeArray<int> Stats;              // [0] lane hits, [1] parking buildings in reach
            public uint Frame;

            public void Execute()
            {
                var hits = new NativeList<Entity>(256, Allocator.Temp);
                int n = math.min(Lodges.Length, math.min(Signs.Length, math.min(Results.Length, LodgeHits.Length)));
                for (int i = 0; i < n; i++)
                {
                    Entity lodge = Lodges[i];
                    Entity sign = Signs[i];
                    var r = new LodgeParking { Lodge = lodge, Sign = sign, NearestM = -1f, Frame = Frame };

                    // 1. The lodge verdict: slope-side is not judged here.
                    bool hasBuilding = Buildings.TryGetComponent(lodge, out Building building);
                    r.State = SkiDayRules.CheckLodge(new SkiDayRules.LodgeFacts
                    {
                        IsLodgePrefab = true,
                        OnLiveSign = true,
                        Active = hasBuilding && !BuildingUtils.CheckOption(building, BuildingOption.Inactive),   // BuildingUtils.cs:351
                        Destroyed = Destroyeds.HasComponent(lodge) || Deleteds.HasComponent(lodge) || !hasBuilding,
                        LeisureTagged = LeisureProviders.HasComponent(lodge),
                        HasRoad = hasBuilding && building.m_RoadEdge != Entity.Null,
                        NearestRunOrStationM = 0f,
                    });
                    LodgeHits[i] = 0;
                    if (!Transforms.TryGetComponent(lodge, out Game.Objects.Transform transform))
                    {
                        Results[i] = r;   // Done = false: no position
                        continue;
                    }
                    float2 p = transform.m_Position.xz;

                    // 2. Lane pass: the lane tree inside the ± 300 m box.
                    hits.Clear();
                    var iterator = new LaneBoxIterator
                    {
                        m_Bounds = new Bounds2(p - SkiParkingRules.RadiusM, p + SkiParkingRules.RadiusM),
                        m_Results = hits,
                    };
                    LaneTree.Iterate(ref iterator);
                    LodgeHits[i] = hits.Length;
                    Stats[0] += hits.Length;
                    for (int h = 0; h < hits.Length; h++)
                        CountLane(hits[h], lodge, sign, p, ref r);

                    // 3. Garage pass: the parking facilities within 300 m.
                    for (int c = 0; c < FacilityChunks.Length; c++)
                    {
                        NativeArray<Entity> facilities = FacilityChunks[c].GetNativeArray(EntityType);
                        for (int f = 0; f < facilities.Length; f++)
                            CountFacility(facilities[f], lodge, sign, p, ref r);
                    }

                    r.Done = true;
                    Results[i] = r;
                }
                hits.Dispose();
            }

            /// <summary>Step 2 for one tree hit: only ParkingLanes; ClassifyLane; a counted lane's slots (the game's
            /// own count) and parked cars, split street / lot by the owner walk.</summary>
            private void CountLane(Entity lane, Entity lodge, Entity sign, float2 p, ref LodgeParking r)
            {
                if (!ParkingLanes.TryGetComponent(lane, out Game.Net.ParkingLane parkingLane))
                    return;   // a car, bicycle or walking lane: not a parking lane at all
                if (!Curves.TryGetComponent(lane, out Game.Net.Curve curve) ||
                    !PrefabRefs.TryGetComponent(lane, out Game.Prefabs.PrefabRef prefabRef) ||
                    !ParkingLaneDatas.TryGetComponent(prefabRef.m_Prefab, out Game.Prefabs.ParkingLaneData laneData))
                {
                    r.OtherLanes++;
                    return;
                }
                float d = MathUtils.Distance(curve.m_Bezier.xz, p, out float _);
                Entity restriction = parkingLane.m_AccessRestriction;
                bool restricted = SkiParkingRules.IsRestricted(restriction != Entity.Null, restriction == sign || restriction == lodge,
                                                               (uint)parkingLane.m_Flags, SkiParkingRules.LaneFlagAllowEnterExit);
                switch (SkiParkingRules.ClassifyLane((uint)parkingLane.m_Flags, (byte)laneData.m_RoadTypes, laneData.m_SlotInterval,
                                                     d, restricted))
                {
                    case ParkingLaneVerdict.Count:
                        int slots = math.max(0, Game.Net.NetUtils.GetParkingSlotCount(curve, parkingLane, laneData));   // NetUtils.cs:954-957
                        int parked = ParkedOn(lane);
                        if (RootOf(lane) == ParkingRoot.Street)
                        {
                            r.StreetSpaces += slots;
                            r.StreetParked += parked;
                        }
                        else
                        {
                            r.LotSpaces += slots;
                            r.LotParked += parked;
                        }
                        if (slots > 0 && (r.NearestM < 0f || d < r.NearestM))
                            r.NearestM = d;
                        break;
                    case ParkingLaneVerdict.TooFar:
                        break;   // in the box, beyond the radius
                    case ParkingLaneVerdict.Closed:
                        r.ClosedLanes++;
                        break;
                    case ParkingLaneVerdict.Private:
                        r.PrivateLanes++;
                        break;
                    default:   // Virtual, Service, NotCar, NoSlots
                        r.OtherLanes++;
                        break;
                }
            }

            /// <summary>The LaneObjects of <paramref name="lane"/> that are parked cars (RoadsInfoviewUISystem.cs:159-165).</summary>
            private int ParkedOn(Entity lane)
            {
                if (!LaneObjects.TryGetBuffer(lane, out DynamicBuffer<Game.Net.LaneObject> objects))
                    return 0;
                int parked = 0;
                for (int k = 0; k < objects.Length; k++)
                {
                    if (ParkedCars.HasComponent(objects[k].m_LaneObject))
                        parked++;
                }
                return parked;
            }

            /// <summary>The owner walk (≤ MaxOwnerDepth): a CarParkingFacility or CarParking on the chain → a parking
            /// building; else any Building → another building's lot; else the street (DUMP split only).</summary>
            private ParkingRoot RootOf(Entity lane)
            {
                bool parkingBuilding = false, anyBuilding = false;
                Entity e = lane;
                for (int k = 0; k < SkiParkingRules.MaxOwnerDepth; k++)
                {
                    if (!Owners.TryGetComponent(e, out Owner owner) || owner.m_Owner == Entity.Null)
                        break;
                    e = owner.m_Owner;
                    if (CarParkingFacilities.HasComponent(e) || CarParkings.HasComponent(e))
                        parkingBuilding = true;
                    if (Buildings.HasComponent(e))
                        anyBuilding = true;
                }
                return SkiParkingRules.RootOf(parkingBuilding, anyBuilding);
            }

            /// <summary>Step 3 for one parking facility: within 300 m of the lodge, its garage lanes by the info
            /// view's walk (depth fixed at 2).</summary>
            private void CountFacility(Entity facility, Entity lodge, Entity sign, float2 p, ref LodgeParking r)
            {
                if (Destroyeds.HasComponent(facility) || !Transforms.TryGetComponent(facility, out Game.Objects.Transform t))
                    return;
                float df = math.distance(t.m_Position.xz, p);
                if (!(df <= SkiParkingRules.RadiusM))
                    return;
                Stats[1]++;
                if (SubLanes.TryGetBuffer(facility, out DynamicBuffer<Game.Net.SubLane> lanes))
                    CountGarages(lanes, lodge, sign, df, ref r);
                if (SubNets.TryGetBuffer(facility, out DynamicBuffer<Game.Net.SubNet> nets))
                {
                    for (int k = 0; k < nets.Length; k++)
                    {
                        if (SubLanes.TryGetBuffer(nets[k].m_SubNet, out DynamicBuffer<Game.Net.SubLane> netLanes))
                            CountGarages(netLanes, lodge, sign, df, ref r);
                    }
                }
                if (SubObjects.TryGetBuffer(facility, out DynamicBuffer<Game.Objects.SubObject> objects))
                {
                    for (int k = 0; k < objects.Length; k++)
                    {
                        Entity sub = objects[k].m_SubObject;
                        if (SubLanes.TryGetBuffer(sub, out DynamicBuffer<Game.Net.SubLane> subLanes))
                            CountGarages(subLanes, lodge, sign, df, ref r);
                        if (!SubObjects.TryGetBuffer(sub, out DynamicBuffer<Game.Objects.SubObject> inner))
                            continue;
                        for (int j = 0; j < inner.Length; j++)
                        {
                            if (SubLanes.TryGetBuffer(inner[j].m_SubObject, out DynamicBuffer<Game.Net.SubLane> innerLanes))
                                CountGarages(innerLanes, lodge, sign, df, ref r);
                        }
                    }
                }
            }

            /// <summary>Each GarageLane connection lane of <paramref name="lanes"/>: ClassifyGarage with its own
            /// restriction; counted → its capacity and the cars in it.</summary>
            private void CountGarages(DynamicBuffer<Game.Net.SubLane> lanes, Entity lodge, Entity sign, float df, ref LodgeParking r)
            {
                for (int k = 0; k < lanes.Length; k++)
                {
                    Entity lane = lanes[k].m_SubLane;
                    if (!GarageLanes.TryGetComponent(lane, out Game.Net.GarageLane garage) ||
                        !ConnectionLanes.TryGetComponent(lane, out Game.Net.ConnectionLane connection))
                        continue;
                    Entity restriction = connection.m_AccessRestriction;
                    bool restricted = SkiParkingRules.IsRestricted(restriction != Entity.Null, restriction == sign || restriction == lodge,
                                                                   (uint)connection.m_Flags, SkiParkingRules.ConnFlagAllowEnterExit);
                    switch (SkiParkingRules.ClassifyGarage((uint)connection.m_Flags, (byte)connection.m_RoadTypes,
                                                           garage.m_VehicleCapacity, df, restricted))
                    {
                        case ParkingLaneVerdict.Count:
                            r.GarageSpaces += garage.m_VehicleCapacity;
                            r.GarageParked += garage.m_VehicleCount;
                            if (r.NearestM < 0f || df < r.NearestM)
                                r.NearestM = df;
                            break;
                        case ParkingLaneVerdict.TooFar:
                            break;
                        case ParkingLaneVerdict.Closed:
                            r.ClosedLanes++;
                            break;
                        case ParkingLaneVerdict.Private:
                            r.PrivateLanes++;
                            break;
                        default:   // NotCar, NoSlots
                            r.OtherLanes++;
                            break;
                    }
                }
            }
        }
    }
}
