# Mod.cs: why each system runs in its update phase and anchor

`Mod.OnLoad` registers every system with `UpdateSystem` (`UpdateAt` / `UpdateBefore` / `UpdateAfter`). The one-line comments in
`Mod.cs` say which registrations are load-bearing. This note holds the engine reasoning behind them. Citations are `File.cs:line`
in the 1.6.0f1 decompile unless a 1.6.2 line is given. Where a line says "same in 1.6.2", the cited lines are identical there.

## Shared idioms

- A plain `UpdateAt` sorts after every vanilla `UpdateAt` of its phase. Mods register after `SystemOrder.Initialize`, so add-index
  order applies (UpdateSystem.cs:39-47, :158-161). An anchorless `UpdateBefore` sorts ahead of every `UpdateAt` (SystemOrder.cs:49,
  UpdateSystem.cs:39-47, :163-166).
- Each `UpdateBefore` / `UpdateAfter` call adds one entry (UpdateSystem.cs:173-176, :271-292). Give a system ONE registration:
  an `UpdateAt` as well would run it twice a frame.
- Anchoring a mod system to a mod system works regardless of registration order. `UpdateBefore<T, Anchor>` files T under the anchor
  in the reference map of `UpdateSystem`. When both are in the same phase, Refresh emits T right before the anchor (UpdateSystem.cs:173-176, :278-292, :388-413).
  A mod-to-mod anchor recurses. `AddSystemUpdate` (:382-441) adds the same-phase before-entries of a system, then the system itself,
  then its after-entries. If the phase of the anchor differs from the registered phase, the anchor is skipped and the system
  silently never runs.
- A follower inherits the update offset of its anchor only when the update intervals match (UpdateSystem.cs:400-431). Never override
  `GetUpdateOffset` on a follower.
- Phases that the non-indexed `UpdateSystem.Update(phase)` drives ignore update intervals (ModificationSystem and UIUpdate,
  UpdateSystem.cs:183-221).
- MainLoop order:
  1. The `EndFrameBarrier` playback comes first (SystemOrder.cs:49).
  2. ToolSystem (:58).
  3. ModificationSystem (:60, ModificationSystem.cs:19-26).
  4. UIUpdateSystem (:67, UIUpdateSystem.cs:17-20).
  5. Rendering (:68).
- `CleanUpSystem` strips Created/Updated and destroys Deleted at Cleanup (SystemOrder.cs:54, CleanUpSystem.cs:52-53). GameSimulation
  runs later, from LateUpdate (SystemOrder.cs:74, SimulationSystem.cs:286). It does nothing while paused (zero iterations,
  SimulationSystem.cs:221-223).
- UITooltip: `TooltipUISystem` clears the mouse group first each frame (TooltipUISystem.cs:48-55), so every tooltip is re-added every
  frame. The phase is already suppressed over UI. To keep our lines next to a vanilla tooltip, anchor after that vanilla system
  instead of a plain `UpdateAt`.
- `OverlayRenderSystem` (Rendering, SystemOrder.cs:687, 1.6.2 :688) completes every registered buffer writer. Then it uploads and clears
  the overlay lists each frame (OverlayRenderSystem.cs:738-741, 781, 790). Schedule anything that draws overlay lines
  `UpdateBefore<..., OverlayRenderSystem>` in Rendering. Rendering runs every frame, paused included.
- Systems that only mutate prefab data (`CursorAimSystem`, `LodgeRoadSystem`, `SkiRunAreaTypeSystem`) are registered disabled and
  act in `OnGameLoadingComplete` only. Load hooks fire for a disabled system (GameSystemBase.cs:27-29, :60-70).

## Near SkiResortAttractivenessSystem

`AttractionSystem` rewrites `AttractivenessProvider.m_Attractiveness` for every building that carries the component. It runs on a
16-frame cycle (1/16 of buildings per tick, sliced by the `UpdateFrame` shared component). So a write once per game hour would be
overwritten within 16 frames. We mirror the cadence of vanilla (interval 16, inherited offset) with `UpdateAfter<TSystem, TAnchor>`.
Each tick, our write lands after the write of vanilla for the same `UpdateFrame` slice and wins.

## Near SnowLineSystem

Snow-line / climate oracle: pull-based, `Enabled = false`, no per-tick work. It is the single source for snow and season gating, the
snowmaking margin, the ambient (`ClimateSystem.SampleClimate` at the current date) and the cached 24-hour day mean. The attraction
score, the snow injection, the run decals, and the hub pull these values. It logs the climate constants once per load.

## Near SkiPrefabCatalogSystem

Every lift, station, lodge, sign and run prefab name lives in the pure `SkiCatalog.cs`. This system resolves them lazily on the
first query after each Game / Editor load. It revalidates cached prefab entities once per frame (`PrefabSystem.UpdatePrefabs` gives
an updated prefab a new entity and tags the old one Deleted, PrefabSystem.cs:766-824). It writes one `PrefabCatalog [arm]` line per
load.

- `OnGamePreload` invalidates for those modes only. A main-menu preload keeps the views of the last load. GameManager.cs:1023 fires
  the preload before the deserialization of every load, and :1126 completes the load.
- It is registered like `CursorAimSystem`: disabled, no per-frame work. The phase is not load-bearing (load hooks fire for a disabled
  system, GameSystemBase.cs:27-29, :60-70). Consumers reach it with `GetOrCreateSystemManaged` in their `OnCreate`, so
  registration order is not load-bearing either.
