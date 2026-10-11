// SkiLiftTicketSystem — the lift ticket (Round E1, decision D26; docs/research/econ-round/spec/
// ch4-money.md §5, §10): each arriving guest (SkiGuestCounterSystem.DrainArrivals) pays the ticket price
// from their household into the city's balance, when the gate is open (SkiMoneyRules.TicketGateOf,
// e1-spec R6) and the household can pay (never into debt, DV9). Stage 0 (S0) laid down the shell
// (e1-spec I-19); stream S4 wrote the system (2026-10-04).
//
// THE MONEY MOVEMENT (ch4 §5.1): the vanilla transit-fare pattern (ResidentAISystem.BoardingJob : IJob,
// ResidentAISystem.cs:3393; the fare at :3922-3935 = EconomyUtils.AddResources(Resource.Money, -price,
// household resources) + PlayerMoney.Add on the City entity in the same job). One Burst IJob per tick that
// has arrivals: EconomyUtils.GetResources / AddResources (EconomyUtils.cs:307-338) on the household's
// Game.Economy.Resources buffer and one PlayerMoney.Add(total) (PlayerMoney.cs:33-36). Money moves; none is
// created and none is lost (a City without PlayerMoney charges nobody: guests go free, one WARN per load).
// Two deliberate differences from the fare: NO FeeEvent (PlayerResource is a closed enum with no parks or
// tourism entry — any choice would book the ticket under a wrong vanilla line), and NEVER INTO DEBT
// (DV9): SkiMoneyRules.TicketsPaid = min(people, money / price), the rest are guests who could not pay
// (counted, admitted, no money): low money makes residents move away (HouseholdBehaviorSystem.cs:172) and
// hotel tourists leave (TouristLeaveSystem.cs:60-73).
//
// THE GATE (ch4 §5.3, e1-spec R6), first match wins: Off (price 0) → NoEfficiency (the sign's efficiency
// not > 0) → Open on an operating lift → NotKnownYet (no score result yet, DV10) → Open on a counted run
// (RunsCounted, runs worth more than 0), else NothingOpen. Inputs per ski area with arrivals ("slot"):
// SkiResortAttractivenessSystem.TryGetAreaScore → (true, Result.RunsCounted) else (false, 0);
// SkiLiftChargeSystem.TryGetResort(sign).Operating else 0 — the same two sources the hub uses (ch5 §5.4).
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
// (SkiGuestCounterSystem.CostSuffix). No synchronous part: the job is never waited on here.
// LOGS (ch4 §5.7, exact): created; ARMED; `Tickets [price]` on a change; `Tickets [gate]` per ski area when its
// gate changes, evaluated each tick its inputs move (attraction Generation, lift-charge Revision, price) with the
// hub's inputs (Round S1 F6, ski-days spec ch3 §5: before, the line came only with arriving guests — field test
// 8's "open" was 4 min 26 s late), the first evaluation of a sign too, through m_GateLog (LogWindow 10 s; R15:
// the operating-lift count can flip with a brownout), folds flushed on every tick (the idle tick included), in
// OnGamePreload and in OnDestroy; `Tickets [first]` per ski area per load; `Tickets [cost]` after the load and daily
// at midday (Round M1); the one-shot
// `!!!` (disabled for the load). Month totals are in the counter's `Guests [month]` line.
// GATE WATCH (Round S1 F6): step 2b of every tick — when Generation, Revision or the price moved, one gate per live
// sign (SkiResortAttractivenessSystem.CopySigns) from SkiMoneyRules.TicketGateOf(price, Result.Efficiency else 1,
// known, RunsCounted, Operating), exactly the hub's rule (SkiResortStatsSystem.cs:1877-1878); a changed gate (or a
// new sign) writes one line; a sign that left is dropped silently. The job's charged gate is no longer logged: it
// is recorded per sign (DUMP "gates (charged)") and each disagreement with the watch is counted (DUMP).
// CONTRACT (house pattern): "created" line; per-load reset in OnGameLoadingComplete; OnGamePreload completes
// the job and books its results first (money already moved must reach the ledger before it resets), then
// flushes the gate fold (try/caught: GameSystemBase.cs:85-96); since the E1 fixer pass 2 the guest counter's
// OnGamePreload, which runs first, books that job through BookInFlightForPreload before it writes the
// `(until the load)` month lines; OnDestroy completes and disposes.

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

        private struct TicketRequest { public int Slot; public Entity Household; public int People; public byte Tourist; }
        private struct TicketSlot { public Entity Sign; public byte RunStateKnown; public int OpenRuns, OperatingLifts; }
        private struct TicketResult { public int Paid, Unpaid, Free, Income; public byte NoCity; }

        /// <summary>The clamped setting read on the last tick (SkiMoneyRules.ClampTicketPrice).</summary>
        internal int Price { get; private set; } = SkiMoneyRules.TicketPriceDefault;

        /// <summary>The gate the last ticket tick evaluated for <paramref name="sign"/>.</summary>
        internal bool TryGetLastGate(Entity sign, out SkiMoneyRules.TicketGate gate)
        {
            return m_LastGate.TryGetValue(sign, out gate);
        }

        // ── systems and lookups ───────────────────────────────────────────────────────────────────
        private SimulationSystem m_SimulationSystem;
        private TimeSystem m_TimeSystem;   // Round M1 (M8): the midday re-arm and the [cost] clock
        private CitySystem m_CitySystem;
        private SkiGuestCounterSystem m_Counter;
        private SkiResortAttractivenessSystem m_Attractiveness;
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
        private bool m_Armed, m_Disabled, m_ErrorLogged, m_NoCityWarned;
        private long m_BusyTicks, m_RequestsTotal, m_GuestsTotal, m_IncomeTotal;
        private int m_LargestBatch, m_MaxLagTicks, m_LastLagTicks;
        private int m_CostTicks, m_CostLargestBatch;
        private double m_CostSumMs, m_CostMaxMs;
        private bool m_CostLogged;
        private bool m_CostSkipNext;               // Round M1 (M8): true on the arming tick only; RecordCost leaves that tick out
        private int m_CostArmDay = int.MinValue;   // Round M1: SkiDayRules.DayKey of the last midday re-arm or afternoon line
        private int m_CostMeasurements;            // Round M1: `Tickets [cost]` lines this load

        protected override void OnCreate()
        {
            base.OnCreate();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_TimeSystem = World.GetOrCreateSystemManaged<TimeSystem>();   // Round M1 (M8)
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_Counter = World.GetOrCreateSystemManaged<SkiGuestCounterSystem>();
            m_Attractiveness = World.GetOrCreateSystemManaged<SkiResortAttractivenessSystem>();
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
            m_BusyTicks = m_RequestsTotal = m_GuestsTotal = m_IncomeTotal = 0;
            m_LargestBatch = m_MaxLagTicks = m_LastLagTicks = 0;
            m_CostTicks = m_CostLargestBatch = 0;
            m_CostSumMs = m_CostMaxMs = 0.0;
            m_CostLogged = false;
            m_CostSkipNext = false;        // Round M1 (M8)
            m_CostArmDay = int.MinValue;
            m_CostMeasurements = 0;
            Price = ReadPrice();

            if (mode != GameMode.Game)
                return;

            m_Armed = true;
            Mod.Log.Info($"Tickets: ARMED — price {Price} (setting LiftTicketPrice, 0 = off; read live); each counted guest " +
                         "pays once on arrival, taken from the guest's household and added to the city's balance like the " +
                         "vanilla transit fare (ResidentAISystem BoardingJob) without a FeeEvent; a household pays for as many " +
                         "members as its money covers, never below 0; gate = Welcome Sign efficiency above 0 and an open run " +
                         "or an operating lift; one job per 16-frame tick with arrivals");
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !m_Armed)
                    return;
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
                BuildSlots();

                // 4. Tickets off: every arrival is booked free, no job.
                if (price == 0)
                    BookAllFree();
                else
                    ScheduleJob(price);

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

        // ── slots and the job ─────────────────────────────────────────────────────────────────────

        /// <summary>One slot per distinct sign of this tick's arrivals (gate inputs, e1-spec R6) and one request
        /// per arrival.</summary>
        private void BuildSlots()
        {
            m_SlotOf.Clear();
            m_Slots.Clear();
            m_Requests.Clear();
            for (int i = 0; i < m_Arrivals.Count; i++)
            {
                GuestArrival a = m_Arrivals[i];
                if (!m_SlotOf.TryGetValue(a.Sign, out int slot))
                {
                    slot = m_Slots.Length;
                    m_SlotOf[a.Sign] = slot;
                    bool known = m_Attractiveness.TryGetAreaScore(a.Sign, out SkiResortAttractivenessSystem.AreaScoreView view);
                    int operating = m_LiftCharge.TryGetResort(a.Sign, out SkiLiftChargeSystem.ResortLiftCharge rc) ? rc.Operating : 0;
                    m_Slots.Add(new TicketSlot
                    {
                        Sign = a.Sign,
                        RunStateKnown = (byte)(known ? 1 : 0),
                        OpenRuns = known ? view.Result.RunsCounted : 0,
                        OperatingLifts = operating,
                    });
                }
                m_Requests.Add(new TicketRequest
                {
                    Slot = slot,
                    Household = a.Household,
                    People = a.People,
                    Tourist = (byte)(a.Tourist ? 1 : 0),
                });
            }
        }

        private void ScheduleJob(int price)
        {
            int requests = m_Requests.Length, slots = m_Slots.Length;
            m_Results.ResizeUninitialized(requests);
            m_SlotGate.ResizeUninitialized(slots);
            m_EfficiencyLookup.Update(this);
            m_ResourcesLookup.Update(this);
            m_PlayerMoneyLookup.Update(this);
            var job = new TicketJob
            {
                Requests = m_Requests.AsArray(),
                Slots = m_Slots.AsArray(),
                Efficiencies = m_EfficiencyLookup,
                HouseholdResources = m_ResourcesLookup,
                PlayerMoneys = m_PlayerMoneyLookup,
                City = m_CitySystem.City,
                Price = price,
                Results = m_Results.AsArray(),
                SlotGate = m_SlotGate.AsArray(),
            };
            JobHandle handle = job.Schedule(Dependency);
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
                if (q.Tourist == 0)
                    m_Counter.AddLocalsPaid(sign, r.Paid);
                m_GuestsTotal += Math.Max(0, q.People);
                m_IncomeTotal += r.Income;
                noCity |= r.NoCity != 0;
                if (!quiet && r.Paid > 0 && m_FirstPaid.Add(sign))
                    FirstTicketLine(sign, q.People, r.Paid, r.Income);
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
        /// an efficiency change is expected.</summary>
        private void RecordChargedGate(TicketSlot slot, SkiMoneyRules.TicketGate gate)
        {
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
                bool known = m_Attractiveness.TryGetAreaScore(sign, out SkiResortAttractivenessSystem.AreaScoreView view);
                float efficiency = known ? (float)view.Result.Efficiency : 1f;
                int runs = known ? view.Result.RunsCounted : 0;
                int operating = m_LiftCharge.TryGetResort(sign, out SkiLiftChargeSystem.ResortLiftCharge rc) ? rc.Operating : 0;
                // = the hub's rule (SkiResortStatsSystem.cs:1877-1878, C9)
                SkiMoneyRules.TicketGate gate = SkiMoneyRules.TicketGateOf(price, efficiency, known, runs, operating);
                if (m_WatchGate.TryGetValue(sign, out SkiMoneyRules.TicketGate old) && old == gate)
                    continue;   // a new sign falls through: its first line
                m_WatchGate[sign] = gate;
                GateLine(sign, gate, efficiency, known, runs, operating);
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
                case SkiMoneyRules.TicketGate.NothingOpen: state = "closed — no open run and no operating lift"; break;
                default: state = "closed — run state not known yet, no operating lift"; break;
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

        /// <summary>The first paid ticket per ski area per load; reads PlayerMoney on the main thread once (rare).</summary>
        private void FirstTicketLine(Entity sign, int people, int paid, int income)
        {
            Entity city = m_CitySystem.City;
            string balance = city != Entity.Null && EntityManager.HasComponent<PlayerMoney>(city)
                ? EntityManager.GetComponentData<PlayerMoney>(city).money.ToString()
                : "n/a";
            Mod.Log.Info($"Tickets [first] ski area {Fmt(sign)}: {people} guest(s), {paid} paid +{income} (balance {balance})");
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
            return true;
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
        /// TicketsPaid × price from the household (never below 0) and credits the city once at the end.</summary>
        [BurstCompile]
        private struct TicketJob : IJob
        {
            [ReadOnly] public NativeArray<TicketRequest> Requests;
            [ReadOnly] public NativeArray<TicketSlot> Slots;
            [ReadOnly] public BufferLookup<Efficiency> Efficiencies;
            public BufferLookup<GameResources> HouseholdResources;
            public ComponentLookup<PlayerMoney> PlayerMoneys;
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
                    SlotGate[s] = (byte)SkiMoneyRules.TicketGateOf(Price, efficiency, slot.RunStateKnown != 0,
                                                                   slot.OpenRuns, slot.OperatingLifts);
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
                        paid = SkiMoneyRules.TicketsPaid(people, Price, EconomyUtils.GetResources(Resource.Money, buffer));
                        if (paid > 0)
                        {
                            r.Income = paid * Price;                   // paid ≤ money / price: no overflow, never below 0
                            EconomyUtils.AddResources(Resource.Money, -r.Income, buffer);
                            credited += r.Income;
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
