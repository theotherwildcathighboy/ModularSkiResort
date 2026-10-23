// Attraction model: ONE attraction score per ski area (one Welcome Sign), from its member runs (band, size with the 1 ha knee,
// type, lift service, open factor), its complete and powered lifts, its active lodges and the sign's efficiency. Consumers:
// SkiResortAttractivenessSystem (Compute on every recompute), SkiResortStatsSystem / HubRules (the hub reads this Result, never
// recomputes), tests/ResortBatch ScoreTests.
// Pure file: System and System.Collections.Generic only, plus SkiCatalog, SchoolRule and PatrolRule (linked wherever this file is).
// Main thread and the test host only: static managed rows and static scratch lists (not re-entrant, not thread-safe). Arithmetic
// is double (SignEfficiency float, as vanilla's). net48 traps: no Math.Clamp, MathF, double.IsFinite, Span<T>.
// Engine notes: EngineNotes/SkiResortScore.md

using System;
using System.Collections.Generic;

namespace SkiResortMod.Systems
{
    /// <summary>Attraction model: one score per ski area. Pure.</summary>
    public static class SkiResortScore
    {
        public const double RefAreaM2 = 20000.0;
        public const double SizeLogSlope = 0.3;
        public const double SizeFloor = 0.25;
        public const double SizeCap = 2.0;
        public const double SizeKneeM2 = 10000.0; // proportional below 1 ha (1641.699972, the old floor kept)
        public const double UnrateableValue = 3.0;
        public const double NightFactor = 1.25;
        public const double NaturalUnrateableFactor = 0.5;
        public const double LiftValue = SkiCatalog.FixedGrip.ScoreValue; // const alias of SkiCatalog.FixedGrip.ScoreValue; ScoreTests guards 6
        public const double VarietyFactor = 1.15;
        public const double VerticalRefM = 600.0;
        public const double FirstLodgeBase = 20.0; // = SkiCatalog.Lodges[0].B (the A-frame; LadderTests guard)
        public const double LodgeExtra = 4.0; // = SkiCatalog.Lodges[0].E
        // const alias; ScoreTests guards 24
        public const double LodgeExtraCap = SkiCatalog.Ladder.ExtraCap;
        public const double SummerLodgeShare = 0.5; // mu of the A-frame (= SkiCatalog.Lodges[0].Mu); the no-lodge summer share
        public const double SummerLiftShare = 0.25; // lambda of the fixed-grip (= SkiCatalog.FixedGrip.SummerShare); the no-lift summer share
        public const int MaxAttraction = 200;
        public const int MinK = 1, MaxK = 10;

        // Ski-day capacity, skiers per hectare by band. Unrateable counts as black (conservative).
        public const double SkiersPerHaGreen = 45.0, SkiersPerHaBlue = 25.0, SkiersPerHaBlack = 15.0,
                            SkiersPerHaDoubleBlack = 7.5, SkiersPerHaUnrateable = 15.0;
        public const double NaturalSkiersFactor = 0.5;
        public const double MaxCapacityAreaM2 = 20000000.0; // 2,000 ha = SkiRunFees.MaxAreaHa

        // RunKind values (EntityRef.cs RunKind).
        public const byte KindGroomed = 0, KindSnowmaking = 1, KindNight = 2, KindNatural = 3;
        // SlopeBand values (SkiSlopeClassifier.SlopeBand).
        public const byte BandFlat = 0, BandGreen = 1, BandBlue = 2, BandBlack = 3, BandDoubleBlack = 4;

        public enum BandState : byte { Rated = 0, Unrateable = 1, Pending = 2 }

        [Flags]
        public enum ScoreFlags : ushort // not "Flags": avoids any clash with the [Flags] attribute and the field name
        {
            None = 0,
            NoLodge = 1, // N = 0: A = 0 (no lodge: guests have nowhere to arrive)
            SummerMode = 2, // no counted run (RunsCounted == 0): mu 0.5, lambda 0.25
            PendingRatings = 4, // >= 1 member run has no band yet (it counts 0)
            SnowGatingOff = 8, // the setting: every run counts fully open
            OpenEstimated = 16, // >= 1 open run's open factor comes from the temperature model
            EfficiencyZero = 32, // Efficiency <= 0 (or NaN): A = 0
            Clamped = 64, // A_open x Efficiency > 200 (N >= 1)
            NoRuns = 128, // the area has no member run
        }

