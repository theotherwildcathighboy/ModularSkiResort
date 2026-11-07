# SkiRunPropMember and SkiRunPropSettings: why placed run props carry a tag instead of Owner(run), and the save layouts

The run prop placer persists two components. Their type names and `Serialize` layouts are a save-format contract (see `SaveLayouts`).
Citations are the `Game.dll` 1.6.0f1 decompile baseline.

## Why a tag and not Owner(run)

- `Owner(run)` + `Secondary`: `SecondaryObjectSystem` reaps every Secondary child at each rebuild of the owner (`:1159-1211`), so any
  edit would delete the placed props.
- `Owner(run)` without `Secondary`: `SubObjectSystem` relocates or deletes owned objects at each `Updated` of the area (the
  `SkiRunTreeMember` note). An object on the outline also touches the edge.
- At a load, the game appends every object with `Owner` to its owner's `SubObject` buffer (`Game.Serialization/SubObjectSystem.cs:39-46`).
  So an owner cannot be hidden. Also, a cat track has no runtime buffer.

So a placed prop is free-standing. Its entity shape is the shape of vanilla's creation (`SecondaryObjectSystem.cs:3552-3560`,
`:3638-3649`) without the owner types. It also has the `Game.Objects.Surface` that an unowned physical object gets
(`GenerateObjectsSystem.cs:1264-1274`). `ObjectInitializeSystem.cs:595-605` gives a plain static prop `Overridable | Brushable`.
`GroundHeightSystem.cs:229`, `:604` re-snap those props to the terrain after a terrain edit. So a prop follows the hill with no work here.

What that changes in play: a free-standing prop raycasts as itself (`Game.Objects/RaycastJobs.cs:192`, `:328+`). So the default tool
selects one prop, and the bulldozer deletes one prop. A deleted prop stays gone until the run's outline or a slider changes. A load keeps a
run whose key matches.

Its own `Surface`: `BatchDataSystem.cs:795-817` reads the entity's own `Surface` first. `WetnessSystem.cs:160` writes every `Surface` that
is not Deleted, Overridden or Temp. So `SkiRunSurfaceSystem` writes the run's dress value onto all tagged props right after
`WetnessSystem`.

Effects: for a gun, `EffectControlData` takes the object as its own top owner (`EffectControlData.cs:702-731`). So the plume's Disaster
gate and the Night random read the prop either way.

## SkiRunPropMember (6 bytes)

The component has two fields: `m_Run` (remapped on load like `Owner`) and `m_Class` (`RunPropDensityRules.ClassGun`, `ClassHydrant` or
`ClassMast`). A null or dead `m_Run` marks an orphan. The placer's load sweep deletes it. Plume, night light, prop snow and the census read
this tag and never `Secondary` (`tools/check-run-props.js`).

## SkiRunPropSettings (13 bytes)

The run holds three fields:

- `m_GunSpacingM`
- `m_LightSpacingM` (0 = off, else `RunPropPlacerRules.SpacingMinM`..`SpacingMaxM`)
- `m_AppliedKey`, the `RingKey` of what stands

The placer writes the key last in a rebuild. So a save taken mid-rebuild keeps the old key, and the next load finishes the job. The
settings affect the look of the run only, with one exception: a cat track's gun value. Code reads that value only through
`SkiRunPropSystem.CatTrackGunsOn`. Off means two things:

- no snowmaking bill (upkeep and the hub's cost row, DECISIONS 563)
- an open state on natural snow only (`SkiSnowInjectionSystem.GetRunSnow` passes it to `RunSnowRules.ThresholdC`, the natural threshold,
  DECISIONS 568)

## The delete cascade

Every delete path ends in a `Deleted` tag on the area before Modification4B. The delete paths are tool apply, PostTool and the sign cascade
at Modification2. `SkiRunPropSystem.CascadeDeletes` selects those areas each frame and deletes the props in the same phase as vanilla's
reap. So the deletions reach the search tree at Modification5. `SkiDeleteAllSystem` also selects tagged props directly. So a fallen run
cannot leave them behind. The load sweep also removes any orphan.

## Mod removal

The two component types drop with the mod. The props become plain objects of a missing prefab. The game's standard missing-asset handling
applies. This is UNVERIFIED for a free-standing prop. The handling is the same as for any other placed object of ours.
