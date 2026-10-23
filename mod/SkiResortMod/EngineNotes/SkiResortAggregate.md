# SkiResortAggregate: the per-resort sum rules behind the hub's resort rows

`SkiResortStatsSystem` collects one small input row per run, lift and lodge (ECS reads stay in the system), calls `Aggregate`, and
adds the fields that need the game (names, the sign's own score, tourism share and estimate, jobs, building upkeep). Tested by
tests/ResortBatch.

## Resort index

Inputs carry `Resort` = 0..resortCount-1 (the signs in the stable `SkiResortIndex` order) or -1 = Not linked; the output array has
resortCount + 1 entries, the last one being the Not linked row (always last; always sent).

## Rules, one entry per field

- runs / rated / band counts: every run of the resort; rated = RateState 1; band counts over rated runs by Band 0..4
  (SkiSlopeClassifier.cs:441-448); Unrateable = RateState 2/3/4 (no terrain / too large / failed: final); pending = runs - rated -
  unrateable (only RateState 0). Unrateable is C#-side only (dump, tests): the UI counts the runs whose band is -2 itself (no
  `ResortRow` field).
- runLengthM, areaM2: sum of `LengthM` (rated: `SlopeResult.Length`, else the `LengthRect`), sum of `AreaM2`
  (`Geometry.m_SurfaceArea` - PLAN area, GeometrySystem.cs:517).
- verticalM: max(run `TopM`, top-station tip y) - min(run `BottomM`, base-station tip y) over the resort's runs and its lifts'
  stations; -1 when nothing has a height; `verticalApprox` while any of its runs is PENDING (RateState 0: TopM/BottomM are node
  max/min until it is rated). An unrateable run also uses node heights, but they are final, so it does not keep the approximate
  mark forever.
- runVerticalM: max(run `TopM`) - min(run `BottomM`) over the resort's runs only; -1 when no run has a height.
- lifts, liftsRunning, liftsOpen, chairs, liftLengthM, liftRiseM: every lift row of the resort; running = State 0; open = State 2
  (a missing section - LIFTS_OPEN "{N} with a missing section"); chairs, `LengthM` and `RiseM` summed (RiseM summed like LengthM:
  "total lift rise").
- lodges, freeLodges, lodgeScore: lodges listed under the resort; freeLodges = how many of them are free-standing (only the Not
  linked row has any); lodgeScore = sum of Score, -1 with no lodge.
- runCostMonth, snowHa: sum over the resort's runs (bound runs bill; the caller passes 0 for an unbound run).
- snowPct, season: over the resort's runs that HAVE a cover value (Cover finite - the caller passes NaN for an unbound run, which
  makes no snow): snowPct = 100 x sum(cover x area) / sum(area) (-1 with no such run); season = 0 natural (any run with natural
  cover > 0), 1 snowmaking only (else any run with cover > 0), 2 too warm (else), -1 with no such run.
- HasAny: the row has any run, lift or lodge (the Not linked switcher entry is shown only then).

## Lift ordinals

`LiftOrdinals` numbers lifts per resort by top-station height descending, rows without a top last, ties by Id (Index, Version):
"Lift N" for a lift whose station has no name.

## Resort names

`SmallestFreeOrdinal` is THE "Ski area N" pick, the smallest N >= 1 whose formatted name is not taken. It is used by the sign
auto-name (`SkiRunBindTriggerSystem`) and by a hub rename to "" (it writes a fresh auto-name instead of removing the name).
`FallbackOrdinals` is the hub's display-only backstop for a sign that still has no name: in one pass over the signs in stable
order, each unnamed sign gets the smallest N whose name is neither a live sign's custom name nor an earlier unnamed sign's
fallback. Custom names always win, so the hub never shows two resorts with one name (positional labels collided with the
auto-name, which counts only custom names).
