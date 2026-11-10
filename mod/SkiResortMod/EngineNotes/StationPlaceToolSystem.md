# StationPlaceToolSystem: the station placer, the plan sheet, the lift build queue and the plan marks

The Resort Planner builds its proposed lifts (task R1-c, plan sections 3.1 to 3.4, DECISIONS 630). Five parts do it:

- `StationAlign.PoseLift` (pure): the two station poses of a planned lift.
- `StationPlaceToolSystem`: the station placer, a queue of station requests.
- `ResortPlanSheet` (pure) and its owner `ResortLiftBuildSystem`: the plan sheet.
- `ResortLiftBuildSystem.Queue.cs`: the lift build queue.
- `ResortPlanMarksSystem`: the plan marks outside the planner.

The lift-line tool has a scripted entry for the line (LiftLineToolSystem.md, Scripted entry). The hub (task R1-d, contract 41) reads the sheet and
calls the queue (see Hub entry points). Citations are `File.cs:line` against the decompile baseline 1.6.0f1.

## Station pose (`StationAlign.PoseLift`)
- Input: the two spots of a `PlanLift` (`Bottom`, `Top`) and the station rows of the lift type (`SkiCatalog.StationOf`).
- A spot is the lot center of its station. The line axis is the local x of the stub tip (`StubTipLocalX`). The axis goes through the spot.
  So the tram base sits 4 m to the side, and the line stays on the chord.
- The base faces the top with its local -z: `RotateY(atan2(-d.x, -d.y))`. The top faces the base: `RotateY(atan2(d.x, d.y))`.
- Each stub tip is |StubTipLocalZ| in from its spot: 12 m on the fixed-grip and tram stations, 16 m on the DQ and gondola stations.
- y is NaN. The placer then takes the vanilla height (see Height).
- `Valid` is false when a spot is not finite, or when the spots are no more than both stub offsets plus `MinTipGapM` (1 m) apart.
- LiftBatch SP-1 to SP-6 pin it: four types, 72 directions, three lengths, the axis through the spot, the tip distance, the degenerate cases.

## Station placer (`StationPlaceToolSystem`)
- A prefab-less subclass of `ObjectToolBaseSystem`. Vanilla builds object definitions only in `CreateDefinitions`, which is protected
  (ObjectToolBaseSystem.cs:2363, 1.6.2f1 :2382). With one control point and distance 0, its job emits the building with its sub-nets.
- `Enqueue(why, PlaceRequest)` returns a serial. It returns 0 when it refuses the request: not armed, disabled, a full queue, no
  prefab, not a building, or x or z not finite. `TryGetOutcome(serial)` gives the outcome. The placer keeps the last 64 outcomes.
- An outcome is Pending, Placed (the permanent station), Refused (the error types of the game on the preview) or Canceled.
  `FirstError` is the first typed error. The sheet uses it as the reason key.
- `CancelPending(tag, why)` drops the queued requests with that tag. It never drops the request in flight.
- Phases: Pending, then Emitted (wait for the Temp, one settle frame, then Apply or Clear), then Applying (read the permanent station).
  Apply and Clear come from the tool that ran this frame (ToolSystem.cs:147-156, ToolOutputSystem.cs:22-28). Apply keeps the Temp entity
  as the building (ApplyObjectsSystem.cs:670).
- Own Temp: `FindTemp` takes a Create Temp of the requested prefab within `OwnTempM` (1 m, xz) of the requested position, the nearest one.
- The tool stays active while requests wait in the queue. Then it gives the tool back to the default tool.
- When another tool takes over, `OnStopRunning` cancels the queue. A request in Applying gets its station by a pose search.
- An inactive tool never ticks (ToolSystem.cs:312-327). `TickWatch` runs every frame from `StationPlaceProbeUISystem` for the follow-up line.

### Height
Vanilla stands a building on the terrain at the front edge of its lot, plus `PlaceableObjectData.m_PlacementOffset.y`
(ObjectToolSystem.cs:1608-1625, `BuildingUtils.CalculateFrontPosition` :88-92). `ResolveY` does the same when the request has no y.

## Plan sheet (`ResortPlanSheet`, `ResortLiftBuildSystem`)
- The rows are the proposed lifts of the last published plan (not the pinned lifts). `QuickRunsToolSystem.FinishScan` loads them
  (`PublishSheet`). A failed scan or a plan without lifts empties the sheet.
