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
            SectionF();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);

        private static bool Near(double a, double b, double eps) => Math.Abs(a - b) <= eps;

        private static void SectionA()
        {
            const string W = "RunLabel A";
            Check(RunLabelRules.ClearFraction(0, 5) == -1f, W, "first clearance sample is one edge of the text");
            Check(RunLabelRules.ClearFraction(2, 5) == 0f, W, "middle clearance sample is the center");
            Check(RunLabelRules.ClearFraction(4, 5) == 1f, W, "last clearance sample is the other edge");
            Check(RunLabelRules.ClearFraction(0, 1) == 0f, W, "a single clearance sample is the center");
            Check(RunLabelRules.ClearAlong >= 3 && RunLabelRules.ClearAcross >= 3, W, "clearance samples reach both edges and the middle");

            // A 300 x 40 run along x: the axis is x (either sign is one axis). A 200 x 100 one is still clear; a square has no direction.
            float2[] Rect(float l, float w) => new[] { new float2(-l / 2, -w / 2), new float2(l / 2, -w / 2), new float2(l / 2, w / 2), new float2(-l / 2, w / 2) };
            Check(RunLabelRules.LongAxis(Rect(300f, 40f), out float2 ax) && Near(Math.Abs(ax.x), 1.0, 1e-4), W, "a long run's axis is its length");
            Check(RunLabelRules.LongAxis(Rect(200f, 100f), out _), W, "2 to 1 has a direction");
            Check(!RunLabelRules.LongAxis(Rect(100f, 100f), out _), W, "a square has no direction");
            Check(!RunLabelRules.LongAxis(new[] { new float2(0f, 0f), new float2(1f, 0f) }, out _), W, "two nodes have no direction");
            Check(!RunLabelRules.LongAxis(null, out _), W, "no outline has no direction");
            Check(!RunLabelRules.LongAxis(new[] { new float2(float.NaN, 0f), new float2(1f, 0f), new float2(1f, 1f) }, out _), W, "NaN has no direction");
            // The same 300 x 40 run turned 45 degrees: nodes hand-rotated, axis (0.7071, 0.7071) either sign.
            const float C = 0.70710678f;
            float2[] turned = new[] { new float2((-150f + 20f) * C, (-150f - 20f) * C), new float2((150f + 20f) * C, (150f - 20f) * C), new float2((150f - 20f) * C, (150f + 20f) * C), new float2((-150f - 20f) * C, (-150f + 20f) * C) };
            Check(RunLabelRules.LongAxis(turned, out float2 ad) && Near(Math.Abs(ad.x), C, 1e-3) && Near(Math.Abs(ad.y), C, 1e-3) && ad.x * ad.y > 0f, W, "a turned run's axis turns with it");
            // Extra nodes along one side must not pull the axis (edge-length weights).
            float2[] dense = new[] { new float2(-150f, -20f), new float2(-100f, -20f), new float2(-50f, -20f), new float2(0f, -20f), new float2(50f, -20f), new float2(100f, -20f), new float2(150f, -20f), new float2(150f, 20f), new float2(-150f, 20f) };
            Check(RunLabelRules.LongAxis(dense, out float2 ae) && Near(Math.Abs(ae.x), 1.0, 1e-4), W, "node spacing does not pull the axis");

            float2 right = new float2(1f, 0f);
            Check(!RunLabelRules.ReadsFlipped(new float2(1f, 0f), right, false), W, "axis along camera right reads as is");
            Check(RunLabelRules.ReadsFlipped(new float2(-1f, 0f), right, false), W, "axis against camera right turns over");
            Check(RunLabelRules.ReadsFlipped(new float2(-0.05f, 0.9987f), right, true), W, "just past square keeps the flipped state");
            Check(!RunLabelRules.ReadsFlipped(new float2(-0.05f, 0.9987f), right, false), W, "just past square keeps the unflipped state");
            Check(!RunLabelRules.ReadsFlipped(new float2(0.2f, 0.98f), right, true), W, "well past square clears the flip");

            RunLabelRules.TextBasis(new float2(1f, 0f), 0f, 0f, out float3 bx, out float3 by, out float3 bn);
            Check(Near(bx.x, 1.0, 1e-5) && Near(by.z, 1.0, 1e-5) && Near(bn.y, 1.0, 1e-5), W, "flat ground: the text lies flat, x right, y away");
            RunLabelRules.TextBasis(new float2(1f, 0f), 1f, 0f, out float3 sx, out float3 sy, out float3 sn);
            Check(Near(sx.x, 0.7071, 1e-3) && Near(sx.y, 0.7071, 1e-3) && Near(sy.z, 1.0, 1e-5) && Near(sn.x, -0.7071, 1e-3) && Near(sn.y, 0.7071, 1e-3), W, "45 degrees along the text tilts it and stays up");
            RunLabelRules.TextBasis(new float2(0.6f, 0.8f), 0.4f, -0.7f, out float3 ox, out float3 oy, out float3 on);
            Check(Near(math.dot(ox, oy), 0.0, 1e-5) && Near(math.dot(ox, on), 0.0, 1e-5) && Near(math.dot(oy, on), 0.0, 1e-5) && on.y > 0f, W, "any slope: orthonormal, normal up");
            RunLabelRules.TextBasis(new float2(1f, 0f), 50f, 0f, out float3 cx, out _, out _);
            Check(Near(cx.y / cx.x, RunLabelRules.MaxSlope, 1e-4), W, "a cliff clamps the tilt");
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

        private static void SectionF()
        {
            const string W = "RunLabel F";
            // A U of 20 m wide legs: its center (0, 50) lies in the notch, off the run. The label moves onto the bar, y = 10, along x.
            var u = new[] { new float2(-60f, 0f), new float2(60f, 0f), new float2(60f, 200f), new float2(40f, 200f), new float2(40f, 20f), new float2(-40f, 20f), new float2(-40f, 200f), new float2(-60f, 200f) };
            Check(RunLabelRules.CurvedAnchor(u, new float2(0f, 50f), false, new float2(1f, 0f), out float2 at, out float2 dir) && Near(at.x, 0.0, 1e-3) && Near(at.y, 10.0, 1e-3) && Near(Math.Abs(dir.x), 1.0, 1e-3), W, "a center off a U moves between the sides of the bar, along the bar");
            // The same U the other way around the ring gives the same spot.
            var back = new float2[u.Length];
            for (int i = 0; i < u.Length; i++)
                back[i] = u[u.Length - 1 - i];
            Check(RunLabelRules.CurvedAnchor(back, new float2(0f, 50f), false, new float2(1f, 0f), out float2 bt, out _) && Near(bt.y, 10.0, 1e-3), W, "ring direction does not matter");

            // A straight 300 x 40 run keeps its overall axis and its center.
            var straight = new[] { new float2(-150f, -20f), new float2(150f, -20f), new float2(150f, 20f), new float2(-150f, 20f) };
            Check(!RunLabelRules.CurvedAnchor(straight, new float2(0f, 0f), true, new float2(1f, 0f), out _, out _), W, "a straight run stays as it is");
            // The same run with an overall axis 45 degrees off the ground under the label counts as curved.
            Check(RunLabelRules.CurvedAnchor(straight, new float2(0f, 0f), true, new float2(0.7071f, 0.7071f), out _, out float2 sd) && Near(Math.Abs(sd.x), 1.0, 1e-3), W, "axis 45 degrees off the local direction: the local one wins");
            // A 100 x 100 bowl is as wide as it is long: it keeps facing the camera.
            var bowl = new[] { new float2(0f, 0f), new float2(100f, 0f), new float2(100f, 100f), new float2(0f, 100f) };
            Check(!RunLabelRules.CurvedAnchor(bowl, new float2(50f, 50f), false, new float2(1f, 0f), out _, out _), W, "a bowl has no local direction");
            Check(!RunLabelRules.CurvedAnchor(null, new float2(0f, 0f), false, new float2(1f, 0f), out _, out _), W, "no outline");
            Check(!RunLabelRules.CurvedAnchor(straight, new float2(float.NaN, 0f), false, new float2(1f, 0f), out _, out _), W, "NaN center");
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