- It covers 30 names:
  - 4 lift types with line + carrier each
  - the marker
  - 8 stations
  - 5 lodge tiers
  - the school
  - 2 patrol huts
  - the sign
  - 4 runs

  It also covers the one 25-row build-item table. The system computes the counts in its "created" and [arm] lines.

## Near ChairPinSystem

`SubObjectSystem` spawns/moves chairs at Modification2B. `LaneSystem` finalizes the cable-lane curves at Modification4
(`ModificationBarrier4`). At Modification4B both exist in their final state. Our Transform write lands before
`Game.Objects.SearchSystem` (Modification5) and PreCulling consume it.

## Near PowerBridgeSystem

The owned 3 m connection stub of our stations fails the orphan gate of vanilla `ElectricityBuildingGraphSystem`. So that system never
registers the electricity connection of the station (and actively deletes it on updates). `PowerBridgeSystem` bootstraps
`ElectricityValveConnection` onto the stub nodes. Then the OTHER arm of the gate passes. It must run BEFORE the vanilla system in the
same phase (Modification4B). Then the valve is visible the very frame a station is created/updated. The anchored registration
guarantees this.

## Near LiftReconnectSystem

Placing a station onto the end of an existing lift line never re-triggers lane building (`LaneSystem` only processes Updated owners).
So cables stayed undrawn. `LiftReconnectSystem` marks the net entities of the connection stub Updated on station events.
`SubNetReferencesSystem` populates the `SubNet` buffer at Modification4. `LaneSystem` consumes the tags later in that same phase. So
the system anchors directly before `LaneSystem` inside Modification4.

## Near CursorAimSystem

`NetToolSystem` reads `NetGeometryData.m_DefaultSurfaceHeight.max` of the drawn prefab as the ray offset. The prefab-side value is
hard-coupled to center(HeightRange) = 13. `CursorAimSystem` rewrites the prefab field to the tower head once per load. It changes
data only and does no per-frame work (registered like `SnowLineSystem`: disabled, `OnGameLoadingComplete` only).


## Near SkiSnowInjectionSystem

Tops up run-shaped snow in `SnowSystem`'s own depth buffer.

- Phase: GameSimulation is `SnowSystem`'s own phase. So it ticks only while the sim advances and only in Game action mode. Pause
  gating and editor inertness come from the phase itself.
- Anchor: the write must land after the ping-pong flip of the frame. Then the NEXT `AddSnow` consumes the injected texels as
  `_Previous`. The anchored registration inherits the 4-frame offset slot of `SnowSystem`. The system gates its own 64-frame top-up
  cadence off `SimulationSystem.frameIndex`.

## Near SkiRunPropSystem

Lot class gates vanilla area props (`AreaSubObjects`, one border prop per polygon vertex). The archetype of `LotPrefab` hard-adds the
runtime `Game.Objects.SubObject` buffer that the spawner queries for. The archetype of `SurfacePrefab` does not. This system adds
that buffer (and ensures Updated) on OUR run surfaces. It stops the per-node props and walks the border itself
(EngineNotes/SkiRunPropSystem.md).

- The phase is load-bearing for two reasons. `SecondaryObjectSystem` consumes the Updated tag at Modification4B, and Cleanup wipes
  the tag every frame. So the bridge must land earlier in the SAME frame's Modification4B. It anchors directly before the vanilla
  spawner (the `PowerBridgeSystem` precedent).
- Game mode only: spawned props must never be baked into maps.
- It also writes the Options prop density into the run prefabs. After a settled slider change, it tags `ChangedDistrict` on our
  propped runs. Both writes must land before the vanilla spawner too.

## Near SkiNightLightSystem

Nothing in the engine re-evaluates a Night-gated `LightEffect` on a static area sub-object. In a field test with a control mast, the
magenta light stayed ON all day until a tool hover rebuilt the props. This system stamps `EffectsUpdated` on run-owned props at
every day/night flip (it is our `StreetLightSystem`). Anchor AND interval are load-bearing. We read the flag that `EffectFlagSystem`
just wrote (public `GetData`). Offset inheritance requires equal intervals (2048).

## Near SkiSnowGunPlumeSystem

Sets the plume lease of each gun. The lease is `EarlyDisasterWarningDuration`, which the Disaster condition of our `SkiSnowGunPlume`
EffectPrefab reads. The lease comes from the per-run "guns running" flag of the injection and the elevation of the gun itself. The
system stamps `EffectsUpdated` on a flip.

- The phase is load-bearing: GameSimulation (LateUpdate) lets the tag live into the NEXT frame's PreCulling. There
  `EffectControlSystem` consumes it, before Cleanup strips it (the `SkiNightLightSystem` precedent). Pause freezes the plumes with the
  snow.
- The anchor after the injection is NOT load-bearing. It reads flags that the injection published, at most one 64-frame window old.
  The intervals differ, so no offset is inherited.
- Game mode only.

## Near SkiRunSurfaceSystem

`SurfacePrefab` adds `Game.Areas.Surface`, an EMPTY TAG. The owner-walk of the renderer looks for the `Game.Objects.Surface` weather
struct instead. So props parented to our runs render snow-free in every season. This system gives run areas that component and
tracks it to measured run depth. BOTH the anchor and the interval are load-bearing. `BatchDataSystem` only re-reads surface wetness
when `LastSystemVersion` of `WetnessSystem` moves, so we must write in the same tick as vanilla. `UpdateSystem` inherits the offset of
an anchor only when the intervals match. Hence `GetUpdateInterval` == 256, the interval of `WetnessSystem`.

