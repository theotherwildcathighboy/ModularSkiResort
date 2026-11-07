// Tests for the snow groomer's pure rules (GroomerRules.cs) and the centerline of hand-drawn rings (GroomerCenterline.cs).
// GradeBatch GR-30 tests generated ribbons, against the generator's own centerline.
//   GM-1 restraint and path source
//   GM-2 season gate
//   GM-3 trip: speed, stops, pivots, no jumps
//   GM-4 phase and clock
//   GM-5 ground plane and smoothing
//   GM-6 pose and local forward axis
//   GM-7 path lookup
//   GM-8 hand-drawn shapes: rectangle, bend, neck, branch, bulge
//   GM-9 degenerate rings: never a wild path
// Oracles are literal hand values (the comments show the arithmetic) and geometric facts of the shape under test.

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using Unity.Mathematics;
using G = SkiResortMod.Systems.GroomerRules;

namespace ResortBatch
{
    public static class GroomerTests
    {
        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Restraint();
            Season();
            Trip();
            PhaseAndClock();
            Ground();
            Pose();
            PathLookup();
            HandDrawn();
            Degenerate();
        }

        private static void C(bool ok, string where, string msg) => s_Check(ok, where, msg);

        private static bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol;

        // ---- GM-1 ---------------------------------------------------------------------------------------------------------------------

        private static void Restraint()
        {
            C(G.MaxGroomersPerTrack == 1, "GM-1 one per track", $"{G.MaxGroomersPerTrack}");
            C(G.SourceFor((int)RunKind.CatTrack) != null, "GM-1 cat track", "a cat track has a path source");
            foreach (RunKind kind in new[] { RunKind.Natural, RunKind.Groomed, RunKind.Snowmaking, RunKind.Night })
                C(G.SourceFor((int)kind) == null, "GM-1 groomed runs out of scope", $"{kind} has a path source");
            C(G.SourceFor(-1) == null, "GM-1 unknown kind", "no source");

            C(G.Wanted(true, true, true, true), "GM-1 wanted", "all four true");
            C(!G.Wanted(false, true, true, true), "GM-1 option off", "wanted");
            C(!G.Wanted(true, false, true, true), "GM-1 unbound", "wanted");
            C(!G.Wanted(true, true, false, true), "GM-1 closed", "wanted");
            C(!G.Wanted(true, true, true, false), "GM-1 no path", "wanted");

            C(G.Decide(true, false, 0) == GroomerAction.Spawn, "GM-1 spawn", "wanted, none yet");
            C(G.Decide(true, true, 1) == GroomerAction.None, "GM-1 never two", "a second prop on a track");
            C(G.Decide(false, true, 1) == GroomerAction.Remove, "GM-1 remove", "not wanted, has one");
            C(G.Decide(false, false, 0) == GroomerAction.None, "GM-1 idle", "nothing to do");
            C(G.Decide(true, false, G.MaxGroomers - 1) == GroomerAction.Spawn, "GM-1 below the cap", "last slot");
            C(G.Decide(true, false, G.MaxGroomers) == GroomerAction.None, "GM-1 cap", "over the cap");
            C(G.Decide(false, true, G.MaxGroomers + 5) == GroomerAction.Remove, "GM-1 removal ignores the cap", "stuck over the cap");
        }

        // ---- GM-2 ---------------------------------------------------------------------------------------------------------------------

        private static void Season()
        {
            // The first look uses the midpoint, the hub's own line (GetRunSnow's NextState(Unknown, share))
            RunSnowState s = RunSnowState.Unknown;
            C(G.SeasonStep(ref s, 0.50f) && s == RunSnowState.Open, "GM-2 first look open", $"{s}");
            s = RunSnowState.Unknown;
            C(!G.SeasonStep(ref s, 0.49f) && s == RunSnowState.Closed, "GM-2 first look closed", $"{s}");
            C(RunSnowRules.NextState(RunSnowState.Unknown, 0.50f) == RunSnowState.Open && RunSnowRules.NextState(RunSnowState.Unknown, 0.49f) == RunSnowState.Closed,
                "GM-2 same line as the hub", "the hub's first label moved");

            // Open: closes where the hub calls it closed, below the midpoint
            s = RunSnowState.Open;
            C(G.SeasonStep(ref s, 0.50f), "GM-2 open holds at the midpoint", $"{s}");
            C(!G.SeasonStep(ref s, 0.49f), "GM-2 closes below the midpoint", $"{s}");
            // Closed: reopens only at 0.60, so a share hovering at the midpoint does not pop the machine in and out
            C(!G.SeasonStep(ref s, 0.59f), "GM-2 stays closed below 0.60", $"{s}");
            C(G.SeasonStep(ref s, 0.60f), "GM-2 reopens at 0.60", $"{s}");
            // Summer and winter ends, and NaN
            s = RunSnowState.Open;
            C(!G.SeasonStep(ref s, 0f), "GM-2 summer", $"{s}");
            C(G.SeasonStep(ref s, 1f), "GM-2 winter", $"{s}");
            C(!G.SeasonStep(ref s, float.NaN) && s == RunSnowState.Closed, "GM-2 NaN closes", $"{s}");
        }

