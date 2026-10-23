// Tests of the attraction model (SkiResortScore): constants guard, helpers, the 37 sanity rows, monotonicity, order
// independence, sum of squares, no-throw. `public static class ScoreTests` with `public static void Run(Func<bool, string, string,
// bool> check)` and its own Near; Program.Main calls ScoreTests.Run(Check). Oracles are literal hand values, never a second run of
// SkiResortScore. Sections, in run order:
//   A  constants guard: every constant against its literal + the four RunKind copies.
//   B  helpers: SizeFactor (knee oracles, ±1e-9), LodgeBaseOf, BandValueOf, TypeFactorOf, SignEfficiency (exact float),
//      RoundToEven; plus the K / backcountry clamps of Compute step 1.
//   C  the sanity table: 37 rows built from the Small / Medium / Large compositions: A, PerLodge, AttractionFullEfficiency,
//      LodgeBase, LodgeShare, LiftShare, LiftsCounted, Variety and VerticalM exact; RunsValue, S, AOpen and the vertical
//      multiplier ±1e-6; the per-row counts and flags; the T1 / T2 / T3 RunPart values (±1e-6).
//   D  properties, seed 20261004, 5,000 random areas: twelve monotone mutations (A' ≥ A) and the zero-run invariance (gating on),
//      order independence (reversed and one shuffle: Result.Equals exact, RunParts permuted), sum of squares, then the no-throw
//      inputs.
// Ladder: LodgeExtraCap is 24 (LodgeBaseOf rows 5 → 36, 10 / 13 → 44; L10: LodgeBase 44, AOpen 72.882668052, A 73, PerLodge 23);
// the A-frame constants are checked against SkiCatalog.Lodges[0]; T1L / T2L / T3L = T1-T3 with explicit A-frame
// AreaInput.Lodges lists → Result.Equals(T1 / T2 / T3) bit for bit (the ladder over A-frames = the closed form every other row
// takes). Every other row passes on the Lodges == null path. The ladder / school / patrol / per-type rows are LadderTests.cs.

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using Score = SkiResortMod.Systems.SkiResortScore;

namespace ResortBatch
{
    public static class ScoreTests
    {
        private static Func<bool, string, string, bool> s_Check;

        // Kinds and bands, short (RunKind / SlopeBand values)
        private const byte Gr = Score.KindGroomed, Sm = Score.KindSnowmaking, Ni = Score.KindNight, Na = Score.KindNatural;
        private const byte Flat = Score.BandFlat, Green = Score.BandGreen, Blue = Score.BandBlue,
                           Black = Score.BandBlack, DBlack = Score.BandDoubleBlack;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            ConstantsGuard();
            Helpers();
            SanityTable();
            Properties();
            NoThrow();
        }

        private static bool Check(bool cond, string where, string msg)
        {
            return s_Check(cond, where, msg);
        }

        private static bool Near(double a, double b, double eps)
        {
            return Math.Abs(a - b) <= eps;
        }


        private static void ConstantsGuard()
        {
            const string w = "score-const";
            Check(Score.RefAreaM2 == 20000.0, w, "RefAreaM2");
            Check(Score.SizeLogSlope == 0.3, w, "SizeLogSlope");
            Check(Score.SizeFloor == 0.25, w, "SizeFloor");
            Check(Score.SizeCap == 2.0, w, "SizeCap");
            Check(Score.SizeKneeM2 == 10000.0, w, "SizeKneeM2 (M3 / DV21)");
            Check(Score.UnrateableValue == 3.0, w, "UnrateableValue");
            Check(Score.NightFactor == 1.25, w, "NightFactor");
            Check(Score.NaturalUnrateableFactor == 0.5, w, "NaturalUnrateableFactor");
            Check(Score.LiftValue == 6.0, w, "LiftValue");
            Check(Score.VarietyFactor == 1.15, w, "VarietyFactor");
            Check(Score.VerticalRefM == 600.0, w, "VerticalRefM");
            Check(Score.FirstLodgeBase == 20.0, w, "FirstLodgeBase");
            Check(Score.LodgeExtra == 4.0, w, "LodgeExtra");
            Check(Score.LodgeExtraCap == 24.0, w, "LodgeExtraCap (Catalog Phase B R11: was 12)");
            // The A-frame closed form (Lodges == null) uses these; they are the catalog's row 0
            Check(SkiCatalog.Lodges.Count > 0 && Score.FirstLodgeBase == SkiCatalog.Lodges[0].B && Score.LodgeExtra == SkiCatalog.Lodges[0].E &&
                  Score.SummerLodgeShare == SkiCatalog.Lodges[0].Mu && SkiCatalog.Lodges[0].MuSummit == SkiCatalog.Lodges[0].Mu,
                  w, "FirstLodgeBase / LodgeExtra / SummerLodgeShare == SkiCatalog.Lodges[0] B / E / μ (and μ summit == μ)");
            Check(Score.LodgeExtraCap == SkiCatalog.Ladder.ExtraCap, w, "LodgeExtraCap == SkiCatalog.Ladder.ExtraCap");
            Check(SkiCatalog.Lifts.Count > 0 && Score.SummerLiftShare == SkiCatalog.Lifts[0].SummerShare && Score.LiftValue == SkiCatalog.Lifts[0].ScoreValue,
                  w, "SummerLiftShare / LiftValue == SkiCatalog.Lifts[0] SummerShare / ScoreValue");
            Check(Score.SummerLodgeShare == 0.5, w, "SummerLodgeShare");
            Check(Score.SummerLiftShare == 0.25, w, "SummerLiftShare");
            Check(Score.MaxAttraction == 200, w, "MaxAttraction");
            Check(Score.MinK == 1 && Score.MaxK == 10, w, "MinK / MaxK");
            Check(Score.BandFlat == 0 && Score.BandGreen == 1 && Score.BandBlue == 2 && Score.BandBlack == 3 &&
                  Score.BandDoubleBlack == 4, w, "SlopeBand copies");
            // Band values (0 / 6 / 12 / 20 / 28) through the helper
            Check(Score.BandValueOf(Score.BandState.Rated, Flat) == 0.0 && Score.BandValueOf(Score.BandState.Rated, Green) == 6.0 &&
                  Score.BandValueOf(Score.BandState.Rated, Blue) == 12.0 && Score.BandValueOf(Score.BandState.Rated, Black) == 20.0 &&
                  Score.BandValueOf(Score.BandState.Rated, DBlack) == 28.0, w, "band values 0/6/12/20/28");
            // The four RunKind copies (EntityRef.cs is linked; second pass).
            Check(Score.KindGroomed == (byte)RunKind.Groomed, w, "KindGroomed == RunKind.Groomed");
            Check(Score.KindSnowmaking == (byte)RunKind.Snowmaking, w, "KindSnowmaking == RunKind.Snowmaking");
            Check(Score.KindNight == (byte)RunKind.Night, w, "KindNight == RunKind.Night");
            Check(Score.KindNatural == (byte)RunKind.Natural, w, "KindNatural == RunKind.Natural");
        }


