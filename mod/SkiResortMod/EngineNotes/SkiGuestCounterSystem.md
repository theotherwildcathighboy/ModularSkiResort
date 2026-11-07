# SkiGuestCounterSystem: how ski-area guests are detected, counted once and booked into the ledger

`SkiGuestCounterSystem` counts the guests who arrive at a ski area (tourist parties through meetings, residents and tourists
through leisure at a lodge). It writes them into the monthly `SkiResortLedger`. It hands them to `SkiLiftTicketSystem` as
`GuestArrival` records (DrainArrivals).

## What is a guest

- Tourist party: a CoordinatedMeeting whose m_Target is one of our targets and whose first attendee's household has
  TouristHousehold. The system counts it at the first scan where the meeting is Attending and was not Attending in the previous scan.
  - Attending = every attendee's CurrentBuilding is the target (MeetingSystem.cs:126-147).
  - A one-phase attraction outing with delay (0, 0) is Attending for exactly one MeetingSystem update (:149-178).
  - People = the attendee buffer length (all members of one household, AddMeetingSystem.cs:70-84).
  - The prev-set rule makes a longer Attending spell count once. A new spell the same game day counts only members not yet counted there
    that day (see "Tourist on leisure").
- Resident: a citizen whose Leisure.m_TargetAgent is one of our lodges and whose CurrentBuilding.m_CurrentBuilding is that lodge,
  household without TouristHousehold. The system counts it at the first leisure scan where it is on site and was not on site the scan
  before (set difference).
- Tourist on leisure: the same test at a lodge (never the sign), household with TouristHousehold.
  - The system counts it at most once per game day per ski area (SkiDayRules.DayKey), and not when its household came there that day by
    outing.
  - An outing counts only the attendees not yet counted there that day (a multi-phase outing returning the same day is not counted
    again).
  - The day sets are in memory. They are cleared at the day roll and at load. So after a load, a tourist counted before the save can
    count, and pay, once more that day.
  - The system counts an outing member that is already on site with a Leisure aimed at the lodge (LeisureSystem.cs 1.6.2 :375-403) on
    leisure first. Its outing then counts the rest (totals right, the ledger's TouristOnLeisure slightly high).
- The first scan of each kind after a load is a baseline. It records but counts nothing (a party or a resident on site at load time
  was counted, and maybe charged, before the save). A target that joins the map later gets the same baseline at the next leisure scan,
  so nobody is counted or charged twice. Example: a lodge switched back on with residents still inside. SpendLeisure ends a visit at an
  inactive provider only on its own 1,024-frame pass (LeisureSystem.cs 1.6.2 :283-312).

## Targets

A target is every live Welcome Sign (`SkiRunOwnershipSystem.IsGatePrefab`) and every active lodge. The active lodges come from the
catalog's LodgePrefabSet, fetched once per rebuild, every lodge tier. The school and the patrol huts are not targets. A lodge (top-level,
no Owner) is a target of its published link (`SkiResortAttractivenessSystem.CopyTopLevelLinks`). It is active by its own
Building.m_OptionMask (BuildingUtils.cs:351). Free-standing (not linked) and out-of-service lodges are not targets (counted for the
targets line only).

The system rebuilds the map building -> sign on the main thread between jobs in these cases:
- The Park query's entity count moves.
- The attraction model's Generation moves (a lodge switched on or off changes no count).
- Every Tuning.MapBackstopTicks ticks.
- On the first tick after a load.

Query {Building, Game.Buildings.Park, PrefabRef} None {Temp, Deleted}: sign and lodge both carry Game.Buildings.Park
(Game.Prefabs/Park.cs:29). The rebuild syncs only PrefabRef's writers (CompleteDependencyBeforeRO, ParkedVehiclesSystem.cs:1732). It never
syncs the Transform writers of the move systems. Transform in the query would make every rebuild wait on CarMoveSystem / HumanMoveSystem
jobs. The map rebuilds on every attraction Generation move, up to every 256 frames.

## Jobs

- MeetingScanJob runs every tick.
- LeisureScanJob runs every Tuning.LeisureEveryNthTick-th tick. It is scheduled after the meeting job. The two jobs share Stats.

Both are single-threaded IJobs over ToArchetypeChunkListAsync chunk lists (the BrandPopularitySystem.cs:168-177 pattern). They are
[BurstCompile] best-effort (unburst they run as managed code on a worker). The system reads their results on the next tick after
Complete(), which by then costs nothing: no main-thread wait on MeetingSystem's or TripNeededSystem's jobs. Both jobs get EarlyJobInit in
OnCreate. The system copies the dev counters (parties picked, locals heading, scan stats) into managed fields after Complete. So DUMP
/ hub reads never touch a container a job may be writing.

## Step order per tick

1. Complete the scans.
2. Month (`SkiMoneyRules.MonthKey` of TimeSystem year / normalizedDate, TimeSystem.cs:38-40). The step calls Ledger.Advance, and writes
   month lines and Prune on a roll or jump. 2b: day (`SkiDayRules.DayKey`). A new key clears the two day sets. The jobs are complete, so
   the main thread clears them safely.
3. Apply last tick's arrivals (ledger, pending list for the ticket system, first-arrival lines). Swap the Attending / on-site sets.
   Clear the baselines that ran. Copy the day sets' counts (managed, for the DUMP).
4. Rebuild the map when asked.
5. Schedule.

Arrivals detected in the last 16 frames of a month are booked into the new month (accepted).

Day sets: m_CitizensToday {(citizen, sign)} and m_HouseholdsToday {(household, sign) -> SkiMoneyRules.GuestDay* flags} use int4 keys
of (Index, Version) pairs. They are Persistent. Both scan jobs write them (the leisure job runs on the meeting job's handle). The main
thread never reads them while a job runs.

## Ledger

This system owns the one SkiResortLedger (reset in OnGameLoadingComplete). Only this system calls Advance. SkiLiftTicketSystem
(AddTickets, AddLocalsPaid) and SkiLiftChargeSystem (AddLiftCharge) write into it through Ledger. AddLocalsPaid keeps the
per-ski-area count of residents who paid this month for the lodge note line. The ledger's tourist / local split of guests
alone cannot give this count.

## Phase, cadence and cost

GameSimulation, `UpdateAfter<SkiGuestCounterSystem, Game.Simulation.MeetingSystem>` (Mod.cs). The anchor is load-bearing for freshness,
not for exactly-once. MeetingSystem is UpdateAt GameSimulation (SystemOrder.cs:404). Pause stops the system. The Editor never arms it.
Cadence: GetUpdateInterval 16 = MeetingSystem's interval (MeetingSystem.cs:277-280), so the anchor's offset is inherited
(UpdateSystem.cs:400-431).

Cost: main thread per tick = Complete of jobs already done, apply (few arrivals), one CalculateEntityCount and two schedules (tens
of microseconds). `Guests [cost]` measures it after each load and daily at midday (CostSuffix is shared with the parking census
and the tickets).

SkiResortHistorySystem (UpdateAfter this system, same 16-frame interval) reads LastMonthStep, MonthFramesSimulated, MonthByJump
and LiveSigns. SkiDaySystem reads the leisure scan's phase (LastLeisureScanFrame) and the resident maps (TryResidentVisits).

## Load handling

The system resets and re-arms per load in OnGameLoadingComplete (Game mode only). OnGamePreload completes jobs, books the finished scan and
the ticket system's batch in flight, and writes the month in progress. It is try/caught, because GameSystemBase.cs:85-96 disables a
system whose OnGamePreload throws. A one-shot "!!!" disables the system for the load.
