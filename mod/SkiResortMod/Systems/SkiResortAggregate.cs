// The pure per-resort sums of the hub's resort rows. SkiResortStatsSystem collects one small input row per run, lift and lodge
// (ECS reads stay in the system), calls Aggregate, and adds the fields that need the game. Tested by tests/ResortBatch.
// Resort index: inputs carry Resort = 0..resortCount-1 (the signs in the stable SkiResortIndex order) or -1 = Not linked; the
// output array has resortCount + 1 entries, the last one being the Not linked row (always last; always sent).
// Pure file: System (+ System.Collections.Generic) and Unity.Mathematics only. No allocation: the caller owns every list and the
// output array (grown once to the city's sign count + 1).
// Engine notes: EngineNotes/SkiResortAggregate.md

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public static class SkiResortAggregate
    {
        public struct RunIn
        {
            public int Resort;          // −1 = Not linked
            public byte RateState;      // 0 pending, 1 rated, 2-4 cannot be rated (SkiRunCatalogSystem.RunRow.RateState)
            public sbyte Band;          // −1 not rated, else 0..4
            public float LengthM, AreaM2, TopM, BottomM;
            public float CostMonth;     // >= 0
            public float SnowHa;        // >= 0
            public float Cover;  // 0..1 snow cover with the run's own snowmaking; NaN = no cover (unbound; the stats always pass NaN)
            public float NaturalCover;  // 0..1 snow cover without snowmaking; NaN = no cover
        }

        public struct LiftIn
        {
            public int Resort;          // −1 = Not linked
            public EntityRef Id;
            public byte State;          // LiftRow.state: 0 Running, 1 Static, 2 Open, 3 Unfinished, 4 StationsOnly
            public int Chairs;
            public float LengthM, RiseM;
            public float BaseTipY, TopTipY;   // NaN when that station does not exist
        }

        public struct LodgeIn
        {
            public int Resort;          // −1 = Not linked
            public int Score;
            public bool Free;           // free-standing (no live-sign owner)
        }

        public struct Agg
        {
            public bool HasAny;
            public int Runs, Rated, Flat, Green, Blue, Black, DoubleBlack;
            public int Unrateable;  // RateState 2/3/4 (C# only; the UI counts band -2 rows)
            public float RunLengthM, AreaM2, VerticalM;
            public bool VerticalApprox;
            public int Lifts, LiftsRunning, LiftsOpen, Chairs;
            public float LiftLengthM, LiftRiseM;
            public int Lodges, FreeLodges, LodgeScore;
            public float RunCostMonth, SnowHa, SnowPct;
            public int Season;
            public float RunVerticalM;  // runs only; -1 when no run has a height

            // Accumulators (valid only inside Aggregate).
            internal float Hi, Lo;
            internal float RunHi, RunLo;
            internal double CoverArea, CoverWeighted;
            internal int CoverRuns;
            internal bool AnyNatural, AnySnow;
        }

        public const int SeasonNone = -1, SeasonNatural = 0, SeasonSnowmakingOnly = 1, SeasonTooWarm = 2;

        /// <summary>Fills result[0..resortCount] (the last entry = Not linked) from the inputs.
        /// Inputs whose Resort is out of range count as Not linked. result must have at least
        /// resortCount + 1 entries.</summary>
        public static void Aggregate(List<RunIn> runs, List<LiftIn> lifts, List<LodgeIn> lodges, int resortCount, Agg[] result)
        {
            if (resortCount < 0)
                resortCount = 0;
            int rows = resortCount + 1;
            for (int i = 0; i < rows; i++)
            {
                result[i] = default(Agg);
                result[i].Hi = float.NegativeInfinity;
                result[i].Lo = float.PositiveInfinity;
                result[i].RunHi = float.NegativeInfinity;
                result[i].RunLo = float.PositiveInfinity;
                result[i].LodgeScore = -1;
            }

            if (runs != null)
            {
                for (int k = 0; k < runs.Count; k++)
                {
                    RunIn r = runs[k];
                    ref Agg a = ref result[Row(r.Resort, resortCount)];
                    a.HasAny = true;
                    a.Runs++;
                    if (r.RateState == 1)
                    {
                        a.Rated++;
                        switch (r.Band)
                        {
                            case 0: a.Flat++; break;
                            case 1: a.Green++; break;
                            case 2: a.Blue++; break;
                            case 3: a.Black++; break;
                            case 4: a.DoubleBlack++; break;
                        }
                    }
                    else if (r.RateState == 0)
                    {
                        a.VerticalApprox = true;   // pending: node heights until rated
                    }
                    else
                    {
                        a.Unrateable++;            // final: no terrain / too large / failed
                    }
                    a.RunLengthM += Finite(r.LengthM);
                    a.AreaM2 += Finite(r.AreaM2);
                    a.RunCostMonth += Finite(r.CostMonth);
                    a.SnowHa += Finite(r.SnowHa);
                    Extend(ref a, r.TopM, r.BottomM);
                    ExtendRun(ref a, r.TopM, r.BottomM);
                    if (IsFinite(r.Cover))
                    {
                        double area = Finite(r.AreaM2);
                        a.CoverRuns++;
                        a.CoverArea += area;
                        a.CoverWeighted += area * math.saturate(r.Cover);
                        if (r.Cover > 0f)
                            a.AnySnow = true;
                        if (IsFinite(r.NaturalCover) && r.NaturalCover > 0f)
                            a.AnyNatural = true;
                    }
                }
            }

            if (lifts != null)
            {
                for (int k = 0; k < lifts.Count; k++)
                {
                    LiftIn l = lifts[k];
                    ref Agg a = ref result[Row(l.Resort, resortCount)];
                    a.HasAny = true;
                    a.Lifts++;
                    if (l.State == 0)
                        a.LiftsRunning++;
                    else if (l.State == 2)
                        a.LiftsOpen++;
                    a.Chairs += l.Chairs > 0 ? l.Chairs : 0;
                    a.LiftLengthM += Finite(l.LengthM);
                    a.LiftRiseM += Finite(l.RiseM);
                    // Stations: the top tip raises the top, the base tip lowers the bottom.
                    if (IsFinite(l.TopTipY) && l.TopTipY > a.Hi)
                        a.Hi = l.TopTipY;
                    if (IsFinite(l.BaseTipY) && l.BaseTipY < a.Lo)
                        a.Lo = l.BaseTipY;
                }
            }

            if (lodges != null)
            {
                for (int k = 0; k < lodges.Count; k++)
                {
                    LodgeIn d = lodges[k];
                    ref Agg a = ref result[Row(d.Resort, resortCount)];
                    a.HasAny = true;
                    a.Lodges++;
                    if (d.Free)
                        a.FreeLodges++;
                    a.LodgeScore = (a.LodgeScore < 0 ? 0 : a.LodgeScore) + (d.Score > 0 ? d.Score : 0);
                }
            }

            for (int i = 0; i < rows; i++)
            {
                ref Agg a = ref result[i];
                a.VerticalM = IsFinite(a.Hi) && IsFinite(a.Lo) && a.Hi >= a.Lo ? a.Hi - a.Lo : -1f;
                if (a.VerticalM < 0f)
                    a.VerticalApprox = false;
                a.RunVerticalM = IsFinite(a.RunHi) && IsFinite(a.RunLo) && a.RunHi >= a.RunLo ? a.RunHi - a.RunLo : -1f;
                if (a.CoverRuns == 0)
                {
                    a.SnowPct = -1f;
                    a.Season = SeasonNone;
                }
                else
                {
                    a.SnowPct = a.CoverArea > 0.0 ? (float)(100.0 * a.CoverWeighted / a.CoverArea) : 0f;
                    a.Season = a.AnyNatural ? SeasonNatural : a.AnySnow ? SeasonSnowmakingOnly : SeasonTooWarm;
                }
            }
        }

        /// <summary>Per-resort lift ordinals: resort by resort, top-station tip height
        /// descending, lifts without a top station last, ties by Id (Index, Version). ordinals[k]
        /// is lift k's N (1-based). <paramref name="order"/> is caller-owned scratch.</summary>
        public static void LiftOrdinals(List<LiftIn> lifts, List<int> order, List<int> ordinals)
        {
            order.Clear();
            ordinals.Clear();
            for (int k = 0; k < lifts.Count; k++)
            {
                order.Add(k);
                ordinals.Add(0);
            }
            s_LiftOrder.Lifts = lifts;   // main thread only (the stats system's rebuild)
            order.Sort(s_LiftOrder);
            s_LiftOrder.Lifts = null;
            int resort = int.MinValue, n = 0;
            for (int p = 0; p < order.Count; p++)
            {
                LiftIn l = lifts[order[p]];
                if (l.Resort != resort)
                {
                    resort = l.Resort;
                    n = 0;
                }
                ordinals[order[p]] = ++n;
            }
        }

        /// <summary>The smallest N in 1..maxN whose formatted name is not in
        /// <paramref name="taken"/>, or −1 when every one is taken (or format is null).</summary>
        public static int SmallestFreeOrdinal(HashSet<string> taken, Func<int, string> format, int maxN)
        {
            if (format == null)
                return -1;
            for (int n = 1; n <= maxN; n++)
            {
                string candidate = format(n);
                if (candidate != null && (taken == null || !taken.Contains(candidate)))
                    return n;
            }
            return -1;
        }

        /// <summary><paramref name="customNames"/>[i] is sign i's custom name in
        /// stable sign order (null or empty = unnamed). ordinals[i] = the N of its display-only
        /// fallback for an unnamed sign (−1 when none is free up to maxN), 0 for a named one.
        /// <paramref name="taken"/> is caller-owned scratch (cleared here).</summary>
        public static void FallbackOrdinals(List<string> customNames, Func<int, string> format, HashSet<string> taken,
            List<int> ordinals, int maxN)
        {
            taken.Clear();
            ordinals.Clear();
            for (int i = 0; i < customNames.Count; i++)
            {
                if (!string.IsNullOrEmpty(customNames[i]))
                    taken.Add(customNames[i]);
            }
            for (int i = 0; i < customNames.Count; i++)
            {
                if (!string.IsNullOrEmpty(customNames[i]))
                {
                    ordinals.Add(0);
                    continue;
                }
                int n = SmallestFreeOrdinal(taken, format, maxN);
                ordinals.Add(n);
                if (n > 0)
                    taken.Add(format(n));
            }
        }

        /// <summary>Index comparer over the caller's lift list (one cached instance: no allocation
        /// per call; not re-entrant — LiftOrdinals is called from the main thread only).</summary>
        private sealed class LiftOrder : IComparer<int>
        {
            public List<LiftIn> Lifts;

            public int Compare(int x, int y)
            {
                return CompareLift(Lifts[x], Lifts[y]);
            }
        }

        private static readonly LiftOrder s_LiftOrder = new LiftOrder();

        private static int CompareLift(LiftIn a, LiftIn b)
        {
            if (a.Resort != b.Resort)
                return a.Resort < b.Resort ? -1 : 1;
            bool ha = IsFinite(a.TopTipY), hb = IsFinite(b.TopTipY);
            if (ha != hb)
                return ha ? -1 : 1;
            if (ha && a.TopTipY != b.TopTipY)
                return a.TopTipY > b.TopTipY ? -1 : 1;
            return a.Id.CompareTo(b.Id);
        }

        private static int Row(int resort, int resortCount)
        {
            return resort >= 0 && resort < resortCount ? resort : resortCount;
        }

        private static void Extend(ref Agg a, float top, float bottom)
        {
            if (IsFinite(top))
            {
                if (top > a.Hi) a.Hi = top;
                if (top < a.Lo) a.Lo = top;
            }
            if (IsFinite(bottom))
            {
                if (bottom > a.Hi) a.Hi = bottom;
                if (bottom < a.Lo) a.Lo = bottom;
            }
        }

        /// <summary>Extend's rule for the runs-only accumulators: each finite top and
        /// bottom widens both ends.</summary>
        private static void ExtendRun(ref Agg a, float top, float bottom)
        {
            if (IsFinite(top))
            {
                if (top > a.RunHi) a.RunHi = top;
                if (top < a.RunLo) a.RunLo = top;
            }
            if (IsFinite(bottom))
            {
                if (bottom > a.RunHi) a.RunHi = bottom;
                if (bottom < a.RunLo) a.RunLo = bottom;
            }
        }

        private static bool IsFinite(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }

        private static float Finite(float v)
        {
            return IsFinite(v) ? v : 0f;
        }
    }
}
