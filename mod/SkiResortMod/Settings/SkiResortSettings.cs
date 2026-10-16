// Mod options (Options → Mods → Modular Ski Resort).
//
// SPEC §10: "A setting for every balance constant" — players must be able to fix our tuning
// mistakes without waiting for a patch.
//
// BALANCE AND SNOW (Round E1, docs/research/econ-round/e1-spec.md §2.3-2.4; stage 0 owns this file
// and its texts, R10). The five options below are live, each with ONE reader:
//   * AttractivenessScaleK (K, default 4) and BackcountryValuePercent (default 40): the attraction
//     model v2 (SkiResortAttractivenessSystem → SkiResortScore, econ-round spec ch1 §2), read as
//     Mod.Settings?.X ?? DefaultX and clamped there; the hub's CityStats echoes them.
//   * LiftTicketPrice (Round E1 ch4, D26): read live by SkiLiftTicketSystem through
//     SkiMoneyRules.ClampTicketPrice(Mod.Settings?.LiftTicketPrice ?? SkiMoneyRules.TicketPriceDefault);
//     0 turns tickets off. Persisted under this property name — never rename.
//   * BookTicketsAsFees (DECISIONS 239, 241): read live by SkiLiftTicketSystem through
//     Mod.Settings?.BookTicketsAsFees ?? DefaultBookTicketsAsFees; default on. It books lift tickets, passes, lessons and lodge spend
//     as public transportation fees (label "Book resort income as public transportation fees"). Persisted under this property
//     name — never rename.
//   * SeasonPassShare / SeasonPassMultiplier (DECISIONS 240): read live by SkiLiftTicketSystem through
//     SkiMoneyRules.ClampPassShare / ClampPassMultiplier (Mod.Settings?.X ?? DefaultX); defaults 50 % (0 = passes off) and 4.
//     Persisted under these property names — never rename.
//   * LessonShare / LessonMultiplier (DECISIONS 241): the lesson fee, read live by SkiLiftTicketSystem
//     through SkiMoneyRules.ClampLessonShare / ClampLessonMultiplier (Mod.Settings?.X ?? DefaultX); defaults 20 % (0 = lessons off)
//     and 1.5 × the lift ticket. LessonMultiplier is a float slider (Game.UI.Menu AutomaticSettings.cs:1083: float + SettingsUISlider =
//     FloatSlider). Persisted under these property names — never rename.
//   * EnableSnowGating (D13): read only through SnowLineSystem.IsSnowGatingEnabled() (true when the
//     settings are not loaded); the score and the hub call that.
//   * SnowmakingBonusCelsius (D14, default 3.3 since T09 (38 F, Richard 2026-10-16; was 2, before that 3): THE snowmaking margin, a float
//     slider since T09 (the property was an int; saved ints load into a float, so an options file keeps its stored value), read only through
//     SnowLineSystem.GetSnowmakingMarginC() (clamped 0..6).
// The Default* constants are the one copy of each default: SetDefaults and every null-settings
// fallback use them. Property names are persisted — never rename (labels may change, D15).
//
// RUN OUTLINE (Round E1 addendum A1; Round F7 item C1, docs/research/field-test-7-fixes.md):
// RunOutlineSize (0-16, default 8) is ONLY the thickness of the outline and corner dots that
// SkiRunOutlineSystem draws over a ski run while it is drawn, edited or bulldozed: 0.3 × size m
// (8 → 2.4 m); 0 draws nothing (the game's thin outline only). Read every frame, so a change shows
// at once. Since F7 it no longer touches AreaGeometryData.m_SnapDistance: snapping (0.75 m) and the
// minimum corner distance (0.375 m) are the game's again (A1 wrote the setting there, which also
// made snapping 8 m and corners 4 m apart). Persisted under this property name — never rename.
//
// PROP DENSITY (Round M2 option 5, ruling R8; docs/research/round-m2/understand/density.md, build-contract.md §6.9):
// SnowGunDensityPct and FloodlightDensityPct (Runs group, 0-100 step 5, default RunPropDensityRules.PctDefault = 100)
// are LOOK ONLY: how many corners of our propped runs get a snow gun or hydrant (snowmaking and night runs) and a
// floodlight mast (night runs). One reader, SkiRunPropSystem, through RunPropDensityRules.ClampPct(Mod.Settings?.X ??
// PctDefault), polled every frame in a loaded city: it writes the run prefabs' per-corner chance and the night group's
// member weights at every game load and once the pair has been still for RunPropDensityRules.SettleFrames frames, and
// re-stamps the runs already built only after such a settled change (never at a load, so a save made at another
// density keeps its layout until a run is edited). Snow and upkeep never read them. Persisted under these property
// names — never rename.
//
// LIFT TICKET PRICE readers (doc drift fixed in Round M2, hub-price.md §4 Open): besides SkiLiftTicketSystem (whose
// clamped Price SkiDaySystem reads for the ski-day price factor), SkiResortStatsSystem.ReadCity reads the setting for
// the hub's Money view. The Options description (Settings/LocaleEN.cs) says what the price does since Round M2.
//
// Pattern follows the official ModSetting idiom (Game.Modding.ModSetting), the same shape
// Tourism Overhaul uses, so the two mods' options pages behave identically.
//
// KEY BINDINGS (glade tool, docs/research/glade-tool-spec.md §5). Mod keys are plain
// ModSetting members — no UIInputActionCollection, no reflection:
//   * ModSetting discovers ONLY public get+set properties typed ProxyBinding
//     (ModSetting.cs:32-34). Its constructor generates each default binding from the
//     [SettingsUIKeyboardBinding] attribute (ModSetting.cs:43, 56-64; action name =
//     attribute.actionName ?? property name, :78; key + modifiers →
//     "<Keyboard>/g" + "<Keyboard>/ctrl" etc., SettingsUIKeyboardBindingAttribute.cs:41,
//     119-136; ctor :153-160). So SetDefaults must NOT assign them — that would replace
//     the generated binding with an empty default(ProxyBinding).
//   * The class-level [SettingsUIKeyboardAction(name)] gives each action's keyboard
//     composite the default usages (SettingsUIKeyboardActionAttribute.cs:9;
//     SettingsUIInputActionAttribute.cs:41-51, read by RegisterKeyBindings at
//     ModSetting.cs:150-153, 183-192). Without it the composite carries no usages
//     (ModSetting.cs:195-197) and the options row never shows a conflict warning.
//   * The actions only exist once Mod.OnLoad has called RegisterKeyBindings()
//     (ModSetting.cs:141-231 → InputManager.AddActions, :210); before that GetAction
//     returns null (:289-292). Mod actions start disabled (ProxyAction.cs:322-349) —
//     SkiGladeHotkeySystem enables them per game mode.
//   * Conflicts: a mod action always LOSES to an enabled vanilla system/UI action and is
//     silently disabled (InputConflictResolution.cs:118-190; mods are never built-in,
//     ModSetting.cs:181). Defaults therefore avoid camera keys. Whether vanilla binds
//     Ctrl/Shift/Alt+G anywhere is input-asset data, not code — unverified; the options
//     row's conflict triangle answers it in the field (spec §7 step 1).
//
// R3 (docs/research/run-binding-glade-spec.md §5.2, decision D13; DECISIONS 121):
//   * Toggle default Ctrl+G → Ctrl+Shift+G. Field test 2: Ctrl+G showed a conflict marker
//     (Richard rebound it to Shift+Ctrl+G); the spec attributes it to two installed mods'
//     defaults (MertsToolBox OpenGridToolKey, MoveIt Key_TB_TerrainHeight — likely, not
//     verified here: other mods are not decompiled). A player with a SAVED rebind keeps it (the
//     settings file holds the binding); whether a changed DEFAULT reaches players who never
//     rebound is unverified.
//   * New DumpDiagnostics action, default Ctrl+Alt+G: writes the drawing-feedback and/or glade
//     state to the mod log, also while a run is on the cursor (the dev-tab button cannot: the
//     cursor leaves the map). Why the G family: every live probe so far logged ctrl/shift/alt+G
//     all live (field log 244); a vanilla binding on G that disallowed modifiers would have
//     disabled all three (PathEquals uses the path-only comparer when either side disallows
//     modifiers, ProxyBinding.cs:822-825; the runtime pass, InputConflictResolution.cs:148-182,
//     disables the losing mod action). Ctrl+Alt+G vs our Alt+G: an Allow-modifiers composite
//     gets a prohibition processor for every supported modifier the binding does not use
//     (InputManager.cs:2104-2112), so Alt+G should not fire on Ctrl+Alt+G (the Unity-side
//     processor behaviour is unverified). The spec's lens proposal Ctrl+Shift+D was rejected:
//     D is vanilla's camera-right key (input-asset data, unverified) and a press would pan the
//     camera off the run being dumped. Another installed mod on Ctrl+Alt+G: unverified (mod
//     vs mod conflicts both fire, InputConflictResolution.cs:138-198).
//   All four files cited above are unchanged in 1.6.2 (not in diff-1.6.0-to-1.6.2).
//
// HUB (Round U1, docs/research/resort-hub-understand.md §5.1 OD2 / §5.2 item 2; implementer U1-B,
// 2026-10-01): ShowHubButton (the top-left FloatingButton; the Universal Mod Panel button is always
// there) and OpenHubOnSignSelect (selecting a Ski Area Welcome Sign opens the hub once per selection
// change, never closes it). Both default true, both read live by SkiResortHubUISystem every frame
// (a bool read; the binding pushes only on change), so a change takes effect at once.
//
// LIFTS (Round L1, docs/research/hub-u2-spec.md §2.1 / §2.4; implementer I-4, 2026-10-03): LiftBuildAids
// (Settings ▸ Main ▸ Lifts, default true) switches the station alignment aid (guide line + offset
// readout while a station is placed or nudged, SkiStationAlignSystem / SkiLiftTooltipSystem) and the
// bulldozer warning on a lift (SkiLiftTooltipSystem). Read live by those systems (I-2), so a change
// takes effect at once. Persisted under this property name — never rename.
//
// SKI DAYS (Round S1, docs/research/ski-days/s1-spec.md §2.11-§2.12, ski-days spec ch4 §E; stage 0 owns this
// file and its texts). Group "Ski days" after Balance, four int sliders, persisted under these property names —
// never rename: SkiDayLocalsPct (0-50 step 5, default 25), SkiDayTouristsPct (0-100 step 5, default 50),
// SkiDayReachKm (2-12 step 1, default 7; unit "integer" with "(km)" in the label, Game.UI/Unit.cs:5, because
// "length" would read the value as metres) and SkiDayPowderPct (0-200 step 10, default 100). The Default / Min /
// Max constants are ALIASES of SkiDayRules' (s1-spec N8: one source of the numbers; tests/ResortBatch links
// SkiDayRules.cs, not this file). Read only through the clamped readers SkiDayLocalsPctOf, SkiDayTouristsPctOf,
// SkiDayReachKmOf and SkiDayPowderPctOf(Mod.Settings) — null settings give the default; main thread only (the
// jobs get values): SkiDaySystem reads all four live every pass, so a change applies at its next pass;
// SkiResortStatsSystem reads Locals and Tourists (both 0 = ski days off, the hub's "Off" line).
// SkiDayRules.ClampKnobs is a second, defensive clamp.
//
// LODGE SPEND (Catalog Phase B, docs/research/art/assets-2026-10/code-round-2/round-2-contract.md §9, D15; ROUND-SPEC
// R11 "lodge spend OFF by default", reversed by DECISIONS 241, revenue-design §6a: default 15, 0 = off): LodgeSpendPerGuest
// (Balance group, directly after LiftTicketPrice; 0-50 step 5, unit "money"). What each PAYING ski guest also spends at the lodge they arrive at, × the lodge
// tier's SkiCatalog.LodgeDef.SpendMultiplier, rounded half up; charged in SkiLiftTicketSystem's ticket job after the
// ticket, never into debt, and only while the ticket gate is open — a ticket price of 0 charges nothing. Read live by
// SkiLiftTicketSystem through SkiMoneyRules.ClampLodgeSpend(Mod.Settings?.LodgeSpendPerGuest ?? LodgeSpendDefault).
// The Default / Max / Step constants alias SkiCatalog.LodgeSpend (one source). Persisted under this property name —
// never rename.

