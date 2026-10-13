// ShuttleTests — Catalog Phase C (docs/research/art/assets-2026-10/code-round-3/round-3-contract.md §8.1, loopcache
// group): the pure stateless shuttle profile (ShuttleProfile.cs). Where-tags `shuttle/…`. Oracles from the contract's
// §3.3 table, recomputed by the skeptic (round-3-contract-skeptic claim 10). v 10 m/s, a 0.8 m/s², dwell 60 s unless said.
//   SH-1  TripTimeS: 100 m → 22.360679774997898 (triangle), 125 → 25 (the boundary), 1000 → 112.5, 3000 → 312.5;
//         invalid → 0.
//   SH-2  DistanceAt: 0 → 0, 12.5 → 62.5, 56.25 → 500, T → L, clamps; monotone over 10,000 samples (trapezoid and
//         triangle); s continuous at the ramp ends (≤ 1e-9); speed has no jump there (one-sided differences agree to a·δ:
//         a finite difference cannot resolve 1e-9 in speed, so this is the C¹ check the contract's "≤ 1e-9" can mean).
//   SH-3  PeriodS(1000) = 345; invalid → 0.
//   SH-4  FractionA legs at τ 0 / 59.999 / 60 / 116.25 (u 0.5) / 172.5 / 232.5 / 288.75 (u 0.5) / 344.999; periodicity
//         FractionA(t + P) = FractionA(t) for 1,000 t up to 1e7 s (≤ 1e-9).
//   SH-5  HoldFractionA: τ 100 → 0, 200 → 1, 300 → 1, 10 → 0 (the terminal last reached), the 172.5 boundary.
//   SH-6  cabin B = 1 − A everywhere; with L_A = L_B, s_B = L − s_A; both dock at the same instant.
//   SH-7  TimeSOf(f, ft) == ((double)f + ft) / 60.0 bit for bit (ChairPinSystem.cs:1132-1135); one game day = 262,144
//         frames = 4,369.07 s.
//   SH-8  CapacityPerHour(2, 100, 1000) = 2086.9565217391305, one cabin 1043.4782608695652, 0 cabins / 0 seats → −1;
//         a → ∞: 2,250 at 1 km and 1,000 at 3 km (code-economy A.2's L/v + dwell figures).
//   SH-9  invalid inputs (L, v, a ≤ 0 or NaN, dwell < 0, t NaN / ∞) → u 0, DwellBase (FractionA and HoldFractionA).
//   SH-10 (skeptic F4, additive) QuantizeLengthM rounds to 1 cm; two lengths within one centimetre bucket give the same
//         period bit for bit; the catalog's float tram row (0.8f) stays within 1e-5 s of the double oracle.

using System;
using System.Globalization;
using SkiResortMod.Systems;

namespace LiftBatch
{
    public static class ShuttleTests
    {
        private static Func<bool, string, string, bool> s_Check;

        private const double V = 10.0, A = 0.8, D = 60.0;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Sh1Trip();
            Sh2Distance();
            Sh3Period();
            Sh4Fraction();
            Sh5Hold();
            Sh6CabinB();
            Sh7Clock();
            Sh8Capacity();
            Sh9Invalid();
            Sh10Quantum();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);

        private static void Eq<T>(T expected, T actual, string where, string what)
        {
            Check(System.Collections.Generic.EqualityComparer<T>.Default.Equals(expected, actual), where,
                  what + ": expected " + expected + ", got " + actual);
        }

        private static void Near(double expected, double actual, double tol, string where, string what)
        {
            Check(Math.Abs(expected - actual) <= tol, where, what + ": expected " + expected.ToString("R", CultureInfo.InvariantCulture) +
                  " ± " + tol + ", got " + actual.ToString("R", CultureInfo.InvariantCulture));
        }

        // ================================================================== SH-1

