# SkiSnowInjectionSystem: how run-shaped snow is injected into the game's own snow simulation buffer

Layer 1 of the run-rendering stack. It writes run-shaped snow into the game's own snow simulation buffer. So runs render as native
terrain snow (same triplanar shading, sparkle, altitude/aspect-correct melt, wind drift, save/load). The mod contributes nothing
but periodic depth top-ups. The buffer facts are against the decompile (`Game.Simulation\SnowSystem.cs`, Game.dll 1.6.0f1).

## Buffer facts and the write path

- Storage: two ping-pong 1024x1024 R16G16_UNorm RenderTextures, enableRandomWrite, bilinear/clamp (CreateTexture, :385-395). R = snow
  depth normalized 0..1 -> 0..8 m (kSnowHeightScale = 8f, :131, and SnowScaleVector = (8,1,1,1), :197). G = rain/water
  accumulation, preserved bit-exactly. UV == playable-area UV, ~14 m/texel (TerrainSystem.playableOffset/playableArea,
  TerrainSystem.cs:2502-2508, :3388).
- Injection point: `public RenderTexture SnowDepth => m_SnowHeights[Read]` (:212-222). This is the buffer bound as _SnowMap right
  now. The next AddSnow consumes it as _Previous (:474) and copies it forward. A write between snow ticks is instantly visible. The
  simulation permanently adopts it (it melts, drifts and saves like natural snow).
- Write path: an own compute kernel would need a shipped compiled shader, which this mod cannot carry. So the mod uses only game
  objects. The steps are:
  1. Blocking AsyncGPUReadback of SnowDepth (the game's own Serialize pattern, :244-266).
  2. CPU merge under the run mask.
  3. ComputeBuffer.SetData.
  4. Dispatch the vanilla snowUpdate shader's LoadR16G16_UNorm kernel into SnowDepth (the game's own Deserialize pattern, :323-327).
     The shader is reachable via `AssetDatabase.global.resources.shaders.snowUpdate`, :233.

  Read, merge and upload happen in one frame, so no natural accumulation is ever reverted.
- Semantics: ramped approach under the run mask. r moves toward target x coverage by a noisy per-texel step. It never passes the
  target and never moves down. So a thermal gate opening reads as snow guns building base patch by patch, not as a light switch. It
  is monotone between top-ups. It is a structural no-op wherever natural snow already exceeds the target (deep winter = zero raised
  texels). It is never additive. The G channel is untouched.
- No managed melt (T222, DECISIONS 667): the game's own shader melts a run texel like any natural snow. Before T222 the mod decayed
  warm run texels by a fixed step (the 2026-10 "immortal snow" fix) and held them for a budget (warm-spell protection). The round 2
  melt probe measured the game's melt on a written patch (`RunSnowRules.MeltPerHourPerC`), so neither is needed. Made snow lasts
  longer only because the guns build more of it (the 2.0 m base). The raise is `RunSnowRules.PackRaiseTexelM`, the one pack step
  (`PackStep`, applyMelt false) a cat track cell also uses.

## Injection policy

- Snowmaking runs (SkiRunSnowmakingA): target ~0.35 m. The target applies whenever the local temperature at the run's mean elevation
  (SnowLineSystem lapse) is at or below freezing + `SnowLineSystem.GetSnowmakingMarginC()`. The margin is Options > Snow
  "Snowmaking margin" (default 2 C). The system reads it once per evaluated pass. The estimate and, through the mask, the bill read the same value. So gameplay, injection
  and visuals share one model.
- Groomed runs (SkiRunGroomedA): target ~0.08 m where locally at/below freezing + GroomedMarginC (an early/late-season grooming
  assist. In summer groomed runs are bare).
- Both gates evaluate at each texel's own elevation inside the fold. So the snow line crosses a run (top white, bottom bare, the
  boundary creeping with the season). Too-warm texels are marked on the warm plane (`WarmMask`, a run-texel marker for the dress
  depth and the natural measurement) and left to the game's melt.

## Scheduling and pause

