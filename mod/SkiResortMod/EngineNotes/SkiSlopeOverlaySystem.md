# SkiSlopeOverlaySystem: slope colors around the cursor (T253, 1.0.3, outlines 1.0.4)

While the `ShowSlopeOverlay` option is on, the ground within 300 m of the cursor shows each 3.5 m cell's slope band in the run
colors. North American: green, blue, black, double black. European: green, blue, red, black, ski route. Engine citations are
`File.cs:line` against the decompile 1.6.4f1 unless a line names another version.

## One source of truth

A cell's value is `SkiSlopeClassifier.CellValue`, the same per-cell grade the run rating uses (`ClassifyRing` calls it in its loop).
The 5x5 box gradient gives the fall direction. A centered 20 m fall-line window gives the grade. Near the edge of the known
heights the window shrinks to 15, 10 or 7 m, else the cell takes |g|. The bands come from `BandOf` (6 / 25 / 40 / 60 %). The color
key is `DisplayUnits.StripKey`, shared with the run heat-map. SlopeBatch proves the tile values bit-exact against `ClassifyRing`'s
`CellGrade` on two terrains (`overlay/parity/*`), and the rating fingerprints stayed the same after the refactor.

## World lattice and tiles

The cells sit on the rating's world lattice: origin `-TerrainHeightData.offset.xz`, texel `1 / scale.x` (3.5 m on a standard map).
`SkiSlopeOverlayTiles` keeps 32x32-cell tiles. Each tile samples its heights with a `FullWindowMargin` border, so every cell has
its full 20 m window and needs no neighbor tile. The tiles are pure C# (System and Unity.Mathematics only) and SlopeBatch links them.

- **Update.** The missing tiles in the disk are computed nearest first until `BudgetMs` (1.5 ms wall clock) is spent, at least one
  per frame. A cold 300 m disk is about 34 tiles.
- **Verify.** Each frame re-samples `VerifyNodesPerFrame` (2,048) stored heights, round robin over the cached tiles, and drops a tile
  whose height differs bit for bit. Terraforming and the Editor brush then repaint within a few frames. No event from the terrain
  system is needed.
- **Evict.** At `TileCapacity` (128) the least recently used tile goes.
- **Gather (1.0.4).** `Gather` copies the grades and node heights of `R + BuildMarginM` (300 + 40 m) into two arrays for the
  contour build (EngineNotes/SkiSlopeContours.md). Cells outside that circle and cells the heat-map paints are NaN. The outer
  `FadeM` (15 m) fades in `FadeLevels` (4) alpha steps. 1.0.3's `BuildRuns` / `MergeRows` row runs stay for the tests only.

## Smoothing of the shown colors (1.0.3, replaced in 1.0.4)

1.0.4 replaces this filter with the blur and the outlines of EngineNotes/SkiSlopeContours.md. The text below is the 1.0.3 record.

