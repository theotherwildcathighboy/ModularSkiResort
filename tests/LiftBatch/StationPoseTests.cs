// The station pose of a planned lift (StationAlign.PoseLift, Resort Planner round task R1-c): the base and the top station of a plan's
// lift, posed so the placer can stand them and the lift-line tool can join their stubs. Where-tags `pose/...`, SP-1..SP-7.
// Pinned:
// - For the four lift types and 72 directions at three lengths, StationAlign.Measure reads the pair as aligned both ways (0.5 m /
//   0.5 deg), the subject ahead of the partner.
// - The stub-tip distance is the spot distance minus the two stub offsets (|StubTipLocalZ| of each row).
// - Each line axis runs through its spot: the tram base's origin sits 4 m to the side of its spot, every other origin on it.
// - Each station's line leaves along its local -Z toward the other station (StationAlign.Axis).
// - y stays NaN (the placer resolves it).
// - Spots too close for the two stubs, or not finite, give an invalid pose.
// - The height rule (SP-7, StationAlign.StationY, task R1-e): LowestOfThree is the lowest finite of the three terrain heights plus the
//   placement offset, never above the vanilla front-edge y and never above the origin. On the field r1c tops (front edge uphill) it
//   removes the float. On the bases (front edge downhill) it equals the vanilla y. NaN inputs are skipped. Fit is LowestOfThree on a pad
//   station and the origin on a slope station (DontRaise + DontLower, TerrainKindOf).

