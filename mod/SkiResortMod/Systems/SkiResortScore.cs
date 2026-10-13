// SkiResortScore — attraction model v2 of Round E1 (decisions D1-D12; docs/research/econ-round/spec/
// ch1-model.md §2 formulas, §5 this class, §15 the tests): ONE attraction score per ski area (one
// Welcome Sign), from its member runs (band, size with the 1 ha knee M3, type, lift service, open
// factor), its complete and powered lifts, its active lodges and the sign's efficiency. Consumers:
// SkiResortAttractivenessSystem (Compute on every recompute, the write lists), SkiResortStatsSystem /
// HubRules (the hub reads this Result, never recomputes — D10), tests/ResortBatch ScoreTests.
//
// HISTORY. Stage 0 (Round E1, S0, 2026-10-04; e1-spec I-07) wrote the constants and nested types
// (ch1 §5 verbatim, as edited by e1-spec R1: no IsLiftPowered, no PowerWarningMask — the one power
// rule is LiftServedRule.IsPowered). Stream S1 (2026-10-04) wrote the bodies, exactly ch1 §5 "Compute
// order of work" (steps 1-8), and owns the file.
//
// COMPUTE IN ONE BREATH (ch1 §2.1): per run v = band × size × type × served × open (each factor ≥ 0;
// open = clamp01(OpenFactor) with gating on, NaN → 0, else 1); a run COUNTS when v > 0 (I1) and only
// counted runs feed RunsValue, variety, vertical and the summer test (I2: summer ⇔ RunsCounted == 0).
// RunsValue is the sum of the counted values in ASCENDING order (second pass, step 2b): the sorted
// sequence is the same for any permutation of the runs, so the Result is bit-identical whatever order
// the chunk walk returns (Result.Equals stays exact; the attractiveness Generation does not move on a
// chunk reorder). A is clamped BEFORE rounding (step 7): a non-finite double never reaches an int cast.
//
// ROUND S1 "Ski days" (2026-10-05, stream A; docs/research/ski-days/spec/ch1-rules.md §4): the same one
// recompute also yields the ski-day inputs, so score, capacity and the D9 gate come from one snapshot
// (SkiResortAttractivenessSystem publishes Result with the hub closed). Five Result fields, appended after
// Flags and in Equals: RunsValueFull (Σ band × size × type × served over ALL member runs, open ignored: the
// denominator of SkiDayRules.OpenShare), RunsCapacity (Σ RunSkiersPerDay over counted runs: D21 skiers per
// hectare by band, natural × 0.5, × served × open), NightRunsCounted (D20 night hours), TopKnown / TopM (the
// counted-run top, D19 powder). Both sums are ascending like RunsValue, so Equals stays exact in any run
// order. Nothing existing changes value; no new Generation trigger (SameRun already compares every input).
//
// CATALOG PHASE B (2026-10-11, economy group; docs/research/art/assets-2026-10/code-round-2/round-2-contract.md §7.1,
// with round-2-contract-skeptic.md MUST 1 and MUST 3 applied). Additive, and bit-identical for every Phase A input:
//   * THE LODGE LADDER (R11). Per active lodge a catalog row (SkiCatalog.Lodges: B base, E extra, μ summer share,
//     μ summit when summit-served). LodgeBase (WINTER selection, μ = 1 for every lodge) = B(best) + min(Σ E(others),
//     LodgeExtraCap 24); LodgeApplied = the SEASON's selection: in winter = LodgeBase, in summer best B·μ + extras where
//     μ_i = (SummitServed && μ summit > μ) ? μ summit : μ, extras = (Σ E·μ over the others, ascending) × cap / Σ E when
//     Σ E > cap (sum first, then scale: uniform μ gives μ × min(Σ E, 24) exactly). EVERY active lodge counts, of any tier
//     and in any number: the one-luxury-lodge cap of the first Phase B build (D3, LuxuryPerArea, LuxuryIgnored) was removed
//     on 2026-10-11 (Richard: no limit on how many a player builds; extra lodges add diminishing attractiveness until the
//     24-point extras cap, which is now the ONLY limiter). The ladder numbers for A-frame, hut and chalet lodges are
//     unchanged. ORDER-FREE (MUST 3):
//     "better" = higher B·μ, then higher E, then higher Capacity, then lower LodgeIndex — two lodges that tie on all
//     four are the same row with the same μ, so which one counts changes no number. AOpen = LodgeApplied + K × √S
//     (for A-frames only LodgeApplied = LodgeBase × 0.5 exactly in summer, so T1-T3 and W1-W3 are bit-identical).
//     LodgeShare = LodgeBase > 0 ? applied / base : (summer ? 0.5 : 1) (display; MUST 1 keeps the season share when
//     there is no lodge). AreaInput.Lodges == null ⇒ ActiveLodges A-frame rows (the closed form LodgeBaseOf(n) and
//     × 0.5 in summer — LadderTests proves it equals the ladder over explicit A-frame lists bit for bit).
//   * LIFTS PER TYPE (R12): LiftInput.TypeIndex (SkiCatalog.Lifts index, 0 = fixed-grip). LiftsValue = Σ ScoreValue
//     (winter, displayed), LiftsApplied = Σ (summer ? SummerShare × ScoreValue : ScoreValue), both ascending; S uses
//     LiftsApplied; LiftShare = applied / value (display).
//   * SCHOOL (R13, D13): S += SchoolRule.ScoreValueOf(area.SchoolActive, GreenCounted) before variety and vertical.
//   * PATROL (R13, D12): RunInput.Patrolled → PatrolRule.TypeFactorOf / ServedFactorOf in the run part.
//   Result gains LodgeApplied, LodgeExtraApplied, LiftsApplied, SchoolValue, LodgeBestIndex,
//   PriceTolerance (max over the active lodges), RunsPatrolled, SchoolActive, GreenCounted — all in Equals.
//
// PURE FILE: `using System;` and `using System.Collections.Generic;` only (no Unity.Entities, no Game
// types, no Unity.Mathematics), + SkiCatalog consts and rows, SchoolRule and PatrolRule (Catalog Phase A R8: LiftValue
// aliases SkiCatalog.FixedGrip.ScoreValue; SkiCatalog.cs, SchoolRule.cs and PatrolRule.cs are linked wherever this file
// is). Main thread and the test host only (the rows are managed statics). net48 traps (the mod build is the API gate): no Math.Clamp, MathF,
// double.IsFinite, Span<T> — the private Clamp / IsFinite helpers below stand in. Arithmetic is double
// (SignEfficiency float, as vanilla's). Compute uses static scratch lists (one in E1, three since S1): main
// thread and the test host only (not re-entrant, not thread-safe).

