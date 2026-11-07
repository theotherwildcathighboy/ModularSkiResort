# HubRules: the hub's pure rules, the single Net definition, and the lift-pairing logic of the problems band

`HubRules.cs` holds the hub's pure rules:

- The literal-valued enums of the hub contract.
- The small rules behind the ResortLive / RunLive / LiftRow fields.
- The area-weighted snow sums.
- Rule 7's guard (`FollowCheck`).
- The problems band (`HubProblems.Build`).

The literal oracles for these rules are tests/ResortBatch `HubRulesTests` sections A-H. The methods also document the small rules.

Callers:

- `SkiResortStatsSystem` (every rule of a rebuild, `HubProblems.Build` per area).
- `SkiResortHubUISystem` (`LiftNoPair` for `build[lift].hint`, `FollowCheck` for rule 7, `LiftLinePerKm` for the Build strip).

Never the UI. The UI receives the results as numbers and enum values and never recomputes the score. (`AttractionWithLodges` is the one
"what if", and it uses the model's own `LodgeBaseOf` / `RoundToEven` / `MaxAttraction`.)

The enums (ResortStatus, RunOpenState, SeasonHint, NextStepKind, HubLiftStatus, HubProblemCode, HubSkiDayState) are literal-valued. Gate
rule R17 of mod/SkiResortUI/tools/check-bundle.js compares each name and value with bindings.ts. So every value is written out, and
`HubProblemCode` is append only.

## RULES

- **Season.**
  - Gating off -> `GatingOff`.
  - No run or unknown top temperature -> `None`.
  - Natural = T <= freezing (T = the 24-hour day mean at the top).
  - Gun = a gun run and one of these:
    - The live gun gates, when known (`anyGunOn`: a bound snowmaking or night run with gun-gated texels at the last mask pass).
    - Else (before the first mask pass, or the injection off): T <= freezing + margin (both inclusive).
  - With an open run: natural `SnowSeason`, else gun `SnowGunsOnly`, else `Thawing`.
  - None open: natural or gun `WaitingForSnow`, else `TooWarm`.
- **LiftStatusOf.**
  - Stats state 0: powered `Running` / `NoPower`.
  - State 1: powered `ChairsStopped` / `NoPower`.
  - State 2: `SectionMissing`.
  - Anything else: `Unfinished`.

  The closed overload: a complete, powered lift (state 0 or 1) that the no-open-run rule holds (`LiftClosedRule`, the tram exempt)
  reads `Closed`, not `Running`.
- **NetMonth.** The one Net of the hub, over the nine terms of `HubMonthMoney` (see ONE NET below).
- **AttractionWithLodges.** The model's A with `lodges` active lodges at the same efficiency. The steps: swap the lodge base, clamp to
  0..`MaxAttraction` before rounding, then `RoundToEven`. It equals `r.AOpen - r.LodgeApplied + LodgeBaseOf(n) x (summer ? SummerLodgeShare : 1)`:
  n A-frames in place of the area's applied lodge base. So a summer area with no lodge keeps its "if fixed" number (its `LodgeApplied`
  is 0). `AttractionWithLodgeApplied(r, applied)` is the stats system's `IfLodgesOn`, with the ladder's applied value of the area's
  lodges all switched on (`SkiResortScore.LodgeBase`).
- **LiftNoPair.** False in these cases:
  - One row of the shown area has both stations live and a line to draw or finish (state 2, 3 or 4).
  - A lone base and a lone top sit on two rows of state 3 or 4 (the census never puts both on one such row).

  Else true (the empty list too). The hub calls it per lift type over the rows of that type (`HubLiftHintRow.Type`).
- **FollowCheck** (order matters). The checks run in this order:
  1. Hub closed.
  2. A rule switched in this pass.
  3. Within `FollowQuietAfterUserS` of the player's own switch.
  4. Within `FollowMinIntervalS` of the last follow.
  5. Else Follow.

  Negative infinity = never. A NaN comparison is false, so it allows.