        private static void Helpers()
        {
            const string w = "score-helpers";
            // SizeFactor, ±1e-9 (knee M3).
            double[] sizeIn = { 0, -5, double.NaN, double.PositiveInfinity, 1640, 1700, 5000, 9999, 10000, 20000, 25000, 30000,
                                40000, 80000, 120000, 270000, 561000, 1e9 };
            double[] sizeOut = { 0, 0, 0, 2.0, 0.129897159, 0.134649494, 0.396027923, 0.791976640, 0.792055846, 1.0, 1.066943065,
                                 1.121639532, 1.207944154, 1.415888308, 1.537527841, 1.780806906, 2.0, 2.0 };
            for (int i = 0; i < sizeIn.Length; i++)
            {
                double got = Score.SizeFactor(sizeIn[i]);
                Check(Near(got, sizeOut[i], 1e-9), w, $"SizeFactor({sizeIn[i]}) = {got:R}, expected {sizeOut[i]}");
            }
            Check(Score.SizeFactor(20000) == 1.0, w, "SizeFactor(20000) is exactly 1.0");

            // LodgeBaseOf: cap 24: 5 → 36, 10 / 13 → 44
            int[] lodgeIn = { 0, 1, 2, 3, 4, 5, 10, 13, -1 };
            double[] lodgeOut = { 0, 20, 24, 28, 32, 36, 44, 44, 0 };
            for (int i = 0; i < lodgeIn.Length; i++)
                Check(Score.LodgeBaseOf(lodgeIn[i]) == lodgeOut[i], w, $"LodgeBaseOf({lodgeIn[i]}) = {Score.LodgeBaseOf(lodgeIn[i])}, expected {lodgeOut[i]}");

            // BandValueOf.
            double[] bandOut = { 0, 6, 12, 20, 28 };
            for (byte b = 0; b <= 4; b++)
            {
                Check(Score.BandValueOf(Score.BandState.Rated, b) == bandOut[b], w, $"BandValueOf(Rated, {b})");
                Check(Score.BandValueOf(Score.BandState.Unrateable, b) == 3.0, w, $"BandValueOf(Unrateable, {b}) = 3");
                Check(Score.BandValueOf(Score.BandState.Pending, b) == 0.0, w, $"BandValueOf(Pending, {b}) = 0");
            }

            // TypeFactorOf.
            for (byte b = 0; b <= 4; b++)
            {
                Check(Score.TypeFactorOf(Gr, Score.BandState.Rated, b) == 1.0, w, $"TypeFactorOf(Groomed, Rated, {b})");
                Check(Score.TypeFactorOf(Sm, Score.BandState.Rated, b) == 1.0, w, $"TypeFactorOf(Snowmaking, Rated, {b})");
                Check(Score.TypeFactorOf(Ni, Score.BandState.Rated, b) == 1.25, w, $"TypeFactorOf(Night, Rated, {b})");
            }
            Check(Score.TypeFactorOf(Gr, Score.BandState.Unrateable, 0) == 1.0 && Score.TypeFactorOf(Gr, Score.BandState.Pending, 0) == 1.0,
                  w, "TypeFactorOf(Groomed, any)");
            Check(Score.TypeFactorOf(Ni, Score.BandState.Pending, 0) == 1.25, w, "TypeFactorOf(Night, Pending)");
            double[] naturalOut = { 0.5, 0.5, 0.75, 1.0, 1.0 };
            for (byte b = 0; b <= 4; b++)
                Check(Score.TypeFactorOf(Na, Score.BandState.Rated, b) == naturalOut[b], w, $"TypeFactorOf(Natural, Rated, {b})");
            Check(Score.TypeFactorOf(Na, Score.BandState.Unrateable, 0) == 0.5, w, "TypeFactorOf(Natural, Unrateable) = 0.5 (I3)");
            Check(Score.TypeFactorOf(Na, Score.BandState.Pending, 0) == 0.5, w, "TypeFactorOf(Natural, Pending) = 0.5");
            Check(Score.TypeFactorOf(7, Score.BandState.Rated, Blue) == 1.0, w, "TypeFactorOf(kind 7) = 1.0");

            // SignEfficiency, exact float.
            Check(Score.SignEfficiency(new float[0], 0) == 1f, w, "SignEfficiency([]) = 1");
            Check(Score.SignEfficiency(null, 0) == 1f, w, "SignEfficiency(no buffer) = 1");
            Check(Score.SignEfficiency(new[] { 1f, 1f }, 2) == 1f, w, "SignEfficiency([1, 1]) = 1");
            Check(Score.SignEfficiency(new[] { 0.5f, 0.5f }, 2) == 0.25f, w, "SignEfficiency([0.5, 0.5]) = 0.25");
            Check(Score.SignEfficiency(new[] { 0.5f, 0f }, 2) == 0f, w, "SignEfficiency([0.5, 0]) = 0");
            Check(Score.SignEfficiency(new[] { 0.004f }, 1) == 0.01f, w, "SignEfficiency([0.004]) = 0.01 (floor)");
            Check(Score.SignEfficiency(new[] { 1.25f }, 1) == 1.25f, w, "SignEfficiency([1.25]) = 1.25");
            Check(Score.SignEfficiency(new[] { 0.333f }, 1) == 0.33f, w, "SignEfficiency([0.333]) = 0.33");
            Check(Score.SignEfficiency(new[] { -0.2f }, 1) == 0f, w, "SignEfficiency([-0.2]) = 0");
            Check(Score.SignEfficiency(new[] { 0.9f, 0.9f, 0.9f }, 3) == 0.73f, w, "SignEfficiency([0.9, 0.9, 0.9]) = 0.73");
            // count reads only the first `count` entries (the reused float[32] of the system).
            Check(Score.SignEfficiency(new[] { 0.5f, 0f, 0f }, 1) == 0.5f, w, "SignEfficiency(count 1 of 3) = 0.5");

            // RoundToEven.
            Check(Score.RoundToEven(0.5) == 0, w, "RoundToEven(0.5) = 0");
            Check(Score.RoundToEven(1.5) == 2, w, "RoundToEven(1.5) = 2");
            Check(Score.RoundToEven(2.5) == 2, w, "RoundToEven(2.5) = 2");
            Check(Score.RoundToEven(48.882668052) == 49, w, "RoundToEven(48.882668052) = 49");
            Check(Score.RoundToEven(-0.4) == 0, w, "RoundToEven(-0.4) = 0");

            // Compute step 1: K clamped to 1..10, backcountry to 0..100.
            var parts = new List<Score.RunPart>();
            Score.Result lo = Score.Compute(Area(1, 1.0, 0, -5, true), new List<Score.RunInput>(), new List<Score.LiftInput>(), parts);
            Score.Result hi = Score.Compute(Area(1, 1.0, 99, 150, true), new List<Score.RunInput>(), new List<Score.LiftInput>(), parts);
            Check(lo.K == 1 && lo.BackcountryPercent == 0, w, $"K 0 / backcountry -5 clamp to 1 / 0 (got {lo.K} / {lo.BackcountryPercent})");
            Check(hi.K == 10 && hi.BackcountryPercent == 100, w, $"K 99 / backcountry 150 clamp to 10 / 100 (got {hi.K} / {hi.BackcountryPercent})");
        }


        private static Score.RunInput R(byte kind, byte band, double areaM2, double topM, double bottomM)
        {
            return new Score.RunInput
            {
                Kind = kind,
                State = Score.BandState.Rated,
                Band = band,
                AreaM2 = areaM2,
                Served = true,
                OpenFactor = 1.0,
                OpenEstimated = false,
                TopM = topM,
                BottomM = bottomM,
            };
        }

        private static void Add(List<Score.RunInput> runs, int count, Score.RunInput run)
        {
            for (int i = 0; i < count; i++)
                runs.Add(run);
        }

