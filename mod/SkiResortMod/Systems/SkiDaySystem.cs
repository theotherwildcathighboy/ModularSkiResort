// SkiDaySystem — Round S1 "Ski days" (docs/research/ski-days/s1-spec.md §2.5; ski-days spec ch2-system.md;
// decisions D1-D4, D9-D10, D15, D18-D20, D30): claim at decision. For a share of residents at home and hotel
// tourists who just decided to go out (Leisure with no venue yet), name a ski-day lodge with LeisureSystem's own
// two writes (Leisure.m_TargetAgent + TripNeeded) and, for a far car trip with a free household car, the public
// car-reserve queue; then track each claim to arrival or failure, learn speeds and confidence per area and class,
// and publish one SkiDayAreaView per ski area for the hub. Every demand, capacity, powder, confidence and gate
// rule is SkiDayRules' (ch1); timing helpers are SkiDayTiming's. In memory only, reset on load (no serialized
// component or field).
//
// PHASE: GameSimulation, anchored UpdateAfter<SkiDaySystem, Game.Simulation.LeisureSystem> (Mod.cs, ch2 R1).
// Anchor AND interval are load-bearing: LeisureSystem is UpdateAt GameSimulation (SystemOrder.cs:377, 1.6.2 :378)
// and GetUpdateInterval returns LeisureSystem's own (262144 / kUpdatePerDay = 64, LeisureSystem.cs:914-917, 1.6.2
// :911-914), so this follower inherits its offset (UpdateSystem.cs:431-436, :355-356, :457-516) and every pass
// runs on LeisureSystem's frames, before TripNeededSystem (SystemOrder.cs:393, 1.6.2 :394). NEVER override
// GetUpdateOffset. Pause stops it; the Editor never arms it.
//
// THE PASS (ch2 §3; one per LeisureSystem interval): 1 complete last pass's jobs (scheduled one interval ago, done);
// 2 ApplyPass — copy every job result into managed state (month, tracks, load totals, views, DUMP copies, logs);
// 3 month (SkiDayRules.Advance of SkiMoneyRules.MonthKey: Rolled → `[day]` lines, Last = This; Jumped → cleared);
// 4 ApplyCensus; 5 the powder ring (one ClimateSystem.SampleClimate per game hour, 24 after a load; main thread
// only: PrefabSystem.GetPrefab + AnimationCurve.Evaluate, ClimateSystem.cs:625-654 (=)); 5b after a load, nothing
// more until the attraction system's first recompute (HasSnapshot; fixer, review CP-4); 6 the area snapshot,
// rebuilt only on change (attraction Generation, LodgeSetRevision, LiftCensusSystem.Revision, the settings key,
// the climate hour, a 64-pass backstop); 6b (Round M9) the powder year table: 288 hourly climate samples at load
// and at each game-year roll, one `[powder]` line per ski area (PowderYearTick, main thread, timed); 7 no ski area
// → idle (claims still in flight are resolved as orphans); 8 schedule SiteJob (when due) → ResolveJob → ClaimJob
// (+ the car-reserve handshake) → CensusJob (hourly). Between 3 and 4 the ledger check (M7 a2) runs once, 4,096
// frames after a roll. The jobs are SkiDaySystem.Jobs.cs. No EntityManager read of a citizen, lane or Transform, no
// CompleteDependency: citizens are walked through ToArchetypeChunkListAsync + IJob; positions through RO lookups
// in the jobs (s1-spec §1.2 rule 2). The one exception is the `[cost]` pass, once per load and then daily at
// midday (header COST).
//
// THE CLAIM (D2) writes Leisure and TripNeeded (RW type handles in ClaimJob: ECS orders it after LeisureSystem's
// job and TripNeededSystem's after it, no main-thread wait) and enqueues on CitizenBehaviorSystem.GetCarReserveQueue
// with AddCarReserveWriter (WorkerSystem.cs:450-454 (=); CBS :1094-1103, 1.6.2 :1126-1135). Those are the only
// writes to vanilla state, and, since Round M2 (D12b, ruling R4 b), ResolveJob lowering that claim's
// Leisure.m_LastPossibleFrame (the field D2 writes, through the RW lookup m_LeisureRW) when the hill closes early: a
// guest on site then leaves with the new closing, at least SkiDayTiming.OnSiteGraceFrames from then, and vanilla's
// LeisureSystem still ends the visit (LeisureSystem.cs:314-317, 1.6.2 :308-311). A ski day given before a load
// finishes as a vanilla leisure trip (D4; open point O8).
//
// TRACKING (ch2 §6): m_Claims is a list compacted every pass by ResolveJob (outcome per claim: SkiDayTiming.Resolve).
// Arrivals and failures feed the per-area, per-class ClassTrack (confidence, learned speed, "cannot reach");
// tracks live in AreaMemory (by sign; the class is not named AreaState, which inside this system is
// SkiDayRules.AreaState, s1-spec N1) and are copied into the job list at each rebuild and back after each pass.
// Round M1 keeps two more kinds of claim for MEASUREMENT only: state 2 (visit over, still inside: watched up to 2
// game hours until the guest leaves) and state 3 (expired: watched up to 6 game hours for a late arrival); Round M2
// (D5) adds state 4 (followed after leaving for home, the map exit or with no trip purpose: up to
// SkiDayTiming.FollowWatchFrames, at most Tuning.FollowCap at once). None of them is ever on the way, on site, in
// ActiveNow or in a view; a late arrival feeds only the all-outcomes speed (Round M2 R10; the fit check reads the
// arrival-only speed this round).
// MONTH: one SkiDayRules.MonthFlags (the ledger's rules); per area This / Last; `[day]` lines at a roll, a jump and
// before a load. A rolled month in which fewer than 95 % of a month's frames were simulated (a developer time jump
// that landed a month later) closes "partial" (M7c, D5: SkiMoneyRules.IsShortMonth). VIEWS: published every armed
// pass while an area exists (also with both knobs at 0: the powder mark is a snow fact); Revision moves only when
// a view is not Same (Powder bit for bit, NaN equals NaN).
// ROUND M1 "measure first" (docs/research/ski-days/measure/m1-spec.md; builder A, 2026-10-05): every counter of
// M1-M9 is measure only and in memory (nothing serialized; everything resets at load, claims in flight at a save
// are never resolved). ResolveJob and ClaimJob fill per-area measure rows (RM / RH / CM, Jobs.cs) that ApplyPass
// sums into AreaMemory's month arrays (MThis / HThis / CThis, rolled like This / Last); a 40-slot ring of ended ski
// days is copied into managed state when it changes; resident arrivals are noted for the ledger check (M7 a2).
// ROUND M2 (docs/research/round-m2/build-contract.md, the system parts; builder B2, 2026-10-10): D3 / R6 (a guest
// found in the lodge with Leisure gone is booked as arrived, visit ended "removed (on arrival)": the 9th end-cause
// word; the Dump key "reached the lodge without Leisure" narrowed to citizens that still exist); measure-core (the
// door wait, the car-reserve text without the dead bucket, "under 0.5 km" only past 0.5 km, the quit / preload
// ledger lines, clocks on the cost maxima, the `[area]` Full line, the highest and last capacity, "arrived after the
// hill closed", the partial midday `[cost]` line, late arrivals by hour, "tracked" = the exact bucket sum);
// remover-reason (why the Leisure went before the deadline, the game's clock checks at the visit end, where
// no-trip-purpose exits stood); walkers-after (NearJob: walkers within SkiDayTiming.NearLodgeM of each lodge every
// Tuning.NearScanPasses passes with an area, on the hour and on the timed `[cost]` pass; claim state 4 summed per
// pass; the hourly `[near]` line); R9 (the six trip systems' Enabled flags read at each Game load and every armed
// pass, ArmVerdict.SystemOff at load and the per-pass stop, the hub label accessors); R10 (TripKnobs, both speeds in
// the logs; the fit check stays on the arrival-only speed); R12 (the leisure route cap line at load); R4 b (the
// deadline pull: ResolveJob's RW Leisure lookup and the area snapshot, the `[closing]` line once per closure edge,
// written directly, not through a LogWindow: fixer S2/F1).
// All of it measure and text except SystemOff (R9) and the pull (R4 b); everything new is in memory, reset at load.
//
// LOGS (ch2 §10, exact): created; ARMED / NOT ARMED; `[idle]` per transition; `[area]`, `[lodges]`, `[settings]`
// and `[reach]` through four LogWindows (10 s, flushed on the pass, in OnGamePreload and OnDestroy); `[first]`
// claim and arrival per area per load; `[reach]` when "cannot reach" turns on AND off (M6, D2; it names the hub row
// by the text the active dictionary holds, so it can never name a row the hub does not show; the row also clears
// without a line at a load, where the tracks reset as the ARMED line says, when a ski area is dropped at a roll,
// which its `[area]` lines show (m1-spec E15), and when ski days are switched off in the settings, which the
// `[settings]` line shows: the hub feeds the row only while ski days are on, HubRules.cs:667-670); `[day]` per area
// + up to three measure lines per area, `(measure: failures)`, `(measure: trips and stays)` and `(measure: modes,
// scan, powder)` (M1-M4, M7, M9; fixer L7: "; " separates groups, " | " parts of a group) + total; the ledger
// check line 4,096 frames after a roll (M7 a2), or why it was skipped (a jump) or lost (a load); the jump line;
// `[powder]` per area at load, at each game-year roll and after a climate change (M9); `[drift]` once per load;
// `[cost]` once per load and then once per game day at midday (M8); the claims cap once per load; the one-shot
// `!!!`. No per-claim or per-arrival line: the last 40 ended ski days are in the DUMP (M5).
// Round M2 adds: the trip systems line once per Game-mode load (INFO all on, WARN otherwise; none on an Editor load,
// where ski days never arm) and a trip-systems change line at most Tuning.TripChangeLines per load, then "SkiDays:
// stopped" when a change switches ski days off (R9); the leisure route cap line right after ARMED (R12); `[area]`
// "full at HH:MM" (D10); `[closing]` once per closure edge per area through the `[area]` window (R4 b); the hourly
// `[near]` line per ski area with a lodge while a ski-day guest is on site or followed, and one pause line when that
// stops (D5); the follow-cap WARN once per load; the ledger check "skipped (quit before it was due …)" at a quit and
// "lost (a load or a return to the menu came before it was due …)" before a load (run 1 D7); the partial midday
// `[cost]` line at a day change (D10).
// DUMP and accessors read managed copies only, made after Complete (ch2-RF2-5); never a native container.
// CATALOG PHASE B (2026-10-11, economy group; docs/research/art/assets-2026-10/code-round-2/round-2-contract.md §7.4):
// RebuildSnapshot copies each active lodge through SkiResortAttractivenessSystem.CopyActiveLodgeInfo (seam S5, the same
// lodges and order as CopyActiveLodges) and fills the LodgeJob tier fields from SkiCatalog.Lodges on the main thread
// (Capacity, PriceTolerance, NoRoadNeeded = RoadRule != Base), and AreaJob.SchoolFactor =
// SchoolRule.DemandFactorOf(Result.SchoolActive, Result.GreenCounted) (1 without a known score); the jobs read only those
// fields (SkiDaySystem.Jobs.cs header CATALOG PHASE B). Logs: the ARMED line's capacity and price clauses name the
// per-tier capacity, the tolerance and the school; the `[lodges]` line splits ski-day (target) lodges from mountain lodges
// that only hold skiers (every lodge of any tier counts; the luxury cap was removed 2026-10-11); the DUMP prints the lodge capacity and tolerance per
// area and the tier per lodge. An A-frame-only city has the same numbers as before (360 × n, tolerance 0, factor 1).
// COST (ch2 §11): measured once per load over Tuning.CostPasses passes that scheduled ClaimJob (idle passes and the
// load's first such pass left out: F7 E3, fixer M-2), rebuilds timed apart, then one pass completed at once: the
// first one after a pass whose candidate walk ran (else after CostWaitPasses more), with the census scheduled on
// it; its vanilla inputs are completed first, timed apart and printed as "waited first for vanilla jobs" (M8 E6:
// not ours, and never in our number), so the number is our chain, schedule to complete (fixer JP-1, JP-2); its
// results are applied next pass as usual. Round M1 M8 (U8, costs at city size) re-arms it once per game day at the
// first pass at or after 12:00 (midday: claims, scans and lots are busy; the roll at midnight is the quietest
// time), once the previous line was written; the arming pass itself is left out (E5). Every pass with work also
// adds to a running day mean / max (the `[day]` total line and the DUMP), the timed pass excepted (its number
// includes the synchronous wait). NOT in any main-thread figure (fixer M1-ECS-1): our one dependency handle covers
// ResolveJob, ClaimJob and CensusJob together, and Round M1 reads about 12 more types that the game's movement and
// AI jobs write (TravelPurpose, CurrentTransport, CurrentVehicle, Divert, PathOwner, Controller, PublicTransport,
// Taxi, Bicycle, HealthProblem, ResourceBuyer, PublicTransportVehicleData), so on a pass frame those writers wait
// for the whole chain. Round M2 adds Worker, Student, HouseholdNeed, Building, OutsideConnection, Population and
// Resident to ResolveJob (every pass) and Resident, Unspawned, WaitingPassengers and Game.Routes.Position to NearJob
// (scan passes): ResidentAISystem writes Resident every frame, so its job now also waits for our chain on a pass frame
// (not measured by `[cost]`; walkers-after check gap 11); and three main-thread singleton reads per pass (Economy,
// TimeData, the Population entity, as CitizenBehaviorSystem reads them, 1.6.0 :1179, :1185-1186, 1.6.2 :1219,
// :1224-1225). A midday measurement not finished when the game day ends is written partial at the day change (Round
// M2, D10). The "one synchronous pass (schedule to complete)" figure is that chain's length, so it is
// roughly the longest such wait; a profiler capture of a pass frame in field test 10 would show it directly.
// The daily midday re-arm (four short forced waits per game day across the ski-day, guest, parking and ticket
// systems) is a measurement-round feature: gate it or remove it before release (fixer M1-ECS-3).
// CONTRACT (house pattern): "created" line; per-load reset and arm in OnGameLoadingComplete (Game mode only);
// OnGamePreload completes and books the last pass, writes the month in progress (try/caught: GameSystemBase.cs
// :85-96 (=) disables a system whose OnGamePreload throws); one-shot "!!!" and disabled for the load on an
// exception; OnDestroy completes, flushes and disposes.
//
// S1 stage 0 laid down the shell (s1-spec §4.2 step 6); stream B wrote the system (2026-10-05).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Game;
using Game.Agents;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Companies;
using Game.Events;
using Game.Simulation;
using Game.Tools;
using SkiResortMod.Settings;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using static System.FormattableString;
using Purpose = Colossal.Serialization.Entities.Purpose;   // Game.Citizens has a Purpose too

namespace SkiResortMod.Systems
{
    public sealed partial class SkiDaySystem : GameSystemBase
    {
        // ── Tuning (ch2 §1) ───────────────────────────────────────────────────────────────────────
        private static class Tuning
        {
            /// <summary>Passes with work timed for `SkiDays [cost]` (64 × 64 = 4,096 sim frames), after the load and after
            /// each midday re-arm (Round M1, M8).</summary>
            public const int CostPasses = 64;
            /// <summary>After CostPasses, the synchronous pass waits for a pass after a walk at most this many more
            /// passes with work (4,096 = one game day), then is taken anyway: L13 is never missing (fixer, JP-1).</summary>
            public const int CostWaitPasses = 4096;
            /// <summary>Snapshot and SiteJob backstop (64 passes = 4,096 sim frames): a lodge moved with no score change.</summary>
            public const int BackstopPasses = 64;
            /// <summary>Claims tracked at most; above it no new claims (L14), tracking goes on.</summary>
            public const int ClaimsCap = 32768;
            /// <summary>The three LogWindows (L5, L6, L17).</summary>
            public const float LogWindowS = 10f;
            /// <summary>The `[drift]` line needs at least this many walk passes in the month.</summary>
            public const int DriftMinWalkPasses = 256;
            /// <summary>Round M1 M5: ended ski days kept for the DUMP (80 bytes each; January of field test 9 sent 106).</summary>
            public const int RingSize = 40;
            /// <summary>Round M1 M7 (a2): the ledger check runs this many frames after a roll (0.375 game hours): past
            /// the guest counter's 256-frame leisure scan, its next-tick apply and its rule that books the last 16 frames
            /// of a month into the next (SkiGuestCounterSystem header :54-61).</summary>
            public const uint LedgerCheckDelayFrames = 4096;
            /// <summary>Round M1 M7 (a2): the visit lists start this big (managed; they grow only with arrivals).</summary>
            public const int VisitsCapacity = 256;
            /// <summary>Round M2 (D5): guests followed after leaving the lodge at most at once (claim state 4); above it
            /// "not followed", warned once per load.</summary>
            public const int FollowCap = 256;
            /// <summary>Round M2 (D5): passes with an area between walker scans (16 × 64 = 1,024 frames ≈ 0.09 game h);
            /// also on the hour and on the timed `[cost]` pass.</summary>
            public const int NearScanPasses = 16;
            /// <summary>Round M2 (R9): trip-systems change lines written at most per load (later changes are counted in the
            /// Dump; a stop is always logged).</summary>
            public const int TripChangeLines = 8;
        }

        /// <summary>Round M9: month names of the game calendar (SkiMoneyRules.MonthOfKey 1 = Jan).</summary>
        private static readonly string[] s_MonthNames = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

        private const string IdleNoArea = "no ski area";

        // TimeSystem.kTicksPerDay (TimeSystem.cs:18 (=)) copied into a static field so the arm-time compare is a
        // run-time check (house pattern, SkiLiftChargeSystem.cs:150-152; a const compare would raise CS0162). Only a
        // rebuild against a changed Game.dll can trip it (s1-spec ch2-RF2-7).
        private static readonly int s_GameTicksPerDay = TimeSystem.kTicksPerDay;

        // ── systems ───────────────────────────────────────────────────────────────────────────────
        private SimulationSystem m_Simulation;
        private TimeSystem m_Time;
        private ClimateSystem m_Climate;
        private CitizenBehaviorSystem m_Behavior;
        private Game.UI.NameSystem m_NameSystem;
        private SkiResortAttractivenessSystem m_Attraction;
        private SkiLiftTicketSystem m_Ticket;
        private SnowLineSystem m_SnowLine;
        private LiftCensusSystem m_LiftCensus;
        private SkiLodgeParkingSystem m_Parking;   // registered after us: resolved at each load (null-safe, ch2-RF2-9)
        private SkiGuestCounterSystem m_Counter;   // Round M1 M7: registered before us (Mod.cs:231 before :250); read only
        // Round M2 (R9): the six vanilla trip systems whose ComponentSystemBase.Enabled is read at each Game load and every
        // armed pass (a public property vanilla reads on other systems too, Game.Policies/ModifiedSystem.cs:412 (=)). All
        // six are registered by SystemOrder before any mod's OnLoad (SystemOrder.cs:352-353, :375, :377, :393-394;
        // GameManager.cs:615 before :618, 1.6.2 :621, :624), so our OnCreate finds them; a null reads "missing".
        private LeisureSystem m_LeisureSys;
        private TripNeededSystem m_TripNeededSys;
        private ResidentAISystem m_ResidentAI;
        private ResidentAISystem.Actions m_ResidentActions;
        private ResourceBuyerSystem m_ResourceBuyer;

        // ── intervals (read in OnCreate; ch2 R2) ──────────────────────────────────────────────────
        private int m_LeisureInterval = 64;   // what GetUpdateInterval returns (64 unless LS's is a power of two >= 16)
        private int m_LeisureIntervalRaw = 64, m_TripInterval = 16, m_BehaviorInterval = 16, m_BehaviorOffset = 11;

        // ── queries, handles, lookups (registered in OnCreate, so no first-pass sync) ──────────────
        private EntityQuery m_CandidateQuery, m_TouristQuery, m_OccupantQuery, m_MeetingQuery;
        private EntityTypeHandle m_EntityType;
        private ComponentTypeHandle<Citizen> m_CitizenType;
        private ComponentTypeHandle<Leisure> m_LeisureType;            // RW (the claim)
        private BufferTypeHandle<TripNeeded> m_TripType;               // RW (the claim)
        private ComponentTypeHandle<CurrentBuilding> m_CurrentBuildingType;
        private ComponentTypeHandle<HouseholdMember> m_HouseholdMemberType;
        private SharedComponentTypeHandle<UpdateFrame> m_UpdateFrameType;
        private ComponentTypeHandle<TouristHousehold> m_TouristHouseholdType;
        private BufferTypeHandle<HouseholdCitizen> m_HouseholdCitizenType;
        private ComponentTypeHandle<CoordinatedMeeting> m_MeetingType;
        private ComponentLookup<Leisure> m_LeisureLookup;
        private ComponentLookup<CurrentBuilding> m_CurrentBuildingLookup;
        private ComponentLookup<Household> m_HouseholdLookup;
        private ComponentLookup<PropertyRenter> m_PropertyRenterLookup;
        private ComponentLookup<TouristHousehold> m_TouristHouseholdLookup;
        private ComponentLookup<CommuterHousehold> m_CommuterLookup;
        private ComponentLookup<HomelessHousehold> m_HomelessLookup;
        private ComponentLookup<MovingAway> m_MovingAwayLookup;
        private ComponentLookup<AttendingEvent> m_AttendingEventLookup;
        private ComponentLookup<LodgingSeeker> m_LodgingSeekerLookup;
        private ComponentLookup<LodgingProvider> m_LodgingProviderLookup;
        private ComponentLookup<HouseholdNeed> m_HouseholdNeedLookup;
        private ComponentLookup<CarKeeper> m_CarKeeperLookup;
        private ComponentLookup<BicycleOwner> m_BicycleOwnerLookup;
        private ComponentLookup<Game.Vehicles.PersonalCar> m_PersonalCarLookup;
        private ComponentLookup<Game.Vehicles.ParkedCar> m_ParkedCarLookup;
        private BufferLookup<Game.Vehicles.OwnedVehicle> m_OwnedVehicleLookup;
        private ComponentLookup<Game.Objects.Transform> m_TransformLookup;
        private ComponentLookup<Game.Areas.Geometry> m_GeometryLookup;
        private BufferLookup<Game.Areas.Node> m_NodeLookup;
        private ComponentLookup<Building> m_BuildingLookup;
        private ComponentLookup<Destroyed> m_DestroyedLookup;
        private ComponentLookup<Deleted> m_DeletedLookup;
        private ComponentLookup<Game.Buildings.LeisureProvider> m_LeisureProviderLookup;
        private ComponentLookup<SkiSeasonPass> m_SeasonPassLookup;   // Season passes v1 (DECISIONS 240): a holder gets P = 1 in the chance
        private ComponentLookup<Game.Objects.OutsideConnection> m_OutsideConnectionLookup;
        private ComponentLookup<Worker> m_WorkerLookup;
        private ComponentLookup<AttendingMeeting> m_AttendingMeetingLookup;
        // Round M1 (m1-spec §1.2): ResolveJob's read-only lookups (with the existing Deleted, CarKeeper, PersonalCar,
        // Transform and MovingAway ones). Every new type fully qualified: no new using.
        private ComponentLookup<Citizen> m_CitizenLookup;
        private ComponentLookup<TravelPurpose> m_TravelPurposeLookup;
        private ComponentLookup<Game.Citizens.CurrentTransport> m_CurrentTransportLookup;
        private ComponentLookup<Game.Creatures.CurrentVehicle> m_CurrentVehicleLookup;
        private ComponentLookup<Game.Creatures.Divert> m_DivertLookup;
        private ComponentLookup<Game.Pathfind.PathOwner> m_PathOwnerLookup;
        private ComponentLookup<Game.Prefabs.PrefabRef> m_PrefabRefLookup;
        private ComponentLookup<Game.Vehicles.Controller> m_ControllerLookup;
        private ComponentLookup<Game.Vehicles.Bicycle> m_BicycleLookup;
        private ComponentLookup<Game.Vehicles.Taxi> m_TaxiLookup;
        private ComponentLookup<Game.Vehicles.PublicTransport> m_PublicTransportLookup;
        private ComponentLookup<Game.Prefabs.PublicTransportVehicleData> m_TransportVehicleDataLookup;
        private ComponentLookup<HealthProblem> m_HealthProblemLookup;
        private ComponentLookup<HouseholdMember> m_HouseholdMemberLookup;
        private ComponentLookup<Game.Companies.ResourceBuyer> m_ResourceBuyerLookup;
        private BufferLookup<TripNeeded> m_TripLookup;   // RO (ResolveJob); ClaimJob's RW type handle is scheduled after it
        // Round M2: ResolveJob's Leisure lookup is read-write (R4 b, the deadline pull; one access mode per type per job:
        // ClaimJob's RW type handle runs after it, CensusJob keeps the read-only m_LeisureLookup after ClaimJob); the
        // remover-reason and follow lookups (RO; Worker, HouseholdNeed, Building and OutsideConnection exist above).
        private ComponentLookup<Leisure> m_LeisureRW;
        private ComponentLookup<Game.Citizens.Student> m_StudentLookup;   // Game.Buildings has a Student too
        private ComponentLookup<Game.City.Population> m_PopulationLookup;
        private ComponentLookup<Game.Creatures.Resident> m_ResidentLookup;
        // Round M2: the live-check singletons (remover-reason, the queries CitizenBehaviorSystem reads, 1.6.0 :1114-1119),
        // the leisure route cap (R12, the query TripNeededSystem uses, TripNeededSystem.cs:1970 (=)) and NearJob's two
        // chunk queries (D5).
        private EntityQuery m_EconomyQuery, m_TimeDataQuery, m_PopulationQuery, m_TripPriorityQuery, m_WalkerQuery, m_StopPointQuery;
        private ComponentTypeHandle<Game.Creatures.Resident> m_ResidentType;
        private ComponentTypeHandle<Game.Objects.Transform> m_TransformType;
        private ComponentTypeHandle<Game.Creatures.CurrentVehicle> m_CurrentVehicleType;
        private ComponentTypeHandle<Game.Creatures.Divert> m_DivertType;
        private ComponentTypeHandle<Game.Pathfind.PathOwner> m_PathOwnerType;
        private ComponentTypeHandle<Game.Objects.Unspawned> m_UnspawnedType;
        private ComponentTypeHandle<Game.Routes.WaitingPassengers> m_WaitingPassengersType;
        private ComponentTypeHandle<Game.Routes.Position> m_RoutePositionType;

        // ── native state (Persistent; created in OnCreate, disposed in OnDestroy) ────────────────────
        private NativeList<SkiDayClaim> m_Claims;
        private NativeList<AreaJob> m_Areas;
        private NativeList<LodgeJob> m_Lodges;
        private NativeList<SiteOut> m_SiteOut;
        private NativeList<Entity> m_SiteRuns, m_SiteStations;
        private NativeList<SkiDayRules.ClassTrack> m_Tracks;   // RW in ResolveJob, RO in ClaimJob
        private NativeList<ClaimOut> m_ClaimOut;
        private NativeList<ResolveOut> m_ResolveOut;           // areas + 1 (orphan row)
        private NativeList<byte> m_LodgeReject;
        private NativeList<int> m_LodgeCarFails;
        private NativeList<int> m_ActiveNow;
        private NativeArray<int> m_PassStats;                   // Stat.Count
        private NativeArray<int> m_Census;                      // Census.Count
        private NativeList<int> m_Occupants;                    // lodges × Occupant.Kinds
        private NativeList<TravelingOuting> m_Traveling;
        private NativeParallelHashMap<Entity, int> m_AreaOfSign, m_LodgeIndex;
        // Round M1 (m1-spec §1.3, §1.4): measure rows, sized where m_ResolveOut / m_ClaimOut are sized (OnCreate, load,
        // RebuildSnapshot; E4); the ring of ended ski days and its state ([0] next slot, [1] records this load); the
        // resident notes of a pass (cleared before ResolveJob is scheduled).
        private NativeList<int> m_ResolveMeasure;               // (areas + 1) × RM.Count, zeroed by ResolveJob
        private NativeList<double> m_ResolveHours;              // (areas + 1) × RH.Count, zeroed by ResolveJob
        private NativeList<int> m_ClaimMeasure;                 // areas × CM.Count, zeroed before ClaimJob
        private NativeArray<ClaimRecord> m_Ring;                // Tuning.RingSize
        private NativeArray<int> m_RingState;                   // 2
        private NativeList<ResidentNote> m_ResidentNotes;
        // Round M2 (D5): NearJob's per-lodge result (aligned with m_Lodges, resized when scheduled, like m_Occupants) and
        // its totals ([0] walkers scanned, [1] line stop points scanned).
        private NativeList<NearOut> m_NearOut;
        private NativeArray<int> m_NearTotals;
        private JobHandle m_PassHandle;
        private bool m_PassInFlight, m_CensusInFlight, m_NearInFlight;

        // ── managed state ─────────────────────────────────────────────────────────────────────────
        private readonly Dictionary<Entity, AreaMemory> m_State = new Dictionary<Entity, AreaMemory>();   // by sign
        private readonly Dictionary<Entity, SkiDayAreaView> m_Views = new Dictionary<Entity, SkiDayAreaView>();
        private readonly Dictionary<Entity, int> m_CarFailsByLodge = new Dictionary<Entity, int>();      // this load
        private readonly Dictionary<Entity, uint> m_TravelingSince = new Dictionary<Entity, uint>();     // D30 meeting → first census frame
        private readonly Dictionary<Entity, Occupants> m_OccupantsByLodge = new Dictionary<Entity, Occupants>();
        private readonly HashSet<Entity> m_SeenMeetings = new HashSet<Entity>();
        private readonly List<Entity> m_PruneScratch = new List<Entity>(8);
        private readonly List<Entity> m_SignScratch = new List<Entity>(4);
        private readonly List<Entity> m_LodgeScratch = new List<Entity>(4);
        // Catalog Phase B (seam S5): the snapshot's active lodges with their tier (RebuildSnapshot).
        private readonly List<SkiResortAttractivenessSystem.LodgeInfo> m_LodgeInfoScratch = new List<SkiResortAttractivenessSystem.LodgeInfo>(4);
        private readonly List<Entity> m_RunScratch = new List<Entity>(16);
        private readonly List<Entity> m_StationScratch = new List<Entity>(8);
        private readonly List<Entity> m_SnapSigns = new List<Entity>(4);       // signs of the current snapshot
        private readonly List<AreaInfo> m_AreaInfo = new List<AreaInfo>(4);    // managed per-area values of the snapshot
        private readonly List<Entity> m_DumpSigns = new List<Entity>(4);       // signs of the last applied pass
        private readonly List<DumpLodge> m_DumpLodges = new List<DumpLodge>(8);
        private readonly int[] m_LastPassStats = new int[Stat.Count];
        private readonly int[] m_CensusLast = new int[Census.Count];
        private readonly float[] m_Precip = new float[SkiDayRules.SnowHourSamples];    // newest at index 0
        private readonly float[] m_Ambient = new float[SkiDayRules.SnowHourSamples];
        private readonly LogWindow m_AreaLog = new LogWindow(Tuning.LogWindowS);
        private readonly LogWindow m_LodgesLog = new LogWindow(Tuning.LogWindowS);
        private readonly LogWindow m_SettingsLog = new LogWindow(Tuning.LogWindowS);
        private readonly LogWindow m_ReachLog = new LogWindow(Tuning.LogWindowS);   // Round M1 M6: on and off
        // Round M1 M5: the managed copy of the ring (the DUMP never reads a native container).
        private readonly ClaimRecord[] m_RingCopy = new ClaimRecord[Tuning.RingSize];
        // Round M1 M7 (a2): resident ski-day arrivals of the month in progress and of the closed month awaiting its
        // ledger check (swapped at a roll, so not readonly).
        private List<ResidentVisit> m_VisitsThis = new List<ResidentVisit>(Tuning.VisitsCapacity);
        private List<ResidentVisit> m_VisitsClosed = new List<ResidentVisit>(Tuning.VisitsCapacity);
        // fixer R2 / L6: the closed month's visits per resident, rebuilt at each check (reused, no per-check allocation)
        private readonly Dictionary<Entity, VisitTally> m_LedgerTally = new Dictionary<Entity, VisitTally>(Tuning.VisitsCapacity);
        // Round M1 M9: the year's hourly climate samples (24 × days; reallocated only when daysPerYear changes) and
        // per-area scratch.
        private float[] m_YearPrecip, m_YearAmbient;
        private bool[] m_YearSnow;
        private readonly int[] m_YearMarks = new int[12];
        private readonly int[] m_YearSnowHours = new int[12];
        // Round M2 (D5): the last walker scan per lodge (the DUMP) and this game hour's per-lodge accumulators (the
        // hourly `[near]` line); values only, rebuilt per scan / cleared per hour (no allocation after warm-up).
        private readonly Dictionary<Entity, NearCopy> m_NearByLodge = new Dictionary<Entity, NearCopy>(8);
        private readonly Dictionary<Entity, NearHour> m_NearHour = new Dictionary<Entity, NearHour>(8);

        // ── per-load state ────────────────────────────────────────────────────────────────────────
        private bool m_Armed, m_Disabled, m_ErrorLogged, m_CapWarned, m_DriftWarned, m_Idle;
        private string m_IdleReason;
        private SkiDayRules.MonthFlags m_Month;
        private SkiDayCityMonth m_CityThis;
        private SkiDayRules.Knobs m_Knobs;
        private int m_Price;
        private bool m_KnobsKnown;
        // snapshot keys
        private bool m_SnapshotValid, m_SiteDue, m_CensusDue;
        private int m_SnapGeneration, m_SnapLodgeRevision, m_SnapLiftRevision, m_SnapHourKey, m_PassesSinceRebuild;
        // climate ring
        private int m_HourKey = -1;
        private bool m_RingValid;
        private float m_BaseHeight, m_Lapse, m_Freezing;
        // pass and census bookkeeping
        private uint m_PassFrame, m_LastPassFrame, m_CensusFrame, m_CensusAppliedFrame;
        private bool m_LastPassKnown, m_CensusKnown;
        private long m_PassesLoad, m_WalkPassesLoad, m_Rebuilds, m_SiteJobs;
        private int m_TrackedLast, m_OnTheWayLast, m_OnSiteLast;
        // this load's outcome totals (DUMP line 1). m_LoadArrivedNoLeisure ("reached the lodge without Leisure") counts, since
        // Round M2 (D3, R6), only citizens that still exist when found in the lodge with Leisure gone: they are booked as
        // arrivals (visit ended "removed (on arrival)"); a deleted citizen in the lodge stays a failure and is not counted.
        private long m_LoadSent, m_LoadCars, m_LoadArrived, m_LoadGone, m_LoadRetargeted, m_LoadRetargetedAtOrigin,
                     m_LoadExpired, m_LoadLeft, m_LoadArrivedNoLeisure, m_LoadOrphaned;
        // cost (once per load, then daily at midday: Round M1 M8)
        private int m_CostPasses, m_CostWait, m_CostMeasurements, m_CostArmDay = int.MinValue;
        private double m_CostSumMs, m_CostMaxMs, m_RebuildSumMs, m_RebuildMaxMs, m_CostWaitMs;
        private bool m_CostLogged, m_CostFirstSkipped;
        // Round M1: city-wide last-pass snapshots (DUMP header), the ring copy's position, the month-start frame (M7c)
        private int m_LeavingLast, m_WatchingLast, m_RingCopyNext, m_RingCopyTotal;
        private uint m_MonthStartFrame;
        private bool m_HasMonthStart;
        private bool m_MonthByJump;   // fixer R8: the month in progress started at a calendar jump, not at the load
        // Round M1 M7 (a2): the closed month's ledger check
        private int m_VisitsClosedKey = SkiDayRules.NoMonth;
        private uint m_VisitsClosedAt;
        private bool m_LedgerCheckDue;
        private string m_LastLedgerCheck;
        // Round M1 M9: the year table's key (climate entity, days, game year) and its last sampling time
        private Entity m_YearClimate;
        private int m_YearDays, m_YearYear;
        private bool m_YearValid;
        private double m_YearSampleMs;
        // fixer M1-ECS-6, L8, R9: ++ at every refill of the samples (an area's table is stale while its generation
        // differs) and why that refill happened ("at load", "after the year roll", "after a climate change")
        private int m_YearGeneration;
        private string m_YearWhy;
        // Round M2 (R9): the trip systems' found / on masks (SkiDayRules.Trip… bits; "all on" until read, so an Editor
        // load reports the game's own planner), the changes seen during play, and the hub label and blocking flag.
        private int m_TripFound = SkiDayRules.TripAll, m_TripOn = SkiDayRules.TripAll, m_TripChanges;
        private string m_TripLabel = "";
        private bool m_TripBlocks;
        // Round M2 (R10): the trip knobs both jobs read (defaults; FitUsesAllOutcomes hard-coded false this round).
        private SkiDayRules.TripKnobs m_TripKnobs = SkiDayRules.DefaultTripKnobs();
        // Round M2 (D10): the measurement in progress was re-armed at a midday (not the load's), so a day change before
        // it is written writes it partial (CostDayEnd).
        private bool m_CostByRearm;
        // Round M2 (D5, D9, D1): city-wide last-pass snapshots for the DUMP header (followed after leaving, sent this
        // pass) and the follow cap's input; the follow-cap WARN flag.
        private int m_FollowingLast, m_SentLast;
        private bool m_FollowWarned;
        // Round M2 (D5): the walker scan's schedule (due flag, passes with an area since the last one, its frame and
        // minute), the scans of the current game hour and the climate hour step of this pass (ClimateTick stores it
        // before its no-climate return, so the hourly line never stops silently).
        private bool m_NearDue, m_NearKnown;
        private int m_NearPasses, m_NearMinute, m_NearHourScans, m_HourStep;
        private uint m_NearFrame;