using System;
using System.Collections.Generic;

namespace SkiResortMod.Systems
{
    /// <summary>Attraction model v2 (Round E1, D1-D12): one score per ski area. Pure.</summary>
    public static class SkiResortScore
    {
        // ── constants (§2.2) ─────────────────────────────────────────────
        public const double RefAreaM2 = 20000.0;
        public const double SizeLogSlope = 0.3;
        public const double SizeFloor = 0.25;
        public const double SizeCap = 2.0;
        public const double SizeKneeM2 = 10000.0;     // M3 / e1-spec DV21: proportional below 1 ha (1641.699972 = D5's floor kept)
        public const double UnrateableValue = 3.0;
        public const double NightFactor = 1.25;
        public const double NaturalUnrateableFactor = 0.5;
        public const double LiftValue = SkiCatalog.FixedGrip.ScoreValue;   // Catalog Phase A (ext row 14; was SkiResortScore.cs:53 = 6.0): const alias (D4); ScoreTests :69 guards 6
        public const double VarietyFactor = 1.15;
        public const double VerticalRefM = 600.0;
        public const double FirstLodgeBase = 20.0;     // = SkiCatalog.Lodges[0].B (the A-frame; LadderTests guard)
        public const double LodgeExtra = 4.0;          // = SkiCatalog.Lodges[0].E
        // Catalog Phase B (round-2-contract §7.1, R11; was SkiResortScore.cs:59 = 12.0): const alias; ScoreTests guards 24.
        public const double LodgeExtraCap = SkiCatalog.Ladder.ExtraCap;
        public const double SummerLodgeShare = 0.5;    // μ of the A-frame (= SkiCatalog.Lodges[0].Mu); the no-lodge summer share (MUST 1)
        public const double SummerLiftShare = 0.25;    // λ of the fixed-grip (= SkiCatalog.FixedGrip.SummerShare); the no-lift summer share
        public const int MaxAttraction = 200;
        public const int MinK = 1, MaxK = 10;
        // (e1-spec R1: no power constant here — the one power rule is LiftServedRule.IsPowered.)

        // Round S1 ski-day capacity (ski-days ch1 §4.1; D21). Unrateable: ch1 §11 N-2 (black's, conservative).
        public const double SkiersPerHaGreen = 45.0, SkiersPerHaBlue = 25.0, SkiersPerHaBlack = 15.0,
                            SkiersPerHaDoubleBlack = 7.5, SkiersPerHaUnrateable = 15.0;
        public const double NaturalSkiersFactor = 0.5;                  // D21
        public const double MaxCapacityAreaM2 = 20000000.0;             // 2,000 ha = SkiRunFees.MaxAreaHa

        // RunKind values (EntityRef.cs RunKind; Natural = 3 is appended by the natural-run chapter).
        public const byte KindGroomed = 0, KindSnowmaking = 1, KindNight = 2, KindNatural = 3;
        // SlopeBand values (SkiSlopeClassifier.SlopeBand).
        public const byte BandFlat = 0, BandGreen = 1, BandBlue = 2, BandBlack = 3, BandDoubleBlack = 4;

        public enum BandState : byte { Rated = 0, Unrateable = 1, Pending = 2 }

        [Flags]
        public enum ScoreFlags : ushort   // not "Flags": avoids any clash with the [Flags] attribute and the field name
        {
            None = 0,
            NoLodge = 1,          // N = 0: A = 0 ("No lodge: guests have nowhere to arrive", D2)
            SummerMode = 2,       // no counted run (RunsCounted == 0, I2): μ 0.5, λ 0.25 (D8)
            PendingRatings = 4,   // ≥ 1 member run has no band yet (it counts 0)
            SnowGatingOff = 8,    // the setting: every run counts fully open (D13)
            OpenEstimated = 16,   // ≥ 1 open run's open factor comes from the temperature model (D13)
            EfficiencyZero = 32,  // Efficiency ≤ 0 (or NaN): A = 0 (D9)
            Clamped = 64,         // A_open × Efficiency > 200 (N ≥ 1)
            NoRuns = 128,         // the area has no member run
        }

        public struct RunInput
        {
            public byte Kind;            // KindGroomed .. KindNatural; other values are treated as groomed
            public BandState State;      // Rated: Band holds the SlopeBand
            public byte Band;            // 0..4, read only when State == Rated
            public double AreaM2;        // plan area (Geometry.m_SurfaceArea); ≤ 0 or NaN → size 0 (the run counts 0)
            public bool Served;          // the lift chapter's served rule over complete, powered lifts (LiftServedRule.Assign)
            public double OpenFactor;    // 0..1 = GetRunSnow(...).Multiplier (D13, e1-spec R3); ignored when SnowGating is off
            public bool OpenEstimated;   // GetRunSnow(...).Source == RunSnowSource.Estimated
            public double TopM, BottomM; // heights for the vertical; non-finite values are skipped
            public bool Patrolled;       // Catalog Phase B (R13, D12): PatrolRule.Patrolled(run top xz, the area's active patrol huts)
        }

        public struct LiftInput
        {
            public bool Complete;        // census State.Complete
            public bool Powered;         // LiftServedRule.IsPowered(lift.Power) from CopyServeLifts (e1-spec R1)
            public int TypeIndex;        // Catalog Phase B (R12): SkiCatalog.Lifts index of the census row; 0 = fixedGrip (default)
        }