        /// <summary>Small: Groomed Green 2.0 ha 1150/1000; Groomed Blue 2.5 ha 1250/1000; Groomed Blue 2.0 ha 1250/1050.</summary>
        private static List<Score.RunInput> Small()
        {
            return new List<Score.RunInput>
            {
                R(Gr, Green, 20000, 1150, 1000),
                R(Gr, Blue, 25000, 1250, 1000),
                R(Gr, Blue, 20000, 1250, 1050),
            };
        }

        /// <summary>Medium (runs 0-9 in the order the per-run oracles use).</summary>
        private static List<Score.RunInput> Medium()
        {
            var runs = new List<Score.RunInput>();
            Add(runs, 3, R(Gr, Green, 25000, 1250, 1000));   // 0-2
            Add(runs, 3, R(Gr, Blue, 30000, 1400, 1000));    // 3-5
            Add(runs, 1, R(Sm, Blue, 30000, 1400, 1000));    // 6
            Add(runs, 1, R(Ni, Blue, 25000, 1300, 1000));    // 7
            Add(runs, 2, R(Na, Black, 30000, 1500, 1100));   // 8-9
            return runs;
        }

        /// <summary>Large (runs 0-24).</summary>
        private static List<Score.RunInput> Large()
        {
            var runs = new List<Score.RunInput>();
            Add(runs, 6, R(Gr, Green, 30000, 1300, 1000));    // 0-5
            Add(runs, 5, R(Gr, Blue, 40000, 1500, 1000));     // 6-10
            Add(runs, 3, R(Sm, Blue, 40000, 1500, 1000));     // 11-13
            Add(runs, 2, R(Ni, Blue, 30000, 1300, 1000));     // 14-15
            Add(runs, 5, R(Na, Black, 40000, 1800, 1200));    // 16-20
            Add(runs, 1, R(Sm, Black, 40000, 1700, 1100));    // 21
            Add(runs, 3, R(Na, DBlack, 40000, 1800, 1300));   // 22-24
            return runs;
        }

        private static List<Score.LiftInput> Lifts(int count, bool complete = true, bool powered = true)
        {
            var lifts = new List<Score.LiftInput>();
            for (int i = 0; i < count; i++)
                lifts.Add(new Score.LiftInput { Complete = complete, Powered = powered });
            return lifts;
        }

        private static Score.AreaInput Area(int lodges, double efficiency = 1.0, int k = 4, int backcountry = 40, bool gating = true)
        {
            return new Score.AreaInput
            {
                ActiveLodges = lodges,
                InactiveLodges = 0,
                Efficiency = efficiency,
                Settings = new Score.ScoreSettings { K = k, BackcountryPercent = backcountry, SnowGating = gating },
            };
        }

        private static List<Score.RunInput> WithOpen(List<Score.RunInput> runs, double open)
        {
            var copy = new List<Score.RunInput>(runs.Count);
            for (int i = 0; i < runs.Count; i++)
            {
                Score.RunInput r = runs[i];
                r.OpenFactor = open;
                copy.Add(r);
            }
            return copy;
        }

        private static List<Score.RunInput> WithServed(List<Score.RunInput> runs, bool served, params int[] indices)
        {
            var copy = new List<Score.RunInput>(runs);
            for (int i = 0; i < indices.Length; i++)
            {
                Score.RunInput r = copy[indices[i]];
                r.Served = served;
                copy[indices[i]] = r;
            }
            return copy;
        }

        private static List<Score.RunInput> AllServed(List<Score.RunInput> runs, bool served)
        {
            var copy = new List<Score.RunInput>(runs);
            for (int i = 0; i < copy.Count; i++)
            {
                Score.RunInput r = copy[i];
                r.Served = served;
                copy[i] = r;
            }
            return copy;
        }

        private static List<Score.RunInput> With(List<Score.RunInput> runs, Score.RunInput extra)
        {
            var copy = new List<Score.RunInput>(runs) { extra };
            return copy;
        }

        private static List<Score.RunInput> Replace(List<Score.RunInput> runs, int index, Score.RunInput run)
        {
            var copy = new List<Score.RunInput>(runs);
            copy[index] = run;
            return copy;
        }

        /// <summary>One sanity row: computes it and checks the common columns. verticalX = the table's vertical multiplier (±1e-6).
        /// Returns the Result for the row's own counts and flags.</summary>
        private static Score.Result Row(string id, Score.AreaInput area, List<Score.RunInput> runs, List<Score.LiftInput> lifts,
            List<Score.RunPart> parts, double lodgeBase, bool summer, double runsValue, int liftsCounted, bool variety,
            double verticalM, double verticalX, double s, double aOpen, int a, int perLodge, int full)
        {
            string w = "score-row " + id;
            Score.Result r = Score.Compute(area, runs, lifts, parts);
            Check(r.Attraction == a, w, $"A {r.Attraction}, expected {a}");
            Check(r.PerLodge == perLodge, w, $"PerLodge {r.PerLodge}, expected {perLodge}");
            Check(r.AttractionFullEfficiency == full, w, $"AttractionFullEfficiency {r.AttractionFullEfficiency}, expected {full}");
            Check(Near(r.RunsValue, runsValue, 1e-6), w, $"RunsValue {r.RunsValue:R}, expected {runsValue}");
            Check(Near(r.S, s, 1e-6), w, $"S {r.S:R}, expected {s}");
            Check(Near(r.AOpen, aOpen, 1e-6), w, $"AOpen {r.AOpen:R}, expected {aOpen}");
            Check(r.VerticalM == verticalM, w, $"VerticalM {r.VerticalM:R}, expected {verticalM}");
            Check(Near(r.VerticalMultiplier, verticalX, 1e-6), w, $"VerticalMultiplier {r.VerticalMultiplier:R}, expected {verticalX}");
            Check(r.Variety == variety && r.VarietyMultiplier == (variety ? 1.15 : 1.0), w,
                  $"Variety {r.Variety} ×{r.VarietyMultiplier}, expected {variety}");
            Check(r.LiftsCounted == liftsCounted && r.LiftsValue == 6.0 * liftsCounted, w,
                  $"LiftsCounted {r.LiftsCounted} (value {r.LiftsValue}), expected {liftsCounted}");
            Check(r.LodgeBase == lodgeBase, w, $"LodgeBase {r.LodgeBase}, expected {lodgeBase}");
            Check(r.LodgeShare == (summer ? 0.5 : 1.0) && r.LiftShare == (summer ? 0.25 : 1.0), w,
                  $"LodgeShare {r.LodgeShare} / LiftShare {r.LiftShare}, expected summer {summer}");
            Check(r.ActiveLodges == area.ActiveLodges && r.K == Math.Min(10, Math.Max(1, area.Settings.K)) &&
                  r.Efficiency == area.Efficiency, w, "echoed N / K / efficiency");
            Check(parts.Count == runs.Count, w, $"{parts.Count} RunPart(s) for {runs.Count} run(s)");
            return r;
        }

        private static void Flags(string id, Score.Result r, Score.ScoreFlags expected)
        {
            Check(r.Flags == expected, "score-row " + id, $"Flags {r.Flags}, expected {expected}");
        }

