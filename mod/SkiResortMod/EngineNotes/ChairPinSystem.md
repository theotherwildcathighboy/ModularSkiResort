# ChairPinSystem: how lift chairs are pinned to the cable, moved along the loop, held without power and culled

`ChairPinSystem` re-seats every lift-chair sub-object so its grip top lies exactly on the cable-lane bezier (no prefab-side height is
guessed) and, while `MovingChairs` is true, moves the chairs along their lift's closed loop. Engine citations are `File.cs:line`
against the Game.dll decompile baseline 1.6.0f1.

## Why the system exists

Vanilla `SubObjectSystem` places chairs on the edge bezier (EdgeMiddle + EvenSpacing, `SubObjectSystem.CreateSubObjects`, line ~1739) at
a prefab-authored Y offset. The cable lane the player sees is the anchored, sagging one: `LaneSystem` moves its endpoints onto
tower/station anchor stubs and droops it by `UtilityLaneData.m_Hanging`.

## Scheduling (load-bearing)

`SubObjectSystem` spawns/moves chairs at Modification2B (its barrier plays at end of 2B) and tags them Updated; `LaneSystem` rebuilds
lane curves at Modification4 (ModificationBarrier4). This system runs at Modification4B: chairs exist, cable curves are final, and the
Transform write lands before `Game.Objects.SearchSystem` (Modification5) and PreCulling read it, so the search tree and the renderer
both see the pinned position with no extra event round-trip. The Created/Updated tags live until `CleanUpSystem` (Cleanup phase), so
the Updated-filtered query reliably sees everything that changed this frame.

## How a chair is pinned

All game types verified against the decompiled Game.dll.

1. Chairs are found generically: Owner + PrefabRef + Transform, excluding Deleted/Temp, with PrefabRef matched against the chair
   prefab set resolved per load by `SkiPrefabCatalogSystem` (SkiCatalog `Lifts[*].CarrierPrefabs`). Not Secondary.
2. Owner is the lift edge entity; its `Game.Net.SubLane` buffer holds the lanes. Cable lanes are recognized structurally, not by name:
   lane PrefabRef -> `UtilityLaneData` with the HighVoltageLine flag (0x40). This skips the LV comm lane (LowVoltageLine, x 0) and
   works unchanged if the HV lane is ever swapped for a custom haul-rope clone that keeps the utility class.
3. Side matching: the two cable lanes run at x +-1.8 and the chair hangs at x +-1.8. On straight spans the chair's own-side lane is
   the one directly above it (~2.4 m) and the far lane is >= 3.6 m away, so nearest-curve-distance suffices, but hanging lanes are
   straight XZ chords, so on a curved span (sagitta > 1.8 m) the far chord can become 3D-nearest. Matching is therefore by lateral
   sign against the owner edge's curve first (SideSign), with nearest-distance only ranking same-side candidates.
4. Grip-top-on-curve: the chair prefab's `ObjectGeometryData.m_Bounds.max.y` is the pivot->top distance of the imported mesh. The
   Blender contract (PIPELINE section 1) makes the grip the topmost geometry with the pivot at the lowest point, so bounds.max.y is
   exactly the pivot->grip-top distance, read at runtime per chair prefab, never hardcoded:
   `pinnedPosition = lanePoint - (0, bounds.max.y, 0)`.