        /// <summary>Catalog Phase B (R11): one ACTIVE lodge of the area. LodgeIndex = SkiCatalog.Lodges index (out of
        /// range reads the A-frame row 0); SummitServed = within Ladder.SummitServedRadiusM of a complete, powered lift's
        /// top stub tip of the same area (LiftServedRule.SummitServed; only rows with μ summit &gt; μ read it).</summary>
        public struct LodgeInput
        {
            public int LodgeIndex;
            public bool SummitServed;
        }

        public struct ScoreSettings      // not "Settings": the namespace SkiResortMod.Settings is in scope in Systems/
        {
            public int K;                // AttractivenessScaleK; clamped to MinK..MaxK
            public int BackcountryPercent; // clamped to 0..100
            public bool SnowGating;      // EnableSnowGating
        }

        public struct AreaInput
        {
            public int ActiveLodges;     // N: lodges in the sign's InstalledUpgrade buffer, not Inactive (read when Lodges == null)
            public int InactiveLodges;   // echoed for the hub
            public double Efficiency;    // SignEfficiency(...) of the sign (§8)
            public ScoreSettings Settings;
            // Catalog Phase B (round-2-contract §7.1): the active lodges by tier. null = ActiveLodges A-frame rows (every
            // Phase A caller and test row); non-null = N is Lodges.Count (the system passes both, equal).
            public List<LodgeInput> Lodges;
            public bool SchoolActive;    // Catalog Phase B (R13): the area's ski school is installed and not Inactive
        }

        public struct RunPart : IEquatable<RunPart>
        {
            public double BandValue, SizeFactor, TypeFactor, ServedFactor, OpenFactor; // effective factors
            public double Value;         // product of the five
            public bool Counted;         // Value > 0

            /// <summary>Exact field compare (each part depends only on its own run). Doubles bit for bit
            /// (Same), so a NaN — which Compute never produces — would still compare equal to itself.</summary>
            public bool Equals(RunPart o)
            {
                return Same(BandValue, o.BandValue) && Same(SizeFactor, o.SizeFactor) &&
                       Same(TypeFactor, o.TypeFactor) && Same(ServedFactor, o.ServedFactor) &&
                       Same(OpenFactor, o.OpenFactor) && Same(Value, o.Value) && Counted == o.Counted;
            }
        }

        public struct Result : IEquatable<Result>
        {
            public int Attraction;               // A
            public int AttractionFullEfficiency; // N ≥ 1 ? clamp(round(A_open), 0, 200) : 0 — "at full efficiency"
            public int PerLodge;                 // round(A / sqrt(N)) — the value each active lodge carries
            public int ActiveLodges, InactiveLodges;
            public double LodgeBase, LodgeShare; // LodgeBase(N), μ
            public double RunsValue;             // Σ v_i
            public int LiftsCounted;             // complete AND powered
            public double LiftsValue, LiftShare; // 6 × LiftsCounted, λ
            public bool Variety;
            public double VarietyMultiplier;     // 1.15 or 1
            public double VerticalM, VerticalMultiplier;
            public double S, AOpen, Efficiency;
            public int K, BackcountryPercent;    // the clamped settings used
            public int Runs, RunsOpen, RunsCounted, RunsPending, RunsUnrateable, RunsFlat,
                       RunsUnserved, RunsEstimated;  // RunsUnserved: !Served over ALL member runs;
                                                     // RunsEstimated: open runs whose factor is estimated
            public int Lifts, LiftsComplete, LiftsOpenSection, LiftsUnpowered;
            public ScoreFlags Flags;
            // Round S1 (ski-days spec ch1 §4.2): the ski-day inputs of the same recompute; in Equals.
            public double RunsValueFull;  // Σ BandValue × SizeFactor × TypeFactor × ServedFactor over ALL member runs (open ignored), terms > 0, ascending
            public double RunsCapacity;   // Σ RunSkiersPerDay over counted runs, terms > 0, ascending
            public int NightRunsCounted;  // counted runs of KindNight (D20 night hours)
            public bool TopKnown;         // ≥ 1 counted run with finite heights (= the existing `heights`)
            public double TopM;           // highest counted-run top (the existing `top`); 0 when !TopKnown
            // Catalog Phase B (round-2-contract §7.1; header CATALOG PHASE B): appended, all in Equals.
            public double LodgeApplied;      // the season's lodge term of AOpen (winter = LodgeBase); LodgeBase = the winter value
            public double LodgeExtraApplied; // the season's extras after the cap (LodgeApplied − best B·μ)
            public double LiftsApplied;      // Σ per counted lift (summer ? λ × value : value), ascending: the lift term of S
            public double SchoolValue;       // SchoolRule.ScoreValueOf(SchoolActive, GreenCounted), added to S
            public int LodgeBestIndex;       // SkiCatalog.Lodges index of the season's best lodge; −1 without a lodge
            public int PriceTolerance;       // max PriceTolerance over the active lodges (0 without)
            public int RunsPatrolled;        // member runs with RunInput.Patrolled (all member runs, counted or not)
            public bool SchoolActive;        // AreaInput.SchoolActive echoed
            public bool GreenCounted;        // a counted run Rated Green (the variety flag `green`)