GameSimulation phase, UpdateAfter SnowSystem (the write lands after the frame's buffer flip). GetUpdateInterval mirrors SnowSystem's
4, so the inherited offset puts every invocation right behind a snow tick. The 64-frame top-up cadence is self-gated off
SimulationSystem.frameIndex, plus one immediate top-up on the first sim tick after load. Pause needs no special code. SnowSystem
gates purely by phase (GameSimulation runs only while the sim advances and only in Game action mode, SimulationSystem.cs:284-287),
and we mirror that. The Editor is excluded twice: the phase never runs there, and activation is hard-gated to GameMode.Game. So map
authors never bake our snow into a map.

## Logs and failure mode

An ARMED line per load with every tuning constant. Flood-prone lines go through LogWindow (flushed before an idle line, a load and at
quit). The buffer guard WARNs once and disables for the load if a game patch drifts the contract. The failure mode is "no injected
snow", never corruption. Defensive contract (ChairPinSystem precedent):

- Never throw into the game loop.
- Use a top-level try/catch per update and per load hook.
- Write a one-shot "!!!" error log.
- Disable for the current load only.
- Re-arm at OnGameLoadingComplete.

## Per-run snow measurement

Every evaluated pass measures these values for every bound run of all four types:
- The share of its footprint with skiable snow (`RunSnowRules.IsSkiable`). A closed run counts at the open bar, 3 ft = 0.91 m. An open run counts at the keep bar, 1 ft. Fringe texels are left out of the share and the mean (`RunSnowRules.IsFringeTarget`). A fringe texel has its own target under the bar: the feathered edge, at most `FringeMaxDepthM`. Before T183 they counted at their own target, and a narrow run opened on a few mm.
- Its mean depth.

It measures from the newest snapshot of the
snow map valid this load (m_Readback of the previous real pass, or the measure-only copy: ChooseSnapshot). Injected runs are measured
inside the fold (`FoldRowsJob.RouteTexel`), so the look cannot diverge from the measurement. Natural runs (SkiRunNaturalA) are
rasterized for measurement only, on a second chain after the injected one (`MeasureRowsJob`). The system never injects, decays or
bills them. They are never in the mask, decay or ramp planes. They are never in MeanRunSnowDepthM or the per-run active-texel map.
Texels an injected run manages this pass are excluded from them.

The inert path ("all runs inert, too warm": no mask or stopped texel) runs no real pass. It requests the
measure-only readback for the natural AND the bound injected runs (T183, post-release round 1). Before, it asked only for natural runs.
A city with no natural run then lost its snapshot after MaxSnapshotAgeFrames. Every run fell to the day-mean estimate (cold air at
the top, no snow on the map). Runs read open with no visible snow.

Cat tracks (T220, 1.0.1). The system measures a cat track like a natural run: it never injects snow on it (DECISIONS 551). Its guns'
snow is a model, `RunSnowRules.TrackMadeSnow`. The model has a share of the vertical and a pack depth:
- The guns follow the air at the track now, by elevation, as a run texel's guns do. The gun share and the cold share use the
  current air (`GetLocalTemperatureCoefficients`), not the day mean. With the day mean, tracks kept their guns in a 15 C spell
  (bridge pass r1c, "day mean 13.3 C").
- T222 (DECISIONS 667): the made snow lives in `RunSnowRules.TrackCells` (8) cells down the vertical, cell 0 at the top, each one a
  texel the mod owns. Every refresh, `StepTrackPack` melts each cell at its own air (`PackMeltPerHourM`, the measured rate the game
  melts a run texel with), steps the gun state on the pack of the cold cells (`NextPackGun`: stop at 2.0 m, restart below 1.95 m,
  the measured natural snow under the track as a floor), then builds each cold cell with the run's ramp (`PackStep`, the mean noise
  rate). So a track and a run over the same vertical open, close and make snow alike. No hold: a warm track melts as a run does.
- The made share counts at the run bars (`TrackMadeCountedShare`: the share of cells at the bar). An open track counts it at 1 ft.
  A closed track counts it at 3 ft. A track with no known state (Unknown) also counts it at 3 ft. A run with no known state counts
  at 1 ft (`MeasuredShare`). So the track opens about 0.6 game h after its guns start at full chill, not at once.
- The track's share is the larger of the snow under it and the counted made share.
- Its mean depth (hub, `Snow [open]` line) is the made pack on the made share and the measured mean on the rest
  (`TrackMeanDepthM`). Before, the line printed NaN.
- A pass that carries the measurement over starts from the snow under the track (`RunSnow.SnowShare`, `SnowMeanDepthM`), not
  from last pass's share with the made snow.
- The save holds three values. SkiTrackSnow v1 holds the share (cells holding made snow / 8). SkiTrackPack v2 holds their mean depth
  in millimeters (clamped at 8 m from T222, 0.96 m before: the i32 needs no layout change) and the open state. The save is lossy
  (DECISIONS 667): a load puts the mean depth on the top round(share x 8) cells. SkiTrackPack is new in 1.0.1. Its v2 added the state
  before any release carried the type.
- A track without a table entry starts from its saved state (`SavedTrackState`, `TrackStateFromSave`). This applies to its first
  refresh of a load and to `GetRunSnow` before that refresh. `QueueTrackSave` writes the values after the label is set.
