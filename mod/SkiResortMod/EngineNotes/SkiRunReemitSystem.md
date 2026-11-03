# SkiRunReemitSystem: the Groomed toggle by re-emit (run types R2, DECISIONS 577)

Decompile baseline `Game.dll` 1.6.2f1.

## Why a new entity

A placed area cannot change its prefab in place: the R0 probe (2026-11-02) swapped `PrefabRef` on a live run and the renderer and the
area search kept the old prefab's batch. Groomed is the surface itself (SkiRunGroomedA draws the groomed decal, SkiRunNaturalA draws
nothing), so turning it on or off re-emits the run as the other prefab. Snowmaking and Lights are sliders on the same entity
(`SkiRunPropSettings`, read through `SkiRunPropSystem.FeaturesOf`) and never re-emit.

## Creation

The vanilla shape of a permanent area creation, done directly on the main thread instead of through a `CreationDefinition` (the area tool's
Temp round trip would charge construction cost and needs the tool's Apply frame):

- `AreaData.m_Archetype` of the target prefab (`GenerateAreasSystem.cs:259`); `AreaPrefab.cs:47-48` puts `Created` and `Updated` in it.
- `PrefabRef`, the old `Node` ring copied as is (positions and elevations: no re-snap), the old `Area` flags (`Complete` among them,
  `GenerateAreasSystem.cs:283-290, 341`), `PseudoRandomSeed` when both prefabs carry it.
- `Applied`, as `ApplyAreasSystem.cs:272` (`m_AppliedTypes`) adds it to an applied creation.
- `EntityManager.CreateEntity(archetype, NativeArray)`: the single-entity overload needs `ReadOnlySpan`, absent from this target.

`Geometry`, `Triangle` and the batch are computed by vanilla later in the same frame (Modification4-5) from `Updated`.

The old run is deleted the bulldozer's way (`RunAreaEmit.DeleteLikeBulldozer`): plain `Deleted`, no refund. Vanilla has no game-mode undo
to bring it back (searched 1.6.2f1: only the area tool's in-progress "Undo Area Node", `AreaToolSystem.cs:2564`); the mod's own undo is
the Quick runs batch, which is rekeyed (below).

## What carries over

| State | How |
| --- | --- |
| Binding (sign, paid flags, paid amount) | `SkiRunBinding` copied; `SkiRunBindTriggerSystem` only takes runs without one, so no bind fee |
| Sliders | `SkiRunPropSettings` copied with `m_AppliedKey = 0`: the placer re-places on the new run |
| Props | not moved: they cascade with the old run (`SkiRunPropSystem` CascadeDeletes, Modification4B) and the placer stands new ones |
| Custom name | queued to `SkiResortHubUISystem.RunReplaced`, which calls `NameSystem.SetCustomName` at UIUpdate: it makes an `EndFrameBarrier` command buffer, which throws in a Modification phase (in game, 2026-11-02). A default name is built from the kind and an ordinal until run types R3 stores a picked trail name, so it follows the new kind |
| Glade trees | `SkiRunTreeMember.m_Run` retargeted before `SkiGladeCleanupSystem` (Modification2) would delete them with the old run |
| Snow, texels, gun state, protection, ledger | `SkiSnowInjectionSystem.RekeyRun` |
| Rating | `SkiRunCatalogSystem.RekeyRun` (the rating's key is the node hash: same nodes, so it stands) |
| Score view, last rating | `SkiResortAttractivenessSystem.RekeyRun`: the hub's open state never drops to unknown |
| Selection, panel focus, bind notices | `SkiResortHubUISystem.RunReplaced`: `ToolSystem.selected` (the info panel stays on the run), the `runReplaced` binding (HubPanel's focus), the sign map (no follow offer) |
| Quick runs undo | `QuickRunsToolSystem.RekeyRun`: the batch holds the new run and prefab, so an undo deletes the run that stands |
| Feature fee | `SkiRunOwnershipSystem.SettleFeatureSteps(new, "groomed on", old area)`: the Groomed step once per run, free again after that; Off refunds nothing |

A pass that started before the swap can still write the old key afterwards (the injection's mask and snow passes, the score's recompute),
so each of those systems keeps a `RunAliases` list and re-applies it at the top of every update for `RunAliases.TtlUpdates` updates. Move
never overwrites a value already written for the new run.

## The flash

The new entity draws once its batch is built later in the frame or the next one, while the old one stops drawing at Cleanup: the run may
flash for a frame. Accepted (2026-11-02): this is how the game redraws an area that changes type.

## Phase

Modification1 (Mod.cs): before `SkiGladeCleanupSystem` (Modification2), the placer's cascade, the area geometry and the bind trigger
(ModificationEnd) of the same frame. The Modification phases run paused too (`ModificationSystem`, `SystemOrder.cs:60`).
