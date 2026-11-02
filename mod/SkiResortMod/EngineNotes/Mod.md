# Mod.cs: why each system runs in its update phase and anchor

`Mod.OnLoad` registers every system with `UpdateSystem` (`UpdateAt` / `UpdateBefore` / `UpdateAfter`). The one-line comments in
`Mod.cs` say which registrations are load-bearing; this note holds the engine reasoning behind them. Citations are `File.cs:line`
in the 1.6.0f1 decompile unless a 1.6.2 line is given. Where a line says "same in 1.6.2" the cited lines are identical there.

## Shared idioms

- A plain `UpdateAt` sorts after every vanilla `UpdateAt` of its phase (mods register after `SystemOrder.Initialize`; add-index
  order, UpdateSystem.cs:39-47, :158-161). An anchorless `UpdateBefore` sorts ahead of every `UpdateAt` (SystemOrder.cs:49,
  UpdateSystem.cs:39-47, :163-166).
- Each `UpdateBefore` / `UpdateAfter` call adds one entry (UpdateSystem.cs:173-176, :271-292), so a system gets ONE registration:
  an `UpdateAt` as well would run it twice a frame.
- Anchoring a mod system to a mod system works regardless of registration order: `UpdateBefore<T, Anchor>` files T under the anchor
  in `UpdateSystem`'s reference map and Refresh emits it right before the anchor when both are in the same phase (UpdateSystem.cs:
  173-176, :278-292, :388-413). A mod-to-mod anchor recurses: `AddSystemUpdate` (:382-441) adds a system's same-phase before-entries,
  then itself, then its after-entries. If the anchor's phase differs from the registered phase, the anchor is skipped and the system
  silently never runs.
- A follower inherits its anchor's update offset only when the update intervals match (UpdateSystem.cs:400-431). Never override
  `GetUpdateOffset` on a follower.
- Update intervals are ignored for phases driven by the non-indexed `UpdateSystem.Update(phase)` (ModificationSystem, UIUpdate;
  UpdateSystem.cs:183-221).
- MainLoop order: the `EndFrameBarrier` playback comes first (SystemOrder.cs:49), then ToolSystem (:58), ModificationSystem (:60,
  ModificationSystem.cs:19-26), UIUpdateSystem (:67, UIUpdateSystem.cs:17-20), then Rendering (:68). `CleanUpSystem` strips
  Created/Updated and destroys Deleted at Cleanup (SystemOrder.cs:54; CleanUpSystem.cs:52-53). GameSimulation runs later, from
  LateUpdate (SystemOrder.cs:74; SimulationSystem.cs:286), and does nothing while paused (zero iterations, SimulationSystem.cs:221-223).
- UITooltip: `TooltipUISystem` clears the mouse group first each frame (TooltipUISystem.cs:48-55), so every tooltip is re-added every
  frame; the phase is already suppressed over UI. To keep our lines next to a vanilla tooltip's, anchor after that vanilla system
  instead of a plain `UpdateAt`.
- `OverlayRenderSystem` (Rendering, SystemOrder.cs:687, 1.6.2 :688) completes every registered buffer writer, then uploads and clears
  the overlay lists each frame (OverlayRenderSystem.cs:738-741, 781, 790). Anything that draws overlay lines must be scheduled
  `UpdateBefore<..., OverlayRenderSystem>` in Rendering. Rendering runs every frame, paused included.
- Systems that only mutate prefab data (`CursorAimSystem`, `LodgeRoadSystem`, `SkiRunAreaTypeSystem`) are registered disabled and
  act in `OnGameLoadingComplete` only; load hooks fire for a disabled system (GameSystemBase.cs:27-29, :60-70).

## Near SkiResortAttractivenessSystem

`AttractionSystem` rewrites `AttractivenessProvider.m_Attractiveness` for every building carrying the component on a 16-frame cycle
(1/16 of buildings per tick, sliced by the `UpdateFrame` shared component), so a write once per game hour would be clobbered within
16 frames. We mirror vanilla's cadence (interval 16, inherited offset) with `UpdateAfter<TSystem, TAnchor>`, so each tick our write
lands after vanilla's for the same `UpdateFrame` slice and wins.

## Near SnowLineSystem

Snow-line / climate oracle: pull-based, `Enabled = false`, no per-tick work. It is the one read of snow and season gating, the
snowmaking margin, the ambient (`ClimateSystem.SampleClimate` at the current date) and the cached 24-hour day mean, pulled by the
attraction score, the snow injection, the run decals and splats, and the hub. It logs the climate constants once per load.

## Near SkiPrefabCatalogSystem

Every lift, station, lodge, sign and run prefab name lives in the pure `SkiCatalog.cs`; this system resolves them lazily on the
first query after each Game / Editor load and revalidates cached prefab entities once per frame (`PrefabSystem.UpdatePrefabs` gives
an updated prefab a new entity and tags the old one Deleted, PrefabSystem.cs:766-824). It writes one `PrefabCatalog [arm]` line per
load.

- `OnGamePreload` invalidates for those modes only. A main-menu preload keeps the last load's views: GameManager.cs:1023 fires it
  before every load's deserialization, :1126 completes the load.
