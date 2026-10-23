# SkiRunDecalSystem: how the live decal material of our ski-run surfaces is upgraded at runtime

The system upgrades the live material behind our ski-run decal surfaces at runtime: no Editor re-import, no custom shader, no shared
asset touched. `AreaBatchSystem` clones the shared `AreaDecalShader` material once per surface prefab and names the clone
"Area batch (<prefab name>)" (AreaBatchSystem.cs 2094-2097); everything `RenderedArea` does not expose (the shader's snow slots, the
`_MATERIAL_AFFECTS_*` keywords, the `_Affect*` toggles) is reachable only by mutating that clone (the pattern
`SkiRunSplatSystem.ApplyDecalVisibility` uses). Mutating our clone cannot leak to other surfaces: we never touch the shared
`m_RenderedAreaMaterial` or any other prefab's clone.

## Each load (Game and Editor)

1. Probe: on first sight of each live clone, dump the shader name and every property with its pre-mutation value.
2. Apply, safe defaults only:
   - our decal texture pair -> `_BaseColorMap` / `_NormalMap` (live material only, the prefab's fields are
     `AssetReference<TextureAsset>`);
   - the groomed-corduroy pair -> `_Snow_BaseColorMap` / `_Snow_Normal` (fed by the global `_SnowMap`, SnowSystem.cs 277/369; groomed
     only, see `Tuning.ApplySnowSlots`);
   - the scalars known to be safe: `_NormalOpacity` 1, `_NormalAlphaSource` 1, `_Smoothness` 0.4, each mirrored onto the `RenderedArea`
     field of the same name so later batches agree.
3. Re-assert: texture-quality changes re-Load the prefab's assets into the live clone (`AreaBatchSystem.UpdateTextures`, 1964) and batch
   rebuilds Destroy our mutated clone, so a periodic sentinel verifies our values and re-applies (loudly) when the game overwrote them.

`_Snow` float, `_MATERIAL_AFFECTS_ALBEDO` and `_Wetness` / `_Porosity` are left alone (the vanilla material already whitens in winter;
we only change what the white is).

## Seasonal dress

The active pair follows snow presence on the runs (`SkiSnowInjectionSystem.TryGetDressDepth`: the managed mean or, with no managed
measurement this load, the natural runs' measured mean; fallback the 24-hour mean temperature, not the instantaneous ambient, which
flipped the dress daily and read 0 C before a session's first tick). Winter wears the per-run wear/corduroy set at per-type alpha
(snowmaking near-clear: its winter body is injected snow; groomed keeps a visible corduroy lane), summer the shared faded green. Prefab
texture references are re-pointed per season too, so the game's own batch re-derivations stay in season.

## The m_Version gotcha

`AreaBatchSystem` mutates `RenderedArea.m_Version` to OverrideMaterial in place the first time a batch builds with `m_Material == null`.
We never assign `m_Material`, so it cannot arise here; recorded so a future escalation trips over this note before the bug.

## Scheduling and logging

PreCulling, anchored after `AreaBatchSystem` (clones build lazily on prefab Created); every frame in Game and Editor, paused included,
so the retry countdown and the re-assert sentinel tick. Logging and the defensive contract are the ChairPinSystem pattern. A property
write is logged only when it changes the slot (texture: not the object already bound, by `UnityEngine.Object`'s `!=`; float: a
different value), but every write still happens and is recorded for the sentinel. `SkiRunSplatSystem.ApplyDecalVisibility` finds the
same clones by name; this system's retry-until-found supersedes its find-once.
