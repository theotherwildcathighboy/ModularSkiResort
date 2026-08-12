// LiftReconnectSystem — makes the LINE-FIRST build order work, dissolving DECISIONS #6
// ("place both stations before drawing the line").
//
// THE PROBLEM: when a station is placed snapping onto an EXISTING lift line's end, the
// net-level connection succeeds but cables don't draw. Root cause, decompile-verified:
// lanes are (re)built only for owners matching LaneSystem's trigger query — All{SubLane} +
// Any{Updated, Deleted} (LaneSystem.OnCreate, m_OwnerQuery) — and nothing about placing a
// building marks the pre-existing line's node or edges Updated. Anchors that did not exist
// at edge creation are therefore never searched again. The fix is exactly what the docs
// hypothesized: mark the affected net entities Updated on station placement, so the game's
// own systems rebuild lanes and re-run FindAnchors as if the player had modified the net
// (the vanilla "net modified" path — GenerateEdgesSystem itself pokes affected entities
// with AddComponent<Updated> in the same way).
//
// MERGE-vs-JOINED (decompile finding, GenerateEdgesSystem.TryGetNode line ~1244): a course
// end position landing on an existing node REUSES that node entity — single merged node,
// never two coincident nodes. Consequences per build order:
//   * stations-first: the drawn line reuses the OWNED stub node → FindAnchors(node) walks
//     the node's owner (the station) prefab ObjectSubLanes → bullwheel anchors → cables.
//   * line-first: the stub course reuses the UNOWNED line node — the node keeps no owner
//     (Owner is only added to CREATED course entities, GenerateEdgesSystem ~1550) and is
//     NOT in the station's SubNet buffer. Anchors at that node can then come only from the
//     node's SUB-OBJECT (the line tower standing there — its prefab carries the stage-2
//     ObjectSubLanes wire stubs), which FindAnchors also walks. Cables reconnect to the
//     tower at the shared node; the station's own wrap-arc lanes come from its own
//     creation. The reused node is reachable for the poke ONLY through the stub EDGE's
//     m_Start/m_End — a SubNet walk alone would miss it (handled below).
//
// WHAT GETS POKED (dedup set, per station, only when a FOREIGN edge is attached):
//   * every subnet entry whose net prefab carries ElectricityConnectionData (the
//     SkiLiftMarker stub): the entry itself (edge or node);
//   * for stub EDGES: both end nodes (this is how the reused, unowned line node is found);
//   * every edge in each such node's ConnectedEdge buffer plus those edges' far nodes — the
//     redraw-equivalent set (a real net modification updates an edge and both its nodes);
//   * chairs are NOT tagged directly — they are Secondary sub-objects and appear in no
//     SubObject buffer (SubObjectReferencesSystem's query excludes Secondary; the original
//     buffer walk could only ever find zero). Instead ChairPinSystem.RequestFullPass runs
//     an idempotent full re-pin at Modification4B, after LaneSystem has rebuilt the poked
//     lanes later this same frame.
// "Foreign edge" = an edge on a stub node not owned by this station (the lift line). No
// foreign edge → complete no-op, no log (fresh station placement with no line nearby, and
// the normal stations-first flow, stay silent).
//
// SCHEDULING (load-bearing, and why NOT a barrier command buffer): anchored
// UpdateBefore<LiftReconnectSystem, Game.Net.LaneSystem> at Modification4.
//   * The station's SubNet buffer is populated by SubNetReferencesSystem at Modification4
//     (SystemOrder line 147, direct BufferLookup writes) — on the very placement frame the
//     buffer does not exist before this phase, which rules out running at 2B.
//   * LaneSystem runs later in the same phase (SystemOrder line 155) — our immediate
//     EntityManager writes are exactly what its m_OwnerQuery consumes, same frame. A
//     barrier buffer would defer the tags to the END of Modification4, after LaneSystem
//     already ran, silently delaying the rebuild by a frame and losing the tags to the
//     end-of-frame CleanUpSystem sweep. Immediate writes ARE the correct timing here.
//   * Downstream consumers all still see the tags: Mod4B (ChairPin/PowerBridge/EBGS),
//     Mod5 SearchSystem, PreCulling; CleanUpSystem sweeps at Cleanup.
//   * GeometrySystem/SubObjectSystem ran earlier — harmless: the poked entities' geometry
//     and spawn placements are position-unchanged (the node was reused in place); only
//     LANES need rebuilding, plus the chair re-pin we trigger directly.
//
// Idempotent (re-poking a healthy connection rebuilds identical lanes), event-driven only
// (station Created/Updated — placement, Move It nudges — never per-frame), and Move It
// moves re-poke automatically, keeping anchors fresh after nudges.

