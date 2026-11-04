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
from the Transform; LaneSystem rebuilds the edges' rope lanes on the moved anchors. **UNVERIFIED in game:** that the Updated edge added
in this slot is enough for LaneSystem to re-anchor its ropes; that the Standing leg box follows the rotation.

## Triggers and cost
Both end nodes of every Updated lift edge, the same nodes once more next frame (a station neighbor's rope lane is rebuilt after this
system), every lift node once after a load (the Transform is saved, `Transform.cs:34-53`; a rule change applies there). Event-driven,
main thread, 0 when idle; a node edit touches 2-6 towers (unmeasured; each is a handful of buffer reads). Never Temp / Deleted towers,
line ends, stations, junctions, Editor.

## Readers of the lean
- TrainPitchSystem: `LiftAlignMath.MastPitch(t.m_Rotation)` off the absolute lane-end pitches (the bones are local to the mast).
- TowerNumberTileSystem: tiles are placed in the tower frame (`inverse(tt.m_Rotation)`), so they follow the lean; the carry keeps
  their local pose, so a lean does not churn them.
