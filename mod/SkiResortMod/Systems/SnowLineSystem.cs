// SnowLineSystem — snow/season model for the resort (SPEC §6.6/§6.7).
//
// STATUS: promoted from log-only stub to the mod's shared climate oracle. The Phase 2
// validation happened: the formula below was checked against visible snow lines in game
// (lapse 0.01 °C/m via snowTemperatureHeightScale, freezing at 0 °C, reference elevation
// temperatureBaseHeight — logged values 596.44 / 0.01 / 0 on the A-Basin test map):
//
//   T_local(h)  = T_ambient − (h − temperatureBaseHeight) × snowTemperatureHeightScale
//   snowLine(h) : T_local = freezingTemperature
//               ⇒ h_snow = temperatureBaseHeight + (T_ambient − freezing) / scale
//
// Sanity anchors from the validated numbers: summer ambient ~8.8 °C → snow line ~1470 m
// (high alpine only); winter ambient ~−5 °C → snow line ~100 m (snow everywhere).
//
// The system stays Enabled = false — it does no per-tick work. Consumers PULL:
// SkiResortAttractivenessSystem calls the three accessors during its (event/periodic)
// recomputes; ClimateSystem's public floats are cheap main-thread reads. The climate log
// stays as the permanent calibration instrument in Player-adjacent logs.
//
// ROUND E1 (2026-10-04, stream S3; docs/research/econ-round/spec/ch3-snow.md §3, D13 / D14 / D31):
//   * D31 — GetAmbientTemperature samples the climate curve at the current date
//     (ClimateSystem.SampleClimate(float), ClimateSystem.cs:625-654, overrides applied) instead of
//     reading ClimateSystem.temperature, which is written only in its OnUpdate (:465-472): 0.0 before
//     the first simulation tick of a session and the previous city's value on later loads — the
//     "winter dress in June on the first load" bug. Inside GameSimulation the two are equal bit for
//     bit (ClimateSystem runs earlier in the same sim frame with the same date and overrides,
//     SystemOrder.cs:358-360), so the injection's thermal routing is unchanged; every read outside
//     GameSimulation before the first tick now sees the real temperature.
//   * D13 fallback — GetDayMeanTemperature: the mean of 24 hourly SampleClimate samples ending at the
//     start of the current game hour (RunSnowRules.DayMeanC), cached per (game hour, daysPerYear,
//     climate, temperature override); at most one recompute per game hour (~182 s of simulation at 1×
//     and ≥ 30 fps: 262,144 / 24 = 10,923 frames, SimulationSystem.cs:238, :255).
//   * D14 — GetSnowmakingMarginC is THE snowmaking margin of the mod (Options ▸ Snow "Snowmaking
//     margin (°C)", clamped 0..6, RunSnowRules.DefaultSnowmakingMarginC = 3.3 when settings are not loaded); IsSnowGatingEnabled is the one read
//     of "Snow & season gating". GetLapsePerM / GetBaseHeight expose the lapse for the estimates.
//   * The climate line is written on EVERY load in Game or Editor mode (was once per session), with
//     the D31 numbers and the cost of 100 SampleClimate reads; the first day-mean of a load writes one
//     `Climate [day-mean]` line with its cost (ch3 §10 L-C1, L-C2; §12).

