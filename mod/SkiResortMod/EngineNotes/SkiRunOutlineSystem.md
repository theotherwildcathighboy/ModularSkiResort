# SkiRunOutlineSystem: how the thick run outline reuses the game's area border pass, and why it never touches m_SnapDistance

This system draws the thick outline and corner dots of a ski run while the player draws, edits or bulldozes it. It draws them as an
overlay. The "Run outline size" setting means only "how thick". Citations are `Game.dll` 1.6.0f1 decompile baseline. Every cited file is
identical in 1.6.2f1.

## Why not m_SnapDistance

`m_SnapDistance` does three jobs:

1. The outline width (AreaBorderRenderSystem.cs:517, :531, x 0.3).
2. The area tool's snapping radius (AreaToolSystem.cs:353, :370-395, :1190-1247).
3. Halved, the blocking "invalid shape" clearance between a corner and the run's other edges (AreaUtils.cs:55-58, ValidationHelpers.cs:667,
   :887-962).

At 8, the thick line came with 8 m snapping and a 4 m clearance, against the game's 0.75 / 0.375. This system NEVER writes
`AreaGeometryData`. The game's value stays (`AreaUtils.GetMinNodeDistance(Surface)` = 0.75, written at every start by
AreaInitializeSystem.cs:336-344, never saved). Runs drawn at 8 need nothing: their corners are >= 4 m apart, which passes the 0.375 m rule.

## What it draws

The system draws the game's own outline (`AreaBorderRenderSystem`, its non-infoview pass :129-136) wider, for our four run prefabs only.
The rules are `RunOutlineRules` (pure, tested in tests/ResortBatch RunOutlineTests). The job copies the game's job (:40-139, :141-264,
:357-513) with these differences:

* The same query as the game's (:614-628: Any {Temp, Error, Warning}, None {Hidden, Deleted}) plus All {Node, PrefabRef}, which every
  area archetype carries.
* The system files the SHARED-EDGE set (`borderMap`) from EVERY outlined area of any prefab, exactly as the game does. So a side our run
  shares with another outlined area is dropped by us exactly when the game drops it. The DOT set (`nodeMap`) holds our own dots, one per
  corner position per frame.
* Only areas whose `PrefabRef` is one of our four run prefab entities are DRAWN.
* Colors: the `RenderingSettingsData` singleton with the game's fallbacks (:666-680). The game's dots are white at alpha 0.5 (:408) and
  draw UNDER ours. OUR dots are opaque white with a thin dark rim (`RunOutlineRules.NodeDot*`), so the player can find the corner to
  connect to on snow, grass and forest. Line width and dot diameter are both 0.3 x `RunOutlineSize` m (8 -> 2.4 m, 16 -> 4.8 m). While a
  run is drawn / edited, the same dots also mark the nodes of our EXISTING run areas within `RunOutlineRules.NeighbourMarginM` (150 m) of
  the drawn bounds (the corner to connect to).
* `StyleFlags.Projected` (the piste-map choice) so a long side lies on the terrain instead of cutting through a ridge. A straight 3D line
  cuts through a ridge. The game draws surfaces that way (styleFlags 0, :363). The game's own thin line and dots still draw underneath in
  the same color. Which one is on top is unverified (cosmetic).
* Not mirrored (none applies to a run): the Lot half color, MapTile dashes, the infoview pass and editor mode. We arm in Game mode only,
  and the editor keeps the game's thin outline.

Which runs show: exactly the ones the game outlines:

* Temp areas flagged Create / Delete / Select / Modify / Replace / Parent (not Hidden).
* Any area carrying Error or Warning. An untouched neighbor shows nothing unless hovered or flagged.

Corner dots show only for Create / Modify. A 1-node Temp (the cursor before the first click) draws one dot. The closing side draws only when
the run is complete.

## The frame

Phase Rendering, anchored `UpdateBefore<SkiRunOutlineSystem, Game.Rendering.OverlayRenderSystem>` (Mod.cs). This is load-bearing:
`OverlayRenderSystem.OnUpdate` completes every registered writer, then uploads and clears the lists (OverlayRenderSystem.cs:738-741). So
our job must be registered before it in the same frame.

## The guard

On every frame with nothing drawn, the system does only these checks: two bools, the setting read, the prefab count and ONE
`IsEmptyIgnoreFilter` on a narrow query. Our runs are Surface areas, so a Temp / flagged Surface must exist. Otherwise nothing of ours can
be outlined. The guard is NECESSARY, not sufficient. Any Temp or flagged Surface passes it (the vanilla Surfaces brush, a building with
surface sub-areas being placed), so on those frames the job IS scheduled. It returns before allocating when no area of ours is in the frame
(`Execute`, `if (!ours)`). The frame still counts in `Draws`, the cost line and the slow-draw WARN
("draw frames with a Temp or flagged surface, ours or not").

Not narrowed by tool on purpose. The area tool edits our runs while holding ANY Surface prefab (AreaToolSystem.cs:320-322). Also, the game
can change the set of tools that make Temp copies of areas (area, bulldoze, selection, ...). A missed tool would silently lose the thick
outline. Size 0: no draw. The system logs the switch to 0 at the first frame with a Temp or flagged surface, then runs one int test per
frame.

Then the system runs this sequence, which is the game's own (:681-708):

1. `RenderingSettingsData` read
2. `ToArchetypeChunkListAsync`
3. `GetBuffer`
4. one Burst `IJob` scheduled on `CombineDependencies(base.Dependency, chunk list, overlay writers)`
5. `chunks.Dispose(handle)`
6. `AddBufferWriter`
7. `base.Dependency = handle`

The job reads ECS data through type handles. So, unlike the piste map's job, it must join `base.Dependency` and hand its handle back. The
system creates the type handles once in `OnCreate` and refreshes them with `Update(this)` (the game's `__AssignHandles` pattern), so no
sync point is added. The system reads the setting every frame, so a change shows at once.

## Logs

The system writes these lines:

* `SkiRunOutlineSystem created`
* the ARMED line once per load (Game mode) with the size, widths and the run prefabs' snap distance
* one WARN per load if a run prefab is missing or its snap distance is not the game's Surface value (someone else wrote it, and the system
  leaves it as is)
* one WARN, and the system is never armed, if a TempFlags / AreaFlags value no longer matches `RunOutlineRules`
* one line when the size changed, at the next frame that would draw (a slider drag logs once, and a switch to 0 logs too:
  `RunOutlineRules.SizeChangeDue`)
* the first draw of the process (JIT, kept out of the peak)
* `RunOutline [cost]` after `CostLogAfterDraws` draw frames of a load (frames with a Temp or flagged surface, ours or not)
* one WARN per load for a draw frame over `SlowDrawWarnMs`
* a one-shot `!!!` that switches the overlay off until the next load (the game's thin outline remains)

Nothing logs per frame.
