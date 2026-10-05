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
// Round M1 (docs/research/hub-u2-spec.md §2.4 "Removed", §6.1 d/e; 2026-10-03, implementer I-4):
// the SkiResortCategory toolbar tab prefab is gone (zero vanilla toolbar entries, OD-10), so its
// SubServices.NAME / Assets.SUB_SERVICE_DESCRIPTION keys are removed; the three run prefabs lost
// their ServiceUpgrade component (OD-12 (a): the sign's upgrade strip holds the lodge only), so
// their six Assets.UPGRADE_NAME / UPGRADE_DESCRIPTION keys are removed too.
// Round E1 (2026-10-04, stream S6; docs/research/econ-round/spec/ch6-hub-ui.md §7.5, which owns this
// file, e1-spec.md R11 / I-27): hub contract 3 — 64 retired SkiResort.Hub.* keys deleted (ch6 §7.4,
// no C# reference), 7 texts changed in place (RESORT_PREV, RESORT_NEXT, UNLINKED_ENTRY, RUNS_EMPTY,
// LIFT_CAPACITY, BUILD_HINT_SIGN, HINT[LiftNoPair]), 281 new keys in block (e); chapter 2 §4.6's run
// asset names (Title Case, D17), the natural run's name and description and SkiResort.Tooltip.RUN_FEE;
// ch5 R-UI-2's RUN_DEFAULT[Natural]. Gate rule R4 (mod/SkiResortUI/tools/check-bundle.js) fails the UI
// build on any text that differs from its src/hub/hubText.ts tuple.
// Round F7 (2026-10-04, implementer 4; docs/research/field-test-7-fixes.md D1-D6, findings/hub-ux.md and
// its Verification): this round CUTS text. The sign, lodge and base station descriptions are one line
// each (D6). New hub keys: RUN_STATE[Open|Patchy|Closed], TILE[RunsOpen], EST_TO_TIP, MONEY_TIP[Rwh],
// PROBLEM[SignNoRoad|LodgeNoRoad] + PROBLEM_TIP (D5; C# HubProblemCode 18 / 19). Changed in place: the
// four efficiency rows (PROBLEM[Efficiency|EfficiencyMore|NotRunning|NotRunningMore]: "Welcome Sign ...",
// "and {N} more"), PROBLEM_TIP[Efficiency], RECEIPT[LodgeBaseHalf], EST_TO_NOTE, MONEY_NOTE[Rwh] (the
// "bills less" claim is retired: never measured), RUN_OPEN[Partly], TILE_TIP_PARTLY, and the five longest
// tooltips (PISTE_MAP_TIP, PROBLEM_TIP[NoLift|RunsUnserved|LiftNoPower], TILE_TIP[Attraction]) cut to 25
// words or fewer. Retired: START_NOTE, MONEY_NOTE[Unknown]. F7 fixer: STATUS_LINE_PARTLY (+ _ONE, _EST,
// _ONE_EST), STATUS_TIP_COUNTS and RECEIPT_TIP[Open] count "patchy" runs, not "partly open" ones (one name per
// run state; STATUS_WORD[Partly] "Partly open" stays the area's word).
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
                // --- Resort anchor --------------------------------------------------------
                // Description changed in M1 (hub-u2-spec.md §2.4 "Changed"): the hub, not the sign's
                // upgrade menu, is where everything is built. Round F7 (D6): one line in the vanilla
                // info panel (findings/hub-ux.md §5 item 12; it was 4 lines). Each of the three Round F7
                // descriptions is at most 334 rem in the panel's S font at 100 % text (Overpass advances;
                // the panel is 400 rem wide, its description box narrower: one line at 100 % [likely]).
                { "Assets.NAME[SkiResortGate]", "Ski Area Welcome Sign" },
                { "Assets.DESCRIPTION[SkiResortGate]", "The ski area's entrance. It opens the Ski Resort panel." },

                // --- Lodge (ServiceUpgrade child of the gate) -----------------------------
                // Round F7 (D6): one line (was 2), both keys (the selected-info panel reads UPGRADE_*).
                { "Assets.NAME[SkiAFrameDayLodge]", "A-Frame Day Lodge" },
                { "Assets.DESCRIPTION[SkiAFrameDayLodge]", "A first lodge, where guests arrive." },
                { "Assets.UPGRADE_NAME[SkiAFrameDayLodge]", "A-Frame Day Lodge" },
                { "Assets.UPGRADE_DESCRIPTION[SkiAFrameDayLodge]", "A first lodge, where guests arrive." },

                // --- Lift network ---------------------------------------------------------
                // Round F7 (D6): the base station's description is one line (was 3).
                { "Assets.NAME[SkiBaseStationBuilding]", "Chairlift Base Station" },
                { "Assets.DESCRIPTION[SkiBaseStationBuilding]", "Drive terminal. Connect a low-voltage line to its pole." },
                { "Assets.NAME[SkiTopStationBuilding]", "Chairlift Top Station" },
                {
                    "Assets.DESCRIPTION[SkiTopStationBuilding]",
                    "Return terminal of a fixed-grip chairlift. Power flows up the line from " +
                    "the base — no hookup needed here."
                },
                { "Assets.NAME[SkiChairliftHV]", "Fixed-Grip Chairlift" },
                {
                    "Assets.DESCRIPTION[SkiChairliftHV]",
                    "Draw the lift line like a road: towers pick their own height, chairs " +
                    "ride the cable."
                },

                // --- Runs -----------------------------------------------------------------
                // Round E1 (econ-round spec ch2 §4.6, D17): run asset names in Title Case like every
                // other asset name of ours ("Groomed Run", was "Groomed Ski Run"; "Snowmaking Run", was
                // "Ski Run with Snowmaking"; "Night Run", was "Night Skiing Run"); the natural run (D16)
                // is new. The hub says the sentence-case run types (SkiResort.Hub.RUN_TYPE[*], block (e)).
                { "Assets.NAME[SkiRunGroomedA]", "Groomed Run" },
                {
                    "Assets.DESCRIPTION[SkiRunGroomedA]",
                    "Freeform ski run. Paint the trail within 5 km of a Ski Area Welcome " +
                    "Sign; grooming appears on the mountain and counts toward the " +
                    "resort's attractiveness."
                },
                { "Assets.NAME[SkiRunSnowmakingA]", "Snowmaking Run" },
                {
                    "Assets.DESCRIPTION[SkiRunSnowmakingA]",
                    "A groomed run with snowmaking coverage — holds its season longer and " +
                    "scores attractiveness in marginal weather. Operates within 5 km of a " +
                    "Ski Area Welcome Sign."
                },
                { "Assets.NAME[SkiRunNightA]", "Night Run" },
                {
                    "Assets.DESCRIPTION[SkiRunNightA]",
                    "A snowmaking run with floodlighting for a second, after-dark shift — " +
                    "the most expensive trail to keep open, and the biggest draw. Operates " +
                    "within 5 km of a Ski Area Welcome Sign."
                },
                { "Assets.NAME[SkiRunNaturalA]", "Natural Run" },
                {
                    "Assets.DESCRIPTION[SkiRunNaturalA]",
                    "A marked trail on natural snow. Nothing is groomed or painted: the mountain's " +
                    "own snow is the run, and it opens only when real snow covers it. No monthly " +
                    "cost. Cut the trail through the forest with the tree tool. Links to the nearest " +
                    "Ski Area Welcome Sign within 5 km."
                },

                // --- Run drawing feedback (docs/research/run-feedback-spec.md §5, §9.6) -------
                // Mouse tooltip of SkiRunTooltipSystem while the area tool draws or edits a
                // run. Read through LocalizedString.Id(key) (LocalizedString.cs:39-42); keys
                // are free-form strings, the bracketed band suffix = SlopeBand member name.
                // The pitch / drop / patch labels sit in front of IntTooltip values whose units ("percentage",
                // "length") the UI formats, so they carry no number or unit themselves.
                // R2 (DECISIONS 116): the rating is the crux of the easiest line, so
                // STEEPEST_PITCH ("Steepest 20 m pitch") is retired in favour of CRUX_PITCH;
                // STEEPEST_PATCH labels the old statistic when it is shown as an avoidable patch.
                // APPROX_PITCH (R2 fixer round) replaces CRUX_PITCH when the run outline falls
                // apart (Readout.Connected false): the number is then the P90 fallback, not an
                // unavoidable pitch. Bands follow the amended thresholds 6 / 25 / 40 / 60 % (spec §9.2).
                // U2 (hub-u2-spec.md §2.4 "Changed"): Flat no longer says "unrated" — band -1 in the hub is
                // "Being rated" (SkiResort.Hub.PENDING), so no two states share the word.
                // Round E1 (econ-round spec ch2 §4.6, §6.4, D21): RUN_FEE labels the one-time run fee row
                // (a money IntTooltip value) of SkiRunTooltipSystem.
                { "SkiResort.SlopeBand[Flat]", "Flat (too gentle to rate)" },
                { "SkiResort.SlopeBand[Green]", "Green circle" },
                { "SkiResort.SlopeBand[Blue]", "Blue square" },
                { "SkiResort.SlopeBand[Black]", "Black diamond" },
                { "SkiResort.SlopeBand[DoubleBlack]", "Double black diamond" },
                { "SkiResort.Tooltip.CRUX_PITCH", "Hardest unavoidable pitch" },
                { "SkiResort.Tooltip.APPROX_PITCH", "Typical steep pitch (run outline broken, rating approximate)" },
                { "SkiResort.Tooltip.VERTICAL_DROP", "Vertical drop" },
                { "SkiResort.Tooltip.STEEPEST_PATCH", "Steepest patch (avoidable)" },
                { "SkiResort.Tooltip.RUN_FEE", "Run fee (one-time)" },
                {
                    "SkiResort.Tooltip.NO_GATE_IN_RANGE",
                    "No Ski Area Welcome Sign within 5 km — this run will stay unbound " +
                    "until one is placed"
                },
                // R3 (docs/research/run-binding-glade-spec.md §2 "Locale keys", §5.1): the run's
                // horizontal length, an IntTooltip label in front of a "length" unit value
                // (SkiRunTooltipSystem.KeyLength).
                { "SkiResort.Tooltip.RUN_LENGTH", "Length" },

                // --- Glade tool tooltip (run-binding-glade-spec.md §2 "Locale keys", §4.9, §8.3) --
                // Read by SkiGladeTooltipSystem through LocalizedString.Id(key), built once in
                // OnCreate. Round G3 retired MODE[Corridor|Glade|TreeLine] and VARIANT and added
                // TARGET[Interior|TreeLine], LEVEL, LEVEL_NAME[1..8] (the names of spec §8.2's
                // table, also SkiGladeToolSystem.Tuning.LevelNames for the log), TREES_NOW and
                // TREES_TARGET. PLANT / FELL / REMOVE / BLOCKED_SPOTS / LEVEL / TREES_NOW /
                // TREES_TARGET label IntTooltips (unit "integer"), so they carry no number
                // themselves. Texts are the spec's (final unless Richard edits them), except
                // REMOVE: G2's "Planted trees to replace" became "Planted trees to remove" (G3
                // removes members to lower the density; nothing is replaced), and COMPLETE: "this
                // layout is already planted" became "the run is already at this tree density"
                // (level 1 plants nothing). G3 fixer round (findings tool-2 / behaviour-2; texts
                // are the fixer's, Richard may edit them): COMPLETE_BLOCKED for a complete plan
                // below its target (learned blocked spots used up the stream) and COMPLETE_FIXED
                // for one above it (the trees left inside may not be felled), so COMPLETE is shown
                // only when Trees now = Target.
                { "SkiResort.GladeTooltip.HINT", "Hover a ski run to plan trees" },
                { "SkiResort.GladeTooltip.NO_RUNS", "No ski run in this city is linked to a Welcome Sign yet" },
                { "SkiResort.GladeTooltip.UNBOUND", "Run not linked: a Ski Area Welcome Sign must stand within 5 km of its centre" },
                { "SkiResort.GladeTooltip.BLOCKED", "Trees cannot be planned on this run (see the mod log)" },
                { "SkiResort.GladeTooltip.COMPLETE", "Nothing to change: the run is already at this tree density" },
                { "SkiResort.GladeTooltip.COMPLETE_BLOCKED", "Nothing more can be planted: the remaining spots are blocked (roads, buildings)" },
                { "SkiResort.GladeTooltip.COMPLETE_FIXED", "Nothing to change: the remaining trees cannot be felled (other runs', buildings')" },
                { "SkiResort.GladeTooltip.CLAMPED", "Large run: apply again to continue" },
                { "SkiResort.GladeTooltip.SATURATED", "No room for more trees" },
                { "SkiResort.GladeTooltip.MONEY", "Some trees cancelled — not enough money?" },
                { "SkiResort.GladeTooltip.TARGET[Interior]", "Run interior" },
                { "SkiResort.GladeTooltip.TARGET[TreeLine]", "Tree line" },
                { "SkiResort.GladeTooltip.LEVEL", "Tree density (1-8)" },
                { "SkiResort.GladeTooltip.LEVEL_NAME[1]", "Clear" },
                { "SkiResort.GladeTooltip.LEVEL_NAME[2]", "Scattered trees" },
                { "SkiResort.GladeTooltip.LEVEL_NAME[3]", "Sparse" },
                { "SkiResort.GladeTooltip.LEVEL_NAME[4]", "Open glade" },
                { "SkiResort.GladeTooltip.LEVEL_NAME[5]", "Glade" },
                { "SkiResort.GladeTooltip.LEVEL_NAME[6]", "Open forest" },
                { "SkiResort.GladeTooltip.LEVEL_NAME[7]", "Forest" },
                { "SkiResort.GladeTooltip.LEVEL_NAME[8]", "Thick forest" },
                { "SkiResort.GladeTooltip.TREES_NOW", "Trees now" },
                { "SkiResort.GladeTooltip.TREES_TARGET", "Target" },
                { "SkiResort.GladeTooltip.PLANT", "Trees to plant" },
                { "SkiResort.GladeTooltip.FELL", "Wild trees to fell" },
                { "SkiResort.GladeTooltip.REMOVE", "Planted trees to remove" },
                { "SkiResort.GladeTooltip.BLOCKED_SPOTS", "Spots blocked (roads, buildings)" },

                // --- Ski Resort hub (docs/research/resort-hub-understand.md §5.3 last paragraph,
                // Round U1, implementer U1-B, 2026-10-01) ----------------------------------------
                // The UI module (mod/SkiResortUI) renders every SkiResort.Hub.* key through
                // translate(key, englishFallback); each text below MUST equal its TS fallback (the
                // fallback shows only while a key is missing). GLYPHS: printable ASCII plus the
                // research §2.F.2 safe list only (← → ↑ ↓ … · – × ° ² ≈ NBSP; NO em dash):
                // mod/SkiResortUI/tools/check-bundle.js fails the UI build on any other glyph in a
                // SkiResort.Hub.* value of this file. Editing this file re-runs the UI build
                // (it is an input of the csproj's SkiUiBuild target).
                //
                // (a) src/l10n.ts LOC (U1-A): launcher, connection and error texts.
                { "SkiResort.Hub.TITLE", "Ski Resort" },
                // CONNECTED, CONTRACT_MISMATCH: retired in contract 2 (no longer used by TS or C#; the mismatch
                // text is CONTRACT_MISMATCH_CORE; integrator 2026-10-03, check-bundle rule 4 warned on both).
                { "SkiResort.Hub.LAUNCHER_TOOLTIP", "Ski Resort: runs, resort totals and every building tool" },
                { "SkiResort.Hub.NOT_CONNECTED", "The mod is not answering: its hub system is missing or failed. See SkiResortMod.Mod.log." },
                { "SkiResort.Hub.STAMP_DIFFERS", "The UI bundle comes from a different build than the DLL." },
                { "SkiResort.Hub.UI_ERROR", "The Ski Resort panel hit an error and was closed. Details are in the mod log." },

                // (b) Keys the C# side SENDS (SkiResortHubUISystem / SkiRunCatalogSystem /
                // SkiGladeToolSystem). RUN_DEFAULT[*]: the default run names of OD7, read by
                // SkiRunCatalogSystem from the ACTIVE dictionary (so a translation applies) with
                // "{N}" replaced by the run's number by top height; the UI receives the finished
                // string as RunRow.name. RUN_DEFAULT[Natural] is new in Round E1 (ch2 §4.6, ch5 R-UI-2;
                // the catalog's English fallback "Natural run {N}" is the same text). REASON[*]:
                // build[].reason; GLADE_REASON[*]: glade.reason. HINT[LiftNoPair]: build[lift].hint for
                // the SHOWN area (D37); its text changed in Round E1 (ch5 R-UI-2) and it moved here from
                // block (d).
                { "SkiResort.Hub.RUN_DEFAULT[Groomed]", "Groomed run {N}" },
                { "SkiResort.Hub.RUN_DEFAULT[Snowmaking]", "Snowmaking run {N}" },
                { "SkiResort.Hub.RUN_DEFAULT[Night]", "Night run {N}" },
                { "SkiResort.Hub.RUN_DEFAULT[Natural]", "Natural run {N}" },
                { "SkiResort.Hub.REASON[Locked]", "Not unlocked yet" },
                { "SkiResort.Hub.REASON[NoSign]", "Place a Ski Area Welcome Sign first" },
                { "SkiResort.Hub.REASON[Missing]", "Asset not found: is the Modular Ski Resort asset pack installed?" },
                { "SkiResort.Hub.HINT[LiftNoPair]", "Place the other station first: a lift needs a base and a top station." },
                { "SkiResort.Hub.GLADE_REASON[Disabled]", "The tree tool hit an error and is off until the next load. Details are in the mod log." },
                { "SkiResort.Hub.GLADE_REASON[NotInGame]", "The tree tool works in a loaded city only." },
                { "SkiResort.Hub.GLADE_REASON[NoRuns]", "The ski run assets were not found: is the asset pack installed?" },
                { "SkiResort.Hub.GLADE_REASON[NoSpecies]", "No tree species to plant were found." },

                // (c) src/hub/hubText.ts HUB_LOC (U1-C, the panel), copied as of 2026-10-01 17:26
                // while U1-C was still writing it — the integrator re-diffs hubText.ts against this
                // block before the build. Contract 2 (integrator 2026-10-03, re-diffed by check-bundle rule 4):
                // SECTION[Resort], SECTION[Build], COL_AREA_TIP, RUNS_UNBOUND_HEADER and RUNS_UNBOUND_HINT are
                // retired — the dashboard, the Build footer and the resort switcher's Not linked entry replace them.
                // Contract 3 (Round E1, ch6 §7.4): SECTION[Runs], NO_SIGN, BUILD_HINT_RUN, TREES_TOOL_OFF,
                // TREES_TOOL_ON and RUN_FLY retired; RUNS_EMPTY's text changed (ch6 §7.2).
                { "SkiResort.Hub.SECTION[Trees]", "Trees" },
                { "SkiResort.Hub.SECTION_FAILED", "This part of the panel hit an error. Details are in the mod log." },
                { "SkiResort.Hub.PLACE_SIGN", "Place Welcome Sign" },
                // SIGNS, RUNS_BOUND, RUNS_UNBOUND, LIFTS: removed in contract 2 (hub-u2-spec.md §2.4
                // "Removed": the `summary` binding they labelled is gone; the resort rows replace it).
                { "SkiResort.Hub.AREA", "Skiable area" },
                { "SkiResort.Hub.BUILD_EMPTY", "No building tools were reported. See SkiResortMod.Mod.log." },
                { "SkiResort.Hub.BUILD_ACTIVE", "In use: click again to stop" },
                // Round F6 (C4): the hub shows a ski area -> the lodge goes on its sign and the camera flies
                // there first (SkiResortHubUISystem lodge launch, item A2); BUILD_HINT_LODGE_SELECTED below
                // is the Not linked / mismatch text (src/hub/BuildStrip.tsx picks one by FooterInfo.lodgeShown).
                { "SkiResort.Hub.BUILD_HINT_LODGE", "Goes on this ski area's Welcome Sign. The camera flies there first." },
                { "SkiResort.Hub.BUILD_HINT_LODGE_SELECTED", "Goes on the selected Welcome Sign, or the nearest one." },
                { "SkiResort.Hub.LOCKED", "Not available yet" },
                { "SkiResort.Hub.TREES_FEWER", "Fewer trees" },
                { "SkiResort.Hub.TREES_MORE", "More trees" },
                { "SkiResort.Hub.TREES_APPLY_TO", "Apply to" },
                { "SkiResort.Hub.TREES_UNAVAILABLE", "The tree tool is not available right now." },
                { "SkiResort.Hub.RUNS_EMPTY", "No runs yet. Pick a run type in the Build strip below and draw its outline." },
                { "SkiResort.Hub.COL_LENGTH", "Length" },
                { "SkiResort.Hub.COL_DROP", "Drop" },
                { "SkiResort.Hub.COL_AREA", "Area" },
                { "SkiResort.Hub.COL_LENGTH_TIP", "Horizontal length, estimated from the run's outline" },
                { "SkiResort.Hub.COL_DROP_TIP", "Height difference between the highest and lowest points of the run's outline (approximate)" },

                // (d) Contract 2 (docs/research/hub-u2-spec.md §2.4, Rounds U2/L1/M1, implementer I-4,
                // 2026-10-03). Texts are the spec's, verbatim (final unless Richard edits them); the TS
                // fallbacks (I-3) carry the same text and tools/check-bundle.js rule 4 fails the UI build
                // on any difference. Keys with {X} placeholders are rendered by LocalizedString in TS, never
                // through translate() (gate rule 5), except RESORT_DEFAULT and LIFT_DEFAULT, which C#
                // formats (SkiResortStatsSystem / SkiRunBindTriggerSystem, "{N}" replaced) and sends as a
                // finished name.
                // Contract 3 (Round E1, ch6 §7.4) retired the contract-2 dashboard and list keys (KPI[*],
                // KPI_TIP[*], LODGE_NO_RUNS, NO_LODGE, NO_LODGE_TIP, LIFTS_OPEN, DETAILS, DETAIL[*] but Share,
                // DETAIL_TIP[*], SEASON[*], EST_TIP, CITY_TIP, PAUSED_TIP, TERM[*], TERM_CAP, SNOW_LINE,
                // AIR_TEMP, LODGE_N, TAB[Lifts], FOCUS_HINT, COL_RISE, COL_RISE_TIP, LIFT_STATE[*],
                // BUILD_ORDER, BUILD_PRICE, TREES_IDLE): the five tiles and views of hub v3 replace them.
                // RESORT_PREV, RESORT_NEXT, UNLINKED_ENTRY ("ski area", D17 wording), LIFT_CAPACITY and
                // BUILD_HINT_SIGN changed in place (ch6 §7.2).
                // Resort switcher (title bar, §4.3).
                { "SkiResort.Hub.RESORT_PREV", "Previous ski area" },
                { "SkiResort.Hub.RESORT_NEXT", "Next ski area" },
                { "SkiResort.Hub.RESORT_POSITION", "{N} / {COUNT}" },
                { "SkiResort.Hub.RESORT_DEFAULT", "Ski area {N}" },
                { "SkiResort.Hub.RESORT_FLY", "Fly to the Welcome Sign" },
                { "SkiResort.Hub.UNLINKED_ENTRY", "Not linked to a ski area" },
                { "SkiResort.Hub.UNLINKED_HINT", "More than 5 km from every Welcome Sign. Place a sign nearby to link them." },
                { "SkiResort.Hub.RENAME", "Rename" },
                { "SkiResort.Hub.RENAME_TIP", "Enter or clicking away keeps the name, Esc cancels, an empty name restores the default." },
                // Kept from the contract-2 dashboard (now the Overview's mountain block and receipt).
                { "SkiResort.Hub.DIFFICULTY", "Difficulty" },
                { "SkiResort.Hub.PENDING", "Being rated" },
                // Fixer round (findings u2-data-05 / u2-panel-5): band -2, a rating that ended in no terrain /
                // too large / failed — final, so it must not read "Being rated" forever.
                { "SkiResort.Hub.UNRATEABLE", "Cannot be rated (outline too large or too thin, or no terrain under it)" },
                { "SkiResort.Hub.DETAIL[Share]", "Share of the city's tourist draw" },
                // Round F6 (C3, attraction-honesty D8): the target-tourists row's own tooltip.
                { "SkiResort.Hub.EST_TOURISTS_TIP", "Worked out with the game's own tourist formula, not counted. Tourism mods can replace that formula, so real arrivals may differ." },
                // Lists (now the Runs and Lifts views).
                { "SkiResort.Hub.LIFTS_EMPTY", "No lifts yet. Place a base station and a top station facing each other, then draw the lift line between them." },
                { "SkiResort.Hub.LIFT_DEFAULT", "Lift {N}" },
                { "SkiResort.Hub.LIFT_SECTIONS", "Sections" },
                { "SkiResort.Hub.LIFT_TOWERS", "Towers" },
                { "SkiResort.Hub.LIFT_CHAIRS", "Chairs" },
                // Fixer round (finding u2-data-06): chairs run on the simulation clock, so the hour is one at
                // normal speed (spec §2.4's "real time" was wrong at 2× / 3× and while paused). Round E1: a
                // design figure (D34 honest labels; the game does not simulate riders).
                { "SkiResort.Hub.LIFT_CAPACITY", "Design capacity (riders an hour at 1× game speed)" },
                { "SkiResort.Hub.LIFT_TOWER_REMOVED", "A tower was removed by another tool. Redraw a section next to it to restore it." },
                { "SkiResort.Hub.LIFT_STRAIGHT", "Stations straight (180°)" },
                // Fixer round (finding behaviour-4): a lift without a base station stores its name on the top.
                { "SkiResort.Hub.LIFT_NAME_TIP", "The name is stored on the base station (on the top station while the lift has no base)" },
                { "SkiResort.Hub.LIFT_SIGN_TIP", "Lifts belong to the nearest Welcome Sign within 5 km of their base station; runs keep the sign they were drawn for" },
                // Build strip (§4.2 BuildStrip, §5.8).
                { "SkiResort.Hub.BUILD_HINT_SIGN", "The sign is the ski area: runs and lifts within 5 km belong to it." },
                { "SkiResort.Hub.BUILD_HINT_BASE", "Place at the bottom of the lift." },
                { "SkiResort.Hub.BUILD_HINT_TOP", "Place at the top, facing the base station. A guide shows when it is straight." },
                { "SkiResort.Hub.BUILD_HINT_LIFT", "Draw from station to station. On steep ground, redraw one section at a time." },
                // Round F6 (C4): an extra TOOLTIP line of the base, top and lift tiles (the footer's two
                // one-string lines would cut it at 130-150 % text; src/hub/BuildStrip.tsx header).
                { "SkiResort.Hub.BUILD_HINT_LIFT_SIGN", "Lifts join the nearest Welcome Sign within 5 km of their base station." },
                // The link states (§4.5).
                { "SkiResort.Hub.CONTRACT_MISMATCH_CORE", "This panel (version {UI}) and the mod's DLL (version {DLL}) come from different builds. Building still works; the resort statistics are hidden until the mod is reinstalled." },

                // (e) Contract 3 (Round E1, econ-round spec ch6 §7): NEW hub keys; each text equals its hubText.ts tuple (gate R4).
                // One line per NEW row of ch6 §7.2, in that order (= src/hub/hubText.ts order): HUB_LOC, then
                // HUB_FMT (placeholder entries: rendered only by LocalizedString), then the family maps
                // STATUS_WORD, SEASON_HINT, SEASON_TIP, LIFT_STATUS, RUN_TYPE, BUILD_SHORT[run kinds], NEXT, NEXT2.
                // Texts are final unless Richard edits them (DECISIONS 132: the owner reads new player text).
                { "SkiResort.Hub.START_TITLE", "Start a ski area" },
                { "SkiResort.Hub.START_STEP[1]", "Place a Welcome Sign" },
                { "SkiResort.Hub.START_STEP[2]", "Connect the sign: road, power, water and sewage" },
                { "SkiResort.Hub.START_STEP[3]", "Add a lodge to the sign" },
                { "SkiResort.Hub.START_STEP[4]", "Build a lift: two stations, then the line" },
                { "SkiResort.Hub.START_STEP[5]", "Draw runs from the top of the lift" },
                // START_NOTE retired in Round F7 (D6): footer line 2 (BUILD_HINT_SIGN) says it.
                { "SkiResort.Hub.STATUS_NO_RUNS", "Closed · no runs yet" },
                { "SkiResort.Hub.STATUS_UNLINKED", "Not linked to a ski area" },
                { "SkiResort.Hub.STATUS_TIP_RULE", "A run is open when snow (2 cm or more) covers 60 % of it, and closed below 40 %. In between it counts in part." },
                { "SkiResort.Hub.STATUS_TIP_MEASURED", "Snow is measured on each run from the game's own snow." },
                { "SkiResort.Hub.STATUS_TIP_ESTIMATED", "Some runs are estimated from the day's temperature until their snow can be measured." },
                { "SkiResort.Hub.STATUS_TIP_GATING", "Snow & season gating is off in Options, so every run counts as open." },
                { "SkiResort.Hub.STATUS_TIP_VISUALS", "Snow is hidden in your Gameplay settings. Runs still open on the simulated snow." },
                { "SkiResort.Hub.PROBLEM[None]", "Nothing needs your attention." },
                { "SkiResort.Hub.PROBLEM[Unlinked]", "More than 5 km from every Welcome Sign" },
                { "SkiResort.Hub.PROBLEM[NoLodge]", "No lodge: guests have nowhere to arrive" },
                { "SkiResort.Hub.PROBLEM[LodgesAllOff]", "All lodges are switched off" },
                { "SkiResort.Hub.PROBLEM[NoRuns]", "No runs yet: draw one to open" },
                // Round F7 (D5): the two road rows (C# HubProblemCode SignNoRoad 18, LodgeNoRoad 19).
                { "SkiResort.Hub.PROBLEM[SignNoRoad]", "Welcome Sign is not at a road" },
                { "SkiResort.Hub.PROBLEM[LodgeNoRoad]", "Lodge is not at a road: guests cannot reach it" },
                { "SkiResort.Hub.PROBLEM_TIP[NoLodge]", "Guests arrive at a lodge, so a ski area without one has an attraction of 0. Place a lodge on the Welcome Sign with the lodge tile in the Build strip." },
                { "SkiResort.Hub.PROBLEM_TIP[LodgesAllOff]", "A lodge that is switched off does not count. With none left, guests have nowhere to arrive and the attraction is 0. Switch one on in the Welcome Sign's info panel (Upgrades)." },
                { "SkiResort.Hub.PROBLEM_TIP[LodgesSomeOff]", "A lodge that is switched off does not count in the lodge base." },
                // Round F7 (D4): no longer tells the player to connect a sign that is connected.
                { "SkiResort.Hub.PROBLEM_TIP[Efficiency]", "The Welcome Sign needs staff, power, water and sewage. Its info panel shows which is short." },
                { "SkiResort.Hub.PROBLEM_TIP[LiftNoPower]", "Its base station gets no electricity, so the lift adds nothing and its runs count as having no lift. Connect a low-voltage line to it." },
                { "SkiResort.Hub.PROBLEM_TIP[LiftSectionMissing]", "A section of the line is being redrawn or was removed. The lift counts again as soon as the line is closed; until then it costs a quarter of its running cost." },
                { "SkiResort.Hub.PROBLEM_TIP[LiftUnfinished]", "The line does not join a base station and a top station yet." },
                { "SkiResort.Hub.PROBLEM_TIP[NoRuns]", "Pick a run type in the Build strip and draw its outline within 5 km of the Welcome Sign." },
                { "SkiResort.Hub.PROBLEM_TIP[Stations]", "A station is placed, but no line joins it to its partner yet." },
                { "SkiResort.Hub.PROBLEM_TIP[LiftChairsStopped]", "The lift is complete and still counts, but its chairs stopped. The mod log says why." },
                { "SkiResort.Hub.PROBLEM_TIP[RunsUnrateable]", "Their outline is too large or too thin, or there is no terrain under it. Such a run counts very little." },
                { "SkiResort.Hub.PROBLEM_TIP[RunsRating]", "Their difficulty is being worked out, one run at a time. A new run counts 0 until then; a reshaped run keeps its last rating." },
                { "SkiResort.Hub.PROBLEM_TIP[RunsFlat]", "A run rated Flat adds nothing to the score. Draw it on steeper ground." },
                { "SkiResort.Hub.PROBLEM_TIP[FreeLodges]", "These lodges are not on a Welcome Sign, so they count nowhere." },
                // Round F7 (D5): the tooltip's first line is the row ("Welcome Sign is not at a road" /
                // "Lodge is not at a road: ..."), so "it" names the building; the fix hints come from
                // findings/sign-utilities.md §5 and ped-access.md §2.4 / §4.2 (the deck side is the lot front;
                // the game links a road within 8.4 m of the front point).
                { "SkiResort.Hub.PROBLEM_TIP[SignNoRoad]", "Move it so its front touches a road. Power, water and sewage come from that road." },
                { "SkiResort.Hub.PROBLEM_TIP[LodgeNoRoad]", "Move it so its deck side touches a road. Guests walk in from that road." },
                { "SkiResort.Hub.PROBLEM_MORE_TIP", "Also needs attention:" },
                { "SkiResort.Hub.PROBLEM_CLICK_FLY", "Click to fly there." },
                { "SkiResort.Hub.PROBLEM_CLICK_LIST", "Click to open the list." },
                { "SkiResort.Hub.TILE[Attraction]", "Attraction" },
                { "SkiResort.Hub.TILE[Runs]", "Runs" },
                { "SkiResort.Hub.TILE[RunsOpen]", "Runs open" },
                { "SkiResort.Hub.TILE[Lifts]", "Lifts" },
                { "SkiResort.Hub.TILE[Guests]", "Guests" },
                { "SkiResort.Hub.TILE[Money]", "Money" },
                { "SkiResort.Hub.TILE_TIP[Attraction]", "The ski area's score on the game's attractiveness scale (large park about 10, landmark 120). Its lodges carry it, though their own panel shows 0." },
                { "SkiResort.Hub.TILE_TIP_HELD", "Checking runs and snow after loading. The game keeps the saved score meanwhile." },
                { "SkiResort.Hub.TILE_TIP[Runs]", "Runs fully open now, of all runs of this ski area." },
                { "SkiResort.Hub.TILE_TIP[Lifts]", "Lifts that count (complete, with power at the base station), of the complete lifts. Without power the chairs still move, but the lift adds nothing." },
                { "SkiResort.Hub.TILE_TIP[Guests]", "Guests who arrived this month: tourists at the lodges and the Welcome Sign, and residents visiting a lodge." },
                { "SkiResort.Hub.TILE_TIP_SINCE_LOAD", "Counted since you loaded the city." },
                { "SkiResort.Hub.TILE_TIP[Money]", "Resort money a month: lift tickets minus running costs and the game's bill for buildings and wages, at today's rates. What tourists spend in your city's shops and hotels is not included." },
                { "SkiResort.Hub.TILE_TIP_EST", "≈ estimated: less than a full month of tickets has been counted." },
                { "SkiResort.Hub.TILE_TIP_EST_BUILDINGS", "≈ The game's bill for buildings and wages is not known yet." },
                { "SkiResort.Hub.TILE_TIP_OPEN", "Click to show it below." },
                { "SkiResort.Hub.SECTION[Attraction]", "Attraction" },
                { "SkiResort.Hub.SCORE_TICK[Attraction]", "tourist attraction 50" },
                { "SkiResort.Hub.SCORE_TICK[Landmark]", "landmark 120" },
                { "SkiResort.Hub.RECEIPT[LodgeBase]", "Lodge base" },
                { "SkiResort.Hub.RECEIPT[LodgeBaseHalf]", "Lodge base (half until a run counts)" },
                { "SkiResort.Hub.RECEIPT[LodgeBaseNone]", "Lodge base (no lodge)" },
                { "SkiResort.Hub.RECEIPT_TIP[LodgeBase]", "20 for the first lodge, 4 more for each extra one, up to 12 more. A lodge that is switched off does not count. Half while no run counts (none open, or none worth anything yet): summer visitors only." },
                { "SkiResort.Hub.RECEIPT[Terrain]", "Runs and lifts" },
                { "SkiResort.Hub.RECEIPT_TIP[Runs]", "Runs: the sum of each run's value now (difficulty, size, type, lift and snow). Each run's tooltip in the Runs view shows its own." },
                { "SkiResort.Hub.RECEIPT_TIP[Lifts]", "Lifts: 6 for each complete lift with power; a quarter of that while no run counts." },
                { "SkiResort.Hub.RECEIPT_TIP[Variety]", "Variety: 15 % more while a green, a blue and a black (or harder) run all count." },
                { "SkiResort.Hub.RECEIPT_OPEN_NONE", "Runs open now: –" },
                { "SkiResort.Hub.RECEIPT[Efficiency]", "Welcome Sign efficiency" },
                { "SkiResort.Hub.RECEIPT_TIP[Efficiency]", "The game's efficiency of the Welcome Sign: staff, power, water, sewage, budget and more, as on its info panel. The score is multiplied by it." },
                { "SkiResort.Hub.RECEIPT[Result]", "Attraction" },
                { "SkiResort.Hub.RECEIPT[ResultNoLodge]", "Attraction (no lodge: 0)" },
                { "SkiResort.Hub.RECEIPT_TIP[Result]", "(Lodge base + runs and lifts) × efficiency, at most 200. Each active lodge carries an equal part, so the game counts the ski area once. The game's own panel of a lodge shows Attractiveness 0; the ski area's score is this one, carried by its lodges." },
                { "SkiResort.Hub.SHARE_TIP", "This ski area's part of all attraction in the city, counted as the game counts it (the square of each building's score). It sets how many tourists come to the city, not where they go." },
                { "SkiResort.Hub.SECTION[Mountain]", "Mountain" },
                { "SkiResort.Hub.MOUNTAIN[Vertical]", "Vertical" },
                { "SkiResort.Hub.MOUNTAIN_TIP[Vertical]", "From the lowest run bottom to the highest run top. ≈ while runs are being rated." },
                { "SkiResort.Hub.AREA_TIP", "The plan area of all runs of this ski area." },
                { "SkiResort.Hub.MOUNTAIN[Snow]", "Snow now" },
                { "SkiResort.Hub.MOUNTAIN[SnowMeasured]", "Snow now (measured)" },
                { "SkiResort.Hub.MOUNTAIN[SnowEstimated]", "Snow now (estimated)" },
                { "SkiResort.Hub.MOUNTAIN_TIP[Snow]", "Share of the run area with at least 2 cm of snow." },
                { "SkiResort.Hub.UNLINKED_NOTE", "These runs and lifts belong to no ski area, so they score nothing and draw no guests." },
                { "SkiResort.Hub.TREE_TOOL", "Tree tool" },
                { "SkiResort.Hub.TREE_TOOL_TIP", "Plan trees on and around your runs: turn the tool on, hover a run and click." },
                { "SkiResort.Hub.TREE_TOOL_TIP_ON", "The tree tool is on. Click to turn it off." },
                { "SkiResort.Hub.PISTE_MAP", "Piste map" },
                { "SkiResort.Hub.PISTE_MAP_TIP", "Draws this ski area's runs on the ground, in difficulty colours, while the panel is open. Natural: dashed. Too flat: grey. Cannot be rated: orange." },
                { "SkiResort.Hub.COL_LIFT", "Lift" },
                { "SkiResort.Hub.COL_LIFT_TIP", "↑: a running lift reaches the top of this run." },
                { "SkiResort.Hub.RUN_ESTIMATED", "Estimated from the day's temperature." },
                { "SkiResort.Hub.RUN_OPEN_GATING", "Counts as open: snow rules are off." },
                { "SkiResort.Hub.RUN_OPEN_NONE", "Not linked: no snow state." },
                { "SkiResort.Hub.RUN_SERVED_ANON", "A running lift reaches its top." },
                { "SkiResort.Hub.RUN_TYPE_NOTE", "Natural green and blue runs count less; a groomed run counts in full." },
                { "SkiResort.Hub.RUN_VALUE", "Score value" },
                { "SkiResort.Hub.RUN_COST", "Running cost" },
                { "SkiResort.Hub.RUN_ACTIONS", "Click to fly there. The pen renames it." },
                { "SkiResort.Hub.LIFT_LINE2[SectionMissing]", "A section is missing: redraw it with the lift line tool" },
                { "SkiResort.Hub.LIFT_LINE2[Unfinished]", "Not connected to a station at both ends yet" },
                { "SkiResort.Hub.LIFT_LINE2[StationsPair]", "Both stations placed: draw the lift line between them" },
                { "SkiResort.Hub.LIFT_LINE2[StationsBaseOnly]", "Base station placed: place the top station facing it" },
                { "SkiResort.Hub.LIFT_LINE2[StationsTopOnly]", "Top station placed: place the base station facing it" },
                { "SkiResort.Hub.LIFT_RIDE", "Ride time" },
                { "SkiResort.Hub.LIFT_RIDE_TIP", "Half the cable loop at 2.5 m/s, at 1× game speed." },
                { "SkiResort.Hub.LIFT_CAPACITY_TIP", "4-seat chairs at 2.5 m/s, spaced as built. The game does not simulate riders." },
                { "SkiResort.Hub.LIFT_RUNS", "Runs it serves" },
                { "SkiResort.Hub.LIFT_COST", "Running cost" },
                { "SkiResort.Hub.LIFT_COST_REDUCED[NoPower]", "Billed at 25 %: no power" },
                { "SkiResort.Hub.LIFT_COST_REDUCED[Unfinished]", "Billed at 25 % while unfinished" },
                { "SkiResort.Hub.LIFT_COST_REDUCED[SectionMissing]", "Billed at 25 % while a section is missing" },
                { "SkiResort.Hub.LIFT_ACTIONS", "Click to fly there (to the gap if a section is missing). The pen renames it." },
                { "SkiResort.Hub.LIFTS_TOTAL_LENGTH", "Total lift length" },
                { "SkiResort.Hub.LIFTS_TOTAL_CAPACITY", "Design capacity, all lifts" },
                { "SkiResort.Hub.SECTION[Guests]", "Guests" },
                { "SkiResort.Hub.GUESTS_MONTH[This]", "This month" },
                { "SkiResort.Hub.GUESTS_MONTH[Last]", "Last month" },
                { "SkiResort.Hub.GUESTS_ROW[Tourists]", "Tourists" },
                { "SkiResort.Hub.GUESTS_ROW[Groups]", "Tourist groups" },
                { "SkiResort.Hub.GUESTS_ROW[Residents]", "Residents" },
                { "SkiResort.Hub.GUESTS_ROW[Paid]", "Paid a lift ticket" },
                { "SkiResort.Hub.GUESTS_ROW[Tickets]", "Lift tickets" },
                { "SkiResort.Hub.GUESTS_TIP[Tourists]", "People in tourist groups that arrived at this ski area's lodges or Welcome Sign." },
                { "SkiResort.Hub.GUESTS_TIP[Groups]", "Tourists arrive in groups; each group counts once." },
                { "SkiResort.Hub.GUESTS_TIP[Residents]", "Residents of your city who came to a lodge for leisure." },
                { "SkiResort.Hub.GUESTS_TIP[Tickets]", "Paid by each guest's household into your city's balance." },
                { "SkiResort.Hub.GUESTS_LAST_NA", "Not counted: guests are counted only from the moment the city loads." },
                { "SkiResort.Hub.EST_ROW[Tourists]", "Target tourists from this ski area (estimated)" },
                { "SkiResort.Hub.EST_TO_NOTE", "Detected: Tourism Overhaul. It sets tourist numbers." },
                { "SkiResort.Hub.EST_TO_TIP", "Tourism Overhaul sets tourist numbers in this city, so no estimate is shown." },
                { "SkiResort.Hub.GUESTS_NOTE[Where]", "Counted when tourists arrive at this ski area's lodge or Welcome Sign, and when residents come to a lodge for leisure." },
                { "SkiResort.Hub.GUESTS_NOTE[SinceLoad]", "Counting started when you loaded the city, partway through this month." },
                { "SkiResort.Hub.TICKETS_NOTE[Off]", "Lift tickets are off (Options: Lift ticket price is 0)." },
                { "SkiResort.Hub.TICKETS_NOTE[NoEfficiency]", "No tickets now: the Welcome Sign is not running." },
                { "SkiResort.Hub.TICKETS_NOTE[NothingOpen]", "No tickets now: no run counts and no lift is running." },
                { "SkiResort.Hub.TICKETS_NOTE[NotKnownYet]", "No tickets yet: the runs are still being checked after loading." },
                { "SkiResort.Hub.SECTION[Money]", "Money" },
                { "SkiResort.Hub.MONEY_COL", "A month" },
                { "SkiResort.Hub.MONEY_TICKETS[Unknown]", "Lift tickets: counted after a quarter month" },
                { "SkiResort.Hub.MONEY_TICKETS[Off]", "Lift tickets: off" },
                { "SkiResort.Hub.MONEY_TICKETS_TIP", "Last month's ticket money once a whole month has been counted. Before that, this month so far scaled to a whole month (≈). The count is the guests who paid, each at the price of the moment they arrived." },
                { "SkiResort.Hub.SECTION[Costs]", "Running costs" },
                { "SkiResort.Hub.MONEY_ROW[Buildings]", "Buildings and wages (game formula)" },
                { "SkiResort.Hub.MONEY_NET", "Net a month" },
                { "SkiResort.Hub.MONEY_TIP[Natural]", "Natural runs have no running cost." },
                { "SkiResort.Hub.MONEY_TIP[Net]", "Ticket income minus the running costs at today's rates. What tourists spend in your city's shops and hotels is not included." },
                { "SkiResort.Hub.MONEY_NOTE[Panel]", "Resort money is not in the game's Economy panel; it goes straight to your balance. Buildings and wages are billed by the game under Parks & Recreation." },
                { "SkiResort.Hub.MONEY_NOTE[Rwh]", "Detected: Realistic Workplaces. The real bill can differ." },
                { "SkiResort.Hub.MONEY_TIP[Rwh]", "Realistic Workplaces And Households changes staff and service costs, so the bill can differ." },
                { "SkiResort.Hub.MONEY_NOTE[Est]", "≈ Tickets are projected from this month so far until a full month has been counted." },
                // MONEY_NOTE[Unknown] retired in Round F7 (D2): the tickets row says "counted after a quarter month".
                { "SkiResort.Hub.UNLINKED_MONEY", "Runs that are not linked cost nothing and earn nothing; their lifts still cost money to run." },
                { "SkiResort.Hub.BUILD_SHORT[Lodge]", "Goes on this ski area's Welcome Sign; the camera flies there." },
                { "SkiResort.Hub.BUILD_SHORT[Top]", "Place at the top, facing the base station." },
                { "SkiResort.Hub.BUILD_SHORT[Lift]", "Draw from station to station, one section at a time." },
                { "SkiResort.Hub.BUILD_TIP_ONCE", "One-time" },
                { "SkiResort.Hub.BUILD_TIP_MONTH", "Each month" },
                { "SkiResort.Hub.BUILD_MONTH_NONE", "Nothing" },
                { "SkiResort.Hub.BUILD_MONTH_GAME", "Upkeep and wages, billed by the game" },
                { "SkiResort.Hub.BUILD_MONTH_STATION", "Part of its lift's running cost" },
                { "SkiResort.Hub.BUILD_TIP_RESHAPE", "Reshaping a run later costs nothing more." },
                { "SkiResort.Hub.BUILD_TIP_LINK", "Runs link to the nearest Welcome Sign within 5 km." },
                { "SkiResort.Hub.BUILD_TIP_NATURAL", "Nothing is drawn on the ground: find it with the piste map." },
                { "SkiResort.Hub.BUILD_TIP_LODGE", "A new lodge needs staff, power and water; until it has them, the sign's efficiency, and so the attraction, can fall." },
                { "SkiResort.Hub.BUILD_TIP_TYPE[Natural]", "Score: greens count half, blues three quarters, blacks in full. It opens only when natural snow covers it." },
                { "SkiResort.Hub.STATUS_LINE", "{STATE} · {OPEN} of {TOTAL} runs open" },
                { "SkiResort.Hub.STATUS_LINE_ONE", "{STATE} · {OPEN} of 1 run open" },
                { "SkiResort.Hub.STATUS_LINE_EST", "{STATE} · ≈ {OPEN} of {TOTAL} runs open" },
                { "SkiResort.Hub.STATUS_LINE_ONE_EST", "{STATE} · ≈ {OPEN} of 1 run open" },
                { "SkiResort.Hub.STATUS_LINE_PARTLY", "{STATE} · {PARTLY} of {TOTAL} runs patchy" },
                { "SkiResort.Hub.STATUS_LINE_PARTLY_ONE", "{STATE} · {PARTLY} of 1 run patchy" },
                { "SkiResort.Hub.STATUS_LINE_PARTLY_EST", "{STATE} · ≈ {PARTLY} of {TOTAL} runs patchy" },
                { "SkiResort.Hub.STATUS_LINE_PARTLY_ONE_EST", "{STATE} · ≈ {PARTLY} of 1 run patchy" },
                { "SkiResort.Hub.STATUS_TIP_COUNTS", "Open {OPEN} · patchy {PARTLY} · closed {CLOSED}" },
                { "SkiResort.Hub.STATUS_TIP_TEMP", "Day average at the top of the highest run: {TEMP}" },
                { "SkiResort.Hub.PROBLEM[LodgesSomeOff]", "Lodges switched off: {N}" },
                { "SkiResort.Hub.PROBLEM[Efficiency]", "Welcome Sign at {PCT}: {REASON}" },
                { "SkiResort.Hub.PROBLEM[EfficiencyMore]", "Welcome Sign at {PCT}: {REASON} and {N} more" },
                { "SkiResort.Hub.PROBLEM[NotRunning]", "Welcome Sign not running: {REASON}" },
                { "SkiResort.Hub.PROBLEM[NotRunningMore]", "Welcome Sign not running: {REASON} and {N} more" },
                { "SkiResort.Hub.PROBLEM[LiftNoPower]", "{NAME} has no power: it counts 0" },
                { "SkiResort.Hub.PROBLEM[LiftSectionMissing]", "{NAME} is missing a section: it counts 0" },
                { "SkiResort.Hub.PROBLEM[LiftUnfinished]", "{NAME} is unfinished" },
                { "SkiResort.Hub.PROBLEM[NoLift]", "No lift yet: runs count {PCT} without one" },
                { "SkiResort.Hub.PROBLEM[StationsPair]", "{NAME}: draw the lift line" },
                { "SkiResort.Hub.PROBLEM[StationsBaseOnly]", "{NAME}: place its top station" },
                { "SkiResort.Hub.PROBLEM[StationsTopOnly]", "{NAME}: place its base station" },
                { "SkiResort.Hub.PROBLEM[LiftChairsStopped]", "{NAME}: the chairs are not moving" },
                { "SkiResort.Hub.PROBLEM[RunsUnserved]", "Runs without a lift: {N} (they count {PCT})" },
                { "SkiResort.Hub.PROBLEM[RunsUnrateable]", "Runs that cannot be rated: {N}" },
                { "SkiResort.Hub.PROBLEM[RunsRating]", "Runs being rated: {N}" },
                { "SkiResort.Hub.PROBLEM[RunsFlat]", "Runs too flat to score: {N}" },
                { "SkiResort.Hub.PROBLEM[FreeLodges]", "Lodges on no Welcome Sign: {N}" },
                { "SkiResort.Hub.PROBLEM_MORE", "+{N}" },
                { "SkiResort.Hub.PROBLEM_TIP[NoLift]", "A run counts {PCT} until a running lift reaches its top: within 150 m of the line, at most 15 m above the top station." },
                { "SkiResort.Hub.PROBLEM_TIP[RunsUnserved]", "Each counts {PCT} until a running lift reaches its top: within 150 m of the line, at most 15 m above the top station." },
                { "SkiResort.Hub.PROBLEM_GAIN[Efficiency]", "At full efficiency the attraction would be {FULL}." },
                { "SkiResort.Hub.PROBLEM_GAIN[Lodge]", "With a lodge, the attraction would be {A}." },
                { "SkiResort.Hub.PROBLEM_GAIN[LodgesOn]", "With them switched on, the attraction would be {A}." },
                { "SkiResort.Hub.STAFF_LINE", "Staff: {FILLED} of {JOBS}" },
                { "SkiResort.Hub.TILE_TIP_FULL", "At full efficiency: {N}" },
                { "SkiResort.Hub.TILE_TIP_PARTLY", "Patchy: {N}" },
                { "SkiResort.Hub.TILE_TIP_UNFINISHED", "Not complete (a section missing, unfinished, or stations only): {N}" },
                { "SkiResort.Hub.TILE_TIP_LAST", "Last month: {N}" },
                { "SkiResort.Hub.SCORE_OF", "{A} of 200" },
                { "SkiResort.Hub.SCORE_BAR_TIP", "{NOW} now. {FULL} at full efficiency. The ticks show what game buildings score: a tourist attraction about 50, a landmark about 120." },
                { "SkiResort.Hub.RECEIPT_TIP_LODGES", "Lodges counted: {N}" },
                { "SkiResort.Hub.RECEIPT_TIP[Terrain]", "{K} × the square root of ((runs + lifts) × variety × vertical). {K} is the Attractiveness scale option. Adding a run never lowers it." },
                { "SkiResort.Hub.RECEIPT_PARTS", "Runs {RUNS} · lifts {LIFTS} · variety ×{VARIETY} · vertical ×{VERTICAL}" },
                { "SkiResort.Hub.RECEIPT_TIP[Vertical]", "Vertical: up to double at 600 m, from the lowest bottom to the highest top of the runs that count now ({M})." },
                { "SkiResort.Hub.RECEIPT_OPEN", "Runs open now: {PCT} of their full value" },
                { "SkiResort.Hub.RECEIPT_TIP[Open]", "Closed runs add nothing and patchy runs add part, so snow moves the score. With every run fully open, runs would add {FULL}." },
                { "SkiResort.Hub.RECEIPT_TIMES", "× {PCT}" },
                { "SkiResort.Hub.DIFF_TIP_PENDING", "Being rated: {N}" },
                { "SkiResort.Hub.DIFF_TIP_UNRATEABLE", "Cannot be rated: {N}" },
                { "SkiResort.Hub.AREA_HA", "{HA} ha" },
                { "SkiResort.Hub.SNOW_DEPTH", "Average depth on the measured runs: {CM} cm" },
                { "SkiResort.Hub.RUN_OPEN[Open]", "Open: snow on {PCT} of it" },
                { "SkiResort.Hub.RUN_OPEN[Partly]", "Patchy: snow on {PCT} of it" },
                { "SkiResort.Hub.RUN_OPEN[Closed]", "Closed: snow on {PCT} of it" },
                { "SkiResort.Hub.RUN_SERVED", "Served by {NAME}" },
                { "SkiResort.Hub.RUN_NOT_SERVED", "No running lift reaches its top: it counts {PCT}" },
                { "SkiResort.Hub.RUN_VALUE_OF", "{NOW} of {FULL}" },
                { "SkiResort.Hub.RUN_VALUE_PARTS", "difficulty {B} × size {S} × type {T} × lift {L} × open {O}" },
                { "SkiResort.Hub.LIFT_DETAIL", "{LENGTH} · rise {RISE} · {MIN} min · {CAP}/h design · {N} runs" },
                { "SkiResort.Hub.LIFT_DETAIL_ONE", "{LENGTH} · rise {RISE} · {MIN} min · {CAP}/h design · 1 run" },
                { "SkiResort.Hub.LIFT_DETAIL_NONE", "{LENGTH} · rise {RISE} · {MIN} min · {CAP}/h design · no runs" },
                { "SkiResort.Hub.LIFT_RIDE_MIN", "{MIN} min" },
                { "SkiResort.Hub.CAPACITY_VALUE", "{CAP} riders/h" },
                { "SkiResort.Hub.GUESTS_TIP[Paid]", "{GUESTS} guests this month: {PAID} paid, {UNPAID} could not pay, {FREE} came in free (tickets off or the ski area closed)." },
                { "SkiResort.Hub.TICKETS_NOTE[Open]", "Each guest pays {PRICE} on arrival (Options: Lift ticket price)." },
                { "SkiResort.Hub.MONEY_TICKETS[Last]", "Lift tickets last month: {N} paid" },
                { "SkiResort.Hub.MONEY_TICKETS[Est]", "Lift tickets (estimated): {N} paid" },
                { "SkiResort.Hub.MONEY_TICKETS_PRICE", "Ticket price now: {PRICE}" },
                { "SkiResort.Hub.MONEY_ROW[Natural]", "Natural runs · {HA} ha" },
                { "SkiResort.Hub.MONEY_ROW[Groomed]", "Groomed runs · {HA} ha with snow" },
                { "SkiResort.Hub.MONEY_ROW[Snowmaking]", "Snowmaking runs · {HA} ha with snow" },
                { "SkiResort.Hub.MONEY_ROW[Night]", "Night runs · {HA} ha with snow" },
                { "SkiResort.Hub.MONEY_ROW[Lifts]", "Lifts · {N} · {LEN} of line" },
                { "SkiResort.Hub.MONEY_TIP[Runs]", "{RATE} per hectare a month where the run holds managed snow now. It moves with the temperature through the day." },
                { "SkiResort.Hub.MONEY_TIP[Lifts]", "{BASE} a month for each complete lift, plus {PERKM} per km of line; a quarter while unfinished or without power. Now {FULL} at the full rate and {REDUCED} at a quarter." },
                { "SkiResort.Hub.MONEY_TIP[Buildings]", "The game's own formula for the lodges and the Welcome Sign at the current Parks & Recreation budget: upkeep {UPKEEP}, wages {WAGES} for {FILLED} of {JOBS} staff." },
                { "SkiResort.Hub.BUILD_LINE_RUN", "{NAME} · {FEE} + {PERHA} per ha" },
                { "SkiResort.Hub.BUILD_LINE_COST", "{NAME} · {COST}" },
                { "SkiResort.Hub.BUILD_LINE_LIFT", "{NAME} · about {PERKM} per km of line" },
                { "SkiResort.Hub.BUILD_ONCE_RUN", "{FEE} + {PERHA} per ha" },
                { "SkiResort.Hub.BUILD_ONCE_LIFT", "about {PERKM} per km of line" },
                { "SkiResort.Hub.BUILD_MONTH_RUN", "{RATE} per ha with snow" },
                { "SkiResort.Hub.BUILD_MONTH_LIFT", "{BASE} per complete lift + {PERKM} per km of line" },
                { "SkiResort.Hub.FOLLOW", "Now showing {NAME}." },
                { "SkiResort.Hub.STATUS_WORD[Open]", "Open" },
                { "SkiResort.Hub.STATUS_WORD[Partly]", "Partly open" },
                { "SkiResort.Hub.STATUS_WORD[Closed]", "Closed" },
                // Round F7 (D1): the Runs view's per-run state word (hubText.ts RUN_STATE_TEXT).
                { "SkiResort.Hub.RUN_STATE[Closed]", "Closed" },
                { "SkiResort.Hub.RUN_STATE[Patchy]", "Patchy" },
                { "SkiResort.Hub.RUN_STATE[Open]", "Open" },
                { "SkiResort.Hub.SEASON_HINT[SnowSeason]", "Snow season" },
                { "SkiResort.Hub.SEASON_HINT[SnowGunsOnly]", "Snow guns only" },
                { "SkiResort.Hub.SEASON_HINT[Thawing]", "Thawing" },
                { "SkiResort.Hub.SEASON_HINT[WaitingForSnow]", "Waiting for snow" },
                { "SkiResort.Hub.SEASON_HINT[TooWarm]", "Too warm for snow" },
                { "SkiResort.Hub.SEASON_HINT[GatingOff]", "Snow rules off" },
                { "SkiResort.Hub.SEASON_TIP[SnowSeason]", "Cold enough for natural snow at the top." },
                { "SkiResort.Hub.SEASON_TIP[SnowGunsOnly]", "Too warm for natural snow at the top, but cold enough for snow guns." },
                { "SkiResort.Hub.SEASON_TIP[Thawing]", "Too warm for new snow at the top. The runs are open on the snow that is left." },
                { "SkiResort.Hub.SEASON_TIP[WaitingForSnow]", "Cold enough, but the runs have no snow yet." },
                { "SkiResort.Hub.SEASON_TIP[TooWarm]", "Too warm for snow at the top, even for snow guns. If this map never has a winter, turn off Options > Modular Ski Resort > Snow & season gating so runs count all year." },
                { "SkiResort.Hub.LIFT_STATUS[Running]", "Running" },
                { "SkiResort.Hub.LIFT_STATUS[NoPower]", "No power" },
                { "SkiResort.Hub.LIFT_STATUS[ChairsStopped]", "Chairs stopped" },
                { "SkiResort.Hub.LIFT_STATUS[SectionMissing]", "Section missing" },
                { "SkiResort.Hub.LIFT_STATUS[Unfinished]", "Unfinished" },
                { "SkiResort.Hub.RUN_TYPE[Groomed]", "Groomed run" },
                { "SkiResort.Hub.RUN_TYPE[Snowmaking]", "Snowmaking run" },
                { "SkiResort.Hub.RUN_TYPE[Night]", "Night run" },
                { "SkiResort.Hub.RUN_TYPE[Natural]", "Natural run" },
                { "SkiResort.Hub.BUILD_SHORT[Groomed]", "Snow laid whenever it freezes; scores more on easy runs." },
                { "SkiResort.Hub.BUILD_SHORT[Snowmaking]", "Snow guns open it earlier and keep it open longer." },
                { "SkiResort.Hub.BUILD_SHORT[Night]", "Floodlit snowmaking run: scores 25 % more." },
                { "SkiResort.Hub.BUILD_SHORT[Natural]", "Natural snow only, no running cost; easy runs score less." },
                { "SkiResort.Hub.NEXT[Sign]", "Next: place a Ski Area Welcome Sign." },
                { "SkiResort.Hub.NEXT[Unlinked]", "Runs and lifts link to the nearest Welcome Sign within 5 km." },
                { "SkiResort.Hub.NEXT[Lodge]", "Next: a lodge on this ski area's Welcome Sign." },
                { "SkiResort.Hub.NEXT[Lift]", "Next: a base station, a top station, then the lift line." },
                { "SkiResort.Hub.NEXT[Run]", "Next: draw a run within 5 km of this Welcome Sign." },
                { "SkiResort.Hub.NEXT[Done]", "Hover a tile to see what it builds and what it costs." },
                { "SkiResort.Hub.NEXT2[Unlinked]", "Place a sign near them to link them." },
                { "SkiResort.Hub.NEXT2[Lodge]", "Guests arrive at a lodge. Without one the attraction is 0." },
                { "SkiResort.Hub.NEXT2[Lift]", "Runs that a lift serves count in full." },
                { "SkiResort.Hub.NEXT2[Run]", "Natural runs cost nothing to run; the others cost per hectare." },
                { "SkiResort.Hub.NEXT2[Done]", "Click a tile to build; click it again to stop." },

                // --- Lift tooltips (docs/research/hub-u2-spec.md §2.4, §5.5, §5.7; Round L1) ----------
                // Read by SkiLiftTooltipSystem (I-2) through LocalizedString.Id(key), built once — never
                // Id(key, args) per frame. ALIGN_OFFSET / ANGLE / DISTANCE / RISE and BULLDOZE_COUNT /
                // LENGTH label FloatTooltip / IntTooltip values whose units the UI formats. These are not
                // SkiResort.Hub.* keys, so the UI gate's glyph check does not cover them: ALIGN_STRAIGHT's
                // em dash is the spec's text and is NOT on the research §2.F.2 safe-glyph list; it renders
                // correctly in game (field test 6, screenshot 20.46.20.81; ui-small.md Verification), so
                // ALIGN_FACING_AWAY reuses it.
                { "SkiResort.LiftTooltip.ALIGN_OFFSET", "Offset from the straight line" },
                { "SkiResort.LiftTooltip.ALIGN_ANGLE", "Angle off straight" },
                { "SkiResort.LiftTooltip.ALIGN_DISTANCE", "Distance to the other station" },
                { "SkiResort.LiftTooltip.ALIGN_RISE", "Rise" },
                { "SkiResort.LiftTooltip.ALIGN_STRAIGHT", "Straight — 180°" },
                { "SkiResort.LiftTooltip.ALIGN_NO_PARTNER", "No free station of the other kind within 3 km" },
                // Round F6 (B2): in place of the angle row when the yaw is beyond 90° (findings/ui-small.md
                // §2.5), and when this station sits behind the other one's stub (StationAlign READOUT CLASS).
                { "SkiResort.LiftTooltip.ALIGN_FACING_AWAY", "Facing away from the other station — turn it around" },
                { "SkiResort.LiftTooltip.ALIGN_PARTNER_AWAY", "The other station faces away from this one" },
                { "SkiResort.LiftTooltip.BULLDOZE_SECTION", "Removes 1 lift section. The lift stops until you redraw it." },
                { "SkiResort.LiftTooltip.BULLDOZE_TOWER", "This is a tower: it removes every section attached to it. Click the cable to remove one section." },
                // Fixer round (finding l1-lifts-3): a bulldozer drag over several cables is not "a tower".
                { "SkiResort.LiftTooltip.BULLDOZE_SECTIONS", "Removes these lift sections. The lift stops until you redraw them." },
                { "SkiResort.LiftTooltip.BULLDOZE_STUB", "Also removes the station's connection stub" },
                { "SkiResort.LiftTooltip.BULLDOZE_COUNT", "Sections removed" },
                { "SkiResort.LiftTooltip.BULLDOZE_LENGTH", "Length removed" },
            };
        }

        public void Unload()
        {
        }
    }
}
