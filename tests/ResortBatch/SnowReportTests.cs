// Tests for the snow report's pure rules (SnowReportRules.cs, T200). Oracles are hand values (worked with a calculator), never a second run
// of the code. Not testable headless: the climate samples, the snow-map read (SkiSnowInjectionSystem.TrySampleDepthM), the run outline
// extremes (SkiSnowReportSystem) and CmPerPrecipHour itself (the T200 in-game check measures it).

using System;
using SkiResortMod.Systems;

namespace ResortBatch
{
    public static class SnowReportTests
    {
        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            SectionSeason();
            SectionHour();
            SectionSums();
            SectionRounding();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);

        private static bool Near(double a, double b, double eps) => Math.Abs(a - b) <= eps;

        private static void SectionSeason()
        {
            const string w = "SR-1 season";
            // 12 days a year: a month is 24 hours. October (9) starts at hour 216.
            Check(SnowReportRules.MonthStartHour(9, 12) == 216, w, "MonthStartHour(9, 12) != 216");
            Check(SnowReportRules.MonthStartHour(0, 12) == 0, w, "MonthStartHour(0, 12) != 0");
            // 1 day a year: a month is 2 hours. December (11) starts at hour 22.
            Check(SnowReportRules.MonthStartHour(11, 1) == 22, w, "MonthStartHour(11, 1) != 22");
            // Out of range clamps.
            Check(SnowReportRules.MonthStartHour(14, 12) == 264, w, "MonthStartHour(14, 12) != 264 (clamped to 11)");
            // The start hour is the first hour of that month (SkiDayRules.MonthOfHour) and the hour before is the month before.
            for (int days = 1; days <= 30; days++)
                for (int m = 1; m < 12; m++)
                {
                    int h = SnowReportRules.MonthStartHour(m, days);
                    if (SkiDayRules.MonthOfHour(h, days) != m || SkiDayRules.MonthOfHour(h - 1, days) != m - 1)
                    {
                        Check(false, w, $"MonthStartHour({m}, {days}) = {h} is not the month's first hour");
                        return;
                    }
                }
            // Season hours, the current hour included, wrapping over the year start (288 hours).
            Check(SnowReportRules.SeasonHours(220, 216, 288) == 5, w, "SeasonHours(220, 216, 288) != 5");
            Check(SnowReportRules.SeasonHours(216, 216, 288) == 1, w, "SeasonHours at the first hour != 1");
            Check(SnowReportRules.SeasonHours(10, 216, 288) == 83, w, "SeasonHours(10, 216, 288) != 83 (72 before the year start + 11)");
            Check(SnowReportRules.SeasonHours(215, 216, 288) == 288, w, "SeasonHours the hour before the start != 288 (a full year)");
            Check(SnowReportRules.SeasonHours(5, 0, 0) == 0, w, "SeasonHours with no hours != 0");
        }

        private static void SectionHour()
        {
            const string w = "SR-2 hour";
            // Base height 0, lapse 0.01 °C per m (vanilla snowTemperatureHeightScale), freezing 0 °C.
            Check(Near(SnowReportRules.HourSnowCm(1f, -5f, 1000f, 0f, 0.01f, 0f), 4.37, 1e-5), w, "precip 1, -15 °C: 4.37 cm");
            Check(SnowReportRules.HourSnowCm(0.05f, -5f, 1000f, 0f, 0.01f, 0f) == 0f, w, "precip 0.05 is under 0.1: no snow");
            Check(SnowReportRules.HourSnowCm(1f, 5f, 0f, 0f, 0.01f, 0f) == 0f, w, "5 °C at the base height: rain, no snow");
            // The same hour snows 600 m higher (5 - 6 = -1 °C): the elevation decides.
            Check(Near(SnowReportRules.HourSnowCm(0.5f, 5f, 600f, 0f, 0.01f, 0f), 2.185, 1e-5), w, "5 °C ambient, 600 m: -1 °C, 0.5 x 4.37 cm");
            Check(SnowReportRules.HourSnowCm(float.NaN, -5f, 0f, 0f, 0.01f, 0f) == 0f, w, "NaN precipitation: no snow");
        }

        private static void SectionSums()
        {
            const string w = "SR-3 sums";
            var precip = new float[288];
            var ambient = new float[288];
            for (int h = 0; h < 288; h++)
            {
                precip[h] = h < 24 ? 0.5f : 0f;
                ambient[h] = -2f;
            }
            // Hours 0..23 snow 0.5 each: 24 x 0.5 x 4.37 = 52.44 cm.
            Check(Near(SnowReportRules.SnowfallCm(precip, ambient, 23, 24, 0f, 0f, 0.01f, 0f), 52.44, 1e-3), w, "24 h at hour 23 != 52.44");
            // At hour 5 the window is hours 5..0 and 287..270: 6 x 0.5 x 4.37 = 13.11 cm.
            Check(Near(SnowReportRules.SnowfallCm(precip, ambient, 5, 24, 0f, 0f, 0.01f, 0f), 13.11, 1e-3), w, "24 h at hour 5 != 13.11 (wrap)");
            // A window longer than the year counts each hour once.
            Check(Near(SnowReportRules.SnowfallCm(precip, ambient, 100, 1000, 0f, 0f, 0.01f, 0f), 52.44, 1e-3), w, "an over-long window != one year");
            // Too warm at 0 m with ambient +3 °C, snowing above 300 m: the summit gets it, the base does not.
            for (int h = 0; h < 288; h++)
                ambient[h] = 3f;
            Check(SnowReportRules.SnowfallCm(precip, ambient, 23, 24, 100f, 0f, 0.01f, 0f) == 0f, w, "base at 100 m (+2 °C) != 0");
            Check(Near(SnowReportRules.SnowfallCm(precip, ambient, 23, 24, 2000f, 0f, 0.01f, 0f), 52.44, 1e-3), w, "summit at 2,000 m != 52.44");
            Check(SnowReportRules.SnowfallCm(precip, ambient, 23, 24, float.NaN, 0f, 0.01f, 0f) == -1f, w, "NaN elevation != -1");
            Check(SnowReportRules.SnowfallCm(precip, new float[10], 23, 24, 0f, 0f, 0.01f, 0f) == -1f, w, "mismatched arrays != -1");
            Check(SnowReportRules.SnowfallCm(null, ambient, 23, 24, 0f, 0f, 0.01f, 0f) == -1f, w, "no samples != -1");
        }

        private static void SectionRounding()
        {
            const string w = "SR-4 rounding";
            Check(SnowReportRules.WholeCm(2.5f) == 3, w, "WholeCm(2.5) != 3");
            Check(SnowReportRules.WholeCm(0f) == 0, w, "WholeCm(0) != 0");
            Check(SnowReportRules.WholeCm(-0.1f) == -1, w, "WholeCm(-0.1) != -1");
            Check(SnowReportRules.WholeCm(float.NaN) == -1, w, "WholeCm(NaN) != -1");
            Check(SnowReportRules.DepthCm(1.62f) == 162, w, "DepthCm(1.62 m) != 162");
            Check(SnowReportRules.DepthCm(-1f) == -1, w, "DepthCm(-1) != -1");
        }
    }
}