- Registered like `CursorAimSystem`: disabled, no per-frame work. The phase is not load-bearing (load hooks fire for a disabled
  system, GameSystemBase.cs:27-29, :60-70), and consumers reach it with `GetOrCreateSystemManaged` in their `OnCreate`, so
  registration order is not load-bearing either.
- It covers 30 names (4 lift types with line + carrier each, the marker, 8 stations, 5 lodge tiers, the school, 2 patrol huts, the
  sign, 4 runs) and the one 25-row build-item table; the counts are computed in its "created" and [arm] lines.

## Near ChairPinSystem

`SubObjectSystem` spawns/moves chairs at Modification2B; `LaneSystem` finalizes the cable-lane curves at Modification4
(`ModificationBarrier4`). At Modification4B both exist in their final state, and our Transform write lands before
`Game.Objects.SearchSystem` (Modification5) and PreCulling consume it.

## Near PowerBridgeSystem

Our stations' owned 3 m connection stub fails vanilla `ElectricityBuildingGraphSystem`'s orphan gate, so that system never registers
the station's electricity connection (and actively deletes it on updates). `PowerBridgeSystem` bootstraps
`ElectricityValveConnection` onto the stub nodes so the gate's OTHER arm passes. It must run BEFORE the vanilla system in the same
phase (Modification4B) so the valve is visible the very frame a station is created/updated; the anchored registration guarantees it.

## Near LiftReconnectSystem

Placing a station onto an existing lift line's end never re-triggers lane building (`LaneSystem` only processes Updated owners), so
cables stayed undrawn. `LiftReconnectSystem` marks the connection stub's net entities Updated on station events. The `SubNet` buffer
is populated by `SubNetReferencesSystem` at Modification4 and `LaneSystem` consumes the tags later in that same phase, so the system
anchors directly before `LaneSystem` inside Modification4.

## Near CursorAimSystem

`NetToolSystem` reads the drawn prefab's `NetGeometryData.m_DefaultSurfaceHeight.max` as the ray offset; the prefab-side value is
hard-coupled to center(HeightRange) = 13. `CursorAimSystem` rewrites the prefab field to the tower head once per load: data only, no
per-frame work (registered like `SnowLineSystem`: disabled, `OnGameLoadingComplete` only).

## Near SkiRunSplatSystem

Mirrors run polygons into the terrain-material user splatmap (polygons are data, the splat is paint). PreCulling runs every frame in
Game AND Editor, paused included: area edits arrive through tool phases that ignore simulation pause, and the Created / Updated /
Deleted tags live until `CleanUpSystem` (Cleanup phase), so a per-frame phase reliably sees every event. Anchored before the vanilla
`TerrainMaterialSystem` so our `ApplyBrush` dabs execute before the same frame's splat cascade update composes them.

## Near SkiSnowInjectionSystem

Tops up run-shaped snow in `SnowSystem`'s own depth buffer.

- Phase: GameSimulation is `SnowSystem`'s own phase, so it only ticks while the sim advances and only in Game action mode; pause
  gating and editor inertness come from the phase itself.
- Anchor: the write must land after the frame's ping-pong flip so injected texels are consumed as `_Previous` by the NEXT `AddSnow`.
  Anchored registration inherits `SnowSystem`'s 4-frame offset slot, and the system self-gates its 64-frame top-up cadence off
  `SimulationSystem.frameIndex`.

## Near SkiRunPropSystem

Vanilla area props (`AreaSubObjects`, one border prop per polygon vertex) are lot-class-gated: `LotPrefab`'s archetype hard-adds the
runtime `Game.Objects.SubObject` buffer the spawner queries for, `SurfacePrefab`'s does not. This system adds that buffer (and ensures
Updated) on OUR run surfaces, stops the per-node props and walks the border itself (EngineNotes/SkiRunPropSystem.md).

- Phase is load-bearing twice over: `SecondaryObjectSystem` consumes the Updated tag at Modification4B and Cleanup wipes the tag every
  frame, so the bridge must land earlier in the SAME frame's Modification4B; it anchors directly before the vanilla spawner (the
  `PowerBridgeSystem` precedent).
- Game mode only: spawned props must never be baked into maps.
- It also writes the Options prop density into the run prefabs and, after a settled slider change, tags `ChangedDistrict` on our
  propped runs; both must land before the vanilla spawner too.

## Near SkiNightLightSystem

Nothing in the engine re-evaluates a Night-gated `LightEffect` on a static area sub-object: a control-mast test in the field left
the magenta light stuck ON all day until a tool hover rebuilt the props. This system stamps `EffectsUpdated` on run-owned props at
every day/night flip (it is our `StreetLightSystem`). Anchor AND interval are load-bearing: we read the flag `EffectFlagSystem` just
wrote (public `GetData`), and offset inheritance requires equal intervals (2048).

## Near SkiSnowGunPlumeSystem

Sets each gun's plume lease (`EarlyDisasterWarningDuration`, read by the Disaster condition of our `SkiSnowGunPlume` EffectPrefab)
from the injection's per-run "guns running" flag and the gun's own elevation, and stamps `EffectsUpdated` on a flip.

- Phase is load-bearing: GameSimulation (LateUpdate) lets the tag live into the NEXT frame's PreCulling, where `EffectControlSystem`
  consumes it, before Cleanup strips it (the `SkiNightLightSystem` precedent). Pause freezes the plumes with the snow.
