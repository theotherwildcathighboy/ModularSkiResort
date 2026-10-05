// SkiRunUpkeepSystem — run OPERATING COSTS, the economics leg of the design triangle
// (DECISIONS 67): snowmaking runs cost the most (guns + water + power), groomed runs a
// third of that (cat fuel), natural terrain is free. Without this, snowmaking strictly
// dominates groomed and both dominate "just ski the powder" — with it, season length
// versus money is a real decision, which is what makes the run types matter.
//
// WHAT IS CHARGED (the one-model rule): the billable base is the injection system's
// per-type ACTIVE mask — texels whose thermal gate is ON at their own elevation this
// top-up window (SkiSnowInjectionSystem.SnowmakingActiveTexels / GroomedActiveTexels ×
// TexelAreaM2), each run capped at its plan area since F7 (header PLAN-AREA CAP below).
// Guns bill while guns can run, groomers bill while grooming holds, and in
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
// TIME MODEL: 262144 sim frames per in-game day (TimeSystem.kTicksPerDay — a PUBLIC const,
// Game.Simulation/TimeSystem.cs:18, same line in 1.6.2; corrected in U2: an earlier comment
// called it private and mirrored the literal) and daysPerYear from TimeSystem (settings-
// dependent); one calendar month = kTicksPerDay × daysPerYear / 12 frames. Charges
// accrue per elapsed frame between passes (wrap-safe uint math) with a fractional
// carry, so no cadence drift and no rounding loss on small resorts.
//
// SCHEDULING & PAUSE: GameSimulation phase (only runs while the sim advances, only in
// Game action mode — pause stops the meter, Editor never runs it), anchored after
// SkiSnowInjectionSystem so a same-frame top-up publishes its mask before we read it.
// Self-gated cadence like the injection system's.
//
// U2 ACCESSORS (docs/research/hub-u2-spec.md §2.1, §3.3 runCostMonth; 2026-10-03, implementer
// I-1): CostPerHaMonth(RunKind) — the three rates below by the shared RunKind (0 groomed / 1
// snowmaking / 2 night = SkiRunOwnershipSystem.KindOf), so the hub prices a run's texels with the
// SAME numbers this system bills; LastMonthlyRate — the rate of the last charge pass (0 while
// idle); IdleReason — "" while billing, else the current idle reason (the text of the last
// "Upkeep [idle]" line, or the pre-first-pass / disabled state). Plain field reads; the values
// freeze while the game is paused (GameSimulation phase, below).
//
// OBSERVABILITY + DEFENSIVE CONTRACT (house pattern): ARMED line with every rate at
// load, a charge line whenever the monthly rate bucket changes plus a periodic
// heartbeat, idle states logged once per reason transition, and the ChairPinSystem
// error pattern — never throw into the game loop, one-shot "!!!" with stack, disabled
// for the current load only, re-armed at OnGameLoadingComplete.
// FLOOD WINDOW (Round F6, item D3; log-audit A2: 126 charge lines in load 2 — the rate moves on
// almost every pass while snow builds, and a pass is ~4.3 s apart at 4×): the charge lines go
// through a LogWindow (LogWindow.cs) — at most one per Tuning.LogWindowS (10 s wall clock); the
// first after a quiet window is written as before, the rest are folded into one
// `Upkeep [charge] N more charge line(s) in T s, folded …` line (debit per pass and rate
// min-max, then the last pass's full line). At 1× a pass is ~17 s apart, so every changed pass
// still logs plain. A fold is flushed before an idle line, before a load (OnGamePreload) and at
// quit (OnDestroy; F6 fixer). F6 fixer (review): the first billable pass after a load or an idle
// spell is forced (written at once, with any fold), so a billing restart is never folded away.
//
// PLAN-AREA CAP (Round F7, item E2; docs/research/field-test-7-fixes.md, log-audit.md "Missed" 1): a run
// is never billed for more hectares than it has. The mask counts every texel the outline touches as a
// whole texel (~14 m cells, TexSize 1024 over the playable area), so edge texels over-read the run by
// about 3-4 % (field test 7: 26.5 ha "with snow" on a 25.54 ha run, 3.2 ha on a 3.05 ha run). Each pass
// therefore takes, per run of the catalog (SkiRunCatalogSystem rows: Entity, Kind, AreaM2 = the plan area
// Geometry.m_SurfaceArea) with an entry in the per-run map (SkiSnowInjectionSystem.TryGetRunActiveTexels,
// first-claim counts that sum exactly to the three totals), the part of its texel hectares above its plan
// area off its kind's total: BilledHa(texels, texelHa, plan) = min(texels × texelHa, plan / 10⁴). A run the
// catalog does not list yet (one UIUpdate rebuild behind) is billed uncapped for that pass, as before.
// BilledHa is public and pure so the hub's run rows (SkiResortStatsSystem.CollectRuns, its own owner;
// request in docs/research/field-test-7-notes-impl5.md) can read the same number as the bill. The charge
// line names the hectares clipped when there are any. Cost: one dictionary lookup per run per charge pass
// (1,024 frames).

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

            // F6 D3 (header FLOOD WINDOW): at most one charge line per this many wall-clock
            // seconds; the rest of a burst is folded into one summary (contract: 10 s).
            public const float LogWindowS = 10f;
        }

        // TimeSystem.kTicksPerDay is `public const int kTicksPerDay = 262144`
        // (Game.Simulation/TimeSystem.cs:18 in 1.6.0f1 and 1.6.2f1) — readable, so it is used
        // directly (U2 correction of the old "private const, unreadable at runtime" comment; the
        // value is unchanged). Being a const, the compiler copies the number into this DLL: a patch
        // that changed it would still need a rebuild of the mod to take effect.
        private const float TicksPerDay = TimeSystem.kTicksPerDay;

        /// <summary>U2 (hub-u2-spec.md §2.1): the monthly operating cost per hectare of ACTIVE run
        /// surface for a run kind — the numbers Charge bills (Tuning: groomed 150, snowmaking 500,
        /// night 800). Unknown values read 0.</summary>
        public static int CostPerHaMonth(RunKind kind)
        {
            switch (kind)
            {
                case RunKind.Groomed:
                    return Tuning.GroomedCostPerHaMonth;
                case RunKind.Snowmaking:
                    return Tuning.SnowmakingCostPerHaMonth;
                case RunKind.Night:
                    return Tuning.NightCostPerHaMonth;
                default:
                    return 0;
            }
        }

        /// <summary>F7 E2 (header PLAN-AREA CAP): the hectares a run is billed for — its active mask texels
        /// × the texel area in ha, never more than its plan area (m², Geometry.m_SurfaceArea). Texels on
        /// the outline count whole, so the raw product reads a few per cent over the run. A plan area that
        /// is not a finite positive number leaves the raw value (nothing to cap against). Pure.</summary>
        public static float BilledHa(int activeTexels, float texelHa, float planAreaM2)
        {
            float ha = activeTexels > 0 && texelHa > 0f && !float.IsInfinity(texelHa) ? activeTexels * texelHa : 0f;
            if (float.IsNaN(planAreaM2) || float.IsInfinity(planAreaM2) || planAreaM2 <= 0f)
                return ha;
            return Math.Min(ha, planAreaM2 / 10000f);
        }

        /// <summary>U2: the monthly rate of the last charge pass (0 while idle or before the first
        /// pass of this load).</summary>
        public float LastMonthlyRate { get; private set; }

        /// <summary>U2: "" while billing; otherwise why nothing is billed (the idle reason of the
        /// last pass, "first charge pass pending" until the first pass of this load, or the
        /// disabled / not-armed state).</summary>
        public string IdleReason { get; private set; } = IdleNotArmed;

        private const string IdleNotArmed = "not armed (not in a city)";
        private const string IdleFirstPass = "first charge pass pending";
        private const string IdleDisabled = "disabled after an exception (see the !!! line)";

        private SimulationSystem m_SimulationSystem;
        private SkiSnowInjectionSystem m_SnowInjectionSystem;
        private SkiRunCatalogSystem m_Catalog;      // F7 E2: the runs' plan areas (header PLAN-AREA CAP)
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

        // F6 D3 (header FLOOD WINDOW): folds the charge lines; reset per load, flushed before a load.
        private readonly LogWindow m_ChargeLog = new LogWindow(Tuning.LogWindowS);

        protected override void OnCreate()
        {
            base.OnCreate();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_SnowInjectionSystem = World.GetOrCreateSystemManaged<SkiSnowInjectionSystem>();
            m_Catalog = World.GetOrCreateSystemManaged<SkiRunCatalogSystem>();
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

        /// <summary>F6 D3 (header FLOOD WINDOW): a load (or leaving to the menu) writes the folded
        /// charge lines of the city being left. Never throws: GameSystemBase.GamePreload DISABLES a
        /// system whose OnGamePreload throws (Game/GameSystemBase.cs:85-96; same in 1.6.2).</summary>
        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            try
            {
                if (m_ChargeLog.Count > 0)
                    WriteChargeSummary(UnityEngine.Time.realtimeSinceStartup);
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Upkeep [charge]: folded charge lines could not be written before the load " +
                             $"({e.GetType().Name}: {e.Message}) — non-fatal");
            }
        }

        /// <summary>F6 fixer (header FLOOD WINDOW): quit to desktop raises no onGamePreload, but the
        /// world's systems are destroyed while the logger still lives (LogWindow.cs header), so the
        /// folded charge lines are written here. Teardown must never throw: a failed write is dropped.</summary>
        protected override void OnDestroy()
        {
            try
            {
                if (m_ChargeLog.Count > 0)
                    WriteChargeSummary(UnityEngine.Time.realtimeSinceStartup);
            }
            catch (Exception)
            {
                // nothing left to report to
            }
            base.OnDestroy();
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
            m_ChargeLog.Reset();   // F6 D3: flushed in OnGamePreload; nothing carries over
            LastMonthlyRate = 0f;
            IdleReason = IdleNotArmed;

            if (mode != GameMode.Game)
                return;

            m_Armed = true;
            IdleReason = IdleFirstPass;
            Mod.Log.Info("Upkeep: ARMED — snowmaking " +
                         $"{Tuning.SnowmakingCostPerHaMonth}/ha/month, groomed " +
                         $"{Tuning.GroomedCostPerHaMonth}/ha/month, night " +
                         $"{Tuning.NightCostPerHaMonth}/ha/month, billed against the " +
                         "injection system's ACTIVE thermal mask (gates off = free, summer " +
                         "= free, natural terrain = free), each run's hectares capped at its plan area; charge pass every " +
                         $"{Tuning.ChargeIntervalFrames} sim frames, elapsed-frame accrual " +
                         "with fractional carry; charge lines at most one per " +
                         $"LogWindowS={Tuning.LogWindowS:0} s wall clock (a burst is folded into one summary)");
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
                LastMonthlyRate = 0f;
                IdleReason = IdleDisabled;
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
            float clippedHa = ClipToPlanAreas(texelHa, ref snowmakingHa, ref groomedHa, ref nightHa);   // F7 E2
            float monthlyRate = snowmakingHa * Tuning.SnowmakingCostPerHaMonth
                              + groomedHa * Tuning.GroomedCostPerHaMonth
                              + nightHa * Tuning.NightCostPerHaMonth;
            if (monthlyRate <= 0f)
            {
                SetIdle("no active operations — all run gates off (too warm) or no runs placed");
                return;
            }

            LastMonthlyRate = monthlyRate;   // U2 accessor (the rate this pass accrues at)
            float monthFrames = TicksPerDay * math.max(1, m_TimeSystem.daysPerYear) / 12f;
            m_CarryFraction += monthlyRate * (elapsedFrames / monthFrames);
            int debit = (int)m_CarryFraction;
            if (debit > 0)
            {
                Entity city = m_CitySystem.City;
                if (city == Entity.Null || !EntityManager.HasComponent<PlayerMoney>(city))
                {
                    IdleReason = "the City entity has no PlayerMoney (charges accrue but are not billed)";
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
            IdleReason = "";   // U2: billing

            int rateBucket = (int)math.round(monthlyRate);
            bool log = rateBucket != m_LastLoggedMonthlyRate ||
                       m_ChargeCounter % Tuning.LogEveryNthCharge == 1;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (log)
            {
                // F6 fixer (header FLOOD WINDOW): -1 = after a load or an idle spell, so this is the
                // billing (re)start — forced, like the snow system's m_ForceNextTopupLog.
                bool force = m_LastLoggedMonthlyRate < 0;
                m_LastLoggedMonthlyRate = rateBucket;
                Entity cityForLog = m_CitySystem.City;
                string balance = cityForLog != Entity.Null &&
                                 EntityManager.HasComponent<PlayerMoney>(cityForLog)
                    ? EntityManager.GetComponentData<PlayerMoney>(cityForLog).money.ToString()
                    : "n/a";
                string body = $"-{debit} this pass (carry {m_CarryFraction:F2}) — " +
                              $"rate {rateBucket}/month = snowmaking {snowmakingHa:F1} ha × " +
                              $"{Tuning.SnowmakingCostPerHaMonth} + groomed {groomedHa:F1} ha × " +
                              $"{Tuning.GroomedCostPerHaMonth} + night {nightHa:F1} ha × " +
                              $"{Tuning.NightCostPerHaMonth}, balance {balance}" +
                              (clippedHa >= 0.05f ? $" (edge texels over the runs' plan areas not billed: {clippedHa:F1} ha)" : "");
                // F6 D3 (header FLOOD WINDOW): the first line after a quiet window (or the forced
                // restart) is written as before; the rest of a burst is folded into one summary.
                switch (m_ChargeLog.Offer(now, force, body, debit, rateBucket))
                {
                    case LogWindow.Verdict.Plain:
                        Mod.Log.Info("Upkeep [charge] " + body);
                        break;
                    case LogWindow.Verdict.Summary:
                        WriteChargeSummary(now);
                        break;
                }
            }
            else if (m_ChargeLog.TrailingDue(now))
            {
                WriteChargeSummary(now);   // the end of a burst: never silent
            }
        }

        /// <summary>F7 E2 (header PLAN-AREA CAP): takes each catalogued run's texel hectares above its plan
        /// area off its kind's total; returns the hectares taken off. Runs the catalog does not list, natural
        /// runs and runs without an entry in the per-run map are left as they are. Each total stays >= 0.
        /// Main thread; no allocation.</summary>
        private float ClipToPlanAreas(float texelHa, ref float snowmakingHa, ref float groomedHa, ref float nightHa)
        {
            if (!(texelHa > 0f))
                return 0f;
            float clipped = 0f;
            int rows = m_Catalog.RowCount;
            for (int i = 0; i < rows; i++)
            {
                SkiRunCatalogSystem.RunRow row = m_Catalog.GetRow(i);
                if (row.Kind == (int)RunKind.Natural)
                    continue;
                if (!m_SnowInjectionSystem.TryGetRunActiveTexels(row.Entity, out int texels) || texels <= 0)
                    continue;
                float excess = texels * texelHa - BilledHa(texels, texelHa, row.AreaM2);
                if (!(excess > 0f))
                    continue;
                switch (row.Kind)
                {
                    case (int)RunKind.Snowmaking:
                        excess = math.min(excess, snowmakingHa);
                        snowmakingHa -= excess;
                        break;
                    case (int)RunKind.Night:
                        excess = math.min(excess, nightHa);
                        nightHa -= excess;
                        break;
                    case (int)RunKind.Groomed:
                        excess = math.min(excess, groomedHa);
                        groomedHa -= excess;
                        break;
                    default:
                        continue;
                }
                clipped += excess;
            }
            return clipped;
        }

        /// <summary>F6 D3 (header FLOOD WINDOW): one line for the folded charge lines — how many, over
        /// how long, the debit-per-pass and rate ranges, then the last one's full text. Only passes
        /// that would have logged are folded (a pass at an unchanged rate never was), so no sum.</summary>
        private void WriteChargeSummary(float now)
        {
            Mod.Log.Info($"Upkeep [charge] {m_ChargeLog.Count} more charge line(s) in {m_ChargeLog.SpanS(now):F0} s, folded " +
                         $"(one line per LogWindowS={Tuning.LogWindowS:0} s): debit {m_ChargeLog.Range(0, "F0")} per pass, " +
                         $"rate {m_ChargeLog.Range(1, "F0")}/month — last: " + m_ChargeLog.LastText);
            m_ChargeLog.Wrote(now);
        }

        /// <summary>Idle states log once per reason transition (silence impossible, spam
        /// impossible). Accrual meter naturally reads zero while idle: the rate is zero.</summary>
        private void SetIdle(string reason)
        {
            LastMonthlyRate = 0f;   // U2 accessors: idle every pass this is called
            IdleReason = reason;
            if (m_LastIdleReason == reason)
                return;
            m_LastIdleReason = reason;
            m_LastLoggedMonthlyRate = -1; // next billable pass logs unconditionally
            // F6 D3: the folded charge passes before the idle spell come first, so the log reads in order.
            if (m_ChargeLog.Count > 0)
                WriteChargeSummary(UnityEngine.Time.realtimeSinceStartup);
            Mod.Log.Info($"Upkeep [idle]: {reason} — nothing billed; re-evaluated every " +
                         $"{Tuning.ChargeIntervalFrames} sim frames");
        }
    }
}