            /// <summary>Exact field compare (Generation, §12): bit-exact in any run order because
            /// RunsValue (and, since S1, RunsValueFull and RunsCapacity) is summed in ascending order.
            /// Every field, doubles bit for bit (Same). Catalog Phase B: the lift sums are ascending too and the
            /// ladder's selection is order-free (header), so the Result is bit-exact in any lift or lodge order.</summary>
            public bool Equals(Result o)
            {
                return Attraction == o.Attraction && AttractionFullEfficiency == o.AttractionFullEfficiency &&
                       PerLodge == o.PerLodge && ActiveLodges == o.ActiveLodges && InactiveLodges == o.InactiveLodges &&
                       Same(LodgeBase, o.LodgeBase) && Same(LodgeShare, o.LodgeShare) && Same(RunsValue, o.RunsValue) &&
                       LiftsCounted == o.LiftsCounted && Same(LiftsValue, o.LiftsValue) && Same(LiftShare, o.LiftShare) &&
                       Variety == o.Variety && Same(VarietyMultiplier, o.VarietyMultiplier) &&
                       Same(VerticalM, o.VerticalM) && Same(VerticalMultiplier, o.VerticalMultiplier) &&
                       Same(S, o.S) && Same(AOpen, o.AOpen) && Same(Efficiency, o.Efficiency) &&
                       K == o.K && BackcountryPercent == o.BackcountryPercent &&
                       Runs == o.Runs && RunsOpen == o.RunsOpen && RunsCounted == o.RunsCounted &&
                       RunsPending == o.RunsPending && RunsUnrateable == o.RunsUnrateable && RunsFlat == o.RunsFlat &&
                       RunsUnserved == o.RunsUnserved && RunsEstimated == o.RunsEstimated &&
                       Lifts == o.Lifts && LiftsComplete == o.LiftsComplete && LiftsOpenSection == o.LiftsOpenSection &&
                       LiftsUnpowered == o.LiftsUnpowered && Flags == o.Flags &&
                       Same(RunsValueFull, o.RunsValueFull) && Same(RunsCapacity, o.RunsCapacity) &&
                       NightRunsCounted == o.NightRunsCounted && TopKnown == o.TopKnown && Same(TopM, o.TopM) &&
                       Same(LodgeApplied, o.LodgeApplied) && Same(LodgeExtraApplied, o.LodgeExtraApplied) &&
                       Same(LiftsApplied, o.LiftsApplied) && Same(SchoolValue, o.SchoolValue) &&
                       LodgeBestIndex == o.LodgeBestIndex &&
                       PriceTolerance == o.PriceTolerance && RunsPatrolled == o.RunsPatrolled &&
                       SchoolActive == o.SchoolActive && GreenCounted == o.GreenCounted;
            }
        }

        /// <summary>Bit-for-bit double equality (BitConverter: net48 and net8; no allocation).</summary>
        private static bool Same(double a, double b)
        {
            return BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);
        }

        /// <summary>net48 has no Math.Clamp. NaN passes through (callers never pass one).</summary>
        private static double Clamp(double x, double lo, double hi)
        {
            return x < lo ? lo : (x > hi ? hi : x);
        }

        /// <summary>net48 has no double.IsFinite.</summary>
        private static bool IsFinite(double x)
        {
            return !double.IsNaN(x) && !double.IsInfinity(x);
        }

        /// <summary>Sorts <paramref name="values"/> and sums them ascending: the same sorted sequence, so the
        /// same sum bit for bit, for any order the values were added in (step 2b).</summary>
        private static double AscendingSum(List<double> values)
        {
            values.Sort();
            double sum = 0.0;
            for (int i = 0; i < values.Count; i++)
                sum += values[i];
            return sum;
        }

        /// <summary>Compute's scratch for the ascending sum of counted values (second pass). Main
        /// thread and the test host only: Compute is not re-entrant and not thread-safe.</summary>
        private static readonly List<double> s_CountedValues = new List<double>(64);

        /// <summary>Round S1 (ch1 §4.3): the same scratch rule for the full values (every member run) and the
        /// counted runs' skiers, summed ascending into RunsValueFull / RunsCapacity.</summary>
        private static readonly List<double> s_FullValues = new List<double>(64);
        private static readonly List<double> s_CapacityValues = new List<double>(64);

        /// <summary>Catalog Phase B: the counted lifts' winter values and season values (summed ascending, step 5) and
        /// the ladder's per-lodge scratch (LodgeLadder). Same rule as the lists above: main thread and the test host.</summary>
        private static readonly List<double> s_LiftValues = new List<double>(16);
        private static readonly List<double> s_LiftApplied = new List<double>(16);
        private static readonly List<double> s_LadderV = new List<double>(8);      // per lodge: B·μ of the selection
        private static readonly List<double> s_LadderSumA = new List<double>(8);   // sort scratch (Σ E)
        private static readonly List<double> s_LadderSumB = new List<double>(8);   // sort scratch (Σ E·μ)

