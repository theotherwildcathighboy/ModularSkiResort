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
  band, kind, merge, cluster), Junctions (node index pairs), Clusters (`ClusterCap` summits + up to `MaxPods` pods), reject histogram,
  the probe (StageMs, StageMaxMs = longest unit per stage, Units, MaxUnitMs; per scan: a Reselect clears them), a fingerprint.
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
| Heights | Plan; mask row chunks; row chunks | region raster (classifier Plan, cell center inside) in its own unit, its Mask copied in row chunks, then heights, off-map margin (`OffMapMarginTexels`), Wet flags |
| Obstacles | one obstacle | hard ones seed `DHard` at -texel/2 - clearance - 1 m (mask cells plus edge samples, so thin outlines count); cat tracks set a soft flag |
| Field | one tile | o = `SlopeResult.CellGrade` per cell from Plan + FillHeights + Classify on an 80-cell tile with a 20 m apron: bit-identical to one call (host asserts) |
| DistForward / Backward | row chunk | chamfer distance from every hard cell, non-region and off-map cell |
| FlowSort / FlowAccum | row chunk / 16 Ki cells | D8 catchment area (counting sort on R16: count rows, then place rows), the gully cost |
| Usable, Zones | row chunks | open = region cell with `DHard >= texel + RegionClearanceM` (an outline marched from an open cell stays inside), cliff >= 100 %, base = lowest 15 % of the vertical (min 10 m) |
| Minimax | 32 Ki pops, then row chunks | M = easiest band down to the base (bucket queue on the classifier's 1/1024 grade buckets, 8-neighbor); `EasiestBand` serves the preview heat map |
| Route | row chunks + 8 Ki pops | four reverse Dijkstra passes from the base (G / B / K / DB ceilings 25 / 40 / 60 / 80 %): in-band x0.4, sidehill +2 x cross-slope, flat +3, gully +0.5 ln(A / 2 ha), cat track +1, no climb over 0.5 m. `StepCost` is shared with the reroute |
| Seeds | row chunks; one summit / pod / cluster | top cells, summits, pods, seeds (below) |
| Candidates | 8 seeds | walk each seed's predecessor chain to the base, dedupe by path hash, cheap score = SizeFactor x BandValue x vertical term x (1 + `ScoreJitter` x seeded jitter) |
| Select | one phase of one candidate | greedy by score x MixWeight. Phases (`m_SelPhase`): 0 pick + merge truncation (+ the build when no reroute), 1 reroute A* (8 Ki pops a unit, resumable), 2 build (corridor march, GradeRibbon tail, exact checks), 3 `SkiRunRater.Rate` + accept, 4 / 5 the accepted run's local chamfer of `DAcc` in row chunks |
| Finish | setup; pairs under 40 k edge pairs; runs + junctions; kinds etc. | junctions (ring bounding boxes prefilter), existing-run links, kinds, clusters, warnings, fingerprint |

Every whole-lattice pass is row-chunked (65536 cells a unit) since P2, for the 9 km2 cap. The one unit that cannot shrink is a
single `SkiRunRater.Rate` (about 1-2 ms CoreCLR, field: about 10 ms Mono for a 1 km2 run). Slicing, not a worker thread: the
Step contract, determinism tests and both callers (the tool's scan and the Trees snapshot) stay as they are.

The predecessor byte stores the direction the route ARRIVED from (UnitRoute relaxes v = u + Dir[d] and stores d at v), so the walk
toward the base steps c - Dir[d]. `Grow` does not copy: it sizes scratch buffers only; size a buffer once before a fill loop.

## Clusters and pods

Top cells = usable cells in the upper `TopShare` (25 %) of the vertical, at least `TopBandM` (30 m), that reach the base below the
double black ceiling. Summits: the highest top cell at least `ClusterSpacingM` (300 m) from every cluster, one per unit; after the
first (the true summit) the height carries a seeded jitter of `SummitJitterM` (20 m), so Shuffle moves the other lift tops. Up to
`ClusterCap` = round(3 x AreaScale) summits, AreaScale = sqrt(km2) clamped to [1, 3] (3 at 1 km2, 9 at 9 km2). Pods come on top
(at most `MaxPods` 2, easiest pass first; P2: they no longer take a summit's slot): a pass (green, blue, black) that NO top cell
reaches gets a pod at the highest cell it reaches `MinDropM` above the base, outside every cluster's radius (the beginner pod near
the base, a blue mid-station under a steep summit). `AutoCluster.Pod` / `PodBand` tell the tool which clusters are pods.

Seeds per cluster and pass: member cells within 150 m and `TopBandM` of the cluster top, ordered by height plus a seeded jitter
(`SeedJitterM` 10 m; 20 m spread the tops so far that the cone's apex runs lost their junctions), spaced one corridor width + 15 m
apart, `SeedsPerClusterPass` 12.

Run caps: `RunCapOf` = the knob, else 8 / 12 / 16 (Sparse / Normal / Dense) x AreaScale, at most `HardRunCap` 48; ratings at most
max(48, 3 x the run cap).

## Reroute (P2)

Natural fall lines converge, so a later candidate used to hit an accepted run's gap early and merge (TooShort) or leave pockets.
When the merge keeps under `RerouteShare` (70 %) of the free line, an A* on the pass's own graph looks for a line around the
accepted runs: heuristic = the pass's route cost to the base (`m_Dist[k]`, exact and consistent: same `StepCost`); a cell within
stopD of an accepted run ends the line (a merge there) at an extra `MergePenalty` x the route it leaves unskied; the free line's
own merge cost bounds the search (at most `RerouteMaxPops` 48 k). A line that skis over `DetourFactor` (1.5) x the free route
between the same two heights is a traverse and is dropped. The rerouted line wins only when it is longer than the free stub.

## Geometry rules

- Corridor: half widths marched per centerline sample (1 m steps) until a cell is not open, is cliff, is at the pass ceiling or
  within the gap of an accepted run; min-of-neighbors smoothing; Pinched when over 30 % of samples are under 15 m.
- Outline: GradeRibbon's tail (CutLoops, Simplify 1.0 m, Thin, Clear 1.5 m) on two variable-width edges. The ring is never
  re-simplified after junction nodes are inserted (they would be lost).
- Exact checks (the planner is authoritative; the engine is no safety net with Anarchy on): inside the region and `EdgeDistance`
  to it >= 0.25 m, on the map, `ObstaclesClear`, pairwise >= 0.8 x gap (>= 2 m to the run it merges into; a merge counts only when
  the ring comes within `JunctionReachM` of that run, else the full gap holds). Bounding boxes farther apart than the need skip
  the exact distance. `RingDistance` counts
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

New seed: restart at Seeds (summit jitter, seed jitter and score jitter all follow the seed). Mix / Density / RunCap: restart at Select. Kinds only: Finish. Density with an ExistingRun obstacle:
a full restart (that obstacle's clearance is the density gap, rastered in stage 0), on a copy of the input. The host asserts each
Reselect equals a fresh run with the same knobs.

## Timing (AutoGenBatch, CoreCLR, fBm mountainside, Dense, Step(4 ms))

| Region | Total | Units | Longest unit | Runs |
|---|---|---|---|---|
| 1 km2 | ~70 ms | ~275 | ~2.2 ms | 16 |
| 4 km2 | ~250-300 ms | ~800 | ~1.8-2.5 ms | 22-30 |
| 9 km2 | ~700 ms | ~1500 | ~2.6 ms | 41 |

Measured with a game running on the machine (inflated, best of three for the gate). Field 2026-10-26 Mono / CoreCLR ratios per
stage: Field x10, Route x3.4, Minimax x3.8, Select x7.7 (whole scan x6.7: 468 ms vs ~70 ms at 1 km2). So the longest Mono units
at 9 km2 are about 10-18 ms (a Field tile, one Rate; estimated, not field-measured) and a 9 km2 scan about 4-5 s of work over
~330 frames at the field's ~14 ms a frame. AutoGenBatch gates the 9 km2 longest unit at 4 ms CoreCLR. The Field tile (about
1.5-2 ms, x10 on Mono) does not grow with the area; a smaller `TileCoreCells` would cut it at more apron work in all.
