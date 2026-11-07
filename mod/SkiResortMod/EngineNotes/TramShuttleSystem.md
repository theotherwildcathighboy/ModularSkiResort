# TramShuttleSystem: how the aerial tram's two cabins are driven, gated, identified and placed against vanilla's sub-object system

Citations refer to Game.dll 1.6.0f1 (tools/decompile/1.6.0f1). A "=1.6.2" mark or a "1.6.2 :N" mark shows where 1.6.2 shifts a line.
The system moves the aerial tram's two cabins. The dev-tab foldout is `TramShuttleSystem.Debug.cs`.

## What it moves

The system moves the Shuttle carriers. A Shuttle carrier is a SkiCatalog lift type whose Mover is Shuttle: the tram and its cabin prefab.
A Shuttle Base station HOSTS the carriers. The base prefab's ObjectSubObjects list the two cabins at (9.50 / -1.50, 6.00, -5.50) in the
game frame, with identity rotation and m_ParentMesh 0. The top station hosts none. Cabin A rides chain A (the up lane, the right lane
when you look uphill, `StationAlign.UpLaneIsSecond`). Cabin B rides chain B.

Both paths run base dock -> top dock (`LiftLoopCache.BuildShuttles`). This is the open-chain mode. The two chains stay UNCLOSED. A
straight run extends each chain from and to the station's track-rope dock. `StationDef.DockLocalA` / `B` = (9.50 / -1.50, 16.00, -5.50)
on the base and (-5.50 / +5.50, ...) on the top.

- Lanes: the track ropes are the line's ONLY high-voltage lanes. The haul ropes are low-voltage (UtilityType 8), and the HV flag skips
  them. A third HV lane per side would give "3 chain(s), need exactly 2" and no path.
- ChairPinSystem never touches a Shuttle carrier (IsPinnable). It never harvests a Shuttle line's lanes. Its LogPrefabState writes the
  hand-off line once per load.
- The prototype is named SkiTramHVProto (not a catalog name). The catalog never finds it, so this system ignores it. If someone imported
  it under the real line name, this system would find no tram cabin and would idle.

## Clock

TimeS = `ShuttleProfile.TimeSOf(RenderingSystem.frameIndex, frameTime)`. It equals ChairPinSystem's TimeS bit for bit: (frameIndex +
frameTime) / 60 in double, ChairPinSystem.cs:1132-1135, LiftBatch SH-7. TimeS is frozen on pause (RenderingSystem.cs:187-190). It is
smooth at any speed (:193-212). It resnaps to the saved frame on load (:292-294). Movement runs about 20x slower than the game clock:
one game day = 262,144 frames = 4,369 s of movement.

The profile is the pure `ShuttleProfile`. It is a trapezoid at the type's SpeedMps and AccelMps2, with DwellS at each terminal, over L =
`Shuttle.ProfileLengthM`. L is the mean of L_A and L_B rounded to 1 cm, so a float-noise rebuild cannot shift the
period. Cabin A is at u = `ShuttleProfile.FractionA(TimeS, L, ...)`, with s_A = u * L_A. Cabin B is at s_B = (1 - u) * L_B. Both cabins
dock at the same instant because they share one haul rope. One clock serves every tram. There is no per-tram phase offset.

## Gate

The system evaluates the gate per shuttle at a rebuild, on a power change and on the kill switch. It never evaluates the gate per frame.
The gate has three states:

- Running = a shuttle path AND its census row Complete AND not Mixed AND `LiftServedRule.IsPowered(power)` AND not closed.
- Held = a path AND Complete AND not Mixed AND (unpowered OR closed). Both cabins stand at the terminals that cabin A last reached.
  Cabin A is at u 0 or 1. Cabin B is mirrored.
- Parked = everything else (no path, Open, Unfinished, StationsOnly, Mixed, no census row, the kill switch off, an invalid profile).
  Every cabin is in its hosted pose.

