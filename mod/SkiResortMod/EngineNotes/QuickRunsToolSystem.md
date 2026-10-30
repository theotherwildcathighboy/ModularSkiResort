# QuickRunsToolSystem: the Quick runs tool (scan, preview, batch build, refund, tree filler)

Experimental (DECISIONS 404-415). A prefab-less `ToolBaseSystem` on the cat track tool's skeleton: point editing (`GradeEdit`), an
overlay, a mouse tooltip, emit -> Temp -> Apply, one hotkey system. The planner is pure (`SkiRunAutoPlanner`, SkiRunAutoPlanner.md);
this tool feeds it a snapshot and turns its result into runs. Files: `QuickRunsToolSystem.cs` (lifecycle, input, knobs, `HubSet`),
`.Scan.cs`, `.Build.cs`, `.Trees.cs`, `.Draw.cs`; `QuickRunsRules.cs` (pure, AutoGenBatch QuickRunsTests); `RunAreaEmit.cs` (shared);
`QuickRunsHotkeySystem.cs`; `QuickRunsTooltipSystem.cs`; the hub side in `SkiResortHubUISystem.QuickRuns.cs` (SkiResortHubUISystem.md).

## Off switch and gating
- Setting `QuickRunsEnabled` ("Resort Planner (experimental)", Options > Modular Ski Resort > Runs, default OFF since DECISIONS 443; `OptionOn` falls back to false when Mod.Settings is null). `OptionOn` reads it; off = `TryActivate` refuses
  and logs, the hotkey is inert, the hub chip stays visible but disabled with `SkiResort.Hub.QUICK_REASON[Off]` ("Resort Planner is experimental and off. Turn it on in Options > Modular Ski Resort > Runs."; the hotkey path only logs the same hint). Turning it off while
  the tool is open closes the tool on its next frame.
- `UnavailableReasonKey`: Off, then Disabled (an exception, until the next load), then NotArmed (no city loaded, run prefabs not
  resolved). Null = available.
- Phases: Region -> Scanning -> Preview -> Emitted (-> Retry -> Emitted) -> Applying -> Region. Right click steps back one thing at a
  time (drag, scan, preview, a corner edit, all corners, close; never a built batch or planted trees, DECISIONS 418).

## Shared emit
`RunAreaEmit` was lifted out of `CatTrackGradeToolSystem` unchanged (behavior-preserving, every GradeBatch test unchanged):
`Emit` (the area `CreationDefinition` + nodes the run tool would write), `Ground` (terrain y of a ring node) and `DeleteLikeBulldozer`
(the bulldoze definition shape). No state: each tool keeps its own phases, queries and logs.

## Scan
- `TakeSnapshot` copies the region's texel rectangle of the CPU heightmap (`heights[z * resolution.x + x]`, texel k at world
  `k / scale - offset`) plus a wet layer (water depth over 0.1 m) into one reused `HeightSnapshot`. `CollectObstacles` reads every
  live area: our runs -> ExistingRun / CatTrack, any other Surface -> Surface, a MapTile with `Game.Common.Native` -> Unowned
  (ValidationHelpers.cs:555-582).
- Double buffer: two `HeightSnapshot`s (`m_SnapBuf`). `TakeSnapshot` always writes the one the planner does NOT hold
  (`m_PlannerSnap`, set at each launch); `m_Snap` = the latest taken (Draw, Trees, the probes and `WorldUnchanged` read it). A running
  worker's buffer, and the buffer a later `Reselect` reads again, is never rewritten. `CollectObstacles` builds a fresh list per call
  for the same reason; each scan gets a fresh `AutoPlanInput` (`m_Input` = the last launched one).
- The tool always plans in Resort mode (`Mode = Resort`, DECISIONS 444); Runs mode is reachable from the test host only.
- `BeginScan` adds the plan context before the launch (main thread, `CollectPlanContext`, measured as `m_ContextMs`): pinned lifts,
  access points, the tree line (Worker section). Then the scan job runs (Worker section); the tooltip and the hub show
  `ProgressVolatile`.
