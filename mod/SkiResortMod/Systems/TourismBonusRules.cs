// T226 resort city tourism bonus (DECISIONS 666): the pure rules. ResortTourismBonusSystem applies the result as ONE relative city
// Attractiveness modifier (the vanilla airport pattern); the hub shows it.
//
//   size  = BasePct + PerLiftLinePct x lift lines + PerTierPointPct x lodge tier points     (an area needs >= 1 running lift and >= 1 lodge)
//   bonus = size x open share                                                              (per ski area)
//   city  = min(cap, sum of the areas' bonuses)                                            (cap = the "Resort tourism bonus" option, 0 = off)
//
// Lift lines   = the area's complete, powered census rows (Result.LiftsCounted): one lift line, not its sections.
// Tier points  = Hut 1, Mid 2, Summit 3, Grand 3, a merged lodge 2 (DECISIONS 666), the A-frame 1 (smallest tier beside the hut; the
//                ruling names no A-frame value). Active lodges only: an off-season lodge the mod closed does not count.
// Open share   = what the score counts now over what it would count fully open: (RunsValue + LiftsApplied) / (RunsValueFull + LiftsValue).
//                In season with every run open it is 1. Closed runs lower it. In summer the runs count 0 and the lifts their summer share,
//                so a summer-open resort keeps a small part (Result fields of the same recompute, SkiResortScore step 4-6).
// Stacking     = vanilla Relative: y' = y (1 + d) + d (CityModifierUpdateSystem.cs:229, 1.6.0f1).
//
// TouristDayMean: the city's tourist count swings about 20 % over a day (tourism-lab 2.2), so the hub shows a rolling mean of the last
// game day, sampled every SampleFrames simulation frames.
// Pure file: System only (tests/ResortBatch links it).

using System;

namespace SkiResortMod.Systems
{
    public static class TourismBonusRules
    {
        public const double BasePct = 2.0;
        public const double PerLiftLinePct = 0.25;
        public const double PerTierPointPct = 0.25;

        // The option "Resort tourism bonus" (the city-wide cap), percent: 0-20, step 1, default 10, 0 = off (DECISIONS 666).
        public const int CapMinPct = 0;
        public const int CapMaxPct = 20;
        public const int CapDefaultPct = 10;

        public const int MergedTierPoints = 2;

        /// <summary>A logged line when the applied city bonus moved at least this many percentage points since the last line, or turned
        /// on / off (the open share moves it in small steps all day).</summary>
        public const double LogStepPct = 0.5;

        /// <summary>The option's value clamped to [CapMinPct, CapMaxPct].</summary>
        public static int ClampCapPct(int pct) => pct < CapMinPct ? CapMinPct : pct > CapMaxPct ? CapMaxPct : pct;

        /// <summary>Tier points of one active lodge. <paramref name="lodgeId"/> = LodgeDef.Id. A merged building counts MergedTierPoints
        /// whatever its tier. An unknown id counts 1.</summary>
        public static int TierPointsOf(string lodgeId, bool merged)
        {
            if (merged)
                return MergedTierPoints;
            switch (lodgeId)
            {
                case "aFrame":
                case "hut":
                    return 1;
                case "mid":
                    return 2;
                case "summit":
                case "grand":
                    return 3;
                default:
                    return 1;
            }
        }

        /// <summary>The area's size term in percent. 0 without a running lift line or without a lodge.</summary>
        public static double AreaSizePct(int liftLines, int tierPoints)
        {
            if (liftLines < 1 || tierPoints < 1)
                return 0.0;
            return BasePct + PerLiftLinePct * liftLines + PerTierPointPct * tierPoints;
        }

        /// <summary>The area's open share, 0..1: (runsValue + liftsApplied) / (runsValueFull + liftsValue). 0 when the full value is 0 or
        /// any input is not finite.</summary>
        public static double OpenShare(double runsValue, double liftsApplied, double runsValueFull, double liftsValue)
        {
            double full = runsValueFull + liftsValue;
            double now = runsValue + liftsApplied;
            if (!(full > 0.0) || double.IsInfinity(full) || double.IsNaN(now) || double.IsInfinity(now))
                return 0.0;
            double share = now / full;
            return share < 0.0 ? 0.0 : share > 1.0 ? 1.0 : share;
        }

        /// <summary>One area's bonus in percent: size x open share.</summary>
        public static double AreaBonusPct(double sizePct, double openShare)
        {
            if (!(sizePct > 0.0) || !(openShare > 0.0))
                return 0.0;
            return sizePct * Math.Min(1.0, openShare);
        }

