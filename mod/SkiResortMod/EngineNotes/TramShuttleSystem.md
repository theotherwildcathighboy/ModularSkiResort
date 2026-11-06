# TramShuttleSystem: how the aerial tram's two cabins are driven, gated, identified and placed against vanilla's sub-object system

Citations are Game.dll 1.6.0f1 (tools/decompile/1.6.0f1); "=1.6.2" or a "1.6.2 :N" mark where 1.6.2 shifts a line. The system moves the
aerial tram's two cabins; the dev-tab foldout is `TramShuttleSystem.Debug.cs`.

## What it moves

The Shuttle carriers (a SkiCatalog lift type whose Mover is Shuttle: the tram and its cabin prefab) HOSTED on a Shuttle Base station:
the base prefab's ObjectSubObjects list the two cabins at (9.50 / -1.50, 6.00, -5.50) in the game frame, identity, m_ParentMesh 0; the
top station hosts none. Cabin A rides chain A (the up lane, right looking uphill, `StationAlign.UpLaneIsSecond`), cabin B rides chain B;
both paths run base dock -> top dock (`LiftLoopCache.BuildShuttles`, the open-chain mode: the two chains UNCLOSED, each extended by a
straight run from / to the station's track-rope dock, `StationDef.DockLocalA` / `B` = (9.50 / -1.50, 16.00, -5.50) on the base,
(-5.50 / +5.50, ...) on the top).

- Lanes: the track ropes are the line's ONLY high-voltage lanes (the haul ropes are low-voltage, UtilityType 8, skipped by the HV flag;
  a third HV lane per side would give "3 chain(s), need exactly 2" and no path).
- ChairPinSystem never touches a Shuttle carrier (IsPinnable) and never harvests a Shuttle line's lanes (its LogPrefabState writes the
  hand-off line once per load).
- The prototype is named SkiTramHVProto (not a catalog name): the catalog never finds it, so this system ignores it; imported under the
  real line name it would find no tram cabin and idle.

## Clock

TimeS = `ShuttleProfile.TimeSOf(RenderingSystem.frameIndex, frameTime)` = ChairPinSystem's TimeS bit for bit ((frameIndex + frameTime) /
60 in double, ChairPinSystem.cs:1132-1135; LiftBatch SH-7): frozen on pause (RenderingSystem.cs:187-190), smooth at any speed
(:193-212), resnapped to the saved frame on load (:292-294). Movement runs about 20x slower than the game clock (one game day = 262,144
frames = 4,369 s of movement).

