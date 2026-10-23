# SlopeBatch: terrain stand-in, oracle design and run-length checks for the slope classifier tests

`SlopeBatch` tests `SkiSlopeClassifier` headlessly. Within-runtime replay is proven bit-identical; CoreCLR versus the game's Mono bit
equality is unverified, so no golden values are asserted. `WARN  SPEC-GAP` marks a spec invariant the classifier does not meet at a run's
operating step, with the measured numbers; an asserted regression envelope keeps the gap from widening. WARN lines do not count as
failures.

## Terrain stand-in

Test-local; the classifier only ever sees it through the public Plan -> Need -> Heights -> Classify contract.

- A 3.5 m texel lattice (14336/4096, `TerrainSystem.cs:2618-2620` in 1.6.0f1, :2630-2632 in 1.6.2f1).
- Anchored at the default map's texel centers (-7166.25 + 3.5k: `ToWorldSpace`, `TerrainUtils.cs:35-38`, with the -0.5 texel offset of
  `TerrainSystem.cs:2621-2622`).
- Heights quantized to 4096/65535 m (the default height scale).
- Sampled with the float bilinear of `TerrainUtils.SampleHeightInternal` (`TerrainUtils.cs:121-137`: heightmap space, floor, saturate, lerp
  along x per row, then along z).

## Oracles

They are deliberately different code from the classifier's:

- analytic plane grades and fall lines;
- the closed-form max 20 m window of piecewise-linear 1-D profiles (evaluated at the breakpoints);
- winding-number point-in-polygon (classifier: even-odd scanline);
- brute-force Chebyshev dilation (classifier: rectangle marking);
- sorted lists for the k-th selection (classifier: fixed top-k insertion).

`OracleCheck` runs on EVERY ring-mode classification this host makes (plus `RandomOracleTests`' random polygons and terrains) and
recomputes, by brute force and with its own data structures:

- the end zones (sorted bin table);
- the exact minimax (binary search over the sorted distinct per-cell values, a plain flood fill per threshold: 8-neighbor, or the diagonal
  rule);
- the expected rating (the largest value of the minimax's bucket below 200 %, the exact minimax in the open last bucket), P90 and
  Connected;
- the EXACT crux cell, from an independent BFS written from the traversal contract (row-major seeds, row-major neighbor order, stop on
  discovery, largest o nearest the top).

`OracleCheck` takes the per-cell values (`CellGrade`) as given; they are pinned separately. `KthCase` asserts `CellGrade` of EVERY Mask
cell against a per-cell oracle (20/15/10/7 m row windows, |g| fallback) on z-invariant profiles. `BankTests` and `ShortHeadwallTests`
assert `RatingGrade` of full-width (unavoidable) features against the closed-form max window; the plane / bank / ridge tests assert
`SteepestGrade`, a separate top-k list.

## The caller and the diagonal rule

The "caller" (`RateWith`, `ReplayCapture`, the TIME / allocation loops) sets `SlopeGrid.DiagonalRule` = step multiple >= 2, as
`SkiRunDrawFeedbackSystem` does. `OracleCheck` recomputes the zones under BOTH keys: its own double-precision mean box gradient decides
the key and checks `FallLine` / `MeanGradient`; the key gate's coherence clause (|mean g| >= 3 % OR `FallCoherence` >= 0.5) is decided
from its own double sums, and `FallCoherence` is asserted. It uses the m >= 2 connectivity (4-connected flood fill with the diagonal
exception) in its minimax and its crux BFS. `EndZoneTests` asserts the end-zone fixtures at m = 1 over 8 frames and reports the
known-and-deliberate curved runs (C-run, switchback) under the fall-line key and, through the test-only gate (+inf), the height key.

## Captures

The writer uses G9 / G17, the loader reads both, and the round trip proves the legacy "R" file parses to the same values.

## SkiRunRater parity

The Plan -> Fill -> Classify path lives in the pure `SkiRunRater` (linked here). `RaterParityTests` (`RaterParity.cs`) proves
`SkiRunRater.Rate` bit-identical to the direct calls and to verbatim copies of the pre-extraction `ShoelaceArea` / `FillHeights` /
`Recompute` middle on every capture (plus the reuse path and stepCurrent 0 vs 1 at m = 1) and on synthetic shapes at stepCurrent 0..3; it
runs with the fingerprint and the oracle off.

## Run length

- `LengthCheck` on every PostCheck'd classification: polyline arc length, `LengthRect` with an independent area formula, `Length` =
  max(`LengthRect`, `AxisLength`), zeros on a non-rating status.
- `OracleCheck` pins `AxisLength` to the oracle's key range + step.
- `CaptureTests` prints a length column and asserts each capture's `Length` (regression, +-0.1 %) and `LengthRect` (+-1 m).
- `LengthTests` runs 11 synthetic shapes (regression +-0.1 %, error versus the designed centerline as INFO).
- "Every existing rating bit-identical" is proven by the rating fingerprint (FNV-1a over every PostCheck'd classification outside
  `LengthTests`, printed before SUMMARY). The value changes whenever a test is added, so it is printed, not asserted.
