# SkiDaySystem.Jobs: what each Burst job reads and writes, the measure-only parts, and the deadline pull

Decompile baseline `Game.dll` 1.6.0f1 (1.6.2f1 line numbers are given where they moved). The claim struct is 128 bytes (4.0 MiB at
the cap). The ring record is 108.

## The jobs

- `SiteJob` (at a snapshot rebuild and on the 4,096-frame backstop). Per lodge, it computes the lodge's xz and the distance to the nearest
  run outline or lift station of its ski area. The run outline is the `Game.Areas.Node` polygon, with the `Geometry` box as an exact skip.
  The job reads Transform / Geometry / Node through read-only lookups. It is a job edge after the move systems, never a main-thread read.
- `ResolveJob` (every pass). Per tracked claim, `SkiDayTiming.Resolve` keeps or drops the claim. The job also handles arrivals, failures,
  learning (`SkiDayRules.RecordOutcome` / `LearnSpeed`), and the claims on the way + on site per area. Its one write to vanilla state is
  lowering a claim's own `Leisure.m_LastPossibleFrame` when the hill closes early (the deadline pull, below).
- `ClaimJob` (every pass while a ski area exists):
  - Step A: lodge verdicts (`SkiDayRules.CheckLodge`), capacity, area state.
  - Step B (an area Open, a knob on, under the cap): the candidate walk and the claim. The claim writes `Leisure.m_TargetAgent` +
    `m_LastPossibleFrame` and one `TripNeeded`. For a Car-class claim without a parked car, it also writes the `CitizenBehaviorSystem`
    car-reserve queue.
  - The job writes nothing else. It makes no structural change and uses no command buffer.
- `CensusJob` (first pass after a load, then each game hour). It counts tourist households (DUMP), lodge occupants by kind and attraction
  outings still Traveling to a sign or lodge. Measure only.
- `NearJob` (every `Tuning.NearScanPasses` passes with an area, on the hour and on the timed `[cost]` pass. `SkiDaySystem` schedules it
  after `ClaimJob`). Per lodge, it counts the walkers within `SkiDayTiming.NearLodgeM`. The groups are: waiting for transport, failed path, hanging
  around, walking, not drawn, ski-day guests among them, and the game timer's wait so far. It also counts the game's own waiting count at
  the line stop points within that radius. It uses read-only type handles. It writes only its own `NearOut` / `Totals`.

## Burst rules

- No managed type, string or exception.
- Every component read is a `TryGetComponent` / `HasComponent` / `TryGetBuffer`. A destroyed lodge or a citizen gone since the claim is a
  normal input. Release builds run without collection checks, so an unguarded lookup reads a dead chunk slot.
- Every index into a snapshot array is range-checked.
- Area -1 (an orphan claim: its sign left the snapshot) writes only the orphan row.
- Tracks are copy-modify-write (no ref into a `NativeArray` / `NativeList` element).

One access mode per type per job:
- `ClaimJob` holds `Leisure` / `TripNeeded` as RW type handles and no lookup of them.
- `ResolveJob` holds `Leisure` as a RW lookup (the deadline pull).
- `CensusJob` holds `Leisure` as a RO lookup.

The jobs run on one chain (resolve, claim, census), so no two of them touch `Leisure` at once. `ResolveJob`'s RO `TripNeeded` lookup follows
the same rule: `ClaimJob`, which writes it, is scheduled after `ResolveJob`.

## Measure-only parts

`SkiDayClaim` carries departure, ride, end and closest-approach frames, markers and the mode seen. It has states 2 (visit over, still
inside), 3 (expired, watched for a late arrival) and 4 (followed after leaving). None of these states is ever counted as on the way or on
site.

`ResolveJob` samples each claim (`Sample`). It books these into a per-area measure row (RM / RH, written only through `MeasureWrite`'s
range checks):
- failures by place, cause and marker,
- trip phases, visit end and exit,
- modes, distance bands and the car reserve.

