// The pure snow rules: skiable texel test, open state and multiplier, the
// estimate from the day-mean temperature, the dry-skip predicate and step scale, the footprint hash. No UnityEngine, no
// Entities. IsSkiable and Mix64 are called from Burst jobs (SkiSnowInjectionSystem). Linked into tests/ResortBatch
// (SnowRulesTests.cs).
//
// EVERY run opens at 3 ft (0.91 m), made or natural: one bar for all kinds and ratings. The guns of snowmaking and night
// runs (and the groomer) keep making snow while the run is under 3 ft and top it up to 3 ft + the restart band (0.96 m),
// so the run stays open between restarts.
using System;

namespace SkiResortMod.Systems
{
    /// <summary>Open label with hysteresis: opens at >= 60 %, closes below 40 %.</summary>
    public enum RunSnowState : byte { Unknown = 0, Closed = 1, Open = 2 }

    /// <summary>Where a run's numbers come from. GatingOff = the "Snow & season gating" setting is off.</summary>
    public enum RunSnowSource : byte { None = 0, Measured = 1, Estimated = 2, GatingOff = 3 }

    /// <summary>Display phase from the label and the multiplier (hub marks, overlay).</summary>
    public enum RunSnowPhase : byte { Closed = 0, Opening = 1, Open = 2, Closing = 3 }

    /// <summary>What the decal dress and the props' snow follow.</summary>
    public enum RunDressSource : byte { None = 0, Managed = 1, NaturalMeasured = 2 }

    /// <summary>Which skip a pass takes. Dry = bare footprint; Packed = the pack
    /// guard holds managed melt and no texel is cold.</summary>
    public enum TopupSkip : byte { None = 0, Dry = 1, Packed = 2 }

    /// <summary>One run's snow, as published. Runtime only; never serialized.</summary>
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

    /// <summary>What the skip predicates need from a real pass. Runtime only; never
    /// serialized (SkiSnowInjectionSystem implements no serialization interface; nothing else holds one).</summary>
    public struct DryPass
    {
        public bool Valid;
        public int ColdTexels;            // contained texels with T_local <= threshold
        public int Injected;              // texels raised by the merge (real passes only)
        public double RunDepthSum;        // merge's RunDepthSum (real passes only), normalized units
        public int ContainedTexels;       // texels with Scratch > 0, summed over bound injected runs
        public ulong FootprintHash;       // wrapping sum of Mix64(texel index) over the same texels
        // The packed skip's inputs, real passes only.
        public int Lowered;               // texels lowered by managed melt in this pass's merge
        public bool PackGuardNext;        // PackGuard(map mean this pass read): the pack guard the NEXT merge applies
        public float RunMeanDepthM;       // MeanRunSnowDepthM after this pass (the dress depth, m)
    }

    internal static class RunSnowRules
    {
        // Skiable threshold and shares
        // 3 ft (0.91 m, not 0.9144) for every run, made or natural. One bar for every run: the expert bar is the same value (kept
        // as a name so SkiableBarNorm and its callers stay; it can split again by changing this one line).
        public const float SkiableDepthM = 0.91f;
        public const float ExpertNaturalSkiableDepthM = SkiableDepthM;
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
        // Day-mean fallback
        public const int DayMeanSamples = 24;          // hourly samples, the 24 game hours up to now
        public const float GroomedMarginC = 0f;        // = the injection's groomed gate
        // Snowmaking margin: snowmaking runs make snow in up to 38F / 3.3 C temps. The setting is a float slider (step 0.1) so
        // the default can be 3.3; the range stays 0..6.
        public const float DefaultSnowmakingMarginC = 3.3f;  // = SkiResortSettings.DefaultSnowmakingBonusCelsius
        public const float MinSnowmakingMarginC = 0f;       // = the slider's range (SkiResortSettings)
        public const float MaxSnowmakingMarginC = 6f;
        // Top-up cadence and skip limits
        public const int TopupIntervalFrames = 64;     // the injection's Tuning.TopupIntervalFrames points here
        public const float MaxStepScale = 4f;          // covers 8× (3.75); a step never spans more than 4 windows
        public const float MinPassIntervalS = 0.5f;    // at most two evaluated passes per wall-clock second
        public const uint ForcedRealPassFrames = 1024; // 16 windows
        public const uint MeasureIntervalFrames = 1024;
        public const uint MaxSnapshotAgeFrames = 4096; // older snapshots are stale → estimate
        public const int MeasureFailLimit = 10;        // vanilla's retry cap (TerrainSystem.cs:3595)
        public const float PackGuardMeanDepthM = 0.02f;
        // The dress guard. 2 x SkiRunSurfaceSystem.Tuning.FullWhiteDepthM (0.05 m, the saturate(depth / 0.05) prop snow): the dress
        // is saturated above 0.05 m (the decal's season thresholds, 0.02 / 0.005 m, sit below it too) and the extra 0.05 m covers
        // one forced interval of melt (0.046 m per 1,264 frames at most, measured). DEPENDS ON FullWhiteDepthM — that file is not
        // linked into ResortBatch, so no test ties the two: change both together.
        public const float PackedSkipMinRunDepthM = 0.10f;
        public const float CalmOpenShare = 0.70f;           // OpenAtShare + 0.10
        public const float CalmClosedShare = 0.30f;         // CloseBelowShare − 0.10

