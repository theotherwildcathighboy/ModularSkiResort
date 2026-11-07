# SkiLodgeParkingSystem: how the lodge parking census counts car spaces and when it re-runs

The lodge parking census counts, for every active road lodge on a Welcome Sign, the car spaces within 600 m (`SkiParkingRules.RadiusM`).
A Burst job counts them over the lane search tree and the parking facilities.

- Counted: street parking lanes, lanes of parking lots, garages of parking halls.
- Not counted: boarding-only, bicycle, service or private spaces.

`SkiDaySystem` reads the census every pass (TryGetParking, hub row 21 "No parking near the lodge"). The stats DUMP prints it
(AppendDump). The parking ring reads the lodge positions (CopyLodgePositions). The rules are in `SkiParkingRules`. The lodge verdict
is `SkiDayRules.CheckLodge`. The census lives in memory only and resets on load.

## The radius (600 m since 2026-11-06; was 300 m)

Vanilla has no parking radius. A car trip is one pathfind: drive, park (parking cost 10 s of time, CarPathfind.cs), walk to the
door. The pathfinder takes the cheapest whole route under the cost cap of the trip.

- Tourist parties: TripNeededSystem.cs:1403 sets `m_MaxCost = GetMaxCost(priority)`, and :1441-1442 seek `SetupTargetType.Attraction`,
  whose targets get -100 x attractiveness x random (CitizenPathfindSetup.cs:369).
- GetMaxCost = 17,000 x priority / 128 (TripPriorityParametersData.cs:83-86). The tourist priority is 64
  (TripPriorityParametersPrefab.cs:12, :35). This gives 8,500.
- A car whose spot is taken re-paths with car + parking + walking at `kMaxPathfindCost` 17,000 (PersonalCarAISystem.cs:787-799). So once
  under way it parks at the cheapest free spot, which is the nearest one: walking dominates.
- The cost of a lane = (length / speed x time weight + its own costs . weights) x random 0.5-1 (PathUtils.cs:28-35). Walking adds
  0.01 comfort per m (PedestrianPathfind.cs:10, PathUtils.cs:1304) at 6 km/h (HumanPrefab.cs:14, :43).
- The weights are in CitizenUtils.cs:81-89 (time 5 x (4 - 3.75 x leisure counter / 255), comfort 1 + 2 x random). A mid tourist
  (leisure counter 128, comfort 2, random 0.75) pays 4.78 per m on foot, about 10 times a gravel road's (research
  far-resort-travel-2026-10-31.md section 1).

`SkiParkingRules.WalkCost` / `MidWalkCost` hold that model. 600 m straight x 1.3 detour (`SkiDayRules.DetourFactor`) = 780 m on
foot = 3,728, 44 % of 8,500. 700 m = 4,350, 51 %. So 600 m is the largest round 100 m whose walk costs a mid tourist at most half
the budget. That leaves the other half for the drive in from the border (TouristSpawnSystem.cs:105-111 starts the party at an
outside connection). Field anchor: residents with no closer parking walked about 790 m from the car to the near hill. The walk took 2.6 game
hours at 0.30 km per hour (research far-resort-travel-2026-10-31.md section 2). All are 1.6.0f1 lines, identical in 1.6.2f1.
Unverified: the shipped TripPriorityParameters and HumanPrefab values (we read the class defaults). Also unverified: how a tourist party
without a held car gets the car it parks (the effect is field-proven, DECISIONS 489).

What the row means: spaces inside the radius make arrivals possible, not certain (the drive and the competing attractions decide
the rest, "necessary, not sufficient"). Spaces just outside the radius still draw some parties, fewer the farther they are. The row and
tips state the radius (CatalogGuard T6c checks the text against `RadiusM`). The ring on the ground is the parking layer of
`SkiAreaRingOverlaySystem` (EngineNotes/SkiAreaRadius.md).

## Which lodges

Only lodges with a road edge whose row takes arrivals are counted (`LodgeSupplyRule.IsRoadLodge` per building, the ski-day
targets). These lodges never get a parking row: mountain lodges (hut, summit: roadless by design), patrol huts, and an off-road A-frame, Chalet or Grand.
So row 21 can never name them. The count is per lodge. The hub's Guests view shows the count of each road lodge (`lodgeParking`,
contract 29). The tier comes from `SkiResortAttractivenessSystem.CopyActiveLodgeInfo`. The system line of the DUMP counts the mountain
lodges that the last census skipped.

## Phase and cadence

