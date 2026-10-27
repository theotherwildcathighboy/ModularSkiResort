# Resort Planner (v2): index and seams

The planner rebuilt lift-first (code names stay `SkiRunAutoPlanner*` / `QuickRuns*`). Design, build spec and gates live in the private
docs (`docs/research/quickruns-v2/`); this note is the tracked index: where each piece lives, which hook calls which file, and one
section per task for what that task learned. Tasks write only their own section.

## Modes

`AutoPlanInput.Mode` is `Runs` (default: v1 exactly, test host only) or `Resort` (v2; the tool always sets it). Every v2 hook in a v1 file
is guarded by `m_Mode == AutoPlanMode.Resort`, so Runs output and fingerprints are bit-identical to main at the branch point. The
proof is `tests/AutoGenBatch/V1Golden.cs` (V1-1: 40 fingerprints recorded from main before the first hook; V1-2: no `ResortPlan`, no v2
layers in Runs mode). A re-record is allowed only when main's v1 planner changed on purpose (the merge commit says so).

## Stage order and probe slots

Resort mode inserts `Terrain, PodRoute, Pods, Lifts` between `Minimax` and `Route` in the `Stage` enum. `Minimax` hands over to `Terrain`
(Resort) or `EnterRoute()` (Runs); `Lifts` hands over to `EnterRoute()`. `StageSlot(stage)` keeps the v1 numbering for
`AutoPlanResult.StageMs` / `Progress` (the new stages have no v1 slot, so `StageCount` is still 16). The Resort probe slots are
`ResortPlan.StageUnits / StageMs / StageMaxMs[ResortStageCount = 6]`: Terrain, PodRoute, Pods, Lifts, Route (band passes), Flow
(`FinishResort`). The planner fills them as it runs and copies them into the plan after `FinishResort`.

## Hooks (all A0, one line each in the v1 files)