        public struct RunInput
        {
            public byte Kind; // KindGroomed .. KindNatural; other values are treated as groomed
            public BandState State; // Rated: Band holds the SlopeBand
            public byte Band; // 0..4, read only when State == Rated
            public double AreaM2; // plan area (Geometry.m_SurfaceArea); <= 0 or NaN gives size 0 (the run counts 0)
            public bool Served; // the lift chapter's served rule over complete, powered lifts (LiftServedRule.Assign)
            public double OpenFactor; // 0..1 = GetRunSnow(...).Multiplier; ignored when SnowGating is off
            public bool OpenEstimated; // GetRunSnow(...).Source == RunSnowSource.Estimated
            public double TopM, BottomM; // heights for the vertical; non-finite values are skipped
            public bool Patrolled; // PatrolRule.Patrolled(run top xz, the area's active patrol huts)
        }

        public struct LiftInput
        {
            public bool Complete; // census State.Complete
            public bool Powered; // LiftServedRule.IsPowered(lift.Power) from CopyServeLifts
            public int TypeIndex; // SkiCatalog.Lifts index of the census row; 0 = fixedGrip (default)
        }

        /// <summary>One active lodge of the area. LodgeIndex = SkiCatalog.Lodges index (out of range reads the A-frame row 0);
        /// SummitServed = within Ladder.SummitServedRadiusM of a complete, powered lift's top stub tip of the same area
        /// (LiftServedRule.SummitServed; only rows with mu summit &gt; mu read it).</summary>
        public struct LodgeInput
        {
            public int LodgeIndex;
            public bool SummitServed;
        }

        public struct ScoreSettings // not "Settings": the namespace SkiResortMod.Settings is in scope in Systems/
        {
            public int K; // AttractivenessScaleK; clamped to MinK..MaxK
            public int BackcountryPercent; // clamped to 0..100
            public bool SnowGating;
        }

        public struct AreaInput
        {
            public int ActiveLodges; // N: the area's active lodges (read when Lodges == null)
            public int InactiveLodges; // echoed for the hub
            public double Efficiency; // SignEfficiency(...) of the sign
            public ScoreSettings Settings;
            // The active lodges by tier. null = ActiveLodges A-frame rows; non-null = N is Lodges.Count (the system passes both, equal).
            public List<LodgeInput> Lodges;
            public bool SchoolActive; // the area's ski school is installed and not Inactive
        }

        public struct RunPart : IEquatable<RunPart>
        {
            public double BandValue, SizeFactor, TypeFactor, ServedFactor, OpenFactor;
            public double Value;
            public bool Counted;

            /// <summary>Exact field compare (each part depends only on its own run). Doubles bit for bit (Same), so a NaN, which Compute
            /// never produces, would still compare equal to itself.</summary>
            public bool Equals(RunPart o)
            {
                return Same(BandValue, o.BandValue) && Same(SizeFactor, o.SizeFactor) &&
                       Same(TypeFactor, o.TypeFactor) && Same(ServedFactor, o.ServedFactor) &&
                       Same(OpenFactor, o.OpenFactor) && Same(Value, o.Value) && Counted == o.Counted;
            }
        }

