# SkiLiftChargeSystem: how the monthly lift running charge is billed, resolved to a ski area and logged

The lift running charge works as follows:

- Every lift census row bills `SkiMoneyRules.LiftRate` per month (the lift type's two rates, `SkiCatalog.LiftByIndex(row Type)`, else the
  fixed-grip row).
- The charge accrues like the run upkeep and debits from PlayerMoney.
- The ledger books each lift's share under its ski area.

## Rate and power

The charge is full when the lift is complete. It is reduced (Loop types 25 %, the tram 50 %, `LiftClosedRule.NoOpenRunShare`) in two cases:

- The lift is unfinished (any census state but Complete, lone stations included).
- The lift is complete, but its ski area has no counted run (NoOpenRun).

Power does not change the bill (`SkiMoneyRules.LiftUpkeepBillingOf` has no power input). An unpowered complete lift bills in full. The
real power stays in these places:

- LiftCharge.Powered.
- The Operating count (complete && powered). The ticket gate line and the DUMP print it. It does not feed the gate.
- The `Lifts [state]` "no power" word.
- The power poll.

LiftBilling.NoPower stays in the enum and the state switch, unreachable from here.

Power read: `LiftServedRule.IsPowered(LiftCensusSystem.ReadLiftPower(base station))`. It reads the base station's electricity warning flags
with the game's 2-dispatch debounce (DispatchElectricitySystem.cs:126-158). The score, the served rule and the hub all use this one reader
and predicate. The code reads power only for Complete rows, because an unfinished lift must not raise a `Lift [power] ... no power` line.
For the other rows, Powered = true (not read), and the state line says "power not read".

## Ski area

The code calls `SkiResortIndex.TryResolve(xz, SkiRunOwnershipSystem.TetherMeters)` at the first of these positions that exists:

1. The live base station's Transform xz.
2. The live top station's Transform xz.
3. The anchor edge's start node (Game.Net.Edge.m_Start -> Game.Net.Node).

Live = exists, not Deleted, has a Transform. If no sign is within 5,000 m, the lift is Not linked (Entity.Null), which is billed. The code
rebuilds the sign index on every snapshot from {Building, Game.Buildings.Park, PrefabRef, Transform} None {Temp, Deleted} (nothing saved).

## Snapshot

The system takes a snapshot of the published LiftCharge / ResortLiftCharge tables in these cases:

- On the first tick after a load (the anchor tick, which bills nothing).
- On every charge pass.
- When `LiftCensusSystem.Revision` moved.
- When the power signature moved (`LiftCensusSystem.ComputePowerSignature` polled every Tuning.PowerPollTicks ticks = 256 sim frames).

It takes at most one snapshot per Tuning.SnapshotMinFrames sim frames. Revision++ when any value changed. The system reads the area's run
state (`SkiResortAttractivenessSystem.TryGetAreaScore`: released score, RunsCounted) on each snapshot. Every charge pass snapshots right
before it bills. So a run that closes or opens bills from the next pass. The transition pass bills its whole interval at the new rate.

## Billing

Billing follows the run-upkeep pattern (`SkiRunUpkeepSystem`):

- The system gates itself every Tuning.ChargeIntervalFrames elapsed sim frames (SimulationSystem.frameIndex, wrap-safe uint).
- Accrual: `SkiMoneyRules.Accrue` (carry += rate x elapsed / month, with month = TimeSystem.kTicksPerDay x daysPerYear / 12, TimeSystem.cs:18).
- Debit: PlayerMoney.Subtract on the City entity (PlayerMoney.cs:38-41, clamped +-2e9).

Each ski area's exact AccruedShare goes into the ledger. The shares sum to what accrued, not to the floored debit. The difference is the
carry, < 1 credit. Nothing is owed for the time before a load. The carry is separate from the run upkeep's carry. The Economy panel does
not show the charge (closed enums, BudgetUISystem.cs:61-66). The hub says so.

Reduced precedence: Unfinished, then NoOpenRun. `SkiMoneyRules.LiftAreaRunOpen` false = linked, the area's score released and 0 runs
counted. A lift bills full in these cases: not linked, no score yet, or the first score after a load.

## Phase

GameSimulation, `UpdateAfter<SkiLiftChargeSystem, SkiRunUpkeepSystem>` (Mod.cs). It keeps the two monthly debits next to each other in the
frame and the log. It is not load-bearing. GetUpdateInterval 4 = the upkeep's slot (anchored children with equal intervals inherit the
anchor's offset, UpdateSystem.cs:400-431). The real cadence is self-gated. Pause stops the meter. The Editor never arms it.

## Cost

Between passes, the cost is a frame-index compare, an int compare of the census revision and a tick counter. A snapshot costs O(lift rows +
Park buildings) plus one ElectricityConsumer read per Complete row (the read waits for the dispatch job if one is in flight). A snapshot is
also a Transform sync point. The sign index (m_SignQuery.CompleteDependency) and TryLiveXz complete the move systems' Transform writers.
This happens once per snapshot, never per frame. The positions are needed (a station or sign moved by Move It re-resolves). The system
measures `Lifts [cost]` once per load, after Tuning.CostPasses charge passes whose snapshot had a lift row.

## Logs

- created. ARMED (every type's rate).
- `Lifts [charge]` when the rounded rate changes, else every Tuning.LogEveryNthCharge-th pass. It logs through a LogWindow as
  `SkiRunUpkeepSystem.Charge`, with the full / reduced count per type.
- `Lifts [state]`: the first snapshot of a load lists every lift plain, at most Tuning.StateLinesPerPass + "and N more". Later changes go
  through m_StateLog, a 10 s LogWindow. The reason: a brownout can flip a base station's warning every 384 frames.
- `Lifts [idle]` per reason transition. The no-PlayerMoney WARN once per load. `Lifts [cost]` once.
- The one-shot `!!!` (disabled for the load: the system clears the tables and bills nothing).

The system flushes folds in these places:

- On its own tick (TrailingDue), before an idle line.
- In OnGamePreload (try/caught: GameSystemBase.cs:85-96).
- In OnDestroy (LogWindow.cs header).

Per-load reset in OnGameLoadingComplete (Game mode only).
