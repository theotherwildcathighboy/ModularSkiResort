# SkiRunPropSystem: the bridge on our run surfaces, the corner stop, and the placer that walks a run's border

Two parts. `SkiRunPropSystem.cs` keeps the bridge and stops vanilla's per-node border props; `SkiRunPropSystem.Placer.cs` stands the guns, hydrants and masts by distance. The walk is in
`RunPropPlacerRules`, why the props carry a tag in `SkiRunPropMember`. Citations are the `Game.dll` 1.6.0f1 decompile baseline.

## The missing wire

The vanilla mechanism is complete but lot-class-gated: the `AreaSubObjects` prefab component (snow guns / crash fencing per polygon
vertex, border-placed by `SecondaryObjectSystem.CreateSecondaryAreaObjects`) only fires for area entities that already carry a runtime
`Game.Objects.SubObject` buffer (`m_ObjectQuery`: All=SubObject + Any=Updated..., SecondaryObjectSystem.cs:4073-4083), and that buffer
comes from the prefab class archetype: `LotPrefab.GetArchetypeComponents` hard-adds it (LotPrefab.cs:36), `SurfacePrefab` does not. No
prefab JSON can add a runtime archetype component, so this system bridges the one missing wire.

For each of our run-surface area entities whose prefab carries prefab-side sub-object entries (`AreaSubObjects` filled by
`AreaInitializeSystem`) but whose instance lacks the runtime buffer: `AddBuffer<Game.Objects.SubObject>` + ensure `Updated`. Vanilla's
spawner would place one prop per drawn polygon vertex; with the corner stop below it places none. The buffer stays because old saves' corner
props are filed in it until the load sweep removes them, and the seed and `Surface` the bridge adds are read by the placer and `SkiRunSurfaceSystem`.

## Phase

Load-bearing: `SecondaryObjectSystem` runs at Modification4B and the Created/Updated tags it keys on are wiped at Cleanup every frame,
so the buffer and tag must exist earlier in the same frame's Modification4B; this system registers
`UpdateBefore<SecondaryObjectSystem>` there (the `PowerBridgeSystem` precedent). Freshly drawn runs carry `Updated` already;
pre-existing runs from older saves get `Updated` added here once, alongside the buffer, so they spawn on first sight.

## Scope and mod removal

Game mode only, never Editor (spawned props are real owned entities and a map author must never bake them into a map). Only surfaces
in our watched prefab set are touched, and only when the prefab actually has sub-object entries.

Mod removal: the runtime buffer is a vanilla component (saves load fine without us); spawned props are owned objects referencing our
prefab, so the game's standard missing-asset handling applies.

Defensive contract (house pattern): ARMED line per load, one line per bridged area, one-shot "!!!" catch that disables for the load
only.

The bridge looks only at areas tagged Created or Updated, plus one full scan after a load: a loop over every unbridged Surface area per
frame cost 0.53 ms on a city with 31,183 of them, paused too (DECISIONS 521).

## The corner stop

The run prefabs' per-corner chance, `Game.Prefabs.SubObject.m_Probability` of the `EdgePlacement` entries (filled with 100 once, `AreaInitializeSystem.cs:384-404`; passed per corner,
`SecondaryObjectSystem.cs:3004`; rolled `NextInt(100) < chance`, `:3279`), is written to 0 at every Game load and back to the recorded value at a non-Game load (hygiene only). No Editor import,
and the `PrefabBase` objects are not touched. Groomed and natural runs and cat tracks have no such entries.

## The placer

`UpdatePlacer` runs in this system's frame, before `SecondaryObjectSystem` in Modification4B:

1. `CascadeDeletes`: this frame's deleted runs take their props along (`SkiRunPropMember`, The delete cascade). This runs before the presence gate: the last run of a resort must still clean up.
2. `ResortPresence.Present` gate (fails open).
3. `LoadSweep`, once per load on the first frame with a resort: an old save's corner props (`Secondary` with `Owner` == run) go, orphan tags go, and every run of a walked kind gets its settings and is
   compared with what stands. Idempotent: with the chance at 0 corner props cannot come back. Runs whose key matches stay untouched.
4. `NoteFreshRuns`: an area tagged Created or Updated gets default settings if new, then its `RingKey` is compared with `m_AppliedKey`. A match is not dirty, so a save or a vanilla re-tag costs one key.
5. `Settle`: dirty runs wait until nothing changed for `RunPropDensityRules.SettleFrames` updates, so a slider drag rebuilds once.
6. `StepQueue`: at most `MaxPlansPerFrame` plans and `MaxOpsPerFrame` created or deleted objects per frame. Diff first (`RunPropPlacerRules.Diff`), so unchanged stretches stay. The key is written last.

The hub's rows call `SetSpacing` from UIUpdate: a component write and a dirty mark, nothing structural (`tools/check-run-props.js`).

Lit: no lit state exists on a run. On a cat track lit means its lights slider is above Off, default Off. Unbilled: sliders never touch upkeep, except a cat track's gun value at Off (no bill, DECISIONS 563; opens on natural snow only, DECISIONS 568; both through `CatTrackGunsOn`).

Prefabs resolve by name (`PrefabID("StaticObjectPrefab", name)`), not through a run's sub-object list, so a cat track needs no `AreaSubObjects`.

## Logs

`Props: placer ARMED ...` or `Props: placer inert ...` per Game load; `Props: load sweep ...` once per load with what was converted; `Props [cost] placer: ...` per rebuild burst, then `Props: census ...` by the
tag; `Props: N run(s) deleted, M placed prop(s) with them`; `Hub [props] ...` per slider step; one `!!!` per load, after which the placer stays off until the next game load (standing props stay, the bridge's
catch and this one are separate).