## Near SkiRunUpkeepSystem

Run operating costs (snowmaking > groomed > natural = free). The system bills `PlayerMoney` on the City entity from the per-type
ACTIVE thermal mask of the injection system. So money, visuals and attractiveness share one climate model. There is no budget-panel
line item: `ExpenseSource` is a closed enum, and extending it needs Harmony (banned). GameSimulation means pause stops the meter and
the Editor never bills. The system is anchored after the injection system. So a same-frame top-up publishes its mask before the
charge pass reads it.

## Near SkiGuestCounterSystem

The anchor is load-bearing for freshness, not for exactly-once. `MeetingSystem` (UpdateAt GameSimulation, SystemOrder.cs:404,
interval 16, MeetingSystem.cs:277-280) moves a meeting only on its own updates. With the same interval we inherit its offset
(UpdateSystem.cs:400-431) and read the state its update just wrote. The scans are IJobs. Nothing waits on the job of `MeetingSystem`
on the main thread.

## Near SkiResortHistorySystem

Keeps one saved sample per ski area per closed month on its Welcome Sign. It mirrors the month in progress onto the sign (seeded
back on load). Anchor AND interval are load-bearing. It reads the counter's `LastMonthStep` and `Ledger` right after the counter's
`AdvanceMonth`. With the interval of the counter (16), it inherits the offset of the counter (UpdateSystem.cs:400-431) and runs in the
same frame after it. So the first tick after a load seeds the ledger before anything mirrors over the save. GameSimulation: pause
stops it. The Editor never arms it.

## Near SkiDaySystem

Claim at decision. This applies to a share of residents at home and to hotel tourists who just decided to go out (Leisure with no
venue yet). The system names a ski-day lodge with the two writes of `LeisureSystem` (`Leisure.m_TargetAgent` +
`TripNeeded { lodge, Leisure, 128 }`, LeisureSystem.cs:398-405, 1.6.2 :392-399). For a far trip with a free household car, it also
uses the public car-reserve queue (CitizenBehaviorSystem.cs:1094-1103, 1.6.2 :1126-1135, WorkerSystem.cs:450-454).

- Anchor AND interval are load-bearing. `LeisureSystem` is UpdateAt GameSimulation (SystemOrder.cs:377, 1.6.2 :378).
  `GetUpdateInterval` returns the own interval of `LeisureSystem` (262144 / kUpdatePerDay = 64, LeisureSystem.cs:914-917,
  1.6.2 :911-913). So the follower inherits its offset (UpdateSystem.cs:431-436, :355-356, :457-516). Every pass runs on the frames of
  `LeisureSystem`, before `TripNeededSystem` (SystemOrder.cs:393, 1.6.2 :394). NEVER override `GetUpdateOffset`.
- `Leisure` and `TripNeeded` are read-write in the job. ECS orders our job after the job of `LeisureSystem`. It orders the job of
  `TripNeededSystem` after our job. No main-thread wait.
- We register it before `SkiResortStatsSystem`, so this line creates `SkiResortStatsSystem` (UpdateSystem.cs:178-181).
- Pause stops it. The Editor never arms it. ONE registration only.

## Near SkiRunOwnershipSystem

Supplies the one-time bind charge and the 5 km tether. Vanilla implements them only for OBJECT ServiceUpgrades. Area upgrades are a
menu entry with no install path, and all four writers are Object-gated (see the system header). A run carries no vanilla Owner, and
the sign has no `Game.Areas.SubArea` buffer. The link is our own serialized `SkiRunBinding`. An Owner on a run let the raycast of the
game-mode bulldozer walk from one run to the sign (Game.Areas/RaycastJobs.cs:80-93) and delete the whole resort
(BulldozeToolSystem.cs:664). Both are unchanged in 1.6.2. The sign-to-runs delete cascade is ours (`SkiRunGateCascadeSystem`). The
system is anchored before the injection system in the same phase. So a bind and the first snow of a freshly bound run can land in one
64-frame window.

## Near SkiRunDecalSystem

Probes and mutates the LIVE "Area batch (<run surface>)" material clones. It touches these items:
- The alpha-coverage decal texture pairs.
- The built-in snow slots of the shader (the winter look rides the snow sim of the game).
- Conservative doc-safe scalars.

It is anchored AFTER the
vanilla `AreaBatchSystem` in PreCulling. So in the frame when a batch clone is lazily created, our find-retry can already see it.
PreCulling runs every frame in Game AND Editor, paused included. So the retry countdown and re-assert sentinel tick.

## Near SkiGladeToolSystem

The registration order between the four glade systems (tool, hotkey, tag, cleanup) is not load-bearing. `UpdateAt` creates each
system immediately (UpdateSystem.cs:158-161), and the other three reach the tool through `GetOrCreateSystemManaged` anyway. The tool
goes first only so its "created" log line precedes the lines of its dependents.

ToolUpdate is where every vanilla tool lives (SystemOrder.cs:699-709, 1.6.2 :700-710). `ToolBaseSystem.OnCreate` adds it to
`ToolSystem.tools` and sets `Enabled = false` (ToolBaseSystem.cs:310, 315). `ToolSystem.ToolUpdate` enables ONLY the active tool
(ToolSystem.cs:312-327). So the tool ticks only while active and cannot poll its own toggle key.

