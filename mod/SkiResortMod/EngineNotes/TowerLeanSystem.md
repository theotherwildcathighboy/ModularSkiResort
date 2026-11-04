# TowerLeanSystem: where a line tower's lean is written, and why there

Header: `Systems/TowerLeanSystem.cs`. Decision: DECISIONS 600. Spike: research tilted-towers-spike-2026-11-04 section 1 (option A).

## What it does
A line tower (a node with exactly two lift edges of one type) whose rope pitch is past what its head (or the tram's saddle) follows
leans its whole mast downhill about its foot: rotation = `LiftAlignMath.LeanRotation(yaw, pitchAlongZ, LeanDeg(type, pitch))`, the
position unchanged (the prefab origin is the foot). Pitch = `ChordPitchDeg` over the two neighbors' plumb rope points (a neighbor
tower's prefab sub-lane ends under its yaw-only pose; a station end's rope lane ends at that node) and the tower's own plumb anchor.
The planner uses the same measure, so plan and game agree and the lean never feeds back.

## Why after AlignSystem, before LaneSystem
- A node tower's Transform is re-derived yaw-only by SubObjectSystem (Modification2B; `Game.Objects/SubObjectSystem.cs:1032-1034`,
  `:1757-1758`) and by AlignSystem on every Updated (Modification4; `Game.Objects/AlignSystem.cs:153-160`, query `:927`). Nothing
  else writes it (spike 1.1). Written later in Modification4 the lean survives until the next node update (U1 probe, DECISIONS 570 c).
- `UpdateAfter<TowerLeanSystem, Game.Objects.AlignSystem>(Modification4)` (`Game/UpdateSystem.cs:178`): AlignSystem then LaneSystem
  are `SystemOrder.cs:153`, `:155`, so LaneSystem hangs the ropes from the leaned anchors in the same frame (no plumb frame).
- Sub-objects (GD beacon row, number tiles) are carried like `AlignSystem.MoveObject` (`:167-180`): same pose relative to the tower.

## Updated
A tower leaned or set plumb in a frame where vanilla did not update it gets `Updated`, and so do its node's lift edges and its moved
sub-objects: Objects SearchSystem (Modification5) and PreCulling (`PreCullingSystem.cs:718-723`) refresh bounds, raycast and culling
from the Transform; LaneSystem rebuilds the edges' rope lanes on the moved anchors. Verified in game (600 f): the Updated edge added
in this slot is enough for LaneSystem to re-anchor its ropes (lane ends on the leaned anchors within 0.02 m). **UNVERIFIED:** that the
Standing leg box follows the rotation.

## Triggers and cost
Both end nodes of every Updated lift edge, the same nodes once more next frame (a station neighbor's rope lane is rebuilt after this
system), every lift node once after a load (the Transform is saved, `Transform.cs:34-53`; a rule change applies there). Event-driven,
main thread, 0 when idle; a node edit touches 2-6 towers (unmeasured; each is a handful of buffer reads). Never Temp / Deleted towers,
line ends, stations, junctions, Editor.

## Readers of the lean
- TrainPitchSystem: `LiftAlignMath.MastPitch(t.m_Rotation)` off the absolute lane-end pitches (the bones are local to the mast).
- TowerNumberTileSystem: tiles are placed in the tower frame (`inverse(tt.m_Rotation)`), so they follow the lean; the carry keeps
  their local pose, so a lean does not churn them.

## 600 a: FG rule, tram pedestals
- FG lean rule: clamp(|pitch| - 12, 0, 25) (`FgLeanFreeDeg`, `FgMaxLeanDeg`); DQ / GD stay 20 / 20, tram 30 / 25. Nothing changes in
  this system: it reads `LeanDeg` per type.
- Tram pedestals: a leaned tram tower floats its pivot `FloatM` above the ground. The planner writes the node at ground + E + FloatM with
  Elevation (E, E), so the engine picks the member as before and stands its pivot FloatM up (the AnchorTop placement, LiftTowerPlanner.md).
  This system keeps the Transform position, so the lean turns about the floated pivot, and `PlumbAnchor` reads the floated rope: the same
  rope points the planner's final pitch uses (Build re-runs SetLeans after the float), so plan and game still agree.