        private static void Sh1Trip()
        {
            const string W = "shuttle/SH-1";
            Near(22.360679774997898, ShuttleProfile.TripTimeS(100, V, A), 1e-12, W, "100 m (triangle, peak √(aL) = 8.94 m/s)");
            Near(25.0, ShuttleProfile.TripTimeS(125, V, A), 1e-12, W, "125 m (the trapezoid boundary v²/a)");
            Near(112.5, ShuttleProfile.TripTimeS(1000, V, A), 1e-12, W, "1000 m");
            Near(212.5, ShuttleProfile.TripTimeS(2000, V, A), 1e-12, W, "2000 m");
            Near(312.5, ShuttleProfile.TripTimeS(3000, V, A), 1e-12, W, "3000 m");
            Eq(0.0, ShuttleProfile.TripTimeS(0, V, A), W, "L 0");
            Eq(0.0, ShuttleProfile.TripTimeS(double.NaN, V, A), W, "L NaN");
            Eq(0.0, ShuttleProfile.TripTimeS(1000, 0, A), W, "v 0");
            Eq(0.0, ShuttleProfile.TripTimeS(1000, V, 0), W, "a 0");
            Eq(0.0, ShuttleProfile.TripTimeS(1000, V, -1), W, "a −1");
            Eq(0.0, ShuttleProfile.TripTimeS(double.PositiveInfinity, V, A), W, "L ∞");
            Check(ShuttleProfile.Valid(1000, V, A, 0) && !ShuttleProfile.Valid(1000, V, A, -1) && !ShuttleProfile.Valid(1000, V, A, double.NaN),
                  W, "Valid: dwell 0 valid, −1 / NaN invalid");
        }

        // ================================================================== SH-2

        private static void Sh2Distance()
        {
            const string W = "shuttle/SH-2";
            Eq(0.0, ShuttleProfile.DistanceAt(0, 1000, V, A), W, "t 0");
            Near(62.5, ShuttleProfile.DistanceAt(12.5, 1000, V, A), 1e-9, W, "t 12.5 (end of the ramp: v²/2a)");
            Near(500.0, ShuttleProfile.DistanceAt(56.25, 1000, V, A), 1e-9, W, "t 56.25 (mid-trip)");
            Near(937.5, ShuttleProfile.DistanceAt(100, 1000, V, A), 1e-9, W, "t 100 (start of the decel ramp)");
            Near(1000.0, ShuttleProfile.DistanceAt(112.5, 1000, V, A), 1e-9, W, "t = T");
            Near(1000.0, ShuttleProfile.DistanceAt(500, 1000, V, A), 1e-9, W, "t > T clamps to L");
            Eq(0.0, ShuttleProfile.DistanceAt(-3, 1000, V, A), W, "t < 0 clamps to 0");
            Near(50.0, ShuttleProfile.DistanceAt(ShuttleProfile.TripTimeS(100, V, A) / 2, 100, V, A), 1e-9, W, "triangle midpoint = L/2");

            foreach (double L in new[] { 1000.0, 100.0, 125.0, 3000.0 })
            {
                double trip = ShuttleProfile.TripTimeS(L, V, A);
                double prev = -1.0;
                bool monotone = true;
                for (int k = 0; k <= 10000; k++)
                {
                    double s = ShuttleProfile.DistanceAt(trip * k / 10000.0, L, V, A);
                    if (s < prev)
                        monotone = false;
                    prev = s;
                }
                Check(monotone, W, "L " + L + ": DistanceAt not monotone over 10,000 samples");
                Near(L, prev, 1e-9, W, "L " + L + ": the last sample");

                // Continuity at the ramp ends (trapezoid: v/a and T − v/a; triangle: T/2).
                double[] joints = L >= V * V / A ? new[] { V / A, trip - V / A } : new[] { trip / 2 };
                foreach (double j in joints)
                {
                    const double eps = 1e-9, delta = 1e-4;
                    double left = ShuttleProfile.DistanceAt(j - eps, L, V, A), right = ShuttleProfile.DistanceAt(j + eps, L, V, A);
                    Check(Math.Abs(right - left) <= 2 * eps * V + 1e-9, W, "L " + L + ": s jumps at t " + j + " (" + left + " → " + right + ")");
                    double sj = ShuttleProfile.DistanceAt(j, L, V, A);
                    double vLeft = (sj - ShuttleProfile.DistanceAt(j - delta, L, V, A)) / delta;
                    double vRight = (ShuttleProfile.DistanceAt(j + delta, L, V, A) - sj) / delta;
                    Check(Math.Abs(vLeft - vRight) <= A * delta + 1e-6, W, "L " + L + ": speed jumps at t " + j + " (" + vLeft + " / " + vRight + ")");
                }
            }
        }

        // ================================================================== SH-3

