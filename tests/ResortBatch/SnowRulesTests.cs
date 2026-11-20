// Tests for the snow rules. Oracles are literal hand values. These items need the Unity runtime, so the field test covers them:
// - the Burst jobs
// - the snapshot state machine
// - the async request
// - the merge with stepScale
// - the episode bookkeeping
// - the Snow [day] roll

using System;
using SkiResortMod.Systems;

namespace ResortBatch
{
    public static class SnowRulesTests
    {
        public static void Run(Func<bool, string, string, bool> check)
        {
            Constants(check);
            OpenMultiplierCases(check);
            NextStateCases(check);
            SeasonSteps(check);
            RepublishCases(check);
            PhaseCases(check);
            ThresholdCases(check);
            ClampMarginCases(check);
            EstimatedShareCases(check);
            IsSkiableCases(check);
            ShareFromSumsCases(check);
            StepScaleCases(check);
            CanSkipDryCases(check);
            ForcedDryCheckCases(check);
            PackedConstants(check);
            CalmShareCases(check);
            CanSkipPackedCases(check);
            SkipKindCases(check);
            ForcedCheckCases(check);
            PackedProperties(check);
            Mix64Cases(check);
            HourAlignedDateCases(check);
            DayMeanCases(check);
            T09Cases(check);
            ContinuityCases(check);
        }

        // T268: the open rule by continuity (RunSnowRules ContinuityBand, WorstBandShare, ContinuityShare, OpenShare, Gap).
        private static void ContinuityCases(Func<bool, string, string, bool> check)
        {
            const string W = "snow T268 continuity";
            check(RunSnowRules.ContinuityBands == 8 && RunSnowRules.BandOpenShare == 0.5f && RunSnowRules.BandCloseShare == 0.25f &&
                  RunSnowRules.ContinuityMinBandTexels == 4f && RunSnowRules.ContinuityMinBandShare == 0.05f, W,
                "a T268 constant moved: the oracles below assume 8 bands, 50 / 25 %, a 4-texel and 5 % band floor");
            // ContinuityBand: band 0 at the top, clamped, a flat or NaN run in band 0
            int b;
            b = RunSnowRules.ContinuityBand(1000f, 200f, 1000f); check(b == 0, W, "top -> " + b);
            b = RunSnowRules.ContinuityBand(200f, 200f, 1000f);  check(b == 7, W, "bottom -> " + b);
            b = RunSnowRules.ContinuityBand(150f, 200f, 1000f);  check(b == 7, W, "under the bottom node -> " + b);
            b = RunSnowRules.ContinuityBand(1100f, 200f, 1000f); check(b == 0, W, "over the top node -> " + b);
            b = RunSnowRules.ContinuityBand(899f, 200f, 1000f);  check(b == 1, W, "899 m of 200..1000 -> " + b);
            b = RunSnowRules.ContinuityBand(500f, 500f, 500.5f); check(b == 0, W, "a 0.5 m vertical -> " + b);
            b = RunSnowRules.ContinuityBand(float.NaN, 0f, 100f); check(b == 0, W, "NaN elevation -> " + b);
            // ContinuityShare: 50 % -> 60 %, 25 % -> 40 %, clamps, NaN
            float v;
            v = RunSnowRules.ContinuityShare(0.5f);  check(Near(v, 0.6, 1e-6), W, "band 0.5 -> " + v);
            v = RunSnowRules.ContinuityShare(0.25f); check(Near(v, 0.4, 1e-6), W, "band 0.25 -> " + v);
            v = RunSnowRules.ContinuityShare(0f);    check(Near(v, 0.2, 1e-6), W, "band 0 -> " + v);
            v = RunSnowRules.ContinuityShare(1f);    check(v == 1f, W, "band 1 -> " + v);
            check(float.IsNaN(RunSnowRules.ContinuityShare(float.NaN)), W, "band NaN stays NaN");

            var f = new double[8];
            var k = new double[8];
            bool bottom;
            // Promenade: 8 even bands of 10 texels, the lower half bare, the upper half full -> worst 0 at the bottom
            for (int i = 0; i < 8; i++) { f[i] = 10; k[i] = i < 4 ? 10 : 0; }
            v = RunSnowRules.WorstBandShare(f, k, 0, out bottom);
            check(v == 0f && bottom, W, "lower half bare: worst " + v + ", bottom " + bottom);
            float area = 0.5f;
            float open = RunSnowRules.OpenShare(area, v);
            check(Near(open, 0.2, 1e-6) && RunSnowRules.NextState(RunSnowState.Open, open) == RunSnowState.Closed &&
                  RunSnowRules.OpenMultiplier(open) == 0f, W, "a bare lower half closes an open run: open share " + open);
            check(RunSnowRules.Gap(area, open, bottom) == RunSnowGap.Bottom, W, "the gap reads Bottom");
            // the 1.0.3 shot: 63 % area, a bare lower quarter (bands 6-7) -> closed, gap at the bottom
            for (int i = 0; i < 8; i++) { f[i] = 10; k[i] = i < 6 ? 8.4 : 0; }
            v = RunSnowRules.WorstBandShare(f, k, 0, out bottom);
            open = RunSnowRules.OpenShare(0.63f, v);
            check(v == 0f && bottom && RunSnowRules.NextState(RunSnowState.Open, open) == RunSnowState.Closed, W,
                "63 % with a bare lower quarter closes: worst " + v + ", open share " + open);
            // uniform 70 % cover: the area rule decides (no change from 1.0.3)
            for (int i = 0; i < 8; i++) { f[i] = 10; k[i] = 7; }
            v = RunSnowRules.WorstBandShare(f, k, 0, out bottom);
            open = RunSnowRules.OpenShare(0.7f, v);
            check(Near(v, 0.7, 1e-6) && open == 0.7f, W, "uniform 70 %: worst " + v + ", open share " + open + " (= the area share)");
            check(RunSnowRules.Gap(0.7f, open, bottom) == RunSnowGap.None, W, "uniform 70 %: no gap");
            // a thin stretch partway down: 90 % everywhere, band 3 at 40 % -> Patchy, gap Partway
            for (int i = 0; i < 8; i++) { f[i] = 10; k[i] = i == 3 ? 4 : 9; }
            v = RunSnowRules.WorstBandShare(f, k, 0, out bottom);
            open = RunSnowRules.OpenShare(0.86f, v);
            float m = RunSnowRules.OpenMultiplier(open);
            check(Near(v, 0.4, 1e-6) && !bottom && m > 0f && m < 1f, W, "a 40 % stretch partway: worst " + v + ", multiplier " + m);
            check(RunSnowRules.Gap(0.86f, open, bottom) == RunSnowGap.Partway, W, "the gap reads Partway");
            // the merge: a 2-texel bare run-out tip (band 7) joins band 6 and no longer closes the run alone
            for (int i = 0; i < 8; i++) { f[i] = 20; k[i] = 20; }
            f[7] = 2; k[7] = 0;
            v = RunSnowRules.WorstBandShare(f, k, 0, out bottom);
            check(Near(v, 20.0 / 22.0, 1e-5) && bottom, W, "a 2-texel bare tip merges into the band above: worst " + v);
            // a small run: under 2 x 4 texels counted -> NaN (the area rule)
            for (int i = 0; i < 8; i++) { f[i] = 0.9; k[i] = 0; }
            v = RunSnowRules.WorstBandShare(f, k, 0, out bottom);
            check(float.IsNaN(v) && RunSnowRules.OpenShare(0.65f, v) == 0.65f, W, "7.2 texels counted: no band rule, open share = area");
            // a flat run (every texel in band 0) -> one group -> NaN
            for (int i = 0; i < 8; i++) { f[i] = 0; k[i] = 0; }
            f[0] = 60; k[0] = 30;
            v = RunSnowRules.WorstBandShare(f, k, 0, out bottom);
            check(float.IsNaN(v), W, "a flat run in one band: no band rule (" + v + ")");
            // the rule off, NaN area
            check(RunSnowRules.OpenShare(0.63f, 0f, false) == 0.63f, W, "Options off: the area share");
            check(float.IsNaN(RunSnowRules.OpenShare(float.NaN, 0.5f)), W, "NaN area stays NaN");
            // a fully open run has no gap even when the band rule lowers its share a little
            check(RunSnowRules.Gap(0.95f, 0.8f, true) == RunSnowGap.None, W, "multiplier 1: no gap line");
            // the hysteresis on the band scale: open at a 50 % band, stays open down to 25 %
            for (int i = 0; i < 8; i++) { f[i] = 10; k[i] = 10; }
            k[7] = 5;
            open = RunSnowRules.OpenShare(0.95f, RunSnowRules.WorstBandShare(f, k, 0, out bottom));
            check(RunSnowRules.NextState(RunSnowState.Closed, open) == RunSnowState.Open, W, "a 50 % run-out opens a closed run (" + open + ")");
            k[7] = 2.6;
            open = RunSnowRules.OpenShare(0.95f, RunSnowRules.WorstBandShare(f, k, 0, out bottom));
            check(RunSnowRules.NextState(RunSnowState.Open, open) == RunSnowState.Open &&
                  RunSnowRules.NextState(RunSnowState.Closed, open) == RunSnowState.Closed, W, "a 26 % run-out keeps an open run open, a closed one shut (" + open + ")");
            k[7] = 2.4;
            open = RunSnowRules.OpenShare(0.95f, RunSnowRules.WorstBandShare(f, k, 0, out bottom));
            check(RunSnowRules.NextState(RunSnowState.Open, open) == RunSnowState.Closed, W, "a 24 % run-out closes an open run (" + open + ")");
        }

