// Replaces the vanilla "high voltage not connected" icon on our lifts with our own "needs a lift line" icon (LiftLineIconRule). It acts on
// a station whose lift stub has no line, and on a loose lift end (a cut section, a removed station).
// The vanilla icon comes from ConnectionWarningSystem (UpdateConnectionWarning, ConnectionWarningSystem.cs:2376-2401: owner = the station
// with target = its stub node, or owner = the loose lift node with no target). That system queues its commands in an IconCommandBuffer.
// IconCommandSystem runs the playback of every buffer at ModificationEnd (SystemOrder.cs:261, :271). The playback sorts by owner, then
// buffer index. It skips a command when a LATER one has the same prefab and target (IconCommandSystem.cs:106-160, Command.CompareTo,
// IconCommandBuffer.cs:43-47).
// So this system runs right after ConnectionWarningSystem (Mod.cs: UpdateAfter, phase ModificationEnd, load-bearing). It queues, in a
// buffer created after vanilla's, a Remove of the HV icon for every attach point vanilla may have just judged. Its Add is then dropped and
// the icon never shows. It runs on the frames ConnectionWarningSystem runs (its own update query, :2675-2693, and the map tile flag,
// :2722). It also runs on each Game load and each tile purchase (a full pass). Every PollFrames frames it runs as a check that cleans
// anything a pass missed (a vanilla icon from an older save).
// Our icon is the code-made prefab SkiLiftLineNeeded (LiftLineIconAsset), registered in OnGamePreload. When it cannot be registered,
// this system stays off and the vanilla icon stays (the only cue left), with one "!!!" line.
// Logs: "created". The registration result, once per session. ARMED, per Game load. One line per owner whose icon state changes, capped per
// load (LogLinesMax). "!!!" once on a throw (then off until the next load). DUMP: AppendDump.
// Engine notes: EngineNotes/LiftLineIconSystem.md

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
    public sealed partial class LiftLineIconSystem : GameSystemBase
    {
        internal const int PollFrames = 64;
        internal const int LogLinesMax = 64;

        private PrefabSystem m_PrefabSystem;
        private SkiPrefabCatalogSystem m_Catalog;
        private IconCommandSystem m_IconCommandSystem;
        private Game.Areas.UpdateCollectSystem m_AreaUpdateCollect;

        private EntityQuery m_TriggerQuery;        // ConnectionWarningSystem.m_UpdateQuery (ConnectionWarningSystem.cs:2675-2693)
        private EntityQuery m_UpdatedNodeQuery;    // nodes it judges as owners this frame (CollectOwnersJob, :63-140)
        private EntityQuery m_StationQuery;        // every placed building with sub-nets; ours filtered by prefab
        private EntityQuery m_LooseNodeQuery;      // every node not owned by anything (a lift node is one)
        private EntityQuery m_IconNodeQuery;       // nodes carrying icons (the poll)
        private EntityQuery m_ElectricityConfigQuery;

        private Entity m_IconPrefab;               // SkiLiftLineNeeded, Entity.Null when not registered
        private Entity m_HvIcon;                   // ElectricityParameterData.m_HighVoltageNotConnectedPrefab
        private string m_RegisterReport = "not tried";
        private bool m_RegisterFailed;

        private bool m_Armed, m_Disabled, m_ErrorLogged, m_FullPass;
        private int m_Frame, m_LogLines;
        private bool m_CapLogged;

        // DUMP counters, per load.
        private int m_TriggerPasses, m_Polls, m_HvRemovesQueued, m_HvFoundByPoll, m_Shown, m_Cleared;
        private readonly Dictionary<Entity, bool> m_LastNeed = new Dictionary<Entity, bool>();

        private readonly List<Entity> m_Nodes = new List<Entity>(8);

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Catalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_IconCommandSystem = World.GetOrCreateSystemManaged<IconCommandSystem>();
            m_AreaUpdateCollect = World.GetOrCreateSystemManaged<Game.Areas.UpdateCollectSystem>();
            m_TriggerQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Updated>() },
                Any = new[]
                {
                    ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<Game.Buildings.ServiceUpgrade>(),
                    ComponentType.ReadOnly<Game.Objects.SpawnLocation>(), ComponentType.ReadOnly<Game.Routes.TakeoffLocation>(),
                },
                None = new[] { ComponentType.ReadOnly<Temp>() },
            }, new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Buildings.RoadConnectionUpdated>(), ComponentType.ReadOnly<Event>() },
            });
            m_UpdatedNodeQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<PrefabRef>(),
                              ComponentType.ReadOnly<ConnectedEdge>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Owner>() },
            });
            m_StationQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Buildings.Building>(), ComponentType.ReadOnly<PrefabRef>(),
                              ComponentType.ReadOnly<Game.Net.SubNet>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            m_LooseNodeQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<ConnectedEdge>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Owner>() },
            });
            m_IconNodeQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<IconElement>(),
                              ComponentType.ReadOnly<ConnectedEdge>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Owner>() },
            });
            m_ElectricityConfigQuery = GetEntityQuery(ComponentType.ReadOnly<ElectricityParameterData>());
            Mod.Log.Info($"{nameof(LiftLineIconSystem)} created (our \"needs a lift line\" icon replaces the vanilla high-voltage icon on lifts)");
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            if (m_RegisterFailed || LiftLineIconAsset.Find(m_PrefabSystem) != Entity.Null)
                return;
            try
            {
                Entity reference = m_ElectricityConfigQuery.IsEmptyIgnoreFilter
                    ? Entity.Null
                    : m_ElectricityConfigQuery.GetSingleton<ElectricityParameterData>().m_HighVoltageNotConnectedPrefab;
                bool ok = LiftLineIconAsset.Register(m_PrefabSystem, EntityManager, reference, out m_RegisterReport);
                m_RegisterFailed = !ok;
                if (ok)
                    Mod.Log.Info($"LiftIcon [prefab]: {m_RegisterReport}");
                else
                    Mod.Log.Error($"!!! LiftIcon [prefab]: {m_RegisterReport} — the vanilla high-voltage icon stays on lifts this session");
            }
            catch (Exception e)
            {
                m_RegisterFailed = true;
                m_RegisterReport = "threw " + e.GetType().Name;
                Mod.Log.Error($"!!! LiftIcon [prefab]: registration threw — the vanilla high-voltage icon stays on lifts this session:\n{e}");
            }
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Disabled = m_ErrorLogged = m_CapLogged = false;
            m_Frame = m_LogLines = 0;
            m_TriggerPasses = m_Polls = m_HvRemovesQueued = m_HvFoundByPoll = m_Shown = m_Cleared = 0;
            m_LastNeed.Clear();
            m_IconPrefab = LiftLineIconAsset.Find(m_PrefabSystem);
            m_HvIcon = m_ElectricityConfigQuery.IsEmptyIgnoreFilter
                ? Entity.Null
                : m_ElectricityConfigQuery.GetSingleton<ElectricityParameterData>().m_HighVoltageNotConnectedPrefab;
            m_Armed = mode == GameMode.Game && m_IconPrefab != Entity.Null && m_HvIcon != Entity.Null;
            m_FullPass = m_Armed;
            if (mode != GameMode.Game)
                return;
            if (!m_Armed)
            {
                Mod.Log.Error($"!!! LiftIcon: NOT ARMED (icon prefab {(m_IconPrefab != Entity.Null ? "found" : "missing: " + m_RegisterReport)}, " +
                              $"vanilla HV icon {(m_HvIcon != Entity.Null ? "found" : "missing")}) — the vanilla high-voltage icon stays on lifts");
                return;
            }
            Mod.Log.Info($"LiftIcon: ARMED — {LiftLineIconAsset.PrefabName} replaces the vanilla high-voltage icon on stations with no lift " +
                         "line and on loose lift ends; runs after ConnectionWarningSystem on its frames, a full pass now, a check every " +
                         $"{PollFrames} frames; LogLinesMax={LogLinesMax}");
        }

        protected override void OnUpdate()
        {
            if (!m_Armed || m_Disabled)
                return;
            try
            {
                bool poll = ++m_Frame % PollFrames == 0;
                // A tile purchase: vanilla judges every node inside the new tile, Updated or not (ConnectionWarningSystem.cs:2722-2752,
                // CollectOwnersJob2 :142), so the loose ends get the load's full pass. This is a rare event, with the same cost as a load's
                // pass.
                bool tiles = m_AreaUpdateCollect.mapTilesUpdated;
                bool trigger = !m_TriggerQuery.IsEmptyIgnoreFilter || tiles;
                if (!m_FullPass && !trigger && !poll)
                    return;
                if (!m_Catalog.IsResolved)
                    return;
                // Created after ConnectionWarningSystem's buffer this frame: our commands sort after its own (file header).
                IconCommandBuffer icb = m_IconCommandSystem.CreateCommandBuffer();
                bool cover = m_FullPass || trigger;
                if (cover)
                    m_TriggerPasses++;
                if (poll)
                    m_Polls++;
                Stations(icb, cover);
                if (m_FullPass || tiles)
                    LooseNodes(icb, m_LooseNodeQuery, true);
                else if (trigger)
                    LooseNodes(icb, m_UpdatedNodeQuery, true);
                if (poll)
                    LooseNodes(icb, m_IconNodeQuery, false);
                m_FullPass = false;
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(LiftLineIconSystem)} EXCEPTION — lift line icons off until the next game load (vanilla " +
                                  $"icons stay as the game writes them). Please report this stack:\n{e}");
                }
            }
        }

        /// <summary>Every station: the vanilla HV icon is removed at each stub node (always when <paramref name="cover"/> is set, because vanilla may have
        /// judged it this frame. Otherwise only when present). Our icon is shown or cleared by the rule.</summary>
        private void Stations(IconCommandBuffer icb, bool cover)
        {
            IReadOnlyDictionary<Entity, StationDef> defs = m_Catalog.StationDefs;
            Entity marker = m_Catalog.MarkerEntity;
            using (NativeArray<Entity> entities = m_StationQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<PrefabRef> prefabs = m_StationQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (!defs.TryGetValue(prefabs[i].m_Prefab, out StationDef def))
                        continue;
                    Entity station = entities[i];
                    m_Nodes.Clear();
                    DynamicBuffer<Game.Net.SubNet> subNets = EntityManager.GetBuffer<Game.Net.SubNet>(station, isReadOnly: true);
                    for (int k = 0; k < subNets.Length; k++)
                    {
                        if (EntityManager.HasBuffer<ConnectedEdge>(subNets[k].m_SubNet))
                            m_Nodes.Add(subNets[k].m_SubNet);
                    }
                    int stubNodes = 0, liftEdges = 0;
                    for (int k = 0; k < m_Nodes.Count; k++)
                    {
                        if (!IsLiftStub(m_Nodes[k], marker))
                            continue;
                        stubNodes++;
                        liftEdges += LiftEdgesAt(m_Nodes[k], station);
                    }
                    bool need = LiftLineIconRule.StationNeedsLine(stubNodes, liftEdges);
                    IconsOf(station, out bool ours, out int hv);
                    if (cover || hv > 0)
                    {
                        for (int k = 0; k < m_Nodes.Count; k++)
                            icb.Remove(station, m_HvIcon, m_Nodes[k]);
                        icb.Remove(station, m_HvIcon);
                        m_HvRemovesQueued += m_Nodes.Count + 1;
                        if (!cover)
                            m_HvFoundByPoll += hv;
                    }
                    Apply(icb, station, need, ours, def.Role == StationRole.Base ? "base station" : "top station",
                          $"{stubNodes} stub node(s), {liftEdges} lift edge(s) at them");
                }
            }
        }

        /// <summary>Lift nodes outside stations from the query: the vanilla HV icon is removed (always when <paramref name="cover"/>,
        /// otherwise when present). Our icon is shown or cleared.</summary>
        private void LooseNodes(IconCommandBuffer icb, EntityQuery query, bool cover)
        {
            if (query.IsEmptyIgnoreFilter)
                return;
            HashSet<Entity> lines = m_Catalog.LiftLinePrefabSet;
            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
            using (NativeArray<PrefabRef> prefabs = query.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (!lines.Contains(prefabs[i].m_Prefab))
                        continue;
                    Entity node = entities[i];
                    int liftEdges = LiftEdgesAt(node, Entity.Null);
                    bool need = LiftLineIconRule.LooseEndNeedsLine(liftEdges);
                    IconsOf(node, out bool ours, out int hv);
                    if (cover || hv > 0)
                    {
                        icb.Remove(node, m_HvIcon);
                        m_HvRemovesQueued++;
                        if (!cover)
                            m_HvFoundByPoll += hv;
                    }
                    Apply(icb, node, need, ours, "lift node", $"{liftEdges} lift edge(s)");
                }
            }
        }

        private void Apply(IconCommandBuffer icb, Entity owner, bool need, bool ours, string what, string facts)
        {
            LineIconAction action = LiftLineIconRule.Decide(need, ours);
            if (action == LineIconAction.Show)
            {
                icb.Add(owner, m_IconPrefab, IconPriority.Warning);
                m_Shown++;
            }
            else if (action == LineIconAction.Clear)
            {
                icb.Remove(owner, m_IconPrefab);
                m_Cleared++;
            }
            if (action != LineIconAction.None && (!m_LastNeed.TryGetValue(owner, out bool last) || last != need))
                Line($"LiftIcon [{LiftLineIconRule.Describe(action)}] {what} {Id(owner)}: {facts} — " +
                     (need ? "needs a lift line" : "has its lift line"));
            m_LastNeed[owner] = need;
        }

        /// <summary>A station sub-net node of the lift stub: the marker net, or any high-voltage connection net.</summary>
        private bool IsLiftStub(Entity node, Entity marker)
        {
            if (!EntityManager.TryGetComponent(node, out PrefabRef pr))
                return false;
            if (marker != Entity.Null && pr.m_Prefab == marker)
                return true;
            return EntityManager.TryGetComponent(pr.m_Prefab, out ElectricityConnectionData c) &&
                   c.m_Voltage == Game.Prefabs.ElectricityConnection.Voltage.High;
        }

        /// <summary>Live lift edges at the node that the station (Entity.Null: nothing) does not own.</summary>
        private int LiftEdgesAt(Entity node, Entity station)
        {
            if (!EntityManager.TryGetBuffer(node, isReadOnly: true, out DynamicBuffer<ConnectedEdge> edges))
                return 0;
            HashSet<Entity> lines = m_Catalog.LiftLinePrefabSet;
            int n = 0;
            for (int k = 0; k < edges.Length; k++)
            {
                Entity e = edges[k].m_Edge;
                if (EntityManager.HasComponent<Deleted>(e) || !EntityManager.TryGetComponent(e, out PrefabRef pr) || !lines.Contains(pr.m_Prefab))
                    continue;
                if (station != Entity.Null && EntityManager.TryGetComponent(e, out Owner o) && o.m_Owner == station)
                    continue;
                n++;
            }
            return n;
        }

        private void IconsOf(Entity owner, out bool ours, out int hv)
        {
            ours = false;
            hv = 0;
            if (!EntityManager.TryGetBuffer(owner, isReadOnly: true, out DynamicBuffer<IconElement> icons))
                return;
            for (int k = 0; k < icons.Length; k++)
            {
                Entity icon = icons[k].m_Icon;
                if (EntityManager.HasComponent<Deleted>(icon) || !EntityManager.TryGetComponent(icon, out PrefabRef ip))
                    continue;
                if (ip.m_Prefab == m_IconPrefab)
                    ours = true;
                else if (ip.m_Prefab == m_HvIcon)
                    hv++;
            }
        }

        private void Line(string text)
        {
            if (m_LogLines < LogLinesMax)
            {
                m_LogLines++;
                Mod.Log.Info(text);
                return;
            }
            if (!m_CapLogged)
            {
                m_CapLogged = true;
                Mod.Log.Info($"LiftIcon: {LogLinesMax} icon lines this load (LogLinesMax) — further changes are only counted (DUMP)");
            }
        }

        /// <summary>DUMP (main thread).</summary>
        internal void AppendDump(StringBuilder sb)
        {
            int needing = 0;
            foreach (KeyValuePair<Entity, bool> kv in m_LastNeed)
                if (kv.Value)
                    needing++;
            sb.Append("  lift line icon: armed ").Append(m_Armed).Append(", disabled ").Append(m_Disabled)
              .Append(", prefab ").Append(m_IconPrefab != Entity.Null ? LiftLineIconAsset.PrefabName : "missing (" + m_RegisterReport + ")")
              .Append("; passes ").Append(m_TriggerPasses).Append(", polls ").Append(m_Polls)
              .Append(", HV removes queued ").Append(m_HvRemovesQueued).Append(", HV icons found by a poll ").Append(m_HvFoundByPoll)
              .Append(", shown ").Append(m_Shown).Append(", cleared ").Append(m_Cleared)
              .Append("; owners judged ").Append(m_LastNeed.Count).Append(", needing a line ").Append(needing).Append('\n');
        }

        private static string Id(Entity e) => e.Index.ToString(CultureInfo.InvariantCulture) + ":" + e.Version.ToString(CultureInfo.InvariantCulture);
    }
}
