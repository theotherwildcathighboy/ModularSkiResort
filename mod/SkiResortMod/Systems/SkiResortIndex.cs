// THE "which resort is this point in" rule for lifts and stations: the nearest live Ski Area Welcome Sign whose distance is
// <= the tether (SkiRunOwnershipSystem.TetherMeters, 5000 m); none → not linked.
// Count / SignAt / PositionAt are sorted by Index, then Version (EntityRef.CompareTo): the stable order the hub numbers
// "Ski area N" by when a sign has no name. The scan replaces the best only on a STRICTLY smaller distance, so an exact tie keeps
// the lower Index / Version.
// Distances are float math.distance on xz, the same arithmetic as the run bind test (SkiRunOwnershipSystem.TryNearestGateEntity,
// `d <= TetherM`); runs never go through this index, so the bind test's tie order (chunk order) never disagrees.
// Pure file: System, System.Collections.Generic, Unity.Mathematics only (tests/ResortBatch links it).

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed class SkiResortIndex
    {
        private readonly List<EntityRef> m_Signs = new List<EntityRef>(4);
        private readonly List<float2> m_Positions = new List<float2>(4);

        public int Count => m_Signs.Count;

        public void Clear()
        {
            m_Signs.Clear();
            m_Positions.Clear();
        }

        /// <summary>Inserts (or moves) a sign, keeping Index/Version order. O(signs).</summary>
        public void Add(EntityRef sign, float2 xz)
        {
            int at = 0;
            while (at < m_Signs.Count)
            {
                int c = m_Signs[at].CompareTo(sign);
                if (c == 0)
                {
                    m_Positions[at] = xz;
                    return;
                }
                if (c > 0)
                    break;
                at++;
            }
            m_Signs.Insert(at, sign);
            m_Positions.Insert(at, xz);
        }

        public EntityRef SignAt(int i)
        {
            return m_Signs[i];
        }

        public float2 PositionAt(int i)
        {
            return m_Positions[i];
        }

        /// <summary>Position of <paramref name="sign"/> in the stable order, -1 when absent.</summary>
        public int IndexOf(EntityRef sign)
        {
            for (int i = 0; i < m_Signs.Count; i++)
            {
                if (m_Signs[i] == sign)
                    return i;
            }
            return -1;
        }

        public bool TryResolve(float2 xz, float maxDistance, out EntityRef sign, out float distance)
        {
            sign = EntityRef.Null;
            distance = float.PositiveInfinity;
            if (!math.all(math.isfinite(xz)))
                return false;
            int best = -1;
            for (int i = 0; i < m_Signs.Count; i++)
            {
                float d = math.distance(xz, m_Positions[i]);
                if (d < distance)
                {
                    distance = d;
                    best = i;
                }
            }
            if (best < 0 || !(distance <= maxDistance))
                return false;
            sign = m_Signs[best];
            return true;
        }
    }
}
