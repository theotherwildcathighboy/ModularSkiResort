# LodgeSupplySystem: how a road-optional lodge drops and regains city utilities, and why it runs in Modification1

Decompile baseline `Game.dll` 1.6.0f1. Pure rules: `LodgeSupplyRule.cs` (tests/BuildingBatch LS1-LS13, LC1-LC6). The prototype that
measured every step below lives in the test bridge (`tools/testbridge/TestBridgeSystem.Utility.cs`, the `offgrid` op).

## The rule
The A-frame, Chalet and Grand rows are `LodgeRoadRule.RoadOptional`. `LodgeRoadSystem` W4 clears `RequireAccess` and `RequireRoad` on
their prefab entities at every Game-mode load (and restores the game's flags for the Editor). So the game no longer asks for a road.
Per building:
- Road edge (`Building.m_RoadEdge`): vanilla's consumers, plus mail and crime (below).
- No road edge, but a cat track within `LiftServedRule.LinkMeters` (25 m) of the lot. The chain of tracks must reach a road lodge, or a Ski Bus
  Depot with a road edge, in the same ski area. Then power, water, garbage, mail and crime are off, with their icons and efficiency factors.
- Neither: vanilla's consumers stay, so the game's warnings and the lower efficiency apply.

## The chain (SkiResortAttractivenessSystem)
The walk lives in the attractiveness recompute because it already holds every input. The inputs are the member runs' outlines, areas and the cat track
mask (`m_RunOutlines`, `m_RunAreas`, `m_TrackRuns`) and the run link graph (`LiftServedRule.RunLinks`, links never cross areas).
- Roots: live, standing road lodges and depots with a road edge, by their lot rectangle. A road lodge is `LodgeSupplyRule.IsRoadLodge`: a Base or
  road-optional row with a road edge. A switched-off lodge still counts, because it still has its road. The lot rectangle comes from
  `BuildingUtils.CalculateCorners` (Game.Buildings BuildingUtils.cs:72). A Grand lot is 80 x 72 m, so the 25 m is measured from the lot, not the center.
- `LodgeSupplyRule.Reach`: tracks within 25 m of a root, then along the links over tracks only. A lodge is supplied when a reached track
  of its area is within 25 m of its lot.
- The walk runs only when its inputs change. The inputs are a signature over the link graph's rebuild count, the track mask, and every root's and off-road
  lodge's lot and area. For everyone else, the answer is a dictionary read (`IsSupplied`).
- The system holds a lost answer for `LodgeSupplyRule.HoldFrames` (10923 sim frames, one game hour). So a section redo (delete and redraw a track
  section) does not flip the lodge. Frames stop while paused. A true answer publishes at once. A lodge at a road reads not supplied with
  no hold, so it never counts as its own root when its road goes.
- First sight after a load: the shape the save holds (no `ElectricityConsumer` = supplied). So a load changes nothing before the walk.
- The road bit and the answer are mixed into the state poll's hash. So a change asks for a recompute, and the ski-day targets, the parking
  census, the carrier and the hub follow.

## The switch (per frame, Modification1)
`Game.Buildings.InitializeSystem` (Modification2, SystemOrder.cs:118) adds a building's consumers when it carries `Updated`:
- :132-135 returns for a destroyed building that is not Created.
- :173 skips Created, Abandoned and a prefab without ObjectData.
- :178-208 adds `ElectricityConsumer`, `WaterConsumer` and `GarbageProducer` only when the prefab's archetype has them.
- :143-153: `CrimeProducer` only at Created with ConsumptionData and `RequireRoad`. `MailProducer` with `RequireRoad`. W4 clears
  `RequireRoad`, so a road-optional lodge never gets these two from the game. The road branch adds them itself, one by one.

So the system runs in Modification1. An `Updated` written there reaches `InitializeSystem` the same frame (ModificationSystem.cs:19-26
runs the Modification phases in order inside MainLoop, SystemOrder.cs:60). It also lands before `PrepareCleanUpSystem` collects the tag for the cleanup
(SystemOrder.cs:50-54, PrepareCleanUpSystem.cs:48-52). The system runs paused or not.

Per watched lodge (the attractiveness system's list, rebuilt at every load because entity ids remap):
- Skipped: Temp, Deleted, Destroyed, Abandoned. Vanilla owns those. `InitializeSystem` would skip them, and the road branch would tag
  `Updated` forever.
- Road or Unsupplied with a consumer missing: the system tags `Updated` once. It tags again only after `LodgeSupplyRule.RetagUpdates` (256) updates while
  the consumer stays missing. Otherwise a prefab whose archetype lacks one would be tagged every frame.
- Road: the system adds `MailProducer` and `CrimeProducer` when missing. A relocate back onto a road re-adds the first three only.
- OffGrid: the system removes whichever of the five is present, then the icons and factors below. The check runs every frame because vanilla
  re-adds the three on any `Updated` of the lodge (a Move It drag). The system counts that re-add (DUMP "re-adds") and does not log it.

Cleanup after a removal (the writers of these values skip a building without the consumer, so the last value would stay):
- Icons through `IconCommandBuffer.Remove` (applied by `IconCommandSystem` at ModificationEnd, SystemOrder.cs:271):
  - `ElectricityParameterData.m_ElectricityNotificationPrefab`
  - `.m_BuildingBottleneckNotificationPrefab`
  - `WaterPipeParameterData.m_WaterNotification`
  - `.m_DirtyWaterNotification`
  - `.m_SewageNotification`
  - `GarbageParameterData.m_GarbageNotificationPrefab`
  - `TrafficConfigurationData.m_RoadConnectionNotification`
- Factors set to 1 (`BuildingUtils.SetEfficiencyFactor`): ElectricitySupply, ElectricityFee, WaterSupply, DirtyWater, SewageHandling,
  WaterFee, Garbage, Mail.

What stays: `TelecomConsumer` (coverage does not need a road), `MaintenanceConsumer` and `Park` (maintenance vans cannot reach an
off-road lodge, the same as the Hut and Summit today).

Other readers of `RequireRoad` [from code, not run]:
- `ObjectColorSystem.cs:1079`: the telecom infoview color only. An off-road lodge loses it like the Hut and Summit.
- `Game.Simulation/CollapsedBuildingSystem.cs:110-113`: collapse clearing. It is the same path as the Hut and Summit. The effect is UNVERIFIED.

## Jobs (b1)
When the lodge is off-road and supplied, its own jobs leave for the area's carrier (`MountainJobsRules` header). `MountainJobsSystem` adds them to
the carrier's sum. `OffSeasonStaffSystem` writes the lodge 0 and waits for the game's tick to lay the staff off. Then it removes
`WorkProvider` and `FreeWorkplaces` (the shape the prototype measured: no worker icons, efficiency 1). Back at a road or off the chain, the
`WorkProvider` returns. See `OffSeasonStaffSystem.md`.

## Logs
- "created".
- ARMED once per load.
- One "LodgeSupply [switch] <tier> <id>: <before> -> <after> (<what changed>; raw, supplied, holding)"
  per mode change, including the first sight after a load.
- "!!!" once on a throw (then frozen until the next load).
- DUMP: per lodge, the mode, the five consumers (EWGMC), the answer and its hold, the Updated tags and the vanilla re-adds. DUMP also lists the chain walks of this load.
