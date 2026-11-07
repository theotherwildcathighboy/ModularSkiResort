// Mod options (Options > Mods > Modular Ski Resort). A setting for every balance constant, so players can fix tuning mistakes without
// waiting for a patch. Property names are persisted: never rename them (labels may change). The Default* constants are the one copy of each
// default. SetDefaults and every null-settings fallback use them. Systems read Mod.Settings?.X ?? DefaultX because Mod.Settings is null
// before OnLoad and after OnDispose.
// Key bindings are plain ProxyBinding members: SetDefaults must not assign them (it would replace the generated default binding
// with an empty one).
// Engine notes: EngineNotes/SkiResortSettings.md

using Colossal.IO.AssetDatabase;
using Game.Input;
using Game.Modding;
using Game.Settings;
using SkiResortMod.Systems;

namespace SkiResortMod.Settings
{
    [FileLocation("ModsSettings/SkiResortMod/SkiResortMod")]
    [SettingsUIGroupOrder(GroupHub, GroupBalance, GroupSkiDays, GroupSnow, GroupRuns, GroupLifts, GroupKeybindings, GroupDiagnostics, GroupReset)]
    [SettingsUIShowGroupName(GroupHub, GroupBalance, GroupSkiDays, GroupSnow, GroupRuns, GroupLifts, GroupKeybindings, GroupDiagnostics, GroupReset)]
    // One keyboard action declaration per binding (conflict warnings, see engine notes)
    [SettingsUIKeyboardAction(ActToggleGlade)]
    [SettingsUIKeyboardAction(ActGladeNextVariant)]
    [SettingsUIKeyboardAction(ActGladeNextMode)]
    [SettingsUIKeyboardAction(ActDumpDiagnostics)]
    [SettingsUIKeyboardAction(ActToggleLiftLine)]
    [SettingsUIKeyboardAction(ActBuildLiftLine)]
    [SettingsUIKeyboardAction(ActToggleCatTrackGrade)]
    [SettingsUIKeyboardAction(ActCatTrackDeleteMode)]
    [SettingsUIKeyboardAction(ActToggleQuickRuns)]
    public sealed class SkiResortSettings : ModSetting
    {
        public const string SectionMain = "Main";
        public const string GroupHub = "Hub";
        public const string GroupBalance = "Balance";
        public const string GroupSkiDays = "SkiDays";
        public const string GroupSnow = "Snow";
        public const string GroupTerrain = "Terrain";
        public const string GroupRuns = "Runs";
        public const string GroupLifts = "Lifts";
        public const string GroupKeybindings = "Keybindings";
        public const string GroupDiagnostics = "Diagnostics";
        public const string GroupReset = "Reset";

        // Action names inside our input map (map name = ModSetting.id, "SkiResortMod.SkiResortMod.Mod", ModSetting.cs:39, 245).
        // Persisted in the settings file and the player's rebinds: never rename. GladeNextVariant steps the tree density (1-8).
        // GladeNextMode toggles run interior / tree line. The names stay although the meaning changed.
        public const string ActToggleGlade = "ToggleGladeTool";
        public const string ActGladeNextVariant = "GladeNextVariant";
        public const string ActGladeNextMode = "GladeNextMode";
        public const string ActDumpDiagnostics = "DumpDiagnostics";
        public const string ActToggleLiftLine = "ToggleLiftLineTool";
        public const string ActBuildLiftLine = "BuildLiftLine";
        public const string ActToggleCatTrackGrade = "ToggleCatTrackGradeTool";
        public const string ActCatTrackDeleteMode = "CatTrackDeleteMode";
        public const string ActToggleQuickRuns = "ToggleQuickRunsTool";

