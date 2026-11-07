// Tests for the pure rules in SkiAreaRadiusRules.cs (the ski area radius option, its ring and the site survey).
//   AR-1 clamp and step
//   AR-2 ring points
//   AR-3 survey: flat, slope, skiable vertical, water, playable clip, lines
// Oracles are literal hand values (the comments show the arithmetic).

using System;
using SkiResortMod.Systems;
using R = SkiResortMod.Systems.SkiAreaRadiusRules;

namespace ResortBatch
{
    public static class SkiAreaRadiusTests
    {
        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Clamp();
            Ring();
            Survey();
        }

        private static bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol;

        private static void C(bool ok, string where, string msg) => s_Check(ok, where, msg);

        // AR-1
        private static void Clamp()
        {
            const string W = "radius AR-1 clamp";
            C(R.DefaultKm == 5f && R.MinKm == 2f && R.MaxKm == 10f && R.StepKm == 0.5f, W, "default 5 km, 2-10 km, step 0.5");
            C(R.ClampKm(5f) == 5f, W, "5 stays 5");
            C(R.ClampKm(1f) == 2f && R.ClampKm(-3f) == 2f, W, "below the minimum -> 2");
            C(R.ClampKm(25f) == 10f, W, "above the maximum -> 10");
            C(R.ClampKm(float.NaN) == 5f && R.ClampKm(float.PositiveInfinity) == 5f, W, "not finite -> the default");
            C(R.ClampKm(6.3f) == 6.5f && R.ClampKm(6.2f) == 6f && R.ClampKm(6.25f) == 6.5f, W, "rounded to the nearest 0.5 (half away from zero)");
            C(R.MetersOf(7.5f) == 7500f && R.MetersOf(float.NaN) == 5000f, W, "meters = km x 1000");
        }

        // AR-2
        private static void Ring()
        {
            const string W = "radius AR-2 ring";
            int n = R.RingSegments;
            R.RingPoint(100f, 200f, 5000f, 0, n, out float x0, out float z0);
            C(Near(x0, 5100, 1e-3) && Near(z0, 200, 1e-3), W, "point 0 = center + (R, 0)");
            R.RingPoint(100f, 200f, 5000f, n / 4, n, out float x1, out float z1);
            C(Near(x1, 100, 0.01) && Near(z1, 5200, 0.01), W, "a quarter turn = center + (0, R)");
            R.RingPoint(100f, 200f, 5000f, n, n, out float xn, out float zn);
            C(xn == x0 && zn == z0, W, "i = n closes the ring at point 0");
            double sag = 10000.0 * (1.0 - Math.Cos(Math.PI / n));
            C(sag < 2.0, W, $"chord sag at 10 km under 2 m ({sag:F2} m)");
        }

        // AR-3
        private static void Survey()
        {
            const string W = "radius AR-3 survey";
            const float big = 1e6f;
            // Flat at 800 m, snow line 1000: nothing above, vertical 0.
            var flat = R.Survey((x, z) => 800f, 0f, 0f, 5000f, -big, -big, big, big, 1000f, 900f);
            C(flat.Samples > 0 && flat.Samples == flat.InDisc, W, $"flat: every disc sample dry ({flat.Samples})");
            // A 72 x 72 grid holds ~pi/4 of 5,184 cells in the disc = ~4,072.
            C(flat.InDisc > 3900 && flat.InDisc < 4250, W, $"flat: ~pi/4 of 72 x 72 cells in the disc ({flat.InDisc})");
            C(flat.MinM == 800f && flat.MaxM == 800f && flat.VerticalM == 0f && flat.PctAboveSnow == 0, W, "flat: 800 to 800 m, 0 vertical, 0 %");

            // A slope h = 1000 + 0.1 x: x in about -5000..5000 -> 500..1500, half the disc above 1000.
            var slope = R.Survey((x, z) => 1000f + 0.1f * x, 0f, 0f, 5000f, -big, -big, big, big, 1000f, 950f);
            C(slope.MinM > 500f && slope.MinM < 510f && slope.MaxM > 1490f && slope.MaxM < 1500f, W,
              $"slope: ground ~500 to ~1500 m ({slope.MinM:F1} to {slope.MaxM:F1})");
            C(slope.PctAboveSnow == 50, W, $"slope: half the disc above the snow line ({slope.PctAboveSnow} %)");
            C(slope.PctAboveGuns > 50 && slope.PctAboveGuns < 60, W, $"slope: a guns line 50 m lower adds ~6 % ({slope.PctAboveGuns} %)");

            // Skiable vertical: from the snow line (1000) to the top (~1495).
            C(Near(slope.SkiableVerticalM, slope.MaxM - 1000f, 1e-3) && slope.SkiableVerticalM > 480f, W, $"slope: skiable vertical = top - snow line ({slope.SkiableVerticalM:F0} m)");
            C(flat.SkiableVerticalM == 0f, W, "flat below the snow line: skiable vertical 0");
            var high = R.Survey((x, z) => 1000f + 0.1f * x, 0f, 0f, 5000f, -big, -big, big, big, 200f, 100f);
            C(Near(high.SkiableVerticalM, high.VerticalM, 1e-3), W, "all ground above the snow line: skiable vertical = vertical");
            // Water: the west half is a lake (NaN): only the east half counts.
            var lake = R.Survey((x, z) => x < 0f ? float.NaN : 1000f + 0.1f * x, 0f, 0f, 5000f, -big, -big, big, big, 1000f, 950f);
            C(lake.Samples * 2 > lake.InDisc - 80 && lake.Samples * 2 < lake.InDisc + 80, W, $"lake: about half dry ({lake.Samples} of {lake.InDisc})");
            C(lake.MinM >= 1000f && lake.PctAboveSnow == 100, W, "lake: the lowest dry ground is the shore, 100 % above");

            // All water: nothing to show.
            var wet = R.Survey((x, z) => float.NaN, 0f, 0f, 5000f, -big, -big, big, big, 1000f, 950f);
            C(wet.Samples == 0 && wet.InDisc > 0 && float.IsNaN(wet.MinM) && wet.VerticalM == 0f && wet.PctAboveSnow == 0, W, "all water: no samples, NaN ground");

            // Playable clip: the map ends at x = 0. The survey samples only the east half at all.
            var edge = R.Survey((x, z) => 1000f, 0f, 0f, 5000f, 0f, -big, big, big, 1000f, 950f);
            C(edge.InDisc * 2 > flat.InDisc - 80 && edge.InDisc * 2 < flat.InDisc + 80, W, $"clip: half the disc on the map ({edge.InDisc})");

            // Same sample count at every radius (cell = 2R / side).
            var far = R.Survey((x, z) => 800f, 0f, 0f, 10000f, -big, -big, big, big, 1000f, 900f);
            C(far.InDisc == flat.InDisc, W, "10 km samples as many cells as 5 km");

            // NaN snow line: nothing counts above it. Bad input gives an empty result.
            var noLine = R.Survey((x, z) => 800f, 0f, 0f, 5000f, -big, -big, big, big, float.NaN, float.NaN);
            C(noLine.Samples > 0 && noLine.PctAboveSnow == 0 && noLine.PctAboveGuns == 0 && noLine.SkiableVerticalM == 0f, W, "NaN lines: 0 % above, 0 skiable");
            C(R.Survey(null, 0f, 0f, 5000f, -big, -big, big, big, 1000f, 900f).InDisc == 0 &&
              R.Survey((x, z) => 1f, 0f, 0f, 0f, -big, -big, big, big, 1000f, 900f).InDisc == 0, W, "null sampler or zero radius: empty");
        }
    }
}