- The anchor after the injection is NOT load-bearing: it reads flags the injection published, at most one 64-frame window old.
  Intervals differ, so no offset is inherited.
- Game mode only.

## Near SkiRunSurfaceSystem

`SurfacePrefab` adds `Game.Areas.Surface`, an EMPTY TAG, not the `Game.Objects.Surface` weather struct the renderer's owner-walk
looks for, so props parented to our runs render snow-free in every season. This system gives run areas that component and tracks it
to measured run depth. BOTH the anchor and the interval are load-bearing: `BatchDataSystem` only re-reads surface wetness when
`WetnessSystem`'s `LastSystemVersion` moves (so we must write in vanilla's own tick), and `UpdateSystem` inherits an anchor's offset
only when the intervals match, hence `GetUpdateInterval` == `WetnessSystem`'s 256.

## Near SkiRunUpkeepSystem

Run operating costs (snowmaking > groomed > natural = free). Bills `PlayerMoney` on the City entity from the injection system's
per-type ACTIVE thermal mask, so money, visuals and attractiveness share one climate model. There is no budget-panel line item:
`ExpenseSource` is a closed enum and extending it needs Harmony (banned). GameSimulation means pause stops the meter and the Editor
never bills. Anchored after the injection system so a same-frame top-up publishes its mask before the charge pass reads it.

## Near SkiGuestCounterSystem

The anchor is load-bearing for freshness, not for exactly-once: `MeetingSystem` (UpdateAt GameSimulation, SystemOrder.cs:404;
interval 16, MeetingSystem.cs:277-280) moves a meeting only on its own updates. With the same interval we inherit its offset
(UpdateSystem.cs:400-431) and read the state its update just wrote. The scans are IJobs; nothing waits on `MeetingSystem`'s job on
the main thread.

## Near SkiResortHistorySystem

Keeps one saved sample per ski area per closed month on its Welcome Sign, and mirrors the month in progress onto the sign (seeded
back on load). Anchor AND interval are load-bearing: it reads the counter's `LastMonthStep` and `Ledger` right after the counter's
`AdvanceMonth`; with the counter's interval (16) it inherits the counter's offset (UpdateSystem.cs:400-431) and runs in the same frame
after it, so the first tick after a load seeds the ledger before anything mirrors over the save. GameSimulation: pause stops it; the
Editor never arms it.

## Near SkiDaySystem

Claim at decision: for a share of residents at home and hotel tourists who just decided to go out (Leisure with no venue yet), name
a ski-day lodge with `LeisureSystem`'s own two writes (`Leisure.m_TargetAgent` + `TripNeeded { lodge, Leisure, 128 }`,
LeisureSystem.cs:398-405, 1.6.2 :392-399) and, for a far trip with a free household car, the public car-reserve queue
(CitizenBehaviorSystem.cs:1094-1103, 1.6.2 :1126-1135; WorkerSystem.cs:450-454).

- Anchor AND interval are load-bearing: `LeisureSystem` is UpdateAt GameSimulation (SystemOrder.cs:377, 1.6.2 :378);
  `GetUpdateInterval` returns `LeisureSystem`'s own (262144 / kUpdatePerDay = 64, LeisureSystem.cs:914-917, 1.6.2 :911-913), so the
  follower inherits its offset (UpdateSystem.cs:431-436, :355-356, :457-516) and every pass runs on `LeisureSystem`'s frames,
  before `TripNeededSystem` (SystemOrder.cs:393, 1.6.2 :394). NEVER override `GetUpdateOffset`.
- `Leisure` and `TripNeeded` are read-write in the job: ECS orders it after `LeisureSystem`'s job and `TripNeededSystem`'s after it,
  no main-thread wait.
- Registered before `SkiResortStatsSystem`, so this line creates it (UpdateSystem.cs:178-181).
- Pause stops it; the Editor never arms it. ONE registration only.

## Near SkiRunOwnershipSystem

Supplies the one-time bind charge and the 5 km tether that vanilla only implements for OBJECT ServiceUpgrades (area upgrades are a
menu entry with no install path; all four writers are Object-gated, see the system header). A run carries no vanilla Owner and the
sign no `Game.Areas.SubArea` buffer: the link is our own serialized `SkiRunBinding`. An Owner on a run let the game-mode bulldozer's
raycast walk from one run to the sign (Game.Areas/RaycastJobs.cs:80-93) and delete the whole resort (BulldozeToolSystem.cs:664);
both are unchanged in 1.6.2. The sign-to-runs delete cascade is ours (`SkiRunGateCascadeSystem`). Anchored before the injection
system in the same phase so a bind and the first snow of a freshly bound run can land in one 64-frame window.

## Near SkiRunDecalSystem