using System;
using System.Diagnostics;
using Colossal.Serialization.Entities;
using Game;
using Game.Simulation;
using SkiResortMod.Settings;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class SnowLineSystem : GameSystemBase
    {
        /// <summary>Ambient fallback when climate is unavailable (menu, early load) —
        /// mild shoulder-season default, keeps consumers in the summer-floor regime.</summary>
        private const float FallbackAmbientC = 10f;

        private ClimateSystem m_ClimateSystem;
        private TimeSystem m_TimeSystem;
        private Func<float, float> m_TemperatureAt;   // SampleClimate(t).temperature, allocated once

        // E1 D13 fallback (ch3 §3.2): the day-mean cache. Second pass, e1-spec R7/R14: no generation
        // counter (no reader).
        private bool m_DayMeanValid;
        private int m_DayMeanHourKey, m_DayMeanDays;
        private Entity m_DayMeanClimate;
        private bool m_DayMeanOverride;
        private float m_DayMeanOverrideValue, m_DayMean;
        private bool m_DayMeanLoggedThisLoad;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ClimateSystem = World.GetOrCreateSystemManaged<ClimateSystem>();
            m_TimeSystem = World.GetOrCreateSystemManaged<TimeSystem>();
            m_TemperatureAt = t => m_ClimateSystem.SampleClimate(t).temperature;
            Enabled = false; // pull-based API; no per-tick work
            Mod.Log.Info($"{nameof(SnowLineSystem)} created (climate oracle, pull-based)");
        }

        // ─────────────────────────────────────────────────────────────────────────────────
        // Shared climate API (validated formula — see header)
        // ─────────────────────────────────────────────────────────────────────────────────

        /// <summary>Ambient temperature (°C) at the reference elevation NOW: the climate curve sampled at the
        /// current date, overrides applied (ClimateSystem.SampleClimate(float), ClimateSystem.cs:625-654).
        /// E1 D31: replaces the read of ClimateSystem.temperature, which is 0.0 before the first simulation
        /// tick of a session and the previous city's value on later loads (ClimateSystem.cs:465-472).
        /// Inside GameSimulation it equals the stored value bit for bit: ClimateSystem runs earlier in the
        /// same sim frame with the same date and overrides (SystemOrder.cs:358-360).</summary>
        public float GetAmbientTemperature()
        {
            if (m_ClimateSystem == null || m_ClimateSystem.currentClimate == Entity.Null)
                return FallbackAmbientC;   // SampleClimate would return 0 here (ClimateSystem.cs:653)
            return m_ClimateSystem.SampleClimate((float)m_ClimateSystem.currentDate).temperature;
        }

        /// <summary>The game's freezing threshold (0 °C on all observed maps).</summary>
        public float GetFreezingTemperature()
        {
            return m_ClimateSystem != null ? m_ClimateSystem.freezingTemperature : 0f;
        }

        /// <summary>Local temperature at a world-space elevation, via the validated lapse.</summary>
        public float GetLocalTemperature(float elevation)
        {
            if (m_ClimateSystem == null)
                return FallbackAmbientC;
            return GetAmbientTemperature() -
                   (elevation - m_ClimateSystem.temperatureBaseHeight) * m_ClimateSystem.snowTemperatureHeightScale;
        }

        /// <summary>The lapse as its three RAW coefficients, for Burst jobs that cannot
        /// call back into a managed system. Consumers MUST evaluate the exact expression
        /// <see cref="GetLocalTemperature"/> uses — `ambient - (elevation - baseHeight) *
        /// scale` — so job-side routing stays BIT-identical to the managed oracle (the
        /// entry-88 review caught that pre-folding these into two coefficients shifts
        /// threshold-adjacent texels by a float ulp). The null-climate fallback
        /// (ambient = FallbackAmbientC, base = 0, scale = 0) reproduces the old constant
        /// at every elevation.</summary>
        public void GetLocalTemperatureCoefficients(out float ambient, out float baseHeight,
            out float scale)
        {
            if (m_ClimateSystem == null)
            {
                ambient = FallbackAmbientC;
                baseHeight = 0f;
                scale = 0f;
                return;
            }
            ambient = GetAmbientTemperature();
            baseHeight = m_ClimateSystem.temperatureBaseHeight;
            scale = m_ClimateSystem.snowTemperatureHeightScale;
        }

        /// <summary>Elevation of the current snow line (T_local == freezing). Terrain above
        /// this holds natural snow. float.MaxValue when the lapse is degenerate (no snow
        /// anywhere); float.MinValue never occurs (a negative ambient just lowers the line).</summary>
        public float GetSnowLineElevation()
        {
            if (m_ClimateSystem == null)
                return float.MaxValue;
            float scale = m_ClimateSystem.snowTemperatureHeightScale;
            if (scale <= 0f)
                return float.MaxValue;
            return m_ClimateSystem.temperatureBaseHeight +
                   (GetAmbientTemperature() - m_ClimateSystem.freezingTemperature) / scale;
        }

        // ── Round E1 accessors (econ-round spec ch3 §3; e1-spec I-17, R10; stream S3). D31 rewrote
        // GetAmbientTemperature above. Main thread, pull-based like the rest of this API.

        /// <summary>E1 D13 fallback: mean temperature (°C) at the reference elevation over the 24 game hours
        /// up to the start of the current game hour (hourly SampleClimate samples, overrides applied — under a
        /// temperature override it equals the override). Cached per (game hour, daysPerYear, climate, override);
        /// recomputed at most once per game hour (~182 s of simulation at 1× and ≥ 30 fps: 262,144 / 24 = 10,923
        /// frames, SimulationSystem.cs:238, :255). FallbackAmbientC without a climate.</summary>
        public float GetDayMeanTemperature()
        {
            if (m_ClimateSystem == null || m_ClimateSystem.currentClimate == Entity.Null)
                return FallbackAmbientC;
            int days = m_TimeSystem != null ? m_TimeSystem.daysPerYear : 12;
            float aligned = RunSnowRules.HourAlignedDate((float)m_ClimateSystem.currentDate, days, out int hourKey);
            bool ov = m_ClimateSystem.temperature.overrideState;
            float ovValue = ov ? m_ClimateSystem.temperature.overrideValue : 0f;
            Entity climate = m_ClimateSystem.currentClimate;
            if (m_DayMeanValid && hourKey == m_DayMeanHourKey && days == m_DayMeanDays &&
                climate == m_DayMeanClimate && ov == m_DayMeanOverride && ovValue == m_DayMeanOverrideValue)
                return m_DayMean;
            long t0 = Stopwatch.GetTimestamp();
            m_DayMean = RunSnowRules.DayMeanC(m_TemperatureAt, aligned, days);
            double us = (Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency;
            m_DayMeanValid = true;
            m_DayMeanHourKey = hourKey;
            m_DayMeanDays = days;
            m_DayMeanClimate = climate;
            m_DayMeanOverride = ov;
            m_DayMeanOverrideValue = ovValue;
            if (!m_DayMeanLoggedThisLoad)
            {
                m_DayMeanLoggedThisLoad = true;
                // ch3 §10 L-C2 (once per load; the cost record of §12).
                Mod.Log.Info($"Climate [day-mean] {m_DayMean:F1} C over the 24 game hours before hour {hourKey} " +
                             $"of {24 * (days < 1 ? 1 : days)} (first this load, {us:F0} us)");
            }
            return m_DayMean;
        }

        /// <summary>E1 D14: THE snowmaking margin (°C above freezing at which snow guns still work), read by the
        /// injection (snowmaking and night gates), the estimate (RunSnowRules.ThresholdC) and therefore the bill
        /// (the upkeep bills the injection's mask). Options ▸ Snow "Snowmaking margin (°C)", clamped 0..6;
        /// 2 when settings are not loaded.</summary>
        public float GetSnowmakingMarginC()
        {
            SkiResortSettings s = Mod.Settings;
            return RunSnowRules.ClampMargin(s != null ? s.SnowmakingBonusCelsius : RunSnowRules.DefaultSnowmakingMarginC);
        }

        /// <summary>E1 D13: "Snow & season gating" (true when settings are not loaded) — the only read of
        /// the setting.</summary>
        public bool IsSnowGatingEnabled()
        {
            SkiResortSettings s = Mod.Settings;
            return s == null || s.EnableSnowGating;
        }

        /// <summary>E1: ClimateSystem.snowTemperatureHeightScale (°C per m), 0 without a climate.</summary>
        public float GetLapsePerM()
        {
            return m_ClimateSystem != null ? m_ClimateSystem.snowTemperatureHeightScale : 0f;
        }

        /// <summary>E1: ClimateSystem.temperatureBaseHeight (m), 0 without a climate.</summary>
        public float GetBaseHeight()
        {
            return m_ClimateSystem != null ? m_ClimateSystem.temperatureBaseHeight : 0f;
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            // E1 (ch3 §3.2): the day-mean cache and its first-of-load line belong to the city just loaded.
            m_DayMeanValid = false;
            m_DayMeanLoggedThisLoad = false;

            // Calibration log, once per LOAD since Round E1 (ch3 §3.4; was once per session) — Editor counts:
            // its climate panel can force seasons on demand, which is how the formula was validated.
            if ((mode != GameMode.Game && mode != GameMode.Editor) || m_ClimateSystem == null)
                return;

            try
            {
                // §12 cost record: 100 D31 reads, timed. The day-mean is computed first so its own L-C2
                // line (first of the load) precedes this one.
                long t0 = Stopwatch.GetTimestamp();
                float ambient = 0f;
                for (int i = 0; i < 100; i++)
                    ambient = GetAmbientTemperature();
                double us = (Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency;
                float dayMean = GetDayMeanTemperature();
                Mod.Log.Info(
                    "Climate constants: " +
                    $"temperatureBaseHeight={m_ClimateSystem.temperatureBaseHeight}, " +
                    $"snowTemperatureHeightScale={m_ClimateSystem.snowTemperatureHeightScale}, " +
                    $"freezingTemperature={m_ClimateSystem.freezingTemperature}, " +
                    $"ambient={ambient:F1} (SampleClimate at date {(float)m_ClimateSystem.currentDate:F4}; " +
                    $"ClimateSystem.temperature reads {(float)m_ClimateSystem.temperature:F1} until its first tick), " +
                    $"dayMean={dayMean:F1} (24 game hours), " +
                    $"snowmakingMargin={GetSnowmakingMarginC():F0} C, " +
                    $"snowLine={GetSnowLineElevation():F0} m, " +
                    $"currentSeason={m_ClimateSystem.currentSeasonName}, " +
                    $"isSnowing={m_ClimateSystem.isSnowing}; " +
                    $"SampleClimate x100 {us:F0} us");
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Climate logging failed (non-fatal): {e.Message}");
            }
        }

        protected override void OnUpdate()
        {
        }
    }
}
