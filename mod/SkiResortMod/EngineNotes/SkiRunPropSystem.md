# SkiRunPropSystem: the bridge on our run surfaces, the corner stop, and the placer that walks a run's border

Two parts. `SkiRunPropSystem.cs` keeps the bridge and stops vanilla's per-node border props. `SkiRunPropSystem.Placer.cs` stands the guns, hydrants and masts by distance. The walk is in
`RunPropPlacerRules`. `SkiRunPropMember` explains why the props carry a tag. Citations are the `Game.dll` 1.6.0f1 decompile baseline.

## The missing wire

The vanilla mechanism is complete but lot-class-gated. The `AreaSubObjects` prefab component (snow guns / crash fencing per polygon
vertex, border-placed by `SecondaryObjectSystem.CreateSecondaryAreaObjects`) only fires for some area entities.
These are the entities that already carry a runtime `Game.Objects.SubObject` buffer (`m_ObjectQuery`: All=SubObject + Any=Updated..., SecondaryObjectSystem.cs:4073-4083). That buffer
comes from the prefab class archetype: `LotPrefab.GetArchetypeComponents` hard-adds it (LotPrefab.cs:36), but `SurfacePrefab` does not. No
prefab JSON can add a runtime archetype component, so this system bridges the one missing wire.

The system handles each of our run-surface area entities whose prefab carries prefab-side sub-object entries (`AreaSubObjects` filled by
`AreaInitializeSystem`) but whose instance lacks the runtime buffer. For each, it calls `AddBuffer<Game.Objects.SubObject>` and ensures
`Updated`. Vanilla's spawner would place one prop per drawn polygon vertex. With the corner stop below, it places none. The buffer
stays, because old saves' corner props are filed in it until the load sweep removes them. The placer and `SkiRunSurfaceSystem` read
the seed and `Surface` that the bridge adds.

## Phase

Load-bearing: `SecondaryObjectSystem` runs at Modification4B, and the Created/Updated tags it keys on are wiped at Cleanup every frame.
So the buffer and tag must exist earlier in the same frame's Modification4B. This system registers
`UpdateBefore<SecondaryObjectSystem>` there (the `PowerBridgeSystem` precedent). Freshly drawn runs carry `Updated` already. This system
adds `Updated` once to pre-existing runs from older saves, alongside the buffer, so they spawn on first sight.

## Scope and mod removal

Game mode only, never Editor (spawned props are real owned entities, and a map author must never bake them into a map). The system
touches only surfaces in our watched prefab set, and only when the prefab actually has sub-object entries.

Mod removal: the runtime buffer is a vanilla component (saves load fine without us). Spawned props are owned objects referencing our
prefab, so the game's standard missing-asset handling applies.

Defensive contract (house pattern): the system writes an ARMED line per load and one line per bridged area. A one-shot "!!!" catch
disables it for the load only.

The bridge looks only at areas tagged Created or Updated, plus one full scan after a load. A loop over every unbridged Surface area per
frame cost 0.53 ms on a city with 31,183 of them, paused too (DECISIONS 521).

## The corner stop

The run prefabs' per-corner chance is `Game.Prefabs.SubObject.m_Probability` of the `EdgePlacement` entries. `AreaInitializeSystem.cs:384-404` fills it with 100 once.
`SecondaryObjectSystem.cs:3004` passes it per corner, and `:3279` rolls it as `NextInt(100) < chance`. The system writes it to 0 at every Game load and back to the recorded value at a non-Game load (hygiene only). It needs no Editor import,
and it does not touch the `PrefabBase` objects. Groomed and natural runs and cat tracks have no such entries.

## The placer

`UpdatePlacer` runs in this system's frame, before `SecondaryObjectSystem` in Modification4B:

1. `CascadeDeletes`: this frame's deleted runs take their props along (`SkiRunPropMember`, The delete cascade). This runs before the presence gate, because the last run of a resort must still clean up.
2. `ResortPresence.Present` gate (fails open).
3. `LoadSweep`, once per load on the first frame with a resort. It removes an old save's corner props (`Secondary` with `Owner` == run) and orphan tags. Every run of a walked kind gets its settings, and the sweep compares each run with what stands. It is idempotent: with the chance at 0, corner props cannot come back. Runs whose key matches stay untouched.
4. `NoteFreshRuns`: an area tagged Created or Updated gets default settings if new. The system then compares its `RingKey` with `m_AppliedKey`. A match is not dirty, so a save or a vanilla re-tag costs one key.
5. `Settle`: dirty runs wait until nothing changed for `RunPropDensityRules.SettleFrames` updates, so a slider drag rebuilds once.
6. `StepQueue`: at most `MaxPlansPerFrame` plans and `MaxOpsPerFrame` created or deleted objects per frame. It diffs first (`RunPropPlacerRules.Diff`), so unchanged stretches stay. It writes the key last.

The hub's rows call `SetSpacing` from UIUpdate: a component write and a dirty mark, nothing structural (`tools/check-run-props.js`).

Lit: no lit state exists on a run. On a cat track lit means its lights slider is above Off, default Off. Unbilled: sliders never touch upkeep, except a cat track's gun value at Off (no bill, DECISIONS 563. It opens on natural snow only, DECISIONS 568. Both go through `CatTrackGunsOn`).

Prefabs resolve by name (`PrefabID("StaticObjectPrefab", name)`), not through a run's sub-object list, so a cat track needs no `AreaSubObjects`.

## Logs

- `Props: placer ARMED ...` or `Props: placer inert ...` per Game load.
- `Props: load sweep ...` once per load, with what was converted.
- `Props [cost] placer: ...` per rebuild burst, then `Props: census ...` by the tag.
- `Props: N run(s) deleted, M placed prop(s) with them`.
- `Hub [props] ...` per slider step.
- One `!!!` per load. After it, the placer stays off until the next game load (standing props stay. The bridge's catch and this one are separate).
