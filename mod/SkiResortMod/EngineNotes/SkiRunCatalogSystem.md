# SkiRunCatalogSystem: the run list, the always-on rating service, its cadences and the rating cache

`SkiRunCatalogSystem` is the Ski Resort hub's in-memory list of our ski runs plus the resort totals, and the always-on rating service:
the simulation scores runs by these ratings, so they exist with the hub closed. Read by `SkiResortHubUISystem`, `SkiResortStatsSystem`
and `SkiResortAttractivenessSystem`; it owns no binding and writes nothing to the world.

## Two cadences, one service

The same Rebuild, RateStep, RateOne, queue rules (`SkiRunRatingRules`), slow-rating WARN and log windows run in both; only the
triggers, the background throttle and the backstop interval change with the panel. Demand is set by the hub every frame (any of its
bindings observed; Colossal.UI.Binding `EventBindingBase.observerCount` / `active`, EventBindingBase.cs:14-16, scratch decompile of the
installed 1.6.2f1 DLL).

| | panel open (Demand true) | panel closed (Demand false) |
| --- | --- | --- |
| rebuild triggers | the change signature below, 2 s heartbeat | one of our bound runs (`SkiRunBinding`) Created/Updated/Deleted this frame (vanilla building surfaces excluded) or BindGeneration moved, coalesced to one rebuild per `ClosedMinRebuildIntervalS` (0.5 s); `ClosedHeartbeatS` (10 s) |
| sign count | `CountSigns` (walks every building) | skipped: Signs keeps its last value |
| background ratings | duty factor `DutyFactorOpen` 8 | `DutyFactorClosed` 32, >= 0.25 s apart |
| backstop | `BackstopS` 60 s | `ClosedBackstopS` 600 s (a safety net) |

One front rating per UIUpdate frame at most. An explicit `RequestRebuild` (dev tab, load) is served in both states. While paused the
service keeps working (UIUpdate runs paused, SystemOrder.cs:67; throttles use `realtimeSinceStartup`), so a run drawn while paused
binds (`SkiRunBindTriggerSystem.SweepNow` at ModificationEnd) and is rated the next frame.

After a load, OnGameLoadingComplete clears everything, sets `ServiceState = WarmingUp` and requests a "load" rebuild; the warm-up starts
after `WarmupFrames` and the heights-ready check, hub or no hub; ServiceState turns Ready when the seeded warm-up set is done or
`SkiRunRatingRules.WarmupDeadlineS` (90 s) after the load (one WARN), so the simulation never waits forever. The one-shot "!!!" sets
`ServiceState = Failed`: the attraction model stops holding its first provider write and unrated runs count 0. The panel-closed cost is
logged once per load ("Catalog [service]", after `ServiceCostLogAfterRebuilds` closed rebuilds that had at least one run row; rebuilds of
an empty list are only counted in `m_ClosedRebuilds` for the DUMP).

## Backstop

The one change that moves no key is terrain under the outline changed by roads and buildings (no readable terrain version), so the
backstop re-rates the oldest-rated run every `BackstopS` (60 s: a main-thread rating of 2-9.5 ms every 10 s was too much). Skipping
unchanged runs is not possible cheaply; node moves and re-snaps already re-rate through Key / YKey.

## Read API

`RatingServiceState` and `RunFacts` are nested; main thread only.

- `ServiceState`.
- `RatingRevision`: ++ whenever a RunFacts field of any run changed. An FNV-1a over Entity, Gate, Kind, RateState|Band, AreaM2, LengthM,
  DropM, TopM, BottomM and NodeTopPos per row plus the row count; names, ordinals, grades and the sign count are not inputs. Compare for
  equality.