        // ---- GM-3 ---------------------------------------------------------------------------------------------------------------------

        private static void Trip()
        {
            // 400 m: 400 / 3.33 + 3 = 123.12 s of drive. One way 123.12 + 2.5 + 12 + 2.5 = 140.12. The cycle is 280.24 s
            C(Near(G.DriveSeconds(400.0), 400.0 / 3.33 + 3.0, 1e-3), "GM-3 drive seconds", $"{G.DriveSeconds(400.0):F3}");
            C(Near(G.PeriodSeconds(400.0), 2.0 * (400.0 / 3.33 + 3.0 + 17.0), 1e-3), "GM-3 period", $"{G.PeriodSeconds(400.0):F3}");
            C(Near(G.PivotS, 12f, 1e-4), "GM-3 pivot 180 / 15", $"{G.PivotS}");
            C(G.SpeedMps * 3.6f >= 10f && G.SpeedMps * 3.6f <= 20f, "GM-3 real speed band", $"{G.SpeedMps * 3.6f:F1} km/h");

            foreach (double length in new[] { 400.0, 2000.0, 31.0, 6.0 })
            {
                foreach (int sign in new[] { 1, -1 })
                    WalkTrip(length, sign);
            }

            // Leg by leg, in order, for 400 m
            double d = G.DriveSeconds(400.0);
            double half = d + 17.0;
            GroomerLeg[] legs = { GroomerLeg.DriveOut, GroomerLeg.StopAtEnd, GroomerLeg.PivotAtEnd, GroomerLeg.WaitAtEnd, GroomerLeg.DriveBack, GroomerLeg.StopAtStart, GroomerLeg.PivotAtStart, GroomerLeg.WaitAtStart };
            double[] times = { 1.0, d + 1.0, d + 3.0, d + 15.5, half + 1.0, half + d + 1.0, half + d + 3.0, half + d + 15.5 };
            for (int i = 0; i < legs.Length; i++)
                C(G.TripAt(times[i], 400.0, 1).Leg == legs[i], "GM-3 leg order", $"t {times[i]:F1}: {G.TripAt(times[i], 400.0, 1).Leg}, expected {legs[i]}");

            // A stop stands at the end, a pivot turns on the spot, and the turn ends a half turn further
            C(Near(G.TripAt(d + 1.0, 400.0, 1).S, 400.0, 1e-9), "GM-3 stops at the end", "S");
            C(Near(G.TripAt(d + 8.5, 400.0, 1).S, 400.0, 1e-9), "GM-3 pivots on the spot", "S");
            C(Near(G.TripAt(d + 8.5, 400.0, 1).TurnRad, Math.PI / 2.0, 1e-4), "GM-3 half of the half turn at mid pivot", $"{G.TripAt(d + 8.5, 400.0, 1).TurnRad}");
            C(Near(G.TripAt(d + 8.5, 400.0, -1).TurnRad, -Math.PI / 2.0, 1e-4), "GM-3 the other sense", $"{G.TripAt(d + 8.5, 400.0, -1).TurnRad}");
            C(Near(G.TripAt(half + 1.0, 400.0, 1).TurnRad, Math.PI, 1e-4), "GM-3 drives back turned round", $"{G.TripAt(half + 1.0, 400.0, 1).TurnRad}");
            C(Near(G.TripAt(half + d + 1.0, 400.0, 1).S, 0.0, 1e-9), "GM-3 stops at the start", "S");

            C(Near(G.Ease(0f), 0.0, 1e-6) && Near(G.Ease(1f), 1.0, 1e-6) && Near(G.Ease(0.5f), 0.5, 1e-6), "GM-3 ease", "0, 0.5, 1");
            C(Near(G.Ease(-1f), 0.0, 1e-6) && Near(G.Ease(2f), 1.0, 1e-6), "GM-3 ease clamps", "outside 0..1");
        }

