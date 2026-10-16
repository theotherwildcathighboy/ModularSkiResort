// RunSnowRules — the pure snow rules of Round E1 (docs/research/econ-round/spec/ch3-snow.md):
// skiable texel test, open state and multiplier (D13), the estimate from the day-mean temperature
// (D13 fallback), the dry-skip predicate and step scale (D30), the footprint hash. No UnityEngine,
// no Entities. IsSkiable and Mix64 are called from Burst jobs (SkiSnowInjectionSystem).
//
// Stage 0 (Round E1, S0, 2026-10-04; e1-spec I-06) laid down the shared types block verbatim (the four
// enums, RunSnow and DryPass); stream S3 (2026-10-04) added `internal static class RunSnowRules` (ch3 §2,
// verbatim) and owns the file. Linked into tests/ResortBatch (SnowRulesTests.cs, ch3 §11: 108 checks).
// Round M2 (topup-skip, 2026-10-10; docs/research/round-m2/understand/topup-skip.md §2.1 C1-C5 and its check,
// decisions.md R11): the packed skip (TopupSkip, CanSkipPacked, SkipKind, IsForcedCheck), THE pack guard (moved
// here from SkiSnowInjectionSystem.Tuning, value unchanged) and the share guard; S17-S23 in SnowRulesTests.
// T09 (2026-10-16; docs/research/field/2026-10-15/build/T09-snow-rules.md, Richard's rulings): the skiable bars (15 cm,
// 31 cm on natural black and double black), the gun margin 3.3 C, the gun and groomed targets, the ramp constants (moved
// from SkiSnowInjectionSystem.Tuning so a test can tie them to the opening claim), the warm-spell PROTECTION rules
// (budget, 10 C stop, spring halving, aspect factors, the per-run state machine) and the per-run snow-made LEDGER.
using System;

namespace SkiResortMod.Systems
{
    /// <summary>Open label with hysteresis (D13): opens at >= 60 %, closes below 40 %.</summary>
    public enum RunSnowState : byte { Unknown = 0, Closed = 1, Open = 2 }

    /// <summary>Where a run's numbers come from. GatingOff = the "Snow & season gating" setting is off.</summary>
    public enum RunSnowSource : byte { None = 0, Measured = 1, Estimated = 2, GatingOff = 3 }

    /// <summary>Display phase from the label and the multiplier (hub marks, overlay).</summary>
    public enum RunSnowPhase : byte { Closed = 0, Opening = 1, Open = 2, Closing = 3 }

    /// <summary>What the decal dress and the props' snow follow (§8).</summary>
    public enum RunDressSource : byte { None = 0, Managed = 1, NaturalMeasured = 2 }

    /// <summary>Round M2 (topup-skip, R11): which skip a pass takes. Dry = D30 (bare footprint); Packed = the pack
    /// guard holds managed melt and no texel is cold.</summary>
    public enum TopupSkip : byte { None = 0, Dry = 1, Packed = 2 }

    /// <summary>One run's snow, as published (§5). Runtime only; never serialized.</summary>
    public struct RunSnow
    {
        public RunKind Kind;
        public RunSnowSource Source;
        public RunSnowState State;        // label, hysteretic
        public float Share;               // 0..1 skiable share, exact (measured or estimated)
        public float Multiplier;          // 0..1 published with a 0.05 dead band; endpoints exact
        public float MeanDepthM;          // coverage-weighted mean depth (m); NaN when not measured
        public float FootprintTexels;     // coverage-weighted footprint (texels); 0 when not measured
        public float ExcludedShare;       // natural runs: share of the raster owned by injected runs this pass
        public uint SnapshotFrame;        // sim frame of the snapshot used; 0 when not measured
        public float BottomY, TopY;       // Geometry.m_Bounds y (lowest / highest node), for estimates
    }