- `TryGetRunFacts`: TopNode = the highest outline node, the lift-served test point of `LiftServedRule`.
- `TryGetRating`: true only for a current rating (the run's outline key matched at the last rebuild, RateState 1-4).

## Rebuild triggers while Demand is true

The panel-open path listens only to our runs (vanilla building, surface-area and area-edit triggers fired about once per wall second).

- One of our bound runs Created / Updated / Deleted this frame (`m_RunChangeQuery`: an edited run is tagged Updated on its original
  entity by ApplyAreasSystem, ApplyAreasSystem.cs:167-202; the tag lives until CleanUpSystem at Cleanup, CleanUpSystem.cs:53, and UIUpdate
  runs before it, SystemOrder.cs:67), coalesced to one rebuild per `ClosedMinRebuildIntervalS`.
- `SkiRunOwnershipSystem.BindGeneration` (bind / re-link / orphan / migration write our own `SkiRunBinding` and raise no tag).
- The active localization dictionary changed (default names are localized).
- A 2 s wall-clock heartbeat (`UnityEngine.Time.realtimeSinceStartup`, not UIUpdateState, which freezes while paused), which catches what
  the signature cannot see (a sign moved, a run re-snapped by a terrain edit, a Welcome Sign placed or bulldozed, a newly drawn run not
  bound to a sign).

The rising edge of Demand rebuilds at once. A rebuild whose result equals the previous one (a content hash over every row field and the
totals) does not bump Revision, so the hub re-sends nothing; it is counted, not logged. A signature-driven rebuild that did change
something is logged at most `RebuildLinesPerWindow` times per `RebuildLineWindowS` (a terrain brush stroke re-snaps an unrated run's
nodes every pass); the rest are counted and the next logged line names how many were not.

## Rows

Per run, cheap numbers are kept as the fallback while a run is not rated (`RunRow.Approx`):

- areaM2 = `Geometry.m_SurfaceArea` (plan area, GeometrySystem.cs:517).
- lengthM = `(P + sqrt(max(0, P^2 - 16A))) / 4` with P = the horizontal perimeter of the node ring and A = |shoelace area|
  (`SkiSlopeClassifier.EquivalentRectLength`; exact for a constant-width band, a bowl over-reads, a diamond under-reads).
- dropM = max - min node y (a peak or hollow inside the outline is missed; the rating replaces it).
- OutlineXZ / OutlineY (RunRow, RunFacts): every outline node's xz / y in ring order, read in the same Node-buffer loop (no extra
  read). OutlineXZ is kept across rebuilds while Key and the node count are unchanged; OutlineY while Key, YKey and the count are, so a
  GroundHeightSystem re-snap hands a new height array. LiftServedRule.RunLinks keys its graph on both references (the downhill links).

Bound = `SkiRunOwnershipSystem.TryGetBoundGate`; Gate = that sign (Entity.Null = not linked), the run's resort (sticky from bind time,
re-links free only when its sign died).

Names: a custom name from `NameSystem.TryGetCustomName` (NameSystem.cs:209-212) wins; otherwise "Groomed run N" etc. from the active
dictionary's `SkiResort.Hub.RUN_DEFAULT[<kind>]` with "{N}" replaced (NameSystem.cs:250), English if the key is missing. N counts per
(Gate, kind), ordered by `Geometry.m_Bounds.max.y` descending, then center x, z, entity index/version (numbers shift when a run above is
added or removed, accepted); formatted names are cached per (kind, N), dropped when the dictionary changes
(`LocalizationManager.onActiveDictionaryChanged`, AreaBufferSystem.cs:661).

Row order sent to the UI: linked runs first, unlinked last; inside each group highest top first. Summary: signs = live Welcome Signs (a
chunk walk of every building checked against the ownership system's sign prefab set), runsBound / runsUnbound, areaM2 = the sum over all
our runs.

## Rating cache

`SkiRunRater.Rate` (the Plan -> Fill -> Classify path shared with `SkiRunDrawFeedbackSystem`, bit-identical there; tests/SlopeBatch) rates
the run's Node ring, terrain read through `TerrainHeightSampler`, stepCurrent 0 (the smallest m within CellBudget,
SkiSlopeClassifier.cs:729-741) and no reuse source, so a given polygon on a given terrain always rates the same. Lattice = the feedback
system's: texel 1/hd.scale.x, origin -hd.offset.xz (TerrainSystem.cs:2618-2622, TerrainUtils.cs:35-38); heights-ready check as there
(`GetHeightData()` fresh, TerrainSystem.cs:2603; >= `SkiRunDrawFeedbackSystem.Tuning.MinHeightmapResolution` wide). A built run's band
matches the drawing tooltip except when the tooltip's per-Temp step hysteresis held a larger m, and within `DisplayHysteresis` (0.5 %) of
a band threshold (the cache shows `BandOf(RatingGrade)`); both by design.