        // ── accessors (ch2 §9; s1-spec §2.5) ───────────────────────────────────────────────────────

        /// <summary>Armed this load and not disabled by an exception (m_Armed &amp;&amp; !m_Disabled).</summary>
        internal bool IsArmed => m_Armed && !m_Disabled;

        /// <summary>++ at load, arm, area-set change, and when any published view changed (at most once per
        /// pass); compare for equality; never reset.</summary>
        internal int Revision { get; private set; }

        /// <summary>The last pass's view of <paramref name="sign"/>. Main thread, no job sync; false: unarmed or
        /// unknown sign (a sign enters at its first applied pass).</summary>
        internal bool TryGetArea(Entity sign, out SkiDayAreaView view)
        {
            if (!IsArmed)
            {
                view = default;
                return false;
            }
            return m_Views.TryGetValue(sign, out view);
        }

        /// <summary>Round M2 (R9): the hub's trip-planner label (CityStats.tripPlanner): "" while the game's six trip
        /// systems are all on (and before a Game load reads them), else the known swaps' names or
        /// SkiTripSystems.UnknownLabel. Main thread.</summary>
        internal string TripPlannerLabel => m_TripLabel;

        /// <summary>Round M2 (R9): LeisureSystem or CitizenBehaviorSystem is switched off, so ski days are off
        /// (CityStats.tripPlannerBlocksSkiDays). Main thread.</summary>
        internal bool TripPlannerBlocksSkiDays => m_TripBlocks;

        /// <summary>The ski-day lines of the stats DUMP (ch2 §10). Appends only, never throws; managed copies
        /// only (never a native container).</summary>
        internal void AppendDump(StringBuilder sb)
        {
            try
            {
                AppendDumpBody(sb);
            }
            catch (Exception e)
            {
                sb.Append("  Ski days: dump failed (").Append(e.GetType().Name).Append(": ").Append(e.Message).Append(")\n");
            }
        }

        // ── lifecycle ──────────────────────────────────────────────────────────────────────────────

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Simulation = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_Time = World.GetOrCreateSystemManaged<TimeSystem>();
            m_Climate = World.GetOrCreateSystemManaged<ClimateSystem>();
            m_LeisureSys = World.GetOrCreateSystemManaged<LeisureSystem>();
            m_Behavior = World.GetOrCreateSystemManaged<CitizenBehaviorSystem>();
            m_TripNeededSys = World.GetOrCreateSystemManaged<TripNeededSystem>();
            // Round M2 (R9): the other three trip systems, for their Enabled flags only (GetExisting: SystemOrder created
            // them before any mod's OnLoad, header; a null reads "missing" in the trip systems line).
            m_ResidentAI = World.GetExistingSystemManaged<ResidentAISystem>();
            m_ResidentActions = World.GetExistingSystemManaged<ResidentAISystem.Actions>();
            m_ResourceBuyer = World.GetExistingSystemManaged<ResourceBuyerSystem>();
            m_NameSystem = World.GetOrCreateSystemManaged<Game.UI.NameSystem>();
            m_Attraction = World.GetOrCreateSystemManaged<SkiResortAttractivenessSystem>();
            m_Ticket = World.GetOrCreateSystemManaged<SkiLiftTicketSystem>();
            m_SnowLine = World.GetOrCreateSystemManaged<SnowLineSystem>();
            m_LiftCensus = World.GetOrCreateSystemManaged<LiftCensusSystem>();
            m_Counter = World.GetOrCreateSystemManaged<SkiGuestCounterSystem>();

            // ch2 R2: LeisureSystem's interval (262144 / kUpdatePerDay, LeisureSystem.cs:914-917, 1.6.2 :911-914). A
            // value that is not a power of two >= 16 is never returned (UpdateSystem.Register throws on a non-power
            // of two, UpdateSystem.cs:303-306); ArmCheck refuses it at load (L3).
            m_LeisureIntervalRaw = m_LeisureSys.GetUpdateInterval(SystemUpdatePhase.GameSimulation);
            m_LeisureInterval = SkiDayRules.IsPowerOfTwo(m_LeisureIntervalRaw) && m_LeisureIntervalRaw >= 16 ? m_LeisureIntervalRaw : 64;
            m_TripInterval = m_TripNeededSys.GetUpdateInterval(SystemUpdatePhase.GameSimulation);          // 16 (:1939-1942)
            m_BehaviorInterval = m_Behavior.GetUpdateInterval(SystemUpdatePhase.GameSimulation);           // 16 (:1016-1024, 1.6.2 :1048-1056)
            m_BehaviorOffset = m_Behavior.GetUpdateOffset(SystemUpdatePhase.GameSimulation);               // 11

