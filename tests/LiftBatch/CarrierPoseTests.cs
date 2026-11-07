// The two-object carrier pose (CarrierPose.cs, the carrier grip round, docs plan carrier-pivot-swing-plan-2026-11-03 section 6).
// Where-tags `carrier-pose/...`, CP-1..CP-20.
// Pinned:
// - Zero swing on a level rope is today's rigid pose (body = rope - drop, yaw = NetUtils.GetNodeRotation's).
// - The grip's axis follows the rope tangent within 0.1 deg, and the rope point stays in its jaw within 1 cm.
// - The body is plumb at zero wind, and its pin stays on the grip's pin under any pitch and swing.
// - The swing stays within the clamps and leans downwind.
// - Guided segments carry no swing, and the ramps start at 0.
// - The wind blend is continuous.
// - The swing shows no step or pop on every lift type at realistic winds (CP-9: within the analytic rate at 30 fps
//   3x and 1/480 s, under 0.5 deg per 30 fps frame at 1x).
// - The pairing math inverts vanilla's EvenSpacing index.
// Review fixes:
// - The static pin's body hangs from the pitched grip's pin (CP-15).
// - A grip assembled on a body that stays put keeps the joint (CP-16).
// - A late first wind sample blends in instead of snapping (CP-17).
// The swing convention (2026-11-03): the roll turns grip and body together about the rope point and the rope tangent.
// The fore-aft turns the body about the pin. The roll's per-type limits keep a chair's base excursion at the old 8 deg pin roll's.
// They hold the tram to 2 deg at half its fore-aft amplitude (CP-18).
// The visible-sway round (2026-11-06): chairs and cabins rock 4 to 6 deg peak to peak at |w| 0.31 under gusts while the lean keeps
// its direction (CP-19). A hold release resumes the gusts (CP-20).

