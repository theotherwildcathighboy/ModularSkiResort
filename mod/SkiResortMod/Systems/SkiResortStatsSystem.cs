// SkiResortStatsSystem — ski-area membership and every per-area number of the Ski Resort hub, contract 3
// (Round E1; docs/research/econ-round/spec/ch5-hub-csharp.md §5, design docs/research/econ-round/design/
// hub-v3-design.md §8.4-§8.10). Created 2026-10-03 (Round U2, implementer I-1); rows of contract 3 written
// 2026-10-04 (Round E1, stream S5). SkiResortHubUISystem publishes these rows; this system owns no binding
// and writes nothing to the world.
//
// MEMBERSHIP (hub-u2-spec.md §3.1, unchanged; nothing new is stored except the OD-2 names written by
// SkiRunBindTriggerSystem):
//   * Ski area = a live Ski Area Welcome Sign (PrefabRef in the ownership sign set, None {Temp,
//     Deleted}; a Destroyed sign stays live, D5 — SkiRunOwnershipSystem.IsLiveGate).
//   * Run     = its SkiRunCatalogSystem row's Gate (TryGetBoundGate: sticky from bind time, re-links
//     free only when its sign died); Entity.Null → Not linked.
//   * Lodge   = an entry of a sign's InstalledUpgrade buffer whose PrefabRef is the lodge prefab
//     (ServiceUpgradeReferencesSystem.cs:54-65 fills that buffer from the upgrade's Owner). A
//     lodge-prefab building that no live sign lists is FREE-STANDING → Not linked. A listed lodge is
//     ACTIVE unless its buffer entry carries BuildingOption.Inactive (BuildingUtils.CheckOption
//     (InstalledUpgrade, …), BuildingUtils.cs:356 — the attraction chapter's rule, ch1 §7.2): the live
//     count feeds the problems band at once (C7), the Result's ActiveLodges feeds the tile.
//   * Lift    = TryResolveResort(base station xz); no base → top station; no station → the anchor
//     edge's start node: the nearest live sign with distance <= SkiRunOwnershipSystem.TetherMeters,
//     ties by distance then Index then Version (SkiResortIndex). None → Not linked (OD-1 (a): worked
//     out every rebuild, nothing saved; a NEW nearer sign takes the lifts but not the runs).
//   * Station (world selection; the hub's rule) = the resort of the first census row touching it,
//     else TryResolveResort(its xz) — exposed here as TryResolveResort.
//   SkiResortIndex is filled from THIS system's own chunk walk of {Building, PrefabRef, Transform}
//   None {Temp, Deleted}. TryResolveResort rebuilds the index on the spot when it was not built this load
//   or the building count moved since (event time only: world selection, lodge launch).
//
// ROWS (contract 3, ch5 §5.1): ResortStats (identity and slow facts: names, run and band counts, area,
// run vertical, lodges, lift rows, lift line, capacity), ResortLiveStats (every value that moves: open
// counts, snow, season, the score receipt, guests, tickets, costs, net), RunStats (identity, rating,
// served lift, value parts), RunLiveStats (open state, snow, value now, cost), LiftStats (the census row
// plus stations, power, status, ride time, runs served, cost), ProblemStats (the problems band, HubProblems
// .Build per area) and CityStats (settings and flags). The slow / live split keeps the header, switcher
// and run identity from re-rendering on every snow, guest or money change (design §8.1 "why the split").
//
// NUMBERS (ch5 §5.2; the hub NEVER recomputes the score, D10 — every score number is read):
//   * score, run parts, lift power / counted, efficiency factors: SkiResortAttractivenessSystem's last
//     recompute (TryGetAreaScore, TryGetRunScore, TryGetLiftScore keyed by (row id, row top) — e1-spec R5,
//     CopyEfficiencyFactors). A run is a MEMBER when it has a run view of its own bound sign. The receipt's
//     lodge part and terrain are HubRules.LodgePart / Terrain of the Result (their sum is AOpen, tested);
//     a run's full value is HubRules.RunValueFull of its parts. No result yet → −1 / 0 / 1 (ch5 §5.4).
//   * snow per run: SkiSnowInjectionSystem.GetRunSnow(run, kind, NodeBottomM, NodeTopM) for bound runs
//     (e1-spec R13: the catalog's node heights = the Geometry bounds ch1 passes), summed area-weighted by
//     HubSnowSums (share, depth of measured runs, any estimate); open / partly counts from the member
//     runs' RunPart.OpenFactor (the multiplier the score used, R3).
//   * season hint and top temperature: HubRules.Season / TopTempC from SnowLineSystem's day mean, base
//     height and lapse rate at the area's highest run top, the freezing point and GetSnowmakingMarginC.
//   * served lift and runs served: LiftCensusSystem.CopyServeLifts + LiftServedRule.Assign over EVERY
//     catalog row's NodeTopPos (ch2 §3.4-3.5). servedBy is Null when the run view says not served.
//   * run costs per kind: TryGetRunActiveTexels × TexelAreaM2 / 10⁴ ha × SkiRunUpkeepSystem
//     .CostPerHaMonth(kind) for bound runs — the numbers the upkeep bills (natural: 0, its plan area is
//     reported apart); area sums are float sums rounded once (C11).
//   * guests and tickets: SkiGuestCounterSystem's ledger (TryGetMonth, the month flags); last month
//     whenever the ledger knows one (Round S1 F7: a since-load month too, then LastMonthFull is false and the
//     hub marks it "(partial)"; −1 only before the first month roll); ticket income per month = HubRules
//     .TicketIncomeMonth (last month, or this month projected from at least a quarter); tickets off (price
//     0) → 0, not estimated (H7); the ticket gate = SkiMoneyRules.TicketGateOf with the counted runs of
//     the Result and the lift charge's Operating lifts (e1-spec R6) — the ticket system's two sources.
//   * lift charges: SkiLiftChargeSystem.TryGetLift / TryGetResort (Not linked: Entity.Null).
//   * buildings and wages: CityServiceBudgetSystem.cs:392-421 for the sign (its lodges merged by
//     GetUpkeepWithUsageScale) split into the wage part and the rest, × the CURRENT service budget
//     (GetServiceBudget, once per service per rebuild; it completes its temp arrays, :1060-1072) × daysPerYear
//     / 12; −1 when the prefab has no service upkeep or the budget reads 0 (no entry yet).
//   * net: HubRules.NetMonth(tickets, run + lift + buildings + wages); "≈" when the income is projected
//     or unknown or the building bill is unknown.
//   * share / estimate / jobs: as in contract 2 — ONE walk of AttractivenessProvider None {Temp, Deleted}
//     (TourismSystem.cs:241): S_all, S_res over the area's providers, SkiResortTourism.Estimate (tourists
//     only); jobs = the sign's WorkProvider.m_MaxWorkers and Employee count.
//   * lifts: LiftCensusSystem.CopyRows (state Complete → Running when ChairPinSystem.TryGetLoop has chairs
//     riding, else Static; Open / Unfinished / StationsOnly as the census, an Open row that lost a station
//     shown as Unfinished — finding l1-lifts-1); capacity at 1× game speed; ride time = HubRules
//     .RideTimeS(loop) (a chair rides half the loop); names as in contract 2 (custom base, else an unshared
//     top, else "Lift N").
//   * city: the settings (K, backcountry, ticket price, gating, margin), the game's Snow visuals option,
//     daysPerYear and the two other-mod labels (D34).
//
// WHEN: UIUpdate, registered between SkiRunCatalogSystem and SkiResortHubUISystem (add order,
// UpdateSystem.cs:158-161): each frame catalog → stats → hub. Only while Demand (set by the hub every frame:
// any panel binding observed) or a requested rebuild; nothing while closed. A rebuild runs when the CHANGE
// SIGNATURE moves — catalog Revision, census Revision, ChairPinSystem.LoopGeneration, attractiveness
// Generation, injection RunTexelGeneration, injection RunSnowGeneration, guest counter Revision, lift
// charge Revision (ch5 §5.7), SkiDaySystem Revision (Round S1) — at most once per MinRebuildIntervalS, on the
// rising edge of Demand at once,
// or on a 2 s wall-clock heartbeat (realtimeSinceStartup) that covers renames, jobs, the provider walk, the
// top temperature, the counted month fraction, a moved service budget and the settings. The building count
// is NOT in the signature (U2 fixer round, finding u2-data-04). Per-array content hashes gate each array's
// revision (ResortsRevision, ResortLiveRevision, RunsRevision, RunLiveRevision, LiftsRevision,
// ProblemsRevision); City is a value (IEquatable) the hub dedups.
//
// COST: one building walk, one provider walk, O(signs + runs + lifts + lodges) reads, one CopyServeLifts
// (a power read per Complete row) + Assign (runs × chords), the per-sign upkeep mirror and one
// GetServiceBudget per service, at most every MinRebuildIntervalS while the signature moves, else every
// HeartbeatS while open; never while closed. Measured: `Stats [cost]` once per load after 32 rebuilds
// with an area, run or lift row (F7 E3: empty rebuilds are not counted) (mean / max, the serve + assign and
// problems parts) and one WARN per load above SlowRebuildWarnMs (Round S1 F5: only for a rebuild with at least
// one area). Run hectares are the billed ones (F7 E2:
// SkiRunUpkeepSystem.BilledHa, capped at the plan area).
// Strings are built only in a rebuild (names, log lines); no per-frame allocation or formatting.
//
// ROAD EDGES (Round F7 D5, docs/research/field-test-7-fixes.md; field-test-7 findings sign-utilities.md §5,
// ped-access.md §4.4 / §5 item 4): the building walk reads Building.m_RoadEdge (Game.Buildings/Building.cs:8,
// set by RoadConnectionSystem.cs:551 / :606 / :634) of every live sign and lodge; Entity.Null → the
// HubProblemCode SignNoRoad / LodgeNoRoad rows (HubRules header). Read only, in the walk's own chunks (the
// query already completes Building's writers), no new sync point.
//
// SKI DAYS (Round S1, 2026-10-05, stream D; docs/research/ski-days/s1-spec.md §2.9, spec/ch4-hub.md §A.4, §C.4):
// contract 4 appends ten ResortLive fields — the area's ski-day state (HubRules.SkiDayStateOf over the attraction
// view and SkiDaySystem.TryGetArea), arrivals this / last month, sent this month (all, car, far without a car), on
// the way or on site now, the capacity, its limit and the powder percent — copied from SkiDaySystem's last published
// view (main thread, no job sync; −1, State and Limit 0, when the system is absent or not armed or has no view of the
// sign). "Off" = both ski-day settings 0, read once per rebuild (m_SkiDaysOff). SkiDaySystem.Revision is part of the
// change signature. Problem rows 20-22 (D28) come from the same view, only while ski days are on. F1: the Efficiency
// row's hysteresis state (the signs whose band showed it on the last build, m_EffRowOn) lives here, the rule in
// HubRules; it moves only while the hub rebuilds. SkiDaySystem and SkiLodgeParkingSystem are resolved at each load
// (GetExistingSystemManaged: the creation order stays Mod.cs's; null-safe); the stats DUMP is the one caller of
// their AppendDump (s1-spec N12).
//
// LOGS: `Stats [arm]` once per load (rules, constants, daysPerYear, providers); a WARN at arm if
// HubRules.ChairSpeedMps drifted from ChairPinSystem's; `Stats [rebuild] (why): …` (Round F7, log-audit D10)
// only when a count or a band problem changed, or on a request / the hub opening — through a LogWindow
// (LogWindowS: a burst folds into one line, trailing line within the window) — and every other rebuild
// counted into ONE `Stats [rebuild] n rebuild(s) in … s with no count or problem change` line per
// QuietSummaryS (flushed when the hub closes, at a load and at quit: the final state is never silent);
// `Stats [problems]` once per load (the first problems build); `Stats [cost]` once per load; one-shot `!!!`
// that disables the system until the next load (the hub keeps the last rows).

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
    // ── The row structs (contract 3; ch5 §5.1: the bindings.ts fields in PascalCase, Entity for Entity) ──

    /// <summary>bindings.ts ResortRow (contract 3): identity and slow facts. Kind 1 = Not linked (Sign Null), always last.</summary>
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
        public int CostGroomedMonth, CostSnowmakingMonth, CostNightMonth;
        public float HaNatural, HaGroomed, HaSnowmaking, HaNight;
        public int LiftCostMonth, LiftsBilled, LiftsBilledReduced;
        public int BuildingsMonth, WagesMonth, Jobs, JobsFilled;
        public int NetMonth; public bool NetEstimated;
        // Round S1 (contract 4, ski-days ch4 §A.1): appended in this order; −1 unknown, State and Limit 0.
        public int SkiDaysState, SkiDaysThisMonth, SkiDaysLastMonth, SkiDaysSent, SkiDaysSentCar, SkiDaysSentNoCar,
                   SkiDaysNow, SkiDaysCap, SkiDaysLimit, SkiDaysPowderPct;
    }

    /// <summary>bindings.ts RunRow (contract 3). Catalog row order.</summary>
    public struct RunStats
    {
        public Entity Entity; public string Name; public int Kind; public bool Bound;
        public float LengthM, DropM, AreaM2; public Entity Sign; public int Ordinal; public bool CustomName;
        public int Band; public float GradePct, SteepestPct; public bool Approx;
        public bool Member, Served; public Entity ServedBy;
        public float ValueFull, BandValue, SizeFactor, TypeFactor, ServedFactor;
    }

    /// <summary>bindings.ts RunLive. Same order and count as the runs rows.</summary>
    public struct RunLiveStats
    {
        public Entity Entity; public int OpenState; public float OpenFactor; public int SnowPct; public bool SnowEstimated;
        public float ValueNow; public int CostMonth;
    }

    /// <summary>bindings.ts LiftRow (contract 3): the contract-2 fields, then nine new ones. Entity = the census
    /// row id (base station, else top station, else the lowest-index line edge).</summary>
    public struct LiftStats
    {
        public Entity Entity, Top, Sign; public string Name; public bool CustomName;
        public int State;                    // 0 Running, 1 Static, 2 Open, 3 Unfinished, 4 StationsOnly (as today)
        public int Sections, Towers, Chairs, ChairsRiding, Gaps, TowersRemoved;
        public float LengthM, RiseM, HorizontalM; public bool AlignKnown; public float AlignOffsetM, AlignYawDeg;
        public float CapacityPerHour;        // −1 when no chair rides
        public bool HasBase, HasTop, Powered, Counted; public int Status;
        public float RideTimeS; public int RunsServed, CostMonth; public bool BilledReduced;
    }

    /// <summary>bindings.ts ProblemRow. Grouped by area in resorts order (ch5 §5.6).</summary>
    public struct ProblemStats
    {
        public Entity Sign; public int Code, Severity; public Entity Subject, SubjectTop; public string Name;
        public int Count, Pct; public string Factor; public int More, Detail, IfFixed; public string BuildItem;
    }

    /// <summary>bindings.ts CityStats (contract 3). The hub's CityStatsWriter / CityStatsComparer write and compare it.</summary>
    public struct CityStats : IEquatable<CityStats>
    {
        public bool Known; public int DaysPerYear;
        public int ScaleK, BackcountryPct, TicketPrice;
        public bool SnowGating; public float SnowmakingMarginC; public bool SnowVisualsOff;
        public bool TourismOverhaul, RealisticWorkplaces;

        public bool Equals(CityStats o)
        {
            return Known == o.Known && DaysPerYear == o.DaysPerYear && ScaleK == o.ScaleK && BackcountryPct == o.BackcountryPct &&
                   TicketPrice == o.TicketPrice && SnowGating == o.SnowGating && SnowmakingMarginC.Equals(o.SnowmakingMarginC) &&
                   SnowVisualsOff == o.SnowVisualsOff && TourismOverhaul == o.TourismOverhaul &&
                   RealisticWorkplaces == o.RealisticWorkplaces;
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
                return h * 31 + (RealisticWorkplaces ? 1 : 0);
            }
        }
    }

    public sealed partial class SkiResortStatsSystem : GameSystemBase
    {
        private static class Tuning
        {
            /// <summary>Wall-clock heartbeat (s) while the hub listens (the catalog's value).</summary>
            public const float HeartbeatS = 2f;

            /// <summary>Minimum wall-clock seconds between two signature-driven rebuilds (coalesces
            /// the catalog warm-up; tunable, unmeasured). The rising edge of Demand and requested
            /// rebuilds are immediate.</summary>
            public const float MinRebuildIntervalS = 0.5f;

            /// <summary>At most one "Stats [rebuild]" change line per this many seconds (a burst is folded,
            /// LogWindow).</summary>
            public const float LogWindowS = 5f;

            /// <summary>Round F7 (log-audit D10): rebuilds with no count or problem change are written as one
            /// summary line per this many seconds of wall clock (flushed at hub close, load and quit).</summary>
            public const float QuietSummaryS = 60f;

            /// <summary>Local position of a station's HV stub tip (SkiTopStationBuilding.Prefab /
            /// SkiBaseStationBuilding.Prefab ObjectSubNets: (0,0,−9) → (0,0,−12)).</summary>
            public const float StubTipLocalZ = -12f;

            /// <summary>Round E1 (ch5 §5.8): the `Stats [cost]` line after this many rebuilds of a load.</summary>
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
        // The change signature's reasons (OnUpdate); Round F7: also the quiet summary's counters, in this order.
        private const string WhyCatalog = "catalog";
        private const string WhyCensus = "lift census";
        private const string WhyLoops = "chair loops";
        private const string WhyAttract = "attractiveness";
        private const string WhyTexels = "snow texels";
        private const string WhySnow = "snow state";
        private const string WhyGuests = "guests";
        private const string WhyCharge = "lift charge";
        private const string WhySkiDays = "ski days";   // Round S1: appended after WhyHubOpened (counter indices kept)
        private static readonly string[] s_QuietReasons =
        {
            WhyHeartbeat, WhyCatalog, WhyCensus, WhyLoops, WhyAttract, WhyTexels, WhySnow, WhyGuests, WhyCharge, WhyHubOpened,
            WhySkiDays,
        };

        /// <summary>The shared-base lift label's separator ("&lt;name&gt; → &lt;top label&gt;", §3.7). The hub's
        /// rename strips a trailing separator + top label before storing (fixer round, m1-hub-csharp-2).</summary>
        internal const string LiftNameSeparator = " → ";

        /// <summary>Upper bound of the "Ski area N" search (a city never holds this many signs; the bind
        /// trigger's AutoNameMaxN).</summary>
        internal const int ResortNameMaxN = 10000;

        /// <summary>EfficiencyFactor byte → its enum NAME (the UI's SelectedInfoPanel.EFFICIENCY_FACTORS[<name>]
        /// key, Game.UI.InGame/EfficiencySection.cs:36); a byte without a name reads "Factor&lt;n&gt;" (ch5 §5.6).
        /// Built once; no allocation per rebuild.</summary>
        private static readonly string[] s_FactorNames = BuildFactorNames();

        // Systems.
        private SkiRunCatalogSystem m_Catalog;
        private SkiRunOwnershipSystem m_Ownership;
        private SkiResortAttractivenessSystem m_Attractiveness;
        private SkiSnowInjectionSystem m_Injection;
        private LiftCensusSystem m_Census;
        private ChairPinSystem m_ChairPin;
        private NameSystem m_NameSystem;
        private CitySystem m_CitySystem;
        private SnowLineSystem m_SnowLine;
        private TimeSystem m_TimeSystem;
        private ResourceSystem m_ResourceSystem;
        private PrefabSystem m_PrefabSystem;                 // DUMP only (notification prefab names)
        private SkiGuestCounterSystem m_GuestCounter;        // Round E1 (ch4 §10)
        private SkiLiftChargeSystem m_LiftCharge;
        private SkiLiftTicketSystem m_LiftTicket;            // DUMP only
        private CityServiceBudgetSystem m_CityServiceBudget;
        private SimulationSystem m_Simulation;
        // Round S1 (ch4 §A.4): resolved at each load with GetExistingSystemManaged, so the creation order stays
        // Mod.cs's (code-map V.2 #3; a GetOrCreate in OnCreate could create them first); null-safe everywhere.
        private SkiDaySystem m_SkiDays;
        private SkiLodgeParkingSystem m_LodgeParking;        // DUMP only (s1-spec N12)

        // Queries.
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
        private readonly List<Entity> m_LodgeEntities = new List<Entity>(4);      // every live lodge building
        private readonly Dictionary<Entity, int> m_LodgeResort = new Dictionary<Entity, int>();   // lodge → resort index
        private readonly HashSet<Entity> m_LodgeActive = new HashSet<Entity>();                    // listed and not Inactive
        private readonly HashSet<Entity> m_NoRoad = new HashSet<Entity>();                         // F7 D5: signs / lodges, m_RoadEdge Null
        private readonly Dictionary<Entity, int> m_ProviderOf = new Dictionary<Entity, int>();    // our signs / lodges → a
        private readonly HashSet<Entity> m_Ours = new HashSet<Entity>();
        private readonly List<SkiResortAggregate.RunIn> m_RunIn = new List<SkiResortAggregate.RunIn>(32);
        private readonly List<SkiResortAggregate.LiftIn> m_LiftIn = new List<SkiResortAggregate.LiftIn>(8);
        private readonly List<SkiResortAggregate.LodgeIn> m_LodgeIn = new List<SkiResortAggregate.LodgeIn>(4);
        private SkiResortAggregate.Agg[] m_Agg = new SkiResortAggregate.Agg[4];
        private AreaWork[] m_Work = new AreaWork[4];                                             // grown with m_Agg
        private readonly List<LiftCensus.Row> m_CensusRows = new List<LiftCensus.Row>(8);
        private readonly List<int> m_LiftOrder = new List<int>(8);
        private readonly List<int> m_LiftOrdinals = new List<int>(8);
        private readonly Dictionary<Entity, int> m_BaseUse = new Dictionary<Entity, int>();
        private readonly Dictionary<Entity, int> m_TopUse = new Dictionary<Entity, int>();        // fixer round, behaviour-4
        private readonly List<string> m_SignCustomNames = new List<string>(4);                   // fixer round, u2-data-02
        private readonly List<int> m_SignFallbackN = new List<int>(4);
        private readonly HashSet<string> m_NameScratch = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<Efficiency> m_EfficiencyScratch = new List<Efficiency>(8);         // F6 D5: the DUMP's factor list
        private float[] m_EfficiencyValues = new float[8];                                       // DUMP: SignEfficiency input
        // Round E1 (ch5 §5.3): served rule, problems, budgets.
        private readonly List<float3> m_RunTops = new List<float3>(32);
        private readonly List<LiftServedRule.Lift> m_ServeLifts = new List<LiftServedRule.Lift>(8);
        private readonly List<LiftServedRule.Chord> m_Chords = new List<LiftServedRule.Chord>(64);
        private readonly List<int> m_ServingLift = new List<int>(32);
        private readonly List<float> m_ServingDistance = new List<float>(32);
        private readonly List<int> m_ServedCount = new List<int>(8);
        private readonly List<Efficiency> m_FactorScratch = new List<Efficiency>(8);
        private readonly List<HubProblemFactor> m_PFactors = new List<HubProblemFactor>(8);
        private readonly List<HubProblemLift> m_PLifts = new List<HubProblemLift>(8);
        private readonly List<int> m_PLiftRows = new List<int>(8);
        private readonly List<HubProblem> m_POut = new List<HubProblem>(16);
        private readonly Dictionary<Entity, int> m_BudgetOf = new Dictionary<Entity, int>();    // service prefab → budget %, per rebuild
        private Func<int, string> m_ResortDefaultFormat;                                         // cached delegate (no per-rebuild allocation)
        private NativeList<ServiceUpkeepData> m_UpkeepScratchList;   // Persistent, created on first use, disposed in OnDestroy
        private readonly Dictionary<int, string> m_ResortNameCache = new Dictionary<int, string>();
        private readonly Dictionary<int, string> m_LiftNameCache = new Dictionary<int, string>();
        private string m_ResortTemplate, m_LiftTemplate, m_UnlinkedName;
        private readonly Stopwatch m_Timer = new Stopwatch();
        private static readonly Func<int, int> s_TargetTourists = TourismSystem.GetTargetTourists;

        /// <summary>Per-area work of one rebuild (ch5 §5.3). Index = resort row (signs, then Not linked).</summary>
        private struct AreaWork
        {
            public HubSnowSums Snow;
            public int RunsOpen, RunsPartly, LodgesActive, CompleteLifts;
            public double RunsValueFull;
            public float TopM;                 // −∞ = none
            public bool AnyGunRun;             // a bound snowmaking or night run
            public float CostGroomed, CostSnowmaking, CostNight;   // ¢/month, float sums (C11)
            public float HaNatural, HaGroomed, HaSnowmaking, HaNight;
            public float Capacity; public bool AnyCapacity;
            // Round F7 D5 (CollectLodges): Building.m_RoadEdge Null on the sign; the area's lodges with a Null edge
            // and the one with the lowest (Index, Version) — LodgeNoRoad's subject, stable across chunk order.
            public bool SignNoRoad; public int LodgesNoRoad; public Entity LodgeNoRoad;
            // The attraction chapter's view of the area (BuildResortRows; reused by BuildProblems).
            public bool HasResult;
            public SkiResortAttractivenessSystem.AreaScoreView View;
            // Round S1 (ch4 §A.4): SkiDaySystem's view of the area (BuildResortRows; reused by BuildProblems).
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
            m_SigAttract = int.MinValue, m_SigTexels = int.MinValue, m_SigSnow = int.MinValue, m_SigGuests = int.MinValue,
            m_SigCharge = int.MinValue, m_SigSkiDays = int.MinValue;
        // Round S1 (ch4 §A.4, §C.4): both ski-day settings 0 (set once per rebuild in BuildResortRows, read by
        // BuildProblems); F1's hysteresis state — the signs whose band showed the Efficiency row on the last build
        // (m_EffRowOn) and the ones of this build (m_EffRowOnNext, copied over after the loop: readonly, no swap).
        private bool m_SkiDaysOff;
        private readonly HashSet<Entity> m_EffRowOn = new HashSet<Entity>();
        private readonly HashSet<Entity> m_EffRowOnNext = new HashSet<Entity>();
        private float m_NextHeartbeat, m_LastRebuildTime = float.NegativeInfinity;
        private ulong m_HashResorts, m_HashResortLive, m_HashRuns, m_HashRunLive, m_HashLifts, m_HashProblems;
        // Round F7 (log-audit D10): the rebuild log (LogRebuild). Change lines through m_ChangeLog; the rest counted
        // into one quiet summary per QuietSummaryS.
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

        /// <summary>Set by SkiResortHubUISystem every frame (hub-u2-spec.md §3.6; ch5 §4.3).</summary>
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

        /// <summary>THE lift / station resort rule (§3.1): the nearest live sign within
        /// SkiRunOwnershipSystem.TetherMeters of <paramref name="xz"/>. Rebuilds the sign index on the
        /// spot when it was not built this load or the building count moved since. Event time only
        /// (world selection, lodge launch): the stale check is a chunk count. Main thread.</summary>
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
            m_Ownership = World.GetOrCreateSystemManaged<SkiRunOwnershipSystem>();
            m_Attractiveness = World.GetOrCreateSystemManaged<SkiResortAttractivenessSystem>();
            m_Injection = World.GetOrCreateSystemManaged<SkiSnowInjectionSystem>();
            m_Census = World.GetOrCreateSystemManaged<LiftCensusSystem>();
            m_ChairPin = World.GetOrCreateSystemManaged<ChairPinSystem>();
            m_NameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_SnowLine = World.GetOrCreateSystemManaged<SnowLineSystem>();
            m_TimeSystem = World.GetOrCreateSystemManaged<TimeSystem>();
            m_ResourceSystem = World.GetOrCreateSystemManaged<ResourceSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_GuestCounter = World.GetOrCreateSystemManaged<SkiGuestCounterSystem>();
            m_LiftCharge = World.GetOrCreateSystemManaged<SkiLiftChargeSystem>();
            m_LiftTicket = World.GetOrCreateSystemManaged<SkiLiftTicketSystem>();
            m_CityServiceBudget = World.GetOrCreateSystemManaged<CityServiceBudgetSystem>();
            m_Simulation = World.GetOrCreateSystemManaged<SimulationSystem>();

            m_BuildingQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
            });
            // Exactly TourismSystem's provider set (TourismSystem.cs:241, same in 1.6.2), read-only here.
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

        /// <summary>Round F7: quit to desktop raises no onGamePreload; the rebuild log is written here (LogWindow.cs
        /// header). Teardown never throws.</summary>
        protected override void OnDestroy()
        {
            try
            {
                FlushRebuildLog(UnityEngine.Time.realtimeSinceStartup);
            }
            catch (Exception)
            {
                // nothing left to report to
            }
            if (m_UpkeepScratchList.IsCreated)
                m_UpkeepScratchList.Dispose();
            base.OnDestroy();
        }

        /// <summary>Round F7: a load — write what the rebuild log holds before OnGameLoadingComplete resets it.</summary>
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
            m_SigSnow = m_SigGuests = m_SigCharge = m_SigSkiDays = int.MinValue;
            // Round S1 (ch4 §A.4, §C.4): the ski-day systems of this world (null-safe) and a fresh F1 state.
            m_SkiDays = World.GetExistingSystemManaged<SkiDaySystem>();
            m_LodgeParking = World.GetExistingSystemManaged<SkiLodgeParkingSystem>();
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
            Mod.Log.Info("Stats [arm] — the Ski Resort hub's rows (contract 4; econ-round spec ch5, ski-days spec ch4): ski areas = live Welcome " +
                         "Signs in (Index, Version) order + a Not linked row; runs by their bound sign (catalog Gate), lodges " +
                         "by their sign's InstalledUpgrade buffer (else free-standing → Not linked), lifts and stations by the " +
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
                         $"StubTipLocalZ={Tuning.StubTipLocalZ}; daysPerYear {m_TimeSystem.daysPerYear} (TimeSettingsData, " +
                         "copied by TimeSystem's first sim update — 1 before it; a later line reports the value once it is > " +
                         $"1); {providers} attractiveness provider(s) at load; contract 3: resorts/resortLive and runs/runLive " +
                         "split, problems built in C#; ski days from SkiDaySystem " + (m_SkiDays != null ? "(present)" : "(absent)"));
            // ch5 §5.8: the hub's ride time uses its own copy of the chair speed (HubRules is pure). Locals, not
            // the constants themselves, so the compiler does not fold the compare into unreachable code.
            float hubSpeed = HubRules.ChairSpeedMps, pinSpeed = ChairPinSystem.ChairSpeedMps;
            if (hubSpeed != pinSpeed)
            {
                Mod.Log.Warn($"Stats [arm]: HubRules.ChairSpeedMps {hubSpeed} != ChairPinSystem.ChairSpeedMps {pinSpeed} — " +
                             "ride times in the hub are wrong; fix HubRules");
            }
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
                    if (m_WasDemanded)   // Round F7: the hub just closed — write what the rebuild log holds (once)
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
                    // Change signature (ch5 §5.7): field reads only; no allocation. No building count.
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
                    else if (m_GuestCounter.Revision != m_SigGuests)
                        sig = WhyGuests;
                    else if (m_LiftCharge.Revision != m_SigCharge)
                        sig = WhyCharge;
                    else if (m_SkiDays != null && m_SkiDays.Revision != m_SigSkiDays)
                        sig = WhySkiDays;   // Round S1 (ch4 §A.4): moves at most once per SkiDaySystem pass
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

        // ── rebuild (ch5 §5.3) ──────────────────────────────────────────────────────────────────

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
            m_SigGuests = m_GuestCounter.Revision;
            m_SigCharge = m_LiftCharge.Revision;
            m_SigSkiDays = m_SkiDays != null ? m_SkiDays.Revision : 0;   // Round S1

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
            m_BudgetOf.Clear();

            bool ready = m_Ownership.PrefabSetsReady;
            int resorts = 0;
            if (ready)
            {
                BuildIndex();                        // 1. signs (+ the lodge building list)
                double sAll = WalkProviders();       // 2. S_all + our providers
                resorts = m_Index.Count;
                PrepareWork(resorts);
                CollectLodges(resorts);              // 3.
                CollectRuns(resorts);                // 4.
                AssignServedLifts();                 // 5.
                CollectLifts(resorts);               // 6.
                SkiResortAggregate.Aggregate(m_RunIn, m_LiftIn, m_LodgeIn, resorts, m_Agg);   // 7.
                CityStats city = ReadCity();         // read before the rows: ticket price, backcountry
                BuildResortRows(resorts, sAll, city);   // 8.
                BuildProblems(resorts, city);        // 9.
                City = city;                         // 10.
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

            // Swap and revise (per-array content hashes, ch5 §5.7).
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

        /// <summary>Round F7 (log-audit D10: `Stats [rebuild]` was 26 % of field test 7's log, 113 heartbeats with
        /// nothing new). A rebuild is written only when a COUNT or a BAND PROBLEM changed (LogKey: areas, runs, lift
        /// rows, lodges, and each band row's area, code, severity, count, detail and subject — not the Factor rows
        /// nor the drifting numbers), or when forced (a request: dev tab, DUMP, rename; the hub opening). Those
        /// lines go through m_ChangeLog (one per LogWindowS, a burst folded into one summary). Every other rebuild
        /// is counted and written as ONE quiet summary per QuietSummaryS (by reason, arrays moved, ms), flushed when
        /// the hub closes, at a load and at quit, so the final state is never silent. Per rebuild: field arithmetic
        /// and one hash over the published rows; a string only for a change (written or folded) or a summary.</summary>
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

        /// <summary>Round F7: the counts and band problems a change line reports (LogRebuild's rule), as one FNV-1a
        /// hash of the published rows. Factor rows and every drifting number (pct, more, ifFixed, factor) are left
        /// out: efficiency drift is the attractiveness area line's business.</summary>
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
              .Append(" s with no count or problem change (");
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

        /// <summary>Round F7: writes what the rebuild log holds — the hub closed, a load, quit.</summary>
        private void FlushRebuildLog(float now)
        {
            if (m_ChangeLog.Count > 0)
                WriteChangeSummary(now);
            if (m_QuietRebuilds > 0)
                WriteQuietSummary(now);
        }

        /// <summary>ch5 §5.8: the `Stats [cost]` line after the 32nd rebuild of a load and one WARN per load
        /// above SlowRebuildWarnMs. Field arithmetic per rebuild. Round F7 E3 (integrator, request in
        /// field-test-7-notes-impl5.md): only rebuilds with at least one area, run or lift row count, so the
        /// line no longer measures an empty city. Round S1 F5 (ski-days ch4 §A.4; field test 8 L299 warned for a
        /// rebuild with 0 areas): the WARN only for a rebuild with at least one area.</summary>
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

        // ── signs, lodges, providers ────────────────────────────────────────────────────────────

        /// <summary>The building walk: live signs into the index (stable order) and every live lodge
        /// building into m_LodgeEntities. Completes the query's writers first (chunk reads do not wait
        /// on running jobs — Entities semantics, the ownership header).</summary>
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
            var buildingHandle = GetComponentTypeHandle<Building>(isReadOnly: true);   // Round F7 D5: m_RoadEdge
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
                        else if (m_Attractiveness.IsLodgePrefab(prefab))
                        {
                            m_LodgeEntities.Add(entities[i]);
                            if (buildings[i].m_RoadEdge == Entity.Null)
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

        /// <summary>§3.1 lodge rule: lodges listed in a live sign's InstalledUpgrade buffer belong to that
        /// sign; every other live lodge building is free-standing (Not linked). Round E1 (ch5 §5.9): no lodge
        /// rows any more — the aggregate's LodgeIn (Score 0) and the live count of ACTIVE listed lodges
        /// (C7: the entry's BuildingOption.Inactive bit, BuildingUtils.CheckOption(InstalledUpgrade, …)).</summary>
        private void CollectLodges(int resorts)
        {
            m_LodgeResort.Clear();
            m_LodgeActive.Clear();
            for (int s = 0; s < m_SignEntities.Count; s++)
            {
                Entity sign = m_SignEntities[s];
                if (s < resorts)
                    m_Work[s].SignNoRoad = m_NoRoad.Contains(sign);   // Round F7 D5 (the building walk read it)
                if (!EntityManager.HasBuffer<InstalledUpgrade>(sign))
                    continue;
                DynamicBuffer<InstalledUpgrade> upgrades = EntityManager.GetBuffer<InstalledUpgrade>(sign, isReadOnly: true);
                for (int u = 0; u < upgrades.Length; u++)
                {
                    Entity upgrade = upgrades[u].m_Upgrade;
                    if (EntityManager.TryGetComponent(upgrade, out PrefabRef pr) && m_Attractiveness.IsLodgePrefab(pr.m_Prefab) &&
                        !m_LodgeResort.ContainsKey(upgrade))
                    {
                        m_LodgeResort[upgrade] = s;
                        if (!BuildingUtils.CheckOption(upgrades[u], BuildingOption.Inactive))   // BuildingUtils.cs:356
                            m_LodgeActive.Add(upgrade);
                    }
                }
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
                    // Round F7 D5: the same lodge set as the aggregate's Lodges count, so LodgesNoRoad <= Lodges.
                    Entity first = m_Work[resort].LodgeNoRoad;
                    if (m_Work[resort].LodgesNoRoad == 0 || lodge.Index < first.Index ||
                        (lodge.Index == first.Index && lodge.Version < first.Version))
                        m_Work[resort].LodgeNoRoad = lodge;
                    m_Work[resort].LodgesNoRoad++;
                }
            }
        }

        // ── runs ────────────────────────────────────────────────────────────────────────────────

        /// <summary>ch5 §5.3 step 4: identity, membership and run parts, snow, costs, the run tops for the
        /// served rule, and the per-area sums.</summary>
        private void CollectRuns(int resorts)
        {
            float texelHa = m_Injection.TexelAreaM2 / 10000f;
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
                if (bound)
                {
                    ref AreaWork work = ref m_Work[resort];
                    snow = m_Injection.GetRunSnow(row.Entity, (RunKind)row.Kind, row.NodeBottomM, row.NodeTopM);   // R13
                    work.Snow.Add(row.AreaM2, snow.Share, snow.MeanDepthM, snow.Source == RunSnowSource.Estimated);
                    if (row.Kind == (int)RunKind.Natural)
                    {
                        // Natural runs bill nothing (D22): their plan area is reported apart.
                        if (IsFinite(row.AreaM2) && row.AreaM2 > 0f)
                            work.HaNatural += row.AreaM2 / 10000f;
                    }
                    else if (m_Injection.TryGetRunActiveTexels(row.Entity, out int texels) && texels > 0)
                    {
                        // Round F7 E2 (integrator, request in field-test-7-notes-impl5.md): the billed hectares,
                        // capped at the run's plan area — the same number SkiRunUpkeepSystem charges.
                        ha = SkiRunUpkeepSystem.BilledHa(texels, texelHa, row.AreaM2);
                        cost = ha * SkiRunUpkeepSystem.CostPerHaMonth((RunKind)row.Kind);
                        switch (row.Kind)
                        {
                            case (int)RunKind.Snowmaking:
                                work.HaSnowmaking += ha;
                                work.CostSnowmaking += cost;
                                break;
                            case (int)RunKind.Night:
                                work.HaNight += ha;
                                work.CostNight += cost;
                                break;
                            default:
                                work.HaGroomed += ha;
                                work.CostGroomed += cost;
                                break;
                        }
                    }
                    if (IsFinite(row.TopM) && row.TopM > work.TopM)
                        work.TopM = row.TopM;
                    if (row.Kind == (int)RunKind.Snowmaking || row.Kind == (int)RunKind.Night)
                        work.AnyGunRun = true;
                }

                // Member: ch1 has a view of this run for its OWN bound sign (a stale view of a re-linked run is
                // not a member of the new area); members are always bound.
                SkiResortScore.RunPart part = default;
                bool member = false, served = false;
                if (bound && m_Attractiveness.TryGetRunScore(row.Entity, out SkiResortAttractivenessSystem.RunScoreView view) &&
                    view.Sign == row.Gate)
                {
                    member = true;
                    part = view.Part;
                    served = view.Input.Served;
                    ref AreaWork work = ref m_Work[resort];
                    if (part.OpenFactor >= 1.0)
                        work.RunsOpen++;
                    else if (part.OpenFactor > 0.0)
                        work.RunsPartly++;
                    work.RunsValueFull += HubRules.RunValueFull(in part);
                }

                m_RunIn.Add(new SkiResortAggregate.RunIn
                {
                    Resort = resort,
                    RateState = row.RateState,
                    Band = row.Band,
                    LengthM = row.LengthM,
                    AreaM2 = row.AreaM2,
                    TopM = row.TopM,
                    BottomM = row.BottomM,
                    CostMonth = cost,
                    SnowHa = ha,
                    Cover = float.NaN,          // contract 3: the aggregate's snow fields are unused (ch5 §5.3)
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
                    ServedBy = Entity.Null,     // step 5
                    ValueFull = member ? (float)HubRules.RunValueFull(in part) : 0f,
                    BandValue = member ? (float)part.BandValue : 0f,
                    SizeFactor = member ? (float)part.SizeFactor : 0f,
                    TypeFactor = member ? (float)part.TypeFactor : 0f,
                    ServedFactor = member ? (float)part.ServedFactor : 0f,
                });
                m_RunLiveNext.Add(new RunLiveStats
                {
                    Entity = row.Entity,
                    OpenState = (int)HubRules.OpenStateOf(member ? part.OpenFactor : 0.0, member),
                    OpenFactor = member ? (float)part.OpenFactor : 0f,
                    SnowPct = bound ? HubRules.Percent(snow.Share) : -1,
                    SnowEstimated = bound && snow.Source == RunSnowSource.Estimated,
                    ValueNow = member ? (float)part.Value : 0f,
                    CostMonth = (int)Math.Round(cost, MidpointRounding.AwayFromZero),
                });
                m_RunTops.Add(row.NodeTopPos);
            }
        }

        /// <summary>ch5 §5.3 step 5: the census as served-rule lifts (power read now) and the rule over every
        /// run's top node; servedBy = the serving lift's row id when the run view says served.</summary>
        private void AssignServedLifts()
        {
            long t0 = Stopwatch.GetTimestamp();
            m_Census.CopyServeLifts(m_ServeLifts, m_Chords);
            LiftServedRule.Assign(m_RunTops, m_ServeLifts, m_Chords, m_ServingLift, m_ServingDistance, m_ServedCount);
            for (int i = 0; i < m_RunsNext.Count && i < m_ServingLift.Count; i++)
            {
                RunStats r = m_RunsNext[i];
                int l = m_ServingLift[i];
                if (r.Served && l >= 0 && l < m_ServeLifts.Count)
                {
                    r.ServedBy = ToEntity(m_ServeLifts[l].Id);
                    m_RunsNext[i] = r;
                }
            }
            m_LastServeMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        }

        // ── lifts ───────────────────────────────────────────────────────────────────────────────

        /// <summary>Lift rows from the census (I-2, §5.2), resolved to a resort by §3.1, named by §3.7;
        /// contract 3 adds stations, power, status, ride time, runs served and the charge (ch5 §5.2).</summary>
        private void CollectLifts(int resorts)
        {
            m_CensusRows.Clear();
            m_Census.CopyRows(m_CensusRows);
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

                int riding = 0;
                double loopM = 0.0;
                bool loop = false;
                byte state;
                bool complete = c.State == LiftCensus.State.Complete;
                switch (c.State)
                {
                    case LiftCensus.State.Complete:
                        loop = m_ChairPin.TryGetLoop(baseStation, topStation, out riding, out loopM);
                        state = (byte)(loop && riding > 0 ? 0 : 1);
                        if (!loop)
                            riding = 0;
                        break;
                    case LiftCensus.State.Open:
                        // Header "lifts": a complete lift that lost a station keeps its key and opens;
                        // the hub says "not connected to a station at both ends" (3) for it, not "a
                        // section is missing" (2). Fixer round, finding l1-lifts-1.
                        state = (byte)(IsLive(baseStation) && IsLive(topStation) ? 2 : 3);
                        break;
                    case LiftCensus.State.Unfinished:
                        state = 3;
                        break;
                    default:
                        state = 4;
                        break;
                }
                float capacity = riding > 0 && loopM > 0.0
                    ? (float)(riding * ChairPinSystem.SeatsPerChair * (double)ChairPinSystem.ChairSpeedMps * 3600.0 / loopM)
                    : -1f;

                // Power and counted (ch5 §5.2; e1-spec R1, R5): the score's view keyed by (row id, row top); a
                // Complete row with no view → the hub's own CopyServeLifts read; not Complete → powered (not read).
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
                int costMonth = -1;
                bool reduced = false;
                if (m_LiftCharge.TryGetLift(id, topStation, out SkiLiftChargeSystem.LiftCharge lc))
                {
                    costMonth = RoundToInt(lc.RatePerMonth);
                    reduced = lc.Billing != SkiMoneyRules.LiftBilling.Full;
                }

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
                    Chairs = c.Chairs,
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
                    Chairs = c.Chairs,
                    ChairsRiding = riding,
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
                    Status = (int)HubRules.LiftStatusOf(state, powered),
                    RideTimeS = complete && loop ? HubRules.RideTimeS((float)loopM) : -1f,
                    RunsServed = serveRow && k < m_ServedCount.Count ? m_ServedCount[k] : 0,
                    CostMonth = costMonth,
                    BilledReduced = reduced,
                });
            }

            // Names (§3.7) — ordinals per resort by top height, no-top last.
            SkiResortAggregate.LiftOrdinals(m_LiftIn, m_LiftOrder, m_LiftOrdinals);
            for (int k = 0; k < m_LiftsNext.Count; k++)
            {
                LiftStats l = m_LiftsNext[k];
                LiftCensus.Row c = m_CensusRows[k];
                string defaultName = LiftDefault(m_LiftOrdinals[k]);
                // Header "lift names" (amended, behaviour-4): live base's name, else an unshared live top's.
                Entity baseE = ToEntity(c.Base), topE = ToEntity(c.Top);
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

        /// <summary>A station entity that exists and is not being deleted (Entity.Null → false). A
        /// census key may hold a removed station (header "lifts", l1-lifts-1).</summary>
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

        /// <summary>Stub tip y of a station (Transform ∘ local (0, 0, StubTipLocalZ)) and its xz;
        /// NaN when the entity is Null or has no Transform.</summary>
        private float StubTip(Entity station, out float2 xz)
        {
            xz = new float2(float.NaN, float.NaN);
            if (station == Entity.Null || !EntityManager.TryGetComponent(station, out Game.Objects.Transform t))
                return float.NaN;
            float3 tip = t.m_Position + math.mul(t.m_Rotation, new float3(0f, 0f, Tuning.StubTipLocalZ));
            xz = t.m_Position.xz;
            return tip.y;
        }

        /// <summary>A station-less row's anchor: its id edge's start node (Game.Net.Edge.m_Start →
        /// Game.Net.Node.m_Position); NaN when unreadable (TryResolve then returns Not linked).</summary>
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

        // ── resort rows (ch5 §5.4, §5.5) ────────────────────────────────────────────────────────

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

            // Climate and season inputs, read once.
            bool gating = city.SnowGating;
            float freezing = m_SnowLine.GetFreezingTemperature();
            float margin = city.SnowmakingMarginC;
            float dayMean = m_SnowLine.GetDayMeanTemperature();
            float baseHeight = m_SnowLine.GetBaseHeight();
            float lapse = m_SnowLine.GetLapsePerM();

            // Guests (ch4 §10) and the counted month fraction (ch5 §5.5).
            SkiResortLedger ledger = m_GuestCounter.Ledger;
            bool counterArmed = m_GuestCounter.IsArmed && ledger != null;
            bool sinceLoad = ledger != null && ledger.ThisMonthSinceLoad;
            bool lastMonthFull = ledger != null && ledger.LastMonthKnown && !ledger.LastMonthSinceLoad;
            // Round S1 F7 (ch4 §A.1): the six last-month guest values are sent whenever a month rolled since counting
            // started (full OR since load); LastMonthFull marks the partial one. Ticket income still needs a full month.
            bool lastKnown = ledger != null && ledger.LastMonthKnown;
            // E1 fixer (e1-spec §8.9 F8): a since-load month is counted from the counter's start frame — the
            // first tick after the load, or a calendar jump — not always from the load.
            uint countStart = m_GuestCounter.HasCountingSinceFrame ? m_GuestCounter.CountingSinceFrame : m_LoadFrameIndex;
            float countedFraction = HubRules.CountedFraction(sinceLoad, m_TimeSystem.normalizedDate,
                unchecked(m_Simulation.frameIndex - countStart), m_TimeSystem.daysPerYear);
            int price = city.TicketPrice;

            // Round S1 (ch4 §A.4): ski days are "off" when both settings are 0 (the clamped readers), once per rebuild;
            // BuildProblems reads the field. SkiDaySystem's views are read per sign below (main thread, no job sync).
            SkiResortSettings settings = Mod.Settings;
            m_SkiDaysOff = SkiResortSettings.SkiDayLocalsPctOf(settings) == 0 && SkiResortSettings.SkiDayTouristsPctOf(settings) == 0;
            bool skiDaysArmed = m_SkiDays != null && m_SkiDays.IsArmed;

            // Names (§3.2, amended in the fixer round, finding u2-data-02): custom names first; an unnamed
            // sign's display-only label is the smallest "Ski area N" no custom name and no earlier
            // unnamed sign's label uses (SkiResortAggregate.FallbackOrdinals) — never a duplicate.
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

                // Name (§3.2, amended): the sign's custom name, else its smallest free "Ski area N" (above).
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

                // ── ResortLiveStats of a sign (ch5 §5.2, §5.4) ──
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
                    live.LiftsValue = (float)(r.LiftsValue * r.LiftShare);
                    live.LiftsComplete = r.LiftsComplete;
                    live.LiftsCounted = r.LiftsCounted;
                    live.VarietyMultiplier = (float)r.VarietyMultiplier;
                    live.VerticalMultiplier = (float)r.VerticalMultiplier;
                    live.ScoreVerticalM = (float)r.VerticalM;
                    live.EfficiencyPct = v.EfficiencyPct;
                }
                else
                {
                    // Just placed, ch1 disabled, or before its first recompute (ch5 §5.4).
                    live.Attraction = live.AttractionFull = live.LodgesActive = -1;
                    live.LiftsComplete = live.LiftsCounted = live.EfficiencyPct = live.OpenSharePct = -1;
                    live.VarietyMultiplier = live.VerticalMultiplier = 1f;
                }
                live.RunsValueFull = (float)work.RunsValueFull;
                live.Status = (int)HubRules.Status(row.Runs, live.RunsOpen, live.RunsPartly);

                // Snow and season.
                live.SnowPct = work.Snow.SnowPct();
                live.SnowDepthCm = work.Snow.DepthCm();
                live.SnowEstimated = work.Snow.AnyEstimated;
                float topTemp = IsFinite(work.TopM) ? HubRules.TopTempC(dayMean, work.TopM, baseHeight, lapse) : float.NaN;
                live.TopTempKnown = !float.IsNaN(topTemp);
                live.TopTempC = live.TopTempKnown ? topTemp : 0f;
                live.SeasonHint = (int)HubRules.Season(gating, row.Runs, live.RunsOpen + live.RunsPartly, work.AnyGunRun,
                    live.TopTempKnown, topTemp, freezing, margin);

                // Share, estimate, jobs (as contract 2: the provider walk, the sign's workplace).
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
                if (anyProvider)
                {
                    live.SharePct = SkiResortTourism.SharePct(sRes, sAll);
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
                live.Jobs = jobs;
                live.JobsFilled = filled;
                live.NextStep = (int)HubRules.NextStep(row.Lodges, work.CompleteLifts, row.Runs);   // C8

                // Guests and tickets (ch5 §5.5).
                live.ThisMonthSinceLoad = sinceLoad;
                live.LastMonthFull = lastMonthFull;
                int ticketIncome, ticketPaid;
                bool ticketEstimated;
                if (counterArmed)
                {
                    SkiResortLedger.Entry e = m_GuestCounter.TryGetMonth(sign, out SkiResortLedger.Entry entry) ? entry : default;
                    live.GuestsThisMonth = e.This.Guests;
                    live.TouristsThisMonth = e.This.TouristGuests;
                    live.TouristGroupsThisMonth = e.This.TouristParties;
                    live.ResidentsThisMonth = e.This.LocalGuests;
                    live.PaidThisMonth = e.This.TicketsPaid;
                    live.UnpaidThisMonth = e.This.TicketsUnpaid;
                    live.FreeThisMonth = e.This.TicketsFree;
                    live.TicketsThisMonth = ClampToInt(Math.Max(0L, e.This.TicketIncome));
                    live.GuestsLastMonth = lastKnown ? e.Last.Guests : -1;   // Round S1 F7: lastKnown, not lastMonthFull
                    live.TouristsLastMonth = lastKnown ? e.Last.TouristGuests : -1;
                    live.TouristGroupsLastMonth = lastKnown ? e.Last.TouristParties : -1;
                    live.ResidentsLastMonth = lastKnown ? e.Last.LocalGuests : -1;
                    live.PaidLastMonth = lastKnown ? e.Last.TicketsPaid : -1;
                    live.TicketsLastMonth = lastKnown ? ClampToInt(Math.Max(0L, e.Last.TicketIncome)) : -1;
                    ticketIncome = HubRules.TicketIncomeMonth(lastMonthFull, e.Last.TicketIncome, e.Last.TicketsPaid,
                        e.This.TicketIncome, e.This.TicketsPaid, countedFraction, out ticketPaid, out ticketEstimated);
                }
                else
                {
                    live.GuestsThisMonth = live.TouristsThisMonth = live.TouristGroupsThisMonth = live.ResidentsThisMonth = -1;
                    live.PaidThisMonth = live.UnpaidThisMonth = live.FreeThisMonth = live.TicketsThisMonth = -1;
                    live.GuestsLastMonth = live.TouristsLastMonth = live.TouristGroupsLastMonth = live.ResidentsLastMonth = -1;
                    live.PaidLastMonth = live.TicketsLastMonth = -1;
                    ticketIncome = ticketPaid = -1;
                    ticketEstimated = true;
                }
                if (price == 0)
                {
                    // Tickets off (second pass, H7): nothing to estimate, no "≈" without a reason.
                    ticketIncome = ticketPaid = 0;
                    ticketEstimated = false;
                }
                live.TicketIncomeMonth = ticketIncome;
                live.TicketPaidMonth = ticketPaid;
                live.TicketIncomeEstimated = ticketEstimated;

                // Ski days (Round S1, ch4 §A.4): SkiDaySystem's last published view of the sign, else −1 (State and
                // Limit 0). `d = default` first: definite assignment does not flow through `has` (CS0165).
                SkiDayAreaView d = default;
                bool has = skiDaysArmed && m_SkiDays.TryGetArea(sign, out d);
                m_Work[s].HasSkiDays = has;
                m_Work[s].SkiDays = d;
                if (has)
                {
                    live.SkiDaysThisMonth = d.ArrivedThisMonth;
                    live.SkiDaysLastMonth = d.ArrivedLastMonth;   // −1 until a month roll (partial: LastMonthFull false)
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

                // Lift charge and the ticket gate (e1-spec R6: the ticket system's two sources).
                float liftRate = 0f;
                int operating = 0;
                if (m_LiftCharge.TryGetResort(sign, out SkiLiftChargeSystem.ResortLiftCharge charge))
                {
                    liftRate = Finite0(charge.RatePerMonth);
                    operating = charge.Operating;
                    live.LiftCostMonth = RoundToInt(liftRate);
                    live.LiftsBilled = charge.Lifts;
                    live.LiftsBilledReduced = charge.Reduced;
                }
                live.TicketGate = (int)SkiMoneyRules.TicketGateOf(price, hasResult ? (float)r.Efficiency : 1f, hasResult,
                    hasResult ? r.RunsCounted : 0, operating);   // C9

                // Run costs and hectares (C11: float sums, rounded once).
                live.CostGroomedMonth = RoundToInt(work.CostGroomed);
                live.CostSnowmakingMonth = RoundToInt(work.CostSnowmaking);
                live.CostNightMonth = RoundToInt(work.CostNight);
                live.HaNatural = work.HaNatural;
                live.HaGroomed = work.HaGroomed;
                live.HaSnowmaking = work.HaSnowmaking;
                live.HaNight = work.HaNight;

                // Buildings and wages (game formula at the current service budget), then the net.
                float buildingsF = -1f, wagesF = -1f;
                if (upkeepReady && BuildingCostSplit(sign, m_SignPrefabs[s], economy, resourcePrefabs, out float b, out float w))
                {
                    buildingsF = b;
                    wagesF = w;
                }
                live.BuildingsMonth = buildingsF >= 0f ? RoundToInt(buildingsF) : -1;
                live.WagesMonth = wagesF >= 0f ? RoundToInt(wagesF) : -1;
                float runCosts = work.CostGroomed + work.CostSnowmaking + work.CostNight;
                live.NetMonth = HubRules.NetMonth(live.TicketIncomeMonth, live.TicketIncomeEstimated, runCosts, liftRate,
                    buildingsF, wagesF, out bool netEstimated);
                live.NetEstimated = netEstimated;
                m_ResortLiveNext.Add(live);
            }
        }

        /// <summary>The Not linked live row (ch5 §5.4): −1 for every score, guest and ticket number, 0 / 1 /
        /// false elsewhere; only the lift-charge fields carry values (unlinked lifts bill, unlinked runs do
        /// not), and the net is minus the lift charge, not estimated.</summary>
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
                // Round S1 (ch4 §A.4): no ski area, no ski days — State and Limit 0, the other eight −1.
                SkiDaysState = 0, SkiDaysLimit = 0,
                SkiDaysThisMonth = -1, SkiDaysLastMonth = -1, SkiDaysSent = -1, SkiDaysSentCar = -1, SkiDaysSentNoCar = -1,
                SkiDaysNow = -1, SkiDaysCap = -1, SkiDaysPowderPct = -1,
            };
            if (m_LiftCharge.TryGetResort(Entity.Null, out SkiLiftChargeSystem.ResortLiftCharge charge))
            {
                live.LiftCostMonth = RoundToInt(Finite0(charge.RatePerMonth));
                live.LiftsBilled = charge.Lifts;
                live.LiftsBilledReduced = charge.Reduced;
            }
            live.NetMonth = -live.LiftCostMonth;
            return live;
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

        /// <summary>CityServiceBudgetSystem.cs:392-421 (identical in 1.6.2) for ONE sign (its installed lodges
        /// merged by GetUpkeepWithUsageScale), split into the building part and the wage part, per MONTH at the
        /// CURRENT service budget: × budget / 100 × daysPerYear / 12 (ch5 §5.5, ch4 §12.3 item 4). Per entry
        /// with amount &gt; 0: RoundToInt(value), value including the wage for the Money entry and × 0.1 when
        /// inactive, as vanilla's m_FullCost (:420); the wage part = the wage when a Money entry was seen (it
        /// is 0 for an inactive sign, CityServiceUpkeepSystem.cs:628-633). Rounding before the budget scale
        /// differs from vanilla's m_Cost by &lt; 1 ¢ per entry per day ("game formula"). False (both −1) when
        /// the prefab has no ServiceObjectData or ServiceUpkeepData, or the budget reads ≤ 0 (no budget entry
        /// yet, :1063-1066).</summary>
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

        /// <summary>CityServiceBudgetSystem.GetServiceBudget(service) once per service per rebuild (it completes
        /// m_TempArrayDeps and reads the budget singleton, :1060-1066). 0 = no budget entry yet (F9). A throw
        /// (no budget singleton yet) reads −1 for this rebuild with one WARN per load: the hub then shows
        /// "n/a" for buildings and wages, never a wrong number.</summary>
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

        /// <summary>ch5 §5.2 CityStats: the settings (one read each), the snow flags and the other-mod labels.</summary>
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
            };
        }

        // ── problems (ch5 §5.6) ─────────────────────────────────────────────────────────────────

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
                        CustomName = l.CustomName,   // E1 fixer pass 2: the station-pair row takes the kept name
                    });
                    m_PLiftRows.Add(k);
                }
                SkiResortAggregate.Agg a = m_Agg[s];
                // Round S1 D28 (ch4 §C.4): the ski-day rows only for a sign with a SkiDaySystem view while ski days are on.
                bool skiDayRows = !notLinked && work.HasSkiDays && !m_SkiDaysOff;
                SkiDayAreaView d = work.SkiDays;
                var input = new HubProblemInput
                {
                    Unlinked = notLinked,
                    Lodges = row.Lodges, LodgesActive = work.LodgesActive, FreeLodges = row.FreeLodges,   // C7
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
                    IfLodgesOn = hasResult && row.Lodges > 0 ? HubRules.AttractionWithLodges(in r, row.Lodges) : -1,
                    // Round F7 D5: road edges, read in the building walk (no result needed: a placement fact).
                    SignNoRoad = !notLinked && work.SignNoRoad,
                    LodgesNoRoad = notLinked ? 0 : work.LodgesNoRoad,
                    // Round S1 D28: from SkiDaySystem's view (else false / 0 / 0).
                    SkiDaysCannotReach = skiDayRows && d.CannotReach,
                    LodgesNoParking = skiDayRows ? d.LodgesNoParking : 0,
                    LodgesNotSlopeside = skiDayRows ? d.LodgesNotSlopeside : 0,
                    // Round S1 F1: did the band show the Efficiency row on this area's previous build?
                    EfficiencyRowWasOn = !notLinked && m_EffRowOn.Contains(sign),
                };
                HubProblems.Build(in input, m_POut);
                for (int i = 0; i < m_POut.Count; i++)
                {
                    HubProblem p = m_POut[i];
                    bool liftRow = p.LiftIndex >= 0 && p.LiftIndex < m_PLiftRows.Count;
                    LiftStats lift = liftRow ? m_LiftsNext[m_PLiftRows[p.LiftIndex]] : default;
                    int code = (int)p.Code;
                    // Subject: the sign for the lodge, efficiency and (F7) SignNoRoad rows; F7 LodgeNoRoad: the lodge
                    // (lowest entity) with no road; a lift row: its census id; Round S1 D28: the lodge with the most car
                    // failures (else the sign), the first lodge with no parking, the first lodge far from the runs
                    // (lowest entity, SkiDaySystem's view); else Null.
                    Entity subject = liftRow ? lift.Entity : Entity.Null;
                    if ((code >= 1 && code <= 4) || p.Code == HubProblemCode.SignNoRoad)
                        subject = sign;
                    else if (p.Code == HubProblemCode.LodgeNoRoad)
                        subject = work.LodgeNoRoad;
                    else if (p.Code == HubProblemCode.SkiDaysCannotReach)
                        subject = d.ReachLodge != Entity.Null ? d.ReachLodge : sign;
                    else if (p.Code == HubProblemCode.NoParkingNearLodge)
                        subject = d.FirstLodgeNoParking;
                    else if (p.Code == HubProblemCode.LodgeNotSlopeside)
                        subject = d.FirstLodgeNotSlopeside;
                    if (p.Code == HubProblemCode.Efficiency)
                        m_EffRowOnNext.Add(sign);   // F1: the band shows it on this build (Not linked never has one)
                    m_ProblemsNext.Add(new ProblemStats
                    {
                        Sign = sign,
                        Code = code,
                        Severity = p.Severity,
                        Subject = subject,
                        SubjectTop = liftRow ? lift.Top : Entity.Null,
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
            // Round S1 F1 (ch4 §C.4, refutation RF-3): this build's Efficiency rows become the next build's state. COPY,
            // not swap (both sets are readonly, CS0191); a sign that is gone drops out; no allocation once grown. A
            // foreach + Add, not UnionWith: UnionWith(IEnumerable<T>) enumerates through the interface and boxes the
            // enumerator (System.Core HashSet`1, the game's own copy); the struct enumerator does not (fixer, JP-4).
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

        // ── names ───────────────────────────────────────────────────────────────────────────────

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

        // ── hashes (per-array revisions, ch5 §5.7) ──────────────────────────────────────────────

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
                h = Mix(h, (uint)r.CostGroomedMonth);
                h = Mix(h, (uint)r.CostSnowmakingMonth);
                h = Mix(h, (uint)r.CostNightMonth);
                h = MixF(h, r.HaNatural);
                h = MixF(h, r.HaGroomed);
                h = MixF(h, r.HaSnowmaking);
                h = MixF(h, r.HaNight);
                h = Mix(h, (uint)r.LiftCostMonth);
                h = Mix(h, (uint)r.LiftsBilled);
                h = Mix(h, (uint)r.LiftsBilledReduced);
                h = Mix(h, (uint)r.BuildingsMonth);
                h = Mix(h, (uint)r.WagesMonth);
                h = Mix(h, (uint)r.Jobs);
                h = Mix(h, (uint)r.JobsFilled);
                h = Mix(h, (uint)r.NetMonth);
                h = MixB(h, r.NetEstimated);
                // Round S1 (contract 4, mandatory: a field not hashed would never re-send, code-map V.1 #25).
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

        // ── helpers ─────────────────────────────────────────────────────────────────────────────

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

        /// <summary>round half away from zero, clamped to int; non-finite → 0 (C# never sends −0: ints).</summary>
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

        // ── DUMP (ch5 §5.8; event time only) ────────────────────────────────────────────────────

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
                  .Append(", Realistic Workplaces ").Append(c.RealisticWorkplaces).Append('\n');
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
                      .Append(" state ").Append(l.State).Append(' ').Append(((HubLiftStatus)l.Status).ToString())
                      .Append(", sections ").Append(l.Sections).Append(", chairs ").Append(l.ChairsRiding).Append('/').Append(l.Chairs)
                      .Append(", length ").Append(F(l.LengthM, "F0")).Append(" m, rise ").Append(F(l.RiseM, "F0"))
                      .Append(" m, powered ").Append(l.Powered).Append(", counted ").Append(l.Counted)
                      .Append(", ride ").Append(F(l.RideTimeS, "F0")).Append(" s, serves ").Append(l.RunsServed)
                      .Append(" run(s), cost ").Append(l.CostMonth).Append("/month").Append(l.BilledReduced ? " (at 25 %)" : "")
                      .Append(", sign ").Append(Ent(l.Sign)).Append('\n');
                }
            }
            catch (Exception e)
            {
                sb.Append("  stats: DUMP failed (").Append(e.GetType().Name).Append(": ").Append(e.Message).Append(")\n");
            }
            // ch4 §12.3 item 6 / e1-spec I-29: the score, guest, ticket and lift-charge systems' own lines.
            m_Attractiveness.AppendDump(sb);
            m_GuestCounter.AppendDump(sb);
            m_LiftTicket.AppendDump(sb);
            m_LiftCharge.AppendDump(sb);
            // Round S1 (ch4 §A.4, s1-spec N12; D26 "in DUMP always"): the ski-day system's lines, then the lodge parking
            // census right after. This DUMP is the ONE caller of both; each in its own try, so one never hides the other.
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
              // Round S1 F7 (ch4 refutation RF-9): a partial month prints with " (partial)", as the hub shows it.
              .Append(l.GuestsLastMonth >= 0
                  ? l.GuestsLastMonth.ToString(CultureInfo.InvariantCulture) + (l.LastMonthFull ? "" : " (partial)")
                  : "not counted")
              .Append(", tickets/month ").Append(l.TicketIncomeMonth).Append(l.TicketIncomeEstimated ? " ≈" : "")
              .Append(" (paid ").Append(l.TicketPaidMonth).Append("), gate ").Append(GateName(l.TicketGate))
              .Append("; runs ").Append(l.CostGroomedMonth).Append('/').Append(l.CostSnowmakingMonth).Append('/').Append(l.CostNightMonth)
              .Append(" (").Append(F(l.HaGroomed, "F1")).Append('/').Append(F(l.HaSnowmaking, "F1")).Append('/').Append(F(l.HaNight, "F1"))
              .Append(" ha with snow; natural ").Append(F(l.HaNatural, "F1")).Append(" ha), lifts ").Append(l.LiftCostMonth)
              .Append(" (").Append(l.LiftsBilled).Append(" billed, ").Append(l.LiftsBilledReduced).Append(" at 25 %), buildings ")
              .Append(l.BuildingsMonth).Append(", wages ").Append(l.WagesMonth).Append(", jobs ").Append(l.JobsFilled).Append('/')
              .Append(l.Jobs).Append(", net ").Append(l.NetMonth).Append(l.NetEstimated ? " ≈" : "").Append('\n');
            if (!notLinked)
            {
                // Round S1 (ch4 §A.4): the ski-day fields the hub sent (signs only).
                sb.Append("      ski days: ").Append(((HubSkiDayState)l.SkiDaysState).ToString()).Append(" (")
                  .Append(((SkiDayLimit)l.SkiDaysLimit).ToString()).Append(" limit, now ").Append(l.SkiDaysNow).Append(" of cap ")
                  .Append(l.SkiDaysCap).Append("), arrived ").Append(l.SkiDaysThisMonth).Append(" this month / ")
                  .Append(l.SkiDaysLastMonth).Append(" last").Append(l.SkiDaysLastMonth >= 0 && !l.LastMonthFull ? " (partial)" : "")
                  .Append(", sent ").Append(l.SkiDaysSent).Append(" (car ").Append(l.SkiDaysSentCar).Append(", no car ")
                  .Append(l.SkiDaysSentNoCar).Append("), powder ").Append(l.SkiDaysPowderPct).Append(" %\n");
                AppendSignDetail(sb, r.Sign);
                AppendSignNotifications(sb, r.Sign);
            }
            AppendProblems(sb, r.Sign);
        }

        private static string GateName(int gate)
        {
            return gate < 0 ? "-" : ((SkiMoneyRules.TicketGate)gate).ToString();
        }

        /// <summary>F6 D5 (log-audit defect 10; attraction-honesty.md §9), contract 3: one dump line per sign —
        /// its own AttractivenessProvider now (normally absent since E1, ch1 §1.1) and its efficiency with every
        /// factor below 100 %, worst first. Efficiency = round(100 × SkiResortScore.SignEfficiency(factors))
        /// — ch1's one re-implementation of BuildingUtils.cs:121-133 (e1-spec I-28; the Span overload of
        /// BuildingUtils.GetEfficiency does not compile on net48, CS7069). Event time only; log only.</summary>
        private void AppendSignDetail(StringBuilder sb, Entity sign)
        {
            sb.Append("      sign ").Append(Ent(sign)).Append(": own provider ");
            bool exists = EntityManager.Exists(sign);
            if (exists && EntityManager.TryGetComponent(sign, out AttractivenessProvider provider))
                sb.Append(provider.m_Attractiveness).Append(" now");
            else
                sb.Append("none");
            // Round F7 D5: the road edge the SignNoRoad row reads (Building.m_RoadEdge), with its prefab name.
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
                // Round F7 (log-audit D14: "NotEnoughEmployees 80 %" here vs "-18 %" on the problems line): ONE
                // rounding for every factor of the DUMP — HubRules.FactorEffectPct, the game's panel number
                // (EfficiencySection.cs:99-127), as the problems line's Factor rows carry it.
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

        /// <summary>ch5 §7.2 (DUMP only, U5): the sign's live notifications by prefab name
        /// (NotificationsSection.GetNotifications, Game.UI.InGame/NotificationsSection.cs:352-365; names by
        /// PrefabSystem.GetPrefabName). The hub sends no notification key; this line settles which names the
        /// game raises on our sign.</summary>
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

        /// <summary>ch5 §5.8: `problems: Code(k=v, …), … | none; factors Name pct %, … | none` from the
        /// published rows of the area (detail keys only when not default).</summary>
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
                    Key(sb, ref first, "lodge=" + Ent(p.Subject));   // Round F7 D5: the row's subject
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
