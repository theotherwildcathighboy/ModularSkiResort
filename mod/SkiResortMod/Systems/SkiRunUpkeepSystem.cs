// SkiRunUpkeepSystem — run OPERATING COSTS, the economics leg of the design triangle
// (DECISIONS 67): snowmaking runs cost the most (guns + water + power), groomed runs a
// third of that (cat fuel), natural terrain is free. Without this, snowmaking strictly
// dominates groomed and both dominate "just ski the powder" — with it, season length
// versus money is a real decision, which is what makes the run types matter.
//
// WHAT IS CHARGED (the one-model rule): the billable base is the injection system's
// per-type ACTIVE mask — texels whose thermal gate is ON at their own elevation this
// top-up window (SkiSnowInjectionSystem.SnowmakingActiveTexels / GroomedActiveTexels ×
// TexelAreaM2). Guns bill while guns can run, groomers bill while grooming holds, and in
// summer everything is free because the gates are off. Visuals, attractiveness and money
// all read the same climate model and cannot disagree. Overlapping runs never
// double-bill (a texel is claimed once — see MaskStats). "Natural runs" are free by
// construction: no painted surface, no mask, no bill.
//
// WHERE THE MONEY GOES: Game.City.PlayerMoney on the CitySystem.City entity —
// public Add/Subtract, the component every vanilla income/expense ultimately mutates
// (ResourceProducerSystem pattern). ACCEPTED LIMITATION (DECISIONS 67): ExpenseSource is
// a closed enum aggregated inside CityServiceBudgetSystem, so the charge cannot appear
// as its own budget-panel line item without Harmony (banned, SPEC §4.1). The cost shows
// in the balance; the log carries the itemization.
//
// TIME MODEL: 262144 sim frames per in-game day (TimeSystem.kTicksPerDay, mirrored as a
// private const — unreadable at runtime) and daysPerYear from TimeSystem (settings-
// dependent); one calendar month = kTicksPerDay × daysPerYear / 12 frames. Charges
// accrue per elapsed frame between passes (wrap-safe uint math) with a fractional
// carry, so no cadence drift and no rounding loss on small resorts.
//
// SCHEDULING & PAUSE: GameSimulation phase (only runs while the sim advances, only in
// Game action mode — pause stops the meter, Editor never runs it), anchored after
// SkiSnowInjectionSystem so a same-frame top-up publishes its mask before we read it.
// Self-gated cadence like the injection system's.
//
// OBSERVABILITY + DEFENSIVE CONTRACT (house pattern): ARMED line with every rate at
// load, a charge line whenever the monthly rate bucket changes plus a periodic
// heartbeat, idle states logged once per reason transition, and the ChairPinSystem
// error pattern — never throw into the game loop, one-shot "!!!" with stack, disabled
// for the current load only, re-armed at OnGameLoadingComplete.

