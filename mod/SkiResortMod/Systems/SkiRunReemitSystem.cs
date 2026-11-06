// The Groomed feature's toggle (DECISIONS 577): a run is re-emitted as the other surface prefab, because a placed area cannot change its
// prefab in place (the R0 probe: the renderer and the area search keep the old prefab's batch). One request per run is queued by the hub's
// setRunGroomed trigger and done here, main thread: a new area entity from the target prefab's archetype with the old ring and Area flags
// (GenerateAreasSystem.cs:259-345 for a permanent creation, Applied as ApplyAreasSystem.cs:272 adds it), then the old one deleted the
// bulldozer's way. Everything else carries over: the binding (no new bind fee: the bind trigger only takes runs without one), the prop
// sliders (the placer re-places on the new run; the old props cascade with the old run), the custom name, the trail name (SkiRunName), the glade trees (retargeted
// before SkiGladeCleanupSystem's Modification2 pass), and the run-keyed runtime state of the injection, the catalog, the score, the hub and
// the Quick runs undo. A step the run has never paid (Groomed on) is charged once through SkiRunOwnershipSystem.SettleFeatureSteps.
// The surface redraws, so the run may flash for a frame: the game itself draws an area that changes type the same way.
// Phase Modification1 is load-bearing (see Mod.cs): before SkiGladeCleanupSystem (Modification2), the placer's cascade and the area
// geometry (Modification4-5) and the bind trigger (ModificationEnd), all of which must see the new run and the old one's Deleted tag in
// the same frame. Game mode only; one-shot "!!!" disables re-emits until the next load.
// Logs: "created", one "Reemit [groomed]" line per re-emit, "!!!" once on a throw.
// Engine notes: EngineNotes/SkiRunReemitSystem.md

