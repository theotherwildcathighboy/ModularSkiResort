# LiftLineEdit: the pure edit model of the lift-line tool, and how tool lines are recognized after the engine builds them

`LiftLineEdit` is the pure model behind `LiftLineToolSystem` (no ECS, no Game types; LiftBatch tests it headless). The tool owns one
instance per line being drawn and turns its `Emit()` list into CreationDefinition + NetCourse entities. `LiftLineInput` holds the pure input
rules (right-click stepping, Build, Esc).

## The model
- Two station ends A (base click) and B (top click): the stub-tip nodes' world positions; the line is the straight xz segment A to B
  ("along" = meters from A in xz). Station ropes RopeA / RopeB are given (`LiftLineToolSystem`: the station's bullwheel height, else stub
  node y + the rope above the head the node carries, `LiftTowerEdit.RopeAboveNodeM`: the shortest member's for a Line head; a ground end
  plans with the anchor of the station that will finish it, `LiftLineInput.GroundEndRope`, see "Ground ends").
- Pins are positions the user fixed (click on the line = add, drag a tower = move, which pins a free tower). Pins are snapped to the
  planner's sample grid (StepM: 5 m, tram 10 m) counted from the end the planner works from (`FromB` = `LiftTowerPlanner.PlansFromB`:
  the end with the lower rope, LiftTowerPlanner.md "Direction"), so a pin is a planner sample and `LiftTowerPlanner.Plan` keeps it
  exactly as a seed, and the same world spot snaps the same whichever end the player clicked first (LiftBatch DR-3). `Replan` passes
  the ends' world xz (`EndKeyA` / `EndKeyB`) and the tool a sampler from B (`PointFromB`, bit-exact with the reversed edit's
  `PointAt`). A pin closer than MinSpanM to another pin is refused, and one closer than PinEndMarginM to an end:
  a loose or ground end (a tower) MinSpanM, a station end MinSpanM too except the tram, one step (10 m; the 11.4 m tower then stands 4 m
  clear of the stub tip). Field test 2 refused a tram pin 19 m from the top station five times under the old 40 m at both ends. The
  refusal names the margin (LastEndMarginM).
- The ends know their kind (StationA / StationB, from the tool): a station or ground end gets the planner approach floor and station-side
  rule, a loose end is a tower node. Replan plans with the engine edge slope rule (CheckEdgeSlope) between the nodes as built, the end
  nodes at A.y / B.y (LiftTowerPlanner.md, Station ends).
- The plan = `LiftTowerPlanner.Plan(seeds = pins)`: the fewest-towers search fills every pin-to-pin segment, members by phase for every
  tower (pinned ones included). Re-planned after every change.
- Undo (right click): every Done edit saves the pins / removed spots before it; Undo restores the last (UndoDepth).
- Remove (drag a tower off the line): a pinned tower is unpinned (the planner may put a tower back near it if the line needs one). A free
  tower is removed by pinning every other line tower where it stands and dropping this one, so the "not here" sticks: the planner fills
  only spans that still exceed MaxSpanM or violate the floor, and never puts a new tower within RemovedRadiusM of a removed spot
  (`LiftTowerPlanner.Forbidden`), so a tower the span still needs lands elsewhere in it.
- Emit: the plan's nodes in order, stations first and last, each tower at (x, ground + E(member), z) with E = TopM[member] - TopM[0]
  (`LiftTowerEdit.ElevationM`): the node height the fit system writes, so the line is born fitted. A tower with a twin head (B2,
  LiftTowerPlanner.md "Heads") gets the twin's E (`ElevationM(c, member, head)`) and `LiftLineNode.Head`; its tower key height is the
  head's top (`TopOf(member, head)`).

## Tool lines (`LiftToolLines`)
After an Apply the tool registers the emitted inner nodes (xz, member, pinned). `LiftTowerFitSystem` asks `Match()` for every line it
fits: a line whose every inner node is a registered tool node (xz within MatchTolM) is a tool line, fitted heights only with the tool's
members (never Moved, so a pinned node is never snapped). In-session only (the registry is not saved): after a load the fit's hysteresis
(`Fit` currentMembers) keeps a clear line as it is.