- A save without SkiTrackPack (1.0.0) loads a saved share as a full 0.96 m pack (`TrackFromSave`, `LegacyTrackStopDepthM`). It clears both bars, so the
  track reloads as in 1.0.0. A v1 value (unreleased test saves) has no state. It reads as Unknown and counts at 3 ft.
- History: before SkiTrackPack, 32 tracks reloaded Open on a 0.12 m pack. The save was made just after gun start (bridge pass
  r1c, loadcheck d). With v1, Unknown counted at 1 ft. A resave held every track Closed on 0.352 m. After the load, 44 of 45 tracks
  read Open at 24-37 cm. 39 groomers came 7 s after the load (bridge pass r1d, loadcheck d).
- While the injection runs, these read the model, not the day mean (bridge pass r1d):
  - The hub's gun line: `TryGetCatTrackModel`, `HubRules.TrackGunWhyOf` (contract 49).
  - The bill and the stats' cost row: `CatTrackGunsCanRun` reads `TrackMadeSnow.GunReach` (the share of cold cells with the
    switch on): grooming and lights. Snowmaking bills the plan area x `CatTrackGunsMakingShare` (`TrackMadeSnow.MakingShare`, the
    cells the guns make snow on now), as a run bills the texels its guns raise (T222).
  - The plumes and the hub's "Making": `TrackGunsBuilding` (MakingShare > 0). Idle at the base or too warm: no plume.
  With the injection off, all three use the day-mean estimate, the rule the track's open state then follows. On Oct 1 (10.5 C
  air, 14.3 C day mean) the day-mean rule billed nothing and showed no plume while the guns built.
- T223: the gun state is not saved. The first refresh of a load (dt 0) decides it from the saved depth alone (`PackGunAfterLoad`
  rule): a pack at the base or in the band reads Stopped (no plume flash, no bill), one under 1.95 m reads Running (the bill and the
  plumes from the first pass). Before T222 the runtime gun work was lost on a load (no bill under the stop depth) and a full 960 mm
  pack reloaded as 0.95999998 m, under the stop depth (a plume flash).
- An estimate never opens a track (`TrackEstimatedSnowShare`): it keeps an Open track open, as a NaN share does. Before, an
  estimated track opened on "0.0 texel(s), mean NaN m" at a 14.5 C day mean (bridge pass r1c). While the injection runs,
  `GetRunSnow` gives a track not yet in the table its saved made snow only.

- Cheaper top-ups work in two ways:
  - A dry skip (no readback, merge or upload) happens when all of these hold:
    - No run texel is cold enough.
    - The last real pass found the whole footprint warm and bare.
    - The footprint is the same (`RunSnowRules.CanSkipDry`).

    A forced real pass runs every 1,024 sim frames.
  - At most one evaluated pass per 0.5 s of wall clock (the first after a load is exempt). The merge's ramp step scales
    with the frames since the last evaluated pass. The scale is `RunSnowRules.StepScale`: exactly 1 at 64 frames (bit-identical at 1x
    and 2x), cap x4.

## Packed skip beside the dry skip

`RunSnowRules.SkipKind`: dry first, then packed. A pass skips readback, merge and upload when all of these hold:

- No run texel is cold now.
- The last real pass had no cold texel and raised nothing.
- The footprint is the same.
- The runs held at least `RunSnowRules.PackedSkipMinRunDepthM` (0.10 m, the dress guard).
- Every measured run's snow share is >= 70 % or < 30 % (the share guard, moot with gating off).
- The forced real pass (1,024 sim frames) is not due.

- Why such a pass changes no texel: with no cold texel the mask is empty (FoldRowsJob writes it only for a cold texel). So
  MergeRowsJob's raise branch never fires (target 0). Since T222 the merge never lowers (no managed melt, no pack guard). The upload
  runs only when a texel was raised, so no vanilla write is added or removed. The skip skips a read (the readback).
- What a skipped pass still does:
  - It builds the mask (cold test, footprint, per-run measurement).
  - It publishes the active texels.
  - It refreshes the run table.
  - It counts and logs.

  So a gun restart or a run edit ends the skip on that same pass. These stay stale: the
  snapshot (run shares, depths), the map mean and the run mean. Each is less than 1,024 sim frames (5.6 game minutes: kTicksPerDay
  262,144, TimeSystem.cs:18) older than a real pass would leave them. The game melts and accumulates snow on its own every 4 sim
  frames (SnowSystem.GetUpdateInterval 4, SnowSystem.cs:226-229. AddSnow with _MeltMultiplier :455-481). That thaw is what a packed
  spell waits on. The share guard keeps every published label / multiplier change on a real pass (they move only between 40 % and
  60 %). The dress guard keeps every visible dress change on a real pass. Prop snow saturates at SkiRunSurfaceSystem FullWhiteDepthM
  0.05 m, and the decal season flips at 0.02 / 0.005 m. The snapshot is frozen during a spell, so the share guard can only trip on
  the pass after a real (forced) pass. A label change the frozen snapshot held back is published on that same pass. Its `Snow [open]`
  line is offered in step 6, just before the step-11 "share is near the open line" end line. A failed readback returns before the
  record, so a spell may continue on the older record until the forced pass. This is safe, because that record still proves no write
  is owed.

