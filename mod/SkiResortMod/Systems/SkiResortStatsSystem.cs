// Ski-area membership and every per-area number of the Ski Resort hub. SkiResortHubUISystem publishes these rows; this system
// owns no binding and writes nothing to the world. The hub never recomputes the score: every score number is read from
// SkiResortAttractivenessSystem's last recompute. Nothing is stored except the names written by SkiRunBindTriggerSystem.
// Rebuild: UIUpdate, registered between SkiRunCatalogSystem and SkiResortHubUISystem (UpdateSystem.cs:158-161). Runs only while
// Demand (set by the hub every frame) or a requested rebuild; nothing while closed. Strings are built only in a rebuild.
// Logs: `Stats [arm]` once per load; a rebuild logs only when a count or band problem changed, else it is counted into ONE quiet
// summary line per QuietSummaryS; a one-shot `!!!` disables the system until the next load (the hub keeps the last rows).
// Engine notes: EngineNotes/SkiResortStatsSystem.md

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.City;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using Game.UI;
using SkiResortMod.Settings;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>bindings.ts ResortRow: identity and slow facts. Kind 1 = Not linked (Sign Null), always last.</summary>
    public struct ResortStats
    {
        public Entity Sign; public int Kind; public string Name; public bool CustomName;
        public int Runs, Rated, Flat, Green, Blue, Black, DoubleBlack;
        public float AreaM2, VerticalM;        // VerticalM: runs only (Agg.RunVerticalM); −1 = no run height
        public bool VerticalApprox;
        public int Lodges, FreeLodges;
        public int LiftRows; public float LiftLengthM, CapacityPerHour;   // CapacityPerHour −1 = none
    }

    /// <summary>bindings.ts ResortLive: per-area values that move. Same order and count as the resorts rows.</summary>
    public struct ResortLiveStats
    {
        public Entity Sign;
        public int Status, RunsOpen, RunsPartly;
        public bool SnowEstimated; public float SnowPct, SnowDepthCm;
        public int SeasonHint; public bool TopTempKnown; public float TopTempC;
        public int Attraction, AttractionFull; public bool AttractionHeld; public int LodgesActive; public bool Summer;
        public float LodgePart, Terrain, RunsValue, RunsValueFull; public int OpenSharePct;
        public float LiftsValue; public int LiftsComplete, LiftsCounted;
        public float VarietyMultiplier, VerticalMultiplier, ScoreVerticalM;
        public int EfficiencyPct; public float SharePct; public bool EstKnown; public int EstTourists;
        public int NextStep;
        public bool ThisMonthSinceLoad, LastMonthFull;
        public int GuestsThisMonth, TouristsThisMonth, TouristGroupsThisMonth, ResidentsThisMonth,
                   PaidThisMonth, UnpaidThisMonth, FreeThisMonth, TicketsThisMonth;
        public int GuestsLastMonth, TouristsLastMonth, TouristGroupsLastMonth, ResidentsLastMonth, PaidLastMonth, TicketsLastMonth;
        public int TicketGate, TicketIncomeMonth, TicketPaidMonth; public bool TicketIncomeEstimated;
        // Run upkeep rates by feature (R3, DECISIONS 578; SkiRunUpkeepRates.PisteUpkeep) and their hectares.
        public int CostGroomingMonth, CostSnowmakingMonth, CostLightsMonth, CostCatTracksMonth;
        public float HaNatural, HaGroomed, HaSnowmaking, HaLit, HaCatTracks;
        public int LiftCostMonth, LiftsBilled, LiftsBilledReduced;
        public int BuildingsMonth, WagesMonth, Jobs, JobsFilled;
        public int NetMonth; public bool NetEstimated;
        // appended in this order; -1 unknown, State and Limit 0.
        public int SkiDaysState, SkiDaysThisMonth, SkiDaysLastMonth, SkiDaysSent, SkiDaysSentCar, SkiDaysSentNoCar,
                   SkiDaysNow, SkiDaysCap, SkiDaysLimit, SkiDaysPowderPct;
        // Appended in this order. LodgeBest = the season's best lodge's BuildItemId ("" none); LodgeExtra = the season's extras after
        // the cap (Result.LodgeExtraApplied); SchoolValue, PatrolledRuns, PriceTolerance from the Result (-1 / 0 without one, as the
        // other score numbers); LodgeSpendMonth = the ledger's lodge spend per month by the tickets' month rule (-1 unknown; 0 while the
        // "Lodge spend per guest" setting is 0). LuxuryIgnored is retained for the wire only and is always 0: dropping the field would
        // bump HUB_CONTRACT (contract.lock) for nothing.
        public string LodgeBest;
        public float LodgeExtra;
        public int LuxuryIgnored;
        public float SchoolValue;
        public int PatrolledRuns, PriceTolerance, LodgeSpendMonth;
        // Passes, appended in this order: PassesSoldSeason / PassIncomeSeason = this ski season's pass sales at the area
        // (SkiMoneyRules.SeasonKeyOfDate; counted since the game was loaded, the ledger is in memory), PassHoldersThisMonth / LastMonth
        // = guests who came in on a pass (-1 unknown, as the other guest numbers).
        public int PassesSoldSeason, PassIncomeSeason, PassHoldersThisMonth, PassHoldersLastMonth;
        // Lessons, pre-season sale and city income, appended in this order: LessonsThisMonth / LessonsLastMonth = people who took a
        // lesson (-1 unknown; Last -1 until a month rolled); LessonIncomeMonth = what this month's lessons paid so far (the ledger's raw
        // month figure, no projection; -1 unknown); PassesPreSeason / PassIncomePreSeason = the part of this ski season's pass sales
        // (PassesSoldSeason / PassIncomeSeason) made in the pre-season sale (-1 unknown); CityIncomeEst = the resort's SharePct of the
        // game's TouristIncome statistic (CityStatisticsSystem.GetStatisticValue(StatisticType.TouristIncome), sampled 32 times a game
        // day, CityStatisticsSystem.cs:363). -1 unknown, no share, or Tourism Overhaul detected (the UI hides the row then). An estimate:
        // the statistic's window (cumulative / daily) is prefab data, not in the decompile. More: EngineNotes/SkiResortStatsSystem.md
        public int LessonsThisMonth, LessonsLastMonth, LessonIncomeMonth, PassesPreSeason, PassIncomePreSeason, CityIncomeEst;
        // The Net's rows (EngineNotes/HubRules.md ONE NET), appended in this order: the rows of NetMonth for the month in progress that are not
        // already sent (TicketsThisMonth and LessonIncomeMonth are the ticket and lesson rows). PassIncomeMonth = credits of passes SOLD
        // at the area this month, pre-season sales included; PassIncomePreMonth = the pre-season part of it (a subset); LodgeIncomeMonth
        // = lodge spend booked this month (LodgeSpendMonth keeps the last-month / projected rule); LiftChargedMonth / RunCostMonth = the
        // lift charge / run upkeep booked this month (LiftCostMonth / CostGroomingMonth ... stay the rates); BuildingsSoFar / WagesSoFar
        // = BuildingsMonth / WagesMonth x MonthShare (HubRules.RateSoFar); MonthShare = the share of the month simulated (0..1, -1
        // unknown). -1 unknown, as the other numbers. Invariant (HubRulesTests): sum of max(0, the four income rows) - sum of max(0, the
        // four cost rows) == NetMonth.
        public int PassIncomeMonth, PassIncomePreMonth, LodgeIncomeMonth, LiftChargedMonth, RunCostMonth, BuildingsSoFar, WagesSoFar;
        public float MonthShare;
        // The Ski Bus Depots: BusChargedMonth = the coach bill booked this month (a ninth Net row); Depots = active depots linked here;
        // BusGuestsMonth = ski days that arrived at a depot this month (-1 ski days off); BusBillMonth = the bill's rate now.
        public int BusChargedMonth, Depots, BusGuestsMonth, BusBillMonth;

        /// <summary>The terms NetMonth was computed from (-1 unknown).</summary>
        public HubMonthMoney NetTerms => new HubMonthMoney
        {
            TicketIncome = TicketsThisMonth, LodgeIncome = LodgeIncomeMonth, LessonIncome = LessonIncomeMonth, PassIncome = PassIncomeMonth,
            LiftCost = LiftChargedMonth, RunCost = RunCostMonth, BuildingsCost = BuildingsSoFar, WagesCost = WagesSoFar, BusCost = BusChargedMonth,
        };
    }

    /// <summary>bindings.ts RunRow. Catalog row order.</summary>
    public struct RunStats
    {
        public Entity Entity; public string Name; public int Kind; public bool Bound;
        public float LengthM, DropM, AreaM2; public Entity Sign; public int Ordinal; public bool CustomName;
        public int Band; public float GradePct, SteepestPct; public bool Approx;
        public bool Member, Served; public Entity ServedBy;
        public float ValueFull, BandValue, SizeFactor, TypeFactor, ServedFactor;
        public int GunSpacingM, LightSpacingM; // the run's two prop sliders: meters, 0 = off, -1 = its kind carries none
        public int PaidSteps;                  // the one-time steps this run has paid (SkiRunFees.PaidSteps bits of its binding): on again is free
    }

    /// <summary>bindings.ts RunLive. Same order and count as the runs rows.</summary>
    public struct RunLiveStats
    {
        public Entity Entity; public int OpenState; public float OpenFactor; public int SnowPct; public bool SnowEstimated;
        public float ValueNow; public int CostMonth;
        public int SnowDepthCm; // mean depth, cm; -1 not measured
        public int GunWhy;
    }

    /// <summary>bindings.ts LiftRow: Entity = the census row id (base station, else top station, else the lowest-index line
    /// edge).</summary>
    public struct LiftStats
    {
        public Entity Entity, Top, Sign; public string Name; public bool CustomName;
        public int State;                    // 0 Running, 1 Static, 2 Open, 3 Unfinished, 4 StationsOnly (as today)
        public int Sections, Towers, Chairs, ChairsRiding, Gaps, TowersRemoved;
        public float LengthM, RiseM, HorizontalM; public bool AlignKnown; public float AlignOffsetM, AlignYawDeg;
        public float CapacityPerHour;        // −1 when no chair rides
        public bool HasBase, HasTop, Powered, Counted; public int Status;
        public float RideTimeS; public int RunsServed, CostMonth; public bool BilledReduced;
        // The census row's lift type: Type = its LiftTypeDef.Id ("" unknown; LiftRow.type), TypeIndex = its SkiCatalog.Lifts index
        // (LiftCensus.NoType when unknown; the hub's per-type hints and items), Mixed = the row's component mixes lift types
        // (Unfinished), Seats / SpeedMps = the type's (0 unknown; LiftRow.seats / speedMps, the capacity and ride-time tips).
        public string Type; public byte TypeIndex; public bool Mixed;
        public int Seats; public float SpeedMps;
        // C# only (not written to the LiftRow binding): the census row's sharpest tower bend for the LiftBend problem row. BendTenths =
        // LiftCensus.BendWarnTenths (0 = none over LiftAlignMath.BendWarnDeg), BendTower = its tower number, BendNode = its net node.
        public int BendTenths, BendTower; public Entity BendNode;
        // The same for the LiftTrainLimit row: TrainTower = the number of the tower whose rope goes furthest past what its sheave train
        // follows (0 = none), TrainNode = its net node.
        public int TrainTower; public Entity TrainNode;
        // The same for the LiftLowSpan row: the row's lowest span whose carriers pass under the floor (LowSpan false = none), its tower
        // numbers (0 = a station), LowTenths = LiftCensus.LowTenths of the carriers' height above the ground, LowEdge = its net edge.
        public bool LowSpan; public int LowFrom, LowTo, LowTenths; public Entity LowEdge;
    }

    /// <summary>bindings.ts ProblemRow. Grouped by area in resorts order (ch5 §5.6).</summary>
    public struct ProblemStats
    {
        public Entity Sign; public int Code, Severity; public Entity Subject, SubjectTop; public string Name;
        public int Count, Pct; public string Factor; public int More, Detail, IfFixed; public string BuildItem;
    }

    /// <summary>bindings.ts CityStats. The hub's CityStatsWriter / CityStatsComparer write and compare it.</summary>
    public struct CityStats : IEquatable<CityStats>
    {
        public bool Known; public int DaysPerYear;
        public int ScaleK, BackcountryPct, TicketPrice;
        public bool SnowGating; public float SnowmakingMarginC; public bool SnowVisualsOff;
        public bool TourismOverhaul, RealisticWorkplaces;
        // "" = the game's own trip systems all run; else the known replacement(s), joined ", ", or "?" (SkiTripSystems.UnknownLabel =
        // bindings.ts TRIP_PLANNER_UNKNOWN: a system off, no known mod).
        public string TripPlanner;
        public bool TripPlannerBlocksSkiDays;   // LeisureSystem or CitizenBehaviorSystem off: ski days not armed
        public bool BookTicketsAsFees; // resort income is booked as public transportation fees
        public int PassShare; // SeasonPassShare, clamped 0..100 (0 = passes off)
        // the calendar month 0..11 of the game date, the month the guest ledger counts as "this month" (SkiMoneyRules.MonthKey, as
        // SkiGuestCounterSystem rolls it). The UI names it through vanilla Common.MONTH:<0-11>.
        public int Month;

        public bool Equals(CityStats o)
        {
            return Known == o.Known && DaysPerYear == o.DaysPerYear && ScaleK == o.ScaleK && BackcountryPct == o.BackcountryPct &&
                   TicketPrice == o.TicketPrice && SnowGating == o.SnowGating && SnowmakingMarginC.Equals(o.SnowmakingMarginC) &&
                   SnowVisualsOff == o.SnowVisualsOff && TourismOverhaul == o.TourismOverhaul &&
                   RealisticWorkplaces == o.RealisticWorkplaces && TripPlanner == o.TripPlanner &&
                   TripPlannerBlocksSkiDays == o.TripPlannerBlocksSkiDays && BookTicketsAsFees == o.BookTicketsAsFees &&
                   PassShare == o.PassShare && Month == o.Month;
        }

        public override bool Equals(object obj)
        {
            return obj is CityStats o && Equals(o);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Known ? 1 : 0;
                h = h * 31 + DaysPerYear;
                h = h * 31 + ScaleK;
                h = h * 31 + BackcountryPct;
                h = h * 31 + TicketPrice;
                h = h * 31 + (SnowGating ? 1 : 0);
                h = h * 31 + SnowmakingMarginC.GetHashCode();
                h = h * 31 + (SnowVisualsOff ? 1 : 0);
                h = h * 31 + (TourismOverhaul ? 1 : 0);
                h = h * 31 + (RealisticWorkplaces ? 1 : 0);
                h = h * 31 + (TripPlanner ?? "").GetHashCode();
                h = h * 31 + (TripPlannerBlocksSkiDays ? 1 : 0);
                h = h * 31 + (BookTicketsAsFees ? 1 : 0);
                h = h * 31 + PassShare;
                return h * 31 + Month;
            }
        }
    }

    public sealed partial class SkiResortStatsSystem : GameSystemBase
    {
        private static class Tuning
        {
            /// <summary>Wall-clock heartbeat (s) while the hub listens (the catalog's value).</summary>
            public const float HeartbeatS = 2f;

            /// <summary>Minimum wall-clock seconds between two signature-driven rebuilds (coalesces the catalog warm-up; tunable,
            /// unmeasured). The rising edge of Demand and requested rebuilds are immediate.</summary>
            public const float MinRebuildIntervalS = 0.5f;

            /// <summary>At most one "Stats [rebuild]" change line per this many seconds (a burst is folded,
            /// LogWindow).</summary>
            public const float LogWindowS = 5f;

            /// <summary>Rebuilds with no count or problem change are written as one summary line per this many seconds of wall clock
            /// (flushed at hub close and load; at quit only a pending change fold).</summary>
            public const float QuietSummaryS = 60f;

            /// <summary>The `Stats [cost]` line after this many rebuilds of a load.</summary>
            public const int CostLogAfterRebuilds = 32;

            /// <summary>One WARN per load when a single rebuild takes longer (ms).</summary>
            public const float SlowRebuildWarnMs = 5f;
        }

        private const string ResortDefaultKey = "SkiResort.Hub.RESORT_DEFAULT";
        private const string ResortDefaultEnglish = "Ski area {N}";
        private const string LiftDefaultKey = "SkiResort.Hub.LIFT_DEFAULT";
        private const string LiftDefaultEnglish = "Lift {N}";
        private const string UnlinkedKey = "SkiResort.Hub.UNLINKED_ENTRY";
        private const string UnlinkedEnglish = "Not linked to a ski area";
        private const string OrdinalPlaceholder = "{N}";
        private const string WhyHeartbeat = "heartbeat";
        private const string WhyHubOpened = "hub opened";
        // The change signature's reasons (OnUpdate); also the quiet summary's counters, in this order.
        private const string WhyCatalog = "catalog";
        private const string WhyCensus = "lift census";
        private const string WhyLoops = "chair loops";
        private const string WhyAttract = "attractiveness";
        private const string WhyTexels = "snow texels";
        private const string WhySnow = "snow state";
        private const string WhyGuests = "guests";
        private const string WhyCharge = "lift charge";
        private const string WhySkiDays = "ski days";   // appended after WhyHubOpened (counter indices kept)
        private static readonly string[] s_QuietReasons =
        {
            WhyHeartbeat, WhyCatalog, WhyCensus, WhyLoops, WhyAttract, WhyTexels, WhySnow, WhyGuests, WhyCharge, WhyHubOpened,
            WhySkiDays,
        };

        /// <summary>The shared-base lift label's separator ("&lt;name&gt; → &lt;top label&gt;"). The hub's rename strips a trailing
        /// separator + top label before storing.</summary>
        internal const string LiftNameSeparator = " → ";

        /// <summary>Upper bound of the "Ski area N" search (a city never holds this many signs; the bind
        /// trigger's AutoNameMaxN).</summary>
        internal const int ResortNameMaxN = 10000;

        /// <summary>EfficiencyFactor byte → its enum NAME (the UI's SelectedInfoPanel.EFFICIENCY_FACTORS[&lt;name&gt;] key,
        /// Game.UI.InGame/EfficiencySection.cs:36); a byte without a name reads "Factor&lt;n&gt;". Built once; no allocation per
        /// rebuild.</summary>
        private static readonly string[] s_FactorNames = BuildFactorNames();

        private SkiRunCatalogSystem m_Catalog;
        private SkiRunPropSystem m_RunProps;
        private SkiRunOwnershipSystem m_Ownership;
        private SkiResortAttractivenessSystem m_Attractiveness;
        private MountainJobsSystem m_MountainJobs;   // the SignNoRoad row: only while the sign carries jobs
        private SkiSnowInjectionSystem m_Injection;
        private LiftCensusSystem m_Census;
        private ChairPinSystem m_ChairPin;
        private NameSystem m_NameSystem;
        private CitySystem m_CitySystem;
        private SnowLineSystem m_SnowLine;
        private TimeSystem m_TimeSystem;
        private CityStatisticsSystem m_CityStatistics; // the TouristIncome statistic, read once per rebuild
        private ResourceSystem m_ResourceSystem;
        private PrefabSystem m_PrefabSystem;                 // DUMP (notification prefab names), the climate's winter
        private ClimateSystem m_ClimateSystem;               // the ski season's first month (SkiClimateCalendar)
        private SkiReachCensusSystem m_ReachCensus;          // hub row 27: homes and hotels near the area
        private SkiGuestCounterSystem m_GuestCounter;
        private SkiLiftChargeSystem m_LiftCharge;
        private SkiBusChargeSystem m_BusCharge;
        private SkiLiftTicketSystem m_LiftTicket;            // DUMP only
        private CityServiceBudgetSystem m_CityServiceBudget;
        private SimulationSystem m_Simulation;
        // Resolved at each load with GetExistingSystemManaged, so the creation order stays Mod.cs's (a GetOrCreate in OnCreate could
        // create them first); null-safe everywhere.
        private SkiDaySystem m_SkiDays;
        private SkiLodgeParkingSystem m_LodgeParking; // DUMP only
        // The prefab catalog's views (lodge rows for the building walk, fetched once with no per-building delegate, and station rows for
        // the per-row stub tip).
        private SkiPrefabCatalogSystem m_PrefabCatalog;
        // The tram shuttle mover, resolved at each load with GetExistingSystemManaged like m_SkiDays (creation order stays Mod.cs's);
        // null = not registered: a Shuttle row reads state 1, capacity / ride -1, chairs 0.
        private TramShuttleSystem m_Tram;

        private EntityQuery m_BuildingQuery;     // {Building, PrefabRef, Transform} None {Temp, Deleted}
        private EntityQuery m_ProviderQuery;     // AttractivenessProvider None {Temp, Deleted} (TourismSystem.cs:241)
        private EntityQuery m_EconomyQuery;      // EconomyParameterData singleton

        // Lookups for the vanilla upkeep helpers (registered in OnCreate; Update(this) per rebuild).
        private BufferLookup<ServiceUpkeepData> m_ServiceUpkeepLookup;
        private BufferLookup<InstalledUpgrade> m_InstalledUpgradeLookup;
        private ComponentLookup<PrefabRef> m_PrefabRefLookup;
        private ComponentLookup<ServiceUsage> m_ServiceUsageLookup;
        private BufferLookup<Employee> m_EmployeeLookup;

        // Published rows (swapped with scratch lists on rebuild).
        private List<ResortStats> m_Resorts = new List<ResortStats>(4);
        private List<ResortLiveStats> m_ResortLive = new List<ResortLiveStats>(4);
        private List<RunStats> m_Runs = new List<RunStats>(32);
        private List<RunLiveStats> m_RunLive = new List<RunLiveStats>(32);
        private List<LiftStats> m_Lifts = new List<LiftStats>(8);
        private List<ProblemStats> m_Problems = new List<ProblemStats>(8);
        private List<ResortStats> m_ResortsNext = new List<ResortStats>(4);
        private List<ResortLiveStats> m_ResortLiveNext = new List<ResortLiveStats>(4);
        private List<RunStats> m_RunsNext = new List<RunStats>(32);
        private List<RunLiveStats> m_RunLiveNext = new List<RunLiveStats>(32);
        private List<LiftStats> m_LiftsNext = new List<LiftStats>(8);
        private List<ProblemStats> m_ProblemsNext = new List<ProblemStats>(8);

        // Rebuild scratch (cleared, never shrunk).
        private readonly SkiResortIndex m_Index = new SkiResortIndex();
        private readonly List<Entity> m_SignEntities = new List<Entity>(4);       // index order
        private readonly List<Entity> m_SignPrefabs = new List<Entity>(4);        // parallel
        private readonly List<Entity> m_LodgeEntities = new List<Entity>(4); // every live lodge building
        private readonly Dictionary<Entity, int> m_LodgeResort = new Dictionary<Entity, int>(); // lodge -> resort index
        private readonly HashSet<Entity> m_LodgeActive = new HashSet<Entity>();                    // listed and not Inactive
        private readonly List<Entity> m_TopLevel = new List<Entity>(8); // CopyTopLevelLinks, per rebuild (CollectLodges)
        private readonly List<Entity> m_TopLevelSigns = new List<Entity>(8); // the building's sign, Entity.Null = not linked
        private readonly HashSet<Entity> m_NoRoad = new HashSet<Entity>(); // signs / lodges, m_RoadEdge Null
        private readonly Dictionary<Entity, int> m_ProviderOf = new Dictionary<Entity, int>(); // our signs / lodges -> a
        private readonly HashSet<Entity> m_Ours = new HashSet<Entity>();
        private readonly List<SkiResortAggregate.RunIn> m_RunIn = new List<SkiResortAggregate.RunIn>(32);
        private readonly List<SkiResortAggregate.LiftIn> m_LiftIn = new List<SkiResortAggregate.LiftIn>(8);
        private readonly List<SkiResortAggregate.LodgeIn> m_LodgeIn = new List<SkiResortAggregate.LodgeIn>(4);
        private SkiResortAggregate.Agg[] m_Agg = new SkiResortAggregate.Agg[4];
        private AreaWork[] m_Work = new AreaWork[4];                                             // grown with m_Agg
        private readonly List<LiftCensus.Row> m_CensusRows = new List<LiftCensus.Row>(8);
        private readonly List<Entity> m_RowBase = new List<Entity>(8), m_RowTop = new List<Entity>(8);   // per m_LiftsNext row, for its name
        private readonly List<HubRules.LoneStationRow> m_LonePairIn = new List<HubRules.LoneStationRow>(8);
        private readonly List<int> m_LonePairTop = new List<int>(8);
        private readonly List<int> m_LiftOrder = new List<int>(8);
        private readonly List<int> m_LiftOrdinals = new List<int>(8);
        private readonly Dictionary<Entity, int> m_BaseUse = new Dictionary<Entity, int>();
        private readonly Dictionary<Entity, int> m_TopUse = new Dictionary<Entity, int>();
        private readonly List<string> m_SignCustomNames = new List<string>(4);
        private readonly List<int> m_SignFallbackN = new List<int>(4);
        private readonly HashSet<string> m_NameScratch = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<Efficiency> m_EfficiencyScratch = new List<Efficiency>(8); // the DUMP's factor list
        private float[] m_EfficiencyValues = new float[8];                                       // DUMP: SignEfficiency input
        private readonly List<float3> m_RunTops = new List<float3>(32);
        // The run-link inputs and results of AssignServedLifts (LiftServedRule.AssignLinked, the same call as the attraction path).
        private readonly List<float2[]> m_RunOutlines = new List<float2[]>(32);
        private readonly List<float[]> m_RunHeights = new List<float[]>(32);
        private readonly List<int> m_RunAreas = new List<int>(32);
        private readonly LiftServedRule.RunLinks m_RunLinks = new LiftServedRule.RunLinks();
        private readonly List<int> m_ServedRoot = new List<int>(32);
        private readonly List<int> m_ServedVia = new List<int>(32);
        private readonly List<LiftServedRule.Lift> m_ServeLifts = new List<LiftServedRule.Lift>(8);
        private readonly List<LiftServedRule.Chord> m_Chords = new List<LiftServedRule.Chord>(64);
        private readonly List<int> m_ServingLift = new List<int>(32);
        private readonly List<float> m_ServingDistance = new List<float>(32);
        private readonly List<int> m_ServedCount = new List<int>(8);
        private readonly List<bool> m_TrackRuns = new List<bool>(32); // per run row, RunKindRules.IsCatTrackLink (the cat track mask)
        private readonly List<Efficiency> m_FactorScratch = new List<Efficiency>(8);
        private readonly List<HubProblemFactor> m_PFactors = new List<HubProblemFactor>(8);
        private readonly List<HubProblemLift> m_PLifts = new List<HubProblemLift>(8);
        private readonly List<int> m_PLiftRows = new List<int>(8);
        private readonly List<HubProblem> m_POut = new List<HubProblem>(16);
        // Reused scratch (no per-recompute allocation).
        private readonly List<int> m_CensusResort = new List<int>(8);                              // census row → resort (−1 Not linked)
        private readonly List<LiftServedRule.Lift> m_AreaServeLifts = new List<LiftServedRule.Lift>(8);   // one area's lifts
        private readonly List<Entity> m_DepotScratch = new List<Entity>(1);
        private readonly List<SkiResortAttractivenessSystem.LodgeInfo> m_LodgeInfo =
            new List<SkiResortAttractivenessSystem.LodgeInfo>(4);                                 // seam S5, one area's active lodges
        private readonly List<SkiResortScore.LodgeInput> m_LadderLodges = new List<SkiResortScore.LodgeInput>(4);   // IfLodgesOn
        private readonly Dictionary<Entity, int> m_BudgetOf = new Dictionary<Entity, int>();    // service prefab → budget %, per rebuild
        private Func<int, string> m_ResortDefaultFormat;                                         // cached delegate (no per-rebuild allocation)
        private NativeList<ServiceUpkeepData> m_UpkeepScratchList;   // Persistent, created on first use, disposed in OnDestroy
        private readonly Dictionary<int, string> m_ResortNameCache = new Dictionary<int, string>();
        private readonly Dictionary<int, string> m_LiftNameCache = new Dictionary<int, string>();
        private string m_ResortTemplate, m_LiftTemplate, m_UnlinkedName;
        private readonly Stopwatch m_Timer = new Stopwatch();
        private static readonly Func<int, int> s_TargetTourists = TourismSystem.GetTargetTourists;

        /// <summary>Per-area work of one rebuild. Index = resort row (signs, then Not linked).</summary>
        private struct AreaWork
        {
            public HubSnowSums Snow;
            public int RunsOpen, RunsPartly, LodgesActive, CompleteLifts;
            public double RunsValueFull;
            public float TopM;                 // −∞ = none
            public bool AnyGunRun;             // a bound snowmaking or night run
            public bool AnyGunOn; // a bound snowmaking or night run with gun-gated texels at the last mask pass
            public float CostGrooming, CostSnowmaking, CostLights, CostCatTracks; // per month, float sums
            public float HaNatural, HaGroomed, HaSnowmaking, HaLit, HaCatTracks;
            public float Capacity; public bool AnyCapacity;
            // Building.m_RoadEdge Null on the sign; the area's lodges with a Null edge and the one with the lowest (Index, Version):
            // LodgeNoRoad's subject, stable across chunk order.
            public bool SignNoRoad; public int LodgesNoRoad; public Entity LodgeNoRoad;
            // Active lodges whose row counts more summit-served (MuSummit > Mu) and that are not (LodgeInfo.SummitServed false), and the
            // lowest such lodge (the problem's subject).
            public int SummitNotServed; public Entity SummitNotServedLodge;
            // The attraction chapter's view of the area (BuildResortRows; reused by BuildProblems).
            public bool HasResult;
            public SkiResortAttractivenessSystem.AreaScoreView View;
            // SkiDaySystem's view of the area (BuildResortRows; reused by BuildProblems).
            public bool HasSkiDays;
            public SkiDayAreaView SkiDays;
        }

        // Per-load state.
        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;
        private bool m_WasDemanded;
        private string m_RebuildRequest;
        private string m_PendingWhy;
        private bool m_IndexBuilt;
        private int m_IndexBuildingCount = -1;
        private int m_SigCatalog = int.MinValue, m_SigCensus = int.MinValue, m_SigLoops = int.MinValue,
            m_SigAttract = int.MinValue, m_SigTexels = int.MinValue, m_SigSnow = int.MinValue, m_SigGuns = int.MinValue, m_SigGuests = int.MinValue,
            m_SigCharge = int.MinValue, m_SigSkiDays = int.MinValue;
        // Both ski-day settings 0 (set once per rebuild in BuildResortRows, read by BuildProblems); the Efficiency row's hysteresis
        // state: the signs whose band showed the row on the last build (m_EffRowOn) and the ones of this build (m_EffRowOnNext, copied
        // over after the loop: readonly, no swap).
        private bool m_SkiDaysOff;
        private readonly HashSet<Entity> m_EffRowOn = new HashSet<Entity>();
        private readonly HashSet<Entity> m_EffRowOnNext = new HashSet<Entity>();
        private float m_NextHeartbeat, m_LastRebuildTime = float.NegativeInfinity;
        private ulong m_HashResorts, m_HashResortLive, m_HashRuns, m_HashRunLive, m_HashLifts, m_HashProblems;
        // The rebuild log (LogRebuild). Change lines through m_ChangeLog; the rest counted into one quiet summary per QuietSummaryS.
        private readonly LogWindow m_ChangeLog = new LogWindow(Tuning.LogWindowS);
        private ulong m_LoggedKey;
        private bool m_KeyLogged;
        private int m_QuietRebuilds;
        private float m_QuietStart;
        private double m_QuietSumMs, m_QuietMaxMs;
        private readonly int[] m_QuietWhy = new int[s_QuietReasons.Length + 1];   // last slot: any other reason
        private readonly int[] m_QuietMoved = new int[6];                          // s_ArrayNames order
        private static readonly string[] s_ArrayNames = { "resorts", "resortLive", "runs", "runLive", "lifts", "problems" };
        private bool m_DaysPerYearLogged;
        private uint m_LoadFrameIndex;
        private bool m_ProblemsLogged, m_CostLogged, m_SlowLogged, m_BudgetWarned;
        private int m_CostRebuilds;
        private double m_CostSumMs, m_CostMaxMs, m_ServeSumMs, m_ProblemsSumMs;
        private double m_LastServeMs, m_LastProblemsMs;

        /// <summary>Set by SkiResortHubUISystem every frame.</summary>
        public bool Demand;

        public int ResortsRevision { get; private set; }
        public int ResortLiveRevision { get; private set; }
        public int RunsRevision { get; private set; }
        public int RunLiveRevision { get; private set; }
        public int LiftsRevision { get; private set; }
        public int ProblemsRevision { get; private set; }

        /// <summary>Signs in stable (Index, Version) order, then the Not linked row (always last,
        /// always present once a rebuild ran). The live rows have the same order and count.</summary>
        public int ResortCount => m_Resorts.Count;
        public ResortStats GetResort(int i) => m_Resorts[i];
        public int ResortLiveCount => m_ResortLive.Count;
        public ResortLiveStats GetResortLive(int i) => m_ResortLive[i];
        public int RunCount => m_Runs.Count;
        public RunStats GetRun(int i) => m_Runs[i];
        public int RunLiveCount => m_RunLive.Count;
        public RunLiveStats GetRunLive(int i) => m_RunLive[i];
        public int LiftCount => m_Lifts.Count;
        public LiftStats GetLift(int i) => m_Lifts[i];
        public int ProblemCount => m_Problems.Count;
        public ProblemStats GetProblem(int i) => m_Problems[i];
        public CityStats City { get; private set; }

        // Dev tab / DUMP.
        public string LastRebuildWhy = "-";
        public float LastRebuildMs, MaxRebuildMs;
        public long Rebuilds;
        public bool IsArmed => m_Armed;
        public bool IsDisabled => m_Disabled;

        /// <summary>Rebuild on the next OnUpdate even while nobody listens (dev tab, DUMP, a hub
        /// rename). Logged.</summary>
        public void RequestRebuild(string why)
        {
            m_RebuildRequest = why;
        }

        /// <summary>THE lift / station resort rule: the nearest live sign within SkiRunOwnershipSystem.TetherMeters of
        /// <paramref name="xz"/>. Rebuilds the sign index on the spot when it was not built this load or the building count moved since.
        /// Event time only (world selection, lodge launch): the stale check is a chunk count. Main thread.</summary>
        internal bool TryResolveResort(float2 xz, out Entity sign)
        {
            sign = Entity.Null;
            if (m_Disabled || !m_Armed || !m_Ownership.PrefabSetsReady)
                return false;
            try
            {
                if (!m_IndexBuilt || m_BuildingQuery.CalculateEntityCount() != m_IndexBuildingCount)
                    BuildIndex();
                if (!m_Index.TryResolve(xz, SkiRunOwnershipSystem.TetherMeters, out EntityRef r, out float _))
                    return false;
                sign = ToEntity(r);
                return true;
            }
            catch (Exception e)
            {
                HandleException(e, "TryResolveResort");
                return false;
            }
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Catalog = World.GetOrCreateSystemManaged<SkiRunCatalogSystem>();
            m_RunProps = World.GetOrCreateSystemManaged<SkiRunPropSystem>();
            m_Ownership = World.GetOrCreateSystemManaged<SkiRunOwnershipSystem>();
            m_Attractiveness = World.GetOrCreateSystemManaged<SkiResortAttractivenessSystem>();
            m_MountainJobs = World.GetOrCreateSystemManaged<MountainJobsSystem>();
            m_Injection = World.GetOrCreateSystemManaged<SkiSnowInjectionSystem>();
            m_Census = World.GetOrCreateSystemManaged<LiftCensusSystem>();
            m_ChairPin = World.GetOrCreateSystemManaged<ChairPinSystem>();
            m_NameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_SnowLine = World.GetOrCreateSystemManaged<SnowLineSystem>();
            m_TimeSystem = World.GetOrCreateSystemManaged<TimeSystem>();
            m_ClimateSystem = World.GetOrCreateSystemManaged<ClimateSystem>();
            m_ReachCensus = World.GetOrCreateSystemManaged<SkiReachCensusSystem>();
            m_CityStatistics = World.GetOrCreateSystemManaged<CityStatisticsSystem>();
            m_ResourceSystem = World.GetOrCreateSystemManaged<ResourceSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_GuestCounter = World.GetOrCreateSystemManaged<SkiGuestCounterSystem>();
            m_LiftCharge = World.GetOrCreateSystemManaged<SkiLiftChargeSystem>();
            m_BusCharge = World.GetOrCreateSystemManaged<SkiBusChargeSystem>();
            m_LiftTicket = World.GetOrCreateSystemManaged<SkiLiftTicketSystem>();
            m_CityServiceBudget = World.GetOrCreateSystemManaged<CityServiceBudgetSystem>();
            m_Simulation = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();

            m_BuildingQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(),
                    // The walk keeps only signs and lodges, and every one of them carries Game.Buildings.Park (Game.Prefabs/Park.cs archetype; the
                    // attractiveness system finds both sets through its Park query). Checked on the prefab JSON mirror for all 5 LodgeDefs prefabs +
                    // the sign.
                    ComponentType.ReadOnly<Game.Buildings.Park>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
            });
            // Exactly TourismSystem's provider set (TourismSystem.cs:241), read-only here.
            m_ProviderQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<AttractivenessProvider>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            m_EconomyQuery = GetEntityQuery(ComponentType.ReadOnly<EconomyParameterData>());

            m_ServiceUpkeepLookup = GetBufferLookup<ServiceUpkeepData>(isReadOnly: true);
            m_InstalledUpgradeLookup = GetBufferLookup<InstalledUpgrade>(isReadOnly: true);
            m_PrefabRefLookup = GetComponentLookup<PrefabRef>(isReadOnly: true);
            m_ServiceUsageLookup = GetComponentLookup<ServiceUsage>(isReadOnly: true);
            m_EmployeeLookup = GetBufferLookup<Employee>(isReadOnly: true);
            m_ResortDefaultFormat = ResortDefault;

            Mod.Log.Info($"{nameof(SkiResortStatsSystem)} created");
        }

        /// <summary>Quit to desktop raises no onGamePreload; the rebuild log is written here (LogWindow.cs header). At quit only when it
        /// holds a count or problem change: the quit disposes the mods before the world (m_ModManager?.Dispose() then DestroyWorld(),
        /// GameManager.cs:792-793), so after Mod.OnDispose the quiet summary of no-change rebuilds is dropped, while a pending change
        /// fold (a real change) is still written. Teardown never throws.</summary>
        protected override void OnDestroy()
        {
            try
            {
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (!Mod.Disposed)
                    FlushRebuildLog(now);
                else if (m_ChangeLog.Count > 0)
                    WriteChangeSummary(now);
            }
            catch (Exception)
            {
                // nothing left to report to
            }
            if (m_UpkeepScratchList.IsCreated)
                m_UpkeepScratchList.Dispose();
            base.OnDestroy();
        }

        /// <summary>A load: write what the rebuild log holds before OnGameLoadingComplete resets it.</summary>
        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            try
            {
                FlushRebuildLog(UnityEngine.Time.realtimeSinceStartup);
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Stats [rebuild]: folded lines could not be written before the load ({e.GetType().Name}: " +
                             $"{e.Message}) — non-fatal");
            }
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_WasDemanded = false;
            m_RebuildRequest = null;
            m_PendingWhy = null;
            m_IndexBuilt = false;
            m_IndexBuildingCount = -1;
            m_Index.Clear();
            m_SigCatalog = m_SigCensus = m_SigLoops = m_SigAttract = m_SigTexels = int.MinValue;
            m_SigSnow = m_SigGuns = m_SigGuests = m_SigCharge = m_SigSkiDays = int.MinValue;
            // The ski-day systems of this world (null-safe) and a fresh efficiency-row state.
            m_SkiDays = World.GetExistingSystemManaged<SkiDaySystem>();
            m_LodgeParking = World.GetExistingSystemManaged<SkiLodgeParkingSystem>();
            m_Tram = World.GetExistingSystemManaged<TramShuttleSystem>();
            m_SkiDaysOff = false;
            m_EffRowOn.Clear();
            m_EffRowOnNext.Clear();
            m_NextHeartbeat = 0f;
            m_LastRebuildTime = float.NegativeInfinity;
            m_HashResorts = m_HashResortLive = m_HashRuns = m_HashRunLive = m_HashLifts = m_HashProblems = 0;
            m_ChangeLog.Reset();          // OnGamePreload wrote what it held
            m_KeyLogged = false;
            m_LoggedKey = 0;
            m_QuietRebuilds = 0;
            m_DaysPerYearLogged = false;
            m_ProblemsLogged = m_CostLogged = m_SlowLogged = m_BudgetWarned = false;
            m_CostRebuilds = 0;
            m_CostSumMs = m_CostMaxMs = m_ServeSumMs = m_ProblemsSumMs = 0.0;
            m_LastServeMs = m_LastProblemsMs = 0.0;
            m_Resorts.Clear();
            m_ResortLive.Clear();
            m_Runs.Clear();
            m_RunLive.Clear();
            m_Lifts.Clear();
            m_Problems.Clear();
            m_ResortNameCache.Clear();
            m_LiftNameCache.Clear();
            m_ResortTemplate = m_LiftTemplate = m_UnlinkedName = null;
            City = default;
            // The previous city's rows are gone: every reader re-reads.
            ResortsRevision++;
            ResortLiveRevision++;
            RunsRevision++;
            RunLiveRevision++;
            LiftsRevision++;
            ProblemsRevision++;
            LastRebuildWhy = "-";
            LastRebuildMs = 0f;
            MaxRebuildMs = 0f;
            Rebuilds = 0;
            Demand = false;
            m_LoadFrameIndex = m_Simulation.frameIndex;   // the counted month fraction (ch5 §5.5)

            if (mode != GameMode.Game)
                return;

            m_Armed = true;
            int providers = m_ProviderQuery.CalculateEntityCount();
            Mod.Log.Info("Stats [arm] — the Ski Resort hub's rows (contract 6; econ-round spec ch5, ski-days spec ch4, code-round-2 " +
                         "§6.4: lift rows typed — capacity riding × the type's seats × speed × 3600 / loop for a Loop type; ride " +
                         "time half the loop at the type's speed; code-round-3 §6.2: a Shuttle row (tram) reads TramShuttleSystem " +
                         "— capacity cabins on the path × seats × 3600 / (2 × (trip + dwell)), ride time the dock-to-dock trip " +
                         "(dwell " + (HubRules.ShuttleRideIncludesDwell ? "included" : "excluded") + "), chairs = the cabins its " +
                         "base hosts, state 0 with a path and a cabin on it, else 1; tram mover: " +
                         (m_Tram != null ? "registered" : "NOT registered (Shuttle rows: state 1, capacity and ride −1, chairs 0)") +
                         "; problems 23 mixed lift types, 24 summit " +
                         "lodges not lift-served, 25 school without a counted green run; LodgeNoRoad counts lodges with no road and " +
                         "no cat track chain whose row needs one; IfLodgesOn = the ladder with every lodge on; lodge spend in the net): ski areas = live Welcome " +
                         "Signs in (Index, Version) order + a Not linked row; runs by their bound sign (catalog Gate), lodges " +
                         "by their published link (else free-standing → Not linked), lifts and stations by the " +
                         $"nearest live sign within TetherMeters={SkiRunOwnershipSystem.TetherMeters:F0} m of the base station " +
                         "(else top station, else the line's first node); score, run parts, lift parts and efficiency factors " +
                         "read from SkiResortAttractivenessSystem's last recompute (never recomputed here); per-run snow from " +
                         "SkiSnowInjectionSystem.GetRunSnow; guests and tickets from SkiGuestCounterSystem's ledger; lift " +
                         "charges from SkiLiftChargeSystem; buildings and wages = CityServiceBudgetSystem's formula at the " +
                         "current service budget × daysPerYear / 12; served lift and runs served from LiftServedRule.Assign " +
                         "over every run; share = S_res / S_all over every AttractivenessProvider; rebuilds only while the hub " +
                         "observes a panel binding (or on a request: dev tab, DUMP, a hub rename) — at once when the panel " +
                         "opens, on a catalog / lift census / chair loop / attractiveness / snow texel / snow state / guests / " +
                         $"lift charge change at most every MinRebuildIntervalS={Tuning.MinRebuildIntervalS} s, and every " +
                         $"HeartbeatS={Tuning.HeartbeatS} s wall clock; LogWindowS={Tuning.LogWindowS}; " +
                         $"StubTipLocalZ={StationAlign.StubTipLocalZ} (fixed-grip; per station row since Catalog Phase B); daysPerYear {m_TimeSystem.daysPerYear} (TimeSettingsData, " +
                         "copied by TimeSystem's first sim update — 1 before it; a later line reports the value once it is > " +
                         $"1); {providers} attractiveness provider(s) at load; contract 3: resorts/resortLive and runs/runLive " +
                         "split, problems built in C#; ski days from SkiDaySystem " + (m_SkiDays != null ? "(present)" : "(absent)") +
                         "; a lift that does not carry (unpowered) shows capacity – (Phase D)");
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !m_Armed)
                    return;
                bool demand = Demand;
                if (!demand && m_RebuildRequest == null)
                {
                    if (m_WasDemanded) // the hub just closed: write what the rebuild log holds (once)
                        FlushRebuildLog(UnityEngine.Time.realtimeSinceStartup);
                    m_WasDemanded = false;
                    return;   // the hub is closed: nothing per frame
                }
                GameManager gm = GameManager.instance;
                if (gm == null || gm.isGameLoading)
                    return;

                float now = UnityEngine.Time.realtimeSinceStartup;
                string why = m_RebuildRequest;
                bool requested = why != null;
                m_RebuildRequest = null;
                if (why == null && demand && !m_WasDemanded)
                    why = WhyHubOpened;
                m_WasDemanded = demand;

                if (why == null)
                {
                    // Change signature: field reads only; no allocation. No building count.
                    string sig = null;
                    if (m_Catalog.Revision != m_SigCatalog)
                        sig = WhyCatalog;
                    else if (m_Census.Revision != m_SigCensus)
                        sig = WhyCensus;
                    else if (m_ChairPin.LoopGeneration != m_SigLoops)
                        sig = WhyLoops;
                    else if (m_Attractiveness.Generation != m_SigAttract)
                        sig = WhyAttract;
                    else if (m_Injection.RunTexelGeneration != m_SigTexels)
                        sig = WhyTexels;
                    else if (m_Injection.RunSnowGeneration != m_SigSnow)
                        sig = WhySnow;
                    else if (m_Injection.GunGeneration != m_SigGuns)
                        sig = WhySnow; // a run's guns started or stopped (RunLive.gunWhy)
                    else if (m_GuestCounter.Revision != m_SigGuests)
                        sig = WhyGuests;
                    else if (m_LiftCharge.Revision != m_SigCharge)
                        sig = WhyCharge;
                    else if (m_SkiDays != null && m_SkiDays.Revision != m_SigSkiDays)
                        sig = WhySkiDays; // moves at most once per SkiDaySystem pass
                    if (sig != null && m_PendingWhy == null)
                        m_PendingWhy = sig;
                    if (m_PendingWhy != null && now - m_LastRebuildTime >= Tuning.MinRebuildIntervalS)
                        why = m_PendingWhy;
                    else if (now >= m_NextHeartbeat)
                        why = WhyHeartbeat;
                }
                if (why == null)
                    return;

                m_PendingWhy = null;
                Rebuild(why, requested, now);
                m_LastRebuildTime = now;
                m_NextHeartbeat = now + Tuning.HeartbeatS;
            }
            catch (Exception e)
            {
                HandleException(e, "OnUpdate");
            }
        }

        private void Rebuild(string why, bool requested, float now)
        {
            m_Timer.Reset();
            m_Timer.Start();

            // The signature this rebuild answers (read first, so a change during it re-runs).
            m_SigCatalog = m_Catalog.Revision;
            m_SigCensus = m_Census.Revision;
            m_SigLoops = m_ChairPin.LoopGeneration;
            m_SigAttract = m_Attractiveness.Generation;
            m_SigTexels = m_Injection.RunTexelGeneration;
            m_SigSnow = m_Injection.RunSnowGeneration;
            m_SigGuns = m_Injection.GunGeneration;
            m_SigGuests = m_GuestCounter.Revision;
            m_SigCharge = m_LiftCharge.Revision;
            m_SigSkiDays = m_SkiDays != null ? m_SkiDays.Revision : 0;

            RefreshTemplates();
            m_ResortsNext.Clear();
            m_ResortLiveNext.Clear();
            m_RunsNext.Clear();
            m_RunLiveNext.Clear();
            m_LiftsNext.Clear();
            m_ProblemsNext.Clear();
            m_RunIn.Clear();
            m_LiftIn.Clear();
            m_LodgeIn.Clear();
            m_RunTops.Clear();
            m_RunOutlines.Clear();
            m_RunHeights.Clear();
            m_RunAreas.Clear();
            m_BudgetOf.Clear();

            bool ready = m_Ownership.PrefabSetsReady;
            int resorts = 0;
            if (ready)
            {
                BuildIndex(); // signs (+ the lodge building list)
                double sAll = WalkProviders(); // S_all + our providers
                resorts = m_Index.Count;
                PrepareWork(resorts);
                CollectLodges(resorts);
                CollectRuns(resorts);
                AssignServedLifts();
                CollectLifts(resorts);
                SkiResortAggregate.Aggregate(m_RunIn, m_LiftIn, m_LodgeIn, resorts, m_Agg);
                CityStats city = ReadCity();         // read before the rows: ticket price, backcountry
                BuildResortRows(resorts, sAll, city);
                BuildProblems(resorts, city);
                City = city;
                // Paused right after a load the simulation systems have not run: their readings are not known yet (not "Closed, no lodge").
                if (!HubRules.FactsKnownAfterLoad(unchecked(m_Simulation.frameIndex - m_LoadFrameIndex)))
                {
                    for (int i = 0; i < m_ResortLiveNext.Count; i++)
                    {
                        ResortLiveStats blank = NotLinkedLive();
                        blank.Sign = m_ResortLiveNext[i].Sign;
                        m_ResortLiveNext[i] = blank;
                    }
                    m_ProblemsNext.Clear();
                }
            }
            else
            {
                // Pre-import: one empty Not linked row of each kind, so "always last; always sent" holds.
                m_ResortsNext.Add(new ResortStats
                {
                    Sign = Entity.Null, Kind = 1, Name = m_UnlinkedName, VerticalM = -1f, CapacityPerHour = -1f,
                });
                m_ResortLiveNext.Add(NotLinkedLive());
                City = ReadCity();
            }

            // Swap and revise (per-array content hashes).
            bool resortsMoved = Publish(ref m_Resorts, ref m_ResortsNext, HashResorts(m_ResortsNext), ref m_HashResorts);
            bool liveMoved = Publish(ref m_ResortLive, ref m_ResortLiveNext, HashResortLive(m_ResortLiveNext), ref m_HashResortLive);
            bool runsMoved = Publish(ref m_Runs, ref m_RunsNext, HashRuns(m_RunsNext), ref m_HashRuns);
            bool runLiveMoved = Publish(ref m_RunLive, ref m_RunLiveNext, HashRunLive(m_RunLiveNext), ref m_HashRunLive);
            bool liftsMoved = Publish(ref m_Lifts, ref m_LiftsNext, HashLifts(m_LiftsNext), ref m_HashLifts);
            bool problemsMoved = Publish(ref m_Problems, ref m_ProblemsNext, HashProblems(m_ProblemsNext), ref m_HashProblems);
            if (resortsMoved) ResortsRevision++;
            if (liveMoved) ResortLiveRevision++;
            if (runsMoved) RunsRevision++;
            if (runLiveMoved) RunLiveRevision++;
            if (liftsMoved) LiftsRevision++;
            if (problemsMoved) ProblemsRevision++;
            Rebuilds++;

            m_Timer.Stop();
            float ms = (float)m_Timer.Elapsed.TotalMilliseconds;
            LastRebuildMs = ms;
            if (ms > MaxRebuildMs)
                MaxRebuildMs = ms;
            LastRebuildWhy = why;

            if (!m_DaysPerYearLogged && m_TimeSystem.daysPerYear > 1)
            {
                // The arm line can only print the pre-read 1; say once what the month uses.
                m_DaysPerYearLogged = true;
                Mod.Log.Info($"Stats: daysPerYear {m_TimeSystem.daysPerYear} (TimeSettingsData) — building upkeep per month = per day × {m_TimeSystem.daysPerYear} / 12");
            }
            LogRebuild(why, ms, ready, now, requested || why == WhyHubOpened,
                       resortsMoved, liveMoved, runsMoved, runLiveMoved, liftsMoved, problemsMoved);
            LogCost(why, ms, resorts);
        }

        private static bool Publish<T>(ref List<T> current, ref List<T> next, ulong hash, ref ulong lastHash)
        {
            bool moved = hash != lastHash || current.Count != next.Count;
            List<T> swap = current;
            current = next;
            next = swap;
            lastHash = hash;
            return moved;
        }

        /// <summary>A rebuild is written only when a COUNT or a BAND PROBLEM changed (LogKey: areas, runs, lift rows, lodges, and each
        /// band row's area, code, severity, count, detail and subject, not the Factor rows nor the drifting numbers), or when forced (a
        /// request: dev tab, DUMP, rename; the hub opening). Those lines go through m_ChangeLog (one per LogWindowS, a burst folded into
        /// one summary). Every other rebuild is counted and written as ONE quiet summary per QuietSummaryS (by reason, arrays moved, ms),
        /// flushed when the hub closes, at a load and at quit, so the final state is never silent. Per rebuild: field arithmetic and one
        /// hash over the published rows; a string only for a change (written or folded) or a summary.</summary>
        private void LogRebuild(string why, float ms, bool ready, float now, bool force,
                                bool resortsMoved, bool liveMoved, bool runsMoved, bool runLiveMoved, bool liftsMoved, bool problemsMoved)
        {
            ulong key = LogKey();
            if (force || !m_KeyLogged || key != m_LoggedKey)
            {
                m_KeyLogged = true;
                m_LoggedKey = key;
                string body = RebuildBody(why, ms, ready);
                switch (m_ChangeLog.Offer(now, force, body, ms))
                {
                    case LogWindow.Verdict.Plain:
                        Mod.Log.Info("Stats [rebuild] " + body);
                        break;
                    case LogWindow.Verdict.Summary:
                        WriteChangeSummary(now);
                        break;
                }
            }
            else
            {
                if (m_QuietRebuilds == 0)
                {
                    m_QuietStart = now;
                    m_QuietSumMs = m_QuietMaxMs = 0.0;
                    Array.Clear(m_QuietWhy, 0, m_QuietWhy.Length);
                    Array.Clear(m_QuietMoved, 0, m_QuietMoved.Length);
                }
                m_QuietRebuilds++;
                m_QuietSumMs += ms;
                if (ms > m_QuietMaxMs)
                    m_QuietMaxMs = ms;
                int reason = Array.IndexOf(s_QuietReasons, why);
                m_QuietWhy[reason >= 0 ? reason : s_QuietReasons.Length]++;   // last slot: any other reason
                if (resortsMoved) m_QuietMoved[0]++;
                if (liveMoved) m_QuietMoved[1]++;
                if (runsMoved) m_QuietMoved[2]++;
                if (runLiveMoved) m_QuietMoved[3]++;
                if (liftsMoved) m_QuietMoved[4]++;
                if (problemsMoved) m_QuietMoved[5]++;
            }
            if (m_ChangeLog.TrailingDue(now))
                WriteChangeSummary(now);
            if (m_QuietRebuilds > 0 && !(now - m_QuietStart < Tuning.QuietSummaryS))
                WriteQuietSummary(now);
        }

        /// <summary>The change line's body (after "Stats [rebuild] "): counts, the band codes per area, ms, revisions.</summary>
        private string RebuildBody(string why, float ms, bool ready)
        {
            int signs = Math.Max(0, m_Resorts.Count - 1);
            ResortStats nl = m_Resorts.Count > 0 ? m_Resorts[m_Resorts.Count - 1] : default;
            int band = 0, factors = 0;
            var codes = new StringBuilder(64);
            Entity area = default;
            bool anyArea = false;
            for (int i = 0; i < m_Problems.Count; i++)
            {
                ProblemStats p = m_Problems[i];
                if (p.Code == (int)HubProblemCode.Factor)
                {
                    factors++;
                    continue;
                }
                band++;
                if (!anyArea || p.Sign != area)
                {
                    codes.Append(anyArea ? "; " : "").Append(p.Sign == Entity.Null ? "not linked" : "area " + Ent(p.Sign)).Append(": ");
                    area = p.Sign;
                    anyArea = true;
                }
                else
                {
                    codes.Append(", ");
                }
                codes.Append(((HubProblemCode)p.Code).ToString());
                if (p.Count != -1)
                    codes.Append(' ').Append(p.Count);
            }
            return $"({why}): {signs} resort(s) + not linked ({nl.Runs} run(s), {nl.LiftRows} lift row(s), {nl.Lodges} lodge(s)); " +
                   $"runs {m_Runs.Count}, lifts {m_Lifts.Count}, problems {band} band row(s) ({(anyArea ? codes.ToString() : "none")}) + " +
                   $"{factors} factor row(s); {ms:F2} ms; revisions {ResortsRevision}/{ResortLiveRevision}/{RunsRevision}/" +
                   $"{RunLiveRevision}/{LiftsRevision}/{ProblemsRevision}" +
                   (ready ? "" : " — ownership prefab sets not ready (pre-import?): no resorts");
        }

        /// <summary>The counts and band problems a change line reports (LogRebuild's rule), as one FNV-1a hash of the published rows.
        /// Factor rows and every drifting number (pct, more, ifFixed, factor) are left out: efficiency drift is the attractiveness area
        /// line's business.</summary>
        private ulong LogKey()
        {
            ulong h = 14695981039346656037UL;
            h = Mix(h, (uint)m_Resorts.Count);
            h = Mix(h, (uint)m_Runs.Count);
            h = Mix(h, (uint)m_Lifts.Count);
            for (int i = 0; i < m_Resorts.Count; i++)
            {
                ResortStats r = m_Resorts[i];
                h = MixE(h, r.Sign);
                h = Mix(h, (uint)r.Runs);
                h = Mix(h, (uint)r.LiftRows);
                h = Mix(h, (uint)r.Lodges);
                h = Mix(h, (uint)r.FreeLodges);
            }
            for (int i = 0; i < m_Problems.Count; i++)
            {
                ProblemStats p = m_Problems[i];
                if (p.Code == (int)HubProblemCode.Factor)
                    continue;
                h = MixE(h, p.Sign);
                h = Mix(h, (uint)p.Code | (uint)p.Severity << 8);
                h = Mix(h, (uint)p.Count);
                h = Mix(h, (uint)p.Detail);
                h = MixE(h, p.Subject);
            }
            return h;
        }

        private void WriteChangeSummary(float now)
        {
            LogWindow w = m_ChangeLog;
            Mod.Log.Info($"Stats [rebuild] {w.Count} more count or problem change(s) in {w.SpanS(now):F0} s, folded (one line per " +
                         $"LogWindowS={Tuning.LogWindowS:0} s; {w.Range(0, "F2")} ms) — last: {w.LastText}");
            w.Wrote(now);
        }

        /// <summary>One line for the rebuilds that changed no count and no band problem since the last summary.</summary>
        private void WriteQuietSummary(float now)
        {
            var sb = new StringBuilder(256);
            sb.Append("Stats [rebuild] ").Append(m_QuietRebuilds).Append(" rebuild(s) in ")
              .Append(Math.Max(0f, now - m_QuietStart).ToString("F0", CultureInfo.InvariantCulture))
              .Append(" s that changed no count and no problem row (");
            bool any = false;
            for (int i = 0; i < m_QuietWhy.Length; i++)
            {
                if (m_QuietWhy[i] == 0)
                    continue;
                sb.Append(any ? ", " : "").Append(i < s_QuietReasons.Length ? s_QuietReasons[i] : "other").Append(' ').Append(m_QuietWhy[i]);
                any = true;
            }
            sb.Append("); arrays moved:");
            any = false;
            for (int i = 0; i < m_QuietMoved.Length; i++)
            {
                if (m_QuietMoved[i] == 0)
                    continue;
                sb.Append(any ? ", " : " ").Append(s_ArrayNames[i]).Append(' ').Append(m_QuietMoved[i]);
                any = true;
            }
            if (!any)
                sb.Append(" none");
            sb.Append("; ").Append((m_QuietSumMs / m_QuietRebuilds).ToString("F2", CultureInfo.InvariantCulture)).Append(" ms mean, ")
              .Append(m_QuietMaxMs.ToString("F2", CultureInfo.InvariantCulture)).Append(" ms max (one summary per QuietSummaryS=")
              .Append(Tuning.QuietSummaryS.ToString("F0", CultureInfo.InvariantCulture)).Append(" s; problems = factor rows or numbers)");
            Mod.Log.Info(sb.ToString());
            m_QuietRebuilds = 0;
        }

        /// <summary>Writes what the rebuild log holds: the hub closed, a load, quit.</summary>
        private void FlushRebuildLog(float now)
        {
            if (m_ChangeLog.Count > 0)
                WriteChangeSummary(now);
            if (m_QuietRebuilds > 0)
                WriteQuietSummary(now);
        }

        /// <summary>The `Stats [cost]` line after the 32nd rebuild of a load and one WARN per load above SlowRebuildWarnMs. Field
        /// arithmetic per rebuild. Only rebuilds with at least one area, run or lift row count, so the line does not measure an empty
        /// city; the WARN likewise only for a rebuild with at least one area.</summary>
        private void LogCost(string why, float ms, int resorts)
        {
            if (!m_CostLogged && (resorts > 0 || m_Runs.Count > 0 || m_Lifts.Count > 0))
            {
                m_CostRebuilds++;
                m_CostSumMs += ms;
                if (ms > m_CostMaxMs)
                    m_CostMaxMs = ms;
                m_ServeSumMs += m_LastServeMs;
                m_ProblemsSumMs += m_LastProblemsMs;
                if (m_CostRebuilds >= Tuning.CostLogAfterRebuilds)
                {
                    m_CostLogged = true;
                    double n = m_CostRebuilds;
                    Mod.Log.Info($"Stats [cost] first {m_CostRebuilds} rebuilds with an area, run or lift this load: mean {m_CostSumMs / n:F2} ms, max " +
                                 $"{m_CostMaxMs:F2} ms (serve lifts + assign mean {m_ServeSumMs / n:F3} ms, problems mean " +
                                 $"{m_ProblemsSumMs / n:F3} ms); last: {resorts} area(s), {m_Runs.Count} run(s), " +
                                 $"{m_Lifts.Count} lift row(s), {m_Chords.Count} chord(s)");
                }
            }
            if (ms > Tuning.SlowRebuildWarnMs && !m_SlowLogged && resorts > 0)
            {
                m_SlowLogged = true;
                Mod.Log.Warn($"Stats [cost]: one rebuild took {ms:F2} ms (> {Tuning.SlowRebuildWarnMs:0} ms) — {resorts} area(s), " +
                             $"{m_Runs.Count} run(s), {m_Lifts.Count} lift row(s); reason '{why}' (once per load)");
            }
        }

        /// <summary>m_Agg and m_Work hold resorts + 1 rows (the last = Not linked); m_Work is reset.</summary>
        private void PrepareWork(int resorts)
        {
            int rows = resorts + 1;
            if (m_Agg.Length < rows)
                m_Agg = new SkiResortAggregate.Agg[Math.Max(rows, m_Agg.Length * 2)];
            if (m_Work.Length < rows)
                m_Work = new AreaWork[Math.Max(rows, m_Work.Length * 2)];
            for (int i = 0; i < rows; i++)
            {
                m_Work[i] = default;
                m_Work[i].TopM = float.NegativeInfinity;
            }
        }

        /// <summary>The building walk: live signs into the index (stable order) and every live lodge building into m_LodgeEntities.
        /// Completes the query's writers first (chunk reads do not wait on running jobs: Entities semantics, see the
        /// EngineNotes/SkiRunOwnershipSystem.md).</summary>
        private void BuildIndex()
        {
            m_Index.Clear();
            m_LodgeEntities.Clear();
            m_NoRoad.Clear();
            m_IndexBuildingCount = m_BuildingQuery.CalculateEntityCount();
            m_IndexBuilt = true;
            if (m_BuildingQuery.IsEmptyIgnoreFilter)
            {
                FillSignLists();
                return;
            }
            m_BuildingQuery.CompleteDependency();
            var entityHandle = GetEntityTypeHandle();
            var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
            var transformHandle = GetComponentTypeHandle<Game.Objects.Transform>(isReadOnly: true);
            var buildingHandle = GetComponentTypeHandle<Building>(isReadOnly: true); // m_RoadEdge
            // The catalog's lodge rows, fetched once before the chunk loop (no per-building delegate call).
            IReadOnlyDictionary<Entity, LodgeDef> lodgeDefs = m_PrefabCatalog.LodgeDefs;
            using (var chunks = m_BuildingQuery.ToArchetypeChunkArray(Allocator.Temp))
            {
                for (int c = 0; c < chunks.Length; c++)
                {
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    var transforms = chunks[c].GetNativeArray(ref transformHandle);
                    var buildings = chunks[c].GetNativeArray(ref buildingHandle);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        Entity prefab = prefabRefs[i].m_Prefab;
                        if (m_Ownership.IsGatePrefab(prefab))
                        {
                            m_Index.Add(ToRef(entities[i]), transforms[i].m_Position.xz);
                            if (buildings[i].m_RoadEdge == Entity.Null)
                                m_NoRoad.Add(entities[i]);
                        }
                        else if (lodgeDefs.TryGetValue(prefab, out LodgeDef lodgeDef))
                        {
                            m_LodgeEntities.Add(entities[i]);
                            // LodgeNoRoad counts a lodge with no road edge that still needs one (LodgeSupplyRule.CountsAsNoRoad, per building): a Hut
                            // or Summit lodge is roadless by design (LodgeRoadSystem clears its NoRoadConnection), and an off-road A-frame, Chalet
                            // or Grand on a cat track chain is supplied (the published answer, SkiResortAttractivenessSystem.IsSupplied).
                            if (lodgeDef != null && LodgeSupplyRule.CountsAsNoRoad(lodgeDef.RoadRule, buildings[i].m_RoadEdge != Entity.Null,
                                                                                   m_Attractiveness.IsSupplied(entities[i])))
                                m_NoRoad.Add(entities[i]);
                        }
                    }
                }
            }
            FillSignLists();
        }

        private void FillSignLists()
        {
            m_SignEntities.Clear();
            m_SignPrefabs.Clear();
            for (int i = 0; i < m_Index.Count; i++)
            {
                Entity sign = ToEntity(m_Index.SignAt(i));
                m_SignEntities.Add(sign);
                m_SignPrefabs.Add(EntityManager.TryGetComponent(sign, out PrefabRef pr) ? pr.m_Prefab : Entity.Null);
            }
        }

        /// <summary>One walk of TourismSystem's provider set: S_all = Σ a²/10⁴ (SkiResortTourism.Term)
        /// and the providers of our signs and lodges (m_ProviderOf).</summary>
        private double WalkProviders()
        {
            m_Ours.Clear();
            m_ProviderOf.Clear();
            for (int i = 0; i < m_SignEntities.Count; i++)
                m_Ours.Add(m_SignEntities[i]);
            for (int i = 0; i < m_LodgeEntities.Count; i++)
                m_Ours.Add(m_LodgeEntities[i]);
            double sAll = 0.0;
            if (m_ProviderQuery.IsEmptyIgnoreFilter)
                return 0.0;
            m_ProviderQuery.CompleteDependency();
            var entityHandle = GetEntityTypeHandle();
            var providerHandle = GetComponentTypeHandle<AttractivenessProvider>(isReadOnly: true);
            using (var chunks = m_ProviderQuery.ToArchetypeChunkArray(Allocator.Temp))
            {
                for (int c = 0; c < chunks.Length; c++)
                {
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var providers = chunks[c].GetNativeArray(ref providerHandle);
                    for (int i = 0; i < providers.Length; i++)
                    {
                        int a = providers[i].m_Attractiveness;
                        sAll += SkiResortTourism.Term(a);
                        if (m_Ours.Contains(entities[i]))
                            m_ProviderOf[entities[i]] = a;
                    }
                }
            }
            return sAll;
        }

        /// <summary>Lodge rule: a lodge (top-level, no Owner) belongs to its published link (CopyTopLevelLinks: the attraction snapshot's
        /// nearest live sign within the tether, the score's own membership; active = its own Building.m_OptionMask); every other live
        /// lodge building is free-standing (Not linked). No lodge rows: the aggregate's LodgeIn (Score 0) and the live count of active
        /// linked lodges. Also the per-sign SignNoRoad fact: no road edge while the sign carries jobs (MountainJobsRules.SignNoRoadRow; with
        /// an active road lodge in the area the jobs ride on it and nobody travels to the sign). It follows MountainJobsSystem's poll, so it
        /// may trail a road lodge by one heartbeat.</summary>
        private void CollectLodges(int resorts)
        {
            m_LodgeResort.Clear();
            m_LodgeActive.Clear();
            // The top-level buildings and their signs (the attraction snapshot's links), once per rebuild; read by the lodge rule below,
            // SchoolFacts, the jobs and cost sums (BuildResortRows) and the DUMP's patrol count.
            m_Attractiveness.CopyTopLevelLinks(m_TopLevel, m_TopLevelSigns);
            // The lodge set fetched once; school and patrol upgrades are not lodges and stay out of the lodge counts.
            HashSet<Entity> lodgePrefabs = m_PrefabCatalog.LodgePrefabSet;
            for (int s = 0; s < m_SignEntities.Count; s++)
            {
                Entity sign = m_SignEntities[s];
                if (s < resorts)
                    m_Work[s].SignNoRoad = MountainJobsRules.SignNoRoadRow(m_NoRoad.Contains(sign), m_MountainJobs.SignCarriesJobs(sign));
            }
            // Lodges (top-level) by their published link.
            for (int t = 0; t < m_TopLevel.Count; t++)
            {
                Entity lodge = m_TopLevel[t];
                Entity linked = m_TopLevelSigns[t];
                if (linked == Entity.Null || m_LodgeResort.ContainsKey(lodge) ||
                    !EntityManager.TryGetComponent(lodge, out PrefabRef lodgeRef) || !lodgePrefabs.Contains(lodgeRef.m_Prefab))
                    continue;
                int at = m_Index.IndexOf(ToRef(linked));
                if (at < 0)
                    continue;   // a sign the attraction walk saw and this walk did not (a frame apart): free-standing this rebuild
                m_LodgeResort[lodge] = at;
                if (EntityManager.TryGetComponent(lodge, out Building building) && !BuildingUtils.CheckOption(building, BuildingOption.Inactive))
                    m_LodgeActive.Add(lodge);
            }

            for (int k = 0; k < m_LodgeEntities.Count; k++)
            {
                Entity lodge = m_LodgeEntities[k];
                int resort = m_LodgeResort.TryGetValue(lodge, out int r) ? r : -1;
                bool free = resort < 0;
                m_LodgeIn.Add(new SkiResortAggregate.LodgeIn { Resort = resort, Score = 0, Free = free });
                if (!free && resort < resorts && m_LodgeActive.Contains(lodge))
                    m_Work[resort].LodgesActive++;
                if (!free && resort < resorts && m_NoRoad.Contains(lodge))
                {
                    // The same lodge set as the aggregate's Lodges count, so LodgesNoRoad <= Lodges.
                    Entity first = m_Work[resort].LodgeNoRoad;
                    if (m_Work[resort].LodgesNoRoad == 0 || lodge.Index < first.Index ||
                        (lodge.Index == first.Index && lodge.Version < first.Version))
                        m_Work[resort].LodgeNoRoad = lodge;
                    m_Work[resort].LodgesNoRoad++;
                }
            }
        }

        /// <summary>The top-level ski schools linked to <paramref name="sign"/> (Entity.Null = linked to none) in the last
        /// CopyTopLevelLinks (m_TopLevel, this rebuild; school prefabs only): live ones (exists, not Deleted / Temp), those whose
        /// Building.m_RoadEdge is Null, and the lowest (Index, Version) of each (stable subjects). Main thread, a handful of
        /// entities.</summary>
        private void SchoolFacts(Entity sign, out int count, out Entity first, out int noRoad, out Entity firstNoRoad)
        {
            count = noRoad = 0;
            first = firstNoRoad = Entity.Null;
            HashSet<Entity> schoolPrefabs = m_PrefabCatalog.SchoolPrefabSet;
            for (int t = 0; t < m_TopLevel.Count; t++)
            {
                Entity school = m_TopLevel[t];
                if (m_TopLevelSigns[t] != sign || school == Entity.Null || !EntityManager.Exists(school) ||
                    EntityManager.HasComponent<Deleted>(school) || EntityManager.HasComponent<Temp>(school) ||
                    !EntityManager.TryGetComponent(school, out PrefabRef schoolRef) || !schoolPrefabs.Contains(schoolRef.m_Prefab))
                    continue;
                count++;
                if (first == Entity.Null || Lower(school, first))
                    first = school;
                if (EntityManager.TryGetComponent(school, out Building building) && building.m_RoadEdge == Entity.Null)
                {
                    noRoad++;
                    if (firstNoRoad == Entity.Null || Lower(school, firstNoRoad))
                        firstNoRoad = school;
                }
            }
        }

        private static bool Lower(Entity a, Entity b) => a.Index < b.Index || (a.Index == b.Index && a.Version < b.Version);

        /// <summary>Identity, membership and run parts, snow, costs, the run tops for the served rule, and the per-area sums.</summary>
        private void CollectRuns(int resorts)
        {
            int count = m_Catalog.RowCount;
            for (int i = 0; i < count; i++)
            {
                SkiRunCatalogSystem.RunRow row = m_Catalog.GetRow(i);
                int resort = row.Gate == Entity.Null ? -1 : m_Index.IndexOf(ToRef(row.Gate));
                bool bound = resort >= 0 && resort < resorts;
                if (!bound)
                    resort = -1;
                float ha = 0f, cost = 0f;
                RunSnow snow = default;
                // A cat track counts minimally (2026-10-24, RunKindRules): a member (RunsOpen / RunsPartly / RunValueFull / Status) and a RunIn
                // (Runs, Rated, the difficulty bar, length, area), but never injected: no snow aggregate, no TopM, and NaN heights in its RunIn so the
                // hub's vertical stays the score's (SkiResortScore keeps it out of the vertical too). It follows the snowmaking rules otherwise: while its
                // guns can run it bills its plan area at the snowmaking rate, the same number SkiRunUpkeepSystem charges; a track whose gun value is
                // Off has no guns: no bill, no gun line (DECISIONS 563). A lit track adds the lights rate on its plan area in its season, whatever
                // its gun switch, in the Lights row (SkiRunUpkeepRates.TrackLitHa, DECISIONS 579).
                bool catTrack = row.Kind == (int)RunKind.CatTrack;
                bool trackGunsOn = catTrack && m_RunProps.CatTrackGunsOn(row.Entity);
                if (bound && catTrack)
                {
                    snow = m_Injection.GetRunSnow(row.Entity, (RunKind)row.Kind, row.NodeBottomM, row.NodeTopM);   // the estimate: never injected
                    bool trackInSeason = m_Injection.CatTrackGunsCanRun(row.Entity, row.NodeBottomM, row.NodeTopM);
                    float litHa = SkiRunUpkeepRates.TrackLitHa(row.Features, row.AreaM2, trackInSeason);
                    if (litHa > 0f)
                    {
                        ref AreaWork work = ref m_Work[resort];
                        cost += litHa * SkiRunUpkeepRates.LightsStepPerHaMonth;
                        work.HaLit += litHa;
                        work.CostLights += litHa * SkiRunUpkeepRates.LightsStepPerHaMonth;
                    }
                    if (trackGunsOn && trackInSeason)
                    {
                        ref AreaWork work = ref m_Work[resort];
                        ha = SnowGunPlumeRules.TrackBilledHa(row.AreaM2, true, true);
                        cost += ha * SkiRunUpkeepSystem.CostPerHaMonth(RunKind.CatTrack);
                        work.AnyGunOn = true;
                        work.HaCatTracks += ha;
                        work.CostCatTracks += cost;
                    }
                }
                else if (bound)
                {
                    ref AreaWork work = ref m_Work[resort];
                    snow = m_Injection.GetRunSnow(row.Entity, (RunKind)row.Kind, row.NodeBottomM, row.NodeTopM);
                    work.Snow.Add(row.AreaM2, snow.Share, snow.MeanDepthM, snow.Source == RunSnowSource.Estimated);
                    if (!row.Features.Groomed)
                    {
                        // Natural runs bill nothing: their plan area is reported apart.
                        if (IsFinite(row.AreaM2) && row.AreaM2 > 0f)
                            work.HaNatural += row.AreaM2 / 10000f;
                    }
                    else
                    {
                        // By feature, capped at the run's plan area: the one rule SkiRunUpkeepSystem charges (SkiSnowInjectionSystem.PisteUpkeep).
                        // Grooming and lights bill a gun run's texels at depth too; snowmaking only where its guns make snow. Attribution is
                        // first-claim in chunk order, so a run fully under an earlier-scheduled run, or narrower than the texel grid, can read 0.
                        RunUpkeep u = m_Injection.PisteUpkeep(row.Entity, row.Features, row.AreaM2);
                        ha = u.GroomedHa;
                        cost = u.Total;
                        if (row.Features.Snowmaking && u.SnowmakingHa > 0f)
                            work.AnyGunOn = true;
                        work.HaGroomed += u.GroomedHa;
                        work.HaSnowmaking += u.SnowmakingHa;
                        work.HaLit += u.LitHa;
                        work.CostGrooming += u.Grooming;
                        work.CostSnowmaking += u.Snowmaking;
                        work.CostLights += u.Lights;
                    }
                    if (IsFinite(row.TopM) && row.TopM > work.TopM)
                        work.TopM = row.TopM;
                    if (row.Features.Snowmaking)
                        work.AnyGunRun = true;
                }

                // Member: the score has a view of this run for its own bound sign (a stale view of a re-linked run is not a member of the new
                // area); members are always bound.
                SkiResortScore.RunPart part = default;
                bool member = false, served = false;
                double openNow = 0.0;   // the open factor from this read's snow (HubRules.LiveOpenFactor), not the score's last pass
                if (bound && m_Attractiveness.TryGetRunScore(row.Entity, out SkiResortAttractivenessSystem.RunScoreView view) &&
                    view.Sign == row.Gate)
                {
                    member = true;
                    part = view.Part;
                    served = view.Input.Served;
                    openNow = HubRules.LiveOpenFactor(snow.Multiplier);
                    ref AreaWork work = ref m_Work[resort];
                    if (openNow >= 1.0)
                        work.RunsOpen++;
                    else if (openNow > 0.0)
                        work.RunsPartly++;
                    work.RunsValueFull += HubRules.RunValueFull(in part);
                }

                m_RunProps.TryGetSpacing(row.Entity, out int gunSpacing, out int lightSpacing);
                m_RunIn.Add(new SkiResortAggregate.RunIn
                {
                    Resort = resort,
                    RateState = row.RateState,
                    Band = row.Band,
                    LengthM = row.LengthM,
                    AreaM2 = row.AreaM2,
                    TopM = catTrack ? float.NaN : row.TopM,
                    BottomM = catTrack ? float.NaN : row.BottomM,
                    CostMonth = cost,
                    SnowHa = ha,
                    Cover = float.NaN, // the aggregate's snow fields are unused
                    NaturalCover = float.NaN,
                });
                m_RunsNext.Add(new RunStats
                {
                    Entity = row.Entity,
                    Name = row.Name,
                    Kind = row.Kind,
                    Bound = row.Bound,
                    LengthM = row.LengthM,
                    DropM = row.DropM,
                    AreaM2 = row.AreaM2,
                    Sign = bound ? row.Gate : Entity.Null,
                    Ordinal = row.Ordinal,
                    CustomName = row.CustomName,
                    Band = row.Band,
                    GradePct = row.GradePct,
                    SteepestPct = row.SteepestPct,
                    Approx = row.Approx,
                    Member = member,
                    Served = member && served,
                    ServedBy = Entity.Null, // filled by AssignServedLifts
                    ValueFull = member ? (float)HubRules.RunValueFull(in part) : 0f,
                    BandValue = member ? (float)part.BandValue : 0f,
                    SizeFactor = member ? (float)part.SizeFactor : 0f,
                    TypeFactor = member ? (float)part.TypeFactor : 0f,
                    ServedFactor = member ? (float)part.ServedFactor : 0f,
                    GunSpacingM = gunSpacing,
                    LightSpacingM = lightSpacing,
                    PaidSteps = EntityManager.TryGetComponent(row.Entity, out SkiRunBinding binding) ? binding.m_Flags & SkiRunFees.PaidSteps : 0,
                });
                m_RunLiveNext.Add(new RunLiveStats
                {
                    Entity = row.Entity,
                    OpenState = (int)HubRules.OpenStateOf(openNow, member),
                    OpenFactor = (float)openNow,
                    SnowPct = bound ? HubRules.Percent(snow.Share) : -1,
                    SnowEstimated = bound && snow.Source == RunSnowSource.Estimated,
                    ValueNow = member ? (float)part.Value : 0f,
                    CostMonth = (int)Math.Round(cost, MidpointRounding.AwayFromZero),
                    SnowDepthCm = bound && snow.Source == RunSnowSource.Measured ? HubRules.DepthCm(snow.MeanDepthM) : -1,
                    GunWhy = catTrack   // a track set to Off is not a gun run: no gun line
                        ? (int)HubRules.GunWhyOf(bound && trackGunsOn, m_SnowLine.IsSnowGatingEnabled(), m_Injection.GunSeasonNow,
                                                 m_Injection.GunSeasonKnown, true,
                                                 bound && m_Injection.CatTrackGunsCanRun(row.Entity, row.NodeBottomM, row.NodeTopM))
                        : (int)GunWhyOf(row.Entity, bound && row.Features.Snowmaking),
                });
                m_RunTops.Add(row.NodeTopPos);
                m_RunOutlines.Add(row.OutlineXZ); // the catalog row's arrays, by reference (the link graph keeps while they are unchanged)
                m_RunHeights.Add(row.OutlineY);
                m_RunAreas.Add(bound ? resort : -1);
            }
        }

        /// <summary>The run's snow-gun line (HubRules.GunWhyOf) from the injection's per-run gun state and season.</summary>
        private RunGunWhy GunWhyOf(Entity run, bool gunRun)
        {
            if (!gunRun)
                return RunGunWhy.None;
            bool known = m_Injection.TryGetRunGunState(run, out SnowGunPlumeRules.GunState state, out float _) &&
                         state != SnowGunPlumeRules.GunState.Unknown;
            m_Injection.TryGetRunGunsRunning(run, out bool runningNow);
            return HubRules.GunWhyOf(true, m_SnowLine.IsSnowGatingEnabled(), m_Injection.GunSeasonNow, known && m_Injection.GunSeasonKnown,
                                     state == SnowGunPlumeRules.GunState.Running, runningNow);
        }

        /// <summary>The census as served-rule lifts (power read now) and the rule over every run's top node; servedBy = the serving
        /// lift's row id when the run view says served.</summary>
        private void AssignServedLifts()
        {
            long t0 = Stopwatch.GetTimestamp();
            m_Census.CopyServeLifts(m_ServeLifts, m_Chords);
            // The same rule call as the attraction path (a run skiers get onto downhill from a served run is served); Served itself
            // comes from the attraction's view, this call fills servedBy: a linked run shows the lift of the run it is served through.
            m_RunLinks.Build(m_RunOutlines, m_RunHeights, m_RunAreas);
            // Cat tracks carry service level or downhill along themselves (the track mask), so one that crosses a run below where
            // skiers get on serves it, and one above them does not.
            m_TrackRuns.Clear();
            bool anyTrack = false;
            for (int i = 0; i < m_RunsNext.Count; i++)
            {
                bool track = RunKindRules.IsCatTrackLink(m_RunsNext[i].Kind);
                m_TrackRuns.Add(track);
                anyTrack |= track;
            }
            LiftServedRule.AssignLinked(m_RunTops, m_RunLinks, m_ServeLifts, m_Chords, m_ServingLift, m_ServingDistance,
                                        m_ServedCount, m_ServedRoot, m_ServedVia, anyTrack ? m_TrackRuns : null);
            for (int i = 0; i < m_RunsNext.Count && i < m_ServingLift.Count; i++)
            {
                RunStats r = m_RunsNext[i];
                int root = m_ServedRoot[i];
                int l = root >= 0 ? m_ServingLift[root] : -1;
                // A cat track is a member since 2026-10-24: its Served comes from the attraction's view like every run's (the same
                // downhill rule runs there), and a lift's "runs served" counts it.
                if (r.Served && l >= 0 && l < m_ServeLifts.Count)
                    r.ServedBy = ToEntity(m_ServeLifts[l].Id);
                m_RunsNext[i] = r;
            }
            m_LastServeMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        }

        /// <summary>Lift rows from the census, resolved to a resort, named; plus stations, power, status, ride time, runs served and the
        /// charge.</summary>
        private void CollectLifts(int resorts)
        {
            m_CensusRows.Clear();
            m_RowBase.Clear();
            m_RowTop.Clear();
            m_Census.CopyRows(m_CensusRows);
            m_CensusResort.Clear(); // census row -> resort (the area filter of SummitServed / IfLodgesOn)
            m_BaseUse.Clear();
            m_TopUse.Clear();
            for (int k = 0; k < m_CensusRows.Count; k++)
            {
                LiftCensus.Row c = m_CensusRows[k];
                if (!c.Base.IsNull)
                {
                    Entity b = ToEntity(c.Base);
                    m_BaseUse[b] = m_BaseUse.TryGetValue(b, out int u) ? u + 1 : 1;
                }
                if (!c.Top.IsNull)
                {
                    Entity t = ToEntity(c.Top);
                    m_TopUse[t] = m_TopUse.TryGetValue(t, out int u) ? u + 1 : 1;
                }
            }
            for (int k = 0; k < m_CensusRows.Count; k++)
            {
                LiftCensus.Row c = m_CensusRows[k];
                Entity id = ToEntity(c.Id), baseStation = ToEntity(c.Base), topStation = ToEntity(c.Top);
                float baseTipY = StubTip(baseStation, out float2 baseXz);
                float topTipY = StubTip(topStation, out float2 topXz);
                float2 at;
                if (!c.Base.IsNull && IsFinite(baseTipY))
                    at = baseXz;
                else if (!c.Top.IsNull && IsFinite(topTipY))
                    at = topXz;
                else
                    at = AnchorEdgeStart(id);
                int resort = -1;
                if (m_Index.TryResolve(at, SkiRunOwnershipSystem.TetherMeters, out EntityRef sign, out float _))
                    resort = m_Index.IndexOf(sign);
                if (resort >= resorts)
                    resort = -1;
                m_CensusResort.Add(resort);
                // The row's lift type; null = untyped (NoType).
                LiftTypeDef typeDef = SkiCatalog.LiftByIndex(c.Type);
                // A Shuttle row (the tram) reads TramShuttleSystem instead of ChairPin's loop; every Loop row below is the plain loop
                // computation.
                bool shuttleRow = typeDef != null && typeDef.Mover == MoverKind.Shuttle;
                bool shuttlePath = false;
                ShuttleView shuttle = default(ShuttleView);

                int riding = 0;
                double loopM = 0.0;
                bool loop = false;
                byte state;
                bool complete = c.State == LiftCensus.State.Complete;
                switch (c.State)
                {
                    case LiftCensus.State.Complete when shuttleRow:
                        // State 0 = a shuttle path with >= 1 cabin on it (status Running, or NoPower when unpowered: the mover holds the cabins at
                        // their terminals); 1 = Complete without a path or without cabins (ChairsStopped when powered, HubRules.LiftStatusOf).
                        // Unregistered mover gives 1.
                        shuttlePath = m_Tram != null && m_Tram.TryGetShuttle(baseStation, topStation, out shuttle);
                        riding = shuttlePath ? shuttle.CabinsOnPath : 0;
                        state = (byte)(shuttlePath && riding > 0 ? 0 : 1);
                        break;
                    case LiftCensus.State.Complete:
                        loop = m_ChairPin.TryGetLoop(baseStation, topStation, out riding, out loopM);
                        state = (byte)(loop && riding > 0 ? 0 : 1);
                        if (!loop)
                            riding = 0;
                        break;
                    case LiftCensus.State.Open:
                        // A complete lift that lost a station keeps its key and opens; the hub says "not connected to a station at both ends" (3) for
                        // it, not "a section is missing" (2).
                        state = (byte)(IsLive(baseStation) && IsLive(topStation) ? 2 : 3);
                        break;
                    case LiftCensus.State.Unfinished:
                        state = 3;
                        break;
                    default:
                        state = 4;
                        break;
                }
                // Display only (the hub and the area sum; no rule reads it): riding x the row type's seats x speed x 3600 / loop for a Loop
                // type; -1 for a Shuttle type (below) or an untyped row.
                float capacity = riding > 0 && loopM > 0.0 && typeDef != null && typeDef.Mover == MoverKind.Loop
                    ? (float)(riding * typeDef.SeatsPerCarrier * (double)typeDef.SpeedMps * 3600.0 / loopM)
                    : -1f;
                // The tram's numbers come from the mover's own profile (ShuttleProfile via HubRules): capacity only for state 0 (cabins on a
                // path) and only while the mover is Running, ride time whenever a path exists. PathM is the mover's mean dock-to-dock length
                // (ShuttleView).
                float rideTimeS = complete && loop && typeDef != null ? HubRules.RideTimeS((float)loopM, typeDef.SpeedMps) : -1f;
                int chairs = c.Chairs;
                if (shuttleRow)
                {
                    // A Held (unpowered) tram carries nobody (the cabins stand at their terminals), so it adds no capacity to the area total while
                    // its status reads No power. Status (LiftStatusOf) and the cabins count (2/2) are untouched.
                    capacity = state == 0 && shuttle.State == ShuttleState.Running
                        ? HubRules.ShuttleCapacityPerHour(riding, typeDef.SeatsPerCarrier, (float)shuttle.PathM, typeDef.SpeedMps,
                                                          typeDef.AccelMps2, typeDef.DwellS)
                        : -1f;
                    rideTimeS = shuttlePath
                        ? HubRules.ShuttleRideTimeS((float)shuttle.PathM, typeDef.SpeedMps, typeDef.AccelMps2, typeDef.DwellS)
                        : -1f;
                    // A tram edge carries no carrier sub-objects (the census Row.Chairs is edge-based, 0): the row's "chairs" are the cabins hosted
                    // on its base station.
                    chairs = m_Tram != null && baseStation != Entity.Null ? m_Tram.CabinsHostedBy(baseStation) : 0;
                }

                // Power and counted: the score's view keyed by (row id, row top); a Complete row with no view uses the hub's own CopyServeLifts
                // read; not Complete is powered (not read).
                bool powered = true, counted = false;
                bool serveRow = k < m_ServeLifts.Count && m_ServeLifts[k].Id == c.Id && m_ServeLifts[k].Top == c.Top;
                if (complete)
                {
                    if (m_Attractiveness.TryGetLiftScore(id, topStation, out SkiResortAttractivenessSystem.LiftScoreView lv))
                    {
                        powered = lv.Powered;
                        counted = lv.Counted;
                    }
                    else if (serveRow)
                    {
                        powered = m_ServeLifts[k].Power != LiftPower.NoPower;
                    }
                }
                // A lift that does not carry (LiftServedRule.Carries: complete, powered at its base station - the census reads only the base's
                // consumer - and not closed) has no capacity: -1 ("–"), left out of the area total below. Status, chairs and ride time are
                // untouched. capacity is first read after this line (the area sum, CapacityPerHour), so both see the gate; a carrying row gets
                // its float back bit for bit (HubRules.LiftCapacityShown, ResortBatch HK-1). A row that is not Complete already has -1 (riding
                // 0, state != 0). closed is the charge system's settled "no open run in its ski area" flag (LiftCharge.Closed, one game hour of
                // hysteresis), the same read ChairPinSystem.IsLoopClosed and TramShuttleSystem.IsClosed use, so the hub's "–" and the stopped
                // carriers agree. Read before Carries.
                int costMonth = -1;
                bool reduced = false;
                bool closed = false;
                if (m_LiftCharge.TryGetLift(id, topStation, out SkiLiftChargeSystem.LiftCharge lc))
                {
                    costMonth = RoundToInt(lc.RatePerMonth);
                    reduced = lc.Billing != SkiMoneyRules.LiftBilling.Full;
                    closed = lc.Closed;
                }
                bool carries = LiftServedRule.Carries(complete, powered, closed);
                capacity = HubRules.LiftCapacityShown(capacity, carries);

                // Per area (Not linked = the last work row).
                ref AreaWork work = ref m_Work[resort >= 0 ? resort : resorts];
                if (state == 0 || state == 1)
                {
                    work.CompleteLifts++;
                    if (capacity >= 0f)
                    {
                        work.Capacity += capacity;
                        work.AnyCapacity = true;
                    }
                }

                m_LiftIn.Add(new SkiResortAggregate.LiftIn
                {
                    Resort = resort,
                    Id = c.Id,
                    State = state,
                    Chairs = chairs,
                    LengthM = c.LengthM,
                    RiseM = c.RiseM,
                    BaseTipY = baseTipY,
                    TopTipY = topTipY,
                });
                m_LiftsNext.Add(new LiftStats
                {
                    Entity = id,
                    Top = topStation,
                    Sign = resort >= 0 ? m_SignEntities[resort] : Entity.Null,
                    State = state,
                    Sections = c.Sections,
                    Towers = c.Towers,
                    Chairs = chairs,
                    ChairsRiding = riding, // a Shuttle row: the cabins on its path (ShuttleView.CabinsOnPath)
                    Gaps = c.Gaps,
                    TowersRemoved = c.TowersRemoved,
                    LengthM = c.LengthM,
                    RiseM = c.RiseM,
                    HorizontalM = c.HorizontalM,
                    AlignKnown = c.AlignKnown,
                    AlignOffsetM = c.AlignOffsetM,
                    AlignYawDeg = c.AlignYawDeg,
                    CapacityPerHour = capacity,
                    HasBase = IsLive(baseStation),
                    HasTop = IsLive(topStation),
                    Powered = powered,
                    Counted = counted,
                    // closed (the held-closed flag read above) makes a held lift read Closed, not Running.
                    Status = (int)HubRules.LiftStatusOf(state, powered, closed),
                    // Half the loop at the row type's speed; for a tram, the trip (the local rideTimeS above).
                    RideTimeS = rideTimeS,
                    RunsServed = serveRow && k < m_ServedCount.Count ? m_ServedCount[k] : 0,
                    CostMonth = costMonth,
                    BilledReduced = reduced,
                    // The row's type, mixed flag, seats and speed.
                    Type = typeDef != null ? typeDef.Id : "",
                    TypeIndex = c.Type,
                    Mixed = c.Mixed,
                    Seats = typeDef != null ? typeDef.SeatsPerCarrier : 0,
                    SpeedMps = typeDef != null ? typeDef.SpeedMps : 0f,
                    // The sharpest tower bend (census-decided; 0 = none over the warning angle).
                    BendTenths = LiftCensus.BendWarnTenths(c.BendDeg),
                    BendTower = c.BendTower,
                    BendNode = ToEntity(c.BendNode),
                    // The tower past its train's limits (census-decided; 0 = every train follows its rope).
                    TrainTower = c.TrainTower,
                    TrainNode = ToEntity(c.TrainNode),
                    // The lowest span under the floor (census-decided; LowSpan false = every span clears it).
                    LowSpan = c.LowSpan,
                    LowFrom = c.LowFrom,
                    LowTo = c.LowTo,
                    LowTenths = LiftCensus.LowTenths(c.LowClearM),
                    LowEdge = ToEntity(c.LowEdge),
                });
                m_RowBase.Add(baseStation);
                m_RowTop.Add(topStation);
            }
            MergeLoneStationPairs();

            // Names: ordinals per resort by top height, no-top last.
            SkiResortAggregate.LiftOrdinals(m_LiftIn, m_LiftOrder, m_LiftOrdinals);
            for (int k = 0; k < m_LiftsNext.Count; k++)
            {
                LiftStats l = m_LiftsNext[k];
                string defaultName = LiftDefault(m_LiftOrdinals[k]);
                // Live base's name, else an unshared live top's.
                Entity baseE = m_RowBase[k], topE = m_RowTop[k];
                string custom = null;
                bool fromBase = false;
                if (IsLive(baseE) && TryCustomName(baseE, out custom))
                    fromBase = true;
                else if (!(IsLive(topE) && m_TopUse.TryGetValue(topE, out int topUses) && topUses <= 1 &&
                           TryCustomName(topE, out custom)))
                    custom = null;
                if (custom != null)
                {
                    l.CustomName = true;
                    l.Name = custom;
                    if (fromBase && m_BaseUse.TryGetValue(baseE, out int uses) && uses > 1)
                    {
                        // A base station shared by two rows (LiftLoopCache.cs:14-20): "<name> → <top label>".
                        string top = TryCustomName(topE, out string topName) ? topName : defaultName;
                        l.Name = custom + LiftNameSeparator + top;
                    }
                }
                else
                {
                    l.CustomName = false;
                    l.Name = defaultName;
                }
                m_LiftsNext[k] = l;
            }
            // Row order: by resort (Not linked last), then the name ordinal.
            SortLifts();
        }

        /// <summary>A lone base and a lone top of one lift type in one area become one row, counted as one lift (HubRules.PairLoneStations):
        /// the top's row takes the base as its id, the shape a finished lift's row has, so it keeps the name the finished lift will carry.
        /// The base's row leaves m_LiftsNext, m_LiftIn, m_RowBase and m_RowTop together; the census-indexed lists stay as they are.</summary>
        private void MergeLoneStationPairs()
        {
            m_LonePairIn.Clear();
            for (int k = 0; k < m_LiftsNext.Count; k++)
            {
                LiftStats l = m_LiftsNext[k];
                m_LonePairIn.Add(new HubRules.LoneStationRow
                {
                    Area = m_LiftIn[k].Resort, State = l.State, HasBase = l.HasBase, HasTop = l.HasTop, Mixed = l.Mixed, Type = l.TypeIndex,
                });
            }
            HubRules.PairLoneStations(m_LonePairIn, m_LonePairTop);
            for (int k = m_LiftsNext.Count - 1; k >= 0; k--)
            {
                int top = m_LonePairTop[k];
                if (top < 0)
                    continue;
                LiftStats merged = m_LiftsNext[top], lone = m_LiftsNext[k];
                merged.Entity = lone.Entity;
                merged.HasBase = true;
                merged.BilledReduced |= lone.BilledReduced;
                if (lone.CostMonth >= 0)
                    merged.CostMonth = Math.Max(0, merged.CostMonth) + lone.CostMonth;
                m_LiftsNext[top] = merged;
                SkiResortAggregate.LiftIn inTop = m_LiftIn[top];
                inTop.BaseTipY = m_LiftIn[k].BaseTipY;
                m_LiftIn[top] = inTop;
                m_RowBase[top] = m_RowBase[k];
                // Every index above k is already compacted, and a top row index above k shifts with the removal: fix the pairs still to come.
                m_LiftsNext.RemoveAt(k);
                m_LiftIn.RemoveAt(k);
                m_RowBase.RemoveAt(k);
                m_RowTop.RemoveAt(k);
                for (int b = 0; b < k; b++)
                {
                    if (m_LonePairTop[b] > k)
                        m_LonePairTop[b]--;
                }
            }
        }

        /// <summary>A station entity that exists and is not being deleted (Entity.Null gives false). A census key may hold a removed
        /// station.</summary>
        private bool IsLive(Entity e)
        {
            return e != Entity.Null && EntityManager.Exists(e) && !EntityManager.HasComponent<Deleted>(e);
        }

        /// <summary>NameSystem's non-empty custom name (NameSystem.cs:209-212); false for Entity.Null.</summary>
        private bool TryCustomName(Entity e, out string name)
        {
            name = null;
            if (e == Entity.Null || !m_NameSystem.TryGetCustomName(e, out string n) || string.IsNullOrEmpty(n))
                return false;
            name = n;
            return true;
        }

        /// <summary>Stub tip y of a station (Transform ∘ local (0, 0, StubTipLocalZ)) and its xz; NaN when the entity is Null or has no
        /// Transform. StationAlign.StubTip, the same expression (position + mul(rotation, (0, 0, StubTipLocalZ))).</summary>
        private float StubTip(Entity station, out float2 xz)
        {
            xz = new float2(float.NaN, float.NaN);
            if (station == Entity.Null || !EntityManager.TryGetComponent(station, out Game.Objects.Transform t))
                return float.NaN;
            // The station row's own stub tip z (-16 on the detachable / gondola stations); an unknown station keeps -12.
            float stubZ = StationAlign.StubTipLocalZ;
            if (EntityManager.TryGetComponent(station, out PrefabRef pr) && m_PrefabCatalog.TryGetStation(pr.m_Prefab, out StationDef def) &&
                def != null)
                stubZ = def.StubTipLocalZ;
            float3 tip = StationAlign.StubTip(t.m_Position, t.m_Rotation, stubZ);
            xz = t.m_Position.xz;
            return tip.y;
        }

        /// <summary>A station-less row's anchor: its id edge's start node (Game.Net.Edge.m_Start → Game.Net.Node.m_Position); NaN when
        /// unreadable (TryResolve then returns Not linked).</summary>
        private float2 AnchorEdgeStart(Entity edge)
        {
            if (edge != Entity.Null && EntityManager.TryGetComponent(edge, out Game.Net.Edge e) &&
                EntityManager.TryGetComponent(e.m_Start, out Game.Net.Node n))
                return n.m_Position.xz;
            return new float2(float.NaN, float.NaN);
        }

        private void SortLifts()
        {
            // Insertion sort on (resort row index with Not linked last, ordinal): lifts are few.
            for (int i = 1; i < m_LiftsNext.Count; i++)
            {
                LiftStats a = m_LiftsNext[i];
                SkiResortAggregate.LiftIn ai = m_LiftIn[i];
                int ao = m_LiftOrdinals[i];
                int j = i - 1;
                while (j >= 0 && CompareLiftRows(m_LiftIn[j], m_LiftOrdinals[j], ai, ao) > 0)
                {
                    m_LiftsNext[j + 1] = m_LiftsNext[j];
                    m_LiftIn[j + 1] = m_LiftIn[j];
                    m_LiftOrdinals[j + 1] = m_LiftOrdinals[j];
                    j--;
                }
                m_LiftsNext[j + 1] = a;
                m_LiftIn[j + 1] = ai;
                m_LiftOrdinals[j + 1] = ao;
            }
        }

        private static int CompareLiftRows(SkiResortAggregate.LiftIn a, int ao, SkiResortAggregate.LiftIn b, int bo)
        {
            int ra = a.Resort < 0 ? int.MaxValue : a.Resort, rb = b.Resort < 0 ? int.MaxValue : b.Resort;
            if (ra != rb)
                return ra < rb ? -1 : 1;
            return ao.CompareTo(bo);
        }

        private void BuildResortRows(int resorts, double sAll, CityStats city)
        {
            Entity cityEntity = m_CitySystem.City;
            bool haveModifiers = cityEntity != Entity.Null && EntityManager.HasBuffer<CityModifier>(cityEntity);
            Func<float, float> modifier = null;
            if (haveModifiers)
            {
                DynamicBuffer<CityModifier> modifiers = EntityManager.GetBuffer<CityModifier>(cityEntity, isReadOnly: true);
                // TourismSystem.cs:106: CityUtils.ApplyModifier(ref num, modifiers, Attractiveness), before rounding.
                modifier = x =>
                {
                    CityUtils.ApplyModifier(ref x, modifiers, CityModifierType.Attractiveness);
                    return x;
                };
            }

            // Upkeep inputs (CityServiceBudgetSystem.cs:392-421), fetched once per rebuild.
            bool upkeepReady = PrepareUpkeep(out EconomyParameterData economy, out ResourcePrefabs resourcePrefabs);

            // The game's TouristIncome statistic, once per rebuild (GetStatisticValue completes the statistics writers, a short sync); not
            // read under Tourism Overhaul (the UI hides the row), -1 when unavailable.
            int touristIncome = -1;
            if (!city.TourismOverhaul && m_CityStatistics != null)
            {
                try
                {
                    touristIncome = Math.Max(0, m_CityStatistics.GetStatisticValue(StatisticType.TouristIncome));
                }
                catch (Exception)
                {
                    touristIncome = -1;   // the statistic is an estimate input only; never take the hub down for it
                }
            }

            // Climate and season inputs, read once.
            bool gating = city.SnowGating;
            float freezing = m_SnowLine.GetFreezingTemperature();
            float margin = city.SnowmakingMarginC;
            float dayMean = m_SnowLine.GetDayMeanTemperature();
            float baseHeight = m_SnowLine.GetBaseHeight();
            float lapse = m_SnowLine.GetLapsePerM();
            // The live gun gates are known once the injection has measured a mask this load and is active (HasMaskMeasurement is false from
            // the load to the first mask pass); in that window Season falls back to the mean. An injection that is not active (not armed, or
            // disabled for the rest of the load after a "!!!") injects nothing, so it is known too and reads "no gun running" instead of the
            // mean's guess.
            bool injectionOn = m_Injection.GetSnowSummary().InjectionActive;
            bool gunsLive = !injectionOn || m_Injection.HasMaskMeasurement;

            // Guests and the counted month fraction.
            SkiResortLedger ledger = m_GuestCounter.Ledger;
            bool counterArmed = m_GuestCounter.IsArmed && ledger != null;
            bool sinceLoad = ledger != null && ledger.ThisMonthSinceLoad;
            bool lastMonthFull = ledger != null && ledger.LastMonthKnown && !ledger.LastMonthSinceLoad;
            // The six last-month guest values are sent whenever a month rolled since counting started (full or since load); LastMonthFull
            // marks the partial one. Ticket income still needs a full month.
            bool lastKnown = ledger != null && ledger.LastMonthKnown;
            // A since-load month is counted from the counter's start frame (the first tick after the load, or a calendar jump), not always
            // from the load.
            uint countStart = m_GuestCounter.HasCountingSinceFrame ? m_GuestCounter.CountingSinceFrame : m_LoadFrameIndex;
            float countedFraction = HubRules.CountedFraction(sinceLoad, m_TimeSystem.normalizedDate,
                unchecked(m_Simulation.frameIndex - countStart), m_TimeSystem.daysPerYear);
            // The share of the month in progress simulated, which scales the buildings / wages rates into the month's Net; the frames the
            // ledger's month has booked (the counter's, as the History binding read it). -1 unknown.
            double monthShare = counterArmed
                ? HubRules.MonthShare(m_GuestCounter.MonthFramesSimulated, SkiMoneyRules.MonthFrames(m_TimeSystem.daysPerYear))
                : -1.0;
            int price = city.TicketPrice;

            // Ski days are "off" when both settings are 0 (the clamped readers), once per rebuild; BuildProblems reads the field.
            // SkiDaySystem's views are read per sign below (main thread, no job sync).
            SkiResortSettings settings = Mod.Settings;
            m_SkiDaysOff = SkiResortSettings.SkiDayLocalsPctOf(settings) == 0 && SkiResortSettings.SkiDayTouristsPctOf(settings) == 0;
            bool skiDaysArmed = m_SkiDays != null && m_SkiDays.IsArmed;
            // The "Lodge spend per guest" setting, clamped as the ticket job reads it; 0 = off (lodgeSpendMonth 0, nothing estimated, the
            // net unchanged).
            int lodgeSpendSetting = SkiMoneyRules.ClampLodgeSpend(settings != null ? settings.LodgeSpendPerGuest
                                                                                     : SkiMoneyRules.LodgeSpendDefault);

            // Names: custom names first; an unnamed sign's display-only label is the smallest "Ski area N" no custom name and no earlier
            // unnamed sign's label uses (SkiResortAggregate.FallbackOrdinals), never a duplicate.
            m_SignCustomNames.Clear();
            for (int s = 0; s < resorts; s++)
                m_SignCustomNames.Add(TryCustomName(m_SignEntities[s], out string cn) ? cn : null);
            SkiResortAggregate.FallbackOrdinals(m_SignCustomNames, m_ResortDefaultFormat, m_NameScratch, m_SignFallbackN,
                ResortNameMaxN);

            for (int s = 0; s <= resorts; s++)
            {
                bool notLinked = s == resorts;
                SkiResortAggregate.Agg a = m_Agg[s];
                AreaWork work = m_Work[s];
                Entity sign = notLinked ? Entity.Null : m_SignEntities[s];
                var row = new ResortStats
                {
                    Sign = sign,
                    Kind = notLinked ? 1 : 0,
                    Runs = a.Runs,
                    Rated = a.Rated,
                    Flat = a.Flat,
                    Green = a.Green,
                    Blue = a.Blue,
                    Black = a.Black,
                    DoubleBlack = a.DoubleBlack,
                    AreaM2 = a.AreaM2,
                    VerticalM = a.RunVerticalM,
                    VerticalApprox = a.VerticalApprox && a.RunVerticalM >= 0f,
                    Lodges = a.Lodges,
                    FreeLodges = a.FreeLodges,
                    LiftRows = a.Lifts,
                    LiftLengthM = a.LiftLengthM,
                    CapacityPerHour = work.AnyCapacity ? work.Capacity : -1f,
                };

                // Name: the sign's custom name, else its smallest free "Ski area N" (above).
                if (notLinked)
                {
                    row.Name = m_UnlinkedName;
                }
                else if (m_SignCustomNames[s] != null)
                {
                    row.Name = m_SignCustomNames[s];
                    row.CustomName = true;
                }
                else
                {
                    int n = m_SignFallbackN[s];
                    row.Name = ResortDefault(n > 0 ? n : s + 1);
                }
                m_ResortsNext.Add(row);

                if (notLinked)
                {
                    m_ResortLiveNext.Add(NotLinkedLive());
                    continue;
                }

                bool hasResult = m_Attractiveness.TryGetAreaScore(sign, out SkiResortAttractivenessSystem.AreaScoreView v);
                m_Work[s].HasResult = hasResult;
                m_Work[s].View = v;
                SkiResortScore.Result r = v.Result;
                var live = new ResortLiveStats { Sign = sign };
                if (hasResult)
                {
                    live.RunsOpen = work.RunsOpen;
                    live.RunsPartly = work.RunsPartly;
                    live.Attraction = r.Attraction;
                    live.AttractionFull = r.AttractionFullEfficiency;
                    live.AttractionHeld = v.WriteHeld;
                    live.LodgesActive = r.ActiveLodges;
                    live.Summer = (r.Flags & SkiResortScore.ScoreFlags.SummerMode) != 0;
                    live.LodgePart = (float)HubRules.LodgePart(in r);
                    live.Terrain = (float)HubRules.Terrain(in r);
                    live.RunsValue = (float)r.RunsValue;
                    live.OpenSharePct = HubRules.OpenSharePct(r.RunsValue, work.RunsValueFull);
                    // The season's lift term of S, summed per type (equals LiftsValue x LiftShare for fixed-grip only: lambda is the one share).
                    live.LiftsValue = (float)r.LiftsApplied;
                    live.LiftsComplete = r.LiftsComplete;
                    live.LiftsCounted = r.LiftsCounted;
                    live.VarietyMultiplier = (float)r.VarietyMultiplier;
                    live.VerticalMultiplier = (float)r.VerticalMultiplier;
                    live.ScoreVerticalM = (float)r.VerticalM;
                    live.EfficiencyPct = v.EfficiencyUsedPct;
                    // The ladder, school and patrol facts of the Result.
                    LodgeDef best = r.LodgeBestIndex >= 0 && r.LodgeBestIndex < SkiCatalog.Lodges.Count
                        ? SkiCatalog.Lodges[r.LodgeBestIndex] : null;
                    live.LodgeBest = best != null ? best.BuildItemId ?? "" : "";
                    live.LodgeExtra = (float)r.LodgeExtraApplied;
                    live.SchoolValue = (float)r.SchoolValue;
                    live.PatrolledRuns = r.RunsPatrolled;
                    live.PriceTolerance = r.PriceTolerance;
                }
                else
                {
                    // Just placed, the attraction system disabled, or before its first recompute.
                    live.Attraction = live.AttractionFull = live.LodgesActive = -1;
                    live.LiftsComplete = live.LiftsCounted = live.EfficiencyPct = live.OpenSharePct = -1;
                    live.VarietyMultiplier = live.VerticalMultiplier = 1f;
                    live.LodgeBest = ""; // no result: no best lodge, the counts 0
                }
                live.RunsValueFull = (float)work.RunsValueFull;
                live.Status = (int)HubRules.Status(row.Runs, live.RunsOpen, live.RunsPartly);

                live.SnowPct = work.Snow.SnowPct();
                live.SnowDepthCm = work.Snow.DepthCm();
                live.SnowEstimated = work.Snow.AnyEstimated;
                float topTemp = IsFinite(work.TopM) ? HubRules.TopTempC(dayMean, work.TopM, baseHeight, lapse) : float.NaN;
                live.TopTempKnown = !float.IsNaN(topTemp);
                live.TopTempC = live.TopTempKnown ? topTemp : 0f;
                live.SeasonHint = (int)HubRules.Season(gating, row.Runs, live.RunsOpen + live.RunsPartly, work.AnyGunRun,
                    gunsLive, injectionOn && work.AnyGunOn, live.TopTempKnown, topTemp, freezing, margin);

                // Share, estimate, jobs (the provider walk, the sign's workplace).
                double sRes = 0.0;
                bool anyProvider = false;
                if (m_ProviderOf.TryGetValue(sign, out int signA))
                {
                    sRes += SkiResortTourism.Term(signA);
                    anyProvider = true;
                }
                for (int k = 0; k < m_LodgeEntities.Count; k++)
                {
                    Entity lodge = m_LodgeEntities[k];
                    if (!m_LodgeResort.TryGetValue(lodge, out int lr) || lr != s)
                        continue;
                    if (m_ProviderOf.TryGetValue(lodge, out int la))
                    {
                        sRes += SkiResortTourism.Term(la);
                        anyProvider = true;
                    }
                }
                live.SharePct = -1f;
                live.CityIncomeEst = -1;
                if (anyProvider)
                {
                    live.SharePct = SkiResortTourism.SharePct(sRes, sAll);
                    // The resort's share of the city's draw x the TouristIncome statistic (CityIncomeEst).
                    if (touristIncome >= 0 && live.SharePct >= 0f)
                        live.CityIncomeEst = ClampToInt((long)Math.Round(live.SharePct * (double)touristIncome / 100.0, MidpointRounding.AwayFromZero));
                    if (haveModifiers && sRes > 0.0)
                    {
                        SkiResortTourism.Estimate(sAll, sRes, modifier, s_TargetTourists, out int _, out int _,
                            out int tNow, out int tWithout);
                        live.EstKnown = true;
                        live.EstTourists = tNow - tWithout;
                    }
                }
                int jobs = 0, filled = 0;
                AddJobs(sign, ref jobs, ref filled);
                // A linked top-level building's jobs are on its own WorkProvider (lodges, the school). The patrol huts' and mountain lodges'
                // jobs, and an off-road lodge's on a cat track chain, ride on the area's carrier (MountainJobsSystem): the nearest active road
                // lodge, counted below with its own, else the sign, counted by AddJobs(sign) above. Each job is counted once either way.
                for (int t = 0; t < m_TopLevel.Count; t++)
                {
                    if (m_TopLevelSigns[t] == sign)
                        AddJobs(m_TopLevel[t], ref jobs, ref filled);
                }
                live.Jobs = jobs;
                live.JobsFilled = filled;
                live.NextStep = (int)HubRules.NextStep(row.Lodges, work.CompleteLifts, row.Runs);

                // Guests and tickets.
                live.ThisMonthSinceLoad = sinceLoad;
                live.LastMonthFull = lastMonthFull;
                int ticketIncome, ticketPaid;
                bool ticketEstimated;
                int lodgeSpendMonth = -1; // -1 unknown
                bool lodgeSpendEstimated = true;
                SkiResortLedger.Month booked = default; // the month in progress as booked (the Net's six booked terms)
                if (counterArmed)
                {
                    SkiResortLedger.Entry e = m_GuestCounter.TryGetMonth(sign, out SkiResortLedger.Entry entry) ? entry : default;
                    booked = e.This;
                    // The lodge spend per month by the tickets' month rule.
                    lodgeSpendMonth = HubRules.TicketIncomeMonth(lastMonthFull, e.Last.LodgeSpendIncome, e.Last.LodgeSpends,
                        e.This.LodgeSpendIncome, e.This.LodgeSpends, countedFraction, out int _, out lodgeSpendEstimated);
                    live.GuestsThisMonth = e.This.Guests;
                    live.TouristsThisMonth = e.This.TouristGuests;
                    live.TouristGroupsThisMonth = e.This.TouristParties;
                    live.ResidentsThisMonth = e.This.LocalGuests;
                    live.PaidThisMonth = e.This.TicketsPaid;
                    live.UnpaidThisMonth = e.This.TicketsUnpaid;
                    live.FreeThisMonth = e.This.TicketsFree;
                    live.TicketsThisMonth = ClampToInt(Math.Max(0L, e.This.TicketIncome));
                    live.GuestsLastMonth = lastKnown ? e.Last.Guests : -1; // lastKnown, not lastMonthFull
                    live.TouristsLastMonth = lastKnown ? e.Last.TouristGuests : -1;
                    live.TouristGroupsLastMonth = lastKnown ? e.Last.TouristParties : -1;
                    live.ResidentsLastMonth = lastKnown ? e.Last.LocalGuests : -1;
                    live.PaidLastMonth = lastKnown ? e.Last.TicketsPaid : -1;
                    live.TicketsLastMonth = lastKnown ? ClampToInt(Math.Max(0L, e.Last.TicketIncome)) : -1;
                    // The season's sales (the ledger keeps them across month rolls) and the holders by month.
                    int seasonKey = SkiMoneyRules.SeasonKeyOfDate(m_TimeSystem.year, m_TimeSystem.normalizedDate,
                                                                  SkiClimateCalendar.SeasonFirstMonth(m_ClimateSystem, m_PrefabSystem));
                    live.PassesSoldSeason = SkiResortLedger.PassSeasonSoldOf(e, seasonKey);
                    live.PassIncomeSeason = ClampToInt(SkiResortLedger.PassSeasonIncomeOf(e, seasonKey));
                    live.PassHoldersThisMonth = e.This.PassHolderGuests;
                    live.PassHoldersLastMonth = lastKnown ? e.Last.PassHolderGuests : -1;
                    // Lessons by month, the lesson income so far this month, the pre-season part of the season's passes.
                    live.LessonsThisMonth = e.This.Lessons;
                    live.LessonsLastMonth = lastKnown ? e.Last.Lessons : -1;
                    live.LessonIncomeMonth = ClampToInt(Math.Max(0L, e.This.LessonIncome));
                    live.PassesPreSeason = SkiResortLedger.PassSeasonPreOf(e, seasonKey);
                    live.PassIncomePreSeason = ClampToInt(SkiResortLedger.PassSeasonPreIncomeOf(e, seasonKey));
                    ticketIncome = HubRules.TicketIncomeMonth(lastMonthFull, e.Last.TicketIncome, e.Last.TicketsPaid,
                        e.This.TicketIncome, e.This.TicketsPaid, countedFraction, out ticketPaid, out ticketEstimated);
                }
                else
                {
                    live.GuestsThisMonth = live.TouristsThisMonth = live.TouristGroupsThisMonth = live.ResidentsThisMonth = -1;
                    live.PaidThisMonth = live.UnpaidThisMonth = live.FreeThisMonth = live.TicketsThisMonth = -1;
                    live.GuestsLastMonth = live.TouristsLastMonth = live.TouristGroupsLastMonth = live.ResidentsLastMonth = -1;
                    live.PaidLastMonth = live.TicketsLastMonth = -1;
                    live.PassesSoldSeason = live.PassIncomeSeason = live.PassHoldersThisMonth = live.PassHoldersLastMonth = -1;
                    live.LessonsThisMonth = live.LessonsLastMonth = live.LessonIncomeMonth = live.PassesPreSeason = live.PassIncomePreSeason = -1;
                    ticketIncome = ticketPaid = -1;
                    ticketEstimated = true;
                }
                if (price == 0)
                {
                    // Tickets off: nothing to estimate, no "≈" without a reason.
                    ticketIncome = ticketPaid = 0;
                    ticketEstimated = false;
                }
                live.TicketIncomeMonth = ticketIncome;
                live.TicketPaidMonth = ticketPaid;
                live.TicketIncomeEstimated = ticketEstimated;
                if (lodgeSpendSetting == 0 || price == 0)
                {
                    // The setting off (or tickets off: nobody pays, nobody spends) gives 0, not estimated.
                    lodgeSpendMonth = 0;
                    lodgeSpendEstimated = false;
                }
                live.LodgeSpendMonth = lodgeSpendMonth;

                // SkiDaySystem's last published view of the sign, else -1 (State and Limit 0). `d = default` first: definite assignment does
                // not flow through `has` (CS0165).
                SkiDayAreaView d = default;
                bool has = skiDaysArmed && m_SkiDays.TryGetArea(sign, out d);
                m_Work[s].HasSkiDays = has;
                m_Work[s].SkiDays = d;
                if (has)
                {
                    live.SkiDaysThisMonth = d.ArrivedThisMonth;
                    live.SkiDaysLastMonth = d.ArrivedLastMonth; // -1 until a month roll (partial: LastMonthFull false)
                    live.SkiDaysSent = d.SentThisMonth;
                    live.SkiDaysSentCar = d.SentCarThisMonth;
                    live.SkiDaysSentNoCar = d.SentNoCarThisMonth;
                    live.SkiDaysNow = d.OnTheWayOrOnSite;
                    live.SkiDaysCap = d.Capacity;
                    live.SkiDaysLimit = (int)d.Limit;
                    live.SkiDaysPowderPct = HubRules.Percent(d.Powder);   // NaN (unknown) → −1
                }
                else
                {
                    live.SkiDaysThisMonth = live.SkiDaysLastMonth = live.SkiDaysSent = live.SkiDaysSentCar = -1;
                    live.SkiDaysSentNoCar = live.SkiDaysNow = live.SkiDaysCap = live.SkiDaysPowderPct = -1;
                    live.SkiDaysLimit = 0;
                }
                live.SkiDaysState = (int)HubRules.SkiDayStateOf(m_SkiDaysOff, has, hasResult && !v.WriteHeld,
                    hasResult && r.Efficiency > 0.0, hasResult ? r.RunsCounted : 0, has ? d.TargetLodges : 0, has && d.ClaimHours,
                    live.SkiDaysNow, live.SkiDaysCap);

                // Lift charge (Reduced counts NoOpenRun too) and the ticket gate (the ticket system's source and rule: known = a result present
                // and not in the first-write hold).
                float liftRate = 0f;
                if (m_LiftCharge.TryGetResort(sign, out SkiLiftChargeSystem.ResortLiftCharge charge))
                {
                    liftRate = Finite0(charge.RatePerMonth);
                    live.LiftCostMonth = RoundToInt(liftRate);
                    live.LiftsBilled = charge.Lifts;
                    live.LiftsBilledReduced = charge.Reduced;
                }
                // The bus depots: the ski days' view when it has one; else the attraction snapshot's active depots.
                live.Depots = has ? d.Depots : m_Attractiveness.CopyActiveDepots(sign, m_DepotScratch);
                live.BusGuestsMonth = has ? d.DepotArrivedThisMonth : -1;
                live.BusBillMonth = RoundToInt(Finite0(m_BusCharge.MonthlyRateOf(sign)));
                bool gateKnown = hasResult && !v.WriteHeld;
                live.TicketGate = (int)SkiMoneyRules.TicketGateOf(price, hasResult ? (float)r.Efficiency : 1f, gateKnown,
                    gateKnown ? r.RunsCounted : 0);

                // Run costs and hectares (float sums, rounded once).
                live.CostGroomingMonth = RoundToInt(work.CostGrooming);
                live.CostSnowmakingMonth = RoundToInt(work.CostSnowmaking);
                live.CostLightsMonth = RoundToInt(work.CostLights);
                live.CostCatTracksMonth = RoundToInt(work.CostCatTracks);
                live.HaNatural = work.HaNatural;
                live.HaGroomed = work.HaGroomed;
                live.HaSnowmaking = work.HaSnowmaking;
                live.HaLit = work.HaLit;
                live.HaCatTracks = work.HaCatTracks;

                // Buildings and wages (game formula at the current service budget), then the net.
                float buildingsF = -1f, wagesF = -1f;
                if (upkeepReady && BuildingCostSplit(sign, m_SignPrefabs[s], economy, resourcePrefabs, out float b, out float w))
                {
                    buildingsF = b;
                    wagesF = w;
                    // A linked top-level building is billed on its own (CityServiceBuilding): the same game formula per building, summed.
                    for (int t = 0; t < m_TopLevel.Count; t++)
                    {
                        Entity school = m_TopLevel[t];
                        if (m_TopLevelSigns[t] != sign || !EntityManager.TryGetComponent(school, out PrefabRef schoolRef))
                            continue;
                        if (BuildingCostSplit(school, schoolRef.m_Prefab, economy, resourcePrefabs, out float schoolB, out float schoolW))
                        {
                            buildingsF += schoolB;
                            wagesF += schoolW;
                        }
                    }
                }
                live.BuildingsMonth = buildingsF >= 0f ? RoundToInt(buildingsF) : -1;
                live.WagesMonth = wagesF >= 0f ? RoundToInt(wagesF) : -1;
                // The Net of the month in progress (EngineNotes/HubRules.md ONE NET) = what the ledger booked this month (tickets, lodge spend,
                // lessons, passes sold, lift charge, run upkeep) - buildings / wages x the share simulated. The rows go out as the Net fields,
                // from the same HubMonthMoney, so the UI's rows add up to NetMonth exactly. Before the counter arms every term is unknown.
                live.MonthShare = monthShare >= 0.0 ? (float)monthShare : -1f;
                live.BuildingsSoFar = counterArmed ? HubRules.RateSoFar(live.BuildingsMonth, monthShare) : -1;
                live.WagesSoFar = counterArmed ? HubRules.RateSoFar(live.WagesMonth, monthShare) : -1;
                HubMonthMoney money = HubMonthMoney.Unknown;
                if (counterArmed)
                    money = HubRules.MonthMoneyOf(booked, live.BuildingsSoFar, live.WagesSoFar);
                else
                {
                    money.BuildingsCost = live.BuildingsSoFar;
                    money.WagesCost = live.WagesSoFar;
                }
                SetNetRows(ref live, in money);
                live.PassIncomePreMonth = counterArmed ? ClampToInt(Math.Max(0L, booked.PassIncomePre)) : -1;
                live.NetMonth = HubRules.NetMonth(money, out bool netEstimated);
                live.NetEstimated = netEstimated;
                m_ResortLiveNext.Add(live);
            }
        }

        /// <summary>The Not linked live row: -1 for every score, guest and ticket number, 0 / 1 / false elsewhere; only the lift-charge
        /// fields carry values (unlinked lifts bill, unlinked runs do not), and the net is minus the lift charge, not
        /// estimated.</summary>
        private ResortLiveStats NotLinkedLive()
        {
            var live = new ResortLiveStats
            {
                Sign = Entity.Null,
                RunsOpen = -1, RunsPartly = -1, Attraction = -1, AttractionFull = -1, LodgesActive = -1,
                LiftsComplete = -1, LiftsCounted = -1, EfficiencyPct = -1, OpenSharePct = -1,
                SnowPct = -1f, SnowDepthCm = -1f, SharePct = -1f,
                GuestsThisMonth = -1, TouristsThisMonth = -1, TouristGroupsThisMonth = -1, ResidentsThisMonth = -1,
                PaidThisMonth = -1, UnpaidThisMonth = -1, FreeThisMonth = -1, TicketsThisMonth = -1,
                GuestsLastMonth = -1, TouristsLastMonth = -1, TouristGroupsLastMonth = -1, ResidentsLastMonth = -1,
                PaidLastMonth = -1, TicketsLastMonth = -1,
                TicketGate = -1, TicketIncomeMonth = -1, TicketPaidMonth = -1,
                BuildingsMonth = -1, WagesMonth = -1,
                VarietyMultiplier = 1f, VerticalMultiplier = 1f,
                // no ski area, no ski days: State and Limit 0, the other eight -1.
                SkiDaysState = 0, SkiDaysLimit = 0,
                SkiDaysThisMonth = -1, SkiDaysLastMonth = -1, SkiDaysSent = -1, SkiDaysSentCar = -1, SkiDaysSentNoCar = -1,
                SkiDaysNow = -1, SkiDaysCap = -1, SkiDaysPowderPct = -1,
                // no ski area: no best lodge, the counts 0, the lodge spend unknown.
                LodgeBest = "", LodgeSpendMonth = -1,
                // no ski area: no bus depot (one links to the nearest sign at any distance).
                BusGuestsMonth = -1,
                // no ski area: the pass numbers unknown.
                PassesSoldSeason = -1, PassIncomeSeason = -1, PassHoldersThisMonth = -1, PassHoldersLastMonth = -1,
                // no ski area: the lesson, pre-season and city-income numbers unknown.
                LessonsThisMonth = -1, LessonsLastMonth = -1, LessonIncomeMonth = -1, PassesPreSeason = -1, PassIncomePreSeason = -1, CityIncomeEst = -1,
            };
            if (m_LiftCharge.TryGetResort(Entity.Null, out SkiLiftChargeSystem.ResortLiftCharge charge))
            {
                live.LiftCostMonth = RoundToInt(Finite0(charge.RatePerMonth));
                live.LiftsBilled = charge.Lifts;
                live.LiftsBilledReduced = charge.Reduced;
            }
            // Not linked has no income and no buildings; its Net is minus what the ledger booked this month for unlinked lifts and runs
            // (EntityRef.Null), not estimated. The income rows stay -1 (no ski area), so the invariant sum of max(0, row) holds; before the
            // counter arms the two cost rows are -1 and the Net reads 0, estimated.
            SkiResortLedger ledger = m_GuestCounter.Ledger;
            bool armed = m_GuestCounter.IsArmed && ledger != null;
            SkiResortLedger.Month booked = armed && m_GuestCounter.TryGetMonth(Entity.Null, out SkiResortLedger.Entry e) ? e.This : default;
            HubMonthMoney money = HubMonthMoney.Unknown;
            if (armed)
            {
                HubMonthMoney m = HubRules.MonthMoneyOf(booked, 0, 0);
                money.LiftCost = m.LiftCost;
                money.RunCost = m.RunCost;
                money.BusCost = m.BusCost;
            }
            SetNetRows(ref live, in money);
            live.PassIncomePreMonth = -1;
            live.MonthShare = -1f;
            // the income rows and the bill are not terms of Not linked (0, known), so only an unarmed ledger makes the Net "≈"
            live.NetMonth = HubRules.NetMonth(new HubMonthMoney
            {
                LiftCost = money.LiftCost, RunCost = money.RunCost, BusCost = money.BusCost,
            }, out bool _);
            live.NetEstimated = !armed;
            return live;
        }

        /// <summary>The Net rows (and the two earlier rows that are Net terms: TicketsThisMonth, LessonIncomeMonth) from the terms
        /// NetMonth uses, so the UI's rows and the Net come from one HubMonthMoney.</summary>
        private static void SetNetRows(ref ResortLiveStats live, in HubMonthMoney money)
        {
            live.TicketsThisMonth = money.TicketIncome;
            live.LodgeIncomeMonth = money.LodgeIncome;
            live.LessonIncomeMonth = money.LessonIncome;
            live.PassIncomeMonth = money.PassIncome;
            live.LiftChargedMonth = money.LiftCost;
            live.RunCostMonth = money.RunCost;
            live.BuildingsSoFar = money.BuildingsCost;
            live.WagesSoFar = money.WagesCost;
            live.BusChargedMonth = money.BusCost;
        }

        private void AddJobs(Entity building, ref int jobs, ref int filled)
        {
            // WorkProvider.m_MaxWorkers (missing = 0) and the Employee buffer length (Workplace.cs:38-51;
            // EmployeesSection.cs:87-93).
            if (EntityManager.TryGetComponent(building, out WorkProvider wp))
                jobs += wp.m_MaxWorkers;
            if (EntityManager.HasBuffer<Employee>(building))
                filled += EntityManager.GetBuffer<Employee>(building, isReadOnly: true).Length;
        }

        /// <summary>The per-rebuild inputs of BuildingCostSplit; false (every building cost −1) when
        /// the economy singleton or the resource prefabs are not available.</summary>
        private bool PrepareUpkeep(out EconomyParameterData economy, out ResourcePrefabs resourcePrefabs)
        {
            economy = default;
            resourcePrefabs = default;
            if (m_EconomyQuery.IsEmptyIgnoreFilter)
                return false;
            economy = m_EconomyQuery.GetSingleton<EconomyParameterData>();
            resourcePrefabs = m_ResourceSystem.GetPrefabs();   // ResourceSystem.cs:172
            // The lookups are read on the main thread: complete their writers first.
            CompleteDependency();
            m_ServiceUpkeepLookup.Update(this);
            m_InstalledUpgradeLookup.Update(this);
            m_PrefabRefLookup.Update(this);
            m_ServiceUsageLookup.Update(this);
            m_EmployeeLookup.Update(this);
            if (!m_UpkeepScratchList.IsCreated)
                m_UpkeepScratchList = new NativeList<ServiceUpkeepData>(4, Allocator.Persistent);
            return true;
        }

        /// <summary>CityServiceBudgetSystem.cs:392-421 for ONE sign (its installed lodges merged by GetUpkeepWithUsageScale), split into
        /// the building part and the wage part, per month at the current service budget: x budget / 100 x daysPerYear / 12. Per entry
        /// with amount &gt; 0: RoundToInt(value), value including the wage for the Money entry and x 0.1 when inactive, as vanilla's
        /// m_FullCost (:420); the wage part = the wage when a Money entry was seen (it is 0 for an inactive sign,
        /// CityServiceUpkeepSystem.cs:628-633). Rounding before the budget scale differs from vanilla's m_Cost by &lt; 1 credit per entry
        /// per day ("game formula"). False (both -1) when the prefab has no ServiceObjectData or ServiceUpkeepData, or the budget reads
        /// &lt;= 0 (no budget entry yet, :1063-1066).</summary>
        private bool BuildingCostSplit(Entity sign, Entity prefab, EconomyParameterData economy, ResourcePrefabs resourcePrefabs,
                                       out float buildingsMonth, out float wagesMonth)
        {
            buildingsMonth = -1f;
            wagesMonth = -1f;
            if (prefab == Entity.Null || !EntityManager.TryGetComponent(prefab, out ServiceObjectData serviceObject) ||
                !m_ServiceUpkeepLookup.HasBuffer(prefab))
                return false;
            bool inactive = EntityManager.TryGetComponent(sign, out Building building) &&
                            BuildingUtils.CheckOption(building, BuildingOption.Inactive);   // :396 (BuildingUtils.cs:351-354)
            m_UpkeepScratchList.Clear();
            CityServiceUpkeepSystem.GetUpkeepWithUsageScale(m_UpkeepScratchList, m_ServiceUpkeepLookup, m_InstalledUpgradeLookup,
                m_PrefabRefLookup, m_ServiceUsageLookup, sign, prefab, inactive);                                       // :397 (CityServiceUpkeepSystem.cs:645)
            int wage = CityServiceUpkeepSystem.GetUpkeepOfEmployeeWage(m_EmployeeLookup, sign, economy, inactive);       // :398 (:628)
            DynamicBuffer<CityModifier> modifiers = default;
            Entity city = m_CitySystem.City;
            bool haveModifiers = city != Entity.Null && EntityManager.HasBuffer<CityModifier>(city);
            if (haveModifiers)
                modifiers = EntityManager.GetBuffer<CityModifier>(city, isReadOnly: true);
            long perDay = 0;
            bool moneySeen = false;
            for (int j = 0; j < m_UpkeepScratchList.Length; j++)
            {
                ServiceUpkeepData d = m_UpkeepScratchList[j];
                int amount = d.m_Upkeep.m_Amount;
                float value = amount * EconomyUtils.GetMarketPrice(d.m_Upkeep.m_Resource, resourcePrefabs, EntityManager);   // :403 (EntityManager overload, EconomyUtils.cs:1245)
                bool money = d.m_Upkeep.m_Resource == Resource.Money;
                if (money)
                {
                    if (haveModifiers)
                        CityUtils.ApplyModifier(ref value, modifiers, CityModifierType.CityServiceBuildingBaseUpkeepCost);   // :407
                    value += wage;                                                                                            // :408
                    if (inactive)
                        value *= 0.1f;                                                                                        // :409-412
                }
                if (amount > 0)
                {
                    perDay += UnityEngine.Mathf.RoundToInt(value);   // m_FullCost (:420)
                    if (money)
                        moneySeen = true;
                }
            }
            int budget = ServiceBudget(serviceObject.m_Service);
            if (budget <= 0)
                return false;
            long wageDay = moneySeen ? wage : 0;
            long buildingsDay = perDay - wageDay;
            float factor = budget / 100f * math.max(1, m_TimeSystem.daysPerYear) / 12f;
            buildingsMonth = buildingsDay * factor;
            wagesMonth = wageDay * factor;
            return true;
        }

        /// <summary>CityServiceBudgetSystem.GetServiceBudget(service) once per service per rebuild (it completes m_TempArrayDeps and
        /// reads the budget singleton, :1060-1066). 0 = no budget entry yet. A throw (no budget singleton yet) reads -1 for this rebuild
        /// with one WARN per load: the hub then shows "n/a" for buildings and wages, never a wrong number.</summary>
        private int ServiceBudget(Entity service)
        {
            if (m_BudgetOf.TryGetValue(service, out int cached))
                return cached;
            int budget;
            try
            {
                budget = m_CityServiceBudget.GetServiceBudget(service);
            }
            catch (Exception e)
            {
                budget = -1;
                if (!m_BudgetWarned)
                {
                    m_BudgetWarned = true;
                    Mod.Log.Warn($"Stats: CityServiceBudgetSystem.GetServiceBudget threw ({e.GetType().Name}: {e.Message}) — " +
                                 "buildings and wages read n/a until it answers (warned once per load)");
                }
            }
            m_BudgetOf[service] = budget;
            return budget;
        }

        /// <summary>CityStats: the settings (one read each), the snow flags and the other-mod labels; the trip planner label and whether
        /// it blocks ski days (SkiDaySystem's accessors, set at each Game load and on a change during play, each followed by its
        /// Revision++).</summary>
        private CityStats ReadCity()
        {
            SkiResortSettings settings = Mod.Settings;
            int k = settings != null ? settings.AttractivenessScaleK : SkiResortSettings.DefaultAttractivenessScaleK;
            int backcountry = settings != null ? settings.BackcountryValuePercent : SkiResortSettings.DefaultBackcountryValuePercent;
            int ticket = settings != null ? settings.LiftTicketPrice : SkiMoneyRules.TicketPriceDefault;
            return new CityStats
            {
                Known = true,
                DaysPerYear = m_TimeSystem.daysPerYear,
                ScaleK = math.clamp(k, SkiResortScore.MinK, SkiResortScore.MaxK),
                BackcountryPct = math.clamp(backcountry, 0, 100),
                TicketPrice = SkiMoneyRules.ClampTicketPrice(ticket),
                SnowGating = m_SnowLine.IsSnowGatingEnabled(),
                SnowmakingMarginC = m_SnowLine.GetSnowmakingMarginC(),
                SnowVisualsOff = m_Injection.GetSnowSummary().SnowVisualsOff,
                TourismOverhaul = Mod.TourismOverhaulDetected,
                RealisticWorkplaces = SkiOtherMods.RealisticWorkplacesDetected,
                TripPlanner = m_SkiDays != null ? m_SkiDays.TripPlannerLabel ?? "" : "",
                TripPlannerBlocksSkiDays = m_SkiDays != null && m_SkiDays.TripPlannerBlocksSkiDays,
                // the settings the ticket system reads
                BookTicketsAsFees = SkiResortSettings.DefaultBookTicketsAsFees, // always on (DECISIONS 601)
                PassShare = SkiMoneyRules.ClampPassShare(settings != null ? settings.SeasonPassShare : SkiMoneyRules.PassShareDefault),
                // the ledger month key's calendar month, 0..11 (MonthOfKey is 1..12)
                Month = SkiMoneyRules.MonthOfKey(SkiMoneyRules.MonthKey(m_TimeSystem.year, m_TimeSystem.normalizedDate)) - 1,
            };
        }

        private void BuildProblems(int resorts, CityStats city)
        {
            long t0 = Stopwatch.GetTimestamp();
            int bandRows = 0, factorRows = 0;
            for (int s = 0; s <= resorts && s < m_ResortsNext.Count; s++)
            {
                bool notLinked = s == resorts;
                ResortStats row = m_ResortsNext[s];
                Entity sign = row.Sign;
                AreaWork work = m_Work[s];
                bool hasResult = !notLinked && work.HasResult;
                SkiResortScore.Result r = work.View.Result;

                m_PFactors.Clear();
                if (hasResult)
                {
                    m_FactorScratch.Clear();
                    m_Attractiveness.CopyEfficiencyFactors(sign, m_FactorScratch);
                    for (int i = 0; i < m_FactorScratch.Count; i++)
                    {
                        Efficiency f = m_FactorScratch[i];
                        m_PFactors.Add(new HubProblemFactor
                        {
                            Name = FactorName(f.m_Factor), EffectPct = HubRules.FactorEffectPct(f.m_Efficiency),
                        });
                    }
                }
                m_PLifts.Clear();
                m_PLiftRows.Clear();
                for (int k = 0; k < m_LiftsNext.Count; k++)
                {
                    LiftStats l = m_LiftsNext[k];
                    if (l.Sign != sign)
                        continue;
                    m_PLifts.Add(new HubProblemLift
                    {
                        State = l.State, Powered = l.Powered, HasBase = l.HasBase, HasTop = l.HasTop,
                        CustomName = l.CustomName, // the station-pair row takes the kept name
                        // The row's type (its build items; NoType reads fixed-grip) and Mixed.
                        Type = l.TypeIndex == LiftCensus.NoType ? (byte)0 : l.TypeIndex,
                        Mixed = l.Mixed,
                        BendTenths = l.BendTenths, BendTower = l.BendTower, // the LiftBend row
                        TrainTower = l.TrainTower,                          // the LiftTrainLimit row
                        LowSpan = l.LowSpan, LowFrom = l.LowFrom, LowTo = l.LowTo, LowTenths = l.LowTenths,   // the LiftLowSpan row
                        Cabins = l.Type == "gondola" || l.Type == "tram",
                    });
                    m_PLiftRows.Add(k);
                }
                SkiResortAggregate.Agg a = m_Agg[s];
                // The ski-day rows only for a sign with a SkiDaySystem view while ski days are on.
                bool skiDayRows = !notLinked && work.HasSkiDays && !m_SkiDaysOff;
                SkiDayAreaView d = work.SkiDays;
                // Summit lodges not lift-served and the IfLodgesOn ladder.
                if (!notLinked)
                    CountSummitNotServed(sign, ref m_Work[s]);
                work = m_Work[s];
                int ifLodgesOn = hasResult && row.Lodges > 0 ? IfLodgesOnApplied(s, sign, in r) : -1;
                // This area's top-level schools (Not linked: those on no sign), the road-less ones and the first of each (lowest entity) for
                // the rows' subjects.
                SchoolFacts(notLinked ? Entity.Null : sign, out int schoolsHere, out Entity firstSchool,
                            out int schoolsNoRoad, out Entity firstSchoolNoRoad);
                var input = new HubProblemInput
                {
                    Unlinked = notLinked,
                    Lodges = row.Lodges, LodgesActive = work.LodgesActive, FreeLodges = row.FreeLodges,
                    EfficiencyPct = hasResult ? work.View.EfficiencyPct : -1,
                    Factors = m_PFactors,
                    Lifts = m_PLifts,
                    LiftRows = row.LiftRows, Runs = row.Runs,
                    RunsUnserved = hasResult ? r.RunsUnserved : 0,
                    RunsUnrateable = a.Unrateable,
                    RunsRating = Math.Max(0, row.Runs - row.Rated - a.Unrateable),
                    RunsFlat = hasResult ? r.RunsFlat : 0,
                    BackcountryPct = hasResult ? r.BackcountryPercent : city.BackcountryPct,
                    AttractionFull = hasResult ? r.AttractionFullEfficiency : -1,
                    IfOneLodge = hasResult && row.Lodges == 0 ? HubRules.AttractionWithLodges(in r, 1) : -1,
                    // The ladder over every lodge of the area switched on (IfLodgesOnApplied).
                    IfLodgesOn = ifLodgesOn,
                    // Road edges, read in the building walk (no result needed: a placement fact).
                    SignNoRoad = !notLinked && work.SignNoRoad,
                    LodgesNoRoad = notLinked ? 0 : work.LodgesNoRoad,
                    // From SkiDaySystem's view (else false / 0 / 0).
                    SkiDaysCannotReach = skiDayRows && d.CannotReach,
                    NoGuestsNearTenthsKm = skiDayRows && hasResult && r.RunsCounted > 0 && m_ReachCensus.TryGetReading(sign, out SkiReachCensusSystem.Reading near) &&
                                           SkiDayRules.NoGuestsNear(near.Households, near.Near) ? SkiDayRules.TenthsKm(near.NearestM) : 0,
                    LodgesNoParking = skiDayRows ? d.LodgesNoParking : 0,
                    LodgesNotSlopeside = skiDayRows ? d.LodgesNotSlopeside : 0,
                    DepotsNoHillLodge = skiDayRows ? d.DepotsNoHillLodge : 0,
                    DepotsNoRoad = skiDayRows ? d.DepotsNoRoad : 0,
                    // Did the band show the Efficiency row on this area's previous build?
                    EfficiencyRowWasOn = !notLinked && m_EffRowOn.Contains(sign),
                    // Rows 24 and 25. School: active, runs count, none of them a counted green.
                    SummitLodgesNotServed = notLinked ? 0 : work.SummitNotServed,
                    SchoolNoGreenRun = hasResult && r.SchoolActive && r.RunsCounted > 0 && !r.GreenCounted,
                    // Road-less top-level schools join LodgeNoRoad; unlinked ones get their Not linked row.
                    SchoolsNoRoad = notLinked ? 0 : schoolsNoRoad,
                    FreeSchools = notLinked ? schoolsHere : 0,
                };
                HubProblems.Build(in input, m_POut);
                for (int i = 0; i < m_POut.Count; i++)
                {
                    HubProblem p = m_POut[i];
                    bool liftRow = p.LiftIndex >= 0 && p.LiftIndex < m_PLiftRows.Count;
                    LiftStats lift = liftRow ? m_LiftsNext[m_PLiftRows[p.LiftIndex]] : default;
                    int code = (int)p.Code;
                    // Subject: the sign for the lodge, efficiency and SignNoRoad rows; LodgeNoRoad: the lodge (lowest entity) with no road; a lift
                    // row: its census id; the lodge with the most car failures (else the sign), the first lodge with no parking, the first lodge far
                    // from the runs (lowest entity, SkiDaySystem's view); else Null.
                    Entity subject = liftRow ? lift.Entity : Entity.Null;
                    if ((code >= 1 && code <= 4) || p.Code == HubProblemCode.SignNoRoad || p.Code == HubProblemCode.SkiDaysNoGuestsNear)
                        subject = sign;
                    else if (p.Code == HubProblemCode.LodgeNoRoad)
                        subject = work.LodgesNoRoad > 0 ? work.LodgeNoRoad : firstSchoolNoRoad; // a school alone
                    else if (p.Code == HubProblemCode.SchoolsNotLinked)
                        subject = firstSchool; // the first school on no sign (the click flies there)
                    else if (p.Code == HubProblemCode.SkiDaysCannotReach)
                        subject = d.ReachLodge != Entity.Null ? d.ReachLodge : sign;
                    else if (p.Code == HubProblemCode.NoParkingNearLodge)
                        subject = d.FirstLodgeNoParking;
                    else if (p.Code == HubProblemCode.LodgeNotSlopeside)
                        subject = d.FirstLodgeNotSlopeside;
                    else if (p.Code == HubProblemCode.DepotNoHillLodge || p.Code == HubProblemCode.DepotNoRoad)
                        subject = d.FirstDepotProblem;
                    else if (p.Code == HubProblemCode.SummitLodgeNotLiftServed)
                        subject = work.SummitNotServedLodge; // the lowest such lodge
                    else if (p.Code == HubProblemCode.SchoolNoGreenRun)
                        subject = firstSchool != Entity.Null ? firstSchool : sign; // the school (else the sign)
                    // LiftBend: the bent tower's node with no top, so focusLift flies to that tower (SkiResortHubUISystem.ApplyFocusLift).
                    Entity subjectTop = liftRow ? lift.Top : Entity.Null;
                    if (p.Code == HubProblemCode.LiftBend && lift.BendNode != Entity.Null)
                    {
                        subject = lift.BendNode;
                        subjectTop = Entity.Null;
                    }
                    // LiftTrainLimit: the same with the tower past its train's limits.
                    if (p.Code == HubProblemCode.LiftTrainLimit && lift.TrainNode != Entity.Null)
                    {
                        subject = lift.TrainNode;
                        subjectTop = Entity.Null;
                    }
                    // LiftLowSpan: the low span's edge with no top, so focusLift flies to its low point.
                    if (p.Code == HubProblemCode.LiftLowSpan && lift.LowEdge != Entity.Null)
                    {
                        subject = lift.LowEdge;
                        subjectTop = Entity.Null;
                    }
                    if (p.Code == HubProblemCode.Efficiency)
                        m_EffRowOnNext.Add(sign); // the band shows it on this build (Not linked never has one)
                    m_ProblemsNext.Add(new ProblemStats
                    {
                        Sign = sign,
                        Code = code,
                        Severity = p.Severity,
                        Subject = subject,
                        SubjectTop = subjectTop,
                        Name = liftRow ? lift.Name ?? "" : "",
                        Count = p.Count,
                        Pct = p.Pct,
                        Factor = p.Factor ?? "",
                        More = p.More,
                        Detail = p.Detail,
                        IfFixed = p.IfFixed,
                        BuildItem = p.BuildItem ?? "",
                    });
                    if (p.Code == HubProblemCode.Factor)
                        factorRows++;
                    else
                        bandRows++;
                }
            }
            // This build's Efficiency rows become the next build's state. COPY, not swap (both sets are readonly, CS0191); a sign that is
            // gone drops out; no allocation once grown. A foreach + Add, not UnionWith: UnionWith(IEnumerable<T>) enumerates through the
            // interface and boxes the enumerator (System.Core HashSet`1, the game's own copy); the struct enumerator does not.
            m_EffRowOn.Clear();
            foreach (Entity e in m_EffRowOnNext)
                m_EffRowOn.Add(e);
            m_EffRowOnNext.Clear();
            m_LastProblemsMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            if (!m_ProblemsLogged)
            {
                m_ProblemsLogged = true;
                Mod.Log.Info($"Stats [problems] first build this load: {resorts + 1} area(s), {bandRows} band row(s), " +
                             $"{factorRows} factor row(s) in {m_LastProblemsMs:F2} ms");
            }
        }

        /// <summary>The area's ACTIVE lodges whose row counts more when summit-served (MuSummit &gt; Mu: the Hut and the Summit lodge)
        /// and that are not (LodgeInfo.SummitServed of the attraction model's last recompute), and the lowest such lodge by (Index,
        /// Version): HubProblemCode 24's count and subject. Allocation-free once m_LodgeInfo has grown.</summary>
        private void CountSummitNotServed(Entity sign, ref AreaWork w)
        {
            w.SummitNotServed = 0;
            w.SummitNotServedLodge = Entity.Null;
            m_Attractiveness.CopyActiveLodgeInfo(sign, m_LodgeInfo);
            IReadOnlyList<LodgeDef> lodges = SkiCatalog.Lodges;
            for (int i = 0; i < m_LodgeInfo.Count; i++)
            {
                SkiResortAttractivenessSystem.LodgeInfo info = m_LodgeInfo[i];
                LodgeDef def = info.LodgeIndex >= 0 && info.LodgeIndex < lodges.Count ? lodges[info.LodgeIndex] : null;
                if (def == null || !(def.MuSummit > def.Mu) || info.SummitServed)
                    continue;
                Entity lodge = info.Lodge;
                Entity first = w.SummitNotServedLodge;
                if (w.SummitNotServed == 0 || lodge.Index < first.Index || (lodge.Index == first.Index && lodge.Version < first.Version))
                    w.SummitNotServedLodge = lodge;
                w.SummitNotServed++;
            }
            m_LodgeInfo.Clear();
        }

        /// <summary>The "if fixed" A of the LodgesAllOff row = the model's A with EVERY lodge the sign lists switched on: the ladder
        /// (SkiResortScore.LodgeBase) over their tiers in the Result's season, each summit-served when a complete, powered lift top of
        /// this ski area is within Ladder.SummitServedRadiusM (LiftServedRule.SummitServed over this area's serve lifts), through
        /// HubRules.AttractionWithLodgeApplied. Computed only for an area whose lodges are all switched off (the only row that shows it);
        /// -1 otherwise. Reads each lodge's Transform (the rebuild's existing sync point; at most a handful per area).</summary>
        private int IfLodgesOnApplied(int s, Entity sign, in SkiResortScore.Result r)
        {
            if (m_Work[s].LodgesActive > 0)
                return -1;   // HubProblems.Build shows IfLodgesOn only on LodgesAllOff (LodgesActive == 0)
            m_AreaServeLifts.Clear();
            for (int k = 0; k < m_ServeLifts.Count && k < m_CensusResort.Count && k < m_CensusRows.Count; k++)
            {
                if (m_CensusResort[k] == s && m_ServeLifts[k].Id == m_CensusRows[k].Id && m_ServeLifts[k].Top == m_CensusRows[k].Top)
                    m_AreaServeLifts.Add(m_ServeLifts[k]);
            }
            IReadOnlyDictionary<Entity, LodgeDef> lodgeDefs = m_PrefabCatalog.LodgeDefs;
            m_LadderLodges.Clear();
            for (int i = 0; i < m_LodgeEntities.Count; i++)
            {
                Entity lodge = m_LodgeEntities[i];
                if (!m_LodgeResort.TryGetValue(lodge, out int lr) || lr != s)
                    continue;
                if (!EntityManager.TryGetComponent(lodge, out PrefabRef pr) || !lodgeDefs.TryGetValue(pr.m_Prefab, out LodgeDef def) ||
                    def == null)
                    continue;
                bool served = false;
                if (def.MuSummit > def.Mu && EntityManager.TryGetComponent(lodge, out Game.Objects.Transform t))
                {
                    served = LiftServedRule.SummitServed(t.m_Position.xz, m_AreaServeLifts, SkiCatalog.Ladder.SummitServedRadiusM);
                    for (int run = 0; !served && run < m_RunOutlines.Count && run < m_RunAreas.Count && run < m_ServedRoot.Count; run++)
                        served = m_RunAreas[run] == s && m_ServedRoot[run] >= 0 &&
                                 LiftServedRule.OutlineWithin(t.m_Position.xz, m_RunOutlines[run], SkiCatalog.Ladder.SummitRunRadiusM);
                }
                m_LadderLodges.Add(new SkiResortScore.LodgeInput { LodgeIndex = def.Index, SummitServed = served });
            }
            bool summer = (r.Flags & SkiResortScore.ScoreFlags.SummerMode) != 0;
            SkiResortScore.LodgeBase(m_LadderLodges, summer, out double applied, out int _, out double _);
            m_LadderLodges.Clear();
            m_AreaServeLifts.Clear();
            return HubRules.AttractionWithLodgeApplied(in r, applied);
        }

        private static string[] BuildFactorNames()
        {
            var names = new string[256];
            for (int b = 0; b < names.Length; b++)
                names[b] = "Factor" + b.ToString(CultureInfo.InvariantCulture);
            foreach (EfficiencyFactor f in Enum.GetValues(typeof(EfficiencyFactor)))
            {
                string name = Enum.GetName(typeof(EfficiencyFactor), f);
                if (!string.IsNullOrEmpty(name))
                    names[(byte)f] = name;
            }
            return names;
        }

        private static string FactorName(EfficiencyFactor f) => s_FactorNames[(byte)f];

        /// <summary>Templates from the ACTIVE dictionary (NameSystem.cs:250 precedent), English if
        /// missing; re-read every rebuild (three dictionary lookups) so a language change applies on
        /// the next rebuild; the formatted-name caches are dropped when a template changes.</summary>
        private void RefreshTemplates()
        {
            var dictionary = GameManager.instance?.localizationManager?.activeDictionary;
            string resort = Lookup(dictionary, ResortDefaultKey, ResortDefaultEnglish);
            string lift = Lookup(dictionary, LiftDefaultKey, LiftDefaultEnglish);
            m_UnlinkedName = Lookup(dictionary, UnlinkedKey, UnlinkedEnglish);
            if (!ReferenceEquals(resort, m_ResortTemplate) && resort != m_ResortTemplate)
                m_ResortNameCache.Clear();
            if (!ReferenceEquals(lift, m_LiftTemplate) && lift != m_LiftTemplate)
                m_LiftNameCache.Clear();
            m_ResortTemplate = resort;
            m_LiftTemplate = lift;
        }

        private static string Lookup(Colossal.Localization.LocalizationDictionary dictionary, string key, string english)
        {
            return dictionary != null && dictionary.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value) ? value : english;
        }

        private string ResortDefault(int n)
        {
            return Format(m_ResortNameCache, m_ResortTemplate ?? ResortDefaultEnglish, n);
        }

        private string LiftDefault(int n)
        {
            return Format(m_LiftNameCache, m_LiftTemplate ?? LiftDefaultEnglish, n);
        }

        private static string Format(Dictionary<int, string> cache, string template, int n)
        {
            if (cache.TryGetValue(n, out string cached))
                return cached;
            string number = n.ToString(CultureInfo.InvariantCulture);
            string name = template.IndexOf(OrdinalPlaceholder, StringComparison.Ordinal) >= 0
                ? template.Replace(OrdinalPlaceholder, number)
                : template + " " + number;
            cache[n] = name;
            return name;
        }

        private static ulong Mix(ulong hash, uint value)
        {
            for (int b = 0; b < 4; b++)
            {
                hash ^= (value >> (8 * b)) & 0xFFu;
                hash *= 1099511628211UL;
            }
            return hash;
        }

        private static ulong MixF(ulong hash, float v)
        {
            return Mix(hash, math.asuint(v));
        }

        private static ulong MixE(ulong hash, Entity e)
        {
            return Mix(Mix(hash, (uint)e.Index), (uint)e.Version);
        }

        private static ulong MixS(ulong hash, string s)
        {
            return Mix(hash, (uint)(s == null ? 0 : s.GetHashCode()));
        }

        private static ulong MixB(ulong hash, bool b)
        {
            return Mix(hash, b ? 1u : 0u);
        }

        private static ulong HashResorts(List<ResortStats> rows)
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < rows.Count; i++)
            {
                ResortStats r = rows[i];
                h = MixE(h, r.Sign);
                h = Mix(h, (uint)r.Kind);
                h = MixS(h, r.Name);
                h = MixB(h, r.CustomName);
                h = Mix(h, (uint)r.Runs);
                h = Mix(h, (uint)r.Rated);
                h = Mix(h, (uint)(r.Flat | r.Green << 8 | r.Blue << 16 | r.Black << 24));
                h = Mix(h, (uint)r.DoubleBlack);
                h = MixF(h, r.AreaM2);
                h = MixF(h, r.VerticalM);
                h = MixB(h, r.VerticalApprox);
                h = Mix(h, (uint)r.Lodges);
                h = Mix(h, (uint)r.FreeLodges);
                h = Mix(h, (uint)r.LiftRows);
                h = MixF(h, r.LiftLengthM);
                h = MixF(h, r.CapacityPerHour);
            }
            return Mix(h, (uint)rows.Count);
        }

        private static ulong HashResortLive(List<ResortLiveStats> rows)
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < rows.Count; i++)
            {
                ResortLiveStats r = rows[i];
                h = MixE(h, r.Sign);
                h = Mix(h, (uint)r.Status);
                h = Mix(h, (uint)r.RunsOpen);
                h = Mix(h, (uint)r.RunsPartly);
                h = MixB(h, r.SnowEstimated);
                h = MixF(h, r.SnowPct);
                h = MixF(h, r.SnowDepthCm);
                h = Mix(h, (uint)r.SeasonHint);
                h = MixB(h, r.TopTempKnown);
                h = MixF(h, r.TopTempC);
                h = Mix(h, (uint)r.Attraction);
                h = Mix(h, (uint)r.AttractionFull);
                h = MixB(h, r.AttractionHeld);
                h = Mix(h, (uint)r.LodgesActive);
                h = MixB(h, r.Summer);
                h = MixF(h, r.LodgePart);
                h = MixF(h, r.Terrain);
                h = MixF(h, r.RunsValue);
                h = MixF(h, r.RunsValueFull);
                h = Mix(h, (uint)r.OpenSharePct);
                h = MixF(h, r.LiftsValue);
                h = Mix(h, (uint)r.LiftsComplete);
                h = Mix(h, (uint)r.LiftsCounted);
                h = MixF(h, r.VarietyMultiplier);
                h = MixF(h, r.VerticalMultiplier);
                h = MixF(h, r.ScoreVerticalM);
                h = Mix(h, (uint)r.EfficiencyPct);
                h = MixF(h, r.SharePct);
                h = MixB(h, r.EstKnown);
                h = Mix(h, (uint)r.EstTourists);
                h = Mix(h, (uint)r.NextStep);
                h = MixB(h, r.ThisMonthSinceLoad);
                h = MixB(h, r.LastMonthFull);
                h = Mix(h, (uint)r.GuestsThisMonth);
                h = Mix(h, (uint)r.TouristsThisMonth);
                h = Mix(h, (uint)r.TouristGroupsThisMonth);
                h = Mix(h, (uint)r.ResidentsThisMonth);
                h = Mix(h, (uint)r.PaidThisMonth);
                h = Mix(h, (uint)r.UnpaidThisMonth);
                h = Mix(h, (uint)r.FreeThisMonth);
                h = Mix(h, (uint)r.TicketsThisMonth);
                h = Mix(h, (uint)r.GuestsLastMonth);
                h = Mix(h, (uint)r.TouristsLastMonth);
                h = Mix(h, (uint)r.TouristGroupsLastMonth);
                h = Mix(h, (uint)r.ResidentsLastMonth);
                h = Mix(h, (uint)r.PaidLastMonth);
                h = Mix(h, (uint)r.TicketsLastMonth);
                h = Mix(h, (uint)r.TicketGate);
                h = Mix(h, (uint)r.TicketIncomeMonth);
                h = Mix(h, (uint)r.TicketPaidMonth);
                h = MixB(h, r.TicketIncomeEstimated);
                h = Mix(h, (uint)r.CostGroomingMonth);
                h = Mix(h, (uint)r.CostSnowmakingMonth);
                h = Mix(h, (uint)r.CostLightsMonth);
                h = Mix(h, (uint)r.CostCatTracksMonth);
                h = MixF(h, r.HaNatural);
                h = MixF(h, r.HaGroomed);
                h = MixF(h, r.HaSnowmaking);
                h = MixF(h, r.HaLit);
                h = MixF(h, r.HaCatTracks);
                h = Mix(h, (uint)r.LiftCostMonth);
                h = Mix(h, (uint)r.LiftsBilled);
                h = Mix(h, (uint)r.LiftsBilledReduced);
                h = Mix(h, (uint)r.BuildingsMonth);
                h = Mix(h, (uint)r.WagesMonth);
                h = Mix(h, (uint)r.Jobs);
                h = Mix(h, (uint)r.JobsFilled);
                h = Mix(h, (uint)r.NetMonth);
                h = MixB(h, r.NetEstimated);
                // Every appended field must be hashed: a field not hashed would never re-send.
                h = Mix(h, (uint)r.SkiDaysState);
                h = Mix(h, (uint)r.SkiDaysThisMonth);
                h = Mix(h, (uint)r.SkiDaysLastMonth);
                h = Mix(h, (uint)r.SkiDaysSent);
                h = Mix(h, (uint)r.SkiDaysSentCar);
                h = Mix(h, (uint)r.SkiDaysSentNoCar);
                h = Mix(h, (uint)r.SkiDaysNow);
                h = Mix(h, (uint)r.SkiDaysCap);
                h = Mix(h, (uint)r.SkiDaysLimit);
                h = Mix(h, (uint)r.SkiDaysPowderPct);
                h = MixS(h, r.LodgeBest);
                h = MixF(h, r.LodgeExtra);
                h = Mix(h, (uint)r.LuxuryIgnored);
                h = MixF(h, r.SchoolValue);
                h = Mix(h, (uint)r.PatrolledRuns);
                h = Mix(h, (uint)r.PriceTolerance);
                h = Mix(h, (uint)r.LodgeSpendMonth);
                h = Mix(h, (uint)r.PassesSoldSeason);
                h = Mix(h, (uint)r.PassIncomeSeason);
                h = Mix(h, (uint)r.PassHoldersThisMonth);
                h = Mix(h, (uint)r.PassHoldersLastMonth);
                h = Mix(h, (uint)r.LessonsThisMonth);
                h = Mix(h, (uint)r.LessonsLastMonth);
                h = Mix(h, (uint)r.LessonIncomeMonth);
                h = Mix(h, (uint)r.PassesPreSeason);
                h = Mix(h, (uint)r.PassIncomePreSeason);
                h = Mix(h, (uint)r.CityIncomeEst);
                h = Mix(h, (uint)r.PassIncomeMonth);
                h = Mix(h, (uint)r.PassIncomePreMonth);
                h = Mix(h, (uint)r.LodgeIncomeMonth);
                h = Mix(h, (uint)r.LiftChargedMonth);
                h = Mix(h, (uint)r.RunCostMonth);
                h = Mix(h, (uint)r.BuildingsSoFar);
                h = Mix(h, (uint)r.WagesSoFar);
                h = MixF(h, r.MonthShare);
                h = Mix(h, (uint)r.BusChargedMonth);
                h = Mix(h, (uint)r.Depots);
                h = Mix(h, (uint)r.BusGuestsMonth);
                h = Mix(h, (uint)r.BusBillMonth);
            }
            return Mix(h, (uint)rows.Count);
        }

        private static ulong HashRuns(List<RunStats> rows)
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < rows.Count; i++)
            {
                RunStats r = rows[i];
                h = MixE(h, r.Entity);
                h = MixS(h, r.Name);
                h = Mix(h, (uint)r.Kind | (r.Bound ? 1u << 8 : 0u) | (r.CustomName ? 1u << 9 : 0u) | (r.Approx ? 1u << 10 : 0u) |
                           (r.Member ? 1u << 11 : 0u) | (r.Served ? 1u << 12 : 0u));
                h = MixF(h, r.LengthM);
                h = MixF(h, r.DropM);
                h = MixF(h, r.AreaM2);
                h = MixE(h, r.Sign);
                h = Mix(h, (uint)r.Ordinal);
                h = Mix(h, (uint)r.Band);
                h = MixF(h, r.GradePct);
                h = MixF(h, r.SteepestPct);
                h = MixE(h, r.ServedBy);
                h = MixF(h, r.ValueFull);
                h = MixF(h, r.BandValue);
                h = MixF(h, r.SizeFactor);
                h = MixF(h, r.TypeFactor);
                h = MixF(h, r.ServedFactor);
                h = Mix(h, (uint)r.GunSpacingM);
                h = Mix(h, (uint)r.LightSpacingM);
                h = Mix(h, (uint)r.PaidSteps);
            }
            return Mix(h, (uint)rows.Count);
        }

        private static ulong HashRunLive(List<RunLiveStats> rows)
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < rows.Count; i++)
            {
                RunLiveStats r = rows[i];
                h = MixE(h, r.Entity);
                h = Mix(h, (uint)r.OpenState);
                h = MixF(h, r.OpenFactor);
                h = Mix(h, (uint)r.SnowPct);
                h = MixB(h, r.SnowEstimated);
                h = MixF(h, r.ValueNow);
                h = Mix(h, (uint)r.CostMonth);
                h = Mix(h, (uint)r.SnowDepthCm);
                h = Mix(h, (uint)r.GunWhy);
            }
            return Mix(h, (uint)rows.Count);
        }

        private static ulong HashLifts(List<LiftStats> rows)
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < rows.Count; i++)
            {
                LiftStats r = rows[i];
                h = MixE(h, r.Entity);
                h = MixE(h, r.Top);
                h = MixE(h, r.Sign);
                h = MixS(h, r.Name);
                h = Mix(h, (uint)r.State | (r.CustomName ? 1u << 8 : 0u) | (r.AlignKnown ? 1u << 9 : 0u) | (r.HasBase ? 1u << 10 : 0u) |
                           (r.HasTop ? 1u << 11 : 0u) | (r.Powered ? 1u << 12 : 0u) | (r.Counted ? 1u << 13 : 0u) |
                           (r.BilledReduced ? 1u << 14 : 0u));
                h = Mix(h, (uint)r.Sections);
                h = Mix(h, (uint)r.Towers);
                h = Mix(h, (uint)r.Chairs);
                h = Mix(h, (uint)r.ChairsRiding);
                h = Mix(h, (uint)r.Gaps);
                h = Mix(h, (uint)r.TowersRemoved);
                h = MixF(h, r.LengthM);
                h = MixF(h, r.RiseM);
                h = MixF(h, r.HorizontalM);
                h = MixF(h, r.AlignOffsetM);
                h = MixF(h, r.AlignYawDeg);
                h = MixF(h, r.CapacityPerHour);
                h = Mix(h, (uint)r.Status);
                h = MixF(h, r.RideTimeS);
                h = Mix(h, (uint)r.RunsServed);
                h = Mix(h, (uint)r.CostMonth);
                h = MixS(h, r.Type);
                h = Mix(h, (uint)r.TypeIndex | (r.Mixed ? 1u << 8 : 0u));
                h = Mix(h, (uint)r.Seats);
                h = MixF(h, r.SpeedMps);
                h = Mix(h, (uint)r.BendTenths);
                h = Mix(h, (uint)r.BendTower);
                h = MixE(h, r.BendNode);
                h = Mix(h, (uint)r.TrainTower);
                h = MixE(h, r.TrainNode);
                h = Mix(h, r.LowSpan ? 1u : 0u);
                h = Mix(h, (uint)r.LowFrom);
                h = Mix(h, (uint)r.LowTo);
                h = Mix(h, (uint)r.LowTenths);
                h = MixE(h, r.LowEdge);
            }
            return Mix(h, (uint)rows.Count);
        }

        private static ulong HashProblems(List<ProblemStats> rows)
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < rows.Count; i++)
            {
                ProblemStats r = rows[i];
                h = MixE(h, r.Sign);
                h = Mix(h, (uint)r.Code | (uint)r.Severity << 8);
                h = MixE(h, r.Subject);
                h = MixE(h, r.SubjectTop);
                h = MixS(h, r.Name);
                h = Mix(h, (uint)r.Count);
                h = Mix(h, (uint)r.Pct);
                h = MixS(h, r.Factor);
                h = Mix(h, (uint)r.More);
                h = Mix(h, (uint)r.Detail);
                h = Mix(h, (uint)r.IfFixed);
                h = MixS(h, r.BuildItem);
            }
            return Mix(h, (uint)rows.Count);
        }

        internal static EntityRef ToRef(Entity e)
        {
            return new EntityRef(e.Index, e.Version);
        }

        internal static Entity ToEntity(EntityRef r)
        {
            return new Entity { Index = r.Index, Version = r.Version };
        }

        private static bool IsFinite(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }

        private static float Finite0(float v)
        {
            return IsFinite(v) ? v : 0f;
        }

        /// <summary>Round half away from zero, clamped to int; non-finite gives 0.</summary>
        private static int RoundToInt(float v)
        {
            if (!IsFinite(v))
                return 0;
            double r = Math.Round((double)v, MidpointRounding.AwayFromZero);
            return r >= int.MaxValue ? int.MaxValue : r <= int.MinValue ? int.MinValue : (int)r;
        }

        private static int ClampToInt(long v)
        {
            return v > int.MaxValue ? int.MaxValue : v < int.MinValue ? int.MinValue : (int)v;
        }

        private static string Ent(Entity e)
        {
            return e == Entity.Null ? "null" : e.Index + ":" + e.Version;
        }

        private static string F(float v, string format)
        {
            return v.ToString(format, CultureInfo.InvariantCulture);
        }

        /// <summary>Multi-line description for the hub's DUMP (event time only). Appends only.</summary>
        public void AppendDump(StringBuilder sb)
        {
            try
            {
                CityStats c = City;
                sb.Append("  stats: armed ").Append(m_Armed).Append(", disabled ").Append(m_Disabled).Append(", demand ").Append(Demand)
                  .Append(", rebuilds ").Append(Rebuilds).Append(", last '").Append(LastRebuildWhy).Append("' ")
                  .Append(F(LastRebuildMs, "F2")).Append(" ms (max ").Append(F(MaxRebuildMs, "F2")).Append("), revisions ")
                  .Append(ResortsRevision).Append('/').Append(ResortLiveRevision).Append('/').Append(RunsRevision).Append('/')
                  .Append(RunLiveRevision).Append('/').Append(LiftsRevision).Append('/').Append(ProblemsRevision)
                  .Append("; city known ").Append(c.Known).Append(", days/year ").Append(c.DaysPerYear).Append(", K ").Append(c.ScaleK)
                  .Append(", backcountry ").Append(c.BackcountryPct).Append(" %, ticket ").Append(c.TicketPrice)
                  .Append(", gating ").Append(c.SnowGating ? "on" : "off").Append(", margin ").Append(F(c.SnowmakingMarginC, "F0"))
                  .Append(" C, snow visuals ").Append(c.SnowVisualsOff ? "off" : "on").Append(", Tourism Overhaul ").Append(c.TourismOverhaul)
                  .Append(", Realistic Workplaces ").Append(c.RealisticWorkplaces)
                  .Append(", trip planner ").Append(string.IsNullOrEmpty(c.TripPlanner) ? "game's own" : c.TripPlanner)
                  .Append(c.TripPlannerBlocksSkiDays ? " (ski days off)" : "")
                  .Append(", resort income booked as ").Append(c.BookTicketsAsFees ? "public transportation fees" : "a direct add")
                  .Append(", season pass share ").Append(c.PassShare).Append(" %").Append('\n');
                for (int i = 0; i < m_Resorts.Count; i++)
                {
                    ResortStats r = m_Resorts[i];
                    bool hasLive = i < m_ResortLive.Count;
                    ResortLiveStats l = hasLive ? m_ResortLive[i] : default;
                    AppendResort(sb, i, r, l, hasLive);
                }
                for (int i = 0; i < m_Lifts.Count; i++)
                {
                    LiftStats l = m_Lifts[i];
                    sb.Append("    lift '").Append(l.Name).Append("' ").Append(Ent(l.Entity))
                      .Append(" [").Append(string.IsNullOrEmpty(l.Type) ? "untyped" : l.Type).Append(", ").Append(l.Seats)
                      .Append(" seats, ").Append(F(l.SpeedMps, "0.#")).Append(" m/s").Append(l.Mixed ? ", MIXES lift types" : "");
                    // A Shuttle row names the mover's view of it (DUMP time only). l.Entity is the census row id = the base station when the row has
                    // one (LiftCensus.Row.Id).
                    LiftTypeDef dumpType = l.Mixed ? null : SkiCatalog.LiftByIndex(l.TypeIndex);
                    if (dumpType != null && dumpType.Mover == MoverKind.Shuttle)
                    {
                        string shuttleText = m_Tram != null && l.HasBase ? m_Tram.DescribeShuttle(l.Entity, l.Top) : "";
                        sb.Append("; shuttle ").Append(m_Tram == null ? "mover NOT registered"
                                                       : string.IsNullOrEmpty(shuttleText) ? "none (no path)" : shuttleText);
                    }
                    sb.Append(']')
                      .Append(" state ").Append(l.State).Append(' ').Append(((HubLiftStatus)l.Status).ToString())
                      .Append(", sections ").Append(l.Sections).Append(", chairs ").Append(l.ChairsRiding).Append('/').Append(l.Chairs)
                      .Append(", length ").Append(F(l.LengthM, "F0")).Append(" m, rise ").Append(F(l.RiseM, "F0"))
                      .Append(" m, powered ").Append(l.Powered).Append(", counted ").Append(l.Counted)
                      // The run predicate as the hub applied it (state 0 / 1 = census Complete) and the capacity it left ("–" = -1, not in the area
                      // total). The row carries the closed gate through its capacity (-1 when complete + powered but closed), so read that.
                      .Append(", carries ").Append(l.CapacityPerHour >= 0f ? "Y" : "N")
                      .Append(" (capacity ").Append(l.CapacityPerHour >= 0f ? F(l.CapacityPerHour, "F0") + "/h" : "–").Append(')')
                      .Append(", ride ").Append(F(l.RideTimeS, "F0")).Append(" s, serves ").Append(l.RunsServed)
                      .Append(" run(s), cost ").Append(l.CostMonth).Append("/month").Append(l.BilledReduced ? " (at 25 %)" : "")
                      .Append(", sign ").Append(Ent(l.Sign)).Append('\n');
                }
            }
            catch (Exception e)
            {
                sb.Append("  stats: DUMP failed (").Append(e.GetType().Name).Append(": ").Append(e.Message).Append(")\n");
            }
            // The score, guest, ticket and lift-charge systems' own lines.
            m_Attractiveness.AppendDump(sb);
            m_GuestCounter.AppendDump(sb);
            m_LiftTicket.AppendDump(sb);
            m_LiftCharge.AppendDump(sb);
            // The ski-day system's lines, then the lodge parking census right after. This DUMP is the one caller of both; each in its own
            // try, so one never hides the other.
            try
            {
                m_SkiDays?.AppendDump(sb);
            }
            catch (Exception e)
            {
                sb.Append("  ski days: DUMP failed (").Append(e.GetType().Name).Append(": ").Append(e.Message).Append(")\n");
            }
            try
            {
                m_LodgeParking?.AppendDump(sb);
            }
            catch (Exception e)
            {
                sb.Append("  parking: DUMP failed (").Append(e.GetType().Name).Append(": ").Append(e.Message).Append(")\n");
            }
        }

        private void AppendResort(StringBuilder sb, int i, ResortStats r, ResortLiveStats l, bool hasLive)
        {
            bool notLinked = r.Kind == 1;
            int unrateable = 0, completeLifts = 0;
            for (int k = 0; k < m_Runs.Count; k++)
            {
                RunStats run = m_Runs[k];
                if (run.Sign == r.Sign && run.Band == -2)
                    unrateable++;
            }
            for (int k = 0; k < m_Lifts.Count; k++)
            {
                LiftStats lift = m_Lifts[k];
                if (lift.Sign == r.Sign && (lift.State == 0 || lift.State == 1))
                    completeLifts++;
            }
            sb.Append("    resort ").Append(i + 1).Append(" '").Append(r.Name).Append("' ").Append(notLinked ? "(not linked)" : Ent(r.Sign))
              .Append(": runs ").Append(r.Runs).Append(" (rated ").Append(r.Rated).Append(": ")
              .Append(r.Flat).Append('/').Append(r.Green).Append('/').Append(r.Blue).Append('/').Append(r.Black).Append('/').Append(r.DoubleBlack)
              .Append(", unrateable ").Append(unrateable).Append(", being rated ").Append(Math.Max(0, r.Runs - r.Rated - unrateable))
              .Append("), area ").Append(F(r.AreaM2 / 10000f, "F1")).Append(" ha, vertical ").Append(F(r.VerticalM, "F0")).Append(" m")
              .Append(r.VerticalApprox ? " (approx)" : "")
              .Append(", lodges ").Append(r.Lodges).Append(" (").Append(hasLive ? l.LodgesActive : -1).Append(" on)")
              .Append(", lift rows ").Append(r.LiftRows).Append(" (").Append(completeLifts).Append(" complete), line ")
              .Append(F(r.LiftLengthM / 1000f, "F2")).Append(" km, capacity ").Append(F(r.CapacityPerHour, "F0")).Append("/h\n");
            if (!hasLive)
                return;
            sb.Append("      live: ").Append(((ResortStatus)l.Status).ToString()).Append(" (").Append(l.RunsOpen).Append(" open, ")
              .Append(l.RunsPartly).Append(" partly), snow ").Append(F(l.SnowPct, "F0")).Append(" %").Append(l.SnowEstimated ? " (estimated)" : "")
              .Append(", depth ").Append(F(l.SnowDepthCm, "F1")).Append(" cm, season ").Append(((SeasonHint)l.SeasonHint).ToString())
              .Append(", top ").Append(l.TopTempKnown ? F(l.TopTempC, "F1") : "?").Append(" C, A ").Append(l.Attraction)
              .Append(" (full ").Append(l.AttractionFull).Append(')').Append(l.AttractionHeld ? ", HELD" : "")
              .Append(", efficiency ").Append(l.EfficiencyPct).Append(" %, lodge part ").Append(F(l.LodgePart, "F1"))
              .Append(" + terrain ").Append(F(l.Terrain, "F2")).Append(", runs ").Append(F(l.RunsValue, "F2")).Append(" of ")
              .Append(F(l.RunsValueFull, "F2")).Append(" (").Append(l.OpenSharePct).Append(" %), lifts ").Append(l.LiftsCounted).Append('/')
              .Append(l.LiftsComplete).Append(" worth ").Append(F(l.LiftsValue, "F1")).Append(", share ").Append(F(l.SharePct, "F1"))
              .Append(" %, est +").Append(l.EstTourists).Append(" tourists, next ").Append(((NextStepKind)l.NextStep).ToString()).Append('\n');
            sb.Append("      money: guests ").Append(l.GuestsThisMonth).Append(" this month").Append(l.ThisMonthSinceLoad ? " (since load)" : "")
              .Append(" (").Append(l.TouristsThisMonth).Append(" tourists in ").Append(l.TouristGroupsThisMonth).Append(" groups, ")
              .Append(l.ResidentsThisMonth).Append(" residents; paid ").Append(l.PaidThisMonth).Append(", unpaid ").Append(l.UnpaidThisMonth)
              .Append(", free ").Append(l.FreeThisMonth).Append("; tickets ").Append(l.TicketsThisMonth).Append("), last month ")
              // a partial month prints with " (partial)", as the hub shows it
              .Append(l.GuestsLastMonth >= 0
                  ? l.GuestsLastMonth.ToString(CultureInfo.InvariantCulture) + (l.LastMonthFull ? "" : " (partial)")
                  : "not counted")
              .Append(", tickets/month ").Append(l.TicketIncomeMonth).Append(l.TicketIncomeEstimated ? " ≈" : "")
              .Append(" (paid ").Append(l.TicketPaidMonth).Append("), gate ").Append(GateName(l.TicketGate))
              .Append("; runs grooming/snowmaking/lights/cat tracks ").Append(l.CostGroomingMonth).Append('/').Append(l.CostSnowmakingMonth)
              .Append('/').Append(l.CostLightsMonth).Append('/').Append(l.CostCatTracksMonth)
              .Append(" (").Append(F(l.HaGroomed, "F1")).Append('/').Append(F(l.HaSnowmaking, "F1")).Append('/').Append(F(l.HaLit, "F1"))
              .Append('/').Append(F(l.HaCatTracks, "F1")).Append(" ha; natural ").Append(F(l.HaNatural, "F1")).Append(" ha), lifts ").Append(l.LiftCostMonth)
              .Append(" (").Append(l.LiftsBilled).Append(" billed, ").Append(l.LiftsBilledReduced).Append(" at 25 %), buildings ")
              .Append(l.BuildingsMonth).Append(", wages ").Append(l.WagesMonth).Append(", jobs ").Append(l.JobsFilled).Append('/')
              .Append(l.Jobs).Append(", net ").Append(l.NetMonth).Append(l.NetEstimated ? " ≈" : "").Append('\n');
            if (!notLinked)
            {
                // the ski-day fields the hub sent (signs only)
                sb.Append("      ski days: ").Append(((HubSkiDayState)l.SkiDaysState).ToString()).Append(" (")
                  .Append(((SkiDayLimit)l.SkiDaysLimit).ToString()).Append(" limit, now ").Append(l.SkiDaysNow).Append(" of cap ")
                  .Append(l.SkiDaysCap).Append("), arrived ").Append(l.SkiDaysThisMonth).Append(" this month / ")
                  .Append(l.SkiDaysLastMonth).Append(" last").Append(l.SkiDaysLastMonth >= 0 && !l.LastMonthFull ? " (partial)" : "")
                  .Append(", sent ").Append(l.SkiDaysSent).Append(" (car ").Append(l.SkiDaysSentCar).Append(", no car ")
                  .Append(l.SkiDaysSentNoCar).Append("), powder ").Append(l.SkiDaysPowderPct).Append(" %\n");
                AppendLadder(sb, r.Sign, l); // the lodge ladder, school, patrol
                AppendSignDetail(sb, r.Sign);
                AppendSignNotifications(sb, r.Sign);
            }
            AppendProblems(sb, r.Sign);
        }

        private static string GateName(int gate)
        {
            return gate < 0 ? "-" : ((SkiMoneyRules.TicketGate)gate).ToString();
        }

        /// <summary>The DUMP's ladder line of a sign: "lodges: best SkiLodgeGrand (40), extras 4, capacity 1440, tolerance 40; school on
        /// (green counted); patrols 2, runs patrolled 3; lodge spend/month 0; summit lodges not lift-served 0". Best / extras / tolerance
        /// / patrolled from the Result the hub sent; capacity = sum of Capacity of the active lodges; patrols = the sign's patrol
        /// upgrades; school = the Result's SchoolActive / GreenCounted. Allocates (DUMP only).</summary>
        private void AppendLadder(StringBuilder sb, Entity sign, ResortLiveStats l)
        {
            int capacity = 0;
            m_Attractiveness.CopyActiveLodgeInfo(sign, m_LodgeInfo);
            for (int i = 0; i < m_LodgeInfo.Count; i++)
            {
                SkiResortAttractivenessSystem.LodgeInfo info = m_LodgeInfo[i];
                if (info.LodgeIndex >= 0 && info.LodgeIndex < SkiCatalog.Lodges.Count)
                    capacity += SkiCatalog.Lodges[info.LodgeIndex].Capacity;
            }
            m_LodgeInfo.Clear();
            int patrols = 0;
            // patrol huts linked to this sign (the rebuild's CopyTopLevelLinks)
            HashSet<Entity> topPatrolPrefabs = m_PrefabCatalog.PatrolPrefabSet;
            for (int t = 0; t < m_TopLevel.Count; t++)
            {
                if (m_TopLevelSigns[t] == sign && EntityManager.TryGetComponent(m_TopLevel[t], out PrefabRef tpr) &&
                    topPatrolPrefabs.Contains(tpr.m_Prefab))
                    patrols++;
            }
            bool hasResult = m_Attractiveness.TryGetAreaScore(sign, out SkiResortAttractivenessSystem.AreaScoreView v);
            string bestName = "none";
            if (hasResult && v.Result.LodgeBestIndex >= 0 && v.Result.LodgeBestIndex < SkiCatalog.Lodges.Count)
            {
                LodgeDef best = SkiCatalog.Lodges[v.Result.LodgeBestIndex];
                bestName = best.Prefab + " (" + best.B.ToString("0.##", CultureInfo.InvariantCulture) + ")";
            }
            int summitNotServed = 0;
            for (int s = 0; s < m_SignEntities.Count && s < m_Work.Length; s++)
            {
                if (m_SignEntities[s] == sign)
                    summitNotServed = m_Work[s].SummitNotServed;
            }
            sb.Append("      lodges: best ").Append(bestName).Append(", extras ").Append(F(l.LodgeExtra, "0.##"))
              .Append(", capacity ").Append(capacity)
              .Append(", tolerance ").Append(l.PriceTolerance).Append("; school ")
              .Append(hasResult && v.Result.SchoolActive ? (v.Result.GreenCounted ? "on (green counted)" : "on (no counted green run)") : "off")
              .Append(" worth ").Append(F(l.SchoolValue, "0.##")).Append("; patrols ").Append(patrols).Append(", runs patrolled ")
              .Append(l.PatrolledRuns).Append("; lodge spend/month ").Append(l.LodgeSpendMonth)
              .Append("; passes this season ").Append(l.PassesSoldSeason).Append(" +").Append(l.PassIncomeSeason)
              .Append(", pass-holder guests this month ").Append(l.PassHoldersThisMonth).Append(" / last ").Append(l.PassHoldersLastMonth)
              .Append("; pre-season passes ").Append(l.PassesPreSeason).Append(" +").Append(l.PassIncomePreSeason)
              .Append("; lessons this month ").Append(l.LessonsThisMonth).Append(" +").Append(l.LessonIncomeMonth).Append(" / last ").Append(l.LessonsLastMonth)
              .Append("; city income estimate ").Append(l.CityIncomeEst)
              // The Net's rows of the month so far, in NetMonth's order
              .Append("; net rows: tickets ").Append(l.TicketsThisMonth).Append(" + lodge ").Append(l.LodgeIncomeMonth)
              .Append(" + lessons ").Append(l.LessonIncomeMonth).Append(" + passes ").Append(l.PassIncomeMonth)
              .Append(" (pre-season ").Append(l.PassIncomePreMonth).Append(") − lifts ").Append(l.LiftChargedMonth)
              .Append(" − runs ").Append(l.RunCostMonth).Append(" − ski bus ").Append(l.BusChargedMonth).Append(" − buildings ").Append(l.BuildingsSoFar).Append(" − wages ")
              .Append(l.WagesSoFar).Append(" (month share ").Append(F(l.MonthShare, "0.###")).Append(") = ").Append(l.NetMonth)
              .Append(l.NetEstimated ? " ≈" : "")
              .Append("; bus depots ").Append(l.Depots).Append(", guests by bus this month ").Append(l.BusGuestsMonth).Append(", bill/month ").Append(l.BusBillMonth)
              .Append("; summit lodges not lift-served ").Append(summitNotServed).Append('\n');
        }

        /// <summary>One dump line per sign: its own AttractivenessProvider now (normally absent) and its efficiency with every factor
        /// below 100 %, worst first. Efficiency = round(100 x SkiResortScore.SignEfficiency(factors)), the one re-implementation of
        /// BuildingUtils.cs:121-133 (the Span overload of BuildingUtils.GetEfficiency does not compile on net48, CS7069). Event time only;
        /// log only.</summary>
        private void AppendSignDetail(StringBuilder sb, Entity sign)
        {
            sb.Append("      sign ").Append(Ent(sign)).Append(": own provider ");
            bool exists = EntityManager.Exists(sign);
            if (exists && EntityManager.TryGetComponent(sign, out AttractivenessProvider provider))
                sb.Append(provider.m_Attractiveness).Append(" now");
            else
                sb.Append("none");
            // the road edge the SignNoRoad row reads (Building.m_RoadEdge), with its prefab name
            Entity road = exists && EntityManager.TryGetComponent(sign, out Building building) ? building.m_RoadEdge : Entity.Null;
            sb.Append(", road edge ");
            if (road == Entity.Null)
                sb.Append("none");
            else
                sb.Append(Ent(road)).Append(" '").Append(PrefabNameOf(road)).Append('\'');
            if (!exists || !EntityManager.HasBuffer<Efficiency>(sign))
            {
                sb.Append(", efficiency n/a (no Efficiency buffer)\n");
                return;
            }
            DynamicBuffer<Efficiency> buffer = EntityManager.GetBuffer<Efficiency>(sign, isReadOnly: true);
            if (m_EfficiencyValues.Length < buffer.Length)
                m_EfficiencyValues = new float[Math.Max(buffer.Length, m_EfficiencyValues.Length * 2)];
            m_EfficiencyScratch.Clear();
            for (int i = 0; i < buffer.Length; i++)
            {
                m_EfficiencyValues[i] = buffer[i].m_Efficiency;
                m_EfficiencyScratch.Add(buffer[i]);
            }
            float efficiency = SkiResortScore.SignEfficiency(m_EfficiencyValues, buffer.Length);
            // The attractiveness system's percent expression (ReadSign), so "now" and the area line compare.
            sb.Append(", efficiency now ").Append((int)Math.Round(100.0 * efficiency)).Append(" %");
            m_EfficiencyScratch.Sort();   // Efficiency.CompareTo: highest value first (Efficiency.cs:36-43)
            bool any = false;
            for (int i = m_EfficiencyScratch.Count - 1; i >= 0; i--)   // so backwards = worst first
            {
                Efficiency e = m_EfficiencyScratch[i];
                if (!(e.m_Efficiency < 1f))
                    continue;   // shortfalls only, the hub's rule (CopyEfficiencyFactors keeps m_Efficiency < 1)
                // ONE rounding for every factor of the DUMP: HubRules.FactorEffectPct, the game's panel number (EfficiencySection.cs:99-127), as
                // the problems line's Factor rows carry it.
                sb.Append(any ? ", " : " — factors now (game panel effect), worst first: ").Append(FactorName(e.m_Factor))
                  .Append(' ').Append(HubRules.FactorEffectPct(e.m_Efficiency)).Append(" %");
                any = true;
            }
            if (!any)
                sb.Append(" — no factor below 100 %");
            sb.Append('\n');
        }

        /// <summary>DUMP only: the prefab name of <paramref name="e"/> (PrefabSystem.GetPrefabName), "?" when unknown.</summary>
        private string PrefabNameOf(Entity e)
        {
            if (!EntityManager.Exists(e) || !EntityManager.TryGetComponent(e, out PrefabRef prefabRef))
                return "?";
            return m_PrefabSystem.GetPrefabName(prefabRef.m_Prefab);
        }

        /// <summary>(DUMP only) the sign's live notifications by prefab name (NotificationsSection.GetNotifications,
        /// Game.UI.InGame/NotificationsSection.cs:352-365; names by PrefabSystem.GetPrefabName). The hub sends no notification key; this
        /// line settles which names the game raises on our sign.</summary>
        private void AppendSignNotifications(StringBuilder sb, Entity sign)
        {
            sb.Append("      sign ").Append(Ent(sign)).Append(" notifications: ");
            if (!EntityManager.Exists(sign))
            {
                sb.Append("none (sign gone)\n");
                return;
            }
            var list = new NativeList<Game.UI.InGame.Notification>(8, Allocator.Temp);
            try
            {
                Game.UI.InGame.NotificationsSection.GetNotifications(EntityManager, sign, list);
                if (list.Length == 0)
                    sb.Append("none");
                for (int i = 0; i < list.Length; i++)
                    sb.Append(i > 0 ? ", " : "").Append(m_PrefabSystem.GetPrefabName(list[i].entity));
            }
            finally
            {
                list.Dispose();
            }
            sb.Append('\n');
        }

        /// <summary>`problems: Code(k=v, ...), ... | none; factors Name pct %, ... | none` from the published rows of the area (detail keys
        /// only when not default).</summary>
        private void AppendProblems(StringBuilder sb, Entity sign)
        {
            sb.Append("      problems: ");
            bool any = false;
            for (int i = 0; i < m_Problems.Count; i++)
            {
                ProblemStats p = m_Problems[i];
                if (p.Sign != sign || p.Code == (int)HubProblemCode.Factor)
                    continue;
                sb.Append(any ? ", " : "").Append(((HubProblemCode)p.Code).ToString()).Append('(');
                bool first = true;
                if (!string.IsNullOrEmpty(p.Name))
                    Key(sb, ref first, "lift='" + p.Name + "'");
                if (p.Code == (int)HubProblemCode.LodgeNoRoad)
                    Key(sb, ref first, "lodge=" + Ent(p.Subject)); // the row's subject
                if (p.Count != -1)
                    Key(sb, ref first, "count=" + p.Count.ToString(CultureInfo.InvariantCulture));
                if (p.Pct != -1)
                    Key(sb, ref first, "pct=" + p.Pct.ToString(CultureInfo.InvariantCulture));
                if (!string.IsNullOrEmpty(p.Factor))
                    Key(sb, ref first, "factor=" + p.Factor);
                if (p.More != 0)
                    Key(sb, ref first, "more=" + p.More.ToString(CultureInfo.InvariantCulture));
                if (p.Detail != 0)
                    Key(sb, ref first, "detail=" + p.Detail.ToString(CultureInfo.InvariantCulture));
                if (p.IfFixed != -1)
                    Key(sb, ref first, "ifFixed=" + p.IfFixed.ToString(CultureInfo.InvariantCulture));
                if (!string.IsNullOrEmpty(p.BuildItem))
                    Key(sb, ref first, "build=" + p.BuildItem);
                sb.Append(')');
                any = true;
            }
            if (!any)
                sb.Append("none");
            sb.Append("; factors at the last score recompute (game panel effect) ");
            any = false;
            for (int i = 0; i < m_Problems.Count; i++)
            {
                ProblemStats p = m_Problems[i];
                if (p.Sign != sign || p.Code != (int)HubProblemCode.Factor)
                    continue;
                sb.Append(any ? ", " : "").Append(p.Factor).Append(' ').Append(p.Pct).Append(" %");
                any = true;
            }
            if (!any)
                sb.Append("none");
            sb.Append('\n');
        }

        private static void Key(StringBuilder sb, ref bool first, string text)
        {
            if (!first)
                sb.Append(", ");
            sb.Append(text);
            first = false;
        }

        private void HandleException(Exception e, string where)
        {
            m_Disabled = true;
            if (!m_ErrorLogged)
            {
                m_ErrorLogged = true;
                Mod.Log.Error($"!!! {nameof(SkiResortStatsSystem)} EXCEPTION in {where} — the hub's resort statistics stop " +
                              $"updating until the next game load (the hub keeps the last rows):\n{e}");
            }
        }
    }
}