        /// <summary>The area's ActiveLodges as an explicit list of A-frame LodgeInput rows (LodgeIndex 0; odd positions SummitServed, which
        /// the A-frame ignores: μ summit == μ) must give <paramref name="expected"/> (the Lodges == null row) bit for bit.</summary>
        private static void ExplicitAFrames(string id, Score.AreaInput area, List<Score.RunInput> runs, List<Score.LiftInput> lifts,
                                             Score.Result expected)
        {
            var lodges = new List<Score.LodgeInput>();
            for (int i = 0; i < area.ActiveLodges; i++)
                lodges.Add(new Score.LodgeInput { LodgeIndex = 0, SummitServed = (i & 1) == 1 });
            area.Lodges = lodges;
            Score.Result r = Score.Compute(area, runs, lifts, new List<Score.RunPart>());
            Check(r.Equals(expected), "score-row " + id,
                  $"explicit A-frame list ≠ the Lodges == null row: A {r.Attraction} / {expected.Attraction}, LodgeBase {r.LodgeBase:R} / " +
                  $"{expected.LodgeBase:R}, applied {r.LodgeApplied:R} / {expected.LodgeApplied:R}, AOpen {r.AOpen:R} / {expected.AOpen:R}, " +
                  $"extras {r.LodgeExtraApplied:R} / {expected.LodgeExtraApplied:R}, share {r.LodgeShare:R} / {expected.LodgeShare:R}");
        }

