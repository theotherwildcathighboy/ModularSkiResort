// Tests for RunLabelRules (the run-name labels' pure rules). Oracles are literal hand values from the RULES header of
// RunLabelRules.cs and the piste map's color table (SkiPisteMapSystem.s_Fill), never a second run of the same code.

using System;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace ResortBatch
{
    public static class RunLabelTests
    {
        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            SectionA();
            SectionB();
            SectionC();
            SectionD();
            SectionE();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);

        private static bool Near(double a, double b, double eps) => Math.Abs(a - b) <= eps;

        private static void SectionA()
        {
            const string W = "RunLabel A";
            Check(RunLabelRules.FadeAlpha(0f) == 0f, W, "zoom 0 (no camera) is hidden");
            Check(RunLabelRules.FadeAlpha(-5f) == 0f, W, "negative zoom is hidden");
            Check(RunLabelRules.FadeAlpha(float.NaN) == 0f, W, "NaN zoom is hidden");
            Check(RunLabelRules.FadeAlpha(float.PositiveInfinity) == 0f, W, "infinite zoom is hidden");
            Check(RunLabelRules.FadeAlpha(100f) == 0f, W, "100 m is the hidden edge");
            Check(Near(RunLabelRules.FadeAlpha(175f), 0.5, 1e-6), W, "175 m is half faded in");
            Check(RunLabelRules.FadeAlpha(250f) == 1f, W, "250 m is full");
            Check(RunLabelRules.FadeAlpha(800f) == 1f, W, "800 m is full");
            Check(RunLabelRules.FadeAlpha(1500f) == 1f, W, "1500 m is still full");
            Check(Near(RunLabelRules.FadeAlpha(2250f), 0.5, 1e-6), W, "2250 m is half faded out");
            Check(RunLabelRules.FadeAlpha(3000f) == 0f, W, "3000 m is the far edge");
            Check(RunLabelRules.FadeAlpha(4500f) == 0f, W, "4500 m (the piste map's widest) has no labels");
        }

        private static void SectionB()
        {
            const string W = "RunLabel B";
            Check(RunLabelRules.AlphaStep(0f) == 0, W, "alpha 0 = step 0");
            Check(RunLabelRules.AlphaStep(-1f) == 0, W, "negative alpha = step 0");
            Check(RunLabelRules.AlphaStep(float.NaN) == 0, W, "NaN alpha = step 0");
            Check(RunLabelRules.AlphaStep(0.01f) == 1, W, "a sliver of alpha is step 1, not 0");
            Check(RunLabelRules.AlphaStep(0.06f) == 1, W, "0.06 * 8 = 0.48 rounds to 0 but is lifted to 1");
            Check(RunLabelRules.AlphaStep(0.5f) == 4, W, "0.5 = step 4");
            Check(RunLabelRules.AlphaStep(0.99f) == 8, W, "0.99 * 8 = 7.92 rounds to 8");
            Check(RunLabelRules.AlphaStep(1f) == 8, W, "1 = step 8");
            Check(RunLabelRules.AlphaByte(0) == 0, W, "byte of step 0");
            Check(RunLabelRules.AlphaByte(8) == 255, W, "byte of step 8");
            Check(RunLabelRules.AlphaByte(9) == 255, W, "byte of a step above the top");
            Check(RunLabelRules.AlphaByte(1) == 32, W, "byte of step 1 (31.875 rounds to 32)");
        }

        private static void SectionC()
        {
            const string W = "RunLabel C";
            (int band, byte r, byte g, byte b)[] table =
            {
                (-2, 220, 150, 60), (-1, 255, 255, 255), (0, 170, 180, 190), (1, 76, 175, 80),
                (2, 52, 120, 220), (3, 14, 14, 18), (4, 0, 0, 0), (9, 255, 255, 255),
            };
            foreach (var t in table)
            {
                RunLabelRules.BandRgb(t.band, out byte r, out byte g, out byte b);
                Check(r == t.r && g == t.g && b == t.b, W, $"band {t.band} = ({t.r},{t.g},{t.b}), got ({r},{g},{b})");
            }
        }

        private static void SectionD()
        {
            const string W = "RunLabel D";
            Check(RunLabelRules.CleanText(null) == "", W, "null");
            Check(RunLabelRules.CleanText("") == "", W, "empty");
            Check(RunLabelRules.CleanText("<>") == "", W, "only mark-up characters");
            Check(RunLabelRules.CleanText("Hawk Run") == "Hawk Run", W, "a plain name is kept");
            Check(RunLabelRules.CleanText("<b>Hello</b>  World") == "bHello/b World", W, "mark-up characters out, spaces collapsed");
            Check(RunLabelRules.CleanText("  a \t b\n") == "a b", W, "trim, tab and newline collapse to one space");
            string longName = new string('x', 40);
            Check(RunLabelRules.CleanText(longName) == new string('x', 29) + "...", W, "40 chars cut to 29 + '...' = 32");
            Check(RunLabelRules.CleanText(new string('y', 32)) == new string('y', 32), W, "exactly 32 chars is kept");
        }

        private static void SectionE()
        {
            const string W = "RunLabel E";
            Check(RunLabelRules.Extent(null) == 0f, W, "null outline");
            Check(RunLabelRules.Extent(new float2[0]) == 0f, W, "empty outline");
            var rect = new[] { new float2(0f, 0f), new float2(10f, 0f), new float2(10f, 20f), new float2(0f, 20f) };
            Check(RunLabelRules.Extent(rect) == 20f, W, "10 x 20 rectangle = 20");
            var wide = new[] { new float2(-5f, 3f), new float2(95f, 3f), new float2(95f, 13f) };
            Check(RunLabelRules.Extent(wide) == 100f, W, "100 x 10 triangle = 100");
            Check(RunLabelRules.Extent(new[] { new float2(0f, 0f), new float2(float.NaN, 1f) }) == 0f, W, "a NaN node = 0");

            Check(Near(RunLabelRules.FitScale(2f, 100f, 50f), 0.5, 1e-6), W, "text 100 wide on a 50 m run at scale 2 is capped to 0.5");
            Check(RunLabelRules.FitScale(2f, 10f, 50f) == 2f, W, "a short text keeps its scale");
            Check(RunLabelRules.FitScale(2f, 0f, 50f) == 2f, W, "no text width = unchanged");
            Check(RunLabelRules.FitScale(2f, 100f, 0f) == 2f, W, "no extent = unchanged");

            Check(Near(RunLabelRules.CullRadius(1000f), 3000.0, 1e-3), W, "cull radius at 1000 m zoom = 500 + 2.5 x 1000");
            Check(RunLabelRules.InRange(3000f, 0f, 1000f), W, "exactly on the cull radius is in");
            Check(!RunLabelRules.InRange(3001f, 0f, 1000f), W, "beyond the cull radius is out");
            Check(RunLabelRules.InRange(1800f, 2400f, 1000f), W, "the 3-4-5 case: 3000 m away is in");
        }
    }
}
