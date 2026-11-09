// The LV stub of one placed station, read on the main thread: its edge curve and its wire state (StationLvRule.Classify).
// Shared by StationLvIconSystem (the vanilla icon clean-up) and StationLvMarkerSystem (the overlay mark), so both see the same wire.
// The stub = the station's own LV sub-net (Owner == station, net prefab ElectricityConnectionData.m_Voltage Low,
// Game.Prefabs/ElectricityConnection.cs:13). A foreign LV edge ending at a stub node = AtNode; a foreign LV node in the stub edge's
// ConnectedNode buffer (a wire end local-connected onto its middle) = MidStub (the why: the StationLvRule.cs header).
// Reads only: SubNet, Edge, ConnectedNode, ConnectedEdge, Curve, Owner, PrefabRef, Deleted. A few component reads per station.

using System.Collections.Generic;
using Colossal.Entities;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public static class StationLvStubProbe
    {
        /// <summary>The station's LV stub: <paramref name="curve"/> is its first LV stub edge's curve (world), and the wire state with
        /// its counts. False when the station has no LV stub edge (a top station, or a station not built yet).</summary>
        public static bool TryProbe(EntityManager em, Entity station, out Bezier4x3 curve, out LvStubWire wire, out int atNode, out int mid)
        {
            curve = default;
            wire = LvStubWire.None;
            atNode = mid = 0;
            if (!em.TryGetBuffer(station, isReadOnly: true, out DynamicBuffer<Game.Net.SubNet> buffer))
                return false;
            // Copy first: a buffer handle must not be held across other EntityManager reads.
            var subNets = new List<Entity>(buffer.Length);
            for (int i = 0; i < buffer.Length; i++)
                subNets.Add(buffer[i].m_SubNet);
            bool found = false;
            for (int i = 0; i < subNets.Count; i++)
            {
                Entity sub = subNets[i];
                if (!IsLv(em, sub))
                    continue;
                if (em.HasComponent<Edge>(sub))
                {
                    if (!found && em.TryGetComponent(sub, out Curve c))
                    {
                        curve = c.m_Bezier;
                        found = true;
                    }
                    if (!em.TryGetBuffer(sub, isReadOnly: true, out DynamicBuffer<ConnectedNode> nodes))
                        continue;
                    var list = new List<Entity>(nodes.Length);
                    for (int k = 0; k < nodes.Length; k++)
                        list.Add(nodes[k].m_Node);
                    for (int k = 0; k < list.Count; k++)
                    {
                        if (!OwnedBy(em, list[k], station) && IsLv(em, list[k]))
                            mid++;
                    }
                }
                else if (em.TryGetBuffer(sub, isReadOnly: true, out DynamicBuffer<ConnectedEdge> edges))
                {
                    var list = new List<Entity>(edges.Length);
                    for (int k = 0; k < edges.Length; k++)
                        list.Add(edges[k].m_Edge);
                    for (int k = 0; k < list.Count; k++)
                    {
                        if (!OwnedBy(em, list[k], station) && IsLv(em, list[k]))
                            atNode++;
                    }
                }
            }
            wire = StationLvRule.Classify(atNode, mid);
            return found;
        }

        private static bool IsLv(EntityManager em, Entity net)
        {
            return em.TryGetComponent(net, out PrefabRef pr) &&
                   em.TryGetComponent(pr.m_Prefab, out ElectricityConnectionData c) &&
                   c.m_Voltage == Game.Prefabs.ElectricityConnection.Voltage.Low &&
                   !em.HasComponent<Deleted>(net);
        }

        private static bool OwnedBy(EntityManager em, Entity net, Entity station)
        {
            return em.TryGetComponent(net, out Owner owner) && owner.m_Owner == station;
        }
    }
}