        public const int DefaultAttractivenessScaleK = 4;
        public const int DefaultBackcountryValuePercent = 40;
        public const bool DefaultEnableSnowGating = true;
        public const bool DefaultSnowGunPlumes = true;
        public const bool DefaultGroomers = true;
        public const bool DefaultDiagnosticMeasurements = false;
        public const bool DefaultPlannerOnMainThread = false;
        public const bool DefaultAnimatedLiftCarriers = true;
        public const int DefaultSeasonPassShare = SkiMoneyRules.PassShareDefault;
        public const int DefaultSeasonPassMultiplier = SkiMoneyRules.PassMultiplierDefault;
        public const int DefaultLessonShare = SkiMoneyRules.LessonShareDefault;
        public const float DefaultLessonMultiplier = SkiMoneyRules.LessonMultiplierDefault;
        public const float DefaultSnowmakingBonusCelsius = RunSnowRules.DefaultSnowmakingMarginC;
        // 8 = the look of the game's lot outline (snap distance 8 x 0.3, AreaBorderRenderSystem.cs:517, :531): outline and corner dots 2.4 m
        public const int DefaultRunOutlineSize = 8;
        // Aliases of the SkiDayRules constants: the one source of the numbers
        public const int DefaultSkiDayLocalsPct = SkiDayRules.LocalsPctDefault;
        public const int SkiDayLocalsPctMax = SkiDayRules.LocalsPctMax;
        public const int DefaultSkiDayTouristsPct = SkiDayRules.TouristsPctDefault;
        public const int SkiDayTouristsPctMax = SkiDayRules.TouristsPctMax;
        public const int DefaultSkiDayReachKm = SkiDayRules.ReachKmDefault;
        public const int SkiDayReachKmMin = SkiDayRules.ReachKmMin;
        public const int SkiDayReachKmMax = SkiDayRules.ReachKmMax;
        public const int DefaultSkiDayPowderPct = SkiDayRules.PowderPctDefault;
        public const int SkiDayPowderPctMax = SkiDayRules.PowderPctMax;
        // Aliases of SkiCatalog.LodgeSpend: the one source of the numbers
        public const int DefaultLodgeSpendPerGuest = SkiCatalog.LodgeSpend.Default;
        public const int LodgeSpendPerGuestMax = SkiCatalog.LodgeSpend.Max;
        public const int LodgeSpendPerGuestStep = SkiCatalog.LodgeSpend.Step;

        public SkiResortSettings(IMod mod) : base(mod)
        {
            SetDefaults();
        }


        /// <summary>Show the Ski Resort hub's top-left FloatingButton (mouse/keyboard layout only: the GameTopLeft hook does not
        /// render in the gamepad layout). The Universal Mod Panel button is always shown. Pushed to the UI as skiResort.showHubButton.</summary>
        [SettingsUISection(SectionMain, GroupHub)]
        public bool ShowHubButton { get; set; }

        /// <summary>Selecting a Ski Area Welcome Sign opens the Ski Resort hub (once per selection change). It never forces the hub closed.
        /// SkiResortHubUISystem.</summary>
        [SettingsUISection(SectionMain, GroupHub)]
        public bool OpenHubOnSignSelect { get; set; }


        /// <summary>The ski area radius in km (the tether). A run binds to the nearest Welcome Sign within it when drawn (and keeps it).
        /// Lifts, lodges, schools and patrol huts link to the nearest sign within it, live, at every recompute. Float slider 2-10, step
        /// 0.5, default 5 (SkiAreaRadiusRules). Read through SkiRunOwnershipSystem.TetherMeters (SkiAreaRadiusMOf).</summary>
        [SettingsUISlider(min = SkiAreaRadiusRules.MinKm, max = SkiAreaRadiusRules.MaxKm, step = SkiAreaRadiusRules.StepKm, unit = "floatSingleFraction")]
        [SettingsUISection(SectionMain, GroupBalance)]
        public float SkiAreaRadiusKm { get; set; }

        /// <summary>The radius in meters, clamped. Null settings give the default. Main thread only.</summary>
        public static float SkiAreaRadiusMOf(SkiResortSettings s)
            => SkiAreaRadiusRules.MetersOf(s == null ? SkiAreaRadiusRules.DefaultKm : s.SkiAreaRadiusKm);

        /// <summary>K: A = lodge base + K x sqrt(runs and running lifts) x variety x vertical, x the Welcome Sign's efficiency, at most
        /// 200. Read by SkiResortAttractivenessSystem as Mod.Settings?.AttractivenessScaleK ?? DefaultAttractivenessScaleK, clamped
        /// SkiResortScore.MinK..MaxK.</summary>
        [SettingsUISlider(min = 1f, max = 10f, step = 1f, unit = "integer")]
        [SettingsUISection(SectionMain, GroupBalance)]
        public int AttractivenessScaleK { get; set; }

        /// <summary>Backcountry run value: what a run no complete, powered lift serves is worth, in percent of a lift-served run
        /// (default 40). Read by SkiResortAttractivenessSystem with the DefaultBackcountryValuePercent fallback.</summary>
        [SettingsUISlider(min = 0f, max = 100f, step = 5f, unit = "percentage")]
        [SettingsUISection(SectionMain, GroupBalance)]
        public int BackcountryValuePercent { get; set; }

