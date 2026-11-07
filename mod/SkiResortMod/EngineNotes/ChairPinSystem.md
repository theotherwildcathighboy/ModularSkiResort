# ChairPinSystem: how lift chairs are pinned to the cable, moved along the loop, held without power and culled

`ChairPinSystem` re-seats every lift-chair sub-object. After the re-seat, its rope point (the grip jaw) lies exactly on the cable-lane
bezier. The system guesses no prefab-side height. While `MovingChairs` is true, the system also moves the chairs along their lift's closed
loop. Engine citations are `File.cs:line` against the Game.dll decompile baseline 1.6.0f1.

## Why the system exists

Vanilla `SubObjectSystem` places chairs on the edge bezier (EdgeMiddle + EvenSpacing, `SubObjectSystem.CreateSubObjects`, line ~1739) at
a prefab-authored Y offset. The cable lane the player sees is the anchored, sagging one. `LaneSystem` moves its endpoints onto
tower/station anchor stubs and droops it by `UtilityLaneData.m_Hanging`.

## Scheduling (load-bearing)

`SubObjectSystem` spawns or moves chairs at Modification2B (its barrier plays at end of 2B). It tags them Updated. `LaneSystem` rebuilds
lane curves at Modification4 (ModificationBarrier4). This system runs at Modification4B. At that point the chairs exist and the cable
curves are final. The Transform write lands before `Game.Objects.SearchSystem` (Modification5) and PreCulling read it. So the search tree
and the renderer both see the pinned position with no extra event round-trip. The Created/Updated tags live until `CleanUpSystem`
(Cleanup phase). So the Updated-filtered query reliably sees everything that changed this frame.

## How a chair is pinned

All game types verified against the decompiled Game.dll.

1. The system finds chairs generically: Owner + PrefabRef + Transform, excluding Deleted/Temp. It matches PrefabRef against the chair
   prefab set that `SkiPrefabCatalogSystem` resolves per load (SkiCatalog `Lifts[*].CarrierPrefabs`). Chairs are not Secondary.
2. Owner is the lift edge entity. Its `Game.Net.SubLane` buffer holds the lanes. The system recognizes cable lanes structurally, not by
   name: lane PrefabRef -> `UtilityLaneData` with the HighVoltageLine flag (0x40). This skips the LV comm lane (LowVoltageLine, x 0). It
   works unchanged if someone swaps the HV lane for a custom haul-rope clone that keeps the utility class.
3. Side matching: the two cable lanes run at x +-1.8 and the chair hangs at x +-1.8. On straight spans, the chair's own-side lane is
   the one directly above it (~2.4 m). The far lane is >= 3.6 m away. So nearest-curve-distance suffices on straight spans. But hanging
   lanes are straight XZ chords. On a curved span (sagitta > 1.8 m), the far chord can become 3D-nearest. So matching is by lateral
   sign against the owner edge's curve first (SideSign). Nearest-distance only ranks same-side candidates.
4. Rope-point-on-curve (lift alignment F5, 2026-11-02). Until then the grip TOP rode the lane (round-2 contract D8). The chair
   prefab's `ObjectGeometryData.m_Bounds.max.y` is the pivot->top distance of the imported mesh. The Blender contract (PIPELINE
   section 1) makes the grip the topmost geometry with the pivot at the lowest point. So bounds.max.y is exactly the pivot->grip-top
   distance. The system reads it at runtime per chair prefab and never hardcodes it. The rope runs through the jaw, `GripTopAboveRopeM`
   below the grip top (catalog/carriers.json: FG 0.08, DQ 0.22, GD 0.08, tram 0.5). So:
   `pinnedPosition = lanePoint - (0, LiftMoverMath.CarrierDrop(bounds.max.y, GripTopAboveRopeM), 0)`
   This uses the catalog's `CarrierDrops` view. The tram uses the same helper.
