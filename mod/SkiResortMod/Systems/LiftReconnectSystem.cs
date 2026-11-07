// LiftReconnectSystem - makes the line-first build order work (draw the line, then place the station). On station placement it marks the
// affected net entities Updated, so the game rebuilds lanes and re-runs FindAnchors. LaneSystem's m_OwnerQuery needs All{SubLane} +
// Any{Updated, Deleted}. Placing a building marks neither the line's node nor its edges.
// Scheduling (load-bearing): UpdateBefore<LiftReconnectSystem, Game.Net.LaneSystem> at Modification4, immediate EntityManager writes (a
// barrier buffer would defer the tags past LaneSystem). Idempotent, event-driven only. With no foreign edge, the system does nothing and
// writes no log.
// Episodes (pure LiftReconnectEpisodes) throttle pokes and logs while a station is dragged. Frames are update counts (every frame).
// Engine notes: EngineNotes/LiftReconnectSystem.md

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
        private PowerBridgeSystem m_PowerBridgeSystem;
        private ChairPinSystem m_ChairPinSystem;

        private EntityQuery m_UpdatedStationQuery;
        private EntityQuery m_DeletedStationQuery;     // prune the last-seen map

        /// <summary>What a station looked like the last time this system saw it (last seen, not last poked: the episode machine
        /// needs every change). The set of attached foreign edges (order-independent XOR hash + presence) and the station
        /// transform. The poke's own echo leaves it unchanged, which keeps the poke from re-firing every frame. System-side and
        /// transient: cleared per load (worst case one extra idempotent poke on the first station event after loading).</summary>
        private struct PokeSignature
        {
            public int m_ForeignEdgeHash;
            public bool m_HasForeign;
            public float3 m_Position;
            public quaternion m_Rotation;

            public bool Equals(in PokeSignature other)
            {
                return m_ForeignEdgeHash == other.m_ForeignEdgeHash &&
                       m_HasForeign == other.m_HasForeign &&
                       m_Position.Equals(other.m_Position) &&
                       m_Rotation.Equals(other.m_Rotation);
            }
        }

        private readonly Dictionary<Entity, PokeSignature> m_LastSeen = new Dictionary<Entity, PokeSignature>();
        private readonly LiftReconnectEpisodes m_Episodes = new LiftReconnectEpisodes();
        private readonly List<LiftReconnectEpisodes.Episode> m_Closed = new List<LiftReconnectEpisodes.Episode>();
        private readonly HashSet<Entity> m_PokeSet = new HashSet<Entity>();
        private readonly HashSet<Entity> m_NodesToWalk = new HashSet<Entity>();

        private long m_Frame;
        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;

        /// <summary>Dev counters: episodes open now / lines written this load</summary>
        internal int OpenEpisodes => m_Episodes.OpenCount;
        internal int EpisodeLines { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_PowerBridgeSystem = World.GetOrCreateSystemManaged<PowerBridgeSystem>();
            m_ChairPinSystem = World.GetOrCreateSystemManaged<ChairPinSystem>();

            // Same trigger family as PowerBridgeSystem. No Transformer requirement: the prefab set is the real gate, and
            // reconnection is a net concern, not electric.
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
            m_DeletedStationQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Deleted>(),
                ComponentType.Exclude<Temp>());

            Mod.Log.Info($"{nameof(LiftReconnectSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            // Pending episodes are flushed (logged) here. They belong to the world that was just replaced, so no poke and no entity lookup.
            FlushEpisodes("game load", lookupNames: false);
            m_LastSeen.Clear();
            m_Frame = 0;
            EpisodeLines = 0;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_Armed = mode == GameMode.Game || mode == GameMode.Editor;
            if (!m_Armed)
                return;

            // Re-arm per load. No load pass by design: saved lifts already have their lanes, and this system fires on station events
            // only, never per-frame.
            Mod.Log.Info($"LiftReconnect: ARMED — per-station episodes: first change pokes at once (+ full chair pass " +
                         $"when a line attaches), then at most one poke per {LiftReconnectEpisodes.PokeIntervalFrames} " +
                         $"frames while the station keeps changing, and after {LiftReconnectEpisodes.SettleFrames} stable " +
                         "frames one trailing poke + full chair pass + ONE log line; Modification4 before LaneSystem");
        }

        protected override void OnUpdate()
        {
            try
            {
                if (!m_Armed || m_Disabled)
                    return;
                m_Frame++;

                HashSet<Entity> stationPrefabs = m_PowerBridgeSystem.GetResolvedStationPrefabs();
                if (stationPrefabs.Count > 0)
                {
                    if (!m_DeletedStationQuery.IsEmptyIgnoreFilter)
                        PruneDeleted(stationPrefabs);
                    if (!m_UpdatedStationQuery.IsEmptyIgnoreFilter)
                        ProcessChanges(stationPrefabs);
                }

                // The episode tick runs whether or not any station changed this frame: the trailing poke of a settled episode depends
                // on it. Allocation-free when idle.
                if (m_Episodes.OpenCount > 0)
                {
                    m_Closed.Clear();
                    m_Episodes.Tick(m_Frame, m_Closed);
                    for (int i = 0; i < m_Closed.Count; i++)
                        CloseEpisode(m_Closed[i], trailing: true, note: null, lookupNames: true);
                    m_Closed.Clear();
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
                try
                {
                    FlushEpisodes("exception", lookupNames: false);
                }
                catch
                {
                    // The flush is a courtesy log. The !!! line above already says what happened
                }
            }
        }

        private void ProcessChanges(HashSet<Entity> stationPrefabs)
        {
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
                        OnStationEvent(stations[i]);
                }
            }
            finally
            {
                chunks.Dispose();
                stations.Dispose();
            }
        }

        /// <summary>Deleted stations: forget their last-seen signature. An open episode is logged and closed without a trailing
        /// poke</summary>
        private void PruneDeleted(HashSet<Entity> stationPrefabs)
        {
            var chunks = m_DeletedStationQuery.ToArchetypeChunkArray(Allocator.Temp);
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
                        if (!stationPrefabs.Contains(prefabRefs[i].m_Prefab))
                            continue;
                        m_LastSeen.Remove(entities[i]);
                        if (m_Episodes.Remove(Ref(entities[i]), out LiftReconnectEpisodes.Episode ep))
                            CloseEpisode(ep, trailing: false, note: "station removed — no trailing poke", lookupNames: true);
                    }
                }
            }
            finally
            {
                chunks.Dispose();
            }
        }

        // One station event: compare with what was last seen. On a change, let the episode machine decide whether to poke now and whether
        // to request the full chair pass.
        private void OnStationEvent(Entity station)
        {
            if (!CollectPokeSet(station, out bool hasForeign, out int foreignHash))
                return;

            var signature = new PokeSignature { m_ForeignEdgeHash = foreignHash, m_HasForeign = hasForeign };
            if (EntityManager.TryGetComponent<Game.Objects.Transform>(station, out var stationTransform))
            {
                signature.m_Position = stationTransform.m_Position;
                signature.m_Rotation = stationTransform.m_Rotation;
            }
            bool seen = m_LastSeen.TryGetValue(station, out PokeSignature last);
            if (seen && signature.Equals(in last))
                return;

            EntityRef key = Ref(station);
            bool open = m_Episodes.TryGetOpen(key, out _);
            if (!hasForeign && !open)
            {
                // No line attached and nothing in progress (fresh placement in the void, or the stations-first flow where the line arrives
                // later and builds its own lanes): full no-op. Forgetting the station makes a later attach a first sight, which pokes.
                m_LastSeen.Remove(station);
                return;
            }

            bool moved = seen && !(last.m_Position.Equals(signature.m_Position) && last.m_Rotation.Equals(signature.m_Rotation));
            bool foreignChanged = seen
                ? last.m_ForeignEdgeHash != foreignHash || last.m_HasForeign != hasForeign
                : hasForeign;
            m_LastSeen[station] = signature;

            LiftReconnectEpisodes.Decision d = m_Episodes.OnChange(key, m_Frame, UnityEngine.Time.realtimeSinceStartup,
                moved, foreignChanged, hasForeign);
            if (d.Poke)
            {
                ApplyPoke(out int nodes, out int edges);
                m_Episodes.NotePokeSet(key, nodes, edges);
            }
            if (d.FullPass)
                m_ChairPinSystem.RequestFullPass("reconnect");
        }

        /// <summary>Logs one closed episode. <paramref name="trailing"/>: the station settled, so do the trailing poke (when a
        /// line is still attached) and request the full chair pass.</summary>
        private void CloseEpisode(LiftReconnectEpisodes.Episode ep, bool trailing, string note, bool lookupNames)
        {
            Entity station = Ent(ep.Station);
            bool alive = lookupNames && EntityManager.Exists(station) && !EntityManager.HasComponent<Deleted>(station);
            int nodes = ep.PokedNodes, edges = ep.PokedEdges, pokes = ep.Pokes;
            if (trailing && alive)
            {
                if (CollectPokeSet(station, out bool hasForeign, out _) && hasForeign)
                {
                    ApplyPoke(out nodes, out edges);
                    pokes++;
                }
                m_ChairPinSystem.RequestFullPass("reconnect");
                note = "chair re-pin requested";
            }
            else if (note == null)
            {
                note = "station gone — no trailing poke";
            }

            string name = alive && EntityManager.TryGetComponent<PrefabRef>(station, out var prefabRef)
                ? m_PrefabSystem.GetPrefabName(prefabRef.m_Prefab)
                : "station";
            string what;
            if (ep.Attaches > 0 && ep.Moves > 0)
                what = $"line attached, station moved {ep.Moves}× over {ep.SpanSeconds:F1} s";
            else if (ep.Attaches > 0)
                what = "line attached";
            else if (ep.Moves > 0)
                what = $"station moved {ep.Moves}× over {ep.SpanSeconds:F1} s";
            else
                what = $"station changed {ep.Changes}×";
            if (ep.Detaches > 0)
                what += ", line detached";
            EpisodeLines++;
            Mod.Log.Info($"LiftReconnect [reconnect]: '{name}' {station.Index}:{station.Version} — {what}; " +
                         $"{nodes} node(s) + {edges} edge(s), {pokes} poke(s); {note}");
        }

        private void FlushEpisodes(string reason, bool lookupNames)
        {
            if (m_Episodes.OpenCount == 0)
                return;
            m_Closed.Clear();
            m_Episodes.FlushAll(m_Closed);
            for (int i = 0; i < m_Closed.Count; i++)
                CloseEpisode(m_Closed[i], trailing: false, note: $"flushed ({reason}) — no trailing poke", lookupNames: lookupNames);
            m_Closed.Clear();
        }

        // Collect the redraw-equivalent poke set for one station into m_PokeSet. Main-thread EntityManager. Bounded set (~10 entities per
        // station). False = no SubNet buffer.
        private bool CollectPokeSet(Entity station, out bool hasForeignEdge, out int foreignEdgeHash)
        {
            hasForeignEdge = false;
            foreignEdgeHash = 0;
            m_PokeSet.Clear();
            m_NodesToWalk.Clear();
            if (!EntityManager.TryGetBuffer(station, isReadOnly: true, out DynamicBuffer<Game.Net.SubNet> subNets))
                return false;

            for (int i = 0; i < subNets.Length; i++)
            {
                Entity subNet = subNets[i].m_SubNet;
                if (EntityManager.HasComponent<Deleted>(subNet) ||
                    EntityManager.HasComponent<Temp>(subNet) ||
                    !EntityManager.TryGetComponent<PrefabRef>(subNet, out var prefabRef) ||
                    !EntityManager.HasComponent<ElectricityConnectionData>(prefabRef.m_Prefab))
                    continue;

                if (EntityManager.TryGetComponent<Game.Net.Edge>(subNet, out var stubEdge))
                {
                    // The stub edge: its end nodes include the reused, unowned line node in the line-first order, the node a SubNet-only walk
                    // can never see.
                    m_PokeSet.Add(subNet);
                    if (stubEdge.m_Start != Entity.Null) m_NodesToWalk.Add(stubEdge.m_Start);
                    if (stubEdge.m_End != Entity.Null) m_NodesToWalk.Add(stubEdge.m_End);
                }
                else if (EntityManager.HasComponent<Node>(subNet))
                {
                    m_NodesToWalk.Add(subNet);
                }
            }

            foreach (Entity node in m_NodesToWalk)
            {
                if (EntityManager.HasComponent<Deleted>(node) || EntityManager.HasComponent<Temp>(node))
                    continue;
                m_PokeSet.Add(node);

                if (!EntityManager.TryGetBuffer(node, isReadOnly: true, out DynamicBuffer<ConnectedEdge> connectedEdges))
                    continue;
                for (int i = 0; i < connectedEdges.Length; i++)
                {
                    Entity edge = connectedEdges[i].m_Edge;
                    if (edge == Entity.Null ||
                        EntityManager.HasComponent<Deleted>(edge) ||
                        EntityManager.HasComponent<Temp>(edge))
                        continue;

                    m_PokeSet.Add(edge);

                    // Redraw semantics: a modified edge updates both its end nodes too
                    if (EntityManager.TryGetComponent<Game.Net.Edge>(edge, out var edgeTopology))
                    {
                        if (edgeTopology.m_Start != Entity.Null) m_PokeSet.Add(edgeTopology.m_Start);
                        if (edgeTopology.m_End != Entity.Null) m_PokeSet.Add(edgeTopology.m_End);
                    }

                    // The lift line: any attached edge this station does not own. XOR keeps the signature order-independent over the foreign
                    // edge set.
                    if (!EntityManager.TryGetComponent<Owner>(edge, out var edgeOwner) ||
                        edgeOwner.m_Owner != station)
                    {
                        hasForeignEdge = true;
                        foreignEdgeHash ^= edge.Index * 397 ^ edge.Version;
                    }
                }
            }
            if (m_PokeSet.Count == 0)
                hasForeignEdge = false;
            return true;
        }

        /// <summary>Tags the collected set Updated (immediate writes, see Scheduling): LaneSystem rebuilds those lanes later in
        /// this same Modification4.</summary>
        private void ApplyPoke(out int pokedNodes, out int pokedEdges)
        {
            pokedNodes = 0;
            pokedEdges = 0;
            foreach (Entity entity in m_PokeSet)
            {
                if (!EntityManager.HasComponent<Updated>(entity))
                    EntityManager.AddComponent<Updated>(entity);
                if (EntityManager.HasComponent<Game.Net.Edge>(entity))
                    pokedEdges++;
                else
                    pokedNodes++;
            }
        }

        private static EntityRef Ref(Entity e) => new EntityRef(e.Index, e.Version);
        private static Entity Ent(EntityRef r) => new Entity { Index = r.Index, Version = r.Version };
    }
}
