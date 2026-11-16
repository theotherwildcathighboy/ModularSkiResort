// Stamps what our tools create with the rules level it was built under (SkiRulesStamp = SkiRulesLevel.Current; T229, DECISIONS 670). 1.0.2
// ships the stamp only: no sync engine, no button, nothing the player sees. The point is that every object built from now on carries its
// level, so the later sync steps (design docs/research/round2-2026-11-16/sync-resort/DESIGN.md) know what was built under which rules.
// What gets stamped, at creation only (the entity's Created tag, not Temp, not Deleted, no stamp yet):
//   * lift line sections (Game.Net.Edge of a lift line prefab, no Owner: a station's stub sub-net is not a section). Sections are edited
//     one by one, so a line is as current as its oldest section. Not in LiftTowerFitSystem: the fit's own load-time writes re-queue those
//     lines as "changed in session", so a fit-side stamp would mark old lines current.
//   * our catalog buildings: stations, lodges, the school, patrol huts, the bus depot, Welcome Signs,
//   * runs and cat tracks (ski ways are cat tracks with another profile, the same prefab).
// Elsewhere: SkiRunReemitSystem carries the stamp of a re-emitted run (an unstamped run's copy gets an explicit level 0), SkiMergeSystem
// stamps a merged building and removes the stamp on unmerge.
// Loaded objects are never stamped. Decompile (1.6.4f1): Created is no ISerializable (Created.cs, 1.6.4f1), so a save never holds it,
// and the load adds missing non-serialized components without it (RequiredComponentSystem.cs:752-801 in the serialization namespace, e.g. NodeGeometry,
// MeshBatch); only version upgrades add Updated (:1189-1205, :1391, :1517, :1814), never Created. On top, a guard skips the first
// GuardFrames frames of a load (our own load passes) and counts any of our objects that did carry Created there: one "Stamp [guard]" line,
// expected 0. Phase ModificationEnd (Mod.cs): MainLoop, paused or not, the frame's Created tag still on, after the re-emit's Modification1
// copy. Game mode only. Main thread. One "!!!" disables it until the next load (objects built then simply carry no stamp: level 0, safe).
// Logs: "Stamp [guard]" and "Stamp [census] load" once per load, "Stamp [new]" per frame that stamps, "Stamp [census]" once a build burst
// is quiet for CensusQuietFrames. LastCensusLine holds the newest census (dev read by reflection).
// Engine notes: EngineNotes/SkiRulesStampSystem.md