            // ch2 §5.1. Busy / Ill / gone are archetype exclusions (CitizenFacts.Has… = false in the job).
            m_CandidateQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Citizen>(),
                    ComponentType.ReadWrite<Leisure>(),
                    ComponentType.ReadWrite<TripNeeded>(),
                    ComponentType.ReadOnly<CurrentBuilding>(),
                    ComponentType.ReadOnly<HouseholdMember>(),
                    ComponentType.ReadOnly<UpdateFrame>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<TravelPurpose>(),
                    ComponentType.ReadOnly<Game.Pathfind.PathInformation>(),
                    ComponentType.ReadOnly<Game.Common.Target>(),
                    ComponentType.ReadOnly<Game.Companies.ResourceBuyer>(),
                    ComponentType.ReadOnly<AttendingMeeting>(),
                    ComponentType.ReadOnly<HealthProblem>(),
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            m_TouristQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<TouristHousehold>(),
                    ComponentType.ReadOnly<Household>(),
                    ComponentType.ReadOnly<HouseholdCitizen>(),
                },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            // D30 (s1-spec N11, ch2-RF2-3): lodge occupants and outings Traveling to our targets.
            m_OccupantQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Citizen>(),
                    ComponentType.ReadOnly<CurrentBuilding>(),
                    ComponentType.ReadOnly<HouseholdMember>(),
                },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            m_MeetingQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<CoordinatedMeeting>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });

            // ch2 §5.2: one access mode per type per job.
            m_EntityType = GetEntityTypeHandle();
            m_CitizenType = GetComponentTypeHandle<Citizen>(isReadOnly: true);
            m_LeisureType = GetComponentTypeHandle<Leisure>(isReadOnly: false);
            m_TripType = GetBufferTypeHandle<TripNeeded>(isReadOnly: false);
            m_CurrentBuildingType = GetComponentTypeHandle<CurrentBuilding>(isReadOnly: true);
            m_HouseholdMemberType = GetComponentTypeHandle<HouseholdMember>(isReadOnly: true);
            m_UpdateFrameType = GetSharedComponentTypeHandle<UpdateFrame>();
            m_TouristHouseholdType = GetComponentTypeHandle<TouristHousehold>(isReadOnly: true);
            m_HouseholdCitizenType = GetBufferTypeHandle<HouseholdCitizen>(isReadOnly: true);
            m_MeetingType = GetComponentTypeHandle<CoordinatedMeeting>(isReadOnly: true);
            m_LeisureLookup = GetComponentLookup<Leisure>(isReadOnly: true);
            m_CurrentBuildingLookup = GetComponentLookup<CurrentBuilding>(isReadOnly: true);
            m_HouseholdLookup = GetComponentLookup<Household>(isReadOnly: true);
            m_PropertyRenterLookup = GetComponentLookup<PropertyRenter>(isReadOnly: true);
            m_TouristHouseholdLookup = GetComponentLookup<TouristHousehold>(isReadOnly: true);
            m_CommuterLookup = GetComponentLookup<CommuterHousehold>(isReadOnly: true);
            m_HomelessLookup = GetComponentLookup<HomelessHousehold>(isReadOnly: true);
            m_MovingAwayLookup = GetComponentLookup<MovingAway>(isReadOnly: true);
            m_AttendingEventLookup = GetComponentLookup<AttendingEvent>(isReadOnly: true);
            m_LodgingSeekerLookup = GetComponentLookup<LodgingSeeker>(isReadOnly: true);
            m_LodgingProviderLookup = GetComponentLookup<LodgingProvider>(isReadOnly: true);
            m_HouseholdNeedLookup = GetComponentLookup<HouseholdNeed>(isReadOnly: true);
            m_CarKeeperLookup = GetComponentLookup<CarKeeper>(isReadOnly: true);
            m_BicycleOwnerLookup = GetComponentLookup<BicycleOwner>(isReadOnly: true);
            m_PersonalCarLookup = GetComponentLookup<Game.Vehicles.PersonalCar>(isReadOnly: true);
            m_ParkedCarLookup = GetComponentLookup<Game.Vehicles.ParkedCar>(isReadOnly: true);
            m_OwnedVehicleLookup = GetBufferLookup<Game.Vehicles.OwnedVehicle>(isReadOnly: true);
            m_TransformLookup = GetComponentLookup<Game.Objects.Transform>(isReadOnly: true);
            m_GeometryLookup = GetComponentLookup<Game.Areas.Geometry>(isReadOnly: true);
            m_NodeLookup = GetBufferLookup<Game.Areas.Node>(isReadOnly: true);
            m_BuildingLookup = GetComponentLookup<Building>(isReadOnly: true);
            m_DestroyedLookup = GetComponentLookup<Destroyed>(isReadOnly: true);
            m_DeletedLookup = GetComponentLookup<Deleted>(isReadOnly: true);
            m_LeisureProviderLookup = GetComponentLookup<Game.Buildings.LeisureProvider>(isReadOnly: true);
            m_SeasonPassLookup = GetComponentLookup<SkiSeasonPass>(isReadOnly: true);   // Season passes v1
            m_OutsideConnectionLookup = GetComponentLookup<Game.Objects.OutsideConnection>(isReadOnly: true);
            m_WorkerLookup = GetComponentLookup<Worker>(isReadOnly: true);
            m_AttendingMeetingLookup = GetComponentLookup<AttendingMeeting>(isReadOnly: true);
            // Round M1 (m1-spec §1.2, E10): all read-only, so ResolveJob declares each [ReadOnly].
            m_CitizenLookup = GetComponentLookup<Citizen>(isReadOnly: true);
            m_TravelPurposeLookup = GetComponentLookup<TravelPurpose>(isReadOnly: true);
            m_CurrentTransportLookup = GetComponentLookup<Game.Citizens.CurrentTransport>(isReadOnly: true);
            m_CurrentVehicleLookup = GetComponentLookup<Game.Creatures.CurrentVehicle>(isReadOnly: true);
            m_DivertLookup = GetComponentLookup<Game.Creatures.Divert>(isReadOnly: true);
            m_PathOwnerLookup = GetComponentLookup<Game.Pathfind.PathOwner>(isReadOnly: true);
            m_PrefabRefLookup = GetComponentLookup<Game.Prefabs.PrefabRef>(isReadOnly: true);
            m_ControllerLookup = GetComponentLookup<Game.Vehicles.Controller>(isReadOnly: true);
            m_BicycleLookup = GetComponentLookup<Game.Vehicles.Bicycle>(isReadOnly: true);
            m_TaxiLookup = GetComponentLookup<Game.Vehicles.Taxi>(isReadOnly: true);
            m_PublicTransportLookup = GetComponentLookup<Game.Vehicles.PublicTransport>(isReadOnly: true);
            m_TransportVehicleDataLookup = GetComponentLookup<Game.Prefabs.PublicTransportVehicleData>(isReadOnly: true);
            m_HealthProblemLookup = GetComponentLookup<HealthProblem>(isReadOnly: true);
            m_HouseholdMemberLookup = GetComponentLookup<HouseholdMember>(isReadOnly: true);
            m_ResourceBuyerLookup = GetComponentLookup<Game.Companies.ResourceBuyer>(isReadOnly: true);
            m_TripLookup = GetBufferLookup<TripNeeded>(isReadOnly: true);
            // Round M2: ResolveJob's RW Leisure lookup (R4 b), the remover-reason and follow lookups (RO), the live-check
            // singletons (CitizenBehaviorSystem's own three queries, 1.6.0 :1114-1119, 1.6.2 :1146-1151), the
            // leisure route cap (R12; not RequireForUpdate: its absence is logged) and NearJob's chunk queries and type
            // handles (D5; all read-only: one access mode per type per job).
            m_LeisureRW = GetComponentLookup<Leisure>(isReadOnly: false);
            m_StudentLookup = GetComponentLookup<Game.Citizens.Student>(isReadOnly: true);
            m_PopulationLookup = GetComponentLookup<Game.City.Population>(isReadOnly: true);
            m_ResidentLookup = GetComponentLookup<Game.Creatures.Resident>(isReadOnly: true);
            m_EconomyQuery = GetEntityQuery(ComponentType.ReadOnly<Game.Prefabs.EconomyParameterData>());
            m_TimeDataQuery = GetEntityQuery(ComponentType.ReadOnly<Game.Common.TimeData>());
            m_PopulationQuery = GetEntityQuery(ComponentType.ReadOnly<Game.City.Population>());
            m_TripPriorityQuery = GetEntityQuery(ComponentType.ReadOnly<Game.Prefabs.TripPriorityParametersData>());
            m_WalkerQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Creatures.Resident>(), ComponentType.ReadOnly<Game.Objects.Transform>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            m_StopPointQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Routes.WaitingPassengers>(), ComponentType.ReadOnly<Game.Routes.Position>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            m_ResidentType = GetComponentTypeHandle<Game.Creatures.Resident>(isReadOnly: true);
            m_TransformType = GetComponentTypeHandle<Game.Objects.Transform>(isReadOnly: true);
            m_CurrentVehicleType = GetComponentTypeHandle<Game.Creatures.CurrentVehicle>(isReadOnly: true);
            m_DivertType = GetComponentTypeHandle<Game.Creatures.Divert>(isReadOnly: true);
            m_PathOwnerType = GetComponentTypeHandle<Game.Pathfind.PathOwner>(isReadOnly: true);
            m_UnspawnedType = GetComponentTypeHandle<Game.Objects.Unspawned>(isReadOnly: true);
            m_WaitingPassengersType = GetComponentTypeHandle<Game.Routes.WaitingPassengers>(isReadOnly: true);
            m_RoutePositionType = GetComponentTypeHandle<Game.Routes.Position>(isReadOnly: true);

            m_Claims = new NativeList<SkiDayClaim>(256, Allocator.Persistent);
            m_Areas = new NativeList<AreaJob>(4, Allocator.Persistent);
            m_Lodges = new NativeList<LodgeJob>(8, Allocator.Persistent);
            m_SiteOut = new NativeList<SiteOut>(8, Allocator.Persistent);
            m_SiteRuns = new NativeList<Entity>(32, Allocator.Persistent);
            m_SiteStations = new NativeList<Entity>(16, Allocator.Persistent);
            m_Tracks = new NativeList<SkiDayRules.ClassTrack>(12, Allocator.Persistent);
            m_ClaimOut = new NativeList<ClaimOut>(4, Allocator.Persistent);
            m_ResolveOut = new NativeList<ResolveOut>(5, Allocator.Persistent);
            m_LodgeReject = new NativeList<byte>(8, Allocator.Persistent);
            m_LodgeCarFails = new NativeList<int>(8, Allocator.Persistent);
            m_ActiveNow = new NativeList<int>(4, Allocator.Persistent);
            m_PassStats = new NativeArray<int>(Stat.Count, Allocator.Persistent);
            m_Census = new NativeArray<int>(Census.Count, Allocator.Persistent);
            m_Occupants = new NativeList<int>(8 * Occupant.Kinds, Allocator.Persistent);
            m_Traveling = new NativeList<TravelingOuting>(8, Allocator.Persistent);
            m_AreaOfSign = new NativeParallelHashMap<Entity, int>(8, Allocator.Persistent);
            m_LodgeIndex = new NativeParallelHashMap<Entity, int>(16, Allocator.Persistent);
            m_ResolveMeasure = new NativeList<int>(5 * RM.Count, Allocator.Persistent);
            m_ResolveHours = new NativeList<double>(5 * RH.Count, Allocator.Persistent);
            m_ClaimMeasure = new NativeList<int>(4 * CM.Count, Allocator.Persistent);
            m_Ring = new NativeArray<ClaimRecord>(Tuning.RingSize, Allocator.Persistent);
            m_RingState = new NativeArray<int>(2, Allocator.Persistent);
            m_ResidentNotes = new NativeList<ResidentNote>(16, Allocator.Persistent);
            m_NearOut = new NativeList<NearOut>(8, Allocator.Persistent);
            m_NearTotals = new NativeArray<int>(2, Allocator.Persistent);
            m_ResolveOut.Resize(1, NativeArrayOptions.ClearMemory);   // the orphan row exists from the start
            m_ResolveMeasure.Resize(RM.Count, NativeArrayOptions.ClearMemory);   // and its measure rows (E4)
            m_ResolveHours.Resize(RH.Count, NativeArrayOptions.ClearMemory);

            // Job reflection data and JIT before the first Schedule (the counter's precedent). Schedules nothing.
            IJobExtensions.EarlyJobInit<SiteJob>();
            IJobExtensions.EarlyJobInit<ResolveJob>();
            IJobExtensions.EarlyJobInit<ClaimJob>();
            IJobExtensions.EarlyJobInit<CensusJob>();
            IJobExtensions.EarlyJobInit<NearJob>();   // Round M2 (D5)

            Mod.Log.Info($"{nameof(SkiDaySystem)} created");
        }

        /// <summary>LeisureSystem's interval (header PHASE): the anchor's offset is inherited only when the
        /// intervals are equal (ch2 R2; 64 before OnCreate and when LeisureSystem's is unusable).</summary>
        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return m_LeisureInterval;
        }

        protected override void OnDestroy()
        {
            try
            {
                m_PassHandle.Complete();
                // Round M2 (run 1 D7): a ledger check due at a quit to desktop is never run (no preload: the game disposes
                // the mods, then destroys the world, GameManager.cs:792-793, 1.6.2 :802-803), so say so; this line lands
                // after "OnDispose" by that order, and its words say why.
                if (m_Armed && !m_Disabled && m_LedgerCheckDue)
                    Mod.Log.Info($"SkiDays [day] {MonthText(m_VisitsClosedKey)} ledger check skipped " +
                                 $"(quit before it was due; {m_VisitsClosed.Count} resident arrival(s))");
                FlushLogs(all: true);
            }
            catch (Exception)
            {
                // teardown: nothing left to report to
            }
            if (m_Claims.IsCreated) m_Claims.Dispose();
            if (m_Areas.IsCreated) m_Areas.Dispose();
            if (m_Lodges.IsCreated) m_Lodges.Dispose();
            if (m_SiteOut.IsCreated) m_SiteOut.Dispose();
            if (m_SiteRuns.IsCreated) m_SiteRuns.Dispose();
            if (m_SiteStations.IsCreated) m_SiteStations.Dispose();
            if (m_Tracks.IsCreated) m_Tracks.Dispose();
            if (m_ClaimOut.IsCreated) m_ClaimOut.Dispose();
            if (m_ResolveOut.IsCreated) m_ResolveOut.Dispose();
            if (m_LodgeReject.IsCreated) m_LodgeReject.Dispose();
            if (m_LodgeCarFails.IsCreated) m_LodgeCarFails.Dispose();
            if (m_ActiveNow.IsCreated) m_ActiveNow.Dispose();
            if (m_PassStats.IsCreated) m_PassStats.Dispose();
            if (m_Census.IsCreated) m_Census.Dispose();
            if (m_Occupants.IsCreated) m_Occupants.Dispose();
            if (m_Traveling.IsCreated) m_Traveling.Dispose();
            if (m_AreaOfSign.IsCreated) m_AreaOfSign.Dispose();
            if (m_LodgeIndex.IsCreated) m_LodgeIndex.Dispose();
            if (m_ResolveMeasure.IsCreated) m_ResolveMeasure.Dispose();
            if (m_ResolveHours.IsCreated) m_ResolveHours.Dispose();
            if (m_ClaimMeasure.IsCreated) m_ClaimMeasure.Dispose();
            if (m_Ring.IsCreated) m_Ring.Dispose();
            if (m_RingState.IsCreated) m_RingState.Dispose();
            if (m_ResidentNotes.IsCreated) m_ResidentNotes.Dispose();
            if (m_NearOut.IsCreated) m_NearOut.Dispose();
            if (m_NearTotals.IsCreated) m_NearTotals.Dispose();
            base.OnDestroy();
        }

        /// <summary>A load (or leaving to the menu), ch2 R5: completes the last pass, books it, writes the month
        /// in progress (` (until the load)`) and flushes the folds. Never throws: GameSystemBase.GamePreload
        /// DISABLES a system whose OnGamePreload throws (GameSystemBase.cs:85-96 (=)).</summary>
        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            try
            {
                m_PassHandle.Complete();
                if (m_Armed && !m_Disabled)
                {
                    if (m_PassInFlight)
                        ApplyPass();
                    if (m_Month.Key != SkiDayRules.NoMonth)
                        WriteDayLines(m_Month.Key, SinceText() + " (until the load)");
                    // Fixer L13: a ledger check still waiting (4,096 frames after the roll) is lost with the load; Round M2
                    // (run 1 D7): the words also fit a return to the menu, which comes through here too.
                    if (m_LedgerCheckDue)
                        Mod.Log.Info($"SkiDays [day] {MonthText(m_VisitsClosedKey)} ledger check lost (a load or a return to the menu came " +
                                     $"before it was due; {m_VisitsClosed.Count} resident arrival(s))");
                }
                m_PassInFlight = m_CensusInFlight = m_NearInFlight = false;
                FlushLogs(all: true);
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"SkiDays [day]: the month in progress could not be written before the load " +
                             $"({e.GetType().Name}: {e.Message}) — non-fatal");
            }
        }

        /// <summary>ch2 R4: per-load reset; arm in Game mode when ArmCheck is Ok and the day is 262,144 frames.</summary>
        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            m_PassHandle.Complete();
            m_PassInFlight = m_CensusInFlight = m_NearInFlight = false;
            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = m_CapWarned = m_DriftWarned = m_Idle = false;
            m_IdleReason = null;
            m_Month = SkiDayRules.NewMonthFlags();
            m_CityThis = default;
            m_KnobsKnown = false;
            m_SnapshotValid = false;
            m_SiteDue = false;
            m_CensusDue = true;   // the first pass after a load (ch2 §5.6)
            m_SnapGeneration = m_SnapLodgeRevision = m_SnapLiftRevision = 0;
            m_SnapHourKey = -1;
            m_PassesSinceRebuild = 0;
            m_HourKey = -1;
            m_RingValid = false;
            m_PassFrame = m_LastPassFrame = m_CensusFrame = m_CensusAppliedFrame = 0;
            m_LastPassKnown = m_CensusKnown = false;
            m_PassesLoad = m_WalkPassesLoad = m_Rebuilds = m_SiteJobs = 0;
            m_TrackedLast = m_OnTheWayLast = m_OnSiteLast = 0;
            m_LoadSent = m_LoadCars = m_LoadArrived = m_LoadGone = m_LoadRetargeted = m_LoadRetargetedAtOrigin = 0;
            m_LoadExpired = m_LoadLeft = m_LoadArrivedNoLeisure = m_LoadOrphaned = 0;
            m_CostPasses = m_CostWait = 0;
            m_CostSumMs = m_CostMaxMs = m_RebuildSumMs = m_RebuildMaxMs = 0.0;
            m_CostLogged = m_CostFirstSkipped = false;
            // Round M1 (E12: everything new is memory only and resets here)
            m_CostMeasurements = 0;
            m_CostArmDay = int.MinValue;
            m_CostWaitMs = 0.0;
            m_LeavingLast = m_WatchingLast = 0;
            m_RingCopyNext = m_RingCopyTotal = 0;
            Array.Clear(m_RingCopy, 0, m_RingCopy.Length);
            m_MonthStartFrame = 0;
            m_HasMonthStart = false;
            m_MonthByJump = false;
            m_VisitsThis.Clear();
            m_VisitsClosed.Clear();
            m_VisitsClosedKey = SkiDayRules.NoMonth;
            m_VisitsClosedAt = 0;
            m_LedgerCheckDue = false;
            m_LastLedgerCheck = null;
            m_YearClimate = Entity.Null;
            m_YearDays = m_YearYear = 0;
            m_YearValid = false;
            m_YearSampleMs = 0.0;
            m_YearGeneration = 0;
            m_YearWhy = null;
            m_AreaLog.Reset();
            m_LodgesLog.Reset();
            m_SettingsLog.Reset();
            m_ReachLog.Reset();
            // Round M2 (everything new is memory only and resets here)
            m_NearDue = true;   // the first pass with an area scans the walkers
            m_NearKnown = false;
            m_NearPasses = m_NearMinute = m_NearHourScans = m_HourStep = 0;
            m_NearFrame = 0;
            m_NearByLodge.Clear();
            m_NearHour.Clear();
            m_NearOut.Clear();
            m_NearTotals[0] = 0;
            m_NearTotals[1] = 0;
            m_FollowingLast = m_SentLast = 0;
            m_FollowWarned = false;
            m_TripFound = m_TripOn = SkiDayRules.TripAll;   // "all on" until read (an Editor load reports the game's own)
            m_TripChanges = 0;
            m_TripLabel = "";
            m_TripBlocks = false;
            m_TripKnobs = SkiDayRules.DefaultTripKnobs();   // R10: code constants, the switch hard-coded false
            m_CostByRearm = false;

            m_Claims.Clear();
            m_Areas.Clear();
            m_Lodges.Clear();
            m_SiteOut.Clear();
            m_SiteRuns.Clear();
            m_SiteStations.Clear();
            m_Tracks.Clear();
            m_ClaimOut.Clear();
            m_ResolveOut.Clear();
            m_ResolveOut.Resize(1, NativeArrayOptions.ClearMemory);
            m_ClaimMeasure.Clear();
            m_ResolveMeasure.Clear();
            m_ResolveMeasure.Resize(RM.Count, NativeArrayOptions.ClearMemory);   // the orphan row (E4)
            m_ResolveHours.Clear();
            m_ResolveHours.Resize(RH.Count, NativeArrayOptions.ClearMemory);
            m_RingState[0] = 0;
            m_RingState[1] = 0;
            m_ResidentNotes.Clear();
            m_LodgeReject.Clear();
            m_LodgeCarFails.Clear();
            m_ActiveNow.Clear();
            for (int i = 0; i < m_PassStats.Length; i++)
                m_PassStats[i] = 0;
            for (int i = 0; i < m_Census.Length; i++)
                m_Census[i] = 0;
            m_Occupants.Clear();
            m_Traveling.Clear();
            m_AreaOfSign.Clear();
            m_LodgeIndex.Clear();
            m_State.Clear();
            m_Views.Clear();
            m_CarFailsByLodge.Clear();
            m_TravelingSince.Clear();
            m_OccupantsByLodge.Clear();
            m_SnapSigns.Clear();
            m_AreaInfo.Clear();
            m_DumpSigns.Clear();
            m_DumpLodges.Clear();
            Array.Clear(m_LastPassStats, 0, m_LastPassStats.Length);
            Array.Clear(m_CensusLast, 0, m_CensusLast.Length);

            m_Parking = World.GetExistingSystemManaged<SkiLodgeParkingSystem>();   // created after us (ch2-RF2-9)
            Revision++;

            if (mode != GameMode.Game)
                return;   // R9 read on Game loads only: ski days never arm in the Editor (a deliberate reading of R9)

            // Round M2 (R9): the six trip systems' Enabled flags, read here, after every mod's IMod.OnLoad (mods load at boot,
            // GameManager.cs:618 after CreateSystems :615, long before a city's onGameLoadingComplete :1126; 1.6.2 :624,
            // :621, :1136). Reading a property writes nothing; an exception (not expected) reads as all on.
            try
            {
                m_TripOn = ReadTripSystems(out m_TripFound);
            }
            catch (Exception e)
            {
                m_TripFound = m_TripOn = SkiDayRules.TripAll;
                Mod.Log.Warn($"SkiDays: trip systems could not be read ({e.GetType().Name}: {e.Message}); treated as all on (log and label only)");
            }
            int planners = SkiOtherMods.TripPlannerMask;   // SkiOtherMods.Refresh ran in the guest counter's load handler (Mod.cs:233 before :252)
            m_TripLabel = SkiTripSystems.Label(m_TripFound, m_TripOn, planners);
            m_TripBlocks = SkiTripSystems.BlocksSkiDays(m_TripOn);
            Revision++;   // the hub re-reads the label (also on the NOT ARMED path below)
            string tripLine = SkiTripSystems.LoadLine(m_TripFound, m_TripOn, planners);
            if (TripAllOn(m_TripFound, m_TripOn))
                Mod.Log.Info(tripLine);
            else
                Mod.Log.Warn(tripLine);
            string trips = SkiTripSystems.Short(m_TripFound, m_TripOn, planners);

            SkiDayRules.ArmVerdict verdict = SkiDayRules.ArmCheck(m_LeisureIntervalRaw, m_TripInterval, m_BehaviorInterval, m_BehaviorOffset, m_TripOn);
            bool dayOk = s_GameTicksPerDay == SkiDayRules.TicksPerDay;
            if (verdict != SkiDayRules.ArmVerdict.Ok || !dayOk)
            {
                string why = verdict != SkiDayRules.ArmVerdict.Ok ? verdict.ToString() : "DayLength";
                Mod.Log.Warn(Invariant($"SkiDays: NOT ARMED — {why}: LeisureSystem every {m_LeisureIntervalRaw} frames, TripNeededSystem every ") +
                             Invariant($"{m_TripInterval}, CitizenBehaviorSystem every {m_BehaviorInterval} at offset {m_BehaviorOffset}, day {s_GameTicksPerDay} ") +
                             Invariant($"frames (read from the game's own systems; trip systems {trips}); needs LeisureSystem >= 4 x TripNeededSystem, ") +
                             "CitizenBehaviorSystem slower than 8 frames, the game's own LeisureSystem and CitizenBehaviorSystem switched on and a " +
                             "262144-frame day (D1; the day check trips only on a rebuild against a changed Game.dll); ski days off this load");
                return;
            }

            m_Armed = true;
            Revision++;
            SkiDayRules.Knobs k = ReadKnobs();
            int price = m_Ticket.Price;
            SkiDayRules.TripKnobs tk = m_TripKnobs;
            // Round M2: the merged ARMED line (build-contract.md §6.1: R9 trip systems, R4 b the pull, D3 on-arrival, R10
            // both speeds and the floors, D5 walkers and follows, D4 the remover traces, the measure-core list).
            Mod.Log.Info(
                Invariant($"SkiDays: ARMED — residents at home and hotel tourists (outing used, real hotel) who just decided to go out may get a ski day: ") +
                Invariant($"LeisureSystem's two writes plus the car-reserve queue for a free household car; pass every {m_LeisureInterval} sim frames after ") +
                Invariant($"LeisureSystem, its slice skipped, CitizenBehaviorSystem's slice skipped within 8 frames of its run (TripNeededSystem every {m_TripInterval}; ") +
                Invariant($"intervals read from the game's own systems; trip systems {trips}); ") +
                Invariant($"walk <= {SkiDayRules.WalkMaxM} m, car beyond (not a child), no car far at learned confidence (start {SkiDayRules.PriorNoCar}); ") +
                Invariant($"reach {k.ReachKm} km; trip = km x {SkiDayRules.DetourFactor} / speed (walk {SkiDayRules.StartSpeedWalk}, car {SkiDayRules.StartSpeedCar}, ") +
                Invariant($"no car {SkiDayRules.StartSpeedNoCar} km per game hour, learned) + {tk.StayHours} h stay before deadline and closing; ") +
                Invariant($"claims from {(int)SkiDayRules.ClaimStartHour:D2}:00, lifts {(int)SkiDayRules.LiftsOpenHour:D2}:00-16:30 (21:00 with an open night run), ") +
                "deadline = closing, pulled in when the hill closes early (no counted run, or the night run closed): a guest on site then leaves at the " +
                Invariant($"new closing but at least {SkiDayTiming.HoursFor(SkiDayTiming.OnSiteGraceFrames):F0} h from then, a guest on the way when it arrives; ") +
                "the same Leisure field, lowered only; " +
                // Catalog Phase B (round-2-contract §7.3-§7.4; was "x W x P x confidence", "P at price … = …" and "capacity
                // min(runs, 360 x ski-day lodges)"): the school factor, the lodge price tolerance and the per-tier capacity.
                Invariant($"locals min({SkiDayRules.ResidentCap}, {k.LocalsPct}% x A/100 x D) x W x P x confidence x school ({SchoolRule.DemandFactor} while ") +
                "the ski school is active and a green run counts), tourists " +
                Invariant($"min({SkiDayRules.TouristCap}, {k.TouristsPct}% x A/200 x D) x Wt x P x confidence, D = 1 to 1 km then 0 at reach, P at price ") +
                Invariant($"{price} = {SkiDayRules.PriceFactor(price):F2} (the full-price threshold raised by the highest price tolerance among the lodges that pass: ") +
                TierText(tolerance: true) +
                Invariant($"); powder = clamp01((snow-hours - 4) / 6) over 24 game hours (precipitation >= 0.1, ") +
                Invariant($"top at or below freezing), pull {k.PowderPct}%; capacity min(runs, {SkiDayRules.LodgeSkiersPerDay} x ski-day lodges ") +
                "for A-frames — by tier the lodges' skiers a day: " + TierText(tolerance: false) +
                "; ski-day targets are road lodges, a mountain lodge holds skiers only; no limit on the number of lodges) x " +
                Invariant($"(1 + {SkiDayRules.PowderCapacityBoost} x powder); slope-side <= {SkiDayRules.SlopesideMaxM} m to a run's outline or a lift station; ") +
                Invariant($"back-off per area and class: confidence = (arrived + {SkiDayRules.PriorWeight} x prior) / (arrived + failed + {SkiDayRules.PriorWeight}), ") +
                Invariant($"{SkiDayRules.ConfidenceMin}-1, history x {SkiDayRules.TrackKeep} per outcome; a guest found in the lodge with the outing gone ") +
                "counts as arrived, visit ended \"removed (on arrival)\"; " +
                Invariant($"\"cannot reach\" after {SkiDayRules.CannotReachMinResolved} resolved car ski days below {SkiDayRules.CannotReachBelow}, clears at ") +
                Invariant($"{SkiDayRules.CannotReachClearAt}; speeds learned at rate {SkiDayRules.SpeedLearnRate} between a floor (walk {tk.FloorWalk}, car ") +
                Invariant($"{tk.FloorCar}, no car {tk.FloorNoCar}) and a cap (walk {SkiDayRules.MaxSpeedWalk}, car {SkiDayRules.MaxSpeedCar}, no car ") +
                Invariant($"{SkiDayRules.MaxSpeedNoCar}) from arrivals; a second, all-outcomes speed also learns from late arrivals (exact) and upper bounds ") +
                Invariant($"(an expired claim when its late watch ends, or a claim gone or retargeted on the road without turning back: km x {SkiDayRules.DetourFactor} ") +
                "/ hours from the claim to the last pass it was not there, applied only when lower); " +
                (tk.FitUsesAllOutcomes ? "the fit check uses all outcomes; " : "the fit check uses arrivals only; ") +
                Invariant($"at most {Tuning.ClaimsCap} tracked; in memory, reset on load; ") +
                "measure only (Round M1): failures by place, cause and marker, trip phases, visit end and exit, modes seen, refusals, " +
                Invariant($"distance bands, car reserve, late arrivals after expiry, walkers within {SkiDayTiming.NearLodgeM:F0} m of each lodge every ") +
                Invariant($"{Tuning.NearScanPasses} passes, guests followed up to {SkiDayTiming.HoursFor(SkiDayTiming.FollowWatchFrames):F0} h after leaving for ") +
                Invariant($"home, the map exit or with no trip purpose (at most {Tuning.FollowCap}), guest-ledger check, daily cost at midday, last ") +
                Invariant($"{Tuning.RingSize} ski days in the Dump; why a visit ended before its deadline (the game's traces at the visit end) and where ") +
                "no-trip-purpose exits stood (in those lines \"home\" means home or hotel); Round M2: door wait, arrivals after the hill closed, " +
                "late arrivals by hour, Full in the [area] lines");
            CapLine();   // Round M2 (R12): right after the ARMED line, once per Game load
        }

        /// <summary>Round M2 (R9): the six trip systems' Enabled flags (SkiDayRules.Trip… bits); <paramref name="found"/>
        /// has the bit of every system that exists. ComponentSystemBase.Enabled is a public property that vanilla code
        /// reads on other systems too (Game.Policies/ModifiedSystem.cs:412 (=)); main thread, no allocation.</summary>
        private int ReadTripSystems(out int found)
        {
            found = 0;
            int on = 0;
            TripBit(m_LeisureSys, SkiDayRules.TripLeisure, ref found, ref on);
            TripBit(m_Behavior, SkiDayRules.TripBehavior, ref found, ref on);
            TripBit(m_TripNeededSys, SkiDayRules.TripNeeded, ref found, ref on);
            TripBit(m_ResidentAI, SkiDayRules.TripResidentAI, ref found, ref on);
            TripBit(m_ResidentActions, SkiDayRules.TripResidentActions, ref found, ref on);
            TripBit(m_ResourceBuyer, SkiDayRules.TripResourceBuyer, ref found, ref on);
            return on;
        }

        private static void TripBit(ComponentSystemBase system, int bit, ref int found, ref int on)
        {
            if (system == null)
                return;
            found |= bit;
            if (system.Enabled)
                on |= bit;
        }

        private static bool TripAllOn(int found, int on)
            => (found & SkiDayRules.TripAll) == SkiDayRules.TripAll && (on & SkiDayRules.TripAll) == SkiDayRules.TripAll;

        /// <summary>Round M2 (R9), right after the armed gate of every pass: a change of the trip systems is logged (at
        /// most Tuning.TripChangeLines per load; later ones are counted in the Dump), the hub label follows, and when
        /// LeisureSystem or CitizenBehaviorSystem went off (ArmCheck's SystemOff) ski days stop until the next load:
        /// the pass completes and is applied, the month in progress is written partial, the claims are dropped (they are
        /// vanilla Leisure now and the game owns them) — never the exception path. Why per pass: a mod system created
        /// after ours may switch one off in its own load handler (handlers run in creation order, GameSystemBase.cs:29
        /// (=)), or from its Options; neither known swap does. Returns true when ski days stopped this pass.</summary>
        private bool TripSystemsTick()
        {
            int on = ReadTripSystems(out int found);
            if (on == m_TripOn && found == m_TripFound)
                return false;
            int planners = SkiOtherMods.TripPlannerMask;
            bool disarms = SkiDayRules.ArmCheck(m_LeisureIntervalRaw, m_TripInterval, m_BehaviorInterval, m_BehaviorOffset, on) ==
                           SkiDayRules.ArmVerdict.SystemOff;
            m_TripChanges++;
            if (m_TripChanges <= Tuning.TripChangeLines)
                Mod.Log.Warn(SkiTripSystems.ChangeLine(m_TripFound, m_TripOn, found, on, planners, disarms, m_TripChanges == Tuning.TripChangeLines));
            m_TripFound = found;
            m_TripOn = on;
            m_TripLabel = SkiTripSystems.Label(found, on, planners);
            m_TripBlocks = SkiTripSystems.BlocksSkiDays(on);
            Revision++;
            if (!disarms)
                return false;

            // R9: complete the pass, book it, write the month partial, log the stop, drop the claims, disarm.
            m_PassHandle.Complete();
            if (m_PassInFlight)
                ApplyPass();
            bool monthWritten = m_Month.Key != SkiDayRules.NoMonth;
            if (monthWritten)
                WriteDayLines(m_Month.Key, SinceText() + " (until ski days stopped)");
            // Fixer S3/F2: after m_Armed = false neither LedgerCheckTick nor CostDayEnd runs again, and the preload / quit
            // lines need m_Armed, so a pending ledger check or an unwritten [cost] measurement says so here (silence
            // must be impossible).
            if (m_LedgerCheckDue)
            {
                Mod.Log.Info($"SkiDays [day] {MonthText(m_VisitsClosedKey)} ledger check lost (ski days stopped before it was due; " +
                             $"{m_VisitsClosed.Count} resident arrival(s))");
                m_LedgerCheckDue = false;
            }
            CostStopped();
            FlushLogs(all: true);
            string off = "";
            if ((on & SkiDayRules.TripLeisure) == 0)
                off = SkiTripSystems.SystemName(SkiDayRules.TripLeisure);
            if ((on & SkiDayRules.TripBehavior) == 0)
                off += (off.Length > 0 ? ", " : "") + SkiTripSystems.SystemName(SkiDayRules.TripBehavior);
            string names = SkiTripSystems.Names(planners, true);
            // Fixer F2: on the first pass after a load this runs before AdvanceMonth, so no month has started and nothing
            // was written; the line says which.
            Mod.Log.Warn($"SkiDays: stopped — {off} switched off ({(names.Length > 0 ? names : "no known replacement loaded")}); " +
                         (monthWritten ? "month written partial" : "no month started yet") +
                         $"; {m_Claims.Length} tracked claim(s) dropped (vanilla leisure trips now, the game finishes them)");
            m_Claims.Clear();
            m_PassInFlight = m_CensusInFlight = m_NearInFlight = false;
            m_Armed = false;
            Revision++;
            return true;
        }

        /// <summary>Round M2 (R12, U9): the leisure route cap the game prices a ski day's first route under, once per Game
        /// load right after the ARMED line, from the TripPriorityParametersData singleton through the game's own
        /// GetPriority / GetMaxCost (TripPriorityParametersData.cs:39-81, Leisure :73-77, GetMaxCost :83-86 (=); TripNeededSystem
        /// asks the route with them, TripNeededSystem.cs:1071, :1082 (=)); the retry after a failed route uses
        /// CitizenBehaviorSystem.kMaxPathfindCost (CitizenBehaviorSystem.cs:982, 1.6.2 :1012; ResidentAISystem.cs:3071 (=)).
        /// Silence impossible: one of its lines always prints.</summary>
        private void CapLine()
        {
            try
            {
                if (m_TripPriorityQuery.IsEmptyIgnoreFilter)
                {
                    Mod.Log.Warn("SkiDays: leisure route cap unknown at load (no TripPriorityParametersData); the U9 reading is unchecked this load");
                    return;
                }
                // A resident decides to go out only while its leisure counter is below 128 (the chance is 128 − counter,
                // CitizenBehaviorSystem.cs:459-460, 1.6.2 :460-461), so 128 is the cheapest end for a fresh decision.
                const byte decidedCounter = 128;
                Game.Prefabs.TripPriorityParametersData d = m_TripPriorityQuery.GetSingleton<Game.Prefabs.TripPriorityParametersData>();
                byte p128 = d.GetPriority(Game.Citizens.Purpose.Leisure, new Citizen { m_LeisureCounter = decidedCounter });
                byte p0 = d.GetPriority(Game.Citizens.Purpose.Leisure, new Citizen());   // counter 0: the top of the range
                float capMin = d.GetMaxCost(d.m_LeisurePriorityMin), capMax = d.GetMaxCost(p0), cap128 = d.GetMaxCost(p128);
                float work = d.GetMaxCost(d.GetPriority(Game.Citizens.Purpose.GoingToWork, default));
                float shop = d.GetMaxCost(d.GetPriority(Game.Citizens.Purpose.Shopping, default));
                float home = d.GetMaxCost(d.GetPriority(Game.Citizens.Purpose.GoingHome, default));
                string source = m_TripLabel.Length == 0
                    ? "base game"
                    : "trip systems " + SkiTripSystems.Short(m_TripFound, m_TripOn, SkiOtherMods.TripPlannerMask) + ": the replacement may price routes its own way";
                Mod.Log.Info(Invariant($"SkiDays: leisure route cap ({source}, TripPriorityParameters read at load): {d.m_BaseMaxCost:F0} x priority / 128; ") +
                             Invariant($"leisure priority {d.m_LeisurePriorityMin}-{d.m_LeisurePriorityMax} by the leisure counter = {capMin:F0}-{capMax:F0}, ") +
                             Invariant($"{cap128:F0}-{capMax:F0} for a citizen who just decided to go out (counter {decidedCounter} or less); work {work:F0}, shopping {shop:F0}, ") +
                             Invariant($"going home {home:F0}; the game's retry after a failed route uses {CitizenBehaviorSystem.kMaxPathfindCost:F0} ") +
                             "(CitizenBehaviorSystem); a ski day's first route is priced under the leisure cap (U9)");
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"SkiDays: leisure route cap could not be read ({e.GetType().Name}: {e.Message}); the U9 reading is unchecked this load");
            }
        }

        // ── the pass (ch2 §3) ─────────────────────────────────────────────────────────────────────

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !m_Armed)
                    return;
                if (TripSystemsTick())
                    return;                         // Round M2 (R9): a trip system went off and ski days stopped (logged)
                long t0 = Stopwatch.GetTimestamp();

                m_PassHandle.Complete();            // 1. scheduled one interval ago: already done
                if (m_PassInFlight)
                    ApplyPass();                    // 2.
                AdvanceMonth();                     // 3.
                LedgerCheckTick();                  // 3b. Round M1 M7 (a2): once, 4,096 frames after a roll
                CostDayEnd();                       // 3c. Round M2 (D10): a midday measurement cut by the day change, partial
                if (m_CensusInFlight)
                    ApplyCensus();                  // 4.
                if (m_NearInFlight)
                    ApplyNear();                    // 4b. Round M2 (D5): the walker scan
                ClimateTick();                      // 5. (an hour change also sets m_CensusDue and m_NearDue, and m_HourStep)
                NearHourTick();                     // 5c. Round M2 (D5): the hourly `[near]` lines on an hour step
                if (!m_Attraction.HasSnapshot && m_Claims.Length == 0)
                {
                    // 5b. After a load the attraction snapshot is empty until its first recompute (≤ 16 frames): not
                    // "no ski area", so no `[idle]` line yet (fixer: review CP-4's mechanism, tests-logs §7).
                    FlushLogs(all: false);
                    return;
                }
                MaybeRebuildSnapshot();             // 6.
                PowderYearTick();                   // 6b. Round M1 M9: the year's powder hours, at load and each year roll

                bool work = m_Areas.Length > 0;
                if (!work)
                {
                    SetIdle();                      // 7. no ski area: claims in flight still resolve (as orphans)
                    if (m_Claims.Length == 0)
                    {
                        FlushLogs(all: false);
                        return;
                    }
                }
                else
                {
                    ClearIdle();
                }
                MaybeRearmCost();                   // Round M1 M8: daily at midday (this pass is then left out)
                bool timed = work && CostTimedDue();
                if (timed)
                    m_CensusDue = m_NearDue = true; // the timed pass carries every worker job (fixer, JP-1; Round M2 the scan too)
                double jobsMs = Schedule(work, timed);   // 8.

                double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                RecordCost(ms, work, timed, jobsMs);     // 9.
                FlushLogs(all: false);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                try
                {
                    m_PassHandle.Complete();
                }
                catch (Exception)
                {
                    // the first exception is the one worth reporting
                }
                m_PassInFlight = m_CensusInFlight = m_NearInFlight = false;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiDaySystem)} EXCEPTION — ski days disabled until the next game load (ski days " +
                                  $"already given finish as vanilla trips). Please report this stack:\n{e}");
                }
            }
        }

        // ── 2. apply (ch2 §6.3) ───────────────────────────────────────────────────────────────────

        /// <summary>Main thread, after Complete: books last pass's ClaimOut / ResolveOut into the month and the
        /// load totals, copies tracks back (car: the "cannot reach" hysteresis), copies every DUMP value into
        /// managed state (ch2-RF2-5), writes the first / reach / area / lodges lines and publishes the views.
        /// The snapshot is still the one the pass ran with (rebuilds come after this, step 6).</summary>
        private void ApplyPass()
        {
            m_PassInFlight = false;
            m_PassesLoad++;
            for (int i = 0; i < Stat.Count && i < m_PassStats.Length; i++)
                m_LastPassStats[i] = m_PassStats[i];
            m_LastPassFrame = m_PassFrame;
            m_LastPassKnown = true;
            if (m_LastPassStats[Stat.WalkRan] != 0)
            {
                m_WalkPassesLoad++;
                m_CityThis.WalkPasses++;
                m_CityThis.Walked += m_LastPassStats[Stat.Walked];
            }
            m_CityThis.FreshLocals += m_LastPassStats[Stat.FreshLocals];
            m_CityThis.FreshTourists += m_LastPassStats[Stat.FreshTourists];
            m_CityThis.FreshWithCar += m_LastPassStats[Stat.FreshWithCar];
            m_CityThis.ShoppingNeed += m_LastPassStats[Stat.ShoppingNeed];
            if (m_LastPassStats[Stat.CapReached] != 0 && !m_CapWarned)
            {
                m_CapWarned = true;
                Mod.Log.Warn($"SkiDays: {m_Claims.Length} ski days tracked — at the cap of {Tuning.ClaimsCap}; new ski days paused until " +
                             "some end (warned once per load)");
            }
            m_TrackedLast = m_Claims.Length;
            CopyRing();            // Round M1 M5
            TakeResidentNotes();   // Round M1 M7 (a2)

            int areas = m_Areas.Length;
            int onTheWay = 0, onSite = 0, leaving = 0, watching = 0, following = 0, followSkipped = 0, sent = 0;
            if (m_ResolveOut.Length > areas)
            {
                ResolveOut orphan = m_ResolveOut[areas];
                AddLoad(orphan);
                m_LoadOrphaned += orphan.ArrivedWalk + orphan.ArrivedCar + orphan.ArrivedNoCar + orphan.Gone + orphan.Retargeted +
                                  orphan.Expired + orphan.Left;
                onTheWay += orphan.OnTheWay;
                onSite += orphan.OnSite;
                leaving += orphan.Leaving;     // the orphan row's measure rows belong to no area (its ring records still print)
                watching += orphan.Watching;
                following += orphan.Following; // Round M2 (D5): orphans' follows count toward the city-wide cap too
                followSkipped += orphan.FollowSkipped;
            }
            // Round M2 (D12a, R4): the clock of this apply (64 frames after the pass ran: under half a game minute) for the
            // month's first-Full and closing minutes, the `[area]` Full line and the `[closing]` line.
            int minute = SkiDayTiming.MinuteOfDay(m_Time.normalizedTime);
            uint nowFrame = m_Simulation.frameIndex;

            // Per-lodge copies (DUMP) and the load's car failures per lodge.
            m_DumpLodges.Clear();
            for (int li = 0; li < m_Lodges.Length; li++)
            {
                LodgeJob lj = m_Lodges[li];
                int fails = li < m_LodgeCarFails.Length ? m_LodgeCarFails[li] : 0;
                if (fails > 0)
                    m_CarFailsByLodge[lj.Lodge] = (m_CarFailsByLodge.TryGetValue(lj.Lodge, out int n) ? n : 0) + fails;
                SiteOut so = li < m_SiteOut.Length ? m_SiteOut[li] : new SiteOut { NearestRunOrStationM = float.PositiveInfinity };
                LodgeParking p = default;
                bool parked = m_Parking != null && m_Parking.TryGetParking(lj.Lodge, out p);   // managed data, no sync (ch3 §3.5)
                m_DumpLodges.Add(new DumpLodge
                {
                    Lodge = lj.Lodge,
                    Area = lj.Area,
                    Reject = li < m_LodgeReject.Length ? m_LodgeReject[li] : (byte)0,
                    HasPosition = so.HasPosition != 0,
                    Xz = so.Xz,
                    NearestM = so.NearestRunOrStationM,
                    ParkingKnown = parked,
                    ParkingDone = parked && p.Done,
                    CarSpaces = parked ? p.CarSpaces : -1,
                    LodgeIndex = lj.LodgeIndex,          // Catalog Phase B
                    Capacity = lj.Capacity,
                    NoRoadNeeded = lj.NoRoadNeeded != 0,
                });
            }

            m_DumpSigns.Clear();
            bool knobsOn = m_Knobs.LocalsPct > 0 || m_Knobs.TouristsPct > 0;
            bool changed = false;
            for (int a = 0; a < areas; a++)
            {
                AreaJob aj = m_Areas[a];
                Entity sign = aj.Sign;
                m_DumpSigns.Add(sign);
                AreaMemory mem = MemoryOf(sign);
                ClaimOut co = a < m_ClaimOut.Length ? m_ClaimOut[a] : default;
                ResolveOut ro = a < m_ResolveOut.Length ? m_ResolveOut[a] : default;
                AreaInfo info = a < m_AreaInfo.Count ? m_AreaInfo[a] : default;

                // Round M2 (D12a, ruling R4 a; Decided here 1-2): the pass's arrivals count as "after the hill closed" when
                // the snapshot it ran with had a known, released score and no counted run (D3's on-arrival bookings are
                // arrivals too); a closure edge (1 no counted run, 2 the night run closed) needs the hill (night run) seen
                // open since the load, and the "seen" flags move only on a known, released score (a load hold with 0
                // runs is never an edge).
                bool scoreKnown = aj.ScoreKnown != 0, held = aj.WriteHeld != 0, nightOpen = aj.NightRunOpen != 0;
                bool closed = SkiDayRules.HillClosedForCount(scoreKnown, held, aj.RunsCounted);
                byte edge = SkiDayRules.ClosingEdgeOf(mem.HillOpenSeen, mem.NightOpenSeen, scoreKnown, held, aj.RunsCounted, nightOpen);
                if (scoreKnown && !held)
                {
                    mem.HillOpenSeen = aj.RunsCounted > 0;
                    mem.NightOpenSeen = aj.RunsCounted > 0 && nightOpen;
                }
                mem.This.Add(co, ro, aj.Powder, info.SnowHours, minute, closed, edge == 1);
                AddMeasure(mem, a);   // Round M1: this pass's measure rows into the month
                AddLoad(ro);
                m_LoadSent += co.Sent;
                m_LoadCars += co.CarsEnqueued;
                onTheWay += ro.OnTheWay;
                onSite += ro.OnSite;
                leaving += ro.Leaving;
                watching += ro.Watching;
                following += ro.Following;
                followSkipped += ro.FollowSkipped;
                sent += co.Sent;

                // Tracks back into memory; the car track's "cannot reach" (D15, hub row 20) written to both.
                bool wasCannot = mem.Tracks[(int)SkiDayRules.TravelClass.Car].CannotReachShown;
                for (int c = 0; c < 3; c++)
                {
                    int ti = SkiDayTiming.TrackIndex(a, (byte)c);
                    if (ti >= 0 && ti < m_Tracks.Length)
                        mem.Tracks[c] = m_Tracks[ti];
                }
                SkiDayRules.ClassTrack car = mem.Tracks[(int)SkiDayRules.TravelClass.Car];
                bool cannot = SkiDayRules.UpdateCannotReach(ref car);
                mem.Tracks[(int)SkiDayRules.TravelClass.Car] = car;
                int carTi = SkiDayTiming.TrackIndex(a, (byte)SkiDayRules.TravelClass.Car);
                if (carTi >= 0 && carTi < m_Tracks.Length)
                    m_Tracks[carTi] = car;

                // Last-pass copies (DUMP).
                mem.HasPass = true;
                mem.State = co.State;
                mem.Limit = co.Limit;
                mem.ClaimHours = co.ClaimHours != 0;
                mem.NightRunOpen = aj.NightRunOpen != 0;
                mem.Capacity = co.CapacityPeople;
                mem.TargetLodges = co.TargetLodges;
                mem.LodgeCapacity = co.LodgeCapacity;   // Catalog Phase B
                mem.PriceTolerance = co.PriceTolerance;
                mem.SchoolFactor = aj.SchoolFactor;
                mem.Attraction = aj.Attraction;
                mem.OpenShare = aj.OpenShare;
                mem.Powder = aj.Powder;
                mem.RunsCapacity = aj.RunsCapacity;
                mem.SnowHours = info.SnowHours;
                mem.TopKnown = info.TopKnown;
                mem.TopM = info.TopM;
                mem.OnTheWay = ro.OnTheWay;
                mem.OnSite = ro.OnSite;
                mem.NotLeft = ro.NotLeft;
                mem.Riding = ro.Riding;
                mem.Leaving = ro.Leaving;
                mem.Watching = ro.Watching;
                mem.AtDoor = ro.AtDoor;           // Round M2 (D1)
                mem.Following = ro.Following;     // Round M2 (D5)
                mem.GuestsThisHour |= ro.OnSite + ro.Following > 0;   // the hourly `[near]` line (D5)

                // Lodge-derived view fields (rows 21, 22; ReachLodge).
                int notSlopeside = 0, noParking = 0, reachFails = 0;
                Entity firstNotSlopeside = Entity.Null, firstNoParking = Entity.Null, reachLodge = Entity.Null;
                int lodgeEnd = Math.Min(aj.LodgeStart + aj.LodgeCount, m_DumpLodges.Count);
                for (int li = Math.Max(0, aj.LodgeStart); li < lodgeEnd; li++)
                {
                    DumpLodge dl = m_DumpLodges[li];
                    if (dl.Reject == (byte)SkiDayRules.LodgeReject.NotSlopeside)
                    {
                        notSlopeside++;
                        firstNotSlopeside = Lower(firstNotSlopeside, dl.Lodge);
                    }
                    if (dl.Reject == (byte)SkiDayRules.LodgeReject.None && dl.ParkingKnown &&
                        SkiParkingRules.LodgeLacksParking(knobsOn, true, dl.ParkingDone, dl.CarSpaces))
                    {
                        noParking++;
                        firstNoParking = Lower(firstNoParking, dl.Lodge);
                    }
                    if (m_CarFailsByLodge.TryGetValue(dl.Lodge, out int f) && f > 0 &&
                        (f > reachFails || (f == reachFails && Lower(reachLodge, dl.Lodge) == dl.Lodge)))
                    {
                        reachFails = f;
                        reachLodge = dl.Lodge;
                    }
                }

                FirstLines(mem, sign, co, ro);
                if (cannot && !wasCannot)
                    ReachLine(mem, sign, reachLodge, on: true);
                else if (!cannot && wasCannot)
                    ReachLine(mem, sign, reachLodge, on: false);   // Round M1 M6 (D2): the clear is logged too
                StateLine(mem, sign, (SkiDayRules.AreaState)co.State, ro.OnTheWay + ro.OnSite, co.CapacityPeople, (SkiDayLimit)co.Limit, minute);
                if (edge != 0)
                    ClosingLine(sign, edge, ro, minute, nowFrame);   // Round M2 (R4 b): once per closure edge per area
                LodgesLine(mem, sign, aj);

                var view = new SkiDayAreaView
                {
                    TargetLodges = co.TargetLodges,
                    LodgesNotSlopeside = notSlopeside,
                    FirstLodgeNotSlopeside = firstNotSlopeside,
                    ClaimHours = co.ClaimHours != 0,
                    OnTheWayOrOnSite = ro.OnTheWay + ro.OnSite,
                    Capacity = co.CapacityPeople,
                    Limit = (SkiDayLimit)co.Limit,
                    Powder = (float)aj.Powder,
                    CannotReach = cannot,
                    ReachLodge = reachLodge,
                    LodgesNoParking = noParking,
                    FirstLodgeNoParking = firstNoParking,
                };
                SetMonthFields(ref view, mem);
                if (!m_Views.TryGetValue(sign, out SkiDayAreaView old) || !view.Same(old))
                {
                    m_Views[sign] = view;
                    changed = true;
                }
            }
            m_OnTheWayLast = onTheWay;
            m_OnSiteLast = onSite;
            m_LeavingLast = leaving;
            m_WatchingLast = watching;
            m_FollowingLast = following;   // Round M2 (D5): the next ResolveJob's FollowingLast (the cap's input)
            m_SentLast = sent;             // Round M2 (D9): the DUMP's "sent this pass" (tracked = the exact bucket sum)
            if (followSkipped > 0 && !m_FollowWarned)
            {
                m_FollowWarned = true;
                Mod.Log.Warn($"SkiDays: {Tuning.FollowCap} guests already followed after leaving the lodge — more are booked \"not followed\" " +
                             "until some finish (measure only; warned once per load)");
            }
            if (changed)
                Revision++;
        }

        /// <summary>Round M1 (m1-spec §1.3): adds area <paramref name="a"/>'s rows of this pass's three measure arrays
        /// into the month (bounds-checked; the arrays were sized for the snapshot this pass ran with).</summary>
        private void AddMeasure(AreaMemory mem, int a)
        {
            NativeArray<int> rm = m_ResolveMeasure.AsArray();
            int b = a * RM.Count;
            for (int k = 0; k < RM.Count && b + k < rm.Length; k++)
                mem.MThis[k] += rm[b + k];
            NativeArray<double> rh = m_ResolveHours.AsArray();
            b = a * RH.Count;
            for (int k = 0; k < RH.Count && b + k < rh.Length; k++)
                mem.HThis[k] += rh[b + k];
            NativeArray<int> cm = m_ClaimMeasure.AsArray();
            b = a * CM.Count;
            for (int k = 0; k < CM.Count && b + k < cm.Length; k++)
                mem.CThis[k] += cm[b + k];
        }

        /// <summary>Round M1 M5: the ring into its managed copy, only when a record was written since the last copy.</summary>
        private void CopyRing()
        {
            if (m_RingState.Length < 2 || m_RingState[1] == m_RingCopyTotal)
                return;
            int n = Math.Min(m_Ring.Length, m_RingCopy.Length);
            for (int i = 0; i < n; i++)
                m_RingCopy[i] = m_Ring[i];
            m_RingCopyNext = m_RingState[0];
            m_RingCopyTotal = m_RingState[1];
        }

        /// <summary>Round M1 M7 (a2): this pass's resident notes into the visit lists. An arrival adds a visit; a visit
        /// end sets the guest-scan verdict of the visit with the same citizen and arrival frame (searched from the end
        /// of the month in progress, then of the closed month).</summary>
        private void TakeResidentNotes()
        {
            for (int i = 0; i < m_ResidentNotes.Length; i++)
            {
                ResidentNote n = m_ResidentNotes[i];
                if (n.Kind == 0)
                    m_VisitsThis.Add(new ResidentVisit { Citizen = n.Citizen, ArrivalFrame = n.ArrivalFrame, Verdict = 255 });
                else if (!SetVerdict(m_VisitsThis, n))
                    SetVerdict(m_VisitsClosed, n);
            }
        }

        private static bool SetVerdict(List<ResidentVisit> visits, in ResidentNote n)
        {
            for (int i = visits.Count - 1; i >= 0; i--)
            {
                ResidentVisit v = visits[i];
                if (v.Citizen != n.Citizen || v.ArrivalFrame != n.ArrivalFrame)
                    continue;
                v.Verdict = n.Verdict;
                visits[i] = v;
                return true;
            }
            return false;
        }

        private void AddLoad(in ResolveOut ro)
        {
            m_LoadArrived += ro.ArrivedWalk + ro.ArrivedCar + ro.ArrivedNoCar;
            m_LoadGone += ro.Gone;
            m_LoadRetargeted += ro.Retargeted;
            m_LoadRetargetedAtOrigin += ro.RetargetedAtOrigin;
            m_LoadExpired += ro.Expired;
            m_LoadLeft += ro.Left;
            m_LoadArrivedNoLeisure += ro.ArrivedNoLeisure;
        }

        private void SetMonthFields(ref SkiDayAreaView view, AreaMemory mem)
        {
            view.SentThisMonth = mem.This.Sent;
            view.SentCarThisMonth = mem.This.SentCar;
            view.SentNoCarThisMonth = mem.This.SentNoCar;
            view.ArrivedThisMonth = mem.This.Arrived;
            view.ArrivedLastMonth = m_Month.LastKnown ? mem.Last.Arrived : -1;
        }

        /// <summary>After a roll or a jump the month fields of the views move at once (not one pass later).</summary>
        private void RefreshViewMonths()
        {
            bool changed = false;
            for (int i = 0; i < m_DumpSigns.Count; i++)
            {
                Entity sign = m_DumpSigns[i];
                if (!m_Views.TryGetValue(sign, out SkiDayAreaView view) || !m_State.TryGetValue(sign, out AreaMemory mem))
                    continue;
                SkiDayAreaView next = view;
                SetMonthFields(ref next, mem);
                if (!next.Same(view))
                {
                    m_Views[sign] = next;
                    changed = true;
                }
            }
            if (changed)
                Revision++;
        }

        // ── 3. month (ch2 §6.4) ───────────────────────────────────────────────────────────────────

        private void AdvanceMonth()
        {
            int key = SkiMoneyRules.MonthKey(m_Time.year, m_Time.normalizedDate);
            int prev = m_Month.Key;
            bool jump = prev != SkiDayRules.NoMonth && key != prev && !(prev != int.MaxValue && key == prev + 1);
            if (jump)
                WriteDayLines(prev, SinceText() + " (until the jump)");
            // Round M1 M7 (c), D5: frames do not jump with the clock, so a month that rolls after far fewer frames than a
            // month holds (a developer time jump that landed exactly one month later) closes partial (95 % rule,
            // SkiMoneyRules.IsShortMonth; the guest counter applies the same rule to the ledger).
            uint now = m_Simulation.frameIndex;
            int days = m_Time.daysPerYear;
            string closedSince = SinceText();   // read before the step moves m_Month and m_MonthByJump
            bool closedSinceLoad = m_Month.ThisSinceLoad;
            uint simulated = now - m_MonthStartFrame;
            bool closedShort = m_HasMonthStart && SkiMoneyRules.IsShortMonth(simulated, days);
            SkiDayRules.MonthStep step = SkiDayRules.Advance(ref m_Month, key, closedShort);
            if (step != SkiDayRules.MonthStep.None)
            {
                m_MonthStartFrame = now;
                m_HasMonthStart = true;
                m_MonthByJump = step == SkiDayRules.MonthStep.Jumped;   // fixer R8
            }
            switch (step)
            {
                case SkiDayRules.MonthStep.Rolled:
                {
                    string label = closedSinceLoad ? closedSince
                        : closedShort ? Invariant($" (partial: {SkiMoneyRules.SimulatedPct(simulated, days)} % of a month simulated)") : "";
                    WriteDayLines(prev, label);   // the closed month is still in This
                    DriftCheck(prev);
                    foreach (AreaMemory mem in m_State.Values)
                    {
                        mem.Last = mem.This;
                        mem.This = SkiDayMonth.Empty();
                        mem.RollMeasure();
                    }
                    m_CityThis = default;
                    RollVisits(prev, now);
                    DropAbsentAreas();
                    RefreshViewMonths();
                    break;
                }
                case SkiDayRules.MonthStep.Jumped:
                    Mod.Log.Info($"SkiDays [day] the calendar jumped from {MonthText(prev)} to {MonthText(key)} (time changed) — " +
                                 "last month unknown; counting restarts");
                    foreach (AreaMemory mem in m_State.Values)
                    {
                        mem.This = SkiDayMonth.Empty();
                        mem.Last = SkiDayMonth.Empty();
                        mem.ClearMeasure();
                    }
                    m_CityThis = default;
                    // Fixer L4: name the closed month only when its check had not run yet, and the month in progress
                    // (prev) whenever it had resident arrivals: both lists are dropped here.
                    if (m_LedgerCheckDue)
                        Mod.Log.Info($"SkiDays [day] {MonthText(m_VisitsClosedKey)} ledger check skipped (time jumped before it ran; " +
                                     $"{m_VisitsClosed.Count} resident arrival(s))");
                    if (m_VisitsThis.Count > 0)
                        Mod.Log.Info($"SkiDays [day] {MonthText(prev)} ledger check not possible (time jumped; {m_VisitsThis.Count} resident arrival(s))");
                    m_VisitsThis.Clear();
                    m_VisitsClosed.Clear();
                    m_VisitsClosedKey = SkiDayRules.NoMonth;
                    m_LedgerCheckDue = false;
                    RefreshViewMonths();
                    break;
            }
        }

        /// <summary>The month in progress's start label: "" (a full month so far), " (since load)", or " (since the time
        /// jump)" when counting restarted at a calendar jump (fixer R8: no load happened then).</summary>
        private string SinceText()
        {
            if (!m_Month.ThisSinceLoad)
                return "";
            return m_MonthByJump ? " (since the time jump)" : " (since load)";
        }

        // ── 3b. the ledger check (Round M1 M7 a2; F9) ─────────────────────────────────────────────

        /// <summary>At a roll: a closed month still waiting for its check is checked now; then the month in progress
        /// becomes the closed one (its check is due Tuning.LedgerCheckDelayFrames later) and a new list starts.</summary>
        private void RollVisits(int closedKey, uint now)
        {
            if (m_LedgerCheckDue)
                RunLedgerCheck();
            List<ResidentVisit> swap = m_VisitsClosed;
            m_VisitsClosed = m_VisitsThis;
            m_VisitsThis = swap;
            m_VisitsThis.Clear();
            m_VisitsClosedKey = closedKey;
            m_VisitsClosedAt = now;
            m_LedgerCheckDue = true;
        }

        /// <summary>Every pass: the check is due 4,096 frames after the roll (past the counter's scan, its apply and its
        /// "last 16 frames of a month booked into the next" rule). A check due at a load is lost (memory only).</summary>
        private void LedgerCheckTick()
        {
            if (m_LedgerCheckDue && m_Simulation.frameIndex - m_VisitsClosedAt >= Tuning.LedgerCheckDelayFrames)
                RunLedgerCheck();
        }

        /// <summary>M7 (a2), the direct check of U5 (December: 23 resident ski-day arrivals, 18 residents in the guest
        /// ledger): the resident ski-day arrivals of the closed month are matched, PER RESIDENT, against the number of
        /// times the guest counter booked that resident in that month and the next (TryResidentVisits; fixer R2 / L6:
        /// the ledger counts every visit, so a set would call both visits of a resident "counted" when the counter
        /// booked one). A resident's uncounted visits are taken as the likeliest ones by the guest-scan timing verdict
        /// (missed first, then not over, unsure, seen; M7a: the explanation, not the check). The counter's count may
        /// include a vanilla leisure visit to the lodge, which can hide one missed ski-day visit of the same resident
        /// (accepted). City-wide, one line per month.</summary>
        private void RunLedgerCheck()
        {
            m_LedgerCheckDue = false;
            int key = m_VisitsClosedKey;
            string month = MonthText(key);
            bool armed = m_Counter != null && m_Counter.IsArmed;
            bool held = armed && (m_Counter.TryResidentVisits(Entity.Null, key, out int _) || m_Counter.TryResidentVisits(Entity.Null, key + 1, out int _));
            m_LedgerTally.Clear();
            for (int i = 0; i < m_VisitsClosed.Count; i++)
            {
                ResidentVisit v = m_VisitsClosed[i];
                m_LedgerTally.TryGetValue(v.Citizen, out VisitTally t);
                t.Visits++;
                if (v.Verdict == (byte)SkiDayTiming.ScanVerdict.Missed)
                    t.Missed++;
                else if (v.Verdict == (byte)SkiDayTiming.ScanVerdict.Unsure)
                    t.Unsure++;
                else if (v.Verdict == (byte)SkiDayTiming.ScanVerdict.Seen)
                    t.Seen++;
                else
                    t.NotOver++;
                m_LedgerTally[v.Citizen] = t;
            }
            int inMonth = 0, inNext = 0, missed = 0, unsure = 0, seen = 0, notOver = 0, repeat = 0;
            foreach (KeyValuePair<Entity, VisitTally> pair in m_LedgerTally)
            {
                VisitTally t = pair.Value;
                if (t.Visits > 1)
                    repeat++;
                if (!held)
                    continue;
                m_Counter.TryResidentVisits(pair.Key, key, out int k0);
                m_Counter.TryResidentVisits(pair.Key, key + 1, out int k1);
                int c0 = Math.Min(t.Visits, k0), c1 = Math.Min(t.Visits - c0, k1);
                inMonth += c0;
                inNext += c1;
                int left = t.Visits - c0 - c1, take;
                take = Math.Min(left, t.Missed);
                missed += take;
                left -= take;
                take = Math.Min(left, t.NotOver);
                notOver += take;
                left -= take;
                take = Math.Min(left, t.Unsure);
                unsure += take;
                seen += left - take;
            }
            string who = Invariant($"{m_VisitsClosed.Count} by {m_LedgerTally.Count} resident(s) ({repeat} came more than once)");
            string body = !held
                ? Invariant($"ledger check not possible (guest counter not armed or its months moved on; resident arrivals {who})")
                : Invariant($"ledger check (resident ski-day arrivals of the month against the guest ledger's residents, matched per resident): arrivals {who}; ") +
                  Invariant($"counted {inMonth + inNext} (in that month {inMonth}, booked into the next {inNext}); not counted {missed + unsure + seen + notOver} ") +
                  Invariant($"(guest-scan timing: missed {missed}, unsure {unsure}, seen {seen}, visit not over at the check {notOver})");
            m_LastLedgerCheck = month + " " + body;
            Mod.Log.Info($"SkiDays [day] {month} {body}");
        }

        /// <summary>L12: no fresh leisure decision at home in a whole month although citizens with Leisure were
        /// walked (the vanilla candidate filter may have changed). Once per load.</summary>
        private void DriftCheck(int key)
        {
            if (m_DriftWarned || m_CityThis.WalkPasses < Tuning.DriftMinWalkPasses || m_CityThis.Walked <= 0 || m_CityThis.FreshLocals != 0)
                return;
            m_DriftWarned = true;
            Mod.Log.Warn($"SkiDays [drift] no fresh leisure decision at home in {MonthText(key)} although {m_CityThis.Walked} citizen(s) " +
                         $"with Leisure were walked over {m_CityThis.WalkPasses} pass(es) — the vanilla candidate filter may have changed " +
                         "(patch?); ski days found no one to send (warned once per load)");
        }

        /// <summary>At a roll: forget the areas whose sign is no longer in the snapshot (ch2 §6.4).</summary>
        private void DropAbsentAreas()
        {
            m_PruneScratch.Clear();
            foreach (Entity sign in m_State.Keys)
            {
                if (!m_SnapSigns.Contains(sign))
                    m_PruneScratch.Add(sign);
            }
            for (int i = 0; i < m_PruneScratch.Count; i++)
                m_State.Remove(m_PruneScratch[i]);
        }

        /// <summary>L10: one line per ski area with memory, then the total, for the month in This (at a roll the
        /// closed month, written before the shift; before a jump or a load the month in progress). <paramref name="label"/>
        /// follows the month: " (since load)", " (partial: N % of a month simulated)" (Round M1 M7c), and for a month in
        /// progress " (until the jump)" / " (until the load)"; fixer R8: " (since the time jump)" for a month that started
        /// at a calendar jump. Round M1: each area's line is followed by its measure lines (same tag; no per-pass line;
        /// fixer L7: up to three, "(measure: failures)", "(measure: trips and stays)", "(measure: modes, scan, powder)",
        /// or one "(measure): nothing measured").</summary>
        private void WriteDayLines(int key, string label)
        {
            string month = MonthText(key) + label;
            int sent = 0, arrived = 0, failed = 0;
            foreach (KeyValuePair<Entity, AreaMemory> pair in m_State)
            {
                AreaMemory mem = pair.Value;
                SkiDayMonth m = mem.This;
                sent += m.Sent;
                arrived += m.Arrived;
                failed += m.Gone + m.Retargeted + m.Expired;
                Mod.Log.Info($"SkiDays [day] {month} ski area {Fmt(pair.Key)}{NameSuffix(pair.Key)}: {DayBody(mem, m, m_TripKnobs)}");
                string[] parts = MeasureParts(mem, m, mem.MThis, mem.HThis, mem.CThis, m_LeisureInterval, m_TripKnobs);
                string head = $"SkiDays [day] {month} ski area {Fmt(pair.Key)}{NameSuffix(pair.Key)} (measure";
                if (parts == null)
                {
                    Mod.Log.Info(head + "): nothing measured");
                    continue;
                }
                for (int p = 0; p < parts.Length; p++)
                {
                    if (parts[p].Length > 0)
                        Mod.Log.Info(head + ": " + s_MeasurePartWords[p] + "): " + parts[p]);   // fixer L7: up to three lines
                }
            }
            SkiDayCityMonth city = m_CityThis;
            Mod.Log.Info($"SkiDays [day] {month} all ski areas: sent {sent}, arrived {arrived}, failed {failed}; fresh decisions " +
                         $"{city.FreshLocals + city.FreshTourists} (locals at home {city.FreshLocals}, tourists in a hotel {city.FreshTourists}; " +
                         $"{city.FreshWithCar} with a car; {city.ShoppingNeed} waiting to shop); citizens with Leisure walked {city.Walked} over " +
                         $"{city.WalkPasses} pass(es); {CensusText()}; " + CityCostText(city));
        }

        /// <summary>M8 running cost of the month (= one game day): every pass with work but the timed one; Round M2 (D8):
        /// the clock of the maximum.</summary>
        private static string CityCostText(in SkiDayCityMonth city)
        {
            return city.CostPasses > 0
                ? Invariant($"pass cost mean {city.CostSumMs / city.CostPasses:F3} ms, max {city.CostMaxMs:F3} ms at {ClockText(city.CostMaxMinute)} over ") +
                  Invariant($"{city.CostPasses} passes with work")
                : "pass cost: no pass with work";
        }

        /// <summary>One area's `[day]` line body. Round M2: the highest and the last capacity and the first Full clock
        /// (D11), "arrived after the hill closed" with its closings (D12a, always printed), "deadlines pulled in" (R4 b,
        /// always printed), and both car speeds, the fit check's first (R10; <paramref name="k"/> says which one it
        /// reads).</summary>
        private static string DayBody(AreaMemory mem, in SkiDayMonth m, in SkiDayRules.TripKnobs k)
        {
            SkiDayRules.ClassTrack car = mem.Tracks[(int)SkiDayRules.TravelClass.Car];
            double fit = SkiDayRules.FitSpeed(car, k);
            string speeds = k.FitUsesAllOutcomes
                ? Invariant($"car speed {fit:F2} km per game hour (arrivals, late arrivals and upper bounds, used by the fit check; arrivals only ") +
                  Invariant($"{car.SpeedKmPerHour:F2})")
                : Invariant($"car speed {fit:F2} km per game hour (arrivals only, used by the fit check; with late arrivals and upper bounds ") +
                  Invariant($"{car.SpeedAllKmPerHour:F2})");
            return Invariant($"pool {m.Pool} (locals {m.Pool - m.PoolTourists}, tourists {m.PoolTourists}); sent {m.Sent} (walk {m.SentWalk}, car {m.SentCar}, ") +
                   Invariant($"no car {m.SentNoCar}; tourists {m.SentTourists}); arrived {m.Arrived} (tourists {m.ArrivedTourists}); failed ") +
                   Invariant($"{m.Gone + m.Retargeted + m.Expired} (gone {m.Gone}, retargeted {m.Retargeted}, expired {m.Expired}); mean trip h walk ") +
                   Mean(m.HoursWalk, m.ArrivedWalk) + " car " + Mean(m.HoursCar, m.ArrivedCar) + " no car " + Mean(m.HoursNoCar, m.ArrivedNoCar) +
                   Invariant($"; capacity {m.CapacityMax} at most ({(SkiDayLimit)m.LimitAtMax}), {m.CapacityLast} at the last pass ({(SkiDayLimit)m.LimitLast}), ") +
                   Invariant($"peak on the hill {m.PeakActive}, full in {m.FullPasses} of {m.Passes} pass(es)") +
                   (m.FirstFullMinute >= 0 ? " (first at " + ClockText(m.FirstFullMinute) + ")" : "") +
                   Invariant($"; arrived after the hill closed {m.ArrivedHillClosed}") +
                   (m.HillClosings > 0
                       ? Invariant($" (closed {m.HillClosings} time(s), the last at {ClockText(m.LastCloseMinute)} with {m.OnTheWayAtClose} on the way and ") +
                         Invariant($"{m.OnSiteAtClose} on site)")
                       : "") +
                   Invariant($"; deadlines pulled in {m.Pulled}; powder ") +
                   (double.IsNaN(m.PowderMax) ? "-" : m.PowderMax.ToString("F2", CultureInfo.InvariantCulture)) +
                   Invariant($" at most ({m.SnowHoursAtMax} snow-hours); confidence walk {Conf(mem, SkiDayRules.TravelClass.Walk):F2} car ") +
                   Invariant($"{Conf(mem, SkiDayRules.TravelClass.Car):F2} no car {Conf(mem, SkiDayRules.TravelClass.NoCarFar):F2}; ") + speeds;
        }

        private static string Mean(double hours, int n)
        {
            return n > 0 ? (hours / n).ToString("F1", CultureInfo.InvariantCulture) : "-";
        }

        // ── Round M1 measure line (m1-spec §3.1; the texts are the examples under M1-M9) ──────────

        // Words, indexed by the SkiDayTiming / SkiDayRules enum literals. Fixer L9, L10, L12: no word holds a comma (the
        // lists are comma-separated) and none nests parentheses in the DUMP history.
        private static readonly string[] s_ClassWords = { "walk", "car", "no car" };
        private static readonly string[] s_OutcomeWords = { "gone", "retargeted", "expired" };
        private static readonly string[] s_PlaceWords = { "never left home", "came back home", "road", "other building", "lodge", "deleted" };
        private static readonly string[] s_WhyWords =
            { "unknown", "ill", "moving away", "bedtime", "work or school", "shopping", "heading home", "other trip", "deleted" };
        private static readonly int[] s_WhyOrder = { 1, 2, 3, 4, 5, 6, 7, 8, 0 };   // "unknown" last
        private static readonly string[] s_MarkerWords =
            { "path failed at the start", "walker path failed", "vehicle path failed", "walker lost", "turned back" };
        private static readonly string[] s_LateWords = { "arrived late", "gave up", "deleted", "still not there after 6 h" };
        private static readonly string[] s_ClosestWords = { "under 0.5 km", "0.5-2 km", "farther", "never seen on the road" };
        // Round M2 (D2): bucket 0 (HeldAtHome) is never set since Round M2 and never printed; bucket 1 reads "held a car"
        // (seen holding the reserved car at one of our looks).
        private static readonly string[] s_ReserveWords =
        {
            "held a car while still at home", "held a car", "left home with none seen",
            "held one but never left", "never held one and never left",
        };
        // Mode 0 (ArrivalMode.Foot) also holds an "other" vehicle only (ambulance, police car) and a ride that fell
        // between two passes, so it says what was not seen (fixer L9; the DUMP history names an "other vehicle").
        private static readonly string[] s_ModeWords = { "no car/transit/taxi/bike seen", "by car", "public transport", "taxi", "bike" };
        private static readonly string[] s_TransitKindWords = { "bus", "tram", "train", "subway", "boat", "other" };
        private static readonly string[] s_StayBinWords = { "0-0.5 h", "0.5-1 h", "1-2 h", "2-4 h", "4-6 h", "6+ h" };
        private static readonly string[] s_EndCauseWords =
        {
            "own deadline (sleep or work)", "lifts closing", Invariant($"leisure need met (counter at least {SkiDayTiming.NeedFilledCounter}, inferred)"),
            "new outing", "removed by the game", "walked out",
            Invariant($"still there {SkiDayTiming.HoursFor(SkiDayTiming.OnSiteGraceFrames):F0} h past the deadline"), "deleted",
            "removed (on arrival)",   // Round M2 (D3, R6): EndCause.RemovedOnArrival = 8
        };
        private static readonly string[] s_ExitWords =
        {
            "going home", "to work or school", "shopping", "to another outing", "moving away", "to health care", "path failed",
            "other trip", "no trip purpose", "still inside after 2 h", "deleted",
        };
        // Round M2 (D4, remover-reason §2.3 item 4): SkiDayTiming.RemoveWhy literals 0-9, printed in s_RemoveWhyOrder
        // ("unknown" last); SkiDayTiming.NoPurposeAt literals 0-5 ("placed" = the game's teleport mark, m_PenaltyCounter at
        // least SkiDayTiming.PlacedMarkMin); MoveAwayReason literals 0-8 (Game.Agents/MoveAwayReason.cs (=); out of range
        // prints "-" through Word).
        private static readonly string[] s_RemoveWhyWords =
        {
            "unknown", "hotel money check", "moving away", "ill or hurt", "work time", "bedtime", "shopping errand",
            "lodge switched off", "need met", "no trip purpose",
        };
        private static readonly int[] s_RemoveWhyOrder = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 0 };
        private static readonly string[] s_NoPurposeAtWords =
            { "placed at home", "at home", "at the map edge", "placed elsewhere", "elsewhere", "in no building" };
        private static readonly string[] s_MoveReasonWords =
        {
            "no reason", "no suitable home", "not happy", "no adults", "no money", "tourist found no target",
            "tourist without a hotel", "hotel money check", "never moved in",
        };
        // Round M2 (R10): RM.SpeedLearn kinds 0-4.
        private static readonly string[] s_SpeedLearnWords =
        {
            "from arrivals", "from late arrivals", "upper bounds that lowered the all-outcomes speed", "upper bounds already met",
            "no information",
        };

        /// <summary>The measure body of one area's month (§3.1), in three parts written as three lines (fixer L7: one
        /// line ran to about 3,500 characters): <paramref name="failures"/> = groups 1-10 (refusals, distance, failures,
        /// car reserve), <paramref name="trips"/> = groups 11-16 (trip phases, stays, visit ends), <paramref name="modes"/>
        /// = groups 17-19 (modes, guest-scan timing, powder). Groups in the spec's order, each left out when all zero;
        /// "; " separates groups, " | " the parts of one group, ", " the items. <paramref name="mi"/> / <paramref name="mh"/>
        /// / <paramref name="ci"/> are the month's RM, RH and CM arrays (AreaMemory.MThis / HThis / CThis, or the Last
        /// ones). Round M2 adds, in the trips part: the door wait (group 11, D1), the speed samples after group 12 (R10;
        /// <paramref name="tk"/> names the speed the fit check reads), late arrivals by hour beside group 14 (D17), why the
        /// Leisure went and the game's clock checks after group 15 (15b, 15c, D4), the follow after leaving (16b, D5) and
        /// where no-trip-purpose exits stood (16c, U16); 15b prints whenever a visit ended with the Leisure gone before the
        /// deadline (silence impossible), the others when not all zero.</summary>
        private static void MeasureBody(AreaMemory mem, in SkiDayMonth m, int[] mi, double[] mh, int[] ci, int passInterval,
                                        in SkiDayRules.TripKnobs tk, out string failures, out string trips, out string modes)
        {
            var sb = new StringBuilder(1536);

            // 1. M4 too late to fit (+ the expected ski days lost, an upper bound)
            int lateClosing = 0, lateOwn = 0;
            for (int c = 0; c < 3; c++)
            {
                lateClosing += ci[CM.TooLateClosing + c];
                lateOwn += ci[CM.TooLateOwn + c];
            }
            if (lateClosing + lateOwn > 0)
            {
                Group(sb).Append(Invariant($"too late to fit {lateClosing + lateOwn} (before closing {lateClosing}, before the guest's own deadline {lateOwn} | "));
                for (int c = 0; c < 3; c++)
                    sb.Append(c > 0 ? ", " : "").Append(s_ClassWords[c]).Append(' ').Append(ci[CM.TooLateClosing + c] + ci[CM.TooLateOwn + c]);
                sb.Append(Invariant($" | expected ski days lost ~{m.TooLateChanceSum:F0})"));
            }
            // 2. M4 pool mean chance
            if (m.Pool > 0)
                Group(sb).Append(Invariant($"pool mean chance {m.PoolChanceSum / m.Pool:F3}"));
            // 3. M4 distance split (all, then the car class)
            string sentAll = Bands(ci, CM.SentBand, 0, 2, out int ns), arrAll = Bands(mi, RM.ArrivedBand, 0, 2, out int na),
                   failAll = Bands(mi, RM.FailedBand, 0, 2, out int nf);
            if (ns + na + nf > 0)
            {
                Group(sb).Append("by distance under 1 / 1-3 / 3-5 / 5-7 / 7+ km: sent ").Append(sentAll).Append(", arrived ").Append(arrAll)
                  .Append(", failed ").Append(failAll);
                string sentCar = Bands(ci, CM.SentBand, 1, 1, out int cs), arrCar = Bands(mi, RM.ArrivedBand, 1, 1, out int ca),
                       failCar = Bands(mi, RM.FailedBand, 1, 1, out int cf);
                if (cs + ca + cf > 0)
                    sb.Append(" | car class sent ").Append(sentCar).Append(", arrived ").Append(arrCar).Append(", failed ").Append(failCar);
            }
            // 4. M1 failures per class by place, with residents / tourists per outcome (fixer R3: field test 9's finding
            // "all failures were residents" must be readable from the log, not only from a DUMP taken before midnight)
            for (int c = 0; c < 3; c++)
            {
                bool any = false;
                for (int o = 0; o < 3; o++)
                {
                    int residents = 0, tourists = 0;
                    for (int p = 0; p < SkiDayTiming.PlaceCount; p++)
                    {
                        residents += FailAt(mi, o, p, c, 0);
                        tourists += FailAt(mi, o, p, c, 1);
                    }
                    if (residents + tourists == 0)
                        continue;
                    if (!any)
                        Group(sb).Append("failed ").Append(s_ClassWords[c]).Append(": ");
                    else
                        sb.Append(", ");
                    any = true;
                    sb.Append(s_OutcomeWords[o]).Append(' ').Append(residents + tourists)
                      .Append(Invariant($" (residents {residents}, tourists {tourists} | "));
                    bool firstPlace = true;
                    for (int p = 0; p < SkiDayTiming.PlaceCount; p++)
                    {
                        int k = FailAt(mi, o, p, c, 0) + FailAt(mi, o, p, c, 1);
                        if (k == 0)
                            continue;
                        sb.Append(firstPlace ? "" : ", ").Append(s_PlaceWords[p]).Append(' ').Append(k);
                        firstPlace = false;
                    }
                    sb.Append(')');
                }
            }
            // 5. M1 why gone / retargeted (F3)
            for (int c = 0; c < 3; c++)
            {
                for (int o = 0; o < 2; o++)
                {
                    int n = 0;
                    for (int y = 0; y < SkiDayTiming.GoneWhyCount; y++)
                        n += mi[RM.GoneWhy + (o * SkiDayTiming.GoneWhyCount + y) * 3 + c];
                    if (n == 0)
                        continue;
                    Group(sb).Append("why ").Append(s_OutcomeWords[o]).Append(", ").Append(s_ClassWords[c]).Append(": ");
                    bool first = true;
                    for (int j = 0; j < s_WhyOrder.Length; j++)
                    {
                        int y = s_WhyOrder[j];
                        int k = mi[RM.GoneWhy + (o * SkiDayTiming.GoneWhyCount + y) * 3 + c];
                        if (k == 0)
                            continue;
                        sb.Append(first ? "" : ", ").Append(s_WhyWords[y]).Append(' ').Append(k);
                        first = false;
                    }
                }
            }
            // 6. M1 expired by bound (F2), then what became of them (F7)
            for (int c = 0; c < 3; c++)
            {
                int own = mi[RM.ExpiredBound + c * 2], closing = mi[RM.ExpiredBound + c * 2 + 1];
                if (own + closing > 0)
                    Group(sb).Append("expired ").Append(s_ClassWords[c]).Append(Invariant($": own deadline (sleep or work) {own}, lifts closing {closing}"));
                int late = 0;
                for (int r = 0; r < SkiDayTiming.LateResultCount; r++)
                    late += mi[RM.LateWatch + c * SkiDayTiming.LateResultCount + r];
                if (late == 0)
                    continue;
                // Round M2 (D9): booked when the 6 h watch ends, which can fall in the next month: say so.
                Group(sb).Append("after expiring (counted when the 6 h watch ended), ").Append(s_ClassWords[c]).Append(": ");
                for (int r = 0; r < SkiDayTiming.LateResultCount; r++)
                    sb.Append(r > 0 ? ", " : "").Append(s_LateWords[r]).Append(' ').Append(mi[RM.LateWatch + c * SkiDayTiming.LateResultCount + r]);
            }
            // 7. M1 markers (failed, then arrived; F1: "seen", so they undercount)
            for (int c = 0; c < 3; c++)
            {
                bool failedAny = MarkersText(sb, mi, RM.FailMarker, c, "markers in failed ", false);
                MarkersText(sb, mi, RM.ArrivedMarker, c, failedAny ? "in arrived " : "markers in arrived ", failedAny);
            }
            // 8. M1 closest approach (F7)
            for (int c = 0; c < 3; c++)
            {
                int n = 0;
                for (int b = 0; b < 4; b++)
                    n += mi[RM.Closest + c * 4 + b];
                if (n == 0)
                    continue;
                Group(sb).Append("closest to the lodge before failing, ").Append(s_ClassWords[c]).Append(": ");
                for (int b = 0; b < 4; b++)
                    sb.Append(b > 0 ? ", " : "").Append(s_ClosestWords[b]).Append(' ').Append(mi[RM.Closest + c * 4 + b]);
            }
            // 9. M1 failures by 2-hour bin (F3)
            {
                bool any = false;
                for (int o = 0; o < 3; o++)
                {
                    if (!AnyIn(mi, RM.FailClock + o * 12, 12))
                        continue;
                    if (!any)
                        Group(sb).Append("failures by 2-hour bin from 00:00: ");
                    else
                        sb.Append(" | ");
                    any = true;
                    sb.Append(s_OutcomeWords[o]);
                    for (int b = 0; b < 12; b++)
                        sb.Append(' ').Append(mi[RM.FailClock + o * 12 + b]);
                }
            }
            // 10. M4 car reserve × mode (E2, E3, F8). Invariant: the block's sum = the queued car claims that resolved
            // this month (not CarsEnqueued, which counts claims made). Round M2 (D2): our first look is one pass after the
            // claim, after the game planned the first route, and every queued car claim had already left home by then in
            // field test 10 (0 of 101 March, 0 of 89 April), so the "held a car while still at home" bucket is no longer set
            // (Jobs.cs) and is skipped here, and RM.CarLeftFirstLook (always every claim) is neither bumped nor printed.
            {
                int reserve = 0;
                for (int j = 0; j < SkiDayTiming.CarReserveCount * SkiDayRules.ArrivalModes; j++)
                    reserve += mi[RM.CarReserve + j];
                int parked = m.SentCar - m.CarsEnqueued;
                if (reserve > 0 || parked > 0)
                {
                    Group(sb).Append(Invariant($"car reserve (car ski days that asked for a household car and ended {reserve}; {parked} more car ski days sent "))
                      .Append(Invariant($"this month already held a parked car; \"held\" = seen holding the reserved car at one of our looks, every {passInterval} "))
                      .Append("frames, after the game planned the first route): ");
                    for (int b = 1; b < SkiDayTiming.CarReserveCount; b++)
                    {
                        int n = 0;
                        for (int md = 0; md < SkiDayRules.ArrivalModes; md++)
                            n += mi[RM.CarReserve + b * SkiDayRules.ArrivalModes + md];
                        sb.Append(b > 1 ? ", " : "").Append(s_ReserveWords[b]).Append(' ').Append(n);
                        if (n > 0)
                            sb.Append(" (").Append(ModesText(mi, RM.CarReserve + b * SkiDayRules.ArrivalModes)).Append(')');
                    }
                }
            }
            failures = sb.ToString();
            sb.Clear();

            // 11. M2 trip phases (arrivals: claim to leaving + leaving to arrival = the trip, F6), all who left, the
            // hours on the road of failures
            {
                bool any = false;
                for (int c = 0; c < 3; c++)
                {
                    int n = PhaseN(mi, SkiDayTiming.Phase.LeaveToArrive, c);
                    if (n == 0)
                        continue;
                    if (!any)
                        Group(sb).Append("trip h (mean and count, arrivals unless said): ");
                    else
                        sb.Append(" | ");
                    any = true;
                    // Round M2 (D1): "of it at the door" = departure to moving off (the game's Divert{PathFailed} wait, its
                    // removal is the end, ResidentAISystem.cs:2580-2590, :2787-2790 (=)); unseen = arrivals whose walker
                    // was never seen moving off. LeaveToRide now starts at the move: "door to the vehicle".
                    int unseen = mi[RM.DoorUnseen + c];
                    sb.Append(s_ClassWords[c]).Append(" claim to leaving home ").Append(PhaseMean(mi, mh, SkiDayTiming.Phase.ClaimToLeaveArrived, c))
                      .Append(", leaving to arrival ").Append(PhaseMean(mi, mh, SkiDayTiming.Phase.LeaveToArrive, c)).Append(" (").Append(n)
                      .Append("; of it at the door ").Append(Mean(mh[RH.Door + c], mi[RM.DoorN + c]))
                      .Append(unseen > 0 ? ", not seen for " + unseen.ToString(CultureInfo.InvariantCulture) : "").Append(')');
                    int rode = PhaseN(mi, SkiDayTiming.Phase.LeaveToRide, c);
                    if (rode > 0)
                    {
                        sb.Append(": door to the vehicle ").Append(PhaseMean(mi, mh, SkiDayTiming.Phase.LeaveToRide, c))
                          .Append(", riding ").Append(PhaseMean(mi, mh, SkiDayTiming.Phase.RideSpan, c))
                          .Append(" (seen in a vehicle ").Append(PhaseMean(mi, mh, SkiDayTiming.Phase.RideSeen, c))
                          .Append("), from the vehicle ").Append(PhaseMean(mi, mh, SkiDayTiming.Phase.RideToArrive, c))
                          .Append(" (").Append(rode).Append(" rode)");
                    }
                }
                for (int c = 0; c < 3; c++)
                {
                    int n = PhaseN(mi, SkiDayTiming.Phase.ClaimToLeaveAll, c);
                    if (n > 0)
                        Group(sb).Append("all ").Append(s_ClassWords[c]).Append(" guests who left: claim to leaving home ")
                          .Append(PhaseMean(mi, mh, SkiDayTiming.Phase.ClaimToLeaveAll, c)).Append(" (").Append(n).Append(')');
                }
                for (int c = 0; c < 3; c++)
                {
                    bool first = true;
                    for (int o = 0; o < 3; o++)
                    {
                        int phase = SkiDayTiming.Phase.RoadGone + o, n = PhaseN(mi, phase, c);
                        if (n == 0)
                            continue;
                        if (first)
                            Group(sb).Append("failed after leaving, ").Append(s_ClassWords[c]).Append(" hours on the road: ");
                        sb.Append(first ? "" : ", ").Append(s_OutcomeWords[o]).Append(' ').Append(PhaseMean(mi, mh, phase, c)).Append(" (").Append(n).Append(')');
                        first = false;
                    }
                    // Round M2 (D1): departed failures' door wait (group 1 of RM.DoorN / RH.Door) and those that never moved
                    // off; every departed failure also books a road phase, so the group is open (else it is opened here).
                    int doorN = mi[RM.DoorN + 3 + c], never = mi[RM.NeverMovedFailed + c];
                    if (doorN + never > 0)
                    {
                        if (first)
                            Group(sb).Append("failed after leaving, ").Append(s_ClassWords[c]).Append(": ");
                        else
                            sb.Append(" | ");
                        sb.Append("at the door before moving off ").Append(Mean(mh[RH.Door + 3 + c], doorN)).Append(" (").Append(doorN)
                          .Append("), never moved off ").Append(never);
                    }
                }
            }
            // 12. M2 hours left at arrival (F2)
            if (na > 0)
            {
                Group(sb).Append("hours left at arrival ");
                for (int c = 0; c < 3; c++)
                    sb.Append(c > 0 ? ", " : "").Append(s_ClassWords[c]).Append(' ').Append(PhaseMean(mi, mh, SkiDayTiming.Phase.HoursLeftAtArrival, c));
            }
            // 12b. Round M2 (R10): what each ended claim taught the speeds, per class (classes with no count left out):
            // arrivals (both speeds), late arrivals (exact, the all-outcomes speed), upper bounds that lowered it or were
            // already met, and no information (an arrival or late arrival under 300 m or 0.05 h, or an end with no bound).
            if (AnyIn(mi, RM.SpeedLearn, 3 * s_SpeedLearnWords.Length))
            {
                Group(sb).Append(tk.FitUsesAllOutcomes ? "speed samples (the fit check uses all outcomes): " : "speed samples (the fit check uses arrivals only): ");
                bool firstClass = true;
                for (int c = 0; c < 3; c++)
                {
                    int b0 = RM.SpeedLearn + c * s_SpeedLearnWords.Length;
                    if (!AnyIn(mi, b0, s_SpeedLearnWords.Length))
                        continue;
                    sb.Append(firstClass ? "" : " | ").Append(s_ClassWords[c]);
                    for (int j = 0; j < s_SpeedLearnWords.Length; j++)
                        sb.Append(j > 0 ? ", " : " ").Append(s_SpeedLearnWords[j]).Append(' ').Append(mi[b0 + j]);
                    firstClass = false;
                }
            }
            // 13. M2 stay with its histogram (F5), then the time inside after the visit ended
            {
                bool any = false;
                for (int c = 0; c < 3; c++)
                {
                    int n = PhaseN(mi, SkiDayTiming.Phase.Stay, c);
                    if (n == 0)
                        continue;
                    if (!any)
                        Group(sb).Append("stay h ");
                    else
                        sb.Append(" | ");
                    any = true;
                    sb.Append(s_ClassWords[c]).Append(' ').Append(PhaseMean(mi, mh, SkiDayTiming.Phase.Stay, c)).Append(" (").Append(n).Append("):");
                    for (int b = 0; b < 6; b++)
                        sb.Append(b > 0 ? "," : "").Append(' ').Append(s_StayBinWords[b]).Append(' ').Append(mi[RM.StayBin + c * 6 + b]);
                }
                int exitN = 0;
                double exitH = 0.0;
                for (int c = 0; c < 3; c++)
                {
                    exitN += PhaseN(mi, SkiDayTiming.Phase.EndToExit, c);
                    exitH += mh[SkiDayTiming.Phase.EndToExit * 3 + c];
                }
                if (exitN > 0)
                    Group(sb).Append("then inside ").Append(Mean(exitH, exitN)).Append(" h (").Append(exitN).Append(" left the lodge)");
            }
            // 14. M2 arrivals by 2-hour bin; Round M2 (D17, measure-core G10): late arrivals after expiry in their own bins,
            // each part under its own guard (a month with late arrivals and no on-time one still prints them)
            bool onTime = AnyIn(mi, RM.ArrivalClock, 12), lateBins = AnyIn(mi, RM.LateClock, 12);
            if (onTime || lateBins)
            {
                Group(sb).Append("arrivals by 2-hour bin from 00:00 (on time):");
                for (int b = 0; b < 12; b++)
                    sb.Append(' ').Append(mi[RM.ArrivalClock + b]);
                if (lateBins)
                {
                    sb.Append(" | late after expiry:");
                    for (int b = 0; b < 12; b++)
                        sb.Append(' ').Append(mi[RM.LateClock + b]);
                }
            }
            // 15. M2 visits ended, with the mean stay per cause (Round M2: the 9th cause, "removed (on arrival)", prints like
            // the others; Word guards a table shorter than EndCauseCount)
            if (AnyIn(mi, RM.EndCause, SkiDayTiming.EndCauseCount))
            {
                Group(sb).Append("visits ended (mean stay h): ");
                bool first = true;
                for (int e = 0; e < SkiDayTiming.EndCauseCount; e++)
                {
                    int n = mi[RM.EndCause + e];
                    if (n == 0)
                        continue;
                    sb.Append(first ? "" : ", ").Append(Word(s_EndCauseWords, (byte)e)).Append(' ').Append(n).Append(" (").Append(Mean(mh[RH.EndStay + e], n)).Append(')');
                    first = false;
                }
            }
            // 15b. Round M2 (D4): why the Leisure went before the deadline (the first trace in CitizenBehaviorSystem's order
            // at the visit end, SkiDayTiming.RemoveWhyOf), for need met, removed by the game and removed (on arrival).
            // Printed WHENEVER such a visit ended (silence impossible: "none recorded" when the recorder did not fire);
            // items with 0 left out; mean stay per why = RH.RemoveStay / (residents + tourists of that why).
            int removedVisits = mi[RM.EndCause + (int)SkiDayTiming.EndCause.NeedFilled] + mi[RM.EndCause + (int)SkiDayTiming.EndCause.Removed] +
                                mi[RM.EndCause + (int)SkiDayTiming.EndCause.RemovedOnArrival];
            if (removedVisits > 0)
                RemoveWhyText(Group(sb), mi, mh);
            // 15c. Round M2 (D4): CitizenBehaviorSystem's own clock tests at the visit end (work time: WorkerSystem /
            // StudentSystem, CitizenBehaviorSystem.cs:743, 1.6.2 :771; bedtime: IsSleepTime, 1.6.2 :1108), per end cause with
            // a visit; they should read 0/0 under "removed by the game" (the claim deadline is min(sleep or work, closing)).
            if (AnyIn(mi, RM.EndCause, SkiDayTiming.EndCauseCount))
            {
                Group(sb).Append("the game's clock checks at the visit end, work time / bedtime now: ");
                bool first = true;
                for (int e = 0; e < SkiDayTiming.EndCauseCount; e++)
                {
                    if (mi[RM.EndCause + e] == 0)
                        continue;
                    sb.Append(first ? "" : ", ").Append(Word(s_EndCauseWords, (byte)e)).Append(' ').Append(mi[RM.LiveEnd + e * 2]).Append('/')
                      .Append(mi[RM.LiveEnd + e * 2 + 1]);
                    first = false;
                }
                int notChecked = mi[RM.LiveUnknown];
                if (notChecked > 0)
                    sb.Append(" | not checked ").Append(notChecked);
            }
            // 16. M2 visit end → exit pairs (F5)
            if (AnyIn(mi, RM.Exit, SkiDayTiming.EndCauseCount * SkiDayTiming.ExitWayCount))
            {
                Group(sb).Append("then left the lodge: ");
                bool first = true;
                for (int e = 0; e < SkiDayTiming.EndCauseCount; e++)
                {
                    for (int x = 0; x < SkiDayTiming.ExitWayCount; x++)
                    {
                        int n = mi[RM.Exit + e * SkiDayTiming.ExitWayCount + x];
                        if (n == 0)
                            continue;
                        sb.Append(first ? "" : ", ").Append(Word(s_EndCauseWords, (byte)e)).Append(" → ").Append(s_ExitWords[x]).Append(' ').Append(n);
                        first = false;
                    }
                }
            }
            // 16b. Round M2 (D5): the first hour after leaving the lodge (walkers-after W8)
            FollowText(sb, mi, mh);
            // 16c. Round M2 (U16): where the no-trip-purpose exits stood (non-zero places)
            if (AnyIn(mi, RM.NoPurposeAt, SkiDayTiming.NoPurposeAtCount))
            {
                Group(sb).Append("no trip purpose exits by place (\"placed\" = the game's teleport mark): ");
                bool first = true;
                for (int p = 0; p < SkiDayTiming.NoPurposeAtCount; p++)
                {
                    int n = mi[RM.NoPurposeAt + p];
                    if (n == 0)
                        continue;
                    sb.Append(first ? "" : ", ").Append(Word(s_NoPurposeAtWords, (byte)p)).Append(' ').Append(n);
                    first = false;
                }
            }
            trips = sb.ToString();
            sb.Clear();

            // 17. M3 modes seen at arrival, transit legs, modes of departed failures, never left (F12)
            {
                bool any = false;
                for (int c = 0; c < 3; c++)
                {
                    if (!AnyIn(mi, RM.ModeArrived + c * SkiDayRules.ArrivalModes, SkiDayRules.ArrivalModes))
                        continue;
                    if (!any)
                        Group(sb).Append("modes seen at arrival: ");
                    else
                        sb.Append(" | ");
                    any = true;
                    sb.Append(s_ClassWords[c]).Append(' ').Append(ModesText(mi, RM.ModeArrived + c * SkiDayRules.ArrivalModes));
                }
                if (AnyIn(mi, RM.TransitKind, SkiDayRules.KindBits))
                {
                    Group(sb).Append("public transport legs");
                    bool first = true;
                    for (int b = 0; b < SkiDayRules.KindBits; b++)
                    {
                        int n = mi[RM.TransitKind + b];
                        if (n == 0)
                            continue;
                        sb.Append(first ? " " : ", ").Append(s_TransitKindWords[b]).Append(' ').Append(n);
                        first = false;
                    }
                }
                for (int c = 0; c < 3; c++)
                {
                    bool left = AnyIn(mi, RM.ModeFailed + c * SkiDayRules.ArrivalModes, SkiDayRules.ArrivalModes);
                    int never = mi[RM.NeverLeftFailed + c];
                    if (!left && never == 0)
                        continue;
                    Group(sb).Append("failed ").Append(s_ClassWords[c]).Append(" ski days");
                    if (left)
                        sb.Append(" that left home: ").Append(ModesText(mi, RM.ModeFailed + c * SkiDayRules.ArrivalModes)).Append(" |");
                    else
                        sb.Append(':');
                    sb.Append(" never left home ").Append(never);
                }
            }
            // 18. M7 (a) guest-scan timing at the visit end (booked in the month the visit ended)
            if (AnyIn(mi, RM.Scan, 2 * SkiDayTiming.ScanVerdictCount))
            {
                Group(sb).Append("guest-scan timing at visit end (residents / tourists): ");
                string[] verdicts = { "seen", "missed", "unsure" };
                for (int v = 0; v < SkiDayTiming.ScanVerdictCount; v++)
                    sb.Append(v > 0 ? ", " : "").Append(verdicts[v]).Append(' ').Append(mi[RM.Scan + v]).Append('/')
                      .Append(mi[RM.Scan + SkiDayTiming.ScanVerdictCount + v]);
            }
            // 19. M9 powder mark passes
            if (m.PowderKnownPasses > 0)
                Group(sb).Append(Invariant($"powder mark on in {m.PowderMarkPasses} of {m.PowderKnownPasses} passes ") +
                                 Invariant($"({SkiDayTiming.HoursFor((uint)((long)m.PowderMarkPasses * passInterval)):F1} h)"));
            modes = sb.ToString();
        }

        /// <summary>The labels of the three measure parts (fixer L7), in MeasureBody's order.</summary>
        private static readonly string[] s_MeasurePartWords = { "failures", "trips and stays", "modes, scan, powder" };

        /// <summary>MeasureBody's three parts as an array (failures, trips and stays, modes), or null when all three are
        /// empty ("nothing measured"). Managed; at a roll, a jump, a load and a DUMP only.</summary>
        private static string[] MeasureParts(AreaMemory mem, in SkiDayMonth m, int[] mi, double[] mh, int[] ci, int passInterval,
                                             in SkiDayRules.TripKnobs tk)
        {
            MeasureBody(mem, m, mi, mh, ci, passInterval, tk, out string failures, out string trips, out string modes);
            return failures.Length + trips.Length + modes.Length == 0 ? null : new[] { failures, trips, modes };
        }

        /// <summary>Group 15b (Round M2, D4; remover-reason §2.4): counts residents / tourists per why (RM.RemoveWhy, kind ×
        /// RemoveWhyCount + why), the mean stay per why (RH.RemoveStay over residents + tourists) and the stays under
        /// SkiDayTiming.FastStayHours (RM.RemoveFast), in s_RemoveWhyOrder ("unknown" last); "none recorded" when no why
        /// was booked. The caller opened the group.</summary>
        private static void RemoveWhyText(StringBuilder sb, int[] mi, double[] mh)
        {
            int whys = SkiDayTiming.RemoveWhyCount;
            if (!AnyIn(mi, RM.RemoveWhy, 2 * whys))
            {
                sb.Append("why the Leisure went before the deadline: none recorded");
                return;
            }
            sb.Append(Invariant($"why the Leisure went before the deadline (residents/tourists; need met = counter at least {SkiDayTiming.NeedFilledCounter}): "));
            bool first = true;
            for (int j = 0; j < s_RemoveWhyOrder.Length; j++)
            {
                int y = s_RemoveWhyOrder[j];
                int r = mi[RM.RemoveWhy + y], t = mi[RM.RemoveWhy + whys + y];
                if (r + t == 0)
                    continue;
                sb.Append(first ? "" : ", ").Append(Word(s_RemoveWhyWords, (byte)y)).Append(' ').Append(r).Append('/').Append(t);
                first = false;
            }
            sb.Append(" | mean stay h: ");
            first = true;
            for (int j = 0; j < s_RemoveWhyOrder.Length; j++)
            {
                int y = s_RemoveWhyOrder[j];
                int n = mi[RM.RemoveWhy + y] + mi[RM.RemoveWhy + whys + y];
                if (n == 0)
                    continue;
                sb.Append(first ? "" : ", ").Append(Word(s_RemoveWhyWords, (byte)y)).Append(' ').Append(Mean(mh[RH.RemoveStay + y], n));
                first = false;
            }
            if (!AnyIn(mi, RM.RemoveFast, 2 * whys))
                return;
            sb.Append(Invariant($" | under {SkiDayTiming.FastStayHours} h: "));
            first = true;
            for (int j = 0; j < s_RemoveWhyOrder.Length; j++)
            {
                int y = s_RemoveWhyOrder[j];
                int r = mi[RM.RemoveFast + y], t = mi[RM.RemoveFast + whys + y];
                if (r + t == 0)
                    continue;
                sb.Append(first ? "" : ", ").Append(Word(s_RemoveWhyWords, (byte)y)).Append(' ').Append(r).Append('/').Append(t);
                first = false;
            }
        }

        /// <summary>Group 16b (Round M2, D5; walkers-after W8 with the contract's U16 wording): what became of the guests
        /// followed after leaving for home / with no trip purpose / moving away, per kind ("a / b / c"); booked in the
        /// month the follow ended. "followed" per kind is the sum of that kind's FollowResult slots, so the parts close:
        /// home (not placed) + placed home + map exit + another building + back in the lodge + deleted + still out.
        /// Left out when no follow ended and none was skipped at the cap.</summary>
        private static void FollowText(StringBuilder sb, int[] mi, double[] mh)
        {
            const int kinds = SkiDayTiming.FollowExitKinds;
            if (!AnyIn(mi, RM.FollowResult, kinds * SkiDayTiming.FollowResultCount * 2) && mi[RM.FollowSkipped] == 0)
                return;
            int home = (int)SkiDayTiming.FollowResult.Home, exit = (int)SkiDayTiming.FollowResult.MapExit,
                other = (int)SkiDayTiming.FollowResult.OtherBuilding, lodge = (int)SkiDayTiming.FollowResult.BackInLodge,
                deleted = (int)SkiDayTiming.FollowResult.Deleted, still = (int)SkiDayTiming.FollowResult.StillOut;
            string watch = Invariant($"{SkiDayTiming.HoursFor(SkiDayTiming.FollowWatchFrames):F0}");
            Group(sb).Append("after leaving the lodge (followed up to ").Append(watch).Append(" h; going home / no trip purpose / moving away): followed ");
            for (int k = 0; k < kinds; k++)
            {
                int f = 0;
                for (int r = 0; r < SkiDayTiming.FollowResultCount; r++)
                    f += FollowAt(mi, k, r, false) + FollowAt(mi, k, r, true);
                sb.Append(k > 0 ? " / " : "").Append(f);
            }
            sb.Append("; home ");
            for (int k = 0; k < kinds; k++)
                sb.Append(k > 0 ? " / " : "").Append(FollowAt(mi, k, home, false));
            sb.Append(" in mean ").Append(Mean(mh[RH.FollowHomeH], mi[RM.FollowHomeN])).Append(" h (");
            int[] modeOrder = { (int)SkiDayRules.ArrivalMode.Transit, (int)SkiDayRules.ArrivalMode.Foot, (int)SkiDayRules.ArrivalMode.Car,
                                (int)SkiDayRules.ArrivalMode.Taxi, (int)SkiDayRules.ArrivalMode.Bike };
            for (int j = 0; j < modeOrder.Length; j++)
                sb.Append(j > 0 ? ", " : "").Append(s_ModeWords[modeOrder[j]]).Append(' ').Append(mi[RM.FollowHomeMode + modeOrder[j]]);
            sb.Append("); placed home ");
            for (int k = 0; k < kinds; k++)
                sb.Append(k > 0 ? " / " : "").Append(FollowAt(mi, k, home, true));
            sb.Append(" (at once ");
            for (int k = 0; k < kinds; k++)
                sb.Append(k > 0 ? " / " : "").Append(AtOnceAt(mi, k, home));
            sb.Append("); at the map exit ");
            for (int k = 0; k < kinds; k++)
                sb.Append(k > 0 ? " / " : "").Append(FollowAt(mi, k, exit, false) + FollowAt(mi, k, exit, true));
            sb.Append(" (placed ");
            for (int k = 0; k < kinds; k++)
                sb.Append(k > 0 ? " / " : "").Append(FollowAt(mi, k, exit, true));
            sb.Append(", at once ");
            for (int k = 0; k < kinds; k++)
                sb.Append(k > 0 ? " / " : "").Append(AtOnceAt(mi, k, exit));
            sb.Append("); another building ");
            for (int k = 0; k < kinds; k++)
                sb.Append(k > 0 ? " / " : "").Append(FollowAt(mi, k, other, false) + FollowAt(mi, k, other, true));
            sb.Append(" (placed ");
            for (int k = 0; k < kinds; k++)
                sb.Append(k > 0 ? " / " : "").Append(FollowAt(mi, k, other, true));
            sb.Append("); back in the lodge ");
            for (int k = 0; k < kinds; k++)
                sb.Append(k > 0 ? " / " : "").Append(FollowAt(mi, k, lodge, false) + FollowAt(mi, k, lodge, true));
            sb.Append("; deleted ");
            for (int k = 0; k < kinds; k++)
                sb.Append(k > 0 ? " / " : "").Append(FollowAt(mi, k, deleted, false) + FollowAt(mi, k, deleted, true));
            sb.Append("; still out after ").Append(watch).Append(" h ");
            for (int k = 0; k < kinds; k++)
                sb.Append(k > 0 ? " / " : "").Append(FollowAt(mi, k, still, false) + FollowAt(mi, k, still, true));
            sb.Append(" (riding ").Append(mi[RM.FollowStillOut + (int)SkiDayTiming.StillOut.Riding])
              .Append(", waiting at a stop ").Append(mi[RM.FollowStillOut + (int)SkiDayTiming.StillOut.Waiting])
              .Append(", failed path ").Append(mi[RM.FollowStillOut + (int)SkiDayTiming.StillOut.FailedPath])
              .Append(", on foot ").Append(mi[RM.FollowStillOut + (int)SkiDayTiming.StillOut.OnFoot])
              .Append(", no walker ").Append(mi[RM.FollowStillOut + (int)SkiDayTiming.StillOut.NoWalker]).Append(')');
            sb.Append("; waited at a stop ").Append(mi[RM.FollowWaitN]).Append(" (mean ").Append(Mean(mh[RH.FollowWaitH], mi[RM.FollowWaitN]))
              .Append(Invariant($" h), within {SkiDayTiming.NearLodgeM:F0} m of the lodge ")).Append(mi[RM.FollowWaitNearN]).Append(" (mean ")
              .Append(Mean(mh[RH.FollowWaitNearH], mi[RM.FollowWaitNearN])).Append(" h)");
            sb.Append("; failed path seen ").Append(mi[RM.FollowFailSeen]);
            sb.Append(Invariant($"; not followed (cap {Tuning.FollowCap}) ")).Append(mi[RM.FollowSkipped]);
        }

        /// <summary>RM.FollowResult at (exit kind, result, placed); 0 outside the slots (SkiDayTiming.FollowSlot −1).</summary>
        private static int FollowAt(int[] mi, int kind, int result, bool placed)
        {
            int slot = SkiDayTiming.FollowSlot(kind, result, placed);
            return slot >= 0 ? mi[RM.FollowResult + slot] : 0;
        }

        /// <summary>RM.FollowAtOnce at (exit kind, result): placed at the first sample (Home, MapExit, OtherBuilding).</summary>
        private static int AtOnceAt(int[] mi, int kind, int result)
        {
            int slot = SkiDayTiming.AtOnceSlot(kind, result);
            return slot >= 0 ? mi[RM.FollowAtOnce + slot] : 0;
        }

        /// <summary>"; " before every group but the first.</summary>
        private static StringBuilder Group(StringBuilder sb)
        {
            return sb.Length > 0 ? sb.Append("; ") : sb;
        }

        /// <summary>"a/b/c/d/e" over the five distance bands of classes <paramref name="fromCls"/>..<paramref name="toCls"/>
        /// (block = cls × 5 + band).</summary>
        private static string Bands(int[] a, int block, int fromCls, int toCls, out int total)
        {
            total = 0;
            var s = new StringBuilder(24);
            for (int band = 0; band < SkiDayRules.DistanceBands; band++)
            {
                int n = 0;
                for (int c = fromCls; c <= toCls; c++)
                    n += a[block + c * SkiDayRules.DistanceBands + band];
                total += n;
                s.Append(band > 0 ? "/" : "").Append(n);
            }
            return s.ToString();
        }

        /// <summary>RM.Fail at (outcome, place, class, kind).</summary>
        private static int FailAt(int[] mi, int o, int place, int cls, int kind)
        {
            return mi[RM.Fail + ((o * SkiDayTiming.PlaceCount + place) * 3 + cls) * 2 + kind];
        }

        private static bool AnyIn(int[] a, int start, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (a[start + i] != 0)
                    return true;
            }
            return false;
        }

        private static int PhaseN(int[] mi, int phase, int cls) => mi[RM.PhaseN + phase * 3 + cls];

        private static string PhaseMean(int[] mi, double[] mh, int phase, int cls) => Mean(mh[phase * 3 + cls], PhaseN(mi, phase, cls));

        /// <summary>The non-zero modes of a 5-mode block, "by car 28, public transport 2, on foot 4" (car first, foot last).</summary>
        private static string ModesText(int[] a, int block)
        {
            var s = new StringBuilder(48);
            int[] order = { (int)SkiDayRules.ArrivalMode.Car, (int)SkiDayRules.ArrivalMode.Transit, (int)SkiDayRules.ArrivalMode.Taxi,
                            (int)SkiDayRules.ArrivalMode.Bike, (int)SkiDayRules.ArrivalMode.Foot };
            for (int i = 0; i < order.Length; i++)
            {
                int n = a[block + order[i]];
                if (n == 0)
                    continue;
                s.Append(s.Length > 0 ? ", " : "").Append(s_ModeWords[order[i]]).Append(' ').Append(n);
            }
            return s.Length > 0 ? s.ToString() : "none";
        }

        /// <summary>Group 7: the five markers of one class (zeros included) when any is set; returns whether it wrote.</summary>
        private static bool MarkersText(StringBuilder sb, int[] mi, int block, int cls, string prefix, bool sameGroup)
        {
            if (!AnyMarker(mi, block, cls))
                return false;
            (sameGroup ? sb.Append(" | ") : Group(sb)).Append(prefix).Append(s_ClassWords[cls]).Append(" ski days: ");
            for (int k = 0; k < SkiDayTiming.MarkerCount; k++)
                sb.Append(k > 0 ? ", " : "").Append(s_MarkerWords[k]).Append(' ').Append(mi[block + k * 3 + cls]);
            return true;
        }

        private static bool AnyMarker(int[] mi, int block, int cls)
        {
            for (int k = 0; k < SkiDayTiming.MarkerCount; k++)
            {
                if (mi[block + k * 3 + cls] != 0)
                    return true;
            }
            return false;
        }

        /// <summary>DUMP: this month's failures by class, outcome and place, resident / tourist, zeros included (§3.2).</summary>
        private static string FailPlacesText(int[] mi)
        {
            var sb = new StringBuilder(1024);
            for (int c = 0; c < 3; c++)
            {
                for (int o = 0; o < 3; o++)
                {
                    sb.Append(sb.Length > 0 ? "; " : "").Append(s_ClassWords[c]).Append(' ').Append(s_OutcomeWords[o]).Append(':');
                    for (int p = 0; p < SkiDayTiming.PlaceCount; p++)
                        sb.Append(p > 0 ? "," : "").Append(' ').Append(s_PlaceWords[p]).Append(' ').Append(FailAt(mi, o, p, c, 0)).Append('/')
                          .Append(FailAt(mi, o, p, c, 1));
                }
            }
            return sb.ToString();
        }

        private static double Conf(AreaMemory mem, SkiDayRules.TravelClass c)
        {
            return SkiDayRules.Confidence(c, mem.Tracks[(int)c]);
        }

        private string CensusText()
        {
            if (!m_CensusKnown)
                return "tourists (no census yet)";
            int[] k = m_CensusLast;
            return Invariant($"tourists (census {CensusAgeH():F1} h old): {k[Census.Parties]} parties, {k[Census.People]} people, ") +
                   Invariant($"{k[Census.AtEdge]} at a map-edge connection, {k[Census.Hotel]} with a hotel, {k[Census.OtherLodging]} other lodging, ") +
                   Invariant($"{k[Census.NoHotel]} no hotel, {k[Census.OutingUsed]} outing used, {k[Census.WithCar]} with a car");
        }

        private double CensusAgeH()
        {
            return SkiDayTiming.HoursFor(m_Simulation.frameIndex - m_CensusAppliedFrame);
        }

        // ── 4. census (ch2 §5.6; D30) ─────────────────────────────────────────────────────────────

        /// <summary>Main thread, after Complete: copies the census, the occupants (by lodge) and the outings on
        /// the way (by sign, with the age from m_TravelingSince: the first census that saw the meeting Traveling;
        /// absent meetings pruned). The snapshot is still the census's (rebuilds come after this).</summary>
        private void ApplyCensus()
        {
            m_CensusInFlight = false;
            for (int i = 0; i < Census.Count && i < m_Census.Length; i++)
                m_CensusLast[i] = m_Census[i];
            m_CensusKnown = true;
            m_CensusAppliedFrame = m_CensusFrame;

            m_OccupantsByLodge.Clear();
            int lodges = Math.Min(m_Lodges.Length, m_Occupants.Length / Occupant.Kinds);
            for (int li = 0; li < lodges; li++)
            {
                int b = li * Occupant.Kinds;
                m_OccupantsByLodge[m_Lodges[li].Lodge] = new Occupants
                {
                    Workers = m_Occupants[b + Occupant.Worker],
                    Residents = m_Occupants[b + Occupant.ResidentOnLeisure],
                    Tourists = m_Occupants[b + Occupant.TouristOnLeisure],
                    OutingMembers = m_Occupants[b + Occupant.OutingMember],
                    Other = m_Occupants[b + Occupant.Other],
                };
            }

            foreach (AreaMemory mem in m_State.Values)
            {
                mem.OutingsOnTheWay = 0;
                mem.OldestOutingH = 0.0;
            }
            m_SeenMeetings.Clear();
            for (int i = 0; i < m_Traveling.Length; i++)
            {
                TravelingOuting t = m_Traveling[i];
                m_SeenMeetings.Add(t.Meeting);
                if (!m_TravelingSince.TryGetValue(t.Meeting, out uint first))
                {
                    first = m_CensusFrame;
                    m_TravelingSince[t.Meeting] = first;
                }
                Entity sign = SignOfTarget(t.Target);
                if (sign == Entity.Null || !m_State.TryGetValue(sign, out AreaMemory mem))
                    continue;
                mem.OutingsOnTheWay++;
                double h = SkiDayTiming.HoursFor(m_CensusFrame - first);
                if (h > mem.OldestOutingH)
                    mem.OldestOutingH = h;
            }
            m_PruneScratch.Clear();
            foreach (Entity meeting in m_TravelingSince.Keys)
            {
                if (!m_SeenMeetings.Contains(meeting))
                    m_PruneScratch.Add(meeting);
            }
            for (int i = 0; i < m_PruneScratch.Count; i++)
                m_TravelingSince.Remove(m_PruneScratch[i]);
        }

        private Entity SignOfTarget(Entity target)
        {
            if (m_AreaOfSign.TryGetValue(target, out int a) && a >= 0 && a < m_Areas.Length)
                return m_Areas[a].Sign;
            if (m_LodgeIndex.TryGetValue(target, out int li) && li >= 0 && li < m_Lodges.Length)
            {
                int area = m_Lodges[li].Area;
                if (area >= 0 && area < m_Areas.Length)
                    return m_Areas[area].Sign;
            }
            return Entity.Null;
        }

        // ── 4b. walkers near the lodges (Round M2, D5) ────────────────────────────────────────────

        /// <summary>Main thread, after Complete: the walker scan into managed copies, per lodge (m_NearByLodge, rebuilt: a
        /// lodge added since reads "no scan yet") with its frame and minute, and folded into this game hour's accumulators
        /// (scans, the last scan, the most walkers with its minute and waiting count). The snapshot is still the scan's
        /// (rebuilds come after this, step 6).</summary>
        private void ApplyNear()
        {
            m_NearInFlight = false;
            m_NearKnown = true;
            m_NearHourScans++;
            int scanned = m_NearTotals.Length > 0 ? m_NearTotals[0] : 0;
            m_NearByLodge.Clear();
            int n = Math.Min(m_Lodges.Length, m_NearOut.Length);
            for (int li = 0; li < n; li++)
            {
                Entity lodge = m_Lodges[li].Lodge;
                NearOut o = m_NearOut[li];
                m_NearByLodge[lodge] = new NearCopy { Out = o, Frame = m_NearFrame, Minute = m_NearMinute, Scanned = scanned };
                m_NearHour.TryGetValue(lodge, out NearHour h);
                h.Scans++;
                h.Last = o;
                if (h.Scans == 1 || o.Walkers > h.MostWalkers)
                {
                    h.MostWalkers = o.Walkers;
                    h.MostMinute = m_NearMinute;
                    h.MostWaiting = o.Waiting;
                }
                m_NearHour[lodge] = h;
            }
        }

        /// <summary>Round M2 (D5, walkers-after W9): on an hour step of 1 (m_HourStep, ClimateTick), per ski area with a
        /// lodge: one `[near]` line when a ski-day guest was on site or followed during the hour (ApplyPass sets
        /// GuestsThisHour), else one pause line when the hour before printed; then the hour's accumulators reset. Any other
        /// step (a load's first pass, a time jump with its own line) resets without a line. The label is the TimeSystem
        /// clock at this pass, floored to the hour (the step comes from the climate date's hour key; the two agree within
        /// the pass).</summary>
        private void NearHourTick()
        {
            int step = m_HourStep;
            if (step == 0)
                return;
            if (step == 1)
            {
                const int minutesPerHour = 60;
                string label = ClockText(SkiDayTiming.MinuteOfDay(m_Time.normalizedTime) / minutesPerHour * minutesPerHour);
                for (int a = 0; a < m_Areas.Length; a++)
                {
                    AreaJob aj = m_Areas[a];
                    if (aj.LodgeCount <= 0 || !m_State.TryGetValue(aj.Sign, out AreaMemory mem))
                        continue;
                    if (mem.GuestsThisHour)
                    {
                        Mod.Log.Info(NearHourLine(aj, label));
                        mem.NearPrinted = true;
                    }
                    else if (mem.NearPrinted)
                    {
                        Mod.Log.Info($"SkiDays [near] ski area {Fmt(aj.Sign)}{NameSuffix(aj.Sign)}: no ski-day guest on site or followed in the hour to " +
                                     $"{label}; hourly walker lines pause until there is one (the Dump still shows the last scan)");
                        mem.NearPrinted = false;
                    }
                }
            }
            m_NearHour.Clear();
            m_NearHourScans = 0;
            foreach (AreaMemory mem in m_State.Values)
                mem.GuestsThisHour = false;
        }

        /// <summary>The hourly `[near]` line of one ski area: per lodge the hour's last scan, its most walkers with the
        /// minute and waiting count, the wait so far and the game's waiting count at the line stops within reach.</summary>
        private string NearHourLine(in AreaJob aj, string label)
        {
            var sb = new StringBuilder(320);
            sb.Append("SkiDays [near] hour to ").Append(label).Append(", ski area ").Append(Fmt(aj.Sign)).Append(NameSuffix(aj.Sign)).Append(" (")
              .Append(m_NearHourScans).Append(Invariant($" scans of walkers within {SkiDayTiming.NearLodgeM:F0} m of each lodge): "));
            int end = Math.Min(aj.LodgeStart + aj.LodgeCount, m_Lodges.Length);
            bool first = true;
            for (int li = Math.Max(0, aj.LodgeStart); li < end; li++)
            {
                Entity lodge = m_Lodges[li].Lodge;
                sb.Append(first ? "" : " | ").Append("lodge ").Append(Fmt(lodge));
                first = false;
                if (!m_NearHour.TryGetValue(lodge, out NearHour h) || h.Scans == 0)
                {
                    sb.Append(": no scan this hour");
                    continue;
                }
                NearOut o = h.Last;
                if (o.HasPosition == 0)
                {
                    sb.Append(": the lodge has no position");
                    continue;
                }
                sb.Append(" last ").Append(o.Walkers).Append(" (waiting for transport ").Append(o.Waiting).Append(", failed path ").Append(o.FailedPath)
                  .Append(", hanging around ").Append(o.Hangaround).Append(", walking ").Append(WalkingOf(o)).Append("; not drawn ").Append(o.NotDrawn)
                  .Append("; ski-day guests ").Append(o.Ours).Append(", waiting ").Append(o.OursWaiting).Append("), most ").Append(h.MostWalkers)
                  .Append(" at ").Append(ClockText(h.MostMinute)).Append(" (waiting ").Append(h.MostWaiting).Append("); ").Append(WaitText(o, false))
                  .Append("; the game's waiting count ").Append(o.StopCount).Append(" at ").Append(o.StopPoints).Append(" line stop point(s), nearest ")
                  .Append(NearestStopText(o.NearestStopM));
            }
            return sb.ToString();
        }

        /// <summary>Walkers of none of the other kinds (SkiDayTiming.WalkerKindOf gives each walker exactly one).</summary>
        private static int WalkingOf(in NearOut o) => Math.Max(0, o.Walkers - o.Waiting - o.FailedPath - o.Hangaround);

        /// <summary>"wait so far mean m h, longest x h" from the game's Resident.m_Timer of the walkers waiting for transport
        /// (SkiDayTiming.WaitHoursOfTimer: a mean, the timer steps by a random 1-2), "(game timer)" in the DUMP; "wait so
        /// far -" when none waits. The mean scales linearly: WaitHoursOfTimer(1) × the mean timer.</summary>
        private static string WaitText(in NearOut o, bool dump)
        {
            if (o.WaitN <= 0)
                return "wait so far -";
            double mean = SkiDayTiming.WaitHoursOfTimer(1) * ((double)o.WaitTimerSum / o.WaitN);
            return Invariant($"wait so far mean {mean:F1} h, longest {SkiDayTiming.WaitHoursOfTimer(o.WaitTimerMax):F1} h") + (dump ? " (game timer)" : "");
        }

        /// <summary>"38 m", or "none" without a line stop point (+∞).</summary>
        private static string NearestStopText(float metres)
        {
            return float.IsNaN(metres) || float.IsInfinity(metres) ? "none" : metres.ToString("F0", CultureInfo.InvariantCulture) + " m";
        }

        // ── 5. climate ring (ch2 §4.4; D19) ───────────────────────────────────────────────────────

        /// <summary>One ClimateSystem.SampleClimate per game hour (24 after a load, a jump or a climate change):
        /// the climate prefab curves with overrides applied (ClimateSystem.cs:97-107, :625-654 (=)); live weather is
        /// the same curve (:465-472), so sampling past hours is exact. Newest hour at index 0.</summary>
        private void ClimateTick()
        {
            int days = m_Time.daysPerYear;
            float aligned = RunSnowRules.HourAlignedDate((float)m_Climate.currentDate, days, out int hourKey);
            int step = SkiDayTiming.HourStep(m_HourKey, hourKey, days);
            m_HourStep = step;   // Round M2 (D5): stored before the no-climate return below, so NearHourTick never stops silently
            if (step != 0)
            {
                m_HourKey = hourKey;
                m_CensusDue = true;   // hourly census
                m_NearDue = true;     // Round M2 (D5): and a walker scan on the hour
            }
            if (m_Climate.currentClimate == Entity.Null)
            {
                m_RingValid = false;   // SampleClimate returns default without a climate (:653)
                return;
            }
            if (step == 0 && m_RingValid)
                return;
            m_BaseHeight = m_SnowLine.GetBaseHeight();
            m_Lapse = m_SnowLine.GetLapsePerM();
            m_Freezing = m_SnowLine.GetFreezingTemperature();
            if (step == 1 && m_RingValid)
            {
                for (int k = SkiDayRules.SnowHourSamples - 1; k > 0; k--)
                {
                    m_Precip[k] = m_Precip[k - 1];
                    m_Ambient[k] = m_Ambient[k - 1];
                }
                Sample(aligned, 0, days);
            }
            else
            {
                for (int k = 0; k < SkiDayRules.SnowHourSamples; k++)
                    Sample(aligned, k, days);
            }
            m_RingValid = true;
        }

        private void Sample(float aligned, int k, int days)
        {
            ClimateSystem.ClimateSample s = m_Climate.SampleClimate(SkiDayRules.SnowHourSampleDate(aligned, k, days));
            m_Precip[k] = s.precipitation;
            m_Ambient[k] = s.temperature;
        }

        /// <summary>D19: snow-hours at <paramref name="topM"/> over the ring; −1 when unknown.</summary>
        private int SnowHoursAt(float topM)
        {
            if (!m_RingValid)
                return -1;
            int n = 0;
            for (int k = 0; k < SkiDayRules.SnowHourSamples; k++)
            {
                if (SkiDayRules.IsSnowHour(m_Precip[k], m_Ambient[k], topM, m_BaseHeight, m_Lapse, m_Freezing))
                    n++;
            }
            return n;
        }

        // ── 6b. the powder year (Round M1 M9) ─────────────────────────────────────────────────────

        /// <summary>M9: the weather is a pure function of the date plus any developer override (1.6.2 ClimateSystem.cs
        /// :607-654: curves evaluated at t × daysPerYear) and repeats every game year, so the whole year's "Powder day"
        /// hours can be counted once. 24 × days SampleClimate calls (288 at 12 days), refilled only when the climate
        /// entity, daysPerYear or the game year changed; each call reads the prefab on the main thread
        /// (PrefabSystem.GetPrefab, E13), kept because it applies the overrides exactly as the live ring (ClimateTick)
        /// does, and timed. Then, per ski area of the snapshot whose top is known and whose table is not from the current
        /// samples (fixer M1-ECS-6: a refill for any reason, a climate change too, makes every table stale): the snow
        /// hours at its top, MarkHoursPerMonth, one `[powder]` line saying why ("at load", "after the year roll", "after a
        /// climate change", "first time this load" for an area that joined later; fixer L8, R9) and the sampling time
        /// only on the lines of the pass that refilled. The top can move when runs open or close;
        /// the table is not recomputed for that (it would log at every opening). Needs the climate ring's base height,
        /// lapse and freezing point (ClimateTick), so only after the ring is valid.</summary>
        private void PowderYearTick()
        {
            if (m_Climate.currentClimate == Entity.Null || !m_RingValid)
                return;
            int days = Math.Max(1, m_Time.daysPerYear);
            int year = m_Time.year;
            int hours = 24 * days;
            if (m_YearPrecip == null || m_YearPrecip.Length != hours)
            {
                m_YearPrecip = new float[hours];   // only when daysPerYear changes: never per pass
                m_YearAmbient = new float[hours];
                m_YearSnow = new bool[hours];
                m_YearValid = false;
            }
            bool refilled = false;
            if (!m_YearValid || m_YearClimate != m_Climate.currentClimate || m_YearDays != days || m_YearYear != year)
            {
                // Why (fixer L8, R9, M1-ECS-6): the first refill of this load is "at load"; later a new game year, else a
                // new climate entity or day count (the developer menu). Every refill makes every area's table stale.
                m_YearWhy = m_YearGeneration == 0 ? "at load"
                    : m_YearYear != year ? "after the year roll"
                    : m_YearClimate != m_Climate.currentClimate ? "after a climate change" : "after the days per year changed";
                long t0 = Stopwatch.GetTimestamp();
                for (int h = 0; h < hours; h++)
                {
                    ClimateSystem.ClimateSample s = m_Climate.SampleClimate(SkiDayRules.HourDate(h, days));
                    m_YearPrecip[h] = s.precipitation;
                    m_YearAmbient[h] = s.temperature;
                }
                m_YearSampleMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                m_YearValid = true;
                m_YearClimate = m_Climate.currentClimate;
                m_YearDays = days;
                m_YearYear = year;
                m_YearGeneration++;
                refilled = true;
            }
            for (int a = 0; a < m_Areas.Length && a < m_AreaInfo.Count; a++)
            {
                AreaInfo info = m_AreaInfo[a];
                Entity sign = m_Areas[a].Sign;
                if (!info.TopKnown || !m_State.TryGetValue(sign, out AreaMemory mem) || mem.PowderYearGeneration == m_YearGeneration)
                    continue;
                float top = (float)info.TopM;
                for (int h = 0; h < hours; h++)
                    m_YearSnow[h] = SkiDayRules.IsSnowHour(m_YearPrecip[h], m_YearAmbient[h], top, m_BaseHeight, m_Lapse, m_Freezing);
                SkiDayTiming.MarkHoursPerMonth(m_YearSnow, days, m_YearMarks, m_YearSnowHours);
                Array.Copy(m_YearMarks, mem.PowderYearMarks, 12);
                Array.Copy(m_YearSnowHours, mem.PowderYearSnow, 12);
                mem.PowderYearTopM = top;
                // Not refilled this pass: the area joined, or its top became known, after the year was sampled.
                mem.PowderYearWhen = refilled ? m_YearWhy : mem.PowderYearLogged == int.MinValue ? "first time this load" : m_YearWhy + ", computed late";
                mem.PowderYearLogged = year;
                mem.PowderYearGeneration = m_YearGeneration;
                int markSum = 0, snowSum = 0;
                for (int i = 0; i < 12; i++)
                {
                    markSum += m_YearMarks[i];
                    snowSum += m_YearSnowHours[i];
                }
                string sampled = refilled ? Invariant($"sampled in {m_YearSampleMs:F2} ms") : "year sampled earlier";
                Mod.Log.Info(Invariant($"SkiDays [powder] year {year} ski area {Fmt(sign)}") + NameSuffix(sign) +
                             Invariant($" (top {top:F0} m, {mem.PowderYearWhen}; {sampled}): the weather repeats every game year; ") +
                             Invariant($"\"Powder day\" hours per month (of {hours / 12}): ") + MonthList(m_YearMarks) + "; snow-hours per month: " +
                             MonthList(m_YearSnowHours) + Invariant($"; year {markSum} \"Powder day\" hours, {snowSum} snow-hours"));
            }
        }

        /// <summary>"Jan 22, Feb 20, …, Dec 18".</summary>
        private static string MonthList(int[] perMonth)
        {
            var sb = new StringBuilder(96);
            for (int i = 0; i < 12 && i < perMonth.Length; i++)
                sb.Append(i > 0 ? ", " : "").Append(s_MonthNames[i]).Append(' ').Append(perMonth[i]);
            return sb.ToString();
        }

        // ── 6. the area snapshot (ch2 §4) ─────────────────────────────────────────────────────────

        /// <summary>Rebuild only on change: attraction Generation, LodgeSetRevision, LiftCensusSystem.Revision, the
        /// settings key (four knobs + ticket price; L17 on a change), the climate hour, the backstop.</summary>
        private void MaybeRebuildSnapshot()
        {
            m_PassesSinceRebuild++;
            SkiDayRules.Knobs knobs = ReadKnobs();
            int price = m_Ticket.Price;
            bool settingsChanged = !m_KnobsKnown || knobs.LocalsPct != m_Knobs.LocalsPct || knobs.TouristsPct != m_Knobs.TouristsPct ||
                                   knobs.ReachKm != m_Knobs.ReachKm || knobs.PowderPct != m_Knobs.PowderPct || price != m_Price;
            if (settingsChanged && m_KnobsKnown)
                SettingsLine(knobs, price);
            m_Knobs = knobs;
            m_Price = price;
            m_KnobsKnown = true;

            int generation = m_Attraction.Generation;
            int lodgeRevision = m_Attraction.LodgeSetRevision;
            int liftRevision = m_LiftCensus.Revision;
            if (m_SnapshotValid && !settingsChanged && generation == m_SnapGeneration && lodgeRevision == m_SnapLodgeRevision &&
                liftRevision == m_SnapLiftRevision && m_HourKey == m_SnapHourKey && m_PassesSinceRebuild < Tuning.BackstopPasses)
                return;
            m_SnapshotValid = true;
            m_SnapGeneration = generation;
            m_SnapLodgeRevision = lodgeRevision;
            m_SnapLiftRevision = liftRevision;
            m_SnapHourKey = m_HourKey;
            m_PassesSinceRebuild = 0;

            long t0 = Stopwatch.GetTimestamp();
            RebuildSnapshot();
            double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            m_Rebuilds++;
            m_RebuildSumMs += ms;
            if (ms > m_RebuildMaxMs)
                m_RebuildMaxMs = ms;
        }

        /// <summary>Catalog Phase B (header CATALOG PHASE B): one snapshot lodge with its tier's numbers from SkiCatalog.Lodges
        /// (an out-of-range index reads the A-frame row 0, as the score does; no row at all → today's A-frame values).</summary>
        private static LodgeJob LodgeJobOf(Entity lodge, int area, in SkiResortAttractivenessSystem.LodgeInfo info)
        {
            int count = SkiCatalog.Lodges.Count;
            LodgeDef def = count == 0 ? null : (info.LodgeIndex >= 0 && info.LodgeIndex < count ? SkiCatalog.Lodges[info.LodgeIndex] : SkiCatalog.Lodges[0]);
            return new LodgeJob
            {
                Lodge = lodge,
                Area = area,
                Capacity = def != null ? def.Capacity : (int)SkiDayRules.LodgeSkiersPerDay,
                PriceTolerance = def != null ? def.PriceTolerance : 0,
                NoRoadNeeded = (byte)(def != null && def.RoadRule != LodgeRoadRule.Base ? 1 : 0),
                LodgeIndex = (short)(def != null ? def.Index : 0),
            };
        }

        /// <summary>Catalog Phase B: the ARMED line's per-tier numbers from SkiCatalog.Lodges, "aFrame 360, hut 200, …"
        /// (capacity) or "aFrame 0, …, grand 40" (price tolerance); invariant culture.</summary>
        private static string TierText(bool tolerance)
        {
            var sb = new StringBuilder(96);
            for (int i = 0; i < SkiCatalog.Lodges.Count; i++)
            {
                LodgeDef d = SkiCatalog.Lodges[i];
                sb.Append(i > 0 ? ", " : "").Append(d.Id).Append(' ')
                  .Append((tolerance ? d.PriceTolerance : d.Capacity).ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>Catalog Phase B: the lodge tier's id for logs and the DUMP ("aFrame", "hut", …; "?" out of range).</summary>
        private static string TierOf(int lodgeIndex)
        {
            return lodgeIndex >= 0 && lodgeIndex < SkiCatalog.Lodges.Count ? SkiCatalog.Lodges[lodgeIndex].Id : "?";
        }

        /// <summary>ch2 §4.2: one AreaJob per live sign (CopySigns order), its active lodges (the sign itself
        /// excluded; one area per lodge), its member runs and lift stations (D10 inputs), score inputs and powder;
        /// tracks from AreaMemory. Main thread, between jobs (step 1 completed them); no EntityManager read.</summary>
        private void RebuildSnapshot()
        {
            m_Areas.Clear();
            m_Lodges.Clear();
            m_SiteRuns.Clear();
            m_SiteStations.Clear();
            m_Tracks.Clear();
            m_AreaOfSign.Clear();
            m_LodgeIndex.Clear();
            m_AreaInfo.Clear();

            m_Attraction.CopySigns(m_SignScratch);
            bool setChanged = false;
            int kept = 0;
            for (int s = 0; s < m_SignScratch.Count; s++)
            {
                Entity sign = m_SignScratch[s];
                int a = m_Areas.Length;
                if (sign == Entity.Null || !m_AreaOfSign.TryAdd(sign, a))
                    continue;
                if (kept >= m_SnapSigns.Count || m_SnapSigns[kept] != sign)
                    setChanged = true;
                kept++;

                bool known = m_Attraction.TryGetAreaScore(sign, out SkiResortAttractivenessSystem.AreaScoreView v);
                SkiResortScore.Result r = v.Result;
                AreaMemory mem = MemoryOf(sign);

                int lodgeStart = m_Lodges.Length;
                // Catalog Phase B (header CATALOG PHASE B; was CopyActiveLodges + LodgeJob { Lodge, Area }): the same
                // lodges with their tier's numbers (main thread: the jobs never read a catalog row, contract risk R-a).
                m_Attraction.CopyActiveLodgeInfo(sign, m_LodgeInfoScratch);
                for (int i = 0; i < m_LodgeInfoScratch.Count; i++)
                {
                    SkiResortAttractivenessSystem.LodgeInfo info = m_LodgeInfoScratch[i];
                    Entity lodge = info.Lodge;
                    if (lodge == Entity.Null || lodge == sign)
                        continue;   // never the Welcome Sign (D8)
                    if (m_LodgeIndex.TryAdd(lodge, m_Lodges.Length))
                        m_Lodges.Add(LodgeJobOf(lodge, a, info));
                }
                int runStart = m_SiteRuns.Length, stationStart = m_SiteStations.Length;
                m_Attraction.CopySiteMembers(sign, m_RunScratch, m_StationScratch);
                for (int i = 0; i < m_RunScratch.Count; i++)
                {
                    if (m_RunScratch[i] != Entity.Null)
                        m_SiteRuns.Add(m_RunScratch[i]);
                }
                for (int i = 0; i < m_StationScratch.Count; i++)
                {
                    if (m_StationScratch[i] != Entity.Null)
                        m_SiteStations.Add(m_StationScratch[i]);
                }

                bool topKnown = known && r.TopKnown;
                int snowHours = topKnown ? SnowHoursAt((float)r.TopM) : -1;
                double powder = snowHours >= 0 ? SkiDayRules.PowderOf(snowHours) : double.NaN;   // NaN unknown (0 in formulas)
                m_Areas.Add(new AreaJob
                {
                    Sign = sign,
                    SignRef = Ref(sign),
                    LodgeStart = lodgeStart,
                    LodgeCount = m_Lodges.Length - lodgeStart,
                    RunStart = runStart,
                    RunCount = m_SiteRuns.Length - runStart,
                    StationStart = stationStart,
                    StationCount = m_SiteStations.Length - stationStart,
                    Attraction = known ? r.Attraction : 0,
                    RunsCounted = known ? r.RunsCounted : 0,
                    ScoreKnown = (byte)(known ? 1 : 0),
                    WriteHeld = (byte)(known && v.WriteHeld ? 1 : 0),
                    NightRunOpen = (byte)(known && r.NightRunsCounted > 0 ? 1 : 0),
                    Efficiency = known ? r.Efficiency : 0.0,
                    RunsCapacity = known ? r.RunsCapacity : 0.0,
                    OpenShare = known ? SkiDayRules.OpenShare(r.RunsValue, r.RunsValueFull) : 0.0,
                    Powder = powder,
                    // Catalog Phase B (R13, D13): locals × 1.2 while the school is active and a green run counts.
                    SchoolFactor = known ? SchoolRule.DemandFactorOf(r.SchoolActive, r.GreenCounted) : 1.0,
                });
                for (int c = 0; c < 3; c++)
                    m_Tracks.Add(mem.Tracks[c]);
                m_AreaInfo.Add(new AreaInfo { SnowHours = snowHours, TopKnown = topKnown, TopM = topKnown ? r.TopM : 0.0 });
            }
            if (kept != m_SnapSigns.Count)
                setChanged = true;

            int areas = m_Areas.Length, lodges = m_Lodges.Length;
            m_SiteOut.Resize(lodges, NativeArrayOptions.ClearMemory);
            m_LodgeReject.Resize(lodges, NativeArrayOptions.ClearMemory);
            m_LodgeCarFails.Resize(lodges, NativeArrayOptions.ClearMemory);
            m_ClaimOut.Resize(areas, NativeArrayOptions.ClearMemory);
            m_ResolveOut.Resize(areas + 1, NativeArrayOptions.ClearMemory);
            m_ClaimMeasure.Resize(areas * CM.Count, NativeArrayOptions.ClearMemory);              // Round M1 (E4): with
            m_ResolveMeasure.Resize((areas + 1) * RM.Count, NativeArrayOptions.ClearMemory);      // m_ClaimOut and
            m_ResolveHours.Resize((areas + 1) * RH.Count, NativeArrayOptions.ClearMemory);        // m_ResolveOut
            m_ActiveNow.Resize(areas, NativeArrayOptions.ClearMemory);
            m_SiteDue = true;

            if (setChanged)
            {
                m_SnapSigns.Clear();
                for (int a = 0; a < areas; a++)
                    m_SnapSigns.Add(m_Areas[a].Sign);
                m_PruneScratch.Clear();
                foreach (Entity sign in m_Views.Keys)
                {
                    if (!m_SnapSigns.Contains(sign))
                        m_PruneScratch.Add(sign);
                }
                for (int i = 0; i < m_PruneScratch.Count; i++)
                    m_Views.Remove(m_PruneScratch[i]);
                Revision++;
            }
        }

        private SkiDayRules.Knobs ReadKnobs()
        {
            SkiResortSettings s = Mod.Settings;
            return new SkiDayRules.Knobs
            {
                LocalsPct = SkiResortSettings.SkiDayLocalsPctOf(s),
                TouristsPct = SkiResortSettings.SkiDayTouristsPctOf(s),
                ReachKm = SkiResortSettings.SkiDayReachKmOf(s),
                PowderPct = SkiResortSettings.SkiDayPowderPctOf(s),
            };
        }

        private AreaMemory MemoryOf(Entity sign)
        {
            if (!m_State.TryGetValue(sign, out AreaMemory mem))
            {
                mem = new AreaMemory();
                m_State[sign] = mem;
            }
            return mem;
        }

        // ── 8. schedule (ch2 §5.7) ────────────────────────────────────────────────────────────────

        /// <summary>SiteJob (when due) → ResolveJob → ClaimJob (with the car-reserve handshake, WorkerSystem.cs
        /// :450-454 (=): GetCarReserveQueue's deps in, AddCarReserveWriter(handle) out; CBS combines its writers
        /// before the reserve job that drains the queue, :1196-1210, 1.6.2 :1231-1249) → CensusJob (when due).
        /// CensusJob follows ClaimJob, not Dependency: it reads Leisure through a RO lookup while ClaimJob writes it
        /// through a RW type handle (Unity's safety system refuses the two in parallel). Without an area only
        /// ResolveJob runs (every claim an orphan).
        /// Every job is in Dependency and m_PassHandle as soon as it is scheduled (a running `all`, assigned in a
        /// finally): a later schedule that throws can no longer leave ClaimJob (Leisure, TripNeeded RW) in flight
        /// and unknown to ECS and to the R7 catch (fixer, review SIM-2).
        /// <paramref name="timed"/> (the `SkiDays [cost]` pass, once per load and daily at midday): the vanilla jobs
        /// this pass waits on (Dependency: LeisureSystem's job of this frame, the move systems' Transform writers; CBS's
        /// car-reserve writers) are completed first, timed apart (m_CostWaitMs, Round M1 M8) and left out, then our
        /// chain is completed at once; returns its ms (schedule to complete), else −1 (fixer, review JP-2; the parking
        /// census's order, ch3 refutation R13).</summary>
        private double Schedule(bool areas, bool timed)
        {
            uint now = m_Simulation.frameIndex;
            m_PassFrame = now;
            for (int i = 0; i < m_PassStats.Length; i++)
                m_PassStats[i] = 0;
            for (int i = 0; i < m_ClaimMeasure.Length; i++)
                m_ClaimMeasure[i] = 0;   // Round M1: ClaimJob's measure row (no job holds it: step 1 completed the pass)

            long tJobs = 0;
            if (timed)
            {
                // Once per load and daily at midday (M8), the measurement's one main-thread sync (header COST). The wait
                // for the vanilla jobs we depend on is timed apart and printed, never in our number (M8 E6).
                long tWait = Stopwatch.GetTimestamp();
                Dependency.Complete();
                m_Behavior.GetCarReserveQueue(out JobHandle reserveInputs);
                reserveInputs.Complete();
                tJobs = Stopwatch.GetTimestamp();
                m_CostWaitMs = (tJobs - tWait) * 1000.0 / Stopwatch.Frequency;
            }

            JobHandle all = Dependency;
            try
            {
                ScheduleJobs(areas, now, ref all);
            }
            finally
            {
                Dependency = all;
                m_PassHandle = all;
            }
            m_PassInFlight = true;
            if (!timed)
                return -1.0;
            m_PassHandle.Complete();
            return (Stopwatch.GetTimestamp() - tJobs) * 1000.0 / Stopwatch.Frequency;
        }

        /// <summary>Schedule's body. <paramref name="all"/> comes in as Dependency and holds the newest job's handle
        /// the moment that job exists (each job depends on the one before it).</summary>
        private void ScheduleJobs(bool areas, uint now, ref JobHandle all)
        {
            NativeArray<AreaJob> areaArray = m_Areas.AsArray();
            NativeArray<LodgeJob> lodgeArray = m_Lodges.AsArray();
            NativeArray<SiteOut> siteArray = m_SiteOut.AsArray();
            NativeArray<SkiDayRules.ClassTrack> trackArray = m_Tracks.AsArray();
            NativeArray<int> activeArray = m_ActiveNow.AsArray();
            JobHandle h = all;

            if (areas && m_SiteDue)
            {
                m_TransformLookup.Update(this);
                m_GeometryLookup.Update(this);
                m_NodeLookup.Update(this);
                var site = new SiteJob
                {
                    Transforms = m_TransformLookup,
                    Geometries = m_GeometryLookup,
                    Nodes = m_NodeLookup,
                    Areas = areaArray,
                    Lodges = lodgeArray,
                    Runs = m_SiteRuns.AsArray(),
                    Stations = m_SiteStations.AsArray(),
                    Out = siteArray,
                };
                h = site.Schedule(h);
                all = h;
                m_SiteDue = false;
                m_SiteJobs++;
            }

            m_LeisureLookup.Update(this);
            m_CurrentBuildingLookup.Update(this);
            // Round M1 (m1-spec §1.2): ResolveJob's lookups are updated here because it also runs without an area; the
            // `if (areas)` block below updating some of them again is harmless.
            m_CitizenLookup.Update(this);
            m_DeletedLookup.Update(this);
            m_CarKeeperLookup.Update(this);
            m_PersonalCarLookup.Update(this);
            m_TransformLookup.Update(this);
            m_MovingAwayLookup.Update(this);
            m_TravelPurposeLookup.Update(this);
            m_CurrentTransportLookup.Update(this);
            m_CurrentVehicleLookup.Update(this);
            m_DivertLookup.Update(this);
            m_PathOwnerLookup.Update(this);
            m_PrefabRefLookup.Update(this);
            m_ControllerLookup.Update(this);
            m_BicycleLookup.Update(this);
            m_TaxiLookup.Update(this);
            m_PublicTransportLookup.Update(this);
            m_TransportVehicleDataLookup.Update(this);
            m_HealthProblemLookup.Update(this);
            m_HouseholdMemberLookup.Update(this);
            m_ResourceBuyerLookup.Update(this);
            m_TripLookup.Update(this);
            // Round M2: the RW Leisure lookup (R4 b, the pull), the remover-reason lookups (D4: HouseholdNeed is updated here,
            // before ResolveJob, not only in the `if (areas)` block) and the follow's (D5).
            m_LeisureRW.Update(this);
            m_WorkerLookup.Update(this);
            m_StudentLookup.Update(this);
            m_HouseholdNeedLookup.Update(this);
            m_BuildingLookup.Update(this);
            m_OutsideConnectionLookup.Update(this);
            m_PopulationLookup.Update(this);
            m_ResidentLookup.Update(this);
            // Round M2 (D4): the live checks' inputs, read as CitizenBehaviorSystem reads them each update (1.6.0 :1179,
            // :1185-1186; 1.6.2 :1219, :1224-1225); exactly one of each, else LiveKnown 0 (the checks read "not checked").
            bool live = m_EconomyQuery.CalculateEntityCount() == 1 && m_TimeDataQuery.CalculateEntityCount() == 1 &&
                        m_PopulationQuery.CalculateEntityCount() == 1;
            m_ResidentNotes.Clear();
            var resolve = new ResolveJob
            {
                Leisures = m_LeisureRW,
                CurrentBuildings = m_CurrentBuildingLookup,
                AreaOfSign = m_AreaOfSign,
                LodgeIndex = m_LodgeIndex,
                Citizens = m_CitizenLookup,
                Deleteds = m_DeletedLookup,
                CarKeepers = m_CarKeeperLookup,
                PersonalCars = m_PersonalCarLookup,
                Transforms = m_TransformLookup,
                MovingAways = m_MovingAwayLookup,
                TravelPurposes = m_TravelPurposeLookup,
                CurrentTransports = m_CurrentTransportLookup,
                CurrentVehicles = m_CurrentVehicleLookup,
                Diverts = m_DivertLookup,
                PathOwners = m_PathOwnerLookup,
                PrefabRefs = m_PrefabRefLookup,
                Controllers = m_ControllerLookup,
                Bicycles = m_BicycleLookup,
                Taxis = m_TaxiLookup,
                PublicTransports = m_PublicTransportLookup,
                TransportVehicleDatas = m_TransportVehicleDataLookup,
                HealthProblems = m_HealthProblemLookup,
                HouseholdMembers = m_HouseholdMemberLookup,
                ResourceBuyers = m_ResourceBuyerLookup,
                TripNeededs = m_TripLookup,
                Claims = m_Claims,
                Tracks = trackArray,
                Out = m_ResolveOut.AsArray(),
                ActiveNow = activeArray,
                LodgeCarFails = m_LodgeCarFails.AsArray(),
                Measure = m_ResolveMeasure.AsArray(),
                Hours = m_ResolveHours.AsArray(),
                Ring = m_Ring,
                RingState = m_RingState,
                Notes = m_ResidentNotes,
                Now = now,
                TimeOfDay = m_Time.normalizedTime,
                ScanKnown = (byte)(m_Counter.HasLeisureScan && m_Counter.IsArmed ? 1 : 0),
                ScanFrame = m_Counter.LastLeisureScanFrame,
                ScanInterval = (uint)SkiGuestCounterSystem.LeisureScanIntervalFrames,
                PassInterval = (uint)m_LeisureInterval,
                // Round M2 (build-contract.md §5.1): the pull's area snapshot (the same m_Areas ClaimJob gets; empty without
                // an area: every claim is then an orphan and nothing is pulled), the remover-reason and follow inputs, the
                // follow cap and the trip knobs.
                Areas = areaArray,
                Workers = m_WorkerLookup,
                Students = m_StudentLookup,
                HouseholdNeeds = m_HouseholdNeedLookup,
                Buildings = m_BuildingLookup,
                OutsideConnections = m_OutsideConnectionLookup,
                Populations = m_PopulationLookup,
                Residents = m_ResidentLookup,
                Economy = live ? m_EconomyQuery.GetSingleton<Game.Prefabs.EconomyParameterData>() : default,
                TimeData = live ? m_TimeDataQuery.GetSingleton<Game.Common.TimeData>() : default,
                PopulationEntity = live ? m_PopulationQuery.GetSingletonEntity() : Entity.Null,
                LiveKnown = (byte)(live ? 1 : 0),
                FollowingLast = m_FollowingLast,
                FollowCap = Tuning.FollowCap,
                TripKnobs = m_TripKnobs,
            };
            h = resolve.Schedule(h);
            all = h;

            if (areas)
            {
                m_EntityType.Update(ref CheckedStateRef);
                m_CitizenType.Update(ref CheckedStateRef);
                m_LeisureType.Update(ref CheckedStateRef);
                m_TripType.Update(ref CheckedStateRef);
                m_CurrentBuildingType.Update(ref CheckedStateRef);
                m_HouseholdMemberType.Update(ref CheckedStateRef);
                m_UpdateFrameType.Update(ref CheckedStateRef);
                m_HouseholdLookup.Update(this);
                m_PropertyRenterLookup.Update(this);
                m_TouristHouseholdLookup.Update(this);
                m_CommuterLookup.Update(this);
                m_HomelessLookup.Update(this);
                m_MovingAwayLookup.Update(this);
                m_AttendingEventLookup.Update(this);
                m_LodgingSeekerLookup.Update(this);
                m_LodgingProviderLookup.Update(this);
                m_HouseholdNeedLookup.Update(this);
                m_CarKeeperLookup.Update(this);
                m_BicycleOwnerLookup.Update(this);
                m_PersonalCarLookup.Update(this);
                m_ParkedCarLookup.Update(this);
                m_OwnedVehicleLookup.Update(this);
                m_TransformLookup.Update(this);
                m_BuildingLookup.Update(this);
                m_DestroyedLookup.Update(this);
                m_DeletedLookup.Update(this);
                m_LeisureProviderLookup.Update(this);
                m_SeasonPassLookup.Update(this);   // Season passes v1

                NativeList<ArchetypeChunk> chunks = m_CandidateQuery.ToArchetypeChunkListAsync(Allocator.TempJob, out JobHandle chunksHandle);
                NativeQueue<Entity>.ParallelWriter reserve = m_Behavior.GetCarReserveQueue(out JobHandle reserveDeps);   // CBS :1094-1098, 1.6.2 :1126-1130
                var claim = new ClaimJob
                {
                    Chunks = chunks,
                    EntityType = m_EntityType,
                    CitizenType = m_CitizenType,
                    LeisureType = m_LeisureType,
                    TripType = m_TripType,
                    CurrentBuildingType = m_CurrentBuildingType,
                    HouseholdMemberType = m_HouseholdMemberType,
                    UpdateFrameType = m_UpdateFrameType,
                    Households = m_HouseholdLookup,
                    PropertyRenters = m_PropertyRenterLookup,
                    TouristHouseholds = m_TouristHouseholdLookup,
                    CommuterHouseholds = m_CommuterLookup,
                    HomelessHouseholds = m_HomelessLookup,
                    MovingAways = m_MovingAwayLookup,
                    AttendingEvents = m_AttendingEventLookup,
                    LodgingSeekers = m_LodgingSeekerLookup,
                    LodgingProviders = m_LodgingProviderLookup,
                    HouseholdNeeds = m_HouseholdNeedLookup,
                    CarKeepers = m_CarKeeperLookup,
                    BicycleOwners = m_BicycleOwnerLookup,
                    PersonalCars = m_PersonalCarLookup,
                    ParkedCars = m_ParkedCarLookup,
                    OwnedVehicles = m_OwnedVehicleLookup,
                    Transforms = m_TransformLookup,
                    Buildings = m_BuildingLookup,
                    Destroyeds = m_DestroyedLookup,
                    Deleteds = m_DeletedLookup,
                    LeisureProviders = m_LeisureProviderLookup,
                    SeasonPasses = m_SeasonPassLookup,   // Season passes v1
                    SeasonKey = SkiMoneyRules.SeasonKeyOfDate(m_Time.year, m_Time.normalizedDate),
                    Areas = areaArray,
                    Lodges = lodgeArray,
                    Sites = siteArray,
                    Tracks = trackArray,
                    ActiveNow = activeArray,
                    LodgeReject = m_LodgeReject.AsArray(),
                    Out = m_ClaimOut.AsArray(),
                    Claims = m_Claims,
                    PassStats = m_PassStats,
                    Measure = m_ClaimMeasure.AsArray(),
                    CarReserve = reserve,
                    Now = now,
                    TimeOfDay = m_Time.normalizedTime,
                    DayKey = SkiDayRules.DayKey(m_Time.year, m_Time.normalizedDate, m_Time.daysPerYear),
                    Knobs = m_Knobs,
                    Price = m_Price,
                    LeisureInterval = m_LeisureInterval,
                    BehaviorInterval = m_BehaviorInterval,
                    BehaviorOffset = m_BehaviorOffset,
                    TripKnobs = m_TripKnobs,   // Round M2 (R10): FitSpeed and the stay reserve (bit-identical at the defaults)
                };
                h = claim.Schedule(JobHandle.CombineDependencies(h, chunksHandle, reserveDeps));
                all = h;
                m_Behavior.AddCarReserveWriter(h);   // CBS :1100-1103, 1.6.2 :1132-1135
                chunks.Dispose(h);

                if (m_CensusDue)
                    ScheduleCensus(ref all, now);
                // Round M2 (D5): a walker scan every Tuning.NearScanPasses passes with an area, on the hour (ClimateTick)
                // and on the timed `[cost]` pass (OnUpdate), after the census (it reads the claims ClaimJob just appended).
                if (++m_NearPasses >= Tuning.NearScanPasses)
                    m_NearDue = true;
                if (m_NearDue)
                    ScheduleNear(ref all, now, lodgeArray, siteArray);
            }
        }

        /// <summary>Round M2 (D5): NearJob after <paramref name="all"/> (ClaimJob, and the census when due), in the census's
        /// pattern: chunk lists by ToArchetypeChunkListAsync, disposed on the job's handle, read-only type handles updated
        /// here; <paramref name="all"/> becomes its handle right after its Schedule. Per lodge (aligned with m_Lodges) the
        /// walkers within SkiDayTiming.NearLodgeM of the lodge's xz (SiteJob's), and the game's per-stop waiting counts
        /// (Game.Routes.WaitingPassengers on line waypoints with a Game.Routes.Position; taxi stands have none). A chunk
        /// walk, not the moving search tree: a creature on a lane with a LaneObject buffer (every pedestrian lane) is not
        /// in the tree (Game.Creatures/ReferencesSystem.cs:125-138, Game.Prefabs/PedestrianLane.cs:36 (=)). Fixer F1
        /// (review-job-safety-perf): <paramref name="lodges"/> and <paramref name="sites"/> are the arrays ScheduleJobs took
        /// at its top, before SiteJob (which writes m_SiteOut) was scheduled; calling m_SiteOut.AsArray() here, after that
        /// writer is in the chain, is a main-thread access the collection safety checks may reject (editor / development
        /// builds only; unverified which check). The chunk lists are disposed here if Schedule throws.</summary>
        private void ScheduleNear(ref JobHandle all, uint now, NativeArray<LodgeJob> lodges, NativeArray<SiteOut> sites)
        {
            m_NearOut.Clear();
            m_NearOut.Resize(m_Lodges.Length, NativeArrayOptions.ClearMemory);   // the job zeroes it too
            m_ResidentType.Update(ref CheckedStateRef);
            m_TransformType.Update(ref CheckedStateRef);
            m_CurrentVehicleType.Update(ref CheckedStateRef);
            m_DivertType.Update(ref CheckedStateRef);
            m_PathOwnerType.Update(ref CheckedStateRef);
            m_UnspawnedType.Update(ref CheckedStateRef);
            m_WaitingPassengersType.Update(ref CheckedStateRef);
            m_RoutePositionType.Update(ref CheckedStateRef);
            NativeList<ArchetypeChunk> walkers = m_WalkerQuery.ToArchetypeChunkListAsync(Allocator.TempJob, out JobHandle w0);
            NativeList<ArchetypeChunk> stops = m_StopPointQuery.ToArchetypeChunkListAsync(Allocator.TempJob, out JobHandle w1);
            var near = new NearJob
            {
                WalkerChunks = walkers,
                StopChunks = stops,
                ResidentType = m_ResidentType,
                TransformType = m_TransformType,
                CurrentVehicleType = m_CurrentVehicleType,
                DivertType = m_DivertType,
                PathOwnerType = m_PathOwnerType,
                UnspawnedType = m_UnspawnedType,
                WaitingPassengersType = m_WaitingPassengersType,
                RoutePositionType = m_RoutePositionType,
                Lodges = lodges,
                Sites = sites,
                Claims = m_Claims,
                Out = m_NearOut.AsArray(),
                Totals = m_NearTotals,
            };
            JobHandle n;
            try
            {
                n = near.Schedule(JobHandle.CombineDependencies(all, w0, w1));
            }
            catch
            {
                // Hygiene (fixer F1): the TempJob lists would leak; wait for their fill jobs, free them, let the caller's
                // catch report the failure.
                JobHandle.CombineDependencies(w0, w1).Complete();
                walkers.Dispose();
                stops.Dispose();
                throw;
            }
            all = n;
            walkers.Dispose(n);
            stops.Dispose(n);
            m_NearDue = false;
            m_NearPasses = 0;
            m_NearInFlight = true;
            m_NearFrame = now;
            m_NearMinute = SkiDayTiming.MinuteOfDay(m_Time.normalizedTime);
        }

        /// <summary>CensusJob after <paramref name="all"/> (ClaimJob); <paramref name="all"/> becomes the census's
        /// handle right after its Schedule (fixer, SIM-2's gap: the handle was returned only at the end).</summary>
        private void ScheduleCensus(ref JobHandle all, uint now)
        {
            // Resize(ClearMemory) zeroes only the elements past the old length (Unity.Collections UnsafeList<T>.Resize,
            // the game's own copy: `length2 < length` → MemClear of the new tail); emptied first, every element starts at
            // 0 and each census counts one hour, not the sum since the load (fixer, review M-1).
            m_Occupants.Clear();
            m_Occupants.Resize(m_Lodges.Length * Occupant.Kinds, NativeArrayOptions.ClearMemory);
            m_Traveling.Clear();
            m_TouristHouseholdType.Update(ref CheckedStateRef);
            m_HouseholdCitizenType.Update(ref CheckedStateRef);
            m_MeetingType.Update(ref CheckedStateRef);
            m_OutsideConnectionLookup.Update(this);
            m_WorkerLookup.Update(this);
            m_AttendingMeetingLookup.Update(this);
            NativeList<ArchetypeChunk> tourists = m_TouristQuery.ToArchetypeChunkListAsync(Allocator.TempJob, out JobHandle c0);
            NativeList<ArchetypeChunk> occupants = m_OccupantQuery.ToArchetypeChunkListAsync(Allocator.TempJob, out JobHandle c1);
            NativeList<ArchetypeChunk> meetings = m_MeetingQuery.ToArchetypeChunkListAsync(Allocator.TempJob, out JobHandle c2);
            var census = new CensusJob
            {
                TouristChunks = tourists,
                OccupantChunks = occupants,
                MeetingChunks = meetings,
                EntityType = m_EntityType,
                TouristHouseholdType = m_TouristHouseholdType,
                HouseholdCitizenType = m_HouseholdCitizenType,
                CurrentBuildingType = m_CurrentBuildingType,
                HouseholdMemberType = m_HouseholdMemberType,
                MeetingType = m_MeetingType,
                CurrentBuildings = m_CurrentBuildingLookup,
                OutsideConnections = m_OutsideConnectionLookup,
                PropertyRenters = m_PropertyRenterLookup,
                AttendingEvents = m_AttendingEventLookup,
                LodgingSeekers = m_LodgingSeekerLookup,
                MovingAways = m_MovingAwayLookup,
                OwnedVehicles = m_OwnedVehicleLookup,
                Workers = m_WorkerLookup,
                Leisures = m_LeisureLookup,
                AttendingMeetings = m_AttendingMeetingLookup,
                TouristHouseholds = m_TouristHouseholdLookup,
                Areas = m_Areas.AsArray(),
                Lodges = m_Lodges.AsArray(),
                AreaOfSign = m_AreaOfSign,
                LodgeIndex = m_LodgeIndex,
                Counts = m_Census,
                Occupants = m_Occupants.AsArray(),
                Traveling = m_Traveling,
            };
            JobHandle c = census.Schedule(JobHandle.CombineDependencies(all, c0, JobHandle.CombineDependencies(c1, c2)));
            all = c;
            tourists.Dispose(c);
            occupants.Dispose(c);
            meetings.Dispose(c);
            m_CensusDue = false;
            m_CensusInFlight = true;
            m_CensusFrame = now;
        }

        // ── cost (once per load, then daily at midday; ch2 §11, L13; Round M1 M8) ──────────────────

        /// <summary>Is this pass the synchronous one (once per load, then once per re-arm)? After CostPasses passes with work, the first pass
        /// that follows a pass whose candidate walk ran (the walk is the cost to measure: a night, closed, knobs-off
        /// or capped pass has none, and in a new city the early passes are NoLodge); after CostWaitPasses more passes
        /// with work without one, the next pass anyway, so L13 is never missing (fixer, review JP-1). The caller
        /// also schedules the census on it (JP-1: CensusJob is the other worker cost of ch2 §11).</summary>
        private bool CostTimedDue()
        {
            if (m_CostLogged || m_CostPasses < Tuning.CostPasses)
                return false;
            return m_LastPassStats[Stat.WalkRan] != 0 || m_CostWait >= Tuning.CostWaitPasses;
        }

        /// <summary>`SkiDays [cost]`, once per load and once per midday re-arm (MaybeRearmCost). Only passes that scheduled ClaimJob count (F7 E3); the first of
        /// them is left out of mean and max (one-off Mono JIT of Schedule, the rebuild that found the area, 24 climate
        /// samples after a load: the attraction system's E1 F1 precedent; fixer, review M-2). On the timed pass
        /// (CostTimedDue) Schedule completed this pass's jobs at once and returned <paramref name="jobsMs"/>; the
        /// results are applied next pass as on any other pass.</summary>
        private void RecordCost(double ms, bool work, bool timed, double jobsMs)
        {
            if (work && !timed)
            {
                // Round M1 M8 (F10 alternative): the running day cost, before the early return below (E5). The timed
                // pass is left out: its number holds the synchronous wait of the measurement itself.
                m_CityThis.CostSumMs += ms;
                m_CityThis.CostPasses++;
                if (ms > m_CityThis.CostMaxMs)
                {
                    m_CityThis.CostMaxMs = ms;
                    m_CityThis.CostMaxMinute = SkiDayTiming.MinuteOfDay(m_Time.normalizedTime);   // Round M2 (D8): its clock
                }
            }
            if (m_CostLogged || !work)
                return;
            if (!timed)
            {
                if (!m_CostFirstSkipped)
                {
                    m_CostFirstSkipped = true;
                }
                else if (m_CostPasses < Tuning.CostPasses)
                {
                    m_CostPasses++;
                    m_CostSumMs += ms;
                    if (ms > m_CostMaxMs)
                        m_CostMaxMs = ms;
                }
                else
                {
                    m_CostWait++;
                }
                return;
            }
            m_CostLogged = true;
            m_CostMeasurements++;
            float t = m_Time.normalizedTime;
            if (t >= 0.5f)
                m_CostArmDay = SkiDayRules.DayKey(m_Time.year, m_Time.normalizedDate, m_Time.daysPerYear);   // today's midday is done
            Mod.Log.Info(Invariant($"SkiDays [cost] main thread per pass: mean {m_CostSumMs / Math.Max(1, m_CostPasses):F3} ms, max {m_CostMaxMs:F3} ms over ") +
                         Invariant($"{m_CostPasses} passes with work; snapshot rebuilds {m_Rebuilds} (mean {m_RebuildSumMs / Math.Max(1L, m_Rebuilds):F3} ms, ") +
                         Invariant($"max {m_RebuildMaxMs:F3} ms); last pass: {m_PassStats[Stat.Chunks]} chunks, {m_PassStats[Stat.Walked]} citizens walked, ") +
                         Invariant($"{m_PassStats[Stat.FreshLocals] + m_PassStats[Stat.FreshTourists]} fresh decisions, {m_Claims.Length} ski days tracked; ") +
                         Invariant($"one synchronous pass (schedule to complete; also about the longest a game job that writes a type we read waits ") +
                         Invariant($"on a pass frame): {jobsMs:F3} ms; waited first for vanilla jobs {m_CostWaitMs:F3} ms (not ours); ") +
                         Invariant($"measurement {m_CostMeasurements} this load, {MonthText(SkiMoneyRules.MonthKey(m_Time.year, m_Time.normalizedDate))} at ") +
                         ClockText(SkiDayTiming.MinuteOfDay(t)));
        }

        /// <summary>Round M1 M8 (U8, F10): once the previous `[cost]` line is written, re-arm the measurement at the
        /// first pass at or after 12:00 of a game day it has not run on (midday: claims, scans and lots are busy; the
        /// midnight roll is the quietest time and carries the `[day]` and `[powder]` work). The arming pass itself is not
        /// measured (E5, the house skip of the first pass: RecordCost leaves out the next pass with work).</summary>
        private void MaybeRearmCost()
        {
            if (!m_CostLogged || !(m_Time.normalizedTime >= 0.5f))
                return;
            int day = SkiDayRules.DayKey(m_Time.year, m_Time.normalizedDate, m_Time.daysPerYear);
            if (day == m_CostArmDay)
                return;
            m_CostLogged = false;
            m_CostPasses = m_CostWait = 0;
            m_CostSumMs = m_CostMaxMs = 0.0;
            m_CostFirstSkipped = false;
            m_CostArmDay = day;
            m_CostByRearm = true;   // Round M2 (D10): a day change before its line writes it partial (CostDayEnd)
        }

        /// <summary>Round M2 (D10, measure-core 2.10): every pass, before the idle returns — a midday-re-armed measurement
        /// that has not written its line when the game day changes is written partial (SkiDayRules.CostDayEnded), so a
        /// day with no walk after noon (an area never Open, the knobs off, the cap) is never silent. m_CostArmDay stays
        /// the re-arm day, so the next noon re-arms. The load's measurement keeps its own rule (its CostWaitPasses
        /// fallback guarantees a line).</summary>
        private void CostDayEnd()
        {
            int day = SkiDayRules.DayKey(m_Time.year, m_Time.normalizedDate, m_Time.daysPerYear);
            if (!SkiDayRules.CostDayEnded(m_CostLogged, m_CostByRearm, m_CostArmDay, day))
                return;
            CostPartialLine("no pass with work since noon", "no walk since noon");
        }

        /// <summary>Fixer S3/F2: the R9 per-pass stop ends any `[cost]` measurement that has not written its line (the
        /// load's or a midday re-arm's): after m_Armed = false neither RecordCost nor CostDayEnd runs again this load,
        /// so it is written skipped or partial here, as CostDayEnd writes a day change.</summary>
        private void CostStopped()
        {
            if (m_CostLogged)
                return;
            string which = m_CostByRearm ? "the midday measurement" : "the load's measurement";
            CostPartialLine($"ski days stopped before {which} ended", $"ski days stopped before {which} ended");
        }

        /// <summary>The skipped (no pass with work) or partial `[cost]` line of a measurement cut short (CostDayEnd,
        /// CostStopped); marks it logged.</summary>
        private void CostPartialLine(string noWork, string noTimed)
        {
            m_CostLogged = true;
            m_CostMeasurements++;
            string when = Invariant($"measurement {m_CostMeasurements} this load, {MonthText(SkiMoneyRules.MonthKey(m_Time.year, m_Time.normalizedDate))} at ") +
                          ClockText(SkiDayTiming.MinuteOfDay(m_Time.normalizedTime));
            if (m_CostPasses == 0)
            {
                Mod.Log.Info("SkiDays [cost] skipped: " + noWork + "; " + when);
                return;
            }
            var areas = new StringBuilder(64);
            for (int i = 0; i < m_DumpSigns.Count; i++)
            {
                Entity sign = m_DumpSigns[i];
                if (!m_State.TryGetValue(sign, out AreaMemory mem) || !mem.HasPass)
                    continue;
                areas.Append(areas.Length > 0 ? ", " : "").Append(Fmt(sign)).Append(' ').Append((SkiDayRules.AreaState)mem.State);
            }
            Mod.Log.Info(Invariant($"SkiDays [cost] main thread per pass: mean {m_CostSumMs / m_CostPasses:F3} ms, max {m_CostMaxMs:F3} ms over {m_CostPasses} ") +
                         "passes with work" + (m_CostPasses < Tuning.CostPasses ? Invariant($" (fewer than {Tuning.CostPasses})") : "") +
                         Invariant($"; snapshot rebuilds {m_Rebuilds} (mean {m_RebuildSumMs / Math.Max(1L, m_Rebuilds):F3} ms, max {m_RebuildMaxMs:F3} ms); ") +
                         "one synchronous pass: skipped: " + noTimed + " (ski areas at the last pass: " + (areas.Length > 0 ? areas.ToString() : "none") +
                         "); " + when);
        }

        // ── logs (ch2 §10) ────────────────────────────────────────────────────────────────────────

        /// <summary>L4, entering idle (once per transition; the folds are flushed first).</summary>
        private void SetIdle()
        {
            if (m_Idle)
                return;
            m_Idle = true;
            m_IdleReason = IdleNoArea;
            FlushLogs(all: true);
            Mod.Log.Info("SkiDays [idle]: no ski area — nothing to send");
        }

        /// <summary>L4, leaving idle.</summary>
        private void ClearIdle()
        {
            m_IdleReason = null;
            if (!m_Idle)
                return;
            m_Idle = false;
            Mod.Log.Info($"SkiDays [idle]: {m_Areas.Length} ski area(s) back");
        }

        /// <summary>L7 / L8: the first claim and the first arrival per ski area per load.</summary>
        private void FirstLines(AreaMemory mem, Entity sign, in ClaimOut co, in ResolveOut ro)
        {
            if (co.HasFirst != 0 && !mem.FirstClaimLogged)
            {
                mem.FirstClaimLogged = true;
                Mod.Log.Info(Invariant($"SkiDays [first] ski area {Fmt(sign)}") + NameSuffix(sign) +
                             Invariant($": first ski day this load — {KindText(co.FirstKind)} by {ClassText(co.FirstClass)} to lodge {Fmt(co.FirstLodge)}, ") +
                             Invariant($"{co.FirstDistM / 1000.0:F1} km, chance {co.FirstChance:F3}"));
            }
            if (ro.HasFirst != 0 && !mem.FirstArrivalLogged)
            {
                mem.FirstArrivalLogged = true;
                Mod.Log.Info(Invariant($"SkiDays [first] ski area {Fmt(sign)}") + NameSuffix(sign) +
                             Invariant($": first arrival this load — {KindText(ro.FirstKind)} by {ClassText(ro.FirstClass)} after {ro.FirstHours:F1} game hours"));
            }
        }

        /// <summary>L9: the car track's "cannot reach" turned on, and (Round M1 M6, D2) off: hub row 20
        /// (HubProblemCode.SkiDaysCannotReach). The row is named by the text the active dictionary holds for it (fixer
        /// L1: the D1 rewording waits for a ruling, integration.md §5 open point 1, so the line follows whichever text
        /// ships), plus its code. The percentage is the track's faded history (×0.975 per outcome, about the last 40;
        /// fixer L5), the count the outcomes since the load. Folded through m_ReachLog. The row also clears without a
        /// line at a load (tracks reset), when a ski area is dropped at a roll (its `[area]` lines show that; m1-spec
        /// E15) and when ski days are switched off (the `[settings]` line shows that).</summary>
        private void ReachLine(AreaMemory mem, Entity sign, Entity reachLodge, bool on)
        {
            SkiDayRules.ClassTrack car = mem.Tracks[(int)SkiDayRules.TravelClass.Car];
            double total = car.Arrived + car.Failed;
            double pct = total > 0.0 ? 100.0 * car.Arrived / total : 0.0;
            double conf = SkiDayRules.Confidence(SkiDayRules.TravelClass.Car, car);
            int window = (int)Math.Round(1.0 / (1.0 - SkiDayRules.TrackKeep));
            Offer(m_ReachLog, "reach",
                  Invariant($"SkiDays [reach] ski area {Fmt(sign)}") + NameSuffix(sign) +
                  Invariant($": recent car ski days arrived {pct:F0} % (weighted over about the last {window}; {car.Resolved} resolved this load; ") +
                  Invariant($"confidence {conf:F2}, on below {SkiDayRules.CannotReachBelow:F2}, off from {SkiDayRules.CannotReachClearAt:F2}; most failures: lodge ") +
                  (reachLodge == Entity.Null ? "-" : Fmt(reachLodge)) + ") — hub row \"" + ReachRowText() + "\" (SkiDaysCannotReach) " + (on ? "on" : "off"));
        }

        /// <summary>The hub row's text as the active dictionary holds it (the SkiResortStatsSystem.Lookup precedent);
        /// "?" when no dictionary is loaded. Main thread (ApplyPass).</summary>
        private static string ReachRowText()
        {
            var dictionary = Game.SceneFlow.GameManager.instance?.localizationManager?.activeDictionary;
            return dictionary != null && dictionary.TryGetValue(ReachRowKey, out string text) && !string.IsNullOrEmpty(text) ? text : "?";
        }

        private const string ReachRowKey = "SkiResort.Hub.PROBLEM[SkiDaysCannotReach]";

        /// <summary>L5: an area state change; Full has its own line (Round M2, D10); OutOfHours reads as the state before
        /// it (SkiDayRules.AreaLogKey: the claim window closing on a full hill is not "room again"). Written only when the
        /// key changes (AreaMemory.StateKey); a Full ↔ Open flicker at the cap folds through m_AreaLog (one line plus a
        /// summary per 10 s). <paramref name="onHill"/> = this area's claims on the way and on site.</summary>
        private void StateLine(AreaMemory mem, Entity sign, SkiDayRules.AreaState state, int onHill, int capacity, SkiDayLimit limit, int minute)
        {
            byte key = SkiDayRules.AreaLogKey(state, mem.StateKey);
            if (key == mem.StateKey)
                return;
            mem.StateKey = key;
            string text;
            switch (key)
            {
                case SkiDayRules.LogKeyWaiting:
                    text = "waiting for its first score";
                    break;
                case SkiDayRules.LogKeyNoEfficiency:
                    text = "closed — Welcome Sign efficiency 0 %";
                    break;
                case SkiDayRules.LogKeyNoOpenRun:
                    text = "closed — no counted run";
                    break;
                case SkiDayRules.LogKeyNoLodge:
                    text = "closed — no ski-day lodge";
                    break;
                case SkiDayRules.LogKeyFull:
                    text = Invariant($"full at {ClockText(minute)} — {onHill} on the way or on site, capacity {capacity} ({limit}); ") +
                           "no new ski days until there is room";
                    break;
                default:   // Open (and OutOfHours after Open)
                    text = "open for ski days";
                    break;
            }
            Offer(m_AreaLog, "area", $"SkiDays [area] ski area {Fmt(sign)}{NameSuffix(sign)}: {text}");
        }

        /// <summary>Round M2 (R4 b, build-contract.md §6.6): one line per closure edge per area (ClosingEdgeOf: 1 no counted
        /// run, 2 the night run closed), with that pass's pulls (ResolveOut.Pulled, the last PulledTo). Written directly
        /// (fixer S2/F1), NOT through the `[area]` window: StateLine offers the NoOpenRun / NoEfficiency `[area]` line on
        /// the same pass, so a windowed `[closing]` line was always folded, and two areas closing inside one 10 s window
        /// (a thaw is city-wide) kept only the last text, losing R4's once-per-edge pulled count. The volume is bounded
        /// without a window: each edge needs the hill (or the night run) seen open again first (AreaMemory
        /// HillOpenSeen / NightOpenSeen, SkiDayRules.ClosingEdgeOf). HH:MM = the clock at PulledTo from this apply's clock
        /// and frame; "the clock jumped this pass" when the
        /// pass's hour step was neither 0 nor 1 (a developer time jump moves every closing frame: a pull it fires is
        /// accepted and named, ruling R4).</summary>
        private void ClosingLine(Entity sign, byte edge, in ResolveOut ro, int minute, uint nowFrame)
        {
            string why = edge == 2 ? "the night run closed" : "no counted run";
            string head = $"SkiDays [closing] ski area {Fmt(sign)}{NameSuffix(sign)}: the hill closed early ({why}); ";
            string tail = "later arrivals today are pulled in when they arrive" + (m_HourStep != 0 && m_HourStep != 1 ? "; the clock jumped this pass" : "");
            string body = ro.Pulled > 0
                ? Invariant($"{ro.Pulled} guest(s) on site now leave by {ClockText(SkiDayTiming.ClockMinuteAfter(minute, nowFrame, ro.PulledTo))} (deadline pulled ") +
                  Invariant($"in to the closing, at least {SkiDayTiming.HoursFor(SkiDayTiming.OnSiteGraceFrames):F0} h from now; Leisure.m_LastPossibleFrame lowered); ")
                : "no guest on site to pull in; ";
            Mod.Log.Info(head + body + tail);
        }

        /// <summary>L6: the area's set of (lodge, verdict) changed (the first pass always writes it).</summary>
        private void LodgesLine(AreaMemory mem, Entity sign, in AreaJob aj)
        {
            int start = Math.Max(0, aj.LodgeStart), end = Math.Min(aj.LodgeStart + aj.LodgeCount, m_DumpLodges.Count);
            bool same = mem.LodgesLogged && mem.LodgeVerdicts.Count == end - start;
            for (int li = start; same && li < end; li++)
            {
                LodgeVerdict lv = mem.LodgeVerdicts[li - start];
                same = lv.Lodge == m_DumpLodges[li].Lodge && lv.Reject == m_DumpLodges[li].Reject;
            }
            if (same)
                return;
            mem.LodgesLogged = true;
            mem.LodgeVerdicts.Clear();
            // Catalog Phase B (header CATALOG PHASE B): a passing mountain lodge holds skiers but is not a ski-day (target)
            // lodge — listed apart; a tier other than the A-frame is named.
            // An A-frame-only area writes today's text.
            int targets = 0, mountain = 0;
            var sb = new StringBuilder(160);
            var mountainText = new StringBuilder(0);
            for (int li = start; li < end; li++)
            {
                DumpLodge dl = m_DumpLodges[li];
                mem.LodgeVerdicts.Add(new LodgeVerdict { Lodge = dl.Lodge, Reject = dl.Reject });
                if (dl.Reject != (byte)SkiDayRules.LodgeReject.None)
                    continue;
                StringBuilder into = dl.NoRoadNeeded ? mountainText : sb;
                if (dl.NoRoadNeeded)
                    mountain++;
                else
                    targets++;
                into.Append(' ').Append(Fmt(dl.Lodge));
                if (dl.LodgeIndex > 0)
                    into.Append(' ').Append(TierOf(dl.LodgeIndex));
            }
            string text = $"SkiDays [lodges] ski area {Fmt(sign)}{NameSuffix(sign)}: {targets} ski-day lodge(s){sb}" +
                          (mountain > 0 ? $"; {mountain} mountain lodge(s) holding skiers, not a ski-day target:{mountainText}" : "") +
                          $"; not used: {end - start - targets - mountain}";
            sb.Clear();
            for (int li = start; li < end; li++)
            {
                DumpLodge dl = m_DumpLodges[li];
                if (dl.Reject == (byte)SkiDayRules.LodgeReject.None)
                    continue;
                sb.Append("; lodge ").Append(Fmt(dl.Lodge)).Append(' ').Append((SkiDayRules.LodgeReject)dl.Reject);
                if (dl.Reject == (byte)SkiDayRules.LodgeReject.NotSlopeside)
                    sb.Append(" (").Append(MetresText(dl.NearestM)).Append(" m)");
            }
            Offer(m_LodgesLog, "lodges", text + sb);
        }

        /// <summary>L17: a settings change (folded: a slider drag writes one line per value otherwise).</summary>
        private void SettingsLine(SkiDayRules.Knobs k, int price)
        {
            Offer(m_SettingsLog, "settings", $"SkiDays [settings] locals {k.LocalsPct}%, tourists {k.TouristsPct}%, reach {k.ReachKm} km, " +
                                             $"powder pull {k.PowderPct}%, ticket {price}");
        }

        private static void Offer(LogWindow window, string tag, string text)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            switch (window.Offer(now, false, text))
            {
                case LogWindow.Verdict.Plain:
                    Mod.Log.Info(text);
                    break;
                case LogWindow.Verdict.Summary:
                    WriteFold(window, tag, now);
                    break;
            }
        }

        private static void WriteFold(LogWindow window, string tag, float now)
        {
            Mod.Log.Info(Invariant($"SkiDays [{tag}] {window.Count} more change(s) in {window.SpanS(now):F0} s, folded (one line per 10 s) — last: ") +
                         window.LastText);
            window.Wrote(now);
        }

        /// <summary>The trailing fold summaries (on the pass: silence impossible), or every waiting fold
        /// (<paramref name="all"/>: OnGamePreload, OnDestroy, before an idle line).</summary>
        private void FlushLogs(bool all)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            Flush(m_AreaLog, "area", now, all);
            Flush(m_LodgesLog, "lodges", now, all);
            Flush(m_SettingsLog, "settings", now, all);
            Flush(m_ReachLog, "reach", now, all);
        }

        private static void Flush(LogWindow window, string tag, float now, bool all)
        {
            if (all ? window.Count > 0 : window.TrailingDue(now))
                WriteFold(window, tag, now);
        }

        // ── DUMP (ch2 §10; managed copies only) ───────────────────────────────────────────────────

        private void AppendDumpBody(StringBuilder sb)
        {
            CultureInfo ic = CultureInfo.InvariantCulture;
            string recent = !m_LastPassKnown ? "n/a"
                : SkiDayTiming.RecentBehaviorSlice(m_LastPassFrame, m_BehaviorInterval, m_BehaviorOffset, out uint _) ? "every pass" : "never";
            // Round M2: the trip systems (R9), "tracked" = on the way + on site + visit over + expired and watched + followed
            // after leaving + sent this pass exactly (D9, Decided here 12: ClaimJob appends this pass's claims after ResolveJob
            // counted its buckets); "reached the lodge without Leisure" now counts only citizens that still exist (R6): they
            // are booked as arrivals, visit ended "removed (on arrival)" (a deleted citizen in the lodge stays a failure).
            sb.Append("  Ski days: armed ").Append(m_Armed).Append(", disabled ").Append(m_Disabled)
              .Append(", interval ").Append(m_LeisureInterval).Append(" (TripNeededSystem ").Append(m_TripInterval)
              .Append(", CitizenBehaviorSystem ").Append(m_BehaviorInterval).Append('+').Append(m_BehaviorOffset)
              .Append(", recent CBS slice skipped ").Append(recent)
              .Append("; trip systems ").Append(SkiTripSystems.Short(m_TripFound, m_TripOn, SkiOtherMods.TripPlannerMask))
              .Append(m_TripChanges > 0 ? ", changed " + m_TripChanges.ToString(ic) + " times during play" : "")
              .Append("), passes ").Append(m_PassesLoad)
              .Append(" (walked ").Append(m_WalkPassesLoad).Append("), month ")
              .Append(m_Month.Key == SkiDayRules.NoMonth ? "none" : MonthText(m_Month.Key)).Append(SinceText())
              .Append(", last month ").Append(m_Month.LastKnown ? (m_Month.LastSinceLoad ? "known (partial)" : "known (full)") : "unknown")
              .Append("; tracked ").Append(m_TrackedLast).Append(" (on the way ").Append(m_OnTheWayLast).Append(", on site ").Append(m_OnSiteLast)
              .Append(", visit over ").Append(m_LeavingLast).Append(", expired and watched ").Append(m_WatchingLast)
              .Append(", followed after leaving ").Append(m_FollowingLast).Append(", sent this pass ").Append(m_SentLast)
              .Append("); this load: ski days ").Append(m_LoadSent).Append(" (cars enqueued ").Append(m_LoadCars).Append("), arrived ").Append(m_LoadArrived)
              .Append(", gone ").Append(m_LoadGone).Append(", retargeted ").Append(m_LoadRetargeted).Append(" (").Append(m_LoadRetargetedAtOrigin)
              .Append(" at the origin), expired ").Append(m_LoadExpired).Append(", left ").Append(m_LoadLeft)
              .Append(", reached the lodge without Leisure ").Append(m_LoadArrivedNoLeisure).Append(" (booked as arrivals), orphaned ").Append(m_LoadOrphaned)
              .Append("; snapshot rebuilds ").Append(m_Rebuilds).Append(", site runs ").Append(m_SiteJobs)
              .Append(", pass cost today: ").Append(m_CityThis.CostPasses > 0
                  ? Invariant($"mean {m_CityThis.CostSumMs / m_CityThis.CostPasses:F3} ms, max {m_CityThis.CostMaxMs:F3} ms at {ClockText(m_CityThis.CostMaxMinute)} ") +
                    Invariant($"over {m_CityThis.CostPasses} passes")
                  : "no pass with work yet")
              .Append("; last ledger check: ").Append(m_LastLedgerCheck ?? "none yet")
              .Append("; idle '").Append(m_IdleReason ?? "").Append("'\n");

            // climate
            int wet = 0;
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            for (int k = 0; k < SkiDayRules.SnowHourSamples; k++)
            {
                if (m_Precip[k] >= SkiDayRules.SnowPrecipMin)
                    wet++;
                min = Math.Min(min, m_Ambient[k]);
                max = Math.Max(max, m_Ambient[k]);
            }
            sb.Append("  Ski days climate: hour ").Append(m_HourKey).Append(", ring ").Append(m_RingValid ? "valid" : "invalid")
              .Append(", precipitation >= 0.1 in ").Append(m_RingValid ? wet : 0).Append(" of 24 h, ambient ")
              .Append(m_RingValid ? min.ToString("F1", ic) : "-").Append("..").Append(m_RingValid ? max.ToString("F1", ic) : "-")
              .Append(" C at ").Append(m_BaseHeight.ToString("F0", ic)).Append(" m, lapse ").Append(m_Lapse.ToString(ic))
              .Append(" C/m, freezing ").Append(m_Freezing.ToString("F1", ic)).Append(" C\n");

            // tourists
            if (!m_CensusKnown)
            {
                sb.Append("  Ski days tourists: no census yet\n");
            }
            else
            {
                int[] c = m_CensusLast;
                sb.Append("  Ski days tourists (").Append(CensusAgeH().ToString("F1", ic)).Append(" game h ago): parties ").Append(c[Census.Parties])
                  .Append(", people ").Append(c[Census.People]).Append(", at a map-edge connection ").Append(c[Census.AtEdge])
                  .Append(", with a hotel ").Append(c[Census.Hotel]).Append(" (in it now ").Append(c[Census.InHotel]).Append("), other lodging ")
                  .Append(c[Census.OtherLodging]).Append(", no hotel ").Append(c[Census.NoHotel]).Append(", outing used ").Append(c[Census.OutingUsed])
                  .Append(", with a car ").Append(c[Census.WithCar]).Append(", lodging seekers ").Append(c[Census.LodgingSeekers])
                  .Append(", moving away ").Append(c[Census.MovingAway]).Append('\n');
            }

            // last pass
            int[] s = m_LastPassStats;
            sb.Append("  Ski days last pass (frame ").Append(m_LastPassKnown ? m_LastPassFrame.ToString(ic) : "-").Append("): chunks ").Append(s[Stat.Chunks])
              .Append(" (skipped LS ").Append(s[Stat.SkippedLeisure]).Append(", CBS ").Append(s[Stat.SkippedBehavior]).Append("), walked ").Append(s[Stat.Walked])
              .Append("; rejects NotDeciding ").Append(s[Stat.RejectBase + 1]).Append(" Busy ").Append(s[Stat.RejectBase + 2])
              .Append(" Ill ").Append(s[Stat.RejectBase + 3]).Append(" TripsPending ").Append(s[Stat.RejectBase + 4])
              .Append(" NotInBuilding ").Append(s[Stat.RejectBase + 5]).Append(" HouseholdExcluded ").Append(s[Stat.RejectBase + 6])
              .Append(" NotAtHome ").Append(s[Stat.RejectBase + 7]).Append(" OutingNotSpent ").Append(s[Stat.RejectBase + 8])
              .Append("; shopping ").Append(s[Stat.ShoppingNeed]).Append(", leaving today ").Append(s[Stat.LeavingToday])
              .Append("; fresh locals ").Append(s[Stat.FreshLocals]).Append(" tourists ").Append(s[Stat.FreshTourists])
              .Append(" (with a car ").Append(s[Stat.FreshWithCar]).Append("); ski days ").Append(s[Stat.Claimed]).Append('\n');

            // per area, per lodge
            for (int a = 0; a < m_DumpSigns.Count; a++)
            {
                Entity sign = m_DumpSigns[a];
                if (!m_State.TryGetValue(sign, out AreaMemory mem) || !mem.HasPass)
                    continue;
                SkiDayRules.ClassTrack tw = mem.Tracks[(int)SkiDayRules.TravelClass.Walk];
                SkiDayRules.ClassTrack tc = mem.Tracks[(int)SkiDayRules.TravelClass.Car];
                SkiDayRules.ClassTrack tn = mem.Tracks[(int)SkiDayRules.TravelClass.NoCarFar];
                sb.Append("    ski area ").Append(Fmt(sign)).Append(NameSuffix(sign)).Append(": state ").Append((SkiDayRules.AreaState)mem.State)
                  .Append(", A ").Append(mem.Attraction).Append(", open share ").Append(mem.OpenShare.ToString("F2", ic))
                  .Append(", powder ").Append(double.IsNaN(mem.Powder) ? "-" : mem.Powder.ToString("F2", ic))
                  .Append(" (").Append(mem.SnowHours >= 0 ? mem.SnowHours.ToString(ic) : "-").Append(" h, top ")
                  .Append(mem.TopKnown ? mem.TopM.ToString("F0", ic) : "-").Append(" m), claim hours ").Append(mem.ClaimHours ? "y" : "n")
                  .Append(" until ").Append(HourText(SkiDayRules.CloseHourOf(mem.NightRunOpen)))
                  .Append(", capacity ").Append(mem.Capacity).Append(" (").Append((SkiDayLimit)mem.Limit).Append(": runs ")
                  // Catalog Phase B (was "lodges N x 360"): the ski-day (target) lodges and the lodges' per-tier
                  // capacity; the price tolerance and the school factor of the chance.
                  .Append(mem.RunsCapacity.ToString("F0", ic)).Append(", lodges ").Append(mem.TargetLodges).Append(" ski-day, ")
                  .Append(mem.LodgeCapacity).Append(" skiers a day by tier (A-frame ").Append(SkiDayRules.LodgeSkiersPerDay.ToString("F0", ic))
                  .Append(")), price tolerance ").Append(mem.PriceTolerance).Append(", school x").Append(mem.SchoolFactor.ToString("0.##", ic))
                  .Append(", on the way ").Append(mem.OnTheWay)
                  .Append(" (not left home ").Append(mem.NotLeft).Append(", at the door ").Append(mem.AtDoor).Append(", riding ").Append(mem.Riding)
                  .Append("), on site ").Append(mem.OnSite)
                  .Append(", visit over and still inside ").Append(mem.Leaving).Append(", expired and watched ").Append(mem.Watching)
                  .Append(", followed after leaving ").Append(mem.Following)
                  .Append("; this month sent ").Append(mem.This.Sent).Append(" arrived ").Append(mem.This.Arrived)
                  .Append(", arrived after the hill closed ").Append(mem.This.ArrivedHillClosed)
                  .Append("; confidence walk ").Append(SkiDayRules.Confidence(SkiDayRules.TravelClass.Walk, tw).ToString("F2", ic))
                  .Append(" car ").Append(SkiDayRules.Confidence(SkiDayRules.TravelClass.Car, tc).ToString("F2", ic))
                  .Append(" no car ").Append(SkiDayRules.Confidence(SkiDayRules.TravelClass.NoCarFar, tn).ToString("F2", ic))
                  .Append(" (car resolved ").Append(tc.Resolved).Append(", cannot reach ").Append(tc.CannotReachShown ? "y" : "n")
                  .Append("); speeds walk ").Append(tw.SpeedKmPerHour.ToString("F2", ic)).Append(" car ").Append(tc.SpeedKmPerHour.ToString("F2", ic))
                  .Append(" no car ").Append(tn.SpeedKmPerHour.ToString("F2", ic))
                  .Append("; all-outcomes speeds walk ").Append(tw.SpeedAllKmPerHour.ToString("F2", ic)).Append(" car ")
                  .Append(tc.SpeedAllKmPerHour.ToString("F2", ic)).Append(" no car ").Append(tn.SpeedAllKmPerHour.ToString("F2", ic))
                  .Append(m_TripKnobs.FitUsesAllOutcomes ? " (the fit check uses all outcomes)" : " (the fit check uses arrivals only)")
                  .Append("; outings on the way ");
                if (m_CensusKnown)
                {
                    sb.Append(mem.OutingsOnTheWay).Append(", oldest ").Append(mem.OldestOutingH.ToString("F1", ic)).Append(" game h (census ")
                      .Append(CensusAgeH().ToString("F1", ic)).Append(" h ago; ages count from the first census after the load)");
                }
                else
                {
                    sb.Append("? (no census yet)");
                }
                // Round M1 M9: the year table
                if (mem.PowderYearLogged == int.MinValue)
                    sb.Append("; powder year not computed yet\n");
                else
                    sb.Append("; powder year ").Append(MonthList(mem.PowderYearMarks)).Append(" (top ").Append(mem.PowderYearTopM.ToString("F0", ic))
                      .Append(" m, ").Append(mem.PowderYearWhen).Append(")\n");
                // Round M1 (§3.2): this month's measure lines (fixer L7: the three parts), and its failures by place,
                // residents / tourists; fixer R3: last month's too, so a roll does not lose the split
                string[] parts = MeasureParts(mem, mem.This, mem.MThis, mem.HThis, mem.CThis, m_LeisureInterval, m_TripKnobs);
                if (parts == null)
                {
                    sb.Append("      measure this month: nothing measured\n");
                }
                else
                {
                    for (int p = 0; p < parts.Length; p++)
                    {
                        if (parts[p].Length > 0)
                            sb.Append("      measure this month, ").Append(s_MeasurePartWords[p]).Append(": ").Append(parts[p]).Append('\n');
                    }
                }
                sb.Append("      failures this month by place, resident / tourist: ").Append(FailPlacesText(mem.MThis)).Append('\n');
                if (m_Month.LastKnown)
                    sb.Append("      failures last month by place, resident / tourist: ").Append(FailPlacesText(mem.MLast)).Append('\n');

                for (int li = 0; li < m_DumpLodges.Count; li++)
                {
                    DumpLodge dl = m_DumpLodges[li];
                    if (dl.Area != a)
                        continue;
                    m_CarFailsByLodge.TryGetValue(dl.Lodge, out int fails);
                    sb.Append("      lodge ").Append(Fmt(dl.Lodge)).Append(": ").Append((SkiDayRules.LodgeReject)dl.Reject)
                      // Catalog Phase B: the tier, its capacity, mountain (not a target).
                      .Append(", ").Append(TierOf(dl.LodgeIndex)).Append(' ').Append(dl.Capacity).Append(" a day")
                      .Append(dl.NoRoadNeeded ? ", mountain (not a ski-day target)" : "")
                      .Append(", nearest run or station ").Append(MetresText(dl.NearestM)).Append(" m, xz (")
                      .Append(dl.HasPosition ? dl.Xz.x.ToString("F0", ic) : "-").Append(", ").Append(dl.HasPosition ? dl.Xz.y.ToString("F0", ic) : "-")
                      .Append("), car failures ").Append(fails).Append(", parking ")
                      .Append(dl.ParkingKnown && dl.ParkingDone ? dl.CarSpaces.ToString(ic) : "?").Append("; occupants ");
                    if (m_OccupantsByLodge.TryGetValue(dl.Lodge, out Occupants o))
                    {
                        sb.Append(o.Workers + o.Residents + o.Tourists + o.OutingMembers + o.Other).Append(": workers ").Append(o.Workers)
                          .Append(", residents on leisure ").Append(o.Residents).Append(", tourists on leisure ").Append(o.Tourists)
                          .Append(", outing members ").Append(o.OutingMembers).Append(", other ").Append(o.Other).Append('\n');
                    }
                    else
                    {
                        sb.Append("? (no census of this lodge yet)\n");
                    }
                    AppendNearLodge(sb, dl.Lodge);   // Round M2 (D5)
                }
            }
            AppendRing(sb);
        }

        /// <summary>Round M2 (D5, build-contract.md §6.3): one lodge's last walker scan, 8 spaces in, after its occupants
        /// line: walkers within SkiDayTiming.NearLodgeM by kind, the ski-day guests among them, the wait so far, the game's
        /// per-stop waiting count (one waypoint per line per stop; taxi stands carry no Game.Routes.Position, so the scan
        /// cannot see them) and this hour's most.</summary>
        private void AppendNearLodge(StringBuilder sb, Entity lodge)
        {
            CultureInfo ic = CultureInfo.InvariantCulture;
            string head = Invariant($"        walkers within {SkiDayTiming.NearLodgeM:F0} m");
            if (!m_NearKnown || !m_NearByLodge.TryGetValue(lodge, out NearCopy nc))
            {
                sb.Append(head).Append(": no scan yet\n");
                return;
            }
            NearOut o = nc.Out;
            if (o.HasPosition == 0)
            {
                sb.Append(head).Append(": the lodge has no position\n");
                return;
            }
            sb.Append(head).Append(" (scan of ").Append(nc.Scanned).Append(" walkers ")
              .Append(SkiDayTiming.HoursFor(m_Simulation.frameIndex - nc.Frame).ToString("F2", ic)).Append(" h ago, ").Append(ClockText(nc.Minute))
              .Append("): ").Append(o.Walkers).Append(" (waiting for transport ").Append(o.Waiting).Append(", failed path ").Append(o.FailedPath)
              .Append(", hanging around ").Append(o.Hangaround).Append(", walking ").Append(WalkingOf(o)).Append("; not drawn ").Append(o.NotDrawn)
              .Append("); ski-day guests ").Append(o.Ours).Append(" (waiting ").Append(o.OursWaiting).Append(", of them followed after leaving ")
              .Append(o.OursWaitingFollowed).Append("; failed path ").Append(o.OursFailed).Append("); ").Append(WaitText(o, true))
              .Append("; the game's waiting count ").Append(o.StopCount).Append(" at ").Append(o.StopPoints)
              .Append(Invariant($" line stop point(s) within {SkiDayTiming.NearLodgeM:F0} m (one per line per stop; taxi stands not seen), nearest line stop "))
              .Append(NearestStopText(o.NearestStopM)).Append("; this hour ");
            if (m_NearHour.TryGetValue(lodge, out NearHour h) && h.Scans > 0)
                sb.Append("most ").Append(h.MostWalkers).Append(" at ").Append(ClockText(h.MostMinute)).Append('\n');
            else
                sb.Append("no scan yet\n");
        }

        /// <summary>Round M1 M5: the last ended ski days, newest first, from the managed copy of the ring.</summary>
        private void AppendRing(StringBuilder sb)
        {
            int n = Math.Min(m_RingCopyTotal, m_RingCopy.Length);
            if (n <= 0)
            {
                sb.Append("  Ski days last ended ski days: none yet\n");
                return;
            }
            sb.Append("  Ski days last ").Append(n).Append(" ended ski days (newest first; +h = game hours after the claim; \"home\" = home or hotel; ")
              .Append("\"then …\" = the first hour after leaving the lodge; ").Append(m_RingCopyTotal).Append(" this load):\n");
            for (int k = 0; k < n; k++)
            {
                int slot = ((m_RingCopyNext - 1 - k) % m_RingCopy.Length + m_RingCopy.Length) % m_RingCopy.Length;
                sb.Append("    ").Append(RecordText(m_RingCopy[slot])).Append('\n');
            }
        }

        /// <summary>RecordText's 255 sentinel ("not read" / "none" / "n/a" in ClaimRecord's byte fields).</summary>
        private const byte RecordNone = 255;
        /// <summary>ClaimRecord.EndLive bits (Round M2, D4): the game's work-time and bedtime checks were true at the visit end.</summary>
        private const byte EndLiveWork = 1, EndLiveBed = 2;

        /// <summary>One ended ski day (M5): who, how far, when claimed and the deadline's bound, departure, rides,
        /// closest approach, markers, then the outcome with its place, why, visit end and exit, or the late watch.
        /// Round M2: "pulled in" on the deadline (R4 b), the door wait (D1), "under 0.5 km" only for claims over 0.5 km
        /// (D6), why the Leisure went and the game's clock checks in the visit end (D4), where a no-purpose exit stood
        /// when it was not followed (U16), and the first hour after leaving (D5, "then …").</summary>
        private string RecordText(in ClaimRecord r)
        {
            CultureInfo ic = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(320);
            sb.Append(Fmt(r.Citizen)).Append(' ').Append(KindText(r.Kind)).Append(" by ").Append(ClassText(r.Class)).Append(' ')
              .Append((r.DistanceM / 1000.0).ToString("F1", ic)).Append(" km, claimed ").Append(ClockText(r.ClaimMinute))
              .Append(" (deadline ").Append(ClockText(SkiDayTiming.ClockMinuteAfter(r.ClaimMinute, r.ClaimFrame, r.Deadline)))
              .Append((r.Flags & SkiDayTiming.FlagDeadlineIsClosing) != 0 ? ", lifts closing" : ", own deadline")
              .Append((r.Flags2 & SkiDayTiming.Flag2DeadlinePulled) != 0 ? ", pulled in" : "").Append("): ");
            bool departed = (r.Flags & SkiDayTiming.FlagDeparted) != 0;
            sb.Append(departed ? "left home " + PlusH(r, r.DepartFrame) : "did not leave home");
            // Round M2 (D1): the door wait (MoveFrame = the first pass the walker carried no Divert{PathFailed}; 0 = never seen)
            if (departed && r.MoveFrame != 0u && (int)(r.MoveFrame - r.DepartFrame) > 0)
                sb.Append(", waited at the door until ").Append(PlusH(r, r.MoveFrame));
            else if (departed && r.MoveFrame == 0u && r.Outcome != 0)
                sb.Append(", never moved off");
            if ((r.Flags & SkiDayTiming.FlagRode) != 0)
            {
                sb.Append(", rode ").Append(HoursAfter(r, r.RideFirstFrame)).Append(" to ").Append(PlusH(r, r.RideLastFrame)).Append(" (")
                  .Append(LegsText(r.Legs, r.Kinds)).Append("; seen in it ")
                  .Append(SkiDayTiming.HoursFor((uint)((long)r.RidePasses * m_LeisureInterval)).ToString("F1", ic)).Append(" h)");
            }
            else if (departed)
            {
                sb.Append(", never rode");
            }
            if (r.ClosestM < float.PositiveInfinity)
            {
                sb.Append(", closest ").Append((r.ClosestM / 1000.0).ToString("F1", ic)).Append(" km");
                if (SkiDayTiming.ShowNear500(r.DistanceM, r.Near500Frame))   // Round M2 (D6): only for a claim over 0.5 km
                    sb.Append(" (under 0.5 km at ").Append(PlusH(r, r.Near500Frame)).Append(')');
            }
            string markers = MarkersOf(r);
            if (markers.Length > 0)
                sb.Append(", markers ").Append(markers);
            switch (r.Outcome)
            {
                case 0:   // arrived, visit ended, out
                    sb.Append(", arrived ").Append(PlusH(r, r.ArrivalFrame)).Append(" (")
                      .Append(ClockText(SkiDayTiming.ClockMinuteAfter(r.ClaimMinute, r.ClaimFrame, r.ArrivalFrame))).Append("), visit ended ")
                      .Append(PlusH(r, r.EndFrame)).Append(" [");   // fixer L10: no nested parentheses
                    VisitEndText(sb, r);
                    sb.Append(']');
                    if (r.Exit == (byte)SkiDayTiming.ExitWay.StillInside)
                    {
                        sb.Append(", still inside ").Append(PlusH(r, r.ExitFrame));
                    }
                    else
                    {
                        sb.Append(", out ").Append(PlusH(r, r.ExitFrame)).Append(" (").Append(Word(s_ExitWords, r.Exit));
                        // Round M2 (U16, Decided here 5): the place of a no-purpose exit only when the guest was not followed
                        // (a follow says where the guest ended up in its own words)
                        if (r.Exit == (byte)SkiDayTiming.ExitWay.NoPurpose && (r.FollowFlags & SkiDayTiming.FollowFlagFollowed) == 0 &&
                            r.ExitAt < SkiDayTiming.NoPurposeAtCount)
                            sb.Append("; ").Append(Word(s_NoPurposeAtWords, r.ExitAt));
                        sb.Append(')');
                        FollowSuffix(sb, r);
                    }
                    break;
                case 3:   // expired, then the late watch
                    sb.Append(", expired ").Append(PlusH(r, r.EndFrame)).Append(" (").Append(Word(s_PlaceWords, r.Place)).Append("), after expiring ");
                    if (r.LateResult == (byte)SkiDayTiming.LateResult.NotWithin6h)
                        sb.Append("still not there when the 6 h watch ended (").Append(PlusH(r, r.ExitFrame)).Append(')');   // fixer L11
                    else
                        sb.Append(Word(s_LateWords, r.LateResult)).Append(' ').Append(PlusH(r, r.ExitFrame));
                    break;
                default:  // gone or retargeted
                    sb.Append(", ").Append(r.Outcome == 1 ? "gone " : "retargeted ").Append(PlusH(r, r.EndFrame)).Append(" at ")
                      .Append(ClockText(SkiDayTiming.ClockMinuteAfter(r.ClaimMinute, r.ClaimFrame, r.EndFrame))).Append(" (")
                      .Append(Word(s_PlaceWords, r.Place)).Append("; why ").Append(Word(s_WhyWords, r.GoneWhy)).Append(')');
                    break;
            }
            sb.Append("; ski area ").Append(Fmt(r.Sign));
            return sb.ToString();
        }

        /// <summary>Round M2 (D4): "{cause}{: why}{ (move reason)}{; work time now}{; bedtime now}" — the why when the
        /// recorder read one (RemoveWhy &lt; RemoveWhyCount: need met, removed by the game, removed on arrival), the household's
        /// MoveAwayReason for moving away or the hotel money check, and the game's own clock checks at the visit end.</summary>
        private static void VisitEndText(StringBuilder sb, in ClaimRecord r)
        {
            sb.Append(Word(s_EndCauseWords, r.EndCause));
            if (r.RemoveWhy < SkiDayTiming.RemoveWhyCount)
            {
                sb.Append(": ").Append(Word(s_RemoveWhyWords, r.RemoveWhy));
                if ((r.RemoveWhy == (byte)SkiDayTiming.RemoveWhy.MovingAway || r.RemoveWhy == (byte)SkiDayTiming.RemoveWhy.MoneyCheck) &&
                    r.MoveReason != RecordNone)
                    sb.Append(" (").Append(Word(s_MoveReasonWords, r.MoveReason)).Append(')');
            }
            if ((r.EndLive & EndLiveWork) != 0)
                sb.Append("; work time now");
            if ((r.EndLive & EndLiveBed) != 0)
                sb.Append("; bedtime now");
        }

        /// <summary>Round M2 (D5, walkers-after W7 with the contract's U16 texts): the first hour after leaving the lodge,
        /// for exits followed (going home, no trip purpose, moving away) or skipped at the cap; nothing for other exits.
        /// "in X h" counts from leaving the lodge (FollowEndFrame − ExitFrame); "placed … at once" = the game's placement
        /// seen at the first sample (SkiDayTiming.PlacedNow, U16).</summary>
        private void FollowSuffix(StringBuilder sb, in ClaimRecord r)
        {
            byte ff = r.FollowFlags;
            if ((ff & SkiDayTiming.FollowFlagNotFollowedCap) != 0)
            {
                sb.Append(Invariant($"; not followed ({Tuning.FollowCap} already followed)"));
                return;
            }
            if ((ff & SkiDayTiming.FollowFlagFollowed) == 0)
                return;
            bool placed = (ff & SkiDayTiming.FollowFlagPlaced) != 0, atOnce = (ff & SkiDayTiming.FollowFlagAtOnce) != 0;
            string inH = " in " + SkiDayTiming.HoursFor(r.FollowEndFrame - r.ExitFrame).ToString("F1", CultureInfo.InvariantCulture) + " h ";
            switch ((SkiDayTiming.FollowResult)r.FollowResult)
            {
                case SkiDayTiming.FollowResult.Home:
                    if (placed && atOnce)
                        sb.Append("; then placed home at once");
                    else
                        sb.Append(placed ? "; then placed home" : "; then home").Append(inH).Append(FollowDetail(r));
                    break;
                case SkiDayTiming.FollowResult.MapExit:
                    if (placed && atOnce)
                        sb.Append("; then placed at the map exit at once");
                    else
                        sb.Append(placed ? "; then placed at the map exit" : "; then at the map exit").Append(inH).Append(FollowDetail(r));
                    break;
                case SkiDayTiming.FollowResult.OtherBuilding:
                    if (placed && atOnce)
                        sb.Append("; then placed in another building at once");
                    else
                        sb.Append(placed ? "; then placed in another building" : "; then into another building").Append(inH).Append(FollowDetail(r));
                    break;
                case SkiDayTiming.FollowResult.BackInLodge:
                    sb.Append("; then back in the lodge").Append(inH).Append(FollowDetail(r));
                    break;
                case SkiDayTiming.FollowResult.Deleted:
                    sb.Append("; then deleted").Append(inH).Append(FollowDetail(r));
                    break;
                case SkiDayTiming.FollowResult.StillOut:
                    sb.Append(Invariant($"; then still out after {SkiDayTiming.HoursFor(SkiDayTiming.FollowWatchFrames):F0} h: ")).Append(StillOutText(r));
                    break;
            }
        }

        /// <summary>"(mode; waited at a stop w h, n h of it within 100 m of the lodge, failed path seen f h)": the vehicles
        /// seen after leaving ("on foot" when none), the stop wait (passes × our interval) when any, the failed-path time
        /// when any.</summary>
        private string FollowDetail(in ClaimRecord r)
        {
            CultureInfo ic = CultureInfo.InvariantCulture;
            string mode = LegsText(r.FollowLegs, r.FollowKinds);
            var sb = new StringBuilder(96);
            sb.Append('(').Append(mode == "none" ? "on foot" : mode);
            bool more = false;
            if (r.WaitPasses > 0)
            {
                sb.Append("; waited at a stop ").Append(PassHours(r.WaitPasses).ToString("F1", ic)).Append(" h, ")
                  .Append(PassHours(r.WaitNearPasses).ToString("F1", ic)).Append(Invariant($" h of it within {SkiDayTiming.NearLodgeM:F0} m of the lodge"));
                more = true;
            }
            if (r.FailPasses > 0)
                sb.Append(more ? ", " : "; ").Append("failed path seen ").Append(PassHours(r.FailPasses).ToString("F1", ic)).Append(" h");
            return sb.Append(')').ToString();
        }

        /// <summary>The state of a guest still out when the follow ended (SkiDayTiming.StillOut at the last sample).</summary>
        private string StillOutText(in ClaimRecord r)
        {
            CultureInfo ic = CultureInfo.InvariantCulture;
            switch ((SkiDayTiming.StillOut)r.StillOutState)
            {
                case SkiDayTiming.StillOut.Waiting:
                    return "waiting at a stop (waited " + PassHours(r.WaitPasses).ToString("F1", ic) + " h, " + PassHours(r.WaitNearPasses).ToString("F1", ic) +
                           Invariant($" h of it within {SkiDayTiming.NearLodgeM:F0} m of the lodge; ") +
                           (r.FollowRidePasses == 0 ? "never rode" : "rode earlier: " + LegsText(r.FollowLegs, r.FollowKinds)) + ")";
                case SkiDayTiming.StillOut.Riding:
                    return "riding (" + LegsText(r.FollowLegs, r.FollowKinds) + ", first seen in it " +
                           SkiDayTiming.HoursFor(r.FollowRideFirst - r.ExitFrame).ToString("F1", ic) + " h after leaving)";
                case SkiDayTiming.StillOut.FailedPath:
                    return "standing with a failed path (seen " + PassHours(r.FailPasses).ToString("F1", ic) + " h)";
                case SkiDayTiming.StillOut.OnFoot:
                    return "on foot";
                default:
                    return "no walker at the last look";
            }
        }

        /// <summary>Game hours of a count of our passes (passes × the pass interval), as the ride time is printed.</summary>
        private double PassHours(ushort passes) => SkiDayTiming.HoursFor((uint)((long)passes * m_LeisureInterval));

        /// <summary>"+6.0 h": game hours from the claim to <paramref name="frame"/>.</summary>
        private static string PlusH(in ClaimRecord r, uint frame)
        {
            return HoursAfter(r, frame) + " h";
        }

        /// <summary>"+6.0": game hours from the claim to <paramref name="frame"/> (0 when not later).</summary>
        private static string HoursAfter(in ClaimRecord r, uint frame)
        {
            uint d = frame - r.ClaimFrame;
            double h = (int)d > 0 ? SkiDayTiming.HoursFor(d) : 0.0;
            return "+" + h.ToString("F1", CultureInfo.InvariantCulture);
        }

        private static string Word(string[] words, byte index)
        {
            return index < words.Length ? words[index] : "-";
        }

        /// <summary>The vehicles of a record: "car", "public transport (bus, train)", "taxi", "bike", "other vehicle".</summary>
        private static string LegsText(byte legs, byte kinds)
        {
            var sb = new StringBuilder(48);
            if ((legs & SkiDayRules.LegCar) != 0)
                sb.Append("car");
            if ((legs & SkiDayRules.LegTransit) != 0)
            {
                sb.Append(sb.Length > 0 ? ", " : "").Append("public transport");
                bool first = true;
                for (int b = 0; b < SkiDayRules.KindBits; b++)
                {
                    if ((kinds & (1 << b)) == 0)
                        continue;
                    sb.Append(first ? " (" : ", ").Append(s_TransitKindWords[b]);
                    first = false;
                }
                if (!first)
                    sb.Append(')');
            }
            if ((legs & SkiDayRules.LegTaxi) != 0)
                sb.Append(sb.Length > 0 ? ", " : "").Append("taxi");
            if ((legs & SkiDayRules.LegBike) != 0)
                sb.Append(sb.Length > 0 ? ", " : "").Append("bike");
            if ((legs & SkiDayRules.LegOther) != 0)
                sb.Append(sb.Length > 0 ? ", " : "").Append("other vehicle");
            return sb.Length > 0 ? sb.ToString() : "none";
        }

        /// <summary>The markers of a record; "car path failed" when a car was ridden, else "vehicle path failed".</summary>
        private static string MarkersOf(in ClaimRecord r)
        {
            var sb = new StringBuilder(64);
            for (int k = 0; k < SkiDayTiming.MarkerCount; k++)
            {
                if (!SkiDayTiming.HasMarker(r.Flags, r.Flags2, k))
                    continue;
                sb.Append(sb.Length > 0 ? ", " : "");
                if (k == SkiDayTiming.MarkerVehiclePathFailed)
                    sb.Append((r.Legs & SkiDayRules.LegCar) != 0 ? "car path failed" : "vehicle path failed");
                else if (k == SkiDayTiming.MarkerTurnedBack)
                    sb.Append("turned back (").Append(Word(s_ExitWords, (byte)SkiDayTiming.ExitOfPurpose(true, r.TurnPurpose))).Append(')');
                else
                    sb.Append(s_MarkerWords[k]);
            }
            return sb.ToString();
        }

        // ── helpers ───────────────────────────────────────────────────────────────────────────────

        private string NameSuffix(Entity e)
        {
            if (e == Entity.Null || !m_NameSystem.TryGetCustomName(e, out string name) || string.IsNullOrEmpty(name))
                return "";
            return " '" + name + "'";
        }

        private static string MonthText(int key)
        {
            return SkiMoneyRules.YearOfKey(key) + "-" + SkiMoneyRules.MonthOfKey(key).ToString("D2", CultureInfo.InvariantCulture);
        }

        /// <summary>16.5 → "16:30".</summary>
        private static string HourText(double hour)
        {
            int minutes = (int)Math.Round(hour * 60.0);
            return (minutes / 60).ToString("D2", CultureInfo.InvariantCulture) + ":" + (minutes % 60).ToString("D2", CultureInfo.InvariantCulture);
        }

        /// <summary>Minute of the day 0..1439 → "HH:MM" (Round M1).</summary>
        private static string ClockText(int minute)
        {
            int m = ((minute % 1440) + 1440) % 1440;
            return (m / 60).ToString("D2", CultureInfo.InvariantCulture) + ":" + (m % 60).ToString("D2", CultureInfo.InvariantCulture);
        }

        private static string MetresText(float m)
        {
            return float.IsNaN(m) || float.IsInfinity(m) ? "-" : m.ToString("F0", CultureInfo.InvariantCulture);
        }

        private static string KindText(byte kind)
        {
            return kind == (byte)SkiDayRules.GuestKind.Tourist ? "tourist" : "local";
        }

        private static string ClassText(byte cls)
        {
            return cls == (byte)SkiDayRules.TravelClass.Walk ? "walk" : cls == (byte)SkiDayRules.TravelClass.Car ? "car" : "no car";
        }

        /// <summary>The lower of two entities by (Index, Version); Null counts as "none" (the other wins).</summary>
        private static Entity Lower(Entity current, Entity candidate)
        {
            if (current == Entity.Null)
                return candidate;
            if (candidate.Index != current.Index)
                return candidate.Index < current.Index ? candidate : current;
            return candidate.Version < current.Version ? candidate : current;
        }

        private static EntityRef Ref(Entity e) => new EntityRef(e.Index, e.Version);
        private static string Fmt(Entity e) => e.Index + ":" + e.Version;

        // ── managed state types ───────────────────────────────────────────────────────────────────

        /// <summary>Per-sign state that outlives a snapshot rebuild (ch2 §4.2; named AreaMemory so that AreaState
        /// here is SkiDayRules.AreaState, s1-spec N1). Dropped at a month roll once its sign has left.</summary>
        private sealed class AreaMemory
        {
            public readonly SkiDayRules.ClassTrack[] Tracks =
            {
                SkiDayRules.NewTrack(SkiDayRules.TravelClass.Walk),
                SkiDayRules.NewTrack(SkiDayRules.TravelClass.Car),
                SkiDayRules.NewTrack(SkiDayRules.TravelClass.NoCarFar),
            };
            public SkiDayMonth This = SkiDayMonth.Empty(), Last = SkiDayMonth.Empty();
            public bool FirstClaimLogged, FirstArrivalLogged, LodgesLogged;
            public byte StateKey;   // Round M2 (D10): the `[area]` line's last key (SkiDayRules.LogKey…; None = nothing written yet)
            // Round M2 (D12a, R4): the hill (a counted run) and the night run seen open since the load, on a known, released
            // score only (SkiDayRules.ClosingEdgeOf); a ski-day guest on site or followed this game hour, and whether the
            // last hourly `[near]` line was written (D5).
            public bool HillOpenSeen, NightOpenSeen, GuestsThisHour, NearPrinted;
            public readonly List<LodgeVerdict> LodgeVerdicts = new List<LodgeVerdict>(4);
            // the last applied pass (DUMP copies)
            public bool HasPass, ClaimHours, NightRunOpen, TopKnown;
            public byte State, Limit;
            public int Attraction, Capacity, TargetLodges, OnTheWay, OnSite, SnowHours = -1;
            public int LodgeCapacity, PriceTolerance;   // Catalog Phase B: step A's lodge side and price tolerance (DUMP)
            public double SchoolFactor = 1.0;           // Catalog Phase B: the area's school factor at the last pass (DUMP)
            public int NotLeft, Riding, Leaving, Watching;   // Round M1 per-pass snapshots
            public int AtDoor, Following;                    // Round M2 per-pass snapshots (D1, D5)
            public double OpenShare, Powder = double.NaN, RunsCapacity, TopM;
            // the last census (D30)
            public int OutingsOnTheWay;
            public double OldestOutingH;
            // Round M1 (m1-spec §1.3): the month's measure rows (RM / RH / CM layouts, Jobs.cs), This and Last;
            // preallocated once per area per load, none per pass.
            public readonly int[] MThis = new int[RM.Count], MLast = new int[RM.Count];
            public readonly double[] HThis = new double[RH.Count], HLast = new double[RH.Count];
            public readonly int[] CThis = new int[CM.Count], CLast = new int[CM.Count];
            // Round M1 M9: the powder year table of this area (int.MinValue = not computed yet)
            public readonly int[] PowderYearMarks = new int[12], PowderYearSnow = new int[12];
            public int PowderYearLogged = int.MinValue;
            public int PowderYearGeneration = -1;   // fixer M1-ECS-6: m_YearGeneration of the samples this table came from
            public float PowderYearTopM;
            public string PowderYearWhen;

            /// <summary>At a roll (after the `[day]` lines): Last = This, This cleared.</summary>
            public void RollMeasure()
            {
                Array.Copy(MThis, MLast, MThis.Length);
                Array.Clear(MThis, 0, MThis.Length);
                Array.Copy(HThis, HLast, HThis.Length);
                Array.Clear(HThis, 0, HThis.Length);
                Array.Copy(CThis, CLast, CThis.Length);
                Array.Clear(CThis, 0, CThis.Length);
            }

            /// <summary>At a jump: both months cleared.</summary>
            public void ClearMeasure()
            {
                Array.Clear(MThis, 0, MThis.Length);
                Array.Clear(MLast, 0, MLast.Length);
                Array.Clear(HThis, 0, HThis.Length);
                Array.Clear(HLast, 0, HLast.Length);
                Array.Clear(CThis, 0, CThis.Length);
                Array.Clear(CLast, 0, CLast.Length);
            }
        }

        /// <summary>One ski area's month (ch2 §6.4).</summary>
        private struct SkiDayMonth
        {
            public int Pool, PoolTourists, Sent, SentWalk, SentCar, SentNoCar, SentTourists, Arrived, ArrivedTourists, Gone,
                       Retargeted, Expired, ArrivedNoLeisure;
            public double HoursWalk, HoursCar, HoursNoCar;
            public int ArrivedWalk, ArrivedCar, ArrivedNoCar, PeakActive, FullPasses, Passes, CapacityLast;
            public byte LimitLast;
            public double PowderMax;
            public int SnowHoursAtMax;
            // Round M1: the pool's chance sum and the too-late chance sum (M4, F11), the passes with a known powder and
            // with the "Powder day" mark (M9), and the car claims that used the car-reserve queue (M4 text).
            public double PoolChanceSum, TooLateChanceSum;
            public int PowderMarkPasses, PowderKnownPasses, CarsEnqueued;
            // Round M2: the highest capacity of the month with its limit and the clock of the first Full pass (D11); the
            // arrivals while the hill was closed for the count, the closings (no-counted-run edges) with the last one's clock
            // and the guests on the way / on site then (D12a, R4 a); the deadlines pulled in (R4 b).
            public int CapacityMax, FirstFullMinute, ArrivedHillClosed, HillClosings, LastCloseMinute, OnTheWayAtClose, OnSiteAtClose, Pulled;
            public byte LimitAtMax;

            public static SkiDayMonth Empty() => new SkiDayMonth
            {
                CapacityLast = -1, PowderMax = double.NaN, SnowHoursAtMax = -1, CapacityMax = -1, FirstFullMinute = -1, LastCloseMinute = -1,
            };

            /// <summary>One applied pass. <paramref name="minute"/> = the clock at the apply; <paramref name="closed"/> =
            /// SkiDayRules.HillClosedForCount of the snapshot the pass ran with (its arrivals, D3 bookings included, count as
            /// "after the hill closed"); <paramref name="closing"/> = a no-counted-run edge (ClosingEdgeOf == 1).</summary>
            public void Add(in ClaimOut co, in ResolveOut ro, double powder, int snowHours, int minute, bool closed, bool closing)
            {
                Pool += co.Pool;
                PoolTourists += co.PoolTourists;
                PoolChanceSum += co.PoolChanceSum;
                TooLateChanceSum += co.TooLateChanceSum;
                CarsEnqueued += co.CarsEnqueued;
                if (!double.IsNaN(powder))
                {
                    PowderKnownPasses++;
                    if (SkiDayRules.PowderMark(powder))
                        PowderMarkPasses++;
                }
                Sent += co.Sent;
                SentWalk += co.SentWalk;
                SentCar += co.SentCar;
                SentNoCar += co.SentNoCar;
                SentTourists += co.SentTourists;
                ArrivedWalk += ro.ArrivedWalk;
                ArrivedCar += ro.ArrivedCar;
                ArrivedNoCar += ro.ArrivedNoCar;
                Arrived += ro.ArrivedWalk + ro.ArrivedCar + ro.ArrivedNoCar;
                ArrivedTourists += ro.ArrivedTourists;
                Gone += ro.Gone;
                Retargeted += ro.Retargeted;
                Expired += ro.Expired;
                ArrivedNoLeisure += ro.ArrivedNoLeisure;
                HoursWalk += ro.HoursWalk;
                HoursCar += ro.HoursCar;
                HoursNoCar += ro.HoursNoCar;
                PeakActive = Math.Max(PeakActive, ro.OnTheWay + ro.OnSite);
                if (co.State == (byte)SkiDayRules.AreaState.Full)
                {
                    if (FirstFullMinute < 0)
                        FirstFullMinute = minute;
                    FullPasses++;
                }
                Passes++;
                CapacityLast = co.CapacityPeople;
                LimitLast = co.Limit;
                if (co.CapacityPeople > CapacityMax)
                {
                    CapacityMax = co.CapacityPeople;
                    LimitAtMax = co.Limit;
                }
                if (closed)
                    ArrivedHillClosed += ro.ArrivedWalk + ro.ArrivedCar + ro.ArrivedNoCar;
                if (closing)
                {
                    HillClosings++;
                    LastCloseMinute = minute;
                    OnTheWayAtClose = ro.OnTheWay;
                    OnSiteAtClose = ro.OnSite;
                }
                Pulled += ro.Pulled;
                if (!double.IsNaN(powder) && (double.IsNaN(PowderMax) || powder > PowderMax))
                {
                    PowderMax = powder;
                    SnowHoursAtMax = snowHours;
                }
            }
        }

        /// <summary>The city's month (ch2 §6.4): walk passes and fresh decisions, for the total line and `[drift]`; Round
        /// M1 M8: the running pass cost of every pass with work (the timed pass excepted).</summary>
        private struct SkiDayCityMonth
        {
            public int WalkPasses, Walked, FreshLocals, FreshTourists, FreshWithCar, ShoppingNeed;
            public double CostSumMs, CostMaxMs;
            public int CostPasses;
            public int CostMaxMinute;   // Round M2 (D8): the clock of CostMaxMs
        }

        /// <summary>Round M2 (D5): one lodge's last walker scan (the DUMP): NearJob's result, its frame and minute, and the
        /// walkers the scan walked city-wide.</summary>
        private struct NearCopy
        {
            public NearOut Out;
            public uint Frame;
            public int Minute, Scanned;
        }

        /// <summary>Round M2 (D5): one lodge's scans in the current game hour (the hourly `[near]` line): how many, the last,
        /// and the scan with the most walkers (its minute and waiting count).</summary>
        private struct NearHour
        {
            public int Scans, MostWalkers, MostMinute, MostWaiting;
            public NearOut Last;
        }

        /// <summary>Round M1 M7 (a2): one resident ski-day arrival of a month, with the guest-scan verdict of its visit
        /// (255 = the visit was not over yet).</summary>
        private struct ResidentVisit
        {
            public Entity Citizen;
            public uint ArrivalFrame;
            public byte Verdict;
        }

        /// <summary>Fixer R2 / L6: one resident's ski-day visits of the checked month, by guest-scan verdict.</summary>
        private struct VisitTally
        {
            public int Visits, Missed, Unsure, Seen, NotOver;
        }

        /// <summary>Managed per-area values of the snapshot (not needed by the jobs).</summary>
        private struct AreaInfo
        {
            public int SnowHours;   // −1 unknown
            public bool TopKnown;
            public double TopM;
        }

        /// <summary>One lodge of the last applied pass (DUMP, rows 21-22, the `[lodges]` line).</summary>
        private struct DumpLodge
        {
            public Entity Lodge;
            public int Area;
            public byte Reject;
            public bool HasPosition, ParkingKnown, ParkingDone;
            public float2 Xz;
            public float NearestM;
            public int CarSpaces;
            // Catalog Phase B: the tier, its capacity and a mountain lodge (not a target).
            public int LodgeIndex, Capacity;
            public bool NoRoadNeeded;
        }

        private struct LodgeVerdict
        {
            public Entity Lodge;
            public byte Reject;
        }

        /// <summary>D30 occupants of one lodge at the last census.</summary>
        private struct Occupants
        {
            public int Workers, Residents, Tourists, OutingMembers, Other;
        }
    }

    /// <summary>One ski area's ski-day state for the hub (ch2 §9, ch4 §H.1; s1-spec §2.5). Published every armed
    /// pass while the area exists; Revision moves only when !Same.</summary>
    public struct SkiDayAreaView
    {
        public int TargetLodges;              // lodges with CheckLodge == None
        public int LodgesNotSlopeside;        // lodges whose reject is NotSlopeside (row 22)
        public Entity FirstLodgeNotSlopeside; // lowest (Index, Version); Null if none
        public bool ClaimHours;               // InClaimWindow at the last pass
        public int OnTheWayOrOnSite;          // −1 before the first pass
        public int Capacity;                  // CapacityPeople; −1 before the first pass; 0 is real (Full)
        public SkiDayLimit Limit;
        public float Powder;                  // (float)PowderOf(snowHours); NaN unknown
        public int SentThisMonth, SentCarThisMonth, SentNoCarThisMonth;     // Walk = Sent − Car − NoCar exactly
        public int ArrivedThisMonth, ArrivedLastMonth;                      // Last −1 unless MonthFlags.LastKnown
        public bool CannotReach;              // UpdateCannotReach(car track)
        public Entity ReachLodge;             // most car failures this load; Null allowed
        public int LodgesNoParking;           // every pass, ch3 §3.6 rule
        public Entity FirstLodgeNoParking;

        /// <summary>Field by field; Powder via SkiDayTiming.SameBits (NaN == NaN; a != on NaN would move
        /// Revision every pass, and ValueType.Equals boxes).</summary>
        public bool Same(in SkiDayAreaView o)
        {
            return TargetLodges == o.TargetLodges && LodgesNotSlopeside == o.LodgesNotSlopeside &&
                   FirstLodgeNotSlopeside == o.FirstLodgeNotSlopeside && ClaimHours == o.ClaimHours &&
                   OnTheWayOrOnSite == o.OnTheWayOrOnSite && Capacity == o.Capacity && Limit == o.Limit &&
                   SkiDayTiming.SameBits(Powder, o.Powder) && SentThisMonth == o.SentThisMonth &&
                   SentCarThisMonth == o.SentCarThisMonth && SentNoCarThisMonth == o.SentNoCarThisMonth &&
                   ArrivedThisMonth == o.ArrivedThisMonth && ArrivedLastMonth == o.ArrivedLastMonth &&
                   CannotReach == o.CannotReach && ReachLodge == o.ReachLodge && LodgesNoParking == o.LodgesNoParking &&
                   FirstLodgeNoParking == o.FirstLodgeNoParking;
        }
    }
}