        private static bool Near(double a, double b, double eps)
        {
            return Math.Abs(a - b) <= eps;
        }

        private static void Constants(Func<bool, string, string, bool> check)
        {
            const string W = "snow S1 constants";
            check(RunSnowRules.SkiableNorm == 0.91f / 8f && RunSnowRules.OpenAtShare == 0.60f &&
                  RunSnowRules.CloseBelowShare == 0.40f && RunSnowRules.MultiplierDeadBand == 0.05f &&
                  RunSnowRules.DefaultSnowmakingMarginC == 3.3f && RunSnowRules.ForcedRealPassFrames == 1024u &&
                  RunSnowRules.MeasureIntervalFrames == 1024u && RunSnowRules.MaxSnapshotAgeFrames == 4096u &&
                  RunSnowRules.MinPassIntervalS == 0.5f && RunSnowRules.MaxStepScale == 1024f &&
                  RunSnowRules.TopupIntervalFrames == 64 && RunSnowRules.DayMeanSamples == 24,
                W, "a tuning constant moved: the literal oracles below assume the ch3 §2 values (T09: skiable bar 15 cm, margin 3.3 C)");
            check((byte)RunKind.Natural == 3, W, "RunKind.Natural = " + (byte)RunKind.Natural + ", expected 3 (appended, never renumbered)");
        }

        private static void OpenMultiplierCases(Func<bool, string, string, bool> check)
        {
            const string W = "snow S2 OpenMultiplier";
            float v;
            v = RunSnowRules.OpenMultiplier(0f);       check(v == 0f, W, "0 -> " + v);
            v = RunSnowRules.OpenMultiplier(0.39f);    check(v == 0f, W, "0.39 -> " + v);
            v = RunSnowRules.OpenMultiplier(0.4f);     check(v == 0f, W, "0.4 -> " + v);
            v = RunSnowRules.OpenMultiplier(0.5f);     check(Near(v, 0.5, 1e-5), W, "0.5 -> " + v);
            v = RunSnowRules.OpenMultiplier(0.55f);    check(Near(v, 0.75, 1e-5), W, "0.55 -> " + v);
            v = RunSnowRules.OpenMultiplier(0.6f);     check(v == 1f, W, "0.6 -> " + v);
            v = RunSnowRules.OpenMultiplier(1f);       check(v == 1f, W, "1 -> " + v);
            v = RunSnowRules.OpenMultiplier(float.NaN); check(v == 0f, W, "NaN -> " + v);
        }

        private static void NextStateCases(Func<bool, string, string, bool> check)
        {
            const string W = "snow S3 NextState";
            State(check, W, RunSnowState.Unknown, 0.5f, RunSnowState.Open);
            State(check, W, RunSnowState.Unknown, 0.49f, RunSnowState.Closed);
            State(check, W, RunSnowState.Closed, 0.59f, RunSnowState.Closed);
            State(check, W, RunSnowState.Closed, 0.6f, RunSnowState.Open);
            State(check, W, RunSnowState.Open, 0.4f, RunSnowState.Open);
            State(check, W, RunSnowState.Open, 0.39f, RunSnowState.Closed);
            State(check, W, RunSnowState.Open, float.NaN, RunSnowState.Open);
            State(check, W, RunSnowState.Closed, float.NaN, RunSnowState.Closed);
            State(check, W, RunSnowState.Unknown, float.NaN, RunSnowState.Closed);
        }

        private static void State(Func<bool, string, string, bool> check, string w, RunSnowState previous, float share,
            RunSnowState expected)
        {
            RunSnowState got = RunSnowRules.NextState(previous, share);
            check(got == expected, w, "(" + previous + ", " + share + ") -> " + got + ", expected " + expected);
        }

        // A season, step by step: state, published multiplier (dead band), phase
        private static void SeasonSteps(Func<bool, string, string, bool> check)
        {
            const string W = "snow S4 season";
            float[] shares = { 0.00f, 0.30f, 0.45f, 0.55f, 0.62f, 0.58f, 0.45f, 0.39f, 0.42f, 0.61f };
            RunSnowState[] states =
            {
                RunSnowState.Closed, RunSnowState.Closed, RunSnowState.Closed, RunSnowState.Closed, RunSnowState.Open,
                RunSnowState.Open, RunSnowState.Open, RunSnowState.Closed, RunSnowState.Closed, RunSnowState.Open,
            };
            float[] published = { 0f, 0f, 0.25f, 0.75f, 1f, 0.9f, 0.25f, 0f, 0.1f, 1f };
            RunSnowPhase[] phases =
            {
                RunSnowPhase.Closed, RunSnowPhase.Closed, RunSnowPhase.Opening, RunSnowPhase.Opening, RunSnowPhase.Open,
                RunSnowPhase.Closing, RunSnowPhase.Closing, RunSnowPhase.Closed, RunSnowPhase.Opening, RunSnowPhase.Open,
            };
            RunSnowState state = RunSnowState.Unknown;
            float pub = float.NaN;
            for (int i = 0; i < shares.Length; i++)
            {
                float s = shares[i];
                state = RunSnowRules.NextState(state, s);
                float m = RunSnowRules.OpenMultiplier(s);
                if (RunSnowRules.Republish(pub, m))
                    pub = m;
                RunSnowPhase phase = RunSnowRules.Phase(state, pub);
                check(state == states[i] && Near(pub, published[i], 1e-5) && phase == phases[i], W,
                    "step " + i + " s=" + s + ": " + state + " / " + pub + " / " + phase + ", expected " +
                    states[i] + " / " + published[i] + " / " + phases[i]);
            }
        }

        private static void RepublishCases(Func<bool, string, string, bool> check)
        {
            const string W = "snow S5 Republish";
            check(RunSnowRules.Republish(float.NaN, 0.3f), W, "(NaN, 0.3) should republish");
            check(!RunSnowRules.Republish(0.25f, 0.25f), W, "(0.25, 0.25) should hold");
            check(!RunSnowRules.Republish(0.5f, 0.53125f), W, "(0.5, 0.53125) should hold (inside the dead band)");
            check(RunSnowRules.Republish(0.5f, 0.5625f), W, "(0.5, 0.5625) should republish");
            check(RunSnowRules.Republish(0.96875f, 1f), W, "(0.96875, 1) should republish (endpoint)");
            check(RunSnowRules.Republish(0.03125f, 0f), W, "(0.03125, 0) should republish (endpoint)");
            check(!RunSnowRules.Republish(1f, 1f), W, "(1, 1) should hold");
            check(!RunSnowRules.Republish(0f, 0f), W, "(0, 0) should hold");
        }

        private static void PhaseCases(Func<bool, string, string, bool> check)
        {
            const string W = "snow S6 Phase";
            PhaseOf(check, W, RunSnowState.Open, 1f, RunSnowPhase.Open);
            PhaseOf(check, W, RunSnowState.Open, 0.5f, RunSnowPhase.Closing);
            PhaseOf(check, W, RunSnowState.Closed, 0f, RunSnowPhase.Closed);
            PhaseOf(check, W, RunSnowState.Closed, 0.5f, RunSnowPhase.Opening);
            PhaseOf(check, W, RunSnowState.Unknown, 0f, RunSnowPhase.Closed);
        }

        private static void PhaseOf(Func<bool, string, string, bool> check, string w, RunSnowState state, float m,
            RunSnowPhase expected)
        {
            RunSnowPhase got = RunSnowRules.Phase(state, m);
            check(got == expected, w, "(" + state + ", " + m + ") -> " + got + ", expected " + expected);
        }