5. The chair hangs plumb. Rotation is yaw-only from the lane tangent via `NetUtils.GetNodeRotation` (it zeroes tangent.y, as vanilla
   does for sub-objects). The two rows face opposite travel directions (authored via the prefab's local rotations). So the system
   chooses the tangent sign to preserve the chair's current facing.

## Reactivity (static pinning)

The system is event-driven, never per-frame. It has two triggers:

- Chairs carrying Updated (creation and every `SubObjectSystem` reposition, `SubObjectSystem` lines ~2824/3050).
- One full pass on the first update after game/editor load. Saved chairs carry no Updated tag. Chairs moved in that pass get Updated
  added, so culling/search refresh.

## Defensive contract

Never throw into the game loop (top-level try/catch, error logged once). The system leaves untouched a chair whose edge has no matching
cable sublane (stations mid-build, weird states). The system logs a missing chair prefab per load and idles for the load.

## Moving chairs (decompile-verified)

- Chairs do not carry `Game.Objects.Secondary`. SubObjectSystem's creation path adds only Owner+PrefabRef+Transform
  (`SubObjectSystem.cs:2824-2827`). The only Secondary writer is `SecondaryObjectSystem` (:4097), a different pipeline. The queries
  therefore do not require Secondary. The chair-prefab set filter is the real selector.
- Clock: `phase_m = speed * (RenderingSystem.frameIndex + frameTime) / 60`, in double. frameIndex is ~1e7+ in mature saves, and float32
  would quantize at meter scale. Vanilla interpolation consumes the same clock. It freezes on pause (`RenderingSystem.cs:187-190` sets
  frameDelta=0). It slews smoothly toward the sim clock at any speed (:193-222, atan-clamped). It resnaps to the serialized
  `SimulationSystem.frameIndex` on load. So pause, speed and save-continuity come for free.
- Loop: `LiftLoopCache` chains the lift's HV cable lanes end-to-end by exact PathNode equality. FindAnchor rewrites endpoint+PathNode
  onto shared anchors (`LaneSystem.cs:4049-4072/:4117-4120`). The chain ends at station-owned anchor PathNodes. The cache closes the
  circuit with synthesized bullwheel semicircle arcs between the two chains' terminal endpoints. Chairs get even loop offsets. The
  system derives every position (`s = (offset + phase) mod length`). There is no per-chair mutable state and nothing is serialized.
- Rendering: the system enrolls each riding chair once with vanilla `InterpolatedTransform` (IEmptySerializable, no save-format
  liability) plus one Updated tag. Then these steps happen:
  - `ObjectInterpolateSystem.UpdateStaticAnimations` copies our per-frame Transform into InterpolatedTransform for NearCamera entities
    (`ObjectInterpolateSystem.cs:296`, the no-TransformFrame branch).
  - `PreCullingFlags.InterpolatedTransform` is set by component presence (`PreCullingSystem.cs:1918-1920`).
  - `BatchDataSystem` re-uploads the instance TRS every rendered frame.
  - Direct Transform writes happen at Modification4B. This is the same frame slot where SubObjectSystem (2B) resets chairs on lift
    edits. So there is no vanilla-pose flash.
  - `UpdateGroupSystem`'s Updated+InterpolatedTransform job is a no-op for bufferless chairs (TryGetBuffer fails at :579, and :613
    re-copies our Transform).
- Culling: a bounds refresh keeps CullingInfo and the static search tree within ~2 m of the true position. The refresh (Updated on all riding chairs) runs every `BoundsRefreshMeters` of travel. `CleanUpSystem` strips tags added at 4B the same frame, so they never retrigger
  this system's own change detection.
- Degradation: lifts whose cable chains do not close into a loop (unfinished line, missing station) fall back to static pinning per
  chair. `MovingChairs=false` restores the static flow wholesale.

### RequestImmediateRebuild(why)

It marks the cache dirty and bypasses `RebuildCooldownFrames` once. `LiftCensusSystem` (Modification4B, anchored before this system)
calls it in two cases:

- When lift topology changed. The system drops the loop over a deleted span the same frame. Without the call, chairs would ride cached
  beziers across the gap for up to 30 frames.
- Once more the frame after a park. The re-seated chairs are static-pinned onto their own lanes at once.

### Restart seeding (BuildLoopCache step 4)

This case applies when a loop's identity carry-forward fails and none of its chairs rode a loop in the previous cache. This happens when
the lift has just closed, first time or after a section was redrawn. Each chair starts from where it stands: `offset =
ProjectOnOwner(loop, its owner, its grip point) - phase`. Spacing may be uneven at the turns. Two cases send the whole loop back to the
entity-index grid:

- Any chair whose owner has no segment.
- Any chair whose grip point is farther than `ApproachPinDistanceCeiling` from its own lane. This is a rider: after a game load the
  saved chairs stand anywhere on the loop.

All other non-identity rebuilds also use the grid (chairs that were already riding keep that behavior).

