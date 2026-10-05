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
// PURE FILE: `using System;` and `using System.Collections.Generic;` only (no Unity.Entities, no Game
// types, no Unity.Mathematics). net48 traps (the mod build is the API gate): no Math.Clamp, MathF,
// double.IsFinite, Span<T> — the private Clamp / IsFinite helpers below stand in. Arithmetic is double
// (SignEfficiency float, as vanilla's). Compute uses one static scratch list: main thread and the test
// host only (not re-entrant, not thread-safe).

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
        public const double LiftValue = 6.0;
        public const double VarietyFactor = 1.15;
        public const double VerticalRefM = 600.0;
        public const double FirstLodgeBase = 20.0;
        public const double LodgeExtra = 4.0;
        public const double LodgeExtraCap = 12.0;
        public const double SummerLodgeShare = 0.5;    // μ
        public const double SummerLiftShare = 0.25;    // λ
        public const int MaxAttraction = 200;
        public const int MinK = 1, MaxK = 10;
        // (e1-spec R1: no power constant here — the one power rule is LiftServedRule.IsPowered.)

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
        }

        public struct LiftInput
        {
            public bool Complete;        // census State.Complete
            public bool Powered;         // LiftServedRule.IsPowered(lift.Power) from CopyServeLifts (e1-spec R1)
        }

        public struct ScoreSettings      // not "Settings": the namespace SkiResortMod.Settings is in scope in Systems/
        {
            public int K;                // AttractivenessScaleK; clamped to MinK..MaxK
            public int BackcountryPercent; // clamped to 0..100
            public bool SnowGating;      // EnableSnowGating
        }

        public struct AreaInput
        {
            public int ActiveLodges;     // N: lodges in the sign's InstalledUpgrade buffer, not Inactive
            public int InactiveLodges;   // echoed for the hub
            public double Efficiency;    // SignEfficiency(...) of the sign (§8)
            public ScoreSettings Settings;
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

            /// <summary>Exact field compare (Generation, §12): bit-exact in any run order because
            /// RunsValue is summed in ascending order. Every field, doubles bit for bit (Same).</summary>
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
                       LiftsUnpowered == o.LiftsUnpowered && Flags == o.Flags;
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

        /// <summary>Compute's scratch for the ascending sum of counted values (second pass). Main
        /// thread and the test host only: Compute is not re-entrant and not thread-safe.</summary>
        private static readonly List<double> s_CountedValues = new List<double>(64);

        /// <summary>The model (§2.1). <paramref name="parts"/> is cleared, then gets one RunPart per
        /// run, in the order of <paramref name="runs"/>. Never throws for any finite or non-finite
        /// input; never allocates beyond growing <paramref name="parts"/> and the static scratch list.
        /// The Result is bit-identical for any permutation of <paramref name="runs"/> (and
        /// <paramref name="lifts"/>).</summary>
        public static Result Compute(in AreaInput area, List<RunInput> runs, List<LiftInput> lifts,
                                     List<RunPart> parts)
        {
            // Step 1: scratch, clamped settings.
            parts?.Clear();
            s_CountedValues.Clear();
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

                var part = new RunPart
                {
                    BandValue = BandValueOf(run.State, run.Band),
                    SizeFactor = SizeFactor(run.AreaM2),
                    TypeFactor = TypeFactorOf(run.Kind, run.State, run.Band),
                    ServedFactor = run.Served ? 1.0 : bc,
                    OpenFactor = open,
                };
                // Left to right, as Appendix A: ((((band × size) × type) × served) × open).
                part.Value = part.BandValue * part.SizeFactor * part.TypeFactor * part.ServedFactor * part.OpenFactor;
                part.Counted = part.Value > 0.0;
                parts?.Add(part);

                if (!part.Counted)
                    continue;
                r.RunsCounted++;
                s_CountedValues.Add(part.Value);
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

            // Step 2b: the order-independent sum (ascending; every value is finite and > 0).
            s_CountedValues.Sort();
            double sum = 0.0;
            for (int i = 0; i < s_CountedValues.Count; i++)
                sum += s_CountedValues[i];
            r.RunsValue = sum;

            // Step 3: summer = no counted run (I2, second pass).
            bool summer = r.RunsCounted == 0;
            r.LodgeShare = summer ? SummerLodgeShare : 1.0;
            r.LiftShare = summer ? SummerLiftShare : 1.0;

            // Step 4: lifts (the caller passes only lifts whose two stations exist, ch1 §6).
            int liftCount = lifts?.Count ?? 0;
            for (int i = 0; i < liftCount; i++)
            {
                LiftInput lift = lifts[i];
                r.Lifts++;
                if (lift.Complete)
                {
                    r.LiftsComplete++;
                    if (lift.Powered)
                        r.LiftsCounted++;
                    else
                        r.LiftsUnpowered++;
                }
                else
                {
                    r.LiftsOpenSection++;
                }
            }

            // Step 5: lift value, variety, vertical (0 when no counted run had finite heights).
            r.LiftsValue = LiftValue * r.LiftsCounted;
            r.Variety = green && blue && black;
            r.VarietyMultiplier = r.Variety ? VarietyFactor : 1.0;
            r.VerticalM = heights ? Math.Max(0.0, top - bottom) : 0.0;
            r.VerticalMultiplier = 1.0 + Clamp(r.VerticalM / VerticalRefM, 0.0, 1.0);

            // Step 6: S, lodge base, A_open, efficiency (NaN and ≤ 0 → 0).
            r.S = (r.RunsValue + r.LiftShare * r.LiftsValue) * r.VarietyMultiplier * r.VerticalMultiplier;
            int n = area.ActiveLodges > 0 ? area.ActiveLodges : 0;
            r.ActiveLodges = n;
            r.InactiveLodges = area.InactiveLodges > 0 ? area.InactiveLodges : 0;
            r.LodgeBase = LodgeBaseOf(n);
            r.AOpen = r.LodgeBase * r.LodgeShare + r.K * Math.Sqrt(r.S);
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

        /// <summary>0, 20, 24, 28, 32, 32 … by active lodge count (D4); negative → 0. Double
        /// arithmetic, so no int overflow for any count.</summary>
        public static double LodgeBaseOf(int activeLodges)
        {
            if (activeLodges < 1)
                return 0.0;
            return FirstLodgeBase + Math.Min(LodgeExtra * (activeLodges - 1.0), LodgeExtraCap);
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