        /// <summary>Read live by SkiLiftTicketSystem (and by SkiResortStatsSystem for the hub). 0 = tickets off. The range is 0-200, step 5
        /// (SkiMoneyRules.TicketPriceMax / TicketPriceStep). The default is SkiMoneyRules.TicketPriceDefault. unit "money" =
        /// Game.UI.Unit.kMoney (Game.UI/Unit.cs:49, 1.6.0f1 and 1.6.2f1). Use "integer" instead if the Options row renders it badly
        /// (unverified).</summary>
        [SettingsUISlider(min = 0f, max = 200f, step = 5f, unit = "money")]
        [SettingsUISection(SectionMain, GroupBalance)]
        public int LiftTicketPrice { get; set; }

        /// <summary>What each paying ski guest also spends at the lodge they arrive at (x the tier's SpendMultiplier), charged with the
        /// ticket. Read live by SkiLiftTicketSystem through SkiMoneyRules.ClampLodgeSpend. 0 = off. The default is 15
        /// (DefaultLodgeSpendPerGuest). unit "money" as LiftTicketPrice.</summary>
        [SettingsUISlider(min = 0f, max = LodgeSpendPerGuestMax, step = LodgeSpendPerGuestStep, unit = "money")]
        [SettingsUISection(SectionMain, GroupBalance)]
        public int LodgeSpendPerGuest { get; set; }

        /// <summary>The share, in percent, of resident households that buy a season pass (rolled in the pre-season sale, or at the first
        /// ski of the season for households outside it). 0 = passes off. Read live by SkiLiftTicketSystem through
        /// SkiMoneyRules.ClampPassShare(Mod.Settings?.SeasonPassShare ?? DefaultSeasonPassShare).</summary>
        [SettingsUISlider(min = 0f, max = SkiMoneyRules.PassShareMax, step = SkiMoneyRules.PassShareStep, unit = "percentage")]
        [SettingsUISection(SectionMain, GroupBalance)]
        public int SeasonPassShare { get; set; }

        /// <summary>A pass costs this many lift tickets (the price at the moment of sale, locked). Read live by SkiLiftTicketSystem
        /// through SkiMoneyRules.ClampPassMultiplier.</summary>
        [SettingsUISlider(min = SkiMoneyRules.PassMultiplierMin, max = SkiMoneyRules.PassMultiplierMax, step = 1f, unit = "integer")]
        [SettingsUISection(SectionMain, GroupBalance)]
        public int SeasonPassMultiplier { get; set; }

        /// <summary>The percentage of the people counted at a ski area with an active ski school who take a lesson that day. 0 = lessons
        /// off. Read live by SkiLiftTicketSystem through SkiMoneyRules.ClampLessonShare.</summary>
        [SettingsUISlider(min = 0f, max = SkiMoneyRules.LessonShareMax, step = SkiMoneyRules.LessonShareStep, unit = "percentage")]
        [SettingsUISection(SectionMain, GroupBalance)]
        public int LessonShare { get; set; }

        /// <summary>A lesson costs this many lift tickets at the ticket price now (1.0-3.0 step 0.5). Read live through
        /// SkiMoneyRules.ClampLessonMultiplier.</summary>
        [SettingsUISlider(min = SkiMoneyRules.LessonMultiplierMin, max = SkiMoneyRules.LessonMultiplierMax, step = SkiMoneyRules.LessonMultiplierStep, unit = "floatSingleFraction")]
        [SettingsUISection(SectionMain, GroupBalance)]
        public float LessonMultiplier { get; set; }

        /// <summary>On = lodges and the ski school keep half their workplaces while their ski area has had no open run for 12 game hours
        /// (OffSeasonStaffSystem, OffSeasonRules). Read live (on when settings are not loaded). Turning it off restores full staff on the
        /// next poll. Default on.</summary>
        [SettingsUISection(SectionMain, GroupBalance)]
        public bool OffSeasonHalfStaff { get; set; }


        /// <summary>Residents' share in percent (0 = no ski days for residents). Read by SkiDaySystem (and the hub's "off" test) through
        /// SkiDayLocalsPctOf.</summary>
        [SettingsUISlider(min = 0f, max = SkiDayLocalsPctMax, step = 5f, unit = "percentage")]
        [SettingsUISection(SectionMain, GroupSkiDays)]
        public int SkiDayLocalsPct { get; set; }