## Near SkiGladeHotkeySystem

Defaults: Ctrl+Shift+G toggle, Shift+G next tree density 1-8, Alt+G run interior / tree line, Ctrl+Alt+G dump diagnostics. The phase
is load-bearing. `UIUpdateSystem` drives UIUpdate in MainLoop (SystemOrder.cs:67, UIUpdateSystem.cs:19), i.e. every frame, paused
or not. GameSimulation would stop while paused. The phase of the tool itself only ticks while the tool is active. UIUpdate also
precedes Rendering (SystemOrder.cs:67-68). So a dump pressed with a run on the cursor reads the live readout of the previous frame.

## Near SkiGladeTagSystem

The tag must land on the Temp trees BEFORE `ApplyObjectsSystem` turns them permanent in the same ApplyTool phase (SystemOrder.cs:714,
1.6.2 :715). Create keeps the entity and only swaps Temp for Applied/Created/Updated (ApplyObjectsSystem.cs:670-689). So the tag
carries into the permanent tree.

## Near SkiGladeCleanupSystem

Member cleanup when a run is deleted. A run is deleted directly (bulldozer, area tool, info panel) or by `SkiRunGateCascadeSystem`
when its sign is deleted (registered right below, anchored BEFORE this system). There is no vanilla SubArea cascade. Runs carry no
Owner and the sign has no SubArea buffer. So `SubElementDeleteSystem` (PostTool, SystemOrder.cs:724) and the sub-area walk of
`BulldozeToolSystem` never reach a run. The phase is load-bearing. Modification2 runs paused or not. It sees the Deleted tags of the
same frame (they live one frame, and Cleanup wipes them). It runs after PostTool (ToolSystem runs PostTool at ToolSystem.cs:257 and
precedes ModificationSystem in MainLoop, SystemOrder.cs:58, 60, ModificationSystem.cs:20).

## Near SkiRunGateCascadeSystem

When a Ski Area Welcome Sign gets Deleted, every run bound to it (`SkiRunBinding.m_Gate`) gets Deleted the same frame. Phase and
anchor are load-bearing:

- A bulldozed sign is tagged Deleted at ApplyTool inside ToolUpdate (ApplyObjectsSystem.cs:484-488). The playback of `EndFrameBarrier`
  tags a collapsed sign at the start of MainLoop, ahead of ToolSystem (CollapsedBuildingSystem.cs:150-175 via :261/:318). An anchorless
  `UpdateBefore` sorts ahead of every `UpdateAt` (SystemOrder.cs:49, UpdateSystem.cs:39-47, :163-166). MainLoop then runs ToolSystem and
  ModificationSystem (SystemOrder.cs:58, 60) and Modification2 inside it (ModificationSystem.cs:19-26). So both tags are visible here.
- Modification1/2 hold no vanilla area-delete consumer (SystemOrder.cs:94-121).
- `UpdateBefore<SkiGladeCleanupSystem>` makes the chain sign, runs, member trees complete in one frame. The border props of the runs
  are Secondary sub-objects. `SecondaryObjectSystem` deletes them later in the frame at Modification4B (SecondaryObjectSystem.cs:728-740,
  SystemOrder.cs:183). Everything is destroyed in the same Cleanup (SystemOrder.cs:50, 54). All lines are identical in 1.6.2.
- The anchor must stay at Modification2. Otherwise the cascade silently never runs (see the mod-to-mod anchor idiom above, precedent:
  `SkiRunUpkeepSystem` after `SkiSnowInjectionSystem`).

## Near the run-binding and drawing-feedback systems

Each is gated per frame on its own setting (Settings > Runs) and on the ownership system. The bind trigger and the contour lines gate
on `SkiRunOwnershipSystem.IsArmed`. The drawing feedback gates on `PrefabSetsReady` (a binding exception must not switch the slope
feedback off). All four reach the ownership system through `GetOrCreateSystemManaged`, so registration order between them is not
load-bearing.

## Near SkiRunBindTriggerSystem

ModificationEnd runs in MainLoop, paused or not (SystemOrder.cs:58, 60, ModificationSystem.cs:19-26). The GameSimulation sweep
differs: it has zero iterations while paused (SimulationSystem.cs:221-223).

- The finished run already lost Temp and gained Applied/Created/Updated at the `ToolOutputBarrier` inside ToolUpdate
  (ApplyAreasSystem.cs:204-208, 272, 297-299, SystemOrder.cs:694-695, 1.6.2 :695-696). `GeometrySystem` re-triangulated it at
  Modification2B (SystemOrder.cs:124, GeometrySystem.cs:752, 780). Created lives until Cleanup (CleanUpSystem.cs:53).
- A plain `UpdateAt` sorts after every vanilla `UpdateAt` of the phase. So every vanilla Modification system has already run, and
  the Geometry center of the run is final. The bind adds only our own `SkiRunBinding`. This structural change raises no tag, so vanilla
  systems still see nothing (no Owner, no SubArea).
- The sign arm reacts to a Created sign only. It has no update interval: the non-indexed `UpdateSystem.Update(phase)` of
  ModificationSystem ignores it (UpdateSystem.cs:183-221).

## Near SkiResortAttractivenessWatchSystem