Probes and mutates the LIVE "Area batch (<run surface>)" material clones: the alpha-coverage decal texture pairs, the shader's
built-in snow slots (the winter look rides the game's snow sim) and conservative doc-safe scalars. Anchored AFTER the vanilla
`AreaBatchSystem` in PreCulling so that the frame a batch clone is lazily created, our find-retry can already see it. PreCulling runs
every frame in Game AND Editor, paused included, so the retry countdown and re-assert sentinel tick.

## Near SkiGladeToolSystem

Registration order between the four glade systems (tool, hotkey, tag, cleanup) is not load-bearing: `UpdateAt` creates each system
immediately (UpdateSystem.cs:158-161) and the other three reach the tool through `GetOrCreateSystemManaged` anyway; the tool goes first
only so its "created" log line precedes its dependents'.

ToolUpdate is where every vanilla tool lives (SystemOrder.cs:699-709; 1.6.2 :700-710). `ToolBaseSystem.OnCreate` adds it to
`ToolSystem.tools` and sets `Enabled = false` (ToolBaseSystem.cs:310, 315), and `ToolSystem.ToolUpdate` enables ONLY the active tool
(ToolSystem.cs:312-327), so the tool ticks only while active and cannot poll its own toggle key.

## Near SkiGladeHotkeySystem

Defaults: Ctrl+Shift+G toggle, Shift+G next tree density 1-8, Alt+G run interior / tree line, Ctrl+Alt+G dump diagnostics. Phase is
load-bearing: UIUpdate is driven by `UIUpdateSystem` in MainLoop (SystemOrder.cs:67, UIUpdateSystem.cs:19), i.e. every frame, paused
or not. GameSimulation would stop while paused, and the tool's own phase only ticks while it is active. UIUpdate also precedes
Rendering (SystemOrder.cs:67-68), so a dump pressed with a run on the cursor reads the previous frame's live readout.

## Near SkiGladeTagSystem

The tag must land on the Temp trees BEFORE `ApplyObjectsSystem` turns them permanent in the same ApplyTool phase (SystemOrder.cs:714;
1.6.2 :715). Create keeps the entity and only swaps Temp for Applied/Created/Updated (ApplyObjectsSystem.cs:670-689), so the tag
carries into the permanent tree.

## Near SkiGladeCleanupSystem

Member cleanup when a run is deleted: directly (bulldozer, area tool, info panel) or by `SkiRunGateCascadeSystem` when its sign is
deleted (registered right below, anchored BEFORE this system). There is no vanilla SubArea cascade: runs carry no Owner and the sign
no SubArea buffer, so `SubElementDeleteSystem` (PostTool, SystemOrder.cs:724) and `BulldozeToolSystem`'s sub-area walk never reach a
run. Phase is load-bearing: Modification2 runs paused or not and sees the same frame's Deleted tags (they live one frame, wiped at
Cleanup), after PostTool (ToolSystem runs PostTool at ToolSystem.cs:257 and precedes ModificationSystem in MainLoop, SystemOrder.cs:
58, 60; ModificationSystem.cs:20).

## Near SkiRunGateCascadeSystem

When a Ski Area Welcome Sign gets Deleted, every run bound to it (`SkiRunBinding.m_Gate`) gets Deleted the same frame. Phase and
anchor are load-bearing:

- A bulldozed sign is tagged Deleted at ApplyTool inside ToolUpdate (ApplyObjectsSystem.cs:484-488); a collapsed one by the
  `EndFrameBarrier`'s playback at the start of MainLoop, ahead of ToolSystem (CollapsedBuildingSystem.cs:150-175 via :261/:318; an
  anchorless `UpdateBefore` sorts ahead of every `UpdateAt`, SystemOrder.cs:49, UpdateSystem.cs:39-47, :163-166). MainLoop then runs
  ToolSystem and ModificationSystem (SystemOrder.cs:58, 60) and Modification2 inside it (ModificationSystem.cs:19-26), so both are
  visible here.
- Modification1/2 hold no vanilla area-delete consumer (SystemOrder.cs:94-121).
- `UpdateBefore<SkiGladeCleanupSystem>` makes the sign, runs, member trees chain complete in one frame. The runs' border props are
  Secondary sub-objects deleted later in the frame by `SecondaryObjectSystem` at Modification4B (SecondaryObjectSystem.cs:728-740,
  SystemOrder.cs:183); everything is destroyed in the same Cleanup (SystemOrder.cs:50, 54). All lines identical in 1.6.2.
- The anchor must stay at Modification2 or the cascade silently never runs (see the mod-to-mod anchor idiom above; precedent:
  `SkiRunUpkeepSystem` after `SkiSnowInjectionSystem`).

## Near the run-binding and drawing-feedback systems

Each is gated per frame on its own setting (Settings > Runs) and on the ownership system: the bind trigger and the contour lines on
`SkiRunOwnershipSystem.IsArmed`, the drawing feedback on `PrefabSetsReady` (a binding exception must not switch the slope feedback
off). All four reach the ownership system through `GetOrCreateSystemManaged`, so registration order between them is not load-bearing.

## Near SkiRunBindTriggerSystem

ModificationEnd runs in MainLoop, paused or not (SystemOrder.cs:58, 60; ModificationSystem.cs:19-26), unlike the GameSimulation sweep
(zero iterations while paused, SimulationSystem.cs:221-223).

- The finished run already lost Temp and gained Applied/Created/Updated at the `ToolOutputBarrier` inside ToolUpdate
  (ApplyAreasSystem.cs:204-208, 272, 297-299; SystemOrder.cs:694-695, 1.6.2 :695-696). `GeometrySystem` re-triangulated it at
  Modification2B (SystemOrder.cs:124; GeometrySystem.cs:752, 780). Created lives until Cleanup (CleanUpSystem.cs:53).
