// Lodges and the ski school run at half staff while their ski area is off-season. Pure rules and engine citations:
// OffSeasonRules.cs. Phase GameSimulation, not load-bearing (WorkProviderSystem's tick reads the value whenever it runs).
// Every PollFrames sim frames (Game mode, setting SkiResortSettings.OffSeasonHalfStaff):
//   * per Welcome Sign, OffSeasonRules.Step on its released score (SkiResortAttractivenessSystem.TryGetAreaScore: known and not
//     WriteHeld; noOpenRun = Result.RunsCounted == 0). An area with no released score is not stepped and reads in season;
//   * per top-level lodge / school with its own WorkProvider: m_MaxWorkers = OffSeasonRules.Target(prefab
//     WorkplaceData.m_MaxWorkers, area off, setting). Lowering it lets the game's tick lay the surplus off
//     (WorkProviderSystem.cs:199-230); raising it restarts vanilla's new-workplace grace (m_EfficiencyCooldown =
//     -m_ServiceBuildingEfficiencyGracePeriod, CityServiceWorkplaceInitializeSystem.cs:111, :119) so the hiring weeks do not
//     cut efficiency (WorkProviderSystem.cs:259-263 saturates a negative cooldown to no penalty);
//   * the hut's and the summit lodge's jobs ride on the sign: MountainJobsSystem asks IsHalf(sign) and halves them.
// Nothing vanilla rewrites m_MaxWorkers of a city service building after its Created frame, so this system re-asserts on every
// poll. Turning the setting off restores every building on the next poll. The first poll after a load takes the area state at
// once (WorkProvider serializes).
// Upkeep is not halved, only the wage row (OffSeasonRules header).
// Logs: "created"; ARMED once per load; one "OffSeason [area]" line per transition and one "OffSeason [staff]" line per write;
// "!!!" once on a throw (disabled until the next load). AppendDump feeds the hub's "Dump diagnostics".

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
using Game.Simulation;
using Game.Tools;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class OffSeasonStaffSystem : GameSystemBase
    {
        internal const int PollFrames = 256;

        private SkiPrefabCatalogSystem m_PrefabCatalog;
        private SkiResortAttractivenessSystem m_Attractiveness;
        private SimulationSystem m_Simulation;
        private EntityQuery m_EfficiencyParamQuery;

        private bool m_Armed, m_Disabled, m_ErrorLogged, m_LastEnabled;
        private int m_Frame, m_Writes, m_Polls;

        private readonly Dictionary<Entity, OffSeasonState> m_Areas = new Dictionary<Entity, OffSeasonState>();
        private readonly List<Entity> m_Buildings = new List<Entity>(16);
        private readonly List<Entity> m_Signs = new List<Entity>(16);
        private readonly HashSet<Entity> m_SeenSigns = new HashSet<Entity>();
        private readonly List<Entity> m_GoneSigns = new List<Entity>(4);

        /// <summary>The setting, read live (on when settings are not loaded).</summary>
        internal static bool EnabledSetting => Mod.Settings?.OffSeasonHalfStaff ?? OffSeasonRules.DefaultEnabled;

        /// <summary>True while <paramref name="sign"/>'s area is off-season and the setting is on (MountainJobsSystem halves the
        /// hut's and the summit lodge's sign jobs then). False for Entity.Null, an unknown area, or before the first poll.</summary>
        internal bool IsHalf(Entity sign)
        {
            return m_Armed && !m_Disabled && sign != Entity.Null && EnabledSetting &&
                   m_Areas.TryGetValue(sign, out OffSeasonState s) && s.Known && s.Off;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_Attractiveness = World.GetOrCreateSystemManaged<SkiResortAttractivenessSystem>();
            m_Simulation = World.GetOrCreateSystemManaged<SimulationSystem>();
            // Vanilla singleton query (CityServiceWorkplaceInitializeSystem.__AssignQueries: IncludeSystems), as MountainJobsSystem
            m_EfficiencyParamQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<BuildingEfficiencyParameterData>() },
                Options = EntityQueryOptions.IncludeSystems,
            });
            Mod.Log.Info($"{nameof(OffSeasonStaffSystem)} created (lodges and the ski school at half staff off-season, T137 / DECISIONS 358)");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_Disabled = m_ErrorLogged = false;
            m_Frame = m_Writes = m_Polls = 0;
            m_Areas.Clear();
            m_LastEnabled = EnabledSetting;
            if (m_Armed)
                Mod.Log.Info($"OffSeason: ARMED — setting {(m_LastEnabled ? "on" : "off")}; an area is off-season after " +
                             $"{OffSeasonRules.HoldToOffFrames} sim frames (12 game hours) with no open run, in season after " +
                             $"{OffSeasonRules.HoldToOnFrames} (one game hour) with one; off-season the A-frame, chalet, grand lodge and " +
                             "school keep half their workplaces (rounded up) and the hut / summit jobs on the sign halve; patrol stays full; " +
                             "upkeep is not halved (vanilla's budget formula has no honest half for it; Inactive = upkeep x0.1, no wages)");
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
                    Mod.Log.Error($"!!! {nameof(OffSeasonStaffSystem)} EXCEPTION - lodge staff frozen at their last value until the next " +
                                  $"game load (MountainJobs reads full). Please report this stack:\n{e}");
                }
            }
        }

        private void Sync()
        {
            if (!m_PrefabCatalog.IsResolved)
                return;
            m_Polls++;
            bool enabled = EnabledSetting;
            if (enabled != m_LastEnabled)
            {
                m_LastEnabled = enabled;
                Mod.Log.Info($"OffSeason: setting turned {(enabled ? "on" : "off")} — staff follow on this poll");
            }
            uint now = m_Simulation.frameIndex;

            m_Attractiveness.CopyTopLevelLinks(m_Buildings, m_Signs);
            m_SeenSigns.Clear();
            for (int i = 0; i < m_Signs.Count; i++)
            {
                Entity sign = m_Signs[i];
                if (sign == Entity.Null || !m_SeenSigns.Add(sign))
                    continue;
                StepArea(sign, now, enabled);
            }
            m_GoneSigns.Clear();
            foreach (KeyValuePair<Entity, OffSeasonState> kv in m_Areas)
            {
                if (!m_SeenSigns.Contains(kv.Key))
                    m_GoneSigns.Add(kv.Key);
            }
            for (int i = 0; i < m_GoneSigns.Count; i++)
                m_Areas.Remove(m_GoneSigns[i]);

            for (int i = 0; i < m_Buildings.Count; i++)
            {
                Entity b = m_Buildings[i];
                if (!EntityManager.Exists(b) || EntityManager.HasComponent<Deleted>(b) || EntityManager.HasComponent<Temp>(b))
                    continue;
                if (!EntityManager.TryGetComponent(b, out PrefabRef pr) || !IsSeasonal(pr.m_Prefab, out string name))
                    continue;
                if (!EntityManager.TryGetComponent(b, out WorkProvider wp))
                    continue;   // jobs ride on the sign (MountainJobsSystem)
                if (!EntityManager.TryGetComponent(pr.m_Prefab, out WorkplaceData wd))
                    continue;
                bool off = m_Signs[i] != Entity.Null && m_Areas.TryGetValue(m_Signs[i], out OffSeasonState st) && st.Known && st.Off;
                int target = OffSeasonRules.Target(wd.m_MaxWorkers, off, enabled);
                if (wp.m_MaxWorkers == target)
                    continue;
                int before = wp.m_MaxWorkers;
                bool raise = target > before;
                short grace = raise ? GracePeriod() : (short)0;
                wp.m_MaxWorkers = target;
                if (raise && grace > 0)
                    wp.m_EfficiencyCooldown = (short)(-grace);
                EntityManager.SetComponentData(b, wp);
                m_Writes++;
                int staff = EntityManager.HasBuffer<Employee>(b) ? EntityManager.GetBuffer<Employee>(b, isReadOnly: true).Length : 0;
                Mod.Log.Info($"OffSeason [staff] {name} {Id(b)} workplaces {before} -> {target} (prefab {wd.m_MaxWorkers}, staff now {staff}; " +
                             (raise ? $"hiring, vanilla grace {grace}" : "the game's tick lays the surplus off") +
                             $"; ski area {(m_Signs[i] == Entity.Null ? "not linked" : Id(m_Signs[i]))} {(off ? "off-season" : "in season")}" +
                             (enabled ? string.Empty : ", setting off") + ")");
            }
        }

        private void StepArea(Entity sign, uint now, bool enabled)
        {
            if (!m_Attractiveness.TryGetAreaScore(sign, out SkiResortAttractivenessSystem.AreaScoreView v) || v.WriteHeld)
                return;   // no released score: not stepped, reads in season
            m_Areas.TryGetValue(sign, out OffSeasonState st);
            bool wasKnown = st.Known, wasOff = st.Off;
            OffSeasonRules.Step(ref st, v.Result.RunsCounted == 0, now);
            m_Areas[sign] = st;
            if (wasKnown && wasOff == st.Off)
                return;
            Mod.Log.Info($"OffSeason [area] Welcome Sign {Id(sign)}: " +
                         (st.Off
                             ? $"off-season (no open run{(wasKnown ? " for 12 game hours" : " at load")}) — " +
                               (enabled ? "lodges and the school at half staff" : "setting off: staff stay full")
                             : $"in season (an open run{(wasKnown ? " for one game hour" : " at load")}) — full staff"));
        }

        private bool IsSeasonal(Entity prefab, out string name)
        {
            name = null;
            if (m_PrefabCatalog.LodgeDefs.TryGetValue(prefab, out LodgeDef lodge))
            {
                name = lodge.Id;
                return OffSeasonRules.IsSeasonal(true, false);
            }
            if (m_PrefabCatalog.TryGetSchool(prefab, out SchoolDef _))
            {
                name = "school";
                return OffSeasonRules.IsSeasonal(false, true);
            }
            return false;
        }

        private short GracePeriod()
        {
            if (m_EfficiencyParamQuery.IsEmptyIgnoreFilter)
                return 0;
            return m_EfficiencyParamQuery.GetSingleton<BuildingEfficiencyParameterData>().m_ServiceBuildingEfficiencyGracePeriod;
        }

        /// <summary>The setting, each area's state and every seasonal building's workplaces vs its prefab.</summary>
        internal void AppendDump(StringBuilder sb)
        {
            sb.Append("  off-season staff (T137): armed ").Append(m_Armed).Append(", disabled ").Append(m_Disabled)
              .Append(", setting ").Append(EnabledSetting ? "on" : "off").Append(", polls ").Append(m_Polls)
              .Append(", writes ").Append(m_Writes).Append(';');
            if (m_Areas.Count == 0)
                sb.Append(" no area observed");
            foreach (KeyValuePair<Entity, OffSeasonState> kv in m_Areas)
                sb.Append(" sign ").Append(Id(kv.Key)).Append(kv.Value.Off ? " off-season" : " in season")
                  .Append(kv.Value.Pending ? " (changing)" : string.Empty).Append(';');
            for (int i = 0; i < m_Buildings.Count; i++)
            {
                Entity b = m_Buildings[i];
                if (!EntityManager.Exists(b) || !EntityManager.TryGetComponent(b, out PrefabRef pr) || !IsSeasonal(pr.m_Prefab, out string name) ||
                    !EntityManager.TryGetComponent(b, out WorkProvider wp))
                    continue;
                int full = EntityManager.TryGetComponent(pr.m_Prefab, out WorkplaceData wd) ? wd.m_MaxWorkers : -1;
                int staff = EntityManager.HasBuffer<Employee>(b) ? EntityManager.GetBuffer<Employee>(b, isReadOnly: true).Length : 0;
                sb.Append(' ').Append(name).Append(' ').Append(Id(b)).Append(": workplaces ").Append(wp.m_MaxWorkers)
                  .Append(" of ").Append(full).Append(", staff ").Append(staff).Append(';');
            }
            sb.Append('\n');
        }

        private static string Id(Entity e) => e.Index.ToString(CultureInfo.InvariantCulture) + ":" + e.Version.ToString(CultureInfo.InvariantCulture);
    }
}
