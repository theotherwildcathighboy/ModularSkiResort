// The Options button "Delete all ski resort content": one request deletes everything the mod placed in the loaded city, the way the
// bulldozer does, so a player can clean a city before unsubscribing. The button's setter only queues (an Options UI callback cannot
// use ToolOutputBarrier / EndFrameBarrier, Game/SafeCommandBufferSystem.cs:14-21); this system does the work on the main thread.
// Frame 1: the active tool goes back to the default tool, so no Temp preview of ours outlives the delete. Frame 2: the delete.
// Scheduling (load-bearing): UpdateBefore<SkiDeleteAllSystem, SubElementDeleteSystem>(PostTool), where a bulldoze's Deleted stands
// after ApplyTool (Game.Common/SystemOrder.cs:712-724); PostTool runs every frame, paused included (Game.Tools/ToolSystem.cs:251-257).
// Vanilla then cascades station sub-nets and sub-areas (SubElementDeleteSystem, PostTool), the electricity graph
// (ElectricityGraphDeleteSystem, Modification1), sub-objects incl. towers / chairs (SubObjectSystem, Modification2B),
// the number tiles (SecondaryObjectSystem, Modification4B) and the ConnectedEdge refs (Game.Net/ReferencesSystem.cs:143-156).
// Selection is by our resolved prefab entities only (DeleteAllRules.Targets); a vanilla power line, road or building is never
// selected, and a net node of ours that a foreign edge still uses is kept (DeleteAllRules.NodeVerdict). One summary log line.
// Plan, inventory and what stays (graded terrain, injected snow, felled trees): docs/research/delete-all-2026-10-29.md.

