# HeightmapWriter: why the base terrain heightmap is the layer to write, and the exact write sequence

`HeightmapWriter` is the one place the mod writes the game's base terrain heightmap (used by `CatTrackGradeToolSystem` apply / undo and
its dev-tab probe). Engine citations are `TerrainSystem.cs:line` unless stated, against the Game.dll decompile baseline 1.6.0f1.

## Why the base map

`TerrainSystem` keeps two height layers. The base heightmap `m_Heightmap` (R16_UNorm RenderTexture, `SetHeightmap` :3231-3250) is the
only layer the save stores (`SerializeHeightmap` :3030-3056). The cascade the game renders and reads back to the CPU is re-blitted from
it with the net / lot / area overlays drawn on top (`RenderCascade` :4833-4925), so a cascade edit never lasts. The vanilla terrain
brush writes the base map: `ApplyBrush` (:3942-3954) -> `ApplyToTerrain` (:3715-3935, compute shader into `m_Heightmap`) ->
`UpdateMinMax` -> `TriggerAsyncChange`.

## The write (vanilla's own public calls, no reflection)

1. Read: `AsyncGPUReadback.Request(heightmap, mip 0, x, w, y, h, 0, 1).WaitForCompletion()`, the sub-rect form of the save's readback
   (:3043).
2. Write: a temporary Texture2D R16_UNorm of the rect, SetPixelData + Apply, then `Graphics.CopyTexture(src, 0, 0, 0, 0, w, h,
   heightmap, 0, 0, x, y)`: the sub-rect form of the copy `SetHeightmap` makes (:3250). Same format both sides (the D3D11 CopyTexture
   rule). Whether the sub-rect copy lands was unverified, so every write is verified by reading the rect back and comparing every texel
   (a mismatch is an ERROR and the caller restores the pre-image).
3. Tail: `WaterSystem.TerrainWillChangeFromBrush(area)` (`WaterSystem.cs:873-889`: pauses the water sim for 15 frames),
   `TerrainSystem.OnAreaChanged(area)` (:5096-5108: the cascade re-renders the area, `GroundHeightSystem` re-snaps trees / props),
   `UpdateMinMax(default(Brush), area)` (:3956), `TriggerAsyncChange()` (:3622: the CPU height readback refreshes).
   `ApplyToTerrain`'s `m_TerrainChange` / `m_TerrainChangeArea` are private and only read by `GetTerrainBrushUpdate` (:2642), which
   nothing in Game.dll calls: not needed.

## Format gate

Once per load (logged): the heightmap must be a created RenderTexture, R16_UNorm, at least 1024 texels a side, with a positive playable
area and height scale; anything else refuses every write (texture formats have changed without patch notes before). The mapping uses
`playableArea` / `playableOffset` / `heightScaleOffset`, never the 14,336 m defaults (custom maps).
