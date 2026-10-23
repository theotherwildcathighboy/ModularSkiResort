# LodgeRoadSystem: why roadless top-level buildings and the cat track need prefab flag writes, and what each write touches

Road and access flags on our building and cat-track prefab entities, written at every Game-mode load (decompile 1.6.0f1; every file cited
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
back) and of NetGeometryData on the cat track (W3). No component is added to or removed from any entity, nothing is serialized by us, no
prefab JSON change, no Harmony, no reflection.

## Near the cat track probe (W3)
SkiCatTrack is a RoadPrefab (JSON only, ImportedData\SkiCatTrack*) with a geometry-less piece, so the road draws nothing and the terrain
shows. Two additions:
- W3, NetGeometryData.m_Flags on the SkiCatTrack prefab entity: clear GeometryFlags.ClipTerrain (Game.Net, 0x40000) at every Game-mode load.
  - NetInitializeSystem.cs:1943 ORs FlattenTerrain | ClipTerrain into every RoadPrefab; the terrain system turns ClipTerrain into a +-0.3 m
    clip slab along each lane section (TerrainSystem.cs:1136-1139, GetFlags; GenerateClipDataJob :1698-1722) that vanilla fills with the
    road mesh. A geometry-less piece would leave that slab see-through.
  - FlattenTerrain is kept (CatTrackClearFlatten = false): it makes the terrain follow the net cross-section (the ShiftTerrain lane flag,
    :1140-1143), so the 3 m strip is a level bench and the lane curves sit on the ground. Cleared, a cross-slope would leave the flat lane
    ribbon hanging above/below the sloping ground by up to 1.5 m x slope, and GroundHeightSystem / LotHeightSystem stop treating the net as
    a flat surface (they test the same flag, GroundHeightSystem.cs:322, LotHeightSystem.cs:286). If a bench scar proves unacceptable, flip
    CatTrackClearFlatten and re-test (then lane Y follows the net's own heights: cars may float).
  - Read live by the terrain jobs (the prefab component is looked up at job time), so the write needs no Updated tag.
  - One line per load: "CatTrack: prefab SkiCatTrack NetGeometryData ...". A prefab without the flag the game should have set (and no
    earlier write by us) writes nothing and logs one WARN. Not restored for the Editor (the net is a placeable probe road; nothing in the
    Editor uses it).
- DUMP "Lodge [road]": per lodge / school / patrol hut, which road edge Building.m_RoadEdge holds (entity, prefab name, "= CAT TRACK" when
  it is SkiCatTrack), the distance from the building's front point to that edge's boundary curves (the RoadConnectionSystem.CheckEdge
  test, limit 8.4 m, RoadConnectionSystem.cs:88, :353-385; edge geometry only, node geometry ignored) and to its center curve, and, when
  the edge is not a cat track, the nearest cat-track edge in reach (so a lodge beside a cat track that a vanilla road beat, or that the
  Raised/Lowered rule dropped, shows).