        /// <summary>The model (§2.1). <paramref name="parts"/> is cleared, then gets one RunPart per
        /// run, in the order of <paramref name="runs"/>. Never throws for any finite or non-finite
        /// input; never allocates beyond growing <paramref name="parts"/> and the static scratch lists.
        /// The Result is bit-identical for any permutation of <paramref name="runs"/> (and
        /// <paramref name="lifts"/>).</summary>
        public static Result Compute(in AreaInput area, List<RunInput> runs, List<LiftInput> lifts,
                                     List<RunPart> parts)
        {
            // Step 1: scratch, clamped settings.
            parts?.Clear();
            s_CountedValues.Clear();
            s_FullValues.Clear();
            s_CapacityValues.Clear();
            s_LiftValues.Clear();      // Catalog Phase B
            s_LiftApplied.Clear();
            var r = new Result();
            int k = area.Settings.K;
            r.K = k < MinK ? MinK : (k > MaxK ? MaxK : k);
            int bcPct = area.Settings.BackcountryPercent;
            r.BackcountryPercent = bcPct < 0 ? 0 : (bcPct > 100 ? 100 : bcPct);
            double bc = r.BackcountryPercent / 100.0;
            bool gating = area.Settings.SnowGating;

            // Step 2: per run, in list order — counts, the five factors, the part; counted runs feed
            // the value list, the height range and the band flags (I1).
            bool green = false, blue = false, black = false, heights = false;
            double top = double.NegativeInfinity, bottom = double.PositiveInfinity;
            int runCount = runs?.Count ?? 0;
            for (int i = 0; i < runCount; i++)
            {
                RunInput run = runs[i];
                double open;
                if (gating)
                {
                    open = run.OpenFactor;
                    if (!(open > 0.0))
                        open = 0.0;          // NaN and ≤ 0
                    else if (open > 1.0)
                        open = 1.0;
                }
                else
                {
                    open = 1.0;              // D13: gating off — every run fully open
                }

                r.Runs++;
                if (open > 0.0)
                {
                    r.RunsOpen++;
                    if (gating && run.OpenEstimated)
                        r.RunsEstimated++;
                }
                if (run.State == BandState.Pending)
                    r.RunsPending++;
                else if (run.State == BandState.Unrateable)
                    r.RunsUnrateable++;
                else if (run.State == BandState.Rated && run.Band == BandFlat)
                    r.RunsFlat++;
                if (!run.Served)
                    r.RunsUnserved++;
                if (run.Patrolled)
                    r.RunsPatrolled++;   // Catalog Phase B (R13)

                // Catalog Phase B (round-2-contract §7.1, D12; was SkiResortScore.cs:293-294 TypeFactorOf(...) and
                // `run.Served ? 1.0 : bc`): the patrol rule wraps both; an unpatrolled run's factors are today's bit for bit.
                var part = new RunPart
                {
                    BandValue = BandValueOf(run.State, run.Band),
                    SizeFactor = SizeFactor(run.AreaM2),
                    TypeFactor = PatrolRule.TypeFactorOf(TypeFactorOf(run.Kind, run.State, run.Band), run.Patrolled, run.State, run.Band),
                    ServedFactor = PatrolRule.ServedFactorOf(run.Served, run.Patrolled, bc),
                    OpenFactor = open,
                };
                // Left to right, as Appendix A: ((((band × size) × type) × served) × open).
                part.Value = part.BandValue * part.SizeFactor * part.TypeFactor * part.ServedFactor * part.OpenFactor;
                part.Counted = part.Value > 0.0;
                parts?.Add(part);
                // Round S1: the run's value with the open factor ignored (OpenShare's denominator), the same
                // left-to-right product without its last factor.
                double full = part.BandValue * part.SizeFactor * part.TypeFactor * part.ServedFactor;
                if (full > 0.0)
                    s_FullValues.Add(full);

                if (!part.Counted)
                    continue;
                r.RunsCounted++;
                s_CountedValues.Add(part.Value);
                double skiers = RunSkiersPerDay(run, part);   // Round S1 (D21)
                if (skiers > 0.0)
                    s_CapacityValues.Add(skiers);
                if (run.Kind == KindNight)
                    r.NightRunsCounted++;
                if (IsFinite(run.TopM) && IsFinite(run.BottomM))
                {
                    if (run.TopM > top)
                        top = run.TopM;
                    if (run.BottomM < bottom)
                        bottom = run.BottomM;
                    heights = true;
                }
                if (run.State == BandState.Rated)
                {
                    if (run.Band == BandGreen)
                        green = true;
                    else if (run.Band == BandBlue)
                        blue = true;
                    else if (run.Band == BandBlack || run.Band == BandDoubleBlack)
                        black = true;
                }
            }

            // Step 2b: the order-independent sums (ascending; every value is finite and > 0).
            r.RunsValue = AscendingSum(s_CountedValues);
            r.RunsValueFull = AscendingSum(s_FullValues);       // Round S1
            r.RunsCapacity = AscendingSum(s_CapacityValues);    // Round S1

            // Step 3: summer = no counted run (I2, second pass). Catalog Phase B: the two shares are DISPLAY values now,
            // set in steps 5 and 6 from the per-type lift sums and the ladder (was :342-343, the season's μ / λ).
            bool summer = r.RunsCounted == 0;

            // Step 4: lifts (the caller passes only lifts whose two stations exist, ch1 §6). Catalog Phase B (R12): a
            // counted lift's value and summer share are its type's row (SkiCatalog.LiftByIndex(TypeIndex); an unknown
            // index reads the fixed-grip row 0, as the census's NoType rows never reach here: they are Unfinished).
            int liftCount = lifts?.Count ?? 0;
            for (int i = 0; i < liftCount; i++)
            {
                LiftInput lift = lifts[i];
                r.Lifts++;
                if (lift.Complete)
                {
                    r.LiftsComplete++;
                    if (lift.Powered)
                    {
                        r.LiftsCounted++;
                        LiftTypeDef def = SkiCatalog.LiftByIndex(lift.TypeIndex) ?? SkiCatalog.LiftByIndex(0);
                        double value = def != null ? def.ScoreValue : LiftValue;
                        double share = def != null ? def.SummerShare : SummerLiftShare;
                        s_LiftValues.Add(value);
                        s_LiftApplied.Add(summer ? share * value : value);
                    }
                    else
                    {
                        r.LiftsUnpowered++;
                    }
                }
                else
                {
                    r.LiftsOpenSection++;
                }
            }

            // Step 5: lift value, variety, vertical (0 when no counted run had finite heights). Catalog Phase B (was
            // :366 LiftValue × LiftsCounted): both sums ascending (bit-exact in any lift order); for fixed-grip lifts
            // Σ 6 = 6n and Σ 1.5 = 1.5n = 0.25 × 6n exactly, so every Phase A row is bit-identical. LiftShare = the
            // applied share of the value (display; the season's λ without a counted lift).
            r.LiftsValue = AscendingSum(s_LiftValues);
            r.LiftsApplied = AscendingSum(s_LiftApplied);
            r.LiftShare = r.LiftsValue > 0.0 ? r.LiftsApplied / r.LiftsValue : (summer ? SummerLiftShare : 1.0);
            r.Variety = green && blue && black;
            r.VarietyMultiplier = r.Variety ? VarietyFactor : 1.0;
            r.VerticalM = heights ? Math.Max(0.0, top - bottom) : 0.0;
            r.VerticalMultiplier = 1.0 + Clamp(r.VerticalM / VerticalRefM, 0.0, 1.0);
            r.TopKnown = heights;                  // Round S1: the area top for powder (D19)
            r.TopM = heights ? top : 0.0;

            // Step 6: S, lodge base, A_open, efficiency (NaN and ≤ 0 → 0).
            // Catalog Phase B (round-2-contract §7.1; was :375 (RunsValue + LiftShare × LiftsValue), :376-380 LodgeBaseOf(n)
            // and LodgeBase × LodgeShare): the school joins S before variety and vertical (+ 0.0 without one: bit for bit);
            // the lodge term is the ladder's season value (header CATALOG PHASE B).
            r.GreenCounted = green;
            r.SchoolActive = area.SchoolActive;
            r.SchoolValue = SchoolRule.ScoreValueOf(area.SchoolActive, green);
            r.S = (r.RunsValue + r.LiftsApplied + r.SchoolValue) * r.VarietyMultiplier * r.VerticalMultiplier;
            int n;
            if (area.Lodges != null)
            {
                n = area.Lodges.Count;
                LadderOut ladder = LodgeLadder(area.Lodges, summer);
                r.LodgeBase = ladder.Base;
                r.LodgeApplied = ladder.Applied;
                r.LodgeExtraApplied = ladder.ExtraApplied;
                r.LodgeBestIndex = ladder.BestIndex;
                r.PriceTolerance = ladder.PriceTolerance;
            }
            else
            {
                // n A-frame rows (LodgeIndex 0, not served): the ladder's closed form — winter B + min(4(n−1), 24), summer
                // exactly half (μ 0.5 on the best and on every extra; the cap scales the half sum to exactly 12).
                n = area.ActiveLodges > 0 ? area.ActiveLodges : 0;
                r.LodgeBase = LodgeBaseOf(n);
                r.LodgeApplied = summer ? r.LodgeBase * SummerLodgeShare : r.LodgeBase;
                double extra = n >= 1 ? Math.Min(LodgeExtra * (n - 1.0), LodgeExtraCap) : 0.0;
                r.LodgeExtraApplied = summer ? extra * SummerLodgeShare : extra;
                r.LodgeBestIndex = n >= 1 ? 0 : -1;
                r.PriceTolerance = n >= 1 && SkiCatalog.Lodges.Count > 0 ? SkiCatalog.Lodges[0].PriceTolerance : 0;
            }
            r.ActiveLodges = n;
            r.InactiveLodges = area.InactiveLodges > 0 ? area.InactiveLodges : 0;
            r.LodgeShare = r.LodgeBase > 0.0 ? r.LodgeApplied / r.LodgeBase : (summer ? SummerLodgeShare : 1.0);   // MUST 1
            r.AOpen = r.LodgeApplied + r.K * Math.Sqrt(r.S);
            double eff = area.Efficiency > 0.0 ? area.Efficiency : 0.0;
            r.Efficiency = eff;

            // Step 7: clamp before rounding (a non-finite double never reaches the int cast).
            ScoreFlags flags = ScoreFlags.None;
            if (n >= 1)
            {
                double raw = r.AOpen * eff;
                if (!(raw >= 0.0))
                    raw = 0.0;
                if (raw > MaxAttraction)
                {
                    flags |= ScoreFlags.Clamped;
                    raw = MaxAttraction;
                }
                r.Attraction = RoundToEven(raw);

                double full = r.AOpen;
                if (!(full >= 0.0))
                    full = 0.0;
                if (full > MaxAttraction)
                    full = MaxAttraction;
                r.AttractionFullEfficiency = RoundToEven(full);
                r.PerLodge = RoundToEven(r.Attraction / Math.Sqrt(n));
            }
            else
            {
                flags |= ScoreFlags.NoLodge;   // A, full and PerLodge stay 0
            }

            // Step 8: flags.
            if (summer)
                flags |= ScoreFlags.SummerMode;
            if (r.RunsPending > 0)
                flags |= ScoreFlags.PendingRatings;
            if (!gating)
                flags |= ScoreFlags.SnowGatingOff;
            if (r.RunsEstimated > 0)
                flags |= ScoreFlags.OpenEstimated;
            if (eff == 0.0)
                flags |= ScoreFlags.EfficiencyZero;
            if (r.Runs == 0)
                flags |= ScoreFlags.NoRuns;
            r.Flags = flags;
            return r;
        }