- Every reader takes `m_Published` (Build, Draw, Trees' run holes, toggles, warnings, hover, `HasPreview`, the hub's `Result`), never
  `m_Planner.Result`.
- Both inputs are fingerprinted (FNV over the floats). On Enter `WorldUnchanged` re-reads them; a changed world scans again instead
  of building a stale plan (notice RESCANNED).
- Warnings: the planner's flags, then the tool's: MoneyShort (fee > city money), Renumber (default-named runs exist: the new runs
  renumber them, SkiRunCatalogSystem ordinals), OtherResort / NoSign (the gate nearest each enabled run's ring center, the tether rule),
  ChecksOff (`ToolSystem.ignoreErrors`, ToolSystem.cs:159: Anarchy). Only our own results feed them; no other mod is read.
- Mix / Density / Kinds re-select from the same scan (`Reselect`) when the field allows it and no job runs, else re-scan (a running
  job is canceled first: a canceled planner drops its field); Shuffle changes the seed.

## Worker (Resort Planner v2 A2; stage-a-spec.md section 5)
- `QuickRunsToolSystem.Worker.cs`, the tree compose job's pattern (Trees.cs: launch with a GC count, stale test, poll + cancel,
  `Task.Run`, collect with `IsFaulted` and the stale drop, the summary fields). The worker touches only the planner instance and its
  input; no Entities, no Unity API, no log off the main thread.
- States: Idle -> Running (worker; or main-thread slices of `ScanBudgetMs` with the fallback) -> collected on the main thread. Collect:
  `IsFaulted` -> `Fail(e, "scan worker")` (`!!!` line, tool disabled until the next load, closed when collected in the frame update);
  stale (a cancel or a newer request bumped `m_ScanGen`) or `Canceled` -> dropped; else `m_Published = Result.Snapshot()` and the v1
  epilogue (`FinishScan`).
- Cancel (`CancelScanJob`): `DropPreview` during Scanning (right click, region edit, close, load, a knob while scanning). The worker
  stops at its next unit (`AutoPlanCancel`, polled before every unit); a main-thread scan stops at once (`Step(0)` sees the request
  before any unit). One job at a time: a scan requested while a canceled job is still stopping is queued (`m_PendingInput`, START
  says "queued") and launches when that job returns.
- Knobs (`RescanForKnobs`, the one SetKnobs line): no job in flight, no queued scan, the token not requested -> `Reselect` on the main
  thread, then the Step loop resumes on the worker (Reselect only sets the restart stage). Otherwise cancel + a full scan with the new
  knobs.
- Phase machine: Build needs Preview (Build.cs); while Scanning the hub refuses build / undo / plant / treesUndo / trees
  (`ScanBlocks`, one HubApply line). A canceled job may still be stopping in Region: Undo / Plant there are race-free (the worker
  reads only its own input; the tree job reads `m_Published` and its own snapshot copy).
- Plan context (main thread, sorted, spec 4.7): pinned lifts = `LiftCensusSystem.CopyServeLifts` rows (order = `CopyRows`) with a
  chord sample (10 m) inside or within 150 m of the region; Bottom / Top = the census stations' stub tips (`StationAt`; NaN when
  not in the last build); Key = FNV of the row's EntityRef; TypeIndex = the row's type. Access = every live road edge in the net
  search tree within the bounds + 450 m sampled about every 25 m along its curve, plus the Welcome Sign nearest the bounds center when
  within reach; sorted by x then z, duplicates removed. Tree line = the tree tool's current estimate unless stale or None, else NaN.
- Fallback: Options > Diagnostics "Resort Planner on the main thread" (`PlannerOnMainThread`, default off), read at each launch: the
  v1 `Step(ScanBudgetMs)` slicing, same planner, same result (gate D-2).
- Logs: START (+ pinned lifts, access points with their ms, worker / main thread, "queued"), DONE (+ worker ms, main ms over frames,
  worst frame, gc0, then pods / lifts / home verdicts / dead ends / run km from the ResortPlan), CANCELED (every cancel of a running or
  queued scan, with the reason, ms so far and progress: always, not only with Diagnostics), `!!!` on a fault; the stale drop
  (`[probe] scan ... dropped`) and a queued launch only with Measurement diagnostics.
