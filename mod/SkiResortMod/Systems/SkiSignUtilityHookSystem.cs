// Gives the Ski Area Welcome Sign its power, water and sewage link to the road when a lodge (or any ServiceUpgrade
// building) is placed on or removed from it (docs/research/field/2026-10-04/findings/sign-utilities.md).
//
// Defect (decompile 1.6.0f1, byte-identical in 1.6.2f1): the sign prefab has no ServiceConsumption, so the lodge's
// consumers land on the sign later through ServiceUpgradeSystem.UpgradeInstalled (cs:138-156), which queues no road
// edge. A road-fed consumer's flow edge is created only by ElectricityRoadConnectionGraphSystem's UpdateRoadEdgesJob for
// an edge in its queue (:86-104, :124-130), fed only for a building that already has the consumer (:38-47) when its road
// changed or it was Created (RoadConnectionSystem.cs:597-598, :622-629); the water twin is
// WaterPipeRoadConnectionGraphSystem.cs:36-47, :85-137. With no link the dispatch gives the sign 0
// (DispatchElectricitySystem.cs:88-105; DispatchWaterSystem.cs:96-122), whatever the city produces.
//
// Hook: when a ServiceUpgrade object owned by a Welcome Sign is Created or Deleted (not Temp) and the sign's
// Building.m_RoadEdge is not null, enqueue the edge into both public queues (ElectricityRoadConnectionGraphSystem.
// GetEdgeUpdateQueue / AddQueueWriter :209-218; water :208-217), the pattern of Game.Buildings/InitializeSystem.cs:184-201,
// on the main thread: returned dependencies are completed and AddQueueWriter gets an empty handle. The job recomputes
// the edge from scratch (:106-122) and dedupes (:88-92), so an extra enqueue is harmless; on removal, re-queueing makes
// it resize or delete the stale link (:132-138).
//
// Order (load-bearing; registered in Mod.cs). In the event frame ModificationSystem runs Modification1..End
// (ModificationSystem.cs:19-26; SystemOrder.cs:58, 60, paused included):
//   * Modification4: ServiceUpgradeSystem (SystemOrder.cs:170) records Add/RemoveComponent of the consumers on the sign;
//     ModificationBarrier4 (:90) plays them back before Modification4B;
//   * Modification4B: RoadConnectionSystem (:180) writes Building.m_RoadEdge in a job; our EntityManager read of the sign's
//     Building completes it;
//   * Modification5: this system, UpdateBefore ElectricityRoadConnectionGraphSystem (:246). The water graph system is
//     registered after it (:247), so one anchor puts us before both. Use only one: every UpdateBefore adds its own entry
//     (UpdateSystem.cs:173-176, :278-292), so two would run the system twice a frame. ModificationBarrier5 (:92) plays
//     the new flow edge back in the same frame, before any GameSimulation dispatch reads it.
// Created / Deleted tags live until CleanUpSystem (SystemOrder.cs:54), so a deleted lodge still carries its Owner here.
//
// Skipped (one log line each): a Deleted sign (bulldozing it deletes its lodges; the game raises the sign's own
// RoadConnectionUpdated, RoadConnectionSystem.cs:622-629), a Deleted road edge (vanilla skips those too,
// ElectricityRoadConnectionGraphSystem.cs:40), a sign with no road edge (the game's own event links it once a road is built).
//
// No load pass: flow edges are saved with the city (ISerializable) and a load adds no Created tag. Game mode only;
// RequireForUpdate on the event query, so OnUpdate runs only on a frame with an upgrade event (ServiceUpgradeSystem.cs:84).
// Logs: "created", one ARMED / inert line per load, one line per hook, and one "!!!" line if it throws (the system then
// disables itself until the next load).
// Dump (called by the hub's DUMP): per sign the road edge or "none", the two consumers, and whether the road's node has its
// link to the supply sink (TryGetFlowEdge with ElectricityFlowSystem.sinkNode :401, WaterPipeFlowSystem.sinkNode :370).
// "power link MISSING" with a road that has a cable is the original defect.
// No serialized state, no added components, no Harmony, no reflection; the only writes are the two vanilla queues.

