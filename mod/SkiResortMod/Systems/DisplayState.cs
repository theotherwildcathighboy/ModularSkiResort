// The game-side half of the display layer (DisplayUnits.cs is the pure half). Live facts:
//   Units     the game's unit and temperature settings (InterfaceSettings.unitSystem / temperatureUnit) and the mod's Options (Lengths
//             and elevation, Weather, Slope), resolved per category: LengthImperial, DepthImperial, Temperature, SlopePercent. Read
//             live, a few field reads, so a change in Options shows on the next tooltip.
//   Scheme    the run grading the player sees (T162): Options > Run grading, Auto = the city's theme. SkiResortHubUISystem.OnUpdate calls
//             Refresh every frame while a city is loaded; it re-reads the theme prefab only when the theme entity or the option changes.
// Main thread only. Readers that run before the first Refresh (or in the Editor) see North American, the 1.0.0 look.

using System;
using Game.City;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Settings;
using Game.UI.Localization;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    internal static class DisplayState
    {
        private static Entity s_Theme = Entity.Null;
        private static GradeSchemeOption s_Option = GradeSchemeOption.Auto;
        private static bool s_Resolved;

        /// <summary>The scheme last resolved by Refresh (North American until then).</summary>
        internal static GradeScheme Scheme { get; private set; } = GradeScheme.NorthAmerican;

        /// <summary>The theme prefab name last read ("" = none). For the log line and the hub's Options note.</summary>
        internal static string ThemeName { get; private set; } = "";

        /// <summary>True while the game's unit setting is US customary (InterfaceSettings.UnitSystem.Freedom). Vanilla's formatter
        /// follows it.</summary>
        internal static bool GameImperial
        {
            get
            {
                try
                {
                    InterfaceSettings ui = GameManager.instance?.settings?.userInterface;
                    return ui != null && ui.unitSystem == InterfaceSettings.UnitSystem.Freedom;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>The game's temperature unit (InterfaceSettings.TemperatureUnit as an int: 0 °C, 1 °F, 2 K).</summary>
        internal static int GameTemperatureUnit
        {
            get
            {
                try
                {
                    InterfaceSettings ui = GameManager.instance?.settings?.userInterface;
                    return ui != null ? (int)ui.temperatureUnit : 0;
                }
                catch
                {
                    return 0;
                }
            }
        }

        /// <summary>Lengths, heights, areas, earthwork and line speed in imperial units (Options > Lengths and elevation, Auto = the game's
        /// unit setting). Read live, a few field reads.</summary>
        internal static bool LengthImperial => DisplayUnits.ResolveImperial(Mod.Settings?.UnitLengths ?? UnitOption.Auto, GameImperial);

        /// <summary>Snow depth in inches (Options > Weather, Auto = the game's unit setting).</summary>
        internal static bool DepthImperial => DisplayUnits.ResolveImperial(Mod.Settings?.UnitWeather ?? UnitOption.Auto, GameImperial);

        /// <summary>The temperature unit (Options > Weather, Auto = the game's temperature unit).</summary>
        internal static TempUnit Temperature => DisplayUnits.ResolveTemperature(Mod.Settings?.UnitWeather ?? UnitOption.Auto, GameTemperatureUnit);

        /// <summary>Slopes as whole percent grade instead of degrees (Options > Slope).</summary>
        internal static bool SlopePercent => (Mod.Settings?.SlopeUnit ?? SlopeOption.Degrees) == SlopeOption.Percent;

        /// <summary>The resolved choices as the hub's packed int (binding displayUnits, DisplayUnits.PackUnits).</summary>
        internal static int Packed => DisplayUnits.PackUnits(LengthImperial, DepthImperial, Temperature, SlopePercent);

        /// <summary>Re-resolves the scheme when the option or the city's theme entity changed. Returns the scheme.</summary>
        internal static GradeScheme Refresh(CityConfigurationSystem config, PrefabSystem prefabs, GradeSchemeOption option)
        {
            Entity theme = config != null ? config.defaultTheme : Entity.Null;
            if (s_Resolved && theme == s_Theme && option == s_Option)
                return Scheme;
            string name = "";
            try
            {
                if (theme != Entity.Null && prefabs != null && prefabs.TryGetPrefab<ThemePrefab>(theme, out ThemePrefab prefab) && prefab != null)
                    name = prefab.name ?? "";
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Run grading: the city theme could not be read ({e.Message}); North American unless Options says otherwise");
            }
            GradeScheme next = DisplayUnits.ResolveScheme(option, name);
            if (!s_Resolved || next != Scheme || name != ThemeName)
                Mod.Log.Info($"Run grading: {next} (Options {option}, city theme '{(name.Length > 0 ? name : "none")}')");
            s_Theme = theme;
            s_Option = option;
            s_Resolved = true;
            ThemeName = name;
            Scheme = next;
            return next;
        }
    }
}

namespace SkiResortMod.Systems
{
    /// <summary>Numbers as locale elements for C#-built player texts (tooltips, tool lines), in the display units of DisplayUnits /
    /// DisplayState. Vanilla formats length / height / angle by the game's unit setting (installed index.js unit table). The
    /// SkiResort.Unit.* keys (LocaleEN) carry the units vanilla has no format for, with the number already converted here.</summary>
    internal static class DisplayText
    {
        internal const string UnitPrefix = "SkiResort.Unit.";

        /// <summary>The locale key a text shows in the run grading (T162): key + "@EU" while the scheme is European and the active
        /// dictionary has that variant, else the key. Only texts that name a run color have a variant.</summary>
        internal static string Key(string key)
        {
            if (key == null || DisplayState.Scheme != GradeScheme.European)
                return key;
            string eu = key + DisplayUnits.SchemeSuffix(GradeScheme.European);
            var dictionary = GameManager.instance?.localizationManager?.activeDictionary;
            return dictionary != null && dictionary.TryGetValue(eu, out _) ? eu : key;
        }

        private static ILocElement UnitNumber(string key, ILocElement n) => LocalizedString.Id(UnitPrefix + key, ("N", n));

        /// <summary>A horizontal distance in meters: m / km, or yd / mi (Options > Lengths and elevation). Vanilla "length" when that
        /// matches the game's setting, else the same thresholds (DisplayUnits.LengthText) with the SkiResort.Unit.* keys.</summary>
        internal static ILocElement Length(float m)
        {
            bool imp = DisplayState.LengthImperial;
            if (imp == DisplayState.GameImperial)
                return new LocalizedNumber<int>(DisplayUnits.Whole(m), "length");
            float a = Math.Abs(m);
            if (imp)
                return a < 1609f ? UnitNumber("YARDS", new LocalizedNumber<int>(DisplayUnits.Whole(m / 0.9144f), "integer"))
                    : UnitNumber("MILES", new LocalizedNumber<float>(DisplayUnits.Round1(m / 1609.344f), "floatSingleFraction"));
            return a < 1000f ? UnitNumber("METERS", new LocalizedNumber<int>(DisplayUnits.Whole(m), "integer"))
                : UnitNumber("KILOMETERS", new LocalizedNumber<float>(DisplayUnits.Round1(m / 1000f), "floatSingleFraction"));
        }

        /// <summary>A height, drop or elevation in meters: m, or ft (Options > Lengths and elevation; vanilla "height" when that matches
        /// the game's setting).</summary>
        internal static ILocElement Height(float m)
        {
            bool imp = DisplayState.LengthImperial;
            if (imp == DisplayState.GameImperial)
                return new LocalizedNumber<int>(DisplayUnits.Whole(m), "height");
            return imp ? UnitNumber("FEET", new LocalizedNumber<int>(DisplayUnits.Whole(m * DisplayUnits.FeetPerMeter), "integer"))
                : UnitNumber("METERS", new LocalizedNumber<int>(DisplayUnits.Whole(m), "integer"));
        }

        /// <summary>An area in m²: m² / km², or ft² / acres (Options > Lengths and elevation; vanilla "area" when that matches the game's
        /// setting, else vanilla's thresholds: 100,000 m² and 100,000 ft², installed index.js).</summary>
        internal static ILocElement Area(float m2)
        {
            bool imp = DisplayState.LengthImperial;
            if (imp == DisplayState.GameImperial)
                return new LocalizedNumber<int>(DisplayUnits.Whole(m2), "area");
            if (imp)
            {
                float ft2 = m2 / 0.092903f;
                return Math.Abs(ft2) < 100000f ? UnitNumber("SQUARE_FEET", new LocalizedNumber<int>(DisplayUnits.Whole(ft2), "integer"))
                    : UnitNumber("ACRES", new LocalizedNumber<float>(DisplayUnits.Round1(m2 / 4046.873f), "floatSingleFraction"));
            }
            return Math.Abs(m2) < 100000f ? UnitNumber("SQUARE_METERS", new LocalizedNumber<int>(DisplayUnits.Whole(m2), "integer"))
                : UnitNumber("SQUARE_KILOMETERS", new LocalizedNumber<float>(DisplayUnits.Round1(m2 / 1e6f), "floatSingleFraction"));
        }

        /// <summary>An angle in degrees, one decimal (vanilla "angle" drops a trailing .0): a lift bend, a yaw. Never a slope (Slope).</summary>
        internal static ILocElement Angle(float deg) => new LocalizedNumber<float>(DisplayUnits.Round1(deg), "angle");

        /// <summary>A grade (rise / run) as the shown slope: degrees with one decimal, or whole percent grade (Options > Slope).</summary>
        internal static ILocElement Slope(float grade) =>
            DisplayState.SlopePercent ? new LocalizedNumber<int>(DisplayUnits.GradePct(grade), "percentage") : Angle(DisplayUnits.GradeToDeg(grade));

        /// <summary>A slope already in degrees, in the shown slope format (Slope).</summary>
        internal static ILocElement SlopeDeg(float deg) => DisplayState.SlopePercent ? Slope(DisplayUnits.DegToGrade(deg)) : Angle(deg);

        /// <summary>A vanilla FloatTooltip line showing a grade as the shown slope: unit "angle" with degrees (one decimal), or unit
        /// "percentage" with whole percent (Options > Slope). Both setters push only on a change.</summary>
        internal static void SetSlope(Game.UI.Tooltip.FloatTooltip tip, float grade)
        {
            bool pct = DisplayState.SlopePercent;
            tip.unit = pct ? "percentage" : "angle";
            tip.value = pct ? DisplayUnits.GradePct(grade) : DisplayUnits.Deg1(grade);
        }

        /// <summary>Earthwork: "{N} m³" or "{N} yd³".</summary>
        internal static ILocElement Volume(float m3)
        {
            bool imp = DisplayState.LengthImperial;
            return LocalizedString.Id(UnitPrefix + (imp ? "CUBIC_YARDS" : "CUBIC_METERS"),
                ("N", new LocalizedNumber<int>(DisplayUnits.Volume(m3, imp), "integer")));
        }
    }
}

namespace SkiResortMod.Systems
{
    /// <summary>A vanilla label + number tooltip line (IntTooltip / FloatTooltip with unit "length", "height" or "area") in the mod's
    /// units. Vanilla formats those units by the game's setting only, so while Options > Lengths and elevation differs from it the line
    /// shows as text instead: "{LABEL}: {VALUE}" (SkiResort.Unit.LABELED, DisplayText). Pick returns the widget to add. It allocates only
    /// when the shown value or the choice changes.</summary>
    internal sealed class UnitTooltipLine
    {
        internal enum Kind : byte
        {
            Length,
            Height,
            Area,
        }

        private readonly Kind m_Kind;
        private readonly Game.UI.Tooltip.StringTooltip m_Text;
        private float m_LastValue = float.NaN;
        private bool m_LastImperial;
        private string m_LastLabel;

        internal UnitTooltipLine(Game.UI.Widgets.PathSegment path, Kind kind)
        {
            m_Kind = kind;
            m_Text = new Game.UI.Tooltip.StringTooltip { path = (path.m_Key ?? "skiUnit") + "Text" };
        }

        /// <summary>The widget to add for `tip` showing `v` (meters, or m² for Area): `tip` itself (its value set by the caller) while the
        /// choice matches the game's setting, else the text line with the same label and color.</summary>
        internal Game.UI.Widgets.IWidget Pick(Game.UI.Tooltip.LabelIconTooltip tip, float v)
        {
            bool imp = DisplayState.LengthImperial;
            if (imp == DisplayState.GameImperial)
                return tip;
            m_Text.color = tip.color;
            float shown = (float)Math.Round(v, 1);
            string label = tip.label.id;
            if (shown != m_LastValue || imp != m_LastImperial || !string.Equals(label, m_LastLabel, StringComparison.Ordinal))
            {
                m_LastValue = shown;
                m_LastImperial = imp;
                m_LastLabel = label;
                ILocElement value = m_Kind == Kind.Area ? DisplayText.Area(v) : m_Kind == Kind.Height ? DisplayText.Height(v) : DisplayText.Length(v);
                m_Text.value = LocalizedString.Id(DisplayText.UnitPrefix + "LABELED", ("LABEL", tip.label), ("VALUE", value));
            }
            return m_Text;
        }
    }
}
