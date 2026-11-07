// Scoped cable-lane harvest shared by ChairPinSystem (Loop lifts) and TramShuttleSystem (Shuttle lifts). It reads the SubLane buffers of
// the census's line edges and end nodes instead of scanning every utility lane in the city, so cost scales with lift edges. It applies the
// full scan's filter (live lane, UtilityLane, HV cable prefab, owner is a lift line of the mover kind, Lane + Curve). A lane reachable from
// both an edge and its node is kept once.
// Both paths sort by lane entity (SortByLane): the full scan's chunk order moves when a lane changes archetype.
// Callers run the full scan once per load beside the first scoped harvest and Compare them. A mismatch is a WARN, and that caller stays on
// the full scan.

using System.Collections.Generic;
using System.Text;
using Colossal.Entities;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    internal static class LiftLaneHarvest
    {
        /// <summary>The lanes in <paramref name="owners"/>' SubLane buffers that pass the full scan's filter for <paramref name="mover"/>
        /// lines, appended to <paramref name="into"/> (not cleared, not sorted). Returns the SubLane entries visited.</summary>
        public static int Scoped(EntityManager em, List<Entity> owners, IReadOnlyDictionary<Entity, LiftTypeDef> lineTypes,
            MoverKind mover, List<LiftLoopCache.CableLane> into, HashSet<Entity> seen)
        {
            seen.Clear();
            int visited = 0;
            for (int o = 0; o < owners.Count; o++)
            {
                if (!em.TryGetBuffer(owners[o], isReadOnly: true, out DynamicBuffer<Game.Net.SubLane> subLanes))
                    continue;
                for (int k = 0; k < subLanes.Length; k++)
                {
                    Entity lane = subLanes[k].m_SubLane;
                    visited++;
                    if (!seen.Add(lane))
                        continue;
                    if (TryCableLane(em, lane, lineTypes, mover, out LiftLoopCache.CableLane cable))
                        into.Add(cable);
                }
            }
            return visited;
        }

        /// <summary>The full scan's filter for one lane entity.</summary>
        public static bool TryCableLane(EntityManager em, Entity lane, IReadOnlyDictionary<Entity, LiftTypeDef> lineTypes,
            MoverKind mover, out LiftLoopCache.CableLane cable)
        {
            cable = default;
            if (lane == Entity.Null || !em.Exists(lane) || em.HasComponent<Deleted>(lane) || em.HasComponent<Temp>(lane) ||
                !em.HasComponent<Game.Net.UtilityLane>(lane))
                return false;
            if (!em.TryGetComponent<PrefabRef>(lane, out var lanePrefab) || !CableLanePrefabs.IsHighVoltage(em, lanePrefab.m_Prefab))
                return false;
            if (!em.TryGetComponent<Owner>(lane, out var owner) || owner.m_Owner == Entity.Null ||
                !em.TryGetComponent<PrefabRef>(owner.m_Owner, out var ownerPrefab) ||
                !lineTypes.TryGetValue(ownerPrefab.m_Prefab, out LiftTypeDef lineDef) || lineDef == null || lineDef.Mover != mover)
                return false;
            if (!em.TryGetComponent<Game.Net.Lane>(lane, out var laneData) || !em.TryGetComponent<Curve>(lane, out var curve))
                return false;
            cable = new LiftLoopCache.CableLane
            {
                m_Lane = lane,
                m_Owner = owner.m_Owner,
                m_Bezier = curve.m_Bezier,
                m_Start = laneData.m_StartNode,
                m_End = laneData.m_EndNode,
                m_Length = curve.m_Length,
                m_TypeIndex = lineDef.Index,
            };
            return true;
        }

        /// <summary>Sorts by lane entity (Index, then Version).</summary>
        public static void SortByLane(List<LiftLoopCache.CableLane> lanes)
        {
            lanes.Sort((a, b) =>
            {
                int c = a.m_Lane.Index.CompareTo(b.m_Lane.Index);
                return c != 0 ? c : a.m_Lane.Version.CompareTo(b.m_Lane.Version);
            });
        }

        /// <summary>Lanes only the full scan kept (<paramref name="missed"/>) and lanes only the scoped harvest kept
        /// (<paramref name="extra"/>), via LiftLaneScope.CompareHarvests.</summary>
        public static void Compare(List<LiftLoopCache.CableLane> full, List<LiftLoopCache.CableLane> scoped,
            List<Entity> missed, List<Entity> extra)
        {
            var fullIds = new List<Entity>(full.Count);
            for (int i = 0; i < full.Count; i++)
                fullIds.Add(full[i].m_Lane);
            var scopedIds = new List<Entity>(scoped.Count);
            for (int i = 0; i < scoped.Count; i++)
                scopedIds.Add(scoped[i].m_Lane);
            LiftLaneScope.CompareHarvests(fullIds, scopedIds, missed, extra);
        }

        /// <summary>"lane i:v (owner i:v)" for the first <paramref name="max"/> lanes, for log text.</summary>
        public static string Describe(EntityManager em, List<Entity> lanes, int max)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < lanes.Count && i < max; i++)
            {
                if (sb.Length > 0)
                    sb.Append(", ");
                Entity lane = lanes[i];
                sb.Append(lane.Index).Append(':').Append(lane.Version);
                if (em.TryGetComponent<Owner>(lane, out var owner))
                    sb.Append(" (owner ").Append(owner.m_Owner.Index).Append(':').Append(owner.m_Owner.Version).Append(')');
            }
            if (lanes.Count > max)
                sb.Append(", … ").Append(lanes.Count - max).Append(" more");
            return sb.ToString();
        }
    }
}
