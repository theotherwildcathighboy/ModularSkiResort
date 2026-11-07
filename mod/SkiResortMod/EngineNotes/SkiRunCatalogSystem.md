# SkiRunCatalogSystem: the run list, the always-on rating service, its cadences and the rating cache

`SkiRunCatalogSystem` is the Ski Resort hub's in-memory list of our ski runs plus the resort totals. It is also the always-on rating service.
The simulation scores runs by these ratings, so they exist with the hub closed. `SkiResortHubUISystem`, `SkiResortStatsSystem`
and `SkiResortAttractivenessSystem` read it. It owns no binding and writes nothing to the world.

## Two cadences, one service

The same Rebuild, RateStep, RateOne, queue rules (`SkiRunRatingRules`), slow-rating WARN and log windows run in both cadences. Only the
triggers, the background throttle and the backstop interval change with the panel. The hub sets Demand every frame (any of its
bindings observed: Colossal.UI.Binding `EventBindingBase.observerCount` / `active`, EventBindingBase.cs:14-16, scratch decompile of the
installed 1.6.2f1 DLL).

| | panel open (Demand true) | panel closed (Demand false) |
| --- | --- | --- |
| rebuild triggers | the change signature below, 2 s heartbeat | one of our bound runs (`SkiRunBinding`) Created/Updated/Deleted this frame (vanilla building surfaces excluded) or BindGeneration moved, coalesced to one rebuild per `ClosedMinRebuildIntervalS` (0.5 s). Heartbeat: `ClosedHeartbeatS` (10 s) |
| sign count | `CountSigns` (walks every building) | skipped: Signs keeps its last value |
| background ratings | duty factor `DutyFactorOpen` 8 | `DutyFactorClosed` 32, >= 0.25 s apart |
| backstop | `BackstopS` 60 s | `ClosedBackstopS` 600 s (a safety net) |

The service does at most one front rating per UIUpdate frame. It serves an explicit `RequestRebuild` (dev tab, load) in both states. While paused, the
service keeps working (UIUpdate runs paused, SystemOrder.cs:67; throttles use `realtimeSinceStartup`). So a run drawn while paused
binds (`SkiRunBindTriggerSystem.SweepNow` at ModificationEnd) and is rated the next frame.

After a load, OnGameLoadingComplete clears everything, sets `ServiceState = WarmingUp` and requests a "load" rebuild.
- The warm-up starts after `WarmupFrames` and the heights-ready check, hub or no hub.
- ServiceState turns Ready when the seeded warm-up set is done, or `SkiRunRatingRules.WarmupDeadlineS` (90 s) after the load (one WARN). So the simulation never waits forever.
- The one-shot "!!!" sets `ServiceState = Failed`. The attraction model then stops holding its first provider write, and unrated runs count 0.
- The system logs the panel-closed cost once per load ("Catalog [service]"), after `ServiceCostLogAfterRebuilds` closed rebuilds that had at least one run row.
  Rebuilds of an empty list are only counted in `m_ClosedRebuilds` for the DUMP.

## Backstop

One change moves no key: roads and buildings change the terrain under the outline (no readable terrain version). So the
backstop re-rates the oldest-rated run every `BackstopS` (60 s: a main-thread rating of 2-9.5 ms every 10 s was too much). Skipping
unchanged runs is not possible cheaply. Node moves and re-snaps already re-rate through Key / YKey.

## Read API

`RatingServiceState` and `RunFacts` are nested. Use them on the main thread only.

- `ServiceState`.
- `RatingRevision`: ++ whenever a RunFacts field of any run changed. It is an FNV-1a over Entity, Gate, Kind, RateState|Band, AreaM2, LengthM,
  DropM, TopM, BottomM and NodeTopPos per row, plus the row count. Names, ordinals, grades and the sign count are not inputs. Compare for
  equality.
