// Sign deleted -> its bound runs deleted, by our own code (run-binding-glade-spec.md §3.6).
//
// Why our own cascade: runs carry no Owner and the sign carries no SubArea buffer, so every vanilla cascade ignores the
// relation: SubAreaReferencesSystem is Owner-gated (SubAreaReferencesSystem.cs:122-134), SubElementDeleteSystem and the
// bulldozer's own walk read the sign's SubArea buffer (SubElementDeleteSystem.cs:66-82; BulldozeToolSystem.cs:907-935). The
// link is SkiRunBinding.m_Gate, so this system walks it.
//
// Why Modification2 (Mod.cs: UpdateBefore<SkiRunGateCascadeSystem, SkiGladeCleanupSystem>(Modification2)):
//   * Bulldozed: ApplyObjectsSystem.Delete adds Deleted at ApplyTool inside ToolUpdate (ApplyObjectsSystem.cs:484-488).
//   * Collapsed: CollapsedBuildingSystem adds Deleted through the EndFrameBarrier (CollapsedBuildingSystem.cs:150-175, :261,
//     :318), which plays back at the start of MainLoop (SystemOrder.cs:49).
//   * Both are visible at Modification2: MainLoop runs ToolSystem then ModificationSystem (SystemOrder.cs:58, 60), which runs
//     Modification1 -> ... -> ModificationEnd in order (ModificationSystem.cs:19-26); Modification1/2 hold no vanilla
//     area-delete consumer (SystemOrder.cs:94-121). Deleted lives one frame: PrepareCleanUpSystem and CleanUpSystem
//     (SystemOrder.cs:50, :54) destroy the sign, its runs and their trees in the same Cleanup.
//   * Anchored BEFORE SkiGladeCleanupSystem so the glade cleanup sees the runs deleted here the same frame and their member
//     trees go with them. UpdateBefore on a mod system registers into the anchor's reference list (UpdateSystem.cs:168-176,
//     :278-292). The runs' border props are Secondary sub-objects, deleted by SecondaryObjectSystem at Modification4B for a
//     Deleted owner (SecondaryObjectSystem.cs:728-740; SystemOrder.cs:183), not by SubObjectSystem.
//   * Not on Destroyed: a disaster keeps the sign entity, so its runs stay bound. Only Deleted is a delete.
//
// Shape copied from SkiGladeCleanupSystem.DeleteLikeOwnerCascade (mirrors SubElementDeleteSystem.cs:78-79): strip
// Applied/Created/Updated where present, add Deleted. No refund (vanilla never refunds areas, ApplyAreasSystem.cs:158-165).
//
// Queries: m_DeletedGates = All{Building, Deleted, PrefabRef} None{Temp} (Temp excludes the bulldozer's preview copy and an
// applied Temp, which also end Deleted); m_BoundRuns = All{SkiRunBinding, Area} None{Deleted, Temp}.
// Guards per frame: m_Disabled || !m_Armed; GameManager.isGameLoading; ownership.PrefabSetsReady, not ownership.IsArmed: an
// exception in the sweep or the load pass must not switch the cascade off (degraded mode stays "runs go with their sign").
// Cost: one IsEmptyIgnoreFilter per frame; when any building is deleted, a managed scan of the deleted buildings' PrefabRef
// (ToComponentDataArray completes only PrefabRef's writers); only when one is a sign of ours, one copy of the bound runs'
// bindings (main-thread EntityManager batch calls, a sync point on that rare frame).
// Failure mode: a one-shot "!!!" disables the cascade until the next load. Runs whose sign is deleted meanwhile are NOT
// deleted: the ownership sweep reports them as paid orphans ("[orphan]" WARN); never deleted on an inferred event.
// Game mode only; "created" line; ARMED line per load listing every constant; m_Armed/m_Disabled/m_ErrorLogged reset in
// OnGameLoadingComplete; one log line per deleted sign (zero runs included), so silence is impossible.

