// The game-side half of the display layer (DisplayUnits.cs is the pure half). Two live facts:
//   Imperial  the game's unit setting (Options > Interface > Unit system, InterfaceSettings.unitSystem = Freedom). Read live, a few
//             field reads, so a change in Options shows on the next tooltip.
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

        /// <summary>True while the game's unit setting is US customary (InterfaceSettings.UnitSystem.Freedom).</summary>
        internal static bool Imperial
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

        /// <summary>A horizontal distance in meters: m / km, or yd / mi (vanilla "length"). Whole meters in.</summary>
        internal static ILocElement Length(float m) => new LocalizedNumber<int>(DisplayUnits.Whole(m), "length");

        /// <summary>A height, drop or elevation in meters: m, or ft (vanilla "height").</summary>
        internal static ILocElement Height(float m) => new LocalizedNumber<int>(DisplayUnits.Whole(m), "height");

        /// <summary>A slope or bend already in degrees, one decimal (vanilla "angle" drops a trailing .0).</summary>
        internal static ILocElement Angle(float deg) => new LocalizedNumber<float>(DisplayUnits.Round1(deg), "angle");

        /// <summary>A grade (rise / run) as the shown slope in degrees.</summary>
        internal static ILocElement Slope(float grade) => Angle(DisplayUnits.GradeToDeg(grade));

        /// <summary>Earthwork: "{N} m³" or "{N} yd³".</summary>
        internal static ILocElement Volume(float m3)
        {
            bool imp = DisplayState.Imperial;
            return LocalizedString.Id(UnitPrefix + (imp ? "CUBIC_YARDS" : "CUBIC_METERS"),
                ("N", new LocalizedNumber<int>(DisplayUnits.Volume(m3, imp), "integer")));
        }
    }
}