5. The chair hangs plumb: rotation is yaw-only from the lane tangent via `NetUtils.GetNodeRotation` (it zeroes tangent.y, as vanilla
   does for sub-objects). The two rows face opposite travel directions (authored via the prefab's local rotations), so the tangent
   sign is chosen to preserve the chair's current facing.

## Reactivity (static pinning)

Event-driven, never per-frame. Two triggers: chairs carrying Updated (creation and every `SubObjectSystem` reposition,
`SubObjectSystem` lines ~2824/3050), and one full pass on the first update after game/editor load (saved chairs carry no Updated tag;
chairs moved in that pass get Updated added so culling/search refresh).

## Defensive contract

Never throw into the game loop (top-level try/catch, error logged once). A chair whose edge has no matching cable sublane (stations
mid-build, weird states) is left untouched. A missing chair prefab is logged per load and idles the system for the load.

## Moving chairs (decompile-verified)

- Chairs do not carry `Game.Objects.Secondary`: SubObjectSystem's creation path adds only Owner+PrefabRef+Transform
  (`SubObjectSystem.cs:2824-2827`); the only Secondary writer is `SecondaryObjectSystem` (:4097), a different pipeline. The queries
  therefore do not require it; the chair-prefab set filter is the real selector.
- Clock: `phase_m = speed * (RenderingSystem.frameIndex + frameTime) / 60`, in double (frameIndex ~1e7+ in mature saves; float32 would
  quantize at meter scale). Vanilla interpolation consumes the same clock: it freezes on pause (`RenderingSystem.cs:187-190` sets
  frameDelta=0), slews smoothly toward the sim clock at any speed (:193-222, atan-clamped), and resnaps to the serialized
  `SimulationSystem.frameIndex` on load, so pause/speed/save-continuity come for free.
- Loop: `LiftLoopCache` chains the lift's HV cable lanes end-to-end by exact PathNode equality (FindAnchor rewrites endpoint+PathNode
  onto shared anchors, `LaneSystem.cs:4049-4072/:4117-4120`), terminates at station-owned anchor PathNodes, and closes the circuit with
  synthesized bullwheel semicircle arcs between the two chains' terminal endpoints. Chairs get even loop offsets; every position is
  derived (`s = (offset + phase) mod length`): no per-chair mutable state, nothing serialized.
- Rendering: each riding chair is enrolled once with vanilla `InterpolatedTransform` (IEmptySerializable, no save-format liability)
  plus one Updated tag. `ObjectInterpolateSystem.UpdateStaticAnimations` then copies our per-frame Transform into InterpolatedTransform
  for NearCamera entities (`ObjectInterpolateSystem.cs:296`, the no-TransformFrame branch), `PreCullingFlags.InterpolatedTransform` is
  set by component presence (`PreCullingSystem.cs:1918-1920`), and `BatchDataSystem` re-uploads the instance TRS every rendered frame.
  Direct Transform writes happen at Modification4B, the same frame slot where SubObjectSystem (2B) resets chairs on lift edits, so
  there is no vanilla-pose flash. `UpdateGroupSystem`'s Updated+InterpolatedTransform job is a no-op for bufferless chairs
  (TryGetBuffer fails at :579; :613 re-copies our Transform).
- Culling: a bounds refresh (Updated on all riding chairs) every `BoundsRefreshMeters` of travel keeps CullingInfo and the static
  search tree within ~2 m of the true position. Tags added at 4B are stripped by `CleanUpSystem` the same frame, so they never retrigger
  this system's own change detection.
- Degradation: lifts whose cable chains do not close into a loop (unfinished line, missing station) fall back to static pinning per
  chair; `MovingChairs=false` restores the static flow wholesale.

### RequestImmediateRebuild(why)

Marks the cache dirty and bypasses `RebuildCooldownFrames` once. `LiftCensusSystem` (Modification4B, anchored before this system)
calls it when lift topology changed, so the loop over a deleted span is dropped the same frame instead of chairs riding cached beziers
across the gap for up to 30 frames, and once more the frame after a park (the re-seated chairs are static-pinned onto their own lanes
at once).

### Restart seeding (BuildLoopCache step 4)

When a loop's identity carry-forward fails and none of its chairs rode a loop in the previous cache (the lift has just closed, first
time or after a section was redrawn), each chair starts from where it stands: `offset = ProjectOnOwner(loop, its owner, its grip
point) - phase`. Spacing may be uneven at the turns. Any chair whose owner has no segment, or whose grip point is farther than
`ApproachPinDistanceCeiling` from its own lane (a rider: after a game load the saved chairs stand anywhere on the loop), sends the whole
loop back to the entity-index grid, as do all other non-identity rebuilds (chairs that were already riding keep that behavior).

A refused seeding writes one line per rebuild, `ChairPinSystem [moving] OD-7: N loop(s) could not restart from where their chairs
stood ...`, naming per loop (at most `SeedRefusalLoopsLogged`) its stations, the first failing chair, its section and the cause. The
first rebuild after a load is marked as expected.

