# ChairPinSystem.Mover: the Burst chair move pass, its threading rules and far-loop pacing

The per-frame chair move pass at release scale. Target: under 0.5 ms a frame on the main thread for 20 lifts / ~3,000 chairs, and no
cost that grows with the city. Engine citations are `File.cs:line` against the Game.dll decompile baseline 1.6.0f1.

## What runs when

- On a loop-cache rebuild (lift edits, load, the 4,096-frame watchdog): `BuildLoopCache` seeds every loop's pace (`LiftMoverMath.Seed`)
  and marks the flat arrays dirty; the next move pass flattens the loops once into persistent native arrays (`EnsureMoverArrays`:
  segments, chairs with offset / grip top / loop, a box per loop). A hold resume (Rebase) marks them dirty too (the offsets moved).
  O(chairs + segments), never per frame.
- Every moving pass (every rendered frame the render clock advanced; `RunMovePass`), main thread, O(loops) + the tags:
  1. Last frame's job is complete (OnUpdate's first line, `MoverCompleteJob`) and its result counts become the "last pass moved / in
     place / missing" numbers (a missing chair dirties the cache).
  2. Per loop: a held loop keeps the managed path (its frozen pose is written at the hold's entry or a rebuild only, a handful of
     frames per session); a running loop gets its camera distance (box), its stride (`LiftMoverMath.StrideFor`) and the pace decision
     (`LiftMoverMath.Decide`: write this pass? tag bounds?).
  3. The bounds tags of this pass (the loops that reached their refresh travel, plus held loops' entry tags) are added in one batched
     `EntityManager.AddComponent<Updated>`: a structural change, so it runs before the job is scheduled (a structural change completes
     every running job).
  4. `MoveChairsJob` is scheduled (IJobParallelFor, batch 64, Burst) over all rostered chairs; a chair of a loop not written this pass
     returns at once. `SumResultsJob` (IJob, Burst) folds the per-chair results into four ints.
- Worker threads: `MoveChairsJob`, per chair: alive (`EntityStorageInfoLookup.Exists`, no Deleted), `s = (offset + phase) % L` (phase =
  speed x TimeS computed on the main thread, PhaseOf's product: bit for bit the managed s), `LiftLoopSampler.Sample`
  (= `LiftLoopCache.SampleSegments`) and `LiftLoopSampler.Pose` (= MoveChair's pose and 1 mm idempotence), then a Transform write
  through `ComponentLookup<Transform>` only when the pose changed.

## Threading / ECS correctness

- The Transform write: `[NativeDisableParallelForRestriction] ComponentLookup<Transform>`. Safe because every index writes a different
  entity (a chair is rostered once: `BuildLoopCache` adds each chair to one loop) and nothing else in the job reads another chair's
  Transform. The stress mode never schedules two writers of one entity concurrently: its K passes are chained jobs (each depends on the
  previous).
- Dependency: the lookup is a RW declaration of Transform by this system, and the job handle is assigned to `Dependency`, so the
  Entities dependency manager makes every later reader / writer of Transform this frame wait for it (Modification5
  `Objects.SearchSystem` via `UpdateCollectSystem`, PreCulling, `ObjectInterpolateSystem.UpdateStaticAnimations`' copy into
  InterpolatedTransform). A later main-thread EntityManager read or write of Transform (`TramShuttleSystem`'s cabin pass, a structural
  change by a barrier) completes the job first (the main thread then waits for what is left of it).
- Timing: Modification4B. Vanilla's Modification4B systems and `ModificationBarrier4B` run after this system (`SystemOrder.cs:83,:91`);
  our job is done by the time PreCulling / Rendering read the Transforms.
- Our own native arrays are read by the job and written only on the main thread after `MoverCompleteJob`, never while the job can run;
  the dev tab and the dump read the managed loop cache, never these arrays.
- The lookups are refreshed (`Update(this)`) right before the schedule, after this pass's structural change.
- Held loops, Temp-chair previews, the static flow and `BuildLoopCache`'s roster stay managed (rare events).

## Far-loop pace (LiftMoverMath item 2)

A loop whose box is >= 500 m from the camera is written every k-th pass, k <= 8, such that a chair never steps more than 0.5 mrad on
screen; its bounds refresh every 2 m x k of its own travel; it is written (and its bounds refreshed when >= 2 m stale) on the very
pass it comes back near. Positions are a function of the clock, so a throttled loop is written at its exact current pose (no catch-up
error). Camera = `CameraUpdateSystem.position` (the active viewer's position, `CameraUpdateSystem.cs:164,:268`), which updates before
PreCulling (`SystemOrder.cs:631`): at Modification4B it is the previous frame's (one frame of lag, inside the 500 m margin). No
viewer -> every loop near (fail open). Distance only, no frustum test: a loop behind the camera but within 500 m runs at full rate
(cheap and safe).

## Dev tab ("Mover scale" foldout in ChairPinSystem.Debug.cs)

Burst job vs managed path (A/B), pace on / off, stress x1 / x4 / x16 (the move work repeated K times per pass: K - 1 extra writes at an
earlier clock, then the real pass; the Updated tags are not multiplied), sync-measure (the job is completed inside the timed span so
the `[cost]` numbers include it). The once-per-load "[cost] chair mover" line reports p50 / p95 / max main-thread ms over the first 600
moving passes.

## Grips and swing (the carrier grip round, ChairPinSystem.Grips.cs, CarrierPose.cs)

A split carrier is two vanilla sub-objects of one lift edge: the BODY (the rostered carrier, seat / cabin + hanger) and the GRIP (jaws /
carriage + the hanger head). The grip pitches with the rope; the body hangs plumb from the pin and swings in the wind. Engine citations
against the 1.6.2f1 decompile.

- **Split = the grip prefab resolved** (`SkiPrefabCatalogSystem.GripTypes`, catalog kind Grip, pending until the art import). A split
  type's carrier drop comes from the catalog pivots (`CarrierPivots.RigidDrop` = BodyPin.y + GripRope.y - GripPin.y), not from the
  body mesh's bounds (the body alone is shorter than the one-piece carrier); its virtual grip top = RigidDrop + GripTopAboveRopeM.
- **Pairing** (roster, `RosterGrip`): the grips are their own NetSubObjects rows on the line prefab, with the carrier row's EvenSpacing
  and spacing. Vanilla's EvenSpacing loop writes `Aligned.m_SubObjectIndex = m x rowCount + row` (Game.Objects/SubObjectSystem.cs:
  1742-1757; rowCount = the line prefab's `Game.Prefabs.SubObject` buffer length), and both rows get the same slot count
  `(int)(len / max(1, spacing) - 0.5f)`, so a body and its grip share (owner edge, slot m). The side (the lane) is the sign of the row's
  m_Position.x (`CarrierPose.RowSide`, +-1 cm dead zone). `IndexEdgeGrips` indexes an edge's grips once per pass; a missing grip leaves
  its body on the rigid pose. A line prefab with no grip rows (before the prefab surgery): one INFO per line prefab per load, all its
  carriers rigid. Contract breaks (one WARN per load, `LogGripContract`): two grips on one key keep the FIRST (the other is never
  posed and stays at its own spawn); a grip two bodies pair with stays with the first body (one writer per entity in the job; the
  static pass applies the same rule per pass), the second body keeps the rigid pose.
- **Prefab contract for the grip rows** (so vanilla's spawn pose is already the assembled carrier): each grip row copies its carrier
  row's flags (EvenSpacing), m_Position.x (the side), m_Position.z (the spacing: the slot count follows it) and rotation, and its
  m_Position.y = the carrier row's y + (BodyPin.y - GripPin.y), the rigid grip offset (FG 2.1509, DQ 2.7645, GD 3.8465
  on the build's pivots, build.md). One carrier row and one grip row per side.
- **Grips are not carriers**: never in CarrierGripTops, no census, no count, no hold row, no seeding of their own (a seeded body takes
  its rope point from its grip's jaw when it has one, `RopePointOfGrip`: exact under any swing). DeleteAllRules skips the kind.
- **Enrollment**: InterpolatedTransform + one Updated, exactly like a chair (vanilla creates the grip without it; an entity CREATED with
  InterpolatedTransform never enters the static search tree). The bounds tags cover the grips (`CollectGripsToTag`).
- **Pose** (`CarrierPose.Pose`, pure, LiftBatch CP-1..CP-18): yaw = `NetUtils.GetNodeRotation(tangent, current)` (NetUtils.cs:316-324:
  y zeroed, the rigid pose's yaw); pitch = the rope tangent's slope in the yaw frame. The swing convention (2026-11-03: a chair's
  clevis and the tram's single lateral pin cannot roll, so the lateral swing is the whole assembly's): ONE world rotation Q_roll = the
  roll about the rope TANGENT through the rope point turns the assembly. Grip rotation = Q_roll x yaw x RotateX(-pitch) = yaw x pitch x
  RotateZ(lateral), so the jaw stays along the rope under any roll (CP-18, MoverBench B-5: 0.1 deg); body rotation = Q_roll x yaw x
  RotateX(-foreAft) (`BodySwing`). Grip position = rope - R_grip x GripRope (the rope point sits in the jaws, roll or not); the pin =
  rope + R_grip x (GripPin - GripRope); body position = pin - R_body x BodyPin. The fore-aft turns the body alone about the pin; at
  zero swing the body is plumb under pitch. On a pitched span the inclined roll axis also yaws the body by about roll x sin(pitch) and
  leans it by about roll x cos(pitch). Offsets are rotated in the yaw frame first, so at zero pitch and swing the body is the rigid
  one-piece pose exactly (CP-1, B-5: 1e-4 m), and zero roll is the unrolled pose exactly (CP-15, CP-16); the joint gap is 0 under any
  pitch, roll and fore-aft (CP-4, CP-18, B-5: 1 mm). (The first roll convention, yaw x roll x pitch about the horizontal travel axis,
  tilted the jaw off the rope by roll x sin(pitch): 3.8 deg at the FG clamp on 30 deg.) Toggle off: no pitch, no swing.
- **Swing** (`CarrierPose.Swing`): lean = clamp(LeanPerWind2 x RollGain x |w| x w_cross) on the roll and LeanPerWind2 x ForeAftShare x
  |w| x w_along fore-aft, CLAMPED BEFORE the ramp (a saturating wind would otherwise jump at the ramp end, CP-9); sway = min(SwayPerWind
  x |w|, MaxSway) x (RollGain sin, ForeAftShare cos) of `SwayArg` = omega / v x s + SeedPhase(entity index), omega = sqrt(g / (0.7 x
  BodyPin.y)) (a pendulum of 70 % of the body's pin height). The sway argument is a function of the loop distance s, not of time: it
  is continuous across a pause, a hold, a Rebase and a far-loop stride (a frozen loop's carriers freeze mid-sway, never snap).
- **Roll limits per type** (`CarrierPose.Limits`, from `SwingInputs` into the job's `JobLoop.Limits`): the roll's lever (rope point to
  the body's base, |RigidOffset.xy| on a level rope; times cos(pitch) on a span) is longer than the first convention's pin roll
  (|BodyPin.xy|), so a chair / gondola scales its
  roll gain AND clamp by k = |BodyPin.xy| / |RigidOffset.xy|: the base moves as far per unit wind as the 8 deg pin roll moved it, and
  no farther at the clamp (2 asin(k sin 4 deg)): FG k 0.951, 7.61 deg; DQ k 0.920, 7.36 deg; GD k 0.898, 7.19 deg (CP-18: 0.308 /
  0.395 / 0.554 m at the clamp). The tram: 2 deg, gain 1/6 = half its fore-aft amplitude (the 10 m hanger damps it;
  TramShuttleSystem). Fore-aft 3 deg and the 1 deg sway cap are shared; each type's clamp is reached by a strong wind (CP-5).
- **Guided segments** (a bull wheel arc or a synthesized terminal run, `Owner == Entity.Null`): no swing. `LiftLoopSampler.MarkRuns`
  stores per flat segment the free meters to the nearest guided segment behind / ahead (around the wrap, `NoGuidedM` when the loop has
  none); `CarrierPose.Ramp` brings the lean in over [0, R] and the sway over [R, 2R] after leaving a station and out before the next,
  R = max(10 m, 2 s x speed).
- **Wind** (`PollWind`): `WindSystem.GetMap(readOnly: true, out deps)` (CellMapSystem.cs:154-158), `deps.Complete()`, then
  `WindSystem.GetWind(loop box center, map)` (WindSystem.cs:68-80, the bilinear sample). Synchronous on the main thread, so no
  AddReader. Every 512 render frames (WindSystem.kUpdateInterval, WindSystem.cs:45), at every rebuild, and at once while a samplable
  loop's wind is still unknown (a new loop, a load's first passes); the cadence is stamped only when something was sampled. Held loops
  keep their last wind. Per loop a `CarrierWind` (CarrierPose.cs, kept per base station across rebuilds) blends old to new over 4 s
  (smoothstep). Its first sample is the start value when no pose read the unknown (zero) wind yet, else it blends in from that zero
  (CP-17: a late first sample would otherwise snap the lean by 1.65 deg in one frame). Units
  and direction (velocity vs "from") UNVERIFIED: the field test reads the swing against the wind arrows. A failure: one "!!!" ERROR per
  load, the carriers keep their last wind.
- **Job**: `MoveChairsJob` samples ONCE per carrier and writes two Transforms (body, then grip, each with the 1 mm / 0.99999
  idempotence, `LiftLoopSampler.Write`); a non-split loop runs today's rigid kernel unchanged (MoverBench B-1 bit-equality). The grip
  results are counted apart (Counts[4..7]); a missing grip dirties the loop cache like a missing chair. The grip writes are safe
  under NativeDisableParallelForRestriction because each grip is paired with exactly one body (the roster refuses a second pairing).
- **Managed paths**: held loops, the A/B managed path and the static pin flow all use CarrierPose (`MoveCarrier`, `PinGripOf`); a grip
  that died under `MoveCarrier` dirties the loop cache like the job's grip Missing. The static flow (unclosed lifts, the load pass,
  Temp previews) poses a pitched grip and takes the BODY from the same Pose call at zero swing (plumb, still, hung from the grip's pin:
  the rigid rope - drop body would sit |GripPin - GripRope| x sin(pitch) off the pin, 10 cm on a 30 deg span, LiftBatch CP-15). A
  carrier the static flow leaves at vanilla's placement (no cable lane, lane too far) gets its grip assembled on the body where it
  stands (`ParkGripOnBody` = `CarrierPose.GripOnBody`, CP-16).
- **Fallback hooks** (NOT switched on; one constant each in ChairPinSystem.Grips.cs): `GripNearOnly` (a far loop's grips keep their
  last pose while the bodies move on) and `GripHalfRate` (grips written every second pass). For the field test to flip if the cost is
  over budget.
- **Cost**: the `[cost] chair mover` line counts the grips apart (paired, written per pass, Updated tags, animated on / off, the
  fallbacks). MoverBench B-5 (2,000 carriers, one thread, JIT, 2026-11-03, the roll about the rope tangent): rigid p50 0.261 ms, split p50 0.481 ms
  (0.240 us per carrier; +0.44 ms on the x2 mid-range estimate, 0.06 ms under the 0.5 ms budget); the job spreads it over the workers (B-3b: ~5x). Budget (PIPELINE.md): 0.5 ms worker, 0.1 ms main thread.
