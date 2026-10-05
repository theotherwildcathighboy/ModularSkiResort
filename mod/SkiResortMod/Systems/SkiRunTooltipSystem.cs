// SkiRunTooltipSystem — the mouse tooltip shown while the vanilla area tool draws or edits one
// of our ski runs (docs/research/run-feedback-spec.md §5 as amended by §9.6 "Round R2";
// DECISIONS 113, 116; R3: docs/research/run-binding-glade-spec.md §5.1, DECISIONS 121; E1: the fee
// row 7). Up to seven lines:
//
//   1. band name ("Blue square", ...)          StringTooltip, Info (Warning for Flat)
//   2. "Hardest unavoidable pitch: 32 %"       IntTooltip, unit "percentage" (0-100 in) —
//                                              RatingGrade, the crux of the easiest line (§9.3).
//      or, when the run's Mask fell apart (Readout.Connected false; the rating is the P90
//      fallback, not a crux): "Typical steep pitch (run outline broken, rating approximate): 32 %",
//      Warning colour, its own path (R2 fixer round)
//   3. "Length: 864 m"                         IntTooltip, unit "length" — R3: Readout.Length,
//                                              HORIZONTAL metres (decision D14), round(Length);
//                                              ring = max(equivalent rectangle, fall-line
//                                              extent), polyline = arc length
//                                              (SkiSlopeClassifier header "RUN LENGTH")
//   4. "Vertical drop: 140 m"                  IntTooltip, unit "length" (metres in; the UI
//                                              converts to m/km or yd/mi)
//   5. "Steepest patch (avoidable): 45 %"      IntTooltip, unit "percentage" — only when
//                                              SkiSlopeClassifier.SteepestPatchShown (below);
//                                              explains red cells on a run rated easier
//                                              (§9.6 item 4)
//   6. "No Ski Area Welcome Sign within 5 km"  StringTooltip, Warning — new runs only
//   7. Run fee (one-time) — IntTooltip, money unit, Money icon; NEW runs only;
//      SkiRunFees.Fee(kind, plan area), the same function the bind charges; Error colour when the
//      city has less money (vanilla TempCostTooltipSystem.cs:79-82). Round E1 (D21; econ-round spec
//      ch2 §6.4; stream S2, 2026-10-04): label key SkiResort.Tooltip.RUN_FEE (root LocaleEN.cs), path
//      "skiRunFee", inputs SkiRunDrawFeedbackSystem.TryGetFeeInputs. It shows even when the rating
//      failed (too large, no terrain): lines 1-5 need a valid rating, line 7 only a new run of ours.
//      ORDER on screen: 1-5, then 7, then 6. The vanilla cost line does not show for our areas (Temp
//      areas carry no m_Cost, and AreaToolTooltipSystem shows nothing for a plain Surface, header
//      ORDER), so there is no double price. Known limit: the row follows the "Show run grade while
//      drawing" setting, like the whole feedback system; with it off the fee shows only in the hub.
//
// R3 (field test 2, DECISIONS 121: Richard wants the run's LENGTH visible while drawing): line 3
// is new, placed between the pitch and the drop (spec §5.1), path "skiRunLength", label key
// SkiResort.Tooltip.RUN_LENGTH ("Length", root LocaleEN.cs). It uses the drop line's IntTooltip +
// "length" unit, which the spec reports as displaying in the field (spec §5.1; this file's
// author did not see it on screen — unverified here).
//
// WHAT CHANGED IN R2 AND WHY (DECISIONS 116): the field test found the R1 rating ("steepest
// 20 m pitch", the k-th largest window) rated a meadow Double black because its top corner
// touched a bank. The rating is now the hardest pitch a skier CANNOT avoid, so line 2 is
// relabelled (key CRUX_PITCH; the STEEPEST_PITCH key is retired) and the old statistic returns
// as line 4 only when it would otherwise look contradictory.
//
// R2 FIXER ROUND (review findings, both reproduced in a host linking the classifier):
//  * Line 4's rule moved to SkiSlopeClassifier.SteepestPatchShown so SlopeBatch can sweep it:
//    Connected, SteepestGrade >= RatingGrade + DisplayHysteresis (0.5 %), the two ROUNDED
//    percentages differ, and BandOf(SteepestGrade) above the shown band. Without the margin a
//    uniform 24.90 % plane read "Green / Hardest unavoidable pitch 25 % / Steepest patch
//    (avoidable) 25 %" (quantisation puts SteepestGrade up to +0.29 % above the crux; 61-68 of
//    936 near-threshold plane cases fired). In polyline mode the feedback system publishes
//    SteepestGrade == RatingGrade (a line has one path), so line 4 never shows there.
//  * Readout.Connected is now read: with a broken Mask (a sub-cell neck, a bow-tie) the rating
//    is P90, so line 2 must not claim "unavoidable" and line 4 must not call a full-width
//    headwall "avoidable" (reviewer's 60 × 200 m hourglass with a 0.6-2 m neck: "Blue / Hardest
//    unavoidable pitch 32 % / Steepest patch (avoidable) 45 %" for a full-width 45 % headwall;
//    such necks pass vanilla validation — minNodeDistance 0.375 m, ValidationHelpers.cs:667,
//    887-943 — so this is a committable shape, not only a drawing transient).
//
// Every engine citation is `Game.X/File.cs:line` in tools/decompile/1.6.0f1. None of the cited
// tooltip files (Game.UI.Tooltip/*, Game.UI.Localization/LocalizedString.cs) changed in 1.6.2f1
// (not in diff-1.6.0-to-1.6.2/changed-files.txt).
//
// HOW A MOD TOOLTIP WORKS. TooltipSystemBase (Game.UI.Tooltip/TooltipSystemBase.cs:9) caches
// TooltipUISystem in OnCreate (:14-18); AddMouseTooltip appends to mouseGroup.children and
// drops a duplicate non-empty path with a LogError (:32-42), so our paths are mod-unique
// ("skiRunGrade", "skiRunPitch", "skiRunPitchApprox", "skiRunLength", "skiRunDrop",
// "skiRunSteepest", "skiRunNoGate", E1 "skiRunFee"; at most one of the two pitch lines per frame). TooltipUISystem clears
// mouseGroup.children and THEN runs the whole UITooltip phase, only while the "tooltip.groups"
// binding is active, the mouse is not over UI and tooltips are not hidden
// (TooltipUISystem.cs:48-55) — so this OnUpdate re-adds its lines every frame they should show,
// and needs no over-UI check of its own. The phase is driven from UIUpdate in MainLoop, i.e.
// paused or not (UIUpdateSystem at SystemOrder.cs:67 runs UIUpdate, UIUpdateSystem.cs:19; that
// the UITooltip phase updates while paused is likely, not field-tested).
//
// ORDER. Registered UpdateAfter<SkiRunTooltipSystem, AreaToolTooltipSystem>(UITooltip) in
// Mod.cs: a plain UpdateAt sorts by add index (UpdateSystem.cs:39-47) and would put our lines
// after InputHintsTooltipSystem (registered last, SystemOrder.cs:933). AreaToolTooltipSystem
// (SystemOrder.cs:916; 1.6.2 :917) shows nothing for a plain Surface (AreaToolTooltipSystem.cs:
// 101, 176-183), so our lines follow the (normally absent) cost line and precede notifications
// and input hints.
//
// WIDGETS. Built once in OnCreate as fields (vanilla: TempCostTooltipSystem.cs:44-56,
// AreaToolTooltipSystem.cs:78-95) and never subclassed — Widget.propertiesTypeName is
// GetType().FullName (Widget.cs:74) and the JS renderer knows only the Game.UI.Tooltip.* names;
// IntTooltip renders because NumberTooltip<T> overrides it (NumberTooltip.cs:80). TooltipColor
// has only Info/Success/Warning/Error (TooltipColor.cs:3-9), so bands cannot be coloured
// blue/black — the band NAME carries it. IntTooltip has a colour too (NumberTooltip<T> :
// LabelIconTooltip : IconTooltip, NumberTooltip.cs:6, LabelIconTooltip.cs:7, IconTooltip.cs:31),
// which the approximate-pitch line uses. A label is a LocalizedString compared with
// object.Equals (LabelIconTooltip.cs:21, boxing), so each label lives on its own widget, set
// once in OnCreate, instead of being swapped per frame.
//
// TEXT. Locale keys from the root LocaleEN.cs (registered in Mod.OnLoad through
// localizationManager.AddSource). LocalizedString.Id(key) is a plain struct ctor (:39-42); a
// bare string converts implicitly to a KEY, not raw text (:124-127). Never Id(key, args...) per
// frame: it builds a Dictionary through LINQ (:44-48). The five band strings are a static
// array built once.
//
// ZERO ALLOCATION IN OUR CODE PER FRAME. StringTooltip.value / LabelIconTooltip.label compare
// with object.Equals on the LocalizedString STRUCT (StringTooltip.cs:18, LabelIconTooltip
// setter) — that boxes both sides, so the band line's value is assigned only when the band
// changes. NumberTooltip<int>.value compares through IEquatable<int> (NumberTooltip.cs:24, no
// box) and IconTooltip.color is an enum compare (IconTooltip.cs:39), so those are assigned
// every frame; their setters flag a change only when the value differs. Not ours to avoid:
// the base AddMouseTooltip runs children.Any(lambda) (TooltipSystemBase.cs:34), a closure +
// enumerator per call — the same cost every vanilla tooltip system pays.
//
// DATA. The numbers come from SkiRunDrawFeedbackSystem.TryGetReadout (spec §2/§4): computed in
// the PREVIOUS frame's Rendering phase (MainLoop runs UIUpdate before Rendering,
// SystemOrder.cs:67-68), a one-frame lag by design. The gate is NOT the tool's prefab: in
// Modify the area tool edits any Surface regardless of the selected prefab
// (AreaToolSystem.cs:320-322) and the Temp carries the ORIGINAL run's prefab
// (GenerateAreasSystem.cs:172-175) — the feedback system already filters on that.
//
// SCOPE: Game mode only. Defensive contract per house rules: "created" line, ARMED line per
// load, m_Armed/m_Disabled/m_ErrorLogged reset in OnGameLoadingComplete, one-shot "!!!" catch
// that silences the tooltip until the next game load.

