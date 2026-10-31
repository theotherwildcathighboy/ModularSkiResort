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

            // A click on the region: the first corner closes it, and once closed a click past it adds nothing
            {
                Check(QuickRunsRules.RegionClick(false, 0, -1, 0) == QuickRegionClick.AddCorner, W, "region click: first corner adds");
                Check(QuickRunsRules.RegionClick(false, 2, 0, 2) == QuickRegionClick.DragCorner, W, "region click: two corners cannot close, the first drags");
                Check(QuickRunsRules.RegionClick(false, 3, 0, 3) == QuickRegionClick.CloseRegion, W, "region click: first corner of 3 closes");
                Check(QuickRunsRules.RegionClick(false, 3, 1, 3) == QuickRegionClick.DragCorner, W, "region click: another corner drags");
                Check(QuickRunsRules.RegionClick(false, 3, -1, 3) == QuickRegionClick.AddCorner, W, "region click: open, away from it, adds");
                Check(QuickRunsRules.RegionClick(false, 3, -1, 1) == QuickRegionClick.InsertCorner, W, "region click: open, on an edge, inserts");
                Check(QuickRunsRules.RegionClick(true, 4, -1, 4) == QuickRegionClick.Ignore, W, "region click: closed, away from it, does nothing");
                Check(QuickRunsRules.RegionClick(true, 4, -1, 2) == QuickRegionClick.InsertCorner, W, "region click: closed, on an edge, inserts");
                Check(QuickRunsRules.RegionClick(true, 4, 0, 4) == QuickRegionClick.DragCorner, W, "region click: closed, the first corner drags");
            }

            // The site verdict (DECISIONS 443, contract 21): tiers at the boundaries, the figure shown, the locale keys
            {
                var want = new (float grade, int pct, QuickRunsRules.SiteTier tier)[]
                {
                    (0f, 0, QuickRunsRules.SiteTier.Gentle), (0.2449f, 24, QuickRunsRules.SiteTier.Gentle),
                    (0.25f, 25, QuickRunsRules.SiteTier.Steep), (0.4449f, 44, QuickRunsRules.SiteTier.Steep),
                    (0.45f, 45, QuickRunsRules.SiteTier.VerySteep), (0.5749f, 57, QuickRunsRules.SiteTier.VerySteep),
                    (0.58f, 58, QuickRunsRules.SiteTier.Beyond), (2f, 200, QuickRunsRules.SiteTier.Beyond),
                    (float.NaN, -1, QuickRunsRules.SiteTier.Unknown), (-0.1f, -1, QuickRunsRules.SiteTier.Unknown),
                    (float.PositiveInfinity, -1, QuickRunsRules.SiteTier.Unknown),
                };
                foreach (var (grade, pct, tier) in want)
                {
                    int got = QuickRunsRules.SitePercent(grade);
                    Check(got == pct && QuickRunsRules.SiteTierOf(got) == tier, W, $"site verdict: grade {grade} -> {got} % / {QuickRunsRules.SiteTierOf(got)}, want {pct} % / {tier}");
                }
                Check(QuickRunsRules.SiteKey(QuickRunsRules.SiteTier.VerySteep) == "SkiResort.Tooltip.QUICK_SITE[VerySteep]" && QuickRunsRules.SiteKey(QuickRunsRules.SiteTier.Unknown) == null, W,
                    "site verdict locale keys");

                // The corner phase's estimate on planes of known grade (a 1 km square): exact for a plane, capped at 200 %, NaN for a sliver
                float2[] km = Square(-500f, -500f, 1000f);
                foreach (float g in new[] { 0.10f, 0.30f, 0.50f, 0.80f })
                {
                    float est = QuickRunsRules.EstimateMeanGrade(km, p => g * p.x);
                    Check(System.Math.Abs(est - g) < 0.002f, W, $"site estimate on a {g:P0} plane: {est:P1}");
                }
                Check(System.Math.Abs(QuickRunsRules.EstimateMeanGrade(km, p => 5f * p.x) - SkiRunAutoPlanner.MeanGradeCap) < 1e-4f, W, "site estimate caps each sample at MeanGradeCap");
                Check(float.IsNaN(QuickRunsRules.EstimateMeanGrade(Square(0f, 0f, 5f), p => p.x)) && float.IsNaN(QuickRunsRules.EstimateMeanGrade(new[] { new float2(0f, 0f), new float2(9f, 9f) }, p => p.x)) &&
                    float.IsNaN(QuickRunsRules.EstimateMeanGrade(null, p => p.x)), W, "site estimate: NaN for a sliver, two corners or no ring");
                // The sample count stays bounded (a 9 km2 square stays near 300 samples)
                int calls = 0;
                QuickRunsRules.EstimateMeanGrade(Square(-1500f, -1500f, 3000f), p => { calls++; return 0.3f * p.x; });
                Check(calls > 0 && calls <= 5 * 400, W, $"site estimate sampled {calls} heights on 9 km2 (about 5 per point, 300 points)");

                // The scan's figure: the planner's mean over the open ground (set even when the scan is refused later)
                foreach (double g in new[] { 0.15, 0.30, 0.50 })
                {
                    AutoPlanResult scan = Plan(Input(Terrain.Plane(g), Terrain.Square(0f, 0f, 400f)));
                    Check(System.Math.Abs(scan.MeanGrade - g) < 0.02, W, $"scan mean grade on a {g:P0} plane: {scan.MeanGrade:P1} (status {scan.Status})");
                }
            }

            // The hub's "lines dropped" buckets (contract 20): every reason lands in one bucket or none, the sums add up
            {
                var hist = new int[(int)AutoReject.Count];
                for (int i = 1; i < hist.Length; i++)
                    hist[i] = i;   // reason i dropped i lines
                int[] buckets = QuickRunsRules.RejectCounts(hist);
                int dropped = 0, expected = 0;
                for (int i = 0; i < buckets.Length; i++)
                    dropped += buckets[i];
                for (int i = 1; i < hist.Length; i++)
                    if ((AutoReject)i != AutoReject.Duplicate)
                        expected += i;
                Check(buckets.Length == QuickRunsRules.RejectBuckets && dropped == expected, W,
                    $"reject buckets sum {dropped}, want {expected} (every reason but Duplicate)");
                Check(buckets[0] == (int)AutoReject.TooShort + (int)AutoReject.MergeStub && buckets[2] == (int)AutoReject.Region, W,
                    "TooShort + MergeStub are bucket 0, Region is bucket 2");
                Check(QuickRunsRules.RejectBucket(AutoReject.Duplicate) == -1 && QuickRunsRules.RejectBucket(AutoReject.Kept) == -1, W,
                    "Duplicate and Kept are not drops");
                Check(QuickRunsRules.RejectCounts(null).Length == QuickRunsRules.RejectBuckets, W, "a null histogram gives zeros");
            }

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
            Check(QuickRunsRules.StatusKey(AutoPlanStatus.NoVertical, true) == "SkiResort.Tooltip.QUICK_STATUS[OutsideTiles]", W, "no vertical with unowned tiles names the tiles");
            Check(QuickRunsRules.StatusKey(AutoPlanStatus.NoVertical, false) == "SkiResort.Tooltip.QUICK_STATUS[NoVertical]", W, "no vertical on owned ground keeps its reason");
            Check(QuickRunsRules.StatusKey(AutoPlanStatus.Ok, true) == null && QuickRunsRules.StatusKey(AutoPlanStatus.TooLarge, true) == "SkiResort.Tooltip.QUICK_STATUS[TooLarge]", W, "tiles never replace Ok or a size refusal");
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

            // Centroid (F3): the area-weighted polygon centroid, either winding, any start node
            float2 c = QuickRunsRules.Centroid(Square(100, 200, 50));
            Check(math.distance(c, new float2(125, 225)) < 1e-3f, W, $"square centroid (125, 225), got {c}");
            float2[] sq = Square(100, 200, 50);
            System.Array.Reverse(sq);
            Check(math.distance(QuickRunsRules.Centroid(sq), new float2(125, 225)) < 1e-3f, W, "reversed square, same centroid");
            // A tongue: a 20 x 400 m strip (z 0-400) on a 200 x 100 m fan (z 400-500). Vertex average = (100, 325) (eight nodes, four of
            // them on the narrow strip); area-weighted = (8000 x 200 + 20000 x 450) / 28000 = 378.6 in z.
            var tongue = new[]
            {
                new float2(90, 0), new float2(110, 0), new float2(110, 400), new float2(200, 400),
                new float2(200, 500), new float2(0, 500), new float2(0, 400), new float2(90, 400),
            };
            c = QuickRunsRules.Centroid(tongue);
            Check(math.abs(c.x - 100f) < 1e-3f && math.abs(c.y - 10600000f / 28000f) < 1e-2f, W, $"tongue centroid (100, 378.57), got {c}");
            float2 avg = 0f;
            for (int i = 0; i < tongue.Length; i++)
                avg += tongue[i];
            avg /= tongue.Length;
            Check(c.y - avg.y > 50f, W, $"the tongue's centroid lies in the fan, well below the vertex average ({avg.y}): the F3 case");
            // Degenerate: empty / null -> 0; a line (zero area) -> the vertex average; non-finite stays out
            Check(math.all(QuickRunsRules.Centroid(null) == 0f) && math.all(QuickRunsRules.Centroid(new float2[0]) == 0f), W, "null / empty -> 0");
            c = QuickRunsRules.Centroid(new[] { new float2(0, 0), new float2(10, 0), new float2(20, 0) });
            Check(math.distance(c, new float2(10, 0)) < 1e-4f, W, $"zero-area ring -> vertex average (10, 0), got {c}");

            // Game checks (F1): the vanilla error prefab for OverlapExisting, enabled in game, else off; ignoreErrors a second source
            Check(QuickRunsRules.ChecksState(false, 1, 1) == QuickGameChecks.On, W, "an enabled overlap prefab and no ignoreErrors: On");
            Check(QuickRunsRules.ChecksState(false, 1, 0) == QuickGameChecks.OverlapDisabled, W, "the prefab with DisableInGame (Anarchy): OverlapDisabled");
            Check(QuickRunsRules.ChecksState(false, 0, 0) == QuickGameChecks.NoOverlapPrefab, W, "no overlap prefab at all: NoOverlapPrefab");
            Check(QuickRunsRules.ChecksState(true, 1, 1) == QuickGameChecks.IgnoreErrors, W, "ignoreErrors with the prefab on: IgnoreErrors");
            Check(QuickRunsRules.ChecksState(true, 1, 0) == QuickGameChecks.OverlapDisabled, W, "both: the prefab reason wins");
            Check(QuickRunsRules.ChecksState(false, 2, 1) == QuickGameChecks.On, W, "two prefabs, one enabled: On (ValidationSystem keeps the enabled one)");
            Check(!QuickRunsRules.ChecksOff(QuickGameChecks.On) && QuickRunsRules.ChecksOff(QuickGameChecks.IgnoreErrors) &&
                  QuickRunsRules.ChecksOff(QuickGameChecks.OverlapDisabled) && QuickRunsRules.ChecksOff(QuickGameChecks.NoOverlapPrefab), W, "ChecksOff");

            QuickRunsPlanTests();
        }

        // Resort Planner v2 hub rules (QuickRunsPlanRules.cs, task A7): knob clamps, which runs a pod / lift toggle leaves out, the pod rows,
        // the pod ring / lift chord picks and the "plan around this lift" region.
        private static void QuickRunsPlanTests()
        {
            const string W = "quick-plan";
            Check(QuickRunsPlanRules.ClampLifts(-3) == 0 && QuickRunsPlanRules.ClampLifts(0) == 0 && QuickRunsPlanRules.ClampLifts(4) == 4 &&
                  QuickRunsPlanRules.ClampLifts(9) == 9 && QuickRunsPlanRules.ClampLifts(10) == 9, W, "Lifts knob clamps to 0 (Auto) .. 9");
            Check(QuickRunsPlanRules.ClampHome(0) == HomeLevel.Auto && QuickRunsPlanRules.ClampHome(1) == HomeLevel.Green &&
                  QuickRunsPlanRules.ClampHome(2) == HomeLevel.Blue && QuickRunsPlanRules.ClampHome(3) == HomeLevel.Auto &&
                  QuickRunsPlanRules.ClampHome(-1) == HomeLevel.Auto, W, "Home knob: 0 Auto, 1 Green, 2 Blue, anything else Auto");
            Check(QuickRunsPlanRules.Letter(0) == "A" && QuickRunsPlanRules.Letter(11) == "L" && QuickRunsPlanRules.Letter(26) == "A", W, "pod letters A.. by id");

            // Two pods (A: lift 0 proposed, B: lift 1 pinned) and a third pod without a lift
            var plan = new ResortPlan
            {
                Pods = new[]
                {
                    new PlanPod { Id = 0, Key = 100u, Lift = 0, TopCenter = new float2(0, 500) },
                    new PlanPod { Id = 1, Key = 200u, Lift = 1, TopCenter = new float2(800, 500) },
                    new PlanPod { Id = 2, Key = 300u, Lift = -1, TopCenter = new float2(-800, 500) },
                },
                Lifts = new[]
                {
                    new PlanLift { Id = 0, Key = 7u, Origin = LiftOrigin.Proposed, Bottom = new float3(0, 0, 0), Top = new float3(0, 100, 600), Pod = 0 },
                    new PlanLift { Id = 1, Key = 8u, Origin = LiftOrigin.Pinned, Bottom = new float3(800, 0, 0), Top = new float3(800, 100, 600), Pod = 1 },
                    new PlanLift { Id = 2, Key = 9u, Origin = LiftOrigin.Proposed, Bottom = new float3(float.NaN, 0, 0), Top = new float3(float.NaN, 0, 0), Pod = -1 },
                },
                RunPod = new[] { 0, 0, 1, 2, -1 },
                RunLift = new[] { 0, 0, 1, -1, 0 },
            };
            Check(QuickRunsPlanRules.Ordinal(plan, 0) == 1 && QuickRunsPlanRules.Ordinal(plan, 2) == 2 && QuickRunsPlanRules.Ordinal(plan, 1) == 1 &&
                  QuickRunsPlanRules.Ordinal(plan, 5) == 0, W, "lift ordinals count within the origin (P1, P2; the pinned one is its own series)");
            var offPods = new System.Collections.Generic.HashSet<uint>();
            var offLifts = new System.Collections.Generic.HashSet<uint>();
            bool any = false;
            for (int i = 0; i < 5; i++)
                any |= QuickRunsPlanRules.LeftOut(plan, i, offPods, offLifts);
            Check(!any, W, "nothing switched off: no run is left out");
            offPods.Add(100u);
            Check(QuickRunsPlanRules.LeftOut(plan, 0, offPods, offLifts) && QuickRunsPlanRules.LeftOut(plan, 1, offPods, offLifts) &&
                  !QuickRunsPlanRules.LeftOut(plan, 2, offPods, offLifts) && !QuickRunsPlanRules.LeftOut(plan, 3, offPods, offLifts),
                  W, "pod A off: its runs go, the other pods' stay");
            Check(QuickRunsPlanRules.LeftOut(plan, 4, offPods, offLifts) == false, W, "pod A off: a run only seeded by lift 0 but in no pod stays (the lift is not off)");
            offPods.Clear();
            offLifts.Add(7u);
            Check(QuickRunsPlanRules.LeftOut(plan, 0, offPods, offLifts) && QuickRunsPlanRules.LeftOut(plan, 1, offPods, offLifts) &&
                  QuickRunsPlanRules.LeftOut(plan, 4, offPods, offLifts) && !QuickRunsPlanRules.LeftOut(plan, 2, offPods, offLifts),
                  W, "lift P1 off: the runs of its pod and the runs its corridor seeded go");
            Check(!QuickRunsPlanRules.LeftOut(null, 0, offPods, offLifts) && !QuickRunsPlanRules.LeftOut(plan, -1, offPods, offLifts) &&
                  !QuickRunsPlanRules.LeftOut(plan, 99, offPods, offLifts), W, "no plan, a bad run index: never left out");

            // Pod rows: 5 runs, run 1 switched off
            var result = new AutoPlanResult { Resort = plan };
            var bands = new[] { SlopeBand.Green, SlopeBand.Blue, SlopeBand.Black, SlopeBand.DoubleBlack, SlopeBand.Green };
            for (int i = 0; i < 5; i++)
                result.Runs.Add(new AutoRun { Id = i, Band = bands[i], Kind = RunKind.Groomed, AreaM2 = 20000f });
            offLifts.Clear();
            QuickPodRow[] rows = QuickRunsPlanRules.PodRows(result, new[] { true, false, true, true, true }, offPods, offLifts);
            Check(rows.Length == 3 && rows[0].Runs == 2 && rows[0].Kept == 1 && rows[0].Count[(int)SlopeBand.Green] == 1 && rows[0].Count[(int)SlopeBand.Blue] == 0,
                  W, $"pod A: 2 runs, 1 kept (green); got {rows[0].Runs}/{rows[0].Kept}");
            Check(rows[1].Runs == 1 && rows[1].Kept == 1 && rows[1].Count[(int)SlopeBand.Black] == 1 && rows[2].Runs == 1 && rows[2].Count[(int)SlopeBand.DoubleBlack] == 1,
                  W, "pods B / C: their runs and bands");
            Check(rows[0].Fee > 0 && rows[0].Skiers > 0 && rows[0].On && rows[1].On, W, "a kept run has a fee and skiers; pods are on");
            offLifts.Add(8u);
            offPods.Add(300u);
            rows = QuickRunsPlanRules.PodRows(result, null, offPods, offLifts);
            Check(rows[0].On && !rows[1].On && !rows[2].On, W, "pod B is off because its lift is off, pod C because it is off itself");
            Check(QuickRunsPlanRules.PodRows(new AutoPlanResult(), null, offPods, offLifts).Length == 0, W, "no plan: no pod rows");

            // Picks
            Check(QuickRunsPlanRules.NearestLift(plan, new float2(5, 300), 15f) == 0 && QuickRunsPlanRules.NearestLift(plan, new float2(300, 300), 15f) == -1,
                  W, "a click within 15 m of a proposed chord picks its lift, else none");
            Check(QuickRunsPlanRules.NearestLift(plan, new float2(795, 300), 15f) == -1 && QuickRunsPlanRules.NearestLift(plan, new float2(795, 5), 15f) == 1 &&
                  QuickRunsPlanRules.NearestLift(plan, new float2(795, 595), 15f) == 1 && QuickRunsPlanRules.NearestLift(plan, new float2(760, 5), 15f) == -1,
                  W, "a pinned lift is picked only inside its drawn station rings, not along its rope");
            Check(QuickRunsPlanRules.PickAt(plan, null, new float2(5, 300), out int pickAt) == QuickRunsPlanRules.PlanPickKind.Lift && pickAt == 0 &&
                  QuickRunsPlanRules.PickAt(plan, null, new float2(780, 520), out pickAt) == QuickRunsPlanRules.PlanPickKind.Pod && pickAt == 1 &&
                  QuickRunsPlanRules.PickAt(plan, null, new float2(300, 300), out pickAt) == QuickRunsPlanRules.PlanPickKind.None && pickAt == -1,
                  W, "one shared pick: lift chord, else pod ring, else none (runs sit between them: QuickRunsRules.Pick)");
            var pickOffPods = new System.Collections.Generic.HashSet<uint>();
            var pickOffLifts = new System.Collections.Generic.HashSet<uint>();
            Check(QuickRunsPlanRules.LiftOn(plan, 0, pickOffPods, pickOffLifts) && !QuickRunsPlanRules.LiftOn(plan, 5, pickOffPods, pickOffLifts), W, "a lift reads on when neither it nor its pod is off");
            pickOffPods.Add(100u);
            Check(!QuickRunsPlanRules.LiftOn(plan, 0, pickOffPods, pickOffLifts) && QuickRunsPlanRules.LiftOn(plan, 1, pickOffPods, pickOffLifts), W, "a lift row reads off while its pod is off");
            pickOffPods.Clear();
            pickOffLifts.Add(7u);
            Check(!QuickRunsPlanRules.LiftOn(plan, 0, pickOffPods, pickOffLifts), W, "a lift row reads off while the lift is off");
            QuickRunsPlanRules.MixPercents(new[] { 0f, 0.034f, 0.024f, 0.026f, 0.916f }, out int mixG, out int mixB, out int mixK);
            Check(mixG == 3 && mixB == 2 && mixK == 95 && mixG + mixB + mixK == 100, W, $"mix shares add up to the rated total ({mixG}/{mixB}/{mixK})");
            QuickRunsPlanRules.MixPercents(new[] { 0f, 0.5f, 0.25f, 0.125f, 0.125f }, out mixG, out mixB, out mixK);
            Check(mixG == 50 && mixB == 25 && mixK == 25, W, "mix shares: black carries double black");
            Check(QuickRunsPlanRules.NearestLift(plan, new float2(float.NaN, 0), 15f) == -1, W, "a lift without finite stations has no chord to hit");
            Check(QuickRunsPlanRules.NearestPod(plan, new float2(780, 520), 40f) == 1 && QuickRunsPlanRules.NearestPod(plan, new float2(300, 500), 40f) == -1,
                  W, "a click inside a pod ring picks the pod");
            Check(math.abs(QuickRunsPlanRules.SegDistance(new float2(5, 700), new float2(0, 0), new float2(0, 600)) - math.sqrt(25f + 10000f)) < 1e-3f, W, "segment distance clamps to the end point");

            // Plan around this lift: a straight lift from the base (0, 0) up to (0, 1000), fall line south
            float2[] around = QuickRunsPlanRules.AroundRegion(new[] { new float2(0, 0), new float2(0, 500), new float2(0, 1000) }, new float2(0, -1), 9e6f);
            float area = QuickRunsPlanRules.Area(around);
            Check(around != null && math.abs(area - 300f * 1400f) < 300f * 1400f * 0.01f, W, $"corridor 300 m x (1,000 m + 400 m beyond the bottom) = 420,000 m2, got {area:F0}");
            Check(around != null && around.Length == 4 && CatTrack_Inside(around, new float2(0, -300)) && CatTrack_Inside(around, new float2(140, 900)) &&
                  !CatTrack_Inside(around, new float2(200, 500)) && !CatTrack_Inside(around, new float2(0, -450)) && !CatTrack_Inside(around, new float2(0, 1100)),
                  W, "straight lift: 4 corners (collinear chord ends simplified away), the stretch below the base inside, the sides and top outside");
            float2[] shrunk = QuickRunsPlanRules.AroundRegion(new[] { new float2(0, 0), new float2(0, 1000) }, new float2(0, -1), 100000f);
            Check(shrunk != null && QuickRunsPlanRules.Area(shrunk) <= 100000f, W, "an area cap shrinks the corridor's half width until it fits");
            float2[] flatDown = QuickRunsPlanRules.AroundRegion(new[] { new float2(0, 0), new float2(1000, 0) }, float2.zero, 9e6f);
            Check(flatDown != null && CatTrack_Inside(flatDown, new float2(-300, 0)) && !CatTrack_Inside(flatDown, new float2(1100, 0)), W, "no fall line: the lift's own axis extends below the bottom");
            Check(QuickRunsPlanRules.AroundRegion(new[] { new float2(5, 5) }, new float2(0, -1), 9e6f) == null &&
                  QuickRunsPlanRules.AroundRegion(new[] { new float2(5, 5), new float2(5, 5.4f) }, new float2(0, -1), 9e6f) == null, W, "fewer than two distinct points: no region");
            float2[] bent = QuickRunsPlanRules.AroundRegion(new[] { new float2(0, 0), new float2(0, 500), new float2(400, 900), new float2(400, 1400) }, new float2(0, -1), 9e6f);
            Check(bent != null && bent.Length >= 8 && CatTrack_Inside(bent, new float2(200, 700)) && CatTrack_Inside(bent, new float2(400, 1300)),
                  W, "a bent lift keeps its bends (Douglas-Peucker at 12 m) and the corridor follows them");
        }

        private static bool CatTrack_Inside(float2[] poly, float2 p) => GradeRaster.Inside(poly, p);
    }
}
