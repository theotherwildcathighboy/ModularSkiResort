// SkiRunRater — THE Plan → Fill → Classify path of a ski run's rating, moved out of
// SkiRunDrawFeedbackSystem so the drawing tooltip and the hub's built-run rating cache
// (SkiRunCatalogSystem, docs/research/hub-u2-spec.md §3.5) rate a polygon with the SAME code.
// Created 2026-10-03 (Round U2, implementer I-1).
//
// WHAT MOVED (bodies unchanged, only the terrain read became a type parameter):
//   * ShoelaceArea      ← SkiRunDrawFeedbackSystem.ShoelaceArea (was :2231-2239)
//   * FillHeights<T>    ← SkiRunDrawFeedbackSystem.FillHeights (was :1361-1464): the per-row Need
//                         RUN walk, the reuse copy from a source grid (same Step / Origin checked
//                         by the caller of FillHeights, here: Rate) and the sample at the world
//                         point (float)(Origin + (I0 + i)·Step) — now `sampler.Sample(new float3(wx,
//                         0f, wz))` instead of `TerrainUtils.SampleHeight(ref hd, …)`.
//   * Rate<T>           ← the middle of SkiRunDrawFeedbackSystem.Recompute (was :1216-1264):
//                         StepMultiple(area, texel, stepCurrent) (clamped to >= 1) → step = m·texel
//                         → target.DiagonalRule = m >= 2 (R2b §9.8: the classifier does not know m)
//                         → Plan → [plan ok] reuse guard (source non-null, identical Step and
//                         Origin) → FillHeights → Classify. A failed Plan is NOT classified (the
//                         feedback system never did either; the result keeps its previous contents).
//   The heights-ready check (heightmap created and >= MinHeightmapResolution wide) stays with each
//   caller (spec §3.5), and so does the caller's own part of the reuse guard (the feedback system's
//   verify age and forced resample): a caller that may not reuse passes reuseSource = null.
//
// PARITY (spec §3.5 "Gate"): tests/SlopeBatch links this file and proves, in the test runtime,
// that SkiRunRater.Rate on every field capture in tests/SlopeBatch/captures is bit-identical to
// the direct Plan → fill → Classify calls, that FillHeights is bit-identical to a verbatim copy of
// the pre-extraction body (with and without a reuse source), that ShoelaceArea is bit-identical to
// the old one, and that stepCurrent 0 and 1 give the same m at m = 1. CoreCLR vs the game's Mono bit
// equality is unverified (as for the classifier itself).
//
// SPEC DEVIATIONS (hub-u2-spec.md §2.1 lists the signatures; reported to the orchestrator):
//   * Rate takes `double2 origin` (the lattice origin, -hd.offset.xz): Plan cannot run without it
//     and the §2.1 signature has no parameter that could carry it.
//   * ShoelaceArea returns double (the §2.1 line says float): the moved body is double and
//     SkiSlopeClassifier.StepMultiple takes double — a float would move m at a budget boundary.
//   * An overload of Rate adds `out RateDetail detail`: the feedback system publishes the plan /
//     classify status, the reuse decision (it decides whether the verify stamp is inherited),
//     reused / sampled counts and the plan time; the §2.1 overload is kept and calls it.
//
// PURE FILE: `using System;` + `using Unity.Mathematics;` only (the tests/SlopeBatch rule); the
// classifier types (SlopeGrid, SlopeResult, SlopeStatus, SkiSlopeClassifier) come from
// SkiSlopeClassifier.cs, which is pure too. Timing uses System.Diagnostics.Stopwatch by its full
// name (no using; the type exists in net48 and net8 alike). No allocation except FillHeights'
// one-time growth of a too-small Heights array (as before; Plan sizes it in practice).