    /// <summary>What the skip predicates need from a real pass (§7.2; Round M2 packed skip). Runtime only; never
    /// serialized (SkiSnowInjectionSystem implements no serialization interface; nothing else holds one).</summary>
    public struct DryPass
    {
        public bool Valid;
        public int ColdTexels;            // contained texels with T_local <= threshold
        public int Injected;              // texels raised by the merge (real passes only)
        public double RunDepthSum;        // merge's RunDepthSum (real passes only), normalized units
        public int ContainedTexels;       // texels with Scratch > 0, summed over bound injected runs
        public ulong FootprintHash;       // wrapping sum of Mix64(texel index) over the same texels
        // Round M2 (topup-skip): the packed skip's inputs, real passes only.
        public int Lowered;               // texels lowered by managed melt in this pass's merge
        public bool PackGuardNext;        // PackGuard(map mean this pass read): the pack guard the NEXT merge applies
        public float RunMeanDepthM;       // MeanRunSnowDepthM after this pass (the dress depth, m)
    }

    internal static class RunSnowRules
    {
        // D13 threshold and shares
        // T09 ruling (Richard, 2026-10-16): "6 inches / 15 cm is the min for all runs, except 12 inches / 31 cm for natural
        // expert and above runs". Was 2 cm. 31, not 30.48: the ruling's own figure.
        public const float SkiableDepthM = 0.15f;
        public const float ExpertNaturalSkiableDepthM = 0.31f;
        public const byte ExpertBandMin = 3;                               // = SkiResortScore.BandBlack (3 black, 4 double black)
        public const float SnowHeightScaleM = 8f;                          // = SnowSystem kSnowHeightScale
        public const float SkiableNorm = SkiableDepthM / SnowHeightScaleM; // 0.01875
        public const float ExpertNaturalSkiableNorm = ExpertNaturalSkiableDepthM / SnowHeightScaleM;
        public const float HalfQuantum = 0.5f / 65535f;                    // = SkiSnowInjectionSystem.HalfQuantum
        public const float OpenAtShare = 0.60f;
        public const float CloseBelowShare = 0.40f;
        public const float InitialOpenShare = 0.50f;   // first label of a load (no history): the band's midpoint
        public const float MultiplierDeadBand = 0.05f;
        public const float MinFootprintTexels = 0.5f;  // below this a run is too small to measure → estimate
        // D13 fallback
        public const int DayMeanSamples = 24;          // hourly samples, the 24 game hours up to now
        public const float GroomedMarginC = 0f;        // = the injection's groomed gate
        // D14
        // T09 ruling: "Snowmaking runs should be able to make snow in up to 38F / 3.3 C temps" (was 2). The setting is a
        // float slider since T09 (step 0.1) so the default can be 3.3; the range stays 0..6.
        public const float DefaultSnowmakingMarginC = 3.3f; // = SkiResortSettings.DefaultSnowmakingBonusCelsius (O8)
        public const float MinSnowmakingMarginC = 0f;       // = the slider's range (SkiResortSettings)
        public const float MaxSnowmakingMarginC = 6f;
        // D29 / D30
        public const int TopupIntervalFrames = 64;     // the injection's Tuning.TopupIntervalFrames points here
        public const float MaxStepScale = 4f;          // covers 8× (3.75); a step never spans more than 4 windows
        public const float MinPassIntervalS = 0.5f;    // at most two evaluated passes per wall-clock second
        public const uint ForcedRealPassFrames = 1024; // 16 windows
        public const uint MeasureIntervalFrames = 1024;
        public const uint MaxSnapshotAgeFrames = 4096; // older snapshots are stale → estimate
        public const int MeasureFailLimit = 10;        // vanilla's retry cap (TerrainSystem.cs:3595)
        // Round M2 (topup-skip, R11). The pack guard moved here from SkiSnowInjectionSystem.Tuning (value unchanged;
        // Tuning points here).
        public const float PackGuardMeanDepthM = 0.02f;
        // The dress guard. 2 x SkiRunSurfaceSystem.Tuning.FullWhiteDepthM (0.05 m, SkiRunSurfaceSystem.cs, the
        // saturate(depth / 0.05) prop snow): the dress is saturated above 0.05 m (the decal's season thresholds,
        // 0.02 / 0.005 m, sit below it too) and the extra 0.05 m covers one forced interval of melt (topup-skip
        // §4.2: 0.046 m per 1,264 frames at most in field test 10). DEPENDS ON FullWhiteDepthM — that file is not
        // linked into ResortBatch, so no test ties the two: change both together.
        public const float PackedSkipMinRunDepthM = 0.10f;
        public const float CalmOpenShare = 0.70f;           // OpenAtShare + 0.10
        public const float CalmClosedShare = 0.30f;         // CloseBelowShare − 0.10