- A plain `UpdateAt` sorts after every vanilla `UpdateAt` of the phase, so every vanilla Modification system has already run and the
  run's Geometry center is final. The bind adds only our own `SkiRunBinding`, a structural change that raises no tag, so vanilla
  systems still see nothing (no Owner, no SubArea).
- The sign arm reacts to a Created sign only. No update interval: ModificationSystem's non-indexed `UpdateSystem.Update(phase)`
  ignores it (UpdateSystem.cs:183-221).

## Near SkiResortAttractivenessWatchSystem

Phase is load-bearing, for the bind trigger's reason: `CleanUpSystem` strips Created/Updated and destroys Deleted entities at
Cleanup (GameManager.cs:711 -> :2399; SystemOrder.cs:54; CleanUpSystem.cs:52-53) BEFORE GameSimulation runs from LateUpdate
(GameManager.cs:721 -> :2407; SystemOrder.cs:74; SimulationSystem.cs:286; 1.6.2 :721/:2414, :731/:2422), so
`SkiResortAttractivenessSystem` (GameSimulation, anchored above) never sees a placement. At ModificationEnd (MainLoop, paused
included; ModificationSystem.cs:19-26) a plain `UpdateAt` runs after every vanilla `UpdateAt` of the phase (UpdateSystem.cs:39-47,
158-161) and sees this frame's tool output and Deleted entities; it only sets the attractiveness system's pending flag, consumed on
its next 16-frame tick. Order against the bind trigger is not load-bearing (independent).

## Near SkiRunDrawFeedbackSystem

Slope heat-map overlay. Rendering follows ToolSystem -> ModificationSystem -> UIUpdateSystem in MainLoop (SystemOrder.cs:58, 60, 67,
68), so the Temp area built in Modification1 and triangulated in 2B is the frame's final polygon. `OverlayRenderSystem` (Rendering,
SystemOrder.cs:687, 1.6.2 :688) consumes and clears the overlay lists each frame (OverlayRenderSystem.cs:781, 790), so our strips
must be added BEFORE it in the same phase.

## Near SkiRunTooltipSystem

Grade tooltip. `UITooltip` runs only from `TooltipUISystem`, which clears the mouse group first (TooltipUISystem.cs:48-55), so the
tooltip is re-added every frame. Anchoring after the vanilla area-tool tooltip (SystemOrder.cs:916, 1.6.2 :917) keeps our lines with
it; a plain `UpdateAt` would sort after every vanilla UITooltip system (add-index order, UpdateSystem.cs:39-47).

## Near SkiGladeTooltipSystem

Hint / unbound / complete / counts while the glade tool is active ("the glade tool is never silent"). `TempCostTooltipSystem`
(UITooltip, SystemOrder.cs:911, 1.6.2 :912) writes the vanilla cost line our planted/felled Temps produce (it needs Temps,
TempCostTooltipSystem.cs:57), so anchoring after it keeps our lines directly below that line; a plain `UpdateAt` would sort after
every vanilla UITooltip system (UpdateSystem.cs:39-47). The UITooltip phase is already suppressed over UI (TooltipUISystem.cs:48-55).

## Near SkiRunContourSystem

`UndergroundViewSystem` (PreCulling, SystemOrder.cs:632, 1.6.2 :633) derives `contourLinesOn` from the public `globalContourLinesOn`
each frame (UndergroundViewSystem.cs:67, 120), so our write must land BEFORE it to take effect the same frame. PreCulling runs every
frame, paused included, which the restore path needs.

## Near SkiRunOutlineSystem

