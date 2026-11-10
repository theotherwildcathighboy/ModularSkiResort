// RunsFirstPlanner rater bands (port round 2, step R2-p1): the band the mod's rater gives a run's outline, the band the game shows and
// scorecard v7 reads. The scorecard rates a raw plan's run on a ribbon around its centerline (RunsFirst.Score.cs RibbonBand): the
// Catmull-Rom path at a 4 m step, a ribbon of constant half width (15 m for a run, 7.5 m for a graded element), its nodes at
// snapshot heights, SkiRunRater.Rate. A ribbon that cannot be built or rated, and a rated ribbon whose mask is not connected, rate
// double black. RaterBand is that rule line for line (a mod file cannot call the host), so the planner and the scorecard agree bit
// for bit on the same points. The answers are cached per polyline (by its points), and each new rating polls the cancel token.
// Pure C# 9, net48-clean.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class RunsFirstPlanner
    {
        private const float RaterStepM = 4f;          // the scorecard's RibbonStepM: the realizer's centerline step
        private const float RaterRunHalfM = 15f;      // the scorecard's RibbonHalfWidthM: a run is rated as a 30 m piste

        // Which band decisions read the rater (RunsFirstOptions.RaterBands, one bit each; 0: the planner's own predictors, the plans
        // before this step). On by default: 1, 2, 128. The others were measured and left off (the option's comment)
        private const int RaterResult = 1;       // the result's band of a plain run (what the realizer plans and the game shows)
        private const int RaterGraph = 2;        // the ski graph: way home, beginner terrain, learning km, the keeps of every cut
        private const int RaterGreen = 4;        // green routes: beginner ways down and pods
        private const int RaterValue = 8;        // the serve pick's value (band weight of a candidate run)
        private const int RaterLink = 16;        // a link run between lifts may not be double black
        private const int RaterChord = 32;       // a moved run keeps its band; a chair's own green run under its line
        private const int RaterLine = 64;        // the first pick's line score (band weight) and its weak test
        private const int RaterLearnRuns = 128;  // a learning chair's further green runs
        private const int RaterChairRun = 256;   // a learning chair's own first run (the route under the chair)

        /// <summary>The rater's state for one plan: its buffers, the cache of rated polylines and the cancel token.</summary>
        private sealed class BandRater
        {
            public readonly SlopeGrid Grid = new SlopeGrid();
            public readonly SlopeResult Res = new SlopeResult();
            public readonly Dictionary<ulong, (float2[] Pts, float Half, SlopeBand Band)> Cache = new Dictionary<ulong, (float2[], float, SlopeBand)>();
            public AutoPlanCancel Cancel;
            public int Bits;                                  // RunsFirstOptions.RaterBands
            public bool Use(int bit) => (Bits & bit) != 0;
            public int Rated, Hits, Unrated, NotConnected;   // a report only
        }

        /// <summary>The band the mod's rater gives the ribbon of a centerline (scorecard v7's RibbonBand, bit-exact): DoubleBlack when
        /// the ribbon cannot be built or rated or its mask is not connected; Flat reads green. halfWidth: RaterRunHalfM for a run.</summary>
        private static SlopeBand RaterBand(Grid g, List<float2> pts, float halfWidth = RaterRunHalfM)
        {
            BandRater br = g.Rater;
            ulong key = KeyOf(pts, halfWidth);
            if (br.Cache.TryGetValue(key, out var hit) && hit.Half == halfWidth && Same(hit.Pts, pts))
            {
                br.Hits++;
                return hit.Band;
            }
            if (br.Cancel != null && br.Cancel.Requested)
                throw new PlanCanceled();   // each rating takes a few ms: a poll per rating keeps the cancel bound
            SlopeBand band = RateRibbon(g.S, pts, halfWidth, br);
            br.Rated++;
            br.Cache[key] = (pts.ToArray(), halfWidth, band);
            return band;
        }

        /// <summary>Scorecard v7's RibbonBand (RunsFirst.Score.cs: RibbonOf, RateRing, the DoubleBlack rule), line for line.</summary>
        private static SlopeBand RateRibbon(HeightSnapshot snap, List<float2> pts, float halfWidth, BandRater br)
        {
            GradePath path = pts == null || pts.Count < 2 ? null : GradePath.Build(pts, RaterStepM);
            float2[] ring = path == null ? null : GradeRibbon.Build(path, halfWidth, out GradeRibbonFail _);
            if (ring == null || ring.Length < 3)
            {
                br.Unrated++;
                return SlopeBand.DoubleBlack;
            }
            var nodes = new float3[ring.Length];
            for (int i = 0; i < ring.Length; i++)
                nodes[i] = new float3(ring[i].x, snap.Sample(new float3(ring[i].x, 0f, ring[i].y)), ring[i].y);
            var sampler = new SnapshotSampler(snap);
            RateStatus rs = SkiRunRater.Rate(nodes, nodes.Length, snap.Origin, ref sampler, snap.TexelM, 0, br.Grid, null, br.Res,
                out int _, out float _, out float _);
            if (rs != RateStatus.Rated || sampler.Misses > 0)
            {
                br.Unrated++;
                return SlopeBand.DoubleBlack;
            }
            if (!br.Res.Connected)
            {
                br.NotConnected++;
                return SlopeBand.DoubleBlack;
            }
            return br.Res.Band == SlopeBand.Flat ? SlopeBand.Green : br.Res.Band;
        }

        /// <summary>FNV-1a 64 over the half width and the points' bits.</summary>
        private static ulong KeyOf(List<float2> pts, float halfWidth)
        {
            ulong h = 14695981039346656037UL;
            void U(float f)
            {
                h ^= math.asuint(f);
                h *= 1099511628211UL;
            }
            U(halfWidth);
            for (int i = 0; i < pts.Count; i++)
            {
                U(pts[i].x);
                U(pts[i].y);
            }
            return h;
        }

        private static bool Same(float2[] a, List<float2> b)
        {
            if (a.Length != b.Count)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i].x != b[i].x || a[i].y != b[i].y)
                    return false;
            return true;
        }

        /// <summary>A plain run's band for a decision: the rater's when the decision reads it (RaterBands), else the planner's own
        /// predictor (the 85th-percentile ground band, scorecards v3 to v6).</summary>
        private static SlopeBand SeenBand(Grid g, List<float2> pts, int bit) => g.Rater.Use(bit) ? RaterBand(g, pts) : GroundBand(g, pts);

        /// <summary>As SeenBand, where the decision's own predictor is the 30 m pitch band (RunBand).</summary>
        private static SlopeBand SeenRunBand(Grid g, List<float2> pts, int bit) => g.Rater.Use(bit) ? RaterBand(g, pts) : RunBand(g, pts);

        /// <summary>A route reads green for a green run: by the ground (GroundBand), and by the rater too when green routes read it
        /// (the rater is almost never easier than the ground band, so the ground band is a cheap first test).</summary>
        private static bool IsGreen(Grid g, List<float2> pts) => GroundBand(g, pts) == SlopeBand.Green && ReadsGreen(g, pts, RaterGreen);

        /// <summary>The rater reads the run green, or green routes do not read the rater. Called on the points the run will have.</summary>
        private static bool ReadsGreen(Grid g, List<float2> pts, int bit) => !g.Rater.Use(bit) || RaterBand(g, pts) == SlopeBand.Green;
    }
}