        private static void Sh3Period()
        {
            const string W = "shuttle/SH-3";
            Near(345.0, ShuttleProfile.PeriodS(1000, V, A, D), 1e-9, W, "PeriodS(1000)");
            Near(164.72135954999578, ShuttleProfile.PeriodS(100, V, A, D), 1e-9, W, "PeriodS(100)");
            Near(745.0, ShuttleProfile.PeriodS(3000, V, A, D), 1e-9, W, "PeriodS(3000)");
            Near(225.0, ShuttleProfile.PeriodS(1000, V, A, 0), 1e-9, W, "PeriodS(1000, dwell 0)");
            Eq(0.0, ShuttleProfile.PeriodS(1000, V, A, -1), W, "PeriodS dwell −1");
            Eq(0.0, ShuttleProfile.PeriodS(0, V, A, D), W, "PeriodS L 0");
            // 345 s = 20,700 frames ≈ 1.9 game hours; ≈ 12.66 round trips a game day (262,144 frames).
            Near(20700.0, 345.0 * ShuttleProfile.FramesPerSecond, 1e-9, W, "one 1 km cycle in frames");
        }

        // ================================================================== SH-4

        private static void Sh4Fraction()
        {
            const string W = "shuttle/SH-4";
            double P = ShuttleProfile.PeriodS(1000, V, A, D);
            object[][] rows =
            {
                new object[] { 0.0, ShuttleLeg.DwellBase, 0.0 },
                new object[] { 59.999, ShuttleLeg.DwellBase, 0.0 },
                new object[] { 60.0, ShuttleLeg.Up, 0.0 },
                new object[] { 116.25, ShuttleLeg.Up, 0.5 },
                new object[] { 172.5, ShuttleLeg.DwellTop, 1.0 },
                new object[] { 232.5, ShuttleLeg.Down, 1.0 },
                new object[] { 288.75, ShuttleLeg.Down, 0.5 },
                new object[] { 344.999, ShuttleLeg.Down, 0.0 },
            };
            foreach (object[] r in rows)
            {
                double tau = (double)r[0];
                for (int cycle = 0; cycle < 3; cycle++)
                {
                    double u = ShuttleProfile.FractionA(tau + cycle * P, 1000, V, A, D, out ShuttleLeg leg);
                    string at = "τ " + tau.ToString(CultureInfo.InvariantCulture) + " (+" + cycle + " P) ";
                    Eq((ShuttleLeg)r[1], leg, W, at + "leg");
                    Near((double)r[2], u, 1e-6, W, at + "u");
                }
            }
            var rng = new Random(20261011);
            double worst = 0.0;
            for (int k = 0; k < 1000; k++)
            {
                double t = rng.NextDouble() * 1e7;
                double u0 = ShuttleProfile.FractionA(t, 1000, V, A, D, out _);
                double u1 = ShuttleProfile.FractionA(t + P, 1000, V, A, D, out _);
                worst = Math.Max(worst, Math.Abs(u1 - u0));
            }
            Check(worst <= 1e-9, W, "periodicity: max |u(t + P) − u(t)| = " + worst.ToString("R", CultureInfo.InvariantCulture) +
                  " over 1,000 t up to 1e7 s");
            // u is continuous through every leg boundary (no jump: the cabins never teleport while running).
            foreach (double edge in new[] { 60.0, 172.5, 232.5, P })
            {
                double before = ShuttleProfile.FractionA(edge - 1e-7, 1000, V, A, D, out _);
                double after = ShuttleProfile.FractionA(edge + 1e-7, 1000, V, A, D, out _);
                Check(Math.Abs(after - before) < 1e-6, W, "u jumps at τ " + edge + ": " + before + " → " + after);
            }
        }

        // ================================================================== SH-5

        private static void Sh5Hold()
        {
            const string W = "shuttle/SH-5";
            object[][] rows =
            {
                new object[] { 100.0, 0.0, ShuttleLeg.DwellBase },
                new object[] { 200.0, 1.0, ShuttleLeg.DwellTop },
                new object[] { 300.0, 1.0, ShuttleLeg.DwellTop },
                new object[] { 10.0, 0.0, ShuttleLeg.DwellBase },
                new object[] { 172.4999, 0.0, ShuttleLeg.DwellBase },
                new object[] { 172.5, 1.0, ShuttleLeg.DwellTop },
                new object[] { 345.0 + 100.0, 0.0, ShuttleLeg.DwellBase },
            };
            foreach (object[] r in rows)
            {
                double u = ShuttleProfile.HoldFractionA((double)r[0], 1000, V, A, D, out ShuttleLeg leg);
                string at = "τ " + ((double)r[0]).ToString(CultureInfo.InvariantCulture) + " ";
                Eq((double)r[1], u, W, at + "u");
                Eq((ShuttleLeg)r[2], leg, W, at + "leg");
            }
        }

