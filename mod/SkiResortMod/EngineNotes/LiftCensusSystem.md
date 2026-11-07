# LiftCensusSystem: lift edit probe, snapshot, event logs, chair park and the power reader

`LiftCensusSystem` is the ECS side of the lift census (the model is the pure `LiftCensus`, see LiftCensus.md). A lift is edited section
by section. The system never deletes anything. It reads, logs, and asks `ChairPinSystem` for a loop rebuild. Via `LiftParkSystem`
(same file), it also asks the GAME to re-seat the chairs of the surviving sections of an opened lift.

## Registration and timing
Mod.cs: `UpdateBefore<LiftCensusSystem, ChairPinSystem>(Modification4B)`, a mod-to-mod anchor in the same phase.
- At 4B the Created / Updated / Deleted tags of this frame are visible, and Deleted entities still exist. The tags live until Cleanup
  (PrepareCleanUp is the last MainLoop system, SystemOrder.cs:50, :54).
- Chairs created at 2B are already in the SubObject buffer of their owner. SubObjectReferencesSystem at Modification3
  (SystemOrder.cs:136) adds Created and removes Deleted sub-objects (SubObjectReferencesSystem.cs:43-53).
- Modification phases run every frame, paused included (ModificationSystem.cs:19-26). So all frame counts are update counts.

## Probe (event-driven; with no events nothing runs but a counter)
- Lift line edges / nodes with PrefabRef and Any {Created, Updated, Deleted}, None {Temp}, filtered to the catalog's lift lines.
  Created / Deleted build THIS frame. A build for Updated only is coalesced (at most one build per CoalesceFrames while it continues,
  plus a trailing one).
- Station buildings Any {Created, Updated, Deleted}, None {Temp}. Created / Deleted build now. An Updated station whose Transform
  differs from the last recorded one is a MOVE (Move It drags tag the station Updated every frame). The system records
  LastMovedStation / LastMovedFrame and coalesces the build. An Updated station that did not move (the echo of the reconnect poke)
  triggers nothing.
- Deleted static objects with Owner (chairs and towers, Game.Objects.Static keeps vehicles and cims out) whose owner is a lift edge or
  node. Also Deleted utility lanes whose owner is a lift edge (HV = the cable). There are two uses:
  - Snapshot for the section-deleted line: the system counts the chairs / towers whose owner went too HERE, grouped by owner. It does
    NOT count them from SubObject buffers, because SubObjectReferencesSystem has already emptied those of Deleted sub-objects at
    Modification3.
  - Removed by another tool: a Deleted chair / tower / cable lane whose owner is alive and carries no Created / Updated / Deleted this
    frame. Vanilla re-seats and lane rebuilds delete old sub-objects only while the owner is Updated, so they are excluded. The vanilla
    bulldozer offers only MainElements in game mode (BulldozeToolSystem.cs:1339-1347) and never targets a chair or a tower without its
    node. Chair: log only. Tower: log + the row's TowersRemoved (stateless in `LiftCensus`). Cable: log. It is unverified whether
    the Deleted tag of Better Bulldozer is visible here. So every build also compares per-row chair / tower counts with the previous
    build and logs a drop that no event explained. A WatchdogFrames build runs while any lift exists.

## Snapshot, before the build
The snapshot records these items for each Deleted lift edge:
- Its row, through the previous edge-to-row map of the census (TryGetRowOfEdge before Build = PreviousRows after it).
- Curve length.
- End nodes, and whether each was deleted this frame.

A Deleted SkiLiftMarker edge that shares a node with it adds "and the station's connection stub" (a line-first end-tower click).

