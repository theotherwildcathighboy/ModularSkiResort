// PowerBridgeSystem — makes the vanilla electricity graph accept our stations' owned stub.
//
// THE PROBLEM (DECISIONS.md #9b, now superseded): our stations connect the lift line at a
// marker-net subnet stub that is a REAL 3 m edge (needed so line attachment passes
// validation). Game.Simulation.ElectricityBuildingGraphSystem (EBGS) registers a building's
// electricity connection only for subnet nodes that pass FindMarkerNodes' gate:
//
//     m_ElectricityValveConnections.HasComponent(subNet) || IsOrphan(subNet)
//
// and IsOrphan returns false when any connected edge has an Owner — our owned stub edge
// disqualifies BOTH of its nodes. Result: line attaches, zero power flow, and worse, on
// every station update EBGS's else-branch actively DELETES any ElectricityBuildingConnection
// the station has (Execute: result.Length == 0 → DeleteBuildingNodes).
//
// THE FIX (deliberately minimal — read the gate again): the gate is an OR. A node that
// carries ElectricityValveConnection passes REGARDLESS of orphan-ness. So this system does
// exactly one thing: it bootstraps an ElectricityValveConnection (with a freshly created
// valve flow node from ElectricityFlowSystem.nodeArchetype) onto our stations' qualifying
// stub nodes, BEFORE EBGS runs in the same frame. From that moment on, VANILLA does 100% of
// the real work and maintenance with its own code paths:
//   * CreateOrUpdateMarkerNode reuses our valve (TryGetComponent-then-create pattern),
//     creates/reuses the node's ElectricityNodeConnection, wires valve↔marker with the stub
//     prefab's ElectricityConnectionData (SkiLiftMarker: voltage HV, direction Both,
//     capacity 800000), wires transformer↔valve, and EnsureMarkerNodeEdgeConnections wires
//     the marker to any attached lift-line edges (dedup via HasAnyFlowEdge);
//   * CreateOrUpdateBuildingNodes builds the building side — SINCE THE STATION-AS-
//     UPGRADE MIGRATION (entry 93) that side lives on the GATE, not the station: a
//     ServiceUpgrade-carrying station loses runtime Transformer (Transformer.cs
//     archetype guard) and is excluded from every EBGS building arm
//     (None{ServiceUpgrade}, EBGS:574-578). ServiceUpgradeSystem instead applies
//     Transformer+Efficiency to the gate on install, EBGS registers the GATE, and its
//     InstalledUpgrade recursion (EBGS:189-199) walks into the station's own SubNet
//     buffer — where it hits the same valve||IsOrphan gate this system exists to
//     satisfy. The stub edge stays station-owned, so IsOrphan still fails and the
//     valve bootstrap is still load-bearing, unchanged;
//   * ElectricityEdgeGraphSystem (Modification2B) already self-wires newly drawn line edges
//     to any node carrying ElectricityNodeConnection (GetOrCreateNetNodeConnection), so
//     later line draws/deletes need nothing from us;
//   * DELETION is fully vanilla and leak-free: ElectricityGraphDeleteSystem (Modification1)
//     reacts to Deleted on anything carrying Node/Valve/BuildingConnection components and
//     cascades Deleted onto the flow nodes and all their ConnectedFlowEdge entities. When
//     the station is bulldozed its subnet nodes die → our valve + vanilla's marker node are
//     cascaded; the building connection cascades via the same system. We never need a
//     delete path of our own, and we create no entity vanilla doesn't know how to clean.
//     (Its DeleteValveNodesJob strips valves only from Owner-LESS updated nodes — our stub
//     nodes are owned by the station, so it never touches them.)
//
// WHICH NODES: ALL of the station's subnet nodes whose net prefab carries
// ElectricityConnectionData (both ends of the stub edge). Deliberate, reasoned from the
// flow code: vanilla buildings with several marker stubs register every one of them against
// the same transformer (EBGS's foreach over FindMarkerNodes' result list), so multiple
// registrations per building are the vanilla NORM, and the flow solver is a max-flow
// network where a second transformer↔valve↔marker path is just a parallel path. Registering
// both ends also means we never have to geometrically identify "the outer node the line
// attaches at" — whichever node the line lands on is registered.
//
// SCHEDULING (load-bearing): anchored UpdateBefore<PowerBridgeSystem, EBGS> at
// Modification4B — EBGS's own phase (SystemOrder line 185). Our EntityManager writes are
// immediate, so EBGS's job (scheduled after us, same frame) sees the valve and takes its
// maintain path instead of its delete path. Fresh panel placement needs no tagging from
// us: ApplyObjectsSystem:595 tags the GATE Updated when its upgrade temp applies, and
// the gate carries Transformer by the time EBGS runs (ServiceUpgradeSystem adds it at
// the Modification4 barrier, which plays back before 4B). On the repair/load pass we
// tag the station's OWNER (the gate) Updated — tagging the station itself would be
// useless, EBGS cannot see ServiceUpgrade carriers (mod-era saves reload the whole flow
// graph natively; this pass only heals valve loss, and is idempotent).
//
// SAFETY: touches ONLY nodes owned by our station prefabs (name-resolved, same pattern as
// ChairPinSystem); every other building keeps the untouched vanilla path. Defensive
// try/catch with one-shot disable; missing prefabs tolerated (logged once).

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
        // Station prefabs whose stub nodes we bridge. Extend when new station generations
        // ship. Resolution is by name, once per session, missing names tolerated.
        private static readonly string[] StationPrefabNames =
        {
            "SkiBaseStationBuilding",
            "SkiTopStationBuilding",
        };

        private PrefabSystem m_PrefabSystem;
        private ElectricityFlowSystem m_ElectricityFlowSystem;

        private readonly HashSet<Entity> m_StationPrefabs = new HashSet<Entity>();

        private EntityQuery m_UpdatedStationQuery; // stations changed this frame
        private EntityQuery m_AllStationQuery;     // every live station (load pass)
        private EntityQuery m_PrefabScanQuery;     // fallback prefab-by-name resolution

        private bool m_PrefabsResolved;
        private bool m_FullPassPending;
        private bool m_Disabled;    // tripped by the catch; re-armed per load (v1.2.1 hardening)
        private bool m_ErrorLogged; // one error with stack per load
        private bool m_OrphanStationWarned; // ownerless station on the repair pass (entry 93)

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_ElectricityFlowSystem = World.GetOrCreateSystemManaged<ElectricityFlowSystem>();

            // NO Transformer requirement (entry 93): as ServiceUpgrade children the
            // stations never carry runtime Game.Buildings.Transformer — requiring it
            // made both queries silently empty post-migration (dead lifts). The prefab
            // filter in the pass loop is the real selector; Building+SubNet is just the
            // cheap chunk-level narrowing.
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

            m_PrefabScanQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());

            Mod.Log.Info($"{nameof(PowerBridgeSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (mode != GameMode.Game && mode != GameMode.Editor)
                return;

            ResolveStationPrefabs();
            // Saves written before this system shipped have unregistered stations; also
            // covers valve loss from any earlier session. Idempotent when already bridged.
            m_FullPassPending = true;

            // Re-arm per load (an error no longer kills the whole app run).
            m_Disabled = false;
            m_ErrorLogged = false;
            m_OrphanStationWarned = false;
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || m_StationPrefabs.Count == 0)
                    return; // error-tripped this load, or station prefabs missing — logged already

                if (m_FullPassPending)
                {
                    m_FullPassPending = false;
                    BridgeStations(m_AllStationQuery, tagStationsUpdated: true, logLabel: "load pass");
                }
                else if (!m_UpdatedStationQuery.IsEmptyIgnoreFilter)
                {
                    // These stations already carry Created/Updated, so EBGS will process
                    // them later this frame and find our valves in place.
                    BridgeStations(m_UpdatedStationQuery, tagStationsUpdated: false, logLabel: "update");
                }
            }
            catch (Exception e)
            {
                // Unmissable, with the full stack, once per load; the prefab table is KEPT so
                // the next load re-arms automatically.
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(PowerBridgeSystem)} EXCEPTION — power bridging disabled until " +
                                  $"the next game load. Please report this stack:\n{e}");
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────────
        // Core pass: for every matching station, ensure each qualifying stub node carries an
        // ElectricityValveConnection so EBGS's FindMarkerNodes gate passes. Main-thread
        // EntityManager on a handful of stations — no job needed, and immediacy is the point
        // (EBGS must see the writes later this same frame).
        // ─────────────────────────────────────────────────────────────────────────────────
        private void BridgeStations(EntityQuery query, bool tagStationsUpdated, string logLabel)
        {
            int valvesCreated = 0, stationsTouched = 0, stationsSeen = 0;

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
                        if (m_StationPrefabs.Contains(prefabRefs[i].m_Prefab))
                            stations.Add(entities[i]);
                    }
                }

                for (int i = 0; i < stations.Length; i++)
                {
                    Entity station = stations[i];
                    stationsSeen++;

                    // A destroyed (collapsed) station keeps whatever registration it has —
                    // EBGS maintains marker/valve wiring for destroyed buildings itself —
                    // but we don't bootstrap NEW valves onto a ruin.
                    if (EntityManager.HasComponent<Destroyed>(station))
                        continue;

                    int created = BridgeStation(station);
                    if (created > 0)
                    {
                        valvesCreated += created;
                        stationsTouched++;

                        // Repair/load pass: EBGS's subject is the GATE (the station's
                        // owner) — it cannot see ServiceUpgrade carriers at all — so a
                        // healed valve only takes effect once the gate reprocesses. Tag
                        // the gate Updated; we run before EBGS in Modification4B, so
                        // registration completes this same frame (CleanUpSystem sweeps
                        // the tag at end of frame). A station with no owner is a
                        // placement anomaly: log once, skip (nothing to tag).
                        if (tagStationsUpdated)
                        {
                            if (EntityManager.TryGetComponent(station, out Owner owner) &&
                                EntityManager.Exists(owner.m_Owner) &&
                                !EntityManager.HasComponent<Deleted>(owner.m_Owner))
                            {
                                if (!EntityManager.HasComponent<Updated>(owner.m_Owner) &&
                                    !EntityManager.HasComponent<Created>(owner.m_Owner))
                                {
                                    EntityManager.AddComponent<Updated>(owner.m_Owner);
                                }
                            }
                            else if (!m_OrphanStationWarned)
                            {
                                m_OrphanStationWarned = true;
                                Mod.Log.Warn("PowerBridge: station has no live owner gate — " +
                                             "valve seeded but EBGS registration deferred " +
                                             "until its gate next updates (warned once per load)");
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

                // Already bridged (steady state after the first pass, and after any vanilla
                // maintenance) — nothing to do.
                if (EntityManager.TryGetComponent<ElectricityValveConnection>(node, out var existing) &&
                    existing.m_ValveNode != Entity.Null &&
                    EntityManager.Exists(existing.m_ValveNode) &&
                    !EntityManager.HasComponent<Deleted>(existing.m_ValveNode))
                    continue;

                // The bootstrap: a valve flow node (vanilla's own archetype: ElectricityFlowNode
                // + ConnectedFlowEdge) + the valve component on the net node. EBGS's
                // CreateOrUpdateMarkerNode does everything else with this in place.
                // (NativeArray overload: the single-entity CreateEntity overload set pulls in
                // ReadOnlySpan, which net48 lacks.)
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

        /// <summary>Shared station-prefab resolution for the lift system family
        /// (LiftReconnectSystem reuses this set — one source of truth). Resolves on first
        /// call; safe to call from any main-thread system once prefabs are loaded.</summary>
        internal HashSet<Entity> GetResolvedStationPrefabs()
        {
            ResolveStationPrefabs();
            return m_StationPrefabs;
        }

        // ─────────────────────────────────────────────────────────────────────────────────
        // Prefab resolution — same two-tier pattern as ChairPinSystem: PrefabID fast path,
        // name-scan fallback, once per session, missing names logged and tolerated.
        // ─────────────────────────────────────────────────────────────────────────────────
        private void ResolveStationPrefabs()
        {
            if (m_PrefabsResolved)
                return;
            m_PrefabsResolved = true;

            foreach (string name in StationPrefabNames)
            {
                Entity prefabEntity = Entity.Null;
                if (m_PrefabSystem.TryGetPrefab(new PrefabID(nameof(BuildingPrefab), name), out PrefabBase prefabBase))
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
                    Mod.Log.Warn($"{nameof(PowerBridgeSystem)}: station prefab '{name}' not found — " +
                                 "not bridged (asset pack missing or renamed?)");
                    continue;
                }

                m_StationPrefabs.Add(prefabEntity);
                Mod.Log.Info($"{nameof(PowerBridgeSystem)}: resolved station prefab '{name}'");
            }

            if (m_StationPrefabs.Count == 0)
                Mod.Log.Warn($"{nameof(PowerBridgeSystem)}: no station prefabs resolved — power bridging inactive this session");
        }
    }
}