## Guns stop and restart

A snowmaking or night run's cold texels are raised only while its guns run. `SnowGunPlumeRules.NextGunState` stops the guns when the
run's pack reaches `RunSnowRules.GunStopDepthM` (natural snow that deep included). It restarts them below GunStopDepthM -
GunRestartBandM, i.e. 2.0 / 1.95 m (T222; 0.96 / 0.91 m before). The gun ramp's ceiling is GunStopDepthM (`Tuning.SnowmakingTargetDepthM`).

- The fold measures the pack (FoldRowsJob, Measure only) over the run's cold texels with a target: sum min(depth, own target) / sum
  own target. The state steps after the reduction (StepGuns) and gates the next pass's fold (`FoldRowsJob.GunGate`). A stopped run
  writes no mask target (no raise, no active texel, so no bill, because the upkeep bills the mask). It marks its cold texels
  StoppedMark (-1) on the ramp plane instead. The merge counts these as run texels (the dress depth is unchanged), and the natural
  measurement excludes them. A pass with stopped texels is never idle (the snapshot the restart reads stays fresh: one readback per
  window).
- `TryGetRunGunsRunning` (state Running in this pass's fold and a cold texel) is the read for SkiSnowGunPlumeSystem.
- Unknown (no measurement yet this load) does not raise. A load in deep winter never flashes the guns on. Groomed runs have no guns.
  Warm texels are never "running".

## Near OnDestroy

The wait before disposing the array a measure-only request writes into mirrors vanilla (BaseDataReader.Dispose,
BaseDataReader.cs:148-153. WaterRenderSystem.ReleaseShader, :291-296). A "Game terminated due to timeout" line at quit is not from
this wait. GameManager's quit (Game.SceneFlow/GameManager.cs:779-819) runs these steps in order:

1. m_ModManager.Dispose (:792).
2. DestroyWorld (:793 -> World.DisposeAllWorlds :2416, which runs this OnDestroy synchronously).
3. Only then, the two timed awaits, 10 s each: DisposePlatforms (:795, :1679) and TaskManager.CompleteAndClear (:802).
   AsyncHelpers.AwaitWithTimeout starts its Task.Delay when called (AsyncHelpers.cs:8-12).

The line at :819 means one of those two did not finish. Nothing of ours runs in them (no TaskManager task, no async code in the mod).
Same code in 1.6.2f1 (quit block :789-829, DisposePlatforms :1694, DisposeAllWorlds :2431).

The wait happens only with a measure-only request in flight. DisableCameraRendering + WaitXFrames(30) (:779, :782) come before it, so
the request has normally landed by then. The wait stays, because disposing an array a readback still writes is the real hazard. It
writes one line when it actually waited, so a slow quit is attributable (the logger lives until :805).

## Near Topup

The blocking readback is deliberate. The merge writes the full texture back, because the vanilla Load kernel has no sub-rect parameters. So
a stale async snapshot would revert natural snowfall map-wide on upload. Vanilla's rolling-async mirror pattern (water/terrain) is
read-only and does not apply. Same-frame atomicity holds: GameSimulation systems run sequentially on the main thread, SnowSystem is
the sole gameplay-time writer, and same-queue GPU ordering covers the rest. The per-phase ms split in the top-up log line is the
instrument for whatever cost remains. Every per-texel loop is a row-parallel Burst job. The managed code is orchestration.

Pass flow (the "step N" comments in the method refer to these):

- Step 0: every evaluated pass counts toward the Snow [day] line.
- Step 1: playable check (starts the SnowSettled clock), buffer contract once.
- Step 2: snapshot buffers, poll a measure-only request.
- Step 3: ambient, freezing and the margin, settings changes.
- Step 4: choose the snapshot, build the mask (it measures every bound run as it goes).
- Step 5: publish the active texels (before any early-out).
- Step 6: refresh the per-run table.
- Steps 7-9: the idle exits (a natural-only city requests a measure-only readback).
- Step 10: the dry or packed skip (`RunSnowRules.SkipKind`).
- Step 11: end a skip / measure-only episode that no longer holds.
- Step 12: the blocking readback.
- Step 13: the merge with the step scale.
- Step 14: the skip record (also keeps Lowered, the next merge's pack guard and the run mean).
- Step 15: a forced check ends its episode when its new record would not skip the same way.
- Step 16: upload and the top-up line.
