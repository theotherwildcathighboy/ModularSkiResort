// The roadless mountain buildings' jobs live on their linked Welcome Sign (rows: hut 4, summit 8, patrol HQ 2, patrol hut 2).
// Pure arithmetic: MountainJobsRules.cs (tests/BuildingBatch). Why the sign: the four are top-level buildings that demand nothing,
// and a Workplace on a top-level building sets RequireRoad (BuildingInitializeSystem.cs:229-283); the sign has the road. This system
// gives a sign with a target > 0 a WorkProvider + Employee buffer and sets m_MaxWorkers to its target (what vanilla's ServiceUpgrade
// combine did for the owner). WorkProviderSystem's tick needs WorkplaceData on the sign PREFAB (:133, else it liquidates the staff):
// SkiResortGate.Prefab keeps its Workplace block with m_Workplaces 0, hence the WorkProvider add here (Workplace.cs:36-43).
// The carrier rule (MountainJobsRules header): while the area has an active road lodge, the nearest one to the sign carries these jobs
// and the own jobs of an off-road lodge on a cat track chain (SkiResortAttractivenessSystem.CarrierOf); the sign's target is then 0.
// This system writes the SIGN only; a carrier lodge's sum is read by OffSeasonStaffSystem (CarriedBy), the one writer for lodges.
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

        private bool m_Armed, m_Disabled, m_ErrorLogged, m_LoadScanDone, m_TargetsReady;
        private int m_Frame, m_Writes, m_Polls;

        private readonly List<Entity> m_Buildings = new List<Entity>(16);
        private readonly List<Entity> m_Signs = new List<Entity>(16);
        private readonly List<EntityRef> m_SignRefs = new List<EntityRef>(16);
        private readonly List<int> m_Jobs = new List<int>(16);
        private readonly List<bool> m_Live = new List<bool>(16);
        private readonly Dictionary<EntityRef, int> m_Targets = new Dictionary<EntityRef, int>();
        private readonly HashSet<Entity> m_Carriers = new HashSet<Entity>();
        private readonly List<Entity> m_Gone = new List<Entity>(4);
        // Per building: the lodge that carries its jobs (EntityRef.Null = its sign) and those jobs; summed per carrier lodge.
        private readonly List<EntityRef> m_CarrierRefs = new List<EntityRef>(16);
        private readonly List<int> m_CarrierJobs = new List<int>(16);
        private readonly Dictionary<EntityRef, int> m_LodgeTargets = new Dictionary<EntityRef, int>();

        /// <summary>The jobs <paramref name="lodge"/> carries for its area (0 when it carries none). OffSeasonStaffSystem adds them to its own.</summary>
        internal int CarriedBy(Entity lodge)
        {
            return m_LodgeTargets.TryGetValue(LiftCensusSystem.Ref(lodge), out int jobs) ? jobs : 0;
        }

        /// <summary>CarriedBy has read this load's links at least once (or this system stopped on a throw and keeps its last sums): before
        /// that, OffSeasonStaffSystem writes no lodge, so a load does not lay off a carrier's staff.</summary>
        internal bool TargetsReady => m_TargetsReady || m_Disabled;

        /// <summary>The sign carries jobs (MountainJobsRules.SignCarriesJobs): its target once TargetsReady, before that the workplaces the
        /// save holds. Read by the stats system for the SignNoRoad row.</summary>
        internal bool SignCarriesJobs(Entity sign)
        {
            m_Targets.TryGetValue(LiftCensusSystem.Ref(sign), out int target);
            bool has = EntityManager.TryGetComponent(sign, out WorkProvider wp);
            return MountainJobsRules.SignCarriesJobs(TargetsReady, target, has, has ? wp.m_MaxWorkers : 0);
        }

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
            Mod.Log.Info($"{nameof(MountainJobsSystem)} created (the roadless mountain buildings' jobs on their area's carrier: a road lodge, else the Welcome Sign; DECISIONS 347 c)");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_Disabled = m_ErrorLogged = m_LoadScanDone = m_TargetsReady = false;
            m_Frame = m_Writes = m_Polls = 0;
            m_Carriers.Clear();
            m_LodgeTargets.Clear();
            if (m_Armed)
                Mod.Log.Info("MountainJobs: armed — hut 4, summit 8, patrol HQ 2, patrol hut 2 job(s), and an off-road lodge's own jobs on a cat " +
                             "track chain, ride on the nearest active road lodge to the linked Welcome Sign, else on the sign's WorkProvider " +
                             $"(poll every {PollFrames} frames; a sign changes only when its sum does; lodges are written by OffSeason)");
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
            if (!m_PrefabCatalog.IsResolved || !m_Attractiveness.HasSnapshot)
                return;   // no recompute yet this load: no links, no carrier
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
                if (m_OffSeason.AwaitsFirstObservation(sign))
                    continue;   // its area's off-season state after this load is not known yet: keep the saved staff
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

        /// <summary>The published top-level links → per-sign targets and per-carrier-lodge sums (MountainJobsRules.SumBySign). A building
        /// whose area has a carrier lodge adds 0 to its sign, so the sign's target drops and the game's tick lays its staff off.</summary>
        private void CollectTargets()
        {
            m_Attractiveness.CopyTopLevelLinks(m_Buildings, m_Signs);
            m_SignRefs.Clear();
            m_Jobs.Clear();
            m_Live.Clear();
            m_CarrierRefs.Clear();
            m_CarrierJobs.Clear();
            for (int i = 0; i < m_Buildings.Count; i++)
            {
                Entity b = m_Buildings[i];
                Entity sign = m_Signs[i];
                bool live = EntityManager.Exists(b) && !EntityManager.HasComponent<Deleted>(b) && !EntityManager.HasComponent<Temp>(b);
                Entity carrier = sign == Entity.Null ? Entity.Null : m_Attractiveness.CarrierOf(sign);
                bool onLodge = carrier != Entity.Null && carrier != sign;
                int jobs = live ? SeasonalSignJobsOf(b, sign, out _) : 0;
                m_SignRefs.Add(sign == Entity.Null ? EntityRef.Null : LiftCensusSystem.Ref(sign));
                m_Jobs.Add(onLodge ? 0 : jobs);
                m_Live.Add(live);
                m_CarrierRefs.Add(onLodge ? LiftCensusSystem.Ref(carrier) : EntityRef.Null);
                m_CarrierJobs.Add(onLodge ? jobs : 0);
            }
            MountainJobsRules.SumBySign(m_SignRefs, m_Jobs, m_Live, m_Targets);
            MountainJobsRules.SumBySign(m_CarrierRefs, m_CarrierJobs, m_Live, m_LodgeTargets);
            m_TargetsReady = true;
        }

        /// <summary>The jobs a top-level building hands to its area's carrier: the catalog's SignJobs (0 for every row with its own Workplace),
        /// or an off-road lodge's own workplaces while its cat track chain holds (LodgeSupplyRule.JobsLeave; the prefab's WorkplaceData
        /// is the truth, the catalog's Jobs the fallback).</summary>
        private int SignJobsOf(Entity building, out string name)
        {
            name = null;
            if (!EntityManager.TryGetComponent(building, out PrefabRef pr))
                return 0;
            if (m_PrefabCatalog.LodgeDefs.TryGetValue(pr.m_Prefab, out LodgeDef lodge))
            {
                name = lodge.Id;
                if (!m_Attractiveness.IsOffRoadSupplied(building))
                    return lodge.SignJobs;
                name += " (off-road)";
                return EntityManager.TryGetComponent(pr.m_Prefab, out WorkplaceData wd) ? wd.m_MaxWorkers : lodge.Jobs;
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

        /// <summary>"summit 8 + patrol hut 2" for the live buildings whose jobs <paramref name="holder"/> carries, a sign or a carrier
        /// lodge (log and DUMP).</summary>
        private string BreakdownOf(Entity holder)
        {
            var names = new List<string>(4);
            var jobs = new List<int>(4);
            EntityRef id = LiftCensusSystem.Ref(holder);
            for (int i = 0; i < m_Buildings.Count && i < m_CarrierRefs.Count; i++)
            {
                EntityRef carriedBy = m_CarrierRefs[i].IsNull ? m_SignRefs[i] : m_CarrierRefs[i];
                if (carriedBy != id || !m_Live[i])
                    continue;
                int j = SeasonalSignJobsOf(m_Buildings[i], m_Signs[i], out string name);
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
            foreach (KeyValuePair<EntityRef, int> kv in m_LodgeTargets)
            {
                Entity lodge = LiftCensusSystem.Ent(kv.Key);
                if (!EntityManager.Exists(lodge))
                    continue;
                bool has = EntityManager.TryGetComponent(lodge, out WorkProvider wp);
                sb.Append(" carrier lodge ").Append(Id(lodge)).Append(": carries ").Append(kv.Value).Append(" (").Append(BreakdownOf(lodge))
                  .Append("), max workers ").Append(has ? wp.m_MaxWorkers.ToString(CultureInfo.InvariantCulture) : "none").Append(';');
            }
            sb.Append('\n');
        }

        private static string Id(Entity e) => e.Index.ToString(CultureInfo.InvariantCulture) + ":" + e.Version.ToString(CultureInfo.InvariantCulture);
    }
}