        /// <summary>Hotel tourists' share in percent (0 = no ski days for tourists). Read by SkiDaySystem (and the hub's "off" test)
        /// through SkiDayTouristsPctOf.</summary>
        [SettingsUISlider(min = 0f, max = SkiDayTouristsPctMax, step = 5f, unit = "percentage")]
        [SettingsUISection(SectionMain, GroupSkiDays)]
        public int SkiDayTouristsPct { get; set; }

        /// <summary>The longest ski-day trip in km, straight line to the lodge. Unit "integer" with "(km)" in the label
        /// (Game.UI/Unit.cs:5). Unit "length" would read meters. Read by SkiDaySystem through SkiDayReachKmOf.</summary>
        [SettingsUISlider(min = SkiDayReachKmMin, max = SkiDayReachKmMax, step = 1f, unit = "integer")]
        [SettingsUISection(SectionMain, GroupSkiDays)]
        public int SkiDayReachKm { get; set; }

        /// <summary>What a full powder day adds, in percent (100 doubles the residents who go). Read by SkiDaySystem through
        /// SkiDayPowderPctOf.</summary>
        [SettingsUISlider(min = 0f, max = SkiDayPowderPctMax, step = 10f, unit = "percentage")]
        [SettingsUISection(SectionMain, GroupSkiDays)]
        public int SkiDayPowderPct { get; set; }

        /// <summary>Clamped read. Null settings give the default. Main thread only.</summary>
        public static int SkiDayLocalsPctOf(SkiResortSettings s)
            => s == null ? DefaultSkiDayLocalsPct : Clamp(s.SkiDayLocalsPct, SkiDayRules.LocalsPctMin, SkiDayRules.LocalsPctMax);

        /// <summary>Clamped read. Null settings give the default. Main thread only.</summary>
        public static int SkiDayTouristsPctOf(SkiResortSettings s)
            => s == null ? DefaultSkiDayTouristsPct : Clamp(s.SkiDayTouristsPct, SkiDayRules.TouristsPctMin, SkiDayRules.TouristsPctMax);

        /// <summary>Clamped read. Null settings give the default. Main thread only.</summary>
        public static int SkiDayReachKmOf(SkiResortSettings s)
            => s == null ? DefaultSkiDayReachKm : Clamp(s.SkiDayReachKm, SkiDayRules.ReachKmMin, SkiDayRules.ReachKmMax);

        /// <summary>Clamped read. Null settings give the default. Main thread only.</summary>
        public static int SkiDayPowderPctOf(SkiResortSettings s)
            => s == null ? DefaultSkiDayPowderPct : Clamp(s.SkiDayPowderPct, SkiDayRules.PowderPctMin, SkiDayRules.PowderPctMax);

        /// <summary>net48 has no Math.Clamp</summary>
        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;


        /// <summary>On = a run counts toward attraction by its measured snow (open at 60 %, closed below 40 %). Off = every run counts as
        /// open all year. Read by SkiSnowInjectionSystem.GetRunSnow, through SnowLineSystem.IsSnowGatingEnabled() (the one read). It
        /// returns true when settings are not loaded.</summary>
        [SettingsUISection(SectionMain, GroupSnow)]
        public bool EnableSnowGating { get; set; }

        /// <summary>The snowmaking margin: snow guns (snowmaking and night runs) work where the air on the run is at most this many degrees
        /// C above freezing. Read by SnowLineSystem.GetSnowmakingMarginC (injection, estimate, and through the mask the bill). Default 3.3
        /// (38 F). It is a float slider, step 0.1. An options file saved with the old int slider keeps its stored value, with no migration.
        /// The property keeps its old name (persisted).</summary>
        [SettingsUISlider(min = 0f, max = 6f, step = 0.1f, unit = "floatSingleFraction")]
        [SettingsUISection(SectionMain, GroupSnow)]
        public float SnowmakingBonusCelsius { get; set; }


        // Runs: read live every frame by the consuming systems, so a change takes effect at once

        /// <summary>Bind a run to its Ski Area Welcome Sign the frame it is drawn (and waiting runs the frame a sign is placed), paused or
        /// not (SkiRunBindTriggerSystem). Off = binding only by the 64-sim-frame sweep, which never runs while paused.</summary>
        [SettingsUISection(SectionMain, GroupRuns)]
        public bool EnableBindOnDraw { get; set; }