        /// <summary>D13 multiplier, deviation E1-S2 (§5.2): 0 below 40 %, 1 at or above 60 %, linear
        /// in between. NaN → 0.</summary>
        public static float OpenMultiplier(float share)
        {
            if (!(share >= CloseBelowShare))
                return 0f;
            if (share >= OpenAtShare)
                return 1f;
            return (share - CloseBelowShare) / (OpenAtShare - CloseBelowShare);
        }

        /// <summary>D13 label with hysteresis. Unknown (first refresh of a load) uses the midpoint.
        /// NaN keeps an Open label, otherwise Closed.</summary>
        public static RunSnowState NextState(RunSnowState previous, float share)
        {
            if (float.IsNaN(share))
                return previous == RunSnowState.Open ? RunSnowState.Open : RunSnowState.Closed;
            switch (previous)
            {
                case RunSnowState.Open:
                    return share < CloseBelowShare ? RunSnowState.Closed : RunSnowState.Open;
                case RunSnowState.Closed:
                    return share >= OpenAtShare ? RunSnowState.Open : RunSnowState.Closed;
                default:
                    return share >= InitialOpenShare ? RunSnowState.Open : RunSnowState.Closed;
            }
        }

        /// <summary>Dead band for the published multiplier: republish on a move of at least 0.05,
        /// and always when the new value is an endpoint (0 or 1) or nothing was published yet.</summary>
        public static bool Republish(float published, float current)
        {
            if (float.IsNaN(published))
                return true;
            if (current == published)
                return false;
            if (current <= 0f || current >= 1f)
                return true;
            return Math.Abs(current - published) >= MultiplierDeadBand;
        }

        public static RunSnowPhase Phase(RunSnowState state, float multiplier)
        {
            if (state == RunSnowState.Open)
                return multiplier >= 1f ? RunSnowPhase.Open : RunSnowPhase.Closing;
            return multiplier > 0f ? RunSnowPhase.Opening : RunSnowPhase.Closed;
        }

        /// <summary>The temperature at or below which a run's texels hold snow, per kind: the
        /// injection's own gates (snowmaking and night: freezing + margin; groomed: freezing + 0), and
        /// freezing for natural runs (the snow shader's own threshold, presumably 0 °C — UNVERIFIED).</summary>
        public static float ThresholdC(RunKind kind, float freezingC, float snowmakingMarginC)
        {
            switch (kind)
            {
                case RunKind.Snowmaking:
                case RunKind.Night:
                case RunKind.CatTrack:   // T54 probe 1 (cat-track-run.md §2): no injection, opens by the day-mean estimate with the gun margin
                    return freezingC + snowmakingMarginC;
                case RunKind.Groomed:
                    return freezingC + GroomedMarginC;
                default:
                    return freezingC;
            }
        }

        /// <summary>The slider value clamped to 0..6 °C. NaN (a hand-edited options file) → the default.</summary>
        public static float ClampMargin(float settingValue)
        {
            if (float.IsNaN(settingValue))
                return DefaultSnowmakingMarginC;
            return settingValue < MinSnowmakingMarginC ? MinSnowmakingMarginC
                 : settingValue > MaxSnowmakingMarginC ? MaxSnowmakingMarginC : settingValue;
        }

        /// <summary>D13 fallback: the share of the run's vertical that is cold enough, with the
        /// validated lapse T_local(h) = T − (h − base) × lapse. Same shape as today's RunCoverNow, with
        /// the day-mean temperature and the kind's threshold. Degenerate lapse: all or nothing.</summary>
        public static float EstimatedShare(float bottomY, float topY, float temperatureC,
            float thresholdC, float baseHeight, float lapsePerM)
        {
            if (float.IsNaN(bottomY) || float.IsNaN(topY) || float.IsNaN(temperatureC))
                return 0f;
            if (!(lapsePerM > 0f))
                return temperatureC <= thresholdC ? 1f : 0f;
            if (topY < bottomY)
            {
                float swap = topY;
                topY = bottomY;
                bottomY = swap;
            }
            float lineY = baseHeight + (temperatureC - thresholdC) / lapsePerM; // cold at or above
            float share = (topY - lineY) / Math.Max(topY - bottomY, 1f);
            return share <= 0f ? 0f : share >= 1f ? 1f : share;
        }

