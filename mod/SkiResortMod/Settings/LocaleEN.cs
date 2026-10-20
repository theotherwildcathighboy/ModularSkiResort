// English strings for the options UI. Registered in Mod.OnLoad via
// GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(settings)).
// R3 (docs/research/run-binding-glade-spec.md §2 "Locale keys", §5.2): the dump-diagnostics key
// (label, description, binding key, hint) and the toggle's new default in its description.
// G3 (§8.3): next-variant → "next tree density (1-8)", next-mode → "interior / tree line"
// (labels, descriptions, binding names and hints; the persisted action names are unchanged).
// U1 (docs/research/resort-hub-understand.md §5.2 item 2): the "Ski Resort hub" group with
// ShowHubButton and OpenHubOnSignSelect.
// L1 (docs/research/hub-u2-spec.md §2.4, 2026-10-03): the "Lifts" group with LiftBuildAids.
// E1 (docs/research/econ-round/e1-spec.md §2.4, 2026-10-04; stage 0 owns this file, R10): group
// "Ski area balance", rewritten K / backcountry / gating / snowmaking texts, the new LiftTicketPrice,
// and (addendum A1) RunOutlineSize in the Runs group. Plain characters only (°, &, %).
// F7 (docs/research/field-test-7-fixes.md C1): the RunOutlineSize description, shorter (thickness only).
// S1 (docs/research/ski-days/s1-spec.md §2.12, 2026-10-05; stage 0 owns this file): the "Ski days" group
// with its four sliders (locals, tourists, longest trip, powder pull). Plain characters only.
// M2 (docs/research/round-m2/understand/density.md, ruling R8; texts docs/research/round-m2/build-contract.md §6.9):
// the two prop density sliders in the Runs group, look only.
// M2 (docs/research/round-m2/understand/hub-price.md, option 4b; ruling R7, texts build-contract.md §6.9): the Locals who
// ski and Lift ticket price descriptions say what the price does (ski days only, no effect up to 40) and follow R2's
// gate (an open run; the operating-lift branch of the ticket gate is gone).
// Catalog Phase B (docs/research/art/assets-2026-10/code-round-2/round-2-contract.md §8.6, §9, D15): the Lodge spend
// per guest slider (Balance group, after Lift ticket price). Plain characters only.
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

                // --- Hub (Round U1, resort-hub-understand.md §5.2 item 2) --------------------
                { m_Setting.GetOptionGroupLocaleID(SkiResortSettings.GroupHub), "Ski Resort hub" },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.ShowHubButton)),
                    "Show the Ski Resort button (top left)"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.ShowHubButton)),
                    "Shows a Ski Resort button at the top left of the screen that opens the hub: " +
                    "your runs, the resort totals and every building tool in one place. The hub " +
                    "is also always in the Universal Mod Panel. Mouse and keyboard layout only."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.OpenHubOnSignSelect)),
                    "Open the hub when a Welcome Sign is selected"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.OpenHubOnSignSelect)),
                    "Selecting a Ski Area Welcome Sign opens the Ski Resort hub beside its panel. " +
                    "It never closes the hub; close it with its X or Escape."
                },

                // --- Balance and Snow (Round E1, docs/research/econ-round/e1-spec.md §2.4, verbatim) --
                { m_Setting.GetOptionGroupLocaleID(SkiResortSettings.GroupBalance), "Ski area balance" },
                { m_Setting.GetOptionGroupLocaleID(SkiResortSettings.GroupSnow), "Snow" },

                // --- Ski days (Round S1, docs/research/ski-days/s1-spec.md §2.12, verbatim; ch4 §E) ------
                { m_Setting.GetOptionGroupLocaleID(SkiResortSettings.GroupSkiDays), "Ski days" },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.SkiDayLocalsPct)),
                    "Locals who ski"
                },
                // Description: Round M2 hub-price fix 1 (build-contract.md §6.9), replacing the S1 text, which said a
                // lower price brings more (false at or below 40: the price factor is 1 there, SkiDayRules).
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.SkiDayLocalsPct)),
                    "How often residents spend a day out skiing. More attraction, fresh snow and a shorter trip " +
                    "bring more; a lift ticket price above 40 brings fewer. 0 turns ski days off for residents. Default 25 %."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.SkiDayTouristsPct)),
                    "Tourists who ski"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.SkiDayTouristsPct)),
                    "How often hotel guests spend a day out skiing, moved by the same things. 0 turns ski days " +
                    "off for tourists. Default 50 %."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.SkiDayReachKm)),
                    "Longest ski-day trip (km)"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.SkiDayReachKm)),
                    "Nobody farther than this from a lodge, in a straight line, is sent skiing there. Fewer " +
                    "come the farther they live. Default 7."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.SkiDayPowderPct)),
                    "Powder pull"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.SkiDayPowderPct)),
                    "How much a fresh-snow day adds. At 100 % a full powder day doubles the residents who go; " +
                    "at 0 powder brings no extra skiers. Default 100 %."
                },

                // --- Balance and Snow, continued (Round E1, e1-spec §2.4, verbatim) ----------------------

                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.AttractivenessScaleK)),
                    "Attractiveness scale (K)"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.AttractivenessScaleK)),
                    "How much a ski area's runs and lifts add to its attraction score. Score = lodge base " +
                    "+ K times the square root of its runs and running lifts, times variety and vertical, " +
                    "then times the Welcome Sign's efficiency, at most 200. Default 4: in winter, with " +
                    "every run open, at full efficiency, a small ski area scores about 50, a medium one " +
                    "about 95, a large one about 160."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.BackcountryValuePercent)),
                    "Backcountry run value"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.BackcountryValuePercent)),
                    "What a run that no running lift serves is worth, as a percentage of a lift-served " +
                    "run. A run is lift-served when a complete, powered lift reaches its top. Default 40 %."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.LiftTicketPrice)),
                    "Lift ticket price"
                },
                // Description: Round M2 hub-price fix 2 (build-contract.md §6.9: hub-price.md §2 item 5 with "then
                // fewer: about 80 % as many at 80"), replacing the E1 text. 40 and 200 are SkiDayRules' price edge and
                // SkiMoneyRules.TicketPriceMax (HubRulesTests H17 holds them). The gate (Welcome Sign working, an open run) and the
                // household with no money are in the hub's ticket notes, not here (DECISIONS 242).
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.LiftTicketPrice)),
                    "What each guest pays on arrival, one price for every ski area. Above 40 it lowers ski days " +
                    "(about 90 % as many at 60, 80 % at 80, 20 % at 200); visitors the game sends on its own come " +
                    "at any price and still pay it. 0 turns lift tickets off. Default 60."
                },
                // Catalog Phase B (round-2-contract §8.6 / §9, D15): the contract's text plus D15's "price 0 = nothing
                // charged" (documented in the slider tip) and the default. Multipliers: SkiCatalog.LodgeDef.SpendMultiplier.
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.LodgeSpendPerGuest)),
                    "Lodge spend per guest"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.LodgeSpendPerGuest)),
                    "What each paying ski guest also spends at the lodge they arrive at, more at bigger lodges: at the " +
                    "default, 15 at a hut or A-frame, 23 at a chalet and 45 at a summit or grand lodge. It comes out of " +
                    "what the household has left after the lift ticket, so nothing is spent while lift tickets are off. " +
                    "0 turns it off. Default 15."
                },
                // Econ-2 (DECISIONS 239-241): income booking, lessons, season passes.
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.BookTicketsAsFees)),
                    "Book resort income as public transportation fees"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.BookTicketsAsFees)),
                    "On: lift tickets, season passes, lessons and lodge spend are booked as public transportation fees " +
                    "and reach your balance through the game's budget. Off: they go straight to your balance. Default on."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.LessonShare)),
                    "Lesson share"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.LessonShare)),
                    "At a ski area with a working ski school, this share of the people who go skiing that day also take " +
                    "a lesson. The household pays from what it has left after the lift ticket or pass. 0 turns lessons " +
                    "off. Default 20 %."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.LessonMultiplier)),
                    "Lesson price (× lift ticket)"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.LessonMultiplier)),
                    "A lesson costs this many lift tickets at the day's ticket price. Needs a lift ticket price above 0. " +
                    "Default 1.5."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.SeasonPassShare)),
                    "Season pass share"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.SeasonPassShare)),
                    "The share of resident households that buy a season pass (July to June). Each household within reach " +
                    "rolls once when a ski area opens for a new season, at this share times its chance of skiing there; " +
                    "households that moved in later or live farther away roll on their first visit. Holders ski free at " +
                    "the gate and go skiing more often. Tourists never buy one. 0 turns passes off. Default 50 %."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.SeasonPassMultiplier)),
                    "Season pass price (× lift ticket)"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.SeasonPassMultiplier)),
                    "A season pass costs this many lift tickets at the ticket price when it is sold, locked for the " +
                    "season. It pays off for a household that skis more often than this many times. Needs a lift " +
                    "ticket price above 0. Default 4."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.EnableSnowGating)),
                    "Snow & season gating"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.EnableSnowGating)),
                    "On: a run adds to its ski area's score only while snow lies on it - open when 60 % " +
                    "of it has 15 cm of snow (31 cm on natural black runs), closed below 40 % (measured in the game's own snow, or estimated " +
                    "from the climate until the first measurement). A ski area with no open run counts " +
                    "as summer sightseeing. Off: every run counts as open all year, for maps without " +
                    "winter. Runs pay upkeep only while their grooming or snow guns run, so on a warm " +
                    "map with gating off they cost no upkeep. Default on."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.SnowmakingBonusCelsius)),
                    "Snowmaking margin (°C)"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.SnowmakingBonusCelsius)),
                    "Snow guns on snowmaking and night runs work where the air on the run is at most " +
                    "this many degrees above freezing. Higher means a longer season and a bigger " +
                    "snowmaking bill. Default 3.3 (38 F)."
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
                    "(usually small areas — check your map), and ERASES the map's paint of " +
                    "that material under and around your ski runs: once the city is saved, " +
                    "that paint stays erased, and switching the option back does not restore " +
                    "it. Nothing is touched until the city has its first ski run. Takes " +
                    "effect when a save loads."
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

                // --- Runs (bind on draw + drawing feedback, run-feedback-spec §2/§6) ---------
                { m_Setting.GetOptionGroupLocaleID(SkiResortSettings.GroupRuns), "Runs" },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.EnableBindOnDraw)),
                    "Bind runs the moment they are drawn"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.EnableBindOnDraw)),
                    "When enabled, a run drawn within 5 km of a Ski Area Welcome Sign joins " +
                    "the resort (and is charged) immediately, even while paused, and waiting " +
                    "runs join the moment a sign is placed. Disabled, runs join on the next " +
                    "periodic check, which only happens while the game is running."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.ShowRunGradeWhileDrawing)),
                    "Show run difficulty while drawing"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.ShowRunGradeWhileDrawing)),
                    "When enabled, drawing or editing a ski run shows a slope heat-map over " +
                    "the run (grey flat, green, blue, black, red double black) and a tooltip " +
                    "with its rating, hardest unavoidable pitch, length and vertical drop."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.ContourLinesWhileDrawingRuns)),
                    "Contour lines while drawing runs"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.ContourLinesWhileDrawingRuns)),
                    "When enabled, terrain contour lines turn on while a ski run is selected " +
                    "in the area tool and turn off again afterwards. If you already had them " +
                    "on, they stay on."
                },
                // T32 (2026-10-16): the winter snow line while placing.
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.ShowSnowLineWhilePlacing)),
                    "Show the winter snow line while placing"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.ShowSnowLineWhilePlacing)),
                    "When enabled, drawing a ski run or placing a Ski Area Welcome Sign draws two lines on the terrain: the " +
                    "average winter snow line (where the December-February average climate reaches freezing, even in " +
                    "summer) and a fainter one below it where snow guns start to work. The tooltip gives both heights."
                },
                // T37 (2026-10-16): the run names on the hill.
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.ShowRunNamesOnTheHill)),
                    "Show run names on the hill"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.ShowRunNamesOnTheHill)),
                    "When enabled, each ski run's name is written on the ground at the run's centre, in its difficulty colour, " +
                    "while the camera is at piste-map distance. Handy in summer, when the runs carry no snow. The names step " +
                    "aside while you draw or edit a run."
                },
                // Label: Round E1 addendum A1. Description: Round F7 C1 (docs/research/field-test-7-fixes.md),
                // verbatim — the setting is thickness only since F7 (no corner rule, no snapping).
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.RunOutlineSize)),
                    "Run outline size"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.RunOutlineSize)),
                    "How thick the outline and corner dots are while you draw, edit or bulldoze a ski run. " +
                    "0 keeps the game's thin outline."
                },
                // Round M2 prop density (ruling R8, texts build-contract.md §6.9): look only, labels exactly R8's names.
                // Fixer (review-ui-hub-money F3, B5 item 6): "in a city; saved runs keep their look until edited", since
                // R8 re-stamps only on a settled slider change, never at a load.
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.SnowGunDensityPct)),
                    "Snow lances, fan guns and hydrants"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.SnowGunDensityPct)),
                    "How many corners of snowmaking and night runs get a snow lance, fan gun or hydrant, in percent of the " +
                    "full look. Look only: snow and upkeep follow the run type. Runs already built change when " +
                    "you move the slider in a city; saved runs keep their look until edited. Default 100 %."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.FloodlightDensityPct)),
                    "Floodlight masts on night runs"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.FloodlightDensityPct)),
                    "How many corners of night runs get a floodlight mast, in percent of the full look. Look " +
                    "only: snow and upkeep follow the run type, and night skiing does not need the masts. Runs " +
                    "already built change when you move the slider in a city; saved runs keep their look until " +
                    "edited. Default 100 %."
                },
                // T46 (2026-10-17): look only; the guns' start / stop rule is not a setting.
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.SnowGunPlumes)),
                    "Snow-gun plumes"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.SnowGunPlumes)),
                    "Snow guns on snowmaking and night runs show a vapour plume while they make snow: cold enough at " +
                    "the gun and the run's snow below the depth where the guns stop. Look only: snow and upkeep do not " +
                    "change. Default on."
                },

                // --- Lifts (Round L1, docs/research/hub-u2-spec.md §2.4; texts are the spec's) -------
                { m_Setting.GetOptionGroupLocaleID(SkiResortSettings.GroupLifts), "Lifts" },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.LiftBuildAids)),
                    "Lift building aids"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.LiftBuildAids)),
                    "Guide line and offset readout while placing a station; a warning on the bulldozer " +
                    "showing what a click on a lift removes"
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.LiftTowerPlanner)),
                    "Plan lift towers"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.LiftTowerPlanner)),
                    "After you draw or change a lift line, its towers are raised and moved along the line so the chairs " +
                    "and cabins stay at least 3 m above the ground (trams 5 m). Off: the towers stay where the game puts them."
                },

                // --- Diagnostics (code review 2026-10-19, DECISIONS 314 D4 + D9) --------------------
                { m_Setting.GetOptionGroupLocaleID(SkiResortSettings.GroupDiagnostics), "Diagnostics" },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.DiagnosticMeasurements)),
                    "Measurement diagnostics"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.DiagnosticMeasurements)),
                    "For field tests, off by default. Turns on extra measurements that cost a little frame time " +
                    "(a daily ski-day measurement pass and the Ctrl+Alt+G slope capture file). Leave it off for normal play."
                },

                // --- Key bindings (glade tool, spec §5) --------------------------------------
                // Every key built with the ModSetting helpers (ModSetting.cs:303-371):
                //   option row label/description — GetOptionLabelLocaleID/DescLocaleID(property)
                //     (the KeyBinding widget uses the normal item displayName/description,
                //     AutomaticSettings.cs:1492-1500);
                //   GetBindingKeyLocaleID(action) → "Options.OPTION[<id>/<action>/<binding>]"
                //     (:343-361) and GetBindingKeyHintLocaleID(action) → "Common.ACTION[<id>/
                //     <action>]" (:363-366) — consumed by the frontend rebinding UI / input
                //     hints, not by Game.dll C# (unverified which screens show them);
                //   GetBindingMapLocaleID() → "Options.INPUT_MAP[<id>]" (:368-371), the map
                //     title the conflict notification falls back to (InputManager.cs:1522).
                { m_Setting.GetOptionGroupLocaleID(SkiResortSettings.GroupKeybindings), "Key bindings" },
                { m_Setting.GetBindingMapLocaleID(), "Modular Ski Resort" },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.ToggleGladeToolBinding)),
                    "Glade tool: toggle"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.ToggleGladeToolBinding)),
                    "Opens or closes the ski-run glade tool. Hover a run bound to a Ski Area " +
                    "Welcome Sign to preview it at the chosen tree density (felled, removed and " +
                    "planted trees); left click applies. Works in a loaded city only. Default Ctrl+Shift+G."
                },
                // T48 round 2: the lift-line tool.
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.ToggleLiftLineToolBinding)),
                    "Lift-line tool: toggle"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.ToggleLiftLineToolBinding)),
                    "Opens or closes the lift-line tool: click a lift station, then the other station of the same lift. " +
                    "The planned towers appear (fewest towers, tall ones where they save a tower). Click on the line to " +
                    "add a tower, drag a tower to move it, right click a tower to remove it, then press the Build key. " +
                    "Esc drops the line. Default Ctrl+Shift+L."
                },
                { m_Setting.GetBindingKeyLocaleID(SkiResortSettings.ActToggleLiftLine), "Toggle lift-line tool" },
                { m_Setting.GetBindingKeyHintLocaleID(SkiResortSettings.ActToggleLiftLine), "Lift-line tool" },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.BuildLiftLineBinding)),
                    "Lift-line tool: build"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.BuildLiftLineBinding)),
                    "Builds the line the lift-line tool shows, with exactly the towers on screen. Only while the tool " +
                    "is open. Default Enter."
                },
                { m_Setting.GetBindingKeyLocaleID(SkiResortSettings.ActBuildLiftLine), "Build lift line" },
                { m_Setting.GetBindingKeyHintLocaleID(SkiResortSettings.ActBuildLiftLine), "Build line" },
                { m_Setting.GetBindingKeyLocaleID(SkiResortSettings.ActToggleGlade), "Toggle glade tool" },
                { m_Setting.GetBindingKeyHintLocaleID(SkiResortSettings.ActToggleGlade), "Glade tool" },
                // Round G3 (run-binding-glade-spec.md §8.3): the two persisted actions keep their
                // names; only the labels and descriptions change.
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.GladeNextVariantBinding)),
                    "Glade tool: next tree density (1-8)"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.GladeNextVariantBinding)),
                    "Steps the tree density the glade tool brings a run to: 1 clears the run, each higher " +
                    "level holds more trees per hectare (felling or planting to reach it), 8 is a thick " +
                    "forest; after 8 it starts again at 1. Only while the glade tool is open. Default Shift+G."
                },
                { m_Setting.GetBindingKeyLocaleID(SkiResortSettings.ActGladeNextVariant), "Next tree density" },
                { m_Setting.GetBindingKeyHintLocaleID(SkiResortSettings.ActGladeNextVariant), "Tree density" },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.GladeNextModeBinding)),
                    "Glade tool: interior / tree line"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.GladeNextModeBinding)),
                    "Switches what the glade tool works on: the trees inside the run, or the tree " +
                    "line along its outside edge. Only while the glade tool is open. Default Alt+G."
                },
                { m_Setting.GetBindingKeyLocaleID(SkiResortSettings.ActGladeNextMode), "Interior / tree line" },
                { m_Setting.GetBindingKeyHintLocaleID(SkiResortSettings.ActGladeNextMode), "Interior / tree line" },
                // R3 (docs/research/run-binding-glade-spec.md §2 locale keys, §5.2): the dump key.
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.DumpDiagnosticsBinding)),
                    "Dump diagnostics"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.DumpDiagnosticsBinding)),
                    "Writes the drawing-feedback and glade-tool state to the mod log, also while a run is on the cursor"
                },
                { m_Setting.GetBindingKeyLocaleID(SkiResortSettings.ActDumpDiagnostics), "Dump diagnostics" },
                { m_Setting.GetBindingKeyHintLocaleID(SkiResortSettings.ActDumpDiagnostics), "Dump" },
            };
        }

        public void Unload()
        {
        }
    }
}
