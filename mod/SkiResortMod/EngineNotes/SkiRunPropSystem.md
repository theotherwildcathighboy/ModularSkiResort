# SkiRunPropSystem: how run borders get snow guns and fencing from the vanilla area-props spawner, and how the prop density sliders work

Unlocks the game's own area-props machinery for our run surfaces. Citations are the `Game.dll` 1.6.0f1 decompile baseline.

## The missing wire

The vanilla mechanism is complete but lot-class-gated: the `AreaSubObjects` prefab component (snow guns / crash fencing per polygon
vertex, border-placed by `SecondaryObjectSystem.CreateSecondaryAreaObjects`) only fires for area entities that already carry a runtime
`Game.Objects.SubObject` buffer (`m_ObjectQuery`: All=SubObject + Any=Updated..., SecondaryObjectSystem.cs:4073-4083), and that buffer
comes from the prefab class archetype: `LotPrefab.GetArchetypeComponents` hard-adds it (LotPrefab.cs:36), `SurfacePrefab` does not. No
prefab JSON can add a runtime archetype component, so this system bridges the one missing wire.

For each of our run-surface area entities whose prefab carries prefab-side sub-object entries (`AreaSubObjects` filled by
`AreaInitializeSystem`) but whose instance lacks the runtime buffer: `AddBuffer<Game.Objects.SubObject>` + ensure `Updated`. The
vanilla spawner does everything else the same frame: at most one prop per drawn polygon vertex (see prop density below), rotated
along the outgoing border segment, elevation handled, self-healing on polygon edits (edits re-tag `Updated`; the buffer persists).

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

## Prop density

Pure rules in `RunPropDensityRules` (tests `DensityTests`). Two Options sliders in the Runs group, `SnowGunDensityPct` ("Snow guns and
hydrants": snowmaking and night runs) and `FloodlightDensityPct` ("Floodlight masts on night runs"), 0-100 % step 5, default 100. Look
only: snow and upkeep follow the run type. `SecondaryObjectSystem`'s 1.6.2 diff changes only :3499 and :3617 (no shift), and
`AreaInitializeSystem`, `ChangedDistrict` and `CurrentDistrictSystem` are unchanged in 1.6.2.

### What is written

The runtime weight route, no prefab surgery:

1. The run prefabs' per-corner chance, `Game.Prefabs.SubObject.m_Probability` of `SkiRunSnowmakingA`'s and `SkiRunNightA`'s border
   entries (filled with 100 once, when the prefab entity is created, AreaInitializeSystem.cs:384-404, :398; passed on per corner,
   SecondaryObjectSystem.cs:3004; rolled on the picked member's stream copy, :3279).
2. The night group's member weights, `SpawnableObjectData.m_Probability` (copied from the prefab at init, ObjectInitializeSystem.cs:913,
   1.6.2 :923; read live by the pick, SecondaryObjectSystem.cs:3258, :3262): the masts (members of the night group only) scaled by
   m / g, the shared guns, hydrants and empty marker 0 at g = 0. Formulas and why they are exact: `RunPropDensityRules`.

Every reader of those member weights picks among one placeholder's members (SecondaryObjectSystem.cs:3258/3262,
SubObjectSystem.cs:2345-2397, AreaUtils.cs:553-554, LaneSystem.cs:6710-6715, AreaSpawnSystem.cs:375, CreatureSpawnerSystem.cs:168-169,
PlaceholderSystem.cs:99-103 via :196, ObjectToolBaseSystem.cs:617-627 via :684) and our members sit in our two groups only, so only
the run-prop path reads them.

### When

Base values are recorded once per session, at the first game load and before any write (prefab entities outlive loads; the writes
persist for the session and are never serialized: the `PrefabBase` objects are not touched). The values are written at the first update
of every game load and after the slider pair has been still for `RunPropDensityRules.SettleFrames` updates; written back to the bases
at a non-Game load after a write (hygiene only: no run gets props outside a game).

### Re-stamp only after a settled slider change, never at a load

The tie-break draw of the pick (:3264) depends on each member's position in the group, and that order is the prefab-init order
(ObjectInitializeSystem.cs:900-905, 1.6.2 :910-915), not shown stable across game sessions: a load re-stamp could reshuffle every
saved run. So a load writes the prefab values only, and a save made at another density keeps its layout until a run is edited (any
edit re-tags `Updated` and re-runs that run's props with the current values).

The re-stamp tag is `Game.Areas.ChangedDistrict` on our propped runs:

* It is in `SecondaryObjectSystem`'s owner query (:4073-4083); each entity of such a chunk is enqueued as an owner exactly as for
  `Updated` (:750-771), connected edges are added only for net edges (:774-778), and the system removes the tag on its
  `ModificationBarrier4B` buffer (:779; barrier :4072, buffer :4202).
* Its only other user is its writer `CurrentDistrictSystem` (CurrentDistrictSystem.cs:235-237; grep of 1.6.0f1 and 1.6.2f1, codegen
  registry excluded).
* Not `Updated`: that would also wake our splat rasterize, catalog rebuild and attractiveness watcher and the vanilla area pipeline.
  A new kind of vanilla write, on our own run entities only.
* Fallback if a patch gives the tag another reader or drops it from that query: tag `Updated` in `StampRuns` instead (re-grep on each
  game patch).
* Props a re-stamp creates carry Created + Updated from their archetype (ObjectPrefab.cs:40-41), so their lights are tested at
  creation; a re-used prop with an unchanged Transform gets no `Updated` (:3445-3452).

### Phase and cost

`UpdateDensity` runs first in `OnUpdate`, before the bridge, so this frame's bridging and `SecondaryObjectSystem` (right after us in
Modification4B) already see the written values. The writes go through `EntityManager` on the main thread (a sync point on prefab data:
applies only, never per frame). An idle frame costs two settings reads, two compares and a counter.

### Logs

Per Game load "Props: density ARMED - ..." or "Props: density inert: <reason>"; one apply line per write ("... runs not re-stamped
(load: ...)" or "K run(s) re-stamped (setting change)"); a census the frame after a re-stamp with K > 0 (`SecondaryObjectSystem` has
run, its barrier played back and `SecondaryObjectReferencesSystem`, Modification5, SystemOrder.cs:206, filed the new props); the
write-back line at a non-Game load; two once-per-session WARNs; and its own one-shot "!!!" catch that turns density off for the load
and leaves the bridge alone (the bridge's catch likewise never stops density).
