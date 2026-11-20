# LvServiceBackfillSystem: why old saves need a poke for the LV service, and why it is safe

The 1.0.4 LV service adds rows to each base station prefab:
- a vanilla `PowerLinePole8m01` and an `ElectricalBox02` (two `m_Meshes` rows)
- three single-point LV wire anchors at the pole's insulators (node indexes 10-12)
- a two-point riser down the pole face (node indexes 13-14).

Gate LV-7 (tests/LiftBatch EishainTests) pins the rows and the FindAnchors margins.

## What reaches an old save by itself, and what does not (decompile 1.6.x)
- Mesh rows and prefab sub-lane rows come live from the prefab. A placed station shows the pole and the box at load.
- New or redrawn wires land on the insulators.
- An existing wire span keeps its old mid-air end. LaneSystem rebuilds the lanes of an edge only when the edge is in m_OwnerQuery
  (All{SubLane} + Any{Updated, Deleted}). Only then does FindAnchors run again. Nothing on load tags the edge.
- The riser is a lane entity that the station owns (a two-point row). LaneSystem builds the lanes of an owner when it is Created or
  Updated (LaneSystem.cs:740-787). An old station has no riser until something tags it.

## What the system tags, once per load (GameMode.Game)
1. The LV edges at the station's LV stub. Each edge must meet all of these conditions:
   - The station does not own it, and it ends at a node of the LV stub (the AtNode wires of StationLvStubProbe).
   - It has a wire lane of the anchor lane prefab (an aerial LV line).
   - None of its wire ends is within 0.3 m of an insulator yet.

   The system also tags the two end nodes of each edge. This is the redraw set. LiftReconnectSystem pokes the same entity kinds in
   production.
2. With `RiserBackfill`: each base station that owns fewer `Game.Net.UtilityLane` sub-lanes than its prefab has two-point utility rows.

Both conditions are false after the rebuild, so the next load tags nothing. There is no persisted state and no SaveLayouts type.

## Scheduling (load-bearing)
The registration is `UpdateAfter<LvServiceBackfillSystem, LiftReconnectSystem>(Modification4)`.
- UpdateSystem.AddSystemUpdate puts a system that is registered "after X" directly after X.
- LiftReconnectSystem is registered directly before LaneSystem.
- The tags are immediate EntityManager writes, so they land before LaneSystem in the same frame. A barrier command buffer plays back
  later.
- LiftReconnectSystem ran earlier in this frame. It never sees the station tag as a station event. There is no reconnect poke, no chair
  full pass and no episode log.
- Updated is cleared at the end of the frame.

## Who else sees a station tagged Updated at Modification4
Only the systems later in the frame see the tag:
- LaneSystem: the riser and the other station lanes. It matches unchanged lanes and keeps them.
- Modification4B: Buildings.ReferencesSystem, BuildingPoliciesSystem, SecondaryObjectSystem, SecondaryLaneSystem, PowerBridgeSystem,
  ElectricityBuildingGraphSystem and WaterPipeBuildingGraphSystem.
- Modification5: the update-collect and search systems. Then the rendering batches.

SubObjectSystem (Modification2B) and Buildings.InitializeSystem (Modification2) ran earlier in the frame. They do not regenerate
sub-objects or sub-nets. The load pass of PowerBridgeSystem also tags stations Updated when it creates valves. So this path has
production precedent.

Probe P1 (lv-connect PLAN §6) checks the path on a save copy. It compares sub-objects, sub-nets, stub nodes, valves, wires, power and
errors before and after a station Updated. If P1 shows a side effect, `RiserBackfill` changes to false. The wire backfill stays.

P1 result (2026-11-20, a copy of the 1.0.3 baseline save, 8 base stations): the log line said "8 riser backfill(s) ... 8 entities tagged
Updated". Sub-objects, sub-nets, stub nodes, valves (42) and errors did not change. But no station got the riser: a dump of all 21456
UtilityLane entities found none with a base station as owner. So `RiserBackfill` is false: a tag with no result is not shipped. The
cause is not proven. Stations placed before 1.0.4 show the pole and the box without the riser. Open check: does a NEW placement get
the riser lane? If it does not either, the riser row has no effect anywhere.
