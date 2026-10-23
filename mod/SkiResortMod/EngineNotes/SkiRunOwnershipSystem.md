# SkiRunOwnershipSystem: how ski runs are bound to their Welcome Sign without Owner, the decision table, the load pass and the sweep

`SkiRunOwnershipSystem` links drawn ski runs to their Ski Area Welcome Sign through our own serialized component `SkiRunBinding`.
Runs are found by prefab name. The system supplies the link, the one-time bind charge and the tether. Citations are the `Game.dll`
1.6.0f1 decompile baseline.

## Why not Owner(sign)

In game mode the bulldozer walks an owned run's `Owner` to the sign (Game.Areas/RaycastJobs.cs:80-93), so bulldozing one run deleted
the sign and, through the `SubArea` buffer this system used to add, every run. Removing `Owner` from runs is necessary and sufficient
for both the surface and the border-prop path (Game.Objects/RaycastJobs.cs:355-358). So: no `Owner` on runs, no `SubArea` buffer on
the sign, no vanilla cascade; the sign -> runs cascade is `SkiRunGateCascadeSystem`, keyed on `SkiRunBinding.m_Gate`.

Our run `SurfacePrefab`s do not carry `Game.Prefabs.ServiceUpgrade` (runs are launched from the hub), and every downstream upgrade
behavior was Object-gated in vanilla anyway: a drawn run never gets `Owner` in game mode (AreaToolSystem.cs:2105-2111 is
editor-only), `Temp.m_Cost` is written on the object path only, no `InstalledUpgrade`, no tether.

## The decision table

One table (`SkiRunBindingRules.Decide`, pure, tested by tests/BindingBatch) serves both entry points, so the load pass and the sweep
cannot disagree:

* `MigrateLegacy`: `Owner` is our sign, or a dead owner. Drop the owner's `SubArea` entry first (once `Owner` is gone
  `SubAreaReferencesSystem` never removes it: its query is Owner-gated, Game.Areas/SubAreaReferencesSystem.cs:122-134), remove
  `Owner`, add `SkiRunBinding {owner if live else Null, Paid, cost}`. Never charges: every Owner(sign) run was charged when the old
  sweep added `Owner`.
* `LeaveForeign`: a live non-sign `Owner`: untouched, counted.
* `KeepBound`: binding to a live sign: nothing. A relocated sign keeps its runs at any distance (the tether is checked at bind time
  only).
* `RebindFree`: binding to a dead sign + a sign within `TetherM`: `m_Gate` = nearest, no charge.
* `Orphan`: binding to a dead sign, none in tether: `m_Gate` = Null (WARN on the transition only), `FlagPaid` kept: a "paid orphan",
  unbound for snow, bills and score, re-links free later. Reached only when our cascade missed the sign's delete or the sign was gone
  at save time. Never delete a run on an inferred event: a broken reference at load would otherwise wipe the resort.
* `RestoreFree`: load pass only: no binding, no `Owner`, a sign in tether: binding `{sign, Paid|Restored, 0}`, no charge
  (uninstall -> save -> reinstall re-links free; the residual exploit is at most one bind fee per run, accepted).
* `BindCharge`: sweep only: no binding, no `Owner`, a sign in tether: binding `{sign, Paid, cost}` + `Charge(cost)`; refused (counted
  unbound, WARN once) before this load's load pass completed (`LoadPassDone`).
* `LeaveUnbound`: no sign in tether: nothing (unbound runs stay bare, bill nothing, score nothing:
  `SkiSnowInjectionSystem.IsRunBound` -> `IsBound`).

## The load pass

In `OnGameLoadingComplete`, after `ResolvePrefabs()`, before `m_Armed = true`. `GameManager` invokes `onGameLoadingComplete` from the
load continuation (Game.SceneFlow/GameManager.cs:1126) while `isGameLoading` is still true (:262-272), and `GameSystemBase` routes it
inside a try/catch (Game/GameSystemBase.cs:29, :60-70). No sweep can interleave: GameSimulation ticks zero times while loading
(Game.Simulation/SimulationSystem.cs:202-219) and the trigger and cascade systems guard `isGameLoading`. Structural EntityManager
changes in that callback have a vanilla precedent (Game.Tutorials/TutorialSystem.cs:301, :313-314).

Steps, collect first and mutate after:

1. Migrate legacy runs.
2. Sign `SubArea` buffers: remove entries pointing at one of our runs or a non-existing entity, then the buffer itself when it is
   empty and the sign's prefab has no `Game.Prefabs.SubArea` buffer. The vanilla load rebuilder appends every Area+Owner into an
   existing buffer (Game.Serialization/SubAreaSystem.cs:27-40, :75), and `DestroySystem` adds one for rubble
   (Game.Objects/DestroySystem.cs:146-147, :182-185), so rubble entries are kept; whether the buffer's presence survives a load is
   unverified, the pass handles both.
