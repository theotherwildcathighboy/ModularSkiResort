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
// ─────────────────────────────────────────────────────────────────────────────────────────
// HOW A CHAIR IS PINNED (all game types verified against the decompiled Game.dll, fullsrc)
//
//   1. Chairs are found generically: Owner + PrefabRef + Transform, excluding Deleted/Temp,
//      with PrefabRef matched against the chair prefab set resolved ONCE by name
//      (ChairPrefabNames). NOT Secondary — see the milestone-2 discovery correction above.
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
// untouched; a missing chair prefab is logged once and disables the system for the session.
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
        // Extend this array when new chair generations ship (PIPELINE §0: every generation gets
        // a new letter/name). Lookup is by prefab NAME through PrefabSystem, resolved once per
        // session; a missing name is tolerated (logged once, never a crash).
        private static readonly string[] ChairPrefabNames = { "SkiLiftChair4" };

        // ── Milestone 2: moving chairs (kill-switch) ─────────────────────────────────────
        // true = chairs ride the cable loop (see the milestone-2 header block). false = the
        // milestone-1 static flow, untouched, wholesale. Promote to a SkiResortSettings
        // toggle for shipping. Not readonly since entry 101: the Ski Resort developer-menu
        // tab flips it at runtime for A/B checks (a flip requests a full pass).
        private static bool MovingChairs = true;
        private const float ChairSpeedMetersPerSecond = 2.5f;

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
        private const float ApproachPinDistanceCeiling = 4.0f;

        // Reconnect full passes can fire into the frame gap where vanilla has deleted the
        // old chairs but not yet respawned replacements (the Aug 11 field log: 26 requests,
        // zero chairs matched, silent pass). Keep the pass armed until chairs appear or the
        // retry window closes.
        private const int FullPassRetryFrames = 30;

        private PrefabSystem m_PrefabSystem;

        /// <summary>Chair prefab entity → pivot-to-grip-top offset (bounds.max.y), resolved once.</summary>
        private readonly Dictionary<Entity, float> m_ChairPrefabs = new Dictionary<Entity, float>();

        private EntityQuery m_UpdatedChairQuery; // chairs changed this frame (creation + reposition)
        private EntityQuery m_AllChairQuery;     // every live chair (load pass, watchdog, moving mode)
        private EntityQuery m_TempChairQuery;    // tool-preview chairs (pinned silently, v1.2.1)
        private EntityQuery m_PrefabScanQuery;   // fallback prefab-by-name resolution
        private EntityQuery m_NetPrefabScanQuery; // fallback net-prefab-by-name resolution (no geometry)
        private EntityQuery m_LaneQuery;         // moving mode: every live net lane (cache harvest)
        private EntityQuery m_StationQuery;      // moving mode: every live building (station scan)

        // ── Moving-mode state (all transient — nothing here is ever serialized) ──────────
        private Game.Rendering.RenderingSystem m_RenderingSystem;
        private readonly LiftLoopCache m_LoopCache = new LiftLoopCache();
        private Entity m_ChairliftPrefab = Entity.Null; // SkiChairliftHV net prefab entity
        private bool m_ChairliftPrefabResolved;
        private bool m_LoopCacheDirty = true;
        private int m_RebuildCooldown;
        private int m_EmptyRosterRetries;
        private double m_LastMovePhase = -1.0;     // metres; -1 forces the first mover pass
        private double m_LastBoundsPhase;          // metres at the last bounds-refresh burst
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
        private string m_DiagLastExit = "never ran"; // which branch the last OnUpdate took
        private string m_LastError;                // message + stack of the tripping exception
        private int m_DiagProbeUpdatedChairs;      // chairs matched by the change probe, last pass
        private double m_DiagLastPhase;            // derived clock, last mover pass (metres)
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
        // Source of truth: assets/blender/lift_terminal.py — build_terminal_pali (drive):
        // wheel_x, wheel_z = 0.2, 3.5 "bullwheel centre - loading height (INVARIANT)";
        // build_return_simple (return): wheel_z = 3.7 at the origin; rope over the wheel is
        // wheel_z + 0.05; wheel radius 1.8 = half the 3.6 m rope gauge. Authoring frame
        // (+X travel, Y lateral, Z up) → prefab-local (X lateral, Y up, Z travel)
        // (cs2_asset.py rotate-then-bake), so authoring (x, y, z) lands at (y, z, x).
        // Add a line here when a new terminal generation ships; unknown names fall back
        // to the plain terminal semicircle (LiftLoopCache.AppendTerminalTurn).
        private static readonly Dictionary<string, float3> StationWheelLocal = new Dictionary<string, float3>
        {
            { "SkiBaseStationBuilding", new float3(0f, 3.55f, 0.2f) },
            { "SkiTopStationBuilding", new float3(0f, 3.75f, 0f) },
        };

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
        private bool m_PrefabsResolved;    // resolution attempted (once per app run)
        private bool m_FullPassPending;    // set on load or by RequestFullPass, consumed next update
        private int m_FullPassRetries;     // v1.3: frames a non-load pass stays armed while zero chairs match
        private string m_PendingPassLabel = "load pass"; // log label of the pending full pass
        private bool m_Disabled;           // tripped by the catch; re-armed per load
        private bool m_ErrorLogged;        // one error with stack per load
        private bool m_UpdateAliveLogged;  // one-time "[update] pass alive"
        private bool m_ZeroMatchLogged;    // one-time zero-prefab-match diagnostic
        private int m_WatchdogCountdown = WatchdogIntervalFrames;
        private const int WatchdogIntervalFrames = 4096; // ~68 s of frames at 60 fps

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();

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

            // Prefab entities carry PrefabData; ObjectGeometryData narrows the scan to object
            // prefabs. Only used if the fast PrefabID lookup misses (see ResolveChairPrefabs).
            m_PrefabScanQuery = GetEntityQuery(
                ComponentType.ReadOnly<PrefabData>(),
                ComponentType.ReadOnly<ObjectGeometryData>());

            // Net prefabs have no ObjectGeometryData — the chairlift-prefab fallback scan
            // needs the unnarrowed version.
            m_NetPrefabScanQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());

            // Moving mode: cable-lane harvest (Owner filter to the chairlift net happens in
            // the build pass — lanes hang off both edges AND nodes of the drawn line) and
            // station scan (prefab filter in the pass; prefab set shared with PowerBridge).
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

            ResolveChairPrefabs();
            ResolveChairliftPrefab();
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
            m_LoopCacheDirty = true;
            m_RebuildCooldown = 0;
            m_EmptyRosterRetries = 0;
            m_LastMovePhase = -1.0;
            m_LastBoundsPhase = 0.0;
            m_MovingAliveLogged = false;
            m_LoopWarnLogged = false;
            m_LastBuildHadLanes = true;  // assume lanes until the first rebuild proves otherwise
            m_LastBuildSignature = null; // first rebuild per load always logs

            // Entry 101: developer-tab counters are per load.
            m_DiagUpdateCalls = 0;
            m_DiagLastUpdateFrame = -1;
            m_DiagLastExit = "load complete, no update yet";
            m_LastError = null;
            m_DiagProbeUpdatedChairs = 0;
            m_DiagLastPhase = 0.0;
            m_DiagLastMoved = m_DiagLastInPlace = m_DiagLastMissing = m_DiagLastRiding = 0;
            m_DiagPassesWithMoves = 0;
            m_DiagPausedSkips = 0;
            m_DiagRebuilds = 0;
            m_DiagLastRebuildFrame = -1;
            m_DiagLaneTotal = m_DiagLanesMatched = m_DiagStations = m_DiagChairsMatched = m_DiagEnrolled = 0;
        }

        protected override void OnUpdate()
        {
            m_DiagUpdateCalls++;
            m_DiagLastUpdateFrame = m_RenderingSystem.frameIndex;
            try
            {
                if (m_Disabled || m_ChairPrefabs.Count == 0)
                {
                    // error-tripped this load, or chair prefab missing — logged already
                    m_DiagLastExit = m_Disabled ? "disabled by exception" : "no chair prefab resolved";
                    return;
                }

                if (MovingChairs && m_ChairliftPrefab != Entity.Null)
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
                        if (m_ChairPrefabs.ContainsKey(prefabRefs[i].m_Prefab))
                            chairs.Add(entities[i]);
                    }
                }

                processed = chairs.Length;
                for (int i = 0; i < chairs.Length; i++)
                {
                    var chair = chairs[i];
                    float gripTop = m_ChairPrefabs[EntityManager.GetComponentData<PrefabRef>(chair).m_Prefab];
                    switch (TryPinChair(chair, gripTop, advanceDistance, tagUpdated))
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

        private PinResult TryPinChair(Entity chair, float gripTop, float advanceDistance, bool tagUpdated)
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
            float distanceCeiling = isApproach ? ApproachPinDistanceCeiling
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
            if (m_LoopCacheDirty && m_RebuildCooldown <= 0)
            {
                // Debounce guards the reconnect poke storms (DECISIONS 40): at most one
                // rebuild per window, dirty flag re-set by the triggers if needed.
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
            // speed * time at metre scale.
            double phase = ChairSpeedMetersPerSecond *
                (((double)m_RenderingSystem.frameIndex + m_RenderingSystem.frameTime) / 60.0);
            if (phase == m_LastMovePhase && !rebuilt)
            {
                // paused — the derived positions are identical, write nothing
                m_DiagPausedSkips++;
                m_DiagLastExit = "moving: phase unchanged (paused)";
                return;
            }
            m_LastMovePhase = phase;
            m_DiagLastPhase = phase;

            int moved = 0, inPlace = 0, missing = 0, riding = 0;
            for (int l = 0; l < m_LoopCache.m_Loops.Count; l++)
            {
                LiftLoopCache.Loop loop = m_LoopCache.m_Loops[l];
                riding += loop.m_Chairs.Count;
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
            if (missing > 0)
                m_LoopCacheDirty = true; // roster chair died mid-window — heal next rebuild

            m_DiagLastMoved = moved;
            m_DiagLastInPlace = inPlace;
            m_DiagLastMissing = missing;
            m_DiagLastRiding = riding;
            if (moved > 0)
                m_DiagPassesWithMoves++;
            m_DiagLastExit = riding == 0
                ? "moving: empty roster (no closed loop has chairs)"
                : $"moving: {moved} moved / {inPlace} in place / {missing} missing of {riding}";

            if (moved > 0 && !m_MovingAliveLogged)
            {
                m_MovingAliveLogged = true;
                Mod.Log.Info($"{nameof(ChairPinSystem)} [moving] pass alive — {riding} chair(s) riding " +
                             $"{m_LoopCache.m_Loops.Count} loop(s), {m_StaticFallbackChairs} static-fallback");
            }

            // Bounds refresh: CullingInfo + the static search tree only re-read Transform on
            // Updated — tag the whole roster every BoundsRefreshMeters of travel (~2 m lag
            // ceiling at any sim speed; suppressed entirely while paused because phase
            // freezes). CleanUpSystem strips the tags at end of frame.
            if (phase - m_LastBoundsPhase >= BoundsRefreshMeters)
            {
                m_LastBoundsPhase = phase;
                for (int l = 0; l < m_LoopCache.m_Loops.Count; l++)
                {
                    LiftLoopCache.Loop loop = m_LoopCache.m_Loops[l];
                    for (int i = 0; i < loop.m_Chairs.Count; i++)
                    {
                        Entity chair = loop.m_Chairs[i];
                        if (EntityManager.Exists(chair) &&
                            !EntityManager.HasComponent<Deleted>(chair) &&
                            !EntityManager.HasComponent<Updated>(chair) &&
                            !EntityManager.HasComponent<Created>(chair))
                        {
                            EntityManager.AddComponent<Updated>(chair);
                        }
                    }
                }
            }
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
            var chunks = query.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    for (int i = 0; i < prefabRefs.Length; i++)
                    {
                        if (m_ChairPrefabs.ContainsKey(prefabRefs[i].m_Prefab))
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

            // 1. Station entities (prefab set shared with PowerBridgeSystem — one source
            //    of truth for what counts as a station).
            var stationPrefabs = World.GetOrCreateSystemManaged<PowerBridgeSystem>().GetResolvedStationPrefabs();
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
                        if (!stationPrefabs.Contains(prefabRefs[i].m_Prefab))
                            continue;
                        stations.Add(entities[i]);
                        stationPositions.Add(transforms[i].m_Position);

                        // Bullwheel centre (entry 103): rope height over the wheel, in
                        // station-local space, from the terminal generator's INVARIANT wheel
                        // placement (lift_terminal.py: drive wheel_x/z 0.2/3.5, return 0/3.7,
                        // rope = wheel + 0.05; authoring +X = travel maps to prefab +Z).
                        string name = m_PrefabSystem.GetPrefabName(prefabRefs[i].m_Prefab);
                        float3 wheel = StationWheelLocal.TryGetValue(name, out float3 local)
                            ? ObjectUtils.LocalToWorld(transforms[i], local)
                            : new float3(float.NaN);
                        stationWheels.Add(wheel);
                        m_DiagStationLines.Add(
                            $"station {stations.Count - 1}: {name} {entities[i].Index}:{entities[i].Version} " +
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
            var lanes = new List<LiftLoopCache.CableLane>();
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
                    for (int i = 0; i < entities.Length; i++)
                    {
                        Entity ownerEntity = owners[i].m_Owner;
                        if (ownerEntity == Entity.Null ||
                            !EntityManager.TryGetComponent<PrefabRef>(ownerEntity, out var ownerPrefab) ||
                            ownerPrefab.m_Prefab != m_ChairliftPrefab)
                            continue;
                        if (!EntityManager.TryGetComponent<UtilityLaneData>(prefabRefs[i].m_Prefab, out var utility) ||
                            (utility.m_UtilityTypes & UtilityTypes.HighVoltageLine) == 0)
                            continue;
                        lanes.Add(new LiftLoopCache.CableLane
                        {
                            m_Lane = entities[i],
                            m_Owner = ownerEntity,
                            m_Bezier = curves[i].m_Bezier,
                            m_Start = laneData[i].m_StartNode,
                            m_End = laneData[i].m_EndNode,
                            m_Length = curves[i].m_Length,
                        });
                    }
                }
            }
            finally
            {
                chunks.Dispose();
            }

            m_LastBuildHadLanes = lanes.Count > 0;

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
                        if (m_ChairPrefabs.ContainsKey(prefabRefs[i].m_Prefab))
                            chairs.Add(entities[i]);
                    }
                }

                m_DiagChairsMatched = chairs.Length;
                for (int i = 0; i < chairs.Length; i++)
                {
                    Entity chair = chairs[i];
                    float gripTop = m_ChairPrefabs[EntityManager.GetComponentData<PrefabRef>(chair).m_Prefab];
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

                    if (home == null)
                    {
                        // Unclosed lift (or mid-edit state): milestone-1 static pin.
                        TryPinChair(chair, gripTop, 0f, tagUpdated: true);
                        m_StaticFallbackChairs++;
                        continue;
                    }

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

            // 4. Slot assignment (review fix, entry 98): even spacing. An identity rebuild
            //    carries every offset forward BY ENTITY (same chairs, unchanged loop length
            //    → same slots, zero jump — a chair deleted mid-session even leaves an
            //    honest gap). Anything else redistributes on a deterministic entity-index
            //    grid: a one-time visual redistribution, accepted for v1 (the previous
            //    rank-by-projection scheme keyed off spawn-edge geometry that stops meaning
            //    anything once a chair has traveled — it was identity only by accident).
            for (int l = 0; l < m_LoopCache.m_Loops.Count; l++)
            {
                LiftLoopCache.Loop loop = m_LoopCache.m_Loops[l];
                int n = loop.m_Chairs.Count;
                if (n == 0)
                    continue;

                bool carried = true;
                for (int i = 0; i < n; i++)
                {
                    if (!previousSlots.TryGetValue(loop.m_Chairs[i], out PrevSlot prev) ||
                        math.abs(prev.m_LoopLength - loop.m_Length) > 1e-3)
                    {
                        carried = false;
                        break;
                    }
                }
                if (carried)
                {
                    for (int i = 0; i < n; i++)
                        loop.m_Offsets.Add(previousSlots[loop.m_Chairs[i]].m_Offset);
                    continue;
                }

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
            string signature =
                $"{m_LoopCache.m_Loops.Count} loop(s) ({totalLength:F0} m total), {riding} chair(s) riding, " +
                $"{m_StaticFallbackChairs} static-fallback, " +
                $"{m_LoopCache.m_ChainCount} chain(s)/{m_LoopCache.m_UnclosedChainCount} unclosed";
            if (signature != m_LastBuildSignature)
            {
                m_LastBuildSignature = signature;
                Mod.Log.Info($"{nameof(ChairPinSystem)} [moving] cache rebuilt: {signature}" +
                             (enrolled > 0 ? $" ({enrolled} newly enrolled)" : string.Empty));
            }

            if (m_LoopCache.m_UnclosedChainCount > 0 && !m_LoopWarnLogged)
            {
                m_LoopWarnLogged = true;
                Mod.Log.Warn($"{nameof(ChairPinSystem)} [moving]: {m_LoopCache.m_UnclosedChainCount} cable " +
                             "chain(s) did not close into a station-to-station loop — chairs on those " +
                             "lifts are static-pinned (unfinished line, missing station, or unexpected " +
                             "anchor topology; logged once per load)");
            }
        }

        /// <summary>Resolves the chairlift NET prefab entity (moving mode's lane-owner
        /// filter). Same two-tier pattern as the chair prefabs; missing is tolerated —
        /// moving mode simply stays inactive and the static flow keeps running.</summary>
        private void ResolveChairliftPrefab()
        {
            if (m_ChairliftPrefabResolved)
                return;
            m_ChairliftPrefabResolved = true;

            const string name = "SkiChairliftHV";
            if (m_PrefabSystem.TryGetPrefab(new PrefabID(nameof(PowerLinePrefab), name), out PrefabBase prefabBase))
                m_PrefabSystem.TryGetEntity(prefabBase, out m_ChairliftPrefab);

            if (m_ChairliftPrefab == Entity.Null)
            {
                var candidates = m_NetPrefabScanQuery.ToEntityArray(Allocator.Temp);
                try
                {
                    for (int i = 0; i < candidates.Length; i++)
                    {
                        if (m_PrefabSystem.GetPrefabName(candidates[i]) == name)
                        {
                            m_ChairliftPrefab = candidates[i];
                            break;
                        }
                    }
                }
                finally
                {
                    candidates.Dispose();
                }
            }

            if (m_ChairliftPrefab == Entity.Null)
                Mod.Log.Warn($"{nameof(ChairPinSystem)}: net prefab '{name}' not found — moving " +
                             "chairs inactive this session (static pinning still runs)");
            else
                Mod.Log.Info($"{nameof(ChairPinSystem)}: resolved chairlift net prefab '{name}'");
        }

        /// <summary>Cross-system request (used by LiftReconnectSystem after it rebuilds
        /// lanes): re-pin every chair — and in moving mode rebuild the loop cache — on the
        /// next update. Chairs appear in no SubObject buffer (SubObjectReferencesSystem
        /// keys on buffers chairs are not in), so event systems cannot tag them directly —
        /// a full idempotent pass is the reliable path. Same-frame when requested before
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
        // Prefab resolution — once per session. Fast path: PrefabSystem's PrefabID index
        // (name-only IDs match asset-database prefabs whose asset carries no platform hash).
        // Fallback: linear scan of object-prefab entities compared via GetPrefabName, immune
        // to PrefabID hash semantics for subscribed/UGC content. Missing prefab → log once.
        // ─────────────────────────────────────────────────────────────────────────────────
        private void ResolveChairPrefabs()
        {
            if (m_PrefabsResolved)
                return;
            m_PrefabsResolved = true;

            foreach (string name in ChairPrefabNames)
            {
                Entity prefabEntity = Entity.Null;
                if (m_PrefabSystem.TryGetPrefab(new PrefabID(nameof(StaticObjectPrefab), name), out PrefabBase prefabBase))
                    m_PrefabSystem.TryGetEntity(prefabBase, out prefabEntity);

                if (prefabEntity == Entity.Null)
                {
                    var candidates = m_PrefabScanQuery.ToEntityArray(Allocator.Temp);
                    try
                    {
                        for (int i = 0; i < candidates.Length; i++)
                        {
                            if (m_PrefabSystem.GetPrefabName(candidates[i]) == name)
                            {
                                prefabEntity = candidates[i];
                                break;
                            }
                        }
                    }
                    finally
                    {
                        candidates.Dispose();
                    }
                }

                if (prefabEntity == Entity.Null)
                {
                    Mod.Log.Warn($"{nameof(ChairPinSystem)}: chair prefab '{name}' not found — " +
                                 "chairs of this type will not be pinned (asset pack missing or renamed?)");
                    continue;
                }

                if (!EntityManager.TryGetComponent<ObjectGeometryData>(prefabEntity, out var geometry) ||
                    !(geometry.m_Bounds.max.y > 0f))
                {
                    Mod.Log.Warn($"{nameof(ChairPinSystem)}: chair prefab '{name}' has no usable " +
                                 "ObjectGeometryData bounds — skipped (cannot derive grip-top offset)");
                    continue;
                }

                // bounds.max.y = pivot→grip-top (grip is the topmost geometry by the Blender
                // contract). Read at runtime so future chair meshes re-derive automatically.
                m_ChairPrefabs[prefabEntity] = geometry.m_Bounds.max.y;
                Mod.Log.Info($"{nameof(ChairPinSystem)}: resolved chair prefab '{name}' " +
                             $"(grip-top offset {geometry.m_Bounds.max.y:F3} m from geometry bounds)");
            }

            if (m_ChairPrefabs.Count == 0)
                Mod.Log.Warn($"{nameof(ChairPinSystem)}: no chair prefabs resolved — pinning inactive this session");
        }
    }
}