The shown color of a cell is the 3x3 mode of the cell keys around it (`SkiSlopeOverlayTiles.ModeFilter3`, on while
`SmoothDisplay`). The cell keeps its own key unless another key has strictly more votes among its 3x3 neighbors. So a tie never
moves an edge, a lone cell takes the color of its surroundings, and a straight band edge stays where it is. Cells without a value
(`NoKey`) do not vote and never gain a key. This changes only the shown colors: `Grade` and `Band` (the rating's values, `TryCell`, the parity tests) stay
raw, and the rating fingerprints in SlopeBatch are unchanged.

- Per tile, at compute time. A tile computes its 32 x 32 cells plus a one-cell ring (`Pad` = `FullWindowMargin` + 1, so the ring
  cells also read the full 20 m window). The filter reads the ring, so a tile never needs a neighbor tile and never goes stale. Two
  keys are cached per cell (`KeyNa`, `KeyEu`), so a scheme switch needs no recompute.
- The run heat-map uses the same filter over its own cells (`SkiRunDrawFeedbackSystem.PrepareShownKeys`). So both draw the same rule.
  At the edge of the run, its filter sees only the run's cells. So a border cell can differ from the overlay's cell next to it.
- SlopeBatch: `overlay/modefilter` (lone cell, ties, NoKey, window pass) and `overlay/runs` against an independent reference that reads
  the neighbors through `TryCell` from their own tiles. On the steep test disk, 501 of 23,079 cells (2.2 %) change color. The strips
  drop from 3,034 to 2,782 (-8 %). On the mountain disk the count stays about the same (2,528 -> 2,530).

**Feathered edges: not possible with this renderer.** A projected curve carries only its size, dash lengths, roundness, outline
width, fill style and depth-fade style (`CurveData`, OverlayRenderSystem.cs:38-60, set in `DrawCurveImpl` :267-296). It has no
feather or smoothing width. The outline could fake a soft rim (a fill color with a lower alpha). But every strip has one, also
where a band is split into several strips, so it would draw a grid inside the bands. Roundness rounds the strip ends and opens gaps
where strips meet.

**Half-size cells (1.75 m) near the cursor: not done.** The heights exist only at the 3.5 m texel. Half cells would show interpolated
values that no rating ever reads (against the one source of truth), with about 4x the strips near the cursor.

## Renderer

1.0.4: both the overlay and the run heat-map draw through `ContourDrawJob` (EngineNotes/SkiSlopeContours.md): fill strips and
`DrawCurve` outlines, each with a tight box. The 1.0.3 record follows. The box rules below still apply to every strip and curve.

The 1.0.3 path: `OverlayRenderSystem.Buffer.DrawLine` with `StyleFlags.Projected`, one strip per run, written by a
`[BurstCompile]` IJob registered with `AddBufferWriter` (OverlayRenderSystem.cs:207, 1.6.0f1). Vanilla's infoview heat map has no
mod slot: `OverlayInfomodeSystem` builds its texture from vanilla infomode components only (OverlayInfomodeSystem.cs:735-1026).
Inside the run being drawn, the heat-map paints the same cells. The overlay skips them (`SkiRunDrawFeedbackSystem.PaintedGrid`,
non-null only on a frame where the heat-map drew strips), so the two never stack alphas.

**GPU cost and the strip boxes.** `GetBuffer` builds the buffer with `positionY = heightScaleOffset.y - 50` and
`scaleY = heightScaleOffset.x + 100` (OverlayRenderSystem.cs:633). `FitBox` (:321-327) gives every projected curve a box. The box
starts at positionY and is scaleY tall (the box mesh is y 0..1, :1086-1096). That is the whole terrain height scale plus 150 m. Every
view ray then crosses every strip box between the camera and the ground. So the GPU cost grows with the strip count and the screen
coverage. Test with those boxes: 2026-11-19, 4K, RTX 5090, 83-mod playset, steep site, 3,358 strips. Frame time, overlay off vs on:
15.2 -> 19.9 ms at zoom 1,100, 15.2 -> 28.1 ms at zoom 450, 16.9 -> 18.0 ms at zoom 2,600. The managed part was only 0.65 ms.

The fix: the `Buffer` constructor is public (:122) and takes positionY and scaleY. The draw job reads the five native containers of the
buffer through `BufferView`, a struct with the same fields in the same order (`UnsafeUtility.As`). It builds one `Buffer` per strip
with the box of that strip. The box goes from the lowest node height minus `BoxMarginM` (8 m) to the highest node height plus 8 m.
`Tile.Lo / Hi` give the node range: the min and max node height within `HeightSpan` (3) lattice steps of each cell. That range covers
the cell square and the rows of a stride-3 row group. SlopeBatch `overlay/runs/box` asserts that every node around every painted
cell is inside the bare range. Mean node range per strip on the SlopeBatch terrains: 28 m (steep), 12 m (mountain).

`FitBox` writes bounds.max.y = scaleY, which is the height, not the top. So after its strips the job adds the real top to
`m_CurveBounds`. OnCreate compares the sizes of the two structs. On a mismatch (a game patch changed `Buffer`), it logs a WARN and
draws through the vanilla buffer. That path is slow but draws the same picture. The game-surface lock pins the `Buffer` fields.

`MergeRows` stacks identical row runs into rectangles (same cells, same picture), 9-19 % fewer strips on the SlopeBatch terrains.

Test with the tight boxes: 2026-11-19 08:55, the same machine, site and camera, 2,650 strips, sim paused. Each value is the mean of
three interleaved 5 s samples.

| Zoom | Overlay on | Overlay off |
|---|---|---|
| 450 | 15.8 ms | 16.4 ms |
| 1,100 | 18.3 ms | 18.1 ms |

At zoom 2,600 (two 4 s samples), the difference was inside the noise. The GPU cost of the overlay is now below the noise floor of about 0.5 ms (before: +12.8 ms).
The shots at zoom 450, at zoom 1,100 and at zoom 1,100 with tilt 22 show no holes at the strip edges. Tree canopies stand more than
8 m above the ground and are no longer tinted.

Test with the smoothing of the shown colors: 2026-11-19 11:25, the same site and camera, 2,485 strips (-6 %), sim paused.

| Zoom | Overlay on | Overlay off |
|---|---|---|
| 450 | 13.97 ms | 12.97 ms |
| 1,100 | 13.03 ms | 12.78 ms |
| 2,600 | 14.52 ms | 14.21 ms |

The base frame was 3.4 ms faster than in the first test, so the frame was less GPU-bound. The added 1.0 ms at zoom 450 then includes
the managed cost of about 0.63 ms (the [cost] line). The run heat-map with its strip boxes added 0.08 ms for a run of about 2,750 cells.

The overlay and the run heat-map use the same alpha per strip key (`SkiRunDrawFeedbackSystem.Tuning.KeyAlpha`). The overlay
multiplies it by the fade level at the disk edge.

## Cursor

The default tool raycasts objects only in its default state (DefaultToolSystem.cs:794-796), so its hit cannot be borrowed. The
system marches its own ray along `ToolRaycastSystem.CalculateRaycastLine` against `TerrainUtils.SampleHeight` (`MarchSteps` 384,
then `BisectSteps` 14). The overlay hides while `InputManager.instance.mouseOverUI` is true or the mouse is off screen.

## Phase

Rendering phase. `UpdateBefore` OverlayRenderSystem comes from the anchor on `SkiRunDrawFeedbackSystem`. Mod.cs registers this system
with `UpdateAfter<SkiSlopeOverlaySystem, SkiRunDrawFeedbackSystem>`. An UpdateAfter child is added right after its parent inside the
parent's slot (UpdateSystem.cs:382-412), so it also runs before OverlayRenderSystem. Register it once: each Update call adds an entry.

## Toggle and settings

- The `ToggleSlopeOverlay` action has no default key. The player can bind one in Options (`ToggleSlopeOverlayBinding`). The
  attribute without a key gives an empty control path (SettingsUIKeyboardBindingAttribute.cs:18-20 and :138, ModSetting.cs:82). The hub
  chip and the `ShowSlopeOverlay` option are the default ways in. `SkiSlopeOverlayKeySystem` polls the action at UIUpdate, like the
  other hotkey pollers, and calls `RequestToggle`. The conflict probe (WARN "not live 120 frames after the load") runs only when the
  action has a key (`isSet`, ProxyBinding.cs:600).
- Why no default key: the first default, Ctrl+Shift+O, failed (next item), and the T214 rule leaves no free letter (J is Ski way's).
- **Ctrl+Shift+O never reached the game on the test machine (bridge test 2026-11-19).** The action was live, its binding read
  `<Keyboard>/shift + <Keyboard>/ctrl + <Keyboard>/o`, and the game's raw keyboard saw a plain O. It never saw O with Ctrl+Shift held.
  `RegisterHotKey` for Ctrl+Shift+O fails there with 1409 (already registered): another program holds it system-wide. Windows hands
  such a chord to that program, not to the focused window. Of the 26 Ctrl+Shift letters, only O and X were taken that way on that
  machine. The owning program is not identified. The T214 key audit had already ruled out O: ImageOverlay binds Ctrl+O. A mod key
  can fire on a chord with one more modifier (EngineNotes/SkiResortSettings.md). An earlier note here said that a poll from the
  Rendering phase reads false. That was wrong: the same system-wide hotkey caused those misses too.
- The key log lines (`SlopeOverlay [keys]`): the live state and bindings when polling starts, each live-state change, and the first
  press. A dead key from outside the game shows as "polling ... live True" with no "first press" line.
- The hub's Slope colors chip on the Runs tab (contract 59, `setSlopeOverlay`) calls `RequestSet`.
- Both paths save the settings file. `ShowSlopeOverlay` is a new bool (default off). Old settings files load without it.
- Game mode only. Nothing is saved in the city, and no prefab changes.

## Logs and cost

- `SlopeOverlay: ARMED` once per load, `SlopeOverlay: ON|off (<source>)` per toggle.
- `[cost]` every `CostLogSeconds` (30 s) while on, with Measurement diagnostics only. It gives the mean and peak managed ms per
  frame and the rebuilds. It also gives the fill strips, the curves and the sub-rows used. CAP HIT shows when the builder fell
  back below `SubRows` 4. Then come the mean and peak worker build ms, the tile counters and the heights verified.
- One WARN per load when a warm frame (no tile missing) costs more than `SlowFrameWarnMs` (4 ms) of managed time.
- A one-shot `!!!` on an exception disables the system until the next load.

Host cost (CoreCLR, SlopeBatch `overlay/timing`): a cold 300 m disk is 34 tiles in about 3.1 ms (0.09 ms a tile). The budget
spreads that over frames. Building the runs takes about 0.2 ms, Verify about 0.04 ms. Mono in game is slower: the in-game `[cost]` lines are the
real number. GPU overdraw of projected strips is not measured on the host.
