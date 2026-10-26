// The run-area emission idioms shared by the tools that build runs (CatTrackGradeToolSystem: one run; QuickRunsToolSystem: a batch).
// Lifted out of the cat track tool unchanged (2026-10-25, Quick runs package D): the area creation definition, the terrain y of a ring
// node, and the bulldozer-shape delete. No state: the tools keep their own phases, queries and logs.
// Engine notes: EngineNotes/QuickRunsToolSystem.md ("Shared emit")

using Game.Common;
using Game.Simulation;
using Game.Tools;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    internal static class RunAreaEmit
    {
        /// <summary>One area creation definition (AreaToolSystem's shape for a new ownerless area, AreaToolSystem.cs:1572-1587,
        /// 1883-1886, 1967-1968): CreationDefinition{m_Prefab} + the Node ring with the closing duplicate (GenerateAreasSystem.cs:282-291
        /// marks the area Complete only then) + Updated. Elevation float.MinValue snaps each node to the terrain (GenerateAreasSystem.cs:
        /// 307-316) and later to the graded terrain (GroundHeightSystem.cs:762-790 re-snaps such nodes inside OnAreaChanged's bounds).</summary>
        public static void Emit(EntityCommandBuffer ecb, Entity prefab, float2[] ring, ref TerrainHeightData hd)
        {
            Entity e = ecb.CreateEntity();
            ecb.AddComponent(e, new CreationDefinition { m_Prefab = prefab });
            DynamicBuffer<Game.Areas.Node> nodes = ecb.AddBuffer<Game.Areas.Node>(e);
            nodes.ResizeUninitialized(ring.Length + 1);
            for (int i = 0; i < ring.Length; i++)
                nodes[i] = new Game.Areas.Node(Ground(ref hd, ring[i]), float.MinValue);
            nodes[ring.Length] = nodes[0];
            ecb.AddComponent(e, default(Updated));
        }

        /// <summary>The terrain point under xz (y 0 while the height data is not created).</summary>
        public static float3 Ground(ref TerrainHeightData hd, float2 xz)
        {
            var p = new float3(xz.x, 0f, xz.y);
            if (hd.isCreated)
                p.y = TerrainUtils.SampleHeight(ref hd, p);
            return p;
        }

        /// <summary>Plain Deleted with Applied / Created / Updated stripped (the SkiRunGateCascadeSystem idiom; CleanUpSystem destroys it at
        /// frame end), through the EntityManager on the main thread. The caller checks that the entity is still the one it means.</summary>
        public static void DeleteLikeBulldozer(EntityManager em, Entity e)
        {
            if (em.HasComponent<Applied>(e))
                em.RemoveComponent<Applied>(e);
            if (em.HasComponent<Created>(e))
                em.RemoveComponent<Created>(e);
            if (em.HasComponent<Updated>(e))
                em.RemoveComponent<Updated>(e);
            em.AddComponent<Deleted>(e);
        }
    }
}