using System;
using System.Globalization;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    public static class CarrierPoseTests
    {
        private static Func<bool, string, string, bool> s_Check;

        /// <summary>The frame of the continuity test: 30 fps at 3x = 0.1 s of mover clock per frame.</summary>
        private const double FrameS = 3.0 / 30.0;
        private const float MaxStepDeg = 0.5f;
        /// <summary>Realistic wind magnitudes: step 0 measured |w| ~ 0.31. Vanilla's constant wind (0.275, 0.275) is 0.389.</summary>
        private const float RealisticWind = 0.4f;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Cp1RigidIdentity();
            Cp2GripAxis();
            Cp3RopeInJaw();
            Cp4Plumb();
            Cp5Clamps();
            Cp6LeanDownwind();
            Cp7Guided();
            Cp8Blend();
            Cp9Continuity();
            Cp10Pairing();
            Cp11SwayArg();
            Cp12SeedPhase();
            Cp13ToggleOff();
            Cp14Constants();
            Cp15StaticPinJoint();
            Cp16GripOnBody();
            Cp17FirstWind();
            Cp18RollAboutRope();
            Cp19Visible();
            Cp20HoldResume();
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);

        /// <summary>The type's lateral limits, as SwingInputs and the tram path build them.</summary>
        private static SwingLimits LimitsOf(LiftTypeDef d) => CarrierPose.Limits(d.Pivots, d.Mover == MoverKind.Shuttle);

        private static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

        /// <summary>NetUtils.GetNodeRotation(tangent, fallback) (Game.Net/NetUtils.cs:316-324): y zeroed, normalized, LookRotation
        /// with up. The fallback is for a vertical tangent. MathUtils.TryNormalize's threshold is not decompiled here: 1e-12 on lengthsq.</summary>
        private static quaternion Yaw(float3 tangent, quaternion fallback)
        {
            tangent.y = 0f;
            float lsq = math.lengthsq(tangent);
            if (!(lsq > 1e-12f))
                return fallback;
            return quaternion.LookRotation(tangent / math.sqrt(lsq), math.up());
        }

        private static float3 Tangent(float yawRad, float pitchRad)
        {
            math.sincos(pitchRad, out float sp, out float cp);
            math.sincos(yawRad, out float sy, out float cy);
            return new float3(cp * sy, sp, cp * cy);
        }

        private static float AngleDeg(float3 a, float3 b)
        {
            float c = math.clamp(math.dot(math.normalize(a), math.normalize(b)), -1f, 1f);
            return math.degrees(math.acos(c));
        }

        private static float AngleDeg(quaternion a, quaternion b)
        {
            float d = math.clamp(math.abs(math.dot(a, b)), 0f, 1f);
            return math.degrees(2f * math.acos(d));
        }

        private static readonly float[] Pitches = { -0.785f, -0.5f, -0.2f, -0.05f, 0f, 0.05f, 0.2f, 0.5f, 0.785f };
        private static readonly float[] Yaws = { 0f, 0.3f, 1.2f, 2.5f, 3.1f, -1.7f, -0.6f };

        // ── CP-1: zero swing + level rope == today's rigid pose ──
        private static void Cp1RigidIdentity()
        {
            const string W = "carrier-pose/rigid";
            int n = 0, bad = 0;
            foreach (LiftTypeDef d in SkiCatalog.Lifts)
            {
                CarrierPivots pv = d.Pivots;
                float drop = pv.RigidDrop;
                foreach (float y in Yaws)
                {
                    float3 rope = new float3(1234.5f + 17f * y, 812.25f, -3456.75f + 9f * y);
                    float3 t = Tangent(y, 0f);
                    quaternion yaw = Yaw(t, quaternion.identity);
                    foreach (bool pitch in new[] { true, false })
                    {
                        CarrierPose.Pose(rope, t, yaw, pv, pitch, float2.zero, out float3 gp, out quaternion gr, out float3 bp, out quaternion br);
                        float3 rigid = rope;
                        rigid.y -= drop;
                        n++;
                        // The rigid offset (0, -drop, 0) rotates by the yaw. The error is sub-0.1 mm (LookRotation's float residue off the pure
                        // yaw and the drop's own rounding), far inside the mover's 1 mm idempotence. The body rotation IS the yaw.
                        bool ok = math.distance(bp, rigid) <= 1e-4f &&
                                  br.value.Equals(yaw.value) && math.abs(math.dot(gr, yaw)) > 0.99999f;   // the mover's own test
                        if (!ok)
                        {
                            bad++;
                            Check(false, W, d.Id + " yaw " + F(y) + " pitch " + pitch + ": body " + bp + " rot " + br + ", want " + rigid + " rot " + yaw);
                        }
                    }
                }
            }
            Check(bad == 0 && n > 0, W, bad + " of " + n + " poses off the rigid pose");
        }

        // ── CP-2: the grip's forward axis follows the rope tangent (<= 0.1 deg), its lateral axis stays horizontal ──
        private static void Cp2GripAxis()
        {
            const string W = "carrier-pose/grip-axis";
            float worst = 0f, worstLat = 0f;
            CarrierPivots pv = SkiCatalog.Lifts[0].Pivots;
            foreach (float y in Yaws)
            {
                foreach (float p in Pitches)
                {
                    float3 t = Tangent(y, p);
                    foreach (bool flip in new[] { false, true })
                    {
                        // flip: the static pin's facing flip (yaw from -tangent). The grip must still lie along the rope.
                        quaternion yaw = Yaw(flip ? -t : t, quaternion.identity);
                        CarrierPose.Pose(float3.zero, t, yaw, pv, true, float2.zero, out _, out quaternion gr, out _, out _);
                        float3 fwd = math.rotate(gr, new float3(0f, 0f, 1f));
                        float a = math.min(AngleDeg(fwd, t), AngleDeg(fwd, -t));
                        worst = math.max(worst, a);
                        worstLat = math.max(worstLat, math.abs(math.rotate(gr, new float3(1f, 0f, 0f)).y));
                        // The grip's forward agrees with the body's facing (no 180-degree grip on a flipped carrier).
                        float3 bodyFwd = math.rotate(yaw, new float3(0f, 0f, 1f));
                        Check(fwd.x * bodyFwd.x + fwd.z * bodyFwd.z > 0f, W, "yaw " + F(y) + " pitch " + F(p) + " flip " + flip + ": grip faces against the body");
                    }
                }
            }
            Check(worst <= 0.1f, W, "grip axis vs tangent: worst " + F(worst) + " deg (<= 0.1)");
            Check(worstLat <= 1e-5f, W, "the pin axis tilts out of the horizontal: |lateral.y| " + F(worstLat));
        }

        // ── CP-3: the rope point stays in the jaw (<= 1 cm) at any pitch ──
        private static void Cp3RopeInJaw()
        {
            const string W = "carrier-pose/rope-in-jaw";
            float worst = 0f;
            foreach (LiftTypeDef d in SkiCatalog.Lifts)
            {
                foreach (float y in Yaws)
                {
                    foreach (float p in Pitches)
                    {
                        float3 rope = new float3(-2100.125f, 1530.5f, 4400.25f);
                        float3 t = Tangent(y, p);
                        quaternion yaw = Yaw(t, quaternion.identity);
                        CarrierPose.Pose(rope, t, yaw, d.Pivots, true, new float2(0.1f, -0.03f), out float3 gp, out quaternion gr, out _, out _);
                        float3 jaw = gp + math.rotate(gr, d.Pivots.GripRope);
                        worst = math.max(worst, math.distance(jaw, rope));
                    }
                }
            }
            Check(worst <= 0.01f, W, "rope point vs the jaw: worst " + F(worst) + " m (<= 0.01)");
        }

        // ── CP-4: plumb at zero wind. The body's pin stays on the grip's pin under pitch and swing ──
        private static void Cp4Plumb()
        {
            const string W = "carrier-pose/plumb";
            float worstUp = 0f, worstJoint = 0f;
            foreach (LiftTypeDef d in SkiCatalog.Lifts)
            {
                foreach (float y in Yaws)
                {
                    foreach (float p in Pitches)
                    {
                        float3 rope = new float3(500.5f, 900.25f, -700.75f);
                        float3 t = Tangent(y, p);
                        quaternion yaw = Yaw(t, quaternion.identity);
                        float2 calm = CarrierPose.Swing(float2.zero, yaw, 1.3f, new float2(1f, 1f), LimitsOf(d), 0.9f);
                        Check(calm.x == 0f && calm.y == 0f, W, "zero wind gives swing " + calm);
                        CarrierPose.Pose(rope, t, yaw, d.Pivots, true, calm, out _, out quaternion gr, out float3 bp, out quaternion br);
                        worstUp = math.max(worstUp, AngleDeg(math.rotate(br, new float3(0f, 1f, 0f)), new float3(0f, 1f, 0f)));
                        foreach (float2 sw in new[] { calm, new float2(0.13f, 0.05f), new float2(-0.1f, -0.04f) })
                        {
                            CarrierPose.Pose(rope, t, yaw, d.Pivots, true, sw, out _, out gr, out bp, out br);
                            float3 pinGrip = CarrierPose.PinWorld(rope, gr, d.Pivots);
                            float3 pinBody = bp + math.rotate(br, d.Pivots.BodyPin);
                            worstJoint = math.max(worstJoint, math.distance(pinGrip, pinBody));
                        }
                    }
                }
            }
            Check(worstUp <= 1e-3f, W, "body up vs world up at zero wind: worst " + F(worstUp) + " deg");
            Check(worstJoint <= 0.001f, W, "body pin vs grip pin: worst " + F(worstJoint) + " m (<= 1 mm)");
        }

        // ── CP-5: lean + sway stay within the type's roll clamp (Limits.MaxRoll) and MaxForeAft at any wind ──
        private static void Cp5Clamps()
        {
            const string W = "carrier-pose/clamps";
            float[] mags = { 0.05f, 0.31f, 0.389f, 1f, 3f, 20f };
            foreach (LiftTypeDef d in SkiCatalog.Lifts)
            {
                SwingLimits limits = LimitsOf(d);
                float worstLat = 0f, worstFa = 0f;
                foreach (float mag in mags)
                {
                    for (int a = 0; a < 16; a++)
                    {
                        float ang = a * (math.PI / 8f);
                        float2 w = mag * new float2(math.cos(ang), math.sin(ang));
                        foreach (float y in Yaws)
                        {
                            quaternion yaw = Yaw(Tangent(y, 0f), quaternion.identity);
                            // Sway arguments on the quarter turns (sin / cos reach +-1 exactly) and between. The gust at both ends.
                            for (int k = 0; k < 16; k++)
                            {
                                float2 s = CarrierPose.Swing(w, yaw, k * (math.PI / 4f), new float2(1f, 1f), limits, (k & 1) == 0 ? 1f : 0.6f);
                                worstLat = math.max(worstLat, math.abs(s.x));
                                worstFa = math.max(worstFa, math.abs(s.y));
                            }
                        }
                    }
                }
                string clamp = F(math.degrees(limits.MaxRoll));
                Check(limits.MaxRoll > 0f && worstLat <= limits.MaxRoll + 1e-7f, W, d.Id + ": roll " + F(math.degrees(worstLat)) + " deg over the " + clamp + " deg clamp");
                Check(worstFa <= CarrierPose.MaxForeAft + 1e-7f, W, d.Id + ": fore-aft " + F(math.degrees(worstFa)) + " deg over the 3 deg clamp");
                // The strong winds reach the clamps (the clamp is live, not dead code).
                Check(worstLat >= limits.MaxRoll - 1e-6f && worstFa >= CarrierPose.MaxForeAft - 1e-6f, W, d.Id + ": a 20 |w| wind does not reach the clamps");
                Console.WriteLine("INFO  " + W + ": " + d.Id + " roll clamp " + clamp + " deg, roll gain " + F(limits.RollGain));
            }
        }

        // ── CP-6: the body leans downwind: a cross wind to the carrier's right moves its bottom right, and a tail wind moves it forward
        // (measured against the calm pose: the roll turns the whole assembly about the rope point, the pin moves with it) ──
        private static void Cp6LeanDownwind()
        {
            const string W = "carrier-pose/downwind";
            foreach (LiftTypeDef d in SkiCatalog.Lifts)
            {
                CarrierPivots pv = d.Pivots;
                SwingLimits limits = LimitsOf(d);
                foreach (float y in Yaws)
                {
                    float3 t = Tangent(y, 0f);
                    quaternion yaw = Yaw(t, quaternion.identity);
                    float3 f = math.rotate(yaw, new float3(0f, 0f, 1f));
                    float3 right = math.rotate(yaw, new float3(1f, 0f, 0f));
                    CarrierPose.Pose(float3.zero, t, yaw, pv, true, float2.zero, out _, out _, out float3 calmBody, out _);
                    // the lean alone (ramp (1, 0)) at a wind under the clamps (0.25: the ratio check needs both leans unclamped)
                    float2 cross = CarrierPose.Swing(0.25f * new float2(right.x, right.z), yaw, 0f, new float2(1f, 0f), limits, 1f);
                    CarrierPose.Pose(float3.zero, t, yaw, pv, true, cross, out _, out _, out float3 bp, out _);
                    float moved = math.dot(bp - calmBody, right);
                    Check(cross.x > 0f && moved > 0.01f, W, d.Id + " yaw " + F(y) + ": a cross wind to the right leans the bottom by " + F(moved) + " m");
                    float2 tail = CarrierPose.Swing(0.25f * new float2(f.x, f.z), yaw, 0f, new float2(1f, 0f), limits, 1f);
                    CarrierPose.Pose(float3.zero, t, yaw, pv, true, tail, out _, out _, out bp, out _);
                    moved = math.dot(bp - calmBody, f);
                    Check(tail.y > 0f && moved > 0.005f, W, d.Id + " yaw " + F(y) + ": a tail wind swings the bottom forward by " + F(moved) + " m");
                    // The lateral lean against the fore-aft lean: the roll gain over ForeAftShare (chairs: k / (1/3) > 1, tram: 1/2).
                    float ratio = math.abs(cross.x) / math.abs(tail.y);
                    float want = limits.RollGain / CarrierPose.ForeAftShare;
                    Check(math.abs(ratio - want) <= 1e-3f * want, W, d.Id + " yaw " + F(y) + ": lateral / fore-aft lean " + F(ratio) + ", want " + F(want));
                }
            }
        }

        // ── CP-7: guided segments (arcs, station runs) carry no swing. The ramps start at 0 and end at 1 ──
        private static void Cp7Guided()
        {
            const string W = "carrier-pose/guided";
            Check(CarrierPose.DistanceToGuided(true, 3f, 10f, 50f, 50f) == 0f, W, "a guided segment has a nonzero distance");
            float2 r0 = CarrierPose.Ramp(0f, 10f);
            Check(r0.x == 0f && r0.y == 0f, W, "ramp at a guided boundary " + r0);
            quaternion yaw = quaternion.identity;
            foreach (LiftTypeDef d in SkiCatalog.Lifts)
            {
                float2 s = CarrierPose.Swing(new float2(5f, 5f), yaw, 1.1f, r0, LimitsOf(d), 1f);
                Check(s.x == 0f && s.y == 0f, W, d.Id + ": a guided pose swings " + s);
            }
            float2 r1 = CarrierPose.Ramp(10f, 10f), r2 = CarrierPose.Ramp(20f, 10f), rh = CarrierPose.Ramp(5f, 10f);
            Check(r1.x == 1f && r1.y == 0f && r2.x == 1f && r2.y == 1f && math.abs(rh.x - 0.5f) < 1e-6f && rh.y == 0f, W,
                  "ramps: at R " + r1 + ", 2R " + r2 + ", R/2 " + rh);
            // The distance takes the nearer end of the free run, both ways.
            Check(CarrierPose.DistanceToGuided(false, 2f, 30f, 5f, 100f) == 7f, W, "distance from the run start");
            Check(CarrierPose.DistanceToGuided(false, 28f, 30f, 500f, 1f) == 3f, W, "distance to the run end");
            // Ramp length: 10 m minimum, 2 s of travel for the tram.
            Check(CarrierPose.RampM(2.5f) == 10f && CarrierPose.RampM(10f) == 20f, W, "RampM(2.5) " + CarrierPose.RampM(2.5f) + ", RampM(10) " + CarrierPose.RampM(10f));
        }

        // ── CP-8: the wind blend: from at 0, to at WindBlendS, continuous and monotone between. A re-change mid-blend is continuous ──
        private static void Cp8Blend()
        {
            const string W = "carrier-pose/blend";
            float2 a = new float2(0.3f, -0.1f), b = new float2(-0.25f, 0.2f);
            Check(CarrierPose.BlendWind(a, b, 0.0).Equals(a) && CarrierPose.BlendWind(a, b, -1.0).Equals(a), W, "not 'from' at the change");
            Check(CarrierPose.BlendWind(a, b, CarrierPose.WindBlendS).Equals(b) && CarrierPose.BlendWind(a, b, 99.0).Equals(b), W, "not 'to' after the blend");
            float worst = 0f;
            float2 prev = a;
            float lastU = -1f;
            bool monotone = true;
            for (double t = 0.0; t <= CarrierPose.WindBlendS + 0.5; t += 1.0 / 60.0)
            {
                float2 w = CarrierPose.BlendWind(a, b, t);
                worst = math.max(worst, math.distance(w, prev));
                float u = math.dot(w - a, b - a) / math.lengthsq(b - a);
                monotone &= u >= lastU - 1e-6f;
                lastU = u;
                prev = w;
            }
            // smoothstep's peak slope is 1.5 / WindBlendS per second: 1.5 x |b - a| / WindBlendS / 60 per 60 fps frame
            float bound = 1.5f * math.distance(a, b) / (float)CarrierPose.WindBlendS / 60f * 1.01f;
            Check(worst <= bound, W, "blend step " + F(worst) + " over the smoothstep bound " + F(bound));
            Check(monotone, W, "the blend goes backward");
            // A change mid-blend: the system sets prev := the blended value at the change, so the new blend starts where the old one was.
            float2 mid = CarrierPose.BlendWind(a, b, 1.7);
            float2 c = new float2(0.05f, 0.35f);
            Check(math.distance(CarrierPose.BlendWind(mid, c, 0.0), mid) == 0f && math.distance(CarrierPose.BlendWind(mid, c, 1.0 / 60.0), mid) < 0.001f, W,
                  "a re-change mid-blend jumps");
        }

        // ── CP-9: no visible step or pop (redefined 2026-11-06, the visible-sway round). The first check (every swing step <= 0.5 deg per
        // 30 fps frame at 3x) held the sway to 1 deg. A pendulum big enough to see moves faster than that at 3x without any step (FG:
        // 2.85 deg x omega 2.54 rad/s = 0.72 deg per 0.1 s of clock). The intent is no visible step or pop. It has three checks on the body's
        // swing rotation per type, guided -> free run -> guided, with the carrier moving through the gust field. The checks run at realistic
        // winds from three directions with a reversal mid-span (blended over WindBlendS) and at a saturating wind with no change:
        //  (a) rate: every 30 fps step at 3x (0.1 s of mover clock) <= RateBound x 0.1 s + 0.01 deg (RateBound: the analytic smooth rate).
        //  (b) continuity: every step at 48x finer sampling (1/480 s of clock) <= RateBound x 1/480 s + 0.01 deg, so a jump over about
        //      0.05 deg anywhere fails (a snap of the gust, the wind, a ramp or a clamp cannot hide between two frames).
        //  (c) hard cap at 1x: every 30 fps step at 1x (1/30 s) <= 0.5 deg.
        // A planted 0.1 deg jump must fail (b). The test can catch a pop. ──
        private const double FineS = 1.0 / 480.0;
        private const int FinePer3x = 48, FinePer1x = 16;
        private const double StepEpsDeg = 0.01;

        /// <summary>The analytic bound on the swing's rate (rad/s): |d lat/dt| + |d fa/dt| (the angle between two roll x fore-aft rotations
        /// is at most |d lat| + |d fa|: the rotation metric is bi-invariant). Per axis with gain G (RollGain, ForeAftShare) and clamp C:
        /// value = ramp.x x clamp(LeanPerWind2 G m c g, +-room) + ramp.y x A G sin(theta), A = min(SwayPerWind m, MaxSway) x (1 - S + S g),
        /// room = C - A G.
        /// theta' = omega (s' = v). ramp' <= v / rampM. |d(m c)/dt| <= 2 m |w'|.
        /// g in [0.6, 1], g' <= (GustAmp1 2 pi / T1 +
        /// GustAmp2 2 pi / T2)(1 + v / GustSpeedMps) (the carrier moves through the traveling waves).
        /// While the clamp is open the lean is at most C / 0.6 before the gust. The room moves at |A' G|.</summary>
        private static double RateBound(in SwingLimits lim, float omega, float v, float rampM, float mMax, float windRate)
        {
            double gRate = (CarrierPose.GustAmp1 * 2.0 * Math.PI / CarrierPose.GustPeriod1S + CarrierPose.GustAmp2 * 2.0 * Math.PI / CarrierPose.GustPeriod2S) *
                           (1.0 + v / CarrierPose.GustSpeedMps);
            double gMin = CarrierPose.GustMean - CarrierPose.GustAmp1 - CarrierPose.GustAmp2;
            double rampRate = v / rampM;
            double amp = Math.Min(CarrierPose.SwayPerWind * mMax, CarrierPose.MaxSway);
            double ampRate = CarrierPose.SwayPerWind * windRate + amp * CarrierPose.SwayGustShare * gRate;
            double total = 0.0;
            foreach ((double gain, double clamp) in new[] { ((double)lim.RollGain, (double)lim.MaxRoll), (CarrierPose.ForeAftShare, CarrierPose.MaxForeAft) })
            {
                double raw = CarrierPose.LeanPerWind2 * gain * mMax * mMax;
                double lean = Math.Min(raw, clamp);
                double leanRate = CarrierPose.LeanPerWind2 * gain * 2.0 * mMax * windRate + Math.Min(raw, clamp / gMin) * gRate + ampRate * gain;
                total += rampRate * lean + leanRate + rampRate * amp * gain + ampRate * gain + amp * gain * omega;
            }
            return total;
        }

        /// <summary>The rotation angle between two quaternions in degrees, in double (conj(a) x b): the float AngleDeg (2 acos |dot|)
        /// has a 0.04 deg quantum near 0, too coarse for the fine steps.</summary>
        private static double StepDeg(quaternion a, quaternion b)
        {
            double aw = a.value.w, ax = a.value.x, ay = a.value.y, az = a.value.z;
            double bw = b.value.w, bx = b.value.x, by = b.value.y, bz = b.value.z;
            double rw = aw * bw + ax * bx + ay * by + az * bz;
            double rx = aw * bx - ax * bw - ay * bz + az * by;
            double ry = aw * by - ay * bw - az * bx + ax * bz;
            double rz = aw * bz - az * bw - ax * by + ay * bx;
            return 2.0 * Math.Atan2(Math.Sqrt(rx * rx + ry * ry + rz * rz), Math.Abs(rw)) * 180.0 / Math.PI;
        }

        /// <summary>One CP-9 run: the swing rotation every FineS through guided -> span -> guided. It returns the worst step at 3x (0.1 s),
        /// at 1x (1/30 s) and fine (FineS), in degrees. <paramref name="plantRad"/> adds a roll jump after the middle (the self-test).</summary>
        private static double3 Cp9Run(LiftTypeDef d, float2 w0, bool change, float yawRad, float plantRad)
        {
            float v = d.SpeedMps;
            float omega = CarrierPose.Omega(d.Pivots.BodyPin.y);
            float rampM = CarrierPose.RampM(v);
            SwingLimits limits = LimitsOf(d);
            const float guidedM = 12f, spanM = 260f;
            float3 dir = Tangent(yawRad, 0f);
            quaternion yaw = Yaw(dir, quaternion.identity);
            float3 start = new float3(-1830.5f, 940f, 2210.25f);
            double changeAt = (guidedM + spanM * 0.5) / v;
            int steps = (int)((guidedM * 2 + spanM) / v / FineS);
            var q = new quaternion[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                double t = i * FineS;
                double s = t * v;
                bool guided = s < guidedM || s > guidedM + spanM;
                float dist = CarrierPose.DistanceToGuided(guided, (float)(s - guidedM), spanM, 0f, 0f);
                float2 wind = change && t >= changeAt ? CarrierPose.BlendWind(w0, -w0, t - changeAt) : w0;
                float gust = CarrierPose.Gust(CarrierPose.GustClock(5000.0 + t), start + dir * (float)s);
                float2 sw = CarrierPose.Swing(wind, yaw, CarrierPose.SwayArg(s, omega, v, 0.9f), CarrierPose.Ramp(dist, rampM), limits, gust);
                if (plantRad != 0f && i > steps / 2)
                    sw.x += plantRad;
                q[i] = CarrierPose.SwingRotation(sw);
            }
            double w3 = 0.0, w1 = 0.0, wf = 0.0;
            for (int i = 1; i <= steps; i++)
            {
                wf = Math.Max(wf, StepDeg(q[i - 1], q[i]));
                if (i >= FinePer1x)
                    w1 = Math.Max(w1, StepDeg(q[i - FinePer1x], q[i]));
                if (i >= FinePer3x)
                    w3 = Math.Max(w3, StepDeg(q[i - FinePer3x], q[i]));
            }
            return new double3(w3, w1, wf);
        }

        private static void Cp9Continuity()
        {
            const string W = "carrier-pose/continuity";
            // Winds: realistic magnitudes from several directions with a reversal mid-span, and a saturating wind with no change (the
            // clamps engaged through the ramp).
            float2[] winds = { new float2(RealisticWind, 0f), new float2(0f, RealisticWind), RealisticWind * math.normalize(new float2(1f, -1f)), new float2(3f, 1f) };
            foreach (LiftTypeDef d in SkiCatalog.Lifts)
            {
                float v = d.SpeedMps;
                float omega = CarrierPose.Omega(d.Pivots.BodyPin.y);
                float rampM = CarrierPose.RampM(v);
                SwingLimits limits = LimitsOf(d);
                double worst3 = 0.0, worst1 = 0.0, worstF = 0.0, worstRatio3 = 0.0, worstRatioF = 0.0, bound3 = 0.0;
                foreach (float2 w0 in winds)
                {
                    bool strong = math.length(w0) > RealisticWind + 1e-3f;
                    float mMax = math.length(w0);
                    // smoothstep's peak slope 1.5 / WindBlendS over the reversal's |w1 - w0| = 2 |w0|
                    float windRate = strong ? 0f : 1.5f * 2f * mMax / (float)CarrierPose.WindBlendS;
                    double rate = RateBound(limits, omega, v, rampM, mMax, windRate);
                    double cap3 = rate * FinePer3x * FineS * 180.0 / Math.PI + StepEpsDeg;
                    double capF = rate * FineS * 180.0 / Math.PI + StepEpsDeg;
                    foreach (float y in new[] { 0f, 0.7f, 2.2f })
                    {
                        double3 r = Cp9Run(d, w0, !strong, y, 0f);
                        string at = d.Id + " wind " + w0 + " yaw " + F(y);
                        Check(r.x <= cap3, W, at + ": a 3x frame steps " + F(r.x) + " deg, over the analytic bound " + F(cap3));
                        Check(r.z <= capF, W, at + ": a 1/480 s step of " + F(r.z) + " deg is over the analytic bound " + F(capF) + " (a pop)");
                        Check(r.y <= MaxStepDeg, W, at + ": a 1x frame steps " + F(r.y) + " deg (hard cap " + F(MaxStepDeg) + ")");
                        worst3 = Math.Max(worst3, r.x);
                        worst1 = Math.Max(worst1, r.y);
                        worstF = Math.Max(worstF, r.z);
                        worstRatio3 = Math.Max(worstRatio3, r.x / cap3);
                        worstRatioF = Math.Max(worstRatioF, r.z / capF);
                        bound3 = Math.Max(bound3, cap3);
                    }
                }
                Console.WriteLine("INFO  " + W + ": " + d.Id + " (omega " + F(omega) + " rad/s) worst step " + F(worst3) + " deg per 3x frame (bound up to " +
                                  F(bound3) + ", worst share " + F(worstRatio3) + "), " + F(worst1) + " deg per 1x frame (cap 0.5), " + F(worstF) +
                                  " deg per 1/480 s (worst share of its bound " + F(worstRatioF) + ")");
            }
            // The self-test: a planted 0.1 deg roll jump mid-span must fail (b).
            LiftTypeDef fg = SkiCatalog.Lifts[0];
            float2 wp = new float2(RealisticWind, 0f);
            double capP = RateBound(LimitsOf(fg), CarrierPose.Omega(fg.Pivots.BodyPin.y), fg.SpeedMps, CarrierPose.RampM(fg.SpeedMps), RealisticWind,
                                    1.5f * 2f * RealisticWind / (float)CarrierPose.WindBlendS) * FineS * 180.0 / Math.PI + StepEpsDeg;
            double3 planted = Cp9Run(fg, wp, true, 0f, math.radians(0.1f));
            Check(planted.z > capP, W, "a planted 0.1 deg jump steps " + F(planted.z) + " deg in 1/480 s, inside the bound " + F(capP) +
                  ": the continuity check cannot catch a pop");
        }

        // ── CP-19: the sway is visible and keeps the lean's direction (the visible-sway round, 2026-11-06). The in-game check found the
        // lean steady and no sway. The test uses a steady |w| 0.31 cross wind (step 0's sample) on a free span, the carrier moving through the gust
        // field for 180 s. Chairs / gondola: the roll's peak to peak over every pendulum period 3.5 to 7.5 deg, its median 4 to 6 deg.
        // The mean roll is downwind (> 1.5 deg), and the roll is upwind of plumb at most 5 % of the time. Tram (a 10 m hanger): subtle but moving, roll
        // peak to peak at most 1.5 deg, fore-aft 0.5 to 3 deg. ──
        private static void Cp19Visible()
        {
            const string W = "carrier-pose/visible";
            const double dt = 1.0 / 60.0, runS = 180.0;
            foreach (LiftTypeDef d in SkiCatalog.Lifts)
            {
                float v = d.SpeedMps;
                float omega = CarrierPose.Omega(d.Pivots.BodyPin.y);
                SwingLimits limits = LimitsOf(d);
                float3 dir = Tangent(0.4f, 0f);
                quaternion yaw = Yaw(dir, quaternion.identity);
                float3 right = math.rotate(yaw, new float3(1f, 0f, 0f));
                float2 wind = 0.31f * math.normalize(new float2(right.x, right.z));
                float3 start = new float3(420.5f, 1100f, -2630.25f);
                double period = 2.0 * Math.PI / omega;
                int perWindow = (int)Math.Round(period / dt);
                int n = (int)(runS / dt);
                var lat = new double[n];
                var fa = new double[n];
                double sum = 0.0;
                int upwind = 0;
                for (int i = 0; i < n; i++)
                {
                    double t = i * dt;
                    double s = 3000.0 + t * v;
                    float gust = CarrierPose.Gust(CarrierPose.GustClock(777.0 + t), start + dir * (float)(t * v));
                    float2 sw = CarrierPose.Swing(wind, yaw, CarrierPose.SwayArg(s, omega, v, 0.4f), new float2(1f, 1f), limits, gust);
                    lat[i] = math.degrees(sw.x);
                    fa[i] = math.degrees(sw.y);
                    sum += lat[i];
                    if (lat[i] < 0.0)
                        upwind++;
                }
                var ppLat = new System.Collections.Generic.List<double>();
                double ppFaMax = 0.0, ppFaMin = double.MaxValue;
                for (int w0 = 0; w0 + perWindow <= n; w0 += perWindow)
                {
                    double lo = double.MaxValue, hi = double.MinValue, flo = double.MaxValue, fhi = double.MinValue;
                    for (int i = w0; i < w0 + perWindow; i++)
                    {
                        lo = Math.Min(lo, lat[i]);
                        hi = Math.Max(hi, lat[i]);
                        flo = Math.Min(flo, fa[i]);
                        fhi = Math.Max(fhi, fa[i]);
                    }
                    ppLat.Add(hi - lo);
                    ppFaMax = Math.Max(ppFaMax, fhi - flo);
                    ppFaMin = Math.Min(ppFaMin, fhi - flo);
                }
                ppLat.Sort();
                double minPp = ppLat[0], maxPp = ppLat[ppLat.Count - 1], medPp = ppLat[ppLat.Count / 2];
                double mean = sum / n, upShare = (double)upwind / n;
                string info = d.Id + ": period " + F(period) + " s, roll peak to peak " + F(minPp) + " / " + F(medPp) + " / " + F(maxPp) +
                              " deg (min / median / max over " + ppLat.Count + " periods), fore-aft " + F(ppFaMin) + " to " + F(ppFaMax) +
                              " deg, mean roll " + F(mean) + " deg, upwind " + F(100.0 * upShare) + " % of the time";
                if (d.Mover == MoverKind.Shuttle)
                {
                    Check(maxPp <= 1.5 && ppFaMax <= 3.0 && ppFaMin >= 0.5, W, "the tram is not subtle-but-moving: " + info);
                }
                else
                {
                    Check(minPp >= 3.5 && maxPp <= 7.5 && medPp >= 4.0 && medPp <= 6.0, W, "the sway is not 4 to 6 deg peak to peak: " + info);
                    Check(mean > 1.5 && upShare <= 0.05, W, "the lean's direction does not read: " + info);
                }
                Console.WriteLine("INFO  " + W + ": " + info);
            }
        }

        // ── CP-20: a hold release (or a re-freeze at a rebuild) resumes the gusts and a wind blend where they froze
        // (CarrierWind.Resume). The swing of the first running frame equals the frozen pose's (<= 0.01 deg). Rebase keeps s continuous.
        // Without the shift it would jump (> 0.1 deg, the test can catch it). Gust: [0.6, 1.0] over the world and the clock, and two
        // carriers 20 m apart nearly together. ──
        private static void Cp20HoldResume()
        {
            const string W = "carrier-pose/resume";
            SwingLimits limits = LimitsOf(SkiCatalog.Lifts[0]);
            quaternion yaw = Yaw(Tangent(0.3f, 0f), quaternion.identity);
            float3 pos = new float3(1520.25f, 880f, -410.5f);
            const float arg = 1.2f;
            var full = new float2(1f, 1f);
            var w = new CarrierWind();
            w.Set(new float2(0.2f, 0.1f), 100.0);
            w.Set(new float2(-0.25f, 0.2f), 103.0);   // a blend in progress at the freeze
            const double frozen = 105.0, now = 105.0 + 37.3;
            float2 Pose(double clockS) => CarrierPose.Swing(w.At(clockS), yaw, arg, full, limits, CarrierPose.Gust(CarrierPose.GustClock(w.GustTimeS(clockS)), pos));
            quaternion before = CarrierPose.SwingRotation(Pose(frozen));
            quaternion noShift = CarrierPose.SwingRotation(Pose(now));
            w.Resume(frozen, now);
            quaternion after = CarrierPose.SwingRotation(Pose(now));
            double step = StepDeg(before, after), jump = StepDeg(before, noShift);
            Check(step <= 0.01, W, "the first running frame after a hold steps " + F(step) + " deg (<= 0.01)");
            Check(jump > 0.1, W, "without Resume the release would step only " + F(jump) + " deg: the test cannot catch a regression");
            // The blend continues from the frozen point: one 3x frame later the swing moves by a smooth step only.
            double next = StepDeg(after, CarrierPose.SwingRotation(Pose(now + FrameS)));
            Check(next <= 0.5, W, "the frame after the release steps " + F(next) + " deg");
            // A zero or negative hold changes nothing.
            double heldBefore = w.m_HeldS;
            w.Resume(now, now);
            w.Resume(now, now - 5.0);
            Check(w.m_HeldS == heldBefore, W, "a zero or negative hold shifts the gust clock");
            Console.WriteLine("INFO  " + W + ": a release after 37.3 s steps " + F(step) + " deg with Resume, " + F(jump) + " deg without");

            // The gust range and its spatial coherence.
            float lo = float.MaxValue, hi = float.MinValue, worstNear = 0f;
            for (int i = 0; i < 4000; i++)
            {
                double t = i * 0.37;
                float3 p = new float3(-5000f + 2.5f * i, 0f, 3000f - 1.7f * i);
                float g = CarrierPose.Gust(CarrierPose.GustClock(t), p);
                lo = math.min(lo, g);
                hi = math.max(hi, g);
                float3 near = p + new float3(20f * 0.6f, 0f, 20f * 0.8f);
                worstNear = math.max(worstNear, math.abs(g - CarrierPose.Gust(CarrierPose.GustClock(t), near)));
            }
            Check(lo >= 0.6f - 1e-5f && hi <= 1f + 1e-5f && lo < 0.65f && hi > 0.95f, W, "the gust spans " + F(lo) + " to " + F(hi) + " (want [0.6, 1.0], reached)");
            Check(worstNear <= 0.2f, W, "two carriers 20 m apart differ by " + F(worstNear) + " in gust (<= 0.2: patches, not noise)");
            Console.WriteLine("INFO  " + W + ": gust " + F(lo) + " to " + F(hi) + ", 20 m apart at most " + F(worstNear) + " apart");
        }

        // ── CP-15: the static pin (TryPinChair + PinGripOf): the body comes from the same Pose call as the pitched grip (zero swing), so the
        // pin joint holds at any pitch. The rigid body (rope - drop, yaw) the first version wrote is off by |GripPin - GripRope| x sin(pitch) ──
        private static void Cp15StaticPinJoint()
        {
            const string W = "carrier-pose/static-pin";
            float worstJoint = 0f, worstPlumb = 0f;
            int rigidCaught = 0, steep = 0;
            foreach (LiftTypeDef d in SkiCatalog.Lifts)
            {
                CarrierPivots pv = d.Pivots;
                foreach (float y in Yaws)
                {
                    foreach (float p in Pitches)
                    {
                        float3 rope = new float3(321.5f, 1404.25f, -987.75f);
                        float3 t = Tangent(y, p);
                        foreach (bool flip in new[] { false, true })
                        {
                            // flip: the static pin keeps the row's authored facing (yaw from -tangent).
                            quaternion yaw = Yaw(flip ? -t : t, quaternion.identity);
                            CarrierPose.Pose(rope, t, yaw, pv, true, float2.zero, out float3 gp, out quaternion gr, out float3 bp, out quaternion br);
                            worstJoint = math.max(worstJoint, CarrierPose.PinGap(gp, gr, bp, br, pv));
                            // Plumb and still: the body rotation IS the yaw (zero swing), its up axis world up (CP-4's measure).
                            worstPlumb = math.max(worstPlumb, br.value.Equals(yaw.value)
                                ? AngleDeg(math.rotate(br, new float3(0f, 1f, 0f)), new float3(0f, 1f, 0f))
                                : 180f);
                            if (math.abs(p) >= 0.5f)
                            {
                                // The first version's body: rope - RigidDrop at the yaw. It must be caught (> 5 cm off the pin) on a 30 deg span.
                                float3 rigid = rope;
                                rigid.y -= pv.RigidDrop;
                                steep++;
                                if (CarrierPose.PinGap(gp, gr, rigid, yaw, pv) > 0.05f)
                                    rigidCaught++;
                            }
                        }
                    }
                }
            }
            Check(worstJoint <= 0.001f, W, "static pin: body pin vs grip pin, worst " + F(worstJoint) + " m (<= 1 mm)");
            Check(worstPlumb <= 1e-3f, W, "static pin: the body is not the plumb yaw, worst " + F(worstPlumb) + " deg");
            Check(steep > 0 && rigidCaught == steep, W, "the rigid body off the pin by > 5 cm on a 30 deg span: " + rigidCaught + " of " + steep +
                  " (the test would not catch the first version)");
        }

        // ── CP-16: a grip assembled on a body that stands where it stands (the tram's parked carriage, a static carrier left at vanilla's
        // placement). The pins coincide at any body rotation. On the rigid pose, it is the Pose grip with the toggle off ──
        private static void Cp16GripOnBody()
        {
            const string W = "carrier-pose/grip-on-body";
            float worstJoint = 0f, worstRigid = 0f;
            foreach (LiftTypeDef d in SkiCatalog.Lifts)
            {
                CarrierPivots pv = d.Pivots;
                foreach (float y in Yaws)
                {
                    float3 rope = new float3(-45.5f, 610.75f, 2222.25f);
                    float3 t = Tangent(y, 0.3f);
                    quaternion yaw = Yaw(t, quaternion.identity);
                    foreach (float2 sw in new[] { float2.zero, new float2(0.1f, -0.04f), new float2(-0.13f, 0.05f) })
                    {
                        quaternion bodyRot = math.mul(yaw, CarrierPose.SwingRotation(sw));
                        float3 bodyPos = rope + new float3(0.3f, -pv.RigidDrop, -0.2f);
                        CarrierPose.GripOnBody(bodyPos, bodyRot, pv, out float3 gp, out quaternion gr);
                        worstJoint = math.max(worstJoint, CarrierPose.PinGap(gp, gr, bodyPos, bodyRot, pv));
                    }
                    CarrierPose.Pose(rope, t, yaw, pv, false, float2.zero, out float3 pg, out quaternion pgr, out float3 pb, out quaternion pbr);
                    CarrierPose.GripOnBody(pb, pbr, pv, out float3 og, out quaternion ogr);
                    worstRigid = math.max(worstRigid, math.distance(og, pg));
                    Check(math.abs(math.dot(ogr, pgr)) > 0.99999f, W, d.Id + " yaw " + F(y) + ": the parked grip's rotation is not the toggle-off grip's");
                }
            }
            // 1 mm as CP-4: the positions sit km from the origin, where a float step is 0.24 mm.
            Check(worstJoint <= 0.001f, W, "grip on body: pin gap worst " + F(worstJoint) + " m (<= 1 mm)");
            Check(worstRigid <= 1e-4f, W, "grip on the rigid body vs the toggle-off Pose grip: worst " + F(worstRigid) + " m");
        }

        // ── CP-17: CarrierWind's first sample: a start value when nothing was posed yet. A blend from the zero that poses already used,
        // when it arrives late (a new tram base, a load's first pass, a loop box not yet known). No swing step over 0.5 deg per frame ──
        private static void Cp17FirstWind()
        {
            const string W = "carrier-pose/first-wind";
            float2 w = new float2(0.31f, 0.05f);   // step 0's measured sample
            var fresh = new CarrierWind();
            fresh.Set(w, 100.0);
            Check(fresh.At(100.0).Equals(w), W, "a first sample before any pose is not the start value: " + fresh.At(100.0));

            // Late: poses read zero first, then the sample arrives mid-span.
            var late = new CarrierWind();
            Check(late.At(200.0).Equals(float2.zero), W, "an unknown wind is not zero");
            late.Set(w, 200.0);
            Check(late.At(200.0).Equals(float2.zero) && late.At(200.0 + CarrierPose.WindBlendS).Equals(w), W,
                  "a late first sample does not blend from zero to the sample over WindBlendS");
            float worst = 0f, snap = 0f;
            SwingLimits limits = LimitsOf(SkiCatalog.Lifts[0]);
            foreach (float y in Yaws)
            {
                quaternion yaw = Yaw(Tangent(y, 0f), quaternion.identity);
                const float arg = 1.1f;
                var full = new float2(1f, 1f);
                snap = math.max(snap, AngleDeg(CarrierPose.SwingRotation(float2.zero), CarrierPose.SwingRotation(CarrierPose.Swing(w, yaw, arg, full, limits, 1f))));
                quaternion prev = CarrierPose.SwingRotation(CarrierPose.Swing(late.At(200.0 - FrameS), yaw, arg, full, limits, 1f));
                for (double t = 200.0; t <= 200.0 + CarrierPose.WindBlendS + 1.0; t += FrameS)
                {
                    quaternion q = CarrierPose.SwingRotation(CarrierPose.Swing(late.At(t), yaw, arg, full, limits, 1f));
                    worst = math.max(worst, AngleDeg(prev, q));
                    prev = q;
                }
            }
            Check(worst <= MaxStepDeg, W, "a late first sample steps the swing " + F(worst) + " deg in one frame (<= 0.5)");
            Check(snap > MaxStepDeg, W, "the old snap (" + F(snap) + " deg) would not fail this test: the test cannot catch a regression");
            Console.WriteLine("INFO  " + W + ": late first sample worst step " + F(worst) + " deg per frame (the old snap: " + F(snap) + " deg)");

            // A later change still blends from where the swing is (CP-8 through the class).
            late.Set(-w, 300.0);
            Check(math.distance(late.At(300.0), w) < 1e-6f, W, "a change after the first sample jumps");
        }

        // ── CP-18: the roll is ONE world rotation of the whole assembly about the rope point and the rope tangent. Grip and body rotations
        // move by the same world roll, and both origins turn about the rope point. The grip's axis stays along the rope (<= 0.1 deg: the
        // first roll convention, yaw x roll x pitch, tilted it by roll x sin(pitch), 3.8 deg at the FG clamp on 30 deg). The pin joint gap
        // stays 0, and the rope stays in the jaw (with a fore-aft turn on top too). A chair / gondola's base moves no farther at its roll
        // clamp than the old 8 deg pin roll moved it, and as far per unit wind. The tram rolls at most 2 deg, at half its fore-aft ──
        private static void Cp18RollAboutRope()
        {
            const string W = "carrier-pose/roll";
            float3[] axes = { new float3(1f, 0f, 0f), new float3(0f, 1f, 0f), new float3(0f, 0f, 1f) };
            float worstGap = 0f, worstJaw = 0f, worstTogether = 0f, worstAbout = 0f, worstAngle = 0f, worstAxis = 0f;
            foreach (LiftTypeDef d in SkiCatalog.Lifts)
            {
                CarrierPivots pv = d.Pivots;
                SwingLimits limits = LimitsOf(d);
                foreach (float y in Yaws)
                {
                    foreach (float p in Pitches)
                    {
                        float3 rope = new float3(-812.5f, 1210.25f, 655.75f);
                        float3 t = Tangent(y, p);
                        quaternion yaw = Yaw(t, quaternion.identity);
                        CarrierPose.Pose(rope, t, yaw, pv, true, float2.zero, out float3 g0, out quaternion gr0, out float3 b0, out quaternion br0);
                        foreach (float r in new[] { limits.MaxRoll, -limits.MaxRoll, 0.4f * limits.MaxRoll })
                        {
                            // The world roll: r about the world rope tangent (oriented along the yaw's forward), through the rope point.
                            float3 axis = math.normalize(t);
                            if (math.dot(axis, math.rotate(yaw, new float3(0f, 0f, 1f))) < 0f)
                                axis = -axis;
                            quaternion roll = quaternion.AxisAngle(axis, r);
                            foreach (float fa in new[] { 0f, 0.04f, -0.05f })
                            {
                                CarrierPose.Pose(rope, t, yaw, pv, true, new float2(r, fa), out float3 g, out quaternion gr, out float3 b, out quaternion br);
                                worstGap = math.max(worstGap, CarrierPose.PinGap(g, gr, b, br, pv));
                                worstJaw = math.max(worstJaw, math.distance(g + math.rotate(gr, pv.GripRope), rope));
                                float3 fwd = math.rotate(gr, new float3(0f, 0f, 1f));
                                worstAxis = math.max(worstAxis, math.min(AngleDeg(fwd, t), AngleDeg(fwd, -t)));
                                if (fa != 0f)
                                    continue;
                                // Together: vectors, not quaternion angles (AngleDeg of two quaternions carries ~0.06 deg of float noise).
                                foreach (float3 v in axes)
                                {
                                    worstTogether = math.max(worstTogether, math.distance(math.rotate(gr, v), math.rotate(roll, math.rotate(gr0, v))));
                                    worstTogether = math.max(worstTogether, math.distance(math.rotate(br, v), math.rotate(roll, math.rotate(br0, v))));
                                }
                                worstAbout = math.max(worstAbout, math.max(math.distance(g - rope, math.rotate(roll, g0 - rope)),
                                                                           math.distance(b - rope, math.rotate(roll, b0 - rope))));
                                // The grip's pin axis (perpendicular to the rope) turns by exactly the roll.
                                float lateral = AngleDeg(math.rotate(gr, new float3(1f, 0f, 0f)), math.rotate(gr0, new float3(1f, 0f, 0f)));
                                worstAngle = math.max(worstAngle, math.abs(lateral - math.degrees(math.abs(r))));
                            }
                        }
                    }
                }

                // The base excursion on a level rope near the origin (no km float noise): at the clamp and per unit wind.
                float3 near = new float3(10.5f, 20.25f, -30.75f);
                float3 lt = Tangent(0.3f, 0f);
                quaternion lyaw = Yaw(lt, quaternion.identity);
                CarrierPose.Pose(near, lt, lyaw, pv, true, float2.zero, out _, out _, out float3 calm, out _);
                CarrierPose.Pose(near, lt, lyaw, pv, true, new float2(limits.MaxRoll, 0f), out _, out _, out float3 atClamp, out _);
                float excursion = math.distance(atClamp, calm);
                float pinLever = math.length(pv.BodyPin.xy);
                if (d.Mover == MoverKind.Shuttle)
                {
                    Check(limits.MaxRoll > 0f && limits.MaxRoll <= math.radians(2f) + 1e-6f, W, d.Id + ": the tram's roll clamp is " + F(math.degrees(limits.MaxRoll)) + " deg (<= 2)");
                    // Half the fore-aft amplitude, lean and sway: a 45-degree wind (cross = along), unclamped. The sway at sin = cos.
                    float3 fw = math.rotate(lyaw, new float3(0f, 0f, 1f)), rt = math.rotate(lyaw, new float3(1f, 0f, 0f));
                    float2 wind45 = 0.2f * math.normalize(new float2(fw.x + rt.x, fw.z + rt.z));
                    float2 lean = CarrierPose.Swing(wind45, lyaw, 0f, new float2(1f, 0f), limits, 1f);
                    float2 sway = CarrierPose.Swing(wind45, lyaw, 0.25f * math.PI, new float2(0f, 1f), limits, 1f);
                    Check(math.abs(lean.x / lean.y - 0.5f) <= 1e-3f && math.abs(sway.x / sway.y - 0.5f) <= 1e-3f, W,
                          d.Id + ": roll / fore-aft is " + F(lean.x / lean.y) + " (lean), " + F(sway.x / sway.y) + " (sway), want 0.5");
                }
                else
                {
                    // The old convention rolled the body alone about the pin: its base moved |BodyPin.xy| x 2 sin(4 deg) at the 8 deg clamp.
                    float oldAtClamp = pinLever * 2f * math.sin(0.5f * CarrierPose.MaxLateral);
                    Check(excursion <= oldAtClamp + 1e-4f && excursion >= oldAtClamp - 1e-3f, W, d.Id + ": the base moves " + F(excursion) +
                          " m at the " + F(math.degrees(limits.MaxRoll)) + " deg clamp, the old pin roll " + F(oldAtClamp) + " m (equal, never more)");
                    Check(limits.MaxRoll <= CarrierPose.MaxLateral, W, d.Id + ": the roll clamp exceeds the old 8 deg");
                    // Per unit wind (unclamped): the old lean at gain 1 about the pin vs the new one at RollGain about the rope point.
                    float oldLean = CarrierPose.LeanPerWind2 * 0.31f * 0.31f;
                    CarrierPose.Pose(near, lt, lyaw, pv, true, new float2(oldLean * limits.RollGain, 0f), out _, out _, out float3 leaned, out _);
                    float oldMove = pinLever * 2f * math.sin(0.5f * oldLean);
                    float newMove = math.distance(leaned, calm);
                    Check(math.abs(newMove / oldMove - 1f) <= 0.01f, W, d.Id + ": a 0.31 cross wind moves the base " + F(newMove) + " m, the old pin roll " +
                          F(oldMove) + " m (within 1 %)");
                }
                Console.WriteLine("INFO  " + W + ": " + d.Id + " roll clamp " + F(math.degrees(limits.MaxRoll)) + " deg, gain " + F(limits.RollGain) +
                                  ", base excursion at the clamp " + F(excursion) + " m");
            }
            Check(worstGap <= 0.001f, W, "under roll: body pin vs grip pin, worst " + F(worstGap) + " m (<= 1 mm)");
            Check(worstJaw <= 0.001f, W, "under roll: rope point vs the jaw, worst " + F(worstJaw) + " m (<= 1 mm)");
            Check(worstAxis <= 0.1f, W, "under roll: the grip's axis vs the rope tangent, worst " + F(worstAxis) + " deg (<= 0.1)");
            Check(worstTogether <= 1e-5f, W, "grip and body do not move by the same world roll: worst axis error " + F(worstTogether));
            Check(worstAbout <= 0.001f, W, "the rolled assembly is not the unrolled one turned about the rope point: worst " + F(worstAbout) + " m");
            Check(worstAngle <= 0.01f, W, "the grip's pin axis does not turn by the roll angle: worst error " + F(worstAngle) + " deg");
            Console.WriteLine("INFO  " + W + ": under roll the grip axis stays within " + F(worstAxis) + " deg of the rope, the pin gap " + F(worstGap) + " m");
        }

        // ── CP-10: the pairing math inverts vanilla's alignIndex = m x rowCount + row (SubObjectSystem.cs:1742-1757) ──
        private static void Cp10Pairing()
        {
            const string W = "carrier-pose/pairing";
            // The FG line after the surgery: rows 0 / 1 = carriers at x -1.8 / +1.8, rows 2 / 3 = grips at x -1.8 / +1.8.
            float[] rowX = { -1.8f, 1.8f, -1.8f, 1.8f };
            bool[] isGrip = { false, false, true, true };
            int rows = rowX.Length;
            int bad = 0;
            for (int m = 0; m < 40; m++)
            {
                for (int row = 0; row < rows; row++)
                {
                    int index = m * rows + row;
                    if (CarrierPose.AlignSlot(index, rows) != m || CarrierPose.AlignRow(index, rows) != row)
                        bad++;
                }
                // Each carrier finds exactly one grip with the same (m, side).
                for (int row = 0; row < rows; row++)
                {
                    if (isGrip[row])
                        continue;
                    int found = 0;
                    for (int g = 0; g < rows; g++)
                    {
                        if (isGrip[g] && CarrierPose.RowSide(rowX[g]) == CarrierPose.RowSide(rowX[row]))
                            found++;
                    }
                    if (found != 1)
                        bad++;
                }
            }
            Check(bad == 0, W, bad + " pairing mismatch(es)");
            Check(CarrierPose.AlignSlot(5, 0) == -1 && CarrierPose.AlignRow(5, 0) == -1, W, "a zero row count does not answer -1");
            Check(CarrierPose.RowSide(-1.8f) == -1 && CarrierPose.RowSide(1.8f) == 1 && CarrierPose.RowSide(0f) == 0, W, "RowSide");
        }

        // ── CP-11: the sway argument is a continuous function of s (a frame moves it by omega x dt) and wraps into [0, 2 pi) ──
        private static void Cp11SwayArg()
        {
            const string W = "carrier-pose/sway-arg";
            float omega = 2.5f, v = 5f;
            float worst = 0f;
            float prev = CarrierPose.SwayArg(0.0, omega, v, 1f);
            bool inRange = true;
            for (int i = 1; i < 20000; i++)
            {
                double s = i * v * FrameS + 3000.0;   // kilometers into a loop: the double keeps the step exact
                float a = CarrierPose.SwayArg(s, omega, v, 1f);
                inRange &= a >= 0f && a < 2f * math.PI + 1e-5f;
                if (i > 1)
                {
                    float step = math.abs(math.sin(a) - math.sin(prev));
                    worst = math.max(worst, step);
                }
                prev = a;
            }
            Check(inRange, W, "an argument outside [0, 2 pi)");
            Check(worst <= omega * (float)FrameS * 1.001f, W, "sin(arg) steps " + F(worst) + " per frame (bound omega x dt " + F(omega * FrameS) + ")");
        }

        // ── CP-12: the per-entity phase is in [0, 2 pi) and spreads ──
        private static void Cp12SeedPhase()
        {
            const string W = "carrier-pose/seed";
            int[] buckets = new int[8];
            bool inRange = true;
            for (uint i = 1; i <= 4000; i++)
            {
                float p = CarrierPose.SeedPhase(i);
                inRange &= p >= 0f && p < 2f * math.PI;
                buckets[math.clamp((int)(p / (2f * math.PI) * 8f), 0, 7)]++;
            }
            Check(inRange, W, "a phase outside [0, 2 pi)");
            int min = int.MaxValue, max = 0;
            foreach (int b in buckets)
            {
                min = Math.Min(min, b);
                max = Math.Max(max, b);
            }
            Check(min > 350 && max < 650, W, "phases bunch: buckets " + string.Join(",", buckets));
            Check(CarrierPose.SeedPhase(7) == CarrierPose.SeedPhase(7), W, "the phase is not deterministic");
        }

        // ── CP-13: the Options toggle off: the grip keeps the yaw only (level), the body stands rigid ──
        private static void Cp13ToggleOff()
        {
            const string W = "carrier-pose/toggle-off";
            CarrierPivots pv = SkiCatalog.Lifts[1].Pivots;
            float3 rope = new float3(10.5f, 300.25f, -20.75f);
            float3 t = Tangent(0.9f, 0.4f);
            quaternion yaw = Yaw(t, quaternion.identity);
            CarrierPose.Pose(rope, t, yaw, pv, false, float2.zero, out float3 gp, out quaternion gr, out float3 bp, out quaternion br);
            Check(gr.value.Equals(yaw.value) && br.value.Equals(yaw.value), W, "rotations are not the yaw: grip " + gr + ", body " + br);
            float3 wantGrip = rope - math.rotate(yaw, pv.GripRope);
            float3 rigid = rope;
            rigid.y -= pv.RigidDrop;
            Check(math.distance(gp, wantGrip) < 1e-5f && math.distance(bp, rigid) < 1e-4f, W, "grip " + gp + " / body " + bp + " off the level pose");
        }

        // ── CP-14: the constants are sane (placeholders, but within the plan's limits) ──
        private static void Cp14Constants()
        {
            const string W = "carrier-pose/constants";
            Check(math.abs(math.degrees(CarrierPose.MaxLateral) - 8f) < 1e-3f && math.abs(math.degrees(CarrierPose.MaxForeAft) - 3f) < 1e-3f, W,
                  "the clamps are not 8 / 3 deg");
            Check(CarrierPose.WindBlendS == 8.0, W, "the wind blend is not 8 s");
            Check(math.abs(CarrierPose.ForeAftShare - 1f / 3f) < 1e-6f, W, "the fore-aft share is not 1/3");
            // The sway cap leaves the lean at least half of every clamp (Swing clamps the lean to the room the sway leaves).
            foreach (LiftTypeDef d in SkiCatalog.Lifts)
            {
                SwingLimits lim = LimitsOf(d);
                Check(CarrierPose.MaxSway * lim.RollGain <= 0.5f * lim.MaxRoll && CarrierPose.MaxSway * CarrierPose.ForeAftShare <= 0.5f * CarrierPose.MaxForeAft, W,
                      d.Id + ": the sway cap takes more than half a clamp (roll " + F(math.degrees(CarrierPose.MaxSway * lim.RollGain)) + " of " +
                      F(math.degrees(lim.MaxRoll)) + " deg)");
            }
            Check(math.abs(CarrierPose.GustMean - CarrierPose.GustAmp1 - CarrierPose.GustAmp2 - 0.6f) < 1e-6f &&
                  math.abs(CarrierPose.GustMean + CarrierPose.GustAmp1 + CarrierPose.GustAmp2 - 1f) < 1e-6f, W, "the gust does not span [0.6, 1.0]");
            Check(CarrierPose.GustPeriod1S >= 5.0 && CarrierPose.GustPeriod2S <= 20.0 && CarrierPose.GustPeriod1S < CarrierPose.GustPeriod2S, W,
                  "the gust periods leave 5 to 20 s");
            Check(math.abs(math.degrees(CarrierPose.TramMaxRoll) - 2f) < 1e-3f && CarrierPose.TramRollShare == 0.5f, W,
                  "the tram's roll is not 2 deg at half the fore-aft amplitude");
            // A realistic wind leans 1 to 4 deg (the photos tune it). A 0 or a 20 deg lean is a typo.
            float lean = math.degrees(CarrierPose.LeanPerWind2 * 0.31f * 0.31f);
            Check(lean > 0.5f && lean < 4f, W, "a |w| 0.31 cross wind leans " + F(lean) + " deg");
        }
    }
}
