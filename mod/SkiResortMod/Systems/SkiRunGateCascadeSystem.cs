// SkiRunGateCascadeSystem — sign deleted → its bound runs deleted, by OUR code
// (docs/research/run-binding-glade-spec.md §3.6, Round O1; DECISIONS 90 cascade kept, 121).
// Created 2026-10-01 (implementer I-1).
//
// WHY OUR OWN CASCADE: since O1 runs carry no Owner and the sign carries no SubArea buffer, so
// every vanilla cascade ignores the relation: SubAreaReferencesSystem is Owner-gated
// (Game.Areas/SubAreaReferencesSystem.cs:122-134), SubElementDeleteSystem and the bulldozer's
// own walk read the sign's SubArea buffer (Game.Objects/SubElementDeleteSystem.cs:66-82;
// BulldozeToolSystem.cs:907-935). The link is SkiRunBinding.m_Gate, so this system walks it.
//
// WHEN A SIGN IS DELETED, AND WHY Modification2 (registration, I-4 in Mod.cs):
//   updateSystem.UpdateBefore<SkiRunGateCascadeSystem, SkiGladeCleanupSystem>(SystemUpdatePhase.Modification2)
//   * Bulldozed: ApplyObjectsSystem.Delete adds Deleted to the original at ApplyTool inside
//     ToolUpdate (Game.Tools/ApplyObjectsSystem.cs:484-488, identical in 1.6.2).
//   * Collapsed: CollapsedBuildingSystem adds Deleted through the EndFrameBarrier
//     (Game.Simulation/CollapsedBuildingSystem.cs:150-175, :261, :318, identical in 1.6.2), which
//     plays back at the start of MainLoop (UpdateBefore<EndFrameBarrier>(MainLoop),
//     Game.Common/SystemOrder.cs:49).
//   * Both are visible at Modification2: MainLoop runs ToolSystem then ModificationSystem
//     (SystemOrder.cs:58, 60, same lines in 1.6.2), and ModificationSystem runs Modification1 →
//     … → ModificationEnd in order (Game.Common/ModificationSystem.cs:19-26). Modification1/2 hold
//     no vanilla area-delete consumer (SystemOrder.cs:94-121). Deleted lives one frame:
//     PrepareCleanUpSystem after MainLoop and CleanUpSystem at Cleanup (SystemOrder.cs:50, :54)
//     destroy the sign, its runs and their trees in the SAME Cleanup.
//   * Anchored BEFORE SkiGladeCleanupSystem (UpdateAt Modification2), so the runs we delete here
//     are seen by the glade cleanup the same frame and their member trees go with them (the
//     sign → runs → trees chain is explicit). Anchoring a mod system to a mod system:
//     UpdateBefore registers into the anchor's reference list (Game/UpdateSystem.cs:168-176,
//     :278-292, identical in 1.6.2); precedent Mod.cs (UpdateAfter<SkiRunUpkeepSystem,
//     SkiSnowInjectionSystem>). The runs' border props are Secondary sub-objects: deleted by
//     SecondaryObjectSystem at Modification4B for a Deleted owner
//     (Game.Objects/SecondaryObjectSystem.cs:728-740, unchanged lines in 1.6.2;
//     SystemOrder.cs:183), not by SubObjectSystem.
//   * NOT on Destroyed: a disaster keeps the sign entity (Rebuild keeps it, D5) — its runs stay
//     bound. Only Deleted is a delete.
//
// SHAPE (copied from SkiGladeCleanupSystem.DeleteLikeOwnerCascade, which mirrors
// SubElementDeleteSystem.cs:78-79): strip Applied/Created/Updated where present, add Deleted.
// No refund (D1; vanilla never refunds areas, ApplyAreasSystem.cs:158-165).
//
// QUERIES. m_DeletedGates = All{Game.Buildings.Building, Deleted, PrefabRef} None{Temp} (Temp
// excludes the bulldozer's preview copy and an applied Temp, which also end Deleted);
// m_BoundRuns = All{SkiRunBinding, Area} None{Deleted, Temp}.
//
// GUARDS (per frame): m_Disabled || !m_Armed; GameManager.isGameLoading; and
// ownership.PrefabSetsReady — NOT ownership.IsArmed: an exception in the sweep or the load pass
// must not switch the cascade off (degraded mode stays "runs go with their sign").
//
// COST: one IsEmptyIgnoreFilter per frame. On a frame where ANY building is deleted: a managed
// scan of the deleted buildings' PrefabRef (ToComponentDataArray completes only PrefabRef's
// writers). Only when one of them is a sign of ours: one copy of the bound runs' bindings
// (main-thread EntityManager batch calls — a sync point, fine on that rare frame).
//
// FAILURE MODE: a one-shot "!!!" disables the cascade until the next load. Runs whose sign is
// deleted meanwhile are NOT deleted: the ownership sweep then reports them as paid orphans
// ("[orphan]" WARN) — never deleted on an inferred event (§3.3 row 5, D7).
//
// SCOPE + DEFENSIVE CONTRACT (house pattern): Game mode only; "created" line; ARMED line per
// load listing every constant; m_Armed/m_Disabled/m_ErrorLogged reset in OnGameLoadingComplete;
// one log line per deleted sign (zero runs included) — silence must be impossible.

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
            /// <summary>Deleted runs named individually per sign line; beyond this only the
            /// count is written.</summary>
            public const int LogRunLabelsMax = 4;
        }

        private SkiRunOwnershipSystem m_Ownership;
        private PrefabSystem m_PrefabSystem;

        private EntityQuery m_DeletedGates;
        private EntityQuery m_BoundRuns;

        // Scratch, reused (cleared) every use — no per-frame managed allocation.
        private readonly HashSet<Entity> m_DeletedSigns = new HashSet<Entity>();

        // Per-load state — reset and re-armed at OnGameLoadingComplete.
        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;

        /// <summary>Signs whose deletion this system processed this load (plain field).</summary>
        public int CascadesThisLoad;

        /// <summary>Runs this system deleted this load (plain field).</summary>
        public int RunsDeletedThisLoad;

        /// <summary>Last cascade line ("-" until one ran this load).</summary>
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

            // HARD GATE: Game only — runs are bound only by the Game-mode ownership system.
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

        /// <summary>Fills m_DeletedSigns with this frame's Deleted, non-Temp buildings whose
        /// prefab is the sign. Managed scan of a small result; ToComponentDataArray completes
        /// only PrefabRef's writers (the SkiRunBindTriggerSystem precedent). ToEntityArray and
        /// ToComponentDataArray of the same unfiltered query walk the same chunks in the same
        /// order, so index i matches.</summary>
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

        /// <summary>Collect the runs bound to a deleted sign first, mutate after (structural
        /// changes invalidate chunks), then one line per sign.</summary>
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

                // Labels BEFORE the delete (PrefabRef stays readable either way until Cleanup,
                // but nothing below depends on that).
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

        /// <summary>"sign SkiResortGate i:v deleted — N bound run(s) deleted [labels ≤ 4]".</summary>
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

        /// <summary>The vanilla owner-cascade shape (SubElementDeleteSystem.cs:78-79; copied from
        /// SkiGladeCleanupSystem.DeleteLikeOwnerCascade): remove Applied/Created/Updated where
        /// present, add Deleted. Batched through the EntityManager array overloads (vanilla
        /// precedents: AddComponent&lt;Deleted&gt; over a NativeList, Game.Debug/DebugSystem.cs:2900
        /// [1.6.2 2908]; RemoveComponent over an array, Game.Areas/MapTileSystem.cs:142). Each
        /// remove batch holds only entities that HAVE the component, so no call depends on how
        /// the batch API treats a missing component (unverified).</summary>
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
                // m_BoundRuns excluded Deleted, so no entity here has it yet.
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