It also keeps a 40-slot ring of ended ski days (`ClaimRecord`) and notes resident arrivals for the ledger check (`ResidentNote`). `ClaimJob`
counts decisions refused as too late to fit, the pool's chances and the sent distance bands (CM). No guest rule, learning input, hub view
or vanilla write changes except the deadline pull. Every other lookup is `[ReadOnly]`.

- A Gone claim whose citizen stands in the lodge is booked as arrived (learning included, `BookArrival`) with the visit end
  "removed (on arrival)". Then the normal exit watch follows.
- Door wait (`MoveFrame`: the first pass the walker no longer carries `Divert{PathFailed}`. Related names: `DoorN`, `DoorUnseen`,
  `NeverMovedFailed`, `RH.Door`, `ResolveOut.AtDoor`). A claim that changes state in a pass is counted in its new bucket (Leaving,
  Watching, Following). The job bins late arrivals by clock (`RM.LateClock`).
- At a visit end before the deadline (need met, removed, removed on arrival), the job books why the Leisure went. The source is `SkiDayTiming.RemoveWhyOf`
  over the game's traces and a 2-bit household-need register sampled on the way and on site. At every visit end, it also books the game's
  own work-time / bedtime checks. It books where no-trip-purpose exits stand (`ExitAt`, "placed" = `m_PenaltyCounter >= 250`).
- State 4 "followed after leaving": the job samples exits going home, with no trip purpose or moving away, each pass for up to
  `SkiDayTiming.FollowWatchFrames` (at most `FollowCap` at once). It books each once (`FollowResult`). `NearJob` counts walkers near each
  lodge.
- A second, all-outcomes speed learns from arrivals, late arrivals (exact) and upper bounds. The fit check reads `SkiDayRules.FitSpeed`
  with `TripKnobs` (`FitUsesAllOutcomes` false: sends bit-identical).
- The deadline pull is the one vanilla write. `ResolveJob` does this for a guest on site (or arriving this pass) at a hill that closed early. It
  lowers the claim's `Leisure.m_LastPossibleFrame` (the field `ClaimJob` already writes) to max(the closing now, now + 2 game h)
  through `SkiDayTiming.PullDeadline`. It does this only while our value is still in place. Vanilla's `LeisureSystem` then ends the visit
  (`LeisureSystem.cs:314-317`, 1.6.2 :308-311). `c.Deadline` moves with it.

## Lodge tiers

The ladder, the mountain lodges and the school reach the jobs as plain struct fields filled on the main thread
(`SkiDaySystem.RebuildSnapshot`, from `SkiResortAttractivenessSystem.CopyActiveLodgeInfo` and `SkiCatalog.Lodges`). No job reads a managed
catalog row.
- `LodgeJob` has `Capacity`, `PriceTolerance` and `NoRoadNeeded` (`LodgeSupplyRule.NoRoadNeeded` per building: the Hut and Summit, or an
  off-road lodge on a cat track chain).
- `AreaJob` has `SchoolFactor` (`SchoolRule.DemandFactorOf`).
- `ClaimOut` has `PriceTolerance` and `LodgeCapacity`.

