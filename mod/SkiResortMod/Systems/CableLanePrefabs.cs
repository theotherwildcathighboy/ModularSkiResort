// Cached lookup that answers "is this lane prefab a high-voltage utility lane". The two lane harvests share it (ChairPinSystem's loop
// build, TramShuttleSystem.HarvestLanes).
// The harvests walk every utility lane in the city (Game.Prefabs/UtilityLane.cs:51). There are few lane PREFABS, so the cache holds the
// answer per prefab and the code tests it first.
// Only lanes of an HV prefab go on to the owner test. The code runs on the main thread. ChairPinSystem clears the cache on each load.

using System.Collections.Generic;
using Colossal.Entities;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    internal static class CableLanePrefabs
    {
        private static readonly Dictionary<Entity, bool> s_IsHighVoltage = new Dictionary<Entity, bool>();

        public static void Clear()
        {
            s_IsHighVoltage.Clear();
        }

        /// <summary>True when the lane prefab carries UtilityLaneData with the HighVoltageLine flag.</summary>
        public static bool IsHighVoltage(EntityManager em, Entity lanePrefab)
        {
            if (s_IsHighVoltage.TryGetValue(lanePrefab, out bool hv))
                return hv;
            hv = em.TryGetComponent<UtilityLaneData>(lanePrefab, out var utility) &&
                 (utility.m_UtilityTypes & UtilityTypes.HighVoltageLine) != 0;
            s_IsHighVoltage[lanePrefab] = hv;
            return hv;
        }
    }
}
