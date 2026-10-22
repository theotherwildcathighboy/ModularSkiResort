// LiftLoopSampler — the chair mover's per-chair kernel over FLAT segment arrays (R-3 of
// docs/research/lifts/lift-architecture-review-2026-10-22.md; report docs/research/lifts/lift-scale-2026-10-22.md, DECISIONS 369).
//
// WHY A SECOND SAMPLER. LiftLoopCache.Sample walks a managed List<Segment> (a class-held list: not usable from Burst). The
// mover's Burst job (ChairPinSystem.Mover.cs, MoveChairsJob) needs the same arithmetic over a NativeArray. This file holds
// it ONCE, generic over the segment store (IFlatSegments), so:
//   * the job instantiates it over NativeArray<FlatSegment> (Burst-compiled, worker threads);
//   * tests/MoverBench instantiates it over a managed FlatSegment[] and proves it equals LiftLoopCache.Sample bit for bit on a
//     synthetic 20-lift resort (JIT, not Burst; Burst may contract float ops differently: sub-micrometer, far inside
//     MoveChair's 1 mm idempotence test).
// Body = LiftLoopCache.SampleSegments (binary search on m_CumStart, analytic arcs, MathUtils.ClampLength / ClampLengthInverse
// on lane beziers) and ChairPinSystem.MoveChair's pose (grip-top drop, NetUtils.GetNodeRotation(travel tangent, current
// rotation), the 1 mm / 0.99999 idempotence test) — line for line, no new math.
// Dependencies: Unity.Mathematics, Colossal.Mathematics (Bezier4x3, Bounds1, MathUtils), Game.Net.NetUtils.GetNodeRotation
// (pure math, Game.Net/NetUtils.cs:316-324 [D160]) and Game.Objects.Transform. No Entities / Collections type.

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
            // Binary search the segment table (cumulative starts are sorted).
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

        /// <summary>ChairPinSystem.MoveChair's pose: the grip top rides ON the cable, yaw from the travel tangent (plumb hang;
        /// the current rotation is only the degenerate-tangent fallback). Returns InPlace (transform untouched) when the chair
        /// already stands there (1 mm, |dot| &gt; 0.99999), else Moved with <paramref name="transform"/> updated.</summary>
        public static byte Pose(float3 position, float3 travelTangent, float gripTop, ref Transform transform)
        {
            position.y -= gripTop;
            quaternion rotation = NetUtils.GetNodeRotation(travelTangent, transform.m_Rotation);
            if (math.distancesq(position, transform.m_Position) < 1e-6f &&
                math.abs(math.dot(rotation, transform.m_Rotation)) > 0.99999f)
                return InPlace;
            transform.m_Position = position;
            transform.m_Rotation = rotation;
            return Moved;
        }

        /// <summary>A box around everything a loop's chairs can occupy: the segments' control points (a bezier lies in their
        /// convex hull) and each arc's circle box; the caller grows it by the hang below the rope. The far-loop pace measures
        /// the camera's distance to it.</summary>
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
