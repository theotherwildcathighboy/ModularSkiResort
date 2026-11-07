# SkiRunDrawFeedbackSystem: slope feedback while a run is drawn, the terrain read, the overlay job and the validation watch

This system gives slope feedback while the player draws or edits a ski run with the vanilla area tool. It draws a translucent heat-map of per-cell slope bands inside the Temp polygon. It also draws a white ring on the crux cell (the pitch that sets the rating). It publishes a Readout (band, crux
grade, steepest avoidable patch, mean grade, drop, length, connected, new/edit, gate in range, fee inputs) that `SkiRunTooltipSystem` shows at the mouse.
The rating is `SkiRunRater` (shared with the hub's rating cache), and the classifier owns the rules. This system keeps the heights-ready
check, the ping-pong grids, the step hysteresis and everything after Classify. It only publishes. Engine citations are `Game.X/File.cs:line`
in the 1.6.0f1 decompile. "1.6.2 :N" notes a line shift in 1.6.2f1.

## Phase

`UpdateBefore` `OverlayRenderSystem` in Rendering (SystemOrder.cs:687). MainLoop runs ToolSystem -> ModificationSystem -> UIUpdate ->
Rendering, paused or not (SystemOrder.cs:58, 60, 67, 68). The game builds the Temp area in Modification1 (GenerateAreasSystem, :98) and
triangulates it in Modification2B (GeometrySystem.cs:752, 780). So by Rendering it is the frame's final polygon. `OverlayRenderSystem.OnUpdate`
consumes and clears the overlay lists we write, later in the same phase (OverlayRenderSystem.cs:781, 790). So we draw every
frame.

## Source of truth: the Temp Area entity

The source of truth is never the tool's control points or `tool.prefab`.

- The Temp's PrefabRef is the original run's prefab when editing (GenerateAreasSystem.cs:172-175 and, for a new entity, :258-260). This holds whatever Surface
  prefab the player selected (the tool snaps to any area of the same AreaType, AreaToolSystem.cs:320-322). So the run test is on the
  Temp's own PrefabRef (`ownership.IsRunPrefab`).
- A 1-node Temp (`TempFlags.Create`) exists whenever the tool is active over terrain (AreaToolSystem.cs:1817-1893). So the system requires >= 2 nodes.
  Hovering an existing run yields a full Complete Modify copy with `m_Original` = run (AreaToolSystem.cs:1590-1595). That copy gets feedback too
  ("what is this run rated").
- GenerateAreasSystem snaps the nodes to terrain and strips the closing duplicate with Complete set (GenerateAreasSystem.cs:282-316).
  GeometrySystem triangulates an open polygon of >= 3 nodes as if closed (GeometrySystem.cs:99-126). The classifier's ring mode does the same.
- Last emit's unrecycled Temps still carry Deleted during Rendering (excluded by the query). Dragging a shared vertex yields several
  Temps, and the system takes the first eligible one with the most nodes. The Temp persists on still frames (AreaToolSystem.cs:3700-3703), so a cache
  keyed on (entity, node copy) is enough.
- Gate in range: runs carry no vanilla Owner. The link is our `SkiRunBinding`.
  - For an edited run, the check is `ownership.IsBound(Temp.m_Original)` (Temps never carry SkiRunBinding, GenerateAreasSystem.cs:256-273).
  - For a new run, the check is `TryNearestGate` on the Temp's center. It is false while ownership cannot bind a new run this load
    (`ownership.BindsNewRuns`), so the "will stay unbound" warning shows.
  - The feedback gate is `ownership.PrefabSetsReady`, not IsArmed: an ownership exception disables binding, but nothing here needs binding.

## The lattice

Grade comes from height differences on a lattice, never from triangle planes. Area triangles are not terrain-conformed between nodes
(GeometrySystem.cs:101-113). The 3-argument SampleHeight normal is in raw heightmap units (TerrainUtils.cs:139-157). The lattice is
anchored to the world heightmap: texel center i sits at world `i / scale.x - offset.x` (TerrainUtils.cs:35-38). The offset includes the
half-texel shift (TerrainSystem.cs:2621-2622). So origin = `-hd.offset.xz` and texel = `1 / hd.scale.x` = 3.5 m on a 4096-wide heightmap
(TerrainSystem.cs:2618-2620). Step = an integer multiple m of the texel (`SkiSlopeClassifier.StepMultiple`). Cells away from the moving
edge keep bit-identical values while the player draws (no shimmer), and height reuse relies on it.

## Terrain read

The system calls `GetHeightData(waitForPending: false)` (TerrainSystem.cs:2603) fresh on every recompute. It never caches the result across frames
(`EnsureCPUHeights` can dispose and reallocate the array, TerrainSystem.cs:3538-3552). Heights use the 2-argument
`TerrainUtils.SampleHeight` (TerrainUtils.cs:82-137, plain bilinear), a pure function of the CPU heights and the point. A synchronous
main-thread read needs no `AddCPUHeightReader`. `m_CPUHeights` is written only on the main thread after readers complete. Vanilla
precedent: LandValueTooltipSystem.cs:252-265, SelectionToolSystem.cs:1151. The Unity-side guarantees are unverified. A heightmap narrower
than 4096 (including the (2,2,2) pre-readback fallback, TerrainSystem.cs:2618) skips the rating with a status note.

## Recompute policy

Mono measured 4-5 ms per recompute, about 5-8x CoreCLR.

- Duty-cycle throttle: a recompute caused only by node movement may not start sooner than `clamp(ThrottleFactor x LastRecomputeMs, 0,
  MaxThrottleMs)` after the start of the previous one (wall clock), on top of `MinRecomputeFrames`. Node-count / Complete / Temp-entity
  changes and a verify mismatch (`m_ForceFullResample`) stay immediate. A deferred move recompute still fires once the time passes,
  even if the cursor stopped (the moved test compares against the last recompute's node snapshot).
- Height reuse with two ping-pong `SlopeGrid`s: Plan poisons every Need height with NaN, so reuse copies from the previous grid.
  - A Need node copies its height from the last fully filled and rated grid instead of calling SampleHeight. This needs the same world lattice index (I0 + i, J0 + j) to be a Need node there, with a finite height.
  - Guards: same Step and lattice Origin, and the source's heights were last verified less than
    `Tuning.HeartbeatSeconds` ago (`m_GoodVerifiedAt`). A full resample stamps its start. A reuse recompute inherits it. A complete
    `VerifyStep` pass re-stamps it.
  - Planning always goes into the grid that is not the last good one. So the last rated polygon survives
    failed recomputes and Invalidate (the dump needs it after the tool is closed).
- Heartbeat verify instead of a periodic resample: on hold frames, `VerifyStep` acts once `m_GoodGrid`'s heights are `HeartbeatSeconds` (2 s, wall clock) old. It re-samples up to `VerifyNodesPerFrame` of its Need nodes per frame and compares bit-exact. A mismatch forces a full
  resample. A complete pass re-stamps `m_GoodVerifiedAt` with the pass's start. There is no terrain-version signal to use. The only
  player height writer is the terraform brush (`TerrainSystem.ApplyBrush`, TerrainSystem.cs:3942-3953), which cannot run while the area
  tool is active (ToolSystem.cs:84-98). But the simulation still re-shapes terrain under lots, stamps, pillars and nets
  (TerrainSystem.cs:2728-2853). The ready-update lists are private (GroundHeightSystem.cs:1015-1021), and reflection is banned. Bit-exact is
  sound because a stored height is a SampleHeight result of the same world point or a copy of one.
- JIT pre-warm at arm time (`PrewarmJit`) runs one Plan + Classify + strip build of synthetic geometry, plus
  `IJobExtensions.EarlyJobInit<StripDrawJob>()`. The first Draw of the process still pays the first-call JIT of the job schedule. The system logs it
  once as `FirstDrawMs` and keeps it out of `PeakDrawMs` and the slow-draw WARN.
- Zero managed allocation in steady state (reused node arrays, two SlopeGrids + one SlopeResult that grow and never shrink, a fixed-size
  strip stage + a persistent NativeArray, one memcpy). The fill, strip build and capture writer walk Plan's per-row Need / Mask run
  lists, so they are O(Need) / O(inside) even for U- or C-shaped runs.

## Overlay

A Burst IJob (`StripDrawJob`) writes the strips and the crux ring into the `OverlayRenderSystem.Buffer`. It follows
`AreaBorderRenderSystem`'s pattern (job :40-41, Buffer field without attribute :97, GetBuffer :704, schedule :705, AddBufferWriter :707).

- `GetBuffer(out dependencies)` returns `m_BufferWriters` (OverlayRenderSystem.cs:603-634). `AddBufferWriter` folds a job into it
  (:636-639). OnUpdate completes and resets it before uploading and clearing the lists (:740-741, :774-790). So every registered writer
  finishes by the end of the same frame's Rendering phase.
- We combine only the overlay dependencies. StripDrawJob reads no ECS data, so assigning `base.Dependency` would make later writers of
  the Temp-area component types wait on a job that never reads them.
- `m_Strips` safety: the code writes it only in `PublishStrips` after `CompleteDraw()`. Every new job depends on our previous one (via
  `m_BufferWriters`), so completing the newest handle (`m_DrawHandle`) completes all of ours. OnStopRunning and OnDestroy complete it,
  and OnDestroy disposes `m_Strips` only after that.
- Strips are Projected primitives (`StyleFlags.Projected`, :99-104): a box spanning the whole terrain height range (FitBox :321-330 for
  curves, :300-310 for circles). So cost is count x footprint of GPU overdraw. Hence row run-length strips and `Tuning.MaxStrips`,
  enforced by row decimation before any truncation. DrawLine (:207) drops lines shorter than 0.01 m (:269). The border and node discs are
  vanilla's (AreaBorderRenderSystem.cs:363-427). The crux ring is DrawCircle (:138), where Projected replaces the center's y by the terrain
  range. It is styled like the vanilla range circles (fill = outline color with alpha 0, EffectRangeRenderSystem.cs:166-169). Unverified:
  - square strip ends at roundness (0,0),
  - the outline width being in meters,
  - whether the mod's Burst AOT compiles StripDrawJob (best-effort as in `SkiSnowInjectionSystem`, otherwise it runs as managed code on a
    worker thread).

## Validation verdict (`RunFeedback [validation]` log)

The game attaches errors to the Temp in two ways:

- (a) A notification icon (ValidationSystem.cs:1747-1785, listed in the Temp's IconElement buffer by IconCommandSystem.cs:293-336, prefab = the
  ToolErrorData of the ErrorType, priority Warning or Error).
- (b) The Error tag (:1657-1664). A Warning tags only the other area.

Both are in place by Rendering. The system reads the type as IconElement -> icon PrefabRef ->
`ToolErrorData.m_Error`, and the severity from `Icon.m_Priority` (>= Error blocks the click, ToolBaseSystem.cs:533-540). `WatchValidation`
reads it on recompute frames only. It logs once per change of the (error types, warning types, tags) set ("clear" on a return to clean,
a clean start says nothing). A cursor sliding along a neighbor can flip it every recompute. So lines go through a LogWindow
(`ValidationLogWindowS`), and a burst folds into one summary with the union of the types seen. The system writes the summary when the window passes,
also on hold frames, when the run leaves the cursor, and in OnDestroy. A throw logs one `!!!` and switches only the watch off.

Elsewhere: the click is refused when ANY entity carries Error (ToolBaseSystem.cs:313). A corner shared with a neighbor run gives that
neighbor its own Modify Temp (AreaToolSystem.cs:1542-1560), whose InvalidShape names only the neighbor (ValidationHelpers.cs:887-943). So the
eligible Temp can read clear while the click is refused. The watch therefore also reads every other Error carrier (`m_ErrorQuery`):
their error types, count and the lowest-index one's prefab join the change key and the line. A carrier with no error type of its own
carries the Error tag for the eligible Temp's error (ValidationSystem.cs:1657-1664, the icon stays on the Temp :1749-1752). The line then
names it ("the run it overlaps" when the Temp's own errors include OverlapExisting, else "the run the game marks with this error").

## Dump and capture

"Dump to log" is a dev tab entry or the Ctrl+Alt+G hotkey in `SkiGladeHotkeySystem`. The hotkey calls `DumpToLog()` from UIUpdate, so the dump sees the
live readout. "Dump to log" also writes the last rated polygon as a `.skislope` file under
`<Application.persistentDataPath>/ModsData/SkiResortMod/captures/`. tests/SlopeBatch replays that file as a regression fixture. These handlers
are the only file IO here. `ResetPublishedState()` clears every published field first in OnGameLoadingComplete, and the DUMP says
"rating: none since load" while `m_GoodGrid` is null.

## Scope

Game mode only (armed in OnGameLoadingComplete). Read-only on the ECS side: it never writes a component, never adds Updated to a run, and never
touches ownership. House contract: ARMED line per load, one-shot "!!!" catch that disables the feature until the next load.
