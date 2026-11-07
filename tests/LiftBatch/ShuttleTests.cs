// Tests for the pure stateless shuttle profile (ShuttleProfile.cs). Where-tags `shuttle/...`, SH-1..SH-12. Values: v 10 m/s,
// a 0.8 m/s^2, dwell 60 s.
// Pinned:
// - TripTimeS (triangle below 125 m, trapezoid above)
// - DistanceAt (continuous, monotone, C1 at the ramp ends)
// - PeriodS
// - FractionA / HoldFractionA legs and periodicity
// - cabin B = 1 - A
// - TimeSOf == ((double)f + ft) / 60.0 bit for bit (one game day = 262,144 frames)
// - CapacityPerHour (a -> infinity gives 2,250 at 1 km and 1,000 at 3 km)
// - invalid inputs -> DwellBase
// - QuantizeLengthM (1 cm buckets)
// SH-11, the tram's terminal reach on LiftLoopTopology: a Shuttle station owns a terminal within MaxDockRunM of a dock. Rope tips
// ~29 / 30 m out close both chains. Rope tips at 31 m and ~60 m stay unclosed. Loop mode and docks-less stations keep the 20 m
// origin radius.
// SH-12, the station list order: LiftLoopTopology.SortedStationOrder sorts the harvest by entity (Index, then Version). So two chunk
// orders of one station set give one signature (the TramShuttle watchdog WARN after an icon clear reordered the chunks). A real
// change of set still changes it.