        /// <summary>§2.1 size_i (knee M3): ≤ 0 or NaN → 0; below SizeKneeM2 proportional
        /// (SizeLog(knee) × a / knee); from the knee up SizeLog(a) = clamp(1 + 0.3 ln(a / 2 ha), 0.25, 2)
        /// (+∞ → SizeCap).</summary>
        public static double SizeFactor(double areaM2)
        {
            if (!(areaM2 > 0.0))
                return 0.0;
            if (areaM2 < SizeKneeM2)
                return SizeLog(SizeKneeM2) * areaM2 / SizeKneeM2;
            return SizeLog(areaM2);
        }

        private static double SizeLog(double areaM2)
        {
            if (double.IsPositiveInfinity(areaM2))
                return SizeCap;
            return Clamp(1.0 + SizeLogSlope * Math.Log(areaM2 / RefAreaM2), SizeFloor, SizeCap);
        }

        /// <summary>0/6/12/20/28 by band when Rated, 3 Unrateable, 0 Pending. A Rated band outside
        /// 0..4 (never produced by the classifier) is worth 0.</summary>
        public static double BandValueOf(BandState state, byte band)
        {
            switch (state)
            {
                case BandState.Rated:
                    switch (band)
                    {
                        case BandGreen: return 6.0;
                        case BandBlue: return 12.0;
                        case BandBlack: return 20.0;
                        case BandDoubleBlack: return 28.0;
                        default: return 0.0;   // BandFlat, out of range
                    }
                case BandState.Unrateable:
                    return UnrateableValue;
                default:
                    return 0.0;                // Pending
            }
        }