        private static void ThresholdCases(Func<bool, string, string, bool> check)
        {
            const string W = "snow S7 ThresholdC";
            float v;
            v = RunSnowRules.ThresholdC(RunKind.Natural, 0f, 2f);       check(v == 0f, W, "(Natural, 0, 2) -> " + v);
            v = RunSnowRules.ThresholdC(RunKind.Groomed, 0f, 2f);       check(v == 0f, W, "(Groomed, 0, 2) -> " + v);
            v = RunSnowRules.ThresholdC(RunKind.Snowmaking, 0f, 2f);    check(v == 2f, W, "(Snowmaking, 0, 2) -> " + v);
            v = RunSnowRules.ThresholdC(RunKind.Night, 0f, 2f);         check(v == 2f, W, "(Night, 0, 2) -> " + v);
            v = RunSnowRules.ThresholdC(RunKind.Snowmaking, -1f, 3f);   check(v == 2f, W, "(Snowmaking, -1, 3) -> " + v);
            v = RunSnowRules.ThresholdC(RunKind.Night, 0.5f, 0f);       check(v == 0.5f, W, "(Night, 0.5, 0) -> " + v);
        }

        private static void ClampMarginCases(Func<bool, string, string, bool> check)
        {
            const string W = "snow S8 ClampMargin";
            float[] input = { 2f, -1f, 9f, 0f, 6f, 3.3f, float.NaN };
            float[] expected = { 2f, 0f, 6f, 0f, 6f, 3.3f, 3.3f };
            for (int i = 0; i < input.Length; i++)
            {
                float got = RunSnowRules.ClampMargin(input[i]);
                check(got == expected[i], W, input[i] + " -> " + got + ", expected " + expected[i]);
            }
        }

        private static void EstimatedShareCases(Func<bool, string, string, bool> check)
        {
            const string W = "snow S9 EstimatedShare";
            float v;
            v = RunSnowRules.EstimatedShare(1000f, 1200f, 5f, 0f, 600f, 0.01f);
            check(Near(v, 0.5, 1e-4), W, "line at 1100 m: " + v);
            v = RunSnowRules.EstimatedShare(1000f, 1200f, 5f, 2f, 600f, 0.01f);
            check(v == 1f, W, "line at 900 m: " + v);
            v = RunSnowRules.EstimatedShare(1000f, 1200f, 9f, 0f, 600f, 0.01f);
            check(v == 0f, W, "line at 1500 m: " + v);
            v = RunSnowRules.EstimatedShare(1000f, 1200f, 6.5f, 2f, 600f, 0.01f);
            check(Near(v, 0.75, 1e-4), W, "line at 1050 m: " + v);
            v = RunSnowRules.EstimatedShare(1200f, 1000f, 5f, 0f, 600f, 0.01f);
            check(Near(v, 0.5, 1e-4), W, "swapped heights: " + v);
            v = RunSnowRules.EstimatedShare(1000f, 1000f, 4f, 0f, 600f, 0.01f);
            check(v == 0f, W, "flat run exactly on the line: " + v);
            v = RunSnowRules.EstimatedShare(1000f, 1000f, 3.9f, 0f, 600f, 0.01f);
            check(Near(v, 1.0, 1e-4), W, "flat run 10 m above the line: " + v);
            v = RunSnowRules.EstimatedShare(1000f, 1200f, -1f, 0f, 600f, 0f);
            check(v == 1f, W, "no lapse, cold: " + v);
            v = RunSnowRules.EstimatedShare(1000f, 1200f, 1f, 0f, 600f, 0f);
            check(v == 0f, W, "no lapse, warm: " + v);
            v = RunSnowRules.EstimatedShare(1000f, 1200f, 0f, 0f, 600f, 0f);
            check(v == 1f, W, "no lapse, exactly at the threshold (<=): " + v);
            v = RunSnowRules.EstimatedShare(float.NaN, 1200f, 5f, 0f, 600f, 0.01f);
            check(v == 0f, W, "NaN bottom: " + v);
        }

        // IsSkiable(depthNorm, ownTargetNorm): the 3 ft bar is 0.91 / 8 x 65535 = 7454.53 quanta, so 7455 passes
        private static void IsSkiableCases(Func<bool, string, string, bool> check)
        {
            const string W = "snow S10 IsSkiable";
            check(RunSnowRules.IsSkiable(7455 / 65535f, 0f), W, "91.0 cm, no own target: should be skiable");
            check(!RunSnowRules.IsSkiable(7454 / 65535f, 0f), W, "90.99 cm, no own target: should not be skiable");
            check(!RunSnowRules.IsSkiable(0f, 0f), W, "bare: should not be skiable");
            check(RunSnowRules.IsSkiable(123 / 65535f, 0.001875f), W, "fringe at its 1.5 cm target: should be skiable (E1-S1)");
            check(!RunSnowRules.IsSkiable(122 / 65535f, 0.001875f), W, "fringe one quantum short: should not be skiable");
            check(RunSnowRules.IsSkiable(7455 / 65535f, 0.12f), W, "96 cm gun target, 91.0 cm: the 3 ft bar applies, skiable");
            check(!RunSnowRules.IsSkiable(7454 / 65535f, 0.12f), W, "96 cm gun target, 90.99 cm: the 3 ft bar applies, not skiable");
            check(!RunSnowRules.IsSkiable(0f, 0.5f / 65535f), W, "a sub-quantum target is ignored: bare is not skiable");
        }

        private static void ShareFromSumsCases(Func<bool, string, string, bool> check)
        {
            const string W = "snow S11 ShareFromSums";
            float v;
            v = RunSnowRules.ShareFromSums(3.0, 4.0);  check(v == 0.75f, W, "(3, 4) -> " + v);
            v = RunSnowRules.ShareFromSums(0.0, 0.4);  check(float.IsNaN(v), W, "(0, 0.4) -> " + v + ", expected NaN (too small)");
            v = RunSnowRules.ShareFromSums(0.0, 0.0);  check(float.IsNaN(v), W, "(0, 0) -> " + v + ", expected NaN");
            v = RunSnowRules.ShareFromSums(5.0, 4.0);  check(v == 1f, W, "(5, 4) -> " + v + ", expected 1 (clamped)");
        }

        // StepScale, all exact
        private static void StepScaleCases(Func<bool, string, string, bool> check)
        {
            const string W = "snow S12 StepScale";
            uint[] frames = { 64u, 0u, 32u, 96u, 120u, 240u, 256u, 1000u, 65536u, 100000u };
            float[] expected = { 1f, 1f, 1f, 1.5f, 1.875f, 3.75f, 4f, 15.625f, 1024f, 1024f };
            for (int i = 0; i < frames.Length; i++)
            {
                float got = RunSnowRules.StepScale(frames[i]);
                check(got == expected[i], W, frames[i] + " frames -> " + got + ", expected " + expected[i] +
                      (frames[i] == 64u ? " (the bit-identity case)" : ""));
            }
        }

        private static void CanSkipDryCases(Func<bool, string, string, bool> check)
        {
            const string W = "snow S13 CanSkipDry";
            const ulong Hash = 0x732AD62604202A70UL;
            DryPass baseLast = new DryPass
            {
                Valid = true, ColdTexels = 0, Injected = 0, RunDepthSum = 0.0, ContainedTexels = 120, FootprintHash = Hash,
            };
            DryPass last = baseLast;
            check(RunSnowRules.CanSkipDry(last, 0, 120, Hash, 64u), W, "base case should skip");
            check(RunSnowRules.CanSkipDry(last, 0, 120, Hash, 1023u), W, "1023 frames since the last real pass should skip");
            check(!RunSnowRules.CanSkipDry(last, 0, 120, Hash, 1024u), W, "1024 frames: the forced real pass is due");
            check(!RunSnowRules.CanSkipDry(last, 1, 120, Hash, 64u), W, "one cold texel now: no skip");
            last = baseLast; last.ColdTexels = 3;
            check(!RunSnowRules.CanSkipDry(last, 0, 120, Hash, 64u), W, "the last real pass had cold texels: no skip");
            last = baseLast; last.Injected = 5;
            check(!RunSnowRules.CanSkipDry(last, 0, 120, Hash, 64u), W, "the last real pass injected: no skip");
            last = baseLast; last.RunDepthSum = 1e-9;
            check(!RunSnowRules.CanSkipDry(last, 0, 120, Hash, 64u), W, "snow on the footprint at the last real pass: no skip");
            last = baseLast;
            check(!RunSnowRules.CanSkipDry(last, 0, 121, Hash, 64u), W, "footprint count changed: no skip");
            check(!RunSnowRules.CanSkipDry(last, 0, 120, unchecked(Hash + 1UL), 64u), W, "footprint hash changed: no skip");
            last = baseLast; last.Valid = false;
            check(!RunSnowRules.CanSkipDry(last, 0, 120, Hash, 64u), W, "no real pass this load: no skip");
        }

