# SkiSlopeClassifier: how a ski run is rated (crux of the easiest line), its end zones, difficulty layer, measured limits and engine facts

`SkiSlopeClassifier` is a pure, deterministic slope-difficulty rating for a ski-run polygon. It is Entities-free on purpose. Its usings
are System, System.Collections.Generic and Unity.Mathematics ONLY. So the same file is compiled into the net48 mod (default `**/*.cs`
glob) AND linked into the net8 headless test host tests/SlopeBatch. To run the host, use `dotnet run --project tests\SlopeBatch\SlopeBatch.csproj -c
Release` from the repo root. Nothing here is copied from ParkManager.

Contract:

* No static mutable state (consts only).
* It never logs. It never throws on bad input.
* Every degenerate input returns a `SlopeStatus` and leaves the outputs cleared.
* A tampered grid (public fields edited after `Plan`) cannot index out of range.
* Zero managed allocation per call after the caller's reused `SlopeGrid` / `SlopeResult` buffers have grown. Buffers grow and never shrink.
* The code uses fixed top-k insertion arrays, a counting sort, union-find, a binary heap (open last bucket only) and BFS on reused arrays, with
  per-row run lists. It uses no Sort and no LINQ.

Cross-runtime bit equality (CoreCLR test host vs the game's Mono) is unverified. Within one runtime, a replay is bit-identical (proven
in SlopeBatch).

## What the rating is

A resort rates a run by its hardest part that a skier cannot AVOID. `RatingGrade` = the crux of the easiest line. Take every connected path of inside cells from the run's top end zone
to its bottom end zone. For each path, find the maximum per-cell value o on it. `RatingGrade` is the minimum of these maxima (a
bottleneck / minimax path). A steep corner or side bank that the line can go around does not count. A full-width headwall or a chute does count.
Rating the steepest ~86 m² instead read a corner touching a bank as Double black on a 17 % mean run.

* Per-cell value o (`CellGrade`, with `CellBand` = `BandOf(o)`): the centered 20 m fall-line window when it fits the Mask. Otherwise it is the
  longest centered window of 15 / 10 / 7 m that fits. If none fits, it is |g|.
* Connectivity: `SlopeGrid.DiagonalRule`, set by the CALLER (true when its step multiple m >= 2).
  * false: 8 neighbors.
  * true: the 4 orthogonal neighbors, plus a diagonal only when BOTH cells it shares orthogonally with the step are outside the Mask.
  * The rule applies in the union-find AND the crux BFS.
  * At 7 m cells, an 8-connected path slipped diagonally through a one-cell-wide band of high values (a full-width 12 m bank @ 50 % read up
    to 8.1 % low).
* End zones. KEY: when the mean box gradient |g mean| >= `EndZoneMinMeanGrade` (3 %) OR `FallCoherence` = |sum g| / sum |g| >=
  `EndZoneMinCoherence` (0.5), the key is the position along the mean fall line, key = -(FallLine . (i, j))*step (`ZoneKey`, computed inline per
  cell). Otherwise (cones, craters, saddles, flat ground: no usable axis) the key is the node height. The code publishes this as `ZoneByFallLine`.
  Coherence does not REPLACE the |g mean| clause. Coherence falls with wall steepness, so a coherence-only gate would put steep-sided valleys
  back on the height rule. The coherence clause is ADDED because the |g mean| clause alone sent a long gentle run whose gradients all agree to the
  height key and the area cap. The area cap skipped a full-width pitch near an end (the band flipped as the end node crossed the gate).
  * SIZE: z = clamp(round(`EndZoneShare` * n), 1, max(1, cap)). The cap depends on the key:
    * Fall-line key: cap = floor(n * `EndZoneDepthM` / L). L = key range of the inside cells + step. So the zone is `EndZoneDepthM` of run at the
      run's MEAN width. The cap is a cell count, so the zone is deeper at an end narrower than the mean (limit (g)).
    * Height key: cap = floor(`EndZoneMaxM2` / step^2).
  * SELECTION: 1024 equal bins over the key's inside range. Selection takes whole bins from each end until it holds >= z cells (deterministic,
    independent of scan order). The code reads the key range from the clamped Mask runs' END cells (the key is linear along a row and float rounding
    is monotone: exact, O(runs)). The same `ZoneKey` / `HeightBin` call bins every cell in the histogram (pass 1) and the zone bits (pass 2).
    The BFS reads those bits. `FallLine`, `MeanGradient` and `FallCoherence` are final before `Crux` on every path.
  * The rating uses zones, not the single highest / lowest cell, because a drawn run's entry and exit are unknown and its very top is exactly where
    it pokes onto a bank. The cap stops a long runout from swallowing a short pitch at the top. Zones may overlap (flat ground): a
    shared cell is a zero-length path.
