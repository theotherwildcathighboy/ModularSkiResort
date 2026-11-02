# SkiResortStatsSystem: ski-area membership, hub rebuild rules and the per-area numbers

`SkiResortStatsSystem` computes ski-area membership and every per-area number of the Ski Resort hub. `SkiResortHubUISystem` publishes
these rows; the stats system owns no binding and writes nothing to the world. The hub never recomputes the score: every score number is
read from `SkiResortAttractivenessSystem`'s last recompute.

## Membership

Nothing is stored except the names written by `SkiRunBindTriggerSystem`.

- Ski area = a live Ski Area Welcome Sign (a Destroyed sign stays live: `SkiRunOwnershipSystem.IsLiveGate`).
- Run = its `SkiRunCatalogSystem` row's Gate (sticky from bind time, re-links free only when its sign died); Entity.Null means Not linked.
- Lodge = a lodge-prefab building (top-level, no Owner), active unless its Building carries `BuildingOption.Inactive`
  (BuildingUtils.cs:351). One linked to no live sign is free-standing: Not linked. The lodges, the school and the patrol huts take the
  attraction snapshot's link (`CopyTopLevelLinks`: nearest live sign within the tether); each adds its jobs and its own upkeep and wages
  to its area.
- Lift = the nearest live sign within `SkiRunOwnershipSystem.TetherMeters` of its base station (else top station, else the anchor edge's
  start node), ties by distance, Index, Version (`SkiResortIndex`); none means Not linked. Worked out every rebuild, nothing saved: a
  new nearer sign takes the lifts but not the runs.
- Station (world selection) = the resort of the first census row touching it, else `TryResolveResort(its xz)`, which rebuilds the sign
  index on the spot when it was not built this load or the building count moved (event time only).

## Rebuild

UIUpdate, registered between `SkiRunCatalogSystem` and `SkiResortHubUISystem` (UpdateSystem.cs:158-161). It runs only while Demand (set
by the hub every frame) or a requested rebuild; nothing while closed. It runs when the change signature moves (catalog, census,
ChairPinSystem loop, attractiveness, injection texel / snow, guest counter, lift charge and SkiDaySystem revisions), at most once per
`MinRebuildIntervalS`, at once on the rising edge of Demand, or on a 2 s wall-clock heartbeat that covers renames, jobs, the provider
walk, the top temperature, the counted month fraction, a moved service budget and the settings. The building count is not in the
signature. Per-array content hashes gate each array's revision; City is a value the hub dedups. The slow / live row split keeps the
header, switcher and run identity from re-rendering on every snow, guest or money change. Strings are built only in a rebuild; no
per-frame allocation. Cost: one building walk, one provider walk, one `CopyServeLifts` (a power read per Complete row) + Assign, and one
`GetServiceBudget` per service; `Stats [cost]` is written once per load.

## Rules worth knowing

- Not linked rows: -1 for every score, guest and ticket number; its lifts still bill, runs do not.
- Net = `HubRules.NetMonth` over what the ledger booked this month and buildings / wages x the share of the month simulated; the eight
  terms go out as rows that add up to it, "~" when a term is unknown.
- Buildings and wages: CityServiceBudgetSystem.cs:392-421 for the sign split into wage part and rest, x the current service budget
  (`GetServiceBudget`, once per service per rebuild; it completes its temp arrays, :1060-1072) x daysPerYear / 12; -1 when the prefab
  has no service upkeep or the budget reads 0.
- Last month is sent whenever the ledger knows one (a since-load month too: LastMonthFull false, the hub marks it "(partial)"; -1 only
  before the first month roll). Tickets off (price 0) gives 0, not estimated. The ticket gate is `SkiMoneyRules.TicketGateOf` with the
  Result's counted runs, known = a result present and not in the first-write hold.