        /// <summary>Night 1.25, natural by band (0.5 / 0.75 / 1.0; unrateable or pending 0.5), else 1
        /// (groomed, snowmaking and any unknown kind).</summary>
        public static double TypeFactorOf(byte kind, BandState state, byte band)
        {
            switch (kind)
            {
                case KindNight:
                    return NightFactor;
                case KindNatural:
                    if (state != BandState.Rated)
                        return NaturalUnrateableFactor;   // I3 (pending: worth 0 anyway through its band)
                    switch (band)
                    {
                        case BandBlue: return 0.75;
                        case BandBlack:
                        case BandDoubleBlack: return 1.0;
                        default: return 0.5;              // Flat, Green (and an out-of-range band)
                    }
                default:
                    return 1.0;
            }
        }

        /// <summary>Round S1 (ski-days ch1 §4.1, D21): skiers per hectare at once — Rated green / blue / black /
        /// double black 45 / 25 / 15 / 7.5; Unrateable 15 (ch1 §11 N-2); Flat, out of range and Pending 0 (worth 0
        /// in the score, so never a counted run).</summary>
        public static double SkiersPerHa(BandState state, byte band)
        {
            switch (state)
            {
                case BandState.Rated:
                    switch (band)
                    {
                        case BandGreen: return SkiersPerHaGreen;
                        case BandBlue: return SkiersPerHaBlue;
                        case BandBlack: return SkiersPerHaBlack;
                        case BandDoubleBlack: return SkiersPerHaDoubleBlack;
                        default: return 0.0;   // BandFlat, out of range
                    }
                case BandState.Unrateable:
                    return SkiersPerHaUnrateable;
                default:
                    return 0.0;                // Pending
            }
        }

        /// <summary>Round S1 (ski-days ch1 §4.1, D21): one run's share of RunsCapacity — 0 unless the part is
        /// counted and the area &gt; 0; area capped at MaxCapacityAreaM2 (+∞ too); then, left to right, ha ×
        /// SkiersPerHa × (natural 0.5, else 1) × the part's served and open factors (an open run weighted by its
        /// open factor, as the score is: ch1 §11 N-5). Night runs hold no extra skiers (no type factor).</summary>
        public static double RunSkiersPerDay(in RunInput run, in RunPart part)
        {
            if (!part.Counted || !(run.AreaM2 > 0.0))
                return 0.0;
            double area = run.AreaM2 < MaxCapacityAreaM2 ? run.AreaM2 : MaxCapacityAreaM2;
            return area / 10000.0 * SkiersPerHa(run.State, run.Band) * (run.Kind == KindNatural ? NaturalSkiersFactor : 1.0) *
                   part.ServedFactor * part.OpenFactor;
        }

        /// <summary>n A-frame rows: 0, 20, 24, 28, 32, 36, 40, 44, 44 … by active lodge count (D4; Catalog Phase B R11:
        /// the cap is 24, so 5 → 36 and 7+ → 44; was 32 from 4 on); negative → 0. Double arithmetic, so no int overflow
        /// for any count. = the winter LodgeBase of the ladder over n A-frames (LadderTests).</summary>
        public static double LodgeBaseOf(int activeLodges)
        {
            if (activeLodges < 1)
                return 0.0;
            return FirstLodgeBase + Math.Min(LodgeExtra * (activeLodges - 1.0), LodgeExtraCap);
        }

        // ================================================================== the lodge ladder (Catalog Phase B, R11)

        /// <summary>The ladder's outputs (header CATALOG PHASE B).</summary>
        private struct LadderOut
        {
            public double Base, Applied, ExtraApplied;
            public int BestIndex, PriceTolerance;
        }

        /// <summary>The ladder (round-2-contract §7.1, skeptic MUST 3), pure: returns the WINTER LodgeBase (B(best) +
        /// min(Σ E(others), 24), μ = 1 for every lodge); <paramref name="applied"/> / <paramref name="bestIndex"/> /
        /// <paramref name="extraApplied"/> are the SEASON's selection (summer: μ, or μ summit when summit-served). Every
        /// lodge in the list counts (no per-tier or per-area count limit; the extras cap is the only limiter). Null or empty
        /// list → 0, applied 0, best −1. Order-free: every permutation of <paramref name="lodges"/> gives the same numbers
        /// bit for bit.</summary>
        public static double LodgeBase(List<LodgeInput> lodges, bool summer, out double applied, out int bestIndex,
                                       out double extraApplied)
        {
            LadderOut o = LodgeLadder(lodges, summer);
            applied = o.Applied;
            bestIndex = o.BestIndex;
            extraApplied = o.ExtraApplied;
            return o.Base;
        }

        /// <summary>The catalog row of a lodge input; an out-of-range index reads the A-frame row 0 (callers fill it from
        /// SkiCatalog.LodgeByPrefab, so it never is). Null only when the catalog has no lodge row (its Problems say why).</summary>
        private static LodgeDef RowOf(int lodgeIndex)
        {
            int count = SkiCatalog.Lodges.Count;
            if (count == 0)
                return null;
            return lodgeIndex >= 0 && lodgeIndex < count ? SkiCatalog.Lodges[lodgeIndex] : SkiCatalog.Lodges[0];
        }

        /// <summary>μ of one lodge in the season: winter 1; summer μ, or μ summit when summit-served and higher.</summary>
        private static double MuOf(LodgeDef d, bool summerMu, bool summitServed)
        {
            if (!summerMu)
                return 1.0;
            return summitServed && d.MuSummit > d.Mu ? d.MuSummit : d.Mu;
        }

        /// <summary>"a is better than b" (MUST 3): higher B·μ (s_LadderV), then higher E, then higher Capacity, then lower
        /// LodgeIndex. False for two lodges that tie on every key.</summary>
        private static bool Better(int a, int b, LodgeDef da, LodgeDef db)
        {
            double va = s_LadderV[a], vb = s_LadderV[b];
            if (va != vb)
                return va > vb;
            if (da.E != db.E)
                return da.E > db.E;
            if (da.Capacity != db.Capacity)
                return da.Capacity > db.Capacity;
            return da.Index < db.Index;
        }