        /// <summary>The whole cycle in 1/60 s steps: no jump in position, no flip in heading, speed within the limit, S within the path.</summary>
        private static void WalkTrip(double length, int sign)
        {
            double period = G.PeriodSeconds(length);
            const double dt = 1.0 / 60.0;
            GroomerTrip last = G.TripAt(0.0, length, sign);
            double maxSpeed = 0.0, maxTurnRate = 0.0, maxS = 0.0, minS = double.MaxValue;
            int steps = (int)(period / dt) + 2;
            for (int i = 1; i <= steps; i++)
            {
                GroomerTrip trip = G.TripAt(i * dt, length, sign);
                maxSpeed = Math.Max(maxSpeed, Math.Abs(trip.S - last.S) / dt);
                double turn = trip.TurnRad - last.TurnRad;
                turn = Math.Abs(Math.IEEERemainder(turn, 2.0 * Math.PI));   // a full turn at the wrap is no flip
                maxTurnRate = Math.Max(maxTurnRate, turn / dt);
                maxS = Math.Max(maxS, trip.S);
                minS = Math.Min(minS, trip.S);
                last = trip;
            }
            string tag = $"GM-3 walk {length:F0} m, sense {sign}";
            C(maxSpeed <= G.SpeedMps * 1.001, tag + " speed", $"{maxSpeed:F3} m/s");
            C(maxTurnRate <= 1.5 * math.radians(G.PivotDegPerS) * 1.01, tag + " pivot rate", $"{math.degrees((float)maxTurnRate):F2} deg/s");
            C(minS >= 0.0 && maxS <= length, tag + " stays on the path", $"{minS:F3}..{maxS:F3}");
            C(Near(maxS, length, 1e-6) && Near(minS, 0.0, 1e-6), tag + " reaches both ends", $"{minS:F3}..{maxS:F3}");
        }

        // ---- GM-4 ---------------------------------------------------------------------------------------------------------------------

        private static void PhaseAndClock()
        {
            double period = G.PeriodSeconds(400.0);
            // A pure function of the clock: the same time gives the same place, one period later too, and a negative time wraps
            GroomerTrip a = G.TripAt(77.5, 400.0, 1), b = G.TripAt(77.5, 400.0, 1), c = G.TripAt(77.5 + 3.0 * period, 400.0, 1), n = G.TripAt(77.5 - period, 400.0, 1);
            C(a.S == b.S && a.TurnRad == b.TurnRad, "GM-4 same clock, same place", $"{a.S} {b.S}");
            C(Near(a.S, c.S, 1e-6), "GM-4 periodic", $"{a.S} {c.S}");
            C(Near(a.S, n.S, 1e-6), "GM-4 negative time wraps", $"{a.S} {n.S}");
            // A long-lived clock: a year of play at 3x stays exact in double
            double late = 3.0 * 3600.0 * 24.0 * 365.0;
            C(G.TripAt(late, 400.0, 1).S >= 0.0 && G.TripAt(late, 400.0, 1).S <= 400.0, "GM-4 long clock", $"{G.TripAt(late, 400.0, 1).S}");

            C(G.PhaseFor(0u, period) == 0.0, "GM-4 phase 0", "seed 0");
            C(G.PhaseFor(0xFFFFu, period) < period && G.PhaseFor(0xFFFFu, period) > 0.99 * period, "GM-4 phase inside the period", $"{G.PhaseFor(0xFFFFu, period)}");
            C(G.PhaseFor(0x10000u, period) == 0.0, "GM-4 phase takes the low bits", "seed 0x10000");
            C(G.PivotSignFor(0u) == 1 && G.PivotSignFor(0x10000u) == -1, "GM-4 pivot sense", $"{G.PivotSignFor(0u)} {G.PivotSignFor(0x10000u)}");

            C(Near(G.YawOf(new float2(0f, 1f)), 0.0, 1e-6), "GM-4 yaw 0 along +z", $"{G.YawOf(new float2(0f, 1f))}");
            C(Near(G.YawOf(new float2(1f, 0f)), Math.PI / 2.0, 1e-5), "GM-4 yaw 90 along +x", $"{G.YawOf(new float2(1f, 0f))}");
        }

        // ---- GM-5 ---------------------------------------------------------------------------------------------------------------------