        // IsForcedDryCheck: the forced check is the frame term alone
        private static void ForcedDryCheckCases(Func<bool, string, string, bool> check)
        {
            const string W = "snow S13b IsForcedDryCheck";
            const ulong Hash = 0x732AD62604202A70UL;
            DryPass baseLast = new DryPass
            {
                Valid = true, ColdTexels = 0, Injected = 0, RunDepthSum = 0.0, ContainedTexels = 120, FootprintHash = Hash,
            };
            DryPass last = baseLast;
            check(RunSnowRules.IsForcedDryCheck(last, 0, 120, Hash, 1024u), W, "1024 frames, all else dry and equal: the forced check");
            check(!RunSnowRules.IsForcedDryCheck(last, 0, 120, Hash, 1023u), W, "1023 frames: not forced (the pass skips)");
            check(!RunSnowRules.IsForcedDryCheck(last, 2, 120, Hash, 1024u), W, "1024 frames and 2 cold texels now: a reason of its own, not forced");
            check(!RunSnowRules.IsForcedDryCheck(last, 0, 121, Hash, 1024u), W, "1024 frames and the footprint count changed: not forced");
            check(!RunSnowRules.IsForcedDryCheck(last, 0, 120, unchecked(Hash + 1UL), 1024u), W, "1024 frames and the footprint hash changed: not forced");
            last = baseLast; last.Valid = false;
            check(!RunSnowRules.IsForcedDryCheck(last, 0, 120, Hash, 5000u), W, "no real pass this load: not forced");
        }

        // Packed skip records and arguments. H = the file's footprint hash. P = a packed record: 478 texels and 0.212 m are a
        // field run's last footprint and run mean. D = a dry record. Arguments (cold, contained, hash, sinceReal, calm). The
        // base is (0, 478, H, 188u, true): 188 sim frames = one 8x pass.
        private const ulong PackedHash = 0x732AD62604202A70UL;

        private static DryPass PackedRecord()
        {
            return new DryPass
            {
                Valid = true, ColdTexels = 0, Injected = 0, RunMeanDepthM = 0.212f,
                RunDepthSum = 12.5, ContainedTexels = 478, FootprintHash = PackedHash,
            };
        }

        private static DryPass DryRecord()
        {
            return new DryPass
            {
                Valid = true, ColdTexels = 0, Injected = 0, RunMeanDepthM = 0f,
                RunDepthSum = 0.0, ContainedTexels = 478, FootprintHash = PackedHash,
            };
        }

        private static void PackedConstants(Func<bool, string, string, bool> check)
        {
            const string W = "snow S17 packed constants";
            check(RunSnowRules.PackedSkipMinRunDepthM == 0.10f &&
                  RunSnowRules.CalmOpenShare == 0.70f && RunSnowRules.CalmClosedShare == 0.30f,
                W, "a Round M2 constant moved: the literal oracles below assume dress guard 0.10 m, " +
                   "calm shares 70 % / 30 %");
            check((byte)TopupSkip.None == 0 && (byte)TopupSkip.Dry == 1 && (byte)TopupSkip.Packed == 2,
                W, "TopupSkip literals changed: expected None 0, Dry 1, Packed 2");
        }

        private static void CalmShareCases(Func<bool, string, string, bool> check)
        {
            const string W = "snow S19 IsCalmShare";
            float[] shares = { 0.70f, 0.6999f, 0.5f, 0.30f, 0.2999f, 1f, 0f, float.NaN };
            bool[] expected = { true, false, false, false, true, true, true, false };
            for (int i = 0; i < shares.Length; i++)
            {
                bool got = RunSnowRules.IsCalmShare(shares[i]);
                check(got == expected[i], W, shares[i] + " -> " + got + ", expected " + expected[i]);
            }
        }

        private static void CanSkipPackedCases(Func<bool, string, string, bool> check)
        {
            const string W = "snow S20 CanSkipPacked";
            const ulong H = PackedHash;
            DryPass p = PackedRecord();
            Packed(check, W, p, 0, 478, H, 188u, true, true, "base case should skip");
            Packed(check, W, p, 0, 478, H, 1023u, true, true, "1023 frames since the last real pass should skip");
            Packed(check, W, p, 0, 478, H, 1024u, true, false, "1024 frames: the forced real pass is due");
            Packed(check, W, p, 1, 478, H, 188u, true, false, "one cold texel now: no skip");
            p = PackedRecord(); p.ColdTexels = 2;
            Packed(check, W, p, 0, 478, H, 188u, true, false, "the last real pass had cold texels (the gate-change pass is real): no skip");
            p = PackedRecord(); p.Injected = 1;
            Packed(check, W, p, 0, 478, H, 188u, true, false, "the last real pass raised a texel: no skip");
            p = PackedRecord(); p.RunMeanDepthM = 0.10f;
            Packed(check, W, p, 0, 478, H, 188u, true, true, "runs at exactly 0.10 m should skip (inclusive)");
            p = PackedRecord(); p.RunMeanDepthM = 0.0999f;
            Packed(check, W, p, 0, 478, H, 188u, true, false, "runs at 0.0999 m (under the dress guard): no skip");
            p = PackedRecord(); p.RunMeanDepthM = float.NaN;
            Packed(check, W, p, 0, 478, H, 188u, true, false, "runs at NaN m: no skip");
            p = PackedRecord();
            Packed(check, W, p, 0, 478, H, 188u, false, false, "a measured share near the open line: no skip");
            Packed(check, W, p, 0, 479, H, 188u, true, false, "footprint count changed: no skip");
            Packed(check, W, p, 0, 478, unchecked(H + 1UL), 188u, true, false, "footprint hash changed: no skip");
            p = PackedRecord(); p.ContainedTexels = 0;
            Packed(check, W, p, 0, 0, H, 188u, true, false, "an empty footprint (0 = 0): no skip");
            p = PackedRecord(); p.Valid = false;
            Packed(check, W, p, 0, 478, H, 188u, true, false, "no real pass this load: no skip");
            p = PackedRecord();
            check(!RunSnowRules.CanSkipDry(p, 0, 478, H, 188u), W, "CanSkipDry on the packed record should be false (snow lies: not dry)");
        }

        private static void Packed(Func<bool, string, string, bool> check, string w, DryPass last, int cold, int contained,
            ulong hash, uint since, bool calm, bool expected, string what)
        {
            bool got = RunSnowRules.CanSkipPacked(last, cold, contained, hash, since, calm);
            check(got == expected, w, what + " (got " + got + ")");
        }

        // Row 3 (a dry record with RunMeanDepthM 0.212, dry first) is a record on which both predicates hold on
        // that synthetic record. The test then really checks the order (with 0 m only Dry holds)
        private static void SkipKindCases(Func<bool, string, string, bool> check)
        {
            const string W = "snow S21 SkipKind";
            const ulong H = PackedHash;
            DryPass d = DryRecord();
            Kind(check, W, d, 0, 478, H, 188u, true, TopupSkip.Dry, "dry record, base");
            Kind(check, W, d, 0, 478, H, 188u, false, TopupSkip.Dry, "dry record, a share near the open line (D30 ignores the share guard)");
            d = DryRecord(); d.RunMeanDepthM = 0.212f;
            Kind(check, W, d, 0, 478, H, 188u, true, TopupSkip.Dry, "dry record with 0.212 m (both hold: dry first)");
            DryPass p = PackedRecord();
            Kind(check, W, p, 0, 478, H, 188u, true, TopupSkip.Packed, "packed record, base");
            Kind(check, W, p, 1, 478, H, 188u, true, TopupSkip.None, "packed record, one cold texel now");
            Kind(check, W, p, 0, 478, H, 1024u, true, TopupSkip.None, "packed record, 1024 frames (forced pass due)");
            Kind(check, W, default(DryPass), 0, 478, H, 188u, true, TopupSkip.None, "no real pass this load (default record)");
        }

        private static void Kind(Func<bool, string, string, bool> check, string w, DryPass last, int cold, int contained,
            ulong hash, uint since, bool calm, TopupSkip expected, string what)
        {
            TopupSkip got = RunSnowRules.SkipKind(last, cold, contained, hash, since, calm);
            check(got == expected, w, what + ": " + got + ", expected " + expected);
        }