using System;
using System.Collections.Generic;
using System.Text;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRulesStampSystem : GameSystemBase
    {
        /// <summary>Frames after a load in which nothing is stamped (our own load passes; the guard of design 4.1 rule 4).</summary>
        internal const int GuardFrames = 120;

        /// <summary>Frames without a new stamp before the census line after a build burst.</summary>
        internal const int CensusQuietFrames = 300;

        /// <summary>The newest census line (load or after a build burst). Dev read (bridge reflection); null before the first.</summary>
        public static string LastCensusLine { get; private set; }

        /// <summary>The newest census counts per SkiRulesKind (same moment as LastCensusLine). Dev read.</summary>
        public static SkiRulesLevel.Count[] LastCensus { get; private set; }

        private SkiPrefabCatalogSystem m_Catalog;
        private SkiRunOwnershipSystem m_Ownership;
        private EntityQuery m_New, m_CreatedAny, m_Edges, m_Buildings, m_Areas, m_Merged;
        private bool m_Armed, m_Disabled, m_ErrorLogged, m_GuardLogged, m_LoadCensusDone;
        private int m_Frame, m_GuardSeen, m_GuardCreated, m_CensusDue;
        private readonly int[] m_NewByKind = new int[SkiRulesLevel.KindCount];

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Catalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_Ownership = World.GetOrCreateSystemManaged<SkiRunOwnershipSystem>();
            ComponentType[] anyKind =
            {
                ComponentType.ReadOnly<Game.Net.Edge>(),
                ComponentType.ReadOnly<Game.Buildings.Building>(),
                ComponentType.ReadOnly<Game.Areas.Area>(),
            };
            m_New = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<PrefabRef>() },
                Any = anyKind,
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<SkiRulesStamp>() },
            });
            // The guard's count: any of our objects with Created in a load's first frames, stamped or not
            m_CreatedAny = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<PrefabRef>() },
                Any = anyKind,
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            m_Edges = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Net.Edge>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Owner>() },
            });
            m_Buildings = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Buildings.Building>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            m_Areas = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Areas.Area>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            m_Merged = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<SkiMergedLodge>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            Mod.Log.Info($"{nameof(SkiRulesStampSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_Disabled = m_ErrorLogged = m_GuardLogged = m_LoadCensusDone = false;
            m_Frame = m_GuardSeen = m_GuardCreated = 0;
            m_CensusDue = -1;
            Array.Clear(m_NewByKind, 0, m_NewByKind.Length);
            LastCensusLine = null;
            LastCensus = null;
            if (m_Armed)
                Mod.Log.Info($"Stamp: ARMED — what our tools build from now on carries rules level {SkiRulesLevel.Current} (SkiRulesStamp v" +
                             $"{SkiRulesStamp.CurrentVersion}): lift sections, stations, lodges, school, patrol huts, bus depot, Welcome Signs, " +
                             $"runs, cat tracks, merged buildings; objects from the save keep theirs, none = level 0; nothing stamped in " +
                             $"the first {GuardFrames} frames");
        }

        protected override void OnUpdate()
        {
            if (!m_Armed || m_Disabled)
                return;
            try
            {
                m_Frame++;
                if (m_Frame <= GuardFrames)
                {
                    if (!m_CreatedAny.IsEmptyIgnoreFilter)
                        CountGuard();
                    return;
                }
                if (!m_GuardLogged)
                {
                    m_GuardLogged = true;
                    Mod.Log.Info($"Stamp [guard]: {m_GuardSeen} of our objects carried Created in the load's first {GuardFrames} frames " +
                                 $"(of {m_GuardCreated} new entities in all; expected 0 of ours: a save holds no Created tag, so loaded " +
                                 "objects are never taken for new)");
                }
                if (!m_LoadCensusDone && m_Ownership.PrefabSetsReady)
                {
                    m_LoadCensusDone = true;
                    LogCensus("load");
                }
                if (!m_New.IsEmptyIgnoreFilter)
                    StampNew();
                if (m_CensusDue >= 0 && m_Frame >= m_CensusDue)
                {
                    m_CensusDue = -1;
                    LogCensus("after building");
                }
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiRulesStampSystem)} EXCEPTION — objects built until the next load carry no rules stamp " +
                                  $"(they read as level 0, which is safe). Please report this stack:\n{e}");
                }
            }
        }

        /// <summary>The kind of an object of ours, or -1. Sets fetched once per pass (catalog LIVE VIEWs).</summary>
        private int KindOf(Entity entity, Entity prefab)
        {
            if (EntityManager.HasComponent<Game.Net.Edge>(entity))
                return !EntityManager.HasComponent<Owner>(entity) && m_Catalog.LiftLinePrefabSet.Contains(prefab) ? (int)SkiRulesKind.LiftEdge : -1;
            if (EntityManager.HasComponent<Game.Buildings.Building>(entity))
                return BuildingKind(prefab);
            if (EntityManager.HasComponent<Game.Areas.Area>(entity))
                return AreaKind(prefab);
            return -1;
        }

        private int BuildingKind(Entity prefab)
        {
            if (m_Catalog.StationPrefabSet.Contains(prefab)) return (int)SkiRulesKind.Station;
            if (m_Catalog.LodgePrefabSet.Contains(prefab)) return (int)SkiRulesKind.Lodge;
            if (m_Catalog.SchoolPrefabSet.Contains(prefab)) return (int)SkiRulesKind.School;
            if (m_Catalog.PatrolPrefabSet.Contains(prefab)) return (int)SkiRulesKind.Patrol;
            if (m_Catalog.DepotPrefabSet.Contains(prefab)) return (int)SkiRulesKind.Depot;
            if (m_Catalog.SignPrefabSet.Contains(prefab)) return (int)SkiRulesKind.Sign;
            return -1;
        }

        private int AreaKind(Entity prefab)
        {
            if (!m_Ownership.PrefabSetsReady)
                return -1;
            int k = m_Ownership.KindOf(prefab);
            return k < 0 ? -1 : k == (int)RunKind.CatTrack ? (int)SkiRulesKind.CatTrack : (int)SkiRulesKind.Run;
        }

        private void CountGuard()
        {
            using (NativeArray<Entity> entities = m_CreatedAny.ToEntityArray(Allocator.Temp))
            using (NativeArray<PrefabRef> prefabs = m_CreatedAny.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                m_GuardCreated += entities.Length;
                for (int i = 0; i < entities.Length; i++)
                {
                    if (KindOf(entities[i], prefabs[i].m_Prefab) >= 0)
                        m_GuardSeen++;
                }
            }
        }

        private void StampNew()
        {
            int stamped = 0;
            Array.Clear(m_NewByKind, 0, m_NewByKind.Length);
            using (NativeArray<Entity> entities = m_New.ToEntityArray(Allocator.Temp))
            using (NativeArray<PrefabRef> prefabs = m_New.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    int kind = KindOf(entities[i], prefabs[i].m_Prefab);
                    if (kind < 0)
                        continue;
                    EntityManager.AddComponentData(entities[i], new SkiRulesStamp(SkiRulesLevel.Current));
                    m_NewByKind[kind]++;
                    stamped++;
                }
            }
            if (stamped == 0)
                return;
            var sb = new StringBuilder();
            for (int k = 0; k < m_NewByKind.Length; k++)
            {
                if (m_NewByKind[k] == 0)
                    continue;
                sb.Append(sb.Length > 0 ? ", " : string.Empty).Append(m_NewByKind[k]).Append(' ').Append(SkiRulesLevel.Label((SkiRulesKind)k));
            }
            Mod.Log.Info($"Stamp [new]: {sb} at rules level {SkiRulesLevel.Current}");
            m_CensusDue = m_Frame + CensusQuietFrames;
        }

        /// <summary>A merged building is built under this build's rules (SkiMergeSystem.Merge). Keeps an existing stamp's flags.</summary>
        internal static void StampMerged(EntityManager em, Entity building)
        {
            if (em.TryGetComponent(building, out SkiRulesStamp old))
            {
                old.m_Rules = SkiRulesLevel.Current;
                em.SetComponentData(building, old);
            }
            else
            {
                em.AddComponentData(building, new SkiRulesStamp(SkiRulesLevel.Current));
            }
        }

        /// <summary>An unmerged building is plain vanilla again: no stamp (SkiMergeSystem.Strip).</summary>
        internal static void UnstampMerged(EntityManager em, Entity building)
        {
            if (em.HasComponent<SkiRulesStamp>(building))
                em.RemoveComponent<SkiRulesStamp>(building);
        }

        /// <summary>A re-created object keeps its source's level (SkiRunReemitSystem): an unstamped source gives an explicit level 0, so the
        /// stamp-new pass leaves the copy alone. Call before the source is deleted.</summary>
        internal static void CarryOver(EntityManager em, Entity from, Entity to)
        {
            bool has = em.TryGetComponent(from, out SkiRulesStamp stamp);
            SkiRulesStamp copy = has ? stamp : new SkiRulesStamp(SkiRulesLevel.Unknown);
            copy.m_Rules = SkiRulesLevel.CarriedLevel(has, stamp.m_Rules);
            if (em.HasComponent<SkiRulesStamp>(to))
                em.SetComponentData(to, copy);
            else
                em.AddComponentData(to, copy);
        }

        /// <summary>Counts every object of ours per kind and level. Main thread, a few queries over buildings, areas and edges: once per load
        /// and once per quiet build burst, never per frame.</summary>
        internal SkiRulesLevel.Count[] Census()
        {
            var counts = new SkiRulesLevel.Count[SkiRulesLevel.KindCount];
            CensusQuery(m_Edges, counts, prefab => m_Catalog.LiftLinePrefabSet.Contains(prefab) ? (int)SkiRulesKind.LiftEdge : -1);
            CensusQuery(m_Buildings, counts, BuildingKind);
            CensusQuery(m_Areas, counts, AreaKind);
            using (NativeArray<Entity> merged = m_Merged.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < merged.Length; i++)
                {
                    bool has = EntityManager.TryGetComponent(merged[i], out SkiRulesStamp s);
                    counts[(int)SkiRulesKind.Merged].Add(SkiRulesLevel.LevelOf(has, s.m_Rules));
                }
            }
            return counts;
        }

        private void CensusQuery(EntityQuery query, SkiRulesLevel.Count[] counts, Func<Entity, int> kindOf)
        {
            if (query.IsEmptyIgnoreFilter)
                return;
            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
            using (NativeArray<PrefabRef> prefabs = query.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    int kind = kindOf(prefabs[i].m_Prefab);
                    if (kind < 0)
                        continue;
                    bool has = EntityManager.TryGetComponent(entities[i], out SkiRulesStamp s);
                    counts[kind].Add(SkiRulesLevel.LevelOf(has, s.m_Rules));
                }
            }
        }

        private void LogCensus(string when)
        {
            SkiRulesLevel.Count[] counts = Census();
            LastCensus = counts;
            LastCensusLine = SkiRulesLevel.Describe(counts);
            Mod.Log.Info($"Stamp [census] {when}: {LastCensusLine}");
        }
    }
}