        /// <summary>Multiplier: 0 below 40 %, 1 at or above 60 %, linear
        /// in between. NaN → 0.</summary>
        public static float OpenMultiplier(float share)
        {
            if (!(share >= CloseBelowShare))
                return 0f;
            if (share >= OpenAtShare)
                return 1f;
            return (share - CloseBelowShare) / (OpenAtShare - CloseBelowShare);
        }

        /// <summary>Label with hysteresis. Unknown (first refresh of a load) uses the midpoint.
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

        /// <summary>The temperature at or below which a run's texels hold snow, per kind, while its guns make snow: the
        /// injection's own gates (snowmaking and night: freezing + margin; groomed: freezing + 0), the same gun margin for a cat track,
        /// and freezing for natural runs (the snow shader's own threshold, presumably 0 °C — UNVERIFIED). An open estimate does not
        /// read this directly: <see cref="EstimateThresholdC"/> adds the gun season and the gun switch.</summary>
        public static float ThresholdC(RunKind kind, float freezingC, float snowmakingMarginC)
        {
            switch (kind)
            {
                case RunKind.Snowmaking:
                case RunKind.Night:
                case RunKind.CatTrack:  // the guns' gate (SnowGunPlumeRules.TrackGunsCanRun); no injection: it opens by the estimate below
                    return freezingC + snowmakingMarginC;
                case RunKind.Groomed:
                    return freezingC + GroomedMarginC;
                default:
                    return freezingC;
            }
        }

        /// <summary>The kinds whose snow guns make snow: snowmaking and night runs, and the cat track, which follows the snowmaking
        /// rules (DECISIONS 551).</summary>
        public static bool IsGunKind(RunKind kind) => kind == RunKind.Snowmaking || kind == RunKind.Night || kind == RunKind.CatTrack;

        /// <summary>Whether a run's snow guns can make snow now, one rule for snowmaking runs, night runs and cat tracks: in the gun
        /// season (SnowGunPlumeRules.InGunSeason; out of it the injection's guns raise nothing, FoldRowsJob.GunGate through
        /// SnowGunPlumeRules.RaisesThisPass) and with its guns on. A run's gun value is look only (tools/check-run-props.js R2), so the run
        /// path passes <paramref name="gunsOn"/> true; a cat track passes its switch (SkiRunPropSystem.CatTrackGunsOn: Off stands no gun,
        /// DECISIONS 563, 568). False for groomed and natural runs.</summary>
        public static bool GunsMakeSnow(RunKind kind, bool inGunSeason, bool gunsOn) => IsGunKind(kind) && inGunSeason && gunsOn;

        /// <summary>The threshold of a run's open estimate: the kind's own gate (<see cref="ThresholdC"/>) while its guns make snow, else
        /// the natural threshold, freezing. So a snowmaking run, a night run or a cat track out of the gun season, and a cat track with its
        /// guns Off, open on natural snow only (DECISIONS 568, 571). Groomed and natural: ThresholdC all year.</summary>
        public static float EstimateThresholdC(RunKind kind, float freezingC, float snowmakingMarginC, bool inGunSeason, bool gunsOn)
        {
            if (IsGunKind(kind) && !GunsMakeSnow(kind, inGunSeason, gunsOn))
                return ThresholdC(RunKind.Natural, freezingC, snowmakingMarginC);
            return ThresholdC(kind, freezingC, snowmakingMarginC);
        }

