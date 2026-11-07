// The pure, stateless motion of a jig-back tram: a stateless s(t) (trapezoid speed profile + dwell, cabin B at L − s_A), double throughout.
// Pure file: System and Unity.Mathematics only. Linked into tests/LiftBatch (ShuttleTests) and tests/ResortBatch (HubRules' shuttle rows).
// Cabin B is 1 − u (both dock at the same instant: one haul rope).
// Invalid input (L, v, a ≤ 0 or not finite, D < 0 or not finite, t not finite) gives u 0, DwellBase.
// TimeSOf is ChairPin's clock bit for bit (ChairPinSystem.cs:1132-1135): frozen on pause, smooth at any speed, resnapped to the
// saved frame on load. QuantizeLengthM rounds a path length to 1 cm before it reaches the profile, so float-noise rebuilds
// (identical topology, last-bit differences) give the same cycle period P.
// Engine notes: EngineNotes/ShuttleProfile.md

using System;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>The mover's gate result per shuttle.</summary>
    public enum ShuttleState : byte { Parked = 0, Running = 1, Held = 2 }

    /// <summary>Cabin A's leg (cabin B is on the mirrored leg).</summary>
    public enum ShuttleLeg : byte { DwellBase = 0, Up = 1, DwellTop = 2, Down = 3 }

    /// <summary>The mover's per-shuttle view for the hub.</summary>
    public struct ShuttleView
    {
        public int Cabins;          // Shuttle carriers assigned to this shuttle (0..2)
        public int CabinsOnPath;    // = Cabins while the path exists (Running or Held), else 0
        public double PathM;        // mean dock-to-dock path length. 0 without a path
        public ShuttleState State;
        public ShuttleLeg Leg;
        public double FractionA;    // 0 = cabin A at the base dock … 1 = at the top dock
        public float TripS, CycleS; // TripTimeS(PathM), PeriodS(PathM)
    }

    public static class ShuttleProfile
    {
        /// <summary>ChairPinSystem.cs:1132-1135: TimeS = (frameIndex + frameTime) / 60.</summary>
        public const double FramesPerSecond = 60.0;

        /// <summary>Path lengths are rounded to this before the profile: 1 cm.</summary>
        public const double LengthQuantumM = 0.01;

        /// <summary>ChairPin's clock, bit for bit: ((double)frameIndex + frameTime) / 60.</summary>
        public static double TimeSOf(uint frameIndex, float frameTime) => ((double)frameIndex + frameTime) / FramesPerSecond;

        /// <summary>Finite, L, v, a &gt; 0, dwell ≥ 0.</summary>
        public static bool Valid(double lengthM, double speedMps, double accelMps2, double dwellS)
        {
            return Finite(lengthM) && Finite(speedMps) && Finite(accelMps2) && Finite(dwellS) &&
                   lengthM > 0.0 && speedMps > 0.0 && accelMps2 > 0.0 && dwellS >= 0.0;
        }

        /// <summary>Dock-to-dock seconds (acceleration included, dwell excluded). 0 when !Valid(…, 0).</summary>
        public static double TripTimeS(double lengthM, double speedMps, double accelMps2)
        {
            if (!Valid(lengthM, speedMps, accelMps2, 0.0))
                return 0.0;
            double rampsM = speedMps * speedMps / accelMps2;
            if (lengthM >= rampsM)
                return lengthM / speedMps + speedMps / accelMps2;
            return 2.0 * Math.Sqrt(lengthM / accelMps2);
        }

        /// <summary>Meters traveled tS seconds after leaving a dock (tS clamped to [0, trip]). 0 when invalid.</summary>
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

        /// <summary>One full cycle: 2 × (trip + dwell). 0 when invalid.</summary>
        public static double PeriodS(double lengthM, double speedMps, double accelMps2, double dwellS)
        {
            if (!Valid(lengthM, speedMps, accelMps2, dwellS))
                return 0.0;
            return 2.0 * (TripTimeS(lengthM, speedMps, accelMps2) + dwellS);
        }

        /// <summary>Cabin A's fraction of the path at <paramref name="timeS"/> (0 = base dock, 1 = top dock) and its leg. Cabin B is 1 − u.
        /// Invalid input gives 0, DwellBase.</summary>
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

        /// <summary>Held: 0 or 1, the terminal cabin A last reached (the base when τ &lt; dwell + trip, else the top). The leg is DwellBase
        /// / DwellTop. Invalid input gives 0, DwellBase.</summary>
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

        /// <summary>Riders an hour: cabins × seats × 3600 / (2 × (trip + dwell)) (a cabin carries one load per one-way
        /// trip plus its station stop). −1 when !Valid, cabins ≤ 0 or seats ≤ 0.</summary>
        public static double CapacityPerHour(int cabins, int seats, double lengthM, double speedMps, double accelMps2,
                                             double dwellS)
        {
            if (cabins <= 0 || seats <= 0 || !Valid(lengthM, speedMps, accelMps2, dwellS))
                return -1.0;
            return (double)cabins * seats * 3600.0 / PeriodS(lengthM, speedMps, accelMps2, dwellS);
        }

        /// <summary>A path length rounded to LengthQuantumM (1 cm, Math.Round to even) before it reaches the profile. A non-finite input is
        /// returned unchanged (Valid rejects it).</summary>
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
