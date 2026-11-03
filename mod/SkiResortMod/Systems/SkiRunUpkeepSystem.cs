// SkiRunUpkeepSystem - run operating costs: snowmaking runs cost the most (guns + water + power), groomed runs a third of that (cat
// fuel), natural terrain is free, so season length versus money is a real decision.
// Billing: the injection system's per-type active mask (texels whose thermal gate is on at their own elevation), each run capped at
// its plan area (BilledHa, public and pure so the hub's rows read the same number); overlapping runs never double-bill. A cat track is in no
// mask: it bills its plan area at the snowmaking rate while its guns can run and its gun value is above Off (SnowGunPlumeRules.TrackBilledHa,
// SkiRunPropSystem.CatTrackGunsOn; DECISIONS 563). No other prop slider counts.
// Money goes to Game.City.PlayerMoney (no budget-panel line item without Harmony); every debit is also booked per ski area into SkiResortLedger.
// Charges accrue per elapsed frame between passes (wrap-safe uint math, fractional carry); the meter stops while paused.
// Scheduling: GameSimulation, Game action mode only, anchored after SkiSnowInjectionSystem so its same-frame top-up publishes first.
// Engine notes: EngineNotes/SkiRunUpkeepSystem.md

using System;
using System.Collections.Generic;
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
        private static class Tuning
        {
            // Monthly operating cost per hectare of active run surface. Anchors: the resort gate's Park maintenance pool is 8000 and
            // the day lodge's upkeep 3000/month; a mid-size resort (~40 ha of snowmaking) should read as a serious municipal line item
            // without bankrupting a town: 40 ha x 500 = 20k/month while the guns run. Groomed at roughly a third (fuel and labor, no
            // water/power). Needs field calibration before release.
            // The totals of the feature steps in SkiRunUpkeepRates (DECISIONS 576): groomed 150, snowmaking 150 + 350 = 500.
            public const int SnowmakingCostPerHaMonth = SkiRunUpkeepRates.GroomedPerHaMonth + SkiRunUpkeepRates.SnowmakingStepPerHaMonth;
            public const int GroomedCostPerHaMonth = SkiRunUpkeepRates.GroomedPerHaMonth;

            // Night skiing: 500 snowmaking (a night run is a snowmaking run) + 100 floodlight power (piste lighting is ~10 kW/ha for
            // ~5 h/night = ~1,500 kWh/ha/month, 15-20% of snowmaking's energy) + 200 for the second staffed shift. The night premium
            // (+300, the lights step) is exactly twice the whole groomed rate: "lights plus a night shift cost two groomers."
            public const int NightCostPerHaMonth = SnowmakingCostPerHaMonth + SkiRunUpkeepRates.LightsStepPerHaMonth;

            // Charge cadence. The injection mask refreshes every 64 frames; billing at 1024 (16 top-ups) keeps PlayerMoney writes rare
            // while the elapsed-frame accrual keeps the amount exact regardless of cadence.
            public const int ChargeIntervalFrames = 1024;

            // Heartbeat: unconditionally log every Nth charging pass even when the rate bucket is stable (~32 x 1024 frames = ~4 charge
            // lines per in-game day)
            public const int LogEveryNthCharge = 32;

            // At most one charge line per this many wall-clock seconds; the rest of a burst is folded into one summary
            public const float LogWindowS = 10f;
        }

        // TimeSystem.kTicksPerDay is `public const int kTicksPerDay = 262144` (Game.Simulation/TimeSystem.cs:18 in 1.6.0f1 and
        // 1.6.2f1), readable, so it is used directly. Being a const, the compiler copies the number into this DLL: a patch that
        // changed it would still need a rebuild of the mod to take effect.
        private const float TicksPerDay = TimeSystem.kTicksPerDay;

        /// <summary>The monthly operating cost per hectare of active run surface for a run kind - the numbers Charge bills
        /// (Tuning: groomed 150, snowmaking 500, night 800; a cat track follows the snowmaking rules). Unknown values read 0. A wrapper over
        /// SkiRunUpkeepRates.PerHaMonth (RunFeatures.FromKind: a cat track with its guns on).</summary>
        public static int CostPerHaMonth(RunKind kind) => SkiRunUpkeepRates.PerHaMonth(RunFeatures.FromKind((int)kind));

        /// <summary>The hectares a run is billed for: its active mask texels x the texel area in ha, never more than its plan area
        /// (m2, Geometry.m_SurfaceArea). Texels on the outline count whole, so the raw product reads a few percent over the run. A
        /// plan area that is not a finite positive number leaves the raw value (nothing to cap against). Pure.</summary>
        public static float BilledHa(int activeTexels, float texelHa, float planAreaM2)
        {
            float ha = activeTexels > 0 && texelHa > 0f && !float.IsInfinity(texelHa) ? activeTexels * texelHa : 0f;
            if (float.IsNaN(planAreaM2) || float.IsInfinity(planAreaM2) || planAreaM2 <= 0f)
                return ha;
            return Math.Min(ha, planAreaM2 / 10000f);
        }

        /// <summary>The monthly rate of the last charge pass (0 while idle or before the first pass of this load)</summary>
        public float LastMonthlyRate { get; private set; }

        /// <summary>The credits debited from the city since this load (0 at load). The guest counter takes month differences of it
        /// for the `Guests [month]` total line (whole city: no ski-area attribution exists). Memory only.</summary>
        internal long DebitedThisLoad { get; private set; }

        /// <summary>"" while billing; otherwise why nothing is billed (the idle reason of the last pass, "first charge pass pending"
        /// until the first pass of this load, or the disabled / not-armed state)</summary>
        public string IdleReason { get; private set; } = IdleNotArmed;

        private const string IdleNotArmed = "not armed (not in a city)";
        private const string IdleFirstPass = "first charge pass pending";
        private const string IdleDisabled = "disabled after an exception (see the !!! line)";

        private SimulationSystem m_SimulationSystem;
        private SkiSnowInjectionSystem m_SnowInjectionSystem;
        private SkiRunCatalogSystem m_Catalog;
        private SkiRunPropSystem m_RunProps;   // a cat track's gun value: Off bills nothing (CatTrackGunsOn)
        private CitySystem m_CitySystem;
        private TimeSystem m_TimeSystem;

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

        private readonly LogWindow m_ChargeLog = new LogWindow(Tuning.LogWindowS);

        // The split of each debit over the ski areas (engine notes, per-area booking); carries per sign, reset per load
        private SkiGuestCounterSystem m_Counter;
        private readonly List<Entity> m_SplitSigns = new List<Entity>(8);
        private readonly Dictionary<Entity, double> m_SplitCarryOf = new Dictionary<Entity, double>();
        private double m_SplitCarryNotLinked;
        private double[] m_SplitWeights = new double[8], m_SplitCarry = new double[8];
        private long[] m_SplitParts = new long[8];
        private int m_LastSplitAreas;
        private long m_LastSplitNotLinked;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_SnowInjectionSystem = World.GetOrCreateSystemManaged<SkiSnowInjectionSystem>();
            m_Catalog = World.GetOrCreateSystemManaged<SkiRunCatalogSystem>();
            m_RunProps = World.GetOrCreateSystemManaged<SkiRunPropSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_TimeSystem = World.GetOrCreateSystemManaged<TimeSystem>();
            Mod.Log.Info($"{nameof(SkiRunUpkeepSystem)} created");
        }

        /// <summary>Same 4-frame slot as the injection anchor chain; the real cadence is self-gated in OnUpdate off elapsed sim frames</summary>
        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 4;
        }

        /// <summary>A load (or leaving to the menu) writes the folded charge lines of the city being left. Never throws:
        /// GameSystemBase.GamePreload disables a system whose OnGamePreload throws (Game/GameSystemBase.cs:85-96; same in 1.6.2).</summary>
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

        /// <summary>Quit to desktop raises no onGamePreload, but the world's systems are destroyed while the logger still lives
        /// (LogWindow.cs header), so the folded charge lines are written here. Teardown must never throw: a failed write is dropped.</summary>
        protected override void OnDestroy()
        {
            try
            {
                if (m_ChargeLog.Count > 0)
                    WriteChargeSummary(UnityEngine.Time.realtimeSinceStartup);
            }
            catch (Exception)
            {
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
            m_ChargeLog.Reset();
            LastMonthlyRate = 0f;
            DebitedThisLoad = 0;
            m_SplitCarryOf.Clear();
            m_SplitCarryNotLinked = 0.0;
            m_LastSplitAreas = 0;
            m_LastSplitNotLinked = 0;
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
                         "= free, natural terrain = free), each run's hectares capped at its plan area; cat tracks by plan area at the " +
                         "snowmaking rate while their guns can run and are not set to Off; charge pass every " +
                         $"{Tuning.ChargeIntervalFrames} sim frames, elapsed-frame accrual " +
                         "with fractional carry; each debit split over the ski areas by their runs' billed rate (the hub's per-run rule) " +
                         "into the guest ledger; charge lines at most one per " +
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
                    // First tick after load anchors the meter; nothing owed for the pre-load past
                    m_ChargeFrameValid = true;
                    m_LastChargeFrame = frame;
                    return;
                }
                uint elapsed = frame - m_LastChargeFrame;
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
            float clippedHa = ClipToPlanAreas(texelHa, ref snowmakingHa, ref groomedHa, ref nightHa);
            float trackHa = CatTrackHa();
            float monthlyRate = snowmakingHa * Tuning.SnowmakingCostPerHaMonth
                              + groomedHa * Tuning.GroomedCostPerHaMonth
                              + nightHa * Tuning.NightCostPerHaMonth
                              + trackHa * Tuning.SnowmakingCostPerHaMonth;
            if (monthlyRate <= 0f)
            {
                SetIdle("no active operations — all run gates off (too warm) or no runs placed");
                return;
            }

            LastMonthlyRate = monthlyRate;
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
                DebitedThisLoad += debit;
                BookPerArea(debit, monthlyRate, texelHa);
            }

            m_ChargeCounter++;
            m_LastIdleReason = null;
            IdleReason = "";

            int rateBucket = (int)math.round(monthlyRate);
            bool log = rateBucket != m_LastLoggedMonthlyRate ||
                       m_ChargeCounter % Tuning.LogEveryNthCharge == 1;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (log)
            {
                // -1 = after a load or an idle spell, so this is the billing (re)start: forced, like the snow system's m_ForceNextTopupLog
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
                              $"{Tuning.NightCostPerHaMonth} + cat tracks {trackHa:F1} ha × " +
                              $"{Tuning.SnowmakingCostPerHaMonth}, balance {balance}" +
                              (clippedHa >= 0.05f ? $" (edge texels over the runs' plan areas not billed: {clippedHa:F1} ha)" : "") +
                              (debit > 0 ? $"; booked to {m_LastSplitAreas} ski area(s)" + (m_LastSplitNotLinked > 0 ? $" + Not linked {m_LastSplitNotLinked}" : "") : "");
                // The first line after a quiet window (or the forced restart) is written as before; the rest of a burst is folded into one summary
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
                WriteChargeSummary(now);
            }
        }

        /// <summary>Splits <paramref name="debit"/> over the ski areas by each bound run's billed rate and books the parts into the
        /// guest ledger (AddRunUpkeep). The parts sum exactly to the debit (SkiResortHistory.SplitDebit). Main thread; no allocation
        /// once the lists have grown to the area count.</summary>
        private void BookPerArea(int debit, float monthlyRate, float texelHa)
        {
            if (m_Counter == null)
                m_Counter = World.GetExistingSystemManaged<SkiGuestCounterSystem>();
            if (m_Counter == null || debit <= 0)
                return;
            // Weights: the stats system's per-run rule (SkiResortStatsSystem.CollectRuns): BilledHa x CostPerHaMonth(kind) for every
            // cataloged non-natural run with active texels, grouped by its gate; a cat track's plan area while its guns can run and are on.
            m_SplitSigns.Clear();
            double bound = 0.0, unbound = 0.0;
            int rows = m_Catalog.RowCount;
            for (int i = 0; i < rows; i++)
            {
                SkiRunCatalogSystem.RunRow row = m_Catalog.GetRow(i);
                if (row.Kind == (int)RunKind.Natural)
                    continue;
                float billedHa;
                if (row.Kind == (int)RunKind.CatTrack)
                {
                    billedHa = SnowGunPlumeRules.TrackBilledHa(row.AreaM2,
                        m_SnowInjectionSystem.CatTrackGunsCanRun(row.Entity, row.NodeBottomM, row.NodeTopM), m_RunProps.CatTrackGunsOn(row.Entity));
                }
                else
                {
                    if (!m_SnowInjectionSystem.TryGetRunActiveTexels(row.Entity, out int texels) || texels <= 0)
                        continue;
                    billedHa = BilledHa(texels, texelHa, row.AreaM2);
                }
                double w = (double)billedHa * CostPerHaMonth((RunKind)row.Kind);
                if (!(w > 0.0) || double.IsInfinity(w))
                    continue;
                if (row.Gate == Entity.Null)
                {
                    unbound += w;
                    continue;
                }
                int s = m_SplitSigns.IndexOf(row.Gate);
                if (s < 0)
                {
                    s = m_SplitSigns.Count;
                    m_SplitSigns.Add(row.Gate);
                    EnsureSplitCapacity(s + 2);
                    m_SplitWeights[s] = 0.0;
                }
                m_SplitWeights[s] += w;
                bound += w;
            }
            int areas = m_SplitSigns.Count;
            int slots = areas + 1;   // the last slot = Not linked: unbound runs + the part of the bill the catalog does not list
            EnsureSplitCapacity(slots);
            m_SplitWeights[areas] = unbound + Math.Max(0.0, monthlyRate - bound - unbound);
            for (int s = 0; s < areas; s++)
                m_SplitCarry[s] = m_SplitCarryOf.TryGetValue(m_SplitSigns[s], out double c) ? c : 0.0;
            m_SplitCarry[areas] = m_SplitCarryNotLinked;
            SkiResortHistory.SplitDebit(debit, m_SplitWeights, m_SplitCarry, m_SplitParts, slots, areas);
            SkiResortLedger ledger = m_Counter.Ledger;
            for (int s = 0; s < areas; s++)
            {
                m_SplitCarryOf[m_SplitSigns[s]] = m_SplitCarry[s];
                ledger.AddRunUpkeep(new EntityRef(m_SplitSigns[s].Index, m_SplitSigns[s].Version), m_SplitParts[s]);
            }
            m_SplitCarryNotLinked = m_SplitCarry[areas];
            ledger.AddRunUpkeep(EntityRef.Null, m_SplitParts[areas]);
            m_LastSplitAreas = areas;
            m_LastSplitNotLinked = m_SplitParts[areas];
        }

        private void EnsureSplitCapacity(int n)
        {
            if (m_SplitWeights.Length >= n)
                return;
            int size = Math.Max(n, m_SplitWeights.Length * 2);
            Array.Resize(ref m_SplitWeights, size);
            Array.Resize(ref m_SplitCarry, size);
            Array.Resize(ref m_SplitParts, size);
        }

        /// <summary>The cat tracks' billed hectares: each cataloged track's plan area while its guns can run and are not set to Off. Main
        /// thread; no allocation.</summary>
        private float CatTrackHa()
        {
            float ha = 0f;
            int rows = m_Catalog.RowCount;
            for (int i = 0; i < rows; i++)
            {
                SkiRunCatalogSystem.RunRow row = m_Catalog.GetRow(i);
                if (row.Kind != (int)RunKind.CatTrack)
                    continue;
                ha += SnowGunPlumeRules.TrackBilledHa(row.AreaM2,
                    m_SnowInjectionSystem.CatTrackGunsCanRun(row.Entity, row.NodeBottomM, row.NodeTopM), m_RunProps.CatTrackGunsOn(row.Entity));
            }
            return ha;
        }

        /// <summary>Takes each cataloged run's texel hectares above its plan area off its kind's total; returns the hectares taken
        /// off. Runs the catalog does not list, natural runs and runs without an entry in the per-run map are left as they are. Each
        /// total stays >= 0. Main thread; no allocation.</summary>
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

        /// <summary>One line for the folded charge lines: how many, over how long, the debit-per-pass and rate ranges, then the last
        /// one's full text. Only passes that would have logged are folded (a pass at an unchanged rate never was), so no sum.</summary>
        private void WriteChargeSummary(float now)
        {
            Mod.Log.Info($"Upkeep [charge] {m_ChargeLog.Count} more charge line(s) in {m_ChargeLog.SpanS(now):F0} s, folded " +
                         $"(one line per LogWindowS={Tuning.LogWindowS:0} s): debit {m_ChargeLog.Range(0, "F0")} per pass, " +
                         $"rate {m_ChargeLog.Range(1, "F0")}/month — last: " + m_ChargeLog.LastText);
            m_ChargeLog.Wrote(now);
        }

        /// <summary>Idle states log once per reason transition (silence impossible, spam impossible). The accrual meter reads zero
        /// while idle: the rate is zero.</summary>
        private void SetIdle(string reason)
        {
            LastMonthlyRate = 0f;
            IdleReason = reason;
            if (m_LastIdleReason == reason)
                return;
            m_LastIdleReason = reason;
            m_LastLoggedMonthlyRate = -1; // next billable pass logs unconditionally
            // The folded charge passes before the idle spell come first, so the log reads in order
            if (m_ChargeLog.Count > 0)
                WriteChargeSummary(UnityEngine.Time.realtimeSinceStartup);
            Mod.Log.Info($"Upkeep [idle]: {reason} — nothing billed; re-evaluated every " +
                         $"{Tuning.ChargeIntervalFrames} sim frames");
        }
    }
}
