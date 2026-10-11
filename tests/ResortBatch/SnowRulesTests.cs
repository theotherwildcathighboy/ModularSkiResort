// SnowRulesTests — Round E1 test file for tests/ResortBatch (econ-round spec ch3 §11: the snow rules (108 checks)).
// Owner: stream S3 (docs/research/econ-round/e1-spec.md §3.2). Created empty by stage 0 (S0, 2026-10-04):
// e1-spec R9 — `public static class SnowRulesTests` with `public static void Run(Func<bool, string, string, bool> check)`
// and its own Near; Program.Main calls SnowRulesTests.Run(Check). Oracles are literal hand values.
// Written by stream S3 (2026-10-04): cases S1-S16 of ch3 §11.2, one check per row (2 + 8 + 9 + 10 + 8 + 5 + 6 +
// 5 + 11 + 8 + 4 + 8 + 10 + 5 + 4 + 5 = 108). The Burst jobs, the snapshot state machine, the async request and
// the merge with stepScale need the Unity runtime; the field test and the ch3 §10 log lines cover them (§11.3).
// E1 fixer (2026-10-04, e1-spec §8.9 F6): S13b IsForcedDryCheck, 6 checks (108 → 114).
// Round M2 (2026-10-10, builder B6; docs/research/round-m2/build-contract.md §7, topup-skip.md §3.1 and its check G7):
// the packed skip, S17-S23, 2 + 6 + 8 + 17 + 8 + 8 + 2 = 51 checks (114 → 165). S13 / S13b stay byte for byte (the new
// DryPass fields default to 0 / false and CanSkipDry is untouched). The episode bookkeeping, the Snow [day] roll and
// the merge need the Unity runtime: the field sheet (topup-skip §3.2 F1-F7) covers them.

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
            ForcedDryCheckCases(check);   // E1 fixer F6 (+6)
            PackedConstants(check);       // Round M2 S17 (+2)
            PackGuardCases(check);        // S18 (+6)
            CalmShareCases(check);        // S19 (+8)
            CanSkipPackedCases(check);    // S20 (+17)
            SkipKindCases(check);         // S21 (+8)
            ForcedCheckCases(check);      // S22 (+8)
            PackedProperties(check);      // S23 (+2)
            Mix64Cases(check);
            HourAlignedDateCases(check);
            DayMeanCases(check);
        }

        private static bool Near(double a, double b, double eps)
        {
            return Math.Abs(a - b) <= eps;
        }

        // S1 constants (2)
        private static void Constants(Func<bool, string, string, bool> check)
        {
            const string W = "snow S1 constants";
            check(RunSnowRules.SkiableNorm == 0.0025f && RunSnowRules.OpenAtShare == 0.60f &&
                  RunSnowRules.CloseBelowShare == 0.40f && RunSnowRules.MultiplierDeadBand == 0.05f &&
                  RunSnowRules.DefaultSnowmakingMarginC == 2 && RunSnowRules.ForcedRealPassFrames == 1024u &&
                  RunSnowRules.MeasureIntervalFrames == 1024u && RunSnowRules.MaxSnapshotAgeFrames == 4096u &&
                  RunSnowRules.MinPassIntervalS == 0.5f && RunSnowRules.MaxStepScale == 4f &&
                  RunSnowRules.TopupIntervalFrames == 64 && RunSnowRules.DayMeanSamples == 24,
                W, "a tuning constant moved: the literal oracles below assume the ch3 §2 values");
            check((byte)RunKind.Natural == 3, W, "RunKind.Natural = " + (byte)RunKind.Natural + ", expected 3 (appended, never renumbered)");
        }

        // S2 OpenMultiplier (8)
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

        // S3 NextState (9)
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

        // S4 a season, step by step (10): state, published multiplier (dead band), phase.
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

        // S5 Republish (8)
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

        // S6 Phase (5)
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

        // S7 ThresholdC (6)
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

        // S8 ClampMargin (5)
        private static void ClampMarginCases(Func<bool, string, string, bool> check)
        {
            const string W = "snow S8 ClampMargin";
            int[] input = { 2, -1, 9, 0, 6 };
            int[] expected = { 2, 0, 6, 0, 6 };
            for (int i = 0; i < input.Length; i++)
            {
                int got = RunSnowRules.ClampMargin(input[i]);
                check(got == expected[i], W, input[i] + " -> " + got + ", expected " + expected[i]);
            }
        }

        // S9 EstimatedShare(bottom, top, T, threshold, base, lapse) (11)
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

        // S10 IsSkiable(depthNorm, ownTargetNorm) (8)
        private static void IsSkiableCases(Func<bool, string, string, bool> check)
        {
            const string W = "snow S10 IsSkiable";
            check(RunSnowRules.IsSkiable(164 / 65535f, 0f), W, "2.002 cm, no own target: should be skiable");
            check(!RunSnowRules.IsSkiable(163 / 65535f, 0f), W, "1.990 cm, no own target: should not be skiable");
            check(!RunSnowRules.IsSkiable(0f, 0f), W, "bare: should not be skiable");
            check(RunSnowRules.IsSkiable(123 / 65535f, 0.001875f), W, "fringe at its 1.5 cm target: should be skiable (E1-S1)");
            check(!RunSnowRules.IsSkiable(122 / 65535f, 0.001875f), W, "fringe one quantum short: should not be skiable");
            check(RunSnowRules.IsSkiable(164 / 65535f, 0.015f), W, "12 cm target, 2.002 cm: the 2 cm bar applies, skiable");
            check(!RunSnowRules.IsSkiable(163 / 65535f, 0.015f), W, "12 cm target, 1.990 cm: the 2 cm bar applies, not skiable");
            check(!RunSnowRules.IsSkiable(0f, 0.5f / 65535f), W, "a sub-quantum target is ignored: bare is not skiable");
        }

        // S11 ShareFromSums (4)
        private static void ShareFromSumsCases(Func<bool, string, string, bool> check)
        {
            const string W = "snow S11 ShareFromSums";
            float v;
            v = RunSnowRules.ShareFromSums(3.0, 4.0);  check(v == 0.75f, W, "(3, 4) -> " + v);
            v = RunSnowRules.ShareFromSums(0.0, 0.4);  check(float.IsNaN(v), W, "(0, 0.4) -> " + v + ", expected NaN (too small)");
            v = RunSnowRules.ShareFromSums(0.0, 0.0);  check(float.IsNaN(v), W, "(0, 0) -> " + v + ", expected NaN");
            v = RunSnowRules.ShareFromSums(5.0, 4.0);  check(v == 1f, W, "(5, 4) -> " + v + ", expected 1 (clamped)");
        }

        // S12 StepScale (8), all exact
        private static void StepScaleCases(Func<bool, string, string, bool> check)
        {
            const string W = "snow S12 StepScale";
            uint[] frames = { 64u, 0u, 32u, 96u, 120u, 240u, 256u, 1000u };
            float[] expected = { 1f, 1f, 1f, 1.5f, 1.875f, 3.75f, 4f, 4f };
            for (int i = 0; i < frames.Length; i++)
            {
                float got = RunSnowRules.StepScale(frames[i]);
                check(got == expected[i], W, frames[i] + " frames -> " + got + ", expected " + expected[i] +
                      (frames[i] == 64u ? " (the bit-identity case)" : ""));
            }
        }

        // S13 CanSkipDry (10)
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

        // S13b IsForcedDryCheck (6) — E1 fixer (e1-spec §8.9 F6): the forced check is the frame term ALONE.
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

        // ── Round M2 packed skip (topup-skip §3.1): records and arguments ────────────────────────────────────────
        // H = the file's footprint hash (S13's constant). P = a packed record: 478 texels and 0.212 m are field test 10
        // run 2b's last footprint (L 951) and run mean (Surface L 946). D = a dry record. Arguments (cold, contained,
        // hash, sinceReal, calm); the base is (0, 478, H, 188u, true): 188 sim frames = one 8× pass (L 452).
        private const ulong PackedHash = 0x732AD62604202A70UL;

        private static DryPass PackedRecord()
        {
            return new DryPass
            {
                Valid = true, ColdTexels = 0, Injected = 0, Lowered = 0, PackGuardNext = true, RunMeanDepthM = 0.212f,
                RunDepthSum = 12.5, ContainedTexels = 478, FootprintHash = PackedHash,
            };
        }

        private static DryPass DryRecord()
        {
            return new DryPass
            {
                Valid = true, ColdTexels = 0, Injected = 0, Lowered = 0, PackGuardNext = false, RunMeanDepthM = 0f,
                RunDepthSum = 0.0, ContainedTexels = 478, FootprintHash = PackedHash,
            };
        }

        // S17 Round M2 constants (2)
        private static void PackedConstants(Func<bool, string, string, bool> check)
        {
            const string W = "snow S17 packed constants";
            check(RunSnowRules.PackGuardMeanDepthM == 0.02f && RunSnowRules.PackedSkipMinRunDepthM == 0.10f &&
                  RunSnowRules.CalmOpenShare == 0.70f && RunSnowRules.CalmClosedShare == 0.30f,
                W, "a Round M2 constant moved: the literal oracles below assume pack guard 0.02 m, dress guard 0.10 m, " +
                   "calm shares 70 % / 30 %");
            check((byte)TopupSkip.None == 0 && (byte)TopupSkip.Dry == 1 && (byte)TopupSkip.Packed == 2,
                W, "TopupSkip literals changed: expected None 0, Dry 1, Packed 2");
        }

        // S18 PackGuard (6)
        private static void PackGuardCases(Func<bool, string, string, bool> check)
        {
            const string W = "snow S18 PackGuard";
            check(!RunSnowRules.PackGuard(0.02f), W, "0.02 m: expected off (strictly above the constant)");
            check(RunSnowRules.PackGuard(0.020001f), W, "0.020001 m: expected on");
            check(RunSnowRules.PackGuard(0.212f), W, "0.212 m: expected on");
            check(!RunSnowRules.PackGuard(0f), W, "0 m: expected off");
            check(!RunSnowRules.PackGuard(float.NaN), W, "NaN: expected off");
            check(!RunSnowRules.PackGuard(-0.5f), W, "-0.5 m: expected off");
        }

        // S19 IsCalmShare (8)
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

        // S20 CanSkipPacked (17)
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
            p = PackedRecord(); p.Lowered = 1;
            Packed(check, W, p, 0, 478, H, 188u, true, false, "the last real pass lowered a texel: no skip");
            p = PackedRecord(); p.PackGuardNext = false;
            Packed(check, W, p, 0, 478, H, 188u, true, false, "the next merge has the pack guard off: no skip");
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

        // S21 SkipKind (8). Row 3 (topup-skip: "D.PackGuardNext = true → Dry (dry first)") also sets RunMeanDepthM 0.212 so
        // that BOTH predicates hold on that synthetic record and the order is really tested (with 0 m only Dry holds).
        private static void SkipKindCases(Func<bool, string, string, bool> check)
        {
            const string W = "snow S21 SkipKind";
            const ulong H = PackedHash;
            DryPass d = DryRecord();
            Kind(check, W, d, 0, 478, H, 188u, true, TopupSkip.Dry, "dry record, base");
            Kind(check, W, d, 0, 478, H, 188u, false, TopupSkip.Dry, "dry record, a share near the open line (D30 ignores the share guard)");
            d = DryRecord(); d.PackGuardNext = true; d.RunMeanDepthM = 0.212f;
            Kind(check, W, d, 0, 478, H, 188u, true, TopupSkip.Dry, "dry record with the pack guard on and 0.212 m (both hold: dry first)");
            DryPass p = PackedRecord();
            Kind(check, W, p, 0, 478, H, 188u, true, TopupSkip.Packed, "packed record, base");
            Kind(check, W, p, 1, 478, H, 188u, true, TopupSkip.None, "packed record, one cold texel now");
            Kind(check, W, p, 0, 478, H, 1024u, true, TopupSkip.None, "packed record, 1024 frames (forced pass due)");
            p = PackedRecord(); p.PackGuardNext = false;
            Kind(check, W, p, 0, 478, H, 188u, true, TopupSkip.None, "snow lies and the pack guard is off (managed melt has work)");
            Kind(check, W, default(DryPass), 0, 478, H, 188u, true, TopupSkip.None, "no real pass this load (default record)");
        }

        private static void Kind(Func<bool, string, string, bool> check, string w, DryPass last, int cold, int contained,
            ulong hash, uint since, bool calm, TopupSkip expected, string what)
        {
            TopupSkip got = RunSnowRules.SkipKind(last, cold, contained, hash, since, calm);
            check(got == expected, w, what + ": " + got + ", expected " + expected);
        }

        // S22 IsForcedCheck (8): the 1,024-frame term is the ONLY failing term (E1 F6, extended to the packed kind).
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
            p = PackedRecord(); p.PackGuardNext = false;
            check(!RunSnowRules.IsForcedCheck(p, 0, 478, H, 1024u, true), W, "1024 frames, the pack guard off: not forced");
            p = PackedRecord(); p.Valid = false;
            check(!RunSnowRules.IsForcedCheck(p, 0, 478, H, 5000u, true), W, "no real pass this load: not forced");
        }

        // S23 properties (2), seed 20261010, 20,000 records: one check per property, violations counted (the oracle is
        // 0). Draw order per record (fixed, topup-skip check G7), each value tables[rng.Next(length)]: Valid, ColdTexels,
        // Injected, Lowered, PackGuardNext, RunDepthSum, RunMeanDepthM (drawn only when RunDepthSum != 0; else 0f), then
        // the arguments cold, contained, hash, sinceReal, calm; the record's footprint is 478 texels / H. B6 deviation: the
        // spec's 2,000 records give this seed ONE packed record (a node port of the seeded System.Random says so); 20,000
        // give 18 packed and 103 dry, and each check also requires that both kinds were drawn (no vacuous pass).
        private static void PackedProperties(Func<bool, string, string, bool> check)
        {
            const string W = "snow S23 packed properties";
            const int Records = 20000;
            var rng = new Random(20261010);
            bool[] validT = { true, false };
            int[] coldT = { 0, 0, 0, 3 };
            int[] injectedT = { 0, 0, 2 };
            int[] loweredT = { 0, 0, 1 };
            bool[] guardT = { true, false };
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
                last.Lowered = loweredT[rng.Next(loweredT.Length)];
                last.PackGuardNext = guardT[rng.Next(guardT.Length)];
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
                // P1: a packed skip implies the terms that make the merge a no-op (no cold texel now or at the last real
                // pass, nothing raised or lowered, the pack guard on for the next merge) and the pass not due.
                if (canPacked && !(cold == 0 && last.ColdTexels == 0 && last.Injected == 0 && last.Lowered == 0 &&
                                   last.PackGuardNext && since < RunSnowRules.ForcedRealPassFrames))
                {
                    if (bad1++ == 0)
                        first1 = Describe(last, cold, contained, hash, since, calm);
                }
                // P2: never both skips on these records; SkipKind is Dry exactly when CanSkipDry, Packed exactly when
                // CanSkipPacked.
                if ((canDry && canPacked) || (kind == TopupSkip.Dry) != canDry || (kind == TopupSkip.Packed) != canPacked)
                {
                    if (bad2++ == 0)
                        first2 = Describe(last, cold, contained, hash, since, calm) + " -> " + kind;
                }
            }
            check(bad1 == 0 && packed > 0, W, "P1 CanSkipPacked implies no cold texel now or at the last real pass, nothing " +
                  "raised or lowered, the pack guard on, not due: " + bad1 + " violation(s) in " + Records + " records (" +
                  packed + " packed, expected > 0)" + (first1 != null ? "; first: " + first1 : ""));
            check(bad2 == 0 && packed > 0 && dry > 0, W, "P2 never both kinds, SkipKind = Dry iff CanSkipDry, = Packed iff " +
                  "CanSkipPacked: " + bad2 + " violation(s) in " + Records + " records (" + packed + " packed, " + dry +
                  " dry, both expected > 0)" + (first2 != null ? "; first: " + first2 : ""));
        }

        private static string Describe(DryPass last, int cold, int contained, ulong hash, uint since, bool calm)
        {
            return "record (valid " + last.Valid + ", cold " + last.ColdTexels + ", injected " + last.Injected +
                   ", lowered " + last.Lowered + ", guard " + last.PackGuardNext + ", sum " +
                   last.RunDepthSum.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ", mean " +
                   last.RunMeanDepthM.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "), now (cold " +
                   cold + ", contained " + contained + ", hash " + (hash == PackedHash ? "H" : "H+1") + ", since " + since +
                   ", calm " + calm + ")";
        }

        // S14 Mix64 and the footprint hash (5)
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

        // S15 HourAlignedDate (4)
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

        // S16 DayMeanC (5)
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
    }
}
