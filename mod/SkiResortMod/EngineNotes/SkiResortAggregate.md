# SkiResortAggregate: the per-resort sum rules behind the hub's resort rows

`SkiResortStatsSystem` collects one small input row per run, lift and lodge (ECS reads stay in the system). It calls `Aggregate`. Then
it adds the fields that need the game (names, the sign's own score, tourism share and estimate, jobs, building upkeep). Tested by
tests/ResortBatch.

## Resort index

Inputs carry `Resort` = 0..resortCount-1 (the signs in the stable `SkiResortIndex` order) or -1 = Not linked. The output array has
resortCount + 1 entries. The last entry is the Not linked row. It is always last and always sent.

## Rules, one entry per field

- runs / rated / band counts:
  - runs counts every run of the resort.
  - rated = RateState 1.
  - Band counts cover the rated runs by Band 0..4 (SkiSlopeClassifier.cs:441-448).
  - Unrateable = RateState 2/3/4 (no terrain / too large / failed: final).
  - pending = runs - rated - unrateable (only RateState 0).
  - Unrateable is C#-side only (dump, tests). The UI counts the runs whose band is -2 itself (no `ResortRow` field).
- runLengthM, areaM2:
  - runLengthM is the sum of `LengthM` (rated: `SlopeResult.Length`, else the `LengthRect`).
  - areaM2 is the sum of `AreaM2` (`Geometry.m_SurfaceArea` - PLAN area, GeometrySystem.cs:517).
- verticalM = max(run `TopM`, top-station tip y) - min(run `BottomM`, base-station tip y). The range covers the resort's runs and its
  lifts' stations.
  - It is -1 when nothing has a height.
  - `verticalApprox` applies while any of its runs is PENDING (RateState 0). Until the run is rated, TopM/BottomM are node max/min.
  - An unrateable run also uses node heights. These are final, so such a run does not keep the approximate mark forever.
- runVerticalM = max(run `TopM`) - min(run `BottomM`) over the resort's runs only. It is -1 when no run has a height.
- lifts, liftsRunning, liftsOpen, chairs, liftLengthM, liftRiseM: these fields use every lift row of the resort.
  - running = State 0.
  - open = State 2 (a missing section - LIFTS_OPEN "{N} with a missing section").
  - chairs, `LengthM` and `RiseM` are summed. RiseM is summed like LengthM: "total lift rise".
- lodges, freeLodges, lodgeScore:
  - lodges = the lodges listed under the resort.
  - freeLodges = how many of them are free-standing (only the Not linked row has any).
  - lodgeScore = sum of Score, -1 with no lodge.
- runCostMonth, snowHa: both are sums over the resort's runs. Bound runs bill. The caller passes 0 for an unbound run.
- snowPct, season: both use the resort's runs that HAVE a cover value (Cover finite). The caller passes NaN for an unbound run, which
  makes no snow.
  - snowPct = 100 x sum(cover x area) / sum(area). It is -1 with no such run.
  - season = 0 natural (any run with natural cover > 0).
  - season = 1 snowmaking only (else any run with cover > 0).
  - season = 2 too warm (else).
  - season = -1 with no such run.
- HasAny: the row has any run, lift or lodge. The hub shows the Not linked switcher entry only then.

## Lift ordinals

`LiftOrdinals` numbers lifts per resort by top-station height, descending. Rows without a top come last. Ties go by Id (Index, Version).
The label is "Lift N" for a lift whose station has no name.

## Resort names

`SmallestFreeOrdinal` is THE "Ski area N" pick, the smallest N >= 1 whose formatted name is not taken. The sign auto-name
(`SkiRunBindTriggerSystem`) and a hub rename to "" use it. A rename to "" writes a fresh auto-name instead of removing the name.
`FallbackOrdinals` is the hub's display-only backstop for a sign that still has no name. It makes one pass over the signs in stable
order. Each unnamed sign gets the smallest N whose name is neither a live sign's custom name nor an earlier unnamed sign's fallback.
Custom names always win, so the hub never shows two resorts with one name. Positional labels collided with the auto-name, which counts
only custom names.
