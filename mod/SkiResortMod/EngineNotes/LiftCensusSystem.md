# LiftCensusSystem: lift edit probe, snapshot, event logs, chair park and the power reader

`LiftCensusSystem` is the ECS side of the lift census (the model is the pure `LiftCensus`, see LiftCensus.md). A lift is edited section
by section: the system never deletes anything; it reads, logs, asks `ChairPinSystem` for a loop rebuild, and (via `LiftParkSystem`, same
file) asks the GAME to re-seat the chairs of surviving sections of an opened lift.

## Registration and timing
Mod.cs: `UpdateBefore<LiftCensusSystem, ChairPinSystem>(Modification4B)`, a mod-to-mod anchor in the same phase.
- At 4B this frame's Created / Updated / Deleted tags are visible (they live until Cleanup; PrepareCleanUp is the last MainLoop system,
  SystemOrder.cs:50, :54) and Deleted entities still exist.
- Chairs created at 2B are already in their owner's SubObject buffer (SubObjectReferencesSystem at Modification3, SystemOrder.cs:136, adds
  Created and removes Deleted sub-objects, SubObjectReferencesSystem.cs:43-53).
- Modification phases run every frame, paused included (ModificationSystem.cs:19-26), so all frame counts are update counts.

## Probe (event-driven; with no events nothing runs but a counter)
- Lift line edges / nodes with PrefabRef and Any {Created, Updated, Deleted}, None {Temp}, filtered to the catalog's lift lines.
  Created / Deleted build THIS frame; Updated only is coalesced (at most one build per CoalesceFrames while it continues, plus a
  trailing one).
