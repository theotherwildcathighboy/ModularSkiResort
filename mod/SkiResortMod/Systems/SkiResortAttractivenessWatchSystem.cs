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
// Catalog Phase B (2026-10-11, round-2-contract §7.4): IsWatchedBuildingPrefab adds the ski school and the patrol huts
// (SkiPrefabCatalogSystem.IsUpgradeBuilding), so their placement recomputes the area on the next tick; the reason text
// and the ARMED line name them. Nothing else changes here.
// IsEmptyIgnoreFilter keeps idle frames free of any job sync; on an event frame the only sync is
// PrefabRef's writers (ToComponentDataArray — the bind trigger's AnyRunPrefab contract; no query-wide
// CompleteDependency). Nothing is scanned while a structural recompute is already pending.
//
// WHAT IT DOES: SkiResortAttractivenessSystem.RequestRecompute(reason) — a structural request (a flag and
// a reason). The recompute runs on that system's next 16-frame tick in GameSimulation; T120 (2026-10-20): while
// the game is PAUSED (SimulationSystem.selectedSpeed 0, no GameSimulation frame) this watcher runs it at once through
// RecomputeWhilePaused, so a lodge placed while paused links to its sign and the hub stops asking for one (it used to
// wait for the game to run: 87 wall minutes of "No lodge" in the 2026-10-20 playthrough). Bindings, the lift census,
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
        // Reason texts (ch1 §10.1 / §10.2, exact). Catalog Phase B (round-2-contract §7.4; was "lodge, lift station or
        // Welcome Sign placed, moved or removed"): the ski school and the patrol huts trigger too.
        internal const string ReasonBuildings = "lodge, ski school, patrol hut, lift station or Welcome Sign placed, moved or removed";
        internal const string ReasonRuns = "ski run drawn, edited or removed";
        internal const string ReasonBoth = "buildings and ski runs changed";

        private SkiResortAttractivenessSystem m_Attractiveness;
        private Game.Simulation.SimulationSystem m_Simulation;   // T120: selectedSpeed 0 = paused
        private EntityQuery m_BuildingEvents;
        private EntityQuery m_AreaEvents;
        // L1 (code review 2026-10-19): while ResortPresence says no sign, run or lift of ours exists, only a CREATED
        // building / run can start a resort (an Updated or Deleted one cannot be ours), so the per-frame test looks
        // at these Created-only queries and an ordinary city pays no PrefabRef read for its building churn.
        private EntityQuery m_BuildingCreated;
        private EntityQuery m_AreaCreated;

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
            m_Simulation = World.GetOrCreateSystemManaged<Game.Simulation.SimulationSystem>();   // T120
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
            m_BuildingCreated = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Buildings.Building>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Created>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            m_AreaCreated = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<Game.Areas.Surface>(),   // L3: our runs are SurfacePrefab areas
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Created>(),
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
                    ComponentType.ReadOnly<Game.Areas.Surface>(),   // L3: our runs are SurfacePrefab areas
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
                Mod.Log.Info("Attractiveness [watch]: ARMED — ModificationEnd every frame (paused included): a lodge, ski school, " +
                             "patrol hut, lift station, Welcome Sign or ski run of ours Created, Updated or Deleted asks the attractiveness system to recompute " +
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
                bool present = ResortPresence.Present;   // L1
                EntityQuery buildingQuery = present ? m_BuildingEvents : m_BuildingCreated;
                EntityQuery areaQuery = present ? m_AreaEvents : m_AreaCreated;
                bool buildingEvents = !buildingQuery.IsEmptyIgnoreFilter;
                bool areaEvents = !areaQuery.IsEmptyIgnoreFilter;
                if (!buildingEvents && !areaEvents)
                    return;
                GameManager gm = GameManager.instance;
                if (gm == null || gm.isGameLoading)
                    return;

                bool buildings = buildingEvents && AnyWatched(buildingQuery, areas: false);
                bool runs = areaEvents && AnyWatched(areaQuery, areas: true);
                if (!buildings && !runs)
                    return;

                string reason = buildings ? (runs ? ReasonBoth : ReasonBuildings) : ReasonRuns;
                Triggers++;
                // The first request of each reason per load is logged there (ch1 §13), so not here.
                m_Attractiveness.RequestRecompute(reason);
                // T120: paused, GameSimulation runs no tick, so run the request here (links a new lodge / school / patrol hut
                // to its sign at once; the hub's "No lodge" and the Buildings-tab dot follow without unpausing).
                if (m_Simulation.selectedSpeed == 0f)
                    m_Attractiveness.RecomputeWhilePaused();
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
