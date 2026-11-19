# SkiSlopeOverlaySystem: slope colors around the cursor (T253, 1.0.3)

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
- **Runs.** `BuildRuns` turns the disk into same-key row runs. The outer `FadeM` (15 m) fades in `FadeLevels` (4) alpha steps. Over
  `MaxStrips` (4,500) runs, rows are decimated with stride 2..3, the heat-map's rule.

## Renderer

The run heat-map's path: `OverlayRenderSystem.Buffer.DrawLine` with `StyleFlags.Projected`, one strip per run, written by a
`[BurstCompile]` IJob registered with `AddBufferWriter` (OverlayRenderSystem.cs:207, 1.6.0f1). Vanilla's infoview heat map has no
mod slot: `OverlayInfomodeSystem` builds its texture from vanilla infomode components only (OverlayInfomodeSystem.cs:735-1026).
Inside the run being drawn, the heat-map paints the same cells. The overlay skips them (`SkiRunDrawFeedbackSystem.PaintedGrid`,
non-null only on a frame where the heat-map drew strips), so the two never stack alphas.

The overlay's alphas are softer than the heat-map's (`Tuning.Alpha`). The heat-map keeps its own alphas. Whether it should adopt the
overlay's is an open look question in the T253 report.

## Cursor

The default tool raycasts objects only in its default state (DefaultToolSystem.cs:794-796), so its hit cannot be borrowed. The
system marches its own ray along `ToolRaycastSystem.CalculateRaycastLine` against `TerrainUtils.SampleHeight` (`MarchSteps` 384,
then `BisectSteps` 14). The overlay hides while `InputManager.instance.mouseOverUI` is true or the mouse is off screen.

## Phase

Rendering phase. `UpdateBefore` OverlayRenderSystem comes from the anchor on `SkiRunDrawFeedbackSystem`. Mod.cs registers this system
with `UpdateAfter<SkiSlopeOverlaySystem, SkiRunDrawFeedbackSystem>`. An UpdateAfter child is added right after its parent inside the
parent's slot (UpdateSystem.cs:382-412), so it also runs before OverlayRenderSystem. Register it once: each Update call adds an entry.

## Toggle and settings

- The `ToggleSlopeOverlay` action, default Ctrl+Shift+O, rebindable in Options (`ToggleSlopeOverlayBinding`).
- The hub's Slope colors chip on the Runs tab (contract 57, `setSlopeOverlay`) calls `RequestSet`.
- Both paths save the settings file. `ShowSlopeOverlay` is a new bool (default off). Old settings files load without it.
- Game mode only. Nothing is saved in the city, and no prefab changes.

## Logs and cost

- `SlopeOverlay: ARMED` once per load, `SlopeOverlay: ON|off (<source>)` per toggle.
- `[cost]` every `CostLogSeconds` (30 s) while on, with Measurement diagnostics only. It gives the mean and peak managed ms per
  frame and the strip rebuilds. It also gives the strips and stride (CAP HIT when decimated), the tile counters and the heights verified.
- One WARN per load when a warm frame (no tile missing) costs more than `SlowFrameWarnMs` (4 ms) of managed time.
- A one-shot `!!!` on an exception disables the system until the next load.

Host cost (CoreCLR, SlopeBatch `overlay/timing`): a cold 300 m disk is 34 tiles in about 3.1 ms (0.09 ms a tile). The budget
spreads that over frames. Building the runs takes about 0.2 ms, Verify about 0.04 ms. Mono in game is slower: the in-game `[cost]` lines are the
real number. GPU overdraw of projected strips is not measured on the host.
