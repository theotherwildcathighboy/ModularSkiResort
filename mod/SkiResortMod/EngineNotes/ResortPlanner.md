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
(not started)

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