        public struct Result : IEquatable<Result>
        {
            public int Attraction;
            public int AttractionFullEfficiency; // N >= 1 ? clamp(round(A_open), 0, 200) : 0 - "at full efficiency"
            public int PerLodge; // round(A / sqrt(N)) - the value each active lodge carries
            public int ActiveLodges, InactiveLodges;
            public double LodgeBase, LodgeShare; // LodgeBase(N), mu
            public double RunsValue;
            public int LiftsCounted; // complete AND powered
            public double LiftsValue, LiftShare;
            public bool Variety;
            public double VarietyMultiplier; // 1.15 or 1
            public double VerticalM, VerticalMultiplier;
            public double S, AOpen, Efficiency;
            public int K, BackcountryPercent; // the clamped settings used
            public int Runs, RunsOpen, RunsCounted, RunsPending, RunsUnrateable, RunsFlat,
                       RunsUnserved, RunsEstimated; // RunsUnserved: !Served over ALL member runs;
                                                     // RunsEstimated: open runs whose factor is estimated
            public int Lifts, LiftsComplete, LiftsOpenSection, LiftsUnpowered;
            public ScoreFlags Flags;
            // The ski-day inputs of the same recompute; in Equals.
            public double RunsValueFull; // sum BandValue x SizeFactor x TypeFactor x ServedFactor over ALL member runs (open ignored), terms > 0, ascending
            public double RunsCapacity; // sum RunSkiersPerDay over counted runs, terms > 0, ascending
            public int NightRunsCounted; // counted runs of KindNight
            public bool TopKnown; // >= 1 counted run with finite heights (= the existing `heights`)
            public double TopM; // highest counted-run top (the existing `top`); 0 when !TopKnown
            // Appended, all in Equals.
            public double LodgeApplied; // the season's lodge term of AOpen (winter = LodgeBase); LodgeBase = the winter value
            public double LodgeExtraApplied; // the season's extras after the cap (LodgeApplied - best B*mu)
            public double LiftsApplied; // sum per counted lift (summer ? lambda x value : value), ascending: the lift term of S
            public double SchoolValue; // SchoolRule.ScoreValueOf(SchoolActive, GreenCounted), added to S
            public int LodgeBestIndex; // SkiCatalog.Lodges index of the season's best lodge; -1 without a lodge
            public int PriceTolerance; // max PriceTolerance over the active lodges (0 without)
            public int RunsPatrolled; // member runs with RunInput.Patrolled (all member runs, counted or not)
            public bool SchoolActive;
            public bool GreenCounted; // a counted run Rated Green (the variety flag `green`)

            /// <summary>Exact field compare (for the Generation): bit-exact in any run order because RunsValue (and RunsValueFull and
            /// RunsCapacity) is summed in ascending order. Every field, doubles bit for bit (Same). The lift sums are ascending too and the
            /// ladder's selection is order-free, so the Result is bit-exact in any lift or lodge order.</summary>
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

        /// <summary>Sorts <paramref name="values"/> and sums them ascending: the same sorted sequence, so the same sum bit for bit, for
        /// any order the values were added in.</summary>
        private static double AscendingSum(List<double> values)
        {
            values.Sort();
            double sum = 0.0;
            for (int i = 0; i < values.Count; i++)
                sum += values[i];
            return sum;
        }

        /// <summary>Compute's scratch for the ascending sum of counted values. Main thread and the test host only: Compute is not
        /// re-entrant and not thread-safe.</summary>
        private static readonly List<double> s_CountedValues = new List<double>(64);

        /// <summary>The same scratch rule for the full values (every member run) and the counted runs' skiers, summed ascending into
        /// RunsValueFull / RunsCapacity.</summary>
        private static readonly List<double> s_FullValues = new List<double>(64);
        private static readonly List<double> s_CapacityValues = new List<double>(64);

        /// <summary>The counted lifts' winter values and season values (summed ascending, step 5) and the ladder's per-lodge scratch
        /// (LodgeLadder). Same rule as the lists above: main thread and the test host.</summary>
        private static readonly List<double> s_LiftValues = new List<double>(16);
        private static readonly List<double> s_LiftApplied = new List<double>(16);
        private static readonly List<double> s_LadderV = new List<double>(8); // per lodge: B*mu of the selection
        private static readonly List<double> s_LadderSumA = new List<double>(8); // sort scratch (sum E)
        private static readonly List<double> s_LadderSumB = new List<double>(8); // sort scratch (sum E*mu)

