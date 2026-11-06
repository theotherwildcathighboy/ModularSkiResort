// SkiMergeSystem - merge an existing building into a ski area as a lodge (MergeRules header; research merge-asset-into-resort-2026-11-02.md).
// The actions (merge, tier change, unmerge) come from the hub's merge card on the main thread. A merge charges the one-time fee at once
// (MergeRules.MergeFee, never refunded), tags the building (SkiMergedLodge, a new serial) and adds an AttractivenessProvider only when the
// prefab has none (FlagAddedProvider: unmerge removes only that one). The building keeps its prefab, renters, jobs and own provider.
// The monthly fee (MergeRules.MonthlyFee) accrues per elapsed frame while the building is counted and switched on (MergeRules.Billed), the
// SkiRunUpkeepSystem pattern: PlayerMoney.Subtract on the City entity, fractional carry, the meter stops while paused. No ledger line: like our
// own lodges' upkeep it is a city cost; the hub's BuildingsCost shows the rate.
// The duplicate guard: two buildings with one serial (a copy that kept the component) - the later entity loses its tag (MergeRules.IsDuplicateLoser).
// Scheduling: GameSimulation, Game mode only; the billing and the guard run every ChargeIntervalFrames.
// Engine notes: EngineNotes/SkiMergeSystem.md

