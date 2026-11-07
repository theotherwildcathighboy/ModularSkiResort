# SkiLiftTicketSystem: how lift tickets, passes, lessons and lodge spend are charged and booked

Each arriving guest (`SkiGuestCounterSystem.DrainArrivals`) pays the ticket price from their household into the city's balance when
the gate is open (`SkiMoneyRules.TicketGateOf`) and the household can pay. Never into debt: `SkiMoneyRules.TicketsPaid = min(people, money / price)`.
The system admits the rest free and counts them as unpaid. Low money makes residents move away (HouseholdBehaviorSystem.cs:172), and
hotel tourists leave (TouristLeaveSystem.cs:60-73).

## Money movement

It follows the vanilla transit fare (ResidentAISystem.BoardingJob : IJob, ResidentAISystem.cs:3393). The fare at :3922-3935 has three
parts:

- `EconomyUtils.AddResources(Resource.Money, -price, household resources)`
- `PlayerMoney.Add` on the City entity
- a `ServiceFeeSystem.FeeEvent{m_Amount 1, m_Cost price, PlayerResource.PublicTransport, m_Outside false}` on the fee queue

One Burst IJob runs per tick that has arrivals (EconomyUtils.cs:307-338, PlayerMoney.cs:33-36). Money moves. None is created or lost. A
City without PlayerMoney charges nobody: guests go free, with one WARN per load.

## Booked as public transportation fees

This is always on (the Options switch and the direct-add path were removed 2026-11-05, DECISIONS 607). Each paid lift ticket, season pass,
lesson and lodge spend enqueues one FeeEvent like the bus fare, so the income shows in the Economy panel. The job does NOT add the total
to PlayerMoney. BudgetApplySystem.cs:28-60 credits the fee line (CityServiceBudgetSystem.cs:225-226) into PlayerMoney itself. So adding
it here too would pay the city twice. The system handles the fee queue and its dependency as ResidentAISystem does (:265, :277, :285,
ServiceFeeSystem.cs:443-452). The jobs read PlayerMoney only to check that the City has it (no PlayerMoney: guests booked free).

Unverified in game:

- that the Economy panel line rises and the treasury is credited once
- that fee income reaches the treasury at all, with Realistic Workplaces And Households ("RWH") loaded (one WARN per load). RWH replaces
  BudgetApplySystem with its own copy.

## The gate

The first match wins:

1. Off (price 0)
2. NoEfficiency (the sign's efficiency is not > 0)
3. NotKnownYet (no score yet, or the area's first score after a load is still held: `AreaScoreView.WriteHeld`)
4. Open on a counted run (RunsCounted), else NothingOpen.

A running lift alone does not open the gate. Free entry during a load hold is accepted (at most RatingHoldMaxTicks = 1,024 ticks). The
inputs per ski area with arrivals ("slot") are the hub's source and rule (SkiResortStatsSystem's ticket gate). The system computes
efficiency IN THE JOB from a `BufferLookup<Efficiency>` (the BuildingUtils.cs:121-133 formula). So the main thread never waits on the many
jobs that write Efficiency. The code does not call `BuildingUtils.GetEfficiency(DynamicBuffer)`, because its overload set includes
GetEfficiency(Span<float>): CS7069 on net48.

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

The lookups (Efficiency RO, Resources RW, PlayerMoney RW) make ECS order the job after every writer of household money or PlayerMoney.
They also make ECS order every later writer after the job.

## Phase, cadence and cost

The system runs in GameSimulation, anchored `UpdateAfter<SkiLiftTicketSystem, SkiGuestCounterSystem>` (Mod.cs). The anchor is not
load-bearing: the counter's pending list is DRAINED, so a different offset neither loses nor repeats an arrival. Pause stops the system.
The Editor never arms it. Cadence: GetUpdateInterval 16 (the counter's interval).

Cost: `Tickets [cost]` after Tuning.CostTicks ticks with arrivals.

- The system re-arms once per game day at the first tick at or after 12:00 (SkiDayRules.DayKey), behind Options > Diagnostics.
- It does not measure a busy arming tick.
- It writes a midday measurement partial if the measurement is not finished when the game day ends.

There is no synchronous part: nothing here waits on the job. The load-time measurement never writes when no guest arrives after a load.

## Gate watch

Step 2b runs every tick, whether or not guests arrive. When the attraction Generation, the lift-charge Revision or the price moved, the watch computes one gate per live sign. It uses `SkiMoneyRules.TicketGateOf` with exactly the hub's inputs (SkiResortStatsSystem, the `live.TicketGate` line). A changed gate (or a new sign) writes one `Tickets [gate]` line through m_GateLog. The LogWindow is 10 s, because a counted run can flip with the snow. The watch silently drops a sign that left, and prunes it from the charged gates too. Releasing the first-write
hold moves the attraction Generation, so the watch leaves NotKnownYet on that pass. The system records the job's charged gate per sign
(DUMP "gates (charged)"). It counts each disagreement with the watch and does not log it.

## Contract (house pattern)

- A "created" line.
- A per-load reset in OnGameLoadingComplete.
- OnGamePreload completes the job and books its results first (money already moved must reach the ledger before it resets). Then it
  flushes the gate fold (try/caught: GameSystemBase.cs:85-96).
- The guest counter's OnGamePreload runs first. It books that job through BookInFlightForPreload before it writes the `(until the load)`
  month lines.
- OnDestroy completes and disposes.

## Lodge spend

Lodge spend is optional. The setting is Options -> Balance, 0-50, default 15, 0 = off. The system reads it live
(`SkiMoneyRules.ClampLodgeSpend`).

- Per request, on the main thread: SpendPerPerson = `SkiMoneyRules.LodgeSpendOf(setting, the lodge row's SpendMultiplier)` when the
  arrival's building is a lodge (the prefab catalog's LodgeDefs view), else 0. It is a plain int, so the Burst job reads no catalog row.
- In the job: only for a PAYING guest of an OPEN gate (or a pass holder), AFTER the ticket and the lesson. The job uses
  `SkiMoneyRules.SpendPaid(paid, spend per person, money left)`. It never goes into debt. It books the spend like the ticket (Book) and
  records it with AddLodgeSpend.

Price 0 = gate Off = nothing charged. Setting 0 = no change at all.

## Season passes

The job decides ONCE per season for a RESIDENT household (request.Tourist == 0) at an OPEN gate with no standing for this season. It decides at the household's first such arrival. No standing means SkiSeasonPass is absent or of an older season (`SkiMoneyRules.PassStandingOf`). The exception: the job skips the decision
when the pre-season sale decided it already. The decision is `SkiMoneyRules.PassSale(share, household index, season key,
PassPrice(multiplier, the lift ticket now), household money)`. The outcomes are:

- Bought -> the job takes the price from the household (never below 0). The arrival's people ski free (ledger PassHolders). The job books
  it like tickets (Book). FeeBookedThisLoad = tickets + passes + lessons + lodge spend.
- Declined / CannotPay -> lift tickets as before and a Declined standing (not asked again this season).
- Share 0 / a tourist = nothing written.

A holder pays 0 at the gate for the season and still takes lessons and spends at the lodge.

The job only READS the component (ComponentLookup) and appends PassWrite rows. ApplyResults (step 1 of the next tick, main thread) adds
or overwrites SkiSeasonPass. So no structural change happens in the job. A Temp map inside the job remembers a household that arrives
twice in one batch. Known limit: a save made between a purchase (job) and the next tick's step 1 (<= 16 frames) loses the component. The
household then rolls the same way again and pays twice.

Pre-season pass sale: `SkiLiftTicketSystem.Presale.cs` (its header says how the pool is built and why a sale tick does not drain
arrivals). Step 1b applies it. Step 2c starts it. It feeds the same pass totals.

## Ski lessons

This applies to a counted arrival at an OPEN gate (pass holder or not, resident or tourist). The ski area's Welcome Sign must have an ACTIVE ski school (Result.SchoolActive: installed, not BuildingOption.Inactive). Then each person counted gets one roll at LessonShare %. The roll is
`SkiMoneyRules.LessonRoll`, a stateless hash of household index, SkiDayRules.DayKey and the person's index. A taker pays LessonPrice =
LessonMultiplier x the lift ticket now. The taker pays from what remains of the household's money after the ticket or pass
(`SkiMoneyRules.LessonsPaid`), BEFORE the lodge spend. The system books the lesson like tickets (Book).
