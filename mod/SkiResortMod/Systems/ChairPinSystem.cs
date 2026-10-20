// ChairPinSystem — pins every lift-chair sub-object ON its edge's cable-lane curve.
//
// MILESTONE 1 (this file, shipping): static pinning. The vanilla SubObjectSystem places our
// chairs on the EDGE bezier (EdgeMiddle + EvenSpacing, decompile: SubObjectSystem.CreateSubObjects
// line ~1739) at a prefab-authored Y offset — height correctness has so far depended on keeping
// the prefab constants (axle 7.04, chair Y 4.64, drop 2.400) in sync by hand, and the chairs sit
// on the *edge* curve, not on the *cable lane* curve the player actually sees (the lane is the
// anchored, sagging one — LaneSystem moves its endpoints onto tower/station anchor stubs and
// droops it by UtilityLaneData.m_Hanging; docs/research/pillar-system.md Q4/Q6). This system
// re-seats each chair so its grip top lies exactly on the cable-lane bezier, eliminating the
// prefab-side height guessing permanently (DECISIONS.md #18).
//
// MILESTONE 2 (SHIPPED, entry 98 — see MovingChairs below): chairs MOVE. Design, every claim
// decompile-verified before a line was written:
//   * DISCOVERY CORRECTION: chairs do NOT carry Game.Objects.Secondary. SubObjectSystem's
//     creation path adds only Owner+PrefabRef+Transform on the prefab archetype
//     (SubObjectSystem.cs:2824-2827); the only Secondary writer is SecondaryObjectSystem
//     (:4097), a different pipeline. All three queries required Secondary, so milestone-1
//     static pinning NEVER matched a real chair (field logs: every pass 0 chairs; the
//     chairs on screen were pure vanilla EvenSpacing placement). Secondary is dropped from
//     the queries — the chair-prefab set filter was always the real selector. Supersedes
//     DECISIONS #19c's premise.
//   * CLOCK: phase_m = speed * (RenderingSystem.frameIndex + frameTime) / 60, in double
//     (frameIndex ~1e7+ in mature saves; float32 would quantize at metre scale). That clock
//     is what vanilla interpolation itself consumes: it freezes on pause (RenderingSystem.cs
//     :187-190 sets frameDelta=0), slews smoothly toward the sim clock at any speed
//     (:193-222, atan-clamped), and resnaps to the SERIALIZED SimulationSystem.frameIndex on
//     load — pause/speed/save-continuity handled by vanilla, free.
//   * LOOP: LiftLoopCache chains the lift's HV cable lanes end-to-end by EXACT PathNode
//     equality (FindAnchor rewrites endpoint+PathNode onto shared anchors, LaneSystem.cs
//     :4049-4072/:4117-4120), terminates at station-owned anchor PathNodes, and closes the
//     circuit with synthesized bullwheel semicircle arcs between the two chains' terminal
//     endpoints. Chairs get even loop offsets; every position is DERIVED
//     (s = (offset + phase) mod length) — no per-chair mutable state, nothing serialized.
//   * RENDERING: each riding chair is enrolled ONCE with vanilla InterpolatedTransform
//     (IEmptySerializable — zero save-format liability) + one Updated tag. Thereafter
//     ObjectInterpolateSystem.UpdateStaticAnimations copies our per-frame Transform into
//     InterpolatedTransform for NearCamera entities (ObjectInterpolateSystem.cs:296 — the
//     no-TransformFrame branch), PreCullingFlags.InterpolatedTransform is set by component
//     presence (PreCullingSystem.cs:1918-1920), and BatchDataSystem re-uploads the instance
//     TRS every rendered frame. Direct Transform writes at Modification4B — the same frame
//     slot where SubObjectSystem (2B) resets chairs on lift edits, so no vanilla-pose flash.
//     UpdateGroupSystem's Updated+InterpolatedTransform job is a no-op for bufferless chairs
//     (TryGetBuffer fails at :579; :613 just re-copies our Transform).
//   * CULLING: a bounds refresh (Updated on all riding chairs) every BoundsRefreshMeters of
//     travel keeps CullingInfo + the static search tree within ~2 m of the true position.
//     Tags added at 4B are stripped by CleanUpSystem the SAME frame, so they can never
//     retrigger our own change detection.
//   * DEGRADATION: lifts whose cable chains do not close into a loop (unfinished line,
//     missing station) fall back to milestone-1 static pinning per chair; MovingChairs=false
//     restores the static flow wholesale.
//
// ROUND L1 (2026-10-03, implementer I-2; docs/research/hub-u2-spec.md §2.1, §5.4) — DATA AND
// TIMING ONLY, the mover's algorithm is unchanged:
//   * Accessors for the hub and the lift census: MovingChairsOn, ChairSpeedMps, LoopGeneration
//     (++ on EVERY BuildLoopCache, independent of the drift-gated rebuild log), TryGetLoop
//     (unordered station pair → chairs riding + circuit length), IsChairPrefab, ChairliftPrefab.
//     (Catalog Phase A: IsChairPrefab and ChairliftPrefab are thin delegates to SkiPrefabCatalogSystem, contract
//     D6; the seat count is the lift type's SkiCatalog row — Phase A review finding 5.)
//   * RequestImmediateRebuild(why): dirty + bypass RebuildCooldownFrames ONCE. LiftCensusSystem
//     (Modification4B, anchored before this system) calls it when lift topology changed, so the
//     loop over a deleted span is dropped the same frame instead of chairs riding cached beziers
//     across the gap for up to 30 frames (spec §5.1), and once more the frame after a park
//     (the re-seated chairs are static-pinned onto their own lanes at once).
//   * Restart seeding, OD-7 (a), BuildLoopCache step 4: when a loop's identity carry-forward
//     fails AND none of its chairs rode a loop in the previous cache (the lift has just closed —
//     first time, or again after a section was redrawn), each chair starts from where it STANDS:
//     offset = ProjectOnOwner(loop, its owner, its grip point) − phase. Spacing may be uneven at
//     the turns. Any chair whose owner has no segment (cannot happen for a rostered chair, guarded
//     anyway) or whose grip point is farther than ApproachPinDistanceCeiling from its own lane (a
//     rider: after a game load the saved chairs stand anywhere on the loop) sends the whole loop
//     back to the entity-index grid, as do all other non-identity rebuilds (chairs that were
//     already riding keep today's behaviour, OD-7 (b)).
//
// ROUND F6 (2026-10-03, item B4; docs/research/field/2026-10-03b/findings/log-audit.md A3 and its
// Verification) — LOGGING ONLY, no behaviour change: the west lift of field test 6 fell back to the
// grid on 2 of its closures (log 562, 1671) and nothing said why. A refused seeding now writes ONE
// line per rebuild, `ChairPinSystem [moving] OD-7: N loop(s) could not restart from where their
// chairs stood …`, naming per loop (at most SeedRefusalLoopsLogged) its stations, the first failing
// chair, its section and the cause: farther than ApproachPinDistanceCeiling (4.0 m) from its own lane
// (with how many chairs were over and the farthest), no lane segment for its section, or no
// Transform / Owner. The first rebuild after a load is marked as expected (saved riders
// stand anywhere). Per-chair seeding (grid only the failures) needs gap filling: a later round.
//
// CATALOG PHASE A (2026-10-11; docs/research/art/assets-2026-10/catalog-a/phase-a-contract.md §3.2 L1, D8 lifts
// the freeze of this file) — PREFAB IDENTITY ONLY, the pinning and the mover are unchanged:
//   * The two copy-pasted resolvers (ResolveChairPrefabs, ResolveChairliftPrefab) and their scan queries are
//     gone (extensibility.md §1 rows 1 and 3, F5). The names live in the PURE SkiCatalog.cs and
//     SkiPrefabCatalogSystem resolves them: CarrierGripTops (carrier → bounds.max.y; the bounds read moved
//     there), LiftLinePrefabSet (an explicit whitelist of lift lines) and StationDefs (station → its row,
//     with the bullwheel centre that was StationWheelLocal here, row 4). Each view is fetched ONCE before a
//     chunk loop (contract §2.1, skeptic M4).
//   * LIFETIME (contract §8 N1): resolved per LOAD and revalidated at the first catalog call of each frame,
//     where this file resolved once per APP RUN. So the prefab lines below are written per load, and a prefab
//     replaced mid-session is followed (the catalog logs "PrefabCatalog [re-resolve]").
//   * Constants: chair speed = SkiCatalog.FixedGrip.SpeedMps (row 2; HubRules aliases the same const) and the
//     approach ceiling = FixedGrip.ApproachPinCeilingM (row 5). SeatsPerChair (4) is gone (row 2): the one
//     reader, SkiResortStatsSystem's capacity, reads FixedGrip.SeatsPerCarrier (3, the triple, ROUND-SPEC R2).
//   * Logs, per Game / Editor load (LogPrefabState): Info "chair prefab(s) … (grip-top …) and chairlift net
//     prefab(s) … from the prefab catalog"; WARN per chair prefab not found; WARN "no chair prefabs resolved —
//     pinning inactive this load"; WARN "net prefab(s) … not found — moving chairs inactive this load (static
//     pinning still runs)". The catalog's own "PrefabCatalog [arm]" line names every prefab FOUND or MISSING.
//
// CATALOG PHASE B (2026-10-11; docs/research/art/assets-2026-10/code-round-2/round-2-contract.md §5.2 and its
// orchestrator addendum 1, lift group) — PER LIFT TYPE; the pin, the loop algorithm and the re-spacing are unchanged:
//   1. LANES per registered type: a lane counts when its owner's prefab is in the catalog's LiftLineTypes view AND that
//      type's Mover is Loop (CableLane.m_TypeIndex = its row). Shuttle lines (the tram, Phase C) are not harvested.
//   2. CLOCK per loop: TimeS = (RenderingSystem.frameIndex + frameTime) / 60 in double (the milestone-2 clock, unchanged
//      source), phase(loop) = loop.m_SpeedMps × TimeS with m_SpeedMps = the loop type's SpeedMps — for the fixed-grip
//      row 2.5 × the same clock, i.e. the former CurrentPhase() bit for bit. The pause skip compares TimeS; OD-7 seeding
//      uses the loop's own phase. A loop whose lanes span several types (LiftLoopCache m_TypeIndex −1) gets no roster:
//      its chairs static-pin, one WARN per rebuild signature.
//   3. ROSTER: the CarrierGripTops view minus every carrier whose CarrierTypes row is a Shuttle — excluded from the
//      roster, CountMatchingChairs and the static pin (a tram cabin keeps its hosted pose, R29). A carrier whose type
//      differs from its loop's rides the loop anyway (it needs a mixed net; counted in the DUMP).
//   4. SPACING CHECK (D2): when a loop carries N > floor(loop / SpacingM) carriers, ONE WARN per lift type per load —
//      the line prefab's m_Spacing is below the catalog's; all of them ride, denser than designed. FEWER is the normal
//      case (EvenSpacing spawns n = (int)(L / s − 0.5) per edge, SubObjectSystem.cs:1743; the loop adds arcs and
//      terminal runs) and logs nothing.
//   5. F7 CEILING per type: the static pin's approach ceiling = the carrier's LiftTypeDef.ApproachPinCeilingM
//      (max(4.0, HangM + 1.5): fixed-grip 4.0 as before, detachable 4.8, gondola 6.0); OD-7 seeding and its refusal
//      text use the loop type's.
//   6. BOUNDS REFRESH every BoundsRefreshMeters of travel of the FASTEST moving loop: (TimeS − last) × max speed.
//   7. SHUTTLE HAND-OFF, once per load (LogPrefabState; Catalog Phase C, round-3-contract §4.12): "lift type 'tram' moves
//      as a Shuttle: TramShuttleSystem moves its N carrier(s); ChairPin neither harvests its lanes nor pins them"
//      (" (pending, not imported)" while its names are MISSING). The Shuttle carriers are TramShuttleSystem's (it moves,
//      holds or parks them in their hosted pose); IsPinnable, the lane harvest and the roster here are unchanged.
//   8. LogPrefabState per type; the per-carrier and per-line WARNs fire for EXPECTED names only (per-name flags:
//      CarrierDef.Expected, LiftTypeDef.LineExpected); pending names are listed in the Info line.
//   9. UP LANE (addendum 1): LiftLoopCache makes chain A the cable on the RIGHT looking uphill; this system writes
//      "ChairPinSystem [loop] up lane = right looking uphill: …" once per loop (station pair) per load.
// NOT BEHAVIOUR-IDENTICAL for a fixed-grip city: item 9 can reverse a lift's direction of travel versus "the first
// chain found" (each chair still faces its travel and rides the same loop); everything else is identical.
//
// PHASE D (2026-10-11; docs/research/art/assets-2026-10/code-round-4/round-4-contract.md §3.1-§3.2, §4.2-§4.3, §5, D4-D7,
// with round-4-contract-skeptic.md S-1 and S-8; movers group; report code-round-4/build-movers.md) — AN UNPOWERED LIFT'S
// CHAIRS STOP. Richard's re-ruling, DECISIONS 171: an unpowered lift carries no guests and its carriers stop; a lift's
// power is its BASE station's. Per loop (station pair), transient, no save data:
//   1. POLL (D6): every HoldPollFrames (64) of the render clock (RenderingSystem.frameIndex: nothing while paused, the
//      clock is frozen, RenderingSystem.cs:187-190) and once at each rebuild — the tram's C11 / D2 cadence. Per loop: the
//      census row of {m_BaseStation, m_TopStation} in either order (a loop's base is the LOWER terminal, geometry; the
//      row's Base is the Base-ROLE station, the one with the ElectricityConsumer), and for a Complete row
//      LiftCensusSystem.ReadLiftPower(row's Base) — THE reader, guarded against flicker there (512 sim frames before a
//      NoPower lift counts as powered again). Never the top station. Gate = LiftLoopHold.GateOf(rowKnown, complete, power,
//      closed: false) (D7: "closed" stays the pre-beta item; it is one argument here). No row, a row not Complete, or the
//      census not armed → Run, as before (D5, fail open; S-1: one Info line per lift per load for a loop with chairs and no
//      Complete row while the census is armed; one WARN per load when the census is not armed 120 frames after the load).
//   2. HOLD (Run → Hold): FrozenTimeS = this frame's clock; that frame writes the frozen pose — (offset_i +
//      LiftLoopHold.FrozenPhase(speed, FrozenTimeS)) % L, PhaseOf's product at the frozen clock bit for bit, i.e. the very
//      pose a running pass writes this frame — and tags every chair Updated once (CullingInfo and the search tree re-read
//      Transform only on Updated, PreCullingSystem.cs:709-718), then nothing: no Transform write, skipped by the bounds
//      refresh. A frozen chair cannot jitter: chairs have no TransformFrame buffer, so ObjectInterpolateSystem takes the
//      static branch (:252-263) and copies Transform as is (:296); the culling flag follows component presence
//      (PreCullingSystem.cs:1918-1920). The chairs stay ROSTERED (D4: TryGetLoop keeps "riding" > 0, so the hub reads "No
//      power", not "Chairs stopped") and enrolled (no structural change).
//   3. RESUME (Hold → Run): every offset := LiftLoopHold.Rebase(offset, speed, FrozenTimeS, now, L), so (offset' + speed ×
//      now) % L is the frozen place to ~1e-9 m (LiftBatch LH-1), far inside MoveChair's 1 mm idempotence test: the resume
//      frame writes nothing new and the next frame advances by speed × Δt. Every offset shifts by the same amount: the
//      spacing is kept. No jump, no speed ramp (R-11).
//   4. REBUILDS: an identity rebuild carries the offsets by entity and the hold by pair, so a held loop stays put (its
//      chairs are re-written at the frozen pose, idempotent). A held loop whose offsets this rebuild did NOT carry (OD-7
//      seed or grid; BuildLoopCache records the pair, a record-only edit) is re-frozen at this frame's clock (= the
//      rebuild's seedTimeS): seeded → exactly where its chairs stood; gridded → once to its grid places, as a running loop
//      would. A pair with no loop any more is dropped (the census lines and the static pin explain the lift). At a LOAD the
//      first rebuild grids the saved riders (OD-7 refuses them) and its poll holds an unpowered loop there: a held lift's
//      chairs may shift once at a load (R-5, accepted by Richard).
//   5. LOGS (§4.2): "[hold] power gate armed" once per load; "[state] … Running → Held (no power) — chairs stop at phase
//      …", "Held → Running (power back) — chairs resume from phase … after N s held", "Held, re-frozen after a rebuild …"
//      through a 10 s LogWindow (fold line; flushed on load and quit); the rebuild signature gains ", h held (why)"
//      only when h > 0; "[moving] pass alive" also fires on a held write (S-8), with ", c held (why)" only when c > 0.
//      T116 (2026-10-20): "why" = "no power", "closed" or "p no power, c closed" (LiftLoopHold.HeldSuffix; before, every
//      hold printed "no power"). The once-per-load "did not close into a station-to-station loop" WARN skips Shuttle
//      rows (LiftLoopHold.ExpectsLoop): the tram has no ChairPin loop by design.
//      A powered city's lines are unchanged except the one "[hold] power gate armed" line per load.
//   POWERED LIFTS (§6, skeptic MUST-4): with no hold, the move pass, PhaseOf, TimeS, MoveChair, the bounds refresh and
//   BuildLoopCache's slot assignment run exactly as before; the new work is read-only (the poll). The move loop gains a
//   held branch taken only while a loop is held. Identity holds per consumer-state path: the 64-frame power read can see a
//   one-dispatch shortfall window a 256-frame poller would miss (LiftServedRule header).
//
// ─────────────────────────────────────────────────────────────────────────────────────────
// HOW A CHAIR IS PINNED (all game types verified against the decompiled Game.dll, fullsrc)
//
//   1. Chairs are found generically: Owner + PrefabRef + Transform, excluding Deleted/Temp,
//      with PrefabRef matched against the chair prefab set resolved per load by
//      SkiPrefabCatalogSystem (SkiCatalog Lifts[*].CarrierPrefabs; Catalog Phase A).
//      NOT Secondary — see the milestone-2 discovery correction above.
//   2. Owner is the lift EDGE entity; its Game.Net.SubLane buffer holds the lanes. Cable lanes
//      are recognised structurally, not by name: lane PrefabRef → UtilityLaneData with the
//      HighVoltageLine flag (0x40). This skips the LV comm lane (LowVoltageLine, x 0) and works
//      unchanged if the HV lane is ever swapped for a custom haul-rope clone that keeps the
//      utility class (lane-chairs.md "custom haul-rope re-skin").
//   3. SIDE matching: the two cable lanes run at x ±1.8, the chair hangs at x ±1.8. On straight
//      spans the chair's own-side lane is the one directly above it (~2.4 m) and the far lane is
//      ≥ 3.6 m away, so nearest-curve-distance suffices — but hanging lanes are straight XZ
//      CHORDS, so on a curved span (sagitta > 1.8 m) the far chord can become 3D-nearest.
//      v1.4 therefore matches by lateral SIGN against the owner edge's curve first (SideSign),
//      with nearest-distance only ranking same-side candidates.
//   4. Grip-top-on-curve: the chair prefab entity's ObjectGeometryData.m_Bounds.max.y is the
//      pivot→top distance of the imported mesh. The Blender contract (PIPELINE §1) makes the
//      grip the topmost geometry with the pivot at the lowest point, so bounds.max.y is exactly
//      the pivot→grip-top distance — read at runtime per chair prefab, never hardcoded, so
//      future chair generations with different drops pin correctly with zero code changes.
//        pinnedPosition = lanePoint − (0, bounds.max.y, 0)
//   5. The chair hangs plumb: rotation is yaw-only from the lane tangent via
//      NetUtils.GetNodeRotation (it zeroes tangent.y — same call vanilla uses for sub-objects).
//      The two rows face opposite travel directions (authored via the prefab's local rotations),
//      so the tangent sign is chosen to preserve the chair's current facing.
//
// SCHEDULING (load-bearing):
//   SubObjectSystem spawns/moves chairs at Modification2B (its barrier plays at end of 2B) and
//   tags them Updated; LaneSystem rebuilds lane curves at Modification4 (ModificationBarrier4).
//   We run at Modification4B: chairs exist, cable curves are final, and our Transform write
//   lands BEFORE Game.Objects.SearchSystem (Modification5) and PreCulling read it — the search
//   tree and the renderer both see the pinned position with no extra event round-trip. The
//   Created/Updated tags live until CleanUpSystem (Cleanup phase), so the Updated-filtered
//   query reliably sees everything that changed this frame.
//
// REACTIVITY: event-driven, never per-frame (milestone 1). Two triggers:
//   * chairs carrying Updated (creation and every SubObjectSystem reposition — the vanilla
//     code path adds Updated in both cases, SubObjectSystem lines ~2824/3050);
//   * one full pass on the first update after game/editor load (saved chairs carry no Updated
//     tag; chairs we move in that pass get Updated added so culling/search refresh).
//
// DEFENSIVE CONTRACT: never throw into the game loop (top-level try/catch, error logged once);
// a chair whose edge has no matching cable sublane (stations mid-build, weird states) is left
// untouched; a missing chair prefab is logged per load and idles the system for the load
// (Catalog Phase A: was "once … for the session").
// ─────────────────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using Colossal.Entities;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Rendering;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace SkiResortMod.Systems
{
    public sealed partial class ChairPinSystem : GameSystemBase
    {
        // ── Chair prefab identity ────────────────────────────────────────────────────────
        // Catalog Phase A (ext row 1; was ChairPinSystem.cs:157 ChairPrefabNames = { "SkiLiftChair4" }):
        // the carrier names live in SkiCatalog (Lifts[*].CarrierPrefabs — add a new chair
        // generation there, PIPELINE §0: every generation gets a new name) and
        // SkiPrefabCatalogSystem resolves them per load into CarrierGripTops. A missing name is
        // tolerated: the catalog's ARM line lists it MISSING and LogPrefabState WARNs per load.

        // ── Milestone 2: moving chairs (kill-switch) ─────────────────────────────────────
        // true = chairs ride the cable loop (see the milestone-2 header block). false = the
        // milestone-1 static flow, untouched, wholesale. Promote to a SkiResortSettings
        // toggle for shipping. Not readonly since entry 101: the Ski Resort developer-menu
        // tab flips it at runtime for A/B checks (a flip requests a full pass).
        private static bool MovingChairs = true;
        // Catalog Phase A (ext row 2; was ChairPinSystem.cs:165 = 2.5f, the first of two copies — HubRules.cs:208
        // was the second and aliases the same const now; the stats arm's compare of the two is gone).
        private const float ChairSpeedMetersPerSecond = SkiCatalog.FixedGrip.SpeedMps;

        // ── Round L1 accessors (hub-u2-spec.md §2.1) ─────────────────────────────────────
        /// <summary>The moving-chairs kill switch (MovingChairs above; the developer tab flips it).</summary>
        internal static bool MovingChairsOn => MovingChairs;
        /// <summary>Chair speed along the loop, metres per second of the render clock.</summary>
        internal const float ChairSpeedMps = ChairSpeedMetersPerSecond;
        // Catalog Phase A (ext row 2; was ChairPinSystem.cs:172-174 SeatsPerChair = 4, "by the prefab name"):
        // removed. Its only reader, SkiResortStatsSystem's lift capacity, reads SkiCatalog.FixedGrip.SeatsPerCarrier.
        /// <summary>++ on EVERY BuildLoopCache (not tied to the count signature, which contains no
        /// station identities).</summary>
        internal int LoopGeneration { get; private set; }

        /// <summary>A carrier (chair) prefab of the catalog. Catalog Phase A (ext row 1; was the m_ChairPrefabs set
        /// resolved once by name, ChairPinSystem.cs:180): a thin delegate kept for LiftCensusSystem (contract D6).</summary>
        internal bool IsChairPrefab(Entity prefab) => m_PrefabCatalog.IsCarrier(prefab);

        /// <summary>ADDITIVE: the fixed-grip line (SkiChairliftHV) prefab entity; Entity.Null when MISSING or before
        /// the first game / editor load. Catalog Phase A (ext row 3; was m_ChairliftPrefab, ChairPinSystem.cs:183).</summary>
        internal Entity ChairliftPrefab => m_PrefabCatalog.LineEntity(SkiCatalog.Lifts[0]);

        /// <summary>The closed loop between two stations, in either order: chairs riding and the
        /// CIRCUIT length (chain A + top turn + chain B + base turn). False in static mode, for an
        /// open lift, or before the first rebuild.</summary>
        internal bool TryGetLoop(Entity stationA, Entity stationB, out int chairsRiding, out double loopM)
        {
            for (int l = 0; l < m_LoopCache.m_Loops.Count; l++)
            {
                LiftLoopCache.Loop loop = m_LoopCache.m_Loops[l];
                if ((loop.m_BaseStation == stationA && loop.m_TopStation == stationB) ||
                    (loop.m_BaseStation == stationB && loop.m_TopStation == stationA))
                {
                    chairsRiding = loop.m_Chairs.Count;
                    loopM = loop.m_Length;
                    return true;
                }
            }
            chairsRiding = 0;
            loopM = 0.0;
            return false;
        }

        /// <summary>Dirty the loop cache and bypass RebuildCooldownFrames once (LiftCensusSystem:
        /// lift topology changed / a park was just re-seated). Same frame when called before
        /// Modification4B's ChairPinSystem update (the census is anchored before it). Static mode
        /// keeps no loop cache: the flag is harmless there.</summary>
        internal void RequestImmediateRebuild(string why)
        {
            m_LoopCacheDirty = true;
            m_ImmediateRebuild = true;
            m_ImmediateWhy = why;
            m_DiagImmediateRequests++;
        }

        private bool m_ImmediateRebuild;           // bypass the cooldown once (RequestImmediateRebuild)
        private string m_ImmediateWhy = "-";       // reason of the last request (dev tab)
        private int m_DiagImmediateRequests;       // requests this load
        private int m_DiagImmediateRebuilds;       // rebuilds that bypassed the cooldown this load
        private int m_DiagSeededLoops;             // loops seeded from where the chairs stood (OD-7 a)
        private int m_DiagSeededChairs;
        private int m_DiagGridLoops;               // non-identity loops given the entity-index grid

        // Moving-mode tuning. Bounds refresh: chairs' CullingInfo/search-tree entries are
        // refreshed (one Updated tag per riding chair) every this-many metres of loop travel;
        // 2 m keeps stale bounds inside a chair's own extents at any sim speed (distance per
        // SIM frame is speed-independent). Rebuild cooldown: cache rebuild requests (lift
        // edits, reconnect pokes — DECISIONS 40's poke storms) are debounced to at most one
        // rebuild per window.
        private const double BoundsRefreshMeters = 2.0;
        private const int RebuildCooldownFrames = 30;
        private const int EmptyRosterRetryLimit = 10;

        // ── v1.1 final-span guards ───────────────────────────────────────────────────────
        // On the FINAL edge (last tower → station) the cable lane is NOT the edge curve:
        // LaneSystem moves the lane's endpoints onto the anchor stubs (tower sheave tips /
        // the station's bullwheel stubs, which sit several metres past the edge node inside
        // the station) and then rebuilds the bezier as straight-line-plus-droop between the
        // ANCHORS. Near the station that lane can diverge laterally/vertically from the
        // chair row's spawn line, so projecting chairs onto it produced visibly wrong
        // positions/yaw there (user report: "angles on the last node section").
        // Two guards, both inert on normal spans:
        //   * MaxPinDistanceMargin — a chair sits ~gripTop below its own cable (pre-pin
        //     distance ≈ gripTop everywhere on normal spans, sag included, because the edge
        //     curve droops with the same formula). If the best lane is farther than
        //     gripTop + margin the geometry is degenerate (or a wrong-side match: the far
        //     lane is ≥ √(3.6² + gripTop²) ≈ 4.3 m away, above the threshold by
        //     construction) — leave the chair at its tidy vanilla spawn placement.
        //   * EndClampMeters — keep the pin parameter this many metres of arc away from the
        //     lane's extreme ends so no chair ever sits ON an anchor stub / bullwheel point.
        // Note: station bullwheel wrap-arc sublanes can NEVER be candidates here — we only
        // walk the chair's owner EDGE SubLane buffer, and the wrap arcs live on the station
        // building's own sublanes.
        private const float MaxPinDistanceMargin = 1.5f;
        private const float EndClampMeters = 1.0f;

        // ── v1.3 station approach: trust the cable (replaces the v1.2 row blend) ─────────
        // History: v1.1 pinned approach chairs to the anchored lane and users read the span
        // as skewed — but the ugly part was the DISTANCE GUARD firing mid-span (divergence
        // from the row line exceeds gripTop+margin on approach spans by design), leaving a
        // mix of pinned and spawn-pose chairs: a zigzag. v1.2 "fixed" it by blending toward
        // the authored row pose near stations — which deliberately lifts chairs OFF the
        // visible rope, and that is exactly what the field reports showed ("still off at
        // bottom and top near the stations"). The rope is what the eye checks against.
        //
        // v1.3: chairs ride the rope, full stop — position AND yaw from the lane curve on
        // every span, like real carriers descending into a terminal. Approach differences:
        //   * ApproachPinDistanceCeiling — on approach edges large divergence from the row
        //     line is EXPECTED, so the plausibility ceiling is the wrong-side floor
        //     (~4.3 m), not gripTop+margin. No mid-span guard firing → no zigzag.
        //   * StationEndClampMeters — the anchored lane continues past the edge node onto
        //     the station's bullwheel stub INSIDE the building; the station-side end clamp
        //     is raised so the last chair holds just outside the station face. The station
        //     side of the LANE curve is found geometrically (nearest curve end to the
        //     station node) — lane t-direction vs edge node order is not assumed.
        private const float StationEndClampMeters = 4.0f;
        // Catalog Phase A (ext row 5; was ChairPinSystem.cs:279 = 4.0f): the lift type's absolute ceiling (F7
        // derives it per carrier in Phase B). Uses unchanged: TryPinChair, TrySeedFromPositions, NoteSeedRefusal.
        private const float ApproachPinDistanceCeiling = SkiCatalog.FixedGrip.ApproachPinCeilingM;

        // Reconnect full passes can fire into the frame gap where vanilla has deleted the
        // old chairs but not yet respawned replacements (the Aug 11 field log: 26 requests,
        // zero chairs matched, silent pass). Keep the pass armed until chairs appear or the
        // retry window closes.
        private const int FullPassRetryFrames = 30;

        // Catalog Phase A (ext rows 1, 3, 4; F5): the prefab catalog replaces m_PrefabSystem's two name resolvers,
        // the m_ChairPrefabs map (was :290; now m_PrefabCatalog.CarrierGripTops, carrier → bounds.max.y) and the two
        // scan queries (were :295-296, created at :435-443).
        private SkiPrefabCatalogSystem m_PrefabCatalog;

        private EntityQuery m_UpdatedChairQuery; // chairs changed this frame (creation + reposition)
        private EntityQuery m_AllChairQuery;     // every live chair (load pass, watchdog, moving mode)
        private EntityQuery m_TempChairQuery;    // tool-preview chairs (pinned silently, v1.2.1)
        private EntityQuery m_LaneQuery;         // moving mode: every live net lane (cache harvest)
        private EntityQuery m_StationQuery;      // moving mode: every live building (station scan)

        // ── Moving-mode state (all transient — nothing here is ever serialized) ──────────
        private Game.Rendering.RenderingSystem m_RenderingSystem;
        private readonly LiftLoopCache m_LoopCache = new LiftLoopCache();
        // Catalog Phase A (ext row 3): m_ChairliftPrefab / m_ChairliftPrefabResolved (were :303-304) → the catalog's
        // LiftLinePrefabSet (the lane-owner whitelist) and LineEntity(SkiCatalog.Lifts[0]).
        private bool m_LoopCacheDirty = true;
        // M9 / M10 (code review 2026-10-19): once-per-load cost lines (silence impossible, spam impossible).
        private bool m_LaneScanLogged;
        private bool m_BoundsCostLogged;
        private long m_BoundsCostTicks, m_MoverCostTicks;
        private int m_BoundsCostPasses, m_MoverCostPasses;
        private const int MoverCostPassesToLog = 600;   // ~10 s of moving frames at 60 fps
        private int m_RebuildCooldown;
        private int m_EmptyRosterRetries;
        // Catalog Phase B (§5.2 items 2, 6; were m_LastMovePhase / m_LastBoundsPhase in metres of the one fixed-grip
        // phase): the render clock in SECONDS — each loop's phase is its speed × this clock.
        private double m_LastMoveTimeS = -1.0;     // seconds; -1 forces the first mover pass
        private double m_LastBoundsTimeS;          // seconds at the last bounds-refresh burst
        // Catalog Phase B, per load: lift types whose spacing WARN fired (D2, once per type), station pairs whose up
        // lane was logged (addendum 1, once per loop), the last mixed-loop WARN signature (item 2), the carriers riding
        // a loop of another type (DUMP), the fastest moving loop's speed (item 6).
        private readonly HashSet<int> m_SpacingWarned = new HashSet<int>();
        private readonly HashSet<long> m_UpLaneLogged = new HashSet<long>();
        private string m_MixedLoopSignature;
        private int m_DiagTypeMismatch;
        private int m_DiagMixedLoops;
        private int m_DiagShuttleCarriers;
        private double m_MaxLoopSpeedMps;
        private bool m_MovingAliveLogged;          // one-time "[moving] pass alive" per load
        private bool m_LoopWarnLogged;             // one-time unclosed-chain warning per load
        private int m_StaticFallbackChairs;        // chairs static-pinned by the last rebuild
        private bool m_LastBuildHadLanes;          // last rebuild found chairlift cable lanes
        private string m_LastBuildSignature;       // count fingerprint — gates the rebuild log

        // ── L0 observability (entry 101): plain counters the Ski Resort developer-menu tab
        // reads at 10 Hz (ChairPinSystem.Debug.cs). Written only where the work already
        // happens; never consulted by the mover itself. All transient, reset per load.
        private int m_DiagUpdateCalls;             // OnUpdate entries this load
        private long m_DiagLastUpdateFrame = -1;   // RenderingSystem.frameIndex at the last entry
        private string m_DiagLastExit = "never ran"; // which branch the last OnUpdate took; null = the moving pass ran (L9: text built on read)
        /// <summary>L9 (code review 2026-10-19): the last-exit text for the dev tab / dump. The per-frame moving pass stores
        /// null and leaves the numbers in the m_DiagLast* counters; the string is formatted only here, when read.</summary>
        private string LastExitText()
        {
            if (m_DiagLastExit != null)
                return m_DiagLastExit;
            return m_DiagLastRiding == 0
                ? "moving: empty roster (no closed loop has chairs)"
                : $"moving: {m_DiagLastMoved} moved / {m_DiagLastInPlace} in place / {m_DiagLastMissing} missing of {m_DiagLastRiding}{m_HeldExitSuffix}";
        }
        private string m_LastError;                // message + stack of the tripping exception
        private int m_DiagProbeUpdatedChairs;      // chairs matched by the change probe, last pass
        private double m_DiagLastTimeS;            // derived clock, last mover pass (seconds; Catalog Phase B, was metres)
        private int m_DiagLastMoved, m_DiagLastInPlace, m_DiagLastMissing, m_DiagLastRiding;
        private int m_DiagPassesWithMoves;         // mover passes that wrote ≥1 Transform
        private int m_DiagPausedSkips;             // mover passes skipped (phase unchanged)
        private int m_DiagRebuilds;                // loop-cache rebuilds this load
        private long m_DiagLastRebuildFrame = -1;
        private int m_DiagLaneTotal;               // m_LaneQuery entity count at the last rebuild
        private int m_DiagLanesMatched;            // lanes owned by the chairlift net, HV
        private int m_DiagStations;                // station buildings found
        private int m_DiagChairsMatched;           // chair-prefab entities found by the rebuild
        private int m_DiagEnrolled;                // InterpolatedTransform enrollments, last rebuild
        private readonly List<string> m_DiagStationLines = new List<string>(); // per-station census, last rebuild

        // ── Bullwheel centres per station prefab (entry 103), station-local, rope height ──
        // Catalog Phase A (ext row 4; was ChairPinSystem.cs:337-350 StationWheelLocal, keyed by prefab name):
        // the centres are SkiCatalog.Stations[*].WheelLocal now — base (0, 3.55, 0.2), top (0, 3.75, 0) —
        // read through the catalog's StationDefs view in BuildLoopCache. Provenance (unchanged):
        // assets/blender/lift_terminal.py — build_terminal_pali (drive): wheel_x, wheel_z = 0.2, 3.5
        // "bullwheel centre - loading height (INVARIANT)"; build_return_simple (return): wheel_z = 3.7
        // at the origin; rope over the wheel is wheel_z + 0.05; wheel radius 1.8 = half the 3.6 m rope
        // gauge. Authoring frame (+X travel, Y lateral, Z up) → prefab-local (X lateral, Y up, Z travel)
        // (cs2_asset.py rotate-then-bake), so authoring (x, y, z) lands at (-y, z, x) in the game frame since T14 (the game negates
        // FBX x on import, rule T; it was (y, z, x) before — the wheel centres here have y = 0, so they do not move). A new terminal
        // generation adds a StationDef; a non-finite WheelLocal falls back to the plain terminal
        // semicircle (LiftLoopCache.AppendTerminalTurn).

        // v1.2.1 observability contract: SILENCE MUST BE IMPOSSIBLE. Every way this system
        // can fail or idle now has a distinct, unmissable log signature:
        //   * first update pass that matches chairs → one-time "[update] pass alive" line;
        //   * update event with zero prefab matches → one-time diagnostic line;
        //   * exception → "!!!" error WITH stack, once per load, and the system disables for
        //     the CURRENT LOAD ONLY (prefab table kept; OnGameLoadingComplete re-arms) —
        //     previously the catch cleared the prefab table, which also made every later
        //     load in the same app run silently dead;
        //   * watchdog: every WatchdogIntervalFrames frames, a full idempotent pass; if it
        //     pins anything the event path missed, that is a WARNING with counts.
        // (Catalog Phase A: m_PrefabsResolved, "resolution attempted once per app run", was :362 — the catalog
        // resolves per load, contract §8 N1.)
        private bool m_FullPassPending;    // set on load or by RequestFullPass, consumed next update
        private int m_FullPassRetries;     // v1.3: frames a non-load pass stays armed while zero chairs match
        private string m_PendingPassLabel = "load pass"; // log label of the pending full pass
        private bool m_Disabled;           // tripped by the catch; re-armed per load
        private bool m_ErrorLogged;        // one error with stack per load
        private bool m_UpdateAliveLogged;  // one-time "[update] pass alive"
        private bool m_ZeroMatchLogged;    // one-time zero-prefab-match diagnostic
        private int m_WatchdogCountdown = WatchdogIntervalFrames;
        private const int WatchdogIntervalFrames = 4096; // ~68 s of frames at 60 fps

        // ── Phase D (round-4-contract §3.2, D4-D7; header PHASE D): the power hold per station pair ──────────────────
        // All transient (cleared per load, never saved: §5). The pair key is the two stations ordered by (Index, Version),
        // the tram's idiom (TramShuttleSystem m_RunsByPair); a loop's own order is geometry and kept for the logs.
        /// <summary>D6: render frames between two hold polls (= TramShuttleSystem.PowerPollFrames, C11).</summary>
        internal const int HoldPollFrames = 64;
        /// <summary>The census window before the "not armed" WARN (= TramShuttleSystem.CensusGateFrames, C12).</summary>
        private const int HoldCensusGateFrames = 120;

        private sealed class LoopHold
        {
            public Entity Base, Top;                       // the loop's stations, geometry order (logs, dump)
            public bool Held;
            public bool ForPower;                          // held for no power (else closed, D7): the resume line's reason
            public double FrozenTimeS;                     // the render clock the held chairs stand at (TimeS, seconds)
            public double HeldSinceS;                      // the clock of the hold's entry (the resume line's "after N s held")
            public LiftPower Power = LiftPower.Unknown;    // the last power read for this pair (Unknown = not read)
            public bool PoseDirty;                         // write the frozen pose on the next move pass
            public bool TagBounds;                         // one Updated per chair after that write (bounds refresh)
            public bool RowKnown;                          // a census row of this pair at the last poll
            public LiftCensus.State RowState;
            public Entity RowBase;                         // that row's Base (the Base-ROLE station: the one read)
            public bool Seen;                              // in the cache at the last poll
        }

        private struct HoldRow
        {
            public LiftCensus.State State;
            public Entity Base;
        }

        private readonly Dictionary<(Entity, Entity), LoopHold> m_Holds = new Dictionary<(Entity, Entity), LoopHold>();
        private readonly List<LoopHold> m_HoldByLoop = new List<LoopHold>();   // aligned with m_LoopCache.m_Loops (set per poll)
        private readonly Dictionary<(Entity, Entity), HoldRow> m_HoldRows = new Dictionary<(Entity, Entity), HoldRow>();
        private readonly List<LiftCensus.Row> m_HoldRowScratch = new List<LiftCensus.Row>();
        private readonly List<(Entity, Entity)> m_HoldDropScratch = new List<(Entity, Entity)>();
        // BuildLoopCache's record (§3.2 step 3; record only): pairs whose offsets the last rebuild did NOT carry → true =
        // seeded from where the chairs stood (OD-7 a), false = the entity-index grid.
        private readonly Dictionary<(Entity, Entity), bool> m_RebuildUncarried = new Dictionary<(Entity, Entity), bool>();
        private readonly HashSet<(Entity, Entity)> m_HoldFailOpenLogged = new HashSet<(Entity, Entity)>();   // S-1, per load
        private readonly LogWindow m_HoldLog = new LogWindow(10f);   // §4.2: the [state] lines (slot 0 to held, 1 to running)
        private LiftCensusSystem m_HoldCensus;                       // resolved lazily (GetExistingSystemManaged, never created)
        private int m_HoldRowsRevision = int.MinValue;
        private uint m_HoldPollFrame = uint.MaxValue;
        private int m_HoldCensusWait;
        private bool m_HoldInGame, m_HoldInEditor;                   // set per Game / Editor load, cleared at every preload
        private bool m_HoldArmLogged, m_HoldCensusWarned, m_HoldEditorLogged;
        private int m_HeldLoopCount, m_HeldChairCount;               // as of the last poll
        private int m_HeldChairsForPower;                            // T116: of m_HeldChairCount, held for no power (rest: closed)
        private string m_HeldExitSuffix = string.Empty;              // "; n held (no power)" for m_DiagLastExit; "" at 0 held
        private int m_DiagHoldPolls, m_DiagHoldPowerReads, m_DiagHoldChanges, m_DiagHeldWrites, m_DiagHoldDropped;
        private double m_DiagHoldPollMs;
        private string m_HoldLastChange = "-";

        protected override void OnCreate()
        {
            base.OnCreate();
            // Catalog Phase A: the prefab catalog (contract §3.0; disabled system, load hooks only — creating it here
            // is safe whatever Mod.cs's order, UpdateSystem.cs:158-160 uses GetOrCreateSystemManaged too).
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();

            // v1.2.1: Any{Created, Updated} (the vanilla EBGS idiom) instead of All{Updated}.
            // Chairs spawned from the prefab archetype carry BOTH tags, so All{Updated} did
            // match creations — but Any{} also catches any hypothetical Created-without-
            // Updated path and costs nothing.
            // Entry 98: NO Secondary term — chairs never carry it (see the milestone-2
            // header block); with it, every one of these queries was provably empty of
            // chairs for the mod's entire field history. Game.Objects.Static replaces it
            // as the chunk-level narrowing (chairs keep Static — SubObjectSystem strips
            // it only from Relative sub-objects, decompile :2898 — and without it these
            // queries would also match every vehicle and cim on the map). The
            // chair-prefab filter inside the pass loop is the real selector.
            m_UpdatedChairQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Objects.Static>(),
                    ComponentType.ReadOnly<Owner>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadWrite<Transform>(),
                },
                Any = new[]
                {
                    ComponentType.ReadOnly<Created>(),
                    ComponentType.ReadOnly<Updated>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });

            m_AllChairQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Objects.Static>(),
                ComponentType.ReadOnly<Owner>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadWrite<Transform>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            // v1.2.1: tool-preview chairs. Pinned every frame they appear (they are rebuilt
            // by the tool with fresh Updated tags), silently — so the drag preview shows the
            // same pinned/blended chairs the applied lift will have. Previously previews
            // showed raw spawn placement, which reads as "the mod is not working" (and is the
            // likely source of at least one misalignment report from a preview screenshot).
            // Safe: Temp sub-objects are regenerated from scratch every tool tick and their
            // original-matching (SubObjectSystem's FindOriginalSubObject) runs at 2B before
            // we touch them; applied lifts spawn REAL chairs that the normal pass pins.
            m_TempChairQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Objects.Static>(),
                ComponentType.ReadOnly<Owner>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadWrite<Transform>(),
                ComponentType.ReadOnly<Updated>(),
                ComponentType.ReadOnly<Temp>(),
                ComponentType.Exclude<Deleted>());

            // (Catalog Phase A, F5: the two prefab-by-name scan queries that were created here are gone; the
            // catalog's one typed resolver scans once per resolution for every name, SkiPrefabCatalogSystem.cs.)

            // Moving mode: cable-lane harvest (Owner filter to the chairlift net happens in
            // the build pass — lanes hang off both edges AND nodes of the drawn line) and
            // station scan (prefab filter in the pass: the prefab catalog's StationDefs, the
            // same station set PowerBridge serves).
            // UtilityLane (review fix, entry 98): without it this query matches EVERY road/
            // path/parking lane on the map — utility narrows it to power/pipe/comm lanes.
            m_LaneQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Net.Lane>(),
                ComponentType.ReadOnly<Game.Net.UtilityLane>(),
                ComponentType.ReadOnly<Curve>(),
                ComponentType.ReadOnly<Owner>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            m_StationQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Buildings.Building>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Transform>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            m_RenderingSystem = World.GetOrCreateSystemManaged<Game.Rendering.RenderingSystem>();

            Mod.Log.Info($"{nameof(ChairPinSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (mode != GameMode.Game && mode != GameMode.Editor)
                return;

            // Catalog Phase A (ext rows 1, 3; F5): ResolveChairPrefabs() / ResolveChairliftPrefab() were called here,
            // once per APP RUN. The catalog resolves per load; this system's per-load prefab lines are written by
            // LogPrefabState() at the end of this method (after the re-arm, so a throw there cannot skip it).
            // Saved chairs carry no Updated tag, and prefab-side constants may have changed
            // since the save was written — re-pin everything once per load.
            m_FullPassPending = true;
            m_PendingPassLabel = "load pass";

            // v1.2.1: re-arm per load. A tripped error no longer kills the whole app run,
            // and the one-time diagnostics fire freshly for each load.
            m_Disabled = false;
            m_ErrorLogged = false;
            m_UpdateAliveLogged = false;
            m_ZeroMatchLogged = false;
            m_WatchdogCountdown = WatchdogIntervalFrames;

            // Moving mode: the loop cache is world-state — rebuild per load. The phase
            // itself needs no reseeding (RenderingSystem resnaps its clock to the
            // serialized SimulationSystem.frameIndex).
            // D7 (L10, code review 2026-10-19): the loops of the previous session carry that session's chair entities
            // and offsets; clear them so no reader sees them between this load and the first rebuild. LoopGeneration moves
            // so generation-keyed readers (hub stats) drop what they derived from them. The HV-lane prefab cache is
            // hygiene (M9).
            m_LoopCache.Clear();
            LoopGeneration++;
            CableLanePrefabs.Clear();
            m_LoopCacheDirty = true;
            m_RebuildCooldown = 0;
            m_EmptyRosterRetries = 0;
            m_LastMoveTimeS = -1.0;
            m_LastBoundsTimeS = 0.0;
            m_SpacingWarned.Clear();       // Catalog Phase B: per-load WARN / log latches
            m_UpLaneLogged.Clear();
            m_MixedLoopSignature = null;
            m_DiagTypeMismatch = m_DiagMixedLoops = m_DiagShuttleCarriers = 0;
            m_MaxLoopSpeedMps = 0.0;
            m_MovingAliveLogged = false;
            m_LoopWarnLogged = false;
            m_LaneScanLogged = false;
            m_BoundsCostLogged = false;
            m_BoundsCostTicks = 0;
            m_BoundsCostPasses = 0;
            m_MoverCostTicks = 0;
            m_MoverCostPasses = 0;
            m_LastBuildHadLanes = true;  // assume lanes until the first rebuild proves otherwise
            m_LastBuildSignature = null; // first rebuild per load always logs

            // Entry 101: developer-tab counters are per load.
            m_DiagUpdateCalls = 0;
            m_DiagLastUpdateFrame = -1;
            m_DiagLastExit = "load complete, no update yet";
            m_LastError = null;
            m_DiagProbeUpdatedChairs = 0;
            m_DiagLastTimeS = 0.0;
            m_DiagLastMoved = m_DiagLastInPlace = m_DiagLastMissing = m_DiagLastRiding = 0;
            m_DiagPassesWithMoves = 0;
            m_DiagPausedSkips = 0;
            m_DiagRebuilds = 0;
            m_DiagLastRebuildFrame = -1;
            m_DiagLaneTotal = m_DiagLanesMatched = m_DiagStations = m_DiagChairsMatched = m_DiagEnrolled = 0;

            // Round L1: the immediate-rebuild latch and its counters are per load.
            m_ImmediateRebuild = false;
            m_ImmediateWhy = "-";
            m_DiagImmediateRequests = m_DiagImmediateRebuilds = 0;
            m_DiagSeededLoops = m_DiagSeededChairs = m_DiagGridLoops = 0;
            m_SeedLoadPass = true;   // F6 B4: the refusal line of the first rebuild says "expected"

            // Phase D (round-4-contract §3.2, §5): the hold is transient — every pair re-derives at this load's first
            // rebuild (its poll reads the saved ElectricityConsumer through the census). The fold was flushed in
            // OnGamePreload.
            m_Holds.Clear();
            m_HoldByLoop.Clear();
            m_HoldRows.Clear();
            m_RebuildUncarried.Clear();
            m_HoldFailOpenLogged.Clear();
            m_HoldLog.Reset();
            m_HoldRowsRevision = int.MinValue;
            m_HoldPollFrame = uint.MaxValue;
            m_HoldCensusWait = 0;
            m_HoldInGame = mode == GameMode.Game;     // the census arms in game mode only
            m_HoldInEditor = mode == GameMode.Editor;
            m_HoldArmLogged = m_HoldCensusWarned = m_HoldEditorLogged = false;
            m_HeldLoopCount = m_HeldChairCount = m_HeldChairsForPower = 0;
            m_HeldExitSuffix = string.Empty;
            m_DiagHoldPolls = m_DiagHoldPowerReads = m_DiagHoldChanges = m_DiagHeldWrites = m_DiagHoldDropped = 0;
            m_DiagHoldPollMs = 0.0;
            m_HoldLastChange = "-";

            LogPrefabState();
        }

        /// <summary>Phase D (round-4-contract §4.2): a load (or leaving to the menu) writes the folded "[state]" lines of the
        /// city being left. Never throws: GameSystemBase.GamePreload DISABLES a system whose OnGamePreload throws
        /// (Game/GameSystemBase.cs:85-96), the census's precedent (LiftCensusSystem.OnGamePreload).</summary>
        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            try
            {
                if (m_HoldLog.Count > 0)
                    WriteHoldSummary(UnityEngine.Time.realtimeSinceStartup);
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"{nameof(ChairPinSystem)}: folded [state] lines could not be written before the load " +
                             $"({e.GetType().Name}: {e.Message}) — non-fatal");
            }
            // The city is being left: nothing is held any more. OnGameLoadingComplete sets the mode again for a Game /
            // Editor load; a MainMenu load leaves both false (no gate, no hold line, no census WARN in the menu, where the
            // census is not armed: LiftCensusSystem.OnGameLoadingComplete).
            m_HoldInGame = m_HoldInEditor = false;
            m_Holds.Clear();
            m_HoldByLoop.Clear();
            m_HeldLoopCount = m_HeldChairCount = m_HeldChairsForPower = 0;
            m_HeldExitSuffix = string.Empty;
        }

        /// <summary>Phase D (round-4-contract §4.2): quit to desktop raises no OnGamePreload, but the world's systems are
        /// destroyed while the logger still lives (LogWindow.cs header): the folded "[state]" lines are written here.
        /// Teardown never throws.</summary>
        protected override void OnDestroy()
        {
            try
            {
                if (m_HoldLog.Count > 0)
                    WriteHoldSummary(UnityEngine.Time.realtimeSinceStartup);
            }
            catch (Exception)
            {
                // nothing left to report to
            }
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            m_DiagUpdateCalls++;
            m_DiagLastUpdateFrame = m_RenderingSystem.frameIndex;
            try
            {
                // Phase D (round-4-contract §4.2): the end of a "[state]" burst is written at most 10 s after the last line,
                // whatever path this update takes (the census's precedent). Per frame: one int compare while nothing is
                // folded.
                if (m_HoldLog.Count > 0)
                {
                    float now = UnityEngine.Time.realtimeSinceStartup;
                    if (m_HoldLog.TrailingDue(now))
                        WriteHoldSummary(now);
                }

                // Catalog Phase A (ext rows 1, 3): the catalog's views, validated by the first catalog call of the
                // frame (were m_ChairPrefabs.Count / m_ChairliftPrefab != Null, resolved once per app run). Before
                // the first game / editor load both are empty: the startup main menu exits here, as before (M1).
                if (m_Disabled || m_PrefabCatalog.CarrierGripTops.Count == 0)
                {
                    // error-tripped this load, or chair prefab missing — logged already
                    m_DiagLastExit = m_Disabled ? "disabled by exception" : "no chair prefab resolved";
                    return;
                }

                if (MovingChairs && m_PrefabCatalog.LiftLinePrefabSet.Count > 0)
                {
                    UpdateMovingChairs();
                    return;
                }
                // (chairlift net prefab unresolved → the static flow below still pins)
                m_DiagLastExit = MovingChairs
                    ? "static flow (chairlift net prefab unresolved)"
                    : "static flow (moving mode off)";

                if (m_FullPassPending)
                {
                    // v1.3 + entry 98: a reconnect-requested pass can land in the deletion gap
                    // (old chairs gone, replacements not yet spawned) — stay armed until chairs
                    // appear or the retry window closes. The retry no longer re-scans the map
                    // every armed frame (review fix): it waits for the cheap change probe to
                    // see a chair spawn, then runs; the final window frame scans regardless as
                    // a belt. The load pass keeps consume-immediately semantics (it must log
                    // its summary exactly once, even on lift-free maps).
                    bool isLoadPass = m_PendingPassLabel == "load pass";
                    bool lastAttempt = --m_FullPassRetries <= 0;
                    bool chairsAppeared = !m_UpdatedChairQuery.IsEmptyIgnoreFilter &&
                                          CountMatchingChairs(m_UpdatedChairQuery) > 0;
                    if (isLoadPass || chairsAppeared || lastAttempt)
                    {
                        int processed = PinChairs(m_AllChairQuery, 0f, tagUpdated: true,
                            logLabel: m_PendingPassLabel, out _);
                        if (processed > 0 || isLoadPass || lastAttempt)
                        {
                            m_FullPassPending = false;
                            if (processed == 0 && !isLoadPass)
                                Mod.Log.Info($"{nameof(ChairPinSystem)} [{m_PendingPassLabel}]: no chairs " +
                                             "appeared within the retry window — nothing to re-pin");
                        }
                    }
                }
                else if (!m_UpdatedChairQuery.IsEmptyIgnoreFilter)
                {
                    // Chairs created/re-seated this frame already carry Updated; culling and the
                    // search tree will pick the write up downstream of this phase without help.
                    int processed = PinChairs(m_UpdatedChairQuery, 0f, tagUpdated: false,
                        logLabel: "update", out _);

                    // v1.2.1 observability: prove the event path is alive the first time it
                    // matches, and expose the previously silent zero-prefab-match case.
                    if (processed > 0 && !m_UpdateAliveLogged)
                    {
                        m_UpdateAliveLogged = true;
                        Mod.Log.Info($"{nameof(ChairPinSystem)} [update] pass alive — " +
                                     $"first event pass matched {processed} chair(s) this load");
                    }
                    else if (processed == 0 && !m_ZeroMatchLogged)
                    {
                        m_ZeroMatchLogged = true;
                        Mod.Log.Info($"{nameof(ChairPinSystem)} [update] diagnostic: change event fired but " +
                                     $"0 of {m_UpdatedChairQuery.CalculateEntityCount()} updated owned " +
                                     "object(s) matched a chair prefab (logged once per load)");
                    }
                }

                // v1.2.1: pin tool-preview chairs every frame they exist, silently — the drag
                // preview now shows exactly what the applied lift will look like.
                if (!m_TempChairQuery.IsEmptyIgnoreFilter)
                    PinChairs(m_TempChairQuery, 0f, tagUpdated: false, logLabel: null, out _);

                // v1.2.1 watchdog: silence-proofing of last resort. A full idempotent pass on
                // a slow cadence; anything it pins was MISSED by the event path — self-heal
                // and say so loudly. In steady state it pins nothing and logs nothing.
                if (--m_WatchdogCountdown <= 0)
                {
                    m_WatchdogCountdown = WatchdogIntervalFrames;
                    int processed = PinChairs(m_AllChairQuery, 0f, tagUpdated: true,
                        logLabel: null, out int pinnedByWatchdog);
                    if (pinnedByWatchdog > 0)
                    {
                        Mod.Log.Warn($"{nameof(ChairPinSystem)} [watchdog]: pinned {pinnedByWatchdog} of " +
                                     $"{processed} chair(s) that the change-event pass missed — " +
                                     "self-healed, but please report this log line");
                    }
                }
            }
            catch (Exception e)
            {
                // Never throw into the game loop. Unmissable, with the full stack, once per
                // load; the prefab table is KEPT so the next load re-arms automatically.
                m_Disabled = true;
                m_LastError = e.ToString();
                m_DiagLastExit = "exception (see Last error)";
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(ChairPinSystem)} EXCEPTION — chair pinning disabled until " +
                                  $"the next game load. Please report this stack:\n{e}");
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────────
        // Core pass: collect matching chairs from the query, pin each onto its cable lane.
        // Runs on the main thread with EntityManager — chair counts are tiny (a 1 km lift is
        // ~250 chairs) and passes are event-driven, so a Burst job would be complexity without
        // a measurable win. Chunk iteration keeps the prefab filter cheap on the full pass,
        // where the query also matches every other Secondary sub-object on the map.
        // ─────────────────────────────────────────────────────────────────────────────────
        /// <summary>Runs one pinning pass. Returns the number of prefab-matched chairs
        /// processed; <paramref name="pinnedCount"/> reports how many were actually moved.</summary>
        private int PinChairs(EntityQuery query, float advanceDistance, bool tagUpdated,
            string logLabel, out int pinnedCount)
        {
            int pinned = 0, alreadyPinned = 0, skippedNoLane = 0, skippedFar = 0;
            int processed;

            // Catalog Phase A (ext row 1): the carrier view, fetched ONCE before the chunk loop (was m_ChairPrefabs).
            // Catalog Phase B (§5.2 items 3, 5): with the carrier → type view, fetched once too: Shuttle carriers (the
            // tram's) are never pinned, and each carrier's approach ceiling is its type's.
            IReadOnlyDictionary<Entity, float> grips = m_PrefabCatalog.CarrierGripTops;
            IReadOnlyDictionary<Entity, LiftTypeDef> carrierTypes = m_PrefabCatalog.CarrierTypes;
            var chairs = new NativeList<Entity>(64, Allocator.Temp);
            var chunks = query.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var entityHandle = GetEntityTypeHandle();
                var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (IsPinnable(grips, carrierTypes, prefabRefs[i].m_Prefab))
                            chairs.Add(entities[i]);
                    }
                }

                processed = chairs.Length;
                for (int i = 0; i < chairs.Length; i++)
                {
                    var chair = chairs[i];
                    // TryGetValue, never the indexer (contract §2.1); always true here: the same view filtered above.
                    Entity prefab = EntityManager.GetComponentData<PrefabRef>(chair).m_Prefab;
                    if (!grips.TryGetValue(prefab, out float gripTop))
                        continue;
                    switch (TryPinChair(chair, gripTop, CeilingOf(carrierTypes, prefab), advanceDistance, tagUpdated))
                    {
                        case PinResult.Pinned: pinned++; break;
                        case PinResult.AlreadyPinned: alreadyPinned++; break;
                        case PinResult.NoCableLane: skippedNoLane++; break;
                        case PinResult.LaneTooFar: skippedFar++; break;
                    }
                }
            }
            finally
            {
                chunks.Dispose();
                chairs.Dispose();
            }

            // One-line verifiable summary (mod log): emitted on load always, on changes only
            // when chairs were actually touched. Moving mode (milestone 2) stays silent.
            if (logLabel != null && (pinned + alreadyPinned + skippedNoLane + skippedFar > 0 || logLabel == "load pass"))
            {
                Mod.Log.Info(
                    $"{nameof(ChairPinSystem)} [{logLabel}]: {pinned} chair(s) pinned to cable lanes, " +
                    $"{alreadyPinned} already in place, {skippedNoLane} left untouched (no cable sublane), " +
                    $"{skippedFar} left at spawn placement (cable too far — station approach)");
            }

            pinnedCount = pinned;
            return processed;
        }

        private enum PinResult
        {
            Pinned,
            AlreadyPinned,
            NoCableLane,
            LaneTooFar
        }

        /// <summary>Catalog Phase B (§5.2 item 3): a carrier prefab this system pins or moves — in the CarrierGripTops view
        /// (usable bounds) and not of a Shuttle lift type (a tram cabin keeps its hosted pose, R29).</summary>
        private static bool IsPinnable(IReadOnlyDictionary<Entity, float> grips, IReadOnlyDictionary<Entity, LiftTypeDef> types,
            Entity prefab)
        {
            if (!grips.ContainsKey(prefab))
                return false;
            return !(types.TryGetValue(prefab, out LiftTypeDef def) && def != null && def.Mover == MoverKind.Shuttle);
        }

        /// <summary>Catalog Phase B (§5.2 item 5, D9): the carrier's approach-pin ceiling, its type's
        /// ApproachPinCeilingM = max(4.0, HangM + 1.5); the fixed-grip 4.0 when the type is unknown.</summary>
        private static float CeilingOf(IReadOnlyDictionary<Entity, LiftTypeDef> types, Entity prefab)
        {
            return types.TryGetValue(prefab, out LiftTypeDef def) && def != null && def.ApproachPinCeilingM > 0f
                ? def.ApproachPinCeilingM
                : ApproachPinDistanceCeiling;
        }

        /// <summary>Catalog Phase B (§5.2 item 5): a loop's approach-pin ceiling (its type's; fixed-grip 4.0 when the loop
        /// has no single type).</summary>
        private static float CeilingOf(LiftLoopCache.Loop loop)
        {
            LiftTypeDef def = SkiCatalog.LiftByIndex(loop.m_TypeIndex);
            return def != null && def.ApproachPinCeilingM > 0f ? def.ApproachPinCeilingM : ApproachPinDistanceCeiling;
        }

        private PinResult TryPinChair(Entity chair, float gripTop, float approachCeiling, float advanceDistance, bool tagUpdated)
        {
            // Owner must be a live edge carrying sublanes; anything else (mid-demolition,
            // station oddities) → leave the chair exactly as vanilla placed it.
            Entity owner = EntityManager.GetComponentData<Owner>(chair).m_Owner;
            if (owner == Entity.Null || !EntityManager.Exists(owner))
                return PinResult.NoCableLane;
            if (!EntityManager.TryGetBuffer(owner, isReadOnly: true, out DynamicBuffer<Game.Net.SubLane> subLanes))
                return PinResult.NoCableLane;

            var transform = EntityManager.GetComponentData<Transform>(chair);

            // v1.4 side guard (review fix, entry 98): hanging cable lanes are straight XZ
            // CHORDS (LaneSystem rebuilds b/c as lerp(a,d) for hanging lanes), so on a
            // curved span whose sagitta exceeds half the cable gauge (1.8 m) the 3D-nearest
            // lane can be the OPPOSITE side's chord — nearest-distance alone would merge
            // both rows onto one rope. Resolve side by SIGN against the owner EDGE curve:
            // a candidate whose lateral sign disagrees with the chair's is never a valid
            // pin target. Degenerate signs (point ~on the edge curve) fall back to distance.
            float chairSide = 0f;
            bool hasEdgeCurve = EntityManager.TryGetComponent<Curve>(owner, out var edgeCurve);
            if (hasEdgeCurve)
                chairSide = SideSign(edgeCurve.m_Bezier, transform.m_Position);

            // Nearest same-side HV cable lane = the chair's own-side lane (header note 3).
            bool found = false;
            float bestDistance = float.MaxValue;
            float bestT = 0f;
            Curve bestCurve = default;
            for (int i = 0; i < subLanes.Length; i++)
            {
                Entity lane = subLanes[i].m_SubLane;
                if (!EntityManager.TryGetComponent<Curve>(lane, out var curve) ||
                    !EntityManager.TryGetComponent<PrefabRef>(lane, out var lanePrefabRef) ||
                    !EntityManager.TryGetComponent<UtilityLaneData>(lanePrefabRef.m_Prefab, out var laneData) ||
                    (laneData.m_UtilityTypes & UtilityTypes.HighVoltageLine) == 0)
                    continue;

                if (hasEdgeCurve && chairSide != 0f)
                {
                    float laneSide = SideSign(edgeCurve.m_Bezier,
                        MathUtils.Position(curve.m_Bezier, 0.5f));
                    if (laneSide != 0f && laneSide != chairSide)
                        continue; // wrong-side chord
                }

                float distance = MathUtils.Distance(curve.m_Bezier, transform.m_Position, out float t);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestT = t;
                    bestCurve = curve;
                    found = true;
                }
            }

            if (!found)
                return PinResult.NoCableLane;

            // v1.2: is this a station-approach edge, and which end is the station?
            // v1.2.2: detection covers both build orders (see IsStationConnectionNode) —
            // an owned node (stations-first) or an unowned node carrying the station's
            // owned stub edge (line-first).
            bool stationAtStart = false, stationAtEnd = false;
            if (EntityManager.TryGetComponent<Game.Net.Edge>(owner, out var edgeTopology))
            {
                stationAtStart = IsStationConnectionNode(edgeTopology.m_Start);
                stationAtEnd = IsStationConnectionNode(edgeTopology.m_End);
            }
            bool isApproach = stationAtStart || stationAtEnd;

            // v1.3 plausibility guard: on normal spans, farther than gripTop+margin means a
            // wrong-side match or degenerate geometry — leave the chair at spawn placement.
            // On approach edges divergence from the row line is EXPECTED (the anchored lane
            // dives into the station), so the ceiling is the wrong-side floor instead; no
            // mid-span guard firing, no pinned/spawn zigzag.
            // Catalog Phase B (§5.2 item 5; was ApproachPinDistanceCeiling, the fixed-grip 4.0): the carrier type's.
            float distanceCeiling = isApproach ? approachCeiling
                                               : gripTop + MaxPinDistanceMargin;
            if (bestDistance > distanceCeiling)
                return PinResult.LaneTooFar;

            // Milestone 2: advance along the lane by arc length. t-space advance scaled by the
            // lane's total length is a uniform-speed approximation (good enough for the
            // scaffold; proper arc-length reparameterization is a milestone-2 refinement).
            // The end clamp below also bounds the advance — a moving chair holds just short of
            // the lane end; cross-edge looping is OUT of scope (see header).
            float pinT = bestT;
            if (advanceDistance > 0f)
                pinT += advanceDistance / math.max(bestCurve.m_Length, 0.01f);

            // End clamps, per side (v1.3): the station side of the LANE curve gets the big
            // clamp so the last chair holds just outside the station face (the anchored lane
            // continues onto the bullwheel stub INSIDE the building). Which lane end is the
            // station side is decided geometrically — nearest curve end to the station node —
            // because lane t-direction vs edge node order is not guaranteed. Tower/normal
            // ends keep the 1 m anchor-stub clamp. Inert for interior chairs: EvenSpacing
            // never places a chair within a spacing-length of an edge end.
            float clampT0 = EndClampMeters, clampT1 = EndClampMeters;
            if (isApproach)
            {
                float3 curveStart = MathUtils.Position(bestCurve.m_Bezier, 0f);
                float3 curveEnd = MathUtils.Position(bestCurve.m_Bezier, 1f);
                if (stationAtStart &&
                    EntityManager.TryGetComponent<Game.Net.Node>(edgeTopology.m_Start, out var startNode))
                {
                    if (math.distancesq(curveStart, startNode.m_Position) <=
                        math.distancesq(curveEnd, startNode.m_Position))
                        clampT0 = StationEndClampMeters;
                    else
                        clampT1 = StationEndClampMeters;
                }
                if (stationAtEnd &&
                    EntityManager.TryGetComponent<Game.Net.Node>(edgeTopology.m_End, out var endNode))
                {
                    if (math.distancesq(curveEnd, endNode.m_Position) <=
                        math.distancesq(curveStart, endNode.m_Position))
                        clampT1 = StationEndClampMeters;
                    else
                        clampT0 = StationEndClampMeters;
                }
            }
            float laneLength = math.max(bestCurve.m_Length, 0.01f);
            pinT = math.clamp(pinT,
                math.min(clampT0 / laneLength, 0.5f),
                1f - math.min(clampT1 / laneLength, 0.5f));

            float3 lanePoint = MathUtils.Position(bestCurve.m_Bezier, pinT);
            float3 tangent = MathUtils.Tangent(bestCurve.m_Bezier, pinT);

            // Plumb hang: yaw-only rotation from the tangent (GetNodeRotation zeroes tangent.y);
            // sign chosen to preserve the authored facing of this row (up-side vs down-side).
            quaternion newRotation = NetUtils.GetNodeRotation(tangent, transform.m_Rotation);
            float3 newForward = math.mul(newRotation, new float3(0f, 0f, 1f));
            float3 oldForward = math.mul(transform.m_Rotation, new float3(0f, 0f, 1f));
            if (math.dot(newForward.xz, oldForward.xz) < 0f)
                newRotation = NetUtils.GetNodeRotation(-tangent, transform.m_Rotation);

            // Grip top onto the curve: drop the pivot by the runtime-read pivot→grip-top offset.
            // v1.3: no approach-blend — chairs ride the rope on every span (see header).
            float3 newPosition = lanePoint;
            newPosition.y -= gripTop;

            // Idempotence short-circuit — a chair already on target (repeat load pass, second
            // event in one frame) is not rewritten and not re-tagged.
            if (math.distancesq(newPosition, transform.m_Position) < 1e-6f &&
                math.abs(math.dot(newRotation, transform.m_Rotation)) > 0.99999f)
                return PinResult.AlreadyPinned;

            float deltaY = newPosition.y - transform.m_Position.y;
            transform.m_Position = newPosition;
            transform.m_Rotation = newRotation;
            EntityManager.SetComponentData(chair, transform);

            // Keep height-above-ground bookkeeping consistent with the vertical shift.
            if (EntityManager.TryGetComponent<Game.Objects.Elevation>(chair, out var elevation))
            {
                elevation.m_Elevation += deltaY;
                EntityManager.SetComponentData(chair, elevation);
            }

            // Static chairs have no InterpolatedTransform; handled anyway per the contract
            // (and for the milestone-2 future where chairs become interpolated movers).
            if (EntityManager.TryGetComponent<InterpolatedTransform>(chair, out var interpolated))
            {
                interpolated.m_Position = transform.m_Position;
                interpolated.m_Rotation = transform.m_Rotation;
                EntityManager.SetComponentData(chair, interpolated);
            }

            // Load-pass (and moving-mode) chairs carry no Updated tag — add it so PreCulling
            // and SearchSystem refresh this entity; CleanUpSystem removes it at end of frame.
            if (tagUpdated && !EntityManager.HasComponent<Updated>(chair))
                EntityManager.AddComponent<Updated>(chair);

            return PinResult.Pinned;
        }

        // ─────────────────────────────────────────────────────────────────────────────────
        // MILESTONE 2 — the moving-chairs pass (every Unity frame; see the header block for
        // the verified design). Runs only when the chairlift net prefab resolved.
        // ─────────────────────────────────────────────────────────────────────────────────
        private void UpdateMovingChairs()
        {
            // Change detection: any created/updated REAL chair this frame means vanilla
            // touched the fleet (lift edit, respawn after reconnect) — rebuild the cache.
            // Our own bounds-refresh Updated tags can never re-enter here: tags added at
            // Modification4B are stripped by CleanUpSystem at the end of the SAME frame.
            m_DiagProbeUpdatedChairs = m_UpdatedChairQuery.IsEmptyIgnoreFilter
                ? 0
                : CountMatchingChairs(m_UpdatedChairQuery);
            if (m_DiagProbeUpdatedChairs > 0)
            {
                m_LoopCacheDirty = true;
                m_EmptyRosterRetries = 0; // real chair activity re-arms the roster retry
            }

            // Watchdog repurposed (silence-proofing of last resort): a slow-cadence forced
            // rebuild that self-heals missed invalidation (e.g. a lane-curve rebuild that
            // never reseated chairs). Gated (review fix, entry 98): it only fires when the
            // last rebuild actually saw lift infrastructure — on lift-free maps the mod
            // does zero steady-state work, and the rebuild log is drift-gated besides.
            if (--m_WatchdogCountdown <= 0)
            {
                m_WatchdogCountdown = WatchdogIntervalFrames;
                if (m_LastBuildHadLanes || m_LoopCache.m_Loops.Count > 0)
                    m_LoopCacheDirty = true;
            }

            if (m_RebuildCooldown > 0)
                m_RebuildCooldown--;
            bool rebuilt = false;
            if (m_LoopCacheDirty && (m_RebuildCooldown <= 0 || m_ImmediateRebuild))
            {
                // Debounce guards the reconnect poke storms (DECISIONS 40): at most one
                // rebuild per window, dirty flag re-set by the triggers if needed. Round L1:
                // RequestImmediateRebuild bypasses the window ONCE (lift topology changed /
                // parked chairs re-seated) and then the window restarts as usual.
                if (m_ImmediateRebuild && m_RebuildCooldown > 0)
                    m_DiagImmediateRebuilds++;
                m_ImmediateRebuild = false;
                m_RebuildCooldown = RebuildCooldownFrames;
                BuildLoopCache();
                rebuilt = true;
            }

            // Tool-preview chairs keep milestone-1 static pinning so the drag preview
            // matches the applied lift (Temp chairs are never in the moving roster).
            if (!m_TempChairQuery.IsEmptyIgnoreFilter)
                PinChairs(m_TempChairQuery, 0f, tagUpdated: false, logLabel: null, out _);

            // The derived clock: frozen on pause (RenderingSystem holds frameIndex/frameTime
            // when selectedSpeed ~ 0), smooth at any sim speed, save-continuous. Double is
            // mandatory — frameIndex reaches 1e7+ in mature saves and float32 would quantize
            // speed * time at metre scale. Catalog Phase B (§5.2 item 2): the clock is in seconds and each loop's
            // phase is its own speed × this clock (PhaseOf); the pause test compares the clock (was the one phase).
            double timeS = TimeS();

            // Phase D (round-4-contract §3.2 step 3, D5-D6): the power hold per station pair — polled every HoldPollFrames
            // render frames and at each rebuild. A census read, not a pure one: ReadLiftPower updates the flicker-guard state,
            // may write a Lift [power] line and completes the electricity dispatch job (a sync point); a transition re-bases or
            // freezes the loop's offsets.
            bool holdChanged = PollHolds(timeS, rebuilt);

            // Phase D (§3.2 step 4; was ChairPinSystem.cs:1057 `… && !rebuilt`): a hold change is never skipped (it can
            // only happen while paused through a rebuild, which passes anyway).
            if (timeS == m_LastMoveTimeS && !rebuilt && !holdChanged)
            {
                // paused — the derived positions are identical, write nothing
                m_DiagPausedSkips++;
                m_DiagLastExit = "moving: phase unchanged (paused)";
                return;
            }
            m_LastMoveTimeS = timeS;
            m_DiagLastTimeS = timeS;

            long moverStart = System.Diagnostics.Stopwatch.GetTimestamp();   // M10: once-per-load cost line below
            int moved = 0, inPlace = 0, missing = 0, riding = 0;
            int heldWrites = 0;   // Phase D: frozen-pose writes of held loops (not "moved": S-8 liveness only)
            for (int l = 0; l < m_LoopCache.m_Loops.Count; l++)
            {
                LiftLoopCache.Loop loop = m_LoopCache.m_Loops[l];
                riding += loop.m_Chairs.Count;
                // Phase D (§3.2 step 5): a HELD loop is written only at a rebuild or the hold's entry, at the frozen pose
                // (LiftLoopHold.FrozenPhase = PhaseOf's product at FrozenTimeS, bit for bit); its chairs count in riding.
                // Taken only while some loop is held, so a powered city runs the lines below exactly as before.
                if (m_HeldLoopCount > 0 && l < m_HoldByLoop.Count && m_HoldByLoop[l].Held)
                {
                    LoopHold hold = m_HoldByLoop[l];
                    if (rebuilt || hold.PoseDirty)
                    {
                        double frozen = LiftLoopHold.FrozenPhase(loop.m_SpeedMps, hold.FrozenTimeS);
                        for (int i = 0; i < loop.m_Chairs.Count; i++)
                        {
                            double s = (loop.m_Offsets[i] + frozen) % loop.m_Length;
                            switch (MoveChair(loop.m_Chairs[i], loop.m_GripTops[i], loop, s))
                            {
                                case MoveResult.Moved: heldWrites++; break;
                                case MoveResult.InPlace: break;
                                case MoveResult.Missing: missing++; break;
                            }
                        }
                        hold.PoseDirty = false;
                    }
                    continue;
                }
                double phase = PhaseOf(loop, timeS);
                for (int i = 0; i < loop.m_Chairs.Count; i++)
                {
                    double s = (loop.m_Offsets[i] + phase) % loop.m_Length;
                    switch (MoveChair(loop.m_Chairs[i], loop.m_GripTops[i], loop, s))
                    {
                        case MoveResult.Moved: moved++; break;
                        case MoveResult.InPlace: inPlace++; break;
                        case MoveResult.Missing: missing++; break;
                    }
                }
            }
            if (riding > 0 && !m_BoundsCostLogged)
            {
                m_MoverCostTicks += System.Diagnostics.Stopwatch.GetTimestamp() - moverStart;
                m_MoverCostPasses++;
            }
            if (missing > 0)
                m_LoopCacheDirty = true; // roster chair died mid-window — heal next rebuild

            m_DiagLastMoved = moved;
            m_DiagLastInPlace = inPlace;
            m_DiagLastMissing = missing;
            m_DiagLastRiding = riding;
            if (moved > 0)
                m_DiagPassesWithMoves++;
            m_DiagHeldWrites += heldWrites;
            // Phase D (round-4-contract §4.3; was ChairPinSystem.cs:1093-1095): the held count appended only when > 0, as
            // m_HeldExitSuffix (PollHolds rebuilds it only when the count changes; "" for a powered city), so this line still
            // makes one string per frame, as before Phase D (round-4 review: no per-frame suffix format).
            m_DiagLastExit = null;   // L9: LastExitText() formats it from m_DiagLast* when the dev tab / dump reads it

            // Phase D (skeptic S-8; was ChairPinSystem.cs:1097-1101 `moved > 0`): a frozen-pose write proves the pass
            // alive too (a city loaded with every lift unpowered moves nothing); the held count is appended only when > 0,
            // so a powered city's line is unchanged.
            if ((moved > 0 || heldWrites > 0) && !m_MovingAliveLogged)
            {
                m_MovingAliveLogged = true;
                Mod.Log.Info($"{nameof(ChairPinSystem)} [moving] pass alive — {riding} chair(s) riding " +
                             $"{m_LoopCache.m_Loops.Count} loop(s), {m_StaticFallbackChairs} static-fallback" +
                             LiftLoopHold.HeldSuffix(m_HeldChairsForPower, m_HeldChairCount - m_HeldChairsForPower));
            }

            // Bounds refresh: CullingInfo + the static search tree only re-read Transform on
            // Updated — tag the whole roster every BoundsRefreshMeters of travel (~2 m lag
            // ceiling at any sim speed; suppressed entirely while paused because phase
            // freezes). CleanUpSystem strips the tags at end of frame. Catalog Phase B (§5.2 item 6): the travel of the
            // FASTEST moving loop, (TimeS − last) × its speed (fixed-grip only: 2.5 × Δt, the former phase delta).
            if ((timeS - m_LastBoundsTimeS) * m_MaxLoopSpeedMps >= BoundsRefreshMeters)
            {
                m_LastBoundsTimeS = timeS;
                long boundsStart = System.Diagnostics.Stopwatch.GetTimestamp();
                // M10 (code review 2026-10-19): one batched AddComponent for the whole roster instead of one structural change
                // per chair (~250 chairs per km of fixed-grip, every BoundsRefreshMeters of travel).
                var tagChairs = new NativeList<Entity>(64, Allocator.Temp);
                try
                {
                    for (int l = 0; l < m_LoopCache.m_Loops.Count; l++)
                    {
                        // Phase D (round-4-contract §3.2 step 7): a held loop's chairs do not travel — no refresh (its pose was
                        // tagged once at the hold's entry, below). m_MaxLoopSpeedMps is the rebuild's, as before, so the
                        // running loops keep their cadence.
                        if (m_HeldLoopCount > 0 && l < m_HoldByLoop.Count && m_HoldByLoop[l].Held)
                            continue;
                        CollectChairsToTag(m_LoopCache.m_Loops[l], tagChairs);
                    }
                    if (tagChairs.Length > 0)
                        EntityManager.AddComponent<Updated>(tagChairs.AsArray());
                }
                finally
                {
                    tagChairs.Dispose();
                }
                if (!m_BoundsCostLogged)
                {
                    m_BoundsCostTicks += System.Diagnostics.Stopwatch.GetTimestamp() - boundsStart;
                    m_BoundsCostPasses++;
                }
            }
            if (!m_BoundsCostLogged && m_MoverCostPasses >= MoverCostPassesToLog)
            {
                m_BoundsCostLogged = true;
                double msPerTick = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                Mod.Log.Info($"{nameof(ChairPinSystem)} [cost] chair mover (once per load, first {m_MoverCostPasses} moving passes): mean " +
                             $"{m_MoverCostTicks * msPerTick / m_MoverCostPasses:F3} ms per pass over {riding} chair(s) in {m_LoopCache.m_Loops.Count} loop(s); " +
                             $"bounds tag (one batched AddComponent): {m_BoundsCostPasses} refresh(es), mean " +
                             $"{(m_BoundsCostPasses > 0 ? m_BoundsCostTicks * msPerTick / m_BoundsCostPasses : 0.0):F3} ms");
            }

            // Phase D (round-4-contract §3.2 step 6): the hold-entry bounds tag — one Updated per chair of a loop that was
            // frozen this frame (the bounds-refresh guard above), so CullingInfo and the static search tree hold the
            // exact frozen pose (they re-read Transform only on Updated, PreCullingSystem.cs:709-718) instead of a pose
            // up to BoundsRefreshMeters stale. CleanUpSystem strips the tags at the end of the frame.
            if (m_HeldLoopCount > 0)
            {
                for (int l = 0; l < m_LoopCache.m_Loops.Count && l < m_HoldByLoop.Count; l++)
                {
                    LoopHold hold = m_HoldByLoop[l];
                    if (!hold.Held || !hold.TagBounds)
                        continue;
                    hold.TagBounds = false;
                    var heldTag = new NativeList<Entity>(32, Allocator.Temp);
                    try
                    {
                        CollectChairsToTag(m_LoopCache.m_Loops[l], heldTag);
                        if (heldTag.Length > 0)
                            EntityManager.AddComponent<Updated>(heldTag.AsArray());
                    }
                    finally
                    {
                        heldTag.Dispose();
                    }
                }
            }
        }

        /// <summary>M10: the loop's chairs that can take an Updated tag now (alive, not Deleted / Updated / Created).</summary>
        private void CollectChairsToTag(LiftLoopCache.Loop loop, NativeList<Entity> into)
        {
            for (int i = 0; i < loop.m_Chairs.Count; i++)
            {
                Entity chair = loop.m_Chairs[i];
                if (EntityManager.Exists(chair) &&
                    !EntityManager.HasComponent<Deleted>(chair) &&
                    !EntityManager.HasComponent<Updated>(chair) &&
                    !EntityManager.HasComponent<Created>(chair))
                {
                    into.Add(chair);
                }
            }
        }

        /// <summary>The derived clock in seconds (see the milestone-2 header): (frameIndex + frameTime) / 60 in double.
        /// Catalog Phase B (§5.2 item 2; was CurrentPhase() = ChairSpeedMetersPerSecond × this, in metres).</summary>
        private double TimeS()
        {
            return ((double)m_RenderingSystem.frameIndex + m_RenderingSystem.frameTime) / 60.0;
        }

        /// <summary>A loop's phase in metres: its speed × the clock — one formula for the mover and the OD-7 seeding, so
        /// a seeded chair lands exactly where it stood. For a fixed-grip loop (m_SpeedMps = (double)2.5f) this is the
        /// former CurrentPhase() bit for bit: the same double product of the same operands.</summary>
        private static double PhaseOf(LiftLoopCache.Loop loop, double timeS)
        {
            return loop.m_SpeedMps * timeS;
        }

        private enum MoveResult
        {
            Moved,
            InPlace,
            Missing
        }

        private MoveResult MoveChair(Entity chair, float gripTop, LiftLoopCache.Loop loop, double s)
        {
            if (!EntityManager.Exists(chair) ||
                EntityManager.HasComponent<Deleted>(chair) ||
                !EntityManager.TryGetComponent<Transform>(chair, out var transform))
                return MoveResult.Missing;

            LiftLoopCache.Sample(loop, s, out float3 position, out float3 travelTangent);
            position.y -= gripTop; // grip top rides ON the cable (milestone-1 contract)

            // Travel-facing yaw: GetNodeRotation zeroes tangent.y (plumb hang); its second
            // argument is only the degenerate-tangent fallback. Facing the travel direction
            // naturally reverses heading on the return side and through the bullwheel arcs —
            // no per-side sign logic.
            quaternion rotation = NetUtils.GetNodeRotation(travelTangent, transform.m_Rotation);

            // Idempotence short-circuit (paused frames after a forced pass, arc endpoints).
            if (math.distancesq(position, transform.m_Position) < 1e-6f &&
                math.abs(math.dot(rotation, transform.m_Rotation)) > 0.99999f)
                return MoveResult.InPlace;

            transform.m_Position = position;
            transform.m_Rotation = rotation;
            EntityManager.SetComponentData(chair, transform);

            // Elevation is deliberately NOT touched (review fix, entry 98): per-frame
            // delta-accumulation makes m_Elevation wrong by exactly the terrain rise along
            // the lift (it is height-above-GROUND, serialized into saves), while the
            // enrollment-time value stays approximately right — and nothing consumes it
            // for non-OnGround objects (GroundHeightSystem skips them).

            // InterpolatedTransform is deliberately NOT written: vanilla's
            // UpdateStaticAnimations copies Transform into it every rendered frame for
            // NearCamera chairs — that copy IS the render path (see header).
            return MoveResult.Moved;
        }

        /// <summary>Counts entities in the query whose prefab is a known chair prefab —
        /// the cheap change-detection probe for moving mode.</summary>
        private int CountMatchingChairs(EntityQuery query)
        {
            int count = 0;
            IReadOnlyDictionary<Entity, float> grips = m_PrefabCatalog.CarrierGripTops;   // Catalog Phase A (ext row 1)
            IReadOnlyDictionary<Entity, LiftTypeDef> carrierTypes = m_PrefabCatalog.CarrierTypes;   // Phase B: Shuttle out
            var chunks = query.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    for (int i = 0; i < prefabRefs.Length; i++)
                    {
                        if (IsPinnable(grips, carrierTypes, prefabRefs[i].m_Prefab))
                            count++;
                    }
                }
            }
            finally
            {
                chunks.Dispose();
            }
            return count;
        }

        // ─────────────────────────────────────────────────────────────────────────────────
        // Loop-cache rebuild: harvest stations + cable lanes, chain into loops, roster the
        // chairs, enroll them for per-frame rendering, assign even loop offsets. Main-thread
        // EntityManager on small counts; runs only on load / lift edits / watchdog cadence.
        // ─────────────────────────────────────────────────────────────────────────────────
        private struct PrevSlot
        {
            public double m_Offset;
            public double m_LoopLength;
        }

        private void BuildLoopCache()
        {
            m_LoopCacheDirty = false;
            LoopGeneration++;   // Round L1: every rebuild, whatever it finds (hub-u2-spec.md §5.4)

            // Carry-forward snapshot (review fix, entry 98): identity rebuilds (watchdog
            // self-heal, reconnect no-ops) keep every surviving chair's offset when its
            // loop length is unchanged — zero jump by construction. Keyed by entity.
            var previousSlots = new Dictionary<Entity, PrevSlot>();
            for (int l = 0; l < m_LoopCache.m_Loops.Count; l++)
            {
                LiftLoopCache.Loop old = m_LoopCache.m_Loops[l];
                for (int i = 0; i < old.m_Chairs.Count; i++)
                {
                    previousSlots[old.m_Chairs[i]] = new PrevSlot
                    {
                        m_Offset = old.m_Offsets[i],
                        m_LoopLength = old.m_Length,
                    };
                }
            }

            // 1. Station entities. Catalog Phase A (ext row 4, skeptic M4): the catalog's StationDefs view, fetched
            //    ONCE before the chunk loop, replaces both the PowerBridge station set (was :1130 / :1148; the same
            //    set PowerBridge serves now — one source of truth for what counts as a station) and the per-station
            //    GetPrefabName + StationWheelLocal lookup (was :1157-1160).
            IReadOnlyDictionary<Entity, StationDef> stationDefs = m_PrefabCatalog.StationDefs;
            var stations = new List<Entity>();
            var stationPositions = new List<float3>();
            var stationWheels = new List<float3>();
            m_DiagStationLines.Clear();
            var chunks = m_StationQuery.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var entityHandle = GetEntityTypeHandle();
                var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                var transformHandle = GetComponentTypeHandle<Transform>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    var transforms = chunks[c].GetNativeArray(ref transformHandle);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (!stationDefs.TryGetValue(prefabRefs[i].m_Prefab, out StationDef stationDef))
                            continue;
                        stations.Add(entities[i]);
                        stationPositions.Add(transforms[i].m_Position);

                        // Bullwheel centre (entry 103): rope height over the wheel, in
                        // station-local space, from the terminal generator's INVARIANT wheel
                        // placement (lift_terminal.py: drive wheel_x/z 0.2/3.5, return 0/3.7,
                        // rope = wheel + 0.05; authoring +X = travel maps to prefab +Z) — the
                        // catalog row's WheelLocal; non-finite = unknown → plain semicircle.
                        float3 wheel = math.all(math.isfinite(stationDef.WheelLocal))
                            ? ObjectUtils.LocalToWorld(transforms[i], stationDef.WheelLocal)
                            : new float3(float.NaN);
                        stationWheels.Add(wheel);
                        m_DiagStationLines.Add(
                            $"station {stations.Count - 1}: {stationDef.Prefab} {entities[i].Index}:{entities[i].Version} " +
                            $"at ({transforms[i].m_Position.x:F1},{transforms[i].m_Position.y:F1},{transforms[i].m_Position.z:F1}) " +
                            $"wheel {(math.all(math.isfinite(wheel)) ? $"({wheel.x:F1},{wheel.y:F1},{wheel.z:F1})" : "unknown")}");
                    }
                }
            }
            finally
            {
                chunks.Dispose();
            }

            // 2. HV cable lanes owned by the chairlift net — edges AND nodes both carry
            //    PrefabRef to the net prefab, so one owner-prefab check covers both. The
            //    UtilityLaneData HV flag skips the LV comm lane (same structural test as
            //    the static pin). Station/tower stub lanes are excluded automatically:
            //    their owners are building/object entities with different prefabs.
            //    Catalog Phase A (ext row 3; was `ownerPrefab.m_Prefab != m_ChairliftPrefab`, :1198-1201): the
            //    catalog's lift-line set, an explicit whitelist (ext-chk X1), fetched ONCE before the chunk loop.
            //    Catalog Phase B (§5.2 item 1; was LiftLinePrefabSet.Contains): the line → type view, fetched once; a lane
            //    counts when its owner's line type moves as a Loop (Shuttle lines — the tram, Phase C — are not harvested).
            IReadOnlyDictionary<Entity, LiftTypeDef> lineTypes = m_PrefabCatalog.LiftLineTypes;
            var lanes = new List<LiftLoopCache.CableLane>();
            // M9 (code review 2026-10-19): the query is every utility lane of the city (water, sewage, power, fences); the lane
            // PREFAB is tested first through a cached lookup (CableLanePrefabs), so only HV-lane-prefab lanes pay for the owner
            // read. One cost line per load (m_LaneScanLogged).
            long laneScanStart = System.Diagnostics.Stopwatch.GetTimestamp();
            int lanesSeen = 0, lanesHv = 0;
            chunks = m_LaneQuery.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var entityHandle = GetEntityTypeHandle();
                var laneHandle = GetComponentTypeHandle<Game.Net.Lane>(isReadOnly: true);
                var curveHandle = GetComponentTypeHandle<Curve>(isReadOnly: true);
                var ownerHandle = GetComponentTypeHandle<Owner>(isReadOnly: true);
                var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var laneData = chunks[c].GetNativeArray(ref laneHandle);
                    var curves = chunks[c].GetNativeArray(ref curveHandle);
                    var owners = chunks[c].GetNativeArray(ref ownerHandle);
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    lanesSeen += entities.Length;
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (!CableLanePrefabs.IsHighVoltage(EntityManager, prefabRefs[i].m_Prefab))
                            continue;
                        lanesHv++;
                        Entity ownerEntity = owners[i].m_Owner;
                        if (ownerEntity == Entity.Null ||
                            !EntityManager.TryGetComponent<PrefabRef>(ownerEntity, out var ownerPrefab) ||
                            !lineTypes.TryGetValue(ownerPrefab.m_Prefab, out LiftTypeDef lineDef) || lineDef == null ||
                            lineDef.Mover != MoverKind.Loop)
                            continue;
                        lanes.Add(new LiftLoopCache.CableLane
                        {
                            m_Lane = entities[i],
                            m_Owner = ownerEntity,
                            m_Bezier = curves[i].m_Bezier,
                            m_Start = laneData[i].m_StartNode,
                            m_End = laneData[i].m_EndNode,
                            m_Length = curves[i].m_Length,
                            m_TypeIndex = lineDef.Index,   // Catalog Phase B
                        });
                    }
                }
            }
            finally
            {
                chunks.Dispose();
            }

            m_LastBuildHadLanes = lanes.Count > 0;
            if (!m_LaneScanLogged)
            {
                m_LaneScanLogged = true;
                Mod.Log.Info($"{nameof(ChairPinSystem)} [cost] lane scan (first rebuild of the load): {lanesSeen} utility lane(s) in the city, " +
                             $"{lanesHv} of an HV lane prefab, {lanes.Count} kept as chairlift cable lane(s), " +
                             $"{(System.Diagnostics.Stopwatch.GetTimestamp() - laneScanStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency:F2} ms " +
                             "(lane prefab tested first through a cached lookup)");
            }

            // Station ownership map for the loop walk (entry 102): PathNode owners are
            // entity indices, and a station's anchors may be stamped with the station
            // itself OR one of its children (sub-objects, sub-net nodes/edges) — collect
            // them all so terminals classify and the walk still stops at shared stations.
            var stationByOwnerIndex = new Dictionary<int, int>();
            for (int i = 0; i < stations.Count; i++)
            {
                Entity station = stations[i];
                stationByOwnerIndex[station.Index] = i;
                if (EntityManager.TryGetBuffer<Game.Objects.SubObject>(station, isReadOnly: true, out var subObjects))
                    for (int k = 0; k < subObjects.Length; k++)
                        stationByOwnerIndex[subObjects[k].m_SubObject.Index] = i;
                if (EntityManager.TryGetBuffer<Game.Net.SubNet>(station, isReadOnly: true, out var subNets))
                    for (int k = 0; k < subNets.Length; k++)
                        stationByOwnerIndex[subNets[k].m_SubNet.Index] = i;
            }
            m_LoopCache.Build(lanes, stations, stationPositions, stationByOwnerIndex, stationWheels);

            // Catalog Phase B (§5.2 item 2): each loop's speed from its lift type; a loop whose lanes span several types
            // moves nothing (no roster below; its chairs static-pin) — one WARN per rebuild signature. The fastest speed
            // drives the bounds refresh (item 6). Addendum 1: the up-lane line, once per loop per load.
            m_MaxLoopSpeedMps = 0.0;
            int mixedLoops = 0;
            System.Text.StringBuilder mixedText = null;
            for (int l = 0; l < m_LoopCache.m_Loops.Count; l++)
            {
                LiftLoopCache.Loop loop = m_LoopCache.m_Loops[l];
                LiftTypeDef def = SkiCatalog.LiftByIndex(loop.m_TypeIndex);
                loop.m_SpeedMps = def != null && def.Mover == MoverKind.Loop && def.SpeedMps > 0f ? def.SpeedMps : 0.0;
                if (loop.m_SpeedMps > m_MaxLoopSpeedMps)
                    m_MaxLoopSpeedMps = loop.m_SpeedMps;
                if (def == null)
                {
                    mixedLoops++;
                    mixedText ??= new System.Text.StringBuilder(64);
                    if (mixedText.Length > 0)
                        mixedText.Append(", ");
                    mixedText.Append(loop.m_BaseStation.Index).Append(':').Append(loop.m_BaseStation.Version).Append(" ↔ ")
                             .Append(loop.m_TopStation.Index).Append(':').Append(loop.m_TopStation.Version);
                }
                long pairKey = PairKey(loop.m_BaseStation, loop.m_TopStation);
                if (m_UpLaneLogged.Add(pairKey))
                {
                    Mod.Log.Info($"{nameof(ChairPinSystem)} [loop] up lane = right looking uphill: base " +
                                 $"{loop.m_BaseStation.Index}:{loop.m_BaseStation.Version} → top {loop.m_TopStation.Index}:" +
                                 $"{loop.m_TopStation.Version} ({(def != null ? def.Id : "mixed lift types")}, {loop.m_Length:F0} m loop; " +
                                 $"{(loop.m_UpLaneDecided ? loop.m_UpLaneSwapped ? "chain order swapped" : "chain order kept" : "chains side by side undecided — order kept")})");
                }
            }
            m_DiagMixedLoops = mixedLoops;
            string mixedSignature = mixedLoops > 0 ? mixedText.ToString() : null;
            if (mixedSignature != null && mixedSignature != m_MixedLoopSignature)
            {
                Mod.Log.Warn($"{nameof(ChairPinSystem)} [moving]: {mixedLoops} loop(s) mix lift types ({mixedSignature}) — " +
                             "not moving: their chairs are static-pinned until the line and its stations are one kind of lift " +
                             "(the census reads them Unfinished, 'mixes lift types')");
            }
            m_MixedLoopSignature = mixedSignature;

            m_DiagRebuilds++;
            m_DiagLastRebuildFrame = m_RenderingSystem.frameIndex;
            m_DiagStations = stations.Count;
            m_DiagLanesMatched = lanes.Count;
            m_DiagLaneTotal = m_LaneQuery.CalculateEntityCount();

            // 3. Roster: every live chair joins its loop (by owner edge) or falls back to
            //    milestone-1 static pinning. Riding chairs are enrolled once with vanilla
            //    InterpolatedTransform (+ one Updated so batches/culling re-evaluate).
            m_StaticFallbackChairs = 0;
            int riding = 0, enrolled = 0;

            // Catalog Phase A (ext row 1): the carrier view, fetched ONCE before the chunk loop (was m_ChairPrefabs).
            // Catalog Phase B (§5.2 item 3): with the carrier → type view, also fetched once — Shuttle carriers (the
            // tram's cabins, hosted on the base station) are neither rostered nor static-pinned (counted for the DUMP).
            IReadOnlyDictionary<Entity, float> grips = m_PrefabCatalog.CarrierGripTops;
            IReadOnlyDictionary<Entity, LiftTypeDef> carrierTypes = m_PrefabCatalog.CarrierTypes;
            int shuttleCarriers = 0, typeMismatch = 0;
            var chairs = new NativeList<Entity>(64, Allocator.Temp);
            chunks = m_AllChairQuery.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var entityHandle = GetEntityTypeHandle();
                var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        Entity prefab = prefabRefs[i].m_Prefab;
                        if (IsPinnable(grips, carrierTypes, prefab))
                            chairs.Add(entities[i]);
                        else if (grips.ContainsKey(prefab))
                            shuttleCarriers++;
                    }
                }

                m_DiagChairsMatched = chairs.Length;
                for (int i = 0; i < chairs.Length; i++)
                {
                    Entity chair = chairs[i];
                    // TryGetValue, never the indexer (contract §2.1); always true here: the same view filtered above.
                    Entity chairPrefab = EntityManager.GetComponentData<PrefabRef>(chair).m_Prefab;
                    if (!grips.TryGetValue(chairPrefab, out float gripTop))
                        continue;
                    Entity owner = EntityManager.GetComponentData<Owner>(chair).m_Owner;

                    LiftLoopCache.Loop home = null;
                    for (int l = 0; l < m_LoopCache.m_Loops.Count; l++)
                    {
                        if (m_LoopCache.m_Loops[l].m_MemberOwners.Contains(owner))
                        {
                            home = m_LoopCache.m_Loops[l];
                            break;
                        }
                    }

                    // Catalog Phase B (§5.2 item 2): a loop that mixes lift types gets no roster — its chairs static-pin.
                    if (home != null && home.m_TypeIndex < 0)
                        home = null;

                    if (home == null)
                    {
                        // Unclosed lift (or mid-edit state): milestone-1 static pin.
                        TryPinChair(chair, gripTop, CeilingOf(carrierTypes, chairPrefab), 0f, tagUpdated: true);
                        m_StaticFallbackChairs++;
                        continue;
                    }

                    // Catalog Phase B (§5.2 item 3): a carrier of another type than its loop rides anyway (it needs a
                    // mixed net to happen; counted for the DUMP).
                    if (carrierTypes.TryGetValue(chairPrefab, out LiftTypeDef chairDef) && chairDef != null &&
                        chairDef.Index != home.m_TypeIndex)
                        typeMismatch++;

                    home.m_Chairs.Add(chair);
                    home.m_GripTops.Add(gripTop);
                    riding++;

                    if (!EntityManager.HasComponent<InterpolatedTransform>(chair))
                    {
                        var transform = EntityManager.GetComponentData<Transform>(chair);
                        EntityManager.AddComponentData(chair, new InterpolatedTransform(transform));
                        enrolled++;
                    }
                    if (!EntityManager.HasComponent<Updated>(chair) &&
                        !EntityManager.HasComponent<Created>(chair))
                    {
                        EntityManager.AddComponent<Updated>(chair);
                    }
                }
            }
            finally
            {
                chunks.Dispose();
                chairs.Dispose();
            }
            m_DiagEnrolled = enrolled;
            m_DiagShuttleCarriers = shuttleCarriers;
            m_DiagTypeMismatch = typeMismatch;

            // Catalog Phase B (§5.2 item 4, D2): with the line prefab's m_Spacing = SkiCatalog SpacingM a loop can never
            // carry more than floor(loop / SpacingM) − 1 carriers (EvenSpacing n = (int)(L / s − 0.5) per edge and row,
            // SubObjectSystem.cs:1743; the loop adds the bullwheel arcs and terminal runs — contract §3.1). MORE means the
            // prefab drifted below the catalog: all of them ride, evenly and denser than designed; one WARN per type per
            // load. FEWER is the normal case and logs nothing.
            for (int l = 0; l < m_LoopCache.m_Loops.Count; l++)
            {
                LiftLoopCache.Loop loop = m_LoopCache.m_Loops[l];
                LiftTypeDef def = SkiCatalog.LiftByIndex(loop.m_TypeIndex);
                if (def == null || !(def.SpacingM > 0f) || !(loop.m_Length > 0.0) || m_SpacingWarned.Contains(def.Index))
                    continue;
                long holds = (long)Math.Floor(loop.m_Length / def.SpacingM);
                if (loop.m_Chairs.Count <= holds)
                    continue;
                m_SpacingWarned.Add(def.Index);
                Mod.Log.Warn($"{nameof(ChairPinSystem)}: a {def.Id} loop (base {loop.m_BaseStation.Index}:{loop.m_BaseStation.Version} " +
                             $"→ top {loop.m_TopStation.Index}:{loop.m_TopStation.Version}) carries {loop.m_Chairs.Count} carriers, " +
                             $"more than its {loop.m_Length:F0} m loop holds at SkiCatalog SpacingM {def.SpacingM:0.##} m ({holds}) — " +
                             $"the line prefab '{def.LinePrefab}' m_Spacing is below the catalog's; riding them all, denser than " +
                             "designed (logged once per lift type per load)");
            }

            // 4. Slot assignment (review fix, entry 98): even spacing. An identity rebuild
            //    carries every offset forward BY ENTITY (same chairs, unchanged loop length
            //    → same slots, zero jump — a chair deleted mid-session even leaves an
            //    honest gap). Anything else redistributes on a deterministic entity-index
            //    grid: a one-time visual redistribution, accepted for v1 (the previous
            //    rank-by-projection scheme keyed off spawn-edge geometry that stops meaning
            //    anything once a chair has traveled — it was identity only by accident).
            //    Round L1, OD-7 (a): a loop where NO chair rode before (the lift has just closed,
            //    first time or again after a redrawn section) starts every chair from where it
            //    stands — parked chairs keep their place (header ROUND L1). Mixed and length-
            //    changed loops keep the grid (OD-7 (b)).
            double seedTimeS = TimeS();   // Catalog Phase B (§5.2 item 2): each loop seeds with its own phase
            int seededChairs = 0;
            int seedRefusals = 0;                       // F6 B4: loops refused this rebuild (log only)
            bool seedLoadPass = m_SeedLoadPass;         // the first rebuild of this load (a new city's has no loop)
            m_SeedLoadPass = false;
            m_SeedRefusalText?.Clear();
            m_RebuildUncarried.Clear();   // Phase D (round-4-contract §3.2 step 3): this rebuild's record only
            for (int l = 0; l < m_LoopCache.m_Loops.Count; l++)
            {
                LiftLoopCache.Loop loop = m_LoopCache.m_Loops[l];
                int n = loop.m_Chairs.Count;
                if (n == 0)
                    continue;

                bool carried = true;
                bool anyPrevious = false;
                for (int i = 0; i < n; i++)
                {
                    if (!previousSlots.TryGetValue(loop.m_Chairs[i], out PrevSlot prev))
                    {
                        carried = false;
                        continue;
                    }
                    anyPrevious = true;
                    if (math.abs(prev.m_LoopLength - loop.m_Length) > 1e-3)
                        carried = false;
                }
                if (carried)
                {
                    for (int i = 0; i < n; i++)
                        loop.m_Offsets.Add(previousSlots[loop.m_Chairs[i]].m_Offset);
                    continue;
                }

                if (!anyPrevious)
                {
                    if (TrySeedFromPositions(loop, PhaseOf(loop, seedTimeS), out SeedRefusal refusal))
                    {
                        m_DiagSeededLoops++;
                        m_DiagSeededChairs += n;
                        seededChairs += n;
                        // Phase D (§3.2 step 3; record only): seeded at seedTimeS — a held pair re-freezes here.
                        m_RebuildUncarried[HoldKey(loop.m_BaseStation, loop.m_TopStation)] = true;
                        continue;
                    }
                    NoteSeedRefusal(loop, refusal, seedRefusals++);   // F6 B4: log only
                }
                m_DiagGridLoops++;
                // Phase D (§3.2 step 3; record only): gridded at seedTimeS — a held pair re-freezes here.
                m_RebuildUncarried[HoldKey(loop.m_BaseStation, loop.m_TopStation)] = false;

                double spacing = loop.m_Length / n;
                var order = new List<int>(n);
                for (int i = 0; i < n; i++)
                    order.Add(i);
                order.Sort((a, b) => loop.m_Chairs[a].Index.CompareTo(loop.m_Chairs[b].Index));
                for (int i = 0; i < n; i++)
                    loop.m_Offsets.Add(0.0);
                for (int rank = 0; rank < n; rank++)
                    loop.m_Offsets[order[rank]] = rank * spacing;
            }

            // 5. Deletion-gap retry (reconnect rebuilds can land between vanilla deleting
            //    old chairs and spawning replacements): loops without riders stay dirty
            //    for a bounded number of windows. LATCHED (review fix): once the limit is
            //    hit the burst ends for good — only real chair activity (the probe in
            //    UpdateMovingChairs) or a successful roster re-arms the counter.
            if (riding == 0 && m_LoopCache.m_Loops.Count > 0 && m_EmptyRosterRetries < EmptyRosterRetryLimit)
            {
                m_EmptyRosterRetries++;
                m_LoopCacheDirty = true;
            }
            else if (riding > 0)
            {
                m_EmptyRosterRetries = 0;
            }

            // The moving-mode load/observability signature: log on the FIRST rebuild per
            // load and whenever the counts drift — silent when a rebuild changes nothing
            // (watchdog self-heals, Move It drag storms), per the log-discipline contract.
            m_FullPassPending = false; // moving mode's load signature is the line below
            double totalLength = 0.0;
            for (int l = 0; l < m_LoopCache.m_Loops.Count; l++)
                totalLength += m_LoopCache.m_Loops[l].m_Length;
            // Phase D (round-4-contract §4.2; was ChairPinSystem.cs:1628-1631): ", h held (no power)" appended only when
            // h > 0 — h = the loops of this rebuild whose pair is held as of the last poll (this frame's poll, right after
            // the rebuild, writes its own [state] lines), so a powered city's signature is unchanged.
            // T116: the held part names its reason (LiftLoopHold.HeldSuffix): the FG lift of 2026-10-19 was held CLOSED
            // and read "1 held (no power)".
            int heldAtRebuild = CountHeldLoops(out int heldForPowerAtRebuild);
            string signature =
                $"{m_LoopCache.m_Loops.Count} loop(s) ({totalLength:F0} m total), {riding} chair(s) riding, " +
                $"{m_StaticFallbackChairs} static-fallback, " +
                $"{m_LoopCache.m_ChainCount} chain(s)/{m_LoopCache.m_UnclosedChainCount} unclosed" +
                LiftLoopHold.HeldSuffix(heldForPowerAtRebuild, heldAtRebuild - heldForPowerAtRebuild);
            if (signature != m_LastBuildSignature)
            {
                m_LastBuildSignature = signature;
                Mod.Log.Info($"{nameof(ChairPinSystem)} [moving] cache rebuilt: {signature}" +
                             (enrolled > 0 ? $" ({enrolled} newly enrolled)" : string.Empty) +
                             (seededChairs > 0 ? $"; {seededChairs} chair(s) start from where they stood (OD-7)" : string.Empty));
            }

            // F6 B4 (findings/log-audit.md A3: the west lift fell back to the grid twice, silently):
            // ONE line per rebuild that refused a loop's restart seeding — a refusal happens only for
            // a loop none of whose chairs rode before (a fresh closure or a load), so this is once per
            // event, never per frame. Log only: the grid fallback above is unchanged.
            if (seedRefusals > 0)
            {
                Mod.Log.Info($"{nameof(ChairPinSystem)} [moving] OD-7: {seedRefusals} loop(s) could not restart from " +
                             "where their chairs stood and use the even spacing instead (each chair jumps once): " +
                             m_SeedRefusalText +
                             (seedRefusals > SeedRefusalLoopsLogged ? $"; … and {seedRefusals - SeedRefusalLoopsLogged} more loop(s)" : string.Empty) +
                             (seedLoadPass ? " (first rebuild after loading: saved chairs stand wherever they rode, so " +
                                             "'from its own lane' failures are expected here)" : string.Empty));
            }

            if (m_LoopCache.m_UnclosedChainCount > 0 && !m_LoopWarnLogged)
            {
                // Round L1 (spec §5.3): an OPEN or UNFINISHED lift is explained by the census's own
                // lines (`Lift [line opened]` / `Lift [new line]`), so this warning is kept only for
                // loops that fail GEOMETRICALLY — a census-Complete lift with no closed loop. The
                // census builds before this system at Modification4B, so its rows are this frame's.
                // Without a running census the warning stays as it was.
                var census = World.GetExistingSystemManaged<LiftCensusSystem>();
                if (census == null || !census.IsArmed)
                {
                    m_LoopWarnLogged = true;
                    Mod.Log.Warn($"{nameof(ChairPinSystem)} [moving]: {m_LoopCache.m_UnclosedChainCount} cable " +
                                 "chain(s) did not close into a station-to-station loop — chairs on those " +
                                 "lifts are static-pinned (unfinished line, missing station, or unexpected " +
                                 "anchor topology; logged once per load)");
                }
                else
                {
                    string failed = CompleteLiftsWithoutLoop(census, out int count);
                    if (count > 0)
                    {
                        m_LoopWarnLogged = true;
                        Mod.Log.Warn($"{nameof(ChairPinSystem)} [moving]: {count} complete lift(s) did not close into a " +
                                     $"station-to-station loop ({failed}) — their chairs are static-pinned (unexpected anchor " +
                                     "topology; see the developer tab dump; logged once per load)");
                    }
                }
            }
        }

        private readonly List<LiftCensus.Row> m_CensusScratch = new List<LiftCensus.Row>();

        /// <summary>Census-Complete lifts with no closed loop in the cache just built ("base:v → top:v"
        /// list, log only — called at most until the once-per-load warning fires).</summary>
        private string CompleteLiftsWithoutLoop(LiftCensusSystem census, out int count)
        {
            count = 0;
            census.CopyRows(m_CensusScratch);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < m_CensusScratch.Count; i++)
            {
                LiftCensus.Row row = m_CensusScratch[i];
                if (row.State != LiftCensus.State.Complete)
                    continue;
                // T116 (2026-10-20 log 23:14:34): a Shuttle lift (the tram) has no ChairPin loop by design —
                // TramShuttleSystem moves it — so it is not a failed closure.
                if (!LiftLoopHold.ExpectsLoop(SkiCatalog.LiftByIndex(row.Type)))
                    continue;
                Entity b = LiftCensusSystem.Ent(row.Base), t = LiftCensusSystem.Ent(row.Top);
                if (TryGetLoop(b, t, out _, out _))
                    continue;
                count++;
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append(b.Index).Append(':').Append(b.Version).Append(" → ").Append(t.Index).Append(':').Append(t.Version);
            }
            m_CensusScratch.Clear();
            return sb.ToString();
        }

        /// <summary>Round L1, OD-7 (a): offsets so that every chair of a freshly closed loop rides on
        /// from where it stands — s = ProjectOnOwner(its owner's lane segments, its grip point),
        /// offset = (s − phase) mod L, so (offset + phase) mod L = s at this very frame. False (the
        /// caller falls back to the grid, nothing written) if any chair is gone, its owner has no
        /// lane segment in the loop, or its grip point is farther than ApproachPinDistanceCeiling
        /// from its own lane. That last gate is load-bearing: after a game LOAD the previous cache
        /// holds no rider, yet saved chairs stand wherever the mover left them on the loop (the
        /// serialized Transform), mostly far from their own section — projecting those onto their
        /// owner would pile them onto its lane ends. Parked chairs (static-pinned onto their own
        /// lane) and freshly spawned ones (vanilla placement on their own edge) pass it, by the
        /// same plausibility ceiling the static pin uses on station approaches.
        /// Round F6, item B4 (LOG ONLY, same return value, same writes): <paramref name="why"/> names the
        /// first failing chair and its cause; after a chair is too far the scan goes on for the COUNT of
        /// chairs over the limit and the farthest one (nothing is written on a refusal: the offsets are
        /// added only on success), and stops at once on the other causes, as before.</summary>
        private bool TrySeedFromPositions(LiftLoopCache.Loop loop, double phase, out SeedRefusal why)
        {
            why = default;
            int n = loop.m_Chairs.Count;
            double length = loop.m_Length;
            why.Scanned = n;
            // Catalog Phase B (§5.2 item 5; was ApproachPinDistanceCeiling, the fixed-grip 4.0): the loop type's ceiling.
            float ceiling = CeilingOf(loop);
            why.CeilingM = ceiling;
            if (!(length > 1.0))
            {
                why.Cause = SeedRefusalCause.LoopTooShort;
                return false;
            }
            m_SeedScratch.Clear();
            for (int i = 0; i < n; i++)
            {
                Entity chair = loop.m_Chairs[i];
                if (!EntityManager.TryGetComponent<Transform>(chair, out var transform) ||
                    !EntityManager.TryGetComponent<Owner>(chair, out var owner))
                {
                    if (why.Cause == SeedRefusalCause.None)
                    {
                        why.Cause = SeedRefusalCause.NoTransformOrOwner;
                        why.Chair = chair;
                    }
                    why.Scanned = i + 1;
                    return false;
                }
                float3 grip = transform.m_Position;
                grip.y += loop.m_GripTops[i];   // the grip rides ON the cable (milestone-1 contract)
                double s = LiftLoopCache.ProjectOnOwner(loop, owner.m_Owner, grip, out float distance);
                if (s < 0.0)
                {
                    if (why.Cause == SeedRefusalCause.None)
                    {
                        why.Cause = SeedRefusalCause.NoLaneSegment;
                        why.Chair = chair;
                        why.Owner = owner.m_Owner;
                    }
                    why.Scanned = i + 1;
                    return false;
                }
                if (distance > ceiling)
                {
                    // Not standing on its own section: a rider (e.g. a loaded save). Refused; scan on
                    // for the count only.
                    if (why.Cause == SeedRefusalCause.None)
                    {
                        why.Cause = SeedRefusalCause.TooFar;
                        why.Chair = chair;
                        why.Owner = owner.m_Owner;
                        why.DistanceM = distance;
                    }
                    why.OverLimit++;
                    why.FarthestM = math.max(why.FarthestM, distance);
                    continue;
                }
                if (why.Cause != SeedRefusalCause.None)
                    continue;   // already refused: no offset to compute
                double offset = (s - phase) % length;
                if (offset < 0.0)
                    offset += length;
                m_SeedScratch.Add(offset);
            }
            if (why.Cause != SeedRefusalCause.None)
                return false;
            loop.m_Offsets.AddRange(m_SeedScratch);
            return true;
        }

        private readonly List<double> m_SeedScratch = new List<double>();

        // ── Round F6, item B4: why a loop could not restart from where its chairs stood (log only) ──
        private enum SeedRefusalCause : byte { None, LoopTooShort, NoTransformOrOwner, NoLaneSegment, TooFar }

        private struct SeedRefusal
        {
            public SeedRefusalCause Cause;
            public Entity Chair, Owner;   // the first failing chair and its owner (section)
            public float DistanceM;       // TooFar: that chair's distance from its own lane
            public int OverLimit;         // TooFar: chairs over the loop type's approach ceiling (whole loop)
            public float FarthestM;
            public float CeilingM;        // Catalog Phase B: the loop type's ApproachPinCeilingM used (refusal text)
            public int Scanned;           // chairs looked at (all of them unless a non-distance cause stopped the scan)
        }

        /// <summary>At most this many loops are described in one refusal line; the rest are counted.</summary>
        private const int SeedRefusalLoopsLogged = 4;
        private System.Text.StringBuilder m_SeedRefusalText;   // reused; built only when a loop is refused
        private bool m_SeedLoadPass;                            // the next rebuild is the first of this load

        /// <summary>Appends one refused loop to this rebuild's refusal line (log only; allocates
        /// strings, called only on a refusal).</summary>
        private void NoteSeedRefusal(LiftLoopCache.Loop loop, in SeedRefusal why, int index)
        {
            if (index >= SeedRefusalLoopsLogged)
                return;
            var sb = m_SeedRefusalText ??= new System.Text.StringBuilder(256);
            if (index > 0)
                sb.Append(" | ");
            sb.Append("base ").Append(loop.m_BaseStation.Index).Append(':').Append(loop.m_BaseStation.Version)
              .Append(" → top ").Append(loop.m_TopStation.Index).Append(':').Append(loop.m_TopStation.Version)
              .Append(", ").Append(loop.m_Chairs.Count).Append(" chair(s), ").Append(loop.m_Length.ToString("F0")).Append(" m: ");
            string chair = $"chair {why.Chair.Index}:{why.Chair.Version}";
            string section = $"section {why.Owner.Index}:{why.Owner.Version}";
            switch (why.Cause)
            {
                case SeedRefusalCause.TooFar:
                    sb.Append($"{chair} on {section} is {why.DistanceM:F1} m from its own lane (limit " +
                              $"{why.CeilingM:F1} m); {why.OverLimit} of {why.Scanned} chair(s) over the limit, " +
                              $"farthest {why.FarthestM:F1} m");
                    break;
                case SeedRefusalCause.NoLaneSegment:
                    sb.Append($"{chair}'s {section} has no lane segment in the loop");
                    break;
                case SeedRefusalCause.NoTransformOrOwner:
                    sb.Append($"{chair} has no Transform or Owner");
                    break;
                case SeedRefusalCause.LoopTooShort:
                    sb.Append("the loop is too short to seed (≤ 1 m)");
                    break;
                default:
                    sb.Append("refused for an unknown reason");
                    break;
            }
        }

        /// <summary>Cross-system request (used by LiftReconnectSystem after it rebuilds
        /// lanes): re-pin every chair — and in moving mode rebuild the loop cache — on the
        /// next update. Corrected in Round L1 (entry 98 superseded the old wording): chairs
        /// never carry Secondary (SubObjectSystem.cs:2824-2827 adds only Owner + PrefabRef +
        /// Transform) and they ARE in their owner edge's SubObject buffer
        /// (SubObjectReferencesSystem.cs:122-138 queries Object + Owner, None Secondary). The
        /// full idempotent pass is used because the poked lanes are rebuilt by LaneSystem later
        /// in Modification4 and a pass over every chair is the simplest way to re-pin onto the
        /// NEW curves, not because chairs are unreachable. Same-frame when requested before
        /// Modification4B (our phase).</summary>
        internal void RequestFullPass(string label)
        {
            m_FullPassPending = true;
            m_PendingPassLabel = label;
            m_FullPassRetries = FullPassRetryFrames; // v1.3: survive the chair deletion gap
            m_LoopCacheDirty = true;                 // moving mode: lanes changed under the loop
        }

        /// <summary>Which side of a curve a point lies on in XZ: -1/+1, or 0 when
        /// degenerate (point ~on the curve, or no usable tangent). Used to keep chairs on
        /// their own row's cable chord (v1.4 side guard).</summary>
        private static float SideSign(Bezier4x3 curveBezier, float3 position)
        {
            MathUtils.Distance(curveBezier, position, out float t);
            float3 point = MathUtils.Position(curveBezier, t);
            float3 tangent = MathUtils.Tangent(curveBezier, t);
            float cross = tangent.x * (position.z - point.z) - tangent.z * (position.x - point.x);
            if (math.abs(cross) < 0.01f)
                return 0f;
            return cross > 0f ? 1f : -1f;
        }

        /// <summary>v1.2.2: is this edge end node a station connection? Stations-first: the
        /// line reuses the OWNED stub node (Owner present). Line-first: the stub course
        /// reuses the line's UNOWNED node (GenerateEdgesSystem.TryGetNode), so ownership
        /// never appears on the node itself — but the station's connection stub edge (which
        /// IS building-owned) sits in the node's ConnectedEdge buffer. Both shapes count.</summary>
        private bool IsStationConnectionNode(Entity node)
        {
            if (node == Entity.Null)
                return false;
            if (EntityManager.HasComponent<Owner>(node))
                return true;
            if (EntityManager.TryGetBuffer(node, isReadOnly: true, out DynamicBuffer<ConnectedEdge> connectedEdges))
            {
                for (int i = 0; i < connectedEdges.Length; i++)
                {
                    Entity edge = connectedEdges[i].m_Edge;
                    if (edge != Entity.Null && EntityManager.HasComponent<Owner>(edge))
                        return true;
                }
            }
            return false;
        }

        // ─────────────────────────────────────────────────────────────────────────────────
        // Prefab state — per load. Catalog Phase A (ext rows 1, 3; F5): ResolveChairPrefabs and
        // ResolveChairliftPrefab (were ChairPinSystem.cs:1630-1669 and :1726-1788: PrefabID fast
        // path, then a name scan, once per APP RUN) are gone. SkiPrefabCatalogSystem resolves the
        // same names with ONE typed resolver per load (its "PrefabCatalog [arm]" line names every
        // prefab FOUND or MISSING) and reads the carrier's grip-top from its geometry bounds (the
        // WARN for a carrier without usable bounds moved there, with this file's old text). This
        // method keeps this system's own lines, now per load: a WARN per chair prefab not found,
        // the two "inactive this load" WARNs behind OnUpdate's exits, and one Info line.
        // ─────────────────────────────────────────────────────────────────────────────────
        // Catalog Phase B (§5.2 items 7, 8): per lift type; the per-carrier and per-line WARNs fire for EXPECTED names only
        // (the per-name flags CarrierDef.Expected / LiftTypeDef.LineExpected, build-core.md §3); a pending MISSING name is
        // listed in the Info line ("pending (not imported yet): …"); a Shuttle type (the tram, Phase C) writes one Info.
        private void LogPrefabState()
        {
            IReadOnlyDictionary<Entity, float> grips = m_PrefabCatalog.CarrierGripTops;
            HashSet<Entity> lineSet = m_PrefabCatalog.LiftLinePrefabSet;
            var chairsFound = new System.Text.StringBuilder();
            var netsFound = new System.Text.StringBuilder();
            var netsMissing = new System.Text.StringBuilder();   // EXPECTED lines MISSING (WARN)
            var pending = new System.Text.StringBuilder();       // pending names MISSING (Info)
            IReadOnlyList<LiftTypeDef> lifts = SkiCatalog.Lifts;
            for (int l = 0; l < lifts.Count; l++)
            {
                LiftTypeDef def = lifts[l];
                bool shuttle = def.Mover == MoverKind.Shuttle;
                int shuttleFound = 0;
                for (int c = 0; c < def.Carriers.Count; c++)
                {
                    CarrierDef carrierDef = def.Carriers[c];
                    string name = carrierDef.Prefab;
                    Entity carrier = m_PrefabCatalog.EntityOf(name);
                    if (carrier == Entity.Null)
                    {
                        if (carrierDef.Expected)
                            Mod.Log.Warn($"{nameof(ChairPinSystem)}: chair prefab '{name}' not found — " +
                                         "chairs of this type will not be pinned (asset pack missing or renamed?)");
                        else
                            Append(pending, $"'{name}' ({def.Id} carrier)");
                        continue;
                    }
                    // FOUND without usable bounds: not in the view; the catalog WARNed ("PrefabCatalog: carrier
                    // prefab '…' has no usable ObjectGeometryData bounds — skipped …").
                    if (!grips.TryGetValue(carrier, out float gripTop))
                        continue;
                    if (shuttle)
                        shuttleFound++;
                    Append(chairsFound, $"'{name}' ({def.Id}{(shuttle ? ", shuttle: not pinned" : "")}, grip-top " +
                                        $"{gripTop.ToString("F3")} m)");
                }
                Entity line = m_PrefabCatalog.LineEntity(def);
                if (line != Entity.Null)
                    Append(netsFound, $"'{def.LinePrefab}' ({def.Id}{(shuttle ? ", shuttle: lanes not harvested" : "")})");
                else if (def.LineExpected)
                    Append(netsMissing, $"'{def.LinePrefab}'");
                else
                    Append(pending, $"'{def.LinePrefab}' ({def.Id} line)");

                if (shuttle)
                {
                    // Catalog Phase C (round-3-contract §4.12; was "moves as a Shuttle (Phase C): its lanes are not harvested
                    // and its N carrier(s) keep their hosted pose — not pinned, not moved"): the hand-off to TramShuttleSystem.
                    bool anyFound = line != Entity.Null || shuttleFound > 0;
                    Mod.Log.Info($"{nameof(ChairPinSystem)}: lift type '{def.Id}' moves as a Shuttle: TramShuttleSystem moves its " +
                                 $"{(anyFound ? shuttleFound.ToString() : "0")} carrier(s); ChairPin neither harvests its lanes nor " +
                                 $"pins them{(anyFound ? "" : " (pending, not imported)")}");
                }
            }

            if (grips.Count == 0)
                Mod.Log.Warn($"{nameof(ChairPinSystem)}: no chair prefabs resolved — pinning inactive this load");
            if (lineSet.Count == 0)
                Mod.Log.Warn($"{nameof(ChairPinSystem)}: net prefab(s) {(netsMissing.Length > 0 ? netsMissing.ToString() : "(every lift line)")} " +
                             "not found — moving chairs inactive this load (static pinning still runs)");
            else if (netsMissing.Length > 0)
                Mod.Log.Warn($"{nameof(ChairPinSystem)}: net prefab(s) {netsMissing} not found — chairs of those lift " +
                             "types have no line to ride this load");
            if (grips.Count > 0 || lineSet.Count > 0 || pending.Length > 0)
                Mod.Log.Info($"{nameof(ChairPinSystem)}: chair prefab(s) {(chairsFound.Length > 0 ? chairsFound.ToString() : "none")} " +
                             $"and line prefab(s) {(netsFound.Length > 0 ? netsFound.ToString() : "none")} from the prefab catalog; " +
                             $"pending (not imported yet): {(pending.Length > 0 ? pending.ToString() : "none")}");
        }

        private static void Append(System.Text.StringBuilder sb, string item)
        {
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append(item);
        }

        /// <summary>Catalog Phase B (addendum 1): a station pair's key for the once-per-loop up-lane line.</summary>
        private static long PairKey(Entity a, Entity b)
        {
            return ((long)a.Index << 32) ^ (uint)b.Index ^ ((long)b.Version << 20) ^ a.Version;
        }

        // ─────────────────────────────────────────────────────────────────────────────────
        // Phase D (round-4-contract §3.2, §4.2, D4-D7; header PHASE D) — the power hold. Main thread, every frame only a
        // frame compare and (while a loop is held) a list index per loop; the work runs every HoldPollFrames and at rebuilds.
        // ─────────────────────────────────────────────────────────────────────────────────

        /// <summary>Phase D (§3.2 step 3): the poll. Every HoldPollFrames render frames (none while paused: the render clock
        /// is frozen, RenderingSystem.cs:187-190) and at each rebuild: per loop, its census row (either order) and, for a
        /// Complete row with chairs, the guarded power of the row's Base (LiftCensusSystem.ReadLiftPower — THE reader, never a
        /// top station); gate = LiftLoopHold.GateOf(rowKnown, complete, power, IsLoopClosed). Run → Hold freezes the loop at
        /// this frame's clock; Hold → Run re-bases every offset (LiftLoopHold.Rebase: no jump); a held loop whose offsets this
        /// rebuild did not carry re-freezes at this clock first. Aligns m_HoldByLoop with the cache. Returns true when a hold
        /// began, ended or re-froze. Allocation: one LoopHold per pair first seen; strings only when a line is written.</summary>
        private bool PollHolds(double timeS, bool rebuilt)
        {
            if (m_HoldCensus == null)
                m_HoldCensus = World.GetExistingSystemManaged<LiftCensusSystem>();
            LiftCensusSystem census = m_HoldCensus;
            // D5: the census not armed (or not built yet this load) → every loop runs, as before Phase D (fail open). The
            // census arms in game mode only (LiftCensusSystem.OnGameLoadingComplete): in the Editor the gate is off.
            bool ready = m_HoldInGame && census != null && census.IsArmed && census.Builds > 0;
            if (!m_HoldInGame)
            {
                if (m_HoldInEditor && !m_HoldEditorLogged)
                {
                    m_HoldEditorLogged = true;
                    Mod.Log.Info($"{nameof(ChairPinSystem)} [hold]: Editor — the power gate is off (the lift census runs in " +
                                 "game mode only); chairs move as before");
                }
            }
            else if (!ready)
            {
                if (m_HoldCensusWait < HoldCensusGateFrames)
                {
                    m_HoldCensusWait++;
                }
                else if (!m_HoldCensusWarned)
                {
                    m_HoldCensusWarned = true;
                    Mod.Log.Warn($"{nameof(ChairPinSystem)} [hold]: lift census not armed — chairs ignore power this load " +
                                 "(fail open)" + (census == null ? " (LiftCensusSystem is not registered)" : string.Empty));
                }
            }

            // D6: the tram's C11 cadence (TramShuttleSystem.PollPower) plus every rebuild.
            uint frame = m_RenderingSystem.frameIndex;
            if (!rebuilt && m_HoldPollFrame != uint.MaxValue && frame >= m_HoldPollFrame && frame - m_HoldPollFrame < HoldPollFrames)
                return false;
            m_HoldPollFrame = frame;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            m_DiagHoldPolls++;

            if (ready && !m_HoldArmLogged)
            {
                m_HoldArmLogged = true;
                Mod.Log.Info($"{nameof(ChairPinSystem)} [hold] power gate armed: a complete lift without power stops its chairs " +
                             $"where they stand and resumes from there (poll every {HoldPollFrames} frames and at each rebuild; " +
                             $"LiftServedRule.Carries; PowerGatesService={(LiftServedRule.PowerGatesService ? "true" : "false")}; " +
                             "closed: its ski area has no open run, held one game hour — T13)");
            }

            // The census rows by station pair (either order), rebuilt on a census Revision change. A row's Base is the
            // Base-ROLE station (LiftCensus.cs:639-647), the one that carries the ElectricityConsumer; a pair named twice
            // keeps its Complete row.
            if (ready && census.Revision != m_HoldRowsRevision)
            {
                m_HoldRowsRevision = census.Revision;
                census.CopyRows(m_HoldRowScratch);
                m_HoldRows.Clear();
                for (int r = 0; r < m_HoldRowScratch.Count; r++)
                {
                    LiftCensus.Row row = m_HoldRowScratch[r];
                    if (row.Base.IsNull || row.Top.IsNull)
                        continue;
                    Entity b = LiftCensusSystem.Ent(row.Base), t = LiftCensusSystem.Ent(row.Top);
                    (Entity, Entity) rowKey = HoldKey(b, t);
                    if (m_HoldRows.TryGetValue(rowKey, out HoldRow existing) && existing.State == LiftCensus.State.Complete)
                        continue;
                    m_HoldRows[rowKey] = new HoldRow { State = row.State, Base = b };
                }
                m_HoldRowScratch.Clear();
            }

            foreach (LoopHold seen in m_Holds.Values)
                seen.Seen = false;
            m_HoldByLoop.Clear();
            bool changed = false;
            int heldLoops = 0, heldChairs = 0, heldChairsForPower = 0;
            for (int l = 0; l < m_LoopCache.m_Loops.Count; l++)
            {
                LiftLoopCache.Loop loop = m_LoopCache.m_Loops[l];
                (Entity, Entity) key = HoldKey(loop.m_BaseStation, loop.m_TopStation);
                bool isNew = !m_Holds.TryGetValue(key, out LoopHold hold);
                if (isNew)
                {
                    hold = new LoopHold();
                    m_Holds.Add(key, hold);
                }
                hold.Base = loop.m_BaseStation;
                hold.Top = loop.m_TopStation;
                hold.Seen = true;
                m_HoldByLoop.Add(hold);

                // §3.2 step 3, third bullet: a held loop whose offsets this rebuild did not carry (OD-7 seed or grid,
                // BuildLoopCache's record) re-freezes at this frame's clock = the rebuild's seedTimeS (both TimeS() of this
                // update): a seeded loop stays exactly where its chairs stood, a gridded one stands on its grid places.
                if (rebuilt && hold.Held && m_RebuildUncarried.TryGetValue(key, out bool seeded))
                {
                    hold.FrozenTimeS = timeS;
                    hold.PoseDirty = true;
                    hold.TagBounds = true;
                    changed = true;
                    OfferHoldLine(loop, timeS, HoldInv($"Held, re-frozen after a rebuild ") +
                                               (seeded ? "(from where they stood)" : "(on the even spacing)") +
                                               HoldInv($" — chairs stand at phase {FrozenPlace(loop, hold.FrozenTimeS):F1} m"), 2);
                }

                // The census row and, for a Complete row with chairs or a held loop, the power of its BASE station
                // (DECISIONS 171: the top station needs none and is never read).
                bool rowKnown = false, complete = false;
                LiftPower power = LiftPower.Unknown;
                if (ready && m_HoldRows.TryGetValue(key, out HoldRow row))
                {
                    rowKnown = true;
                    complete = row.State == LiftCensus.State.Complete;
                    hold.RowState = row.State;
                    hold.RowBase = row.Base;
                    // Round-4 review (fixer): a HELD loop is read whatever its chair count. BuildLoopCache keeps a loop with 0
                    // chairs (a deletion gap after a Move It or tower edit, or a re-seat); reading Unknown there let GateOf
                    // return Run and log "Held → Running (power back)" with no power back. A running loop with no chairs is
                    // still not read (nothing to stop), so a powered city makes exactly the reads it made before.
                    if (complete && (loop.m_Chairs.Count > 0 || hold.Held))
                    {
                        power = census.ReadLiftPower(row.Base);
                        m_DiagHoldPowerReads++;
                    }
                }
                else
                {
                    hold.RowBase = Entity.Null;
                }
                hold.RowKnown = rowKnown;
                hold.Power = power;
                bool closed = IsLoopClosed(hold);
                LoopGate gate = ready ? LiftLoopHold.GateOf(rowKnown, complete, power, closed) : LoopGate.Run;

                // Skeptic S-1 ("silence must be impossible"): a loop with chairs that the gate cannot judge — no census row
                // of its pair, or a row that is not Complete — runs (D5, fail open); said once per lift per load.
                if (ready && !complete && loop.m_Chairs.Count > 0 && m_HoldFailOpenLogged.Add(key))
                {
                    Mod.Log.Info($"{nameof(ChairPinSystem)} [hold] {DescribeHoldLoop(loop)}: " +
                                 (rowKnown ? $"its census row is {hold.RowState}" : "no census row for this station pair") +
                                 " — its chairs ignore power (fail open; logged once per lift per load)");
                }

                if (gate == LoopGate.Hold && !hold.Held)
                {
                    // Run → Hold: this frame's move pass writes the pose of THIS clock (what a running pass would write)
                    // and tags the chairs once; later frames write nothing.
                    hold.Held = true;
                    hold.ForPower = !LiftServedRule.IsPowered(power);
                    hold.FrozenTimeS = timeS;
                    hold.HeldSinceS = timeS;
                    hold.PoseDirty = true;
                    hold.TagBounds = true;
                    changed = true;
                    string why = hold.ForPower ? "no power" : "closed";
                    OfferHoldLine(loop, timeS, (isNew ? "(new loop)" : "Running") + " → Held (" + why + ")" +
                                               HoldInv($" — chairs stop at phase {FrozenPlace(loop, hold.FrozenTimeS):F1} m"), 0);
                }
                else if (gate == LoopGate.Run && hold.Held)
                {
                    // Hold → Run: re-base every offset so (offset' + speed × now) % L = the frozen place (LH-1: ~1e-9 m);
                    // the move pass below then finds every chair in place and the next frame advances by speed × Δt.
                    for (int i = 0; i < loop.m_Offsets.Count; i++)
                        loop.m_Offsets[i] = LiftLoopHold.Rebase(loop.m_Offsets[i], loop.m_SpeedMps, hold.FrozenTimeS, timeS,
                                                                loop.m_Length);
                    double place = FrozenPlace(loop, hold.FrozenTimeS);
                    hold.Held = false;
                    hold.PoseDirty = false;
                    hold.TagBounds = false;
                    changed = true;
                    string why = !ready ? "lift census not armed: fail open"
                        : !rowKnown ? "no census row: fail open"
                        : !complete ? "census " + hold.RowState + ": fail open"
                        : hold.ForPower ? "power back"
                        : "open again";
                    OfferHoldLine(loop, timeS, "Held → Running (" + why + ")" +
                                               HoldInv($" — chairs resume from phase {place:F1} m after {timeS - hold.HeldSinceS:F0} s held"), 1);
                }
                if (hold.Held)
                {
                    // T116: the reason follows the gate while the loop stays held (power back on a still-closed lift
                    // reads "closed"; the resume line then says "open again"); only a read value moves it.
                    if (gate == LoopGate.Hold && power != LiftPower.Unknown)
                        hold.ForPower = !LiftServedRule.IsPowered(power);
                    heldLoops++;
                    heldChairs += loop.m_Chairs.Count;
                    if (hold.ForPower)
                        heldChairsForPower += loop.m_Chairs.Count;
                }
            }

            // Pairs with no loop in the cache any more: dropped (the census lines and the static pin explain the lift).
            if (m_Holds.Count > m_HoldByLoop.Count)
            {
                m_HoldDropScratch.Clear();
                foreach (KeyValuePair<(Entity, Entity), LoopHold> kv in m_Holds)
                {
                    if (!kv.Value.Seen)
                        m_HoldDropScratch.Add(kv.Key);
                }
                for (int d = 0; d < m_HoldDropScratch.Count; d++)
                {
                    if (m_Holds.TryGetValue(m_HoldDropScratch[d], out LoopHold gone) && gone.Held)
                        m_HoldLastChange = HoldInv($"base {EntInv(gone.Base)} → top {EntInv(gone.Top)}: Held → dropped (no loop) at clock {timeS:F1} s");
                    m_Holds.Remove(m_HoldDropScratch[d]);
                    m_DiagHoldDropped++;
                }
                m_HoldDropScratch.Clear();
            }

            m_HeldLoopCount = heldLoops;
            // T116: the suffix names the reason (LiftLoopHold.HeldSuffix; was "; n held (no power)" for every hold).
            if (heldChairs != m_HeldChairCount || heldChairsForPower != m_HeldChairsForPower)
                m_HeldExitSuffix = heldChairs > 0
                    ? "; " + LiftLoopHold.HeldSuffix(heldChairsForPower, heldChairs - heldChairsForPower).Substring(2)
                    : string.Empty;
            m_HeldChairCount = heldChairs;
            m_HeldChairsForPower = heldChairsForPower;
            m_DiagHoldPollMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (changed)
                m_DiagHoldChanges++;
            return changed;
        }

        /// <summary>T13 (report item 78; was D7, "closed" not wired, always false): the loop's lift is closed when its ski area has
        /// no open run — SkiLiftChargeSystem.IsLiftClosed (LiftClosedRule: the state the 25 % billing reads, held one game hour
        /// before it flips). A loop with no census row is never closed (fail open, like GateOf); neither is a lift with no ski
        /// area. LiftLoopHold.GateOf and the [state] lines take it from here.</summary>
        private bool IsLoopClosed(LoopHold hold)
        {
            if (!hold.RowKnown)
                return false;
            if (m_HoldCharge == null)
                m_HoldCharge = World.GetExistingSystemManaged<SkiLiftChargeSystem>();
            return m_HoldCharge != null && m_HoldCharge.IsLiftClosed(hold.Base, hold.Top);
        }

        private SkiLiftChargeSystem m_HoldCharge;   // T13: resolved lazily (GetExistingSystemManaged, never created here)

        /// <summary>Phase D: a station pair's key, the two stations ordered by (Index, Version) — the same key either way
        /// round (a loop's order is geometry, a census row's is role).</summary>
        private static (Entity, Entity) HoldKey(Entity a, Entity b)
        {
            bool aFirst = a.Index < b.Index || (a.Index == b.Index && a.Version <= b.Version);
            return aFirst ? (a, b) : (b, a);
        }

        /// <summary>Phase D: loops of the cache just built whose pair is held as of the last poll (the rebuild signature).</summary>
        private int CountHeldLoops(out int heldForPower)
        {
            heldForPower = 0;
            if (m_HeldLoopCount == 0)
                return 0;
            int held = 0;
            for (int l = 0; l < m_LoopCache.m_Loops.Count; l++)
            {
                LiftLoopCache.Loop loop = m_LoopCache.m_Loops[l];
                if (m_Holds.TryGetValue(HoldKey(loop.m_BaseStation, loop.m_TopStation), out LoopHold hold) && hold.Held)
                {
                    held++;
                    if (hold.ForPower)
                        heldForPower++;   // T116: the rest are held closed (no open run, T13)
                }
            }
            return held;
        }

        /// <summary>Phase D: where a frozen loop's offset 0 stands, in [0, L): FrozenPhase(speed, frozen) mod L (the
        /// [state] lines' "phase"; equal in the hold and the resume line of one episode).</summary>
        private static double FrozenPlace(LiftLoopCache.Loop loop, double frozenTimeS)
        {
            if (!(loop.m_Length > 0.0))
                return 0.0;
            double p = LiftLoopHold.FrozenPhase(loop.m_SpeedMps, frozenTimeS) % loop.m_Length;
            return p < 0.0 ? p + loop.m_Length : p;
        }

        /// <summary>Phase D: "base 119459:77 → top 119398:67 (fixedGrip, 812 m loop, 40 chair(s))" (invariant culture).</summary>
        private static string DescribeHoldLoop(LiftLoopCache.Loop loop)
        {
            LiftTypeDef def = SkiCatalog.LiftByIndex(loop.m_TypeIndex);
            return HoldInv($"base {EntInv(loop.m_BaseStation)} → top {EntInv(loop.m_TopStation)} ({(def != null ? def.Id : "mixed lift types")}, ") +
                   HoldInv($"{loop.m_Length:F0} m loop, {loop.m_Chairs.Count} chair(s))");
        }

        /// <summary>Phase D (§4.2): one "[state]" transition through m_HoldLog (LogWindow 10 s): Plain → the line now;
        /// Summary → the fold line; Fold → counted (slot 0 = to held, 1 = to running, 2 = re-frozen).</summary>
        private void OfferHoldLine(LiftLoopCache.Loop loop, double timeS, string change, int slot)
        {
            string text = DescribeHoldLoop(loop) + ": " + change;
            m_HoldLastChange = text + HoldInv($" (clock {timeS:F1} s)");
            float now = UnityEngine.Time.realtimeSinceStartup;
            switch (m_HoldLog.Offer(now, false, text, slot == 0 ? 1.0 : 0.0, slot == 1 ? 1.0 : 0.0, slot == 2 ? 1.0 : 0.0))
            {
                case LogWindow.Verdict.Plain:
                    Mod.Log.Info($"{nameof(ChairPinSystem)} [state] " + text);
                    break;
                case LogWindow.Verdict.Summary:
                    WriteHoldSummary(now);
                    break;
            }
        }

        /// <summary>Phase D (§4.2): the fold line of m_HoldLog; restarts its window.</summary>
        private void WriteHoldSummary(float now)
        {
            Mod.Log.Info(HoldInv($"{nameof(ChairPinSystem)} [state] {m_HoldLog.Count} more change(s) in {m_HoldLog.SpanS(now):F0} s, ") +
                         HoldInv($"folded (one line per {m_HoldLog.WindowS:0} s): {m_HoldLog.Sum(0):F0} to held, {m_HoldLog.Sum(1):F0} to running") +
                         (m_HoldLog.Sum(2) > 0.0 ? HoldInv($", {m_HoldLog.Sum(2):F0} re-frozen") : string.Empty) +
                         " — last: " + m_HoldLog.LastText);
            m_HoldLog.Wrote(now);
        }

        private static string EntInv(Entity e) => HoldInv($"{e.Index}:{e.Version}");
        private static string HoldInv(FormattableString text) => FormattableString.Invariant(text);
    }
}
