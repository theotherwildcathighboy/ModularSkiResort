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
  school is active with a counted green run) outside the cap like W and P. Tourists ignore it. The old signature =
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
