// The cat track tool's FULL solve as one pure function (2026-11-05, DECISIONS 601 f: hand-test note 20, "massive hitches" while adding
// points). Everything that is slow and does not need the engine: the raster over the whole rect, the crossed-run band rating (twice per
// allowance step), the guards over every changed texel, the along-track crossing check and the run outline. The tool takes the engine
// inputs on the main thread (heights and water of the rect as a HeightSnapshot, the obstacles, the profile) and runs this either on a
// worker (the debounced preview) or inline (Enter, and the fallback), so the plan the player sees and the plan Enter checks are the
// same function of the same inputs. The pure parts it calls (CatTrackGrade.cs) are unchanged.
// Pure file: System, Unity.Mathematics and the rater (the tests/GradeBatch rule). No Entities, no Unity API, no log on the worker.
// Engine notes: EngineNotes/CatTrackGradeToolSystem.md (Solve split)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>A cancel flag the worker polls between stages (one raster build is the latency of a cancel).</summary>
    public sealed class GradeCancel
    {
        private volatile bool m_Requested;

        public bool Requested => m_Requested;

        public void Request() => m_Requested = true;
    }

    /// <summary>What the full solve reads. All of it is private to the solve once handed over (the worker owns Obstacles: it fills
    /// WetTexels).</summary>
    public sealed class GradeSolveInput
    {
        public GradeMap Map;
        public GradePath Path;
        public float[] Ground;
        public GradeParams Params;
        public int4 Rect;
        public float Reach;
        /// <summary>Lots, lift lines and crossed run outlines of the rect (CollectObstacles); WetTexels is filled by the solve.</summary>
        public GradeObstacles Obstacles;
        /// <summary>The base terrain height at a world point (xz used): texel centers of the rect, and anywhere the crossed runs' rating
        /// reads outside it.</summary>
        public Func<float3, float> Height;
        /// <summary>Is full-map texel (i, j) under water deeper than the wet threshold? Null = no water.</summary>
        public Func<int, int, bool> IsWet;
        /// <summary>The rater's lattice (SkiRunCatalogSystem): world position of texel (0, 0) and the texel size in meters.</summary>
        public double2 RateOrigin;
        public float RateTexel;
        public GradeCancel Cancel;
    }

    public sealed class GradeSolveResult
    {
        public bool Canceled;
        /// <summary>The profile of the chosen inside allowance, the raster built with it, and the params it used (PlanParams.InsideMaxM).</summary>
        public GradeProfile Prof;
        public GradeRaster Raster;
        public GradeParams PlanParams;
        public GradeGuardKind Guard;
        public float2 GuardAt;
        /// <summary>The chosen inside allowance in meters; -1 when no step keeps every crossed run's band; the params' own when no run is crossed.</summary>
        public float Allowance;
        public bool Crossing;
        public string BandText;
        public string StepText = string.Empty;
        /// <summary>The run outline of the flat top (GradeRibbon); null when there is a guard before it or the ring fails.</summary>
        public float2[] Ring;
        public double RasterMs, GuardMs, RingMs, TotalMs;
    }

    public static class GradeSolver
    {
        /// <summary>The profile of the first inside-allowance step: what the preview shows while the full solve is out. Equal to the full
        /// solve's profile when no crossed run changes the allowance (always without a crossing).</summary>
        public static GradeProfile QuickProfile(GradePath path, float[] ground, GradeParams p, IList<float2[]> runs)
        {
            GradeParams pa = p.Clone();
            pa.InsideMaxM = GradeParams.InsideAllowanceSteps[0];
            return GradeProfile.Solve(path, ground, pa, GradeRaster.PinTolerance(path, runs, pa));
        }

        /// <summary>The full solve: raster at the largest inside allowance that keeps every crossed run's band (all steps tried, DECISIONS
        /// 420), then the guards, the along-track crossing check and the run outline. Canceled = true when <see cref="GradeSolveInput.Cancel"/>
        /// was requested between stages (the other fields are then incomplete).</summary>
        public static GradeSolveResult Run(GradeSolveInput inp)
        {
            var res = new GradeSolveResult();
            var sw = Stopwatch.StartNew();
            GradeMap map = inp.Map;
            GradePath path = inp.Path;
            GradeParams p = inp.Params;
            IList<float2[]> runs = inp.Obstacles.Runs;
            Func<int, int, float> baseHeight = (i, j) =>
            {
                float2 c = map.TexelCenter(i, j);
                return inp.Height(new float3(c.x, 0f, c.y));
            };
            bool[] entered = GradeRaster.EnteredOutlines(path, runs);
            bool crossing = false;
            for (int k = 0; entered != null && k < entered.Length; k++)
                crossing |= entered[k];
            res.Crossing = crossing;
            sbyte[] bandsBefore = null;
            var steps = new StringBuilder();
            GradeParams pa = p;
            GradeProfile prof = null;
            GradeRaster raster = null;
            string bandText = null;

            bool BuildAt(float a)
            {
                if (Canceled(inp))
                    return true;   // ends ChooseInsideAllowance at once; the result is dropped below
                double t0 = sw.Elapsed.TotalMilliseconds;
                pa = p.Clone();
                pa.InsideMaxM = a;
                prof = GradeProfile.Solve(path, inp.Ground, pa, GradeRaster.PinTolerance(path, runs, pa));
                raster = GradeRaster.Build(map, inp.Rect, path, prof, pa, inp.Reach, baseHeight, runs);
                if (!crossing)
                    return true;
                if (bandsBefore == null)
                    bandsBefore = RateCrossed(inp, raster, entered, false);
                bool kept = SameBands(bandsBefore, RateCrossed(inp, raster, entered, true), out bandText);
                steps.Append(steps.Length > 0 ? "; " : string.Empty)
                     .AppendFormat(CultureInfo.InvariantCulture, "{0:F1} m {1} {2:F0} ms", a, kept ? "kept" : "changed", sw.Elapsed.TotalMilliseconds - t0);
                return kept;
            }

            float chosen = GradeRaster.ChooseInsideAllowance(BuildAt);
            res.RasterMs = sw.Elapsed.TotalMilliseconds;
            if (Canceled(inp))
            {
                res.Canceled = true;
                return res;
            }
            res.Allowance = chosen;
            res.BandText = bandText;
            res.StepText = steps.ToString();
            res.Prof = prof;
            res.Raster = raster;
            res.PlanParams = pa;

            double g0 = sw.Elapsed.TotalMilliseconds;
            res.Guard = CheckGuards(inp, raster, out res.GuardAt);
            if (res.Guard == GradeGuardKind.None && runs.Count > 0)
            {
                int i = raster.AlongTrackSteepSample(map, path, runs, pa.CrossBlendM, pa.MaxGrade);
                if (i >= 0)
                {
                    res.Guard = GradeGuardKind.CrossingSteep;
                    res.GuardAt = path.Xz[i];
                }
            }
            if (res.Guard == GradeGuardKind.None && chosen < 0f)
            {
                res.Guard = GradeGuardKind.CrossedRunBand;
                res.GuardAt = path.Xz[path.Count / 2];
            }
            res.GuardMs = sw.Elapsed.TotalMilliseconds - g0;
            if (Canceled(inp))
            {
                res.Canceled = true;
                return res;
            }

            // The run outline (the flat top as one ring): a ring that touches itself is a guard like the others, so Enter refuses before
            // anything is emitted or graded.
            if (res.Guard == GradeGuardKind.None)
            {
                double r0 = sw.Elapsed.TotalMilliseconds;
                res.Ring = GradeRibbon.Build(path, 0.5f * p.TopWidthM, out GradeRibbonFail ringFail, out float2 ringAt);
                res.RingMs = sw.Elapsed.TotalMilliseconds - r0;
                if (ringFail != GradeRibbonFail.None)
                {
                    res.Ring = null;
                    res.Guard = GradeGuardKind.RunShape;
                    res.GuardAt = ringAt;
                }
            }
            res.TotalMs = sw.Elapsed.TotalMilliseconds;
            return res;
        }

        private static bool Canceled(GradeSolveInput inp) => inp.Cancel != null && inp.Cancel.Requested;

        /// <summary>Water on the changed texels, then GradeGuards.Check over everything the obstacles hold.</summary>
        private static GradeGuardKind CheckGuards(GradeSolveInput inp, GradeRaster r, out float2 where)
        {
            GradeObstacles obs = inp.Obstacles;
            obs.WetTexels.Clear();
            if (inp.IsWet != null)
            {
                for (int j = 0; j < r.H; j++)
                {
                    for (int i = 0; i < r.W; i++)
                    {
                        if (r.Zone[j * r.W + i] != 0 && inp.IsWet(r.X0 + i, r.Z0 + j))
                            obs.WetTexels.Add(new int2(r.X0 + i, r.Z0 + j));
                    }
                }
            }
            return GradeGuards.Check(inp.Map, r, obs, out where);
        }

        /// <summary>The rated band of each entered crossed run on the raster's Before (or After) terrain, the catalog's own rater
        /// (SkiRunRater, the catalog's origin and texel); -3 for an outline the path does not enter or a rating that did not finish.</summary>
        public static sbyte[] RateCrossed(GradeSolveInput inp, GradeRaster r, bool[] entered, bool after)
        {
            IList<float2[]> runs = inp.Obstacles.Runs;
            var bands = new sbyte[runs.Count];
            Func<float3, float> height = inp.Height;
            var sampler = new GradeRaster.Sampler { Map = inp.Map, R = r, After = after, Outside = height };
            for (int k = 0; k < bands.Length; k++)
            {
                bands[k] = -3;
                float2[] poly = runs[k];
                if (entered == null || !entered[k] || poly == null || poly.Length < 3)
                    continue;
                var nodes = new float3[poly.Length];
                for (int i = 0; i < poly.Length; i++)
                    nodes[i] = new float3(poly[i].x, sampler.Sample(new float3(poly[i].x, 0f, poly[i].y)), poly[i].y);
                var result = new SlopeResult();
                RateStatus st = SkiRunRater.Rate(nodes, nodes.Length, inp.RateOrigin, ref sampler, inp.RateTexel, 0, new SlopeGrid(), null, result,
                    out int _, out float _, out float _);
                if (st == RateStatus.Rated)
                    bands[k] = (sbyte)result.Band;
            }
            return bands;
        }

        public static bool SameBands(sbyte[] before, sbyte[] after, out string text)
        {
            var sb = new StringBuilder();
            bool same = true;
            for (int k = 0; k < before.Length; k++)
            {
                if (before[k] == -3 || after[k] == -3)
                    continue;
                sb.Append(sb.Length > 0 ? ", " : string.Empty).Append("run ").Append(k).Append(' ').Append((SlopeBand)before[k]);
                if (before[k] != after[k])
                {
                    same = false;
                    sb.Append(" -> ").Append((SlopeBand)after[k]);
                }
                else
                    sb.Append(" kept");
            }
            text = sb.Length > 0 ? "crossed runs: " + sb : "no crossed run rated";
            return same;
        }
    }
}