* The open last bucket: bucket 2047 is OPEN ([199.9 %, inf)). There, "the largest o in the bucket" would be the largest value of any
  cell above 199.9 %, avoidable or not. Example: a full-width 235 % step plus an avoidable 60 m pit rated 310.7 % with the crux ring in the
  pit. So the code activates its cells in ascending o (a binary min-heap over its Order segment) with a TOP ~ BOTTOM test per cell.
  `RatingGrade` is then the EXACT minimax there. In every other bucket, `RatingGrade` is the largest o in the connecting bucket. This value is
  order-independent and at most one bucket width (1/1024) above the exact minimax (SlopeBatch checks every ring rating against a brute-force oracle).
* Not connected (the Mask falls apart between the zones: self-intersecting or hair-thin polygons): `Connected` = false and
  `RatingGrade` = `P90Grade`.
* Crux cell (`CruxI` / `CruxJ`, `CruxHeight`, the overlay's marker): a BFS runs over the traversable cells (o <= `RatingGrade`) from the
  top-zone cells in row-major scan order. It expands neighbors in row-major order (NW N NE W E SW S SE). It stops when the first
  bottom-zone cell is DISCOVERED (not dequeued). The crux is the path cell with the largest o, the one nearest the top on ties. A seed
  that is also in the bottom zone is the crux at once. The contract is pinned so that SlopeBatch's independent BFS can assert the cell
  exactly.
* Polyline mode keeps its statistic: a line has exactly one path, so its k-th window IS its crux.

## Difficulty layer

This layer covers an IRL "hard blue", run width, and the rule that no cat track becomes a black diamond. The layer does not change `RatingGrade`
or any crux output. It only reads them. `SlopeResult.Band` is `BandOf(RatingGrade)`, moved by `FinishBand`. This happens in ring mode with a valid
easiest line only. The rules are:

1. Green-mean runs (`MeanGrade` < `GreenMeanKeepsBlueMax`) rate at most blue, unless the crux pitch is >= `CruxLengthForBlack`.
2. Otherwise, a borderline black (double black) crux rates one band lower when both conditions hold: its steep-cell share is below
   `SteepShareForBlack` (`DoubleBlackShare`) and the mean is below its floor. The exception: the pitch is long, or the steep section is narrower
   than `NarrowWidthM` (narrow cells count double in the share).
3. A blue crux < `WideEase` above the green/blue boundary on a run wider than `WideRunM` with a green mean rates green.

Pitch length = (meters of the recorded easiest line (`SlopeResult.Path`, written by the crux pass) at or above the crux band's floor) +
`PitchBlurM` + `EndZoneDepthM` per end it touches. Widths = Mask extent across the mean fall line over the lattice already built (no
terrain sampling). The code publishes `CruxBand` / `BandGrade` / `Note` and the evidence fields for the tooltip and the tests.

Limits:

* Widths need a usable fall line (`ZoneByFallLine`).
* The easiest line is the BFS-shortest traversable path, so its stretch follows the pitch the line crosses.
* The pitch estimate wobbles about +-5 m on a 3.5 m lattice (a 15 m / 50 % pitch reads 34-43 m), so "40 m" is a soft edge.

## Known and deliberate

* (a) A headwall or drop-in entirely inside an END zone is skipped, at the top AND the bottom. The fall-line zone is ~`EndZoneDepthM`
  of run at the mean width, deeper at a narrower end, (g). On the height key, the area cap still skips `EndZoneMaxM2` / width at each
  end.
* (b) Not monotone under supersets: widening a run can add an easier line (and it moves the mean fall line, hence the zones).
  `SteepestGrade` is still monotone.
* (c) A narrow traverse is still rated by its sidehill (no run-axis input).
* (d) Among equal-valued cells the crux follows scan order (deterministic).
* (e) A run that curves strongly around convex terrain (fall line turning with the run): position along the MEAN fall line is not
  position along the run. On a C-run the arms' along-run fall lines cancel in the mean. So the fall-line key puts both arms' far ends
  in the top zone and the bend in the bottom zone, and the easiest "line" climbs the gentle last arm. The result is lenient (15.6 % Green against
  41 % Black on the height key).
* (f) A full-length avoidable side cliff covering about 40-55 % of the width or more tilts the mean fall line onto itself.
  The rating varies with orientation.
* (g) The depth cap is a cell COUNT. So at an end narrower than the mean, the zone runs `EndZoneDepthM` x mean / end width down the run.
  In a 20 m trail opening into a 150 m bowl, a top zone ~105 m deep skips a full-width pitch within it. Capping each bin walk at
  2.5 x `EndZoneDepthM` of key was measured and NOT applied. It fixes those rows, but it moves (f) to Blue..Black and corner pokes toward
  Blue. A pure 20 m key-depth cap breaks slanted top edges.
* (h) The key is a position, never height. So a polygon drawn more than ~`EndZoneDepthM` past a crest (or valley floor) puts the top
  (bottom) zone on the back slope, and the back slope is rated. Extending each zone to the highest (lowest) cell's bin was measured and
  NOT applied. The reason is that a knoll in the top quarter, higher than the top edge, pulls the zone down to it.
* (i) "Large corner pokes read Green" holds for a top edge roughly ALONG the contours. With the polygon rotated or its top edge
  slanted, the count-capped top zone collects in the highest corner wedge. Pokes of 60 x 80 and up then read Blue to Black.
* (j) Below 3 % mean gradient, terrain whose gradients partly cancel (coherence < 0.5) keeps the height key and area cap and their
  limits. Example: a 30 m drop-in atop a 1 % run with a +-1 m wave reads Black at 700 m but Flat at 1,500 m. The coherence clause moves the band
  flip from every coherent gentle run near 3 % to textured or canceling terrain.
* (k) The `ShortRun` -> `Ok` transition as a small run grows can LOWER `SteepestGrade` (shorter windows read steeper). Values carry up
  to +0.3 % height-quantization bias on planes, hence `DisplayHysteresis` for the live readout.

## Measured limits of the crux

The line follows the LOWEST-reading cells, so per-cell under-reads become rating under-reads.

* Knife-edge ridge (12 % crest, 25 % flanks): the line runs down the crest. Its cells read 12 % on a node and up to ~20 % between
  nodes (all Green). Rotation shifts the reading by up to ~1.8 %.
* A full-width pitch SHORTER than ~17 m: its plateau (20 m minus the pitch length) is narrower than a 3.5 m cell. So lattice nodes
  straddle the peak, and every per-cell value reads below it (a 20 m 50 % pitch ~4.7 % low, 16 m ~2 % low). 12 m / 5 m banks and 60 m
  pitches read within 0.4 %.
* Next to steep ground outside the polygon, box gradients tilt toward it and in-run windows run off the fall line, reading
  g * cos(tilt).
* Lattice resolution: a valid window's endpoint may lie up to half a cell past the last inside node (rounded mask check). So its
  bilinear can read ONE node beyond the polygon. Terrain within one step outside the edge leaks into `SteepestGrade` (a 60 % cliff at
  the boundary of a 10.5 % run reads up to +4.7 %). The crux avoids those edge cells. Stricter endpoint rules are listed in `Window`.
  A coarse step blurs narrow features. `StepMultiple` budgets by AREA (`CellBudget` 16384), so only a run over 200,704 m^2 gets m = 2
  (longer than 2,007 m at 100 m wide). There, a 12 m bank's `SteepestGrade` reads up to 2.66 % low and its crux up to ~3 % low with
  the diagonal rule (an 8-connected slip read up to 14.7 % low). A crest running diagonally across the lattice reads up to ~20.5 %
  instead of ~14 %.

## Run length

Run length uses HORIZONTAL map meters (< 5 % from along-the-slope meters below a 30 % grade). The code publishes it as `SlopeResult.Length` / `LengthRect` /
`AxisLength`. These values are geometry only, and NOTHING in the rating reads them.

* Ring: `Length` = max(`LengthRect`, `AxisLength`). `LengthRect` is the centerline of the rectangle with the ring's perimeter and area
  (`EquivalentRectLength`), exact for any constant-width band. `AxisLength` = the fall-line key range + step when `ZoneByFallLine`,
  else 0. It rescues outlines whose perimeter under-states their length. It over-reads a straight run by up to about one cell.
* Polyline: the horizontal arc length.
* Known limits: a bowl funneling into a trail OVER-reads (+11 % on a 200 m bowl into a 400 m trail: its wide top adds perimeter, not
  length). A diamond or tapered outline on the height-key fallback reads `LengthRect` alone (a 500 x 240 m diamond on flat ground
  reads 407 m).

## Engine facts this rests on

`Game.dll` 1.6.0f1 citations. The caller relies on them too.

* One height texel = 1 / `hd.scale.x` world meters: scale = float3(res.x, res.y-1, res.z) / float3(14336, heightScale, 14336)
  (Game.Simulation/TerrainSystem.cs:2618-2620 and 1.6.2f1 :2630-2632) = 3.5 m on a 4096-wide heightmap. Vertical quantum = heightScale /
  65535 (about 0.0625 m at the default 4096). Texel centers sit at world -`hd.offset.xz` + k*texel (TerrainUtils.cs:35-38). Offset
  carries the -0.5 texel shift (TerrainSystem.cs:2621-2622). So the caller anchors the lattice there, and the lattice is WORLD-fixed.
* `TerrainUtils.SampleHeight(ref data, pos)` is a plain bilinear over four texels (Game.Simulation/TerrainUtils.cs:82-93 dispatch,
  :121-137 `SampleHeightInternal`). At step multiple m = 1 the lattice nodes ARE the texels, so this file's own bilinear over the node
  heights reproduces the game's surface exactly (no double smoothing).
* The three-argument overload's `normal` is in RAW heightmap units per texel (TerrainUtils.cs:139-157, built at :154-155). So
  |n.xz| / n.y overstates grade by scale.y / scale.x (about 56x at the default height scale). This file never uses it.
* Area triangles are NOT terrain-conformed between nodes. `GeometrySystem` triangulates the node positions
  (Game.Areas/GeometrySystem.cs:101-113) and keeps terrain only as `Triangle.m_HeightRange`. So a triangle plane is the node-to-node
  average, which the rating rules out. Grade therefore comes from height differences on a lattice. `GeometrySystem` triangulates an open
  polyline with >= 3 nodes as if closed (GeometrySystem.cs:99-112), hence ring mode below.

## Plan and Classify, in short

`Plan(points, count, latticeOrigin, step, grid)` does these things:

* It sanitizes the input:
  * NaN/Inf -> `NonFinite`.
  * step <= 0, NaN or > 100 m -> `BadStep`.
  * It strips consecutive duplicates and a duplicated closing vertex (xz within 1 mm).
  * < 2 points -> `TooFewPoints`.
* It picks ring mode (>= 3 points and |shoelace| >= 1 m^2) or polyline mode.
* It rasterizes the Mask (cell CENTER = lattice node inside, > 1 << 21 bbox cells -> `TooLarge`).
* It sets Need = Mask dilated by `Dilation` (Chebyshev). The 5x5 box gradient reads +-3 nodes, and the bilinear lookups read the +1 neighbor.
* It records both as exact per-row RUN LISTS (`SlopeGrid` `MaskRuns` / `NeedRuns`) that every later pass walks.
* It writes NaN into every Need height, so a node the caller forgets to fill surfaces as `NonFinite` instead of rating garbage.

The CALLER fills Heights for every Need node (world (Origin.x + (I0 + i)*Step, Origin.y + (J0 + j)*Step), q = j*Nx + i) by walking
`grid.NeedRuns`. `Plan` returns `Ok` in BOTH modes (`grid.PolylineMode` tells them apart).

`Classify(grid, result)`, ring mode, per Mask cell:

* g = box gradient (25 central differences / (50*step)). It reads unmasked Need heights, so a cell's value never depends on where the polygon
  edge is (superset polygons keep every old window bit-identical).
* window = |H(p - 10 d) - H(p + 10 d)| / 20 with d = -normalize(g). The window is VALID only when the rounded node at every check point along
  the segment is a Mask cell. The drop is ABSOLUTE on purpose: on bowls, saddles and washboards the box direction can point uphill relative to
  the 20 m chord.
* `SteepestGrade` = the k-th largest FULL window, k = max(`MinRatingCells`, ceil(`RatingAreaM2` / step^2)). The count k is an ABSOLUTE area,
  never a fraction of the run (a fraction dilutes with length). With fewer than k full windows, the status is `ShortRun` and `SteepestGrade` =
  the k-th (or last) largest overlay value.

Polyline mode: window |H(s) - H(s + 20)| / 20 over arc length at ds = step / 2. Rating = k-th largest with k = max(`PolylineMinWindows`,
ceil(8 / ds)) -> Status `Polyline`. Fewer windows -> `ShortRun`. `FallLine`, `FallCoherence` and `Area` stay 0. `CellBand` is
`BandOutside` everywhere, and `CellGrade` is not meaningful (no Mask).

## Bands

Flat < 6 % <= Green < 25 % <= Blue < 40 % <= Black < 60 % <= Double black. The intervals are half-open: band = number of thresholds <= grade (exactly
25 % is Blue). Grades are rise/run fractions (0.25 = 25 %), not degrees. 60 % for double black is our choice (no numeric convention
exists).

`MinRatingCells` (7) and `PolylineMinWindows` (11) are the smallest values that keep a +-3 m single-texel spike to "same band,
|delta| <= 1 %". The values 5 and 3 do not (numbers at the constants). The crux needs no such count: a spike raises a few cells, and the easiest
line goes around them.

## Cost

All passes walk the exact per-row RUN LISTS. So managed work is O(inside) / O(Need) whatever the shape. Managed work also includes O(bbox) memsets
(`Plan`'s `Array.Clear` of Mask and Need, `FillOutside` of `CellBand`). A walk over each row's first..last span would bridge the outside gap of a
row that crosses two arms of a concave run. For a 15 m x r1500 C, that is 37x the inside cells.

The crux pass is O(n) (O(n + k log k) in the open last bucket). Its per-cell state lives in a bbox-sized int buffer (`Uf`, up to 8 MB
at `MaxBboxCells`, retained). The pass initializes the buffer over the Mask runs (dilated by one cell) only. Its cost is memory latency (bucket order
visits cells at random). Hence the pass does one union per run of active neighbors (8-neighbor path) and packs zone bits into the bucket-ordered
entries. The bbox is bounded only by `MaxBboxCells`, not `CellBudget` (a 2-node 5 km diagonal at m = 1 has ~1.04M bbox cells).

Budget 1.5 ms per classification (SlopeBatch TIME, CoreCLR): the costliest convex m = 1 run (2.5 km x 80 m) takes 1.1-1.5 ms. The
game's Mono ran an earlier version at ~5-8x CoreCLR. This version is unmeasured.

## Near SlopeNote

* `None` = the crux band stands.
* `Capped` = a black (double black) crux capped one band down by the steep-cell share.
* `WideEase` = a crux within `WideEase` of the green/blue boundary on a wide gentle run rounded down to green.
* `NarrowKept` = the cap WOULD apply (few steep cells, short pitch), but the steep section is narrower than `NarrowWidthM`, so the crux band
  stands. The deciding factor is the width.
* `LongKept` = the cap would apply, but the crux pitch is at least `CruxLengthForBlack` long. The length decides.
* `GreenMean` = a black or double black crux on a run whose MEAN grade is in the green band, rated blue (no cat track becomes
  a black diamond).

## Near SlopeGrid.MaskRuns

The grid stores the Mask and Need as per-row RUN LISTS. A per-row first/last span would bridge the outside gap of every row that crosses two arms
of a concave ring. Row j's runs are the inclusive column pairs (`Runs[2k]`, `Runs[2k + 1]`) for k in [`RowStart[j]`, `RowStart[j + 1]`).
`RowStart` has Nz + 1 entries. Runs are EXACT, sorted, disjoint and non-adjacent. Every cell of a Mask run is a Mask cell, and every Mask cell lies
in exactly one Mask run (same for Need). Ring mode has Mask and Need runs. Polyline mode has Need runs only (no Mask run).
Consumers (`Classify`, `SkiRunDrawFeedbackSystem`'s fill / strips / capture, SlopeBatch) walk these, so managed work is O(Mask) /
O(Need), never O(bbox). The exception is the bbox-sized memsets (`Plan`'s `Array.Clear` of Mask and Need, `Classify`'s `FillOutside`).

## Near SteepShareForBlack (end-zone and difficulty constants)

Crux end zones. The key is the position along the mean fall line (key = -(FallLine . (i, j))*step) when the mean box gradient |sum g| / n >=
`EndZoneMinMeanGrade` OR `FallCoherence` = |sum g| / sum |g| >= `EndZoneMinCoherence` (see "What the rating is"). Otherwise the key is the node height.
Size: z = clamp(round(`EndZoneShare` * n), 1, max(1, cap)) inside cells at each end. On the fall-line key, cap = floor(n * `EndZoneDepthM` / L).
L = key range of the inside cells + step. So the zone is ~`EndZoneDepthM` of run at the run's MEAN width, deeper at an end narrower
than the mean (limit (g)). On the height key, cap = floor(`EndZoneMaxM2` / step^2) (the area cap). The zone takes whole histogram bins of the key from each
end. `RatingBuckets` = the counting-sort resolution of the per-cell values: bucket = min(2047, (int)(o*1024)), i.e. 1/1024 = 0.098 %
grade wide up to 200 %.

The difficulty layer applies AFTER the crux (see `FinishBand`). Each constant follows, with its reason:

* `SteepShareForBlack` 0.15: a black crux is capped at blue ("hard blue") when fewer than 15 % of the run's cells read at/above 40 %.
  A few steep cells are a pitch, not a black run.
* `DoubleBlackShare` 0.10: the same test against the 60 % band, one step tighter. A 60 % face is rarer, and the cap only drops a double
  black to black, never to blue.
* `CruxLengthForBlack` 40 m: never cap a crux pitch this long along the easiest line. A 40 m wall is black whatever its share of a
  long run. Measured as `CruxStretchM` + `PitchBlurM`.
* `PitchBlurM` 20 m: the 20 m window smears a wall of length L into about L - 15 .. L - 5 m of line at/above its floor (50 m @ 55 %
  reads 42 m, 40 m @ 45 % reads 25 m). The estimate adds the window length, so a 40 m wall clears 40 m.
* `CapMaxOverFloor` 0.10: only a BORDERLINE band is capped (crux < floor + 10 pp, "a borderline black"). A 20 m wall at 62 % is black
  however few the cells.
* `NarrowWidthM` 20 m: a steep section narrower than this counts double in the steep share and can never be capped (narrow + steep =
  harder). A 20 m chute leaves no room to turn.
* `WideRunM` 60 m, `WideEase` 0.02: a crux within 2 pp above the green/blue boundary on a run wider than 60 m with a blue-or-gentler
  mean rounds down to green. Room to turn makes the same slope easier.

## Near SteepestPatchShown

The tooltip's "Steepest patch (avoidable)" line shows only when ALL of these hold. The classifier keeps this display rule, pure and
Entities-free, so the net8 test host can sweep it (SlopeBatch `TooltipLineTests`).

* (a) `Connected`: with a broken Mask the rating is the P90 fallback, not a crux, so no patch can be called "avoidable" relative to it.
* (b) Both grades finite.
* (c) `SteepestGrade` >= `RatingGrade` + `DisplayHysteresis` (0.5 %): on a uniform plane, height quantization alone puts `SteepestGrade`
  up to +0.29 % above the crux (a sweep of near-threshold plane cases: 61-68 of 936 fired without it).
* (d) The two rounded percentages differ (the tooltip shows round(100*g)), so the line never repeats the crux line's number.
* (e) `BandOf(SteepestGrade)` is above the SHOWN band.

Polyline mode never shows it (the feedback system publishes `SteepestGrade` == `RatingGrade` there, which fails (c)).

## Near Rasterize

`Rasterize` uses an even-odd scanline per lattice row (local v = j exactly) in double. A node is inside when it lies in [x_2k, x_2k+1] of the sorted
crossings. It records each row's inside runs into `grid.MaskRuns`. Sorted crossings give ascending runs. Two consecutive pairs can share
or touch one node only when x_2k+1 and x_2k+2 meet at a node. The code then merges them, so the runs are disjoint and non-adjacent. `Rasterize` returns
the inside count, or -1 when the polygon has more than `MaxCrossings` row crossings (`Plan`: `TooLarge`).

Edge bucketing: testing every edge against every row is O(rows * vertices), 0.42 ms of `Plan` for a 662-vertex C-shaped run on
CoreCLR. Row j crosses edge (k, l) iff (v_k > j) != (v_l > j), i.e. iff min(v_k, v_l) <= j < max(v_k, v_l) for an integer j. So each
edge visits exactly the rows ceil(min) .. ceil(max) - 1. A count pass, a prefix sum and a fill pass bucket every crossing by row in
O(rows + vertices + crossings). The code then insertion-sorts each row's bucket (few crossings per row, no Sort). A sorted list of equal
values is the same list whatever the insertion order, so the Mask does not depend on the bucketing.

## Near EquivalentRectLength

The centerline length of the rectangle with the ring's perimeter P and area A, (P + sqrt(max(0, P^2 - 16A))) / 4, in horizontal meters
(xz only). P includes the closing edge explicitly (`Plan` stripped the duplicated closing vertex, so pts[m - 1] -> pts[0] is an edge
here). A = |shoelace| relative to point 0, in double. The result is exact for an L x W rectangle (P^2 - 16A = 4(L - W)^2, the root returns the longer
side). It is also exact for any constant-width band whose area is centerline x width and whose perimeter is 2*centerline + 2*width (mitered polygonal
bends, circular arcs). Shapes far from a band clamp the root at 0 (a circle reads P / 4). The function returns 0 for fewer than 3 points, a short
buffer (tampered grid) or a non-finite result.

## Near Crux

`RatingGrade` = min over all connected Mask paths from the top end zone
to the bottom end zone of the max per-cell value o along the path. Paths are 8-neighbor, or follow the diagonal rule when `result.DiagonalRule`. The code finds it with a counting sort into `RatingBuckets` buckets and a union-find
with TOP / BOTTOM virtual nodes. Then a BFS finds the crux cell. The pass also outputs `P90Grade`, `Connected`, the zone sizes and `ZoneByFallLine`.

End zones: the key is `ZoneKey` (position along the mean fall line) when the `FallLine` is non-zero and `MeanGradient` >=
`endZoneMinMeanGrade` OR `FallCoherence` >= `endZoneMinCoherence`. Otherwise the key is the node height. The cap is `DepthCap` (fall-line key) or
`AreaCap` (height key). See "What the rating is".

"Inside cell" here = a cell the per-cell loop processed, i.e. `CellBand` != `BandOutside`. (`CellBand` is memset to `BandOutside` every
`Classify`, and the loop writes 0..4 exactly for the Mask cells of its clamped Mask runs.) Every processed cell lies >= `Dilation` (3)
from the grid border. So its 8 neighbors are in range even on
a tampered grid. The diagonal rule's shared orthogonal cells are among those neighbors. The pass initializes per-cell state over the clamped Mask RUNS dilated by one cell (O(inside + runs), never O(bbox) and
never O(row span)). So every state this pass reads is fresh.

`Uf` encoding (one int per flat index, Nx*Nz + 2 long):

* Pass 1 sets every cell of the dilated runs to `Inactive` (0). Only inside cells are ever activated, so "Uf != 0" alone means
  "an active inside cell".
* 0 is never a parent pointer. Parents are inside cells, which lie >= `Dilation` from the border (never flat index 0), or TOP / BOTTOM.
* Activation makes a cell a root. Roots hold -size. Other active cells hold their parent (> 0).
* After the union-find, the pass resets the active cells to `Unvisited` ORed with their Order entry's zone bits (`UnvisitedTop` / `UnvisitedBottom`).
  So the BFS uses exactly the union-find's zones. The active cells are every cell with o <= `RatingGrade` = the traversable cells (see the last bucket
  below). `Uf` becomes the BFS predecessor map (-1 = seed, > 0 = predecessor's flat index).
* Everything else in the dilated runs is still `Inactive`, so "Uf < UnvisitedLimit" alone means "traversable and not yet reached".

The open last bucket. Bucket 2047 holds every o >= 2047/1024 (199.9 %). So "the largest o in the connecting bucket" would be the
largest value of ANY cell above 199.9 %, avoidable or not. One case: a full-width 235 % step plus an avoidable 60 m pit rated
310.7 % with the crux in the pit. When the union-find reaches bucket 2047 unconnected, the pass activates that bucket's cells in
ASCENDING o. It uses an in-place binary min-heap over its Order segment (O(k log k) for its k cells, no Sort, deterministic). It runs a
TOP ~ BOTTOM test after every cell. `RatingGrade` is then the EXACT minimax (the o of the connecting cell). The traversable set
is every cell with o <= `RatingGrade` (ties included). Below 200 % nothing changes: the traversable set "buckets <= connecting" IS
"o <= RatingGrade" there.

## Near Crux (union-find and crux BFS)

The union-find works bucket by bucket (ascending) and stops at the first connecting bucket. Activation turns `Inactive` into a size-1 root and
joins the cell to its ACTIVE inside neighbors under the grid's connectivity rule.

* 8-neighbor (`DiagonalRule` false): the code walks the neighbors as a cycle (NW N NE E SE S SW W). Consecutive neighbors are 8-adjacent to each
  other. So by induction on activation order, every run of consecutive active neighbors is already one set. One union per run gives
  exactly the partition of unioning with all of them. This is usually one union instead of up to eight, and the union-find is most of this
  pass's cost.
* `DiagonalRule` (step multiple >= 2): N W E S, plus a diagonal only when both cells it shares orthogonally with q are outside the
  Mask. Two orthogonal neighbors of q are NOT adjacent under this rule, so the run trick above does not hold. The code does one union per allowed
  active neighbor.

Crux cell: BFS from the top-zone cells (row-major seed order) through the traversable cells (o <= `RatingGrade`). It expands neighbors
in row-major order (`nbr[0..7]`: NW N NE W E SW S SE). Under `DiagonalRule`, it expands a diagonal only where the union-find above allows it.
The BFS stops when the first bottom-zone cell is DISCOVERED (not dequeued), then walks back. Below bucket 2047 the traversable cells are
order[0 .. bound[connecting + 1]). In the last bucket, they are order[0 .. bound[2047]) plus the popped segment order[heapEnd .. n). Each
traversable cell's `Unvisited` state carries its Order entry's zone bits, so seeds and the stop test read exactly the zones the
union-find used (no key recomputation here).

## Near FinishBand

`FinishBand` sets `Band` / `CruxBand` / `BandGrade` / `Note` and the evidence fields from the finished crux pass. It is pure: it reads only the result
(`CellGrade`, the recorded easiest line `Path`, `FallLine`, `MeanGrade`, `RatingGrade`) and the grid's Mask. It does no terrain sampling and no
allocation. Each rule is a pure function of those numbers (for constants and reasons, see "Near SteepShareForBlack").

1. Steep-share cap. The rule applies when all of these hold:
   * The crux is in the black (double black) band, within `CapMaxOverFloor` of its floor.
   * `MeanGrade` is below the floor.
   * The share of inside cells with `CellGrade` >= the floor is below `SteepShareForBlack` (`DoubleBlackShare`).

   Then the run rates one band lower (black -> blue, double black -> black, never two). The rule does not apply when the crux pitch is
   `CruxLengthForBlack` or longer (`LongKept`). It also does not apply when the crux stretch is narrower than `NarrowWidthM`, or when
   the width-weighted share (cells in a section narrower than `NarrowWidthM` counted twice) reaches the limit (`NarrowKept`: the width decided).
2. Wide ease. A blue crux less than `WideEase` above the green/blue boundary, with `MeanGrade` below the boundary and a crux stretch wider than
   `WideRunM`, rates Green.
3. Green mean. `MeanGrade` below `GreenMeanKeepsBlueMax` (the green band): the run rates at most blue however short and steep the crux (double
   black included), unless the pitch is `CruxLengthForBlack` or longer (`LongKept`). The code checks this before the share rule. It has no share,
   margin or width condition.

Widths are perpendicular extents of the Mask across the mean fall line (cells x step), marched over the lattice already built. They
need a usable fall line (`ZoneByFallLine`). Without one (cones, craters, flat ground), width is unknown (`CruxWidthM` -1) and no width
rule fires. Polyline mode, a broken Mask (the rating is the P90 fallback, no easiest line) and `ShortRun` results with no valid crux
keep `Band` = `BandOf(RatingGrade)`.
