# SkiRunBindingRules: the run bind decision table and the facts it reads

One pure function serves both the load pass and the sweep of `SkiRunOwnershipSystem`. So the two can never disagree about what a
run's state means. tests/BindingBatch links the file and asserts every one of the 2^7 fact combinations, the priority order,
idempotence and the money rules. `SkiRunFees` is pure too (System.Math written out).

## The table

The first matching row wins.

1. `HasOwner && OwnerIsOurSign`: MigrateLegacy. Drop the sign's SubArea entry. Remove Owner. Set binding {owner if live else Null, Paid,
   cost}. Never charge.
2. `HasOwner && !OwnerIsOurSign`: LeaveForeign. The run stays untouched. The run is counted. The run is never charged.
3. `HasBinding && GateLive`: KeepBound.
4. `HasBinding && !GateLive && SignInTether`: RebindFree. Set `m_Gate` = nearest sign. Keep Paid. Log "[rebind] ... no charge".
5. `HasBinding && !GateLive`: Orphan. Set `m_Gate` = Null (if it was not Null already). Keep Paid. Log WARN once per run.
6. `!HasBinding && SignInTether && LoadPass`: RestoreFree. Set binding {sign, Paid|Restored, 0}. Log "[restore]".
7. `!HasBinding && SignInTether`: BindCharge. Set binding {sign, Paid, cost} + Charge(cost). The charge is refused before LoadPassDone.
8. Otherwise: LeaveUnbound.

## Facts

Built by `SkiRunOwnershipSystem`:

- `GateNull`: `m_Gate == Entity.Null`. It separates "never had a sign" from "sign died". It steers only the effect of Orphan.
  Decide never reads it.
- `GateLive`: `ownership.IsLiveGate`. It is true when the gate is != Null, exists, is not Deleted, and has a PrefabRef in the sign prefab
  set.
- `OwnerIsOurSign`: the owner is a sign of ours or no longer exists. Only our old sweep ever wrote Owner on a drawn run. Vanilla never
  does this in game mode. The vanilla write at AreaToolSystem.cs:2105-2111 is editor-only (1.6.2 :2112-2118). So a dead owner on one of
  our runs is legacy. A live non-sign owner is foreign.
- `SignInTether`: a live sign lies within TetherM of the run's `Geometry.m_CenterPosition.xz`.
- `LoadPass`: the load pass in OnGameLoadingComplete. A paused SweepNow is not a load pass. There, an unbound run in tether is a fresh
  draw and pays.

## Never delete on an inferred event

No row deletes a run. A broken reference at load would otherwise wipe the resort. A paid orphan is the one exception, when the cascade
was missed.