GameSimulation, UpdateAt (Mod.cs, after SkiDaySystem's line). Not anchored: the census reads the lodge list of the attraction snapshot
and the lane search tree through the reader handle, so no frame order is needed. Pause stops it. The Editor never arms it.

Cadence: GetUpdateInterval 16 (one tick is one int compare unless a census is due). A census is due in three cases:

- `SkiResortAttractivenessSystem.LodgeSetRevision` moved.
- The parking signature moved (parking-lane entity count x 31 + parking-facility count, read every SignatureTicks = 16 ticks = 256
  frames with CalculateEntityCount, which never syncs). A road built anywhere also moves it, which is fine.
- Every BackstopTicks = 256 ticks (4,096 frames).

A swap that keeps the parking-lane count (a Gravel Road upgraded to a Small Road) does not move the signature and waits for the
backstop. A component order version is no better a trigger (every car parking on a street lane moves it). The system completes and
applies the job scheduled on one tick on the next tick, so the main thread never waits.

## The census

`ParkingCensusJob` (one Burst IJob), per (lodge, sign) pair from CopySigns / CopyActiveLodgeInfo:

1. State = `SkiDayRules.CheckLodge(IsLodgePrefab, OnLiveSign, Active = Building && !Inactive (BuildingUtils.cs:351), Destroyed =
   Destroyed || Deleted || no Building, LeisureTagged = Game.Buildings.LeisureProvider (Tourism Overhaul removes / restores it),
   HasRoad = Building.m_RoadEdge, NearestRunOrStationM = 0`: slope-side is SkiDaySystem's to judge). No position gives Done = false
   (DUMP "no census (no position)").
2. Lane pass:
   - The input is the lane search tree of the game (SearchSystem.GetLaneSearchTree(readOnly), SearchSystem.cs:839-843). It holds every
     lane with LaneGeometry or ParkingLane (:733-741).
   - The job searches the box lodge xz +- RadiusM (`LaneBoxIterator`: the house iterator shape, `SkiGladeToolSystem.TreeBoundsIterator`).
   - For each hit with a Game.Net.ParkingLane: d = MathUtils.Distance(curve xz, lodge xz). restricted =
     `SkiParkingRules.IsRestricted(m_AccessRestriction set and not our sign or lodge, the lane's AllowEnter | AllowExit)`.
   - ParkingLaneDataSystem sets the restriction to the owning building for every building lane (:383-398). It sets the two allow flags
     from its Restricted* building flags (:225-279). The pathfinder honors this (PathUtils.cs:1195, :1220-1225).
   - The verdict is `SkiParkingRules.ClassifyLane`.
   - Count = NetUtils.GetParkingSlotCount (NetUtils.cs:954-957, the roads info view's own count, RoadsInfoviewUISystem.cs:134-173) and
     the LaneObjects that are ParkedCars.
   - The owner walk (<= MaxOwnerDepth) splits street from lots. Street means no building on the chain. Lots means any building: a
     parking lot or hall, or a park's or shop's lot the game opens to outsiders. This is for the DUMP only.
3. Garage pass:
   - The job takes every CarParkingFacility building whose center is within RadiusM + `FacilityReachM` 250 m (`FacilityNear`). It skips
     an upgrade, because the garage lanes of an upgrade sit on its root (m_FacilityWalkQuery).
   - It reaches the GarageLane connection lanes through SubLane, SubNet -> SubLane, SubObject -> SubLane and
     SubObject -> SubObject -> SubLane (the walk of the info view, depth fixed at 2).
   - It measures each garage lane on its own, to its Curve (`GarageDistance`, the building center when it has none). So a big hall
     counts by its entrance, not its middle. Before 2026-11-06 the code used the center, which missed a hall whose middle lay past the
     radius.
   - The term "in reach" (DUMP, [cost]) = a garage lane within RadiusM.
   - The verdict is `SkiParkingRules.ClassifyGarage`, with the own restriction of the connection lane and AllowEnter | AllowExit.
   - Count = m_VehicleCapacity spaces, m_VehicleCount parked.
   - Garage lanes are connection lanes (RoadConnectionSystem.cs:1374-1399, :1445-1448), not in the lane tree, and lot ParkingLanes
     come only from pass 2, so nothing is counted twice.

Reader handshake (SearchSystem.cs:839-843, :855-858, and vanilla FixParkingLocationSystem.cs:1847, :1856): the write dependency of the
tree joins the schedule. AddLaneSearchTreeReader(handle) follows it. The tree field is [ReadOnly] (FixParkingLocationSystem.cs:928). The
read-only Transform, Curve and LaneObject lookups of the job make the vanilla writers of those types wait for the job in the next
frame. They wait as for a reader (a job edge, not a main-thread stall).

## Flags

The system asserts the constants of SkiParkingRules against Game.Net.ParkingLaneFlags / ConnectionLaneFlags / RoadTypes at arm time
(s_FlagsMatch). The compiler folds the enum values in, so only a rebuild against a Game.dll whose values moved can trip the assert.
A mismatch gives one WARN, and the census is off for the load.

## Cost

These are estimates. The [cost] line measures.

- Idle tick = one int compare (two CalculateEntityCount every 16th tick).
- Census tick = <= 10 signs x <= 10 lodges copied, one async chunk list, one schedule.
- Worker = every lane of a 1,200 x 1,200 m box per lodge (4x the box of the 300 m radius, thousands in town, unverified) + every
  parking facility.

The line measures the CostCensuses-th census with lodges after each load. That census completes synchronously, in this order:

1. Its dependencies first (the vanilla writers of the types of the lookups, the one main-thread sync of a measurement).
2. Schedule + complete (the own time of the job, "job". A Stopwatch cannot run inside a Burst job).
3. Applied at once.

The measurement re-arms once per game day at the first tick at or after 12:00 (SkiDayRules.DayKey, only once the last line was
written). A census on the arming tick is not measured. The line prints the vanilla wait (tRun - tDeps, "not ours") on its own and ends
"; measurement n this load, YYYY-MM at HH:MM" (`SkiGuestCounterSystem.CostSuffix`).

## Logs

- created, ARMED.
- `Parking [census]` per lodge whose Done, State or car spaces changed (or new), through one LogWindow(10 s) for all lodges
  (trailing flush each tick, preload, destroy).
- `Parking [idle]` on each transition to no lodge.
- `Parking [cost]` after the load and daily at midday.
- The flag WARN.
- The one-shot `!!!` (disabled until the next load).
- DUMP: the system line, one line per lodge (managed copies only, made in ApplyCensus after Complete).

## Contract (house pattern)

- A "created" line.
- Per-load reset and re-arm in OnGameLoadingComplete (Game mode only).
- OnGamePreload completes and discards the census in flight and flushes the fold (try/caught: GameSystemBase.cs:85-96 disables a system
  whose OnGamePreload throws).
- OnDestroy completes, flushes and disposes.