- Station buildings Any {Created, Updated, Deleted}, None {Temp}. Created / Deleted build now. An Updated station whose Transform differs
  from the last recorded one is a MOVE (Move It drags tag the station Updated every frame): LastMovedStation / LastMovedFrame are
  recorded and the build is coalesced. An Updated station that did not move (the reconnect poke's echo) triggers nothing.
- Deleted static objects with Owner (chairs and towers; Game.Objects.Static keeps vehicles and cims out) whose owner is a lift edge or
  node; Deleted utility lanes whose owner is a lift edge (HV = the cable). Two uses:
  - Snapshot for the section-deleted line: chairs / towers whose owner went too are counted HERE, grouped by owner, NOT from SubObject
    buffers, which SubObjectReferencesSystem has already emptied of Deleted sub-objects at Modification3.
  - Removed by another tool: a Deleted chair / tower / cable lane whose owner is alive and carries no Created / Updated / Deleted this
    frame (vanilla re-seats and lane rebuilds delete old sub-objects only while the owner is Updated, so they are excluded). The vanilla
    bulldozer offers only MainElements in game mode (BulldozeToolSystem.cs:1339-1347) and never targets a chair or a tower without its
    node. Chair: log only; tower: log + the row's TowersRemoved (stateless in `LiftCensus`); cable: log. Whether Better Bulldozer's
    Deleted tag is visible here is unverified: every build also compares per-row chair / tower counts with the previous build and logs
    a drop that no event explained, and a WatchdogFrames build runs while any lift exists.

## Snapshot, before the build
For each Deleted lift edge: its row through the census's previous edge-to-row map (TryGetRowOfEdge before Build = PreviousRows after it),
Curve length, end nodes and whether each was deleted this frame. A Deleted SkiLiftMarker edge sharing a node with it adds "and the
station's connection stub" (a line-first end-tower click).

## After the build (LiftCensus events; never on the per-load baseline build)
- SectionsDeleted: `Lift [section deleted]: ...` (enriched from the snapshot).
- Opened: `Lift [line opened]: ...` and, when `ChairPinSystem.MovingChairsOn`, PARK: every surviving edge of the row, once per open
  episode (m_Parked, cleared when the row closes or drops), is queued for a SubObjectsUpdated event.
  - Next frame SubObjectSystem picks the owner up (query term SubObjectSystem.cs:3944-3951; event branch :369-381), reuses each chair,
    resets its Transform to the vanilla even-spacing pose (:2604-2611), removes InterpolatedTransform for an EDGE owner (edges pass
    interpolated: false, :1039; removal :2686-2690) and tags it Updated (:2737-2740).
  - The frame after the events were created, this system asks `ChairPinSystem` for an immediate rebuild, which static-pins the re-seated
    chairs onto their own lanes. Two frames after, it counts chairs of the parked edges that still carry InterpolatedTransform: > 0 gives
    `WARN Lift [park]`, else one Info line.
  - Both the line-opened line and the park line print "N chair(s) parked (R were riding)" (N is every chair on the surviving sections, R
    those that carried InterpolatedTransform).
  - A LaneTooFar safety net is not built (on approach edges LaneTooFar is an expected outcome, ChairPinSystem header).
  - A complete lift that loses a STATION keeps its census key and opens (LiftCensus attribution rule 2), so the same park runs and the
    line reads `Lift [station removed]: ...` instead of `Lift [new line]`. Rows may therefore name a removed station: Describe names the
    live one, TryGetOppositeStation skips a removed partner, the stats system shows such a row as "not connected to a station at both
    ends", and the row id is the live station.
- Closed: `Lift [line closed]: ...`; NewLine: `Lift [new line]: ...`.
- Topology changed: `ChairPinSystem.RequestImmediateRebuild("lift topology")` (closes the 30-frame window: the loop over a deleted span is
  dropped this frame).

## Near LiftParkSystem: why it emits the park event
Creating the event with `EndFrameBarrier.CreateCommandBuffer()` at Modification4B throws:
- SafeCommandBufferSystem.CreateCommandBuffer refuses while m_IsAllowed is false (SafeCommandBufferSystem.cs:16-23); the barrier clears the
  flag in its own OnUpdate (:25-30); EndFrameBarrier updates FIRST in MainLoop (SystemOrder.cs:49) and is re-allowed only by
  AllowBarrier<EndFrameBarrier> AFTER ModificationSystem (SystemOrder.cs:60, :62).
- Every modification barrier before 4B is likewise disallowed at 4B; ModificationBarrier4B plays back this frame (the event would be
  destroyed by this frame's cleanup, PrepareCleanUpSystem.cs:21-28, before the next 2B), and an EntityManager-created event dies the same
  way.
- Vanilla's precedent WetnessSystem (WetnessSystem.cs:93-94, :161, :205-207) runs in GameSimulation, i.e. in LateUpdate (SystemOrder.cs:74),
  where the barrier IS allowed.

So the census only QUEUES the edges. `LiftParkSystem` (`UpdateAt<LiftParkSystem>(UIUpdate)`, after AllowBarrier<EndFrameBarrier>) creates
the events from an EndFrameBarrier buffer the same frame.
- UIUpdate is driven by UIUpdateSystem in MainLoop (SystemOrder.cs:67), AFTER AllowBarrier<EndFrameBarrier> (:62), so
  EndFrameBarrier.CreateCommandBuffer is allowed (SafeCommandBufferSystem.cs:16-23); the same frame's 4B census has already queued the edges.
- The buffer plays back when EndFrameBarrier updates first in the next MainLoop (:49); SubObjectSystem consumes the {Event,
  SubObjectsUpdated} entity at that frame's Modification2B (query :3944-3951, event branch :369-381); PrepareCleanUpSystem then collects the
  Event entity (:21-28).
- The archetype is vanilla's own (WetnessSystem.cs:161; SetComponent pattern :93-94). Order within UIUpdate is not load-bearing.
- If the emitter never runs (not registered), the census warns once and drops the queue. The emitter has one command buffer per park (an
  event, not per frame) and a one-shot "!!!" catch that disables it until the next load (the census then warns once that the queue was not
  taken).

## Lift names in logs
The base station's custom name (else the top's) from NameSystem (NameSystem.cs:209-212), quoted; else the plain word "lift". The hub's
per-resort "Lift N" default is computed by the stats system and not reproduced here; every line carries the station ids.

## Frames and time
Frame counts are this system's own update count (it runs every frame, paused included). LastMovedFrame is UnityEngine.Time.frameCount,
NOT RenderingSystem.frameIndex: frameIndex is derived from the simulation clock and freezes while paused (RenderingSystem.cs:182-190), so
"moved within the last 2 s" would never expire on a paused game, where players place stations. LastMovedRealtime is
realtimeSinceStartup.

## Accessors for the served rule (main thread, never per frame)
- `CopyServeLifts`: every row (row order = CopyRows) as a `LiftServedRule.Lift` (Id / Base / Top, Complete = State Complete, TopY = the top
  station's stub-tip y in the last build else NaN, Power read now for Complete rows only) plus its line CHORDS (each lift-line edge's
  StartPos.xz to EndPos.xz of the last build; station stubs are not line edges). The pure `LiftServedRule` decides service;
  `SkiResortAttractivenessSystem` and `SkiResortStatsSystem` call this. 0 lifts when the census is not armed (the callers fail open). The
  first call per load writes one `LiftCensus [serve]` cost line.
- `ReadLiftPower(base station)`: THE ONLY reader of a lift station's Game.Buildings.ElectricityConsumer in the mod.
  - `LiftServedRule.PowerOf`: NoPower while the consumer shows the game's warning (NoElectricityWarning or BottleneckWarning,
    DispatchElectricitySystem.cs:126-158; flags ElectricityConsumerFlags.cs:5-12) and under half of its wanted power arrives; back at
    three quarters or when the warning clears (4-argument overload, fed the last value read for the station: a 9 % shortfall once
    switched both lifts off); else Powered; Unknown for a Null / gone station or one without a consumer (one `LiftCensus [power]` line per
    station per load).
  - The base station is a transformer fed from its stub valves, so a fed base station means an energised line. Every caller turns the value
    into "powered" with `LiftServedRule.IsPowered`.
  - Transition lines: the first read of a station this load logs only NoPower; later reads log a change. They go through m_PowerLog
    (LogWindow 10 s: a brownout can flip the warning every 384 frames per station and four callers read power): the first change after a
    quiet window is written plain (`Lift [power]: ... - no power: ...` / `... - power back ...`), the rest fold into one
    `Lift [power] N more power change(s) ...` summary per 10 s; OnUpdate writes a trailing summary (checked every frame, an int compare
    while nothing is folded, before the armed gate), and OnGamePreload / OnDestroy flush it.
