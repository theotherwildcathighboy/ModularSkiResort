// The display layer for numbers and grading names the player reads (post-release round 1, T160 T161 T162). Internal math stays in
// grade (rise / run) and meters. Every conversion for display goes through this file (C#) or src/hub/units.ts (the hub), which
// mirrors the same factors and rules (ResortBatch DU-* and test-hub-rules.js check both).
//   Four Options, each independent (players mix them: North American run names with metric heights and °C is common):
//   Grading    North American (green circle, blue square, black diamond, double black) or European (blue, red, black, expert black).
//              Display only: SlopeBand, run kinds and saved data never change. ResolveScheme picks it from the Options value and the
//              city theme (ThemePrefab name "European" / "North American": the theme ids of the new-game menu, decompile 1.6.4f1).
//   Lengths    length, height, elevation, vertical drop, area, earthwork and line speed: metric or imperial. Auto = the game's unit
//              setting (InterfaceSettings.unitSystem). ResolveImperial.
//   Weather    temperature (°C / °F) and snow depth (cm / in). Auto = the game's temperature unit (°C / °F / K) and unit system.
//              ResolveTemperature, ResolveImperial.
//   Slope      degrees with one decimal (default, vanilla unit "angle" drops a trailing .0) or whole percent grade.
//   Vanilla's own UI keeps following the game's setting: these Options change only the mod's text. Where the resolved choice equals
//   the game's setting, the mod uses vanilla's formatter (units "length", "height", "temperature"); otherwise the SkiResort.Unit.* keys
//   with the number converted here. What vanilla cannot format (earthwork, line speed, hectares, snow depth) always uses those keys.
//   The hub reads the resolved choices as one packed int (PackUnits, binding displayUnits, src/hub/units.ts unpackUnits).
// Pure file: `using System;` only (the ResortBatch and GradeBatch hosts link it).

using System;

namespace SkiResortMod.Systems
{
    /// <summary>The grading scheme the player sees. Display only.</summary>
    public enum GradeScheme : byte
    {
        NorthAmerican = 0,
        European = 1,
    }

    /// <summary>The Options value (SkiResortSettings.RunGradeScheme). Auto follows the city's theme.</summary>
    public enum GradeSchemeOption
    {
        Auto = 0,
        NorthAmerican = 1,
        European = 2,
    }

    /// <summary>The Options value of a unit category (SkiResortSettings.UnitLengths, UnitWeather). Auto follows the game's setting.
    /// The values are saved in the settings file: never renumber.</summary>
    public enum UnitOption
    {
        Auto = 0,
        Metric = 1,
        Imperial = 2,
    }

    /// <summary>The Options value of the slope format (SkiResortSettings.SlopeUnit). Saved: never renumber.</summary>
    public enum SlopeOption
    {
        Degrees = 0,
        Percent = 1,
    }

    /// <summary>A temperature unit, in the values of vanilla's InterfaceSettings.TemperatureUnit and the UI's TemperatureUnit.</summary>
    public enum TempUnit
    {
        Celsius = 0,
        Fahrenheit = 1,
        Kelvin = 2,
    }

    public static class DisplayUnits
    {
        // The packed display choices the hub reads (binding displayUnits; src/hub/units.ts UNITS_* reads the same bits).
        public const int PackLengthImperial = 1;
        public const int PackDepthImperial = 2;
        public const int PackTempShift = 2;      // bits 2-3: TempUnit
        public const int PackTempMask = 3;
        public const int PackSlopePercent = 16;

        /// <summary>Imperial or not for a unit category: the Options value, or for Auto the game's unit setting.</summary>
        public static bool ResolveImperial(UnitOption option, bool gameImperial)
        {
            switch (option)
            {
                case UnitOption.Metric: return false;
                case UnitOption.Imperial: return true;
                default: return gameImperial;
            }
        }

        /// <summary>The temperature unit: Metric °C, Imperial °F, Auto the game's own temperature unit (an unknown value reads °C).</summary>
        public static TempUnit ResolveTemperature(UnitOption weather, int gameTemperatureUnit)
        {
            switch (weather)
            {
                case UnitOption.Metric: return TempUnit.Celsius;
                case UnitOption.Imperial: return TempUnit.Fahrenheit;
                default:
                    return gameTemperatureUnit == (int)TempUnit.Fahrenheit ? TempUnit.Fahrenheit
                        : gameTemperatureUnit == (int)TempUnit.Kelvin ? TempUnit.Kelvin : TempUnit.Celsius;
            }
        }

