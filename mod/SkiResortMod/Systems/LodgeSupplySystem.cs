// The road-optional lodges' city utilities (A-frame, Chalet, Grand: LodgeSupplyRule). Every frame, Game mode only, the system handles each
// lodge of the attractiveness system's watch list. At a road: vanilla's consumers plus mail and crime. Off-road with a cat track chain (the
// cached answer, SkiResortAttractivenessSystem.IsSupplied): the five road-only consumers off, with their icons and efficiency factors
// cleaned. Off-road with no chain: vanilla's consumers and warnings. A missing consumer comes back through vanilla's own path: an Updated
// tag that Game.Buildings.InitializeSystem reads in Modification2. InitializeSystem.cs:178-208 re-adds power, water and garbage when the
// prefab's archetype has them. It re-adds mail and crime only with RequireRoad (:143-153), which W4 clears, so the road branch adds those
// two itself.
// Phase Modification1 is load-bearing (see Mod.cs). The component check runs every frame because vanilla re-adds a consumer on any Updated
// of the lodge (a Move It drag). The supply answer is cached and walked only when its inputs change.
// Skips Temp, Deleted, Destroyed and Abandoned lodges (InitializeSystem.cs:132-135, :173: vanilla owns those).
// Logs: "created", ARMED once per load, one "LodgeSupply [switch]" per mode change, "!!!" once on a throw. DUMP: per lodge.
// Engine notes: EngineNotes/LodgeSupplySystem.md

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Notifications;
using Game.Prefabs;
using Game.Tools;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class LodgeSupplySystem : GameSystemBase
    {
        // Their writers run only for entities with the consumer, so a removal leaves the last value behind unless it is reset here.
        private static readonly EfficiencyFactor[] kOffGridFactors =
        {
            EfficiencyFactor.ElectricitySupply,
            EfficiencyFactor.ElectricityFee,
            EfficiencyFactor.WaterSupply,
            EfficiencyFactor.DirtyWater,
            EfficiencyFactor.SewageHandling,
            EfficiencyFactor.WaterFee,
            EfficiencyFactor.Garbage,
            EfficiencyFactor.Mail,
        };

        /// <summary>One watched lodge: the mode last applied, updates since the last Updated tag (-1 = none in this streak), and the counts
        /// the DUMP shows.</summary>
        private struct Watched
        {
            public bool Known;
            public LodgeSupplyMode Mode;
            public int SinceTag;
            public int Tags;
            public int ReAdds;   // consumers vanilla re-added while off-grid (an Updated of the lodge), removed again without a log line
        }

        private SkiResortAttractivenessSystem m_Attractiveness;
        private IconCommandSystem m_IconCommands;
        private EntityQuery m_ElectricityParamQuery;
        private EntityQuery m_WaterParamQuery;
        private EntityQuery m_GarbageParamQuery;
        private EntityQuery m_TrafficParamQuery;

        private bool m_Armed, m_Disabled, m_ErrorLogged;
        private int m_Revision = -1, m_Switches;

        private readonly List<Entity> m_Watch = new List<Entity>(4);
        private readonly Dictionary<Entity, Watched> m_State = new Dictionary<Entity, Watched>();
        private readonly List<Entity> m_Gone = new List<Entity>(2);

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Attractiveness = World.GetOrCreateSystemManaged<SkiResortAttractivenessSystem>();
            m_IconCommands = World.GetOrCreateSystemManaged<IconCommandSystem>();
            m_ElectricityParamQuery = GetEntityQuery(ComponentType.ReadOnly<ElectricityParameterData>());
            m_WaterParamQuery = GetEntityQuery(ComponentType.ReadOnly<WaterPipeParameterData>());
            m_GarbageParamQuery = GetEntityQuery(ComponentType.ReadOnly<GarbageParameterData>());
            m_TrafficParamQuery = GetEntityQuery(ComponentType.ReadOnly<TrafficConfigurationData>());
            Mod.Log.Info($"{nameof(LodgeSupplySystem)} created (road-optional lodges: off-road on a cat track chain, city utilities off)");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_Disabled = m_ErrorLogged = false;
            m_Revision = -1;   // ids remap at a load: the watch list is rebuilt from the attractiveness system's (limit 8)
            m_Switches = 0;
            m_Watch.Clear();
            m_State.Clear();
            if (m_Armed)
                Mod.Log.Info("LodgeSupply: ARMED — A-frame, chalet and grand lodge: at a road, the city's utilities (plus mail and crime); " +
                             $"off-road with a cat track within {LiftServedRule.LinkMeters:0} m whose chain reaches a road lodge or a bus depot, " +
                             "power, water, garbage, mail and crime off (no warnings, no fee); with neither, the game's warnings. A lost chain " +
                             $"holds for {LodgeSupplyRule.HoldFrames} sim frames (one game hour)");
        }

        protected override void OnUpdate()
        {
            if (!m_Armed || m_Disabled)
                return;
            try
            {
                Tick();
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(LodgeSupplySystem)} EXCEPTION - lodge utilities frozen in their last shape until the next game " +
                                  $"load. Please report this stack:\n{e}");
                }
            }
        }

        private void Tick()
        {
            int revision = m_Attractiveness.SupplyWatchRevision;
            if (revision != m_Revision)
            {
                m_Revision = revision;
                m_Attractiveness.CopySupplyWatch(m_Watch);
                m_Gone.Clear();
                foreach (KeyValuePair<Entity, Watched> pair in m_State)
                {
                    if (!m_Watch.Contains(pair.Key))
                        m_Gone.Add(pair.Key);
                }
                for (int i = 0; i < m_Gone.Count; i++)
                    m_State.Remove(m_Gone[i]);
            }
            for (int i = 0; i < m_Watch.Count; i++)
            {
                Entity lodge = m_Watch[i];
                if (!EntityManager.Exists(lodge) || EntityManager.HasComponent<Temp>(lodge) || EntityManager.HasComponent<Deleted>(lodge) ||
                    EntityManager.HasComponent<Destroyed>(lodge) || EntityManager.HasComponent<Abandoned>(lodge) ||
                    !EntityManager.TryGetComponent(lodge, out Building data))
                    continue;
                bool road = data.m_RoadEdge != Entity.Null;
                LodgeSupplyMode mode = LodgeSupplyRule.ModeOf(road, m_Attractiveness.IsSupplied(lodge));
                m_State.TryGetValue(lodge, out Watched w);
                bool changed = !w.Known || w.Mode != mode;
                if (changed)
                    w.SinceTag = -1;
                string did;
                if (mode == LodgeSupplyMode.OffGrid)
                {
                    did = RemoveConsumers(lodge);
                    if (did != null && !changed)
                        w.ReAdds++;
                }
                else
                {
                    did = null;
                    bool missing = !EntityManager.HasComponent<ElectricityConsumer>(lodge) || !EntityManager.HasComponent<WaterConsumer>(lodge) ||
                                   !EntityManager.HasComponent<GarbageProducer>(lodge);
                    if (!missing)
                        w.SinceTag = -1;
                    else if (w.SinceTag >= 0)
                        w.SinceTag++;
                    if (LodgeSupplyRule.ShouldTagUpdated(mode, missing, false, w.SinceTag) && !EntityManager.HasComponent<Updated>(lodge))
                    {
                        EntityManager.AddComponent<Updated>(lodge);
                        w.SinceTag = 0;
                        w.Tags++;
                        did = "Updated tag (the game re-adds power, water and garbage)";
                    }
                    if (LodgeSupplyRule.AddsMailAndCrime(mode))
                    {
                        bool mail = !EntityManager.HasComponent<MailProducer>(lodge);
                        bool crime = !EntityManager.HasComponent<CrimeProducer>(lodge);
                        if (mail)
                            EntityManager.AddComponent<MailProducer>(lodge);
                        if (crime)
                            EntityManager.AddComponent<CrimeProducer>(lodge);
                        if (mail || crime)
                            did = (did == null ? string.Empty : did + ", ") + (mail && crime ? "mail and crime added" : mail ? "mail added" : "crime added");
                    }
                }
                if (changed)
                {
                    m_Switches++;
                    Mod.Log.Info($"LodgeSupply [switch] {NameOf(lodge)} {Id(lodge)}: {(w.Known ? Word(w.Mode) : "first seen")} -> {Word(mode)}" +
                                 $" ({did ?? "nothing to change"}; {m_Attractiveness.SupplyDumpOf(lodge)})");
                }
                w.Known = true;
                w.Mode = mode;
                m_State[lodge] = w;
            }
        }

        /// <summary>Off-grid: removes whichever of the five consumers is present, then the seven icons and the eight factors (their writers
        /// skip a building without the consumer). Null when nothing was present.</summary>
        private string RemoveConsumers(Entity lodge)
        {
            int removed = 0;
            removed += Remove<ElectricityConsumer>(lodge);
            removed += Remove<WaterConsumer>(lodge);
            removed += Remove<GarbageProducer>(lodge);
            removed += Remove<MailProducer>(lodge);
            removed += Remove<CrimeProducer>(lodge);
            if (removed == 0)
                return null;
            ClearIcons(lodge);
            if (EntityManager.TryGetBuffer(lodge, isReadOnly: false, out DynamicBuffer<Efficiency> factors))
            {
                for (int i = 0; i < kOffGridFactors.Length; i++)
                    BuildingUtils.SetEfficiencyFactor(factors, kOffGridFactors[i], 1f);
            }
            return removed.ToString(CultureInfo.InvariantCulture) + " consumer(s) removed, icons and factors cleaned";
        }

        private int Remove<T>(Entity lodge) where T : unmanaged, IComponentData
        {
            if (!EntityManager.HasComponent<T>(lodge))
                return 0;
            EntityManager.RemoveComponent<T>(lodge);
            return 1;
        }

        /// <summary>The icons vanilla raised for the removed consumers and the road connection (the prototype's order). IconCommandSystem
        /// applies the queued removes at ModificationEnd (SystemOrder.cs:271), later in this frame.</summary>
        private void ClearIcons(Entity lodge)
        {
            IconCommandBuffer icons = m_IconCommands.CreateCommandBuffer();
            if (!m_ElectricityParamQuery.IsEmptyIgnoreFilter)
            {
                ElectricityParameterData electricity = m_ElectricityParamQuery.GetSingleton<ElectricityParameterData>();
                icons.Remove(lodge, electricity.m_ElectricityNotificationPrefab);
                icons.Remove(lodge, electricity.m_BuildingBottleneckNotificationPrefab);
            }
            if (!m_WaterParamQuery.IsEmptyIgnoreFilter)
            {
                WaterPipeParameterData water = m_WaterParamQuery.GetSingleton<WaterPipeParameterData>();
                icons.Remove(lodge, water.m_WaterNotification);
                icons.Remove(lodge, water.m_DirtyWaterNotification);
                icons.Remove(lodge, water.m_SewageNotification);
            }
            if (!m_GarbageParamQuery.IsEmptyIgnoreFilter)
                icons.Remove(lodge, m_GarbageParamQuery.GetSingleton<GarbageParameterData>().m_GarbageNotificationPrefab);
            if (!m_TrafficParamQuery.IsEmptyIgnoreFilter)
                icons.Remove(lodge, m_TrafficParamQuery.GetSingleton<TrafficConfigurationData>().m_RoadConnectionNotification);
        }

        private static string Word(LodgeSupplyMode mode)
        {
            switch (mode)
            {
                case LodgeSupplyMode.Road: return "road";
                case LodgeSupplyMode.OffGrid: return "off-grid (cat track chain)";
                default: return "off-road, no chain (the game's warnings)";
            }
        }

        private string NameOf(Entity lodge)
        {
            return EntityManager.TryGetComponent(lodge, out PrefabRef pr) &&
                   World.GetExistingSystemManaged<SkiPrefabCatalogSystem>()?.LodgeDefs.TryGetValue(pr.m_Prefab, out LodgeDef def) == true
                ? def.Id
                : "lodge";
        }

        /// <summary>DUMP: per watched lodge the mode, the five consumers, the supply answer and the counts.</summary>
        internal void AppendDump(StringBuilder sb)
        {
            sb.Append("  lodge supply: armed ").Append(m_Armed).Append(", disabled ").Append(m_Disabled).Append(", switches ").Append(m_Switches)
              .Append(", chain walks ").Append(m_Attractiveness.SupplyWalks).Append(';');
            if (m_Watch.Count == 0)
                sb.Append(" no road-optional lodge");
            for (int i = 0; i < m_Watch.Count; i++)
            {
                Entity lodge = m_Watch[i];
                if (!EntityManager.Exists(lodge))
                    continue;
                m_State.TryGetValue(lodge, out Watched w);
                sb.Append(' ').Append(NameOf(lodge)).Append(' ').Append(Id(lodge)).Append(": ").Append(w.Known ? Word(w.Mode) : "not seen")
                  .Append(", consumers ")
                  .Append(EntityManager.HasComponent<ElectricityConsumer>(lodge) ? "E" : "-")
                  .Append(EntityManager.HasComponent<WaterConsumer>(lodge) ? "W" : "-")
                  .Append(EntityManager.HasComponent<GarbageProducer>(lodge) ? "G" : "-")
                  .Append(EntityManager.HasComponent<MailProducer>(lodge) ? "M" : "-")
                  .Append(EntityManager.HasComponent<CrimeProducer>(lodge) ? "C" : "-")
                  .Append(", ").Append(m_Attractiveness.SupplyDumpOf(lodge))
                  .Append(", tags ").Append(w.Tags).Append(", re-adds ").Append(w.ReAdds).Append(';');
            }
            sb.Append('\n');
        }

        private static string Id(Entity e) => e.Index.ToString(CultureInfo.InvariantCulture) + ":" + e.Version.ToString(CultureInfo.InvariantCulture);
    }
}
