// Pure math of a station bullwheel that turns with its lift's rope (T256). BullwheelSpinSystem uses it per frame, tests/LiftBatch
// (WheelTests) replays it. The wheel is a bone of the station mesh (bone type None: vanilla never writes it,
// Game.Rendering/ObjectInterpolateSystem.cs:796-1056 has no case for it), turned about the bone's local Y, like vanilla's
// AnimateRotatingBoneY (:3826-3845).
// The angle is integrated from the rope's speed on the mover's own clock: it stands when the loop is held (no power, closed) or the
// game is paused, and goes on from where it stood, so there is no jump at a resume. Rim speed = rope speed by construction.
// Pure file: System and Unity.Mathematics only. Static and Burst-safe. net48: no Math.Clamp, no double.IsFinite.
// Engine notes: EngineNotes/BullwheelSpinSystem.md

using System;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    internal static class BullwheelMath
    {
        /// <summary>A station mesh bone whose name starts with this turns with the rope ("Bullwheel", "BullwheelDrive", ...).</summary>
        public const string BonePrefix = "Bullwheel";

        /// <summary>The rope radius limits of a wheel that can be a bullwheel (LiftLoopTopology: a turn's diameter is 1 to 10 m).</summary>
        public const float MinRadiusM = 0.5f, MaxRadiusM = 5f;

        /// <summary>A pass's clock step above this, seconds, is a jump (a load, a clock reset), not motion: the wheel keeps its angle.
        /// At sim speed 8 and 10 fps a real step is 0.8 s.</summary>
        public const double MaxStepS = 5.0;

        public static bool IsWheelBone(string boneName)
        {
            return boneName != null && boneName.StartsWith(BonePrefix, StringComparison.Ordinal);
        }

        /// <summary>The turn sense of the rope round a wheel, as the sign of the wheel's angle rate about +Y: +1 = clockwise seen from
        /// above (Unity is left-handed: quaternion.RotateY(+a) turns +z toward +x), -1 = counterclockwise.
        /// <paramref name="travelAtEntry"/> is the rope's travel direction where it meets the wheel, <paramref name="centerToEntry"/> the
        /// direction from the wheel's center to that point (LiftLoopCache.Segment.m_ArcB / m_ArcU). The rope turns toward the center:
        /// the center on the right of the travel direction is a clockwise turn. A degenerate input gives +1.</summary>
        public static int TurnSign(float3 travelAtEntry, float3 centerToEntry)
        {
            float3 toCenter = -centerToEntry;
            // (travel x toCenter).y in Unity's left-handed frame: travel +z with the center at +x gives +1
            float y = travelAtEntry.z * toCenter.x - travelAtEntry.x * toCenter.z;
            return y >= 0f ? 1 : -1;
        }

        /// <summary>An arc of a loop belongs to a station's wheel when its center is this close to the wheel (the loop builder puts the
        /// arc's center on the wheel, LiftLoopCache.AppendTerminalTurn). The plain arc between two terminals of a station whose wheel
        /// the builder refused is centered on the terminals' midpoint, several meters away.</summary>
        public const float MatchM = 0.5f;

        /// <summary>The wheel drive of one loop turn arc (LiftLoopCache.Segment: m_ArcCenter, m_ArcU = center to entry, m_ArcB =
        /// travel at entry, m_ArcRadius) for a station whose wheel center at rope height is <paramref name="wheelWorld"/>. False when
        /// the arc is not that wheel's (center too far) or its radius is no wheel's. Else the rope radius and the turn sense
        /// (TurnSign).</summary>
        public static bool ArcDrive(float3 arcCenter, float3 arcU, float3 arcB, float arcRadius, float3 wheelWorld, out float radiusM,
            out int sign)
        {
            radiusM = 0f;
            sign = 1;
            if (!(arcRadius >= MinRadiusM) || arcRadius > MaxRadiusM || !(math.distance(arcCenter, wheelWorld) < MatchM))
                return false;
            radiusM = arcRadius;
            sign = TurnSign(arcB, arcU);
            return true;
        }

        /// <summary>The wheel's angle after one pass, radians, wrapped to (-pi, pi]. <paramref name="speedMps"/> is the rope's speed now
        /// (0 = held or stopped; negative = the rope runs backward, the tram's return trip), <paramref name="dtS"/> the mover clock's
        /// step. A step that is not in (0, MaxStepS], a radius outside the wheel limits or a non-finite input leaves the angle as it
        /// was.</summary>
        public static double Advance(double angle, double speedMps, double dtS, double radiusM, int sign)
        {
            if (!math.isfinite(angle))
                angle = 0.0;
            if (!(dtS > 0.0) || dtS > MaxStepS || !math.isfinite(speedMps) || !(radiusM >= MinRadiusM) || radiusM > MaxRadiusM)
                return Wrap(angle);
            return Wrap(angle + sign * speedMps * dtS / radiusM);
        }

        /// <summary>An angle wrapped to (-pi, pi].</summary>
        public static double Wrap(double angle)
        {
            const double twoPi = 2.0 * Math.PI;
            double a = angle % twoPi;
            if (a > Math.PI)
                a -= twoPi;
            else if (a <= -Math.PI)
                a += twoPi;
            return a;
        }

        /// <summary>The bone's local rotation at <paramref name="angle"/>: its rest rotation turned about its own Y.</summary>
        public static quaternion BoneRotation(quaternion rest, float angle)
        {
            return math.mul(rest, quaternion.RotateY(angle));
        }

        /// <summary>Wheel revolutions per second at a rope speed (the log line and the tests).</summary>
        public static double RevPerSecond(double speedMps, double radiusM)
        {
            return radiusM > 0.0 ? speedMps / (2.0 * Math.PI * radiusM) : 0.0;
        }
    }
}
