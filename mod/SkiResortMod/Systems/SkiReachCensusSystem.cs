// How many homes and hotels lie within a ski day's drive of each ski area: the hub's "nearest homes and hotels are N km away" row (code 27).
// A hill beyond the city otherwise reads Open with 0 guests and no reason. One Burst job per area, one area per tick: a read of the household
// list, nothing written. Rules: SkiDayRules.DayTripM / NoGuestsNear.

using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Agents;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Tools;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public partial class SkiReachCensusSystem : GameSystemBase
    {
        /// <summary>One area's reading. Households = the city's homes and hotel parties. Near = those within SkiDayRules.DayTripM.
        /// NearestM = the closest, infinity with no household.</summary>
        public struct Reading
        {
            public int Households, Near;
            public float NearestM;
            public int DayTripKm;
        }

        private struct Result { public int Households, Near; public float NearestM; }

        private SkiDaySystem m_SkiDay;
        private EntityQuery m_Query;
        private NativeList<float2> m_Targets;
        private NativeArray<Result> m_Result;
        private JobHandle m_Handle;
        private bool m_InFlight;
        private Entity m_InFlightSign;
        private int m_InFlightKm, m_Next;
        private readonly List<Entity> m_Signs = new List<Entity>(4);
        private readonly Dictionary<Entity, Reading> m_Readings = new Dictionary<Entity, Reading>();
        private readonly Dictionary<Entity, bool> m_Logged = new Dictionary<Entity, bool>();

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 4096;   // about 20 game minutes

        protected override void OnCreate()
        {
            base.OnCreate();
            m_SkiDay = World.GetOrCreateSystemManaged<SkiDaySystem>();
            m_Targets = new NativeList<float2>(16, Allocator.Persistent);
            m_Result = new NativeArray<Result>(1, Allocator.Persistent);
            m_Query = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Household>() },
                Any = new[] { ComponentType.ReadOnly<PropertyRenter>(), ComponentType.ReadOnly<TouristHousehold>() },
                None = new[]
                {
                    ComponentType.ReadOnly<CommuterHousehold>(), ComponentType.ReadOnly<MovingAway>(), ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
        }

        protected override void OnDestroy()
        {
            m_Handle.Complete();
            m_Targets.Dispose();
            m_Result.Dispose();
            base.OnDestroy();
        }

        protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            m_Handle.Complete();
            m_InFlight = false;
            m_Readings.Clear();
            m_Logged.Clear();
        }

        /// <summary>This area's last reading. False until its first one is in.</summary>
        internal bool TryGetReading(Entity sign, out Reading reading) => m_Readings.TryGetValue(sign, out reading);

        protected override void OnUpdate()
        {
            if (m_InFlight)
            {
                m_Handle.Complete();
                m_InFlight = false;
                Harvest();
            }
            m_SkiDay.CopyAreaSigns(m_Signs);
            if (m_Signs.Count == 0)
                return;
            m_Next = m_Next >= m_Signs.Count ? 0 : m_Next;
            Entity sign = m_Signs[m_Next++];
            if (m_SkiDay.TryCopyPresaleInputs(sign, m_Targets, out SkiDayPresaleInputs inputs) != PresaleCopy.Ready)
            {
                m_Readings.Remove(sign);   // no target lodge: nothing to say about guests
                return;
            }
            m_Result[0] = default;
            NativeList<ArchetypeChunk> chunks = m_Query.ToArchetypeChunkListAsync(Allocator.TempJob, out JobHandle chunksHandle);
            m_Handle = new ReachJob
            {
                Chunks = chunks,
                HouseholdType = GetComponentTypeHandle<Household>(isReadOnly: true),
                RenterType = GetComponentTypeHandle<PropertyRenter>(isReadOnly: true),
                TouristType = GetComponentTypeHandle<TouristHousehold>(isReadOnly: true),
                Renters = GetComponentLookup<PropertyRenter>(isReadOnly: true),
                Transforms = GetComponentLookup<Game.Objects.Transform>(isReadOnly: true),
                Targets = m_Targets.AsArray(),
                NearM = (float)SkiDayRules.DayTripM(inputs.Knobs.ReachKm),
                Output = m_Result,
            }.Schedule(JobHandle.CombineDependencies(Dependency, chunksHandle));
            chunks.Dispose(m_Handle);
            Dependency = m_Handle;
            m_InFlight = true;
            m_InFlightSign = sign;
            m_InFlightKm = (int)(SkiDayRules.DayTripM(inputs.Knobs.ReachKm) / 1000.0);
        }

        private void Harvest()
        {
            Result r = m_Result[0];
            var reading = new Reading { Households = r.Households, Near = r.Near, NearestM = r.NearestM, DayTripKm = m_InFlightKm };
            m_Readings[m_InFlightSign] = reading;
            bool none = SkiDayRules.NoGuestsNear(r.Households, r.Near);
            if (!m_Logged.TryGetValue(m_InFlightSign, out bool was) || was != none)
            {
                m_Logged[m_InFlightSign] = none;
                Mod.Log.Info($"SkiDays [near] ski area {m_InFlightSign.Index}:{m_InFlightSign.Version}: {r.Near} of {r.Households} households and hotel parties " +
                             $"within {m_InFlightKm} km, nearest {r.NearestM / 1000f:F1} km — hub row {(none ? "on" : "off")}");
            }
        }

        [BurstCompile]
        private struct ReachJob : IJob
        {
            [ReadOnly] public NativeList<ArchetypeChunk> Chunks;
            [ReadOnly] public ComponentTypeHandle<Household> HouseholdType;
            [ReadOnly] public ComponentTypeHandle<PropertyRenter> RenterType;
            [ReadOnly] public ComponentTypeHandle<TouristHousehold> TouristType;
            [ReadOnly] public ComponentLookup<PropertyRenter> Renters;
            [ReadOnly] public ComponentLookup<Game.Objects.Transform> Transforms;
            [ReadOnly] public NativeArray<float2> Targets;
            public float NearM;
            public NativeArray<Result> Output;

            public void Execute()
            {
                var res = new Result { NearestM = float.PositiveInfinity };
                for (int c = 0; c < Chunks.Length; c++)
                {
                    ArchetypeChunk chunk = Chunks[c];
                    NativeArray<Household> households = chunk.GetNativeArray(ref HouseholdType);
                    NativeArray<PropertyRenter> renters = chunk.GetNativeArray(ref RenterType);
                    NativeArray<TouristHousehold> tourists = chunk.GetNativeArray(ref TouristType);
                    for (int i = 0; i < households.Length; i++)
                    {
                        if ((households[i].m_Flags & HouseholdFlags.Commuter) != 0)
                            continue;
                        Entity property = Entity.Null;
                        if (tourists.IsCreated && tourists.Length > 0)
                        {
                            if (Renters.TryGetComponent(tourists[i].m_Hotel, out PropertyRenter hotel))
                                property = hotel.m_Property;   // a hotel party starts from its hotel (SkiDaySystem.Jobs test 2)
                        }
                        else if (renters.IsCreated && renters.Length > 0)
                            property = renters[i].m_Property;
                        if (!Transforms.TryGetComponent(property, out Game.Objects.Transform at))
                            continue;
                        float2 origin = at.m_Position.xz;
                        float d = float.PositiveInfinity;
                        for (int k = 0; k < Targets.Length; k++)
                            d = math.min(d, SkiDayRules.DistanceXZ(origin, Targets[k]));
                        res.Households++;
                        if (d <= NearM)
                            res.Near++;
                        if (d < res.NearestM)
                            res.NearestM = d;
                    }
                }
                Output[0] = res;
            }
        }
    }
}
