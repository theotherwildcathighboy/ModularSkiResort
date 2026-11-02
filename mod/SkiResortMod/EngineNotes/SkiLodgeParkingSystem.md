# SkiLodgeParkingSystem: how the lodge parking census counts car spaces and when it re-runs

The lodge parking census: for every active Base lodge on a Welcome Sign, the car spaces within 300 m (street parking lanes, lanes
and garages of parking lots and halls; not boarding-only, bicycle, service or private spaces), counted by a Burst job over the
lane search tree and the parking facilities. `SkiDaySystem` reads it every pass (TryGetParking, hub row 21 "No parking near the
lodge"); the stats DUMP prints it (AppendDump). The rules are `SkiParkingRules`'; the lodge verdict is `SkiDayRules.CheckLodge`.
In memory only, reset on load.

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
   with LaneGeometry or ParkingLane, :733-741) over the lodge xz +- 300 m (box, `LaneBoxIterator`: the house iterator shape,
   `SkiGladeToolSystem.TreeBoundsIterator`); each hit with a Game.Net.ParkingLane: d = MathUtils.Distance(curve xz, lodge xz);
   restricted = `SkiParkingRules.IsRestricted(m_AccessRestriction set and not our sign or lodge, the lane's AllowEnter |
   AllowExit)`. ParkingLaneDataSystem sets the restriction to the owning building for every building lane (:383-398) and the two
   allow flags from its Restricted* building flags (:225-279), which is what the pathfinder honors (PathUtils.cs:1195,
   :1220-1225). Verdict `SkiParkingRules.ClassifyLane`. Count = NetUtils.GetParkingSlotCount (NetUtils.cs:954-957, the roads info
   view's own count, RoadsInfoviewUISystem.cs:134-173) and the LaneObjects that are ParkedCars; the owner walk (<= MaxOwnerDepth)
   splits street (no building on the chain) from lots (any building: a parking lot or hall, or a park's or shop's lot the game
   opens to outsiders) for the DUMP only.
3. Garage pass: every CarParkingFacility building within 300 m (its Transform; not an upgrade, whose garage lanes sit on its root,
   m_FacilityWalkQuery); its GarageLane connection lanes reached through SubLane, SubNet -> SubLane, SubObject -> SubLane and
   SubObject -> SubObject -> SubLane (the info view's walk, depth fixed at 2); verdict `SkiParkingRules.ClassifyGarage` with the
   connection lane's own restriction and AllowEnter | AllowExit; Count = m_VehicleCapacity spaces, m_VehicleCount parked. Garage
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
signs x <= 10 lodges copied, one async chunk list, one schedule; worker = every lane of a 600 x 600 m box per lodge (thousands in
town, unverified) + every parking facility. Measured at the CostCensuses-th census with lodges after each load, which is
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
