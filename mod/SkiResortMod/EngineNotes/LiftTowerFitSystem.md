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
`LiftCensusSystem.BuildCensus` calls `OnCensusBuilt(edges, nodes)` after every build (load baseline included). A component whose key
(`LiftTowerDiagSystem.Signature`: edge ids + node positions, XOR its station connection nodes, `LiftCensus.Node.Station`) is new is queued
and fitted once the census has been quiet for SettleFrames
updates (a drag keeps resetting the timer). The fit is a fixed point (LiftBatch LP-13): the build it causes queues the line again and the
re-fit writes nothing ("already fitted"). Guard: a line written MaxWrites times within GuardFrames is left alone with a WARN.
The station ends are in the key because a station attached line-first (its stub course reuses the line's unowned end node,
LiftReconnectSystem.md) changes no edge and no node position: without them the line was fitted in session as ground-ended and only
re-fitted after the next load, against the station's lower rope anchor (FIX-LIST 52).

## Ground
The terrain sampler is `LiftTowerEdit.UncutSampler`. A sample at this line's own cut ceiling (edge y + composition height range min +
terrain offset, `TerrainSystem.AddSegment`) is our trench, so the plane through the uncut ground beside it stands in; otherwise towers
stand in the trenches of a vanilla line drawn on the ground and keep round pits around them.

## A built tower's ground is its foot (FIX-LIST 57)
Our own cut sinks the ground under a line's towers AFTER the fit that placed them, and the load re-fit (every line: `m_Seen` is in
memory only) reads the sunk ground. Field (handtest-tower-lean run 2, 717be77, `bridgeleanpad`): the tram x -1700 was fitted 0.3 s
after its build with T60 520719 at ground 1290.82 (node 1320.82 = ground + E 30, float 0; the fit wrote Elevation only, so the key did
not change and nothing re-fitted in session). At load the same spot read 1289.77, and EVERY tram tower of the city stood 0.3-1.0 m over
its ground (chairs 0.0-0.3, one GD12 0.7); the high side beside the T60 read +6.4 for +11.4 (5 m lower). The fit re-grounded the T60
1.05 m lower (past `HeightAboveTolM` 1.0), and right after that write the ground under it read 1289.49: another 0.28 m, so the ground
there follows our own node height (the cut, never a fill: `TerrainSystem.AddSegment`, TerrainSystem.cs:1397-1516; the CPU heights are read
back from the cascade, :3598-3646). UNVERIFIED: which curve height `AddSegment` gets for a raised power-line node (a 5 m cut 7.5 m beside
a node 30 m over the ground does not fit a ceiling of node y + 4; it fits one near the tower's foot), and the readback latency that lets
the build-time fit miss the cut.
Not the 52 root (the station ends missing from the key): the plan here was sound and the same, so `ToWrite` let it through; the write
was a pure height drift. Rule, over a BUILT line only:
- the fit samples each built tower's center at max(ground, foot), foot = node y - E (`LiftTowerEdit.BuiltFootSampler`); the sides, the
  span samples and the float profile keep the ground as it is (lower = more clearance);
- the foot already holds a tram tower's pedestal float (600 b), so the planner adds none at a built tower (`LiftTowerPlanner.FootAlong`;
  without it the bench T60 rose 0.49 m on every reload, RL-1);
- a node with member, head and position unchanged is written only when the ground rose over its foot (`HeightWrite(old, target, built)`,
  one-sided below); `NodeWrite` is the whole per-node decision, pure for LiftBatch.
Log: the fit line names "N built tower(s) stand up to X m over the ground now under them (our own cut sank it): judged at their feet".
Trade-off: a tower whose ground the player lowers stays at its height after a load, as it already did in session (terraform changes no fit
key); the per-section redo re-grounds it. A moved or re-membered built tower keeps its old foot (it stands where the old one stood).
Tests: LiftBatch RL-57 (the field tram replayed from the build dump: the pre-57 path writes the T60 1.05 m lower, the fit now nothing),
RL-1 (FG / DQ / GD / tram both draw orders, leaned, a floated tram: a reload on the same ground and on ground sunk 1.05 m writes nothing).

## Station ends
An end is a station end (approach floor, `LiftTowerPlanner.StationA/B`) by the census rule `LiftCensusSystem.IsStationNode`
(`StationOfNode`: the node's Owner or owner's owner is a station, or, line-first, a station-owned stub edge is in its ConnectedEdge buffer),
or any owned node. Owner alone (before FIX-LIST 52) read a line-first end as a ground end in session and as a station after a load. The
end rope is the lowest end point of the end edge's HV sub-lanes (`StationRope`), which with a station attached is the station's own anchor.

## Never worse over a built line (FIX-LIST 52)
A line is BUILT when every inner node's Elevation is exactly (E, E) of the member and head `MemberOf` reads from it, with evidence (a node
raised above E 0, or every node classified this session); an engine line on the ground or the tool's fresh nodes are not. Over a built
line the fit writes its plan only when it is sound (every span clear, no tower over its cap) or no worse than the line as it stands (the
"before" plan: its own members and heads, `HeadsAsGiven`) on every axis: floor margin (0.05 m), violated spans, towers over cap, worst
excess over a cap (`LiftTowerPlanner.ToWrite`, `LiftTowerPlan.WorseThan`). Otherwise one WARN "KEPT AS BUILT" with both plans and the
axes, and nothing is written. Field case: the Eishain FG line read -1.7 m after a load (stations attached line-first under ground-end
ropes) and the Short fit wrote FG20BreakA at d +47.2, over its cap (LiftBatch EI-52). Trade-off: a built line that is floor-violated stays
so when every better arrangement puts a tower over its cap; the WARN names it, and the tool's per-section redo is the remedy.

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
line; Short = WARN with the tower count the line needs; KEPT AS BUILT = WARN (above); skips (branched / bent with a non-plain node / no
constants) one Info line each; an exception disables the system until the next load.