The setting changes thickness only (the game's 0.75 snapping is untouched). The system draws its own outline and corner dots for our
four run prefabs, mirroring `AreaBorderRenderSystem` (Rendering, SystemOrder.cs:679, 1.6.2 :680) in a Burst job. The anchor is
load-bearing for the `OverlayRenderSystem` reason: it completes every registered buffer writer and then uploads and clears the
overlay lists each frame (OverlayRenderSystem.cs:738-741, 781, 790), so the job must be scheduled BEFORE it in the same phase.
Rendering follows ToolSystem -> ModificationSystem -> UIUpdateSystem in MainLoop (SystemOrder.cs:58, 60, 67, 68), so the frame's Temp
areas are final, and it runs every frame, paused included. ONE registration only (`UpdateBefore`; an `UpdateAt` as well would run it
twice a frame, UpdateSystem.cs:158-176 -> :271-292). Per frame, when nothing is drawn, the system must cost only an emptiness check.

## Near the hub systems (SkiSignNameSystem, SkiRunCatalogSystem, SkiResortStatsSystem, SkiResortHubUISystem)

All four at UIUpdate, and the order of these lines is load-bearing. UIUpdate is driven by `UIUpdateSystem` in MainLoop every frame,
paused included (UIUpdateSystem.cs:17-20, SystemOrder.cs:67), after ToolSystem and ModificationSystem (SystemOrder.cs:58, 60), so this
frame's Created/Updated/Deleted tags are still visible (`CleanUpSystem` runs at Cleanup, SystemOrder.cs:54). Update intervals are
ignored in this phase (UpdateSystem.cs:183-221) and a plain `UpdateAt` runs in add order (UpdateSystem.cs:158-161). Each frame:

1. `SkiSignNameSystem` first: it writes the sign auto-names that `SkiRunBindTriggerSystem` (ModificationEnd) queues, because
   `NameSystem.SetCustomName` needs `EndFrameBarrier`, which is refused until `AllowBarrier<EndFrameBarrier>` (SystemOrder.cs:62, after
   ModificationSystem :60; Game/SafeCommandBufferSystem.cs:16-23); UIUpdate (:67) is after it. Added before the catalog and stats so
   this frame's stats rebuild reads the name.
2. The catalog (run rows + rating cache). It reads the hub's Demand of the previous frame, so it runs at a slower cadence while the
   panel is closed; the simulation reads its ratings.
3. `SkiResortStatsSystem`: resort membership and every per-resort number, aggregated from this frame's catalog Revision, the lift
   census, the attractiveness and snow systems (same Demand rule).
4. The hub: pushes the stats revisions, applies the queued triggers, sets both Demands for the next frame.

All lines identical in 1.6.2. The hub is a `UISystemBase` with gameMode Game: disabled in the menu and the editor
(UISystemBase.cs:60-64). Its bindings are registered in `OnCreate`, which `UpdateAt` runs right here: `GameManager.InitializeUI`
precedes `InitializeModManager` (GameManager.cs:593, :618; 1.6.2 :599, :624), so `userInterface.bindings` exists. Its "Ski Resort
Hub" developer tab lives in `SkiRunCatalogSystem.Debug.cs`.

## Near LiftCensusSystem

The one lift model: topology of the `SkiChairliftHV` edges, stations, events, the "line opened" chair parking. At Modification4B this
frame's Created/Updated/Deleted tags are visible and Deleted entities still exist until Cleanup (SystemOrder.cs:50, 54). The census
must run BEFORE `ChairPinSystem` (UpdateAt Modification4B) so its `RequestImmediateRebuild("lift topology")` is served in the same
frame: the loop over a deleted span is dropped this frame, not 30 frames later (`ChairPinSystem` cooldown). A mod-to-mod anchor in
the same phase works regardless of registration order, but the anchor must stay at Modification4B or the census silently never runs.

## Near TramShuttleSystem

Moves the two cabins hosted on a tram base station along the two track ropes; no save data. At Modification4B the cabins exist in
their final vanilla state (`SubObjectSystem`, Modification2B, SystemOrder.cs:131, re-hosts them on a station update and its barrier
plays at the end of 2B, :88), the track-rope lanes are final (`LaneSystem`, Modification4, :155), and our Transform write lands
before `Game.Objects.SearchSystem` (Modification5, :197), `UpdateGroupSystem` (:214) and PreCulling (:633). AFTER `LiftCensusSystem`
so the census rows (Complete / Mixed / Revision) are this frame's. The mod-to-mod anchor recurses (UpdateSystem.cs:178-181 registers
under the anchor, :278-292; `AddSystemUpdate` :382-441 adds a system's same-phase before-entries, then itself, then its
after-entries): census (anchored before ChairPin) -> this -> `ChairPinSystem`. The phase must stay Modification4B or the anchor is
skipped and the system silently never runs.

## Near LiftTowerDiagSystem

Read-only tower diagnostic ("Towers [nodes]" blocks: node y / terrain / Elevation, chord clearance, the tower the engine spawned,
placeholder scores). Fed by `LiftCensusSystem.BuildCensus` (`OnCensusBuilt`) and printed from here after the census has been quiet;
`DumpAll` on DUMP. The anchor is load-bearing like `TramShuttleSystem`'s: AFTER `LiftCensusSystem` at Modification4B, so a line's
towers (created at 2B, in the node SubObject buffers by Modification3, SystemOrder.cs:131, :136) are readable. The phase must stay
Modification4B or the anchor is skipped and the system silently never prints.

## Near TowerNumberTileSystem

Per-tower number tiles, Secondary sub-objects of the line towers. Fed by `LiftCensusSystem` (`OnCensusBuilt`). The anchor is
load-bearing: AFTER `LiftCensusSystem` at Modification4B (towers are in the node buffers by Modification3, SystemOrder.cs:136) and
BEFORE Modification5, where `SecondaryObjectReferencesSystem` files a Created tile into its tower's SubObject buffer (SystemOrder.cs:
206). Writes nothing while the `SkiNumTile` prefabs are not loaded (the baked plates show).

## Near LiftTowerPlanOverlaySystem

Read-only overlay of the planned towers and rope of the selected lift line (the plan `LiftTowerDiagSystem` logs as "Towers [plan]").
The anchor is load-bearing for the `OverlayRenderSystem` reason: the lines must be added BEFORE it in the same phase (it consumes and
clears the overlay buffers each frame, OverlayRenderSystem.cs:781, 790). Gated by the `LiftBuildAids` setting inside the system.

## Near LiftTowerFitSystem

Writes each new / changed lift line's node positions, node y + `Game.Net.Elevation` and edge curves, and tags them Updated. Fed by
`LiftCensusSystem` (`OnCensusBuilt`, 4B of an earlier frame). Phase is load-bearing: Modification1 is the first phase of the frame's
modification chain, so every consumer of the Updated tag runs after the write in the SAME frame: `GroundHeightSystem` (Modification2,
SystemOrder.cs:117), `SubObjectSystem` (2B, :131: re-scores the tower ladder), `CompositionSelectSystem` (3), `Net.GeometrySystem`
and `LaneSystem` (4), the census and `ChairPinSystem` (4B), all before `CleanUpSystem` wipes the tag (Cleanup, :54). A write at 4B
would lose the tag unseen. No ordering inside Modification1 is needed (its systems generate from tool definitions; none reads
permanent lift nodes). Gated by the `LiftTowerPlanner` setting inside.

