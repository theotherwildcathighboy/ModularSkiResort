# Resort Planner (v2): index and seams

The planner was rebuilt lift-first (code names stay `SkiRunAutoPlanner*` / `QuickRuns*`). Design, build spec and gates live in the private
docs (`docs/research/quickruns-v2/`). This note is the tracked index. It says where each piece lives, which hook calls which file, and what
each task learned (one section per task). Tasks write only their own section.

Where the shared pieces live (the planner files hold none of them, so a planner can go without a change to any reader):
- `Systems/PlanContract.cs`: the result and input types (`AutoPlanInput`, `AutoPlanKnobs`, `AutoPlanResult`, `AutoRun`, `AutoJunction`, `AutoCluster`, `AutoObstacle`, the `Auto*` enums).
- `Systems/PlanGeometry.cs`: the pure helpers (`PolygonArea`, `BBox`, ring and point distances, `SeedOf`, `SnapshotRect`, `ObstaclesClear`, `ClearanceOf`, `ScoreBandOf`, station lot half diagonals) and the shared constants (`V1MaxAreaM2`, `DefaultMin*`, `MaxLatticeCells`, `MeanGradeCap`, gaps and clearances, probe slot counts).
- `SkiRunAutoPlanner` keeps one-line forwarders under the old names (v2 and the tests compile unchanged). The tool layer calls `PlanGeometry` directly. Only the planner instance (`m_Planner`) names `SkiRunAutoPlanner`.
- The v2 result types (`ResortPlan`, `PlanLift`, ...) stay in `SkiRunAutoPlanner.Plan.cs`, `HeightSnapshot` in `SkiRunAutoPlanner.Snapshot.cs`.

## Runs-first in the tool (port round R2-a)

The tool no longer calls v2. The scan runs the runs-first planner (`RunsFirstPlanner.md`) and the realizer (`RunsFirstRealizer.md`).
The v2 files and their tests stay in the tree, unused by the tool. The planner still calls `SkiRunAutoPlanner.PolygonArea`.
The round that removes v2 must point that call at `PlanGeometry`.

The job:
- `Systems/QuickRunsScanJob.cs` is one scan as a pure job. The tool runs it on a worker (`Task.Run`). The test host runs the same class.
- Step 1 is the refusal ladder. The realizer runs with no plan. A refused region is never planned.
- Step 2 is the plan. The tool keeps the last two plans. The key is the snapshot, the region, the obstacles, the lifts, the access points, the options and the bare flag.
- Step 3 is the realizer. It makes the `AutoPlanResult` and the `ResortPlan` that all readers use. Their types did not change.
- One cancel token stops both. The planner returns within 250 ms. The job then has no result.
- The progress is the planner progress times 0.85. The realizer gives no progress, so the bar shows 0.925 while it works.
- The job owns its result. The tool publishes it as it is. No later job writes it.

The inputs (`QuickRunsScanJob.InputsFrom`):
- Other areas and unowned tiles are blocked with the clearance of the realizer (`PlanGeometry.ClearanceOf`).
- Existing runs are blocked with the gap of the density.
- Existing cat tracks stay open. A new run can cross them.
- Water is blocked (the wet layer of the snapshot).
- Each existing lift blocks its two stations and a corridor of 10 m on each side of its line. The planner has no fixed lift. The START line says so once.
- The access points go to the planner, but no pass reads them yet.

The knobs:
- Density picks one of three option presets (`RunsFirstOptions.ForDensity`). Normal is the scored default. A change starts a new scan.
- Kinds is read by the realizer only. A change uses the cached plan and runs the realizer again.
- Lifts, Mix, Shuffle and Way home have no effect. The tool logs one line and keeps the plan.
- The lift toggles work as before. `QuickRunsPlanRules.MaxLifts` is 15.

Build order: cat tracks, then pistes, then lifts. Cat tracks are Space areas, so no piste refuses them. They are not graded.
`BuildChunkRuns` (2) is the count of areas per frame. To measure it, build a full plan with Measurement diagnostics on.
Read the `[probe] build summary` line. It gives the worst chunk in ms and the frames. Raise the constant while the worst chunk stays under one long frame (about 50 ms).

The proof (Measurement diagnostics on):
- `[probe] runs-first` gives the plan hash, the result hash, the options and inputs prints, the planner ms per pass, the realizer ms per stage and the total.
- The dev trigger `skiResortDev.quickRuns` takes `scan <region> [bare]` and `dump [stem]` (`Systems/QuickRunsDevUISystem.cs`).
- `scan ft5-r1 bare` must give the plan hash of the golden line of ft5-r1.
- `dump` writes the exact input to `Logs/SkiResortMod.<stem>.rfscan`. The host mode `rf-replay <file>` runs it again and prints MATCH or DIFF.
- The host mode `rf-scan <out>` scans the field test regions at the three densities. It prints runs, lifts, km, score, time and memory.

## Modes

`AutoPlanInput.Mode` is `Runs` or `Resort`. `Runs` is the default: v1 exactly, test host only. `Resort` is v2, and the tool always sets it. Every v2 hook in a v1 file
is guarded by `m_Mode == AutoPlanMode.Resort`, so Runs output and fingerprints are bit-identical to main at the branch point. The
proof is `tests/AutoGenBatch/V1Golden.cs` (V1-1: 40 fingerprints recorded from main before the first hook. V1-2: no `ResortPlan`, no v2
layers in Runs mode). A re-record is allowed only when main's v1 planner changed on purpose (the merge commit says so).

## Stage order and probe slots

Resort mode inserts `Terrain, PodRoute, Pods, Lifts` between `Minimax` and `Route` in the `Stage` enum. `Minimax` hands over to `Terrain`
(Resort) or `EnterRoute()` (Runs). `Lifts` hands over to `EnterRoute()`. `StageSlot(stage)` keeps the v1 numbering for
`AutoPlanResult.StageMs` / `Progress`. The new stages have no v1 slot, so `StageCount` is still 16. The Resort probe slots are
`ResortPlan.StageUnits / StageMs / StageMaxMs[ResortStageCount = 6]`: Terrain, PodRoute, Pods, Lifts, Route (band passes), Flow
(`FinishResort`). The planner fills them as it runs and copies them into the plan after `FinishResort`.

## Hooks (all A0, one line each in the v1 files)

