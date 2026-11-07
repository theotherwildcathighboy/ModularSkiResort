// Refuses a vanilla high-voltage power line that would join one of our lift lines (a tower or stub node, a split of a lift section, a local
// connection on its middle) or replace a lift section. The vanilla Temp edge gets the game's Error, so the preview turns red and the tool
// cannot apply (ToolBaseSystem.GetAllowApply: any Error entity gives false unless ToolSystem.ignoreErrors, ToolBaseSystem.cs:533-540, query
// :313). The why and what is not refused are in the PowerJoinRule.cs header and docs/research/lifts/power-join-refusal-2026-10-27.md.
// Mechanism: ValidationSystem fills ValidationSystem.Components.m_ErrorMap (a public NativeHashMap, created only on frames with Updated
// Temps, ValidationSystem.cs:1926-1931, 1977). Components then makes the Error components match the map: an Error on an entity missing from
// the map is REMOVED (ValidationSystem.cs:450-466, 527-531, 552-567). A plain AddComponent<Error> would be stripped on the next validation,
// so this system writes into the map instead, between the two (Mod.cs: UpdateBefore Components at ModificationEnd. Components itself is
// UpdateAfter ValidationSystem, SystemOrder.cs:264-265. UpdateSystem.AddSystemUpdate emits a "before" ref right before its anchor,
// UpdateSystem.cs:382-425). It evaluates EVERY live Temp edge, not only Updated ones, since the map must name every refused edge on each
// frame it exists. Frames without the map change no Error, so the last verdict stands.
// Cost: nothing unless the map exists AND Temp edges exist. Then it costs one Complete on the validation job and a main-thread pass over
// the Temp edges (a drag preview: tens). It never runs while our lift tool is active (its Temps and the copies it makes are its own).
// Logs: "created", ARMED per load, one INFO line per refusal episode (start and end, PowerJoinEpisode), "!!!" once on a throw (then the
// system is disabled until the next load, and the game builds the join as vanilla does).

