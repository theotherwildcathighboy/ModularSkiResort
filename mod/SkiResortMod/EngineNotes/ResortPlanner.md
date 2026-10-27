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
(not started)

### A4 lifts + overlay
(not started)

### A5 run fans
(not started)

### A6 way home
(not started)

### A7 hub + tooltips
(not started)
