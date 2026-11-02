// Prototype for switching city utilities off and back on for a live building (docs/research/utility-switch-2026-11-01.md).
// comp edits one entity by component name, util reads what the test needs in one call, offgrid runs the candidate rule on a listed
// set of buildings every frame, prefabFlags stands in for a road-optional catalog row, autosave protects existing autosaves.

using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Notifications;
using Game.Prefabs;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;

namespace SkiTestBridge
{
    /// <summary>Runs the candidate rule in Modification1, where the real rule would have to run too.</summary>
    public partial class TestBridgeOffGridSystem : GameSystemBase
    {
        private TestBridgeSystem m_Bridge;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Bridge = World.GetOrCreateSystemManaged<TestBridgeSystem>();
        }

        protected override void OnUpdate()
        {
            m_Bridge.TickOffGrid();
        }
    }

    public partial class TestBridgeSystem
    {
        // Road-only services. TelecomConsumer stays: coverage does not depend on a road edge (TelecomEfficiencySystem).
        private static readonly string[] kOffGridComponents =
        {
            "Game.Buildings.ElectricityConsumer",
            "Game.Buildings.WaterConsumer",
            "Game.Buildings.GarbageProducer",
            "Game.Buildings.MailProducer",
            "Game.Buildings.CrimeProducer",
        };

        // Their writers run only for entities with the consumer, so a removal leaves the last value behind unless it is reset here.
        private static readonly EfficiencyFactor[] kOffGridFactors =
        {
            EfficiencyFactor.ElectricitySupply,
            EfficiencyFactor.ElectricityFee,
            EfficiencyFactor.WaterSupply,
            EfficiencyFactor.DirtyWater,
            EfficiencyFactor.SewageHandling,
            EfficiencyFactor.WaterFee,
            EfficiencyFactor.Garbage,
            EfficiencyFactor.Mail,
        };

        private static readonly string[] kUtilRead =
        {
            "Game.Buildings.Building",
            "Game.Buildings.ElectricityConsumer",
            "Game.Buildings.WaterConsumer",
            "Game.Buildings.GarbageProducer",
            "Game.Buildings.MailProducer",
            "Game.Buildings.CrimeProducer",
            "Game.Buildings.TelecomConsumer",
            "Game.Buildings.Efficiency",
            "Game.Companies.WorkProvider",
            "Game.Companies.Employee",
            "Game.Objects.Transform",
        };

        private readonly List<Entity> m_OffGrid = new List<Entity>();
        private readonly JArray m_OffGridEvents = new JArray();

        private Entity ParseEntity(string text)
        {
            string[] id = text.Split(':');
            var entity = new Entity { Index = int.Parse(id[0]), Version = int.Parse(id[1]) };
            if (!EntityManager.Exists(entity))
                throw new ArgumentException($"entity {text} does not exist");
            return entity;
        }

        /// <summary>{id, remove: [names], add: [names], eff: {factor: value}, clearIcons, updated}: applied in that order; replies with util.</summary>
        private JObject EditComponents(JObject args)
        {
            Entity entity = ParseEntity((string)args["id"]);
            foreach (string name in Names(args["remove"]))
                EntityManager.RemoveComponent(entity, ComponentType.ReadWrite(FindType(name)));
            foreach (string name in Names(args["add"]))
                EntityManager.AddComponent(entity, ComponentType.ReadWrite(FindType(name)));
            if (args["eff"] is JObject factors && EntityManager.HasBuffer<Efficiency>(entity))
            {
                DynamicBuffer<Efficiency> buffer = EntityManager.GetBuffer<Efficiency>(entity);
                foreach (KeyValuePair<string, JToken> pair in factors)
                    BuildingUtils.SetEfficiencyFactor(buffer, (EfficiencyFactor)Enum.Parse(typeof(EfficiencyFactor), pair.Key), (float)pair.Value);
            }
            if ((bool?)args["clearIcons"] ?? false)
                ClearUtilityIcons(entity);
            if ((bool?)args["updated"] ?? false)
                EntityManager.AddComponent<Updated>(entity);
            return ReadUtility(entity);
        }

        /// <summary>{id}: the utility state of a building: consumers, road edge, efficiency factors, icons by prefab name, jobs.</summary>
        private JObject UtilityCommand(JObject args)
        {
            return ReadUtility(ParseEntity((string)args["id"]));
        }

        private JObject ReadUtility(Entity entity)
        {
            var result = new JObject { ["entity"] = EntityId(entity), ["prefab"] = PrefabNameOf(entity) };
            foreach (string name in kUtilRead)
            {
                Type type = FindType(name);
                if (EntityManager.HasComponent(entity, ComponentType.ReadOnly(type)))
                    result[type.Name] = ReadComponent(entity, type);
            }
            var icons = new JArray();
            if (EntityManager.HasBuffer<IconElement>(entity))
            {
                foreach (IconElement icon in EntityManager.GetBuffer<IconElement>(entity, isReadOnly: true))
                    icons.Add(PrefabNameOf(icon.m_Icon));
            }
            result["icons"] = icons;
            return result;
        }

        private void ClearUtilityIcons(Entity entity)
        {
            IconCommandBuffer icons = World.GetOrCreateSystemManaged<IconCommandSystem>().CreateCommandBuffer();
            var electricity = SingletonOf<ElectricityParameterData>();
            var water = SingletonOf<WaterPipeParameterData>();
            var garbage = SingletonOf<GarbageParameterData>();
            icons.Remove(entity, electricity.m_ElectricityNotificationPrefab);
            icons.Remove(entity, electricity.m_BuildingBottleneckNotificationPrefab);
            icons.Remove(entity, water.m_WaterNotification);
            icons.Remove(entity, water.m_DirtyWaterNotification);
            icons.Remove(entity, water.m_SewageNotification);
            icons.Remove(entity, garbage.m_GarbageNotificationPrefab);
            icons.Remove(entity, SingletonOf<TrafficConfigurationData>().m_RoadConnectionNotification);
        }

        private T SingletonOf<T>() where T : unmanaged, IComponentData
        {
            EntityQuery query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
            try
            {
                return query.GetSingleton<T>();
            }
            finally
            {
                query.Dispose();
            }
        }

        /// <summary>
        /// {add: [ids], remove: [ids], clear, clearEvents}: the buildings the candidate rule watches. Replies with the list and every
        /// switch so far with its sim frame, so a component that vanilla re-adds shows up as a second removal.
        /// </summary>
        private JObject OffGridCommand(JObject args)
        {
            if ((bool?)args["clear"] ?? false)
                m_OffGrid.Clear();
            foreach (string id in Names(args["add"]))
            {
                Entity entity = ParseEntity(id);
                if (!m_OffGrid.Contains(entity))
                    m_OffGrid.Add(entity);
            }
            foreach (string id in Names(args["remove"]))
                m_OffGrid.Remove(ParseEntity(id));
            if ((bool?)args["clearEvents"] ?? false)
                m_OffGridEvents.Clear();
            return new JObject
            {
                ["watching"] = new JArray(m_OffGrid.Select(EntityId).Cast<object>().ToArray()),
                ["events"] = m_OffGridEvents.DeepClone(),
            };
        }

        /// <summary>The candidate rule: no road edge means no road-only services; a road edge brings them back through vanilla's own path.</summary>
        internal void TickOffGrid()
        {
            if (m_OffGrid.Count == 0)
                return;
            for (int i = m_OffGrid.Count - 1; i >= 0; i--)
            {
                Entity entity = m_OffGrid[i];
                if (!EntityManager.Exists(entity) || !EntityManager.HasComponent<Building>(entity) || EntityManager.HasComponent<Deleted>(entity))
                {
                    LogOffGrid(entity, "gone", null);
                    m_OffGrid.RemoveAt(i);
                    continue;
                }
                bool road = EntityManager.GetComponentData<Building>(entity).m_RoadEdge != Entity.Null;
                if (road)
                {
                    // InitializeSystem re-adds power, water and garbage. Crime comes only at Created, and mail only with RequireRoad on the prefab,
                    // so each is checked on its own: a relocation onto a road re-adds the first three without this branch.
                    if (!EntityManager.HasComponent<ElectricityConsumer>(entity) && !EntityManager.HasComponent<Updated>(entity))
                    {
                        EntityManager.AddComponent<Updated>(entity);
                        LogOffGrid(entity, "road: Updated", null);
                    }
                    if (!EntityManager.HasComponent<MailProducer>(entity) || !EntityManager.HasComponent<CrimeProducer>(entity))
                    {
                        EntityManager.AddComponent<MailProducer>(entity);
                        EntityManager.AddComponent<CrimeProducer>(entity);
                        LogOffGrid(entity, "road: mail, crime", null);
                    }
                    continue;
                }
                var removed = new JArray();
                foreach (string name in kOffGridComponents)
                {
                    ComponentType type = ComponentType.ReadWrite(FindType(name));
                    if (EntityManager.HasComponent(entity, type))
                    {
                        EntityManager.RemoveComponent(entity, type);
                        removed.Add(type.GetManagedType().Name);
                    }
                }
                if (removed.Count == 0)
                    continue;
                ClearUtilityIcons(entity);
                if (EntityManager.HasBuffer<Efficiency>(entity))
                {
                    DynamicBuffer<Efficiency> buffer = EntityManager.GetBuffer<Efficiency>(entity);
                    foreach (EfficiencyFactor factor in kOffGridFactors)
                        BuildingUtils.SetEfficiencyFactor(buffer, factor, 1f);
                }
                LogOffGrid(entity, "off", removed);
            }
        }

        private void LogOffGrid(Entity entity, string what, JArray removed)
        {
            var row = new JObject { ["frame"] = m_Simulation.frameIndex, ["entity"] = EntityId(entity), ["what"] = what };
            if (removed != null)
                row["removed"] = removed;
            if (m_OffGridEvents.Count < 200)
                m_OffGridEvents.Add(row);
        }

        /// <summary>{prefab, clear: [flag names], set: [flag names]}: edits the prefab's BuildingData.m_Flags for this run of the game only.</summary>
        private JObject PrefabFlags(JObject args)
        {
            Entity prefab = FindPrefabEntity((string)args["prefab"]);
            BuildingData data = EntityManager.GetComponentData<BuildingData>(prefab);
            foreach (string name in Names(args["clear"]))
                data.m_Flags &= ~(Game.Prefabs.BuildingFlags)Enum.Parse(typeof(Game.Prefabs.BuildingFlags), name);
            foreach (string name in Names(args["set"]))
                data.m_Flags |= (Game.Prefabs.BuildingFlags)Enum.Parse(typeof(Game.Prefabs.BuildingFlags), name);
            EntityManager.SetComponentData(prefab, data);
            return new JObject { ["prefab"] = (string)args["prefab"], ["flags"] = data.m_Flags.ToString() };
        }

        /// <summary>{on}: switches the autosave system for this run of the game; no settings file changes. A test on a save would otherwise prune real autosaves.</summary>
        private JObject SetAutoSave(JObject args)
        {
            AutoSaveSystem system = World.GetExistingSystemManaged<AutoSaveSystem>();
            if (args["on"] != null)
                system.Enabled = (bool)args["on"];
            return new JObject { ["enabled"] = system.Enabled };
        }
    }
}
