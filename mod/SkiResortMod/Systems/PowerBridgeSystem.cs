// Makes the vanilla electricity graph accept our stations' owned stub: a real 3 m marker-net edge whose Owner makes
// ElectricityBuildingGraphSystem (EBGS) reject its nodes. This system bootstraps an ElectricityValveConnection onto the station's
// qualifying stub nodes. From then on vanilla wires, maintains and deletes everything (we need no delete path).
// Scheduling (load-bearing): UpdateBefore<PowerBridgeSystem, EBGS> at Modification4B. Our EntityManager writes are immediate, so EBGS's job
// (same frame, scheduled after us) sees the valve and takes its maintain path instead of its delete path. Idempotent.
// Touches only nodes owned by our station prefabs (SkiPrefabCatalogSystem.StationPrefabSet). Defensive try/catch with one-shot disable.
// Missing prefabs are tolerated (per load: a WARN per prefab not found, a WARN when none resolved, else one Info line).
// Engine notes: EngineNotes/PowerBridgeSystem.md

using System;
using System.Collections.Generic;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class PowerBridgeSystem : GameSystemBase
    {
        // The station prefabs whose stub nodes we bridge are SkiCatalog.Stations (a new station generation adds a row there), resolved per
        // load by the prefab catalog. Missing names are tolerated (WARN per load).
        private SkiPrefabCatalogSystem m_PrefabCatalog;
        private ElectricityFlowSystem m_ElectricityFlowSystem;

        private EntityQuery m_UpdatedStationQuery; // stations changed this frame
        private EntityQuery m_AllStationQuery;     // every live station (load pass)

        private bool m_FullPassPending;
        private bool m_Disabled; // tripped by the catch. Re-armed per load
        private bool m_ErrorLogged; // one error with stack per load

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_ElectricityFlowSystem = World.GetOrCreateSystemManaged<ElectricityFlowSystem>();

            // No Transformer requirement in the QUERIES: a chunk-level Transformer term once made both queries silently empty, and the TOP
            // station intentionally has no Transformer. The requirement is an explicit per-entity check in the pass loop. The prefab filter is
            // the real selector; Building+SubNet is just the cheap chunk-level narrowing.
            m_UpdatedStationQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<Game.Net.SubNet>(),
                    ComponentType.ReadOnly<PrefabRef>(),
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

            m_AllStationQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<Game.Net.SubNet>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });


            Mod.Log.Info($"{nameof(PowerBridgeSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (mode != GameMode.Game && mode != GameMode.Editor)
                return;

            // Saves written before this system shipped have unregistered stations. This pass also covers valve loss from any earlier
            // session. Idempotent when already bridged.
            m_FullPassPending = true;

            m_Disabled = false;
            m_ErrorLogged = false;

            LogStationPrefabs();
        }

        protected override void OnUpdate()
        {
            try
            {
                // The catalog's live station set, validated by the first catalog call of the frame. Empty before the first game / editor load,
                // so the startup main menu returns here.
                HashSet<Entity> stationPrefabs = m_PrefabCatalog.StationPrefabSet;
                if (m_Disabled || stationPrefabs.Count == 0)
                    return; // error-tripped this load, or station prefabs missing. Already logged

                if (m_FullPassPending)
                {
                    m_FullPassPending = false;
                    BridgeStations(m_AllStationQuery, tagStationsUpdated: true, logLabel: "load pass");
                }
                else if (!m_UpdatedStationQuery.IsEmptyIgnoreFilter)
                {
                    // These stations already carry Created/Updated, so EBGS processes them later this frame and finds our valves in place.
                    BridgeStations(m_UpdatedStationQuery, tagStationsUpdated: false, logLabel: "update");
                }
            }
            catch (Exception e)
            {
                // Unmissable, with the full stack, once per load. The next load re-arms automatically.
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(PowerBridgeSystem)} EXCEPTION — power bridging disabled until " +
                                  $"the next game load. Please report this stack:\n{e}");
                }
            }
        }

        // Core pass: for every matching station, ensure each qualifying stub node carries an ElectricityValveConnection so EBGS's
        // FindMarkerNodes gate passes. Main-thread EntityManager on a handful of stations: immediacy is the point (EBGS must see the
        // writes later this same frame).
        private void BridgeStations(EntityQuery query, bool tagStationsUpdated, string logLabel)
        {
            int valvesCreated = 0, stationsTouched = 0, stationsSeen = 0;

            HashSet<Entity> stationPrefabs = m_PrefabCatalog.StationPrefabSet;
            var stations = new NativeList<Entity>(8, Allocator.Temp);
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
                        if (stationPrefabs.Contains(prefabRefs[i].m_Prefab))
                            stations.Add(entities[i]);
                    }
                }

                for (int i = 0; i < stations.Length; i++)
                {
                    Entity station = stations[i];
                    stationsSeen++;

                    // A destroyed (collapsed) station keeps whatever registration it has (EBGS maintains marker/valve wiring for destroyed buildings
                    // itself), but we don't bootstrap NEW valves onto a ruin.
                    if (EntityManager.HasComponent<Destroyed>(station))
                        continue;

                    // Only transformer stations get bridged. The top station sheds its prefab Transformer + LV stub so it stops demanding a
                    // PowerlineLow feed (the resort's power enters at the base drive terminal only). A transformer-less building matches no
                    // EBGS building arm, so a valve bootstrapped here would be a stranded flow node nothing ever wires.
                    if (!EntityManager.HasComponent<Game.Buildings.Transformer>(station))
                        continue;

                    int created = BridgeStation(station);
                    if (created > 0)
                    {
                        valvesCreated += created;
                        stationsTouched++;

                        // Repair/load pass: tag whichever entity EBGS actually processes: the station itself (its Building+Transformer arm matches it),
                        // or its Owner if it has one (EBGS cannot see ServiceUpgrade carriers). We run before EBGS in Modification4B, so registration
                        // completes this same frame (CleanUpSystem sweeps the tag at end of frame).
                        if (tagStationsUpdated)
                        {
                            Entity tagTarget = station;
                            if (EntityManager.TryGetComponent(station, out Owner owner) &&
                                EntityManager.Exists(owner.m_Owner) &&
                                !EntityManager.HasComponent<Deleted>(owner.m_Owner))
                            {
                                tagTarget = owner.m_Owner;
                            }
                            if (!EntityManager.HasComponent<Updated>(tagTarget) &&
                                !EntityManager.HasComponent<Created>(tagTarget))
                            {
                                EntityManager.AddComponent<Updated>(tagTarget);
                            }
                        }
                    }
                }
            }
            finally
            {
                chunks.Dispose();
                stations.Dispose();
            }

            if (valvesCreated > 0 || logLabel == "load pass")
            {
                Mod.Log.Info(
                    $"PowerBridge [{logLabel}]: registered {valvesCreated} station connection node(s) " +
                    $"on {stationsTouched} of {stationsSeen} station(s) — vanilla graph maintains them from here");
            }
        }

        /// <summary>Ensures a valve connection on every qualifying stub node. Returns how many
        /// valves were created (0 = already bridged, the idempotent steady state).</summary>
        private int BridgeStation(Entity station)
        {
            if (!EntityManager.TryGetBuffer(station, isReadOnly: true, out DynamicBuffer<Game.Net.SubNet> subNets))
                return 0;

            // NOTE: vanilla also recurses into InstalledUpgrade subnets. Our stations have no
            // subnet-bearing upgrades; extend here if station upgrade buildings ever ship.
            int created = 0;
            for (int i = 0; i < subNets.Length; i++)
            {
                Entity node = subNets[i].m_SubNet;

                // Same qualification FindMarkerNodes uses, minus the orphan gate we exist to
                // bypass: a live net node whose net prefab carries ElectricityConnectionData.
                if (!EntityManager.HasComponent<Node>(node) ||
                    EntityManager.HasComponent<Deleted>(node) ||
                    !EntityManager.TryGetComponent<PrefabRef>(node, out var prefabRef) ||
                    !EntityManager.HasComponent<ElectricityConnectionData>(prefabRef.m_Prefab))
                    continue;

                // Already bridged (steady state after the first pass and after any vanilla maintenance).
                if (EntityManager.TryGetComponent<ElectricityValveConnection>(node, out var existing) &&
                    existing.m_ValveNode != Entity.Null &&
                    EntityManager.Exists(existing.m_ValveNode) &&
                    !EntityManager.HasComponent<Deleted>(existing.m_ValveNode))
                    continue;

                // The bootstrap: a valve flow node (vanilla's own archetype: ElectricityFlowNode + ConnectedFlowEdge) plus the valve
                // component on the net node. EBGS's CreateOrUpdateMarkerNode does everything else. NativeArray overload: the single-entity
                // CreateEntity overload set pulls in ReadOnlySpan, which net48 lacks.
                Entity valveNode;
                using (var createdEntities = new NativeArray<Entity>(1, Allocator.Temp))
                {
                    EntityManager.CreateEntity(m_ElectricityFlowSystem.nodeArchetype, createdEntities);
                    valveNode = createdEntities[0];
                }
                EntityManager.AddComponentData(node, new ElectricityValveConnection { m_ValveNode = valveNode });
                created++;
            }
            return created;
        }

        /// <summary>The station prefab set of the lift system family (LiftReconnectSystem reads it each frame): the prefab catalog's live
        /// set (SkiPrefabCatalogSystem.StationPrefabSet), revalidated by the first catalog call of a frame and resolved per load. Empty
        /// before the first game / editor load. Read-only by convention. Main thread.</summary>
        internal HashSet<Entity> GetResolvedStationPrefabs()
        {
            return m_PrefabCatalog.StationPrefabSet;
        }

        // Per-load lines of this system. SkiPrefabCatalogSystem resolves the names (its "PrefabCatalog [arm]" line lists every prefab FOUND
        // or MISSING). The station set is the catalog's (every lift type's terminals; a transformer-less station is skipped in
        // BridgeStations). The per-station "not found" WARN fires for expected rows only (StationDef.Expected). Pending stations (designed,
        // not imported yet) get one Info line per load.
        private void LogStationPrefabs()
        {
            HashSet<Entity> stationPrefabs = m_PrefabCatalog.StationPrefabSet;
            var found = new System.Text.StringBuilder();
            var pending = new System.Text.StringBuilder();
            IReadOnlyList<StationDef> defs = SkiCatalog.Stations;
            for (int i = 0; i < defs.Count; i++)
            {
                string name = defs[i].Prefab;
                if (m_PrefabCatalog.EntityOf(name) == Entity.Null)
                {
                    if (!defs[i].Expected)
                    {
                        if (pending.Length > 0)
                            pending.Append(", ");
                        pending.Append('\'').Append(name).Append('\'');
                        continue;
                    }
                    Mod.Log.Warn($"{nameof(PowerBridgeSystem)}: station prefab '{name}' not found — " +
                                 "not bridged (asset pack missing or renamed?)");
                    continue;
                }
                if (found.Length > 0)
                    found.Append(", ");
                found.Append('\'').Append(name).Append('\'');
            }

            if (stationPrefabs.Count == 0)
                Mod.Log.Warn($"{nameof(PowerBridgeSystem)}: no station prefabs resolved — power bridging inactive this load");
            else
                Mod.Log.Info($"{nameof(PowerBridgeSystem)}: station prefab(s) {found} from the prefab catalog");
            if (pending.Length > 0)
                Mod.Log.Info($"{nameof(PowerBridgeSystem)}: pending station prefab(s) not imported yet: {pending}");
        }
    }
}
