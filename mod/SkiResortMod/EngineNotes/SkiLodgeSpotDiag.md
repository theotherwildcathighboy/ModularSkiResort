# SkiLodgeSpotDiag: which spawn spots the engine registers for a lodge

What "registered" means (decompile 1.6.0f1):

- The buffer is `Game.Buildings.SpawnLocationElement {m_SpawnLocation, m_Type}` on the building entity (added to every building
  archetype, Game.Prefabs/BuildingPrefab.cs:45). `Game.Buildings.ReferencesSystem` (Modification4B, SystemOrder.cs:176) adds every
  Created, Owned, non-Secondary entity carrying `Game.Objects.SpawnLocation`, `Game.Areas.HangaroundLocation` or
  `Game.Net.ParkingLane` to the buffer of its owner and of every owner above it (ReferencesSystem.cs:51-117), so a lodge's spots
  also sit in the Welcome Sign's buffer. On load `Game.Serialization.SpawnLocationElementSystem` (Deserialize, SystemOrder.cs:818)
  rebuilds it the same way (the buffer is `IEmptySerializable`).
- `PathfindTargetSeeker.FindTargets` offers exactly the entries of this buffer as the building's targets
  (PathfindTargetSeeker.cs:429-481, :530-740): a SpawnLocation by its prefab's `SpawnLocationData` (`m_ConnectionType`,
  `m_ActivityMask`: offered only when the mask is 0 or intersects the visitor's, :653), a HangaroundLocation area by its
  `Game.Net.SubLane` connection lanes (:700-720). An unlinked spot does not switch off the road-front door; a linked one does
  (:720, :729).
- A prop prefab with `Game.Prefabs.ActivityLocation` (bench, chair, table set) becomes itself a `Game.Objects.SpawnLocation` +
  `ActivityLocation` entity whose `SpawnLocationData.m_ActivityMask` is the OR of its seats' masks
  (Game.Prefabs/ActivityLocation.cs:60-67, :105-125); owned by the lodge, it registers like a door marker.
- A Pedestrian Access Location marker is not a SpawnLocation (`Game.Routes.TakeoffLocation`): never in the buffer; it joins an
  area's lanes to the street (Game.Areas/AreaConnectionSystem.cs, Modification4B, :179).

Unverified: that every spot the buffer holds is offered to a given visitor (needs the visitor's activity mask), and what the area's
connection lanes join on the street side (read the Pathfind Debug gizmo in game).
