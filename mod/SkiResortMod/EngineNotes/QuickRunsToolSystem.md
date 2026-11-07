# QuickRunsToolSystem: the Quick runs tool (scan, preview, batch build, refund, tree filler)

Experimental (DECISIONS 404-415). A prefab-less `ToolBaseSystem` on the skeleton of the cat track tool. It has point editing (`GradeEdit`),
an overlay, a mouse tooltip, emit -> Temp -> Apply, and one hotkey system. The planner is pure (`SkiRunAutoPlanner`,
SkiRunAutoPlanner.md). This tool feeds it a snapshot and turns its result into runs. Files:

- `QuickRunsToolSystem.cs` (lifecycle, input, knobs, `HubSet`), `.Scan.cs`, `.Build.cs`, `.Trees.cs`, `.Draw.cs`
- `QuickRunsRules.cs` (pure, AutoGenBatch QuickRunsTests)
- `RunAreaEmit.cs` (shared)
- `QuickRunsHotkeySystem.cs`
- `QuickRunsTooltipSystem.cs`
- The hub side in `SkiResortHubUISystem.QuickRuns.cs` (SkiResortHubUISystem.md)

## Off switch and gating
- The setting `QuickRunsEnabled` ("Resort Planner (experimental)", Options > Modular Ski Resort > Runs) is OFF by default since
  DECISIONS 443. `OptionOn` reads it. `OptionOn` falls back to false when Mod.Settings is null. When the setting is off:
  - `TryActivate` refuses and logs.
  - The hotkey is inert.
  - The hub chip stays visible but disabled with `SkiResort.Hub.QUICK_REASON[Off]` ("Resort Planner is experimental and off. Its switch is in Options > Modular Ski Resort > Main > Runs."). The hotkey path only logs the same hint.

  Turning it off while the tool is open closes the tool on its next frame.
- `UnavailableReasonKey`: Off, then Disabled (an exception, until the next load), then NotArmed (no city loaded, run prefabs not
  resolved). Null = available.
- Phases: Region -> Scanning -> Preview -> Emitted (-> Retry -> Emitted) -> Applying -> Region. Right click steps back one thing at a
  time (drag, scan, preview, a corner edit, all corners, close). It never steps back a built batch or planted trees (DECISIONS 418).

## Shared emit
`RunAreaEmit` came out of `CatTrackGradeToolSystem` unchanged (behavior-preserving, every GradeBatch test unchanged). It has three members:

- `Emit` (the area `CreationDefinition` + nodes the run tool would write).
- `Ground` (terrain y of a ring node).
- `DeleteLikeBulldozer` (the bulldoze definition shape).

It holds no state: each tool keeps its own phases, queries and logs.

## Scan
- `TakeSnapshot` copies the texel rectangle of the region from the CPU heightmap (`heights[z * resolution.x + x]`, texel k at world
  `k / scale - offset`) plus a wet layer (water depth over 0.1 m) into one reused `HeightSnapshot`. `CollectObstacles` reads every
  live area. Our runs -> ExistingRun / CatTrack. Any other Surface -> Surface. A MapTile with `Game.Common.Native` -> Unowned
  (ValidationHelpers.cs:555-582).
- Double buffer: two `HeightSnapshot`s (`m_SnapBuf`). `TakeSnapshot` always writes the one the planner does NOT hold
  (`m_PlannerSnap`, set at each launch). `m_Snap` = the latest taken (Draw, Trees, the probes and `WorldUnchanged` read it). The code
  never rewrites the buffer of a running worker, nor the buffer that a later `Reselect` reads again. `CollectObstacles` builds a fresh
  list per call for the same reason. Each scan gets a fresh `AutoPlanInput` (`m_Input` = the last launched one).
- The tool always plans in Resort mode (`Mode = Resort`, DECISIONS 444). Runs mode is reachable from the test host only.
- `BeginScan` adds the plan context before the launch (main thread, `CollectPlanContext`, measured as `m_ContextMs`): pinned lifts,
  access points, the tree line (Worker section). Then the scan job runs (Worker section). The tooltip and the hub show
  `ProgressVolatile`.
- Every reader takes `m_Published` (Build, Draw, Trees' run holes, toggles, warnings, hover, `HasPreview`, the hub's `Result`), never
  `m_Planner.Result`.
