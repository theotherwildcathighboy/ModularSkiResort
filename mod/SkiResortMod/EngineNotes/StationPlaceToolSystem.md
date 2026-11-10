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
- y is NaN. The placer then sets y by the height rule of the request (see Height).
- `Valid` is false when a spot is not finite, or when the spots are no more than both stub offsets plus `MinTipGapM` (1 m) apart.
- LiftBatch SP-1 to SP-6 pin it: four types, 72 directions, three lengths, the axis through the spot, the tip distance, the degenerate cases.
  SP-7 pins the height rule.

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
A request without a y gets the y of its `HeightRule` (`StationAlign.StationY`, pure, LiftBatch SP-7). An explicit y is used as is.
- VanillaFront (the default of a request, so the dev probe): the terrain at the front edge of the lot, plus
  `PlaceableObjectData.m_PlacementOffset.y`. This is the height of the object tool (ObjectToolSystem.cs:1608-1625,
  `BuildingUtils.CalculateFrontPosition` :88-92). The front edge is the side away from the line.
- LowestOfThree (all station kinds): the lowest of three terrain heights, plus the offset. The three heights are at the front edge, at
  the origin and at the line-side edge (local -z, the stub tip side). A height that is not finite is skipped.
- Fit (the build queue, task R1-e): the rule per station kind (`StationAlign.StationTerrainKind`). A pad station takes LowestOfThree. A
  slope station takes the terrain at its origin, plus the offset. Without a finite origin it takes the lowest of the other two.
- The station kind comes from the prefab at run time: its `BuildingTerraformData` (`StationAlign.TerrainKindOf`). Slope = `m_DontRaise`
  and `m_DontLower` both set. Pad = anything else. The flags come from the prefab's `BuildingTerraformOverride`
  (BuildingInitializeSystem.cs:1364-1365). Every BuildingPrefab has the component (BuildingPrefab.cs:36).
  The catalog holds the same fact as `StationDef.PadBackLocalZ` (NaN = keeps the slope), and LiftBatch EI-48 gates it against the mirror.
  The placer reads the prefab, because it has the prefab entity and no catalog row. The hub tile's terrain line is UI data
  (buildItems.ts), so the placer does not read it.
- Why: on a top station the front edge is uphill. In the r1c pass the vanilla y stood 4.8 to 13.8 m over the terrain at the origin of
  every top. The rope anchor of the top then stood 15 to 26 m over its stub node (`LiftLine [plan]` "node + 24.38"). The ruling is that
  stations cut their pad and never raise the ground (DECISIONS 625, 626). On a base the front edge is downhill, so the two rules agree on a
  plain slope. A slope station does not cut. So LowestOfThree sinks its uphill side, and the origin splits the error between the two sides.
- The switch: `ResortLiftBuildSystem.StationHeight` (default `DefaultStationHeight` = Fit). The dev trigger "height vanilla", "height
  lowest" or "height fit" changes it for the next stations. A load resets it.
- Log: one `StationPlace [height]` line per request. It gives the rule, the station kind, the three terrain heights and the offset. It
  also gives the chosen y, the vanilla y and y minus the origin terrain.

What the game does with the lot when y is below the terrain under part of it (1.6.0f1, the same in 1.6.2f1):
- The terrain under a building comes from `BuildingUtils.CalculateLotInfo` (BuildingUtils.cs:538-619), called by
  `TerrainSystem.CullBuildingLotsJob` (TerrainSystem.cs:896-899). The target height is the Transform y plus
  `BuildingTerraformData.m_HeightOffset` (:566, 0 on our stations) plus the Lot edge heights.
- The Lot edge heights only match neighbor lots and nets (LotHeightSystem.cs:616-756). A free station has none.
- `m_DontLower` empties `m_MinLimit` (:610-613). `m_DontRaise` empties `m_MaxLimit` (:614-617). The BuildingLot shader applies the
  limits (TerrainSystem.cs:1620-1639, :2679). The shader is not in the decompile. Field evidence (DECISIONS 625): DontRaise alone cuts and
  never fills. So `m_MinLimit` is the cut window and `m_MaxLimit` the fill window.
