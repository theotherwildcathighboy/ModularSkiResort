# SkiResortStatsSystem: ski-area membership, hub rebuild rules and the per-area numbers

`SkiResortStatsSystem` computes ski-area membership and every per-area number of the Ski Resort hub. `SkiResortHubUISystem` publishes
these rows. The stats system owns no binding and writes nothing to the world. The hub never recomputes the score. It reads every score
number from the last recompute of `SkiResortAttractivenessSystem`.

## Membership

Nothing is stored except the names written by `SkiRunBindTriggerSystem`.

- Ski area = a live Ski Area Welcome Sign (a Destroyed sign stays live: `SkiRunOwnershipSystem.IsLiveGate`).
- Run = the Gate of its `SkiRunCatalogSystem` row (sticky from bind time, re-links free only when its sign died). Entity.Null means
  Not linked.
- Lodge = a lodge-prefab building (top-level, no Owner), active unless its Building carries `BuildingOption.Inactive`
  (BuildingUtils.cs:351). A lodge linked to no live sign is free-standing: Not linked. The lodges, the school and the patrol huts take
  the link of the attraction snapshot (`CopyTopLevelLinks`: nearest live sign within the tether). Each adds its jobs and its own upkeep
  and wages to its area.
- Lift = the nearest live sign within `SkiRunOwnershipSystem.TetherMeters` of its base station (else top station, else the start node
  of the anchor edge), ties by distance, Index, Version (`SkiResortIndex`). None means Not linked. The system works it out every
  rebuild and saves nothing: a new nearer sign takes the lifts but not the runs.
- Station (world selection) = the resort of the first census row that touches it, else `TryResolveResort(its xz)`. That call rebuilds
  the sign index on the spot when it was not built this load or the building count moved (event time only).

## Rebuild

The system runs at UIUpdate, registered between `SkiRunCatalogSystem` and `SkiResortHubUISystem` (UpdateSystem.cs:158-161). It runs
only while Demand is set (the hub sets it every frame) or while a rebuild is requested. It does nothing while the hub is closed. It
runs in these cases:
- The change signature moves (catalog, census, ChairPinSystem loop, attractiveness, injection texel / snow, guest counter, lift charge
  and SkiDaySystem revisions), at most once per `MinRebuildIntervalS`.
- At once on the rising edge of Demand.
- On a 2 s wall-clock heartbeat. The heartbeat covers renames, jobs, the provider walk, the top temperature, the counted month
  fraction, a moved service budget and the settings.

The building count is not in the signature. Per-array content hashes gate the revision of each array. City is a value that the hub
dedups. The slow / live row split keeps the header, switcher and run identity from re-rendering on every snow, guest or money change.
Strings are built only in a rebuild, so there is no per-frame allocation. Cost: one building walk, one provider walk, one
`CopyServeLifts` (a power read per Complete row) + Assign + `CountInReach`, and one `GetServiceBudget` per service. `Stats [cost]` is written once per
load.

## Rules worth knowing
- A lift row's `RunsServed` is `LiftServedRule.CountInReach`: run tops in the lift's reach, power ignored. A lift with no power or with
  power pending reads its runs, and its status word gives the reason it does not run. The power-gated served count stays for service.
  OPEN: a lift with power still pending (paused right after a load) reads "Running" with capacity while its chairs are held.

- Not linked rows: -1 for every score, guest and ticket number. Its lifts still bill, runs do not.
- Net = `HubRules.NetMonth` over what the ledger booked this month and buildings / wages x the share of the month simulated. The eight
  terms go out as rows that add up to it, "~" when a term is unknown.
- Buildings and wages: CityServiceBudgetSystem.cs:392-421 for the sign split into wage part and rest. The value is that split x the
  current service budget (`GetServiceBudget`, once per service per rebuild; it completes its temp arrays, :1060-1072) x
  daysPerYear / 12. The value is -1 when the prefab has no service upkeep or the budget reads 0.
- Last month: the system sends it whenever the ledger knows one (a since-load month too: LastMonthFull false, the hub marks it
  "(partial)"). It sends -1 only before the first month roll. Tickets off (price 0) gives 0, not estimated. The ticket gate is
  `SkiMoneyRules.TicketGateOf` with the counted runs of the Result, known = a result present and not in the first-write hold.
- Gun word: `HubRules.Season` reads the live gun gates once the injection has measured a mask this load and is active. The gates are
  `AreaWork.AnyGunOn` = a bound snowmaking or night run with active texels in the per-run map of the injection. Before that, it uses
  the mean test. An inactive injection reads "no gun running".
