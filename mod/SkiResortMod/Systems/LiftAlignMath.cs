// Pure geometry of the lift alignment fix (docs: lift-alignment-2026-11-02): the plan bend at a tower, the rope slopes a hanging
// lane meets a node with, and the pitch of a sheave train that follows them. No Game or Entities type (Unity.Mathematics only), so
// tests/LiftBatch links it (AlignTests.cs, AL-*).
// The rope of a span is the engine's hanging lane: the chord a..d with b, c at 1/3 and 2/3 lowered by |a.xz - d.xz| * m_Hanging * 4/3
// (LaneSystem.cs:4128-4138; LiftTowerEdit.HangingCurve). Its end slopes are the chord slope -4h (leaving a) and +4h (arriving at d),
// so a tower between two spans sees a break-over of the chord change plus 8h.
// Engine notes: EngineNotes/LiftAlignMath.md

using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    internal static class LiftAlignMath
    {
        /// <summary>Plan bend above which the lift tool and the hub warn (owner verdict 2026-11-02: warn, never refuse). Real lines turn
        /// with sheaves only by a few degrees; larger turns are angle stations.</summary>
        public const float BendWarnDeg = 5f;

        /// <summary>The plan angle, degrees in [0, 180], between the line arriving at <paramref name="node"/> from
        /// <paramref name="prev"/> and leaving it toward <paramref name="next"/> (0 = straight). y is ignored; a zero-length leg gives 0.</summary>
        public static float PlanBendDeg(float3 prev, float3 node, float3 next)
        {
            float2 a = node.xz - prev.xz, b = next.xz - node.xz;
            float la = math.length(a), lb = math.length(b);
            if (la < 1e-4f || lb < 1e-4f)
                return 0f;
            float c = math.clamp(math.dot(a, b) / (la * lb), -1f, 1f);
            return math.degrees(math.acos(c));
        }

        /// <summary>Slope dy/dxz of the hanging rope a..d where it LEAVES a (toward d): chord - 4h.</summary>
        public static float LeaveSlope(float3 a, float3 d, float hanging)
        {
            float l = math.distance(a.xz, d.xz);
            return l < 1e-4f ? 0f : (d.y - a.y) / l - 4f * hanging;
        }

        /// <summary>Slope dy/dxz of the hanging rope a..d where it ARRIVES at d (coming from a): chord + 4h.</summary>
        public static float ArriveSlope(float3 a, float3 d, float hanging)
        {
            float l = math.distance(a.xz, d.xz);
            return l < 1e-4f ? 0f : (d.y - a.y) / l + 4f * hanging;
        }

        /// <summary>Train pitch, radians, nose UP toward <paramref name="next"/>, at a line node <paramref name="node"/> whose rope points
        /// (the axle or crown points of the neighbors) are <paramref name="prev"/> and <paramref name="next"/>: the bisector of the
        /// arriving and leaving rope angles, clamped to ±<paramref name="maxPitch"/>. A straight uniform slope gives exactly that slope.</summary>
        public static float TrainPitch(float3 prev, float3 node, float3 next, float hanging, float maxPitch)
        {
            float inAngle = math.atan(ArriveSlope(prev, node, hanging));
            float outAngle = math.atan(LeaveSlope(node, next, hanging));
            return math.clamp(0.5f * (inAngle + outAngle), -maxPitch, maxPitch);
        }

        /// <summary>Pitch, radians, nose up toward the line, of a station's line-side train: the angle of the first span's rope where it
        /// meets the station anchor <paramref name="station"/>, coming from (or going to) the first line rope point <paramref name="line"/>.</summary>
        public static float StationTrainPitch(float3 station, float3 line, float hanging, float maxPitch)
        {
            return math.clamp(math.atan(LeaveSlope(station, line, hanging)), -maxPitch, maxPitch);
        }

        /// <summary>The rotation about local x that pitches a node-yawed object nose up by <paramref name="pitchTowardNext"/> toward
        /// <paramref name="next"/>: the sign flips when the object's +z faces <paramref name="prev"/>. Unity's RotateX(+a) turns +z toward
        /// -y (nose down), so nose up along +z is RotateX(-a).</summary>
        public static quaternion PitchedRotation(quaternion yaw, float3 prev, float3 next, float pitchTowardNext)
        {
            float3 forward = math.mul(yaw, new float3(0f, 0f, 1f));
            float sign = math.dot(forward.xz, next.xz - prev.xz) >= 0f ? 1f : -1f;
            return math.mul(yaw, quaternion.RotateX(-sign * pitchTowardNext));
        }

        /// <summary>Where a point fixed on a pitched object lands: <paramref name="pivotWorld"/> is the pin the object turns about (world),
        /// <paramref name="local"/> the point in the object's frame relative to that pin.</summary>
        public static float3 AboutPin(float3 pivotWorld, quaternion rotation, float3 local)
        {
            return pivotWorld + math.mul(rotation, local);
        }
    }
}