        // IsForcedCheck: the 1,024-frame term is the only failing term
        private static void ForcedCheckCases(Func<bool, string, string, bool> check)
        {
            const string W = "snow S22 IsForcedCheck";
            const ulong H = PackedHash;
            DryPass p = PackedRecord();
            check(RunSnowRules.IsForcedCheck(p, 0, 478, H, 1024u, true), W, "packed record at 1024 frames, all else equal: the forced check");
            check(!RunSnowRules.IsForcedCheck(p, 0, 478, H, 1023u, true), W, "1023 frames: not forced (the pass skips)");
            check(!RunSnowRules.IsForcedCheck(p, 2, 478, H, 1024u, true), W, "1024 frames and 2 cold texels now: a reason of its own, not forced");
            check(!RunSnowRules.IsForcedCheck(p, 0, 478, unchecked(H + 1UL), 1024u, true), W, "1024 frames and the footprint hash changed: not forced");
            check(!RunSnowRules.IsForcedCheck(p, 0, 478, H, 1024u, false), W, "1024 frames and a share near the open line: a reason of its own, not forced");
            DryPass d = DryRecord();
            bool forcedDry = RunSnowRules.IsForcedCheck(d, 0, 478, H, 1024u, false);
            check(forcedDry && forcedDry == RunSnowRules.IsForcedDryCheck(d, 0, 478, H, 1024u), W,
                "dry record at 1024 frames, calm false: expected true and equal to IsForcedDryCheck (got " + forcedDry + ")");
            p = PackedRecord(); p.Valid = false;
            check(!RunSnowRules.IsForcedCheck(p, 0, 478, H, 5000u, true), W, "no real pass this load: not forced");
        }

        // Properties over seed 20261010, 20,000 records: one check per property, and the test counts violations (the oracle is 0).
        // The draw order is the same for every record. Each value is tables[rng.Next(length)]. The order is: Valid, ColdTexels, Injected,
        // RunDepthSum, RunMeanDepthM (drawn only when RunDepthSum != 0, else 0f), then the arguments cold, contained, hash,
        // sinceReal, calm. The record's footprint is 478 texels / H. 20,000 records give 18 packed and 103 dry for this seed
        // (2,000 give one packed record). Each check also requires that the draw includes both kinds (no vacuous pass).
        private static void PackedProperties(Func<bool, string, string, bool> check)
        {
            const string W = "snow S23 packed properties";
            const int Records = 20000;
            var rng = new Random(20261010);
            bool[] validT = { true, false };
            int[] coldT = { 0, 0, 0, 3 };
            int[] injectedT = { 0, 0, 2 };
            double[] depthSumT = { 0.0, 1e-9, 12.5 };
            float[] meanT = { 0.05f, 0.0999f, 0.10f, 0.212f, float.NaN };
            int[] coldNowT = { 0, 0, 1 };
            int[] containedT = { 0, 478, 479 };
            ulong[] hashT = { PackedHash, unchecked(PackedHash + 1UL) };
            uint[] sinceT = { 0u, 188u, 1023u, 1024u, 5000u };
            bool[] calmT = { true, false };
            int bad1 = 0, bad2 = 0, packed = 0, dry = 0;
            string first1 = null, first2 = null;
            for (int n = 0; n < Records; n++)
            {
                DryPass last = default;
                last.Valid = validT[rng.Next(validT.Length)];
                last.ColdTexels = coldT[rng.Next(coldT.Length)];
                last.Injected = injectedT[rng.Next(injectedT.Length)];
                last.RunDepthSum = depthSumT[rng.Next(depthSumT.Length)];
                last.RunMeanDepthM = last.RunDepthSum == 0.0 ? 0f : meanT[rng.Next(meanT.Length)];
                last.ContainedTexels = 478;
                last.FootprintHash = PackedHash;
                int cold = coldNowT[rng.Next(coldNowT.Length)];
                int contained = containedT[rng.Next(containedT.Length)];
                ulong hash = hashT[rng.Next(hashT.Length)];
                uint since = sinceT[rng.Next(sinceT.Length)];
                bool calm = calmT[rng.Next(calmT.Length)];

                bool canPacked = RunSnowRules.CanSkipPacked(last, cold, contained, hash, since, calm);
                bool canDry = RunSnowRules.CanSkipDry(last, cold, contained, hash, since);
                TopupSkip kind = RunSnowRules.SkipKind(last, cold, contained, hash, since, calm);
                if (canPacked)
                    packed++;
                if (canDry)
                    dry++;
                // A packed skip implies the pass is not due. It also implies the terms that make the merge a no-op. These terms are: no cold texel now
                // or at the last real pass and nothing raised (T222: the merge never lowers; the game melts)
                if (canPacked && !(cold == 0 && last.ColdTexels == 0 && last.Injected == 0 && since < RunSnowRules.ForcedRealPassFrames))
                {
                    if (bad1++ == 0)
                        first1 = Describe(last, cold, contained, hash, since, calm);
                }
                // Never both skips on these records. SkipKind is Dry exactly when CanSkipDry, and Packed exactly when CanSkipPacked
                if ((canDry && canPacked) || (kind == TopupSkip.Dry) != canDry || (kind == TopupSkip.Packed) != canPacked)
                {
                    if (bad2++ == 0)
                        first2 = Describe(last, cold, contained, hash, since, calm) + " -> " + kind;
                }
            }
            check(bad1 == 0 && packed > 0, W, "P1 CanSkipPacked implies no cold texel now or at the last real pass, nothing " +
                  "raised, not due: " + bad1 + " violation(s) in " + Records + " records (" +
                  packed + " packed, expected > 0)" + (first1 != null ? "; first: " + first1 : ""));
            check(bad2 == 0 && packed > 0 && dry > 0, W, "P2 never both kinds, SkipKind = Dry iff CanSkipDry, = Packed iff " +
                  "CanSkipPacked: " + bad2 + " violation(s) in " + Records + " records (" + packed + " packed, " + dry +
                  " dry, both expected > 0)" + (first2 != null ? "; first: " + first2 : ""));
        }

        private static string Describe(DryPass last, int cold, int contained, ulong hash, uint since, bool calm)
        {
            return "record (valid " + last.Valid + ", cold " + last.ColdTexels + ", injected " + last.Injected +
                   ", sum " +
                   last.RunDepthSum.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ", mean " +
                   last.RunMeanDepthM.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "), now (cold " +
                   cold + ", contained " + contained + ", hash " + (hash == PackedHash ? "H" : "H+1") + ", since " + since +
                   ", calm " + calm + ")";
        }

        private static void Mix64Cases(Func<bool, string, string, bool> check)
        {
            const string W = "snow S14 Mix64";
            ulong v;
            v = RunSnowRules.Mix64(0UL);
            check(v == 0xE220A8397B1DCDAFUL, W, "Mix64(0) = 0x" + v.ToString("X16"));
            v = RunSnowRules.Mix64(1UL);
            check(v == 0x910A2DEC89025CC1UL, W, "Mix64(1) = 0x" + v.ToString("X16"));
            v = RunSnowRules.Mix64(1048575UL);
            check(v == 0xBB8EA5903D3CF26CUL, W, "Mix64(1048575) (the last texel) = 0x" + v.ToString("X16"));
            v = unchecked(RunSnowRules.Mix64(0UL) + RunSnowRules.Mix64(1UL));
            check(v == 0x732AD62604202A70UL, W, "Mix64(0) + Mix64(1) = 0x" + v.ToString("X16"));
            v = unchecked(RunSnowRules.Mix64(2UL) + RunSnowRules.Mix64(1UL) + RunSnowRules.Mix64(0UL));
            check(v == 0x0A830C0420B7813EUL, W, "Mix64(2) + Mix64(1) + Mix64(0) (order does not matter) = 0x" + v.ToString("X16"));
        }

        private static void HourAlignedDateCases(Func<bool, string, string, bool> check)
        {
            const string W = "snow S15 HourAlignedDate";
            int key;
            float d;
            d = RunSnowRules.HourAlignedDate(100.4f / 288f, 12, out key);
            check(key == 100 && Near(d, 100.0 / 288.0, 1e-6), W, "(100.4/288, 12) -> key " + key + ", date " + d);
            d = RunSnowRules.HourAlignedDate(0f, 12, out key);
            check(key == 0 && d == 0f, W, "(0, 12) -> key " + key + ", date " + d);
            d = RunSnowRules.HourAlignedDate(0.999999f, 12, out key);
            check(key == 287, W, "(0.999999, 12) -> key " + key + ", date " + d);
            d = RunSnowRules.HourAlignedDate(0.5f, 0, out key);
            check(key == 12 && d == 0.5f, W, "(0.5, days 0 counts as 1) -> key " + key + ", date " + d);
        }

        private static void DayMeanCases(Func<bool, string, string, bool> check)
        {
            const string W = "snow S16 DayMeanC";
            float v;
            v = RunSnowRules.DayMeanC(t => 7.25f, 0.3f, 12);
            check(v == 7.25f, W, "constant 7.25 -> " + v);
            v = RunSnowRules.DayMeanC(t => 288f * t, 100f / 288f, 12);
            check(Near(v, 88.5, 1e-3), W, "hours 100..77 -> " + v + ", expected 88.5");
            v = RunSnowRules.DayMeanC(t => 288f * t, 5f / 288f, 12);
            check(Near(v, 209.5, 1e-3), W, "hours 5..0 then 287..270 across the year start -> " + v + ", expected 209.5");
            v = RunSnowRules.DayMeanC(t => 24f * t, 0.5f, 1);
            check(Near(v, 11.5, 1e-3), W, "one day a year -> " + v + ", expected 11.5");
            v = RunSnowRules.DayMeanC(t => 24f * t, 0.5f, 0);
            check(Near(v, 11.5, 1e-3), W, "days 0 counts as 1 -> " + v + ", expected 11.5");
        }