using System;
using System.Collections.Generic;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class PowerJoinRefusalSystem : GameSystemBase
    {
        private ToolSystem m_ToolSystem;
        private ValidationSystem.Components m_ValidationComponents;
        private SkiPrefabCatalogSystem m_PrefabCatalog;
        private EntityQuery m_TempEdgeQuery;

        private bool m_Armed, m_Disabled;
        private PowerJoinEpisode m_Episode;
        private readonly HashSet<Entity> m_LiftTempEnds = new HashSet<Entity>();     // reused; ends of lift-side Temp edges
        private readonly HashSet<Entity> m_LiftTempMiddles = new HashSet<Entity>();  // reused; their ConnectedNode entries
        private readonly List<Entity> m_Refused = new List<Entity>(8);

        /// <summary>The last evaluation refused at least one edge (the tooltip reads it). Cleared when no Temp edge is left.</summary>
        internal bool RefusingNow { get; private set; }

        /// <summary>Refused edges in the last evaluation, and evaluations that refused since the load (dev / field aid).</summary>
        public int LastRefusedCount;
        public long RefusingEvaluations;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_ValidationComponents = World.GetOrCreateSystemManaged<ValidationSystem.Components>();
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_TempEdgeQuery = GetEntityQuery(
                ComponentType.ReadOnly<Edge>(),
                ComponentType.ReadOnly<Temp>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>());
            Mod.Log.Info($"{nameof(PowerJoinRefusalSystem)} created (refuses a vanilla HV power line that joins a ski lift)");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_Disabled = false;
            m_Episode.Reset();
            RefusingNow = false;
            LastRefusedCount = 0;
            RefusingEvaluations = 0;
            Mod.Log.Info(m_Armed
                ? "PowerJoin: ARMED — a vanilla high-voltage line that joins a lift tower, stub or section (or replaces one) gets the " +
                  "game's Error (red preview, cannot be built); vanilla lines elsewhere, low-voltage wires and our lift tool are untouched"
                : $"PowerJoin: NOT ARMED (mode {mode})");
        }

        protected override void OnUpdate()
        {
            if (!m_Armed || m_Disabled)
                return;
            if (m_TempEdgeQuery.IsEmptyIgnoreFilter || m_ToolSystem.activeTool is LiftLineToolSystem)
            {
                Settle(0);
                return;
            }
            if (!m_ValidationComponents.m_ErrorMap.IsCreated)
                return;   // no validation this frame: the Error components stay as the last evaluation left them
            try
            {
                Evaluate();
            }
            catch (Exception e)
            {
                m_Disabled = true;
                RefusingNow = false;
                Mod.Log.Error($"!!! {nameof(PowerJoinRefusalSystem)} EXCEPTION — the power-line join refusal is off until the next game " +
                              $"load (a vanilla HV line can join a lift again, as in the unmodded game). Please report this stack:\n{e}");
            }
        }

        private void Evaluate()
        {
            if (!m_PrefabCatalog.IsResolved)
                return;
            HashSet<Entity> lines = m_PrefabCatalog.LiftLinePrefabSet;
            Entity marker = m_PrefabCatalog.MarkerEntity;
            m_LiftTempEnds.Clear();
            m_LiftTempMiddles.Clear();
            m_Refused.Clear();
            string firstWhat = null;

            using (NativeArray<Entity> edges = m_TempEdgeQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<PrefabRef> prefabs = m_TempEdgeQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                // Pass 1: the lift side of the preview (the split pieces and the copies GenerateEdgesSystem makes at a joined node).
                bool anyHv = false;
                for (int i = 0; i < edges.Length; i++)
                {
                    Entity p = prefabs[i].m_Prefab;
                    if (IsLiftSide(p, lines, marker))
                    {
                        Edge edge = EntityManager.GetComponentData<Edge>(edges[i]);
                        m_LiftTempEnds.Add(edge.m_Start);
                        m_LiftTempEnds.Add(edge.m_End);
                        if (EntityManager.TryGetBuffer(edges[i], true, out DynamicBuffer<ConnectedNode> mids))
                            for (int k = 0; k < mids.Length; k++)
                                m_LiftTempMiddles.Add(mids[k].m_Node);
                    }
                    else if (!anyHv && IsHighVoltage(p))
                    {
                        anyHv = true;
                    }
                }
                if (!anyHv)
                {
                    Settle(0);
                    return;
                }

                // Pass 2: judge each vanilla HV Temp edge.
                for (int i = 0; i < edges.Length; i++)
                {
                    Entity p = prefabs[i].m_Prefab;
                    if (!IsHighVoltage(p))
                        continue;
                    Entity e = edges[i];
                    Temp temp = EntityManager.GetComponentData<Temp>(e);
                    Edge edge = EntityManager.GetComponentData<Edge>(e);
                    var facts = new PowerJoinFacts
                    {
                        HighVoltage = true,
                        LiftSide = IsLiftSide(p, lines, marker),
                        OwnedByOurStation = OwnedByOurStation(e),
                        Deleting = (temp.m_Flags & TempFlags.Delete) != 0,
                        Creates = (temp.m_Flags & TempFlags.Create) != 0,
                        OriginalLiftSide = temp.m_Original != Entity.Null &&
                                           EntityManager.TryGetComponent(temp.m_Original, out PrefabRef op) &&
                                           IsLiftSide(op.m_Prefab, lines, marker),
                    };
                    if (facts.LiftSide || facts.OwnedByOurStation || facts.Deleting)
                        continue;
                    facts.Start = Touch(edge.m_Start, lines, marker);
                    facts.End = Touch(edge.m_End, lines, marker);
                    PowerJoinVerdict v = PowerJoinRule.Decide(facts);
                    if (!PowerJoinRule.Refuses(v))
                        continue;
                    m_Refused.Add(e);
                    if (firstWhat == null)
                        firstWhat = v == PowerJoinVerdict.RefuseReplace
                            ? "replaces a lift section"
                            : $"start {PowerJoinRule.Describe(facts.Start)}, end {PowerJoinRule.Describe(facts.End)}";
                }
            }

            if (m_Refused.Count > 0)
            {
                // ProcessValidationResultsJob writes the map. It must be done before the main thread touches it.
                m_ValidationComponents.m_ErrorMapDeps.Complete();
                NativeHashMap<Entity, ErrorSeverity> map = m_ValidationComponents.m_ErrorMap;
                for (int i = 0; i < m_Refused.Count; i++)
                {
                    Entity e = m_Refused[i];
                    // AddError's rule (ValidationSystem.cs:1787-1798): keep a stronger severity (Cancel / CancelError).
                    if (map.TryGetValue(e, out ErrorSeverity have))
                    {
                        if (have < ErrorSeverity.Error)
                            map[e] = ErrorSeverity.Error;
                    }
                    else
                    {
                        map.Add(e, ErrorSeverity.Error);
                    }
                }
            }
            Settle(m_Refused.Count, firstWhat);
        }

        private void Settle(int refused, string what = null)
        {
            LastRefusedCount = refused;
            RefusingNow = refused > 0;
            if (refused > 0)
                RefusingEvaluations++;
            switch (m_Episode.Step(refused > 0))
            {
                case PowerJoinEpisode.Event.Started:
                    Mod.Log.Info($"PowerJoin: REFUSED — {refused} vanilla high-voltage edge(s) in the preview ({what}); shown red, cannot " +
                                 "be built (A power line cannot connect to a ski lift)");
                    break;
                case PowerJoinEpisode.Event.Ended:
                    Mod.Log.Info($"PowerJoin: refusal episode ended after {m_Episode.Evaluations} refusing evaluation(s)");
                    break;
            }
        }

        private static bool IsLiftSide(Entity prefab, HashSet<Entity> lines, Entity marker)
        {
            return prefab != Entity.Null && (prefab == marker || lines.Contains(prefab));
        }

        private bool IsHighVoltage(Entity prefab)
        {
            return EntityManager.TryGetComponent(prefab, out ElectricityConnectionData ec) &&
                   ec.m_Voltage == Game.Prefabs.ElectricityConnection.Voltage.High;
        }

        /// <summary>The edge's top owner is one of our catalog stations (Owner chain, as the station's stub sub-nets carry it).</summary>
        private bool OwnedByOurStation(Entity e)
        {
            Entity cur = e;
            for (int guard = 0; guard < 8 && EntityManager.TryGetComponent(cur, out Owner owner) && owner.m_Owner != Entity.Null; guard++)
                cur = owner.m_Owner;
            return cur != e && EntityManager.TryGetComponent(cur, out PrefabRef pr) && m_PrefabCatalog.IsStation(pr.m_Prefab);
        }

        private PowerJoinTouch Touch(Entity node, HashSet<Entity> lines, Entity marker)
        {
            bool hasLift = HasLiftEdge(node, lines, marker);
            if (!hasLift && EntityManager.TryGetComponent(node, out Temp nt) && nt.m_Original != Entity.Null)
                hasLift = HasLiftEdge(nt.m_Original, lines, marker);
            return PowerJoinRule.ClassifyNode(hasLift, m_LiftTempEnds.Contains(node), m_LiftTempMiddles.Contains(node));
        }

        private bool HasLiftEdge(Entity node, HashSet<Entity> lines, Entity marker)
        {
            if (!EntityManager.TryGetBuffer(node, true, out DynamicBuffer<ConnectedEdge> connected))
                return false;
            for (int k = 0; k < connected.Length; k++)
            {
                Entity ce = connected[k].m_Edge;
                if (!EntityManager.HasComponent<Deleted>(ce) && EntityManager.TryGetComponent(ce, out PrefabRef pr) &&
                    IsLiftSide(pr.m_Prefab, lines, marker))
                    return true;
            }
            return false;
        }
    }
}