## After the build (LiftCensus events; never on the per-load baseline build)
- SectionsDeleted: `Lift [section deleted]: ...` (enriched from the snapshot).
- Opened: `Lift [line opened]: ...` and, when `ChairPinSystem.MovingChairsOn`, PARK. PARK queues every surviving edge of the row for a
  SubObjectsUpdated event, once per open episode (m_Parked, cleared when the row closes or drops).
  - Next frame SubObjectSystem processes the owner (query term SubObjectSystem.cs:3944-3951, event branch :369-381). It reuses each
    chair and resets its Transform to the vanilla even-spacing pose (:2604-2611). It removes InterpolatedTransform for an EDGE owner
    (edges pass interpolated: false, :1039, removal :2686-2690) and tags it Updated (:2737-2740).
  - One frame after the system created the events, it asks `ChairPinSystem` for an immediate rebuild. The rebuild static-pins the
    re-seated chairs onto their own lanes. Two frames after, the system counts the chairs of the parked edges that still carry
    InterpolatedTransform. A count > 0 gives `WARN Lift [park]`. Otherwise the system writes one Info line.
  - Both the line-opened line and the park line print "N chair(s) parked (R were riding)". N is every chair on the surviving sections.
    R is the number of those chairs that carried InterpolatedTransform.
  - A LaneTooFar safety net is not built (LaneTooFar is an expected outcome on approach edges, ChairPinSystem header).
  - A complete lift that loses a STATION keeps its census key and opens (LiftCensus attribution rule 2). So the same park runs, and the
    line reads `Lift [station removed]: ...` instead of `Lift [new line]`. Rows may therefore name a removed station:
    - Describe names the live one.
    - TryGetOppositeStation skips a removed partner.
    - The stats system shows such a row as "not connected to a station at both ends".
    - The row id is the live station.
- Closed: `Lift [line closed]: ...`. NewLine: `Lift [new line]: ...`.
- Topology changed: `ChairPinSystem.RequestImmediateRebuild("lift topology")` (closes the 30-frame window: the loop over a deleted span is
  dropped this frame).

## Near LiftParkSystem: why it emits the park event
Creating the event with `EndFrameBarrier.CreateCommandBuffer()` at Modification4B throws:
- SafeCommandBufferSystem.CreateCommandBuffer refuses while m_IsAllowed is false (SafeCommandBufferSystem.cs:16-23). The barrier clears
  the flag in its own OnUpdate (:25-30). EndFrameBarrier updates FIRST in MainLoop (SystemOrder.cs:49). Only
  AllowBarrier<EndFrameBarrier> AFTER ModificationSystem re-allows it (SystemOrder.cs:60, :62).
- Every modification barrier before 4B is likewise disallowed at 4B. ModificationBarrier4B plays back this frame. So the cleanup of
  this frame would destroy the event before the next 2B (PrepareCleanUpSystem.cs:21-28). An event created with EntityManager dies the
  same way.
- The vanilla precedent WetnessSystem (WetnessSystem.cs:93-94, :161, :205-207) runs in GameSimulation, i.e. in LateUpdate
  (SystemOrder.cs:74). There the barrier IS allowed.

So the census only QUEUES the edges. `LiftParkSystem` (`UpdateAt<LiftParkSystem>(UIUpdate)`, after AllowBarrier<EndFrameBarrier>) creates
the events from an EndFrameBarrier buffer in the same frame.
- UIUpdateSystem drives UIUpdate in MainLoop (SystemOrder.cs:67), AFTER AllowBarrier<EndFrameBarrier> (:62). So
  EndFrameBarrier.CreateCommandBuffer is allowed (SafeCommandBufferSystem.cs:16-23). The 4B census of the same frame has already queued
  the edges.
- The buffer plays back when EndFrameBarrier updates first in the next MainLoop (:49). SubObjectSystem consumes the {Event,
  SubObjectsUpdated} entity at Modification2B of that frame (query :3944-3951, event branch :369-381). PrepareCleanUpSystem then
  collects the Event entity (:21-28).
- The archetype is the own archetype of vanilla (WetnessSystem.cs:161, SetComponent pattern :93-94). Order within UIUpdate is not
  load-bearing.
- If the emitter never runs (not registered), the census warns once and drops the queue. The emitter has one command buffer per park
  (an event, not per frame) and a one-shot "!!!" catch. The catch disables the emitter until the next load (the census then warns once
  that the queue was not taken).

## Lift names in logs
The system uses the custom name of the base station (else of the top) from NameSystem (NameSystem.cs:209-212), quoted. Else it uses the
plain word "lift". The stats system computes the per-resort "Lift N" default of the hub. This file does not reproduce it. Every line
carries the station ids.

