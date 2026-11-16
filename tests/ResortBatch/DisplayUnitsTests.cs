// Tests for the display layer of slopes, units and run grading (DisplayUnits.cs, post-release round 1 T160 T161 T162). Oracles are literal
// hand values (worked with a calculator and node), never a second run of the code. The hub's twin (src/hub/units.ts) is pinned by
// tools/test-hub-rules.js T60 with the same numbers.
// Not testable headless: DisplayState (the game's unit setting, the city theme), the LocalizedNumber rendering.

using System;
using SkiResortMod.Systems;

namespace ResortBatch
{
    public static class DisplayUnitsTests
    {
        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            SectionSlope();
            SectionRounding();
            SectionUnits();
            SectionText();
            SectionScheme();
            SectionCatTrack();
            SectionOptions();
            SectionEuropean();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);

        private static bool Near(double a, double b, double eps) => Math.Abs(a - b) <= eps;

        private static void Eq<T>(T got, T want, string where, string call)
        {
            Check(Equals(got, want), where, call + " = " + got + ", want " + want);
        }

        private static void SectionSlope()
        {
            const string w = "DU-1 slope";
            Check(Near(DisplayUnits.GradeToDeg(0.25f), 14.036, 0.001), w, "GradeToDeg(0.25) != 14.036");
            Check(Near(DisplayUnits.GradeToDeg(1f), 45.0, 0.0001), w, "GradeToDeg(1) != 45");
            Check(float.IsNaN(DisplayUnits.GradeToDeg(float.NaN)) && float.IsNaN(DisplayUnits.GradeToDeg(float.PositiveInfinity)), w, "not finite -> NaN");
            // The band thresholds as the player reads them (SkiSlopeClassifier: 6 / 25 / 40 / 60 %)
            Eq(DisplayUnits.Deg1(0.06f), 3.4f, w, "Deg1(0.06)");
            Eq(DisplayUnits.Deg1(0.25f), 14f, w, "Deg1(0.25)");
            Eq(DisplayUnits.Deg1(0.40f), 21.8f, w, "Deg1(0.40)");
            Eq(DisplayUnits.Deg1(0.60f), 31f, w, "Deg1(0.60)");
            Eq(DisplayUnits.Deg1(-0.25f), -14f, w, "Deg1(-0.25)");
            Eq(DisplayUnits.Deg1(0f), 0f, w, "Deg1(0)");
            Check(float.IsNaN(DisplayUnits.Deg1(float.NaN)), w, "Deg1(NaN) not NaN");
            Check(Near(DisplayUnits.DegToGrade(45f), 1.0, 0.0001), w, "DegToGrade(45) != 1");
            Check(Near(DisplayUnits.DegToGrade(14f), 0.24933, 0.00001), w, "DegToGrade(14) != 0.24933");
            Check(Near(DisplayUnits.DegToGrade(90f), 572.96, 0.05), w, "DegToGrade(90) not clamped to 89.9 deg (572.96)");
        }

        private static void SectionRounding()
        {
            const string w = "DU-2 rounding";
            Eq(DisplayUnits.Round1(2.25f), 2.3f, w, "Round1(2.25)");
            Eq(DisplayUnits.Round1(-2.25f), -2.3f, w, "Round1(-2.25)");
            Eq(DisplayUnits.Whole(2.5f), 3, w, "Whole(2.5)");
            Eq(DisplayUnits.Whole(-2.5f), -3, w, "Whole(-2.5)");
            Eq(DisplayUnits.Whole(float.NaN), 0, w, "Whole(NaN)");
        }

        private static void SectionUnits()
        {
            const string w = "DU-3 units";
            Eq(DisplayUnits.Volume(100f, false), 100, w, "Volume(100 m3, metric)");
            Eq(DisplayUnits.Volume(100f, true), 131, w, "Volume(100 m3, imperial)");          // 130.8 yd3
            Eq(DisplayUnits.AreaHa(3.24f, false), 3.2f, w, "AreaHa(3.24, metric)");
            Eq(DisplayUnits.AreaHa(1f, true), 2.5f, w, "AreaHa(1 ha, imperial)");             // 2.47 acres
            Eq(DisplayUnits.Speed(5f, false), 5f, w, "Speed(5 m/s, metric)");
            Eq(DisplayUnits.Speed(5f, true), 984f, w, "Speed(5 m/s, imperial)");              // 984.25 ft/min
            Eq(DisplayUnits.Speed(2.3f, true), 453f, w, "Speed(2.3 m/s, imperial)");          // 452.76 ft/min
            Eq(DisplayUnits.Depth(254f, true), 100, w, "Depth(254 cm, imperial)");
            Eq(DisplayUnits.Depth(100f, true), 39, w, "Depth(100 cm, imperial)");             // 39.37 in
            Eq(DisplayUnits.Depth(80f, false), 80, w, "Depth(80 cm, metric)");
        }

