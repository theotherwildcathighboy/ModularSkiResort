# SkiRunAutoPlanner: the Quick runs planner (field, routing, selection, junctions, refusal ladder)

The pure half of Quick runs (design: docs/research/autogen-2026-10-25/design.md, skeptic.md, FEASIBILITY.md). Entities-free:
System, System.Collections.Generic and Unity.Mathematics only, so tests/AutoGenBatch links the four files and proves every rule
headless. The engine half (wave 2: the tool) owns the snapshot copy, obstacle collection, overlay, emission and undo.

Files: `SkiRunAutoPlanner.cs` (API, stages 0-4), `.Select.cs` (stages 5-9), `.Kernels.cs` (distance, minimax and Dijkstra
kernels, ring geometry), `.Snapshot.cs` (`HeightSnapshot`, `SnapshotSampler`).

## Contract

- Input: `AutoPlanInput` = a `HeightSnapshot` (R16 texels of the rectangle `SnapshotRect` names, plus an optional Wet layer), the
  region polygon (xz), obstacles (`AutoObstacle`: Surface, ExistingRun, Unowned, CatTrack) and `AutoPlanKnobs` (Mix, Density, Kinds,
  RunCap, Seed). `MaxAreaM2` is 9 km2 (`V1MaxAreaM2`, 3 x 3 km, DECISIONS 419; a host may pass less); the lattice cap
  `MaxLatticeCells` (2^21, about 5 x 5 km) bounds the scan rectangle.
- Output: `AutoPlanResult`: Status (the refusal ladder), Warnings, Runs (ring, nodes with snapshot heights, centerline, band, pass
  band, kind, merge, cluster), Junctions (node index pairs), Clusters (`ClusterCap` summits + up to `PodCap` pods), reject histogram,
  the probe (StageMs, StageMaxMs = longest unit per stage, SelectPhaseMaxMs, Units, MaxUnitMs; per scan: a Reselect clears them), a
  fingerprint.
- Deterministic: the output is a pure function of (snapshot, region, obstacles, knobs) within a runtime. `SeedOf` ignores the
  region's start vertex and drawing direction; ties break by cell index everywhere (heaps, sorts, picks).
- Never logs, never throws on bad input (BadInput), no static mutable state, buffers reused across scans.
- Obstacles (owner rulings): every Surface area and unowned tile blocks; cat tracks cost (`CatTrackCost`) but never block; water is a
  warning (`OverWater`) and never a refusal; roads, lots and lift stations are not obstacles (there is no class for them).
- The band on each run is `SkiRunRater.Rate` on its own final ring (StepMultiple 1, no source): the call the catalog makes after
  build, run as its own steps over several units (below; AutoGenBatch re-rates every run with `SkiRunRater.Rate` and compares). The
  field is guidance only. The rater's crux is a MINIMAX (the easiest line's steepest cell between the end zones), not a percentile:
  that is why the routes' in-band test below is strict.

## Stages (one bounded unit per Step iteration)