        // Snow-rule scenarios with hand-value oracles:
        // - constants
        // - the skiable bar per run
        // - the gun reaching "open" by itself
        // - the pack step (T222: no hold, no aspect, spring or budget)
        // - the ledger

        private static void T09Cases(Func<bool, string, string, bool> check)
        {
            T09Constants(check);
            T09SkiableBar(check);
            T09GunOpens(check);
            T222PackStep(check);
            T269Seed(check);
            T09Ledger(check);
        }

        private static void T09Constants(Func<bool, string, string, bool> check)
        {
            const string W = "snow S24 T09 constants";
            check(RunSnowRules.SkiableDepthM == 0.91f && RunSnowRules.ExpertNaturalSkiableDepthM == 0.91f, W, "T133 one bar, 3 ft (0.91 m), for every run");
            check(RunSnowRules.ExpertBandMin == SkiResortScore.BandBlack && SkiResortScore.BandDoubleBlack == RunSnowRules.ExpertBandMin + 1,
                W, "ExpertBandMin tracks SkiResortScore.BandBlack (black and double black)");
            check(RunSnowRules.OpenAtShare == 0.60f && RunSnowRules.CloseBelowShare == 0.40f, W, "the 60 % / 40 % hysteresis is unchanged");
            check(RunSnowRules.DefaultSnowmakingMarginC == 3.3f && RunSnowRules.MinSnowmakingMarginC == 0f && RunSnowRules.MaxSnowmakingMarginC == 6f,
                W, "margin default 3.3 C (38 F), slider range 0..6");
            check(RunSnowRules.GunTargetDepthM == RunSnowRules.SkiableDepthM && Near(RunSnowRules.GroomedTargetDepthM, 0.96, 1e-6) &&
                  RunSnowRules.GunStopDepthM == 2f && RunSnowRules.GunRestartBandM == 0.05f,
                W, "T222 gun base 2.0 m, restart 1.95 m; groomed ceiling 0.96 m (the 3 ft bar + the band)");
            check(RunSnowRules.GunTargetDepthM >= RunSnowRules.SkiableDepthM, W, "the gun target reaches the open bar");
            check(RunSnowRules.GroomedTargetDepthM > RunSnowRules.SkiableDepthM, W, "the groomed target clears the open bar");
            check(RunSnowRules.MeltFloorPerHourM > 0f && RunSnowRules.MeltDepthCoef > 0f && RunSnowRules.MaxPackStepHours == 6f, W, "a measured melt, a 6 h step cap");
        }

        private static void T09SkiableBar(Func<bool, string, string, bool> check)
        {
            const string W = "snow S25 skiable bar";
            // Every run opens at 3 ft (0.91 m), made or natural, every rating
            float bar = 0.91f / 8f;
            check(RunSnowRules.SkiableBarNorm(RunKind.Natural, 3) == bar, W, "T133 natural black needs 3 ft");
            check(RunSnowRules.SkiableBarNorm(RunKind.Natural, 4) == bar, W, "T133 natural double black needs 3 ft");
            check(RunSnowRules.SkiableBarNorm(RunKind.Natural, 2) == bar, W, "T133 natural blue needs 3 ft");
            check(RunSnowRules.SkiableBarNorm(RunKind.Natural, 1) == bar, W, "T133 natural green needs 3 ft");
            check(RunSnowRules.SkiableBarNorm(RunKind.Natural, 0) == bar, W, "T133 natural flat needs 3 ft");
            check(RunSnowRules.SkiableBarNorm(RunKind.Natural, -1) == bar, W, "T133 an unrated natural run needs 3 ft");
            check(RunSnowRules.SkiableBarNorm(RunKind.Groomed, 4) == bar, W, "T133 a groomed double black needs 3 ft");
            check(RunSnowRules.SkiableBarNorm(RunKind.Snowmaking, 3) == bar, W, "T133 a snowmaking black needs 3 ft");
            check(RunSnowRules.SkiableBarNorm(RunKind.Night, 4) == bar, W, "T133 a night double black needs 3 ft");
            check(RunSnowRules.SkiableBarNorm(RunKind.Natural, 3) == RunSnowRules.ExpertNaturalSkiableNorm, W, "the expert bar is ExpertNaturalSkiableNorm");
            float black = RunSnowRules.SkiableBarNorm(RunKind.Natural, 3);
            check(!RunSnowRules.IsSkiable(0.90f / 8f, 0f, black), W, "T133 90 cm: not skiable");
            check(RunSnowRules.IsSkiable(0.91f / 8f, 0f, black), W, "T133 91 cm: skiable");
            check(!RunSnowRules.IsSkiable(0.31f / 8f, 0f, black), W, "T133 the old 31 cm bar: not skiable any more");
            check(!RunSnowRules.IsSkiable(0.15f / 8f, 0f, RunSnowRules.SkiableBarNorm(RunKind.Natural, 2)), W, "T133 the old 15 cm bar: not skiable any more");
            check(RunSnowRules.IsSkiable(1.20f / 8f, 0f, black), W, "T133 1.2 m: skiable");
        }

        // The gun reaches "open" by itself: windows of 64 frames at step scale 1, the slowest texel has rate noise at its minimum
        private static void T09GunOpens(Func<bool, string, string, bool> check)
        {
            const string W = "snow S26 gun opens";
            float thr = RunSnowRules.DefaultSnowmakingMarginC;   // freezing 0 + 3.3
            float slow = RunSnowRules.RampRateMin;
            float mean = RunSnowRules.RampRateMin + RunSnowRules.RampRateSpan * 0.5f;
            // The gun target is the 3 ft open bar (0.91 m). -3 C: chill (3.3 + 3) / 4 > 1 -> output 2.0. Slowest step 0.002 m.
            float w91 = RunSnowRules.GunWindowsToReach(RunSnowRules.GunTargetDepthM, -3f, thr, slow);
            check(Near(w91, 455.0, 1.0), W, "T133 slowest texel to 91 cm at -3 C: " + w91 + " windows, expected 455");
            check(RunSnowRules.WindowsToGameHours(w91) < 3f, W, "T133 slowest texel opens (3 ft) within 3 game h at -3 C");
            check(RunSnowRules.WindowsToGameHours(w91) <= 8f, W, "inside one night (8 game h) at -3 C");
            float mean91 = RunSnowRules.GunWindowsToReach(RunSnowRules.GunTargetDepthM, -3f, thr, mean);
            check(mean91 < w91 && Near(mean91, 0.91 / (0.004 * 1.125 * 2.0), 0.5), W, "the mean texel is faster: " + mean91);
            float marg = RunSnowRules.GunWindowsToReach(RunSnowRules.GunTargetDepthM, thr, thr, mean);
            check(RunSnowRules.WindowsToGameHours(marg) <= 8f, W, "T133 even at the gate (marginal output) the mean texel reaches 3 ft within a night: " +
                  RunSnowRules.WindowsToGameHours(marg) + " h");
            check(RunSnowRules.WindowsToGameHours(RunSnowRules.GunWindowsToReach(RunSnowRules.SkiableDepthM, thr, thr, slow)) <= 24f, W,
                  "T133 and the slowest texel at the gate within one game day");
            check(float.IsPositiveInfinity(RunSnowRules.GunWindowsToReach(0.35f, thr + 0.1f, thr, slow)), W, "no gun above the gate");
            check(Near(RunSnowRules.WindowsToGameHours(4096f), 24.0, 1e-3), W, "4096 windows of 64 frames are one game day");
        }

