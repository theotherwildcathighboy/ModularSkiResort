// T256 F1 (P1 field test, 2026-11-20): an old save's lift stations get the skeleton that their new mesh needs.
// Why: the game gives an object Skeleton + Bone + BoneHistory only when its prefab archetype is built from a mesh with
// ProceduralAnimationProperties (Game.Prefabs/ObjectGeometryPrefab.cs:92-95, 157-161, decompile 1.6.4f1). A loaded entity keeps the
// archetype of its save, and vanilla's RequiredComponentSystem adds only BoneHistory to entities that already have Bone
// (Game.Serialization/RequiredComponentSystem.cs:494, 1187-1190), never a skeleton. So a station placed with a mesh without bones
// (the FG O stations, ET62P) and loaded with a mesh that has bones (P, ET62Q) has no skeleton: the bullwheel and nose-train bones
// are never uploaded and the art stays in its rest pose. Measured on the 1.0.3 Ash Flat save: 4 / 4 FG stations and the hosted
// SkiLiftTerminalTowerTop had no Skeleton after the load.
// What: once per load, in the Deserialize phase right after RequiredComponentSystem (before the culling and batch post-deserialize
// steps), add the three buffers to every Ski object whose prefab has a sub-mesh with ProceduralBone and whose entity has no
// Skeleton. The buffers start empty: InitializeBonesSystem sizes them for objects near the camera (InitializeBonesSystem.cs:59,
// 129-146), as for a new object. Adding them in a running city instead (without BoneHistory and outside the load) exited the game
// (P1 probe, REPORT "Game lock log"): this system never does a structural change outside the load.
// Scope: only prefabs whose name starts with "Ski" (ours); other mods' objects are never touched. Runtime-safe to repeat: the query
// excludes entities that already have a Skeleton.
// Log discipline: one line per load (count, prefabs), "!!!" on an exception (the objects then keep their rest pose).
// Mod.cs: UpdateAfter<BoneRetrofitSystem, Game.Serialization.RequiredComponentSystem>(Deserialize).

using System;
using System.Collections.Generic;
using Colossal.Entities;
using Game;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Rendering;
using Unity.Collections;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class BoneRetrofitSystem : GameSystemBase
    {
        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_Query;
        private readonly Dictionary<Entity, bool> m_WantsByPrefab = new Dictionary<Entity, bool>();

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Query = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<ObjectGeometry>() },
                None = new[] { ComponentType.ReadOnly<Skeleton>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>() },
            });
            Mod.Log.Info($"{nameof(BoneRetrofitSystem)} created");
        }

        protected override void OnUpdate()
        {
            try
            {
                m_WantsByPrefab.Clear();
                var fix = new NativeList<Entity>(Allocator.Temp);
                var names = new SortedDictionary<string, int>(StringComparer.Ordinal);
                using (NativeArray<Entity> entities = m_Query.ToEntityArray(Allocator.Temp))
                using (NativeArray<PrefabRef> refs = m_Query.ToComponentDataArray<PrefabRef>(Allocator.Temp))
                {
                    for (int i = 0; i < entities.Length; i++)
                    {
                        Entity prefab = refs[i].m_Prefab;
                        if (!m_WantsByPrefab.TryGetValue(prefab, out bool wants))
                        {
                            wants = WantsSkeleton(prefab);
                            m_WantsByPrefab[prefab] = wants;
                        }
                        if (!wants)
                            continue;
                        fix.Add(entities[i]);
                        string name = m_PrefabSystem.GetPrefabName(prefab);
                        names[name] = names.TryGetValue(name, out int n) ? n + 1 : 1;
                    }
                }

                if (fix.Length > 0)
                {
                    EntityManager.AddComponent(fix.AsArray(), new ComponentTypeSet(ComponentType.ReadWrite<Skeleton>(),
                        ComponentType.ReadWrite<Bone>(), ComponentType.ReadWrite<BoneHistory>()));
                    var parts = new List<string>();
                    foreach (KeyValuePair<string, int> kv in names)
                        parts.Add($"{kv.Key} x{kv.Value}");
                    Mod.Log.Info($"Lifts [bones]: {fix.Length} object(s) of this save got the skeleton their new mesh needs " +
                                 $"(Skeleton + Bone + BoneHistory; placed before the mesh had bones): {string.Join(", ", parts)}");
                }
                else
                {
                    Mod.Log.Info("Lifts [bones]: no object of this save needed a skeleton (none placed before its mesh had bones)");
                }
                fix.Dispose();
            }
            catch (Exception e)
            {
                Mod.Log.Error($"!!! Lifts [bones]: {e.GetType().Name}: {e.Message} — no skeleton added this load (old stations keep their " +
                              $"rest pose; new ones are not affected)\n{e}");
            }
        }

        /// <summary>A Ski prefab with at least one sub-mesh that carries bones (the archetype the game would give a new object).</summary>
        private bool WantsSkeleton(Entity prefab)
        {
            if (!EntityManager.TryGetBuffer(prefab, true, out DynamicBuffer<SubMesh> subMeshes))
                return false;
            bool bones = false;
            for (int k = 0; k < subMeshes.Length && !bones; k++)
                bones = EntityManager.HasBuffer<ProceduralBone>(subMeshes[k].m_SubMesh);
            return bones && m_PrefabSystem.GetPrefabName(prefab).StartsWith("Ski", StringComparison.Ordinal);
        }
    }
}