        private static void SectionText()
        {
            const string w = "DU-4 text";
            // vanilla's metric "length" thresholds: m under 1,000 m, else km. Imperial (T215): yd under 500 yd (457.2 m), else mi
            Eq(DisplayUnits.LengthText(376f, false), "376 m", w, "LengthText(376, metric)");
            Eq(DisplayUnits.LengthText(999.4f, false), "999 m", w, "LengthText(999.4, metric)");
            Eq(DisplayUnits.LengthText(1000f, false), "1.0 km", w, "LengthText(1000, metric)");
            Eq(DisplayUnits.LengthText(12345f, false), "12.3 km", w, "LengthText(12345, metric)");
            Eq(DisplayUnits.LengthText(100f, true), "109 yd", w, "LengthText(100, imperial)");
            Eq(DisplayUnits.LengthText(457.1f, true), "500 yd", w, "LengthText(457.1, imperial): the last yard value");
            Eq(DisplayUnits.LengthText(457.2f, true), "0.3 mi", w, "LengthText(457.2 = 500 yd, imperial): miles from here");
            Eq(DisplayUnits.LengthText(-457.2f, true), "-0.3 mi", w, "LengthText(-457.2, imperial): the switch on the magnitude");
            Eq(DisplayUnits.LengthText(1000f, true), "0.6 mi", w, "LengthText(1000, imperial)");
            Eq(DisplayUnits.LengthText(1600f, true), "1.0 mi", w, "LengthText(1600, imperial)");
            Eq(DisplayUnits.LengthText(1609f, true), "1.0 mi", w, "LengthText(1609, imperial)");
            Check(Math.Abs(DisplayUnits.ImperialMilesFromM - 500.0 * DisplayUnits.MetersPerYard) < 1e-3 && !DisplayUnits.ShowsMiles(float.NaN), w,
                  "ImperialMilesFromM is 500 yd; NaN never shows miles");
            Eq(DisplayUnits.LengthText(5000f, true), "3.1 mi", w, "LengthText(5000, imperial)");
            Eq(DisplayUnits.LengthText(float.NaN, false), "–", w, "LengthText(NaN)");
            Eq(DisplayUnits.AreaHaText(3.24f, false), "3.2 ha", w, "AreaHaText(3.24, metric)");
            Eq(DisplayUnits.AreaHaText(1f, true), "2.5 acres", w, "AreaHaText(1, imperial)");
            Eq(DisplayUnits.SlopeText(0.25f), "14°", w, "SlopeText(0.25)");
            Eq(DisplayUnits.SlopeText(0.40f), "21.8°", w, "SlopeText(0.40)");
            Eq(DisplayUnits.SlopeText(float.NaN), "–", w, "SlopeText(NaN)");
            Eq(DisplayUnits.SlopeText(0.25f, true), "25%", w, "SlopeText(0.25, percent)");
            Eq(DisplayUnits.SlopeText(0.354f, true), "35%", w, "SlopeText(0.354, percent)");
            Eq(DisplayUnits.SlopeText(float.NaN, true), "–", w, "SlopeText(NaN, percent)");
            Eq(DisplayUnits.HeightText(350f, false), "350 m", w, "HeightText(350, metric)");
            Eq(DisplayUnits.HeightText(350f, true), "1,148 ft", w, "HeightText(350, imperial)");
        }

