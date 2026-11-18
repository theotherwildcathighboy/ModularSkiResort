// The mouse tooltip shown while the vanilla area tool draws or edits one of our ski runs. It holds: band name, hardest unavoidable pitch,
// length, vertical drop, live slope, avoidable steepest patch, "no Welcome Sign" warning and the one-time run fee.
// Order: UpdateAfter<SkiRunTooltipSystem, AreaToolTooltipSystem>(UITooltip) in Mod.cs. A plain UpdateAt would sort after the input hints.
// Widgets are built once in OnCreate and never subclassed (the JS renderer knows only the Game.UI.Tooltip.* type names).
// Zero allocation per frame: LocalizedString values compare by boxing, so the band value is assigned only when the band changes.
// Data comes from SkiRunDrawFeedbackSystem.TryGetReadout, one frame behind by design. That system filters runs (not the tool's prefab).
// Game mode only. A one-shot "!!!" catch silences the tooltip until the next game load.
// Engine notes: EngineNotes/SkiRunTooltipSystem.md

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
        public const string KeySlope = "SkiResort.Tooltip.SLOPE_AVG_MAX"; // "Avg {AD} · steepest {SD}" (degrees since T160)
        public const string KeySlopeNarrow = "SkiResort.Tooltip.SLOPE_AVG_MAX_NARROW"; // + "· narrow {W} at the crux"
        public const string KeySlopeWide = "SkiResort.Tooltip.SLOPE_AVG_MAX_WIDE"; // + "· wide {W} at the crux"
        public const string KeySlopeShare = "SkiResort.Tooltip.SLOPE_AVG_MAX_SHARE"; // + "· only {SS} of the run is steeper than {ST}"
        public const string KeySlopeGentle = "SkiResort.Tooltip.SLOPE_AVG_MAX_GENTLE"; // + "· gentle run, short steep pitch"
        public const string KeyNoGate ="SkiResort.Tooltip.NO_GATE_IN_RANGE";
        public const string KeyFee = "SkiResort.Tooltip.RUN_FEE"; // "Run fee (one-time)"
        public const string KeySoFar = "SkiResort.Tooltip.RATING_SO_FAR"; // "{BAND} (so far)": a new outline still open (T252)

        /// <summary>Built once, North American by (int)SlopeBand (Flat 0 .. DoubleBlack 4).</summary>
        private static readonly LocalizedString[] BandText =
        {
            LocalizedString.Id(KeyBandPrefix + "[Flat]"),
            LocalizedString.Id(KeyBandPrefix + "[Green]"),
            LocalizedString.Id(KeyBandPrefix + "[Blue]"),
            LocalizedString.Id(KeyBandPrefix + "[Black]"),
            LocalizedString.Id(KeyBandPrefix + "[DoubleBlack]"),
        };

        /// <summary>European grading (T162 rev. 2), by (int)EuClass: Flat keeps the band's name, the rest the class names (EuClassKey).</summary>
        private static readonly LocalizedString[] EuClassText =
        {
            LocalizedString.Id(KeyBandPrefix + "[Flat]"),
            LocalizedString.Id(DisplayUnits.EuClassKey(EuClass.Green)),
            LocalizedString.Id(DisplayUnits.EuClassKey(EuClass.Blue)),
            LocalizedString.Id(DisplayUnits.EuClassKey(EuClass.Red)),
            LocalizedString.Id(DisplayUnits.EuClassKey(EuClass.Black)),
            LocalizedString.Id(DisplayUnits.EuClassKey(EuClass.Route)),
        };

        private EuClass m_ShownEu = EuClass.Flat;   // the drawing readout's European class, kept within the hysteresis

        private ToolSystem m_ToolSystem;
        private AreaToolSystem m_AreaTool;
        private SkiRunDrawFeedbackSystem m_Feedback;
        private CitySystem m_CitySystem; // moneyAmount for the fee row's color (CitySystem.cs:31)
        private SkiResortHubUISystem m_Hub; // the Run tile's draw row: the sliders a new run starts with (TryGetDrawSeed)

        private StringTooltip m_Grade;
        private FloatTooltip m_Pitch; // crux (RatingGrade) in degrees (T160), path kept as "skiRunPitch"
        private FloatTooltip m_PitchApprox; // P90 fallback when the Mask is not connected (Warning)
        private IntTooltip m_Length; // horizontal run length (m)
        private IntTooltip m_Drop;
        private UnitTooltipLine m_LengthUnits, m_DropUnits;   // the two as text while Options > Lengths differs from the game's setting
        private StringTooltip m_Slope; // "Avg 18 ° (32 %) · steepest 31 ° (60 %)", rebuilt only when a rounded number changes
        private float m_SlopeAd = -1f, m_SlopeSd = -1f, m_SlopeSt = -1f;   // degrees, one decimal
        private int m_SlopeKind = -1, m_SlopeW = -1, m_SlopeSs = -1; // suffix kind (SkiRunRater.SlopeNoteLine) and its numbers
        private int m_SlopeUnits = -1;   // DisplayState.Packed the line was built in (slope format, lengths)
        private FloatTooltip m_Steepest; // steepest avoidable patch (line 5), degrees
        private IntTooltip m_Fee; // one-time run fee (new runs only), money unit
        private StringTooltip m_NoGate;
        private float m_NoGateKm = float.NaN;
        private int m_ShownBand = -1; // band currently written into m_Grade.value

        // Per-load state, reset at OnGameLoadingComplete.
        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;


        protected override void OnCreate()
        {
            base.OnCreate(); // caches TooltipUISystem (TooltipSystemBase.cs:14-18)
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_AreaTool = World.GetOrCreateSystemManaged<AreaToolSystem>();
            m_Feedback = World.GetOrCreateSystemManaged<SkiRunDrawFeedbackSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_Hub = World.GetOrCreateSystemManaged<SkiResortHubUISystem>();

            m_Grade = new StringTooltip
            {
                path = "skiRunGrade",
                color = TooltipColor.Info,
            };
            m_Pitch = new FloatTooltip
            {
                path = "skiRunPitch",
                label = LocalizedString.Id(KeyCrux),
                unit = "angle", // degrees, one decimal (DisplayUnits.Deg1, T160)
            };
            m_PitchApprox = new FloatTooltip
            {
                path = "skiRunPitchApprox",
                label = LocalizedString.Id(KeyApproxPitch),
                unit = "angle",
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
                unit = "height", // meters in, m or ft by the game's unit setting (vertical, T161)
            };
            m_LengthUnits = new UnitTooltipLine(m_Length.path, UnitTooltipLine.Kind.Length);
            m_DropUnits = new UnitTooltipLine(m_Drop.path, UnitTooltipLine.Kind.Height);
            m_Slope = new StringTooltip
            {
                path = "skiRunSlope",
                color = TooltipColor.Info,
            };
            m_Steepest = new FloatTooltip
            {
                path = "skiRunSteepest",
                label = LocalizedString.Id(KeySteepest),
                unit = "angle",
            };
            m_NoGate = new StringTooltip
            {
                path = "skiRunNoGate",
                color = TooltipColor.Warning,
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
                         "skiRunPitchApprox/skiRunLength/skiRunDrop/skiRunSlope/skiRunSteepest/skiRunNoGate; " +
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
                // The fee row shows for a new run even when the rating failed (too large, no terrain). An edited run never shows a fee (no
                // re-charge on a reshape).
                bool rated = m_Feedback.TryGetReadout(out SkiRunDrawFeedbackSystem.Readout r) && r.Valid;
                bool fee = m_Feedback.TryGetFeeInputs(out int kind, out float areaM2, out bool isNew) && isNew;
                if (!rated && !fee)
                    return;

                // Lines 1-5, the rating lines. Defensive: an out-of-contract band shows none of them rather than garbage (the fee still
                // shows). Line 1 takes the rated band from the classifier result. The readout's own Band is the crux band and stays the input
                // of line 5.
                bool haveDiff = m_Feedback.TryGetDifficulty(out SkiRunDrawFeedbackSystem.Difficulty dif);
                int band = haveDiff ? (int)dif.Band : (int)r.Band;
                if (rated && band >= 0 && band < BandText.Length)
                {
                    // Band. Assign the struct only on change (object.Equals boxes, see engine notes). European grading names the class of the
                    // band and the crux (DisplayUnits.EuClassShown, the readout's DisplayHysteresis at the 15 % boundary).
                    bool eu = DisplayState.Scheme == GradeScheme.European;
                    if (eu)
                        m_ShownEu = DisplayUnits.EuClassShown(band, r.RatingGrade * 100f, m_ShownEu, 100f * SkiSlopeClassifier.DisplayHysteresis);
                    // T252: while a new outline is still open, the next click changes the shape, so the band reads "so far". At the closing click
                    // (and for an edited run) the readout is the rating the built run gets, so the plain band shows.
                    int shown = (eu ? 8 + (int)m_ShownEu : band) + (r.Open ? 100 : 0);   // a scheme change re-sets the text too
                    if (shown != m_ShownBand)
                    {
                        LocalizedString text = eu ? EuClassText[(int)m_ShownEu] : BandText[band];
                        m_Grade.value = r.Open ? LocalizedString.Id(KeySoFar, ("BAND", text)) : text;
                        m_ShownBand = shown;
                    }
                    m_Grade.color = band == (int)SlopeBand.Flat ? TooltipColor.Warning : TooltipColor.Info;
                    AddMouseTooltip(m_Grade);

                    // Hardest unavoidable pitch, degrees. When the Mask is not connected (RatingGrade = P90 fallback) the same number under a
                    // Warning label that does not claim "unavoidable".
                    if (math.isfinite(r.RatingGrade))
                    {
                        FloatTooltip pitch = r.Connected ? m_Pitch : m_PitchApprox;
                        DisplayText.SetSlope(pitch, r.RatingGrade);   // degrees or percent (Options > Slope)
                        AddMouseTooltip(pitch);
                    }

                    // Run length, horizontal meters. Shown only when positive (a cleared result reads 0, Valid already excludes it).
                    if (math.isfinite(r.Length) && r.Length > 0f)
                    {
                        m_Length.value = (int)math.round(r.Length);
                        AddMouseTooltip(m_LengthUnits.Pick(m_Length, r.Length));
                    }

                    if (math.isfinite(r.Drop))
                    {
                        m_Drop.value = (int)math.round(r.Drop);
                        AddMouseTooltip(m_DropUnits.Pick(m_Drop, r.Drop));
                    }

                    // The live grade. The LocalizedString (a Dictionary of LocalizedNumber args, the vanilla pattern) is rebuilt only when one of
                    // the four rounded numbers changes. It is never rebuilt per frame (engine notes: zero allocation).
                    if (SkiRunRater.SlopeReadout(r.MeanGrade, r.RatingGrade, r.SteepestGrade,
                            out float avgDeg, out float maxDeg))
                    {
                        // The suffix naming the deciding factor (width / steep share / gentle run), if any.
                        SkiRunRater.SlopeNoteLine noteKind = SkiRunRater.SlopeNoteLine.None;
                        int noteW = 0, noteSs = 0;
                        float noteSt = 0f;
                        if (haveDiff)
                            noteKind = SkiRunRater.NoteLine(dif.Note, dif.WidthM, dif.SteepShare, dif.CruxBand, out noteW, out noteSs, out noteSt);
                        int units = DisplayState.Packed;
                        if (avgDeg != m_SlopeAd || maxDeg != m_SlopeSd || units != m_SlopeUnits ||
                            (int)noteKind != m_SlopeKind || noteW != m_SlopeW || noteSs != m_SlopeSs || noteSt != m_SlopeSt)
                        {
                            ILocElement ad = DisplayText.SlopeDeg(avgDeg), sd = DisplayText.SlopeDeg(maxDeg);   // degrees or percent
                            switch (noteKind)
                            {
                                case SkiRunRater.SlopeNoteLine.Narrow:
                                case SkiRunRater.SlopeNoteLine.Wide:
                                    m_Slope.value = LocalizedString.Id(noteKind == SkiRunRater.SlopeNoteLine.Narrow ? KeySlopeNarrow : KeySlopeWide,
                                        ("AD", ad), ("SD", sd),
                                        ("W", DisplayText.Length(noteW))); // m or yd (Options > Lengths and elevation)
                                    break;
                                case SkiRunRater.SlopeNoteLine.Share:
                                    m_Slope.value = LocalizedString.Id(KeySlopeShare,
                                        ("AD", ad), ("SD", sd),
                                        ("SS", new LocalizedNumber<int>(noteSs, "percentage")),
                                        ("ST", DisplayText.SlopeDeg(noteSt)));
                                    break;
                                case SkiRunRater.SlopeNoteLine.Gentle:
                                    m_Slope.value = LocalizedString.Id(KeySlopeGentle, ("AD", ad), ("SD", sd));
                                    break;
                                default:
                                    m_Slope.value = LocalizedString.Id(KeySlope, ("AD", ad), ("SD", sd));
                                    break;
                            }
                            m_SlopeAd = avgDeg;
                            m_SlopeSd = maxDeg;
                            m_SlopeKind = (int)noteKind;
                            m_SlopeW = noteW;
                            m_SlopeSs = noteSs;
                            m_SlopeSt = noteSt;
                            m_SlopeUnits = units;
                        }
                        AddMouseTooltip(m_Slope);
                    }

                    // Steepest patch (avoidable): connected, a real margin above the crux, a different rounded percentage, a higher band than the
                    // shown one (SlopeBatch sweeps it).
                    if (SkiSlopeClassifier.SteepestPatchShown(r.RatingGrade, r.SteepestGrade, r.Band, r.Connected))
                    {
                        DisplayText.SetSlope(m_Steepest, r.SteepestGrade);
                        AddMouseTooltip(m_Steepest);
                    }
                }

                // Run fee: SkiRunFees.Fee, the function the bind charges, of the kind and the plan area. On a groomed piste it uses the draw row's sliders
                // (SkiRunBindTriggerSystem.SeedDrawnRuns seeds them before the bind). Error color when the city has less money
                // (TempCostTooltipSystem.cs:79-82). value / color assignments allocate nothing.
                if (fee)
                {
                    int value = FeeOf(kind, areaM2);
                    if (value >= 0)
                    {
                        m_Fee.value = value;
                        m_Fee.color = m_CitySystem.moneyAmount < value ? TooltipColor.Error : TooltipColor.Info;
                        AddMouseTooltip(m_Fee);
                    }
                }

                // New run with no gate in tether range: it will not bind until one is placed. An edited run is bound or not already. The
                // warning is for the draw decision. Shown with the fee row too, rated or not: both failure paths of the feedback recompute set
                // GateInRange (UpdateGateInRange), and TryGetReadout always fills r.
                if ((rated || fee) && r.IsNewRun && !r.GateInRange)
                {
                    // {RADIUS} = the ski area radius option in km. The LocalizedString is rebuilt only when it changes.
                    float km = SkiRunOwnershipSystem.TetherMeters / 1000f;
                    if (km != m_NoGateKm)
                    {
                        m_NoGateKm = km;
                        m_NoGate.value = LocalizedString.Id(KeyNoGate, ("RADIUS", DisplayText.Length(km * 1000f)));   // km / mi (T161)
                    }
                    AddMouseTooltip(m_NoGate);
                }
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

        /// <summary>The one-time fee of the new run being drawn, as the fee row shows it. False when there is no live new-run readout.
        /// SkiRunToolAssistSystem reads it for the money icon (T243b).</summary>
        public bool TryGetNewRunFee(out int fee)
        {
            fee = -1;
            if (!m_Feedback.TryGetFeeInputs(out int kind, out float areaM2, out bool isNew) || !isNew)
                return false;
            fee = FeeOf(kind, areaM2);
            return fee >= 0;
        }

        private int FeeOf(int kind, float areaM2)
        {
            RunFeatures drawn = RunFeatures.FromKind(kind);
            if (drawn.IsPiste && drawn.Groomed && m_Hub.TryGetDrawSeed(out int gunM, out int lightM))
                drawn = RunFeatures.FromSpacings(kind, gunM, lightM);
            return SkiRunFees.Fee(drawn, areaM2);
        }
    }
}