- Gun word: `HubRules.Season` reads the live gun gates (`AreaWork.AnyGunOn` = a bound snowmaking or night run with active texels in the
  injection's per-run map) once the injection has measured a mask this load and is active; before that the mean test; an inactive
  injection reads "no gun running".
- Road edges: the building walk reads `Building.m_RoadEdge` (set by RoadConnectionSystem.cs:551 / :606 / :634) of every live sign and
  lodge; Entity.Null gives the SignNoRoad / LodgeNoRoad rows. SignNoRoad only while the sign carries jobs
  (`MountainJobsRules.SignNoRoadRow`, `MountainJobsSystem.SignCarriesJobs`: no active road lodge in the area and a switched-on carried
  building; it follows the jobs poll, so it can trail a new road lodge by a heartbeat). LodgeNoRoad counts lodges with no road and no
  cat track chain whose row needs one (`LodgeSupplyRule.CountsAsNoRoad`; Hut and Summit lodges are roadless by design).
- Ski days: `ResortLive` carries `SkiDaySystem`'s last published view (main thread, no job sync; -1, State and Limit 0, when the system
  is absent, not armed or has no view of the sign). "Off" = both ski-day settings 0, read once per rebuild. The Efficiency row's
  hysteresis state (`m_EffRowOn`) lives here, the rule in `HubRules`. `SkiDaySystem` and `SkiLodgeParkingSystem` are resolved at each
  load (`GetExistingSystemManaged`: the creation order stays Mod.cs's; null-safe); the DUMP is the one caller of their `AppendDump`.
- Trip planner: `CityStats.TripPlanner` is `SkiDaySystem.TripPlannerLabel` ("" when the game's own trip systems all run, else the
  replacement's name(s) or "?" = `SkiTripSystems.UnknownLabel`); `TripPlannerBlocksSkiDays` = LeisureSystem or CitizenBehaviorSystem off.
- `IfLodgesOn` = `HubRules.AttractionWithLodgeApplied` with the ladder over every lodge of the area switched on, summit-served by this
  area's lifts, computed only for the LodgesAllOff row that shows it.
- Lift capacity: Loop types riding x SeatsPerCarrier x SpeedMps x 3600 / loop. A Shuttle (the tram) reads `TramShuttleSystem` (null-safe,
  resolved per load): state 0 = a shuttle path with >= 1 cabin on it (status Running, or NoPower when unpowered), else 1; Chairs =
  `CabinsHostedBy(base station)` (a tram edge carries no carriers); capacity = `HubRules.ShuttleCapacityPerHour` only while the mover is
  Running, ride time `HubRules.ShuttleRideTimeS` while a path exists. The mover has no generation counter: a path appearing or a cabin
  count moving reaches the hub within the `HeartbeatS` rebuild while the panel is open.
- Every lift row that does not carry (`LiftServedRule.Carries`: complete, powered at its base station, not closed) shows capacity -1
  ("-") and is left out of the area total (`HubRules.LiftCapacityShown`). `powered` is the census's one reading of the base station (the
  top station is fed through the modeled lift line): the score's `LiftScoreView.Powered`, else `CopyServeLifts`, both through
  `LiftCensusSystem.ReadLiftPower` and its flicker guard. Power never lowers the bill (`SkiMoneyRules.LiftUpkeepBillingOf`).

## Logs

`Stats [arm]` once per load; `Stats [rebuild] (why): ...` only when a count or a band problem changed, or on a request / the hub
opening, through a LogWindow; every other rebuild is counted into ONE quiet summary line per `QuietSummaryS` (flushed when the hub
closes and at a load; at quit only a pending change fold, because the quit runs after Mod.OnDispose, GameManager.cs:792-793);
`Stats [problems]` and `Stats [cost]` once per load; one-shot `!!!` that disables the system until the next load (the hub keeps the last
rows).

## Near ResortLiveStats.CityIncomeEst

`CityIncomeEst` = the resort's SharePct of the game's TouristIncome statistic
(`CityStatisticsSystem.GetStatisticValue(StatisticType.TouristIncome)`): an int in money, the last entry's total, the net money tourist
households brought in, HouseholdInitializeSystem.cs:240-244 minus what they take home, HouseholdMoveAwaySystem.cs:173; sampled
`kUpdatesPerDay` = 32 times a game day, CityStatisticsSystem.cs:363; BudgetSystem.cs:172-174, 280 reads it the same way. -1 means unknown,
no share, or Tourism Overhaul detected (the UI hides the row then). It is an estimate: the statistic's window (cumulative / daily) is
prefab data, not in the decompile.