The power test and the closed test are ONE predicate: `LiftServedRule.Carries(true, run.Power, IsClosed(run))`. This is THE run
predicate. ChairPinSystem's hold and the hub's capacity use it too (LiftBatch PG-1). "Closed" is the lift's held-closed flag from
`SkiLiftChargeSystem`. The ski area has no open run.

Power = `LiftCensusSystem.ReadLiftPower(base)`. It is the mod's only reader of a station's ElectricityConsumer, and it is a sync point on
DispatchElectricitySystem's writers. The system reads power in two cases:

- Per shuttle whose census row is Complete, every PowerPollFrames (64) rendered frames.
- Once per shuttle at each rebuild. Without the rebuild read, a city loaded unpowered would run its tram for up to 64 frames first.

The system reads power for run.Base only. A Complete row has exactly one Base-role and one Top-role station (LiftCensus.cs).
LiftLoopTopology picks the Base-role station as the shuttle's base by role (Shuttle mode, LiftLoopTopology.cs:293-297). So run.Base IS
the row's Base, the station with the ElectricityConsumer. The system never reads the top station. The flicker guard is inherited:
ReadLiftPower returns the guarded value. It returns NoPower at once. It returns Powered again after 512 sim frames of power.

### Held latch

`ShuttleProfile.HoldFractionA` is a pure function of the clock. It flips between 0 and 1 every half cycle (172.5 s for 1 km). An
unlatched Held tram would swap both cabins between the bays every half cycle while it stays unpowered. So the mover latches
HoldFractionA once per Held episode. The latch is transient, per shuttle, and never saved.

- Running -> Held takes HoldFractionA at the cut. This is the terminal last reached: back to the station left, or on to the one reached.
- Any other entry takes the terminal configuration that the cabins already stand at (summed distance <= 1 m), else HoldFractionA. The
  other entries are a load, a rebuild, the kill switch and Parked -> Held.

Leaving Held drops the latch. Held -> Running jumps once to the clock place (stateless by design). Running -> Held also snaps mid-span
cabins to the terminals last reached. The chairs, by contrast, stop where they are.

## Cabin A / B identity (transient, per session, never saved)

The design rejects index-keyed state. Entity indices are remapped on load, so "cabin A = the lower (Index, Version)" could swap the
cabins after a load. Each cabin would jump to the other rope. The mover keeps a transient map cabin -> hosted BAY. Bay 0 = hosted entry
0 = dock A. Bay 1 = entry 1 = dock B. Dock A is the +X bay on the tram BASE and the -X bay on the tram TOP (SkiCatalog
`StationDef.DockLocalA` / `B`).

Geometry decides which chain a bay rides. Chain A's cabin is the one in the bay of chain A's base dock (Path.m_BaseDock). The other bay
rides chain B. A cabin without an entry gets one, per station, by the first rule that applies:

1. FRESH BASE: every cabin within 0.05 m of a hosted pose -> the bay it stands in. The system reads the bay from the cabin's GEOMETRY
   against Path.m_BaseDock, never from the sign of X. The base's dock A is the +X bay, so "the cabin in hosted bay 0 is A" holds by
   dock, not by side.
2. CLOCK PLACES: the assignment with the smaller sum |Transform - target|. The target is the clock place when Running. It is the latched
   (or both candidate) terminal configurations when Held. It is the hosted poses when Parked or when the station has no path. After a
   load the saved Transforms sit at the clock places, so the right mapping costs ~0.

A tie (within 1 mm) -> (Index, Version) order. The [rebuild] line names the rule ("kept", "fresh base", "by clock places, sum x.xx m",
"tie by entity"). Vanilla re-hosting reuses the cabin entities (below), so the map survives it.

## How a cabin is placed

The system writes the pose exactly as `ChairPinSystem.MoveChair` does (ChairPinSystem.cs:1152-1187):

