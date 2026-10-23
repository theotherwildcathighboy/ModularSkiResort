# SkiDaySystem: scheduling anchor, the pass, claim writes into vanilla state, tracking and the cost measurement

Decompile baseline `Game.dll` 1.6.0f1 (1.6.2f1 line numbers are given where they moved).

## Phase and anchor

GameSimulation, anchored `UpdateAfter<SkiDaySystem, Game.Simulation.LeisureSystem>` (`Mod.cs`). Anchor AND interval are load-bearing:
`LeisureSystem` is UpdateAt GameSimulation (`SystemOrder.cs:377`, 1.6.2 :378) and `GetUpdateInterval` returns `LeisureSystem`'s own
(262144 / `kUpdatePerDay` = 64, `LeisureSystem.cs:914-917`, 1.6.2 :911-914), so this follower inherits its offset
(`UpdateSystem.cs:431-436`, :355-356, :457-516) and every pass runs on `LeisureSystem`'s frames, before `TripNeededSystem`
(`SystemOrder.cs:393`, 1.6.2 :394). NEVER override `GetUpdateOffset`. Pause stops it; the Editor never arms it.

## The pass (one per LeisureSystem interval)

1. Complete last pass's jobs.
2. `ApplyPass` copies every job result into managed state.
3. Month roll.
4. `ApplyCensus`.
5. Powder ring (one `ClimateSystem.SampleClimate` per game hour; main thread only: `PrefabSystem.GetPrefab` +
   `AnimationCurve.Evaluate`, `ClimateSystem.cs:625-654`).
6. Area snapshot, rebuilt only on change; 6b powder year table.
7. No ski area: idle (claims in flight resolve as orphans).
8. Schedule `SiteJob` (when due), `ResolveJob`, `ClaimJob` (plus the car-reserve handshake), `CensusJob` (hourly).

No EntityManager read of a citizen, lane or Transform and no CompleteDependency: citizens are walked through
`ToArchetypeChunkListAsync` + IJob, positions through RO lookups in the jobs. The one exception is the timed `[cost]` pass.

## Writes into vanilla state

The claim writes `Leisure` and `TripNeeded` (RW type handles in `ClaimJob`: ECS orders it after `LeisureSystem`'s job and
`TripNeededSystem`'s after it, no main-thread wait) and enqueues on `CitizenBehaviorSystem.GetCarReserveQueue` with
`AddCarReserveWriter` (`WorkerSystem.cs:450-454`; `CitizenBehaviorSystem.cs:1094-1103`, 1.6.2 :1126-1135). Those are the only writes
to vanilla state, plus `ResolveJob` lowering that claim's `Leisure.m_LastPossibleFrame` when the hill closes early: a guest on site
then leaves with the new closing, at least `SkiDayTiming.OnSiteGraceFrames` from then, and vanilla's `LeisureSystem` still ends the
visit (`LeisureSystem.cs:314-317`, 1.6.2 :308-311). A ski day given before a load finishes as a vanilla leisure trip.

## Tracking

`m_Claims` is compacted every pass by `ResolveJob` (outcome per claim: `SkiDayTiming.Resolve`). Arrivals and failures feed the
per-area, per-class `ClassTrack` (confidence, learned speed, "cannot reach"), kept in `AreaMemory` (not named `AreaState`, which here
is `SkiDayRules.AreaState`). Claim states 2 (visit over, still inside), 3 (expired, watched up to 6 game hours for a late arrival)
and 4 (followed after leaving) exist for measurement only: none is ever on the way, on site, in `ActiveNow` or in a view, and a late
arrival feeds only the all-outcomes speed.

## Month and views

Per area This / Last (`SkiDayRules.MonthFlags`, the ledger's rules); a rolled month with fewer than 95 % of its frames simulated
closes "partial" (`SkiMoneyRules.IsShortMonth`). Views are published every armed pass while an area exists (also with both knobs at
0: the powder mark is a snow fact); `Revision` moves only when a view is not Same.

Measurement state (the RM / RH / CM rows in `SkiDaySystem.Jobs.cs`, the ring of ended ski days, the DUMP) is memory only and resets
at load. Two measured features change behavior: SystemOff (a switched-off vanilla trip system stops ski days) and the deadline pull.

## Cost

`[cost]` is measured once per load over `Tuning.CostPasses` passes that scheduled `ClaimJob`, then one pass is completed at once (its
vanilla inputs are completed first and timed apart, never in our number); re-armed daily at midday. It is NOT in any main-thread
figure: our one dependency handle covers `ResolveJob`, `ClaimJob` and `CensusJob` together, and those jobs read about 20 types that
the game's movement and AI jobs write, so on a pass frame those writers wait for the whole chain.

## Lifecycle contract

"created" line; per-load reset and arm in `OnGameLoadingComplete` (Game mode only); `OnGamePreload` completes and books the last
pass, writes the month in progress (try/caught: `GameSystemBase.cs:85-96` disables a system whose `OnGamePreload` throws); one-shot
"!!!" and disabled for the load on an exception; `OnDestroy` completes, flushes and disposes.
