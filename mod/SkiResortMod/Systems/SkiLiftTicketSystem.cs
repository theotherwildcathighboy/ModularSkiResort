// Lift tickets: each arriving guest (SkiGuestCounterSystem.DrainArrivals) pays the ticket price from their household into the city's
// balance when the gate is open (SkiMoneyRules.TicketGateOf). Never into debt: TicketsPaid = min(people, money / price). The rest
// are admitted free and counted as unpaid. Money moves like the vanilla transit fare (ResidentAISystem.cs:3922-3935). One Burst IJob.
// Every payment is one FeeEvent (public transportation, like the bus fare). The job must NOT add it to PlayerMoney.
// BudgetApplySystem.cs:28-60 credits the fee line itself, so adding it here would pay the city twice.
// Phase: GameSimulation, UpdateAfter<SkiLiftTicketSystem, SkiGuestCounterSystem> (Mod.cs). Not load-bearing: the counter's pending list
// is DRAINED. Interval 16. OnGamePreload books the job's results first, after the counter's (BookInFlightForPreload).
// Engine notes: EngineNotes/SkiLiftTicketSystem.md

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.City;
using Game.Economy;
using Game.Prefabs;
using Game.Simulation;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using GameResources = Game.Economy.Resources;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiLiftTicketSystem : GameSystemBase
    {
        private static class Tuning
        {
            /// <summary>At most one gate line per this many wall-clock seconds.</summary>
            public const float LogWindowS = 10f;
            /// <summary>Ticks with arrivals timed for the `Tickets [cost]` line.</summary>
            public const int CostTicks = 64;
        }

        // SpendPerPerson = the lodge spend per paying guest of this request (0 = none). The main thread fills it. Spends / SpendIncome =
        // the guests who spent and the ₵ moved for it.
        // Outing (1.0.3 T258) = a game-sent tourist party (a vanilla attraction outing from the meeting scan, not a leisure arrival): it passes
        // the party price check at the gate (SkiDayRules.PartyStays).
        private struct TicketRequest { public int Slot; public Entity Household; public int People; public byte Tourist; public byte Outing; public int SpendPerPerson; }
        // RunStateKnown = score present and not held. SchoolActive = the area's score is known and its ski school is installed and not
        // Inactive (Result.SchoolActive).
        // 1.0.3 destination pass (SkiPassRules): DestShare = the share of tourist parties on a pass at this area (its price tolerance prices the
        // fee), DestFee = what a person on one pays per counted day. The main thread fills both (BuildSlots).
        // PriceTolerance (T258) = the area's best lodge's tolerance for the party price check (SkiDaySystem.PriceToleranceOf, as the ski days).
        // T284 (1.0.4) per-area prices (SkiLiftTicketSystem.Prices.cs AreaPriceOf, filled by BuildSlots): Ticket = the area's day ticket (0 =
        // tickets off there), LessonPrice, SalePrice = its pass price now (0 = not on sale), PassDemand = the pass demand at it (value left),
        // AppliedPos = the area's last applied hourly sale window position this season (-1 none), OnDefault = the area's plan in force is the
        // Options default (it sells and honors pool passes).
        private struct TicketSlot
        {
            public Entity Sign; public byte RunStateKnown; public int OpenRuns; public byte SchoolActive; public double DestShare; public int DestFee; public int PriceTolerance;
            public int Ticket, LessonPrice, SalePrice; public double PassDemand, AppliedPos; public byte OnDefault;
        }
        // Booked = the income booked as public transportation fees instead of added to PlayerMoney (tickets, passes, lessons, lodge
        // spend). PassSold (0/1) and PassIncome = the pass this arrival bought (PassSeason = its season key). PassHolders = the people who
        // came in on a pass.
        // DestPaid / DestIncome (1.0.3) = the tourists on a destination pass who paid the fee, and the fee money (pass income, not tickets).
        // PriceTurned (T258) = the people of a game-sent party that walked away at the price: no ticket, lesson or lodge spend.
        private struct TicketResult { public int Paid, Unpaid, Free, Income, Spends, SpendIncome, Booked, PassSold, PassIncome, PassHolders, PassSeason, Lessons, LessonIncome, DestPaid, DestIncome, DestUnpaid, PriceTurned; public byte NoCity; }
        // One SkiSeasonPass to add or overwrite on the main thread (Flags 0 = a pass bought, else Declined.
        // CannotPay 1 = Declined because the household could not afford the pass it rolled).
        // T284: Sign = the area whose sale decided it, Pool = SkiPassPlanRules.AreaFlagPool (the area was on the default plan): written as
        // SkiSeasonPassArea with the standing.
        private struct PassWrite { public Entity Household; public int SeasonKey; public int PricePaid; public byte Flags; public byte CannotPay; public Entity Sign; public byte Pool; }

        /// <summary>The Options default lift ticket (clamped, live): the ticket of every area without its own (T284, AreaPriceOf).</summary>
        internal int Price { get; private set; } = SkiMoneyRules.TicketPriceDefault;

        internal int LodgeSpend { get; private set; } = SkiMoneyRules.LodgeSpendDefault;

        /// <summary>The clamped SeasonPassShare read on the last tick (0 = passes off).</summary>
        internal int PassShare { get; private set; } = SkiMoneyRules.PassShareDefault;

        /// <summary>The clamped SeasonPassPrice read on the last tick (1.0.2: money, 20-2,000): the Options default early price (T284).</summary>
        internal int PassPriceSetting { get; private set; } = SkiMoneyRules.PassPriceDefault;

        /// <summary>The clamped LessonShare read on the last tick (0 = lessons off).</summary>
        internal int LessonShare { get; private set; } = SkiMoneyRules.LessonShareDefault;

        internal float LessonMultiplier { get; private set; } = SkiMoneyRules.LessonMultiplierDefault;

        /// <summary>Resort income booked as public transportation fees this load, as ApplyResults and ApplySale booked it (the counter's
        /// `Guests [month]` totals take month baselines of it).</summary>
        internal long FeeBookedThisLoad => m_FeeBookedTotal;

        private SimulationSystem m_SimulationSystem;
        private TimeSystem m_TimeSystem; // the midday re-arm and the [cost] clock
        private ClimateSystem m_ClimateSystem; // with m_PrefabSystem: the ski season's first month (SkiClimateCalendar)
        private PrefabSystem m_PrefabSystem;
        private CitySystem m_CitySystem;
        private ServiceFeeSystem m_ServiceFeeSystem;   // the fee queue (ResidentAISystem.cs:199 pattern)
        private SkiGuestCounterSystem m_Counter;
        private SkiResortAttractivenessSystem m_Attractiveness;
        private SkiPrefabCatalogSystem m_PrefabCatalog; // the lodge rows (LodgeDefs)
        private SkiLiftChargeSystem m_LiftCharge;
        private BufferLookup<Efficiency> m_EfficiencyLookup;
        private BufferLookup<GameResources> m_ResourcesLookup;
        private ComponentLookup<PlayerMoney> m_PlayerMoneyLookup;
        private ComponentLookup<SkiSeasonPass> m_PassLookup;   // read-only in the job, written on the main thread
        private ComponentLookup<SkiSeasonPassArea> m_PassAreaLookup;   // T284: the standing's area, read-only in the jobs

        // Job data (Persistent, the job owns them while in flight)
        private NativeList<PassWrite> m_PassWrites;   // filled by the job, applied in ApplyResults
        private NativeList<TicketRequest> m_Requests;
        private NativeList<TicketSlot> m_Slots;
        private NativeList<TicketResult> m_Results;
        private NativeList<byte> m_SlotGate;
        private JobHandle m_Handle;
        private bool m_JobInFlight;
        private uint m_JobFrame, m_JobPendingFrame;

        private readonly List<GuestArrival> m_Arrivals = new List<GuestArrival>(16);
        private readonly Dictionary<Entity, int> m_SlotOf = new Dictionary<Entity, int>();
        private readonly Dictionary<Entity, SkiMoneyRules.TicketGate> m_LastGate = new Dictionary<Entity, SkiMoneyRules.TicketGate>();
        private readonly HashSet<Entity> m_FirstPaid = new HashSet<Entity>();
        private readonly LogWindow m_GateLog = new LogWindow(Tuning.LogWindowS);
        private readonly LogWindow m_PassLog = new LogWindow(Tuning.LogWindowS);   // `Passes [sold]`
        private readonly LogWindow m_LessonLog = new LogWindow(Tuning.LogWindowS); // `Lessons [sold]`

        // The gate watch (engine notes "Gate watch"). All reset at load.
        private readonly List<Entity> m_WatchSigns = new List<Entity>(4);
        private readonly HashSet<Entity> m_WatchSeen = new HashSet<Entity>();
        private readonly List<Entity> m_WatchDrop = new List<Entity>(2);
        private readonly Dictionary<Entity, SkiMoneyRules.TicketGate> m_WatchGate = new Dictionary<Entity, SkiMoneyRules.TicketGate>();
        private bool m_WatchPrimed;
        private int m_WatchGeneration, m_WatchChargeRevision, m_WatchPrice;   // m_WatchPrice = PriceRevision (T284)
        private long m_GateMismatches;

        private bool m_Armed, m_Disabled, m_ErrorLogged, m_NoCityWarned, m_RwhWarned;
        private long m_BusyTicks, m_RequestsTotal, m_GuestsTotal, m_IncomeTotal;
        private long m_FeeBookedTotal;   // this load (DUMP, the counter's month line)
        private long m_SpendsTotal, m_SpendIncomeTotal; // this load (DUMP)
        // Passes, this load (DUMP, the Passes lines). Sold / income as ApplyResults booked them. Declined = rolled out.
        // CannotPay = rolled in without the money. HolderGuests = people who came in on a pass. SeasonKey = the last decided season.
        private long m_PassesSold, m_PassIncomeTotal, m_PassesDeclined, m_PassesCannotPay, m_PassHolderGuests;
        private int m_PassSeasonKey = int.MinValue;
        private bool m_PassFirstLogged;
        private long m_LessonsTotal, m_LessonIncomeTotal;
        private bool m_LessonFirstLogged;
        // 1.0.3 destination pass, this load (DUMP, `Passes [destination]`): people who paid the fee, its money, people who could not pay it.
        private long m_DestGuests, m_DestIncomeTotal, m_DestUnpaid;
        private bool m_DestFirstLogged;
        // T258, this load (DUMP, `Tickets [turned away] first`): people and parties of game-sent outings that walked away at the price.
        private long m_PriceTurnedTotal, m_PriceTurnedParties;
        private bool m_PriceTurnedLogged;
        private readonly LogWindow m_DestLog = new LogWindow(Tuning.LogWindowS);   // `Passes [destination]`
        private int m_LargestBatch, m_MaxLagTicks, m_LastLagTicks;
        private int m_CostTicks, m_CostLargestBatch;
        private double m_CostSumMs, m_CostMaxMs;
        private bool m_CostLogged;
        private bool m_CostSkipNext; // true on the arming tick only. RecordCost leaves that tick out
        private int m_CostArmDay = int.MinValue; // SkiDayRules.DayKey of the last midday re-arm or afternoon line
        private int m_CostMeasurements; // `Tickets [cost]` lines this load
        private bool m_CostByRearm; // the measurement in progress was armed at midday (not the load's)

        protected override void OnCreate()
        {
            base.OnCreate();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_TimeSystem = World.GetOrCreateSystemManaged<TimeSystem>();
            m_ClimateSystem = World.GetOrCreateSystemManaged<ClimateSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_ServiceFeeSystem = World.GetOrCreateSystemManaged<ServiceFeeSystem>();
            m_Counter = World.GetOrCreateSystemManaged<SkiGuestCounterSystem>();
            m_Attractiveness = World.GetOrCreateSystemManaged<SkiResortAttractivenessSystem>();
            // After the attractiveness system, which creates the catalog in its own OnCreate (creation order = load-hook order,
            // GameSystemBase.cs:27-29).
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_LiftCharge = World.GetOrCreateSystemManaged<SkiLiftChargeSystem>();
            m_EfficiencyLookup = GetBufferLookup<Efficiency>(isReadOnly: true);
            m_ResourcesLookup = GetBufferLookup<GameResources>(isReadOnly: false);
            m_PlayerMoneyLookup = GetComponentLookup<PlayerMoney>(isReadOnly: true);
            m_PassLookup = GetComponentLookup<SkiSeasonPass>(isReadOnly: true);
            m_PassAreaLookup = GetComponentLookup<SkiSeasonPassArea>(isReadOnly: true);
            m_PassWrites = new NativeList<PassWrite>(16, Allocator.Persistent);
            m_Requests = new NativeList<TicketRequest>(16, Allocator.Persistent);
            m_Slots = new NativeList<TicketSlot>(4, Allocator.Persistent);
            m_Results = new NativeList<TicketResult>(16, Allocator.Persistent);
            m_SlotGate = new NativeList<byte>(4, Allocator.Persistent);
            SaleOnCreate();   // the pre-season pass sale (SkiLiftTicketSystem.Presale.cs)
            // Job reflection data and JIT before the first Schedule (the PrewarmJit precedent).
            IJobExtensions.EarlyJobInit<TicketJob>();
            Mod.Log.Info($"{nameof(SkiLiftTicketSystem)} created");
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 16;
        }

        protected override void OnDestroy()
        {
            try
            {
                if (m_JobInFlight)
                    ApplyResults(quiet: true);
                m_Handle.Complete();
                if (m_GateLog.Count > 0)
                    WriteGateSummary(UnityEngine.Time.realtimeSinceStartup);
                if (m_PassLog.Count > 0)
                    WritePassSummary(UnityEngine.Time.realtimeSinceStartup);
                if (m_LessonLog.Count > 0)
                    WriteLessonSummary(UnityEngine.Time.realtimeSinceStartup);
            }
            catch (Exception)
            {
                // teardown: nothing left to report to
            }
            SaleOnDestroy();
            if (m_PassWrites.IsCreated) m_PassWrites.Dispose();
            if (m_Requests.IsCreated) m_Requests.Dispose();
            if (m_Slots.IsCreated) m_Slots.Dispose();
            if (m_Results.IsCreated) m_Results.Dispose();
            if (m_SlotGate.IsCreated) m_SlotGate.Dispose();
            base.OnDestroy();
        }

        /// <summary>Called by SkiGuestCounterSystem.OnGamePreload BEFORE it writes the `(until the load)` month lines. The counter's
        /// preload runs first (it subscribes first), so without this the line missed the last ticket batch. Books the job in flight,
        /// if any. This system's own OnGamePreload then finds nothing in flight. Never throws (one WARN instead).</summary>
        internal void BookInFlightForPreload()
        {
            try
            {
                if (m_JobInFlight)
                    ApplyResults(quiet: false);
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Tickets: the last ticket results could not be booked before the month line " +
                             $"({e.GetType().Name}: {e.Message}) — non-fatal");
            }
        }

        /// <summary>A load (or leaving to the menu): the job's results reach the ledger before it resets,
        /// then the gate fold is written. Never throws (GameSystemBase.cs:85-96).</summary>
        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            try
            {
                if (m_JobInFlight)
                    ApplyResults(quiet: false);
                if (m_SaleInFlight)
                    ApplySale(quiet: false);   // money already moved must reach the ledger and the households
                if (m_GateLog.Count > 0)
                    WriteGateSummary(UnityEngine.Time.realtimeSinceStartup);
                if (m_PassLog.Count > 0)
                    WritePassSummary(UnityEngine.Time.realtimeSinceStartup);
                if (m_LessonLog.Count > 0)
                    WriteLessonSummary(UnityEngine.Time.realtimeSinceStartup);
                if (m_DestLog.Count > 0)
                    WriteDestSummary(UnityEngine.Time.realtimeSinceStartup);
                FlushSaleDay();
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Tickets: the last ticket results or folded gate lines could not be written before the load " +
                             $"({e.GetType().Name}: {e.Message}) — non-fatal");
            }
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            m_Handle.Complete();
            m_JobInFlight = false;
            SaleOnLoad();
            PricesOnLoad();
            m_Requests.Clear();
            m_Slots.Clear();
            m_Results.Clear();
            m_SlotGate.Clear();
            m_PassWrites.Clear();
            m_PassLog.Reset();   // flushed in OnGamePreload. Nothing carries over
            m_LessonLog.Reset(); // flushed in OnGamePreload. Nothing carries over
            m_Arrivals.Clear();
            m_SlotOf.Clear();
            m_LastGate.Clear();
            m_FirstPaid.Clear();
            m_GateLog.Reset();   // flushed in OnGamePreload. Nothing carries over
            m_WatchSigns.Clear();
            m_WatchSeen.Clear();
            m_WatchDrop.Clear();
            m_WatchGate.Clear();
            m_WatchPrimed = false;
            m_WatchGeneration = m_WatchChargeRevision = m_WatchPrice = 0;
            m_GateMismatches = 0;
            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_NoCityWarned = false;
            m_RwhWarned = false;           // one WARN per load
            m_FeeBookedTotal = 0;
            m_BusyTicks = m_RequestsTotal = m_GuestsTotal = m_IncomeTotal = 0;
            m_SpendsTotal = m_SpendIncomeTotal = 0;
            m_PassesSold = m_PassIncomeTotal = m_PassesDeclined = m_PassesCannotPay = m_PassHolderGuests = 0;
            m_PassSeasonKey = int.MinValue;
            m_PassFirstLogged = false;
            m_LessonsTotal = m_LessonIncomeTotal = 0;
            m_LessonFirstLogged = false;
            m_DestGuests = m_DestIncomeTotal = m_DestUnpaid = 0;
            m_DestFirstLogged = false;
            m_PriceTurnedTotal = m_PriceTurnedParties = 0;
            m_PriceTurnedLogged = false;
            m_DestLog.Reset();   // flushed in OnGamePreload. Nothing carries over
            m_LargestBatch = m_MaxLagTicks = m_LastLagTicks = 0;
            m_CostTicks = m_CostLargestBatch = 0;
            m_CostSumMs = m_CostMaxMs = 0.0;
            m_CostLogged = false;
            m_CostSkipNext = false;
            m_CostArmDay = int.MinValue;
            m_CostMeasurements = 0;
            m_CostByRearm = false; // the load's measurement is not a midday one
            Price = ReadPrice();
            LodgeSpend = ReadLodgeSpend();
            PassShare = ReadPassShare();
            PassPriceSetting = ReadPassPrice();
            LessonShare = ReadLessonShare();
            LessonMultiplier = ReadLessonMultiplier();

            if (mode != GameMode.Game)
                return;

            m_Armed = true;
            Mod.Log.Info($"Tickets: ARMED — price {Price} (setting LiftTicketPrice, 0 = off; read live); each counted guest " +
                         "pays once on arrival, taken from the guest's household; " + BookingText +
                         "; a household pays for as many " +
                         "members as its money covers, never below 0; gate = Welcome Sign efficiency above 0 and a counted run " +
                         "(open or patchy; a running lift alone does not open it), not known while the ski area's first score " +
                         "after a load is held; one job per 16-frame tick with arrivals; " +
                         $"lodge spend {LodgeSpend} per paying guest at a lodge (setting LodgeSpendPerGuest, 0 = off; read live; " +
                         "× the lodge tier's multiplier, A-frame / hut 1, mid 1.5, luxury 3, × sqrt(the day ticket price / "+SkiMoneyRules.TicketPriceDefault+") (the setting is the spend at the default ticket); after the ticket and the lesson, open gate only, " +
                         "never below 0; booked like the ticket: a fee each); " + PassArmedText() + "; " +
                         LessonArmedText());
            WarnRwhOnce();
        }

        private string LessonArmedText()
        {
            return LessonShare > 0
                ? $"lessons: {LessonShare} % of the people counted at an open gate of a ski area with an ACTIVE ski school (installed, not " +
                  $"switched off) take one (setting LessonShare, 0 = off; read live; a stateless hash of household, day and person), price " +
                  $"{LessonMultiplier:0.0#} × the lift ticket now = {SkiMoneyRules.LessonPrice(LessonMultiplier, Price)} (setting LessonMultiplier; read live); " +
                  "taken from what the household has left after the ticket or pass (never below 0), booked like the ticket"
                : "lessons: off (setting LessonShare 0; read live)";
        }

        private string PassArmedText()
        {
            return PassShare > 0
                ? $"season passes: {PassShare} % of a resident household's first arrivals of the season buy one (setting SeasonPassShare, " +
                  $"0 = off; read live), PER AREA (1.0.4): each ski area's own day ticket and pass plan (the hub), else the Options defaults " +
                  $"(ticket {Price}, early price {PassPriceSetting}, +" + SkiPassRules.StepPct + " % from season month 3, on sale in season months 1-4); " +
                  "a season's plan locks when its sale starts (no cuts; an edit applies from the next season); the price is locked at the sale, sales × " +
                  $"the pass demand factor (full up to {SkiMoneyRules.PassFullAt} × the area's lift ticket, then fewer; in the winter judged against the " +
                  "share of the winter still ahead and × that share: SkiPassPlanRules.Demand); " +
                  "taken from the household (never below 0), booked like a ticket; the pass holder pays 0 at the gate for the season " +
                  "(July to June) at the area that sold it (a pass of an area on the default plan: at every area on the default plan; a 1.0.3 pass: " +
                  "everywhere) and gets the full price factor there in the ski-day chance; a household that does not buy pays lift tickets all season; " +
                  "SALE RUN: the area's months on sale (one unbroken run) at its price of the month; each resident household is in the pool of " +
                  "its nearest area within reach and has its own " +
                  "buy hour in the run (front-loaded, a stateless hash) and the hourly sale (once per ski area per game hour, nothing open " +
                  "needed: the area rated as if fully open) rolls it once against the share × its ski-day chance as a pass holder (Bought or " +
                  "Declined); first arrivals decide only households whose hour has come and that no sale pool held; DESTINATION PASS (1.0.3): " +
                  "tourist parties roll once a season (stateless) against share × pass demand × the price factor of the fee; one on a pass pays " +
                  "a quarter of the area's early pass price per person and day (never above its ticket) instead of the ticket, booked as pass income, and " +
                  "the ticket price does not deter it in the send"
                : "season passes: off (setting SeasonPassShare 0; read live); holders keep what they bought";
        }

        private const string BookingText =
            "booked as public transportation fees: one FeeEvent per paid lift ticket, pass, lesson and lodge spend like the vanilla bus fare " +
            "(ResidentAISystem.cs:3922-3935), credited to the city by the budget (BudgetApplySystem), shown in the " +
            "Economy panel, NOT added to the balance directly (that would pay twice)";

        /// <summary>One WARN per load when Realistic Workplaces And Households (assembly "RWH", by name only,
        /// SkiOtherMods) is loaded (income is always booked as fees). RWH replaces BudgetApplySystem with its own copy, so
        /// whether fee income reaches the treasury there is UNVERIFIED. The behavior does not depend on it.</summary>
        private void WarnRwhOnce()
        {
            if (m_RwhWarned || !SkiOtherMods.RealisticWorkplacesDetected)
                return;
            m_RwhWarned = true;
            Mod.Log.Warn("Tickets: Realistic Workplaces And Households (assembly RWH) is loaded and resort income is booked as " +
                         "public transportation fees: it replaces BudgetApplySystem with its own copy, so whether fee income " +
                         "reaches the treasury there is UNVERIFIED (warned once per load)");
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !m_Armed)
                    return;
                // 0a. A midday measurement not finished when the game day ended is written partial (inside the try, after the disarmed / disabled return).
                CostDayEnd();
                // 0. The cost measurement re-arms once per game day at midday. The arming tick is not measured (the flag lives for this tick only).
                m_CostSkipNext = RearmCostAtMidday();
                long t0 = Stopwatch.GetTimestamp();

                // 1. Last tick's job (done by now) → the ledger.
                if (m_JobInFlight)
                    ApplyResults(quiet: false);
                // 1b. Last tick's pre-season sale (done by now) → standings, ledger, `Passes [season sale]`.
                if (m_SaleInFlight)
                    ApplySale(quiet: false);

                // 2. The price, live.
                int price = ReadPrice();
                if (price != Price)
                {
                    Mod.Log.Info($"Tickets [price] lift ticket price {Price} → {price}{(price == 0 ? " (tickets off)" : "")}");
                    Price = price;
                }
                // 2a. The lodge-spend setting, live.
                int spend = ReadLodgeSpend();
                if (spend != LodgeSpend)
                {
                    Mod.Log.Info($"Tickets [lodge spend] per paying guest at a lodge {LodgeSpend} → {spend}{(spend == 0 ? " (off)" : "")} " +
                                 "(at the default ticket, × the lodge tier's multiplier, × sqrt(the day ticket price / "+SkiMoneyRules.TicketPriceDefault+"); charged after the ticket while the gate is open, never below 0)");
                    LodgeSpend = spend;
                }
                // 2a''. The two pass settings, live (take effect with the next job scheduled).
                int passShare = ReadPassShare();
                int passPrice = ReadPassPrice();
                if (passShare != PassShare || passPrice != PassPriceSetting)
                {
                    Mod.Log.Info($"Passes [settings] share {PassShare} → {passShare} %, price {PassPriceSetting} → {passPrice}; " +
                                 (passShare == 0 ? "passes off (holders keep what they bought)" : "the pre-season sale (from the season's second month) and first arrivals use the new values"));
                    PassShare = passShare;
                    PassPriceSetting = passPrice;
                }
                // 2a'''. The two lesson settings, live (take effect with the next job scheduled).
                int lessonShare = ReadLessonShare();
                float lessonMult = ReadLessonMultiplier();
                if (lessonShare != LessonShare || lessonMult != LessonMultiplier)
                {
                    LessonShare = lessonShare;
                    LessonMultiplier = lessonMult;
                    Mod.Log.Info("Lessons [settings] " + LessonArmedText());
                }
                if (m_GateLog.Count > 0)
                {
                    float now = UnityEngine.Time.realtimeSinceStartup;
                    if (m_GateLog.TrailingDue(now))
                        WriteGateSummary(now);
                }
                if (m_LessonLog.Count > 0)
                {
                    float now = UnityEngine.Time.realtimeSinceStartup;
                    if (m_LessonLog.TrailingDue(now))
                        WriteLessonSummary(now);
                }
                if (m_PassLog.Count > 0)
                {
                    float now = UnityEngine.Time.realtimeSinceStartup;
                    if (m_PassLog.TrailingDue(now))
                        WritePassSummary(now);
                }
                if (m_DestLog.Count > 0)
                {
                    float now = UnityEngine.Time.realtimeSinceStartup;
                    if (m_DestLog.TrailingDue(now))
                        WriteDestSummary(now);
                }

                // 2a''''. T284: every area's prices (own or the Options defaults), the season lock, the cache the jobs and the hub read.
                RefreshAreaPrices();

                // 2b. The gate watch (engine notes "Gate watch"), whether guests arrive or not.
                WatchGates();

                // 2c. The pre-season pass sale of the first ski area whose gate is Open in a new season. A tick that
                // starts one does not drain arrivals (they stay pending in the counter). A ticket job scheduled in the same window would
                // not see the standings the sale writes next tick. It could sell the same household a pass again.
                if (TryStartSale())
                    return;

                // 3. Drain the counter.
                m_Arrivals.Clear();
                if (m_Counter.DrainArrivals(m_Arrivals) == 0)
                    return;
                m_JobPendingFrame = m_Counter.LastPendingFrame;

                // 4. One slot per area with its own prices (T284), then the job. An area with tickets off books its arrivals free in the job
                // (TicketGateOf(0) = Off per slot).
                BuildSlots(spend);
                ScheduleJob();

                RecordCost((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency, m_Arrivals.Count);
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
                // m_JobInFlight is NOT cleared here. The job above completed and may already have moved household money. Leaving the flag set lets
                // the preload path (OnGamePreload / OnDestroy) book that completed job into the ledger and write the batch's passes. A cleared flag
                // would drop both.
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiLiftTicketSystem)} EXCEPTION — lift tickets disabled until the next game " +
                                  $"load. Please report this stack:\n{e}");
                }
            }
        }

        private static int ReadPrice()
        {
            return SkiMoneyRules.ClampTicketPrice(Mod.Settings?.LiftTicketPrice ?? SkiMoneyRules.TicketPriceDefault);
        }

        private static int ReadLodgeSpend()
        {
            return SkiMoneyRules.ClampLodgeSpend(Mod.Settings?.LodgeSpendPerGuest ?? SkiMoneyRules.LodgeSpendDefault);
        }

        private static int ReadPassShare()
        {
            return SkiMoneyRules.ClampPassShare(Mod.Settings?.SeasonPassShare ?? SkiMoneyRules.PassShareDefault);
        }

        private static int ReadPassPrice()
        {
            return SkiMoneyRules.ClampPassPrice(Mod.Settings?.SeasonPassPrice ?? SkiMoneyRules.PassPriceDefault);
        }

        private static int ReadLessonShare()
        {
            return SkiMoneyRules.ClampLessonShare(Mod.Settings?.LessonShare ?? SkiMoneyRules.LessonShareDefault);
        }

        private static float ReadLessonMultiplier()
        {
            return SkiMoneyRules.ClampLessonMultiplier(Mod.Settings?.LessonMultiplier ?? SkiMoneyRules.LessonMultiplierDefault);
        }


        /// <summary>One slot per distinct sign of this tick's arrivals (gate inputs, the area's own prices: T284) and one request per arrival. With a price and a
        /// lodge-spend setting above 0, a request whose building is a lodge carries SpendPerPerson (the catalog's LodgeDefs view, fetched
        /// once). With either setting at 0 nothing is read.</summary>
        private void BuildSlots(int spendSetting)
        {
            m_SlotOf.Clear();
            m_Slots.Clear();
            m_Requests.Clear();
            IReadOnlyDictionary<Entity, LodgeDef> lodgeDefs = spendSetting > 0 ? m_PrefabCatalog.LodgeDefs : null;
            for (int i = 0; i < m_Arrivals.Count; i++)
            {
                GuestArrival a = m_Arrivals[i];
                if (!m_SlotOf.TryGetValue(a.Sign, out int slot))
                {
                    slot = m_Slots.Length;
                    m_SlotOf[a.Sign] = slot;
                    // known = a score present AND not in the first-write hold.
                    bool has = m_Attractiveness.TryGetAreaScore(a.Sign, out SkiResortAttractivenessSystem.AreaScoreView view);
                    bool known = has && !view.WriteHeld;
                    AreaPrice ap = AreaPriceOf(a.Sign);
                    m_Slots.Add(new TicketSlot
                    {
                        Sign = a.Sign,
                        RunStateKnown = (byte)(known ? 1 : 0),
                        OpenRuns = known ? view.Result.RunsCounted : 0,
                        // The area's ski school is installed and not Inactive (the attraction snapshot's slot.SchoolActive, echoed in
                        // Result.SchoolActive). It is only meaningful once the score is known.
                        SchoolActive = (byte)(known && view.Result.SchoolActive ? 1 : 0),
                        DestShare = DestShareAt(a.Sign, ap),
                        DestFee = ap.DestFee,
                        PriceTolerance = m_SkiDay != null ? m_SkiDay.PriceToleranceOf(a.Sign) : 0,
                        Ticket = ap.R.Ticket,
                        LessonPrice = ap.LessonPrice,
                        SalePrice = ap.SalePrice,
                        PassDemand = ap.Demand,
                        // a run of an earlier season says nothing about this one: no first-arrival sale until this season's first hourly run
                        AppliedPos = AppliedSalePos(a.Sign, ap.Season),
                        OnDefault = ap.R.OnDefault,
                    });
                }
                int price = m_Slots[slot].Ticket;
                int spendPerPerson = 0;
                if (lodgeDefs != null && price > 0 && a.Building != Entity.Null && a.Building != a.Sign &&
                    EntityManager.Exists(a.Building) && EntityManager.HasComponent<Game.Prefabs.PrefabRef>(a.Building) &&
                    lodgeDefs.TryGetValue(EntityManager.GetComponentData<Game.Prefabs.PrefabRef>(a.Building).m_Prefab, out LodgeDef lodge))
                    spendPerPerson = SkiMoneyRules.LodgeSpendOf(spendSetting, lodge.SpendMultiplier, price);
                m_Requests.Add(new TicketRequest
                {
                    Slot = slot,
                    Household = a.Household,
                    People = a.People,
                    Tourist = (byte)(a.Tourist ? 1 : 0),
                    Outing = (byte)(a.Tourist && !a.OnLeisure ? 1 : 0),
                    SpendPerPerson = spendPerPerson,
                });
            }
        }

        private void ScheduleJob()
        {
            int requests = m_Requests.Length, slots = m_Slots.Length;
            m_Results.ResizeUninitialized(requests);
            m_SlotGate.ResizeUninitialized(slots);
            m_EfficiencyLookup.Update(this);
            m_ResourcesLookup.Update(this);
            m_PlayerMoneyLookup.Update(this);
            m_PassLookup.Update(this);
            m_PassAreaLookup.Update(this);
            m_PassWrites.Clear();   // the previous job's rows were applied (or dropped) in ApplyResults
            // The fee queue and its dependency, as ResidentAISystem.cs:265 / :277 / :285 (ServiceFeeSystem.cs:443-452).
            NativeQueue<ServiceFeeSystem.FeeEvent> feeQueue = m_ServiceFeeSystem.GetFeeQueue(out JobHandle feeDeps);
            int seasonKey = SkiMoneyRules.SeasonKeyOfDate(m_TimeSystem.year, m_TimeSystem.normalizedDate, SkiClimateCalendar.SeasonFirstMonth(m_ClimateSystem, m_PrefabSystem));
            var job = new TicketJob
            {
                Requests = m_Requests.AsArray(),
                Slots = m_Slots.AsArray(),
                Efficiencies = m_EfficiencyLookup,
                HouseholdResources = m_ResourcesLookup,
                PlayerMoneys = m_PlayerMoneyLookup,
                Passes = m_PassLookup,
                PassAreas = m_PassAreaLookup,
                PassWrites = m_PassWrites,
                SeasonKey = seasonKey,
                PassShare = PassShare,
                LessonShare = LessonShare,
                LessonDay = SkiDayRules.DayKey(m_TimeSystem.year, m_TimeSystem.normalizedDate, m_TimeSystem.daysPerYear),
                FeeQueue = feeQueue,
                City = m_CitySystem.City,
                Results = m_Results.AsArray(),
                SlotGate = m_SlotGate.AsArray(),
            };
            JobHandle handle = job.Schedule(JobHandle.CombineDependencies(Dependency, feeDeps));
            m_ServiceFeeSystem.AddQueueWriter(handle);
            Dependency = handle;
            m_Handle = handle;
            m_JobInFlight = true;
            m_JobFrame = m_SimulationSystem.frameIndex;
        }

        /// <summary>Step 1: complete the job and book every request's result (ledger, the locals count). Then book the charged gate per slot
        /// (recorded, not logged) and the first paid ticket per ski area. <paramref name="quiet"/> (teardown) skips the lines that read ECS data.</summary>
        private void ApplyResults(bool quiet)
        {
            m_Handle.Complete();
            m_JobInFlight = false;
            SkiResortLedger ledger = m_Counter.Ledger;
            bool noCity = false;
            int passSold = 0;       // this batch
            long passIncome = 0;
            int lessons = 0;
            long lessonIncome = 0;
            Entity firstLessonSign = Entity.Null;
            int firstLessonPeople = 0, firstLessonCount = 0;
            List<int> destBatch = m_DestBatch;
            destBatch.Clear();
            for (int i = 0; i < m_Requests.Length; i++)
            {
                TicketRequest q = m_Requests[i];
                TicketResult r = m_Results[i];
                Entity sign = m_Slots[q.Slot].Sign;
                ledger.AddTickets(Ref(sign), r.Paid, r.Unpaid, r.Free, r.Income);
                ledger.AddLodgeSpend(Ref(sign), r.Spends, r.SpendIncome); // all-zero = no change
                ledger.AddPasses(Ref(sign), r.PassSold, r.PassIncome, r.PassHolders, r.PassSold > 0 ? r.PassSeason : SkiResortLedger.NoSeason);   // all-zero = no change
                ledger.AddLessons(Ref(sign), r.Lessons, r.LessonIncome);   // all-zero = no change
                // 1.0.3 destination pass: the fee is pass income of the month and its payers are guests on a pass. No season-sale fields (no
                // NoSeason): the season's destination money = the season's pass income minus PassSeasonIncome.
                ledger.AddPasses(Ref(sign), 0, r.DestIncome, r.DestPaid);   // all-zero = no change
                if (r.DestPaid > 0 || r.DestUnpaid > 0)
                    destBatch.Add(i);
                if (r.PriceTurned > 0)
                {
                    ledger.AddPriceTurned(Ref(sign), r.PriceTurned);   // T258
                    m_PriceTurnedTotal += r.PriceTurned;
                    m_PriceTurnedParties++;
                    if (!quiet && !m_PriceTurnedLogged)
                    {
                        m_PriceTurnedLogged = true;
                        int tol = m_Slots[q.Slot].PriceTolerance;
                        int areaTicket = m_Slots[q.Slot].Ticket;
                        Mod.Log.Info($"Tickets [turned away] first: ski area {Fmt(sign)}: a tourist party of {q.People} walked away at the lift ticket {areaTicket} " +
                                     $"(party price factor {SkiDayRules.PartyPriceFactor(areaTicket, tol):0.00}: 1 up to {SkiDayRules.PartyPriceFullAt - SkiDayRules.PriceSpan + tol}, " +
                                     "a stateless roll per party and day); no ticket, lesson or lodge spend");
                    }
                }
                if (r.Lessons > 0 && firstLessonSign == Entity.Null)
                {
                    firstLessonSign = sign;
                    firstLessonPeople = q.People;
                    firstLessonCount = r.Lessons;
                }
                lessons += r.Lessons;
                lessonIncome += r.LessonIncome;
                passSold += r.PassSold;
                passIncome += r.PassIncome;
                m_PassHolderGuests += r.PassHolders;
                if (q.Tourist == 0)
                    m_Counter.AddLocalsPaid(sign, r.Paid);
                m_GuestsTotal += Math.Max(0, q.People);
                m_IncomeTotal += r.Income;
                m_FeeBookedTotal += r.Booked;
                m_SpendsTotal += r.Spends;
                m_SpendIncomeTotal += r.SpendIncome;
                noCity |= r.NoCity != 0;
                if (!quiet && r.Paid > 0 && m_FirstPaid.Add(sign))
                    FirstTicketLine(sign, q.People, r.Paid, r.Income, r.Spends, r.SpendIncome);
            }
            m_RequestsTotal += m_Requests.Length;
            if (m_Requests.Length > m_LargestBatch)
                m_LargestBatch = m_Requests.Length;
            ApplyPassWrites(quiet, passSold, passIncome);   // the structural part, main thread
            ApplyDest(quiet, destBatch);
            ApplyLessons(quiet, lessons, lessonIncome, firstLessonSign, firstLessonPeople, firstLessonCount);
            uint frame = m_SimulationSystem.frameIndex;
            m_LastLagTicks = (int)((frame - m_JobPendingFrame) / 16u);
            if (m_LastLagTicks > m_MaxLagTicks)
                m_MaxLagTicks = m_LastLagTicks;
            if (!quiet)
            {
                for (int s = 0; s < m_Slots.Length; s++)
                    RecordChargedGate(m_Slots[s], (SkiMoneyRules.TicketGate)m_SlotGate[s]);
                if (noCity && !m_NoCityWarned)
                {
                    m_NoCityWarned = true;
                    Mod.Log.Warn("Tickets: City entity has no PlayerMoney — guests are admitted free and no household is " +
                                 "charged (warned once per load)");
                }
            }
            m_Requests.Clear();
            m_Slots.Clear();
        }

        /// <summary>Step 1, main thread: the job's PassWrite rows become SkiSeasonPass components (added, or overwritten when the
        /// household holds one of an older season). The system counts the batch's totals and writes `Passes [first]` / `Passes [sold]`.
        /// <paramref name="quiet"/> (teardown) only counts. A household that no longer exists is skipped.</summary>
        private void ApplyPassWrites(bool quiet, int sold, long income)
        {
            int declined = 0, cannotPay = 0;
            Entity firstBuyer = Entity.Null;
            int firstPrice = 0, season = m_PassSeasonKey;
            Entity firstSign = Entity.Null;
            for (int i = 0; i < m_PassWrites.Length; i++)
            {
                PassWrite w = m_PassWrites[i];
                if (w.Flags == 0)
                {
                    if (firstBuyer == Entity.Null)
                    {
                        firstBuyer = w.Household;
                        firstPrice = w.PricePaid;
                        firstSign = w.Sign;
                    }
                }
                else if (w.CannotPay != 0)
                    cannotPay++;
                else
                    declined++;
                season = w.SeasonKey;
                if (quiet || !EntityManager.Exists(w.Household))
                    continue;
                WriteStanding(w);
            }
            m_PassWrites.Clear();
            m_PassesSold += sold;
            m_PassIncomeTotal += income;
            m_PassesDeclined += declined;
            m_PassesCannotPay += cannotPay;
            m_PassSeasonKey = season;
            if (quiet || (sold == 0 && declined == 0 && cannotPay == 0))
                return;
            if (sold > 0 && !m_PassFirstLogged)
            {
                m_PassFirstLogged = true;
                Entity city = m_CitySystem.City;
                string balance = city != Entity.Null && EntityManager.HasComponent<PlayerMoney>(city)
                    ? EntityManager.GetComponentData<PlayerMoney>(city).money.ToString()
                    : "n/a";
                AreaPrice ap = AreaPriceOf(firstSign);
                Mod.Log.Info($"Passes [first] resident household {Fmt(firstBuyer)} bought the season-{season} pass of ski area {Fmt(firstSign)} for {firstPrice} " +
                             $"(demand x{ap.Demand:0.###} at the lift ticket {ap.R.Ticket}, value left {ap.ValueLeft:P0}; share {PassShare} %): it pays 0 at the gate " +
                             (ap.R.OnDefault != 0 ? "at every area on the default plan" : "at that area") + " until the season ends and skis " +
                             $"with the full price factor there; booked as a public transportation fee (balance {balance})");
            }
            string body = $"{sold} pass(es) sold +{income} (first-arrival sales at each area's own price), {declined} household(s) rolled out, " +
                          $"{cannotPay} could not afford the pass; season {season}";
            float now = UnityEngine.Time.realtimeSinceStartup;
            switch (m_PassLog.Offer(now, false, body, sold, income, declined + cannotPay))
            {
                case LogWindow.Verdict.Plain:
                    Mod.Log.Info("Passes [sold] " + body);
                    break;
                case LogWindow.Verdict.Summary:
                    WritePassSummary(now);
                    break;
            }
        }

        /// <summary>Step 1, main thread: counts the batch's lessons and writes `Lessons [first]` (once per load) and
        /// `Lessons [sold]` (folded like `Passes [sold]`). <paramref name="quiet"/> (teardown) only counts.</summary>
        private void ApplyLessons(bool quiet, int lessons, long income, Entity firstSign, int firstPeople, int firstCount)
        {
            if (lessons <= 0 && income <= 0)
                return;
            m_LessonsTotal += lessons;
            m_LessonIncomeTotal += income;
            if (quiet)
                return;
            AreaPrice lap = AreaPriceOf(firstSign);
            int unit = lap.LessonPrice;
            if (!m_LessonFirstLogged && firstSign != Entity.Null)
            {
                m_LessonFirstLogged = true;
                Mod.Log.Info($"Lessons [first] ski area {Fmt(firstSign)}: a party of {firstPeople} took {firstCount} lesson(s) at the ski school " +
                             $"for {unit} each ({LessonMultiplier:0.0#} × the area's lift ticket {lap.R.Ticket}; share {LessonShare} %), taken after the ticket or pass " +
                             "and booked as a public transportation fee each");
            }
            string body = $"{lessons} lesson(s) +{income} ({LessonMultiplier:0.0#} × each area's lift ticket; first area {unit} each; share {LessonShare} %)";
            float now = UnityEngine.Time.realtimeSinceStartup;
            switch (m_LessonLog.Offer(now, false, body, lessons, income))
            {
                case LogWindow.Verdict.Plain:
                    Mod.Log.Info("Lessons [sold] " + body);
                    break;
                case LogWindow.Verdict.Summary:
                    WriteLessonSummary(now);
                    break;
            }
        }

        private readonly List<int> m_DestBatch = new List<int>(8);

        /// <summary>Step 1, main thread: the batch's destination-pass totals, `Passes [destination] first` once per load, then folded lines.</summary>
        private void ApplyDest(bool quiet, List<int> batch)
        {
            if (batch.Count == 0)
                return;
            int paid = 0, unpaid = 0;
            long income = 0;
            int fee = 0, destTicket = 0;
            Entity firstSign = Entity.Null;
            double share = 0.0;
            for (int k = 0; k < batch.Count; k++)
            {
                int i = batch[k];
                TicketResult r = m_Results[i];
                TicketSlot slot = m_Slots[m_Requests[i].Slot];
                paid += r.DestPaid;
                income += r.DestIncome;
                unpaid += r.DestUnpaid;
                if (firstSign == Entity.Null)
                {
                    firstSign = slot.Sign;
                    fee = slot.DestFee;
                    share = slot.DestShare;
                    destTicket = slot.Ticket;
                }
            }
            m_DestGuests += paid;
            m_DestIncomeTotal += income;
            m_DestUnpaid += unpaid;
            if (quiet)
                return;
            string shareText = share.ToString("P0", CultureInfo.InvariantCulture);
            if (!m_DestFirstLogged && paid > 0)
            {
                m_DestFirstLogged = true;
                Mod.Log.Info($"Passes [destination] first: ski area {Fmt(firstSign)}: {paid} tourist(s) on a destination pass paid the fee {fee} instead of the " +
                             $"lift ticket {destTicket} (a quarter of the area's early pass price, never above the ticket; {shareText} of the tourist parties " +
                             "are on a pass this season, a stateless roll per party); booked as pass income, a public transportation fee each");
            }
            string body = $"{paid} tourist(s) on a destination pass paid +{income} (fee {fee}, lift ticket {destTicket}, share {shareText}), {unpaid} could not pay";
            float now = UnityEngine.Time.realtimeSinceStartup;
            switch (m_DestLog.Offer(now, false, body, paid, income, unpaid))
            {
                case LogWindow.Verdict.Plain:
                    Mod.Log.Info("Passes [destination] " + body);
                    break;
                case LogWindow.Verdict.Summary:
                    WriteDestSummary(now);
                    break;
            }
        }

        private void WriteDestSummary(float now)
        {
            Mod.Log.Info($"Passes [destination] {m_DestLog.Count} more batch(es) in {m_DestLog.SpanS(now):F0} s, folded " +
                         $"(one line per LogWindowS={Tuning.LogWindowS:0} s): {m_DestLog.Sum(0):F0} tourist(s) on a pass paid +{m_DestLog.Sum(1):F0}, " +
                         $"{m_DestLog.Sum(2):F0} could not pay — last: {m_DestLog.LastText}");
            m_DestLog.Wrote(now);
        }

        /// <summary>1.0.3: the share of tourist parties on a destination pass at this sign (SkiPassRules.DestShare, the area's tolerance from the
        /// ski-day system prices the fee). 0 with passes off or tickets off.</summary>
        private double DestShareAt(Entity sign, in AreaPrice ap)
        {
            int fee = ap.DestFee;
            if (fee <= 0 || PassShare <= 0)
                return 0.0;
            int tolerance = m_SkiDay != null ? m_SkiDay.PriceToleranceOf(sign) : 0;
            return SkiPassRules.DestShare(PassShare, ap.R.Plan.Early, ap.R.Ticket, SkiDayRules.PriceFactor(fee, tolerance));
        }

        /// <summary>T284: one standing as components on the main thread: SkiSeasonPass and SkiSeasonPassArea, added in one archetype move
        /// when the household has neither (a 1.0.3 household may hold SkiSeasonPass alone: the area is added on its own).</summary>
        private void WriteStanding(in PassWrite w)
        {
            bool hasPass = EntityManager.HasComponent<SkiSeasonPass>(w.Household);
            bool hasArea = EntityManager.HasComponent<SkiSeasonPassArea>(w.Household);
            if (!hasPass && !hasArea)
                EntityManager.AddComponent(w.Household, new ComponentTypeSet(ComponentType.ReadWrite<SkiSeasonPass>(), ComponentType.ReadWrite<SkiSeasonPassArea>()));
            else if (!hasPass)
                EntityManager.AddComponent<SkiSeasonPass>(w.Household);
            else if (!hasArea)
                EntityManager.AddComponent<SkiSeasonPassArea>(w.Household);
            EntityManager.SetComponentData(w.Household, new SkiSeasonPass(w.SeasonKey, w.PricePaid, w.Flags));
            EntityManager.SetComponentData(w.Household, new SkiSeasonPassArea(w.Sign, w.Pool));
        }

        private void WriteLessonSummary(float now)
        {
            Mod.Log.Info($"Lessons [sold] {m_LessonLog.Count} more batch(es) in {m_LessonLog.SpanS(now):F0} s, folded " +
                         $"(one line per LogWindowS={Tuning.LogWindowS:0} s): {m_LessonLog.Sum(0):F0} lesson(s) +{m_LessonLog.Sum(1):F0} — last: {m_LessonLog.LastText}");
            m_LessonLog.Wrote(now);
        }

        private void WritePassSummary(float now)
        {
            Mod.Log.Info($"Passes [sold] {m_PassLog.Count} more batch(es) in {m_PassLog.SpanS(now):F0} s, folded " +
                         $"(one line per LogWindowS={Tuning.LogWindowS:0} s): {m_PassLog.Sum(0):F0} pass(es) sold +{m_PassLog.Sum(1):F0}, " +
                         $"{m_PassLog.Sum(2):F0} household(s) not buying — last: {m_PassLog.LastText}");
            m_PassLog.Wrote(now);
        }


        /// <summary>The gate the job charged under (silent). A charged gate that differs from the watch's gate for that sign is counted
        /// (DUMP). The job reads the sign's Efficiency buffer live. The watch reads the last recompute's efficiency. So a short disagreement around
        /// an efficiency change is expected. A result for a sign the primed watch no longer sees (it left between the schedule and the
        /// booking) is neither recorded nor counted. So it does not re-add the sign.</summary>
        private void RecordChargedGate(TicketSlot slot, SkiMoneyRules.TicketGate gate)
        {
            if (m_WatchPrimed && !m_WatchSeen.Contains(slot.Sign))
                return;
            m_LastGate[slot.Sign] = gate;
            if (m_WatchGate.TryGetValue(slot.Sign, out SkiMoneyRules.TicketGate watched) && watched != gate)
                m_GateMismatches++;
        }

        /// <summary>When the attraction Generation, the lift charge Revision or the price moved since the last evaluation: the gate of every
        /// live sign with the hub's inputs. One line per sign whose gate changed (a new sign's first gate too). Signs that left are
        /// dropped without a line. Main thread, plain reads of the two snapshots.</summary>
        private void WatchGates()
        {
            int generation = m_Attractiveness.Generation;
            int revision = m_LiftCharge.Revision;
            int priceRevision = PriceRevision;
            if (m_WatchPrimed && generation == m_WatchGeneration && revision == m_WatchChargeRevision && priceRevision == m_WatchPrice)
                return;
            m_WatchPrimed = true;
            m_WatchGeneration = generation;
            m_WatchChargeRevision = revision;
            m_WatchPrice = priceRevision;

            m_Attractiveness.CopySigns(m_WatchSigns);
            m_WatchSeen.Clear();
            for (int i = 0; i < m_WatchSigns.Count; i++)
            {
                Entity sign = m_WatchSigns[i];
                m_WatchSeen.Add(sign);
                // known = a score present AND not in the first-write hold. The efficiency is the result's while a score is present (held or not),
                // 1 without one.
                bool has = m_Attractiveness.TryGetAreaScore(sign, out SkiResortAttractivenessSystem.AreaScoreView view);
                bool known = has && !view.WriteHeld;
                float efficiency = has ? (float)view.Result.Efficiency : 1f;
                int runs = known ? view.Result.RunsCounted : 0;
                // The lift-charge operating count is printed in the line's body only (no longer a gate input).
                int operating = m_LiftCharge.TryGetResort(sign, out SkiLiftChargeSystem.ResortLiftCharge rc) ? rc.Operating : 0;
                // = the hub's rule (SkiResortStatsSystem, the `live.TicketGate` line), at the area's own ticket (T284)
                SkiMoneyRules.TicketGate gate = SkiMoneyRules.TicketGateOf(AreaPriceOf(sign).R.Ticket, efficiency, known, runs);
                if (m_WatchGate.TryGetValue(sign, out SkiMoneyRules.TicketGate old) && old == gate)
                    continue;   // a new sign falls through: its first line
                m_WatchGate[sign] = gate;
                GateLine(sign, gate, efficiency, known, runs, operating);
            }
            // The charged gates of signs that left go too (no allocation: m_WatchDrop is reused, the DUMP lists live signs only). No Count
            // precondition (unlike the m_WatchGate prune below). A sign that left in the same evaluation a new, not yet charged sign appeared
            // would keep m_LastGate.Count <= m_WatchSeen.Count and stay listed. The walk is over a handful of signs, only on the ticks the
            // watch re-evaluates.
            if (m_LastGate.Count > 0)
            {
                m_WatchDrop.Clear();
                foreach (var pair in m_LastGate)
                {
                    if (!m_WatchSeen.Contains(pair.Key))
                        m_WatchDrop.Add(pair.Key);
                }
                for (int i = 0; i < m_WatchDrop.Count; i++)
                    m_LastGate.Remove(m_WatchDrop[i]);
                m_WatchDrop.Clear();
            }
            if (m_WatchGate.Count > m_WatchSeen.Count)
            {
                m_WatchDrop.Clear();
                foreach (var pair in m_WatchGate)
                {
                    if (!m_WatchSeen.Contains(pair.Key))
                        m_WatchDrop.Add(pair.Key);
                }
                for (int i = 0; i < m_WatchDrop.Count; i++)
                    m_WatchGate.Remove(m_WatchDrop[i]);
                m_WatchDrop.Clear();
            }
        }

        private void GateLine(Entity sign, SkiMoneyRules.TicketGate gate, float efficiency, bool runStateKnown, int openRuns,
                              int operatingLifts)
        {
            string state;
            switch (gate)
            {
                case SkiMoneyRules.TicketGate.Open: state = "open"; break;
                case SkiMoneyRules.TicketGate.Off: state = "closed — tickets off"; break;
                case SkiMoneyRules.TicketGate.NoEfficiency: state = "closed — Welcome Sign efficiency 0 %"; break;
                // The gate does not open on a running lift.
                case SkiMoneyRules.TicketGate.NothingOpen: state = "closed — no counted run (a running lift alone does not open it)"; break;
                default: state = "closed — run state not known yet (no score yet, or the first score after the load is held)"; break;
            }
            string eff = float.IsNaN(efficiency) ? "n/a" : efficiency.ToString("P0", CultureInfo.InvariantCulture);
            string runs = runStateKnown ? openRuns.ToString() : "?";
            string body = $"ski area {Fmt(sign)}: {state} (efficiency {eff}, open runs {runs}, operating lifts {operatingLifts})";
            float now = UnityEngine.Time.realtimeSinceStartup;
            switch (m_GateLog.Offer(now, false, body))
            {
                case LogWindow.Verdict.Plain:
                    Mod.Log.Info("Tickets [gate] " + body);
                    break;
                case LogWindow.Verdict.Summary:
                    WriteGateSummary(now);
                    break;
            }
        }

        private void WriteGateSummary(float now)
        {
            Mod.Log.Info($"Tickets [gate] {m_GateLog.Count} more gate change(s) in {m_GateLog.SpanS(now):F0} s, folded " +
                         $"(one line per LogWindowS={Tuning.LogWindowS:0} s) — last: {m_GateLog.LastText}");
            m_GateLog.Wrote(now);
        }

        /// <summary>The first paid ticket per ski area per load (names the batch's lodge spend when there was one).</summary>
        private void FirstTicketLine(Entity sign, int people, int paid, int income, int spends, int spendIncome)
        {
            Entity city = m_CitySystem.City;
            string balance = city != Entity.Null && EntityManager.HasComponent<PlayerMoney>(city)
                ? EntityManager.GetComponentData<PlayerMoney>(city).money.ToString()
                : "n/a";
            string spend = spends > 0 ? $", lodge spend {spends} +{spendIncome}" : "";
            Mod.Log.Info($"Tickets [first] ski area {Fmt(sign)}: {people} guest(s), {paid} paid +{income}{spend} (balance {balance})");
        }

        /// <summary>Once the last `Tickets [cost]` line was written, the first tick at or after 12:00 of a new game day (SkiDayRules.DayKey)
        /// starts a new measurement. Returns true on the arming tick, which RecordCost leaves out.</summary>
        private bool RearmCostAtMidday()
        {
            // The midday re-arm is measurement scaffolding behind Options > Diagnostics (off by default). The once-per-load measurement stays.
            if (!SkiResortMod.Settings.SkiResortSettings.DiagnosticsOn)
                return false;
            if (!m_CostLogged || !(m_TimeSystem.normalizedTime >= 0.5f))
                return false;
            int day = SkiDayRules.DayKey(m_TimeSystem.year, m_TimeSystem.normalizedDate, m_TimeSystem.daysPerYear);
            if (day == m_CostArmDay)
                return false;
            m_CostArmDay = day;
            m_CostTicks = m_CostLargestBatch = 0;
            m_CostSumMs = m_CostMaxMs = 0.0;
            m_CostLogged = false;
            m_CostByRearm = true; // this measurement may end partial at the day change (CostDayEnd)
            return true;
        }

        /// <summary>A midday measurement still open when the game day changes is written partial. So the noon line is never silent: it carries
        /// the ticks it has ("the game day ended before 64"), or "skipped: no tick with arrivals since noon". m_CostArmDay stays the re-arm
        /// day, so the next noon re-arms. The load's measurement (m_CostByRearm false) keeps the plain rule.</summary>
        private void CostDayEnd()
        {
            if (m_CostLogged || !m_CostByRearm)
                return;   // cost: the DayKey below only while a midday measurement is armed and unlogged
            int day = SkiDayRules.DayKey(m_TimeSystem.year, m_TimeSystem.normalizedDate, m_TimeSystem.daysPerYear);
            if (!SkiDayRules.CostDayEnded(m_CostLogged, m_CostByRearm, m_CostArmDay, day))
                return;
            m_CostLogged = true;
            m_CostMeasurements++;
            if (m_CostTicks > 0)
                Mod.Log.Info($"Tickets [cost] main thread per tick with arrivals: mean {m_CostSumMs / m_CostTicks:F3} ms, max " +
                             $"{m_CostMaxMs:F3} ms over {m_CostTicks} ticks (the game day ended before {Tuning.CostTicks}); " +
                             $"largest batch {m_CostLargestBatch} request(s)" +
                             SkiGuestCounterSystem.CostSuffix(m_CostMeasurements, m_TimeSystem));
            else
                Mod.Log.Info("Tickets [cost] skipped: no tick with arrivals since noon" +
                             SkiGuestCounterSystem.CostSuffix(m_CostMeasurements, m_TimeSystem));
        }

        private void RecordCost(double ms, int batch)
        {
            m_BusyTicks++;
            if (m_CostLogged)
                return;
            if (m_CostSkipNext)
            {
                m_CostSkipNext = false; // a busy arming tick is not measured
                return;
            }
            m_CostTicks++;
            m_CostSumMs += ms;
            if (ms > m_CostMaxMs)
                m_CostMaxMs = ms;
            if (batch > m_CostLargestBatch)
                m_CostLargestBatch = batch;
            if (m_CostTicks < Tuning.CostTicks)
                return;
            m_CostLogged = true;
            m_CostMeasurements++;
            if (m_TimeSystem.normalizedTime >= 0.5f) // a line written at or after 12:00 is today's (as SkiDaySystem)
                m_CostArmDay = SkiDayRules.DayKey(m_TimeSystem.year, m_TimeSystem.normalizedDate, m_TimeSystem.daysPerYear);
            Mod.Log.Info($"Tickets [cost] main thread per tick with arrivals: mean {m_CostSumMs / m_CostTicks:F3} ms, max " +
                         $"{m_CostMaxMs:F3} ms over {m_CostTicks} ticks; largest batch {m_CostLargestBatch} request(s)" +
                         SkiGuestCounterSystem.CostSuffix(m_CostMeasurements, m_TimeSystem));
        }

        internal void AppendDump(StringBuilder sb)
        {
            try
            {
                sb.Append("  Tickets: armed ").Append(m_Armed).Append(", disabled ").Append(m_Disabled)
                  .Append(", default price ").Append(Price).Append(", job in flight ").Append(m_JobInFlight)
                  .Append(", busy ticks ").Append(m_BusyTicks).Append(", requests ").Append(m_RequestsTotal)
                  .Append(" (largest batch ").Append(m_LargestBatch).Append("), guests ").Append(m_GuestsTotal)
                  .Append(", income +").Append(m_IncomeTotal)
                  .Append(" (booked as public transportation fees +").Append(m_FeeBookedTotal)
                  .Append(')')
                  .Append(", lodge spend ").Append(LodgeSpend).Append(" per guest (× tier): ").Append(m_SpendsTotal)
                  .Append(" spent +").Append(m_SpendIncomeTotal)
                  // Passes: sold / income as booked, holder guests, households decided not to buy.
                  .Append(", season passes share ").Append(PassShare).Append(" %, default early price ").Append(PassPriceSetting).Append(": ")
                  .Append(m_PassesSold).Append(" sold +").Append(m_PassIncomeTotal).Append(", ").Append(m_PassHolderGuests)
                  .Append(" pass-holder guest(s), ").Append(m_PassesDeclined).Append(" rolled out, ").Append(m_PassesCannotPay)
                  .Append(" could not afford it, destination pass ").Append(m_DestGuests).Append(" paid +").Append(m_DestIncomeTotal)
                  .Append(" (").Append(m_DestUnpaid).Append(" could not pay)")
                  .Append(", turned away by price ").Append(m_PriceTurnedTotal).Append(" in ").Append(m_PriceTurnedParties).Append(" party(ies)")
                  .Append(" (last season key ").Append(m_PassSeasonKey == int.MinValue ? "n/a" : m_PassSeasonKey.ToString()).Append(')')
                  // The pre-season sale (included in the pass totals above), then lessons, as booked.
                  .Append(", pre-season sale: ").Append(m_SalesRun).Append(" run(s), pool ").Append(m_SalePoolTotal).Append(", bought ")
                  .Append(m_SaleBoughtTotal).Append(" +").Append(m_SaleIncomeTotal).Append(", could not pay ").Append(m_SaleCannotPayTotal)
                  .Append(", last cost ").Append(m_SaleLastScheduleMs.ToString("F2", CultureInfo.InvariantCulture)).Append(" ms schedule / ")
                  .Append(m_SaleLastApplyMs.ToString("F2", CultureInfo.InvariantCulture)).Append(" ms apply / ")
                  .Append(m_SaleLastWaitMs.ToString("F2", CultureInfo.InvariantCulture)).Append(" ms wait")
                  .Append(", lessons share ").Append(LessonShare).Append(" % × ").Append(LessonMultiplier.ToString("0.0#", CultureInfo.InvariantCulture))
                  .Append(" tickets: ").Append(m_LessonsTotal).Append(" taken +").Append(m_LessonIncomeTotal)
                  .Append(", lag arrival → booked ").Append(m_LastLagTicks).Append(" tick(s) (max ").Append(m_MaxLagTicks)
                  .Append(") of 16 frames; gates (charged):");
                if (m_LastGate.Count == 0)
                    sb.Append(" none yet;");
                foreach (var pair in m_LastGate)
                    sb.Append(' ').Append(Fmt(pair.Key)).Append(' ').Append(pair.Value).Append(';');
                // The watch's gates and the disagreements.
                sb.Append(" watch:");
                if (m_WatchGate.Count == 0)
                    sb.Append(" none yet;");
                foreach (var pair in m_WatchGate)
                    sb.Append(' ').Append(Fmt(pair.Key)).Append(' ').Append(pair.Value).Append(';');
                sb.Append(" charged gate differed from the watch ").Append(m_GateMismatches).Append(" time(s); area prices (revision ").Append(PriceRevision).Append("):");
                if (m_AreaPrices.Count == 0)
                    sb.Append(" none yet;");
                foreach (var pair in m_AreaPrices)
                {
                    AreaPrice ap = pair.Value;
                    sb.Append(' ').Append(Fmt(pair.Key)).Append(" ticket ").Append(ap.R.Ticket).Append(ap.R.OwnTicket != 0 ? " own" : " default")
                      .Append(", pass ").Append(PlanText(ap.R.Plan)).Append(ap.R.OnDefault != 0 ? " (default" : " (own").Append(ap.R.Locked != 0 ? ", locked)" : ")")
                      .Append(", now ").Append(ap.SalePrice).Append(" ").Append(ap.Phase).Append(", demand x").Append(ap.Demand.ToString("0.###", CultureInfo.InvariantCulture))
                      .Append(", value left ").Append(ap.ValueLeft.ToString("P0", CultureInfo.InvariantCulture)).Append(';');
                }
                sb.Append('\n');
            }
            catch (Exception e)
            {
                sb.Append("  Tickets: dump failed (").Append(e.GetType().Name).Append(": ").Append(e.Message).Append(")\n");
            }
        }

        private static EntityRef Ref(Entity e) => new EntityRef(e.Index, e.Version);
        private static string Fmt(Entity e) => e.Index + ":" + e.Version;


        /// <summary>Per slot: the sign's efficiency (inline BuildingUtils.cs:121-133) and the gate. Per request: a closed gate (or a City
        /// without PlayerMoney) books the guests free. An open one takes TicketsPaid × price from the household (never below 0). It books one
        /// FeeEvent per paid item. The budget credits the city, never this code. A
        /// pass holder pays no ticket. The lessons (LessonsAndSpend) and then the lodge spend come out of what the household has left.</summary>
        [BurstCompile]
        private struct TicketJob : IJob
        {
            [ReadOnly] public NativeArray<TicketRequest> Requests;
            [ReadOnly] public NativeArray<TicketSlot> Slots;
            [ReadOnly] public BufferLookup<Efficiency> Efficiencies;
            public BufferLookup<GameResources> HouseholdResources;
            [ReadOnly] public ComponentLookup<PlayerMoney> PlayerMoneys;   // HasComponent(City) only
            [ReadOnly] public ComponentLookup<SkiSeasonPass> Passes;   // the household's standing (read only)
            [ReadOnly] public ComponentLookup<SkiSeasonPassArea> PassAreas;   // T284: the standing's area (absent = good at every area)
            public NativeList<PassWrite> PassWrites;                   // appended here, applied on the main thread
            public int SeasonKey;                                      // SkiMoneyRules.SeasonKeyOfDate at the schedule
            public int PassShare;                                      // clamped setting (0 = no sales)
            public int LessonShare;                                    // clamped setting (0 = no lessons)
            public int LessonDay;                                      // SkiDayRules.DayKey at the schedule (the roll's day)
            public NativeQueue<ServiceFeeSystem.FeeEvent> FeeQueue;   // always valid (ScheduleJob)
            public Entity City;
            // T284: the ticket, lesson price, pass price, pass demand and sale window are per slot (TicketSlot, the area's own prices).
            public NativeArray<TicketResult> Results;              // Requests.Length
            public NativeArray<byte> SlotGate;                     // Slots.Length

            public void Execute()
            {
                for (int s = 0; s < Slots.Length; s++)
                {
                    TicketSlot slot = Slots[s];
                    float efficiency = EfficiencyOf(slot.Sign);
                    // RunStateKnown already excludes the load hold.
                    SlotGate[s] = (byte)SkiMoneyRules.TicketGateOf(slot.Ticket, efficiency, slot.RunStateKnown != 0, slot.OpenRuns);
                }
                bool cityOk = City != Entity.Null && PlayerMoneys.HasComponent(City);
                // Households decided earlier in THIS batch, with the deciding area (T284). So a second arrival record of the same household
                // in one drain does not wait for the components to exist, and a pass bought at one area is judged at the next one.
                var decided = new NativeHashMap<Entity, Decided>(8, Allocator.Temp);
                for (int i = 0; i < Requests.Length; i++)
                {
                    TicketRequest q = Requests[i];
                    TicketSlot qs = Slots[q.Slot];
                    int price = qs.Ticket;   // T284: the area's own day ticket
                    // 1.0.3 sale window, per area since T284: the first-arrival sale sells at the area's price now, not at all outside its run.
                    int passPrice = qs.SalePrice;
                    double passFactor = qs.PassDemand;   // pass demand (1.0.2), with value left (T284)
                    var r = new TicketResult { NoCity = (byte)(cityOk ? 0 : 1) };
                    int people = math.max(0, q.People);
                    if (SlotGate[q.Slot] != (byte)SkiMoneyRules.TicketGate.Open || !cityOk)
                    {
                        r.Free = people;
                        Results[i] = r;
                        continue;
                    }
                    int paid = 0;
                    bool holder = false;
                    if (HouseholdResources.HasBuffer(q.Household))
                    {
                        DynamicBuffer<GameResources> buffer = HouseholdResources[q.Household];
                        int money = EconomyUtils.GetResources(Resource.Money, buffer);
                        // A resident household's standing this season. No standing → decided now (once).
                        if (q.Tourist == 0)
                        {
                            SkiMoneyRules.PassStanding standing;
                            bool validHere = true;   // T284: the pass is good at this area (SkiPassPlanRules.PassValidAt)
                            if (decided.TryGetValue(q.Household, out Decided seen))
                            {
                                standing = seen.Kind == 1 ? SkiMoneyRules.PassStanding.Holder : SkiMoneyRules.PassStanding.Declined;
                                validHere = SkiPassPlanRules.PassValidAt(true, seen.Pool, seen.Sign == qs.Sign, qs.OnDefault != 0);
                            }
                            else if (Passes.TryGetComponent(q.Household, out SkiSeasonPass have))
                            {
                                standing = SkiMoneyRules.PassStandingOf(true, have.m_SeasonKey, have.m_Flags, SeasonKey);
                                bool hasArea = PassAreas.TryGetComponent(q.Household, out SkiSeasonPassArea area);
                                validHere = SkiPassPlanRules.PassValidAt(hasArea, area.m_Flags, hasArea && area.m_Sign == qs.Sign, qs.OnDefault != 0);
                            }
                            else
                                standing = SkiMoneyRules.PassStanding.None;
                            // 1.0.3: a household whose own buy hour has not come by the last hourly sale is left to that sale (it pays the
                            // ticket today, undecided). One whose hour has come and still has no standing is not in a sale pool: it decides here.
                            if (standing == SkiMoneyRules.PassStanding.None && PassShare > 0 && passPrice > 0 &&
                                SkiPassRules.HourCome(q.Household.Index, SeasonKey, qs.AppliedPos))
                            {
                                SkiMoneyRules.PassSaleResult sale =
                                    SkiMoneyRules.PassSale(true, PassShare, q.Household.Index, SeasonKey, passPrice, money, passFactor);
                                if (sale == SkiMoneyRules.PassSaleResult.Bought)
                                {
                                    // Taken from the household (price ≤ money: never below 0) and booked exactly like tickets.
                                    EconomyUtils.AddResources(Resource.Money, -passPrice, buffer);
                                    money -= passPrice;
                                    r.PassSold = 1;
                                    r.PassIncome = passPrice;
                                    r.PassSeason = SeasonKey;
                                    Book(ref r, passPrice, 1);
                                    byte pool = qs.OnDefault != 0 ? SkiPassPlanRules.AreaFlagPool : (byte)0;
                                    PassWrites.Add(new PassWrite { Household = q.Household, SeasonKey = SeasonKey, PricePaid = passPrice, Sign = qs.Sign, Pool = pool });
                                    decided.TryAdd(q.Household, new Decided { Kind = 1, Pool = pool, Sign = qs.Sign });
                                    standing = SkiMoneyRules.PassStanding.Holder;
                                    validHere = true;
                                }
                                else if (sale != SkiMoneyRules.PassSaleResult.NotEligible)
                                {
                                    // Rolled out or could not afford the pass: day tickets all season, not asked again.
                                    PassWrites.Add(new PassWrite
                                    {
                                        Household = q.Household,
                                        SeasonKey = SeasonKey,
                                        Flags = SkiMoneyRules.PassFlagDeclined,
                                        CannotPay = (byte)(sale == SkiMoneyRules.PassSaleResult.CannotPay ? 1 : 0),
                                        Sign = qs.Sign,
                                        Pool = qs.OnDefault != 0 ? SkiPassPlanRules.AreaFlagPool : (byte)0,
                                    });
                                    decided.TryAdd(q.Household, new Decided { Kind = 2, Pool = 0, Sign = qs.Sign });
                                    standing = SkiMoneyRules.PassStanding.Declined;
                                }
                            }
                            holder = standing == SkiMoneyRules.PassStanding.Holder && validHere;
                        }
                        else
                        {
                            // 1.0.3 destination pass: a party on one this season (stateless roll, SkiPassRules) pays the fee per person
                            // instead of the ticket, never into debt. The fee is pass income; the payers are guests on a pass.
                            TicketSlot ds = qs;
                            bool onDest = ds.DestFee > 0 && SkiPassRules.OnDestPass(q.Household.Index, q.Household.Version, SeasonKey, ds.DestShare);
                            if (onDest)
                            {
                                int destPaid = SkiMoneyRules.TicketsPaid(people, ds.DestFee, money);
                                if (destPaid > 0)
                                {
                                    r.DestIncome = destPaid * ds.DestFee;   // destPaid ≤ money / fee: no overflow, never below 0
                                    EconomyUtils.AddResources(Resource.Money, -r.DestIncome, buffer);
                                    Book(ref r, ds.DestFee, destPaid);
                                    LessonsAndSpend(ref r, buffer, money - r.DestIncome, people, destPaid, q.SpendPerPerson,
                                                    SlotSchool(q.Slot), q.Household, qs.LessonPrice);
                                }
                                r.DestPaid = destPaid;
                                r.Unpaid = people - destPaid;
                                r.DestUnpaid = people - destPaid;
                                Results[i] = r;
                                continue;
                            }
                            // 1.0.3 T258: a game-sent party (a vanilla outing; the ski days already priced their own claims) checks the
                            // price on its own curve and may walk away: nothing is taken (no ticket, lesson or lodge spend). Destination-pass
                            // holders never get here (they paid above). At a factor of 1 there is no roll: the default price keeps today's numbers.
                            if (SkiDayRules.PartyWalksAway(q.Outing != 0, onDest, price, ds.PriceTolerance, q.Household.Index, q.Household.Version, LessonDay))
                            {
                                r.PriceTurned = people;
                                Results[i] = r;
                                continue;
                            }
                        }
                        if (holder)
                        {
                            // A holder pays nothing at the gate (ledger: pass-holder guests, not paid / unpaid / free). The holder still
                            // takes lessons and spends at the lodge from what is left (shopping, not the gate).
                            r.PassHolders = people;
                            LessonsAndSpend(ref r, buffer, money, people, people, q.SpendPerPerson,
                                            SlotSchool(q.Slot), q.Household, qs.LessonPrice);
                            Results[i] = r;
                            continue;
                        }
                        paid = SkiMoneyRules.TicketsPaid(people, price, money);
                        if (paid > 0)
                        {
                            r.Income = paid * price;                   // paid ≤ money / price: no overflow, never below 0
                            EconomyUtils.AddResources(Resource.Money, -r.Income, buffer);
                            Book(ref r, price, paid);
                            // The lessons of the people counted, then the lodge spend of the paying guests, each from what the ticket left.
                            LessonsAndSpend(ref r, buffer, money - r.Income, people, paid, q.SpendPerPerson,
                                            SlotSchool(q.Slot), q.Household, qs.LessonPrice);
                        }
                    }
                    r.Paid = paid;
                    r.Unpaid = people - paid;
                    Results[i] = r;
                }
                decided.Dispose();
            }

            private bool SlotSchool(int slot) => Slots[slot].SchoolActive != 0;

            /// <summary>T284: a household decided in this batch: Kind 1 = bought, 2 = declined; the area that decided it and its pool flag.</summary>
            private struct Decided { public byte Kind, Pool; public Entity Sign; }

            /// <summary>After the ticket or pass, in this order, each from what the household still has (never below 0):
            /// - the ski lessons (SkiMoneyRules.LessonsTaken over <paramref name="people"/>, paid up to LessonsPaid),
            /// - then the lodge spend of <paramref name="spendPeople"/> at <paramref name="spendPerPerson"/>.
            /// Both are booked like the ticket (Book).</summary>
            private void LessonsAndSpend(ref TicketResult r, DynamicBuffer<GameResources> buffer, int money,
                                         int people, int spendPeople, int spendPerPerson, bool school, Entity household, int lessonPrice)
            {
                if (school && LessonShare > 0 && lessonPrice > 0)
                {
                    int taken = SkiMoneyRules.LessonsTaken(people, household.Index, LessonDay, LessonShare);
                    int lessons = SkiMoneyRules.LessonsPaid(taken, lessonPrice, money);
                    if (lessons > 0)
                    {
                        r.Lessons = lessons;
                        r.LessonIncome = lessons * lessonPrice;   // ≤ money left: no overflow, never below 0
                        EconomyUtils.AddResources(Resource.Money, -r.LessonIncome, buffer);
                        money -= r.LessonIncome;
                        Book(ref r, lessonPrice, lessons);
                    }
                }
                if (spendPerPerson > 0)
                {
                    int spends = SkiMoneyRules.SpendPaid(spendPeople, spendPerPerson, money);
                    if (spends > 0)
                    {
                        r.Spends = spends;
                        r.SpendIncome = spends * spendPerPerson;   // ≤ money left: no overflow, never below 0
                        EconomyUtils.AddResources(Resource.Money, -r.SpendIncome, buffer);
                        Book(ref r, spendPerPerson, spends);
                    }
                }
            }

            /// <summary>Books <paramref name="count"/> × <paramref name="unit"/> for a ticket, pass, lesson or lodge spend: one
            /// FeeEvent{PublicTransport, 1, unit} per item as the bus fare (ResidentAISystem.cs:3929-3935). The budget credits the fee line to
            /// PlayerMoney (BudgetApplySystem.cs:57-59), so no direct add.</summary>
            private void Book(ref TicketResult r, int unit, int count)
            {
                r.Booked += unit * count;
                for (int k = 0; k < count; k++)
                    FeeQueue.Enqueue(new ServiceFeeSystem.FeeEvent
                    {
                        m_Amount = 1f,
                        m_Cost = unit,
                        m_Resource = PlayerResource.PublicTransport,
                        m_Outside = false,
                    });
            }

            /// <summary>BuildingUtils.GetEfficiency(DynamicBuffer) re-implemented: product of
            /// max(0, f). 0 when not > 0. Else max(0.01, round(100p)/100). No buffer → 1.</summary>
            private float EfficiencyOf(Entity sign)
            {
                if (!Efficiencies.TryGetBuffer(sign, out DynamicBuffer<Efficiency> buffer))
                    return 1f;
                float p = 1f;
                for (int k = 0; k < buffer.Length; k++)
                    p *= math.max(0f, buffer[k].m_Efficiency);
                if (!(p > 0f))
                    return 0f;
                return math.max(0.01f, math.round(100f * p) / 100f);
            }
        }
    }
}
