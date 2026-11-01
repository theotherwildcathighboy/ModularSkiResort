// The coach bill of the Ski Bus Depots: every depot SkiDaySystem reads as working (SkiBusRules.IsBilled) bills
// SkiBusRules.MonthlyBill(straight-line distance to its area's nearest hill lodge) per month, accrued like the lift charge and debited from
// PlayerMoney, each depot's share booked in the ledger under its ski area (SkiResortLedger.AddBusCharge).
// The bill stands for the coach SkiCoachSystem shows, which reads the same SkiDaySystem.CopyDepots and adds no cost of its own.
// Phase: GameSimulation, UpdateAfter<SkiBusChargeSystem, SkiLiftChargeSystem> (Mod.cs; keeps the monthly debits together, not load-bearing);
// GetUpdateInterval 4 = the lift charge's slot (UpdateSystem.cs:400-431), the real cadence is self-gated. Pause stops it; the Editor never bills.
// Engine notes: EngineNotes/SkiBusChargeSystem.md

using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.City;
using Game.Simulation;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiBusChargeSystem : GameSystemBase
    {
        /// <summary>Charge pass cadence in elapsed sim frames (the lift charge's).</summary>
        private const int ChargeIntervalFrames = 1024;

        private SimulationSystem m_SimulationSystem;
        private CitySystem m_CitySystem;
        private TimeSystem m_TimeSystem;
        private SkiDaySystem m_SkiDays;
        private SkiGuestCounterSystem m_Counter;
        private readonly List<SkiDaySystem.DepotView> m_Depots = new List<SkiDaySystem.DepotView>(2);
        private readonly Dictionary<Entity, float> m_RateBySign = new Dictionary<Entity, float>(2);
        private bool m_Armed, m_Disabled, m_FrameValid, m_CityWarned;
        private uint m_LastFrame;
        private float m_Carry;
        private float m_LastLoggedRate = -1f;

        /// <summary>The coach bill per month of <paramref name="sign"/>'s working depots at the last charge pass (0 = none). Main thread.</summary>
        internal float MonthlyRateOf(Entity sign) => m_RateBySign.TryGetValue(sign, out float rate) ? rate : 0f;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_TimeSystem = World.GetOrCreateSystemManaged<TimeSystem>();
            m_SkiDays = World.GetOrCreateSystemManaged<SkiDaySystem>();
            m_Counter = World.GetOrCreateSystemManaged<SkiGuestCounterSystem>();
            Mod.Log.Info($"{nameof(SkiBusChargeSystem)} created");
        }

        /// <summary>The lift charge's 4-frame slot (engine notes: cadence).</summary>
        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 4;
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_Disabled = false;
            m_FrameValid = false;
            m_CityWarned = false;
            m_Carry = 0f;
            m_LastLoggedRate = -1f;
            m_LastFrame = 0;
            m_RateBySign.Clear();
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !m_Armed)
                    return;
                uint frame = m_SimulationSystem.frameIndex;
                if (!m_FrameValid)
                {
                    // The first tick after a load anchors the meter: nothing is owed for the pre-load past.
                    m_FrameValid = true;
                    m_LastFrame = frame;
                    return;
                }
                uint elapsed = frame - m_LastFrame; // wrap-safe
                if (elapsed < (uint)ChargeIntervalFrames)
                    return;
                m_LastFrame = frame;
                Charge(elapsed);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                Mod.Log.Error($"Bus [charge]: disabled for this load after {e.GetType().Name}: {e.Message}");
            }
        }

        private void Charge(uint elapsedFrames)
        {
            m_SkiDays.CopyDepots(m_Depots);
            m_RateBySign.Clear();
            float total = 0f;
            int billed = 0;
            int dpy = m_TimeSystem.daysPerYear;
            SkiResortLedger ledger = m_Counter.Ledger;
            for (int i = 0; i < m_Depots.Count; i++)
            {
                SkiDaySystem.DepotView depot = m_Depots[i];
                if (!SkiBusRules.IsBilled(depot.Verdict))
                    continue;
                float rate = SkiBusRules.MonthlyBill(depot.DistanceM);
                total += rate;
                billed++;
                m_RateBySign[depot.Sign] = MonthlyRateOf(depot.Sign) + rate;
                ledger?.AddBusCharge(LiftCensusSystem.Ref(depot.Sign), SkiMoneyRules.AccruedShare(rate, elapsedFrames, dpy));
            }
            if (total != m_LastLoggedRate && (m_LastLoggedRate >= 0f || billed > 0))
            {
                m_LastLoggedRate = total;
                Mod.Log.Info($"Bus [charge] {billed} depot(s) billed, {total:F0} a month in all ({SkiBusRules.BillFixedPerMonth:F0} + " +
                             $"{SkiBusRules.BillPerKmPerMonth:F0} per straight-line km to the area's nearest hill lodge, per depot); " +
                             $"{m_Depots.Count - billed} not working");
            }
            int debit = SkiMoneyRules.Accrue(ref m_Carry, total, elapsedFrames, dpy);
            if (debit <= 0)
                return;
            Entity city = m_CitySystem.City;
            if (city == Entity.Null || !EntityManager.HasComponent<PlayerMoney>(city))
            {
                m_Carry += debit; // the upkeep's rule: it keeps accruing, nothing is lost
                if (!m_CityWarned)
                {
                    m_CityWarned = true;
                    Mod.Log.Warn("Bus [charge]: City entity has no PlayerMoney: the coach bill accrues but cannot be billed (warned once per load)");
                }
                return;
            }
            PlayerMoney money = EntityManager.GetComponentData<PlayerMoney>(city);
            money.Subtract(debit);
            EntityManager.SetComponentData(city, money);
        }
    }
}
