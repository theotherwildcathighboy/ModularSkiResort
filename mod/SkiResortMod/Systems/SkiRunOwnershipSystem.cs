// SkiRunOwnershipSystem — binds drawn ski runs to their Ski Area Welcome Sign (the
// resort gate), supplying the three upgrade behaviors the engine does not provide for
// area-class ServiceUpgrades (entry 90 research, all decompile-verified):
//
//   THE GAP: our run SurfacePrefabs carry Game.Prefabs.ServiceUpgrade so they appear in
//   the gate's upgrade panel (BuildingInitializeSystem builds BuildingUpgradeElement for
//   ANY carrier, :1200-1208) and clicking one correctly activates the area tool — but
//   every downstream upgrade behavior is Object-gated in vanilla:
//     * Owner: AreaToolSystem never learns the selected building; a fresh surface gets
//       NO Owner in game mode (AreaToolSystem.cs:2105-2111 editor-only owner path).
//     * Cost: Temp.m_Cost is only written on the object path
//       (GenerateObjectsSystem.cs:1003-1006); GenerateAreasSystem never charges.
//     * InstalledUpgrade: both writers require Game.Objects.Object
//       (ServiceUpgradeReferencesSystem + the load-time rebuilder) — areas are invisible
//       to them, so we deliberately skip InstalledUpgrade entirely.
//     * Tether: the only m_MaxPlacementDistance check is building-object-only
//       (Game.Objects.ValidationHelpers.ValidateSubPlacement).
//
//   WHAT THIS SYSTEM DOES, every 64 sim frames (idempotent sweep):
//     1. BIND: an unowned run whose polygon center lies within Tuning.TetherM of a gate
//        gets Owner(nearest gate) + a one-time bind charge via PlayerMoney (the upkeep
//        system's money pattern). The charge constants MUST match the display-only
//        m_UpgradeCost values in the three run prefabs — vanilla never charges areas,
//        so the prefab field is UI truth and this system is the till.
//     2. CASCADE WIRING: the gate instance gets a runtime SubArea buffer (AddBuffer —
//        the SkiRunPropSystem practice) and the run is appended. That is the exact
//        buffer Game.Objects.SubElementDeleteSystem walks on bulldoze, so deleting the
//        gate deletes its runs — vanilla machinery, zero mod code at delete time.
//        SubArea is IEmptySerializable (rebuilt-on-load), and vanilla's rebuilder is
//        Object-gated, so this sweep re-appends after every load — which is why it must
//        stay idempotent rather than Created-driven.
//     3. HEAL: a bound run whose owner vanished without the cascade (should not happen;
//        defensive) has its Owner stripped so it can rebind or read as unbound.
//
//   ENFORCEMENT: SkiSnowInjectionSystem refuses to classify UNBOUND runs (its BuildMask
//   gate) — an unbound run stays summer-bare, bills nothing and scores nothing, and the
//   injection idle/topup log says why. Draw order is free: runs drawn before any gate
//   exists simply light up (and get charged) on the first sweep after a gate arrives.
//   Border props still appear on unbound runs on purpose — hardware without snow reads
//   as "installed, not operating", which is honest feedback.
//
//   XP: deliberately none. Area re-draws re-trigger Created, so a prefab m_XPReward
//   would be an XP faucet (redraw = free XP); the prefabs ship m_XPReward: 0 and this
//   system awards none in v1.
//
// SCOPE: Game mode only. Defensive contract per house rules (ChairPinSystem precedent).

