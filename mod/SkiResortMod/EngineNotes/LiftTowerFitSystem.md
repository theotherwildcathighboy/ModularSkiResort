# LiftTowerFitSystem: what the fit writes into the engine's line, why it holds, and when it runs

`LiftTowerFitSystem` fits every new or changed lift line so its carriers keep the floor over the ground. The planner is the pure
`LiftTowerPlanner` (`Fit`). This system reads the line, asks the planner, writes the answer back and logs. Citations are the
decompile baseline 1.6.0f1.

## What the engine builds, and what the fit sets
The engine splits a drawn line into equal spans <= MaxPylonDistance (CourseSplitSystem.cs:3485-3493) with every node on the ground. So the
placeholder ladder always spawns the shortest tower. The node count stays the engine's. Per inner node, the system sets:
- Its position along the line: kept, or slid to the planner's spot. The slide happens when heights alone cannot clear the line and the line is
  straight in xz (power-line edges are straight, NetInitializeSystem.cs:2195 StraightEdges).
- Its height: node y = center ground + E, and `Game.Net.Elevation` = (E, E), E = TopM[member] - TopM[0] (`LiftTowerEdit`). The vanilla ladder
  then scores the planned member exactly 1 (SubObjectSystem.cs:980, :2189-2199). It stands the tower on the ground, top-anchored
  (:2464-2469, :2477-2482). The rope lane rides node y + lane y (LaneSystem.cs:4112-4115) until FindAnchor swaps its end for the tower
  sub-lane (pivot + member lane y). An end without an anchor falls back to node y + `RopeAboveNode` (head-aware: a twin node sits lower
  than RopeAboveBase(0), DECISIONS 592 u). E = 0 removes the component (the engine adds it only for a non-zero elevation,
  GenerateNodesSystem.cs:1497-1499).
- Each touched edge's Curve = `NetUtils.StraightCurve(start, end, net m_Hanging)`, the engine's own power-line curve (NetUtils.cs:178-191,
  CourseSplitSystem.cs:1702-1704). m_Length = `MathUtils.Length` (as `Curve.Deserialize` does).

Then the system tags the nodes and edges Updated. Nothing re-snaps them: `GroundHeightSystem` skips FlattenTerrain nets without a lot owner (edges
:661-667, nodes :747-749). Every non-marker power line gets FlattenTerrain (NetInitializeSystem.cs:2184-2187). The system never touches
station stub nodes, branch nodes and owned nodes. A later edit by the net tool starts from these values (GenerateEdgesSystem.cs:1421-1425
copies the original's Elevation).

## Phase (Mod.cs)
`UpdateAt Modification1`. Load-bearing: every consumer of the Updated tag runs later in the same frame:
- GroundHeightSystem (Modification2)
- SubObjectSystem (2B, with owner query All SubObject + Any Updated :3936-3943)
- CompositionSelect (3)
- Net.GeometrySystem and LaneSystem (4)
- the census and ChairPinSystem (4B)

`CleanUpSystem` wipes the tag at the end of the frame (SystemOrder.cs:54). So a tag added at 4B would be lost.

## Trigger and guard
`LiftCensusSystem.BuildCensus` calls `OnCensusBuilt(edges, nodes)` after every build (load baseline included). The system queues a component
whose key is new (`LiftTowerDiagSystem.Signature`: edge ids + node positions, XOR its station connection nodes, `LiftCensus.Node.Station`).
It fits the component once the census has been quiet for SettleFrames updates (a drag keeps resetting the timer). The fit is a fixed point
(LiftBatch LP-13): the build it causes queues the line again, and the re-fit writes nothing ("already fitted"). Guard: a line written
MaxWrites times within GuardFrames is left alone with a WARN.
The station ends are in the key. A station attached line-first (its stub course reuses the line's unowned end node,
LiftReconnectSystem.md) changes no edge and no node position. Without them the line was fitted in session as ground-ended. The line was
re-fitted only after the next load, against the station's lower rope anchor (FIX-LIST 52).

## Ground
The terrain sampler is `LiftTowerEdit.UncutSampler`. A sample at this line's own cut ceiling (edge y + composition height range min +
terrain offset, `TerrainSystem.AddSegment`) is our trench. So the plane through the uncut ground beside it stands in. Otherwise towers
stand in the trenches of a vanilla line drawn on the ground and keep round pits around them.

## A built tower's ground is its foot (FIX-LIST 57)
Our own cut sinks the ground under a line's towers AFTER the fit that placed them. The load re-fit (every line: `m_Seen` is in
memory only) reads the sunk ground. Field (handtest-tower-lean run 2, 717be77, `bridgeleanpad`): the tram x -1700 was fitted 0.3 s
after its build. The T60 520719 stood at ground 1290.82 (node 1320.82 = ground + E 30, float 0). The fit wrote Elevation only, so the key did
not change and nothing re-fitted in session. At load the same spot read 1289.77. EVERY tram tower of the city stood 0.3-1.0 m over
its ground (chairs 0.0-0.3, one GD12 0.7). The high side beside the T60 read +6.4 for +11.4 (5 m lower). The fit re-grounded the T60
1.05 m lower (past `HeightAboveTolM` 1.0). Right after that write, the ground under it read 1289.49: another 0.28 m. So the ground
there follows our own node height (the cut, never a fill: `TerrainSystem.AddSegment`, TerrainSystem.cs:1397-1516). The CPU heights are
read back from the cascade (:3598-3646). UNVERIFIED:
- Which curve height `AddSegment` gets for a raised power-line node. A 5 m cut 7.5 m beside a node 30 m over the ground does not fit a
  ceiling of node y + 4. It fits one near the tower's foot.