- The object validation has no error for a building below the terrain (Game.Objects/ValidationHelpers.cs: no such error type).
- Expected in game, per station type:
  - FG base, DQ base and top, gondola base and top (DontRaise only, pad): the cut window is lowered to y. A level pad at the lowest of the
    three heights. Nothing is raised. A top no longer floats: its pad is cut into the uphill side, with the 8 m apron as the bank.
  - Tram base (no override, pad): it levels to y. With y at the lowest sample, nearly all of it is cut. A lot corner lower than the
    three samples can still be filled a little (UNVERIFIED).
  - FG top and tram top (block A, DontRaise and DontLower, slope): both windows are empty, so the terrain does not change
    (LotHeightSystem.cs:253 also skips them). Under LowestOfThree the uphill part of the lot is below the ground by up to (front - y).
    On the r1c tops that is up to about 17 m (plan #8 P3: front 997.61, stub nodes 980.4). Under Fit (the origin) the uphill side is
    below the ground and the downhill side floats, each by about half the drop across the lot. UNVERIFIED in game: r1e-bridge-sheet.md
    item A3c.

## Plan sheet (`ResortPlanSheet`, `ResortLiftBuildSystem`)
- The rows are the proposed lifts of the last published plan (not the pinned lifts). `QuickRunsToolSystem.FinishScan` loads them
  (`PublishSheet`). A failed scan or a plan without lifts empties the sheet.
- `TogglePlanKey` keeps the `On` flag of each row in step with the planner (`SyncToggles`). A toggle of another plan has no effect.
- The sheet outlives the run build and the planner tool. A new published plan replaces it. `ClearSheet` empties it. Session only.
- Load sorts the rows by the height of the bottom spot, lowest first. Ties go by key. A NaN height sorts last. A repeated key keeps its
  first row.
- Row: `Key`, `Name` ("P1"), `TypeIndex`, `Bottom`, `Top`, `Cost`, `On`, `State`, `ReasonKey`, `BaseStation`, `TopStation`, `Order`.
- The over-limit flag (task R2-l): `OverLimitTowers`, `OverLimitTower`, `OverLimitAlongM`, `OverLimitBendDeg`, `OverLimitCapDeg`.
  A Built row whose line has towers over their sheave-train limit carries them. The state stays Built. `SetOverLimit` writes them, and
  a count of 0 clears them. The dump prints `[OverLimitTowers=N]` after the state. LiftBatch PS-13 pins it.
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
3. Stations. The missing stations go to the placer, base first. The queue asks the top only after the base stands
   (`ResortPlanSheet.NextStationStep`, pure, LiftBatch PS-10). The queue's stations take the height rule `StationHeight` (see Height).
   - The game refuses the base: nothing is placed and nothing is charged. Refused BASE[error type]. Before R1-e the queue asked both at
     once. The placer then placed and charged the top in the frame that refused the base (field r1c item 8b).
   - The game refuses the top: the base stays in the row. Refused TOP[error type].
   - The next lift starts in both cases.
   - Between the two requests the placer gives the tool to the default tool and takes it back in the same frame (UIUpdate). The tool
     system compares the tools only in its next update (ToolSystem.cs:312-322), so the placer does not stop.
4. Settle. The lot cut runs after the station is permanent, and the CPU heights come later. The queue reads the terrain under both stub
   tips every 10 frames. It goes on when two reads agree within 0.05 m, after at least 30 frames. After 900 frames it logs a warning
   and goes on.
5. Line. `LiftLineToolSystem.TryActivateFor` with auto build.
   - Built: the row is Built. The next lift starts. A line with one tower over its sheave-train limit also builds (LiftLineToolSystem.md,
     Scripted entry). Its row gets the over-limit flag. The BUILT line adds `FLAGGED — 1 tower(s) over the sheave-train limit: tower T at
     A m bends B deg, its limit is C deg (OverLimitTowers=1)`.
   - Waiting: NeedsFitting with the reason. The queue stops with the tool open in Edit.
   - Refused (the tool could not pick the ends): Refused LINE[PICK]. The next lift starts.
   - Abandoned (the player took another tool): StationsPlaced STOPPED. The queue stops.
6. The tool. A built scripted line gives the lift tool back to the default tool (LiftLineToolSystem.md, Scripted entry). When the queue
   ends, `HandToolBack` gives the tool back if it is the placer with nothing queued, or the lift tool with nothing in hand
   (`IdleAfterScript`). A line that waits for the player keeps the lift tool open. A tool that the player took is never touched.
   Log: `ResortLifts [queue]: the tool goes back to the default tool` or `the tool stays (...)`.

More rules:

- Stop: the lift in flight finishes. No lift starts after it.
- A new plan or a clear stops the queue the same way. The lift in flight writes nothing into the new rows (`PlanSerial` guard).
- A row that waits in the lift tool stays on a watch list. When the player builds the line later, the row becomes Built. When the ticket
  ends another way, the row becomes StationsPlaced with LINE[ABANDONED] or LINE[REFUSED]. A line that the player builds sets the
  over-limit flag from its plan, also with two or more towers.
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
  keys out of the queue but NOT out of the total. Pass r1e row B3 read "Stopping after lift 1 / 1" for a queue of three (fixed in R1-f).
  The hub reads the place in the build that was asked for until the queue ends. `QueueIndex` (pure: `ResortPlanSheet.QueueIndexOf`, LiftBatch PS-12) is the
  1-based place of the lift in flight (or next), capped at the total. Between a lift's end and the queue's end after a Stop it stays on the
  lift that finished. A run starts counting from zero when the queue was idle. `QueueStopping` is true after Stop while a lift is in flight.
- `QueueRevision` moves with every start, step, end, stop and notice. The hub pushes on it, because none of those change a sheet row.
- `ResortPlanSheet.ReasonGroup(reasonKey)` (pure, LiftBatch PS-8) maps a reason key to a player-text group of the hub. An unknown key is
  GAME, so the hub never shows a raw key. The groups are:
  - MONEY, POSE, PREFAB, GONE, STOPPED, GAME.
  - BASE_* and TOP_* (OVERLAP, WATER, LAND, OTHER), by the error type of the game.
  - LINE_PICK, LINE_ABANDONED, LINE_REFUSED.
  - PLAN_LOW, PLAN_STEEP, PLAN_HIGH, ALONG, CROSS, BACKWARD.
  - PLAN_TRAIN, PLAN_SADDLE, PLAN_PITCH, PLAN_CLEARANCE (R1-f): the other plan keys of `LiftLineInput.PlanKey`.
  - PLAN_TRAINS (task R2-l): two or more towers over their sheave-train limit. The queue builds a line with one such tower, so a
    waiting train row reads PLAN_TRAINS. PLAN_TRAIN stays in the list for older rows.
    A line waits on them because the tool's auto build needs a Sound plan. A tower over its cap is not Sound.
    Nothing refused the line, so these keys have their own texts.
  - BUILD_REFUSED: the game refused the build while the tool waited.
  - WAIT: any other waiting text (no plan, auto build off, the build request refused, no Temp edges).
- `ResortPlanSheet.WaitingKey(reason)` (pure, LiftBatch PS-11) turns the Reason of a waiting ScriptTicket into the row's key.
  A plan or notice key stays as it is. "the game refused the build ..." is BUILD_REFUSED. Any other text is WAIT.
  Before R1-f the result was GAME. A PLAN_TRAIN row read "the game or the lift tool refused it" (pass r1e note 4).

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
- `skiResortDev.planLifts`: `"build all"`, `"build <row name or hex key>"`, `"stop"`, `"clear"`, `"dump"`, `"height vanilla"`,
  `"height lowest"`, `"height fit"`, and
  `"line <lift type id> <x1> <z1> <x2> <z2> [manual]"` (a scripted line between two ground points).

## Logs
Silence is impossible. Every part logs ARMED or NOT ARMED per load. One line for each of these:

- `StationPlace [request]`, `[height]`, `[tool]`, `[outcome]`, and `[request] CANCELED`.
- `ResortLifts [sheet]`: LOADED, CLEARED, DUMP, and the toggle sync.
- `ResortLifts [queue]`: BUILD, START, STATIONS PLACED, SETTLED, LINE, the end state of each lift, STOP, END.
- `ResortLifts [watch]`: the end of a watched line.
- `ResortLifts [hub]`: each hub call and its result. `ResortLifts [sheet]` also logs BUILT by hand.
- `ResortLifts [marks]`: SHOWN and HIDDEN.
- `ResortLifts [dev]`: each dev call.

An exception disables the part until the next load (one "!!!" ERROR with the stack).

## Not proven in game
The host tests prove the pose, the sheet, its totals, the reason groups, the hand-built match, the station order (PS-10) and the height
rule (SP-7). The hub rows, the buttons and the notices
are proven in the harness only (UI). The bridge sheet `r1c-bridge-sheet.md` (private docs) proves the rest: the queue, the
settle rule, the scripted line, the refusals and the marks. The sheet `r1e-bridge-sheet.md` proves the base-first order in game, the tool
hand-back, the new height (photos for the owner) and the hub rows by real clicks.