        private static void SectionScheme()
        {
            const string w = "DU-5 grading";
            Eq(DisplayUnits.ResolveScheme(GradeSchemeOption.Auto, "European"), GradeScheme.European, w, "Auto + European theme");
            Eq(DisplayUnits.ResolveScheme(GradeSchemeOption.Auto, "North American"), GradeScheme.NorthAmerican, w, "Auto + North American theme");
            Eq(DisplayUnits.ResolveScheme(GradeSchemeOption.Auto, null), GradeScheme.NorthAmerican, w, "Auto + no theme");
            Eq(DisplayUnits.ResolveScheme(GradeSchemeOption.Auto, "european"), GradeScheme.NorthAmerican, w, "Auto + wrong case (ordinal)");
            Eq(DisplayUnits.ResolveScheme(GradeSchemeOption.NorthAmerican, "European"), GradeScheme.NorthAmerican, w, "NorthAmerican wins over the theme");
            Eq(DisplayUnits.ResolveScheme(GradeSchemeOption.European, null), GradeScheme.European, w, "European without a theme");
            Eq(DisplayUnits.SchemeSuffix(GradeScheme.European), "@EU", w, "SchemeSuffix(European)");
            Eq(DisplayUnits.SchemeSuffix(GradeScheme.NorthAmerican), "", w, "SchemeSuffix(NorthAmerican)");
            // In-world colors (labels, piste map): European grading shows the class of the band and the crux (DU-8), North American the band
            RunLabelRules.BandRgb(1, GradeScheme.European, 10f, out byte r, out byte g, out byte b);
            Eq(r * 1000000 + g * 1000 + b, 76175080, w, "BandRgb(green band, 10 %, European) (green piste)");
            RunLabelRules.BandRgb(1, GradeScheme.European, 20f, out r, out g, out b);
            Eq(r * 1000000 + g * 1000 + b, 52120220, w, "BandRgb(green band, 20 %, European) (blue piste)");
            RunLabelRules.BandRgb(2, GradeScheme.European, 30f, out r, out g, out b);
            Eq(r * 1000000 + g * 1000 + b, 214048040, w, "BandRgb(blue band, European) (red)");
            RunLabelRules.BandRgb(3, GradeScheme.European, 45f, out r, out g, out b);
            Eq(r * 1000000 + g * 1000 + b, 14014018, w, "BandRgb(black band, European) (unchanged)");
            RunLabelRules.BandRgb(4, GradeScheme.European, 70f, out r, out g, out b);
            Eq(r * 1000000 + g * 1000 + b, 240130020, w, "BandRgb(double-black band, European) (ski route orange)");
            RunLabelRules.BandRgb(1, GradeScheme.NorthAmerican, 20f, out r, out g, out b);
            Eq(r * 1000000 + g * 1000 + b, 76175080, w, "BandRgb(green band, 20 %, North American) (unchanged: the grade is ignored)");
            RunLabelRules.BandRgb(-2, GradeScheme.European, 20f, out r, out g, out b);
            Eq(r * 1000000 + g * 1000 + b, 220150060, w, "BandRgb(cannot be rated, European) (unchanged)");
            // The values the hub reads (bindings.ts GRADE_SCHEME) and the Options values (saved settings) never move.
            Eq((int)GradeScheme.NorthAmerican * 10 + (int)GradeScheme.European, 1, w, "GradeScheme values 0 / 1");
            Eq((int)GradeSchemeOption.Auto * 100 + (int)GradeSchemeOption.NorthAmerican * 10 + (int)GradeSchemeOption.European, 12, w, "GradeSchemeOption values 0 / 1 / 2");
        }