using System;
using System.Collections.Generic;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Areas;
using Game.City;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunOwnershipSystem : GameSystemBase
    {
        private static class Tuning
        {
            /// <summary>Max distance (m) from a run polygon's center to its gate. The
            /// user's number: 5 km covers a full real-scale resort on the 14.3 km
            /// playable map. Mirrored (display-only) in the prefabs'
            /// m_MaxPlacementDistance.</summary>
            public const float TetherM = 5000f;

            /// <summary>One-time bind charges. MUST match the three run prefabs'
            /// m_UpgradeCost fields — the panel displays those, this system charges
            /// these; they are the same number by contract.</summary>
            public const int BindCostGroomed = 4000;
            public const int BindCostSnowmaking = 12000;
            public const int BindCostNight = 20000;

            /// <summary>Sweep cadence — same 64-frame family as injection; binding
            /// within one window of drawing is imperceptible.</summary>
            public const int SweepIntervalFrames = 64;

            /// <summary>Heartbeat: log every Nth sweep even when nothing changed.</summary>
            public const int LogEveryNthSweep = 64;
        }

        private static readonly string[] GroomedSurfaceNames = { "SkiRunGroomedA" };
        private static readonly string[] SnowmakingSurfaceNames = { "SkiRunSnowmakingA" };
        private static readonly string[] NightSurfaceNames = { "SkiRunNightA" };
        private static readonly string[] GateNames = { "SkiResortGate" };

        private SimulationSystem m_SimulationSystem;
        private PrefabSystem m_PrefabSystem;
        private CitySystem m_CitySystem;

        private EntityQuery m_RunAreaQuery;
        private EntityQuery m_GateQuery;
        private EntityQuery m_PrefabScanQuery;

        private readonly HashSet<Entity> m_GroomedPrefabs = new HashSet<Entity>();
        private readonly HashSet<Entity> m_SnowmakingPrefabs = new HashSet<Entity>();
        private readonly HashSet<Entity> m_NightPrefabs = new HashSet<Entity>();
        private readonly HashSet<Entity> m_GatePrefabs = new HashSet<Entity>();
        private bool m_PrefabsResolved;

        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;
        private bool m_CityWarned;
        private uint m_LastSweepFrame;
        private bool m_SweepFrameValid;
        private long m_SweepCounter;
        private int m_LastBound;
        private int m_LastUnbound;
        private int m_LastGates;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();

            m_RunAreaQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<Geometry>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            m_GateQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Buildings.Building>(),
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            m_PrefabScanQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());

            Mod.Log.Info($"{nameof(SkiRunOwnershipSystem)} created");
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 64;
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_CityWarned = false;
            m_SweepFrameValid = false;
            m_SweepCounter = 0;
            m_LastBound = -1;
            m_LastUnbound = -1;
            m_LastGates = -1;

            if (mode != GameMode.Game)
                return;

            try
            {
                ResolvePrefabs();
                if (m_GatePrefabs.Count == 0 ||
                    (m_GroomedPrefabs.Count == 0 && m_SnowmakingPrefabs.Count == 0 &&
                     m_NightPrefabs.Count == 0))
                {
                    Mod.Log.Info("Ownership: gate or run prefabs missing — binding inert " +
                                 "(expected pre-import)");
                    return;
                }
                m_Armed = true;
                Mod.Log.Info("Ownership: ARMED — runs bind to the nearest gate within " +
                             $"{Tuning.TetherM:F0} m; one-time bind charge groomed " +
                             $"{Tuning.BindCostGroomed} / snowmaking {Tuning.BindCostSnowmaking} " +
                             $"/ night {Tuning.BindCostNight}; gate gains a runtime SubArea " +
                             "buffer so vanilla SubElementDeleteSystem cascades bulldozes; " +
                             $"sweep every {Tuning.SweepIntervalFrames} sim frames, idempotent " +
                             "(SubArea is rebuilt-on-load and vanilla's rebuilder is " +
                             "Object-gated, so the sweep re-appends after every load)");
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiRunOwnershipSystem)} EXCEPTION during load " +
                                  $"setup — run binding disabled until the next game load:\n{e}");
                }
            }
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !m_Armed)
                    return;

                uint frame = m_SimulationSystem.frameIndex;
                if (m_SweepFrameValid &&
                    frame - m_LastSweepFrame < (uint)Tuning.SweepIntervalFrames)
                    return;
                m_SweepFrameValid = true;
                m_LastSweepFrame = frame;

                Sweep();
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiRunOwnershipSystem)} EXCEPTION — run " +
                                  $"binding disabled until the next game load:\n{e}");
                }
            }
        }

        private void Sweep()
        {
            // Gates first: entity + XZ position. A handful of buildings; managed scan.
            var gates = new NativeList<Entity>(4, Allocator.Temp);
            var gatePos = new NativeList<float2>(4, Allocator.Temp);
            var runs = new NativeList<Entity>(16, Allocator.Temp);
            try
            {
                using (var gateChunks = m_GateQuery.ToArchetypeChunkArray(Allocator.Temp))
                {
                    var entityHandle = GetEntityTypeHandle();
                    var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                    var transformHandle =
                        GetComponentTypeHandle<Game.Objects.Transform>(isReadOnly: true);
                    for (int c = 0; c < gateChunks.Length; c++)
                    {
                        var entities = gateChunks[c].GetNativeArray(entityHandle);
                        var prefabRefs = gateChunks[c].GetNativeArray(ref prefabRefHandle);
                        var transforms = gateChunks[c].GetNativeArray(ref transformHandle);
                        for (int i = 0; i < prefabRefs.Length; i++)
                        {
                            if (!m_GatePrefabs.Contains(prefabRefs[i].m_Prefab))
                                continue;
                            gates.Add(entities[i]);
                            gatePos.Add(transforms[i].m_Position.xz);
                        }
                    }
                }

                using (var runChunks = m_RunAreaQuery.ToArchetypeChunkArray(Allocator.Temp))
                {
                    var entityHandle = GetEntityTypeHandle();
                    var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                    for (int c = 0; c < runChunks.Length; c++)
                    {
                        var entities = runChunks[c].GetNativeArray(entityHandle);
                        var prefabRefs = runChunks[c].GetNativeArray(ref prefabRefHandle);
                        for (int i = 0; i < prefabRefs.Length; i++)
                        {
                            Entity prefab = prefabRefs[i].m_Prefab;
                            if (m_GroomedPrefabs.Contains(prefab) ||
                                m_SnowmakingPrefabs.Contains(prefab) ||
                                m_NightPrefabs.Contains(prefab))
                                runs.Add(entities[i]);
                        }
                    }
                }

                int bound = 0, unbound = 0, newBinds = 0, healed = 0;
                long chargedThisSweep = 0;
                for (int r = 0; r < runs.Length; r++)
                {
                    Entity run = runs[r];
                    if (EntityManager.TryGetComponent(run, out Owner owner))
                    {
                        // HEAL: owner must be a live gate. The bulldoze cascade normally
                        // deletes the run with the gate, so this path is defensive only.
                        bool ownerValid = EntityManager.Exists(owner.m_Owner) &&
                            !EntityManager.HasComponent<Deleted>(owner.m_Owner) &&
                            EntityManager.TryGetComponent(owner.m_Owner, out PrefabRef opr) &&
                            m_GatePrefabs.Contains(opr.m_Prefab);
                        if (!ownerValid)
                        {
                            EntityManager.RemoveComponent<Owner>(run);
                            healed++;
                            unbound++;
                            continue;
                        }
                        EnsureSubAreaMembership(owner.m_Owner, run);
                        bound++;
                        continue;
                    }

                    // BIND: nearest gate within the tether.
                    float2 center = EntityManager.GetComponentData<Geometry>(run)
                        .m_CenterPosition.xz;
                    int best = -1;
                    float bestDist = Tuning.TetherM;
                    for (int g = 0; g < gates.Length; g++)
                    {
                        float d = math.distance(center, gatePos[g]);
                        if (d <= bestDist)
                        {
                            bestDist = d;
                            best = g;
                        }
                    }
                    if (best < 0)
                    {
                        unbound++;
                        continue;
                    }

                    Entity gate = gates[best];
                    EntityManager.AddComponentData(run, new Owner(gate));
                    EnsureSubAreaMembership(gate, run);
                    int cost = BindCostFor(EntityManager.GetComponentData<PrefabRef>(run).m_Prefab);
                    Charge(cost);
                    chargedThisSweep += cost;
                    newBinds++;
                    bound++;
                    Mod.Log.Info($"Ownership [bind]: run bound to gate {bestDist:F0} m away — " +
                                 $"charged {cost} (one-time)");
                }

                m_SweepCounter++;
                bool log = bound != m_LastBound || unbound != m_LastUnbound ||
                           gates.Length != m_LastGates || newBinds > 0 || healed > 0 ||
                           m_SweepCounter % Tuning.LogEveryNthSweep == 1;
                if (log)
                {
                    Mod.Log.Info($"Ownership: {bound} bound + {unbound} unbound run(s), " +
                                 $"{gates.Length} gate(s)" +
                                 (newBinds > 0 ? $", {newBinds} new bind(s) -{chargedThisSweep}" : "") +
                                 (healed > 0 ? $", {healed} dangling owner(s) stripped" : "") +
                                 (unbound > 0 && gates.Length == 0
                                     ? " — unbound runs stay bare until a Ski Area Welcome " +
                                       "Sign is placed within " + $"{Tuning.TetherM:F0} m"
                                     : ""));
                }
                m_LastBound = bound;
                m_LastUnbound = unbound;
                m_LastGates = gates.Length;
            }
            finally
            {
                gates.Dispose();
                gatePos.Dispose();
                runs.Dispose();
            }
        }

        /// <summary>Gate gains a runtime SubArea buffer on first use (the prop-bridge
        /// practice: vanilla components added to entities we do not own the prefab of),
        /// and the run is appended once. Vanilla removes entries on area deletion
        /// (SubAreaReferencesSystem's Deleted arm) and walks the buffer on gate bulldoze
        /// (SubElementDeleteSystem) — both generic, both free.</summary>
        private void EnsureSubAreaMembership(Entity gate, Entity run)
        {
            DynamicBuffer<Game.Areas.SubArea> buffer =
                EntityManager.HasBuffer<Game.Areas.SubArea>(gate)
                    ? EntityManager.GetBuffer<Game.Areas.SubArea>(gate)
                    : EntityManager.AddBuffer<Game.Areas.SubArea>(gate);
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i].m_Area == run)
                    return;
            }
            buffer.Add(new Game.Areas.SubArea(run));
        }

        private int BindCostFor(Entity prefab)
        {
            if (m_NightPrefabs.Contains(prefab))
                return Tuning.BindCostNight;
            if (m_SnowmakingPrefabs.Contains(prefab))
                return Tuning.BindCostSnowmaking;
            return Tuning.BindCostGroomed;
        }

        private void Charge(int amount)
        {
            if (amount <= 0)
                return;
            Entity city = m_CitySystem.City;
            if (city == Entity.Null || !EntityManager.HasComponent<PlayerMoney>(city))
            {
                if (!m_CityWarned)
                {
                    m_CityWarned = true;
                    Mod.Log.Warn("Ownership: City entity has no PlayerMoney — bind charges " +
                                 "skipped (warned once per load)");
                }
                return;
            }
            PlayerMoney money = EntityManager.GetComponentData<PlayerMoney>(city);
            money.Subtract(amount);
            EntityManager.SetComponentData(city, money);
        }

        private void ResolvePrefabs()
        {
            if (m_PrefabsResolved)
                return;
            m_PrefabsResolved = true;
            ResolveSet(GroomedSurfaceNames, "SurfacePrefab", m_GroomedPrefabs);
            ResolveSet(SnowmakingSurfaceNames, "SurfacePrefab", m_SnowmakingPrefabs);
            ResolveSet(NightSurfaceNames, "SurfacePrefab", m_NightPrefabs);
            ResolveSet(GateNames, "BuildingPrefab", m_GatePrefabs);
        }

        /// <summary>House pattern: fast PrefabID path + name-scan fallback, tolerate
        /// missing (pre-import states must not throw).</summary>
        private void ResolveSet(string[] names, string typeName, HashSet<Entity> set)
        {
            foreach (string name in names)
            {
                Entity prefabEntity = Entity.Null;
                if (m_PrefabSystem.TryGetPrefab(new PrefabID(typeName, name),
                        out PrefabBase prefabBase))
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

                if (prefabEntity == Entity.Null)
                {
                    Mod.Log.Info($"Ownership: prefab '{name}' not found (tolerated)");
                    continue;
                }
                set.Add(prefabEntity);
            }
        }
    }
}
