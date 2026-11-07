// The carrier grip round in TramShuttleSystem: the tram's CARRIAGE is the grip (pitched to the track rope) and its CABIN the body
// (plumb, swinging in the wind on the span). The carriage is hosted by the base station like the cabin (a second ObjectSubObjects
// entry per cabin entry, T138 option A, added by prefab surgery after the art import), so HarvestCabins finds both among the base's
// sub-objects (Owner = the station). Pairing: carriage i with cabin i, i.e. the assignment of the base's carriages to its cabins
// with the least summed xz distance (a carriage hangs its cabin straight below it; the two bays are a track gauge apart), so it holds
// whatever the entity order. No carriage (before the surgery, or the prefab missing) = the cabin takes the rigid pose (logged once).
// Poses: on the path, CarrierPose with the cabin's yaw (-t_up, PlaceOnPath) at the path point; on the synthesized dock runs (the
// station track) the carriage takes the track's pitch and the cabin's swing is 0 (guided). Parked: the cabin's hosted pose and the
// carriage assembled above it (body pose + R x (BodyPin - GripPin)), no pitch, no swing. Wind: one CarrierWind per run (kept per base
// station), polled every ChairPinSystem-like 512 render frames.
// Engine notes: EngineNotes/TramShuttleSystem.md ("Carriages")

