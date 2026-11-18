// The shared "dark" signal (T250): dark when the sun is dim, the way vanilla street lights decide (LightingSystem.dayLightBrightness),
// with hysteresis (DarkSignalRules). It also steers our own Night-gated light effects to the signal.
// Why: the game's Night flag is a fixed 18:00-06:00 clock (EffectFlagSystem.cs:155). Roads light at real dusk, our runs did not.
// How the lights follow it, without Harmony: an effect's conditions live on its effect prefab entity (EffectData.m_Flags, written once at
// EffectPrefab.LateInitialize, EffectPrefab.cs:31-45), and every enable check reads them fresh (EffectControlData.CheckConditions
// :614-621). Our four light effects are our own prefabs (no vanilla object uses them), so this system rewrites their required flags at
// dusk and dawn. The consumers (SkiNightLightSystem, SkiGroomerSystem) then stamp EffectsUpdated as before.
// Readers: SkiGroomerSystem (work window, lights), SkiNightLightSystem (run props). Known stays false until the lighting had frames to run.
// Log discipline: one ARMED line per load, one [dark] line per dusk or dawn, one WARN per missing effect prefab, a one-shot "!!!" catch.
// Game mode only. Any other load restores the prefabs' own conditions.
// Engine notes: EngineNotes/SkiDarkSignalSystem.md

