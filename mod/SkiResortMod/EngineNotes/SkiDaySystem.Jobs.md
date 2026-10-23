# SkiDaySystem.Jobs: what each Burst job reads and writes, the measure-only parts, and the deadline pull

Decompile baseline `Game.dll` 1.6.0f1 (1.6.2f1 line numbers are given where they moved). The claim struct is 128 bytes (4.0 MiB at
the cap); the ring record is 108.

## The jobs

- `SiteJob` (at a snapshot rebuild and on the 4,096-frame backstop): per lodge, its xz and the distance to the nearest run outline
  (`Game.Areas.Node` polygon, the `Geometry` box as an exact skip) or lift station of its ski area. Transform / Geometry / Node are
  read through read-only lookups: a job edge after the move systems, never a main-thread read.
- `ResolveJob` (every pass): per tracked claim, `SkiDayTiming.Resolve` keeps or drops it; arrivals, failures, learning
  (`SkiDayRules.RecordOutcome` / `LearnSpeed`); the claims on the way + on site per area. Its one write to vanilla state: lowering a
  claim's own `Leisure.m_LastPossibleFrame` when the hill closes early (the deadline pull, below).
- `ClaimJob` (every pass while a ski area exists): step A, lodge verdicts (`SkiDayRules.CheckLodge`), capacity, area state; step B (an
  area Open, a knob on, under the cap), the candidate walk and the claim: `Leisure.m_TargetAgent` + `m_LastPossibleFrame`, one
  `TripNeeded`, and for a Car-class claim without a parked car the `CitizenBehaviorSystem` car-reserve queue. Nothing else is
  written; no structural change; no command buffer.
- `CensusJob` (first pass after a load, then each game hour): tourist households (DUMP), lodge occupants by kind and attraction
  outings still Traveling to a sign or lodge. Measure only.
