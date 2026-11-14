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
            // vanilla's "length" thresholds: m under 1,000 m, else km; yd under 1,609 m, else mi
            Eq(DisplayUnits.LengthText(376f, false), "376 m", w, "LengthText(376, metric)");
            Eq(DisplayUnits.LengthText(999.4f, false), "999 m", w, "LengthText(999.4, metric)");
            Eq(DisplayUnits.LengthText(1000f, false), "1.0 km", w, "LengthText(1000, metric)");
            Eq(DisplayUnits.LengthText(12345f, false), "12.3 km", w, "LengthText(12345, metric)");
            Eq(DisplayUnits.LengthText(100f, true), "109 yd", w, "LengthText(100, imperial)");
            Eq(DisplayUnits.LengthText(1600f, true), "1,750 yd", w, "LengthText(1600, imperial)");
            Eq(DisplayUnits.LengthText(1609f, true), "1.0 mi", w, "LengthText(1609, imperial)");
            Eq(DisplayUnits.LengthText(5000f, true), "3.1 mi", w, "LengthText(5000, imperial)");
            Eq(DisplayUnits.LengthText(float.NaN, false), "–", w, "LengthText(NaN)");
            Eq(DisplayUnits.AreaHaText(3.24f, false), "3.2 ha", w, "AreaHaText(3.24, metric)");
            Eq(DisplayUnits.AreaHaText(1f, true), "2.5 acres", w, "AreaHaText(1, imperial)");
            Eq(DisplayUnits.SlopeText(0.25f), "14°", w, "SlopeText(0.25)");
            Eq(DisplayUnits.SlopeText(0.40f), "21.8°", w, "SlopeText(0.40)");
            Eq(DisplayUnits.SlopeText(float.NaN), "–", w, "SlopeText(NaN)");
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
            // In-world colors (labels, piste map): European grading shows the green band as the blue piste, the blue band as the red piste
            RunLabelRules.BandRgb(1, GradeScheme.European, out byte r, out byte g, out byte b);
            Eq(r * 1000000 + g * 1000 + b, 52120220, w, "BandRgb(green band, European) (blue)");
            RunLabelRules.BandRgb(2, GradeScheme.European, out r, out g, out b);
            Eq(r * 1000000 + g * 1000 + b, 214048040, w, "BandRgb(blue band, European) (red)");
            RunLabelRules.BandRgb(3, GradeScheme.European, out r, out g, out b);
            Eq(r * 1000000 + g * 1000 + b, 14014018, w, "BandRgb(black band, European) (unchanged)");
            RunLabelRules.BandRgb(1, GradeScheme.NorthAmerican, out r, out g, out b);
            Eq(r * 1000000 + g * 1000 + b, 76175080, w, "BandRgb(green band, North American) (unchanged)");
            // The values the hub reads (bindings.ts GRADE_SCHEME) and the Options values (saved settings) never move.
            Eq((int)GradeScheme.NorthAmerican * 10 + (int)GradeScheme.European, 1, w, "GradeScheme values 0 / 1");
            Eq((int)GradeSchemeOption.Auto * 100 + (int)GradeSchemeOption.NorthAmerican * 10 + (int)GradeSchemeOption.European, 12, w, "GradeSchemeOption values 0 / 1 / 2");
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
    }
}
