# SkiRunPropMember and SkiRunPropSettings: why placed run props carry a tag instead of Owner(run), and the save layouts

Two persisted components of the run prop placer. Their type names and `Serialize` layouts are a save-format contract (see `SaveLayouts`). Citations are the `Game.dll` 1.6.0f1
decompile baseline.

## Why a tag and not Owner(run)

- `Owner(run)` + `Secondary`: `SecondaryObjectSystem` reaps every Secondary child at each rebuild of the owner (`:1159-1211`), so any edit would delete the placed props.
- `Owner(run)` without `Secondary`: `SubObjectSystem` relocates or deletes owned objects at each `Updated` of the area (the `SkiRunTreeMember` note), and an object on the outline
  touches the edge.
- At a load every object with `Owner` is appended to its owner's `SubObject` buffer (`Game.Serialization/SubObjectSystem.cs:39-46`), so an owner cannot be hidden, and a cat track has
  no runtime buffer.

So a placed prop is free-standing: the entity shape of vanilla's creation (`SecondaryObjectSystem.cs:3552-3560`, `:3638-3649`) without the owner types, plus the `Game.Objects.Surface` an
unowned physical object gets (`GenerateObjectsSystem.cs:1264-1274`). `ObjectInitializeSystem.cs:595-605` gives a plain static prop `Overridable | Brushable`, and `GroundHeightSystem.cs:229`,
`:604` re-snap those to the terrain after a terrain edit, so a prop follows the hill with no work here.

What that changes in play: a free-standing prop raycasts as itself (`Game.Objects/RaycastJobs.cs:192`, `:328+`), so the default tool selects one prop and the bulldozer deletes one prop. Such a
prop stays gone until the run's outline or a slider changes: a load keeps a run whose key matches.

Its own `Surface`: `BatchDataSystem.cs:795-817` reads the entity's own first and `WetnessSystem.cs:160` writes every `Surface` that is not Deleted, Overridden or Temp, so
`SkiRunSurfaceSystem` writes the run's dress value onto all tagged props right after `WetnessSystem`.

Effects: for a gun `EffectControlData` takes the object as its own top owner (`EffectControlData.cs:702-731`), so the plume's Disaster gate and the Night random read the prop either way.

## SkiRunPropMember (6 bytes)

`m_Run` (remapped on load like `Owner`) and `m_Class` (`RunPropDensityRules.ClassGun`, `ClassHydrant` or `ClassMast`). A null or dead `m_Run` marks an orphan: the placer's load sweep
deletes it. Plume, night light, prop snow and the census read this tag and never `Secondary` (`tools/check-run-props.js`).

## SkiRunPropSettings (13 bytes)

On the run: `m_GunSpacingM`, `m_LightSpacingM` (0 = off, else `RunPropPlacerRules.SpacingMinM`..`SpacingMaxM`) and `m_AppliedKey`, the `RingKey` of what stands. The placer writes the key last
in a rebuild, so a save taken mid-rebuild keeps the old key and the next load finishes the job. Look only, except a cat track's gun value, read only through `SkiRunPropSystem.CatTrackGunsOn`: Off = no snowmaking bill (upkeep and the hub's cost row, DECISIONS 563) and an open state on natural snow only (`SkiSnowInjectionSystem.GetRunSnow` passes it to `RunSnowRules.ThresholdC`, the natural threshold, DECISIONS 568).

## The delete cascade

Every delete path ends in a `Deleted` tag on the area before Modification4B: tool apply, PostTool, the sign cascade at Modification2. `SkiRunPropSystem.CascadeDeletes` selects those areas each
frame and deletes the props in the same phase as vanilla's reap, so they reach the search tree at Modification5. `SkiDeleteAllSystem` also selects tagged props directly, so a fallen run cannot
leave them behind, and the load sweep removes any orphan.

## Mod removal

The two component types drop with the mod, and the props become plain objects of a missing prefab: the game's standard missing-asset handling applies (UNVERIFIED for a free-standing prop; the
same handling as any other placed object of ours).
