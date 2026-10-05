// RunSnowRules — the pure snow rules of Round E1 (docs/research/econ-round/spec/ch3-snow.md):
// skiable texel test, open state and multiplier (D13), the estimate from the day-mean temperature
// (D13 fallback), the dry-skip predicate and step scale (D30), the footprint hash. No UnityEngine,
// no Entities. IsSkiable and Mix64 are called from Burst jobs (SkiSnowInjectionSystem).
//
// Stage 0 (Round E1, S0, 2026-10-04; e1-spec I-06) laid down the shared types block verbatim (the four
// enums, RunSnow and DryPass); stream S3 (2026-10-04) added `internal static class RunSnowRules` (ch3 §2,
// verbatim) and owns the file. Linked into tests/ResortBatch (SnowRulesTests.cs, ch3 §11: 108 checks).
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

    /// <summary>What the dry-skip predicate needs from a pass (§7.2).</summary>
    public struct DryPass
    {
        public bool Valid;
        public int ColdTexels;            // contained texels with T_local <= threshold
        public int Injected;              // texels raised by the merge (real passes only)
        public double RunDepthSum;        // merge's RunDepthSum (real passes only), normalized units
        public int ContainedTexels;       // texels with Scratch > 0, summed over bound injected runs
        public ulong FootprintHash;       // wrapping sum of Mix64(texel index) over the same texels
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