| Hook | Called from | Defined in | Stub behavior | Owner of the body |
|---|---|---|---|---|
| `UnitTerrain` | `Unit()` dispatch | `.Terrain.cs` | `Next(PodRoute)` | A3 |
| `UnitPodRoute`, `UnitPods` | dispatch | `.Pods.cs` | `Next(Pods)`, `Next(Lifts)` | A3 |
| `UnitLifts` | dispatch | `.Lifts.cs` | `EnterRoute()` | A4 |
| `IsResortSource(c)` | `StartPassRows` (per cell, `m_Pass` = the pass) | `.Fans.cs` | the v1 base-band rule | A5 (selected lifts' bottom pads) |
| `FanSeeds()` | top of `UnitSeeds` | `.Fans.cs` | `false` = run the v1 seeding | A5 (returns true when it handled the unit; owns `m_Cursor2`, calls `Next(Candidates)`, repeats the v1 case 0 bookkeeping) |
| `FanCaps()` | end of `BeginSelect` | `.Fans.cs` | nothing | A5 |
| `FinishResort(res)` | end of `UnitFinish`, after `Warn` | `.Flow.cs` | an empty `ResortPlan` | A6 (also replaces `Clusters` by the pod clusters and adds the three warnings) |

Select.cs bodies behind `FanSeeds` / `FanCaps` (and the `March` widths) belong to A5; `UnitFinish` itself is not edited again.

## Knobs and Reselect

`AutoPlanKnobs.Lifts` (0 = Auto, 1-9) restarts at `Lifts` (Resort only; Runs ignores it). `AutoPlanKnobs.Home` changes only verdicts: it
falls into the existing "Finish" restart, and `FinishResort` recomputes the flow (there is no separate Flow stage: Flow is a hook
inside Finish). Seed = Seeds, Mix / Density / RunCap = Select, Kinds = Finish, as v1. `Reselect` copies Mode / Pinned / Access /
TreeLineY / Cancel into the restart input.

## Threads

`AutoPlanCancel` is polled once per unit at the top of the `Step` loop; a canceled `Step` returns true with `Result.Canceled` set and
the scan stops (Reselect refuses until the next scan completes; a fresh `Begin` / `Run` clears everything). `ProgressVolatile` is written
after every unit into a volatile int (per-mille). `AutoPlanResult.Snapshot()` copies lists and arrays, shares the `AutoRun` objects and
deep-copies the `ResortPlan` (`ResortPlan.Copy`). It lives in `SkiRunAutoPlanner.cs` (written by A0 because A2 does not own that file).

## Fingerprint

`AutoPlanResult.Fingerprint` folds `ResortPlan.Fingerprint` in only when it is non-zero, so while the hooks are stubs the Resort
fingerprint equals the Runs one (a `PlanTypes` check named "A0 stub"; the task that first writes a non-zero plan fingerprint removes it).

## Layers

Resort mode only (grown in `Allocate`): `m_OS` float, `m_Band` / `m_PadOf` / `m_Ridge` byte, `m_PodOf` ushort (9 B per cell).
`SkiRunAutoPlanner.ResortLayerBytes` reports what is held (the P-4 memory gate sums it).

## Player text

A0 added every key (LocaleEN, the hubText.ts mirror, `locale/en-US.json`). Hub: `SkiResort.Hub.QUICK_LIFTS_AUTO / LIFTS_N / LIFTS_TIP /
HOME_AUTO / HOME_GREEN / HOME_BLUE / HOME_TIP / POD_TIP / LIFT_TIP`. Tooltip (also mirrored for the hub): `SkiResort.Tooltip.QUICK_VERDICT[Green|Blue|Black|DoubleBlack|Download]`,
`QUICK_CLASS[<TerrainBand name>]`, `QUICK_PLAN_LINE`, `QUICK_MIX_HONEST`, `QUICK_POD_LINE`, `QUICK_LIFT_LINE`, `QUICK_PROPOSAL_NOTE`,
`QUICK_AROUND_LIFT`, `QUICK_WARN[NoLiftSite|Download|DeadEnd]`. Labels: `SkiResort.Label.PLAN_LIFT`, `PLAN_POD`. `{TYPE}` is the existing
`SkiResort.Hub.LIFT_TYPE[<type id>]`.

## Sections per task

### A1 fixtures + host
Files (all under `tests/AutoGenBatch`, test host only; nothing in the mod changed): `Heightmap.cs` (16-bit gray PNG reader, window
snapshot, `HeightmapTests`), `V2Fixtures.cs` (region matrix, field-test 3 regions, Steep / Benched shape checks, the Resort-mode refusal
ladder = V1-4), `V2Metrics.cs` (every gate metric as a pure function returning a `Gate`; `V2MetricsTests` on hand-made plans),
`V2Tests.cs` (the `-- v2` matrix), `Terrain.cs` (`Steep(seed)`, `Benched(seed)` and the private `Terraced` remap only).

**Fixtures** live outside git in `%SKI_FIXTURES%` or `%LOCALAPPDATA%\ModularSkiResort\fixtures\<Name>\` (11: Mountain Village, Okemo,
JacksonHole, Whistler, Snowbird, Zermatt, Chamonix, Fjords, Highlands, Delta, Plains). They are copied from the research archive by
`docs/research/quickruns-v2/scripts/fixtures-copy.cjs` (size + SHA-256, idempotent; rebuild commands and attributions in
`fixtures-build.md`, same folder). A missing fixture is one `SKIP`, never a FAIL. `Heightmap.Snapshot` returns the same window
`SnapshotRect` asks for, rows flipped to the game's south-up lattice, `Terrain.Scale` / `Offset` unchanged, the sidecar offset in
`Offset.y`; the loader reads the two Mountain Village field corner heights 2143.82 / 1240.20 m (log 2144.0 / 1240.4).

**Running**: the default run adds `HeightmapTests`, `V2FixtureTests` (field regions equal the logged areas, Steep seed 1 and Benched
shape, V1-4 ladder in Resort mode) and `V2MetricsTests`: about +0.5 s on a 22 s run, no real file. `-- v2` adds the matrix: 57 regions
(synthetic Alpine x4, Benched x2, Steep x2; per real fixture 1 / 4 / 9 km2 at the base point and 4 km2 on the steepest window; Mountain
Village: the four field regions, the 9 km2 window, base 1 / 4 / 9, steepest), about 70 s. Each region runs Runs mode and Resort mode;
Resort results get the shared `Invariants` (V1-3) and every metric. Output: `FAIL` (HARD miss), `REPORT <ID> <region>: ... [ok|MISS tag]`,
`SKIP`, a `ROW` table and a per-ID tally (`GATE`).

**Arming a gate** (the owner task of A3-A6): add its IDs to `V2Hard` in `V2Tests.cs`. Armed now: V1-2, V1-3, V1-4, D-1, D-2, D-3,
P-1..P-4. `V2AlwaysSoft` (F-8, H-5, H-6, S-6 bottoms) never fails; `V2SoftOnSteeper` (F-1..F-7, F-9, F-10, S-5) is REPORT on Steep, on
Mountain Village and on any region with mean grade over 45 % (the gates table's "HARD under 45 % mean"). F-9 is HARD on Alpine and on the
Mountain Village field regions (the gates table names them).

**Deviations from the spec** (none changes a bound):
1. Metric inputs: besides `AutoPlanResult` + `ResortPlan` the functions take the region area (km2), the density knob, the pinned list
   (S-7) and, for F-10 / H-2, a re-rate delegate (the host passes `SkiRunRater.Rate` over the snapshot, tests pass `run => run.Band`).
2. A metric with nothing to measure (no pad, no lift, no pod with runs) returns `n/a` with pass = false: a stubbed planner cannot turn a
   HARD gate green by being empty. Not-yet-armed gates print REPORT, so nothing fails today.
3. S-2 is recomputed from the stored `BandShare` (80th percentile band over the listed shares), because the host cannot see the cells.
4. H-2 / H-5 host search: start runs = tops within 100 m of the lift Top; run A continues into run B when A's bottom is inside B's outline or
   within 25 m of it; the target is a base pad or the lift's own pad (disc radius max(InscribedM, sqrt(area / pi)) + 25 m) or the lift's
   Bottom within 25 m. Lifts, cat tracks and obstacles are not edges in the host search (stage A has no earthworks).
5. F-2 / F-3 count the runs with `RunLift == lift`; F-4 counts a run top inside, or within 25 m of, another run's outline.
6. P-4 sums the exact v2 layers (`ResortLayerBytes`) plus 49 B per snapshot cell for the v1 layers (estimate, the v1 arrays are private).
7. P-1: a miss is measured once more and the smaller figure counts (single runs showed 5-10 ms outliers on Delta / Snowbird that did not
   repeat); on the real heightmaps it stays REPORT because the v1 gate never covered real terrain; synthetic cases are HARD.
8. D-4 / D-5 are SKIP here (A2's `WorkerTests` owns them); P-5 is SKIP (no tower-plan counter until A4); the 2.6 realism report is SKIP
   (needs the real lift / run layouts); S-7 only has the unit test (the matrix plans without pinned lifts).
9. The 3.5 km Mountain Village window is over the 9 km2 cap as a region: it runs as the 3 km square at (4895, -1528).
10. The "steep-compact" class of S-5 / F-7 is not a fixture attribute: Steep and Mountain Village are SOFT instead.
11. `Steep(seed)`: vertical 1,190 / 1,302 m, mean grade (28 m baseline) 59 / 64 %, 9.4 / 13.0 % of cells over 100 % (spec: about 15 %), two
    benches 8.5 / 6.2 ha and 11.2 / 4.5 ha at 0.35 and 0.6 of the vertical (spec 6-10; the check accepts 4-12). `Benched(seed)`: three
    benches at 0.22 / 0.42 / 0.62 of the vertical, 34 / 23 / 14 ha (seed 1; the top one is cut into the rim face and stays under the spec's
    20 ha; the check wants two of 20-60 ha and three of 9 ha or more).
12. Base points come from lat / lon with the equirectangular ground-meter mapping of the builder; the elevations under them equal the
    village elevations of [E] 3.1 within 1 m (printed as `REPORT base point` in the matrix).

**Findings for the owner tasks** (not tuned): in Resort mode with the A0 stubs every region plans exactly the v1 runs; the v1 kept
share (runs / candidates) is 1-12 % on nearly every real region (the exceptions are tiny candidate counts: Zermatt 1 km2 7 of 23, Mountain
Village steepest 1 of 2) and 4-9 % on the Mountain Village field regions (field 1: 33 of 482 here, the field log 26 of 478), so F-9 starts
far below its 0.40 bound. Plan time CoreCLR: 0.2-1.3 s, longest 9 km2 region Delta 1.32 s
(P-3 bound 1.5 s); the v2 layers are 0.9 / 3.2 / 7.0 MB at 1 / 4 / 9 km2.

### A2 worker
(not started)

### A3 terrain + pods

Files: `.Terrain.cs` (stage Terrain), `.Pods.cs` (stages PodRoute, Pods), `tests/AutoGenBatch/TerrainTests.cs`. Kernels.cs is untouched: the
labeled Dijkstra is `DijkstraQueue` + `StepCost` inline in PodRoute, the labeling reuses `Root` (union-find) and `Chamfer`.

- **Terrain** (row chunks; m_Cursor2 phases 0-9): m_OS = 3x3 box mean of m_O over open cells (segmentation only), m_Band, the 3x3
  smoothed height (scratch m_Dist[1]); ridges = usable cells with no D8 inflow (m_Flow still the own cell's area) that are a strict
  maximum of the smoothed height across one of the 4 axes; pad candidates (usable, m_OS < 0.25) unioned with their earlier 8-neighbors
  in m_Order (the smaller root wins, so every parent is an earlier cell and one ascending pass flattens them into compact labels);
  components of at least 0.2 ha get the inscribed-distance chamfer (scratch m_Dist[2], seeds -half a texel, the DHard convention); pads
  = inscribed >= 9 m, at most 255 (largest kept), ids in smallest-cell order; m_PadOf.
- **Pad = a graded site** (DECISIONS 447): `PlanPad.GradeM3 / GradeMaxM` (added to Plan.cs) = balanced cut (= fill) and the deepest
  cut or fill to level the cheapest station's 16 x 24 m lot at the station spot (long side along x or z, the smaller). A screening
  number; stage B grades with the cat track kernel. Station capability stays `InscribedM >= StationHalfDiagonalM[t]`.
- **PodRoute**: sources = cells of pads with InscribedM >= 14.4 m (and m_O < 0.70, out of the edge strip), label = the pad, cost =
  StepCost(floor 0.08, ceiling = hard 0.70); the label rides each relaxation (m_PodOf scratch, cost m_Dist[0]). A multi-source
  Dijkstra's nearest source is the first pad on the least-cost descent.
- **Pods** (phases 0-10): Down(P) = the label of P's lowest outside 8-neighbor carrying another label; the 80 m merge in ascending
  MinY, root = the lower pad; pod components = 4-connected cells of one surviving pad's catchment, minus ridge cells, minus the lower
  pad's own cells; borders (ridge cells touching two components of one pad, diagonal touches) as sorted runs into an adjacency list;
  small components (under 2 ha or 80 m) join the neighbor with the longest summed border (ties: smaller root) or drop; statistics per
  group; final pods need a top cell (DHard >= 35 m) at least 80 m above their lowest cell; cap round(4 x AreaScale); sort (LowerPad.MinY,
  top cell); m_PodOf = pod id (MUnreached = none).
- **Plan hand-over**: `TerrainPlan(plan)` copies pads and pods (fresh arrays; BandShare cloned) and is called from the FinishResort
  stub (Flow.cs, one line A6 keeps). Pads / pods are computed before Lifts, so every Reselect except Density-with-runs keeps them.
- **Scratch contract for A4+**: after Pods, m_Order, m_Dist[0..2] hold nothing usable (TopRows / StartPassRows rewrite them);
  m_PadOf, m_PodOf, m_Band, m_Ridge, m_OS are final and stay valid across Reselect.
- **Public for the host**: `BandOfGrade`, `PrimaryClassOf`, `MaxPodsOf`, `PodGrades` (S-2 oracle), `BufferBytes` (P-4), `PadCount`,
  `PodCount`, the constants.
- **Measured** (CoreCLR Release, Step(4 ms), unit max best of 3): 9 km2 fBm: Terrain 17 ms in 122 units (max 0.9), PodRoute 114 ms
  in 119 (max 1.45), Pods 20 ms in 142 (max 0.6); planner buffers 51.7 MB (v2 layers 6.7 MB). Alpine 3.9 km2: 9 / 30 / 5 ms.
- **Deviations from the spec** (also in the private handover note): summits are not computed (no stage A consumer; PickSummit's seeded
  jitter would make the plan depend on the Seed knob, which restarts after Pods); UpperPad counts pads the pod's cells are OR touch
  (a bench at the pod's top is its own source, never inside); the lower pad's own cells are not pod cells (else two faces draining
  onto one pad always join through it and the ridge split never fires); 4-connected pod components; a pod without a 35 m-inside top
  cell drops; Down(P) as above.
- **Tests**: `Terrain` suite: two terraces 60 m apart = 2 pads / 1 pod (merge), 110 m = 2 pods (bench, UpperPad), ridge = 1 pad /
  2 pods, a 35 % plane = no pad; invariants (S-1, S-2 recomputed from the cells, shapes) on Alpine 1-3, Alpine lower, the terraces;
  S-3; D-1 / D-2 / D-3 on the pads and pods; P-1, P-4. `TerrainCase(id, input, expectBench, expectPods)` is the per-fixture check +
  REPORT for A1's `-- v2` matrix (real fixtures SKIP here until A1's loader merges).

### A4 lifts + overlay
(not started)

### A5 run fans
(not started)

### A6 way home
(not started)

### A7 hub + tooltips
(not started)
