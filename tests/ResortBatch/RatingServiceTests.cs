// RatingServiceTests — Round E1 ch2 §2.5: the always-on rating service's pure rules
// (SkiRunRatingRules.NextBackgroundStart / NextServiceState) and RunKind.Natural (13 checks).
// Owner: stream S2 (docs/research/econ-round/e1-spec.md §3.2). Created empty by stage 0 (S0, 2026-10-04),
// filled by S2 (2026-10-04) with ch2 §2.5's text: e1-spec R9 — `public static class RatingServiceTests` with
// `public static void Run(Func<bool, string, string, bool> check)` and its own Near; Program.Main calls
// RatingServiceTests.Run(Check). Oracles are literal hand values.
using System;
using SkiResortMod.Systems;

namespace ResortBatch
{
    public static class RatingServiceTests          // e1-spec R9: public class, public Run
    {
        public static void Run(Func<bool, string, string, bool> check)
        {
            // Constants first: the oracles below are literal for them.
            check(SkiRunRatingRules.DutyFactorOpen == 8f && SkiRunRatingRules.DutyFactorClosed == 32f &&
                  SkiRunRatingRules.ClosedMinGapS == 0.25f && SkiRunRatingRules.WarmupDeadlineS == 90f,
                  "service constants", "tuning changed: update the oracles");

            // Duty cycle.
            Near(check, SkiRunRatingRules.NextBackgroundStart(100f, 8f, true), 100.064f, "duty open 8 ms");
            Near(check, SkiRunRatingRules.NextBackgroundStart(100f, 8f, false), 100.256f, "duty closed 8 ms (32 x 8 ms)");
            Near(check, SkiRunRatingRules.NextBackgroundStart(100f, 4f, false), 100.25f, "duty closed 4 ms (min gap wins)");
            Near(check, SkiRunRatingRules.NextBackgroundStart(100f, 10.9f, false), 100.3488f, "duty closed 10.9 ms");
            check(SkiRunRatingRules.NextBackgroundStart(100f, 0f, true) == 100f, "duty open 0 ms", "not 100");

            // Service state.
            byte W = SkiRunRatingRules.ServiceWarmingUp, R = SkiRunRatingRules.ServiceReady,
                 F = SkiRunRatingRules.ServiceFailed, O = SkiRunRatingRules.ServiceOff;
            check(SkiRunRatingRules.NextServiceState(W, false, 189.9f, 100f) == W, "state", "89.9 s: must still warm up");
            check(SkiRunRatingRules.NextServiceState(W, false, 190f, 100f) == R, "state", "90 s deadline is inclusive");
            check(SkiRunRatingRules.NextServiceState(W, true, 101f, 100f) == R, "state", "warm-up done → Ready");
            check(SkiRunRatingRules.NextServiceState(R, false, 0f, 0f) == R, "state", "Ready must stay Ready");
            check(SkiRunRatingRules.NextServiceState(F, true, 500f, 0f) == F, "state", "Failed must stay Failed");
            check(SkiRunRatingRules.NextServiceState(O, true, 500f, 0f) == O, "state", "Off must stay Off");

            // RunKind: Natural appended, the others unchanged.
            check((byte)RunKind.Groomed == 0 && (byte)RunKind.Snowmaking == 1 && (byte)RunKind.Night == 2 &&
                  (byte)RunKind.Natural == 3, "runkind", "RunKind values != 0/1/2/3");
        }

        private static void Near(Func<bool, string, string, bool> check, float got, float expected, string what)
        {
            check(Math.Abs(got - expected) <= 1e-4f, "service " + what, "expected " + expected.ToString("R") + ", got " + got.ToString("R"));
        }
    }
}