A refused seeding writes one line per rebuild: `ChairPinSystem [moving] OD-7: N loop(s) could not restart from where their chairs
stood ...`. The line names, per loop (at most `SeedRefusalLoopsLogged`), its stations, the first failing chair, its section and the
cause. The system marks the first rebuild after a load as expected.

### Per lift type

1. Lanes: a lane counts when its owner's prefab is in the catalog's `LiftLineTypes` view and that type's Mover is Loop
   (`CableLane.m_TypeIndex` = its row). The system does not harvest Shuttle lines (the tram).
2. Clock per loop: `TimeS = (RenderingSystem.frameIndex + frameTime) / 60` in double. `phase(loop) = loop.m_SpeedMps x TimeS`, with
   `m_SpeedMps` = the loop type's SpeedMps. The pause skip compares TimeS. Restart seeding uses the loop's own phase. A loop whose lanes
   span several types (`LiftLoopCache` m_TypeIndex -1) gets no roster. Its chairs static-pin, with one WARN per rebuild signature.
3. Roster: the `CarrierGripTops` view minus every carrier whose `CarrierTypes` row is a Shuttle. The roster, `CountMatchingChairs` and
   the static pin exclude those carriers. A tram cabin keeps its hosted pose. A carrier whose type differs from its loop's rides
   the loop anyway. This needs a mixed net. The dump counts these carriers.
4. Spacing check: when a loop carries N > floor(loop / SpacingM) carriers, the system logs one WARN per lift type per load. In this case the line prefab's m_Spacing is below the catalog's. All of them ride, denser than designed. Fewer carriers is the normal case and logs
   nothing. EvenSpacing spawns `n = (int)(L / s - 0.5)` per edge (`SubObjectSystem.cs:1743`). The loop adds arcs and terminal runs.
5. Ceiling per type: the static pin's approach ceiling = the carrier's `LiftTypeDef.ApproachPinCeilingM` (`max(4.0, HangM + 1.5)`:
   fixed-grip 4.0, detachable 4.8, gondola 6.0). Restart seeding and its refusal text use the loop type's ceiling.
6. Bounds refresh every `BoundsRefreshMeters` of travel of the fastest moving loop: `(TimeS - last) x max speed`.
7. Up lane: `LiftLoopCache` makes chain A the cable on the right looking uphill. This system writes
   `ChairPinSystem [loop] up lane = right looking uphill: ...` once per loop (station pair) per load. Each chair still faces its
   travel and rides the same loop.

### Hold: an unpowered lift's chairs stop

A lift's power is its base station's. Each item below applies per loop (station pair). The state is transient and has no save data.

1. Poll: every `HoldPollFrames` (64) of the render clock and once at each rebuild, the tram's cadence. The render clock is
   `RenderingSystem.frameIndex`. Nothing happens while paused, because the clock is frozen (`RenderingSystem.cs:187-190`). Per loop,
   the system does these steps:
   - It finds the census row of {m_BaseStation, m_TopStation} in either order. A loop's base is the lower terminal (geometry). The
     row's Base is the Base-role station, the one with the ElectricityConsumer.
   - For a Complete row, it calls `LiftCensusSystem.ReadLiftPower(row's Base)`. The call is guarded against flicker there (512
     sim frames before a NoPower lift counts as powered again).
   - It never reads the top station.

   Gate = `LiftLoopHold.GateOf(rowKnown, complete, power, closed: false)`. The gate is Run (fail open) in these cases: no row, a row not
   Complete, or the census not armed. The log lines are:
   - One Info line per lift per load, for a loop with chairs and no Complete row while the census is armed.
   - One WARN per load, when the census is not armed 120 frames after the load.
2. Hold (Run -> Hold): `FrozenTimeS` = this frame's clock. On that frame, the system does two things:
   - It writes the frozen pose, `(offset_i + LiftLoopHold.FrozenPhase(speed, FrozenTimeS)) % L`. This is PhaseOf's product at the
     frozen clock bit for bit, i.e. the very pose a running pass writes this frame.
   - It tags every chair Updated once. CullingInfo and the search tree re-read Transform only on Updated
     (`PreCullingSystem.cs:709-718`).

   After that frame, the system does nothing: no Transform write, and the bounds refresh skips the loop. A frozen chair cannot jitter.
   Chairs have no TransformFrame buffer, so `ObjectInterpolateSystem` takes the static branch (:252-263) and copies Transform as is
   (:296). The culling flag follows component presence (`PreCullingSystem.cs:1918-1920`). The chairs stay rostered (`TryGetLoop` keeps
   "riding" > 0, so the hub reads "No power", not "Chairs stopped") and enrolled (no structural change).
