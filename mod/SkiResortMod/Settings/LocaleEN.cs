// English strings for the options UI. Registered in Mod.OnLoad via
// GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(settings)). Plain characters only (°, &, %).
// Additional languages: add further IDictionarySource implementations and AddSource them with the matching locale id.
// Engine notes: EngineNotes/Settings.LocaleEN.md

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

                // --- Hub ---
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

                // --- Balance and Snow ---
                { m_Setting.GetOptionGroupLocaleID(SkiResortSettings.GroupBalance), "Ski area balance" },
                { m_Setting.GetOptionGroupLocaleID(SkiResortSettings.GroupSnow), "Snow" },

                // --- Ski days ---
                { m_Setting.GetOptionGroupLocaleID(SkiResortSettings.GroupSkiDays), "Ski days" },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.SkiDayLocalsPct)),
                    "Locals who ski"
                },
                // Description: a lower price does not bring more (false at or below 40: the price factor is 1 there, SkiDayRules)
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

                // --- Balance and Snow, continued ---

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
                // Description: "then fewer: about 80 % as many at 80". 40 and 200 are SkiDayRules' price edge and SkiMoneyRules.TicketPriceMax
                // (HubRulesTests H17 holds them). The gate (Welcome Sign working, an open run) and the household with no money are in the hub's
                // ticket notes, not here.
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.LiftTicketPrice)),
                    "What each guest pays on arrival, one price for every ski area. Above 40 it lowers ski days " +
                    "(about 90 % as many at 60, 80 % at 80, 20 % at 200); visitors the game sends on its own come " +
                    "at any price and still pay it. 0 turns lift tickets off. Default 80."
                },
                // Lodge spend per guest: "price 0 = nothing charged" is documented in the slider tip. Multipliers: SkiCatalog.LodgeDef.SpendMultiplier.
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
                // Income booking, lessons, season passes
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
                    "off. Default 30 %."
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
                // Off-season half staffing
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.OffSeasonHalfStaff)),
                    "Half staff off-season"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.OffSeasonHalfStaff)),
                    "On: when a ski area has had no open run for half a day, its lodges and ski school keep half their " +
                    "jobs, so half the wages, until a run opens again. Upkeep stays. To cut a closed lodge further, set it " +
                    "Inactive in its info panel (a tenth of its upkeep, no wages). Default on."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.SeasonPassShare)),
                    "Season pass share"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.SeasonPassShare)),
                    "The share of resident households that buy a season pass (the season starts 5 months before the map's winter). Each household within reach " +
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
                    "ticket price above 0. Default 2."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.EnableSnowGating)),
                    "Snow & season gating"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.EnableSnowGating)),
                    "On: a run adds to its ski area's score only while snow lies on it - open when 60 % " +
                    "of it has 3 ft (91 cm) of snow, closed below 40 % (measured in the game's own snow, or estimated " +
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
                    "Snow guns on snowmaking runs, night runs and cat tracks work where the air on the run is at most " +
                    "this many degrees above freezing, from 2 months before winter to 1 month after, until the run has 3 ft (91 cm) of snow. " +
                    "Higher means a longer season and a bigger snowmaking bill. Default 3.3 (38 F)."
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
                    "material slots. Auto uses a free slot only; many maps have none, and " +
                    "runs then fall back to flat painted surfaces. Force replaces that map " +
                    "material with ski-run snow everywhere the map author painted it " +
                    "(usually small areas, so check your map), and erases the map's paint of " +
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

                // --- Runs (bind on draw + drawing feedback) ---
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
                    "periodic check, which only happens while the game is running. Default on."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.ShowRunGradeWhileDrawing)),
                    "Show run difficulty while drawing"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.ShowRunGradeWhileDrawing)),
                    "When enabled, drawing or editing a ski run shows a slope heat-map over " +
                    "the run (gray flat, green, blue, black, red double black) and a tooltip " +
                    "with its rating, hardest unavoidable pitch, length and vertical drop. Default on."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.ContourLinesWhileDrawingRuns)),
                    "Contour lines while drawing runs"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.ContourLinesWhileDrawingRuns)),
                    "When enabled, terrain contour lines turn on while a ski run is selected " +
                    "in the area tool and turn off again afterwards. If you already had them " +
                    "on, they stay on. Default on."
                },
                // The winter snow line while placing
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.ShowSnowLineWhilePlacing)),
                    "Show the winter snow line while placing"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.ShowSnowLineWhilePlacing)),
                    "When enabled, drawing a ski run or placing a Ski Area Welcome Sign draws two lines on the terrain: the " +
                    "average winter snow line (where the December-February average climate reaches freezing, even in " +
                    "summer) and a fainter one below it where snow guns start to work. The tooltip gives both heights. Default on."
                },
                // The run names on the hill
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.ShowRunNamesOnTheHill)),
                    "Show run names on the hill"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.ShowRunNamesOnTheHill)),
                    "When enabled, each ski run's name is written on the ground at the run's center, in its difficulty color, " +
                    "while the camera is at piste-map distance. Handy in summer, when the runs carry no snow. The names step " +
                    "aside while you draw or edit a run. Default on."
                },
                // The setting is thickness only (no corner rule, no snapping)
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.RunOutlineSize)),
                    "Run outline size"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.RunOutlineSize)),
                    "How thick the outline and corner dots are while you draw, edit or bulldoze a ski run. " +
                    "0 keeps the game's thin outline. Default 8."
                },
                // Look only; the guns' start / stop rule is not a setting
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.SnowGunPlumes)),
                    "Snow-gun plumes"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.SnowGunPlumes)),
                    "Snow guns on snowmaking runs, night runs and cat tracks show a vapor plume while they run: cold enough at " +
                    "the gun and the run's snow below the depth where the guns stop. Look only: snow and upkeep do not " +
                    "change. Default on."
                },
                // Look only; the machine never changes snow, rating or upkeep
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.Groomers)),
                    "Snow groomers"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.Groomers)),
                    "One snow groomer drives each linked cat track while the track is open. Look only: snow, rating " +
                    "and upkeep do not change. Default on."
                },
                // The cat track tool's knobs
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.CatTrackGradeMaxPct)),
                    "Cat track: max grade"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.CatTrackGradeMaxPct)),
                    "The steepest a cat track may climb. A track that cannot stay under it turns red and asks for " +
                    "more length (a switchback). Default 25 %."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.CatTrackWidthM)),
                    "Cat track: width (m)"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.CatTrackWidthM)),
                    "Width of the cat track: the flat top the tool cuts and fills, and the run built on it. Default 20 m."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.CatTrackGradeCutRun)),
                    "Cat track: cut slope"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.CatTrackGradeCutRun)),
                    "Meters out per meter down where the track cuts into the hill. Lower is steeper. Default 1.5."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.CatTrackGradeFillRun)),
                    "Cat track: fill slope"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.CatTrackGradeFillRun)),
                    "Meters out per meter up where the track is built up. Lower is steeper. Default 2."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.CatTrackGradeConstant)),
                    "Cat track: constant grade"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.CatTrackGradeConstant)),
                    "Off: the track follows the ground and moves as little earth as the max grade allows. On: one even " +
                    "slope from end to end. Default off."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.QuickRunsEnabled)),
                    "Resort Planner (experimental)"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.QuickRunsEnabled)),
                    "Outline a mountainside and the planner proposes lifts and a set of linked runs around them: you review and change the " +
                    "plan, place the lift stations yourself, and build the runs in one go, with an optional tree filler. Experimental. " +
                    "Off: the tool cannot open and its hub button is grayed. Default off."
                },

                // --- Lifts ---
                { m_Setting.GetOptionGroupLocaleID(SkiResortSettings.GroupLifts), "Lifts" },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.LiftBuildAids)),
                    "Lift building aids"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.LiftBuildAids)),
                    "Guide line and offset readout while placing a station; a warning on the bulldozer " +
                    "showing what a click on a lift removes. Default on."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.LiftTowerPlanner)),
                    "Plan lift towers"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.LiftTowerPlanner)),
                    "After you draw or change a lift line, its towers are raised and moved along the line so the chairs " +
                    "and cabins stay at least 3 m above the ground (trams 5 m). Off: the towers stay where the game puts them. Default on."
                },

                // --- Diagnostics ---
                { m_Setting.GetOptionGroupLocaleID(SkiResortSettings.GroupDiagnostics), "Diagnostics" },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.DiagnosticMeasurements)),
                    "Measurement diagnostics"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.DiagnosticMeasurements)),
                    "For field tests, off by default. Turns on extra measurements that cost a little frame time " +
                    "(a daily ski-day measurement pass and the Ctrl+Alt+G slope capture file). Leave it off for normal play. Default off."
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.PlannerOnMainThread)),
                    "Resort Planner on the main thread"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.PlannerOnMainThread)),
                    "For field tests, off by default. The Resort Planner scans on a background thread; turn this on to scan on the " +
                    "game's main thread in small steps instead (slower, same result). Use it only if a scan never finishes or the " +
                    "log reports a scan worker error. A worker error turns the Resort Planner off until the next game load. Default off."
                },

                // --- Reset ---
                { m_Setting.GetOptionGroupLocaleID(SkiResortSettings.GroupReset), "Reset" },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.ResetToDefaults)),
                    "Reset all options to defaults"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.ResetToDefaults)),
                    "Puts every Modular Ski Resort option and key binding back to its default. Asks you to confirm first."
                },
                // The confirmation dialog text (AutomaticSettings.GetConfirmationMessage)
                {
                    m_Setting.GetOptionWarningLocaleID(nameof(SkiResortSettings.ResetToDefaults)),
                    "Reset all Modular Ski Resort options and key bindings to their defaults?"
                },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.DeleteAllResortContent)),
                    "Delete all ski resort content"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.DeleteAllResortContent)),
                    "Removes everything Modular Ski Resort placed in this city: lifts and their towers, stations, lodges, the ski school, " +
                    "patrol huts, Welcome Signs, ski runs, cat tracks, safety fences and the trees the glade tool or the Resort Planner " +
                    "planted. Use it before you unsubscribe. Your own power lines, roads and buildings are not touched. Terrain graded " +
                    "for cat tracks stays as it is, snow already on the runs melts with the weather, and felled trees do not come back. " +
                    "If one of your power lines ends at a base station, bulldoze that last span first. This cannot be undone: save your " +
                    "city first. Available only in a loaded city."
                },
                {
                    m_Setting.GetOptionWarningLocaleID(nameof(SkiResortSettings.DeleteAllResortContent)),
                    "Delete every lift, station, lodge, run, cat track and fence of Modular Ski Resort in this city? This cannot be undone."
                },

                // --- Key bindings (glade tool) ---
                // Every key comes from the ModSetting helpers (ModSetting.cs:303-371): option row label / description, the binding key and
                // hint key, and the map title. Which key feeds which screen: EngineNotes/Settings.LocaleEN.md
                { m_Setting.GetOptionGroupLocaleID(SkiResortSettings.GroupKeybindings), "Key bindings" },
                { m_Setting.GetBindingMapLocaleID(), "Modular Ski Resort" },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.ToggleGladeToolBinding)),
                    "Glade tool: toggle"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.ToggleGladeToolBinding)),
                    "Opens or closes the ski-run glade tool. Point at a run of a ski area " +
                    "to preview it at the chosen tree density (felled, removed and " +
                    "planted trees); left click applies. Works in a loaded city only. Default Ctrl+Shift+G."
                },
                // The lift-line tool
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.ToggleLiftLineToolBinding)),
                    "Lift-line tool: toggle"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.ToggleLiftLineToolBinding)),
                    "Opens or closes the lift-line tool: click a lift station, then the other station of the same lift. " +
                    "The planned towers appear (fewest towers, tall ones where they save a tower). Click on the line to " +
                    "add a tower, drag a tower to move it (release it off the line to remove it), right click or Esc to " +
                    "step back, then press the Build key. Default Ctrl+Shift+L."
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
                    "is open. In the cat track tool it grades and builds the track shown. Default Enter."
                },
                { m_Setting.GetBindingKeyLocaleID(SkiResortSettings.ActBuildLiftLine), "Build lift line" },
                { m_Setting.GetBindingKeyHintLocaleID(SkiResortSettings.ActBuildLiftLine), "Build line" },
                // The cat track tool
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.ToggleCatTrackGradeToolBinding)),
                    "Cat track tool: toggle"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.ToggleCatTrackGradeToolBinding)),
                    "Opens or closes the cat track tool (the hub's Cat track tile): click points up the mountain, drag a point " +
                    "to move it, right click to step back, Enter to grade the ground and build the track. Default Ctrl+Shift+K."
                },
                { m_Setting.GetBindingKeyLocaleID(SkiResortSettings.ActToggleCatTrackGrade), "Toggle cat track tool" },
                { m_Setting.GetBindingKeyHintLocaleID(SkiResortSettings.ActToggleCatTrackGrade), "Cat track tool" },
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.CatTrackDeleteModeBinding)),
                    "Cat track tool: delete mode"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.CatTrackDeleteModeBinding)),
                    "Turns the cat track tool's delete mode on or off. In delete mode, click a cat track to select it and click it " +
                    "again to delete it (the ground stays graded, no refund). Right click or Esc leaves delete mode. Only while the " +
                    "tool is open; the hub Runs tab's Delete cat tracks chip does the same. Default Ctrl+Shift+D."
                },
                { m_Setting.GetBindingKeyLocaleID(SkiResortSettings.ActCatTrackDeleteMode), "Cat track delete mode" },
                { m_Setting.GetBindingKeyHintLocaleID(SkiResortSettings.ActCatTrackDeleteMode), "Delete mode" },
                // The Resort Planner tool (code name Quick runs)
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.ToggleQuickRunsToolBinding)),
                    "Resort Planner: toggle"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.ToggleQuickRunsToolBinding)),
                    "Opens or closes the Resort Planner (also a button on the hub's Runs tab): click the corners of a region, " +
                    "Enter to scan it, click a run to leave it out, Enter to build. Inert while the Resort Planner is off. Default Ctrl+Shift+Q."
                },
                { m_Setting.GetBindingKeyLocaleID(SkiResortSettings.ActToggleQuickRuns), "Toggle Resort Planner" },
                { m_Setting.GetBindingKeyHintLocaleID(SkiResortSettings.ActToggleQuickRuns), "Resort Planner" },
                { m_Setting.GetBindingKeyLocaleID(SkiResortSettings.ActToggleGlade), "Toggle glade tool" },
                { m_Setting.GetBindingKeyHintLocaleID(SkiResortSettings.ActToggleGlade), "Glade tool" },
                // The two persisted actions keep their names; only the labels and descriptions change.
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
                // The dump key
                {
                    m_Setting.GetOptionLabelLocaleID(nameof(SkiResortSettings.DumpDiagnosticsBinding)),
                    "Dump diagnostics"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(SkiResortSettings.DumpDiagnosticsBinding)),
                    "Writes the drawing-feedback and glade-tool state to the mod log, also while a run is on the cursor. Needs Measurement diagnostics on. Default Ctrl+Alt+G."
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