        /// <summary>Slope heat-map overlay and grade tooltip while drawing or editing a run with the area tool (SkiRunDrawFeedbackSystem +
        /// SkiRunTooltipSystem).</summary>
        [SettingsUISection(SectionMain, GroupRuns)]
        public bool ShowRunGradeWhileDrawing { get; set; }

        /// <summary>Terrain contour lines on while a run prefab is selected in the area tool, restored afterwards
        /// (SkiRunContourSystem).</summary>
        [SettingsUISection(SectionMain, GroupRuns)]
        public bool ContourLinesWhileDrawingRuns { get; set; }

        /// <summary>While the area tool holds one of our run prefabs or the object tool places the Ski Area Welcome Sign, draw the winter
        /// snow line and a fainter snowmaking line on the terrain, with one tooltip line (SkiSnowLineOverlaySystem +
        /// SkiSnowLineTooltipSystem). The winter snow line is where the mean December-February climate temperature, lapsed with elevation,
        /// crosses 0 C. Read live every frame. Default on.</summary>
        [SettingsUISection(SectionMain, GroupRuns)]
        public bool ShowSnowLineWhilePlacing { get; set; }

        /// <summary>Draw the ski area radius (SkiAreaRadiusKm) as a ring on the ground: around the cursor while a Welcome Sign is placed,
        /// around a selected sign, and around every sign while a sign, run, lift or resort building is placed (SkiAreaRingOverlaySystem).
        /// Read live every frame. Default on.</summary>
        [SettingsUISection(SectionMain, GroupRuns)]
        public bool ShowSkiAreaRadius { get; set; }

        /// <summary>Write each ski run's name flat on the ground at the run's center, in the piste-map color of its difficulty, while the
        /// camera is at piste-map distance (SkiRunLabelSystem). Read live every frame. Default on.</summary>
        [SettingsUISection(SectionMain, GroupRuns)]
        public bool ShowRunNamesOnTheHill { get; set; }

        /// <summary>The thickness of SkiRunOutlineSystem's overlay (line width and corner-dot diameter 0.3 x this many meters). The overlay
        /// is on the run areas the game outlines while the area tool or the bulldozer works on them. 0 = none (the game's thin outline
        /// only). Read every frame. Snapping and the corner rule stay the game's (0.75 m / 0.375 m for a Surface area, AreaUtils.cs:55-58,
        /// :410-421).</summary>
        [SettingsUISlider(min = 0f, max = 16f, step = 1f, unit = "integer")]
        [SettingsUISection(SectionMain, GroupRuns)]
        public int RunOutlineSize { get; set; }

        /// <summary>Look only: snow guns show a vapor plume while their run's guns are running (SnowGunPlumeRules). Read every pass (64 sim
        /// frames) by SkiSnowGunPlumeSystem only. Off cuts every plume within one pass. Snow, upkeep and the guns' own start / stop never
        /// read it.</summary>
        [SettingsUISection(SectionMain, GroupRuns)]
        public bool SnowGunPlumes { get; set; }

        /// <summary>Look only: one snow groomer drives each bound cat track while it is open (SkiGroomerSystem, read every frame). Off stops and removes
        /// them within a frame. Snow, rating and upkeep never read it.</summary>
        [SettingsUISection(SectionMain, GroupRuns)]
        public bool Groomers { get; set; }

        // Cat track tool (grades the corridor and builds the run in one action). Read live by CatTrackGradeToolSystem.ReadParams. Values
        // are clamped there. A change re-solves the open plan.

        /// <summary>The steepest grade the cat track tool lets a track climb, percent (5-25, default 25 = the blue-run crux)</summary>
        [SettingsUISlider(min = 5f, max = 25f, step = 1f, unit = "percentage")]
        [SettingsUISection(SectionMain, GroupRuns)]
        public int CatTrackGradeMaxPct { get; set; }

        /// <summary>The cat track's width = the graded flat top = the run's outline, meters (8-32, default 20 = GradeParams.DefaultTopWidthM).
        /// Renamed from CatTrackGradeWidthM (4-16, default 8) on 2026-10-24 so a saved 8 does not survive the new default.</summary>
        [SettingsUISlider(min = 8f, max = 32f, step = 1f, unit = "integer")]
        [SettingsUISection(SectionMain, GroupRuns)]
        public int CatTrackWidthM { get; set; }

        /// <summary>Cut side slope, meters out per meter down (1.0-3.0, default 1.5 = 1:1.5)</summary>
        [SettingsUISlider(min = 1f, max = 3f, step = 0.5f, unit = "floatSingleFraction")]
        [SettingsUISection(SectionMain, GroupRuns)]
        public float CatTrackGradeCutRun { get; set; }