        /// <summary>DU-8: the European class (T162 rev. 2). Its own limits on the crux grade (green under 15 %, blue to 25, red to 40, black to
        /// 60, ski route above), and the edges: the class never crosses a SlopeBand boundary, so a European green and blue are one score
        /// band and nothing else is ever split.</summary>
        private static void SectionEuropean()
        {
            const string w = "DU-8 European class";
            Eq(DisplayUnits.EuGreenMaxPct, 15f, w, "EuGreenMaxPct");
            Eq(DisplayUnits.EuClassOf(1, 6f), EuClass.Green, w, "EuClassOf(green band, 6 %)");
            Eq(DisplayUnits.EuClassOf(1, 14.99f), EuClass.Green, w, "EuClassOf(green band, 14.99 %)");
            Eq(DisplayUnits.EuClassOf(1, 15f), EuClass.Blue, w, "EuClassOf(green band, 15 %) (half-open, like BandOf)");
            Eq(DisplayUnits.EuClassOf(1, 24.99f), EuClass.Blue, w, "EuClassOf(green band, 24.99 %)");
            Eq(DisplayUnits.EuClassOf(1, 26f), EuClass.Blue, w, "EuClassOf(green band, 26 %) (a wide-ease run rated green: European blue)");
            Eq(DisplayUnits.EuClassOf(1, float.NaN), EuClass.Green, w, "EuClassOf(green band, NaN)");
            Eq(DisplayUnits.EuClassOf(1, -1f), EuClass.Green, w, "EuClassOf(green band, -1 = no rating)");
            Eq(DisplayUnits.EuClassOf(2, 25f), EuClass.Red, w, "EuClassOf(blue band, 25 %)");
            Eq(DisplayUnits.EuClassOf(2, 45f), EuClass.Red, w, "EuClassOf(blue band, 45 %) (a capped black crux: red, as its band)");
            Eq(DisplayUnits.EuClassOf(3, 40f), EuClass.Black, w, "EuClassOf(black band, 40 %)");
            Eq(DisplayUnits.EuClassOf(4, 60f), EuClass.Route, w, "EuClassOf(double-black band, 60 %)");
            Eq(DisplayUnits.EuClassOf(0, 3f), EuClass.Flat, w, "EuClassOf(flat band)");
            Eq(DisplayUnits.EuClassOf(-1, 20f), EuClass.Flat, w, "EuClassOf(pending)");
            Eq(DisplayUnits.EuClassOf(-2, 20f), EuClass.Flat, w, "EuClassOf(cannot be rated)");
            Eq(DisplayUnits.EuClassOfBand(1), EuClass.Blue, w, "EuClassOfBand(green band) (no grade: the hardest it can be)");
            Eq(DisplayUnits.EuClassOfBand(4), EuClass.Route, w, "EuClassOfBand(double-black band)");
            // The edges: every class maps back to the band it came from, for every band and a dense grade sweep.
            int crossed = 0;
            for (int band = 0; band <= 4; band++)
                for (int k = -10; k <= 1000; k++)
                    if (DisplayUnits.BandOfEuClass(DisplayUnits.EuClassOf(band, k * 0.1f)) != band)
                        crossed++;
            Eq(crossed, 0, w, "classes that cross a band boundary (band 0..4, grade -1 .. 100 %)");
            Eq((int)DisplayUnits.BandOfEuClass(EuClass.Green) * 10 + DisplayUnits.BandOfEuClass(EuClass.Blue), 11, w, "green and blue are one band");
            // The live readout's hysteresis (0.5 %) at the green / blue boundary.
            Eq(DisplayUnits.EuClassShown(1, 15.4f, EuClass.Green, 0.5f), EuClass.Green, w, "EuClassShown(15.4 %, shown green) keeps green");
            Eq(DisplayUnits.EuClassShown(1, 15.5f, EuClass.Green, 0.5f), EuClass.Blue, w, "EuClassShown(15.5 %, shown green) moves to blue");
            Eq(DisplayUnits.EuClassShown(1, 14.6f, EuClass.Blue, 0.5f), EuClass.Blue, w, "EuClassShown(14.6 %, shown blue) keeps blue");
            Eq(DisplayUnits.EuClassShown(1, 14.4f, EuClass.Blue, 0.5f), EuClass.Green, w, "EuClassShown(14.4 %, shown blue) moves to green");
            Eq(DisplayUnits.EuClassShown(2, 15f, EuClass.Green, 0.5f), EuClass.Red, w, "EuClassShown(blue band) ignores the shown class");
            Eq(DisplayUnits.EuClassShown(1, 10f, EuClass.Black, 0.5f), EuClass.Green, w, "EuClassShown(green band, shown black) resets");
            // The piste map styles.
            Eq(PisteMapRules.StyleOf(1, GradeScheme.European, 10f), PisteStyle.Green, w, "StyleOf(green band, 10 %, European)");
            Eq(PisteMapRules.StyleOf(1, GradeScheme.European, 20f), PisteStyle.Blue, w, "StyleOf(green band, 20 %, European)");
            Eq(PisteMapRules.StyleOf(2, GradeScheme.European, 30f), PisteStyle.Red, w, "StyleOf(blue band, European)");
            Eq(PisteMapRules.StyleOf(4, GradeScheme.European, 70f), PisteStyle.Route, w, "StyleOf(double-black band, European)");
            Eq(PisteMapRules.StyleOf(-1, GradeScheme.European, 20f), PisteStyle.Pending, w, "StyleOf(pending, European)");
            Eq(PisteMapRules.StyleOf(2, GradeScheme.NorthAmerican, 30f), PisteStyle.Blue, w, "StyleOf(blue band, North American)");
            Eq(PisteMapRules.StyleOfBand(1, GradeScheme.European), PisteStyle.Blue, w, "StyleOfBand(green band, European)");
            Eq(DisplayUnits.EuClassKey(EuClass.Route), "SkiResort.EuClass[Route]", w, "EuClassKey(Route)");
            // The hub's counts: the aggregate counts the European greens inside the Green band only.
            var runs = new System.Collections.Generic.List<SkiResortAggregate.RunIn>
            {
                new SkiResortAggregate.RunIn { Resort = 0, RateState = 1, Band = 1, EuGreen = true },
                new SkiResortAggregate.RunIn { Resort = 0, RateState = 1, Band = 1, EuGreen = false },
                new SkiResortAggregate.RunIn { Resort = 0, RateState = 1, Band = 2, EuGreen = true },   // never counted outside Green
                new SkiResortAggregate.RunIn { Resort = 0, RateState = 0, Band = 1, EuGreen = true },   // pending: not rated
            };
            var agg = new SkiResortAggregate.Agg[2];
            SkiResortAggregate.Aggregate(runs, null, null, 1, agg);
            Eq(agg[0].Green * 10 + agg[0].EuGreen, 21, w, "Aggregate: Green 2, of them EuGreen 1");
            Eq((int)EuClass.Flat * 100000 + (int)EuClass.Green * 10000 + (int)EuClass.Blue * 1000 + (int)EuClass.Red * 100 + (int)EuClass.Black * 10 + (int)EuClass.Route,
                12345, w, "EuClass values 0..5 (units.ts EU_CLASS)");
        }