- Road edges: the building walk reads `Building.m_RoadEdge` (set by RoadConnectionSystem.cs:551 / :606 / :634) of every live sign and
  lodge. Entity.Null gives the SignNoRoad / LodgeNoRoad rows.
  - SignNoRoad shows only while the sign carries jobs (`MountainJobsRules.SignNoRoadRow`, `MountainJobsSystem.SignCarriesJobs`: no
    active road lodge in the area and a switched-on carried building). It follows the jobs poll, so it can trail a new road lodge by a
    heartbeat.
  - LodgeNoRoad counts lodges with no road and no cat track chain whose row needs one (`LodgeSupplyRule.CountsAsNoRoad`, Hut and Summit
    lodges are roadless by design).
- Ski days: `ResortLive` carries the last published view of `SkiDaySystem` (main thread, no job sync). The values are -1, State and
  Limit 0, when the system is absent, not armed or has no view of the sign. "Off" = both ski-day settings 0, read once per rebuild. The
  Efficiency row's hysteresis state (`m_EffRowOn`) lives here, and the rule lives in `HubRules`. The system resolves `SkiDaySystem` and
  `SkiLodgeParkingSystem` at each load (`GetExistingSystemManaged`: the creation order stays Mod.cs's, null-safe). The DUMP is the one
  caller of their `AppendDump`.
- Trip planner: `CityStats.TripPlanner` is `SkiDaySystem.TripPlannerLabel` ("" when the game's own trip systems all run, else the
  name(s) of the replacement or "?" = `SkiTripSystems.UnknownLabel`). `TripPlannerBlocksSkiDays` = LeisureSystem or
  CitizenBehaviorSystem off.
- `IfLodgesOn` = `HubRules.AttractionWithLodgeApplied` with the ladder over every lodge of the area switched on, summit-served by the
  lifts of this area. The system computes it only for the LodgesAllOff row that shows it.
- Lift capacity: Loop types riding x SeatsPerCarrier x SpeedMps x 3600 / loop. A Shuttle (the tram) reads `TramShuttleSystem` (null-safe,
  resolved per load). State 0 = a shuttle path with >= 1 cabin on it (status Running, or NoPower when unpowered), else 1. Chairs =
  `CabinsHostedBy(base station)` (a tram edge carries no carriers). Capacity = `HubRules.ShuttleCapacityPerHour` only while the mover is
  Running. Ride time = `HubRules.ShuttleRideTimeS` while a path exists. The mover has no generation counter. So a path that appears, or
  a cabin count that moves, reaches the hub within the `HeartbeatS` rebuild while the panel is open.
- Lift bends: `LiftStats` carries the C#-only fields `BendTenths` (`LiftCensus.BendWarnTenths` of the `BendDeg` of the census row),
  `BendTower` and `BendNode`. They are not written to the LiftRow binding. They are hashed in HashLifts (gate tools/check-stats-hash.js).
  They feed `HubProblemLift`. The Subject of the LiftBend problem row is `BendNode` and its SubjectTop is Null, so the focusLift click
  of the UI flies to that tower.
- Every lift row that does not carry (`LiftServedRule.Carries`: complete, powered at its base station, not closed) shows capacity -1
  ("-") and is left out of the area total (`HubRules.LiftCapacityShown`). `powered` is the one reading of the base station by the census
  (the top station is fed through the modeled lift line). It is the `LiftScoreView.Powered` of the score, else `CopyServeLifts`, both
  through `LiftCensusSystem.ReadLiftPower` and its flicker guard. Power never lowers the bill (`SkiMoneyRules.LiftUpkeepBillingOf`).

## Logs

- `Stats [arm]` once per load.
- `Stats [rebuild] (why): ...` only when a count or a band problem changed, or on a request / the hub opening, through a LogWindow.
- Every other rebuild is counted into ONE quiet summary line per `QuietSummaryS`. The system flushes it when the hub closes and at a
  load. At quit, it flushes only a pending change fold, because the quit runs after Mod.OnDispose (GameManager.cs:792-793).
- `Stats [problems]` and `Stats [cost]` once per load.
- One-shot `!!!` that disables the system until the next load (the hub keeps the last rows).

## Near ResortLiveStats.CityIncomeEst

`CityIncomeEst` = the SharePct of the resort of the TouristIncome statistic of the game
(`CityStatisticsSystem.GetStatisticValue(StatisticType.TouristIncome)`). It is an int in money: the total of the last entry, the net
money that tourist households brought in (HouseholdInitializeSystem.cs:240-244) minus what they take home
(HouseholdMoveAwaySystem.cs:173). The game samples it `kUpdatesPerDay` = 32 times a game day (CityStatisticsSystem.cs:363).
BudgetSystem.cs:172-174, 280 reads it the same way. -1 means unknown, no share, or Tourism Overhaul detected (the UI hides the row
then). It is an estimate: the window of the statistic (cumulative / daily) is prefab data, not in the decompile.
