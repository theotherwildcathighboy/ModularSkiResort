# SkiSnowInjectionSystem: how run-shaped snow is injected into the game's own snow simulation buffer

Layer 1 of the run-rendering stack: writes run-shaped snow into the game's own snow simulation buffer, so runs render as native
terrain snow (same triplanar shading, sparkle, altitude/aspect-correct melt, wind drift, save/load) with the mod contributing nothing
but periodic depth top-ups. Buffer facts are against the decompile (`Game.Simulation\SnowSystem.cs`, Game.dll 1.6.0f1).

## Buffer facts and the write path

- Storage: two ping-pong 1024x1024 R16G16_UNorm RenderTextures, enableRandomWrite, bilinear/clamp (CreateTexture, :385-395). R = snow
  depth normalized 0..1 -> 0..8 m (kSnowHeightScale = 8f, :131; SnowScaleVector = (8,1,1,1), :197). G = rain/water accumulation,
  preserved bit-exactly. UV == playable-area UV, ~14 m/texel (TerrainSystem.playableOffset/playableArea, TerrainSystem.cs:2502-2508,
  :3388).
- Injection point: `public RenderTexture SnowDepth => m_SnowHeights[Read]` (:212-222), the buffer bound as _SnowMap right now and
  consumed as _Previous by the next AddSnow (:474), which copies it forward. A write between snow ticks is instantly visible and
  permanently adopted by the simulation (melts, drifts, saves like natural snow).
- Write path: an own compute kernel would need a shipped compiled shader, which this mod cannot carry, so it uses only game objects:
  blocking AsyncGPUReadback of SnowDepth (the game's own Serialize pattern, :244-266) -> CPU merge under the run mask ->
  ComputeBuffer.SetData -> dispatch the vanilla snowUpdate shader's LoadR16G16_UNorm kernel into SnowDepth (the game's own Deserialize
  pattern, :323-327; shader reachable via `AssetDatabase.global.resources.shaders.snowUpdate`, :233). Read, merge and upload happen in
  one frame, so no natural accumulation is ever reverted.
- Semantics: ramped approach under the run mask. r moves toward target x coverage by a noisy per-texel step, never past it and never
  down, so a thermal gate opening reads as snow guns building base patch by patch instead of a light switch. Monotone between top-ups,
  a structural no-op wherever natural snow already exceeds the target (deep winter = zero raised texels). Never additive; the G
  channel is untouched.
- Scoped managed melt: when a run's gate is off (too warm to make snow), texels inside our own mask decay by `Tuning.DecayPerTopupM`
  per top-up. Buffer melt at run altitude measures ~zero, so without this injected snow is immortal (it sat unchanged through July
  while the seasonal snow line retreated past it). Decay never touches snow outside run footprints.

## Injection policy

- Snowmaking runs (SkiRunSnowmakingA): target ~0.35 m whenever the local temperature at the run's mean elevation (SnowLineSystem
  lapse) is at or below freezing + `SnowLineSystem.GetSnowmakingMarginC()` (Options > Snow "Snowmaking margin", default 2 C), read once
  per evaluated pass. The estimate and, through the mask, the bill read the same value, so gameplay, injection and visuals share one
  model.
- Groomed runs (SkiRunGroomedA): target ~0.08 m where locally at/below freezing + GroomedMarginC (an early/late-season grooming
  assist; in summer groomed runs are bare).
- Both gates evaluate at each texel's own elevation inside the fold, so the snow line crosses a run (top white, bottom bare, the
  boundary creeping with the season). Too-warm texels route to the managed-decay mask instead.

## Scheduling and pause

