// Zone spawn probe: records every building the game's ZoneSpawnSystem asks for, and the state that gates it. Read-only.
// ZoneSpawnSystem (GameSimulation, every 16 frames) writes one CreationDefinition per zone type through the EndFrameBarrier.
// GenerateObjectsSystem (Modification1) turns it into the building on the next frame, so the probe runs just before it.
// Op: {op:"spawnprobe", action:"start"|"read"|"stop"}. "read" also lists every system whose name holds "Spawn" or "Demand"
// with its Enabled state, and the demand systems' building demand (the values ZoneSpawnSystem.OnUpdate gates on).

using System;
using System.Collections.Generic;
using Game;
using Game.Objects;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;

namespace SkiTestBridge
{
    public partial class TestBridgeSpawnProbeSystem : GameSystemBase
    {
        internal static bool Armed;
        internal static readonly List<JObject> Seen = new List<JObject>();
        internal static int FramesArmed;
        private EntityQuery m_Definitions;
        private PrefabSystem m_Prefabs;
        private SimulationSystem m_Simulation;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Definitions = GetEntityQuery(ComponentType.ReadOnly<CreationDefinition>(), ComponentType.ReadOnly<ObjectDefinition>());
            m_Prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Simulation = World.GetOrCreateSystemManaged<SimulationSystem>();
        }

        protected override void OnUpdate()
        {
            if (!Armed)
                return;
            FramesArmed++;
            if (m_Definitions.IsEmptyIgnoreFilter)
                return;
            using NativeArray<Entity> entities = m_Definitions.ToEntityArray(Allocator.Temp);
            foreach (Entity e in entities)
            {
                CreationDefinition def = EntityManager.GetComponentData<CreationDefinition>(e);
                if (def.m_Prefab == Entity.Null || !EntityManager.HasComponent<SpawnableBuildingData>(def.m_Prefab))
                    continue;
                ObjectDefinition obj = EntityManager.GetComponentData<ObjectDefinition>(e);
                BuildingData bd = EntityManager.HasComponent<BuildingData>(def.m_Prefab) ? EntityManager.GetComponentData<BuildingData>(def.m_Prefab) : default;
                if (Seen.Count >= 400)
                    Seen.RemoveAt(0);
                Seen.Add(new JObject
                {
                    ["simFrame"] = m_Simulation.frameIndex,
                    ["prefab"] = m_Prefabs.GetPrefabName(def.m_Prefab),
                    ["flags"] = def.m_Flags.ToString(),
                    ["x"] = Math.Round(obj.m_Position.x, 1),
                    ["z"] = Math.Round(obj.m_Position.z, 1),
                    ["lot"] = $"{bd.m_LotSize.x}x{bd.m_LotSize.y}",
                });
            }
        }
    }

    public partial class TestBridgeSystem
    {
        private JObject SpawnProbeCommand(JObject args)
        {
            switch ((string)args["action"] ?? "read")
            {
                case "start":
                    TestBridgeSpawnProbeSystem.Seen.Clear();
                    TestBridgeSpawnProbeSystem.FramesArmed = 0;
                    TestBridgeSpawnProbeSystem.Armed = true;
                    break;
                case "stop":
                    TestBridgeSpawnProbeSystem.Armed = false;
                    break;
            }
            var systems = new JArray();
            foreach (ComponentSystemBase system in World.Systems)
            {
                Type t = system.GetType();
                if (t.Name.IndexOf("Spawn", StringComparison.Ordinal) >= 0 || t.Name.IndexOf("Demand", StringComparison.Ordinal) >= 0)
                    systems.Add($"{t.FullName}: {(system.Enabled ? "on" : "OFF")}");
            }
            var res = World.GetOrCreateSystemManaged<ResidentialDemandSystem>();
            var com = World.GetOrCreateSystemManaged<CommercialDemandSystem>();
            var ind = World.GetOrCreateSystemManaged<IndustrialDemandSystem>();
            var zs = World.GetOrCreateSystemManaged<ZoneSpawnSystem>();
            return new JObject
            {
                ["armed"] = TestBridgeSpawnProbeSystem.Armed,
                ["framesArmed"] = TestBridgeSpawnProbeSystem.FramesArmed,
                ["zoneSpawnEnabled"] = zs.Enabled,
                ["debugFastSpawn"] = zs.debugFastSpawn,
                ["residentialBuildingDemand"] = $"{res.buildingDemand.x},{res.buildingDemand.y},{res.buildingDemand.z}",
                ["householdDemand"] = res.householdDemand,
                ["commercialBuildingDemand"] = com.buildingDemand,
                ["industrialBuildingDemand"] = ind.industrialBuildingDemand,
                ["officeBuildingDemand"] = ind.officeBuildingDemand,
                ["storageBuildingDemand"] = ind.storageBuildingDemand,
                ["spawns"] = new JArray(TestBridgeSpawnProbeSystem.Seen),
                ["systems"] = systems,
            };
        }
    }
}
