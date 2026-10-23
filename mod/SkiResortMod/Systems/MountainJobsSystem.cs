// The roadless mountain buildings' jobs live on their linked Welcome Sign (rows: hut 4, summit 8, patrol HQ 2, patrol hut 2).
// Pure arithmetic: MountainJobsRules.cs (tests/BuildingBatch). Why the sign: the four are top-level buildings that demand nothing,
// and a Workplace on a top-level building sets RequireRoad (BuildingInitializeSystem.cs:229-283); the sign has the road. This system
// gives a sign with a target > 0 a WorkProvider + Employee buffer and sets m_MaxWorkers to its target (what vanilla's ServiceUpgrade
// combine did for the owner). WorkProviderSystem's tick needs WorkplaceData on the sign PREFAB (:133, else it liquidates the staff):
// SkiResortGate.Prefab keeps its Workplace block with m_Workplaces 0, hence the WorkProvider add here (Workplace.cs:36-43).
// Sync: every PollFrames simulation frames, Game mode only; no component is ever removed. Phase GameSimulation (not load-bearing).
// Engine notes: EngineNotes/MountainJobsSystem.md

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
        private OffSeasonStaffSystem m_OffSeason;  // the hut's and the summit lodge's sign jobs halve off-season
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
            m_OffSeason = World.GetOrCreateSystemManaged<OffSeasonStaffSystem>();
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
                m_Jobs.Add(live ? SeasonalSignJobsOf(b, m_Signs[i], out _) : 0);
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

        /// <summary>SignJobsOf through MountainJobsRules.SignJobsFor: 0 for an Inactive building, halved for a LODGE row while its
        /// sign's area is off-season and the setting is on (OffSeasonStaffSystem.IsHalf); patrol rows stay full.</summary>
        private int SeasonalSignJobsOf(Entity building, Entity sign, out string name)
        {
            int full = SignJobsOf(building, out name);
            if (full <= 0)
                return 0;
            bool inactive = EntityManager.TryGetComponent(building, out Building bld) && BuildingUtils.CheckOption(bld, BuildingOption.Inactive);
            bool lodge = EntityManager.TryGetComponent(building, out PrefabRef pr) && m_PrefabCatalog.LodgeDefs.ContainsKey(pr.m_Prefab);
            bool half = lodge && m_OffSeason.IsHalf(sign);
            int jobs = MountainJobsRules.SignJobsFor(full, inactive, lodge, half);
            if (inactive)
                name += " (Inactive: 0)";
            else if (jobs != full)
                name += " (off-season half)";
            return jobs;
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
                int j = SeasonalSignJobsOf(m_Buildings[i], sign, out string name);
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