        /// <summary>Fill side slope, meters out per meter up (1.0-4.0, default 2 = 1:2)</summary>
        [SettingsUISlider(min = 1f, max = 4f, step = 0.5f, unit = "floatSingleFraction")]
        [SettingsUISection(SectionMain, GroupRuns)]
        public float CatTrackGradeFillRun { get; set; }

        /// <summary>Off (default) = Follow ground, the least earthwork under the grade limit. On = Constant grade, one straight ramp
        /// between the two ends.</summary>
        [SettingsUISection(SectionMain, GroupRuns)]
        public bool CatTrackGradeConstant { get; set; }

        /// <summary>The Resort Planner tool (code name Quick runs, experimental, DECISIONS 404, 443). Outline a region. The planner
        /// proposes runs. Enter builds them. Off (default) = the tool cannot open, its key is inert and the hub button is grayed with the
        /// reason. Read live by QuickRunsToolSystem.OptionOn.</summary>
        [SettingsUISection(SectionMain, GroupRuns)]
        public bool QuickRunsEnabled { get; set; }


        /// <summary>Station alignment aid (guide line and offset readout while a station is placed or nudged) and the bulldozer warning
        /// on a lift (SkiStationAlignSystem + SkiLiftTooltipSystem). Read live every frame by those systems.</summary>
        [SettingsUISection(SectionMain, GroupLifts)]
        public bool LiftBuildAids { get; set; }

        /// <summary>LiftTowerFitSystem fits every new or changed lift line once its edits settle (tower heights through the node
        /// elevation, tower positions along a straight line) so the carriers keep their floor over the ground. Off = the lines stay as
        /// the engine split them (the diagnostic still logs the plan). Read live by LiftTowerFitSystem.</summary>
        [SettingsUISection(SectionMain, GroupLifts)]
        public bool LiftTowerPlanner { get; set; }

        /// <summary>The carrier grip round. On: each split carrier's grip pitches with the rope and its body (chair or cabin) swings in the
        /// wind (CarrierPose). Off: the grip keeps the level yaw-only pose and the body hangs rigid (nothing else changes). Lift types
        /// whose grip prefab is not in the game are rigid either way. Read live by ChairPinSystem and TramShuttleSystem (one read per
        /// pass).</summary>
        [SettingsUISection(SectionMain, GroupLifts)]
        public bool AnimatedLiftCarriers { get; set; }

        /// <summary>Null-tolerant read for the movers</summary>
        public static bool AnimatedLiftCarriersOn => Mod.Settings?.AnimatedLiftCarriers ?? DefaultAnimatedLiftCarriers;


        /// <summary>Measurement scaffolding for field tests, off by default and for every player. It covers the midday measurement re-arm
        /// and the measure-only ResidentAI pass in SkiDaySystem / SkiGuestCounterSystem / SkiLodgeParkingSystem (they force main-thread
        /// syncs on vanilla jobs). It also covers the .skislope capture file of the Ctrl+Alt+G dump (the only file the mod writes outside
        /// the log and its settings). The log dump stays for every player. Read live by those systems.</summary>
        [SettingsUISection(SectionMain, GroupDiagnostics)]
        public bool DiagnosticMeasurements { get; set; }

        /// <summary>Null-tolerant read for the systems (Mod.Settings is null before OnLoad and after OnDispose)</summary>
        public static bool DiagnosticsOn => Mod.Settings?.DiagnosticMeasurements ?? DefaultDiagnosticMeasurements;

        /// <summary>The Resort Planner's scan on the main thread, sliced over frames as before the worker thread (off by default): the
        /// field-test fallback if the worker misbehaves. Same planner, same result (gate D-2). Read by QuickRunsToolSystem at each scan
        /// launch (a running scan keeps the thread it started on).</summary>
        [SettingsUISection(SectionMain, GroupDiagnostics)]
        public bool PlannerOnMainThread { get; set; }

        /// <summary>Null-tolerant read for the tool</summary>
        public static bool PlannerOnMainThreadOn => Mod.Settings?.PlannerOnMainThread ?? DefaultPlannerOnMainThread;

