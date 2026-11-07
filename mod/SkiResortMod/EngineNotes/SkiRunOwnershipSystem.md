# SkiRunOwnershipSystem: how ski runs are bound to their Welcome Sign without Owner, the decision table, the load pass and the sweep

`SkiRunOwnershipSystem` links drawn ski runs to their Ski Area Welcome Sign through our own serialized component `SkiRunBinding`.
Runs are found by prefab name. The system supplies the link, the one-time bind charge and the tether. Citations are the `Game.dll`
1.6.0f1 decompile baseline.

## Why not Owner(sign)

In game mode the bulldozer walks an owned run's `Owner` to the sign (Game.Areas/RaycastJobs.cs:80-93). So bulldozing one run deleted
the sign and, through the `SubArea` buffer this system used to add, every run. Removing `Owner` from runs is necessary and sufficient
for both the surface and the border-prop path (Game.Objects/RaycastJobs.cs:355-358). So the design is:
- No `Owner` on runs.
- No `SubArea` buffer on the sign.
- No vanilla cascade. The sign -> runs cascade is `SkiRunGateCascadeSystem`, keyed on `SkiRunBinding.m_Gate`.

Our run `SurfacePrefab`s do not carry `Game.Prefabs.ServiceUpgrade` (runs are launched from the hub). Every downstream upgrade behavior
was Object-gated in vanilla anyway:
- A drawn run never gets `Owner` in game mode (AreaToolSystem.cs:2105-2111 is editor-only).
- `Temp.m_Cost` is written on the object path only.
- There is no `InstalledUpgrade`.
- There is no tether.

## The decision table

One table (`SkiRunBindingRules.Decide`, pure, tested by tests/BindingBatch) serves both entry points, so the load pass and the sweep
cannot disagree:

* `MigrateLegacy`: `Owner` is our sign, or a dead owner. The row does these steps:
  * Drop the owner's `SubArea` entry first (once `Owner` is gone, `SubAreaReferencesSystem` never removes it, because its query is
    Owner-gated: Game.Areas/SubAreaReferencesSystem.cs:122-134).
  * Remove `Owner`.
  * Add `SkiRunBinding {owner if live else Null, Paid, cost}`.
  * Never charge: every Owner(sign) run was charged when the old sweep added `Owner`.
* `LeaveForeign`: a live non-sign `Owner`. The row leaves it untouched and counts it.
* `KeepBound`: binding to a live sign. The row does nothing. A relocated sign keeps its runs at any distance (the tether is checked at
  bind time only).
* `RebindFree`: binding to a dead sign + a sign within `TetherM`. `m_Gate` = nearest, no charge.
* `Orphan`: binding to a dead sign, none in tether. `m_Gate` = Null (WARN on the transition only), `FlagPaid` kept. This is a
  "paid orphan": unbound for snow, bills and score, and it re-links free later. The row is reached only when our cascade missed the sign's
  delete or the sign was gone at save time. Never delete a run on an inferred event: a broken reference at load would otherwise wipe the
  resort.
* `RestoreFree`: load pass only. No binding, no `Owner`, a sign in tether. The result is binding `{sign, Paid|Restored, 0}`, no charge
  (uninstall -> save -> reinstall re-links free). The residual exploit is at most one bind fee per run, accepted.
* `BindCharge`: sweep only. No binding, no `Owner`, a sign in tether. The result is binding `{sign, Paid, cost}` + `Charge(cost)`. The
  row is refused (counted unbound, WARN once) before this load's load pass completed (`LoadPassDone`).
* `LeaveUnbound`: no sign in tether. Nothing happens (unbound runs stay bare, bill nothing, score nothing:
  `SkiSnowInjectionSystem.IsRunBound` -> `IsBound`).

## The load pass

The pass runs in `OnGameLoadingComplete`, after `ResolvePrefabs()` and before `m_Armed = true`. `GameManager` invokes
`onGameLoadingComplete` from the load continuation (Game.SceneFlow/GameManager.cs:1126) while `isGameLoading` is still true (:262-272).
`GameSystemBase` routes it inside a try/catch (Game/GameSystemBase.cs:29, :60-70). No sweep can interleave. GameSimulation ticks zero times
while loading (Game.Simulation/SimulationSystem.cs:202-219), and the trigger and cascade systems guard `isGameLoading`. Structural
EntityManager changes in that callback have a vanilla precedent (Game.Tutorials/TutorialSystem.cs:301, :313-314).

Steps, collect first and mutate after:

1. Migrate legacy runs.
2. Sign `SubArea` buffers: remove entries pointing at one of our runs or a non-existing entity. Then remove the buffer itself when it is
   empty and the sign's prefab has no `Game.Prefabs.SubArea` buffer. The vanilla load rebuilder appends every Area+Owner into an
   existing buffer (Game.Serialization/SubAreaSystem.cs:27-40, :75), and `DestroySystem` adds one for rubble
   (Game.Objects/DestroySystem.cs:146-147, :182-185), so rubble entries are kept. It is unverified whether the buffer's presence survives
   a load. The pass handles both.
