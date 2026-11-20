# SkiSlopeContours: smooth band outlines over a fainter fill (T253, 1.0.4)

The slope overlay and the run heat-map draw the slope bands with one renderer. A fill of thin projected strips has its ends on
the band limits. Smooth `DrawCurve` outlines sit on the same limits. Before 1.0.4, each band was a set of whole 3.5 m cells, so the
edges were stairs. The cell values do not change. Only the picture of them changes. Engine citations are `File.cs:line` against the
decompile 1.6.4f1.

Code: `Systems/SkiSlopeContours.cs` (pure C#, SlopeBatch links it) builds the geometry. `Systems/SkiSlopeContourRender.cs` has the
look, the worker-thread builder and the Burst draw job.

## Field

- Input: one value per lattice cell, `SkiSlopeClassifier.CellValue` (the rating's grade, the one source of truth), NaN where the
  cell has no value or is not shown. Node heights come from the same tile heights.
- Smoothing: a NaN-aware 3x3 binomial blur (weights 1-2-1). A NaN neighbor does not vote. This replaces the 1.0.3 mode filter
  (`ModeFilter3` stays in `SkiSlopeOverlayTiles` for the tests and the old preview, but no system calls it). The blur moves an edge
  by less than one cell and removes single-cell islands.
- Band limits: 6 / 25 / 40 / 60 %, plus 15 % for the European scheme (`DisplayUnits.EuGreenMaxPct`). Keys as `DisplayUnits.StripKey`.

## Outlines

- Marching squares at each limit on the smoothed field. A saddle cell is resolved by the average of its four corners. Segments link
  by their edge id into polylines. Closed loops shorter than `MinLoopM` (12 m) are dropped, open pieces shorter than `MinOpenM` (1 m).
- Smoothing: Chaikin, `ChaikinPasses` (2). Open polylines keep their end points. Then Douglas-Peucker with `SimplifyM` (0.2 m).
- Curves: centripetal Catmull-Rom through the points, one cubic Bezier per span (`Bezier4x3`). The centripetal form has no cusps and
  no overshoot at the seams of a closed loop.
- Each curve knows the keys on its two sides (`KeyLo`, `KeyHi`). The line takes the color of the steeper band.

## Fill

- Row strips at the lattice rows. The strip ends are the interpolated crossings of the limits on that row, not the cell edges.
- Sub-rows: where a band edge runs at a shallow angle to x, one strip per row draws "teeth" beyond the outline. So the builder
  sweeps `SubRows` (4) sub-lines per row. Where the crossings of the sub-lines differ by more than `SnapM` (0.5 m), it emits thin
  strips for that piece. Elsewhere one full-height strip covers the row.
- Clip to a polygon (the heat-map's run area, `ClipRing`, even-odd rule): for the fill, the field grows by one ring of cells.
  So the fill reaches the polygon edge. Polygon breaks are not snapped. The curves come from the grown field and are clipped to the
  polygon.
- Clip to a disk (the overlay): the circle, plus 4 fade levels in the outer `FadeM`.

## Look (A2, made overnight 2026-11-20, owner to review)

| Part | Value |
|---|---|
| Fill alpha | `KeyAlpha` x 0.75 (black: x 1.0) |
| Outline color | steeper band color x 0.72, alpha 0.9 |
| Outline width | clamp(0.003 x camera distance, 1, 6) m |
| Caps | round (roundness 1, 1) |
| `KeyAlpha` | 0.35 / 0.35 / 0.40 / 0.55 / 0.50 / 0.40 (flat, green, blue, black, double black, Eu blue) |

`KeyAlpha` sits between the old heat-map alphas and the 1.0.3 alphas (DECISIONS 687). The T234 white edge of the black region is
gone: the outlines mark every band edge.

## Threads and frame cost

- The build runs on a ThreadPool worker (`SkiSlopeContourBuilder`, a `Volatile` state word: idle, running, done). The main thread
  takes a finished build at the next frame, after the last draw job is complete. A worker exception is rethrown on the main thread
  (the one-shot `!!!` path).
- The overlay builds `R + BuildMarginM` (300 + 40 m) and rebuilds after a 20 m cursor move. The draw job clips that to the live
  disk every frame, so the disk follows the cursor without a rebuild.
- Over `MaxFill` strips, the builder retries with `SubRows` 2, then 1.
- The draw job (`ContourDrawJob`, `[BurstCompile]` IJob, `AddBufferWriter`) writes `DrawLine` strips and `DrawCurve` outlines, each
  with its own tight box (`SkiSlopeOverlaySystem.BufferView`, see EngineNotes/SkiSlopeOverlaySystem.md).
- Host cost (CoreCLR, SlopeBatch ContourTiming): about 2.3-2.7 ms per 300 m disk. Mono in game is 5-8x slower, which is why the
  build is off the main thread. Counts on the SlopeBatch terrains: 13-16k fill strips (10-13k of them thin) and 1.5-1.8k curves,
  against 2.7-2.9k strips in 1.0.3. The in-game GPU cost is measured in game (A/B against main at the same camera).

## Tests (SlopeBatch)

- `contours/levels`, `contours/fill`: crossings on a ramp at each limit, fill ends on the crossings.
- `contours/closed`: a cone gives closed loops. The sub-lines are contiguous. `SubRows` 1 gives no sub-strips.
- `contours/nan`: NaN holes give open polylines, no curve through a hole.
- `contours/clip`: a 100 m square turned 23 degrees. The fill ends are on the polygon within `SnapM`. The area is within 1.5 %.
- `contours/box`: every curve and strip box holds its ground (mountain and steep terrains).
- ContourTiming (INFO lines): phase times and counts.
- `--outline-preview <dir>` writes the look sheets (103, A, B, C, A2) for the disk, zoom 450, close and heat-map views.
