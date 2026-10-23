// Run-delete cascade for glade-tool trees: when a ski run is deleted (bulldozed, deleted by the area tool or the info panel, or
// with its sign via SkiRunGateCascadeSystem), every tree carrying SkiRunTreeMember{m_Run = that run} is deleted with it. Every
// deleted run of ours is logged (0 trees when none). Orphaned tags are stripped, the tree kept.
// Why our own cascade: planted trees have no Owner (see EngineNotes/SkiRunTreeMember.md), so vanilla's owner cascade cannot see them.
// Registered in Mod.cs at Modification2: Deleted lives one frame and those phases run after the tools, so a member tree dies in the
// same Cleanup as its run.
// Cost: one IsEmptyIgnoreFilter check per frame; main-thread batch calls (a sync point) only when a run of ours is deleted. Game mode only; one-shot "!!!" disables the cascade until the next load.
// Engine notes: EngineNotes/SkiGladeCleanupSystem.md

using System;
using System.Collections.Generic;
using System.Text;
using Colossal.Serialization.Entities;
using Game;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiGladeCleanupSystem : GameSystemBase
    {
        private static class Tuning
        {
            /// <summary>Deleted runs named individually in the cascade log line; beyond this only the count is written.</summary>
            public const int LogRunLabelsMax = 4;
        }

        // Same tolerant name resolution as the sibling run-aware systems (the table is duplicated per system, as in
        // SkiRunOwnershipSystem's *SurfaceNames); SkiRunNaturalA is included: trees planted on a natural run go with it
        private static readonly string[] RunSurfaceNames =
            { "SkiRunGroomedA", "SkiRunSnowmakingA", "SkiRunNightA", "SkiRunNaturalA" };

        private PrefabSystem m_PrefabSystem;

        private EntityQuery m_DeletedAreaQuery;
        private EntityQuery m_MemberQuery;
        private EntityQuery m_PrefabScanQuery;

        private readonly HashSet<Entity> m_RunPrefabs = new HashSet<Entity>();
        private bool m_PrefabsResolved;            // session cache (PrefabSystem outlives loads)

        // Scratch, reused (cleared) every use: no per-frame managed allocation
        private readonly HashSet<Entity> m_DeletedRuns = new HashSet<Entity>();
        private readonly HashSet<Entity> m_ScratchRuns = new HashSet<Entity>();

        // Per-load state, reset and re-armed at OnGameLoadingComplete
        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;
        private bool m_LoadSweepPending;

        /// <summary>Last cascade / sweep line (plain field; dev-tab safe at 10 Hz).</summary>
        public string LastCensus = "nothing yet";

        /// <summary>Member trees found by this load's sweep; -1 before it ran.</summary>
        public int MembersAtLoad = -1;

        /// <summary>Running totals for this load (plain fields).</summary>
        public int TreesDeletedThisLoad;
        public int OrphansStrippedThisLoad;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();

            // Our runs on their Deleted frame. Temp excluded: the area tool's / bulldozer's Temp copies also end Deleted and are not the run.
            m_DeletedAreaQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            // Live member trees. Deleted excluded (already going, e.g. bulldozed in the same click); Temp excluded (never members).
            m_MemberQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<SkiRunTreeMember>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            m_PrefabScanQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());

            Mod.Log.Info($"{nameof(SkiGladeCleanupSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_LoadSweepPending = false;
            LastCensus = "nothing yet";
            MembersAtLoad = -1;
            TreesDeletedThisLoad = 0;
            OrphansStrippedThisLoad = 0;

            // Hard gate: member trees only come from the Game-mode glade tool
            if (mode != GameMode.Game)
                return;

            try
            {
                ResolveRunPrefabs();
                if (m_RunPrefabs.Count == 0)
                {
                    Mod.Log.Info("Glade: cleanup — no run surface prefab present, member-tree " +
                                 "cascade inert (expected pre-import)");
                    return;
                }

                m_Armed = true;
                m_LoadSweepPending = true;
                Mod.Log.Info("Glade: cleanup ARMED — Modification2, after SkiRunGateCascadeSystem: " +
                             $"when one of {m_RunPrefabs.Count} watched run prefab(s) is Deleted " +
                             "(direct bulldoze, area tool / info-panel delete, or a deleted sign " +
                             "via SkiRunGateCascadeSystem), the run is logged (0 trees included) and " +
                             "its SkiRunTreeMember trees get plain Deleted (vanilla owner-cascade " +
                             "shape, no refund); orphan tags (dead/null run) are stripped and the " +
                             "tree kept; one member census + orphan sweep on the first update after " +
                             $"load; LogRunLabelsMax={Tuning.LogRunLabelsMax}");
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiGladeCleanupSystem)} EXCEPTION during load " +
                                  $"setup — member-tree cascade disabled until the next game " +
                                  $"load:\n{e}");
                }
            }
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !m_Armed)
                    return;

                if (m_LoadSweepPending)
                {
                    m_LoadSweepPending = false;
                    LoadSweep();
                }

                // Steady state: near-free. No deleted area anywhere this frame.
                if (m_DeletedAreaQuery.IsEmptyIgnoreFilter)
                    return;

                CollectDeletedRuns();
                if (m_DeletedRuns.Count == 0)
                    return;          // some other area (lot, district) was deleted

                // A deleted run is always logged, also when no member tree exists anywhere
                if (m_MemberQuery.IsEmptyIgnoreFilter)
                    LogDeletedRuns(0, 0);
                else
                    Cascade();
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiGladeCleanupSystem)} EXCEPTION — member-" +
                                  $"tree cascade disabled until the next game load:\n{e}");
                }
            }
        }

        /// <summary>Fills m_DeletedRuns with this frame's Deleted, non-Temp areas whose prefab is one of our runs.</summary>
        private void CollectDeletedRuns()
        {
            m_DeletedRuns.Clear();
            using (var chunks = m_DeletedAreaQuery.ToArchetypeChunkArray(Allocator.Temp))
            {
                var entityHandle = GetEntityTypeHandle();
                var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    for (int i = 0; i < prefabRefs.Length; i++)
                    {
                        if (m_RunPrefabs.Contains(prefabRefs[i].m_Prefab))
                            m_DeletedRuns.Add(entities[i]);
                    }
                }
            }
        }

        /// <summary>The cascade proper: members of a deleted run get Deleted, orphans lose
        /// the tag. Collect first, mutate after (structural changes invalidate chunks).</summary>
        private void Cascade()
        {
            var toDelete = new NativeList<Entity>(64, Allocator.Temp);
            var orphans = new NativeList<Entity>(4, Allocator.Temp);
            try
            {
                using (var chunks = m_MemberQuery.ToArchetypeChunkArray(Allocator.Temp))
                {
                    var entityHandle = GetEntityTypeHandle();
                    var memberHandle = GetComponentTypeHandle<SkiRunTreeMember>(isReadOnly: true);
                    for (int c = 0; c < chunks.Length; c++)
                    {
                        var entities = chunks[c].GetNativeArray(entityHandle);
                        var members = chunks[c].GetNativeArray(ref memberHandle);
                        for (int i = 0; i < members.Length; i++)
                        {
                            Entity run = members[i].m_Run;
                            if (m_DeletedRuns.Contains(run))
                                toDelete.Add(entities[i]);
                            else if (IsOrphan(run))
                                orphans.Add(entities[i]);
                        }
                    }
                }

                DeleteLikeOwnerCascade(toDelete);
                StripTags(orphans);

                TreesDeletedThisLoad += toDelete.Length;
                OrphansStrippedThisLoad += orphans.Length;

                LogDeletedRuns(toDelete.Length, orphans.Length);
            }
            finally
            {
                toDelete.Dispose();
                orphans.Dispose();
            }
        }

        /// <summary>The one line per deletion frame: "Glade: N run(s) deleted [labels] — K member tree(s) deleted with them" (K may be 0).</summary>
        private void LogDeletedRuns(int treesDeleted, int orphansStripped)
        {
            var sb = new StringBuilder(160);
            sb.Append(m_DeletedRuns.Count).Append(" run(s) deleted");
            if (m_DeletedRuns.Count <= Tuning.LogRunLabelsMax)
            {
                sb.Append(" [");
                bool first = true;
                foreach (Entity run in m_DeletedRuns)
                {
                    if (!first)
                        sb.Append(", ");
                    first = false;
                    sb.Append(RunLabel(run));
                }
                sb.Append(']');
            }
            sb.Append(" — ").Append(treesDeleted)
              .Append(" member tree(s) deleted with them (plain Deleted, no refund)");
            if (orphansStripped > 0)
                sb.Append("; ").Append(orphansStripped).Append(" orphan tag(s) stripped");
            LastCensus = sb.ToString();
            Mod.Log.Info($"Glade: {LastCensus}");
        }

        /// <summary>Once per load: census of member trees and an orphan sweep (a run that
        /// was deleted while the cascade was disabled, or a reference that did not survive
        /// the save, leaves a dangling tag).</summary>
        private void LoadSweep()
        {
            if (m_MemberQuery.IsEmptyIgnoreFilter)
            {
                MembersAtLoad = 0;
                LastCensus = "0 member trees at load";
                Mod.Log.Info("Glade: cleanup census — 0 member trees in this city");
                return;
            }

            // legacy = v1 tags, m_PlanGen == 0, NOT m_Version < 2: a v1 tag re-saved by a newer build reads back as version 2 with
            // PlanGen 0 (EngineNotes/SkiRunTreeMember.md, LEGACY TEST)
            int glade = 0, belt = 0, total = 0, legacy = 0;
            var orphans = new NativeList<Entity>(4, Allocator.Temp);
            m_ScratchRuns.Clear();
            try
            {
                using (var chunks = m_MemberQuery.ToArchetypeChunkArray(Allocator.Temp))
                {
                    var entityHandle = GetEntityTypeHandle();
                    var memberHandle = GetComponentTypeHandle<SkiRunTreeMember>(isReadOnly: true);
                    for (int c = 0; c < chunks.Length; c++)
                    {
                        var entities = chunks[c].GetNativeArray(entityHandle);
                        var members = chunks[c].GetNativeArray(ref memberHandle);
                        for (int i = 0; i < members.Length; i++)
                        {
                            total++;
                            SkiRunTreeMember m = members[i];
                            if (IsOrphan(m.m_Run))
                            {
                                orphans.Add(entities[i]);
                                continue;
                            }
                            m_ScratchRuns.Add(m.m_Run);
                            if (m.m_PlanGen == 0)
                                legacy++;
                            if (m.m_Layer == SkiRunTreeMember.LayerBelt)
                                belt++;
                            else
                                glade++;
                        }
                    }
                }

                StripTags(orphans);
                OrphansStrippedThisLoad += orphans.Length;
                MembersAtLoad = total - orphans.Length;
                LastCensus = $"{MembersAtLoad} member trees at load (glade {glade}, belt {belt}; " +
                             $"legacy v1 {legacy}) across {m_ScratchRuns.Count} run(s)" +
                             (orphans.Length > 0 ? $"; {orphans.Length} orphan tag(s) stripped" : "");
                Mod.Log.Info($"Glade: cleanup census — {LastCensus}");
            }
            finally
            {
                orphans.Dispose();
                m_ScratchRuns.Clear();
            }
        }

        /// <summary>The vanilla owner-cascade shape (SubObjectSystem.cs:1241-1242): remove Applied/Created/Updated where present,
        /// add Deleted. Batched through the EntityManager array overloads (vanilla precedents: AddComponent&lt;Deleted&gt; over a
        /// NativeList, Game.Debug/DebugSystem.cs:2900 [2908]; RemoveComponent over an array, Game.Areas/MapTileSystem.cs:142). Each
        /// remove batch holds only entities that have the component, so no call depends on how the batch API treats a missing
        /// component (unverified).</summary>
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
                // Query excluded Deleted, so no entity here has it yet.
                EntityManager.AddComponent<Deleted>(entities.AsArray());
            }
            finally
            {
                applied.Dispose();
                created.Dispose();
                updated.Dispose();
            }
        }

        private void StripTags(NativeList<Entity> orphans)
        {
            if (orphans.Length > 0)
                EntityManager.RemoveComponent<SkiRunTreeMember>(orphans.AsArray());
        }

        private bool IsOrphan(Entity run)
        {
            return run == Entity.Null ||
                   !EntityManager.Exists(run) ||
                   !EntityManager.HasComponent<Area>(run);
        }

        private string RunLabel(Entity run)
        {
            string name = "?";
            if (EntityManager.HasComponent<PrefabRef>(run))
                name = m_PrefabSystem.GetPrefabName(EntityManager.GetComponentData<PrefabRef>(run).m_Prefab);
            return $"{name} {run.Index}:{run.Version}";
        }

        private void ResolveRunPrefabs()
        {
            // Session cache, but only once something resolved: a pre-import session retries on the next load
            if (m_PrefabsResolved)
                return;
            ResolveSet(RunSurfaceNames, "SurfacePrefab", m_RunPrefabs);
            m_PrefabsResolved = m_RunPrefabs.Count > 0;
        }

        /// <summary>Fast PrefabID path with a name-scan fallback (as SkiRunOwnershipSystem.ResolveSet); a missing prefab is tolerated.</summary>
        private void ResolveSet(string[] names, string typeName, HashSet<Entity> set)
        {
            foreach (string name in names)
            {
                Entity prefabEntity = Entity.Null;
                if (m_PrefabSystem.TryGetPrefab(new PrefabID(typeName, name),
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
                    Mod.Log.Info($"Glade: cleanup — prefab '{name}' not found (tolerated)");
                    continue;
                }
                set.Add(prefabEntity);
            }
        }
    }
}
