// The chair mover's per-chair kernel over FLAT segment arrays. LiftLoopCache.Sample walks a managed List<Segment>, unusable
// from Burst, while MoveChairsJob (ChairPinSystem.Mover.cs) needs the same arithmetic over a NativeArray. It lives here once,
// generic over the segment store (IFlatSegments): the job uses NativeArray<FlatSegment> (Burst, worker threads);
// tests/MoverBench uses a managed array and proves bit-for-bit equality with LiftLoopCache.Sample (JIT, not Burst; Burst may
// contract float ops differently: sub-micrometer, far inside MoveChair's 1 mm idempotence test).
// Body = LiftLoopCache.SampleSegments and ChairPinSystem.MoveChair's pose, line for line, no new math.
// Dependencies: Unity.Mathematics, Colossal.Mathematics, Game.Net.NetUtils.GetNodeRotation (pure math, NetUtils.cs:316-324)
// and Game.Objects.Transform. No Entities / Collections type.

using Colossal.Mathematics;
using Game.Net;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace SkiResortMod.Systems
{
    /// <summary>LiftLoopCache.Segment without the managed parts (no Entity owner; bools as bytes): blittable.</summary>
    internal struct FlatSegment
    {
        public Bezier4x3 Bezier;     // lane / straight segments
        public float3 ArcCenter;     // arc segments
        public float3 ArcU;
        public float3 ArcB;
        public double CumStart;
        public float ArcRadius;
        public float Length;
        public byte IsArc;
        public byte Reversed;
    }

    /// <summary>The segment store the kernel reads (a NativeArray in the job, an array in the bench).</summary>
    internal interface IFlatSegments
    {
        FlatSegment Get(int index);
    }

    internal static class LiftLoopSampler
    {
        /// <summary>Result codes of one chair (the job's result array; 0 = loop not written this pass).</summary>
        public const byte Skipped = 0, Moved = 1, InPlace = 2, Missing = 3;

        public static FlatSegment Flatten(in LiftLoopCache.Segment s)
        {
            return new FlatSegment
            {
                Bezier = s.m_Bezier,
                ArcCenter = s.m_ArcCenter,
                ArcU = s.m_ArcU,
                ArcB = s.m_ArcB,
                CumStart = s.m_CumStart,
                ArcRadius = s.m_ArcRadius,
                Length = s.m_Length,
                IsArc = (byte)(s.m_IsArc ? 1 : 0),
                Reversed = (byte)(s.m_Reversed ? 1 : 0),
            };
        }

        /// <summary>LiftLoopCache.SampleSegments over segments [first, first + count): position + unit travel tangent at loop
        /// distance s (caller wraps s into [0, length)). count must be ≥ 1.</summary>
        public static void Sample<T>(ref T segments, int first, int count, double s, out float3 position, out float3 travelTangent)
            where T : struct, IFlatSegments
        {
            int lo = 0, hi = count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (segments.Get(first + mid).CumStart <= s) lo = mid;
                else hi = mid - 1;
            }
            FlatSegment seg = segments.Get(first + lo);
            float d = math.clamp((float)(s - seg.CumStart), 0f, seg.Length);

            if (seg.IsArc != 0)
            {
                float theta = d / math.max(seg.ArcRadius, 1e-3f);
                math.sincos(theta, out float sin, out float cos);
                position = seg.ArcCenter + seg.ArcRadius * (seg.ArcU * cos + seg.ArcB * sin);
                travelTangent = seg.ArcB * cos - seg.ArcU * sin;
                return;
            }

            var t = new Bounds1(0f, 1f);
            if (seg.Reversed != 0)
            {
                // d meters of travel from the t=1 end toward t=0.
                MathUtils.ClampLengthInverse(seg.Bezier, ref t, d);
                position = MathUtils.Position(seg.Bezier, t.min);
                travelTangent = -MathUtils.Tangent(seg.Bezier, t.min);
            }
            else
            {
                MathUtils.ClampLength(seg.Bezier, ref t, d);
                position = MathUtils.Position(seg.Bezier, t.max);
                travelTangent = MathUtils.Tangent(seg.Bezier, t.max);
            }
            travelTangent = math.normalizesafe(travelTangent, new float3(0f, 0f, 1f));
        }

        /// <summary>ChairPinSystem.MoveChair's pose: the rope point rides ON the cable (<paramref name="drop"/> = CarrierDrop), yaw
        /// from the travel tangent (the current rotation is only the degenerate-tangent fallback). Returns InPlace (transform
        /// untouched) when the chair already stands there (1 mm, |dot| &gt; 0.99999), else Moved with <paramref name="transform"/>
        /// updated.</summary>
        public static byte Pose(float3 position, float3 travelTangent, float drop, ref Transform transform)
        {
            position.y -= drop;
            quaternion rotation = NetUtils.GetNodeRotation(travelTangent, transform.m_Rotation);
            if (math.distancesq(position, transform.m_Position) < 1e-6f &&
                math.abs(math.dot(rotation, transform.m_Rotation)) > 0.99999f)
                return InPlace;
            transform.m_Position = position;
            transform.m_Rotation = rotation;
            return Moved;
        }

        /// <summary>A box around everything a loop's chairs can occupy: the segments' control points (a bezier lies in their convex
        /// hull) and each arc's circle box. The caller grows it by the hang below the rope.</summary>
        public static void GrowBox(in FlatSegment seg, ref float3 min, ref float3 max)
        {
            if (seg.IsArc != 0)
            {
                float3 r = new float3(seg.ArcRadius);
                min = math.min(min, seg.ArcCenter - r);
                max = math.max(max, seg.ArcCenter + r);
                return;
            }
            Bezier4x3 b = seg.Bezier;
            min = math.min(min, math.min(math.min(b.a, b.b), math.min(b.c, b.d)));
            max = math.max(max, math.max(math.max(b.a, b.b), math.max(b.c, b.d)));
        }
    }
}