        private static void Ground()
        {
            var heights = new float[G.FootprintSamples];

            // Offsets: 5 along over 7 m, 3 across over 4 m, centered
            G.FootprintOffset(0, 0, out float u0, out float v0);
            G.FootprintOffset(G.FootprintSamplesLong - 1, G.FootprintSamplesWide - 1, out float u1, out float v1);
            C(Near(u0, -3.5, 1e-5) && Near(v0, -2.0, 1e-5) && Near(u1, 3.5, 1e-5) && Near(v1, 2.0, 1e-5), "GM-5 footprint corners", $"{u0} {v0} {u1} {v1}");

            // The fit recovers a plane exactly: h = 100 + 0.25 u - 0.1 v
            Fill(heights, (u, v) => 100f + 0.25f * u - 0.1f * v);
            C(G.FitPlane(heights, out float h, out float sl, out float sw), "GM-5 fit", "false");
            C(Near(h, 100.0, 1e-3) && Near(sl, 0.25, 1e-4) && Near(sw, -0.1, 1e-4), "GM-5 plane recovered", $"{h} {sl} {sw}");

            // A single raised sample (a terrain grid bump of 0.6 m) moves the height by 0.6 / 15 = 0.04 m, not 0.6 m
            Fill(heights, (u, v) => 100f);
            heights[7] = 100.6f;
            G.FitPlane(heights, out h, out sl, out sw);
            C(Near(h, 100.04, 1e-3), "GM-5 a bump is averaged", $"{h}");
            C(Math.Abs(sl) < 1e-4f && Math.Abs(sw) < 1e-4f, "GM-5 the center bump does not tilt", $"{sl} {sw}");

            // A non-finite sample refuses
            heights[3] = float.NaN;
            C(!G.FitPlane(heights, out _, out _, out _), "GM-5 NaN refuses", "fit");

            // Footprint points: yaw 0 = nose +z, right +x. Yaw 90 = nose +x, right -z
            float2 p = G.FootprintPoint(new float2(10f, 20f), 0f, 2f, 1f);
            C(Near(p.x, 11.0, 1e-5) && Near(p.y, 22.0, 1e-5), "GM-5 footprint point yaw 0", $"{p}");
            p = G.FootprintPoint(new float2(10f, 20f), math.PI / 2f, 2f, 1f);
            C(Near(p.x, 12.0, 1e-5) && Near(p.y, 19.0, 1e-5), "GM-5 footprint point yaw 90", $"{p}");

            // Smoothing: dt 0 holds, one tau covers 1 - 1/e, a long step lands, a frame-rate change changes little
            C(G.Smooth(1f, 5f, 0.0, 0.2f) == 1f, "GM-5 paused holds", $"{G.Smooth(1f, 5f, 0.0, 0.2f)}");
            C(G.Smooth(1f, 5f, -1.0, 0.2f) == 1f, "GM-5 backward clock holds", "value");
            C(Near(G.Smooth(0f, 1f, 0.2, 0.2f), 1.0 - Math.Exp(-1.0), 1e-5), "GM-5 one tau", $"{G.Smooth(0f, 1f, 0.2, 0.2f)}");
            C(Near(G.Smooth(0f, 1f, 100.0, 0.2f), 1.0, 1e-6), "GM-5 a long step lands", $"{G.Smooth(0f, 1f, 100.0, 0.2f)}");
            float fine = 0f, coarse = 0f;
            for (int i = 0; i < 6; i++)
                fine = G.Smooth(fine, 1f, 1.0 / 60.0, 0.2f);
            coarse = G.Smooth(coarse, 1f, 6.0 / 60.0, 0.2f);
            C(Near(fine, coarse, 1e-4), "GM-5 independent of the frame rate", $"{fine} {coarse}");

            // The ground state: first step snaps, small steps smooth, a big jump snaps, slopes clamp, paused holds
            var g = default(GroomerGround);
            G.StepGround(ref g, 100f, 0.2f, -0.1f, 1.0 / 60.0);
            C(g.Known && g.Height == 100f && g.SlopeLong == 0.2f && g.SlopeWide == -0.1f, "GM-5 first step snaps", $"{g.Height}");
            G.StepGround(ref g, 100.5f, 0.2f, -0.1f, 1.0 / 60.0);
            C(g.Height > 100f && g.Height < 100.5f, "GM-5 small step smooths", $"{g.Height}");
            float held = g.Height;
            G.StepGround(ref g, 110f, 0.2f, -0.1f, 0.0);
            C(g.Height == 110f, "GM-5 big jump snaps, even paused", $"{g.Height}");
            held = g.Height;
            G.StepGround(ref g, 110.5f, 0.3f, 0.3f, 0.0);
            C(g.Height == held && g.SlopeLong == 0.2f, "GM-5 paused holds the state", $"{g.Height}");
            G.StepGround(ref g, 110f, 9f, -9f, 100.0);
            C(Near(g.SlopeLong, G.MaxSlope, 1e-4) && Near(g.SlopeWide, -G.MaxSlope, 1e-4), "GM-5 a cliff clamps", $"{g.SlopeLong} {g.SlopeWide}");

            // A climb at speed: the height lag stays under 0.15 m on a 25 % grade (0.25 x 3.33 x 0.12 = 0.1 m)
            g = default;
            float last = 0f;
            for (int i = 0; i < 600; i++)
            {
                last = 0.25f * G.SpeedMps * (i / 60f);
                G.StepGround(ref g, last, 0.25f, 0f, 1.0 / 60.0);
            }
            C(last - g.Height <= 0.15f && last - g.Height > 0f, "GM-5 climb lag", $"{last - g.Height:F3} m");
        }

        private static void Fill(float[] heights, Func<float, float, float> f)
        {
            for (int i = 0; i < G.FootprintSamplesLong; i++)
            {
                for (int j = 0; j < G.FootprintSamplesWide; j++)
                {
                    G.FootprintOffset(i, j, out float u, out float v);
                    heights[i * G.FootprintSamplesWide + j] = f(u, v);
                }
            }
        }

        // ---- GM-6 ---------------------------------------------------------------------------------------------------------------------

