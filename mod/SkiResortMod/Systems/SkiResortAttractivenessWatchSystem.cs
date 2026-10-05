// SkiResortAttractivenessWatchSystem — tells SkiResortAttractivenessSystem that a lodge, a lift station,
// a Welcome Sign or a ski run of ours was placed, changed or removed, so the ski area scores (and the
// hub's Attraction tile) follow a placement on that system's next 16-frame tick instead of its
// 4096-frame backstop (Round F6, item D1; docs/research/field/2026-10-03b/findings/attraction-honesty.md
// D4 and its Verification, correction 2). Created 2026-10-03 (Round F6, implementer D), the
// SkiRunBindTriggerSystem pattern. Widened 2026-10-04 (Round E1, stream S1; econ-round spec ch1 §10.2):
// Welcome Signs join the building set, the run set is the ownership registry (all four kinds, natural
// included), and the request is STRUCTURAL (RecomputePending now means "a structural recompute is
// pending", so a placement is never held behind a pending soft one).
//
// WHY A SEPARATE SYSTEM (decompile 1.6.0f1; 1.6.2 lines in brackets). GameManager.Update runs MainLoop
// through UpdateWorld — ToolSystem, then ModificationSystem (SystemOrder.cs:58, 60) — and then the
// Cleanup phase through PostUpdateWorld (GameManager.cs:709 → :2391, :711 → :2399 [:719 → :2406,
// :721 → :2414]). CleanUpSystem (Cleanup, SystemOrder.cs:54) destroys the frame's Deleted entities and
// strips Created / Updated / Applied (CleanUpSystem.cs:24-32, :52-53). GameSimulation runs only later in
// the frame: GameManager.LateUpdate → LateUpdateWorld → the LateUpdate phase → SimulationSystem
// (GameManager.cs:721 → :2407 [:731 → :2422]; SystemOrder.cs:74) → GameSimulation (SimulationSystem.cs:286,
// same in 1.6.2). So a tool-made tag never reaches a GameSimulation system: the Created/Updated check
// SkiResortAttractivenessSystem made on its own tick was dead code (field test 6: every lodge recompute
// fell on the timer or a load, none on a placement — the tile lagged a new run by about a minute).
// SkiRunBindTriggerSystem's header records the same fact ("the GameSimulation sweep never sees it").
//
// WHY ModificationEnd (registration in Mod.cs). ModificationSystem runs ModificationEnd last
// (ModificationSystem.cs:19-26) inside MainLoop, paused or not, before Cleanup. A plain UpdateAt sorts
// after every vanilla UpdateAt of the phase (mods register after SystemOrder.Initialize; add-index order,
// UpdateSystem.cs:39-47, 158-161), so this frame's tool output (applied through the ToolOutputBarrier
// inside ToolUpdate, before ModificationSystem) and every Modification phase are in, and Deleted entities
// still exist. No update interval: ModificationSystem calls the non-indexed UpdateSystem.Update(phase),
// which ignores it (UpdateSystem.cs:183-221).
//
// WHAT IT WATCHES. Two queries, Any {Created, Updated, Deleted} None {Temp}: buildings {Building,
// PrefabRef} and areas {Area, PrefabRef}. Removals count too: a removed run, station, lodge or sign changes
// a ski area's score as much as a new one. Both match vanilla entities, so the result is filtered in
// managed code against the attractiveness system's prefab sets (IsWatchedBuildingPrefab = lodge ∪ lift
// station ∪ Welcome Sign; IsWatchedAreaPrefab = SkiRunOwnershipSystem.IsRunPrefab — one source of truth).
// IsEmptyIgnoreFilter keeps idle frames free of any job sync; on an event frame the only sync is
// PrefabRef's writers (ToComponentDataArray — the bind trigger's AnyRunPrefab contract; no query-wide
// CompleteDependency). Nothing is scanned while a structural recompute is already pending.
//
// WHAT IT DOES: SkiResortAttractivenessSystem.RequestRecompute(reason) — a structural request (a flag and
// a reason), nothing else. The recompute runs on that system's next 16-frame tick in GameSimulation, so
// not while the game is paused (the tile follows once the game runs again). Bindings, the lift census,
// ratings, run open states, settings, sign efficiency, lodge on/off and lift power raise no tag; the
// attractiveness system compares them on its own tick (ch1 §10.1).
//
// LOGS: "created"; one ARMED line per load (ch1 §10.2 text); the first trigger of each reason per load is
// logged by the attractiveness system's RequestRecompute ("first this load; later triggers are counted"
// — the count is in the hub DUMP's attractiveness line); a one-shot "!!!" that disables the watcher until
// the next load (the attractiveness system's tick and its 4096-frame backstop still refresh the scores).
// Game mode only; GameManager.isGameLoading guard.