- `NearJob` (every `Tuning.NearScanPasses` passes with an area, on the hour and on the timed `[cost]` pass; scheduled by
  `SkiDaySystem` after `ClaimJob`): per lodge, the walkers within `SkiDayTiming.NearLodgeM` (waiting for transport, failed path,
  hanging around, walking; not drawn; ski-day guests among them; the game timer's wait so far) and the game's own waiting count at
  the line stop points within that radius. Read-only type handles; writes only its own `NearOut` / `Totals`.

## Burst rules

No managed type, string or exception. Every component read is a `TryGetComponent` / `HasComponent` / `TryGetBuffer` (a destroyed
lodge or a citizen gone since the claim is a normal input: Release builds run without collection checks, so an unguarded lookup reads
a dead chunk slot). Every index into a snapshot array is range-checked; area -1 (an orphan claim: its sign left the snapshot) writes
only the orphan row; tracks are copy-modify-write (no ref into a `NativeArray` / `NativeList` element).

One access mode per type per job: `ClaimJob` holds `Leisure` / `TripNeeded` as RW type handles and no lookup of them; `ResolveJob`
holds `Leisure` as a RW lookup (the deadline pull) and `CensusJob` as a RO lookup; the jobs run on one chain (resolve, claim, census),
so no two of them touch `Leisure` at once. `ResolveJob`'s RO `TripNeeded` lookup follows the same rule: `ClaimJob`, which writes it,
is scheduled after `ResolveJob`.

## Measure-only parts

`SkiDayClaim` carries departure, ride, end and closest-approach frames, markers and the mode seen, and has states 2 (visit over,
still inside), 3 (expired, watched for a late arrival) and 4 (followed after leaving), none ever counted as on the way or on site.
`ResolveJob` samples each claim (`Sample`), books failures by place, cause and marker, trip phases, visit end and exit, modes,
distance bands and the car reserve into a per-area measure row (RM / RH, written only through `MeasureWrite`'s range checks), keeps a
40-slot ring of ended ski days (`ClaimRecord`) and notes resident arrivals for the ledger check (`ResidentNote`). `ClaimJob` counts
decisions refused as too late to fit, the pool's chances and the sent distance bands (CM). No guest rule, learning input, hub view
or vanilla write changes except the deadline pull; every other lookup is `[ReadOnly]`.

- A Gone claim whose citizen stands in the lodge is booked as arrived (learning included, `BookArrival`) with the visit end
  "removed (on arrival)", then the normal exit watch.
- Door wait (`MoveFrame`: the first pass the walker no longer carries `Divert{PathFailed}`; `DoorN`, `DoorUnseen`,
  `NeverMovedFailed`, `RH.Door`; `ResolveOut.AtDoor`). A claim that changes state in a pass is counted in its new bucket (Leaving,
  Watching, Following). Late arrivals by clock bin (`RM.LateClock`).
- At a visit end before the deadline (need met, removed, removed on arrival), why the Leisure went (`SkiDayTiming.RemoveWhyOf` over the
  game's traces and a 2-bit household-need register sampled on the way and on site) and, at every visit end, the game's own work-time
  / bedtime checks; where no-trip-purpose exits stand (`ExitAt`, "placed" = `m_PenaltyCounter >= 250`).
- State 4 "followed after leaving": exits going home, with no trip purpose or moving away are sampled each pass for up to
  `SkiDayTiming.FollowWatchFrames` (at most `FollowCap` at once) and booked once (`FollowResult`); `NearJob` counts walkers near each
  lodge.
- A second, all-outcomes speed learns from arrivals, late arrivals (exact) and upper bounds; the fit check reads `SkiDayRules.FitSpeed`
  with `TripKnobs` (`FitUsesAllOutcomes` false: sends bit-identical).
- The deadline pull, the one vanilla write: for a guest on site (or arriving this pass) at a hill that closed early, `ResolveJob`
  lowers the claim's `Leisure.m_LastPossibleFrame` (the field `ClaimJob` already writes) to max(the closing now, now + 2 game h)
  through `SkiDayTiming.PullDeadline`, only while our value is still in place; vanilla's `LeisureSystem` then ends the visit
  (`LeisureSystem.cs:314-317`, 1.6.2 :308-311). `c.Deadline` moves with it.

## Lodge tiers

The ladder, the mountain lodges and the school reach the jobs as plain struct fields filled on the main thread
(`SkiDaySystem.RebuildSnapshot`, from `SkiResortAttractivenessSystem.CopyActiveLodgeInfo` and `SkiCatalog.Lodges`): no managed catalog
row is read in a job. `LodgeJob` has `Capacity`, `PriceTolerance` and `NoRoadNeeded` (`RoadRule != Base`); `AreaJob` has `SchoolFactor`
(`SchoolRule.DemandFactorOf`); `ClaimOut` has `PriceTolerance` and `LodgeCapacity`.

- Step A: `LodgeFacts.NoRoadNeeded` from the entry; capacity = sum of `Capacity` over passing lodges (`SkiDayRules.Capacity`'s per-tier
  overload); targets = passing lodges with a Base road rule (claims, `TargetLodges`, `AreaFacts.SkiDayLodges`, `NearestTarget`): a
  mountain lodge holds skiers but is never a claim target; the area's `PriceTolerance` = max over passing lodges.
- Step B (and `TooLate`'s measure): `Chance(..., Price, the area's PriceTolerance, aj.SchoolFactor, ...)`.
- For an A-frame-only area every number equals the plain case (`CheckLodge` verdicts, 360 x n, tolerance 0, factor 1).

## Near SkiDayClaim

Sequential layout, field order as written (128 bytes; 32,768 at the cap: 4.0 MiB). `ClaimJob` starts `RemoveWhy`, `MoveReason` and
`ExitAt` at 255.

## Near ResolveJob

A Gone claim whose citizen stands in the lodge is booked as an arrival with the visit end `RemovedOnArrival`; the household-need
register is sampled at the end of the `OnTheWay`, `Arrived` and `OnSite` cases, never before a `VisitEnd` of the same pass; every
visit end reads the game's clock checks and, before the deadline, why the Leisure went; the all-outcomes speed learns at arrivals, at
Gone / Retargeted failures that give a bound and at the end of a late watch. Per claim the measure rows cover failure place, markers,
why, phases, stay, end cause, exit, modes, bands, car reserve and scan verdict; a state-2 claim goes to `Exit` (keep watching,
recorded, or now followed), a state-4 claim to `Follow`; a state-0 or state-3 claim that is not gone is sampled (car reserve, turned
back, departure, walker and vehicle, closest approach).

## Near the deadline pull (ResolveJob)

The field `ClaimJob` wrote at the claim (`LeisureSystem.cs:398-405`, 1.6.2 :392-399) is lowered only, and only while our value is
still in place: vanilla writes `m_LastPossibleFrame` only at Leisure creation (`CitizenBehaviorSystem.cs:494`, 1.6.2 :493), so a mod
that changed it is left alone. A developer time jump that fires the pull is accepted; the `[closing]` line says so (`SkiDaySystem`).

## Near Sample

Numbered as in the code:

1. Car reserve (queued car claims only): "held while still at home" means held before the walker spawned, NOT in time for the route.
   `TripNeededSystem` asks for a car route only if a held car is parked, about 16 frames after the claim, and our first look is 64
   frames after it.
2. Turned back: a non-Leisure `TravelPurpose` away from the origin. `GoingHome` is `ResidentAISystem`'s ReturnHome after a failed
   re-path (1.6.2 :1344-1358).
3. Departure (`SkiDayTiming.DepartedNow`): out of the origin and in no building, or with `TravelPurpose` Leisure, or in the lodge: a
   walker was spawned, path failed or not.
4. Walker and vehicle, read ONLY while the citizen is in no building (after an arrival the creature can live on and be reused for the
   next trip, 1.6.2 `ResidentAISystem.cs:2287-2309`):
   - no live walker means walker lost (a failed car path leaves the citizen in no building with no walker, :1658-1661, :1284-1289);
   - `Divert{PathFailed}` on the walker means path failed at the start (`TripNeededSystem.cs:681-686`);
   - `PathOwner` Failed or Stuck on the walker or the lead vehicle means walker / vehicle path failed (transient);
   - the ride (`CurrentVehicle` with Ready: vehicles hold boarding until every passenger is Ready, so a 64-frame sample cannot miss a
     ride) with its leg and transit kind;
   - the closest approach to the lodge from the lead vehicle or the walker.

`HeldAtHome` is never set (every queued car claim had left home at our first look); `HeldEver` alone stays. The walker's
`Divert{PathFailed}` is the door wait: `atDoor` = the mark this pass (false on every early return), and the first pass a live walker
carries no mark sets `MoveFrame` (`SkiDayTiming.MovedOffNow`).

## Near NearJob

Walker kind: `SkiDayTiming.WalkerKindOf` (aboard = `CurrentVehicle` with Ready); not drawn = Unspawned; waiting time from
`Resident.m_Timer` (`WaitHoursOfTimer`). Stop points: one point per line per stop. Reads only; writes only `Out` and `Totals`
(`Totals[0]` walkers scanned, `[1]` line stop points scanned). A walker near two lodges counts at both.
