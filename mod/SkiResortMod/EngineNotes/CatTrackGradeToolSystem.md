# CatTrackGradeToolSystem: how the Cat Track Grading tool inputs, solves, guards and writes terrain

The tool: click points up the mountain; it solves a grade-limited track through them (`CatTrackGrade.cs`, pure, tested in
GradeBatch), previews it as an overlay, and on Enter writes the corridor into the game's BASE heightmap once (`HeightmapWriter`).
That is terrain the player keeps, stored in the save by the game itself. The cat-track run is then drawn over graded ground
unchanged (its rater reads the new heights).

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
once: path + profile, the preview raster on the CPU (cascade) heights, and the guards' obstacles in the rect (search-tree walk,
lift-line edges, water depth on the changed texels). Per frame: the raycast, the precomputed overlay lines (about one per 8 m of
track plus ticks) and the tooltip. A cost line is logged per solve.

## Apply (Enter, one frame, main thread)

Refuse when infeasible (too steep for the limit; the shortfall says how much more track) or when a guard trips (`GradeGuards`, the
reason in the tooltip). Otherwise:

1. Read the BASE rect (exact heights, no net / lot overlay) and re-rasterize on it.
2. Re-check the guards.
3. Write, verified bit-exact by readback; a mismatch writes the pre-image back and refuses.
4. Delete the trees standing on the flat top (plain `Deleted` on trees with no `Owner`, the `SkiGladeCleanupSystem` idiom).
5. Push the pre-image for undo.
6. Tell the run catalog to re-rate (terrain epoch).
7. Log one line.

Grading is free, like the vanilla terrain tool.

## Undo

`GradeUndo` keeps the last 8 grades of the session (64 MB cap). Right click with no point undoes the last grade made since the tool
opened; the dev tab's "Undo last grade" reaches older ones. Undo writes the pre-image back only where the terrain still holds what
the grade wrote (a later terrain-tool stroke survives). Removed trees do not come back. Nothing survives a save / load (after a
reload the vanilla terrain tool is the only undo).

## Guards (never under lifts, lots or water)

- Lots: every `Building` / `Extension` entity's lot (`BuildingData.m_LotSize` x 8 m, `BuildingUtils.CalculateCorners`,
  BuildingUtils.cs:72-86) in the static object search tree over the rect.
- Lift lines: every lift-line net edge (`SkiPrefabCatalogSystem.LiftLineTypes`), straight a -> d, `LiftClearanceM` each side.
- Water: surface depth (`WaterSystem.GetSurfaceData`, WaterSystem.cs:754; `WaterUtils.SampleDepth`, WaterUtils.cs:80) above
  `WetDepthM` on any texel the grade changes.
- Roads and other nets are not guarded: their own `FlattenTerrain` overlay wins over the base map there.

## Logs

Silence must be impossible: ARMED / NOT ARMED per load; one line per activation, point edit, solve, apply, undo and refusal; the
heightmap writer logs each read / write; an exception disables the tool.