### Per lift type

1. Lanes: a lane counts when its owner's prefab is in the catalog's `LiftLineTypes` view and that type's Mover is Loop
   (`CableLane.m_TypeIndex` = its row). Shuttle lines (the tram) are not harvested.
2. Clock per loop: `TimeS = (RenderingSystem.frameIndex + frameTime) / 60` in double; `phase(loop) = loop.m_SpeedMps x TimeS` with
   `m_SpeedMps` = the loop type's SpeedMps. The pause skip compares TimeS; restart seeding uses the loop's own phase. A loop whose lanes
   span several types (`LiftLoopCache` m_TypeIndex -1) gets no roster: its chairs static-pin, one WARN per rebuild signature.
3. Roster: the `CarrierGripTops` view minus every carrier whose `CarrierTypes` row is a Shuttle (excluded from the roster,
   `CountMatchingChairs` and the static pin: a tram cabin keeps its hosted pose). A carrier whose type differs from its loop's rides
   the loop anyway (it needs a mixed net; counted in the dump).
4. Spacing check: when a loop carries N > floor(loop / SpacingM) carriers, one WARN per lift type per load (the line prefab's
   m_Spacing is below the catalog's); all of them ride, denser than designed. Fewer is the normal case (EvenSpacing spawns
   `n = (int)(L / s - 0.5)` per edge, `SubObjectSystem.cs:1743`; the loop adds arcs and terminal runs) and logs nothing.
5. Ceiling per type: the static pin's approach ceiling = the carrier's `LiftTypeDef.ApproachPinCeilingM` (`max(4.0, HangM + 1.5)`:
   fixed-grip 4.0, detachable 4.8, gondola 6.0); restart seeding and its refusal text use the loop type's.
6. Bounds refresh every `BoundsRefreshMeters` of travel of the fastest moving loop: `(TimeS - last) x max speed`.
7. Up lane: `LiftLoopCache` makes chain A the cable on the right looking uphill; this system writes
   `ChairPinSystem [loop] up lane = right looking uphill: ...` once per loop (station pair) per load. Each chair still faces its
   travel and rides the same loop.

### Hold: an unpowered lift's chairs stop

A lift's power is its base station's. Per loop (station pair), transient, no save data:

1. Poll: every `HoldPollFrames` (64) of the render clock (`RenderingSystem.frameIndex`: nothing while paused, the clock is frozen,
   `RenderingSystem.cs:187-190`) and once at each rebuild, the tram's cadence. Per loop: the census row of {m_BaseStation,
   m_TopStation} in either order (a loop's base is the lower terminal, geometry; the row's Base is the Base-role station, the one with
   the ElectricityConsumer), and for a Complete row `LiftCensusSystem.ReadLiftPower(row's Base)`, guarded against flicker there (512
   sim frames before a NoPower lift counts as powered again). Never the top station. Gate = `LiftLoopHold.GateOf(rowKnown, complete,
   power, closed: false)`. No row, a row not Complete, or the census not armed -> Run (fail open; one Info line per lift per load for
   a loop with chairs and no Complete row while the census is armed; one WARN per load when the census is not armed 120 frames after
   the load).
