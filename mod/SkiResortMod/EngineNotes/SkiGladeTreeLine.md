# SkiGladeTreeLine: the default tree line of the Quick runs tree filler

`SkiGladeTreeLine.Estimate(ring, trees, groundMin, groundMax)` reads the map's own wild trees in and around the drawn region and
returns the ground height below which most of them grow. The function is pure and is linked into tests/GladeBatch (`treeline/*`).

## Rule

Trees = positions the caller collected from the region's bounds grown by `AroundM` (500 m). y = the tree's height (its ground plus
the prefab's placement offset, 0 for the vanilla trees the glade tool uses). The sources, best first:

1. Region: at least `MinTrees` (30) trees inside the region (the glade planner's own polygon test, a Thin probe): their 95 % height
   (`Quantile`, nearest rank via `LiftMoverMath.Rank`).
2. Around: fewer inside, at least 30 within the grown bounds (inside included): their 95 % height.
3. Sparse: 5..29 around: the highest of them.
4. None: fewer than 5 (or a degenerate ring). There is no tree line. Height = groundMax (the whole region fills), +Inf without a range.

The result is clamped into [groundMin, groundMax] when they are finite. The result does not depend on input order (sorted copy).

## Why the 95 % height

A tree line is where wild trees stop. The top few percent are strays on ridges and in gullies. 95 % keeps the fill visibly below the
natural forest edge. The slider in the tool corrects it. UNVERIFIED on real maps: a map whose region lies wholly below the natural
line gets a line just under its highest trees. The top 5 % of treed ground stays unfilled until the player raises the slider.

## Drawing it

The contour is the existing `SnowLineRules.MarchingSquares` over a height grid at `Height` (same pattern as the snow-line overlay).
There is no second contour routine.
