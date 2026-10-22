// LiftMoverMath — the PURE arithmetic of the chair mover's scale round (R-3 / R-4 of
// docs/research/lifts/lift-architecture-review-2026-10-22.md; report docs/research/lifts/lift-scale-2026-10-22.md, DECISIONS 369).
// No Game or Entities type: Unity.Mathematics and System only, so tests/LiftBatch links it (LoopPaceTests.cs, MP-1..MP-8).
//
// 1. LOOP POSITION. A riding chair's place on its loop is a pure function of the render clock:
//      s = (offset + speed × TimeS) % L        (ChairPinSystem: PhaseOf = speed × TimeS, then the modulo)
//    so a loop that skipped passes is exactly where an unthrottled loop is on the pass it is written again ("catch-up is
//    exact": nothing accumulates, there is no per-chair state to drift). LoopPosition is that expression, bit for bit.
//
// 2. FAR-LOOP PACE (R-3). A loop whose box is at least FarMinM from the camera is written every Stride-th moving pass, where
//    Stride is the largest k ≤ MaxStride for which k passes of travel stay under MaxStepRadians × distance (a far chair never
//    steps more than about one pixel: 0.0005 rad is 0.73 px at a 60° vertical field of view on 1,440 lines, 1.2 px at 35°).
//    Travel per pass = speed × the render-clock delta of the pass (it grows with sim speed and shrinks with fps, so the stride
//    adapts to both). Near loops (Stride 1) are written every pass, exactly as before this round.
//      * A far loop counts passes since its last write (SinceWrite) and is written when SinceWrite ≥ Stride; the countdown is
//        seeded per loop at a rebuild (loop index × 3 mod MaxStride) so far loops do not all land on the same pass.
//      * The pass a loop comes back near (Stride 1) writes it at once, at the exact unthrottled pose (item 1).
//    BOUNDS REFRESH follows the pace: a loop's chairs get one Updated tag when its own travel since its last tag reaches
//    BoundsRefreshMeters × Stride (2 m near, at most 16 m far), and only on a pass that writes it. Per loop, with the loop's own
//    speed (before this round: one global cadence at the FASTEST loop's speed, so a fixed-grip loop next to a gondola was
//    tagged 2.2x too often). A loop that comes back near is tagged on that very pass whenever its untagged travel is ≥ 2 m
//    (the near rule), so near the camera CullingInfo / the search tree are never more than 2 m + one pass of travel stale.
//
// 3. COST PERCENTILES for the "[cost] chair mover" line: p50 / p95 / max of a sample (nearest-rank, on a copy).
//
// 4. LANE SCOPE (R-4). The cable lanes of the lifts hang off the lift-line EDGES and their end NODES (SubLane buffers). The
//    owners to visit are the census's line edges plus each edge's two nodes, each once (OwnersOf). The load-time watchdog
//    compares the full city-wide scan with the scoped harvest (CompareHarvests): a lane the scan kept and the scoped harvest
//    missed is a WARN and switches that system back to the full scan for the rest of the load.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    internal static class LiftMoverMath
    {
        // ── constants (printed in the [cost] / [pace] lines; the dev tab can change the settings at runtime) ──────────────
        /// <summary>The bounds-refresh travel of a NEAR loop (ChairPinSystem BoundsRefreshMeters, unchanged).</summary>
        public const double BoundsRefreshMeters = 2.0;
        /// <summary>No loop nearer than this to the camera is ever throttled.</summary>
        public const float FarMinMetersDefault = 500f;
        /// <summary>A far loop is written at least every this many moving passes.</summary>
        public const int MaxStrideDefault = 8;
        /// <summary>The largest on-screen step allowed for a far chair, as an angle: 0.5 mrad ≈ 0.7-1.2 px (header item 2).</summary>
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

        /// <summary>Header item 1: the loop distance of a chair, (offset + speed × timeS) % length, the mover's expression bit
        /// for bit (PhaseOf's product, then the add and the modulo).</summary>
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

        /// <summary>Header item 2: the stride of a loop this pass. 1 (written every pass) when the pace is off, the loop is
        /// nearer than FarMinM, it does not move, or the clock did not advance; else the largest k in [1, MaxStride] with
        /// k × speed × dt ≤ MaxStepRadians × distance. NaN-safe (NaN distance → 1).</summary>
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

        /// <summary>Rebuild seed: far loops are staggered across passes (loop index × 3 mod MaxStride passes already
        /// "elapsed"); the bounds clock starts at the rebuild (the rebuild tags every rider Updated itself).</summary>
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

        /// <summary>Header item 2: one moving pass for one running loop. <paramref name="write"/> = write its chairs this
        /// pass; <paramref name="tagBounds"/> = tag them Updated this pass (only when written).</summary>
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

        /// <summary>Header item 3: nearest-rank percentile (p in [0, 1]) of the first <paramref name="count"/> values, on a
        /// sorted copy in <paramref name="scratch"/> (sorted once per call; the caller asks for p50 / p95 / max together through
        /// Percentiles). 0 for an empty sample.</summary>
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

        /// <summary>Nearest rank: ceil(p × n) − 1, clamped to [0, n − 1].</summary>
        public static int Rank(double p, int n)
        {
            int r = (int)Math.Ceiling(p * n) - 1;
            return r < 0 ? 0 : r >= n ? n - 1 : r;
        }
    }

    /// <summary>Header item 4 (R-4): which entities the scoped cable-lane harvest visits, and the load-time watchdog's compare.
    /// Generic over the id type so the Game side passes Entity and the tests pass int.</summary>
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

        /// <summary>The watchdog: lanes the FULL scan kept that the SCOPED harvest did not (<paramref name="missed"/>, full
        /// scan order) and lanes only the scoped harvest kept (<paramref name="extra"/>, its order: a lane the scan would
        /// have rejected cannot appear when both apply the same filter, so extra &gt; 0 means the filters drifted).</summary>
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