- `rope, t_up = LiftLoopCache.Sample(path, s)`: the track-rope point and the BASE -> TOP unit tangent at s.
- position = rope - (0, drop, 0). drop = the cabin's grip-top (CarrierGripTops: bounds.max.y, 10.50) - `CarrierDef.GripTopAboveRopeM`
  (0.50) = 10.00. The system places the cabin by the ROPE, never by bounds.max.y. The pivot (floor underside) hangs 10 m below.
- rotation = `NetUtils.GetNodeRotation(-t_up, the parked rotation)`. NetUtils.cs:316-324 (=1.6.2) zeroes tangent.y. The cabins stay level
  and the hanger is modeled plumb. The system uses -t_up, never the travel direction, so the cabin makes no 180-degree turn at a
  reversal. The cabin is front / back symmetric. At the base dock -t_up = station +Z, so the running pose there equals the hosted
  identity pose (rope 16.00 - 10.00 = pivot 6.00).
- Idempotence: distance^2 < 1e-6 and |dot(q, q')| > 0.99999 -> no write (ChairPinSystem.cs:1168-1171). Otherwise the system calls
  EntityManager.SetComponentData(cabin, transform). It leaves elevation untouched (ChairPinSystem.cs:1177-1181). It never writes
  InterpolatedTransform per frame (ChairPinSystem.cs:1183-1185), because vanilla copies Transform into it (see Vanilla facts).
- A level carriage on a pitched rope leaves its wheel ends +-2.80 * tan(pitch) off the rope. The design accepts this.

### The parked pose

The parked pose of hosted entry k = `ObjectUtils.LocalToWorld(station Transform, Transform(m_Position, m_Rotation))` (ObjectUtils.cs:244-250
(=1.6.2)). SubObjectSystem uses the same formula for a building's sub-object (SubObjectSystem.cs:1757). Two vanilla adjustments do not
apply:

- No AnchorTop / AnchorCenter: :2459-2488 is Pillar only.
- No OnGround adjustment (:2507-2545) for m_ParentMesh 0, whose prefab entry carries no OnGround flag (ObjectInitializeSystem.cs:711-730).

The pose equals vanilla's own placement TO FLOAT ROUNDING. Bit-equality is unverified. Vanilla evaluates the pose in the Burst-compiled
UpdateSubObjectsJob (:556-557). This system evaluates it under Mono. FMA contraction may differ by an ULP. At worst, the next re-host's
Transform.Equals test (:2607, 1.6.2 :2610) fails. The result is one SetComponent + Updated per re-host, and this system re-parks the
cabin the same frame.

## Carriages (the carrier grip round, TramShuttleSystem.Carriages.cs)

The tram's carrier splits like a chair's. The CARRIAGE (trolley + hanger head) is the grip. The CABIN (hanger + cabin) is the body. The
catalog entry is `SkiTramCarriage`, kind Grip. It is pending until the art import. `run.Def.Pivots` comes from the split build
(SkiCatalog). The pin is 0.2565 above the track-rope contact and 10.2565 above the cabin's lowest vertex.

- **Hosting**: the carriage is a second ObjectSubObjects entry of the base station, one per cabin entry (T138 option A, prefab surgery
  after the import). So `HarvestCarriages` finds it in `HarvestCabins`' own query (Owner = the station). If there are no carriage
  entries yet (or the prefab is missing), every cabin keeps today's rigid pose. A split tram with a riding cabin but no carriage logs
  one INFO per load.
- **Pairing**: carriage i pairs with cabin i. The pairing is the assignment of the base's carriages to its cabins with the least summed
  xz distance. For 2 x 2, the system tries both assignments. For more, it uses greedy nearest. A carriage hangs its cabin straight below
  it, and the two bays are a track gauge apart. So the pairing holds whatever the entity order. The system recomputes the pairing from
  positions at every rebuild. So FindOldSubObject's nearest-by-position reuse never matters. That reuse may swap a carriage's bay
  independently of its old cabin's. The pairing follows AssignCabins. `Run.Carriage` is parallel to run.Cabin (chain A, chain B). It
  lives ON the Run, never keyed by the base station. A base that starts two lines has a winner Run and a cabin-less loser Run. The loser
  must not clear the winner's carriages.
- **Prefab contract**: each carriage entry copies its cabin entry's rotation and x / z. Its y = the cabin entry's y + (BodyPin.y -
  GripPin.y), which is 9.522 on the build's pivots. So the hosted (parked) pose is already the assembled carrier.
- **Enrollment**: the carriages enroll like the cabins (InterpolatedTransform after Created + one Updated, `EnrollCarriages` at
  FillRun). The bounds tags cover the carriage.
- **On the path** (`PlaceSplitOnPath`): the system takes one sample. yaw = `NetUtils.GetNodeRotation(-t_up, the parked rotation)`, as in
  PlaceOnPath (no 180-degree turn at a reversal). `CarrierPose.Pose` uses the travel tangent t_up. The carriage takes the track rope's
  pitch. The cabin hangs plumb from the pin. At zero swing the cabin's position equals today's rigid pose (CP-1). So the dock alignment
  above (rope 16.00 - 10.00 = pivot 6.00) is unchanged.
- **Swing**: the system uses the chairs' convention (EngineNotes/ChairPinSystem.Mover.md, "Pose"). The roll turns carriage AND cabin
  together about the track-rope point and the rope tangent. There is one lateral pin and no cardan. The carriage stays along the rope.
  The fore-aft turns the cabin alone about the pin. The tram's limits are `CarrierPose.Limits(pivots, shuttle: true)`. The roll is at
  most 2 deg (0.35 m at the cabin floor, 10 m below the rope). It has half the fore-aft amplitude (gain 1/6 against ForeAftShare 1/3).
  The long hanger damps it (LiftBatch CP-5, CP-6, CP-18). The swing is zero on the dock runs (the station track: s < BaseRunM or s > L -
  TopRunM, "guided"). It ramps in after them over R = max(10 m, 2 s x speed) (tram: 20 m). The sway argument is the path distance s. It
  is continuous through a reversal and a hold. The gust scales the lean 0.6 to 1.0, as on the chairs (CarrierPose.Gust at the track-rope
  point, clock = CarrierWind.GustTimeS). The swing is subtle by design (2026-11-06, LiftBatch CP-19). At |w| 0.31 the cabin rocks about
  0.8 to 1.0 deg peak to peak in roll and 1.6 to 2.0 deg fore-aft. The period is 5.4 s (omega 1.17).
  Wind: there is one `CarrierWind` per base station. `PollTramWind` samples it at the path's middle. It uses the same synchronous
  `GetMap` / `GetWind` read as ChairPinSystem (CellMapSystem.cs:154-158, WindSystem.cs:68-80). It samples in these cases:
  - Every 512 render frames.
  - On the pass after a rebuild.
  - At once while a split run's wind is unknown. This happens before the pass poses. The first sample is the start value. A late sample
    blends in from zero (LiftBatch CP-17).

  The system stamps the wind only when something was sampled. The wind blends over WindBlendS (8 s). If a read fails, the system logs one
  "!!!" ERROR per load, and the cabins keep their last wind.
- **Parked** (`ParkCarriage` = `CarrierPose.GripOnBody`, CP-16): the cabin keeps its hosted pose. The carriage sits assembled above it,
  with position = cabin + R x (BodyPin - GripPin) and the cabin's rotation. There is no pitch and no swing. This is the parked picture
  of the one-piece cabin.
- **Toggle off** ("Animated lift carriers"): the carriage stays level, with no swing (the rigid pose split in two).

## Scheduling (Mod.cs; load-bearing)

`UpdateAfter<TramShuttleSystem, LiftCensusSystem>(Modification4B)`: census -> this -> ChairPinSystem (UpdateSystem.cs:178-181, :278-292,
:382-441). At Modification4B, three conditions hold:

- The cabins are in their final vanilla state. SubObjectSystem (Modification2B, SystemOrder.cs:131) re-hosts them on a station update,
  and its barrier plays at the end of 2B (:88).
- The track-rope lanes are final (LaneSystem, Modification4, :155).
- Our Transform write lands before Game.Objects.SearchSystem (Modification5, :197), UpdateGroupSystem (:214) and PreCulling (:633).

The system runs after the census, so the census rows (Complete / Mixed / Revision) are this frame's. It runs on the main thread with no
job. It makes at most 2 Transform writes per tram per frame. The phase must stay Modification4B. Otherwise the anchor is skipped and the
system silently never runs.

## Triggers -> rebuild

These events trigger a rebuild:

- A Shuttle cabin in the change probe ({Static, Owner, PrefabRef, Transform} with Created or Updated, no Deleted / Temp).
- A census Revision change (only while a tram station, run or cabin exists, or a census row is of a Shuttle type).
- A rostered cabin gone (Missing).
- The load.
- A catalog re-resolution.
- The watchdog (4096 frames, only while tram stations or cabins exist).

Event triggers bypass the 30-frame cooldown. The watchdog does not (ChairPinSystem.cs:1017-1044). The system adds its own Updated tags
after the probe at 4B, and CleanUpSystem strips them (SystemOrder.cs:54) the same frame. So they never re-trigger.

Census gate: the system writes nothing until the census builds once in this load (IsArmed && Builds > 0). Builds resets per load. After
120 frames without a build, the system runs on the cable path alone and logs one WARN. In that case there are no census rows: Complete is
assumed, and power Unknown = powered.

### Station order and the watchdog signature

The rebuild signature (LogRebuild) and the pair notes name stations by their index in `m_StationList`.
The watchdog WARNs "the shuttle set changed without an event" when a watchdog rebuild writes a different signature. HarvestStations
used to list stations in chunk order. That order moves without a census revision. The icon clear removes IconElement from a new station (IconCommandSystem.cs:415,
1.6.0f1). It re-archetypes the station and reorders the chunks. This happened 13-17 s after a second tram was built (field test). The
F1 bridge run reproduced it. Only the station indices differed, and the run rebuilt.

Fixed: HarvestStations sorts the station list by entity, Index then Version (`LiftLoopTopology.SortedStationOrder`, LiftBatch SH-12). It
sorts the parallel Transform and StationDef lists the same way. It sorts before the owner map is built. A real change of the set (a
station built or gone, a recycled entity) still changes the signature. The sort changes no behavior. Pairs and runs follow lane order.
Lanes are sorted by lane entity (LiftLaneHarvest.SortByLane). A station index is only ever text or a lookup into these same lists.

The same pattern exists elsewhere. ChairPinSystem's station scan is also in chunk order. Its rebuild signature is a count fingerprint
with no station index. Only its build notes in dumps carry station indices.

## Vanilla facts relied on

- SubObjectSystem re-places a building's sub-objects only when it processes that owner (Created / Updated / Deleted or a
  SubObjectsUpdated event, SubObjectSystem.cs:906-1028). Game.Serialization/SubObjectSystem only rebuilds the SubObject buffers on load.
  Between station updates, nothing in vanilla moves a cabin. A load keeps the saved Transform.
