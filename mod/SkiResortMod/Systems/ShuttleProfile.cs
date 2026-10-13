// ShuttleProfile — the PURE, stateless motion of a jig-back tram (Catalog Phase C, docs/research/art/assets-2026-10/
// code-round-3/round-3-contract.md §3, loopcache group; code-economy §A.4 "a stateless s(t): trapezoid speed profile +
// dwell; cabin B at L − s_A"). PURE FILE: `using System;`, `Unity.Mathematics` only. Linked into tests/LiftBatch
// (ShuttleTests SH-1..SH-10) and tests/ResortBatch (HubRules' shuttle rows, rules group).
//
// THE PROFILE (double throughout). Cruise speed v, symmetric acceleration / deceleration a, path L (dock to dock), dwell
// D at each terminal. Ramps together cover d_ad = v²/a:
//   * L ≥ d_ad (trapezoid): trip T = L/v + v/a; s(t) = ½at² up to v/a, v²/(2a) + v(t − v/a) up to T − v/a, then
//     L − ½a(T − t)².
//   * L < d_ad (triangle): T = 2√(L/a), peak √(aL) < v; s(t) = ½at² up to T/2, then L − ½a(T − t)².
//   Continuous in s and speed, monotone. The tram (v 10, a 0.8, D 60; SkiCatalog's tram row): 1 km → trip 112.5 s,
//   cycle 345 s; 100 m → triangle, 22.36 s; the boundary is 125 m.
// THE CYCLE. P = 2(T + D); τ = t − P·⌊t / P⌋ (clamped to [0, P)). Cabin A: τ < D → u 0 (DwellBase); τ < D + T →
// u = s(τ − D)/L (Up); τ < 2D + T → 1 (DwellTop); else 1 − s(τ − 2D − T)/L (Down). Cabin B is 1 − u (both dock at the
// same instant: one haul rope). HOLD (C6, no power): u = τ < D + T ? 0 : 1, the terminal of cabin A's latest dwell.
// Invalid input (L, v, a ≤ 0 or not finite, D < 0 or not finite, t not finite) → u 0, DwellBase.
// THE CLOCK. TimeSOf is ChairPin's clock bit for bit (ChairPinSystem.cs:1132-1135: (frameIndex + frameTime) / 60 in
// double, RenderingSystem's frame counter): frozen on pause, smooth at any speed, resnapped to the saved frame on load.
// 60 frames = 1 s of movement at 1×; one game day = 262,144 frames = 4,369 s (DECISIONS 152: movement runs about 20×
// slower than the clock), so a 1 km tram does about 12.7 round trips a game day.
// LENGTH QUANTUM (round-3-contract-skeptic F4): τ depends on P, so a rebuild that moves L by δ shifts a cabin by about
// (t / P) · 2δ / v — at t 1e6 s, 1 mm moves it ≈ 5.8 m. QuantizeLengthM rounds a path length to 1 cm before it reaches the
// profile, so float-noise rebuilds (identical topology, last-bit differences) give the same P.