        private static void Pose()
        {
            float3 z = new float3(0f, 0f, 1f), y = new float3(0f, 1f, 0f), x = new float3(1f, 0f, 0f);

            // Flat, yaw 0: identity with forward = +Z
            quaternion q = G.PoseRotation(0f, 0f, 0f, 0f);
            C(NearV(math.rotate(q, z), z), "GM-6 flat nose", $"{math.rotate(q, z)}");
            C(NearV(math.rotate(q, y), y), "GM-6 flat up", $"{math.rotate(q, y)}");
            C(NearV(math.rotate(q, x), x), "GM-6 flat right", $"{math.rotate(q, x)}");

            // Yaw 90: the nose along +x, the right side along -z
            q = G.PoseRotation(math.PI / 2f, 0f, 0f, 0f);
            C(NearV(math.rotate(q, z), x), "GM-6 yaw 90 nose", $"{math.rotate(q, z)}");
            C(NearV(math.rotate(q, x), -z), "GM-6 yaw 90 right", $"{math.rotate(q, x)}");

            // A local forward axis other than +Z: a mesh whose nose is +X still points its nose along the yaw
            float axis = math.radians(90f);
            q = G.PoseRotation(0f, 0f, 0f, axis);
            C(NearV(math.rotate(q, x), z), "GM-6 local forward +X", $"{math.rotate(q, x)}");
            q = G.PoseRotation(math.radians(30f), 0f, 0f, axis);
            float2 d = new float2(math.sin(math.radians(30f)), math.cos(math.radians(30f)));
            C(NearV(math.rotate(q, x), new float3(d.x, 0f, d.y)), "GM-6 local forward +X at yaw 30", $"{math.rotate(q, x)}");
            C(G.LocalForwardYawDeg == 0f, "GM-6 SkiGroomerA forward is +Z", $"{G.LocalForwardYawDeg}");

            // Climbing at slope 0.25 with yaw 0: the nose rises 0.25 per meter, up stays up the normal
            q = G.PoseRotation(0f, 0.25f, 0f, 0f);
            float3 nose = math.rotate(q, z);
            C(Near(nose.y / math.length(nose.xz), 0.25, 1e-4), "GM-6 pitch", $"{nose}");
            C(math.rotate(q, y).y > 0.9f, "GM-6 up stays up", $"{math.rotate(q, y)}");
            // Side slope 0.2 toward the right, nose along +z: the right side rises, the mesh stays a rigid rotation
            q = G.PoseRotation(0f, 0.25f, 0.2f, 0f);
            float3 right = math.rotate(q, x);
            C(right.y > 0.15f, "GM-6 roll", $"{right}");
            C(Near(math.length(q.value), 1.0, 1e-4), "GM-6 unit quaternion", $"{math.length(q.value)}");
            C(Near(math.dot(right, math.rotate(q, z)), 0.0, 1e-4), "GM-6 rigid", "right and nose stay square");

            // Any yaw and the steepest clamp stays finite
            for (float yaw = -7f; yaw < 7f; yaw += 0.37f)
            {
                q = G.PoseRotation(yaw, G.MaxSlope, -G.MaxSlope, 0f);
                C(math.all(math.isfinite(q.value)) && math.rotate(q, y).y > 0.3f, "GM-6 extreme pose", $"yaw {yaw}: {q.value}");
            }
        }

        private static bool NearV(float3 a, float3 b) => math.distance(a, b) < 1e-3f;

        // ---- GM-7 ---------------------------------------------------------------------------------------------------------------------

        private static void PathLookup()
        {
            var path = new GroomerPath(new[] { new float2(0f, 0f), new float2(0f, 10f), new float2(0f, 20f), new float2(10f, 20f), new float2(20f, 20f) }, 10f, 8f);
            C(Near(path.LengthM, 40.0, 1e-5), "GM-7 length", $"{path.LengthM}");
            C(math.all(path.At(-5.0) == new float2(0f, 0f)), "GM-7 clamps at the start", $"{path.At(-5.0)}");
            C(math.all(path.At(99.0) == new float2(20f, 20f)), "GM-7 clamps at the end", $"{path.At(99.0)}");
            C(NearF2(path.At(15.0), new float2(0f, 15f)), "GM-7 inside a step", $"{path.At(15.0)}");
            C(NearF2(path.At(40.0), new float2(20f, 20f)), "GM-7 exact end", $"{path.At(40.0)}");
            C(NearF2(path.Direction(5.0, 4f), new float2(0f, 1f)), "GM-7 heading along a leg", $"{path.Direction(5.0, 4f)}");
            // At the bend s = 20 the chord runs from (0, 16) to (4, 20): 45 degrees
            C(NearF2(path.Direction(20.0, 4f), math.normalize(new float2(1f, 1f))), "GM-7 heading through a bend", $"{path.Direction(20.0, 4f)}");
            // Clamped at the ends: still a direction
            C(NearF2(path.Direction(0.0, 4f), new float2(0f, 1f)), "GM-7 heading at the start", $"{path.Direction(0.0, 4f)}");
            C(NearF2(path.Direction(40.0, 4f), new float2(1f, 0f)), "GM-7 heading at the end", $"{path.Direction(40.0, 4f)}");
            // Stepping through the bend the heading never jumps more than the chord allows: 90 degrees over 8 m at 0.1 m per step = 1.13 degrees
            float2 last = path.Direction(0.0, 4f);
            float worst = 0f;
            for (double s = 0.1; s <= 40.0; s += 0.1)
            {
                float2 dir = path.Direction(s, 4f);
                worst = math.max(worst, math.acos(math.clamp(math.dot(last, dir), -1f, 1f)));
                last = dir;
            }
            C(math.degrees(worst) < 2f, "GM-7 heading is smooth", $"{math.degrees(worst):F2} deg per 0.1 m");
        }

