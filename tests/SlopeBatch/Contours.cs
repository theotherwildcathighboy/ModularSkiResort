// T253 (1.0.4) smooth band outlines (SkiSlopeContours): invariant tests, a TIME line, and the offline look preview.
// Tests: contours/levels (a linear field: every curve sits at the analytic crossing), contours/fill-ends (row strips end at the
// crossings, cover each row once), contours/closed (a cone: closed rings at the analytic radius, every curve end linked), contours/nan
// (no curve or strip in a NaN region, no contour along a NaN border), contours/box (every control point and the curve between them
// lie inside the draw box).
// Preview: `dotnet run --project tests/SlopeBatch -c Release -- --outline-preview <dir>` skips the tests. It writes top-down PNGs with a
// hillshade (2x2 supersampled) of the 300 m overlay disk on the synthetic terrains, and of one field capture's run heat-map. Looks:
// "103" (1.0.3: cell strips, mode filter, 1.0.3 alphas), "A", "B", "C" (PLAN.md "Colors"). Overlay alpha blending is modeled as
// base * (1 - a) + color * a. Outline joints are drawn without double blending (max per pixel): the game's joint look is unverified.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace SlopeBatch
{
    internal static partial class Program
    {
        // ---------------------------------------------------------------- tests
        /// <summary>contours/clip: a 100 m square turned 23 degrees, values only at the cell centers inside it. With ClipRing, every fill
        /// strip end lies inside the polygon or within SnapM of it (a sub-line snapped to its row's crossing), and the fill area matches the polygon area within 1.5 %.</summary>
        private static void ContourClip()
        {
            const string w = "contours/clip";
            const int n = 120;
            const double step = 3.5;
            double a = 23.0 * Math.PI / 180.0, cx = 200, cz = 150;
            var ring = new double2[4];
            for (int k = 0; k < 4; k++)
            {
                double u = (k == 0 || k == 3) ? -50 : 50, v = k < 2 ? -50 : 50;
                ring[k] = new double2(cx + u * Math.Cos(a) - v * Math.Sin(a), cz + u * Math.Sin(a) + v * Math.Cos(a));
            }
            bool Inside(double x, double z)
            {
                bool inside = false;
                for (int k = 0, m = 3; k < 4; m = k++)
                    if ((ring[k].y > z) != (ring[m].y > z) && x < (ring[m].x - ring[k].x) * (z - ring[k].y) / (ring[m].y - ring[k].y) + ring[k].x)
                        inside = !inside;
                return inside;
            }
            double EdgeDist(double x, double z)
            {
                double best = double.MaxValue;
                for (int k = 0; k < 4; k++)
                    best = Math.Min(best, Math.Sqrt(SegDist2Test(new double2(x, z), ring[k], ring[(k + 1) % 4])));
                return best;
            }
            float[] o = Field(n, n, (i, j) => Inside(i * step, j * step) ? 0.1f + 0.0012f * i : float.NaN);
            var c = new SkiSlopeContours { ClipRing = ring, ClipCount = 4 };
            c.Build(o, null, n, n, 0, 0, step, false);
            int outside = 0;
            double area = 0, worst = 0;
            foreach (SkiSlopeContours.FillRun r in c.Fill)
            {
                area += (r.X1 - r.X0) * r.Width;
                foreach (double x in new[] { r.X0, r.X1 })
                    if (!Inside(x + (x == r.X0 ? 1e-6 : -1e-6), r.Z))
                    {
                        double d = EdgeDist(x, r.Z);
                        worst = Math.Max(worst, d);
                        if (d > c.SnapM + 1e-6)
                            outside++;
                    }
            }
            Check(outside == 0, w, outside + " fill strip ends more than SnapM outside the polygon (worst " +
                                   worst.ToString("G3", CultureInfo.InvariantCulture) + " m)");
            Console.WriteLine("INFO  contours/clip: worst strip end outside the polygon " + worst.ToString("G3", CultureInfo.InvariantCulture) +
                              " m (SnapM " + c.SnapM + "), fill area " + area.ToString("F0", CultureInfo.InvariantCulture) + " of 10000 m2");
            Check(Math.Abs(area - 10000.0) < 150.0, w, "fill area " + area.ToString("F0", CultureInfo.InvariantCulture) + " m2, polygon 10000 m2 (limit 1.5 %)");
        }

        private static double SegDist2Test(double2 p, double2 a, double2 b)
        {
            double2 ab = b - a;
            double l2 = math.dot(ab, ab), t = l2 > 0 ? math.clamp(math.dot(p - a, ab) / l2, 0.0, 1.0) : 0.0;
            double2 d = p - (a + t * ab);
            return math.dot(d, d);
        }

        private static void ContourTests()
        {
            ContourLevels();
            ContourClosed();
            ContourNan();
            ContourClip();
            ContourBox("mountain", Mountain());
            ContourBox("steep", OverlaySteep());
            ContourTiming();
        }

        private static float[] Field(int nx, int nz, Func<int, int, float> f)
        {
            float[] o = new float[nx * nz];
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                    o[j * nx + i] = f(i, j);
            return o;
        }

        private static void ContourLevels()
        {
            const string w = "contours/levels";
            const int nx = 44, nz = 30;
            const double step = 3.5, ox = 100.0, oz = -50.0;
            float[] o = Field(nx, nz, (i, j) => 0.015f + 0.01f * i);
            var c = new SkiSlopeContours();
            c.Build(o, null, nx, nz, ox, oz, step, false);
            float[] lims = { SkiSlopeClassifier.GradeGreen, SkiSlopeClassifier.GradeBlue, SkiSlopeClassifier.GradeBlack };
            Check(c.Polylines == 3 && c.LoopsDropped == 0, w, "polylines " + c.Polylines + " dropped " + c.LoopsDropped + " (need 3 / 0)");
            int bad = 0;
            double worst = 0;
            foreach (SkiSlopeContours.Curve cv in c.Curves)
            {
                int k = cv.KeyHi - 1;   // green 1, blue 2, black 3
                if (k < 0 || k > 2)
                {
                    bad++;
                    continue;
                }
                double x = ox + step * (lims[k] - 0.015) / 0.01;
                foreach (float3 p in new[] { cv.P0, cv.P1, cv.P2, cv.P3 })
                    worst = Math.Max(worst, Math.Abs(p.x - x));
            }
            Check(bad == 0, w, bad + " curves with an unexpected key");
            Check(worst < 0.05, w, "worst |x - analytic| " + worst.ToString("G3", CultureInfo.InvariantCulture) + " m (limit 0.05)");

            // fill-ends: per row, contiguous runs from the first cell's left edge to the last cell's right edge, keys 0..3 in order, the
            // boundaries at the crossings.
            const string wf = "contours/fill-ends";
            int rowsBad = 0;
            double worstEnd = 0;
            for (int j = 0; j < nz; j++)
            {
                var row = c.Fill.FindAll(r => r.J == j);
                row.Sort((a, b) => a.X0.CompareTo(b.X0));
                bool ok = row.Count == 4 && Math.Abs(row[0].X0 - (ox - 0.5 * step)) < 1e-6 &&
                          Math.Abs(row[row.Count - 1].X1 - (ox + (nx - 0.5) * step)) < 1e-6;
                for (int k = 0; ok && k < row.Count; k++)
                {
                    ok &= row[k].Key == k;
                    if (k + 1 < row.Count)
                    {
                        ok &= Math.Abs(row[k].X1 - row[k + 1].X0) < 1e-9;
                        worstEnd = Math.Max(worstEnd, Math.Abs(row[k].X1 - (ox + step * (lims[k] - 0.015) / 0.01)));
                    }
                }
                if (!ok)
                    rowsBad++;
            }
            Check(rowsBad == 0, wf, rowsBad + " of " + nz + " rows not 4 contiguous runs flat / green / blue / black");
            Check(worstEnd < 0.05, wf, "worst strip end off its crossing " + worstEnd.ToString("G3", CultureInfo.InvariantCulture) + " m");
        }

        private static void ContourClosed()
        {
            const string w = "contours/closed";
            const int n = 61;
            const double step = 3.5, ox = 0, oz = 0, cx = 30 * step, cz = 30 * step;
            float[] o = Field(n, n, (i, j) => 0.7f - 0.012f * (float)Math.Sqrt((i - 30) * (i - 30) + (j - 30) * (j - 30)));
            var c = new SkiSlopeContours { Blur = false };
            c.Build(o, null, n, n, ox, oz, step, false);
            // 0.60 (r 8.33 cells) and 0.40 (r 25) close; 0.25 (r 37.5) leaves the square in 4 corner arcs.
            Check(c.Polylines == 6 && c.LoopsDropped == 0, w, "polylines " + c.Polylines + " dropped " + c.LoopsDropped + " (need 6 / 0)");
            int unlinked = 0;
            double worst = 0;
            foreach (SkiSlopeContours.Curve cv in c.Curves)
            {
                if (cv.KeyHi != 3 && cv.KeyHi != 4)
                    continue;
                double r = (cv.KeyHi == 4 ? (0.7 - 0.6) : (0.7 - 0.4)) / 0.012 * step;
                worst = Math.Max(worst, Math.Abs(math.distance(new double2(cv.P0.x, cv.P0.z), new double2(cx, cz)) - r));
                bool linked = false;
                foreach (SkiSlopeContours.Curve nx in c.Curves)
                    if (nx.KeyHi == cv.KeyHi && math.distance(nx.P0, cv.P3) < 1e-3f)
                    {
                        linked = true;
                        break;
                    }
                if (!linked)
                    unlinked++;
            }
            Check(unlinked == 0, w, unlinked + " ring curves whose end starts no other curve (a gap)");
            Check(worst < 0.15, w, "worst ring radius error " + worst.ToString("G3", CultureInfo.InvariantCulture) + " m (limit 0.15)");

            // Fill on the cone: on every sub-line of every row, the full-height strips plus that sub-line's thin strips cover the row
            // from its first to its last cell edge, without gaps or overlaps. Thin strips exist (the rings cross rows at shallow angles).
            int gaps = 0, rows = 0;
            int subs = c.SubRows;
            for (int j = 0; j < n; j++)
                for (int s = 0; s < subs; s++)
                {
                    double zs = oz + j * step + ((s + 0.5) / subs - 0.5) * step;
                    var line = c.Fill.FindAll(r => r.J == j && (Math.Abs(r.Width - step) < 1e-4 || Math.Abs(r.Z - zs) < 1e-6));
                    line.Sort((a, b) => a.X0.CompareTo(b.X0));
                    rows++;
                    bool ok = line.Count > 0 && Math.Abs(line[0].X0 - (ox - 0.5 * step)) < 1e-6 &&
                              Math.Abs(line[line.Count - 1].X1 - (ox + (n - 0.5) * step)) < 1e-6;
                    for (int k = 0; ok && k + 1 < line.Count; k++)
                        ok = Math.Abs(line[k].X1 - line[k + 1].X0) < 1e-9;
                    if (!ok)
                        gaps++;
                }
            Check(gaps == 0, w, gaps + " of " + rows + " sub-lines with a fill gap or overlap on the cone");
            Check(c.SubStrips > 0, w, "no thin strips on the cone (the shallow-angle rule did not run)");
            c.SubRows = 1;
            c.Build(o, null, n, n, ox, oz, step, false);
            Check(c.SubStrips == 0, w, c.SubStrips + " thin strips with SubRows = 1");
        }

        private static void ContourNan()
        {
            const string w = "contours/nan";
            const int n = 40;
            const double step = 3.5;
            // A rough field, NaN for i >= 20 and a NaN hole at cells 10..12.
            float[] o = Field(n, n, (i, j) =>
                i >= 20 || (i >= 10 && i <= 12 && j >= 10 && j <= 12) ? float.NaN
                : 0.3f + 0.25f * (float)(Math.Sin(i * 0.9) * Math.Cos(j * 0.7)) + 0.05f * (float)Math.Sin(i * j * 0.37));
            var c = new SkiSlopeContours();
            c.Build(o, null, n, n, 0, 0, step, false);
            int outside = 0, inHole = 0, fillOut = 0;
            foreach (SkiSlopeContours.Curve cv in c.Curves)
                foreach (float3 p in new[] { cv.P0, cv.P1, cv.P2, cv.P3 })
                {
                    if (p.x > 19 * step + 1e-3)
                        outside++;
                    if (p.x > 10 * step && p.x < 12 * step && p.z > 10 * step && p.z < 12 * step)
                        inHole++;
                }
            foreach (SkiSlopeContours.FillRun r in c.Fill)
            {
                if (r.X1 > 19.5 * step + 1e-6)
                    fillOut++;
                if (r.J >= 10 && r.J <= 12 && r.X1 > 9.5 * step + 1e-6 && r.X0 < 12.5 * step - 1e-6)
                    fillOut++;
            }
            Check(c.Curves.Count > 0, w, "the rough field drew no curves");
            Check(outside == 0 && inHole == 0, w, outside + " control points in the NaN half, " + inHole + " in the NaN hole");
            Check(fillOut == 0, w, fillOut + " fill runs over NaN cells");

            float[] flat = Field(n, n, (i, j) => i >= 20 ? float.NaN : 0.3f);
            c.Build(flat, null, n, n, 0, 0, step, false);
            Check(c.Curves.Count == 0, w, c.Curves.Count + " curves along a NaN border of a constant field (need 0)");
        }

        /// <summary>The overlay field of a disk: o from the tiles (NaN without a tile or past R + 2 cells), node heights from the terrain.</summary>
        private static void DiskField(SkiSlopeOverlayTiles tiles, Terrain t, double cx, double cz, float radius, out float[] o, out float[] h,
                                      out int nx, out int nz, out double ox, out double oz)
        {
            double step = Terrain.Texel, reach = radius + 2 * step;
            int i0 = tiles.NodeOf(cx - reach, tiles.Origin.x), i1 = tiles.NodeOf(cx + reach, tiles.Origin.x);
            int j0 = tiles.NodeOf(cz - reach, tiles.Origin.y), j1 = tiles.NodeOf(cz + reach, tiles.Origin.y);
            nx = i1 - i0 + 1;
            nz = j1 - j0 + 1;
            ox = tiles.Origin.x + i0 * step;
            oz = tiles.Origin.y + j0 * step;
            o = new float[nx * nz];
            h = new float[nx * nz];
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int q = j * nx + i;
                    double x = ox + i * step, z = oz + j * step, dx = x - cx, dz = z - cz;
                    h[q] = t.H[(j0 + j - t.L0) * t.Nk + (i0 + i - t.K0)];
                    o[q] = dx * dx + dz * dz <= reach * reach && tiles.TryCell(i0 + i, j0 + j, out byte b, out float g) && b <= 4 ? g : float.NaN;
                }
        }

        private static SkiSlopeContours DiskContours(double cx, double cz, float radius)
        {
            return new SkiSlopeContours { Disk = true, DiskCx = cx, DiskCz = cz, DiskR = radius, FadeM = 15f };
        }

        private static void ContourBox(string name, HeightFn f)
        {
            string w = "contours/box " + name;
            const double cx = 120, cz = -40;
            Terrain t = OverlayTerrain(f, cx, cz, 300);
            var ts = new TerrainSampler { T = t };
            var tiles = new SkiSlopeOverlayTiles();
            tiles.SetLattice(Terrain.Origin, Terrain.Texel);
            tiles.Update(ref ts, cx, cz, 300f, 1, 1e9f);
            DiskField(tiles, t, cx, cz, 300f, out float[] o, out float[] h, out int nx, out int nz, out double ox, out double oz);
            SkiSlopeContours c = DiskContours(cx, cz, 300f);
            c.Build(o, h, nx, nz, ox, oz, Terrain.Texel, false);
            int outBox = 0, noBox = 0, outDisk = 0, fillBox = 0;
            foreach (SkiSlopeContours.Curve cv in c.Curves)
            {
                if (cv.YHi < cv.YLo)
                {
                    noBox++;
                    continue;
                }
                for (int k = 0; k <= 8; k++)
                {
                    float s = k / 8f, u = 1 - s;
                    float3 p = u * u * u * cv.P0 + 3 * u * u * s * cv.P1 + 3 * u * s * s * cv.P2 + s * s * s * cv.P3;
                    float y = c.HeightAt(p.x, p.z);
                    if (y < cv.YLo || y > cv.YHi)
                        outBox++;
                    if (math.distance(new double2(p.x, p.z), new double2(cx, cz)) > 300.0 + Terrain.Texel)
                        outDisk++;
                }
                foreach (float3 p in new[] { cv.P0, cv.P1, cv.P2, cv.P3 })
                    if (p.y < cv.YLo || p.y > cv.YHi)
                        outBox++;
            }
            foreach (SkiSlopeContours.FillRun r in c.Fill)
                if (r.YHi < r.YLo || r.Y < r.YLo || r.Y > r.YHi)
                    fillBox++;
            Check(c.Curves.Count > 100, w, "only " + c.Curves.Count + " curves on the test terrain");
            Check(noBox == 0 && outBox == 0, w, noBox + " curves without a box, " + outBox + " control / curve points outside their box");
            Check(outDisk == 0, w, outDisk + " curve points more than one cell outside the disk");
            Check(fillBox == 0, w, fillBox + " fill runs with the strip height outside the box");
        }

        private static void ContourTiming()
        {
            const double cx = 120, cz = -40;
            foreach (var (name, f) in new[] { ("mountain", Mountain()), ("steep", OverlaySteep()) })
            {
                Terrain t = OverlayTerrain(f, cx, cz, 300);
                var ts = new TerrainSampler { T = t };
                var tiles = new SkiSlopeOverlayTiles();
                tiles.SetLattice(Terrain.Origin, Terrain.Texel);
                tiles.Update(ref ts, cx, cz, 300f, 1, 1e9f);
                DiskField(tiles, t, cx, cz, 300f, out float[] o, out float[] h, out int nx, out int nz, out double ox, out double oz);
                SkiSlopeContours c = DiskContours(cx, cz, 300f);
                double Median(bool curves, bool blur)
                {
                    c.DrawCurves = curves;
                    c.Blur = blur;
                    double[] t2 = new double[120];
                    for (int k = 0; k < 200; k++)
                    {
                        Stopwatch sw = Stopwatch.StartNew();
                        c.Build(o, h, nx, nz, ox, oz, Terrain.Texel, false);
                        if (k >= 80)
                            t2[k - 80] = sw.Elapsed.TotalMilliseconds;   // after the JIT tiers settle
                    }
                    Array.Sort(t2);
                    return t2[t2.Length / 2];
                }
                double noBlurFill = Median(false, false), fillOnly = Median(false, true);
                double[] ms = { Median(true, true) };
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "TIME  contours {0} phases: blur {1:F2} ms, fill {2:F2} ms, contours {3:F2} ms (medians of 120 after warm-up)", name,
                    fillOnly - noBlurFill, noBlurFill, ms[0] - fillOnly));
                var runs = new List<SlopeOverlayRun>();
                var rects = new List<SlopeOverlayRun>();
                var skip = new NoSlopeCellSkip();
                tiles.BuildRuns(cx, cz, 300f, 15f, false, 1, 1 << 20, ref skip, runs);
                tiles.MergeRows(runs, rects);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "TIME  contours {0}: 300 m disk {1}x{2} cells, build median {3:F2} ms (CoreCLR, after warm-up); fill {5} strips, {6} curves from {7} " +
                    "polylines ({8} dropped under {9} m), {11} thin strips; 1.0.3 strips (mode filter, merged) {10}",
                    name, nx, nz, ms[ms.Length / 2], ms[0], c.Fill.Count, c.Curves.Count, c.Polylines, c.LoopsDropped, c.MinLoopM, rects.Count, c.SubStrips));
            }
        }

        // ---------------------------------------------------------------- preview
        private sealed class Look
        {
            public string Id;
            public bool Cells;             // 1.0.3: cell strips
            public float[] Alpha;          // per strip key
            public float FillFactor = 1f;
            public bool Outline;
            public bool DarkLine;          // C: black line instead of the band color
            public float LineAlpha, LineWidthM;
            public bool BlackFull;         // A2: the black fill keeps x 1.0
            public float LineDarken = 1f;  // A2: the band color x this for the line
            public bool DistWidth;         // A2: line width = max(1 m, 0.003 x camera distance)
        }

        private static readonly float[][] PreviewRgb =
        {
            new[] { 0.60f, 0.60f, 0.60f }, new[] { 0.20f, 0.75f, 0.30f }, new[] { 0.20f, 0.45f, 0.95f },
            new[] { 0.15f, 0.15f, 0.15f }, new[] { 0.85f, 0.20f, 0.10f }, new[] { 0.20f, 0.45f, 0.95f },
        };

        private static readonly float[] Alpha103 = { 0.35f, 0.35f, 0.45f, 0.50f, 0.45f, 0.45f };
        private static readonly float[] AlphaMid = { 0.35f, 0.35f, 0.40f, 0.55f, 0.50f, 0.40f };

        private static Look[] Looks() => new[]
        {
            new Look { Id = "103", Cells = true, Alpha = Alpha103 },
            new Look { Id = "A", Alpha = AlphaMid, FillFactor = 0.75f, Outline = true, LineAlpha = 0.9f, LineWidthM = 1.25f },
            new Look { Id = "B", Alpha = AlphaMid, FillFactor = 0.75f, Outline = true, LineAlpha = 0.75f, LineWidthM = 2.0f },
            new Look { Id = "C", Alpha = AlphaMid, FillFactor = 1.0f, Outline = true, DarkLine = true, LineAlpha = 0.6f, LineWidthM = 0.6f },
            new Look { Id = "A2", Alpha = AlphaMid, FillFactor = 0.75f, Outline = true, LineAlpha = 0.9f, LineWidthM = 1.25f, BlackFull = true,
                       LineDarken = 0.72f, DistWidth = true },
        };

        private struct PRect
        {
            public double X0, X1, Z0, Z1;
            public byte Key, Fade;
        }

        private sealed class Canvas
        {
            public int W;                  // supersampled side (px)
            public double X0, Z1, Mpp;     // world x of the left edge, z of the top edge, m per px
            public float[] R, G, B;
            public float[] LineA;
            public byte[] LineKey;

            public Canvas(int px, double cx, double cz, double sizeM)
            {
                W = px * 2;
                Mpp = sizeM / W;
                X0 = cx - 0.5 * sizeM;
                Z1 = cz + 0.5 * sizeM;
                R = new float[W * W];
                G = new float[W * W];
                B = new float[W * W];
                LineA = new float[W * W];
                LineKey = new byte[W * W];
            }

            public void Base(Func<double, double, float> height, bool snow)
            {
                float[] tone = snow ? new[] { 0.90f, 0.92f, 0.95f } : new[] { 0.58f, 0.63f, 0.45f };
                double3 l = math.normalize(new double3(-1, 1.3, 1));
                const double d = 1.75;
                for (int r = 0; r < W; r++)
                {
                    double z = Z1 - (r + 0.5) * Mpp;
                    for (int c = 0; c < W; c++)
                    {
                        double x = X0 + (c + 0.5) * Mpp;
                        float hx0 = height(x - d, z), hx1 = height(x + d, z), hz0 = height(x, z - d), hz1 = height(x, z + d);
                        double s = 0.8;
                        if (!float.IsNaN(hx0 + hx1 + hz0 + hz1))
                        {
                            double3 nrm = math.normalize(new double3(-(hx1 - hx0) / (2 * d), 1, -(hz1 - hz0) / (2 * d)));
                            s = 0.35 + 0.65 * Math.Max(0, math.dot(nrm, l)) / math.dot(new double3(0, 1, 0), l) * 0.8;
                            s = Math.Min(1.15, s);
                        }
                        int q = r * W + c;
                        R[q] = (float)(tone[0] * s);
                        G[q] = (float)(tone[1] * s);
                        B[q] = (float)(tone[2] * s);
                    }
                }
            }

            public void FillRect(PRect rc, float[] rgb, float a)
            {
                int c0 = Math.Max(0, (int)Math.Ceiling((rc.X0 - X0) / Mpp - 0.5)), c1 = Math.Min(W - 1, (int)Math.Ceiling((rc.X1 - X0) / Mpp - 0.5) - 1);
                int r0 = Math.Max(0, (int)Math.Ceiling((Z1 - rc.Z1) / Mpp - 0.5)), r1 = Math.Min(W - 1, (int)Math.Ceiling((Z1 - rc.Z0) / Mpp - 0.5) - 1);
                for (int r = r0; r <= r1; r++)
                    for (int c = c0; c <= c1; c++)
                    {
                        int q = r * W + c;
                        R[q] = R[q] * (1 - a) + rgb[0] * a;
                        G[q] = G[q] * (1 - a) + rgb[1] * a;
                        B[q] = B[q] * (1 - a) + rgb[2] * a;
                    }
            }

            public void Stroke(SkiSlopeContours.Curve cv, float widthM, float a, byte key)
            {
                double hw = 0.5 * widthM;
                double2 prev = default;
                for (int k = 0; k <= 12; k++)
                {
                    float s = k / 12f, u = 1 - s;
                    float3 p3 = u * u * u * cv.P0 + 3 * u * u * s * cv.P1 + 3 * u * s * s * cv.P2 + s * s * s * cv.P3;
                    double2 p = new double2(p3.x, p3.z);
                    if (k > 0)
                        Capsule(prev, p, hw, a, key);
                    prev = p;
                }
            }

            private void Capsule(double2 a, double2 b, double hw, float alpha, byte key)
            {
                double xl = Math.Min(a.x, b.x) - hw, xh = Math.Max(a.x, b.x) + hw, zl = Math.Min(a.y, b.y) - hw, zh = Math.Max(a.y, b.y) + hw;
                int c0 = Math.Max(0, (int)Math.Floor((xl - X0) / Mpp)), c1 = Math.Min(W - 1, (int)Math.Ceiling((xh - X0) / Mpp));
                int r0 = Math.Max(0, (int)Math.Floor((Z1 - zh) / Mpp)), r1 = Math.Min(W - 1, (int)Math.Ceiling((Z1 - zl) / Mpp));
                double2 ab = b - a;
                double l2 = math.dot(ab, ab);
                for (int r = r0; r <= r1; r++)
                    for (int c = c0; c <= c1; c++)
                    {
                        double2 p = new double2(X0 + (c + 0.5) * Mpp, Z1 - (r + 0.5) * Mpp);
                        double t = l2 > 0 ? math.clamp(math.dot(p - a, ab) / l2, 0.0, 1.0) : 0.0;
                        double2 dd = p - (a + t * ab);
                        if (math.dot(dd, dd) > hw * hw)
                            continue;
                        int q = r * W + c;
                        if (alpha > LineA[q])
                        {
                            LineA[q] = alpha;
                            LineKey[q] = key;
                        }
                    }
            }

            public void ApplyLines(bool dark, float darken)
            {
                for (int q = 0; q < LineA.Length; q++)
                {
                    float a = LineA[q];
                    if (a <= 0f)
                        continue;
                    float[] src = PreviewRgb[LineKey[q]];
                    float[] rgb = dark ? new[] { 0f, 0f, 0f } : new[] { src[0] * darken, src[1] * darken, src[2] * darken };
                    R[q] = R[q] * (1 - a) + rgb[0] * a;
                    G[q] = G[q] * (1 - a) + rgb[1] * a;
                    B[q] = B[q] * (1 - a) + rgb[2] * a;
                }
            }

            public void Save(string path)
            {
                int w = W / 2;
                byte[] rgb = new byte[w * w * 3];
                for (int r = 0; r < w; r++)
                    for (int c = 0; c < w; c++)
                    {
                        int q0 = 2 * r * W + 2 * c, q1 = q0 + W;
                        float rr = 0.25f * (R[q0] + R[q0 + 1] + R[q1] + R[q1 + 1]);
                        float gg = 0.25f * (G[q0] + G[q0 + 1] + G[q1] + G[q1 + 1]);
                        float bb = 0.25f * (B[q0] + B[q0 + 1] + B[q1] + B[q1 + 1]);
                        int o = (r * w + c) * 3;
                        rgb[o] = ToByte(rr);
                        rgb[o + 1] = ToByte(gg);
                        rgb[o + 2] = ToByte(bb);
                    }
                WritePng(path, w, w, rgb);
            }

            private static byte ToByte(float v) => (byte)Math.Max(0, Math.Min(255, (int)Math.Round(v * 255f)));
        }

        private struct View
        {
            public string Id;
            public double Cx, Cz, SizeM;
            public int Px;
            public bool Snow;
            public double CamM;            // the camera distance this view stands for (A2 line width)
        }

        private static void Paint(Canvas cv, Look look, List<PRect> cells, SkiSlopeContours c, double camM)
        {
            if (look.Cells)
                foreach (PRect rc in cells)
                    cv.FillRect(rc, PreviewRgb[rc.Key], look.Alpha[rc.Key] * rc.Fade / 4f);
            else
            {
                foreach (SkiSlopeContours.FillRun r in c.Fill)
                    cv.FillRect(new PRect { X0 = r.X0, X1 = r.X1, Z0 = r.Z - 0.5 * r.Width, Z1 = r.Z + 0.5 * r.Width },
                        PreviewRgb[r.Key], look.Alpha[r.Key] * (look.BlackFull && r.Key == 3 ? 1f : look.FillFactor) * r.Fade / 4f);
                if (look.Outline)
                {
                    foreach (SkiSlopeContours.Curve k in c.Curves)
                        cv.Stroke(k, look.DistWidth ? (float)Math.Max(1.0, 0.003 * camM) : look.LineWidthM, look.LineAlpha * k.Fade / 4f, k.KeyHi);
                    cv.ApplyLines(look.DarkLine, look.LineDarken);
                }
            }
        }

        private static int OutlinePreview(string dir)
        {
            Directory.CreateDirectory(dir);
            var log = new List<string>();
            const double cx = 120, cz = -40;
            const float radius = 300f;
            foreach (var (name, f) in new[] { ("mountain", Mountain()), ("steep", OverlaySteep()) })
            {
                Terrain t = OverlayTerrain(f, cx, cz, radius);
                var ts = new TerrainSampler { T = t };
                var tiles = new SkiSlopeOverlayTiles();
                tiles.SetLattice(Terrain.Origin, Terrain.Texel);
                tiles.Update(ref ts, cx, cz, radius, 1, 1e9f);
                DiskField(tiles, t, cx, cz, radius, out float[] o, out float[] h, out int nx, out int nz, out double ox, out double oz);
                SkiSlopeContours c = DiskContours(cx, cz, radius);
                c.Build(o, h, nx, nz, ox, oz, Terrain.Texel, false);
                var runs = new List<SlopeOverlayRun>();
                var rects = new List<SlopeOverlayRun>();
                var skip = new NoSlopeCellSkip();
                tiles.BuildRuns(cx, cz, radius, 15f, false, 1, 1 << 20, ref skip, runs);
                tiles.MergeRows(runs, rects);
                var cells = new List<PRect>();
                double s = Terrain.Texel;
                foreach (SlopeOverlayRun r in rects)
                    cells.Add(new PRect
                    {
                        X0 = Terrain.Origin.x + (r.I0 - 0.5) * s, X1 = Terrain.Origin.x + (r.I1 + 0.5) * s,
                        Z0 = Terrain.Origin.y + (r.JLo - 0.5) * s, Z1 = Terrain.Origin.y + (r.JHi + 0.5) * s, Key = r.Key, Fade = r.Fade,
                    });
                // The 200 m and 80 m crops sit on the busiest spot: the 40 m cell with the most curve midpoints.
                double2 busy = Busiest(c, cx, cz, radius - 100);
                View[] views =
                {
                    new View { Id = "disk", Cx = cx, Cz = cz, SizeM = 620, Px = 800, CamM = 1100 },
                    new View { Id = "z450", Cx = busy.x, Cz = busy.y, SizeM = 200, Px = 760, CamM = 450 },
                    new View { Id = "z450snow", Cx = busy.x, Cz = busy.y, SizeM = 200, Px = 760, Snow = true, CamM = 450 },
                    new View { Id = "close", Cx = busy.x, Cz = busy.y, SizeM = 80, Px = 760, CamM = 180 },
                };
                log.Add(string.Format(CultureInfo.InvariantCulture, "{0}: disk ({1}, {2}) r {3}; crops at ({4:F0}, {5:F0}); fill {6} strips, {7} curves, " +
                    "{8} polylines, {9} dropped; 1.0.3 strips {10}", name, cx, cz, radius, busy.x, busy.y, c.Fill.Count, c.Curves.Count, c.Polylines,
                    c.LoopsDropped, rects.Count));
                foreach (View v in views)
                    foreach (Look look in Looks())
                    {
                        var cv = new Canvas(v.Px, v.Cx, v.Cz, v.SizeM);
                        cv.Base((x, z) => t.Sample((float)x, (float)z), v.Snow);
                        Paint(cv, look, cells, c, v.CamM);
                        cv.Save(Path.Combine(dir, name + "-" + v.Id + "-" + look.Id + ".png"));
                    }
            }
            HeatmapPreview(dir, log);
            File.WriteAllLines(Path.Combine(dir, "preview.txt"), log);
            foreach (string l in log)
                Console.WriteLine("PREVIEW  " + l);
            return 0;
        }

        private static double2 Busiest(SkiSlopeContours c, double cx, double cz, double within)
        {
            var count = new Dictionary<long, int>();
            long best = 0;
            int bestN = -1;
            foreach (SkiSlopeContours.Curve k in c.Curves)
            {
                double mx = 0.5 * (k.P0.x + k.P3.x), mz = 0.5 * (k.P0.z + k.P3.z);
                if ((mx - cx) * (mx - cx) + (mz - cz) * (mz - cz) > within * within)
                    continue;
                long key = ((long)Math.Floor(mx / 40) << 32) ^ (uint)(int)Math.Floor(mz / 40);
                count.TryGetValue(key, out int n);
                count[key] = ++n;
                if (n > bestN)
                {
                    bestN = n;
                    best = key;
                }
            }
            if (bestN < 0)
                return new double2(cx, cz);
            int ki = (int)(best >> 32), kj = (int)(uint)best;
            return new double2((ki + 0.5) * 40, (kj + 0.5) * 40);
        }

        private static void HeatmapPreview(string dir, List<string> log)
        {
            string proj = ProjectDir();
            string file = proj == null ? null : Path.Combine(proj, "captures", "run-20261001-093404.skislope");
            if (file == null || !File.Exists(file) || !TryLoadCapture(file, out Capture cap, out string err))
            {
                log.Add("heat-map: capture run-20261001-093404 not found, skipped");
                return;
            }
            var g = new SlopeGrid();
            var res = new SlopeResult();
            ReplayCapture(cap, g, res, out int missing, out SlopeStatus _, "preview");
            int nx = g.Nx, nz = g.Nz, n = nx * nz;
            float[] o = new float[n], h = new float[n];
            double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
            double ox = g.Origin.x + g.I0 * g.Step, oz = g.Origin.y + g.J0 * g.Step;
            var cells = new List<PRect>();
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int q = j * nx + i;
                    h[q] = g.Need[q] != 0 ? g.Heights[q] : float.NaN;
                    bool inside = g.Mask[q] != 0 && res.CellBand[q] <= 4;
                    o[q] = inside ? res.CellGrade[q] : float.NaN;
                    if (!inside)
                        continue;
                    double x = ox + i * g.Step, z = oz + j * g.Step;
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minZ = Math.Min(minZ, z);
                    maxZ = Math.Max(maxZ, z);
                    byte key = DisplayUnits.StripKey(res.CellBand[q], res.CellGrade[q], false);
                    cells.Add(new PRect { X0 = x - 0.5 * g.Step, X1 = x + 0.5 * g.Step, Z0 = z - 0.5 * g.Step, Z1 = z + 0.5 * g.Step, Key = key, Fade = 4 });
                }
            var c = new SkiSlopeContours();
            var ring = new double2[g.PointCount];
            for (int k = 0; k < g.PointCount; k++)
                ring[k] = new double2(g.Points[k].x, g.Points[k].z);
            c.ClipRing = ring;
            c.ClipCount = ring.Length;
            c.Build(o, h, nx, nz, ox, oz, g.Step, false);
            double hx = 0.5 * (minX + maxX), hz = 0.5 * (minZ + maxZ), size = Math.Max(maxX - minX, maxZ - minZ) + 30;
            double2 busy = Busiest(c, hx, hz, size);
            log.Add(string.Format(CultureInfo.InvariantCulture, "heat-map {0}: {1}x{2} cells (missing heights {3}), {4} m box; fill {5} strips, {6} curves, " +
                "{7} polylines, {8} dropped; 1.0.3 cells {9}; crop at ({10:F0}, {11:F0})", cap.Name, nx, nz, missing, size, c.Fill.Count, c.Curves.Count,
                c.Polylines, c.LoopsDropped, cells.Count, busy.x, busy.y));
            float CapHeight(double x, double z)
            {
                // Shaded inside the run only: the capture holds heights near the polygon only, and their ragged edge is not the overlay.
                bool inside = false;
                for (int k = 0, m = ring.Length - 1; k < ring.Length; m = k++)
                    if ((ring[k].y > z) != (ring[m].y > z) && x < (ring[m].x - ring[k].x) * (z - ring[k].y) / (ring[m].y - ring[k].y) + ring[k].x)
                        inside = !inside;
                if (!inside)
                    return float.NaN;
                double fi = (x - ox) / g.Step, fj = (z - oz) / g.Step;
                int i = (int)Math.Floor(fi), j = (int)Math.Floor(fj);
                if (i < 0 || j < 0 || i + 1 >= nx || j + 1 >= nz)
                    return float.NaN;
                double tx = fi - i, tz = fj - j;
                return (float)((h[j * nx + i] * (1 - tx) + h[j * nx + i + 1] * tx) * (1 - tz) + (h[(j + 1) * nx + i] * (1 - tx) + h[(j + 1) * nx + i + 1] * tx) * tz);
            }
            View[] views =
            {
                new View { Id = "whole", Cx = hx, Cz = hz, SizeM = size, Px = 800, CamM = 1100 },
                new View { Id = "z450", Cx = busy.x, Cz = busy.y, SizeM = 200, Px = 760, CamM = 450 },
            };
            foreach (View v in views)
                foreach (Look look in Looks())
                {
                    var cv = new Canvas(v.Px, v.Cx, v.Cz, v.SizeM);
                    cv.Base(CapHeight, false);
                    Paint(cv, look, cells, c, v.CamM);
                    cv.Save(Path.Combine(dir, "heatmap-" + v.Id + "-" + look.Id + ".png"));
                }
        }

        // ---------------------------------------------------------------- PNG (RGB8, zlib)
        private static uint[] s_Crc;

        private static void WritePng(string path, int w, int h, byte[] rgb)
        {
            using (FileStream fs = File.Create(path))
            {
                fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
                byte[] ihdr = new byte[13];
                Be(ihdr, 0, (uint)w);
                Be(ihdr, 4, (uint)h);
                ihdr[8] = 8;
                ihdr[9] = 2;
                Chunk(fs, "IHDR", ihdr);
                byte[] raw = new byte[h * (w * 3 + 1)];
                for (int r = 0; r < h; r++)
                    Buffer.BlockCopy(rgb, r * w * 3, raw, r * (w * 3 + 1) + 1, w * 3);
                using (var ms = new MemoryStream())
                {
                    using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true))
                        z.Write(raw, 0, raw.Length);
                    Chunk(fs, "IDAT", ms.ToArray());
                }
                Chunk(fs, "IEND", new byte[0]);
            }
        }

        private static void Be(byte[] b, int at, uint v)
        {
            b[at] = (byte)(v >> 24);
            b[at + 1] = (byte)(v >> 16);
            b[at + 2] = (byte)(v >> 8);
            b[at + 3] = (byte)v;
        }

        private static void Chunk(Stream s, string type, byte[] data)
        {
            if (s_Crc == null)
            {
                s_Crc = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++)
                        c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    s_Crc[n] = c;
                }
            }
            byte[] len = new byte[4], tb = System.Text.Encoding.ASCII.GetBytes(type), crcB = new byte[4];
            Be(len, 0, (uint)data.Length);
            s.Write(len, 0, 4);
            s.Write(tb, 0, 4);
            s.Write(data, 0, data.Length);
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in tb)
                crc = s_Crc[(crc ^ b) & 0xFF] ^ (crc >> 8);
            foreach (byte b in data)
                crc = s_Crc[(crc ^ b) & 0xFF] ^ (crc >> 8);
            Be(crcB, 0, crc ^ 0xFFFFFFFFu);
            s.Write(crcB, 0, 4);
        }
    }
}
