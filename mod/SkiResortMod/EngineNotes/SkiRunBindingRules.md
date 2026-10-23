# SkiRunBindingRules: the run bind decision table and the facts it reads

One pure function, used by both the load pass and the sweep of `SkiRunOwnershipSystem`, so the two can never disagree about what a
run's state means. tests/BindingBatch links the file and asserts every one of the 2^7 fact combinations, the priority order,
idempotence and the money rules. `SkiRunFees` is pure too (System.Math written out).

## The table

The first matching row wins.

1. `HasOwner && OwnerIsOurSign`: MigrateLegacy. Drop the sign's SubArea entry, remove Owner, binding {owner if live else Null, Paid,
   cost}; never charge.
2. `HasOwner && !OwnerIsOurSign`: LeaveForeign. Untouched, counted, never charged.
3. `HasBinding && GateLive`: KeepBound.
4. `HasBinding && !GateLive && SignInTether`: RebindFree. `m_Gate` = nearest sign, Paid kept, "[rebind] ... no charge".
5. `HasBinding && !GateLive`: Orphan. `m_Gate` = Null (if it was not), Paid kept, WARN once per run.
6. `!HasBinding && SignInTether && LoadPass`: RestoreFree. Binding {sign, Paid|Restored, 0}, "[restore]".
7. `!HasBinding && SignInTether`: BindCharge. Binding {sign, Paid, cost} + Charge(cost) (refused before LoadPassDone).
8. Otherwise: LeaveUnbound.

## Facts

Built by `SkiRunOwnershipSystem`:

- `GateNull`: `m_Gate == Entity.Null`, "never had a sign" vs "sign died". It steers only the effect of Orphan; Decide never reads it.
- `GateLive`: `ownership.IsLiveGate`: != Null, exists, not Deleted, PrefabRef in the sign prefab set.
- `OwnerIsOurSign`: the owner is a sign of ours or no longer exists. Only our old sweep ever wrote Owner on a drawn run; vanilla never
  does in game mode (AreaToolSystem.cs:2105-2111, editor-only; 1.6.2 :2112-2118), so a dead owner on one of our runs is legacy; a live
  non-sign owner is foreign.
- `SignInTether`: a live sign lies within TetherM of the run's `Geometry.m_CenterPosition.xz`.
- `LoadPass`: the load pass in OnGameLoadingComplete. A paused SweepNow is not one: there an unbound run in tether is a fresh draw and
  pays.

## Never delete on an inferred event

No row deletes a run. A broken reference at load would otherwise wipe the resort. A paid orphan is the one exception, when the cascade
was missed.