using System;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    internal static class StationPoseTests
    {
        private static Func<bool, string, string, bool> s_Check;

        private static bool Check(bool ok, string where, string msg) => s_Check(ok, where, msg);

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Sp1AlignedEveryTypeAndDirection();
            Sp2Degenerate();
            Sp3CatalogRows();
            Sp7HeightRule();
        }

        /// <summary>SP-1..SP-4: the four types, 72 directions, three lengths, a spot off the origin.</summary>
        private static void Sp1AlignedEveryTypeAndDirection()
        {
            const string W = "pose/SP-1";
            int cases = 0;
            for (int type = 0; type < SkiCatalog.Lifts.Count; type++)
            {
                StationDef b = SkiCatalog.StationOf(type, StationRole.Base), t = SkiCatalog.StationOf(type, StationRole.Top);
                if (!Check(b != null && t != null, W, "type " + type + " has no base or top row"))
                    continue;
                foreach (float len in new[] { 120f, 640f, 2900f })
                {
                    for (int k = 0; k < 72; k++)
                    {
                        float a = math.radians(k * 5f + 0.37f);
                        float2 bottom = new float2(-1234.5f, 2018.25f);
                        float2 top = bottom + len * new float2(math.sin(a), math.cos(a));
                        StationAlign.LiftPose p = StationAlign.PoseLift(bottom, top, b, t);
                        string at = " (type " + SkiCatalog.Lifts[type].Id + ", " + len + " m, " + (k * 5) + " deg)";
                        cases++;
                        if (!Check(p.Valid, W, "invalid pose" + at))
                            continue;
                        // Tips as the census computes them, from the placed transform (y 0: Measure's rise is not under test)
                        float3 bp = new float3(p.BasePosition.x, 0f, p.BasePosition.z), tp = new float3(p.TopPosition.x, 0f, p.TopPosition.z);
                        float3 btip = StationAlign.StubTip(bp, p.BaseRotation, b.StubTipLocalZ, b.StubTipLocalX);
                        float3 ttip = StationAlign.StubTip(tp, p.TopRotation, t.StubTipLocalZ, t.StubTipLocalX);
                        float2 bax = StationAlign.Axis(p.BaseRotation), tax = StationAlign.Axis(p.TopRotation);
                        StationAlign.Result up = StationAlign.Measure(btip, bax, ttip, tax);
                        StationAlign.Result down = StationAlign.Measure(ttip, tax, btip, bax);
                        Check(up.Aligned && up.AlongM > 0f, W, "top vs base not aligned: lateral " + up.LateralM + ", yaw " + up.YawDeg + ", along " + up.AlongM + at);
                        Check(down.Aligned && down.AlongM > 0f, "pose/SP-2", "base vs top not aligned: lateral " + down.LateralM + ", yaw " + down.YawDeg + at);
                        float want = len - math.abs(b.StubTipLocalZ) - math.abs(t.StubTipLocalZ);
                        Check(math.abs(math.distance(btip.xz, ttip.xz) - want) <= 1e-3f * math.max(1f, len / 100f) &&
                              math.abs(p.TipDistanceM - want) <= 1e-3f * math.max(1f, len / 100f), "pose/SP-3",
                              "tip distance " + math.distance(btip.xz, ttip.xz) + " (pose " + p.TipDistanceM + "), want " + want + at);
                        // The axis runs through the spot: the spot lies on the line through both tips, and the origin is StubTipLocalX to the side
                        float2 dir = math.normalize(top - bottom);
                        Check(math.dot(bax, dir) > 0.99999f && math.dot(tax, -dir) > 0.99999f, "pose/SP-4", "a line does not leave toward the other station" + at);
                        Check(math.abs(StationAlign.Cross(dir, bottom - btip.xz)) < 2e-3f && math.abs(StationAlign.Cross(dir, top - btip.xz)) < 2e-3f,
                              "pose/SP-4", "a spot is off the line axis" + at);
                        Check(math.abs(math.distance(p.BasePosition.xz, bottom) - math.abs(b.StubTipLocalX)) < 2e-3f &&
                              math.abs(math.distance(p.TopPosition.xz, top) - math.abs(t.StubTipLocalX)) < 2e-3f, "pose/SP-4",
                              "origin to spot " + math.distance(p.BasePosition.xz, bottom) + " / " + math.distance(p.TopPosition.xz, top) + at);
                        Check(float.IsNaN(p.BasePosition.y) && float.IsNaN(p.TopPosition.y), "pose/SP-4", "y is not NaN" + at);
                        Check(math.distance(p.BaseTip.xz, btip.xz) < 2e-3f && math.distance(p.TopTip.xz, ttip.xz) < 2e-3f, "pose/SP-4",
                              "the pose's own tips differ from the transform's" + at);
                    }
                }
            }
            Check(cases == SkiCatalog.Lifts.Count * 3 * 72 && SkiCatalog.Lifts.Count == 4, W, cases + " case(s) for " + SkiCatalog.Lifts.Count + " type(s)");
        }

        /// <summary>SP-5: too close, exactly the two stubs plus the gap, NaN spots, a missing row.</summary>
        private static void Sp2Degenerate()
        {
            const string W = "pose/SP-5";
            int dq = SkiCatalog.LiftById("detachable").Index;
            StationDef b = SkiCatalog.StationOf(dq, StationRole.Base), t = SkiCatalog.StationOf(dq, StationRole.Top);   // 16 + 16
            float stubs = math.abs(b.StubTipLocalZ) + math.abs(t.StubTipLocalZ);
            Check(!StationAlign.PoseLift(float2.zero, new float2(0f, stubs), b, t).Valid, W, "spots one stub pair apart gave a valid pose");
            Check(!StationAlign.PoseLift(float2.zero, new float2(0f, stubs + StationAlign.MinTipGapM), b, t).Valid, W,
                  "spots stubs + gap apart gave a valid pose (the bound is exclusive)");
            Check(StationAlign.PoseLift(float2.zero, new float2(0f, stubs + StationAlign.MinTipGapM + 0.01f), b, t).Valid, W,
                  "spots just over stubs + gap apart gave an invalid pose");
            Check(!StationAlign.PoseLift(new float2(float.NaN, 0f), new float2(0f, 500f), b, t).Valid, W, "a NaN spot gave a valid pose");
            Check(!StationAlign.PoseLift(float2.zero, new float2(0f, 500f), null, t).Valid, W, "a missing base row gave a valid pose");
        }

        /// <summary>SP-6: the rows the pose reads are the ones the brief names (12 m FG / tram, 16 m DQ / gondola, tram base 4 m aside).</summary>
        private static void Sp3CatalogRows()
        {
            const string W = "pose/SP-6";
            string[] ids = { "fixedGrip", "detachable", "gondola", "tram" };
            float[] tip = { 12f, 16f, 16f, 12f };
            for (int i = 0; i < ids.Length; i++)
            {
                LiftTypeDef def = SkiCatalog.LiftById(ids[i]);
                if (!Check(def != null, W, ids[i] + " missing"))
                    continue;
                StationDef b = SkiCatalog.StationOf(def.Index, StationRole.Base), t = SkiCatalog.StationOf(def.Index, StationRole.Top);
                Check(math.abs(math.abs(b.StubTipLocalZ) - tip[i]) < 1e-4f && math.abs(math.abs(t.StubTipLocalZ) - tip[i]) < 1e-4f, W,
                      ids[i] + " stub tips " + b.StubTipLocalZ + " / " + t.StubTipLocalZ);
                Check(math.abs(b.StubTipLocalX - (ids[i] == "tram" ? 4f : 0f)) < 1e-4f && math.abs(t.StubTipLocalX) < 1e-4f, W,
                      ids[i] + " side offsets " + b.StubTipLocalX + " / " + t.StubTipLocalX);
            }
            // The tram base: the origin sits 4 m to the side of its spot, the spot stays on the line
            StationAlign.LiftPose p = StationAlign.PoseLift(float2.zero, new float2(0f, 1000f), SkiCatalog.StationOf(SkiCatalog.LiftById("tram").Index, StationRole.Base),
                                                            SkiCatalog.StationOf(SkiCatalog.LiftById("tram").Index, StationRole.Top));
            Check(p.Valid && math.abs(math.abs(p.BasePosition.x) - 4f) < 1e-3f && math.abs(p.BasePosition.z) < 1e-3f, W,
                  "tram base origin at (" + p.BasePosition.x + ", " + p.BasePosition.z + ") for a line up +z from the origin");
        }
        /// <summary>SP-7: the station height rule of the build queue (StationAlign.StationY). Field numbers: RESULTS r1c note 5 and the
        /// pass log's StationPlace [height] lines (front edge, origin). The line-side height is not in that log, so the tops take it NaN
        /// (the rule must still not float) and a few cases add one.</summary>
        private static void Sp7HeightRule()
        {
            const string W = "pose/SP-7";
            const StationAlign.StationHeightRule V = StationAlign.StationHeightRule.VanillaFront, L = StationAlign.StationHeightRule.LowestOfThree;
            // Plan #8 P3 top (fixed grip): Transform y 997.61 = the front edge, origin 986.34, stub nodes at 980.4.
            Check(math.abs(StationAlign.StationY(V, 997.61f, 986.34f, 980.4f, 0f) - 997.61f) < 1e-3f, W, "vanilla is not the front edge");
            Check(math.abs(StationAlign.StationY(L, 997.61f, 986.34f, 980.4f, 0f) - 980.4f) < 1e-3f, W, "lowest of a top is not the line side");
            Check(math.abs(StationAlign.StationY(L, 997.61f, 986.34f, 980.4f, 0.25f) - 980.65f) < 1e-3f, W, "the placement offset is not added");
            // A base on a slope: the front edge is downhill, so the rule gives the vanilla y (the cut the pass measured, -1.8 to -3.7 m).
            Check(math.abs(StationAlign.StationY(L, 504.95f, 507.43f, 510.2f, 0f) - StationAlign.StationY(V, 504.95f, 507.43f, 510.2f, 0f)) < 1e-4f, W,
                  "a base on a plain slope does not keep the vanilla y");
            // NaN heights are skipped. None finite: NaN (the placer then falls back to the vanilla y or the origin).
            Check(math.abs(StationAlign.StationY(L, 997.61f, 986.34f, float.NaN, 0f) - 986.34f) < 1e-3f, W, "a NaN line side was not skipped");
            Check(math.abs(StationAlign.StationY(L, float.NaN, 986.34f, 990f, 0f) - 986.34f) < 1e-3f, W, "a NaN front was not skipped");
            Check(float.IsNaN(StationAlign.StationY(L, float.NaN, float.NaN, float.NaN, 0f)), W, "three NaN heights gave a number");
            Check(float.IsNaN(StationAlign.StationY(V, float.NaN, 986.34f, 980f, 0f)), W, "vanilla without a front edge gave a number");
            Check(math.abs(StationAlign.StationY(L, 1000f, 1000f, 1000f, float.NaN) - 1000f) < 1e-4f, W, "a NaN offset was not 0");
            // The field r1c tops (front edge, origin from StationPlace [height]): vanilla floats 4.8 to 13.8 m, the rule does not.
            float[][] tops =
            {
                new[] { 1196.07f, 1191.02f }, new[] { 997.61f, 986.34f }, new[] { 1245.85f, 1233.39f }, new[] { 1179.39f, 1170.39f },
                new[] { 1397.86f, 1384.02f }, new[] { 1437.76f, 1430.65f }, new[] { 1056.77f, 1048.02f }, new[] { 1436.08f, 1431.27f },
            };
            foreach (float[] f in tops)
            {
                float v = StationAlign.StationY(V, f[0], f[1], float.NaN, 0f), l = StationAlign.StationY(L, f[0], f[1], float.NaN, 0f);
                Check(v - f[1] > 4f && l <= f[1] + 1e-4f, W, "top front " + f[0] + " origin " + f[1] + ": vanilla " + v + ", lowest " + l);
            }
            // Property: the lowest finite height plus the offset; never above the vanilla y or any finite height.
            var rnd = new System.Random(20261109);
            int bad = 0;
            for (int i = 0; i < 20000; i++)
            {
                float a = (float)(rnd.NextDouble() * 400 + 600), b = (float)(rnd.NextDouble() * 400 + 600), c = (float)(rnd.NextDouble() * 400 + 600);
                if (rnd.Next(10) == 0) a = float.NaN;
                if (rnd.Next(10) == 0) c = float.NaN;
                float off = (float)(rnd.NextDouble() * 2 - 1);
                float l = StationAlign.StationY(L, a, b, c, off), v = StationAlign.StationY(V, a, b, c, off);
                float want = math.min(math.min(float.IsNaN(a) ? float.MaxValue : a, b), float.IsNaN(c) ? float.MaxValue : c) + off;
                if (!(math.abs(l - want) < 1e-3f) || (!float.IsNaN(v) && l > v + 1e-4f))
                    bad++;
            }
            Check(bad == 0, W, bad + " of 20000 random cases broke the lowest-of-three rule");

            // Fit: the rule per station kind. A slope station (DontRaise + DontLower) takes the origin, a pad station the lowest of three.
            const StationAlign.StationHeightRule Fi = StationAlign.StationHeightRule.Fit;
            var pad = StationAlign.StationTerrainKind.Pad;
            var slope = StationAlign.StationTerrainKind.Slope;
            Check(StationAlign.TerrainKindOf(true, true) == slope && StationAlign.TerrainKindOf(true, false) == pad &&
                  StationAlign.TerrainKindOf(false, true) == pad && StationAlign.TerrainKindOf(false, false) == pad, W,
                  "the station kind is not Slope exactly when both flags are set");
            // Plan #8 P3 fixed-grip top (a slope station): fit = the origin 986.34, between the vanilla 997.61 and the lowest 980.4.
            Check(math.abs(StationAlign.StationY(Fi, slope, 997.61f, 986.34f, 980.4f, 0f) - 986.34f) < 1e-3f, W, "fit of a slope top is not the origin");
            Check(math.abs(StationAlign.StationY(Fi, slope, 997.61f, 986.34f, 980.4f, 0.25f) - 986.59f) < 1e-3f, W, "fit of a slope top lost the offset");
            Check(math.abs(StationAlign.StationY(Fi, pad, 997.61f, 986.34f, 980.4f, 0f) - 980.4f) < 1e-3f, W, "fit of a pad top is not the lowest");
            Check(math.abs(StationAlign.StationY(Fi, 997.61f, 986.34f, 980.4f, 0f) - 980.4f) < 1e-3f, W, "fit without a kind is not the pad rule");
            // The kind changes only Fit: vanilla and lowest ignore it.
            Check(math.abs(StationAlign.StationY(V, slope, 997.61f, 986.34f, 980.4f, 0f) - 997.61f) < 1e-3f &&
                  math.abs(StationAlign.StationY(L, slope, 997.61f, 986.34f, 980.4f, 0f) - 980.4f) < 1e-3f, W, "the kind changed vanilla or lowest");
            // A slope station with no origin height: the lowest of the other two. None finite: NaN.
            Check(math.abs(StationAlign.StationY(Fi, slope, 997.61f, float.NaN, 980.4f, 0f) - 980.4f) < 1e-3f, W, "fit of a slope without an origin");
            Check(float.IsNaN(StationAlign.StationY(Fi, slope, float.NaN, float.NaN, float.NaN, 0f)), W, "fit of three NaN heights gave a number");
            // The field r1c tops as slope stations: fit is the origin, so the top neither floats like vanilla nor sinks to the line side.
            foreach (float[] f in tops)
            {
                float fit = StationAlign.StationY(Fi, slope, f[0], f[1], f[1] - 6f, 0f);
                Check(math.abs(fit - f[1]) < 1e-3f, W, "slope top front " + f[0] + " origin " + f[1] + ": fit " + fit);
            }
            // Property: Fit on a pad equals LowestOfThree, Fit on a slope with a finite origin equals origin + offset, never NaN then.
            bad = 0;
            for (int i = 0; i < 20000; i++)
            {
                float a = (float)(rnd.NextDouble() * 400 + 600), b = (float)(rnd.NextDouble() * 400 + 600), c = (float)(rnd.NextDouble() * 400 + 600);
                if (rnd.Next(10) == 0) a = float.NaN;
                if (rnd.Next(10) == 0) b = float.NaN;
                if (rnd.Next(10) == 0) c = float.NaN;
                float off = (float)(rnd.NextDouble() * 2 - 1);
                float lo = StationAlign.StationY(L, a, b, c, off);
                float fp = StationAlign.StationY(Fi, pad, a, b, c, off), fs = StationAlign.StationY(Fi, slope, a, b, c, off);
                bool padOk = float.IsNaN(lo) ? float.IsNaN(fp) : math.abs(fp - lo) < 1e-4f;
                bool slopeOk = float.IsNaN(b) ? (float.IsNaN(lo) ? float.IsNaN(fs) : math.abs(fs - lo) < 1e-4f) : math.abs(fs - (b + off)) < 1e-4f;
                if (!padOk || !slopeOk)
                    bad++;
            }
            Check(bad == 0, W, bad + " of 20000 random cases broke the fit rule");
        }
    }
}