        /// <summary>Copies a bug-report block to the clipboard (SkiDiagnostics: versions, machine, options changed from defaults, code mods,
        /// the log path and its error lines). A button: nothing is stored.</summary>
        [SettingsUIButton]
        [SettingsUISection(SectionMain, GroupDiagnostics)]
        public bool CopyDiagnostics
        {
            set { SkiDiagnostics.CopyToClipboard("options button"); }
        }

        // Key bindings must stay public get+set ProxyBinding (ModSetting.cs:32-34). Defaults come from the attribute via the base
        // constructor: never assign these in SetDefaults.

        /// <summary>Toggles the glade tool (default Ctrl+Shift+G). Polled every frame, paused or not, by SkiGladeHotkeySystem.</summary>
        [SettingsUIKeyboardBinding(BindingKeyboard.G, ActToggleGlade, ctrl: true, shift: true)]
        [SettingsUISection(SectionMain, GroupKeybindings)]
        public ProxyBinding ToggleGladeToolBinding { get; set; }

        /// <summary>Steps the glade tool's tree density from 1 to 8, then back to 1 (default Shift+G). The property and action name keep
        /// their older names (persisted). Forwarded only while the glade tool is active.</summary>
        [SettingsUIKeyboardBinding(BindingKeyboard.G, ActGladeNextVariant, shift: true)]
        [SettingsUISection(SectionMain, GroupKeybindings)]
        public ProxyBinding GladeNextVariantBinding { get; set; }

        /// <summary>Toggles the glade tool's target, run interior / tree line (default Alt+G). The property and action name keep their
        /// older names (persisted). Forwarded only while the glade tool is active.</summary>
        [SettingsUIKeyboardBinding(BindingKeyboard.G, ActGladeNextMode, alt: true)]
        [SettingsUISection(SectionMain, GroupKeybindings)]
        public ProxyBinding GladeNextModeBinding { get; set; }

        /// <summary>Dumps diagnostics to the mod log (default Ctrl+Alt+G): the run drawing feedback while the area tool is active, the
        /// glade tool while it is active, otherwise both. It works with a run on the cursor. Polled by SkiGladeHotkeySystem independently
        /// of the glade keys, in a loaded city only.</summary>
        [SettingsUIKeyboardBinding(BindingKeyboard.G, ActDumpDiagnostics, ctrl: true, alt: true)]
        [SettingsUISection(SectionMain, GroupKeybindings)]
        public ProxyBinding DumpDiagnosticsBinding { get; set; }

        /// <summary>Toggles the lift-line tool (default Ctrl+Shift+L): click a station, the other station, edit the planned towers,
        /// Build. Polled by LiftLineHotkeySystem in a loaded city.</summary>
        [SettingsUIKeyboardBinding(BindingKeyboard.L, ActToggleLiftLine, ctrl: true, shift: true)]
        [SettingsUISection(SectionMain, GroupKeybindings)]
        public ProxyBinding ToggleLiftLineToolBinding { get; set; }

        /// <summary>Builds the line the lift-line tool is editing (default Enter). Live only while that tool is active
        /// (LiftLineHotkeySystem).</summary>
        [SettingsUIKeyboardBinding(BindingKeyboard.Enter, ActBuildLiftLine)]
        [SettingsUISection(SectionMain, GroupKeybindings)]
        public ProxyBinding BuildLiftLineBinding { get; set; }

        /// <summary>Toggles the cat track tool (default Ctrl+Shift+K). Polled by CatTrackGradeHotkeySystem in a loaded city. The tool
        /// grades with the BuildLiftLine key (Enter).</summary>
        [SettingsUIKeyboardBinding(BindingKeyboard.K, ActToggleCatTrackGrade, ctrl: true, shift: true)]
        [SettingsUISection(SectionMain, GroupKeybindings)]
        public ProxyBinding ToggleCatTrackGradeToolBinding { get; set; }

        /// <summary>Toggles the cat track tool's Delete mode (default Ctrl+Shift+D). Live only while that tool is open
        /// (CatTrackGradeHotkeySystem).</summary>
        [SettingsUIKeyboardBinding(BindingKeyboard.D, ActCatTrackDeleteMode, ctrl: true, shift: true)]
        [SettingsUISection(SectionMain, GroupKeybindings)]
        public ProxyBinding CatTrackDeleteModeBinding { get; set; }