- On a station update, vanilla REUSES an existing cabin of the same prefab.
  - FindOldSubObject :3318-3363 (1.6.2 :3322) takes the nearest old entity of that prefab by its CURRENT position. So a mid-span re-host
    may put either entity in either bay. The identity is kept. The bay is irrelevant because we overwrite it.
  - RemoveUnusedOldSubObjects (:1227-1268) deletes only the owned entities left over. The cabins are listed in the prefab, so both are
    reused.
  - The cabin's Transform is reset to the hosted pose (:2607-2610, 1.6.2 +3).
  - Its InterpolatedTransform is REMOVED (:2687-2691). A building owner is never "interpolated" (:922-926).
  - The cabin gets Updated (:2737-2740). Our probe sees it the same frame at 4B -> rebuild -> re-enroll -> rewrite. There is no visible
    flash.
  - EXCEPTIONS: under construction (:2494) and a Destroyed station whose cabin is not on the ground (:2546) return before the reuse. The
    cabins are DELETED. The roster's Missing trigger and the census rebuild cover both.
- Hosted sub-objects of a building keep Game.Objects.Static and are not Relative.
  - Relative applies only under a MovingObjectData or Swaying owner (:916-926).
  - The strip is `relative || boneIndex.y >= 0` (:2895-2900).
  - The bone path needs m_ParentMesh % 1000 > 0 (:2565-2570).
  - So the queries' Static term is right, and ObjectInterpolateSystem does not skip them. It skips Relative / Temp
    (ObjectInterpolateSystem.cs:252).