        private static bool NearF2(float2 a, float2 b) => math.distance(a, b) < 1e-3f;

        // ---- GM-8 ---------------------------------------------------------------------------------------------------------------------

        private static float2[] Rectangle(float length, float width)
        {
            return new[] { new float2(0f, -width / 2f), new float2(length, -width / 2f), new float2(length, width / 2f), new float2(0f, width / 2f) };
        }

        private static void HandDrawn()
        {
            // A four-node rectangle: the shape a player draws by hand. Without the subdivision its one diagonal gives no path at all.
            GroomerPath p = GroomerCenterline.Build(Rectangle(400f, 8f), out GroomerPathFail fail);
            if (C2(p != null, "GM-8 rectangle", $"{fail}"))
            {
                C(p.LengthM > 400f - 20f && p.LengthM <= 400f, "GM-8 rectangle length", $"{p.LengthM}");
                C(Near(p.WidthM, 8.0, 0.5), "GM-8 rectangle width", $"{p.WidthM}");
                float off = 0f;
                for (int i = 0; i < p.Points.Length; i++)
                    off = math.max(off, math.abs(p.Points[i].y));
                C(off < 0.5f, "GM-8 rectangle on center", $"{off:F3} m off");   // the worst is at the trimmed end, where the fan to the corners dies out
                C(Near(p.StepM, GroomerCenterline.PathStepM, 0.05), "GM-8 rectangle step", $"{p.StepM}");
                UniformStep(p, "GM-8 rectangle");
            }

            // Winding does not matter: the reversed ring gives the same line
            float2[] cw = Rectangle(400f, 8f);
            Array.Reverse(cw);
            p = GroomerCenterline.Build(cw, out fail);
            C(p != null && math.abs(p.Points[p.Points.Length / 2].y) < 0.3f, "GM-8 clockwise ring", $"{fail}");

            // A closing duplicate and doubled nodes change nothing
            float2[] rect = Rectangle(300f, 12f);
            var doubled = new[] { rect[0], rect[1], rect[1], rect[2], rect[3], rect[3], rect[0] };
            p = GroomerCenterline.Build(doubled, out fail);
            C(p != null && math.abs(p.Points[p.Points.Length / 2].y) < 0.3f, "GM-8 duplicates", $"{fail}");

            // An L: 300 m east, then 300 m north, 10 m wide. The path stays within 5 m of the axis and turns the corner.
            float2[] ell =
            {
                new float2(0f, -5f), new float2(305f, -5f), new float2(305f, 300f), new float2(295f, 300f), new float2(295f, 5f), new float2(0f, 5f),
            };
            p = GroomerCenterline.Build(ell, out fail);
            if (C2(p != null, "GM-8 L", $"{fail}"))
            {
                float worst = 0f;
                for (int i = 0; i < p.Points.Length; i++)
                {
                    float2 q = p.Points[i];
                    worst = math.max(worst, math.min(math.abs(q.y), math.abs(q.x - 300f)));
                }
                C(worst < 3f, "GM-8 L on the axis", $"{worst:F2} m off");
                C(p.LengthM > 560f && p.LengthM <= 605f, "GM-8 L length", $"{p.LengthM}");
                AllInside(p, ell, "GM-8 L");
                SmoothHeading(p, "GM-8 L");
            }

            // A neck: 16 m wide with a 3 m pinch in the middle. The path passes through the pinch, centered.
            float2[] neck =
            {
                new float2(0f, -8f), new float2(195f, -8f), new float2(200f, -1.5f), new float2(205f, -8f), new float2(400f, -8f),
                new float2(400f, 8f), new float2(205f, 8f), new float2(200f, 1.5f), new float2(195f, 8f), new float2(0f, 8f),
            };
            p = GroomerCenterline.Build(neck, out fail);
            if (C2(p != null, "GM-8 neck", $"{fail}"))
            {
                float at = NearestY(p, 200f);
                C(math.abs(at) < 1f, "GM-8 neck centered", $"{at:F2} m off at the pinch");
                C(p.LengthM > 360f, "GM-8 neck goes through", $"{p.LengthM}");
                AllInside(p, neck, "GM-8 neck");
                SmoothHeading(p, "GM-8 neck");
            }

            // A branch: a 400 m bar with a 60 m side road in the middle. The route takes the bar and never turns into the stub.
            float2[] branch =
            {
                new float2(0f, -6f), new float2(400f, -6f), new float2(400f, 6f), new float2(206f, 6f),
                new float2(206f, 66f), new float2(194f, 66f), new float2(194f, 6f), new float2(0f, 6f),
            };
            p = GroomerCenterline.Build(branch, out fail);
            if (C2(p != null, "GM-8 branch", $"{fail}"))
            {
                float high = float.MinValue;
                for (int i = 0; i < p.Points.Length; i++)
                    high = math.max(high, p.Points[i].y);
                C(high < 6f, "GM-8 branch ignores the stub", $"reaches y {high:F1}");
                C(p.LengthM > 350f, "GM-8 branch takes the bar", $"{p.LengthM}");
                AllInside(p, branch, "GM-8 branch");
            }

            // A one-sided bulge: 12 m wide with a 25 m bay. The path may lean into it but stays inside and has no spike.
            float2[] bulge =
            {
                new float2(0f, -6f), new float2(400f, -6f), new float2(400f, 6f), new float2(230f, 6f),
                new float2(215f, 31f), new float2(185f, 31f), new float2(170f, 6f), new float2(0f, 6f),
            };
            p = GroomerCenterline.Build(bulge, out fail);
            if (C2(p != null, "GM-8 bulge", $"{fail}"))
            {
                C(p.LengthM > 350f && p.LengthM < 440f, "GM-8 bulge length", $"{p.LengthM}");
                C(math.abs(NearestY(p, 50f)) < 1f && math.abs(NearestY(p, 350f)) < 1f, "GM-8 bulge returns to center", $"{NearestY(p, 50f):F2} {NearestY(p, 350f):F2}");
                AllInside(p, bulge, "GM-8 bulge");
                SmoothHeading(p, "GM-8 bulge");
            }

            // An unevenly drawn ribbon: side nodes at random spacing, one side wobbling a little. Fixed seed.
            var rnd = new Unity.Mathematics.Random(12345u);
            var a = new List<float2>();
            var b = new List<float2>();
            float x = 0f;
            while (x < 600f)
            {
                a.Add(new float2(x, -7f + rnd.NextFloat(-1f, 1f)));
                x += rnd.NextFloat(5f, 40f);
            }
            x = 600f;
            while (x > 0f)
            {
                b.Add(new float2(x, 7f + rnd.NextFloat(-1f, 1f)));
                x -= rnd.NextFloat(5f, 40f);
            }
            a.AddRange(b);
            float2[] wobble = a.ToArray();
            p = GroomerCenterline.Build(wobble, out fail);
            if (C2(p != null, "GM-8 uneven ribbon", $"{fail}"))
            {
                float off = 0f;
                for (int i = 0; i < p.Points.Length; i++)
                    off = math.max(off, math.abs(p.Points[i].y));
                C(off < 2.5f, "GM-8 uneven ribbon on center", $"{off:F2} m off");
                AllInside(p, wobble, "GM-8 uneven ribbon");
                SmoothHeading(p, "GM-8 uneven ribbon");
            }
        }