- The code fingerprints both inputs (FNV over the floats). On Enter, `WorldUnchanged` re-reads them. A changed world scans again instead
  of building a stale plan (notice RESCANNED).
- Warnings: the flags of the planner, then these of the tool. Only our own results feed them. No other mod is read.
  - MoneyShort (fee > city money).
  - Renumber (default-named runs exist: the new runs renumber them, SkiRunCatalogSystem ordinals).
  - OtherResort / NoSign (the gate nearest each enabled run's ring center, the tether rule).
  - ChecksOff (`ToolSystem.ignoreErrors`, ToolSystem.cs:159: Anarchy).
- Mix / Density / Kinds re-select from the same scan (`Reselect`) when the field allows it and no job runs. Otherwise they re-scan. The
  code cancels a running job first (a canceled planner drops its field). Shuffle changes the seed.

## Worker (Resort Planner v2 A2; stage-a-spec.md section 5)
- `QuickRunsToolSystem.Worker.cs` follows the pattern of the tree compose job (Trees.cs: launch with a GC count, stale test, poll + cancel,
  `Task.Run`, collect with `IsFaulted` and the stale drop, the summary fields). The worker touches only the planner instance and its
  input. It uses no Entities, no Unity API, and no log off the main thread.
- States: Idle -> Running (worker, or main-thread slices of `ScanBudgetMs` with the fallback) -> collected on the main thread. Collect:
  - `IsFaulted` -> `Fail(e, "scan worker")` (`!!!` line, tool disabled until the next load, closed when collected in the frame update).
  - Stale (a cancel or a newer request bumped `m_ScanGen`) or `Canceled` -> dropped.
  - Else `m_Published = Result.Snapshot()` and the v1 epilogue (`FinishScan`).
- Cancel (`CancelScanJob`): `DropPreview` during Scanning (right click, region edit, close, load, a knob while scanning). The worker
  stops at its next unit (`AutoPlanCancel`, polled before every unit). A main-thread scan stops at once (`Step(0)` sees the request
  before any unit). Only one job runs at a time. A scan requested while a canceled job is still stopping is queued (`m_PendingInput`,
  START says "queued"). It launches when that job returns.
- Knobs (`RescanForKnobs`, the one SetKnobs line):
  - If no job is in flight, no scan is queued and the token is not requested: `Reselect` runs on the main thread. Then the Step loop
    resumes on the worker (Reselect only sets the restart stage).
  - Otherwise: cancel, then run a full scan with the new knobs.
- Phase machine: Build needs Preview (Build.cs). While Scanning, the hub refuses build / undo / plant / treesUndo / trees
  (`ScanBlocks`, one HubApply line). A canceled job may still be stopping in Region. Undo / Plant there are race-free (the worker
  reads only its own input, and the tree job reads `m_Published` and its own snapshot copy).
- Plan context (main thread, sorted, spec 4.7):
  - Pinned lifts = `LiftCensusSystem.CopyServeLifts` rows (order = `CopyRows`) with a chord sample (10 m) inside the region or within
    150 m of it.
  - Bottom / Top = the stub tips of the census stations (`StationAt`). NaN when not in the last build.
  - Key = FNV of the EntityRef of the row. TypeIndex = the type of the row.
  - Access = every live road edge in the net search tree within the bounds + 450 m, sampled about every 25 m along its curve.
    It also holds the Welcome Sign nearest the bounds center when within reach. The list is sorted by x then z, with duplicates removed.
  - Tree line = the current estimate of the tree tool unless stale or None, else NaN.
- Fallback: Options > Diagnostics "Resort Planner on the main thread" (`PlannerOnMainThread`, default off). The code reads it at each
  launch. It uses the v1 `Step(ScanBudgetMs)` slicing with the same planner and the same result (gate D-2).
- Logs:
  - START (+ pinned lifts, access points with their ms, worker / main thread, "queued").
  - DONE (+ worker ms, main ms over frames, worst frame, gc0, then pods / lifts / home verdicts / dead ends / run km from the ResortPlan).
  - CANCELED (every cancel of a running or queued scan, with the reason, ms so far and progress): always, not only with Diagnostics.
  - `!!!` on a fault.
  - The stale drop (`[probe] scan ... dropped`) and a queued launch: only with Measurement diagnostics.
- Host gates (AutoGenBatch `WorkerTests.cs`):
  - D-4 (Task.Run == calling thread in both modes, the same instance across threads, a Reselect resumed on a worker == a fresh run).
  - D-5 (cancel at unit 1 / 10 / 100 / last), with these checks:
    - Canceled.
    - No unit after the request.
    - Reselect refused.
    - The next fresh run equals the uncanceled one.
    - The latency of a cross-thread cancel.
  - ProgressVolatile monotonic across threads.
  - Snapshot() copy independence.
- Spec deviations (A2):
  - The snapshot buffers swap at launch, not at publish. The planner holds its buffer from launch on, so the swap must happen then.
  - CANCELED is logged always (spec: Diagnostics only).
  - The access collection is not sliced over frames (its ms is in the START line: slice it if the field shows it over 2 ms).
  - Only the nearest Welcome Sign is an access point (the sign set is private to SkiRunOwnershipSystem).
  - Two lines outside the reader lines in QuickRunsToolSystem.cs (SetKnobs -> `RescanForKnobs`, the HubApply `ScanBlocks` guard). The old
    SetKnobs called `m_Planner.Reselect` on the main thread, a race with the worker.

## Build
- Build refuses when the city cannot pay the preview total (`QuickRunsRules.CanPay`, unlimited money reads 2e9, PlayerMoney.kMaxMoney).
- Trees and run definitions never share a frame: a build with the tree filler on clears the tree preview and builds on the next frame.
- The build is staged. Smoothness wins over speed. A large area may take tens of seconds, like Parking Lot Tool and ParkManager.
  - The enabled candidates queue up. The tool builds them in chunks of `BuildChunkRuns` (4). It emits, validates and applies one chunk
    before the next starts on the frame after its apply (phase Retry).
  - Hub and tooltip show "Building runs N / M".
  - Right click or the hub Stop (`RequestStopBuild`) ends the build at the next chunk boundary.
  - Closing the tool deletes the chunk in flight (if not yet applied) and keeps the chunks already applied as ordinary runs.
  - Every chunk joins one batch, so Undo removes all of it.
  - Notices: BUILT, STOPPED, PARTIAL (a later chunk refused after earlier ones were built).
  - While building, every hub knob and Enter wait.
- One frame emits one chunk (`RunAreaEmit.Emit` in one ECB). The tool maps the Temps of the game back by geometry
  (`QuickRunsRules.MatchRing`), because the engine recycles Temp entities, so an entity never identifies a candidate. The tool refuses a
  Temp with `Error`. It refuses a candidate with no Temp after 30 frames.
- Apply is all-or-nothing per emission (ToolBaseSystem.cs:533-540). `AfterValidation` picks one of three:
  - Apply.
  - Retry (drop the refused, clear, re-emit the rest of the chunk once).
  - GiveUp (back to Preview with the refused runs red and left out. After built chunks: PARTIAL).
- The permanent runs bind and pay exactly like drawn runs (SkiRunOwnershipSystem: one charge at bind, `SkiRunBinding` FlagPaid +
  `m_PaidAmount`). 150 frames after the apply, the tool logs one bind line: runs bound, signs, paid, charged against the preview total.

## Undo and refund
Undo works from the hub's Undo button only. Right click never undoes a built batch (DECISIONS 418). Undo does three things:

1. It reads `SkiRunBinding` of each run first (`QuickRunsRules.RefundOf`: what was actually paid. An unbound run paid nothing).
2. It deletes the batch the bulldozer's way (`RunAreaEmit.DeleteLikeBulldozer`).
3. It pays back the sum with `PlayerMoney.Add`.

The code clears the batch list on load and when the tool closes. The bind check line survives a close. UNVERIFIED: an undo inside the
150-frame bind window (a run not bound yet refunds 0, and binds after its delete is queued).

## Tree filler
- The filler uses the compose path of the glade tool with a fill mask (DECISIONS 412). It calls `SkiGladeCompose.Compose` with
  `ComposeInput.Fill = GladeMask` (holes = our runs and the enabled candidates plus 3 m, ground = the snapshot, ceiling = the tree line).
  The layer is `LayerFiller`. The glade levels give the density. The caps are 4000 plant / 4000 remove (SkiGladeToolSystem.Tuning).
- Census (DECISIONS 415): filler members are Own, every other tree Existing. Owner-placed and burning trees stay fixed. From now on the
  glade census treats LayerFiller as wild (thinnable).
- Tree line: `SkiGladeTreeLine.Estimate` over the wild trees within 500 m (95 %, DECISIONS 413). The code clamps the value to
  [ground min, ground max + 10 m] (one step above the top = no tree line inside the region). The hub steps it by 10 / 50 m. The overlay
  draws the contour (`SnowLineRules.MarchingSquares`, 65 x 65 grid).
- Species: every TreeData prefab with wood > 0 that is not a placeholder, sorted, grouped by `SkiGladeSpecies`. The chips are Conifer
  (default), Deciduous, Palm, All trees, or one species.
- Tagging: this tool is the second `IGladeTagSource` of `SkiGladeTagSystem` (run Null, LayerFiller, variant 0). `OnTagged` records the
  planted batch and learns the skip slots (positions the game refused) per plan key.
- Plant is staged too. It works in slices of `FillerSliceTrees` (2000, under the glade caps) per apply. Each re-compose after an apply
  plants the next slice by itself until the compose reports complete. In "Planting trees N / M", N = planted by this Plant and
  M = `TreeToPlant` at the start, the shortfall of the whole slope, not of one slice. Right click or the hub Stop (shown in place of
  Plant) ends it after the slice in flight. A blocked apply or a refused re-compose ends it with that notice. Every slice joins the tree
  batch, so Undo trees removes all of it.
- Undo emits the thin definition for the batch (vanilla refunds Recent trees). Undo trees takes one apply frame.

## Overlay
The overlay draws candidates in the palette of the piste map (`SkiPisteMapSystem.PisteFill / PisteOutline / PisteOutlineWidth`,
`PisteMapRules.SolidRing / DashRing`). Natural runs are dashed, left-out runs are thin gray, and refused runs are red. A ring marks each
top cluster (hover: "beginner lift", "mid-station", "one lift here serves N runs": pods are kept and labeled, DECISIONS 409). The region
is white, amber near the 9 km2 cap, and red over it. The tree-line contour is green. The code caches the pieces when the preview changes.
Widths follow the camera zoom like the map. The piste map hides itself while this tool is open (`SkiPisteMapSystem.ToolHidesMap`).

## Tooltip
`QuickRunsTooltipSystem` (the pattern of the cat track tooltip, UpdateAfter TempCostTooltipSystem) shows these lines:

- The step prompt, always.
- Region size as a ProgressTooltip against the cap plus vertical.
- Scan progress.
- The refusal status.
- Totals (runs, the mix template, fee, skiers a day).
- Tree counts and the tree line.
- The progress line of the build or the planting (QUICK_BUILDING_PROGRESS / QUICK_PLANTING_PROGRESS, the same templates the hub uses).
- Up to 8 warnings.
- The line of the hovered cluster.

The site verdict (DECISIONS 443) is one line (`QUICK_SITE[Gentle|Steep|VerySteep|Beyond]`, "average N %") in the Region step and the preview.
The mean grade comes from two sources:

- `QuickRunsRules.EstimateMeanGrade` in the corner phase: about 300 samples inside the ring, central differences over a 20 m window,
  capped at 200 % per sample, recomputed on every corner edit.
- `AutoPlanResult.MeanGrade` after a scan: the mean of the 20 m cell grade of the planner over the open cells, with the same cap. The
  code sets it before the refusal ladder. It is logged as `mean grade` in `[scan] DONE`.

Tiers by whole percent: under 25 Gentle, 25-44 Steep, 45-57 Very steep, 58+ Beyond (QuickRunsRules.SiteTierOf).
Keys: `SkiResort.Tooltip.QUICK_*` (LocaleEN.cs).

## Hub
The code pushes `quickRuns` (QuickRunsState) while the panel observes it, when `HubRevision` (bumped on every visible change),
availability or activity moved. Every hub action is `quickRunsSet(key, value)` -> `HubSet` (mix, density, kind, shuffle, scan, build,
undo, trees, plant, treesUndo, treeLine, group, species, level, stop). C# clamps every value and logs each call. While a build or a
planting is in flight, it refuses every key but stop ("busy"). Progress fields: buildDone / buildTotal, planting, treesDone / treesTotal.

## Resort Planner hub side (task A7; `QuickRunsToolSystem.Plan.cs`, `QuickRunsPlanRules.cs`)
- **Toggles**: `m_OffPods` / `m_OffLifts` hold plan keys (`PlanPod.Key`, `PlanLift.Key`), so they survive a Reselect. A new region and
  `ResetAll` clear them. `RefreshEnabled` (Scan.cs, one added test: `&& !PlanLeftOut(r, i)`) leaves a run out in these cases:
  - Its pod is off.
  - The lift of its pod is off.
  - The lift whose corridor seeded it is off (`ResortPlan.RunPod / RunLift`).

  Nothing is re-planned. The totals, the fee, the overlay and Build follow `m_EnabledByIndex` as for a single left-out run. Both keys are
  no-ops without a Resort preview.
- **Picks** (UpdateInput, Preview), in this order:
  1. A click within 15 m of a lift chord (Bottom -> Top) toggles the lift.
  2. Else a click on a run is the v1 toggle.
  3. Else a click within 40 m of the TopCenter of a pod (the cluster ring) toggles the pod.

  Hover (tooltip) uses the same picks without the click.
- **Plan around this lift**: this works in the Region phase, with no corners, on a press within 30 m of a census station
  (`LiftCensusSystem.StationAt`) whose lift has chords. The region becomes the corridor polygon of `QuickRunsPlanRules.AroundRegion`.
  The polygon extends 150 m each side of the Douglas-Peucker simplified chord line. It extends 400 m past the bottom station along the
  fall line of the terrain at the bottom, or along the lift's own axis on flat ground. The half width shrinks by 15 % steps until the
  area fits 9 km2. Right click clears those corners at once. The click does NOT scan: the player presses Scan or Enter. The lift becomes
  pinned the usual way (`CollectPinned` takes every census lift with a chord within 150 m of the region). A station without a line yet
  logs `[around]`, and the click places a corner as before.
- **Knobs**: `lifts` / `home` are `HubApply` cases beside mix and density. They set `m_Knobs.Lifts / Home`, log through `SetKnobs`
  (`KnobsText` names them) and take the `RescanForKnobs` path that A2 wrote (Reselect on the cached field with no job running, else
  cancel + a full scan). The `ScanBlocks` guard that A2 added in HubApply is unchanged.
- **Tooltip** (`QuickRunsTooltipSystem`): with a plan, it shows the plan line (QUICK_PLAN_LINE), the honest mix, the proposal note and the
  site detail line. The pod or lift under the cursor gets one line (QUICK_POD_LINE / QUICK_LIFT_LINE), and the lift also gets its verdict.
  The empty Region step shows QUICK_AROUND_LIFT when the census has a station. Texts: the keys of A0 plus three that A7 added
  (`QUICK_SITE_DETAIL`, `QUICK_LIFT_ID_PINNED` = "Existing lift", `QUICK_POD_UNSERVED` = "no lift planned"). The hub mirrors them in
  hubText.ts.
- **Logs**:
  - `QuickRuns [toggle]: pod B OFF / lift P1 ON - N of M run(s) enabled, fee F`
  - `QuickRuns [around]: ...`
  - `QuickRuns [hub]: lifts=3 - lifts` / `pod=... - pod B off` through DrainHub.

## Probes (Measurement diagnostics on)
The `[probe]` lines are:

- Snapshot parity (64 points, snapshot vs the height read of the game).
- Per-stage scan cost (Mono).
- The emission (N runs in one frame, validated).
- The Temp -> candidate mapping.
- The bind line per run (bound, paid).
- The Anarchy flag at emit.
- The staged wall times: `[probe] build chunk K` (runs applied, built so far, ms wall and frames for that chunk) and
  `[probe] planting slice K` (planted so far, ms wall and frames since the last slice).

The end line of the build and the end line of the planting always give the total wall time (diagnostics off too). So the field test
reports the real number. UNVERIFIED: the chunk and slice sizes (4 runs, 2000 trees) are first guesses. Tune them from these lines.
