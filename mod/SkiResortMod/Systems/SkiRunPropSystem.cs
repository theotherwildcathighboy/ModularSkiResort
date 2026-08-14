// SkiRunPropSystem — unlocks the game's OWN area-props machinery for our run surfaces
// (DECISIONS 67/71). The vanilla mechanism is complete but lot-class-gated: the
// AreaSubObjects prefab component (snow guns / crash fencing per polygon vertex,
// border-placed by SecondaryObjectSystem.CreateSecondaryAreaObjects) only fires for
// area entities that ALREADY carry a runtime Game.Objects.SubObject buffer
// (m_ObjectQuery: All=SubObject + Any=Updated..., SecondaryObjectSystem.cs:4073-4083),
// and that buffer comes from the prefab CLASS archetype — LotPrefab.GetArchetypeComponents
// hard-adds it (LotPrefab.cs:36), SurfacePrefab does not. No prefab JSON can add a
// runtime archetype component, so this system bridges the one missing wire:
//
//   For each of OUR run-surface area entities whose prefab carries prefab-side
//   sub-object entries (AreaSubObjects filled by AreaInitializeSystem) but whose
//   instance lacks the runtime buffer: AddBuffer<Game.Objects.SubObject> + ensure
//   Updated — and the VANILLA spawner does everything else the same frame: one prop
//   per drawn polygon vertex, rotated along the outgoing border segment, elevation
//   handled, self-healing on polygon edits (edits re-tag Updated; the buffer persists).
//
// PHASE IS LOAD-BEARING: SecondaryObjectSystem runs at Modification4B and the
// Created/Updated tags it keys on are wiped at Cleanup EVERY frame — the buffer and
// tag must exist earlier in the SAME frame's Modification4B, so this system registers
// UpdateBefore<SecondaryObjectSystem> there (the PowerBridgeSystem precedent).
// Freshly drawn runs carry Updated already; pre-existing runs from older saves get
// Updated added here once, alongside the buffer, so they spawn on first sight.
//
// SCOPE + ANTI-CONTAMINATION: Game mode ONLY, never Editor — spawned props are real
// owned entities and a map author must never bake them into a map. Only surfaces in
// our watched prefab set are touched, and only when the prefab actually has
// sub-object entries — nothing else on the map is ever modified.
//
// MOD-REMOVAL STORY: the runtime buffer is a vanilla component (saves load fine
// without us); spawned props are owned objects referencing our prefab — the game's
// standard missing-asset handling applies, same as every shipped asset in the pack.
//
// OBSERVABILITY + DEFENSIVE CONTRACT (house pattern): ARMED line per load, one line
// per bridged area, one-shot "!!!" catch that disables for the load only.

