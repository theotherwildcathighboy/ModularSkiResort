// Quick runs tool rules (QuickRunsRules.cs): region readout, totals (fee, skiers, mix, variety), money check, refund, build step after
// validation, Temp -> candidate match by geometry, click pick, locale keys, knob clamps, tree-line clamp. Plus the LinkMeters alias.

using System.Collections.Generic;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class Program
    {
        private static float2[] Square(float x0, float z0, float side) =>
            new[] { new float2(x0, z0), new float2(x0 + side, z0), new float2(x0 + side, z0 + side), new float2(x0, z0 + side) };

        private static void QuickRunsTests()
        {
            const string W = "quick";

            // LinkMeters is one constant (DECISIONS 405 follow-up)
            Check(SkiRunAutoPlanner.LinkMeters == LiftServedRule.LinkMeters, W, "planner LinkMeters must alias LiftServedRule.LinkMeters");

            // Readout
            QuickRegionReadout r = QuickRunsRules.Readout(new[] { new float2(0, 0), new float2(10, 0) }, 0, 10, 1e6f, 4e4f);
            Check(r.State == QuickRegionState.Open, W, "2 corners read Open, got " + r.State);
            r = QuickRunsRules.Readout(Square(0, 0, 100), 100, 160, 1e6f, 4e4f);
            Check(r.State == QuickRegionState.TooSmall && math.abs(r.AreaM2 - 1e4f) < 1f && r.VerticalM == 60f, W,
                $"1 ha reads TooSmall 10000 m2 60 m, got {r.State} {r.AreaM2} {r.VerticalM}");
            r = QuickRunsRules.Readout(Square(0, 0, 500), 0, 300, 1e6f, 4e4f);
            Check(r.State == QuickRegionState.Ok, W, "25 ha reads Ok, got " + r.State);
            r = QuickRunsRules.Readout(Square(0, 0, 950), 0, 300, 1e6f, 4e4f);
            Check(r.State == QuickRegionState.Near, W, "0.9 km2 reads Near, got " + r.State);
            r = QuickRunsRules.Readout(Square(0, 0, 1000), 0, 300, 1e6f, 4e4f);
            Check(r.State == QuickRegionState.Near, W, "exactly the cap reads Near (not Over), got " + r.State);
            r = QuickRunsRules.Readout(Square(0, 0, 1001), 0, 300, 1e6f, 4e4f);
            Check(r.State == QuickRegionState.Over, W, "over the cap reads Over, got " + r.State);
            r = QuickRunsRules.Readout(Square(0, 0, 500), float.NaN, 300, 1e6f, 4e4f);
            Check(r.VerticalM == 0f, W, "NaN ground reads 0 vertical");

            // Fees: the bind's function
            Check(QuickRunsRules.FeeOf(RunKind.Groomed, 50000f) == SkiRunFees.Fee((int)RunKind.Groomed, 50000f), W, "groomed fee = SkiRunFees.Fee");
            Check(QuickRunsRules.FeeOf(RunKind.Natural, 20000f) == 500 + 250 * 2, W, "natural 2 ha = 1000");
            Check(QuickRunsRules.FeeOf((RunKind)9, 20000f) == 0, W, "unknown kind costs 0, never negative");

            // Skiers: the score's function (groomed blue 2 ha = 2 x 25 = 50; natural halves it)
            Check(math.abs(QuickRunsRules.SkiersOf(RunKind.Groomed, SlopeBand.Blue, 20000f) - 2.0 * SkiResortScore.SkiersPerHaBlue) < 1e-9, W, "groomed blue skiers");
            Check(math.abs(QuickRunsRules.SkiersOf(RunKind.Natural, SlopeBand.Blue, 20000f) -
                           2.0 * SkiResortScore.SkiersPerHaBlue * SkiResortScore.NaturalSkiersFactor) < 1e-9, W, "natural skiers halved");
            Check(QuickRunsRules.SkiersOf(RunKind.Groomed, SlopeBand.Flat, 20000f) == 0.0, W, "a flat run holds no skiers");

            // Totals over enabled runs
            var res = new AutoPlanResult();
            res.Runs.Add(new AutoRun { Band = SlopeBand.Green, Kind = RunKind.Groomed, AreaM2 = 30000f });
            res.Runs.Add(new AutoRun { Band = SlopeBand.Blue, Kind = RunKind.Groomed, AreaM2 = 40000f });
            res.Runs.Add(new AutoRun { Band = SlopeBand.DoubleBlack, Kind = RunKind.Natural, AreaM2 = 10000f });
            res.BandTarget[1] = 2;
            res.BandTarget[2] = 3;
            var t = new QuickTotals();
            QuickRunsRules.Sum(res, null, t);
            long fee = QuickRunsRules.FeeOf(RunKind.Groomed, 30000f) + QuickRunsRules.FeeOf(RunKind.Groomed, 40000f) +
                       QuickRunsRules.FeeOf(RunKind.Natural, 10000f);
            Check(t.Runs == 3 && t.Enabled == 3 && t.Fee == fee && t.Count[1] == 1 && t.Count[2] == 1 && t.Count[4] == 1 &&
                  t.Kinds[(int)RunKind.Natural] == 1 && t.Target[2] == 3 && t.Variety, W,
                $"totals all enabled: {t.Enabled} fee {t.Fee} (want {fee}) variety {t.Variety} (double black counts as black)");
            QuickRunsRules.Sum(res, new[] { true, true, false }, t);
            Check(t.Enabled == 2 && !t.Variety && t.Fee == fee - QuickRunsRules.FeeOf(RunKind.Natural, 10000f) && t.Count[4] == 0, W,
                "a toggled-off run leaves the totals and the variety");
            QuickRunsRules.Sum(res, new[] { false }, t);
            Check(t.Enabled == 2, W, "a short enabled array counts the rest as enabled");

            // Money
            Check(QuickRunsRules.CanPay(1000, 1000) && !QuickRunsRules.CanPay(999, 1000) && QuickRunsRules.CanPay(-5, 0), W, "CanPay boundaries");
            Check(QuickRunsRules.CanPay(2000000000, 14006000L * 16), W, "unlimited money pays a full batch of night runs");

            // Refund: exactly what was paid
            Check(QuickRunsRules.RefundOf(true, true, false, 4250) == 4250, W, "paid run refunds its amount");
            Check(QuickRunsRules.RefundOf(false, true, false, 4250) == 0, W, "no binding yet: never charged, no refund");
            Check(QuickRunsRules.RefundOf(true, false, false, 4250) == 0, W, "not paid: no refund");
            Check(QuickRunsRules.RefundOf(true, true, true, 0) == 0 && QuickRunsRules.RefundOf(true, true, true, 900) == 0, W, "restored: no refund");
            Check(QuickRunsRules.RefundOf(true, true, false, -3) == 0, W, "negative paid refunds nothing");

            // Build step
            Check(QuickRunsRules.AfterValidation(5, 0, false) == QuickBuildStep.Apply, W, "no refusal applies");
            Check(QuickRunsRules.AfterValidation(5, 2, false) == QuickBuildStep.Retry, W, "refusals with survivors retry once");
            Check(QuickRunsRules.AfterValidation(5, 2, true) == QuickBuildStep.GiveUp, W, "a second refusal gives up");
            Check(QuickRunsRules.AfterValidation(3, 3, false) == QuickBuildStep.GiveUp, W, "all refused gives up");
            Check(QuickRunsRules.AfterValidation(0, 0, false) == QuickBuildStep.GiveUp, W, "nothing emitted gives up");
            Check(QuickRunsRules.AfterValidation(3, 0, true) == QuickBuildStep.Apply, W, "a clean retry applies");

            // Temp -> candidate match by geometry
            var emitted = new List<float2[]> { Square(0, 0, 50), Square(100, 0, 50), Square(100.2f, 0.2f, 50) };
            float2[] tempB = Square(100.3f, -0.3f, 50);
            Check(QuickRunsRules.MatchRing(tempB, emitted) == 1, W, "within 0.5 m matches the lowest index");
            Check(QuickRunsRules.MatchRing(Square(103, 0, 50), emitted) == -1, W, "3 m off matches nothing");
            float2[] closed = { tempB[0], tempB[1], tempB[2], tempB[3], tempB[0] };
            Check(QuickRunsRules.MatchRing(closed, emitted) == 1, W, "a closing duplicate is ignored");
            float2[] rotated = { tempB[2], tempB[3], tempB[0], tempB[1] };
            Check(QuickRunsRules.MatchRing(rotated, emitted) == 1, W, "another start node matches");
            float2[] reversed = { tempB[0], tempB[3], tempB[2], tempB[1] };
            Check(QuickRunsRules.MatchRing(reversed, emitted) == 1, W, "the other direction matches");
            float2[] tri = { tempB[0], tempB[1], tempB[2] };
            Check(QuickRunsRules.MatchRing(tri, emitted) == -1, W, "a different node count never matches");
            Check(QuickRunsRules.MatchRing(null, emitted) == -1 && QuickRunsRules.MatchRing(tempB, null) == -1, W, "null safe");

            // Pick: smallest containing ring, else nearest outline within PickM
            var runs = new List<AutoRun>
            {
                new AutoRun { Ring = Square(0, 0, 100), AreaM2 = 10000f },
                new AutoRun { Ring = Square(10, 10, 20), AreaM2 = 400f },
                new AutoRun { Ring = Square(300, 0, 50), AreaM2 = 2500f },
            };
            Check(QuickRunsRules.Pick(new float2(15, 15), runs) == 1, W, "nested: the smaller ring wins");
            Check(QuickRunsRules.Pick(new float2(50, 50), runs) == 0, W, "inside the big ring");
            Check(QuickRunsRules.Pick(new float2(290, 25), runs) == 2, W, "10 m outside picks the nearest outline");
            Check(QuickRunsRules.Pick(new float2(200, 25), runs) == -1, W, "far from every run picks none");

            // Locale keys
            Check(QuickRunsRules.StatusKey(AutoPlanStatus.Ok) == null && QuickRunsRules.StatusKey(AutoPlanStatus.Running) == null, W, "no key for Ok / Running");
            Check(QuickRunsRules.StatusKey(AutoPlanStatus.TooFlat) == "SkiResort.Tooltip.QUICK_STATUS[TooFlat]", W, "status key shape");
            var keys = new List<string> { "stale" };
            QuickRunsRules.WarningKeys(AutoPlanWarning.OverWater | AutoPlanWarning.NoGreen, QuickToolWarning.ChecksOff | QuickToolWarning.Renumber, keys);
            Check(keys.Count == 4 && keys[0] == "SkiResort.Tooltip.QUICK_WARN[NoGreen]" && keys[1] == "SkiResort.Tooltip.QUICK_WARN[OverWater]" &&
                  keys[2] == "SkiResort.Tooltip.QUICK_WARN[Renumber]" && keys[3] == "SkiResort.Tooltip.QUICK_WARN[ChecksOff]", W,
                "warning keys in bit order, planner first: " + string.Join(", ", keys));

            // Knobs
            Check(QuickRunsRules.ClampMix(2) == AutoMix.Expert && QuickRunsRules.ClampMix(7) == AutoMix.Balanced && QuickRunsRules.ClampMix(-1) == AutoMix.Balanced, W, "mix clamp");
            Check(QuickRunsRules.ClampDensity(2) == AutoDensity.Dense && QuickRunsRules.ClampDensity(3) == AutoDensity.Normal, W, "density clamp");
            Check(QuickRunsRules.ClampKinds(0) == AutoKinds.Default && QuickRunsRules.ClampKinds(0x40) == AutoKinds.Default &&
                  QuickRunsRules.ClampKinds(4) == AutoKinds.Snowmaking, W, "kinds clamp");
            Check(QuickRunsRules.ToggleKind(AutoKinds.Default, AutoKinds.Snowmaking) == (AutoKinds.Default | AutoKinds.Snowmaking), W, "kind on");
            Check(QuickRunsRules.ToggleKind(AutoKinds.Default, AutoKinds.Natural) == AutoKinds.Groomed, W, "kind off");
            Check(QuickRunsRules.ToggleKind(AutoKinds.Groomed, AutoKinds.Groomed) == AutoKinds.Groomed, W, "the last kind stays on");

            // Tree line
            Check(QuickRunsRules.ClampTreeLine(float.NaN, 1000, 1400) == 1410 && QuickRunsRules.ClampTreeLine(900, 1000, 1400) == 1000 &&
                  QuickRunsRules.ClampTreeLine(2000, 1000, 1400) == 1410 && QuickRunsRules.ClampTreeLine(1234, 1000, 1400) == 1234, W, "tree-line clamp");
        }
    }
}