        private static void SanityTable()
        {
            var parts = new List<Score.RunPart>();
            const string w = "score-row";

            // T1 Small.
            Score.Result t1 = Row("T1", Area(1), Small(), Lifts(1), parts, 20, false, 30.803316785, 1, false,
                                  250, 1.416667, 52.138032112, 48.882668052, 49, 49, 49);
            Check(t1.Runs == 3 && t1.RunsCounted == 3 && t1.RunsOpen == 3, w + " T1", "Runs / RunsCounted / RunsOpen 3");
            Flags("T1", t1, Score.ScoreFlags.None);
            Check(Near(parts[0].Value, 6.0, 1e-6) && Near(parts[1].Value, 12.803316785, 1e-6) && Near(parts[2].Value, 12.0, 1e-6),
                  w + " T1", "RunPart values 6 / 12.803316785 / 12");

            // T2 Medium.
            Score.Result t2 = Row("T2", Area(2), Medium(), Lifts(3), parts, 24, false, 133.913400012, 3, true,
                                  500, 1.833333, 320.284085025, 95.585929905, 96, 68, 96);
            Check(t2.Runs == 10 && t2.RunsCounted == 10, w + " T2", "Runs / RunsCounted 10");
            Flags("T2", t2, Score.ScoreFlags.None);
            double[] t2Parts = { 6.401658392, 6.401658392, 6.401658392, 13.459674389, 13.459674389, 13.459674389, 13.459674389,
                                 16.004145981, 22.432790649, 22.432790649 };
            for (int i = 0; i < t2Parts.Length; i++)
                Check(Near(parts[i].Value, t2Parts[i], 1e-6) && parts[i].Counted, w + " T2", $"RunPart {i} value {parts[i].Value:R}, expected {t2Parts[i]}");
            Check(parts[7].TypeFactor == 1.25 && parts[8].TypeFactor == 1.0 && parts[0].BandValue == 6 && parts[0].ServedFactor == 1.0 &&
                  parts[0].OpenFactor == 1.0, w + " T2", "RunPart factors (night 1.25, natural black 1.0, green 6, served 1, open 1)");

            // T3 Large.
            Score.Result t3 = Row("T3", Area(3), Large(), Lifts(6), parts, 28, false, 436.411455391, 6, true,
                                  800, 2.0, 1086.546347399, 159.851209924, 160, 92, 160);
            Check(t3.Runs == 25 && t3.RunsCounted == 25 && t3.Lifts == 6 && t3.LiftsCounted == 6, w + " T3", "Runs 25 / Lifts 6");
            Flags("T3", t3, Score.ScoreFlags.None);
            Check(Near(parts[0].Value, 6.729837195, 1e-6) && Near(parts[6].Value, 14.495329850, 1e-6) &&
                  Near(parts[14].Value, 16.824592986, 1e-6) && Near(parts[16].Value, 24.158883083, 1e-6) &&
                  Near(parts[22].Value, 33.822436317, 1e-6), w + " T3", "RunPart values of runs 0, 6, 14, 16, 22");

            // T1L / T2L / T3L: T1-T3 with explicit A-frame AreaInput.Lodges lists (every other one marked summit-served: μ summit == μ on the
            // A-frame, so it changes nothing) → the same Result bit for bit (Equals compares every field)
            ExplicitAFrames("T1L", Area(1), Small(), Lifts(1), t1);
            ExplicitAFrames("T2L", Area(2), Medium(), Lifts(3), t2);
            ExplicitAFrames("T3L", Area(3), Large(), Lifts(6), t3);
            Check(t1.LodgeApplied == 20 && t1.LodgeBestIndex == 0 && t1.PriceTolerance == 0 &&
                  t1.LiftsApplied == 6 && t1.SchoolValue == 0 && !t1.SchoolActive && t1.GreenCounted && t1.RunsPatrolled == 0,
                  w + " T1", "Phase B fields: LodgeApplied 20, best A-frame 0, tolerance 0, LiftsApplied 6, no school, green counted, 0 patrolled");

            // V1: Small, third run Natural Black 2.0 ha 1250/1050.
            Score.Result v1 = Row("V1", Area(1), Replace(Small(), 2, R(Na, Black, 20000, 1250, 1050)), Lifts(1), parts, 20, false,
                                  38.803316785, 1, true, 250, 1.416667, 72.992070262, 54.174158720, 54, 54, 54);
            Check(v1.Variety, w + " V1", "Variety true");

            // W1-W3: summer (every open factor 0).
            Score.Result w1 = Row("W1", Area(1), WithOpen(Small(), 0), Lifts(1), parts, 20, true, 0, 1, false,
                                  0, 1.0, 1.5, 14.898979486, 15, 15, 15);
            Score.Result w2 = Row("W2", Area(2), WithOpen(Medium(), 0), Lifts(3), parts, 24, true, 0, 3, false,
                                  0, 1.0, 4.5, 20.485281374, 20, 14, 20);
            Score.Result w3 = Row("W3", Area(3), WithOpen(Large(), 0), Lifts(6), parts, 28, true, 0, 6, false,
                                  0, 1.0, 9.0, 26.000000000, 26, 15, 26);
            Score.Result[] ws = { w1, w2, w3 };
            for (int i = 0; i < ws.Length; i++)
            {
                Check(ws[i].RunsOpen == 0 && ws[i].RunsCounted == 0, w + " W" + (i + 1), "RunsOpen 0, RunsCounted 0");
                Flags("W" + (i + 1), ws[i], Score.ScoreFlags.SummerMode);
            }
            // The summer twins (μ 0.5 on the best and on every extra = LodgeBase × 0.5 exactly)
            ExplicitAFrames("W1L", Area(1), WithOpen(Small(), 0), Lifts(1), w1);
            ExplicitAFrames("W2L", Area(2), WithOpen(Medium(), 0), Lifts(3), w2);
            ExplicitAFrames("W3L", Area(3), WithOpen(Large(), 0), Lifts(6), w3);
            Check(w2.LodgeApplied == 12 && w2.LodgeBase == 24 && w2.LiftsApplied == 4.5 && w2.LiftsValue == 18 && !w2.GreenCounted,
                  w + " W2", "Phase B fields: LodgeApplied 12 of base 24, LiftsApplied 4.5 of value 18, no counted green");

            // N1: Medium, all 10 runs Natural.
            var natural = new List<Score.RunInput>(Medium());
            for (int i = 0; i < natural.Count; i++)
            {
                Score.RunInput r = natural[i];
                r.Kind = Na;
                natural[i] = r;
            }
            Score.Result n1 = Row("N1", Area(2), natural, Lifts(3), parts, 24, false, 104.449579642, 3, true,
                                  500, 1.833333, 258.164530412, 88.269996784, 88, 62, 88);
            Check(n1.Variety, w + " N1", "Variety true");

            // N2: Medium + one closed natural run — every Result field equal to T2 except Runs (11 vs 10).
            Score.RunInput closedNatural = R(Na, Black, 30000, 1600, 1100);
            closedNatural.OpenFactor = 0;
            Score.Result n2 = Row("N2", Area(2), With(Medium(), closedNatural), Lifts(3), parts, 24, false, 133.913400012, 3, true,
                                  500, 1.833333, 320.284085025, 95.585929905, 96, 68, 96);
            Score.Result t2Plus = t2;
            t2Plus.Runs = 11;
            Check(Near(n2.RunsValueFull, 156.346190661, 1e-6) && Near(t2.RunsValueFull, 133.913400012, 1e-6), w + " N2",
                  $"RunsValueFull {n2.RunsValueFull:R} (T2 {t2.RunsValueFull:R}), expected 156.346190661 (133.913400012 + 22.432790649)");
            t2Plus.RunsValueFull = n2.RunsValueFull;   // the closed run's full value is the one other difference
            Check(n2.Equals(t2Plus) && n2.Runs == 11 && n2.RunsOpen == 10, w + " N2", "equals T2 in every field but Runs (11) and RunsValueFull; RunsOpen 10");
            Check(parts[10].OpenFactor == 0 && parts[10].Value == 0 && !parts[10].Counted, w + " N2", "the closed run's part: open 0, value 0, not counted");

            // N3: Medium + one open Natural Green 2.0 ha 1200/1000.
            Score.Result n3 = Row("N3", Area(2), With(Medium(), R(Na, Green, 20000, 1200, 1000)), Lifts(3), parts, 24, false,
                                  136.913400012, 3, true, 500, 1.833333, 326.609085025, 96.289317056, 96, 68, 96);
            Check(n3.RunsCounted == 11, w + " N3", "RunsCounted 11");

            // L10 / L2: Small with 10 / 2 active lodges. L10: LodgeBase 32 → 44 (cap 24), AOpen 60.882668052 → 72.882668052, A 61 → 73,
            // PerLodge 19 → 23 (73 / √10 = 23.08).
            Score.Result l10 = Row("L10", Area(10), Small(), Lifts(1), parts, 44, false, 30.803316785, 1, false,
                                   250, 1.416667, 52.138032112, 72.882668052, 73, 23, 73);
            Check(l10.ActiveLodges == 10 && l10.LodgeBase == 44 && l10.LodgeApplied == 44 && l10.LodgeBestIndex == 0 &&
                  l10.LodgeExtraApplied == 24, w + " L10", "ActiveLodges 10, LodgeBase / applied 44, best A-frame, extras 24");
            Score.Result l2 = Row("L2", Area(2), Small(), Lifts(1), parts, 24, false, 30.803316785, 1, false,
                                  250, 1.416667, 52.138032112, 52.882668052, 53, 37, 53);
            Check(l2.ActiveLodges == 2 && l2.LodgeBase == 24, w + " L2", "ActiveLodges 2, LodgeBase 24");
            ExplicitAFrames("L10L", Area(10), Small(), Lifts(1), l10);   // 10 A-frames through the ladder (cap reached)

            // E0 / E50 / E125: Medium at efficiency 0 / 0.5 / 1.25.
            Score.Result e0 = Row("E0", Area(2, 0.0), Medium(), Lifts(3), parts, 24, false, 133.913400012, 3, true,
                                  500, 1.833333, 320.284085025, 95.585929905, 0, 0, 96);
            Flags("E0", e0, Score.ScoreFlags.EfficiencyZero);
            Score.Result e50 = Row("E50", Area(2, 0.5), Medium(), Lifts(3), parts, 24, false, 133.913400012, 3, true,
                                   500, 1.833333, 320.284085025, 95.585929905, 48, 34, 96);
            Score.Result e125 = Row("E125", Area(2, 1.25), Medium(), Lifts(3), parts, 24, false, 133.913400012, 3, true,
                                    500, 1.833333, 320.284085025, 95.585929905, 119, 84, 96);
            Check(e50.Attraction == 48 && e125.Attraction == 119 && e50.AttractionFullEfficiency == 96 &&
                  e125.AttractionFullEfficiency == 96, w + " E50/E125", "A 48 / 119, full 96");
            Flags("E125", e125, Score.ScoreFlags.None);

            // B1 / B0 / B100: Medium, both natural blacks (runs 8, 9) unserved, backcountry 40 / 0 / 100.
            List<Score.RunInput> unservedBlacks = WithServed(Medium(), false, 8, 9);
            Score.Result b1 = Row("B1", Area(2), unservedBlacks, Lifts(3), parts, 24, false, 106.994051234, 3, true,
                                  500, 1.833333, 263.529124684, 88.934320624, 89, 63, 89);
            Score.Result b0 = Row("B0", Area(2, 1.0, 4, 0), unservedBlacks, Lifts(3), parts, 24, false, 89.047818715, 3, false,
                                  400, 1.666667, 178.413031191, 77.428536374, 77, 54, 77);
            Score.Result b100 = Row("B100", Area(2, 1.0, 4, 100), unservedBlacks, Lifts(3), parts, 24, false, 133.913400012, 3, true,
                                    500, 1.833333, 320.284085025, 95.585929905, 96, 68, 96);
            Check(b1.RunsUnserved == 2 && b0.RunsUnserved == 2 && b100.RunsUnserved == 2, w + " B1/B0/B100", "RunsUnserved 2");
            Check(b0.RunsCounted == 8 && !b0.Variety && b0.VerticalM == 400, w + " B0", "RunsCounted 8, no variety, VerticalM 400");

            // O1: Medium, lift 3 has a section open; runs 7, 8, 9 unserved.
            var o1Lifts = Lifts(2);
            o1Lifts.Add(new Score.LiftInput { Complete = false, Powered = true });
            Score.Result o1 = Row("O1", Area(2), WithServed(Medium(), false, 7, 8, 9), o1Lifts, parts, 24, false, 97.391563645, 2, true,
                                  500, 1.833333, 230.633880019, 84.746539657, 85, 60, 85);
            Check(o1.Lifts == 3 && o1.LiftsComplete == 2 && o1.LiftsOpenSection == 1 && o1.LiftsCounted == 2 && o1.RunsUnserved == 3,
                  w + " O1", "Lifts 3, LiftsComplete 2, LiftsOpenSection 1, LiftsCounted 2, RunsUnserved 3");

            // P0: Medium, no lift powered; every run unserved.
            Score.Result p0 = Row("P0", Area(2), AllServed(Medium(), false), Lifts(3, true, false), parts, 24, false, 53.565360005, 0, true,
                                  500, 1.833333, 112.933634010, 66.508095043, 67, 47, 67);
            Check(p0.LiftsUnpowered == 3 && p0.LiftsCounted == 0 && p0.RunsUnserved == 10, w + " P0",
                  "LiftsUnpowered 3, LiftsCounted 0, RunsUnserved 10");

            // NL: Medium, no lodge.
            Score.Result nl = Row("NL", Area(0), Medium(), Lifts(3), parts, 0, false, 133.913400012, 3, true,
                                  500, 1.833333, 320.284085025, 71.585929905, 0, 0, 0);
            Flags("NL", nl, Score.ScoreFlags.NoLodge);

            // K1 / K10 / K10L.
            Row("K1", Area(2, 1.0, 1), Medium(), Lifts(3), parts, 24, false, 133.913400012, 3, true,
                500, 1.833333, 320.284085025, 41.896482476, 42, 30, 42);
            Score.Result k10 = Row("K10", Area(2, 1.0, 10), Medium(), Lifts(3), parts, 24, false, 133.913400012, 3, true,
                                   500, 1.833333, 320.284085025, 202.964824763, 200, 141, 200);
            Flags("K10", k10, Score.ScoreFlags.Clamped);
            Score.Result k10l = Row("K10L", Area(3, 1.0, 10), Large(), Lifts(6), parts, 28, false, 436.411455391, 6, true,
                                    800, 2.0, 1086.546347399, 357.628024810, 200, 115, 200);
            Flags("K10L", k10l, Score.ScoreFlags.Clamped);

            // G0: W2 (summer snow) with snow gating off.
            Score.Result g0 = Row("G0", Area(2, 1.0, 4, 40, false), WithOpen(Medium(), 0), Lifts(3), parts, 24, false, 133.913400012, 3, true,
                                  500, 1.833333, 320.284085025, 95.585929905, 96, 68, 96);
            Flags("G0", g0, Score.ScoreFlags.SnowGatingOff);
            Check(g0.RunsOpen == 10, w + " G0", "RunsOpen 10");

            // H50: Medium, every open factor 0.5.
            Score.Result h50 = Row("H50", Area(2), WithOpen(Medium(), 0.5), Lifts(3), parts, 24, false, 66.956700006, 3, true,
                                   500, 1.833333, 179.117042513, 77.533846118, 78, 55, 78);
            Check(h50.RunsOpen == 10, w + " H50", "RunsOpen 10");

            // BLOB: 1 lodge, 1 lift, one Natural DoubleBlack 27 ha 2420/1000.
            Score.Result blob = Row("BLOB", Area(1), new List<Score.RunInput> { R(Na, DBlack, 270000, 2420, 1000) }, Lifts(1), parts, 20, false,
                                    49.862593358, 1, false, 1420, 2.0, 111.725186715, 62.280054251, 62, 62, 62);
            Check(blob.VerticalM == 1420 && blob.VerticalMultiplier == 2.0, w + " BLOB", "VerticalM 1420, multiplier 2.0");

            // Z1 / Z2: sign + 1 lodge (+ 1 lift), no run.
            Score.Result z1 = Row("Z1", Area(1), new List<Score.RunInput>(), Lifts(0), parts, 20, true, 0, 0, false,
                                  0, 1.0, 0, 10.000000000, 10, 10, 10);
            Flags("Z1", z1, Score.ScoreFlags.SummerMode | Score.ScoreFlags.NoRuns);
            Score.Result z2 = Row("Z2", Area(1), new List<Score.RunInput>(), Lifts(1), parts, 20, true, 0, 1, false,
                                  0, 1.0, 1.5, 14.898979486, 15, 15, 15);
            Flags("Z2", z2, Score.ScoreFlags.SummerMode | Score.ScoreFlags.NoRuns);

            // PEND: T1, third run not yet rated.
            Score.RunInput pending = Small()[2];
            pending.State = Score.BandState.Pending;
            Score.Result pend = Row("PEND", Area(1), Replace(Small(), 2, pending), Lifts(1), parts, 20, false, 18.803316785, 1, false,
                                    250, 1.416667, 35.138032112, 43.710936586, 44, 44, 44);
            Check(pend.RunsPending == 1 && pend.RunsCounted == 2, w + " PEND", "RunsPending 1, RunsCounted 2");
            Flags("PEND", pend, Score.ScoreFlags.PendingRatings);

            // UNR / UNRN: T1, third run unrateable (Groomed / Natural).
            Score.RunInput unrateable = Small()[2];
            unrateable.State = Score.BandState.Unrateable;
            Score.Result unr = Row("UNR", Area(1), Replace(Small(), 2, unrateable), Lifts(1), parts, 20, false, 21.803316785, 1, false,
                                   250, 1.416667, 39.388032112, 45.103954146, 45, 45, 45);
            Check(unr.RunsUnrateable == 1 && unr.RunsCounted == 3 && Near(parts[2].Value, 3.0, 1e-12), w + " UNR",
                  "RunsUnrateable 1, RunsCounted 3, third part 3.0");
            unrateable.Kind = Na;
            Score.Result unrn = Row("UNRN", Area(1), Replace(Small(), 2, unrateable), Lifts(1), parts, 20, false, 20.303316785, 1, false,
                                    250, 1.416667, 37.263032112, 44.417381387, 44, 44, 44);
            Check(unrn.RunsUnrateable == 1 && unrn.RunsCounted == 3 && Near(parts[2].Value, 1.5, 1e-12), w + " UNRN",
                  "RunsUnrateable 1, RunsCounted 3, third part 1.5");

            // FLAT: T1, third run rated Flat, 1400/900 (worth 0: vertical stays 250).
            Score.Result flat = Row("FLAT", Area(1), Replace(Small(), 2, R(Gr, Flat, 20000, 1400, 900)), Lifts(1), parts, 20, false,
                                    18.803316785, 1, false, 250, 1.416667, 35.138032112, 43.710936586, 44, 44, 44);
            Check(flat.RunsFlat == 1 && flat.RunsCounted == 2 && flat.VerticalM == 250, w + " FLAT", "RunsFlat 1, RunsCounted 2, VerticalM 250");

            // UNPOW: T1, its only lift unpowered, all 3 runs unserved.
            Score.Result unpow = Row("UNPOW", Area(1), AllServed(Small(), false), Lifts(1, true, false), parts, 20, false,
                                     12.321326714, 0, false, 250, 1.416667, 17.455212845, 36.711774457, 37, 37, 37);
            Check(unpow.LiftsUnpowered == 1 && unpow.LiftsCounted == 0 && unpow.RunsUnserved == 3, w + " UNPOW",
                  "LiftsUnpowered 1, LiftsCounted 0, RunsUnserved 3");

            // SPLIT: 1 lodge, 1 lift, 20 × 0.1 ha Natural DoubleBlack 1600/1000 (M3).
            var split = new List<Score.RunInput>();
            Add(split, 20, R(Na, DBlack, 1000, 1600, 1000));
            Score.Result splitR = Row("SPLIT", Area(1), split, Lifts(1), parts, 20, false, 44.355127367, 1, false,
                                      600, 2.0, 100.710254733, 60.141799608, 60, 60, 60);
            Check(splitR.Runs == 20 && splitR.RunsCounted == 20 && !splitR.Variety && splitR.VerticalM == 600, w + " SPLIT",
                  "Runs 20, RunsCounted 20, no variety, VerticalM 600");
            bool splitParts = true;
            for (int i = 0; i < parts.Count; i++)
                splitParts &= Near(parts[i].SizeFactor, 0.079205585, 1e-9) && Near(parts[i].Value, 2.217756368, 1e-6);
            Check(splitParts, w + " SPLIT", "each RunPart SizeFactor 0.079205585, Value 2.217756368");

            // ZF1 / WF2: one open Flat natural 0.1 ha 1200/1100 does not end summer (I2).
            Score.RunInput openFlat = R(Na, Flat, 1000, 1200, 1100);
            Score.Result zf1 = Row("ZF1", Area(1), new List<Score.RunInput> { openFlat }, Lifts(0), parts, 20, true, 0, 0, false,
                                   0, 1.0, 0, 10.000000000, 10, 10, 10);
            Check(zf1.Runs == 1 && zf1.RunsOpen == 1 && zf1.RunsCounted == 0 && zf1.RunsFlat == 1 && zf1.Attraction == z1.Attraction,
                  w + " ZF1", "Runs 1, RunsOpen 1, RunsCounted 0, RunsFlat 1, A = Z1");
            Flags("ZF1", zf1, Score.ScoreFlags.SummerMode);
            Score.Result wf2 = Row("WF2", Area(2), With(WithOpen(Medium(), 0), openFlat), Lifts(3), parts, 24, true, 0, 3, false,
                                   0, 1.0, 4.5, 20.485281374, 20, 14, 20);
            Check(wf2.Runs == 11 && wf2.RunsOpen == 1 && wf2.RunsCounted == 0 && wf2.RunsFlat == 1 &&
                  wf2.Attraction == w2.Attraction && wf2.PerLodge == w2.PerLodge, w + " WF2", "Runs 11, RunsOpen 1, RunsCounted 0, RunsFlat 1, = W2");
            Flags("WF2", wf2, Score.ScoreFlags.SummerMode);
        }