        /// <summary>The city's bonus in percent: min(cap, sum of the areas). 0 when the cap is 0 (off).</summary>
        public static double CityBonusPct(double sumAreasPct, int capPct)
        {
            int cap = ClampCapPct(capPct);
            if (cap <= 0 || !(sumAreasPct > 0.0) || double.IsInfinity(sumAreasPct))
                return 0.0;
            return Math.Min(cap, sumAreasPct);
        }

        /// <summary>Vanilla's Relative stacking of one more modifier d (a fraction, 0.05 = 5 %) onto the y the buffer holds.</summary>
        public static float StackRelative(float vanillaY, float d) => vanillaY * (1f + d) + d;

        /// <summary>True when the applied bonus moved enough since the last logged value to be worth one log line.</summary>
        public static bool LogWorthy(double lastLoggedPct, double nowPct)
        {
            if ((lastLoggedPct > 0.0) != (nowPct > 0.0))
                return true;
            return Math.Abs(nowPct - lastLoggedPct) >= LogStepPct;
        }
    }

    /// <summary>T289 cold-weather tourism (study docs/research/round3-2026-11-18/t289-tourism-weather.md). Vanilla cuts tourist arrivals
    /// below AttractivenessParameterData.m_ExtremeTemperature.x (prefab -10 C), lerping to m_TemperatureAffect.y (-0.3) at 10 C colder
    /// (TourismSystem.GetWeatherEffect, TourismSystem.cs:215-217; AttractivenessParametersPrefab.cs:27, 52; same lines in 1.6.0f1 and
    /// 1.6.4f1). While a ski area is open and the option is on, ResortTourismBonusSystem lowers only that .x to ColdThresholdOpenC.
    /// m_TemperatureAffect is never touched: its y is shared with the hot side (above m_ExtremeTemperature.y).</summary>
    public static class ColdTourismRules
    {
        /// <summary>The cold threshold while at least one ski area is open, Celsius. The full -30 % then needs -40 C.</summary>
        public const float ColdThresholdOpenC = -30f;

        /// <summary>The option "Cold weather keeps no tourists away while a ski area is open" (SkiResortSettings.ColdWeatherTourism).</summary>
        public const bool DefaultEnabled = true;

        /// <summary>An area counts as open when its sign works (efficiency above 0) and its last score counts at least one run (the same
        /// "open run" test as OffSeasonStaffSystem: Result.RunsCounted).</summary>
        public static bool IsOpenArea(double efficiency, int runsCounted) => efficiency > 0.0 && runsCounted > 0;

        /// <summary>True when the lowered threshold applies: the option is on and at least one ski area is open.</summary>
        public static bool Engaged(bool optionOn, int openAreas) => optionOn && openAreas > 0;

        /// <summary>The cold threshold to apply, Celsius. <paramref name="vanillaC"/> = the PREFAB's m_ExtremeTemperature.x. Engaged:
        /// the colder of vanilla and ColdThresholdOpenC (a vanilla value already colder stays). Otherwise, or when vanilla is not finite:
        /// vanilla unchanged.</summary>
        public static float ThresholdC(bool optionOn, int openAreas, float vanillaC)
        {
            if (!Engaged(optionOn, openAreas) || float.IsNaN(vanillaC) || float.IsInfinity(vanillaC))
                return vanillaC;
            return Math.Min(vanillaC, ColdThresholdOpenC);
        }
    }

    /// <summary>A rolling mean of the city's tourist count over the last game day. Add a sample every SampleFrames simulation frames;
    /// Mean is -1 until MinSamples are in (3 game hours). Clear at every load.</summary>
    public sealed class TouristDayMean
    {
        public const int SamplesPerDay = 64;
        public const int SampleFrames = 262144 / SamplesPerDay;   // 4,096 frames (a game day = 262,144 frames, TimeSystem)
        public const int MinSamples = 8;                          // 3 game hours

        private readonly int[] m_Ring = new int[SamplesPerDay];
        private int m_Count, m_Next;
        private long m_Sum;

        public int Count => m_Count;

        public void Clear()
        {
            m_Count = m_Next = 0;
            m_Sum = 0;
        }

        /// <summary>One sample. A negative count is ignored.</summary>
        public void Add(int tourists)
        {
            if (tourists < 0)
                return;
            if (m_Count == SamplesPerDay)
                m_Sum -= m_Ring[m_Next];
            else
                m_Count++;
            m_Ring[m_Next] = tourists;
            m_Sum += tourists;
            m_Next = (m_Next + 1) % SamplesPerDay;
        }

        /// <summary>The mean of the samples in, rounded half away from zero. -1 below MinSamples.</summary>
        public int Mean => m_Count < MinSamples ? -1 : (int)Math.Round((double)m_Sum / m_Count, MidpointRounding.AwayFromZero);
    }
}
