// The mouse tooltip shown while the vanilla area tool draws or edits one of our ski runs. Up to seven lines, on screen in the
// order 1-5, 7, 6:
//   1. band name ("Blue square", ...)  StringTooltip, Info (Warning for Flat); the rated band
//      (SkiRunDrawFeedbackSystem.TryGetDifficulty).
//   2. "Hardest unavoidable pitch: 32 %"  IntTooltip, unit "percentage": RatingGrade, the crux of the easiest line. With a broken
//      Mask (Readout.Connected false, P90 fallback): "Typical steep pitch (run outline broken, rating approximate): 32 %",
//      Warning color, its own path.
//   3. "Length: 864 m"  IntTooltip, unit "length": Readout.Length, horizontal meters.
//   4. "Vertical drop: 140 m"  IntTooltip, unit "length" (meters in; the UI converts).
//   4b. "Avg 18 ° (32 %) · steepest 31 ° (60 %)"  StringTooltip, path skiRunSlope: the live grade, so the player sees why the run
//      will rate as it does before placing it. LocalizedString.Id(SLOPE_AVG_MAX) with four LocalizedNumber<int> args (units
//      "angle" / "percentage"), rebuilt only when a rounded number changes; numbers from SkiRunRater.SlopeReadout. A suffix names
//      the deciding factor (SkiRunRater.NoteLine; keys SLOPE_AVG_MAX_NARROW / _WIDE / _SHARE / _GENTLE). Rendering of a
//      LocalizedNumber arg with these units is unverified in game (vanilla passes units this way, ModManager.cs:341,
//      GraphicsSettings.cs:654).
//   5. "Steepest patch (avoidable): 45 %"  IntTooltip: only when SkiSlopeClassifier.SteepestPatchShown (Connected, a real margin
//      above the crux, a different rounded percentage, a higher band than shown); explains red cells on a run rated easier. Never
//      shown in polyline mode.
//   6. "No Ski Area Welcome Sign within 5 km"  StringTooltip, Warning, new runs only.
//   7. Run fee (one-time)  IntTooltip, money unit, Money icon; new runs only; SkiRunFees.Fee(kind, plan area), the function the
//      bind charges; Error color when the city has less money (TempCostTooltipSystem.cs:79-82). Label key
//      SkiResort.Tooltip.RUN_FEE, path "skiRunFee", inputs SkiRunDrawFeedbackSystem.TryGetFeeInputs. Shown even when the rating
//      failed. The vanilla cost line does not show for our areas (Temp areas carry no m_Cost, and AreaToolTooltipSystem shows
//      nothing for a plain Surface), so there is no double price. Like the whole feedback system the row follows the "Show run
//      grade while drawing" setting.
//
// How a mod tooltip works (engine citations from the 1.6.0f1 decompile, unchanged in 1.6.2f1): TooltipSystemBase
// (TooltipSystemBase.cs:9) caches TooltipUISystem in OnCreate (:14-18); AddMouseTooltip appends to mouseGroup.children and drops
// a duplicate non-empty path with a LogError (:32-42), so our paths are mod-unique (skiRunGrade, skiRunPitch, skiRunPitchApprox,
// skiRunLength, skiRunDrop, skiRunSlope, skiRunSteepest, skiRunNoGate, skiRunFee; at most one of the two pitch lines per frame).
// TooltipUISystem clears mouseGroup.children and then runs the whole UITooltip phase, only while the "tooltip.groups" binding is
// active, the mouse is not over UI and tooltips are not hidden (TooltipUISystem.cs:48-55): this OnUpdate re-adds its lines every
// frame they should show and needs no over-UI check. The phase runs from UIUpdate (UIUpdateSystem at SystemOrder.cs:67); that it
// updates while paused is likely, not field-tested.
//
// Order: UpdateAfter<SkiRunTooltipSystem, AreaToolTooltipSystem>(UITooltip) in Mod.cs; a plain UpdateAt sorts by add index
// (UpdateSystem.cs:39-47) and would put our lines after InputHintsTooltipSystem (SystemOrder.cs:933). AreaToolTooltipSystem
// (SystemOrder.cs:916) shows nothing for a plain Surface (AreaToolTooltipSystem.cs:101, 176-183), so our lines follow the
// (normally absent) cost line and precede notifications and input hints.
//
// Widgets are built once in OnCreate as fields (vanilla: TempCostTooltipSystem.cs:44-56, AreaToolTooltipSystem.cs:78-95) and
// never subclassed: Widget.propertiesTypeName is GetType().FullName (Widget.cs:74) and the JS renderer knows only the
// Game.UI.Tooltip.* names; IntTooltip renders because NumberTooltip<T> overrides it (NumberTooltip.cs:80). TooltipColor has only
// Info/Success/Warning/Error (TooltipColor.cs:3-9), so bands cannot be colored blue/black: the band name carries it. IntTooltip
// has a color too (NumberTooltip.cs:6, LabelIconTooltip.cs:7, IconTooltip.cs:31), which the approximate-pitch line uses. A label
// is a LocalizedString compared with object.Equals (LabelIconTooltip.cs:21, boxing), so each label lives on its own widget, set
// once in OnCreate. Locale keys come from the root LocaleEN.cs; LocalizedString.Id(key) is a plain struct ctor (:39-42), a bare
// string converts implicitly to a key, not raw text (:124-127), and Id(key, args...) must not run per frame (it builds a
// Dictionary through LINQ, :44-48).
//
// Zero allocation per frame: StringTooltip.value / LabelIconTooltip.label compare with object.Equals on the LocalizedString
// struct (StringTooltip.cs:18), which boxes both sides, so the band value is assigned only when the band changes.
// NumberTooltip<int>.value compares through IEquatable<int> (NumberTooltip.cs:24) and IconTooltip.color is an enum compare
// (IconTooltip.cs:39), so those are assigned every frame; their setters flag a change only when the value differs. Not ours to
// avoid: the base AddMouseTooltip runs children.Any(lambda) (TooltipSystemBase.cs:34), the cost every vanilla tooltip system pays.
//
// Data: from SkiRunDrawFeedbackSystem.TryGetReadout, computed in the previous frame's Rendering phase (MainLoop runs UIUpdate
// before Rendering, SystemOrder.cs:67-68): a one-frame lag by design. The gate is not the tool's prefab (in Modify the area tool
// edits any Surface regardless of the selected prefab, AreaToolSystem.cs:320-322, and the Temp carries the original run's prefab,
// GenerateAreasSystem.cs:172-175); the feedback system filters on that.
//
// Scope: Game mode only. House contract: "created" line, ARMED line per load, m_Armed/m_Disabled/m_ErrorLogged reset in
// OnGameLoadingComplete, one-shot "!!!" catch that silences the tooltip until the next game load.

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
        // Locale keys (root LocaleEN.cs). Band keys are indexed by (int)SlopeBand.
        public const string KeyBandPrefix = "SkiResort.SlopeBand";
        public const string KeyCrux = "SkiResort.Tooltip.CRUX_PITCH";
        public const string KeyApproxPitch = "SkiResort.Tooltip.APPROX_PITCH"; // !Connected (P90 fallback)
        public const string KeyDrop = "SkiResort.Tooltip.VERTICAL_DROP";
        public const string KeyLength = "SkiResort.Tooltip.RUN_LENGTH";
        public const string KeySteepest = "SkiResort.Tooltip.STEEPEST_PATCH";
        public const string KeySlope = "SkiResort.Tooltip.SLOPE_AVG_MAX"; // "Avg {AD} ({AP}) · steepest {SD} ({SP})"
        public const string KeySlopeNarrow = "SkiResort.Tooltip.SLOPE_AVG_MAX_NARROW"; // + "· narrow {W} at the crux"
        public const string KeySlopeWide = "SkiResort.Tooltip.SLOPE_AVG_MAX_WIDE"; // + "· wide {W} at the crux"
        public const string KeySlopeShare = "SkiResort.Tooltip.SLOPE_AVG_MAX_SHARE"; // + "· only {SS} of the run is steeper than {ST}"
        public const string KeySlopeGentle = "SkiResort.Tooltip.SLOPE_AVG_MAX_GENTLE"; // + "· gentle run, short steep pitch"
        public const string KeyNoGate ="SkiResort.Tooltip.NO_GATE_IN_RANGE";
        public const string KeyCatSteep = "SkiResort.Tooltip.CAT_TRACK_TOO_STEEP"; // a cat track rated blue or steeper
        public const string KeyFee = "SkiResort.Tooltip.RUN_FEE"; // "Run fee (one-time)"

        /// <summary>Built once; index = (int)SlopeBand (Flat 0 .. DoubleBlack 4).</summary>
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
        private CitySystem m_CitySystem; // moneyAmount for the fee row's color (CitySystem.cs:31)

        private StringTooltip m_Grade;
        private IntTooltip m_Pitch; // crux (RatingGrade), path kept as "skiRunPitch"
        private IntTooltip m_PitchApprox; // P90 fallback when the Mask is not connected (Warning)
        private IntTooltip m_Length; // horizontal run length (m)
        private IntTooltip m_Drop;
        private StringTooltip m_Slope; // "Avg 18 ° (32 %) · steepest 31 ° (60 %)", rebuilt only when a rounded number changes
        private int m_SlopeAd = -1, m_SlopeAp = -1, m_SlopeSd = -1, m_SlopeSp = -1;
        private int m_SlopeKind = -1, m_SlopeW = -1, m_SlopeSs = -1, m_SlopeSt = -1; // suffix kind (SkiRunRater.SlopeNoteLine) and its numbers
        private IntTooltip m_Steepest; // steepest avoidable patch (line 5)
        private IntTooltip m_Fee; // one-time run fee (new runs only), money unit
        private StringTooltip m_NoGate;
        private StringTooltip m_CatSteep; // Error line "too steep for a cat track" (SkiRunRatingRules.CatTrackUnsuitable)
        private int m_ShownBand = -1; // band currently written into m_Grade.value

        // Per-load state, reset at OnGameLoadingComplete.
        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;

        /// <summary>Frames on which the lines were added since the last load (dev-tab aid; plain field).</summary>
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
                unit = "percentage",
                color = TooltipColor.Warning,
            };
            m_Length = new IntTooltip
            {
                path = "skiRunLength",
                label = LocalizedString.Id(KeyLength),
                unit = "length",
            };
            m_Drop = new IntTooltip
            {
                path = "skiRunDrop",
                label = LocalizedString.Id(KeyDrop),
                unit = "length", // meters in; the unit is JS-side, so IntTooltip + "length" is unverified in game (cf. WaterSourceTooltipSystem.cs:51)
            };
            m_Slope = new StringTooltip
            {
                path = "skiRunSlope",
                color = TooltipColor.Info,
            };
            m_Steepest = new IntTooltip
            {
                path = "skiRunSteepest",
                label = LocalizedString.Id(KeySteepest),
                unit = "percentage",
            };
            m_NoGate = new StringTooltip
            {
                path = "skiRunNoGate",
                value = LocalizedString.Id(KeyNoGate),
                color = TooltipColor.Warning,
            };
            m_CatSteep = new StringTooltip
            {
                path = "skiRunCatSteep",
                value = LocalizedString.Id(KeyCatSteep),
                color = TooltipColor.Error,
            };
            // The vanilla cost row's widget shape (TempCostTooltipSystem.cs:44-56) with our label.
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
                         "vertical drop (m), the average / steepest slope line (T35 / T44: degrees and percent, path skiRunSlope), steepest " +
                         "avoidable patch (%) only when SkiSlopeClassifier.SteepestPatchShown " +
                         "(connected, >= rating + DisplayHysteresis, a different rounded %, a higher " +
                         "band than the shown band), and a warning on NEW runs with " +
                         "no Ski Area Welcome Sign in range; paths skiRunGrade/skiRunPitch/" +
                         "skiRunPitchApprox/skiRunLength/skiRunDrop/skiRunSlope/skiRunSteepest/skiRunNoGate (+ T54 skiRunCatSteep: " +
                         "an Error line on a cat track rated blue or steeper); " +
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

                var settings = Mod.Settings;
                if (settings != null && !settings.ShowRunGradeWhileDrawing)
                    return;
                if (m_ToolSystem.activeTool != m_AreaTool) // ToolSystem.cs:84-99
                    return;
                // The fee row shows for a new run even when the rating failed (too large, no terrain); an edited run never shows a fee (no
                // re-charge on a reshape).
                bool rated = m_Feedback.TryGetReadout(out SkiRunDrawFeedbackSystem.Readout r) && r.Valid;
                bool fee = m_Feedback.TryGetFeeInputs(out int kind, out float areaM2, out bool isNew) && isNew;
                if (!rated && !fee)
                    return;

                // Lines 1-5, the rating lines. Defensive: an out-of-contract band shows none of them rather than garbage (the fee still
                // shows). Line 1 takes the rated band from the classifier result; the readout's own Band is the crux band and stays the input
                // of line 5.
                bool haveDiff = m_Feedback.TryGetDifficulty(out SkiRunDrawFeedbackSystem.Difficulty dif);
                int band = haveDiff ? (int)dif.Band : (int)r.Band;
                if (rated && band >= 0 && band < BandText.Length)
                {
                    // Band. Assign the struct only on change (object.Equals boxes, see header).
                    if (band != m_ShownBand)
                    {
                        m_Grade.value = BandText[band];
                        m_ShownBand = band;
                    }
                    m_Grade.color = band == (int)SlopeBand.Flat ? TooltipColor.Warning : TooltipColor.Info;
                    AddMouseTooltip(m_Grade);

                    // A cat track rated blue or steeper (25 % or more at the crux, the shown band) is "Unsuitable" at bind; say so before the
                    // click. The click is not refused.
                    if (r.Kind == (int)RunKind.CatTrack && SkiRunRatingRules.CatTrackUnsuitable((sbyte)band))
                        AddMouseTooltip(m_CatSteep);

                    // Hardest unavoidable pitch, percent; when the Mask is not connected (RatingGrade = P90 fallback) the same number under a
                    // Warning label that does not claim "unavoidable".
                    if (math.isfinite(r.RatingGrade))
                    {
                        IntTooltip pitch = r.Connected ? m_Pitch : m_PitchApprox;
                        pitch.value = (int)math.round(100f * r.RatingGrade);
                        AddMouseTooltip(pitch);
                    }

                    // Run length, horizontal meters. Shown only when positive (a cleared result reads 0; Valid already excludes it).
                    if (math.isfinite(r.Length) && r.Length > 0f)
                    {
                        m_Length.value = (int)math.round(r.Length);
                        AddMouseTooltip(m_Length);
                    }

                    if (math.isfinite(r.Drop))
                    {
                        m_Drop.value = (int)math.round(r.Drop);
                        AddMouseTooltip(m_Drop);
                    }

                    // The live grade. The LocalizedString (a Dictionary of LocalizedNumber args, the vanilla pattern) is rebuilt only when one of
                    // the four rounded numbers changes, never per frame (header: zero allocation).
                    if (SkiRunRater.SlopeReadout(r.MeanGrade, r.RatingGrade, r.SteepestGrade,
                            out int avgDeg, out int avgPct, out int maxDeg, out int maxPct))
                    {
                        // The suffix naming the deciding factor (width / steep share / gentle run), if any.
                        SkiRunRater.SlopeNoteLine noteKind = SkiRunRater.SlopeNoteLine.None;
                        int noteW = 0, noteSs = 0, noteSt = 0;
                        if (haveDiff)
                            noteKind = SkiRunRater.NoteLine(dif.Note, dif.WidthM, dif.SteepShare, dif.CruxBand, out noteW, out noteSs, out noteSt);
                        if (avgDeg != m_SlopeAd || avgPct != m_SlopeAp || maxDeg != m_SlopeSd || maxPct != m_SlopeSp ||
                            (int)noteKind != m_SlopeKind || noteW != m_SlopeW || noteSs != m_SlopeSs || noteSt != m_SlopeSt)
                        {
                            var ad = new LocalizedNumber<int>(avgDeg, "angle");
                            var ap = new LocalizedNumber<int>(avgPct, "percentage");
                            var sd = new LocalizedNumber<int>(maxDeg, "angle");
                            var sp = new LocalizedNumber<int>(maxPct, "percentage");
                            switch (noteKind)
                            {
                                case SkiRunRater.SlopeNoteLine.Narrow:
                                case SkiRunRater.SlopeNoteLine.Wide:
                                    m_Slope.value = LocalizedString.Id(noteKind == SkiRunRater.SlopeNoteLine.Narrow ? KeySlopeNarrow : KeySlopeWide,
                                        ("AD", ad), ("AP", ap), ("SD", sd), ("SP", sp),
                                        ("W", new LocalizedNumber<int>(noteW, "length"))); // unit "length" in a LocalizedNumber: unverified in game
                                    break;
                                case SkiRunRater.SlopeNoteLine.Share:
                                    m_Slope.value = LocalizedString.Id(KeySlopeShare,
                                        ("AD", ad), ("AP", ap), ("SD", sd), ("SP", sp),
                                        ("SS", new LocalizedNumber<int>(noteSs, "percentage")),
                                        ("ST", new LocalizedNumber<int>(noteSt, "percentage")));
                                    break;
                                case SkiRunRater.SlopeNoteLine.Gentle:
                                    m_Slope.value = LocalizedString.Id(KeySlopeGentle, ("AD", ad), ("AP", ap), ("SD", sd), ("SP", sp));
                                    break;
                                default:
                                    m_Slope.value = LocalizedString.Id(KeySlope, ("AD", ad), ("AP", ap), ("SD", sd), ("SP", sp));
                                    break;
                            }
                            m_SlopeAd = avgDeg;
                            m_SlopeAp = avgPct;
                            m_SlopeSd = maxDeg;
                            m_SlopeSp = maxPct;
                            m_SlopeKind = (int)noteKind;
                            m_SlopeW = noteW;
                            m_SlopeSs = noteSs;
                            m_SlopeSt = noteSt;
                        }
                        AddMouseTooltip(m_Slope);
                    }

                    // Steepest patch (avoidable): connected, a real margin above the crux, a different rounded percentage, a higher band than the
                    // shown one (SlopeBatch sweeps it).
                    if (SkiSlopeClassifier.SteepestPatchShown(r.RatingGrade, r.SteepestGrade, r.Band, r.Connected))
                    {
                        m_Steepest.value = (int)math.round(100f * r.SteepestGrade);
                        AddMouseTooltip(m_Steepest);
                    }
                }

                // Run fee: SkiRunFees.Fee, the function the bind charges, of the kind and the plan area; Error color when the city has less
                // money (TempCostTooltipSystem.cs:79-82). value / color assignments allocate nothing.
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

                // New run with no gate in tether range: it will not bind until one is placed. An edited run is bound or not already; the
                // warning is for the draw decision. Shown with the fee row too, rated or not: both failure paths of the feedback recompute set
                // GateInRange (UpdateGateInRange), and TryGetReadout always fills r.
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