- **SkiDayStateOf** (first match wins):
  - Off -> `Off`.
  - No view -> `Unknown`.
  - No score or the first-write hold -> `Unknown`.
  - Sign not running -> `SignOff`.
  - Runs counted <= 0 -> `NoRun`.
  - Target lodges <= 0 -> `NoLodge`.
  - Outside the claim hours -> `Night`.
  - cap < 0 or now < 0 -> `Unknown` (before the first pass).
  - now >= cap -> `Full` (a capacity of 0 is real: the simulation sends nobody, `SkiDayRules.HasRoom(x, 0)` is false).
  - Else `Room`.
- **ShuttleRideTimeS / ShuttleCapacityPerHour** come from `ShuttleProfile` (the cabin is unavailable while it dwells). Both widen their
  float inputs to double once and narrow the result once (bit-exact float oracles). `ShuttleRideIncludesDwell` (false) is the one
  switch for an open decision.
- **LiftCapacityShown.** An unpowered lift of every type carries no guests, so it shows no capacity (-1, the UI's "-"). It does not
  change status, the chairs count or the ride time.
- **HubProblems.Build.**
  - Unlinked rows only for the Not linked entry (`SchoolsNotLinked`: FreeSchools > 0 -> severity 1, count).
  - Else lodges, then road edges:
    - `SignNoRoad` severity 3 (the input is true only while the sign carries jobs, SkiResortStatsSystem.md).
    - `LodgeNoRoad` counts `min(LodgesNoRoad, Lodges) + SchoolsNoRoad`. Severity 3 only when every lodge has no road, else 1. A school
      alone: 1.
  - Efficiency: at <= `EfficiencyProblemPct`. The row stays while the band showed it on the area's previous build and the percent is
    still below `EfficiencyClearPct`. "more" = the factors listed as Factor rows - 1, or every factor - 1 when none is listed.
  - Lifts:
    - Lone stations of state 4 pair across rows, with one "draw the lift line" row per base + top pair (see below).
    - A lift row with Mixed writes `MixedLiftTypes`, severity 2, instead of `LiftUnfinished`.
    - Every other lift row's build item comes from the row's type, `LineItemOf` / `StationItemOf`. Fixed-grip = "lift" / "baseStation" /
      "topStation".
    - `NoLift` keeps "baseStation".
  - No lift, no runs, unserved, unrateable / rating / flat.
  - The ski-day rows:
    - `SkiDaysCannotReach` severity 2.
    - `NoParkingNearLodge` and `LodgeNotSlopeside` severity 1, count = `min(n, Lodges)`, none when 0.
    - `LodgeNotSlopeside` only when the area has runs (Runs > 0).
  - `LiftBend` (30, 2026-11-02): a lift row of state 0, 1 or 2 (complete or open) with `BendTenths` > 0 -> severity 1, count = `BendTower`,
    detail = `BendTenths`. The census decides the threshold (`LiftCensus.BendWarnTenths`: over `LiftAlignMath.BendWarnDeg`), so this pure
    file needs no Unity.Mathematics. Warn, never refuse: the row changes nothing else. A Mixed row never reaches it (it is Unfinished).
  - `LiftTrainLimit` (31, 2026-11-03): a lift row of state 0, 1 or 2 with `TrainTower` > 0 -> severity 1, count = `TrainTower` (the
    tower plate number). Text "{NAME}: tower {N}'s wheels cannot follow the rope". The tip states the limits relative to the mast
    (DECISIONS 600. Tilt: 30 deg, DQ / GD 40, tram saddles 60. Fold: 40, trams 60). The row also counts a rope half past the carrier
    world-half clearance (`PoseTrain`). A click flies to the tower (`ApplyFocusLift`, subject = `TrainNode`). Warn, never refuse.
  - `LiftLowSpan` (32, 2026-11-03): a lift row of state 0, 1 or 2 with `LowSpan` -> these fields:
    - severity 1.
    - count / more = `LowFrom` / `LowTo` (tower plate numbers, 0 = a station).
    - detail = `LowTenths` (the carriers' height above the ground in tenths, rounded down, 0 at or below it).
    - pct = 1 for cabins (gondola, tram), 0 for chairs.

    Text "{NAME}: {CARRIERS} pass {M} m above the ground" (or "touch the
    ground" at 0). The tip names the span ("Between towers {A} and {B}" / "Between tower {N} and the station" / "Between the stations"). The reason: the row must fit a 1080 band line (about 49 characters). A click flies to the low point (`ApplyFocusLift`, subject =
    `LowEdge`). Warn, never refuse. Like every row, the cut removes it when the band is full.
  - `SummitLodgesNotServed` > 0 -> `SummitLodgeNotLiftServed` (severity 1, count). `SchoolNoGreenRun` (severity 1).
  - Then the final steps:
    - A stable insertion sort by (severity descending, code ascending, insertion order).
    - A cut to `MaxBandRows` SLOTS. The rows of a `FoldedCodes` code (unfinished, no power, section missing, as listed in hubRules.ts
      `FOLDED_PROBLEM_CODES`, T39) are one band line. So they take one slot and keep up to `MaxBandRows` rows (DECISIONS 588 f: four
      "No power" rows used to push a low span off the band).
    - Then one Factor row per efficiency factor that costs at least `FactorRowMinLossPct` points (severity 0, never in the band).

## ONE NET

Every Net the hub shows (the Finances tile, the Finances view, History's months and its month in progress) is `NetMonth` over the same
nine terms (`HubMonthMoney`), so they cannot disagree:

| Term | Source | Booking |
|---|---|---|
| Income: `TicketIncome` | ledger TicketIncome | booked this month (paid at the gate) |
| Income: `LodgeIncome` | ledger LodgeSpendIncome | booked this month |
| Income: `LessonIncome` | ledger LessonIncome | booked this month |
| Income: `PassIncome` | ledger PassIncome | booked this month = passes sold this month, pre-season sales included (they land in the month they were sold: "a big September is honest") |
| Cost: `LiftCost` | ledger LiftCharged, rounded | booked this month (`SkiLiftChargeSystem`'s accrual) |
| Cost: `RunCost` | ledger RunUpkeep | booked this month, per area (`SkiRunUpkeepSystem`) |
| Cost: `BusCost` | ledger BusCharged, rounded | booked this month (`SkiBusChargeSystem`). A closed month's element holds it inside `LiftCost`, 0 here |
| Cost: `BuildingsCost` | the game's upkeep formula for the sign + its lodges | rate snapshot: per month at the current service budget x `MonthShare` for the month in progress, and the rate at the roll for a closed month |
| Cost: `WagesCost` | the game's upkeep formula for the sign + its lodges | rate snapshot, as BuildingsCost |

The game bills the two rate-snapshot terms through the city budget. Nothing books them per area.

Net = sum of income - sum of costs. Every term < 0 (unknown) reads as 0. So the rows the UI shows (max(0, term) each) add up to the Net
exactly (ResortBatch `HubRulesTests` A and `HistoryTests` H-10).

Estimated (the approximation sign) = some term is unknown: the ledger is not counting yet, or the building bill is unreadable. A month
counted only since a load is partial, not estimated. ResortLive's `thisMonthSinceLoad` carries that, as for the guests.

Rounding = the history element's rounding (`SkiResortHistoryMonth.FromLedger`: ClampInt of the longs, `LiftCharged` half away from zero).
So a closed month's element and `MonthMoneyOf(its ledger Month)` give the same Net (the coach bill sits in a different term, in the
same total).

## Near HubProblems.Build, step 4 (lifts)

The census makes every untouched station its own single-station StationsOnly row (state 4). An Unfinished row (state 3) carries at
most one station. So a placed base and a placed top are never one row. Pre-scan: the i-th lone base of state 4 pairs with the i-th lone
top of state 4. The pair is one Detail 0 row ("draw the lift line", build "lift"). The band does not list the other station's row. A lone
state-4 station left over whose other kind waits on an Unfinished line's end also reads Detail 0 (finish the line to it). Only a
station with no partner anywhere keeps Detail 1 / 2.

The pair row sits at the row whose name the finished lift keeps. This follows the stats system's lift-name rule: the base's custom name,
else the top's custom name, else "Lift N" numbered by top-station height with rows without a top last (`SkiResortAggregate.LiftOrdinals`).
So the pair row is the base's row when the base has a custom name, else the top's row. Otherwise the band names a lift "Lift 2" that the
hub calls "Lift 1" once its line is drawn.

The pairing runs per lift type. A lone fixed-grip base never pairs with a lone detachable top (that line would be Mixed). A Mixed row is
not an unfinished line of its stations' kind, so it does not count as one. The code allocates nothing: per-type counts are rescans (an
area has a handful of lift rows).
