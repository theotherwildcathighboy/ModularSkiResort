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
//   * chairs are NOT tagged directly. (Corrected in Round L1: the old reason given here —
//     "chairs are Secondary sub-objects and appear in no SubObject buffer" — is wrong, entry
//     98: chairs never carry Secondary, SubObjectSystem.cs:2824-2827, and they ARE in their
//     owner edge's SubObject buffer, SubObjectReferencesSystem.cs:122-138.) The re-pin is
//     handed to ChairPinSystem.RequestFullPass, an idempotent full re-pin at Modification4B,
//     because it must land AFTER LaneSystem has rebuilt the poked lanes later this same frame —
//     tagging chairs here, before LaneSystem, would pin them onto the old curves.
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
//
// ROUND L1 — EPISODES (2026-10-03, implementer I-2; docs/research/hub-u2-spec.md §5.6). Field
// test 5 logged the reconnect line 104 times, up to 34 per second (field log 443-524, 821-846):
// Move It keyboard nudges of an already connected top station change the Transform in the poke
// signature every moved frame, so every moved frame poked AND logged — a per-frame poke and a
// log flood. Now the pure LiftReconnectEpisodes decides, per station:
//   * a signature change (foreign-edge set or Transform differs from the LAST SEEN one) while no
//     episode is open → open it and poke at once; if the foreign-edge set changed (a line
//     attached) also RequestFullPass at once — this keeps today's same-frame closure of a line
//     drawn before its station (log 437-438: poke, then "1 loop(s)" in the same frame);
//   * further changes → at most one poke per PokeIntervalFrames (a line attached mid-episode
//     still pokes + full pass at once);
//   * stable for SettleFrames → trailing poke + RequestFullPass + ONE line:
//     `LiftReconnect [reconnect]: 'SkiTopStationBuilding' 119398:67 — station moved 34× over
//     4.6 s; 3 node(s) + 2 edge(s), 5 poke(s); chair re-pin requested` (or "— line attached").
//   * The episode tick runs EVERY frame, also when the station query is empty (the old early
//     return on an empty query, LiftReconnectSystem.cs:166 before this round, would have starved
//     the trailing poke).
//   * The last-seen signatures of Deleted stations are pruned (their open episode is logged and
//     closed); open episodes are flushed (logged, no poke) in OnGameLoadingComplete and in the
//     catch. A poke's own echo (the station comes back Updated next frame through ownership
//     propagation) has an unchanged signature and is no change.
// Frames are this system's update count (Modification4 runs every frame, paused included).
// One ARM line per load lists the constants. The poke set and its timing are unchanged.

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
        private EntityQuery m_DeletedStationQuery;     // Round L1: prune the last-seen map

        /// <summary>What a station looked like the last time this system SAW it (Round L1: last
        /// seen, not last poked — the episode machine needs every change). The set of attached
        /// foreign edges (order-independent XOR hash + presence) and the station transform. The
        /// poke's own echo (the station comes back Updated next frame through ownership
        /// update-propagation) leaves it unchanged, which is what keeps the poke from re-firing
        /// every frame (the v1.1 convergence gate). System-side and transient: cleared per load
        /// (worst case one extra idempotent poke on the first station event after loading).</summary>
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
        private readonly HashSet<Entity> m_PokeSet = new HashSet<Entity>();      // reused per collection
        private readonly HashSet<Entity> m_NodesToWalk = new HashSet<Entity>();  // reused per collection

        private long m_Frame;       // update count this load
        private bool m_Armed;       // Game or Editor loaded
        private bool m_Disabled;    // tripped by the catch; re-armed per load
        private bool m_ErrorLogged; // one error with stack per load

        /// <summary>Dev counters: episodes open now / lines written this load.</summary>
        internal int OpenEpisodes => m_Episodes.OpenCount;
        internal int EpisodeLines { get; private set; }

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

            // Spec §5.6: pending episodes are flushed (logged) here. They belong to the world that
            // was just replaced, so no poke and no entity lookup.
            FlushEpisodes("game load", lookupNames: false);
            m_LastSeen.Clear();
            m_Frame = 0;
            EpisodeLines = 0;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_Armed = mode == GameMode.Game || mode == GameMode.Editor;
            if (!m_Armed)
                return;

            // Re-arm per load. No load pass by design: saved lifts already have their lanes,
            // and this system fires on station events only, never per-frame.
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

                // Spec §5.6: the episode tick runs whether or not any station changed this frame —
                // the trailing poke of a settled episode depends on it. Allocation-free when idle.
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
                    // The flush is a courtesy log; the !!! line above already says what happened.
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

        /// <summary>Deleted stations: forget their last-seen signature; an open episode is logged
        /// and closed without a trailing poke.</summary>
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

        // ─────────────────────────────────────────────────────────────────────────────────
        // One station event: compare with what was last seen; on a change, let the episode
        // machine decide whether to poke now and whether to request the full chair pass.
        // ─────────────────────────────────────────────────────────────────────────────────
        private void OnStationEvent(Entity station)
        {
            if (!CollectPokeSet(station, out bool hasForeign, out int foreignHash))
                return;   // no SubNet buffer (not yet populated)

            var signature = new PokeSignature { m_ForeignEdgeHash = foreignHash, m_HasForeign = hasForeign };
            if (EntityManager.TryGetComponent<Game.Objects.Transform>(station, out var stationTransform))
            {
                signature.m_Position = stationTransform.m_Position;
                signature.m_Rotation = stationTransform.m_Rotation;
            }
            bool seen = m_LastSeen.TryGetValue(station, out PokeSignature last);
            if (seen && signature.Equals(in last))
                return;   // no change (e.g. our own poke's echo)

            EntityRef key = Ref(station);
            bool open = m_Episodes.TryGetOpen(key, out _);
            if (!hasForeign && !open)
            {
                // No line attached and nothing in progress (fresh placement in the void, or the
                // stations-first flow where the line arrives later and builds its own lanes):
                // full no-op. Forgetting the station makes a later attach a first sight → poke.
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

        /// <summary>Logs one closed episode. <paramref name="trailing"/>: the station settled — do the
        /// trailing poke (when a line is still attached) and request the full chair pass.</summary>
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

        // ─────────────────────────────────────────────────────────────────────────────────
        // Collect the redraw-equivalent poke set for one station into m_PokeSet. Main-thread
        // EntityManager; bounded set (~10 entities per station). False = no SubNet buffer.
        // ─────────────────────────────────────────────────────────────────────────────────
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
                    continue; // only the connection stub (same qualification as PowerBridge)

                if (EntityManager.TryGetComponent<Game.Net.Edge>(subNet, out var stubEdge))
                {
                    // The stub EDGE: its end nodes include the reused, unowned line node in
                    // the line-first order — the node a SubNet-only walk can never see.
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

                    // Redraw semantics: a modified edge updates both its end nodes too.
                    if (EntityManager.TryGetComponent<Game.Net.Edge>(edge, out var edgeTopology))
                    {
                        if (edgeTopology.m_Start != Entity.Null) m_PokeSet.Add(edgeTopology.m_Start);
                        if (edgeTopology.m_End != Entity.Null) m_PokeSet.Add(edgeTopology.m_End);
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
            if (m_PokeSet.Count == 0)
                hasForeignEdge = false;
            return true;
        }

        /// <summary>Tags the collected set Updated (immediate writes — see SCHEDULING): LaneSystem
        /// rebuilds those lanes later in this same Modification4.</summary>
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