GameSimulation phase, UpdateAfter SnowSystem (the write lands after the frame's buffer flip). GetUpdateInterval mirrors SnowSystem's 4
so the inherited offset puts every invocation right behind a snow tick; the 64-frame top-up cadence is self-gated off
SimulationSystem.frameIndex, plus one immediate top-up on the first sim tick after load. Pause needs no special code: SnowSystem gates
purely by phase (GameSimulation runs only while the sim advances and only in Game action mode, SimulationSystem.cs:284-287) and we
mirror that. The Editor is excluded twice: the phase never runs there and activation is hard-gated to GameMode.Game, so map authors
never bake our snow into a map.

## Logs and failure mode

An ARMED line per load with every tuning constant; flood-prone lines go through LogWindow (flushed before an idle line, a load and at
quit). The buffer guard WARNs once and disables for the load if a game patch drifts the contract (failure mode is "no injected snow",
never corruption). Defensive contract (ChairPinSystem precedent): never throw into the game loop; top-level try/catch per update and
per load hook, one-shot "!!!" error log, disabled for the current load only, re-armed at OnGameLoadingComplete.

## Per-run snow measurement

Every evaluated pass measures, for every bound run of all four types, the share of its footprint with skiable snow
(`RunSnowRules.IsSkiable`: >= 2 cm, or the texel's own fringe target) and its mean depth, from the newest snapshot of the snow map
valid this load (m_Readback of the previous real pass, or the measure-only copy; ChooseSnapshot). Injected runs are measured inside the
fold (`FoldRowsJob.RouteTexel`), so the look cannot diverge from the measurement. Natural runs (SkiRunNaturalA) are rasterized for
measurement only, on a second chain after the injected one (`MeasureRowsJob`): never injected, decayed or billed, never in the mask,
decay or ramp planes, never in MeanRunSnowDepthM or the per-run active-texel map; texels an injected run manages this pass are
excluded from them.

- Cheaper top-ups: a dry skip (no readback, merge or upload) when no run texel is cold enough, the last real pass found the whole
  footprint warm and bare, and the footprint is the same (`RunSnowRules.CanSkipDry`), with a forced real pass every 1,024 sim frames;
  at most one evaluated pass per 0.5 s of wall clock (the first after a load exempt), with the merge's ramp and decay steps scaled by
  the frames since the last evaluated pass (`RunSnowRules.StepScale`, exactly 1 at 64 frames: bit-identical at 1x and 2x, cap x4).

## Packed skip beside the dry skip

`RunSnowRules.SkipKind`: dry first, then packed. A pass skips readback, merge and upload when no run texel is cold now, the last real
pass had no cold texel, raised nothing, lowered nothing and left the pack guard on for the next merge, the footprint is the same, the
runs held at least `RunSnowRules.PackedSkipMinRunDepthM` (0.10 m, the dress guard), every measured run's snow share is >= 70 % or
< 30 % (the share guard; moot with gating off), and the forced real pass (1,024 sim frames) is not due.

- Why such a pass changes no texel: with no cold texel the mask is empty (FoldRowsJob writes it only for a cold texel), so
  MergeRowsJob's raise branch never fires (target 0); the pack guard closes its decay branch; and the guard's input
  m_LastGlobalMeanDepthM is written only by a real pass, so a skipped pass cannot change it. One definition, `RunSnowRules.PackGuard`,
  is read by the merge and by the predicate. The upload already ran only when raised + lowered > 0, so no vanilla write is added or
  removed: the skip skips a read (the readback).
- What a skipped pass still does: builds the mask (cold test, footprint, per-run measurement), publishes the active texels, refreshes
  the run table, counts and logs, so a gun restart or a run edit ends the skip on that same pass. Stale, each less than 1,024 sim
  frames (5.6 game minutes: kTicksPerDay 262,144, TimeSystem.cs:18) older than a real pass would leave them: the snapshot (run shares,
  depths), the map mean and the run mean. The game melts and accumulates snow on its own every 4 sim frames
  (SnowSystem.GetUpdateInterval 4, SnowSystem.cs:226-229; AddSnow with _MeltMultiplier :455-481): that thaw is what a packed spell
  waits on. The share guard keeps every published label / multiplier change on a real pass (they move only between 40 % and 60 %); the
  dress guard keeps every visible dress change on one (prop snow saturates at SkiRunSurfaceSystem FullWhiteDepthM 0.05 m, the decal
  season flips at 0.02 / 0.005 m). The snapshot is frozen during a spell, so the share guard can only trip on the pass after a real
  (forced) pass. A label change the frozen snapshot held back is published on that same pass: its `Snow [open]` line is offered in
  step 6, just before the step-11 "share is near the open line" end line. A failed readback returns before the record, so a spell may
  continue on the older record until the forced pass: safe, that record still proves no write is owed.

## Guns stop and restart

A snowmaking or night run's cold texels are raised only while its guns run (`SnowGunPlumeRules.NextGunState`: stop when the run's pack
reaches `RunSnowRules.GunStopDepthM`, natural snow that deep included; restart below GunStopDepthM - GunRestartBandM, i.e.
0.96 / 0.91 m). The gun ramp's ceiling is GunStopDepthM (`Tuning.SnowmakingTargetDepthM`).

- The pack is measured in the fold (FoldRowsJob, Measure only) over the run's cold texels with a target: sum min(depth, own target) /
  sum own target. The state steps after the reduction (StepGuns) and gates the next pass's fold (`FoldRowsJob.GunGate`): a stopped run
  writes no mask target (no raise, no active texel, so no bill: the upkeep bills the mask) and marks its cold texels StoppedMark (-1)
  on the ramp plane instead, which the merge counts as run texels (the dress depth is unchanged) and the natural measurement excludes;
  a pass with stopped texels is never idle (the snapshot the restart reads stays fresh: one readback per window).
- `TryGetRunGunsRunning` (state Running in this pass's fold and a cold texel) is the read for SkiSnowGunPlumeSystem.
- Unknown (no measurement yet this load) does not raise: a load in deep winter never flashes the guns on. Groomed runs have no guns.
  Warm-spell protection is untouched (warm texels are never "running").

## Near OnDestroy

The wait before disposing the array a measure-only request writes into mirrors vanilla (BaseDataReader.Dispose,
BaseDataReader.cs:148-153; WaterRenderSystem.ReleaseShader, :291-296). A "Game terminated due to timeout" line at quit is not from this
wait: GameManager's quit (Game.SceneFlow/GameManager.cs:779-819) runs m_ModManager.Dispose (:792), then DestroyWorld (:793 ->
World.DisposeAllWorlds :2416, which runs this OnDestroy synchronously), and only then starts the two timed awaits, DisposePlatforms
(:795, :1679) and TaskManager.CompleteAndClear (:802), 10 s each (AsyncHelpers.AwaitWithTimeout starts its Task.Delay when called,
AsyncHelpers.cs:8-12); the line at :819 means one of those two did not finish. Nothing of ours runs in them (no TaskManager task, no
async code in the mod). Same code in 1.6.2f1 (quit block :789-829, DisposePlatforms :1694, DisposeAllWorlds :2431).

The wait happens only with a measure-only request in flight, and DisableCameraRendering + WaitXFrames(30) (:779, :782) come before it,
so the request has normally landed by then. It stays (disposing an array a readback still writes is the real hazard); it writes one
line when it actually waited, so a slow quit is attributable (the logger lives until :805).

## Near Topup

The blocking readback is deliberate: the merge writes the full texture back (the vanilla Load kernel has no sub-rect parameters), so a
stale async snapshot would revert natural snowfall map-wide on upload. Vanilla's rolling-async mirror pattern (water/terrain) is
read-only and does not apply. Same-frame atomicity holds: GameSimulation systems run sequentially on the main thread, SnowSystem is the
sole gameplay-time writer, and same-queue GPU ordering covers the rest. The per-phase ms split in the top-up log line is the
instrument for whatever cost remains. Every per-texel loop is a row-parallel Burst job; the managed code is orchestration.

Pass flow (the "step N" comments in the method refer to these): 0 every evaluated pass counts toward the Snow [day] line; 1 playable
check (starts the SnowSettled clock), buffer contract once; 2 snapshot buffers, poll a measure-only request; 3 ambient, freezing and
the margin, settings changes; 4 choose the snapshot, build the mask (it measures every bound run as it goes); 5 publish the active
texels (before any early-out); 6 refresh the per-run table; 7-9 the idle exits (a natural-only city requests a measure-only
readback); 10 the dry or packed skip (`RunSnowRules.SkipKind`); 11 end a skip / measure-only episode that no longer holds; 12 the
blocking readback; 13 the merge with the step scale; 14 the skip record (also keeps Lowered, the next merge's pack guard and the run
mean); 15 a forced check ends its episode when its new record would not skip the same way; 16 upload and the top-up line.