- InterpolatedTransform is the render path of a moving static object:
  - ObjectInterpolateSystem.cs:252 (gate: the InterpolatedTransform culling flag, NearCamera, not Temp, not Relative) -> :261 -> :266
    UpdateStaticAnimations -> the plain copy :296 (=1.6.2).
  - PreCullingSystem.cs:1918-1920 sets the flag by component presence.
  - UpdateGroupSystem.cs:613 (query :724 {Updated, InterpolatedTransform}, =1.6.2) copies Transform for an Updated entity. This refreshes
    a FAR cabin (not NearCamera) at every bounds refresh.
  - The system enrolls the cabin ONCE per rebuild (AddComponentData when missing + Updated, ChairPinSystem.cs:1494-1504 verbatim).
  - Vanilla InterpolatedTransform is IEmptySerializable (InterpolatedTransform.cs:9). There is no save-format change.
- CullingInfo and the static search tree re-read Transform only on Updated. PreCullingSystem.cs:709-718 reads, per object, the cabin's own
  Transform. Owner only feeds the layer mask (:701). The system adds one Updated tag per BoundsRefreshMeters (2 m) of travel.
- The cabin's UtilityObject grants IgnoreSecondaryCollision (ObjectInitializeSystem.cs:594-597, the else-if after the Pillar branch
  :578-593, 1.6.2 :602). So a hosted cabin is never a validation obstacle. Only RoadConnectionSystem (for Secondary sub-objects, :844,
  :1544) and ObjectColorSystem read Game.Objects.UtilityObject. So a moving cabin cannot capture a power line.
