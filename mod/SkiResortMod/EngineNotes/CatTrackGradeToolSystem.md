# CatTrackGradeToolSystem: how the cat track tool inputs, solves, guards, writes terrain and builds the run

The tool: click points up the mountain; it solves a grade-limited track through them (`CatTrackGrade.cs`, pure, tested in
GradeBatch), previews it as an overlay, and on Enter grades the corridor into the game's BASE heightmap once (`HeightmapWriter`)
AND builds the cat-track run (`SkiRunCatTrackA`) on the graded flat top, in one action (2026-10-24; before, the run was drawn
separately with the vanilla area tool). The terrain is the player's, stored in the save by the game itself; the run is an ordinary
area entity that binds and bills like a drawn one, rated Green by construction (SkiRunRatingRules.CatTrackBand). Opened by the hub's
Cat track tile (ApplyLaunch; no area-tool fallback, so every cat track is tool-built) or Ctrl+Shift+K.

## Skeleton

Same as `LiftLineToolSystem`'s:

- A prefab-less `ToolBaseSystem` (`base.OnCreate` registers it, ToolBaseSystem.cs:292-316; `TrySetPrefab` false, ToolSystem.cs:263-277).
- Activation is `ToolSystem.activeTool` (ToolSystem.cs:84-98), set from the hub tile, the hotkey (`CatTrackGradeHotkeySystem`) or the
  dev tab.
- The raycast is terrain only. The Cancel action (right click) is the only secondary input (Secondary Apply is off: both are the
  right mouse button by default).
- The overlay goes through `OverlayRenderSystem.Buffer` from `ToolUpdate`. Refusals show as a few-second notice in the tooltip; one
  "!!!" ERROR disables the tool until the next load.

## Input (GradeInput, pure)

- Left click adds a point (within `PickPathM` of the track: inserts it between its two neighbors). Press on a point and drag moves it.
- Right click steps back one action: cancel a drag, else undo the last add / insert / move, else (no point left) undo the last
  grade made since the tool opened, else close the tool.
- The Build key (Enter, shared with the lift tool: `LiftLineHotkeySystem` keeps it live while either tool is open) grades.
- Esc is the frontend's Back, which closes the tool (points dropped, logged).

## Solve on change, not per frame

A point added / inserted / moved (on release) / undone, or a grading setting changed, marks the plan dirty. The next frame re-solves
once: path, the rect and the obstacles (search-tree walk, lift-line edges, the other runs' outlines) FIRST, then the profile (pinned to
the ground inside crossed runs and their fade band), the preview raster on the CPU (cascade) heights, the water depth on the changed
texels and the crossing guards. Per frame: the raycast, the precomputed overlay lines (about one per 8 m of
track plus ticks) and the tooltip. A cost line is logged per solve.

## Enter: emit the run, wait for the game, then grade + apply in one frame

1. Refuse when infeasible (the shortfall says how much more track), when a plan guard trips (lots, lift lines, water, or `RunShape`:
   the outline touches itself) or when the run prefab is missing. Nothing is emitted.
2. Emit ONE area creation definition, AreaToolSystem's shape for a new ownerless area (AreaToolSystem.cs:1572-1587, 1883-1886,
   1967-1968): `CreationDefinition{m_Prefab}` + the `Node` ring with the closing duplicate (GenerateAreasSystem.cs:282-291 marks
   it Complete only then) + `Updated`, through `ToolOutputBarrier`. Node elevation float.MinValue snaps each node to the terrain
   (GenerateAreasSystem.cs:307-316). One emission is enough: Temps survive while applyMode stays None (ToolOutputSystem.cs:22-28).
3. Emitted phase: wait for our Temp area (Temp + Area + our PrefabRef; 30-frame timeout). `GetAllowApply` false (any `Error`,
   ToolBaseSystem.cs:533-540; e.g. OverlapExisting against another ownerless Space area, ValidationHelpers.cs:569/582) = REFUSED:
   applyMode Clear (ToolClearSystem deletes the Temp, ToolClearSystem.cs:101), definitions destroyed, nothing graded.
