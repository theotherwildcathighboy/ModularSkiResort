# CatTrackGradeToolSystem: how the cat track tool inputs, solves, guards, writes terrain and builds the run

The player clicks points up the mountain. The tool solves a grade-limited track through them (`CatTrackGrade.cs`, pure, tested in
GradeBatch) and previews it as an overlay. On Enter, the tool does two things in one action (2026-10-24). Before that, the player drew the
run separately with the vanilla area tool. The two things are:

1. It grades the corridor into the game's BASE heightmap once (`HeightmapWriter`).
2. It builds the cat-track run (`SkiRunCatTrackA`) on the graded flat top.

The terrain is the player's, and the game itself stores it in the save. The run is an ordinary area entity that binds and bills like a
drawn one. It is rated Green by construction (SkiRunRatingRules.CatTrackBand). The hub's Cat track tile opens the tool (ApplyLaunch, no
area-tool fallback, so every cat track is tool-built). Ctrl+Shift+K also opens it.

## Skeleton

Same as `LiftLineToolSystem`'s:

- A prefab-less `ToolBaseSystem` (`base.OnCreate` registers it, ToolBaseSystem.cs:292-316. `TrySetPrefab` is false, ToolSystem.cs:263-277).
- Activation is `ToolSystem.activeTool` (ToolSystem.cs:84-98), set from the hub tile, the hotkey (`CatTrackGradeHotkeySystem`) or the
  dev tab.
- The raycast is terrain only. The Cancel action (right click) is the only secondary input (Secondary Apply is off: both are the
  right mouse button by default).
- The overlay goes through `OverlayRenderSystem.Buffer` from `ToolUpdate`. Refusals show as a few-second notice in the tooltip. One
  "!!!" ERROR disables the tool until the next load.

## Input (GradeInput, pure)

- Left click adds a point. A click within `PickPathM` of the track inserts the point between its two neighbors. Press on a point and drag
  moves it.
- Right click and Esc both reach the tool's Cancel action while it is live (the field log of 2026-10-26b shows Esc presses logged as
  right clicks). Each Cancel goes one step back. The first case that applies wins:
  1. Leave Delete mode.
  2. Cancel a drag.
  3. Undo the last add / insert / move.
  4. Close the tool.

  A built cat track is never undone or deleted by them. Built tracks are off the grade undo stack (field test 2026-10-26b).
- The Build key grades. It is Enter, shared with the lift tool. `LiftLineHotkeySystem` keeps it live while either tool is open.
- Delete mode (`CatTrackDeleteMode`, default Ctrl+Shift+D, live only while the tool is open. Logic: `GradeDeleteInput`, pure): the tool
  tests the terrain hit point in outline against the cat track rings. It re-reads the rings every 30 frames and after a delete. The
  raycast stays Terrain only. The default tool cannot pick a Space area either (DefaultToolSystem.cs:796-805). The first click arms a track
  (red outline). A second click on it calls `DeleteTrack` (the hub row trash's path). A click off it disarms. Enter is refused in Delete
  mode.

## Solve on change, not per frame: quick on every edit, full debounced on a worker (hand-test note 20, DECISIONS 601 f)

These events mark the plan dirty: a point added / inserted / moved (on release) / undone, or a grading setting changed. The next frame runs
the QUICK solve (`SolveQuick`, main thread, about 1 to 3 ms at 30 points). The QUICK solve does these steps in order:

1. The path, the ground under it, the rect and the obstacles FIRST (search-tree walk, lift-line edges, the other runs' outlines).
2. The profile at the first inside-allowance step (pinned to the ground inside crossed runs and their fade band).
3. The overlay lines.

The plan line shows length / steepest part (no earthwork yet), and a second tooltip line says "Checking the ground." The FULL solve
(`GradeSolver.Run`, CatTrackGradeSolve.cs, pure) covers the raster, crossed-run band rating, guards, along-track crossing check and outline.
It takes 10 to 190 ms in game, growing with the rect, so it never runs per edit.

- Debounce: `DebounceMs` (150 ms) after the last edit, not while dragging. A newer edit cancels the job (`GradeCancel`, polled between
  stages) and drops its result. One job runs at a time (a canceled one finishes first).
- Worker input:
  - A `HeightSnapshot` of the rect plus the entered crossed runs plus the planner's lattice margin (SkiRunAutoPlanner.SnapshotRect, rows
    copied from `hd.heights`).
  - The wet layer of the rect (`FillWet`: SampleDepth at the GradeMap texel centers behind the node pre-filter).
  - A private `GradeObstacles` (CollectObstacles allocates a new one per solve, and the worker fills its WetTexels).

  The worker uses no Entities, no Unity API and no log. A read outside the snapshot (`SnapshotProbe.Misses`) or a rect over
  `MaxSnapshotTexels` (4 M) solves again inline on the engine's sampler (`TerrainUtils.SampleHeight`). That is the pre-split behavior. It
  causes a hitch, and the log shows mode "engine".
- Enter: the tool runs `SolveQuick` if dirty. Then it runs `FinishFullNow`. `FinishFullNow` waits for the job of this revision, or runs the
  same function inline if there is no job. The guards checked are those of the points on screen. `ApplyCore` still re-rasters from the
  base-map readback and re-checks everything.
- The inside allowance is no longer stepped one per frame (F4 is gone: the worker tries all steps). `m_CrossingPending` and the cached
  Before ratings per frame went with it.
- Gate: GradeBatch GR-31. It covers three points:
  - The full solve on the tool's crop equals the one-shot of before the split.
  - The crop is never read outside itself.
  - SolveQuick may not build a raster or run the full solve.
- Log: `Grade [plan]` and `Grade [crossing]` as before, when the full solve lands. One `Grade [cost]` line per full solve. Its fields are:
  - main thread ms (quick + snapshot + apply)
  - worker ms (raster, guards, outline)
  - edit-to-result ms
  - mode (worker / inline / engine)
  - the snapshot size
  - the snapshot-vs-engine height difference at 8 path points (should print 0.000 m)

  The dev tab shows the last one.

## Enter: emit the run, wait for the game, then grade + apply in one frame

1. Refuse when any of these is true. Nothing is emitted.
   - The plan is infeasible (the shortfall says how much more track).
   - A plan guard trips (lots, lift lines, water, or `RunShape`: the outline touches itself).
   - The run prefab is missing.
2. Emit ONE area creation definition, AreaToolSystem's shape for a new ownerless area (AreaToolSystem.cs:1572-1587, 1883-1886,
   1967-1968). It is `CreationDefinition{m_Prefab}` + the `Node` ring with the closing duplicate + `Updated`, through `ToolOutputBarrier`.
   GenerateAreasSystem.cs:282-291 marks the area Complete only with the closing duplicate. Node elevation float.MinValue snaps each node
   to the terrain (GenerateAreasSystem.cs:307-316). One emission is enough: Temps survive while applyMode stays None
   (ToolOutputSystem.cs:22-28).