        private static bool C2(bool ok, string where, string msg)
        {
            C(ok, where, msg);
            return ok;
        }

        private static float NearestY(GroomerPath p, float x)
        {
            float best = float.MaxValue, y = 0f;
            for (int i = 0; i < p.Points.Length; i++)
            {
                float d = math.abs(p.Points[i].x - x);
                if (d < best)
                {
                    best = d;
                    y = p.Points[i].y;
                }
            }
            return y;
        }

        private static void UniformStep(GroomerPath p, string tag)
        {
            for (int i = 1; i < p.Points.Length; i++)
            {
                if (!Near(math.distance(p.Points[i - 1], p.Points[i]), p.StepM, 0.05))
                {
                    C(false, tag + " uniform step", $"{math.distance(p.Points[i - 1], p.Points[i])} vs {p.StepM} at {i}");
                    return;
                }
            }
            C(true, tag + " uniform step", "");
        }

        private static void AllInside(GroomerPath p, float2[] ring, string tag)
        {
            int outside = 0;
            for (int i = 0; i < p.Points.Length; i++)
            {
                bool inside = false;
                for (int k = 0, j = ring.Length - 1; k < ring.Length; j = k++)
                {
                    float2 u = ring[k], v = ring[j];
                    if ((u.y > p.Points[i].y) != (v.y > p.Points[i].y) && p.Points[i].x < (v.x - u.x) * (p.Points[i].y - u.y) / (v.y - u.y) + u.x)
                        inside = !inside;
                }
                if (!inside)
                    outside++;
            }
            C(outside == 0, tag + " inside", $"{outside} point(s) outside");
        }

        /// <summary>The heading the machine would really follow: no spike or flip anywhere on the drive. A corner of 90 degrees over the 8 m chord and a
        /// 0.25 m step is 2.8 degrees per step at most. Anything much above that is a wobble.</summary>
        private static void SmoothHeading(GroomerPath p, string tag)
        {
            float2 last = p.Direction(0.0, G.HeadingChordHalfM);
            float worst = 0f;
            for (double s = 0.25; s <= p.LengthM; s += 0.25)
            {
                float2 dir = p.Direction(s, G.HeadingChordHalfM);
                worst = math.max(worst, math.acos(math.clamp(math.dot(last, dir), -1f, 1f)));
                last = dir;
            }
            C(math.degrees(worst) < 8f, tag + " heading", $"{math.degrees(worst):F1} deg per 0.25 m");
        }

