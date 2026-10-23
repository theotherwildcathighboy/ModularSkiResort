# SkiRunSplatSystem: how ski runs are painted into the terrain-material splatmap, which channel it claims and when it repaints

`SkiRunSplatSystem` mirrors painted ski-run polygons into the game's terrain-material user splatmap, so runs render as a real terrain
material (triplanar, relief-shaded, vanilla snow stacks on top) instead of a top-down decal smear. Polygons are data, splat is paint:
the `RenderedArea` polygons stay the gameplay layer; this system only paints, and repaints the whole claimed channel on any change.

## Machinery

Line refs are `TerrainMaterialSystem.cs` (`Game.dll` decompile).

* `ApplyBrush(Brush, brushPrefabEntity)` is public (:815). It stamps one dab into `m_UserSplatmap` (4096^2, R8G8_UNorm, :758) and
  `m_UserSplatmapWorld` (1024^2) via the PaintDither/EraseDither kernels: strength > 0 paints, < 0 erases (:872). Both splatmaps
  serialize into the save; we rebuild the channel from polygons every load anyway (see Invariant). `ApplyBrush` owns the encoding
  (R8G8, not RGBA; vanilla editor maps do use Extra3/4).
* Channel selection: `ApplyToSplatmap` -> `GetMaterialType(brush.m_Tool)` (:805) reads `TerrainMaterialProperties.m_Type` from the tool
  prefab, which must resolve through `PrefabSystem.GetPrefab<TerraformingPrefab>`. No vanilla `TerraformingPrefab` has a non-None
  type outside editor-configured maps, so we register our own tool prefab per claimed slot (public `PrefabSystem.AddPrefab` creates
  the entity immediately, PrefabSystem.cs:145). The name encodes the slot ("SkiResortSplatToolExtra3") so `GetMaterialType`'s
  per-entity cache cannot go stale across loads claiming different slots.
* Game mode only: the map editor's `TerrainPanelSystem.OnGameLoaded` builds a material-paint button for every Material-target
  `TerraformingPrefab`, no UIObject required. So editor loads never claim a slot or register the tool prefab, and `OnGamePreload`
  removes any tool prefab left from an earlier Game load before the panel builds its buttons.
* Slot textures: `TerrainRenderSettingsPrefab.m_ExtraNDiffuse` / `m_ExtraNNormal` are `AssetReference<TextureAsset>`, not raw
  textures. `ApplyRenderSettings` (:1058) resolves them via `AssetDatabase.global` and `Shader.SetGlobalTexture`. So: load PNGs ->
  `Texture2D`, register as `TextureAsset`s in `AssetDatabase.user` (in-memory index entry only; we never Save), assign the prefab
  fields, then re-assign `currentTerrainRenderSettings` to itself (setter :654), which runs `ApplyRenderSettings`.
  `TextureAsset.SetData` pins the texture as the live instance (`Load()` returns it without touching disk, and the refcount cycle
  inside repeated `ApplyRenderSettings` never drops to zero), so the claim survives quality changes and seasonal re-applies.
* Collision policy: slot occupancy is a per-map settings question. We claim the first free slot in order Extra3 -> Extra4 -> Extra2 ->
  Extra1 (free = field null or invalid guid, or carrying our asset from an earlier load this session). No free slot: loud log,
  graceful disable for the load. The prefab mutation is runtime-only; map assets on disk are never touched.

Nothing above happens before the city has a run of ours. When the Force policy took a slot the map uses, the load wipe is skipped:
only our own previous bounds are ever erased, never the playable area.

## Invariant

The claimed channel is fully derived state. Every load (free slot only): wipe the channel across the playable area, then re-stamp
every live run polygon. Every change afterwards: erase the previous pass's union bounds, re-stamp everything. Paint left in saves
after uninstalling the mod is the known removal footprint.

## Seasonal

Winter-ish := snow line at/below the mean run mid-elevation or ambient at/below freezing (hysteresis on both). On transition swap the
claimed slot's diffuse/normal references and re-assign `currentTerrainRenderSettings`; the splatmap never changes with seasons.
Checked every `SeasonCheckFrames` + once at load.

## Scheduling

PreCulling, anchored before `TerrainMaterialSystem` (every frame in Game and Editor, paused included: area edits arrive through tool
phases that ignore simulation pause, and Created/Updated/Deleted tags live until `CleanUpSystem` at Cleanup). Rasterization is
debounced: events only mark a pending flag; at most one rasterize per `RasterizeDebounceFrames`.

## Logging

Resolved state at load, every rasterize pass outcome (runs/triangles/dabs/bounds/ms), season transitions, a one-time brush inventory
and splatmap measurement (`LogSplatmapOnce`). Defensive contract (`ChairPinSystem` precedent): never throw into the game loop;
one-shot "!!!" log, disabled for the current load only, re-armed at `OnGameLoadingComplete`.