The engine can build a tool line with more nodes than emitted: `CourseSplitSystem.CheckHeightRange` elevation-transition splits
(CourseSplitSystem.cs:2884-2942) and crossings with other nets. The tool normally prevents the elevation splits with the
ShoreLine gate (see `LiftLineToolSystem.md`), but a split still happens at crossings. `MatchSplit` recognizes such a line when:
- every emitted inner node has its own path node within MatchTolM, in order;
- the path's ends are the tool's ends (EndTolM);
- every extra node lies on the tool's straight line (ExtraLateralTolM).
The extras get `ChordMembers` (the rope stays on the planned chord) and the whole line is fitted heights-only, never Moved.

## Vanilla right click
Vanilla `NetToolSystem` binds right click ("Cancel", override "Undo Net Control Point") to removing the last control point
(NetToolSystem.cs:5482-5483 enable, :6256-6330 Cancel -> `m_ControlPoints.RemoveAt(Length - 1)`); it disables the action when fewer than two
points are placed (:5482), where our tool closes instead.

## StationRope
`LiftLineInput.StationRope` gives the rope height the built line will have at a station end, so the tool plans with what the fit reads.
`LaneSystem` anchors the line's rope lane end to the station: anchors are the world end points of the HV sub-lanes of the node's
sub-objects (order 0) and of the node's owner, the station (order 2) (`FindAnchors`, Game.Net/LaneSystem.cs:2371-2416, :2441-2453;
`FindAnchor` :4049-4071, applied :4117-4121). The fit takes the lowest rope-lane end within AnchorSearchM (30 m) of the node; the lower
order wins when it has any within reach, and none gives the fallback (stub node + the shortest member's rope).

## Ground ends (FIX-LIST 48)
A ground end is where a station goes: the Eishain pass drew every line ground to ground and attached the stations line-first (their stub
course reuses the end node). It plans as a station end (StationA / B true, approach floor) with `LiftLineInput.GroundEndRope` =
`LiftTowerPlanConstants.StationEndRope`: the lowest of node + the shortest tower's rope, node + the station's own anchor
(`StationDef.RopeAnchorLocalY`, its stub tip being at prefab y 0) and the anchor over the terrain at the station's lot front
(`StationDef.FrontLocalZ`; vanilla RoadSide placement stands every building on the terrain there, ObjectToolSystem.CalculateHeight
:1621-1625, BuildingUtils.CalculateFrontPosition :88-92), 24 m (FG, tram) / 32 m (DQ, gondola) beyond the end on the line's axis. The lower
end gets the type's base station, the higher its top station. Before, a ground end planned node + the tower rope, 1.4-10.3 m above any
station anchor: tram 2 planned +0.4 m and read -6.58 m once its stations stood. Tests: LiftBatch EI-48.

A ground end just past a crest plans honestly low (FIX-LIST 55): the 2 km DQ of the eishain-fixes check planned its top end at node +
-8.68 (the lot front 32 m out stood 12.38 m below the node), so its carriers start below the ground ahead (`EndCarrierB` -12) and the
last tower, 8 m from the end, bent the rope +103 deg with no repair possible. Nothing said so: the plan read "clear" (spans only).
Now `LiftLineInput.SunkKey` / `SunkEnd` (pure, `SunkWarnM` 0.25 m) judge ground ends too: SUNK for a built station end, SUNK_END for
a ground end (its station is not placed yet; past a crest it would stand on the lower front), nothing for a loose end (a tower
node). `PlanKey` returns PLAN_TRAIN when every span clears but a tower is over its cap (PLAN_CLEAR only for a `Sound` plan),
`TowerKey` TOWER_TRAIN for that tower (out bend / cap), and `Describe` ends with `LiftTowerPlan.Verdict` ("clear" only when sound,
else "spans clear, N tower(s) OVER CAP" or "VIOLATED"). Tests: LiftBatch EI-55 (the field shape and an 888-plan sweep).

## JoinBendWarnDeg
`LiftLineInput.JoinBendWarnDeg(builtFar, joint, otherEnd)`: the plan bend the drawn line makes at a loose-end joint
(`LiftAlignMath.PlanBendDeg`), or 0 at or under `LiftAlignMath.BendWarnDeg`. LiftLineToolSystem shows it as the BEND notice; LiftBatch
BT-10 tests it.
