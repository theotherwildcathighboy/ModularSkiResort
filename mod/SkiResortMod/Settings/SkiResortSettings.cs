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
//   * EnableSnowGating (D13): read only through SnowLineSystem.IsSnowGatingEnabled() (true when the
//     settings are not loaded); the score and the hub call that.
//   * SnowmakingBonusCelsius (D14, default 2, was 3): THE snowmaking margin, read only through
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
    [SettingsUIGroupOrder(GroupHub, GroupBalance, GroupSnow, GroupTerrain, GroupRuns, GroupLifts, GroupKeybindings)]
    [SettingsUIShowGroupName(GroupHub, GroupBalance, GroupSnow, GroupTerrain, GroupRuns, GroupLifts, GroupKeybindings)]
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
        public const int DefaultSnowmakingBonusCelsius = 2;   // = RunSnowRules.DefaultSnowmakingMarginC (D14; was 3)
        // Round E1 addendum A1, kept in F7: 8 = the look of the game's lot outline (snap distance 8 × 0.3,
        // AreaBorderRenderSystem.cs:517, :531): outline and corner dots 2.4 m. Since F7 it sets nothing else.
        public const int DefaultRunOutlineSize = 8;

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

        /// <summary>LiftTicketPrice (Round E1 ch4, D26): read live by SkiLiftTicketSystem. Persisted under
        /// this property name — never rename. 0 = tickets off; 0-200 step 5 (SkiMoneyRules.TicketPriceMax /
        /// TicketPriceStep); default SkiMoneyRules.TicketPriceDefault (40). unit "money" = Game.UI.Unit.kMoney
        /// (Game.UI/Unit.cs:49, 1.6.0f1 and 1.6.2f1; fallback "integer" if the Options row renders it badly,
        /// UNVERIFIED until field step 2).</summary>
        [SettingsUISlider(min = 0f, max = 200f, step = 5f, unit = "money")]
        [SettingsUISection(SectionMain, GroupBalance)]
        public int LiftTicketPrice { get; set; }

        // --- Snow (Round E1, D13 / D14; econ-round spec ch3 §9) -----------------------------

        /// <summary>E1 D13: On = a run counts toward attraction by its measured snow (open at 60 %, closed
        /// below 40 %). Off = every run counts as open all year. Read by SkiSnowInjectionSystem.GetRunSnow,
        /// through SnowLineSystem.IsSnowGatingEnabled() (the one read; true when settings are not loaded).</summary>
        [SettingsUISection(SectionMain, GroupSnow)]
        public bool EnableSnowGating { get; set; }

        /// <summary>E1 D14: THE snowmaking margin — snow guns (snowmaking and night runs) work where the air
        /// on the run is at most this many °C above freezing. Read by SnowLineSystem.GetSnowmakingMarginC
        /// (injection, estimate, and through the mask the bill). Default 2. Options label "Snowmaking margin
        /// (°C)"; the property keeps its old name (persisted — never rename).</summary>
        [SettingsUISlider(min = 0f, max = 6f, step = 1f, unit = "integer")]
        [SettingsUISection(SectionMain, GroupSnow)]
        public int SnowmakingBonusCelsius { get; set; }

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

        /// <summary>Round F7 C1 (was E1 addendum A1): the thickness of SkiRunOutlineSystem's overlay — line
        /// width and corner-dot diameter 0.3 × this many metres — on the run areas the game outlines while
        /// the area tool or the bulldozer works on them; 0 = none (the game's thin outline only). Read every
        /// frame. It no longer writes AreaGeometryData.m_SnapDistance, so snapping and the corner rule are
        /// the game's (0.75 m / 0.375 m for a Surface area, AreaUtils.cs:55-58, :410-421). Persisted under
        /// this property name — never rename.</summary>
        [SettingsUISlider(min = 0f, max = 16f, step = 1f, unit = "integer")]
        [SettingsUISection(SectionMain, GroupRuns)]
        public int RunOutlineSize { get; set; }

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
            EnableSnowGating = DefaultEnableSnowGating;                 // E1 D13
            SnowmakingBonusCelsius = DefaultSnowmakingBonusCelsius;     // E1 D14: the field-tuned margin (was the code's fixed 2 °C)
            TerrainSnowSlot = TerrainSlotPolicy.Auto;
            EnableBindOnDraw = true;              // run-feedback-spec §2
            ShowRunGradeWhileDrawing = true;      // run-feedback-spec §2
            ContourLinesWhileDrawingRuns = true;  // run-feedback-spec §2
            RunOutlineSize = DefaultRunOutlineSize;   // E1 addendum A1 (thickness only since F7 C1)
            ShowHubButton = true;                 // resort-hub-understand.md §5.1 OD2 (default on)
            OpenHubOnSignSelect = true;           // §5.1 / §5.2 item 2 (default on)
            LiftBuildAids = true;                 // hub-u2-spec.md §2.4 (default on)
            // Key bindings deliberately absent: the base constructor generated them
            // (ModSetting.cs:43, 56-64) before this runs.
        }
    }
}
