// T289 cold-weather tourism (1.0.4). The rule is ColdTourismRules (pure, ResortBatch TB-8). This part of ResortTourismBonusSystem:
//   1. counts the open ski areas in Recompute (ColdTourismRules.IsOpenArea over the same area scores the bonus reads);
//   2. while the option is on and at least one area is open, writes ColdTourismRules.ColdThresholdOpenC into the
//      AttractivenessParameterData singleton's m_ExtremeTemperature.x. Only .x. m_TemperatureAffect is never touched (its y is the hot side's too);
//   3. writes the PREFAB's value back (AttractivenessParametersPrefab through PrefabSystem, never the live singleton, so our own write can
//      never become the "original") when no area is open, when the option is off, before every load (OnGamePreload) and on every load.
//      A game restart rebuilds the singleton from the prefab anyway.
//
// Engine facts (decompile 1.6.0f1, same lines in 1.6.2f1 and 1.6.4f1):
//   - The singleton lives on the parameters prefab's entity. AttractivenessParametersPrefab.LateInitialize fills it once when the prefab
//     loads (AttractivenessParametersPrefab.cs:52-67). Prefab entities outlive a save load, so a write would leak into the next city
//     without the restore. Nothing is saved: prefab data is not part of a save.
//   - TourismSystem copies it into its job at schedule time (TourismSystem.cs:257). The Tourism infoview reads it too
//     (TourismInfoviewUISystem.cs:171), so the player sees the true weather factor.
//   - GameModeSystem (Deserialize phase, SystemOrder.cs:797) may restore and apply a game mode's AttractivenessParametersMode on a load
//     (GameModeSystem.cs:63-89). Our preload restore runs before that, so a mode's value stands until we engage.
//
// Writes: main thread, only when the value differs. A singleton that never was ours stays untouched while we are not engaged (vanilla,
// a game mode or another mod own it then). A missing singleton or prefab logs once per load and does nothing.
// Log: one line per load with the option and the prefab value, then one line per threshold change (old -> new, reason). Never per frame.

using System.Globalization;
using Colossal.Serialization.Entities;
using Game;
using Game.Prefabs;
using SkiResortMod.Settings;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class ResortTourismBonusSystem
    {
        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_ColdParamQuery;

        private int m_OpenAreas;          // open ski areas at the last Recompute
        private bool m_ColdEvaluated;     // the state below was applied at least once since the load
        private bool m_ColdEngaged;       // the state applied last
        private bool m_ColdOption = ColdTourismRules.DefaultEnabled;
        private bool m_ColdWritten;       // the singleton holds our value now (cleared by a restore). Survives a load on purpose.
        private bool m_ColdMissingLogged, m_ColdLoggedOnce;

        /// <summary>Open ski areas at the last recompute (the hub and the bridge may read it).</summary>
        internal int OpenAreas => m_OpenAreas;

        /// <summary>True while the singleton holds the lowered cold threshold.</summary>
        internal bool ColdThresholdLowered => m_ColdWritten;

        private void CreateCold()
        {
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_ColdParamQuery = GetEntityQuery(ComponentType.ReadOnly<AttractivenessParameterData>());
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            RestoreCold("before a load");
        }

        /// <summary>Called from OnGameLoadingComplete: a leaked write goes, the per-load state clears.</summary>
        private void ResetColdForLoad()
        {
            RestoreCold("load");
            m_OpenAreas = 0;
            m_ColdEvaluated = false;
            m_ColdEngaged = false;
            m_ColdMissingLogged = false;
            m_ColdLoggedOnce = false;
        }

        /// <summary>Every simulation frame after the settle. Cheap unless the wanted state changed: one setting read and two compares.</summary>
        private void StepCold()
        {
            bool option = SkiResortSettings.ColdWeatherTourismOf(Mod.Settings);
            bool engaged = ColdTourismRules.Engaged(option, m_OpenAreas);
            if (m_ColdEvaluated && engaged == m_ColdEngaged && option == m_ColdOption)
                return;
            m_ColdEvaluated = true;
            m_ColdEngaged = engaged;
            m_ColdOption = option;
            string reason = engaged
                ? string.Format(CultureInfo.InvariantCulture, "{0} ski area(s) open, option on", m_OpenAreas)
                : option ? "no ski area open" : "option off";
            ApplyCold(option, m_OpenAreas, reason);
        }

        /// <summary>Writes the vanilla value back if the singleton holds ours. Safe outside a game (menu, editor).</summary>
        private void RestoreCold(string reason)
        {
            if (!m_ColdWritten)
                return;
            ApplyCold(false, 0, reason);
        }

        /// <summary>The one write. Main thread.</summary>
        private void ApplyCold(bool option, int openAreas, string reason)
        {
            if (!TryColdSingleton(out Entity entity, out float vanillaX))
                return;
            float target = ColdTourismRules.ThresholdC(option, openAreas, vanillaX);
            bool engaged = ColdTourismRules.Engaged(option, openAreas);
            if (!m_ColdLoggedOnce)
            {
                m_ColdLoggedOnce = true;
                Mod.Log.Info(string.Format(CultureInfo.InvariantCulture,
                    "Cold tourism: option {0}; vanilla cold threshold {1:0.#} C (prefab), {2:0.#} C while a ski area is open",
                    SkiResortSettings.ColdWeatherTourismOf(Mod.Settings) ? "on" : "off", vanillaX, ColdTourismRules.ThresholdC(true, 1, vanillaX)));
            }
            if (!engaged && !m_ColdWritten)
                return;   // never ours: leave vanilla, a game mode or another mod alone

            AttractivenessParameterData data = EntityManager.GetComponentData<AttractivenessParameterData>(entity);
            float old = data.m_ExtremeTemperature.x;
            if (old != target)
            {
                data.m_ExtremeTemperature.x = target;   // .x only (see the header)
                EntityManager.SetComponentData(entity, data);
                Mod.Log.Info(string.Format(CultureInfo.InvariantCulture,
                    "Cold tourism: cold threshold {0:0.#} -> {1:0.#} C ({2})", old, target, reason));
            }
            m_ColdWritten = engaged && target != vanillaX;
        }

        /// <summary>The singleton entity and the prefab's m_ExtremeTemperature.x. False (logged once per load) when either is missing.</summary>
        private bool TryColdSingleton(out Entity entity, out float vanillaX)
        {
            entity = Entity.Null;
            vanillaX = float.NaN;
            string missing = null;
            if (m_ColdParamQuery.CalculateEntityCount() != 1)
                missing = "AttractivenessParameterData singleton not found (count " + m_ColdParamQuery.CalculateEntityCount() + ")";
            else
            {
                entity = m_ColdParamQuery.GetSingletonEntity();
                if (!m_PrefabSystem.TryGetPrefab(entity, out AttractivenessParametersPrefab prefab) || prefab == null)
                    missing = "AttractivenessParametersPrefab not found for the singleton";
                else
                    vanillaX = prefab.m_ExtremeTemperature.x;
            }
            if (missing == null)
                return true;
            if (!m_ColdMissingLogged)
            {
                m_ColdMissingLogged = true;
                Mod.Log.Warn("Cold tourism: " + missing + "; the cold threshold is left as it is");
            }
            return false;
        }
    }
}
