# LiftTowerDiagSystem: what the tower diagnostic logs, and the engine's tower-member scoring

`LiftTowerDiagSystem` is a read-only diagnostic of what the engine did on a lift line, plus the plan cache of the tower-plan overlay. It
changes nothing: no command buffer, no component write, no prefab touch, no per-frame work (an int compare while nothing is pending).

## Triggers
- `LiftCensusSystem.BuildCensus` calls `OnCensusBuilt(edges)` after every build (load baseline included). The system signs components of
  lift-line edges (edge ids + node positions). It queues a component whose signature was not logged yet, and drops that line's cached
  plan (the overlay re-asks for it).
- With Options > Diagnostics > Measurement diagnostics OFF, the queue is cleared without planning (opt pass 2026-11-14 lifts-01): a line
  edit costs no planner run. The overlay plans on demand (below).
- With the option ON, the system prints a queued line after the census stays quiet for SettleFrames updates. A drag keeps resetting the
  timer, so it prints once, not once per build. At most MaxBlocksPerEmit lines per emission (the rest counted in one line).
- `DumpAll(reason)`: every line of the last census build, on demand (hub DUMP, dev builds), capped at MaxBlocksPerDump.

## Output
One "Towers [summary]" line per lift line: node and span count, curve length, the towers the engine spawned (prefab x count, carriers
skipped), nodes without a tower, the highest node Elevation (cmin of Game.Net.Elevation, the value the engine uses, SubObjectSystem.cs:980),
the largest tower pivot height above the terrain (leg gap) and the lowest chord clearance (4 samples per span, t = .2 .4 .6 .8).
The per-node "Towers [nodes]" dump (per node / span / tower detail, the placeholder member scores and MATCH / MISMATCH, the T123 order,
station stub and own-cut lines) was cut to this line in the 2026-11-14 opt pass (lifts-08 b).

## Engine formula (decompile 1.6.0f1, Game.Objects/SubObjectSystem.cs; reference, no longer replicated in this system)
- Per placeholder member (PillarData Standalone / Base / Vertical, :2173-2215): spare = (m_Size.y - PlacementOffset.y) - (ownerElevation +
  local-to-world dy of the spec position).
- score = 1 / (1 + (spare >= 0 ? 2*spare : -spare)) (:2182-2184, :2197-2199, :2212-2214).
- Best score wins. Equal scores break by probability-weighted random (:2336-2358).
- ownerElevation = cmin(Game.Net.Elevation) (:980).
- `LiftTowerEdit.EngineScore` / `EngineWinner` (LiftTowerPlanner.cs) are the shipped implementation of this rule.

## Plan
With the option on, after each summary line, the pure `LiftTowerPlanner` re-plans each path of the line. It plans on the real terrain
(TerrainUtils.SampleHeight on TerrainSystem.GetHeightData(), TerrainUtils.cs:82 / TerrainSystem.cs:2603) along the node polyline (xz). It
takes both ends as stations. The rope is at node y + the rope above the head the node carries (`RopeAboveNode`). `RopeAboveNode` is the
shortest member's rope for a Line head, lower at a twin.
- It logs "Towers [plan]" and one "Towers [plan summary]" line: the planned vs actual node count, per planned node the member, rope and
  flags, and per span the lowest carrier above ground and the max rope height.
- It caches the planned geometry for `LiftTowerPlanOverlaySystem`, keyed by every edge and node entity of the line. Selecting a line whose
  plan is not cached queues the line here (RequestPlan, served next update, also with the option off).

## Registration
Mod.cs: `UpdateAfter<LiftTowerDiagSystem, LiftCensusSystem>(Modification4B)`, the same anchor family as `TramShuttleSystem`.
