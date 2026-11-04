# LiftTowerFitSystem: what the fit writes into the engine's line, why it holds, and when it runs

`LiftTowerFitSystem` fits every new or changed lift line so its carriers keep the floor over the ground. The planner is the pure
`LiftTowerPlanner` (`Fit`); this system reads the line, asks, writes the answer back and logs. Citations are the decompile baseline 1.6.0f1.

## What the engine builds, and what the fit sets
The engine splits a drawn line into equal spans <= MaxPylonDistance (CourseSplitSystem.cs:3485-3493) with every node on the ground, so the
placeholder ladder always spawns the shortest tower. The node count stays the engine's; per inner node the system sets:
- Its position along the line: kept, or slid to the planner's spot when heights alone cannot clear the line and the line is straight in xz
  (power-line edges are straight, NetInitializeSystem.cs:2195 StraightEdges).
- Its height: node y = center ground + E and `Game.Net.Elevation` = (E, E), E = TopM[member] - TopM[0] (`LiftTowerEdit`). The vanilla ladder
  then scores the planned member exactly 1 (SubObjectSystem.cs:980, :2189-2199) and, top-anchored (:2464-2469, :2477-2482), stands it on
  the ground; the rope lane rides node y + lane y (LaneSystem.cs:4112-4115) until FindAnchor swaps its end for the tower sub-lane (pivot + member lane y); an end without an anchor falls back to node y + `RopeAboveNode` (head-aware: a twin node sits lower than RopeAboveBase(0), DECISIONS 592 u). E = 0 removes the component (the engine adds it only for a
  non-zero elevation, GenerateNodesSystem.cs:1497-1499).
- Each touched edge's Curve = `NetUtils.StraightCurve(start, end, net m_Hanging)`, the engine's own power-line curve (NetUtils.cs:178-191,
  CourseSplitSystem.cs:1702-1704), m_Length = `MathUtils.Length` (as `Curve.Deserialize` does).

Then it tags the nodes and edges Updated. Nothing re-snaps them: `GroundHeightSystem` skips FlattenTerrain nets without a lot owner (edges
:661-667, nodes :747-749; every non-marker power line gets FlattenTerrain, NetInitializeSystem.cs:2184-2187). Station stub nodes, branch
nodes and owned nodes are never touched. A later edit by the net tool starts from these values (GenerateEdgesSystem.cs:1421-1425 copies
the original's Elevation).

## Phase (Mod.cs)
`UpdateAt Modification1`. Load-bearing: every consumer of the Updated tag runs later in the same frame (GroundHeightSystem Modification2,
SubObjectSystem 2B with owner query All SubObject + Any Updated :3936-3943, CompositionSelect 3, Net.GeometrySystem and LaneSystem 4, the
census and ChairPinSystem 4B) and `CleanUpSystem` wipes the tag at the end of the frame (SystemOrder.cs:54), so a tag added at 4B would be
lost.

## Trigger and guard
`LiftCensusSystem.BuildCensus` calls `OnCensusBuilt(edges)` after every build (load baseline included). A component whose signature
(`LiftTowerDiagSystem.Signature`: edge ids + node positions) is new is queued and fitted once the census has been quiet for SettleFrames
updates (a drag keeps resetting the timer). The fit is a fixed point (LiftBatch LP-13): the build it causes queues the line again and the
re-fit writes nothing ("already fitted"). Guard: a line written MaxWrites times within GuardFrames is left alone with a WARN.

## Ground
The terrain sampler is `LiftTowerEdit.UncutSampler`. A sample at this line's own cut ceiling (edge y + composition height range min +
terrain offset, `TerrainSystem.AddSegment`) is our trench, so the plane through the uncut ground beside it stands in; otherwise towers
stand in the trenches of a vanilla line drawn on the ground and keep round pits around them.

## Moved
The free plan's own members travel with its nodes, spares get the shortest clearing member (`LiftTowerPlanner.Fit`); a standing line within
`LiftTowerPlanner.KeepTolM` of the floor is kept (grid noise, not Short).

## Tool lines
A line the lift-line tool built is fitted heights-only with the tool's members, never Moved. Exact match = `LiftToolLines.Match` (same node
count). When the engine split the tool's courses into more edges (CourseSplitSystem elevation transitions, :2884-2942),
`LiftToolLines.MatchSplit` still recognizes it (every emitted node within 0.5 m, ends = the tool's, extras on the line) and gives each
extra split node the member that keeps the rope on the planned chord (`ChordMembers`). One Info line per such match; a WARN when a tool
line's pinned towers stay put on a Short line, or when a line holding a tool line's pinned nodes did NOT match and is Moved. See
`LiftLineEdit.md`.

## Heads (B2 twins)
The fit writes a tower's head through its height: E = `LiftTowerEdit.ElevationM(c, member, head)` (a twin's own top - TopM[0]), and the
engine spawns that twin, a member of the same placeholder (LiftTowerPlanner.md "Heads"). The standing line's members and heads are read
back by `LiftTowerEdit.MemberOf(c, E, out head)` (0.05 m match). The planner gets them as `PreviousKinds` / `PreviousHeads` (the fit's own
earlier kinds, else the head's kind) for the kind hysteresis; the "before" plan takes the standing heads as given (`HeadsAsGiven`), so a
line whose heads are still right is "already fitted" (LiftBatch HD-5). A head change alone re-members a tower. `LoadTwins`
(OnGameLoadingComplete) sets `LiftTowerPlanConstants.TwinFilter` to the twin prefabs the PrefabSystem has; one "Towers [twins]" line per
load (n of 16 loaded, a WARN naming the missing ones), and a build without them plans support heads only.

## Logs
Silence is impossible: one ARMED line per load; per fitted line a "Towers [fit]" block (per tower: position, member, elevation, rope before
to after; per span: length, lowest carrier over the high side, max rope above ground) plus one "Towers [fit summary]"; "already fitted" one
line; Short = WARN with the tower count the line needs; skips (branched / bent with a non-plain node / no constants) one Info line each;
an exception disables the system until the next load.
