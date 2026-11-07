// Tree line estimate for the Quick runs tree filler: the ground height below which most of the map's own trees grow. It is read from the
// trees in and around a drawn region. Pure (System, System.Collections.Generic and Unity.Mathematics only) so tests/GladeBatch links it.
// Contract: a pure function of (ring, tree positions, ground range). It never throws and never logs. The inside test is the glade
// planner's own (a Thin probe). The percentile is LiftMoverMath.Rank (nearest rank). The game side draws the result with
// SnowLineRules.MarchingSquares and feeds it into GladeMask.TreeLine.
// Engine notes: EngineNotes/SkiGladeTreeLine.md

using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>Where a tree line estimate came from (best first).</summary>
    public enum TreeLineSource : byte
    {
        /// <summary>At least MinTrees trees inside the region: their Quantile height.</summary>
        Region,
        /// <summary>Too few trees inside, but at least MinTrees within AroundM of the region's bounds: their Quantile height.</summary>
        Around,
        /// <summary>MinSparse..MinTrees-1 trees around: the highest of them.</summary>
        Sparse,
        /// <summary>Fewer than MinSparse trees around (or a degenerate ring): no tree line, the whole ground range is filled.</summary>
        None,
    }

    public struct TreeLineEstimate
    {
        /// <summary>Ground height (m), clamped into [groundMin, groundMax] when that range is finite. +Inf for None without a range.</summary>
        public float Height;
        public TreeLineSource Source;
        /// <summary>Trees inside the region / within AroundM (inside included) that the estimate saw.</summary>
        public int TreesInside, TreesAround;
    }

    public static class SkiGladeTreeLine
    {
        /// <summary>Share of the sampled trees that stand at or below the estimate.</summary>
        public const double Quantile = 0.95;
        /// <summary>Trees needed for a Quantile read (fewer: the next source).</summary>
        public const int MinTrees = 30;
        /// <summary>Trees needed for the Sparse source (the highest tree).</summary>
        public const int MinSparse = 5;
        /// <summary>Reach (m) of the "around" sample beyond the region's bounding box.</summary>
        public const float AroundM = 500f;

        /// <summary>Estimate from <paramref name="trees"/> (wild tree positions, with y = the tree's height, i.e. its ground). The caller
        /// collects them from the region's bounds grown by AroundM. The method ignores positions outside that box or non-finite.
        /// <paramref name="groundMin"/> / <paramref name="groundMax"/> = the region's ground range (NaN = unknown).</summary>
        public static TreeLineEstimate Estimate(float3[] ring, float3[] trees, float groundMin, float groundMax)
        {
            TreeLineEstimate r = new TreeLineEstimate { Source = TreeLineSource.None, Height = float.PositiveInfinity };
            int n = trees != null ? trees.Length : 0;

            // Inside = the planner's own polygon test: a Thin probe with fraction 1 selects every finite tree inside.
            var probe = new GladePlan();
            var p = new GladeParams { Mode = GladeMode.Thin, ThinFraction = 1f };
            GladeStatus st = SkiGladePlanner.Plan(ring, null, 1u, in p, null, trees, null, null, probe);
            if ((st == GladeStatus.Ok || st == GladeStatus.Clamped) && n > 0)
            {
                double4 box = Bounds(ring);
                var inside = new List<float>(probe.Thinned.Count);
                for (int i = 0; i < probe.Thinned.Count; i++)
                    inside.Add(trees[probe.Thinned[i]].y);
                var around = new List<float>();
                for (int i = 0; i < n; i++)
                {
                    float3 t = trees[i];
                    if (!math.isfinite(t.x) || !math.isfinite(t.y) || !math.isfinite(t.z))
                        continue;
                    if (t.x < box.x - AroundM || t.z < box.y - AroundM || t.x > box.z + AroundM || t.z > box.w + AroundM)
                        continue;
                    around.Add(t.y);
                }
                r.TreesInside = inside.Count;
                r.TreesAround = around.Count;
                if (inside.Count >= MinTrees)
                {
                    r.Height = QuantileOf(inside);
                    r.Source = TreeLineSource.Region;
                }
                else if (around.Count >= MinTrees)
                {
                    r.Height = QuantileOf(around);
                    r.Source = TreeLineSource.Around;
                }
                else if (around.Count >= MinSparse)
                {
                    around.Sort();
                    r.Height = around[around.Count - 1];
                    r.Source = TreeLineSource.Sparse;
                }
            }

            if (r.Source == TreeLineSource.None)
            {
                if (math.isfinite(groundMax))
                    r.Height = groundMax;
                return r;
            }
            if (math.isfinite(groundMax) && r.Height > groundMax)
                r.Height = groundMax;
            if (math.isfinite(groundMin) && r.Height < groundMin)
                r.Height = groundMin;
            return r;
        }

        private static float QuantileOf(List<float> values)
        {
            values.Sort();
            return values[LiftMoverMath.Rank(Quantile, values.Count)];
        }

        private static double4 Bounds(float3[] ring)
        {
            double4 b = new double4(double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
            for (int i = 0; i < ring.Length; i++)
            {
                if (!math.isfinite(ring[i].x) || !math.isfinite(ring[i].z))
                    continue;
                b.x = math.min(b.x, ring[i].x);
                b.y = math.min(b.y, ring[i].z);
                b.z = math.max(b.z, ring[i].x);
                b.w = math.max(b.w, ring[i].z);
            }
            return b;
        }
    }
}
