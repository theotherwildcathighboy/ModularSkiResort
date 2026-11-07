# SkiLodgeParkingSystem: how the lodge parking census counts car spaces and when it re-runs

The lodge parking census: for every active road lodge on a Welcome Sign, the car spaces within 600 m (`SkiParkingRules.RadiusM`;
street parking lanes, lanes of parking lots, garages of parking halls; not boarding-only, bicycle, service or private spaces),
counted by a Burst job over the lane search tree and the parking facilities. `SkiDaySystem` reads it every pass (TryGetParking, hub
row 21 "No parking near the lodge"); the stats DUMP prints it (AppendDump); the parking ring reads the lodge positions
(CopyLodgePositions). The rules are `SkiParkingRules`'; the lodge verdict is `SkiDayRules.CheckLodge`. In memory only, reset on load.

## The radius (600 m since 2026-11-06; was 300 m)

Vanilla has no parking radius. A car trip is one pathfind: drive, park (parking cost 10 s of time, CarPathfind.cs), walk to the
door; the pathfinder takes the cheapest whole route under the trip's cost cap. Tourist parties: TripNeededSystem.cs:1403 sets
`m_MaxCost = GetMaxCost(priority)` and :1441-1442 seek `SetupTargetType.Attraction`, whose targets get -100 x attractiveness x
random (CitizenPathfindSetup.cs:369); GetMaxCost = 17,000 x priority / 128 (TripPriorityParametersData.cs:83-86), tourist priority
64 (TripPriorityParametersPrefab.cs:12, :35): 8,500. A car whose spot is taken re-paths with car + parking + walking at
`kMaxPathfindCost` 17,000 (PersonalCarAISystem.cs:787-799), so once under way it parks at the cheapest free spot, which is the
nearest one: walking dominates. A lane's cost = (length / speed x time weight + its own costs . weights) x random 0.5-1
(PathUtils.cs:28-35); walking adds 0.01 comfort per m (PedestrianPathfind.cs:10, PathUtils.cs:1304) at 6 km/h (HumanPrefab.cs:14,
:43); weights CitizenUtils.cs:81-89 (time 5 x (4 - 3.75 x leisure counter / 255), comfort 1 + 2 x random). A mid tourist (leisure
counter 128, comfort 2, random 0.75) pays 4.78 per m on foot, about 10 times a gravel road's (research
far-resort-travel-2026-10-31.md section 1).

`SkiParkingRules.WalkCost` / `MidWalkCost` hold that model. 600 m straight x 1.3 detour (`SkiDayRules.DetourFactor`) = 780 m on
foot = 3,728, 44 % of 8,500; 700 m = 4,350, 51 %. So 600 m is the largest round 100 m whose walk costs a mid tourist at most half
the budget, which leaves the other half for the drive in from the border (TouristSpawnSystem.cs:105-111 starts the party at an
outside connection). Field anchor: residents with no closer parking walked about 790 m from the car to the near hill (2.6 game
hours at 0.30 km per hour, research far-resort-travel-2026-10-31.md section 2). All 1.6.0f1 lines, identical in 1.6.2f1.
Unverified: the shipped TripPriorityParameters and HumanPrefab values (the class defaults were read), and how a tourist party
without a held car gets the car it parks (the effect is field-proven, DECISIONS 489).

What the row means: spaces inside the radius make arrivals possible, not certain (the drive and the competing attractions decide
the rest, "necessary, not sufficient"), and spaces just outside it still draw some parties, fewer the farther they are. The row and
tips state the radius (CatalogGuard T6c checks the text against `RadiusM`). The ring on the ground is `SkiAreaRingOverlaySystem`'s
parking layer (EngineNotes/SkiAreaRadius.md).

## Which lodges

Only lodges with a road edge whose row takes arrivals are counted (`LodgeSupplyRule.IsRoadLodge` per building, the ski-day
targets): mountain lodges (hut, summit: roadless by design), patrol huts and an off-road A-frame, Chalet or Grand never get a parking row,
so row 21 can never name them. The count is per lodge; the hub's Guests view shows each road lodge's (`lodgeParking`, contract 29). The tier comes from
`SkiResortAttractivenessSystem.CopyActiveLodgeInfo`; mountain lodges skipped by the last census are counted in the DUMP's system
line.

## Phase and cadence

