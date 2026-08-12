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
// recomputes; ClimateSystem's public floats are cheap main-thread reads. The one-shot
// climate log stays as the permanent calibration instrument in Player-adjacent logs.

using Colossal.Serialization.Entities;
using Game;
using Game.Simulation;

namespace SkiResortMod.Systems
{
    public sealed partial class SnowLineSystem : GameSystemBase
    {
        /// <summary>Ambient fallback when climate is unavailable (menu, early load) —
        /// mild shoulder-season default, keeps consumers in the summer-floor regime.</summary>
        private const float FallbackAmbientC = 10f;

        private ClimateSystem m_ClimateSystem;
        private bool m_LoggedClimate;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ClimateSystem = World.GetOrCreateSystemManaged<ClimateSystem>();
            Enabled = false; // pull-based API; no per-tick work
            Mod.Log.Info($"{nameof(SnowLineSystem)} created (climate oracle, pull-based)");
        }

        // ─────────────────────────────────────────────────────────────────────────────────
        // Shared climate API (validated formula — see header)
        // ─────────────────────────────────────────────────────────────────────────────────

        /// <summary>Current ambient temperature (°C) at the reference elevation.</summary>
        public float GetAmbientTemperature()
        {
            return m_ClimateSystem != null ? (float)m_ClimateSystem.temperature : FallbackAmbientC;
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

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            // Calibration log, once per session (Editor counts: its climate panel can force
            // seasons on demand, which is how the formula was validated).
            if (m_LoggedClimate || (mode != GameMode.Game && mode != GameMode.Editor) ||
                m_ClimateSystem == null)
                return;
            m_LoggedClimate = true;

            try
            {
                Mod.Log.Info(
                    "Climate constants: " +
                    $"temperatureBaseHeight={m_ClimateSystem.temperatureBaseHeight}, " +
                    $"snowTemperatureHeightScale={m_ClimateSystem.snowTemperatureHeightScale}, " +
                    $"freezingTemperature={m_ClimateSystem.freezingTemperature}, " +
                    $"ambient={GetAmbientTemperature():F1}, " +
                    $"snowLine={GetSnowLineElevation():F0} m, " +
                    $"currentSeason={m_ClimateSystem.currentSeasonName}, " +
                    $"isSnowing={m_ClimateSystem.isSnowing}");
            }
            catch (System.Exception e)
            {
                Mod.Log.Warn($"Climate logging failed (non-fatal): {e.Message}");
            }
        }

        protected override void OnUpdate()
        {
        }
    }
}