using System;
using System.Collections.Generic;
using System.Text;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiSignUtilityHookSystem : GameSystemBase
    {
        /// <summary>The Welcome Sign BuildingPrefab (table duplicated per system; same name as SkiRunOwnershipSystem.GateNames).</summary>
        private static readonly string[] SignPrefabNames = { "SkiResortGate" };

        private PrefabSystem m_PrefabSystem;
        private ElectricityRoadConnectionGraphSystem m_ElectricityRoadGraph;
        private WaterPipeRoadConnectionGraphSystem m_WaterRoadGraph;
        private ElectricityFlowSystem m_ElectricityFlow;
        private WaterPipeFlowSystem m_WaterFlow;

        private EntityQuery m_UpgradeEvents;    // ServiceUpgrade objects with an Owner, Created or Deleted, not Temp
        private EntityQuery m_BuildingQuery;    // committed buildings (DUMP: the signs)
        private EntityQuery m_PrefabScanQuery;  // name-resolution fallback

        private BufferLookup<ConnectedFlowEdge> m_FlowConnectionLookup;      // DUMP only
        private ComponentLookup<ElectricityFlowEdge> m_ElectricityEdgeLookup; // DUMP only
        private ComponentLookup<WaterPipeEdge> m_WaterEdgeLookup;            // DUMP only

        private readonly HashSet<Entity> m_SignPrefabs = new HashSet<Entity>();

        // Per-load state — reset at OnGameLoadingComplete.
        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;
        private int m_Queued, m_QueuedAdded, m_QueuedRemoved, m_NoRoad, m_SignGone, m_EdgeGone;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_ElectricityRoadGraph = World.GetOrCreateSystemManaged<ElectricityRoadConnectionGraphSystem>();
            m_WaterRoadGraph = World.GetOrCreateSystemManaged<WaterPipeRoadConnectionGraphSystem>();
            m_ElectricityFlow = World.GetOrCreateSystemManaged<ElectricityFlowSystem>();
            m_WaterFlow = World.GetOrCreateSystemManaged<WaterPipeFlowSystem>();

            // The ServiceUpgradeSystem query shape (ServiceUpgradeSystem.cs:61-73), narrowed to upgrades with an Owner: those whose
            // components land on (or leave) their owner this frame. Object, not Building: a BuildingExtensionPrefab upgrade carries
            // Extension and no Building (BuildingExtensionPrefab.cs:37-47 vs BuildingPrefab.cs:43) but may carry ServiceConsumption
            // (ServiceConsumption.cs:7-11). Hook() reads only Owner, PrefabRef and Deleted from the upgrade.
            m_UpgradeEvents = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Buildings.ServiceUpgrade>(),
                    ComponentType.ReadOnly<Game.Objects.Object>(),
                    ComponentType.ReadOnly<Owner>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                Any = new[]
                {
                    ComponentType.ReadOnly<Created>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            m_BuildingQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
            });
            m_PrefabScanQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());
            m_FlowConnectionLookup = GetBufferLookup<ConnectedFlowEdge>(isReadOnly: true);
            m_ElectricityEdgeLookup = GetComponentLookup<ElectricityFlowEdge>(isReadOnly: true);
            m_WaterEdgeLookup = GetComponentLookup<WaterPipeEdge>(isReadOnly: true);
            // OnUpdate runs only on frames with an upgrade event (ServiceUpgradeSystem.cs:84)
            RequireForUpdate(m_UpgradeEvents);
            // Inert until a Game-mode load arms it; OnGameLoadingComplete fires while disabled (Game/GameSystemBase.cs:60-70)
            Enabled = false;

            Mod.Log.Info($"{nameof(SkiSignUtilityHookSystem)} created (Modification5, before the road power and water " +
                         "graph systems: a lodge placed on or removed from a Welcome Sign queues the sign's road for its " +
                         "power and water link; no load pass)");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_Queued = m_QueuedAdded = m_QueuedRemoved = m_NoRoad = m_SignGone = m_EdgeGone = 0;
            if (mode != GameMode.Game)
            {
                Enabled = false;
                return;
            }
            Enabled = true;
            try
            {
                ResolvePrefabs();
                m_Armed = m_SignPrefabs.Count > 0;
                if (m_Armed)
                    Mod.Log.Info("SignUtility: ARMED — a lodge placed on or removed from a Welcome Sign queues the sign's " +
                                 "road edge for its power and water link (the game itself does not; field test 7)");
                else
                    Mod.Log.Info("SignUtility: Welcome Sign prefab not found — sign utility hook inert this load");
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        protected override void OnUpdate()
        {
            if (m_Disabled || !m_Armed)
                return;
            try
            {
                GameManager gm = GameManager.instance;
                if (gm == null || gm.isGameLoading)
                    return;
                using (var upgrades = m_UpgradeEvents.ToEntityArray(Allocator.Temp))
                {
                    for (int i = 0; i < upgrades.Length; i++)
                        Hook(upgrades[i]);
                }
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        private void Fail(Exception e)
        {
            m_Disabled = true;
            Enabled = false;
            if (!m_ErrorLogged)
            {
                m_ErrorLogged = true;
                Mod.Log.Error($"!!! {nameof(SkiSignUtilityHookSystem)} EXCEPTION — a lodge placed on or removed from a " +
                              "Welcome Sign no longer queues the sign's road for power and water until the next game load " +
                              $"(the sign may run without utilities on an otherwise empty road):\n{e}");
            }
        }

        /// <summary>One upgrade event: resolve its owner sign and queue the sign's road edge.</summary>
        private void Hook(Entity upgrade)
        {
            Owner owner = EntityManager.GetComponentData<Owner>(upgrade);
            Entity sign = owner.m_Owner;
            if (sign == Entity.Null || !EntityManager.Exists(sign) ||
                !EntityManager.TryGetComponent(sign, out PrefabRef signRef) || !m_SignPrefabs.Contains(signRef.m_Prefab))
                return;   // not one of ours: vanilla upgrades are vanilla's business

            bool removed = EntityManager.HasComponent<Deleted>(upgrade);
            PrefabRef upgradeRef = EntityManager.GetComponentData<PrefabRef>(upgrade);
            string what = $"SignUtility: {m_PrefabSystem.GetPrefabName(upgradeRef.m_Prefab)} {Ent(upgrade)} " +
                          $"{(removed ? "removed from" : "added to")} sign {Ent(sign)}";

            if (EntityManager.HasComponent<Deleted>(sign))
            {
                m_SignGone++;
                Mod.Log.Info($"{what} — not queued: the sign is being removed too (the game updates its road itself)");
                return;
            }
            Entity road = EntityManager.TryGetComponent(sign, out Building building) ? building.m_RoadEdge : Entity.Null;
            if (road == Entity.Null)
            {
                m_NoRoad++;
                Mod.Log.Info($"{what} — not queued: the sign has no road edge (its front is not at a road, so no power, " +
                             "water or sewage can reach it)");
                return;
            }
            if (!EntityManager.Exists(road) || EntityManager.HasComponent<Deleted>(road))
            {
                m_EdgeGone++;
                Mod.Log.Info($"{what} — not queued: its road {Ent(road)} is being removed (the game re-links the sign)");
                return;
            }

            NativeQueue<Entity> power = m_ElectricityRoadGraph.GetEdgeUpdateQueue(out JobHandle powerDeps);
            powerDeps.Complete();
            power.Enqueue(road);
            m_ElectricityRoadGraph.AddQueueWriter(default(JobHandle));   // main-thread write, already done
            NativeQueue<Entity> water = m_WaterRoadGraph.GetEdgeUpdateQueue(out JobHandle waterDeps);
            waterDeps.Complete();
            water.Enqueue(road);
            m_WaterRoadGraph.AddQueueWriter(default(JobHandle));

            m_Queued++;
            if (removed)
                m_QueuedRemoved++;
            else
                m_QueuedAdded++;
            bool hasPower = EntityManager.HasComponent<ElectricityConsumer>(sign);
            bool hasWater = EntityManager.HasComponent<WaterConsumer>(sign);
            Mod.Log.Info($"{what} — road {Ent(road)} queued for the power and water link (sign consumers now: power " +
                         $"{(hasPower ? "yes" : "no")}, water {(hasWater ? "yes" : "no")})");
        }

        /// <summary>The hub DUMP's sign-utility lines: per Welcome Sign the road edge, both consumers and whether the road's link
        /// to the supply sink exists. Main thread, event time only. Appends only; never throws.</summary>
        public void AppendDump(StringBuilder sb)
        {
            try
            {
                sb.Append("  Sign utilities: armed ").Append(m_Armed).Append(", disabled ").Append(m_Disabled)
                  .Append(", queued this load ").Append(m_Queued).Append(" (").Append(m_QueuedAdded).Append(" added, ")
                  .Append(m_QueuedRemoved).Append(" removed), not queued: no road ").Append(m_NoRoad)
                  .Append(", sign removed ").Append(m_SignGone).Append(", road removed ").Append(m_EdgeGone).Append('\n');
                if (m_SignPrefabs.Count == 0)
                {
                    sb.Append("    no Welcome Sign prefab resolved this load\n");
                    return;
                }

                // Complete the writers of the flow graph components once, then read them through lookups on the main thread
                EntityManager.CompleteDependencyBeforeRO<ConnectedFlowEdge>();
                EntityManager.CompleteDependencyBeforeRO<ElectricityFlowEdge>();
                EntityManager.CompleteDependencyBeforeRO<WaterPipeEdge>();
                m_FlowConnectionLookup.Update(this);
                m_ElectricityEdgeLookup.Update(this);
                m_WaterEdgeLookup.Update(this);

                int signs = 0;
                EntityManager.CompleteDependencyBeforeRO<PrefabRef>();
                using (var entities = m_BuildingQuery.ToEntityArray(Allocator.Temp))
                using (var prefabRefs = m_BuildingQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
                {
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (!m_SignPrefabs.Contains(prefabRefs[i].m_Prefab))
                            continue;
                        signs++;
                        AppendSign(sb, entities[i]);
                    }
                }
                if (signs == 0)
                    sb.Append("    no Welcome Sign in the city\n");
            }
            catch (Exception e)
            {
                sb.Append("  Sign utilities: DUMP failed (").Append(e.GetType().Name).Append(": ").Append(e.Message).Append(")\n");
            }
        }

        private void AppendSign(StringBuilder sb, Entity sign)
        {
            sb.Append("    sign ").Append(Ent(sign)).Append(": road ");
            Entity road = EntityManager.TryGetComponent(sign, out Building building) ? building.m_RoadEdge : Entity.Null;
            sb.Append(road == Entity.Null ? "none" : Ent(road));

            sb.Append("; power ");
            if (EntityManager.TryGetComponent(sign, out ElectricityConsumer power))
                sb.Append("wanted ").Append(power.m_WantedConsumption).Append(", fulfilled ").Append(power.m_FulfilledConsumption)
                  .Append(", cooldown ").Append(power.m_CooldownCounter).Append(", flags ").Append(power.m_Flags.ToString());
            else
                sb.Append("no consumer");
            sb.Append("; water ");
            if (EntityManager.TryGetComponent(sign, out WaterConsumer water))
                sb.Append("wanted ").Append(water.m_WantedConsumption).Append(", fresh ").Append(water.m_FulfilledFresh)
                  .Append(", sewage ").Append(water.m_FulfilledSewage).Append(", cooldowns ").Append(water.m_FreshCooldownCounter)
                  .Append('/').Append(water.m_SewageCooldownCounter).Append(", flags ").Append(water.m_Flags.ToString());
            else
                sb.Append("no consumer");

            sb.Append("; road link to supply: ");
            if (road == Entity.Null || !EntityManager.Exists(road))
            {
                sb.Append("none (no road edge)\n");
                return;
            }
            sb.Append("power ");
            AppendPowerLink(sb, road);
            sb.Append(", water ");
            AppendWaterLink(sb, road);
            sb.Append('\n');
        }

        /// <summary>The road node's flow edge to ElectricityFlowSystem.sinkNode (the link the road graph job
        /// creates, ElectricityRoadConnectionGraphSystem.cs:124-130). Guards the asserts and the buffer indexer of
        /// ElectricityGraphUtils.TryGetFlowEdge (ElectricityGraphUtils.cs:38-54: node indices > 0, the start node
        /// must carry ConnectedFlowEdge).</summary>
        private void AppendPowerLink(StringBuilder sb, Entity road)
        {
            if (!EntityManager.TryGetComponent(road, out ElectricityNodeConnection connection))
            {
                sb.Append("no cable on this road");
                return;
            }
            Entity node = connection.m_ElectricityNode;
            Entity sink = m_ElectricityFlow.sinkNode;
            if (node.Index <= 0 || sink.Index <= 0 || !m_FlowConnectionLookup.HasBuffer(node))
            {
                sb.Append("unknown (flow graph not ready)");
                return;
            }
            if (ElectricityGraphUtils.TryGetFlowEdge(node, sink, ref m_FlowConnectionLookup, ref m_ElectricityEdgeLookup,
                    out Entity _, out ElectricityFlowEdge edge))
                sb.Append("yes (capacity ").Append(edge.m_Capacity).Append(", flow ").Append(edge.m_Flow).Append(')');
            else
                sb.Append("MISSING");
        }

        /// <summary>The water twin (WaterPipeRoadConnectionGraphSystem.cs:123-129; WaterPipeGraphUtils.cs:37-53).</summary>
        private void AppendWaterLink(StringBuilder sb, Entity road)
        {
            if (!EntityManager.TryGetComponent(road, out WaterPipeNodeConnection connection))
            {
                sb.Append("no pipes on this road");
                return;
            }
            Entity node = connection.m_WaterPipeNode;
            Entity sink = m_WaterFlow.sinkNode;
            if (node.Index <= 0 || sink.Index <= 0 || !m_FlowConnectionLookup.HasBuffer(node))
            {
                sb.Append("unknown (flow graph not ready)");
                return;
            }
            if (WaterPipeGraphUtils.TryGetFlowEdge(node, sink, ref m_FlowConnectionLookup, ref m_WaterEdgeLookup,
                    out Entity _, out WaterPipeEdge edge))
                sb.Append("yes (fresh capacity ").Append(edge.m_FreshCapacity).Append(", flow ").Append(edge.m_FreshFlow)
                  .Append("; sewage capacity ").Append(edge.m_SewageCapacity).Append(", flow ").Append(edge.m_SewageFlow).Append(')');
            else
                sb.Append("MISSING");
        }

        /// <summary>Fast PrefabID path with a name-scan fallback (as SkiRunOwnershipSystem.ResolveSet); a missing prefab is tolerated.</summary>
        private void ResolvePrefabs()
        {
            m_SignPrefabs.Clear();
            foreach (string name in SignPrefabNames)
            {
                Entity prefabEntity = Entity.Null;
                if (m_PrefabSystem.TryGetPrefab(new PrefabID("BuildingPrefab", name), out PrefabBase prefabBase))
                    m_PrefabSystem.TryGetEntity(prefabBase, out prefabEntity);
                if (prefabEntity == Entity.Null)
                {
                    var candidates = m_PrefabScanQuery.ToEntityArray(Allocator.Temp);
                    try
                    {
                        for (int i = 0; i < candidates.Length; i++)
                        {
                            if (m_PrefabSystem.GetPrefabName(candidates[i]) == name)
                            {
                                prefabEntity = candidates[i];
                                break;
                            }
                        }
                    }
                    finally
                    {
                        candidates.Dispose();
                    }
                }
                if (prefabEntity != Entity.Null)
                    m_SignPrefabs.Add(prefabEntity);
            }
        }

        private static string Ent(Entity e)
        {
            return e == Entity.Null ? "null" : e.Index + ":" + e.Version;
        }
    }
}