3. Restore lost links free.
4. No `Updated` is added (the decal anchor and height mode re-derive; a one-time decal rotate/shift on migrated runs is expected).
5. Publish `Last*` and `LoadPassDone`, log ONE "Binding: load pass" line every load. The pass never calls `Charge`.

### Load-pass failure

A run that still carries `Owner(sign)` keeps the one-run-bulldoze trap (bulldozing it deletes the sign and, through the cascade,
every bound run; Game.Areas/RaycastJobs.cs:59-93), so a throw must not stop the sweep's non-charging late migration:

* Each run's migration has its own try/catch (the first failure per load logs "!!!" naming the run and the trap), and steps 2-3
  still run.
* An exception anywhere in the pass (or any per-run migration failure) sets `m_LoadPassFailed` and leaves `LoadPassDone` false: no
  charges this load and no `RestoreFree`, but not `m_Disabled`. The sweep keeps running with only the non-charging rows
  (`MigrateLegacy`, `RebindFree`, `Orphan`) plus step 2's cleanup, starting on the first frame after the load, paused too
  (`SkiRunBindTriggerSystem` fires `SweepNow` once on `LoadPassRetryPending`).
* The sweep keeps its own one-shot "!!!" (a throw there sets `m_Disabled`).
* `MigrateRun` adds the binding before it removes `Owner`, so a run interrupted mid-migration carries `Owner` + binding (row
  `MigrateLegacy` finishes it, no charge), never neither.

The cascade keeps working throughout (it reads `PrefabSetsReady`, not `m_Disabled`). The next load re-runs the pass (a run left with
neither `Owner` nor binding re-links free there).

## The sweep

`OnUpdate` (GameSimulation, interval 64, anchored before `SkiSnowInjectionSystem` in Mod.cs) and `SweepNow(why)`
(`SkiRunBindTriggerSystem` at ModificationEnd, paused or not, on a Created run of ours or a Created sign). First guard:
`GameManager.isGameLoading`. Same table with `LoadPass = false`; a legacy run found here is migrated with a WARN (no charge).

The census line `Ownership: B bound + U unbound + O orphaned run(s), G gate(s)` is logged on any change, and while unchanged at most
every `CensusHeartbeatS`.

Edits never re-create a run (ApplyAreasSystem.cs:123-131, :167-202: Node copy + `Updated` on the original entity, which keeps its
`SkiRunBinding`), and `Charge()` runs only right after the synchronous `AddComponentData(SkiRunBinding)` of a run that had none, so
the trigger and the cadence sweep cannot double-charge. `CollectGates` / `CollectRuns` complete their queries' dependencies first:
chunk reads through type handles do not wait for running jobs (Entities semantics, unverified here); required at ModificationEnd
where Modification jobs are in flight.

## Downgrade, XP and fees

Downgrade hazard (see the `SkiRunBinding` header): an older build loads a newer save without error, then re-binds and re-charges
every run. XP: deliberately none (a redraw would be an XP faucet).

Fees: `RunFeeFor(prefab, plan area)` = `SkiRunFees.Fee(KindOf(prefab), Geometry.m_SurfaceArea)`, carried by `RunPlan.AreaM2` from
`CollectRuns`. `BindCharge` charges it and stores it in `SkiRunBinding.m_PaidAmount`; the load pass's `MigrateLegacy` stores it as
paid (never charged). Reshape: no re-charge, no refund. Delete: no refund. The hub and the drawing tooltip read the same function.

## Helpers (internal, main thread)

`IsArmed`; `PrefabSetsReady` (sign set + >= 1 run set resolved, independent of `m_Disabled`: the cascade's and the draw feedback's
gate); `LoadPassDone` / `LoadPassFailed` / `BindsNewRuns` / `LoadPassRetryPending`; `IsRunPrefab`; `IsGatePrefab`; `IsLiveGate`;
`IsBound` (the bound test: binding + live sign); `TryGetBoundGate`; `TryNearestGate` / `TryNearestGateEntity` (a managed scan of every
building: recompute or transition only); `RunFeeFor`; `KindOf` (the hub's `RunRow.kind`, -1 = not a run); `BindGeneration` (a counter
bumped on every binding write, which raises no `Updated` tag, so `SkiRunCatalogSystem` reads it in its change signature);
`SweepNow`.

Temps never carry `SkiRunBinding` (GenerateAreasSystem.cs:256-273): callers resolve `Temp.m_Original` before `IsBound`.
`TetherMeters` is the one tether constant (`Tuning.TetherM`) that `SkiResortStatsSystem`'s lift / station resort rule reads too.

## Scope and logging

Game mode only. House contract: "created" line; one ARMED line per load listing every constant; `m_Armed` / `m_Disabled` /
`m_ErrorLogged` reset in `OnGameLoadingComplete`; one-shot "!!!" catch that disables binding until the next load; no per-frame work.
