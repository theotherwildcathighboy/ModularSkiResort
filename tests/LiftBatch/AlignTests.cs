// Lift alignment (docs: lift-alignment-2026-11-02, gates G1 / G2): LiftMoverMath.CarrierDrop and LiftAlignMath. Where-tags `align/...`,
// AL-1..AL-10.
// Pinned: every carrier drops by its grip top minus the jaw's rope point (F5, the rope-point pin); the plan bend is the turn angle;
// a train on a uniform slope pitches to exactly that slope, at a bend or a sag break it takes the bisector of the arriving and leaving
// rope, clamped; the pitch sign follows the node's facing, so the train is nose up toward the higher neighbor either way; re-running
// on the result changes nothing.

using System;
using System.Collections.Generic;
using System.Globalization;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    public static class AlignTests
    {
        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Al1CarrierDrop();
            Al2PlanBend();
            Al3UniformSlope();
            Al4BreakOver();
            Al5Clamp();
            Al6FacingSign();
            Al7Idempotent();
            Al8Station();
            Al9LaneEnds();
            Al10Halves();
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);

        private static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

        // ── AL-1: the rope-point pin (G2) ────────────────────────────────────────────────────────────────────────────────────
        private static void Al1CarrierDrop()
        {
            int n = 0;
            foreach (LiftTypeDef def in SkiCatalog.Lifts)
            {
                foreach (CarrierDef c in def.Carriers)
                {
                    float drop = LiftMoverMath.CarrierDrop(c.HangM, c.GripTopAboveRopeM);
                    Check(math.abs(drop - (c.HangM - c.GripTopAboveRopeM)) < 1e-6f, "align/AL-1 " + c.Prefab,
                          "drop " + F(drop) + " != HangM " + F(c.HangM) + " - rope point " + F(c.GripTopAboveRopeM));
                    Check(c.GripTopAboveRopeM > 0f && drop > 0f && drop < c.HangM, "align/AL-1 " + c.Prefab,
                          "rope point " + F(c.GripTopAboveRopeM) + " m below the grip top must be inside the carrier (drop " + F(drop) + ")");
                    n++;
                }
            }
            Check(n >= 4, "align/AL-1", "only " + n + " carriers in the catalog");
        }

        // ── AL-2: plan bend ─────────────────────────────────────────────────────────────────────────────────────────────────
        private static void Al2PlanBend()
        {
            float3 a = new float3(0f, 10f, 0f), n = new float3(0f, 30f, 60f);
            Check(LiftAlignMath.PlanBendDeg(a, n, new float3(0f, 50f, 120f)) < 1e-3f, "align/AL-2", "straight line is not 0");
            foreach (float deg in new[] { 2f, 5f, 10f, 30f, 90f })
            {
                float r = math.radians(deg);
                float3 next = n + new float3(60f * math.sin(r), -5f, 60f * math.cos(r));
                float got = LiftAlignMath.PlanBendDeg(a, n, next);
                Check(math.abs(got - deg) < 0.01f, "align/AL-2 " + deg, "bend " + F(got));
            }
            Check(LiftAlignMath.PlanBendDeg(n, n, a) == 0f, "align/AL-2", "zero-length leg is not 0");
            Check(LiftAlignMath.BendWarnDeg == 5f, "align/AL-2", "warn threshold moved from the owner's 5 deg");
        }

        // ── AL-3: uniform slope → θ = slope (G1), any sag, any span pair ────────────────────────────────────────────────────────
        private static void Al3UniformSlope()
        {
            foreach (float deg in new[] { 0f, 10f, 20f, 30f, 38.7f })
            {
                float g = math.tan(math.radians(deg));
                foreach (float h in new[] { 0f, 0.004f, 0.01f, 0.03f })
                {
                    float3 p = new float3(0f, 0f, 0f), q = new float3(0f, 60f * g, 60f), r = new float3(0f, 120f * g, 120f);
                    // Sag adds +4h arriving and -4h leaving: the bisector of the two rope ANGLES (the train sits square to both) is the
                    // slope exactly without sag and within 0.3 deg of it at h 0.03 (atan is not linear).
                    float th = math.degrees(LiftAlignMath.TrainPitch(p, q, r, h, math.radians(80f)));
                    float bis = math.degrees(0.5f * (math.atan(g + 4f * h) + math.atan(g - 4f * h)));
                    Check(math.abs(th - bis) < 1e-3f && math.abs(th - deg) < (h == 0f ? 1e-3f : 0.3f), "align/AL-3 " + deg + " h " + h,
                          "pitch " + F(th) + ", bisector " + F(bis));
                }
            }
        }

        // ── AL-4: break-over → bisector; the outer-sheave residual is ± xo·tan(break/2) ────────────────────────────────────────
        private static void Al4BreakOver()
        {
            // 60 m spans, 18 m then 0 m rise (a crest), h 0.01: in = atan(0.3 + 0.04), out = atan(0 - 0.04)
            float3 p = new float3(0f, 0f, 0f), q = new float3(0f, 18f, 60f), r = new float3(0f, 18f, 120f);
            float expect = 0.5f * (math.atan(0.34f) + math.atan(-0.04f));
            float got = LiftAlignMath.TrainPitch(p, q, r, 0.01f, 1.4f);
            Check(math.abs(got - expect) < 1e-5f, "align/AL-4", "pitch " + F(math.degrees(got)) + " expected " + F(math.degrees(expect)));
            // symmetric sag on level ground: arriving +4h, leaving -4h → 0
            Check(math.abs(LiftAlignMath.TrainPitch(p, new float3(0f, 0f, 60f), new float3(0f, 0f, 120f), 0.03f, 1.4f)) < 1e-6f,
                  "align/AL-4", "level line with sag must give 0");
            Check(math.abs(LiftAlignMath.ArriveSlope(p, q, 0.01f) - 0.34f) < 1e-6f && math.abs(LiftAlignMath.LeaveSlope(p, q, 0.01f) - 0.26f) < 1e-6f,
                  "align/AL-4", "end slopes are chord ±4h (LaneSystem.cs:4128-4138)");
        }

        // ── AL-5: clamp at MaxSlope ──────────────────────────────────────────────────────────────────────────────────────────
        private static void Al5Clamp()
        {
            float max = math.atan(0.8f);   // FG m_MaxSlopeSteepness 0.8 = 38.66 deg
            float3 p = new float3(0f, 0f, 0f), q = new float3(0f, 60f, 60f), r = new float3(0f, 120f, 120f);
            Check(math.abs(LiftAlignMath.TrainPitch(p, q, r, 0f, max) - max) < 1e-6f, "align/AL-5", "45 deg not clamped to 38.66");
            Check(math.abs(LiftAlignMath.TrainPitch(r, q, p, 0f, max) + max) < 1e-6f, "align/AL-5", "-45 deg not clamped to -38.66");
        }

        // ── AL-6: a node facing either way ends nose up toward the higher neighbor ───────────────────────────────────────────
        private static void Al6FacingSign()
        {
            float3 p = new float3(0f, 0f, 0f), q = new float3(0f, 20f, 60f), r = new float3(0f, 40f, 120f);
            float th = LiftAlignMath.TrainPitch(p, q, r, 0f, 1.4f);
            foreach (float yawDeg in new[] { 0f, 180f, 7f, 187f })
            {
                quaternion rot = LiftAlignMath.PitchedRotation(quaternion.RotateY(math.radians(yawDeg)), p, r, th);
                float3 fwd = math.mul(rot, new float3(0f, 0f, 1f));
                // the end of the train toward r must be the higher one: +z end up when +z faces r, down when it faces p
                float facing = math.sign(math.dot(fwd.xz, (r - p).xz));
                Check(facing * fwd.y > 0f && math.abs(math.abs(fwd.y) - math.sin(th)) < 1e-4f, "align/AL-6 yaw " + yawDeg,
                      "forward " + fwd + " for pitch " + F(math.degrees(th)));
                // the rope point stays where it is when the pin is the rope point
                float3 at = LiftAlignMath.AboutPin(q, rot, float3.zero);
                Check(math.distance(at, q) < 1e-6f, "align/AL-6 yaw " + yawDeg, "pin moved");
            }
        }

        // ── AL-7: idempotent (a re-run on the same nodes gives the same rotation) ────────────────────────────────────────────
        private static void Al7Idempotent()
        {
            float3 p = new float3(3f, 1f, -2f), q = new float3(40f, 17f, 50f), r = new float3(70f, 25f, 110f);
            quaternion yaw = quaternion.RotateY(0.4f);
            quaternion a = LiftAlignMath.PitchedRotation(yaw, p, r, LiftAlignMath.TrainPitch(p, q, r, 0.004f, 1f));
            quaternion b = LiftAlignMath.PitchedRotation(yaw, p, r, LiftAlignMath.TrainPitch(p, q, r, 0.004f, 1f));
            Check(math.abs(math.dot(a, b)) > 0.999999f, "align/AL-7", "same input, different rotation");
        }

        // ── AL-8: station end: the first span's rope angle at the anchor ─────────────────────────────────────────────────────
        private static void Al8Station()
        {
            float3 s = new float3(0f, 5f, 0f), l = new float3(0f, 5f + 60f * math.tan(math.radians(20f)), 60f);
            float th = math.degrees(LiftAlignMath.StationTrainPitch(s, l, 0f, 1.4f));
            Check(math.abs(th - 20f) < 0.01f, "align/AL-8", "station pitch " + F(th) + " on a 20 deg first span, no sag");
            float sag = math.degrees(LiftAlignMath.StationTrainPitch(s, l, 0.004f, 1.4f));
            Check(sag < th && math.abs(math.tan(math.radians(sag)) - (math.tan(math.radians(20f)) - 0.016f)) < 1e-4f, "align/AL-8",
                  "station pitch with sag " + F(sag) + " is not the leaving slope (chord - 4h)");
        }

        // ── AL-9: Phase 2 trains: the mean of a tower's four rope lane ends is the bisector, either facing; the bone noses up ──
        private static void Al9LaneEnds()
        {
            float h = 0.004f;
            float3 p = new float3(0f, 0f, -200f), q = new float3(0f, 200f * math.tan(math.radians(20f)), 0f);
            float3 r = new float3(0f, q.y + 200f * math.tan(math.radians(25f)), 200f);
            float want = LiftAlignMath.TrainPitch(p, q, r, h, 1.4f);
            foreach (float yawDeg in new[] { 0f, 180f })
            {
                float3 fwd = math.mul(quaternion.RotateY(math.radians(yawDeg)), new float3(0f, 0f, 1f));
                float sum = 0f;
                foreach (float x in new[] { -2f, 2f })
                {
                    float3 o = new float3(x, 0f, 0f);
                    // arriving span p..q ends at q (end d, inner c); leaving span q..r starts at q (end a, inner b)
                    float3 c = math.lerp(p, q, 2f / 3f) - new float3(0f, 200f * h * 4f / 3f, 0f);
                    float3 b = math.lerp(q, r, 1f / 3f) - new float3(0f, 200f * h * 4f / 3f, 0f);
                    sum += LiftAlignMath.LaneEndPitch(q + o, c + o, fwd) + LiftAlignMath.LaneEndPitch(q + o, b + o, fwd);
                }
                float got = sum / 4f * (yawDeg == 0f ? 1f : -1f);
                Check(math.abs(got - want) < 1e-4f, "align/AL-9 yaw " + yawDeg, "lane-end mean " + F(math.degrees(got)) +
                      " is not the bisector " + F(math.degrees(want)));
            }
            float3 nose = math.mul(LiftAlignMath.TrainBoneRotation(quaternion.identity, math.radians(30f)), new float3(0f, 0f, 1f));
            Check(math.abs(nose.y - 0.5f) < 1e-5f && nose.z > 0f, "align/AL-9", "bone at +30 deg points " + nose + ", not nose up");
            Check(math.abs(math.degrees(LiftAlignMath.MaxTrainPitch(0.8f)) - 38.66f) < 0.01f, "align/AL-9", "FG limit is not atan 0.8");
        }

        // ── AL-10: articulated halves (option C): each half follows its own span's rope, either facing; break and slope clamps ──
        private static void Al10Halves()
        {
            float h = 0.004f;
            float3 p = new float3(0f, 0f, -200f), q = new float3(0f, 200f * math.tan(math.radians(30f)), 0f);
            float3 r = new float3(0f, q.y + 200f * math.tan(math.radians(10f)), 200f);   // a 20 deg crest on a climb
            float wantFront = math.atan(LiftAlignMath.LeaveSlope(q, r, h)), wantBack = math.atan(LiftAlignMath.ArriveSlope(p, q, h));
            foreach (float yawDeg in new[] { 0f, 180f })
            {
                float3 fwd = math.mul(quaternion.RotateY(math.radians(yawDeg)), new float3(0f, 0f, 1f));
                float sumF = 0f, sumB = 0f;
                int nF = 0, nB = 0;
                foreach (float x in new[] { -2f, 2f })
                {
                    float3 o = new float3(x, 0f, 0f);
                    float3 c = math.lerp(p, q, 2f / 3f) - new float3(0f, 200f * h * 4f / 3f, 0f);
                    float3 b = math.lerp(q, r, 1f / 3f) - new float3(0f, 200f * h * 4f / 3f, 0f);
                    foreach (float3 inner in new[] { c, b })
                    {
                        float pitch = LiftAlignMath.LaneEndPitch(q + o, inner + o, fwd);
                        if (LiftAlignMath.LaneEndSide(q + o, inner + o, fwd) > 0) { sumF += pitch; nF++; }
                        else { sumB += pitch; nB++; }
                    }
                }
                Check(nF == 2 && nB == 2, "align/AL-10 yaw " + yawDeg, "sides " + nF + " / " + nB + ", not 2 / 2");
                bool clamped = LiftAlignMath.HalfPitches(sumF / nF, nF, sumB / nB, nB, 1.4f, out float f, out float k);
                // facing 180: the tower's +z points at p, so its front half carries the p span, nose up along its own +z (sign flips)
                float ef = yawDeg == 0f ? wantFront : -wantBack, eb = yawDeg == 0f ? wantBack : -wantFront;
                Check(!clamped && math.abs(f - ef) < 1e-4f && math.abs(k - eb) < 1e-4f, "align/AL-10 yaw " + yawDeg,
                      "front " + F(math.degrees(f)) + " / back " + F(math.degrees(k)) + ", expected " + F(math.degrees(ef)) + " / " +
                      F(math.degrees(eb)));
            }
            float d = math.radians(1f), max = LiftAlignMath.MaxTrainPitch(0.8f);
            // a crest of 30 deg stays; one of 50 deg clamps to +-20 about its mean
            Check(!LiftAlignMath.HalfPitches(-15f * d, 2, 15f * d, 2, max, out float f1, out float b1) &&
                  math.abs(f1 + 15f * d) < 1e-6f && math.abs(b1 - 15f * d) < 1e-6f, "align/AL-10", "30 deg crest moved");
            Check(LiftAlignMath.HalfPitches(-20f * d, 2, 30f * d, 2, max, out float f2, out float b2) &&
                  math.abs(f2 + 15f * d) < 1e-5f && math.abs(b2 - 25f * d) < 1e-5f, "align/AL-10",
                  "50 deg crest gives " + F(math.degrees(f2)) + " / " + F(math.degrees(b2)) + ", not -15 / 25");
            // a missing side takes the other's pitch; no rope at all is level and false
            Check(!LiftAlignMath.HalfPitches(12f * d, 2, 0f, 0, max, out float f3, out float b3) && f3 == b3 && math.abs(f3 - 12f * d) < 1e-6f,
                  "align/AL-10", "one-sided tower is not straight");
            Check(!LiftAlignMath.HalfPitches(5f, 0, 5f, 0, max, out float f4, out float b4) && f4 == 0f && b4 == 0f, "align/AL-10", "no rope");
            // the slope clamp holds after the break clamp: both halves within +-max, half-break within the limit
            LiftAlignMath.HalfPitches(30f * d, 2, 80f * d, 2, max, out float f5, out float b5);
            Check(math.abs(f5) <= max + 1e-6f && math.abs(b5) <= max + 1e-6f && (b5 - f5) * 0.5f <= LiftAlignMath.MaxHalfBreakDeg * d + 1e-6f,
                  "align/AL-10", "steep clamp gives " + F(math.degrees(f5)) + " / " + F(math.degrees(b5)));
            Check(LiftAlignMath.MaxHalfBreakDeg == 20f, "align/AL-10", "half-break limit moved from the art's G6 sweep (20 deg)");
            // the owner's 30 deg tilt limit on every type (FG 0.8, DQ 0.9, GD 1.2, tram 2.5 are all steeper); a flatter type keeps its own
            foreach (float slope in new[] { 0.8f, 0.9f, 1.2f, 2.5f })
                Check(math.abs(math.degrees(LiftAlignMath.TrainPitchLimit(slope)) - 30f) < 1e-4f, "align/AL-10 slope " + F(slope),
                      "train limit " + F(math.degrees(LiftAlignMath.TrainPitchLimit(slope))) + ", not 30");
            Check(math.abs(LiftAlignMath.TrainPitchLimit(0.3f) - math.atan(0.3f)) < 1e-6f, "align/AL-10", "a 16.7 deg type is not limited by its slope");
        }
    }
}
