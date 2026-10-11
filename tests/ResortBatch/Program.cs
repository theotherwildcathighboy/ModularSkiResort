// ResortBatch — headless tests for the pure U2 resort-data files (docs/research/hub-u2-spec.md
// §3.8): EntityRef, SkiResortIndex (§3.1 lift / station membership), SkiResortTourism (§3.4 share
// and estimate) and SkiResortAggregate (§3.3 per-resort sums). Created 2026-10-03 (Round U2, I-1).
// Run (from the ModularSkiResort root):
//   dotnet run --project tests\ResortBatch\ResortBatch.csproj -c Release
// Prints `FAIL  where: msg` lines, report-only INFO lines and one SUMMARY line;
// Environment.ExitCode = failures (the BindingBatch / GladeBatch conventions).
//
// ORACLES are literal hand values, not a second run of the same code:
//   * index — hand-placed signs: nearest, exact ties by distance then Index then Version, the
//     tether edge (exactly 5000 m in, 5000.01 m out, float math.distance as the bind test), two
//     signs, a sign removed (Clear + re-add without it), a moved sign (Add again), NaN / empty.
//   * tourism — TourismSystem.cs:104 evaluated by hand (exp values to 6+ digits in the comments)
//     and GetTargetTourists :171-180 at 99 / 100 / 101 / 150 (the 100 boundary); the modifier is
//     applied BEFORE rounding (a ×1.3 modifier on 1.19995 gives 2, rounding first would give 1);
//     Mathf.RoundToInt's ties-to-even.
//   * aggregation — two resorts + Not linked with literal expected band counts, lengths, areas,
//     vertical with and without stations, lift / lodge counts, snow cover %, season, HasAny, and
//     the per-resort lift ordinals (top height descending, no-top last, ties by Id).
//   * FIXER ROUND (2026-10-03): SkiRunRatingRules (u2-data-01: a node re-snap moves only the y-key
//     and asks a background "terrain" re-rate, an x/z move asks a front "edit"; behaviour-2: a
//     queued warm-up is never promoted by "bind", an edit still promotes it — with a 20-run queue
//     replay; u2-data-05: band -1 pending vs -2 cannot be rated), the aggregate's Unrateable count and
//     verticalApprox (only pending runs), and the resort names (u2-data-02: SmallestFreeOrdinal,
//     FallbackOrdinals — the review's duplicate scenarios, including the recycled-lower-Index case).
//   * ROUND F6 (2026-10-03, implementer A; docs/research/field-test-6-fixes.md A1 / A2): HubCameraRules
//     .AlreadyOn — identical points, inside, exactly on the keep radius (a 180 / 240 offset = 300 m at
//     zoom 1000, inclusive), just outside (300.01 m; zoom 999.9), the 1,500 m zoom cap (1500 on, 1500.5
//     off, both edges at once at 450 m), zero / negative zoom, non-finite input, and the field pair
//     (9,698.24 m apart). The tuning (0.3 / 1500) is checked first: the oracles are literal for it.
//   * ROUND E1 (2026-10-04, docs/research/econ-round/e1-spec.md §4.1; R9: each E1 test file is
//     `public static class <Name>` with `public static void Run(Func<bool, string, string, bool> check)`
//     and its own Near; Main passes its Check, Program stays non-partial):
//     - ROUND E1 ch1: attraction model v2 — sanity table, helpers, monotonicity property (ScoreTests.cs)
//     - ROUND E1 ch2: the rating service's pure rules — duty factors, the background start, the
//       service state machine and its warm-up deadline (RatingServiceTests.cs)
//     - ROUND E1 ch3: snow rules — skiable texel, open state and multiplier, the day-mean estimate,
//       the dry-skip predicate and step scale, the footprint hash (SnowRulesTests.cs)
//     - ROUND E1 ch4: money rules and ledger (MoneyBatch.cs)
//     - ROUND E1 ch5 (2026-10-04): hub rules, problems, piste-map rules, the aggregate's run vertical
//       (HubRulesTests.cs)
//   * ROUND S1 "Ski days" (2026-10-05, docs/research/ski-days/s1-spec.md §4.2; the same R9 convention):
//     - ROUND S1 A: ski-day rules — constants, candidate / lodge / area gates, class, reach, travel time and
//       deadline, chances, rolls, back-off and speed learning, powder, capacity, slices and ARM, day key and
//       month step, slope-side geometry, the F2 power hysteresis, the score additions (SkiDayTests.cs; ch1 §7)
//     - ROUND S1 B: ski-day timing — the recent-CBS-slice window, last pass, next LeisureSystem visit, track
//       index, frames and hours, claim outcome, hour step, bit compares (SkiDayTimingTests.cs; ch2 §14 ST1-ST9)
//     - ROUND S1 C: parking census rules — constants, root, restriction, lane and garage verdicts, hub row 21
//       (ParkingTests.cs; ch3 §7.2 P1, P3-P6)