4. Else, the same frame: the grade below (ApplyCore); refused there = Clear as in 3; else `applyMode = Apply`. ApplyAreasSystem keeps
   the Temp entity as the permanent run (removes Temp, adds Applied / Created / Updated, ApplyAreasSystem.cs:204-208), so the entity
   seen in 3 is the run; it goes into the undo entry. The run binds at ModificationEnd like a drawn run (SkiRunBindTriggerSystem) and
   pays its fee there. The frame after, one BUILT line (or a warning if it is not permanent).
5. The run's nodes follow the grade: the write's tail calls OnAreaChanged (TerrainSystem.cs:5096-5098 adds the bounds to
   GroundHeightSystem's update buffer), and GroundHeightSystem re-snaps every float.MinValue area node there and marks the area Updated
   (GroundHeightSystem.cs:345-364, 762-790). UNVERIFIED in game for an area applied in the same frame.

## The grade (ApplyCore)

1. Read the BASE rect (exact heights, no net / lot overlay) and re-rasterize on it.
2. Re-check the guards.
3. Write, verified bit-exact by readback; a mismatch writes the pre-image back and refuses.
4. Delete the trees standing on the flat top (plain `Deleted` on trees with no `Owner`, the `SkiGladeCleanupSystem` idiom).
5. Push the pre-image for undo.
6. Tell the run catalog to re-rate (terrain epoch).
7. Log one line.

Grading is free, like the vanilla terrain tool; the run pays the cat-track fee (groomed pricing by plan area) when it binds.

## Undo

`GradeUndo` keeps the last 8 grades of the session (64 MB cap). Right click with no point undoes the last track built since the tool
opened (terrain AND run); the dev tab's "Undo last grade" reaches older ones. The run is deleted with plain Deleted (Applied / Created
/ Updated stripped, the SkiRunGateCascadeSystem idiom) when it still exists and is still a cat track; no fee refund (the bulldozer
refunds no run fee either). Undo writes the pre-image back only where the terrain still holds what
the grade wrote (a later terrain-tool stroke survives). Removed trees do not come back. Nothing survives a save / load (after a
reload the vanilla terrain tool is the only undo).

## Guards (never under lifts, lots or water)

- Crossed runs: `CollectObstacles` reads every run of ours but cat tracks in the rect (Area + Node + PrefabRef, kind by
  SkiRunOwnershipSystem.KindOf) BEFORE the raster; the raster keeps their ground (GradeRaster.Build keep) and the trees inside them
  stay. A cat track over a groomed / snowmaking / night / natural run raises no game error: their AreaTypes differ (cat track forged to
  Space, the others Surface) and in a game neither carries PhysicalGeometry (ValidationHelpers.cs:549-553). A cat track over another
  cat track is OverlapExisting (same type, :566-582). `CrossingSteep` when the ground under the track inside a crossed run, or the FINAL
  surface along the track where a crossed outline or its fade reaches, is steeper than the max grade (+ 1 pp for the surface). Enter reports a
  guard before an infeasible profile (the pins can bend a profile that a crossing guard explains).
- The run outline: `RunShape` when the flat top's ring touches itself (GradeRibbon, 1.5 m clearance). In a game the area validation
  checks no objects or nets for this run (`SetCollisionFlags` adds PhysicalGeometry only when markers are not ignored, AreaUtils.cs:679-688,
  and ValidationHelpers.cs:1003 passes ignoreMarkers = !editorMode), only the shape and the overlap with another ownerless Space area.

- Lots: every `Building` / `Extension` entity's lot (`BuildingData.m_LotSize` x 8 m, `BuildingUtils.CalculateCorners`,
  BuildingUtils.cs:72-86) in the static object search tree over the rect.
- Lift lines: every lift-line net edge (`SkiPrefabCatalogSystem.LiftLineTypes`), straight a -> d, `LiftClearanceM` each side.
- Water: surface depth (`WaterSystem.GetSurfaceData`, WaterSystem.cs:754; `WaterUtils.SampleDepth`, WaterUtils.cs:80) above
  `WetDepthM` on any texel the grade changes.
- Roads and other nets are not guarded: their own `FlattenTerrain` overlay wins over the base map there.

## Logs

Silence must be impossible: ARMED / NOT ARMED per load; one line per activation, point edit, solve, apply, undo and refusal; the
heightmap writer logs each read / write; an exception disables the tool.
