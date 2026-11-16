// The pure snow rules: skiable texel test, open state and multiplier, the
// estimate from the day-mean temperature, the dry-skip predicate and step scale, the footprint hash. No UnityEngine, no
// Entities. IsSkiable and Mix64 are called from Burst jobs (SkiSnowInjectionSystem). Linked into tests/ResortBatch
// (SnowRulesTests.cs).
//
// EVERY run opens at 3 ft (0.91 m), made or natural: one bar for all kinds and ratings. The guns of snowmaking and night runs and of cat
// tracks build a base of GunStopDepthM (2.0 m, T222, DECISIONS 667) and restart below it minus the band (1.95 m). The groomer tops a groomed
// run up to 3 ft + 0.05 m. Made snow melts at the game's rate like natural snow (one pack step, PackStep): no hold, no managed melt.
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

    /// <summary>Which skip a pass takes. Dry = bare footprint. Packed = the runs hold snow and no texel is cold.</summary>
    public enum TopupSkip : byte { None = 0, Dry = 1, Packed = 2 }

    /// <summary>One run's snow, as published. Runtime only. Never serialized.</summary>
    public struct RunSnow
    {
        public RunKind Kind;
        public RunSnowSource Source;
        public RunSnowState State;        // label, hysteretic
        public float Share;               // 0..1 skiable share, exact (measured or estimated)
        public float Multiplier;          // 0..1 published with a 0.05 dead band. Endpoints exact
        public float MeanDepthM;          // coverage-weighted mean depth (m). NaN when not measured
        public float FootprintTexels;     // coverage-weighted footprint (texels). 0 when not measured
        public float ExcludedShare;       // natural runs: share of the raster owned by injected runs this pass
        public uint SnapshotFrame;        // sim frame of the snapshot used. 0 when not measured
        public float BottomY, TopY;       // Geometry.m_Bounds y (lowest / highest node), for estimates
        // A cat track only (T220): the share and mean of the snow under it, before its made snow. A pass that carries the measurement
        // over starts from these, so the made snow is never counted twice.
        public float SnowShare;
        public float SnowMeanDepthM;
    }

    /// <summary>What the skip predicates need from a real pass. Runtime only. Never serialized (SkiSnowInjectionSystem implements no
    /// serialization interface, and nothing else holds one).</summary>
    public struct DryPass
    {
        public bool Valid;
        public int ColdTexels;            // contained texels with T_local <= threshold
        public int Injected;              // texels raised by the merge (real passes only)
        public double RunDepthSum;        // merge's RunDepthSum (real passes only), normalized units
        public int ContainedTexels;       // texels with Scratch > 0, summed over bound injected runs
        public ulong FootprintHash;       // wrapping sum of Mix64(texel index) over the same texels
        // The packed skip's input, real passes only.
        public float RunMeanDepthM;       // MeanRunSnowDepthM after this pass (the dress depth, m)
    }

    internal static class RunSnowRules
    {
        // Skiable threshold and shares
        // 3 ft (0.91 m, not 0.9144) for every run, made or natural. One bar for every run: the expert bar is the same value (kept as a name
        // so SkiableBarNorm and its callers stay. It can split again by changing this one line).
        public const float SkiableDepthM = 0.91f;
        public const float ExpertNaturalSkiableDepthM = SkiableDepthM;
        public const byte ExpertBandMin = 3;                               // = SkiResortScore.BandBlack (3 black, 4 double black)
        public const float SnowHeightScaleM = 8f;                          // = SnowSystem kSnowHeightScale
        public const float SkiableNorm = SkiableDepthM / SnowHeightScaleM; // 0.11375
        public const float ExpertNaturalSkiableNorm = ExpertNaturalSkiableDepthM / SnowHeightScaleM;
        public const float HalfQuantum = 0.5f / 65535f;                    // = SkiSnowInjectionSystem.HalfQuantum
        // The keep bar: an open run stays open on this much snow (MeasuredShare). TUNED (flagged to the owner): 1 ft. The open bar is the
        // owner's 3 ft rule for opening. Once open, a pack that settles or melts below 3 ft is still skiable. A real resort closes a run
        // when bare patches show, not when its base dips under the depth it opened on. With 3 ft alone, a gun run closed in one pass of a
        // thaw with 0.4-0.6 m of snow left on it (field/2026-11-02/cattrack-snow, DECISIONS 574).
        public const float KeepDepthM = 0.30f;
        public const float KeepNorm = KeepDepthM / SnowHeightScaleM;
        public const float OpenAtShare = 0.60f;
        public const float CloseBelowShare = 0.40f;
        public const float InitialOpenShare = 0.50f;   // first label of a load (no history): the band's midpoint
        public const float MultiplierDeadBand = 0.05f;
        public const float MinFootprintTexels = 0.5f;  // below this a run is too small to measure. It gets the estimate
        // Day-mean fallback
        public const int DayMeanSamples = 24;          // hourly samples, the 24 game hours up to now
        public const float GroomedMarginC = 0f;        // = the injection's groomed gate
        // Snowmaking margin: snowmaking runs make snow at temperatures up to 38F / 3.3 C. The setting is a float slider (step 0.1) so the
        // default can be 3.3. The range stays 0..6.
        public const float DefaultSnowmakingMarginC = 3.3f;  // = SkiResortSettings.DefaultSnowmakingBonusCelsius
        public const float MinSnowmakingMarginC = 0f;       // = the slider's range (SkiResortSettings)
        public const float MaxSnowmakingMarginC = 6f;
        // Top-up cadence and skip limits
        public const int TopupIntervalFrames = 64;     // the injection's Tuning.TopupIntervalFrames points here
        public const float MaxStepScale = 4f;          // covers 8× (3.75). A step never spans more than 4 windows
        public const float MinPassIntervalS = 0.5f;    // at most two evaluated passes per wall-clock second
        public const uint ForcedRealPassFrames = 1024; // 16 windows
        public const uint MeasureIntervalFrames = 1024;
        public const uint MaxSnapshotAgeFrames = 4096; // older snapshots are stale. They get the estimate
        public const int MeasureFailLimit = 10;        // vanilla's retry cap (TerrainSystem.cs:3595)
        // The dress guard. 2 x SkiRunSurfaceSystem.Tuning.FullWhiteDepthM (0.05 m, the saturate(depth / 0.05) prop snow). The dress is
        // saturated above 0.05 m (the decal's season thresholds, 0.02 / 0.005 m, sit below it too). The extra 0.05 m covers one forced
        // interval of melt (0.046 m per 1,264 frames at most, measured). DEPENDS ON FullWhiteDepthM: that file is not linked into
        // ResortBatch, so no test ties the two. Change both together.
        public const float PackedSkipMinRunDepthM = 0.10f;
        public const float CalmOpenShare = 0.70f;           // OpenAtShare + 0.10
        public const float CalmClosedShare = 0.30f;         // CloseBelowShare − 0.10

        /// <summary>Multiplier: 0 below 40 %, 1 at or above 60 %, linear in between. NaN gives 0.</summary>
        public static float OpenMultiplier(float share)
        {
            if (!(share >= CloseBelowShare))
                return 0f;
            if (share >= OpenAtShare)
                return 1f;
            return (share - CloseBelowShare) / (OpenAtShare - CloseBelowShare);
        }

        /// <summary>A measured run's share. A closed run counts texels at the open bar (it opens at 3 ft). An open run counts them at the
        /// keep bar (it stays open on 1 ft). The first refresh of a load also counts them at the keep bar (Unknown: the label is not saved,
        /// and a run that was open on a settled pack stays open). The keep share is never below the open share (a lower bar), so the label
        /// cannot reverse in the pass after it opens. NaN in either share stays NaN.</summary>
        public static float MeasuredShare(RunSnowState previous, float openShare, float keepShare)
        {
            return previous == RunSnowState.Closed ? openShare : keepShare;
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

        /// <summary>The temperature at or below which a run's texels hold snow, per kind, while its guns make snow. The values are the
        /// injection's own gates (snowmaking and night: freezing + margin, groomed: freezing + 0), the same gun margin for a cat track, and
        /// freezing for natural runs (the snow shader's own threshold, presumably 0 °C, UNVERIFIED). An open estimate does not read this
        /// directly: <see cref="EstimateThresholdC"/> adds the gun season and the gun switch.</summary>
        public static float ThresholdC(RunFeatures f, float freezingC, float snowmakingMarginC)
        {
            if (f.Snowmaking)   // a cat track too: the guns' gate (SnowGunPlumeRules.TrackGunsCanRun). No injection: it opens by the estimate below
                return freezingC + snowmakingMarginC;
            if (f.Groomed)
                return freezingC + GroomedMarginC;
            return freezingC;
        }

        public static float ThresholdC(RunKind kind, float freezingC, float snowmakingMarginC) =>
            ThresholdC(RunFeatures.FromKind((int)kind), freezingC, snowmakingMarginC);

        /// <summary>The kinds whose snow guns make snow: snowmaking and night runs, and the cat track, which follows the snowmaking
        /// rules (DECISIONS 551). = RunFeatures.FromKind(kind).Snowmaking.</summary>
        public static bool IsGunKind(RunKind kind) => RunFeatures.FromKind((int)kind).Snowmaking;

        /// <summary>Whether a run's snow guns can make snow now, one rule for snowmaking runs, night runs and cat tracks: it has snowmaking
        /// and it is the gun season (SnowGunPlumeRules.InGunSeason. Out of it the injection's guns raise nothing, FoldRowsJob.GunGate
        /// through SnowGunPlumeRules.RaisesThisPass). False for groomed and natural runs.</summary>
        public static bool GunsMakeSnow(RunFeatures f, bool inGunSeason) => f.Snowmaking && inGunSeason;

        /// <summary>The kind form: <paramref name="gunsOn"/> is the switch (RunFeatures.FromKind). A run's kind already says whether its
        /// guns are on (RunFeatures.SnowKind, DECISIONS 577), so the run path passes true. A cat track passes its switch
        /// (SkiRunPropSystem.CatTrackGunsOn: Off stands no gun, DECISIONS 563, 568).</summary>
        public static bool GunsMakeSnow(RunKind kind, bool inGunSeason, bool gunsOn) => GunsMakeSnow(RunFeatures.FromKind((int)kind, gunsOn), inGunSeason);

        /// <summary>The threshold of a run's open estimate: its own gate (<see cref="ThresholdC(RunFeatures, float, float)"/>) while its
        /// guns make snow, else freezing, the natural threshold (= the groomed one while GroomedMarginC is 0. A gun run whose switch is off
        /// has no snowmaking feature and lands on the groomed gate). So a snowmaking run, a night run or a cat track out of the gun season
        /// opens on natural snow only. A cat track with its guns Off does the same (DECISIONS 568, 571). Groomed and natural: ThresholdC
        /// all year.</summary>
        public static float EstimateThresholdC(RunFeatures f, float freezingC, float snowmakingMarginC, bool inGunSeason)
        {
            if (f.Snowmaking && !GunsMakeSnow(f, inGunSeason))
                return freezingC;
            return ThresholdC(f, freezingC, snowmakingMarginC);
        }

        public static float EstimateThresholdC(RunKind kind, float freezingC, float snowmakingMarginC, bool inGunSeason, bool gunsOn) =>
            EstimateThresholdC(RunFeatures.FromKind((int)kind, gunsOn), freezingC, snowmakingMarginC, inGunSeason);

        /// <summary>THE open estimate of a run or a cat track, one code path for both. SkiSnowInjectionSystem uses it for a run in the
        /// table that could not be measured. GetRunSnow uses it for a run not in the table, which is every cat track (the injection grid
        /// cannot measure a 6-12 m ribbon, DECISIONS 551). It is the share of its vertical cold enough at the day-mean temperature for
        /// <see cref="EstimateThresholdC"/>. The open state then follows from the share alone (OpenMultiplier, HubRules.OpenStateOf), the same
        /// for both.</summary>
        public static float EstimatedRunShare(RunKind kind, float bottomY, float topY, float dayMeanC, float freezingC,
            float snowmakingMarginC, float baseHeight, float lapsePerM, bool inGunSeason, bool gunsOn)
        {
            return EstimatedShare(bottomY, topY, dayMeanC, EstimateThresholdC(kind, freezingC, snowmakingMarginC, inGunSeason, gunsOn),
                baseHeight, lapsePerM);
        }

        /// <summary>The slider value clamped to 0..6 °C. NaN (a hand-edited options file) gives the default.</summary>
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

        /// <summary>The skiable bar of a run, in normalized depth units, by kind and band. It is one bar for every run, 3 ft (0.91 m), so
        /// both branches return SkiableNorm today (ExpertNaturalSkiableDepthM = SkiableDepthM).</summary>
        public static float SkiableBarNorm(RunFeatures f, int band)
        {
            return f.IsNatural && band >= ExpertBandMin ? ExpertNaturalSkiableNorm : SkiableNorm;
        }

        public static float SkiableBarNorm(RunKind kind, int band) => SkiableBarNorm(RunFeatures.FromKind((int)kind), band);

        /// <summary>Skiable texel with the default bar (SkiableNorm).</summary>
        public static bool IsSkiable(float depthNorm, float ownTargetNorm)
        {
            return IsSkiable(depthNorm, ownTargetNorm, SkiableNorm);
        }

        /// <summary>Skiable texel, in normalized depth units (0..1 = 0..8 m). A texel is skiable at <paramref name="barNorm"/> or more. On
        /// a texel where this run makes snow with a lower (fringe) target, it is also skiable at that target, using the merge's own "at
        /// target" tolerance. Targets under one quantum are ignored. Burst-called.</summary>
        public static bool IsSkiable(float depthNorm, float ownTargetNorm, float barNorm)
        {
            float bar = ownTargetNorm >= 2f * HalfQuantum && ownTargetNorm < barNorm
                ? ownTargetNorm : barNorm;
            return depthNorm >= bar - HalfQuantum;
        }

        /// <summary>A fringe texel of an injected run: its own target is real (one quantum or more) and under the open bar. Every full
        /// target (groomed, snowmaking, night) is GunStopDepthM, above the bar, so only the feathered edge (at most FringeMaxDepthM) is
        /// fringe. The run's share and mean skip these texels (T183). Burst-called.</summary>
        public static bool IsFringeTarget(float ownTargetNorm, float barNorm)
        {
            return ownTargetNorm >= 2f * HalfQuantum && ownTargetNorm < barNorm - HalfQuantum;
        }

        // Gun and groomed targets and the ramp (SkiSnowInjectionSystem.Tuning points at these).
        // The base the guns guarantee within a night is the open bar itself.
        public const float GunTargetDepthM = SkiableDepthM;
        // T222 (DECISIONS 667): the guns of a snowmaking run, a night run and a cat track build the pack to GunStopDepthM, a 2.0 m base, and
        // then STOP. They restart only when the pack falls below GunStopDepthM - GunRestartBandM (1.95 m, SnowGunPlumeRules.RestartBelowM).
        // They never run on a pack that is already that deep (natural snow included). Made snow melts at the game's rate like natural snow
        // (PackMeltM); it lasts longer only because the guns build more of it. 2.0 m is a design value from the round 2 season
        // simulation (snowpack REPORT section 3): under the 2.5-4 m natural peak at the top of a cold map, and a deeper base adds under
        // 3 open hours a season for 14-30 % more made snow. Before T222 it was 0.96 m with a two-month protection hold.
        public const float GunRestartBandM = 0.05f;
        public const float GunStopDepthM = 2.00f;
        // The groomer lays snow too: a groomed run is topped up to the open bar + the band (0.96 m, the pre-T222 value), so it can open on
        // its own under the 3 ft bar. Its gate stays freezing + 0 (GroomedMarginC). It has no guns and no hysteresis.
        public const float GroomedTargetDepthM = SkiableDepthM + GunRestartBandM;
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

        // The game's melt (T222). We can only WRITE depth; the game's shader melts it (SnowSystem.AddSnow every 4 frames: _MeltMultiplier
        // 2e-5, the temperature, rain, the sun direction; compiled HLSL, the form is unreadable). The round 2 bridge melt probe (tools/
        // testbridge meltprobe; 9 runs, forced 2 / 5 / 10 / 20 C and rain, flat and both aspects, 0.3 to 2.0 m patches) measured it as
        //   dd/dt = -max(MeltFloorPerHourM, MeltDepthCoef x air / d)   while the local air is above freezing, 0 at or below it:
        // deep snow loses at least one R16 quantum (8 / 65535 m) per 4-frame snow update, 0.333 m per game hour, at ANY air above freezing;
        // thin or warm snow melts faster, inversely with the depth (0.0425 m^2/h/C, RMS 3.1 cm over 54 patch series). Aspect, slope and
        // rain made no difference. So a 2.0 m pack melts at 0.333 m/h up to ~16 C of air. A run texel is melted by the game itself; a cat
        // track cell, which has no texel (DECISIONS 551), is melted by the mod with this rule (PackMeltM), so a track and a run beside it
        // melt alike. The snowpack report's linear estimate (0.26 m/h/C) was 4x too fast for deep snow at 5 C.
        public const int SnowUpdateIntervalFrames = 4;                                                       // = SnowSystem.GetUpdateInterval
        public const float MeltFloorPerHourM = SnowHeightScaleM / 65535f * (FramesPerGameHour / SnowUpdateIntervalFrames);   // 0.33334
        public const float MeltDepthCoef = 0.0425f;   // m^2 per game hour per C above freezing
        public const float MaxPackStepHours = 6f;     // a single pass never credits more than this (a jump in the frame counter)

        /// <summary>Sim frames to game hours, capped at MaxPackStepHours.</summary>
        public static float FramesToHours(uint frames)
        {
            return Math.Min(frames / FramesPerGameHour, MaxPackStepHours);
        }

        // A cat track's made snow (T222, DECISIONS 667). The injection paints no snow on a track (DECISIONS 551: a 6-12 m ribbon on the 14 m
        // grid, the decal is the look), so the snow map under it holds natural snow only. Its guns' snow lives in TrackCells cells down its
        // vertical (cell 0 at the top), runtime only, each one a texel the mod owns. Every pass, each cell melts at the shared rate at its own
        // air (the lapse), the track's gun state follows NextPackGun on the pack of its cold cells (the run rule), and PackStep builds each
        // cold cell toward the 2.0 m base. So a track and a run over the same vertical give the same open hours, gun hours and made snow
        // (snowpack REPORT section 3). The share is the cells holding made snow, the depth their mean. The open state counts cells at the
        // run bars (the open bar while closed, the keep bar while open). The save is lossy and uses the existing layouts: the share in
        // SkiTrackSnow v1, the mean depth in SkiTrackPack v2 (whole mm, now clamped to 8 m instead of 0.96 m). A load rebuilds the cells top
        // down from (share, depth) and the gun state from the depth alone (PackGunAfterLoad), so plumes, the hub and the bill are right on
        // the first pass (T223). Natural snow is not in the cells: it counts through the measured snow share (TrackShare), as before.
        public const int TrackCells = 8;
        // A 1.0.0 save (no SkiTrackPack) loads its made share as a full pack at the pre-T222 stop depth, what a 1.0.0 track had.
        public const float LegacyTrackStopDepthM = 0.96f;

        /// <summary>One cat track's made snow. Runtime, saved lossily to SkiTrackSnow (the share) and SkiTrackPack (the mean depth).</summary>
        public struct TrackMadeSnow
        {
            public float C0, C1, C2, C3, C4, C5, C6, C7;   // made pack per cell (m), cell 0 at the top of the vertical
            public PackGun Gun;          // the gun state (NextPackGun). Runtime only: a load derives it from the saved depth
            public float GunShare;       // the share of cells cold enough for the guns, while they make snow (season, switch): the hub's gun line
            public float GunReach;       // the same with the switch on (CatTrackGunsCanRun: the grooming and lights bill)
            public float MakingShare;    // the share of cells the guns make snow on now (Running and cold): plumes, the hub, the bill

            public float Cell(int i)
            {
                switch (i)
                {
                    case 0: return C0;
                    case 1: return C1;
                    case 2: return C2;
                    case 3: return C3;
                    case 4: return C4;
                    case 5: return C5;
                    case 6: return C6;
                    default: return C7;
                }
            }

            public void SetCell(int i, float v)
            {
                switch (i)
                {
                    case 0: C0 = v; break;
                    case 1: C1 = v; break;
                    case 2: C2 = v; break;
                    case 3: C3 = v; break;
                    case 4: C4 = v; break;
                    case 5: C5 = v; break;
                    case 6: C6 = v; break;
                    default: C7 = v; break;
                }
            }

            /// <summary>0..1 of the vertical holding made snow (cells with a pack).</summary>
            public float Share
            {
                get
                {
                    int n = 0;
                    for (int i = 0; i < TrackCells; i++)
                        if (Cell(i) > 0f)
                            n++;
                    return n / (float)TrackCells;
                }
            }

            /// <summary>The mean made pack over the cells holding one (m), 0 without made snow.</summary>
            public float DepthM
            {
                get
                {
                    int n = 0;
                    float sum = 0f;
                    for (int i = 0; i < TrackCells; i++)
                    {
                        float c = Cell(i);
                        if (c > 0f)
                        {
                            n++;
                            sum += c;
                        }
                    }
                    return n > 0 ? sum / n : 0f;
                }
            }

            /// <summary>The share of cells at or above <paramref name="barM"/> (the merge's tolerance).</summary>
            public float ShareAtOrAbove(float barM)
            {
                int n = 0;
                for (int i = 0; i < TrackCells; i++)
                    if (Cell(i) >= barM - HalfQuantum * SnowHeightScaleM)
                        n++;
                return n / (float)TrackCells;
            }
        }

        /// <summary>The middle height of cell <paramref name="i"/> (0 = top) of a vertical from <paramref name="bottomY"/> to
        /// <paramref name="topY"/>.</summary>
        public static float TrackCellY(float bottomY, float topY, int i)
        {
            if (topY < bottomY)
            {
                float swap = topY;
                topY = bottomY;
                bottomY = swap;
            }
            return topY - (i + 0.5f) * (topY - bottomY) / TrackCells;
        }

        /// <summary>A track's made snow on its first refresh of a load, from its saved share (SkiTrackSnow) and its saved mean pack depth
        /// (SkiTrackPack, <paramref name="savedDepthM"/>): the top round(share x TrackCells) cells (at least one for any share) get the
        /// depth. NaN depth: a save without SkiTrackPack (1.0.0, or a track never stepped by 1.0.1), which loads a full pack at
        /// LegacyTrackStopDepthM, the 1.0.0 rule. The gun state starts Unknown: the first step decides it from the depth (PackGunAfterLoad).</summary>
        public static TrackMadeSnow TrackFromSave(float savedShare, float savedDepthM = float.NaN)
        {
            var t = new TrackMadeSnow();
            float share = Clamp01(savedShare);
            if (!(share > 0f))
                return t;
            float depth = float.IsNaN(savedDepthM) ? LegacyTrackStopDepthM
                : savedDepthM < 0f ? 0f : Math.Min(savedDepthM, PackSaveMaxDepthM);
            if (!(depth > 0f))
                return t;
            int n = Math.Max(1, Math.Min(TrackCells, (int)Math.Round(share * TrackCells)));
            for (int i = 0; i < n; i++)
                t.SetCell(i, depth);
            return t;
        }

        /// <summary>The snow share an ESTIMATE gives a cat track: an estimate keeps an Open track open (as a NaN share does) but never opens
        /// one. A track opens on measured snow or on its made pack only (bridge pass r1c: an estimated track opened on "0.0 texel(s), mean
        /// NaN m" at a 14.5 C day mean and closed 10 s later).</summary>
        public static float TrackEstimatedSnowShare(RunSnowState previous, float estimate)
        {
            return previous == RunSnowState.Open ? estimate : 0f;
        }

        /// <summary>The made share that counts for the open state: cells at the keep bar (1 ft) while the track is Open, at the open bar
        /// (3 ft) while it is Closed or Unknown. Unknown differs from a run's MeasuredShare (keep bar): a track's state comes back from the
        /// save (TrackStateFromSave), so Unknown means no saved state (a 1.0.0 save loads a full pack, which clears both bars).</summary>
        public static float TrackMadeCountedShare(RunSnowState previous, in TrackMadeSnow track)
        {
            return track.ShareAtOrAbove(previous == RunSnowState.Open ? KeepDepthM : SkiableDepthM);
        }

        /// <summary>A track's open state from its saved SkiTrackPack, the previous state of its first refresh (and of GetRunSnow before it).
        /// <paramref name="stateSaved"/>: the value is v2 or newer (SkiTrackPack.StateSaved). No saved state (no SkiTrackPack, or v1): Unknown.
        /// </summary>
        public static RunSnowState TrackStateFromSave(bool stateSaved, bool savedOpen)
        {
            if (!stateSaved)
                return RunSnowState.Unknown;
            return savedOpen ? RunSnowState.Open : RunSnowState.Closed;
        }

        /// <summary>A track's share before its first refresh of a load (GetRunSnow while the injection runs): the natural estimate as it
        /// counts from the saved state (TrackEstimatedSnowShare) and its saved made snow as it counts (TrackMadeCountedShare).</summary>
        public static float TrackShareBeforeRefresh(RunSnowState saved, float estimate, in TrackMadeSnow made)
        {
            return TrackShare(TrackEstimatedSnowShare(saved, estimate), TrackMadeCountedShare(saved, made));
        }

        /// <summary>A track's mean snow depth for the hub and the logs: on its made share, the larger of the made pack and the natural mean.
        /// On the rest, the natural mean (the snow map under it). A NaN natural mean (estimated) counts 0 there. NaN when there is neither.</summary>
        public static float TrackMeanDepthM(float naturalMeanM, in TrackMadeSnow track)
        {
            float made = track.Share;
            bool hasNatural = !float.IsNaN(naturalMeanM);
            if (!(made > 0f))
                return hasNatural ? naturalMeanM : float.NaN;
            float natural = hasNatural ? Math.Max(0f, naturalMeanM) : 0f;
            return made * Math.Max(track.DepthM, natural) + (1f - made) * natural;
        }

        /// <summary>The track's guns make snow now: plumes (SkiSnowGunPlumeSystem), the hub's "Making" (HubRules.TrackGunWhyOf) and the
        /// snowmaking bill (SkiRunUpkeepRates.TrackUpkeep on MakingShare) read this one flag. It follows from the gun state, the air and
        /// the season and switch only, never from runtime history, so a load gets it right on its first pass (T223).</summary>
        public static bool TrackGunsBuilding(in TrackMadeSnow track) => track.MakingShare > 0f;

        /// <summary>A track's guns can run by its model (SkiSnowInjectionSystem.CatTrackGunsCanRun while the injection runs): the gun season and
        /// some of the vertical cold enough for them at the air now, the switch aside (GunReach). The grooming and lights bill and the stats'
        /// cost row follow it. Too warm: no reach, no bill.</summary>
        public static bool TrackGunsCanRun(bool inSeason, in TrackMadeSnow track) => inSeason && track.GunReach > 0f;

        /// <summary>One pass of a track's made snow (T222). <paramref name="gunsAvailable"/>: the guns may make snow (the gun season and
        /// the switch, GunsMakeSnow). <paramref name="reachAvailable"/>: the same with the switch on (GunReach). <paramref name="airAtBaseC"/>,
        /// <paramref name="baseHeight"/>, <paramref name="lapsePerM"/>: the air now by elevation (SnowLineSystem's coefficients). The order is the
        /// pack step's: every cell melts at its own air (PackMeltM), the gun state steps on the pack of the cold cells (NextPackGun:
        /// min(cell, stop) averaged; no cold cell keeps the state), then PackStep builds every cold cell (never above the stop depth). dt 0 (the
        /// first pass of a load) melts and builds nothing but still decides the state and MakingShare from the depth.
        /// <paramref name="naturalDepthM"/>: the measured natural snow under the track (NaN when estimated). It only feeds the gun decision
        /// (a cell's pack for NextPackGun is the larger of its made snow and this), so the guns never run on a track whose natural pack
        /// is already at the base, as on a run. It never counts as made snow (the open share stays the cells' own).</summary>
        public static void StepTrackPack(ref TrackMadeSnow track, in PackParams p, float bottomY, float topY, float airAtBaseC, float baseHeight,
            float lapsePerM, bool gunsAvailable, bool reachAvailable, float dtHours, float naturalDepthM = float.NaN)
        {
            float natural = naturalDepthM > 0f ? naturalDepthM : 0f;
            float dt = dtHours > 0f ? Math.Min(dtHours, MaxPackStepHours) : 0f;
            float threshold = p.FreezingC + p.MarginC;
            float packSum = 0f;
            int cold = 0;
            for (int i = 0; i < TrackCells; i++)
            {
                float air = CellAir(bottomY, topY, i, airAtBaseC, baseHeight, lapsePerM);
                float d = track.Cell(i);
                if (!(d >= 0f))
                    d = 0f;   // NaN from a hand-edited save
                d = Math.Max(0f, d - PackMeltM(p, d, air, dt));
                track.SetCell(i, d);
                if (air <= threshold)
                {
                    cold++;
                    packSum += Math.Min(Math.Max(d, natural), p.StopDepthM);
                }
            }
            track.Gun = NextPackGun(p, track.Gun, cold > 0 ? packSum / cold : float.NaN);
            int making = 0;
            for (int i = 0; i < TrackCells; i++)
            {
                float air = CellAir(bottomY, topY, i, airAtBaseC, baseHeight, lapsePerM);
                if (!PackGunsMaking(p, track.Gun, air, gunsAvailable))
                    continue;
                making++;
                track.SetCell(i, PackStep(p, track.Cell(i), air, true, track.Gun, dt, MeanRampRate, applyMelt: false).DepthM);
            }
            track.GunShare = gunsAvailable ? cold / (float)TrackCells : 0f;
            track.GunReach = reachAvailable ? cold / (float)TrackCells : 0f;
            track.MakingShare = making / (float)TrackCells;
        }

        private static float CellAir(float bottomY, float topY, int i, float airAtBaseC, float baseHeight, float lapsePerM) =>
            airAtBaseC - (TrackCellY(bottomY, topY, i) - baseHeight) * lapsePerM;

        /// <summary>A measured or estimated cat track's share: the larger of its snow (the measured snow map under it, which is natural
        /// snow, or the natural estimate when it could not be measured) and its made snow as it counts (TrackMadeCountedShare).</summary>
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

        /// <summary>Snow a run's guns (or the groomer) made, raised toward the target, this season and in total, in cubic meters.</summary>
        public struct RunLedger
        {
            public bool HasSeason;
            public int SeasonKey;
            public double MadeSeasonM3, MadeTotalM3;

            public void Add(int seasonKey, double madeM3)
            {
                if (!HasSeason || seasonKey != SeasonKey)
                {
                    HasSeason = true;
                    SeasonKey = seasonKey;
                    MadeSeasonM3 = 0.0;
                }
                MadeSeasonM3 += madeM3;
                MadeTotalM3 += madeM3;
            }
        }

        /// <summary>White weight over footprint weight. NaN when the footprint is below MinFootprintTexels (too small to
        /// measure).</summary>
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
        /// CanSkipDry that fails (the same predicate with the frame term ignored holds). Any other failing term (cold texels, a changed
        /// footprint) is a reason of its own and ends the episode.</summary>
        public static bool IsForcedDryCheck(in DryPass lastReal, int coldNow, int containedNow,
            ulong footprintHashNow, uint framesSinceLastReal)
        {
            return framesSinceLastReal >= ForcedRealPassFrames
                && CanSkipDry(lastReal, coldNow, containedNow, footprintHashNow, 0u);
        }

        /// <summary>A measured share far enough from the 40-60 % band that one forced interval cannot carry it in at the measured thaw
        /// rate. NaN gives false.</summary>
        public static bool IsCalmShare(float share) => share >= CalmOpenShare || share < CalmClosedShare;

        /// <summary>Packed skip: no texel is cold now (nothing can be raised). The last real pass was no gate change and raised nothing
        /// (the mod lowers nothing since T222: the game alone melts). The runs held at least PackedSkipMinRunDepthM (the dress cannot move). Every measured share is calm (no label or multiplier can
        /// move). The footprint is the same. The forced pass is not due.</summary>
        public static bool CanSkipPacked(in DryPass lastReal, int coldNow, int containedNow,
            ulong footprintHashNow, uint framesSinceLastReal, bool sharesCalmNow)
        {
            return lastReal.Valid
                && framesSinceLastReal < ForcedRealPassFrames
                && coldNow == 0
                && lastReal.ColdTexels == 0
                && lastReal.Injected == 0
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

        /// <summary>The forced check of either kind: the 1,024-frame term is the only failing term (IsForcedDryCheck's rule,
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

        // ---------------------------------------------------------------------------------------------------------------------------
        // T222 (round 2, DECISIONS 667): ONE snow pack step for a run texel and a cat track cell.
        //
        // The physics rule: made snow is not special for melting. It melts at the game's rate like natural snow and lasts longer only
        // because the guns build more of it. So the step has no hold, no budget, no aspect or spring factor and no 10 C cut. It has:
        // - the gun gate: the guns may make snow (season, switch, snowmaking feature: gunsAvailable) and the air at the cell is at most
        //   freezing + the gun margin;
        // - the gun hysteresis, the same as SnowGunPlumeRules.NextGunState but on the pack's own stop depth: Running below the restart
        //   line, Stopped at the stop depth, kept in the band. Unknown (a load, no history) in the band reads Stopped, under the restart
        //   line Running. So the state after a load follows from the saved depth alone (T223);
        // - the build: the injection's ramp per hour (RampPerTopupM x rate x (floor + gain x chill) x windows per hour), never above the
        //   stop depth and never lowering deeper (natural) snow;
        // - the melt: max(MeltFloorPerHourM, MeltDepthCoef x (air - freezing) / depth) per game hour above freezing (PackMeltM). A run
        //   texel lives in the game's snow map and the game's shader melts it, so the run path passes applyMelt false and the step adds
        //   the build only. A cat track cell has no texel (DECISIONS 551), so the mod melts it with the same rule. That rule is the one
        //   tuning point both paths share, measured against vanilla by the round 2 bridge melt probe.
        //
        // The per-surface part is only where the depth lives: a snow map texel (read, raised by the merge, melted by the game) or a
        // track cell (SkiTrackPack, raised and melted here).

        /// <summary>The pack's gun state. The same values as SnowGunPlumeRules.GunState, so the plume system can read either.</summary>
        public enum PackGun : byte { Unknown = 0, Stopped = 1, Running = 2 }

        /// <summary>The tunable numbers of the pack step. <see cref="PackParams.For"/> gives the live ones (the 2.0 m base, the measured
        /// melt, this pass's freezing and gun margin).</summary>
        public struct PackParams
        {
            public float StopDepthM;          // the guns build to this and stop (GunStopDepthM, 2.0 m)
            public float RestartBandM;        // they restart below StopDepthM - this (0.05 m)
            public float MeltFloorPerHourM;   // the game's melt floor, m per game hour at any air above freezing (MeltFloorPerHourM)
            public float MeltDepthCoef;       // the game's depth term, m^2 per game hour per degree above freezing (MeltDepthCoef)
            public float FreezingC;           // ClimateSystem.freezingTemperature (0 in every vanilla climate)
            public float MarginC;             // the gun margin above freezing (ClampMargin of the setting, default 3.3)

            /// <summary>The measured melt with the stop depth <paramref name="stopDepthM"/>, freezing 0 C.</summary>
            public static PackParams With(float stopDepthM, float marginC = DefaultSnowmakingMarginC) =>
                new PackParams
                {
                    StopDepthM = stopDepthM,
                    RestartBandM = GunRestartBandM,
                    MeltFloorPerHourM = RunSnowRules.MeltFloorPerHourM,
                    MeltDepthCoef = RunSnowRules.MeltDepthCoef,
                    FreezingC = 0f,
                    MarginC = marginC,
                };

            /// <summary>The live parameters: the 2.0 m base, the measured melt, the climate's freezing and the gun margin of this pass.</summary>
            public static PackParams For(float freezingC, float marginC)
            {
                PackParams p = With(GunStopDepthM, marginC);
                p.FreezingC = float.IsNaN(freezingC) ? 0f : freezingC;
                return p;
            }

            /// <summary>The defaults: freezing 0 C (every vanilla climate) and the default gun margin.</summary>
            public static PackParams Default => For(0f, DefaultSnowmakingMarginC);
        }

        /// <summary>Top-up windows per game hour (170.7).</summary>
        public const float WindowsPerGameHour = FramesPerGameHour / TopupIntervalFrames;

        /// <summary>The mean texel's noise rate (RampRateMin + RampRateSpan / 2 = 1.125): a cat track cell builds at it.</summary>
        public const float MeanRampRate = RampRateMin + 0.5f * RampRateSpan;

        /// <summary>Meters the guns add per game hour at <paramref name="airC"/> against the gun threshold <paramref name="thresholdC"/>
        /// (freezing + margin), at the noise rate <paramref name="rateFactor"/>: the injection's ramp per window times the windows per hour.
        /// 0 above the threshold and for NaN. About 0.27 m/h at the threshold and 1.54 m/h at full chill for the mean rate.</summary>
        public static float PackBuildPerHourM(float airC, float thresholdC, float rateFactor)
        {
            if (!(airC <= thresholdC))
                return 0f;
            float chill = Math.Max(0f, Math.Min(1f, (thresholdC - airC) / RampChillSpanC));
            return RampPerTopupM * Math.Max(0f, rateFactor) * (RampChillFloor + RampChillGain * chill) * WindowsPerGameHour;
        }

        /// <summary>The game's melt rate now, m per game hour, for <paramref name="depthM"/> of snow at <paramref name="airC"/>:
        /// max(floor, coef x (air - freezing) / depth). 0 at or below freezing, for NaN and for no snow.</summary>
        public static float PackMeltPerHourM(in PackParams p, float airC, float depthM)
        {
            float above = airC - p.FreezingC;
            if (!(above > 0f) || !(depthM > 0f))
                return 0f;
            return Math.Max(p.MeltFloorPerHourM, p.MeltDepthCoef * above / depthM);
        }

        /// <summary>Meters the game melts off <paramref name="depthM"/> of snow in <paramref name="dtHours"/> game hours at
        /// <paramref name="airC"/>: dd/dt = -max(floor, coef x above / d), integrated exactly (the floor down to the crossover depth
        /// coef x above / floor, then d^2 falls by 2 x coef x above per hour). At most the depth; 0 at or below freezing and for NaN.</summary>
        public static float PackMeltM(in PackParams p, float depthM, float airC, float dtHours)
        {
            float above = airC - p.FreezingC;
            float d = depthM > 0f ? depthM : 0f;
            if (!(above > 0f) || d <= 0f || !(dtHours > 0f))
                return 0f;
            float k = Math.Max(0f, p.MeltDepthCoef) * above;
            float floor = Math.Max(0f, p.MeltFloorPerHourM);
            float t = dtHours;
            float x = d;
            float cross = floor > 0f ? k / floor : float.PositiveInfinity;   // below this depth the depth term is the faster
            if (x > cross)
            {
                float toCross = (x - cross) / floor;
                if (t <= toCross)
                    return floor * t;
                t -= toCross;
                x = cross;
            }
            float r = x * x - 2f * k * t;
            float left = r > 0f ? (float)Math.Sqrt(r) : 0f;
            return Math.Min(d, d - left);
        }

        /// <summary>The hysteresis on the pack's own stop depth. NaN keeps the state. At or above the stop depth (less the merge's
        /// tolerance): Stopped. Below the restart line: Running. In the band: kept, Unknown reads Stopped.</summary>
        public static PackGun NextPackGun(in PackParams p, PackGun previous, float depthM)
        {
            if (float.IsNaN(depthM))
                return previous;
            if (depthM >= p.StopDepthM - HalfQuantum * SnowHeightScaleM)
                return PackGun.Stopped;
            if (depthM < p.StopDepthM - p.RestartBandM)
                return PackGun.Running;
            return previous == PackGun.Running ? PackGun.Running : PackGun.Stopped;
        }

        /// <summary>The result of one pack step.</summary>
        public struct PackResult
        {
            public float DepthM;      // the depth after the step
            public float BuiltM;      // meters the guns added this step (the ledger's made snow)
            public float MeltedM;     // meters the step melted (0 for a run texel: the game melts it)
            public bool GunsMaking;   // the guns added snow on this cell this step (the ledger, the per-texel bill mask)
        }

        /// <summary>ONE pack step, for a run texel and a cat track cell. <paramref name="depthM"/>: the pack now (for a texel, the snow map
        /// depth, natural snow included). <paramref name="airC"/>: the air at the cell (the lapse applied). <paramref name="gunsAvailable"/>:
        /// the cell has snowmaking, the switch is on and it is the gun season (GunsMakeSnow). <paramref name="dtHours"/>: game hours since the
        /// last step (clamped to 0..MaxPackStepHours). <paramref name="gunNow"/>: the state NextPackGun decided this pass on the surface's
        /// pack (a run: SnowGunPlumeRules.PackDepthM over its texels, one state for the run; a track: its own depth), before the step.
        /// <paramref name="rateFactor"/>: the texel's noise rate (MeanRampRate for a track). <paramref name="applyMelt"/>: false for a run
        /// texel (the game's shader melts it), true for a track cell. Order: the melt comes off, then the build goes on, never above the stop
        /// depth and never lowering deeper snow.</summary>
        public static PackResult PackStep(in PackParams p, float depthM, float airC, bool gunsAvailable, PackGun gunNow, float dtHours,
            float rateFactor, bool applyMelt)
        {
            float depth = depthM >= 0f ? depthM : 0f;   // NaN and negatives: bare
            float dt = dtHours > 0f ? Math.Min(dtHours, MaxPackStepHours) : 0f;
            PackGun next = gunNow;
            float melted = 0f;
            if (applyMelt)
            {
                melted = PackMeltM(p, depth, airC, dt);
                depth -= melted;
            }
            float built = 0f;
            float build = gunsAvailable && next == PackGun.Running
                ? PackBuildPerHourM(airC, p.FreezingC + p.MarginC, rateFactor) * dt : 0f;
            if (build > 0f && depth < p.StopDepthM)
            {
                built = Math.Min(build, p.StopDepthM - depth);
                depth += built;
            }
            return new PackResult
            {
                DepthM = depth,
                BuiltM = built,
                MeltedM = melted,
                GunsMaking = built > 0f,
            };
        }

        /// <summary>The gun state a cell starts a load with: NextPackGun from Unknown on its saved depth, so a load never needs the gun
        /// history (T223). A pack under the restart line resumes at once, one in the band waits, as a run does today.</summary>
        public static PackGun PackGunAfterLoad(in PackParams p, float savedDepthM) => NextPackGun(p, PackGun.Unknown, savedDepthM);

        /// <summary>The run path of the pack step for one texel (Burst-callable: the merge job). <paramref name="depthM"/>: the texel's
        /// depth, <paramref name="targetM"/>: its own target (2.0 m for a gun texel, 0.96 m groomed), <paramref name="chill"/>: 0..1 below the
        /// gate (the ramp mask), <paramref name="rateFactor"/>: the texel's noise rate, <paramref name="stepScale"/>: windows since the last
        /// pass. The run's gun state was already applied (a Stopped run marks no texel), so the step runs as Running with no melt: the game
        /// melts the texel. Returns the new depth (m).</summary>
        public static float PackRaiseTexelM(float depthM, float targetM, float chill, float rateFactor, float stepScale)
        {
            var p = new PackParams { StopDepthM = targetM, RestartBandM = GunRestartBandM, MeltFloorPerHourM = MeltFloorPerHourM, MeltDepthCoef = MeltDepthCoef, FreezingC = 0f, MarginC = 0f };
            return PackStep(p, depthM, -chill * RampChillSpanC, true, PackGun.Running, stepScale / WindowsPerGameHour, rateFactor, applyMelt: false).DepthM;
        }

        /// <summary>The largest pack a track cell saves: the snow map's own scale (8 m). SkiTrackPack.m_DepthMm is an i32, so a deeper base
        /// needs NO layout change: only the clamp moves from GunStopDepthM to this. A 1.0.1 build that loads a deeper value clamps it to its
        /// own 0.96 m (its TrackDepthFromMm), a graceful downgrade.</summary>
        public const float PackSaveMaxDepthM = SnowHeightScaleM;

        /// <summary>A pack depth as SkiTrackPack saves it: whole millimeters, 0..PackSaveMaxDepthM. NaN or less than 0 saves 0.</summary>
        public static int PackDepthToMm(float depthM) => !(depthM > 0f) ? 0 : (int)Math.Round(Math.Min(depthM, PackSaveMaxDepthM) * 1000f);

        /// <summary>The saved millimeters as meters, 0..PackSaveMaxDepthM.</summary>
        public static float PackDepthFromMm(int mm) => mm <= 0 ? 0f : Math.Min(mm / 1000f, PackSaveMaxDepthM);

        /// <summary>Whether a cell's guns make snow now, from the state the last step left and the air now. The plume system and the
        /// snowmaking bill read THIS (not a runtime work counter), so a load bills and shows plumes from the first pass on (T223).</summary>
        public static bool PackGunsMaking(in PackParams p, PackGun gun, float airC, bool gunsAvailable) =>
            gunsAvailable && gun == PackGun.Running && airC <= p.FreezingC + p.MarginC;
    }
}
