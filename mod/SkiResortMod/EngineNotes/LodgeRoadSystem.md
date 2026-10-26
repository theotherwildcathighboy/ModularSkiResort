# LodgeRoadSystem: why roadless top-level buildings need prefab flag writes, and what each write touches

Road and access flags on our building prefab entities, written at every Game-mode load (decompile 1.6.0f1; every file cited
here is byte-identical in 1.6.2f1).

## W4: top-level roadless buildings
Every SkiCatalog Lodges / Schools / Patrols row is a top-level building (no ServiceUpgradeData).
- Base rows (A-frame, mid, grand, the school) are not written: the game sets RequireRoad from their demands
  (BuildingInitializeSystem.cs:209-300).
- Mountain and None rows (the hut, the summit lodge, both patrol huts) demand nothing (power / water / garbage 0, no Workplace), so the
  game sets no RequireRoad, but their Park component sets RequireAccess (BuildingInitializeSystem.cs:284-287), which arms the
  pathfind-island warning (ConnectionWarningSystem.cs:612-631) on a lot with no road and no lane.
- W4 clears RequireAccess (and RequireRoad, with a WARN, should a demand come back) on those prefab entities, recorded in m_Written.
  Park stays (the attraction walk and Tourism Overhaul need it).
- A row whose prefab carries ServiceUpgradeData gets one WARN and no write (the mod ships none).
- An expected row that is missing gets one WARN; pending rows (not imported yet, SkiCatalog.IsExpected false) share one Info line.

## Why the write lasts
BuildingInitializeSystem runs only on prefab entities tagged Created and completes its flag job inside that one update; prefab entities
live for the app run and only prefab editing re-creates one. The readers take the prefab's value at every use, never a copy
(ConnectionWarningSystem, RoadConnectionSystem.cs:72-73 and :454-456, Game.Buildings/ValidationHelpers.cs:16-35). The precedent is
`CursorAimSystem` (a prefab component of our own net written at every load). A save carries no prefab flags (prefab entities read from a
save are mapped and destroyed).

## Editor
Because the write lasts for the app run, an Editor load after a game in the same run would keep the written flags. So an Editor load (mode
has GameMode.Editor) writes the game's own values back (m_Written's GameFlags), only when the prefab still holds exactly what Apply wrote,
and forgets the entry: the next game load writes again. The main menu keeps the flags (nothing is placed there), so a game, menu, game
cycle logs the "already" line.

## Logs
"created"; per load in Game mode one line per W4 row (written / already), one Info line naming the top-level rows left to the game's rule,
WARNs (prefab missing, RequireRoad set, ServiceUpgradeData), and a one-shot "!!!" if the write throws. In the Editor after a game:
"LodgeRoad: restored the game's flags on prefab ... for the Editor (mode Editor): BuildingData 0x... -> 0x...; placement n/a", or one
"nothing restored" line, or a "!!!".

## DUMP (called by the hub DUMP)
Per lodge and per Welcome Sign the road edge (entity and prefab name) or "none", and "Lodge roads: n lodge(s), c with a road". Reads only.
A building is a lodge / sign when the prefab catalog says so (LodgePrefabSet / IsSign), not by entity equality.

## Write sites
EntityManager.SetComponentData of vanilla BuildingData on the W4 prefab entities (ApplyRoadless; RestoreForEditor writes the game's values
back). No component is added to or removed from any entity, nothing is serialized by us, no
prefab JSON change, no Harmony, no reflection.

## DUMP "Lodge [road]"
Per lodge / school / patrol hut: which road edge Building.m_RoadEdge holds (entity, prefab name), the distance from the building's front
point to that edge's boundary curves (the RoadConnectionSystem.CheckEdge test, limit 8.4 m, RoadConnectionSystem.cs:88, :353-385; edge
geometry only, node geometry ignored) and to its center curve.

## Retired: W3 and the SkiCatTrack probe net (2026-10-26)
W3 cleared ClipTerrain on the SkiCatTrack RoadPrefab (the T54 CT1 probe net). The net was retired before 1.0 (it sat in the vanilla
Roads menu and the auto-laid net was never built), and W3 with it. The engine facts it rested on (NetInitializeSystem.cs:1943, the
TerrainSystem clip slab :1136-1143, :1698-1722) are in this file's git history.
