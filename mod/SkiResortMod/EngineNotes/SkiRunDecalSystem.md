# SkiRunDecalSystem: how the live decal material of our ski-run surfaces is upgraded at runtime

The system upgrades the live material behind our ski-run decal surfaces at runtime. It needs no Editor re-import, no custom shader and no change to a shared
asset. `AreaBatchSystem` clones the shared `AreaDecalShader` material once per surface prefab. It names the clone
"Area batch (<prefab name>)" (AreaBatchSystem.cs 2094-2097). `RenderedArea` does not expose everything: the shader's snow slots, the
`_MATERIAL_AFFECTS_*` keywords and the `_Affect*` toggles. Only a mutation of that clone reaches them. Mutating our clone cannot leak to other surfaces,
because we never touch the shared `m_RenderedAreaMaterial` or any other prefab's clone.

## Each load (Game and Editor)

1. Probe: on first sight of each live clone, dump the shader name and every property with its pre-mutation value.
2. Apply, safe defaults only:
   - our decal texture pair -> `_BaseColorMap` / `_NormalMap` (live material only, the prefab's fields are
     `AssetReference<TextureAsset>`)
   - the groomed-corduroy pair -> `_Snow_BaseColorMap` / `_Snow_Normal` (fed by the global `_SnowMap`, SnowSystem.cs 277/369. For groomed
     only, see `Tuning.ApplySnowSlots`)
   - the scalars known to be safe: `_NormalOpacity` 1, `_NormalAlphaSource` 1, `_Smoothness` 0.4. Each scalar is mirrored onto the `RenderedArea`
     field of the same name so later batches agree.
3. Re-assert: a texture-quality change re-Loads the prefab's assets into the live clone (`AreaBatchSystem.UpdateTextures`, 1964), and batch
   rebuilds Destroy our mutated clone. So a periodic sentinel verifies our values and re-applies them (loudly) when the game overwrote them.

We leave `_Snow` float, `_MATERIAL_AFFECTS_ALBEDO` and `_Wetness` / `_Porosity` alone. The vanilla material already whitens in winter,
and we only change what the white is.

## Seasonal dress

The active pair follows snow presence on the runs. The source is `SkiSnowInjectionSystem.TryGetDressDepth`: the managed mean or, with no managed
measurement this load, the natural runs' measured mean. The fallback is the 24-hour mean temperature, not the instantaneous ambient. The instantaneous
ambient flipped the dress daily and read 0 C before a session's first tick.

- Winter wears the per-run wear/corduroy set at per-type alpha. Snowmaking is near-clear, because its winter body is injected snow. Groomed keeps a visible corduroy lane.
- Summer wears the shared faded green.
- Prefab texture references are re-pointed per season too, so the game's own batch re-derivations stay in season.

## The m_Version gotcha

`AreaBatchSystem` mutates `RenderedArea.m_Version` to OverrideMaterial in place the first time a batch builds with `m_Material == null`.
We never assign `m_Material`, so this cannot arise here. This note records it so a future escalation trips over the note before the bug.

## Scheduling and logging

The system runs in PreCulling, anchored after `AreaBatchSystem` (clones build lazily on prefab Created). It runs every frame in Game and Editor, paused included,
so the retry countdown and the re-assert sentinel tick. Logging and the defensive contract are the ChairPinSystem pattern. The system logs a property
write only when it changes the slot (texture: not the object already bound, by `UnityEngine.Object`'s `!=`. Float: a
different value). Every write still happens and the sentinel records it. The system finds clones by name and retries until it finds them.