using System;
using System.Collections.Generic;
using Colossal.Entities;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace SkiResortMod.Systems
{
    public sealed partial class TramShuttleSystem
    {
        private const int WindPollFrames = 512;   // WindSystem.kUpdateInterval (WindSystem.cs:45)

        private WindSystem m_WindSystem;
        private readonly Dictionary<Entity, List<Entity>> m_OwnerCarriages = new Dictionary<Entity, List<Entity>>();
        private readonly Dictionary<Entity, Entity> m_CarriageOf = new Dictionary<Entity, Entity>();
        private readonly Dictionary<Entity, CarrierWind> m_WindByBase = new Dictionary<Entity, CarrierWind>();
        private uint m_WindPollFrame = uint.MaxValue;
        private bool m_TramWindForce;   // a rebuild attached the runs' winds: poll on the next pass
        private bool m_WindErrorLogged, m_NoCarriageLogged;
        private int m_DiagCarriagesFound, m_DiagCarriagesPaired, m_DiagCarriageWrites;

        private void CarriagesOnCreate()
        {
            m_WindSystem = World.GetOrCreateSystemManaged<WindSystem>();
        }

        /// <summary>Per load.</summary>
        private void CarriagesReset()
        {
            m_OwnerCarriages.Clear();
            m_CarriageOf.Clear();
            m_WindByBase.Clear();
            m_TramWindForce = false;
            m_WindPollFrame = uint.MaxValue;
            m_WindErrorLogged = m_NoCarriageLogged = false;
            m_DiagCarriagesFound = m_DiagCarriagesPaired = m_DiagCarriageWrites = 0;
        }

        /// <summary>The Shuttle type's grip (carriage) prefabs FOUND this load (SkiPrefabCatalogSystem.GripTypes).</summary>
        private bool IsCarriagePrefab(Entity prefab, out LiftTypeDef def)
        {
            return m_PrefabCatalog.GripTypes.TryGetValue(prefab, out def) && def != null && def.Mover == MoverKind.Shuttle;
        }

        /// <summary>HarvestCabins' twin for the carriages (same query), then the pairing per base station.</summary>
        private void HarvestCarriages()
        {
            m_OwnerCarriages.Clear();
            m_CarriageOf.Clear();
            m_DiagCarriagesFound = m_DiagCarriagesPaired = 0;
            if (m_PrefabCatalog.GripTypes.Count == 0)
                return;
            m_AllCabinQuery.CompleteDependency();
            var chunks = m_AllCabinQuery.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var entityHandle = GetEntityTypeHandle();
                var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                var ownerHandle = GetComponentTypeHandle<Owner>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    var owners = chunks[c].GetNativeArray(ref ownerHandle);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (!IsCarriagePrefab(prefabRefs[i].m_Prefab, out _))
                            continue;
                        Entity owner = owners[i].m_Owner;
                        if (!m_OwnerCarriages.TryGetValue(owner, out List<Entity> list))
                            m_OwnerCarriages[owner] = list = new List<Entity>(2);
                        list.Add(entities[i]);
                        m_DiagCarriagesFound++;
                    }
                }
            }
            finally
            {
                chunks.Dispose();
            }
            foreach (var kv in m_OwnerCarriages)
            {
                if (m_OwnerCabins.TryGetValue(kv.Key, out List<Entity> cabins))
                    PairCarriages(cabins, kv.Value);
            }
        }

        /// <summary>Carriage i with cabin i: the least summed xz distance (2 x 2 tries both; more = greedy nearest).</summary>
        private void PairCarriages(List<Entity> cabins, List<Entity> carriages)
        {
            int n = Math.Min(cabins.Count, carriages.Count);
            if (n == 0)
                return;
            if (cabins.Count == 2 && carriages.Count == 2)
            {
                float straight = XzDistance(cabins[0], carriages[0]) + XzDistance(cabins[1], carriages[1]);
                float crossed = XzDistance(cabins[0], carriages[1]) + XzDistance(cabins[1], carriages[0]);
                bool swap = crossed < straight;
                m_CarriageOf[cabins[0]] = carriages[swap ? 1 : 0];
                m_CarriageOf[cabins[1]] = carriages[swap ? 0 : 1];
                m_DiagCarriagesPaired += 2;
                return;
            }
            var taken = new HashSet<Entity>();
            for (int i = 0; i < cabins.Count; i++)
            {
                Entity best = Entity.Null;
                float bestD = float.MaxValue;
                for (int k = 0; k < carriages.Count; k++)
                {
                    if (taken.Contains(carriages[k]))
                        continue;
                    float d = XzDistance(cabins[i], carriages[k]);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = carriages[k];
                    }
                }
                if (best == Entity.Null)
                    break;
                taken.Add(best);
                m_CarriageOf[cabins[i]] = best;
                m_DiagCarriagesPaired++;
            }
        }

        private float XzDistance(Entity a, Entity b)
        {
            float3 pa = PositionOf(a), pb = PositionOf(b);
            return math.distance(pa.xz, pb.xz);
        }

        /// <summary>Fills run.Carriage, parallel to run.Cabin (after AssignCabins). On the Run, never keyed by the base station: a base
        /// that starts two lines has a winner and a loser Run, and the loser (no cabins) must not clear the winner's carriages.</summary>
        private Entity[] CarriagesOf(Run run)
        {
            for (int c = 0; c < 2; c++)
            {
                Entity cabin = run.Cabin[c];
                run.Carriage[c] = cabin != Entity.Null && m_CarriageOf.TryGetValue(cabin, out Entity g) ? g : Entity.Null;
            }
            return run.Carriage;
        }

        /// <summary>Rebuild hook: each run's carriages enrolled (InterpolatedTransform after Created + one Updated, like its cabins), its
        /// wind state attached; one INFO when a split tram has cabins without a carriage.</summary>
        private void EnrollCarriages(Run run)
        {
            Entity[] carriages = CarriagesOf(run);
            bool missing = false;
            for (int c = 0; c < 2; c++)
            {
                Entity g = carriages[c];
                if (g == Entity.Null)
                {
                    missing |= run.Cabin[c] != Entity.Null && IsSplitTram(run);
                    continue;
                }
                if (!EntityManager.Exists(g) || EntityManager.HasComponent<Deleted>(g))
                    continue;
                if (!EntityManager.HasComponent<InterpolatedTransform>(g))
                    EntityManager.AddComponentData(g, new InterpolatedTransform(EntityManager.GetComponentData<Transform>(g)));
                if (!EntityManager.HasComponent<Updated>(g) && !EntityManager.HasComponent<Created>(g))
                    EntityManager.AddComponent<Updated>(g);
            }
            if (missing && !m_NoCarriageLogged)
            {
                m_NoCarriageLogged = true;
                Mod.Log.Info($"TramShuttle [carriages]: the tram's carriage prefab is found but a riding cabin has no carriage (the base " +
                             $"station hosts no carriage entries yet): such a cabin keeps the rigid pose ({m_DiagCarriagesFound} carriage(s) " +
                             "found this rebuild; logged once per load)");
            }
            if (!m_WindByBase.TryGetValue(run.Base, out _))
                m_WindByBase[run.Base] = new CarrierWind();
            m_TramWindForce = true;
        }

        private bool IsSplitTram(Run run)
        {
            if (run.Def == null)
                return false;
            foreach (LiftTypeDef d in m_PrefabCatalog.GripTypes.Values)
            {
                if (ReferenceEquals(d, run.Def))
                    return true;
            }
            return false;
        }

        /// <summary>A run whose wind the poll samples: a path, a carriage (only a split tram swings) and its base's wind state.</summary>
        private bool TramWindSamplable(Run run, out CarrierWind wind)
        {
            wind = null;
            return run.Sh != null && (run.Carriage[0] != Entity.Null || run.Carriage[1] != Entity.Null) &&
                   m_WindByBase.TryGetValue(run.Base, out wind);
        }

        /// <summary>Samples the wind at each split tram's path middle every WindPollFrames render frames, on the pass after a rebuild,
        /// and at once while any samplable run's wind is still unknown (before this pass poses: the first sample is the start value,
        /// CarrierWind.Set; a late first sample after zero-wind poses blends in, CP-17). Stamped only when something was sampled. Never
        /// throws.</summary>
        private void PollTramWind(double timeS)
        {
            uint frame = m_RenderingSystem.frameIndex;
            bool due = m_TramWindForce || m_WindPollFrame == uint.MaxValue || frame < m_WindPollFrame ||
                       frame - m_WindPollFrame >= WindPollFrames;
            int samplable = 0;
            bool anyUnknown = false;
            for (int r = 0; r < m_Runs.Count; r++)
            {
                if (!TramWindSamplable(m_Runs[r], out CarrierWind w))
                    continue;
                samplable++;
                anyUnknown |= !w.m_Known;
            }
            if (samplable == 0 || m_WindSystem == null || !(due || anyUnknown))
                return;
            try
            {
                // CellMapSystem.GetMap (CellMapSystem.cs:154-158) + WindSystem.GetWind (WindSystem.cs:68-80), synchronous after Complete.
                NativeArray<Wind> map = m_WindSystem.GetMap(readOnly: true, out JobHandle deps);
                deps.Complete();
                if (!map.IsCreated || map.Length == 0)
                    return;   // not stamped: the next pass tries again
                for (int r = 0; r < m_Runs.Count; r++)
                {
                    Run run = m_Runs[r];
                    if (!TramWindSamplable(run, out CarrierWind wind))
                        continue;
                    LiftLoopCache.Sample(run.Sh.m_A, 0.5 * run.LA, out float3 middle, out _);
                    wind.Set(WindSystem.GetWind(middle, map).m_Wind, timeS);
                }
                m_WindPollFrame = frame;
                m_TramWindForce = false;
            }
            catch (Exception e)
            {
                m_WindPollFrame = frame;   // a failing read is retried at the cadence, not every frame
                m_TramWindForce = false;
                if (!m_WindErrorLogged)
                {
                    m_WindErrorLogged = true;
                    Mod.Log.Error($"!!! TramShuttle [carriages]: reading the wind map failed ({e.GetType().Name}: {e.Message}); the cabins keep " +
                                  "their last wind (logged once per load)");
                }
            }
        }

        /// <summary>PlaceOnPath for a split tram: one sample, the cabin (body) and its carriage (grip). Returns the cabin's result.</summary>
        private MoveResult PlaceSplitOnPath(Run run, int chain, Entity cabin, Entity carriage, LiftLoopCache.Path path, double s,
                                            quaternion fallback, double timeS)
        {
            if (!EntityManager.Exists(cabin) || EntityManager.HasComponent<Deleted>(cabin) ||
                !EntityManager.TryGetComponent(cabin, out Transform transform))
                return MoveResult.Missing;
            LiftLoopCache.Sample(path, s, out float3 rope, out float3 baseToTop);
            quaternion yaw = Game.Net.NetUtils.GetNodeRotation(-baseToTop, fallback);
            CarrierPivots pivots = run.Def.Pivots;
            bool animated = SkiResortMod.Settings.SkiResortSettings.AnimatedLiftCarriersOn;
            float2 swing = float2.zero;
            if (animated)
            {
                // The dock runs (the station track) are guided: no swing there, the ramp starts at their ends.
                double clamped = s > 0.0 ? (s < path.m_Length ? s : path.m_Length) : 0.0;
                float distance = (float)Math.Min(clamped - path.m_BaseRunM, path.m_Length - path.m_TopRunM - clamped);
                if (distance > 0f)
                {
                    float speed = run.Def.SpeedMps;
                    float omega = CarrierPose.Omega(pivots.BodyPin.y);
                    bool known = m_WindByBase.TryGetValue(run.Base, out CarrierWind w);
                    float2 wind = known ? w.At(timeS) : float2.zero;
                    float gust = CarrierPose.Gust(CarrierPose.GustClock(known ? w.GustTimeS(timeS) : timeS), rope);
                    float phase = CarrierPose.SeedPhase((uint)cabin.Index);
                    // The tram's limits (CarrierPose.Limits): the roll about the track-rope point at most 2 deg, at half the
                    // fore-aft amplitude (the 10 m hanger damps it).
                    swing = CarrierPose.Swing(wind, yaw, CarrierPose.SwayArg(clamped, omega, speed, phase), CarrierPose.Ramp(distance, CarrierPose.RampM(speed)),
                                              CarrierPose.Limits(pivots, run.Def.Mover == MoverKind.Shuttle), gust);
                }
            }
            CarrierPose.Pose(rope, baseToTop, yaw, pivots, animated, swing,
                             out float3 gripPosition, out quaternion gripRotation, out float3 bodyPosition, out quaternion bodyRotation);
            if (WriteCarriage(carriage, gripPosition, gripRotation))
                m_DiagCarriageWrites++;
            return Write(cabin, transform, bodyPosition, bodyRotation);
        }

        /// <summary>The parked carriage: assembled over its cabin's hosted pose (no pitch, no swing).</summary>
        private void ParkCarriage(Run run, Entity carriage, Transform cabinPose, bool tag)
        {
            if (carriage == Entity.Null || run.Def == null)
                return;
            CarrierPose.GripOnBody(cabinPose.m_Position, cabinPose.m_Rotation, run.Def.Pivots, out float3 position, out quaternion rotation);
            if (WriteCarriage(carriage, position, rotation) && tag)
                TagUpdated(carriage);
        }

        /// <summary>ParkCarriage for a cabin that rides no run (ParkTheRest): the type comes from the carriage's own prefab.</summary>
        private void ParkCarriageOf(Entity cabin, Transform cabinPose)
        {
            if (!m_CarriageOf.TryGetValue(cabin, out Entity carriage) || !EntityManager.Exists(carriage) ||
                !EntityManager.TryGetComponent(carriage, out PrefabRef prefabRef) || !IsCarriagePrefab(prefabRef.m_Prefab, out LiftTypeDef def))
                return;
            CarrierPose.GripOnBody(cabinPose.m_Position, cabinPose.m_Rotation, def.Pivots, out float3 position, out quaternion rotation);
            if (WriteCarriage(carriage, position, rotation))
                TagUpdated(carriage);
        }

        /// <summary>True when the carriage moved (Write's idempotence).</summary>
        private bool WriteCarriage(Entity carriage, float3 position, quaternion rotation)
        {
            if (carriage == Entity.Null || !EntityManager.Exists(carriage) || EntityManager.HasComponent<Deleted>(carriage) ||
                !EntityManager.TryGetComponent(carriage, out Transform transform))
                return false;
            return Write(carriage, transform, position, rotation) == MoveResult.Moved;
        }
    }
}