using Colossal.IO.AssetDatabase;
using Game.Input;
using Game.Modding;
using Game.Settings;
using SkiResortMod.Systems;

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
    [SettingsUIGroupOrder(GroupHub, GroupBalance, GroupSkiDays, GroupSnow, GroupTerrain, GroupRuns, GroupLifts, GroupKeybindings)]
    [SettingsUIShowGroupName(GroupHub, GroupBalance, GroupSkiDays, GroupSnow, GroupTerrain, GroupRuns, GroupLifts, GroupKeybindings)]
    // One keyboard action declaration per binding (conflict warnings; see header).
    [SettingsUIKeyboardAction(ActToggleGlade)]
    [SettingsUIKeyboardAction(ActGladeNextVariant)]
    [SettingsUIKeyboardAction(ActGladeNextMode)]
    [SettingsUIKeyboardAction(ActDumpDiagnostics)]
    public sealed class SkiResortSettings : ModSetting
    {
        public const string SectionMain = "Main";
        public const string GroupHub = "Hub";            // Round U1
        public const string GroupBalance = "Balance";
        public const string GroupSkiDays = "SkiDays";    // Round S1 (s1-spec §2.11)
        public const string GroupSnow = "Snow";
        public const string GroupTerrain = "Terrain";
        public const string GroupRuns = "Runs";
        public const string GroupLifts = "Lifts";        // Round L1 (hub-u2-spec.md §2.1)
        public const string GroupKeybindings = "Keybindings";

        // Action names inside our input map (map name = ModSetting.id,
        // "SkiResortMod.SkiResortMod.Mod", ModSetting.cs:39, 245). Persisted in the
        // settings file and the player's rebinds — never rename. Round G3 (spec §8.3): the
        // names stay although the meaning changed — GladeNextVariant steps the tree density
        // (1-8), GladeNextMode toggles run interior / tree line.
        public const string ActToggleGlade = "ToggleGladeTool";
        public const string ActGladeNextVariant = "GladeNextVariant";
        public const string ActGladeNextMode = "GladeNextMode";
        public const string ActDumpDiagnostics = "DumpDiagnostics";   // R3 (spec §5.2) — persisted, never rename

        // Round E1 (e1-spec §2.3; econ-round spec ch1 §11.1, ch3 §9): the one copy of each default.
        public const int DefaultAttractivenessScaleK = 4;
        public const int DefaultBackcountryValuePercent = 40;
        public const bool DefaultEnableSnowGating = true;
        public const bool DefaultBookTicketsAsFees = true;   // DECISIONS 239
        public const int DefaultSeasonPassShare = SkiMoneyRules.PassShareDefault;             // 50 % (0 = passes off); DECISIONS 240
        public const int DefaultSeasonPassMultiplier = SkiMoneyRules.PassMultiplierDefault;   // 4 lift tickets
        public const int DefaultLessonShare = SkiMoneyRules.LessonShareDefault;               // 20 % (0 = lessons off); DECISIONS 241
        public const float DefaultLessonMultiplier = SkiMoneyRules.LessonMultiplierDefault;   // 1.5 lift tickets
        public const float DefaultSnowmakingBonusCelsius = RunSnowRules.DefaultSnowmakingMarginC;   // 3.3 (T09: 38 F; D14 was 2, before that 3)
        // Round E1 addendum A1, kept in F7: 8 = the look of the game's lot outline (snap distance 8 × 0.3,
        // AreaBorderRenderSystem.cs:517, :531): outline and corner dots 2.4 m. Since F7 it sets nothing else.
        public const int DefaultRunOutlineSize = 8;
        // Round S1 (s1-spec §2.11, N8): aliases of the SkiDayRules constants — the one source of the numbers.
        public const int DefaultSkiDayLocalsPct = SkiDayRules.LocalsPctDefault;       // 25
        public const int SkiDayLocalsPctMax = SkiDayRules.LocalsPctMax;               // 50
        public const int DefaultSkiDayTouristsPct = SkiDayRules.TouristsPctDefault;   // 50
        public const int SkiDayTouristsPctMax = SkiDayRules.TouristsPctMax;           // 100
        public const int DefaultSkiDayReachKm = SkiDayRules.ReachKmDefault;           // 7
        public const int SkiDayReachKmMin = SkiDayRules.ReachKmMin;                   // 2
        public const int SkiDayReachKmMax = SkiDayRules.ReachKmMax;                   // 12
        public const int DefaultSkiDayPowderPct = SkiDayRules.PowderPctDefault;       // 100
        public const int SkiDayPowderPctMax = SkiDayRules.PowderPctMax;               // 200
        // Catalog Phase B (round-2-contract §9, D15): aliases of SkiCatalog.LodgeSpend — the one source of the numbers.
        public const int DefaultLodgeSpendPerGuest = SkiCatalog.LodgeSpend.Default;   // 15 (DECISIONS 241; was 0 = off, R11)
        public const int LodgeSpendPerGuestMax = SkiCatalog.LodgeSpend.Max;           // 50
        public const int LodgeSpendPerGuestStep = SkiCatalog.LodgeSpend.Step;         // 5

        public SkiResortSettings(IMod mod) : base(mod)
        {
            SetDefaults();
        }

        // --- Hub (Round U1, resort-hub-understand.md §5.2 item 2) ---------------------------
        // Persisted under these property names — never rename.

        /// <summary>Show the Ski Resort hub's top-left FloatingButton (mouse/keyboard layout only:
        /// the GameTopLeft hook does not render in the gamepad layout, research §2.E.1). The
        /// Universal Mod Panel button is always shown. Pushed to the UI as skiResort.showHubButton.</summary>
        [SettingsUISection(SectionMain, GroupHub)]
        public bool ShowHubButton { get; set; }

        /// <summary>Selecting a Ski Area Welcome Sign opens the Ski Resort hub (once per selection
        /// change; never forces it closed). SkiResortHubUISystem.</summary>
        [SettingsUISection(SectionMain, GroupHub)]
        public bool OpenHubOnSignSelect { get; set; }

        // --- Balance (Round E1 model v2, econ-round spec ch1 §2; group label "Ski area balance") ---

        /// <summary>K — Round E1 model v2 (econ-round spec ch1 §2): A = lodge base + K × sqrt(runs and
        /// running lifts) × variety × vertical, × the Welcome Sign's efficiency, at most 200. Read by
        /// SkiResortAttractivenessSystem as Mod.Settings?.AttractivenessScaleK ?? DefaultAttractivenessScaleK,
        /// clamped SkiResortScore.MinK..MaxK. Persisted under this property name — never rename.</summary>
        [SettingsUISlider(min = 1f, max = 10f, step = 1f, unit = "integer")]
        [SettingsUISection(SectionMain, GroupBalance)]
        public int AttractivenessScaleK { get; set; }

        /// <summary>Backcountry run value — Round E1 model v2 (econ-round spec ch1 §2): what a run no
        /// complete, powered lift serves is worth, in percent of a lift-served run (default 40). Read by
        /// SkiResortAttractivenessSystem with the DefaultBackcountryValuePercent fallback. Persisted under
        /// this property name — never rename.</summary>
        [SettingsUISlider(min = 0f, max = 100f, step = 5f, unit = "percentage")]
        [SettingsUISection(SectionMain, GroupBalance)]
        public int BackcountryValuePercent { get; set; }

        /// <summary>LiftTicketPrice (Round E1 ch4, D26): read live by SkiLiftTicketSystem (and by SkiResortStatsSystem
        /// for the hub; header LIFT TICKET PRICE readers). Persisted under
        /// this property name — never rename. 0 = tickets off; 0-200 step 5 (SkiMoneyRules.TicketPriceMax /
        /// TicketPriceStep); default SkiMoneyRules.TicketPriceDefault (60 since DECISIONS 241; was 40). unit "money" = Game.UI.Unit.kMoney
        /// (Game.UI/Unit.cs:49, 1.6.0f1 and 1.6.2f1; fallback "integer" if the Options row renders it badly,
        /// UNVERIFIED until field step 2).</summary>
        [SettingsUISlider(min = 0f, max = 200f, step = 5f, unit = "money")]
        [SettingsUISection(SectionMain, GroupBalance)]
        public int LiftTicketPrice { get; set; }

        /// <summary>LodgeSpendPerGuest (Catalog Phase B, round-2-contract §9, D15; header LODGE SPEND): what each paying
        /// ski guest also spends at the lodge they arrive at (× the tier's SpendMultiplier), charged with the ticket. Read
        /// live by SkiLiftTicketSystem through SkiMoneyRules.ClampLodgeSpend. 0 = off (the default, R11). Persisted under
        /// this property name — never rename. unit "money" as LiftTicketPrice.</summary>
        [SettingsUISlider(min = 0f, max = LodgeSpendPerGuestMax, step = LodgeSpendPerGuestStep, unit = "money")]
        [SettingsUISection(SectionMain, GroupBalance)]
        public int LodgeSpendPerGuest { get; set; }

        /// <summary>BookTicketsAsFees (DECISIONS 239, 241; docs/research/econ-2/build/stage1a-fare-booking.md): on = each paid lift
        /// ticket, season pass, lesson and lodge spend is booked as a public transportation fee (a ServiceFeeSystem FeeEvent, like the
        /// vanilla bus fare), so the income shows in the Economy panel and the budget credits the treasury; off = a direct add to the
        /// city's balance. Label "Book resort income as public transportation fees".
        /// Read live by SkiLiftTicketSystem (ReadBookAsFees; true when settings are not loaded). Default on. Persisted under
        /// this property name — never rename.</summary>
        [SettingsUISection(SectionMain, GroupBalance)]
        public bool BookTicketsAsFees { get; set; }

        /// <summary>SeasonPassShare (DECISIONS 240, 241; docs/research/econ-2/build/season-pass-v1.md): the share, in percent, of
        /// resident households that buy a season pass (rolled in the pre-season sale, or at the first ski of the season for households
        /// outside it); 0 = passes off. Read live by
        /// SkiLiftTicketSystem through SkiMoneyRules.ClampPassShare(Mod.Settings?.SeasonPassShare ?? DefaultSeasonPassShare).
        /// Persisted under this property name — never rename.</summary>
        [SettingsUISlider(min = 0f, max = SkiMoneyRules.PassShareMax, step = SkiMoneyRules.PassShareStep, unit = "percentage")]
        [SettingsUISection(SectionMain, GroupBalance)]
        public int SeasonPassShare { get; set; }

        /// <summary>SeasonPassMultiplier (DECISIONS 240): a pass costs this many lift tickets (the price at the moment of
        /// sale, locked). Read live by SkiLiftTicketSystem through SkiMoneyRules.ClampPassMultiplier. Persisted under this
        /// property name — never rename.</summary>
        [SettingsUISlider(min = SkiMoneyRules.PassMultiplierMin, max = SkiMoneyRules.PassMultiplierMax, step = 1f, unit = "integer")]
        [SettingsUISection(SectionMain, GroupBalance)]
        public int SeasonPassMultiplier { get; set; }

        /// <summary>LessonShare (DECISIONS 241; docs/research/econ-2/build/round-2.md): the percentage of the people
        /// counted at a ski area with an ACTIVE ski school who take a lesson that day; 0 = lessons off. Read live by
        /// SkiLiftTicketSystem through SkiMoneyRules.ClampLessonShare. Persisted under this property name — never rename.</summary>
        [SettingsUISlider(min = 0f, max = SkiMoneyRules.LessonShareMax, step = SkiMoneyRules.LessonShareStep, unit = "percentage")]
        [SettingsUISection(SectionMain, GroupBalance)]
        public int LessonShare { get; set; }

        /// <summary>LessonMultiplier (DECISIONS 241): a lesson costs this many lift tickets at the ticket price now (1.0-3.0 step 0.5).
        /// Read live through SkiMoneyRules.ClampLessonMultiplier. Persisted under this property name — never rename.</summary>
        [SettingsUISlider(min = SkiMoneyRules.LessonMultiplierMin, max = SkiMoneyRules.LessonMultiplierMax, step = SkiMoneyRules.LessonMultiplierStep, unit = "floatSingleFraction")]
        [SettingsUISection(SectionMain, GroupBalance)]
        public float LessonMultiplier { get; set; }

        // --- Ski days (Round S1, s1-spec §2.11; ski-days spec ch4 §E; header SKI DAYS) ----------
        // Persisted under these property names — never rename. Read only through the *Of readers below.

        /// <summary>D16: residents' share in percent (0 = no ski days for residents). Read by SkiDaySystem
        /// (and the hub's "off" test) through SkiDayLocalsPctOf.</summary>
        [SettingsUISlider(min = 0f, max = SkiDayLocalsPctMax, step = 5f, unit = "percentage")]
        [SettingsUISection(SectionMain, GroupSkiDays)]
        public int SkiDayLocalsPct { get; set; }

        /// <summary>D17: hotel tourists' share in percent (0 = no ski days for tourists). Read by SkiDaySystem
        /// (and the hub's "off" test) through SkiDayTouristsPctOf.</summary>
        [SettingsUISlider(min = 0f, max = SkiDayTouristsPctMax, step = 5f, unit = "percentage")]
        [SettingsUISection(SectionMain, GroupSkiDays)]
        public int SkiDayTouristsPct { get; set; }

        /// <summary>D14: the longest ski-day trip in km, straight line to the lodge. Unit "integer" with "(km)"
        /// in the label (Game.UI/Unit.cs:5; "length" would read metres). Read by SkiDaySystem through
        /// SkiDayReachKmOf.</summary>
        [SettingsUISlider(min = SkiDayReachKmMin, max = SkiDayReachKmMax, step = 1f, unit = "integer")]
        [SettingsUISection(SectionMain, GroupSkiDays)]
        public int SkiDayReachKm { get; set; }

        /// <summary>D16 / D19: what a full powder day adds, in percent (100 doubles the residents who go). Read
        /// by SkiDaySystem through SkiDayPowderPctOf.</summary>
        [SettingsUISlider(min = 0f, max = SkiDayPowderPctMax, step = 10f, unit = "percentage")]
        [SettingsUISection(SectionMain, GroupSkiDays)]
        public int SkiDayPowderPct { get; set; }

        /// <summary>Clamped read (s1-spec §2.11); null settings → the default. Main thread only.</summary>
        public static int SkiDayLocalsPctOf(SkiResortSettings s)
            => s == null ? DefaultSkiDayLocalsPct : Clamp(s.SkiDayLocalsPct, SkiDayRules.LocalsPctMin, SkiDayRules.LocalsPctMax);

        /// <summary>Clamped read (s1-spec §2.11); null settings → the default. Main thread only.</summary>
        public static int SkiDayTouristsPctOf(SkiResortSettings s)
            => s == null ? DefaultSkiDayTouristsPct : Clamp(s.SkiDayTouristsPct, SkiDayRules.TouristsPctMin, SkiDayRules.TouristsPctMax);

        /// <summary>Clamped read (s1-spec §2.11); null settings → the default. Main thread only.</summary>
        public static int SkiDayReachKmOf(SkiResortSettings s)
            => s == null ? DefaultSkiDayReachKm : Clamp(s.SkiDayReachKm, SkiDayRules.ReachKmMin, SkiDayRules.ReachKmMax);

        /// <summary>Clamped read (s1-spec §2.11); null settings → the default. Main thread only.</summary>
        public static int SkiDayPowderPctOf(SkiResortSettings s)
            => s == null ? DefaultSkiDayPowderPct : Clamp(s.SkiDayPowderPct, SkiDayRules.PowderPctMin, SkiDayRules.PowderPctMax);

        /// <summary>net48 has no Math.Clamp.</summary>
        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

        // --- Snow (Round E1, D13 / D14; econ-round spec ch3 §9) -----------------------------

        /// <summary>E1 D13: On = a run counts toward attraction by its measured snow (open at 60 %, closed
        /// below 40 %). Off = every run counts as open all year. Read by SkiSnowInjectionSystem.GetRunSnow,
        /// through SnowLineSystem.IsSnowGatingEnabled() (the one read; true when settings are not loaded).</summary>
        [SettingsUISection(SectionMain, GroupSnow)]
        public bool EnableSnowGating { get; set; }

        /// <summary>E1 D14: THE snowmaking margin — snow guns (snowmaking and night runs) work where the air
        /// on the run is at most this many °C above freezing. Read by SnowLineSystem.GetSnowmakingMarginC
        /// (injection, estimate, and through the mask the bill). Default 3.3 since T09 (38 F; a float slider, step 0.1;
        /// an options file saved before T09 keeps its stored 2, no migration). Options label "Snowmaking margin
        /// (°C)"; the property keeps its old name (persisted — never rename).</summary>
        [SettingsUISlider(min = 0f, max = 6f, step = 0.1f, unit = "floatSingleFraction")]
        [SettingsUISection(SectionMain, GroupSnow)]
        public float SnowmakingBonusCelsius { get; set; }

        // --- Terrain (ski-run terrain paint — research/terrain-material-runs.md) ----------

        /// <summary>Slot policy for painting runs onto the terrain itself. Auto claims a
        /// free Extra slot only (many CO maps have none); ForceExtraN sacrifices that
        /// map material; Off keeps the classic decal surfaces. Takes effect on load.</summary>
        [SettingsUISection(SectionMain, GroupTerrain)]
        public TerrainSlotPolicy TerrainSnowSlot { get; set; }

        // --- Runs (bind on draw + drawing feedback — research/run-feedback-spec.md) --------
        // Read live every frame by the consuming systems, so a change takes effect at once.
        // Persisted under these property names — never rename.

        /// <summary>Bind a run to its Ski Area Welcome Sign the frame it is drawn (and waiting
        /// runs the frame a sign is placed), paused or not — SkiRunBindTriggerSystem. Off =
        /// binding only by the 64-sim-frame sweep, which never runs while paused.</summary>
        [SettingsUISection(SectionMain, GroupRuns)]
        public bool EnableBindOnDraw { get; set; }

        /// <summary>Slope heat-map overlay and grade tooltip while drawing or editing a run
        /// with the area tool — SkiRunDrawFeedbackSystem + SkiRunTooltipSystem.</summary>
        [SettingsUISection(SectionMain, GroupRuns)]
        public bool ShowRunGradeWhileDrawing { get; set; }

        /// <summary>Terrain contour lines on while a run prefab is selected in the area tool,
        /// restored afterwards — SkiRunContourSystem.</summary>
        [SettingsUISection(SectionMain, GroupRuns)]
        public bool ContourLinesWhileDrawingRuns { get; set; }

        /// <summary>T32 (2026-10-16): while the area tool holds one of our run prefabs or the object tool places the Ski Area
        /// Welcome Sign, draw the WINTER snow line (mean December-February climate temperature, lapsed with elevation, crosses
        /// 0 C) and a fainter snowmaking line on the terrain, with one tooltip line — SkiSnowLineOverlaySystem +
        /// SkiSnowLineTooltipSystem. Read live every frame. Default on. Persisted under this property name — never rename.</summary>
        [SettingsUISection(SectionMain, GroupRuns)]
        public bool ShowSnowLineWhilePlacing { get; set; }

        /// <summary>Round F7 C1 (was E1 addendum A1): the thickness of SkiRunOutlineSystem's overlay — line
        /// width and corner-dot diameter 0.3 × this many metres — on the run areas the game outlines while
        /// the area tool or the bulldozer works on them; 0 = none (the game's thin outline only). Read every
        /// frame. It no longer writes AreaGeometryData.m_SnapDistance, so snapping and the corner rule are
        /// the game's (0.75 m / 0.375 m for a Surface area, AreaUtils.cs:55-58, :410-421). Persisted under
        /// this property name — never rename.</summary>
        [SettingsUISlider(min = 0f, max = 16f, step = 1f, unit = "integer")]
        [SettingsUISection(SectionMain, GroupRuns)]
        public int RunOutlineSize { get; set; }

        /// <summary>Round M2 density (look only, header PROP DENSITY): share of snowmaking and night run corners with
        /// a snow gun or hydrant, in percent of the full look. Read only by SkiRunPropSystem. Persisted under this
        /// property name — never rename.</summary>
        [SettingsUISlider(min = RunPropDensityRules.PctMin, max = RunPropDensityRules.PctMax, step = RunPropDensityRules.PctStep, unit = "percentage")]
        [SettingsUISection(SectionMain, GroupRuns)]
        public int SnowGunDensityPct { get; set; }

        /// <summary>Round M2 density (look only, header PROP DENSITY): share of night run corners with a floodlight
        /// mast, in percent of the full look. Read only by SkiRunPropSystem. Persisted under this property name —
        /// never rename.</summary>
        [SettingsUISlider(min = RunPropDensityRules.PctMin, max = RunPropDensityRules.PctMax, step = RunPropDensityRules.PctStep, unit = "percentage")]
        [SettingsUISection(SectionMain, GroupRuns)]
        public int FloodlightDensityPct { get; set; }

        // --- Lifts (Round L1, hub-u2-spec.md §2.1 / §2.4) ------------------------------------
        // Persisted under this property name — never rename.

        /// <summary>Station alignment aid (guide line and offset readout while a station is placed or
        /// nudged) and the bulldozer warning on a lift — SkiStationAlignSystem + SkiLiftTooltipSystem.
        /// Read live every frame by those systems.</summary>
        [SettingsUISection(SectionMain, GroupLifts)]
        public bool LiftBuildAids { get; set; }

        // --- Key bindings (glade tool, spec §5) ---------------------------------------------
        // MUST stay public get+set ProxyBinding (ModSetting.cs:32-34). Defaults come from the
        // attribute via the base constructor — never assign these in SetDefaults.

        /// <summary>Toggles the glade tool (default Ctrl+Shift+G since R3; was Ctrl+G, which
        /// showed a conflict marker in field test 2 — header R3). Polled every frame, paused or
        /// not, by SkiGladeHotkeySystem.</summary>
        [SettingsUIKeyboardBinding(BindingKeyboard.G, ActToggleGlade, ctrl: true, shift: true)]
        [SettingsUISection(SectionMain, GroupKeybindings)]
        public ProxyBinding ToggleGladeToolBinding { get; set; }

        /// <summary>Round G3 (run-binding-glade-spec.md §8.3): steps the glade tool's tree
        /// density 1 → 8 → 1 (default Shift+G). Property and action name keep their G2 names
        /// (persisted — never rename). Forwarded only while the glade tool is active.</summary>
        [SettingsUIKeyboardBinding(BindingKeyboard.G, ActGladeNextVariant, shift: true)]
        [SettingsUISection(SectionMain, GroupKeybindings)]
        public ProxyBinding GladeNextVariantBinding { get; set; }

        /// <summary>Round G3 (§8.3): toggles the glade tool's target, run interior ⇄ tree line
        /// (default Alt+G). Property and action name keep their G2 names (persisted — never
        /// rename). Forwarded only while the glade tool is active.</summary>
        [SettingsUIKeyboardBinding(BindingKeyboard.G, ActGladeNextMode, alt: true)]
        [SettingsUISection(SectionMain, GroupKeybindings)]
        public ProxyBinding GladeNextModeBinding { get; set; }

        /// <summary>R3 (spec §5.2): dumps diagnostics to the mod log (default Ctrl+Alt+G) —
        /// the run drawing feedback while the area tool is active, the glade tool while it is
        /// active, otherwise both; works with a run on the cursor. Polled by
        /// SkiGladeHotkeySystem independently of the glade keys, in a loaded city only.</summary>
        [SettingsUIKeyboardBinding(BindingKeyboard.G, ActDumpDiagnostics, ctrl: true, alt: true)]
        [SettingsUISection(SectionMain, GroupKeybindings)]
        public ProxyBinding DumpDiagnosticsBinding { get; set; }

        public override void SetDefaults()
        {
            AttractivenessScaleK = DefaultAttractivenessScaleK;         // E1 (e1-spec §2.3)
            BackcountryValuePercent = DefaultBackcountryValuePercent;   // E1
            LiftTicketPrice = SkiMoneyRules.TicketPriceDefault;         // E1 D26 (ch4 §5.4)
            LodgeSpendPerGuest = DefaultLodgeSpendPerGuest;             // Catalog Phase B (round-2-contract §9, D15): 15 since DECISIONS 241, 0 = off
            BookTicketsAsFees = DefaultBookTicketsAsFees;               // DECISIONS 239: on
            SeasonPassShare = DefaultSeasonPassShare;                   // DECISIONS 240: 50 %, 0 = off
            SeasonPassMultiplier = DefaultSeasonPassMultiplier;         // 4 lift tickets
            LessonShare = DefaultLessonShare;                           // DECISIONS 241: 20 %, 0 = off
            LessonMultiplier = DefaultLessonMultiplier;                 // 1.5 lift tickets
            SkiDayLocalsPct = DefaultSkiDayLocalsPct;                   // S1 (s1-spec §2.11)
            SkiDayTouristsPct = DefaultSkiDayTouristsPct;               // S1
            SkiDayReachKm = DefaultSkiDayReachKm;                       // S1
            SkiDayPowderPct = DefaultSkiDayPowderPct;                   // S1
            EnableSnowGating = DefaultEnableSnowGating;                 // E1 D13
            SnowmakingBonusCelsius = DefaultSnowmakingBonusCelsius;     // E1 D14: the field-tuned margin (was the code's fixed 2 °C)
            TerrainSnowSlot = TerrainSlotPolicy.Auto;
            EnableBindOnDraw = true;              // run-feedback-spec §2
            ShowRunGradeWhileDrawing = true;      // run-feedback-spec §2
            ContourLinesWhileDrawingRuns = true;  // run-feedback-spec §2
            ShowSnowLineWhilePlacing = true;      // T32 (default on)
            RunOutlineSize = DefaultRunOutlineSize;   // E1 addendum A1 (thickness only since F7 C1)
            SnowGunDensityPct = RunPropDensityRules.PctDefault;      // M2 density (look only)
            FloodlightDensityPct = RunPropDensityRules.PctDefault;   // M2 density (look only)
            ShowHubButton = true;                 // resort-hub-understand.md §5.1 OD2 (default on)
            OpenHubOnSignSelect = true;           // §5.1 / §5.2 item 2 (default on)
            LiftBuildAids = true;                 // hub-u2-spec.md §2.4 (default on)
            // Key bindings deliberately absent: the base constructor generated them
            // (ModSetting.cs:43, 56-64) before this runs.
        }
    }
}
