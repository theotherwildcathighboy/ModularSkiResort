// Gives every new run a trail name that fits it (R3, DECISIONS 578). A run made this frame (drawn, built by Quick runs, re-emitted
// without a pick, or found without one at load) waits until the catalog has rated it, then gets a SkiRunName: an unused index of the
// trail-name pool among the names that match its traits (SkiRunNameRules.Pick, seeded by its first outline point): steep for a black,
// gentle for a green, glade with trees inside it (SkiRunTreeMember, glade layer), track for a cat track; an "Upper" / "Lower" form near
// the top or the base of its ski area's height range (the rated runs of its gate). None when all are taken (the catalog then shows
// "Run N"). Until then the catalog shows "Run N" / "Cat track N". A run the rating never reaches (MaxWaitFrames while the service is
// ready, or the service failed) is named without traits. The pick is stored, so it never changes: a reload, a reshape or a Groomed toggle
// (SkiRunReemitSystem copies the component) keeps it, and a rename wins over it (NameSystem's custom name, read first by the catalog).
// Picks are per city: two ski areas share the pool, so no two runs share a name while one is free; a bulldozed run frees its name.
// Phase ModificationEnd (Mod.cs): MainLoop, paused or not (ModificationSystem.cs:19-26), the frame's Created tag still on; NOT
// load-bearing for anyone else (the catalog rebuilds on request). Main thread; Game mode only; one-shot "!!!" disables it until the next
// load.

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

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunNameSystem : GameSystemBase
    {
        /// <summary>Frames a run waits for its rating while the service is ready, then it is named without traits.</summary>
        private const int MaxWaitFrames = 900;

        /// <summary>Glade-layer trees inside a run that make it a glade run.</summary>
        private const int GladeMinTrees = 5;

        private SkiRunOwnershipSystem m_Ownership;
        private SkiRunCatalogSystem m_Catalog;
        private EntityQuery m_NewRuns;   // areas made this frame without a pick (vanilla areas too: filtered by kind)
        private EntityQuery m_Unnamed;   // every area without a pick: the load pass only
        private EntityQuery m_Named;     // the picks in use
        private EntityQuery m_Trees;     // trees tagged to a run

        private readonly bool[] m_Used = new bool[SkiRunNameRules.Count];
        private readonly List<Entity> m_Pending = new List<Entity>(8);   // our runs without a pick, waiting for their rating
        private readonly List<int> m_Waited = new List<int>(8);          // frames each has waited while the service was ready
        private readonly List<Entity> m_Ready = new List<Entity>(8);
        private readonly Dictionary<Entity, int> m_GladeTrees = new Dictionary<Entity, int>();

        private bool m_Armed;
        private bool m_LoadPass;
        private bool m_Disabled;
        private bool m_ErrorLogged;

        /// <summary>Runs given a name this load, those given none (pool full), and those named without traits (never rated). Dev tab / bridge.</summary>
        public int Named, Unnamed, Untraited;

        /// <summary>Runs waiting for their rating. Dev tab / bridge.</summary>
        public int PendingCount => m_Pending.Count;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Ownership = World.GetOrCreateSystemManaged<SkiRunOwnershipSystem>();
            m_Catalog = World.GetOrCreateSystemManaged<SkiRunCatalogSystem>();
            m_NewRuns = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<Node>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Created>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<SkiRunName>(),
                },
            });
            m_Unnamed = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<Node>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<SkiRunName>(),
                },
            });
            m_Named = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<SkiRunName>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            m_Trees = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<SkiRunTreeMember>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_LoadPass = m_Armed;   // runs of an older save, or saved before their rating, get a pick once
            m_Disabled = false;
            m_ErrorLogged = false;
            m_Pending.Clear();
            m_Waited.Clear();
            Named = Unnamed = Untraited = 0;
            if (m_Armed)
                Mod.Log.Info($"Run names: ARMED, {SkiRunNameRules.Count} trail names, an unused one that fits each new run once it is rated " +
                             $"(steep, gentle, glade, cat track; Upper / Lower over {SkiRunNameRules.FormMinRangeM:0} m of area height), " +
                             $"stored (SkiRunName); \"Run N\" once all are taken");
        }

        protected override void OnUpdate()
        {
            if (!m_Armed || m_Disabled || !m_Ownership.PrefabSetsReady)
                return;
            try
            {
                EntityQuery query = m_LoadPass ? m_Unnamed : m_NewRuns;
                bool load = m_LoadPass;
                m_LoadPass = false;
                if (!query.IsEmptyIgnoreFilter)
                    CollectRuns(query);
                if (m_Pending.Count == 0)
                    return;
                CollectReady(out bool untraited);
                if (m_Ready.Count == 0)
                    return;
                FillUsed();
                if (!untraited)
                    CountGladeTrees();
                int named = 0, unnamed = 0, upper = 0, lower = 0;
                for (int i = 0; i < m_Ready.Count; i++)
                {
                    Entity run = m_Ready[i];
                    DynamicBuffer<Node> nodes = EntityManager.GetBuffer<Node>(run, true);
                    uint seed = nodes.Length > 0 ? SkiRunNameRules.Seed(nodes[0].m_Position.x, nodes[0].m_Position.z) : (uint)run.Index;
                    Traits(run, untraited, out byte traits, out byte wantForm);
                    int index = SkiRunNameRules.Pick(m_Used, seed, traits, wantForm, out byte form);
                    if (index >= 0)
                    {
                        m_Used[index] = true;   // the next run of this pass picks among the rest
                        named++;
                        upper += form == SkiRunNameRules.FormUpper ? 1 : 0;
                        lower += form == SkiRunNameRules.FormLower ? 1 : 0;
                    }
                    else
                    {
                        unnamed++;
                    }
                    EntityManager.AddComponentData(run, new SkiRunName(index, form));
                }
                Named += named;
                Unnamed += unnamed;
                if (untraited)
                    Untraited += m_Ready.Count;
                Mod.Log.Info($"Run names: {named} named ({upper} upper, {lower} lower)" +
                             (unnamed > 0 ? $", {unnamed} without a name (all {SkiRunNameRules.Count} taken: \"Run N\")" : "") +
                             (untraited ? " without traits (no rating)" : "") + (load ? " (load pass)" : "") +
                             $"; {m_Pending.Count} waiting; this load {Named} named, {Unnamed} without");
                m_Catalog.RequestRebuild("trail names");
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiRunNameSystem)} EXCEPTION — new runs get no trail name until the next load (they show " +
                                  $"\"Run N\"). Please report this stack:\n{e}");
                }
            }
        }

        /// <summary>Adds the runs of ours in <paramref name="query"/> (vanilla areas are skipped) to the waiting list.</summary>
        private void CollectRuns(EntityQuery query)
        {
            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (m_Ownership.KindOf(EntityManager.GetComponentData<PrefabRef>(entities[i]).m_Prefab) >= 0 &&
                        !m_Pending.Contains(entities[i]))
                    {
                        m_Pending.Add(entities[i]);
                        m_Waited.Add(0);
                    }
                }
            }
        }

        /// <summary>Moves the waiting runs that have a rating (or waited too long, or the service failed) to m_Ready, in entity order;
        /// drops those gone or named meanwhile. <paramref name="untraited"/>: name them without traits (no service).</summary>
        private void CollectReady(out bool untraited)
        {
            m_Ready.Clear();
            SkiRunCatalogSystem.RatingServiceState state = m_Catalog.ServiceState;
            untraited = state == SkiRunCatalogSystem.RatingServiceState.Failed || m_Catalog.IsDisabled;
            bool ready = state == SkiRunCatalogSystem.RatingServiceState.Ready;
            for (int i = m_Pending.Count - 1; i >= 0; i--)
            {
                Entity run = m_Pending[i];
                bool drop = !EntityManager.Exists(run) || EntityManager.HasComponent<Deleted>(run) ||
                            EntityManager.HasComponent<SkiRunName>(run);
                bool take = !drop && (untraited || Rated(run) || (ready && ++m_Waited[i] >= MaxWaitFrames));
                if (!drop && !take)
                    continue;
                if (take)
                    m_Ready.Add(run);
                m_Pending.RemoveAt(i);
                m_Waited.RemoveAt(i);
            }
            m_Ready.Sort((a, b) => a.Index != b.Index ? a.Index.CompareTo(b.Index) : a.Version.CompareTo(b.Version));
        }

        private bool Rated(Entity run) =>
            m_Catalog.TryGetRunFacts(run, out SkiRunCatalogSystem.RunFacts facts) && facts.RateState != SkiRunRatingRules.RatePending;

        /// <summary>The run's traits (band, glade trees, cat track) and the form its area's height range asks for.</summary>
        private void Traits(Entity run, bool untraited, out byte traits, out byte form)
        {
            traits = 0;
            form = SkiRunNameRules.FormNone;
            int kind = m_Ownership.KindOf(EntityManager.GetComponentData<PrefabRef>(run).m_Prefab);
            bool catTrack = RunFeatures.FromKind(kind).IsCatTrack;
            if (untraited || !m_Catalog.TryGetRunFacts(run, out SkiRunCatalogSystem.RunFacts facts))
            {
                traits = SkiRunNameRules.RunTraits(catTrack, SkiRunRatingRules.BandPending, false);
                return;
            }
            bool glade = m_GladeTrees.TryGetValue(run, out int trees) && trees >= GladeMinTrees;
            traits = SkiRunNameRules.RunTraits(catTrack, facts.RateState == SkiRunRatingRules.RateRated ? facts.Band : -1, glade);
            if (facts.RateState != SkiRunRatingRules.RateRated)
                return;
            float areaBottom = float.MaxValue, areaTop = float.MinValue;
            for (int i = 0; i < m_Catalog.RowCount; i++)
            {
                SkiRunCatalogSystem.RunRow row = m_Catalog.GetRow(i);
                if (row.Gate != facts.Gate || row.RateState != SkiRunRatingRules.RateRated || float.IsNaN(row.TopM) || float.IsNaN(row.BottomM))
                    continue;
                areaBottom = Math.Min(areaBottom, row.BottomM);
                areaTop = Math.Max(areaTop, row.TopM);
            }
            if (areaTop > areaBottom)
                form = SkiRunNameRules.FormOf(facts.BottomM, facts.TopM, areaBottom, areaTop);
        }

        private void FillUsed()
        {
            Array.Clear(m_Used, 0, m_Used.Length);
            using (NativeArray<SkiRunName> names = m_Named.ToComponentDataArray<SkiRunName>(Allocator.Temp))
            {
                for (int i = 0; i < names.Length; i++)
                {
                    int index = names[i].m_Index;
                    if (index >= 0 && index < m_Used.Length)
                        m_Used[index] = true;
                }
            }
        }

        /// <summary>Glade-layer trees per run (all runs; only on a frame that names some).</summary>
        private void CountGladeTrees()
        {
            m_GladeTrees.Clear();
            if (m_Trees.IsEmptyIgnoreFilter)
                return;
            using (NativeArray<SkiRunTreeMember> trees = m_Trees.ToComponentDataArray<SkiRunTreeMember>(Allocator.Temp))
            {
                for (int i = 0; i < trees.Length; i++)
                {
                    if (trees[i].m_Layer != SkiRunTreeMember.LayerGlade)
                        continue;
                    m_GladeTrees.TryGetValue(trees[i].m_Run, out int n);
                    m_GladeTrees[trees[i].m_Run] = n + 1;
                }
            }
        }
    }
}
