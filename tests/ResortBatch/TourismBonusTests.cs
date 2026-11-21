// Tests for TourismBonusRules.cs (T226 resort city tourism bonus, DECISIONS 666) and TouristDayMean (T225).
//   TB-1 constants: base 2 %, 0.25 % per lift line and lodge point, cap 0-20 default 10, merged lodge 2 (the ruling, and the Options text)
//   TB-2 tier points by lodge id; every catalog lodge maps to a ruled value
//   TB-3 area size: needs a running lift line and a lodge
//   TB-4 open share: in season, closed runs, summer, empty, not finite
//   TB-5 city sum and cap, off at 0, the vanilla Relative stacking
//   TB-6 log step
//   TB-7 tourist day mean: warm-up, rolling window, rounding, negatives
//   TB-8 T289 cold threshold (ColdTourismRules): open / closed / option off / a colder vanilla value preserved, the open-area test
// Oracles are literal hand values (the comments show the arithmetic).

using System;
using SkiResortMod.Systems;
using R = SkiResortMod.Systems.TourismBonusRules;

namespace ResortBatch
{
    public static class TourismBonusTests
    {
        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Constants();
            TierPoints();
            AreaSize();
            OpenShare();
            CityAndStack();
            LogStep();
            DayMean();
            ColdThreshold();
        }

        private static bool Near(double a, double b, double tol = 1e-9) => Math.Abs(a - b) <= tol;

        private static void C(bool ok, string where, string msg) => s_Check(ok, where, msg);

        // TB-1
        private static void Constants()
        {
            const string W = "tourism TB-1 constants";
            C(R.BasePct == 2.0 && R.PerLiftLinePct == 0.25 && R.PerTierPointPct == 0.25, W, "base 2 %, 0.25 % per lift line and per lodge point");
            C(R.CapMinPct == 0 && R.CapMaxPct == 20 && R.CapDefaultPct == 10, W, "the option: 0-20 %, default 10 %");
            C(R.MergedTierPoints == 2, W, "a merged lodge counts 2 points");
            C(R.ClampCapPct(-5) == 0 && R.ClampCapPct(25) == 20 && R.ClampCapPct(7) == 7, W, "the cap clamps to 0..20");
        }

        // TB-2
        private static void TierPoints()
        {
            const string W = "tourism TB-2 tier points";
            C(R.TierPointsOf("hut", false) == 1 && R.TierPointsOf("aFrame", false) == 1, W, "Mountain Hut 1, A-Frame 1");
            C(R.TierPointsOf("mid", false) == 2, W, "Chalet 2");
            C(R.TierPointsOf("summit", false) == 3 && R.TierPointsOf("grand", false) == 3, W, "Summit 3, Grand 3");
            C(R.TierPointsOf("grand", true) == 2 && R.TierPointsOf("hut", true) == 2, W, "a merged building counts 2 whatever its tier");
            C(R.TierPointsOf(null, false) == 1 && R.TierPointsOf("unknown", false) == 1, W, "an unknown id counts 1");
            // A new lodge tier must get a ruled value: every catalog id is one of the five named above.
            for (int i = 0; i < SkiCatalog.Lodges.Count; i++)
            {
                string id = SkiCatalog.Lodges[i].Id;
                C(id == "aFrame" || id == "hut" || id == "mid" || id == "summit" || id == "grand", W,
                    "catalog lodge '" + id + "' has a ruled tier point value (add it to TierPointsOf and the Options text)");
            }
        }

        // TB-3
        private static void AreaSize()
        {
            const string W = "tourism TB-3 area size";
            C(R.AreaSizePct(0, 3) == 0.0, W, "no running lift line: 0");
            C(R.AreaSizePct(2, 0) == 0.0, W, "no lodge: 0");
            C(Near(R.AreaSizePct(1, 1), 2.5), W, "one lift and a hut: 2 + 0.25 + 0.25 = 2.5 %");
            // Ash Flat (tourism-lab): 8 lift lines, own lodges 1 + 2 + 2 + 3 + 3 = 11, two merged hotels 4 -> 15 points.
            C(Near(R.AreaSizePct(8, 15), 7.75), W, "8 lift lines, 15 points: 2 + 2 + 3.75 = 7.75 %");
            C(Near(R.AreaSizePct(40, 40), 22.0), W, "a huge area: 2 + 10 + 10 = 22 % (only the city cap limits it)");
        }

        // TB-4
        private static void OpenShare()
        {
            const string W = "tourism TB-4 open share";
            C(Near(R.OpenShare(5.5, 21, 5.5, 21), 1.0), W, "everything open: 1");
            C(Near(R.OpenShare(4.55, 21, 5.5, 21), 25.55 / 26.5), W, "some runs closed: (4.55 + 21) / (5.5 + 21)");
            // Summer: runs 0, a fixed-grip lift counts its summer share 0.25 of 6.
            C(Near(R.OpenShare(0, 1.5, 4, 6), 0.15), W, "summer: 1.5 / 10 = 0.15");
            C(R.OpenShare(0, 0, 0, 0) == 0.0, W, "nothing to count: 0");
            C(R.OpenShare(double.NaN, 1, 4, 6) == 0.0 && R.OpenShare(1, 1, double.PositiveInfinity, 6) == 0.0, W, "not finite: 0");
            C(R.OpenShare(12, 0, 10, 0) == 1.0, W, "clamped to 1");
            C(Near(R.AreaBonusPct(7.75, 0.5), 3.875), W, "bonus = size x share");
            C(R.AreaBonusPct(7.75, 0) == 0.0 && R.AreaBonusPct(0, 1) == 0.0, W, "closed or no size: 0");
        }

