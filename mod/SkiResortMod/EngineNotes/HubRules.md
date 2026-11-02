# HubRules: the hub's pure rules, the single Net definition, and the lift-pairing logic of the problems band

`HubRules.cs` holds the hub's pure rules: the literal-valued enums of the hub contract, the small rules behind the ResortLive / RunLive /
LiftRow fields, the area-weighted snow sums, rule 7's guard (`FollowCheck`) and the problems band (`HubProblems.Build`). The literal
oracles for these rules are tests/ResortBatch `HubRulesTests` sections A-H; the small rules are also documented on their methods.

Callers: `SkiResortStatsSystem` (every rule of a rebuild; `HubProblems.Build` per area) and `SkiResortHubUISystem` (`LiftNoPair` for
`build[lift].hint`, `FollowCheck` for rule 7, `LiftLinePerKm` for the Build strip). Never the UI: the UI receives the results as numbers
and enum values and never recomputes the score (`AttractionWithLodges` is the one "what if" and uses the model's own `LodgeBaseOf` /
`RoundToEven` / `MaxAttraction`).

The enums (ResortStatus, RunOpenState, SeasonHint, NextStepKind, HubLiftStatus, HubProblemCode, HubSkiDayState) are literal-valued: gate
rule R17 of mod/SkiResortUI/tools/check-bundle.js compares each name and value with bindings.ts, so every value is written out and
`HubProblemCode` is append only.

## RULES

- **Season.** Gating off -> `GatingOff`; no run or unknown top temperature -> `None`. Natural = T <= freezing (T = the 24-hour day mean
  at the top). Gun = a gun run and the live gun gates when known (`anyGunOn`: a bound snowmaking or night run with gun-gated texels at
  the last mask pass), else (before the first mask pass, or the injection off) T <= freezing + margin (both inclusive). With an open
  run: natural `SnowSeason`, else gun `SnowGunsOnly`, else `Thawing`. None open: natural or gun `WaitingForSnow`, else `TooWarm`.
- **LiftStatusOf.** Stats state 0: powered `Running` / `NoPower`; state 1: powered `ChairsStopped` / `NoPower`; state 2:
  `SectionMissing`; anything else `Unfinished`. The closed overload: a complete, powered lift (state 0 or 1) held by the no-open-run
  rule (`LiftClosedRule`, the tram exempt) reads `Closed`, not `Running`.
- **NetMonth.** The one Net of the hub, over the nine terms of `HubMonthMoney` (see ONE NET below).
- **AttractionWithLodges.** The model's A with `lodges` active lodges at the same efficiency: swap the lodge base, clamp to
  0..`MaxAttraction` before rounding, `RoundToEven`. Equals `r.AOpen - r.LodgeApplied + LodgeBaseOf(n) x (summer ? SummerLodgeShare : 1)`:
  n A-frames in place of the area's applied lodge base, so a summer area with no lodge keeps its "if fixed" number (its `LodgeApplied`
  is 0). `AttractionWithLodgeApplied(r, applied)` is the stats system's `IfLodgesOn`, with the ladder's applied value of the area's
  lodges all switched on (`SkiResortScore.LodgeBase`).
- **LiftNoPair.** False when one row of the shown area has both stations live and a line to draw or finish (state 2, 3 or 4), or a
  lone base and a lone top sit on two rows of state 3 or 4 (the census never puts both on one such row); else true (the empty list
  too). The hub calls it per lift type over the rows of that type (`HubLiftHintRow.Type`).
- **FollowCheck** (order matters). Hub closed; a rule switched in this pass; within `FollowQuietAfterUserS` of the player's own switch;
  within `FollowMinIntervalS` of the last follow; else Follow. Negative infinity = never; a NaN comparison is false, so it allows.
- **SkiDayStateOf** (first match wins). Off -> `Off`; no view -> `Unknown`; no score or the first-write hold -> `Unknown`; sign not
  running -> `SignOff`; runs counted <= 0 -> `NoRun`; target lodges <= 0 -> `NoLodge`; outside the claim hours -> `Night`; cap < 0 or
  now < 0 -> `Unknown` (before the first pass); now >= cap -> `Full` (a capacity of 0 is real: the simulation sends nobody,
  `SkiDayRules.HasRoom(x, 0)` is false); else `Room`.
- **ShuttleRideTimeS / ShuttleCapacityPerHour** from `ShuttleProfile` (the cabin is unavailable while it dwells); both widen their
  float inputs to double once and narrow the result once (bit-exact float oracles). `ShuttleRideIncludesDwell` (false) is the one
  switch for an open decision.