## Near LiftLineToolSystem

ToolUpdate is where every vanilla tool lives (SystemOrder.cs:699-709), and the phase is load-bearing: its `CreationDefinition` +
`NetCourse` entities (ToolOutputBarrier ECB) must exist before PostTool's `CourseSplitSystem` (CourseSplitSystem.cs:4034) and
Modification1/2's `GenerateNodes` / `GenerateEdges` of the same frame; `ToolSystem` enables only the active tool (ToolSystem.cs:
312-327).

- Its overlay preview is drawn from ToolUpdate (before `OverlayRenderSystem` in Rendering clears the lists, OverlayRenderSystem.cs:
  738-790).
- The hotkey poller runs at UIUpdate (every frame, paused or not: an inactive tool never ticks, the `SkiGladeHotkeySystem`
  reasoning); its Build request lands on the tool's next frame.
- The tooltip runs at UITooltip after the vanilla cost tooltip (the `SkiLiftTooltipSystem` anchor: `TooltipUISystem` clears the
  mouse group first, TooltipUISystem.cs:48-55).
- Order between the three is not load-bearing (each reaches the tool via `GetOrCreateSystemManaged`).

## Near CatTrackGradeToolSystem

The lift tool's skeleton and the same three phases for the same reasons.

- ToolUpdate: `ToolSystem` enables only the active tool (ToolSystem.cs:312-327) and the overlay must be drawn before
  `OverlayRenderSystem` clears its lists in Rendering (OverlayRenderSystem.cs:738-790). Its apply writes the base heightmap on the
  main thread there; the only ECS output is the cleared trees' Deleted through `ToolOutputBarrier`, played back before Modification.
- UIUpdate: the hotkey poller (an inactive tool never ticks) also runs the dev-tab probe's follow-up checks.
- UITooltip after the vanilla cost tooltip (`TooltipUISystem` clears the mouse group first, TooltipUISystem.cs:48-55).
- Order between the three is not load-bearing.

## Near LiftParkSystem

The census (4B) only queues the surviving sections of an opened lift. `EndFrameBarrier.CreateCommandBuffer` throws at Modification4B
(Game/SafeCommandBufferSystem.cs:16-23): the barrier is the first MainLoop system (SystemOrder.cs:49) and is re-allowed only by
`AllowBarrier<EndFrameBarrier>` (:62, after ModificationSystem :60; same in 1.6.2). UIUpdate (:67) is after :62, so this system
creates the `{Event, SubObjectsUpdated}` entities there; they play back at the next frame's `EndFrameBarrier` and `SubObjectSystem`
consumes them at that frame's Modification2B. Order within UIUpdate is not load-bearing. Without this registration the census logs
one "Lift [park] ... not running" WARN per load and nothing is parked.

## Near SkiStationAlignSystem

Guide lines for the station being placed or nudged. The anchor is load-bearing for the `SkiRunDrawFeedbackSystem` reason:
`OverlayRenderSystem` (Rendering, SystemOrder.cs:687, 1.6.2 :688) consumes and clears the overlay buffers each frame
(OverlayRenderSystem.cs:781, 790), so our lines must be added BEFORE it in the same phase. Order between the two mod systems
anchored there is not load-bearing (separate lines). Gated by the `LiftBuildAids` setting inside the system.

## Near SkiPisteMapSystem

The anchor is load-bearing for the `SkiRunDrawFeedbackSystem` reason: `OverlayRenderSystem` (Rendering, SystemOrder.cs:687, 1.6.2
:688) completes every registered writer and then uploads and clears the overlay lists each frame (OverlayRenderSystem.cs:738-741,
781, 790), so our job must be scheduled BEFORE it in the same phase. It reads the hub's state, written at UIUpdate earlier in the same
MainLoop frame (SystemOrder.cs:67-68). Rendering runs paused included.

## Near SkiSnowLineOverlaySystem

Winter snow line while placing. The anchor is load-bearing for the `SkiPisteMapSystem` reason: `OverlayRenderSystem` uploads and
clears the overlay lists each frame (OverlayRenderSystem.cs:738-741, 781, 790), so our main-thread `DrawLine` writes must land BEFORE
it in the same phase. ONE anchor only (each `UpdateBefore` call adds an entry, UpdateSystem.cs:173-176). Rendering runs paused
included. Gated by `ShowSnowLineWhilePlacing` inside the system.

## Near SkiRunLabelSystem

Run names on the hill. NO anchor to `OverlayRenderSystem`: the labels are `Graphics.DrawMesh` calls with the game's own TextMeshPro
mesh and overlay text material, and nothing is written to the overlay buffers, so a plain `UpdateAt` is correct. It must only run
after the catalog's UIUpdate-phase rebuilds of the same frame, which Rendering does (SystemOrder.cs:67-68). Rendering runs paused
included. Gated by `ShowRunNamesOnTheHill` inside the system.

## Near ResortPlanLabelSystem

