// P0 probe for the merge-asset feature (docs/research/merge-asset-into-resort-2026-11-02.md, section 6). mprobe tags a building with
// the probe mod's saved component (mergetag/, a separate assembly so a save can load without it), lists tagged buildings, and traces
// one building's AttractivenessProvider: TestBridgeMergeProbeSystem runs right after vanilla AttractionSystem with its interval, reads
// the value on the tick that wrote the building's update frame (V), and writes round(sqrt(V^2 + P^2)); the bridge's UIUpdate samples
// the value every frame, so a flicker of vanilla's own value would show.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Colossal.Entities;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Objects;
using Game.Simulation;
using Game.Tools;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiTestBridge
{
    /// <summary>Anchored after AttractionSystem with the same interval (Mod.cs), as SkiResortAttractivenessSystem is.</summary>
    public partial class TestBridgeMergeProbeSystem : GameSystemBase
    {
        private TestBridgeSystem m_Bridge;

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 16;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Bridge = World.GetOrCreateSystemManaged<TestBridgeSystem>();
        }

        protected override void OnUpdate()
        {
            m_Bridge.TickMergeProbe();
        }
    }

    public partial class TestBridgeSystem
    {
        private const string kTagType = "SkiTestMergeTag.MergeProbeTag";
        private const int kTraceRows = 400;

        private static readonly MethodInfo s_SetComponent = typeof(EntityManager).GetMethods()
            .First(m => m.Name == nameof(EntityManager.SetComponentData) && m.IsGenericMethod
                && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(Entity));

        private Entity m_TraceEntity = Entity.Null;
        private int m_TraceP;
        private bool m_TraceWrite;
        private bool m_TraceHasV;
        private int m_TraceV;
        private readonly List<JObject> m_TraceSim = new List<JObject>();
        private readonly Dictionary<int, int> m_TraceUiValues = new Dictionary<int, int>();
        private int m_TraceUiFrames, m_TraceUiChanges, m_TraceUiLast = int.MinValue;
        private readonly List<JObject> m_TraceUiChangeRows = new List<JObject>();

        /// <summary>{action: tag {id, serial} | list | trace {id, p, write} | read | stop}.</summary>
        private JObject MergeProbe(JObject args)
        {
            string action = (string)args["action"] ?? "list";
            switch (action)
            {
                case "tag":
                {
                    Entity entity = ParseEntity((string)args["id"]);
                    Type type = FindType(kTagType);
                    if (!EntityManager.HasComponent(entity, ComponentType.ReadWrite(type)))
                        EntityManager.AddComponent(entity, ComponentType.ReadWrite(type));
                    object value = Activator.CreateInstance(type);
                    type.GetField("m_Version").SetValue(value, (byte)1);
                    type.GetField("m_Serial").SetValue(value, (int?)args["serial"] ?? 1);
                    s_SetComponent.MakeGenericMethod(type).Invoke(EntityManager, new[] { (object)entity, value });
                    if ((bool?)args["provider"] ?? false)
                    {
                        if (!EntityManager.HasComponent<AttractivenessProvider>(entity))
                            EntityManager.AddComponent<AttractivenessProvider>(entity);
                    }
                    return ListTagged();
                }
                case "untag":
                {
                    Entity entity = ParseEntity((string)args["id"]);
                    EntityManager.RemoveComponent(entity, ComponentType.ReadWrite(FindType(kTagType)));
                    return ListTagged();
                }
                case "list":
                    return ListTagged();
                case "trace":
                    m_TraceEntity = ParseEntity((string)args["id"]);
                    m_TraceP = (int?)args["p"] ?? 0;
                    m_TraceWrite = (bool?)args["write"] ?? true;
                    m_TraceHasV = false;
                    m_TraceSim.Clear();
                    m_TraceUiValues.Clear();
                    m_TraceUiChangeRows.Clear();
                    m_TraceUiFrames = m_TraceUiChanges = 0;
                    m_TraceUiLast = int.MinValue;
                    return TraceState();
                case "read":
                    return TraceState();
                case "stop":
                    m_TraceEntity = Entity.Null;
                    return TraceState();
                default:
                    throw new ArgumentException("action: tag, untag, list, trace, read, stop");
            }
        }

        private JObject ListTagged()
        {
            Type type;
            try
            {
                type = FindType(kTagType);
            }
            catch (Exception e)
            {
                return new JObject { ["tagType"] = "missing: " + e.Message };
            }
            var rows = new JArray();
            using (EntityQuery query = EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly(type) },
                Options = EntityQueryOptions.IncludeDisabledEntities,
            }))
            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity e = entities[i];
                    var row = new JObject
                    {
                        ["id"] = EntityId(e),
                        ["prefab"] = PrefabNameOf(e),
                        ["tag"] = ReadComponent(e, type),
                        ["provider"] = EntityManager.HasComponent<AttractivenessProvider>(e)
                            ? EntityManager.GetComponentData<AttractivenessProvider>(e).m_Attractiveness : (int?)null,
                        ["temp"] = EntityManager.HasComponent<Temp>(e),
                        ["deleted"] = EntityManager.HasComponent<Deleted>(e),
                        ["destroyed"] = EntityManager.HasComponent<Destroyed>(e),
                    };
                    if (EntityManager.TryGetComponent(e, out Transform t))
                        row["pos"] = new JArray(Math.Round(t.m_Position.x, 1), Math.Round(t.m_Position.y, 1), Math.Round(t.m_Position.z, 1));
                    rows.Add(row);
                }
            }
            return new JObject { ["count"] = rows.Count, ["rows"] = rows };
        }

        /// <summary>Sim side: after AttractionSystem on the same tick. V is the value read on a tick whose update frame is the building's.</summary>
        internal void TickMergeProbe()
        {
            if (m_TraceEntity == Entity.Null || !EntityManager.Exists(m_TraceEntity) ||
                !EntityManager.HasComponent<AttractivenessProvider>(m_TraceEntity))
                return;
            uint frame = m_Simulation.frameIndex;
            uint slice = SimulationUtils.GetUpdateFrameWithInterval(frame, 16, 16);
            int own = EntityManager.HasComponent<UpdateFrame>(m_TraceEntity)
                ? (int)EntityManager.GetSharedComponent<UpdateFrame>(m_TraceEntity).m_Index : -1;
            int before = EntityManager.GetComponentData<AttractivenessProvider>(m_TraceEntity).m_Attractiveness;
            bool vanillaTick = own == (int)slice;
            if (vanillaTick)
            {
                m_TraceV = before;
                m_TraceHasV = true;
            }
            int written = -1;
            if (m_TraceWrite && m_TraceHasV)
            {
                written = (int)math.round(math.sqrt((double)m_TraceV * m_TraceV + (double)m_TraceP * m_TraceP));
                EntityManager.SetComponentData(m_TraceEntity, new AttractivenessProvider { m_Attractiveness = written });
            }
            if (m_TraceSim.Count < kTraceRows)
            {
                m_TraceSim.Add(new JObject
                {
                    ["f"] = frame, ["slice"] = slice, ["own"] = own, ["read"] = before, ["vanilla"] = vanillaTick, ["wrote"] = written,
                });
            }
        }

        /// <summary>UI side: every frame, the value the rest of the game sees between ticks.</summary>
        private void TickMergeProbeUi()
        {
            if (m_TraceEntity == Entity.Null || !EntityManager.Exists(m_TraceEntity) ||
                !EntityManager.HasComponent<AttractivenessProvider>(m_TraceEntity))
                return;
            int value = EntityManager.GetComponentData<AttractivenessProvider>(m_TraceEntity).m_Attractiveness;
            m_TraceUiFrames++;
            m_TraceUiValues.TryGetValue(value, out int n);
            m_TraceUiValues[value] = n + 1;
            if (m_TraceUiLast != int.MinValue && value != m_TraceUiLast)
            {
                m_TraceUiChanges++;
                if (m_TraceUiChangeRows.Count < 50)
                    m_TraceUiChangeRows.Add(new JObject { ["f"] = m_Simulation.frameIndex, ["from"] = m_TraceUiLast, ["to"] = value });
            }
            m_TraceUiLast = value;
        }

        private JObject TraceState()
        {
            var values = new JObject();
            foreach (KeyValuePair<int, int> pair in m_TraceUiValues)
                values[pair.Key.ToString()] = pair.Value;
            return new JObject
            {
                ["entity"] = m_TraceEntity == Entity.Null ? null : EntityId(m_TraceEntity),
                ["p"] = m_TraceP,
                ["v"] = m_TraceHasV ? m_TraceV : (int?)null,
                ["sim"] = new JArray(m_TraceSim.ToArray()),
                ["uiFrames"] = m_TraceUiFrames,
                ["uiValues"] = values,
                ["uiChanges"] = m_TraceUiChanges,
                ["uiChangeRows"] = new JArray(m_TraceUiChangeRows.ToArray()),
            };
        }
    }

}