| Stage | Unit | What |
|---|---|---|
| Heights | Plan; mask row chunks; row chunks | region raster (classifier Plan, cell center inside) in its own unit, its Mask copied in row chunks, then heights, off-map margin (`OffMapMarginTexels`), Wet flags |
| Obstacles | one obstacle | hard ones seed `DHard` at -texel/2 - clearance - 1 m (mask cells plus edge samples, so thin outlines count); cat tracks set a soft flag |
| Field | one tile | o = `SlopeResult.CellGrade` per cell from Plan + FillHeights + Classify on an 80-cell tile with a 20 m apron: bit-identical to one call (host asserts) |
| DistForward / Backward | half row chunk | chamfer distances: `DHard` from every hard cell, non-region and off-map cell; `DEdge` from the region and map edge only |
| FlowSort / FlowAccum | row chunk / 16 Ki cells | D8 catchment area (counting sort on R16: count rows, then place rows), the gully cost |
| Usable, Zones | row chunks | open = region cell with `DHard >= texel + RegionClearanceM` (an outline marched from an open cell stays inside), cliff >= 100 %, base = usable ground up to `HMinU + clamp(15 % x vertical, BaseMinM 10 m, BaseMaxM 60 m)` (B: the cap keeps gentle lower ground above 60 m of relief skiable) |
| Minimax | 32 Ki pops, then row chunks | M = easiest band down to the base (bucket queue on the classifier's 1/1024 grade buckets, 8-neighbor); `EasiestBand` serves the preview heat map. Then (A) AllCliff only when no usable cell `MinDropM` above the base has M under 80 %, at any height; `m_HReach` = the highest such cell |
| Route | row chunks + 8 Ki pops | four reverse Dijkstra passes (C, see Routing passes). `StepCost` is shared with the reroute. Then `ReachRows` (review F11 / F2): row chunks over the region count the usable cells some pass routes in band `MinDropM` above that pass's sources (`SourceTop`), and those `EdgeSeedMarginM` inside; none at all: AllCliff (TooNarrow when no pass had a source), none inside the margin: TooNarrow. The Minimax reach test has no edge strip and no source limit, so it can pass where no route can seed |
| Seeds | row chunks; one summit / pod; one (cluster, pass) list | top cells, summits, pods, seed cells, seeds (below) |
| Candidates | 8 seeds | walk each seed's predecessor chain to the base, dedupe by path hash, Expected = BandOf(the line's steepest cell), cheap score = SizeFactor x BandValue x vertical term x (1 + `ScoreJitter` x seeded jitter) |
| Select | one phase of one candidate | greedy by `SelectWeight` (Terrain: Score0): the mix picks the band, the score the line. The band under target with the lowest fill share (have / target, in tiers of 1/1000 worth 1000 each) goes first, then the line score Score0 / BandValue(Expected) (SizeFactor x at most 2, far under one tier); bands at target come last, by line score (with BandValue in the order, the long summit blacks claimed the lower mountain before any green was tried; with the old 1 + deficit weight, the summits' own seeds after review F6 filled the cap with blacks: alpine 2 bowl Normal 5 greens of 14). Phases (`m_SelPhase`): 0 pick + merge truncation + the line checks, 1 reroute A* (4 Ki pops a unit, 12 k in all, resumable), 2 build, ONE attempt per unit (corridor march, GradeRibbon tail, exact checks; up to `BuildAttempts` 4: area shrink or 0.75 x corridor), 3 rating in three steps (node heights + the classifier's Plan; Need heights in row chunks of 64 Ki cells with FillHeights' own sampling; Classify + accept), 4 / 5 the accepted run's local chamfer of `DAcc` in row chunks |
| Finish | setup; pairs under 40 k edge pairs; runs + junctions; kinds etc. | junctions (ring bounding boxes prefilter), existing-run links, kinds, clusters, warnings, fingerprint |

Every whole-lattice pass is row-chunked (65536 cells a unit) since P2, for the 9 km2 cap. Since the quality pass (H) no Select unit
exceeds ~1 ms CoreCLR: the rating is `SkiRunRater.Rate`'s own steps on the public entries (StepMultiple of `ShoelaceArea`, the
DiagonalRule for it, `SkiSlopeClassifier.Plan`, FillHeights' sampling without a source, `Classify`, `ToRateStatus`), so no 12 ha
area cap was needed (`MaxRunAreaM2` stays 20 ha; Classify measured up to 0.44 ms on a 20 ha ring). Slicing, not a worker thread: the
Step contract, determinism tests and both callers (the tool's scan and the Trees snapshot) stay as they are.

## Routing passes (C, G)

Four reverse Dijkstra passes from the base, one per band: G / B / K / DB with band [floor, ceiling) = [6, 25) / [25, 40) / [40, 60) /
[60, 80) % and a HARD ceiling = the next pass's ceiling (40 / 60 / 80 / 100 % = `CliffGrade`). `StepCost`: -1 (impossible) at or over
the hard ceiling, on cliff, on a cell that is not open, within `EdgeSeedMarginM / 2` (17.5 m) of the region edge (`DEdge`), or for a
climb over 0.5 m; else length x (1 + 2 x cross-slope + 3 flat + 0.5 ln(A / 2 ha) gully + 1 cat track) x (in band 0.4, or over the pass
ceiling `OverBandFactor` 3) x (`EdgeCostFactor` 1.5 within `EdgeSeedMarginM` 35 m of any hard cell). Sources: base cells under the
hard ceiling, `EdgeSeedMarginM / 2` inside the edge, up to `PassSourceShare` of the base band (green and blue 0.5: beginner and cruiser
runs ski down into the base area, and the gentle ground just above a 60 m base is too short for a green; black and double black 1.0).

IN BAND (bit k of `m_InBand`): the cell's route to the base never crosses pass k's ceiling (a flag carried along the Dijkstra). Only
in-band cells seed runs and pods. Measured on the alpine fixtures (12 scans): a P90 allowance (10 % of the line over the ceiling, the
first version of C) labeled 58-85 of ~280 blue-pass runs black after rating; the strict rule 9-12. The hard ceiling still pays: the
corridor marches to it (median width 19 -> 30 m on the bowl) and routes may pass a steep pitch (their upslope cells are then not in
band). Green sources at 0.5 of the base band took the bowl from G0-G3 to G6-G15 per scan; blue at 0.5 as well balanced the mix
(B5-7 -> B13-15).

The predecessor byte stores the direction the route ARRIVED from (UnitRoute relaxes v = u + Dir[d] and stores d at v), so the walk
toward the base steps c - Dir[d]. `Grow` does not copy: it sizes scratch buffers only; size a buffer once before a fill loop.

## Clusters and pods

Top cells = usable cells in the upper `TopShare` (25 %) of the SKIABLE vertical (from `m_HReach`, the highest ground reaching the base
under 80 %, down to the lowest usable ground), at least `TopBandM` (30 m), that reach the base below the double black ceiling. A steep
summit cap above `m_HReach` no longer refuses the mountain (A) or hides the summits. Summits: the highest top cell at least
`ClusterSpacingM` (300 m) from every cluster, one per unit; after the first (the true summit) the height carries a seeded jitter of
`SummitJitterM` (20 m), so Shuffle moves the other lift tops. Up to `ClusterCap` = round(3 x AreaScale) summits, AreaScale =
sqrt(km2) clamped to [1, 3] (3 at 1 km2, 9 at 9 km2).

Pods (E) come on top: green and blue ALWAYS get up to `MaxPodsPerPass` = round(2 x AreaScale) pods each (2 at 1 km2, 6 at 9 km2),
black only when no top cell is in the black band (mix-independent on purpose: a Mix re-select restarts at Select, not Seeds). Each pod
is the highest in-band cell of its pass `MinDropM` above that pass's source top, `EdgeSeedMarginM` inside the region and
`ClusterRadiusM` (150 m, half the summit spacing) from every cluster center, summits and pods; one row-chunked scan per pod, and a pass
that finds none skips its remaining pods. So a mountain whose summit reaches the base by one green thread still gets its beginner pods
on the gentle lower slope. Clusters <= `ClusterCap + PodCap` (PodCap = 3 x MaxPodsPerPass). `AutoCluster.Pod` / `PodBand` tell the
tool which clusters are pods.

Seeds (D) across each cluster's top band, not a disc: one row-chunked pass over the region's 2-cell seed lattice puts every usable
cell with `DHard >= EdgeSeedMarginM` (35 m) into the list of the nearest cluster k whose top band
[TopY_k - max(`TopBandM`, 0.5 x `TopShare` x skiable vertical), TopY_k] holds its height (`BandOwner`; review F6: with plain Voronoi a pod
below a summit took the summit's top-band cells near it, and summits ended with no run) into k's list for every pass it is in band for; then one
(cluster, pass) list per unit is sorted by height plus a seeded jitter (`SeedJitterM` 10 m; 20 m spread the tops so far that the
cone's apex runs lost their junctions) and spaced one corridor width + `NormalGapM` apart (density-free: a Density re-select restarts
at Select), at most `SeedsPerClusterPass` 24. `ClusterRadiusM` now serves only the pod exclusion.

Run caps: `RunCapOf` = the knob, else 8 / 12 / 16 (Sparse / Normal / Dense) per km2 x the area in km2 (AreaScale squared, at least
1 km2; it grew with the side before, which held a 4 km2 mountain to 6 runs per km2 on Normal), at most `HardRunCap` 48; ratings at
most max(48, 3 x the run cap).

## Reroute (P2)

Natural fall lines converge, so a later candidate used to hit an accepted run's gap early and merge (TooShort) or leave pockets.
When the merge keeps under `RerouteShare` (70 %) of the free line, an A* on the pass's own graph looks for a line around the
accepted runs: heuristic = the pass's route cost to the base (`m_Dist[k]`, exact and consistent: same `StepCost`); a cell within
stopD of an accepted run ends the line (a merge there) at an extra `MergePenalty` x the route it leaves unskied; the free line's
own merge cost bounds the search (at most `RerouteMaxPops` 12 k; 48 k made the A* 60-90 % of Select once the seeds spread, 443 ms
CoreCLR on the 9 km2 fBm fixture, and 12 k gives the same run counts on the alpine fixtures). Cost scratch: `m_AG` (`DHard` stays
intact for a Reselect). A line that skis over `DetourFactor` (1.5) x the free route
between the same two heights is a traverse and is dropped. The rerouted line wins only when it is longer than the free stub.

## Geometry rules

- Lines (F): at least `MinLengthM` 250 m (the cell path, the smoothed centerline and a line cut back for the area cap); a merged line
  keeps `MergeKeepShare` 40 % of its free line or `MergeKeepM` 400 m, else MergeStub (its own reject since review F12, so the field test sees it fire; the reroute runs first).
- Centerline: cells decimated every `DecimateM` 25 m through GradePath's Catmull-Rom chain; the raw cells when a smoothed sample is not
  open, is at the hard ceiling, inside the gap of an accepted run or within `EdgeSeedMarginM / 2` of the edge (a corner cut there left
  a 2 m neck that split the rating mask: 23 of 28 ratings Unrated on an open slope before this check).
- Corridor: half widths marched per centerline sample (1 m steps) until a cell is not open, is cliff, is at the pass's HARD ceiling,
  is within the gap of an accepted run or within `EdgeSeedMarginM / 2` of the region edge (`DEdge`: rings keep ~17.5 m off the
  player's polygon); min-of-neighbors smoothing; Pinched when over 30 % of samples are under 15 m.
- Outline: GradeRibbon's tail (CutLoops, Simplify 1.0 m, Thin, Clear 1.5 m) on two variable-width edges. The ring is never
  re-simplified after junction nodes are inserted (they would be lost).
- Exact checks (the planner is authoritative; the engine is no safety net with Anarchy on): inside the region and `EdgeDistance`
  to it >= 0.25 m, on the map, `ObstaclesClear`, pairwise >= 0.8 x gap (>= 2 m to the run it merges into; a merge counts only when
  the ring comes within `JunctionReachM` of that run, else the full gap holds). Bounding boxes farther apart than the need skip
  the exact distance. `RingDistance` counts
  containment (an obstacle inside a ring is distance 0); `EdgeDistance` does not (a ring inside the region is as far from it as
  from its nearest edge).
- Area cap 20 ha and StepMultiple 1: a longer corridor is cut back until both hold (one Select unit per attempt).

## Junctions (skeptic finding 1)

For every run pair within `JunctionReachM` (20 m): the topmost close point pair, plus the merge point; both are inserted as ring
vertices (`InsertNode` reuses a vertex closer than 2 m), so the pair is <= 20 + 2 x 2 m <= `LinkMeters` 25 m
(`SkiRunAutoPlanner.LinkMeters` is an alias of LiftServedRule.LinkMeters since package D, and AutoGenBatch links LiftServedRule.cs). Existing runs: our node at
the closest point to their highest vertex within reach. The host asserts that every pair of runs closer than 20 m has a node pair
within 25 m.

## Refusal ladder (order)

BadInput, TooLarge (area or lattice), TooSmall (area), Blocked (open < 30 %), TooSmall (no open cell 7.5 m from every edge), TooNarrow (no usable cell
`EdgeSeedMarginM` inside the region and clear of hard cells: no seed or pod can exist; review F2), NoVertical (< 50 m over open ground),
AllCliff (cliff > 60 % or no usable cell `MinDropM` above the base reaching it below 80 %, at any height), TooFlat (< 20 % of usable ground >= 6 %),
after the routes AllCliff / TooNarrow again from `ReachRows` (no routed cell above its pass's sources / none inside the margin),
NoRuns (Rejects says why; the test host fails a NoRuns with an empty histogram). NoSign is the tool's.

## Reselect

New seed: restart at Seeds (summit jitter, seed jitter and score jitter all follow the seed). Mix / Density / RunCap: restart at Select. Kinds only: Finish. Density with an ExistingRun obstacle:
a full restart (that obstacle's clearance is the density gap, rastered in stage 0), on a copy of the input. The host asserts each
Reselect equals a fresh run with the same knobs.

## Field mirror (AutoGenBatch `-- field` and FieldTests, Terrain.Alpine, Balanced)

Quality pass step 2 (2026-10-26), the ~3.9 km2 / ~900 m bowl and the lower 1.1 km2 region, seeds 1-3, CoreCLR:

| Fixture | Before (step 1) | After |
|---|---|---|
| bowl seeds 1-2 | AllCliff | Ok, 44-48 runs |
| bowl seed 3 Normal | 18 runs, 4.5 / km2 open, G0 B1 K6 DB11, width 19/19/28 m | 48 runs, 12.1 / km2, G9 B15 K14 DB10, width 23/30/33 m |
| lower 1.1 seed 1 Normal | 12 runs, 11.5 / km2, G2 B4 K4 DB2 | 13 runs, 12.4 / km2, G4 B5 K4 DB0 |
| Select phase max, ms (pick/A*/build/rate/chamfer) | up to 0.37/1.42/2.93/2.65/0.13 | 0.06/0.65/0.33/0.44/0.11 (best of 2, worst scan) |

FieldTests (normal run) gate: Ok; bowl >= 8 (Normal) / 9 (Dense) runs per km2 open; bowl greens >= 2 and >= 40 % of the target, one
green topping out in the lower half of the vertical; median width >= 25 m; every centerline >= `MinLengthM`; every ring
>= `EdgeSeedMarginM / 2` (less 3.5 m of outline tolerance) from the region edge; lower Dense >= 10 runs per km2, >= 1 green;
Invariants; Select phases <= 1.2 ms (rate <= 2 ms). The bowl run counts sit at `HardRunCap` 48.

## Timing (AutoGenBatch, CoreCLR, fBm mountainside, Dense, Step(4 ms))

| Region | Total | Units | Longest unit | Runs |
|---|---|---|---|---|
| 1 km2 | ~70 ms | ~275 | ~2.2 ms | 16 |
| 4 km2 | ~250-300 ms | ~800 | ~1.8-2.5 ms | 22-30 |
| 9 km2 | ~700 ms | ~1500 | ~2.6 ms | 41 |

After the quality pass (more seeds, pods and runs; `-- big`, 12 k reroute): 4 km2 ~410 ms, 9 km2 ~690 ms, 42-44 runs, Select the
largest stage (A* about 40 % of it); the 9 km2 gate (best of three) 1.60 ms longest unit. Measured with a game running on the machine (inflated, best of three for the gate). Field 2026-10-26 Mono / CoreCLR ratios per
stage: Field x10, Route x3.4, Minimax x3.8, Select x7.7 (whole scan x6.7: 468 ms vs ~70 ms at 1 km2). So the longest Mono units
at 9 km2 are about 10-18 ms (a Field tile, one Rate; estimated, not field-measured) and a 9 km2 scan about 4-5 s of work over
~330 frames at the field's ~14 ms a frame. AutoGenBatch gates the 9 km2 longest unit at 4 ms CoreCLR. The Field tile (about
1.5-2 ms, x10 on Mono) does not grow with the area; a smaller `TileCoreCells` would cut it at more apron work in all.
