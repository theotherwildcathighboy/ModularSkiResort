// The display layer for numbers and grading names the player reads (post-release round 1, T160 T161 T162). Internal math stays in
// grade (rise / run) and meters. Every conversion for display goes through this file (C#) or src/hub/units.ts (the hub), which
// mirrors the same factors and rules (ResortBatch DU-* and test-hub-rules.js check both).
//   Slopes     degrees with one decimal (vanilla unit "angle" drops a trailing .0): GradeToDeg, Deg1.
//   Units      the game's own setting (InterfaceSettings.unitSystem, Metric / Freedom). Lengths, heights and areas go through vanilla's
//              formatter (units "length", "height", "area": m / km or yd / mi, m or ft). What vanilla cannot format (earthwork, line
//              speed, hectares, snow depth) uses the factors below and the SkiResort.Unit.* keys.
//   Grading    North American (green circle, blue square, black diamond, double black) or European (blue, red, black, expert black).
//              Display only: SlopeBand, run kinds and saved data never change. ResolveScheme picks it from the Options value and the
//              city theme (ThemePrefab name "European" / "North American": the theme ids of the new-game menu, decompile 1.6.4f1).
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

    public static class DisplayUnits
    {
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

        /// <summary>A grade as slope text: "14.2°" (one decimal, a trailing .0 dropped like vanilla's "angle").</summary>
        public static string SlopeText(float grade)
        {
            float d = Deg1(grade);
            return float.IsNaN(d) ? "–" : d.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "°";
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