using System;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>The mover's gate result per shuttle (round-3-contract C6).</summary>
    public enum ShuttleState : byte { Parked = 0, Running = 1, Held = 2 }

    /// <summary>Cabin A's leg (cabin B is on the mirrored leg).</summary>
    public enum ShuttleLeg : byte { DwellBase = 0, Up = 1, DwellTop = 2, Down = 3 }

    /// <summary>The mover's per-shuttle view for the hub (seam S3).</summary>
    public struct ShuttleView
    {
        public int Cabins;          // Shuttle carriers assigned to this shuttle (0..2)
        public int CabinsOnPath;    // = Cabins while the path exists (Running or Held), else 0
        public double PathM;        // mean dock-to-dock path length; 0 without a path
        public ShuttleState State;
        public ShuttleLeg Leg;
        public double FractionA;    // 0 = cabin A at the base dock … 1 = at the top dock
        public float TripS, CycleS; // TripTimeS(PathM), PeriodS(PathM)
    }

    public static class ShuttleProfile
    {
        /// <summary>ChairPinSystem.cs:1132-1135: TimeS = (frameIndex + frameTime) / 60.</summary>
        public const double FramesPerSecond = 60.0;

        /// <summary>Path lengths are rounded to this before the profile (skeptic F4): 1 cm.</summary>
        public const double LengthQuantumM = 0.01;

        /// <summary>ChairPin's clock, bit for bit: ((double)frameIndex + frameTime) / 60.</summary>
        public static double TimeSOf(uint frameIndex, float frameTime) => ((double)frameIndex + frameTime) / FramesPerSecond;

        /// <summary>Finite, L, v, a &gt; 0, dwell ≥ 0.</summary>
        public static bool Valid(double lengthM, double speedMps, double accelMps2, double dwellS)
        {
            return Finite(lengthM) && Finite(speedMps) && Finite(accelMps2) && Finite(dwellS) &&
                   lengthM > 0.0 && speedMps > 0.0 && accelMps2 > 0.0 && dwellS >= 0.0;
        }

        /// <summary>Dock-to-dock seconds (acceleration included, dwell excluded); 0 when !Valid(…, 0).</summary>
        public static double TripTimeS(double lengthM, double speedMps, double accelMps2)
        {
            if (!Valid(lengthM, speedMps, accelMps2, 0.0))
                return 0.0;
            double rampsM = speedMps * speedMps / accelMps2;
            if (lengthM >= rampsM)
                return lengthM / speedMps + speedMps / accelMps2;
            return 2.0 * Math.Sqrt(lengthM / accelMps2);
        }

        /// <summary>Metres travelled tS seconds after leaving a dock (tS clamped to [0, trip]); 0 when invalid.</summary>
        public static double DistanceAt(double tS, double lengthM, double speedMps, double accelMps2)
        {
            double trip = TripTimeS(lengthM, speedMps, accelMps2);
            if (!(trip > 0.0) || !Finite(tS))
                return 0.0;
            double t = tS < 0.0 ? 0.0 : tS > trip ? trip : tS;
            double rampS = speedMps / accelMps2;
            if (lengthM >= speedMps * speedMps / accelMps2)
            {
                // Trapezoid: accelerate, cruise, decelerate.
                if (t <= rampS)
                    return 0.5 * accelMps2 * t * t;
                if (t <= trip - rampS)
                    return 0.5 * speedMps * rampS + speedMps * (t - rampS);
            }
            else if (t <= 0.5 * trip)
            {
                // Triangle: accelerate to the midpoint, then decelerate.
                return 0.5 * accelMps2 * t * t;
            }
            double r = trip - t;
            return lengthM - 0.5 * accelMps2 * r * r;
        }

        /// <summary>One full cycle: 2 × (trip + dwell); 0 when invalid.</summary>
        public static double PeriodS(double lengthM, double speedMps, double accelMps2, double dwellS)
        {
            if (!Valid(lengthM, speedMps, accelMps2, dwellS))
                return 0.0;
            return 2.0 * (TripTimeS(lengthM, speedMps, accelMps2) + dwellS);
        }

        /// <summary>Cabin A's fraction of the path at <paramref name="timeS"/> (0 = base dock, 1 = top dock) and its leg;
        /// cabin B is 1 − u. Invalid input → 0, DwellBase.</summary>
        public static double FractionA(double timeS, double lengthM, double speedMps, double accelMps2, double dwellS,
                                       out ShuttleLeg leg)
        {
            leg = ShuttleLeg.DwellBase;
            if (!Valid(lengthM, speedMps, accelMps2, dwellS) || !Finite(timeS))
                return 0.0;
            double trip = TripTimeS(lengthM, speedMps, accelMps2);
            double tau = Tau(timeS, 2.0 * (trip + dwellS));
            if (tau < dwellS)
                return 0.0;
            if (tau < dwellS + trip)
            {
                leg = ShuttleLeg.Up;
                return Clamp01(DistanceAt(tau - dwellS, lengthM, speedMps, accelMps2) / lengthM);
            }
            if (tau < 2.0 * dwellS + trip)
            {
                leg = ShuttleLeg.DwellTop;
                return 1.0;
            }
            leg = ShuttleLeg.Down;
            return Clamp01(1.0 - DistanceAt(tau - 2.0 * dwellS - trip, lengthM, speedMps, accelMps2) / lengthM);
        }

        /// <summary>C6 Held: 0 or 1, the terminal cabin A last reached (τ &lt; dwell + trip → the base, else the top); the
        /// leg is DwellBase / DwellTop. Invalid input → 0, DwellBase.</summary>
        public static double HoldFractionA(double timeS, double lengthM, double speedMps, double accelMps2, double dwellS,
                                           out ShuttleLeg leg)
        {
            leg = ShuttleLeg.DwellBase;
            if (!Valid(lengthM, speedMps, accelMps2, dwellS) || !Finite(timeS))
                return 0.0;
            double trip = TripTimeS(lengthM, speedMps, accelMps2);
            double tau = Tau(timeS, 2.0 * (trip + dwellS));
            if (tau < dwellS + trip)
                return 0.0;
            leg = ShuttleLeg.DwellTop;
            return 1.0;
        }

        /// <summary>Riders an hour: cabins × seats × 3600 / (2 × (trip + dwell)) (C5: a cabin carries one load per one-way
        /// trip plus its station stop). −1 when !Valid, cabins ≤ 0 or seats ≤ 0.</summary>
        public static double CapacityPerHour(int cabins, int seats, double lengthM, double speedMps, double accelMps2,
                                             double dwellS)
        {
            if (cabins <= 0 || seats <= 0 || !Valid(lengthM, speedMps, accelMps2, dwellS))
                return -1.0;
            return (double)cabins * seats * 3600.0 / PeriodS(lengthM, speedMps, accelMps2, dwellS);
        }

        /// <summary>Skeptic F4: a path length rounded to LengthQuantumM (1 cm, Math.Round to even) before it reaches the
        /// profile; a non-finite input is returned unchanged (Valid rejects it).</summary>
        public static double QuantizeLengthM(double lengthM)
        {
            if (!Finite(lengthM))
                return lengthM;
            return Math.Round(lengthM / LengthQuantumM) * LengthQuantumM;
        }

        private static double Tau(double timeS, double periodS)
        {
            double tau = timeS - periodS * Math.Floor(timeS / periodS);
            if (!(tau >= 0.0) || tau >= periodS)
                return 0.0;
            return tau;
        }

        private static double Clamp01(double x)
        {
            return x < 0.0 ? 0.0 : x > 1.0 ? 1.0 : x;
        }

        private static bool Finite(double x)
        {
            return !double.IsNaN(x) && !double.IsInfinity(x);
        }
    }
}