- **LiftCapacityShown.** An unpowered lift of every type carries no guests, so it shows no capacity (-1, the UI's "-"); status, the
  chairs count and the ride time are not touched by it.
- **HubProblems.Build.**
  - Unlinked rows only for the Not linked entry (`SchoolsNotLinked`: FreeSchools > 0 -> severity 1, count).
  - Else lodges, then road edges: `SignNoRoad` severity 3 (the input is true only while the sign carries jobs, SkiResortStatsSystem.md); `LodgeNoRoad` counts `min(LodgesNoRoad, Lodges) + SchoolsNoRoad`, severity 3
    only when every lodge has no road, else 1; a school alone 1.
  - Efficiency: at <= `EfficiencyProblemPct`, and kept while the band showed it on the area's previous build and the percent is still
    below `EfficiencyClearPct`. "more" = the factors listed as Factor rows - 1, or every factor - 1 when none is listed.
  - Lifts: lone stations of state 4 paired across rows, one "draw the lift line" row per base + top pair (see below); a lift row with
    Mixed writes `MixedLiftTypes`, severity 2, instead of `LiftUnfinished`; every other lift row's build item comes from the row's
    type, `LineItemOf` / `StationItemOf`, fixed-grip = "lift" / "baseStation" / "topStation"; `NoLift` keeps "baseStation".
  - No lift, no runs, unserved, unrateable / rating / flat.
  - The ski-day rows: `SkiDaysCannotReach` severity 2; `NoParkingNearLodge` and `LodgeNotSlopeside` severity 1, count =
    `min(n, Lodges)`, none when 0; `LodgeNotSlopeside` only when the area has runs (Runs > 0).
  - `LiftBend` (30, 2026-11-02): a lift row of state 0, 1 or 2 (complete or open) with `BendTenths` > 0 -> severity 1, count = `BendTower`,
    detail = `BendTenths`. The census decides the threshold (`LiftCensus.BendWarnTenths`: over `LiftAlignMath.BendWarnDeg`), so this pure
    file needs no Unity.Mathematics. Warn, never refuse: the row changes nothing else. A Mixed row never reaches it (it is Unfinished).
  - `SummitLodgesNotServed` > 0 -> `SummitLodgeNotLiftServed` (severity 1, count); `SchoolNoGreenRun` (severity 1).
  - Then a stable insertion sort by (severity descending, code ascending, insertion order), cut to `MaxBandRows`, then one Factor row
    per efficiency factor that costs at least `FactorRowMinLossPct` points (severity 0, never in the band).

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
| Cost: `BusCost` | ledger BusCharged, rounded | booked this month (`SkiBusChargeSystem`); a closed month's element holds it inside `LiftCost`, 0 here |
| Cost: `BuildingsCost` | the game's upkeep formula for the sign + its lodges | rate snapshot: per month at the current service budget x `MonthShare` for the month in progress; the rate at the roll for a closed month |
| Cost: `WagesCost` | the game's upkeep formula for the sign + its lodges | rate snapshot, as BuildingsCost |

The game bills the two rate-snapshot terms through the city budget; nothing books them per area.

Net = sum of income - sum of costs, every term < 0 (unknown) read as 0, so the rows the UI shows (max(0, term) each) add up to the Net
exactly (ResortBatch `HubRulesTests` A and `HistoryTests` H-10). Estimated (the approximation sign) = some term is unknown: the ledger
not counting yet, or the building bill unreadable. A month counted only since a load is partial, not estimated: ResortLive's
`thisMonthSinceLoad` carries that, as for the guests. Rounding = the history element's (`SkiResortHistoryMonth.FromLedger`: ClampInt of
the longs, `LiftCharged` half away from zero), so a closed month's element and `MonthMoneyOf(its ledger Month)` give the same Net (the coach bill sits in a different term, in the
same total).

## Near HubProblems.Build, step 4 (lifts)

The census makes every untouched station its own single-station StationsOnly row (state 4) and an Unfinished row (state 3) carries at
most one station, so a placed base and a placed top are never one row. Pre-scan: the i-th lone base of state 4 pairs with the i-th lone
top of state 4. The pair is one Detail 0 row ("draw the lift line", build "lift"), and the other station's row is not listed. A lone
state-4 station left over whose other kind waits on an Unfinished line's end also reads Detail 0 (finish the line to it). Only a
station with no partner anywhere keeps Detail 1 / 2.

The pair row sits at the row whose name the finished lift keeps (the stats system's lift-name rule: the base's custom name, else the
top's custom name, else "Lift N" numbered by top-station height with rows without a top last, `SkiResortAggregate.LiftOrdinals`): the
base's row when the base has a custom name, else the top's row. Otherwise the band names a lift "Lift 2" that the hub calls "Lift 1"
once its line is drawn.

The pairing runs per lift type: a lone fixed-grip base never pairs with a lone detachable top (that line would be Mixed). A Mixed row is
not an unfinished line of its stations' kind, so it does not count as one. No allocation: per-type counts are rescans (an area has a
handful of lift rows).