- `ComputePowerSignature`: FNV-1a (SkiRunRatingRules.Mix) of (base Index, Version, LiftPower) over the Complete rows, read now through
  ReadLiftPower. Power raises no entity event, so the attraction model and the lift charge poll this every 256 frames and compare.

## House rules
"created" line; one `LiftCensus [arm]` line per load (constants incl. RemovedLinesPerBuild, prefabs, first census counts); m_Armed /
m_Disabled / m_ErrorLogged reset per load; Game mode only; one-shot `!!! LiftCensusSystem EXCEPTION` that disables the census until the
next load (ChairPinSystem is unaffected). No per-frame managed allocation: probes are IsEmptyIgnoreFilter checks; strings are formatted
only when a line is logged; member collections are reused.

## Typed census (the model is LiftCensus.cs's)
- Prefab identity comes from the pure SkiCatalog.cs, resolved per load by `SkiPrefabCatalogSystem`: LiftLinePrefabSet (the lift lines, an
  explicit whitelist), MarkerEntity (the stub), StationDefs / StationPrefabSet (the terminals) and CarrierGripTops (the chairs). Each view
  is fetched ONCE before a chunk loop. Base or top comes from the catalog row's Role (StationDef.Role), not from name equality.
- Edges: the catalog's LiftLineTypes view (line prefab to its SkiCatalog row); Edge.Type = the row's index. The whitelist stays explicit:
  a vanilla HV line (which can feed a station stub) has no catalog row, never becomes an Edge, so it never joins a component nor makes one
  "mixed".
