# TowerNumberTileSystem: how number tile objects are created under towers and swept again by the engine

For each numbered tower (`TowerNumberTiles.Plates`) and each plate face, 1-3 one-quad tile objects (SkiNumTile<cls>Blank / N0..N9,
assets/blender/number_tiles.py) cover the baked height numeral. They show the tower's order number 1..200 (wrapping). The tiles are
vanilla object entities: Owner = the tower, Secondary, Elevation(local y, flags 0). The baked plate is the fallback. If the tile
prefabs are absent, this system writes nothing and logs one line per load.

## Engine facts (decompile 1.6.0f1)

- SubObjectSystem's sweeps skip Secondary children (SubObjectSystem.cs:399, :1179, :1232).
  SecondaryObjectSystem sweeps them only when their owner is Updated / Deleted AND has a SubObject buffer
  (SecondaryObjectSystem.cs:727-749, :1159-1212, query :4073-4083). The results:
  - Tiles survive node / edge passes beside them.
  - Tiles die with their tower.
  - The engine sweeps tiles when the tower itself is Updated. We re-create them (see the updated-tower probe below).
- The references systems index m_SubObjects[owner] without a guard for a Created child (SecondaryObjectReferencesSystem.cs:62,72).
  Our tower prefabs have no ObjectSubObjects, so their instances have no buffer. The system therefore calls AddBuffer<SubObject>(tower) BEFORE
  the first tile (precedent SkiRunPropSystem :342). The buffer is IEmptySerializable. The engine refills it from Owner on load
  (SubObject.cs:7-8, Game.Serialization/SubObjectSystem.cs:24-60). Unverified in game: a buffer added at run time on a tower survives
  save / load.
- Recipe: CreateEntity(ObjectData.m_Archetype) + Owner + PrefabRef + Transform (SubObjectSystem.cs:2824-2827). The archetype carries
  Created / Updated / PseudoRandomSeed / culling (ObjectPrefab.cs:28-46). The system creates the tile at 4B. In the same frame,
  SecondaryObjectReferencesSystem files the tile into the tower's buffer at Modification5 (SystemOrder.cs:206).
- Elevation(h, flags 0) keeps GroundHeightSystem from snapping the tile to the ground (GroundHeightSystem.cs:216-243, :234).
- Delete = AddComponent<Deleted> (the sweep's own write, SubObjectSystem.cs:1169-1193).

## Triggers

- `LiftCensusSystem.BuildCensus` -> OnCensusBuilt(edges) after every build (the load baseline included). The pass runs once the census
  has been quiet for SettleFrames updates. A drag reconciles once.
- An updated-tower probe re-arms the pass. It looks for an Updated owned object with a SubObject buffer whose prefab has a plate row.
  This is exactly the case that SecondaryObjectSystem sweeps (:1159-1212). A Deleted-tile probe would miss it, because the sweep's
  Deleted lives only from the 4B barrier to Cleanup.

Idle frames: one counter and one IsEmptyIgnoreFilter.

## Order per line

The tower order on a line comes from these inputs:

- `LiftTowerDiagSystem.Components` / `Paths`.
- Towers on interior nodes (first non-carrier sub-object of the node).
- The xz distance from the BASE station. This is the path end whose station role is Base. The system reverses the path if the first
  end is the Top.
- `LiftTowerNumbering` + wrap.

A line with no station role at either end is left alone. An edit renumbers only the towers whose number changed.