        // ================================================================== SH-6

        private static void Sh6CabinB()
        {
            const string W = "shuttle/SH-6";
            const double L = 1000.0;
            double P = ShuttleProfile.PeriodS(L, V, A, D);
            double worst = 0.0;
            bool inRange = true, docksTogether = true;
            for (int k = 0; k <= 3450; k++)
            {
                double t = P * k / 3450.0;
                double uA = ShuttleProfile.FractionA(t, L, V, A, D, out ShuttleLeg leg);
                double uB = 1.0 - uA;   // the mover's rule (C3): cabin B at (1 − u)·L_B
                if (uA < 0 || uA > 1 || uB < 0 || uB > 1)
                    inRange = false;
                worst = Math.Max(worst, Math.Abs(uB * L - (L - uA * L)));
                if ((leg == ShuttleLeg.DwellBase || leg == ShuttleLeg.DwellTop) && !((uA == 0 && uB == 1) || (uA == 1 && uB == 0)))
                    docksTogether = false;
            }
            Check(inRange, W, "u_A or u_B outside [0, 1]");
            Check(worst <= 1e-9, W, "s_B = L − s_A: max error " + worst);
            Check(docksTogether, W, "a dwell with a cabin off its dock");
        }

        // ================================================================== SH-7

        private static void Sh7Clock()
        {
            const string W = "shuttle/SH-7";
            uint[] frames = { 0u, 1u, 59u, 60u, 262144u, 20700u, 123456789u, uint.MaxValue };
            float[] fts = { 0f, 0.25f, 0.5f, 0.999f };
            foreach (uint f in frames)
            {
                foreach (float ft in fts)
                {
                    double want = ((double)f + ft) / 60.0;   // = ChairPinSystem.TimeS (:1132-1135)
                    double got = ShuttleProfile.TimeSOf(f, ft);
                    Check(BitConverter.DoubleToInt64Bits(want) == BitConverter.DoubleToInt64Bits(got), W,
                          "TimeSOf(" + f + ", " + ft.ToString("R", CultureInfo.InvariantCulture) + ") = " +
                          got.ToString("R", CultureInfo.InvariantCulture) + ", ChairPin's clock " + want.ToString("R", CultureInfo.InvariantCulture));
                }
            }
            Near(4369.066666666667, ShuttleProfile.TimeSOf(262144u, 0f), 1e-9, W, "one game day (262,144 frames) in seconds");
            Eq(60.0, ShuttleProfile.FramesPerSecond, W, "FramesPerSecond");
        }

        // ================================================================== SH-8

        private static void Sh8Capacity()
        {
            const string W = "shuttle/SH-8";
            Near(2086.9565217391305, ShuttleProfile.CapacityPerHour(2, 100, 1000, V, A, D), 1e-9, W, "2 cabins × 100 at 1 km");
            Near(1043.4782608695652, ShuttleProfile.CapacityPerHour(1, 100, 1000, V, A, D), 1e-9, W, "1 cabin × 100 at 1 km");
            Near(4371.017832580889, ShuttleProfile.CapacityPerHour(2, 100, 100, V, A, D), 1e-6, W, "2 × 100 at 100 m (§3.3: 4,371.0)");
            Near(4235.294117647059, ShuttleProfile.CapacityPerHour(2, 100, 125, V, A, D), 1e-6, W, "125 m (§3.3: 4,235.3)");
            Near(1321.1009174311926, ShuttleProfile.CapacityPerHour(2, 100, 2000, V, A, D), 1e-6, W, "2000 m (§3.3: 1,321.10)");
            Near(966.4429530201342, ShuttleProfile.CapacityPerHour(2, 100, 3000, V, A, D), 1e-6, W, "3000 m (§3.3: 966.44)");
            Eq(-1.0, ShuttleProfile.CapacityPerHour(0, 100, 1000, V, A, D), W, "0 cabins");
            Eq(-1.0, ShuttleProfile.CapacityPerHour(2, 0, 1000, V, A, D), W, "0 seats");
            Eq(-1.0, ShuttleProfile.CapacityPerHour(2, 100, 1000, V, A, -1), W, "dwell −1");
            Eq(-1.0, ShuttleProfile.CapacityPerHour(2, 100, double.NaN, V, A, D), W, "L NaN");
            Near(2250.0, ShuttleProfile.CapacityPerHour(2, 100, 1000, V, 1e9, D), 1e-3, W, "a → ∞ at 1 km (code-economy A.2: pax × 3600 / (L/v + dwell))");
            Near(1000.0, ShuttleProfile.CapacityPerHour(2, 100, 3000, V, 1e9, D), 1e-3, W, "a → ∞ at 3 km (A.2)");
        }

