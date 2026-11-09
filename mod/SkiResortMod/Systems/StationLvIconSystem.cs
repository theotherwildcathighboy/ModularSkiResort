// Removes the vanilla "low voltage not connected" icon from a lift station whose LV stub HAS a wire (the why, with decompile
// citations: the StationLvRule.cs header).
// Every PollFrames sim frames (Game mode), the system checks each of our stations that carries the icon
// (ElectricityParameterData, ConnectionWarningSystem.cs:2366). For a station with a wire on its LV stub (StationLvRule.Classify),
// it queues IconCommandBuffer.Remove(station, icon prefab, the icon's own Target) through IconCommandSystem.
// ConnectionWarningSystem itself issues the same command (:2390). The remove matches on prefab + target (IconCommandSystem.cs:157).
// Vanilla re-adds the icon only when it re-evaluates the station (the station or a sub-net Updated, CollectOwnersJob :63-140).
// The next poll removes it again. Nothing else is written.
// Logs: "created". One line per station when its wire state first clears an icon this load, and again only if it changes.
// "!!!" once on a throw (then disabled until the next load).
// Phase GameSimulation, not load-bearing (IconCommandSystem consumes at ModificationEnd, SystemOrder.cs:271).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Notifications;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class StationLvIconSystem : GameSystemBase
    {
        internal const int PollFrames = 64;

        private SkiPrefabCatalogSystem m_PrefabCatalog;
        private IconCommandSystem m_IconCommandSystem;
        private EntityQuery m_IconQuery;
        private EntityQuery m_ElectricityConfigQuery;

        private bool m_Armed, m_Disabled, m_ErrorLogged;
        private int m_Frame, m_Removed;
        private readonly Dictionary<Entity, LvStubWire> m_Logged = new Dictionary<Entity, LvStubWire>();
        private readonly Dictionary<Entity, string> m_LastState = new Dictionary<Entity, string>();

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_IconCommandSystem = World.GetOrCreateSystemManaged<IconCommandSystem>();
            m_IconQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Buildings.Building>(), ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<IconElement>(), ComponentType.ReadOnly<Game.Net.SubNet>(),
                },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            // ConnectionWarningSystem's own config query (:2706).
            m_ElectricityConfigQuery = GetEntityQuery(ComponentType.ReadOnly<ElectricityParameterData>());
            Mod.Log.Info($"{nameof(StationLvIconSystem)} created (clears the vanilla LV-not-connected icon on a station whose LV stub has a wire)");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_Disabled = m_ErrorLogged = false;
            m_Frame = m_Removed = 0;
            m_Logged.Clear();
            m_LastState.Clear();
            Mod.Log.Info(m_Armed
                ? $"StationPower [lv icon]: ARMED — polls every {PollFrames} frames; the first clear of a station is logged"
                : $"StationPower [lv icon]: NOT ARMED (mode {mode})");
        }

        protected override void OnUpdate()
        {
            if (!m_Armed || m_Disabled)
                return;
            if (++m_Frame % PollFrames != 0)
                return;
            try
            {
                Poll();
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(StationLvIconSystem)} EXCEPTION - LV icon clean-up disabled until the next game load " +
                                  $"(vanilla icons stay as the game writes them). Please report this stack:\n{e}");
                }
            }
        }

        private void Poll()
        {
            if (!m_PrefabCatalog.IsResolved || m_IconQuery.IsEmptyIgnoreFilter || m_ElectricityConfigQuery.IsEmptyIgnoreFilter)
                return;
            Entity lvIcon = m_ElectricityConfigQuery.GetSingleton<ElectricityParameterData>().m_LowVoltageNotConnectedPrefab;
            if (lvIcon == Entity.Null)
                return;
            IconCommandBuffer icb = default;
            bool haveBuffer = false;
            using (NativeArray<Entity> entities = m_IconQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<PrefabRef> prefabs = m_IconQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (!m_PrefabCatalog.TryGetStation(prefabs[i].m_Prefab, out StationDef _))
                        continue;
                    Entity station = entities[i];
                    DynamicBuffer<IconElement> icons = EntityManager.GetBuffer<IconElement>(station, isReadOnly: true);
                    int lvIcons = 0;
                    for (int k = 0; k < icons.Length; k++)
                    {
                        if (EntityManager.TryGetComponent(icons[k].m_Icon, out PrefabRef ip) && ip.m_Prefab == lvIcon &&
                            !EntityManager.HasComponent<Deleted>(icons[k].m_Icon))
                            lvIcons++;
                    }
                    LvStubWire wire = ClassifyStub(station, out int atNode, out int mid);
                    m_LastState[station] = $"{StationLvRule.Describe(wire)} (at node {atNode}, mid-stub {mid}), vanilla LV icon(s) {lvIcons}";
                    if (!StationLvRule.ClearsVanillaIcon(lvIcons > 0, wire))
                        continue;
                    if (!haveBuffer)
                    {
                        icb = m_IconCommandSystem.CreateCommandBuffer();
                        haveBuffer = true;
                    }
                    // Copy the targets first: the buffer handle must not be held across other EntityManager reads.
                    var targets = new List<Entity>(lvIcons);
                    icons = EntityManager.GetBuffer<IconElement>(station, isReadOnly: true);
                    for (int k = 0; k < icons.Length; k++)
                    {
                        Entity icon = icons[k].m_Icon;
                        if (EntityManager.TryGetComponent(icon, out PrefabRef ip) && ip.m_Prefab == lvIcon)
                            targets.Add(EntityManager.TryGetComponent(icon, out Target t) ? t.m_Target : Entity.Null);
                    }
                    for (int k = 0; k < targets.Count; k++)
                        icb.Remove(station, lvIcon, targets[k]);
                    m_Removed += targets.Count;
                    if (!m_Logged.TryGetValue(station, out LvStubWire logged) || logged != wire)
                    {
                        m_Logged[station] = wire;
                        Mod.Log.Info($"StationPower [lv icon] {PrefabName(prefabs[i].m_Prefab)} {Id(station)}: removed {targets.Count} vanilla " +
                                     $"'low voltage not connected' icon(s) — {StationLvRule.Describe(wire)} (at node {atNode}, mid-stub {mid}); " +
                                     "the station is LV-connected (ConnectionWarningSystem counts only edges at the stub node)");
                    }
                }
            }
        }

        /// <summary>The LV wire state of a station's LV stub (StationLvStubProbe, shared with StationLvMarkerSystem).</summary>
        private LvStubWire ClassifyStub(Entity station, out int atNode, out int mid)
        {
            StationLvStubProbe.TryProbe(EntityManager, station, out _, out LvStubWire wire, out atNode, out mid);
            return wire;
        }

        /// <summary>DUMP: per station seen with icons this load, its last LV wire state. Also the removal count.</summary>
        internal void AppendDump(StringBuilder sb)
        {
            sb.Append("  station LV icon (T117 follow-up): armed ").Append(m_Armed).Append(", disabled ").Append(m_Disabled)
              .Append(", vanilla LV icons removed ").Append(m_Removed).Append(';');
            if (m_LastState.Count == 0)
                sb.Append(" no station carried an icon at a poll");
            foreach (KeyValuePair<Entity, string> kv in m_LastState)
                sb.Append(' ').Append(Id(kv.Key)).Append(": ").Append(kv.Value).Append(';');
            sb.Append('\n');
        }

        private string PrefabName(Entity prefab)
        {
            PrefabSystem ps = World.GetExistingSystemManaged<PrefabSystem>();
            return ps != null && ps.TryGetPrefab(prefab, out PrefabBase p) ? p.name : "station";
        }

        private static string Id(Entity e) => e.Index.ToString(CultureInfo.InvariantCulture) + ":" + e.Version.ToString(CultureInfo.InvariantCulture);
    }
}