        // TB-5
        private static void CityAndStack()
        {
            const string W = "tourism TB-5 city";
            C(Near(R.CityBonusPct(7.75, 10), 7.75), W, "under the cap: the sum");
            C(Near(R.CityBonusPct(12.5, 10), 10.0), W, "over the cap: the cap");
            C(R.CityBonusPct(7.75, 0) == 0.0, W, "cap 0 = off");
            C(Near(R.CityBonusPct(30, 25), 20.0), W, "a cap above 20 clamps to 20");
            C(R.CityBonusPct(double.PositiveInfinity, 10) == 0.0 && R.CityBonusPct(-1, 10) == 0.0, W, "not finite or negative: 0");
            // Vanilla Relative stacking (CityModifierUpdateSystem.cs:229): y' = y (1 + d) + d. The lab's log: 0.2003 + 5 % -> 0.2603.
            C(Math.Abs(R.StackRelative(0.2003f, 0.05f) - 0.260315f) < 1e-5f, W, "0.2003 (1.05) + 0.05 = 0.260315");
            C(R.StackRelative(0f, 0.1f) == 0.1f, W, "no other modifier: y = d");
        }

        // TB-6
        private static void LogStep()
        {
            const string W = "tourism TB-6 log";
            C(R.LogWorthy(0, 2.5) && R.LogWorthy(2.5, 0), W, "on / off is always logged");
            C(!R.LogWorthy(4.0, 4.3) && R.LogWorthy(4.0, 4.5) && R.LogWorthy(4.0, 3.4), W, "0.5 points or more");
        }

        // TB-7
        private static void DayMean()
        {
            const string W = "tourism TB-7 day mean";
            C(TouristDayMean.SampleFrames == 4096 && TouristDayMean.SamplesPerDay * TouristDayMean.SampleFrames == 262144, W, "64 samples a game day");
            var m = new TouristDayMean();
            for (int i = 0; i < TouristDayMean.MinSamples - 1; i++)
                m.Add(1000);
            C(m.Mean == -1, W, "-1 below MinSamples (3 game hours)");
            m.Add(1001);
            C(m.Mean == 1000, W, "8 samples: 8,001 / 8 = 1,000.125 -> 1,000");
            m.Add(-5);
            C(m.Count == 8, W, "a negative sample is ignored");
            m.Clear();
            for (int i = 0; i < TouristDayMean.SamplesPerDay; i++)
                m.Add(100);
            for (int i = 0; i < TouristDayMean.SamplesPerDay / 2; i++)
                m.Add(300);
            C(m.Count == TouristDayMean.SamplesPerDay && m.Mean == 200, W, "rolling: half the day at 100, half at 300 -> 200");
            m.Clear();
            for (int i = 0; i < 8; i++)
                m.Add(i < 4 ? 10 : 11);
            C(m.Mean == 11, W, "10.5 rounds half away from zero -> 11");
        }

        // TB-8
        private static void ColdThreshold()
        {
            const string W = "tourism TB-8 cold threshold";
            C(ColdTourismRules.ColdThresholdOpenC == -30f && ColdTourismRules.DefaultEnabled, W,
                "-30 C while a ski area is open, option default on (the Options text says -30 °C)");
            // The vanilla prefab value is -10 C (AttractivenessParametersPrefab.cs:27).
            C(ColdTourismRules.ThresholdC(true, 1, -10f) == -30f, W, "open area, option on: -10 -> -30");
            C(ColdTourismRules.ThresholdC(true, 3, -10f) == -30f, W, "three open areas: the same constant, not scaled");
            C(ColdTourismRules.ThresholdC(true, 0, -10f) == -10f, W, "no open area: vanilla -10");
            C(ColdTourismRules.ThresholdC(false, 2, -10f) == -10f, W, "option off: vanilla -10");
            C(ColdTourismRules.ThresholdC(true, 1, -35f) == -35f, W, "a vanilla value already colder than -30 is preserved");
            C(ColdTourismRules.ThresholdC(false, 0, -12.5f) == -12.5f, W, "not engaged: any vanilla value passes through unchanged");
            C(float.IsNaN(ColdTourismRules.ThresholdC(true, 1, float.NaN)), W, "a vanilla value that is not finite passes through (no write)");
            C(ColdTourismRules.Engaged(true, 1) && !ColdTourismRules.Engaged(true, 0) && !ColdTourismRules.Engaged(false, 1), W,
                "engaged = option on and at least one open area");
            C(ColdTourismRules.IsOpenArea(1.0, 1) && !ColdTourismRules.IsOpenArea(1.0, 0) && !ColdTourismRules.IsOpenArea(0.0, 4), W,
                "open = a working sign and at least one counted run");
        }
    }
}