        /// <summary>T09: the skiable bar of a run, in normalized depth units. A natural run rated black or double black
        /// (band 3 or 4) needs 31 cm; every other run, and a natural run not rated yet (band below 0), needs 15 cm.
        /// Injected runs always use 15 cm: their guns can reach it (GunTargetDepthM).</summary>
        public static float SkiableBarNorm(RunKind kind, int band)
        {
            return kind == RunKind.Natural && band >= ExpertBandMin ? ExpertNaturalSkiableNorm : SkiableNorm;
        }

        /// <summary>Skiable texel with the default bar (15 cm).</summary>
        public static bool IsSkiable(float depthNorm, float ownTargetNorm)
        {
            return IsSkiable(depthNorm, ownTargetNorm, SkiableNorm);
        }

        /// <summary>Skiable texel (D13 + deviation E1-S1, §4.4), in normalized depth units (0..1 = 0..8 m):
        /// at least <paramref name="barNorm"/> (T09: 15 cm, or 31 cm for a natural black run), or — on a texel where
        /// this run makes snow with a lower (fringe) target — at that target, using the merge's own "at target"
        /// tolerance. Targets under one quantum are ignored. Burst-called.</summary>
        public static bool IsSkiable(float depthNorm, float ownTargetNorm, float barNorm)
        {
            float bar = ownTargetNorm >= 2f * HalfQuantum && ownTargetNorm < barNorm
                ? ownTargetNorm : barNorm;
            return depthNorm >= bar - HalfQuantum;
        }

        // ── T09: gun targets and the ramp (moved here from SkiSnowInjectionSystem.Tuning; Tuning points at these) ──────
        // Gun target 0.35 m (was 0.12), groomed 0.25 m (was 0.08): a run must be able to open by itself, and the open bar is
        // now 0.15 m. 0.35 is 2.3 bars: the pack can sag 0.20 m (melt between passes, edge noise) and still be open, and the
        // slowest texel (rate noise at its minimum) reaches it in about one game hour at -3 C (GunWindowsToReach, tests).
        // Groomed 0.25 is 1.7 bars: grooming is a shoulder-season assist (gate at 0 C), not a gun. The 2026-08-11 field
        // note in Tuning ("raise only with a melt-rate measurement") is answered by the measured 1.9 m pack that the game
        // melted in five wall minutes (analysis-sim-and-hub.md 92-94): melt is not the limit, the open bar is.
        public const float GunTargetDepthM = 0.35f;
        public const float GroomedTargetDepthM = 0.25f;
        public const float RampPerTopupM = 0.004f;     // metres per top-up window at rate 1 and gun output 1
        public const float RampRateMin = 0.25f;        // per-texel noise rate range: Min .. Min + Span
        public const float RampRateSpan = 1.75f;
        public const float RampChillSpanC = 4f;        // degrees below the threshold for full gun output
        public const float RampChillFloor = 0.35f;     // marginal-temperature gun output
        public const float RampChillGain = 1.65f;      // full-cold gun output = Floor + Gain
        public const float TicksPerDay = 262144f;      // = TimeSystem.kTicksPerDay (TimeSystem.cs:18)
        public const float FramesPerGameHour = TicksPerDay / 24f;

        /// <summary>Top-up windows (64 sim frames each, step scale 1) for a texel to climb from bare to
        /// <paramref name="depthM"/> when the air is <paramref name="localC"/> and the gate opens at
        /// <paramref name="thresholdC"/>. <paramref name="rateFactor"/> is the texel's noise rate: RampRateMin is the
        /// slowest texel, RampRateMin + RampRateSpan / 2 the mean. Infinity when the gun does not run (warmer than the
        /// threshold).</summary>
        public static float GunWindowsToReach(float depthM, float localC, float thresholdC, float rateFactor)
        {
            if (localC > thresholdC)
                return float.PositiveInfinity;
            float chill = Math.Max(0f, Math.Min(1f, (thresholdC - localC) / RampChillSpanC));
            float step = RampPerTopupM * rateFactor * (RampChillFloor + RampChillGain * chill);
            return step > 0f ? depthM / step : float.PositiveInfinity;
        }

        /// <summary>Top-up windows to game hours (a window is TopupIntervalFrames sim frames).</summary>
        public static float WindowsToGameHours(float windows)
        {
            return windows * TopupIntervalFrames / FramesPerGameHour;
        }