The phase is load-bearing, for the reason of the bind trigger. `CleanUpSystem` strips Created/Updated and destroys Deleted entities at
Cleanup (GameManager.cs:711 -> :2399, SystemOrder.cs:54, CleanUpSystem.cs:52-53). This happens BEFORE GameSimulation runs from
LateUpdate (GameManager.cs:721 -> :2407, SystemOrder.cs:74, SimulationSystem.cs:286, 1.6.2 :721/:2414, :731/:2422). So
`SkiResortAttractivenessSystem` (GameSimulation, anchored above) never sees a placement. At ModificationEnd (MainLoop, paused
included, ModificationSystem.cs:19-26) a plain `UpdateAt` runs after every vanilla `UpdateAt` of the phase (UpdateSystem.cs:39-47,
158-161). It sees the tool output and the Deleted entities of this frame. It only sets the pending flag of the attractiveness system.
That system consumes the flag on its next 16-frame tick. Order against the bind trigger is not load-bearing (independent).

## Near SkiRunDrawFeedbackSystem

Slope heat-map overlay. Rendering follows ToolSystem -> ModificationSystem -> UIUpdateSystem in MainLoop (SystemOrder.cs:58, 60, 67,
68). So the Temp area built in Modification1 and triangulated in 2B is the final polygon of the frame. `OverlayRenderSystem`
(Rendering, SystemOrder.cs:687, 1.6.2 :688) consumes and clears the overlay lists each frame (OverlayRenderSystem.cs:781, 790). So our
strips must be added BEFORE it in the same phase.

## Near SkiRunTooltipSystem

Grade tooltip. `UITooltip` runs only from `TooltipUISystem`, which clears the mouse group first (TooltipUISystem.cs:48-55). So the
tooltip is re-added every frame. Anchoring after the vanilla area-tool tooltip (SystemOrder.cs:916, 1.6.2 :917) keeps our lines with
it. A plain `UpdateAt` would sort after every vanilla UITooltip system (add-index order, UpdateSystem.cs:39-47).

## Near SkiGladeTooltipSystem

Hint / unbound / complete / counts while the glade tool is active ("the glade tool is never silent"). `TempCostTooltipSystem`
(UITooltip, SystemOrder.cs:911, 1.6.2 :912) writes the vanilla cost line that our planted/felled Temps produce (it needs Temps,
TempCostTooltipSystem.cs:57). So anchoring after it keeps our lines directly below that line. A plain `UpdateAt` would sort after
every vanilla UITooltip system (UpdateSystem.cs:39-47). The UITooltip phase is already suppressed over UI (TooltipUISystem.cs:48-55).

## Near SkiRunContourSystem

`UndergroundViewSystem` (PreCulling, SystemOrder.cs:632, 1.6.2 :633) derives `contourLinesOn` from the public `globalContourLinesOn`
each frame (UndergroundViewSystem.cs:67, 120). So our write must land BEFORE it to take effect the same frame. PreCulling runs every
frame, paused included. The restore path needs this.

## Near SkiRunOutlineSystem

The setting changes thickness only (the 0.75 snapping of the game is untouched). The system draws its own outline and corner dots for
our four run prefabs. It mirrors `AreaBorderRenderSystem` (Rendering, SystemOrder.cs:679, 1.6.2 :680) in a Burst job. The anchor is
load-bearing for the `OverlayRenderSystem` reason: `OverlayRenderSystem` completes every registered buffer writer and then uploads and
clears the overlay lists each frame (OverlayRenderSystem.cs:738-741, 781, 790). So the job must be scheduled BEFORE it in the same
phase. Rendering follows ToolSystem -> ModificationSystem -> UIUpdateSystem in MainLoop (SystemOrder.cs:58, 60, 67, 68). So the Temp
areas of the frame are final. Rendering runs every frame, paused included. ONE registration only (`UpdateBefore`). An `UpdateAt` as
well would run it twice a frame (UpdateSystem.cs:158-176 -> :271-292). Per frame, when nothing is drawn, the system must cost only an
emptiness check.

## Near the hub systems (SkiSignNameSystem, SkiRunCatalogSystem, SkiResortStatsSystem, SkiResortHubUISystem)

All four are at UIUpdate, and the order of these lines is load-bearing. `UIUpdateSystem` drives UIUpdate in MainLoop every frame,
paused included (UIUpdateSystem.cs:17-20, SystemOrder.cs:67). It runs after ToolSystem and ModificationSystem (SystemOrder.cs:58, 60).
So the Created/Updated/Deleted tags of this frame are still visible (`CleanUpSystem` runs at Cleanup, SystemOrder.cs:54). This phase
ignores update intervals (UpdateSystem.cs:183-221), and a plain `UpdateAt` runs in add order (UpdateSystem.cs:158-161). Each frame:

1. `SkiSignNameSystem` first. It writes the sign auto-names that `SkiRunBindTriggerSystem` (ModificationEnd) queues.
   `NameSystem.SetCustomName` needs `EndFrameBarrier`, and the game refuses that barrier until `AllowBarrier<EndFrameBarrier>`
   (SystemOrder.cs:62, after ModificationSystem :60, and Game/SafeCommandBufferSystem.cs:16-23). UIUpdate (:67) is after it. We add it
   before the catalog and stats, so the stats rebuild of this frame reads the name.
2. The catalog (run rows + rating cache). It reads the hub's Demand of the previous frame, so it runs at a slower cadence while the
   panel is closed. The simulation reads its ratings.