3. Resume (Hold -> Run): every `offset := LiftLoopHold.Rebase(offset, speed, FrozenTimeS, now, L)`. So `(offset' + speed x now) % L` is
   the frozen place to ~1e-9 m. This is far inside `MoveChair`'s 1 mm idempotence test. The resume frame writes nothing new, and the
   next frame advances by `speed x dt`. Every offset shifts by the same amount, so the spacing is kept. There is no jump and no speed
   ramp.
4. Rebuilds: an identity rebuild carries the offsets by entity and the hold by pair, so a held loop stays put. Its chairs are
   re-written at the frozen pose (idempotent). A held loop whose offsets this rebuild did not carry (restart seed or grid) is re-frozen
   at this frame's clock (= the rebuild's `seedTimeS`). `BuildLoopCache` records the pair (a record-only edit). The re-frozen pose is:
   - Seeded: exactly where its chairs stood.
   - Gridded: once to its grid places, as a running loop would.

   A pair with no loop any more is dropped. At a load, the first rebuild grids the saved riders (seeding refuses them). Its poll holds
   an unpowered loop there. So a held lift's chairs may shift once at a load.

With no hold, the move pass, `PhaseOf`, `TimeS`, `MoveChair`, the bounds refresh and `BuildLoopCache`'s slot assignment run unchanged.
The poll is read-only. The move loop gains a held branch that runs only while a loop is held. The 64-frame power read can see a
one-dispatch shortfall window that a 256-frame poller would miss (see `LiftServedRule.md`, GuardPower).

### Scale

Target: 20 lifts, ~3,000 chairs, < 0.5 ms a frame on the main thread, no cost that grows with the city. A Burst job moves the running
loops (`ChairPinSystem.Mover.cs`) with far-loop pacing (`LiftMoverMath.cs`). Cable lanes come from the census's line edges
(`ChairPinSystem.Harvest.cs`). The managed per-chair path (`MoveChair`) stays for held loops and the dev tab's A/B switch.

## Near MaxPinDistanceMargin and EndClampMeters

On the final edge (last tower -> station), the cable lane is not the edge curve. `LaneSystem` moves the lane's endpoints onto the anchor
stubs (tower sheave tips / the station's bullwheel stubs). The station's bullwheel stubs sit several meters past the edge node, inside the station. Then LaneSystem
rebuilds the bezier as straight-line-plus-droop between the anchors. Near the station, that lane can diverge laterally and vertically
from the chair row's spawn line. Projecting chairs onto it gave visibly wrong positions and yaw there. Two guards exist. Both are inert
on normal spans:

- `MaxPinDistanceMargin`: a chair sits ~drop below its own cable. The pre-pin distance is ~ gripTop everywhere on normal spans, sag
  included, because the edge curve droops with the same formula. If the best lane is farther than drop + margin, the geometry is
  degenerate or the match is on the wrong side. The far lane is >= sqrt(3.6^2 + drop^2) ~ 4.3 m away, above the threshold by
  construction. In that case the chair stays at its vanilla spawn placement.
- `EndClampMeters`: keeps the pin parameter this many meters of arc away from the lane's extreme ends. So no chair ever sits on an
  anchor stub / bullwheel point.

Station bullwheel wrap-arc sublanes can never be candidates here. The system walks only the chair's owner edge `SubLane` buffer. The wrap
arcs live on the station building's own sublanes.

## Near StationEndClampMeters and ApproachPinDistanceCeiling

Chairs ride the rope on every span, like real carriers descending into a terminal. Position and yaw come from the lane curve everywhere.
Blending toward the authored row pose near stations lifted chairs off the visible rope. The rope is what the eye checks against.

- `ApproachPinDistanceCeiling`: on approach edges, large divergence from the row line is expected. So the plausibility ceiling is the
  wrong-side floor (~4.3 m), not drop + margin. The mid-span guard never fires there, so there is no zigzag of pinned and
  spawn-pose chairs.
- `StationEndClampMeters`: the anchored lane continues past the edge node onto the station's bullwheel stub inside the building. The
  station-side end clamp is raised, so the last chair holds just outside the station face. The system finds the station side of the lane
  curve geometrically (nearest curve end to the station node). It does not assume lane t-direction vs edge node order.