using System;
using System.Globalization;
using Colossal.Serialization.Entities;
using Game;
using Game.Effects;
using Game.Prefabs;
using Game.Rendering;
using Game.Settings;
using Game.Simulation;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiDarkSignalSystem : GameSystemBase
    {
        /// <summary>Our own Night-gated light effects (tools/shipped-prefabs.json, type EffectPrefab). Run masts, snow gun beacons, groomers.</summary>
        private static readonly string[] EffectNames =
        {
            "SkiPisteFloodSource", "SkiSnowGunBeaconSource", "SkiGroomerFloodSource", "SkiGroomerBeaconSource",
        };

        /// <summary>Frames after a load before the signal is known: LightingSystem (PreCulling) must have run on the new city.</summary>
        private const int WarmupFrames = 30;

        private LightingSystem m_Lighting;
        private TimeSystem m_Time;
        private EffectFlagSystem m_EffectFlags;
        private PrefabSystem m_PrefabSystem;

        private readonly Entity[] m_Effects = new Entity[EffectNames.Length];
        private readonly int[] m_Original = new int[EffectNames.Length];
        private bool m_Resolved;

        private bool m_Armed;
        private bool m_Disabled;
        private int m_Frames;
        private bool m_HaveLight;
        private LightGate m_Applied;
        private bool m_AppliedValid;

        /// <summary>False until the first look after a load.</summary>
        public bool Known { get; private set; }
        /// <summary>Dark now (dusk to dawn). Read only when Known.</summary>
        public bool Dark { get; private set; }
        public DarkSource Source { get; private set; }
        /// <summary>True once the work window can be read: the signal is known, or it failed and the clock stands in.</summary>
        public bool Ready => Known || m_Disabled;

        /// <summary>Dark for the work window: the signal when known, else the 17:00-07:00 clock. Read when Ready.</summary>
        public bool DarkNow => Known ? Dark : DarkSignalRules.ClockDark(m_Time.normalizedTime);

        /// <summary>The gate our light effects carry now.</summary>
        public LightGate Gate => m_AppliedValid ? m_Applied : LightGate.Vanilla;

        /// <summary>Whether our Night-gated lights are lit now: the signal, or the game's Night flag where the prefab conditions are vanilla.</summary>
        public bool LightsOn => DarkSignalRules.LightsOn(Gate, m_EffectFlags.GetData().m_IsNightTime);

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Lighting = World.GetOrCreateSystemManaged<LightingSystem>();
            m_Time = World.GetOrCreateSystemManaged<TimeSystem>();
            m_EffectFlags = World.GetOrCreateSystemManaged<EffectFlagSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            Known = false;
            Dark = false;
            Source = DarkSource.None;
            m_Frames = 0;
            m_HaveLight = false;
            m_Armed = false;
            m_Disabled = false;
            try
            {
                Resolve();
                // Back to the prefabs' own conditions on every load. A Game load moves them again at its first look.
                Apply(LightGate.Vanilla);
                if (mode != GameMode.Game || m_Disabled)
                    return;
                m_Armed = true;
                int found = 0;
                for (int i = 0; i < m_Effects.Length; i++)
                {
                    if (m_Effects[i] != Entity.Null)
                        found++;
                }
                Mod.Log.Info(string.Format(CultureInfo.InvariantCulture,
                    "DarkSignal: ARMED, dark below brightness {0:0.00}, light again from {1:0.00} (vanilla street lights: 0.20-0.30 per light); " +
                    "clock fallback {2:0}:00-{3:0}:00 when the day/night visual cycle is off; {4} of {5} light effects follow it",
                    DarkSignalRules.DarkBelow, DarkSignalRules.LightFrom, DarkSignalRules.ClockDarkFromHour, DarkSignalRules.ClockDarkToHour,
                    found, m_Effects.Length));
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        protected override void OnUpdate()
        {
            if (!m_Armed || m_Disabled)
                return;
            try
            {
                Step();
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        private void Step()
        {
            if (m_Frames < WarmupFrames)
            {
                m_Frames++;
                return;
            }
            bool visual = SharedSettings.instance?.gameplay?.dayNightVisual ?? true;
            DarkSource source = DarkSignalRules.SourceFor(visual, m_Lighting != null);
            bool dark;
            float brightness = m_Lighting != null ? m_Lighting.dayLightBrightness : float.NaN;
            float time = m_Time.normalizedTime;
            if (source == DarkSource.Light)
            {
                dark = m_HaveLight ? DarkSignalRules.StepLight(Dark, brightness) : DarkSignalRules.FirstLight(brightness);
                m_HaveLight = true;
            }
            else
            {
                dark = DarkSignalRules.ClockDark(time);
                m_HaveLight = false;
            }
            bool changed = !Known || dark != Dark || source != Source;
            bool first = !Known;
            Known = true;
            Dark = dark;
            Source = source;
            if (!changed)
                return;
            LightGate gate = DarkSignalRules.GateFor(source, dark);
            Apply(gate);
            Mod.Log.Info(string.Format(CultureInfo.InvariantCulture,
                "DarkSignal [dark]: {0}{1} at {2:00}:{3:00}, brightness {4:0.000} (source {5}); our lights {6}",
                dark ? "DUSK, dark" : "DAWN, light", first ? " (first look of the load)" : "",
                (int)(time * 24f) % 24, (int)(time * 1440f) % 60, brightness, source,
                gate == LightGate.Vanilla ? "on the game's 18:00-06:00 Night flag" : gate == LightGate.On ? "on" : "off"));
        }

        private void Resolve()
        {
            if (m_Resolved)
                return;
            m_Resolved = true;
            for (int i = 0; i < EffectNames.Length; i++)
            {
                m_Effects[i] = Entity.Null;
                if (!m_PrefabSystem.TryGetPrefab(new PrefabID(nameof(EffectPrefab), EffectNames[i]), out PrefabBase prefab) ||
                    !(prefab is EffectPrefab effect) || !m_PrefabSystem.TryGetEntity(prefab, out Entity entity) ||
                    !EntityManager.HasComponent<EffectData>(entity))
                {
                    Mod.Log.Warn($"DarkSignal: the light effect {EffectNames[i]} is not loaded; whatever uses it stays on the game's Night flag");
                    continue;
                }
                m_Effects[i] = entity;
                // The managed prefab keeps the authored conditions; EffectData is the copy the engine reads (EffectPrefab.cs:34-44)
                m_Original[i] = (int)effect.m_Conditions.m_RequiredFlags;
            }
        }

        /// <summary>Writes the required flags of a gate to our light effects. A prefab component write: it syncs on the effect jobs that
        /// read EffectData, twice per game day.</summary>
        private void Apply(LightGate gate)
        {
            if (m_AppliedValid && m_Applied == gate)
                return;
            for (int i = 0; i < m_Effects.Length; i++)
            {
                Entity e = m_Effects[i];
                if (e == Entity.Null || !EntityManager.Exists(e) || !EntityManager.HasComponent<EffectData>(e))
                    continue;
                EffectData data = EntityManager.GetComponentData<EffectData>(e);
                int want = DarkSignalRules.RequiredFor(gate, m_Original[i]);
                if ((int)data.m_Flags.m_RequiredFlags == want)
                    continue;
                data.m_Flags.m_RequiredFlags = (EffectConditionFlags)want;
                EntityManager.SetComponentData(e, data);
            }
            m_Applied = gate;
            m_AppliedValid = true;
        }

        private void Fail(Exception e)
        {
            if (m_Disabled)
                return;
            m_Disabled = true;
            Known = false;
            Mod.Log.Error($"!!! DarkSignal: {e.GetType().Name}: {e.Message}; groomers use the 17:00-07:00 clock and lights the game's Night flag " +
                          $"until the next load\n{e}");
            try
            {
                m_AppliedValid = false;
                Apply(LightGate.Vanilla);
            }
            catch (Exception)
            {
                // the next load restores them
            }
        }
    }
}