3. `SkiResortStatsSystem` computes resort membership and every per-resort number. It aggregates them from the catalog Revision of
   this frame, the lift census, the attractiveness and snow systems (same Demand rule).
4. The hub pushes the stats revisions, applies the queued triggers, and sets both Demands for the next frame.

All lines are identical in 1.6.2. The hub is a `UISystemBase` with gameMode Game. It is disabled in the menu and the editor
(UISystemBase.cs:60-64). Its bindings are registered in `OnCreate`, which `UpdateAt` runs right here. `GameManager.InitializeUI`
precedes `InitializeModManager` (GameManager.cs:593, :618, 1.6.2 :599, :624), so `userInterface.bindings` exists. Its "Ski Resort
Hub" developer tab lives in `SkiRunCatalogSystem.Debug.cs`.

## Near LiftCensusSystem

The one lift model: topology of the `SkiChairliftHV` edges, stations, events, the "line opened" chair parking. At Modification4B the
Created/Updated/Deleted tags of this frame are visible, and Deleted entities still exist until Cleanup (SystemOrder.cs:50, 54). The
census must run BEFORE `ChairPinSystem` (UpdateAt Modification4B). Then its `RequestImmediateRebuild("lift topology")` is served in the
same frame. The loop over a deleted span is dropped this frame, not 30 frames later (`ChairPinSystem` cooldown). A mod-to-mod anchor
in the same phase works regardless of registration order. The anchor must stay at Modification4B. Otherwise the census silently never
runs.

## Near TramShuttleSystem

Moves the two cabins hosted on a tram base station along the two track ropes. It has no save data. At Modification4B:
- The cabins exist in their final vanilla state. `SubObjectSystem` (Modification2B, SystemOrder.cs:131) re-hosts them on a station
  update, and its barrier plays at the end of 2B (:88).
- The track-rope lanes are final (`LaneSystem`, Modification4, :155).
- Our Transform write lands before `Game.Objects.SearchSystem` (Modification5, :197), `UpdateGroupSystem` (:214) and PreCulling (:633).

The system runs AFTER `LiftCensusSystem`. So the census rows (Complete / Mixed / Revision) are the rows of this frame. The mod-to-mod
anchor recurses. UpdateSystem.cs:178-181 registers under the anchor (:278-292). `AddSystemUpdate` (:382-441) adds the same-phase
before-entries of a system, then the system itself, then its after-entries. The order is: census (anchored before ChairPin) -> this ->
`ChairPinSystem`. The phase must stay Modification4B. Otherwise the anchor is skipped and the system silently never runs.

## Near LiftTowerDiagSystem

Read-only tower diagnostic ("Towers [nodes]" blocks: node y / terrain / Elevation, chord clearance, the tower the engine spawned,
placeholder scores). `LiftCensusSystem.BuildCensus` (`OnCensusBuilt`) feeds it. It prints from here after the census has been quiet.
`DumpAll` on DUMP. The anchor is load-bearing like the anchor of `TramShuttleSystem`: AFTER `LiftCensusSystem` at Modification4B. Then
the towers of a line are readable (created at 2B, in the node SubObject buffers by Modification3, SystemOrder.cs:131, :136). The phase
must stay Modification4B. Otherwise the anchor is skipped and the system silently never prints.

## Near TowerNumberTileSystem

Per-tower number tiles, Secondary sub-objects of the line towers. `LiftCensusSystem` (`OnCensusBuilt`) feeds it. The anchor is
load-bearing: AFTER `LiftCensusSystem` at Modification4B (towers are in the node buffers by Modification3, SystemOrder.cs:136) and
BEFORE Modification5. There `SecondaryObjectReferencesSystem` files a Created tile into the SubObject buffer of its tower
(SystemOrder.cs:206). It writes nothing while the `SkiNumTile` prefabs are not loaded (the baked plates show).

## Near LiftTowerPlanOverlaySystem

Read-only overlay of the planned towers and rope of the selected lift line (the plan that `LiftTowerDiagSystem` logs as
"Towers [plan]"). The anchor is load-bearing for the `OverlayRenderSystem` reason. `OverlayRenderSystem` consumes and clears the
overlay buffers each frame (OverlayRenderSystem.cs:781, 790). So the lines must be added BEFORE it in the same phase. The
`LiftBuildAids` setting gates it inside the system.

## Near LiftTowerFitSystem

Writes the node positions, node y + `Game.Net.Elevation` and edge curves of each new / changed lift line, and tags them Updated.
`LiftCensusSystem` (`OnCensusBuilt`, 4B of an earlier frame) feeds it. The phase is load-bearing. Modification1 is the first phase of
the modification chain of the frame. So every consumer of the Updated tag runs after the write in the SAME frame:

- `GroundHeightSystem` (Modification2, SystemOrder.cs:117)
- `SubObjectSystem` (2B, :131: re-scores the tower ladder)
- `CompositionSelectSystem` (3)
- `Net.GeometrySystem` and `LaneSystem` (4)
- the census and `ChairPinSystem` (4B)

All of them run before `CleanUpSystem` wipes the tag (Cleanup, :54). A write at 4B would lose the tag unseen. No ordering inside
Modification1 is needed (its systems generate from tool definitions, and none reads permanent lift nodes). The `LiftTowerPlanner`
setting gates it inside the system.

## Near LiftLineToolSystem