        // ── T09: warm-spell protection (the pack of a gun run survives a short warm spell) ───────────────────────────
        // We can only WRITE depth; the game's shader melts it (SnowSystem.AddSnow, _MeltMultiplier 2e-5, temperature, rain,
        // sun direction; the melt form is compiled HLSL, unreadable: UNVERIFIED). So protection is a RE-INJECTION with a
        // per-run budget: while a gun run has an open pack and some of its texels are too warm for the guns, those texels are
        // raised back toward the depth they had when protection began, by ProtectRaisePerTopupM per top-up window, for up
        // to ProtectBudgetHours game hours. Natural runs get none. Every number below is one of Richard's rulings or a
        // derivation stated beside it.
        public const float ProtectBudgetHours = 48f;     // ruling: "survive a short dry/warm spell"; proposal 48 game hours
        public const float ProtectMaxLocalC = 10f;       // ruling: "above 50 deg (10 C) melts faster": no protection above it
        public const float ProtectSpringFactor = 0.5f;   // ruling: "springtime melts faster due to sun angle": budget halves
        public const float ProtectNorthFactor = 1.5f;    // ruling: "slope orientation can also affect melt": north faces keep 1.5x
        public const float ProtectSouthFactor = 0.5f;    //   and south faces 0.5x (linear in between; east and west 1.0)
        public const float AspectFullGradient = 0.10f;   // 10 % pitch: aspect counts in full at this slope (ski runs are steeper)
        // 10 mm per window beats the worst melt this rule faces: the estimate 2e-5 x T x 0.2 per snow tick, 16 ticks per
        // window, at T = 10 C is 6.4 mm (snow-injection.md 4; linear form UNVERIFIED), so 10 mm = 1.5x headroom. At step
        // scale 4 (high game speed) it is 40 mm per pass against 4 windows of melt.
        public const float ProtectRaisePerTopupM = 0.010f;
        public const float ProtectRearmColdHours = 3f;   // all texels cold this long = the guns are back; the budget refills
        public const float MaxProtectStepHours = 6f;     // a single pass never credits more than this (a jump in the frame counter)

        /// <summary>Degrees of aspect: 1 on flat ground or an east-west face, up to ProtectNorthFactor on a steep north
        /// face and down to ProtectSouthFactor on a steep south face. <paramref name="dhdx"/> and <paramref name="dhdz"/>
        /// are the terrain slope components (metres per metre, central differences); +Z is north (UNVERIFIED: the sun is
        /// computed in local coordinates from the latitude, PlanetarySystem.cs:455-463); <paramref name="northSign"/> is
        /// -1 for a southern-hemisphere latitude, else 1. A slope faces the way it descends, so it faces north when the
        /// height falls toward +Z.</summary>
        public static float AspectFactor(float dhdx, float dhdz, float northSign)
        {
            float g = (float)Math.Sqrt(dhdx * dhdx + dhdz * dhdz);
            if (!(g > 1e-6f))
                return 1f;
            float weight = Math.Min(1f, g / AspectFullGradient);
            float northness = -dhdz / g * northSign;   // +1 faces north, -1 faces south
            float span = northness >= 0f ? ProtectNorthFactor - 1f : 1f - ProtectSouthFactor;
            return 1f + span * weight * northness;
        }

        /// <summary>Spring (the budget halves). A vanilla-named season decides: "SeasonSpring" is spring, the other three
        /// are not. Any other name (a map with its own seasons, or none): the calendar spring, March to May.</summary>
        public static bool IsSpring(string seasonName, float normalizedDate)
        {
            if (seasonName == "SeasonSpring")
                return true;
            if (seasonName == "SeasonSummer" || seasonName == "SeasonAutumn" || seasonName == "SeasonWinter")
                return false;
            float d = normalizedDate - (float)Math.Floor(normalizedDate);
            return d >= SpringCalendarStart && d < SpringCalendarEnd;
        }
        public const float SpringCalendarStart = 2f / 12f;   // the end of the calendar winter window (SnowLineRules): 1 March
        public const float SpringCalendarEnd = 5f / 12f;     // 1 June

