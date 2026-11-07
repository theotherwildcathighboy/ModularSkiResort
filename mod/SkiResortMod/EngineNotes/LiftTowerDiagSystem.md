# LiftTowerDiagSystem: what the tower diagnostic logs and the engine's tower-member scoring it replicates

`LiftTowerDiagSystem` is a read-only diagnostic of what the engine actually did at every node of a lift line. It changes nothing: no
command buffer, no component write, no prefab touch, no per-frame work (an int compare while nothing is pending).

## Triggers
- `LiftCensusSystem.BuildCensus` calls `OnCensusBuilt(edges)` after every build (load baseline included). The system signs components of
  lift-line edges (edge ids + node positions). It queues a component whose signature was not logged yet.
- The system prints a queued line after the census stays quiet for SettleFrames updates. A drag keeps resetting the timer, so it prints
  one block, not one per build. It prints at most MaxBlocksPerEmit blocks per emission (the rest counted in one line).
- `DumpAll(reason)`: every line of the last census build, on demand, capped at MaxBlocksPerDump.

## Output
The log tag "Towers [nodes]" has one block per line, <= MaxNodes nodes. The log tag "Towers [summary]" has one line per line.
- Per node: world y, terrain y under it, clearance, net Elevation component (float2. The engine uses cmin of it, SubObjectSystem.cs:980).
- Per span to the next node:
  - the min chord clearance over 4 samples (t = .2 .4 .6 .8) and where,
  - the largest Curve droop below/above the chord.
- The tower sub-object(s) in the node's SubObject buffer: prefab, entity, mesh top, ObjectGeometryData size / bounds / leg / flags,
  Elevation, pivot height above terrain.
- The placeholder: every member with its m_Size.y and the engine formula's score, the winner marked, plus MATCH / MISMATCH against the
  prefab actually spawned.

## Engine formula replicated (decompile 1.6.0f1, Game.Objects/SubObjectSystem.cs)
- Per placeholder member (PillarData Standalone / Base / Vertical, :2173-2215): spare = (m_Size.y - PlacementOffset.y) - (ownerElevation +
  local-to-world dy of the spec position).
- score = 1 / (1 + (spare >= 0 ? 2*spare : -spare)) (:2182-2184, :2197-2199, :2212-2214).
- Best score wins. Equal scores break by probability-weighted random (:2336-2358).
- ownerElevation = cmin(Game.Net.Elevation) (:980).
- Not replicated (effect unverified): CheckRequirements per member (:2148), the +0.5 for a group already selected this update (:2331).

## Plan
After every "Towers [nodes]" block, the pure `LiftTowerPlanner` re-plans each path of the line. It plans on the real terrain
(TerrainUtils.SampleHeight on TerrainSystem.GetHeightData(), TerrainUtils.cs:82 / TerrainSystem.cs:2603, the same main-thread read the
[nodes] block uses) along the node polyline (xz). It takes both ends as stations. The rope is at node y + the rope above the head the node
carries (`RopeAboveNode`). `RopeAboveNode` is the shortest member's rope for a Line head, lower at a twin.
- It logs "Towers [plan]" and one "Towers [plan summary]" line. The first has the planned vs actual node count, per planned node the
  member, rope and flags, and per span the lowest carrier above ground and the max rope height.
- It caches the planned geometry for `LiftTowerPlanOverlaySystem`, keyed by every edge and node entity of the line. Selecting a line whose
  plan is not cached queues the plan here (RequestPlan, next update).

## Registration
Mod.cs: `UpdateAfter<LiftTowerDiagSystem, LiftCensusSystem>(Modification4B)`, the same anchor family as `TramShuttleSystem`.
