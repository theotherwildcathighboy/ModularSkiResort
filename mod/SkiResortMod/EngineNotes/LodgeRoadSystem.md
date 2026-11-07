# LodgeRoadSystem: why roadless top-level buildings need prefab flag writes, and what each write touches

This system writes road and access flags on our building prefab entities at every Game-mode load (decompile 1.6.0f1). Every file cited
here is byte-identical in 1.6.2f1.

## W4: top-level roadless buildings
Every SkiCatalog Lodges / Schools / Patrols row is a top-level building (no ServiceUpgradeData).
- Base rows (the school, the bus depot) are not written: the game sets RequireRoad from their demands
  (BuildingInitializeSystem.cs:209-300).
- RoadOptional rows (A-frame, mid, grand) demand utilities and workers, so the game sets RequireRoad and RequireAccess. W4 clears both
  with no WARN (by design). Placement snapping is unaffected: it reads PlacementFlags.RoadSide (ObjectToolSystem.cs:3574-3585).
  RoadConnectionSystem still links a road edge (:460 only marks it not required). ValidationHelpers.cs:19 no longer refuses an off-road
  lot. LodgeSupplySystem decides per building what such a lodge consumes (LodgeSupplySystem.md). It also puts back the mail
  and crime that RequireRoad would have given a lodge at a road (InitializeSystem.cs:143-153).
- Mountain and None rows (the hut, the summit lodge, both patrol huts) demand nothing (power / water / garbage 0, no Workplace), so the
  game sets no RequireRoad. But their Park component sets RequireAccess (BuildingInitializeSystem.cs:284-287). This arms the
  pathfind-island warning (ConnectionWarningSystem.cs:612-631) on a lot with no road and no lane.
- W4 clears RequireAccess on those prefab entities, recorded in m_Written. It also clears RequireRoad, with a WARN, should a demand come back.
  Park stays (the attraction walk and Tourism Overhaul need it).
- A row whose prefab carries ServiceUpgradeData gets one WARN and no write (the mod ships none).
- An expected row that is missing gets one WARN. Pending rows (not imported yet, SkiCatalog.IsExpected false) share one Info line.

## Why the write lasts
BuildingInitializeSystem runs only on prefab entities tagged Created and completes its flag job inside that one update. Prefab entities
live for the app run, and only prefab editing re-creates one. The readers take the prefab's value at every use, never a copy
(ConnectionWarningSystem, RoadConnectionSystem.cs:72-73 and :454-456, Game.Buildings/ValidationHelpers.cs:16-35). The precedent is
`CursorAimSystem` (a prefab component of our own net written at every load). A save carries no prefab flags (prefab entities read from a
save are mapped and destroyed).

## Editor
Because the write lasts for the app run, an Editor load after a game in the same run would keep the written flags. So an Editor load (mode
has GameMode.Editor) writes the game's own values back (m_Written's GameFlags). It does this only when the prefab still holds exactly what Apply wrote,
and it forgets the entry. The next game load writes again. The main menu keeps the flags (nothing is placed there), so a game, menu, game
cycle logs the "already" line.

## Logs
- "created"
- Per load in Game mode:
  - one line per W4 row (written / already),
  - one Info line naming the top-level rows left to the game's rule,
  - WARNs (prefab missing, RequireRoad set, ServiceUpgradeData),
  - a one-shot "!!!" if the write throws.
- In the Editor after a game: "LodgeRoad: restored the game's flags on prefab ... for the Editor (mode Editor): BuildingData 0x... -> 0x...; placement n/a", or one
  "nothing restored" line, or a "!!!".

## DUMP (called by the hub DUMP)
The DUMP lists, per lodge and per Welcome Sign, the road edge (entity and prefab name) or "none". It also lists "Lodge roads: n lodge(s), c with a road".
It only reads. A building is a lodge / sign when the prefab catalog says so (LodgePrefabSet / IsSign), not by entity equality.

## Write sites
The write is EntityManager.SetComponentData of vanilla BuildingData on the W4 prefab entities (ApplyRoadless). RestoreForEditor writes the game's values
back. The mod adds no component to any entity and removes none. We serialize nothing. There is no
prefab JSON change, no Harmony and no reflection.

## DUMP "Lodge [road]"
The DUMP lists, per lodge / school / patrol hut:
- which road edge Building.m_RoadEdge holds (entity, prefab name),
- the distance from the building's front point to that edge's boundary curves,
- the distance from the front point to the edge's center curve.

The boundary-curve distance is the RoadConnectionSystem.CheckEdge test (limit 8.4 m, RoadConnectionSystem.cs:88, :353-385). It uses edge
geometry only and ignores node geometry.

## Retired: W3 and the SkiCatTrack probe net (2026-10-26)
W3 cleared ClipTerrain on the SkiCatTrack RoadPrefab (the T54 CT1 probe net). The net was retired before 1.0 (it sat in the vanilla
Roads menu and the auto-laid net was never built), and W3 with it. The engine facts it rested on (NetInitializeSystem.cs:1943, the
TerrainSystem clip slab :1136-1143, :1698-1722) are in this file's git history.