- Stations: Station.Type = StationDef.LiftTypeIndex (NoType when < 0); Station.StubTip = StationAlign.StubTip(pos, rot,
  StationDef.StubTipLocalZ) per row (-16 on the detachable / gondola stations, -12 on the fixed-grip and tram ones).
- `CopyServeLifts` fills LiftServedRule.Lift.Rule / RadiusM / TopMarginM from SkiCatalog.LiftByIndex(row.Type) and TopXZ = the top
  station's stub tip xz of the last build (NaN when absent); a Mixed or untyped row gets AlongLine with radius 0 (it is Unfinished, so it
  serves nothing anyway).
- ARM line: one entry per lift type ("lines: fixedGrip 'SkiChairliftHV' 812:1, detachable pending, ..."); an EXPECTED line MISSING keeps
  the `!!! LiftCensus [arm]: net prefab ... not found` line; pending lines are listed, never `!!!`. The "mixes lift types" build note goes
  through the census note path ("LiftCensus [note]: ...", deduplicated per build signature).

## Power
An unpowered lift of every type carries no guests and its carriers stop; a lift's power is read at its BASE station only. The top station
needs none (it is fed through the modeled lift power line and has no ElectricityConsumer at all: ServiceConsumption 0 on
SkiTopStationBuilding, ConsumptionData.cs:21-26 adds the consumer only above 0) and is never read. `ReadLiftPower` stays THE reader and
every caller passes a census row's Base, the Base-ROLE station (LiftCensus.cs:639-647): CopyServeLifts, ComputePowerSignature,
SkiLiftChargeSystem, the tram and `ChairPinSystem`'s hold poll.
- Flicker guard: `ReadLiftPower` passes PowerOf's value ("raw") through `LiftServedRule.GuardPower` with the station's last EFFECTIVE
  value: NoPower at once; NoPower to Powered only after PowerBackHoldFrames (512) sim frames of raw Powered reads
  (SimulationSystem.frameIndex, SimulationSystem.cs:68, frozen while paused; 60 frames a second at 1x, :238). The first read of a station
  this load is not guarded (no history: a lift saved inside a guard window reads Powered at once). A station whose effective value was
  never NoPower this load gets raw back unchanged. Every consumer (served rule, score, billing, hub, both movers) sees the one guarded
  value.
- Transient, per base station, cleared per load with m_LastPower: m_BackSince (the frame the current Powered streak started, kept only
  while one runs; a Dictionary entry cannot be passed by ref, so a local is guarded and written back only when it changed),
  m_StreakReadFrame (the last read while a streak runs) and m_GuardRecuts (raw NoPower reads that ended a streak this episode: flickers
  absorbed; written into the power-back line, then dropped).
- A streak whose station went unread for more than PowerStreakStaleFrames (1,024) restarts (`LiftServedRule.ExpireStreak`). Every Complete
  row is read at least every 256 sim frames (the attraction's state poll, SkiResortAttractivenessSystem.cs:210, and the charge's power
  poll, SkiLiftChargeSystem.cs:128 x its interval 4), so a longer gap means the row was not Complete (a section being redrawn) and the
  power meanwhile was not seen.
- Texts: the "no power" line says the lift stops and names the guard; the "power back" line says "after 512 frames of power" and how many
  flickers were absorbed; the ARM line gains the guard clause; DumpCensus lists each station inside a guard ("power guard: ...") or
  "power guard: none".
- A census exception (the one-shot `!!!`) leaves IsArmed false: ChairPinSystem's hold and the tram then fail open (the chairs move, the
  tram runs), so the `!!!` line's "chairs keep moving" stays true.