2. Hold (Run -> Hold): `FrozenTimeS` = this frame's clock; that frame writes the frozen pose, `(offset_i +
   LiftLoopHold.FrozenPhase(speed, FrozenTimeS)) % L` (PhaseOf's product at the frozen clock bit for bit, i.e. the very pose a running
   pass writes this frame), and tags every chair Updated once (CullingInfo and the search tree re-read Transform only on Updated,
   `PreCullingSystem.cs:709-718`), then nothing: no Transform write, skipped by the bounds refresh. A frozen chair cannot jitter:
   chairs have no TransformFrame buffer, so `ObjectInterpolateSystem` takes the static branch (:252-263) and copies Transform as is
   (:296); the culling flag follows component presence (`PreCullingSystem.cs:1918-1920`). The chairs stay rostered (`TryGetLoop` keeps
   "riding" > 0, so the hub reads "No power", not "Chairs stopped") and enrolled (no structural change).
3. Resume (Hold -> Run): every `offset := LiftLoopHold.Rebase(offset, speed, FrozenTimeS, now, L)`, so `(offset' + speed x now) % L` is
   the frozen place to ~1e-9 m, far inside `MoveChair`'s 1 mm idempotence test: the resume frame writes nothing new and the next frame
   advances by `speed x dt`. Every offset shifts by the same amount, so the spacing is kept. No jump, no speed ramp.
4. Rebuilds: an identity rebuild carries the offsets by entity and the hold by pair, so a held loop stays put (its chairs are
   re-written at the frozen pose, idempotent). A held loop whose offsets this rebuild did not carry (restart seed or grid;
   `BuildLoopCache` records the pair, a record-only edit) is re-frozen at this frame's clock (= the rebuild's `seedTimeS`): seeded ->
   exactly where its chairs stood; gridded -> once to its grid places, as a running loop would. A pair with no loop any more is
   dropped. At a load the first rebuild grids the saved riders (seeding refuses them) and its poll holds an unpowered loop there: a
   held lift's chairs may shift once at a load.

With no hold, the move pass, `PhaseOf`, `TimeS`, `MoveChair`, the bounds refresh and `BuildLoopCache`'s slot assignment run unchanged;
the poll is read-only and the move loop gains a held branch taken only while a loop is held. The 64-frame power read can see a
one-dispatch shortfall window a 256-frame poller would miss (see the `LiftServedRule` header).

### Scale

Target: 20 lifts, ~3,000 chairs, < 0.5 ms a frame on the main thread, no cost that grows with the city. The running loops are moved by
a Burst job (`ChairPinSystem.Mover.cs`) with far-loop pacing (`LiftMoverMath.cs`); cable lanes come from the census's line edges
(`ChairPinSystem.Harvest.cs`). The managed per-chair path (`MoveChair`) stays for held loops and the dev tab's A/B switch.

## Near MaxPinDistanceMargin and EndClampMeters

On the final edge (last tower -> station) the cable lane is not the edge curve: `LaneSystem` moves the lane's endpoints onto the anchor
stubs (tower sheave tips / the station's bullwheel stubs, which sit several meters past the edge node inside the station) and then
rebuilds the bezier as straight-line-plus-droop between the anchors. Near the station that lane can diverge laterally and vertically
from the chair row's spawn line, so projecting chairs onto it gave visibly wrong positions and yaw there. Two guards, both inert on
normal spans:

- `MaxPinDistanceMargin`: a chair sits ~gripTop below its own cable (pre-pin distance ~ gripTop everywhere on normal spans, sag
  included, because the edge curve droops with the same formula). If the best lane is farther than gripTop + margin the geometry is
  degenerate (or a wrong-side match: the far lane is >= sqrt(3.6^2 + gripTop^2) ~ 4.3 m away, above the threshold by construction):
  the chair stays at its vanilla spawn placement.
- `EndClampMeters`: keeps the pin parameter this many meters of arc away from the lane's extreme ends so no chair ever sits on an
  anchor stub / bullwheel point.

Station bullwheel wrap-arc sublanes can never be candidates here: only the chair's owner edge `SubLane` buffer is walked, and the wrap
arcs live on the station building's own sublanes.

## Near StationEndClampMeters and ApproachPinDistanceCeiling

Chairs ride the rope on every span, like real carriers descending into a terminal: position and yaw come from the lane curve everywhere.
Blending toward the authored row pose near stations lifted chairs off the visible rope, and the rope is what the eye checks against.

- `ApproachPinDistanceCeiling`: on approach edges large divergence from the row line is expected, so the plausibility ceiling is the
  wrong-side floor (~4.3 m), not gripTop + margin. The mid-span guard never fires there, so there is no zigzag of pinned and
  spawn-pose chairs.
- `StationEndClampMeters`: the anchored lane continues past the edge node onto the station's bullwheel stub inside the building; the
  station-side end clamp is raised so the last chair holds just outside the station face. The station side of the lane curve is found
  geometrically (nearest curve end to the station node): lane t-direction vs edge node order is not assumed.