        /// <summary>The resolved choices as the hub's packed int.</summary>
        public static int PackUnits(bool lengthImperial, bool depthImperial, TempUnit temperature, bool slopePercent) =>
            (lengthImperial ? PackLengthImperial : 0) | (depthImperial ? PackDepthImperial : 0)
            | (((int)temperature & PackTempMask) << PackTempShift) | (slopePercent ? PackSlopePercent : 0);

        /// <summary>A temperature in °C in a unit (vanilla's factors: °F = 9/5 °C + 32, K = °C + 273.16).</summary>
        public static float Temperature(float celsius, TempUnit unit) =>
            unit == TempUnit.Fahrenheit ? celsius * 9f / 5f + 32f : unit == TempUnit.Kelvin ? celsius + 273.16f : celsius;

        /// <summary>A grade as whole percent (0.25 = 25), half away from zero: the Percent slope format.</summary>
        public static int GradePct(float grade) => Whole(grade * 100f);

        /// <summary>The ThemePrefab name of the European theme (the new-game menu's theme id).</summary>
        public const string ThemeEuropean = "European";

        // Imperial factors. Feet and yards are vanilla's (installed index.js: 3.28084 ft per m, 0.9144 m per yd).
        public const float FeetPerMeter = 3.28084f;
        public const float CubicYardsPerCubicMeter = 1.307951f;   // 1 / 0.9144^3
        public const float AcresPerHectare = 2.471054f;           // 10,000 / 4,046.873 m² (vanilla's acre)
        public const float CmPerInch = 2.54f;
        public const float FeetPerMinutePerMps = 196.8504f;        // 60 * 3.28084: US lifts quote line speed in ft/min

        /// <summary>Grade (rise / run, 0.25 = 25 %) to degrees. Negative grades give negative degrees. Not finite gives NaN.</summary>
        public static float GradeToDeg(float grade)
        {
            if (float.IsNaN(grade) || float.IsInfinity(grade))
                return float.NaN;
            return (float)(Math.Atan(grade) * (180.0 / Math.PI));
        }

        /// <summary>Degrees to grade (rise / run). Clamped below 89.9 degrees.</summary>
        public static float DegToGrade(float deg)
        {
            if (float.IsNaN(deg) || float.IsInfinity(deg))
                return float.NaN;
            double d = Math.Max(-89.9, Math.Min(89.9, deg));
            return (float)Math.Tan(d * (Math.PI / 180.0));
        }

        /// <summary>A grade as the shown slope: degrees rounded to one decimal (half away from zero). THE slope format.</summary>
        public static float Deg1(float grade)
        {
            float d = GradeToDeg(grade);
            return float.IsNaN(d) ? d : Round1(d);
        }

        /// <summary>Rounds to one decimal, half away from zero.</summary>
        public static float Round1(float v) => (float)(Math.Round(v * 10.0, MidpointRounding.AwayFromZero) / 10.0);

        /// <summary>A whole number for display, half away from zero.</summary>
        public static int Whole(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0 : (int)Math.Round(v, MidpointRounding.AwayFromZero);

        /// <summary>Earthwork: m³, or yd³ when imperial.</summary>
        public static int Volume(float m3, bool imperial) => Whole(imperial ? m3 * CubicYardsPerCubicMeter : m3);

        /// <summary>Hectares, or acres when imperial, one decimal.</summary>
        public static float AreaHa(float ha, bool imperial) => Round1(imperial ? ha * AcresPerHectare : ha);

        /// <summary>Lift line speed: m/s with one decimal, or whole ft/min when imperial.</summary>
        public static float Speed(float mps, bool imperial) => imperial ? Whole(mps * FeetPerMinutePerMps) : Round1(mps);

        /// <summary>Snow depth: whole cm, or whole inches when imperial.</summary>
        public static int Depth(float cm, bool imperial) => Whole(imperial ? cm / CmPerInch : cm);

        /// <summary>A distance as text where C# writes the string itself (3D labels, template tooltips), with vanilla's thresholds
        /// (installed index.js "length"): under 1,000 m whole meters, else km with one decimal; imperial under 1,609 m whole yards, else
        /// miles with one decimal. Invariant culture, thousands separated.</summary>
        public static string LengthText(float m, bool imperial)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (float.IsNaN(m) || float.IsInfinity(m))
                return "–";
            if (imperial)
                return Math.Abs(m) < 1609f ? Whole(m / 0.9144f).ToString("#,0", inv) + " yd" : (m / 1609.344).ToString("#,0.0", inv) + " mi";
            return Math.Abs(m) < 1000f ? Whole(m).ToString("#,0", inv) + " m" : (m / 1000.0).ToString("#,0.0", inv) + " km";
        }

