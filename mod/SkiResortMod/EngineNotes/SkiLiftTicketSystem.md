# SkiLiftTicketSystem: how lift tickets, passes, lessons and lodge spend are charged and booked

Each arriving guest (`SkiGuestCounterSystem.DrainArrivals`) pays the ticket price from their household into the city's balance when
the gate is open (`SkiMoneyRules.TicketGateOf`) and the household can pay. Never into debt: `SkiMoneyRules.TicketsPaid =
min(people, money / price)`; the rest are admitted free and counted as unpaid (low money makes residents move away,
HouseholdBehaviorSystem.cs:172, and hotel tourists leave, TouristLeaveSystem.cs:60-73).

## Money movement

It follows the vanilla transit fare (ResidentAISystem.BoardingJob : IJob, ResidentAISystem.cs:3393; the fare at :3922-3935 =
`EconomyUtils.AddResources(Resource.Money, -price, household resources)` + `PlayerMoney.Add` on the City entity + a
`ServiceFeeSystem.FeeEvent{m_Amount 1, m_Cost price, PlayerResource.PublicTransport, m_Outside false}` on the fee queue). One
Burst IJob per tick that has arrivals (EconomyUtils.cs:307-338, PlayerMoney.cs:33-36). Money moves; none is created or lost (a City
without PlayerMoney charges nobody: guests go free, one WARN per load).

## Booked as public transportation fees

`BookTicketsAsFees` (Options -> Balance, default on, read live): each paid lift ticket, season pass, lesson and lodge spend
enqueues one FeeEvent like the bus fare, so the income shows in the Economy panel, and its total is NOT added to PlayerMoney in
the job: BudgetApplySystem.cs:28-60 credits the fee line (CityServiceBudgetSystem.cs:225-226) into PlayerMoney itself, so adding
it here too would pay the city twice. The fee queue and its dependency are handled as ResidentAISystem does (:265, :277, :285;
ServiceFeeSystem.cs:443-452), also with the setting off, so the job struct never holds a default NativeQueue. Setting off = direct
PlayerMoney.Add, no FeeEvent.

Unverified in game: that the Economy panel line rises and the treasury is credited once; with Realistic Workplaces And Households
("RWH") loaded, which replaces BudgetApplySystem with its own copy, that fee income reaches the treasury at all (one WARN per
load).

## The gate

First match wins: Off (price 0) -> NoEfficiency (the sign's efficiency not > 0) -> NotKnownYet (no score yet, or the area's first
score after a load still held: `AreaScoreView.WriteHeld`) -> Open on a counted run (RunsCounted), else NothingOpen. A running lift
alone does not open it; free entry during a load hold is accepted (at most RatingHoldMaxTicks = 1,024 ticks). Inputs per ski area
with arrivals ("slot") are the hub's source and rule (SkiResortStatsSystem's ticket gate). Efficiency is computed IN THE JOB from a
`BufferLookup<Efficiency>` (the BuildingUtils.cs:121-133 formula), so the main thread never waits on the many jobs that write
Efficiency; `BuildingUtils.GetEfficiency(DynamicBuffer)` is not called (its overload set includes GetEfficiency(Span<float>):
CS7069 on net48).

## Step order per tick

Try/catch around all of it.

1. Complete last tick's job and book its results into the ledger.
   - 1b: apply last tick's pre-season sale.
2. Read the settings live.
   - 2b: the gate watch.
   - 2c: start a pre-season sale.
3. Drain the counter's arrivals (none -> return).
4. Price 0 -> every arrival is booked free (no job).
5. One slot per distinct sign.
6. Fill the persistent lists and schedule TicketJob on Dependency.

The lookups (Efficiency RO, Resources RW, PlayerMoney RW) make ECS order the job after every writer of household money or
PlayerMoney, and every later writer after it.

## Phase, cadence and cost