using System;
using Colossal.Serialization.Entities;
using Game;
using Game.City;
using Game.Simulation;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunUpkeepSystem : GameSystemBase
    {
        // ── Tuning — the single block, all values logged at activation ───────────────────
        private static class Tuning
        {
            // Monthly operating cost per hectare of ACTIVE run surface. First-cut
            // calibration anchors: the resort gate's Park maintenance pool is 8000 and
            // the day lodge's upkeep 3000/month; a mid-size resort (~40 ha of snowmaking)
            // should read as a serious municipal line item without bankrupting a town —
            // 40 ha × 500 = 20k/month while the guns run. Groomed at roughly a third
            // (fuel and labor, no water/power). FIELD-CALIBRATE before release.
            public const int SnowmakingCostPerHaMonth = 500;
            public const int GroomedCostPerHaMonth = 150;

            // Night skiing (night-skiing-run.md §1.2): 500 snowmaking (a night run IS a
            // snowmaking run) + 100 floodlight power (piste lighting is ~10 kW/ha for
            // ~5 h/night ≈ 1,500 kWh/ha/month, 15-20% of snowmaking's energy) + 200 for
            // the second staffed shift. The night premium (+300) is exactly twice the
            // whole groomed rate: "lights plus a night shift cost two groomers."
            public const int NightCostPerHaMonth = 800;

            // Charge cadence. The injection mask refreshes every 64 frames; billing at
            // 1024 (16 top-ups) keeps PlayerMoney writes rare while the elapsed-frame
            // accrual keeps the amount exact regardless of cadence.
            public const int ChargeIntervalFrames = 1024;

            // Heartbeat: unconditionally log every Nth charging pass even when the rate
            // bucket is stable (~32 × 1024 frames ≈ 4 charge lines per in-game day).
            public const int LogEveryNthCharge = 32;
        }

        // TimeSystem.kTicksPerDay mirror (private const in the decompile, TimeSystem.cs:18
        // — unreadable at runtime). A patch changing it mis-scales the rate, never
        // corrupts: the field-calibration session catches it.
        private const float TicksPerDay = 262144f;

        private SimulationSystem m_SimulationSystem;
        private SkiSnowInjectionSystem m_SnowInjectionSystem;
        private CitySystem m_CitySystem;
        private TimeSystem m_TimeSystem;

        // Per-load state — reset and re-armed at OnGameLoadingComplete.
        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;
        private bool m_CityWarned;
        private uint m_LastChargeFrame;
        private bool m_ChargeFrameValid;
        private float m_CarryFraction;          // sub-1-credit accrual between debits
        private long m_ChargeCounter;
        private int m_LastLoggedMonthlyRate;
        private string m_LastIdleReason;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_SnowInjectionSystem = World.GetOrCreateSystemManaged<SkiSnowInjectionSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_TimeSystem = World.GetOrCreateSystemManaged<TimeSystem>();
            Mod.Log.Info($"{nameof(SkiRunUpkeepSystem)} created");
        }

        /// <summary>Same 4-frame slot as the injection anchor chain; the real cadence is
        /// self-gated in OnUpdate off elapsed sim frames.</summary>
        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 4;
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_CityWarned = false;
            m_ChargeFrameValid = false;
            m_CarryFraction = 0f;
            m_ChargeCounter = 0;
            m_LastLoggedMonthlyRate = -1;
            m_LastIdleReason = null;

            if (mode != GameMode.Game)
                return;

            m_Armed = true;
            Mod.Log.Info("Upkeep: ARMED — snowmaking " +
                         $"{Tuning.SnowmakingCostPerHaMonth}/ha/month, groomed " +
                         $"{Tuning.GroomedCostPerHaMonth}/ha/month, night " +
                         $"{Tuning.NightCostPerHaMonth}/ha/month, billed against the " +
                         "injection system's ACTIVE thermal mask (gates off = free, summer " +
                         $"= free, natural terrain = free); charge pass every " +
                         $"{Tuning.ChargeIntervalFrames} sim frames, elapsed-frame accrual " +
                         "with fractional carry");
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !m_Armed)
                    return;

                uint frame = m_SimulationSystem.frameIndex;
                if (!m_ChargeFrameValid)
                {
                    // First tick after load anchors the meter; nothing owed for the
                    // pre-load past.
                    m_ChargeFrameValid = true;
                    m_LastChargeFrame = frame;
                    return;
                }
                uint elapsed = frame - m_LastChargeFrame; // wrap-safe
                if (elapsed < (uint)Tuning.ChargeIntervalFrames)
                    return;
                m_LastChargeFrame = frame;

                Charge(elapsed);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiRunUpkeepSystem)} EXCEPTION — run upkeep " +
                                  "disabled until the next game load. Please report this " +
                                  $"stack:\n{e}");
                }
            }
        }

        private void Charge(uint elapsedFrames)
        {
            if (!m_SnowInjectionSystem.HasMaskMeasurement)
            {
                SetIdle("no injection mask measured yet (injection idle or disabled)");
                return;
            }

            float texelHa = m_SnowInjectionSystem.TexelAreaM2 / 10000f;
            float snowmakingHa = m_SnowInjectionSystem.SnowmakingActiveTexels * texelHa;
            float groomedHa = m_SnowInjectionSystem.GroomedActiveTexels * texelHa;
            float nightHa = m_SnowInjectionSystem.NightActiveTexels * texelHa;
            float monthlyRate = snowmakingHa * Tuning.SnowmakingCostPerHaMonth
                              + groomedHa * Tuning.GroomedCostPerHaMonth
                              + nightHa * Tuning.NightCostPerHaMonth;
            if (monthlyRate <= 0f)
            {
                SetIdle("no active operations — all run gates off (too warm) or no runs placed");
                return;
            }

            float monthFrames = TicksPerDay * math.max(1, m_TimeSystem.daysPerYear) / 12f;
            m_CarryFraction += monthlyRate * (elapsedFrames / monthFrames);
            int debit = (int)m_CarryFraction;
            if (debit > 0)
            {
                Entity city = m_CitySystem.City;
                if (city == Entity.Null || !EntityManager.HasComponent<PlayerMoney>(city))
                {
                    if (!m_CityWarned)
                    {
                        m_CityWarned = true;
                        Mod.Log.Warn("Upkeep: City entity has no PlayerMoney — charges " +
                                     "accrue but cannot be billed (warned once per load)");
                    }
                    return;
                }
                m_CarryFraction -= debit;
                PlayerMoney money = EntityManager.GetComponentData<PlayerMoney>(city);
                money.Subtract(debit);
                EntityManager.SetComponentData(city, money);
            }

            m_ChargeCounter++;
            m_LastIdleReason = null;

            int rateBucket = (int)math.round(monthlyRate);
            bool log = rateBucket != m_LastLoggedMonthlyRate ||
                       m_ChargeCounter % Tuning.LogEveryNthCharge == 1;
            if (log)
            {
                m_LastLoggedMonthlyRate = rateBucket;
                Entity cityForLog = m_CitySystem.City;
                string balance = cityForLog != Entity.Null &&
                                 EntityManager.HasComponent<PlayerMoney>(cityForLog)
                    ? EntityManager.GetComponentData<PlayerMoney>(cityForLog).money.ToString()
                    : "n/a";
                Mod.Log.Info($"Upkeep [charge] -{debit} this pass (carry {m_CarryFraction:F2}) — " +
                             $"rate {rateBucket}/month = snowmaking {snowmakingHa:F1} ha × " +
                             $"{Tuning.SnowmakingCostPerHaMonth} + groomed {groomedHa:F1} ha × " +
                             $"{Tuning.GroomedCostPerHaMonth} + night {nightHa:F1} ha × " +
                             $"{Tuning.NightCostPerHaMonth}, balance {balance}");
            }
        }

        /// <summary>Idle states log once per reason transition (silence impossible, spam
        /// impossible). Accrual meter naturally reads zero while idle: the rate is zero.</summary>
        private void SetIdle(string reason)
        {
            if (m_LastIdleReason == reason)
                return;
            m_LastIdleReason = reason;
            m_LastLoggedMonthlyRate = -1; // next billable pass logs unconditionally
            Mod.Log.Info($"Upkeep [idle]: {reason} — nothing billed; re-evaluated every " +
                         $"{Tuning.ChargeIntervalFrames} sim frames");
        }
    }
}
