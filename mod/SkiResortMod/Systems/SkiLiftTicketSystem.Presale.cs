// Pre-season pass sale: the ticket system's second job. Once per (ski area, season key), at the first ticket tick in which that
// area's gate is Open in a new season (SkiMoneyRules.SeasonKeyOf), one Burst job rates every resident household of the area's pool
// and sells passes in bulk; the first-arrival sale in SkiLiftTicketSystem.cs then covers only households not in the pool.
// Ordering: the standings are components applied on the main thread at the next tick's step 1, so a tick that starts a sale does not
// drain arrivals (they stay in the counter's pending list, nothing is lost or repeated): a ticket job in the same window could not
// see the standings and would sell the same household a pass again. Scheduled only with no ticket job in flight.
// Engine notes: EngineNotes/SkiLiftTicketSystem.Presale.md

using System;
using System.Diagnostics;
using Game.Agents;
using Game.Buildings;
using Game.City;
using Game.Citizens;
using Game.Common;
using Game.Economy;
using Game.Simulation;
using Game.Tools;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using GameResources = Game.Economy.Resources;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiLiftTicketSystem
    {
        /// <summary>The sale's totals (the job writes one; ApplySale reads it). Offered = rolled in (Bought + CannotPay).</summary>
        private struct SaleResult
        {
            public int Pool, Offered, Bought, CannotPay;
            public long Income, Booked;
            public double ChanceSum;
        }

        private EntityQuery m_SaleQuery;
        private SkiDaySystem m_SkiDay;
        private NativeList<PassWrite> m_SaleWrites;   
        private NativeList<float2> m_SaleTargets;        // the area's target lodges (xz), filled by TryCopyPresaleInputs
        private NativeArray<SaleResult> m_SaleResult; 
        private JobHandle m_SaleHandle;
        private bool m_SaleInFlight;
        private Entity m_SaleSign;
        private int m_SaleSeasonKey, m_SalePassPrice;
        private double m_SaleScheduleMs;
        private readonly System.Collections.Generic.Dictionary<Entity, int> m_SaleSeasonOf = new System.Collections.Generic.Dictionary<Entity, int>();
        // Totals this load, for the dump
        private int m_SalesRun;
        private long m_SalePoolTotal, m_SaleBoughtTotal, m_SaleCannotPayTotal, m_SaleIncomeTotal;
        private double m_SaleLastScheduleMs, m_SaleLastApplyMs, m_SaleLastWaitMs;

        private void SaleOnCreate()
        {
            m_SkiDay = World.GetOrCreateSystemManaged<SkiDaySystem>();
            m_SaleQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Household>(),
                    ComponentType.ReadOnly<PropertyRenter>(),
                    ComponentType.ReadOnly<HouseholdCitizen>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<TouristHousehold>(),
                    ComponentType.ReadOnly<CommuterHousehold>(),
                    ComponentType.ReadOnly<HomelessHousehold>(),
                    ComponentType.ReadOnly<MovingAway>(),
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            m_SaleWrites = new NativeList<PassWrite>(64, Allocator.Persistent);
            m_SaleTargets = new NativeList<float2>(8, Allocator.Persistent);
            m_SaleResult = new NativeArray<SaleResult>(1, Allocator.Persistent);
            IJobExtensions.EarlyJobInit<SaleJob>();
        }

        private void SaleOnDestroy()
        {
            try
            {
                if (m_SaleInFlight)
                    ApplySale(quiet: true);
                m_SaleHandle.Complete();
            }
            catch (Exception)
            {
                // teardown: nothing left to report to
            }
            if (m_SaleWrites.IsCreated) m_SaleWrites.Dispose();
            if (m_SaleTargets.IsCreated) m_SaleTargets.Dispose();
            if (m_SaleResult.IsCreated) m_SaleResult.Dispose();
        }

        private void SaleOnLoad()
        {
            m_SaleHandle.Complete();
            m_SaleInFlight = false;
            m_SaleWrites.Clear();
            m_SaleTargets.Clear();
            m_SaleSeasonOf.Clear();
            m_SaleSign = Entity.Null;
            m_SalesRun = 0;
            m_SalePoolTotal = m_SaleBoughtTotal = m_SaleCannotPayTotal = m_SaleIncomeTotal = 0;
            m_SaleLastScheduleMs = m_SaleLastApplyMs = m_SaleLastWaitMs = 0.0;
        }

        /// <summary>Step 2c (main thread, after step 1 applied last tick's ticket job): starts the sale of the first ski area whose
        /// watched gate is Open and whose sale has not run this season. Returns true when a sale was scheduled (the caller skips the
        /// arrival drain this tick, engine notes Ordering). Nothing happens with passes off, no ticket price, or a job in flight.</summary>
        private bool TryStartSale(int price)
        {
            if (m_JobInFlight || m_SaleInFlight || PassShare <= 0 || price <= 0)
                return false;
            int passPrice = SkiMoneyRules.PassPrice(PassMultiplier, price);
            if (passPrice <= 0)
                return false;
            int season = SkiMoneyRules.SeasonKeyOfDate(m_TimeSystem.year, m_TimeSystem.normalizedDate);
            foreach (var pair in m_WatchGate)
            {
                if (pair.Value != SkiMoneyRules.TicketGate.Open)
                    continue;
                Entity sign = pair.Key;
                if (m_SaleSeasonOf.TryGetValue(sign, out int done) && done == season)
                    continue;
                long t0 = Stopwatch.GetTimestamp();
                PresaleCopy copy = m_SkiDay.TryCopyPresaleInputs(sign, m_SaleTargets, out SkiDayPresaleInputs inputs);
                if (copy == PresaleCopy.NotReady)
                    continue;   // the ski-day system has not taken this sign yet: retry next tick
                m_SaleSeasonOf[sign] = season;   // decided for this season either way
                if (copy == PresaleCopy.NoTarget)
                {
                    Mod.Log.Info($"Passes [season sale] ski area {Fmt(sign)} season {season}: no sale — the area has no target lodge " +
                                 "(a passing lodge with a road); first arrivals buy passes as before");
                    continue;
                }
                ScheduleSale(sign, season, passPrice, price, inputs, t0);
                return true;
            }
            return false;
        }

        private void ScheduleSale(Entity sign, int season, int passPrice, int price, in SkiDayPresaleInputs inputs, long t0)
        {
            m_ResourcesLookup.Update(this);
            m_PlayerMoneyLookup.Update(this);
            m_PassLookup.Update(this);
            var transforms = GetComponentLookup<Game.Objects.Transform>(isReadOnly: true);
            var owned = GetBufferLookup<Game.Vehicles.OwnedVehicle>(isReadOnly: true);
            m_SaleWrites.Clear();
            m_SaleResult[0] = default;
            NativeList<ArchetypeChunk> chunks = m_SaleQuery.ToArchetypeChunkListAsync(Allocator.TempJob, out JobHandle chunksHandle);
            NativeQueue<ServiceFeeSystem.FeeEvent> feeQueue = m_ServiceFeeSystem.GetFeeQueue(out JobHandle feeDeps);
            var job = new SaleJob
            {
                Chunks = chunks,
                EntityType = GetEntityTypeHandle(),
                HouseholdType = GetComponentTypeHandle<Household>(isReadOnly: true),
                RenterType = GetComponentTypeHandle<PropertyRenter>(isReadOnly: true),
                Passes = m_PassLookup,
                Transforms = transforms,
                OwnedVehicles = owned,
                HouseholdResources = m_ResourcesLookup,
                PlayerMoneys = m_PlayerMoneyLookup,
                FeeQueue = feeQueue,
                BookAsFees = (byte)(BookAsFees ? 1 : 0),
                City = m_CitySystem.City,
                Targets = m_SaleTargets.AsArray(),
                Writes = m_SaleWrites,
                Result = m_SaleResult,
                Inputs = inputs,
                SeasonKey = season,
                Price = price,
                PassPrice = passPrice,
                PassShare = PassShare,
            };
            JobHandle handle = job.Schedule(JobHandle.CombineDependencies(Dependency, chunksHandle, feeDeps));
            m_ServiceFeeSystem.AddQueueWriter(handle);
            chunks.Dispose(handle);
            Dependency = handle;
            m_SaleHandle = handle;
            m_SaleInFlight = true;
            m_SaleSign = sign;
            m_SaleSeasonKey = season;
            m_SalePassPrice = passPrice;
            m_SaleScheduleMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        }

        /// <summary>Step 1b (main thread): completes the sale job, writes a standing on every pool household (batch AddComponent for the
        /// households without a SkiSeasonPass, then SetComponentData for all), books the ledger (AddPasses preSeason), the totals and the
        /// `Passes [season sale]` line. <paramref name="quiet"/> (teardown) only counts: no ECS access, no line.</summary>
        private void ApplySale(bool quiet)
        {
            long tw = Stopwatch.GetTimestamp();
            m_SaleHandle.Complete();
            double waitMs = (Stopwatch.GetTimestamp() - tw) * 1000.0 / Stopwatch.Frequency;
            m_SaleInFlight = false;
            SaleResult r = m_SaleResult[0];
            long ta = Stopwatch.GetTimestamp();
            int declined = 0;
            for (int i = 0; i < m_SaleWrites.Length; i++)
            {
                PassWrite w = m_SaleWrites[i];
                if (w.Flags != 0 && w.CannotPay == 0)
                    declined++;
            }
            m_Counter.Ledger.AddPasses(Ref(m_SaleSign), r.Bought, r.Income, 0, m_SaleSeasonKey, preSeason: true);
            m_FeeBookedTotal += r.Booked;
            m_PassesSold += r.Bought;
            m_PassIncomeTotal += r.Income;
            m_PassesDeclined += declined;
            m_PassesCannotPay += r.CannotPay;
            m_PassSeasonKey = m_SaleSeasonKey;
            m_SalesRun++;
            m_SalePoolTotal += r.Pool;
            m_SaleBoughtTotal += r.Bought;
            m_SaleCannotPayTotal += r.CannotPay;
            m_SaleIncomeTotal += r.Income;
            if (quiet)
            {
                m_SaleWrites.Clear();
                return;
            }
            var add = new NativeList<Entity>(m_SaleWrites.Length, Allocator.Temp);
            for (int i = 0; i < m_SaleWrites.Length; i++)
            {
                Entity hh = m_SaleWrites[i].Household;
                if (EntityManager.Exists(hh) && !EntityManager.HasComponent<SkiSeasonPass>(hh))
                    add.Add(hh);
            }
            if (add.Length > 0)
                EntityManager.AddComponent(add.AsArray(), ComponentType.ReadWrite<SkiSeasonPass>());
            add.Dispose();
            for (int i = 0; i < m_SaleWrites.Length; i++)
            {
                PassWrite w = m_SaleWrites[i];
                if (EntityManager.Exists(w.Household) && EntityManager.HasComponent<SkiSeasonPass>(w.Household))
                    EntityManager.SetComponentData(w.Household, new SkiSeasonPass(w.SeasonKey, w.PricePaid, w.Flags));
            }
            m_SaleWrites.Clear();
            double applyMs = (Stopwatch.GetTimestamp() - ta) * 1000.0 / Stopwatch.Frequency;
            m_SaleLastScheduleMs = m_SaleScheduleMs;
            m_SaleLastApplyMs = applyMs;
            m_SaleLastWaitMs = waitMs;
            string meanP = r.Pool > 0 ? (r.ChanceSum / r.Pool).ToString("P1", System.Globalization.CultureInfo.InvariantCulture) : "n/a";
            Mod.Log.Info($"Passes [season sale] ski area {Fmt(m_SaleSign)} season {m_SaleSeasonKey}: pool {r.Pool} resident household(s) within reach " +
                         $"with no standing, offered {r.Offered} (rolled in at {PassShare} % × their ski-day chance as pass holders, mean p {meanP}), " +
                         $"bought {r.Bought} +{r.Income} ({PassMultiplier} × the lift ticket {Price} = {m_SalePassPrice} each, " +
                         $"{(BookAsFees ? "booked as public transportation fees" : "added directly")}), could not pay {r.CannotPay}, rolled out {declined}; " +
                         $"cost: scheduled in {m_SaleScheduleMs:F2} ms, applied in {applyMs:F2} ms, waited {waitMs:F2} ms for the job " +
                         "(once per ski area per season)");
        }


        /// <summary>One pass over the resident households (m_SaleQuery's chunks): the pool test, the household's ski-day chance as a pass
        /// holder, SkiMoneyRules.PresaleDecision, and for a buyer the debit + booking like TicketJob. Writes one PassWrite per pool
        /// household and the totals; nothing structural.</summary>
        [BurstCompile]
        private struct SaleJob : IJob
        {
            [ReadOnly] public NativeList<ArchetypeChunk> Chunks;
            [ReadOnly] public EntityTypeHandle EntityType;
            [ReadOnly] public ComponentTypeHandle<Household> HouseholdType;
            [ReadOnly] public ComponentTypeHandle<PropertyRenter> RenterType;
            [ReadOnly] public ComponentLookup<SkiSeasonPass> Passes;
            [ReadOnly] public ComponentLookup<Game.Objects.Transform> Transforms;
            [ReadOnly] public BufferLookup<Game.Vehicles.OwnedVehicle> OwnedVehicles;
            public BufferLookup<GameResources> HouseholdResources;
            public ComponentLookup<PlayerMoney> PlayerMoneys;
            public NativeQueue<ServiceFeeSystem.FeeEvent> FeeQueue;
            public byte BookAsFees;
            public Entity City;
            [ReadOnly] public NativeArray<float2> Targets;
            public NativeList<PassWrite> Writes;
            public NativeArray<SaleResult> Result;
            public SkiDayPresaleInputs Inputs;
            public int SeasonKey, Price, PassPrice, PassShare;

            public void Execute()
            {
                SaleResult res = default;
                bool cityOk = City != Entity.Null && PlayerMoneys.HasComponent(City);
                long credited = 0;
                for (int c = 0; c < Chunks.Length; c++)
                {
                    ArchetypeChunk chunk = Chunks[c];
                    NativeArray<Entity> entities = chunk.GetNativeArray(EntityType);
                    NativeArray<Household> households = chunk.GetNativeArray(ref HouseholdType);
                    NativeArray<PropertyRenter> renters = chunk.GetNativeArray(ref RenterType);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        Entity hh = entities[i];
                        if ((households[i].m_Flags & HouseholdFlags.Commuter) != 0)
                            continue;
                        if (Passes.TryGetComponent(hh, out SkiSeasonPass have) &&
                            SkiMoneyRules.PassStandingOf(true, have.m_SeasonKey, have.m_Flags, SeasonKey) != SkiMoneyRules.PassStanding.None)
                            continue;   // decided this season already (holder or declined)
                        if (!Transforms.TryGetComponent(renters[i].m_Property, out Game.Objects.Transform home))
                            continue;
                        float2 origin = home.m_Position.xz;
                        float d = float.PositiveInfinity;
                        for (int k = 0; k < Targets.Length; k++)
                            d = math.min(d, SkiDayRules.DistanceXZ(origin, Targets[k]));
                        if (!SkiDayRules.WithinReach(d, Inputs.Knobs.ReachKm))
                            continue;   // not in this area's pool
                        res.Pool++;
                        bool hasCar = OwnedVehicles.TryGetBuffer(hh, out DynamicBuffer<Game.Vehicles.OwnedVehicle> cars) && cars.Length > 0;
                        SkiDayRules.TravelClass cls = SkiDayRules.ClassOf(d, false, hasCar, hasCar);
                        double conf = cls == SkiDayRules.TravelClass.Walk ? Inputs.ConfidenceWalk
                                    : cls == SkiDayRules.TravelClass.Car ? Inputs.ConfidenceCar : Inputs.ConfidenceNoCar;
                        double chance = SkiDayRules.Chance(SkiDayRules.GuestKind.Resident, Inputs.Attraction, d / 1000.0, 1.0, 0.0, Price,
                                                           Inputs.PriceTolerance, Inputs.SchoolFactor, conf, Inputs.Knobs, true);
                        double p = SkiMoneyRules.PresaleProbability(PassShare, chance);
                        res.ChanceSum += p;
                        int money = 0;
                        DynamicBuffer<GameResources> buffer = default;
                        bool hasBuffer = HouseholdResources.HasBuffer(hh);
                        if (hasBuffer)
                        {
                            buffer = HouseholdResources[hh];
                            money = EconomyUtils.GetResources(Resource.Money, buffer);
                        }
                        SkiMoneyRules.PassSaleResult sale = cityOk
                            ? SkiMoneyRules.PresaleDecision(hh.Index, SeasonKey, p, PassPrice, money)
                            : SkiMoneyRules.PassSaleResult.Declined;
                        if (sale == SkiMoneyRules.PassSaleResult.Bought && hasBuffer)
                        {
                            EconomyUtils.AddResources(Resource.Money, -PassPrice, buffer);   // PassPrice <= money: never below 0
                            res.Offered++;
                            res.Bought++;
                            res.Income += PassPrice;
                            if (BookAsFees != 0)
                            {
                                res.Booked += PassPrice;
                                FeeQueue.Enqueue(new ServiceFeeSystem.FeeEvent
                                {
                                    m_Amount = 1f,
                                    m_Cost = PassPrice,
                                    m_Resource = PlayerResource.PublicTransport,
                                    m_Outside = false,
                                });
                            }
                            else
                                credited += PassPrice;
                            Writes.Add(new PassWrite { Household = hh, SeasonKey = SeasonKey, PricePaid = PassPrice });
                        }
                        else
                        {
                            bool cannotPay = sale == SkiMoneyRules.PassSaleResult.CannotPay || (sale == SkiMoneyRules.PassSaleResult.Bought && !hasBuffer);
                            if (cannotPay)
                            {
                                res.Offered++;
                                res.CannotPay++;
                            }
                            Writes.Add(new PassWrite
                            {
                                Household = hh,
                                SeasonKey = SeasonKey,
                                Flags = SkiMoneyRules.PassFlagDeclined,
                                CannotPay = (byte)(cannotPay ? 1 : 0),
                            });
                        }
                    }
                }
                if (credited > 0)
                {
                    PlayerMoney money = PlayerMoneys[City];
                    money.Add((int)math.min(credited, (long)int.MaxValue));
                    PlayerMoneys[City] = money;
                }
                Result[0] = res;
            }
        }
    }
}
