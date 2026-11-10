# Resort Planner: index and seams

The Resort Planner is the runs-first planner (`RunsFirstPlanner.md`) and its realizer (`RunsFirstRealizer.md`). The tool runs both in one
scan job. The code names `QuickRuns*` stay. Design, build spec and gates live in the private docs (`docs/research/quickruns-v2/`). This
note is the tracked index. It says where each piece lives and what each task learned.

The first planner (v1, then v2 lift-first, the class `SkiRunAutoPlanner`) was deleted in port round R2-d (plan 7.7). Its compute, its
suites, its host modes and the main-thread fallback setting went. Its types and its height copy stay, because the realizer fills the
types and every reader uses them.

Where the shared pieces live (the planner files hold none of them, so a planner can go without a change to any reader):
- `Systems/PlanContract.cs`: the result and input types (`AutoPlanInput`, `AutoPlanKnobs`, `AutoPlanResult`, `AutoRun`, `AutoJunction`, `AutoCluster`, `AutoObstacle`, the `Auto*` enums).
- `Systems/PlanGeometry.cs`: the pure helpers (`PolygonArea`, `BBox`, ring and point distances, `SeedOf`, `SnapshotRect`, `ObstaclesClear`, `ClearanceOf`, `ScoreBandOf`, station lot half diagonals) and the shared constants (`V1MaxAreaM2`, `DefaultMin*`, `MaxLatticeCells`, `MeanGradeCap`, gaps and clearances, probe slot counts).
- `Systems/SkiRunAutoPlanner.Plan.cs`: the resort plan types (`ResortPlan`, `PlanLift`, `PlanPod`, `PlanPad`, `PlanEdge`, ...). The end of the file holds the last piece of the old class: the static forwarder `SkiRunAutoPlanner.SnapshotRect`. The frozen scorecard probes (`tests/AutoGenBatch/RunsFirstScoreTests.cs`) call it. Other code calls `PlanGeometry.SnapshotRect`.
- `Systems/SkiRunAutoPlanner.Snapshot.cs`: `HeightSnapshot` and `SnapshotSampler`. The tool, the cat track tool and GradeBatch read it.
- The two files keep their old names because many files cite them. A rename is a separate task.

## Runs-first in the tool (port round R2-a)

The tool does not call v2 any more. The scan runs the runs-first planner and the realizer. The v2 files were deleted in round R2-d.

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
- Sparse lowers the line cap and the serve cap and turns the fill off. Dense raises both caps. The host measured them (`rf-scan`).
  On the field test 5 regions, Sparse gives 23 % fewer runs and Dense 9 % more. On the owner set, Dense gives fewer runs than Normal.
  The options have no stronger lever for Dense. The line spacing is a planner constant.
- Memory: the live set of one scan at 9 km2 is 28 MB (Normal) to 72 MB (Dense), under the bound of 80 MB (`RF_SCAN_LIVE=1`).
- Kinds is read by the realizer only. A change uses the cached plan and runs the realizer again.
- Lifts, Mix, Shuffle and Way home have no effect. The tool logs one line and keeps the plan. The hub hides the four knobs since round R2-b.
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

## Test host (tests/AutoGenBatch)

- `Heightmap.cs`: the 16-bit gray PNG reader and the window snapshot (`HeightmapTests`).
- `V2Fixtures.cs`: the region set (synthetic Alpine, Benched and Steep, the real fixtures) and the fixture checks (the field-test 3 regions, the Steep and Benched shape).
- `V2Metrics.cs`: the plan gate metrics as pure functions over `AutoPlanResult` + `ResortPlan` (B-1, the S, F and H gates, the dead-end recount). `V2MetricsTests` checks them on hand-made plans. The v2 run gates (P-1 to P-4, D-1) were deleted with v2.
- `Terrain.cs`: the synthetic terrains.