- Step A:
  - `LodgeFacts.NoRoadNeeded` comes from the entry.
  - Capacity = sum of `Capacity` over passing lodges (`SkiDayRules.Capacity`'s per-tier overload).
  - Targets = passing lodges that need a road (claims, `TargetLodges`, `AreaFacts.SkiDayLodges`, `NearestTarget`). A mountain or off-road
    lodge holds skiers but is never a claim target.
  - The area's `PriceTolerance` = max over passing lodges.
- Step B (and `TooLate`'s measure): `Chance(..., Price, the area's PriceTolerance, aj.SchoolFactor, ...)`.
- For an A-frame-only area, every number equals the plain case (`CheckLodge` verdicts, 360 x n, tolerance 0, factor 1).

## Near SkiDayClaim

Sequential layout, field order as written (128 bytes, 32,768 at the cap: 4.0 MiB). `ClaimJob` starts `RemoveWhy`, `MoveReason` and
`ExitAt` at 255.

## Near ResolveJob

- A Gone claim whose citizen stands in the lodge is booked as an arrival with the visit end `RemovedOnArrival`.
- The job samples the household-need register at the end of the `OnTheWay`, `Arrived` and `OnSite` cases, never before a `VisitEnd` of
  the same pass.
- Every visit end reads the game's clock checks and, before the deadline, why the Leisure went.
- The all-outcomes speed learns at arrivals, at Gone / Retargeted failures that give a bound and at the end of a late watch.
- Per claim, the measure rows cover failure place, markers, why, phases, stay, end cause, exit, modes, bands, car reserve and scan verdict.
- A state-2 claim goes to `Exit` (keep watching, recorded, or now followed). A state-4 claim goes to `Follow`. A state-0 or state-3 claim
  that is not gone is sampled (car reserve, turned back, departure, walker and vehicle, closest approach).

## Near the deadline pull (ResolveJob)

`ResolveJob` only lowers the field `ClaimJob` wrote at the claim (`LeisureSystem.cs:398-405`, 1.6.2 :392-399), and only while our value is
still in place. Vanilla writes `m_LastPossibleFrame` only at Leisure creation (`CitizenBehaviorSystem.cs:494`, 1.6.2 :493), so the pull
leaves alone a mod that changed it. A developer time jump that fires the pull is accepted. The `[closing]` line says so (`SkiDaySystem`).

## Near Sample

Numbered as in the code:

1. Car reserve (queued car claims only): "held while still at home" means held before the walker spawned, NOT in time for the route.
   `TripNeededSystem` asks for a car route only if a held car is parked, about 16 frames after the claim. Our first look is 64
   frames after it.
2. Turned back: a non-Leisure `TravelPurpose` away from the origin. `GoingHome` is `ResidentAISystem`'s ReturnHome after a failed
   re-path (1.6.2 :1344-1358).
3. Departure (`SkiDayTiming.DepartedNow`): a walker was spawned, path failed or not. The test is true in any of these cases:
   - The citizen is out of the origin and in no building.
   - The citizen has `TravelPurpose` Leisure.
   - The citizen is in the lodge.
4. Walker and vehicle, read ONLY while the citizen is in no building. After an arrival, the creature can live on and be reused for the
   next trip (1.6.2 `ResidentAISystem.cs:2287-2309`):
   - No live walker means walker lost (a failed car path leaves the citizen in no building with no walker, :1658-1661, :1284-1289).
   - `Divert{PathFailed}` on the walker means path failed at the start (`TripNeededSystem.cs:681-686`).
   - `PathOwner` Failed or Stuck on the walker or the lead vehicle means walker / vehicle path failed (transient).
   - The sample also reads the ride (`CurrentVehicle` with Ready) with its leg and transit kind. Vehicles hold boarding until every
     passenger is Ready, so a 64-frame sample cannot miss a ride.
   - It also reads the closest approach to the lodge from the lead vehicle or the walker.

`HeldAtHome` is never set (every queued car claim had left home at our first look). `HeldEver` alone stays. The walker's
`Divert{PathFailed}` is the door wait: `atDoor` = the mark this pass (false on every early return). The first pass a live walker
carries no mark sets `MoveFrame` (`SkiDayTiming.MovedOffNow`).

## Near NearJob

Walker kind: `SkiDayTiming.WalkerKindOf` (aboard = `CurrentVehicle` with Ready). Not drawn = Unspawned. Waiting time comes from
`Resident.m_Timer` (`WaitHoursOfTimer`). Stop points: one point per line per stop. The job only reads. It writes only `Out` and `Totals`
(`Totals[0]` walkers scanned, `[1]` line stop points scanned). A walker near two lodges counts at both.
