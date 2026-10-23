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