        private static readonly double[] OpenChoices = { 0, 0, 0.4, 0.45, 0.55, 1, 1, 1 };
        private static readonly double[] EfficiencyChoices = { 0, 0.01, 0.3, 0.75, 1, 1.25 };
        private static readonly double[] RaiseChoices = { 0.4, 0.6, 1.0 };

        private static Score.RunInput RandomRun(Random rng)
        {
            var run = new Score.RunInput();
            int w = rng.Next(7);                       // Rated weight 5, Unrateable 1, Pending 1
            if (w < 5)
            {
                run.State = Score.BandState.Rated;
                run.Band = (byte)rng.Next(5);
            }
            else
            {
                run.State = w == 5 ? Score.BandState.Unrateable : Score.BandState.Pending;
            }
            run.Kind = (byte)rng.Next(4);
            run.AreaM2 = 500.0 + rng.NextDouble() * 300000.0;
            run.Served = rng.NextDouble() < 0.7;
            run.OpenFactor = OpenChoices[rng.Next(OpenChoices.Length)];
            run.OpenEstimated = rng.Next(2) == 0;
            run.BottomM = 800 + rng.Next(800);
            run.TopM = run.BottomM + rng.Next(900);
            return run;
        }

        private static Score.LiftInput RandomLift(Random rng)
        {
            return new Score.LiftInput { Complete = rng.NextDouble() < 0.8, Powered = rng.NextDouble() < 0.8 };
        }