using System;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>The terrain read of a rating (spec §2.1): the height at a world point. The game's
    /// implementation is SkiRunDrawFeedbackSystem's TerrainHeightSampler (2-argument
    /// TerrainUtils.SampleHeight); test hosts pass their own.</summary>
    public interface IHeightSampler
    {
        float Sample(float3 world);
    }

    /// <summary>Outcome of a rating (spec §2.1). Rated = the classifier returned Ok, ShortRun or
    /// Polyline; NoTerrain = the plan was fine but the heights were not usable (Classify returned
    /// NonFinite or BadHeights); TooLarge = Plan refused the lattice (bbox &gt; 2^21 cells,
    /// SkiSlopeClassifier.cs:651); Failed = any other degenerate status (too few points, no
    /// cells, bad step, non-finite nodes).</summary>
    public enum RateStatus : byte
    {
        Rated,
        NoTerrain,
        TooLarge,
        Failed,
    }

    /// <summary>Everything Rate measured besides the §2.1 outputs (see the header's deviations).</summary>
    public struct RateDetail
    {
        /// <summary>Plan's return value.</summary>
        public SlopeStatus PlanStatus;
        /// <summary>The rating status: Classify's when Plan succeeded, else Plan's (exactly the
        /// value SkiRunDrawFeedbackSystem.Recompute held in `status`).</summary>
        public SlopeStatus Status;
        /// <summary>ShoelaceArea of the nodes (m², plan).</summary>
        public double AreaM2;
        /// <summary>m · texel.</summary>
        public float StepM;
        /// <summary>The reuse guard passed: a source was given and has the identical Step and
        /// lattice Origin (exact compare). False when Plan failed.</summary>
        public bool ReuseEnabled;
        /// <summary>Need heights copied from the source / sampled.</summary>
        public int Reused, Sampled;
        /// <summary>ShoelaceArea + StepMultiple + Plan, ms (Stopwatch timestamps).</summary>
        public float PlanMs;
    }

    public static class SkiRunRater
    {
        /// <summary>|shoelace area| of the first n points in xz, closing edge included; 0 below 3
        /// points. Moved verbatim from SkiRunDrawFeedbackSystem (was :2231-2239).</summary>
        public static double ShoelaceArea(float3[] ring, int n)
        {
            if (n < 3)
                return 0.0;
            double s = 0.0;
            for (int i = 0, k = n - 1; i < n; k = i++)
                s += (double)ring[k].x * ring[i].z - (double)ring[i].x * ring[k].z;
            return Math.Abs(s) * 0.5;
        }

        /// <summary>Plan returns Ok when it produced a lattice; the other two values are kept from
        /// the feedback system's original test (it never sees them from Plan).</summary>
        public static bool IsPlanOk(SlopeStatus s)
        {
            return s == SlopeStatus.Ok || s == SlopeStatus.Polyline || s == SlopeStatus.ShortRun;
        }

        /// <summary>A usable rating (ring Ok / ShortRun, or a polyline).</summary>
        public static bool IsRated(SlopeStatus s)
        {
            return s == SlopeStatus.Ok || s == SlopeStatus.ShortRun || s == SlopeStatus.Polyline;
        }

        /// <summary>Fills every Need height of grid g. With a source grid (the caller checked Step,
        /// Origin and, in the feedback system, age), a Need node whose world lattice index
        /// (I0 + i, J0 + j) is a Need node of the source with a finite height copies it; every other
        /// Need node is sampled at the world point (float)(Origin + (I0 + i)·Step), so a copied and a
        /// sampled value are the same function of the same inputs. Moved from
        /// SkiRunDrawFeedbackSystem.FillHeights (was :1361-1464); the body is unchanged except the
        /// terrain read (sampler) and that the counts are returned instead of published.</summary>
        public static void FillHeights<T>(ref T sampler, SlopeGrid g, SlopeGrid src, out int reused, out int sampled)
            where T : struct, IHeightSampler
        {
            int nx = g.Nx, nz = g.Nz, n = nx * nz;
            if (g.Heights == null || g.Heights.Length < n)
                g.Heights = new float[math.max(n, g.Heights == null ? 0 : g.Heights.Length * 2)];
            byte[] need = g.Need;
            float[] heights = g.Heights;
            double step = g.Step;
            double ox = g.Origin.x, oz = g.Origin.y;
            // O(Need), not O(bbox) and not O(row span): Plan records each row's exact Need RUNS
            // (SlopeGrid.NeedRuns / NeedRowStart, R2 fixer round). The lattice is the polygon
            // bbox, so a long diagonal 2-node stroke at m = 1 has ~1M bbox cells but few Need
            // cells, and a row of a U- or C-shaped run crosses two arms with a wide outside gap
            // between them (R1's first/last span walked that gap). Runs are clamped to the row;
            // the need[q] test stays as a guard. Fall back to the full scan if the runs are
            // missing or inconsistent with this grid.
            int[] needRuns = g.NeedRuns, needStart = g.NeedRowStart;
            bool spans = needStart != null && needStart.Length >= nz + 1 && needStart[nz] >= 0 &&
                         (needStart[nz] == 0 || (needRuns != null && needRuns.Length >= 2L * needStart[nz]));

            // Source mapping: target local (i, j) ↔ source local (i + dI, j + dJ).
            bool fromSrc = false;
            int sNx = 0, sNz = 0, dI = 0, dJ = 0;
            byte[] sNeed = null;
            float[] sH = null;
            if (src != null && !ReferenceEquals(src, g))
            {
                sNx = src.Nx;
                sNz = src.Nz;
                sNeed = src.Need;
                sH = src.Heights;
                long sTotal = (long)sNx * sNz;
                long di = (long)g.I0 - src.I0, dj = (long)g.J0 - src.J0;
                const long maxOffset = 1L << 24;   // keeps i + dI inside int; real offsets are tiny
                fromSrc = sNx > 0 && sNz > 0 && sNeed != null && sH != null &&
                          sNeed.Length >= sTotal && sH.Length >= sTotal &&
                          di > -maxOffset && di < maxOffset && dj > -maxOffset && dj < maxOffset;
                if (fromSrc)
                {
                    dI = (int)di;
                    dJ = (int)dj;
                }
            }

            int nReused = 0, nSampled = 0;
            for (int j = 0; j < nz; j++)
            {
                float wz = (float)(oz + (g.J0 + j) * step);
                int row = j * nx;
                int sj = j + dJ;
                bool rowSrc = fromSrc && sj >= 0 && sj < sNz;
                int sRow = rowSrc ? sj * sNx : 0;
                int k0 = 0, k1 = 1;                  // full scan: one pseudo-run [0, nx - 1]
                if (spans)
                {
                    k0 = needStart[j];
                    k1 = needStart[j + 1];
                }
                for (int k = k0; k < k1; k++)
                {
                    int lo = 0, hi = nx - 1;
                    if (spans)
                    {
                        lo = needRuns[2 * k] < 0 ? 0 : needRuns[2 * k];
                        hi = needRuns[2 * k + 1] > nx - 1 ? nx - 1 : needRuns[2 * k + 1];
                    }
                    for (int i = lo; i <= hi; i++)
                    {
                        int q = row + i;
                        if (need[q] == 0)
                            continue;
                        if (rowSrc)
                        {
                            int si = i + dI;
                            if (si >= 0 && si < sNx)
                            {
                                int sq = sRow + si;
                                if (sNeed[sq] != 0)
                                {
                                    float v = sH[sq];
                                    if (!float.IsNaN(v) && !float.IsInfinity(v))
                                    {
                                        heights[q] = v;
                                        nReused++;
                                        continue;
                                    }
                                }
                            }
                        }
                        float wx = (float)(ox + (g.I0 + i) * step);
                        heights[q] = sampler.Sample(new float3(wx, 0f, wz));
                        nSampled++;
                    }
                }
            }
            reused = nReused;
            sampled = nSampled;
        }

        /// <summary>The §2.1 overload: StepMultiple → DiagonalRule → Plan → FillHeights → Classify.
        /// See the detailed overload.</summary>
        public static RateStatus Rate<T>(float3[] nodes, int n, double2 origin, ref T sampler, float texelM, int stepCurrent,
            SlopeGrid target, SlopeGrid reuseSource, SlopeResult result, out int stepMultiple,
            out float fillMs, out float classifyMs) where T : struct, IHeightSampler
        {
            return Rate(nodes, n, origin, ref sampler, texelM, stepCurrent, target, reuseSource, result,
                out stepMultiple, out fillMs, out classifyMs, out RateDetail _);
        }

        /// <summary>Rates the polygon (n &gt;= 3) or line (n = 2) of the first n nodes on the world
        /// lattice (origin, m·texelM). m = SkiSlopeClassifier.StepMultiple(ShoelaceArea, texelM,
        /// stepCurrent), at least 1: stepCurrent &lt; 1 is deterministic for a given polygon (the
        /// smallest m within CellBudget, SkiSlopeClassifier.cs:729-741), stepCurrent &gt;= 1 applies
        /// the up/down hysteresis (the feedback system passes its per-Temp m). Plans into
        /// <paramref name="target"/>; reuses heights from <paramref name="reuseSource"/> only when it
        /// has the identical Step and Origin; classifies into <paramref name="result"/> only when
        /// Plan produced a lattice. Never throws for null grids (Plan / Classify return NoCells).</summary>
        public static RateStatus Rate<T>(float3[] nodes, int n, double2 origin, ref T sampler, float texelM, int stepCurrent,
            SlopeGrid target, SlopeGrid reuseSource, SlopeResult result, out int stepMultiple,
            out float fillMs, out float classifyMs, out RateDetail detail) where T : struct, IHeightSampler
        {
            detail = default(RateDetail);
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();

            double area = ShoelaceArea(nodes, n);
            int m = SkiSlopeClassifier.StepMultiple(area, texelM, stepCurrent);
            if (m < 1)
                m = 1;
            stepMultiple = m;
            float step = m * texelM;
            detail.AreaM2 = area;
            detail.StepM = step;

            // R2b (run-feedback-spec.md §9.8): the crux connectivity follows the step multiple —
            // the classifier does not know it, so the caller sets SlopeGrid.DiagonalRule
            // (8-neighbour at m = 1, R2's rule; the diagonal rule at m >= 2). Set on the target
            // before every Plan; Plan leaves it untouched.
            if (target != null)
                target.DiagonalRule = m >= 2;
            SlopeStatus status = SkiSlopeClassifier.Plan(nodes, n, origin, step, target);
            detail.PlanStatus = status;
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();

            long t2 = t1;
            if (IsPlanOk(status))
            {
                // Reuse only on the identical world lattice (exact Step and Origin); the caller
                // decided everything else (age, forced resample) by passing null or not.
                bool reuse = reuseSource != null &&
                             reuseSource.Step == step &&
                             reuseSource.Origin.x == origin.x && reuseSource.Origin.y == origin.y;
                detail.ReuseEnabled = reuse;
                FillHeights(ref sampler, target, reuse ? reuseSource : null, out detail.Reused, out detail.Sampled);
                t2 = System.Diagnostics.Stopwatch.GetTimestamp();
                status = SkiSlopeClassifier.Classify(target, result);
            }
            long t3 = System.Diagnostics.Stopwatch.GetTimestamp();
            detail.Status = status;

            double toMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            detail.PlanMs = (float)((t1 - t0) * toMs);
            fillMs = (float)((t2 - t1) * toMs);
            classifyMs = (float)((t3 - t2) * toMs);
            return ToRateStatus(detail.PlanStatus, status);
        }

        /// <summary>T35 / T44 (2026-10-16): the drawing tooltip's "avg N° (P %) · steepest N° (P %)" numbers. Average = the
        /// classifier's MeanGrade (the mean of |g| over the run's inside cells, |g| = the 5×5 box central-difference
        /// gradient magnitude, SkiSlopeClassifier "MeanGrade"; polyline: the length-weighted mean segment grade);
        /// steepest = the largest of SteepestGrade (the k-th largest 20 m window), RatingGrade (the crux) and the average
        /// itself, so the line never reads below the "Hardest unavoidable pitch" row or below its own average (the
        /// window statistic can sit under the crux when the crux came from a shorter fallback window; field log
        /// 2026-10-15: "Blue 26.2 % (steepest 21.2 %)"). Percent = grade × 100 rounded; degrees = atan(grade) in degrees
        /// rounded (atan of the mean grade, not the mean of the angles). False (all zero) when an input is not finite or
        /// negative.</summary>
        public static bool SlopeReadout(float meanGrade, float ratingGrade, float steepestGrade,
            out int avgDeg, out int avgPct, out int maxDeg, out int maxPct)
        {
            avgDeg = avgPct = maxDeg = maxPct = 0;
            if (!(meanGrade >= 0f) || !(ratingGrade >= 0f) || !(steepestGrade >= 0f) ||
                float.IsInfinity(meanGrade) || float.IsInfinity(ratingGrade) || float.IsInfinity(steepestGrade))
                return false;
            float steep = Math.Max(steepestGrade, Math.Max(ratingGrade, meanGrade));
            avgPct = (int)Math.Round(100.0 * meanGrade, MidpointRounding.AwayFromZero);
            maxPct = (int)Math.Round(100.0 * steep, MidpointRounding.AwayFromZero);
            avgDeg = (int)Math.Round(Math.Atan(meanGrade) * (180.0 / Math.PI), MidpointRounding.AwayFromZero);
            maxDeg = (int)Math.Round(Math.Atan(steep) * (180.0 / Math.PI), MidpointRounding.AwayFromZero);
            return true;
        }

        /// <summary>The RateStatus of a (plan, final) status pair (see RateStatus).</summary>
        public static RateStatus ToRateStatus(SlopeStatus planStatus, SlopeStatus status)
        {
            if (!IsPlanOk(planStatus))
                return planStatus == SlopeStatus.TooLarge ? RateStatus.TooLarge : RateStatus.Failed;
            if (IsRated(status))
                return RateStatus.Rated;
            if (status == SlopeStatus.NonFinite || status == SlopeStatus.BadHeights)
                return RateStatus.NoTerrain;
            return status == SlopeStatus.TooLarge ? RateStatus.TooLarge : RateStatus.Failed;
        }
    }
}