GameSimulation, UpdateAt (Mod.cs, after SkiDaySystem's line). Not anchored: the census reads the attraction snapshot's lodge list
and the lane search tree through the reader handle, so no frame order is needed. Pause stops it; the Editor never arms it.

Cadence: GetUpdateInterval 16 (one tick is one int compare unless a census is due). A census is due when
`SkiResortAttractivenessSystem.LodgeSetRevision` moved, when the parking signature moved (parking-lane entity count x 31 +
parking-facility count, read every SignatureTicks = 16 ticks = 256 frames with CalculateEntityCount, which never syncs; a road
built anywhere also moves it, which is fine), and every BackstopTicks = 256 ticks (4,096 frames). A swap that keeps the
parking-lane count (a Gravel Road upgraded to a Small Road) does not move the signature and waits for the backstop; a component
order version is no better a trigger (every car parking on a street lane moves it). The job scheduled on one tick is completed
and applied on the next, so the main thread never waits.

## The census

`ParkingCensusJob` (one Burst IJob), per (lodge, sign) pair from CopySigns / CopyActiveLodgeInfo:

1. State = `SkiDayRules.CheckLodge(IsLodgePrefab, OnLiveSign, Active = Building && !Inactive (BuildingUtils.cs:351), Destroyed =
   Destroyed || Deleted || no Building, LeisureTagged = Game.Buildings.LeisureProvider (Tourism Overhaul removes / restores it),
   HasRoad = Building.m_RoadEdge, NearestRunOrStationM = 0`: slope-side is SkiDaySystem's to judge). No position gives Done = false
   (DUMP "no census (no position)").
2. Lane pass: the game's lane search tree (SearchSystem.GetLaneSearchTree(readOnly), SearchSystem.cs:839-843; it holds every lane
   with LaneGeometry or ParkingLane, :733-741) over the lodge xz +- RadiusM (box, `LaneBoxIterator`: the house iterator shape,
   `SkiGladeToolSystem.TreeBoundsIterator`); each hit with a Game.Net.ParkingLane: d = MathUtils.Distance(curve xz, lodge xz);
   restricted = `SkiParkingRules.IsRestricted(m_AccessRestriction set and not our sign or lodge, the lane's AllowEnter |
   AllowExit)`. ParkingLaneDataSystem sets the restriction to the owning building for every building lane (:383-398) and the two
   allow flags from its Restricted* building flags (:225-279), which is what the pathfinder honors (PathUtils.cs:1195,
   :1220-1225). Verdict `SkiParkingRules.ClassifyLane`. Count = NetUtils.GetParkingSlotCount (NetUtils.cs:954-957, the roads info
   view's own count, RoadsInfoviewUISystem.cs:134-173) and the LaneObjects that are ParkedCars; the owner walk (<= MaxOwnerDepth)
   splits street (no building on the chain) from lots (any building: a parking lot or hall, or a park's or shop's lot the game
   opens to outsiders) for the DUMP only.
3. Garage pass: every CarParkingFacility building whose center is within RadiusM + `FacilityReachM` 250 m (`FacilityNear`; not an
   upgrade, whose garage lanes sit on its root, m_FacilityWalkQuery); its GarageLane connection lanes reached through SubLane,
   SubNet -> SubLane, SubObject -> SubLane and SubObject -> SubObject -> SubLane (the info view's walk, depth fixed at 2); each
   garage lane measured on its own, to its Curve (`GarageDistance`; the building center when it has none), so a big hall counts by
   its entrance, not its middle (before 2026-11-06 the center, which missed a hall whose middle lay past the radius); "in reach"
   (DUMP, [cost]) = a garage lane within RadiusM; verdict `SkiParkingRules.ClassifyGarage` with the connection lane's own
   restriction and AllowEnter | AllowExit; Count = m_VehicleCapacity spaces, m_VehicleCount parked. Garage
   lanes are connection lanes (RoadConnectionSystem.cs:1374-1399, :1445-1448), not in the lane tree, and lot ParkingLanes come
   only from pass 2, so nothing is counted twice.

Reader handshake (SearchSystem.cs:839-843, :855-858; vanilla FixParkingLocationSystem.cs:1847, :1856): the tree's write dependency
joins the schedule, AddLaneSearchTreeReader(handle) after it; the tree field is [ReadOnly] (FixParkingLocationSystem.cs:928). Its
read-only Transform, Curve and LaneObject lookups make the next frame's vanilla writers of those types wait for it as a reader (a
job edge, not a main-thread stall).

## Flags

SkiParkingRules' constants are asserted against Game.Net.ParkingLaneFlags / ConnectionLaneFlags / RoadTypes at arm time
(s_FlagsMatch; the enum values are folded in at compile time, so only a rebuild against a Game.dll whose values moved can trip
it): a mismatch is one WARN and the census is off for the load.

## Cost

Estimates; the [cost] line measures. Idle tick = one int compare (two CalculateEntityCount every 16th tick); census tick = <= 10
signs x <= 10 lodges copied, one async chunk list, one schedule; worker = every lane of a 1,200 x 1,200 m box per lodge (4x the
300 m radius's box; thousands in town, unverified) + every parking facility. Measured at the CostCensuses-th census with lodges after each load, which is
completed synchronously: its dependencies first (the vanilla writers of the lookups' types, the one main-thread sync of a
measurement), then schedule + complete (the job's own time, "job"; a Stopwatch cannot run inside a Burst job), then applied at
once. Re-armed once per game day at the first tick at or after 12:00 (SkiDayRules.DayKey; only once the last line was written); a
census on the arming tick is not measured. The line prints the vanilla wait (tRun - tDeps, "not ours") on its own and ends
"; measurement n this load, YYYY-MM at HH:MM" (`SkiGuestCounterSystem.CostSuffix`).

## Logs

- created; ARMED.
- `Parking [census]` per lodge whose Done, State or car spaces changed (or new), through one LogWindow(10 s) for all lodges
  (trailing flush each tick, preload, destroy).
- `Parking [idle]` on each transition to no lodge; `Parking [cost]` after the load and daily at midday; the flag WARN; the
  one-shot `!!!` (disabled until the next load).
- DUMP: the system line, one line per lodge (managed copies only, made in ApplyCensus after Complete).

## Contract (house pattern)

"created" line; per-load reset and re-arm in OnGameLoadingComplete (Game mode only); OnGamePreload completes and discards the
census in flight and flushes the fold (try/caught: GameSystemBase.cs:85-96 disables a system whose OnGamePreload throws);
OnDestroy completes, flushes and disposes.
