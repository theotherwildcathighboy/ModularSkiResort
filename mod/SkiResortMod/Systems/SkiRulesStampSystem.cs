// Stamps what our tools create with the rules level it was built under (SkiRulesStamp = SkiRulesLevel.Current; T229, DECISIONS 670). Every
// object built from now on carries its level, so the Sync engine (SkiSyncSystem, design docs/research/round2-2026-11-16/sync-resort/DESIGN.md)
// knows which steps it already passed. It also marks lift sections changed by a tool that is not ours (FlagHandEdited, MarkHandEdited).
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
        private ToolSystem m_ToolSystem;
        private EntityQuery m_New, m_CreatedAny, m_Edges, m_Buildings, m_Areas, m_Merged, m_LiftTouched;
        private int m_HandMarked;
        private bool m_Armed, m_Disabled, m_ErrorLogged, m_GuardLogged, m_LoadCensusDone;
        private int m_Frame, m_GuardSeen, m_GuardCreated, m_CensusDue;
        private readonly int[] m_NewByKind = new int[SkiRulesLevel.KindCount];

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Catalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_Ownership = World.GetOrCreateSystemManaged<SkiRunOwnershipSystem>();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
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
            // The hand-edit marker: lift sections and tower nodes built or changed this frame (filtered to lift prefabs in the pass)
            m_LiftTouched = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Game.Net.Edge>() },
                Any = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Updated>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Owner>() },
            }, new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Game.Net.Node>() },
                Any = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Updated>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Owner>() },
            });
            Mod.Log.Info($"{nameof(SkiRulesStampSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_Disabled = m_ErrorLogged = m_GuardLogged = m_LoadCensusDone = false;
            m_Frame = m_GuardSeen = m_GuardCreated = m_HandMarked = 0;
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
                string handTool = HandToolName();
                if (!m_New.IsEmptyIgnoreFilter)
                    StampNew(handTool);
                if (handTool != null && !m_LiftTouched.IsEmptyIgnoreFilter)
                    MarkHandEdited(handTool);
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

        private void StampNew(string handTool)
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
                    var stamp = new SkiRulesStamp(SkiRulesLevel.Current);
                    if (handTool != null && kind == (int)SkiRulesKind.LiftEdge)
                        stamp.m_Flags |= SkiRulesLevel.FlagHandEdited;   // a section drawn by a tool that is not ours (MarkHandEdited logs it)
                    EntityManager.AddComponentData(entities[i], stamp);
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

        /// <summary>The name of the active tool when it is a tool that edits lift lines by hand (not ours, not the default tool, not the
        /// bulldozer, not the terrain tool), else null. Our tools and the tower fit write lift nodes under our own rules; the bulldozer only
        /// removes; the terrain brush changes the ground under a line, not its towers. Anything else that builds or changes a lift section or a
        /// tower node (Move It, the vanilla elevation or replace tools, a relocated station) is a hand edit (design 4.6, DECISIONS 670).</summary>
        private string HandToolName()
        {
            ToolBaseSystem tool = m_ToolSystem.activeTool;
            if (tool == null || tool is DefaultToolSystem || tool is BulldozeToolSystem || tool is TerrainToolSystem)
                return null;
            if (tool.GetType().Assembly == typeof(SkiRulesStampSystem).Assembly)
                return null;
            return tool.toolID ?? tool.GetType().Name;
        }

        /// <summary>Marks the lift sections built or changed this frame by a hand tool: m_Flags HandEdited on the section, and on every
        /// section of a changed tower node. A section without a stamp gets one at level 0 (absent and 0 read the same) so the flag has a
        /// home. A step that re-plans towers lists and skips these sections (SyncStep.SkipsHandEdited). The flag is never cleared.</summary>
        private void MarkHandEdited(string handTool)
        {
            HashSet<Entity> lines = m_Catalog.LiftLinePrefabSet;
            int marked = 0;
            using (NativeArray<Entity> entities = m_LiftTouched.ToEntityArray(Allocator.Temp))
            using (NativeArray<PrefabRef> prefabs = m_LiftTouched.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (!lines.Contains(prefabs[i].m_Prefab))
                        continue;
                    Entity e = entities[i];
                    if (EntityManager.HasComponent<Game.Net.Edge>(e))
                    {
                        marked += MarkSection(e);
                    }
                    else if (EntityManager.TryGetBuffer(e, true, out DynamicBuffer<Game.Net.ConnectedEdge> connected))
                    {
                        for (int c = 0; c < connected.Length; c++)
                        {
                            Entity edge = connected[c].m_Edge;
                            if (!EntityManager.HasComponent<Owner>(edge) && !EntityManager.HasComponent<Temp>(edge) &&
                                !EntityManager.HasComponent<Deleted>(edge) && EntityManager.TryGetComponent(edge, out PrefabRef ep) &&
                                lines.Contains(ep.m_Prefab))
                                marked += MarkSection(edge);
                        }
                    }
                }
            }
            if (marked == 0)
                return;
            m_HandMarked += marked;
            Mod.Log.Info($"Stamp [hand]: {marked} lift section(s) marked edited by hand (tool {handTool}; {m_HandMarked} this session). " +
                         "A later tower re-plan lists and skips them");
        }

        /// <summary>1 when the flag was newly set on this section.</summary>
        private int MarkSection(Entity edge)
        {
            if (EntityManager.TryGetComponent(edge, out SkiRulesStamp s))
            {
                if ((s.m_Flags & SkiRulesLevel.FlagHandEdited) != 0)
                    return 0;
                s.m_Flags |= SkiRulesLevel.FlagHandEdited;
                EntityManager.SetComponentData(edge, s);
                return 1;
            }
            var stamp = new SkiRulesStamp(SkiRulesLevel.Unknown);
            stamp.m_Flags = SkiRulesLevel.FlagHandEdited;
            EntityManager.AddComponentData(edge, stamp);
            return 1;
        }

        /// <summary>The load census ran (the guard is over and the run prefab sets were ready). The sync intake waits for it.</summary>
        internal bool LoadSettled => m_Armed && !m_Disabled && m_LoadCensusDone;

        /// <summary>The kind of an object of ours (SkiRulesKind as int), or -1. For the sync intake.</summary>
        internal int KindOfEntity(Entity entity)
        {
            return EntityManager.TryGetComponent(entity, out PrefabRef pr) ? KindOf(entity, pr.m_Prefab) : -1;
        }

        /// <summary>Stamps an object Current (a sync step finished, "Keep as is", or a silent intake verdict). Keeps the flags and adds
        /// <paramref name="addFlags"/>.</summary>
        internal static void StampCurrent(EntityManager em, Entity entity, byte addFlags) =>
            StampLevel(em, entity, SkiRulesLevel.Current, addFlags);

        /// <summary>Stamps an object at <paramref name="level"/> (a sync step finished and a later one waits). Keeps the flags.</summary>
        internal static void StampLevel(EntityManager em, Entity entity, int level, byte addFlags)
        {
            if (em.TryGetComponent(entity, out SkiRulesStamp s))
            {
                s.m_Rules = level;
                s.m_Flags |= addFlags;
                em.SetComponentData(entity, s);
                return;
            }
            var stamp = new SkiRulesStamp(level);
            stamp.m_Flags = addFlags;
            em.AddComponentData(entity, stamp);
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
                    counts[(int)SkiRulesKind.Merged].Add(SkiRulesLevel.LevelOf(has, s.m_Rules), has ? s.m_Flags : (byte)0);
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
                    counts[kind].Add(SkiRulesLevel.LevelOf(has, s.m_Rules), has ? s.m_Flags : (byte)0);
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