ToolUpdate is where every vanilla tool lives (SystemOrder.cs:699-709). The phase is load-bearing. Its `CreationDefinition` +
`NetCourse` entities (ToolOutputBarrier ECB) must exist before `CourseSplitSystem` of PostTool (CourseSplitSystem.cs:4034). They must
also exist before `GenerateNodes` / `GenerateEdges` of Modification1/2 in the same frame. `ToolSystem` enables only the active tool
(ToolSystem.cs:312-327).

- The tool draws its overlay preview from ToolUpdate, before `OverlayRenderSystem` in Rendering clears the lists
  (OverlayRenderSystem.cs:738-790).
- The hotkey poller runs at UIUpdate (every frame, paused or not). An inactive tool never ticks (the `SkiGladeHotkeySystem`
  reasoning). Its Build request lands on the next frame of the tool.
- The tooltip runs at UITooltip after the vanilla cost tooltip (the `SkiLiftTooltipSystem` anchor: `TooltipUISystem` clears the
  mouse group first, TooltipUISystem.cs:48-55).
- Order between the three is not load-bearing (each reaches the tool via `GetOrCreateSystemManaged`).

## Near CatTrackGradeToolSystem

It uses the skeleton of the lift tool and the same three phases, for the same reasons.

- ToolUpdate: `ToolSystem` enables only the active tool (ToolSystem.cs:312-327). The overlay must be drawn before
  `OverlayRenderSystem` clears its lists in Rendering (OverlayRenderSystem.cs:738-790). Its apply writes the base heightmap on the
  main thread there. The only ECS output is the Deleted tag of the cleared trees, through `ToolOutputBarrier`, played back before
  Modification.
- UIUpdate: the hotkey poller (an inactive tool never ticks) also runs the follow-up checks of the dev-tab probe.
- UITooltip after the vanilla cost tooltip (`TooltipUISystem` clears the mouse group first, TooltipUISystem.cs:48-55).
- Order between the three is not load-bearing.

## Near LiftParkSystem

The census (4B) only queues the surviving sections of an opened lift. `EndFrameBarrier.CreateCommandBuffer` throws at Modification4B
(Game/SafeCommandBufferSystem.cs:16-23). The barrier is the first MainLoop system (SystemOrder.cs:49). Only
`AllowBarrier<EndFrameBarrier>` re-allows it (:62, after ModificationSystem :60, same in 1.6.2). UIUpdate (:67) is after :62. So
this system creates the `{Event, SubObjectsUpdated}` entities there. They play back at the `EndFrameBarrier` of the next frame, and
`SubObjectSystem` consumes them at Modification2B of that frame. Order within UIUpdate is not load-bearing. Without this
registration, the census logs one "Lift [park] ... not running" WARN per load and nothing is parked.

## Near SkiStationAlignSystem

Guide lines for the station being placed or nudged. The anchor is load-bearing for the `SkiRunDrawFeedbackSystem` reason:
`OverlayRenderSystem` (Rendering, SystemOrder.cs:687, 1.6.2 :688) consumes and clears the overlay buffers each frame
(OverlayRenderSystem.cs:781, 790). So our lines must be added BEFORE it in the same phase. Order between the two mod systems
anchored there is not load-bearing (separate lines). The `LiftBuildAids` setting gates it inside the system.

## Near SkiPisteMapSystem

The anchor is load-bearing for the `SkiRunDrawFeedbackSystem` reason. `OverlayRenderSystem` (Rendering, SystemOrder.cs:687, 1.6.2
:688) completes every registered writer and then uploads and clears the overlay lists each frame (OverlayRenderSystem.cs:738-741,
781, 790). So we must schedule our job BEFORE it in the same phase. The system reads the hub's state, which the hub writes at UIUpdate
earlier in the same MainLoop frame (SystemOrder.cs:67-68). Rendering runs paused included.

## Near SkiSnowLineOverlaySystem

Winter snow line while placing. The anchor is load-bearing for the `SkiPisteMapSystem` reason: `OverlayRenderSystem` uploads and
clears the overlay lists each frame (OverlayRenderSystem.cs:738-741, 781, 790). So our main-thread `DrawLine` writes must land BEFORE
it in the same phase. ONE anchor only (each `UpdateBefore` call adds an entry, UpdateSystem.cs:173-176). Rendering runs paused
included. The `ShowSnowLineWhilePlacing` setting gates it inside the system.

## Near SkiRunLabelSystem

Run names on the hill. NO anchor to `OverlayRenderSystem`. The labels are `Graphics.DrawMesh` calls with the TextMeshPro mesh and
overlay text material of the game, and nothing is written to the overlay buffers. So a plain `UpdateAt` is correct. The system must
only run after the UIUpdate-phase rebuilds of the catalog in the same frame, and Rendering does (SystemOrder.cs:67-68). Rendering
runs paused included. The `ShowRunNamesOnTheHill` setting gates it inside the system.

## Near ResortPlanLabelSystem

Resort Planner plan labels (one per lift and per pod). Same rule as the run names: NO anchor to `OverlayRenderSystem` (the labels are
`Graphics.DrawMesh` calls, nothing is written to the overlay buffers). Use a plain `UpdateAt` at Rendering. The system is a stub until
stage A4 of the Resort Planner (EngineNotes/ResortPlanner.md). Registering it now keeps `Mod.cs` out of the file lists of the later
tasks.

## Near SkiSnowLineTooltipSystem