## Frames and time
Frame counts are the own update count of this system (it runs every frame, paused included). LastMovedFrame is
UnityEngine.Time.frameCount, NOT RenderingSystem.frameIndex. frameIndex derives from the simulation clock and freezes while paused
(RenderingSystem.cs:182-190). So "moved within the last 2 s" would never expire on a paused game, where players place stations.
LastMovedRealtime is realtimeSinceStartup.

## Accessors for the served rule (main thread, never per frame)
- `CopyServeLifts` returns every row (row order = CopyRows) as a `LiftServedRule.Lift`. The fields are:
  - Id / Base / Top.
  - Complete = State Complete.
  - TopY = the stub-tip y of the top station in the last build (else NaN).
  - Power (read now for Complete rows only).

  It also returns the line CHORDS of the row (each lift-line edge's StartPos.xz to EndPos.xz of the last build, station stubs are not
  line edges). The pure `LiftServedRule` decides service. `SkiResortAttractivenessSystem` and `SkiResortStatsSystem` call this. It
  returns 0 lifts when the census is not armed (the callers fail open). The first call per load writes one `LiftCensus [serve]` cost
  line.
- `ReadLiftPower(base station)`: THE ONLY reader of a lift station's Game.Buildings.ElectricityConsumer in the mod.
  - `LiftServedRule.PowerOf` returns one of these values:
    - NoPower while the consumer shows the game's warning (NoElectricityWarning or BottleneckWarning, DispatchElectricitySystem.cs:126-158,
      flags ElectricityConsumerFlags.cs:5-12) and under half of its wanted power arrives. NoPower ends at three quarters or when the
      warning clears (4-argument overload, fed the last value read for the station: a 9 % shortfall once switched both lifts off).
    - Powered otherwise.
    - Unknown for a Null / gone station or one without a consumer (one `LiftCensus [power]` line per station per load).
  - The base station is a transformer fed from its stub valves, so a fed base station means an energized line. Every caller turns the
    value into "powered" with `LiftServedRule.IsPowered`.
  - Transition lines: the first read of a station in a load logs only NoPower. Later reads log a change. They go through m_PowerLog
    (LogWindow 10 s: a brownout can flip the warning every 384 frames per station, and four callers read power). The first change after
    a quiet window is written plain (`Lift [power]: ... - no power: ...` / `... - power back ...`). The rest fold into one
    `Lift [power] N more power change(s) ...` summary per 10 s. OnUpdate writes a trailing summary (checked every frame, an int compare
    while nothing is folded, before the armed gate). OnGamePreload and OnDestroy flush it.
- `TryGetRowOfBendNode(node)` returns the first row whose sharpest bent tower (LiftCensus.Row.BendNode) is that node. The LiftBend row of
  the hub clicks with the node (SkiResortHubUISystem.ApplyFocusLift). The DUMP row line prints the sharpest bend, its tower and node,
  and "OVER the warning angle" past LiftAlignMath.BendWarnDeg.
- `ComputePowerSignature`: FNV-1a (SkiRunRatingRules.Mix) of (base Index, Version, LiftPower) over the Complete rows, read now through
  ReadLiftPower. Power raises no entity event, so the attraction model and the lift charge poll this every 256 frames and compare.

## House rules
This system follows these rules:
- The "created" line.
- One `LiftCensus [arm]` line per load (constants incl. RemovedLinesPerBuild, prefabs, first census counts).
- m_Armed / m_Disabled / m_ErrorLogged reset per load.
- Game mode only.
- One-shot `!!! LiftCensusSystem EXCEPTION` that disables the census until the next load (ChairPinSystem is unaffected).

There is no per-frame managed allocation. Probes are IsEmptyIgnoreFilter checks. The system formats strings only when it logs a line.
It reuses member collections.

## Typed census (the model is LiftCensus.cs's)
- Prefab identity comes from the pure SkiCatalog.cs, resolved per load by `SkiPrefabCatalogSystem`:
  - LiftLinePrefabSet (the lift lines, an explicit whitelist)
  - MarkerEntity (the stub)
  - StationDefs / StationPrefabSet (the terminals)
  - CarrierGripTops (the chairs)

  The system fetches each view ONCE before a chunk loop. Base or top comes from the Role of the catalog row (StationDef.Role), not from name
  equality.