3. Emitted phase: wait for our Temp area (Temp + Area + our PrefabRef, 30-frame timeout). If `GetAllowApply` is false, the result is
   REFUSED. `GetAllowApply` is false for any `Error` (ToolBaseSystem.cs:533-540), for example OverlapExisting against another ownerless
   Space area (ValidationHelpers.cs:569/582). On REFUSED, the tool sets applyMode Clear (ToolClearSystem deletes the Temp,
   ToolClearSystem.cs:101), destroys the definitions and grades nothing.
4. Otherwise, in the same frame, the tool runs the grade below (ApplyCore). If ApplyCore refuses, the tool sets Clear as in 3. If not, the
   tool sets `applyMode = Apply`. ApplyAreasSystem keeps the Temp entity as the permanent run (removes Temp, adds Applied / Created /
   Updated, ApplyAreasSystem.cs:204-208). So the entity seen in 3 is the run, and it goes into the undo entry. The run binds at
   ModificationEnd like a drawn run (SkiRunBindTriggerSystem) and pays its fee there. The frame after, the tool logs one BUILT line (or a
   warning if the run is not permanent).
5. The run's nodes follow the grade. The write's tail calls OnAreaChanged (TerrainSystem.cs:5096-5098 adds the bounds to
   GroundHeightSystem's update buffer). GroundHeightSystem re-snaps every float.MinValue area node there and marks the area Updated
   (GroundHeightSystem.cs:345-364, 762-790). UNVERIFIED in game for an area applied in the same frame.

## The grade (ApplyCore)

1. Read the BASE rect (exact heights, no net / lot overlay) and re-rasterize on it.
2. Re-check the guards.
3. Write, verified bit-exact by readback. A mismatch writes the pre-image back and refuses.
4. Delete the trees standing on the flat top (plain `Deleted` on trees with no `Owner`, the `SkiGladeCleanupSystem` idiom).
5. Push the pre-image for undo.
6. Tell the run catalog to re-rate (terrain epoch).
7. Log one line.

Grading is free, like the vanilla terrain tool. The run pays the cat-track fee (groomed pricing by plan area) when it binds.

## Undo

`GradeUndo` keeps the last 8 grades of the session (64 MB cap). Right click with no point undoes the last track built since the tool
opened (terrain AND run). The dev tab's "Undo last grade" reaches older ones. The system deletes the run with plain Deleted (Applied /
Created / Updated stripped, the SkiRunGateCascadeSystem idiom) when it still exists and is still a cat track. There is no fee refund (the
bulldozer refunds no run fee either). Undo writes the pre-image back only where the terrain still holds what the grade wrote (a later
terrain-tool stroke survives). Removed trees do not come back. Nothing survives a save / load (after a reload the vanilla terrain tool is
the only undo).

## Crossed runs: inside allowance and band check (DECISIONS 420)

Solve does these steps:

1. Build the plan at InsideMaxM 2 m.
2. Rate every entered crossed run with `SkiRunRater` (the catalog's origin and texel). The sampler reads the raster's Before or After
   where it covers the point, and the CPU heights elsewhere (`PlanSampler`).
3. Step the allowance down (1, 0.5, 0) while a band differs (`GradeRaster.ChooseInsideAllowance`). If no step matches, the guard
   `CrossedRunBand` trips.

One `Grade [crossing]` line names the allowance and the bands. ApplyCore re-rates on the base raster with the plan's params and refuses on
a change.

## Delete (DECISIONS 420)

The game-mode bulldozer raycasts only Lot and Surface areas (BulldozeToolSystem.cs:1497-1500. The filter is AreaUtils.GetTypeMask(m_Type),
RaycastJobs.cs:113, AreaUtils.cs:423-430). The cat track's type is forged to Space, so the bulldozer never hits one. Since DECISIONS 423b,
the delete is the trash button of the cat track's hub Runs row (two clicks, RunList.tsx TrashButton). The delete chain is:

1. The trash button triggers `deleteCatTrack` (contract 18).
2. SkiResortHubUISystem.OnUpdate queues and applies it (never in the binding callback, DECISIONS 418).
3. The public `DeleteTrack(entity, why)` runs `IsCatTrack` (our prefab, live, not Temp), then RunAreaEmit.DeleteLikeBulldozer.

The ground stays as graded, with no refund. The tool itself raycasts Terrain only: a click on an existing cat track starts a new track
there (branching), as before 2026-10-26.

## Crossing cost (F4, round-2 critique; superseded by the split solve above)

Until 2026-11-05, the plan stepped the inside allowance one step per frame while editing (a band change marked the plan pending). Enter
settled all steps in one call. The worker now tries all steps in one job. The `Grade [crossing]` line still lists each step with its ms
("2.0 m changed 131 ms; 1.0 m kept 128 ms").

## Guards (never under lifts, lots or water)

- Crossed runs: `CollectObstacles` reads every run of ours but cat tracks in the rect (Area + Node + PrefabRef, kind by
  SkiRunOwnershipSystem.KindOf) BEFORE the raster. Inside them the raster applies only the limited along-track correction (DECISIONS 420),
  and the trees inside them stay. A cat track over a groomed / snowmaking / night / natural run raises no game error. Their AreaTypes
  differ (cat track forged to Space, the others Surface), and in a game neither carries PhysicalGeometry (ValidationHelpers.cs:549-553).
  A cat track over another cat track is OverlapExisting (same type, :566-582). `CrossingSteep` trips when the FINAL surface along the
  track, where a crossed outline or its fade reaches, is steeper than the max grade + 1 pp (what the 2 m inside cannot fix). Enter reports
  a guard before an infeasible profile (the pins can bend a profile that a crossing guard explains).
- The run outline: `RunShape` trips when the flat top's ring touches itself (GradeRibbon, 1.5 m clearance). In a game, the area validation
  checks no objects or nets for this run. It checks only the shape and the overlap with another ownerless Space area. The reason:
  `SetCollisionFlags` adds PhysicalGeometry only when markers are not ignored (AreaUtils.cs:679-688), and ValidationHelpers.cs:1003 passes
  ignoreMarkers = !editorMode.

- Lots: every `Building` / `Extension` entity's lot (`BuildingData.m_LotSize` x 8 m, `BuildingUtils.CalculateCorners`,
  BuildingUtils.cs:72-86) in the static object search tree over the rect. Each lot also guards the ground it holds past its rectangle.
  That is its cut / fill window from the prefab's `BuildingTerraformData` (`GradeObstacles.LotWindow`, BuildingUtils.cs:571-617).
  The window is m_Smooth grown to the flat zone, and the flags empty it. A pad-cutting base station's 12 m runway is in it. Block A
  stations and lodges hold nothing past the lot. The guard does not hold the engine's 8 m apron past the window. GradeBatch GR-10b
  (banks-pass 2026-11-09).
- Lift lines: every lift-line net edge (`SkiPrefabCatalogSystem.LiftLineTypes`), straight a -> d, `LiftClearanceM` each side.
- Water: surface depth (`WaterSystem.GetSurfaceData`, WaterSystem.cs:754, `WaterUtils.SampleDepth`, WaterUtils.cs:80) above
  `WetDepthM` on any texel the grade changes.
- Roads and other nets are not guarded: their own `FlattenTerrain` overlay wins over the base map there.

## Logs

Silence must be impossible. The logs are:

- ARMED / NOT ARMED per load
- one line per activation, point edit, solve, apply, undo and refusal
- one line for each read / write of the heightmap writer

An exception disables the tool.

## Shared emit (Quick runs package D)
The run-area creation definition, the ring node's terrain y and the bulldozer-shape delete moved to `RunAreaEmit` (static, no state)
unchanged. So the Quick runs tool builds runs with the same definition. The move preserves behavior: every GradeBatch check is unchanged.
