// The ski area snow report (T200, docs/research/post-release-round2/T200-snow-report.md): the pure part. SkiSnowReportSystem reads the
// game and calls these.
//   Snowfall comes from the climate, not from depth changes. The weather is a pure function of the date and repeats every game year
//   (ClimateSystem.cs:607-654, 1.6.2f1), so the snowfall of any past hour at any elevation follows from that hour's climate sample. An hour
//   snows at an elevation when SkiDayRules.IsSnowHour holds (precipitation >= 0.1, SnowSystem.cs:487, and the lapsed air at or below
//   freezing). It adds precipitation x CmPerPrecipHour. Melt and drift do not subtract: the report counts what fell, as a resort's does.
//   Nothing is saved: a load rebuilds every total from the date.
//   Windows: the last 24 game hours (current hour included), and the season from the first hour of the ski season's first month
//   (SkiClimateCalendar.SeasonFirstMonth, the season-pass season) up to now, wrapping over the year start. No "7 days": a game day
//   advances the calendar by a month (12 days a year), so seven days outlast any winter.
// Pure file (System + SkiDayRules). tests/ResortBatch links it (SnowReportTests).

using System;

namespace SkiResortMod.Systems
{
    public static class SnowReportRules
    {
        /// <summary>Snow depth added by one game hour at precipitation 1, in cm: vanilla's add constant read off the kernel's C# call site
        /// (SnowSystem.cs:455-481, 1.6.2f1): _AddMultiplier 1e-5 x _Timestep 0.2 per dispatch, one dispatch per 4 sim frames
        /// (GetUpdateInterval :226-229, SnowSimSpeed 1), 10,922.7 frames per game hour, kSnowHeightScale 8 m. 1e-5 x 0.2 x 2,730.67 x
        /// 800 cm = 4.37 cm. UNVERIFIED: the HLSL is compiled. The T200 in-game check measures it.</summary>
        public const float CmPerPrecipHour = 4.37f;

        public const int HoursInWindow = 24;   // "last 24 hours"
        public const int MonthsPerYear = 12;

        /// <summary>The first hour key of calendar month <paramref name="month0"/> (0..11): the smallest h with SkiDayRules.MonthOfHour(h) ==
        /// month0, ceil(month0 x 24 x days / 12). Integer arithmetic.</summary>
        public static int MonthStartHour(int month0, int daysPerYear)
        {
            int days = daysPerYear < 1 ? 1 : daysPerYear;
            int m = month0 < 0 ? 0 : month0 > MonthsPerYear - 1 ? MonthsPerYear - 1 : month0;
            return (m * 24 * days + MonthsPerYear - 1) / MonthsPerYear;
        }

        /// <summary>The hours of the season so far, the current hour included: from the season's first hour to <paramref name="hourKey"/>,
        /// wrapping over the year start. 1..hoursPerYear.</summary>
        public static int SeasonHours(int hourKey, int seasonStartHour, int hoursPerYear)
        {
            if (hoursPerYear < 1)
                return 0;
            int d = (hourKey - seasonStartHour) % hoursPerYear;
            if (d < 0)
                d += hoursPerYear;
            return d + 1;
        }

        /// <summary>One hour's snowfall in cm at an elevation: precipitation x CmPerPrecipHour when the hour snows there, else 0.</summary>
        public static float HourSnowCm(float precipitation, float ambientC, float elevationM, float baseHeightM, float lapsePerM, float freezingC)
            => SkiDayRules.IsSnowHour(precipitation, ambientC, elevationM, baseHeightM, lapsePerM, freezingC) && precipitation > 0f
                ? precipitation * CmPerPrecipHour : 0f;

        /// <summary>The snowfall in cm over the <paramref name="hours"/> hours ending at <paramref name="hourKey"/> (included), wrapping over
        /// the year start. The arrays hold one climate sample per hour of the year (index = hour key). -1 when they are missing or short.</summary>
        public static float SnowfallCm(float[] precip, float[] ambient, int hourKey, int hours, float elevationM, float baseHeightM,
            float lapsePerM, float freezingC)
        {
            if (precip == null || ambient == null || precip.Length == 0 || ambient.Length != precip.Length || IsBad(elevationM))
                return -1f;
            int n = precip.Length;
            if (hours > n)
                hours = n;
            double sum = 0.0;
            for (int k = 0; k < hours; k++)
            {
                int h = (hourKey - k) % n;
                if (h < 0)
                    h += n;
                sum += HourSnowCm(precip[h], ambient[h], elevationM, baseHeightM, lapsePerM, freezingC);
            }
            return (float)sum;
        }

        /// <summary>Whole cm for the hub (half away from zero). Negative or not finite gives -1 (unknown).</summary>
        public static int WholeCm(float cm)
        {
            if (IsBad(cm) || cm < 0f)
                return -1;
            return (int)Math.Round(cm, MidpointRounding.AwayFromZero);
        }

        /// <summary>A depth in m (0..8, the snow map) as whole cm. Negative or not finite gives -1.</summary>
        public static int DepthCm(float depthM) => IsBad(depthM) || depthM < 0f ? -1 : WholeCm(depthM * 100f);

        private static bool IsBad(float v) => float.IsNaN(v) || float.IsInfinity(v);
    }
}
