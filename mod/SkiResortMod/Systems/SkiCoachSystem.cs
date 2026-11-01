// The Ski Bus Depots' coach: one SkiCoachBus (Bus01 with SKI BUS panels) per working depot, in the resort's color, driving real lanes between the depot and the
// area's nearest hill lodge with a road, back and forth. A visual: DummyTraffic carries nobody, belongs to no line, bills nothing.
// Guests, tickets and the coach bill never read it (SkiDaySystem, SkiBusChargeSystem); no citizen routes through it (DECISIONS 392 stands).
// Phase: GameSimulation, UpdateBefore<SkiCoachSystem, TransportCarAISystem> (Mod.cs). Anchor AND interval (16) are load-bearing: same interval
// inherits the AI's offset 1 (UpdateSystem.cs:431-436, 1.6.2f1), so the turnaround lands between navigation and the AI tick of the same frame.
// Stateless across a save: a coach is found by its Owner's prefab; the probe carries LivePath, which the saver skips. Pause stops it; the Editor never arms it.
// Engine notes: EngineNotes/SkiCoachSystem.md

using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Pathfind;
using Game.Prefabs;
using Game.Routes;
using Game.Simulation;
using Game.Tools;
using Game.Vehicles;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiCoachSystem : GameSystemBase
    {
        /// <summary>Bus01 cloned with its SKI BUS panels and out of the vanilla bus pool (m_Purposes 0, m_MaintenanceRange 0).</summary>
        private const string BusPrefabName = "SkiCoachBus";

        /// <summary>Elapsed frames before a depot whose probe found no road tries again.</summary>
        private const uint NoRoadRetryFrames = 16384;

        /// <summary>A probe unanswered for this long is dropped and tried again.</summary>
        private const uint ProbeTimeoutFrames = 2048;

        /// <summary>Ticks of an empty depot list before coaches are removed: the list is empty after a load until the first ski-day pass.</summary>
        private const int EmptyViewTicks = 32;

        /// <summary>The first turnarounds of a load print, the rest count in the spawn line.</summary>
        private const int LoggedTurns = 4;

        /// <summary>The depot palette's oxblood.</summary>
        private static readonly UnityEngine.Color32 CoachColor = new UnityEngine.Color32(112, 38, 36, 255);

        private struct Slot
        {
            public Entity Probe, From, To, LastTarget;
            public uint ProbeFrame, RetryFrame;
            public bool NoRoadLogged;
        }

        private SimulationSystem m_SimulationSystem;
        private PrefabSystem m_PrefabSystem;
        private SkiDaySystem m_SkiDays;
        private PathfindSetupSystem m_PathfindSetup;
        private EndFrameBarrier m_Barrier;
        private EntityQuery m_VehicleQuery;
        private EntityArchetype m_ProbeArchetype;
        private readonly List<SkiDaySystem.DepotView> m_Depots = new List<SkiDaySystem.DepotView>(2);
        private readonly Dictionary<Entity, Slot> m_Slots = new Dictionary<Entity, Slot>(2);
        private readonly Dictionary<Entity, Entity> m_LodgeOf = new Dictionary<Entity, Entity>(2);
        private readonly HashSet<Entity> m_Driving = new HashSet<Entity>();
        private EntityQuery m_CarPrefabQuery;
        private Entity m_BusPrefab;
        private bool m_Armed, m_Disabled, m_OffsetLogged;
        private int m_EmptyTicks, m_Turns, m_Spawns;
        private Unity.Mathematics.Random m_Random;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_SkiDays = World.GetOrCreateSystemManaged<SkiDaySystem>();
            m_PathfindSetup = World.GetOrCreateSystemManaged<PathfindSetupSystem>();
            m_Barrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_VehicleQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Vehicles.PublicTransport>(), ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<CarCurrentLane>(), ComponentType.ReadOnly<PathOwner>(), ComponentType.ReadOnly<Target>(),
                },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            m_CarPrefabQuery = GetEntityQuery(ComponentType.ReadOnly<CarData>(), ComponentType.ReadOnly<PrefabData>());
            m_ProbeArchetype = EntityManager.CreateArchetype(ComponentType.ReadWrite<PathInformation>(), ComponentType.ReadWrite<PathElement>(),
                ComponentType.ReadWrite<LivePath>());
            m_Random = new Unity.Mathematics.Random(0x5B05C0ACu);
            Mod.Log.Info($"{nameof(SkiCoachSystem)} created");
        }

        /// <summary>TransportCarAISystem's interval (engine notes: the turnaround).</summary>
        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 16;
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_Disabled = false;
            m_BusPrefab = Entity.Null;
            m_OffsetLogged = false;
            m_EmptyTicks = 0;
            m_Turns = 0;
            m_Spawns = 0;
            m_Slots.Clear();
            m_LodgeOf.Clear();
        }

        protected override void OnUpdate()
        {
            if (!m_Armed || m_Disabled || !ResolvePrefabs())
                return;
            uint frame = m_SimulationSystem.frameIndex;
            if (!m_OffsetLogged)
            {
                m_OffsetLogged = true;
                Mod.Log.Info($"Coach [armed] frame mod 16 = {frame & 15}, TransportCarAISystem's offset is 1");
            }

            m_SkiDays.CopyDepots(m_Depots);
            m_EmptyTicks = m_Depots.Count == 0 ? m_EmptyTicks + 1 : 0;
            if (m_Depots.Count == 0 && m_EmptyTicks < EmptyViewTicks)
                return;

            m_LodgeOf.Clear();
            for (int i = 0; i < m_Depots.Count; i++)
            {
                SkiDaySystem.DepotView depot = m_Depots[i];
                // The views date from the last ski-day pass: a building removed since then is gone already.
                if (SkiBusRules.IsBilled(depot.Verdict) && depot.CoachLodge != Entity.Null && Alive(depot.Depot) && Alive(depot.CoachLodge))
                    m_LodgeOf[depot.Depot] = depot.CoachLodge;
            }
            if (m_LodgeOf.Count == 0 && m_Slots.Count == 0 && m_VehicleQuery.IsEmptyIgnoreFilter)
                return;

            EntityCommandBuffer commands = m_Barrier.CreateCommandBuffer();
            TendCoaches(commands);
            TendSpawns(commands, frame);
        }

        /// <summary>Removes coaches without a working depot, turns the others around at the end of their path.</summary>
        private void TendCoaches(EntityCommandBuffer commands)
        {
            m_Driving.Clear();
            using NativeArray<Entity> vehicles = m_VehicleQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < vehicles.Length; i++)
            {
                Entity vehicle = vehicles[i];
                if (EntityManager.GetComponentData<PrefabRef>(vehicle).m_Prefab != m_BusPrefab ||
                    (EntityManager.GetComponentData<Game.Vehicles.PublicTransport>(vehicle).m_State & PublicTransportFlags.DummyTraffic) == 0)
                    continue;   // a line bus
                Entity depot = EntityManager.GetComponentData<Owner>(vehicle).m_Owner;
                bool ownerGone = !EntityManager.Exists(depot) || EntityManager.HasComponent<Deleted>(depot);
                if (!ownerGone && !m_LodgeOf.ContainsKey(depot) && m_PrefabSystem.GetPrefabName(EntityManager.GetComponentData<PrefabRef>(depot).m_Prefab) != SkiCatalog.Names.BusDepot)
                    continue;   // vanilla's traffic filler
                if (ownerGone || !m_LodgeOf.TryGetValue(depot, out Entity lodge) || !m_Driving.Add(depot))
                {
                    commands.AddComponent<Deleted>(vehicle);
                    continue;
                }

                Target target = EntityManager.GetComponentData<Target>(vehicle);
                Slot slot = m_Slots.TryGetValue(depot, out Slot known) ? known : default;
                slot.LastTarget = target.m_Target;
                m_Slots[depot] = slot;

                CarCurrentLane lane = EntityManager.GetComponentData<CarCurrentLane>(vehicle);
                PathOwner path = EntityManager.GetComponentData<PathOwner>(vehicle);
                if ((lane.m_LaneFlags & Game.Vehicles.CarLaneFlags.EndOfPath) == 0 || (path.m_State & (PathFlags.Pending | PathFlags.Obsolete)) != 0)
                    continue;
                // What SetupPathfind clears when the AI repaths in this same frame: without EndOfPath the delete at
                // TransportCarAISystem.cs:366-397 cannot fire, and RequireNewPath (:480) takes the new target.
                VehicleUtils.SetTarget(ref path, ref target, target.m_Target == depot ? lodge : depot);
                lane.m_LaneFlags &= ~Game.Vehicles.CarLaneFlags.EndOfPath;
                EntityManager.SetComponentData(vehicle, path);
                EntityManager.SetComponentData(vehicle, target);
                EntityManager.SetComponentData(vehicle, lane);
                slot.LastTarget = target.m_Target;
                m_Slots[depot] = slot;
                if (++m_Turns <= LoggedTurns)
                    Mod.Log.Info($"Coach [turn] {Fmt(vehicle)} of depot {Fmt(depot)} now drives to {Fmt(target.m_Target)}");
            }
        }

        /// <summary>A working depot without a coach: a road probe first, the vehicle on its answer. Vanilla never makes the vehicle before the path.</summary>
        private void TendSpawns(EntityCommandBuffer commands, uint frame)
        {
            foreach (KeyValuePair<Entity, Entity> pair in m_LodgeOf)
            {
                Entity depot = pair.Key, lodge = pair.Value;
                if (m_Driving.Contains(depot))
                    continue;
                Slot slot = m_Slots.TryGetValue(depot, out Slot known) ? known : default;
                if (slot.Probe == Entity.Null)
                {
                    if (frame >= slot.RetryFrame)
                        StartProbe(depot, lodge, frame, ref slot);
                }
                else
                    FinishProbe(commands, depot, frame, ref slot);
                m_Slots[depot] = slot;
            }

            // A depot that stopped working mid-probe.
            if (m_Slots.Count == m_LodgeOf.Count)
                return;
            using NativeList<Entity> gone = new NativeList<Entity>(Allocator.Temp);
            foreach (KeyValuePair<Entity, Slot> pair in m_Slots)
                if (!m_LodgeOf.ContainsKey(pair.Key))
                    gone.Add(pair.Key);
            for (int i = 0; i < gone.Length; i++)
            {
                Slot slot = m_Slots[gone[i]];
                if (slot.Probe != Entity.Null && EntityManager.Exists(slot.Probe))
                    EntityManager.DestroyEntity(slot.Probe);
                m_Slots.Remove(gone[i]);
            }
        }

        private void StartProbe(Entity depot, Entity lodge, uint frame, ref Slot slot)
        {
            // A coach that was removed at its target starts again from there, so a missed turnaround reads as a stop.
            bool fromLodge = slot.LastTarget == lodge;
            slot.From = fromLodge ? lodge : depot;
            slot.To = fromLodge ? depot : lodge;
            slot.ProbeFrame = frame;
            using (NativeArray<Entity> created = new NativeArray<Entity>(1, Allocator.Temp))
            {
                EntityManager.CreateEntity(m_ProbeArchetype, created);   // the single-entity overload needs ReadOnlySpan, absent from this target
                slot.Probe = created[0];
            }
            EntityManager.SetComponentData(slot.Probe, new PathInformation { m_State = PathFlags.Pending });

            CarData car = EntityManager.GetComponentData<CarData>(m_BusPrefab);
            PathMethod methods = VehicleUtils.GetPathMethods(car);
            PathfindParameters parameters = new PathfindParameters
            {
                m_MaxSpeed = car.m_MaxSpeed,
                m_WalkSpeed = 5.555556f,
                m_Methods = methods,
                m_Weights = new PathfindWeights(1f, 1f, 1f, 1f),
                m_IgnoredRules = RuleFlags.ForbidPrivateTraffic | VehicleUtils.GetIgnoredPathfindRules(car),
            };
            SetupQueueTarget origin = new SetupQueueTarget { m_Type = SetupTargetType.CurrentLocation, m_Methods = methods, m_RoadTypes = RoadTypes.Car, m_Entity = slot.From };
            SetupQueueTarget destination = new SetupQueueTarget { m_Type = SetupTargetType.CurrentLocation, m_Methods = methods, m_RoadTypes = RoadTypes.Car, m_Entity = slot.To };
            m_PathfindSetup.GetQueue(this, 64).Enqueue(new SetupQueueItem(slot.Probe, parameters, origin, destination));
        }

        private void FinishProbe(EntityCommandBuffer commands, Entity depot, uint frame, ref Slot slot)
        {
            if (!EntityManager.Exists(slot.Probe))
            {
                slot.Probe = Entity.Null;
                return;
            }
            PathInformation info = EntityManager.GetComponentData<PathInformation>(slot.Probe);
            if ((info.m_State & PathFlags.Pending) != 0)
            {
                if (frame - slot.ProbeFrame < ProbeTimeoutFrames)
                    return;
                Mod.Log.Warn($"Coach [probe] depot {Fmt(depot)}: no answer after {ProbeTimeoutFrames} frames, trying again");
            }
            else if (info.m_Origin != Entity.Null && EntityManager.Exists(slot.From) && EntityManager.Exists(slot.To))
            {
                Spawn(commands, depot, slot.From, slot.To, slot.Probe, info);
                slot.RetryFrame = frame + 64;   // the barrier plays back at the end of this frame; the vehicle query sees it next tick
                slot.NoRoadLogged = false;
            }
            else
            {
                slot.RetryFrame = frame + NoRoadRetryFrames;
                if (!slot.NoRoadLogged)
                    Mod.Log.Info($"Coach [no road] depot {Fmt(depot)}: no road from {Fmt(slot.From)} to {Fmt(slot.To)}, no coach; retries every {NoRoadRetryFrames} frames");
                slot.NoRoadLogged = true;
            }
            EntityManager.DestroyEntity(slot.Probe);
            slot.Probe = Entity.Null;
        }

        /// <summary>Same result as TransportVehicleSelectData.cs:168-177 plus TrafficSpawnerAISystem.cs:368-397, for the one prefab instead of a random bus.</summary>
        private void Spawn(EntityCommandBuffer commands, Entity depot, Entity from, Entity to, Entity probe, in PathInformation info)
        {
            Entity bus = commands.CreateEntity(EntityManager.GetComponentData<ObjectData>(m_BusPrefab).m_Archetype);
            commands.SetComponent(bus, EntityManager.GetComponentData<Transform>(from));
            commands.SetComponent(bus, new PrefabRef(m_BusPrefab));
            commands.SetComponent(bus, new PseudoRandomSeed(ref m_Random));
            commands.SetComponent(bus, new Game.Vehicles.PublicTransport { m_State = PublicTransportFlags.DummyTraffic });
            commands.SetComponent(bus, new Target(to));
            commands.AddComponent(bus, new Owner(depot));
            commands.AddComponent(bus, new TripSource(from));
            commands.AddComponent(bus, default(Unspawned));
            commands.AddComponent(bus, default(PassengerTransport));
            commands.AddComponent(bus, new Game.Routes.Color(CoachColor));
            DynamicBuffer<PathElement> path = commands.SetBuffer<PathElement>(bus);
            PathUtils.CopyPath(EntityManager.GetBuffer<PathElement>(probe), default(PathOwner), 0, path);
            commands.SetComponent(bus, new PathOwner(0, PathFlags.Updated));
            m_Spawns++;
            Mod.Log.Info($"Coach [spawn] depot {Fmt(depot)}: {Fmt(from)} -> {Fmt(to)}, {info.m_Distance:F0} m, {info.m_Duration:F0} s by the pathfinder; " +
                         $"spawn {m_Spawns}, {m_Turns} turnaround(s) since the load");
        }

        /// <summary>By name over the car prefabs: a lookup by PrefabID can miss a prefab that exists (SkiNightLightSystem.ResolvePrefab has the same scan).</summary>
        private bool ResolvePrefabs()
        {
            if (m_BusPrefab != Entity.Null)
                return true;
            using NativeArray<Entity> cars = m_CarPrefabQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < cars.Length && m_BusPrefab == Entity.Null; i++)
            {
                if (m_PrefabSystem.GetPrefabName(cars[i]) == BusPrefabName)
                    m_BusPrefab = cars[i];
            }
            if (m_BusPrefab != Entity.Null)
                return true;
            m_Disabled = true;
            Mod.Log.Warn($"Coach [off] prefab {BusPrefabName} not found among {cars.Length} car prefabs: no coach this session, the depots still work");
            return false;
        }

        private bool Alive(Entity entity) => EntityManager.Exists(entity) && !EntityManager.HasComponent<Deleted>(entity);

        private static string Fmt(Entity entity) => $"{entity.Index}:{entity.Version}";
    }
}
