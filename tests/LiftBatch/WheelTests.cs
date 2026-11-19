// Turning bullwheels and station nose trains (T256): BullwheelMath and LiftAlignMath.StationNosePose. Where-tags `wheel/BW-n`,
// `wheel/SN-n`.
// Pinned:
// - With the up lane on the right looking uphill, the rope turns counterclockwise seen from above at both terminals. A swapped up
//   lane turns both the other way. The sign does not depend on the line's heading.
// - Rim speed = rope speed: the angle advanced over any number of passes is rope travel / radius.
// - A held loop's wheel keeps its angle bit for bit, and goes on from it at the resume (no jump). A paused clock, a clock step back
//   and a clock jump leave it too.
// - A reversing rope (the tram) turns the wheel back to where it was.
// - The bone turns about its own Y on top of its rest rotation; a positive angle is clockwise seen from above.
// - A station's outer nose half follows the first span's rope under the tower-train limits; the inner half never moves.

using System;
using System.Globalization;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    public static class WheelTests
    {
        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Bw1TurnSign();
            Bw2RimSpeed();
            Bw3HoldAndResume();
            Bw4ClockGuards();
            Bw5Reversal();
            Bw6Bone();
            Bw7CatalogRates();
            Sn1NoseFollows();
            Sn2NoseLimits();
        }

        private static void Check(bool cond, string where, string msg) => s_Check(cond, where, msg);

        private static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

        // ── BW-1: the turn sense from the loop's own arc ─────────────────────────────────────────────────────────────────────────
        private static void Bw1TurnSign()
        {
            const string W = "wheel/BW-1";
            // Plain cases: travel +z with the center on the right (+x) is clockwise seen from above
            Check(BullwheelMath.TurnSign(new float3(0f, 0f, 1f), new float3(-1f, 0f, 0f)) == 1, W, "travel +z, center at +x is not clockwise");
            Check(BullwheelMath.TurnSign(new float3(0f, 0f, 1f), new float3(1f, 0f, 0f)) == -1, W, "travel +z, center at -x is not counterclockwise");
            // A lift at every heading: uphill u, right r (looking uphill). Up lane on the right: the rope enters the top wheel at
            // +r R traveling +u, and the base wheel at -r R traveling -u.
            for (int deg = 0; deg < 360; deg += 15)
            {
                float a = math.radians(deg);
                float3 u = new float3(math.sin(a), 0f, math.cos(a));
                float3 r = new float3(math.cos(a), 0f, -math.sin(a));      // +x when u = +z
                int top = BullwheelMath.TurnSign(u, r), bas = BullwheelMath.TurnSign(-u, -r);
                Check(top == -1 && bas == -1, W, "heading " + deg + ": up lane right gives top " + top + " base " + bas + " (want -1 -1)");
                // the up lane on the left (a swapped loop): both terminals turn the other way
                int topL = BullwheelMath.TurnSign(u, -r), basL = BullwheelMath.TurnSign(-u, r);
                Check(topL == 1 && basL == 1, W, "heading " + deg + ": up lane left gives top " + topL + " base " + basL + " (want 1 1)");
                // a pitched entry tangent (the rope dives to the wheel) does not change the sense
                Check(BullwheelMath.TurnSign(math.normalize(u + new float3(0f, -0.33f, 0f)), r) == -1, W, "heading " + deg + ": a pitched entry flips the sense");
            }
            Check(BullwheelMath.IsWheelBone("Bullwheel") && BullwheelMath.IsWheelBone("BullwheelDrive") &&
                  !BullwheelMath.IsWheelBone("Root") && !BullwheelMath.IsWheelBone("NoseTrainA") && !BullwheelMath.IsWheelBone(null), W,
                  "the bone name rule");
        }

        // ── BW-2: rim speed = rope speed ─────────────────────────────────────────────────────────────────────────────────────────
        private static void Bw2RimSpeed()
        {
            const string W = "wheel/BW-2";
            double[] radii = { 1.8, 2.6, 3.0 };
            double[] speeds = { 2.5, 5.0, 5.5 };
            for (int k = 0; k < radii.Length; k++)
            {
                double angle = 0.0, travel = 0.0;
                var rng = new System.Random(256 + k);
                for (int i = 0; i < 20000; i++)
                {
                    double dt = 0.004 + rng.NextDouble() * 0.2;          // 5 to 250 fps, sim speeds mixed
                    angle = BullwheelMath.Advance(angle, speeds[k], dt, radii[k], -1);
                    travel += speeds[k] * dt;
                    if (!(angle > -Math.PI - 1e-12 && angle <= Math.PI + 1e-12))
                    {
                        Check(false, W, "angle " + F(angle) + " left (-pi, pi] at pass " + i);
                        break;
                    }
                }
                double want = BullwheelMath.Wrap(-travel / radii[k]);
                double err = Math.Abs(BullwheelMath.Wrap(angle - want));
                Check(err < 1e-7, W, "R " + F(radii[k]) + ": after " + F(travel) + " m the angle is " + F(angle) + ", rope travel / R gives " +
                                     F(want) + " (err " + F(err) + ")");
                // one pass: the rim point moves exactly the rope's step
                double one = BullwheelMath.Advance(0.0, speeds[k], 0.02, radii[k], 1);
                Check(Math.Abs(one * radii[k] - speeds[k] * 0.02) < 1e-12, W, "one pass: rim " + F(one * radii[k]) + " m vs rope " + F(speeds[k] * 0.02));
            }
        }

        // ── BW-3: held loops stand, a resume goes on from the same angle ─────────────────────────────────────────────────────────
        private static void Bw3HoldAndResume()
        {
            const string W = "wheel/BW-3";
            double a = 0.0;
            for (int i = 0; i < 500; i++)
                a = BullwheelMath.Advance(a, 2.5, 1.0 / 60.0, 1.8, -1);
            double atHold = a;
            for (int i = 0; i < 100000; i++)
                a = BullwheelMath.Advance(a, 0.0, 1.0 / 60.0, 1.8, -1);      // held: the rope's speed is 0 (ChairPinSystem.WheelSpeedMps)
            Check(a == atHold, W, "a held wheel moved: " + F(atHold) + " -> " + F(a));
            double resumed = BullwheelMath.Advance(a, 2.5, 1.0 / 60.0, 1.8, -1);
            double step = BullwheelMath.Wrap(resumed - atHold);
            Check(Math.Abs(step - (-2.5 / 60.0 / 1.8)) < 1e-12, W, "the first pass after a resume stepped " + F(step) + " rad (want one pass's step: no jump)");
        }

        // ── BW-4: the clock guards ───────────────────────────────────────────────────────────────────────────────────────────────
        private static void Bw4ClockGuards()
        {
            const string W = "wheel/BW-4";
            const double a = 1.25;
            Check(BullwheelMath.Advance(a, 5.0, 0.0, 2.6, 1) == a, W, "a paused clock (dt 0) moved the wheel");
            Check(BullwheelMath.Advance(a, 5.0, -3.0, 2.6, 1) == a, W, "a clock step back moved the wheel");
            Check(BullwheelMath.Advance(a, 5.0, BullwheelMath.MaxStepS + 0.01, 2.6, 1) == a, W, "a clock jump moved the wheel");
            Check(BullwheelMath.Advance(a, 5.0, 0.8, 2.6, 1) != a, W, "a 0.8 s pass (sim speed 8 at 10 fps) did not move the wheel");
            Check(BullwheelMath.Advance(a, double.NaN, 0.02, 2.6, 1) == a, W, "a NaN speed moved the wheel");
            Check(BullwheelMath.Advance(a, 5.0, 0.02, 0.1, 1) == a && BullwheelMath.Advance(a, 5.0, 0.02, 50.0, 1) == a, W, "a radius outside the wheel limits moved the wheel");
            Check(BullwheelMath.Advance(double.NaN, 5.0, 0.0, 2.6, 1) == 0.0, W, "a NaN angle is not reset to 0");
            Check(Math.Abs(BullwheelMath.Wrap(3.0 * Math.PI) - Math.PI) < 1e-12 && Math.Abs(BullwheelMath.Wrap(-3.0 * Math.PI) - Math.PI) < 1e-12 &&
                  BullwheelMath.Wrap(0.5) == 0.5, W, "Wrap range (-pi, pi]");
        }

        // ── BW-5: a reversing rope (the tram's trips) ────────────────────────────────────────────────────────────────────────────
        private static void Bw5Reversal()
        {
            const string W = "wheel/BW-5";
            double a = 0.4;
            for (int i = 0; i < 3000; i++)
                a = BullwheelMath.Advance(a, 10.0, 1.0 / 50.0, 1.8, 1);
            double far = a;
            for (int i = 0; i < 3000; i++)
                a = BullwheelMath.Advance(a, -10.0, 1.0 / 50.0, 1.8, 1);
            Check(Math.Abs(BullwheelMath.Wrap(a - 0.4)) < 1e-7, W, "up and back down the wheel ends at " + F(a) + " (started 0.4, turned to " + F(far) + ")");
        }

        // ── BW-6: the bone rotation ──────────────────────────────────────────────────────────────────────────────────────────────
        private static void Bw6Bone()
        {
            const string W = "wheel/BW-6";
            // identity rest: +90 deg takes the bone's +z to +x (clockwise seen from above), y stays
            float3 z = math.mul(BullwheelMath.BoneRotation(quaternion.identity, math.radians(90f)), new float3(0f, 0f, 1f));
            Check(math.distance(z, new float3(1f, 0f, 0f)) < 1e-5f, W, "+90 deg maps +z to " + z + " (want +x)");
            float3 y = math.mul(BullwheelMath.BoneRotation(quaternion.identity, 1.234f), new float3(0f, 1f, 0f));
            Check(math.distance(y, new float3(0f, 1f, 0f)) < 1e-6f, W, "the axle moved: " + y);
            // a rest rotation is kept: angle 0 is the rest pose, and the turn is about the bone's own Y (a vertical wheel's axle)
            quaternion rest = quaternion.RotateX(math.radians(90f));     // the bone's Y lies along the object's +z
            Check(math.abs(math.dot(BullwheelMath.BoneRotation(rest, 0f), rest)) > 0.999999f, W, "angle 0 is not the rest pose");
            float3 axle = math.mul(BullwheelMath.BoneRotation(rest, 0.7f), new float3(0f, 1f, 0f));
            Check(math.distance(axle, math.mul(rest, new float3(0f, 1f, 0f))) < 1e-5f, W, "a rotated rest: the axle moved to " + axle);
        }

        // ── BW-7: the catalog's lifts turn at a rate a player can read ───────────────────────────────────────────────────────────
        private static void Bw7CatalogRates()
        {
            const string W = "wheel/BW-7";
            int loops = 0;
            foreach (LiftTypeDef def in SkiCatalog.Lifts)
            {
                if (def.Mover != MoverKind.Loop)
                    continue;
                loops++;
                double radius = def.GaugeHalfM;      // the loop's turn arc: half the rope gauge
                Check(radius >= BullwheelMath.MinRadiusM && radius <= BullwheelMath.MaxRadiusM, W, def.Id + ": turn radius " + F(radius) + " m is outside the wheel limits");
                double rev = BullwheelMath.RevPerSecond(def.SpeedMps, radius);
                Check(rev > 0.1 && rev < 0.5, W, def.Id + ": " + F(rev) + " rev/s (want a slow, readable turn)");
                // no strobing: at 30 fps the wheel steps well under a spoke pitch (8 spokes = 45 deg)
                double stepDeg = rev * 360.0 / 30.0;
                Check(stepDeg < 10.0, W, def.Id + ": " + F(stepDeg) + " deg per frame at 30 fps");
            }
            Check(loops == 3, W, "loop lift types " + loops + " (want 3: fixed grip, detachable, gondola)");
        }

        // ── SN-1: the outer nose half follows the first span's rope ──────────────────────────────────────────────────────────────
        private static void Sn1NoseFollows()
        {
            const string W = "wheel/SN-1";
            float d = math.radians(1f);
            float maxPitch = LiftAlignMath.TrainPitchLimit(0.8f), hb = math.radians(LiftAlignMath.MaxHalfBreakDeg);
            // The fixed-grip base: the in-station rope runs down to the wheel toward +z (-18.4), the line climbs away toward -z at 27 deg
            // (-27 in "nose up along +z"). The outer half takes the line's pitch.
            float over = LiftAlignMath.StationNosePose(-18.4f * d, -27f * d, 2, maxPitch, hb, float.PositiveInfinity, out float line, out bool clamped);
            Check(math.abs(line - -27f * d) < 1e-6f && over < 1e-4f && !clamped, W, "FG base 27 deg span: line " + F(math.degrees(line)) + " over " + F(over));
            // no line rope: in line with the inner half
            over = LiftAlignMath.StationNosePose(-18.4f * d, 123f, 0, maxPitch, hb, float.PositiveInfinity, out line, out clamped);
            Check(line == -18.4f * d && over == 0f && !clamped, W, "no line rope: line " + F(math.degrees(line)));
            // a top station: the line drops away (toward -z, so + in this frame)
            over = LiftAlignMath.StationNosePose(0f, 22f * d, 2, maxPitch, hb, float.PositiveInfinity, out line, out clamped);
            Check(math.abs(line - 22f * d) < 1e-6f && over < 1e-4f, W, "DQ top 22 deg: line " + F(math.degrees(line)));
            // within the limits it equals the tower rule's half on that side
            for (int fixedDeg = -20; fixedDeg <= 20; fixedDeg += 10)
                for (int lineDeg = -28; lineDeg <= 28; lineDeg += 4)
                {
                    if (math.abs(lineDeg - fixedDeg) > 2f * LiftAlignMath.MaxHalfBreakDeg)
                        continue;
                    LiftAlignMath.StationNosePose(fixedDeg * d, lineDeg * d, 1, maxPitch, hb, float.PositiveInfinity, out float s, out _);
                    bool towerClamped = LiftAlignMath.HalfPitches(fixedDeg * d, 1, lineDeg * d, 1, maxPitch, hb, out float pf, out float pb);
                    Check(!towerClamped && math.abs(s - pb) < 1e-6f && math.abs(pf - fixedDeg * d) < 1e-6f, W,
                        "fixed " + fixedDeg + " line " + lineDeg + ": station " + F(math.degrees(s)) + " vs tower back " + F(math.degrees(pb)));
                }
        }

        // ── SN-2: the limits ─────────────────────────────────────────────────────────────────────────────────────────────────────
        private static void Sn2NoseLimits()
        {
            const string W = "wheel/SN-2";
            float d = math.radians(1f);
            float hb = math.radians(LiftAlignMath.MaxHalfBreakDeg);
            float dq = LiftAlignMath.TrainPitchLimit(0.9f);
            // the pitch limit (30 deg): a 40 deg first span leaves 10 deg
            float over = LiftAlignMath.StationNosePose(0f, -40f * d, 2, dq, hb, float.PositiveInfinity, out float line, out bool clamped);
            Check(clamped && math.abs(line - -dq) < 1e-6f && math.abs(over - (40f - math.degrees(dq))) < 1e-3f, W,
                "40 deg span: line " + F(math.degrees(line)) + " over " + F(over));
            // the break limit: at most 2 x the half-break away from the inner half
            over = LiftAlignMath.StationNosePose(-18.4f * d, 25f * d, 2, math.radians(60f), hb, float.PositiveInfinity, out line, out clamped);
            Check(clamped && math.abs(line - (-18.4f + 40f) * d) < 1e-5f && math.abs(over - (25f - 21.6f)) < 1e-3f, W,
                "break limit: line " + F(math.degrees(line)) + " over " + F(over));
            // the carrier-clearance limit
            over = LiftAlignMath.StationNosePose(0f, -29f * d, 2, dq, hb, 20f * d, out line, out clamped);
            Check(clamped && math.abs(line - -20f * d) < 1e-6f && math.abs(over - 9f) < 1e-3f, W, "world limit: line " + F(math.degrees(line)) + " over " + F(over));
            // idempotent: feeding the result back changes nothing
            LiftAlignMath.StationNosePose(-18.4f * d, -55f * d, 2, dq, hb, float.PositiveInfinity, out float once, out _);
            float again = LiftAlignMath.StationNosePose(-18.4f * d, once, 2, dq, hb, float.PositiveInfinity, out float twice, out bool c2);
            Check(once == twice && again == 0f && !c2, W, "not idempotent: " + F(math.degrees(once)) + " -> " + F(math.degrees(twice)));
        }
    }
}
