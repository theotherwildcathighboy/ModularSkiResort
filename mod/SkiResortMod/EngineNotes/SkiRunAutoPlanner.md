# SkiRunAutoPlanner: the Quick runs planner (field, routing, selection, junctions, refusal ladder)

The pure half of Quick runs (design: docs/research/autogen-2026-10-25/design.md, skeptic.md, FEASIBILITY.md). Entities-free:
System, System.Collections.Generic and Unity.Mathematics only, so tests/AutoGenBatch links the four files and proves every rule
headless. The engine half (wave 2: the tool) owns the snapshot copy, obstacle collection, overlay, emission and undo.

Files: `SkiRunAutoPlanner.cs` (API, stages 0-4), `.Select.cs` (stages 5-9), `.Kernels.cs` (distance, minimax and Dijkstra
kernels, ring geometry), `.Snapshot.cs` (`HeightSnapshot`, `SnapshotSampler`).

## Contract

- Input: `AutoPlanInput` = a `HeightSnapshot` (R16 texels of the rectangle `SnapshotRect` names, plus an optional Wet layer), the
  region polygon (xz), obstacles (`AutoObstacle`: Surface, ExistingRun, Unowned, CatTrack) and `AutoPlanKnobs` (Mix, Density, Kinds,
  RunCap, Seed). `MaxAreaM2` is 1 km2 (`V1MaxAreaM2`); the code runs 4 km2 (`ReadyMaxAreaM2`, timed in the host).
- Output: `AutoPlanResult`: Status (the refusal ladder), Warnings, Runs (ring, nodes with snapshot heights, centerline, band, pass
  band, kind, merge, cluster), Junctions (node index pairs), up to 3 Clusters, reject histogram, stage times, a fingerprint.
- Deterministic: the output is a pure function of (snapshot, region, obstacles, knobs) within a runtime. `SeedOf` ignores the
  region's start vertex and drawing direction; ties break by cell index everywhere (heaps, sorts, picks).
- Never logs, never throws on bad input (BadInput), no static mutable state, buffers reused across scans.
- Obstacles (owner rulings): every Surface area and unowned tile blocks; cat tracks cost (`CatTrackCost`) but never block; water is a
  warning (`OverWater`) and never a refusal; roads, lots and lift stations are not obstacles (there is no class for them).
- The band on each run is `SkiRunRater.Rate` on its own final ring (StepMultiple 1, no source): the call the catalog makes after
  build. The field is guidance only.

## Stages (one bounded unit per Step iteration)

| Stage | Unit | What |
|---|---|---|
| Heights | row chunk | heights from the snapshot, off-map margin (`OffMapMarginTexels`), Wet flags, region raster (classifier Plan, cell center inside) |
| Obstacles | one obstacle | hard ones seed `DHard` at -texel/2 - clearance - 1 m (mask cells plus edge samples, so thin outlines count); cat tracks set a soft flag |
| Field | one tile | o = `SlopeResult.CellGrade` per cell from Plan + FillHeights + Classify on an 80-cell tile with a 20 m apron: bit-identical to one call (host asserts) |
| DistForward / Backward | row chunk | chamfer distance from every hard cell, non-region and off-map cell |
| FlowSort / FlowAccum | row chunk | D8 catchment area (counting sort on R16), the gully cost |
| Usable, Zones | one pass | open = region cell with `DHard >= texel + RegionClearanceM` (an outline marched from an open cell stays inside), cliff >= 100 %, base = lowest 15 % of the vertical (min 10 m) |
| Minimax | 64 Ki pops | M = easiest band down to the base (bucket queue on the classifier's 1/1024 grade buckets, 8-neighbor); `EasiestBand` serves the preview heat map |
| Route | 32 Ki pops | four reverse Dijkstra passes from the base (G / B / K / DB ceilings 25 / 40 / 60 / 80 %): in-band x0.4, sidehill +2 x cross-slope, flat +3, gully +0.5 ln(A / 2 ha), cat track +1, no climb over 0.5 m |
| Seeds | one | clusters + seeds (below) |
| Candidates | 8 seeds | walk each seed's predecessor chain to the base, dedupe by path hash, cheap score = SizeFactor x BandValue x vertical term |
| Select | one candidate | greedy by score x MixWeight: merge truncation at accepted runs, corridor march, GradeRibbon tail, exact checks, `SkiRunRater.Rate`, accept, local chamfer of `DAcc` |
| Finish | one | junctions, existing-run links, kinds, clusters, warnings, fingerprint |

The predecessor byte stores the direction the route ARRIVED from (UnitRoute relaxes v = u + Dir[d] and stores d at v), so the walk
toward the base steps c - Dir[d]. `Grow` does not copy: it sizes scratch buffers only; size a buffer once before a fill loop.

## Clusters and pods

Top cells = usable cells within `TopBandM` (30 m) of the highest usable ground that reach the base below the double black ceiling.
A pass (green, blue, black) that NO top cell reaches gets a reserved slot, a pod: the highest cell that pass reaches with
`MinDropM` above the base, outside every other cluster's radius (the beginner pod near the base, a blue mid-station under a steep
summit). Summits fill the remaining slots by farthest-point picks at least 300 m apart; a reserved pod that finds no room gives its
slot back. `AutoCluster.Pod` / `PodBand` tell the tool which clusters are pods (a lift there is a short lift, not a summit lift).

