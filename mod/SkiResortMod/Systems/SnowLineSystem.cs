// Shared climate oracle of the mod. Pull-based: the system stays Enabled = false and does no per-tick work; consumers
// (e.g. SkiResortAttractivenessSystem) call the accessors during their own recomputes, and ClimateSystem's public floats are
// cheap main-thread reads.
//
//   T_local(h) = T_ambient - (h - temperatureBaseHeight) x snowTemperatureHeightScale
//   snow line  : T_local = freezingTemperature  =>  h_snow = temperatureBaseHeight + (T_ambient - freezing) / scale
//
// Checked against visible snow lines in game (lapse 0.01 C/m, freezing 0 C). Anchors: summer ambient ~8.8 C gives a snow
// line near 1470 m; winter ~-5 C gives ~100 m.
//
//   * GetAmbientTemperature samples the climate curve at the current date (ClimateSystem.SampleClimate(float),
//     ClimateSystem.cs:625-654, overrides applied) instead of reading ClimateSystem.temperature, which is written only in
//     OnUpdate (:465-472): 0.0 before the first simulation tick of a session, the previous city's value on later loads. Inside
//     GameSimulation the two are equal bit for bit (ClimateSystem runs earlier in the same frame, SystemOrder.cs:358-360).
//   * GetDayMeanTemperature: mean of 24 hourly SampleClimate samples ending at the start of the current game hour, cached per
//     (game hour, daysPerYear, climate, temperature override); at most one recompute per game hour (SimulationSystem.cs:238,
//     :255).
//   * GetSnowmakingMarginC is the one snowmaking margin of the mod; IsSnowGatingEnabled the one read of "Snow & season gating".
//   * The climate calibration line is written on every Game or Editor load with the cost of 100 SampleClimate reads; the first
//     day-mean of a load writes one `Climate [day-mean]` line.

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
        /// <summary>Ambient fallback when climate is unavailable (menu, early load): a mild shoulder-season default that keeps
        /// consumers in the summer-floor regime.</summary>
        private const float FallbackAmbientC = 10f;

        private ClimateSystem m_ClimateSystem;
        private TimeSystem m_TimeSystem;
        private Func<float, float> m_TemperatureAt; // SampleClimate(t).temperature, allocated once

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
            Enabled = false;
            Mod.Log.Info($"{nameof(SnowLineSystem)} created (climate oracle, pull-based)");
        }

        /// <summary>Ambient temperature (C) at the reference elevation now: the climate curve sampled at the current date, overrides
        /// applied. See the header for why it does not read ClimateSystem.temperature.</summary>
        public float GetAmbientTemperature()
        {
            if (m_ClimateSystem == null || m_ClimateSystem.currentClimate == Entity.Null)
                return FallbackAmbientC; // SampleClimate would return 0 here (ClimateSystem.cs:653)
            return m_ClimateSystem.SampleClimate((float)m_ClimateSystem.currentDate).temperature;
        }

        /// <summary>The game's freezing threshold (0 C on all observed maps).</summary>
        public float GetFreezingTemperature()
        {
            return m_ClimateSystem != null ? m_ClimateSystem.freezingTemperature : 0f;
        }

        /// <summary>Local temperature at a world-space elevation, via the lapse.</summary>
        public float GetLocalTemperature(float elevation)
        {
            if (m_ClimateSystem == null)
                return FallbackAmbientC;
            return GetAmbientTemperature() -
                   (elevation - m_ClimateSystem.temperatureBaseHeight) * m_ClimateSystem.snowTemperatureHeightScale;
        }

        /// <summary>The lapse as its three raw coefficients, for Burst jobs that cannot call back into a managed system. Consumers must
        /// evaluate the exact expression <see cref="GetLocalTemperature"/> uses, `ambient - (elevation - baseHeight) * scale`, so
        /// job-side routing stays bit-identical to the managed oracle (pre-folding into two coefficients shifts threshold-adjacent
        /// texels by a float ulp). The null-climate fallback (ambient = FallbackAmbientC, base = 0, scale = 0) reproduces the old
        /// constant at every elevation.</summary>
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

        /// <summary>Elevation of the current snow line (T_local == freezing); terrain above it holds natural snow. float.MaxValue
        /// when the lapse is degenerate (no snow anywhere).</summary>
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

        /// <summary>Mean temperature (C) at the reference elevation over the 24 game hours up to the start of the current game hour
        /// (hourly SampleClimate samples; equals the override under a temperature override). Cached; recomputed at most once per game
        /// hour. FallbackAmbientC without a climate.</summary>
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
                Mod.Log.Info($"Climate [day-mean] {m_DayMean:F1} C over the 24 game hours before hour {hourKey} " +
                             $"of {24 * (days < 1 ? 1 : days)} (first this load, {us:F0} us)");
            }
            return m_DayMean;
        }

        /// <summary>The snowmaking margin (C above freezing at which snow guns still work), read by the injection (snowmaking and night
        /// gates), the estimate (RunSnowRules.ThresholdC) and therefore the bill (the upkeep bills the injection's mask). Clamped to
        /// the setting's range; RunSnowRules.DefaultSnowmakingMarginC when settings are not loaded.</summary>
        public float GetSnowmakingMarginC()
        {
            SkiResortSettings s = Mod.Settings;
            return RunSnowRules.ClampMargin(s != null ? s.SnowmakingBonusCelsius : RunSnowRules.DefaultSnowmakingMarginC);
        }

        /// <summary>True when "Snow &amp; season gating" is on, or settings are not loaded; the only read of the setting.</summary>
        public bool IsSnowGatingEnabled()
        {
            SkiResortSettings s = Mod.Settings;
            return s == null || s.EnableSnowGating;
        }

        /// <summary>ClimateSystem.snowTemperatureHeightScale (C per m), 0 without a climate.</summary>
        public float GetLapsePerM()
        {
            return m_ClimateSystem != null ? m_ClimateSystem.snowTemperatureHeightScale : 0f;
        }

        /// <summary>ClimateSystem.temperatureBaseHeight (m), 0 without a climate.</summary>
        public float GetBaseHeight()
        {
            return m_ClimateSystem != null ? m_ClimateSystem.temperatureBaseHeight : 0f;
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            // Day-mean cache and its first-of-load line belong to the city just loaded
            m_DayMeanValid = false;
            m_DayMeanLoggedThisLoad = false;

            // Calibration log, once per load. Editor counts: its climate panel can force seasons, which is how the formula was validated.
            if ((mode != GameMode.Game && mode != GameMode.Editor) || m_ClimateSystem == null)
                return;

            try
            {
                // Cost record: 100 reads, timed. The day-mean is computed first so its first-of-load line precedes this one.
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