        /// <summary>Toggles the Resort Planner tool (default Ctrl+Shift+Q). Polled by QuickRunsHotkeySystem in a loaded city. It is inert
        /// while the QuickRunsEnabled option is off. The tool scans and builds with the BuildLiftLine key (Enter).</summary>
        [SettingsUIKeyboardBinding(BindingKeyboard.Q, ActToggleQuickRuns, ctrl: true, shift: true)]
        [SettingsUISection(SectionMain, GroupKeybindings)]
        public ProxyBinding ToggleQuickRunsToolBinding { get; set; }

        /// <summary>Write-only button with the game's confirmation dialog (message: GetOptionWarningLocaleID,
        /// AutomaticSettings.cs:1146-1166). Same pattern as GeneralSettings.resetSettings (GeneralSettings.cs:138-146). Restores every
        /// option and the key bindings to their defaults, then saves.</summary>
        [SettingsUIButton]
        [SettingsUIConfirmation]
        [SettingsUISection(SectionMain, GroupReset)]
        public bool ResetToDefaults
        {
            set
            {
                SetDefaults();
                // ResetKeyBindings regenerates the attribute defaults (SetDefaults must not) and saves. Without registered bindings, save
                // here
                if (keyBindingRegistered) ResetKeyBindings();
                else ApplyAndSave();
            }
        }

        /// <summary>Write-only button with the game's confirmation dialog: deletes every lift, station, building, run, cat track, fence and
        /// planted tree of this mod in the loaded city (SkiDeleteAllSystem). Disabled outside a loaded Game-mode city (main menu, Editor,
        /// loading). The request refuses there too. Not an option: nothing is saved.</summary>
        [SettingsUIButton]
        [SettingsUIConfirmation]
        [SettingsUIDisableByCondition(typeof(SkiResortSettings), nameof(DeleteAllUnavailable))]
        [SettingsUISection(SectionMain, GroupReset)]
        public bool DeleteAllResortContent
        {
            set { SkiDeleteAllSystem.Request("options button"); }
        }

        /// <summary>The Delete all button's disable condition (AutomaticSettings.TryGetAction: a method with no parameters returning bool).</summary>
        public bool DeleteAllUnavailable() => SkiDeleteAllSystem.Unavailable();

        public override void SetDefaults()
        {
            SkiAreaRadiusKm = SkiAreaRadiusRules.DefaultKm;
            AttractivenessScaleK = DefaultAttractivenessScaleK;
            BackcountryValuePercent = DefaultBackcountryValuePercent;
            LiftTicketPrice = SkiMoneyRules.TicketPriceDefault;
            LodgeSpendPerGuest = DefaultLodgeSpendPerGuest;
            SeasonPassShare = DefaultSeasonPassShare;
            SeasonPassMultiplier = DefaultSeasonPassMultiplier;
            LessonShare = DefaultLessonShare;
            LessonMultiplier = DefaultLessonMultiplier;
            OffSeasonHalfStaff = OffSeasonRules.DefaultEnabled;
            SkiDayLocalsPct = DefaultSkiDayLocalsPct;
            SkiDayTouristsPct = DefaultSkiDayTouristsPct;
            SkiDayReachKm = DefaultSkiDayReachKm;
            SkiDayPowderPct = DefaultSkiDayPowderPct;
            EnableSnowGating = DefaultEnableSnowGating;
            SnowmakingBonusCelsius = DefaultSnowmakingBonusCelsius;
            EnableBindOnDraw = true;
            ShowRunGradeWhileDrawing = true;
            ContourLinesWhileDrawingRuns = true;
            ShowSnowLineWhilePlacing = true;
            ShowSkiAreaRadius = true;
            ShowRunNamesOnTheHill = true;
            RunOutlineSize = DefaultRunOutlineSize;
            SnowGunPlumes = DefaultSnowGunPlumes;
            Groomers = DefaultGroomers;
            CatTrackGradeMaxPct = 25;
            CatTrackWidthM = 20;
            CatTrackGradeCutRun = 1.5f;
            CatTrackGradeFillRun = 2f;
            CatTrackGradeConstant = false;
            QuickRunsEnabled = false;   // experimental: off until the player turns it on (DECISIONS 443)
            ShowHubButton = true;
            OpenHubOnSignSelect = true;
            LiftBuildAids = true;
            LiftTowerPlanner = true;
            AnimatedLiftCarriers = DefaultAnimatedLiftCarriers;
            DiagnosticMeasurements = DefaultDiagnosticMeasurements;
            PlannerOnMainThread = DefaultPlannerOnMainThread;
            // Key bindings are deliberately absent: the base constructor generated them (ModSetting.cs:43, 56-64) before this runs
        }
    }
}
