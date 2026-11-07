// The lift tree corridor (DECISIONS 601 e, owner hand-test note 8): the pure LiftTreeCorridor rule the lift-line tool applies to every
// tree near a built section. Where-tags `trees/TC-n`.
// Pinned:
// - the corridor widths per lift type from the catalog (FG 11.4, DQ 13.6, gondola 14.2, tram 20.8 m).
// - which trees fall and which stay beside a straight FG line. The trunk edge is exact at the half width. A big crown reaching into
//   the carrier envelope falls. A small crown just outside stays.
// - round ends at a ground end.
// - the inside of a bent line and a station stub segment.
// - a tree beyond the far end of a long tram span stays.
// - degenerate inputs fell nothing.

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    internal static class TreeCorridorTests
    {
        private static Func<bool, string, string, bool> s_Check;

        private static bool Check(bool ok, string where, string msg) => s_Check(ok, where, msg);

        private static void Near(double want, double got, double tol, string where, string what)
        {
            Check(Math.Abs(want - got) <= tol, where, what + ": expected " + want + ", got " + got);
        }

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Tc1Widths();
            Tc2StraightFixedGrip();
            Tc3Ends();
            Tc4BentLineAndStub();
            Tc5Tram();
            Tc6Degenerate();
            s_Check = null;
        }

        private static LiftTypeDef Type(string id)
        {
            for (int i = 0; i < SkiCatalog.Lifts.Count; i++)
            {
                if (SkiCatalog.Lifts[i].Id == id)
                    return SkiCatalog.Lifts[i];
            }
            throw new InvalidOperationException("no lift type " + id);
        }

        private static List<LiftTreeCorridor.Segment> Line(params float[] xz)
        {
            var line = new List<LiftTreeCorridor.Segment>();
            for (int i = 0; i + 3 < xz.Length; i += 2)
                line.Add(new LiftTreeCorridor.Segment(new float2(xz[i], xz[i + 1]), new float2(xz[i + 2], xz[i + 3])));
            return line;
        }

        private static void Tc1Widths()
        {
            const string W = "trees/TC-1";
            Near(11.4, LiftTreeCorridor.WidthM(Type("fixedGrip")), 1e-4, W, "fixed grip corridor (2 x (1.8 + 0.9 + 3))");
            Near(13.6, LiftTreeCorridor.WidthM(Type("detachable")), 1e-4, W, "detachable corridor (2 x (2.6 + 1.2 + 3))");
            Near(14.2, LiftTreeCorridor.WidthM(Type("gondola")), 1e-4, W, "gondola corridor (2 x (3.0 + 1.1 + 3))");
            Near(20.8, LiftTreeCorridor.WidthM(Type("tram")), 1e-4, W, "tram corridor (2 x (5.5 + 1.9 + 3))");
            for (int i = 0; i < SkiCatalog.Lifts.Count; i++)
            {
                LiftTypeDef d = SkiCatalog.Lifts[i];
                Check(d.CarrierHalfWidthM > 0f && d.CarrierHalfWidthM < d.GaugeHalfM + 1f, W,
                      d.Id + ": carrier half width " + d.CarrierHalfWidthM + " is positive and near its gauge " + d.GaugeHalfM);
                Check(LiftTreeCorridor.QueryReachM(d) >= LiftTreeCorridor.HalfWidthM(d), W, d.Id + ": the search reaches the trunk test");
                Check(LiftTreeCorridor.QueryReachM(d) >= LiftTreeCorridor.EnvelopeHalfM(d) + LiftTreeCorridor.MaxCrownM, W,
                      d.Id + ": the search reaches the widest crown test");
            }
        }

        private static void Tc2StraightFixedGrip()
        {
            const string W = "trees/TC-2";
            LiftTypeDef fg = Type("fixedGrip");
            var line = Line(0f, 0f, 0f, 400f);   // a 400 m FG section along +z. Half width 5.7, envelope 2.7
            // trunk test (small crown 2 m)
            Check(LiftTreeCorridor.Fells(new float2(0f, 200f), 2f, line, fg), W, "a tree under the rope falls");
            Check(LiftTreeCorridor.Fells(new float2(-1.8f, 120f), 2f, line, fg), W, "a tree on a rope lane falls");
            Check(LiftTreeCorridor.Fells(new float2(5.7f, 200f), 2f, line, fg), W, "a trunk at the half width (5.7 m) falls (inclusive)");
            Check(!LiftTreeCorridor.Fells(new float2(5.75f, 200f), 2f, line, fg), W, "a trunk 5.75 m out with a 2 m crown stays");
            Check(!LiftTreeCorridor.Fells(new float2(-12f, 50f), 3f, line, fg), W, "a tree 12 m out stays");
            // crown test: a big crown reaching into the carrier envelope (2.7 + 0.5 = 3.2 m) falls. One just short stays
            Check(LiftTreeCorridor.Fells(new float2(9f, 200f), 6f, line, fg), W, "a 6 m crown 9 m out reaches 3.0 m: falls");
            Check(!LiftTreeCorridor.Fells(new float2(9f, 200f), 5.7f, line, fg), W, "a 5.7 m crown 9 m out reaches 3.3 m: stays");
            Check(!LiftTreeCorridor.Fells(new float2(14f, 200f), 50f, line, fg), W,
                  "a crown is believed at most MaxCrownM (8 m): 14 m out reaches 6.0 m, stays");
            // the count over a grid stand: every 2 m from -20..20 x 0..400 -> the trunks within 5.7 m (x in -4..4, 5 columns) all fall
            int fell = 0, stay = 0;
            for (int x = -20; x <= 20; x += 2)
            {
                for (int z = 0; z <= 400; z += 2)
                {
                    if (LiftTreeCorridor.Fells(new float2(x, z), 2.5f, line, fg))
                        fell++;
                    else
                        stay++;
                }
            }
            // columns -4..4 (5) over the 201 rows + the round ends (z -2 / 402 are outside the stand)
            Check(fell == 5 * 201, W, "grid stand: " + fell + " fell, want " + (5 * 201) + " (5 columns within 5.7 m x 201 rows)");
            Check(stay == 21 * 201 - fell, W, "grid stand: the rest stay (" + stay + ")");
        }

        private static void Tc3Ends()
        {
            const string W = "trees/TC-3";
            LiftTypeDef fg = Type("fixedGrip");
            var line = Line(0f, 0f, 0f, 400f);
            Check(LiftTreeCorridor.Fells(new float2(0f, 405f), 2f, line, fg), W, "5 m past a ground end, on the axis: falls (round end)");
            Check(LiftTreeCorridor.Fells(new float2(3f, -4f), 2f, line, fg), W, "5 m from the base end, off the axis: falls");
            Check(!LiftTreeCorridor.Fells(new float2(0f, 406f), 2f, line, fg), W, "6 m past the end: stays");
            Check(!LiftTreeCorridor.Fells(new float2(4.5f, 404.5f), 2f, line, fg), W, "6.4 m diagonally past the end: stays");
        }

        private static void Tc4BentLineAndStub()
        {
            const string W = "trees/TC-4";
            LiftTypeDef dq = Type("detachable");   // half width 6.8, envelope 3.8
            // a 90 deg bend at a tower (a tool line never bends this hard, but the rule must still hold). The line runs
            // base (0,0) -> tower (0,200) -> top (200,200). A station stub edge runs 12 m on from the top node
            var line = Line(0f, 0f, 0f, 200f, 200f, 200f);
            line.Add(new LiftTreeCorridor.Segment(new float2(200f, 200f), new float2(212f, 200f)));
            Check(LiftTreeCorridor.Fells(new float2(5f, 195f), 2f, line, dq), W, "inside the bend, 5 m from both legs: falls");
            Check(!LiftTreeCorridor.Fells(new float2(-6f, 206f), 2f, line, dq), W, "outside the bend corner, 8.5 m from the tower: stays");
            Check(LiftTreeCorridor.Fells(new float2(-4f, 204f), 2f, line, dq), W, "outside the bend corner, 5.7 m from the tower: falls");
            Check(LiftTreeCorridor.Fells(new float2(210f, 205f), 2f, line, dq), W, "beside the station stub edge: falls");
            Check(!LiftTreeCorridor.Fells(new float2(220f, 200f), 2f, line, dq), W, "8 m past the stub's end: stays");
        }

        private static void Tc5Tram()
        {
            const string W = "trees/TC-5";
            LiftTypeDef tram = Type("tram");   // half width 10.4
            var line = Line(0f, 0f, 700f, 700f);   // a 990 m diagonal span
            float2 mid = new float2(350f, 350f);
            float2 side = math.normalize(new float2(1f, -1f));
            Check(LiftTreeCorridor.Fells(mid + side * 10f, 2f, line, tram), W, "10 m beside a diagonal tram span: falls");
            Check(!LiftTreeCorridor.Fells(mid + side * 11f, 2f, line, tram), W, "11 m beside it: stays");
            Check(!LiftTreeCorridor.Fells(new float2(720f, 720f), 2f, line, tram), W, "28 m past the top along the axis: stays");
        }

        private static void Tc6Degenerate()
        {
            const string W = "trees/TC-6";
            LiftTypeDef fg = Type("fixedGrip");
            Check(!LiftTreeCorridor.Fells(new float2(0f, 0f), 2f, new List<LiftTreeCorridor.Segment>(), fg), W, "no line fells nothing");
            Check(!LiftTreeCorridor.Fells(float.NaN, 2f, 2.7f), W, "a NaN distance fells nothing");
            Check(LiftTreeCorridor.Fells(new float2(1f, 1f), -3f, Line(0f, 0f, 0f, 0f), fg), W,
                  "a zero-length line is its point; a negative crown reads 0");
        }
    }
}
