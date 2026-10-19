// CableLanePrefabs — the cached "is this lane prefab a high-voltage utility lane" lookup shared by the two lane
// harvests (ChairPinSystem's loop build, TramShuttleSystem.HarvestLanes). Code review 2026-10-19, M9: the harvests walk
// every utility lane in the city (water, sewage, power, fences; Game.Prefabs/UtilityLane.cs:51) and did two
// EntityManager reads per lane (owner PrefabRef, then the lane prefab's UtilityLaneData). There are only a handful of
// utility lane PREFABS, so the answer per lane prefab is cached and tested FIRST (a dictionary hit, no EntityManager
// call): only lanes of an HV lane prefab go on to the owner test. Main thread; cleared per load by ChairPinSystem
// (prefab entities are stable within a session, the clear is hygiene).

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

        /// <summary>True when the lane prefab carries UtilityLaneData with the HighVoltageLine flag (the structural test
        /// the harvests always made; the LV comm lane and the haul ropes fail it).</summary>
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