        private static void SectionCatTrack()
        {
            const string w = "DU-6 cat track slope";
            // The Options default (14 deg) is exactly the 1.0.0 limit, 25 % grade.
            Eq(DisplayUnits.CatTrackMaxGrade(14f), 0.25f, w, "CatTrackMaxGrade(14)");
            Eq(DisplayUnits.CatTrackMaxGrade(20f), 0.25f, w, "CatTrackMaxGrade(20)");
            Eq(DisplayUnits.CatTrackMaxGrade(float.NaN), 0.25f, w, "CatTrackMaxGrade(NaN)");
            Check(Near(DisplayUnits.CatTrackMaxGrade(2f), 0.05241, 0.00001), w, "CatTrackMaxGrade(2) not clamped to 3 deg (0.05241)");
            Check(Near(DisplayUnits.CatTrackMaxGrade(8.5f), 0.14945, 0.00001), w, "CatTrackMaxGrade(8.5) != 0.14945");
            // 1.0.0 settings (percent) migrate to the 0.5 deg step
            Eq(DisplayUnits.LegacyPctToDeg(25), 14f, w, "LegacyPctToDeg(25)");
            Eq(DisplayUnits.LegacyPctToDeg(30), 14f, w, "LegacyPctToDeg(30)");
            Eq(DisplayUnits.LegacyPctToDeg(20), 11.5f, w, "LegacyPctToDeg(20)");   // 11.31 deg
            Eq(DisplayUnits.LegacyPctToDeg(15), 8.5f, w, "LegacyPctToDeg(15)");    // 8.53 deg
            Eq(DisplayUnits.LegacyPctToDeg(10), 5.5f, w, "LegacyPctToDeg(10)");    // 5.71 deg
            Eq(DisplayUnits.LegacyPctToDeg(5), 3f, w, "LegacyPctToDeg(5)");        // 2.86 deg
        }