**Fixtures** live outside git in `%SKI_FIXTURES%` or `%LOCALAPPDATA%\ModularSkiResort\fixtures\<Name>\` (11: Mountain Village, Okemo,
JacksonHole, Whistler, Snowbird, Zermatt, Chamonix, Fjords, Highlands, Delta, Plains). A script copies them from the research archive:
`docs/research/quickruns-v2/scripts/fixtures-copy.cjs` (size + SHA-256, idempotent. Rebuild commands and attributions are in
`fixtures-build.md`, same folder). A missing fixture is one `SKIP`, never a FAIL. `Heightmap.Snapshot` returns the same window
`SnapshotRect` asks for. It flips the rows to the game's south-up lattice, leaves `Terrain.Scale` / `Offset` unchanged, and puts the sidecar offset in
`Offset.y`. The loader reads the two Mountain Village field corner heights 2143.82 / 1240.20 m (log 2144.0 / 1240.4).

## Player text

The v2 task A0 added every key (LocaleEN, the hubText.ts mirror, `locale/en-US.json`). Round R2-b removed the keys of the four hidden knobs (Lifts, Mix, Shuffle, Way home). Hub: `SkiResort.Hub.QUICK_LIFTS_AUTO / LIFTS_N / LIFTS_TIP /
HOME_AUTO / HOME_GREEN / HOME_BLUE / HOME_TIP / POD_TIP / LIFT_TIP`. Tooltip (also mirrored for the hub): `SkiResort.Tooltip.QUICK_VERDICT[Green|Blue|Black|DoubleBlack|Download]`,
`QUICK_CLASS[<TerrainBand name>]`, `QUICK_PLAN_LINE`, `QUICK_MIX_HONEST`, `QUICK_POD_LINE`, `QUICK_LIFT_LINE`, `QUICK_PROPOSAL_NOTE`,
`QUICK_AROUND_LIFT`, `QUICK_WARN[NoLiftSite|Download|DeadEnd]`. Labels: `SkiResort.Label.PLAN_LIFT`, `PLAN_POD`. `{TYPE}` is the existing
`SkiResort.Hub.LIFT_TYPE[<type id>]`.

## Hub, overlay and tooltips

### A7 hub + tooltips (v2, still the hub's frame)
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
- The hub shows plan data only for a preview. It reads `ResortPlan` Pods / Lifts / RunPod / RunLift / Site / MountainMix / RunKm /
  DeadEnds as the types define them. The realizer fills them (`RunsFirstRealizer.md`).

### R2-b hub, overlay, labels and tooltips (runs-first plans)
Files: `SkiResortHubUISystem.QuickRuns.cs`, `QuickRunsToolSystem.Draw.cs`, `ResortPlanLabelSystem.cs`, `QuickRunsTooltipSystem.cs`,
`QuickRunsHotkeySystem.cs` (a comment), `LocaleEN.cs`, `bindings.ts`, `QuickRunsControls.tsx`, `hubText.ts`, the harness scenarios.
Details: `QuickRunsControls.tsx.md`, `SkiResortHubUISystem.md` (contract 42), `QuickRunsToolSystem.md` (Overlay, Tooltip).
- Hidden knobs: Lifts, Mix, Shuffle, Way home. The planner has no key for any knob, so no hotkey went.
- The counts are pistes only and the cat tracks have their own. `QuickTotals` counts a cat track as green, so the hub and the tooltip count again (`QuickRunsShown`).
- Labels: "Runs from P2: Intermediate" for a lift group and "Other runs" for the last group. "Cat track" marks the longest tracks. They are at least 150 m long. There are at most 24.
  A plan holds at most 15 lifts, 16 groups and 24 track labels. The cap of 64 labels is not reached. If it is, one WARN line says so.
- Caps found and changed:
  - Overlay pieces: 4096 became 12288, plus thinning of rings and a WARN line. Before, the first runs that filled the budget drew and the rest vanished.
  - Plan marks: half of the pieces, now with a WARN line. A plan needs about 450.
  - Labels: 64 stays. One WARN line replaces the silent drop (it was only counted under Measurement diagnostics).
- `ResortPlanMarksSystem` (the marks outside the planner) has no cap and draws a chord per unbuilt row, so 15 rows need no change.
- Existing lifts as blocked ground: the tool logs it (`[scan]`). The hub does not show a notice. The tool does not expose the count.
- Unserved areas (`AutoRun.Unserved`) are not shown yet. The overlay draws them like the others.
