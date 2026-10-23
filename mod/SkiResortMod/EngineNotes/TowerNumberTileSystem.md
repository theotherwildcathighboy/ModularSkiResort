# TowerNumberTileSystem: how number tile objects are created under towers and swept again by the engine

Per numbered tower (`TowerNumberTiles.Plates`), per plate face, 1-3 one-quad tile objects (SkiNumTile<cls>Blank / N0..N9,
assets/blender/number_tiles.py) cover the baked height numeral with the tower's order number 1..200 (wrapping). The tiles are vanilla
object entities: Owner = the tower, Secondary, Elevation(local y, flags 0). The baked plate is the fallback: with the tile prefabs
absent this system writes nothing and logs one line per load.

## Engine facts (decompile 1.6.0f1)

- Secondary children are skipped by SubObjectSystem's sweeps (SubObjectSystem.cs:399, :1179, :1232) and swept by SecondaryObjectSystem
  only when their owner is Updated / Deleted AND has a SubObject buffer (SecondaryObjectSystem.cs:727-749, :1159-1212, query
  :4073-4083): tiles survive node / edge passes beside them, die with their tower and are swept when the tower itself is Updated (we
  re-create them: the updated-tower probe below).
- The references systems index m_SubObjects[owner] unguarded for a Created child (SecondaryObjectReferencesSystem.cs:62,72), and our
  tower prefabs have no ObjectSubObjects, so their instances have no buffer: AddBuffer<SubObject>(tower) BEFORE the first tile
  (precedent SkiRunPropSystem :342). The buffer is IEmptySerializable and refilled from Owner on load (SubObject.cs:7-8,
  Game.Serialization/SubObjectSystem.cs:24-60). Unverified in game: a buffer added at run time on a tower survives save / load.
- Recipe: CreateEntity(ObjectData.m_Archetype) + Owner + PrefabRef + Transform (SubObjectSystem.cs:2824-2827); the archetype carries
  Created / Updated / PseudoRandomSeed / culling (ObjectPrefab.cs:28-46). Created at 4B, the tile is filed into the tower's buffer by
  SecondaryObjectReferencesSystem at Modification5 the same frame (SystemOrder.cs:206).
- Elevation(h, flags 0) keeps GroundHeightSystem from snapping the tile to the ground (GroundHeightSystem.cs:216-243, :234).
- Delete = AddComponent<Deleted> (the sweep's own write, SubObjectSystem.cs:1169-1193).

## Triggers

`LiftCensusSystem.BuildCensus` -> OnCensusBuilt(edges) after every build (load baseline included); the pass runs once the census has
been quiet SettleFrames updates (a drag reconciles once). Plus an updated-tower probe: an Updated owned object with a SubObject buffer
whose prefab has a plate row is exactly the case SecondaryObjectSystem sweeps (:1159-1212), so it re-arms the pass (a Deleted-tile
probe would miss: the sweep's Deleted lives only from the 4B barrier to Cleanup). Idle frames: one counter and one IsEmptyIgnoreFilter.

## Order per line

`LiftTowerDiagSystem.Components` / `Paths`, towers on interior nodes (first non-carrier sub-object of the node), xz distance from the
BASE station (the path end whose station role is Base; reversed if the first end is the Top), `LiftTowerNumbering` + wrap. A line with
no station role at either end is left alone. An edit renumbers only the towers whose number changed.
