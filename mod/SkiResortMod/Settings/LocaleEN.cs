// English strings for the options UI. Registered in Mod.OnLoad via
// GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(settings)).
// Additional languages: add further IDictionarySource implementations (or a translations
// file) and AddSource them with the matching locale id — see SPEC §13 Phase 6.

using System.Collections.Generic;
using Colossal;

namespace SkiResortMod.Settings
{
    public sealed class LocaleEN : IDictionarySource
    {
        private readonly SkiResortSettings m_Setting;

        public LocaleEN(SkiResortSettings setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(
            IList<IDictionaryEntryError> errors,
            Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Modular Ski Resort" },

                { m_Setting.GetOptionTabLocaleID(SkiResortSettings.SectionMain), "Main" },
                { m_Setting.GetOptionGroupLocaleID(SkiResortSettings.GroupBalance), "Balance" },
                { m_Setting.GetOptionGroupLocaleID(SkiResortSettings.GroupSnow), "Snow" },

                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.AttractivenessScaleK)),
                    "Attractiveness scale (K)"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.AttractivenessScaleK)),
                    "How strongly resort size converts into tourist attractiveness. " +
                    "Higher = a big resort draws more tourists. Default 4."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.BackcountryValuePercent)),
                    "Backcountry trail value"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.BackcountryValuePercent)),
                    "How much a trail outside lift service range is worth, relative to a " +
                    "lift-served trail. Default 40%."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.EnableSnowGating)),
                    "Snow & season gating"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.EnableSnowGating)),
                    "When enabled, a resort built below the snow line barely operates outside " +
                    "winter. Disable on maps without real vertical."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.SnowmakingBonusCelsius)),
                    "Snowmaking bonus (°C)"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.SnowmakingBonusCelsius)),
                    "Extra degrees of operating tolerance a snowmaking plant provides. Default 3."
                },
                {
                    m_Setting.GetOptionGroupLocaleID(SkiResortSettings.GroupTerrain),
                    "Terrain"
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.TerrainSnowSlot)),
                    "Ski-run terrain paint slot"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.TerrainSnowSlot)),
                    "Ski runs paint the terrain itself using one of the map's four extra " +
                    "material slots. Auto uses a free slot only — many maps have none, and " +
                    "runs then fall back to flat painted surfaces. Force replaces that map " +
                    "material with ski-run snow everywhere the map author painted it " +
                    "(usually small areas — check your map). Takes effect when a save loads."
                },
                {
                    m_Setting.GetEnumValueLocaleID(TerrainSlotPolicy.Auto),
                    "Auto (free slot only)"
                },
                {
                    m_Setting.GetEnumValueLocaleID(TerrainSlotPolicy.ForceExtra1),
                    "Force slot 1"
                },
                {
                    m_Setting.GetEnumValueLocaleID(TerrainSlotPolicy.ForceExtra2),
                    "Force slot 2"
                },
                {
                    m_Setting.GetEnumValueLocaleID(TerrainSlotPolicy.ForceExtra3),
                    "Force slot 3"
                },
                {
                    m_Setting.GetEnumValueLocaleID(TerrainSlotPolicy.ForceExtra4),
                    "Force slot 4"
                },
                {
                    m_Setting.GetEnumValueLocaleID(TerrainSlotPolicy.Off),
                    "Off (decal surfaces)"
                },
            };
        }

        public void Unload()
        {
        }
    }
}