using System;
using System.Collections.Generic;
using System.Globalization;
using SkiResortMod.Systems;
using Unity.Mathematics;
using Random = System.Random;

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
            Sh11TerminalReach();
            Sh12StationOrder();
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

                // Continuity at the ramp ends (trapezoid: v/a and T - v/a, triangle: T/2).
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

        private static void Sh3Period()
        {
            const string W = "shuttle/SH-3";
            Near(345.0, ShuttleProfile.PeriodS(1000, V, A, D), 1e-9, W, "PeriodS(1000)");
            Near(164.72135954999578, ShuttleProfile.PeriodS(100, V, A, D), 1e-9, W, "PeriodS(100)");
            Near(745.0, ShuttleProfile.PeriodS(3000, V, A, D), 1e-9, W, "PeriodS(3000)");
            Near(225.0, ShuttleProfile.PeriodS(1000, V, A, 0), 1e-9, W, "PeriodS(1000, dwell 0)");
            Eq(0.0, ShuttleProfile.PeriodS(1000, V, A, -1), W, "PeriodS dwell −1");
            Eq(0.0, ShuttleProfile.PeriodS(0, V, A, D), W, "PeriodS L 0");
            // 345 s = 20,700 frames, about 12.66 round trips a game day (262,144 frames).
            Near(20700.0, 345.0 * ShuttleProfile.FramesPerSecond, 1e-9, W, "one 1 km cycle in frames");
        }

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
            // u is continuous through every leg boundary (the cabins never teleport while running).
            foreach (double edge in new[] { 60.0, 172.5, 232.5, P })
            {
                double before = ShuttleProfile.FractionA(edge - 1e-7, 1000, V, A, D, out _);
                double after = ShuttleProfile.FractionA(edge + 1e-7, 1000, V, A, D, out _);
                Check(Math.Abs(after - before) < 1e-6, W, "u jumps at τ " + edge + ": " + before + " → " + after);
            }
        }

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
                double uB = 1.0 - uA; // the mover's rule: cabin B at (1 - u) x L_B
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

        private static void Sh7Clock()
        {
            const string W = "shuttle/SH-7";
            uint[] frames = { 0u, 1u, 59u, 60u, 262144u, 20700u, 123456789u, uint.MaxValue };
            float[] fts = { 0f, 0.25f, 0.5f, 0.999f };
            foreach (uint f in frames)
            {
                foreach (float ft in fts)
                {
                    double want = ((double)f + ft) / 60.0; // = ChairPinSystem.TimeS
                    double got = ShuttleProfile.TimeSOf(f, ft);
                    Check(BitConverter.DoubleToInt64Bits(want) == BitConverter.DoubleToInt64Bits(got), W,
                          "TimeSOf(" + f + ", " + ft.ToString("R", CultureInfo.InvariantCulture) + ") = " +
                          got.ToString("R", CultureInfo.InvariantCulture) + ", ChairPin's clock " + want.ToString("R", CultureInfo.InvariantCulture));
                }
            }
            Near(4369.066666666667, ShuttleProfile.TimeSOf(262144u, 0f), 1e-9, W, "one game day (262,144 frames) in seconds");
            Eq(60.0, ShuttleProfile.FramesPerSecond, W, "FramesPerSecond");
        }

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

        private static void Sh10Quantum()
        {
            const string W = "shuttle/SH-10";
            Near(1013.2, ShuttleProfile.QuantizeLengthM(1013.2049), 1e-9, W, "1013.2049 → 1013.20");
            Near(1013.21, ShuttleProfile.QuantizeLengthM(1013.2051), 1e-9, W, "1013.2051 → 1013.21");
            Check(double.IsNaN(ShuttleProfile.QuantizeLengthM(double.NaN)), W, "NaN passes through");
            double q1 = ShuttleProfile.QuantizeLengthM(1013.2012), q2 = ShuttleProfile.QuantizeLengthM(1013.2037);
            Check(BitConverter.DoubleToInt64Bits(ShuttleProfile.PeriodS(q1, V, A, D)) ==
                  BitConverter.DoubleToInt64Bits(ShuttleProfile.PeriodS(q2, V, A, D)), W,
                  "two lengths in one centimeter give different periods after quantizing");
            // The catalog row reaches the profile as floats widened to double (0.8f = 0.800000011920929).
            LiftTypeDef tram = SkiCatalog.LiftById("tram");
            if (Check(tram != null, W, "no tram row"))
            {
                Near(345.0, ShuttleProfile.PeriodS(1000, tram.SpeedMps, tram.AccelMps2, tram.DwellS), 1e-5, W, "catalog tram PeriodS(1000)");
                Near(112.5, ShuttleProfile.TripTimeS(1000, tram.SpeedMps, tram.AccelMps2), 1e-5, W, "catalog tram TripTimeS(1000)");
                Check(ShuttleProfile.Valid(1000, tram.SpeedMps, tram.AccelMps2, tram.DwellS), W, "the catalog tram row is not a valid profile");
            }
        }

        /// <summary>One synthetic two-rope line in the topology's stand-ins. Station i has owner index 100 + i. The two rope anchors
        /// at each end belong to the line's own nodes (owner 7000 +), not to a station. This is the "line-first" case: the census sees
        /// the stub tip through the stub edge's ConnectedEdge while the chain's PathNode owner is the line's.</summary>
        private sealed class Rig
        {
            public readonly List<LiftLoopTopology.Lane> Lanes = new List<LiftLoopTopology.Lane>();
            public readonly List<LiftLoopTopology.Station> Stations = new List<LiftLoopTopology.Station>();
            public readonly float3[] BaseTip = new float3[2], TopTip = new float3[2];
            /// <summary>LiftLoopTopology.ShuttleDockReach for this build (LiftLoopCache.BuildShuttles sets it true).</summary>
            public bool DockReach = true;

            public LiftLoopTopology Build(LiftLoopTopology.Mode mode)
            {
                var map = new Dictionary<int, int>();
                var t = new LiftLoopTopology { ShuttleDockReach = DockReach };
                t.Build(Lanes, Stations, map, mode);
                return t;
            }

            public void Mirror()
            {
                for (int i = 0; i < Lanes.Count; i++)
                {
                    LiftLoopTopology.Lane l = Lanes[i];
                    l.P0.x = -l.P0.x;
                    l.P1.x = -l.P1.x;
                    l.T0.x = -l.T0.x;
                    l.T1.x = -l.T1.x;
                    Lanes[i] = l;
                }
                for (int i = 0; i < Stations.Count; i++)
                {
                    LiftLoopTopology.Station st = Stations[i];
                    st.Position.x = -st.Position.x;
                    st.DockA.x = -st.DockA.x;
                    st.DockB.x = -st.DockB.x;
                    Stations[i] = st;
                }
            }
        }

        private static float3 ToWorld(float3 origin, float yawDeg, float3 local) =>
            origin + math.mul(quaternion.RotateY(math.radians(yawDeg)), local);

        private static void AddLane(Rig rig, int nodeA, int ownerA, float3 a, int nodeB, int ownerB, float3 b)
        {
            float3 t = b - a;
            rig.Lanes.Add(new LiftLoopTopology.Lane
            {
                StartNode = nodeA, EndNode = nodeB, StartOwner = ownerA, EndOwner = ownerB,
                P0 = a, P1 = b, T0 = t, T1 = t, TypeIndex = 3,
            });
        }

        /// <summary>The catalog tram: base (role Base, yaw 180, axis at its StubTipLocalX) at y 0, top at (0, 400, 1000) yaw 0. It has two
        /// rope lanes per end at lateral +- GaugeHalfM of each station's axis, at rope height AxleM, StubTipLocalZ out. baseShiftZ moves
        /// both base rope anchors along world z (toward the top): the line starts that far past the stub tip. baseTipFromDock (>= 0)
        /// overrides it: both base anchors then sit this far along z from a dock, at dock height.</summary>
        private static Rig TramRig(float baseShiftZ = 0f, float baseTipFromDock = -1f)
        {
            LiftTypeDef tram = SkiCatalog.LiftById("tram");
            StationDef tb = SkiCatalog.StationOf(tram.Index, StationRole.Base), tt = SkiCatalog.StationOf(tram.Index, StationRole.Top);
            var rig = new Rig();
            float3 bPos = new float3(tb.StubTipLocalX, 0f, 0f), tPos = new float3(0f, 400f, 1000f);
            rig.Stations.Add(new LiftLoopTopology.Station
            {
                OwnerIndex = 100, Position = bPos, Role = 0,
                DockA = ToWorld(bPos, 180f, tb.DockLocalA), DockB = ToWorld(bPos, 180f, tb.DockLocalB),
            });
            rig.Stations.Add(new LiftLoopTopology.Station
            {
                OwnerIndex = 101, Position = tPos, Role = 1,
                DockA = ToWorld(tPos, 0f, tt.DockLocalA), DockB = ToWorld(tPos, 0f, tt.DockLocalB),
            });
            for (int k = 0; k < 2; k++)
            {
                float side = k == 0 ? -1f : 1f;
                float3 bTip = ToWorld(bPos, 180f, new float3(tb.StubTipLocalX - side * tram.GaugeHalfM, tram.AxleM, tb.StubTipLocalZ)) + new float3(0f, 0f, baseShiftZ);
                if (baseTipFromDock >= 0f)
                    bTip = (k == 0 ? rig.Stations[0].DockB : rig.Stations[0].DockA) + new float3(0f, 0f, baseTipFromDock);
                float3 tTip = ToWorld(tPos, 0f, new float3(tt.StubTipLocalX + side * tram.GaugeHalfM, tram.AxleM, tt.StubTipLocalZ));
                rig.BaseTip[k] = bTip;
                rig.TopTip[k] = tTip;
                float3 mid = (bTip + tTip) * 0.5f + new float3(0f, 8f, 0f);
                int n0 = 10 * k + 1, n1 = 10 * k + 2, n2 = 10 * k + 3;
                AddLane(rig, n0, 7000 + k, bTip, n1, 8000 + k, mid);
                AddLane(rig, n1, 8000 + k, mid, n2, 7100 + k, tTip);
            }
            return rig;
        }

        private static void Sh11TerminalReach()
        {
            // Reach flag off (a bare topology, LoopCacheTests' default): the tram's 29 / 30 m rope tips stay unclassified.
            Rig offRig = TramRig();
            offRig.DockReach = false;
            LiftLoopTopology off = offRig.Build(LiftLoopTopology.Mode.Shuttle);
            Check(off.UnclosedChainCount == 2 && off.Pairs.Count == 0, "shuttle/SH-11",
                  "(0) flag off: the rope tips close (" + off.UnclosedChainCount + " unclosed)");
            Sh11Body();
        }

        private static void Sh11Body()
        {
            const string W = "shuttle/SH-11";
            LiftTypeDef tram = SkiCatalog.LiftById("tram");
            if (!Check(tram != null && tram.Mover == MoverKind.Shuttle, W, "no tram row"))
                return;

            // (a) Both base rope tips are more than 20 m from the origin (the catalog numbers give 30.4 / 28.9 m). They are 12 m out and ~12 m
            //     from their docks. Both chains close, base 0 -> top 1.
            Rig rig = TramRig();
            for (int k = 0; k < 2; k++)
                Check(math.distance(rig.BaseTip[k], rig.Stations[0].Position) > LiftLoopTopology.StationTerminalRadius, W,
                      "(a) precondition: tip " + k + " is " + math.distance(rig.BaseTip[k], rig.Stations[0].Position) + " m from the origin (want > 20)");
            LiftLoopTopology t = rig.Build(LiftLoopTopology.Mode.Shuttle);
            Eq(2, t.Chains.Count, W, "(a) chains");
            Eq(0, t.UnclosedChainCount, W, "(a) unclosed chains (" + string.Join(" | ", t.Notes) + ")");
            if (Check(t.Pairs.Count == 1 && t.Pairs[0].Status == LiftLoopTopology.PairStatus.Ok, W, "(a) no Ok pair"))
            {
                LiftLoopTopology.Pair p = t.Pairs[0];
                Check(p.Base == 0 && p.Top == 1 && p.A.StartStation == 0 && p.A.EndStation == 1 && p.B.StartStation == 0 && p.B.EndStation == 1,
                      W, "(a) not base 0 -> top 1");
                Check(p.ABaseDock >= 0 && p.BBaseDock >= 0 && p.ATopDock >= 0 && p.BTopDock >= 0, W, "(a) a dock was refused");
                Check(p.ABaseRunM > 5f && p.ABaseRunM < 20f, W, "(a) base dock run " + p.ABaseRunM + " m (want the 12 m stub-to-dock run)");
            }
            Check(t.Diags.Count == 2 && t.Diags[0].StartHow.StartsWith("dock ") && t.Diags[1].StartHow.StartsWith("dock "), W,
                  "(a) the how strings: " + (t.Diags.Count > 0 ? t.Diags[0].StartHow : "none"));

            // (b) The same graph as a Loop (FG / DQ / gondola rules): the 20 m radius does not change, and the text is the original.
            LiftLoopTopology loop = rig.Build(LiftLoopTopology.Mode.Loop);
            Eq(2, loop.UnclosedChainCount, W, "(b) Loop mode: chains still unclosed");
            bool loopText = loop.Diags.Count == 2;
            for (int c = 0; c < loop.Diags.Count; c++)
                loopText &= loop.Diags[c].StartHow.StartsWith("none (owner 700") && loop.Diags[c].StartHow.EndsWith("m)") &&
                            !loop.Diags[c].StartHow.Contains("dock");
            Check(loopText, W, "(b) Loop mode text: " + (loop.Diags.Count > 0 ? loop.Diags[0].StartHow : "none"));

            // (c) x mirrored (stub rows, docks and station move together): the reach follows the docks.
            Rig mirrored = TramRig();
            mirrored.Mirror();
            LiftLoopTopology tm = mirrored.Build(LiftLoopTopology.Mode.Shuttle);
            Check(tm.UnclosedChainCount == 0 && tm.Pairs.Count == 1 && tm.Pairs[0].Status == LiftLoopTopology.PairStatus.Ok, W,
                  "(c) mirrored tram: unclosed " + tm.UnclosedChainCount + ", pairs " + tm.Pairs.Count);

            // (d) A terminal ~60 m from the base origin (the line's first node 48 m past the stub tip): over both reaches.
            Rig far = TramRig(baseShiftZ: 48f);
            Check(math.distance(far.BaseTip[0], far.Stations[0].Position) > 55f, W, "(d) precondition: " + math.distance(far.BaseTip[0], far.Stations[0].Position));
            LiftLoopTopology tf = far.Build(LiftLoopTopology.Mode.Shuttle);
            Eq(2, tf.UnclosedChainCount, W, "(d) 60 m: chains unclosed");
            Check(tf.Pairs.Count == 0, W, "(d) 60 m: no pair");
            bool noted = tf.Diags.Count == 2;
            for (int c = 0; c < tf.Diags.Count; c++)
                noted &= tf.Diags[c].StartStation == -1 && tf.Diags[c].StartHow.StartsWith("none (owner 700") && tf.Diags[c].StartHow.Contains("nearest dock");
            Check(noted, W, "(d) 60 m: per-terminal diagnostic missing: " + (tf.Diags.Count > 0 ? tf.Diags[0].StartHow : "none"));

            // (e) The reach is MaxDockRunM from a dock: 29 m closes, 31 m does not.
            LiftLoopTopology t29 = TramRig(baseTipFromDock: 29f).Build(LiftLoopTopology.Mode.Shuttle);
            Check(t29.UnclosedChainCount == 0 && t29.Pairs.Count == 1 && t29.Pairs[0].Status == LiftLoopTopology.PairStatus.Ok, W,
                  "(e) 29 m from the dock: unclosed " + t29.UnclosedChainCount + " (" + string.Join(" | ", t29.Notes) + ")");
            LiftLoopTopology t31 = TramRig(baseTipFromDock: 31f).Build(LiftLoopTopology.Mode.Shuttle);
            Eq(2, t31.UnclosedChainCount, W, "(e) 31 m from the dock: unclosed");

            // (f) FG / DQ / gondola: Loop mode, their catalog stub tips, unowned anchors. The 20 m origin radius classifies them as before
            //     ("pos N.Nm"), and the pair closes. 25 m out stays unclassified. One docks-less Shuttle probe (fixedGrip) too.
            foreach (string id in new[] { "fixedGrip", "detachable", "gondola" })
            {
                LiftTypeDef def = SkiCatalog.LiftById(id);
                if (!Check(def != null, W, "(f) no " + id + " row"))
                    continue;
                StationDef sb = SkiCatalog.StationOf(def.Index, StationRole.Base), st = SkiCatalog.StationOf(def.Index, StationRole.Top);
                foreach (LiftLoopTopology.Mode mode in new[] { LiftLoopTopology.Mode.Loop, LiftLoopTopology.Mode.Shuttle })
                {
                    if (mode == LiftLoopTopology.Mode.Shuttle && id != "fixedGrip")
                        continue;
                    var g = new Rig();
                    float3 bPos = new float3(0f, 0f, 0f), tPos = new float3(0f, 100f, 600f);
                    for (int i = 0; i < 2; i++)
                        g.Stations.Add(new LiftLoopTopology.Station
                        {
                            OwnerIndex = 100 + i, Position = i == 0 ? bPos : tPos, Role = -1,
                            DockA = new float3(float.NaN), DockB = new float3(float.NaN),
                        });
                    for (int k = 0; k < 2; k++)
                    {
                        float side = k == 0 ? -1f : 1f;
                        float3 bTip = ToWorld(bPos, 180f, new float3(side * def.GaugeHalfM, sb.WheelLocal.y, sb.StubTipLocalZ));
                        float3 tTip = ToWorld(tPos, 0f, new float3(-side * def.GaugeHalfM, st.WheelLocal.y, st.StubTipLocalZ));
                        float3 mid = (bTip + tTip) * 0.5f;
                        AddLane(g, 10 * k + 1, 7000 + k, bTip, 10 * k + 2, 8000 + k, mid);
                        AddLane(g, 10 * k + 2, 8000 + k, mid, 10 * k + 3, 7100 + k, tTip);
                    }
                    LiftLoopTopology lt = g.Build(mode);
                    Check(lt.Diags.Count == 2 && lt.Diags[0].StartHow.StartsWith("pos ") && lt.Diags[0].EndHow.StartsWith("pos "), W,
                          "(f) " + id + " " + mode + ": terminals " + (lt.Diags.Count > 0 ? lt.Diags[0].StartHow + " / " + lt.Diags[0].EndHow : "none"));
                    Check(lt.UnclosedChainCount == 0, W, "(f) " + id + " " + mode + ": unclosed chains " + lt.UnclosedChainCount);

                    // The same line with the base anchors 25 m further out along the line: over the 20 m radius, no docks to reach with.
                    var far25 = new Rig();
                    far25.Stations.AddRange(g.Stations);
                    for (int i = 0; i < g.Lanes.Count; i++)
                    {
                        LiftLoopTopology.Lane l = g.Lanes[i];
                        if (l.StartOwner == 7000 || l.StartOwner == 7001)
                            l.P0 += new float3(0f, 0f, 25f);
                        far25.Lanes.Add(l);
                    }
                    LiftLoopTopology lf = far25.Build(mode);
                    Check(lf.UnclosedChainCount == 2 && lf.Diags.Count == 2 && lf.Diags[0].StartHow.StartsWith("none (owner 700") && !lf.Diags[0].StartHow.Contains("dock"),
                          W, "(f) " + id + " " + mode + " 25 m out: unclosed " + lf.UnclosedChainCount + " / " + (lf.Diags.Count > 0 ? lf.Diags[0].StartHow : "none"));
                }
            }
        }

        // ---- SH-12: the station list is in entity order, so the shuttle signature does not depend on chunk order ----

        /// <summary>The station-dependent part of TramShuttleSystem.LogRebuild's signature: the station count, and per pair the list
        /// indices the pair notes print plus the base and top entities (Ent(run.Base) / Ent(run.Top)), then the topology's own notes.</summary>
        private static string StationSignature(Rig rig, IList<int> version)
        {
            LiftLoopTopology t = rig.Build(LiftLoopTopology.Mode.Shuttle);
            var sb = new System.Text.StringBuilder();
            sb.Append(rig.Stations.Count).Append(" station(s)");
            foreach (LiftLoopTopology.Pair p in t.Pairs)
            {
                sb.Append(" | stations (").Append(p.StationLo).Append(',').Append(p.StationHi).Append("): ").Append(p.Status);
                if (p.Base >= 0 && p.Top >= 0)
                    sb.Append(", base = station ").Append(p.Base).Append(' ').Append(rig.Stations[p.Base].OwnerIndex).Append(':').Append(version[p.Base])
                      .Append(" -> top station ").Append(p.Top).Append(' ').Append(rig.Stations[p.Top].OwnerIndex).Append(':').Append(version[p.Top]);
            }
            foreach (string n in t.Notes)
                sb.Append(" | ").Append(n);
            return sb.ToString();
        }

        /// <summary>The harvest's sort: rig.Stations and version reordered by LiftLoopTopology.SortedStationOrder.</summary>
        private static void SortRig(Rig rig, List<int> version)
        {
            var idx = new List<int>();
            foreach (LiftLoopTopology.Station s in rig.Stations)
                idx.Add(s.OwnerIndex);
            var order = new List<int>();
            LiftLoopTopology.SortedStationOrder(idx, version, order);
            var stations = new List<LiftLoopTopology.Station>(rig.Stations);
            var ver = new List<int>(version);
            for (int k = 0; k < order.Count; k++)
            {
                rig.Stations[k] = stations[order[k]];
                version[k] = ver[order[k]];
            }
        }

        private static void Sh12StationOrder()
        {
            const string W = "shuttle/SH-12";

            // (a) The order function: any shuffle of one entity set sorts to the same entity sequence. Version breaks an Index tie.
            //     Equal keys keep list order. Empty and single lists are fine.
            var idx = new List<int> { 412, 97, 412, 5, 1030, 97 };
            var ver = new List<int> { 3, 1, 2, 9, 1, 4 };
            var expected = new List<string> { "5:9", "97:1", "97:4", "412:2", "412:3", "1030:1" };
            var rng = new Random(1234);
            bool allSame = true;
            string firstBad = null;
            for (int round = 0; round < 200; round++)
            {
                var perm = new List<int> { 0, 1, 2, 3, 4, 5 };
                for (int i = perm.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    int tmp = perm[i];
                    perm[i] = perm[j];
                    perm[j] = tmp;
                }
                var pi = new List<int>();
                var pv = new List<int>();
                foreach (int k in perm)
                {
                    pi.Add(idx[k]);
                    pv.Add(ver[k]);
                }
                var order = new List<int>();
                LiftLoopTopology.SortedStationOrder(pi, pv, order);
                var got = new List<string>();
                foreach (int o in order)
                    got.Add(pi[o] + ":" + pv[o]);
                if (string.Join(" ", got) != string.Join(" ", expected))
                {
                    allSame = false;
                    firstBad = firstBad ?? string.Join(" ", got);
                }
            }
            Check(allSame, W, "(a) 200 shuffles do not sort to one sequence: " + firstBad);
            var dupOrder = new List<int>();
            LiftLoopTopology.SortedStationOrder(new List<int> { 7, 7, 3 }, new List<int> { 1, 1, 1 }, dupOrder);
            Check(string.Join(",", dupOrder) == "2,0,1", W, "(a) equal keys do not keep list order: " + string.Join(",", dupOrder));
            var emptyOrder = new List<int> { 9 };
            LiftLoopTopology.SortedStationOrder(new List<int>(), new List<int>(), emptyOrder);
            Check(emptyOrder.Count == 0, W, "(a) empty list leaves a stale order");

            // (b) The bug: the same two stations listed in the other chunk order read as a different shuttle set (the watchdog WARN).
            Rig fwd = TramRig();
            var fwdVer = new List<int> { 1, 1 };
            Rig rev = TramRig();
            rev.Stations.Reverse();
            var revVer = new List<int> { 1, 1 };
            string sigFwd = StationSignature(fwd, fwdVer), sigRev = StationSignature(rev, revVer);
            Check(sigFwd != sigRev, W, "(b) precondition: the unsorted lists should read differently (" + sigFwd + " / " + sigRev + ")");
            Check(sigFwd.Contains("Ok, base = station 0 100:1 -> top station 1 101:1"), W, "(b) forward signature: " + sigFwd);

            // (c) Sorted by entity, both orders give one signature, byte for byte, and the same base / top entities.
            SortRig(fwd, fwdVer);
            SortRig(rev, revVer);
            string sortedFwd = StationSignature(fwd, fwdVer), sortedRev = StationSignature(rev, revVer);
            Check(sortedFwd == sortedRev, W, "(c) sorted orders differ: " + sortedFwd + " / " + sortedRev);
            Check(sortedRev.Contains("Ok, base = station 0 100:1 -> top station 1 101:1"), W, "(c) base / top entities after the sort: " + sortedRev);

            // (d) A real change of set still changes the signature:
            //     - a station gone
            //     - a station added (it sorts first, so the other stations' indices move too)
            //     - a recycled entity (same Index, new Version) as the base
            Rig gone = TramRig();
            gone.Stations.RemoveAt(1);
            var goneVer = new List<int> { 1 };
            SortRig(gone, goneVer);
            Check(StationSignature(gone, goneVer) != sortedFwd, W, "(d) a station gone left the signature unchanged");

            Rig added = TramRig();
            var addedVer = new List<int> { 1, 1 };
            added.Stations.Add(new LiftLoopTopology.Station { OwnerIndex = 50, Position = new float3(900f, 0f, -900f), Role = 0 });
            addedVer.Add(1);
            SortRig(added, addedVer);
            string sigAdded = StationSignature(added, addedVer);
            Check(sigAdded != sortedFwd, W, "(d) a station added left the signature unchanged");

            Rig recycled = TramRig();
            var recycledVer = new List<int> { 2, 1 };
            SortRig(recycled, recycledVer);
            string sigRecycled = StationSignature(recycled, recycledVer);
            Check(sigRecycled != sortedFwd && sigRecycled.Contains("base = station 0 100:2"), W, "(d) a recycled base entity left the signature unchanged: " + sigRecycled);
        }
    }
}