using System;
using System.Collections.Generic;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Game.UI;
using Unity.Collections;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunReemitSystem : GameSystemBase
    {
        private struct Pending
        {
            public Entity Run;
            public bool Groomed;
        }

        private SkiRunOwnershipSystem m_Ownership;
        private SkiPrefabCatalogSystem m_PrefabCatalog;
        private SkiRunCatalogSystem m_Catalog;
        private SkiResortStatsSystem m_Stats;
        private SkiSnowInjectionSystem m_Injection;
        private SkiResortAttractivenessSystem m_Attractiveness;
        private SkiResortHubUISystem m_Hub;
        private QuickRunsToolSystem m_QuickRuns;
        private NameSystem m_NameSystem;
        private EntityQuery m_TreeMemberQuery;

        private readonly List<Pending> m_Pending = new List<Pending>(2);
        private bool m_Armed, m_Disabled, m_ErrorLogged;

        /// <summary>Re-emits done this load (bridge checks and DUMP).</summary>
        internal int Reemits { get; private set; }

        /// <summary>The last re-emit's old and new entity (bridge checks).</summary>
        internal Entity LastFrom { get; private set; }
        internal Entity LastTo { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Ownership = World.GetOrCreateSystemManaged<SkiRunOwnershipSystem>();
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_Catalog = World.GetOrCreateSystemManaged<SkiRunCatalogSystem>();
            m_Stats = World.GetOrCreateSystemManaged<SkiResortStatsSystem>();
            m_Injection = World.GetOrCreateSystemManaged<SkiSnowInjectionSystem>();
            m_Attractiveness = World.GetOrCreateSystemManaged<SkiResortAttractivenessSystem>();
            m_Hub = World.GetOrCreateSystemManaged<SkiResortHubUISystem>();
            m_QuickRuns = World.GetOrCreateSystemManaged<QuickRunsToolSystem>();
            m_NameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            m_TreeMemberQuery = GetEntityQuery(ComponentType.ReadOnly<SkiRunTreeMember>(), ComponentType.Exclude<Deleted>());
            Mod.Log.Info($"{nameof(SkiRunReemitSystem)} created (the Groomed toggle: a run re-emitted as the other surface, its state kept)");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_Disabled = m_ErrorLogged = false;
            m_Pending.Clear();
            Reemits = 0;
            LastFrom = LastTo = Entity.Null;
        }

        /// <summary>The hub's setRunGroomed trigger: queue a re-emit of <paramref name="run"/> as groomed or natural. Returns what happens
        /// (for the trigger's log line); the re-emit itself runs at the next Modification1.</summary>
        internal string Request(Entity run, bool groomed)
        {
            if (!m_Armed || m_Disabled)
                return m_Disabled ? "refused: re-emits are disabled until the next load" : "refused: not in a game";
            string why = Check(run, groomed, out _, out _);
            if (why != null)
                return why;
            for (int i = 0; i < m_Pending.Count; i++)
            {
                if (m_Pending[i].Run == run)
                {
                    m_Pending[i] = new Pending { Run = run, Groomed = groomed };
                    return "queued (replaces the earlier request)";
                }
            }
            m_Pending.Add(new Pending { Run = run, Groomed = groomed });
            return "queued";
        }

        /// <summary>Null when <paramref name="run"/> can be re-emitted to the requested state; else the reason.</summary>
        private string Check(Entity run, bool groomed, out Entity oldPrefab, out Entity newPrefab)
        {
            oldPrefab = newPrefab = Entity.Null;
            if (run == Entity.Null || !EntityManager.Exists(run) || EntityManager.HasComponent<Deleted>(run) ||
                EntityManager.HasComponent<Temp>(run) || !EntityManager.TryGetComponent(run, out PrefabRef pr) ||
                !EntityManager.HasComponent<Area>(run) || !EntityManager.HasBuffer<Node>(run))
                return "refused: not a placed run";
            oldPrefab = pr.m_Prefab;
            RunFeatures kind = RunFeatures.FromKind(m_Ownership.KindOf(oldPrefab));   // the prefab's base and Groomed
            if (!kind.Known)
                return "refused: not one of our runs";
            if (kind.IsCatTrack)
                return "refused: a cat track is always groomed";
            if (kind.Groomed == groomed)
                return groomed ? "nothing to do: already groomed" : "nothing to do: already natural";
            newPrefab = m_PrefabCatalog.EntityOf(groomed ? SkiCatalog.Names.RunGroomed : SkiCatalog.Names.RunNatural);
            if (newPrefab == Entity.Null || !EntityManager.HasComponent<AreaData>(newPrefab))
                return "refused: the target surface prefab is missing";
            return null;
        }

        protected override void OnUpdate()
        {
            if (m_Pending.Count == 0)
                return;
            try
            {
                for (int i = 0; i < m_Pending.Count; i++)
                    Reemit(m_Pending[i].Run, m_Pending[i].Groomed);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiRunReemitSystem)} EXCEPTION — Groomed toggles disabled until the next game load. " +
                                  $"Please report this stack:\n{e}");
                }
            }
            finally
            {
                m_Pending.Clear();
            }
        }

        private void Reemit(Entity run, bool groomed)
        {
            string why = Check(run, groomed, out Entity oldPrefab, out Entity newPrefab);
            if (why != null)
            {
                Mod.Log.Info($"Reemit [groomed] {Ent(run)} → {(groomed ? "groomed" : "natural")} skipped at apply: {why}");
                return;
            }

            // What carries over, read before any structural change.
            DynamicBuffer<Node> oldNodes = EntityManager.GetBuffer<Node>(run, true);
            var nodes = new NativeArray<Node>(oldNodes.Length, Allocator.Temp);
            for (int i = 0; i < nodes.Length; i++)
                nodes[i] = oldNodes[i];
            Area area = EntityManager.GetComponentData<Area>(run);
            float areaM2 = EntityManager.TryGetComponent(run, out Geometry geometry) ? geometry.m_SurfaceArea : float.NaN;
            bool hasBinding = EntityManager.TryGetComponent(run, out SkiRunBinding binding);
            bool hasProps = EntityManager.TryGetComponent(run, out SkiRunPropSettings props);
            bool hasSeed = EntityManager.TryGetComponent(run, out PseudoRandomSeed seed);
            bool hasTrailName = EntityManager.TryGetComponent(run, out SkiRunName trailName);
            m_NameSystem.TryGetCustomName(run, out string customName);

            // The new run: the target prefab's archetype carries Created and Updated (AreaPrefab.cs:47-48).
            AreaData areaData = EntityManager.GetComponentData<AreaData>(newPrefab);
            Entity neu;
            using (var created = new NativeArray<Entity>(1, Allocator.Temp))
            {
                EntityManager.CreateEntity(areaData.m_Archetype, created);   // the single-entity overload needs ReadOnlySpan, absent from this target
                neu = created[0];
            }
            EntityManager.SetComponentData(neu, new PrefabRef(newPrefab));
            DynamicBuffer<Node> newNodes = EntityManager.GetBuffer<Node>(neu);
            newNodes.ResizeUninitialized(nodes.Length);
            for (int i = 0; i < nodes.Length; i++)
                newNodes[i] = nodes[i];
            nodes.Dispose();
            EntityManager.SetComponentData(neu, area);
            if (hasSeed && EntityManager.HasComponent<PseudoRandomSeed>(neu))
                EntityManager.SetComponentData(neu, seed);
            EntityManager.AddComponent<Applied>(neu);
            if (hasBinding)
                EntityManager.AddComponentData(neu, binding);
            if (hasTrailName)
                EntityManager.AddComponentData(neu, trailName);   // before SkiRunNameSystem (ModificationEnd) could give it another
            if (hasProps)
            {
                props.m_AppliedKey = 0;   // nothing stands on the new run yet: the placer re-places
                EntityManager.AddComponentData(neu, props);
            }
            int trees = RetargetTrees(run, neu);
            // The old run goes before anything else can throw: one run stands whatever happens below.
            RunAreaEmit.DeleteLikeBulldozer(EntityManager, run);

            m_Injection.RekeyRun(run, neu);
            m_Catalog.RekeyRun(run, neu, customName);
            m_Attractiveness.RekeyRun(run, neu);
            m_Hub.RunReplaced(run, neu, customName);   // sets the custom name at UIUpdate (NameSystem needs a buffer this phase may not make)
            m_QuickRuns.RekeyRun(run, neu, newPrefab);

            int charged = groomed ? m_Ownership.SettleFeatureSteps(neu, "groomed on", areaM2) : 0;
            m_Catalog.RequestRebuild("run re-emitted");
            m_Stats.RequestRebuild("run re-emitted");
            Reemits++;
            LastFrom = run;
            LastTo = neu;
            Mod.Log.Info($"Reemit [groomed] {Ent(run)} → {Ent(neu)} as {(groomed ? "groomed" : "natural")} (prefab {Ent(oldPrefab)} → " +
                         $"{Ent(newPrefab)}): binding {(hasBinding ? "kept" : "none")}, sliders {(hasProps ? "kept" : "none")}, " +
                         $"name {(string.IsNullOrEmpty(customName) ? "default" : "kept")}, {trees} tree(s) retargeted, charged {charged}");
        }

        /// <summary>The glade trees of <paramref name="from"/> now belong to <paramref name="to"/>, so the cleanup cascade spares them.</summary>
        private int RetargetTrees(Entity from, Entity to)
        {
            if (m_TreeMemberQuery.IsEmptyIgnoreFilter)
                return 0;
            int moved = 0;
            using (NativeArray<Entity> trees = m_TreeMemberQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<SkiRunTreeMember> tags = m_TreeMemberQuery.ToComponentDataArray<SkiRunTreeMember>(Allocator.Temp))
            {
                for (int i = 0; i < trees.Length; i++)
                {
                    if (tags[i].m_Run != from)
                        continue;
                    SkiRunTreeMember tag = tags[i];
                    tag.m_Run = to;
                    EntityManager.SetComponentData(trees[i], tag);
                    moved++;
                }
            }
            return moved;
        }

        private static string Ent(Entity e) => $"{e.Index}:{e.Version}";
    }
}