using System;
using System.Collections.Generic;
using System.Globalization;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
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
    public sealed partial class SkiMergeSystem : GameSystemBase
    {
        private const int ChargeIntervalFrames = 1024;

        /// <summary>The outcome of an action, for the card (bindings.ts MergeOutcomeName) and the log.</summary>
        internal enum Outcome : byte
        {
            None = 0,
            Merged = 1,
            TierChanged = 2,
            Unmerged = 3,
            Refused = 4,
            NoMoney = 5,
            TierTooBig = 6,
        }

        private SkiPrefabCatalogSystem m_PrefabCatalog;
        private SkiResortAttractivenessSystem m_Attractiveness;
        private SkiMergeOverlaySystem m_Overlay;
        private SimulationSystem m_Simulation;
        private TimeSystem m_Time;
        private CitySystem m_CitySystem;
        private EntityQuery m_MergedQuery;

        private bool m_Armed, m_Disabled, m_ErrorLogged;
        private bool m_ChargeFrameValid;
        private uint m_LastChargeFrame;
        private double m_Carry;
        private int m_HighestSerial;
        private bool m_SerialsKnown;
        private readonly List<SkiResortAttractivenessSystem.MergedView> m_Views = new List<SkiResortAttractivenessSystem.MergedView>(4);
        private readonly Dictionary<int, Entity> m_SerialOwner = new Dictionary<int, Entity>();

        /// <summary>The billed monthly rate of the last pass (the hub's BuildingsCost adds it).</summary>
        internal int MonthlyRate { get; private set; }

        /// <summary>Debited this load (DUMP).</summary>
        internal long DebitedThisLoad { get; private set; }

        /// <summary>The last action's outcome and building (the card shows it until another building is selected).</summary>
        internal Outcome LastOutcome { get; private set; }
        internal Entity LastOutcomeBuilding { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_Attractiveness = World.GetOrCreateSystemManaged<SkiResortAttractivenessSystem>();
            m_Overlay = World.GetOrCreateSystemManaged<SkiMergeOverlaySystem>();
            m_Simulation = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_Time = World.GetOrCreateSystemManaged<TimeSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_MergedQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<SkiMergedLodge>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            Mod.Log.Info($"{nameof(SkiMergeSystem)} created");
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 64;
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_Disabled = m_ErrorLogged = false;
            m_ChargeFrameValid = false;
            m_Carry = 0.0;
            m_SerialsKnown = false;
            m_HighestSerial = 0;
            MonthlyRate = 0;
            DebitedThisLoad = 0;
            LastOutcome = Outcome.None;
            LastOutcomeBuilding = Entity.Null;
            if (m_Armed)
                Mod.Log.Info($"Merge: ARMED - {m_MergedQuery.CalculateEntityCount()} merged building(s); monthly fee billed every " +
                             $"{ChargeIntervalFrames} sim frames by elapsed frames while counted and switched on; duplicate serials untagged (later entity)");
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !m_Armed)
                    return;
                uint frame = m_Simulation.frameIndex;
                if (!m_ChargeFrameValid)
                {
                    m_ChargeFrameValid = true;
                    m_LastChargeFrame = frame;
                    GuardSerials();
                    return;
                }
                uint elapsed = frame - m_LastChargeFrame;
                if (elapsed < ChargeIntervalFrames)
                    return;
                m_LastChargeFrame = frame;
                GuardSerials();
                Charge(elapsed);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                MonthlyRate = 0;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiMergeSystem)} EXCEPTION - merge billing disabled until the next game load. Please report this stack:\n{e}");
                }
            }
        }

        // Facts and fees

        /// <summary>MergeRules.Facts of <paramref name="building"/> from its components and its prefab's. <paramref name="forTag"/>: the
        /// re-check of an already merged building (the walk), where Destroyed is a state of its own and not a refusal.</summary>
        internal static MergeRules.Facts ReadFacts(EntityManager em, SkiPrefabCatalogSystem catalog, Entity building, bool forTag)
        {
            var f = new MergeRules.Facts();
            if (building == Entity.Null || !em.Exists(building) || !em.HasComponent<Building>(building) ||
                !em.TryGetComponent(building, out PrefabRef prefabRef) || !em.TryGetComponent(prefabRef.m_Prefab, out BuildingData data))
                return f;
            Entity prefab = prefabRef.m_Prefab;
            f.HasBuilding = true;
            f.LotCells = data.m_LotSize.x * data.m_LotSize.y;
            f.Ours = catalog.IsUpgradeBuilding(prefab) || catalog.IsSign(prefab) || catalog.IsStation(prefab);
            f.HasOwner = em.HasComponent<Owner>(building);
            f.Transient = em.HasComponent<Temp>(building) || em.HasComponent<Deleted>(building) ||
                          (!forTag && (em.HasComponent<Destroyed>(building) || em.HasComponent<Abandoned>(building) || em.HasComponent<Condemned>(building)));
            f.Signature = em.HasComponent<SignatureBuildingData>(prefab);
            f.Spawnable = em.TryGetComponent(prefab, out SpawnableBuildingData spawnable);
            if (f.Spawnable && spawnable.m_ZonePrefab != Entity.Null && em.TryGetComponent(spawnable.m_ZonePrefab, out ZoneData zone))
                f.IndustrialZone = zone.m_AreaType == Game.Zones.AreaType.Industrial && !zone.IsOffice();
            f.Company = em.HasComponent<Game.Buildings.ExtractorFacility>(building);
            f.Park = em.HasComponent<Game.Buildings.Park>(building);
            f.Service = em.HasComponent<ServiceObjectData>(prefab) || em.HasComponent<CityServiceUpkeep>(building);
            return f;
        }

        /// <summary>The prefab's own construction cost (PlaceableObjectData), 0 without.</summary>
        internal static int OwnConstructionCost(EntityManager em, Entity building)
        {
            if (!em.TryGetComponent(building, out PrefabRef pr) || !em.TryGetComponent(pr.m_Prefab, out PlaceableObjectData placeable))
                return 0;
            return (int)math.min(placeable.m_ConstructionCost, (uint)int.MaxValue);
        }

        /// <summary>The monthly fee of <paramref name="building"/> at <paramref name="tier"/> (MergeRules.MonthlyFee over its kind's
        /// city-billed upkeep); 0 for an unknown tier.</summary>
        internal int MonthlyFeeOf(Entity building, int tier)
        {
            if (tier < 0 || tier >= SkiCatalog.Lodges.Count)
                return 0;
            MergeRules.Kind kind = MergeRules.Classify(ReadFacts(EntityManager, m_PrefabCatalog, building, forTag: true), out _);
            int prefabUpkeep = EntityManager.TryGetComponent(building, out PrefabRef pr) && EntityManager.TryGetComponent(pr.m_Prefab, out ConsumptionData c)
                ? c.m_Upkeep : 0;
            return MergeRules.MonthlyFee(SkiCatalog.Lodges[tier].Upkeep, MergeRules.OwnCityUpkeep(kind, prefabUpkeep));
        }

        /// <summary>What a merge (or a tier change) to <paramref name="tier"/> charges now.</summary>
        internal int ChargeFor(Entity building, int tier)
        {
            if (tier < 0 || tier >= SkiCatalog.Lodges.Count)
                return 0;
            int fee = MergeRules.MergeFee(SkiCatalog.Lodges[tier].BuildCost, OwnConstructionCost(EntityManager, building));
            return EntityManager.TryGetComponent(building, out SkiMergedLodge tag) ? MergeRules.TierChangeCharge(fee, tag.m_FeePaid) : fee;
        }

        // Actions (main thread: the hub's triggers)

        /// <summary>Merge <paramref name="building"/> as <paramref name="tier"/>, or change a merged building's tier. Charges at once.</summary>
        internal Outcome Merge(Entity building, int tier)
        {
            Outcome outcome = MergeCore(building, tier, out string detail);
            LastOutcome = outcome;
            LastOutcomeBuilding = building;
            Mod.Log.Info($"Merge [{(outcome == Outcome.Merged || outcome == Outcome.TierChanged ? "merge" : "refused")}] building {Id(building)} " +
                         $"tier {TierName(tier)}: {outcome}{detail}");
            if (outcome == Outcome.Merged || outcome == Outcome.TierChanged)
            {
                m_Attractiveness.RequestMergedRecompute();
                m_Overlay.OnMerged();   // the merge overlay disarms after a merge (MergeRules.NextArmed)
            }
            return outcome;
        }

        private Outcome MergeCore(Entity building, int tier, out string detail)
        {
            detail = string.Empty;
            MergeRules.Facts facts = ReadFacts(EntityManager, m_PrefabCatalog, building, forTag: false);
            MergeRules.Kind kind = MergeRules.Classify(facts, out MergeRules.Refusal refusal);
            if (kind == MergeRules.Kind.None)
            {
                detail = $" ({refusal})";
                return Outcome.Refused;
            }
            if (tier < 0 || tier >= SkiCatalog.Lodges.Count || !MergeRules.TierAllowed(tier, facts.LotCells))
            {
                detail = $" (lot {facts.LotCells} cells, needs {MergeRules.MinCells(tier)})";
                return Outcome.TierTooBig;
            }
            bool tagged = EntityManager.TryGetComponent(building, out SkiMergedLodge tag);
            if (tagged && tag.m_Tier == tier)
                return Outcome.None;
            int fee = MergeRules.MergeFee(SkiCatalog.Lodges[tier].BuildCost, OwnConstructionCost(EntityManager, building));
            int charge = tagged ? MergeRules.TierChangeCharge(fee, tag.m_FeePaid) : fee;
            if (charge > 0 && !TryPay(charge))
            {
                detail = $" (needs {charge})";
                return Outcome.NoMoney;
            }
            if (tagged)
            {
                tag.m_Tier = (byte)tier;
                tag.m_Flags = (byte)(tag.m_Flags & ~SkiMergedLodge.FlagTierLowered);
                tag.m_FeePaid = math.max(tag.m_FeePaid, fee);
                EntityManager.SetComponentData(building, tag);
                detail = $", charged {charge} (paid so far {tag.m_FeePaid})";
                return Outcome.TierChanged;
            }
            EnsureSerials();
            m_HighestSerial = MergeRules.NextSerial(m_HighestSerial);
            byte flags = 0;
            if (!EntityManager.HasComponent<AttractivenessProvider>(building))
            {
                EntityManager.AddComponentData(building, new AttractivenessProvider());
                flags |= SkiMergedLodge.FlagAddedProvider;
            }
            EntityManager.AddComponentData(building, new SkiMergedLodge((byte)tier, flags, fee, m_HighestSerial));
            EntityManager.AddComponentData(building, new SkiMergedVanillaAttraction());
            detail = $" as {kind}, charged {fee}, serial {m_HighestSerial}{((flags & SkiMergedLodge.FlagAddedProvider) != 0 ? ", provider added" : string.Empty)}";
            return Outcome.Merged;
        }

        /// <summary>Unmerge: the tag and the runtime cache go; the provider only when the merge added it. No refund.</summary>
        internal Outcome Unmerge(Entity building)
        {
            if (!EntityManager.Exists(building) || !EntityManager.TryGetComponent(building, out SkiMergedLodge tag))
                return Outcome.None;
            Strip(building, tag);
            LastOutcome = Outcome.Unmerged;
            LastOutcomeBuilding = building;
            m_Overlay.RequestRebuild();   // the building may be an overlay candidate again
            Mod.Log.Info($"Merge [unmerge] building {Id(building)} tier {TierName(tag.m_Tier)} serial {tag.m_Serial} (fee paid {tag.m_FeePaid}, no refund)");
            m_Attractiveness.RequestMergedRecompute();
            return Outcome.Unmerged;
        }

        /// <summary>Delete all: every merged building goes back to plain vanilla (the building stays). Returns how many.</summary>
        internal int UnmergeAll()
        {
            int n = 0;
            using (var entities = m_MergedQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (!EntityManager.TryGetComponent(entities[i], out SkiMergedLodge tag))
                        continue;
                    Strip(entities[i], tag);
                    n++;
                }
            }
            if (n > 0)
            {
                Mod.Log.Info($"Merge [unmerge all]: {n} merged building(s) back to plain buildings (no refund)");
                m_Attractiveness.RequestMergedRecompute();
            }
            return n;
        }

        private void Strip(Entity building, in SkiMergedLodge tag)
        {
            if ((tag.m_Flags & SkiMergedLodge.FlagAddedProvider) != 0 && EntityManager.HasComponent<AttractivenessProvider>(building))
                EntityManager.RemoveComponent<AttractivenessProvider>(building);
            EntityManager.RemoveComponent<SkiMergedVanillaAttraction>(building);
            EntityManager.RemoveComponent<SkiMergedLodge>(building);
        }

        private bool TryPay(int amount)
        {
            Entity city = m_CitySystem.City;
            if (city == Entity.Null || !EntityManager.TryGetComponent(city, out PlayerMoney money))
                return false;
            // PlayerMoney.money: unlimited money reads 2e9 (Game.City/PlayerMoney.cs:15-24)
            if (money.money < amount)
                return false;
            money.Subtract(amount);
            EntityManager.SetComponentData(city, money);
            return true;
        }

        // Serials

        private void EnsureSerials()
        {
            if (!m_SerialsKnown)
                GuardSerials();
        }

        /// <summary>The highest serial, and the duplicate guard: of two buildings with one serial the later entity loses its tag (a copy that
        /// kept our component; Move It 0.5.16 has no clone, kept for any other copier). A serial of 0 (never given) gets a fresh one.</summary>
        private void GuardSerials()
        {
            m_SerialsKnown = true;
            m_SerialOwner.Clear();
            int highest = m_HighestSerial;
            using (var entities = m_MergedQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                    highest = math.max(highest, EntityManager.GetComponentData<SkiMergedLodge>(entities[i]).m_Serial);
                m_HighestSerial = highest;
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity e = entities[i];
                    SkiMergedLodge tag = EntityManager.GetComponentData<SkiMergedLodge>(e);
                    if (tag.m_Serial <= 0)
                    {
                        m_HighestSerial = MergeRules.NextSerial(m_HighestSerial);
                        tag.m_Serial = m_HighestSerial;
                        EntityManager.SetComponentData(e, tag);
                    }
                    LowerTierIfShrunk(e, ref tag);
                    if (!m_SerialOwner.TryGetValue(tag.m_Serial, out Entity keeper))
                    {
                        m_SerialOwner[tag.m_Serial] = e;
                        continue;
                    }
                    Entity loser = MergeRules.IsDuplicateLoser(LiftCensusSystem.Ref(e), LiftCensusSystem.Ref(keeper)) ? e : keeper;
                    if (loser == keeper)
                        m_SerialOwner[tag.m_Serial] = e;
                    SkiMergedLodge loserTag = EntityManager.GetComponentData<SkiMergedLodge>(loser);
                    Strip(loser, loserTag);
                    Mod.Log.Info($"Merge [duplicate] building {Id(loser)} carried serial {tag.m_Serial} of {Id(m_SerialOwner[tag.m_Serial])} " +
                                 "(a copy kept our component): untagged, the original stays merged");
                    m_Attractiveness.RequestMergedRecompute();
                }
            }
        }

        /// <summary>A lot that no longer fits the tier (a prefab swap, an asset update) lowers the tier to the best one below that fits
        /// (MergeRules.BestAllowed) and sets FlagTierLowered for the hub. No fit at all: left as it is (the walk lists it as AssetMissing).</summary>
        private void LowerTierIfShrunk(Entity building, ref SkiMergedLodge tag)
        {
            if (!EntityManager.TryGetComponent(building, out PrefabRef pr) || !EntityManager.TryGetComponent(pr.m_Prefab, out BuildingData data))
                return;
            int cells = data.m_LotSize.x * data.m_LotSize.y;
            if (MergeRules.TierAllowed(tag.m_Tier, cells))
                return;
            int best = MergeRules.BestAllowed(tag.m_Tier, cells, SkiCatalog.Lodges);
            if (best < 0)
                return;
            Mod.Log.Info($"Merge [tier] building {Id(building)}: lot {cells} cells no longer fits {TierName(tag.m_Tier)}; lowered to {TierName(best)} (no refund)");
            tag.m_Tier = (byte)best;
            tag.m_Flags |= SkiMergedLodge.FlagTierLowered;
            EntityManager.SetComponentData(building, tag);
            m_Attractiveness.RequestMergedRecompute();
        }

        // Billing

        private void Charge(uint elapsedFrames)
        {
            m_Attractiveness.CopyMergedViews(m_Views);
            int rate = 0;
            for (int i = 0; i < m_Views.Count; i++)
            {
                if (MergeRules.Billed(m_Views[i].State) && EntityManager.Exists(m_Views[i].Building))
                    rate += MonthlyFeeOf(m_Views[i].Building, m_Views[i].Tier);
            }
            MonthlyRate = rate;
            if (rate <= 0)
                return;
            double monthFrames = TimeSystem.kTicksPerDay * (double)math.max(1, m_Time.daysPerYear) / 12.0;
            m_Carry += rate * (elapsedFrames / monthFrames);
            int debit = (int)m_Carry;
            if (debit <= 0)
                return;
            Entity city = m_CitySystem.City;
            if (city == Entity.Null || !EntityManager.TryGetComponent(city, out PlayerMoney money))
                return;
            m_Carry -= debit;
            money.Subtract(debit);
            EntityManager.SetComponentData(city, money);
            DebitedThisLoad += debit;
        }

        private static string TierName(int tier) => tier >= 0 && tier < SkiCatalog.Lodges.Count ? SkiCatalog.Lodges[tier].Id : "?";

        private static string Id(Entity e) => e.Index.ToString(CultureInfo.InvariantCulture) + ":" + e.Version.ToString(CultureInfo.InvariantCulture);
    }
}