- The readback latency that lets the build-time fit miss the cut.

This is not the 52 root (the station ends missing from the key). The plan here was sound and the same, so `ToWrite` let it through. The
write was a pure height drift. Rule, over a BUILT line only:
- The fit samples each built tower's center at max(ground, foot), foot = node y - E (`LiftTowerEdit.BuiltFootSampler`). The sides, the
  span samples and the float profile keep the ground as it is (lower = more clearance).
- The foot already holds a tram tower's pedestal float (600 b), so the planner adds none at a built tower (`LiftTowerPlanner.FootAlong`).
  Without it the bench T60 rose 0.49 m on every reload (RL-1).
- A node with member, head and position unchanged is written only when the ground rose over its foot (`HeightWrite(old, target, built)`,
  one-sided below). `NodeWrite` is the whole per-node decision, pure for LiftBatch.

Log: the fit line names "N built tower(s) stand up to X m over the ground now under them (our own cut sank it): judged at their feet".

Trade-off: a tower whose ground the player lowers stays at its height after a load. It already did so in session (terraform changes no fit
key). The per-section redo re-grounds it. A moved or re-membered built tower keeps its old foot (it stands where the old one stood).

Tests:
- LiftBatch RL-57: the field tram replayed from the build dump. The pre-57 path writes the T60 1.05 m lower, the fit now writes nothing.
- RL-1: FG / DQ / GD / tram, both draw orders, leaned, a floated tram. A reload on the same ground and on ground sunk 1.05 m writes nothing.

## Station ends
An end is a station end (approach floor, `LiftTowerPlanner.StationA/B`) when either of these holds:
- The census rule `LiftCensusSystem.IsStationNode` says so (`StationOfNode`: the node's Owner or owner's owner is a station, or,
  line-first, a station-owned stub edge is in its ConnectedEdge buffer).
- The node is any owned node.

Owner alone (before FIX-LIST 52) read a line-first end as a ground end in session and as a station after a load. The end rope is the
lowest end point of the end edge's HV sub-lanes (`StationRope`), which with a station attached is the station's own anchor.

## Never worse over a built line (FIX-LIST 52)
A line is BUILT when both of these hold:
- Every inner node's Elevation is exactly (E, E) of the member and head `MemberOf` reads from it.
- There is evidence: a node raised above E 0, or every node classified this session.

An engine line on the ground or the tool's fresh nodes are not BUILT. Over a built line, the fit writes its plan only in one of these two cases:
- The plan is sound (every span clear, no tower over its cap).
- The plan is no worse than the line as it stands (the "before" plan: its own members and heads, `HeadsAsGiven`). The comparison
  covers every one of these axes: floor margin (0.05 m), violated spans, towers over cap, worst excess over a cap.

(`LiftTowerPlanner.ToWrite`, `LiftTowerPlan.WorseThan`.) Otherwise the fit writes one WARN "KEPT AS BUILT" with both plans and the
axes. It writes nothing else. Field case: the Eishain FG line read -1.7 m after a load (stations attached line-first under ground-end
ropes). The Short fit wrote FG20BreakA at d +47.2, over its cap (LiftBatch EI-52). Trade-off: a built line that is floor-violated stays
so when every better arrangement puts a tower over its cap. The WARN names it, and the tool's per-section redo is the remedy.

## Moved
The free plan's own members travel with its nodes. Spares get the shortest clearing member (`LiftTowerPlanner.Fit`). A standing line within
`LiftTowerPlanner.KeepTolM` of the floor is kept (grid noise, not Short).

## Tool lines
The fit fits a line the lift-line tool built heights-only with the tool's members, never Moved. Exact match = `LiftToolLines.Match` (same node
count). When the engine split the tool's courses into more edges (CourseSplitSystem elevation transitions, :2884-2942),
`LiftToolLines.MatchSplit` still recognizes the line. The match needs every emitted node within 0.5 m, ends = the tool's, extras on the line. It gives each
extra split node the member that keeps the rope on the planned chord (`ChordMembers`). The fit writes one Info line per such match. It
writes a WARN in two cases:
- A tool line's pinned towers stay put on a Short line.
- A line holding a tool line's pinned nodes did NOT match and is Moved.

See `LiftLineEdit.md`.

## Heads (B2 twins)
The fit writes a tower's head through its height: E = `LiftTowerEdit.ElevationM(c, member, head)` (a twin's own top - TopM[0]). The
engine spawns that twin, a member of the same placeholder (LiftTowerPlanner.md "Heads"). `LiftTowerEdit.MemberOf(c, E, out head)` reads
the standing line's members and heads back (0.05 m match). The planner gets them as `PreviousKinds` / `PreviousHeads` (the fit's own
earlier kinds, else the head's kind) for the kind hysteresis. The "before" plan takes the standing heads as given (`HeadsAsGiven`), so a
line whose heads are still right is "already fitted" (LiftBatch HD-5). A head change alone re-members a tower. `LoadTwins`
(OnGameLoadingComplete) sets `LiftTowerPlanConstants.TwinFilter` to the twin prefabs the PrefabSystem has. It writes one "Towers [twins]"
line per load (n of 16 loaded, a WARN naming the missing ones). A build without them plans support heads only.

## Logs
Silence is impossible. The system writes:
- one ARMED line per load
- per fitted line, a "Towers [fit]" block (per tower: position, member, elevation, rope before to after. Per span: length, lowest
  carrier over the high side, max rope above ground), plus one "Towers [fit summary]"
- "already fitted": one line
- Short: a WARN with the tower count the line needs
- KEPT AS BUILT: a WARN (above)
- skips (branched / bent with a non-plain node / no constants): one Info line each

An exception disables the system until the next load.