The profile is the pure `ShuttleProfile` (trapezoid at the type's SpeedMps and AccelMps2 with DwellS at each terminal) over L =
`Shuttle.ProfileLengthM` (the mean of L_A and L_B rounded to 1 cm: a float-noise rebuild cannot shift the period). Cabin A at
u = `ShuttleProfile.FractionA(TimeS, L, ...)`, s_A = u * L_A; cabin B at s_B = (1 - u) * L_B (both dock at the same instant, one haul
rope). One clock for every tram, no per-tram phase offset.

## Gate

Evaluated per shuttle at a rebuild, on a power change and on the kill switch, never per frame:

- Running = a shuttle path AND its census row Complete AND not Mixed AND `LiftServedRule.IsPowered(power)` AND not closed.
- Held = a path AND Complete AND not Mixed AND (unpowered OR closed): both cabins stand at the terminals cabin A last reached (cabin A at
  u 0 or 1, cabin B mirrored).
- Parked = everything else (no path, Open, Unfinished, StationsOnly, Mixed, no census row, the kill switch off, an invalid profile):
  every cabin in its hosted pose.

The power and closed tests are ONE predicate, `LiftServedRule.Carries(true, run.Power, IsClosed(run))` - THE run predicate, which
ChairPinSystem's hold and the hub's capacity use too (LiftBatch PG-1). "Closed" is the lift's held-closed flag from
`SkiLiftChargeSystem` (the ski area has no open run).

Power = `LiftCensusSystem.ReadLiftPower(base)`, the mod's only reader of a station's ElectricityConsumer (a sync point on
DispatchElectricitySystem's writers), per shuttle whose census row is Complete, every PowerPollFrames (64) rendered frames, and once per
shuttle at each rebuild (without the rebuild read a city loaded unpowered would run its tram for up to 64 frames first). It is read for
run.Base only: a Complete row has exactly one Base-role and one Top-role station (LiftCensus.cs), and LiftLoopTopology picks the
Base-role station as the shuttle's base by role (Shuttle mode, LiftLoopTopology.cs:293-297), so run.Base IS the row's Base, the station
with the ElectricityConsumer. The top station is never read. The flicker guard is inherited: ReadLiftPower returns the guarded value
(NoPower at once; Powered again after 512 sim frames of power).

### Held latch

`ShuttleProfile.HoldFractionA` is a pure function of the clock: it flips between 0 and 1 every half cycle (172.5 s for 1 km), so an
unlatched Held tram would swap both cabins between the bays every half cycle while it stays unpowered. The mover latches it once per
Held episode (transient, per shuttle, never saved): Running -> Held takes HoldFractionA at the cut (the terminal last reached: back to
the station left, or on to the one reached); any other entry (a load, a rebuild, the kill switch, Parked -> Held) takes the terminal
configuration the cabins already stand at (summed distance <= 1 m), else HoldFractionA. Leaving Held drops the latch; Held -> Running
jumps once to the clock place (stateless by design). Running -> Held also snaps mid-span cabins to the terminals last reached (the
chairs, by contrast, stop where they are).

## Cabin A / B identity (transient, per session, never saved)

Index-keyed state is rejected because entity indices are remapped on load, so "cabin A = the lower (Index, Version)" could swap the
cabins after a load (each would jump to the other rope). The mover keeps a transient map cabin -> hosted BAY (0 = hosted entry 0 = dock
A; 1 = entry 1 = dock B; dock A is the +X bay on the tram BASE and the -X bay on the tram TOP, SkiCatalog `StationDef.DockLocalA` / `B`).
Which chain a bay rides is geometry: chain A's cabin is the one in the bay of chain A's base dock (Path.m_BaseDock; the other bay rides
chain B). A cabin without an entry gets one, per station, by the first rule that applies:

1. FRESH BASE: every cabin within 0.05 m of a hosted pose -> the bay it stands in (the bay is read from the cabin's GEOMETRY against
   Path.m_BaseDock, never from the sign of X: the base's dock A is the +X bay, so "the cabin in hosted bay 0 is A" holds by dock, not by
   side).
2. CLOCK PLACES: the assignment with the smaller sum |Transform - target|, the target being the clock place when Running, the latched
   (or both candidate) terminal configurations when Held, the hosted poses when Parked or when the station has no path. After a load the
   saved Transforms sit at the clock places, so the right mapping costs ~0.

A tie (within 1 mm) -> (Index, Version) order. The [rebuild] line names the rule ("kept", "fresh base", "by clock places, sum x.xx m",
"tie by entity"). Vanilla re-hosting reuses the cabin entities (below), so the map survives it.

## How a cabin is placed

Written exactly as `ChairPinSystem.MoveChair` (ChairPinSystem.cs:1152-1187):

- `rope, t_up = LiftLoopCache.Sample(path, s)`: the track-rope point and the BASE -> TOP unit tangent at s.
- position = rope - (0, drop, 0), where drop = the cabin's grip-top (CarrierGripTops: bounds.max.y, 10.50) -
  `CarrierDef.GripTopAboveRopeM` (0.50) = 10.00 (placed by the ROPE, never by bounds.max.y); the pivot (floor underside) hangs 10 m below.
- rotation = `NetUtils.GetNodeRotation(-t_up, the parked rotation)`; NetUtils.cs:316-324 (=1.6.2) zeroes tangent.y: level cabins, the
  hanger modeled plumb. -t_up, never the travel direction, so no 180-degree turn at a reversal (the cabin is front / back symmetric); at
  the base dock -t_up = station +Z, so the running pose there equals the hosted identity pose (rope 16.00 - 10.00 = pivot 6.00).
- Idempotence: distance^2 < 1e-6 and |dot(q, q')| > 0.99999 -> no write (ChairPinSystem.cs:1168-1171); then
  EntityManager.SetComponentData(cabin, transform). Elevation untouched (ChairPinSystem.cs:1177-1181). InterpolatedTransform never
  written per frame (ChairPinSystem.cs:1183-1185): vanilla copies Transform into it (see Vanilla facts).
- A level carriage on a pitched rope leaves its wheel ends +-2.80 * tan(pitch) off the rope (accepted).

### The parked pose

The parked pose of hosted entry k = `ObjectUtils.LocalToWorld(station Transform, Transform(m_Position, m_Rotation))`
(ObjectUtils.cs:244-250 (=1.6.2)), the formula SubObjectSystem uses for a building's sub-object (SubObjectSystem.cs:1757; no
AnchorTop / AnchorCenter, :2459-2488 is Pillar only; no OnGround adjustment, :2507-2545, for m_ParentMesh 0, whose prefab entry carries
no OnGround flag, ObjectInitializeSystem.cs:711-730). It equals vanilla's own placement TO FLOAT ROUNDING; bit-equality is unverified
(vanilla evaluates it in the Burst-compiled UpdateSubObjectsJob, :556-557, this system under Mono: FMA contraction may differ by an
ULP). At worst the next re-host's Transform.Equals test (:2607, 1.6.2 :2610) fails: one SetComponent + Updated per re-host, and this
system re-parks the cabin the same frame.

## Carriages (the carrier grip round, TramShuttleSystem.Carriages.cs)

The tram's carrier splits like a chair's: the CARRIAGE (trolley + hanger head) is the grip, the CABIN (hanger + cabin) the body. Catalog:
`SkiTramCarriage`, kind Grip, pending until the art import; `run.Def.Pivots` (the split build's, SkiCatalog: the pin 0.2565 above the
track-rope contact, 10.2565 above the cabin's lowest vertex).

- **Hosting**: the carriage is a second ObjectSubObjects entry of the base station per cabin entry (T138 option A, prefab surgery after
  the import), so `HarvestCarriages` finds it in `HarvestCabins`' own query (Owner = the station). No carriage entries yet (or the
  prefab missing) = every cabin keeps today's rigid pose; a split tram with a riding cabin but no carriage logs one INFO per load.
- **Pairing**: carriage i with cabin i = the assignment of the base's carriages to its cabins with the least summed xz distance (2 x 2:
  both tried; more: greedy nearest). A carriage hangs its cabin straight below it and the two bays are a track gauge apart, so the
  pairing holds whatever the entity order. It is recomputed from positions at every rebuild, so FindOldSubObject's
  nearest-by-position reuse (which may swap a carriage's bay independently of its old cabin's) never matters. It follows AssignCabins:
  `Run.Carriage` is parallel to run.Cabin (chain A, chain B) and lives ON the Run, never keyed by the base station: a base that starts
  two lines has a winner and a cabin-less loser Run, and the loser must not clear the winner's carriages.
- **Prefab contract**: each carriage entry copies its cabin entry's rotation and x / z, with y = the cabin entry's y + (BodyPin.y -
  GripPin.y) (9.522 on the build's pivots), so the hosted (parked) pose is already the assembled carrier.
- **Enrollment**: like the cabins (InterpolatedTransform after Created + one Updated, `EnrollCarriages` at FillRun); the bounds tags
  cover the carriage.
- **On the path** (`PlaceSplitOnPath`): one sample; yaw = `NetUtils.GetNodeRotation(-t_up, the parked rotation)` as PlaceOnPath (no
  180-degree turn at a reversal); `CarrierPose.Pose` with the travel tangent t_up: the carriage takes the track rope's pitch, the cabin
  hangs plumb from the pin. The cabin's position equals today's rigid pose at zero swing (CP-1), so the dock alignment above (rope
  16.00 - 10.00 = pivot 6.00) is unchanged.
- **Swing**: the chairs' convention (EngineNotes/ChairPinSystem.Mover.md, "Pose"): the roll turns carriage AND cabin together about
  the track-rope point and the rope tangent (one lateral pin, no cardan; the carriage stays along the rope), the fore-aft turns the cabin alone about the pin. The tram's limits
  (`CarrierPose.Limits(pivots, shuttle: true)`): the roll at most 2 deg (0.35 m at the cabin floor, 10 m below the rope), at half the
  fore-aft amplitude (gain 1/6 against ForeAftShare 1/3: the long hanger damps it; LiftBatch CP-5, CP-6, CP-18). Zero on the dock runs
  (the station track: s < BaseRunM or s > L - TopRunM, "guided"), ramped in after them over R = max(10 m, 2 s x speed) (tram: 20 m);
  the sway argument is the path distance s (continuous through a reversal and a hold).
  Wind: one `CarrierWind` per base station, sampled at the path's middle (`PollTramWind`, the same synchronous `GetMap` / `GetWind`
  read as ChairPinSystem's, CellMapSystem.cs:154-158, WindSystem.cs:68-80) every 512 render frames, on the pass after a rebuild, and at
  once while a split run's wind is unknown (before the pass poses: the first sample is the start value; a late one blends in from zero,
  LiftBatch CP-17); stamped only when something was sampled. Blended over 4 s. A read failure: one "!!!" ERROR per load, the cabins
  keep their last wind.
- **Parked** (`ParkCarriage` = `CarrierPose.GripOnBody`, CP-16): the cabin's hosted pose and the carriage assembled above it, position =
  cabin + R x (BodyPin - GripPin), the cabin's rotation: no pitch, no swing, the parked picture of the one-piece cabin.
- **Toggle off** ("Animated lift carriers"): the carriage stays level, no swing (the rigid pose split in two).
## Scheduling (Mod.cs; load-bearing)

`UpdateAfter<TramShuttleSystem, LiftCensusSystem>(Modification4B)`: census -> this -> ChairPinSystem (UpdateSystem.cs:178-181,
:278-292, :382-441). At Modification4B the cabins are in their final vanilla state (SubObjectSystem, Modification2B, SystemOrder.cs:131,
re-hosts them on a station update and its barrier plays at the end of 2B, :88), the track-rope lanes are final (LaneSystem,
Modification4, :155) and our Transform write lands before Game.Objects.SearchSystem (Modification5, :197), UpdateGroupSystem (:214) and
PreCulling (:633). After the census so its rows (Complete / Mixed / Revision) are this frame's. Main thread, no job: at most 2 Transform
writes per tram per frame. The phase must stay Modification4B or the anchor is skipped and the system silently never runs.

## Triggers -> rebuild

A Shuttle cabin in the change probe ({Static, Owner, PrefabRef, Transform} with Created or Updated, no Deleted / Temp), a census
Revision change (only while a tram station, run or cabin exists, or a census row is of a Shuttle type), a rostered cabin gone
(Missing), the load, a catalog re-resolution, the watchdog (4096 frames, only while tram stations or cabins exist). Event triggers
bypass the 30-frame cooldown; the watchdog does not (ChairPinSystem.cs:1017-1044). Our own Updated tags are added after the probe at 4B
and stripped by CleanUpSystem (SystemOrder.cs:54) the same frame, so they never re-trigger.

Census gate: nothing is written until the census has built once this load (IsArmed && Builds > 0; Builds resets per load); after 120
frames without a build it runs on the cable path alone (no census rows: Complete assumed, power Unknown = powered) with one WARN.

### Station order and the watchdog signature

The rebuild signature (LogRebuild) and the pair notes name stations by their index in `m_StationList`, and the watchdog WARNs "the shuttle set
changed without an event" when a watchdog rebuild writes a different signature. HarvestStations used to list stations in chunk order, and
that order moves without a census revision: the icon clear that removes IconElement from a new station (IconCommandSystem.cs:415,
1.6.0f1) re-archetypes it and reorders the chunks, 13-17 s after a second tram was built (field test; the F1 bridge run reproduced it),
so only the station indices differed and the run rebuilt. Fixed: HarvestStations sorts the station list (with its parallel Transform and
StationDef lists) by entity, Index then Version (`LiftLoopTopology.SortedStationOrder`, LiftBatch SH-12), before the owner map is built.
A real change of the set (a station built or gone, a recycled entity) still changes the signature. The sort moves no behavior: pairs and
runs follow lane order (lanes are sorted by lane entity, LiftLaneHarvest.SortByLane), and a station index is only ever text or a lookup
into these same lists. Same pattern elsewhere: ChairPinSystem's station scan is also in chunk order, but its rebuild signature is a count
fingerprint with no station index (only its build notes in dumps carry them).

## Vanilla facts relied on

- A building's sub-objects are re-placed only when SubObjectSystem processes that owner (Created / Updated / Deleted or a
  SubObjectsUpdated event; SubObjectSystem.cs:906-1028); Game.Serialization/SubObjectSystem only rebuilds the SubObject buffers on
  load. Between station updates nothing vanilla moves a cabin; a load keeps the saved Transform.
