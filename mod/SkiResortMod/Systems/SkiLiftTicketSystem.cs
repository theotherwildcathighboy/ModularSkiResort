// SkiLiftTicketSystem — the lift ticket (Round E1, decision D26; docs/research/econ-round/spec/
// ch4-money.md §5, §10): each arriving guest (SkiGuestCounterSystem.DrainArrivals) pays the ticket price
// from their household into the city's balance, when the gate is open (SkiMoneyRules.TicketGateOf,
// e1-spec R6) and the household can pay (never into debt, DV9). Stage 0 (S0) laid down the shell
// (e1-spec I-19); stream S4 wrote the system (2026-10-04).
//
// THE MONEY MOVEMENT (ch4 §5.1; stage 1a, DECISIONS 239, docs/research/econ-2/build/stage1a-fare-booking.md): the
// vanilla transit-fare pattern (ResidentAISystem.BoardingJob : IJob, ResidentAISystem.cs:3393; the fare at
// :3922-3935 = EconomyUtils.AddResources(Resource.Money, -price, household resources) + PlayerMoney.Add on the
// City entity + a ServiceFeeSystem.FeeEvent{m_Amount 1, m_Cost price, PlayerResource.PublicTransport, m_Outside
// false} on the fee queue). One Burst IJob per tick that has arrivals: EconomyUtils.GetResources / AddResources
// (EconomyUtils.cs:307-338) on the household's Game.Economy.Resources buffer and one PlayerMoney.Add(total)
// (PlayerMoney.cs:33-36). Money moves; none is created and none is lost (a City without PlayerMoney charges
// nobody: guests go free, one WARN per load).
// BOOKED AS FARES (setting BookTicketsAsFees, Options → Balance, default on, read live): each PAID ticket enqueues
// one FeeEvent like the bus fare, so the income shows in the Economy panel under public transportation fees, and
// the ticket total is NOT added to PlayerMoney in the job: BudgetApplySystem.cs:28-60 credits the fee line
// (CityServiceBudgetSystem.cs:225-226 = ServiceFeeSystem.GetServiceFees(PublicTransport).x) into PlayerMoney
// itself, so adding it here too would pay the city twice (the household is debited once either way). The fee queue
// and its dependency are handled as ResidentAISystem does (:265 GetFeeQueue(out deps), :277 deps combined into the
// schedule, :285 AddQueueWriter(handle); ServiceFeeSystem.cs:443-452): always, also with the setting off, so the job
// struct never holds a default NativeQueue (a job that enqueues nothing is a harmless writer). Setting off = the old
// behavior: direct PlayerMoney.Add, no FeeEvent, income invisible in the Economy panel. The lodge spend stays a
// direct add (shopping, not transit; no FeeEvent). UNVERIFIED in game: that the Economy panel line rises and the
// treasury is credited once; with Realistic Workplaces And Households ("RWH") loaded, which replaces
// BudgetApplySystem with its own copy, that fee income reaches the treasury at all (one WARN per load).
// Two deliberate differences from the fare: the direct add is dropped while tickets are booked as fares (above),
// and NEVER INTO DEBT
// (DV9): SkiMoneyRules.TicketsPaid = min(people, money / price), the rest are guests who could not pay
// (counted, admitted, no money): low money makes residents move away (HouseholdBehaviorSystem.cs:172) and
// hotel tourists leave (TouristLeaveSystem.cs:60-73).
//
// THE GATE (ch4 §5.3, e1-spec R6; Round M2 T1, docs/research/round-m2/decisions.md R2), first match wins: Off
// (price 0) → NoEfficiency (the sign's efficiency not > 0) → NotKnownYet (no score yet, or the area's first score
// after a load still held: the first-write hold, AreaScoreView.WriteHeld) → Open on a counted run (RunsCounted,
// runs worth more than 0), else NothingOpen. Since Round M2 a running lift alone no longer opens it (the
// operating-lift branch of SkiMoneyRules.TicketGateOf is gone; free entry during a load hold is accepted, at most
// RatingHoldMaxTicks = 1,024 ticks = 16,384 frames). Inputs per ski area with arrivals ("slot"): the run state
// is known when SkiResortAttractivenessSystem.TryGetAreaScore returns true and the view is not WriteHeld, then
// Result.RunsCounted, else (false, 0) — the hub's source and rule (ch5 §5.4; SkiResortStatsSystem's ticket gate).
// SkiLiftChargeSystem is no longer a gate input (its Operating count is printed in the gate line only).
// Efficiency is computed IN THE JOB from a BufferLookup<Efficiency> (the BuildingUtils.cs:121-133 formula:
// product of max(0, f); 0 when not > 0; else max(0.01, round(100p)/100); no buffer → 1), so the main
// thread never waits on the many jobs that write Efficiency; BuildingUtils.GetEfficiency(DynamicBuffer) is
// not called (its overload set includes GetEfficiency(Span<float>): CS7069 on net48, e1-spec I-28).
//
// STEP ORDER per tick (ch4 §5.5; try/catch around all of it): 1 complete last tick's job and book its
// results into the ledger (AddTickets per request; AddLocalsPaid for residents who paid, the O1 note);
// the charged gate recorded (Round S1 F6: silent) and first-ticket lines; 2 read the price (live, every tick —
// so Price and the `Tickets [price]` line follow the Options slider even with no guests; deviation, notes-S4);
// 2b the gate watch (Round S1 F6, header GATE WATCH); 3 drain the
// counter's arrivals (none → return: an idle tick costs a list count and the price read); 4 price 0 →
// every arrival is booked free on the main thread (no job); 5 one slot per distinct sign; 6 fill the
// persistent lists and schedule TicketJob on Dependency. The lookups (Efficiency RO, Resources RW,
// PlayerMoney RW) make ECS order the job after every writer of household money or PlayerMoney, and every
// later writer after it (the upkeep's main-thread PlayerMoney read completes it first).
//
// PHASE: GameSimulation, anchored UpdateAfter<SkiLiftTicketSystem, SkiGuestCounterSystem> (Mod.cs; not
// load-bearing: the counter's pending list is DRAINED, so a different offset neither loses nor repeats an
// arrival — ch4 §5.5). Pause stops it; the Editor never arms it.
// CADENCE: GetUpdateInterval 16 (the counter's interval).
// COST (ch4 §8; estimates, UNVERIFIED until the field): idle tick = a list count and the price read; busy
// tick = a few dozen requests and one schedule; the job is O(requests + slots). Measured after each load:
// `Tickets [cost]` after Tuning.CostTicks ticks with arrivals. Round M1 (M8, m1-spec F10, E5): re-armed once per
// game day at the first tick at or after 12:00 (SkiDayRules.DayKey; only once the last line was written); a busy
// arming tick is not measured; the line ends "; measurement n this load, YYYY-MM at HH:MM"
// (SkiGuestCounterSystem.CostSuffix). No synchronous part: the job is never waited on here. Round M2 (D10): a
// midday measurement not finished when the game day ends is written partial at the day change (CostDayEnd:
// "(the game day ended before 64)" with the ticks it has, or "skipped: no tick with arrivals since noon").
// Known silent case, out of scope (measure-core-check G11): the LOAD-time measurement never writes when no guest
// arrives after a load (it waits for CostTicks ticks with arrivals; the midday re-arm needs a written line first).
// LOGS (ch4 §5.7, exact): created; ARMED (names the booking mode, stage 1a); `Tickets [price]` on a change;
// `Tickets [booking]` on a change of BookTicketsAsFees; one WARN per load when "RWH" is loaded and tickets are booked
// as fares (SkiOtherMods; assembly name only, behavior never depends on it); `Tickets [gate]` per ski area when its
// gate changes, evaluated each tick its inputs move (attraction Generation, lift-charge Revision, price) with the
// hub's inputs (Round S1 F6, ski-days spec ch3 §5: before, the line came only with arriving guests — field test
// 8's "open" was 4 min 26 s late), the first evaluation of a sign too, through m_GateLog (LogWindow 10 s; R15:
// a counted run can flip with the snow), folds flushed on every tick (the idle tick included), in
// OnGamePreload and in OnDestroy; `Tickets [first]` per ski area per load; `Tickets [cost]` after the load and daily
// at midday (Round M1; a partial line at the day change, Round M2 D10); the one-shot
// `!!!` (disabled for the load). Month totals are in the counter's `Guests [month]` line.
// GATE WATCH (Round S1 F6): step 2b of every tick — when Generation, Revision or the price moved, one gate per live
// sign (SkiResortAttractivenessSystem.CopySigns) from SkiMoneyRules.TicketGateOf(price, Result.Efficiency else 1,
// known = score present and not WriteHeld, RunsCounted when known else 0), exactly the hub's rule
// (SkiResortStatsSystem, the `live.TicketGate` line of the area rows); a changed gate (or a new sign) writes one line;
// a sign that left is dropped silently. The lift-charge Revision trigger stays (log body only since Round M2 T1;
// harmless). Releasing the first-write hold moves the attraction Generation, so the watch leaves NotKnownYet on the
// pass the hold is released (thaw-rules-check C18). The job's charged gate is no longer logged: it is recorded per
// sign (DUMP "gates (charged)") and each disagreement with the watch is counted (DUMP). Round M2 (D16a): a sign the
// watch no longer sees is pruned from the charged gates too, and a job result for such a sign does not re-add it.
// CONTRACT (house pattern): "created" line; per-load reset in OnGameLoadingComplete; OnGamePreload completes
// the job and books its results first (money already moved must reach the ledger before it resets), then
// flushes the gate fold (try/caught: GameSystemBase.cs:85-96); since the E1 fixer pass 2 the guest counter's
// OnGamePreload, which runs first, books that job through BookInFlightForPreload before it writes the
// `(until the load)` month lines; OnDestroy completes and disposes.
// Round M2 (2026-10-10, builder B3; docs/research/round-m2/build-contract.md §1 B3, §6.8): T1 (R2) the gate needs a
// counted run, known = score present and not held (TicketSlot loses OperatingLifts; the gate line's NothingOpen /
// NotKnownYet texts and the ARMED gate clause); D10 CostDayEnd and m_CostByRearm; D16a m_LastGate pruned.
// CATALOG PHASE B (2026-10-11, economy group; docs/research/art/assets-2026-10/code-round-2/round-2-contract.md §7.4,
// D15; seams S3 SkiMoneyRules lodge-spend rules, S7 Mod.Settings.LodgeSpendPerGuest, S8 SkiResortLedger.AddLodgeSpend):
// the optional LODGE SPEND. The setting (Options, Balance; 0-50, default 0 = off) is read live every tick and clamped
// (SkiMoneyRules.ClampLodgeSpend). Per request, on the main thread: SpendPerPerson = SkiMoneyRules.LodgeSpendOf(setting,
// the lodge row's SpendMultiplier) when the arrival's building is a lodge (the prefab catalog's LodgeDefs view: A-frame /
// hut × 1, mid × 1.5, luxury × 3), else 0 — a plain int field, so the Burst job reads no catalog row (risk R-a). In the
// job, only for a PAYING guest of an OPEN gate, AFTER the ticket: spends = SkiMoneyRules.SpendPaid(paid, spend per
// person, money left after the tickets) — never into debt (DV9) — taken from the same household buffer and added to the
// same single PlayerMoney.Add (the fare pattern above); the result carries Spends / SpendIncome per request, booked with
// AddLodgeSpend. Price 0 = gate Off = nothing charged (BookAllFree, no job). Setting 0 = no change at all: every
// SpendPerPerson is 0, the job moves no extra money, AddLodgeSpend is never called with a non-zero amount. Logs:
// `Tickets [lodge spend]` on a setting change (like `Tickets [price]`), the ARMED line names it, `Tickets [first]` adds
// the first lodge spend of the batch, and the DUMP sums it.

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
            /// <summary>At most one gate line per this many wall-clock seconds (R15).</summary>
            public const float LogWindowS = 10f;
            /// <summary>Ticks with arrivals timed for the `Tickets [cost]` line.</summary>
            public const int CostTicks = 64;
        }

        // Catalog Phase B (D15): SpendPerPerson = the lodge spend per paying guest of this request (0 = none), filled on the
        // main thread; Spends / SpendIncome = the guests who spent and the ₵ moved for it.
        private struct TicketRequest { public int Slot; public Entity Household; public int People; public byte Tourist; public int SpendPerPerson; }
        // Round M2 T1: OperatingLifts removed (the gate no longer reads lifts); RunStateKnown = score present and not held.
        private struct TicketSlot { public Entity Sign; public byte RunStateKnown; public int OpenRuns; }
        // Stage 1a: Booked = the ticket income booked as public-transport fares (FeeEvents) instead of added to PlayerMoney.
        private struct TicketResult { public int Paid, Unpaid, Free, Income, Spends, SpendIncome, Booked; public byte NoCity; }

        /// <summary>The clamped setting read on the last tick (SkiMoneyRules.ClampTicketPrice).</summary>
        internal int Price { get; private set; } = SkiMoneyRules.TicketPriceDefault;

        /// <summary>Catalog Phase B (D15): the clamped lodge-spend setting read on the last tick (SkiMoneyRules.ClampLodgeSpend;
        /// 0 = off).</summary>
        internal int LodgeSpend { get; private set; } = SkiMoneyRules.LodgeSpendDefault;

        /// <summary>Stage 1a: the BookTicketsAsFees setting read on the last tick (true = each paid ticket is a public-transport
        /// FeeEvent and the job does not add the ticket total to PlayerMoney).</summary>
        internal bool BookAsFees { get; private set; } = SkiResortMod.Settings.SkiResortSettings.DefaultBookTicketsAsFees;

        /// <summary>Stage 1a: the ticket income booked as public-transport fees this load, as ApplyResults has booked it
        /// (the counter's `Guests [month]` totals take month baselines of it, like the run upkeep's DebitedThisLoad).</summary>
        internal long FeeBookedThisLoad => m_FeeBookedTotal;

        /// <summary>The gate the last ticket tick evaluated for <paramref name="sign"/>.</summary>
        internal bool TryGetLastGate(Entity sign, out SkiMoneyRules.TicketGate gate)
        {
            return m_LastGate.TryGetValue(sign, out gate);
        }

        // ── systems and lookups ───────────────────────────────────────────────────────────────────
        private SimulationSystem m_SimulationSystem;
        private TimeSystem m_TimeSystem;   // Round M1 (M8): the midday re-arm and the [cost] clock
        private CitySystem m_CitySystem;
        private ServiceFeeSystem m_ServiceFeeSystem;   // Stage 1a: the fee queue (ResidentAISystem.cs:199 pattern)
        private SkiGuestCounterSystem m_Counter;
        private SkiResortAttractivenessSystem m_Attractiveness;
        private SkiPrefabCatalogSystem m_PrefabCatalog;   // Catalog Phase B (D15): the lodge rows (LodgeDefs)
        private SkiLiftChargeSystem m_LiftCharge;
        private BufferLookup<Efficiency> m_EfficiencyLookup;
        private BufferLookup<GameResources> m_ResourcesLookup;
        private ComponentLookup<PlayerMoney> m_PlayerMoneyLookup;

        // ── job data (Persistent; the job owns them while in flight) ──────────────────────────────
        private NativeList<TicketRequest> m_Requests;
        private NativeList<TicketSlot> m_Slots;
        private NativeList<TicketResult> m_Results;
        private NativeList<byte> m_SlotGate;
        private JobHandle m_Handle;
        private bool m_JobInFlight;
        private uint m_JobFrame, m_JobPendingFrame;

        // ── managed state ─────────────────────────────────────────────────────────────────────────
        private readonly List<GuestArrival> m_Arrivals = new List<GuestArrival>(16);
        private readonly Dictionary<Entity, int> m_SlotOf = new Dictionary<Entity, int>();
        private readonly Dictionary<Entity, SkiMoneyRules.TicketGate> m_LastGate = new Dictionary<Entity, SkiMoneyRules.TicketGate>();
        private readonly HashSet<Entity> m_FirstPaid = new HashSet<Entity>();
        private readonly LogWindow m_GateLog = new LogWindow(Tuning.LogWindowS);

        // Round S1 F6: the gate watch (header GATE WATCH); all reset at load.
        private readonly List<Entity> m_WatchSigns = new List<Entity>(4);
        private readonly HashSet<Entity> m_WatchSeen = new HashSet<Entity>();
        private readonly List<Entity> m_WatchDrop = new List<Entity>(2);
        private readonly Dictionary<Entity, SkiMoneyRules.TicketGate> m_WatchGate = new Dictionary<Entity, SkiMoneyRules.TicketGate>();
        private bool m_WatchPrimed;
        private int m_WatchGeneration, m_WatchChargeRevision, m_WatchPrice;
        private long m_GateMismatches;

        // ── per-load state ────────────────────────────────────────────────────────────────────────
        private bool m_Armed, m_Disabled, m_ErrorLogged, m_NoCityWarned, m_RwhWarned;
        private long m_BusyTicks, m_RequestsTotal, m_GuestsTotal, m_IncomeTotal;
        private long m_FeeBookedTotal;   // Stage 1a: this load (DUMP, the counter's month line)
        private long m_SpendsTotal, m_SpendIncomeTotal;   // Catalog Phase B (D15): this load (DUMP)
        private int m_LargestBatch, m_MaxLagTicks, m_LastLagTicks;
        private int m_CostTicks, m_CostLargestBatch;
        private double m_CostSumMs, m_CostMaxMs;
        private bool m_CostLogged;
        private bool m_CostSkipNext;               // Round M1 (M8): true on the arming tick only; RecordCost leaves that tick out
        private int m_CostArmDay = int.MinValue;   // Round M1: SkiDayRules.DayKey of the last midday re-arm or afternoon line
        private int m_CostMeasurements;            // Round M1: `Tickets [cost]` lines this load
        private bool m_CostByRearm;                // Round M2 (D10): the measurement in progress was armed at midday (not the load's)

        protected override void OnCreate()
        {
            base.OnCreate();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_TimeSystem = World.GetOrCreateSystemManaged<TimeSystem>();   // Round M1 (M8)
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_ServiceFeeSystem = World.GetOrCreateSystemManaged<ServiceFeeSystem>();   // Stage 1a
            m_Counter = World.GetOrCreateSystemManaged<SkiGuestCounterSystem>();
            m_Attractiveness = World.GetOrCreateSystemManaged<SkiResortAttractivenessSystem>();
            // Catalog Phase B: after the attractiveness system, which creates the catalog in its own OnCreate (creation
            // order = load-hook order, GameSystemBase.cs:27-29: unchanged by this call).
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_LiftCharge = World.GetOrCreateSystemManaged<SkiLiftChargeSystem>();
            m_EfficiencyLookup = GetBufferLookup<Efficiency>(isReadOnly: true);
            m_ResourcesLookup = GetBufferLookup<GameResources>(isReadOnly: false);
            m_PlayerMoneyLookup = GetComponentLookup<PlayerMoney>(isReadOnly: false);
            m_Requests = new NativeList<TicketRequest>(16, Allocator.Persistent);
            m_Slots = new NativeList<TicketSlot>(4, Allocator.Persistent);
            m_Results = new NativeList<TicketResult>(16, Allocator.Persistent);
            m_SlotGate = new NativeList<byte>(4, Allocator.Persistent);
            // Job reflection data and JIT before the first Schedule (the PrewarmJit precedent; ch4 §4.5).
            IJobExtensions.EarlyJobInit<TicketJob>();
            Mod.Log.Info($"{nameof(SkiLiftTicketSystem)} created");
        }

        /// <summary>The guest counter's interval (header CADENCE).</summary>
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
            }
            catch (Exception)
            {
                // teardown: nothing left to report to
            }
            if (m_Requests.IsCreated) m_Requests.Dispose();
            if (m_Slots.IsCreated) m_Slots.Dispose();
            if (m_Results.IsCreated) m_Results.Dispose();
            if (m_SlotGate.IsCreated) m_SlotGate.Dispose();
            base.OnDestroy();
        }

        /// <summary>E1 fixer pass 2 (e1-spec §8.10 G8): called by SkiGuestCounterSystem.OnGamePreload BEFORE it
        /// writes the `(until the load)` month lines — the counter's preload runs first (it subscribes first),
        /// so without this the line missed the last ticket batch. Books the job in flight, if any; this
        /// system's own OnGamePreload then finds nothing in flight. Never throws (one WARN instead).</summary>
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
                if (m_GateLog.Count > 0)
                    WriteGateSummary(UnityEngine.Time.realtimeSinceStartup);
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
            m_Requests.Clear();
            m_Slots.Clear();
            m_Results.Clear();
            m_SlotGate.Clear();
            m_Arrivals.Clear();
            m_SlotOf.Clear();
            m_LastGate.Clear();
            m_FirstPaid.Clear();
            m_GateLog.Reset();   // flushed in OnGamePreload; nothing carries over
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
            m_RwhWarned = false;           // Stage 1a: one WARN per load
            m_FeeBookedTotal = 0;
            m_BusyTicks = m_RequestsTotal = m_GuestsTotal = m_IncomeTotal = 0;
            m_SpendsTotal = m_SpendIncomeTotal = 0;   // Catalog Phase B
            m_LargestBatch = m_MaxLagTicks = m_LastLagTicks = 0;
            m_CostTicks = m_CostLargestBatch = 0;
            m_CostSumMs = m_CostMaxMs = 0.0;
            m_CostLogged = false;
            m_CostSkipNext = false;        // Round M1 (M8)
            m_CostArmDay = int.MinValue;
            m_CostMeasurements = 0;
            m_CostByRearm = false;         // Round M2 (D10): the load's measurement keeps the M1 rule
            Price = ReadPrice();
            LodgeSpend = ReadLodgeSpend(); // Catalog Phase B (D15)
            BookAsFees = ReadBookAsFees(); // Stage 1a

            if (mode != GameMode.Game)
                return;

            m_Armed = true;
            Mod.Log.Info($"Tickets: ARMED — price {Price} (setting LiftTicketPrice, 0 = off; read live); each counted guest " +
                         "pays once on arrival, taken from the guest's household; " + BookingText(BookAsFees) +
                         " (setting BookTicketsAsFees; read live); a household pays for as many " +
                         "members as its money covers, never below 0; gate = Welcome Sign efficiency above 0 and a counted run " +
                         "(open or patchy; a running lift alone does not open it), not known while the ski area's first score " +
                         "after a load is held; one job per 16-frame tick with arrivals; " +
                         $"lodge spend {LodgeSpend} per paying guest at a lodge (setting LodgeSpendPerGuest, 0 = off; read live; " +
                         "× the lodge tier's multiplier, A-frame / hut 1, mid 1.5, luxury 3; after the ticket, open gate only, " +
                         "never below 0; always a direct add to the city's balance)");
            WarnRwhOnce();
        }

        /// <summary>Stage 1a: the booking mode as the ARMED and `Tickets [booking]` lines print it.</summary>
        private static string BookingText(bool asFees)
        {
            return asFees
                ? "booked as public-transport fares: one FeeEvent per paid ticket like the vanilla bus fare " +
                  "(ResidentAISystem.cs:3922-3935), credited to the city by the budget (BudgetApplySystem), shown in the " +
                  "Economy panel, NOT added to the balance directly (that would pay twice)"
                : "added directly to the city's balance like the vanilla transit fare without a FeeEvent " +
                  "(not shown in the Economy panel)";
        }

        /// <summary>Stage 1a: one WARN per load when Realistic Workplaces And Households (assembly "RWH", by name only,
        /// SkiOtherMods) is loaded and tickets are booked as fares. RWH replaces BudgetApplySystem with its own copy, so
        /// whether fee income reaches the treasury there is UNVERIFIED. The behavior does not depend on it.</summary>
        private void WarnRwhOnce()
        {
            if (m_RwhWarned || !BookAsFees || !SkiOtherMods.RealisticWorkplacesDetected)
                return;
            m_RwhWarned = true;
            Mod.Log.Warn("Tickets: Realistic Workplaces And Households (assembly RWH) is loaded and tickets are booked as " +
                         "public-transport fares: it replaces BudgetApplySystem with its own copy, so whether fee income " +
                         "reaches the treasury there is UNVERIFIED — if the balance does not rise with the tickets, turn off " +
                         "'Book lift tickets as public-transport fees' in Options (warned once per load)");
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !m_Armed)
                    return;
                // 0a. Round M2 (D10): a midday measurement not finished when the game day ended is written partial
                // (inside the try, after the disarmed / disabled return: measure-core-check G11).
                CostDayEnd();
                // 0. Round M1 (M8): the cost measurement re-arms once per game day at midday; the arming tick is not
                // measured (the flag lives for this tick only).
                m_CostSkipNext = RearmCostAtMidday();
                long t0 = Stopwatch.GetTimestamp();

                // 1. Last tick's job (done by now) → the ledger.
                if (m_JobInFlight)
                    ApplyResults(quiet: false);

                // 2. The price, live.
                int price = ReadPrice();
                if (price != Price)
                {
                    Mod.Log.Info($"Tickets [price] lift ticket price {Price} → {price}{(price == 0 ? " (tickets off)" : "")}");
                    Price = price;
                }
                // 2a. Catalog Phase B (D15): the lodge-spend setting, live.
                int spend = ReadLodgeSpend();
                if (spend != LodgeSpend)
                {
                    Mod.Log.Info($"Tickets [lodge spend] per paying guest at a lodge {LodgeSpend} → {spend}{(spend == 0 ? " (off)" : "")} " +
                                 "(× the lodge tier's multiplier; charged after the ticket while the gate is open, never below 0)");
                    LodgeSpend = spend;
                }
                // 2a'. Stage 1a: the booking mode, live (takes effect with the next job scheduled).
                bool asFees = ReadBookAsFees();
                if (asFees != BookAsFees)
                {
                    Mod.Log.Info($"Tickets [booking] {(BookAsFees ? "public-transport fares" : "direct add")} → " +
                                 $"{(asFees ? "public-transport fares" : "direct add")}: {BookingText(asFees)}");
                    BookAsFees = asFees;
                    WarnRwhOnce();
                }
                if (m_GateLog.Count > 0)
                {
                    float now = UnityEngine.Time.realtimeSinceStartup;
                    if (m_GateLog.TrailingDue(now))
                        WriteGateSummary(now);
                }

                // 2b. Round S1 F6: the gate watch (header GATE WATCH), whether guests arrive or not.
                WatchGates(price);

                // 3. Drain the counter.
                m_Arrivals.Clear();
                if (m_Counter.DrainArrivals(m_Arrivals) == 0)
                    return;
                m_JobPendingFrame = m_Counter.LastPendingFrame;

                // 5 (before 4: the price-0 path logs its gates with the same slot inputs).
                BuildSlots(price, spend);

                // 4. Tickets off: every arrival is booked free, no job.
                if (price == 0)
                    BookAllFree();
                else
                    ScheduleJob(price, asFees);

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
                m_JobInFlight = false;
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

        /// <summary>Catalog Phase B (D15, seams S3 / S7): the lodge-spend setting, clamped; the default when no settings.</summary>
        private static int ReadLodgeSpend()
        {
            return SkiMoneyRules.ClampLodgeSpend(Mod.Settings?.LodgeSpendPerGuest ?? SkiMoneyRules.LodgeSpendDefault);
        }

        /// <summary>Stage 1a: BookTicketsAsFees, live; the default when no settings.</summary>
        private static bool ReadBookAsFees()
        {
            return Mod.Settings?.BookTicketsAsFees ?? SkiResortMod.Settings.SkiResortSettings.DefaultBookTicketsAsFees;
        }

        // ── slots and the job ─────────────────────────────────────────────────────────────────────

        /// <summary>One slot per distinct sign of this tick's arrivals (gate inputs, e1-spec R6) and one request
        /// per arrival. Catalog Phase B (D15): with a price and a lodge-spend setting above 0, a request whose building is
        /// a lodge carries SpendPerPerson = LodgeSpendOf(setting, its row's SpendMultiplier) (the catalog's LodgeDefs view,
        /// fetched once; one PrefabRef read per such arrival); 0 otherwise — so with either setting at 0 nothing is read
        /// and every request is today's.</summary>
        private void BuildSlots(int price, int spendSetting)
        {
            m_SlotOf.Clear();
            m_Slots.Clear();
            m_Requests.Clear();
            IReadOnlyDictionary<Entity, LodgeDef> lodgeDefs = price > 0 && spendSetting > 0 ? m_PrefabCatalog.LodgeDefs : null;
            for (int i = 0; i < m_Arrivals.Count; i++)
            {
                GuestArrival a = m_Arrivals[i];
                if (!m_SlotOf.TryGetValue(a.Sign, out int slot))
                {
                    slot = m_Slots.Length;
                    m_SlotOf[a.Sign] = slot;
                    // Round M2 T1 (R2): known = a score present AND not in the first-write hold (thaw-rules.md item 2a).
                    bool has = m_Attractiveness.TryGetAreaScore(a.Sign, out SkiResortAttractivenessSystem.AreaScoreView view);
                    bool known = has && !view.WriteHeld;
                    m_Slots.Add(new TicketSlot
                    {
                        Sign = a.Sign,
                        RunStateKnown = (byte)(known ? 1 : 0),
                        OpenRuns = known ? view.Result.RunsCounted : 0,
                    });
                }
                int spendPerPerson = 0;
                if (lodgeDefs != null && a.Building != Entity.Null && a.Building != a.Sign &&
                    EntityManager.Exists(a.Building) && EntityManager.HasComponent<Game.Prefabs.PrefabRef>(a.Building) &&
                    lodgeDefs.TryGetValue(EntityManager.GetComponentData<Game.Prefabs.PrefabRef>(a.Building).m_Prefab, out LodgeDef lodge))
                    spendPerPerson = SkiMoneyRules.LodgeSpendOf(spendSetting, lodge.SpendMultiplier);
                m_Requests.Add(new TicketRequest
                {
                    Slot = slot,
                    Household = a.Household,
                    People = a.People,
                    Tourist = (byte)(a.Tourist ? 1 : 0),
                    SpendPerPerson = spendPerPerson,
                });
            }
        }

        private void ScheduleJob(int price, bool asFees)
        {
            int requests = m_Requests.Length, slots = m_Slots.Length;
            m_Results.ResizeUninitialized(requests);
            m_SlotGate.ResizeUninitialized(slots);
            m_EfficiencyLookup.Update(this);
            m_ResourcesLookup.Update(this);
            m_PlayerMoneyLookup.Update(this);
            // Stage 1a: the fee queue and its dependency, as ResidentAISystem.cs:265 / :277 / :285 (ServiceFeeSystem.cs:443-452).
            NativeQueue<ServiceFeeSystem.FeeEvent> feeQueue = m_ServiceFeeSystem.GetFeeQueue(out JobHandle feeDeps);
            var job = new TicketJob
            {
                Requests = m_Requests.AsArray(),
                Slots = m_Slots.AsArray(),
                Efficiencies = m_EfficiencyLookup,
                HouseholdResources = m_ResourcesLookup,
                PlayerMoneys = m_PlayerMoneyLookup,
                FeeQueue = feeQueue,
                BookAsFees = (byte)(asFees ? 1 : 0),
                City = m_CitySystem.City,
                Price = price,
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

        /// <summary>Step 4: tickets off — every guest is booked free on the main thread; the gate is Off.</summary>
        private void BookAllFree()
        {
            SkiResortLedger ledger = m_Counter.Ledger;
            for (int i = 0; i < m_Requests.Length; i++)
            {
                TicketRequest q = m_Requests[i];
                ledger.AddTickets(Ref(m_Slots[q.Slot].Sign), 0, 0, q.People, 0);
                m_GuestsTotal += Math.Max(0, q.People);
            }
            m_RequestsTotal += m_Requests.Length;
            for (int s = 0; s < m_Slots.Length; s++)
                RecordChargedGate(m_Slots[s], SkiMoneyRules.TicketGate.Off);
            m_Requests.Clear();
            m_Slots.Clear();
        }

        /// <summary>Step 1: complete the job and book every request's result (ledger, the O1 locals count),
        /// then the charged gate per slot (Round S1 F6: recorded, not logged) and the first paid ticket per ski
        /// area. <paramref name="quiet"/> (teardown) skips the lines that read ECS data.</summary>
        private void ApplyResults(bool quiet)
        {
            m_Handle.Complete();
            m_JobInFlight = false;
            SkiResortLedger ledger = m_Counter.Ledger;
            bool noCity = false;
            for (int i = 0; i < m_Requests.Length; i++)
            {
                TicketRequest q = m_Requests[i];
                TicketResult r = m_Results[i];
                Entity sign = m_Slots[q.Slot].Sign;
                ledger.AddTickets(Ref(sign), r.Paid, r.Unpaid, r.Free, r.Income);
                ledger.AddLodgeSpend(Ref(sign), r.Spends, r.SpendIncome);   // Catalog Phase B (D15, seam S8): all-zero = no change
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

        // ── logs ──────────────────────────────────────────────────────────────────────────────────

        /// <summary>Round S1 F6: the gate the job charged under (silent; was GateTransition, which logged it). A
        /// charged gate that differs from the watch's gate for that sign is counted (DUMP): the job reads the
        /// sign's Efficiency buffer live, the watch the last recompute's efficiency, so a short disagreement around
        /// an efficiency change is expected. Round M2 (D16a): a result for a sign the primed watch no longer sees (it
        /// left between the schedule and the booking) is neither recorded nor counted, so it does not re-add the sign.</summary>
        private void RecordChargedGate(TicketSlot slot, SkiMoneyRules.TicketGate gate)
        {
            if (m_WatchPrimed && !m_WatchSeen.Contains(slot.Sign))
                return;
            m_LastGate[slot.Sign] = gate;
            if (m_WatchGate.TryGetValue(slot.Sign, out SkiMoneyRules.TicketGate watched) && watched != gate)
                m_GateMismatches++;
        }

        /// <summary>Round S1 F6 (ski-days spec ch3 §5; header GATE WATCH): when the attraction Generation, the lift
        /// charge Revision or the price moved since the last evaluation, the gate of every live sign with the
        /// hub's inputs; one line per sign whose gate changed (a new sign's first gate too); signs that left are
        /// dropped without a line. Main thread; plain reads of the two snapshots.</summary>
        private void WatchGates(int price)
        {
            int generation = m_Attractiveness.Generation;
            int revision = m_LiftCharge.Revision;
            if (m_WatchPrimed && generation == m_WatchGeneration && revision == m_WatchChargeRevision && price == m_WatchPrice)
                return;
            m_WatchPrimed = true;
            m_WatchGeneration = generation;
            m_WatchChargeRevision = revision;
            m_WatchPrice = price;

            m_Attractiveness.CopySigns(m_WatchSigns);
            m_WatchSeen.Clear();
            for (int i = 0; i < m_WatchSigns.Count; i++)
            {
                Entity sign = m_WatchSigns[i];
                m_WatchSeen.Add(sign);
                // Round M2 T1 (R2, thaw-rules.md item 2b): known = a score present AND not in the first-write hold;
                // the efficiency is the result's while a score is present (held or not), 1 without one (as before).
                bool has = m_Attractiveness.TryGetAreaScore(sign, out SkiResortAttractivenessSystem.AreaScoreView view);
                bool known = has && !view.WriteHeld;
                float efficiency = has ? (float)view.Result.Efficiency : 1f;
                int runs = known ? view.Result.RunsCounted : 0;
                // The lift-charge operating count is printed in the line's body only (no longer a gate input).
                int operating = m_LiftCharge.TryGetResort(sign, out SkiLiftChargeSystem.ResortLiftCharge rc) ? rc.Operating : 0;
                // = the hub's rule (SkiResortStatsSystem, the `live.TicketGate` line; C9)
                SkiMoneyRules.TicketGate gate = SkiMoneyRules.TicketGateOf(price, efficiency, known, runs);
                if (m_WatchGate.TryGetValue(sign, out SkiMoneyRules.TicketGate old) && old == gate)
                    continue;   // a new sign falls through: its first line
                m_WatchGate[sign] = gate;
                GateLine(sign, gate, efficiency, known, runs, operating);
            }
            // Round M2 (D16a, measure-core.md 2.13): the charged gates of signs that left go too (no allocation:
            // m_WatchDrop is reused; the DUMP lists live signs only). No Count precondition (unlike the m_WatchGate
            // prune below): a sign that left in the same evaluation a new, not yet charged sign appeared would keep
            // m_LastGate.Count <= m_WatchSeen.Count and stay listed. The walk is over a handful of signs, only on the
            // ticks the watch re-evaluates.
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

        /// <summary>ch4 §5.7's gate line, body and LogWindow unchanged (Round S1 F6: written by the watch).</summary>
        private void GateLine(Entity sign, SkiMoneyRules.TicketGate gate, float efficiency, bool runStateKnown, int openRuns,
                              int operatingLifts)
        {
            string state;
            switch (gate)
            {
                case SkiMoneyRules.TicketGate.Open: state = "open"; break;
                case SkiMoneyRules.TicketGate.Off: state = "closed — tickets off"; break;
                case SkiMoneyRules.TicketGate.NoEfficiency: state = "closed — Welcome Sign efficiency 0 %"; break;
                // Round M2 T1 (build-contract.md §6.8): the gate no longer opens on a running lift.
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

        /// <summary>The first paid ticket per ski area per load; reads PlayerMoney on the main thread once (rare).
        /// Catalog Phase B (D15): names the batch's lodge spend when there was one.</summary>
        private void FirstTicketLine(Entity sign, int people, int paid, int income, int spends, int spendIncome)
        {
            Entity city = m_CitySystem.City;
            string balance = city != Entity.Null && EntityManager.HasComponent<PlayerMoney>(city)
                ? EntityManager.GetComponentData<PlayerMoney>(city).money.ToString()
                : "n/a";
            string spend = spends > 0 ? $", lodge spend {spends} +{spendIncome}" : "";
            Mod.Log.Info($"Tickets [first] ski area {Fmt(sign)}: {people} guest(s), {paid} paid +{income}{spend} (balance {balance})");
        }

        /// <summary>Round M1 (M8, m1-spec F10): once the last `Tickets [cost]` line was written, the first tick at or
        /// after 12:00 of a new game day (SkiDayRules.DayKey) starts a new measurement. Returns true on the arming tick,
        /// which RecordCost leaves out.</summary>
        private bool RearmCostAtMidday()
        {
            if (!m_CostLogged || !(m_TimeSystem.normalizedTime >= 0.5f))
                return false;
            int day = SkiDayRules.DayKey(m_TimeSystem.year, m_TimeSystem.normalizedDate, m_TimeSystem.daysPerYear);
            if (day == m_CostArmDay)
                return false;
            m_CostArmDay = day;
            m_CostTicks = m_CostLargestBatch = 0;
            m_CostSumMs = m_CostMaxMs = 0.0;
            m_CostLogged = false;
            m_CostByRearm = true;   // Round M2 (D10): this measurement may end partial at the day change (CostDayEnd)
            return true;
        }

        /// <summary>Round M2 (D10, measure-core.md 2.10): a midday measurement still open when the game day changes
        /// is written partial, so the noon line is never silent — with the ticks it has ("the game day ended before
        /// 64"), or "skipped: no tick with arrivals since noon". m_CostArmDay stays the re-arm day, so the next noon
        /// re-arms. The load's measurement (m_CostByRearm false) keeps the M1 rule.</summary>
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
                m_CostSkipNext = false;   // Round M1 (M8): a busy arming tick is not measured
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
            if (m_TimeSystem.normalizedTime >= 0.5f)   // Round M1 (M8): a line written at or after 12:00 is today's (as SkiDaySystem)
                m_CostArmDay = SkiDayRules.DayKey(m_TimeSystem.year, m_TimeSystem.normalizedDate, m_TimeSystem.daysPerYear);
            Mod.Log.Info($"Tickets [cost] main thread per tick with arrivals: mean {m_CostSumMs / m_CostTicks:F3} ms, max " +
                         $"{m_CostMaxMs:F3} ms over {m_CostTicks} ticks; largest batch {m_CostLargestBatch} request(s)" +
                         SkiGuestCounterSystem.CostSuffix(m_CostMeasurements, m_TimeSystem));
        }

        /// <summary>DUMP lines (appends only; never throws).</summary>
        internal void AppendDump(StringBuilder sb)
        {
            try
            {
                sb.Append("  Tickets: armed ").Append(m_Armed).Append(", disabled ").Append(m_Disabled)
                  .Append(", price ").Append(Price).Append(", job in flight ").Append(m_JobInFlight)
                  .Append(", busy ticks ").Append(m_BusyTicks).Append(", requests ").Append(m_RequestsTotal)
                  .Append(" (largest batch ").Append(m_LargestBatch).Append("), guests ").Append(m_GuestsTotal)
                  .Append(", income +").Append(m_IncomeTotal)
                  .Append(" (booked as public-transport fees +").Append(m_FeeBookedTotal)   // Stage 1a
                  .Append(", booking ").Append(BookAsFees ? "fares" : "direct add").Append(')')
                  .Append(", lodge spend ").Append(LodgeSpend).Append(" per guest (× tier): ").Append(m_SpendsTotal)   // Catalog Phase B (D15)
                  .Append(" spent +").Append(m_SpendIncomeTotal)
                  .Append(", lag arrival → booked ").Append(m_LastLagTicks).Append(" tick(s) (max ").Append(m_MaxLagTicks)
                  .Append(") of 16 frames; gates (charged):");
                if (m_LastGate.Count == 0)
                    sb.Append(" none yet;");
                foreach (var pair in m_LastGate)
                    sb.Append(' ').Append(Fmt(pair.Key)).Append(' ').Append(pair.Value).Append(';');
                // Round S1 F6 (ch3 §5): the watch's gates and the disagreements.
                sb.Append(" watch:");
                if (m_WatchGate.Count == 0)
                    sb.Append(" none yet;");
                foreach (var pair in m_WatchGate)
                    sb.Append(' ').Append(Fmt(pair.Key)).Append(' ').Append(pair.Value).Append(';');
                sb.Append(" charged gate differed from the watch ").Append(m_GateMismatches).Append(" time(s)\n");
            }
            catch (Exception e)
            {
                sb.Append("  Tickets: dump failed (").Append(e.GetType().Name).Append(": ").Append(e.Message).Append(")\n");
            }
        }

        private static EntityRef Ref(Entity e) => new EntityRef(e.Index, e.Version);
        private static string Fmt(Entity e) => e.Index + ":" + e.Version;

        // ── the job (ch4 §5.5) ────────────────────────────────────────────────────────────────────

        /// <summary>Per slot: the sign's efficiency (inline BuildingUtils.cs:121-133) and the gate. Per request:
        /// a closed gate (or a City without PlayerMoney) books the guests free; an open one takes
        /// TicketsPaid × price from the household (never below 0) and credits the city once at the end (stage 1a: with
        /// BookAsFees the ticket total is instead one FeeEvent per paid ticket, credited by the budget, not here).
        /// Catalog Phase B (D15): after the ticket, a request with SpendPerPerson &gt; 0 also takes SpendPaid(paid,
        /// SpendPerPerson, money left) × SpendPerPerson from the same household (never below 0) into the same credit.</summary>
        [BurstCompile]
        private struct TicketJob : IJob
        {
            [ReadOnly] public NativeArray<TicketRequest> Requests;
            [ReadOnly] public NativeArray<TicketSlot> Slots;
            [ReadOnly] public BufferLookup<Efficiency> Efficiencies;
            public BufferLookup<GameResources> HouseholdResources;
            public ComponentLookup<PlayerMoney> PlayerMoneys;
            public NativeQueue<ServiceFeeSystem.FeeEvent> FeeQueue;   // Stage 1a: always valid (ScheduleJob); written only when BookAsFees
            public byte BookAsFees;                                   // Stage 1a: 1 = FeeEvents instead of the direct add of the ticket total
            public Entity City;
            public int Price;
            public NativeArray<TicketResult> Results;              // Requests.Length
            public NativeArray<byte> SlotGate;                     // Slots.Length

            public void Execute()
            {
                for (int s = 0; s < Slots.Length; s++)
                {
                    TicketSlot slot = Slots[s];
                    float efficiency = EfficiencyOf(slot.Sign);
                    // Round M2 T1: four arguments (no operating lifts); RunStateKnown already excludes the load hold.
                    SlotGate[s] = (byte)SkiMoneyRules.TicketGateOf(Price, efficiency, slot.RunStateKnown != 0, slot.OpenRuns);
                }
                bool cityOk = City != Entity.Null && PlayerMoneys.HasComponent(City);
                long credited = 0;
                for (int i = 0; i < Requests.Length; i++)
                {
                    TicketRequest q = Requests[i];
                    var r = new TicketResult { NoCity = (byte)(cityOk ? 0 : 1) };
                    int people = math.max(0, q.People);
                    if (SlotGate[q.Slot] != (byte)SkiMoneyRules.TicketGate.Open || !cityOk)
                    {
                        r.Free = people;
                        Results[i] = r;
                        continue;
                    }
                    int paid = 0;
                    if (HouseholdResources.HasBuffer(q.Household))
                    {
                        DynamicBuffer<GameResources> buffer = HouseholdResources[q.Household];
                        int money = EconomyUtils.GetResources(Resource.Money, buffer);
                        paid = SkiMoneyRules.TicketsPaid(people, Price, money);
                        if (paid > 0)
                        {
                            r.Income = paid * Price;                   // paid ≤ money / price: no overflow, never below 0
                            EconomyUtils.AddResources(Resource.Money, -r.Income, buffer);
                            if (BookAsFees != 0)
                            {
                                // Stage 1a: one fee per paid ticket, as the bus fare (ResidentAISystem.cs:3929-3935); the budget
                                // credits the fee line to PlayerMoney (BudgetApplySystem.cs:57-59), so no direct add here.
                                r.Booked = r.Income;
                                for (int k = 0; k < paid; k++)
                                    FeeQueue.Enqueue(new ServiceFeeSystem.FeeEvent
                                    {
                                        m_Amount = 1f,
                                        m_Cost = Price,
                                        m_Resource = PlayerResource.PublicTransport,
                                        m_Outside = false,
                                    });
                            }
                            else
                                credited += r.Income;
                            // Catalog Phase B (D15): the lodge spend of the paying guests, from what the tickets left.
                            if (q.SpendPerPerson > 0)
                            {
                                int spends = SkiMoneyRules.SpendPaid(paid, q.SpendPerPerson, money - r.Income);
                                if (spends > 0)
                                {
                                    r.Spends = spends;
                                    r.SpendIncome = spends * q.SpendPerPerson;   // ≤ money left: no overflow, never below 0
                                    EconomyUtils.AddResources(Resource.Money, -r.SpendIncome, buffer);
                                    credited += r.SpendIncome;
                                }
                            }
                        }
                    }
                    r.Paid = paid;
                    r.Unpaid = people - paid;
                    Results[i] = r;
                }
                if (credited > 0)
                {
                    PlayerMoney money = PlayerMoneys[City];
                    money.Add((int)math.min(credited, (long)int.MaxValue));
                    PlayerMoneys[City] = money;
                }
            }

            /// <summary>BuildingUtils.GetEfficiency(DynamicBuffer) re-implemented (e1-spec I-28): product of
            /// max(0, f); 0 when not > 0; else max(0.01, round(100p)/100); no buffer → 1.</summary>
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