        // T222 (DECISIONS 667): the one pack step. Hand values at the default parameters (base 2.0 m, restart 1.95 m, margin 3.3 C).
        private static void T222PackStep(Func<bool, string, string, bool> check)
        {
            const string W = "snow S27 T222 pack step";
            RunSnowRules.PackParams p = RunSnowRules.PackParams.Default;
            float full = RunSnowRules.PackBuildPerHourM(-10f, 3.3f, RunSnowRules.MeanRampRate);
            check(Near(full, 0.004 * 1.125 * 2.0 * RunSnowRules.WindowsPerGameHour, 1e-4) &&
                  Near(RunSnowRules.PackBuildPerHourM(3.3f, 3.3f, RunSnowRules.MeanRampRate), 0.004 * 1.125 * 0.35 * RunSnowRules.WindowsPerGameHour, 1e-4) &&
                  RunSnowRules.PackBuildPerHourM(3.4f, 3.3f, 1f) == 0f && RunSnowRules.PackBuildPerHourM(float.NaN, 3.3f, 1f) == 0f, W,
                  "the build per hour is the ramp per window x the windows per hour; none above the gate or for NaN");
            check(Near(RunSnowRules.PackMeltPerHourM(p, 10f, 2f), RunSnowRules.MeltFloorPerHourM, 1e-6) &&
                  Near(RunSnowRules.PackMeltPerHourM(p, 10f, 0.25f), 10.0 * RunSnowRules.MeltDepthCoef / 0.25, 1e-5) && RunSnowRules.PackMeltPerHourM(p, 0f, 1f) == 0f &&
                  RunSnowRules.PackMeltPerHourM(p, -5f, 1f) == 0f && RunSnowRules.PackMeltPerHourM(p, float.NaN, 1f) == 0f, W,
                  "the melt: the floor for deep snow, coef x air / depth for thin, none at or below freezing or for NaN");
            // The hysteresis
            check(RunSnowRules.NextPackGun(p, RunSnowRules.PackGun.Unknown, 2f) == RunSnowRules.PackGun.Stopped &&
                  RunSnowRules.NextPackGun(p, RunSnowRules.PackGun.Running, 2f) == RunSnowRules.PackGun.Stopped &&
                  RunSnowRules.NextPackGun(p, RunSnowRules.PackGun.Running, 1.97f) == RunSnowRules.PackGun.Running &&
                  RunSnowRules.NextPackGun(p, RunSnowRules.PackGun.Stopped, 1.97f) == RunSnowRules.PackGun.Stopped &&
                  RunSnowRules.NextPackGun(p, RunSnowRules.PackGun.Unknown, 1.97f) == RunSnowRules.PackGun.Stopped &&
                  RunSnowRules.NextPackGun(p, RunSnowRules.PackGun.Stopped, 1.94f) == RunSnowRules.PackGun.Running &&
                  RunSnowRules.NextPackGun(p, RunSnowRules.PackGun.Stopped, float.NaN) == RunSnowRules.PackGun.Stopped, W,
                  "stop at 2.0 m, restart below 1.95 m, kept in the band, Unknown in the band reads Stopped, NaN keeps the state");
            check(RunSnowRules.PackGunAfterLoad(p, 1.5f) == RunSnowRules.PackGun.Running && RunSnowRules.PackGunAfterLoad(p, 1.97f) == RunSnowRules.PackGun.Stopped,
                  W, "T223 the gun state after a load follows the saved depth alone");
            // The step: the melt comes off, then the build goes on, never above the stop depth, never lowering deeper snow
            RunSnowRules.PackResult r = RunSnowRules.PackStep(p, 1f, -5f, true, RunSnowRules.PackGun.Running, 0.25f, RunSnowRules.MeanRampRate, true);
            check(Near(r.DepthM, 1.0 + 0.25 * full, 1e-4) && r.GunsMaking && r.MeltedM == 0f, W, "-5 C, 0.25 h: built " + r.BuiltM + " m");
            r = RunSnowRules.PackStep(p, 1.9f, -5f, true, RunSnowRules.PackGun.Running, 1f, RunSnowRules.MeanRampRate, true);
            check(r.DepthM == 2f && Near(r.BuiltM, 0.1, 1e-5), W, "the build stops at the 2.0 m base");
            r = RunSnowRules.PackStep(p, 3f, -5f, true, RunSnowRules.PackGun.Running, 1f, RunSnowRules.MeanRampRate, true);
            check(r.DepthM == 3f && !r.GunsMaking, W, "deeper natural snow is never lowered");
            r = RunSnowRules.PackStep(p, 1f, 5f, true, RunSnowRules.PackGun.Running, 0.5f, RunSnowRules.MeanRampRate, true);
            check(Near(r.DepthM, 1.0 - 0.5 * RunSnowRules.MeltFloorPerHourM, 1e-5) && !r.GunsMaking, W, "5 C: no build, the melt only (a track cell; 1 m is on the floor)");
            r = RunSnowRules.PackStep(p, 1f, 5f, true, RunSnowRules.PackGun.Running, 0.5f, RunSnowRules.MeanRampRate, false);
            check(r.DepthM == 1f && r.MeltedM == 0f, W, "a run texel (applyMelt false): the game melts it, the step does not");
            r = RunSnowRules.PackStep(p, 1f, -5f, true, RunSnowRules.PackGun.Stopped, 1f, RunSnowRules.MeanRampRate, true);
            check(r.DepthM == 1f && !r.GunsMaking, W, "stopped guns build nothing");
            r = RunSnowRules.PackStep(p, 1f, -5f, false, RunSnowRules.PackGun.Running, 1f, RunSnowRules.MeanRampRate, true);
            check(r.DepthM == 1f, W, "guns not available (season, switch): nothing");
            r = RunSnowRules.PackStep(p, 1f, -5f, true, RunSnowRules.PackGun.Running, 100f, RunSnowRules.MeanRampRate, true);
            check(r.DepthM == 2f, W, "a long step is capped (MaxPackStepHours) and still stops at the base");
            r = RunSnowRules.PackStep(p, float.NaN, 5f, true, RunSnowRules.PackGun.Running, 1f, RunSnowRules.MeanRampRate, true);
            check(r.DepthM == 0f, W, "NaN reads bare");
            // The run path in the merge: PackRaiseTexelM is the ramp step it replaced (RampPerTopupM x rate x output x step scale)
            float raised = RunSnowRules.PackRaiseTexelM(0.5f, 2f, 1f, 1.5f, 1f);
            check(Near(raised, 0.5 + 0.004 * 1.5 * 2.0, 1e-5), W, "PackRaiseTexelM at full chill: one window of the ramp (" + raised + ")");
            float marginal = RunSnowRules.PackRaiseTexelM(0.5f, 2f, 0f, 1f, 2f);
            check(Near(marginal, 0.5 + 0.004 * 0.35 * 2.0, 1e-5), W, "PackRaiseTexelM at the gate, step scale 2 (" + marginal + ")");
            check(Near(RunSnowRules.PackRaiseTexelM(0.95f, 0.96f, 1f, 2f, 4f), 0.96, 1e-6) && RunSnowRules.PackRaiseTexelM(1.2f, 0.96f, 1f, 2f, 4f) == 1.2f, W,
                  "PackRaiseTexelM stops at the texel's own target and never lowers");
            check(RunSnowRules.PackGunsMaking(p, RunSnowRules.PackGun.Running, 3.3f, true) && !RunSnowRules.PackGunsMaking(p, RunSnowRules.PackGun.Running, 3.4f, true) &&
                  !RunSnowRules.PackGunsMaking(p, RunSnowRules.PackGun.Stopped, -5f, true) && !RunSnowRules.PackGunsMaking(p, RunSnowRules.PackGun.Running, -5f, false),
                  W, "PackGunsMaking: running, available and at or under the gate");
        }

