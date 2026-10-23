# SkiGuestCounterSystem: how ski-area guests are detected, counted once and booked into the ledger

`SkiGuestCounterSystem` counts the guests who arrive at a ski area (tourist parties through meetings, residents and tourists
through leisure at a lodge), writes them into the monthly `SkiResortLedger` and hands them to `SkiLiftTicketSystem` as
`GuestArrival` records (DrainArrivals).

## What is a guest

- Tourist party: a CoordinatedMeeting whose m_Target is one of our targets and whose first attendee's household has
  TouristHousehold; counted at the first scan where the meeting is Attending and was not Attending in the previous scan
  (MeetingSystem.cs:126-147: Attending = every attendee's CurrentBuilding is the target; :149-178 a one-phase attraction outing
  with delay (0, 0) is Attending for exactly one MeetingSystem update). People = the attendee buffer length (all members of one
  household, AddMeetingSystem.cs:70-84). The prev-set rule makes a longer Attending spell count once; a new spell the same game
  day counts only members not yet counted there that day (see "Tourist on leisure").
- Resident: a citizen whose Leisure.m_TargetAgent is one of our lodges and whose CurrentBuilding.m_CurrentBuilding is that lodge,
  household without TouristHousehold; counted at the first leisure scan where it is on site and was not on site the scan before
  (set difference).
- Tourist on leisure: the same test at a lodge (never the sign), household with TouristHousehold; counted at most once per game
  day per ski area (SkiDayRules.DayKey), and not when its household came there that day by outing. An outing counts only the
  attendees not yet counted there that day (a multi-phase outing returning the same day is not counted again). In-memory day sets,
  cleared at the day roll and at load, so after a load a tourist counted before the save can count, and pay, once more that day.
  An outing member already on site with a Leisure aimed at the lodge (LeisureSystem.cs 1.6.2 :375-403) is counted on leisure
  first; its outing then counts the rest (totals right, the ledger's TouristOnLeisure slightly high).
- The first scan of each kind after a load is a baseline: it records, counts nothing (a party or a resident on site at load time
  was counted, and maybe charged, before the save). A target that joins the map later (a lodge switched back on with residents
  still inside; SpendLeisure ends a visit at an inactive provider only on its own 1,024-frame pass, LeisureSystem.cs 1.6.2
  :283-312) gets the same baseline at the next leisure scan, so nobody is counted or charged twice.

## Targets

Every live Welcome Sign (`SkiRunOwnershipSystem.IsGatePrefab`) and every active lodge (the catalog's LodgePrefabSet, fetched once
per rebuild, every lodge tier; the school and the patrol huts are not targets). A lodge (top-level, no Owner) is a target of its
published link (`SkiResortAttractivenessSystem.CopyTopLevelLinks`), active by its own Building.m_OptionMask (BuildingUtils.cs:351).
Free-standing (not linked) and out-of-service lodges are not targets (counted for the targets line only).

The map building -> sign is rebuilt on the main thread between jobs when the Park query's entity count moves, when the attraction
model's Generation moves (a lodge switched on or off changes no count), every Tuning.MapBackstopTicks ticks, and on the first
tick after a load. Query {Building, Game.Buildings.Park, PrefabRef} None {Temp, Deleted}: sign and lodge both carry
Game.Buildings.Park (Game.Prefabs/Park.cs:29). The rebuild syncs only PrefabRef's writers (CompleteDependencyBeforeRO,
ParkedVehiclesSystem.cs:1732), never the Transform writers of the move systems (Transform in the query would make every rebuild
wait on CarMoveSystem / HumanMoveSystem jobs; the map rebuilds on every attraction Generation move, up to every 256 frames).

## Jobs

MeetingScanJob (every tick) and LeisureScanJob (every Tuning.LeisureEveryNthTick-th tick, scheduled after the meeting job; they
share Stats) are single-threaded IJobs over ToArchetypeChunkListAsync chunk lists (the BrandPopularitySystem.cs:168-177 pattern),
[BurstCompile] best-effort (unburst they run as managed code on a worker). Their results are read on the next tick after
Complete(), which by then costs nothing: no main-thread wait on MeetingSystem's or TripNeededSystem's jobs. EarlyJobInit for both
in OnCreate. The dev counters (parties picked, locals heading, scan stats) are copied into managed fields after Complete, so DUMP
/ hub reads never touch a container a job may be writing.

## Step order per tick

1. Complete the scans.
2. Month (`SkiMoneyRules.MonthKey` of TimeSystem year / normalizedDate, TimeSystem.cs:38-40; Ledger.Advance; month lines and Prune
   on a roll or jump). 2b: day (`SkiDayRules.DayKey`; a new key clears the two day sets: the jobs are complete, so the main thread
   clears them safely).
3. Apply last tick's arrivals (ledger, pending list for the ticket system, first-arrival lines), swap the Attending / on-site
   sets, clear the baselines that ran, copy the day sets' counts (managed, for the DUMP).
4. Rebuild the map when asked.
5. Schedule.

Arrivals detected in the last 16 frames of a month are booked into the new month (accepted).

Day sets: m_CitizensToday {(citizen, sign)} and m_HouseholdsToday {(household, sign) -> SkiMoneyRules.GuestDay* flags}, int4 keys
of (Index, Version) pairs, Persistent, written by both scan jobs (the leisure job runs on the meeting job's handle) and never read
on the main thread while a job runs.

## Ledger

This system owns the one SkiResortLedger (reset in OnGameLoadingComplete; only this system calls Advance). SkiLiftTicketSystem
(AddTickets, AddLocalsPaid) and SkiLiftChargeSystem (AddLiftCharge) write into it through Ledger. AddLocalsPaid keeps the
per-ski-area count of residents who paid this month for the lodge note line, which the ledger's tourist / local split of guests
alone cannot give.

## Phase, cadence and cost

GameSimulation, `UpdateAfter<SkiGuestCounterSystem, Game.Simulation.MeetingSystem>` (Mod.cs; load-bearing for freshness, not for
exactly-once; MeetingSystem is UpdateAt GameSimulation, SystemOrder.cs:404). Pause stops it; the Editor never arms it. Cadence:
GetUpdateInterval 16 = MeetingSystem's interval (MeetingSystem.cs:277-280), so the anchor's offset is inherited
(UpdateSystem.cs:400-431).

Cost: main thread per tick = Complete of jobs already done, apply (few arrivals), one CalculateEntityCount and two schedules (tens
of microseconds); `Guests [cost]` measures it after each load and daily at midday (CostSuffix is shared with the parking census
and the tickets).

SkiResortHistorySystem (UpdateAfter this system, same 16-frame interval) reads LastMonthStep, MonthFramesSimulated, MonthByJump
and LiveSigns; SkiDaySystem reads the leisure scan's phase (LastLeisureScanFrame) and the resident maps (TryResidentVisits).

## Load handling

Per-load reset and re-arm in OnGameLoadingComplete (Game mode only). OnGamePreload completes jobs, books the finished scan and
the ticket system's batch in flight, and writes the month in progress (try/caught: GameSystemBase.cs:85-96 disables a system whose
OnGamePreload throws). A one-shot "!!!" disables the system for the load.
