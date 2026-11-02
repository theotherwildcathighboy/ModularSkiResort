# SkiRunUpkeepSystem: how run operating costs are billed, booked and logged

Run operating costs: snowmaking runs cost the most (guns + water + power), groomed runs a third of that (cat fuel), natural terrain is
free. Without this, snowmaking strictly dominates groomed and both dominate "just ski the powder"; with it, season length versus money
is a real decision.

## Billing

The billable base is the injection system's per-type active mask, texels whose thermal gate is on at their own elevation this top-up
window (`SkiSnowInjectionSystem.SnowmakingActiveTexels` / `GroomedActiveTexels` x `TexelAreaM2`), each run capped at its plan area
(see Plan-area cap). Guns bill while guns can run, groomers while grooming holds, summer is free because the gates are off, and
overlapping runs never double-bill (a texel is claimed once, see `MaskStats`). Visuals, attractiveness and money all read the same
climate model and cannot disagree.

Cat tracks have no active texels (they stay out of injection, SkiSnowInjectionSystem.cs near the mask loop) and bill by plan area at the snowmaking rate while their guns can run
and are on: `SnowGunPlumeRules.TrackBilledHa(area, CatTrackGunsCanRun(run, ...), SkiRunPropSystem.CatTrackGunsOn(run))`, per cataloged track. A track whose gun value is Off
stands no gun and bills nothing (DECISIONS 563); the hub's cost row and gun line follow the same switch. No other prop slider counts. `HasMaskMeasurement` is set
before the injection's idle exits, so a resort with only cat tracks reaches the charge.

## Money

Money goes to `Game.City.PlayerMoney` on the `CitySystem.City` entity (public Add/Subtract, the component every vanilla income/expense
ultimately mutates). `ExpenseSource` is a closed enum aggregated inside `CityServiceBudgetSystem`, so the charge cannot appear as its
own budget-panel line item without Harmony (banned); the cost shows in the balance and the log carries the itemization.

## Time

`TimeSystem.kTicksPerDay` is a public const (262144, TimeSystem.cs:18) and `daysPerYear` comes from `TimeSystem`; one calendar month =
kTicksPerDay x daysPerYear / 12 frames. Charges accrue per elapsed frame between passes (wrap-safe uint math) with a fractional carry,
so no cadence drift and no rounding loss on small resorts.

## Scheduling

GameSimulation phase (runs only while the sim advances, only in Game action mode: pause stops the meter), anchored after
`SkiSnowInjectionSystem` so a same-frame top-up publishes its mask before we read it; self-gated cadence.

## Hub accessors

`CostPerHaMonth(RunKind)` (the same rates this system bills, by the shared `RunKind` 0 groomed / 1 snowmaking / 2 night),
`LastMonthlyRate`, `IdleReason` ("" while billing), `DebitedThisLoad` (read by `SkiGuestCounterSystem` for the month's
`run upkeep -N (whole city)`). Plain field reads; frozen while paused.

## Logging

ARMED line with every rate at load, a charge line whenever the monthly rate bucket changes plus a periodic heartbeat, idle states once
per reason transition, the `ChairPinSystem` error pattern.

Flood window: the rate moves on almost every pass while snow builds (a pass is ~4.3 s apart at 4x), so charge lines go through a
`LogWindow`: at most one per `Tuning.LogWindowS` (10 s), the rest folded into one `Upkeep [charge] N more charge line(s) in T s,
folded ...` line. A fold is flushed before an idle line, before a load (`OnGamePreload`) and at quit (`OnDestroy`); the first billable
pass after a load or idle spell is forced so a billing restart is never folded away.

## Plan-area cap

A run is never billed for more hectares than it has. The mask counts every texel the outline touches as a whole texel (~14 m cells),
so edge texels over-read the run by about 3-4 %. Each pass takes, per cataloged run with an entry in the per-run map
(`SkiSnowInjectionSystem.TryGetRunActiveTexels`, first-claim counts that sum to the three totals), the part of its texel hectares
above its plan area (`Geometry.m_SurfaceArea`) off its kind's total: `BilledHa(texels, texelHa, plan)` = min(texels x texelHa,
plan / 10^4). A run the catalog does not list yet is billed uncapped for that pass. `BilledHa` is public and pure so the hub's run rows
read the same number as the bill.

## Per-area booking

Every debit is also booked per ski area into the guest counter's `SkiResortLedger` (`Month.RunUpkeep`), so the history store keeps each
area's real run cost. Weight of an area = sum over its bound, cataloged, non-natural runs with active texels of `BilledHa` x
`CostPerHaMonth(kind)` (the hub's per-run rule, `SkiResortStatsSystem.CollectRuns`); unbound runs and the part of the bill no cataloged
run explains go to Not linked (`EntityRef.Null`). `SkiResortHistory.SplitDebit` makes the parts sum exactly to the debit with a
per-area carry.