using System;
using System.Collections.Generic;
using System.Globalization;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace ResortBatch
{
    internal static class Program
    {
        private const int MaxPrinted = 250;
        private static int s_Failures;
        private static long s_Checks;
        private static int s_Printed;

        private static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            EntityRefTests();
            IndexTests();
            TourismTests();
            AggregateTests();
            LiftOrdinalTests();
            RatingKeyTests();
            RatingQueueTests();
            RowBandAndUnrateableTests();
            ResortNameTests();
            HubCameraTests();
            // ROUND E1 (2026-10-04, docs/research/econ-round/e1-spec.md §4.1): one entry per E1 test
            // file, each `public static void Run(Func<bool, string, string, bool> check)`.
            ScoreTests.Run(Check);
            RatingServiceTests.Run(Check);
            SnowRulesTests.Run(Check);
            MoneyBatch.Run(Check);
            HubRulesTests.Run(Check);
            RunOutlineTests.Run(Check);   // ROUND F7 C1 (docs/research/field-test-7-fixes.md): RunOutlineRules
            SkiDayTests.Run(Check);   // ROUND S1 A
            SkiDayTimingTests.Run(Check);   // ROUND S1 B
            ParkingTests.Run(Check);   // ROUND S1 C
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "SUMMARY  ResortBatch checks={0} failures={1}", s_Checks, s_Failures));
            Environment.ExitCode = s_Failures;
            return s_Failures;
        }

        // ================================================================== reporting

        private static void Fail(string where, string msg)
        {
            s_Failures++;
            if (s_Printed < MaxPrinted)
                Console.WriteLine("FAIL  " + where + ": " + msg);
            else if (s_Printed == MaxPrinted)
                Console.WriteLine("FAIL  (further FAIL lines suppressed; see SUMMARY count)");
            s_Printed++;
        }

        private static bool Check(bool cond, string where, string msg)
        {
            s_Checks++;
            if (!cond)
                Fail(where, msg);
            return cond;
        }

        private static bool Near(double a, double b, double eps)
        {
            return Math.Abs(a - b) <= eps;
        }

        // ================================================================== EntityRef

        private static void EntityRefTests()
        {
            Check(default(EntityRef).IsNull && EntityRef.Null.IsNull, "entityref", "default is not Null");
            Check(!new EntityRef(0, 1).IsNull && !new EntityRef(1, 0).IsNull, "entityref", "a half-zero ref reads Null");
            Check(new EntityRef(5, 2) == new EntityRef(5, 2) && new EntityRef(5, 2) != new EntityRef(5, 3), "entityref", "equality is not Index AND Version");
            Check(new EntityRef(5, 2).Equals((object)new EntityRef(5, 2)) && !new EntityRef(5, 2).Equals((object)"5:2"), "entityref", "object Equals");
            Check(new EntityRef(4, 9).CompareTo(new EntityRef(5, 1)) < 0 && new EntityRef(5, 1).CompareTo(new EntityRef(5, 2)) < 0 &&
                  new EntityRef(5, 2).CompareTo(new EntityRef(5, 2)) == 0, "entityref", "order is not Index, then Version");
            Check(new EntityRef(124274, 35).ToString() == "124274:35", "entityref", "ToString " + new EntityRef(124274, 35));
            Check((byte)RunKind.Groomed == 0 && (byte)RunKind.Snowmaking == 1 && (byte)RunKind.Night == 2, "entityref", "RunKind values != KindOf (0/1/2)");
        }

        // ================================================================== SkiResortIndex (§3.1)

        private static void IndexTests()
        {
            const float tether = 5000f;
            var idx = new SkiResortIndex();
            EntityRef a = new EntityRef(10, 1), b = new EntityRef(20, 1), c = new EntityRef(20, 2), d = new EntityRef(7, 3);

            // Empty / NaN.
            Check(!idx.TryResolve(new float2(0f, 0f), tether, out EntityRef s0, out float d0) && s0.IsNull && float.IsPositiveInfinity(d0),
                "index empty", "resolved " + s0 + " at " + d0);
            idx.Add(a, new float2(0f, 0f));
            Check(!idx.TryResolve(new float2(float.NaN, 0f), tether, out EntityRef sn, out float _) && sn.IsNull, "index NaN", "NaN point resolved");

            // Tether edge: exactly 5000 m in, 5000.01 m out (float math.distance).
            Check(idx.TryResolve(new float2(5000f, 0f), tether, out EntityRef s1, out float d1) && s1 == a && d1 == 5000f,
                "index edge", "5000 m: " + s1 + " at " + d1);
            Check(idx.TryResolve(new float2(3000f, 4000f), tether, out EntityRef s1b, out float d1b) && s1b == a && d1b == 5000f,
                "index edge", "(3000, 4000) = 5000 m: " + s1b + " at " + d1b.ToString("R", CultureInfo.InvariantCulture));
            Check(!idx.TryResolve(new float2(5000.01f, 0f), tether, out EntityRef s2, out float d2) && s2.IsNull && Near(d2, 5000.01, 0.001),
                "index edge", "5000.01 m: " + s2 + " at " + d2.ToString("R", CultureInfo.InvariantCulture) + " (distance must still be reported)");

            // Two signs: nearest wins.
            idx.Add(b, new float2(4000f, 0f));
            Check(idx.TryResolve(new float2(2500f, 0f), tether, out EntityRef s3, out float d3) && s3 == b && d3 == 1500f, "index two", "nearest: " + s3 + " at " + d3);
            Check(idx.TryResolve(new float2(1000f, 0f), tether, out EntityRef s4, out float _) && s4 == a, "index two", "nearest: " + s4);

            // Exact tie on distance: lower Index wins, whatever the insertion order.
            Check(idx.TryResolve(new float2(2000f, 0f), tether, out EntityRef s5, out float d5) && s5 == a && d5 == 2000f, "index tie", "tie went to " + s5);
            // Same Index, two versions at the same distance: lower Version wins (c inserted before... any order).
            var idx2 = new SkiResortIndex();
            idx2.Add(c, new float2(-100f, 0f));
            idx2.Add(b, new float2(100f, 0f));
            Check(idx2.TryResolve(new float2(0f, 0f), tether, out EntityRef s6, out float _) && s6 == b, "index tie", "version tie went to " + s6);
            // A nearer sign beats a lower Index.
            idx2.Add(d, new float2(0f, 101f));
            Check(idx2.TryResolve(new float2(0f, 0f), tether, out EntityRef s7, out float _) && s7 == b, "index tie", "lower index but farther won: " + s7);

            // Stable order: sorted by Index, then Version, whatever the insertion order.
            Check(idx2.Count == 3 && idx2.SignAt(0) == d && idx2.SignAt(1) == b && idx2.SignAt(2) == c, "index order",
                "order " + idx2.SignAt(0) + ", " + idx2.SignAt(1) + ", " + idx2.SignAt(2));
            Check(idx2.PositionAt(0).y == 101f && idx2.IndexOf(c) == 2 && idx2.IndexOf(a) == -1, "index order", "positions / IndexOf");

            // A moved sign (Add again) replaces its position, no duplicate.
            idx2.Add(d, new float2(0f, 50f));
            bool r8 = idx2.TryResolve(new float2(0f, 0f), tether, out EntityRef s8, out float d8);
            Check(idx2.Count == 3 && r8 && s8 == d && d8 == 50f,
                "index move", "after moving d: count " + idx2.Count + ", resolved " + s8 + " at " + d8);

            // Removal (the stats rebuild clears and re-adds the live signs).
            idx.Clear();
            Check(idx.Count == 0, "index removal", "Clear left " + idx.Count);
            idx.Add(b, new float2(4000f, 0f));
            Check(idx.TryResolve(new float2(0f, 0f), tether, out EntityRef s9, out float d9) && s9 == b && d9 == 4000f, "index removal",
                "a's lift must re-resolve to b: " + s9 + " at " + d9);
            Check(!idx.TryResolve(new float2(-1001f, 0f), tether, out EntityRef s10, out float _) && s10.IsNull, "index removal", "5001 m from b resolved");
        }

        // ================================================================== SkiResortTourism (§3.4)

        /// <summary>Test copy of TourismSystem.GetTargetTourists (TourismSystem.cs:171-180) with
        /// Mathf.Log10 = (float)Math.Log10((double)f) and Mathf.RoundToInt = (int)Math.Round((double)f)
        /// (UnityEngine.CoreModule IL, read with Cecil).</summary>
        private static int TargetTourists(int attractiveness)
        {
            if (attractiveness <= 100)
                return attractiveness * 15;
            float num = attractiveness - 100;
            float f = 100f * (float)Math.Log10(1f + num);
            return 1500 + (int)Math.Round((double)f);
        }

        private static void TourismTests()
        {
            Check(SkiResortTourism.Logistic(0.0) == 0f, "tourism", "Logistic(0) = " + SkiResortTourism.Logistic(0.0));
            Check(SkiResortTourism.Term(20) == 0.04 && SkiResortTourism.Term(0) == 0.0 && SkiResortTourism.Term(-20) == 0.04, "tourism", "Term");
            Check(SkiResortTourism.SharePct(0.04, 0.08) == 50f && SkiResortTourism.SharePct(0.04, 0.0) == 0f && SkiResortTourism.SharePct(0.0, 0.08) == 0f &&
                  SkiResortTourism.SharePct(1.0, double.NaN) == 0f, "tourism", "SharePct");
            Check(SkiResortTourism.RoundToInt(2.5f) == 2 && SkiResortTourism.RoundToInt(3.5f) == 4 && SkiResortTourism.RoundToInt(-0.5f) == 0 &&
                  SkiResortTourism.RoundToInt(1.4999f) == 1, "tourism", "RoundToInt is not Mathf.RoundToInt (ties to even)");

            // GetTargetTourists copy at the 100 boundary (hand: 99·15, 100·15, 1500 + round(100·log10 2 = 30.10),
            // 1500 + round(100·log10 51 = 170.76)).
            Check(TargetTourists(99) == 1485 && TargetTourists(100) == 1500 && TargetTourists(101) == 1530 && TargetTourists(150) == 1671,
                "tourism target", "99→" + TargetTourists(99) + " 100→" + TargetTourists(100) + " 101→" + TargetTourists(101) + " 150→" + TargetTourists(150));

            Func<float, float> identity = x => x;

            // A: a sign (20) and its lodge (20) in an otherwise empty city. s = 0.08:
            //    200 / (1 + e^-0.024 = 1.9762857) − 100 = 1.19995 → 1; without the resort s = 0 → 0.
            double sA = SkiResortTourism.Term(20) + SkiResortTourism.Term(20);
            SkiResortTourism.Estimate(sA, sA, identity, TargetTourists, out int aNow, out int aWo, out int tNow, out int tWo);
            Check(Near(SkiResortTourism.Logistic(sA), 1.19995, 1e-4), "tourism A", "Logistic(0.08) = " + SkiResortTourism.Logistic(sA));
            Check(aNow == 1 && aWo == 0 && tNow == 15 && tWo == 0, "tourism A", $"now {aNow}/{tNow}, without {aWo}/{tWo}");

            // A with a ×1.3 modifier: applied BEFORE rounding → round(1.55994) = 2 (rounding first would give 1).
            SkiResortTourism.Estimate(sA, sA, x => x * 1.3f, TargetTourists, out int bNow, out int bWo, out int _, out int _);
            Check(bNow == 2 && bWo == 0, "tourism modifier order", $"×1.3: now {bNow}, without {bWo} (want 2 / 0)");

            // B: s = 10 (e.g. ten providers of 100): 200 / (1 + e^-3 = 1.0497871) − 100 = 90.5148 → 91 → 1365 tourists;
            //    the resort holds one provider of 100 (Term 1) → s = 9: 200 / (1 + e^-2.7 = 1.0672055) − 100 = 87.4056 → 87 → 1305.
            SkiResortTourism.Estimate(10.0, SkiResortTourism.Term(100), identity, TargetTourists, out int cNow, out int cWo, out int ctNow, out int ctWo);
            Check(cNow == 91 && cWo == 87 && ctNow == 1365 && ctWo == 1305, "tourism B", $"now {cNow}/{ctNow}, without {cWo}/{ctWo} (want 91/1365, 87/1305)");
            Check(Near(SkiResortTourism.SharePct(SkiResortTourism.Term(100), 10.0), 10.0, 1e-5), "tourism B", "share of 1 in 10");

            // C: the 100 boundary. s = 100: e^-30 ≈ 9.4e-14 vanishes in float → exactly 100 → 1500 tourists;
            //    with a ×1.5 city modifier → 150 → 1671 (past the boundary the log branch applies).
            SkiResortTourism.Estimate(100.0, 0.0, identity, TargetTourists, out int dNow, out int dWo, out int dtNow, out int dtWo);
            Check(dNow == 100 && dWo == 100 && dtNow == 1500 && dtWo == 1500, "tourism C", $"s=100: {dNow}/{dtNow}, without {dWo}/{dtWo}");
            SkiResortTourism.Estimate(100.0, 100.0, x => x * 1.5f, TargetTourists, out int eNow, out int eWo, out int etNow, out int etWo);
            Check(eNow == 150 && eWo == 0 && etNow == 1671 && etWo == 0, "tourism C", $"×1.5: {eNow}/{etNow}, without {eWo}/{etWo}");

            // The resort larger than the walk (stale input) never goes negative; null delegates.
            SkiResortTourism.Estimate(0.08, 5.0, null, null, out int fNow, out int fWo, out int ftNow, out int ftWo);
            Check(fNow == 1 && fWo == 0 && ftNow == 0 && ftWo == 0, "tourism edge", $"sRes > sAll / null delegates: {fNow}/{fWo}/{ftNow}/{ftWo}");
        }

        // ================================================================== SkiResortAggregate (§3.3)

        private static SkiResortAggregate.RunIn Run(int resort, bool rated, sbyte band, float len, float area, float top, float bottom,
            float cost, float snowHa, float cover, float natural)
        {
            return new SkiResortAggregate.RunIn
            {
                Resort = resort, RateState = (byte)(rated ? 1 : 0), Band = rated ? band : (sbyte)-1, LengthM = len, AreaM2 = area,
                TopM = top, BottomM = bottom, CostMonth = cost, SnowHa = snowHa, Cover = cover, NaturalCover = natural,
            };
        }

        private static SkiResortAggregate.LiftIn Lift(int resort, int id, byte state, int chairs, float len, float rise, float baseY, float topY)
        {
            return new SkiResortAggregate.LiftIn
            {
                Resort = resort, Id = new EntityRef(id, 1), State = state, Chairs = chairs, LengthM = len, RiseM = rise, BaseTipY = baseY, TopTipY = topY,
            };
        }

        private static void AggregateTests()
        {
            var runs = new List<SkiResortAggregate.RunIn>
            {
                Run(0, true, 2, 500f, 20000f, 1500f, 1300f, 100f, 1.0f, 1.0f, 0.0f),      // Blue, snowmaking cover only
                Run(0, true, 3, 300f, 10000f, 1600f, 1450f, 40f, 0.5f, 0.5f, 0.5f),      // Black, natural cover half
                Run(0, false, -1, 200f, 5000f, 1400f, 1350f, 0f, 0f, 0f, 0f),            // pending (node estimates)
                Run(-1, false, -1, 100f, 3000f, 900f, 850f, 0f, 0f, float.NaN, float.NaN), // Not linked, no cover
                Run(1, true, 1, 250f, 8000f, 2000f, 1900f, 0f, 0f, 0f, 0f),              // Green, too warm
                Run(7, true, 4, 50f, 1000f, 700f, 650f, 0f, 0f, float.NaN, float.NaN),   // out-of-range resort → Not linked
            };
            var lifts = new List<SkiResortAggregate.LiftIn>
            {
                Lift(0, 1, 0, 40, 800f, 300f, 1250f, 1700f),        // running
                Lift(0, 2, 2, 30, 500f, 200f, 1300f, 1650f),        // a missing section
                Lift(-1, 3, 4, 0, 0f, 0f, 950f, float.NaN),         // a base station alone, Not linked
            };
            var lodges = new List<SkiResortAggregate.LodgeIn>
            {
                new SkiResortAggregate.LodgeIn { Resort = 0, Score = 25, Free = false },
                new SkiResortAggregate.LodgeIn { Resort = -1, Score = 10, Free = true },
            };
            var result = new SkiResortAggregate.Agg[3];
            SkiResortAggregate.Aggregate(runs, lifts, lodges, 2, result);

            SkiResortAggregate.Agg r0 = result[0], r1 = result[1], nl = result[2];
            string w = "aggregate resort 0";
            Check(r0.HasAny && r0.Runs == 3 && r0.Rated == 2 && r0.Flat == 0 && r0.Green == 0 && r0.Blue == 1 && r0.Black == 1 && r0.DoubleBlack == 0, w,
                $"bands runs {r0.Runs} rated {r0.Rated} F{r0.Flat} G{r0.Green} B{r0.Blue} K{r0.Black} DK{r0.DoubleBlack}");
            Check(r0.RunLengthM == 1000f && r0.AreaM2 == 35000f && r0.RunCostMonth == 140f && r0.SnowHa == 1.5f, w,
                $"length {r0.RunLengthM} area {r0.AreaM2} cost {r0.RunCostMonth} snowHa {r0.SnowHa}");
            // Vertical WITH stations: max(1500, 1600, 1400, tops 1700 / 1650) − min(1300, 1450, 1350, bases 1250 / 1300) = 1700 − 1250.
            Check(r0.VerticalM == 450f && r0.VerticalApprox, w, $"vertical {r0.VerticalM} approx {r0.VerticalApprox} (want 450, approx: one run pending)");
            Check(r0.Lifts == 2 && r0.LiftsRunning == 1 && r0.LiftsOpen == 1 && r0.Chairs == 70 && r0.LiftLengthM == 1300f && r0.LiftRiseM == 500f, w,
                $"lifts {r0.Lifts} running {r0.LiftsRunning} open {r0.LiftsOpen} chairs {r0.Chairs} length {r0.LiftLengthM} rise {r0.LiftRiseM}");
            Check(r0.Lodges == 1 && r0.FreeLodges == 0 && r0.LodgeScore == 25, w, $"lodges {r0.Lodges} free {r0.FreeLodges} score {r0.LodgeScore}");
            // Snow: (20000·1 + 10000·0.5 + 5000·0) / 35000 = 71.43 %; natural cover on the black run → Natural.
            Check(Near(r0.SnowPct, 100.0 * 25000.0 / 35000.0, 1e-3) && r0.Season == SkiResortAggregate.SeasonNatural, w,
                $"snow {r0.SnowPct} season {r0.Season}");

            w = "aggregate resort 1";
            // Vertical WITHOUT stations: 2000 − 1900, all rated → not approximate; no lodge → −1; cover 0 → too warm.
            Check(r1.HasAny && r1.Runs == 1 && r1.Rated == 1 && r1.Green == 1 && r1.VerticalM == 100f && !r1.VerticalApprox, w,
                $"runs {r1.Runs} green {r1.Green} vertical {r1.VerticalM} approx {r1.VerticalApprox}");
            Check(r1.Lifts == 0 && r1.Lodges == 0 && r1.LodgeScore == -1 && r1.SnowPct == 0f && r1.Season == SkiResortAggregate.SeasonTooWarm, w,
                $"lifts {r1.Lifts} lodges {r1.Lodges} score {r1.LodgeScore} snow {r1.SnowPct} season {r1.Season}");

            w = "aggregate not linked";
            // The pending run (900/850) + the out-of-range Double Black (700/650) + the lone base station at 950:
            // Hi = 900 (a base station never raises the top), Lo = 650.
            Check(nl.HasAny && nl.Runs == 2 && nl.Rated == 1 && nl.DoubleBlack == 1 && nl.VerticalM == 250f && nl.VerticalApprox, w,
                $"runs {nl.Runs} rated {nl.Rated} DK {nl.DoubleBlack} vertical {nl.VerticalM} approx {nl.VerticalApprox}");
            Check(nl.Lifts == 1 && nl.LiftsRunning == 0 && nl.LiftsOpen == 0 && nl.Lodges == 1 && nl.FreeLodges == 1 && nl.LodgeScore == 10, w,
                $"lifts {nl.Lifts} lodges {nl.Lodges} free {nl.FreeLodges} score {nl.LodgeScore}");
            Check(nl.SnowPct == -1f && nl.Season == SkiResortAggregate.SeasonNone, w, $"no cover → snow {nl.SnowPct} season {nl.Season}");

            // Vertical from stations alone; an empty Not linked row; nothing at all.
            var onlyLift = new List<SkiResortAggregate.LiftIn> { Lift(0, 9, 1, 0, 400f, 120f, 1000f, 1120f) };
            var res2 = new SkiResortAggregate.Agg[2];
            SkiResortAggregate.Aggregate(new List<SkiResortAggregate.RunIn>(), onlyLift, null, 1, res2);
            Check(res2[0].VerticalM == 120f && !res2[0].VerticalApprox && res2[0].Lifts == 1 && res2[0].LiftsRunning == 0, "aggregate stations only",
                $"vertical {res2[0].VerticalM} approx {res2[0].VerticalApprox}");
            Check(!res2[1].HasAny && res2[1].Runs == 0 && res2[1].VerticalM == -1f && res2[1].LodgeScore == -1 && res2[1].SnowPct == -1f &&
                  res2[1].Season == SkiResortAggregate.SeasonNone, "aggregate empty not linked",
                $"hasAny {res2[1].HasAny} vertical {res2[1].VerticalM} score {res2[1].LodgeScore} snow {res2[1].SnowPct}");
            var res3 = new SkiResortAggregate.Agg[1];
            SkiResortAggregate.Aggregate(null, null, null, 0, res3);
            Check(!res3[0].HasAny && res3[0].VerticalM == -1f, "aggregate no signs", "the lone Not linked row must be empty");
            // A top station alone (no base): Hi only → vertical unknown.
            var topOnly = new List<SkiResortAggregate.LiftIn> { Lift(0, 9, 4, 0, 0f, 0f, float.NaN, 1500f) };
            SkiResortAggregate.Aggregate(null, topOnly, null, 1, res2);
            Check(res2[0].VerticalM == -1f, "aggregate top only", "vertical " + res2[0].VerticalM);
            // A snowmaking-only resort (cover from the guns, no natural cover) → season 1.
            var gunsOnly = new List<SkiResortAggregate.RunIn> { Run(0, true, 1, 100f, 1000f, 1100f, 1000f, 0f, 0f, 1f, 0f) };
            SkiResortAggregate.Aggregate(gunsOnly, null, null, 1, res2);
            Check(res2[0].Season == SkiResortAggregate.SeasonSnowmakingOnly && res2[0].SnowPct == 100f, "aggregate season", "season " + res2[0].Season);
        }

        private static void LiftOrdinalTests()
        {
            var lifts = new List<SkiResortAggregate.LiftIn>
            {
                Lift(0, 10, 0, 0, 0f, 0f, 1000f, 1700f),        // ties with id 7 on height → 2nd
                Lift(0, 5, 0, 0, 0f, 0f, 1000f, 1650f),         // 3rd
                Lift(-1, 3, 4, 0, 0f, 0f, 950f, float.NaN),     // Not linked: 1st of its own
                Lift(0, 3, 3, 0, 0f, 0f, 900f, float.NaN),      // no top → last
                Lift(0, 7, 0, 0, 0f, 0f, 1000f, 1700f),         // 1st (lower Id on the tie)
                Lift(1, 99, 0, 0, 0f, 0f, 1000f, 1200f),        // resort 1: 1st
            };
            var order = new List<int>();
            var ordinals = new List<int>();
            SkiResortAggregate.LiftOrdinals(lifts, order, ordinals);
            int[] want = { 2, 3, 1, 4, 1, 1 };
            for (int k = 0; k < want.Length; k++)
                Check(ordinals[k] == want[k], "lift ordinals", "lift " + k + " (id " + lifts[k].Id + ") got " + ordinals[k] + ", want " + want[k]);
        }

        // ================================================================== fixer round: rating rules

        private static float3[] Ring()
        {
            // A 4-node run outline, as drawn (x/z), on terrain heights (y).
            return new[]
            {
                new float3(100f, 812.25f, 200f), new float3(140f, 790.5f, 205f),
                new float3(150f, 650.125f, 520f), new float3(95f, 660f, 515f),
            };
        }

        private static void RatingKeyTests()
        {
            const string W = "rating keys";
            float3[] a = Ring();
            SkiRunRatingRules.Keys(a, a.Length, out ulong k0, out ulong y0);

            // A GroundHeightSystem re-snap moves y only (AreaUtils.AdjustPosition): Key stays, YKey moves.
            float3[] snapped = Ring();
            for (int i = 0; i < snapped.Length; i++)
                snapped[i].y += 0.37f;
            SkiRunRatingRules.Keys(snapped, snapped.Length, out ulong k1, out ulong y1);
            Check(k1 == k0, W, "a y-only re-snap changed the x/z key");
            Check(y1 != y0, W, "a y-only re-snap did not change the y key");

            // One node's y by the smallest step GroundHeightSystem applies (1 cm): still x/z-equal.
            float3[] one = Ring();
            one[2].y += 0.01f;
            SkiRunRatingRules.Keys(one, one.Length, out ulong k2, out ulong y2);
            Check(k2 == k0 && y2 != y0, W, "a 1 cm re-snap of one node: key equal, y key different");

            // An outline edit (x or z of one node) changes the key.
            float3[] movedX = Ring();
            movedX[1].x += 0.5f;
            SkiRunRatingRules.Keys(movedX, movedX.Length, out ulong k3, out ulong _);
            float3[] movedZ = Ring();
            movedZ[3].z -= 0.5f;
            SkiRunRatingRules.Keys(movedZ, movedZ.Length, out ulong k4, out ulong _);
            Check(k3 != k0 && k4 != k0 && k3 != k4, W, "an x or z move must change the key");

            // A node added (even a duplicate) changes both keys; n beyond the array is clamped.
            float3[] five = { a[0], a[1], a[2], a[3], a[3] };
            SkiRunRatingRules.Keys(five, 5, out ulong k5, out ulong y5);
            Check(k5 != k0 && y5 != y0, W, "a fifth node must change both keys");
            SkiRunRatingRules.Keys(a, 99, out ulong k6, out ulong y6);
            Check(k6 == k0 && y6 == y0, W, "n > length must clamp to the array");
            SkiRunRatingRules.Keys(a, 0, out ulong k7, out ulong _);
            SkiRunRatingRules.Keys(null, 3, out ulong k8, out ulong _);
            Check(k7 == k8 && k7 != k0, W, "empty and null rings key alike");

            // Incremental form (the catalog's RingMetrics) = the array form (RateOne's KeyOf).
            SkiRunRatingRules.Begin(out ulong ki, out ulong yi);
            for (int i = 0; i < a.Length; i++)
                SkiRunRatingRules.AddNode(ref ki, ref yi, a[i]);
            SkiRunRatingRules.Finish(ref ki, ref yi, a.Length);
            Check(ki == k0 && yi == y0, W, "Begin/AddNode/Finish differs from Keys");

            // NeedsRating: the cache decisions.
            const byte None = SkiRunRatingRules.ReasonNone, Bind = SkiRunRatingRules.ReasonBind, Warm = SkiRunRatingRules.ReasonWarmup;
            const byte Edit = SkiRunRatingRules.ReasonEdit, Terrain = SkiRunRatingRules.ReasonTerrain;
            Check(SkiRunRatingRules.NeedsRating(false, 0, 0, 0, k0, y0, 0, Bind) == Bind, W, "uncached → the missing reason (bind)");
            Check(SkiRunRatingRules.NeedsRating(false, 0, 0, 0, k0, y0, 0, Warm) == Warm, W, "uncached → the missing reason (warm-up)");
            Check(SkiRunRatingRules.NeedsRating(true, k0, y0, 3, k0, y0, 3, Bind) == None, W, "same keys, same epoch → nothing");
            Check(SkiRunRatingRules.NeedsRating(true, k0, y0, 3, k0, y1, 3, Bind) == Terrain, W, "y key moved → terrain (background), not edit");
            Check(SkiRunRatingRules.NeedsRating(true, k0, y0, 2, k0, y0, 3, Bind) == Terrain, W, "older terrain epoch → terrain");
            Check(SkiRunRatingRules.NeedsRating(true, k0, y0, 3, k3, y0, 3, Bind) == Edit, W, "x/z key moved → edit");
            Check(SkiRunRatingRules.NeedsRating(true, k0, y0, 2, k3, y1, 3, Bind) == Edit, W, "an edit wins over terrain");

            // The finding's scenario: a brush stroke re-snaps the nodes 60 passes in a row. Every pass must
            // ask for a BACKGROUND rating and never a front one (no per-frame full rating, the row keeps
            // its rating: ApplyRating matches on the x/z key alone).
            float3[] stroke = Ring();
            int front = 0, terrain = 0;
            for (int pass = 1; pass <= 60; pass++)
            {
                for (int i = 0; i < stroke.Length; i++)
                    stroke[i].y += 0.02f * pass;
                SkiRunRatingRules.Keys(stroke, stroke.Length, out ulong ks, out ulong ys);
                byte r = SkiRunRatingRules.NeedsRating(true, k0, y0, 0, ks, ys, 0, Bind);
                if (SkiRunRatingRules.IsFront(r)) front++;
                if (r == Terrain) terrain++;
                Check(ks == k0, W, "stroke pass " + pass + ": the x/z key moved (the row would go pending)");
            }
            Check(front == 0 && terrain == 60, W, $"stroke: {front} front / {terrain} terrain requests (want 0 / 60)");
        }

        private static void RatingQueueTests()
        {
            const string W = "rating queue";
            const byte Warm = SkiRunRatingRules.ReasonWarmup, Bind = SkiRunRatingRules.ReasonBind, Edit = SkiRunRatingRules.ReasonEdit;
            const byte Terrain = SkiRunRatingRules.ReasonTerrain, Back = SkiRunRatingRules.ReasonBackstop;
            Check(SkiRunRatingRules.IsFront(Bind) && SkiRunRatingRules.IsFront(Edit) && !SkiRunRatingRules.IsFront(Warm) &&
                  !SkiRunRatingRules.IsFront(Terrain) && !SkiRunRatingRules.IsFront(Back), W, "front = bind and edit only");
            Check(!SkiRunRatingRules.Replaces(Warm, Bind), W, "bind must not promote a queued warm-up (behaviour-2)");
            Check(SkiRunRatingRules.Replaces(Warm, Edit), W, "an edit promotes a queued warm-up");
            Check(SkiRunRatingRules.Replaces(Terrain, Edit) && SkiRunRatingRules.Replaces(Back, Edit) && SkiRunRatingRules.Replaces(Terrain, Bind),
                W, "front over background (terrain / backstop)");
            Check(!SkiRunRatingRules.Replaces(Bind, Edit) && !SkiRunRatingRules.Replaces(Edit, Bind) && !SkiRunRatingRules.Replaces(Edit, Edit),
                W, "a front entry keeps its place");
            Check(!SkiRunRatingRules.Replaces(Warm, Terrain) && !SkiRunRatingRules.Replaces(Terrain, Warm) &&
                  !SkiRunRatingRules.Replaces(Terrain, Terrain) && !SkiRunRatingRules.Replaces(Back, Terrain), W, "background never replaces");

            // Replay of the finding: 20 runs seeded as warm-up, then every later rebuild re-asks each
            // unrated row as "bind" (SkiRunCatalogSystem.UpdateQueue). The catalog's Enqueue logic, here
            // over ints: nothing may reach the front; one edit later promotes exactly that run.
            var queued = new Dictionary<int, byte>();
            int frontCount = 0, backCount = 0, promotedFromWarmup = 0;
            void Enqueue(int run, byte reason)
            {
                bool f = SkiRunRatingRules.IsFront(reason);
                if (queued.TryGetValue(run, out byte q))
                {
                    if (!SkiRunRatingRules.Replaces(q, reason))
                        return;
                    if (q == Warm)
                        promotedFromWarmup++;
                }
                queued[run] = reason;
                if (f) frontCount++; else backCount++;
            }
            for (int run = 0; run < 20; run++)
                Enqueue(run, Warm);
            for (int rebuild = 0; rebuild < 5; rebuild++)
                for (int run = 0; run < 20; run++)
                    Enqueue(run, Bind);
            Check(frontCount == 0 && backCount == 20 && promotedFromWarmup == 0, W,
                $"after 5 bind passes: front {frontCount}, back {backCount}, promoted {promotedFromWarmup} (want 0 / 20 / 0)");
            Enqueue(7, Edit);
            Check(frontCount == 1 && promotedFromWarmup == 1 && queued[7] == Edit, W, "an edit of a waiting run promotes it once");
            Enqueue(21, Bind);   // a run drawn after the warm-up seed: front, as before
            Check(frontCount == 2 && queued[21] == Bind, W, "a new run after the seed is a front bind");
        }

        private static void RowBandAndUnrateableTests()
        {
            const string W = "row band";
            Check(SkiRunRatingRules.RowBand(SkiRunRatingRules.RatePending, -1) == SkiRunRatingRules.BandPending, W, "pending → -1");
            Check(SkiRunRatingRules.RowBand(SkiRunRatingRules.RateRated, 3) == 3 && SkiRunRatingRules.RowBand(SkiRunRatingRules.RateRated, 0) == 0,
                W, "rated → its band");
            Check(SkiRunRatingRules.RowBand(SkiRunRatingRules.RateNoTerrain, 2) == SkiRunRatingRules.BandUnrateable &&
                  SkiRunRatingRules.RowBand(SkiRunRatingRules.RateTooLarge, -1) == SkiRunRatingRules.BandUnrateable &&
                  SkiRunRatingRules.RowBand(SkiRunRatingRules.RateFailed, -1) == SkiRunRatingRules.BandUnrateable, W, "states 2/3/4 → -2");
            Check(SkiRunRatingRules.BandUnrateable == -2 && SkiRunRatingRules.BandPending == -1, W, "band values = bindings.ts BAND");
            Check(!SkiRunRatingRules.IsUnrateable(0) && !SkiRunRatingRules.IsUnrateable(1) && SkiRunRatingRules.IsUnrateable(2) &&
                  SkiRunRatingRules.IsUnrateable(3) && SkiRunRatingRules.IsUnrateable(4), W, "IsUnrateable");

            // Aggregate: an unrateable run is neither rated nor pending, and does not keep "≈".
            var runs = new List<SkiResortAggregate.RunIn>
            {
                Run(0, true, 2, 100f, 1000f, 1200f, 1000f, 0f, 0f, 0f, 0f),
                new SkiResortAggregate.RunIn { Resort = 0, RateState = 3, Band = -2, LengthM = 7100f, AreaM2 = 198000f, TopM = 1300f, BottomM = 900f, Cover = 0f, NaturalCover = 0f },
                new SkiResortAggregate.RunIn { Resort = 0, RateState = 4, Band = -2, LengthM = 10f, AreaM2 = 20f, TopM = 1100f, BottomM = 1090f, Cover = 0f, NaturalCover = 0f },
            };
            var res = new SkiResortAggregate.Agg[2];
            SkiResortAggregate.Aggregate(runs, null, null, 1, res);
            SkiResortAggregate.Agg a = res[0];
            Check(a.Runs == 3 && a.Rated == 1 && a.Unrateable == 2 && a.Runs - a.Rated - a.Unrateable == 0, W,
                $"runs {a.Runs} rated {a.Rated} unrateable {a.Unrateable} (want 3 / 1 / 2, pending 0)");
            Check(!a.VerticalApprox && a.VerticalM == 400f, W, $"vertical {a.VerticalM} approx {a.VerticalApprox} (want 400, not approx: no run pending)");
            runs.Add(Run(0, false, -1, 50f, 500f, 1150f, 1120f, 0f, 0f, 0f, 0f));   // one pending run → "≈"
            SkiResortAggregate.Aggregate(runs, null, null, 1, res);
            Check(res[0].VerticalApprox && res[0].Unrateable == 2 && res[0].Runs - res[0].Rated - res[0].Unrateable == 1, W,
                "a pending run sets approx and counts as the one pending");
        }

        private static void ResortNameTests()
        {
            const string W = "resort names";
            Func<int, string> fmt = n => "Ski area " + n.ToString(CultureInfo.InvariantCulture);
            var taken = new HashSet<string>(StringComparer.Ordinal);

            // SmallestFreeOrdinal (the auto-name and the empty-rename pick).
            Check(SkiResortAggregate.SmallestFreeOrdinal(taken, fmt, 100) == 1, W, "nothing taken → 1");
            taken.Add("Ski area 1");
            taken.Add("Ski area 3");
            taken.Add("Copper Bowl");
            Check(SkiResortAggregate.SmallestFreeOrdinal(taken, fmt, 100) == 2, W, "1 and 3 taken → 2");
            taken.Add("Ski area 2");
            Check(SkiResortAggregate.SmallestFreeOrdinal(taken, fmt, 100) == 4, W, "1-3 taken → 4");
            Check(SkiResortAggregate.SmallestFreeOrdinal(taken, fmt, 3) == -1 && SkiResortAggregate.SmallestFreeOrdinal(taken, null, 9) == -1,
                W, "nothing free up to maxN, or no formatter → -1");

            // The review's scenario (u2-data-02): A is auto-named 'Ski area 1', then renamed to "" — the hub
            // now writes a FRESH auto-name excluding A itself: 'Ski area 1' again (no other sign has it).
            taken.Clear();   // other live signs' custom names: none
            Check(SkiResortAggregate.SmallestFreeOrdinal(taken, fmt, 100) == 1, W, "empty rename of the only sign → 'Ski area 1'");
            // ... and a sign B placed later gets 'Ski area 2' (A's written name is taken) — no duplicate.
            taken.Add("Ski area 1");
            Check(SkiResortAggregate.SmallestFreeOrdinal(taken, fmt, 100) == 2, W, "the next sign → 'Ski area 2'");

            // FallbackOrdinals (the hub's display-only label of a sign with no name).
            var names = new List<string>();
            var ordinals = new List<int>();
            // [B named 'Ski area 2' (lower Index, first), A unnamed]: A must not read 'Ski area 2' (the
            // positional rule did: position 2) — it gets 1.
            names.Add("Ski area 2");
            names.Add(null);
            SkiResortAggregate.FallbackOrdinals(names, fmt, taken, ordinals, 100);
            Check(ordinals.Count == 2 && ordinals[0] == 0 && ordinals[1] == 1, W, $"[named 2, unnamed] → [0, {Get(ordinals, 1)}] (want 0, 1)");
            // [A unnamed, B named 'Ski area 1']: A gets 2, not 1.
            names.Clear();
            names.Add(null);
            names.Add("Ski area 1");
            SkiResortAggregate.FallbackOrdinals(names, fmt, taken, ordinals, 100);
            Check(ordinals[0] == 2 && ordinals[1] == 0, W, $"[unnamed, named 1] → [{Get(ordinals, 0)}, 0] (want 2, 0)");
            // Three unnamed + a custom 'Ski area 2': 1, 3, 4 (each earlier label is taken too).
            names.Clear();
            names.Add(null);
            names.Add("Ski area 2");
            names.Add(null);
            names.Add("");
            names.Add("Copper");
            SkiResortAggregate.FallbackOrdinals(names, fmt, taken, ordinals, 100);
            Check(ordinals.Count == 5 && ordinals[0] == 1 && ordinals[1] == 0 && ordinals[2] == 3 && ordinals[3] == 4 && ordinals[4] == 0, W,
                $"[_, 'Ski area 2', _, '', 'Copper'] → [{Get(ordinals, 0)}, {Get(ordinals, 1)}, {Get(ordinals, 2)}, {Get(ordinals, 3)}, {Get(ordinals, 4)}] (want 1, 0, 3, 4, 0)");
            // No label is ever shown twice: every unnamed label differs from every custom name and each other.
            var shown = new HashSet<string>(StringComparer.Ordinal);
            bool unique = true;
            for (int i = 0; i < names.Count; i++)
            {
                string label = string.IsNullOrEmpty(names[i]) ? fmt(ordinals[i]) : names[i];
                unique &= shown.Add(label);
            }
            Check(unique, W, "two resorts show the same name");
        }

        private static int Get(List<int> list, int i)
        {
            return i < list.Count ? list[i] : int.MinValue;
        }

        // ================================================================== HubCameraRules (Round F6, A1 / A2)

        private static void HubCameraTests()
        {
            const string W = "hub camera";
            // The literal oracles below assume the shipped tuning: a change of either constant must fail here
            // first, so the cases are re-derived by hand rather than silently re-scaled.
            Check(HubCameraRules.KeepZoomFraction == 0.3f && HubCameraRules.KeepMaxZoomM == 1500f, W,
                $"tuning {HubCameraRules.KeepZoomFraction} / {HubCameraRules.KeepMaxZoomM} (the oracles assume 0.3 / 1500)");
            // 0.3f × 1000f rounds to exactly 300f (0.300000011920929 × 1000 = 300.0000119; the float ulp at 300 is
            // 3.05e-5, so the product rounds down to 300): the edge cases below sit exactly on the keep radius.
            Check(HubCameraRules.KeepRadiusM(1000f) == 300f, W,
                "keep radius at zoom 1000 = " + HubCameraRules.KeepRadiusM(1000f).ToString("R", CultureInfo.InvariantCulture));

            float2 sign = new float2(5824f, -5691f);   // the field's 'Ski area 1' sign (log 629, 1120)
            float d;

            // Identical points: the state right after a fly (the orbit camera follows the sign, zoom 150).
            Check(HubCameraRules.AlreadyOn(sign, 150f, sign, out d) && d == 0f, W, $"pivot on the sign, zoom 150: d {d}");
            Check(HubCameraRules.AlreadyOn(sign, 150f, sign), W, "the 3-argument form disagrees (identical points)");

            // Inside: 42 m off-centre at zoom 180 (keep 54 m).
            Check(HubCameraRules.AlreadyOn(sign + new float2(0f, 42f), 180f, sign, out d) && d == 42f, W, $"42 m at zoom 180: d {d}");

            // On the edge (inclusive): a 180 / 240 offset is exactly 300 m; zoom 1000 keeps exactly 300 m.
            Check(HubCameraRules.AlreadyOn(sign + new float2(180f, 240f), 1000f, sign, out d) && d == 300f, W,
                $"300 m at zoom 1000 (the edge): d {d.ToString("R", CultureInfo.InvariantCulture)}");
            // Just outside: 300.01 m at zoom 1000; and 300 m at zoom 999.9 (keep 299.97 m).
            Check(!HubCameraRules.AlreadyOn(sign + new float2(0f, 300.01f), 1000f, sign, out d) && Near(d, 300.01, 0.001), W,
                $"300.01 m at zoom 1000 is on: d {d.ToString("R", CultureInfo.InvariantCulture)}");
            Check(!HubCameraRules.AlreadyOn(sign + new float2(180f, 240f), 999.9f, sign), W, "300 m at zoom 999.9 is on");

            // The zoom cap: on the sign at zoom 1500 → on; at 1500.5 → not (the keep radius alone would say on).
            Check(HubCameraRules.AlreadyOn(sign, 1500f, sign), W, "on the sign at zoom 1500 (the cap, inclusive) is not on");
            Check(!HubCameraRules.AlreadyOn(sign, 1500.5f, sign, out d) && d == 0f, W, "on the sign at zoom 1500.5 is on");
            Check(!HubCameraRules.AlreadyOn(sign, 4000f, sign), W, "on the sign at zoom 4000 (whole-map view) is on");
            // Both edges at once: a 270 / 360 offset is exactly 450 m = 0.3 × 1500.
            Check(HubCameraRules.AlreadyOn(sign + new float2(270f, 360f), 1500f, sign, out d) && d == 450f, W,
                $"450 m at zoom 1500: d {d.ToString("R", CultureInfo.InvariantCulture)}");

            // Zero / negative zoom: an unknown camera state flies, even on the sign.
            Check(!HubCameraRules.AlreadyOn(sign, 0f, sign, out d) && d == 0f, W, "zoom 0 on the sign is on");
            Check(!HubCameraRules.AlreadyOn(sign, -10f, sign), W, "zoom -10 on the sign is on");

            // Non-finite input: not on (the caller flies); the distance is reported as computed.
            Check(!HubCameraRules.AlreadyOn(new float2(float.NaN, 0f), 150f, sign, out d) && float.IsNaN(d), W, $"NaN pivot: d {d}");
            Check(!HubCameraRules.AlreadyOn(sign, float.NaN, sign), W, "NaN zoom is on");
            Check(!HubCameraRules.AlreadyOn(sign, float.PositiveInfinity, sign), W, "infinite zoom is on");
            Check(!HubCameraRules.AlreadyOn(sign, 150f, new float2(float.PositiveInfinity, 0f)), W, "infinite sign is on");

            // The field pair (log 629-631): the other sign at (-242, 1876), zoom 412 → fly, d = 9698.24 m
            // (dx 6066, dz 7567: sqrt(94,055,845)).
            Check(!HubCameraRules.AlreadyOn(new float2(-242f, 1876f), 412f, sign, out d) && Near(d, 9698.24, 0.01), W,
                $"the field pair: d {d.ToString("R", CultureInfo.InvariantCulture)} (want 9698.24, not on)");
        }
    }
}
