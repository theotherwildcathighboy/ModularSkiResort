// RaterParity — SlopeBatch section for Round U2 (docs/research/hub-u2-spec.md §3.5 "Gate", §3.8):
// the Plan → Fill → Classify path moved out of SkiRunDrawFeedbackSystem into the pure SkiRunRater,
// and "the drawing feedback's behaviour and numbers must not change". Proven here, in the test
// runtime (CoreCLR; Mono bit equality is unverified, as for the classifier itself):
//
//   (1) CAPTURES — every *.skislope in tests/SlopeBatch/captures:
//       * SkiRunRater.Rate (stepCurrent = the captured multiple, the feedback system's hysteresis
//         input) returns the captured m and a result BIT-IDENTICAL (Snapshot.Diff: every float of
//         the result, crux, zones, CellBand, CellGrade, Mask, Need, segments) to TODAY'S DIRECT
//         CALLS (Plan → DiagonalRule → fill from the capture → Classify, ReplayCapture) and to the
//         OLD PATH (verbatim copies of the pre-extraction ShoelaceArea / Recompute middle /
//         FillHeights below);
//       * a second Rate with the first grid as reuse source copies EVERY Need height (sampled 0)
//         and gives the same result (the feedback system's reuse path);
//       * at m = 1, stepCurrent 0 and 1 give the same m and the same result (§3.8); the
//         deterministic m (stepCurrent 0, the hub catalog's choice) is printed for every capture.
//   (2) SYNTHETIC — shapes on the analytic test terrain (rects at 7 angles, the R2 scenarios, a
//       polyline, the disconnected dumbbell, rings that need m = 2 and 3), stepCurrent 0..3:
//       Rate == old path == direct (Plan + Fill + Classify), and FillHeights == the old body with a
//       reuse source whose lattice is offset (a moved vertex), counts included; a reuse source with
//       another Step is refused.
//   (3) STATUS MAPPING — TooFewPoints / TooLarge never classify (the result is left untouched, as
//       the feedback system never called Classify after a failed Plan), NaN heights → NoTerrain.
//
// The section runs with the rating fingerprint and the minimax oracle OFF (it compares paths; the
// PostCheck'd classifications elsewhere already carry the oracle), so the printed fingerprint stays
// comparable with earlier runs.
//
// Created 2026-10-03 (Round U2, implementer I-1).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace SlopeBatch
{
    /// <summary>IHeightSampler over the analytic test terrain (TerrainUtils.SampleHeightInternal
    /// shape, Terrain.Sample).</summary>
    internal struct TerrainSampler : IHeightSampler
    {
        public Terrain T;

        public float Sample(float3 world)
        {
            return T.Sample(world.x, world.z);
        }
    }

    /// <summary>IHeightSampler over a capture's Need heights: the world point is mapped back to its
    /// world lattice index (round((w - origin) / step)) and looked up; a miss returns NaN and is
    /// counted.</summary>
    internal struct CaptureSampler : IHeightSampler
    {
        public Capture Cap;
        public int Misses;

        public float Sample(float3 world)
        {
            long wi = (long)Math.Round((world.x - Cap.Origin.x) / Cap.Step);
            long wj = (long)Math.Round((world.z - Cap.Origin.y) / Cap.Step);
            if (Cap.Heights.TryGetValue(Capture.Key((int)wi, (int)wj), out float h))
                return h;
            Misses++;
            return float.NaN;
        }
    }

    /// <summary>Constant NaN terrain (status mapping: NoTerrain).</summary>
    internal struct NaNSampler : IHeightSampler
    {
        public float Sample(float3 world)
        {
            return float.NaN;
        }
    }

    internal static partial class Program
    {
        private static int s_RaterCompares;

        private static void RaterParityTests()
        {
            bool fp = s_FingerprintOn, oracle = s_OracleOn;
            s_FingerprintOn = false;
            s_OracleOn = false;
            try
            {
                RaterCaptureParity();
                RaterSyntheticParity();
                RaterStatusMapping();
            }
            finally
            {
                s_FingerprintOn = fp;
                s_OracleOn = oracle;
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "INFO  rater parity (hub-u2-spec §3.5): {0} SkiRunRater.Rate results compared bit for bit with the direct calls / the pre-extraction path",
                s_RaterCompares));
        }

        // ------------------------------------------------------------------ old path (verbatim)

        /// <summary>VERBATIM copy of SkiRunDrawFeedbackSystem.ShoelaceArea before the U2 extraction
        /// (build F71E109E, SkiRunDrawFeedbackSystem.cs:2231-2239).</summary>
        private static double OldShoelaceArea(float3[] p, int n)
        {
            if (n < 3)
                return 0.0;
            double s = 0.0;
            for (int i = 0, k = n - 1; i < n; k = i++)
                s += (double)p[k].x * p[i].z - (double)p[i].x * p[k].z;
            return Math.Abs(s) * 0.5;
        }

        /// <summary>VERBATIM copy of SkiRunDrawFeedbackSystem.FillHeights before the U2 extraction
        /// (build F71E109E, :1367-1464); the only edits: TerrainUtils.SampleHeight(ref hd, p) →
        /// sample(p), and the counts are returned instead of published.</summary>
        private static void OldFillHeights(Func<float3, float> sample, SlopeGrid g, SlopeGrid src, out int reusedOut, out int sampledOut)
        {
            int nx = g.Nx, nz = g.Nz, n = nx * nz;
            if (g.Heights == null || g.Heights.Length < n)
                g.Heights = new float[math.max(n, g.Heights == null ? 0 : g.Heights.Length * 2)];
            byte[] need = g.Need;
            float[] heights = g.Heights;
            double step = g.Step;
            double ox = g.Origin.x, oz = g.Origin.y;
            int[] needRuns = g.NeedRuns, needStart = g.NeedRowStart;
            bool spans = needStart != null && needStart.Length >= nz + 1 && needStart[nz] >= 0 &&
                         (needStart[nz] == 0 || (needRuns != null && needRuns.Length >= 2L * needStart[nz]));

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
                const long maxOffset = 1L << 24;
                fromSrc = sNx > 0 && sNz > 0 && sNeed != null && sH != null &&
                          sNeed.Length >= sTotal && sH.Length >= sTotal &&
                          di > -maxOffset && di < maxOffset && dj > -maxOffset && dj < maxOffset;
                if (fromSrc)
                {
                    dI = (int)di;
                    dJ = (int)dj;
                }
            }

            int reused = 0, sampled = 0;
            for (int j = 0; j < nz; j++)
            {
                float wz = (float)(oz + (g.J0 + j) * step);
                int row = j * nx;
                int sj = j + dJ;
                bool rowSrc = fromSrc && sj >= 0 && sj < sNz;
                int sRow = rowSrc ? sj * sNx : 0;
                int k0 = 0, k1 = 1;
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
                                        reused++;
                                        continue;
                                    }
                                }
                            }
                        }
                        float wx = (float)(ox + (g.I0 + i) * step);
                        heights[q] = sample(new float3(wx, 0f, wz));
                        sampled++;
                    }
                }
            }
            reusedOut = reused;
            sampledOut = sampled;
        }

        /// <summary>VERBATIM copy of the middle of SkiRunDrawFeedbackSystem.Recompute before the
        /// U2 extraction (build F71E109E, :1216-1264): area → StepMultiple (clamped) → step →
        /// DiagonalRule → Plan → [plan ok] reuse guard → FillHeights → Classify. callerAllowsReuse
        /// = the feedback system's own clauses (!forceFull, verified age).</summary>
        private static SlopeStatus OldRate(Func<float3, float> sample, float3[] nodes, int nodeCount, double2 origin, float texel, int stepCurrent,
            SlopeGrid target, SlopeGrid source, bool callerAllowsReuse, SlopeResult result, out int mOut, out bool reuseOut, out int reused, out int sampled)
        {
            double area = OldShoelaceArea(nodes, nodeCount);
            int m = SkiSlopeClassifier.StepMultiple(area, texel, stepCurrent);
            if (m < 1)
                m = 1;
            float step = m * texel;
            target.DiagonalRule = m >= 2;
            SlopeStatus status = SkiSlopeClassifier.Plan(nodes, nodeCount, origin, step, target);
            bool reuse = false;
            reused = 0;
            sampled = 0;
            if (status == SlopeStatus.Ok || status == SlopeStatus.Polyline || status == SlopeStatus.ShortRun)
            {
                reuse = callerAllowsReuse &&
                        source != null &&
                        source.Step == step &&
                        source.Origin.x == origin.x && source.Origin.y == origin.y;
                OldFillHeights(sample, target, reuse ? source : null, out reused, out sampled);
                status = SkiSlopeClassifier.Classify(target, result);
            }
            mOut = m;
            reuseOut = reuse;
            return status;
        }

        // ------------------------------------------------------------------ (1) captures

        private static void RaterCaptureParity()
        {
            string proj = ProjectDir();
            if (proj == null)
            {
                Fail("rater captures", "SlopeBatch.csproj not found above " + AppContext.BaseDirectory + " — the §3.5 parity gate cannot run");
                return;
            }
            string dir = Path.Combine(proj, "captures");
            string[] files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.skislope") : new string[0];
            Array.Sort(files, StringComparer.Ordinal);
            Check(files.Length > 0, "rater captures", "no *.skislope captures in " + dir + " — the §3.5 parity gate needs them");
            int m1Captures = 0;
            foreach (string file in files)
            {
                string w = "rater capture " + Path.GetFileName(file);
                if (!Check(TryLoadCapture(file, out Capture cap, out string err), w, "parse: " + err))
                    continue;

                // Today's direct calls (ReplayCapture: Plan → DiagonalRule → fill → Classify).
                SlopeGrid gd = new SlopeGrid();
                SlopeResult rd = new SlopeResult();
                SlopeStatus sd = ReplayCapture(cap, gd, rd, out int missing, out SlopeStatus planD, w + " direct");
                if (!Check(planD == SlopeStatus.Ok && missing == 0, w, "direct replay unusable (plan " + planD + ", " + missing + " missing heights)"))
                    continue;
                Snapshot direct = Snapshot.Take(gd, rd);

                // ShoelaceArea: the moved body vs the verbatim old one, bit for bit.
                double aNew = SkiRunRater.ShoelaceArea(cap.Nodes, cap.Nodes.Length);
                double aOld = OldShoelaceArea(cap.Nodes, cap.Nodes.Length);
                Check(BitConverter.DoubleToInt64Bits(aNew) == BitConverter.DoubleToInt64Bits(aOld), w,
                    "ShoelaceArea " + aNew.ToString("R", CultureInfo.InvariantCulture) + " vs old " + aOld.ToString("R", CultureInfo.InvariantCulture));

                // SkiRunRater.Rate with the feedback system's hysteresis input (the captured m).
                SlopeGrid gr = new SlopeGrid();
                SlopeResult rr = new SlopeResult();
                var cs = new CaptureSampler { Cap = cap };
                RateStatus rs = SkiRunRater.Rate(cap.Nodes, cap.Nodes.Length, cap.Origin, ref cs, cap.Texel, cap.Multiple, gr, null, rr,
                    out int m, out float _, out float _, out RateDetail det);
                Check(m == cap.Multiple, w, "Rate chose m=" + m + ", the capture was rated at m=" + cap.Multiple);
                Check(cs.Misses == 0, w, cs.Misses + " sample(s) fell outside the captured Need heights");
                Check(rs == RateStatus.Rated && det.Status == sd && det.PlanStatus == SlopeStatus.Ok, w,
                    "Rate status " + rs + " / " + det.Status + " (plan " + det.PlanStatus + "), direct " + sd);
                Check(det.Reused == 0 && det.Sampled == gr.NeedCells && !det.ReuseEnabled, w,
                    "no-source fill: reused " + det.Reused + " sampled " + det.Sampled + " of " + gr.NeedCells + " Need nodes");
                string d = direct.Diff(Snapshot.Take(gr, rr));
                s_RaterCompares++;
                Check(d == null, w, "SkiRunRater.Rate differs from the direct calls: " + d);

                // The pre-extraction path, verbatim.
                SlopeGrid go = new SlopeGrid();
                SlopeResult ro = new SlopeResult();
                Capture capRef = cap;
                SlopeStatus so = OldRate(p => LookupCapture(capRef, p), cap.Nodes, cap.Nodes.Length, cap.Origin, cap.Texel, cap.Multiple, go, null, false, ro,
                    out int mo, out bool _, out int _, out int _);
                d = direct.Diff(Snapshot.Take(go, ro));
                s_RaterCompares++;
                Check(mo == m && so == sd && d == null, w, "old path m=" + mo + " status " + so + " differs from the direct calls: " + d);

                // Reuse: the first grid as source → every Need height copied, same result.
                SlopeGrid g2 = new SlopeGrid();
                SlopeResult r2 = new SlopeResult();
                var cs2 = new CaptureSampler { Cap = cap };
                SkiRunRater.Rate(cap.Nodes, cap.Nodes.Length, cap.Origin, ref cs2, cap.Texel, cap.Multiple, g2, gr, r2,
                    out int m2, out float _, out float _, out RateDetail det2);
                Check(det2.ReuseEnabled && det2.Reused == g2.NeedCells && det2.Sampled == 0 && cs2.Misses == 0, w,
                    "reuse from the first grid: enabled " + det2.ReuseEnabled + ", reused " + det2.Reused + " sampled " + det2.Sampled + " of " + g2.NeedCells);
                d = direct.Diff(Snapshot.Take(g2, r2));
                s_RaterCompares++;
                Check(m2 == m && d == null, w, "reuse path differs: " + d);

                // stepCurrent 0 (deterministic, the hub catalog) and 1 at m = 1.
                int mDet = SkiSlopeClassifier.StepMultiple(aNew, cap.Texel, 0);
                if (cap.Multiple == 1)
                {
                    m1Captures++;
                    SlopeGrid g0 = new SlopeGrid(), g1 = new SlopeGrid();
                    SlopeResult r0 = new SlopeResult(), r1 = new SlopeResult();
                    var c0 = new CaptureSampler { Cap = cap };
                    var c1 = new CaptureSampler { Cap = cap };
                    SkiRunRater.Rate(cap.Nodes, cap.Nodes.Length, cap.Origin, ref c0, cap.Texel, 0, g0, null, r0, out int s0, out float _, out float _);
                    SkiRunRater.Rate(cap.Nodes, cap.Nodes.Length, cap.Origin, ref c1, cap.Texel, 1, g1, null, r1, out int s1, out float _, out float _);
                    Check(s0 == 1 && s1 == 1, w, "stepCurrent 0 → m=" + s0 + ", 1 → m=" + s1 + " (§3.8: same m at m = 1)");
                    d = Snapshot.Take(g0, r0).Diff(Snapshot.Take(g1, r1));
                    s_RaterCompares++;
                    Check(d == null, w, "stepCurrent 0 vs 1 results differ: " + d);
                }
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "INFO  rater capture {0}: m={1} (deterministic m at stepCurrent 0: {2}{3}) band {4} rating {5} steepest {6} drop {7:F1} m length {8:F1} m — Rate == direct == old path, reuse copies {9}/{10}",
                    cap.Name, m, mDet, mDet == m ? "" : " — the hub catalog would rate this polygon at another step", rr.Band, P(rr.RatingGrade), P(rr.SteepestGrade),
                    rr.Drop, rr.Length, det2.Reused, g2.NeedCells));
            }
            Check(m1Captures > 0, "rater captures", "no capture at m = 1: the §3.8 'stepCurrent 0 vs 1 at m = 1' check never ran");
        }

        private static float LookupCapture(Capture cap, float3 world)
        {
            var s = new CaptureSampler { Cap = cap };
            return s.Sample(world);
        }

        // ------------------------------------------------------------------ (2) synthetic

        private static void RaterSyntheticParity()
        {
            var shapes = new List<KeyValuePair<string, KeyValuePair<double2[], Terrain>>>();
            foreach (double deg in Angles7)
            {
                Frame fr = Frame.Make(777.7, -555.5, deg);
                double2[] ring = Rect(400, 90, fr);
                shapes.Add(Shape("rect 400x90 @" + F(deg), ring, Terrain.For(Mountain(), ring, 60)));
            }
            Frame f2 = Frame.Make(-1234.5, 2222.2, 23);
            foreach (Scn sc in new[] { R2a(), R2b(), R2c(), R2d(), R2e(120) })
            {
                double2[] ring = sc.Ring(f2);
                shapes.Add(Shape(sc.Name, ring, Terrain.For(sc.Height(f2), ring, 60)));
            }
            Frame fb = Frame.Make(-900.1, 300.3, 101);
            double2[] line = { fb.World(0, 0), fb.World(90, 0) };
            shapes.Add(Shape("polyline 90 m", line, Terrain.For((x, z) => 1000.0 - 0.15 * fb.Along(x, z), line, 60)));
            double2[] dumb = Dumbbell(-2000.0, 1500.0);
            shapes.Add(Shape("dumbbell (disconnected)", dumb, Terrain.For((x, z) => 1000.0 - 0.15 * x, dumb, 60)));
            Frame fm = Frame.Make(300.0, 400.0, 17);
            double2[] big2 = Rect(2500, 200, fm);     // 500,000 m² → m = 2 at stepCurrent 0
            shapes.Add(Shape("rect 2500x200 (m=2)", big2, Terrain.For(Mountain(), big2, 60)));
            double2[] big3 = Rect(3000, 300, fm);     // 900,000 m² → m = 3
            shapes.Add(Shape("rect 3000x300 (m=3)", big3, Terrain.For(Mountain(), big3, 60)));

            foreach (var sh in shapes)
            {
                string name = sh.Key;
                double2[] ring = sh.Value.Key;
                Terrain t = sh.Value.Value;
                float3[] pts = Pts(ring, false);
                double aNew = SkiRunRater.ShoelaceArea(pts, pts.Length), aOld = OldShoelaceArea(pts, pts.Length);
                Check(BitConverter.DoubleToInt64Bits(aNew) == BitConverter.DoubleToInt64Bits(aOld), "rater " + name, "ShoelaceArea differs from the old body");
                for (int cur = 0; cur <= 3; cur++)
                {
                    string w = "rater " + name + " stepCurrent " + cur;
                    SlopeGrid gr = new SlopeGrid(), go = new SlopeGrid(), gd = new SlopeGrid();
                    SlopeResult rr = new SlopeResult(), ro = new SlopeResult(), rd = new SlopeResult();
                    var ts = new TerrainSampler { T = t };
                    RateStatus rs = SkiRunRater.Rate(pts, pts.Length, Terrain.Origin, ref ts, Terrain.Texel, cur, gr, null, rr,
                        out int m, out float _, out float _, out RateDetail det);
                    SlopeStatus so = OldRate(p => t.Sample(p.x, p.z), pts, pts.Length, Terrain.Origin, Terrain.Texel, cur, go, null, false, ro,
                        out int mo, out bool _, out int ruo, out int smo);
                    // Direct: Plan + Fill (the host's own fill) + Classify at the same m.
                    gd.DiagonalRule = m >= 2;
                    SlopeStatus sd = SkiSlopeClassifier.Plan(pts, pts.Length, Terrain.Origin, Step(m), gd);
                    if (sd == SlopeStatus.Ok)
                    {
                        Fill(gd, t);
                        sd = SkiSlopeClassifier.Classify(gd, rd);
                    }
                    Check(t.OutOfRange == 0, w, "fixture terrain too small (" + t.OutOfRange + " samples out of range)");
                    t.OutOfRange = 0;
                    Check(mo == m && so == det.Status && sd == det.Status, w, "m " + m + " / old " + mo + ", status " + det.Status + " / old " + so + " / direct " + sd);
                    Check(rs == SkiRunRater.ToRateStatus(det.PlanStatus, det.Status), w, "RateStatus " + rs + " does not map " + det.PlanStatus + " / " + det.Status);
                    Check(det.Reused == ruo && det.Sampled == smo, w, "counts " + det.Reused + "/" + det.Sampled + " vs old " + ruo + "/" + smo);
                    Snapshot sr = Snapshot.Take(gr, rr);
                    string d1 = sr.Diff(Snapshot.Take(go, ro)), d2 = sr.Diff(Snapshot.Take(gd, rd));
                    s_RaterCompares += 2;
                    Check(d1 == null, w, "Rate differs from the old path: " + d1);
                    Check(d2 == null, w, "Rate differs from the direct calls: " + d2);
                    if (cur == 0)
                        Check(m == SkiSlopeClassifier.StepMultiple(aNew, Terrain.Texel, 0), w, "stepCurrent 0 is not the deterministic m");
                    if (det.Status != SlopeStatus.Ok || pts.Length < 3)
                        continue;

                    // Reuse with an offset lattice: move vertex 1 by (7.3, -4.1) m (and the ring's
                    // bbox with it), rate with the first grid as the source — new vs old body.
                    float3[] moved = (float3[])pts.Clone();
                    moved[1] += new float3(7.3f, 0f, -4.1f);
                    SlopeGrid gn = new SlopeGrid(), gon = new SlopeGrid(), gfull = new SlopeGrid();
                    SlopeResult rn = new SlopeResult(), ron = new SlopeResult(), rfull = new SlopeResult();
                    var ts2 = new TerrainSampler { T = t };
                    SkiRunRater.Rate(moved, moved.Length, Terrain.Origin, ref ts2, Terrain.Texel, m, gn, gr, rn,
                        out int mn, out float _, out float _, out RateDetail detN);
                    OldRate(p => t.Sample(p.x, p.z), moved, moved.Length, Terrain.Origin, Terrain.Texel, m, gon, go, true, ron,
                        out int mon, out bool reuseOld, out int ruN, out int smN);
                    var ts3 = new TerrainSampler { T = t };
                    SkiRunRater.Rate(moved, moved.Length, Terrain.Origin, ref ts3, Terrain.Texel, m, gfull, null, rfull,
                        out int _, out float _, out float _, out RateDetail detF);
                    t.OutOfRange = 0;
                    string wm = w + " moved vertex";
                    Check(mn == mon && detN.ReuseEnabled == reuseOld && detN.Reused == ruN && detN.Sampled == smN, wm,
                        "reuse " + detN.ReuseEnabled + " " + detN.Reused + "/" + detN.Sampled + " vs old " + reuseOld + " " + ruN + "/" + smN);
                    Check(mn != m || (detN.ReuseEnabled && detN.Reused > 0), wm, "same lattice but nothing reused");
                    Snapshot sn = Snapshot.Take(gn, rn);
                    string d3 = sn.Diff(Snapshot.Take(gon, ron)), d4 = sn.Diff(Snapshot.Take(gfull, rfull));
                    s_RaterCompares += 2;
                    Check(d3 == null, wm, "Rate with reuse differs from the old path with reuse: " + d3);
                    Check(d4 == null, wm, "Rate with reuse differs from a full resample on static terrain: " + d4);
                    Check(detF.Reused == 0 && detF.Sampled == gfull.NeedCells, wm, "full resample reused " + detF.Reused);

                    // A source on another Step is refused (the rater's half of the guard).
                    SlopeGrid gx = new SlopeGrid();
                    SlopeResult rx = new SlopeResult();
                    var ts4 = new TerrainSampler { T = t };
                    SkiRunRater.Rate(pts, pts.Length, Terrain.Origin, ref ts4, Terrain.Texel * 1.0001f, cur, gx, gr, rx,
                        out int _, out float _, out float _, out RateDetail detX);
                    t.OutOfRange = 0;
                    Check(!detX.ReuseEnabled && detX.Reused == 0, w + " other step", "a source with another Step was reused (" + detX.Reused + ")");
                }
            }
        }

        private static KeyValuePair<string, KeyValuePair<double2[], Terrain>> Shape(string name, double2[] ring, Terrain t)
        {
            return new KeyValuePair<string, KeyValuePair<double2[], Terrain>>(name, new KeyValuePair<double2[], Terrain>(ring, t));
        }

        // ------------------------------------------------------------------ (3) status mapping

        private static void RaterStatusMapping()
        {
            Frame fr = Frame.Make(777.7, -555.5, 37);
            double2[] ring = Rect(400, 90, fr);
            Terrain t = Terrain.For(Mountain(), ring, 60);
            float3[] pts = Pts(ring, false);

            // A rated result first, so "untouched" is observable.
            SlopeGrid g = new SlopeGrid();
            SlopeResult r = new SlopeResult();
            var ts = new TerrainSampler { T = t };
            Check(SkiRunRater.Rate(pts, pts.Length, Terrain.Origin, ref ts, Terrain.Texel, 0, g, null, r, out int _, out float _, out float _) == RateStatus.Rated,
                "rater status", "fixture not rated");
            t.OutOfRange = 0;
            float before = r.RatingGrade;
            SlopeStatus beforeStatus = r.Status;

            // One point: Plan fails (TooFewPoints) → Failed, Classify not called.
            var ts1 = new TerrainSampler { T = t };
            RateStatus a = SkiRunRater.Rate(pts, 1, Terrain.Origin, ref ts1, Terrain.Texel, 0, new SlopeGrid(), null, r, out int _, out float _, out float _,
                out RateDetail da);
            Check(a == RateStatus.Failed && da.PlanStatus == SlopeStatus.TooFewPoints && da.Status == SlopeStatus.TooFewPoints, "rater status", "1 point: " + a + " / " + da.Status);
            Check(r.RatingGrade == before && r.Status == beforeStatus, "rater status", "a failed Plan touched the result (the feedback system never classified then)");

            // A 2-node line 10 km diagonal at m = 1 (area 0 → m = 1): bbox ~4M cells > 2^21 → TooLarge.
            float3[] longLine = { new float3(-5000f, 0f, -5000f), new float3(2100f, 0f, 2100f) };
            var ts2 = new NaNSampler();
            RateStatus b = SkiRunRater.Rate(longLine, 2, Terrain.Origin, ref ts2, Terrain.Texel, 0, new SlopeGrid(), null, r, out int mb, out float _, out float _,
                out RateDetail db);
            Check(mb == 1 && b == RateStatus.TooLarge && db.PlanStatus == SlopeStatus.TooLarge, "rater status", "10 km line: m=" + mb + " " + b + " / plan " + db.PlanStatus);
            Check(r.RatingGrade == before && r.Status == beforeStatus, "rater status", "a TooLarge plan touched the result");

            // NaN heights → NoTerrain.
            var nan = new NaNSampler();
            RateStatus c = SkiRunRater.Rate(pts, pts.Length, Terrain.Origin, ref nan, Terrain.Texel, 0, new SlopeGrid(), null, new SlopeResult(),
                out int _, out float _, out float _, out RateDetail dc);
            Check(c == RateStatus.NoTerrain && (dc.Status == SlopeStatus.NonFinite || dc.Status == SlopeStatus.BadHeights), "rater status",
                "NaN terrain: " + c + " / " + dc.Status);

            // Null grid / result never throw.
            var ts3 = new TerrainSampler { T = t };
            RateStatus e = SkiRunRater.Rate(pts, pts.Length, Terrain.Origin, ref ts3, Terrain.Texel, 0, null, null, null, out int _, out float _, out float _);
            Check(e == RateStatus.Failed, "rater status", "null grid: " + e);
            t.OutOfRange = 0;
        }
    }
}