        /// <summary>Budget in game hours for a texel with this aspect factor.</summary>
        public static float EffectiveBudgetHours(bool spring, float aspectFactor)
        {
            return ProtectBudgetHours * (spring ? ProtectSpringFactor : 1f) * aspectFactor;
        }

        /// <summary>The largest budget any texel can have (a steep north face).</summary>
        public static float MaxBudgetHours(bool spring)
        {
            return EffectiveBudgetHours(spring, ProtectNorthFactor);
        }

        /// <summary>Is this warm texel still protected after <paramref name="hoursUsed"/> hours of the spell? Burst-called.</summary>
        public static bool TexelProtected(float hoursUsed, float localC, bool spring, float aspectFactor)
        {
            return localC <= ProtectMaxLocalC && hoursUsed < EffectiveBudgetHours(spring, aspectFactor);
        }

        /// <summary>Sim frames to game hours, capped at MaxProtectStepHours.</summary>
        public static float FramesToHours(uint frames)
        {
            return Math.Min(frames / FramesPerGameHour, MaxProtectStepHours);
        }

        public enum ProtectState : byte { Idle = 0, Active = 1, Spent = 2 }

        public enum ProtectEnd : byte { None = 0, GunsBack = 1, BudgetSpent = 2, TooWarm = 3, PackLost = 4 }

        /// <summary>One gun run's protection record. Runtime only.</summary>
        public struct ProtectRun
        {
            public ProtectState State;
            public float HoursUsed;      // game hours of the current spell with some texel too warm
            public float ColdHours;      // game hours with every texel cold (re-arms at ProtectRearmColdHours)
            public double HeldAtStartM3; // the run's ledger HeldTotalM3 when the spell began (the end line's difference)
        }

        /// <summary>One pass of the per-run protection machine. Idle starts when the run has an open pack, some texel is too
        /// warm for the guns, and the run's own air is not above ProtectMaxLocalC. Active spends one game hour of budget per
        /// hour while some texel is warm; it ends when the pack is lost (the run closed), the air passes ProtectMaxLocalC, the
        /// largest budget is spent, or every texel has been cold for ProtectRearmColdHours (the guns are back). Spent waits for
        /// the guns to come back. Returns why it ended (None otherwise); <paramref name="started"/> is true on the start pass.</summary>
        public static ProtectEnd StepProtection(ref ProtectRun run, float dtHours, int containedTexels, int coldTexels,
            bool packOpen, float runLocalC, bool spring, out bool started)
        {
            started = false;
            bool warm = containedTexels > coldTexels;
            switch (run.State)
            {
                case ProtectState.Idle:
                    if (warm && packOpen && runLocalC <= ProtectMaxLocalC)
                    {
                        run.State = ProtectState.Active;
                        run.HoursUsed = 0f;
                        run.ColdHours = 0f;
                        started = true;
                    }
                    return ProtectEnd.None;
                case ProtectState.Active:
                    if (!warm)
                    {
                        run.ColdHours += dtHours;
                        if (run.ColdHours >= ProtectRearmColdHours)
                        {
                            run.State = ProtectState.Idle;
                            run.ColdHours = 0f;
                            return ProtectEnd.GunsBack;
                        }
                        return ProtectEnd.None;
                    }
                    run.ColdHours = 0f;
                    run.HoursUsed += dtHours;
                    if (!packOpen)
                    {
                        run.State = ProtectState.Spent;
                        return ProtectEnd.PackLost;
                    }
                    if (runLocalC > ProtectMaxLocalC)
                    {
                        run.State = ProtectState.Spent;
                        return ProtectEnd.TooWarm;
                    }
                    if (run.HoursUsed >= MaxBudgetHours(spring))
                    {
                        run.State = ProtectState.Spent;
                        return ProtectEnd.BudgetSpent;
                    }
                    return ProtectEnd.None;
                default: // Spent
                    if (warm)
                    {
                        run.ColdHours = 0f;
                        return ProtectEnd.None;
                    }
                    run.ColdHours += dtHours;
                    if (run.ColdHours >= ProtectRearmColdHours)
                    {
                        run.State = ProtectState.Idle;
                        run.ColdHours = 0f;
                    }
                    return ProtectEnd.None;
            }
        }

        // ── T09: the snow-made ledger (guns and grooming this season and in total; runtime only, not saved) ────────────
        // A season runs from SeasonRolloverDate (0.6 of the year, about 1 August: no snow is made in high summer) to the next.
        public const float SeasonRolloverDate = 0.6f;