- Cache: `Dictionary<Entity, RunRating>` - State (1 rated / 2 no terrain / 3 too large / 4 failed), Band, RatingGrade, SteepestGrade,
  MeanGrade, Connected, DropM, TopM / BottomM, LengthM (horizontal), StepMultiple, RateMs, Key, YKey, TerrainEpoch. Key = FNV-1a over
  every node's x and z bits and the node count, the rating's real inputs (the classifier reads node y only for a finiteness check,
  SkiSlopeClassifier.cs:803-805); YKey = the same over the y bits. A GroundHeightSystem re-snap (y only, AreaUtils.cs:672-677,
  GroundHeightSystem.cs:777-791) moves YKey and never Key, so the row keeps its rating and the run is re-rated in the background as
  "terrain".
- Queue (`SkiRunRatingRules.NeedsRating` / `Replaces`): a row with no entry is queued at the back as "warm-up" while this load's warm-up
  is being seeded, else at the front as "bind"; a Key mismatch at the front as "edit"; a YKey mismatch or an older terrain epoch at the
  back as "terrain". A row queued as warm-up is never moved to the front by "bind" (every rebuild re-asks every unrated row as bind, which
  would drain the warm-up at the front with no duty cycle); an edit still promotes it. Background ratings (warm-up, terrain, backstop)
  start only when `now >= last start + factor x last duration` (`SkiRunRatingRules.NextBackgroundStart`).
- Row band: -1 while pending, -2 when the rating ended in no terrain / too large / failed (`SkiRunRatingRules.RowBand`): a final state
  the hub shows as "cannot be rated" instead of "being rated" forever.
- Invalidation: Key change (front); YKey change (back); terrain epoch++ when the active tool leaves `TerrainToolSystem`
  (`ToolSystem.EventToolChanged`, ToolSystem.cs:30, fired from the activeTool setter :84-98), applied `TerrainEpochDelayFrames` later so
  the heightmap readback has landed (unmeasured); the backstop. An edited run reads "pending" (RateState 0, node numbers) until
  re-rated; a run waiting for a terrain or backstop re-rate keeps its old rating meanwhile. Rows that left are dropped;
  OnGameLoadingComplete clears everything.
- Memory: after a rating whose lattice exceeded `ShrinkCells` (2^19), the grid / result pair is replaced by fresh small instances (the
  classifier's 2^21 bbox cap, SkiSlopeClassifier.cs:651, lets the pair grow to tens of MB; an estimate).
- Logs: `Catalog [rate] run 'Groomed run 2' 95601:11: Blue 23.1 % (steepest 31.0 %), drop 162 m, length 540 m, m=2, 4.8 ms (bind|edit)`
  for bind and edit ratings (at most `RateLinesPerWindow` per `RateLineWindowS`; warm-up, terrain and backstop ratings are counted, not
  logged); one `Catalog [rate-warmup]` line per load (or a WARN when the 90 s deadline hit first); one `Catalog [rate] slow rating ...`
  WARN per load above `SkiRunDrawFeedbackSystem.Tuning.SlowRecomputeWarnMs` (built-run ratings take 4-11 ms on the main thread).

## Phase (Mod.cs)

`UpdateAt` UIUpdate, registered before `SkiResortStatsSystem` and `SkiResortHubUISystem`. UIUpdate is driven by `UIUpdateSystem` in
MainLoop every frame, paused included (UIUpdateSystem.cs:17-20, SystemOrder.cs:67), after ToolSystem and ModificationSystem
(SystemOrder.cs:58, 60), so this frame's Created/Updated/Deleted tags are visible; a plain `UpdateAt` runs in add order
(UpdateSystem.cs:158-161) and update intervals are ignored in this phase (:183-221). So each frame: catalog (reads the Demand the hub
set last frame) -> stats -> hub (pushes this frame's revisions).

## House contract

"created" line; one "Catalog [arm]" line per load listing every constant; a one-shot "!!!" catch that stops the catalog until the next
load; no per-frame managed allocation or string formatting; one rating per frame at most; game mode only.