using System;
using System.Collections.Generic;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class LiftReconnectSystem : GameSystemBase
    {
        private PrefabSystem m_PrefabSystem;
        private PowerBridgeSystem m_PowerBridgeSystem; // shared station-prefab resolution
        private ChairPinSystem m_ChairPinSystem;       // chair re-pin after lane rebuilds

        private EntityQuery m_UpdatedStationQuery;

        /// <summary>Convergence gate (v1.1). The poke's Updated tags propagate back to the
        /// station next frame (ownership update-propagation), which re-matches our trigger
        /// query — without a gate the system re-pokes every frame forever (field-confirmed:
        /// dozens of identical reconnect lines per second). We record what each station was
        /// last poked for — the set of attached foreign edges plus the station's transform —
        /// and skip when unchanged. A NEW foreign edge (line drawn/redrawn onto a connected
        /// station), a detach+reattach, or a Move It nudge (transform change) all change the
        /// signature and re-fire exactly once. System-side state, not an ECS component: it
        /// is intentionally transient (cleared per load — worst case one extra idempotent
        /// poke on the first station event after loading).</summary>
        private struct PokeSignature
        {
            public int m_ForeignEdgeHash;
            public float3 m_Position;
            public quaternion m_Rotation;

            public bool Equals(in PokeSignature other)
            {
                return m_ForeignEdgeHash == other.m_ForeignEdgeHash &&
                       m_Position.Equals(other.m_Position) &&
                       m_Rotation.Equals(other.m_Rotation);
            }
        }

        private readonly Dictionary<Entity, PokeSignature> m_LastPoke = new Dictionary<Entity, PokeSignature>();

        private bool m_Disabled;    // tripped by the catch; re-armed per load
        private bool m_ErrorLogged; // one error with stack per load

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_PowerBridgeSystem = World.GetOrCreateSystemManaged<PowerBridgeSystem>();
            m_ChairPinSystem = World.GetOrCreateSystemManaged<ChairPinSystem>();

            // Same trigger family as PowerBridgeSystem. No Transformer requirement — the
            // prefab set is the real gate, and reconnection is a net concern, not electric.
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

            Mod.Log.Info($"{nameof(LiftReconnectSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (mode != GameMode.Game && mode != GameMode.Editor)
                return;

            // Re-arm per load. No load pass by design: saved lifts already have their lanes,
            // and this system fires on station events only, never per-frame.
            m_Disabled = false;
            m_ErrorLogged = false;
            m_LastPoke.Clear();
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || m_UpdatedStationQuery.IsEmptyIgnoreFilter)
                    return;

                HashSet<Entity> stationPrefabs = m_PowerBridgeSystem.GetResolvedStationPrefabs();
                if (stationPrefabs.Count == 0)
                    return; // station prefabs missing — PowerBridge logged it once already

                var stations = new NativeList<Entity>(4, Allocator.Temp);
                var chunks = m_UpdatedStationQuery.ToArchetypeChunkArray(Allocator.Temp);
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
                        if (!EntityManager.HasComponent<Destroyed>(stations[i]))
                            ReconnectStation(stations[i]);
                    }
                }
                finally
                {
                    chunks.Dispose();
                    stations.Dispose();
                }
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(LiftReconnectSystem)} EXCEPTION — reconnection disabled until " +
                                  $"the next game load. Please report this stack:\n{e}");
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────────
        // Collect the redraw-equivalent poke set for one station, then apply it only if a
        // foreign (line) edge is attached. Main-thread EntityManager; bounded set (~10
        // entities per station).
        // ─────────────────────────────────────────────────────────────────────────────────
        private void ReconnectStation(Entity station)
        {
            if (!EntityManager.TryGetBuffer(station, isReadOnly: true, out DynamicBuffer<Game.Net.SubNet> subNets))
                return;

            var pokeSet = new HashSet<Entity>();
            var nodesToWalk = new HashSet<Entity>();
            bool hasForeignEdge = false;

            for (int i = 0; i < subNets.Length; i++)
            {
                Entity subNet = subNets[i].m_SubNet;
                if (EntityManager.HasComponent<Deleted>(subNet) ||
                    EntityManager.HasComponent<Temp>(subNet) ||
                    !EntityManager.TryGetComponent<PrefabRef>(subNet, out var prefabRef) ||
                    !EntityManager.HasComponent<ElectricityConnectionData>(prefabRef.m_Prefab))
                    continue; // only the connection stub (same qualification as PowerBridge)

                if (EntityManager.TryGetComponent<Game.Net.Edge>(subNet, out var stubEdge))
                {
                    // The stub EDGE: its end nodes include the reused, unowned line node in
                    // the line-first order — the node a SubNet-only walk can never see.
                    pokeSet.Add(subNet);
                    if (stubEdge.m_Start != Entity.Null) nodesToWalk.Add(stubEdge.m_Start);
                    if (stubEdge.m_End != Entity.Null) nodesToWalk.Add(stubEdge.m_End);
                }
                else if (EntityManager.HasComponent<Node>(subNet))
                {
                    nodesToWalk.Add(subNet);
                }
            }

            int foreignEdgeHash = 0;
            foreach (Entity node in nodesToWalk)
            {
                if (EntityManager.HasComponent<Deleted>(node) || EntityManager.HasComponent<Temp>(node))
                    continue;
                pokeSet.Add(node);

                if (!EntityManager.TryGetBuffer(node, isReadOnly: true, out DynamicBuffer<ConnectedEdge> connectedEdges))
                    continue;
                for (int i = 0; i < connectedEdges.Length; i++)
                {
                    Entity edge = connectedEdges[i].m_Edge;
                    if (edge == Entity.Null ||
                        EntityManager.HasComponent<Deleted>(edge) ||
                        EntityManager.HasComponent<Temp>(edge))
                        continue;

                    pokeSet.Add(edge);

                    // Redraw semantics: a modified edge updates both its end nodes too.
                    if (EntityManager.TryGetComponent<Game.Net.Edge>(edge, out var edgeTopology))
                    {
                        if (edgeTopology.m_Start != Entity.Null) pokeSet.Add(edgeTopology.m_Start);
                        if (edgeTopology.m_End != Entity.Null) pokeSet.Add(edgeTopology.m_End);
                    }

                    // The lift line: any attached edge this station does not own. XOR keeps
                    // the signature order-independent over the foreign edge set.
                    if (!EntityManager.TryGetComponent<Owner>(edge, out var edgeOwner) ||
                        edgeOwner.m_Owner != station)
                    {
                        hasForeignEdge = true;
                        foreignEdgeHash ^= edge.Index * 397 ^ edge.Version;
                    }
                }
            }

            // No line attached (fresh placement in the void, or the stations-first flow
            // where the line arrives later and builds its own lanes correctly): full no-op.
            // Clearing the gate here makes a future detach → reattach re-fire correctly.
            if (!hasForeignEdge || pokeSet.Count == 0)
            {
                m_LastPoke.Remove(station);
                return;
            }

            // Convergence gate: the poke itself echoes back as a station Updated next frame
            // (ownership update-propagation); an unchanged signature means there is nothing
            // new to reconnect — this is what turns "every frame forever" into "once per
            // actual connection event". One poke suffices: the lanes rebuild completely in
            // that frame (field-verified: cables re-anchor to the station on the first poke).
            var signature = new PokeSignature { m_ForeignEdgeHash = foreignEdgeHash };
            if (EntityManager.TryGetComponent<Game.Objects.Transform>(station, out var stationTransform))
            {
                signature.m_Position = stationTransform.m_Position;
                signature.m_Rotation = stationTransform.m_Rotation;
            }
            if (m_LastPoke.TryGetValue(station, out var lastSignature) && signature.Equals(in lastSignature))
                return;
            m_LastPoke[station] = signature;

            int pokedNodes = 0, pokedEdges = 0;
            foreach (Entity entity in pokeSet)
            {
                if (!EntityManager.HasComponent<Updated>(entity))
                    EntityManager.AddComponent<Updated>(entity);
                if (EntityManager.HasComponent<Game.Net.Edge>(entity))
                    pokedEdges++;
                else
                    pokedNodes++;
            }

            // Chairs are Secondary sub-objects and appear in NO SubObject buffer
            // (SubObjectReferencesSystem excludes Secondary — the earlier direct-tagging
            // attempt could only ever find zero). Hand the re-pin to ChairPinSystem
            // instead: it runs at Modification4B, after LaneSystem has rebuilt the poked
            // lanes later this same frame, so the full pass lands on the NEW curves.
            m_ChairPinSystem.RequestFullPass("reconnect");

            string stationName = m_PrefabSystem.GetPrefabName(
                EntityManager.GetComponentData<PrefabRef>(station).m_Prefab);
            Mod.Log.Info($"LiftReconnect [reconnect]: poked {pokedNodes} node(s) + {pokedEdges} edge(s) " +
                         $"for station '{stationName}'; chair re-pin requested");
        }
    }
}