        private static void Properties()
        {
            var rng = new Random(20261004);
            var parts = new List<Score.RunPart>();
            var parts2 = new List<Score.RunPart>();
            int violations = 0, orderFails = 0, squareFails = 0, zeroRunFails = 0;
            const int Areas = 5000;
            for (int n = 0; n < Areas; n++)
            {
                var runs = new List<Score.RunInput>();
                int runCount = rng.Next(12);
                for (int i = 0; i < runCount; i++)
                    runs.Add(RandomRun(rng));
                var lifts = new List<Score.LiftInput>();
                int liftCount = rng.Next(6);
                for (int i = 0; i < liftCount; i++)
                    lifts.Add(RandomLift(rng));
                var area = new Score.AreaInput
                {
                    ActiveLodges = rng.Next(6),
                    InactiveLodges = rng.Next(2),
                    Efficiency = EfficiencyChoices[rng.Next(EfficiencyChoices.Length)],
                    Settings = new Score.ScoreSettings
                    {
                        K = rng.Next(1, 11),
                        BackcountryPercent = 5 * rng.Next(21),
                        SnowGating = rng.NextDouble() < 0.85,
                    },
                };
                Score.Result baseR = Score.Compute(area, runs, lifts, parts);
                var baseParts = new List<Score.RunPart>(parts);
                string w = "score-mono area " + n;

                // 1. add a random run.
                violations += Mono(baseR, area, With(runs, RandomRun(rng)), lifts, parts2, w, "add a run");
                // 2. add a complete, powered lift.
                var moreLifts = new List<Score.LiftInput>(lifts) { new Score.LiftInput { Complete = true, Powered = true } };
                violations += Mono(baseR, area, runs, moreLifts, parts2, w, "add a complete, powered lift");
                // 3. add a lodge (efficiency unchanged).
                Score.AreaInput a3 = area;
                a3.ActiveLodges++;
                violations += Mono(baseR, a3, runs, lifts, parts2, w, "add a lodge");
                if (runs.Count > 0)
                {
                    // 4. make a random run Served.
                    int i4 = rng.Next(runs.Count);
                    Score.RunInput r4 = runs[i4];
                    r4.Served = true;
                    violations += Mono(baseR, area, Replace(runs, i4, r4), lifts, parts2, w, "serve run " + i4);
                    // 5. raise a random run's open factor.
                    int i5 = rng.Next(runs.Count);
                    Score.RunInput r5 = runs[i5];
                    r5.OpenFactor = Math.Max(r5.OpenFactor, RaiseChoices[rng.Next(RaiseChoices.Length)]);
                    violations += Mono(baseR, area, Replace(runs, i5, r5), lifts, parts2, w, "open run " + i5 + " further");
                    // 6. grow a random run by 1.5.
                    int i6 = rng.Next(runs.Count);
                    Score.RunInput r6 = runs[i6];
                    r6.AreaM2 *= 1.5;
                    violations += Mono(baseR, area, Replace(runs, i6, r6), lifts, parts2, w, "grow run " + i6);
                }
                // 7. efficiency → max(own, 1).
                Score.AreaInput a7 = area;
                a7.Efficiency = Math.Max(a7.Efficiency, 1.0);
                violations += Mono(baseR, a7, runs, lifts, parts2, w, "efficiency up to 1");
                // 8. K + 1 (≤ 10).
                Score.AreaInput a8 = area;
                a8.Settings.K = Math.Min(10, a8.Settings.K + 1);
                violations += Mono(baseR, a8, runs, lifts, parts2, w, "K + 1");
                // 9. backcountry + 5 (≤ 100).
                Score.AreaInput a9 = area;
                a9.Settings.BackcountryPercent = Math.Min(100, a9.Settings.BackcountryPercent + 5);
                violations += Mono(baseR, a9, runs, lifts, parts2, w, "backcountry + 5");
                // 10. gating off.
                Score.AreaInput a10 = area;
                a10.Settings.SnowGating = false;
                violations += Mono(baseR, a10, runs, lifts, parts2, w, "gating off");
                // 11. a Pending run becomes Rated with a random band.
                int pendingAt = -1;
                for (int i = 0; i < runs.Count && pendingAt < 0; i++)
                {
                    if (runs[i].State == Score.BandState.Pending)
                        pendingAt = i;
                }
                if (pendingAt >= 0)
                {
                    Score.RunInput r11 = runs[pendingAt];
                    r11.State = Score.BandState.Rated;
                    r11.Band = (byte)rng.Next(5);
                    violations += Mono(baseR, area, Replace(runs, pendingAt, r11), lifts, parts2, w, "rate pending run " + pendingAt);
                }
                // 12. gating on: a run with open factor 0 changes nothing but the per-state counts.
                if (area.Settings.SnowGating)
                {
                    Score.RunInput closed = RandomRun(rng);
                    closed.OpenFactor = 0;
                    Score.Result z = Score.Compute(area, With(runs, closed), lifts, parts2);
                    bool same = z.Attraction == baseR.Attraction && z.PerLodge == baseR.PerLodge &&
                                z.AttractionFullEfficiency == baseR.AttractionFullEfficiency &&
                                BitConverter.DoubleToInt64Bits(z.S) == BitConverter.DoubleToInt64Bits(baseR.S) &&
                                BitConverter.DoubleToInt64Bits(z.AOpen) == BitConverter.DoubleToInt64Bits(baseR.AOpen) &&
                                BitConverter.DoubleToInt64Bits(z.RunsValue) == BitConverter.DoubleToInt64Bits(baseR.RunsValue) &&
                                z.RunsCounted == baseR.RunsCounted && z.RunsOpen == baseR.RunsOpen && z.Variety == baseR.Variety &&
                                z.VerticalM == baseR.VerticalM;
                    if (!Check(same, w, "a run with open factor 0 changed the score (I1)"))
                        zeroRunFails++;
                }

                // Order independence: reversed, then one shuffle (Result exact; parts permuted).
                var reversed = new List<Score.RunInput>(runs);
                reversed.Reverse();
                var reversedLifts = new List<Score.LiftInput>(lifts);
                reversedLifts.Reverse();
                Score.Result rev = Score.Compute(area, reversed, reversedLifts, parts2);
                bool revOk = rev.Equals(baseR) && parts2.Count == baseParts.Count;
                for (int i = 0; revOk && i < parts2.Count; i++)
                    revOk = parts2[i].Equals(baseParts[baseParts.Count - 1 - i]);
                if (!Check(revOk, w, "reversed run list changed the Result or its RunParts"))
                    orderFails++;

                int[] perm = new int[runs.Count];
                for (int i = 0; i < perm.Length; i++)
                    perm[i] = i;
                for (int i = perm.Length - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    int t = perm[i];
                    perm[i] = perm[j];
                    perm[j] = t;
                }
                var shuffled = new List<Score.RunInput>(runs.Count);
                for (int i = 0; i < perm.Length; i++)
                    shuffled.Add(runs[perm[i]]);
                Score.Result shuf = Score.Compute(area, shuffled, lifts, parts2);
                bool shufOk = shuf.Equals(baseR) && parts2.Count == baseParts.Count;
                for (int i = 0; shufOk && i < parts2.Count; i++)
                    shufOk = parts2[i].Equals(baseParts[perm[i]]);
                if (!Check(shufOk, w, "shuffled run list changed the Result or its RunParts"))
                    orderFails++;

                // Sum of squares (M2): |N × PerLodge² − A²| ≤ N × (A / √N + 0.25).
                int lodges = baseR.ActiveLodges;
                if (lodges >= 1)
                {
                    double a = baseR.Attraction;
                    double error = Math.Abs(lodges * (double)baseR.PerLodge * baseR.PerLodge - a * a);
                    double bound = lodges * (a / Math.Sqrt(lodges) + 0.25);
                    if (!Check(error <= bound, w, $"sum of squares: |{lodges} × {baseR.PerLodge}² − {a}²| = {error} > {bound}"))
                        squareFails++;
                }
            }
            Check(violations == 0 && orderFails == 0 && squareFails == 0 && zeroRunFails == 0, "score-mono",
                  $"{Areas} areas: {violations} monotonicity violation(s), {zeroRunFails} zero-run, {orderFails} order, {squareFails} sum-of-squares failure(s)");
        }

