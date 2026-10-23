// Tells SkiResortAttractivenessSystem that a lodge, lift station, Welcome Sign, ski school, patrol hut or ski run of ours was
// placed, changed or removed, so scores follow a placement on that system's next 16-frame tick instead of its 4096-frame
// backstop. The request is structural: RecomputePending means "a structural recompute is pending", so a placement is never
// held behind a pending soft one.
//
// Why a separate system (decompile 1.6.0f1): MainLoop runs through UpdateWorld (ToolSystem, then ModificationSystem,
// SystemOrder.cs:58, 60), then Cleanup (GameManager.cs:709 -> :2391, :711 -> :2399). CleanUpSystem (SystemOrder.cs:54) destroys
// the frame's Deleted entities and strips Created / Updated / Applied (CleanUpSystem.cs:24-32, :52-53). GameSimulation runs only
// later in the frame (GameManager.cs:721 -> :2407; SystemOrder.cs:74; SimulationSystem.cs:286), so a tool-made tag never
// reaches a GameSimulation system. Same fact as SkiRunBindTriggerSystem ("the GameSimulation sweep never sees it").
//
// Why ModificationEnd (registration in Mod.cs): ModificationSystem runs ModificationEnd last (ModificationSystem.cs:19-26)
// inside MainLoop, paused or not, before Cleanup. A plain UpdateAt sorts after every vanilla UpdateAt of the phase (mods
// register after SystemOrder.Initialize; UpdateSystem.cs:39-47, 158-161), so this frame's tool output (applied through the
// ToolOutputBarrier inside ToolUpdate) and every Modification phase are in, and Deleted entities still exist. No update
// interval: ModificationSystem calls the non-indexed UpdateSystem.Update(phase), which ignores it (UpdateSystem.cs:183-221).
//
// Watches two queries, Any {Created, Updated, Deleted} None {Temp}: buildings {Building, PrefabRef} and areas {Area,
// PrefabRef}. Removals count too. Both match vanilla entities, so the result is filtered in managed code against the
// attractiveness system's prefab sets (IsWatchedBuildingPrefab = lodge, lift station, Welcome Sign, ski school and patrol huts
// via SkiPrefabCatalogSystem.IsUpgradeBuilding; IsWatchedAreaPrefab = SkiRunOwnershipSystem.IsRunPrefab). IsEmptyIgnoreFilter
// keeps idle frames free of any job sync; on an event frame the only sync is PrefabRef's writers (ToComponentDataArray, as in
// the bind trigger's AnyRunPrefab). Nothing is scanned while a structural recompute is already pending.
//
// Calls SkiResortAttractivenessSystem.RequestRecompute(reason). While the game is paused (selectedSpeed 0, no GameSimulation
// frame) this watcher runs the recompute at once through RecomputeWhilePaused, so a lodge placed while paused links to its
// sign immediately. Bindings, lift census, ratings, run open states, settings, sign efficiency, lodge on/off and lift power
// raise no tag; the attractiveness system compares them on its own tick.
//
// Logs: "created"; one ARMED line per load; the first trigger of each reason per load is logged by RequestRecompute (later
// ones are counted in the hub DUMP); a one-shot "!!!" disables the watcher until the next load. Game mode only;
// GameManager.isGameLoading guard.

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
        // Reason texts; the ski school and patrol huts trigger too
        internal const string ReasonBuildings = "lodge, ski school, patrol hut, lift station or Welcome Sign placed, moved or removed";
        internal const string ReasonRuns = "ski run drawn, edited or removed";
        internal const string ReasonBoth = "buildings and ski runs changed";

        private SkiResortAttractivenessSystem m_Attractiveness;
        private Game.Simulation.SimulationSystem m_Simulation;   // selectedSpeed 0 = paused
        private EntityQuery m_BuildingEvents;
        private EntityQuery m_AreaEvents;
        // While ResortPresence says no sign, run or lift of ours exists, only a CREATED building / run can start a resort (an Updated
        // or Deleted one cannot be ours), so the per-frame test looks at these Created-only queries and an ordinary city pays no
        // PrefabRef read for its building churn.
        private EntityQuery m_BuildingCreated;
        private EntityQuery m_AreaCreated;

        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;

        public int Triggers;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Attractiveness = World.GetOrCreateSystemManaged<SkiResortAttractivenessSystem>();
            m_Simulation = World.GetOrCreateSystemManaged<Game.Simulation.SimulationSystem>();
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
                    ComponentType.ReadOnly<Game.Areas.Surface>(),   // our runs are SurfacePrefab areas
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
                    ComponentType.ReadOnly<Game.Areas.Surface>(),   // our runs are SurfacePrefab areas
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
                // Cheapest first: field reads, then IsEmptyIgnoreFilter (syncs nothing). A pending soft recompute does not stop a placement.
                if (!m_Attractiveness.IsActive || m_Attractiveness.RecomputePending)
                    return;
                bool present = ResortPresence.Present;
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
                // The first request of each reason per load is logged there
                m_Attractiveness.RequestRecompute(reason);
                // Paused: GameSimulation runs no tick, so run the request here (links a new lodge / school / patrol hut to its sign at once)
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

        /// <summary>Managed PrefabRef scan of an event query's small result against the attractiveness system's sets.
        /// ToComponentDataArray&lt;PrefabRef&gt; completes PrefabRef's write dependency itself; no query-wide CompleteDependency.</summary>
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