| Hook | Called from | Defined in | Stub behavior | Owner of the body |
|---|---|---|---|---|
| `UnitTerrain` | `Unit()` dispatch | `.Terrain.cs` | `Next(PodRoute)` | A3 |
| `UnitPodRoute`, `UnitPods` | dispatch | `.Pods.cs` | `Next(Pods)`, `Next(Lifts)` | A3 |
| `UnitLifts` | dispatch | `.Lifts.cs` | `EnterRoute()` | A4 |
| `IsResortSource(c)` | `StartPassRows` (per cell, `m_Pass` = the pass) | `.Fans.cs` | the v1 base-band rule | A5 (selected lifts' bottom pads) |
| `FanSeeds()` | top of `UnitSeeds` | `.Fans.cs` | `false` = run the v1 seeding | A5 (returns true when it handled the unit. Owns `m_Cursor2`, calls `Next(Candidates)`, repeats the v1 case 0 bookkeeping) |
| `FanCaps()` | end of `BeginSelect` | `.Fans.cs` | nothing | A5 |
| `FinishResort(res)` | end of `UnitFinish`, after `Warn` | `.Flow.cs` | an empty `ResortPlan` | A6 (also replaces `Clusters` by the pod clusters and adds the three warnings) |

Select.cs bodies behind `FanSeeds` / `FanCaps` (and the `March` widths) belong to A5. The tasks do not edit `UnitFinish` itself again.

## Knobs and Reselect

`AutoPlanKnobs.Lifts` (0 = Auto, 1-9) restarts at `Lifts` (Resort only, Runs ignores it). `AutoPlanKnobs.Home` changes only verdicts. It
falls into the existing "Finish" restart, and `FinishResort` recomputes the flow (there is no separate Flow stage: Flow is a hook
inside Finish). Seed = Seeds, Mix / Density / RunCap = Select, Kinds = Finish, as v1. `Reselect` copies Mode / Pinned / Access /
TreeLineY / Cancel into the restart input.

## Threads

- The `Step` loop polls `AutoPlanCancel` once per unit at the top. A canceled `Step` returns true with `Result.Canceled` set, and
  the scan stops. Reselect then refuses until the next scan completes. A fresh `Begin` / `Run` clears everything.
- `ProgressVolatile` is written after every unit into a volatile int (per-mille).
- `AutoPlanResult.Snapshot()` copies lists and arrays, shares the `AutoRun` objects and deep-copies the `ResortPlan` (`ResortPlan.Copy`).
  It lives in `SkiRunAutoPlanner.cs` (A0 wrote it there because A2 does not own that file).

## Fingerprint

`AutoPlanResult.Fingerprint` folds `ResortPlan.Fingerprint` in only when it is non-zero. So while the hooks are stubs, the Resort
fingerprint equals the Runs one (a `PlanTypes` check named "A0 stub"). The task that first writes a non-zero plan fingerprint removes that check.

## Layers

These layers exist in Resort mode only (`Allocate` grows them): `m_OS` float, `m_Band` / `m_PadOf` / `m_Ridge` byte, `m_PodOf` ushort (9 B per cell).
`SkiRunAutoPlanner.ResortLayerBytes` reports what is held (the P-4 memory gate sums it).

## Player text

A0 added every key (LocaleEN, the hubText.ts mirror, `locale/en-US.json`). Hub: `SkiResort.Hub.QUICK_LIFTS_AUTO / LIFTS_N / LIFTS_TIP /
HOME_AUTO / HOME_GREEN / HOME_BLUE / HOME_TIP / POD_TIP / LIFT_TIP`. Tooltip (also mirrored for the hub): `SkiResort.Tooltip.QUICK_VERDICT[Green|Blue|Black|DoubleBlack|Download]`,
`QUICK_CLASS[<TerrainBand name>]`, `QUICK_PLAN_LINE`, `QUICK_MIX_HONEST`, `QUICK_POD_LINE`, `QUICK_LIFT_LINE`, `QUICK_PROPOSAL_NOTE`,
`QUICK_AROUND_LIFT`, `QUICK_WARN[NoLiftSite|Download|DeadEnd]`. Labels: `SkiResort.Label.PLAN_LIFT`, `PLAN_POD`. `{TYPE}` is the existing
`SkiResort.Hub.LIFT_TYPE[<type id>]`.

## Sections per task

### A1 fixtures + host
Files, all under `tests/AutoGenBatch` (test host only, nothing in the mod changed):
- `Heightmap.cs` (16-bit gray PNG reader, window snapshot, `HeightmapTests`)
- `V2Fixtures.cs` (region matrix, field-test 3 regions, Steep / Benched shape checks, the Resort-mode refusal ladder = V1-4)
- `V2Metrics.cs` (every gate metric as a pure function that returns a `Gate`. `V2MetricsTests` on hand-made plans)
- `V2Tests.cs` (the `-- v2` matrix)
- `Terrain.cs` (`Steep(seed)`, `Benched(seed)` and the private `Terraced` remap only)

**Fixtures** live outside git in `%SKI_FIXTURES%` or `%LOCALAPPDATA%\ModularSkiResort\fixtures\<Name>\` (11: Mountain Village, Okemo,
JacksonHole, Whistler, Snowbird, Zermatt, Chamonix, Fjords, Highlands, Delta, Plains). A script copies them from the research archive:
`docs/research/quickruns-v2/scripts/fixtures-copy.cjs` (size + SHA-256, idempotent. Rebuild commands and attributions are in
`fixtures-build.md`, same folder). A missing fixture is one `SKIP`, never a FAIL. `Heightmap.Snapshot` returns the same window
`SnapshotRect` asks for. It flips the rows to the game's south-up lattice, leaves `Terrain.Scale` / `Offset` unchanged, and puts the sidecar offset in
`Offset.y`. The loader reads the two Mountain Village field corner heights 2143.82 / 1240.20 m (log 2144.0 / 1240.4).

**Running**: the default run adds `HeightmapTests`, `V2FixtureTests` (field regions equal the logged areas, Steep seed 1 and Benched
shape, V1-4 ladder in Resort mode) and `V2MetricsTests`. This costs about +0.5 s on a 22 s run, and it reads no real file. `-- v2` adds the matrix of 57 regions, about 70 s:
- synthetic Alpine x4, Benched x2, Steep x2
- per real fixture: 1 / 4 / 9 km2 at the base point, and 4 km2 on the steepest window
- Mountain Village: the four field regions, the 9 km2 window, base 1 / 4 / 9, steepest

Each region runs Runs mode and Resort mode. Resort results get the shared `Invariants` (V1-3) and every metric. The output has these parts:
- `FAIL` (HARD miss)
- `REPORT <ID> <region>: ... [ok|MISS tag]`
- `SKIP`
- a `ROW` table
- a per-ID tally (`GATE`)

**Arming a gate** (the owner task of A3-A6): add the gate's IDs to `V2Hard` in `V2Tests.cs`. Armed now: V1-2, V1-3, V1-4, D-1, D-2, D-3,
P-1..P-4. `V2AlwaysSoft` (F-8, H-5, H-6, S-6 bottoms) never fails. `V2SoftOnSteeper` (F-1..F-7, F-9, F-10, S-5) is REPORT on Steep, on
Mountain Village and on any region with mean grade over 45 % (the gates table's "HARD under 45 % mean"). F-9 is HARD on Alpine and on the
Mountain Village field regions (the gates table names them).

**Deviations from the spec** (none changes a bound):
1. Metric inputs: besides `AutoPlanResult` + `ResortPlan`, the functions take these items: the region area (km2), the density knob, the pinned list
   (S-7) and, for F-10 / H-2, a re-rate delegate. The host passes `SkiRunRater.Rate` over the snapshot. Tests pass `run => run.Band`.
2. A metric with nothing to measure (no pad, no lift, no pod with runs) returns `n/a` with pass = false. So a stubbed planner cannot turn a
   HARD gate green by being empty. Gates not yet armed print REPORT, so nothing fails today.
3. S-2 is recomputed from the stored `BandShare` (80th percentile band over the listed shares), because the host cannot see the cells.
4. H-2 / H-5 host search:
   - Start runs = tops within 100 m of the lift Top.
   - Run A continues into run B when A's bottom is inside B's outline or within 25 m of it.
   - The target is a base pad, or the lift's own pad (disc radius max(InscribedM, sqrt(area / pi)) + 25 m), or the lift's
     Bottom within 25 m.
   - Lifts, cat tracks and obstacles are not edges in the host search (stage A has no earthworks).
5. F-2 / F-3 count the runs with `RunLift == lift`. F-4 counts a run top inside, or within 25 m of, another run's outline.
6. P-4 sums the exact v2 layers (`ResortLayerBytes`) plus 49 B per snapshot cell for the v1 layers (an estimate, because the v1 arrays are private).
7. P-1: a miss is measured once more, and the smaller figure counts. Single runs showed 5-10 ms outliers on Delta / Snowbird that did not
   repeat. On the real heightmaps, P-1 stays REPORT because the v1 gate never covered real terrain. Synthetic cases are HARD.
8. D-4 / D-5 are SKIP here (A2's `WorkerTests` owns them). P-5 is SKIP (no tower-plan counter until A4). The 2.6 realism report is SKIP
   (needs the real lift / run layouts). S-7 only has the unit test (the matrix plans without pinned lifts).
9. The 3.5 km Mountain Village window is over the 9 km2 cap as a region. It runs as the 3 km square at (4895, -1528).
10. The "steep-compact" class of S-5 / F-7 is not a fixture attribute: Steep and Mountain Village are SOFT instead.
11. `Steep(seed)` values:
    - vertical 1,190 / 1,302 m
    - mean grade (28 m baseline) 59 / 64 %
    - 9.4 / 13.0 % of cells over 100 % (spec: about 15 %)
    - two benches, 8.5 / 6.2 ha and 11.2 / 4.5 ha, at 0.35 and 0.6 of the vertical (spec 6-10, the check accepts 4-12)

    `Benched(seed)` values: three benches at 0.22 / 0.42 / 0.62 of the vertical, 34 / 23 / 14 ha (seed 1). The top one is cut into the rim face
    and stays under the spec's 20 ha. The check wants two of 20-60 ha and three of 9 ha or more.
12. Base points come from lat / lon with the equirectangular ground-meter mapping of the builder. The elevations under them equal the
    village elevations of [E] 3.1 within 1 m (printed as `REPORT base point` in the matrix).

**Findings for the owner tasks** (not tuned):
- In Resort mode with the A0 stubs, every region plans exactly the v1 runs.
- The v1 kept share (runs / candidates) is 1-12 % on nearly every real region. The exceptions are tiny candidate counts: Zermatt 1 km2 7 of 23, Mountain
  Village steepest 1 of 2.
- The v1 kept share is 4-9 % on the Mountain Village field regions (field 1: 33 of 482 here, the field log 26 of 478). So F-9 starts
  far below its 0.40 bound.
- Plan time CoreCLR: 0.2-1.3 s. The longest is the 9 km2 region Delta at 1.32 s (P-3 bound 1.5 s).
- The v2 layers are 0.9 / 3.2 / 7.0 MB at 1 / 4 / 9 km2.

### A2 worker
(not started)

### A3 terrain + pods

Files: `.Terrain.cs` (stage Terrain), `.Pods.cs` (stages PodRoute, Pods), `tests/AutoGenBatch/TerrainTests.cs`. Kernels.cs is untouched. The
labeled Dijkstra is `DijkstraQueue` + `StepCost` inline in PodRoute. The labeling reuses `Root` (union-find) and `Chamfer`.

- **Terrain** (row chunks, m_Cursor2 phases 0-10):
  - m_OS = 3x3 box mean of m_O over open cells (segmentation only), m_Band, and the 3x3 smoothed height (scratch m_Dist[1]).
  - Ridges = usable cells with no D8 inflow (m_Flow still the own cell's area) that are a strict maximum of the smoothed height across one of the 4 axes.
  - Pad candidates (usable, m_OS < 0.25) are unioned with their earlier 8-neighbors in m_Order. The smaller root wins, so every parent is an earlier
    cell and one ascending pass flattens them into compact labels.
  - Components of at least 0.2 ha get the inscribed-distance chamfer (scratch m_Dist[2], seeds -half a texel, the DHard convention).
  - Pads = inscribed >= 9 m, at most 255 (largest kept), ids in smallest-cell order, built 16 per unit (R2).
  - Output: m_PadOf.
- **Pad = a graded site** (DECISIONS 447): `PlanPad.GradeM3 / GradeMaxM` (added to Plan.cs) = balanced cut (= fill) / the deepest
  cut or fill. Both values are for leveling the cheapest station's 16 x 24 m lot at the station spot (long side along x or z, the smaller). This is a screening
  number. Stage B grades with the cat track kernel. Station capability stays `InscribedM >= StationHalfDiagonalOf(t)`.
- **PodRoute**: sources = cells of pads with InscribedM >= 14.4 m (and m_O < 0.70, out of the edge strip), label = the pad, cost =
  StepCost(floor 0.08, ceiling = hard 0.70). The label rides each relaxation (m_PodOf scratch, cost m_Dist[0]). A multi-source
  Dijkstra's nearest source is the first pad on the least-cost descent.
- **Pods** (as merged in A3. R2 changed Down(P), the merge, the cells and the border phases: see "A-R round R2" below):
  - Down(P) = the label of P's lowest outside 8-neighbor carrying another label.
  - The 80 m merge runs in ascending MinY, root = the lower pad.
  - Pod components = 4-connected cells of one surviving pad's catchment, minus ridge cells, minus the lower pad's own cells.
  - Borders (ridge cells touching two components of one pad, diagonal touches) go as sorted runs into an adjacency list.
  - Small components (under 2 ha or 80 m) join the neighbor with the longest summed border (ties: smaller root) or drop.
  - Statistics per group.
  - Final pods need a top cell (DHard >= 35 m) at least 80 m above their lowest cell.
  - Cap round(4 x AreaScale).
  - Sort (LowerPad.MinY, top cell).
  - m_PodOf = pod id (MUnreached = none).
- **Plan hand-over**: `TerrainPlan(plan)` copies pads and pods (fresh arrays, BandShare cloned). The FinishResort
  stub calls it (Flow.cs, one line A6 keeps). The planner computes pads / pods before Lifts, so every Reselect except Density-with-runs keeps them.
- **Scratch contract for A4+**: after Pods, m_Order and m_Dist[0..2] hold nothing usable (TopRows / StartPassRows rewrite them).
  m_PadOf, m_PodOf, m_Band, m_Ridge, m_OS are final and stay valid across Reselect.
- **Public for the host**: `BandOfGrade`, `PrimaryClassOf`, `MaxPodsOf`, `PodGrades` (S-2 oracle), `BufferBytes` (P-4), `PadCount`,
  `PodCount`, the constants.
- **Measured** (CoreCLR Release, Step(4 ms), unit max best of 3):
  - 9 km2 fBm: Terrain 17 ms in 122 units (max 0.9), PodRoute 114 ms in 119 (max 1.45), Pods 20 ms in 142 (max 0.6).
  - Planner buffers 51.7 MB (v2 layers 6.7 MB).
  - Alpine 3.9 km2: 9 / 30 / 5 ms.
- **Deviations from the spec** (also in the private handover note):
  - The planner does not compute summits. No stage A consumer needs them. PickSummit's seeded jitter would make the plan depend on the Seed knob, which restarts after Pods.
  - UpperPad counts pads the pod's cells are OR touch (a bench at the pod's top is its own source, never inside).
  - The lower pad's own cells are not pod cells. Otherwise two faces draining onto one pad always join through it, and the ridge split never fires.
  - Pod components are 4-connected.
  - A pod without a 35 m-inside top cell drops.
  - Down(P) as above.
- **Tests**: `Terrain` suite:
  - two terraces 60 m apart = 2 pads / 1 pod (merge)
  - two terraces 110 m apart = 2 pods (bench, UpperPad)
  - ridge = 1 pad / 2 pods
  - a 35 % plane = no pad
  - invariants (S-1, S-2 recomputed from the cells, shapes) on Alpine 1-3, Alpine lower, the terraces
  - S-3
  - D-1 / D-2 / D-3 on the pads and pods
  - P-1, P-4

  `TerrainCase(id, input, expectBench, expectPods)` is the per-fixture check + REPORT for A1's `-- v2` matrix (real fixtures SKIP here until A1's loader merges).

### A4 lifts + overlay

Files:
- `.Lifts.cs` (stage Lifts, `LiftPlan`)
- `QuickRunsToolSystem.Draw.cs` (the plan overlay. Picks use A7's `QuickRunsPlanRules.NearestPod` / `NearestLift`)
- `ResortPlanLabelSystem.cs` (world labels)
- `tests/AutoGenBatch/LiftTests.cs`

Line edits elsewhere:
- Flow.cs `FinishResort` calls `LiftPlan(res.Resort, res)` after `TerrainPlan` (A6 keeps the line).
- `V2Tests.cs` arms S-4..S-7 and P-5. Its other edits: a `LiftCase` call per region, the S-4 / S-5 soft argument, P-1 via `UnitMsWithoutTowerPlans`.
- PlanTypes drops the "A0 stub: the plan has lifts" check (the Lifts stage now runs one unit or more).

- **Reuse**: `LiftTowerPlanner.Plan` with `LiftTowerPlanConstants.For(type)` (cached per scan), `LiftServedRule.Reaches` for Serves,
  `SkiCatalog` station + line costs, `StationHalfDiagonalOf` (A3, a private array since R2) for lots. There is no second tower planner and no second served rule.
- **Phases** (m_Cursor2):
  - 0: setup (lists, pod values, tower constants, pinned lifts, the wanted count).
  - 1: the pinned corridors' pod-cell counts (rows). Then each pinned lift takes the pod with the most cells within 150 m of its chords.
  - 2: candidates, one pod per unit.
  - 3: selection, one pick (at most one tower plan) per unit. Then `EnterRoute()`.
- **Candidates** per unserved pod and lift type: bottom = a station spot on the pod's LowerPad, top = the UpperPad's spot or TopCenter.
  The spot walk starts at the pad Center. It steps 6 m at a time (up to 150 m) toward the other end. It stops when the station disc (center, 16 ring and
  8 half-radius points) is on the pad. Norms: length 300-1,500 / 600-2,200 / 800-2,700 / 800-3,000 m, rise 95-512 / 95-512 / 96-781 /
  220-888 m (FG / DQ / GD / tram, the catalog's p10-p90). A chord over max rise or max length gets only a **lowered top**. The planner walks
  the top down the chord in 10 m steps. It stops when the length is at or under the max and the rise is under
  0.95 x max. (The planner checks the float distance, not the step.) Sunk = the end has no pad with `InscribedM >= half-diagonal` (the station sinks as today. DECISIONS 447: a pad is graded
  terrain, never required flat).
- **Score**:
  - Pod value = ha x DevelopableShare x `SkiResortScore.SkiersPerHa(Rated, ScoreBandOf(PrimaryClass))`.
  - Minus cost x 1e-3 (`LiftCostWeight`: NOT a tiebreaker, as the A4 handover first said). Costs of 93-350 k give 93-350 points against pod values of
    80-1,200. So cost is a real term and turns small pods negative. This is kept on purpose, A-R AR-27. 2e-4 would make it a tiebreaker.
  - Minus 0.5 x value per crossing of another pod's fall line (TopCenter -> LowerPad.Center).
  - Stack bonus +0.2 x value when the bottom is within 300 m and 80 m of a selected lift's top.

  The planner orders the candidates of a pod by (sunk ends, type rank, top kind). The upgrade (detachable before fixed grip) applies to pods in the top third by value.
- **Selection**: lazy greedy over pods (best front candidate by static score + stack bonus, ties by pod Key).
  - A conflict advances that pod without a plan. A conflict is: the two station lots overlap a selected lift's lots, or the chord crosses one.
  - One tower plan per unit. A candidate is clear when `plan.Clear` holds, there is no sampler miss and MaxEdgeSlope <= 0.97 x the type cap.
    Otherwise the pod gets its next candidate (at most 3 plans per pod).
  - Selection stops at the wanted count, at the plan cap `MaxTowerPlansOf = 3 x MaxPodsOf`, or when no pod has a candidate left.
  - Unserved pods that still had a candidate when selection stopped count as **capped**. So capped 0 with fewer lifts than wanted = the terrain ran out, not the selection.
- **Tower plan inputs**: stations at both ends, `CheckEdgeSlope`, end node heights = the snapshot heights, ropes at node height +
  `RopeAboveBase(0)` (the planner's own fallback when no station lane anchor is known). The chord sampler returns the last good
  height on a snapshot miss and counts the miss (a miss = not clear).
- **Count**: Auto = clamp(max(round(km2 / 2), ceil(vertical / 400)), 1, 9), minus the pinned lifts. Knob 1-9 caps the proposals (pinned
  lifts do not count against the knob). `LiftsWanted` = wanted + pinned.
- **Pinned lifts**: a missing end (NaN) is filled from the polyline's free ends at snapshot heights. A census copy without chords stands in
  its station chord. A lift off the map, or a row without any finite point, keeps Pod -1 and conflicts with nothing. The planner plans around
  pinned lifts (their lots and chords conflict). Pinned lifts keep `Towers = -1`, no TowerXz.
- **Keys**: proposal `Key = Fnv(pod.Key, type)`. Ids: pinned first, then proposals in selection order.
- **LiftPlan** (in FinishResort) sets or copies these items:
  - the lifts (TowerXz cloned)
  - `Pods[k].Lift`
  - Serves (run top = its highest node, `LiftServedRule.Reaches` with the type's radius / margin)
  - StartsNearTop / EndsAtBottom (100 m)
  - `NoLiftSite` when nothing was proposed but lifts were wanted
- **Reselect**: Lifts restarts at Stage.Lifts (pads / pods kept). Seed restarts at Seeds and keeps the lifts. Home restarts at Finish.
- **Diagnostics getters**: `TowerPlans`, `TowerPlanMs`, `TowerPlanMaxMs`, `TowerPlanLog` (pod, type, length, rise, ms, towers, clear per
  plan), `LiftStat(k)` (candidates, norm rejects, lowered tops, tower fails, conflicts, capped pods).
- **Overlay** (Draw.cs, from `m_Published.Resort` only, cached with the run pieces on `m_DrawDirty`):
  - Per proposed lift: a dashed chord in the Home verdict's piste color (white while Home is None, until A6. Violet for Download), 6 m tower
    discs, and hatched (StyleFlags.Grid) station lots of the type's half-diagonal.
  - Pinned lifts get white station rings only (the game draws them).
  - Hatched white pads under the stations, plus the three largest unused. Benches have a dashed edge (every other side of a 24-gon).
  - A ring per pod at TopCenter in its class color. It replaces the v1 cluster rings when the plan has pods.
  - Marks take at most half of `MaxPieces`. The runs get the rest.
  - The overlay skips non-finite spots.
  - `[cost]` (DiagnosticsOn): `QuickRuns [cost] plan overlay: Draw mean / max ms over 600 frames` after each resort publish.
- **Labels** (ResortPlanLabelSystem): TMP meshes as in SkiRunLabelSystem. The system builds them once per published plan (reference compare), at most 64:
  - "P2: Detachable, 1.3 km, 540 m up, 7 runs" at the chord midpoint (white).
  - "Pod B: Intermediate" at TopCenter (class color).

  The system draws them only while the quick runs tool is active in Preview. Pinned lifts get no label (the game names them, and the plan has no census name).
- **Deviations**:
  1. P-1 excludes the Lifts slot (`UnitMsWithoutTowerPlans`). One Lifts unit runs one tower plan (spec 4.4: 10-44 ms), and P-5 bounds it.
  2. P-5's 50 ms per plan is HARD on synthetic fixtures and REPORT on the real heightmaps, as P-1 (the plan cap is HARD
     everywhere). The time is the shared tower planner's, which A4 does not tune.
  3. S-4 n/a and S-5 under the band are SOFT where the terrain is the limit: fewer lifts than wanted and no capped pod (`LiftsTerrainLimited`).
     A proposal outside the norms and a count over the band stay HARD.
  4. The determinism checks hash the lifts on the host (`LiftsHash`), because `ResortPlan.Fingerprint` is A6's and stays 0.
  5. No pinned-lift label.
- **Measured** (`-- v2`, CoreCLR Release):
  - Tower plans: 0-5 per region, 0.4-28 ms each. The one outlier is Mountain Village base 4 km2, a 1,687 m detachable on pod 0 at 57.5 ms
    (not clear. It repeats, so it is not GC): REPORT.
  - Synthetic: Alpine 1-3 three lifts each (FG / DQ 2 / 1, two bench bottoms), Benched 3 / 2, Steep 3 / 4.
  - Gentle base windows (Okemo / JacksonHole / Fjords / Plains 1 km2, Delta / Plains 4 km2) and Snowbird / Chamonix steepest have no pod
    or no chord inside the norms: 0 lifts, NoLiftSite.
  - Serves is low everywhere (the runs are still v1 until A5).
  - The per-region table is in the A4 handover note.

### A-R round R2: terrain, pods, lifts

This round covers the A-R critique's terrain / pods / lifts items (calibration d, AR-7, AR-8, AR-9, AR-10, AR-17, AR-18, AR-24, AR-27). Runs mode is untouched
(every change sits in a Resort-only stage or behind `AutoPlanInput.MainThread`). V1Golden is green.

- **Pod bottoms are 1 ha** (calibration d): `PadBottom` = capable (inscribed >= 14.4 m) and `AreaM2 >= PodBottomMinAreaM2` (10,000 m2).
  Smaller capable pads are still PodRoute labels, but they always merge into Down(P) (they stay pads: benches, top stations). `PadMinAreaM2`
  0.2 ha still makes a pad. The root of a merged set is its lowest pod-bottom pad (else its lowest pad). A set with no pod-bottom pad
  yields no pod (BuildPods skips a group whose Key is not a PadBottom).
- **Segmentation semantics** (AR-10):
  - Down(P) comes only from neighbors with `h < P.MinY` (no sideways or uphill "descent").
  - The 80 m drop is measured against the merged set's root's MaxY (a 60 m + 50 m staircase no longer chains into one pod).
  - Pod cells exclude every pad whose root is the key (not only the root pad).
  - Pad cells never border (a flat pad's ridge cells no longer join its faces).
  - New in this round, a **bench link** (phase 6): components touching a bench merge into their key border through the bench (one incidence, each
    to the largest of them). So a face too small for a pod still joins its neighbor across the bench. Without it, the staircase case lost
    the lower pod entirely (both 60 m and 50 m faces small and isolated). The Terrain suite's "two terraces 60 / 110 m" case proves the link
    (3 pads, 2 pods: lower + 60 m terrace with about 110 m of vertical, and the 110 m terrace).
- **Bounded units** (AR-9): Pods phases 0-15.
  - The system stores borders as runs (a repeat of the last pair adds a weight).
  - It counts them per smaller component and scatters them into buckets (counting sort, 8,192 entries per unit).
  - It sorts and folds each bucket (equal pairs summed) in units of 8,192 entries. Then it builds the adjacency in units. No unit sorts the whole list.
  - `CompRoot` halves paths.
  - PodSmallMerge settles a group with no neighbor (not rescanned until a group merges into it) and spends at most 8,192 adjacency entries per unit.
    The merge order and result are those of the sequential loop.
- **Pads sliced** (AR-8): Terrain phases 0-10. Phase 8 orders the pads. Phase 9 builds 16 per unit (lot grade, access distance).
  `AccessDistance` binary-searches the x window of the sorted `Access` list and walks outward while the x gap is under the best
  distance. The result equals the full scan's (Terrain suite "access distance" compares every pad with a brute force). `PlanPad.AccessM` keeps its
  meaning: meters from the pad's station spot to the nearest access point, +inf when none. The tool's `CollectAccess` keeps both
  ends of every road edge and widens the 25 m step so the interior samples stay within 2,048 over all edges. The total length sets the step, so the iteration order does not matter.
- **Main-thread fallback** (AR-7): `AutoPlanInput.MainThread` (the tool sets it from the fallback setting, Reselect copies it). With it set, the Lifts
  stage runs no tower plan and selects each pick with `PlanLift.TowersUnchecked` (Towers -1, no tower spots). `TowersUncheckedCount`
  counts them. The worker path is unchanged. A tower plan takes 1-5 ms for a fixed grip and 10-60 ms for a detachable on CoreCLR (matrix
  P-5), and several frames on Mono. No length cap would keep a detachable inside the 4 ms frame budget, so the fallback skips them all. Where
  no worker plan failed, the main-thread run selects the same lifts (Lifts suite "main thread").
- **Allocations / statics** (AR-17, AR-18): cached `Comparison` delegates in Terrain / Pods, a grown keep array, pooled BandShare arrays
  (TerrainPlan clones them into the plan). The `TerrainBandFloor` and `StationHalfDiagonalM` arrays are private: `TerrainBandFloorOf(b)`,
  `StationHalfDiagonalOf(type)`, `StationTypes`. `StationHalfDiagonalM` remains as a property that returns a fresh copy (the overlay
  indexes it). AR-17's Fans / Flow allocations belong to round R1.
- **Measured** (`-- v2`, 57 regions. The machine was under load during both runs, so this note does not judge the timing gates):
  - lifts 112 -> 97
  - under 400 m 13 -> 10, under 600 m 43 -> 30
  - length p10 / p50 / p90 376 / 735 / 1,500 -> 391 / 832 / 1,500 m
  - lift bottoms on pads under 1 ha 28 -> 0
  - Pods drop on benched terrain (Alpine 1-3 8 -> 3 / 3 / 2, Steep1 10 -> 3, JacksonHole base 9 km2 5 -> 1).
  - S-3 stays 2+ per 4 km2 wherever pods exist.
  - S-6 median bottom moves both ways (SOFT, REPORT).
  - Synthetic 9 km2 fBm: Pods 316 units (was 142), max 0.80 ms under load.

### A5 run fans

Files: NEW `.Fans.cs` (sinks, fan seeds, the Resort pick, the merge approach, PodFull, `FanRunPlan`), `tests/AutoGenBatch/FanTests.cs`.

Line edits elsewhere:
- Select.cs (all behind `FansOn`, so Runs mode is bit-identical: V1Golden green):
  - `Cand.BranchOf`
  - `Accepted.MergeInto / BranchOf`
  - the `FanPick` call in UnitSelect
  - BeginAttempt (fan gap, no TopTaken for a branch, the merge search after the branch leaves its parent, `FanApproach`)
  - AttemptStep (`PodFull`)
  - BuildRing (branch taper, approach gap, `FanHalfWidth`)
  - CheckRing (MergeGapM to the branch parent)
  - Rate (chamfer reach `FanChamferM`)
  - FinishPairs (a branch and its parent are a merge pair)
- Flow.cs `FinishResort` calls `FanRunPlan` (merge with A6).
- SkiRunAutoPlanner.cs appends `AutoReject.PodFull`.
- QuickRunsRules.cs buckets PodFull with the overlaps.
- Tests: PlanTypes drops the two A0 stub checks. V2Tests calls `FanCase` per region, `V2Fresh` copies `Pinned`, and V1-4 accepts Runs NoRuns -> Resort Ok (see below).

V2Hard is NOT armed for F-1..F-10 (calibration, below).

- **Sinks**: Route sources are each lift's BottomPad cells within `SinkPadM` 90 m of the bottom station, plus a `SinkDiscM` disc (40 m, 25 m since A-R round 1). They exclude
  pad cells within 60 m of any lift top (a stacked lift's top stays skiable). With no lift that has an open bottom, the sources are the v1 base band and v1 seeds.
- **Seeds** (density-free reserve, so Mix / Density / RunCap Reselect at Select):
  - Tier 0: within `TopSeedM` 100 m of the top.
  - Tier 1 / 2: the served corridor (upper third / rest).
  - Tier 3: the lift's own or an unserved pod above its lower third, only when the line drains to this lift's bottom.
  - The lattice is 2-cell, sorted by tier then height + v1 jitter, spaced 20 m, at most 80 per lift and pass, each pre-walked (MakeCandidate's walk).
  - The list unit is bounded (1,500 keys, 40 walks) for P-1.
- **Pick** (one candidate per unit): the lift with the lowest fill.
  - Its tier-0 seeds come first, until it has 2 top runs (none once it has 4).
  - Else the v1 band-deficit order on the lift's own targets (`Targets` on `FanRunsPerLift` 5 / 7 / 10), branch x1.5, a seed draining to another
    lift x0.8.
  - Before a seed becomes a candidate, the planner re-walks it against the accepted runs. Inside a gap, the planner drops it (a tier 0-1 seed
    next to a run becomes its branch instead). It also drops a line whose merge would be TooShort / MergeStub. So TopTaken is 0 and the
    v1 collapse is gone: kept share 44-79 % on 4 of 5 HARD synthetic fixtures (v1: 5-9 %).
- **Branches**: a top run seeds branches 20-99 m below its top at once. After a lift's second run, every run seeds them 100-400 m below its
  top (every 50 m, both sides, the first cell MergeGapM + half width out). A branch keeps the near gap and must leave it within 150 m.
- **Merge approach** (the main finding): with one sink per lift, the fall lines converge radially. So a second line enters the 100 m
  fan gap long before it meets the first, and a merge there cuts it to a stub. A line that enters a fan gap merges where it reaches the
  near gap (`max(v1 GapOf, 30 m)`, DECISIONS 455. Centerlines stay at least 37 m apart outside merge and split zones, F-5) if that is within `MergeApproachM` 200 m. BuildRing keeps
  the near gap on that stretch. Tried and dropped: a greedy traverse that leads a branch out of its parent's gap (more Overlap rejects,
  fewer runs) and SinkPadM 250 m (F-3 broke, fewer runs).
- **Results** (Normal, synthetic, v1 -> fans):
  - Alpine1 48 -> 14 runs [7, 3, 4] per lift
  - Alpine2 48 -> 9 [7, 1, 1]
  - Alpine3 48 -> 15 [6, 7, 2]
  - Benched1 44 -> 12 [3, 5, 4]
  - Benched2 39 -> 11 [5, 6]

  The `-- v2` matrix, gates passed of 55 regions (before -> after):
  - F-1 0 -> 14
  - F-2 0 -> 10
  - F-3 0 -> 44
  - F-4 1 -> 22
  - F-5 0 -> 24
  - F-6 0 -> 23
  - F-7 20 -> 31
  - F-9 0 -> 25
  - F-10 waits for A6's MountainMix.
- **Armed**: FanTests checks F-3 and F-7 on Alpine 1-3 and Benched 1-2 (they pass everywhere there). F-1, F-2, F-4, F-5, F-6 and F-9 miss
  on some HARD fixtures and stay REPORT. The reason is that the lifts A4 places are short (7 of the 14 Alpine / Benched lifts are under 600 m, Alpine2 lift 3 is 301 m with
  110 m rise). A second reason: converging fall lines give few independent lines per lift. The calibration questions are in the A5 handover.
- **V1-4**: Runs mode NoRuns -> Resort Ok is accepted (NoRuns is a selection outcome, not a refusal): JacksonHole steepest 4 km2 has 0
  v1 runs and 11 fan runs.
- **Interface for A6**: `AutoRun.Cluster` = the lift index (one ClusterInfo per lift, Center = its top). `FanRunPlan(res.Resort, res)`
  fills RunLift / RunPod / PodRuns / Pods[k].RunFirst / RunCount and must run after `LiftPlan`. Diagnostics: `FanReserve`, `FanDropped`,
  `FanDropWhy(0..3)`, `FanBranchCandidates`.

### A6 way home

Files: `.Flow.cs` (`FinishResort` body), `tests/AutoGenBatch/FlowTests.cs`.

Line edits elsewhere:
- `V2Tests.cs` arms H-1, H-2, H-3, H-5, H-6, F-10 in `V2Hard` (H-5 / H-6 stay SOFT). It passes `p.Lifts.Length == 0` as the soft argument of H-1..H-3 (n/a without a lift
  is no miss).
- `PlanTypes.cs` replaces the "A0 stub" fingerprint check by "the plan fingerprint is folded in". It compares the v1 warnings
  only (the three v2 flags masked).
- `LiftTests.cs` `LiftCase` builds its pinned input as a copy. It set `Pinned` on the matrix's shared
  input, so D-1..D-3 compared a pinned planner against unpinned fresh runs once the plan fingerprint counted.

- **Hook**: `FinishResort` (the only Finish hook, one unit, after v1 kinds / clusters / warnings) runs these steps in order:
  1. `TerrainPlan`, `LiftPlan` (A3 / A4 lines kept).
  2. RunPod / PodRuns, RunLift.
  3. The graph, ways home, mix and hull, site inputs, pod clusters.
  4. The Download / DeadEnd warnings.
  5. `ResortPlan.Fingerprint` (never 0, so `AutoPlanResult.Fingerprint` folds it. Resort and Runs fingerprints now differ).

  Home knob Reselect = the Finish restart (A0). D-3 Home passes.
- **RunPod / RunLift**: A5's `FanRunPlan` (Fans.cs, called from `FinishResort` right after `LiftPlan`): RunLift = the seeding lift,
  RunPod / PodRuns / RunFirst / RunCount as in the A5 section. It runs before `PodClusters`, which then makes `AutoRun.Cluster` the
  pod. After Finish the seeding lift is `plan.RunLift`, never `Cluster`. (The merge dropped A6's served-rule RunLift and top-cell RunPod.)
- **Graph** (`PlanEdge`, nodes: pads, then run top / bottom pairs, then lift bottom / top pairs). The edges are:
  - run edges (rated band)
  - links from a run bottom to run B's bottom with B's band. A link exists when the bottom is inside or within `LinkMeters` of B's outline,
    or when the run merges into B (`MergeInto`). v1 merge bottoms lie 25-26 m from B's outline, and the junction pair is the link
  - bottom -> every pad with a cell within 25 m
  - bottom -> a lift bottom within 25 m
  - lift bottom <-> its BottomPad
  - lift top -> TopPad
  - lift top -> every run top within `LiftNearM` 100 m (the F-2 radius, and the host oracle's)
  - lift edges uphill
  - cat tracks (obstacle outlines) join every node they touch, Green, both directions
- **Ways home**: `EasiestDescent` (public static, pure):
  - Dijkstra over (hardest band, meters), ties by node index.
  - Lift edges are excluded.
  - The top's link onto its own TopPad is excluded (a top on a base pad still needs a run).
  - A cat track from the top counts.
  - Targets: the lift's bottom node, its BottomPad, every base pad.
  - Wanted = the knob (Auto: Green on a base BottomPad, else Blue).
  - Home = the band (Flat counts as Green) when at most Wanted, else Download. This is spec 4.6 literally: Black / DoubleBlack are never set in stage A,
    and HomeRun is kept for any path.
  - Pinned lifts with neither station in the region box get None (no log on the worker, the tool may log it).
- **Dead ends**: a run bottom with no outgoing edge that is not on an existing run (obstacle) or a cat track. (A-R round 1 supersedes this:
  targets, verdicts, top links and dead ends are as in that section below.)
- **Mix / density**:
  - `MountainMix` = run-km share per stored band (F-10 equals the host re-rate on all 54 regions).
  - RunKm.
  - HullKm2 = convex hull of ring vertices and lift stations grown 50 m (Steiner, the host's `HullKm2` formula).
  - RunKmPerKm2.
- **Site inputs**:
  - `Kind` = `QuickRunsRules.SiteTierOf(SitePercent(MeanGrade))` (the v1 line, unchanged)
  - MeanGrade
  - SteepShare
  - largest bench pad (ha)
  - largest base pad (`BaseBenchHa`)
  - pods
  - pods whose lift has a Green verdict
- **Clusters**: one per pod (Center TopCenter, Pod, PodBand = `ScoreBandOf(PrimaryClass)`), plus one trailing plain cluster for runs
  outside every pod (the V1-3 invariant: every run has a cluster). `AutoRun.Cluster` = its pod (or that trailing index).
- **Flow suite**:
  - hand graph (minimax beats a shorter black run, regrade flips it, length ties, no path, top-pad hop vs cat track)
  - per fixture (Alpine1 lower / full, Benched1, Steep1 = the dead-end fixture):
    - H-1..H-3, F-10 HARD, H-4..H-6 REPORT
    - graph shape
    - dead ends recounted from the graph
    - warnings
    - RunPod / PodRuns / clusters
    - mix, hull, site, fold
    - the Home knob's monotonicity
  - a cat track from a Download lift's top to the base pad makes it Green

Measured (`-- v2`, v1-shaped runs, A5 not merged):
- H-1 / H-2 / H-3 45 ok, 9 n/a (no lift).
- F-10 54 ok.
- H-4 25 ok / 29 over 5 %.
- H-5 6 ok.
- D-1..D-3 all ok.
- P-1 54 ok.
- **Every lift is Download** (108 lifts): 76 have no run top within 100 m of their top. The rest reach only black / double black runs (A4's Serves is low for the same reason).
- Dead ends come from v1 runs ending in the base band where no pad lies (Steep1: 9 of 12).

Both findings are what A5's fans (seeded at lift tops, sinks at bottom pads) change. The per-fixture table is in the handover note.

After A5 merges:
- Arm H-4.
- Recheck H-2 against the host. The host oracle uses centerline bottoms and pad discs. The planner uses merge links and pad cells. A mismatch shows up as an H-2 FAIL, not silently.
- RunLift = the seeding lift if A5 records it.
- The PlanTypes "A0 stub: Resort runs / v1 warnings differ" check goes (A5 changes runs).
- H-5 and H-6 numbers for the A-R calibration.

### A-R round 1: graph, fans, verdicts (critique AR-1..AR-4, AR-7, AR-13, AR-17)

Rulings: DECISIONS 451 (b) the verdict is the real band, (c) the v1 base band is not a way out. Files: `.Flow.cs`, `.Fans.cs`, `.Plan.cs`
(`PlanPad.TouchesBase`, `SiteVerdict.PodsServed`, comments), Select.cs `Warn` (one guard). Tests: `V2Metrics.cs`, `V2Tests.cs`, `FlowTests.cs`.
Runs mode is untouched (every change is in Resort-only code or behind `m_Mode`). V1Golden is green.

- **Way out** (AR-2): the target set of WaysHome and of the dead-end count has three parts:
  - every lift bottom node (pinned or proposed, finite station)
  - every pad holding a lift bottom
  - every pad touching the v1 base band (`TouchesBase`, from the pad components. Terrain.cs keeps `IsBase` with its lowest-pad fallback for the S gates)
    whose `AccessM` is within its radius (`PadRadiusM`: max(inscribed, equal-area disc)) plus `HomeAccessWalkM` 100 m (`PadIsWayOut`)

  Existing cat tracks reach those through their graph edges. Without access points (the host cases, a region with no road within 450 m), no base pad is
  a way out by itself. Auto Wanted = Green only when the lift's bottom pad touches the base band.
- **Verdicts** (AR-3): Home = the easiest descent's hardest band, Green .. DoubleBlack. Download only when no downhill path reaches a way
  out. Wanted is information (and the color / warning wording, R3). It never changes Home (FlowTests: Home and HomeRun are equal under
  Auto, Green and Blue). `MeetsWanted(home, wanted)` = Green, or Blue under a Blue wish.
- **Lift top links** (AR-3b): lift top -> every run whose highest outline node `LiftServedRule.Reaches` accepts (the type's corridor
  and top margin on the lift's chords. `ServeOf` builds the same row as Lifts.cs `Serves`), instead of the 100 m disc.
- **Merge geometry** (AR-1):
  - The fan merge stop is `FanMergeStopM` = min(near gap, `JunctionReachM`) + `MinHalfWidthM` = 27.5 m (was
    33.5 m). So a ring at least 7.5 m wide ends within 20 m of its parent, and BuildRing keeps the junction.
  - A run bottom with no run link links to the nearest run within `MergeLinkReachM` 35 m when `LiftServedRule.OutlinesLinked` (the game's link rule)
    joins the outlines (`NearRunLink`). Two cases have no run link: a merge whose junction was still dropped, and a fan line that the near gap stopped
    beside another run without the Merge flag. These cases were 15 of the 45 "free end" dead ends of the first `-- v2` pass, 28-35 m from a run.
  - `SinkDiscM` = `LinkMeters` 25 m (was 40 m), so a route ending in the disc off the pad lies within the lift-bottom link.
- **Dead ends**: a run bottom with no downhill path (every edge but lifts) to a way out, not on an existing run or cat track. The count is one reverse
  breadth-first pass from the targets (`CountDeadEnds`). A run ending on a bench with no lift bottom, road or track is now a dead end.
- **AR-4**: the v1 `NoEasyWayHome` warning is skipped in Resort mode (NoGreen stays: it reports the mix).
- **AR-7 / AR-17**: `EasiestDescent` is a binary-heap Dijkstra keyed (band, meters, node), the linear scan's order exactly (FlowTests
  compares it with the old scan on 40 random graphs, every source). `DescentScratch` builds the adjacency once per graph and is reused
  for every lift. Flow and Fans per-scan arrays are Grow'd fields. These are: graph boxes, run tops, edge meters, targets, reverse adjacency,
  cluster parents, hull points, cat track touches, FanCaps' six arrays, FanPodOf's counts, FanBest's done flags, the seed ordinals.
- **AR-13**: `Site.PodsWithGreenHome` (name kept) counts served pods whose lift's verdict meets its Wanted. `Site.PodsServed` is the
  denominator (pods with a lift). The tooltip / hub wording is R3's.
- **Gates**:
  - F-9 is armed (V2Hard): runs / (runs + TopTaken + TooShort + MergeStub) over 40 %. Overlap rejects are the fans
    working. The run prints TopTaken + TooShort as a share of all rejects but does not gate on it. The share tripped on 2 of 4 and 3 of 3 rejects with 85 % / 57 % kept.
  - A low share fails only with TopTaken among the rejects (the v1 mechanism). Fjords base 9 km2 after the 1 ha pod bottoms keeps
    38 % with 10 TooShort and 0 TopTaken. Every fan region keeps 68 % or more. The v1 collapse is 1-19 %.
  - F-9 is REPORT on a plan without lifts (v1 seeding, class 1) and with the A5 SOFT rule on steeper regions.
  - H-4, F-2 and F-5 are coded to their armed definitions but stay REPORT:
    - H-4 under 5 %, "no way out: no lift site" without lifts.
    - F-2 at least one run top within 100 m per lift, "F-2 2-4" SOFT.
    - F-5 no pair under 40 m outside a merge zone, "F-5 median" SOFT.
  - They stay REPORT because the first `-- v2` pass missed on real numbers that this round cannot fix:
    - H-4 on 27 regions (45 free ends, 25 of them over 60 m from any run, pad or station: sinks / stage B work).
    - F-5 on 7 regions with 30-39 m pairs (6 of them already under 40 m before this round: the 26 m near gap allows them).
    - F-2 on one lift (Alpine2 P2, served only by a run below the top).
  - Arming each one is one line in `V2Hard` once its numbers hold. F-1 waits for R2's 1 ha pad change.
- **Host oracle**:
  - H-2 starts at the served runs (station chord, the type's radius and margin). It continues through outline links, merges and the near-run link,
    and ends at a way out (`HostWayOut`). It must equal the verdict (Download = no path).
  - A run bottom touches a pad within 25 m of its inscribed disc (`InscribedM`: all pad, so the planner links too. The equal-area disc
    over-reached irregular pads after the 1 ha pod bottoms and gave host-only Black paths). A run bottom also touches a pad where the graph has the
    run-bottom-to-pad link (the host has no pad cell map. A long bench reaches 150 m past its center on Benched1). That link is the only graph fact H-2 / H-5 read.
  - H-5 enters at base pads with access (every base-touching pad when no pad has access).
  - `HostDeadEnds` recounts the dead ends from the graph (a FAIL when it disagrees). `-- v2` prints one `REPORT dead end` line per dead end:
    band, length, cause (merge dropped / merge / near a station / free end), Merge / MergeInto, distances to the nearest run, pad edge and lift bottom.
  - FlowTests keeps H-4 REPORT on the synthetic fixtures. They pass no roads, so a run ending on a base pad without a lift bottom is an honest class-4 dead end there.
- **Results** (`-- v2`, 57 regions): 0 correctness failures (3 timing misses under load: two Select-phase units, P-2 Plains base
  4 km2 0.96 s).
  - H-4 under 5 % on 22 regions, over on 24, "no way out: no lift site" on 9 (before: 23 / 32, the base band counted as home).
  - 58 dead ends in all: 0 "merge dropped", 42 free ends (30 over 35 m from any run), 12 merges into a free end, 4 near a station.
  - F-2 43 / 12, F-5 30 / 25, F-9 46 ok / 0 FAIL / 9 REPORT, H-2 46 ok (0 FAIL), H-5 12 / 43.

### A7 hub + tooltips
Files:
- `QuickRunsToolSystem.cs` (header, knobs, input pick)
- NEW `QuickRunsToolSystem.Plan.cs` (toggles, picks, plan around)
- NEW pure `QuickRunsPlanRules.cs` (linked into AutoGenBatch, tests in `QuickRunsTests.cs`)
- `QuickRunsTooltipSystem.cs`
- `SkiResortHubUISystem.QuickRuns.cs`
- `bindings.ts`
- `QuickRunsControls.tsx`
- small additions to `hub.module.scss`, `hubText.ts`
- `LocaleEN.cs` (three keys)
- `Scan.cs` (one test in `RefreshEnabled`)
- harness scenario + SHOTS

Details: `QuickRunsToolSystem.md` (Resort Planner hub side), `QuickRunsControls.tsx.md`, `SkiResortHubUISystem.md`.
- The hub shows plan data only for a Resort preview. It reads `ResortPlan` Pods / Lifts / RunPod / RunLift / Site / MountainMix / RunKm /
  DeadEnds as the types define them. So the rows appear as A3 (pods), A4 (lifts) and A6 (RunPod, verdicts, mix, site) merge. Until A4, the
  lift rows are empty. The pod rows (every pod "without a lift") show 0 runs until A6 fills RunPod.
- `HubContract` is not bumped here. `contract.lock` and the harness `base.contract` follow the parent's bump.