using System;
using Colossal.Serialization.Entities;
using Game;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiResortAttractivenessWatchSystem : GameSystemBase
    {
        // Reason texts (ch1 §10.1 / §10.2, exact).
        internal const string ReasonBuildings = "lodge, lift station or Welcome Sign placed, moved or removed";
        internal const string ReasonRuns = "ski run drawn, edited or removed";
        internal const string ReasonBoth = "buildings and ski runs changed";

        private SkiResortAttractivenessSystem m_Attractiveness;
        private EntityQuery m_BuildingEvents;
        private EntityQuery m_AreaEvents;

        // Per-load state — reset at OnGameLoadingComplete.
        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;

        /// <summary>RequestRecompute calls made this load (DUMP).</summary>
        public int Triggers;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Attractiveness = World.GetOrCreateSystemManaged<SkiResortAttractivenessSystem>();
            m_BuildingEvents = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Buildings.Building>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                Any = new[]
                {
                    ComponentType.ReadOnly<Created>(),
                    ComponentType.ReadOnly<Updated>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            m_AreaEvents = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                Any = new[]
                {
                    ComponentType.ReadOnly<Created>(),
                    ComponentType.ReadOnly<Updated>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            Mod.Log.Info($"{nameof(SkiResortAttractivenessWatchSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_Disabled = false;
            m_ErrorLogged = false;
            Triggers = 0;
            if (m_Armed)
                Mod.Log.Info("Attractiveness [watch]: ARMED — ModificationEnd every frame (paused included): a lodge, lift station, " +
                             "Welcome Sign or ski run of ours Created, Updated or Deleted asks the attractiveness system to recompute " +
                             "the ski area scores on its next 16-frame tick (GameSimulation: not while paused); bindings, the lift " +
                             "census, ratings, run open states, settings, sign efficiency, lodge on/off and lift power are compared on " +
                             "that tick; first trigger per load logged, the rest counted (hub DUMP)");
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !m_Armed)
                    return;
                // Cheapest first: field reads, then IsEmptyIgnoreFilter (syncs nothing). RecomputePending =
                // a STRUCTURAL recompute is pending (a pending soft one does not stop a placement).
                if (!m_Attractiveness.IsActive || m_Attractiveness.RecomputePending)
                    return;
                bool buildingEvents = !m_BuildingEvents.IsEmptyIgnoreFilter;
                bool areaEvents = !m_AreaEvents.IsEmptyIgnoreFilter;
                if (!buildingEvents && !areaEvents)
                    return;
                GameManager gm = GameManager.instance;
                if (gm == null || gm.isGameLoading)
                    return;

                bool buildings = buildingEvents && AnyWatched(m_BuildingEvents, areas: false);
                bool runs = areaEvents && AnyWatched(m_AreaEvents, areas: true);
                if (!buildings && !runs)
                    return;

                string reason = buildings ? (runs ? ReasonBoth : ReasonBuildings) : ReasonRuns;
                Triggers++;
                // The first request of each reason per load is logged there (ch1 §13), so not here.
                m_Attractiveness.RequestRecompute(reason);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiResortAttractivenessWatchSystem)} EXCEPTION — ski area scores no longer " +
                                  "follow placements until the next game load (the attractiveness system's own triggers and its " +
                                  "4096-frame backstop still run):\n" + e);
                }
            }
        }

        /// <summary>Managed PrefabRef scan of an event query's small result against the attractiveness
        /// system's sets. ToComponentDataArray&lt;PrefabRef&gt; completes PrefabRef's write dependency
        /// itself (the bind trigger's AnyRunPrefab note); no query-wide CompleteDependency.</summary>
        private bool AnyWatched(EntityQuery query, bool areas)
        {
            using (var prefabRefs = query.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < prefabRefs.Length; i++)
                {
                    Entity prefab = prefabRefs[i].m_Prefab;
                    if (areas ? m_Attractiveness.IsWatchedAreaPrefab(prefab) : m_Attractiveness.IsWatchedBuildingPrefab(prefab))
                        return true;
                }
            }
            return false;
        }
    }
}
