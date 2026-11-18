# SkiDayRules: Burst-callable pure rules, overload families and the trip model

## Why the file is pure

- `SkiDayLimit` is declared at top level (outside the class). Gate rule R17 compares it with `bindings.ts` `SKI_DAY_LIMIT`
  (check-bundle `ENUM_PAIRS`, and `SkiResortMod.csproj` lists this file in `SkiUiInput`), so its literal values never change.
- The file uses only `using System;` and `using Unity.Mathematics;`, plus `EntityRef` (never `Entity`) and `SkiCatalog` consts (compile-time
  literals: Burst sees numbers, never a `SkiCatalog.Lodges[...]` row read here). Every method is static, allocation-free and
  Burst-callable (`SkiDaySystem`'s jobs call it). It uses no managed type, no string and no exception.
- net48 traps (the mod build is the API gate): do not use `Math.Clamp`, `MathF`, `double.IsFinite` or `Span<T>`. Use `math.*` instead
  (`math.isfinite`, `math.clamp`, `math.floor`).
- Per-tier numbers reach the Burst jobs as `LodgeJob` / `AreaJob` fields filled on the main thread. This file reads only consts.

## Overload families

Old signatures stay bit-identical to the new one called with today's arguments (`SkiDayTests` section Y):

- `LodgeFacts.NoRoadNeeded` (false = road required): a mountain lodge (`LodgeRoadRule.Mountain`), or a road-optional lodge off-road on a
  cat track chain (`LodgeSupplyRule.NoRoadNeeded`, per building), passes `CheckLodge` without a road.
- `PriceFactor(price, tolerance)`: the lodge's `PriceTolerance` is added to the full-price threshold (mid 20, luxury 40).
  `PriceFactor(price)` = `PriceFactor(price, 0)`.
- `Capacity(runs, lodgeCapacity, targetLodges, powder, out limit)`: the lodge side is the passing lodges' summed per-tier capacity
  (A-frame 360, hut 200, mid 720, summit 480, grand 1,080). The old `(runs, n, ...)` = `(runs, 360 n, n, ...)`. The limit is Lodges
  when no target (road) lodge passes.
- `Chance(..., price, priceTolerance, schoolFactor, confidence, knobs)`: residents x `schoolFactor` (`SchoolRule`, 1.2 while the
  school is active with a counted green run or cat track, Result.SchoolRunCounted) outside the cap like W and P. Tourists ignore it. The old signature =
  `(..., price, 0, 1.0, ...)`.

## Trip model

`ClassTrack` keeps an arrivals-only speed and a second all-outcomes speed (`LearnSpeedAll`, `SpeedBound`, `LearnSpeedBound` with its
tri-state `BoundLearn`). `FitSpeed` picks which one the fit check reads (arrivals only while `TripKnobs.FitUsesAllOutcomes` is
false, so every send is bit-identical). The measure helpers at the end of the class are measure and text only. They are:
- distance bands,
- the mode seen,
- why a decision did not fit,
- the [area] log keys,
- `HillClosedForCount`, `ClosingEdgeOf`, `MiddayRearmDue`, `DoneDayOf`, `CostDayEnded`.

`Advance`'s closedShort overload closes a partial month when a jump lands a month later.

## Reach causes (T249)

The hub row "Ski-day cars not arriving" (`UpdateCannotReach`) now names a cause. The data was in the measure blocks only.

- `ReachCauseOf` sorts one failed claim, first match:
  1. NoRoute: the walker carried `Divert{PathFailed}` at the start. Vanilla sets that divert when the trip's pathfind returned an
     empty path (TripNeededSystem.cs:1562, :683-686, decompile 1.6.4f1). Verified by code read.
  2. NeverLeft: never departed, or departed with `MoveFrame` 0 (the walker never moved off).
  3. Stuck: moved off, and the walker or the lead vehicle showed PathOwner Failed or Stuck. UNVERIFIED mapping. The marker is
     transient, and it joins "lost its path" with "stuck in traffic". So the tip names both.
  4. OutOfTime: moved off, no path marker, and the deadline passed (outcome Expired).
  5. None: gone or retargeted after moving off with no marker (the guest changed plans). No cause is claimed.
- `RecordCause` adds 1 to the cause. `RecordOutcome` fades the four counters with `Arrived` and `Failed` (x 0.975 per outcome). So the
  causes use the same window as the trigger.
- `DominantCause`: the largest counter wins. It must be at least 5 and at least half of the faded `Failed`. Else None, and the hub shows
  the plain tip.
- The counters are on `ClassTrack`, in memory only. Nothing is saved. A load starts them at 0, as it does the trigger.
- The row's `Detail` carries the cause (hub contract 55). The `[reach]` log line prints the four counters and the cause.