using System;
using System.Collections.Generic;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Area = Game.Areas.Area;
using Node = Game.Net.Node;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiDeleteAllSystem : GameSystemBase
    {
        // Set by the Options button (UI thread = main thread), read and cleared here
        private static bool s_Requested;
        private static string s_RequestedBy;

        private SkiPrefabCatalogSystem m_Catalog;
        private ToolSystem m_ToolSystem;
        private DefaultToolSystem m_DefaultTool;

        private EntityQuery m_Edges;
        private EntityQuery m_Nodes;
        private EntityQuery m_Buildings;
        private EntityQuery m_Areas;
        private EntityQuery m_Trees;
        private EntityQuery m_RunProps;
        private EntityQuery m_Passes;

        private int m_Countdown;   // 0 = idle; 2 = tool reset this frame; 1 = delete this frame
        private bool m_ErrorLogged;

        // Scratch, reused per run
        private readonly Dictionary<Entity, CatalogKind> m_TargetKinds = new Dictionary<Entity, CatalogKind>();
        private readonly HashSet<Entity> m_DeleteEdges = new HashSet<Entity>();
        private readonly HashSet<Entity> m_DeleteNodes = new HashSet<Entity>();

        /// <summary>Queues one delete (the Options button). Refused with a log line outside a loaded Game-mode city.</summary>
        public static void Request(string by)
        {
            GameManager gm = GameManager.instance;
            bool game = gm != null && gm.gameMode.IsGame();
            bool loading = gm == null || gm.isGameLoading;
            if (!DeleteAllRules.ButtonEnabled(game, loading))
            {
                Mod.Log.Info($"DeleteAll: refused ({by}) — no loaded city in Game mode (the button is for a loaded city only)");
                return;
            }
            s_Requested = true;
            s_RequestedBy = by;
            Mod.Log.Info($"DeleteAll: requested ({by}) — runs within two frames");
        }

        /// <summary>The Options button's disable condition (true = disabled): not in a loaded Game-mode city.</summary>
        public static bool Unavailable()
        {
            GameManager gm = GameManager.instance;
            return gm == null || !DeleteAllRules.ButtonEnabled(gm.gameMode.IsGame(), gm.isGameLoading);
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Catalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_DefaultTool = World.GetOrCreateSystemManaged<DefaultToolSystem>();

            ComponentType[] none = { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() };
            m_Edges = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<PrefabRef>() },
                None = none,
            });
            m_Nodes = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<PrefabRef>() },
                None = none,
            });
            m_Buildings = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Buildings.Building>(), ComponentType.ReadOnly<PrefabRef>() },
                None = none,
            });
            m_Areas = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Area>(), ComponentType.ReadOnly<PrefabRef>() },
                None = none,
            });
            m_Trees = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<SkiRunTreeMember>() },
                None = none,
            });
            m_RunProps = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<SkiRunPropMember>() },
                None = none,
            });
            m_Passes = GetEntityQuery(ComponentType.ReadOnly<SkiSeasonPass>());

            Mod.Log.Info($"{nameof(SkiDeleteAllSystem)} created");
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            // A request never crosses a load
            s_Requested = false;
            m_Countdown = 0;
            m_ErrorLogged = false;
        }

        protected override void OnUpdate()
        {
            if (!s_Requested && m_Countdown == 0)
                return;
            try
            {
                if (s_Requested)
                {
                    s_Requested = false;
                    // Frame 1: close any tool of ours (its Temp preview is cleared at the next ClearTool phase)
                    if (m_ToolSystem.activeTool != m_DefaultTool)
                        m_ToolSystem.activeTool = m_DefaultTool;
                    m_Countdown = 2;
                }
                if (--m_Countdown > 0)
                    return;
                if (Unavailable())
                {
                    Mod.Log.Info("DeleteAll: canceled — the city is no longer loaded in Game mode");
                    return;
                }
                Run();
            }
            catch (Exception e)
            {
                m_Countdown = 0;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiDeleteAllSystem)} EXCEPTION — the delete stopped part way; whatever was marked is " +
                                  "gone, the rest is untouched (press the button again to retry):\n" + e);
                }
            }
        }

        private void Run()
        {
            var counts = new int[DeleteAllRules.KindCount];
            if (!ResolveTargets())
            {
                Mod.Log.Warn("DeleteAll: nothing done — none of the mod's prefabs resolved in this city (asset pack missing?)");
                return;
            }

            var edges = new NativeList<Entity>(64, Allocator.Temp);
            var nodes = new NativeList<Entity>(64, Allocator.Temp);
            var touch = new NativeList<Entity>(16, Allocator.Temp);
            var buildings = new NativeList<Entity>(32, Allocator.Temp);
            var areas = new NativeList<Entity>(32, Allocator.Temp);
            try
            {
                // 1. Collect everything first; mutate after (structural changes invalidate chunks)
                CollectEdges(edges, counts);
                CollectNodes(nodes, counts);
                CollectTouchedNodes(edges, touch);
                CollectTopLevel(m_Buildings, buildings, counts, isArea: false);
                CollectTopLevel(m_Areas, areas, counts, isArea: true);
                int trees = m_Trees.CalculateEntityCount();
                int runProps = m_RunProps.CalculateEntityCount();
                int passes = m_Passes.CalculateEntityCount();

                // 2. Delete, the vanilla owner-cascade shape (SubElementDeleteSystem.cs:78-79)
                DeleteLikeOwnerCascade(edges);
                DeleteLikeOwnerCascade(nodes);
                DeleteLikeOwnerCascade(buildings);
                DeleteLikeOwnerCascade(areas);
                if (trees > 0)
                {
                    using (var treeArray = m_Trees.ToEntityArray(Allocator.Temp))
                    {
                        var list = new NativeList<Entity>(treeArray.Length, Allocator.Temp);
                        list.AddRange(treeArray);
                        DeleteLikeOwnerCascade(list);
                        list.Dispose();
                    }
                }
                counts[(int)DeleteKind.Trees] = trees;
                // The placed run props (free-standing, SkiRunPropMember): selected here too, so an orphan tag cannot outlive the button
                if (runProps > 0)
                {
                    using (var propArray = m_RunProps.ToEntityArray(Allocator.Temp))
                    {
                        var list = new NativeList<Entity>(propArray.Length, Allocator.Temp);
                        list.AddRange(propArray);
                        DeleteLikeOwnerCascade(list);
                        list.Dispose();
                    }
                }
                counts[(int)DeleteKind.RunProps] = runProps;

                // 3. Our component on vanilla households: removed, the households stay
                if (passes > 0)
                    EntityManager.RemoveComponent<SkiSeasonPass>(m_Passes);
                counts[(int)DeleteKind.SeasonPasses] = passes;

                // 4. Kept unowned nodes next to a deleted edge rebuild their geometry (SubElementDeleteSystem.cs:94-115 shape). Owned
                //    nodes (station stubs) are NOT tagged: an Updated stub keeps its Owner in SubElementDeleteSystem.cs:126-130.
                if (touch.Length > 0)
                    EntityManager.AddComponent<Updated>(touch.AsArray());

                Mod.Log.Info(DeleteAllRules.Summary(counts) + $"; requested by {s_RequestedBy ?? "?"}");
                if (counts[(int)DeleteKind.KeptSharedNodes] > 0)
                    Mod.Log.Info($"DeleteAll: {counts[(int)DeleteKind.KeptSharedNodes]} node(s) of ours kept because another line " +
                                 "still uses them; each becomes a plain node of that line (Stub [plain])");
            }
            finally
            {
                edges.Dispose();
                nodes.Dispose();
                touch.Dispose();
                buildings.Dispose();
                areas.Dispose();
                m_DeleteEdges.Clear();
                m_DeleteNodes.Clear();
            }
        }

        /// <summary>Fills m_TargetKinds with every resolved target prefab entity. False when none resolved.</summary>
        private bool ResolveTargets()
        {
            m_TargetKinds.Clear();
            foreach (DeleteAllRules.Target t in DeleteAllRules.Targets())
            {
                if (m_Catalog.TryGetPrefab(t.TypeName, t.Name, out Entity prefab, out PrefabBase _) && prefab != Entity.Null)
                    m_TargetKinds[prefab] = t.Kind;
            }
            return m_TargetKinds.Count > 0;
        }

        private void CollectEdges(NativeList<Entity> output, int[] counts)
        {
            using (var entities = m_Edges.ToEntityArray(Allocator.Temp))
            using (var prefabs = m_Edges.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (!m_TargetKinds.TryGetValue(prefabs[i].m_Prefab, out CatalogKind kind) || !DeleteAllRules.IsNetKind(kind))
                        continue;
                    if (EntityManager.HasComponent<Owner>(entities[i]))
                        continue;   // a station's stub edge: vanilla deletes it with the station
                    output.Add(entities[i]);
                    m_DeleteEdges.Add(entities[i]);
                    counts[(int)(kind == CatalogKind.Fence ? DeleteKind.FenceEdges : DeleteKind.LiftEdges)]++;
                }
            }
        }

        private void CollectNodes(NativeList<Entity> output, int[] counts)
        {
            using (var entities = m_Nodes.ToEntityArray(Allocator.Temp))
            using (var prefabs = m_Nodes.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    bool ours = m_TargetKinds.TryGetValue(prefabs[i].m_Prefab, out CatalogKind kind) && DeleteAllRules.IsNetKind(kind);
                    if (!ours)
                        continue;
                    Entity node = entities[i];
                    int edgeCount = 0, deleted = 0;
                    if (EntityManager.TryGetBuffer(node, true, out DynamicBuffer<ConnectedEdge> connected))
                    {
                        for (int k = 0; k < connected.Length; k++)
                        {
                            Entity edge = connected[k].m_Edge;
                            if (EntityManager.HasComponent<Deleted>(edge))
                                continue;
                            edgeCount++;
                            if (m_DeleteEdges.Contains(edge))
                                deleted++;
                        }
                    }
                    switch (DeleteAllRules.NodeVerdict(true, EntityManager.HasComponent<Owner>(node), edgeCount, deleted))
                    {
                        case DeleteNodeVerdict.Delete:
                            output.Add(node);
                            m_DeleteNodes.Add(node);
                            counts[(int)(kind == CatalogKind.Fence ? DeleteKind.FenceNodes : DeleteKind.LiftNodes)]++;
                            break;
                        case DeleteNodeVerdict.KeepShared:
                            counts[(int)DeleteKind.KeptSharedNodes]++;
                            break;
                    }
                }
            }
        }

        /// <summary>The end nodes of deleted edges that stay: unowned, not deleted by us, each once.</summary>
        private void CollectTouchedNodes(NativeList<Entity> deletedEdges, NativeList<Entity> output)
        {
            var seen = new HashSet<Entity>();
            for (int i = 0; i < deletedEdges.Length; i++)
            {
                Edge e = EntityManager.GetComponentData<Edge>(deletedEdges[i]);
                Touch(e.m_Start, seen, output);
                Touch(e.m_End, seen, output);
            }
        }

        private void Touch(Entity node, HashSet<Entity> seen, NativeList<Entity> output)
        {
            if (node == Entity.Null || m_DeleteNodes.Contains(node) || !seen.Add(node) || !EntityManager.Exists(node))
                return;
            if (EntityManager.HasComponent<Owner>(node) || EntityManager.HasComponent<Deleted>(node) ||
                EntityManager.HasComponent<Updated>(node))
                return;
            output.Add(node);
        }

        /// <summary>Top-level (no Owner) buildings or areas whose prefab is a target of a building / run kind.</summary>
        private void CollectTopLevel(EntityQuery query, NativeList<Entity> output, int[] counts, bool isArea)
        {
            using (var entities = query.ToEntityArray(Allocator.Temp))
            using (var prefabs = query.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (!m_TargetKinds.TryGetValue(prefabs[i].m_Prefab, out CatalogKind kind))
                        continue;
                    if (EntityManager.HasComponent<Owner>(entities[i]))
                        continue;
                    if (isArea)
                    {
                        if (kind != CatalogKind.Run)
                            continue;
                        counts[(int)DeleteKind.Runs]++;
                    }
                    else
                    {
                        if (!DeleteAllRules.BuildingKindOf(kind, out DeleteKind counter))
                            continue;
                        counts[(int)counter]++;
                    }
                    output.Add(entities[i]);
                }
            }
        }

        /// <summary>SkiRunGateCascadeSystem.DeleteLikeOwnerCascade's shape: remove Applied / Created / Updated where present, add
        /// Deleted, through the EntityManager array overloads. Every entity here was collected from a query that excludes Deleted.</summary>
        private void DeleteLikeOwnerCascade(NativeList<Entity> entities)
        {
            if (entities.Length == 0)
                return;
            var applied = new NativeList<Entity>(4, Allocator.Temp);
            var created = new NativeList<Entity>(4, Allocator.Temp);
            var updated = new NativeList<Entity>(4, Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity e = entities[i];
                    if (EntityManager.HasComponent<Applied>(e))
                        applied.Add(e);
                    if (EntityManager.HasComponent<Created>(e))
                        created.Add(e);
                    if (EntityManager.HasComponent<Updated>(e))
                        updated.Add(e);
                }
                if (applied.Length > 0)
                    EntityManager.RemoveComponent<Applied>(applied.AsArray());
                if (created.Length > 0)
                    EntityManager.RemoveComponent<Created>(created.AsArray());
                if (updated.Length > 0)
                    EntityManager.RemoveComponent<Updated>(updated.AsArray());
                EntityManager.AddComponent<Deleted>(entities.AsArray());
            }
            finally
            {
                applied.Dispose();
                created.Dispose();
                updated.Dispose();
            }
        }
    }
}