        /// <summary>The model. <paramref name="parts"/> is cleared, then gets one RunPart per run, in the order of
        /// <paramref name="runs"/>. Never throws for any finite or non-finite input; never allocates beyond growing
        /// <paramref name="parts"/> and the static scratch lists. The Result is bit-identical for any permutation of
        /// <paramref name="runs"/> (and <paramref name="lifts"/>).</summary>
        public static Result Compute(in AreaInput area, List<RunInput> runs, List<LiftInput> lifts,
                                     List<RunPart> parts)
        {
            // Step 1: scratch, clamped settings.
            parts?.Clear();
            s_CountedValues.Clear();
            s_FullValues.Clear();
            s_CapacityValues.Clear();
            s_LiftValues.Clear();
            s_LiftApplied.Clear();
            var r = new Result();
            int k = area.Settings.K;
            r.K = k < MinK ? MinK : (k > MaxK ? MaxK : k);
            int bcPct = area.Settings.BackcountryPercent;
            r.BackcountryPercent = bcPct < 0 ? 0 : (bcPct > 100 ? 100 : bcPct);
            double bc = r.BackcountryPercent / 100.0;
            bool gating = area.Settings.SnowGating;

            // Step 2: per run, in list order: counts, the five factors, the part; counted runs feed the value list, the height range and
            // the band flags.
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
                        open = 0.0; // NaN and <= 0
                    else if (open > 1.0)
                        open = 1.0;
                }
                else
                {
                    open = 1.0; // gating off: every run fully open
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
                    r.RunsPatrolled++;

                // The patrol rule wraps both; an unpatrolled run's factors are the plain ones bit for bit.
                var part = new RunPart
                {
                    BandValue = BandValueOf(run.State, run.Band),
                    SizeFactor = SizeFactor(run.AreaM2),
                    TypeFactor = PatrolRule.TypeFactorOf(TypeFactorOf(run.Kind, run.State, run.Band), run.Patrolled, run.State, run.Band),
                    ServedFactor = PatrolRule.ServedFactorOf(run.Served, run.Patrolled, bc),
                    OpenFactor = open,
                };
                // Left to right: ((((band x size) x type) x served) x open).
                part.Value = part.BandValue * part.SizeFactor * part.TypeFactor * part.ServedFactor * part.OpenFactor;
                part.Counted = part.Value > 0.0;
                parts?.Add(part);
                // The run's value with the open factor ignored (OpenShare's denominator), the same left-to-right product without its last
                // factor.
                double full = part.BandValue * part.SizeFactor * part.TypeFactor * part.ServedFactor;
                if (full > 0.0)
                    s_FullValues.Add(full);

                if (!part.Counted)
                    continue;
                r.RunsCounted++;
                s_CountedValues.Add(part.Value);
                double skiers = RunSkiersPerDay(run, part);
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
            r.RunsValueFull = AscendingSum(s_FullValues);
            r.RunsCapacity = AscendingSum(s_CapacityValues);

            // Step 3: summer = no counted run. The two shares are display values, set in steps 5 and 6 from the per-type lift sums and
            // the ladder.
            bool summer = r.RunsCounted == 0;

            // Step 4: lifts (the caller passes only lifts whose two stations exist). A counted lift's value and summer share are its type's
            // row (SkiCatalog.LiftByIndex(TypeIndex); an unknown index reads the fixed-grip row 0, as the census's NoType rows never reach
            // here: they are Unfinished).
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

            // Step 5: lift value, variety, vertical (0 when no counted run had finite heights). Both lift sums are ascending (bit-exact in
            // any lift order); for fixed-grip lifts sum 6 = 6n and sum 1.5 = 1.5n = 0.25 x 6n exactly. LiftShare = the applied share of
            // the value (display; the season's lambda without a counted lift).
            r.LiftsValue = AscendingSum(s_LiftValues);
            r.LiftsApplied = AscendingSum(s_LiftApplied);
            r.LiftShare = r.LiftsValue > 0.0 ? r.LiftsApplied / r.LiftsValue : (summer ? SummerLiftShare : 1.0);
            r.Variety = green && blue && black;
            r.VarietyMultiplier = r.Variety ? VarietyFactor : 1.0;
            r.VerticalM = heights ? Math.Max(0.0, top - bottom) : 0.0;
            r.VerticalMultiplier = 1.0 + Clamp(r.VerticalM / VerticalRefM, 0.0, 1.0);
            r.TopKnown = heights; // the area top for powder
            r.TopM = heights ? top : 0.0;

            // Step 6: S, lodge base, A_open, efficiency (NaN and <= 0 give 0). The school joins S before variety and vertical (+ 0.0
            // without one: bit for bit); the lodge term is the ladder's season value.
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
                // n A-frame rows (LodgeIndex 0, not served): the ladder's closed form: winter B + min(4(n-1), 24), summer exactly half (mu 0.5
                // on the best and on every extra; the cap scales the half sum to exactly 12).
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
            r.LodgeShare = r.LodgeBase > 0.0 ? r.LodgeApplied / r.LodgeBase : (summer ? SummerLodgeShare : 1.0); // the season share holds when there is no lodge
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
                flags |= ScoreFlags.NoLodge; // A, full and PerLodge stay 0
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

        /// <summary>size_i (knee at SizeKneeM2): <= 0 or NaN gives 0; below the knee proportional (SizeLog(knee) x a / knee); from the
        /// knee up SizeLog(a) = clamp(1 + 0.3 ln(a / 2 ha), 0.25, 2) (+inf gives SizeCap).</summary>
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

        /// <summary>0/6/12/20/28 by band when Rated, 3 Unrateable, 0 Pending. A Rated band outside 0..4 (never produced by the
        /// classifier) is worth 0.</summary>
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
                        default: return 0.0; // BandFlat, out of range
                    }
                case BandState.Unrateable:
                    return UnrateableValue;
                default:
                    return 0.0;
            }
        }

