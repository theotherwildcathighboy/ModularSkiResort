# SkiLiftChargeSystem: how the monthly lift running charge is billed, resolved to a ski area and logged

The lift running charge: every lift census row bills `SkiMoneyRules.LiftRate` per month (the lift type's two rates,
`SkiCatalog.LiftByIndex(row Type)`, else the fixed-grip row), accrued like the run upkeep and debited from PlayerMoney, each
lift's share booked in the ledger under its ski area.

## Rate and power

Full when complete; reduced (Loop types 25 %, the tram 50 %, `LiftClosedRule.NoOpenRunShare`) while unfinished (any census state
but Complete, lone stations included) or complete while its ski area has no counted run (NoOpenRun). Power does not change the
bill (`SkiMoneyRules.LiftUpkeepBillingOf` has no power input): an unpowered complete lift bills in full. The real power stays in
LiftCharge.Powered, the Operating count (complete && powered; printed by the ticket gate line and the DUMP, it does not feed the
gate), the `Lifts [state]` "no power" word and the power poll. LiftBilling.NoPower stays in the enum and the state switch,
unreachable from here.

Power read: `LiftServedRule.IsPowered(LiftCensusSystem.ReadLiftPower(base station))`, the base station's electricity warning flags
with the game's 2-dispatch debounce (DispatchElectricitySystem.cs:126-158): the one reader and predicate the score, the served
rule and the hub use. Read only for Complete rows (an unfinished lift must not raise a `Lift [power] ... no power` line);
Powered = true (not read) for the others, and the state line says "power not read".

## Ski area

`SkiResortIndex.TryResolve(xz, SkiRunOwnershipSystem.TetherMeters)` at the live base station's Transform xz, else the live top
station's, else the anchor edge's start node (Game.Net.Edge.m_Start -> Game.Net.Node). Live = exists, not Deleted, has a
Transform. None within 5,000 m means Not linked (Entity.Null), which is billed. The sign index is rebuilt on every snapshot from
{Building, Game.Buildings.Park, PrefabRef, Transform} None {Temp, Deleted} (nothing saved).

## Snapshot

The published LiftCharge / ResortLiftCharge tables, taken on the first tick after a load (the anchor tick, which bills nothing),
on every charge pass, when `LiftCensusSystem.Revision` moved, and when the power signature moved
(`LiftCensusSystem.ComputePowerSignature` polled every Tuning.PowerPollTicks ticks = 256 sim frames), at most once per
Tuning.SnapshotMinFrames sim frames. Revision++ when any value changed. The area's run state
(`SkiResortAttractivenessSystem.TryGetAreaScore`: released score, RunsCounted) is read on each snapshot; every charge pass
snapshots right before it bills, so a run that closes or opens bills from the next pass (the transition pass bills its whole
interval at the new rate).

## Billing

The run-upkeep pattern (`SkiRunUpkeepSystem`): self-gated every Tuning.ChargeIntervalFrames elapsed sim frames
(SimulationSystem.frameIndex, wrap-safe uint), accrual `SkiMoneyRules.Accrue` (carry += rate x elapsed / month; month =
TimeSystem.kTicksPerDay x daysPerYear / 12, TimeSystem.cs:18), PlayerMoney.Subtract on the City entity (PlayerMoney.cs:38-41,
clamped +-2e9). Each ski area's exact AccruedShare goes into the ledger (they sum to what accrued, not to the floored debit; the
difference is the carry, < 1 credit). Nothing is owed for the time before a load. A separate carry from the run upkeep's. Not in
the Economy panel (closed enums, BudgetUISystem.cs:61-66): the hub says so. Reduced precedence: Unfinished, then NoOpenRun
(`SkiMoneyRules.LiftAreaRunOpen` false = linked, the area's score released and 0 runs counted; not linked, no score yet or the
first score after a load bill full).

## Phase

GameSimulation, `UpdateAfter<SkiLiftChargeSystem, SkiRunUpkeepSystem>` (Mod.cs; keeps the two monthly debits next to each other in
the frame and the log, not load-bearing). GetUpdateInterval 4 = the upkeep's slot (anchored children with equal intervals inherit
the anchor's offset, UpdateSystem.cs:400-431); the real cadence is self-gated. Pause stops the meter; the Editor never arms it.

## Cost

Between passes a frame-index compare, an int compare of the census revision and a tick counter. A snapshot is O(lift rows + Park
buildings) plus one ElectricityConsumer read per Complete row (it waits for the dispatch job if one is in flight) and is a
Transform sync point: the sign index (m_SignQuery.CompleteDependency) and TryLiveXz complete the move systems' Transform writers,
once per snapshot, never per frame; the positions are needed (a station or sign moved by Move It re-resolves). `Lifts [cost]` is
measured once per load after Tuning.CostPasses charge passes whose snapshot had a lift row.

## Logs

- created; ARMED (every type's rate).
- `Lifts [charge]` when the rounded rate changes, else every Tuning.LogEveryNthCharge-th pass, through a LogWindow as
  `SkiRunUpkeepSystem.Charge`, with the full / reduced count per type.
- `Lifts [state]`: the first snapshot of a load lists every lift plain, at most Tuning.StateLinesPerPass + "and N more"; later
  changes go through m_StateLog, a 10 s LogWindow, because a brownout can flip a base station's warning every 384 frames.
- `Lifts [idle]` per reason transition; the no-PlayerMoney WARN once per load; `Lifts [cost]` once.
- The one-shot `!!!` (disabled for the load: the tables are cleared, nothing is billed).

Folds are flushed on the system's own tick (TrailingDue), before an idle line, in OnGamePreload (try/caught:
GameSystemBase.cs:85-96) and in OnDestroy (LogWindow.cs header). Per-load reset in OnGameLoadingComplete (Game mode only).
