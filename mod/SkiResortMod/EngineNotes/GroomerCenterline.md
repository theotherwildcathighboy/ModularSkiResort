# GroomerCenterline: the drive path from a ribbon outline

Pure, no game types. Tests: ResortBatch `GroomerTests` GM-7 to GM-9 (hand-drawn shapes and degenerate rings), GradeBatch GR-30 (the tool's own ribbons, against the generator's centerline). Design source: docs/research/cat-track-run.md section 3 (chordal axis, prune branches, Douglas-Peucker), which flagged bulges and necks as unverified on ugly polygons. This page says what the code does instead and why.

## The steps

1. **Clean.** Finite nodes only; drop duplicates (5 cm), the closing duplicate and zero-width spikes, sweeping until nothing moves because removing a spike leaves a duplicate. Under three nodes: `NoOutline`.
2. **Area and winding.** Under 20 m2: `NoArea`. Reverse to counterclockwise.
3. **Simple.** No two non-adjacent edges cross or touch, the rule the engine holds a finished area to (`ValidationHelpers.cs:887-943`). A save can still hold anything: `NotSimple`.
4. **Subdivide long edges** to 3/4 of the mean width (2A / P), between 3 and 40 m. Without this a player's four-node rectangle has one diagonal, one midpoint and no path, and a generated straight has the same problem. The added nodes are collinear with their edge.
5. **Triangulate.** Ear clipping, O(n^2). A node on a would-be diagonal blocks it, so collinear nodes stay. When stuck on rounding, drop one collinear node, which only loses a subdivision. Still stuck: `NoTriangulation`.
6. **Delaunay flips** (Lawson, at most 200 passes). Ear clipping leaves slivers along the track, whose midpoints sit off center. An unfinished pass count is still a valid triangulation, only less centered. Cocircular quads on a straight ribbon are not flipped, and both diagonals have the same midpoint line.
7. **The longest route through the triangle tree.** The dual graph of a simple polygon's triangulation is a tree. Two sweeps (farthest from anywhere, then farthest from that) find its diameter, measured along edge midpoints; a junction triangle joins two of its midpoints directly, so the metric is slightly short of a true tree metric and the result can be short of the true longest by a little. That is fine: the result only has to be long and deterministic. Branches and the stubs of a bulge are shorter, so the route leaves them out, which replaces the doc's explicit "prune branches shorter than the width". An explicit stack, because a long track is a chain thousands deep.
8. **Is it a corridor?** Route length squared over area under 2.5 (a square, a disc), or a route longer than the perimeter: `NotACorridor`. Mean width from here on = area / route length.
9. **Trim the ends** by half the mean width, between 4 and 24 m. The medial axis fans to the two corners of an end cap at about half a width from it, so this removes the fan and keeps the machine's nose on a narrow track. Under 30 m left: `TooShort`.
10. **Resample and smooth.** A uniform 2 m step (2,048 points at most), three passes of a moving average over half the mean width (4 to 12 m); the window shrinks toward the ends, symmetrically, so the ends stay put. Resampled again afterward so `GroomerPath.At` is O(1).
11. **Stay inside.** Every point must be inside the outline. A moving average cuts the corner of a tight bend, so on a miss the window halves and it retries. The unsmoothed route lies inside by construction (each piece lies in one triangle), so `LeftOutline` should never fire; it guards the rules above.

## Cost

Worst measured: GR-30's 3,476 m winding ribbon, 331 nodes, 5.1 ms in the test host (not the game's runtime; `Groomers [cost]` prints the slowest build in game). Only on a load and on an outline edit, at most one per frame. `MaxOutlineNodes` 4,000 and `MaxAddedNodes` 1,500 bound the O(n^2) steps.

## Why not the alternatives

- **Pair the two edges of a generated ribbon.** Fails on hand-drawn tracks, and the ribbon's edges have been cut, simplified and thinned independently (`GradeRibbon.Build`), so index pairing is wrong even there.
- **Keep the tool's path.** A grading tool path is not saved anywhere, and hand-drawn tracks never had one.
- **Raster and skeleton.** Memory follows the bounding box, not the track: a 2 km diagonal at 2 m is a million cells.