        /// <summary>THE open estimate of a run or a cat track, one code path for both (SkiSnowInjectionSystem: a run in the table that could
        /// not be measured, and GetRunSnow for a run not in the table, which is every cat track: the injection grid cannot measure a 6-12 m
        /// ribbon, DECISIONS 551): the share of its vertical cold enough at the day-mean temperature for <see cref="EstimateThresholdC"/>.
        /// The open state then follows from the share alone (OpenMultiplier, HubRules.OpenStateOf), the same for both.</summary>
        public static float EstimatedRunShare(RunKind kind, float bottomY, float topY, float dayMeanC, float freezingC,
            float snowmakingMarginC, float baseHeight, float lapsePerM, bool inGunSeason, bool gunsOn)
        {
            return EstimatedShare(bottomY, topY, dayMeanC, EstimateThresholdC(kind, freezingC, snowmakingMarginC, inGunSeason, gunsOn),
                baseHeight, lapsePerM);
        }

        /// <summary>The slider value clamped to 0..6 °C. NaN (a hand-edited options file) → the default.</summary>
        public static float ClampMargin(float settingValue)
        {
            if (float.IsNaN(settingValue))
                return DefaultSnowmakingMarginC;
            return settingValue < MinSnowmakingMarginC ? MinSnowmakingMarginC
                 : settingValue > MaxSnowmakingMarginC ? MaxSnowmakingMarginC : settingValue;
        }

        /// <summary>The share of the run's vertical that is cold enough, with the validated lapse
        /// T_local(h) = T − (h − base) × lapse, from the day-mean temperature and the kind's threshold.
        /// Degenerate lapse: all or nothing.</summary>
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

        /// <summary>The skiable bar of a run, in normalized depth units, by kind and band: one bar for every run, 3 ft (0.91 m),
        /// so both branches return SkiableNorm today (ExpertNaturalSkiableDepthM = SkiableDepthM).</summary>
        public static float SkiableBarNorm(RunKind kind, int band)
        {
            return kind == RunKind.Natural && band >= ExpertBandMin ? ExpertNaturalSkiableNorm : SkiableNorm;
        }

        /// <summary>Skiable texel with the default bar (SkiableNorm).</summary>
        public static bool IsSkiable(float depthNorm, float ownTargetNorm)
        {
            return IsSkiable(depthNorm, ownTargetNorm, SkiableNorm);
        }

        /// <summary>Skiable texel, in normalized depth units (0..1 = 0..8 m):
        /// at least <paramref name="barNorm"/>, or — on a texel where
        /// this run makes snow with a lower (fringe) target — at that target, using the merge's own "at target"
        /// tolerance. Targets under one quantum are ignored. Burst-called.</summary>
        public static bool IsSkiable(float depthNorm, float ownTargetNorm, float barNorm)
        {
            float bar = ownTargetNorm >= 2f * HalfQuantum && ownTargetNorm < barNorm
                ? ownTargetNorm : barNorm;
            return depthNorm >= bar - HalfQuantum;
        }

        // Gun and groomed targets and the ramp (SkiSnowInjectionSystem.Tuning points at these).
        // The base the guns guarantee is the open bar itself.
        public const float GunTargetDepthM = SkiableDepthM;
        // The guns of a snowmaking or night run build the pack to GunStopDepthM and then STOP (the injection's gun ramp ceiling);
        // they restart only when the run's pack falls below GunStopDepthM - GunRestartBandM = SkiableDepthM
        // (SnowGunPlumeRules.RestartBelowM), and never run on a pack that is already that deep (natural snow included), so a pack
        // the guns built never sags under the open bar before they run again. GunTargetDepthM stays the base the guns GUARANTEE
        // within a night. The predicate is SnowGunPlumeRules.NextGunState; the band is the 0.05 m the PackedSkipMinRunDepthM
        // derivation gives one forced interval of the worst measured melt (0.046 m per 1,264 frames), so the guns cannot cycle
        // faster than one forced interval even under that melt.
        public const float GunRestartBandM = 0.05f;
        public const float GunStopDepthM = SkiableDepthM + GunRestartBandM;
        // The groomer lays made snow too, so the groomed ceiling is the gun stop depth (a groomed run could never open on its own
        // under the 3 ft bar otherwise). Its gate stays freezing + 0 (GroomedMarginC).
        public const float GroomedTargetDepthM = GunStopDepthM;
        public const float RampPerTopupM = 0.004f;     // meters per top-up window at rate 1 and gun output 1
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