The anchor is load-bearing like the anchor of `SkiRunTooltipSystem`: `UITooltip` runs only from `TooltipUISystem`, which clears the
mouse group first (TooltipUISystem.cs:48-55). The system is anchored after the run tooltip (itself after `AreaToolTooltipSystem`,
SystemOrder.cs:916). So the line follows the rows of the run instead of sorting after every vanilla UITooltip system
(UpdateSystem.cs:39-47). It reads `SkiSnowLineOverlaySystem.Showing`, which Rendering wrote in the previous frame (one frame of lag,
as the readout of the run tooltip).

## Near SkiLiftTooltipSystem

Alignment readout + bulldozer warning. Anchoring after the vanilla `TempCostTooltipSystem` (SystemOrder.cs:911, 1.6.2 :912) keeps
our lines with the vanilla cost line, as `SkiGladeTooltipSystem` does. A plain `UpdateAt` would sort after every vanilla UITooltip
system (UpdateSystem.cs:39-47). The `LiftBuildAids` setting gates it inside the system.

## Near SkiPlacementInfoviewSystem

Closes the Electricity infoview that the lift LINE tool opens (once per activation, kept when the player had it open, and the base
station keeps it). The registration is load-bearing for two reasons:

- `UpdateBefore` ONLY. An `UpdateAt` as well would register the system a second time and run it twice per frame
  (UpdateSystem.cs:158-176 -> Register :271-292 appends one entry per call).
- BEFORE `InfoviewsUISystem` (UIUpdate, SystemOrder.cs:939, 1.6.2 :940), so the vanilla infoview binding reflects our close in the
  same frame.

`ToolSystem` (MainLoop, SystemOrder.cs:58) has already applied the tool infoview of this frame before UIUpdate (:67). So the system
sees the view that the tool just opened.

## Near LodgeRoadSystem

Clears `NoRoadConnection` (sets `RequireRoad`) and `OwnerSide` on the lodge PREFAB at every Game-mode load. This is the state that the
game gives an upgrade that is allowed a road (BuildingInitializeSystem.cs:177-188). An Editor load after a game in the same app run
writes the values of the game back. Registered like `CursorAimSystem`. The phase is not load-bearing: `BuildingInitializeSystem`
runs only on Created prefab entities, in PrefabUpdate (SystemOrder.cs:1018, 1.6.2 :1019), long before a city load.

## Near MountainJobsSystem

Gives the jobs of the roadless hut / summit / patrol buildings to the `WorkProvider` of their linked Welcome Sign (the runtime path of
the ServiceUpgrade combine, ServiceUpgradeSystem.cs:137-154 + CityServiceWorkplaceInitializeSystem.cs:97-121). It polls the published
top-level links every 64 frames. The phase is NOT load-bearing. The tick of `WorkProviderSystem` (GameSimulation, slice every 512
frames) reads `m_MaxWorkers` whenever it runs. Nothing vanilla rewrites it for a city service building after its Created frame.

## Near OffSeasonStaffSystem

Lodges and the ski school keep half their workplaces while their ski area has had no open run for 12 game hours (`OffSeasonRules`).
`MountainJobsSystem` halves the hut / summit sign jobs through `IsHalf`. The system polls every 256 frames. The phase is NOT
load-bearing, for the same reason as `MountainJobsSystem` (`WorkProviderSystem` reads `m_MaxWorkers` whenever it runs).

## Near LodgeSupplySystem

The road-optional lodges' city utilities, every frame. Modification1 is load-bearing. Its `Updated` tag must reach
`Game.Buildings.InitializeSystem` in Modification2 of the same frame (SystemOrder.cs:118). ModificationSystem.cs:19-26 runs the phases
in order. The tag must arrive before the cleanup collects it (SystemOrder.cs:50-54, PrepareCleanUpSystem.cs:48-52). ModificationSystem
runs in MainLoop (SystemOrder.cs:60), so this runs paused or not. Details: LodgeSupplySystem.md.

## Near StationLvIconSystem

Removes the vanilla `LowVoltageNotConnected` icon from a station whose LV stub has a wire. `ConnectionWarningSystem` counts only
edges AT the stub node. So a wire that local-connected onto the middle of the stub powers the station but leaves the icon (see the
`StationLvRule` header). The phase is NOT load-bearing: `IconCommandSystem` consumes the queued removes at ModificationEnd
(SystemOrder.cs:271) whenever it runs next.

## Near SkiRunAreaTypeSystem

Forges `AreaGeometryData.m_Type` Surface -> Space on the `SkiRunCatTrackA` prefab at every Game-mode load, so a cat track can cross
other runs (ValidationHelpers.cs:551 compares only the type). Registered like `LodgeRoadSystem`. The phase is not load-bearing:
`AreaInitializeSystem` fills the type on the Created prefab entity long before a city load.

## Near SkiLodgeSpotDiag

Read-only lodge spawn-spot diagnostic ("Lodge [spots]"). It prints the `Game.Buildings.SpawnLocationElement` buffer of each lodge as the
engine registered it: one INFO line per load and per change, and the per-spot lines on the hub DUMP. Phase and order are NOT
load-bearing. The system only reads. The producers all run before GameSimulation in a frame or before `OnGameLoadingComplete`:
- `Game.Buildings.ReferencesSystem` at Modification4B (SystemOrder.cs:176)
- `SpawnLocationConnectionSystem` at Modification5 (:210)
- the load-time rebuild `SpawnLocationElementSystem` at Deserialize (:818)