        private static void SectionOptions()
        {
            const string w = "DU-7 unit options";
            // Each category on its own: Auto follows the game's setting, Metric / Imperial override it (display only).
            Eq(DisplayUnits.ResolveImperial(UnitOption.Auto, true), true, w, "Lengths Auto + game imperial");
            Eq(DisplayUnits.ResolveImperial(UnitOption.Auto, false), false, w, "Lengths Auto + game metric");
            Eq(DisplayUnits.ResolveImperial(UnitOption.Metric, true), false, w, "Metric wins over the game's imperial");
            Eq(DisplayUnits.ResolveImperial(UnitOption.Imperial, false), true, w, "Imperial wins over the game's metric");
            Eq(DisplayUnits.ResolveTemperature(UnitOption.Auto, 2), TempUnit.Kelvin, w, "Weather Auto keeps the game's Kelvin");
            Eq(DisplayUnits.ResolveTemperature(UnitOption.Auto, 1), TempUnit.Fahrenheit, w, "Weather Auto + game °F");
            Eq(DisplayUnits.ResolveTemperature(UnitOption.Auto, 7), TempUnit.Celsius, w, "Weather Auto + unknown game value");
            Eq(DisplayUnits.ResolveTemperature(UnitOption.Metric, 1), TempUnit.Celsius, w, "Weather Metric over the game's °F");
            Eq(DisplayUnits.ResolveTemperature(UnitOption.Imperial, 2), TempUnit.Fahrenheit, w, "Weather Imperial over the game's K");
            Check(Near(DisplayUnits.Temperature(-5f, TempUnit.Fahrenheit), 23.0, 0.001), w, "Temperature(-5 °C, °F) != 23");
            Check(Near(DisplayUnits.Temperature(-5f, TempUnit.Kelvin), 268.16, 0.001), w, "Temperature(-5 °C, K) != 268.16");
            Eq(DisplayUnits.GradePct(0.25f), 25, w, "GradePct(0.25)");
            Eq(DisplayUnits.GradePct(0.245f), 25, w, "GradePct(0.245) half away from zero");
            // Mixed cases, packed for the hub (src/hub/units.ts unpackUnits and test-hub-rules.js T60.12 read the same ints):
            // a Canadian setup: North American grading, metric lengths, °C, degrees (game imperial, Options set metric)
            bool len = DisplayUnits.ResolveImperial(UnitOption.Metric, true), dep = DisplayUnits.ResolveImperial(UnitOption.Metric, true);
            Eq(DisplayUnits.ResolveScheme(GradeSchemeOption.NorthAmerican, "European"), GradeScheme.NorthAmerican, w, "Canadian: NA grading");
            Eq(DisplayUnits.PackUnits(len, dep, DisplayUnits.ResolveTemperature(UnitOption.Metric, 1), false), 0, w, "Canadian: metric + °C + degrees packs 0");
            // European grading on a European theme with everything imperial (game metric)
            Eq(DisplayUnits.ResolveScheme(GradeSchemeOption.Auto, "European"), GradeScheme.European, w, "EU theme: European grading");
            Eq(DisplayUnits.PackUnits(DisplayUnits.ResolveImperial(UnitOption.Imperial, false), DisplayUnits.ResolveImperial(UnitOption.Imperial, false),
                DisplayUnits.ResolveTemperature(UnitOption.Imperial, 0), false), 7, w, "EU + imperial + °F packs 7");
            Eq(DisplayUnits.PackUnits(true, true, TempUnit.Fahrenheit, true), 23, w, "all imperial + percent packs 23");
            Eq(DisplayUnits.PackUnits(false, false, TempUnit.Kelvin, false), 8, w, "game Kelvin packs 8");
            Eq(DisplayUnits.PackUnits(false, true, TempUnit.Fahrenheit, false), 6, w, "metric lengths + inches + °F packs 6");
            // Imperial lengths keep vanilla's thresholds in the text forms
            Eq(DisplayUnits.HeightText(350f, DisplayUnits.ResolveImperial(UnitOption.Imperial, false)), "1,148 ft", w, "imperial height over a metric game");
            Eq(DisplayUnits.LengthText(1600f, DisplayUnits.ResolveImperial(UnitOption.Metric, true)), "1.6 km", w, "metric length over an imperial game");
            // The saved Options values never move (settings file)
            Eq((int)UnitOption.Auto * 100 + (int)UnitOption.Metric * 10 + (int)UnitOption.Imperial, 12, w, "UnitOption values 0 / 1 / 2");
            Eq((int)SlopeOption.Degrees * 10 + (int)SlopeOption.Percent, 1, w, "SlopeOption values 0 / 1");
            Eq((int)TempUnit.Celsius * 100 + (int)TempUnit.Fahrenheit * 10 + (int)TempUnit.Kelvin, 12, w, "TempUnit values 0 / 1 / 2 (vanilla's)");
        }
    }
}
