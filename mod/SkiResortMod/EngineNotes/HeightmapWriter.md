# HeightmapWriter: why the base terrain heightmap is the layer to write, and the exact write sequence

`HeightmapWriter` is the one place where the mod writes the game's base terrain heightmap. `CatTrackGradeToolSystem` uses it for apply,
undo and its dev-tab probe. Engine citations are `TerrainSystem.cs:line` unless stated, against the Game.dll decompile baseline 1.6.0f1.

## Why the base map

`TerrainSystem` keeps two height layers.

- The base heightmap `m_Heightmap` (R16_UNorm RenderTexture, `SetHeightmap` :3231-3250) is the
  only layer the save stores (`SerializeHeightmap` :3030-3056).
- The cascade is the layer the game renders and reads back to the CPU. The game re-blits it from the base heightmap and draws the
  net / lot / area overlays on top (`RenderCascade` :4833-4925). A cascade edit therefore never lasts.

The vanilla terrain brush writes the base map: `ApplyBrush` (:3942-3954) -> `ApplyToTerrain` (:3715-3935, compute shader into
`m_Heightmap`) -> `UpdateMinMax` -> `TriggerAsyncChange`.

## The write (vanilla's own public calls, no reflection)

1. Read: `AsyncGPUReadback.Request(heightmap, mip 0, x, w, y, h, 0, 1).WaitForCompletion()`. This is the sub-rect form of the save's
   readback (:3043).
2. Write: make a temporary Texture2D R16_UNorm of the rect, call SetPixelData + Apply, then call
   `Graphics.CopyTexture(src, 0, 0, 0, 0, w, h, heightmap, 0, 0, x, y)`. This is the sub-rect form of the copy that `SetHeightmap`
   makes (:3250). Both sides use the same format (the D3D11 CopyTexture rule). It was unverified whether the sub-rect copy lands.
   So the code verifies every write: it reads the rect back and compares every texel. A mismatch is an ERROR and the caller restores
   the pre-image.
3. Tail: call these in order.
   - `WaterSystem.TerrainWillChangeFromBrush(area)` (`WaterSystem.cs:873-889`): pauses the water sim for 15 frames.
   - `TerrainSystem.OnAreaChanged(area)` (:5096-5108): the cascade re-renders the area, and `GroundHeightSystem` re-snaps trees / props.
   - `UpdateMinMax(default(Brush), area)` (:3956).
   - `TriggerAsyncChange()` (:3622): the CPU height readback refreshes.

   `ApplyToTerrain`'s `m_TerrainChange` / `m_TerrainChangeArea` are private. Only `GetTerrainBrushUpdate` (:2642) reads them, and
   nothing in Game.dll calls it. The mod does not need them.

## Format gate

The gate runs once per load and logs its result. The heightmap must meet all of these conditions:

- It is a created RenderTexture.
- Its format is R16_UNorm.
- It is at least 1024 texels a side.
- It has a positive playable area and height scale.

If any condition fails, the gate refuses every write. Texture formats have changed without patch notes before. The mapping uses
`playableArea` / `playableOffset` / `heightScaleOffset`, never the 14,336 m defaults (custom maps).