## 600 e: the padded face uphill
- **The defect:** every tower mesh carries its crash pad (padded face, warning triangle) and its TrainFront half on local +z; the ladder,
  the pad's opening and the Hold / Combo deck end on -z (art convention, research art/tower-lean-2026-11/build.md "Yaw fix"; the tram:
  `tram_tower.py` authors travel on +X and the ladder on -X, which `orient_for_net` maps to game +z / -z). Vanilla yaws a node tower
  along the draw direction: `NodeAlignSystem.AlignNode` sums the edges' travel tangents (`Game.Net/NodeAlignSystem.cs:85-111`) and
  flips the yaw to agree with that sum (`:209-212`); AlignSystem copies the node yaw to the tower. A line drawn top to bottom showed every
  pad downhill (smoke check 2026-11-04, FG; plumb towers too).
- **The rule:** this system turns each line tower, and each ground-end tower (one lift edge, nothing else, no Owner: never a station
  stub), by 0 or 180 deg about vertical so +z points toward its SECTION's higher rope end (`LiftAlignMath.SectionUphill`, then
  `PadUphillYaw`), then leans it with the pitch read along the new +z. One decision per section (a walk over the nodes with two lift edges
  of one type, end to end, station to station), so every tower of a section faces the same way, as on a real lift, a hump included; the
  rope height of each end is `RopePoint` (a tower's plumb anchor, a station's rope lane ends). A section whose ends differ by less than
  `PadFlatRiseM` (1 m), or a closed loop, keeps vanilla's yaw (the node's own rotation, so a section that turns flat goes back to vanilla).
  An edit anywhere on a section re-checks all its towers (a walk plus a few buffer reads each; an unchanged pose is skipped).
- **Ropes do not move (decompile):** `LaneSystem.CreateEdgeLanes` gives every FindAnchor lane an edge-side position
  (`Game.Net/LaneSystem.cs:2575-2623`), `FindAnchors(node, ...)` collects the node's sub-objects' prefab sub-lane points in world space
  (`:2371-2418`, `:2441-2453`), groups both lists by lane prefab and, per group, keeps the nearest anchors and sorts them along the edge's
  own lateral axis (`:2492-2560`); `FindAnchor` then snaps each lane end to its anchor's POSITION and PathNode (`:4049-4072`, `:4117-4120`).
  Matching is by position, never by sub-lane index or tower yaw. Every rope-carrying tower prefab (32: members, twins, tram towers) has an
  anchor set symmetric under the turn ((x, y, z) -> (-x, y, -z) per lane prefab: haul +-x at z 0, comm at x 0), and the turned, leaned
  frame is the old one times RotateY(pi), so the anchors land on the same world points: the ropes, their sides and the comm wire are
  unchanged. Only the PathNode index behind each point swaps (left haul = sub-lane 1 instead of 0); both edges at the tower snap to the
  same point, so the lane chain (LiftLoopCache, PathNode equality) still joins left to left. Gate: LiftBatch PF-2 (fails on a new
  asymmetric tower prefab).
- **Readers:** TrainPitchSystem reads front / back and the pitches along the current +z, and `MastPitch` from the rotation: on a turned
  tower TrainFront is the uphill half and both halves read nose up (PF-3). Number tiles: both plates, posed in the tower frame and carried
  with it (same local pose, no churn); the GD beacon is posed through the head in the tower frame. The planner's kind (Hold / Combo by
  convex / concave) does not depend on the yaw; the twin's deck end (-z) is now downhill as the art intends.
- **In game (DECISIONS 600 f, bridge):** pad uphill on 64 of 64 towers (FG, DQ, GD, tram, both draw orders, ground ends, a
  station-to-station section); rope lane ends on the turned, leaned anchors within 0.019 m (tram 0.023), no side swap; save / reload
  keeps yaw and lean (one tram tower re-fit 1.05 m lower at load: the fit, not this system, FIX-LIST 57 candidate).