        /// <summary>Night 1.25, natural by band (0.5 / 0.75 / 1.0; unrateable or pending 0.5), else 1 (groomed, snowmaking and any
        /// unknown kind).</summary>
        public static double TypeFactorOf(byte kind, BandState state, byte band)
        {
            switch (kind)
            {
                case KindNight:
                    return NightFactor;
                case KindNatural:
                    if (state != BandState.Rated)
                        return NaturalUnrateableFactor; // pending: worth 0 anyway through its band
                    switch (band)
                    {
                        case BandBlue: return 0.75;
                        case BandBlack:
                        case BandDoubleBlack: return 1.0;
                        default: return 0.5; // Flat, Green (and an out-of-range band)
                    }
                default:
                    return 1.0;
            }
        }

        /// <summary>Skiers per hectare at once: Rated green / blue / black / double black 45 / 25 / 15 / 7.5; Unrateable 15; Flat, out
        /// of range and Pending 0 (worth 0 in the score, so never a counted run).</summary>
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
                        default: return 0.0; // BandFlat, out of range
                    }
                case BandState.Unrateable:
                    return SkiersPerHaUnrateable;
                default:
                    return 0.0;
            }
        }

        /// <summary>One run's share of RunsCapacity: 0 unless the part is counted and the area &gt; 0; area capped at MaxCapacityAreaM2
        /// (+inf too); then, left to right, ha x SkiersPerHa x (natural 0.5, else 1) x the part's served and open factors (an open run
        /// weighted by its open factor, as the score is). Night runs hold no extra skiers (no type factor).</summary>
        public static double RunSkiersPerDay(in RunInput run, in RunPart part)
        {
            if (!part.Counted || !(run.AreaM2 > 0.0))
                return 0.0;
            double area = run.AreaM2 < MaxCapacityAreaM2 ? run.AreaM2 : MaxCapacityAreaM2;
            return area / 10000.0 * SkiersPerHa(run.State, run.Band) * (run.Kind == KindNatural ? NaturalSkiersFactor : 1.0) *
                   part.ServedFactor * part.OpenFactor;
        }

        /// <summary>n A-frame rows: 0, 20, 24, 28, 32, 36, 40, 44, 44 ... by active lodge count (the cap is 24, so 5 gives 36 and 7+
        /// gives 44); negative gives 0. Double arithmetic, so no int overflow for any count. = the winter LodgeBase of the ladder over n
        /// A-frames (LadderTests).</summary>
        public static double LodgeBaseOf(int activeLodges)
        {
            if (activeLodges < 1)
                return 0.0;
            return FirstLodgeBase + Math.Min(LodgeExtra * (activeLodges - 1.0), LodgeExtraCap);
        }

        /// <summary>The ladder's outputs.</summary>
        private struct LadderOut
        {
            public double Base, Applied, ExtraApplied;
            public int BestIndex, PriceTolerance;
        }

        /// <summary>The ladder, pure: returns the winter LodgeBase (B(best) + min(sum E(others), 24), mu = 1 for every lodge);
        /// <paramref name="applied"/> / <paramref name="bestIndex"/> / <paramref name="extraApplied"/> are the season's selection
        /// (summer: mu, or mu summit when summit-served). Every lodge in the list counts (the extras cap is the only limiter). Null or
        /// empty list gives 0, applied 0, best -1. Order-free: every permutation of <paramref name="lodges"/> gives the same numbers bit
        /// for bit.</summary>
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

        /// <summary>mu of one lodge in the season: winter 1; summer mu, or mu summit when summit-served and higher.</summary>
        private static double MuOf(LodgeDef d, bool summerMu, bool summitServed)
        {
            if (!summerMu)
                return 1.0;
            return summitServed && d.MuSummit > d.Mu ? d.MuSummit : d.Mu;
        }

        /// <summary>"a is better than b": higher B*mu (s_LadderV), then higher E, then higher Capacity, then lower LodgeIndex. False for
        /// two lodges that tie on every key.</summary>
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

        /// <summary>One selection (winter mu or summer mu): fills s_LadderV per lodge; returns the best lodge's list position (-1 for an
        /// empty list). No lodge is ignored: every tier counts, in any number.</summary>
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

        /// <summary>The ladder over <paramref name="lodges"/>. Allocation-free once the scratch lists have grown; never throws for any
        /// list.</summary>
        private static LadderOut LodgeLadder(List<LodgeInput> lodges, bool summer)
        {
            var o = new LadderOut { BestIndex = -1 };
            int n = lodges?.Count ?? 0;
            if (n == 0 || SkiCatalog.Lodges.Count == 0)
                return o;
            double cap = LodgeExtraCap;

            // The winter selection: LodgeBase (the displayed base, the hub's receipt and AttractionWithLodges).
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

            // The summer selection: mu (or mu summit when summit-served) on the best and on every extra; the cap scales the extras' sum in
            // proportion (sum first, then scale).
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

        /// <summary>(int)Math.Round(x, MidpointRounding.ToEven), vanilla's Mathf.RoundToInt rounding. NaN gives 0 and values beyond the
        /// int range saturate (never an unspecified cast); Compute only passes clamped values.</summary>
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

        /// <summary>BuildingUtils.GetEfficiency(DynamicBuffer&lt;Efficiency&gt;) re-done in float, bit for bit
        /// (Game.Buildings/BuildingUtils.cs:121-133): product of max(0, f); 0 when the product is not &gt; 0; else max(0.01f,
        /// (float)Math.Round((double)(100f x p)) / 100f). count = 0 gives 1f. One formula for the score and the hub.</summary>
        public static float SignEfficiency(float[] factors, int count)
        {
            // No buffer (or an empty one) gives 1, vanilla's GetEfficiency(BufferAccessor, i) rule (BuildingUtils.cs:94-101); an empty
            // buffer gives 1 through the formula too.
            if (factors == null || count <= 0)
                return 1f;
            if (count > factors.Length)
                count = factors.Length;
            float product = 1f;
            for (int i = 0; i < count; i++)
            {
                float f = factors[i];
                product *= f > 0f ? f : 0f; // math.max(0f, f); NaN -> 0 as Unity's max(0, NaN)
            }
            if (!(product > 0f))
                return 0f;
            float scaled = 100f * product; // float, as vanilla
            float rounded = (float)Math.Round((double)scaled); // math.round(float) = (float)Math.Round(x), to even
            float efficiency = rounded / 100f;
            return efficiency > 0.01f ? efficiency : 0.01f; // math.max(0.01f, ...)
        }
    }
}