        /// <summary>T269: the seed. The first gun raise of a bare texel above freezing writes the threshold d* plus one window of build, so the
        /// pack grows where the build beats the melt (measured in game: thin made snow above 0 C was gone within one window).</summary>
        private static void T269Seed(Func<bool, string, string, bool> check)
        {
            const string W = "snow T269 seed";
            RunSnowRules.PackParams p = RunSnowRules.PackParams.Default;   // freezing 0, margin 3.3
            float rate = RunSnowRules.MeanRampRate;
            float window = 1f / RunSnowRules.WindowsPerGameHour;
            // the build by hand: 0.004 m x rate x (0.35 + 1.65 x chill) per window, chill = (3.3 - air) / 4
            Func<double, double> build = air => 0.004 * 1.125 * (0.35 + 1.65 * Math.Min(1.0, Math.Max(0.0, (3.3 - air) / 4.0))) * RunSnowRules.WindowsPerGameHour;
            double[] airs = { 0.7, 2.0, 2.8 };
            double[] seeds = { 0.0336, 0.1289, 0.2811 };   // d* + one window at the mean rate, by hand (PLAN table)
            for (int k = 0; k < airs.Length; k++)
            {
                float air = (float)airs[k];
                double b = build(air);
                double dStar = 0.0425 * air / b;
                float t = RunSnowRules.PackSeedThresholdM(p, air, rate);
                check(Near(t, dStar, 1e-4), W, "the threshold d* = coef x above / B at +" + air + " C (" + t + " vs " + dStar.ToString("0.0000") + ")");
                float seeded = RunSnowRules.PackStep(p, 0f, air, true, RunSnowRules.PackGun.Running, window, rate, false).DepthM;
                check(Near(seeded, dStar + b * window, 1e-4) && Near(seeded, seeds[k], 5e-4), W,
                      "a bare texel at +" + air + " C seeds to d* plus one window (" + seeded + " m, by hand " + seeds[k] + ")");
                float run = RunSnowRules.PackRaiseTexelM(0f, 2f, (3.3f - air) / RunSnowRules.RampChillSpanC, rate, 1f, 3.3f);
                check(Near(run, seeded, 1e-5), W, "the run path (PackRaiseTexelM with the margin) seeds the same at +" + air + " C (" + run + ")");
                check(RunSnowRules.PackRaiseTexelM(0f, 2f, (3.3f - air) / RunSnowRules.RampChillSpanC, rate, 1f) < 0.01f, W,
                      "margin 0 (a groomed run, the default) never seeds at +" + air + " C");
                float thin = RunSnowRules.PackStep(p, 0.01f, air, true, RunSnowRules.PackGun.Running, window, rate, false).DepthM;
                check(Near(thin, seeded, 1e-5), W, "a thin texel under d* lifts to the same seed at +" + air + " C");
                float deep = RunSnowRules.PackStep(p, 0.5f, air, true, RunSnowRules.PackGun.Running, window, rate, false).DepthM;
                check(Near(deep, 0.5 + b * window, 1e-5), W, "a texel deeper than d* only gets its build at +" + air + " C");
                check(RunSnowRules.PackStep(p, 0f, air, true, RunSnowRules.PackGun.Stopped, window, rate, false).DepthM == 0f &&
                      RunSnowRules.PackStep(p, 0f, air, false, RunSnowRules.PackGun.Running, window, rate, false).DepthM == 0f, W,
                      "stopped guns or no guns: no seed at +" + air + " C");
            }
            // past +3.1 C the build never beats the melt floor: no seed, the step is the plain build
            foreach (float air in new[] { 3.1f, 3.2f, 3.3f, 3.5f, 10f })
            {
                float d = RunSnowRules.PackStep(p, 0f, air, true, RunSnowRules.PackGun.Running, window, rate, false).DepthM;
                check(RunSnowRules.PackSeedThresholdM(p, air, rate) == 0f && Near(d, air <= 3.3f ? build(air) * window : 0.0, 1e-6), W,
                      "no seed at +" + air + " C (" + d + " m)");
            }
            // at or below freezing: no change, the step is the plain build
            foreach (float air in new[] { 0f, -0.5f, -2f, -10f, float.NaN })
            {
                float d = RunSnowRules.PackStep(p, 0f, air, true, RunSnowRules.PackGun.Running, window, rate, false).DepthM;
                check(RunSnowRules.PackSeedThresholdM(p, air, rate) == 0f && (float.IsNaN(air) ? d == 0f : Near(d, build(air) * window, 1e-6)), W,
                      "no seed at " + air + " C (" + d + " m)");
                float run = RunSnowRules.PackRaiseTexelM(0f, 2f, (3.3f - (float.IsNaN(air) ? 0f : air)) / RunSnowRules.RampChillSpanC, rate, 1f, 3.3f);
                if (!float.IsNaN(air))
                    check(Near(run, build(air) * window, 1e-6), W, "the run path at " + air + " C: the plain build, the chill above 1 clamped (" + run + ")");
            }
            // the bound (critic): the seed peaks just under the floor line, and d* < coef x margin / floor for every margin and rate
            double worst = 0.0;
            bool zeroAtMarginZero = true;
            for (float margin = 0f; margin <= RunSnowRules.MaxSnowmakingMarginC + 1e-3f; margin += 0.25f)
            {
                RunSnowRules.PackParams pm = RunSnowRules.PackParams.With(RunSnowRules.GunStopDepthM, margin);
                double bound = RunSnowRules.MeltDepthCoef * margin / RunSnowRules.MeltFloorPerHourM;
                for (float r = 0.1f; r <= 4f; r += 0.05f)
                    for (float air = 0f; air <= margin + 0.5f; air += 0.01f)
                    {
                        float t = RunSnowRules.PackSeedThresholdM(pm, air, r);
                        if (margin == 0f && t != 0f)
                            zeroAtMarginZero = false;
                        if (t - bound > worst)
                            worst = t - bound;
                    }
            }
            float peak = RunSnowRules.PackSeedThresholdM(p, 3.09f, rate);
            check(zeroAtMarginZero && worst <= 1e-6 && RunSnowRules.MeltDepthCoef * RunSnowRules.MaxSnowmakingMarginC / RunSnowRules.MeltFloorPerHourM < 0.8, W,
                  "the seed is 0 at margin 0 and under coef x margin / floor at every margin and rate (0.77 m at margin 6)");
            check(peak > 0.38f && peak < 0.40f && RunSnowRules.PackSeedThresholdM(p, 3.11f, rate) == 0f, W,
                  "at margin 3.3 the seed peaks just under the floor line: " + peak.ToString("0.000") + " m at +3.09 C, 0 at +3.11 C");
            // the seed never passes the texel's own target
            check(Near(RunSnowRules.PackRaiseTexelM(0f, 0.05f, (3.3f - 2.8f) / RunSnowRules.RampChillSpanC, rate, 1f, 3.3f), 0.05, 1e-6), W,
                  "the seed stops at the texel's own target (a fringe partial)");
            // The point of it: game melt every 4 frames, a raise every 64. Bare at +1 C and +2 C, the seeded texel grows; without it, it stays bare.
            foreach (float air in new[] { 1f, 2f })
            {
                float chill = (3.3f - air) / RunSnowRules.RampChillSpanC;
                float with = 0f, without = 0f;
                float sub = RunSnowRules.SnowUpdateIntervalFrames / RunSnowRules.FramesPerGameHour;
                int windows = (int)RunSnowRules.WindowsPerGameHour;   // one game hour
                for (int w = 0; w < windows; w++)
                {
                    with = RunSnowRules.PackRaiseTexelM(with, 2f, chill, rate, 1f, 3.3f);
                    without = RunSnowRules.PackRaiseTexelM(without, 2f, chill, rate, 1f);
                    for (int s = 0; s < RunSnowRules.TopupIntervalFrames / RunSnowRules.SnowUpdateIntervalFrames; s++)
                    {
                        with = Math.Max(0f, with - RunSnowRules.PackMeltM(p, with, air, sub));
                        without = Math.Max(0f, without - RunSnowRules.PackMeltM(p, without, air, sub));
                    }
                }
                float seed = RunSnowRules.PackSeedThresholdM(p, air, rate) + (float)build(air) * window;
                Console.WriteLine($"INFO  T269 seed: one game hour of guns on bare snow at +{air} C: {with:F3} m seeded (seed {seed:F3} m), {without:F3} m without");
                check(with > seed && without < 0.01f, W, "one game hour of guns on bare snow at +" + air + " C: seeded " + with.ToString("0.000") +
                      " m (seed " + seed.ToString("0.000") + "), without the seed " + without.ToString("0.000") + " m");
            }
        }

        private static void T09Ledger(Func<bool, string, string, bool> check)
        {
            const string W = "snow S32 ledger";
            check(RunSnowRules.SnowSeasonKey(2030, 0.95f) == 2030 && RunSnowRules.SnowSeasonKey(2031, 0.1f) == 2030, W, "December 2030 and February 2031 are one season");
            check(RunSnowRules.SnowSeasonKey(2031, 0.1f) != RunSnowRules.SnowSeasonKey(2031, 0.7f), W, "February and October 2031 are two seasons");
            double v = RunSnowRules.VolumeM3(0.01 / 8.0, 196f);
            check(Near(v, 1.96, 1e-9), W, "1 cm over one 14 m texel is 1.96 m3: " + v);
            check(Near(RunSnowRules.CmTexels(v, 196f), 1.0, 1e-9), W, "which is 1 cm-texel");
            check(RunSnowRules.CmTexels(5.0, 0f) == 0.0, W, "no texel area: 0 cm-texels");
            RunSnowRules.RunLedger l = default;
            l.Add(2030, 100.0);
            l.Add(2030, 50.0);
            check(Near(l.MadeSeasonM3, 150.0, 1e-9) && Near(l.MadeTotalM3, 150.0, 1e-9), W, "two adds in one season accumulate");
            l.Add(2031, 10.0);
            check(Near(l.MadeSeasonM3, 10.0, 1e-9), W, "a new season zeroes the season figures");
            check(Near(l.MadeTotalM3, 160.0, 1e-9), W, "and keeps the totals");
            check(RunSnowRules.FramesToHours(10923u) < 1.001f && RunSnowRules.FramesToHours(10000000u) == RunSnowRules.MaxPackStepHours, W, "frames to hours, capped per pass");
        }
    }
}
