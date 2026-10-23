# SkiMoneyRules: the money rules (lift charge, tickets, passes, lessons, guest days)

The rules `SkiMoneyRules` implements, by topic. All of it is pure static code; the callers pass rate numbers in.

## Lift charge and billing reductions

- Lift charge: Full = `LiftFixedPerMonth` + `LiftPerKmPerMonth` x km. A lift that is not complete, or a complete lift whose ski area
  has no counted run (`LiftAreaRunOpen` false: the area's released score counts 0 runs), bills `LiftReducedShare` of that.
- The charge bills through `LiftUpkeepBillingOf`, so a missing power connection does not reduce it (an unpowered lift still costs
  its maintenance). `LiftOperating(complete, powered)` is the charge's operating count (logs and the hub only).
- Billing reductions (each `LiftReducedShare`, 25 %): `Unfinished` (any census state but Complete) and `NoOpenRun` (complete, its
  ski area counts no run). A run counts only when its value, the snow open factor included, is > 0 (SkiResortScore.cs:326-338,
  :369-380), so NoOpenRun applies in every period without snow at any backcountry share, and with backcountry 0 to an area whose
  only serving lift is unpowered.
- An unpowered complete lift is billed like every complete lift of its area: in full while the area counts a run, 25 % while it
  counts none. `LiftOperating` and the charge's Operating count keep the real power; the `NoPower` billing stays bound but is
  unreachable from the charge. `NoOpenRun` is not bound to the UI (no gate-rule R17 entry).
- Rate numbers are passed in by the caller (`LiftFullRate(lengthM, fixedPerMonth, perKmPerMonth)`, `LiftRate(billing, lengthM,
  fixed, perKm)`); nothing here reads a `SkiCatalog` row. The one-length overloads are the fixed-grip consts.

## Accrual

The run-upkeep pattern (`SkiRunUpkeepSystem`'s month = TicksPerDay x daysPerYear / 12 sim frames; `TimeSystem.kTicksPerDay`,
Game.Simulation/TimeSystem.cs:18; the ECS systems assert it at arm time): carry += rate x elapsed / month, the whole credits are
debited, the fraction carried.

## Ticket gate

Off (price 0) -> NoEfficiency (sign efficiency not > 0) -> NotKnownYet (no score yet, or the area's first score after a load still
held) -> Open on a counted run, else NothingOpen (a running lift alone does not open it). `TicketGate`'s literal values are compared
with bindings.ts `TICKET_GATE` by gate rule R17.

## Lodge spend

`LodgeSpendDefault` / `Max` / `Step` alias `SkiCatalog.LodgeSpend`; `ClampLodgeSpend`; `LodgeSpendOf(setting, multiplier)` =
floor(setting x multiplier + 0.5) (0 when either is not > 0); `SpendPaid(paid, spendPerPerson, moneyAfterTickets)` = min(paid,
money / spend) (0 when any is not > 0): the ticket job charges what a household has left after its tickets, never into debt.

## Months

`MonthKey` = year x 12 + calendar month 0..11; `MonthOfKey` is 1..12. A month with fewer than `FullMonthShare` (95 %) of its frames
simulated is short (`IsShortMonth`) and booked as partial.

## Guest days

Per ski area and game day, a tourist on leisure at a lodge counts once and not when its household came by outing that day; an
outing counts only the members not yet counted there that day. The household's day flags are `GuestDayOuting | GuestDayLeisure`;
the day key is `SkiDayRules.DayKey`, the day sets live in the counter.

## Season passes and lessons

- Season passes: `SeasonKeyOf` / `SeasonKeyOfDate` (July on = the year, else year - 1), `PassPrice`, `PassRoll` (the household's one
  deterministic roll), `PassStandingOf` (what its `SkiSeasonPass` says this season), `PassSale` (NotEligible / Declined / Bought /
  CannotPay). Called from the ticket job (Burst).
- Lessons: `LessonShare` / `LessonMultiplier` consts and clamps, `LessonPrice`, `LessonRoll`, `LessonsTaken`, `LessonsPaid`;
  pre-season pass sale: `PresaleUniform`, `PresaleProbability`, `PresaleDecision`.