- On a station update an existing cabin of the same prefab is REUSED - FindOldSubObject :3318-3363 (1.6.2 :3322) takes the nearest old
  entity of that prefab by its CURRENT position, so a mid-span re-host may put either entity in either bay: identity kept, bay
  irrelevant (we overwrite) - and RemoveUnusedOldSubObjects (:1227-1268) deletes only the owned entities left over (the cabins are
  listed in the prefab, so both are reused). Its Transform is reset to the hosted pose (:2607-2610, 1.6.2 +3), its InterpolatedTransform
  REMOVED (:2687-2691: a building owner is never "interpolated", :922-926) and it gets Updated (:2737-2740): our probe sees it the same
  frame at 4B -> rebuild -> re-enroll -> rewrite, no visible flash. EXCEPTIONS: under construction (:2494) and a Destroyed station whose
  cabin is not on the ground (:2546) return before the reuse and the cabins are DELETED - the roster's Missing trigger and the census
  rebuild cover both.
- Hosted sub-objects of a building keep Game.Objects.Static and are not Relative: Relative only under a MovingObjectData or Swaying
  owner (:916-926), the strip is `relative || boneIndex.y >= 0` (:2895-2900), the bone path needs m_ParentMesh % 1000 > 0
  (:2565-2570). So the queries' Static term is right and ObjectInterpolateSystem does not skip them (it skips Relative / Temp,
  ObjectInterpolateSystem.cs:252).
