// Our run props. This file keeps the bridge: it gives our run surfaces a seed, Game.Objects.Surface and the runtime SubObject buffer.
// It also stops vanilla's per-node border props. SkiRunPropSystem.Placer.cs walks the border by distance and stands the guns, hydrants and masts.
// Corner stop: the vanilla spawner rolls one prop per outline node (SecondaryObjectSystem.cs:2972-3007, :3279), with the run prefab's
// sub-object chance as its bound. So writing that chance to 0 at a Game load stops it without an Editor import (written back at a non-Game load).
// Phase is load-bearing: UpdateBefore<SecondaryObjectSystem> in Modification4B (the Created/Updated tags are wiped at Cleanup each frame).
// Game mode only, never Editor (spawned props are real entities a map author must not bake into a map).
// House contract: ARMED line, one-shot "!!!" catch.
// Engine notes: EngineNotes/SkiRunPropSystem.md

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
        // Same tolerant name resolution as the sibling run-aware systems. SkiRunNaturalA is deliberately absent: it carries no props.
        private static readonly string[] RunSurfaceNames =
            { SkiCatalog.Names.RunSnowmaking, SkiCatalog.Names.RunGroomed, SkiCatalog.Names.RunNight, SkiCatalog.Names.RunCatTrack };

        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_UnbridgedAreaQuery;
        private EntityQuery m_FreshAreaQuery;
        private bool m_FullScanDue;
        private EntityQuery m_PrefabScanQuery;

        private readonly HashSet<Entity> m_RunPrefabs = new HashSet<Entity>();
        private bool m_PrefabsResolved;            // session cache (PrefabSystem outlives loads)

        // Per-load state: reset and re-armed at OnGameLoadingComplete.
        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;
        private int m_BridgedCount;

        // Corner stop: every EdgePlacement entry of our run prefabs, its chance recorded before the first write (session cache: prefab
        // entities and our writes outlive loads).
        private struct CornerEntry
        {
            public Entity Prefab;
            public int Index;
            public int Base;
        }

        private readonly List<CornerEntry> m_CornerEntries = new List<CornerEntry>();
        private bool m_CornersRecorded;
        private bool m_CornersStopped;             // prefab data differs from the bases (write-back due at a non-Game load)

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();

            // Areas that could still need the bridge. The marker of a fully bridged area is PseudoRandomSeed (added last), not the SubObject
            // buffer. So the system finds runs bridged by an earlier buffer-only version again and completes them. Only LotPrefab adds PseudoRandomSeed
            // (Game.Prefabs/LotPrefab.cs:37), so Game.Areas.Surface (SurfacePrefab.cs:21-23) keeps map tiles and districts out.
            // Every vanilla surface area stays in: 31,183 in a city of 355,000, 0.53 ms per scan measured. So this whole query is scanned once after a
            // load, and from then on only the areas of m_FreshAreaQuery.
            m_UnbridgedAreaQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<Game.Areas.Surface>(),
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
            // The same areas this frame: a drawn run carries Updated here, right before SecondaryObjectSystem.
            m_FreshAreaQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<Game.Areas.Surface>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Node>(),
                },
                Any = new[]
                {
                    ComponentType.ReadOnly<Created>(),
                    ComponentType.ReadOnly<Updated>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<PseudoRandomSeed>(),
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            m_PrefabScanQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());

            CreatePlacerQueries();

            Mod.Log.Info($"{nameof(SkiRunPropSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_BridgedCount = 0;
            m_FullScanDue = true;

            // HARD GATE: Game only — spawned props must never be baked into a map. (At a non-Game load the corner chances go back to the
            // prefab values, hygiene, and the placer stays off.)
            if (mode != GameMode.Game)
            {
                LoadPlacer(mode);
                return;
            }

            try
            {
                ResolveRunPrefabs();
                if (m_RunPrefabs.Count == 0)
                {
                    Mod.Log.Info("Props: no run surface prefab present — bridge " +
                                 "inert until one is imported (expected pre-import)");
                }
                else
                {
                    m_Armed = true;
                    Mod.Log.Info($"Props: ARMED — bridging the seed, Game.Objects.Surface and the runtime SubObject buffer onto run " +
                                 $"surfaces whose prefab declares AreaSubObjects ({m_RunPrefabs.Count} " +
                                 "prefab(s) watched); vanilla's per-node border props are stopped, the placer stands the props");
                }
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiRunPropSystem)} EXCEPTION during load setup — " +
                                  "bridge disabled until the next game load. Please " +
                                  $"report this stack:\n{e}");
                }
            }

            // After the bridge's setup (ResolveRunPrefabs found the run prefabs by name), in its own try/catch, so neither catch stops the
            // other.
            LoadPlacer(mode);
        }

        protected override void OnUpdate()
        {
            // The placer first, before the bridge's m_Disabled return: its own try/catch, and its delete cascade runs even with no resort left.
            UpdatePlacer();

            try
            {
                if (m_Disabled || !m_Armed || !ResortPresence.Present)
                    return;
                EntityQuery scan = m_FullScanDue ? m_UnbridgedAreaQuery : m_FreshAreaQuery;
                m_FullScanDue = false;
                if (scan.IsEmptyIgnoreFilter)
                    return;

                // Collect first, mutate after — structural changes invalidate chunks.
                NativeList<Entity> toBridge = default;
                var chunks = scan.ToArchetypeChunkArray(Allocator.Temp);
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

                    // The bridge's done-marker (see m_UnbridgedAreaQuery). LotPrefab's archetype hard-adds PseudoRandomSeed. SurfacePrefab's does not. Hashed
                    // from the area's first node position: deterministic and it serializes with the save like any vanilla seed. The placer does not read it
                    // (its picks hash the canonical ring, RunPropPlacerRules).
                    ushort seed = 1;
                    var nodes = EntityManager.GetBuffer<Node>(area, isReadOnly: true);
                    if (nodes.Length > 0)
                        seed = HashSeed(nodes[0].m_Position);
                    EntityManager.AddComponentData(area, new PseudoRandomSeed(seed));

                    // Seed the weather struct at zero in the same pass: SkiRunSurfaceSystem's value lives here, and a placed prop copies it at creation. That
                    // system sets both fields every pass, so a zero seed is never fought over. Runs carry no Owner (the run -> sign link is SkiRunBinding).
                    EntityManager.AddComponent<Game.Objects.Surface>(area);

                    // Pre-existing areas (older saves) carry no Updated this frame. Freshly drawn areas already have it (AddComponent on an existing
                    // component is a no-op). SecondaryObjectSystem then sees this area with every corner chance at 0: nothing to place.
                    EntityManager.AddComponent<Updated>(area);
                    m_BridgedCount++;
                    Mod.Log.Info($"Props: bridged run area {area.Index}:{area.Version} " +
                                 $"(#{m_BridgedCount} this load) — " +
                                 (addedBuffer ? "runtime SubObject buffer added, " : "buffer already present, ") +
                                 $"variety seed {seed} set, Updated ensured");
                }
                toBridge.Dispose();
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiRunPropSystem)} EXCEPTION — " +
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
                // 0 is a valid seed value but GetRandom clamps it away. Keep 1..65535
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
                // The kinds the placer walks: every surface above, its kind from the catalog (since R2 a groomed run carries both feature
                // sliders, DECISIONS 577). RunPropDensityRules.PropClassAllowed decides what each carries.
                m_RunKindOf[prefabEntity] = (int)SkiCatalog.RunByPrefab(name).Kind;
                Mod.Log.Info($"Props: resolved run surface prefab '{name}'");
            }
        }

        /// <summary>Once per session, before the first write: every EdgePlacement entry of our run prefabs (the entries
        /// CreateSecondaryAreaObjects rolls per corner, SecondaryObjectSystem.cs:2983). Marked done at the end, so a throw part-way is retried at
        /// the next load (nothing is written before it completes).</summary>
        private void RecordCorners()
        {
            if (m_CornersRecorded)
                return;
            m_CornerEntries.Clear();
            foreach (Entity prefab in m_RunPrefabs)
            {
                if (!EntityManager.HasBuffer<Game.Prefabs.SubObject>(prefab))
                    continue;
                var buffer = EntityManager.GetBuffer<Game.Prefabs.SubObject>(prefab, isReadOnly: true);
                for (int i = 0; i < buffer.Length; i++)
                {
                    if ((buffer[i].m_Flags & SubObjectFlags.EdgePlacement) != 0)
                        m_CornerEntries.Add(new CornerEntry { Prefab = prefab, Index = i, Base = buffer[i].m_Probability });
                }
            }
            m_CornersRecorded = true;
        }

        /// <summary>Writes every recorded corner chance: 0 (stop) or the recorded base (restore). The roll is NextInt(100) &lt; chance
        /// (SecondaryObjectSystem.cs:3279), so 0 never places. Returns the values changed.</summary>
        private int WriteCorners(bool stop)
        {
            m_CornersStopped = stop;   // before the first write: a throw part-way is still written back later
            int changed = 0;
            for (int e = 0; e < m_CornerEntries.Count; e++)
            {
                CornerEntry entry = m_CornerEntries[e];
                var buffer = EntityManager.GetBuffer<Game.Prefabs.SubObject>(entry.Prefab);
                if (entry.Index >= buffer.Length)
                    continue;   // the buffer is filled once per session (AreaInitializeSystem.cs:235-247). Never seen shorter
                Game.Prefabs.SubObject element = buffer[entry.Index];
                int value = stop ? 0 : entry.Base;
                if (element.m_Probability == value)
                    continue;
                element.m_Probability = value;
                buffer[entry.Index] = element;
                changed++;
            }
            return changed;
        }
    }
}