using System;
using System.Collections.Generic;
using System.Text;
using Colossal.Serialization.Entities;
using Game;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunGateCascadeSystem : GameSystemBase
    {
        private static class Tuning
        {
            /// <summary>Deleted runs named individually per sign line; beyond this only the count is written.</summary>
            public const int LogRunLabelsMax = 4;
        }

        private SkiRunOwnershipSystem m_Ownership;
        private PrefabSystem m_PrefabSystem;

        private EntityQuery m_DeletedGates;
        private EntityQuery m_BoundRuns;

        // Scratch, reused every use
        private readonly HashSet<Entity> m_DeletedSigns = new HashSet<Entity>();

        // Per-load state, reset at OnGameLoadingComplete
        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;

        public int CascadesThisLoad;

        public int RunsDeletedThisLoad;

        public string LastCascade = "-";

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Ownership = World.GetOrCreateSystemManaged<SkiRunOwnershipSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();

            m_DeletedGates = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Buildings.Building>(),
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            m_BoundRuns = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<SkiRunBinding>(),
                    ComponentType.ReadOnly<Area>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });

            Mod.Log.Info($"{nameof(SkiRunGateCascadeSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            CascadesThisLoad = 0;
            RunsDeletedThisLoad = 0;
            LastCascade = "-";
            m_DeletedSigns.Clear();

            // Game mode only: runs are bound only by the Game-mode ownership system
            if (mode != GameMode.Game)
                return;

            m_Armed = true;
            Mod.Log.Info("RunCascade: ARMED — Modification2 before glade cleanup: a Deleted " +
                         "(non-Temp) Ski Area Welcome Sign deletes every run whose SkiRunBinding " +
                         "points at it (plain Deleted, the vanilla owner-cascade shape, no refund), " +
                         "so its member trees go in the same frame and its border props at " +
                         "Modification4B; not on Destroyed (a disaster keeps the sign, D5); gated " +
                         "per frame on the ownership prefab sets (not on the sweep's state); idle " +
                         "cost one IsEmptyIgnoreFilter per frame; one line per deleted sign; " +
                         $"LogRunLabelsMax={Tuning.LogRunLabelsMax}");
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !m_Armed)
                    return;
                GameManager gm = GameManager.instance;
                if (gm == null || gm.isGameLoading)
                    return;
                if (!m_Ownership.PrefabSetsReady)
                    return;                      // pre-import: no sign prefab, nothing bound

                if (m_DeletedGates.IsEmptyIgnoreFilter)
                    return;

                CollectDeletedSigns();
                if (m_DeletedSigns.Count == 0)
                    return;                      // some other building was deleted

                Cascade();
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    LastCascade = "DISABLED by exception (see mod log)";
                    Mod.Log.Error($"!!! {nameof(SkiRunGateCascadeSystem)} EXCEPTION — run cascade " +
                                  "disabled until the next game load: a sign deleted meanwhile leaves " +
                                  "its runs as paid orphans (kept, re-link free), never deleted:\n" + e);
                }
            }
        }

        /// <summary>Fills m_DeletedSigns with this frame's Deleted, non-Temp buildings whose prefab is the sign. ToEntityArray and
        /// ToComponentDataArray of the same unfiltered query walk the same chunks in the same order, so index i matches.</summary>
        private void CollectDeletedSigns()
        {
            m_DeletedSigns.Clear();
            using (var entities = m_DeletedGates.ToEntityArray(Allocator.Temp))
            using (var prefabRefs = m_DeletedGates.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                int n = Math.Min(entities.Length, prefabRefs.Length);
                for (int i = 0; i < n; i++)
                {
                    if (m_Ownership.IsGatePrefab(prefabRefs[i].m_Prefab))
                        m_DeletedSigns.Add(entities[i]);
                }
            }
        }

        /// <summary>Collect the bound runs first, mutate after (structural changes invalidate chunks), then one line per sign.</summary>
        private void Cascade()
        {
            var runs = new NativeList<Entity>(16, Allocator.Temp);
            var runSigns = new NativeList<Entity>(16, Allocator.Temp);
            try
            {
                if (!m_BoundRuns.IsEmptyIgnoreFilter)
                {
                    using (var entities = m_BoundRuns.ToEntityArray(Allocator.Temp))
                    using (var bindings = m_BoundRuns.ToComponentDataArray<SkiRunBinding>(Allocator.Temp))
                    {
                        int n = Math.Min(entities.Length, bindings.Length);
                        for (int i = 0; i < n; i++)
                        {
                            Entity gate = bindings[i].m_Gate;
                            if (gate != Entity.Null && m_DeletedSigns.Contains(gate))
                            {
                                runs.Add(entities[i]);
                                runSigns.Add(gate);
                            }
                        }
                    }
                }

                var lines = new List<string>(m_DeletedSigns.Count);
                foreach (Entity sign in m_DeletedSigns)
                    lines.Add(SignLine(sign, runs, runSigns));

                DeleteLikeOwnerCascade(runs);
                CascadesThisLoad += m_DeletedSigns.Count;
                RunsDeletedThisLoad += runs.Length;

                for (int i = 0; i < lines.Count; i++)
                {
                    LastCascade = lines[i];
                    Mod.Log.Info($"RunCascade: {lines[i]}");
                }
            }
            finally
            {
                runs.Dispose();
                runSigns.Dispose();
                m_DeletedSigns.Clear();
            }
        }

        private string SignLine(Entity sign, NativeList<Entity> runs, NativeList<Entity> runSigns)
        {
            int count = 0;
            for (int i = 0; i < runSigns.Length; i++)
            {
                if (runSigns[i] == sign)
                    count++;
            }
            var sb = new StringBuilder(160);
            sb.Append("sign ").Append(Label(sign)).Append(" deleted — ")
              .Append(count).Append(" bound run(s) deleted");
            if (count > 0 && count <= Tuning.LogRunLabelsMax)
            {
                sb.Append(" [");
                bool first = true;
                for (int i = 0; i < runs.Length; i++)
                {
                    if (runSigns[i] != sign)
                        continue;
                    if (!first)
                        sb.Append(", ");
                    first = false;
                    sb.Append(Label(runs[i]));
                }
                sb.Append(']');
            }
            sb.Append(" (plain Deleted, no refund)");
            return sb.ToString();
        }

        /// <summary>The vanilla owner-cascade shape (SubElementDeleteSystem.cs:78-79): remove Applied/Created/Updated where present,
        /// add Deleted, batched through the EntityManager array overloads (precedents: DebugSystem.cs:2900, MapTileSystem.cs:142).
        /// Each remove batch holds only entities that have the component, so no call depends on how the batch API treats a missing
        /// one (unverified).</summary>
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
                // m_BoundRuns excluded Deleted, so no entity here has it yet
                EntityManager.AddComponent<Deleted>(entities.AsArray());
            }
            finally
            {
                applied.Dispose();
                created.Dispose();
                updated.Dispose();
            }
        }

        private string Label(Entity e)
        {
            string name = "?";
            if (EntityManager.HasComponent<PrefabRef>(e))
                name = m_PrefabSystem.GetPrefabName(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab);
            return $"{name} {e.Index}:{e.Version}";
        }
    }
}
