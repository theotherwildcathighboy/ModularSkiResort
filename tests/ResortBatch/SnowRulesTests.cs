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
            T09Cases(check);              // T09 S24-S32
        }

        private static bool Near(double a, double b, double eps)
        {
            return Math.Abs(a - b) <= eps;
        }

        // S1 constants (2)
        private static void Constants(Func<bool, string, string, bool> check)
        {
            const string W = "snow S1 constants";
            check(RunSnowRules.SkiableNorm == 0.91f / 8f && RunSnowRules.OpenAtShare == 0.60f &&
                  RunSnowRules.CloseBelowShare == 0.40f && RunSnowRules.MultiplierDeadBand == 0.05f &&
                  RunSnowRules.DefaultSnowmakingMarginC == 3.3f && RunSnowRules.ForcedRealPassFrames == 1024u &&
                  RunSnowRules.MeasureIntervalFrames == 1024u && RunSnowRules.MaxSnapshotAgeFrames == 4096u &&
                  RunSnowRules.MinPassIntervalS == 0.5f && RunSnowRules.MaxStepScale == 4f &&
                  RunSnowRules.TopupIntervalFrames == 64 && RunSnowRules.DayMeanSamples == 24,
                W, "a tuning constant moved: the literal oracles below assume the ch3 §2 values (T09: skiable bar 15 cm, margin 3.3 C)");
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

        // S8 ClampMargin (7; float since T09)
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

        // S10 IsSkiable(depthNorm, ownTargetNorm) (8; T133 the 3 ft bar: 0.91 / 8 x 65535 = 7454.53 quanta, so 7455 passes)
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

        // ───────────────────────── T09 (2026-10-16): Richard's snow rulings ─────────────────────────
        // S24 constants, S25 the skiable bar per run, S26 the gun reaches "open" by itself, S27 aspect, S28 spring, S29 the
        // budget, S30 the protection machine, S31 the field-sheet scenarios in rule form, S32 the ledger. Hand-value oracles.

        private static void T09Cases(Func<bool, string, string, bool> check)
        {
            T09Constants(check);
            T09SkiableBar(check);
            T09GunOpens(check);
            T09Aspect(check);
            T09Spring(check);
            T09Budget(check);
            T09Machine(check);
            T09FieldSheet(check);
            T09Ledger(check);
        }

        // S24 constants (10)
        private static void T09Constants(Func<bool, string, string, bool> check)
        {
            const string W = "snow S24 T09 constants";
            check(RunSnowRules.SkiableDepthM == 0.91f && RunSnowRules.ExpertNaturalSkiableDepthM == 0.91f, W, "T133 one bar, 3 ft (0.91 m), for every run");
            check(RunSnowRules.ExpertBandMin == SkiResortScore.BandBlack && SkiResortScore.BandDoubleBlack == RunSnowRules.ExpertBandMin + 1,
                W, "ExpertBandMin tracks SkiResortScore.BandBlack (black and double black)");
            check(RunSnowRules.OpenAtShare == 0.60f && RunSnowRules.CloseBelowShare == 0.40f, W, "the 60 % / 40 % hysteresis is unchanged");
            check(RunSnowRules.DefaultSnowmakingMarginC == 3.3f && RunSnowRules.MinSnowmakingMarginC == 0f && RunSnowRules.MaxSnowmakingMarginC == 6f,
                W, "margin default 3.3 C (38 F), slider range 0..6");
            check(RunSnowRules.GunTargetDepthM == RunSnowRules.SkiableDepthM && RunSnowRules.GroomedTargetDepthM == RunSnowRules.GunStopDepthM,
                W, "T133 gun base = the 3 ft bar, groomed ceiling = the gun stop depth (0.96 m)");
            check(RunSnowRules.GunTargetDepthM >= RunSnowRules.SkiableDepthM, W, "the gun target reaches the open bar");
            check(RunSnowRules.GroomedTargetDepthM > RunSnowRules.SkiableDepthM, W, "the groomed target clears the open bar");
            check(RunSnowRules.ProtectBudgetHours == 48f && RunSnowRules.ProtectMaxLocalC == 10f, W, "budget 48 game h, stop above 10 C");
            check(RunSnowRules.ProtectSpringFactor == 0.5f && RunSnowRules.ProtectNorthFactor == 1.5f && RunSnowRules.ProtectSouthFactor == 0.5f,
                W, "spring x0.5, north x1.5, south x0.5");
            // The re-raise must beat the melt it faces: 2e-5 x T x 0.2 x 16 snow ticks x 8 m at T = 10 C is 6.4 mm per window
            // (snow-injection.md 4; the linear form is UNVERIFIED), with 1.5x headroom.
            double meltAt10C = 2e-5 * 10.0 * 0.2 * 16.0 * 8.0;
            check(RunSnowRules.ProtectRaisePerTopupM >= 1.5 * meltAt10C - 1e-9, W, "the protection step beats the estimated melt at 10 C by 1.5x");
        }

        // S25 SkiableBarNorm and the bar in IsSkiable (15)
        private static void T09SkiableBar(Func<bool, string, string, bool> check)
        {
            const string W = "snow S25 skiable bar";
            // T133 (Richard, 2026-10-20): every run opens at 3 ft (0.91 m), made or natural, every rating.
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

        // S26 the gun reaches "open" by itself (9): windows of 64 frames at step scale 1, the slowest texel has rate noise at its minimum
        private static void T09GunOpens(Func<bool, string, string, bool> check)
        {
            const string W = "snow S26 gun opens";
            float thr = RunSnowRules.DefaultSnowmakingMarginC;   // freezing 0 + 3.3
            float slow = RunSnowRules.RampRateMin;
            float mean = RunSnowRules.RampRateMin + RunSnowRules.RampRateSpan * 0.5f;
            // T133: the gun target is the 3 ft open bar (0.91 m). -3 C: chill (3.3 + 3) / 4 > 1 -> output 2.0; slowest step 0.002 m.
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

        // S27 AspectFactor (9)
        private static void T09Aspect(Func<bool, string, string, bool> check)
        {
            const string W = "snow S27 aspect";
            check(Near(RunSnowRules.AspectFactor(0f, -0.5f, 1f), 1.5, 1e-6), W, "steep face descending toward +Z (north): x1.5");
            check(Near(RunSnowRules.AspectFactor(0f, 0.5f, 1f), 0.5, 1e-6), W, "steep face descending toward -Z (south): x0.5");
            check(Near(RunSnowRules.AspectFactor(0.5f, 0f, 1f), 1.0, 1e-6), W, "east face: x1");
            check(Near(RunSnowRules.AspectFactor(-0.5f, 0f, 1f), 1.0, 1e-6), W, "west face: x1");
            check(RunSnowRules.AspectFactor(0f, 0f, 1f) == 1f, W, "flat ground: x1");
            check(Near(RunSnowRules.AspectFactor(0f, -0.05f, 1f), 1.25, 1e-6), W, "gentle north face (5 % pitch, half the full weight): x1.25");
            check(Near(RunSnowRules.AspectFactor(0f, 0.05f, 1f), 0.75, 1e-6), W, "gentle south face: x0.75");
            check(Near(RunSnowRules.AspectFactor(0f, -0.5f, -1f), 0.5, 1e-6), W, "a southern latitude flips north and south");
            float ne = RunSnowRules.AspectFactor(-0.3f, -0.3f, 1f);
            check(Near(ne, 1.0 + 0.5 * 0.70710678, 1e-5), W, "north-east face: 1 + 0.5 x sin45 = 1.354 (" + ne + ")");
        }

        // S28 IsSpring (9)
        private static void T09Spring(Func<bool, string, string, bool> check)
        {
            const string W = "snow S28 spring";
            check(RunSnowRules.IsSpring("SeasonSpring", 0.9f), W, "the climate's spring season is spring on any date");
            check(!RunSnowRules.IsSpring("SeasonSummer", 0.3f), W, "the climate's summer is not spring even in April");
            check(!RunSnowRules.IsSpring("SeasonWinter", 0.3f), W, "the climate's winter is not spring");
            check(!RunSnowRules.IsSpring("SeasonAutumn", 0.3f), W, "the climate's autumn is not spring");
            check(RunSnowRules.IsSpring("MySeason", 0.3f), W, "a custom season name falls back to the calendar: April is spring");
            check(!RunSnowRules.IsSpring("MySeason", 0.7f), W, "a custom season name: September is not");
            check(!RunSnowRules.IsSpring(null, 0.1f), W, "no season name: February is winter");
            check(RunSnowRules.IsSpring(null, 1.3f), W, "the date wraps over the year: 1.3 is 0.3");
            check(!RunSnowRules.IsSpring(null, 5f / 12f), W, "1 June is not spring (end exclusive)");
        }

        // S29 budgets (12)
        private static void T09Budget(Func<bool, string, string, bool> check)
        {
            const string W = "snow S29 budget";
            check(RunSnowRules.EffectiveBudgetHours(false, 1f) == 48f, W, "winter flat: 48 h");
            check(RunSnowRules.EffectiveBudgetHours(true, 1f) == 24f, W, "spring flat: 24 h (halved)");
            check(RunSnowRules.EffectiveBudgetHours(false, 1.5f) == 72f, W, "winter north: 72 h");
            check(RunSnowRules.EffectiveBudgetHours(false, 0.5f) == 24f, W, "winter south: 24 h");
            check(RunSnowRules.EffectiveBudgetHours(true, 1.5f) == 36f, W, "spring north: 36 h");
            check(RunSnowRules.EffectiveBudgetHours(true, 0.5f) == 12f, W, "spring south: 12 h");
            check(RunSnowRules.MaxBudgetHours(false) == 72f && RunSnowRules.MaxBudgetHours(true) == 36f, W, "the largest budget is the north face's");
            check(RunSnowRules.TexelProtected(47.9f, 5f, false, 1f), W, "flat texel at 47.9 h of 48: still protected");
            check(!RunSnowRules.TexelProtected(48f, 5f, false, 1f), W, "flat texel at 48 h: spent");
            check(RunSnowRules.TexelProtected(60f, 5f, false, 1.5f) && !RunSnowRules.TexelProtected(30f, 5f, false, 0.5f), W,
                "north texel still held at 60 h, south texel not at 30 h");
            check(RunSnowRules.TexelProtected(1f, 10f, false, 1f) && !RunSnowRules.TexelProtected(1f, 10.1f, false, 1f), W, "protection stops above 10 C");
            check(!RunSnowRules.TexelProtected(25f, 5f, true, 1f), W, "in spring a flat texel is spent at 24 h");
        }

        private static RunSnowRules.ProtectEnd Step(ref RunSnowRules.ProtectRun run, float hours, bool warm, bool open, float runC, bool spring, out bool started)
        {
            return RunSnowRules.StepProtection(ref run, hours, 100, warm ? 60 : 100, open, runC, spring, out started);
        }

        // S30 the protection machine (14)
        private static void T09Machine(Func<bool, string, string, bool> check)
        {
            const string W = "snow S30 protection machine";
            RunSnowRules.ProtectRun run = default;
            RunSnowRules.ProtectEnd end;
            bool started;
            end = Step(ref run, 0.1f, false, true, 5f, false, out started);
            check(run.State == RunSnowRules.ProtectState.Idle && !started && end == RunSnowRules.ProtectEnd.None, W, "all cold: nothing to protect");
            end = Step(ref run, 0.1f, true, false, 5f, false, out started);
            check(run.State == RunSnowRules.ProtectState.Idle && !started, W, "warm but the pack is not open: no spell (nothing worth holding)");
            end = Step(ref run, 0.1f, true, true, 12f, false, out started);
            check(run.State == RunSnowRules.ProtectState.Idle && !started, W, "warm, open, but the air at the run is 12 C: no spell");
            end = Step(ref run, 0.1f, true, true, 5f, false, out started);
            check(run.State == RunSnowRules.ProtectState.Active && started && run.HoursUsed == 0f, W, "warm, open, 5 C: the spell starts");
            for (int h = 0; h < 40; h++)
                Step(ref run, 1f, true, true, 5f, false, out started);
            run.HeldAtStartM3 = 2.5;   // the system stamps the run's ledger here when a spell starts
            check(run.State == RunSnowRules.ProtectState.Active && Near(run.HoursUsed, 40.0, 1e-3) && run.HeldAtStartM3 == 2.5, W, "40 warm hours: still active, 40 h used, the start stamp kept");
            // a cold dip of 2 h does not refill the budget
            run = default;
            Step(ref run, 0.1f, true, true, 5f, false, out started);
            Step(ref run, 10f, true, true, 5f, false, out started);
            end = Step(ref run, 2f, false, true, 5f, false, out started);
            check(run.State == RunSnowRules.ProtectState.Active && end == RunSnowRules.ProtectEnd.None && Near(run.HoursUsed, 10.0, 1e-3), W,
                "a 2 h cold dip: the spell stays open, the 10 h used stay");
            end = Step(ref run, 1.5f, false, true, 5f, false, out started);
            check(run.State == RunSnowRules.ProtectState.Idle && end == RunSnowRules.ProtectEnd.GunsBack, W, "3.5 h all cold: the guns are back, the spell ends (budget refills)");
            // budget spent: winter max is the north face's 72 h
            run = default;
            Step(ref run, 0.1f, true, true, 5f, false, out started);
            end = RunSnowRules.ProtectEnd.None;
            for (int h = 0; h < 71; h++)
                end = Step(ref run, 1f, true, true, 5f, false, out started);
            check(run.State == RunSnowRules.ProtectState.Active && end == RunSnowRules.ProtectEnd.None, W, "71 warm hours in winter: still active");
            end = Step(ref run, 1f, true, true, 5f, false, out started);
            check(run.State == RunSnowRules.ProtectState.Spent && end == RunSnowRules.ProtectEnd.BudgetSpent, W, "72 warm hours in winter: budget spent");
            end = Step(ref run, 1f, true, true, 5f, false, out started);
            check(run.State == RunSnowRules.ProtectState.Spent && !started && end == RunSnowRules.ProtectEnd.None, W, "spent stays spent while it is warm (no restart loop)");
            Step(ref run, 3.5f, false, true, 5f, false, out started);
            check(run.State == RunSnowRules.ProtectState.Idle, W, "3.5 cold hours re-arm a spent run");
            // spring halves the largest budget: 36 h
            run = default;
            Step(ref run, 0.1f, true, true, 5f, true, out started);
            end = RunSnowRules.ProtectEnd.None;
            for (int h = 0; h < 36; h++)
                end = Step(ref run, 1f, true, true, 5f, true, out started);
            check(run.State == RunSnowRules.ProtectState.Spent && end == RunSnowRules.ProtectEnd.BudgetSpent, W, "spring: spent at 36 h (half of winter's 72)");
            // the 10 C stop and the pack
            run = default;
            Step(ref run, 0.1f, true, true, 5f, false, out started);
            end = Step(ref run, 1f, true, true, 10.5f, false, out started);
            check(run.State == RunSnowRules.ProtectState.Spent && end == RunSnowRules.ProtectEnd.TooWarm, W, "the air at the run passes 10 C: protection stops");
            run = default;
            Step(ref run, 0.1f, true, true, 5f, false, out started);
            end = Step(ref run, 1f, true, false, 5f, false, out started);
            check(run.State == RunSnowRules.ProtectState.Spent && end == RunSnowRules.ProtectEnd.PackLost, W, "the run closed under protection: pack lost");
        }

        // S31 the field-sheet scenarios, in rule form (6)
        private static void T09FieldSheet(Func<bool, string, string, bool> check)
        {
            const string W = "snow S31 field sheet";
            // A 2-day +6 C spell in January, a flat texel: the run stays open under protection, texels still held at the end.
            RunSnowRules.ProtectRun run = default;
            bool started;
            RunSnowRules.ProtectEnd end = RunSnowRules.ProtectEnd.None;
            Step(ref run, 0.1f, true, true, 6f, false, out started);
            check(started, W, "+6 C January spell: protection starts");
            for (int h = 0; h < 47; h++)
                end = Step(ref run, 1f, true, true, 6f, false, out started);
            check(run.State == RunSnowRules.ProtectState.Active && end == RunSnowRules.ProtectEnd.None, W, "after 47 h of +6 C the spell is still on");
            check(RunSnowRules.TexelProtected(run.HoursUsed, 6f, false, 1f), W, "a flat texel is still held at 47 h of +6 C (inside the 48 h budget)");
            // A +12 C spell: the protection never starts, so the managed melt and the game's melt close the run.
            RunSnowRules.ProtectRun hot = default;
            Step(ref hot, 0.1f, true, true, 12f, false, out started);
            check(hot.State == RunSnowRules.ProtectState.Idle && !started, W, "+12 C: no protection");
            check(!RunSnowRules.TexelProtected(0f, 12f, false, 1f), W, "+12 C: no texel is held");
            // Natural runs: the machine is driven only for gun runs (SkiSnowInjectionSystem.RefreshRunSnow); a natural black run's bar is 31 cm.
            check(RunSnowRules.SkiableBarNorm(RunKind.Natural, 3) == RunSnowRules.SkiableBarNorm(RunKind.Snowmaking, 3), W, "T133 a natural black needs the same 3 ft as a gun black");
        }

        // S32 the ledger (9)
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
            l.Add(2030, 100.0, 5.0);
            l.Add(2030, 50.0, 0.0);
            check(Near(l.MadeSeasonM3, 150.0, 1e-9) && Near(l.MadeTotalM3, 150.0, 1e-9) && Near(l.HeldSeasonM3, 5.0, 1e-9), W, "two adds in one season accumulate");
            l.Add(2031, 10.0, 1.0);
            check(Near(l.MadeSeasonM3, 10.0, 1e-9) && Near(l.HeldSeasonM3, 1.0, 1e-9), W, "a new season zeroes the season figures");
            check(Near(l.MadeTotalM3, 160.0, 1e-9) && Near(l.HeldTotalM3, 6.0, 1e-9), W, "and keeps the totals");
            check(RunSnowRules.FramesToHours(10923u) < 1.001f && RunSnowRules.FramesToHours(10000000u) == RunSnowRules.MaxProtectStepHours, W, "frames to hours, capped per pass");
        }
    }
}