        // We can only WRITE depth; the game's shader melts it (SnowSystem.AddSnow, _MeltMultiplier 2e-5, temperature, rain,
        // sun direction; the melt form is compiled HLSL, unreadable: UNVERIFIED). So protection is a RE-INJECTION with a
        // per-run budget: while a gun run has an open pack and some of its texels are too warm for the guns, those texels are
        // raised back toward the depth they had when protection began, by ProtectRaisePerTopupM per top-up window, for up
        // to ProtectBudgetHours game hours. Natural runs get none. Every number below is a design choice or a derivation stated
        // beside it.
        public const float ProtectBudgetHours = 48f;  // survive a short dry/warm spell: 48 game hours
        public const float ProtectMaxLocalC = 10f;  // above 10 C melts faster: no protection above it
        public const float ProtectSpringFactor = 0.5f;  // springtime melts faster (sun angle): the budget halves
        public const float ProtectNorthFactor = 1.5f;  // slope orientation affects melt: north faces keep 1.5x
        public const float ProtectSouthFactor = 0.5f;  // south faces 0.5x (linear in between; east and west 1.0)
        public const float AspectFullGradient = 0.10f;   // 10 % pitch: aspect counts in full at this slope (ski runs are steeper)
        // 10 mm per window beats the worst melt this rule faces: the estimate 2e-5 x T x 0.2 per snow tick, 16 ticks per
        // window, at T = 10 C is 6.4 mm (snow-injection.md 4; linear form UNVERIFIED), so 10 mm = 1.5x headroom. At step
        // scale 4 (high game speed) it is 40 mm per pass against 4 windows of melt.
        public const float ProtectRaisePerTopupM = 0.010f;
        public const float ProtectRearmColdHours = 3f;   // all texels cold this long = the guns are back; the budget refills
        public const float MaxProtectStepHours = 6f;     // a single pass never credits more than this (a jump in the frame counter)

        /// <summary>Degrees of aspect: 1 on flat ground or an east-west face, up to ProtectNorthFactor on a steep north
        /// face and down to ProtectSouthFactor on a steep south face. <paramref name="dhdx"/> and <paramref name="dhdz"/>
        /// are the terrain slope components (meters per meter, central differences); +Z is north (UNVERIFIED: the sun is
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

        /// <summary>IsSpring for a map whose winter ends at <paramref name="winterEnd"/> (normalized year; may pass 1): the fallback is the
        /// three months after the climate's winter instead of March to May, so a June winter gets its spring in September.</summary>
        public static bool IsSpring(string seasonName, float normalizedDate, float winterEnd)
        {
            if (seasonName == "SeasonSpring")
                return true;
            if (seasonName == "SeasonSummer" || seasonName == "SeasonAutumn" || seasonName == "SeasonWinter")
                return false;
            float afterWinter = normalizedDate - winterEnd;
            afterWinter -= (float)Math.Floor(afterWinter);
            return afterWinter < SpringCalendarEnd - SpringCalendarStart;
        }

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
            return StepProtection(ref run, dtHours, containedTexels > coldTexels, packOpen, runLocalC, spring, out started);
        }

