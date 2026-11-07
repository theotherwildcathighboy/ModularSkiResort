# SkiResortLedger: the monthly guest and money ledger contract

`SkiResortLedger` is in memory only and is reset on every load. The month in progress is restored from the saved
`SkiResortMonthInProgress` component (see History below). It holds one Entry per ski area keyed by its sign as an `EntityRef`, with
`EntityRef.Null` = Not linked (lift charges only).

## Consumers

- `SkiGuestCounterSystem`: owner of the one instance (Advance / AddArrival / Prune).
- `SkiLiftTicketSystem`: AddTickets, AddLodgeSpend, AddPasses, AddLessons.
- `SkiLiftChargeSystem`: AddLiftCharge.
- `SkiRunUpkeepSystem`: AddRunUpkeep.
- `SkiResortStatsSystem`: Entry.This / Last and the month flags.
- tests/ResortBatch MoneyBatch.

## Contract

- Every Add* creates the entry on first use and increments Revision. A call whose amounts are all zero changes nothing. Inputs below 0
  (and a non-finite or non-positive lift amount) are ignored: the ledger never subtracts. There is no allocation after the list and the index grow to the ski-area count. Storage is a `Dictionary<EntityRef, int>` index into a `List<Entry>`.
- `Advance(key)` returns one of:
  - Started: the first call after Reset (MonthKey = key, ThisMonthSinceLoad).
  - None: same key.
  - Rolled: key == MonthKey + 1. Every Last = This, and This = 0. It sets LastMonthKnown, LastMonthSinceLoad = ThisMonthSinceLoad, and
    ThisMonthSinceLoad = false.
  - Jumped: any other key. Every Last and This = 0. LastMonthKnown = false. LastMonthSinceLoad = false, since there is no last month to
    describe. ThisMonthSinceLoad = true: counting restarts partway through the month as after a load, so the next roll never calls that
    month full.

  "Since load" thus reads "since counting started (load or jump)". Only `SkiGuestCounterSystem` calls it (every 16 sim frames). It also
  keeps the sim frame counting started at (CountingSinceFrame) for the hub's projection.
- `Advance(key, closedMonthShort)`: a roll whose closed month was short sets LastMonthSinceLoad. A month is short when fewer than
  SkiMoneyRules.FullMonthShare of its frames were simulated. A developer time jump that lands exactly one month later rolls, and it does not
  jump. So LastMonthSinceLoad reads "last month partial". The hub marks such a month "(partial)", and the Money tile does not use it as a full month
  (SkiResortStatsSystem: `lastMonthFull = LastMonthKnown && !LastMonthSinceLoad`).
- `Prune(live)` drops the entries whose sign is not in `live`. `EntityRef.Null` is always kept.
- Revision increments on every change (Reset, Started / Rolled / Jumped, any Add* that changed a number, a Prune that removed an
  entry). Readers compare it for equality.

## Near AddArrival

AddArrival takes `newGroup` (default true) and `onLeisure` (default false). `Month.TouristOnLeisure` = tourists counted through the
leisure scan, a subset of TouristGuests. It can be slightly high: an outing member already on site with a Leisure aimed at the lodge is
counted by the leisure scan before its outing turns Attending. `TouristParties` is group-days: distinct households per ski area per
game day, summed over the month. The counter passes `newGroup` only for a household's first count there that day.

## Month fields

Advance / Prune / Total carry these fields (the whole Month moves):

- lodge spend (LodgeSpends, LodgeSpendIncome).
- season passes (PassesSold, PassIncome, PassHolderGuests, the pre-season subset PassesPre / PassIncomePre, counted in the totals once).
- lessons (Lessons, LessonIncome).
- RunUpkeep.

`Entry.PassSeasonKey` / `PassSeasonSold` / `PassSeasonIncome` / `PassSeasonPre` / `PassSeasonPreIncome` keep the ski season's sales
across month rolls (the `*Of` readers return 0 for another season).

## History

The sum of RunUpkeep over the entries, Not linked included, equals the city-wide `run upkeep` of the `Guests [month]` line.
`SkiResortHistorySystem` mirrors each area's This (and its pass season) into the saved `SkiResortMonthInProgress` component. After a
load, `SeedMonth(key, partial)` + `SeedThis(sign, ...)` add the saved month back when its key is the ledger's month. So
ThisMonthSinceLoad is false again for a month that was whole up to the save. Another key seeds nothing. Not linked has no sign to carry
it and is not restored. Closed months go to the per-sign history buffer (SkiResortHistory.cs), not here.