        // ================================================================== SH-9

        private static void Sh9Invalid()
        {
            const string W = "shuttle/SH-9";
            double[][] bad =
            {
                new[] { 100.0, 0.0, V, A, D },
                new[] { 100.0, -5.0, V, A, D },
                new[] { 100.0, double.NaN, V, A, D },
                new[] { 100.0, 1000.0, 0.0, A, D },
                new[] { 100.0, 1000.0, V, 0.0, D },
                new[] { 100.0, 1000.0, V, double.NaN, D },
                new[] { 100.0, 1000.0, V, A, -1.0 },
                new[] { 100.0, 1000.0, V, A, double.PositiveInfinity },
                new[] { double.NaN, 1000.0, V, A, D },
                new[] { double.PositiveInfinity, 1000.0, V, A, D },
                new[] { double.NegativeInfinity, 1000.0, V, A, D },
            };
            for (int i = 0; i < bad.Length; i++)
            {
                double[] r = bad[i];
                double u = ShuttleProfile.FractionA(r[0], r[1], r[2], r[3], r[4], out ShuttleLeg leg);
                double h = ShuttleProfile.HoldFractionA(r[0], r[1], r[2], r[3], r[4], out ShuttleLeg hleg);
                Check(u == 0.0 && leg == ShuttleLeg.DwellBase && h == 0.0 && hleg == ShuttleLeg.DwellBase, W,
                      "row " + i + ": u " + u + " " + leg + ", hold " + h + " " + hleg + " (want 0, DwellBase)");
            }
            Check(ShuttleProfile.FractionA(-30.0, 1000, V, A, D, out ShuttleLeg negLeg) >= 0.0, W, "negative time (before the epoch) leaves [0, 1]");
            Check(negLeg == ShuttleLeg.Down, W, "t −30 s is the end of the previous cycle's Down leg, got " + negLeg);
        }

        // ================================================================== SH-10

        private static void Sh10Quantum()
        {
            const string W = "shuttle/SH-10";
            Near(1013.2, ShuttleProfile.QuantizeLengthM(1013.2049), 1e-9, W, "1013.2049 → 1013.20");
            Near(1013.21, ShuttleProfile.QuantizeLengthM(1013.2051), 1e-9, W, "1013.2051 → 1013.21");
            Check(double.IsNaN(ShuttleProfile.QuantizeLengthM(double.NaN)), W, "NaN passes through");
            double q1 = ShuttleProfile.QuantizeLengthM(1013.2012), q2 = ShuttleProfile.QuantizeLengthM(1013.2037);
            Check(BitConverter.DoubleToInt64Bits(ShuttleProfile.PeriodS(q1, V, A, D)) ==
                  BitConverter.DoubleToInt64Bits(ShuttleProfile.PeriodS(q2, V, A, D)), W,
                  "two lengths in one centimetre give different periods after quantizing");
            // The catalog row reaches the profile as floats widened to double (0.8f = 0.800000011920929).
            LiftTypeDef tram = SkiCatalog.LiftById("tram");
            if (Check(tram != null, W, "no tram row"))
            {
                Near(345.0, ShuttleProfile.PeriodS(1000, tram.SpeedMps, tram.AccelMps2, tram.DwellS), 1e-5, W, "catalog tram PeriodS(1000)");
                Near(112.5, ShuttleProfile.TripTimeS(1000, tram.SpeedMps, tram.AccelMps2), 1e-5, W, "catalog tram TripTimeS(1000)");
                Check(ShuttleProfile.Valid(1000, tram.SpeedMps, tram.AccelMps2, tram.DwellS), W, "the catalog tram row is not a valid profile");
            }
        }
    }
}
