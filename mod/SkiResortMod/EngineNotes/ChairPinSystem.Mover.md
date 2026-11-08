# ChairPinSystem.Mover: the Burst chair move pass, its threading rules and far-loop pacing

The per-frame chair move pass at release scale. Target: under 0.5 ms a frame on the main thread for 20 lifts / ~3,000 chairs, with no
cost that grows with the city. Engine citations are `File.cs:line` against the Game.dll decompile baseline 1.6.0f1.

## What runs when

- On a loop-cache rebuild (lift edits, load, the 4,096-frame watchdog), `BuildLoopCache` seeds the pace of every loop (`LiftMoverMath.Seed`)
  and marks the flat arrays dirty. The next move pass flattens the loops once into persistent native arrays (`EnsureMoverArrays`).
  The arrays hold the segments, the chairs (with offset, grip top and loop) and a box per loop. A hold resume (Rebase) also marks them
  dirty, because the offsets moved. The cost is O(chairs + segments), never per frame.
- Every moving pass runs on the main thread and costs O(loops) + the tags. A moving pass is every rendered frame in which the render
  clock advanced (`RunMovePass`). The steps are:
  1. The job of the last frame is complete (OnUpdate's first line, `MoverCompleteJob`). Its result counts become the "last pass moved / in
     place / missing" numbers. A missing chair dirties the cache.
  2. Per loop, these rules apply:
     - A held split loop with "Animated lift carriers" on keeps swinging (DECISIONS 619). It goes into the job like a running loop.
       Its `JobLoop.Speed` is 0 and its `Phase` is `LiftLoopHold.FrozenPhase`. So s stands, bit for bit the managed frozen s.
       The sway clock, the wind and the gusts are live.
     - Its stride comes from `StrideFor` with `HeldSwayPaceMps` in place of the rope speed. That is 0.5 m/s, the top speed of a
       carrier base in a held sway. A near held loop is written every pass. A held loop 500 m or more away is written every 8th pass.
     - `Decide` with speed 0 never tags it. The sway moves a carrier much less than the 2 m between the bounds tags of a running loop.
     - Any other held loop (toggle off, no grips) keeps the managed path. The code writes its frozen pose at the hold's entry and at a
       rebuild. It also writes it once when the swing stops (the toggle went off during the hold, `LoopHold.Swayed`).
     - A running loop gets its camera distance (box), its stride (`LiftMoverMath.StrideFor`) and the pace decision
       (`LiftMoverMath.Decide`: write this pass? tag bounds?).
  3. The code adds the bounds tags of this pass in one batched `EntityManager.AddComponent<Updated>`. The tags are the loops that reached
     their refresh travel, plus the entry tags of held loops. This is a structural change, so it runs before the job is scheduled.
     A structural change completes every running job.
  4. `MoveChairsJob` is scheduled (IJobParallelFor, batch 64, Burst) over all rostered chairs. A chair of a loop that is not written this
     pass returns at once. `SumResultsJob` (IJob, Burst) folds the per-chair results into four ints.
- Worker threads: `MoveChairsJob` does these steps per chair:
  1. Check that the chair is alive (`EntityStorageInfoLookup.Exists`, no Deleted).
  2. Compute `s = (offset + phase) % L`. Phase = speed x TimeS, computed on the main thread (PhaseOf's product). It matches the managed s
     bit for bit.
  3. Run `LiftLoopSampler.Sample` (= `LiftLoopCache.SampleSegments`) and `LiftLoopSampler.Pose` (= MoveChair's pose and 1 mm idempotence).
  4. Write a Transform through `ComponentLookup<Transform>`, only when the pose changed.

## Threading / ECS correctness

- The Transform write uses `[NativeDisableParallelForRestriction] ComponentLookup<Transform>`. This is safe for two reasons. Every index
  writes a different entity, because a chair is rostered once (`BuildLoopCache` adds each chair to one loop). Nothing else in the job
  reads the Transform of another chair. The stress mode never schedules two writers of one entity at the same time. Its K passes are
  chained jobs, and each depends on the previous one.
- Dependency: the lookup is a RW declaration of Transform by this system, and the job handle is assigned to `Dependency`. So the
  Entities dependency manager makes every later reader or writer of Transform in this frame wait for the job. These readers and writers
  include Modification5 `Objects.SearchSystem` via `UpdateCollectSystem`, PreCulling, and the copy that
  `ObjectInterpolateSystem.UpdateStaticAnimations` makes into InterpolatedTransform. A later main-thread EntityManager read or write of
  Transform completes the job first. Examples are the cabin pass of `TramShuttleSystem` and a structural change by a barrier. The main
  thread then waits for what is left of the job.
- Timing: Modification4B. Vanilla's Modification4B systems and `ModificationBarrier4B` run after this system (`SystemOrder.cs:83,:91`).
  Our job is done by the time PreCulling / Rendering read the Transforms.
- The job reads our own native arrays. Only the main thread writes them, after `MoverCompleteJob`, never while the job can run.
  The dev tab and the dump read the managed loop cache, never these arrays.
- The code refreshes the lookups (`Update(this)`) right before the schedule, after the structural change of this pass.
- Held loops that do not swing, Temp-chair previews, the static flow and the roster of `BuildLoopCache` stay managed (rare events).
  A held loop that swings is a job loop (step 2).

## Far-loop pace (LiftMoverMath item 2)

A loop whose box is >= 500 m from the camera is written every k-th pass. k is at most 8 and is chosen so that a chair never steps more
than 0.5 mrad on screen. The bounds of the loop refresh every 2 m x k of its own travel. On the very pass in which the loop comes back
near, the code writes it, and it refreshes the bounds when they are >= 2 m stale. Positions are a function of the clock, so a throttled
loop is written at its exact current pose (no catch-up error). Camera = `CameraUpdateSystem.position` (the active viewer's position,
`CameraUpdateSystem.cs:164,:268`). It updates before PreCulling (`SystemOrder.cs:631`). At Modification4B it is the previous frame's
position (one frame of lag, inside the 500 m margin). No viewer -> every loop near (fail open). The test uses distance only, with no
frustum test. A loop behind the camera but within 500 m runs at full rate (cheap and safe).

## Dev tab ("Mover scale" foldout in ChairPinSystem.Debug.cs)

The foldout has these controls:

- Burst job vs managed path (A/B).
- Pace on / off.
- Stress x1 / x4 / x16. The move work repeats K times per pass: K - 1 extra writes at an earlier clock, then the real pass. The code does
  not multiply the Updated tags.
- Sync-measure. The job completes inside the timed span, so the `[cost]` numbers include it.

The once-per-load "[cost] chair mover" line reports p50 / p95 / max main-thread ms over the first 600 moving passes.

## Grips and swing (the carrier grip round, ChairPinSystem.Grips.cs, CarrierPose.cs)

A split carrier is two vanilla sub-objects of one lift edge: the BODY (the rostered carrier, seat / cabin + hanger) and the GRIP (jaws /
carriage + the hanger head). The grip pitches with the rope. The body hangs plumb from the pin and swings in the wind. Engine citations
in this section refer to the 1.6.2f1 decompile.

- **Split = the grip prefab resolved** (`SkiPrefabCatalogSystem.GripTypes`, catalog kind Grip, pending until the art import). The carrier
  drop of a split type comes from the catalog pivots (`CarrierPivots.RigidDrop` = BodyPin.y + GripRope.y - GripPin.y). It does not come
  from the bounds of the body mesh, because the body alone is shorter than the one-piece carrier. Its virtual grip top = RigidDrop +
  GripTopAboveRopeM.
- **Pairing** (roster, `RosterGrip`): the grips are their own NetSubObjects rows on the line prefab, with the EvenSpacing and the spacing
  of the carrier row. Vanilla's EvenSpacing loop writes `Aligned.m_SubObjectIndex = m x rowCount + row`
  (Game.Objects/SubObjectSystem.cs:1742-1757, rowCount = the length of the `Game.Prefabs.SubObject` buffer of the line prefab). Both rows
  get the same slot count `(int)(len / max(1, spacing) - 0.5f)`. So a body and its grip share (owner edge, slot m). The side (the lane)
  is the sign of the m_Position.x of the row (`CarrierPose.RowSide`, +-1 cm dead zone). `IndexEdgeGrips` indexes the grips of an edge
  once per pass. A missing grip leaves its body on the rigid pose. A line prefab with no grip rows (before the prefab surgery) gives one
  INFO per line prefab per load, and all its carriers stay rigid. Contract breaks give one WARN per load (`LogGripContract`):
  - Two grips on one key: the code keeps the FIRST. The other is never posed and stays at its own spawn.
  - A grip that two bodies pair with stays with the first body (one writer per entity in the job). The static pass applies the same rule
    per pass. The second body keeps the rigid pose.
- **Prefab contract for the grip rows** (so vanilla's spawn pose is already the assembled carrier):
  - Each grip row copies these fields from its carrier row: the flags (EvenSpacing), m_Position.x (the side), m_Position.z (the spacing:
    the slot count follows it) and the rotation.
  - Its m_Position.y = the y of the carrier row + (BodyPin.y - GripPin.y) - (body bounds max y - grip bounds max y).
  - The rows are AnchorTop, and vanilla lowers each object by its OWN bounds max y (SubObjectSystem.cs:2477-2482 in 1.6.0f1).
  - On the pivots and meshes of the build: FG 2.1245 - 1.956, DQ 2.7645 - 2.341, GD 3.8465 - 3.323 (rows at 7.3185 / 8.9831 / 10.6335).
  - The contract has one carrier row and one grip row per side.
- **Grips are not carriers.** A grip is never in CarrierGripTops. It has no census, no count, no hold row and no seeding of its own.
  A seeded body takes its rope point from the jaw of its grip when it has one (`RopePointOfGrip`: exact under any swing).
  DeleteAllRules skips the kind.
- **Enrollment**: a grip gets InterpolatedTransform + one Updated, exactly like a chair. Vanilla creates the grip without it. An entity
  CREATED with InterpolatedTransform never enters the static search tree. The bounds tags cover the grips (`CollectGripsToTag`).
- **Pose** (`CarrierPose.Pose`, pure, LiftBatch CP-1..CP-20):
  - Yaw = `NetUtils.GetNodeRotation(tangent, current)` (NetUtils.cs:316-324: y zeroed, the yaw of the rigid pose). Pitch = the slope of the
    rope tangent in the yaw frame.
  - The swing convention (2026-11-03): the clevis of a chair cannot roll. The single lateral pin of the tram cannot roll either. So the
    lateral swing belongs to the whole assembly. ONE world rotation Q_roll turns the assembly. Q_roll is the roll about the rope TANGENT through the
    rope point.
  - Grip rotation = Q_roll x yaw x RotateX(-pitch) = yaw x pitch x RotateZ(lateral). So the jaw stays along the rope under any roll
    (CP-18, MoverBench B-5: 0.1 deg). Body rotation = Q_roll x yaw x RotateX(-foreAft) (`BodySwing`).
  - Grip position = rope - R_grip x GripRope (the rope point sits in the jaws, roll or not). The pin = rope + R_grip x (GripPin - GripRope).
    Body position = pin - R_body x BodyPin.
  - The fore-aft turns the body alone about the pin. At zero swing the body is plumb under pitch. On a pitched span the inclined roll axis
    also yaws the body by about roll x sin(pitch) and leans it by about roll x cos(pitch).
  - Offsets are rotated in the yaw frame first. So at zero pitch and swing the body is exactly the rigid one-piece pose (CP-1, B-5: 1e-4 m).
    Zero roll is exactly the unrolled pose (CP-15, CP-16). The joint gap is 0 under any pitch, roll and fore-aft (CP-4, CP-18, B-5: 1 mm).
  - The first roll convention (yaw x roll x pitch about the horizontal travel axis) tilted the jaw off the rope by roll x sin(pitch).
    The tilt was 3.8 deg at the FG clamp on 30 deg.
  - Toggle off: no pitch, no swing.
- **Swing** (`CarrierPose.Swing`, the visible-sway round 2026-11-06, after an in-game check found the lean steady and no sway):
  - For each axis, the swing is ramp.x x clamp(LeanPerWind2 x G x |w| x w_component x gust, +-room) + ramp.y x A x G x (sin, cos)(`SwayArg`).
    G is the roll gain RollGain for roll and ForeAftShare for fore-aft. A = min(SwayPerWind x |w|, MaxSway) x (1 - SwayGustShare +
    SwayGustShare x gust). room = the clamp - A x G.
  - The code CLAMPS the lean BEFORE the ramp, because a saturating wind would otherwise jump at the ramp end (CP-9). It also clamps the
    lean to the room that the sway leaves. So lean + sway never reach the clamp (no flat tops). CP-14 keeps MaxSway x G at most half of
    every clamp.
  - `SwayArg` = omega / v x s + SeedPhase(entity index) + `SwayShift`. omega = sqrt(g / (0.7 x BodyPin.y)), a pendulum of 70 % of the
    pin height of the body. Values: FG 2.48 s, DQ 2.82 s, GD 3.32 s, tram 5.38 s.
  - **The stopped lift** (DECISIONS 619). Until then, a held loop froze in the middle of its sway. Now the argument is omega x (s / v +
    the sway clock) + the phase.
    - The sway clock (`CarrierWind.SwayTimeS`) is the mover time that the loop stood held. It runs only while the loop is held.
    - While the loop runs, s / v runs. While the loop is held, the sway clock runs. Both run at 1 s per s. So the argument advances
      at omega in both states, with no step at the stop or the restart. The Rebase keeps s continuous when the clock stops.
    - `SwayShift` = omega x the sway clock, wrapped in double once per loop and pass (`SwingInputs`, `JobLoop.SwayShift`).
    - A pause stops the mover clock. Then everything freezes, as vanilla's objects do.
  - The held amplitude: `CarrierPose.SwayEnvelope` scales the sway (not the lean).
    - After a stop it settles to HeldSwayShare 0.6 with a 4 s time constant. A pendulum loses the share of its swing that the motion gave.
    - After a restart it grows back to 1 with a 3 s time constant.
    - It is a closed form from its value at the last change (`CarrierWind.SetHeld`). It has no step, also when the power flaps.
    - CP-20: the FG roll swings 0.6 x the running peak to peak. The stop and the restart step at most 1e-4 deg. The frozen argument
      would step more than 0.1 deg. CP-21: the wrap, the envelope range and the scaled sway inside the clamps.
  - `CarrierWind.SetHeld(hold.Held, now)` runs for each loop on every hold poll (`PollHolds`). Without a change it does nothing. A
    re-freeze at a rebuild is no change, so the clocks run on.
- **Gust** (`CarrierPose.Gust`, `GustClock`): GustMean + GustAmp1 sin(phi1 - k1 d1.x) + GustAmp2 sin(phi2 - k2 d2.x) = 0.8 +- 0.12
  +- 0.08, in [0.6, 1.0]. The gust is two waves with periods of 7.3 s and 12.7 s. They travel over the ground at 12 m/s in two FIXED world
  directions. A direction taken from the wind would flip with a reversal and snap. x = the rope point.
  - Wavelengths are 88 m and 152 m. Neighbors lean nearly together (20 m apart: at most 0.17 apart, CP-20). A long line shows several gust
    patches.
  - Each carrier needs two sin calls. The clock phases phi = 2 pi t / T wrap in double once per loop and pass (`SwingInputs`,
    `JobLoop.GustClock`).
  - The gust's time t is the mover clock, also on a held loop. The code poses a held loop every pass now. So the 611 `CarrierWind.Resume`
    is gone (DECISIONS 619). It shifted the gust clock and the wind blend at a release to keep the frozen pose continuous.
  - Numbers at |w| 0.31 cross (sample of step 0, CP-19, 180 s):
    - FG / DQ / GD roll: 4.4 to 5.9 deg peak to peak per period (median 5.1 / 5.1 / 5.2).
    - Mean roll: 2.8 to 2.9 deg downwind.
    - The roll is upwind of plumb under 0.5 % of the time.
    - Tram roll 0.8 to 1.0 deg, tram fore-aft 1.6 to 2.0 deg.
- **CP-9, smoothness** (redefined 2026-11-06): the first version capped every swing step at 0.5 deg per 30 fps frame at 3x. That held the
  sway to 1 deg. (A visible pendulum moves 0.7 to 1 deg per 0.1 s of clock at 3x with no step.) Now, through guided -> span -> guided with a
  mid-span wind reversal, gusts and a saturating wind, these limits hold:
  - Every 3x frame step <= the analytic rate bound x 0.1 s + 0.01 deg. The bound is `RateBound` in CarrierPoseTests: the sum of the lean,
    ramp, gust, wind-blend and pendulum rate terms of both axes.
  - Every step at 1/480 s <= the bound x 1/480 s + 0.01 deg. A pop over about 0.05 deg fails. A planted 0.1 deg jump fails.
  - Every 1x frame (1/30 s) <= 0.5 deg.

  Measured worst: FG 0.97 / DQ 0.87 / GD 0.74 / tram 0.16 deg per 3x frame, 0.32 / 0.29 / 0.25 / 0.05 deg per 1x frame.
- **Roll limits per type** (`CarrierPose.Limits`, from `SwingInputs` into the `JobLoop.Limits` of the job). The lever of the roll is longer
  than the pin roll of the first convention (|BodyPin.xy|). The lever runs from the rope point to the base of the body. It is
  |RigidOffset.xy| on a level rope, times cos(pitch) on a span. So a chair / gondola scales its roll gain AND clamp by
  k = |BodyPin.xy| / |RigidOffset.xy|. Per unit wind, the base moves as far as the 8 deg pin roll moved it. At the clamp it moves no farther
  (2 asin(k sin 4 deg)). FG k 0.951, 7.61 deg. DQ k 0.920, 7.36 deg. GD k 0.898, 7.19 deg (CP-18: 0.308 / 0.395 / 0.554 m at the clamp).
  The tram: 2 deg, gain 1/6 = half its fore-aft amplitude. The 10 m hanger damps it (TramShuttleSystem). Fore-aft 3 deg and the 3.5 deg
  sway cap are shared. A strong wind reaches the clamp of each type (CP-5).
- **Guided segments** (a bull wheel arc or a synthesized terminal run, `Owner == Entity.Null`): no swing. `LiftLoopSampler.MarkRuns`
  stores, per flat segment, the free meters to the nearest guided segment behind and ahead (around the wrap, `NoGuidedM` when the loop has
  none). `CarrierPose.Ramp` brings the lean in over [0, R] and the sway over [R, 2R] after a carrier leaves a station, and out before the
  next station. R = max(10 m, 2 s x speed).
- **Wind** (`PollWind`): `WindSystem.GetMap(readOnly: true, out deps)` (CellMapSystem.cs:154-158), `deps.Complete()`, then
  `WindSystem.GetWind(loop box center, map)` (WindSystem.cs:68-80, the bilinear sample). The call is synchronous on the main thread, so it
  needs no AddReader.
  - The code samples the wind in three cases:
    - Every 512 render frames (WindSystem.kUpdateInterval, WindSystem.cs:45).
    - At every rebuild.
    - At once while the wind of a samplable loop is still unknown (a new loop, the first passes of a load).

    The code stamps the cadence only when it sampled something. Held loops are sampled too (they swing with the live wind, 619).
  - Each loop has a `CarrierWind` (CarrierPose.cs, kept per base station across rebuilds). It blends old to new over WindBlendS = 8 s
    (smoothstep). Its first sample is the start value when no pose read the unknown (zero) wind yet. Otherwise it blends in from that zero
    (CP-17: a late first sample would otherwise snap the lean by 1.65 deg in one frame).
  - Units and direction (velocity vs "from") UNVERIFIED: the field test reads the swing against the wind arrows.
  - A failure gives one "!!!" ERROR per load, and the carriers keep their last wind.
- **Job**: `MoveChairsJob` samples ONCE per carrier and writes two Transforms (body, then grip, each with the 1 mm / 0.99999 idempotence,
  `LiftLoopSampler.Write`). A non-split loop runs today's rigid kernel unchanged (MoverBench B-1 bit-equality). The job counts the grip
  results apart (Counts[4..7]). A missing grip dirties the loop cache like a missing chair. The grip writes are safe under
  NativeDisableParallelForRestriction because each grip is paired with exactly one body (the roster refuses a second pairing).
- **Managed paths**: held loops that do not swing, the A/B managed path and the static pin flow all use CarrierPose (`MoveCarrier`,
  `PinGripOf`). With the job off, a swinging held loop also takes the managed path. A grip that
  died under `MoveCarrier` dirties the loop cache like the job's grip Missing. The static flow (unclosed lifts, the load pass, Temp
  previews) poses a pitched grip. It takes the BODY from the same Pose call at zero swing (plumb, still, hung from the pin of the grip).
  The rigid rope - drop body would sit |GripPin - GripRope| x sin(pitch) off the pin. That is 10 cm on a 30 deg span (LiftBatch CP-15). A carrier
  that the static flow leaves at vanilla's placement (no cable lane, lane too far) gets its grip assembled on the body where it stands
  (`ParkGripOnBody` = `CarrierPose.GripOnBody`, CP-16).
- **Fallback hooks** (NOT switched on, one constant each in ChairPinSystem.Grips.cs):
  - `GripNearOnly`: the grips of a far loop keep their last pose while the bodies move on.
  - `GripHalfRate`: grips written every second pass.

  They are for the field test to flip if the cost is over budget.
- **Cost**: the `[cost] chair mover` line counts the grips apart (paired, written per pass, Updated tags, animated on / off, the
  fallbacks). MoverBench B-5 setup: 2,000 carriers, one thread, JIT, 2026-11-03, the roll about the rope tangent. Result: rigid p50 0.261 ms, split
  p50 0.481 ms (0.240 us per carrier). That is +0.44 ms on the x2 mid-range estimate, 0.06 ms under the 0.5 ms budget. The job spreads it
  over the workers (B-3b: ~5x). Budget (PIPELINE.md): 0.5 ms worker, 0.1 ms main thread.
  - The gust (2026-11-06) adds two sin calls per carrier and one GustClock per loop and pass on the main thread. The test compared base
    vs head, alternated, 3 rounds each on a loaded machine. Split p50 was 0.249 / 0.340 / 0.335 vs 0.370 / 0.354 / 0.358 us per carrier. The steady rounds
    show about +0.02 us, +0.04 ms per 2,000 carriers on one JIT thread.
  - The B-5 budget line is load-noisy. It failed on the base commit too (0.70 / 0.71 ms in 2 of 3 rounds). It read 0.46 to 0.58 ms when
    the machine was quieter.
