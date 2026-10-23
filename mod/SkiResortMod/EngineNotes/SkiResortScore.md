# SkiResortScore: how the attraction score of a ski area is computed

One attraction score per ski area (one Welcome Sign), from its member runs (band, size with the 1 ha knee, type, lift service, open
factor), its complete and powered lifts, its active lodges and the sign's efficiency. `SkiResortAttractivenessSystem` calls Compute on
every recompute; `SkiResortStatsSystem` / `HubRules` read the Result (the hub never recomputes); tests/ResortBatch ScoreTests.

## Compute in one breath

Per run `v = band x size x type x served x open` (each factor >= 0; `open = clamp01(OpenFactor)` with gating on, NaN -> 0, else 1). A
run counts when `v > 0`, and only counted runs feed RunsValue, variety, vertical and the summer test (summer <=> RunsCounted == 0).
RunsValue is the sum of the counted values in ascending order: the sorted sequence is the same for any permutation of the runs, so the
Result is bit-identical whatever order the chunk walk returns (`Result.Equals` stays exact; the attractiveness Generation does not move
on a chunk reorder). A is clamped before rounding: a non-finite double never reaches an int cast.

## Ski-day inputs

They come out of the same recompute, so score, capacity and the gate come from one snapshot:

- RunsValueFull: sum of band x size x type x served over all member runs, open ignored (the denominator of `SkiDayRules.OpenShare`).
- RunsCapacity: sum of `RunSkiersPerDay` over counted runs (skiers per hectare by band, natural x 0.5, x served x open).
- NightRunsCounted, TopKnown / TopM (the counted-run top, for powder).

Both sums are ascending like RunsValue, so Equals stays exact in any run order.

## Lodge ladder

Per active lodge a catalog row (`SkiCatalog.Lodges`: B base, E extra, mu summer share, mu summit when summit-served).

- LodgeBase (winter selection, mu = 1 for every lodge) = B(best) + min(sum E(others), LodgeExtraCap 24).
- LodgeApplied = the season's selection. In winter = LodgeBase. In summer: best B*mu + extras, where
  `mu_i = (SummitServed && mu summit > mu) ? mu summit : mu` and extras = (sum E*mu over the others, ascending) x cap / sum E when
  sum E > cap (sum first, then scale: uniform mu gives mu x min(sum E, 24) exactly).
- Every active lodge counts, of any tier and in any number; the extras cap is the only limiter.
- Order-free: "better" = higher B*mu, then higher E, then higher Capacity, then lower LodgeIndex. Two lodges that tie on all four are
  the same row with the same mu, so which one counts changes no number.
- AOpen = LodgeApplied + K x sqrt(S). For A-frames only, LodgeApplied = LodgeBase x 0.5 exactly in summer.
- LodgeShare = `LodgeBase > 0 ? applied / base : (summer ? 0.5 : 1)` (display; the season share holds when there is no lodge).
- `AreaInput.Lodges == null` means ActiveLodges A-frame rows (the closed form `LodgeBaseOf(n)`, x 0.5 in summer; LadderTests proves it
  equals the ladder over explicit A-frame lists bit for bit).

## Lifts per type

`LiftInput.TypeIndex` is the `SkiCatalog.Lifts` index (0 = fixed-grip). LiftsValue = sum ScoreValue (winter, displayed); LiftsApplied =
sum (summer ? SummerShare x ScoreValue : ScoreValue), both ascending; S uses LiftsApplied; LiftShare = applied / value (display).

## School and patrol

`S += SchoolRule.ScoreValueOf(area.SchoolActive, GreenCounted)` before variety and vertical. Patrol: `RunInput.Patrolled` ->
`PatrolRule.TypeFactorOf` / `ServedFactorOf` in the run part.

## Threading and platform

Main thread and the test host only: the catalog rows are managed statics, and Compute uses static scratch lists (not re-entrant, not
thread-safe). net48 traps (the mod build is the API gate): no `Math.Clamp`, `MathF`, `double.IsFinite`, `Span<T>`; the private Clamp /
IsFinite helpers stand in. Arithmetic is double (SignEfficiency float, as vanilla's).