using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunPropSystem : GameSystemBase
    {
        // Same tolerant name resolution as the sibling run-aware systems. Groomed is
        // watched too: it has no AreaSubObjects today, and the prefab-side gate below
        // makes that a no-op until the day it does.
        private static readonly string[] RunSurfaceNames =
            { "SkiRunSnowmakingA", "SkiRunGroomedA", "SkiRunNightA" };

        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_UnbridgedAreaQuery;
        private EntityQuery m_PrefabScanQuery;

        private readonly HashSet<Entity> m_RunPrefabs = new HashSet<Entity>();
        private bool m_PrefabsResolved;            // session cache (PrefabSystem outlives loads)

        // Per-load state — reset and re-armed at OnGameLoadingComplete.
        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;
        private int m_BridgedCount;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();

            // Areas that could still need the bridge. The marker of a FULLY bridged
            // area is PseudoRandomSeed (added last), not the SubObject buffer — so
            // runs bridged by the buffer-only first version of this system are picked
            // up again and completed. Once done they leave the query, so steady state
            // is empty and the early-out is near-free. Districts and foreign surfaces
            // linger in the result set; the per-chunk prefab filter skips them cheaply.
            m_UnbridgedAreaQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Node>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<PseudoRandomSeed>(),
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            m_PrefabScanQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());

            Mod.Log.Info($"{nameof(SkiRunPropSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_BridgedCount = 0;

            // HARD GATE: Game only — spawned props must never be baked into a map.
            if (mode != GameMode.Game)
                return;

            try
            {
                ResolveRunPrefabs();
                if (m_RunPrefabs.Count == 0)
                {
                    Mod.Log.Info("Props: no run surface prefab present — vertex-prop bridge " +
                                 "inert until one is imported (expected pre-import)");
                    return;
                }

                m_Armed = true;
                Mod.Log.Info($"Props: ARMED — bridging the runtime SubObject buffer onto run " +
                             $"surfaces whose prefab declares AreaSubObjects ({m_RunPrefabs.Count} " +
                             "prefab(s) watched); the VANILLA SecondaryObjectSystem then places " +
                             "one border prop per drawn polygon vertex, self-healing on edits");
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiRunPropSystem)} EXCEPTION during load setup — " +
                                  "vertex-prop bridge disabled until the next game load. Please " +
                                  $"report this stack:\n{e}");
                }
            }
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !m_Armed || m_UnbridgedAreaQuery.IsEmptyIgnoreFilter)
                    return;

                // Collect first, mutate after — structural changes invalidate chunks.
                NativeList<Entity> toBridge = default;
                var chunks = m_UnbridgedAreaQuery.ToArchetypeChunkArray(Allocator.Temp);
                try
                {
                    var entityHandle = GetEntityTypeHandle();
                    var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                    for (int c = 0; c < chunks.Length; c++)
                    {
                        var entities = chunks[c].GetNativeArray(entityHandle);
                        var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                        for (int i = 0; i < prefabRefs.Length; i++)
                        {
                            Entity prefab = prefabRefs[i].m_Prefab;
                            if (!m_RunPrefabs.Contains(prefab))
                                continue;
                            // Prefab-side gate: only bridge when there is actually
                            // something to spawn (AreaSubObjects filled the prefab's
                            // sub-object buffer at prefab initialization).
                            if (!EntityManager.HasBuffer<Game.Prefabs.SubObject>(prefab) ||
                                EntityManager.GetBuffer<Game.Prefabs.SubObject>(prefab, isReadOnly: true).Length == 0)
                                continue;
                            if (!toBridge.IsCreated)
                                toBridge = new NativeList<Entity>(8, Allocator.Temp);
                            toBridge.Add(entities[i]);
                        }
                    }
                }
                finally
                {
                    chunks.Dispose();
                }

                if (!toBridge.IsCreated)
                    return;

                for (int i = 0; i < toBridge.Length; i++)
                {
                    Entity area = toBridge[i];
                    bool addedBuffer = false;
                    if (!EntityManager.HasBuffer<Game.Objects.SubObject>(area))
                    {
                        EntityManager.AddBuffer<Game.Objects.SubObject>(area);
                        addedBuffer = true;
                    }

                    // THE VARIETY SEED (area-prop-variation.md). LotPrefab's archetype
                    // hard-adds PseudoRandomSeed; SurfacePrefab's does not, so
                    // SecondaryObjectSystem falls back to a fresh per-run seed
                    // (SecondaryObjectSystem.cs:1120 picks the PseudoRandomSeed branch
                    // only when the component exists) — every geometry edit would
                    // reshuffle every prop on the run. We supply a seed hashed from the
                    // area's FIRST NODE POSITION: deterministic, unique per run, stable
                    // across loads and across edits elsewhere on the polygon, and it
                    // serializes with the save like any vanilla seed.
                    ushort seed = 1;
                    var nodes = EntityManager.GetBuffer<Node>(area, isReadOnly: true);
                    if (nodes.Length > 0)
                        seed = HashSeed(nodes[0].m_Position);
                    EntityManager.AddComponentData(area, new PseudoRandomSeed(seed));

                    // Pre-existing areas (older saves) carry no Updated this frame —
                    // add it so SecondaryObjectSystem processes them once, right after
                    // us in this same Modification4B pass. Freshly drawn areas already
                    // have it; AddComponent on an existing component is a no-op.
                    EntityManager.AddComponent<Updated>(area);
                    m_BridgedCount++;
                    Mod.Log.Info($"Props: bridged run area {area.Index}:{area.Version} " +
                                 $"(#{m_BridgedCount} this load) — " +
                                 (addedBuffer ? "runtime SubObject buffer added, " : "buffer already present, ") +
                                 $"variety seed {seed} set, Updated ensured; vanilla border " +
                                 "placement takes it from here");
                }
                toBridge.Dispose();
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiRunPropSystem)} EXCEPTION — vertex-prop " +
                                  "bridge disabled until the next game load. Please report " +
                                  $"this stack:\n{e}");
                }
            }
        }

        /// <summary>Deterministic ushort seed from a world position, quantized to 0.1 m so
        /// float jitter cannot change it between loads. Integer mix, no state.</summary>
        private static ushort HashSeed(float3 position)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)(int)math.round(position.x * 10f)) * 16777619u;
                h = (h ^ (uint)(int)math.round(position.y * 10f)) * 16777619u;
                h = (h ^ (uint)(int)math.round(position.z * 10f)) * 16777619u;
                h ^= h >> 15;
                // 0 is a valid seed value but GetRandom clamps it away; keep 1..65535
                // so the logged number always matches what the game actually uses.
                return (ushort)math.max(1u, h & 0xFFFFu);
            }
        }

        // House pattern: fast PrefabID path + name-scan fallback, tolerate missing.
        private void ResolveRunPrefabs()
        {
            if (m_PrefabsResolved)
                return;
            m_PrefabsResolved = true;

            foreach (string name in RunSurfaceNames)
            {
                Entity prefabEntity = Entity.Null;
                if (m_PrefabSystem.TryGetPrefab(new PrefabID("SurfacePrefab", name),
                        out PrefabBase prefabBase))
                    m_PrefabSystem.TryGetEntity(prefabBase, out prefabEntity);

                if (prefabEntity == Entity.Null)
                {
                    var candidates = m_PrefabScanQuery.ToEntityArray(Allocator.Temp);
                    try
                    {
                        for (int i = 0; i < candidates.Length; i++)
                        {
                            if (m_PrefabSystem.GetPrefabName(candidates[i]) == name)
                            {
                                prefabEntity = candidates[i];
                                break;
                            }
                        }
                    }
                    finally
                    {
                        candidates.Dispose();
                    }
                }

                if (prefabEntity == Entity.Null)
                {
                    Mod.Log.Info($"Props: run surface prefab '{name}' not found (tolerated)");
                    continue;
                }
                m_RunPrefabs.Add(prefabEntity);
                Mod.Log.Info($"Props: resolved run surface prefab '{name}'");
            }
        }
    }
}
