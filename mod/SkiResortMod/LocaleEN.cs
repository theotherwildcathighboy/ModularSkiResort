// English display names + descriptions for the asset pack's player-facing prefabs.
// Registered in Mod.OnLoad via GameManager.instance.localizationManager.AddSource("en-US", ...)
// alongside the options-UI source (Settings\LocaleEN.cs) — the manager merges every source
// registered for the active locale.
//
// Key patterns (verified against decompiled Game.UI.InGame.PrefabUISystem.GetTitleAndDescription):
//   - plain assets:                Assets.NAME[<prefab name>] / Assets.DESCRIPTION[<prefab name>]
//   - ServiceUpgrade assets:       Assets.UPGRADE_NAME[...] / Assets.UPGRADE_DESCRIPTION[...]
//     (the lodge carries Game.Prefabs.ServiceUpgrade, so the selected-info panel reads the
//     UPGRADE_* keys; the plain keys are supplied too for every other UI path, e.g. search)
//   - UIAssetCategoryPrefab:       SubServices.NAME[...] / Assets.SUB_SERVICE_DESCRIPTION[...]
//     (the toolbar tab tooltip title for SkiResortCategory)
// Additional languages: further IDictionarySource implementations — see SPEC §13 Phase 6.

using System.Collections.Generic;
using Colossal;

namespace SkiResortMod
{
    public sealed class LocaleEN : IDictionarySource
    {
        public IEnumerable<KeyValuePair<string, string>> ReadEntries(
            IList<IDictionaryEntryError> errors,
            Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                // --- Toolbar tab (UIAssetCategoryPrefab) ----------------------------------
                { "SubServices.NAME[SkiResortCategory]", "Ski Resort" },
                {
                    "Assets.SUB_SERVICE_DESCRIPTION[SkiResortCategory]",
                    "Everything needed to build a mountain ski resort: the welcome sign, " +
                    "lodges, lifts and runs."
                },

                // --- Resort anchor --------------------------------------------------------
                { "Assets.NAME[SkiResortGate]", "Ski Area Welcome Sign" },
                {
                    "Assets.DESCRIPTION[SkiResortGate]",
                    "The heart of a ski area. Place it by the road at the resort entrance — " +
                    "utilities and services connect here, and every resort building is built " +
                    "from its upgrade menu."
                },

                // --- Lodge (ServiceUpgrade child of the gate) -----------------------------
                { "Assets.NAME[SkiAFrameDayLodge]", "A-Frame Day Lodge" },
                {
                    "Assets.DESCRIPTION[SkiAFrameDayLodge]",
                    "A modest first lodge: cafeteria, rentals, and a sun deck. The tier-1 " +
                    "anchor of a growing ski area."
                },
                { "Assets.UPGRADE_NAME[SkiAFrameDayLodge]", "A-Frame Day Lodge" },
                {
                    "Assets.UPGRADE_DESCRIPTION[SkiAFrameDayLodge]",
                    "A modest first lodge: cafeteria, rentals, and a sun deck. The tier-1 " +
                    "anchor of a growing ski area."
                },

                // --- Lift network ---------------------------------------------------------
                { "Assets.NAME[SkiBaseStationBuilding]", "Chairlift Base Station" },
                {
                    "Assets.DESCRIPTION[SkiBaseStationBuilding]",
                    "Drive terminal of a fixed-grip chairlift. Place before drawing the lift " +
                    "line, or snap onto an existing line end."
                },
                { "Assets.NAME[SkiTopStationBuilding]", "Chairlift Top Station" },
                {
                    "Assets.DESCRIPTION[SkiTopStationBuilding]",
                    "Return terminal of a fixed-grip chairlift. Power flows up the line from " +
                    "the base."
                },

                // Upgrade-panel keys — stations moved into the gate's sub-menu
                // (DECISIONS 93; lodge precedent for the key pattern).
                { "Assets.UPGRADE_NAME[SkiBaseStationBuilding]", "Chairlift Base Station" },
                {
                    "Assets.UPGRADE_DESCRIPTION[SkiBaseStationBuilding]",
                    "Drive terminal of a fixed-grip chairlift, built for this resort. Place " +
                    "before drawing the lift line; no road connection needed."
                },
                { "Assets.UPGRADE_NAME[SkiTopStationBuilding]", "Chairlift Top Station" },
                {
                    "Assets.UPGRADE_DESCRIPTION[SkiTopStationBuilding]",
                    "Return terminal of a fixed-grip chairlift, built for this resort. Power " +
                    "flows up the line from the base; no road connection needed."
                },
                { "Assets.NAME[SkiChairliftHV]", "Fixed-Grip Chairlift" },
                {
                    "Assets.DESCRIPTION[SkiChairliftHV]",
                    "Draw the lift line like a road: towers pick their own height, chairs " +
                    "ride the cable."
                },

                // --- Runs -----------------------------------------------------------------
                { "Assets.NAME[SkiRunGroomedA]", "Groomed Ski Run" },
                {
                    "Assets.DESCRIPTION[SkiRunGroomedA]",
                    "Freeform ski run. Paint the trail within 5 km of a Ski Area Welcome " +
                    "Sign; grooming appears on the mountain and counts toward the " +
                    "resort's attractiveness."
                },
                { "Assets.NAME[SkiRunSnowmakingA]", "Ski Run with Snowmaking" },
                {
                    "Assets.DESCRIPTION[SkiRunSnowmakingA]",
                    "A groomed run with snowmaking coverage — holds its season longer and " +
                    "scores attractiveness in marginal weather. Operates within 5 km of a " +
                    "Ski Area Welcome Sign."
                },
                { "Assets.NAME[SkiRunNightA]", "Night Skiing Run" },
                {
                    "Assets.DESCRIPTION[SkiRunNightA]",
                    "A snowmaking run with floodlighting for a second, after-dark shift — " +
                    "the most expensive trail to keep open, and the biggest draw. Operates " +
                    "within 5 km of a Ski Area Welcome Sign."
                },

                // Upgrade-panel keys (the gate's sub-menu shows UPGRADE_* for
                // ServiceUpgrade assets — the lodge precedent).
                { "Assets.UPGRADE_NAME[SkiRunGroomedA]", "Groomed Ski Run" },
                {
                    "Assets.UPGRADE_DESCRIPTION[SkiRunGroomedA]",
                    "Draw a groomed trail for this resort. One-time trail-cutting charge; " +
                    "free to keep open."
                },
                { "Assets.UPGRADE_NAME[SkiRunSnowmakingA]", "Ski Run with Snowmaking" },
                {
                    "Assets.UPGRADE_DESCRIPTION[SkiRunSnowmakingA]",
                    "Draw a snowmaking trail for this resort. One-time installation " +
                    "charge, then per-hectare operating costs while the guns run."
                },
                { "Assets.UPGRADE_NAME[SkiRunNightA]", "Night Skiing Run" },
                {
                    "Assets.UPGRADE_DESCRIPTION[SkiRunNightA]",
                    "Draw a floodlit snowmaking trail for this resort. The highest " +
                    "installation and operating costs — and the biggest draw."
                },
            };
        }

        public void Unload()
        {
        }
    }
}