Seeds per cluster and pass: member cells within 150 m and `TopBandM` of the cluster top, ordered by height plus a seeded jitter
(`SeedJitterM`), spaced one corridor width + 15 m apart, 6 per cluster and pass.

## Geometry rules

- Corridor: half widths marched per centerline sample (1 m steps) until a cell is not open, is cliff, is at the pass ceiling or
  within the gap of an accepted run; min-of-neighbors smoothing; Pinched when over 30 % of samples are under 15 m.
- Outline: GradeRibbon's tail (CutLoops, Simplify 1.0 m, Thin, Clear 1.5 m) on two variable-width edges. The ring is never
  re-simplified after junction nodes are inserted (they would be lost).
- Exact checks (the planner is authoritative; the engine is no safety net with Anarchy on): inside the region and `EdgeDistance`
  to it >= 0.25 m, on the map, `ObstaclesClear`, pairwise >= 0.8 x gap (>= 2 m to the run it merges into). `RingDistance` counts
  containment (an obstacle inside a ring is distance 0); `EdgeDistance` does not (a ring inside the region is as far from it as
  from its nearest edge).
- Area cap 20 ha and StepMultiple 1: a longer corridor is cut back until both hold.

## Junctions (skeptic finding 1)

For every run pair within `JunctionReachM` (20 m): the topmost close point pair, plus the merge point; both are inserted as ring
vertices (`InsertNode` reuses a vertex closer than 2 m), so the pair is <= 20 + 2 x 2 m <= `LinkMeters` 25 m
(`SkiRunAutoPlanner.LinkMeters` is an alias of LiftServedRule.LinkMeters since package D, and AutoGenBatch links LiftServedRule.cs). Existing runs: our node at
the closest point to their highest vertex within reach. The host asserts that every pair of runs closer than 20 m has a node pair
within 25 m.

## Refusal ladder (order)

BadInput, TooLarge (area or lattice), TooSmall (area), Blocked (open < 30 %), TooSmall (no open cell 7.5 m from every edge), NoVertical
(< 50 m over open ground), AllCliff (cliff > 60 % or no top reaching the base below 80 %), TooFlat (< 20 % of usable ground >= 6 %),
NoRuns (Rejects says why). NoSign is the tool's.

## Reselect

New seed: restart at Seeds. Mix / Density / RunCap: restart at Select. Kinds only: Finish. Density with an ExistingRun obstacle:
a full restart (that obstacle's clearance is the density gap, rastered in stage 0), on a copy of the input. The host asserts each
Reselect equals a fresh run with the same knobs.

## Timing (AutoGenBatch, CoreCLR, fBm mountainside)

1 km2: ~35-40 ms total, max unit ~3 ms. 4 km2: ~150-180 ms total, max unit ~3.5 ms. Field tiles and the route passes are ~75 % of
the time. Mono in the game is slower (unverified factor); `Step(budgetMs)` keeps each frame bounded either way.