- Host gates (AutoGenBatch `WorkerTests.cs`): D-4 (Task.Run == calling thread in both modes, the same instance across threads, a
  Reselect resumed on a worker == a fresh run), D-5 (cancel at unit 1 / 10 / 100 / last: Canceled, no unit after the request,
  Reselect refused, the next fresh run equals the uncanceled one; a cross-thread cancel's latency), ProgressVolatile monotonic across
  threads, Snapshot() copy independence.
- Spec deviations (A2): the snapshot buffers swap at launch, not at publish (the planner holds its buffer from launch on, so the
  swap must happen then); CANCELED is logged always (spec: Diagnostics only); the access collection is not sliced over frames (its
  ms is in the START line: slice it if the field shows it over 2 ms); only the nearest Welcome Sign is an access point (the sign set
  is private to SkiRunOwnershipSystem); two lines outside the reader lines in QuickRunsToolSystem.cs (SetKnobs -> `RescanForKnobs`,
  the HubApply `ScanBlocks` guard): the old SetKnobs called `m_Planner.Reselect` on the main thread, a race with the worker.

## Build
- Refused when the city cannot pay the preview total (`QuickRunsRules.CanPay`; unlimited money reads 2e9, PlayerMoney.kMaxMoney).
- Trees and run definitions never share a frame: a build with the tree filler on clears the tree preview and builds on the next frame.
- Staged (smoothness over speed; a large area may take tens of seconds, like Parking Lot Tool and ParkManager): the enabled
  candidates queue up and are built in chunks of `BuildChunkRuns` (4), one chunk emitted, validated and applied before the next
  starts on the frame after its apply (phase Retry). Hub and tooltip show "Building runs N / M". Right click or the hub Stop
  (`RequestStopBuild`) ends the build at the next chunk boundary; closing the tool deletes the chunk in flight (if not yet applied)
  and keeps the chunks already applied as ordinary runs. Every chunk joins one batch, so Undo removes all of it. Notices: BUILT,
  STOPPED, PARTIAL (a later chunk refused after earlier ones were built). While building, every hub knob and Enter wait.
- One frame emits one chunk (`RunAreaEmit.Emit` in one ECB). The game's Temps are mapped back by geometry
  (`QuickRunsRules.MatchRing`): the engine recycles Temp entities, so an entity never identifies a candidate. A Temp with `Error` is
  refused; a candidate with no Temp after 30 frames is refused.
- Apply is all-or-nothing per emission (ToolBaseSystem.cs:533-540): `AfterValidation` picks Apply, Retry (drop the refused, clear,
  re-emit the chunk's rest once) or GiveUp (back to Preview with the refused runs red and left out; after built chunks, PARTIAL).
- The permanent runs bind and pay exactly like drawn runs (SkiRunOwnershipSystem: one charge at bind, `SkiRunBinding` FlagPaid +
  `m_PaidAmount`). 150 frames after the apply the tool logs one bind line: runs bound, signs, paid, charged against the preview total.

## Undo and refund
Undo (the hub's Undo button only; right click never undoes a built batch, DECISIONS 418) reads each run's `SkiRunBinding` first (`QuickRunsRules.RefundOf`:
what was actually paid; an unbound run paid nothing), deletes the batch the bulldozer's way (`RunAreaEmit.DeleteLikeBulldozer`) and
pays back the sum with `PlayerMoney.Add`. The batch list is cleared on load and when the tool closes; the bind check line survives a
close. UNVERIFIED: an undo inside the 150-frame bind window (a run not bound yet refunds 0, and binds after its delete is queued).

## Tree filler
- The glade tool's compose path with a fill mask (DECISIONS 412): `SkiGladeCompose.Compose` with `ComposeInput.Fill = GladeMask`
  (holes = our runs and the enabled candidates plus 3 m, ground = the snapshot, ceiling = the tree line), layer `LayerFiller`, the
  glade levels as density, caps 4000 plant / 4000 remove (SkiGladeToolSystem.Tuning).
- Census (DECISIONS 415): filler members are Own, every other tree Existing; owner-placed and burning trees stay fixed. The glade
  census treats LayerFiller as wild (thinnable) from now on.
- Tree line: `SkiGladeTreeLine.Estimate` over the wild trees within 500 m (95 %, DECISIONS 413), clamped to
  [ground min, ground max + 10 m] (one step above the top = no tree line inside the region); the hub steps it by 10 / 50 m; the overlay
  draws the contour (`SnowLineRules.MarchingSquares`, 65 x 65 grid).
- Species: every TreeData prefab with wood > 0 that is not a placeholder, sorted, grouped by `SkiGladeSpecies`; chips Conifer (default),
  Deciduous, Palm, All trees, or one species.
- Tagging: this tool is the second `IGladeTagSource` of `SkiGladeTagSystem` (run Null, LayerFiller, variant 0). `OnTagged` records the
  planted batch and learns the skip slots (positions the game refused) per plan key.
- Plant is staged too: slices of `FillerSliceTrees` (2000, under the glade caps) per apply, each re-compose after an apply planting the
  next slice by itself until the compose reports complete ("Planting trees N / M": planted by this Plant / `TreeToPlant` at the start,
  the whole slope's shortfall, not one slice). Right click or the hub Stop (shown in place of Plant) ends it after the slice in flight;
  a blocked apply or a refused re-compose ends it with that notice. Every slice joins the tree batch, so Undo trees removes all of it.
- Undo emits the thin definition for the batch (vanilla refunds Recent trees). Undo trees takes one apply frame.

## Overlay
Candidates in the piste map's palette (`SkiPisteMapSystem.PisteFill / PisteOutline / PisteOutlineWidth`, `PisteMapRules.SolidRing /
DashRing`): natural runs dashed, left-out runs thin gray, refused runs red; a ring on each top cluster (hover: "beginner lift",
"mid-station", "one lift here serves N runs": pods are kept and labeled, DECISIONS 409); the region white, amber near the 9 km2 cap,
red over it; the tree-line contour in green. Pieces are cached when the preview changes; widths follow the camera zoom like the map.
The piste map hides itself while this tool is open (`SkiPisteMapSystem.ToolHidesMap`).

## Tooltip
`QuickRunsTooltipSystem` (the cat track tooltip's pattern, UpdateAfter TempCostTooltipSystem): the step prompt always; region size as a
ProgressTooltip against the cap plus vertical; scan progress; the refusal status; totals (runs, the mix template, fee, skiers a day);
tree counts and the tree line; the build's or the planting's progress line (QUICK_BUILDING_PROGRESS / QUICK_PLANTING_PROGRESS,
the same templates the hub uses); up to 8 warnings; the hovered cluster's line.
The site verdict (DECISIONS 443): one line (`QUICK_SITE[Gentle|Steep|VerySteep|Beyond]`, "average N %") in the Region step and the preview. The mean grade is `QuickRunsRules.EstimateMeanGrade` in the corner phase (about 300 samples inside the ring, central differences over a 20 m window, capped at 200 % per sample, recomputed on every corner edit) and `AutoPlanResult.MeanGrade` after a scan (mean of the planner's 20 m cell grade over the open cells, same cap; set before the refusal ladder; logged as `mean grade` in `[scan] DONE`). Tiers by whole percent: under 25 Gentle, 25-44 Steep, 45-57 Very steep, 58+ Beyond (QuickRunsRules.SiteTierOf). Keys: `SkiResort.Tooltip.QUICK_*` (LocaleEN.cs).

## Hub
`quickRuns` (QuickRunsState) is pushed while the panel observes it, when `HubRevision` (bumped on every visible change), availability or
activity moved. Every hub action is `quickRunsSet(key, value)` -> `HubSet` (mix, density, kind, shuffle, scan, build, undo, trees,
plant, treesUndo, treeLine, group, species, level, stop); C# clamps every value and logs each call. While a build or a planting is in
flight every key but stop is refused ("busy"). Progress fields: buildDone / buildTotal, planting, treesDone / treesTotal.

## Resort Planner hub side (task A7; `QuickRunsToolSystem.Plan.cs`, `QuickRunsPlanRules.cs`)
- **Toggles**: `m_OffPods` / `m_OffLifts` hold plan keys (`PlanPod.Key`, `PlanLift.Key`), so they survive a Reselect; a new region and
  `ResetAll` clear them. `RefreshEnabled` (Scan.cs, one added test: `&& !PlanLeftOut(r, i)`) leaves a run out when its pod is off, its pod's
  lift is off, or the lift whose corridor seeded it is off (`ResortPlan.RunPod / RunLift`); nothing is re-planned and the totals, the fee,
  the overlay and Build follow `m_EnabledByIndex` as for a single left-out run. Both keys are no-ops without a Resort preview.
- **Picks** (UpdateInput, Preview): a click within 15 m of a lift chord (Bottom -> Top) toggles the lift; else a click on a run is the v1
  toggle; else a click within 40 m of a pod's TopCenter (the cluster ring) toggles the pod. Hover (tooltip): the same picks without the click.
- **Plan around this lift**: Region phase, no corners, a press within 30 m of a census station (`LiftCensusSystem.StationAt`) whose lift has
  chords: the region becomes the corridor polygon of `QuickRunsPlanRules.AroundRegion` (150 m each side of the Douglas-Peucker simplified
  chord line, 400 m past the bottom station along the terrain's fall line at the bottom, or the lift's own axis on flat ground; the half
  width shrinks by 15 % steps until the area fits 9 km2). Right click clears those corners at once. It does NOT scan: the player presses
  Scan or Enter. The lift becomes pinned the usual way (`CollectPinned` takes every census lift with a chord within 150 m of the region).
  A station without a line yet logs `[around]` and the click places a corner as before.
- **Knobs**: `lifts` / `home` are `HubApply` cases beside mix and density: they set `m_Knobs.Lifts / Home`, log through `SetKnobs`
  (`KnobsText` names them) and take the `RescanForKnobs` path A2 wrote (Reselect on the cached field with no job running, else cancel + a
  full scan); the `ScanBlocks` guard A2 added in HubApply is unchanged.
- **Tooltip** (`QuickRunsTooltipSystem`): with a plan the plan line (QUICK_PLAN_LINE), the honest mix, the proposal note, the site detail
  line; the pod or lift under the cursor gets one line (QUICK_POD_LINE / QUICK_LIFT_LINE) and the lift its verdict; the empty Region step
  shows QUICK_AROUND_LIFT when the census has a station. Texts: A0's keys plus three A7 added (`QUICK_SITE_DETAIL`, `QUICK_LIFT_ID_PINNED` =
  "Existing lift", `QUICK_POD_UNSERVED` = "no lift planned"); the hub mirrors them in hubText.ts.
- **Logs**: `QuickRuns [toggle]: pod B OFF / lift P1 ON - N of M run(s) enabled, fee F`; `QuickRuns [around]: ...`;
  `QuickRuns [hub]: lifts=3 - lifts` / `pod=... - pod B off` through DrainHub.

## Probes (Measurement diagnostics on)
`[probe]` lines: snapshot parity (64 points, snapshot vs the game's height read), per-stage scan cost (Mono), the emission (N runs in
one frame, validated), the Temp -> candidate mapping, the bind line per run (bound, paid), the Anarchy flag at emit, and the staged
wall times: `[probe] build chunk K` (runs applied, built so far, ms wall and frames for that chunk) and `[probe] planting slice K`
(planted so far, ms wall and frames since the last slice). The build's end line and the planting's end line give the total wall time
always (diagnostics off too), so the field test reports the real number. UNVERIFIED: the chunk and slice sizes (4 runs, 2000 trees)
are first guesses; tune them from these lines.