        /// <summary>An area in hectares as text: "3.2 ha", or "7.9 acres" when imperial.</summary>
        public static string AreaHaText(float ha, bool imperial) =>
            AreaHa(ha, imperial).ToString("#,0.0", System.Globalization.CultureInfo.InvariantCulture) + (imperial ? " acres" : " ha");

        /// <summary>A grade as slope text: "14.2°" (one decimal, a trailing .0 dropped like vanilla's "angle"), or "25%" when `percent`
        /// (whole percent grade, vanilla's "percentage" look).</summary>
        public static string SlopeText(float grade, bool percent = false)
        {
            if (float.IsNaN(grade) || float.IsInfinity(grade))
                return "–";
            if (percent)
                return GradePct(grade).ToString("#,0", System.Globalization.CultureInfo.InvariantCulture) + "%";
            float d = Deg1(grade);
            return d.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "°";
        }

        /// <summary>A height as text for a 3D label: "350 m", or "1,148 ft" when imperial (whole, thousands separated, invariant).</summary>
        public static string HeightText(float m, bool imperial)
        {
            int v = Whole(imperial ? m * FeetPerMeter : m);
            return v.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture) + (imperial ? " ft" : " m");
        }

        /// <summary>The scheme the player sees: the Options value, or for Auto the city theme (European theme = European, any other
        /// theme or none = North American).</summary>
        public static GradeScheme ResolveScheme(GradeSchemeOption option, string themeName)
        {
            switch (option)
            {
                case GradeSchemeOption.NorthAmerican: return GradeScheme.NorthAmerican;
                case GradeSchemeOption.European: return GradeScheme.European;
                default:
                    return string.Equals(themeName, ThemeEuropean, StringComparison.Ordinal) ? GradeScheme.European : GradeScheme.NorthAmerican;
            }
        }

        // The cat track tool's max slope in Options (degrees). The top of the range stands for the blue-run crux, exactly 25 % grade
        // (SkiSlopeClassifier.GradeBlue), so the default track behaves as in 1.0.0. The bottom is about the old 5 % minimum.
        public const float CatTrackDegMin = 3f, CatTrackDegMax = 14f;
        public const int LegacyCatTrackPctDefault = 25;
        public const float CatTrackGradeAtMax = 0.25f;

        /// <summary>The grade the cat track tool uses for an Options value in degrees: clamped to the range, and the top of the range
        /// reads as exactly CatTrackGradeAtMax (tan 14° = 24.9 %, close but not the crux).</summary>
        public static float CatTrackMaxGrade(float deg)
        {
            if (float.IsNaN(deg) || deg >= CatTrackDegMax)
                return CatTrackGradeAtMax;
            return DegToGrade(Math.Max(CatTrackDegMin, deg));
        }

        /// <summary>A 1.0.0 cat track max grade in percent (5-25) as the new Options value: degrees on the 0.5 step, in range.</summary>
        public static float LegacyPctToDeg(int pct)
        {
            if (pct >= LegacyCatTrackPctDefault)
                return CatTrackDegMax;
            float d = GradeToDeg(Math.Max(1, pct) / 100f);
            float stepped = (float)(Math.Round(d * 2.0, MidpointRounding.AwayFromZero) / 2.0);
            return Math.Max(CatTrackDegMin, Math.Min(CatTrackDegMax, stepped));
        }

        /// <summary>The locale key suffix of a scheme: "" for North American (the shipped keys), "@EU" for European variants
        /// (LocaleEN "SkiResort.SlopeBand[Green]@EU" etc.).</summary>
        public static string SchemeSuffix(GradeScheme s) => s == GradeScheme.European ? "@EU" : "";
    }
}