GameSimulation, anchored `UpdateAfter<SkiLiftTicketSystem, SkiGuestCounterSystem>` (Mod.cs; not load-bearing: the counter's
pending list is DRAINED, so a different offset neither loses nor repeats an arrival). Pause stops it; the Editor never arms it.
Cadence: GetUpdateInterval 16 (the counter's interval).

Cost: `Tickets [cost]` after Tuning.CostTicks ticks with arrivals; re-armed once per game day at the first tick at or after 12:00
(SkiDayRules.DayKey) behind Options > Diagnostics; a busy arming tick is not measured; a midday measurement not finished when the
game day ends is written partial. No synchronous part: the job is never waited on here. The load-time measurement never writes
when no guest arrives after a load.

## Gate watch

Step 2b of every tick, whether or not guests arrive: when the attraction Generation, the lift-charge Revision or the price moved,
one gate per live sign from `SkiMoneyRules.TicketGateOf` with exactly the hub's inputs (SkiResortStatsSystem, the
`live.TicketGate` line); a changed gate (or a new sign) writes one `Tickets [gate]` line through m_GateLog (LogWindow 10 s: a
counted run can flip with the snow); a sign that left is dropped silently, and pruned from the charged gates too. Releasing the
first-write hold moves the attraction Generation, so the watch leaves NotKnownYet on that pass. The job's charged gate is recorded
per sign (DUMP "gates (charged)") and each disagreement with the watch is counted, not logged.

## Contract (house pattern)

"created" line; per-load reset in OnGameLoadingComplete; OnGamePreload completes the job and books its results first (money
already moved must reach the ledger before it resets), then flushes the gate fold (try/caught: GameSystemBase.cs:85-96); the guest
counter's OnGamePreload, which runs first, books that job through BookInFlightForPreload before it writes the `(until the load)`
month lines; OnDestroy completes and disposes.

## Lodge spend

Optional; Options -> Balance, 0-50, default 15, 0 = off; read live, `SkiMoneyRules.ClampLodgeSpend`. Per request on the main
thread, SpendPerPerson = `SkiMoneyRules.LodgeSpendOf(setting, the lodge row's SpendMultiplier)` when the arrival's building is a
lodge (the prefab catalog's LodgeDefs view), else 0, a plain int so the Burst job reads no catalog row. In the job, only for a
PAYING guest of an OPEN gate (or a pass holder), AFTER the ticket and the lesson: `SkiMoneyRules.SpendPaid(paid, spend per person,
money left)`, never into debt, booked like the ticket (Book) and recorded with AddLodgeSpend. Price 0 = gate Off = nothing
charged. Setting 0 = no change at all.

## Season passes

A RESIDENT household (request.Tourist == 0) at an OPEN gate with no standing for this season (SkiSeasonPass absent or of an older
season, `SkiMoneyRules.PassStandingOf`) is decided ONCE per season at its first such arrival, in the job, unless the pre-season
sale decided it already: `SkiMoneyRules.PassSale(share, household index, season key, PassPrice(multiplier, the lift ticket now),
household money)`. Bought -> the price is taken from the household (never below 0), the arrival's people ski free (ledger
PassHolders), booked like tickets (Book); FeeBookedThisLoad = tickets + passes + lessons + lodge spend. Declined / CannotPay ->
lift tickets as before and a Declined standing (not asked again this season). Share 0 / a tourist = nothing written. A holder pays
0 at the gate for the season and still takes lessons and spends at the lodge.

The job only READS the component (ComponentLookup) and appends PassWrite rows; ApplyResults (step 1 of the next tick, main
thread) adds or overwrites SkiSeasonPass, so no structural change happens in the job, and a household arriving twice in one batch
is remembered by a Temp map inside the job. Known limit: a save made between a purchase (job) and the next tick's step 1 (<= 16
frames) loses the component; the household then rolls the same way again and pays twice.

Pre-season pass sale: `SkiLiftTicketSystem.Presale.cs` (its header says how the pool is built and why a sale tick does not drain
arrivals); step 1b applies it, step 2c starts it; it feeds the same pass totals.

## Ski lessons

A counted arrival at an OPEN gate (pass holder or not, resident or tourist) of a ski area whose Welcome Sign has an ACTIVE ski
school (Result.SchoolActive: installed, not BuildingOption.Inactive) gives each person counted one roll (`SkiMoneyRules.LessonRoll`:
a stateless hash of household index, SkiDayRules.DayKey and the person's index) at LessonShare %; a taker pays LessonPrice =
LessonMultiplier x the lift ticket now, from what the household has left after the ticket or pass (`SkiMoneyRules.LessonsPaid`),
BEFORE the lodge spend. Booked like tickets (Book).