- Edges: the LiftLineTypes view of the catalog (line prefab to its SkiCatalog row). Edge.Type = the row's index. The whitelist stays
  explicit. A vanilla HV line (which can feed a station stub) has no catalog row and never becomes an Edge. So it never joins a
  component and never makes one "mixed".
- Stations: Station.Type = StationDef.LiftTypeIndex (NoType when < 0). Station.StubTip = StationAlign.StubTip(pos, rot,
  StationDef.StubTipLocalZ) per row (-16 on the detachable / gondola stations, -12 on the fixed-grip and tram ones).
- `CopyServeLifts` fills LiftServedRule.Lift.Rule / RadiusM / TopMarginM from SkiCatalog.LiftByIndex(row.Type). TopXZ = the stub tip xz
  of the top station in the last build (NaN when absent). A Mixed or untyped row gets AlongLine with radius 0 (it is Unfinished, so it
  serves nothing anyway).
- ARM line: one entry per lift type ("lines: fixedGrip 'SkiChairliftHV' 812:1, detachable pending, ..."). An EXPECTED line that is
  MISSING keeps the `!!! LiftCensus [arm]: net prefab ... not found` line. Pending lines are listed, never `!!!`. The "mixes lift types"
  build note goes through the census note path ("LiftCensus [note]: ...", deduplicated per build signature).

## Power
An unpowered lift of every type carries no guests, and its carriers stop. The system reads the power of a lift at its BASE station
only. The top station needs none. It is fed through the modeled lift power line and has no ElectricityConsumer at all (ServiceConsumption
0 on SkiTopStationBuilding, ConsumptionData.cs:21-26 adds the consumer only above 0). The system never reads it. `ReadLiftPower` stays
THE reader. Every caller passes the Base of a census row, the Base-ROLE station (LiftCensus.cs:639-647). The callers are:
- CopyServeLifts
- ComputePowerSignature
- SkiLiftChargeSystem
- the tram
- the hold poll of `ChairPinSystem`

Rules of the power reader:
- Flicker guard: `ReadLiftPower` passes the value of PowerOf ("raw") through `LiftServedRule.GuardPower` with the last EFFECTIVE value of
  the station. NoPower applies at once. NoPower to Powered needs PowerBackHoldFrames (512) sim frames of raw Powered reads
  (SimulationSystem.frameIndex, SimulationSystem.cs:68, frozen while paused, 60 frames a second at 1x, :238). The first read of a station in a load is not guarded
  (no history: a lift saved inside a guard window reads Powered at once). A station whose effective value was never NoPower in this
  load gets raw back unchanged. Every consumer (served rule, score, billing, hub, both movers) sees the one guarded value.
- Transient state, per base station, cleared per load with m_LastPower:
  - m_BackSince: the frame the current Powered streak started, kept only while one runs. A Dictionary entry cannot be passed by ref, so
    the code guards a local and writes it back only when it changed.
  - m_StreakReadFrame: the last read while a streak runs.
  - m_GuardRecuts: raw NoPower reads that ended a streak this episode (flickers absorbed). The system writes it into the power-back
    line, then drops it.
- A streak whose station went unread for more than PowerStreakStaleFrames (1,024) restarts (`LiftServedRule.ExpireStreak`). The system
  reads every Complete row at least every 256 sim frames. The polls are the state poll of the attraction
  (SkiResortAttractivenessSystem.cs:210) and the power poll of the charge (SkiLiftChargeSystem.cs:128 x its interval 4). So a longer
  gap means the row was not Complete (a section being redrawn), and the system did not see the power meanwhile.
- Texts: the "no power" line says the lift stops and names the guard. The "power back" line says "after 512 frames of power" and how many
  flickers were absorbed. The ARM line gains the guard clause. DumpCensus lists each station inside a guard ("power guard: ...") or
  "power guard: none".
- A census exception (the one-shot `!!!`) leaves IsArmed false. The hold of ChairPinSystem and the tram then fail open (the chairs move,
  the tram runs). So the "chairs keep moving" text of the `!!!` line stays true.
