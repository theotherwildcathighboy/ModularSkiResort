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
//   * CreateOrUpdateBuildingNodes builds the building side. Since DECISIONS 96 (stations
//     back to top-level) that side is the STATION itself, its own transformer node
//     (EBGS's Building+Transformer arm); since DECISIONS 97 only the BASE drive terminal
//     keeps a Transformer, and BridgeStations skips a transformer-less station. The stub
//     edge is station-owned, so IsOrphan still fails and the valve bootstrap stays
//     load-bearing. (History, DECISIONS 93-95: while stations were gate ServiceUpgrades,
//     EBGS excluded them from every building arm (None{ServiceUpgrade}, EBGS:574-578) and
//     registered the GATE, whose InstalledUpgrade recursion (EBGS:189-199) reached the
//     station's SubNet and the same valve||IsOrphan gate; BridgeStations' tag target is
//     still dual-mode for that world);
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
// maintain path instead of its delete path. A freshly placed station carries Created /
// Updated, so the update pass bridges it before EBGS processes it the same frame. On the
// repair/load pass we tag the station itself Updated (top-level, DECISIONS 96), or its
// OWNER when it has one (the entry-93 gate world, where EBGS cannot see ServiceUpgrade
// carriers) — the dual-mode block in BridgeStations. Mod-era saves reload the whole flow
// graph natively; this pass only heals valve loss, and is idempotent.
//
// SAFETY: touches ONLY nodes owned by our station prefabs (the prefab catalog's station set,
// SkiPrefabCatalogSystem.StationPrefabSet); every other building keeps the untouched vanilla
// path. Defensive try/catch with one-shot disable; missing prefabs tolerated (logged per load).
//
// CATALOG PHASE A (2026-10-11; docs/research/art/assets-2026-10/catalog-a/phase-a-contract.md §3.2 L6):
// StationPrefabNames, the station set and its name resolver (extensibility.md §1 row 6, F5) are gone. The
// names live in the PURE SkiCatalog.cs (Stations); SkiPrefabCatalogSystem resolves them per LOAD and
// revalidates them at the first catalog call of each frame, where this file resolved once per APP RUN
// (contract §8 N1). GetResolvedStationPrefabs keeps its signature (LiftReconnectSystem.cs:222 reads it each
// frame) and returns the catalog's live set. Per Game / Editor load OnGameLoadingComplete writes this
// system's own lines: a WARN per station prefab not found, a WARN when none resolved (bridging inactive this
// load), else one Info line naming the stations. Comment only, same round (extensibility.md F17, REVIEW #32):
// the THE FIX bullet on the building side and the SCHEDULING paragraph above, which still described the
// entry-93 gate world, now describe the DECISIONS 96 / 97 world; the code was already dual-mode.

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
        // Catalog Phase A (ext row 6; was PowerBridgeSystem.cs:90-96 StationPrefabNames { "SkiBaseStationBuilding",
        // "SkiTopStationBuilding" }, :101 m_StationPrefabs, :105 m_PrefabScanQuery, :107 m_PrefabsResolved): the
        // station prefabs whose stub nodes we bridge are SkiCatalog.Stations (a new station generation adds a row
        // there), resolved per load by the prefab catalog; missing names tolerated (WARN per load).
        private SkiPrefabCatalogSystem m_PrefabCatalog;
        private ElectricityFlowSystem m_ElectricityFlowSystem;

        private EntityQuery m_UpdatedStationQuery; // stations changed this frame
        private EntityQuery m_AllStationQuery;     // every live station (load pass)

        private bool m_FullPassPending;
        private bool m_Disabled;    // tripped by the catch; re-armed per load (v1.2.1 hardening)
        private bool m_ErrorLogged; // one error with stack per load

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();   // Catalog Phase A
            m_ElectricityFlowSystem = World.GetOrCreateSystemManaged<ElectricityFlowSystem>();

            // NO Transformer requirement in the QUERIES (entry 93 lesson: a chunk-level
            // Transformer term once made both queries silently empty — dead lifts).
            // Since entry 97 the TOP station intentionally has no Transformer at all, so
            // the requirement lives as an explicit per-entity check in the pass loop,
            // where it can't silently zero the query. The prefab filter is the real
            // selector; Building+SubNet is just the cheap chunk-level narrowing.
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

            // (Catalog Phase A, F5: the prefab-by-name scan query created here, was :159, is gone.)

            Mod.Log.Info($"{nameof(PowerBridgeSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (mode != GameMode.Game && mode != GameMode.Editor)
                return;

            // Catalog Phase A (ext row 6; F5): ResolveStationPrefabs() was called here, once per APP RUN; the
            // catalog resolves per load and LogStationPrefabs() below writes this system's per-load lines.
            // Saves written before this system shipped have unregistered stations; also
            // covers valve loss from any earlier session. Idempotent when already bridged.
            m_FullPassPending = true;

            // Re-arm per load (an error no longer kills the whole app run).
            m_Disabled = false;
            m_ErrorLogged = false;

            LogStationPrefabs();
        }

        protected override void OnUpdate()
        {
            try
            {
                // Catalog Phase A (ext row 6): the catalog's live station set, validated by the first catalog call
                // of the frame (was m_StationPrefabs, resolved once per app run). Empty before the first game /
                // editor load, so the startup main menu returns here, as before.
                HashSet<Entity> stationPrefabs = m_PrefabCatalog.StationPrefabSet;
                if (m_Disabled || stationPrefabs.Count == 0)
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

            // Catalog Phase A (ext row 6; was m_StationPrefabs, :235): the catalog's station set, fetched ONCE before
            // the chunk loop (contract §2.1).
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

                    // A destroyed (collapsed) station keeps whatever registration it has —
                    // EBGS maintains marker/valve wiring for destroyed buildings itself —
                    // but we don't bootstrap NEW valves onto a ruin.
                    if (EntityManager.HasComponent<Destroyed>(station))
                        continue;

                    // Only transformer stations get bridged (entry 97): the top station
                    // sheds its prefab Transformer + LV stub so it stops demanding a
                    // PowerlineLow feed — the resort's power enters at the base drive
                    // terminal only. A transformer-less building matches no EBGS building
                    // arm, so a valve bootstrapped here would be a stranded flow node
                    // nothing ever wires.
                    if (!EntityManager.HasComponent<Game.Buildings.Transformer>(station))
                        continue;

                    int created = BridgeStation(station);
                    if (created > 0)
                    {
                        valvesCreated += created;
                        stationsTouched++;

                        // Repair/load pass: tag whichever entity EBGS actually processes.
                        // TOP-LEVEL station (the shipped configuration, entry 96): the
                        // station itself — EBGS's Building+Transformer arm matches it.
                        // If a station ever carries Owner again (the entry-93 upgrade
                        // experiment, reverted for Move It but kept working here), EBGS
                        // cannot see ServiceUpgrade carriers, so the tag must go to the
                        // OWNER. Either way we run before EBGS in Modification4B, so
                        // registration completes this same frame (CleanUpSystem sweeps
                        // the tag at end of frame).
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

        /// <summary>The station prefab set of the lift system family (LiftReconnectSystem reads it
        /// each frame — one source of truth). Catalog Phase A (ext row 6; contract D6, signature
        /// kept): the prefab catalog's LIVE set (SkiPrefabCatalogSystem.StationPrefabSet), validated at
        /// each call — the first catalog call of a frame revalidates it, and it is resolved per load
        /// (was: resolved on the first call, once per app run). Empty before the first game / editor
        /// load. Read-only by convention. Main thread.</summary>
        internal HashSet<Entity> GetResolvedStationPrefabs()
        {
            return m_PrefabCatalog.StationPrefabSet;
        }

        // ─────────────────────────────────────────────────────────────────────────────────
        // Prefab state — per load. Catalog Phase A (ext row 6; F5): ResolveStationPrefabs (was
        // PowerBridgeSystem.cs:363-411: PrefabID fast path, name-scan fallback, once per APP RUN)
        // is gone; SkiPrefabCatalogSystem resolves the same names with ONE typed resolver per load
        // (its "PrefabCatalog [arm]" line names every prefab FOUND or MISSING). This method keeps
        // this system's own lines, now per load, with the old texts ("this session" → "this load").
        // ─────────────────────────────────────────────────────────────────────────────────
        // Catalog Phase B (2026-10-11, code-round-2/round-2-contract.md §6.4, lift group): the station set is the catalog's
        // (every lift type's terminals; a transformer-less station is skipped in BridgeStations as before). The per-station
        // "not found" WARN fires for EXPECTED rows only (StationDef.Expected, the name's own flag); the pending stations
        // (designed, not imported yet) get ONE Info line per load.
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