        /// <summary>The same machine with the warm test given: a run passes "some texel too warm for the guns", a cat track's made snow
        /// "some made snow above what the cold keeps" (StepTrackMadeSnow).</summary>
        public static ProtectEnd StepProtection(ref ProtectRun run, float dtHours, bool warm, bool packOpen, float runLocalC, bool spring,
            out bool started)
        {
            started = false;
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

        // A cat track's made snow (2026-11-02, run-types round). The injection paints no snow on a track (DECISIONS 551: a 6-12 m ribbon
        // on the 14 m grid; the decal is the look), so the snow map under it holds natural snow only, and its guns' snow is this model: the
        // share of its vertical the guns have covered, held by the SAME protection machine a snowmaking run's pack has (StepProtection,
        // ProtectBudgetHours, spring and the 10 C cut) at the largest budget any texel of a run gets (a north face), so a track keeps its
        // made snow at least as long as a run of the same heights. A cat track is groomed and gets made snow first: it closes last.
        //
        // TUNED (flagged to the owner): what melts after the budget. A run's pack sits 0.05 m over the bar at the gun stop depth, and the
        // game's melt (UNVERIFIED linear form, snow-injection.md 4: 2e-5 x T x 0.2 per snow tick) takes that in about 0.5 game h at 1 C and
        // minutes at 10 C. TrackMeltOutHours = 2 lets the track's made share fall over 2 game hours instead of at once: at least the run's
        // tail down to about 0.3 C, and never a one-pass switch on the hub. Raise it for a slower spring close.
        public const float TrackMeltOutHours = 2f;
        public const float TrackWarmEpsilon = 1e-4f;   // a share difference below this is the same height

        /// <summary>One cat track's made snow. Runtime, mirrored to the saved SkiTrackSnow so a load keeps it.</summary>
        public struct TrackMadeSnow
        {
            public float Share;       // 0..1 of the track's vertical holding made snow (top down, as the estimate)
            public ProtectRun Spell;  // the protection spell over it; runtime only (a load refills the budget, as for a run)
        }

        /// <summary>The share of a track's vertical its guns cover now: the estimate at freezing + the margin while they make snow (the gun
        /// season and the switch, GunsMakeSnow), else 0.</summary>
        public static float TrackGunShare(bool inGunSeason, bool gunsOn, float bottomY, float topY, float dayMeanC, float freezingC,
            float snowmakingMarginC, float baseHeight, float lapsePerM)
        {
            if (!GunsMakeSnow(RunKind.CatTrack, inGunSeason, gunsOn))
                return 0f;
            return EstimatedShare(bottomY, topY, dayMeanC, ThresholdC(RunKind.CatTrack, freezingC, snowmakingMarginC), baseHeight, lapsePerM);
        }

        /// <summary>The share of a track's vertical the cold keeps without help: the estimate at the natural threshold (freezing), the
        /// threshold an open estimate uses while no gun makes snow (EstimateThresholdC).</summary>
        public static float TrackColdShare(float bottomY, float topY, float dayMeanC, float freezingC, float baseHeight, float lapsePerM)
        {
            return EstimatedShare(bottomY, topY, dayMeanC, ThresholdC(RunKind.Natural, freezingC, 0f), baseHeight, lapsePerM);
        }

        /// <summary>One pass of a track's made snow. The guns raise it to <paramref name="gunShare"/> at once (the estimate's own speed);
        /// <paramref name="coldShare"/> (the estimate at freezing) is what the cold keeps without help. Made snow above max(gun, cold) is warm:
        /// the protection spell holds it (Active), otherwise it melts toward that floor over TrackMeltOutHours. Returns the spell's end, as
        /// StepProtection.</summary>
        public static ProtectEnd StepTrackMadeSnow(ref TrackMadeSnow track, float dtHours, float gunShare, float coldShare, float trackLocalC,
            bool spring, out bool started)
        {
            gunShare = Clamp01(gunShare);
            coldShare = Clamp01(coldShare);
            if (!(track.Share >= 0f))
                track.Share = 0f;   // NaN from a hand-edited save
            if (gunShare > track.Share)
                track.Share = gunShare;
            float floor = Math.Max(gunShare, coldShare);
            bool warm = track.Share > floor + TrackWarmEpsilon;
            ProtectEnd end = StepProtection(ref track.Spell, dtHours, warm, track.Share > 0f, trackLocalC, spring, out started);
            if (warm && track.Spell.State != ProtectState.Active)
                track.Share = Math.Max(floor, track.Share - Math.Max(0f, dtHours) / TrackMeltOutHours);
            if (track.Share > 1f)
                track.Share = 1f;
            return end;
        }

        /// <summary>A measured or estimated cat track's share: the larger of its snow (the measured snow map under it, natural snow; or the
        /// open estimate when it could not be measured) and its made snow.</summary>
        public static float TrackShare(float snowShare, float madeShare)
        {
            if (float.IsNaN(snowShare))
                return Clamp01(madeShare);
            return Math.Max(Clamp01(snowShare), Clamp01(madeShare));
        }

        private static float Clamp01(float v) => !(v > 0f) ? 0f : v > 1f ? 1f : v;

        // A season runs from SeasonRolloverDate (0.6 of the year, about 1 August: no snow is made in high summer) to the next.
        public const float SeasonRolloverDate = 0.6f;

        public static int SnowSeasonKey(int year, float normalizedDate)
        {
            float d = normalizedDate - (float)Math.Floor(normalizedDate);
            return d >= SeasonRolloverDate ? year : year - 1;
        }

        /// <summary>SnowSeasonKey on a map whose winter starts at <paramref name="winterStart"/>: the rollover keeps its distance before
        /// winter (about 4 months, 0.6 for a December winter), so a June winter rolls over in February and never inside the snow.</summary>
        public static int SnowSeasonKey(int year, float normalizedDate, float winterStart)
        {
            float rollover = winterStart - (11f / 12f - SeasonRolloverDate);   // 11 / 12: SnowLineRules.CalendarWinterStart
            rollover -= (float)Math.Floor(rollover);
            float d = normalizedDate - (float)Math.Floor(normalizedDate);
            return d >= rollover ? year : year - 1;
        }

        /// <summary>Volume in cubic meters of a summed normalized depth gain over texels of the given area.</summary>
        public static double VolumeM3(double normalizedDepthSum, float texelAreaM2)
        {
            return normalizedDepthSum * SnowHeightScaleM * texelAreaM2;
        }

        /// <summary>The same volume as centimeter-texels (1 cm over one texel).</summary>
        public static double CmTexels(double volumeM3, float texelAreaM2)
        {
            return texelAreaM2 > 0f ? volumeM3 / texelAreaM2 * 100.0 : 0.0;
        }

        /// <summary>Snow a run's guns made (raised toward the gun target) and snow protection put back (held against melt),
        /// this season and in total, in cubic meters.</summary>
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

        /// <summary>Ramp and decay steps scale with the frames since the last evaluated pass.
        /// Exactly 1 at 64 frames (bit-identical to today at 1× and 2×), capped at 4.</summary>
        public static float StepScale(uint elapsedFrames)
        {
            float s = elapsedFrames / (float)TopupIntervalFrames;
            return s < 1f ? 1f : s > MaxStepScale ? MaxStepScale : s;
        }

        /// <summary>Dry skip: nothing can be raised now, nothing was on the footprint after
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

        /// <summary>A real pass in an open dry-skip episode is the FORCED check only when the 1,024-frame term is the one term of
        /// CanSkipDry that fails — the same predicate with the frame term ignored holds. Any other failing term (cold texels, a
        /// changed footprint) is a reason of its own and ends the episode.</summary>
        public static bool IsForcedDryCheck(in DryPass lastReal, int coldNow, int containedNow,
            ulong footprintHashNow, uint framesSinceLastReal)
        {
            return framesSinceLastReal >= ForcedRealPassFrames
                && CanSkipDry(lastReal, coldNow, containedNow, footprintHashNow, 0u);
        }

        /// <summary>THE pack guard (the merge's PackGuard input and the packed skip both read it): managed
        /// melt stands down while the map-wide mean depth is over 2 cm. NaN → off.</summary>
        public static bool PackGuard(float mapMeanDepthM) => mapMeanDepthM > PackGuardMeanDepthM;

        /// <summary>A measured share far enough from the 40-60 % band that one forced interval cannot carry it
        /// in at the measured thaw rate. NaN → false.</summary>
        public static bool IsCalmShare(float share) => share >= CalmOpenShare || share < CalmClosedShare;

        /// <summary>Packed skip: no texel cold now (nothing can be raised); the last real pass was no gate change,
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

        /// <summary>Dry first (it ignores the share guard), then Packed.</summary>
        public static TopupSkip SkipKind(in DryPass lastReal, int coldNow, int containedNow,
            ulong footprintHashNow, uint framesSinceLastReal, bool sharesCalmNow)
        {
            if (CanSkipDry(lastReal, coldNow, containedNow, footprintHashNow, framesSinceLastReal))
                return TopupSkip.Dry;
            if (CanSkipPacked(lastReal, coldNow, containedNow, footprintHashNow, framesSinceLastReal, sharesCalmNow))
                return TopupSkip.Packed;
            return TopupSkip.None;
        }

        /// <summary>The forced check of either kind — the 1,024-frame term is the only failing term (IsForcedDryCheck's rule,
        /// extended).</summary>
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

        /// <summary>Mean of DayMeanSamples hourly samples ending at
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
