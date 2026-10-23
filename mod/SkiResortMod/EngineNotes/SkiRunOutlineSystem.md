# SkiRunOutlineSystem: how the thick run outline reuses the game's area border pass, and why it never touches m_SnapDistance

The thick outline and corner dots of a ski run while it is drawn, edited or bulldozed, drawn by us as an overlay. The "Run outline
size" setting means only "how thick". Citations are `Game.dll` 1.6.0f1 decompile baseline; every cited file is identical in 1.6.2f1.

## Why not m_SnapDistance

`m_SnapDistance` does three jobs: the outline width (AreaBorderRenderSystem.cs:517, :531, x 0.3), the area tool's snapping radius
(AreaToolSystem.cs:353, :370-395, :1190-1247) and, halved, the blocking "invalid shape" clearance between a corner and the run's
other edges (AreaUtils.cs:55-58, ValidationHelpers.cs:667, :887-962). At 8 the thick line came with 8 m snapping and a 4 m clearance
against the game's 0.75 / 0.375. This system NEVER writes `AreaGeometryData`: the game's value (`AreaUtils.GetMinNodeDistance(Surface)`
= 0.75, written at every start by AreaInitializeSystem.cs:336-344, never saved) stays. Runs drawn at 8 need nothing: their corners
are >= 4 m apart, which passes the 0.375 m rule.

## What it draws

The game's own outline (`AreaBorderRenderSystem`, its non-infoview pass :129-136), for our four run prefabs only, wider. The rules are
`RunOutlineRules` (pure; tests/ResortBatch RunOutlineTests); the job copies the game's job (:40-139, :141-264, :357-513) with these
differences:

* The same query as the game's (:614-628: Any {Temp, Error, Warning}, None {Hidden, Deleted}) plus All {Node, PrefabRef}, which every
  area archetype carries.
* The SHARED-EDGE set (`borderMap`) is filed from EVERY outlined area of any prefab, exactly as the game's, so a side our run shares
  with another outlined area is dropped by us exactly when the game drops it; the DOT set (`nodeMap`) holds our own dots, one per
  corner position per frame.
* Only areas whose `PrefabRef` is one of our four run prefab entities are DRAWN.
* Colors: the `RenderingSettingsData` singleton with the game's fallbacks (:666-680), dots white at alpha 0.5 in the game (:408) UNDER
  ours; OUR dots are opaque white with a thin dark rim (`RunOutlineRules.NodeDot*`), so the corner to connect to is found on snow,
  grass and forest. Line width and dot diameter are both 0.3 x `RunOutlineSize` m (8 -> 2.4 m, 16 -> 4.8 m). While a run is drawn /
  edited the same dots also mark the nodes of our EXISTING run areas within `RunOutlineRules.NeighbourMarginM` (150 m) of the drawn
  bounds (the corner to connect to).
* `StyleFlags.Projected` (the piste-map choice) so a long side lies on the terrain instead of cutting through a ridge, where the game
  draws surfaces as straight 3D lines (styleFlags 0, :363). The game's own thin line and dots still draw underneath in the same
  color; which one is on top is unverified (cosmetic).
* Not mirrored, none applies to a run: the Lot half color, MapTile dashes, the infoview pass, editor mode (we arm in Game mode only;
  the editor keeps the game's thin outline).

Which runs show: exactly the ones the game outlines: Temp areas flagged Create / Delete / Select / Modify / Replace / Parent (not
Hidden), and any area carrying Error or Warning (an untouched neighbor shows nothing unless hovered or flagged). Corner dots only for
Create / Modify; a 1-node Temp (the cursor before the first click) draws one dot; the closing side only when the run is complete.

## The frame

Phase Rendering, anchored `UpdateBefore<SkiRunOutlineSystem, Game.Rendering.OverlayRenderSystem>` (Mod.cs; load-bearing:
`OverlayRenderSystem.OnUpdate` completes every registered writer, then uploads and clears the lists, OverlayRenderSystem.cs:738-741,
so our job must be registered before it in the same frame).

## The guard

Every frame, nothing drawn: two bools, the setting read, the prefab count and ONE `IsEmptyIgnoreFilter` on a narrow query (our runs
are Surface areas: a Temp / flagged Surface must exist, else nothing of ours can be outlined). The guard is NECESSARY, not
sufficient. Any Temp or flagged Surface passes it (the vanilla Surfaces brush, a building with surface sub-areas being placed), so on
those frames the job IS scheduled; it returns before allocating when no area of ours is in the frame (`Execute`, `if (!ours)`), and
the frame counts in `Draws`, the cost line and the slow-draw WARN ("draw frames with a Temp or flagged surface, ours or not").

Not narrowed by tool on purpose: the area tool edits our runs while holding ANY Surface prefab (AreaToolSystem.cs:320-322), and the
set of tools that make Temp copies of areas (area, bulldoze, selection, ...) is the game's to change; a missed tool would silently lose
the thick outline. Size 0: no draw; the switch to 0 is logged at the first frame with a Temp or flagged surface, then one int test per
frame.

Then: `RenderingSettingsData` read, `ToArchetypeChunkListAsync`, `GetBuffer`, one Burst `IJob` scheduled on
`CombineDependencies(base.Dependency, chunk list, overlay writers)`, `chunks.Dispose(handle)`, `AddBufferWriter`, and
`base.Dependency = handle`: the game's own sequence (:681-708). The job reads ECS data through type handles, so, unlike the piste
map's, it must join `base.Dependency` and hand its handle back. The type handles are created once in `OnCreate` and refreshed with
`Update(this)` (the game's `__AssignHandles` pattern), so no sync point is added. The setting is read every frame: a change shows at
once.

## Logs

`SkiRunOutlineSystem created`; the ARMED line once per load (Game mode) with the size, widths and the run prefabs' snap distance; one
WARN per load if a run prefab is missing or its snap distance is not the game's Surface value (someone else wrote it; left as is);
one WARN and never armed if a TempFlags / AreaFlags value no longer matches `RunOutlineRules`; one line when the size changed, at the
next frame that would draw (a slider drag logs once; a switch to 0 included: `RunOutlineRules.SizeChangeDue`); the first draw of the
process (JIT, kept out of the peak); `RunOutline [cost]` after `CostLogAfterDraws` draw frames of a load (frames with a Temp or flagged
surface, ours or not); one WARN per load for a draw frame over `SlowDrawWarnMs`; a one-shot `!!!` that switches the overlay off until
the next load (the game's thin outline remains). Nothing per frame.