using System;
using Colossal.Serialization.Entities;
using Game;
using Game.Simulation;
using Game.Tools;
using Game.UI.Localization;
using Game.UI.Tooltip;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunTooltipSystem : TooltipSystemBase
    {
        // Locale keys (root LocaleEN.cs; spec §5, §9.6). Band keys are indexed by (int)SlopeBand.
        // SkiResort.Tooltip.STEEPEST_PITCH is retired (R2): line 2 is now the crux.
        public const string KeyBandPrefix = "SkiResort.SlopeBand";
        public const string KeyCrux = "SkiResort.Tooltip.CRUX_PITCH";
        public const string KeyApproxPitch = "SkiResort.Tooltip.APPROX_PITCH";   // R2 fixer round: !Connected (P90 fallback)
        public const string KeyDrop = "SkiResort.Tooltip.VERTICAL_DROP";
        public const string KeyLength = "SkiResort.Tooltip.RUN_LENGTH";       // R3 (run-binding-glade-spec.md §5.1)
        public const string KeySteepest = "SkiResort.Tooltip.STEEPEST_PATCH";
        public const string KeyNoGate = "SkiResort.Tooltip.NO_GATE_IN_RANGE";
        public const string KeyFee = "SkiResort.Tooltip.RUN_FEE";             // E1 (D21, ch2 §4.6 / §6.4): "Run fee (one-time)"

        /// <summary>Built once; index = (int)SlopeBand (Flat 0 .. DoubleBlack 4, spec §2).</summary>
        private static readonly LocalizedString[] BandText =
        {
            LocalizedString.Id(KeyBandPrefix + "[Flat]"),
            LocalizedString.Id(KeyBandPrefix + "[Green]"),
            LocalizedString.Id(KeyBandPrefix + "[Blue]"),
            LocalizedString.Id(KeyBandPrefix + "[Black]"),
            LocalizedString.Id(KeyBandPrefix + "[DoubleBlack]"),
        };

        private ToolSystem m_ToolSystem;
        private AreaToolSystem m_AreaTool;
        private SkiRunDrawFeedbackSystem m_Feedback;
        private CitySystem m_CitySystem;   // E1: moneyAmount for the fee row's colour (CitySystem.cs:31)

        private StringTooltip m_Grade;
        private IntTooltip m_Pitch;       // crux (RatingGrade), path kept as "skiRunPitch"
        private IntTooltip m_PitchApprox; // P90 fallback when the Mask is not connected (Warning)
        private IntTooltip m_Length;      // R3: horizontal run length (m)
        private IntTooltip m_Drop;
        private IntTooltip m_Steepest;    // steepest avoidable patch (R2 line 4)
        private IntTooltip m_Fee;         // E1: one-time run fee (new runs only), money unit
        private StringTooltip m_NoGate;
        private int m_ShownBand = -1;          // band currently written into m_Grade.value

        // Per-load state — reset at OnGameLoadingComplete.
        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;

        /// <summary>Frames on which the lines were added since the last load (dev-tab /
        /// field-test aid; plain field).</summary>
        public long FramesShown;

        protected override void OnCreate()
        {
            base.OnCreate(); // caches TooltipUISystem (TooltipSystemBase.cs:14-18)
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_AreaTool = World.GetOrCreateSystemManaged<AreaToolSystem>();
            m_Feedback = World.GetOrCreateSystemManaged<SkiRunDrawFeedbackSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();

            m_Grade = new StringTooltip
            {
                path = "skiRunGrade",
                color = TooltipColor.Info,
            };
            m_Pitch = new IntTooltip
            {
                path = "skiRunPitch",
                label = LocalizedString.Id(KeyCrux),
                unit = "percentage", // 0-100 in (RaycastElectricityTooltipSystem.cs:133, :251)
            };
            m_PitchApprox = new IntTooltip
            {
                path = "skiRunPitchApprox",
                label = LocalizedString.Id(KeyApproxPitch),
                unit = "percentage", // as m_Pitch
                color = TooltipColor.Warning,
            };
            m_Length = new IntTooltip
            {
                path = "skiRunLength",
                label = LocalizedString.Id(KeyLength),
                unit = "length",     // as m_Drop (spec §5.1: the drop line displays in the field)
            };
            m_Drop = new IntTooltip
            {
                path = "skiRunDrop",
                label = LocalizedString.Id(KeyDrop),
                unit = "length",     // metres in (WaterSourceTooltipSystem.cs:51 height,
                                     // NetCourseTooltipSystem.cs:59 — both FloatTooltip; the
                                     // unit is JS-side, IntTooltip + "length" unverified in-game)
            };
            m_Steepest = new IntTooltip
            {
                path = "skiRunSteepest",
                label = LocalizedString.Id(KeySteepest),
                unit = "percentage", // as m_Pitch
            };
            m_NoGate = new StringTooltip
            {
                path = "skiRunNoGate",
                value = LocalizedString.Id(KeyNoGate),
                color = TooltipColor.Warning,
            };
            // E1 (D21, ch2 §6.4): the vanilla cost row's widget shape (TempCostTooltipSystem.cs:44-56)
            // with our label.
            m_Fee = new IntTooltip
            {
                path = "skiRunFee",
                icon = "Media/Game/Icons/Money.svg",
                label = LocalizedString.Id(KeyFee),
                unit = "money",
            };

            Mod.Log.Info($"{nameof(SkiRunTooltipSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            FramesShown = 0;

            if (mode != GameMode.Game)
                return;

            m_Armed = true;
            Mod.Log.Info("RunTooltip: ARMED — while the area tool holds an eligible run Temp " +
                         "(SkiRunDrawFeedbackSystem readout) and ShowRunGradeWhileDrawing is on: " +
                         "band, hardest unavoidable pitch (%, crux of the easiest line; a Warning " +
                         "'typical steep pitch, approximate' line instead when the run outline is " +
                         "broken and the rating is the P90 fallback), run length (R3: horizontal m, " +
                         "max of equivalent rectangle and fall-line extent; arc for a line), " +
                         "vertical drop (m), steepest " +
                         "avoidable patch (%) only when SkiSlopeClassifier.SteepestPatchShown " +
                         "(connected, >= rating + DisplayHysteresis, a different rounded %, a higher " +
                         "band than the shown band), and a warning on NEW runs with " +
                         "no Ski Area Welcome Sign in range; paths skiRunGrade/skiRunPitch/" +
                         "skiRunPitchApprox/skiRunLength/skiRunDrop/skiRunSteepest/skiRunNoGate; " +
                         "UITooltip after AreaToolTooltipSystem" +
                         ", the one-time run fee (new runs only, SkiRunFees.Fee by plan area, red when the city " +
                         "cannot pay) on path skiRunFee");
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !m_Armed)
                    return;

                // Cheapest gates first: a settings bool and a reference compare.
                var settings = Mod.Settings;
                if (settings != null && !settings.ShowRunGradeWhileDrawing)
                    return;
                if (m_ToolSystem.activeTool != m_AreaTool)          // ToolSystem.cs:84-99
                    return;
                // E1 (ch2 §6.4): the fee row shows for a NEW run even when the rating failed (too
                // large, no terrain); an edited run never shows a fee (no re-charge on a reshape).
                bool rated = m_Feedback.TryGetReadout(out SkiRunDrawFeedbackSystem.Readout r) && r.Valid;
                bool fee = m_Feedback.TryGetFeeInputs(out int kind, out float areaM2, out bool isNew) && isNew;
                if (!rated && !fee)
                    return;

                // 1-5. The rating lines (band, pitch, length, drop, steepest patch). Defensive: an
                // out-of-contract band shows none of them rather than garbage (the fee still shows).
                int band = (int)r.Band;
                if (rated && band >= 0 && band < BandText.Length)
                {
                    // 1. Band. Assign the struct only on change (object.Equals boxes, see header).
                    if (band != m_ShownBand)
                    {
                        m_Grade.value = BandText[band];
                        m_ShownBand = band;
                    }
                    m_Grade.color = r.Band == SlopeBand.Flat ? TooltipColor.Warning : TooltipColor.Info;
                    AddMouseTooltip(m_Grade);

                    // 2. Hardest unavoidable pitch (the crux of the easiest line, spec §9.3), percent —
                    //    or, when the Mask is not connected (RatingGrade = P90 fallback), the same
                    //    number under a Warning label that does not claim "unavoidable" (spec §9.6
                    //    item 2 as amended in the R2 fixer round).
                    if (math.isfinite(r.RatingGrade))
                    {
                        IntTooltip pitch = r.Connected ? m_Pitch : m_PitchApprox;
                        pitch.value = (int)math.round(100f * r.RatingGrade);
                        AddMouseTooltip(pitch);
                    }

                    // 3. Run length (R3, spec §5.1), horizontal metres, between the pitch and the drop.
                    //    Shown only when positive (a cleared result reads 0; Valid already excludes it).
                    if (math.isfinite(r.Length) && r.Length > 0f)
                    {
                        m_Length.value = (int)math.round(r.Length);
                        AddMouseTooltip(m_Length);
                    }

                    // 4. Vertical drop, metres.
                    if (math.isfinite(r.Drop))
                    {
                        m_Drop.value = (int)math.round(r.Drop);
                        AddMouseTooltip(m_Drop);
                    }

                    // 5. Steepest patch (avoidable) — spec §9.6 item 4 as amended in the R2 fixer
                    //    round: connected, a real margin above the crux, a different rounded
                    //    percentage, a higher band than the shown one (header; SlopeBatch sweeps it).
                    if (SkiSlopeClassifier.SteepestPatchShown(r.RatingGrade, r.SteepestGrade, r.Band, r.Connected))
                    {
                        m_Steepest.value = (int)math.round(100f * r.SteepestGrade);
                        AddMouseTooltip(m_Steepest);
                    }
                }

                // 7. Run fee (one-time), E1 (D21): SkiRunFees.Fee — the function the bind charges — of
                //    the kind and the plan area; Error colour when the city has less money (the vanilla
                //    rule, TempCostTooltipSystem.cs:79-82). value / color assignments allocate nothing.
                if (fee)
                {
                    int value = SkiRunFees.Fee(kind, areaM2);
                    if (value >= 0)
                    {
                        m_Fee.value = value;
                        m_Fee.color = m_CitySystem.moneyAmount < value ? TooltipColor.Error : TooltipColor.Info;
                        AddMouseTooltip(m_Fee);
                    }
                }

                // 6. New run with no gate in tether range: it will not bind until one is placed.
                //    An edited run is bound or not already; the warning is for the draw decision.
                //    E1 fixer (e1-spec §8.9 F4): shown with the fee row too, rated or not — both
                //    failure paths of the feedback recompute set GateInRange (UpdateGateInRange), and
                //    TryGetReadout always fills r, so a too-large or no-terrain new run that will not
                //    bind no longer promises a charge without saying why nothing happens.
                if ((rated || fee) && r.IsNewRun && !r.GateInRange)
                    AddMouseTooltip(m_NoGate);

                FramesShown++;
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiRunTooltipSystem)} EXCEPTION — run-grade " +
                                  $"tooltip disabled until the next game load:\n{e}");
                }
            }
        }
    }
}
