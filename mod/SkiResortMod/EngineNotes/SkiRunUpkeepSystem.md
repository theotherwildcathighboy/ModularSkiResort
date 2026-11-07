# SkiRunUpkeepSystem: how run operating costs are billed, booked and logged

Run operating costs: snowmaking runs cost the most (guns + water + power). Groomed runs cost a third of that (cat fuel). Natural terrain is
free. Without this system, snowmaking strictly dominates groomed, and both dominate "just ski the powder". With it, season length versus money
is a real decision.

## Billing

The charge is per run and by feature (R3, DECISIONS 578). One rule serves the charge, its per-area booking and the hub's rows:
`SkiSnowInjectionSystem.PisteUpkeep(run, features, planArea)` -> `SkiRunUpkeepRates.PisteUpkeep`. Its inputs are:
- the run's active texels: the mask texels it claimed first this window (cold, gate on)
- the run's stopped texels: a gun run's cold texels held off while its guns stopped at depth (`RunStoppedTexels`, first mark only)

The rule turns each input into hectares and caps it at the plan area (`BilledHa`). The rates are:
- Grooming (150) and lights (+300) bill active + stopped hectares.
- Snowmaking (+350) bills only the active hectares of a run with guns on.

So a gun run at depth keeps paying its grooming. This was the R2 open item: before R3, such a run billed nothing that pass, while a gun-off run paid 150.
The run pays snowmaking only while its guns raise snow. With guns running on every cold texel, the sum equals the R2 per-hectare rate.
Summer is free because the gates are off. First claim across runs keeps overlaps from double-billing. A stopped texel under a later running run
can count for both runs, and each run's plan area caps its own count. This system no longer reads the global per-class counters
(`SnowmakingActiveTexels` etc.). Visuals, attractiveness and money all read the same climate model, so they cannot disagree.

Cat tracks have no active texels (they stay out of injection, SkiSnowInjectionSystem.cs near the mask loop). They bill by plan area at the snowmaking rate while their guns can run
and are on: `SnowGunPlumeRules.TrackBilledHa(area, CatTrackGunsCanRun(run, ...), SkiRunPropSystem.CatTrackGunsOn(run))`, per cataloged track. A track whose gun value is Off
stands no gun and bills nothing (DECISIONS 563). The hub's cost row and gun line follow the same switch. A lit track adds the lights rate (300)
on its plan area while `CatTrackGunsCanRun` is true (its season gate), whatever its gun switch: `SkiRunUpkeepRates.TrackLitHa`. This applies in the charge, the
per-area split and the hub's Lights row (DECISIONS 579, gate check-run-props R10). Before that, cat track lights were look only and unbilled.
No other prop slider counts. `HasMaskMeasurement` is set
before the injection's idle exits, so a resort with only cat tracks reaches the charge.

## Money

Money goes to `Game.City.PlayerMoney` on the `CitySystem.City` entity (public Add/Subtract, the component that every vanilla income/expense
ultimately mutates). `ExpenseSource` is a closed enum aggregated inside `CityServiceBudgetSystem`. So the charge cannot appear as its
own budget-panel line item without Harmony (banned). The cost shows in the balance, and the log carries the itemization.

## Time

`TimeSystem.kTicksPerDay` is a public const (262144, TimeSystem.cs:18), and `daysPerYear` comes from `TimeSystem`. One calendar month =
kTicksPerDay x daysPerYear / 12 frames. Charges accrue per elapsed frame between passes (wrap-safe uint math) with a fractional carry,
so there is no cadence drift and no rounding loss on small resorts.

## Scheduling

The system runs in the GameSimulation phase. It runs only while the sim advances, and only in Game action mode: pause stops the meter. It is anchored after
`SkiSnowInjectionSystem`, so a same-frame top-up publishes its mask before we read it. The cadence is self-gated.

## Hub accessors

- `CostPerHaMonth(RunKind)`: the same rates this system bills, by the shared `RunKind` 0 groomed / 1 snowmaking / 2 night.
- `LastMonthlyRate`.
- `IdleReason` ("" while billing).
- `DebitedThisLoad`: `SkiGuestCounterSystem` reads it for the month's `run upkeep -N (whole city)`.

These are plain field reads. They are frozen while paused.

## Logging

The system uses the `ChairPinSystem` error pattern. It writes these lines:
- an ARMED line with every rate at load
- a charge line whenever the monthly rate bucket changes, plus a periodic heartbeat
- an idle-state line once per reason transition

Flood window: the rate moves on almost every pass while snow builds (a pass is ~4.3 s apart at 4x). So charge lines go through a
`LogWindow`: at most one per `Tuning.LogWindowS` (10 s). The rest fold into one `Upkeep [charge] N more charge line(s) in T s,
folded ...` line. The system flushes a fold before an idle line, before a load (`OnGamePreload`) and at quit (`OnDestroy`). It forces the first billable
pass after a load or idle spell, so a billing restart is never folded away.

## Plan-area cap

A run is never billed for more hectares than it has. The mask counts every texel that the outline touches as a whole texel (~14 m cells),
so edge texels over-read the run by about 3-4 %. Each pass handles every cataloged run that has an entry in the per-run map
(`SkiSnowInjectionSystem.TryGetRunActiveTexels`, first-claim counts that sum to the three totals). For each such run, the pass takes the part of its texel hectares
above its plan area (`Geometry.m_SurfaceArea`) off its kind's total. The formula is `BilledHa(texels, texelHa, plan)` = min(texels x texelHa,
plan / 10^4). The system bills a run that the catalog does not list yet uncapped for that pass. `BilledHa` is public and pure, so the hub's run rows
read the same number as the bill.

## Per-area booking

The system also books every debit per ski area into the guest counter's `SkiResortLedger` (`Month.RunUpkeep`), so the history store keeps each
area's real run cost. The weight of an area = the sum over its bound, cataloged, non-natural runs with active texels of `BilledHa` x
`CostPerHaMonth(kind)` (the hub's per-run rule, `SkiResortStatsSystem.CollectRuns`). Unbound runs, and the part of the bill that no cataloged
run explains, go to Not linked (`EntityRef.Null`). `SkiResortHistory.SplitDebit` makes the parts sum exactly to the debit with a
per-area carry.
