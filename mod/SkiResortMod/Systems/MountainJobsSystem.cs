// MountainJobsSystem — the roadless mountain buildings' jobs live on their linked Welcome Sign (Richard, DECISIONS 347 (c),
// 2026-10-20: "Summit lodge no road for now, but it should add jobs via simulation like the other buildings"; all four roadless
// rows: hut 4, summit 8, patrol HQ 2, patrol hut 2). Build note docs/research/field/2026-10-20/sim-round-build.md. Created
// 2026-10-20. Pure arithmetic: MountainJobsRules.cs (tests/BuildingBatch J1-J8).
//
// WHY THE SIGN. Since T113 (DECISIONS 344) the four are top-level and demand nothing: a Workplace on a top-level building sets
// RequireRoad (BuildingInitializeSystem.cs:229-283) and they stand on slopes. The sign has the road. Before T113 the engine's
// ServiceUpgrade combine gave the sign its children's workplaces: ServiceUpgradeSystem.UpgradeInstalled added the child's upgrade
// components (Workplace.GetUpgradeComponents = WorkProvider + Employee, Workplace.cs:45-52) to the owner at runtime
// (ServiceUpgradeSystem.cs:137-154), and CityServiceWorkplaceInitializeSystem wrote the owner's WorkProvider.m_MaxWorkers =
// CityUtils.GetCityServiceWorkplaceMaxWorkers (owner + every installed upgrade, CityUtils.cs:41-78) when an upgrade was Created
// or Deleted (CityServiceWorkplaceInitializeSystem.cs:97-121, query :222-248). This system does the same two things for the
// distance link (SchoolLinkRules, SkiResortAttractivenessSystem.CopyTopLevelLinks):
//   * a sign with a target > 0 and no WorkProvider gets WorkProvider + an Employee buffer (the two components vanilla adds), with
//     vanilla's new-workplace grace m_EfficiencyCooldown = -m_ServiceBuildingEfficiencyGracePeriod (:111, :119);
//   * a sign whose m_MaxWorkers differs from its target is set (0 too: the component stays and the tick lays the staff off).
// THE ENGINE PATH THAT READS IT: WorkProviderSystem's tick (query :513-531: WorkProvider + PrefabRef + Employee, Building; every
// 512 frames per UpdateFrame slice, :552) takes reference.m_MaxWorkers as the entity's size for every non-company, non-school
// building (:133-184): workplaces by education (EconomyUtils.CalculateNumberOfWorkplaces with the SIGN prefab's
// WorkplaceData.m_Complexity), FreeWorkplaces (the job market, :199-230), the staffing factors in the sign's Efficiency buffer
// (:252-281) and the workers notifications. It never recomputes m_MaxWorkers for a city service building (only schools and
// outside connections, :153-168), and CityServiceWorkplaceInitializeSystem runs only when the sign itself or an upgrade of it is
// Created / Deleted, so the value we write stays; this system re-asserts it every poll anyway. The tick needs WorkplaceData on
// the sign PREFAB (:133, else it liquidates the staff): SkiResortGate.Prefab keeps its Workplace block with m_Workplaces 0
// (BuildingBatch J8 gates it); with 0 the archetype gets no WorkProvider (Workplace.cs:36-43), hence the add here.
// Saves: WorkProvider and Employee serialize (ISerializable), so a load keeps them; the first poll re-asserts.
//
// SYNC: every PollFrames simulation frames, Game mode only. Targets = SumBySign over the published top-level links (place /
// remove / relink / Move It / sign bulldozed all reach those links at the next recompute). Carriers = every sign that has a
// target or a WorkProvider (the load pass scans the signs once). Inactive buildings count (vanilla's combine counted Inactive
// upgrades). A sign that is gone drops out. No component is ever removed.
//
// LOGS: "created"; one load line; one line per sign change "MountainJobs [sign] Welcome Sign i:v workplaces a -> b (summit 8 +
// patrol hut 2; ...)"; "!!!" once on a throw (then disabled until the next load). DUMP: AppendDump (hub "Dump diagnostics").
//
// No Harmony, no reflection, no prefab write. Phase GameSimulation (not load-bearing: the tick reads the value whenever it runs).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Companies;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class MountainJobsSystem : GameSystemBase
    {
        internal const int PollFrames = 64;

        private SkiPrefabCatalogSystem m_PrefabCatalog;
        private SkiResortAttractivenessSystem m_Attractiveness;
        private EntityQuery m_SignProviderQuery;
        private EntityQuery m_EfficiencyParamQuery;

        private bool m_Armed, m_Disabled, m_ErrorLogged, m_LoadScanDone;
        private int m_Frame, m_Writes, m_Polls;

        private readonly List<Entity> m_Buildings = new List<Entity>(16);
        private readonly List<Entity> m_Signs = new List<Entity>(16);
        private readonly List<EntityRef> m_SignRefs = new List<EntityRef>(16);
        private readonly List<int> m_Jobs = new List<int>(16);
        private readonly List<bool> m_Live = new List<bool>(16);
        private readonly Dictionary<EntityRef, int> m_Targets = new Dictionary<EntityRef, int>();
        private readonly HashSet<Entity> m_Carriers = new HashSet<Entity>();
        private readonly List<Entity> m_Gone = new List<Entity>(4);

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_Attractiveness = World.GetOrCreateSystemManaged<SkiResortAttractivenessSystem>();
            m_SignProviderQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<WorkProvider>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            // The vanilla singleton query (CityServiceWorkplaceInitializeSystem.__AssignQueries: IncludeSystems).
            m_EfficiencyParamQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<BuildingEfficiencyParameterData>() },
                Options = EntityQueryOptions.IncludeSystems,
            });
            Mod.Log.Info($"{nameof(MountainJobsSystem)} created (the roadless mountain buildings' jobs on their linked Welcome Sign, DECISIONS 347 c)");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_Disabled = m_ErrorLogged = m_LoadScanDone = false;
            m_Frame = m_Writes = m_Polls = 0;
            m_Carriers.Clear();
            if (m_Armed)
                Mod.Log.Info("MountainJobs: armed — hut 4, summit 8, patrol HQ 2, patrol hut 2 job(s) ride on the linked Welcome Sign's " +
                             $"WorkProvider (poll every {PollFrames} frames; a sign changes only when its sum does)");
        }

        protected override void OnUpdate()
        {
            if (!m_Armed || m_Disabled)
                return;
            if (++m_Frame % PollFrames != 0)
                return;
            try
            {
                Sync();
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(MountainJobsSystem)} EXCEPTION - mountain jobs frozen at their last value until the next " +
                                  $"game load. Please report this stack:\n{e}");
                }
            }
        }

        private void Sync()
        {
            if (!m_PrefabCatalog.IsResolved)
                return;
            m_Polls++;
            if (!m_LoadScanDone)
            {
                m_LoadScanDone = true;
                using (NativeArray<Entity> entities = m_SignProviderQuery.ToEntityArray(Allocator.Temp))
                using (NativeArray<PrefabRef> prefabs = m_SignProviderQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
                {
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (m_PrefabCatalog.IsSign(prefabs[i].m_Prefab))
                            m_Carriers.Add(entities[i]);
                    }
                }
            }

            CollectTargets();
            foreach (KeyValuePair<EntityRef, int> kv in m_Targets)
                m_Carriers.Add(LiftCensusSystem.Ent(kv.Key));

            m_Gone.Clear();
            foreach (Entity sign in m_Carriers)
            {
                if (!EntityManager.Exists(sign) || EntityManager.HasComponent<Deleted>(sign) || EntityManager.HasComponent<Temp>(sign))
                {
                    m_Gone.Add(sign);
                    continue;
                }
                m_Targets.TryGetValue(LiftCensusSystem.Ref(sign), out int target);
                bool has = EntityManager.TryGetComponent(sign, out WorkProvider wp);
                int current = has ? wp.m_MaxWorkers : 0;
                if (!MountainJobsRules.NeedsWrite(has, current, target))
                    continue;
                bool grace = MountainJobsRules.StartsGrace(has, current, target);
                short graceValue = GracePeriod();
                if (!has)
                {
                    EntityManager.AddComponentData(sign, new WorkProvider
                    {
                        m_MaxWorkers = target,
                        m_EfficiencyCooldown = (short)(-graceValue),
                    });
                    if (!EntityManager.HasBuffer<Employee>(sign))
                        EntityManager.AddBuffer<Employee>(sign);
                }
                else
                {
                    wp.m_MaxWorkers = target;
                    if (grace)
                        wp.m_EfficiencyCooldown = (short)(-graceValue);
                    EntityManager.SetComponentData(sign, wp);
                }
                m_Writes++;
                Mod.Log.Info($"MountainJobs [sign] Welcome Sign {Id(sign)} workplaces {current} -> {target} ({BreakdownOf(sign)}" +
                             (has ? string.Empty : "; WorkProvider + Employee added, as ServiceUpgradeSystem does for an upgrade") +
                             (grace ? $"; vanilla grace {graceValue}" : string.Empty) +
                             (target == 0 ? "; staff over the target are laid off by the game's tick" : "; staff ride up") + ")");
            }
            for (int i = 0; i < m_Gone.Count; i++)
                m_Carriers.Remove(m_Gone[i]);
        }

        /// <summary>The published top-level links → per-sign targets (MountainJobsRules.SumBySign).</summary>
        private void CollectTargets()
        {
            m_Attractiveness.CopyTopLevelLinks(m_Buildings, m_Signs);
            m_SignRefs.Clear();
            m_Jobs.Clear();
            m_Live.Clear();
            for (int i = 0; i < m_Buildings.Count; i++)
            {
                Entity b = m_Buildings[i];
                bool live = EntityManager.Exists(b) && !EntityManager.HasComponent<Deleted>(b) && !EntityManager.HasComponent<Temp>(b);
                m_SignRefs.Add(m_Signs[i] == Entity.Null ? EntityRef.Null : LiftCensusSystem.Ref(m_Signs[i]));
                m_Jobs.Add(live ? SignJobsOf(b, out _) : 0);
                m_Live.Add(live);
            }
            MountainJobsRules.SumBySign(m_SignRefs, m_Jobs, m_Live, m_Targets);
        }

        /// <summary>The catalog's SignJobs of a top-level building's prefab (0 for every row with its own Workplace).</summary>
        private int SignJobsOf(Entity building, out string name)
        {
            name = null;
            if (!EntityManager.TryGetComponent(building, out PrefabRef pr))
                return 0;
            if (m_PrefabCatalog.LodgeDefs.TryGetValue(pr.m_Prefab, out LodgeDef lodge))
            {
                name = lodge.Id;
                return lodge.SignJobs;
            }
            if (m_PrefabCatalog.PatrolDefs.TryGetValue(pr.m_Prefab, out PatrolDef patrol))
            {
                name = patrol.BuildItemId == "patrolHQ" ? "patrol HQ" : "patrol hut";
                return patrol.SignJobs;
            }
            return 0;
        }

        /// <summary>"summit 8 + patrol hut 2" for the live buildings linked to <paramref name="sign"/> (log and DUMP).</summary>
        private string BreakdownOf(Entity sign)
        {
            var names = new List<string>(4);
            var jobs = new List<int>(4);
            for (int i = 0; i < m_Buildings.Count; i++)
            {
                if (m_Signs[i] != sign || !m_Live[i])
                    continue;
                int j = SignJobsOf(m_Buildings[i], out string name);
                if (j <= 0)
                    continue;
                names.Add(name + " " + Id(m_Buildings[i]));
                jobs.Add(j);
            }
            return MountainJobsRules.Breakdown(names, jobs);
        }

        private short GracePeriod()
        {
            if (m_EfficiencyParamQuery.IsEmptyIgnoreFilter)
                return 0;
            return m_EfficiencyParamQuery.GetSingleton<BuildingEfficiencyParameterData>().m_ServiceBuildingEfficiencyGracePeriod;
        }

        /// <summary>DUMP: per carrier sign its WorkProvider and staff vs the target and the breakdown.</summary>
        internal void AppendDump(StringBuilder sb)
        {
            sb.Append("  mountain jobs (DECISIONS 347 c): armed ").Append(m_Armed).Append(", disabled ").Append(m_Disabled)
              .Append(", polls ").Append(m_Polls).Append(", writes ").Append(m_Writes).Append(';');
            if (m_Carriers.Count == 0)
                sb.Append(" no sign carries mountain jobs");
            foreach (Entity sign in m_Carriers)
            {
                if (!EntityManager.Exists(sign))
                    continue;
                m_Targets.TryGetValue(LiftCensusSystem.Ref(sign), out int target);
                bool has = EntityManager.TryGetComponent(sign, out WorkProvider wp);
                int staff = EntityManager.HasBuffer<Employee>(sign) ? EntityManager.GetBuffer<Employee>(sign, isReadOnly: true).Length : 0;
                sb.Append(" sign ").Append(Id(sign)).Append(": max workers ").Append(has ? wp.m_MaxWorkers.ToString(CultureInfo.InvariantCulture) : "none")
                  .Append(", target ").Append(target).Append(", staff ").Append(staff)
                  .Append(", efficiency cooldown ").Append(has ? wp.m_EfficiencyCooldown : 0)
                  .Append(" (").Append(BreakdownOf(sign)).Append(");");
            }
            sb.Append('\n');
        }

        private static string Id(Entity e) => e.Index.ToString(CultureInfo.InvariantCulture) + ":" + e.Version.ToString(CultureInfo.InvariantCulture);
    }
}
