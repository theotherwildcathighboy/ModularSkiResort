// Pure arithmetic of the chair mover's far-loop pace and lane scope. No Game or Entities type (Unity.Mathematics and System only)
// so tests/LiftBatch links it (LoopPaceTests.cs).
// A riding chair's place is a pure function of the render clock, s = (offset + speed x TimeS) % L, so skipped passes leave no
// per-chair state to drift. A far loop is written every Stride-th pass (the largest k <= MaxStride whose travel stays under
// MaxStepRadians x distance); a loop that comes back near is written at once at the exact unthrottled pose.
// Lane scope: owners = the census's line edges plus each edge's two nodes, once each (OwnersOf); the load-time watchdog compares
// the scoped harvest with the full scan and a miss switches that system back to the full scan for the load.
// Engine notes: EngineNotes/LiftMoverMath.md

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    internal static class LiftMoverMath
    {
        // Constants (printed in the [cost] / [pace] lines; the dev tab can change the settings at runtime)
        /// <summary>The bounds-refresh travel of a near loop.</summary>
        public const double BoundsRefreshMeters = 2.0;
        /// <summary>No loop nearer than this to the camera is ever throttled.</summary>
        public const float FarMinMetersDefault = 500f;
        /// <summary>A far loop is written at least every this many moving passes.</summary>
        public const int MaxStrideDefault = 8;
        /// <summary>The largest on-screen step allowed for a far chair, as an angle: 0.5 mrad = 0.7-1.2 px (engine notes item 2).</summary>
        public const double MaxStepRadiansDefault = 0.0005;

        /// <summary>The far-loop pace settings (the dev tab can switch the pace off or change the numbers).</summary>
        internal struct PaceSettings
        {
            public bool Enabled;
            public float FarMinM;
            public int MaxStride;
            public double MaxStepRadians;

            public static PaceSettings Default => new PaceSettings
            {
                Enabled = true,
                FarMinM = FarMinMetersDefault,
                MaxStride = MaxStrideDefault,
                MaxStepRadians = MaxStepRadiansDefault,
            };
        }

        /// <summary>One loop's pace state (transient; reset at every loop-cache rebuild by Seed).</summary>
        internal struct LoopPace
        {
            public int SinceWrite;          // moving passes since this loop was last written
            public int Stride;              // the stride of the last pass (1 = near)
            public double LastBoundsTimeS;  // the render clock at this loop's last bounds tag
        }

        /// <summary>Engine notes item 1: the loop distance of a chair, (offset + speed x timeS) % length, the mover's expression bit for bit
        /// (PhaseOf's product, then the add and the modulo).</summary>
        public static double LoopPosition(double offset, double speedMps, double timeS, double lengthM)
        {
            double phase = speedMps * timeS;
            return (offset + phase) % lengthM;
        }

        /// <summary>Euclidean distance from a point to an axis-aligned box (0 inside).</summary>
        public static float BoxDistance(float3 point, float3 boxMin, float3 boxMax)
        {
            float3 d = math.max(0f, math.max(boxMin - point, point - boxMax));
            return math.length(d);
        }

        /// <summary>Engine notes item 2: the stride of a loop this pass. 1 (written every pass) when the pace is off, the loop is nearer than
        /// FarMinM, it does not move, or the clock did not advance; else the largest k in [1, MaxStride] with
        /// k x speed x dt <= MaxStepRadians x distance. NaN-safe (NaN distance gives 1).</summary>
        public static int StrideFor(float distanceM, double speedMps, double passDtS, in PaceSettings settings)
        {
            if (!settings.Enabled || settings.MaxStride <= 1 || !(distanceM >= settings.FarMinM) ||
                !(speedMps > 0.0) || !(passDtS > 0.0))
                return 1;
            double perPass = speedMps * passDtS;
            double allowed = distanceM * settings.MaxStepRadians;
            double k = Math.Floor(allowed / perPass);
            if (!(k >= 1.0))
                return 1;
            return k >= settings.MaxStride ? settings.MaxStride : (int)k;
        }

        /// <summary>Rebuild seed: far loops are staggered across passes (loop index x 3 mod MaxStride passes already "elapsed"); the
        /// bounds clock starts at the rebuild (the rebuild tags every rider Updated itself).</summary>
        public static LoopPace Seed(int loopIndex, int maxStride, double timeS)
        {
            int m = maxStride > 1 ? maxStride : 1;
            return new LoopPace
            {
                SinceWrite = (int)((uint)(loopIndex * 3) % (uint)m),
                Stride = 1,
                LastBoundsTimeS = timeS,
            };
        }

        /// <summary>Engine notes item 2: one moving pass for one running loop. <paramref name="write"/> = write its chairs this pass;
        /// <paramref name="tagBounds"/> = tag them Updated this pass (only when written).</summary>
        public static void Decide(ref LoopPace pace, int stride, double timeS, double speedMps,
            out bool write, out bool tagBounds)
        {
            if (stride < 1)
                stride = 1;
            pace.SinceWrite++;
            write = stride == 1 || pace.SinceWrite >= stride;
            pace.Stride = stride;
            tagBounds = false;
            if (!write)
                return;
            pace.SinceWrite = 0;
            double travel = Math.Abs(timeS - pace.LastBoundsTimeS) * speedMps;   // abs: a clock step back moves chairs too
            if (travel >= BoundsRefreshMeters * stride)
            {
                tagBounds = true;
                pace.LastBoundsTimeS = timeS;
            }
        }

        /// <summary>Engine notes item 3: nearest-rank percentile (p in [0, 1]) of the first <paramref name="count"/> values, on a sorted copy
        /// in <paramref name="scratch"/> (sorted once per call; callers ask for p50 / p95 / max together through Percentiles). 0 for an
        /// empty sample.</summary>
        public static void Percentiles(float[] values, int count, List<float> scratch, out float p50, out float p95, out float max)
        {
            scratch.Clear();
            int n = values == null ? 0 : Math.Min(count, values.Length);
            for (int i = 0; i < n; i++)
                scratch.Add(values[i]);
            if (scratch.Count == 0)
            {
                p50 = p95 = max = 0f;
                return;
            }
            scratch.Sort();
            p50 = scratch[Rank(0.50, scratch.Count)];
            p95 = scratch[Rank(0.95, scratch.Count)];
            max = scratch[scratch.Count - 1];
        }

        /// <summary>Nearest rank: ceil(p x n) - 1, clamped to [0, n - 1].</summary>
        public static int Rank(double p, int n)
        {
            int r = (int)Math.Ceiling(p * n) - 1;
            return r < 0 ? 0 : r >= n ? n - 1 : r;
        }
    }

    /// <summary>Engine notes item 4: which entities the scoped cable-lane harvest visits, and the load-time watchdog's compare. Generic over
    /// the id type so the Game side passes Entity and the tests pass int.</summary>
    internal static class LiftLaneScope
    {
        /// <summary>A line edge as the census holds it: the edge and its two end nodes (a default value = none).</summary>
        internal struct LineEdge<T>
        {
            public T Edge, Start, End;
        }

        /// <summary>Every edge, then its start and end node, each once, in edge order; default (null) ids are skipped.
        /// Returns the number added to <paramref name="into"/> (which is not cleared; <paramref name="seen"/> is).</summary>
        public static int OwnersOf<T>(IReadOnlyList<LineEdge<T>> edges, List<T> into, HashSet<T> seen) where T : IEquatable<T>
        {
            seen.Clear();
            int before = into.Count;
            T none = default;
            for (int i = 0; i < edges.Count; i++)
            {
                LineEdge<T> e = edges[i];
                Add(e.Edge, none, into, seen);
                Add(e.Start, none, into, seen);
                Add(e.End, none, into, seen);
            }
            return into.Count - before;
        }

        private static void Add<T>(T id, T none, List<T> into, HashSet<T> seen) where T : IEquatable<T>
        {
            if (id == null || id.Equals(none))
                return;
            if (seen.Add(id))
                into.Add(id);
        }

        /// <summary>The watchdog: lanes the full scan kept that the scoped harvest did not (<paramref name="missed"/>, full scan order)
        /// and lanes only the scoped harvest kept (<paramref name="extra"/>, its order; a lane the scan would have rejected cannot appear
        /// when both apply the same filter, so extra &gt; 0 means the filters drifted).</summary>
        public static void CompareHarvests<T>(IReadOnlyList<T> full, IReadOnlyList<T> scoped, List<T> missed, List<T> extra)
            where T : IEquatable<T>
        {
            missed.Clear();
            extra.Clear();
            var inScoped = new HashSet<T>();
            for (int i = 0; i < scoped.Count; i++)
                inScoped.Add(scoped[i]);
            var inFull = new HashSet<T>();
            for (int i = 0; i < full.Count; i++)
            {
                inFull.Add(full[i]);
                if (!inScoped.Contains(full[i]))
                    missed.Add(full[i]);
            }
            for (int i = 0; i < scoped.Count; i++)
                if (!inFull.Contains(scoped[i]))
                    extra.Add(scoped[i]);
        }
    }
}