- `TryGetRunFacts`: TopNode = the highest outline node, the lift-served test point of `LiftServedRule`.
- `TryGetRating`: true only for a current rating (the run's outline key matched at the last rebuild, RateState 1-4).

## Rebuild triggers while Demand is true

The panel-open path listens only to our runs (the vanilla building, surface-area and area-edit triggers fired about once per wall second).

- One of our bound runs Created / Updated / Deleted this frame (`m_RunChangeQuery`). ApplyAreasSystem tags an edited run Updated on its original
  entity (ApplyAreasSystem.cs:167-202). The tag lives until CleanUpSystem at Cleanup (CleanUpSystem.cs:53), and UIUpdate
  runs before it (SystemOrder.cs:67). The system coalesces these to one rebuild per `ClosedMinRebuildIntervalS`.
- `SkiRunOwnershipSystem.BindGeneration` (bind / re-link / orphan / migration write our own `SkiRunBinding` and raise no tag).
- The active localization dictionary changed (default names are localized).
- A 2 s wall-clock heartbeat (`UnityEngine.Time.realtimeSinceStartup`, not UIUpdateState, which freezes while paused). The heartbeat catches what
  the signature cannot see. Examples: a sign moved, a run re-snapped by a terrain edit, a Welcome Sign placed or bulldozed, a newly drawn run not
  bound to a sign.

The rising edge of Demand rebuilds at once. A rebuild whose result equals the previous one (a content hash over every row field and the
totals) does not bump Revision. So the hub re-sends nothing. The system counts that rebuild and does not log it. A signature-driven rebuild that did change
something is logged at most `RebuildLinesPerWindow` times per `RebuildLineWindowS` (a terrain brush stroke re-snaps an unrated run's
nodes every pass). The system counts the rest, and the next logged line names how many were not logged.

## Rows

Per run, the system keeps cheap numbers as the fallback while a run is not rated (`RunRow.Approx`):

- areaM2 = `Geometry.m_SurfaceArea` (plan area, GeometrySystem.cs:517).
- lengthM = `(P + sqrt(max(0, P^2 - 16A))) / 4` with P = the horizontal perimeter of the node ring and A = |shoelace area|
  (`SkiSlopeClassifier.EquivalentRectLength`). It is exact for a constant-width band. A bowl over-reads. A diamond under-reads.
- dropM = max - min node y. A peak or hollow inside the outline is missed. The rating replaces it.
- OutlineXZ / OutlineY (RunRow, RunFacts): every outline node's xz / y in ring order, read in the same Node-buffer loop (no extra
  read). The system keeps OutlineXZ across rebuilds while Key and the node count are unchanged. It keeps OutlineY while Key, YKey and the count are unchanged. So a
  GroundHeightSystem re-snap hands a new height array. LiftServedRule.RunLinks keys its graph on both references (the downhill links).

Bound = `SkiRunOwnershipSystem.TryGetBoundGate`. Gate = that sign (Entity.Null = not linked), the run's resort. Gate is sticky from bind time.
It re-links free only when its sign died.

Names, in order of priority:
1. A custom name from `NameSystem.TryGetCustomName` (NameSystem.cs:209-212) wins.
2. Then a re-emitted run's carried custom name.
3. Then the run's trail name. `SkiRunName.m_Index` holds it (given by SkiRunNameSystem once the run is rated, the active dictionary's
   `SkiResort.RUN_NAME:<index>`, R3 DECISIONS 578). The run shows it in its form (`m_Form`: `RUN_NAME_FORM[Upper]` / `[Lower]` with "{NAME}" replaced,
   cached per index and form).
4. Only without a trail name (pool full, not given yet): "Run N" from `SkiResort.Hub.RUN_DEFAULT[Run]` or, for a
   cat track, "Cat track N" from `RUN_DEFAULT[CatTrack]`.

"{N}" is replaced (NameSystem.cs:250). English is the fallback if the key is missing. N counts per
(Gate, piste / cat track). The order is `Geometry.m_Bounds.max.y` descending, then center x, z, then entity index/version. Numbers shift when a run above is
added or removed (accepted). The system caches formatted names per (kind, N) and drops them when the dictionary changes
(`LocalizationManager.onActiveDictionaryChanged`, AreaBufferSystem.cs:661).

