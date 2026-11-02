// Pure rules of the snow groomer: the path source per run kind, the restraint rule, the season gate, the trip along the path as a function
// of the clock (drive, stop, pivot, stop, drive back), the ground plane under the footprint, its smoothing and the pose. SkiGroomerSystem is
// the only caller. Linked into tests/ResortBatch (GroomerTests, with EntityRef.cs and RunSnowRules.cs); `System` + `Unity.Mathematics` only.
// Engine notes: EngineNotes/SkiGroomerSystem.md

using System;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>Where a groomer drives on one run. Cat tracks use the ribbon centerline; a later pass can add a source for groomed runs
    /// (for example lanes across a wide polygon) without touching the system.</summary>
    public interface IGroomerPathSource
    {
        GroomerPath Build(float2[] outlineXZ, out GroomerPathFail fail);
    }

    /// <summary>The centerline of a ribbon outline (GroomerCenterline).</summary>
    public sealed class RibbonCenterlineSource : IGroomerPathSource
    {
        public static readonly RibbonCenterlineSource Instance = new RibbonCenterlineSource();

        public GroomerPath Build(float2[] outlineXZ, out GroomerPathFail fail)
        {
            return GroomerCenterline.Build(outlineXZ, out fail);
        }
    }

    /// <summary>The part of a trip the clock is in. A cycle runs in this order, then repeats.</summary>
    public enum GroomerLeg : byte
    {
        DriveOut = 0,
        StopAtEnd = 1,
        PivotAtEnd = 2,
        WaitAtEnd = 3,
        DriveBack = 4,
        StopAtStart = 5,
        PivotAtStart = 6,
        WaitAtStart = 7,
    }

    /// <summary>A moment of the trip: meters along the path and the turn relative to the path's own direction there.</summary>
    public struct GroomerTrip
    {
        public double S;
        public float TurnRad;
        public GroomerLeg Leg;
    }

    /// <summary>The smoothed ground under the machine, in its own axes.</summary>
    public struct GroomerGround
    {
        public bool Known;
        public float Height;
        public float SlopeLong;   // rise per meter toward the nose
        public float SlopeWide;   // rise per meter toward the right side
    }

    public enum GroomerAction : byte { None = 0, Spawn = 1, Remove = 2 }

    public static class GroomerRules
    {
        // ---- the asset ------------------------------------------------------------------------------------------------------------
        public const string PrefabName = "SkiGroomerA";
        /// <summary>Shown until SkiGroomerA is imported, so the system can be tested in game. A ground-standing prop that no system scans for
        /// (no chair roster, plume or sign rule reads it) and that has no effect or sub-object.</summary>
        public const string StandInPrefabName = "SkiDressAkja_Red";
        /// <summary>Where the mesh's nose points, as a yaw from local +Z in degrees. SkiGroomerA is built with forward = +Z in game terms.</summary>
        public const float LocalForwardYawDeg = 0f;
        /// <summary>The same for the stand-in (UNVERIFIED: read it off the first in-game screenshot; only the heading's look depends on it).</summary>
        public const float StandInForwardYawDeg = 0f;
        /// <summary>Lift of the origin above the sampled terrain. 0: the mesh's origin sits on the ground. Whether snow buries the tracks is a bridge check.</summary>
        public const float GroundOffsetM = 0f;

        // ---- the trip (seconds and meters of the render clock, the clock the chairs use: 60 clock s = 3,600 frames) --------------------
        /// <summary>12 km/h on the render clock, inside the 10-20 km/h real cats work at. It reads as a real speed on screen at 1x; against game
        /// time it is about 1/20 of real (a game hour is 182 clock s), the same trade the chairs make. See the engine notes.</summary>
        public const float SpeedMps = 3.33f;
        /// <summary>Seconds to reach full speed from a stop, and to stop again. A track shorter than speed x ramp peaks lower (a triangle).</summary>
        public const float RampS = 3f;
        /// <summary>A stop before and after each pivot.</summary>
        public const float StopS = 2.5f;
        /// <summary>Mean rate of the pivot on the spot. Eased, so the peak is 1.5 times this (22.5 deg/s): 12 s for the half turn.</summary>
        public const float PivotDegPerS = 15f;
        public const float PivotS = 180f / PivotDegPerS;
        /// <summary>The heading is the chord from 4 m behind to 4 m ahead: about the machine's own length, so it takes a bend as its tracks would.</summary>
        public const float HeadingChordHalfM = 4f;

        // ---- the footprint --------------------------------------------------------------------------------------------------------
        public const float FootprintLengthM = 7f;
        public const float FootprintWidthM = 4f;
        public const int FootprintSamplesLong = 5;
        public const int FootprintSamplesWide = 3;
        public const int FootprintSamples = FootprintSamplesLong * FootprintSamplesWide;
        /// <summary>Tilt past this (about 39 degrees) is a cliff under the footprint, not a pose: clamped so the machine never stands on its nose.</summary>
        public const float MaxSlope = 0.8f;
        /// <summary>Time constants on the clock. The height is short so the tracks do not sink on a climb (25 % at 3.33 m/s lags 0.1 m); the tilt
        /// is longer, because a lag in tilt does not show and it takes out the kinks of the 3.5 m terrain grid.</summary>
        public const float HeightTauS = 0.12f;
        public const float TiltTauS = 0.35f;
        /// <summary>A height jump larger than this is a terrain edit or a new path, not a bump: snap, do not slide.</summary>
        public const float SnapJumpM = 3f;

        // ---- restraint ------------------------------------------------------------------------------------------------------------
        public const int MaxGroomersPerTrack = 1;
        /// <summary>Cost guard for a city with an absurd number of tracks; the system logs once when it is reached.</summary>
        public const int MaxGroomers = 64;
        /// <summary>Clock seconds between two looks at one track's snow share.</summary>
        public const float SeasonPollS = 5f;

        // ---- the path source and the restraint rule -------------------------------------------------------------------------------

        /// <summary>The path source of a run kind, or null when that kind gets no groomer. Groomed runs are out of scope for now.</summary>
        public static IGroomerPathSource SourceFor(int kind)
        {
            return RunKindRules.IsCatTrackLink(kind) ? RibbonCenterlineSource.Instance : null;
        }

        /// <summary>A groomer belongs on a run when the option is on, the run is bound to a ski area, it is in season and it has a usable path.</summary>
        public static bool Wanted(bool optionOn, bool bound, bool seasonOpen, bool hasPath)
        {
            return optionOn && bound && seasonOpen && hasPath;
        }

        /// <summary>What to do for one track: at most one prop per track (hasProp is the track's one slot), and never over the city cap.</summary>
        public static GroomerAction Decide(bool wanted, bool hasProp, int groomersNow)
        {
            if (wanted && !hasProp)
                return groomersNow < MaxGroomers ? GroomerAction.Spawn : GroomerAction.None;
            return !wanted && hasProp ? GroomerAction.Remove : GroomerAction.None;
        }

        /// <summary>The season gate on the run's snow share (GetRunSnow, whose Share stays real with snow gating off, so the machine still leaves in
        /// summer). The hub calls a cat track closed below the midpoint (RunSnowRules.InitialOpenShare), so that closes it here too and a groomer is
        /// never on a closed track; it returns only at OpenAtShare, so a share hovering at the midpoint does not make it pop in and out. The first
        /// look after a load uses the midpoint. NaN = closed. Returns true when open.</summary>
        public static bool SeasonStep(ref RunSnowState state, float share)
        {
            float line = state == RunSnowState.Closed ? RunSnowRules.OpenAtShare : RunSnowRules.InitialOpenShare;
            state = share >= line ? RunSnowState.Open : RunSnowState.Closed;
            return state == RunSnowState.Open;
        }

        // ---- the trip -------------------------------------------------------------------------------------------------------------

        /// <summary>Seconds of one drive along a path of this length, stop to stop.</summary>
        public static double DriveSeconds(double lengthM)
        {
            double accel = SpeedMps / RampS;
            return lengthM >= SpeedMps * RampS ? lengthM / SpeedMps + RampS : 2.0 * Math.Sqrt(lengthM / accel);
        }

        /// <summary>Seconds of a full cycle: out, turn, back, turn.</summary>
        public static double PeriodSeconds(double lengthM)
        {
            return 2.0 * (DriveSeconds(lengthM) + StopS + PivotS + StopS);
        }

        /// <summary>Meters covered after t seconds of a drive that takes driveS: constant acceleration, cruise, constant braking. Monotonic.</summary>
        public static double DriveDistance(double t, double lengthM, double driveS)
        {
            double accel = SpeedMps / RampS;
            double ramp = Math.Min((double)RampS, 0.5 * driveS);
            if (t <= 0.0)
                return 0.0;
            if (t >= driveS)
                return lengthM;
            if (t < ramp)
                return 0.5 * accel * t * t;
            if (t > driveS - ramp)
            {
                double left = driveS - t;
                return lengthM - 0.5 * accel * left * left;
            }
            return 0.5 * accel * ramp * ramp + SpeedMps * (t - ramp);
        }

        /// <summary>Smoothstep: the pivot starts and ends at rest.</summary>
        public static float Ease(float u)
        {
            u = math.saturate(u);
            return u * u * (3f - 2f * u);
        }

        /// <summary>The trip at t clock seconds into the cycle (any t: wrapped to the period). A pure function of the clock, like the chairs: it
        /// never drifts, stands still when the clock is paused, loses nothing at 3x and comes back at the same place after a save and reload.
        /// pivotSign (+1 or -1) is the turning sense. S and TurnRad are continuous over every leg change.</summary>
        public static GroomerTrip TripAt(double t, double lengthM, int pivotSign)
        {
            double drive = DriveSeconds(lengthM);
            double period = 2.0 * (drive + StopS + PivotS + StopS);
            t %= period;
            if (t < 0.0)
                t += period;
            float half = pivotSign * math.PI;
            bool back = t >= 0.5 * period;
            double u = back ? t - 0.5 * period : t;
            float baseTurn = back ? half : 0f;
            GroomerLeg first = back ? GroomerLeg.DriveBack : GroomerLeg.DriveOut;
            var trip = new GroomerTrip();
            double endS = back ? 0.0 : lengthM;
            if (u < drive)
            {
                double covered = DriveDistance(u, lengthM, drive);
                trip.S = back ? lengthM - covered : covered;
                trip.TurnRad = baseTurn;
                trip.Leg = first;
                return trip;
            }
            trip.S = endS;
            u -= drive;
            if (u < StopS)
            {
                trip.TurnRad = baseTurn;
                trip.Leg = first + 1;
                return trip;
            }
            u -= StopS;
            if (u < PivotS)
            {
                trip.TurnRad = baseTurn + half * Ease((float)(u / PivotS));
                trip.Leg = first + 2;
                return trip;
            }
            trip.TurnRad = baseTurn + half;
            trip.Leg = first + 3;
            return trip;
        }

        /// <summary>A stable per-track start of the cycle, and the turning sense, from the track's first node: tracks do not move in step, and a
        /// reload resumes the same trip. Any seed gives a phase in [0, period).</summary>
        public static double PhaseFor(uint seed, double periodS)
        {
            return (seed & 0xFFFFu) / 65536.0 * periodS;
        }

        public static int PivotSignFor(uint seed)
        {
            return (seed >> 16 & 1u) == 0u ? 1 : -1;
        }

        /// <summary>Yaw of a direction in the xz plane: 0 along +z, positive toward +x.</summary>
        public static float YawOf(float2 direction)
        {
            return math.atan2(direction.x, direction.y);
        }

        // ---- the ground -----------------------------------------------------------------------------------------------------------

        /// <summary>The offset of footprint sample (i along, j across) from the center: u meters toward the nose, v meters toward the right.</summary>
        public static void FootprintOffset(int i, int j, out float u, out float v)
        {
            u = FootprintLengthM * ((float)i / (FootprintSamplesLong - 1) - 0.5f);
            v = FootprintWidthM * ((float)j / (FootprintSamplesWide - 1) - 0.5f);
        }

        /// <summary>The world xz of a footprint sample for a machine at center with this yaw.</summary>
        public static float2 FootprintPoint(float2 center, float yawRad, float u, float v)
        {
            math.sincos(yawRad, out float sin, out float cos);
            return center + new float2(sin, cos) * u + new float2(cos, -sin) * v;
        }

        /// <summary>The least-squares plane through the footprint's samples (index i * FootprintSamplesWide + j). The grid is centered and symmetric,
        /// so the three terms separate. A non-finite sample returns false and changes nothing downstream.</summary>
        public static bool FitPlane(float[] heights, out float height, out float slopeLong, out float slopeWide)
        {
            height = 0f;
            slopeLong = 0f;
            slopeWide = 0f;
            float sum = 0f, sumU = 0f, sumV = 0f, sumUU = 0f, sumVV = 0f;
            for (int i = 0; i < FootprintSamplesLong; i++)
            {
                for (int j = 0; j < FootprintSamplesWide; j++)
                {
                    float h = heights[i * FootprintSamplesWide + j];
                    if (!math.isfinite(h))
                        return false;
                    FootprintOffset(i, j, out float u, out float v);
                    sum += h;
                    sumU += h * u;
                    sumV += h * v;
                    sumUU += u * u;
                    sumVV += v * v;
                }
            }
            height = sum / FootprintSamples;
            slopeLong = sumU / sumUU;
            slopeWide = sumV / sumVV;
            return true;
        }

        /// <summary>Exponential smoothing with the step in clock seconds, so it holds at any frame rate and speed. dt 0 (paused) changes nothing;
        /// a long step lands on the target.</summary>
        public static float Smooth(float value, float target, double dtS, float tauS)
        {
            if (!(dtS > 0.0))
                return value;
            float keep = math.exp(-(float)dtS / tauS);
            return target + (value - target) * keep;
        }

        /// <summary>One step of the ground state: the first step and a jump over SnapJumpM snap, the rest smooth. Slopes are clamped to MaxSlope first.</summary>
        public static void StepGround(ref GroomerGround g, float height, float slopeLong, float slopeWide, double dtS)
        {
            slopeLong = math.clamp(slopeLong, -MaxSlope, MaxSlope);
            slopeWide = math.clamp(slopeWide, -MaxSlope, MaxSlope);
            if (!g.Known || math.abs(height - g.Height) > SnapJumpM)
            {
                g.Known = true;
                g.Height = height;
                g.SlopeLong = slopeLong;
                g.SlopeWide = slopeWide;
                return;
            }
            g.Height = Smooth(g.Height, height, dtS, HeightTauS);
            g.SlopeLong = Smooth(g.SlopeLong, slopeLong, dtS, TiltTauS);
            g.SlopeWide = Smooth(g.SlopeWide, slopeWide, dtS, TiltTauS);
        }

        /// <summary>The rotation that puts the mesh's nose (localForwardYawRad from local +Z) along yaw, tilted by the ground's slopes.</summary>
        public static quaternion PoseRotation(float yawRad, float slopeLong, float slopeWide, float localForwardYawRad)
        {
            math.sincos(yawRad, out float sin, out float cos);
            float3 forward = math.normalize(new float3(sin, slopeLong, cos));
            float3 right = math.normalize(new float3(cos, slopeWide, -sin));
            float3 up = math.normalize(math.cross(forward, right));
            return math.mul(quaternion.LookRotationSafe(forward, up), quaternion.RotateY(-localForwardYawRad));
        }
    }
}