        // ---- GM-9 ---------------------------------------------------------------------------------------------------------------------

        private static void Degenerate()
        {
            Refused(null, "null", GroomerPathFail.NoOutline);
            Refused(new float2[0], "empty", GroomerPathFail.NoOutline);
            Refused(new[] { new float2(0f, 0f), new float2(10f, 0f) }, "two nodes", GroomerPathFail.NoOutline);
            Refused(new[] { new float2(0f, 0f), new float2(0f, 0f), new float2(0f, 0f), new float2(0f, 0f) }, "one point", GroomerPathFail.NoOutline);
            Refused(new[] { new float2(0f, 0f), new float2(100f, 0f), new float2(200f, 0f), new float2(300f, 0f) }, "collinear", GroomerPathFail.NoOutline);
            Refused(new[] { new float2(0f, 0f), new float2(float.NaN, 0f), new float2(10f, 10f), new float2(0f, 10f) }, "NaN node", GroomerPathFail.NotFinite);
            Refused(new[] { new float2(0f, 0f), new float2(float.PositiveInfinity, 0f), new float2(10f, 10f), new float2(0f, 10f) }, "infinite node", GroomerPathFail.NotFinite);
            Refused(Rectangle(4f, 2f), "a sliver of area", GroomerPathFail.NoArea);
            // A bow tie crosses itself: an even one has no net area, an uneven one is caught as a crossing
            Refused(new[] { new float2(0f, 0f), new float2(200f, 20f), new float2(200f, 0f), new float2(0f, 20f) }, "even bow tie", GroomerPathFail.NoArea);
            Refused(new[] { new float2(0f, 0f), new float2(200f, 30f), new float2(200f, 0f), new float2(0f, 20f) }, "uneven bow tie", GroomerPathFail.NotSimple);
            // Too large for the O(n^2) steps
            var many = new float2[GroomerCenterline.MaxOutlineNodes + 1];
            for (int i = 0; i < many.Length; i++)
                many[i] = new float2(math.cos(i * 0.001f) * 1000f, math.sin(i * 0.001f) * 1000f);
            Refused(many, "too many nodes", GroomerPathFail.TooManyNodes);
            // Blobs have no direction: a square, a disc
            Refused(Rectangle(200f, 200f), "square", GroomerPathFail.NotACorridor);
            var disc = new float2[48];
            for (int i = 0; i < disc.Length; i++)
                disc[i] = new float2(math.cos(i * 2f * math.PI / disc.Length) * 80f, math.sin(i * 2f * math.PI / disc.Length) * 80f);
            Refused(disc, "disc", GroomerPathFail.NotACorridor);
            // Too short to drive: 30 m of road after the trim is the minimum
            Refused(Rectangle(30f, 8f), "too short", GroomerPathFail.TooShort);

            // A triangle has no corridor, only a corner-to-corner stretch. Any answer must be inside it and short of a wild path
            float2[] tri = { new float2(0f, -10f), new float2(300f, 0f), new float2(0f, 10f) };
            GroomerPath p = GroomerCenterline.Build(tri, out GroomerPathFail fail);
            C(p != null || fail != GroomerPathFail.None, "GM-9 triangle", "null path without a reason");
            if (p != null)
                AllInside(p, tri, "GM-9 triangle");

            // The build removes a long spike on a track (zero width out and back), and the track still has its path
            float2[] spike =
            {
                new float2(0f, -6f), new float2(200f, -6f), new float2(200f, -86f), new float2(200f, -6f), new float2(400f, -6f), new float2(400f, 6f), new float2(0f, 6f),
            };
            p = GroomerCenterline.Build(spike, out fail);
            C(p != null && math.abs(p.Points[p.Points.Length / 2].y) < 0.5f, "GM-9 spike", $"{fail}");

            // Determinism: the same ring gives the same path to the bit
            GroomerPath x = GroomerCenterline.Build(Rectangle(500f, 20f), out _);
            GroomerPath y = GroomerCenterline.Build(Rectangle(500f, 20f), out _);
            bool same = x != null && y != null && x.Points.Length == y.Points.Length;
            for (int i = 0; same && i < x.Points.Length; i++)
                same = math.all(x.Points[i] == y.Points[i]);
            C(same, "GM-9 deterministic", "two builds differ");
        }

        private static void Refused(float2[] ring, string name, GroomerPathFail expected)
        {
            GroomerPath p = GroomerCenterline.Build(ring, out GroomerPathFail fail);
            C(p == null && fail == expected, "GM-9 " + name, $"path {(p == null ? "null" : "built")}, reason {fail}, expected {expected}");
        }
    }
}