- `TogglePlanKey` keeps the `On` flag of each row in step with the planner (`SyncToggles`). A toggle of another plan has no effect.
- The sheet outlives the run build and the planner tool. A new published plan replaces it. `ClearSheet` empties it. Session only.
- Load sorts the rows by the height of the bottom spot, lowest first. Ties go by key. A NaN height sorts last. A repeated key keeps its
  first row.
- Row: `Key`, `Name` ("P1"), `TypeIndex`, `Bottom`, `Top`, `Cost`, `On`, `State`, `ReasonKey`, `BaseStation`, `TopStation`, `Order`.
- States: Proposed, StationsPlaced, Built, Refused, NeedsFitting.
- Reason keys of the queue: MONEY, POSE, PREFAB, BASE[error type], TOP[error type], BASE[GONE], TOP[GONE], STOPPED.
- Reason keys of the line: LINE[PICK], LINE[ABANDONED], LINE[REFUSED], GAME, and the plan keys of the lift tool (PLAN_LOW,
  PLAN_STEEP, ALONG, CROSS_*, BACKWARD).
- `Revision` changes with every change. `PlanSerial` changes with every load and clear. The hub reads `Rows`, `Revision` and `PlanSerial`.
- LiftBatch PS-1 to PS-6 pin the order, the revision rules, build all, the states, the clear and the dump.

## Build queue (`ResortLiftBuildSystem.Queue.cs`)
Entry points: `BuildAll(why)`, `BuildOne(key, why)`, `Stop(why)`, `ClearSheet(why)`, `DumpSheet(why)`. They only queue keys. The first
step runs in a later tick of `ResortLiftBuildSystem` (UIUpdate, every frame, whatever tool is active). No step runs inside the update of
another tool.

Per lift, lowest bottom spot first:

1. Money. The city must pay the lift estimate less the stations that the row already has. If not: Refused MONEY, and nothing is placed.
2. Pose. `PoseLift`. If not valid: Refused POSE.
3. Stations. The missing stations go to the placer, base first.
   - The game refuses the base: the queue cancels the top. Refused BASE[error type].
   - The game refuses the top: the base stays in the row. Refused TOP[error type].
   - The next lift starts in both cases.
4. Settle. The lot cut runs after the station is permanent, and the CPU heights come later. The queue reads the terrain under both stub
   tips every 10 frames. It goes on when two reads agree within 0.05 m, after at least 30 frames. After 900 frames it logs a warning
   and goes on.
5. Line. `LiftLineToolSystem.TryActivateFor` with auto build.
   - Built: the row is Built. The next lift starts.
   - Waiting: NeedsFitting with the reason. The queue stops with the tool open in Edit.
   - Refused (the tool could not pick the ends): Refused LINE[PICK]. The next lift starts.
   - Abandoned (the player took another tool): StationsPlaced STOPPED. The queue stops.

More rules:

- Stop: the lift in flight finishes. No lift starts after it.
- A new plan or a clear stops the queue the same way. The lift in flight writes nothing into the new rows (`PlanSerial` guard).
- A row that waits in the lift tool stays on a watch list. When the player builds the line later, the row becomes Built. When the ticket
  ends another way, the row becomes StationsPlaced with LINE[ABANDONED] or LINE[REFUSED].
- A second build of a row starts from the stations that it already has.
- The run build starts the queue for the lifts that are on (`FinishBuild`, plan 3.2: runs first, then lifts). A stopped run build does not.
- Enter refuses when the city cannot pay the runs and the lifts that are on (`BuildFromTool`, plan 3.3). The money warning of the tool
  uses the same sum.

## Hub entry points (task R1-d)
SkiResortHubUISystem.md (Plan sheet lifts) has the binding. The engine side gives the hub these members:
- Read API: `Sheet` (Rows, Revision, PlanSerial, Count, `CountToBuild`, `CountUnbuilt`, `CountIn`, `CostToBuild`), `QueueRunning`,
  `QueueStopping`, `QueueIndex`, `QueueTotal`, `QueueRevision`, `InFlightKey`, `PendingKeys`, `Notice`.
- `HubAct(action, key, plannerBusy)`: buildAll, build, stop, clear. It refuses with a notice (Off, RunsBusy, Nothing, Queued, Busy) and a
  `ResortLifts [hub]` line. `plannerBusy` is the hub's test of the Resort Planner tool (a phase other than Region, or planting).
