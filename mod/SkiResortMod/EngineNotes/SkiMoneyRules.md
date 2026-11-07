# SkiMoneyRules: the money rules (lift charge, tickets, passes, lessons, guest days)

The rules `SkiMoneyRules` implements, by topic. All of it is pure static code. The callers supply the rate numbers.

## Lift charge and billing reductions

- Lift charge: Full = `LiftFixedPerMonth` + `LiftPerKmPerMonth` x km. A lift bills `LiftReducedShare` of the Full charge in either of these cases:
  - The lift is not complete.
  - The lift is complete, and its ski area has no counted run (`LiftAreaRunOpen` false: the area's released score counts 0 runs).
- The charge bills through `LiftUpkeepBillingOf`, so a missing power connection does not reduce it (an unpowered lift still costs
  its maintenance). `LiftOperating(complete, powered)` is the charge's operating count (logs and the hub only).
- Billing reductions (each `LiftReducedShare`, 25 %): `Unfinished` (any census state but Complete) and `NoOpenRun` (complete, its
  ski area counts no run). A run counts only when its value, the snow open factor included, is > 0 (SkiResortScore.cs:326-338,
  :369-380). So NoOpenRun applies in every period without snow at any backcountry share. With backcountry 0, it also applies to an
  area whose only serving lift is unpowered.
- An unpowered complete lift is billed like every complete lift of its area. That is in full while the area counts a run, and 25 % while it
  counts none. `LiftOperating` and the charge's Operating count keep the real power. The `NoPower` billing stays bound but is
  unreachable from the charge. `NoOpenRun` is not bound to the UI (no gate-rule R17 entry).
- The caller supplies the rate numbers (`LiftFullRate(lengthM, fixedPerMonth, perKmPerMonth)`, `LiftRate(billing, lengthM,
  fixed, perKm)`). Nothing here reads a `SkiCatalog` row. The one-length overloads are the fixed-grip consts.

## Accrual

The run-upkeep pattern (`SkiRunUpkeepSystem`'s month = TicksPerDay x daysPerYear / 12 sim frames. `TimeSystem.kTicksPerDay`,
Game.Simulation/TimeSystem.cs:18. The ECS systems assert it at arm time): carry += rate x elapsed / month. The whole credits are
debited, and the fraction is carried.

## Ticket gate

The gate checks these states in order:
1. Off (price 0).
2. NoEfficiency (sign efficiency not > 0).
3. NotKnownYet (no score yet, or the area's first score after a load still held).
4. Open on a counted run, else NothingOpen (a running lift alone does not open it).

Gate rule R17 compares `TicketGate`'s
literal values with bindings.ts `TICKET_GATE`.

## Lodge spend

- `LodgeSpendDefault` / `Max` / `Step` alias `SkiCatalog.LodgeSpend`.
- `ClampLodgeSpend`.
- `LodgeSpendOf(setting, multiplier)` = floor(setting x multiplier + 0.5) (0 when either is not > 0).
- `SpendPaid(paid, spendPerPerson, moneyAfterTickets)` = min(paid, money / spend) (0 when any is not > 0). The ticket job charges what a
  household has left after its tickets, never into debt.

## Months

`MonthKey` = year x 12 + calendar month 0..11. `MonthOfKey` is 1..12. A month with fewer than `FullMonthShare` (95 %) of its frames
simulated is short (`IsShortMonth`) and booked as partial.

## Guest days

Per ski area and game day, a tourist on leisure at a lodge counts once, but not when its household came by outing that day. An outing
counts only the members not yet counted there that day. The household's day flags are `GuestDayOuting | GuestDayLeisure`. The day key
is `SkiDayRules.DayKey`. The day sets live in the counter.

## Season passes and lessons

- Season passes: `SeasonKeyOf` / `SeasonKeyOfDate` (July on = the year, else year - 1), `PassPrice`, `PassRoll` (the household's one
  deterministic roll), `PassStandingOf` (what its `SkiSeasonPass` says this season), `PassSale` (NotEligible / Declined / Bought /
  CannotPay). The ticket job (Burst) calls them.
- Lessons: `LessonShare` / `LessonMultiplier` consts and clamps, `LessonPrice`, `LessonRoll`, `LessonsTaken`, `LessonsPaid`.
  Pre-season pass sale: `PresaleUniform`, `PresaleProbability`, `PresaleDecision`.