3. Restore lost links free.
4. The pass adds no `Updated` (the decal anchor and height mode re-derive, and a one-time decal rotate/shift on migrated runs is
   expected).
5. Publish `Last*` and `LoadPassDone`. Log ONE "Binding: load pass" line every load. The pass never calls `Charge`.

### Load-pass failure

A run that still carries `Owner(sign)` keeps the one-run-bulldoze trap (bulldozing it deletes the sign and, through the cascade,
every bound run, Game.Areas/RaycastJobs.cs:59-93). So a throw must not stop the sweep's non-charging late migration:

* Each run's migration has its own try/catch. The first failure per load logs "!!!" naming the run and the trap. Steps 2-3
  still run.
* An exception anywhere in the pass (or any per-run migration failure) sets `m_LoadPassFailed` and leaves `LoadPassDone` false. The
  result: no charges this load and no `RestoreFree`, but not `m_Disabled`. The sweep keeps running with only the non-charging rows
  (`MigrateLegacy`, `RebindFree`, `Orphan`) plus step 2's cleanup. It starts on the first frame after the load, paused too
  (`SkiRunBindTriggerSystem` fires `SweepNow` once on `LoadPassRetryPending`).
* The sweep keeps its own one-shot "!!!" (a throw there sets `m_Disabled`).
* `MigrateRun` adds the binding before it removes `Owner`. So a run interrupted mid-migration carries `Owner` + binding (row
  `MigrateLegacy` finishes it, no charge), never neither.

The cascade keeps working throughout (it reads `PrefabSetsReady`, not `m_Disabled`). The next load re-runs the pass (a run left with
neither `Owner` nor binding re-links free there).

## The sweep

Two entry points start the sweep:
- `OnUpdate` (GameSimulation, interval 64, anchored before `SkiSnowInjectionSystem` in Mod.cs).
- `SweepNow(why)` (`SkiRunBindTriggerSystem` at ModificationEnd, paused or not, on a Created run of ours or a Created sign).

The first guard is `GameManager.isGameLoading`. The sweep uses the same table with `LoadPass = false`. A legacy run found here is migrated
with a WARN (no charge).

The system logs the census line `Ownership: B bound + U unbound + O orphaned run(s), G gate(s)` on any change. While nothing changes, it
logs the line at most every `CensusHeartbeatS`.

Edits never re-create a run (ApplyAreasSystem.cs:123-131, :167-202: Node copy + `Updated` on the original entity, which keeps its
`SkiRunBinding`). `Charge()` runs only right after the synchronous `AddComponentData(SkiRunBinding)` of a run that had none, so
the trigger and the cadence sweep cannot double-charge. `CollectGates` / `CollectRuns` complete their queries' dependencies first:
chunk reads through type handles do not wait for running jobs (Entities semantics, unverified here). This is required at ModificationEnd,
where Modification jobs are in flight.

## Downgrade, XP and fees

Downgrade hazard (see the `SkiRunBinding` header): an older build loads a newer save without error, then re-binds and re-charges
every run. XP: deliberately none (a redraw would be an XP faucet).

Fees: `RunFeeFor(prefab, plan area)` = `SkiRunFees.Fee(KindOf(prefab), Geometry.m_SurfaceArea)`, carried by `RunPlan.AreaM2` from
`CollectRuns`. `BindCharge` charges it and stores it in `SkiRunBinding.m_PaidAmount`. The load pass's `MigrateLegacy` stores it as
paid (never charged). Reshape: no re-charge, no refund. Delete: no refund. The hub and the drawing tooltip read the same function.

## Helpers (internal, main thread)

- `IsArmed`.
- `PrefabSetsReady` (sign set + >= 1 run set resolved, independent of `m_Disabled`: the cascade's and the draw feedback's gate).
- `LoadPassDone` / `LoadPassFailed` / `BindsNewRuns` / `LoadPassRetryPending`.
- `IsRunPrefab`, `IsGatePrefab`, `IsLiveGate`.
- `IsBound` (the bound test: binding + live sign), `TryGetBoundGate`.
- `TryNearestGate` / `TryNearestGateEntity` (a managed scan of every building: recompute or transition only).
- `RunFeeFor`.
- `KindOf` (the hub's `RunRow.kind`, -1 = not a run).
- `BindGeneration` (a counter bumped on every binding write, which raises no `Updated` tag, so `SkiRunCatalogSystem` reads it in its
  change signature).
- `SweepNow`.

Temps never carry `SkiRunBinding` (GenerateAreasSystem.cs:256-273): callers resolve `Temp.m_Original` before `IsBound`.
`TetherMeters` is the one tether constant (`Tuning.TetherM`) that `SkiResortStatsSystem`'s lift / station resort rule reads too.

## Scope and logging

Game mode only. House contract:
- "created" line.
- One ARMED line per load listing every constant.
- `m_Armed` / `m_Disabled` / `m_ErrorLogged` reset in `OnGameLoadingComplete`.
- One-shot "!!!" catch that disables binding until the next load.
- No per-frame work.