Resort Planner plan labels (one per lift and per pod). Same rule as the run names: NO anchor to `OverlayRenderSystem` (the labels are
`Graphics.DrawMesh` calls, nothing is written to the overlay buffers), a plain `UpdateAt` at Rendering. A stub until stage A4 of the
Resort Planner (EngineNotes/ResortPlanner.md); registering it now keeps `Mod.cs` out of the later tasks' file lists.

## Near SkiSnowLineTooltipSystem

The anchor is load-bearing like `SkiRunTooltipSystem`'s: `UITooltip` runs only from `TooltipUISystem`, which clears the mouse group
first (TooltipUISystem.cs:48-55). Anchored after the run tooltip (itself after `AreaToolTooltipSystem`, SystemOrder.cs:916) so the
line follows the run's own rows instead of sorting after every vanilla UITooltip system (UpdateSystem.cs:39-47). Reads
`SkiSnowLineOverlaySystem.Showing`, written at Rendering of the previous frame (one frame of lag, as the run tooltip's readout).

## Near SkiLiftTooltipSystem

Alignment readout + bulldozer warning. Anchoring after the vanilla `TempCostTooltipSystem` (SystemOrder.cs:911, 1.6.2 :912) keeps
our lines with the vanilla cost line, as `SkiGladeTooltipSystem` does; a plain `UpdateAt` would sort after every vanilla UITooltip
system (UpdateSystem.cs:39-47). Gated by the `LiftBuildAids` setting inside the system.

## Near SkiPlacementInfoviewSystem

Closes the Electricity infoview the lift LINE tool opens (once per activation; kept when the player had it open; the base station
keeps it). Registration is load-bearing twice: `UpdateBefore` ONLY (an `UpdateAt` as well would register the system a second time and
run it twice per frame, UpdateSystem.cs:158-176 -> Register :271-292 appends one entry per call); and BEFORE `InfoviewsUISystem`
(UIUpdate, SystemOrder.cs:939, 1.6.2 :940) so the vanilla infoview binding reflects our close in the same frame. `ToolSystem`
(MainLoop, SystemOrder.cs:58) has already applied this frame's tool infoview before UIUpdate (:67), so the system sees the view the
tool just opened.

## Near LodgeRoadSystem

Clears `NoRoadConnection` (sets `RequireRoad`) and `OwnerSide` on the lodge PREFAB at every Game-mode load: the state the game gives
an upgrade allowed a road (BuildingInitializeSystem.cs:177-188). An Editor load after a game in the same app run writes the game's
values back. Registered like `CursorAimSystem`. The phase is not load-bearing: `BuildingInitializeSystem` runs only on Created prefab
entities, in PrefabUpdate (SystemOrder.cs:1018, 1.6.2 :1019), long before a city load.

## Near MountainJobsSystem

Gives the roadless hut / summit / patrol buildings' jobs on their linked Welcome Sign's `WorkProvider` (the ServiceUpgrade combine's
runtime path, ServiceUpgradeSystem.cs:137-154 + CityServiceWorkplaceInitializeSystem.cs:97-121). It polls the published top-level
links every 64 frames. The phase is NOT load-bearing: `WorkProviderSystem`'s tick (GameSimulation, slice every 512 frames) reads
`m_MaxWorkers` whenever it runs, and nothing vanilla rewrites it for a city service building after its Created frame.

## Near OffSeasonStaffSystem

Lodges and the ski school keep half their workplaces while their ski area has had no open run for 12 game hours (`OffSeasonRules`;
`MountainJobsSystem` halves the hut / summit sign jobs through `IsHalf`). Polls every 256 frames. The phase is NOT load-bearing for
the same reason as `MountainJobsSystem` (`WorkProviderSystem` reads `m_MaxWorkers` whenever it runs).

## Near StationLvIconSystem

Removes the vanilla `LowVoltageNotConnected` icon from a station whose LV stub has a wire: `ConnectionWarningSystem` counts only
edges AT the stub node, so a wire that local-connected onto the stub's middle powers the station but leaves the icon (see the
`StationLvRule` header). The phase is NOT load-bearing: the queued removes are consumed by `IconCommandSystem` at ModificationEnd
(SystemOrder.cs:271) whenever it runs next.

## Near SkiRunAreaTypeSystem

Forges `AreaGeometryData.m_Type` Surface -> Space on the `SkiRunCatTrackA` prefab at every Game-mode load so a cat track can cross
other runs (ValidationHelpers.cs:551 compares only the type). Registered like `LodgeRoadSystem`. The phase is not load-bearing:
`AreaInitializeSystem` fills the type on the Created prefab entity long before a city load.

## Near SkiLodgeSpotDiag

Read-only lodge spawn-spot diagnostic ("Lodge [spots]": the `Game.Buildings.SpawnLocationElement` buffer of each lodge as the engine
registered it, one INFO line per load and per change, the per-spot lines on the hub DUMP). Phase and order are NOT load-bearing: it
only reads, and the producers (`Game.Buildings.ReferencesSystem` at Modification4B, SystemOrder.cs:176;
`SpawnLocationConnectionSystem` at Modification5, :210; the load-time rebuild `SpawnLocationElementSystem` at Deserialize, :818) all
run before GameSimulation in a frame or before `OnGameLoadingComplete`.
