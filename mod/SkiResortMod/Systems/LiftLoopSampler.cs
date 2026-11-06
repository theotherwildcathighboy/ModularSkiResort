// The chair mover's per-chair kernel over FLAT segment arrays. LiftLoopCache.Sample walks a managed List<Segment>, unusable
// from Burst, while MoveChairsJob (ChairPinSystem.Mover.cs) needs the same arithmetic over a NativeArray. It lives here once,
// generic over the segment store (IFlatSegments): the job uses NativeArray<FlatSegment> (Burst, worker threads);
// tests/MoverBench uses a managed array and proves bit-for-bit equality with LiftLoopCache.Sample (JIT, not Burst; Burst may
// contract float ops differently: sub-micrometer, far inside MoveChair's 1 mm idempotence test).
// Body = LiftLoopCache.SampleSegments and ChairPinSystem.MoveChair's pose, line for line, no new math.
// The carrier grip round adds, per flat segment, Guided (an arc or a synthesized station run: no swing there) and the free run around
// it (RunBefore / RunAfter, MarkRuns), and a Sample overload that also returns the segment and the distance into it (the swing ramp,
// CarrierPose.DistanceToGuided); the original Sample is that overload with the extras discarded (same arithmetic, MoverBench B-1).
// Dependencies: Unity.Mathematics, Colossal.Mathematics, Game.Net.NetUtils.GetNodeRotation (pure math, NetUtils.cs:316-324),
// Game.Objects.Transform and Unity.Entities.Entity (Flatten's owner test only). No Collections type.

using Colossal.Mathematics;
using Game.Net;
using Unity.Entities;
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
        /// <summary>1 = no swing here: a bull wheel arc or a synthesized terminal run (LiftLoopCache's turns, Owner == Entity.Null).</summary>
        public byte Guided;
        /// <summary>Free-run meters before this segment's start / after its end up to the nearest guided segment (MarkRuns; 0 on guided).</summary>
        public float RunBefore, RunAfter;
    }

    /// <summary>The segment store the kernel reads (a NativeArray in the job, an array in the bench).</summary>
    internal interface IFlatSegments
    {
        FlatSegment Get(int index);
    }

    /// <summary>A writable store (MarkRuns: the mover's NativeList, the bench's array).</summary>
    internal interface IFlatSegmentStore : IFlatSegments
    {
        void Set(int index, FlatSegment value);
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
                Guided = (byte)(s.m_IsArc || s.m_Owner == Entity.Null ? 1 : 0),
            };
        }

        /// <summary>Fills RunBefore / RunAfter of a closed loop's segments [first, first + count): the free meters between each free
        /// segment and the nearest guided segment behind / ahead of it, around the wrap. No guided segment at all: CarrierPose.NoGuidedM.</summary>
        public static void MarkRuns<T>(ref T segments, int first, int count) where T : struct, IFlatSegmentStore
        {
            int g0 = -1;
            for (int i = 0; i < count; i++)
            {
                if (segments.Get(first + i).Guided != 0)
                {
                    g0 = i;
                    break;
                }
            }
            if (g0 < 0)
            {
                for (int i = 0; i < count; i++)
                {
                    FlatSegment f = segments.Get(first + i);
                    f.RunBefore = f.RunAfter = CarrierPose.NoGuidedM;
                    segments.Set(first + i, f);
                }
                return;
            }
            // Forward from a guided segment: the free meters accumulated since it.
            float acc = 0f;
            for (int k = 1; k <= count; k++)
            {
                int i = (g0 + k) % count;
                FlatSegment f = segments.Get(first + i);
                if (f.Guided != 0)
                {
                    f.RunBefore = 0f;
                    acc = 0f;
                }
                else
                {
                    f.RunBefore = acc;
                    acc += f.Length;
                }
                segments.Set(first + i, f);
            }
            // Backward from the same guided segment: the free meters still ahead.
            acc = 0f;
            for (int k = 1; k <= count; k++)
            {
                int i = ((g0 - k) % count + count) % count;
                FlatSegment f = segments.Get(first + i);
                if (f.Guided != 0)
                {
                    f.RunAfter = 0f;
                    acc = 0f;
                }
                else
                {
                    f.RunAfter = acc;
                    acc += f.Length;
                }
                segments.Set(first + i, f);
            }
        }

        /// <summary>LiftLoopCache.SampleSegments over segments [first, first + count): position + unit travel tangent at loop
        /// distance s (caller wraps s into [0, length)). count must be ≥ 1.</summary>
        public static void Sample<T>(ref T segments, int first, int count, double s, out float3 position, out float3 travelTangent)
            where T : struct, IFlatSegments
        {
            Sample(ref segments, first, count, s, out position, out travelTangent, out _, out _);
        }

        /// <summary>Sample plus the segment index (absolute) and the clamped distance d into it: the swing ramp's inputs.</summary>
        public static void Sample<T>(ref T segments, int first, int count, double s, out float3 position, out float3 travelTangent,
                                     out int segment, out float d)
            where T : struct, IFlatSegments
        {
            int lo = 0, hi = count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (segments.Get(first + mid).CumStart <= s) lo = mid;
                else hi = mid - 1;
            }
            segment = first + lo;
            FlatSegment seg = segments.Get(segment);
            d = math.clamp((float)(s - seg.CumStart), 0f, seg.Length);

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

        /// <summary>Pose's idempotence and write for an already computed pose (the split carrier's grip and body, CarrierPose).</summary>
        public static byte Write(float3 position, quaternion rotation, ref Transform transform)
        {
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