- Queue count: `QueueTotal` counts the keys asked in one run. A call while the queue runs adds to it. Stop and a stopped lift take the waiting
  keys out. `QueueIndex` is the 1-based place of the lift in flight (or next), capped at the total. A run starts counting from zero when the
  queue was idle. `QueueStopping` is true after Stop while a lift is in flight.
- `QueueRevision` moves with every start, step, end, stop and notice. The hub pushes on it, because none of those change a sheet row.
- `ResortPlanSheet.ReasonGroup(reasonKey)` (pure, LiftBatch PS-8) maps a reason key to a player-text group of the hub. An unknown key is
  GAME, so the hub never shows a raw key. The groups are:
  - MONEY, POSE, PREFAB, GONE, STOPPED, GAME.
  - BASE_* and TOP_* (OVERLAP, WATER, LAND, OTHER), by the error type of the game.
  - LINE_PICK, LINE_ABANDONED, LINE_REFUSED.
  - PLAN_LOW, PLAN_STEEP, PLAN_HIGH, ALONG, CROSS, BACKWARD.

## A line the player draws by hand (task R1-d)
The queue and the watch list only see lines that the scripted lift tool builds. A player can also draw the line between two placed stations
of a row. `ResortLiftBuildSystem.TickHandBuilt` catches that and makes the row Built:
- Rule (`ResortPlanSheet.HasCompleteLiftBetween`, pure, LiftBatch PS-7): a census row (`LiftCensus.Row`) must match all of these:
  - State Complete.
  - The row's lift type.
  - Base and Top equal to the row's two placed stations (either order).
- Which rows: StationsPlaced, NeedsFitting, and a Refused row that kept both stations (LINE[PICK]). Never Proposed or Built rows, and not the
  lift in flight (its own job writes the result).
- Cost: the pass runs only when the sheet `Revision` or the `LiftCensusSystem.Revision` changed since the last pass. Otherwise it is two
  integer compares a frame. It reads the census rows in place (`CensusRows`), without a copy.
- A match drops the row's watch. The log line is `ResortLifts [sheet]: P2 BUILT by hand`.

## Plan marks outside the planner (`ResortPlanMarksSystem`)
- The rows that are not Built show while one of these tools is active:
  - the lift-line tool,
  - the station placer,
  - the vanilla object tool with one of our lift stations (the test of `LiftLineHotkeySystem`).
- Per row: the dashed chord between the two spots, and a lot disc at each spot without a station. The sizes are those of the planner.
- Colors: Proposed is neutral, StationsPlaced and NeedsFitting are amber, Refused is red. A row that is off is dim.
- It draws at Rendering before `OverlayRenderSystem`, which reads and clears the overlay lists later in the same phase
  (OverlayRenderSystem.cs:781, :790). It completes the writers of the buffer first (OverlayRenderSystem.cs:603-634).

## Dev triggers (Measurement diagnostics on)
- `skiResortDev.probePlaceStation`: `"<station prefab> <x> <z> <yaw deg> [<y>]"`, one station through the placer.
- `skiResortDev.planLifts`: `"build all"`, `"build <row name or hex key>"`, `"stop"`, `"clear"`, `"dump"`, and
  `"line <lift type id> <x1> <z1> <x2> <z2> [manual]"` (a scripted line between two ground points).

## Logs
Silence is impossible. Every part logs ARMED or NOT ARMED per load. One line for each of these:

- `StationPlace [request]`, `[tool]`, `[outcome]`, and `[request] CANCELED`.
- `ResortLifts [sheet]`: LOADED, CLEARED, DUMP, and the toggle sync.
- `ResortLifts [queue]`: BUILD, START, STATIONS PLACED, SETTLED, LINE, the end state of each lift, STOP, END.
- `ResortLifts [watch]`: the end of a watched line.
- `ResortLifts [hub]`: each hub call and its result. `ResortLifts [sheet]` also logs BUILT by hand.
- `ResortLifts [marks]`: SHOWN and HIDDEN.
- `ResortLifts [dev]`: each dev call.

An exception disables the part until the next load (one "!!!" ERROR with the stack).

## Not proven in game
The host tests prove the pose, the sheet, its totals, the reason groups and the hand-built match. The hub rows, the buttons and the notices
are proven in the harness only (UI). The bridge sheet `r1c-bridge-sheet.md` (private docs) proves the rest: the queue, the
settle rule, the scripted line, the refusals and the marks.
