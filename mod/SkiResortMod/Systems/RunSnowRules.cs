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
        public const float SkiableDepthM = 0.02f;
        public const float SnowHeightScaleM = 8f;                          // = SnowSystem kSnowHeightScale
        public const float SkiableNorm = SkiableDepthM / SnowHeightScaleM; // 0.0025
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
        public const int DefaultSnowmakingMarginC = 2; // = SkiResortSettings.DefaultSnowmakingBonusCelsius (O8)
        public const int MinSnowmakingMarginC = 0;     // = the slider's range (SkiResortSettings)
        public const int MaxSnowmakingMarginC = 6;
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
                    return freezingC + snowmakingMarginC;
                case RunKind.Groomed:
                    return freezingC + GroomedMarginC;
                default:
                    return freezingC;
            }
        }

        public static int ClampMargin(int settingValue)
        {
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

        /// <summary>Skiable texel (D13 + deviation E1-S1, §4.4), in normalized depth units (0..1 = 0..8 m):
        /// at least 2 cm, or — on a texel where this run makes snow with a lower (fringe) target —
        /// at that target, using the merge's own "at target" tolerance. Targets under one quantum are
        /// ignored. Burst-called.</summary>
        public static bool IsSkiable(float depthNorm, float ownTargetNorm)
        {
            float bar = ownTargetNorm >= 2f * HalfQuantum && ownTargetNorm < SkiableNorm
                ? ownTargetNorm : SkiableNorm;
            return depthNorm >= bar - HalfQuantum;
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