Row order sent to the UI: linked runs first, unlinked last. Inside each group, the highest top is first. The summary has these items:
- signs = live Welcome Signs (a chunk walk of every building checked against the ownership system's sign prefab set)
- runsBound / runsUnbound
- areaM2 = the sum over all our runs

## Rating cache

`SkiRunRater.Rate` rates the run's Node ring. (The Plan -> Fill -> Classify path is shared with `SkiRunDrawFeedbackSystem` and bit-identical there. See tests/SlopeBatch.)
It reads terrain through `TerrainHeightSampler`. It uses stepCurrent 0 (the smallest m within CellBudget,
SkiSlopeClassifier.cs:729-741) and no reuse source. So a given polygon on a given terrain always rates the same. The lattice is the feedback
system's: texel 1/hd.scale.x, origin -hd.offset.xz (TerrainSystem.cs:2618-2622, TerrainUtils.cs:35-38). The heights-ready check is the same as there
(`GetHeightData()` fresh, TerrainSystem.cs:2603, >= `SkiRunDrawFeedbackSystem.Tuning.MinHeightmapResolution` wide).

A built run's band matches the drawing tooltip, with two exceptions. Both are by design:
- The tooltip's per-Temp step hysteresis held a larger m.
- The rating is within `DisplayHysteresis` (0.5 %) of a band threshold (the cache shows `BandOf(RatingGrade)`).

The cache rules:

- Cache: `Dictionary<Entity, RunRating>`. Its fields are State (1 rated / 2 no terrain / 3 too large / 4 failed), Band, RatingGrade, SteepestGrade,
  MeanGrade, Connected, DropM, TopM / BottomM, LengthM (horizontal), StepMultiple, RateMs, Key, YKey, TerrainEpoch. Key = FNV-1a over
  every node's x and z bits and the node count, the rating's real inputs (the classifier reads node y only for a finiteness check,
  SkiSlopeClassifier.cs:803-805). YKey = the same over the y bits. A GroundHeightSystem re-snap (y only, AreaUtils.cs:672-677,
  GroundHeightSystem.cs:777-791) moves YKey and never Key. So the row keeps its rating, and the system re-rates the run in the background as
  "terrain".
- Queue (`SkiRunRatingRules.NeedsRating` / `Replaces`):
  - A row with no entry is queued at the back as "warm-up" while this load's warm-up is being seeded. Otherwise it is queued at the front as "bind".
  - A Key mismatch is queued at the front as "edit".
  - A YKey mismatch or an older terrain epoch is queued at the back as "terrain".
  - "bind" never moves a row queued as warm-up to the front. Every rebuild re-asks every unrated row as bind, which
    would drain the warm-up at the front with no duty cycle. An edit still promotes it.
  - Background ratings (warm-up, terrain, backstop) start only when `now >= last start + factor x last duration` (`SkiRunRatingRules.NextBackgroundStart`).
- Row band: -1 while pending, -2 when the rating ended in no terrain / too large / failed (`SkiRunRatingRules.RowBand`). This is a final state.
  The hub shows it as "cannot be rated" instead of "being rated" forever.
- Invalidation:
  - Key change (front).
  - YKey change (back).
  - Terrain epoch++ when the active tool leaves `TerrainToolSystem` (`ToolSystem.EventToolChanged`, ToolSystem.cs:30, fired from the activeTool setter :84-98).
    The system applies it `TerrainEpochDelayFrames` later, so the heightmap readback has landed (unmeasured).
  - The backstop.

  An edited run reads "pending" (RateState 0, node numbers) until re-rated. A run that waits for a terrain or backstop re-rate keeps its old rating meanwhile.
  The system drops rows that left. OnGameLoadingComplete clears everything.
- Memory: after a rating whose lattice exceeded `ShrinkCells` (2^19), the system replaces the grid / result pair with fresh small instances. The
  classifier's 2^21 bbox cap (SkiSlopeClassifier.cs:651) lets the pair grow to tens of MB (an estimate).
- Logs:
  - `Catalog [rate] run 'Groomed run 2' 95601:11: Blue 23.1 % (steepest 31.0 %), drop 162 m, length 540 m, m=2, 4.8 ms (bind|edit)`
    for bind and edit ratings (at most `RateLinesPerWindow` per `RateLineWindowS`). The system counts warm-up, terrain and backstop ratings and does not log them.
  - One `Catalog [rate-warmup]` line per load (or a WARN when the 90 s deadline hit first).
  - One `Catalog [rate] slow rating ...` WARN per load above `SkiRunDrawFeedbackSystem.Tuning.SlowRecomputeWarnMs` (built-run ratings take 4-11 ms on the main thread).

## Phase (Mod.cs)

`UpdateAt` UIUpdate, registered before `SkiResortStatsSystem` and `SkiResortHubUISystem`. `UIUpdateSystem` drives UIUpdate in
MainLoop every frame, paused included (UIUpdateSystem.cs:17-20, SystemOrder.cs:67), after ToolSystem and ModificationSystem
(SystemOrder.cs:58, 60). So this frame's Created/Updated/Deleted tags are visible. A plain `UpdateAt` runs in add order
(UpdateSystem.cs:158-161), and update intervals are ignored in this phase (:183-221). So each frame: catalog (reads the Demand the hub
set last frame) -> stats -> hub (pushes this frame's revisions).

## House contract

- A "created" line.
- One "Catalog [arm]" line per load that lists every constant.
- A one-shot "!!!" catch that stops the catalog until the next load.
- No per-frame managed allocation or string formatting.
- One rating per frame at most.
- Game mode only.