- InterpolatedTransform is the render path of a moving static object: ObjectInterpolateSystem.cs:252 (gate: the InterpolatedTransform
  culling flag, NearCamera, not Temp, not Relative) -> :261 -> :266 UpdateStaticAnimations -> the plain copy :296 (=1.6.2);
  PreCullingSystem.cs:1918-1920 sets the flag by component presence; UpdateGroupSystem.cs:613 (query :724 {Updated,
  InterpolatedTransform}, =1.6.2) copies Transform for an Updated entity, which refreshes a FAR cabin (not NearCamera) at every bounds
  refresh. Enrolled ONCE per rebuild (AddComponentData when missing + Updated, ChairPinSystem.cs:1494-1504 verbatim); vanilla
  InterpolatedTransform is IEmptySerializable (InterpolatedTransform.cs:9): no save-format change.
- CullingInfo and the static search tree re-read Transform only on Updated (PreCullingSystem.cs:709-718: per object, the cabin's own
  Transform; Owner only feeds the layer mask, :701): one Updated tag per BoundsRefreshMeters (2 m) of travel.
- The cabin's UtilityObject grants IgnoreSecondaryCollision (ObjectInitializeSystem.cs:594-597, the else-if after the Pillar branch
  :578-593; 1.6.2 :602): a hosted cabin is never a validation obstacle. Game.Objects.UtilityObject is read only by RoadConnectionSystem
  for Secondary sub-objects (:844, :1544) and ObjectColorSystem: a moving cabin cannot capture a power line.
- NetUtils.GetNodeRotation zeroes tangent.y and returns its default for a degenerate tangent (NetUtils.cs:316-324).

## Logs and never

Every state change is logged ("silence must be impossible"): one [arm] line per load, [rebuild] lines, refusal and cabin-count WARNs
(once per text per load), folded [state] transitions (a 10 s LogWindow per shuttle, flushed on load and quit), and on an exception
"!!! TramShuttleSystem disabled for this load" (re-armed on the next load). No managed allocation per frame on the steady path: exit
reasons are string literals, numbers are formatted only in log lines and rebuilds, timing uses Stopwatch.GetTimestamp(), the probe chunk
array is taken only when its query is non-empty.

Never: no save data (no new IComponentData / buffer / serializable; positions come from the clock); no Hidden / Deleted on a cabin;
never touches a Loop carrier, a Temp entity (tool previews keep vanilla's hosted pose) or anything in the Editor (a moved hosted cabin
could be saved into a map or asset).

## Cross-references

`ChairPinSystem.Debug.cs` builds this system's foldout and appends AppendDump to its dump; `SkiResortStatsSystem` reads TryGetShuttle,
CabinsHostedBy and DescribeShuttle; `SkiResortHubUISystem` offers the tram items only while this system is registered.
