// Mod options (Options → Mods → Modular Ski Resort).
//
// SPEC §10: "A setting for every balance constant" — players must be able to fix our tuning
// mistakes without waiting for a patch. This stub exposes the two main dials from §6.8
// (K and the snowmaking bonus) plus the gates that turn subsystems off entirely.
// More constants (per-tier A_lodge, trail class values, lift values, backcountry factor)
// get added here as the systems that consume them land.
//
// Pattern follows the official ModSetting idiom (Game.Modding.ModSetting), the same shape
// Tourism Overhaul uses, so the two mods' options pages behave identically.

using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;

namespace SkiResortMod.Settings
{
    /// <summary>Which terrain Extra material slot the splat system may claim for ski-run
    /// paint. Maps define up to four; many CO maps fill all four, so Auto (free-only)
    /// finds nothing there — Force deliberately replaces that map material with our
    /// snow/summer set (anywhere the map painted it shows runs' material instead).</summary>
    public enum TerrainSlotPolicy
    {
        Auto,
        ForceExtra1,
        ForceExtra2,
        ForceExtra3,
        ForceExtra4,
        Off,
    }

    [FileLocation("ModsSettings/SkiResortMod/SkiResortMod")]
    [SettingsUIGroupOrder(GroupBalance, GroupSnow, GroupTerrain)]
    [SettingsUIShowGroupName(GroupBalance, GroupSnow, GroupTerrain)]
    public sealed class SkiResortSettings : ModSetting
    {
        public const string SectionMain = "Main";
        public const string GroupBalance = "Balance";
        public const string GroupSnow = "Snow";
        public const string GroupTerrain = "Terrain";

        public SkiResortSettings(IMod mod) : base(mod)
        {
            SetDefaults();
        }

        // --- Balance (SPEC §6.8: A_open = A_lodge + K × sqrt(S)) --------------------------

        /// <summary>K — converts the resort size score S into attractiveness. The single most
        /// important balance dial: tourism impact ends up linear in S, scaled by K².</summary>
        [SettingsUISlider(min = 1f, max = 10f, step = 1f, unit = "integer")]
        [SettingsUISection(SectionMain, GroupBalance)]
        public int AttractivenessScaleK { get; set; }

        /// <summary>Value multiplier (percent) for trails outside lift service range
        /// (SPEC §6.4: backcountry is a modifier, not a gate; default 40%).</summary>
        [SettingsUISlider(min = 0f, max = 100f, step = 5f, unit = "percentage")]
        [SettingsUISection(SectionMain, GroupBalance)]
        public int BackcountryValuePercent { get; set; }

        // --- Snow (SPEC §6.6) -------------------------------------------------------------

        /// <summary>Master switch for the snow/season gate. Off = the resort always operates
        /// at full attractiveness (useful on maps without real vertical).</summary>
        [SettingsUISection(SectionMain, GroupSnow)]
        public bool EnableSnowGating { get; set; }

        /// <summary>°C added to the operating threshold when a snowmaking plant is built
        /// (SPEC §5.2 / §6.6; default +3 °C).</summary>
        [SettingsUISlider(min = 0f, max = 6f, step = 1f, unit = "integer")]
        [SettingsUISection(SectionMain, GroupSnow)]
        public int SnowmakingBonusCelsius { get; set; }

        // --- Terrain (ski-run terrain paint — research/terrain-material-runs.md) ----------

        /// <summary>Slot policy for painting runs onto the terrain itself. Auto claims a
        /// free Extra slot only (many CO maps have none); ForceExtraN sacrifices that
        /// map material; Off keeps the classic decal surfaces. Takes effect on load.</summary>
        [SettingsUISection(SectionMain, GroupTerrain)]
        public TerrainSlotPolicy TerrainSnowSlot { get; set; }

        public override void SetDefaults()
        {
            AttractivenessScaleK = 4;      // SPEC §6.8
            BackcountryValuePercent = 40;  // SPEC §6.4
            EnableSnowGating = true;
            SnowmakingBonusCelsius = 3;    // SPEC §6.6
            TerrainSnowSlot = TerrainSlotPolicy.Auto;
        }
    }
}
