// Pure rules of the shared "dark" signal (T250):
//   * The light hysteresis on the game's sun brightness (LightingSystem.dayLightBrightness, 0..1).
//   * The clock fallback when the brightness cannot say dark (the day/night visual cycle is off).
//   * The gate our own Night-gated light effects get from the signal, and the required flags that gate writes.
// SkiDarkSignalSystem is the only caller. Linked into tests/ResortBatch (DarkSignalTests). `System` + `Unity.Mathematics` only.
// Engine notes: EngineNotes/SkiDarkSignalSystem.md

using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>Where the signal comes from. None = not known yet (a load is still warming up).</summary>
    public enum DarkSource : byte { None = 0, Light = 1, Clock = 2 }

    /// <summary>What our own light effects follow. Vanilla = the prefab's own conditions (the game's fixed 18:00-06:00 Night flag).
    /// On = lit now. Off = dark now.</summary>
    public enum LightGate : byte { Vanilla = 0, On = 1, Off = 2 }

    public static class DarkSignalRules
    {
        /// <summary>Dark from below this brightness. Vanilla street lights switch on below a per-light random 0.20-0.30
        /// (StreetLightSystem.cs:89, :109, :368). From 0.20 ours are on once the last road light is on.</summary>
        public const float DarkBelow = 0.20f;
        /// <summary>Light again from this brightness. The band between is the hysteresis: a brightness that hovers does not flicker.</summary>
        public const float LightFrom = 0.30f;
        /// <summary>The clock fallback: dark from 17:00 to 07:00.</summary>
        public const float ClockDarkFromHour = 17f;
        public const float ClockDarkToHour = 7f;

        /// <summary>EffectConditionFlags.Night and .Occurring (Game.Prefabs/EffectConditionFlags.cs; the game-surface lock pins the enum).
        /// Occurring is true only on a weather event entity (EffectControlData.cs:425-430), so a required Occurring keeps our light off.</summary>
        public const int NightFlag = 0x2000;
        public const int OccurringFlag = 0x1000;

        /// <summary>One step of the light hysteresis. Not finite gives the last value.</summary>
        public static bool StepLight(bool wasDark, float brightness)
        {
            if (!math.isfinite(brightness))
                return wasDark;
            if (wasDark)
                return brightness < LightFrom;
            return brightness < DarkBelow;
        }

        /// <summary>The first look of a load: no history, so the midpoint of the band decides.</summary>
        public static bool FirstLight(float brightness)
        {
            return math.isfinite(brightness) && brightness < 0.5f * (DarkBelow + LightFrom);
        }

        /// <summary>The clock fallback on the game's normalized time of day (0..1, TimeSystem.normalizedTime). NaN gives false.</summary>
        public static bool ClockDark(float normalizedTime)
        {
            if (!math.isfinite(normalizedTime))
                return false;
            float hour = (normalizedTime - math.floor(normalizedTime)) * 24f;
            return hour >= ClockDarkFromHour || hour < ClockDarkToHour;
        }

        /// <summary>The brightness signal is used while the day/night visual cycle is on. With it off, the game pins the sun at 14:30
        /// (PlanetarySystem.cs:422-429), so the brightness never says dark and the clock decides.</summary>
        public static DarkSource SourceFor(bool dayNightVisual, bool haveLighting)
        {
            return dayNightVisual && haveLighting ? DarkSource.Light : DarkSource.Clock;
        }

        /// <summary>The gate for our lights. Only the brightness moves them off the vanilla flag: with the clock fallback the game's own
        /// Night flag is closer to what the player sees than ours.</summary>
        public static LightGate GateFor(DarkSource source, bool dark)
        {
            if (source != DarkSource.Light)
                return LightGate.Vanilla;
            return dark ? LightGate.On : LightGate.Off;
        }

        /// <summary>The required flags an effect prefab gets for a gate. Only effects whose own conditions require Night are steered;
        /// any other bit of the original stays. On drops Night (lit). Off drops Night and requires Occurring (never true on our props).</summary>
        public static int RequiredFor(LightGate gate, int originalRequired)
        {
            if (gate == LightGate.Vanilla || (originalRequired & NightFlag) == 0)
                return originalRequired;
            int withoutNight = originalRequired & ~NightFlag;
            return gate == LightGate.On ? withoutNight : withoutNight | OccurringFlag;
        }

        /// <summary>Whether our lights are lit under a gate. vanillaNight is the game's Night flag (EffectFlagSystem m_IsNightTime).</summary>
        public static bool LightsOn(LightGate gate, bool vanillaNight)
        {
            return gate == LightGate.Vanilla ? vanillaNight : gate == LightGate.On;
        }
    }
}
