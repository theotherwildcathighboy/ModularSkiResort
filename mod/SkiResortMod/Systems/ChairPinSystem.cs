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
// MILESTONE 2 (scaffolded, default OFF — see MovingChairs below): advance chairs along the
// cable curve. The pinning helper already takes an "advance" arc-length parameter, so turning
// the dream on is: flip the flag, refine timing/interpolation. NOT shipped: no InterpolatedTransform
// smoothing, no cross-edge looping (see the scaffold comment).
//
// ─────────────────────────────────────────────────────────────────────────────────────────
// HOW A CHAIR IS PINNED (all game types verified against the decompiled Game.dll, fullsrc)
//
//   1. Chairs are found generically: Secondary + Owner + PrefabRef + Transform, excluding
//      Deleted/Temp, with PrefabRef matched against the chair prefab set resolved ONCE by name
//      (ChairPrefabNames). Temp (tool-preview) chairs are left to vanilla placement on purpose.
//   2. Owner is the lift EDGE entity; its Game.Net.SubLane buffer holds the lanes. Cable lanes
//      are recognised structurally, not by name: lane PrefabRef → UtilityLaneData with the
//      HighVoltageLine flag (0x40). This skips the LV comm lane (LowVoltageLine, x 0) and works
//      unchanged if the HV lane is ever swapped for a custom haul-rope clone that keeps the
//      utility class (lane-chairs.md "custom haul-rope re-skin").
//   3. SIDE matching: the two cable lanes run at x ±1.8, the chair hangs at x ±1.8 — the chair's
//      own-side lane is the one directly above it (~2.4 m), the far lane is ≥ 3.6 m away
//      laterally. Nearest-curve-distance (MathUtils.Distance(bezier, pos, out t), the projection
//      also yielding the curve parameter t) therefore IS the "chair local x sign ↔ lane x sign"
//      match, with no reliance on local-space bookkeeping.
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

        // ── Milestone 2 scaffolding (default OFF) ────────────────────────────────────────
        // When true, OnUpdate advances every chair along its cable lane each frame instead of
        // waiting for change events. This is the moving-chairs dream in embryo: the shared
        // pinning helper takes an arc-length advance, so the motion path is already the same
        // code as static pinning. Held deliberately crude until it becomes a real milestone:
        //   * timing uses UnityEngine.Time.deltaTime (moves even while paused);
        //   * no InterpolatedTransform/TransformFrame smoothing;
        //   * a chair reaching the end of its lane HOLDS there (at the end clamp) — advancing
        //     across edges (and around station bullwheels) is explicitly OUT of scope here.
        // Promote to a SkiResortSettings toggle when milestone 2 ships for real.
        private static readonly bool MovingChairs = false;
        private const float ChairSpeedMetersPerSecond = 2.5f;

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
            m_UpdatedChairQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Secondary>(),
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
                ComponentType.ReadOnly<Secondary>(),
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
                ComponentType.ReadOnly<Secondary>(),
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

            Mod.Log.Info($"{nameof(ChairPinSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (mode != GameMode.Game && mode != GameMode.Editor)
                return;

            ResolveChairPrefabs();
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
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || m_ChairPrefabs.Count == 0)
                    return; // error-tripped this load, or chair prefab missing — logged already

                if (MovingChairs)
                {
                    // Milestone 2: advance every chair along its lane, every frame.
                    PinChairs(m_AllChairQuery, ChairSpeedMetersPerSecond * UnityEngine.Time.deltaTime,
                        tagUpdated: true, logLabel: null, out _);
                    return;
                }

                if (m_FullPassPending)
                {
                    // v1.3: a reconnect-requested pass can land in the deletion gap (old chairs
                    // gone, replacements not yet spawned) — stay armed until chairs appear or
                    // the retry window closes. The load pass keeps consume-immediately semantics
                    // (it must log its summary exactly once, even on lift-free maps).
                    int processed = PinChairs(m_AllChairQuery, 0f, tagUpdated: true,
                        logLabel: m_PendingPassLabel, out _);
                    if (processed > 0 || m_PendingPassLabel == "load pass" || --m_FullPassRetries <= 0)
                    {
                        m_FullPassPending = false;
                        if (processed == 0 && m_PendingPassLabel != "load pass")
                            Mod.Log.Info($"{nameof(ChairPinSystem)} [{m_PendingPassLabel}]: no chairs " +
                                         "appeared within the retry window — nothing to re-pin");
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
                                     $"0 of {m_UpdatedChairQuery.CalculateEntityCount()} updated secondary " +
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

            // Nearest HV cable lane = the chair's own-side lane (header note 3).
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

        /// <summary>Cross-system request (used by LiftReconnectSystem after it rebuilds
        /// lanes): re-pin every chair on the next update. Chairs are Secondary objects and
        /// appear in no SubObject buffer (SubObjectReferencesSystem excludes Secondary), so
        /// event systems cannot tag them directly — a full idempotent pass is the reliable
        /// path. Same-frame when requested before Modification4B (our phase).</summary>
        internal void RequestFullPass(string label)
        {
            m_FullPassPending = true;
            m_PendingPassLabel = label;
            m_FullPassRetries = FullPassRetryFrames; // v1.3: survive the chair deletion gap
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
