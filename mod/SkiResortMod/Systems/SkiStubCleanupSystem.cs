// A player's power line outlives the station it was wired to: vanilla drops the station's stub node but keeps it for the line
// (Game.Objects/SubElementDeleteSystem.cs:125-136, Owner removed), with OUR marker prefab still on it. Saved that way, a city depends on
// this mod for one node of a vanilla line. So an unowned marker node with only foreign edges becomes a plain node of that line: its
// PrefabRef takes the edge's prefab, its components the difference between the two node archetypes, and Updated lets vanilla rebuild it.
// A node still on one of our lines stays: a lift line's loose end at a removed station is the section-by-section workflow.
// Scheduling (load-bearing): UpdateBefore<SkiStubCleanupSystem, SubElementDeleteSystem>(PostTool), so the Updated tag reaches the
// Modification phases this frame; PostTool runs every frame, paused included (Game.Tools/ToolSystem.cs:251-257).

using System.Collections.Generic;
using Colossal.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Node = Game.Net.Node;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiStubCleanupSystem : GameSystemBase
    {
        private const int EveryFrames = 60;

        private SkiPrefabCatalogSystem m_Catalog;
        private EntityQuery m_Stubs;
        private int m_Tick;
        private readonly HashSet<Entity> m_OurNets = new HashSet<Entity>();

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Catalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_Stubs = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<Marker>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            RequireForUpdate(m_Stubs);
            Mod.Log.Info($"{nameof(SkiStubCleanupSystem)} created");
        }

        protected override void OnUpdate()
        {
            if (++m_Tick % EveryFrames != 0)
                return;
            if (m_OurNets.Count == 0)
                ResolveOurNets();
            using (NativeArray<Entity> stubs = m_Stubs.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < stubs.Length; i++)
                    TrySwap(stubs[i]);
            }
        }

        private void ResolveOurNets()
        {
            foreach (DeleteAllRules.Target t in DeleteAllRules.Targets())
            {
                if (DeleteAllRules.IsNetKind(t.Kind) && m_Catalog.TryGetPrefab(t.TypeName, t.Name, out Entity prefab, out PrefabBase _) && prefab != Entity.Null)
                    m_OurNets.Add(prefab);
            }
        }

        private void TrySwap(Entity node)
        {
            PrefabRef was = EntityManager.GetComponentData<PrefabRef>(node);
            if (!m_OurNets.Contains(was.m_Prefab) || !EntityManager.TryGetBuffer(node, true, out DynamicBuffer<ConnectedEdge> connected))
                return;
            int edges = 0, foreign = 0;
            Entity plain = Entity.Null;
            for (int i = 0; i < connected.Length; i++)
            {
                Entity edge = connected[i].m_Edge;
                if (EntityManager.HasComponent<Deleted>(edge) || !EntityManager.TryGetComponent(edge, out PrefabRef edgePrefab))
                    continue;
                edges++;
                if (!m_OurNets.Contains(edgePrefab.m_Prefab))
                {
                    foreign++;
                    if (plain == Entity.Null)
                        plain = edgePrefab.m_Prefab;
                }
            }
            if (!DeleteAllRules.StubBecomesPlain(edges, foreign) ||
                !EntityManager.TryGetComponent(was.m_Prefab, out NetData oldNet) || !EntityManager.TryGetComponent(plain, out NetData newNet))
                return;

            // Only the components the two node archetypes disagree on (Marker at least); what the simulation added at run time stays.
            using (NativeArray<ComponentType> oldTypes = oldNet.m_NodeArchetype.GetComponentTypes(Allocator.Temp))
            using (NativeArray<ComponentType> newTypes = newNet.m_NodeArchetype.GetComponentTypes(Allocator.Temp))
            {
                for (int i = 0; i < oldTypes.Length; i++)
                {
                    if (!newTypes.Contains(oldTypes[i]) && EntityManager.HasComponent(node, oldTypes[i]))
                        EntityManager.RemoveComponent(node, oldTypes[i]);
                }
                for (int i = 0; i < newTypes.Length; i++)
                {
                    if (!oldTypes.Contains(newTypes[i]) && !EntityManager.HasComponent(node, newTypes[i]))
                        EntityManager.AddComponent(node, newTypes[i]);
                }
            }
            EntityManager.SetComponentData(node, new PrefabRef(plain));
            AddUpdated(node);
            if (EntityManager.TryGetBuffer(node, true, out connected))
            {
                using (NativeArray<ConnectedEdge> copy = connected.ToNativeArray(Allocator.Temp))
                {
                    for (int i = 0; i < copy.Length; i++)
                        AddUpdated(copy[i].m_Edge);
                }
            }
            Mod.Log.Info($"Stub [plain]: node {node.Index}:{node.Version} of a removed station now belongs to the power line it carries " +
                         $"({foreign} edge(s)); the city no longer needs this mod for it");
        }

        private void AddUpdated(Entity e)
        {
            if (!EntityManager.HasComponent<Updated>(e) && !EntityManager.HasComponent<Deleted>(e))
                EntityManager.AddComponent<Updated>(e);
        }
    }
}