        /// <summary>One selection (winter μ or summer μ): fills s_LadderV per lodge; returns the best lodge's list position
        /// (−1 for an empty list). No lodge is ignored: every tier counts, in any number.</summary>
        private static int Select(List<LodgeInput> lodges, bool summerMu)
        {
            s_LadderV.Clear();
            int n = lodges.Count;
            for (int i = 0; i < n; i++)
            {
                LodgeDef d = RowOf(lodges[i].LodgeIndex);
                s_LadderV.Add(d.B * MuOf(d, summerMu, lodges[i].SummitServed));
            }
            // Best: the better of every pair (ties on every key keep the earlier position).
            int best = -1;
            LodgeDef bestDef = null;
            for (int i = 0; i < n; i++)
            {
                LodgeDef d = RowOf(lodges[i].LodgeIndex);
                if (best < 0 || Better(i, best, d, bestDef))
                {
                    best = i;
                    bestDef = d;
                }
            }
            return best;
        }

        /// <summary>The ladder over <paramref name="lodges"/> (header CATALOG PHASE B). Allocation-free once the
        /// scratch lists have grown; never throws for any list.</summary>
        private static LadderOut LodgeLadder(List<LodgeInput> lodges, bool summer)
        {
            var o = new LadderOut { BestIndex = -1 };
            int n = lodges?.Count ?? 0;
            if (n == 0 || SkiCatalog.Lodges.Count == 0)
                return o;
            double cap = LodgeExtraCap;

            // The WINTER selection: LodgeBase (the displayed base, the hub's receipt and AttractionWithLodges).
            int wb = Select(lodges, false);
            s_LadderSumA.Clear();
            for (int i = 0; i < n; i++)
            {
                if (i != wb)
                    s_LadderSumA.Add(RowOf(lodges[i].LodgeIndex).E);
            }
            double wSumE = AscendingSum(s_LadderSumA);
            double wExtra = Math.Min(wSumE, cap);
            o.Base = wb >= 0 ? RowOf(lodges[wb].LodgeIndex).B + wExtra : 0.0;
            if (!summer)
            {
                o.Applied = o.Base;
                o.ExtraApplied = wb >= 0 ? wExtra : 0.0;
                o.BestIndex = wb >= 0 ? RowOf(lodges[wb].LodgeIndex).Index : -1;
                o.PriceTolerance = Tolerance(lodges);
                return o;
            }

            // The SUMMER selection: μ (or μ summit when summit-served) on the best and on every extra; the cap scales the
            // extras' sum in proportion (sum first, then scale: MUST 3 b).
            int sb = Select(lodges, true);
            s_LadderSumA.Clear();
            s_LadderSumB.Clear();
            for (int i = 0; i < n; i++)
            {
                if (i == sb)
                    continue;
                LodgeDef d = RowOf(lodges[i].LodgeIndex);
                s_LadderSumA.Add(d.E);
                s_LadderSumB.Add(d.E * MuOf(d, true, lodges[i].SummitServed));
            }
            double sumE = AscendingSum(s_LadderSumA);
            double sumEMu = AscendingSum(s_LadderSumB);
            double extras = sumE > cap ? sumEMu * cap / sumE : sumEMu;
            o.Applied = sb >= 0 ? s_LadderV[sb] + extras : 0.0;
            o.ExtraApplied = sb >= 0 ? extras : 0.0;
            o.BestIndex = sb >= 0 ? RowOf(lodges[sb].LodgeIndex).Index : -1;
            o.PriceTolerance = Tolerance(lodges);
            return o;
        }

        /// <summary>Max PriceTolerance over the active lodges (0 without).</summary>
        private static int Tolerance(List<LodgeInput> lodges)
        {
            int tolerance = 0;
            for (int i = 0; i < lodges.Count; i++)
            {
                int t = RowOf(lodges[i].LodgeIndex).PriceTolerance;
                if (t > tolerance)
                    tolerance = t;
            }
            return tolerance;
        }

        /// <summary>(int)Math.Round(x, MidpointRounding.ToEven) — vanilla's Mathf.RoundToInt rounding.
        /// NaN → 0 and values beyond the int range saturate (never an unspecified cast); Compute only
        /// passes clamped values.</summary>
        public static int RoundToEven(double x)
        {
            if (double.IsNaN(x))
                return 0;
            double rounded = Math.Round(x, MidpointRounding.ToEven);
            if (rounded >= int.MaxValue)
                return int.MaxValue;
            if (rounded <= int.MinValue)
                return int.MinValue;
            return (int)rounded;
        }

        // (e1-spec R1: IsLiftPowered is deleted. The D6 power rule is LiftServedRule.IsPowered(LiftPower),
        // ch2 §3.2-3.3; the caller fills LiftInput.Powered with it.)

        /// <summary>BuildingUtils.GetEfficiency(DynamicBuffer&lt;Efficiency&gt;) re-done in float, bit for
        /// bit (Game.Buildings/BuildingUtils.cs:121-133): product of max(0, f); 0 when the product is
        /// not &gt; 0; else max(0.01f, (float)Math.Round((double)(100f × p)) / 100f). count = 0 → 1f.
        /// One formula for the score and the hub (the stats dump computes it inline today).</summary>
        public static float SignEfficiency(float[] factors, int count)
        {
            // No buffer (or an empty one) → 1, vanilla's GetEfficiency(BufferAccessor, i) rule
            // (BuildingUtils.cs:94-101); an empty buffer gives 1 through the formula too.
            if (factors == null || count <= 0)
                return 1f;
            if (count > factors.Length)
                count = factors.Length;
            float product = 1f;
            for (int i = 0; i < count; i++)
            {
                float f = factors[i];
                product *= f > 0f ? f : 0f;   // math.max(0f, f); NaN → 0 as Unity's max(0, NaN)
            }
            if (!(product > 0f))
                return 0f;
            float scaled = 100f * product;                       // float, as vanilla
            float rounded = (float)Math.Round((double)scaled);   // math.round(float) = (float)Math.Round(x), to even
            float efficiency = rounded / 100f;
            return efficiency > 0.01f ? efficiency : 0.01f;      // math.max(0.01f, …)
        }
    }
}