- NetUtils.GetNodeRotation zeroes tangent.y and returns its default for a degenerate tangent (NetUtils.cs:316-324).

## Logs and never

The system logs every state change ("silence must be impossible"):

- One [arm] line per load.
- [rebuild] lines.
- Refusal and cabin-count WARNs (once per text per load).
- Folded [state] transitions (a 10 s LogWindow per shuttle, flushed on load and quit).
- On an exception, "!!! TramShuttleSystem disabled for this load" (re-armed on the next load).

The steady path makes no managed allocation per frame:

- Exit reasons are string literals.
- Numbers are formatted only in log lines and rebuilds.
- Timing uses Stopwatch.GetTimestamp().
- The system takes the probe chunk array only when its query is non-empty.

Never:

- No save data (no new IComponentData / buffer / serializable). Positions come from the clock.
- No Hidden / Deleted on a cabin.
- Never touch a Loop carrier.
- Never touch a Temp entity (tool previews keep vanilla's hosted pose).
- Never touch anything in the Editor (a moved hosted cabin could be saved into a map or asset).

## Cross-references

`ChairPinSystem.Debug.cs` builds this system's foldout and appends AppendDump to its dump. `SkiResortStatsSystem` reads TryGetShuttle,
CabinsHostedBy and DescribeShuttle. `SkiResortHubUISystem` offers the tram items only while this system is registered.
