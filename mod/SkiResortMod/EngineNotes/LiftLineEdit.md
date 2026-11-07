# LiftLineEdit: the pure edit model of the lift-line tool, and how tool lines are recognized after the engine builds them

`LiftLineEdit` is the pure model behind `LiftLineToolSystem`. It has no ECS and no Game types. LiftBatch tests it headless. The tool owns one
instance per line being drawn and turns its `Emit()` list into CreationDefinition + NetCourse entities. `LiftLineInput` holds the pure input
rules (right-click stepping, Build, Esc).

## The model
- Two station ends A (base click) and B (top click): the stub-tip nodes' world positions. The line is the straight xz segment A to B
  ("along" = meters from A in xz). The station ropes RopeA / RopeB are given (`LiftLineToolSystem`). A rope is the station's bullwheel height, else
  stub node y + the rope above the head the node carries (`LiftTowerEdit.RopeAboveNodeM`: the shortest member's for a Line head). A ground
  end plans with the anchor of the station that will finish it (`LiftLineInput.GroundEndRope`, see "Ground ends").
- Pins are positions the user fixed (click on the line = add, drag a tower = move, which pins a free tower).
  - The code snaps pins to the sample grid of the planner (StepM: 5 m, tram 10 m). It counts from the end the planner works from
    (`FromB` = `LiftTowerPlanner.PlansFromB`: the end with the lower rope, LiftTowerPlanner.md "Direction"). So a pin is a planner
    sample, and `LiftTowerPlanner.Plan` keeps it exactly as a seed. The same world spot snaps the same, whichever end the player
    clicked first (LiftBatch DR-3). `Replan` passes the world xz of the ends (`EndKeyA` / `EndKeyB`), and the tool passes a sampler
    from B (`PointFromB`, bit-exact with `PointAt` of the reversed edit).
  - The code refuses a pin closer than MinSpanM to another pin. It also refuses a pin closer than PinEndMarginM to an end. For a loose
    or ground end (a tower), the margin is MinSpanM. For a station end, the margin is MinSpanM too, except for the tram. The tram margin
    is one step (10 m). The 11.4 m tower then stands 4 m clear of the stub tip. Field test 2 refused a tram pin 19 m from the top
    station five times under the old 40 m at both ends. The refusal names the margin (LastEndMarginM).
- The ends know their kind (StationA / StationB, from the tool). A station or ground end gets the planner approach floor and station-side
  rule. A loose end is a tower node. Replan plans with the engine edge slope rule (CheckEdgeSlope) between the nodes as built, with the end
  nodes at A.y / B.y (LiftTowerPlanner.md, Station ends).
- The plan = `LiftTowerPlanner.Plan(seeds = pins)`: the fewest-towers search fills every pin-to-pin segment, with members by phase for
  every tower (pinned ones included). The tool re-plans after every change.
- Undo (right click): every Done edit saves the pins / removed spots before it. Undo restores the last (UndoDepth).
- Remove (drag a tower off the line): a pinned tower is unpinned (the planner may put a tower back near it if the line needs one). A free
  tower is removed by pinning every other line tower where it stands and dropping this one, so the "not here" sticks. The planner fills
  only spans that still exceed MaxSpanM or violate the floor. It never puts a new tower within RemovedRadiusM of a removed spot
  (`LiftTowerPlanner.Forbidden`). So a tower the span still needs lands elsewhere in it.
- Emit: the plan's nodes in order, stations first and last. Each tower is at (x, ground + E(member), z) with E = TopM[member] - TopM[0]
  (`LiftTowerEdit.ElevationM`). E is the node height the fit system writes, so the line is born fitted. A tower with a twin head (B2,
  LiftTowerPlanner.md "Heads") gets the E of the twin (`ElevationM(c, member, head)`) and `LiftLineNode.Head`. Its tower key height is the
  top of the head (`TopOf(member, head)`).

## Tool lines (`LiftToolLines`)
After an Apply the tool registers the emitted inner nodes (xz, member, pinned). `LiftTowerFitSystem` asks `Match()` for every line it
fits. A line whose every inner node is a registered tool node (xz within MatchTolM) is a tool line. The fit gives it heights only, with
the tool's members (never Moved, so a pinned node is never snapped). The registry is in-session only (not saved). After a load, the
hysteresis of the fit (`Fit` currentMembers) keeps a clear line as it is.

The engine can build a tool line with more nodes than the tool emitted. The extra nodes come from `CourseSplitSystem.CheckHeightRange`
elevation-transition splits (CourseSplitSystem.cs:2884-2942) and from crossings with other nets. The tool normally prevents the elevation
splits with the ShoreLine gate (see `LiftLineToolSystem.md`), but a split still happens at crossings. `MatchSplit` recognizes such a line
when:
- every emitted inner node has its own path node within MatchTolM, in order.
- the path's ends are the tool's ends (EndTolM).
- every extra node lies on the tool's straight line (ExtraLateralTolM).

The extras get `ChordMembers` (the rope stays on the planned chord). The fit gives the whole line heights only, never Moved.

## Vanilla right click
Vanilla `NetToolSystem` binds right click ("Cancel", override "Undo Net Control Point") to removing the last control point
(NetToolSystem.cs:5482-5483 enable, :6256-6330 Cancel -> `m_ControlPoints.RemoveAt(Length - 1)`). It disables the action when fewer than two
points are placed (:5482), where our tool closes instead.

## StationRope
`LiftLineInput.StationRope` gives the rope height the built line will have at a station end, so the tool plans with what the fit reads.
`LaneSystem` anchors the rope lane end of the line to the station. The anchors are the world end points of HV sub-lanes (`FindAnchors`, Game.Net/LaneSystem.cs:2371-2416,
:2441-2453, and `FindAnchor` :4049-4071, applied :4117-4121). The sub-lanes belong to the sub-objects of the node (order 0) and to the
owner of the node, the station (order 2). The fit takes the lowest rope-lane end within AnchorSearchM (30 m) of the
node. The lower order wins when it has any within reach. None gives the fallback (stub node + the rope of the shortest member).

## Ground ends (FIX-LIST 48)
A ground end is where a station goes. The Eishain pass drew every line ground to ground and attached the stations line-first (their stub
course reuses the end node). It plans as a station end (StationA / B true, approach floor) with `LiftLineInput.GroundEndRope` =
`LiftTowerPlanConstants.StationEndRope`. That value is the lowest of three values:
- node + the rope of the shortest tower.
- node + the own anchor of the station (`StationDef.RopeAnchorLocalY`, its stub tip being at prefab y 0).
- the anchor over the terrain at the lot front of the station (`StationDef.FrontLocalZ`). The lot front is 24 m (FG, tram) / 32 m (DQ, gondola) beyond the
  end on the axis of the line. Vanilla RoadSide placement stands every building on the terrain there (ObjectToolSystem.CalculateHeight
  :1621-1625, BuildingUtils.CalculateFrontPosition :88-92).

The lower end gets the type's base station, the higher its top station. Before this change, a ground end planned node + the tower rope,
1.4-10.3 m above any station anchor. For example, tram 2 planned +0.4 m and read -6.58 m once its stations stood. Tests: LiftBatch EI-48.

A ground end just past a crest plans honestly low (FIX-LIST 55). The 2 km DQ of the eishain-fixes check planned its top end at node +
-8.68 (the lot front 32 m out stood 12.38 m below the node). So its carriers start below the ground ahead (`EndCarrierB` -12). The
last tower, 8 m from the end, bent the rope +103 deg with no repair possible. Nothing said so: the plan read "clear" (spans only).
Now `LiftLineInput.SunkKey` / `SunkEnd` (pure, `SunkWarnM` 0.25 m) judge ground ends too:
- SUNK for a built station end.
- SUNK_END for a ground end (its station is not placed yet, and past a crest it would stand on the lower front).
- Nothing for a loose end (a tower node).

`PlanKey` returns PLAN_TRAIN when every span clears but a tower is over its cap (PLAN_CLEAR only for a `Sound` plan). `TowerKey` returns
TOWER_TRAIN for that tower (out bend / cap). `Describe` ends with `LiftTowerPlan.Verdict`
("clear" only when sound, else "spans clear, N tower(s) OVER CAP" or "VIOLATED"). Tests: LiftBatch EI-55 (the field shape and an 888-plan sweep).

## JoinBendWarnDeg
`LiftLineInput.JoinBendWarnDeg(builtFar, joint, otherEnd)`: the plan bend the drawn line makes at a loose-end joint
(`LiftAlignMath.PlanBendDeg`), or 0 at or under `LiftAlignMath.BendWarnDeg`. LiftLineToolSystem shows it as the BEND notice. LiftBatch
BT-10 tests it.