        public static int SnowSeasonKey(int year, float normalizedDate)
        {
            float d = normalizedDate - (float)Math.Floor(normalizedDate);
            return d >= SeasonRolloverDate ? year : year - 1;
        }

        /// <summary>Volume in cubic metres of a summed normalized depth gain over texels of the given area.</summary>
        public static double VolumeM3(double normalizedDepthSum, float texelAreaM2)
        {
            return normalizedDepthSum * SnowHeightScaleM * texelAreaM2;
        }

        /// <summary>The same volume as centimetre-texels (1 cm over one texel).</summary>
        public static double CmTexels(double volumeM3, float texelAreaM2)
        {
            return texelAreaM2 > 0f ? volumeM3 / texelAreaM2 * 100.0 : 0.0;
        }

        /// <summary>Snow a run's guns made (raised toward the gun target) and snow protection put back (held against melt),
        /// this season and in total, in cubic metres.</summary>
        public struct RunLedger
        {
            public bool HasSeason;
            public int SeasonKey;
            public double MadeSeasonM3, MadeTotalM3, HeldSeasonM3, HeldTotalM3;

            public void Add(int seasonKey, double madeM3, double heldM3)
            {
                if (!HasSeason || seasonKey != SeasonKey)
                {
                    HasSeason = true;
                    SeasonKey = seasonKey;
                    MadeSeasonM3 = 0.0;
                    HeldSeasonM3 = 0.0;
                }
                MadeSeasonM3 += madeM3;
                MadeTotalM3 += madeM3;
                HeldSeasonM3 += heldM3;
                HeldTotalM3 += heldM3;
            }
        }

        /// <summary>White weight over footprint weight; NaN when the footprint is below
        /// MinFootprintTexels (too small to measure).</summary>
        public static float ShareFromSums(double whiteWeight, double footprintWeight)
        {
            if (!(footprintWeight >= MinFootprintTexels))
                return float.NaN;
            double s = whiteWeight / footprintWeight;
            return (float)(s <= 0.0 ? 0.0 : s >= 1.0 ? 1.0 : s);
        }

        /// <summary>D30: ramp and decay steps scale with the frames since the last evaluated pass.
        /// Exactly 1 at 64 frames (bit-identical to today at 1× and 2×), capped at 4.</summary>
        public static float StepScale(uint elapsedFrames)
        {
            float s = elapsedFrames / (float)TopupIntervalFrames;
            return s < 1f ? 1f : s > MaxStepScale ? MaxStepScale : s;
        }

        /// <summary>D30 dry skip (§7.2): nothing can be raised now, nothing was on the footprint after
        /// the last real pass, the footprint is the same, and the forced real pass is not due.</summary>
        public static bool CanSkipDry(in DryPass lastReal, int coldNow, int containedNow,
            ulong footprintHashNow, uint framesSinceLastReal)
        {
            return lastReal.Valid
                && framesSinceLastReal < ForcedRealPassFrames
                && coldNow == 0
                && lastReal.ColdTexels == 0
                && lastReal.Injected == 0
                && lastReal.RunDepthSum == 0.0
                && containedNow > 0
                && containedNow == lastReal.ContainedTexels
                && footprintHashNow == lastReal.FootprintHash;
        }

        /// <summary>§7.1 step 11 (E1 fixer, e1-spec §8.9 F6): a real pass in an open dry-skip episode is the
        /// FORCED check only when the 1,024-frame term is the one term of CanSkipDry that fails — the same
        /// predicate with the frame term ignored holds. Any other failing term (cold texels, a changed
        /// footprint) is a reason of its own and ends the episode at step 11.</summary>
        public static bool IsForcedDryCheck(in DryPass lastReal, int coldNow, int containedNow,
            ulong footprintHashNow, uint framesSinceLastReal)
        {
            return framesSinceLastReal >= ForcedRealPassFrames
                && CanSkipDry(lastReal, coldNow, containedNow, footprintHashNow, 0u);
        }

        /// <summary>Round M2: THE pack guard (the merge's PackGuard input and the packed skip both read it): managed
        /// melt stands down while the map-wide mean depth is over 2 cm. NaN → off.</summary>
        public static bool PackGuard(float mapMeanDepthM) => mapMeanDepthM > PackGuardMeanDepthM;