        /// <summary>One monotone mutation: A' ≥ A (1 violation when not).</summary>
        private static int Mono(Score.Result before, Score.AreaInput area, List<Score.RunInput> runs, List<Score.LiftInput> lifts,
            List<Score.RunPart> parts, string where, string what)
        {
            Score.Result after = Score.Compute(area, runs, lifts, parts);
            return Check(after.Attraction >= before.Attraction, where, $"{what}: A {before.Attraction} → {after.Attraction}") ? 0 : 1;
        }


        private static void NoThrow()
        {
            const string w = "score-nothrow";
            double[] bad = { double.NaN, double.PositiveInfinity, double.NegativeInfinity };
            var parts = new List<Score.RunPart>();
            int cases = 0, fails = 0;
            for (int field = 0; field < 5; field++)
            {
                for (int b = 0; b < bad.Length; b++)
                {
                    var runs = Small();
                    Score.RunInput r = runs[1];
                    double effIn = 1.0;
                    switch (field)
                    {
                        case 0: r.AreaM2 = bad[b]; break;
                        case 1: r.OpenFactor = bad[b]; break;
                        case 2: r.TopM = bad[b]; break;
                        case 3: r.BottomM = bad[b]; break;
                        default: effIn = bad[b]; break;
                    }
                    runs[1] = r;
                    cases++;
                    try
                    {
                        Score.Result res = Score.Compute(Area(2, effIn), runs, Lifts(1), parts);
                        bool sane = res.Attraction >= 0 && res.Attraction <= Score.MaxAttraction && res.PerLodge >= 0 &&
                                    res.AttractionFullEfficiency >= 0 && res.AttractionFullEfficiency <= Score.MaxAttraction &&
                                    !double.IsNaN(res.RunsValue) && !double.IsNaN(res.S) && !double.IsNaN(res.AOpen) &&
                                    !double.IsNaN(res.VerticalM) && parts.Count == 3;
                        if (!Check(sane, w, $"field {field} = {bad[b]}: A {res.Attraction}, S {res.S}, AOpen {res.AOpen}"))
                            fails++;
                    }
                    catch (Exception e)
                    {
                        Check(false, w, $"field {field} = {bad[b]} threw {e.GetType().Name}: {e.Message}");
                        fails++;
                    }
                }
            }
            // Empty and null lists.
            try
            {
                Score.Result empty = Score.Compute(Area(1), new List<Score.RunInput>(), new List<Score.LiftInput>(), parts);
                Check(empty.Attraction == 10 && parts.Count == 0 && empty.Flags == (Score.ScoreFlags.SummerMode | Score.ScoreFlags.NoRuns),
                      w, "empty lists: A 10 (Z1), no parts");
                Score.Result nulls = Score.Compute(Area(1), null, null, null);
                Check(nulls.Attraction == 10, w, "null lists: A 10 (Z1)");
                Score.Result none = Score.Compute(default(Score.AreaInput), new List<Score.RunInput>(), new List<Score.LiftInput>(), parts);
                Check(none.Attraction == 0 && none.K == Score.MinK && (none.Flags & Score.ScoreFlags.NoLodge) != 0, w,
                      "default AreaInput: A 0, K clamped to 1, NoLodge");
            }
            catch (Exception e)
            {
                Check(false, w, $"empty / null lists threw {e.GetType().Name}: {e.Message}");
            }
            Check(fails == 0, w, $"{cases} non-finite input case(s), {fails} failure(s)");
        }
    }
}
