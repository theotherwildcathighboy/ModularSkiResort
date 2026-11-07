# SkiDaySystem: scheduling anchor, the pass, claim writes into vanilla state, tracking and the cost measurement

Decompile baseline `Game.dll` 1.6.0f1 (1.6.2f1 line numbers are given where they moved).

## Phase and anchor

The system runs in GameSimulation, anchored `UpdateAfter<SkiDaySystem, Game.Simulation.LeisureSystem>` (`Mod.cs`). The anchor AND the
interval are load-bearing:

- `LeisureSystem` is UpdateAt GameSimulation (`SystemOrder.cs:377`, 1.6.2 :378).
- `GetUpdateInterval` returns `LeisureSystem`'s own interval (262144 / `kUpdatePerDay` = 64, `LeisureSystem.cs:914-917`, 1.6.2 :911-914).
- So this follower inherits the offset of `LeisureSystem` (`UpdateSystem.cs:431-436`, :355-356, :457-516).
- So every pass runs on `LeisureSystem`'s frames, before `TripNeededSystem` (`SystemOrder.cs:393`, 1.6.2 :394).

NEVER override `GetUpdateOffset`. A pause stops the system. The Editor never arms it.

## The pass (one per LeisureSystem interval)

1. Complete last pass's jobs.
2. `ApplyPass` copies every job result into managed state.
3. Month roll.
4. `ApplyCensus`.
5. Powder ring. It makes one `ClimateSystem.SampleClimate` per game hour. It runs on the main thread only (`PrefabSystem.GetPrefab` +
   `AnimationCurve.Evaluate`, `ClimateSystem.cs:625-654`).
6. Area snapshot, rebuilt only on change. 6b: powder year table.
7. No ski area: the pass idles (claims in flight resolve as orphans).
8. Schedule `SiteJob` (when due), `ResolveJob`, `ClaimJob` (plus the car-reserve handshake), `CensusJob` (hourly).

The pass makes no EntityManager read of a citizen, lane or Transform, and no CompleteDependency. Jobs walk citizens through
`ToArchetypeChunkListAsync` + IJob. Jobs read positions through RO lookups. The one exception is the timed `[cost]` pass.

## Writes into vanilla state

The claim writes `Leisure` and `TripNeeded`. These are RW type handles in `ClaimJob`. ECS orders `ClaimJob` after `LeisureSystem`'s job,
and `TripNeededSystem`'s job after `ClaimJob`. There is no main-thread wait. The claim also enqueues on
`CitizenBehaviorSystem.GetCarReserveQueue` with `AddCarReserveWriter` (`WorkerSystem.cs:450-454`, `CitizenBehaviorSystem.cs:1094-1103`,
1.6.2 :1126-1135). Those are the only writes to vanilla state, plus one more: `ResolveJob` lowers that claim's
`Leisure.m_LastPossibleFrame` when the hill closes early. A guest on site then leaves with the new closing, at least
`SkiDayTiming.OnSiteGraceFrames` from then. Vanilla's `LeisureSystem` still ends the visit (`LeisureSystem.cs:314-317`, 1.6.2 :308-311).
A ski day given before a load finishes as a vanilla leisure trip.

## Tracking

`ResolveJob` compacts `m_Claims` every pass (outcome per claim: `SkiDayTiming.Resolve`). Arrivals and failures feed the per-area,
per-class `ClassTrack` (confidence, learned speed, "cannot reach"). `AreaMemory` keeps the `ClassTrack`. The name is not `AreaState`,
because `AreaState` here is `SkiDayRules.AreaState`. These claim states exist for measurement only:

- State 2: visit over, still inside.
- State 3: expired, watched up to 6 game hours for a late arrival.
- State 4: followed after leaving.

None of these states is ever on the way, on site, in `ActiveNow` or in a view. A late arrival feeds only the all-outcomes speed.

## Month and views

Each area has a This and a Last month (`SkiDayRules.MonthFlags`, the ledger's rules). A rolled month with fewer than 95 % of its frames
simulated closes "partial" (`SkiMoneyRules.IsShortMonth`). The system publishes views every armed pass while an area exists. It also does
so with both knobs at 0, because the powder mark is a snow fact. `Revision` moves only when a view is not Same.

Measurement state is memory only and resets at load. It includes the RM / RH / CM rows in `SkiDaySystem.Jobs.cs`, the ring of ended ski
days and the DUMP. Two measured features change behavior: SystemOff (a switched-off vanilla trip system stops ski days) and the deadline
pull.

## Cost

The system measures `[cost]` once per load over `Tuning.CostPasses` passes that scheduled `ClaimJob`. Then it completes one pass at once.
It completes the vanilla inputs of that pass first and times them apart, so they are never in our number. It re-arms the measurement daily
at midday. The `[cost]` number is NOT in any main-thread figure. Our one dependency handle covers `ResolveJob`, `ClaimJob` and `CensusJob`
together. Those jobs read about 20 types that the game's movement and AI jobs write. So on a pass frame, those writers wait for the whole
chain.

## Lifecycle contract

- "created" line.
- Per-load reset and arm in `OnGameLoadingComplete` (Game mode only).
- `OnGamePreload` completes and books the last pass, and writes the month in progress. It is try/caught, because `GameSystemBase.cs:85-96`
  disables a system whose `OnGamePreload` throws.
- On an exception: one-shot "!!!", and the system is disabled for the load.
- `OnDestroy` completes, flushes and disposes.
