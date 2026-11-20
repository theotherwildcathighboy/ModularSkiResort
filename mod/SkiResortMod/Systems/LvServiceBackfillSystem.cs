// LvServiceBackfillSystem - brings base stations placed before the LV service (1.0.4: a vanilla pole, an ElectricalBox02 and a riser on
// each base station, PIPELINE §6 "LV service pole") up to date, once per game load:
//   1. Wire backfill. A player LV line already wired to a station's LV stub keeps its old mid-air wire ends until its edge is Updated
//      (LaneSystem rebuilds an edge's lanes, and re-runs FindAnchors, only then). The system tags each such edge Updated, plus its two
//      end nodes (the redraw set, as LiftReconnectSystem). Only edges with an aerial wire of the anchor lane prefab and no wire end on an
//      insulator yet, so a fixed save does nothing on its next load.
//   2. Riser backfill (RiserBackfill). The riser is a two-point sub-lane of the station prefab, and lanes of an owner are built only on
//      Created / Updated. A station with fewer UtilityLane sub-lanes than its prefab's two-point utility rows is tagged Updated once.
//      After the rebuild the counts match, so it is one-time without persisted state.
// Scheduling (load-bearing): UpdateAfter<LvServiceBackfillSystem, LiftReconnectSystem> at Modification4. LiftReconnectSystem sits right
// before Game.Net.LaneSystem, so the tags land before LaneSystem in the same frame (immediate EntityManager writes; a barrier would defer
// them past it), and after LiftReconnectSystem, which therefore never sees the station tag as a station event (no reconnect poke, no
// chair full pass). Systems earlier in the frame (SubObjectSystem at Modification2B, InitializeSystem at Modification2) do not see the
// tag either: the station keeps its sub-objects and sub-nets.
// Engine notes: EngineNotes/LvServiceBackfillSystem.md

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
    public sealed partial class LvServiceBackfillSystem : GameSystemBase
    {
        /// <summary>Part 2 (the one-time station Updated) is on. Off = the wire backfill only, and stations placed before 1.0.4 show the
        /// pole and box without the riser (the P1 fallback, lv-connect PLAN §3b).</summary>
        internal static readonly bool RiserBackfill = true;

        /// <summary>The service anchors are the single-point LV wire rows with these node indexes (gate LV-7).</summary>
        internal const int FirstAnchorIndex = 10, LastAnchorIndex = 12;

        /// <summary>A wire end this close to an insulator anchor counts as landed (FindAnchors puts it on the anchor exactly).</summary>
        private const float LandedM = 0.3f;

        /// <summary>Frames to wait for the station prefab set after a load before giving up for this load.</summary>
        private const int MaxWaitFrames = 600;

        private PowerBridgeSystem m_PowerBridgeSystem;
        private EntityQuery m_StationQuery;
        private bool m_Pending;
        private int m_WaitFrames;
        private bool m_ErrorLogged;

        private readonly List<Entity> m_Stations = new List<Entity>();
        private readonly List<float3> m_Anchors = new List<float3>();
        private readonly HashSet<Entity> m_NodesToWalk = new HashSet<Entity>();
        private readonly HashSet<Entity> m_Edges = new HashSet<Entity>();
        private readonly HashSet<Entity> m_Tags = new HashSet<Entity>();

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PowerBridgeSystem = World.GetOrCreateSystemManaged<PowerBridgeSystem>();
            m_StationQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<Game.Net.SubNet>(),
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            Mod.Log.Info($"{nameof(LvServiceBackfillSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Pending = mode == GameMode.Game;
            m_WaitFrames = 0;
        }

        protected override void OnUpdate()
        {
            if (!m_Pending)
                return;
            try
            {
                HashSet<Entity> stationPrefabs = m_PowerBridgeSystem.GetResolvedStationPrefabs();
                if (stationPrefabs.Count == 0)
                {
                    if (++m_WaitFrames >= MaxWaitFrames)
                    {
                        m_Pending = false;
                        Mod.Log.Warn($"LvServiceBackfill: no station prefab set {MaxWaitFrames} frames after the load; skipped for this load");
                    }
                    return;
                }
                m_Pending = false;
                Run(stationPrefabs);
            }
            catch (Exception e)
            {
                m_Pending = false;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(LvServiceBackfillSystem)} EXCEPTION - LV service backfill skipped. Please report this stack:\n{e}");
                }
            }
        }

        private void Run(HashSet<Entity> stationPrefabs)
        {
            m_Stations.Clear();
            using (NativeArray<Entity> all = m_StationQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < all.Length; i++)
                {
                    if (EntityManager.TryGetComponent(all[i], out PrefabRef pr) && stationPrefabs.Contains(pr.m_Prefab))
                        m_Stations.Add(all[i]);
                }
            }

            int serviced = 0, risers = 0, edges = 0;
            m_Tags.Clear();
            for (int i = 0; i < m_Stations.Count; i++)
            {
                Entity station = m_Stations[i];
                Entity prefab = EntityManager.GetComponentData<PrefabRef>(station).m_Prefab;
                if (!ServiceAnchors(station, prefab, out Entity anchorLane, out int twoPointRows))
                    continue;   // a top station, or a prefab without the LV service rows
                serviced++;

                if (RiserBackfill && UtilityLaneCount(station) < twoPointRows)
                {
                    m_Tags.Add(station);
                    risers++;
                }

                CollectForeignLvEdges(station);
                foreach (Entity edge in m_Edges)
                {
                    if (!HasAerialWire(edge, anchorLane, out bool landed) || landed)
                        continue;
                    m_Tags.Add(edge);
                    if (EntityManager.TryGetComponent(edge, out Edge topology))
                    {
                        if (topology.m_Start != Entity.Null) m_Tags.Add(topology.m_Start);
                        if (topology.m_End != Entity.Null) m_Tags.Add(topology.m_End);
                    }
                    edges++;
                }
            }

            foreach (Entity e in m_Tags)
            {
                if (EntityManager.Exists(e) && !EntityManager.HasComponent<Updated>(e))
                    EntityManager.AddComponent<Updated>(e);
            }
            if (risers > 0 || edges > 0)
                Mod.Log.Info($"LvServiceBackfill: {serviced} base station(s) with the LV service; {risers} riser backfill(s), " +
                             $"{edges} LV edge(s) re-anchored ({m_Tags.Count} entities tagged Updated)");
            m_Stations.Clear();
            m_Edges.Clear();
            m_Tags.Clear();
        }

        /// <summary>World positions of the station's service anchors into m_Anchors (single-point utility rows with node index
        /// FirstAnchorIndex..LastAnchorIndex, LaneSystem.FindAnchors takes curve.a), their lane prefab, and the count of two-point
        /// utility rows (the lane entities the station should own). False = no service anchors.</summary>
        private bool ServiceAnchors(Entity station, Entity prefab, out Entity anchorLane, out int twoPointRows)
        {
            anchorLane = Entity.Null;
            twoPointRows = 0;
            m_Anchors.Clear();
            if (!EntityManager.TryGetBuffer(prefab, isReadOnly: true, out DynamicBuffer<Game.Prefabs.SubLane> rows) ||
                !EntityManager.TryGetComponent(station, out Game.Objects.Transform transform))
                return false;
            for (int i = 0; i < rows.Length; i++)
            {
                Game.Prefabs.SubLane row = rows[i];
                if (!EntityManager.HasComponent<UtilityLaneData>(row.m_Prefab))
                    continue;
                if (row.m_NodeIndex.x != row.m_NodeIndex.y)
                {
                    twoPointRows++;
                    continue;
                }
                if (row.m_NodeIndex.x < FirstAnchorIndex || row.m_NodeIndex.x > LastAnchorIndex)
                    continue;
                anchorLane = row.m_Prefab;
                m_Anchors.Add(Game.Objects.ObjectUtils.LocalToWorld(transform, row.m_Curve.a));
            }
            return m_Anchors.Count > 0;
        }

        private int UtilityLaneCount(Entity station)
        {
            if (!EntityManager.TryGetBuffer(station, isReadOnly: true, out DynamicBuffer<Game.Net.SubLane> lanes))
                return 0;
            int n = 0;
            for (int i = 0; i < lanes.Length; i++)
            {
                Entity lane = lanes[i].m_SubLane;
                if (EntityManager.HasComponent<Game.Net.UtilityLane>(lane) &&!EntityManager.HasComponent<Deleted>(lane))
                    n++;
            }
            return n;
        }

        /// <summary>m_Edges = the LV edges the station does not own that end at a node of its LV stub (the AtNode wires of
        /// StationLvStubProbe).</summary>
        private void CollectForeignLvEdges(Entity station)
        {
            m_Edges.Clear();
            m_NodesToWalk.Clear();
            if (!EntityManager.TryGetBuffer(station, isReadOnly: true, out DynamicBuffer<Game.Net.SubNet> subNetBuffer))
                return;
            var subNets = new List<Entity>(subNetBuffer.Length);
            for (int i = 0; i < subNetBuffer.Length; i++)
                subNets.Add(subNetBuffer[i].m_SubNet);
            foreach (Entity sub in subNets)
            {
                if (!IsLv(sub) || EntityManager.HasComponent<Temp>(sub))
                    continue;
                if (EntityManager.TryGetComponent(sub, out Edge stub))
                {
                    if (stub.m_Start != Entity.Null) m_NodesToWalk.Add(stub.m_Start);
                    if (stub.m_End != Entity.Null) m_NodesToWalk.Add(stub.m_End);
                }
                else if (EntityManager.HasComponent<Node>(sub))
                {
                    m_NodesToWalk.Add(sub);
                }
            }
            foreach (Entity node in m_NodesToWalk)
            {
                if (!EntityManager.TryGetBuffer(node, isReadOnly: true, out DynamicBuffer<ConnectedEdge> connected))
                    continue;
                var list = new List<Entity>(connected.Length);
                for (int i = 0; i < connected.Length; i++)
                    list.Add(connected[i].m_Edge);
                foreach (Entity edge in list)
                {
                    if (edge == Entity.Null || EntityManager.HasComponent<Temp>(edge) || !IsLv(edge))
                        continue;
                    if (EntityManager.TryGetComponent(edge, out Owner owner) && owner.m_Owner == station)
                        continue;
                    m_Edges.Add(edge);
                }
            }
        }

        /// <summary>True when the edge has a wire lane of the anchor lane prefab (an aerial LV line; an underground cable draws another
        /// lane and never takes these anchors). <paramref name="landed"/>: one of its wire ends is on a service anchor already.</summary>
        private bool HasAerialWire(Entity edge, Entity anchorLane, out bool landed)
        {
            landed = false;
            bool aerial = false;
            if (!EntityManager.TryGetBuffer(edge, isReadOnly: true, out DynamicBuffer<Game.Net.SubLane> lanes))
                return false;
            for (int i = 0; i < lanes.Length && !landed; i++)
            {
                Entity lane = lanes[i].m_SubLane;
                if (!EntityManager.TryGetComponent(lane, out PrefabRef pr) || pr.m_Prefab != anchorLane ||
                    !EntityManager.TryGetComponent(lane, out Curve curve))
                    continue;
                aerial = true;
                for (int k = 0; k < m_Anchors.Count; k++)
                {
                    if (math.distance(curve.m_Bezier.a, m_Anchors[k]) <= LandedM || math.distance(curve.m_Bezier.d, m_Anchors[k]) <= LandedM)
                    {
                        landed = true;
                        break;
                    }
                }
            }
            return aerial;
        }

        private bool IsLv(Entity net)
        {
            return EntityManager.TryGetComponent(net, out PrefabRef pr) &&
                   EntityManager.TryGetComponent(pr.m_Prefab, out ElectricityConnectionData c) &&
                   c.m_Voltage == Game.Prefabs.ElectricityConnection.Voltage.Low &&
                   !EntityManager.HasComponent<Deleted>(net);
        }
    }
}