        /// <summary>Round M2: a measured share far enough from the 40-60 % band that one forced interval cannot carry it
        /// in at the thaw rate field test 10 showed (topup-skip §4.2). NaN → false.</summary>
        public static bool IsCalmShare(float share) => share >= CalmOpenShare || share < CalmClosedShare;

        /// <summary>Round M2 packed skip: no texel cold now (nothing can be raised); the last real pass was no gate change,
        /// raised and lowered nothing, and left the pack guard on for the next merge (nothing can be lowered: a skipped
        /// pass does not write the map mean); the runs held at least PackedSkipMinRunDepthM (the dress cannot move);
        /// every measured share is calm (no label or multiplier can move); the same footprint; the forced pass not due.</summary>
        public static bool CanSkipPacked(in DryPass lastReal, int coldNow, int containedNow,
            ulong footprintHashNow, uint framesSinceLastReal, bool sharesCalmNow)
        {
            return lastReal.Valid
                && framesSinceLastReal < ForcedRealPassFrames
                && coldNow == 0
                && lastReal.ColdTexels == 0
                && lastReal.Injected == 0
                && lastReal.Lowered == 0
                && lastReal.PackGuardNext
                && lastReal.RunMeanDepthM >= PackedSkipMinRunDepthM
                && sharesCalmNow
                && containedNow > 0
                && containedNow == lastReal.ContainedTexels
                && footprintHashNow == lastReal.FootprintHash;
        }

        /// <summary>Round M2: Dry first (D30 unchanged, and it ignores the share guard), then Packed.</summary>
        public static TopupSkip SkipKind(in DryPass lastReal, int coldNow, int containedNow,
            ulong footprintHashNow, uint framesSinceLastReal, bool sharesCalmNow)
        {
            if (CanSkipDry(lastReal, coldNow, containedNow, footprintHashNow, framesSinceLastReal))
                return TopupSkip.Dry;
            if (CanSkipPacked(lastReal, coldNow, containedNow, footprintHashNow, framesSinceLastReal, sharesCalmNow))
                return TopupSkip.Packed;
            return TopupSkip.None;
        }

        /// <summary>Round M2: the forced check of either kind — the 1,024-frame term is the only failing term (the E1 F6
        /// rule of IsForcedDryCheck, extended).</summary>
        public static bool IsForcedCheck(in DryPass lastReal, int coldNow, int containedNow,
            ulong footprintHashNow, uint framesSinceLastReal, bool sharesCalmNow)
        {
            return framesSinceLastReal >= ForcedRealPassFrames
                && SkipKind(lastReal, coldNow, containedNow, footprintHashNow, 0u, sharesCalmNow) != TopupSkip.None;
        }

        /// <summary>splitmix64 finalizer. Footprint hash = wrapping sum over texel indices (order-free).
        /// Burst-called.</summary>
        public static ulong Mix64(ulong x)
        {
            unchecked
            {
                x += 0x9E3779B97F4A7C15UL;
                x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
                x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
                return x ^ (x >> 31);
            }
        }

        /// <summary>The start of the game hour that contains <paramref name="date"/> (normalized year
        /// date), and its index. daysPerYear below 1 counts as 1.</summary>
        public static float HourAlignedDate(float date, int daysPerYear, out int hourKey)
        {
            int days = daysPerYear < 1 ? 1 : daysPerYear;
            double hours = 24.0 * days;
            double x = date - Math.Floor(date);
            hourKey = (int)Math.Floor(x * hours);
            return (float)(hourKey / hours);
        }

        /// <summary>D13 fallback: mean of DayMeanSamples hourly samples ending at
        /// <paramref name="alignedDate"/> (inclusive), wrapping over the year start.</summary>
        public static float DayMeanC(Func<float, float> temperatureAt, float alignedDate, int daysPerYear)
        {
            int days = daysPerYear < 1 ? 1 : daysPerYear;
            double step = 1.0 / (24.0 * days);
            double sum = 0.0;
            for (int k = 0; k < DayMeanSamples; k++)
            {
                double t = alignedDate - k * step;
                t -= Math.Floor(t);
                sum += temperatureAt((float)t);
            }
            return (float)(sum / DayMeanSamples);
        }
    }
}
